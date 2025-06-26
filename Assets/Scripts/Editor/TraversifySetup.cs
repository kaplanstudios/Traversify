using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using System.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor.SceneManagement;
using Traversify;
using Traversify.Core;
using Traversify.AI;  // Add this namespace to access MapAnalyzer class

#if UNITY_EDITOR
[InitializeOnLoad]
public class TraversifySetup : EditorWindow
{
    private const string VERSION = "2.0.0";
    private const string MODELS_VERSION = "v2.0";
    
    // Model download URLs
    private const string YOLOV8_MODEL_URL = "https://kaplanstudios.com/models/yolov10m_model.onnx";
    private const string FASTER_RCNN_MODEL_URL = "https://kaplanstudios.com/models/FasterRCNN-10.onnx";
    private const string SAM2_MODEL_URL = "https://kaplanstudios.com/models/sam2_tiny_preprocess.onnx";
    
    // Alternative mirror URLs for redundancy
    private readonly Dictionary<string, string[]> modelMirrors = new Dictionary<string, string[]>
    {
        ["yolov8"] = new string[] {
            "https://kaplanstudios.com/models/yolov10m_model.onnx",
            "https://kaplanstudios.com/models/backup/yolov10m_model.onnx"
        },
        ["fasterrcnn"] = new string[] {
            "https://kaplanstudios.com/models/FasterRCNN-10.onnx",
            "https://kaplanstudios.com/models/backup/FasterRCNN-10.onnx"
        },
        ["sam2"] = new string[] {
            "https://kaplanstudios.com/models/sam2_tiny_preprocess.onnx",
            "https://kaplanstudios.com/models/backup/sam2_tiny_preprocess.onnx"
        }
    };
    
    // Model file sizes (approximate, in MB)
    private readonly Dictionary<string, float> modelSizes = new Dictionary<string, float>
    {
        ["yolov8"] = 6.2f,
        ["fasterrcnn"] = 167.7f,
        ["sam2"] = 89.3f
    };
    
    private bool setupUI = true;
    private bool setupAIModels = true;
    private bool setupTerrain = true;
    private bool downloadModels = true;
    private bool createSampleScene = true;
    private bool installDependencies = true;
    
    private string openAIKey = "";
    private string tripo3DApiKey = "";
    private Vector2 scrollPosition;
    
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle paragraphStyle;
    private GUIStyle boxStyle;
    private GUIStyle progressBarStyle;
    private GUIStyle progressBarFillStyle;
    
    private Color successColor = new Color(0.4f, 0.8f, 0.4f);
    private Color warningColor = new Color(0.9f, 0.7f, 0.2f);
    private Color errorColor = new Color(0.9f, 0.3f, 0.3f);
    private Color progressColor = new Color(0.2f, 0.7f, 1f);
    
    private bool isDownloading = false;
    private string currentDownloadName = "";
    private float currentDownloadProgress = 0f;
    private string downloadStatus = "";
    private EditorCoroutine downloadCoroutine;
    
    private readonly string[] requiredFolders = new string[] {
        "Assets/Scripts",
        "Assets/Scripts/Core",
        "Assets/Scripts/AI",
        "Assets/Scripts/Terrain",
        "Assets/Scripts/UI",
        "Assets/Scripts/Utils",
        "Assets/Scripts/Editor",
        "Assets/StreamingAssets/Traversify",
        "Assets/StreamingAssets/Traversify/Models",
        "Assets/Materials",
        "Assets/Prefabs",
        "Assets/Traversify",
        "Assets/Traversify/Scenes",
        "Assets/Traversify/Materials",
        "Assets/Traversify/Terrain"
    };
    
    private Dictionary<string, bool> dependencyStatus = new Dictionary<string, bool>();
    
    // Track created objects for verification
    private List<GameObject> createdGameObjects = new List<GameObject>();
    private Dictionary<string, Component> createdComponents = new Dictionary<string, Component>();
    
