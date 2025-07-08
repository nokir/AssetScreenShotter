using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class AssetScreenshotterEditor : EditorWindow
{
    // --- Enums for settings ---
    private enum Language { English, Japanese }
    private enum MultiObjectMode { Individual, Group }
    private enum CaptureAngleMode { Normal, Diagonal, NormalAndDiagonal }

    // --- Settings ---
    private string saveFolderPath = "";
    private MultiObjectMode mode = MultiObjectMode.Individual;
    private CaptureAngleMode captureAngleMode = CaptureAngleMode.Normal;
    private Vector2Int resolution = new Vector2Int(1024, 1024);
    private float zoomFactor = 1.1f;
    private Language currentLanguage = Language.English;
    private Vector3 captureOffset = Vector3.zero;
    private bool openFolderAfterCapture = true;

    // --- Angle States ---
    private Dictionary<string, bool> angleEnabledStates = new Dictionary<string, bool>();
    private int previewIndex = -1;

    // --- Preview State ---
    private bool isPreviewing = false;
    private Vector3 originalPivot;
    private Quaternion originalRotation;
    private float originalSize;
    private bool originalOrthographic;

    // --- EditorPrefs Keys ---
    private const string SavePathKey = "AssetScreenshotter_SavePath";
    private const string ModeKey = "AssetScreenshotter_Mode";
    private const string AngleModeKey = "AssetScreenshotter_AngleMode";
    private const string ResolutionXKey = "AssetScreenshotter_ResolutionX";
    private const string ResolutionYKey = "AssetScreenshotter_ResolutionY";
    private const string ZoomKey = "AssetScreenshotter_ZoomFactor";
    private const string LanguageKey = "AssetScreenshotter_Language";
    private const string PositionOffsetKey = "AssetScreenshotter_PositionOffset";
    private const string OpenFolderKey = "AssetScreenshotter_OpenFolder";
    private const string AngleStatesKey = "AssetScreenshotter_AngleStates";

    // --- Localization Dictionary ---
    private Dictionary<string, Dictionary<Language, string>> localization;

    [MenuItem("Tools/N/Asset Screenshotter")]
    public static void ShowWindow()
    {
        GetWindow<AssetScreenshotterEditor>("Asset Screenshotter");
    }

    private void OnEnable()
    {
        // Load settings from EditorPrefs
        saveFolderPath = EditorPrefs.GetString(SavePathKey, Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyPictures), "AssetScreenshots"));
        mode = (MultiObjectMode)EditorPrefs.GetInt(ModeKey, (int)MultiObjectMode.Individual);
        captureAngleMode = (CaptureAngleMode)EditorPrefs.GetInt(AngleModeKey, (int)CaptureAngleMode.Normal);
        resolution.x = EditorPrefs.GetInt(ResolutionXKey, 1024);
        resolution.y = EditorPrefs.GetInt(ResolutionYKey, 1024);
        zoomFactor = EditorPrefs.GetFloat(ZoomKey, 1.1f);
        currentLanguage = (Language)EditorPrefs.GetInt(LanguageKey, (int)Language.English);
        captureOffset = StringToVector3(EditorPrefs.GetString(PositionOffsetKey, "0,0,0"));
        openFolderAfterCapture = EditorPrefs.GetBool(OpenFolderKey, true);

        LoadAngleStates();

        InitializeLocalization();
    }

    private void OnGUI()
    {
        // Language Selection
        EditorGUI.BeginChangeCheck();
        currentLanguage = (Language)EditorGUILayout.EnumPopup(GetText("Language"), currentLanguage);
        if (EditorGUI.EndChangeCheck())
        {
            SaveSettings(); // Save language immediately on change
        }
        EditorGUILayout.Space();

        GUILayout.Label(GetText("SettingsTitle"), EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // 1. Save Folder Path
        EditorGUILayout.LabelField(GetText("SaveFolderPath"), EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        saveFolderPath = EditorGUILayout.TextField(saveFolderPath);
        if (GUILayout.Button(GetText("Browse"), GUILayout.Width(80)))
        {
            string initialPath = string.IsNullOrEmpty(saveFolderPath) ? Application.dataPath : saveFolderPath;
            string path = EditorUtility.OpenFolderPanel(GetText("SelectSaveFolder"), initialPath, "");
            if (!string.IsNullOrEmpty(path))
            {
                saveFolderPath = path;
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        // 2. Multi-Object Mode
        EditorGUILayout.LabelField(GetText("MultiObjectMode"), EditorStyles.miniBoldLabel);
        mode = (MultiObjectMode)EditorGUILayout.EnumPopup(GetText("CaptureMode"), mode);
        EditorGUILayout.Space();

        // 3. Capture Angle Mode
        EditorGUILayout.LabelField(GetText("CaptureAngleMode"), EditorStyles.miniBoldLabel);
        EditorGUI.BeginChangeCheck();
        captureAngleMode = (CaptureAngleMode)EditorGUILayout.EnumPopup(GetText("AngleMode"), captureAngleMode);
        if (EditorGUI.EndChangeCheck())
        {
            // Reset preview index when angle mode changes
            previewIndex = -1;
        }
        EditorGUILayout.Space();

        // Angle Toggles based on selected mode
        if (captureAngleMode == CaptureAngleMode.Normal || captureAngleMode == CaptureAngleMode.NormalAndDiagonal)
        {
            GUILayout.Label(GetText("NormalAngles"), EditorStyles.miniBoldLabel);
            DrawAngleToggles(GetNormalDirections());
        }
        if (captureAngleMode == CaptureAngleMode.Diagonal || captureAngleMode == CaptureAngleMode.NormalAndDiagonal)
        {
            GUILayout.Label(GetText("DiagonalAngles"), EditorStyles.miniBoldLabel);
            DrawAngleToggles(GetDiagonalDirections());
        }
        EditorGUILayout.Space();

        // 4. Resolution
        EditorGUILayout.LabelField(GetText("OutputResolution"), EditorStyles.miniBoldLabel);
        resolution = EditorGUILayout.Vector2IntField("", resolution);
        EditorGUILayout.Space();

        // 5. Zoom Factor
        EditorGUILayout.LabelField(GetText("ZoomFactor"), EditorStyles.miniBoldLabel);
        zoomFactor = EditorGUILayout.Slider(zoomFactor, 0.5f, 5f);
        EditorGUILayout.Space();

        // 6. Position Offset
        EditorGUILayout.LabelField(GetText("PositionOffset"), EditorStyles.miniBoldLabel);
        captureOffset = EditorGUILayout.Vector3Field("", captureOffset);
        EditorGUILayout.Space();

        // 7. Open Folder After Capture
        openFolderAfterCapture = EditorGUILayout.Toggle(GetText("OpenFolderToggle"), openFolderAfterCapture);
        EditorGUILayout.Space(20);

        // --- Preview Buttons ---
        EditorGUILayout.BeginHorizontal();
        // Preview Button
        if (GUILayout.Button(GetText("PreviewButton")))
        {
            if (Selection.gameObjects.Length > 0)
            {
                PreviewNextAngle();
            }
            else
            {
                EditorUtility.DisplayDialog("Error", GetText("ErrorNoSelection"), "OK");
            }
        }

        // Reset View Button
        GUI.enabled = isPreviewing; // Enable button only when previewing
        if (GUILayout.Button(GetText("ResetViewButton")))
        {
            ResetSceneView();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        // Capture Button
        if (GUILayout.Button(GetText("CaptureButton"), GUILayout.Height(40)))
        {
            if (Selection.gameObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("Error", GetText("ErrorNoSelection"), "OK");
                return;
            }
            
            SaveSettings();
            ProcessCapture();
        }
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString(SavePathKey, saveFolderPath);
        EditorPrefs.SetInt(ModeKey, (int)mode);
        EditorPrefs.SetInt(AngleModeKey, (int)captureAngleMode);
        EditorPrefs.SetInt(ResolutionXKey, resolution.x);
        EditorPrefs.SetInt(ResolutionYKey, resolution.y);
        EditorPrefs.SetFloat(ZoomKey, zoomFactor);
        EditorPrefs.SetInt(LanguageKey, (int)currentLanguage);
        EditorPrefs.SetString(PositionOffsetKey, Vector3ToString(captureOffset));
        EditorPrefs.SetBool(OpenFolderKey, openFolderAfterCapture);
        SaveAngleStates();
    }

    private void ProcessCapture()
    {
        if (!Directory.Exists(saveFolderPath))
        {
            try { Directory.CreateDirectory(saveFolderPath); }
            catch (System.Exception e) { EditorUtility.DisplayDialog("Error", GetText("ErrorCreateDirectory") + "\n" + e.Message, "OK"); return; }
        }

        GameObject[] selectedObjects = Selection.gameObjects;

        if (mode == MultiObjectMode.Individual || selectedObjects.Length == 1)
        {
            for (int i = 0; i < selectedObjects.Length; i++)
            {
                GameObject obj = selectedObjects[i];
                string progressTitle = GetText("ProgressCapturing");
                string progressInfo = $"{GetText("ProgressProcessing")} {obj.name} ({i + 1}/{selectedObjects.Length})";
                EditorUtility.DisplayProgressBar(progressTitle, progressInfo, (float)i / selectedObjects.Length);
                TakeSixAxisShots(new GameObject[] { obj }, obj.name);
            }
        }
        else // Group mode
        {
            EditorUtility.DisplayProgressBar(GetText("ProgressCapturing"), GetText("ProgressProcessingGroup"), 0.5f);
            string groupName = selectedObjects[0].name + "_Group";
            TakeSixAxisShots(selectedObjects, groupName);
        }
        
        EditorUtility.ClearProgressBar();
        EditorUtility.DisplayDialog(GetText("SuccessTitle"), GetText("SuccessMessage") + "\n" + saveFolderPath, "OK");
        if (openFolderAfterCapture)
        {
            EditorUtility.RevealInFinder(saveFolderPath);
        }
    }

    private void TakeSixAxisShots(GameObject[] targets, string baseFileName)
    {
        GameObject camObj = new GameObject("ScreenshotCamera");
        Camera cam = camObj.AddComponent<Camera>();
        
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.orthographic = true;
        cam.aspect = (float)resolution.x / resolution.y;

        // --- Isolate Selection Logic (same as before) ---
        var objectsToKeep = new HashSet<GameObject>();
        objectsToKeep.Add(camObj);
        foreach (var target in targets)
        {
            objectsToKeep.Add(target);
            var parent = target.transform.parent;
            while (parent != null)
            {
                objectsToKeep.Add(parent.gameObject);
                parent = parent.parent;
            }
            foreach (var descendant in target.GetComponentsInChildren<Transform>(true))
            {
                objectsToKeep.Add(descendant.gameObject);
            }
        }
        var allLights = UnityEngine.Object.FindObjectsOfType<Light>();
        foreach (var light in allLights)
        {
            if (objectsToKeep.Contains(light.gameObject)) continue;
            objectsToKeep.Add(light.gameObject);
            var parent = light.transform.parent;
            while (parent != null)
            {
                objectsToKeep.Add(parent.gameObject);
                parent = parent.parent;
            }
        }
        var allGameObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
        List<GameObject> hiddenObjects = new List<GameObject>();
        foreach (var go in allGameObjects)
        {
            if (go == null) continue;
            if (!objectsToKeep.Contains(go))
            {
                if (go.activeSelf)
                {
                    hiddenObjects.Add(go);
                    go.SetActive(false);
                }
            }
        }
        // --- End Isolate Selection Logic ---

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmssfff");

        try
        {
            Bounds bounds = CalculateBounds(targets);
            Vector3 targetCenter = bounds.center + captureOffset;

            var directions = GetDirectionsForMode(captureAngleMode);

            foreach (var dir in directions)
            {
                // Only process if the angle is enabled
                if (!IsAngleEnabled(dir.Key)) continue;

                Vector3 directionVector = dir.Value.normalized;
                Vector3 cameraPosition = targetCenter + directionVector * bounds.size.magnitude * 1.5f;

                Vector3 upVector = Vector3.up;
                if (captureAngleMode == CaptureAngleMode.Normal && (dir.Value == Vector3.up || dir.Value == Vector3.down))
                {
                    upVector = Vector3.forward;
                }

                cam.transform.position = cameraPosition;
                cam.transform.LookAt(targetCenter, upVector);

                float requiredSizeV = bounds.size.y / 2f;
                float requiredSizeH = (bounds.size.x / cam.aspect) / 2f;
                cam.orthographicSize = Mathf.Max(requiredSizeV, requiredSizeH) / zoomFactor;
                if (cam.orthographicSize <= 0) cam.orthographicSize = 0.1f;

                CaptureView(cam, $"{baseFileName}_{timestamp}{dir.Key}.png");
            }
        }
        finally
        {
            foreach (var obj in hiddenObjects) { if(obj != null) obj.SetActive(true); }
            DestroyImmediate(camObj);
        }
    }

    private void CaptureView(Camera cam, string fileName)
    {
        RenderTexture rt = new RenderTexture(resolution.x, resolution.y, 24);
        cam.targetTexture = rt;
        
        Texture2D screenShot = new Texture2D(resolution.x, resolution.y, TextureFormat.ARGB32, false);
        cam.Render();

        RenderTexture.active = rt;
        screenShot.ReadPixels(new Rect(0, 0, resolution.x, resolution.y), 0, 0);
        screenShot.Apply();

        cam.targetTexture = null;
        RenderTexture.active = null;
        DestroyImmediate(rt);

        byte[] bytes = screenShot.EncodeToPNG();
        string safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
        string filePath = Path.Combine(saveFolderPath, safeFileName);
        
        File.WriteAllBytes(filePath, bytes);
        DestroyImmediate(screenShot);
    }

    private void PreviewNextAngle()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            EditorUtility.DisplayDialog("Error", GetText("ErrorNoSceneView"), "OK");
            return;
        }

        if (!isPreviewing)
        {
            originalPivot = sceneView.pivot;
            originalRotation = sceneView.rotation;
            originalSize = sceneView.size;
            originalOrthographic = sceneView.orthographic;
            isPreviewing = true;
        }

        GameObject[] selectedObjects = Selection.gameObjects;
        Bounds bounds = CalculateBounds(selectedObjects);
        Vector3 targetCenter = bounds.center + captureOffset;

        var directions = GetDirectionsForMode(captureAngleMode);

        // Find the next enabled angle
        int startIndex = (previewIndex == -1) ? 0 : (previewIndex + 1) % directions.Count;
        int count = 0;
        do
        {
            previewIndex = (startIndex + count) % directions.Count;
            if (IsAngleEnabled(directions[previewIndex].Key))
            {
                break; // Found an enabled angle
            }
            count++;
        } while (count < directions.Count);

        // If no angle is enabled, display an error and return
        if (!IsAngleEnabled(directions[previewIndex].Key))
        {
            EditorUtility.DisplayDialog("Error", GetText("ErrorNoAngleEnabled"), "OK");
            return;
        }

        var dir = directions[previewIndex];

        Vector3 directionVector = dir.Value.normalized;
        Vector3 cameraPosition = targetCenter + directionVector * bounds.size.magnitude * 1.5f;

        Vector3 upVector = Vector3.up;
        if (captureAngleMode == CaptureAngleMode.Normal && (dir.Value == Vector3.up || dir.Value == Vector3.down))
        {
            upVector = Vector3.forward;
        }

        float aspect = sceneView.camera.aspect;
        float requiredSizeV = bounds.size.y / 2f;
        float requiredSizeH = (bounds.size.x / aspect) / 2f;
        float orthoSize = Mathf.Max(requiredSizeV, requiredSizeH) / zoomFactor;
        if (orthoSize <= 0) orthoSize = 0.1f;

        sceneView.orthographic = true;
        sceneView.size = orthoSize;
        sceneView.LookAt(targetCenter, Quaternion.LookRotation(targetCenter - cameraPosition, upVector));
        sceneView.pivot = cameraPosition;
        sceneView.Repaint();

        sceneView.ShowNotification(new GUIContent($"{GetText("PreviewingAngle")} {dir.Key}"), 1.5f);
    }

    private void ResetSceneView()
    {
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null && isPreviewing)
        {
            sceneView.LookAt(originalPivot, originalRotation, originalSize, originalOrthographic, true);
            sceneView.Repaint();
            isPreviewing = false;
            previewIndex = -1; // Reset index
            sceneView.ShowNotification(new GUIContent(GetText("ViewResetMessage")), 1.5f);
        }
    }

    private List<KeyValuePair<string, Vector3>> GetDirectionsForMode(CaptureAngleMode angleMode)
    {
        var directions = new List<KeyValuePair<string, Vector3>>();
        if (angleMode == CaptureAngleMode.Normal)
        {
            directions.AddRange(GetNormalDirections());
        }
        else if (angleMode == CaptureAngleMode.Diagonal)
        {
            directions.AddRange(GetDiagonalDirections());
        }
        else if (angleMode == CaptureAngleMode.NormalAndDiagonal)
        {
            directions.AddRange(GetNormalDirections());
            directions.AddRange(GetDiagonalDirections());
        }
        return directions;
    }

    private List<KeyValuePair<string, Vector3>> GetNormalDirections()
    {
        return new List<KeyValuePair<string, Vector3>>
        {
            new KeyValuePair<string, Vector3>("_Front", Vector3.forward),
            new KeyValuePair<string, Vector3>("_Back", Vector3.back),
            new KeyValuePair<string, Vector3>("_Right", Vector3.right),
            new KeyValuePair<string, Vector3>("_Left", Vector3.left),
            new KeyValuePair<string, Vector3>("_Up", Vector3.up),
            new KeyValuePair<string, Vector3>("_Down", Vector3.down)
        };
    }

    private List<KeyValuePair<string, Vector3>> GetDiagonalDirections()
    {
        return new List<KeyValuePair<string, Vector3>>
        {
            new KeyValuePair<string, Vector3>("_Front_Right_Up", new Vector3(1, 1, 1)),
            new KeyValuePair<string, Vector3>("_Front_Left_Up", new Vector3(-1, 1, 1)),
            new KeyValuePair<string, Vector3>("_Back_Right_Up", new Vector3(1, 1, -1)),
            new KeyValuePair<string, Vector3>("_Back_Left_Up", new Vector3(-1, 1, -1)),
            new KeyValuePair<string, Vector3>("_Front_Right_Down", new Vector3(1, -1, 1)),
            new KeyValuePair<string, Vector3>("_Front_Left_Down", new Vector3(-1, -1, 1)),
            new KeyValuePair<string, Vector3>("_Back_Right_Down", new Vector3(1, -1, -1)),
            new KeyValuePair<string, Vector3>("_Back_Left_Down", new Vector3(-1, -1, -1))
        };
    }

    private Bounds CalculateBounds(GameObject[] targets)
    {
        if (targets.Length == 0) return new Bounds();
        Bounds totalBounds = new Bounds();
        bool first = true;
        foreach (var target in targets)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers)
            {
                if (first) { totalBounds = renderer.bounds; first = false; }
                else { totalBounds.Encapsulate(renderer.bounds); }
            }
        }
        return totalBounds;
    }

    // --- Angle State Management ---
    private void LoadAngleStates()
    {
        angleEnabledStates.Clear();
        string json = EditorPrefs.GetString(AngleStatesKey, "");
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                // Deserialize the JSON string into a temporary dictionary
                var tempDict = JsonUtility.FromJson<SerializableDictionary>(json).ToDictionary();
                foreach (var kvp in tempDict)
                {
                    angleEnabledStates[kvp.Key] = kvp.Value;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to load angle states: {e.Message}");
            }
        }

        // Initialize any missing angles to true
        InitializeAngleStates();
    }

    private void SaveAngleStates()
    {
        // Serialize the dictionary to JSON
        string json = JsonUtility.ToJson(new SerializableDictionary(angleEnabledStates));
        EditorPrefs.SetString(AngleStatesKey, json);
    }

    private void InitializeAngleStates()
    {
        // Ensure all possible angles have an entry, defaulting to true
        foreach (var dir in GetNormalDirections())
        {
            if (!angleEnabledStates.ContainsKey(dir.Key))
            {
                angleEnabledStates[dir.Key] = true;
            }
        }
        foreach (var dir in GetDiagonalDirections())
        {
            if (!angleEnabledStates.ContainsKey(dir.Key))
            {
                angleEnabledStates[dir.Key] = true;
            }
        }
    }

    private bool IsAngleEnabled(string angleKey)
    {
        return angleEnabledStates.ContainsKey(angleKey) ? angleEnabledStates[angleKey] : true; // Default to true if not found
    }

    private void DrawAngleToggles(List<KeyValuePair<string, Vector3>> directions)
    {
        EditorGUILayout.BeginVertical("box");
        int togglesPerRow = 2; // Number of toggles to display per row
        for (int i = 0; i < directions.Count; i += togglesPerRow)
        {
            EditorGUILayout.BeginHorizontal();
            for (int j = 0; j < togglesPerRow; j++)
            {
                int currentIndex = i + j;
                if (currentIndex < directions.Count)
                {
                    var dir = directions[currentIndex];
                    bool currentState = IsAngleEnabled(dir.Key);
                    bool newState = EditorGUILayout.ToggleLeft(GetText(dir.Key.Replace("_", "")), currentState, GUILayout.ExpandWidth(true));
                    if (newState != currentState)
                    {
                        angleEnabledStates[dir.Key] = newState;
                        SaveAngleStates();
                    }
                }
                else
                {
                    GUILayout.FlexibleSpace(); // Fill remaining space if odd number of toggles
                }
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();
    }

    // Helper for JSON serialization of Dictionary
    [System.Serializable]
    private class SerializableDictionary
    {
        public List<string> keys = new List<string>();
        public List<bool> values = new List<bool>();

        public SerializableDictionary(Dictionary<string, bool> dict)
        {
            foreach (var kvp in dict)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
        }

        public Dictionary<string, bool> ToDictionary()
        {
            var dict = new Dictionary<string, bool>();
            for (int i = 0; i < keys.Count; i++)
            {
                dict[keys[i]] = values[i];
            }
            return dict;
        }
    }

    // --- Helpers ---
    private string Vector3ToString(Vector3 v) => $"{v.x},{v.y},{v.z}";

    private Vector3 StringToVector3(string s)
    {
        string[] parts = s.Split(',');
        if (parts.Length != 3) return Vector3.zero;
        float.TryParse(parts[0], out float x);
        float.TryParse(parts[1], out float y);
        float.TryParse(parts[2], out float z);
        return new Vector3(x, y, z);
    }

    // --- Localization ---
    private string GetText(string key)
    {
        if (localization.ContainsKey(key) && localization[key].ContainsKey(currentLanguage))
        {
            return localization[key][currentLanguage];
        }
        return key;
    }

    private void InitializeLocalization()
    {
        localization = new Dictionary<string, Dictionary<Language, string>>
        {
            { "Language", new Dictionary<Language, string> { { Language.English, "Language" }, { Language.Japanese, "言語" } } },
            { "SettingsTitle", new Dictionary<Language, string> { { Language.English, "Asset Screenshotter Settings" }, { Language.Japanese, "アセット撮影設定" } } },
            { "SaveFolderPath", new Dictionary<Language, string> { { Language.English, "Save Folder Path" }, { Language.Japanese, "保存先フォルダ" } } },
            { "Browse", new Dictionary<Language, string> { { Language.English, "Browse" }, { Language.Japanese, "参照" } } },
            { "SelectSaveFolder", new Dictionary<Language, string> { { Language.English, "Select Save Folder" }, { Language.Japanese, "保存先フォルダを選択" } } },
            { "MultiObjectMode", new Dictionary<Language, string> { { Language.English, "Multi-Object Mode" }, { Language.Japanese, "複数オブジェクトの撮影モード" } } },
            { "CaptureMode", new Dictionary<Language, string> { { Language.English, "Capture Mode" }, { Language.Japanese, "撮影モード" } } },
            { "CaptureAngleMode", new Dictionary<Language, string> { { Language.English, "Capture Angle Mode" }, { Language.Japanese, "撮影アングルモード" } } },
            { "AngleMode", new Dictionary<Language, string> { { Language.English, "Angle Mode" }, { Language.Japanese, "アングル" } } },
            { "OutputResolution", new Dictionary<Language, string> { { Language.English, "Output Resolution (X, Y)" }, { Language.Japanese, "出力解像度 (X, Y)" } } },
            { "ZoomFactor", new Dictionary<Language, string> { { Language.English, "Zoom Factor" }, { Language.Japanese, "拡大率" } } },
            { "PositionOffset", new Dictionary<Language, string> { { Language.English, "Position Offset" }, { Language.Japanese, "撮影位置オフセット" } } },
            { "OpenFolderToggle", new Dictionary<Language, string> { { Language.English, "Open folder after capture" }, { Language.Japanese, "撮影後にフォルダを開く" } } },
            { "NormalAngles", new Dictionary<Language, string> { { Language.English, "Normal Angles" }, { Language.Japanese, "通常アングル" } } },
            { "DiagonalAngles", new Dictionary<Language, string> { { Language.English, "Diagonal Angles" }, { Language.Japanese, "斜めアングル" } } },
            { "ErrorNoAngleEnabled", new Dictionary<Language, string> { { Language.English, "No angles are enabled for the current mode. Please enable at least one angle."}, { Language.Japanese, "現在選択されているモードで有効なアングルがありません。少なくとも1つのアングルを有効にしてください。"} } },
            { "_Front", new Dictionary<Language, string> { { Language.English, "Front" }, { Language.Japanese, "正面" } } },
            { "_Back", new Dictionary<Language, string> { { Language.English, "Back" }, { Language.Japanese, "背面" } } },
            { "_Right", new Dictionary<Language, string> { { Language.English, "Right" }, { Language.Japanese, "右" } } },
            { "_Left", new Dictionary<Language, string> { { Language.English, "Left" }, { Language.Japanese, "左" } } },
            { "_Up", new Dictionary<Language, string> { { Language.English, "Up" }, { Language.Japanese, "上" } } },
            { "_Down", new Dictionary<Language, string> { { Language.English, "Down" }, { Language.Japanese, "下" } } },
            { "_Front_Right_Up", new Dictionary<Language, string> { { Language.English, "Front Right Up" }, { Language.Japanese, "右上手前" } } },
            { "_Front_Left_Up", new Dictionary<Language, string> { { Language.English, "Front Left Up" }, { Language.Japanese, "左上手前" } } },
            { "_Back_Right_Up", new Dictionary<Language, string> { { Language.English, "Back Right Up" }, { Language.Japanese, "右奥上" } } },
            { "_Back_Left_Up", new Dictionary<Language, string> { { Language.English, "Back Left Up" }, { Language.Japanese, "左奥上" } } },
            { "_Front_Right_Down", new Dictionary<Language, string> { { Language.English, "Front Right Down" }, { Language.Japanese, "右下手前" } } },
            { "_Front_Left_Down", new Dictionary<Language, string> { { Language.English, "Front Left Down" }, { Language.Japanese, "左下手前" } } },
            { "_Back_Right_Down", new Dictionary<Language, string> { { Language.English, "Back Right Down" }, { Language.Japanese, "右奥下" } } },
            { "_Back_Left_Down", new Dictionary<Language, string> { { Language.English, "Back Left Down" }, { Language.Japanese, "左奥下" } } },
            { "CaptureButton", new Dictionary<Language, string> { { Language.English, "Capture Screenshots" }, { Language.Japanese, "スクリーンショットを撮影" } } },
            { "PreviewButton", new Dictionary<Language, string> { { Language.English, "Preview Angles" }, { Language.Japanese, "アングルをプレビュー" } } },
            { "PreviewingAngle", new Dictionary<Language, string> { { Language.English, "Previewing Angle:" }, { Language.Japanese, "プレビュー中のアングル:" } } },
            { "ErrorNoSceneView", new Dictionary<Language, string> { { Language.English, "Could not find an active Scene View to preview in." }, { Language.Japanese, "プレビューするアクティブなシーンビューが見つかりませんでした。" } } },
            { "ResetViewButton", new Dictionary<Language, string> { { Language.English, "Reset View" }, { Language.Japanese, "視点をリセット" } } },
            { "ViewResetMessage", new Dictionary<Language, string> { { Language.English, "Scene view has been reset." }, { Language.Japanese, "シーンの視点をリセットしました。" } } },
            { "ErrorNoSelection", new Dictionary<Language, string> { { Language.English, "Please select at least one GameObject in the scene." }, { Language.Japanese, "シーン内で最低1つのゲームオブジェクトを選択してください。" } } },
            { "ErrorCreateDirectory", new Dictionary<Language, string> { { Language.English, "Failed to create directory." }, { Language.Japanese, "フォルダの作成に失敗しました。" } } },
            { "ProgressCapturing", new Dictionary<Language, string> { { Language.English, "Capturing..." }, { Language.Japanese, "撮影中..." } } },
            { "ProgressProcessing", new Dictionary<Language, string> { { Language.English, "Processing" }, { Language.Japanese, "処理中" } } },
            { "ProgressProcessingGroup", new Dictionary<Language, string> { { Language.English, "Processing grouped objects..." }, { Language.Japanese, "グループ化されたオブジェクトを処理中..." } } },
            { "SuccessTitle", new Dictionary<Language, string> { { Language.English, "Success" }, { Language.Japanese, "成功" } } },
            { "SuccessMessage", new Dictionary<Language, string> { { Language.English, "Screenshots saved to:" }, { Language.Japanese, "スクリーンショットが保存されました:" } } }
        };
    }
}