using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Collections.Generic;

public class MainCameraReferenceCaptureWindow : EditorWindow
{
    // ====== 設定項目 ======
    public string outputDir = "CapturedPNGs";
    public int width = 1024;
    public int height = 1024;
    public int msaa = 4;
    public bool useMainCameraSettings = true;  // Main Cameraの設定を使用
    public LayerMask captureLayer = -1;
    public Color backgroundColor = new Color(0,0,0,0);

    // ====== ライティング ======
    public bool addDirectionalLight = true;
    public float lightIntensity = 1.2f;
    public Vector3 lightEuler = new Vector3(30, 135, 0);

    // ====== 4方向設定 ======
    public string[] directionLabels = {"front", "right", "back", "left"};
    public float[] yawOffsets = {0f, 90f, 180f, 270f}; // Y軸回転オフセット

    // ====== アセット干渉回避 ======
    public Vector3 capturePosition = new Vector3(1000, 0, 1000); // 撮影用の隔離位置

    // スクロール位置
    private Vector2 scrollPosition = Vector2.zero;

    [MenuItem("Tools/Capture/Main Camera Reference Capture")]
    public static void Open() => GetWindow<MainCameraReferenceCaptureWindow>("Main Camera Reference Capture");

    void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        EditorGUILayout.LabelField("Main Camera参照 4方向撮影", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Main Cameraの位置・回転を参照してPng_folder配下のアセットを4方向撮影します", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("出力設定", EditorStyles.boldLabel);
        outputDir = EditorGUILayout.TextField("Output Folder", outputDir);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("撮影設定", EditorStyles.boldLabel);
        width = EditorGUILayout.IntField("Width (px)", width);
        height = EditorGUILayout.IntField("Height (px)", height);
        msaa = EditorGUILayout.IntPopup("MSAA", msaa, new[] { "1","2","4","8" }, new[] { 1,2,4,8 });

        useMainCameraSettings = EditorGUILayout.Toggle(new GUIContent("Use Main Camera Settings", "Main Cameraのorthographic/FOV設定を使用"), useMainCameraSettings);
        captureLayer = LayerMaskField("Capture Layers", captureLayer);
        backgroundColor = EditorGUILayout.ColorField("Background Color", backgroundColor);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("干渉回避設定", EditorStyles.boldLabel);
        capturePosition = EditorGUILayout.Vector3Field(new GUIContent("Capture Position", "撮影時の隔離位置"), capturePosition);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("ライティング", EditorStyles.boldLabel);
        addDirectionalLight = EditorGUILayout.Toggle("Add Directional Light", addDirectionalLight);
        if (addDirectionalLight)
        {
            lightIntensity = EditorGUILayout.Slider("Light Intensity", lightIntensity, 0f, 3f);
            lightEuler = EditorGUILayout.Vector3Field("Light Rotation", lightEuler);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("4方向設定", EditorStyles.boldLabel);
        for (int i = 0; i < 4; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                directionLabels[i] = EditorGUILayout.TextField($"Direction {i+1}", directionLabels[i], GUILayout.Width(200));
                yawOffsets[i] = EditorGUILayout.FloatField("Yaw Offset", yawOffsets[i], GUILayout.Width(100));
            }
        }

        EditorGUILayout.Space();

        // Main Camera存在チェック
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            EditorGUILayout.HelpBox("Main Cameraが見つかりません。シーン内にTagが'MainCamera'のCameraを配置してください。", MessageType.Error);
        }
        else
        {
            EditorGUILayout.HelpBox($"参照カメラ: {mainCam.name}\n位置: {mainCam.transform.position}\n回転: {mainCam.transform.rotation.eulerAngles}", MessageType.Info);
        }

        EditorGUI.BeginDisabledGroup(mainCam == null);
        if (GUILayout.Button("実行"))
        {
            Run();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndScrollView();
    }

    static LayerMask LayerMaskField(string label, LayerMask selected)
    {
        var layers = Enumerable.Range(0, 32).Select(i => LayerMask.LayerToName(i)).ToArray();
        int mask = EditorGUILayout.MaskField(label, selected.value, layers);
        return mask;
    }

    void Run()
    {
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            EditorUtility.DisplayDialog("Error", "Main Cameraが見つかりません。", "OK");
            return;
        }