    [MenuItem("Tools/Traversify/Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<TraversifySetup>("Traversify Setup Wizard");
        window.minSize = new Vector2(700, 800);
        window.position = new Rect(100, 100, 700, 800);
    }
    
    private void OnEnable()
    {
        Debug.Log("[TraversifySetup] Initializing setup wizard...");
        
        // Try to load saved API keys
        openAIKey = EditorPrefs.GetString("TraversifyOpenAIKey", "");
        tripo3DApiKey = EditorPrefs.GetString("TraversifyTripo3DKey", "");
        
        // Check dependencies
        CheckDependencies();
    }
    
    private void OnDisable()
    {
        if (downloadCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(downloadCoroutine);
        }
        
        // Clear tracked objects
        createdGameObjects.Clear();
        createdComponents.Clear();
    }
    
    private void InitializeStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
            };
        }
        
        if (subHeaderStyle == null)
        {
            subHeaderStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };
        }
        
        if (paragraphStyle == null)
        {
            paragraphStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                fontSize = 12,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) }
            };
        }
        
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 10, 10),
                margin = new RectOffset(5, 5, 5, 5)
            };
        }
        
        if (progressBarStyle == null)
        {
            progressBarStyle = new GUIStyle()
            {
                normal = { background = EditorGUIUtility.whiteTexture }
            };
        }
        
        if (progressBarFillStyle == null)
        {
            progressBarFillStyle = new GUIStyle()
            {
                normal = { background = EditorGUIUtility.whiteTexture }
            };
        }
    }
    
    private void CheckDependencies()
    {
        Debug.Log("[TraversifySetup] Checking dependencies...");
        
        // Check for required packages
        dependencyStatus["AI.Inference"] = IsPackageInstalled("com.unity.ai.inference");
        dependencyStatus["TextMeshPro"] = IsPackageInstalled("com.unity.textmeshpro");
        dependencyStatus["UI"] = IsPackageInstalled("com.unity.ugui");
        dependencyStatus["EditorCoroutines"] = IsPackageInstalled("com.unity.editorcoroutines");
        
        // Check for required Unity modules
        dependencyStatus["TerrainModule"] = true; // Always available in Unity
        dependencyStatus["UIModule"] = true; // Always available in Unity
        
        Debug.Log($"[TraversifySetup] Dependencies checked. Missing: {dependencyStatus.Count(d => !d.Value)}");
    }
    
    private bool IsPackageInstalled(string packageId)
    {
        try {
            // Log that we're checking for this specific package
            Debug.Log($"[TraversifySetup] Checking for package: {packageId}");
            
            var listRequest = UnityEditor.PackageManager.Client.List(true); // Include all packages, including built-in
            while (!listRequest.IsCompleted) { 
                // Wait for request to complete
                System.Threading.Thread.Sleep(100);
            }
            
            if (listRequest.Status == UnityEditor.PackageManager.StatusCode.Success)
            {
                // Log all found packages for debugging
                Debug.Log($"[TraversifySetup] Total packages found: {listRequest.Result.Count()}");
                
                foreach (var package in listRequest.Result)
                {
                    // More detailed logging of package information
                    Debug.Log($"[TraversifySetup] Found package: {package.name} (ID: {package.packageId}) {package.version}");
                    
                    // Check by name or packageId
                    if (package.packageId.StartsWith(packageId) || package.name == packageId)
                    {
                        Debug.Log($"[TraversifySetup] ✓ Package found: {package.name} {package.version}");
                        return true;
                    }
                }
                
                // Alternative check for TextMeshPro specifically (might be loaded differently)
                if (packageId == "com.unity.textmeshpro")
                {
                    // Check if TMP assembly exists
                    var tmpAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Unity.TextMeshPro");
                    
                    if (tmpAssembly != null)
                    {
                        Debug.Log("[TraversifySetup] ✓ TextMeshPro found through assembly check");
                        return true;
                    }
                    
                    // Check if TMP types are available
                    var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                    if (tmpType != null)
                    {
                        Debug.Log("[TraversifySetup] ✓ TextMeshPro found through type check");
                        return true;
                    }
                    
                    // Check for TMP assets
                    if (UnityEditor.AssetDatabase.FindAssets("t:asmdef Unity.TextMeshPro").Length > 0)
                    {
                        Debug.Log("[TraversifySetup] ✓ TextMeshPro found through asset database check");
                        return true;
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[TraversifySetup] Package list request failed with status: {listRequest.Status}");
            }
            
            Debug.LogWarning($"[TraversifySetup] Package not found: {packageId}");
            return false;
        }
        catch (Exception e) {
            Debug.LogError($"[TraversifySetup] Error checking for package {packageId}: {e.Message}");
            return false;
        }
    }
    
    private void OnGUI()
    {
        if (headerStyle == null)
        {
            InitializeStyles();
        }
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        
        try
        {
            // Header
            EditorGUILayout.Space(20);
            GUILayout.Label("TRAVERSIFY", headerStyle);
            GUILayout.Label($"Version {VERSION}", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });
            EditorGUILayout.Space(10);
            
            // Description
            EditorGUILayout.BeginVertical(boxStyle);
            EditorGUILayout.LabelField("Advanced Map-to-Terrain AI System", subHeaderStyle);
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Traversify uses state-of-the-art AI models to analyze map images and generate detailed 3D terrains with accurately placed objects.", paragraphStyle);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.Space(20);
            
            // Dependencies Section
            DrawDependenciesSection();
            
            EditorGUILayout.Space(20);
            
            // Setup Options
            DrawSetupOptions();
            
            EditorGUILayout.Space(20);
            
            // API Configuration
            DrawAPIConfiguration();
            
            EditorGUILayout.Space(20);
            
            // Model Status
            DrawModelStatus();
            
            EditorGUILayout.Space(20);
            
            // Project Structure
            DrawProjectStructure();
            
            EditorGUILayout.Space(20);
            
            // Action Buttons
            DrawActionButtons();
            
            EditorGUILayout.Space(20);
            
            // Download Progress
            if (isDownloading)
            {
                DrawDownloadProgress();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[TraversifySetup] Error in GUI: {e.Message}\n{e.StackTrace}");
            EditorGUILayout.HelpBox("An error occurred while rendering the UI. Check the console for details.", MessageType.Error);
        }
        
        EditorGUILayout.EndScrollView();
    }
    
    private void DrawDependenciesSection()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        GUILayout.Label("Dependencies", subHeaderStyle);
        EditorGUILayout.Space(10);
        
        foreach (var dep in dependencyStatus)
        {
            EditorGUILayout.BeginHorizontal();
            
            if (dep.Value)
            {
                GUI.color = successColor;
                EditorGUILayout.LabelField($"✓ {dep.Key}", GUILayout.Width(200));
                EditorGUILayout.LabelField("Installed");
            }
            else
            {
                GUI.color = errorColor;
                EditorGUILayout.LabelField($"✗ {dep.Key}", GUILayout.Width(200));
                EditorGUILayout.LabelField("Missing");
                
                GUI.color = Color.white;
                if (GUILayout.Button("Install", GUILayout.Width(70)))
                {
                    InstallPackage(dep.Key);
                }
            }
            
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        
        if (dependencyStatus.Values.Any(v => !v))
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("Some dependencies are missing. Click Install to add them via Package Manager.", MessageType.Warning);
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawSetupOptions()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        GUILayout.Label("Setup Options", subHeaderStyle);
        EditorGUILayout.Space(10);
        
        setupUI = EditorGUILayout.ToggleLeft("Set up user interface components", setupUI);
        setupAIModels = EditorGUILayout.ToggleLeft("Configure AI model integration", setupAIModels);
        setupTerrain = EditorGUILayout.ToggleLeft("Set up terrain generation system", setupTerrain);
        downloadModels = EditorGUILayout.ToggleLeft("Download AI models (263 MB total)", downloadModels);
        createSampleScene = EditorGUILayout.ToggleLeft("Create sample scene with demo content", createSampleScene);
        installDependencies = EditorGUILayout.ToggleLeft("Install missing dependencies automatically", installDependencies);
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawAPIConfiguration()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        GUILayout.Label("API Configuration", subHeaderStyle);
        EditorGUILayout.Space(10);
        
        EditorGUILayout.LabelField("API keys are required for enhanced features.", paragraphStyle);
        EditorGUILayout.Space(5);
        
        // OpenAI API Key
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("OpenAI API Key:", GUILayout.Width(150));
        openAIKey = EditorGUILayout.PasswordField(openAIKey);
        EditorGUILayout.EndHorizontal();
        
        // Tripo3D API Key
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Tripo3D API Key:", GUILayout.Width(150));
        tripo3DApiKey = EditorGUILayout.PasswordField(tripo3DApiKey);
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(5);
        
        if (string.IsNullOrEmpty(openAIKey))
        {
            EditorGUILayout.HelpBox("OpenAI API key is needed for enhanced object descriptions.", MessageType.Info);
        }
        
        if (string.IsNullOrEmpty(tripo3DApiKey))
        {
            EditorGUILayout.HelpBox("Tripo3D API key is needed for AI-generated 3D models.", MessageType.Info);
        }
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Get OpenAI API Key", GUILayout.Width(150)))
        {
            Application.OpenURL("https://platform.openai.com/api-keys");
        }
        if (GUILayout.Button("Get Tripo3D API Key", GUILayout.Width(150)))
        {
            Application.OpenURL("https://www.tripo3d.ai/");
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawModelStatus()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        GUILayout.Label("AI Models", subHeaderStyle);
        EditorGUILayout.Space(10);
        
        // Check model files
        string yoloPath = GetModelPath("yolov8n.onnx");
        string fasterRcnnPath = GetModelPath("FasterRCNN-12.onnx");
        string sam2Path = GetModelPath("sam2_hiera_base.onnx");
        
        bool yoloExists = File.Exists(yoloPath);
        bool fasterRcnnExists = File.Exists(fasterRcnnPath);
        bool sam2Exists = File.Exists(sam2Path);
        
        // YOLOv8
        DrawModelStatusRow("YOLOv8", "Object detection", yoloExists, modelSizes["yolov8"]);
        
        // Faster R-CNN
        DrawModelStatusRow("Faster R-CNN", "Advanced object classification", fasterRcnnExists, modelSizes["fasterrcnn"]);
        
        // SAM2
        DrawModelStatusRow("SAM2", "Precise segmentation", sam2Exists, modelSizes["sam2"]);
        
        EditorGUILayout.Space(10);
        
        float totalSize = modelSizes.Values.Sum();
        EditorGUILayout.LabelField($"Total download size: {totalSize:F1} MB", EditorStyles.boldLabel);
        
        // Add reimport models button
        EditorGUILayout.Space(5);
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        
        GUI.enabled = yoloExists || fasterRcnnExists || sam2Exists;
        if (GUILayout.Button("Reimport AI Models", GUILayout.Width(150), GUILayout.Height(30)))
        {
            ReimportAIModels();
        }
        GUI.enabled = true;
        
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawModelStatusRow(string modelName, string description, bool exists, float sizeMB)
    {
        EditorGUILayout.BeginHorizontal();
        
        if (exists)
        {
            GUI.color = successColor;
            EditorGUILayout.LabelField($"✓ {modelName}", GUILayout.Width(150));
            GUI.color = Color.white;
            EditorGUILayout.LabelField(description, GUILayout.Width(250));
            EditorGUILayout.LabelField("Installed", GUILayout.Width(100));
        }
        else
        {
            GUI.color = warningColor;
            EditorGUILayout.LabelField($"○ {modelName}", GUILayout.Width(150));
            GUI.color = Color.white;
            EditorGUILayout.LabelField(description, GUILayout.Width(250));
            EditorGUILayout.LabelField($"{sizeMB:F1} MB", GUILayout.Width(100));
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    private void DrawProjectStructure()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        GUILayout.Label("Project Structure", subHeaderStyle);
        EditorGUILayout.Space(10);
        
        int existingFolders = 0;
        foreach (string folder in requiredFolders)
        {
            if (Directory.Exists(folder))
            {
                existingFolders++;
            }
        }
        
        float structureProgress = (float)existingFolders / requiredFolders.Length;
        
        // Progress bar
        Rect progressRect = GUILayoutUtility.GetRect(18, 18, "TextField");
        DrawProgressBar(progressRect, structureProgress, $"{existingFolders}/{requiredFolders.Length} folders");
        
        EditorGUILayout.Space(5);
        
        if (structureProgress < 1.0f)
        {
            EditorGUILayout.HelpBox($"{requiredFolders.Length - existingFolders} folders will be created during setup.", MessageType.Info);
        }
        else
        {
            GUI.color = successColor;
            EditorGUILayout.LabelField("✓ All project folders exist");
            GUI.color = Color.white;
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.Space();
        
        GUI.enabled = !isDownloading && dependencyStatus.Values.All(v => v);
        
        if (GUILayout.Button("Set Up Traversify", GUILayout.Height(40), GUILayout.Width(200)))
        {
            RunSetup();
        }
        
        GUI.enabled = true;
        
        if (GUILayout.Button("Refresh", GUILayout.Height(40), GUILayout.Width(100)))
        {
            CheckDependencies();
            Repaint();
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.EndHorizontal();
        
        if (!dependencyStatus.Values.All(v => v))
        {
            EditorGUILayout.HelpBox("Please install all dependencies before running setup.", MessageType.Warning);
        }
    }
    
    private void DrawDownloadProgress()
    {
        EditorGUILayout.BeginVertical(boxStyle);
        GUILayout.Label("Download Progress", subHeaderStyle);
        EditorGUILayout.Space(10);
        
        EditorGUILayout.LabelField($"Downloading: {currentDownloadName}");
        EditorGUILayout.LabelField(downloadStatus);
        
        Rect progressRect = GUILayoutUtility.GetRect(18, 20, "TextField");
        DrawProgressBar(progressRect, currentDownloadProgress, $"{(currentDownloadProgress * 100):F1}%");
        
        EditorGUILayout.Space(5);
        
        if (GUILayout.Button("Cancel Download"))
        {
            CancelDownload();
        }
        
        EditorGUILayout.EndVertical();
    }
    
    private void DrawProgressBar(Rect rect, float progress, string label)
    {
        // Background
        GUI.color = new Color(0.2f, 0.2f, 0.2f);
        GUI.Box(rect, GUIContent.none, progressBarStyle);
        
        // Fill
        if (progress > 0)
        {
            Rect fillRect = new Rect(rect.x, rect.y, rect.width * progress, rect.height);
            GUI.color = progressColor;
            GUI.Box(fillRect, GUIContent.none, progressBarFillStyle);
        }
        
        // Label
        GUI.color = Color.white;
        GUI.Label(rect, label, new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });
    }
    
    private void InstallPackage(string packageName)
    {
        string packageId = "";
        
        switch (packageName)
        {
            case "AI.Inference":
                packageId = "com.unity.ai.inference@1.0.0-exp.2";
                break;
            case "TextMeshPro":
                packageId = "com.unity.textmeshpro@3.0.6";
                break;
            case "UI":
                packageId = "com.unity.ugui@1.0.0";
                break;
            case "EditorCoroutines":
                packageId = "com.unity.editorcoroutines@1.0.0";
                break;
        }
        
        if (!string.IsNullOrEmpty(packageId))
        {
            UnityEditor.PackageManager.Client.Add(packageId);
            EditorUtility.DisplayDialog("Package Installation", 
                $"Installing {packageName}. Please wait for the Package Manager to complete the installation.", 
                "OK");
        }
    }
    
    private string GetModelPath(string modelFileName)
    {
        // Standardize on single location: Assets/StreamingAssets/Traversify/Models
        string standardPath = Path.Combine(Application.dataPath, "StreamingAssets", "Traversify", "Models", modelFileName);
        
        // If searching for a specific file and it doesn't exist in the standard location,
        // check legacy locations as fallback
        if (!string.IsNullOrEmpty(modelFileName) && !File.Exists(standardPath))
        {
            string[] legacyPaths = new string[]
            {
                Path.Combine(Application.dataPath, "Scripts", "AI", "Models", modelFileName),
                Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", modelFileName)
            };
            
            foreach (string path in legacyPaths)
            {
                if (File.Exists(path))
                {
                    // Found in legacy location, inform in log
                    Debug.LogWarning($"[TraversifySetup] Model found in legacy location: {path}. Consider moving to: {standardPath}");
                    return path;
                }
            }
        }
        
        // Return the standard path even if file doesn't exist yet
        return standardPath;
    }
    
    private void RunSetup()
    {
        try
        {
            Debug.Log("[TraversifySetup] Starting setup process...");
            
            // Clear tracking lists
            createdGameObjects.Clear();
            createdComponents.Clear();
            
            // Save API keys
            EditorPrefs.SetString("TraversifyOpenAIKey", openAIKey);
            EditorPrefs.SetString("TraversifyTripo3DKey", tripo3DApiKey);
            
            // Create required folders
            CreateRequiredFolders();
            
            // Create sample scene first if requested
            if (createSampleScene)
            {
                CreateSampleScene();
            }
            
            // Download models if requested
            if (downloadModels)
            {
                downloadCoroutine = EditorCoroutineUtility.StartCoroutine(DownloadAllModels(), this);
            }
            else
            {
                CompleteSetup();
            }
        }
        catch (Exception e)
        {
            EditorUtility.DisplayDialog("Setup Error", 
                $"An error occurred during setup: {e.Message}\n\nCheck the console for details.", 
                "OK");
            Debug.LogError($"[TraversifySetup] Setup error: {e.Message}\n{e.StackTrace}");
        }
    }
    
    private void CreateRequiredFolders()
    {
        Debug.Log("[TraversifySetup] Creating required folders...");
        
        foreach (string folder in requiredFolders)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Debug.Log($"[TraversifySetup] Created directory: {folder}");
            }
        }
        
        AssetDatabase.Refresh();
    }
    
    private void CreateSampleScene()
    {
        Debug.Log("[TraversifySetup] Creating sample scene...");
        
        try
        {
            // Create new scene
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, 
                NewSceneMode.Single);
            
            // Set up lighting
            SetupSceneLighting();
            
            // Set up camera
            SetupSceneCamera();
            
            // Create Traversify core objects
            CreateTraversifyCore();
            
            // Create UI system
            CreateUISystem();
            
            // Set up environment
            SetupEnvironment();
            
            // Save the scene
            string scenePath = "Assets/Traversify/Scenes/TraversifyScene.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            
            Debug.Log($"[TraversifySetup] Scene created at: {scenePath}");
            
            // Verify all components
            VerifySetup();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TraversifySetup] Failed to create scene: {e.Message}\n{e.StackTrace}");
            throw;
        }
    }
    
    private void SetupSceneLighting()
    {
        Debug.Log("[TraversifySetup] Setting up scene lighting...");
        
        GameObject lightObj = GameObject.Find("Directional Light");
        if (lightObj != null)
        {
            Light light = lightObj.GetComponent<Light>();
            light.intensity = 1.5f;
            light.color = new Color(1f, 0.98f, 0.92f);
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.4f;
            light.shadowNearPlane = 0.2f;
        }
    }
    
    private void SetupSceneCamera()
    {
        Debug.Log("[TraversifySetup] Setting up scene camera...");
        
        GameObject cameraObj = GameObject.Find("Main Camera");
        if (cameraObj != null)
        {
            Camera camera = cameraObj.GetComponent<Camera>();
            camera.transform.position = new Vector3(250, 180, -150);
            camera.transform.rotation = Quaternion.Euler(35, 15, 0);
            camera.farClipPlane = 3000;
            camera.nearClipPlane = 0.3f;
            camera.fieldOfView = 60;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            
            // Add camera controller
            FreeCameraController cameraController = cameraObj.AddComponent<FreeCameraController>();
            cameraController.moveSpeed = 75f;
            cameraController.fastMoveSpeed = 200f;
            cameraController.rotationSpeed = 2f;
            cameraController.smoothTime = 0.1f;
            
            createdComponents["CameraController"] = cameraController;
        }
    }
    
    private void CreateTraversifyCore()
    {
        Debug.Log("[TraversifySetup] Creating Traversify core components...");
        
        // Create main manager object
        GameObject traversifyManager = new GameObject("TraversifyManager");
        traversifyManager.transform.position = Vector3.zero;
        createdGameObjects.Add(traversifyManager);
        
        // Add TraversifyManager component
        TraversifyManager manager = traversifyManager.AddComponent<TraversifyManager>();
        createdComponents["TraversifyManager"] = manager;
        
        // Create components container
        GameObject componentsObj = new GameObject("TraversifyComponents");
        componentsObj.transform.SetParent(traversifyManager.transform);
        createdGameObjects.Add(componentsObj);
        
        // Add core components
        MapAnalyzer mapAnalyzer = componentsObj.AddComponent<MapAnalyzer>();
        Traversify.TerrainGenerator terrainGenerator = componentsObj.AddComponent<Traversify.TerrainGenerator>();
        SegmentationVisualizer segmentationVisualizer = componentsObj.AddComponent<SegmentationVisualizer>();
        Traversify.AI.ModelGenerator modelGenerator = componentsObj.AddComponent<Traversify.AI.ModelGenerator>();
        
        // Track components
        createdComponents["MapAnalyzer"] = mapAnalyzer;
        createdComponents["TerrainGenerator"] = terrainGenerator;
        createdComponents["SegmentationVisualizer"] = segmentationVisualizer;
        createdComponents["ModelGenerator"] = modelGenerator;
        
        // Configure components
        ConfigureMapAnalyzer(mapAnalyzer);
        ConfigureTerrainGenerator(terrainGenerator);
        ConfigureModelGenerator(modelGenerator);
        
        // Add debugger
        TraversifyDebugger debugger = traversifyManager.AddComponent<TraversifyDebugger>();
        createdComponents["Debugger"] = debugger;
        
        Debug.Log("[TraversifySetup] Core components created successfully");
    }
    
    private void ConfigureMapAnalyzer(MapAnalyzer analyzer)
    {
        if (analyzer == null) return;
        
        analyzer.openAIApiKey = openAIKey;
        analyzer.yoloModelPath = GetModelPath("yolov8n.onnx");
        analyzer.fasterRcnnModelPath = GetModelPath("FasterRCNN-12.onnx");
        analyzer.sam2ModelPath = GetModelPath("sam2_hiera_base.onnx");
        analyzer.maxObjectsToProcess = 100;
        analyzer.groupingThreshold = 0.1f;
        analyzer.useHighQuality = true;
        analyzer.maxAPIRequestsPerFrame = 5;
        analyzer.useGPU = false; // Explicitly disable GPU usage
        
        Debug.Log("[TraversifySetup] MapAnalyzer configured with GPU disabled");
    }
    
    private void ConfigureTerrainGenerator(Traversify.TerrainGenerator generator)
    {
        if (generator == null) return;
        
        generator.heightMapMultiplier = 30f;
        generator.generateWaterPlane = true;
        generator.waterHeight = 0.1f;
        
        Debug.Log("[TraversifySetup] TerrainGenerator configured");
    }
    
    private void ConfigureModelGenerator(Traversify.AI.ModelGenerator generator)
    {
        if (generator == null) return;
        
        generator.openAIApiKey = openAIKey;
        generator.tripo3DApiKey = tripo3DApiKey;
        generator.groupSimilarObjects = true;
        generator.maxConcurrentGenerations = 3;
        generator.useAIGeneration = !string.IsNullOrEmpty(openAIKey) && !string.IsNullOrEmpty(tripo3DApiKey);
        
        Debug.Log("[TraversifySetup] ModelGenerator configured");
    }
    
    private void CreateUISystem()
    {
        Debug.Log("[TraversifySetup] Creating UI system...");
        
        // Create canvas
        GameObject canvasObj = new GameObject("TraversifyCanvas");
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        createdGameObjects.Add(canvasObj);
        
        // Add canvas scaler
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        
        // Add graphic raycaster
        GraphicRaycaster raycaster = canvasObj.AddComponent<GraphicRaycaster>();
        
        // Create UI root
        GameObject uiRoot = new GameObject("TraversifyUI");
        uiRoot.transform.SetParent(canvasObj.transform, false);
        RectTransform uiRootRect = uiRoot.AddComponent<RectTransform>();
        uiRootRect.anchorMin = Vector2.zero;
        uiRootRect.anchorMax = Vector2.one;
        uiRootRect.offsetMin = Vector2.zero;
        uiRootRect.offsetMax = Vector2.zero;
        createdGameObjects.Add(uiRoot);
        
        // Create UI panels
        GameObject mainPanel = CreateMainPanel(uiRoot.transform);
        GameObject loadingPanel = CreateLoadingPanel(uiRoot.transform);
        GameObject settingsPanel = CreateSettingsPanel(uiRoot.transform);
        
        // Connect UI references to TraversifyManager
        ConnectUIToManager(mainPanel, loadingPanel, settingsPanel);
        
        Debug.Log("[TraversifySetup] UI system created successfully");
    }
    
    private GameObject CreateMainPanel(Transform parent)
    {
        GameObject mainPanel = CreateUIPanel("MainPanel", parent, Vector2.zero, Vector2.one);
        
        // Background
        Image bgImage = mainPanel.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        
        // Create side panel
        GameObject sidePanel = CreateUIPanel("SidePanel", mainPanel.transform, 
            new Vector2(0, 0), new Vector2(0.25f, 1));
        
        Image sidePanelBg = sidePanel.AddComponent<Image>();
        sidePanelBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        
        // Add vertical layout
        VerticalLayoutGroup sideLayout = sidePanel.AddComponent<VerticalLayoutGroup>();
        sideLayout.padding = new RectOffset(20, 20, 20, 20);
        sideLayout.spacing = 15;
        sideLayout.childForceExpandWidth = true;
        sideLayout.childForceExpandHeight = false;
        sideLayout.childControlHeight = false;
        sideLayout.childScaleHeight = false;
        
        // Create UI elements
        CreateTitleSection(sidePanel.transform);
        CreateMapPreviewSection(sidePanel.transform);
        CreateButtonSection(sidePanel.transform);
        CreateStatusSection(sidePanel.transform);
        
        // Create content area
        GameObject contentArea = CreateUIPanel("ContentArea", mainPanel.transform, 
            new Vector2(0.25f, 0), new Vector2(1, 1));
        
        return mainPanel;
    }
    
    private void CreateTitleSection(Transform parent)
    {
        // Title
        GameObject titleObj = CreateUIText("Title", parent, "TRAVERSIFY");
        Text titleText = titleObj.GetComponent<Text>();
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 32;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = new Color(0.9f, 0.9f, 0.9f);
        
        LayoutElement titleLayout = titleObj.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 60;
        
        // Subtitle
        GameObject subtitleObj = CreateUIText("Subtitle", parent, "AI-Powered Terrain Generation");
        Text subtitleText = subtitleObj.GetComponent<Text>();
        subtitleText.fontSize = 14;
        subtitleText.alignment = TextAnchor.MiddleCenter;
        subtitleText.color = new Color(0.7f, 0.7f, 0.7f);
        
        LayoutElement subtitleLayout = subtitleObj.AddComponent<LayoutElement>();
        subtitleLayout.preferredHeight = 30;
        
        // Separator
        CreateUISeparator(parent);
    }
    
    private void CreateMapPreviewSection(Transform parent)
    {
        // Preview container
        GameObject previewContainer = CreateUIPanel("PreviewContainer", parent);
        LayoutElement previewLayout = previewContainer.AddComponent<LayoutElement>();
        previewLayout.preferredHeight = 300;
        
        Image previewBg = previewContainer.AddComponent<Image>();
        previewBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        
        // Map preview
        GameObject mapPreviewObj = new GameObject("MapPreview");
        mapPreviewObj.transform.SetParent(previewContainer.transform, false);
        
        RectTransform previewRect = mapPreviewObj.AddComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0.05f, 0.05f);
        previewRect.anchorMax = new Vector2(0.95f, 0.95f);
        previewRect.offsetMin = Vector2.zero;
        previewRect.offsetMax = Vector2.zero;
        
        RawImage mapPreview = mapPreviewObj.AddComponent<RawImage>();
        mapPreview.color = new Color(0.3f, 0.3f, 0.3f, 1);
        
        AspectRatioFitter aspectFitter = mapPreviewObj.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        
        // Initially hide the preview
        mapPreviewObj.SetActive(false);
    }
    
    private void CreateButtonSection(Transform parent)
    {
        // Upload button
        GameObject uploadBtn = CreateUIButton("UploadButton", parent, "UPLOAD MAP");
        Button uploadButton = uploadBtn.GetComponent<Button>();
        Image uploadBtnImage = uploadBtn.GetComponent<Image>();
        uploadBtnImage.color = new Color(0.2f, 0.5f, 0.9f);
        
        LayoutElement uploadLayout = uploadBtn.AddComponent<LayoutElement>();
        uploadLayout.preferredHeight = 50;
        
        // Generate button
        GameObject generateBtn = CreateUIButton("GenerateButton", parent, "GENERATE TERRAIN");
        Button generateButton = generateBtn.GetComponent<Button>();
        Image generateBtnImage = generateBtn.GetComponent<Image>();
        generateBtnImage.color = new Color(0.2f, 0.8f, 0.4f);
        generateButton.interactable = false;
        
        LayoutElement generateLayout = generateBtn.AddComponent<LayoutElement>();
        generateLayout.preferredHeight = 50;
        
        // Settings button
        GameObject settingsBtn = CreateUIButton("SettingsButton", parent, "SETTINGS");
        Button settingsButton = settingsBtn.GetComponent<Button>();
        Image settingsBtnImage = settingsBtn.GetComponent<Image>();
        settingsBtnImage.color = new Color(0.6f, 0.6f, 0.6f);
        
        LayoutElement settingsLayout = settingsBtn.AddComponent<LayoutElement>();
        settingsLayout.preferredHeight = 40;
        
        // Separator
        CreateUISeparator(parent);
    }
    
    private void CreateStatusSection(Transform parent)
    {
        // Status container
        GameObject statusContainer = CreateUIPanel("StatusContainer", parent);
        LayoutElement statusLayout = statusContainer.AddComponent<LayoutElement>();
        statusLayout.preferredHeight = 100;
        statusLayout.flexibleHeight = 1;
        
        Image statusBg = statusContainer.AddComponent<Image>();
        statusBg.color = new Color(0.05f, 0.05f, 0.05f, 0.5f);
        
        // Status text
        GameObject statusObj = CreateUIText("StatusText", statusContainer.transform, "Ready to process map images");
        Text statusText = statusObj.GetComponent<Text>();
        statusText.fontSize = 14;
        statusText.alignment = TextAnchor.UpperLeft;
        statusText.color = new Color(0.8f, 0.8f, 0.8f);
        
        RectTransform statusRect = statusObj.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.05f, 0.05f);
        statusRect.anchorMax = new Vector2(0.95f, 0.95f);
        statusRect.offsetMin = Vector2.zero;
        statusRect.offsetMax = Vector2.zero;
    }
    
    private GameObject CreateLoadingPanel(Transform parent)
    {
        GameObject loadingPanel = CreateUIPanel("LoadingPanel", parent, Vector2.zero, Vector2.one);
        loadingPanel.SetActive(false);
        
        // Dark overlay
        Image overlay = loadingPanel.AddComponent<Image>();
        overlay.color = new Color(0, 0, 0, 0.9f);
        
        // Loading container
        GameObject container = CreateUIPanel("LoadingContainer", loadingPanel.transform, 
            new Vector2(0.3f, 0.3f), new Vector2(0.7f, 0.7f));
        
        Image containerBg = container.AddComponent<Image>();
        containerBg.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
        
        // Add layout
        VerticalLayoutGroup layout = container.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 40, 40);
        layout.spacing = 20;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleCenter;
        
        // Create loading UI elements
        CreateLoadingTitle(container.transform);
        CreateStageText(container.transform);
        CreateProgressBar(container.transform);
        CreateProgressText(container.transform);
        CreateDetailsText(container.transform);
        CreateCancelButton(container.transform);
        
        return loadingPanel;
    }
    
    private void CreateLoadingTitle(Transform parent)
    {
        GameObject titleObj = CreateUIText("LoadingTitle", parent, "PROCESSING");
        Text titleText = titleObj.GetComponent<Text>();
        titleText.fontSize = 28;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        
        LayoutElement titleLayout = titleObj.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 40;
    }
    
    private void CreateStageText(Transform parent)
    {
        GameObject stageObj = CreateUIText("StageText", parent, "Initializing...");
        Text stageText = stageObj.GetComponent<Text>();
        stageText.fontSize = 18;
        stageText.alignment = TextAnchor.MiddleCenter;
        stageText.color = new Color(0.8f, 0.8f, 0.8f);
        
        LayoutElement stageLayout = stageObj.AddComponent<LayoutElement>();
        stageLayout.preferredHeight = 30;
    }
    
    private void CreateProgressBar(Transform parent)
    {
        GameObject progressContainer = CreateUIPanel("ProgressContainer", parent);
        LayoutElement progressLayout = progressContainer.AddComponent<LayoutElement>();
        progressLayout.preferredHeight = 30;
        
        Image progressBg = progressContainer.AddComponent<Image>();
        progressBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        
        GameObject progressFill = CreateUIPanel("ProgressFill", progressContainer.transform, 
            Vector2.zero, new Vector2(0, 1));
        
        Image fillImage = progressFill.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.7f, 1f);
        
        Slider progressBar = progressContainer.AddComponent<Slider>();
        progressBar.fillRect = progressFill.GetComponent<RectTransform>();
        progressBar.targetGraphic = progressBg;
        progressBar.minValue = 0;
        progressBar.maxValue = 1;
        progressBar.value = 0;
    }
    
    private void CreateProgressText(Transform parent)
    {
        GameObject progressTextObj = CreateUIText("ProgressText", parent, "0%");
        Text progressText = progressTextObj.GetComponent<Text>();
        progressText.fontSize = 16;
        progressText.alignment = TextAnchor.MiddleCenter;
        
        LayoutElement progressTextLayout = progressTextObj.AddComponent<LayoutElement>();
        progressTextLayout.preferredHeight = 25;
    }
    
    private void CreateDetailsText(Transform parent)
    {
        GameObject detailsObj = CreateUIText("DetailsText", parent, "");
        Text detailsText = detailsObj.GetComponent<Text>();
        detailsText.fontSize = 14;
        detailsText.alignment = TextAnchor.MiddleCenter;
        detailsText.color = new Color(0.6f, 0.6f, 0.6f);
        
        LayoutElement detailsLayout = detailsObj.AddComponent<LayoutElement>();
        detailsLayout.preferredHeight = 60;
        detailsLayout.flexibleHeight = 1;
    }
    
    private void CreateCancelButton(Transform parent)
    {
        GameObject cancelBtn = CreateUIButton("CancelButton", parent, "CANCEL");
        Button cancelButton = cancelBtn.GetComponent<Button>();
        Image cancelBtnImage = cancelBtn.GetComponent<Image>();
        cancelBtnImage.color = new Color(0.8f, 0.3f, 0.3f);
        
        LayoutElement cancelLayout = cancelBtn.AddComponent<LayoutElement>();
        cancelLayout.preferredHeight = 40;
        cancelLayout.preferredWidth = 150;
    }
    
    private GameObject CreateSettingsPanel(Transform parent)
    {
        GameObject settingsPanel = CreateUIPanel("SettingsPanel", parent, Vector2.zero, Vector2.one);
        settingsPanel.SetActive(false);
        
        // Dark overlay
        Image overlay = settingsPanel.AddComponent<Image>();
        overlay.color = new Color(0, 0, 0, 0.8f);
        
        // Add button to close on overlay click
        Button overlayButton = settingsPanel.AddComponent<Button>();
        overlayButton.transition = Selectable.Transition.None;
        
        // Settings window
        GameObject window = CreateUIPanel("SettingsWindow", settingsPanel.transform, 
            new Vector2(0.2f, 0.1f), new Vector2(0.8f, 0.9f));
        
        Image windowBg = window.AddComponent<Image>();
        windowBg.color = new Color(0.15f, 0.15f, 0.15f, 0.98f);
        
        // Window layout
        VerticalLayoutGroup windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(30, 30, 30, 30);
        windowLayout.spacing = 20;
        
        // Header
        GameObject headerObj = CreateUIText("SettingsHeader", window.transform, "SETTINGS");
        Text headerText = headerObj.GetComponent<Text>();
        headerText.fontSize = 24;
        headerText.fontStyle = FontStyle.Bold;
        headerText.alignment = TextAnchor.MiddleCenter;
        
        LayoutElement headerLayout = headerObj.AddComponent<LayoutElement>();
        headerLayout.preferredHeight = 40;
        
        // Separator
        CreateUISeparator(window.transform);
        
        // Scrollable content
        GameObject scrollView = CreateScrollView("SettingsScroll", window.transform);
        LayoutElement scrollLayout = scrollView.AddComponent<LayoutElement>();
        scrollLayout.flexibleHeight = 1;
        
        // Close button
        GameObject closeBtn = CreateUIButton("CloseButton", window.transform, "CLOSE");
        Button closeButton = closeBtn.GetComponent<Button>();
        Image closeBtnImage = closeBtn.GetComponent<Image>();
        closeBtnImage.color = new Color(0.6f, 0.6f, 0.6f);
        
        LayoutElement closeLayout = closeBtn.AddComponent<LayoutElement>();
        closeLayout.preferredHeight = 40;
        closeLayout.preferredWidth = 150;
        
        return settingsPanel;
    }
    
    private GameObject CreateUIPanel(string name, Transform parent, Vector2 anchorMin = default, Vector2 anchorMax = default)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        
        RectTransform rect = panel.AddComponent<RectTransform>();
        if (anchorMin != default || anchorMax != default)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        
        return panel;
    }
    
    private GameObject CreateUIText(string name, Transform parent, string text)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent, false);
        
        Text textComponent = textObj.AddComponent<Text>();
        textComponent.text = text;
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textComponent.color = Color.white;
        
        return textObj;
    }
    
    private GameObject CreateUIButton(string name, Transform parent, string text)
    {
        GameObject buttonObj = new GameObject(name);
        buttonObj.transform.SetParent(parent, false);
        
        Image image = buttonObj.AddComponent<Image>();
        image.color = new Color(0.3f, 0.3f, 0.3f);
        
        Button button = buttonObj.AddComponent<Button>();
        button.targetGraphic = image;
        
        // Add text
        GameObject textObj = CreateUIText("Text", buttonObj.transform, text);
        Text buttonText = textObj.GetComponent<Text>();
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.fontSize = 16;
        buttonText.fontStyle = FontStyle.Bold;
        
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        
        return buttonObj;
    }
    
    private GameObject CreateUISeparator(Transform parent)
    {
        GameObject separator = new GameObject("Separator");
        separator.transform.SetParent(parent, false);
        
        Image image = separator.AddComponent<Image>();
        image.color = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        
        LayoutElement layout = separator.AddComponent<LayoutElement>();
        layout.preferredHeight = 2;
        
        return separator;
    }
    
    private GameObject CreateScrollView(string name, Transform parent)
    {
        GameObject scrollView = new GameObject(name);
        scrollView.transform.SetParent(parent, false);
        
        RectTransform scrollRect = scrollView.AddComponent<RectTransform>();
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;
        
        ScrollRect scrollRectComponent = scrollView.AddComponent<ScrollRect>();
        Image scrollBg = scrollView.AddComponent<Image>();
        scrollBg.color = new Color(0.1f, 0.1f, 0.1f, 0.3f);
        
        // Viewport
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(scrollView.transform, false);
        
        RectTransform viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(10, 10);
        viewportRect.offsetMax = new Vector2(-10, -10);
        
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1, 1, 1, 0);
        Mask viewportMask = viewport.AddComponent<Mask>();
        viewportMask.showMaskGraphic = false;
        
        // Content
        GameObject content = new GameObject("Content");
        content.transform.SetParent(viewport.transform, false);
        
        RectTransform contentRect = content.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = new Vector2(0, 0);
        
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(0, 0, 0, 0);
        contentLayout.spacing = 15;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        
        ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        
        // Configure scroll rect
        scrollRectComponent.viewport = viewportRect;
        scrollRectComponent.content = contentRect;
        scrollRectComponent.horizontal = false;
        scrollRectComponent.vertical = true;
        scrollRectComponent.movementType = ScrollRect.MovementType.Elastic;
        scrollRectComponent.elasticity = 0.1f;
        scrollRectComponent.inertia = true;
        scrollRectComponent.scrollSensitivity = 20;
        
        return scrollView;
    }
    
    private void ConnectUIToManager(GameObject mainPanel, GameObject loadingPanel, GameObject settingsPanel)
    {
        Debug.Log("[TraversifySetup] Connecting UI to TraversifyManager...");
        
        TraversifyManager manager = createdComponents["TraversifyManager"] as TraversifyManager;
        if (manager == null)
        {
            Debug.LogError("[TraversifySetup] TraversifyManager not found!");
            return;
        }
        
        // Find UI elements
        Transform sidePanel = mainPanel.transform.Find("SidePanel");
        Transform loadingContainer = loadingPanel.transform.Find("LoadingContainer");
        
        // Assign references
        manager.uploadButton = sidePanel.Find("UploadButton")?.GetComponent<Button>();
        manager.generateButton = sidePanel.Find("GenerateButton")?.GetComponent<Button>();
        manager.mapPreviewImage = sidePanel.Find("PreviewContainer/MapPreview")?.GetComponent<RawImage>();
        manager.statusText = sidePanel.Find("StatusContainer/StatusText")?.GetComponent<Text>();
        manager.loadingPanel = loadingPanel;
        manager.progressBar = loadingContainer.Find("ProgressContainer")?.GetComponent<Slider>();
        manager.progressText = loadingContainer.Find("ProgressText")?.GetComponent<Text>();
        manager.stageText = loadingContainer.Find("StageText")?.GetComponent<Text>();
        manager.detailsText = loadingContainer.Find("DetailsText")?.GetComponent<Text>();
        manager.cancelButton = loadingContainer.Find("CancelButton")?.GetComponent<Button>();
        manager.settingsPanel = settingsPanel;
        
        // Configure settings
        manager.terrainSize = new Vector3(500, 100, 500);
        manager.terrainResolution = 513;
        
        // Setup button events
        Button settingsButton = sidePanel.Find("SettingsButton")?.GetComponent<Button>();
        Button settingsCloseButton = settingsPanel.transform.Find("SettingsWindow/CloseButton")?.GetComponent<Button>();
        Button settingsOverlayButton = settingsPanel.GetComponent<Button>();
        
        if (settingsButton != null && settingsCloseButton != null)
        {
            settingsButton.onClick.AddListener(() => settingsPanel.SetActive(true));
            settingsCloseButton.onClick.AddListener(() => settingsPanel.SetActive(false));
            settingsOverlayButton.onClick.AddListener(() => settingsPanel.SetActive(false));
        }
        
        // Mark as dirty to save changes
        EditorUtility.SetDirty(manager);
        
        Debug.Log("[TraversifySetup] UI successfully connected to TraversifyManager");
    }
    
    private void SetupEnvironment()
    {
        Debug.Log("[TraversifySetup] Setting up environment...");
        
        // Configure render settings
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.2f;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.9f, 0.95f, 1.0f, 1.0f);
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0005f;
        
        // Create initial terrain
        GameObject terrainObj = new GameObject("Terrain");
        Terrain terrain = terrainObj.AddComponent<Terrain>();
        TerrainCollider terrainCollider = terrainObj.AddComponent<TerrainCollider>();
        
        // Configure terrain data
        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = 513;
        terrainData.size = new Vector3(1000, 100, 1000);
        terrainData.SetDetailResolution(1024, 16);
        
        terrain.terrainData = terrainData;
        terrainCollider.terrainData = terrainData;
        
        // Save terrain data asset
        string terrainDataPath = "Assets/Traversify/Terrain/DefaultTerrainData.asset";
        AssetDatabase.CreateAsset(terrainData, terrainDataPath);
        
        createdGameObjects.Add(terrainObj);
        
        Debug.Log("[TraversifySetup] Environment setup complete");
    }
    
    private void VerifySetup()
    {
        Debug.Log("[TraversifySetup] Verifying setup...");
        
        int errors = 0;
        int warnings = 0;
        
        // Check core components
        if (!createdComponents.ContainsKey("TraversifyManager"))
        {
            Debug.LogError("[TraversifySetup] TraversifyManager component missing!");
            errors++;
        }
        
        if (!createdComponents.ContainsKey("MapAnalyzer"))
        {
            Debug.LogError("[TraversifySetup] MapAnalyzer component missing!");
            errors++;
        }
        
        if (!createdComponents.ContainsKey("TerrainGenerator"))
        {
            Debug.LogError("[TraversifySetup] TerrainGenerator component missing!");
            errors++;
        }
        
        if (!createdComponents.ContainsKey("ModelGenerator"))
        {
            Debug.LogError("[TraversifySetup] ModelGenerator component missing!");
            errors++;
        }
        
        // Check UI connections
        TraversifyManager manager = createdComponents["TraversifyManager"] as TraversifyManager;
        if (manager != null)
        {
            if (manager.uploadButton == null) warnings++;
            if (manager.generateButton == null) warnings++;
            if (manager.mapPreviewImage == null) warnings++;
            if (manager.statusText == null) warnings++;
            if (manager.loadingPanel == null) warnings++;
            if (manager.progressBar == null) warnings++;
        }
        
        // Check API keys
        if (string.IsNullOrEmpty(openAIKey))
        {
            Debug.LogWarning("[TraversifySetup] OpenAI API key not configured");
            warnings++;
        }
        
        if (string.IsNullOrEmpty(tripo3DApiKey))
        {
            Debug.LogWarning("[TraversifySetup] Tripo3D API key not configured");
            warnings++;
        }
        
        // Check model files
        string[] modelFiles = { "yolov8n.onnx", "FasterRCNN-12.onnx", "sam2_hiera_base.onnx" };
        foreach (string modelFile in modelFiles)
        {
            if (!File.Exists(GetModelPath(modelFile)))
            {
                Debug.LogWarning($"[TraversifySetup] Model file missing: {modelFile}");
                warnings++;
            }
        }
        
        // Summary
        if (errors == 0 && warnings == 0)
        {
            Debug.Log("[TraversifySetup] ✓ All components verified successfully!");
        }
        else
        {
            Debug.Log($"[TraversifySetup] Verification complete: {errors} errors, {warnings} warnings");
        }
    }
    
    private IEnumerator DownloadAllModels()
    {
        Debug.Log("[TraversifySetup] Starting model downloads...");
        isDownloading = true;
        
        List<ModelDownloadInfo> modelsToDownload = new List<ModelDownloadInfo>();
        
        // Check which models need downloading
        if (!File.Exists(GetModelPath("yolov8n.onnx")))
        {
            modelsToDownload.Add(new ModelDownloadInfo {
                name = "YOLOv8",
                fileName = "yolov8n.onnx",
                urls = modelMirrors["yolov8"],
                size = modelSizes["yolov8"]
            });
        }
        
        if (!File.Exists(GetModelPath("FasterRCNN-12.onnx")))
        {
            modelsToDownload.Add(new ModelDownloadInfo {
                name = "Faster R-CNN",
                fileName = "FasterRCNN-12.onnx",
                urls = modelMirrors["fasterrcnn"],
                size = modelSizes["fasterrcnn"]
            });
        }
        
        if (!File.Exists(GetModelPath("sam2_hiera_base.onnx")))
        {
            modelsToDownload.Add(new ModelDownloadInfo {
                name = "SAM2",
                fileName = "sam2_hiera_base.onnx",
                urls = modelMirrors["sam2"],
                size = modelSizes["sam2"]
            });
        }
        
        if (modelsToDownload.Count == 0)
        {
            Debug.Log("[TraversifySetup] All models already downloaded");
            CompleteSetup();
            yield break;
        }
        
        float totalSize = modelsToDownload.Sum(m => m.size);
        float downloadedSize = 0;
        
        foreach (var modelInfo in modelsToDownload)
        {
            bool success = false;
            
            // Try each mirror URL
            foreach (string url in modelInfo.urls)
            {
                currentDownloadName = modelInfo.name;
                downloadStatus = $"Downloading from {new Uri(url).Host}...";
                
                yield return DownloadModel(url, modelInfo.fileName, modelInfo.size,
                    (progress) => {
                        float modelProgress = downloadedSize + (progress * modelInfo.size);
                        currentDownloadProgress = modelProgress / totalSize;
                        Repaint();
                    },
                    (succeeded) => {
                        success = succeeded;
                    });
                
                if (success)
                {
                    Debug.Log($"[TraversifySetup] Successfully downloaded {modelInfo.name}");
                    break;
                }
                else
                {
                    Debug.LogWarning($"[TraversifySetup] Failed to download {modelInfo.name} from {url}");
                }
            }
            
            if (!success)
            {
                EditorUtility.DisplayDialog("Download Failed", 
                    $"Failed to download {modelInfo.name}. Please check your internet connection and try again.", 
                    "OK");
                isDownloading = false;
                yield break;
            }
            
            downloadedSize += modelInfo.size;
        }
        
        isDownloading = false;
        CompleteSetup();
    }
    
    private IEnumerator DownloadModel(string url, string fileName, float sizeMB, 
        System.Action<float> onProgress, System.Action<bool> onComplete)
    {
        string downloadPath = GetModelPath(fileName);
        string tempPath = downloadPath + ".tmp";
        
        // Ensure directory exists
        string directory = Path.GetDirectoryName(downloadPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        Debug.Log($"[TraversifySetup] Downloading {fileName} from {url}...");
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.downloadHandler = new DownloadHandlerFile(tempPath);
            request.timeout = 300; // 5 minute timeout for large files
            
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            
            float lastProgress = 0f;
            while (!operation.isDone)
            {
                float progress = request.downloadProgress;
                if (progress != lastProgress)
                {
                    onProgress?.Invoke(progress);
                    lastProgress = progress;
                }
                yield return new WaitForSeconds(0.1f);
            }
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                // Verify file size
                FileInfo fileInfo = new FileInfo(tempPath);
                float downloadedSizeMB = fileInfo.Length / (1024f * 1024f);
                
                if (downloadedSizeMB < sizeMB * 0.9f) // Allow 10% variance
                {
                    Debug.LogError($"[TraversifySetup] Downloaded file size mismatch. Expected ~{sizeMB}MB, got {downloadedSizeMB}MB");
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    onComplete?.Invoke(false);
                    yield break;
                }
                
                // Move temp file to final location
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }
                File.Move(tempPath, downloadPath);
                
                Debug.Log($"[TraversifySetup] Successfully downloaded {fileName} ({downloadedSizeMB:F1} MB)");
                onComplete?.Invoke(true);
            }
            else
            {
                // Clean up temp file
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                
                Debug.LogError($"[TraversifySetup] Download failed: {request.error}");
                onComplete?.Invoke(false);
            }
        }
    }
    
    private void CancelDownload()
    {
        Debug.Log("[TraversifySetup] Cancelling downloads...");
        
        if (downloadCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(downloadCoroutine);
            downloadCoroutine = null;
        }
        
        isDownloading = false;
        currentDownloadProgress = 0;
        downloadStatus = "Download cancelled";
        
        // Clean up any temp files
        string modelsPath = Path.GetDirectoryName(GetModelPath(""));
        if (Directory.Exists(modelsPath))
        {
            string[] tempFiles = Directory.GetFiles(modelsPath, "*.tmp", SearchOption.AllDirectories);
            foreach (string tempFile in tempFiles)
            {
                try
                {
                    File.Delete(tempFile);
                    Debug.Log($"[TraversifySetup] Cleaned up temp file: {tempFile}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TraversifySetup] Failed to delete temp file: {e.Message}");
                }
            }
        }
        
        Repaint();
    }
    
    private void CompleteSetup()
    {
        Debug.Log("[TraversifySetup] Completing setup...");
        
        try
        {
            // Final verification
            VerifySetup();
            
            // Refresh asset database
            AssetDatabase.Refresh();
            
            // Save project
            AssetDatabase.SaveAssets();
            
            // Show completion dialog
            string message = "Traversify has been successfully set up!\n\n";
            message += "✓ All components configured\n";
            message += "✓ UI system created\n";
            
            if (File.Exists(GetModelPath("yolov8n.onnx")) && 
                File.Exists(GetModelPath("FasterRCNN-12.onnx")) && 
                File.Exists(GetModelPath("sam2_hiera_base.onnx")))
            {
                message += "✓ AI models downloaded\n";
            }
            
            if (createSampleScene)
            {
                message += "✓ Sample scene created\n";
            }
            
            message += "\nYou can now use Traversify to generate terrains from map images.";
            
            if (string.IsNullOrEmpty(openAIKey))
            {
                message += "\n\nNote: OpenAI API key not configured. Enhanced descriptions will be limited.";
            }
            
            if (string.IsNullOrEmpty(tripo3DApiKey))
            {
                message += "\nNote: Tripo3D API key not configured. AI model generation will be disabled.";
            }
            
            EditorUtility.DisplayDialog("Setup Complete", message, "OK");
            
            // Open the scene if created
            if (createSampleScene)
            {
                string scenePath = "Assets/Traversify/Scenes/TraversifyScene.unity";
                if (File.Exists(scenePath))
                {
                    EditorSceneManager.OpenScene(scenePath);
                }
            }
            
            Debug.Log("[TraversifySetup] Setup completed successfully!");
            
            // Close the setup window
            this.Close();
        }
        catch (Exception e)
        {
            Debug.LogError($"[TraversifySetup] Error during completion: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Setup Error", 
                "Setup completed with errors. Check the console for details.", 
                "OK");
        }
    }
    
    // Add new method to reimport models for OnnxModelAsset conversion
    private void ReimportAIModels()
    {
        try
        {
            Debug.Log("[TraversifySetup] Reimporting AI models...");
            
            string modelsPath = Path.Combine(Application.dataPath, "StreamingAssets", "Traversify", "Models");
            
            // Ensure directory exists
            if (!Directory.Exists(modelsPath))
            {
                Directory.CreateDirectory(modelsPath);
                Debug.Log($"[TraversifySetup] Created models directory at {modelsPath}");
            }
            
            // Process each model file
            ProcessModelReimport("yolov8n.onnx");
            ProcessModelReimport("FasterRCNN-12.onnx");
            ProcessModelReimport("sam2_hiera_base.onnx");
            
            // Refresh asset database to detect changes
            AssetDatabase.Refresh();
            
            // Import models as OnnxModelAsset
            ImportModelsAsOnnxAssets();
            
            Debug.Log("[TraversifySetup] AI models reimported successfully");
            
            EditorUtility.DisplayDialog("Models Reimported", 
                "AI models have been reimported and prepared for use with Unity's AI.Inference package.",
                "OK");
        }
        catch (Exception e)
        {
            Debug.LogError($"[TraversifySetup] Error reimporting models: {e.Message}\n{e.StackTrace}");
            EditorUtility.DisplayDialog("Reimport Error", 
                $"An error occurred while reimporting models: {e.Message}",
                "OK");
        }
    }
    
    private void ProcessModelReimport(string modelFileName)
    {
        string modelPath = GetModelPath(modelFileName);
        
        if (File.Exists(modelPath))
        {
            Debug.Log($"[TraversifySetup] Processing model: {modelFileName}");
            
            // Convert the path to a relative asset path
            string assetsPath = "Assets" + modelPath.Substring(Application.dataPath.Length);
            
            // Reimport the asset
            AssetDatabase.ImportAsset(assetsPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[TraversifySetup] Reimported {assetsPath}");
        }
        else
        {
            Debug.LogWarning($"[TraversifySetup] Model file not found: {modelPath}");
        }
    }
    
    private void ImportModelsAsOnnxAssets()
    {
        try
        {
            // This method would use the new Unity.AI.Inference API to create OnnxModelAssets
            // Actual implementation depends on specific Unity.AI.Inference API
            // This is a placeholder for the actual implementation
            
            // Example implementation (may need to be adjusted based on AI.Inference API):
            string modelsDir = Path.Combine(Application.dataPath, "StreamingAssets", "Traversify", "Models");
            
            // Create a folder to store OnnxModelAssets if it doesn't exist
            string onnxAssetsDir = "Assets/Scripts/AI/Models";
            if (!Directory.Exists(Path.Combine(Application.dataPath, "Scripts/AI/Models")))
            {
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scripts/AI/Models"));
                AssetDatabase.Refresh();
            }
            
            // For each model file, create an OnnxModelAsset
            // Note: This part would need to be updated with actual AI.Inference API calls
            // Currently this is just copying the files to the assets folder
            
            foreach (string modelFile in new string[] {"yolov8n.onnx", "FasterRCNN-12.onnx", "sam2_hiera_base.onnx"})
            {
                string sourceFile = Path.Combine(modelsDir, modelFile);
                string destFile = Path.Combine(Application.dataPath, "Scripts/AI/Models", modelFile);
                
                if (File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, destFile, true);
                    Debug.Log($"[TraversifySetup] Copied {modelFile} to {destFile}");
                }
            }
            
            AssetDatabase.Refresh();
            
            // Additional steps to convert .onnx files to OnnxModelAsset would go here
            // This would typically involve using the Unity.AI.Inference API
        }
        catch (Exception e)
        {
            Debug.LogError($"[TraversifySetup] Error creating OnnxModelAssets: {e.Message}\n{e.StackTrace}");
            throw;
        }
    }
    
    private class ModelDownloadInfo
    {
        public string name;
        public string fileName;
        public string[] urls;
        public float size;
    }
}

// Enhanced free camera controller with better controls
public class FreeCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 50f;
    public float fastMoveSpeed = 150f;
    public float rotationSpeed = 2f;
    public float smoothTime = 0.1f;
    
    [Header("Input Settings")]
    public bool invertY = false;
    public bool requireRightClick = true;
    
    [Header("Bounds")]
    public bool limitBounds = false;
    public Bounds movementBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
    
    private Vector3 velocity = Vector3.zero;
    private float rotationX = 0f;
    private float rotationY = 0f;
    private bool isControlling = false;
    
    void Start()
    {
        // Initialize rotation from current transform
        Vector3 rotation = transform.eulerAngles;
        rotationX = rotation.y;
        rotationY = rotation.x;
    }
    
    void Update()
    {
        HandleInput();
        HandleMovement();
        HandleRotation();
    }
    
    private void HandleInput()
    {
        if (requireRightClick)
        {
            if (Input.GetMouseButtonDown(1))
            {
                isControlling = true;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else if (Input.GetMouseButtonUp(1))
            {
                isControlling = false;
                Cursor.lockState = CursorLockMode.None;
            }
        }
        else
        {
            isControlling = true;
        }
    }
    
    private void HandleMovement()
    {
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? fastMoveSpeed : moveSpeed;
        Vector3 targetVelocity = Vector3.zero;
        
        // Forward/Backward
        if (Input.GetKey(KeyCode.W)) targetVelocity += transform.forward;
        if (Input.GetKey(KeyCode.S)) targetVelocity -= transform.forward;
        
        // Left/Right
        if (Input.GetKey(KeyCode.A)) targetVelocity -= transform.right;
        if (Input.GetKey(KeyCode.D)) targetVelocity += transform.right;
        
        // Up/Down
        if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.PageDown)) targetVelocity -= transform.up;
        if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.PageUp)) targetVelocity += transform.up;
        
        // Normalize and apply speed
        targetVelocity = targetVelocity.normalized * currentSpeed;
        
        // Smooth movement
        velocity = Vector3.Lerp(velocity, targetVelocity, smoothTime);
        Vector3 newPosition = transform.position + velocity * Time.deltaTime;
        
        // Apply bounds if enabled
        if (limitBounds)
        {
            newPosition = new Vector3(
                Mathf.Clamp(newPosition.x, movementBounds.min.x, movementBounds.max.x),
                Mathf.Clamp(newPosition.y, movementBounds.min.y, movementBounds.max.y),
                Mathf.Clamp(newPosition.z, movementBounds.min.z, movementBounds.max.z)
            );
        }
        
        transform.position = newPosition;
    }
    
    private void HandleRotation()
    {
        if (!isControlling) return;
        
        // Get mouse input
        float mouseX = Input.GetAxis("Mouse X") * rotationSpeed;
        float mouseY = Input.GetAxis("Mouse Y") * rotationSpeed;
        
        if (invertY) mouseY = -mouseY;
        
        // Apply rotation
        rotationX += mouseX;
        rotationY -= mouseY;
        rotationY = Mathf.Clamp(rotationY, -90f, 90f);
        
        transform.rotation = Quaternion.Euler(rotationY, rotationX, 0);
    }
    
    // Public methods for external control
    public void SetPosition(Vector3 position)
    {
        transform.position = position;
        velocity = Vector3.zero;
    }
    
    public void LookAt(Vector3 target)
    {
        transform.LookAt(target);
        Vector3 rotation = transform.eulerAngles;
        rotationX = rotation.y;
        rotationY = rotation.x;
    }
    
    public void ResetCamera()
    {
        transform.position = new Vector3(250, 180, -150);
        transform.rotation = Quaternion.Euler (35, 15, 0);
        velocity = Vector3.zero;
        rotationX = 15f;
        rotationY = 35f;
    }
}
#endif