        // Png_folder配下のアセット取得
        var targets = CollectPngFolderAssets();
        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "Assets/Png_folder配下にPrefabが見つかりません。", "OK");
            return;
        }

        string absOut = Path.Combine(Directory.GetCurrentDirectory(), outputDir);
        Directory.CreateDirectory(absOut);

        // Main Cameraの現在設定を保存
        Vector3 originalCamPos = mainCam.transform.position;
        Quaternion originalCamRot = mainCam.transform.rotation;
        RenderTexture originalTarget = mainCam.targetTexture;

        // キャプチャ用RenderTexture作成
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = Mathf.Max(1, msaa);

        // Main Cameraの設定適用
        if (useMainCameraSettings)
        {
            // 既存の設定を使用（特に変更なし）
        }
        mainCam.targetTexture = rt;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = backgroundColor;
        mainCam.cullingMask = captureLayer.value;

        // ライト設定
        Light dirLight = null;
        GameObject lightGO = null;
        if (addDirectionalLight)
        {
            lightGO = new GameObject("~TempCaptureLight");
            dirLight = lightGO.AddComponent<Light>();
            dirLight.type = LightType.Directional;
            dirLight.intensity = lightIntensity;
            dirLight.transform.rotation = Quaternion.Euler(lightEuler);
        }

        int count = 0;
        try
        {
            foreach (var prefab in targets)
            {
                EditorUtility.DisplayProgressBar("Capturing", prefab.name, (float)count / targets.Count);

                // アセットを隔離位置にInstantiate
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.name = "~CaptureTarget";
                inst.transform.position = capturePosition;
                SetLayerRecursively(inst, FirstLayerFromMask(captureLayer));

                // アセットのBounds取得
                var renderers = inst.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    Debug.LogWarning($"Renderer が見つかりません: {prefab.name}");
                    DestroyImmediate(inst);
                    continue;
                }
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);

                // 4方向撮影
                for (int dir = 0; dir < 4; dir++)
                {
                    // Main Cameraを基準位置からY軸回転
                    float yawOffset = yawOffsets[dir];
                    Quaternion rotationOffset = Quaternion.Euler(0, yawOffset, 0);

                    // Main Cameraの位置を撮影対象の中心を基準に調整
                    Vector3 offsetFromOriginal = originalCamPos - Vector3.zero; // 元のMain Camera位置のオフセット
                    Vector3 rotatedOffset = rotationOffset * offsetFromOriginal;
                    Vector3 newCamPos = bounds.center + rotatedOffset;

                    mainCam.transform.position = newCamPos;
                    mainCam.transform.rotation = originalCamRot * rotationOffset;

                    // 撮影対象を見るように調整
                    mainCam.transform.LookAt(bounds.center, Vector3.up);

                    // レンダリング
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    mainCam.Render();

                    var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                    tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    tex.Apply();

                    // ファイル保存
                    string safeName = MakeSafeFileName(prefab.name);
                    string fileName = $"{safeName}_{directionLabels[dir]}.png";
                    string filePath = Path.Combine(absOut, fileName);
                    File.WriteAllBytes(filePath, tex.EncodeToPNG());

                    Object.DestroyImmediate(tex);
                    RenderTexture.active = prev;
                }

                DestroyImmediate(inst);
                count++;
            }
        }
        finally
        {
            // 後処理
            if (rt != null) rt.Release();
            if (lightGO != null) DestroyImmediate(lightGO);

            // Main Cameraを元に戻す
            mainCam.transform.position = originalCamPos;
            mainCam.transform.rotation = originalCamRot;
            mainCam.targetTexture = originalTarget;

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            Debug.Log($"Main Camera Reference Capture finished. Files: {count * 4}, Output: {absOut}");
        }
    }

    static List<GameObject> CollectPngFolderAssets()
    {
        var list = new List<GameObject>();
        string folderPath = "Assets/Png_folder";

        if (AssetDatabase.IsValidFolder(folderPath))
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go) list.Add(go);
            }
        }

        return list.Distinct().ToList();
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (layer < 0) return;
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
    }

    static int FirstLayerFromMask(LayerMask mask)
    {
        if (mask.value <= 0) return -1;
        for (int i = 0; i < 32; i++)
            if ((mask.value & (1 << i)) != 0) return i;
        return -1;
    }

    static string MakeSafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}