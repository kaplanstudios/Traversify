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
using UnityEditor.SceneManagement; // Add this for EditorSceneManager
using Traversify; // Add this to access Traversify namespace classes

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
        "Assets/StreamingAssets/Traversify/Models"
    };
    
    private Dictionary<string, bool> dependencyStatus = new Dictionary<string, bool>();
    
    [MenuItem("Tools/Traversify/Setup Wizard")]
    public static void ShowWindow()
    {
        var window = GetWindow<TraversifySetup>("Traversify Setup Wizard");
        window.minSize = new Vector2(700, 800);
        window.position = new Rect(100, 100, 700, 800);
    }
    
    private void OnEnable()
    {
        // Try to load saved API key
        openAIKey = EditorPrefs.GetString("TraversifyOpenAIKey", "");
        
        // Check dependencies
        CheckDependencies();
    }
    
    private void OnDisable()
    {
        if (downloadCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(downloadCoroutine);
        }
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
        // Check for Barracuda
        dependencyStatus["Barracuda"] = IsPackageInstalled("com.unity.barracuda");
        
        // Check for TextMeshPro
        dependencyStatus["TextMeshPro"] = IsPackageInstalled("com.unity.textmeshpro");
        
        // Check for Terrain Tools
        dependencyStatus["TerrainTools"] = IsPackageInstalled("com.unity.terrain-tools");
        
        // Check for Editor Coroutines
        dependencyStatus["EditorCoroutines"] = IsPackageInstalled("com.unity.editorcoroutines");
    }
    
    private bool IsPackageInstalled(string packageId)
    {
        var listRequest = UnityEditor.PackageManager.Client.List();
        while (!listRequest.IsCompleted) { }
        
        if (listRequest.Status == UnityEditor.PackageManager.StatusCode.Success)
        {
            foreach (var package in listRequest.Result)
            {
                if (package.packageId.StartsWith(packageId))
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    private void OnGUI()
    {
        if (headerStyle == null)
        {
            InitializeStyles();
        }
        
        // Begin the scroll view outside the try-catch block
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
            EditorGUILayout.LabelField("Traversify uses state-of-the-art AI models (Faster R-CNN, YOLOv8, and SAM2) to analyze map images and generate detailed 3D terrains with accurately placed objects.", paragraphStyle);
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
            // If an exception occurred during GUI rendering, report it
            Debug.LogError($"Error in TraversifySetup GUI: {e.Message}\n{e.StackTrace}");
            
            // Show error message inside the scroll view
            EditorGUILayout.HelpBox("An error occurred while rendering the UI. Check the console for details.", MessageType.Error);
        }
        
        // Always end the scroll view, even if an exception occurred
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
        
        EditorGUILayout.LabelField("OpenAI API key is required for enhanced object descriptions and intelligent terrain analysis.", paragraphStyle);
        EditorGUILayout.Space(5);
        
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("OpenAI API Key:", GUILayout.Width(150));
        openAIKey = EditorGUILayout.PasswordField(openAIKey);
        EditorGUILayout.EndHorizontal();
        
        if (string.IsNullOrEmpty(openAIKey))
        {
            EditorGUILayout.HelpBox("Without an OpenAI API key, the system will use basic object descriptions.", MessageType.Info);
            
            if (GUILayout.Button("Get OpenAI API Key"))
            {
                Application.OpenURL("https://platform.openai.com/api-keys");
            }
        }
        else
        {
            GUI.color = successColor;
            EditorGUILayout.LabelField("✓ API key configured");
            GUI.color = Color.white;
        }
        
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
            case "Barracuda":
                packageId = "com.unity.barracuda@3.0.0";
                break;
            case "TextMeshPro":
                packageId = "com.unity.textmeshpro@3.0.6";
                break;
            case "TerrainTools":
                packageId = "com.unity.terrain-tools@4.0.5";
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
        // Try multiple locations
        string[] possiblePaths = new string[]
        {
            Path.Combine(Application.dataPath, "Scripts", "AI", "Models", modelFileName),
            Path.Combine(Application.dataPath, "StreamingAssets", "Traversify", "Models", modelFileName),
            Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", modelFileName)
        };
        
        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        
        // Return the preferred path even if file doesn't exist
        return possiblePaths[1];
    }
    
    private void RunSetup()
    {
        try
        {
            // Save API key
            EditorPrefs.SetString("TraversifyOpenAIKey", openAIKey);
            
            // Create required folders
            CreateRequiredFolders();
            
            // Set up components
            if (setupUI) SetupUI();
            if (setupAIModels) SetupAIModels();
            if (setupTerrain) SetupTerrain();
            
            // Create sample scene
            if (createSampleScene) CreateSampleScene();
            
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
            Debug.LogError($"Traversify setup error: {e}");
        }
    }
    
    private void CreateRequiredFolders()
    {
        foreach (string folder in requiredFolders)
        {
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
                Debug.Log($"Created directory: {folder}");
            }
        }
        
        AssetDatabase.Refresh();
    }
    private void SetupUI()
    {
        Debug.Log("[Traversify] Setting up UI components...");
        
        // Find or create canvas
        Canvas mainCanvas = FindObjectOfType<Canvas>();
        GameObject canvasObject;
        
        if (mainCanvas == null)
        {
            canvasObject = new GameObject("TraversifyCanvas");
            mainCanvas = canvasObject.AddComponent<Canvas>();
            mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasObject.AddComponent<GraphicRaycaster>();
        }
        else
        {
            canvasObject = mainCanvas.gameObject;
        }
        
        // Create UI hierarchy
        GameObject uiRoot = new GameObject("TraversifyUI");
        uiRoot.transform.SetParent(canvasObject.transform, false);
        
        // Set up UI panels and components
        SetupMainPanel(uiRoot.transform);
        SetupLoadingPanel(uiRoot.transform);
        SetupSettingsPanel(uiRoot.transform);
        
        // Set up the controller object
        GameObject controllerObj = new GameObject("TraversifyController");
        TraversifyMain controller = controllerObj.AddComponent<TraversifyMain>();
        
        // Add required components
        controllerObj.AddComponent<Traversify.Core.TraversifyDebugger>();
        
        // Connect UI references
        ConnectUIReferences(controller, uiRoot);
        
        Debug.Log("[Traversify] UI setup complete");
    }
    
    private void SetupMainPanel(Transform parent)
    {
        GameObject mainPanel = CreateUIPanel("MainPanel", parent, new Vector2(0, 0), new Vector2(1, 1));
        
        // Background
        Image bgImage = mainPanel.AddComponent<Image>();
        bgImage.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);
        
        // Create side panel for controls
        GameObject sidePanel = CreateUIPanel("SidePanel", mainPanel.transform, 
            new Vector2(0, 0), new Vector2(0.25f, 1));
        
        Image sidePanelBg = sidePanel.AddComponent<Image>();
        sidePanelBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        
        // Add vertical layout group to side panel
        VerticalLayoutGroup sideLayout = sidePanel.AddComponent<VerticalLayoutGroup>();
        sideLayout.padding = new RectOffset(20, 20, 20, 20);
        sideLayout.spacing = 15;
        sideLayout.childForceExpandWidth = true;
        sideLayout.childForceExpandHeight = false;
        
        // Title
        GameObject titleObj = CreateUIText("Title", sidePanel.transform, "TRAVERSIFY");
        Text titleText = titleObj.GetComponent<Text>();
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 32;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.fontStyle = FontStyle.Bold;
        titleText.color = new Color(0.9f, 0.9f, 0.9f);
        
        LayoutElement titleLayout = titleObj.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 60;
        
        // Subtitle
        GameObject subtitleObj = CreateUIText("Subtitle", sidePanel.transform, "AI-Powered Terrain Generation");
        Text subtitleText = subtitleObj.GetComponent<Text>();
        subtitleText.fontSize = 14;
        subtitleText.alignment = TextAnchor.MiddleCenter;
        subtitleText.color = new Color(0.7f, 0.7f, 0.7f);
        
        LayoutElement subtitleLayout = subtitleObj.AddComponent<LayoutElement>();
        subtitleLayout.preferredHeight = 30;
        
        // Separator
        CreateUISeparator(sidePanel.transform);
        
        // Map preview container
        GameObject previewContainer = CreateUIPanel("PreviewContainer", sidePanel.transform);
        LayoutElement previewContainerLayout = previewContainer.AddComponent<LayoutElement>();
        previewContainerLayout.preferredHeight = 300;
        
        // Map preview background
        Image previewBg = previewContainer.AddComponent<Image>();
        previewBg.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        
        // Map preview image
        GameObject mapPreviewObj = new GameObject("MapPreview");
        mapPreviewObj.transform.SetParent(previewContainer.transform, false);
        
        RectTransform previewRect = mapPreviewObj.AddComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0.05f, 0.05f);
        previewRect.anchorMax = new Vector2(0.95f, 0.95f);
        previewRect.offsetMin = Vector2.zero;
        previewRect.offsetMax = Vector2.zero;
        
        RawImage mapPreview = mapPreviewObj.AddComponent<RawImage>();
        mapPreview.color = new Color(0.3f, 0.3f, 0.3f, 1);
        
        // Add aspect ratio fitter to maintain image proportions
        AspectRatioFitter aspectFitter = mapPreviewObj.AddComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        
        // Upload button
        GameObject uploadBtn = CreateUIButton("UploadButton", sidePanel.transform, "UPLOAD MAP");
        Button uploadButton = uploadBtn.GetComponent<Button>();
        Image uploadBtnImage = uploadBtn.GetComponent<Image>();
        uploadBtnImage.color = new Color(0.2f, 0.5f, 0.9f);
        
        LayoutElement uploadLayout = uploadBtn.AddComponent<LayoutElement>();
        uploadLayout.preferredHeight = 50;
        
        // Generate button
        GameObject generateBtn = CreateUIButton("GenerateButton", sidePanel.transform, "GENERATE TERRAIN");
        Button generateButton = generateBtn.GetComponent<Button>();
        Image generateBtnImage = generateBtn.GetComponent<Image>();
        generateBtnImage.color = new Color(0.2f, 0.8f, 0.4f);
        generateButton.interactable = false;
        
        LayoutElement generateLayout = generateBtn.AddComponent<LayoutElement>();
        generateLayout.preferredHeight = 50;
        
        // Settings button
        GameObject settingsBtn = CreateUIButton("SettingsButton", sidePanel.transform, "SETTINGS");
        Button settingsButton = settingsBtn.GetComponent<Button>();
        Image settingsBtnImage = settingsBtn.GetComponent<Image>();
        settingsBtnImage.color = new Color(0.6f, 0.6f, 0.6f);
        
        LayoutElement settingsLayout = settingsBtn.AddComponent<LayoutElement>();
        settingsLayout.preferredHeight = 40;
        
        // Separator
        CreateUISeparator(sidePanel.transform);
        
        // Status text container
        GameObject statusContainer = CreateUIPanel("StatusContainer", sidePanel.transform);
        LayoutElement statusContainerLayout = statusContainer.AddComponent<LayoutElement>();
        statusContainerLayout.preferredHeight = 100;
        statusContainerLayout.flexibleHeight = 1;
        
        // Status text background
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
        
        // Create content area (right side)
        GameObject contentArea = CreateUIPanel("ContentArea", mainPanel.transform, 
            new Vector2(0.25f, 0), new Vector2(1, 1));
        
        // Add padding to content area
        VerticalLayoutGroup contentLayout = contentArea.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(20, 20, 20, 20);
        
        // Create viewport for 3D preview or additional content
        GameObject viewport = CreateUIPanel("Viewport", contentArea.transform);
        Image viewportBg = viewport.AddComponent<Image>();
        viewportBg.color = new Color(0.08f, 0.08f, 0.08f, 1f);
        
        LayoutElement viewportLayout = viewport.AddComponent<LayoutElement>();
        viewportLayout.flexibleWidth = 1;
        viewportLayout.flexibleHeight = 1;
    }
    
    private void SetupLoadingPanel(Transform parent)
    {
        GameObject loadingPanel = CreateUIPanel("LoadingPanel", parent, new Vector2(0, 0), new Vector2(1, 1));
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
        
        // Loading title
        GameObject titleObj = CreateUIText("LoadingTitle", container.transform, "PROCESSING");
        Text titleText = titleObj.GetComponent<Text>();
        titleText.fontSize = 28;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        
        LayoutElement titleLayout = titleObj.AddComponent<LayoutElement>();
        titleLayout.preferredHeight = 40;
        
        // Stage text
        GameObject stageObj = CreateUIText("StageText", container.transform, "Initializing...");
        Text stageText = stageObj.GetComponent<Text>();
        stageText.fontSize = 18;
        stageText.alignment = TextAnchor.MiddleCenter;
        stageText.color = new Color(0.8f, 0.8f, 0.8f);
        
        LayoutElement stageLayout = stageObj.AddComponent<LayoutElement>();
        stageLayout.preferredHeight = 30;
        
        // Progress bar container
        GameObject progressContainer = CreateUIPanel("ProgressContainer", container.transform);
        LayoutElement progressContainerLayout = progressContainer.AddComponent<LayoutElement>();
        progressContainerLayout.preferredHeight = 30;
        
        // Progress bar background
        Image progressBg = progressContainer.AddComponent<Image>();
        progressBg.color = new Color(0.1f, 0.1f, 0.1f, 1f);
        
        // Progress bar fill
        GameObject progressFill = CreateUIPanel("ProgressFill", progressContainer.transform, 
            new Vector2(0, 0), new Vector2(0.5f, 1));
        
        Image fillImage = progressFill.AddComponent<Image>();
        fillImage.color = new Color(0.2f, 0.7f, 1f);
        
        // Create slider component
        Slider progressBar = progressContainer.AddComponent<Slider>();
        progressBar.fillRect = progressFill.GetComponent<RectTransform>();
        progressBar.targetGraphic = progressBg;
        progressBar.minValue = 0;
        progressBar.maxValue = 1;
        progressBar.value = 0;
        
        // Progress text
        GameObject progressTextObj = CreateUIText("ProgressText", container.transform, "0%");
        Text progressText = progressTextObj.GetComponent<Text>();
        progressText.fontSize = 16;
        progressText.alignment = TextAnchor.MiddleCenter;
        
        LayoutElement progressTextLayout = progressTextObj.AddComponent<LayoutElement>();
        progressTextLayout.preferredHeight = 25;
        
        // Details text
        GameObject detailsObj = CreateUIText("DetailsText", container.transform, "");
        Text detailsText = detailsObj.GetComponent<Text>();
        detailsText.fontSize = 14;
        detailsText.alignment = TextAnchor.MiddleCenter;
        detailsText.color = new Color(0.6f, 0.6f, 0.6f);
        
        LayoutElement detailsLayout = detailsObj.AddComponent<LayoutElement>();
        detailsLayout.preferredHeight = 60;
        detailsLayout.flexibleHeight = 1;
        
        // Cancel button
        GameObject cancelBtn = CreateUIButton("CancelButton", container.transform, "CANCEL");
        Button cancelButton = cancelBtn.GetComponent<Button>();
        Image cancelBtnImage = cancelBtn.GetComponent<Image>();
        cancelBtnImage.color = new Color(0.8f, 0.3f, 0.3f);
        
        LayoutElement cancelLayout = cancelBtn.AddComponent<LayoutElement>();
        cancelLayout.preferredHeight = 40;
        cancelLayout.preferredWidth = 150;
    }
    
    private void SetupSettingsPanel(Transform parent)
    {
        GameObject settingsPanel = CreateUIPanel("SettingsPanel", parent, new Vector2(0, 0), new Vector2(1, 1));
        settingsPanel.SetActive(false);
        
        // Dark overlay
        Image overlay = settingsPanel.AddComponent<Image>();
        overlay.color = new Color(0, 0, 0, 0.8f);
        
        // Add button to close settings when clicking overlay
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
        
        // Get content transform from scroll view
        Transform content = scrollView.transform.Find("Viewport/Content");
        
        // Add settings sections
        CreateTerrainSettingsSection(content);
        CreateAISettingsSection(content);
        CreatePerformanceSettingsSection(content);
        
        // Close button
        GameObject closeBtn = CreateUIButton("CloseButton", window.transform, "CLOSE");
        Button closeButton = closeBtn.GetComponent<Button>();
        Image closeBtnImage = closeBtn.GetComponent<Image>();
        closeBtnImage.color = new Color(0.6f, 0.6f, 0.6f);
        
        LayoutElement closeLayout = closeBtn.AddComponent<LayoutElement>();
        closeLayout.preferredHeight = 40;
        closeLayout.preferredWidth = 150;
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
        
        // Make sure we have a RectTransform on the scroll view
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
        
        // Configure scroll rect component
        scrollRectComponent.viewport = viewportRect;
        scrollRectComponent.content = contentRect;
        scrollRectComponent.horizontal = false;
        scrollRectComponent.vertical = true;
        scrollRectComponent.movementType = ScrollRect.MovementType.Elastic;
        scrollRectComponent.elasticity = 0.1f;
        scrollRectComponent.inertia = true;
        scrollRectComponent.scrollSensitivity = 20;
        
        // Add scrollbar
        GameObject scrollbar = new GameObject("Scrollbar");
        scrollbar.transform.SetParent(scrollView.transform, false);
        
        RectTransform scrollbarRect = scrollbar.AddComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1, 0);
        scrollbarRect.anchorMax = new Vector2(1, 1);
        scrollbarRect.pivot = new Vector2(1, 0.5f);
        scrollbarRect.sizeDelta = new Vector2(10, 0);
        scrollbarRect.anchoredPosition = new Vector2(-5, 0);
        
        Image scrollbarBg = scrollbar.AddComponent<Image>();
        scrollbarBg.color = new Color(0.05f, 0.05f, 0.05f, 0.5f);
        
        Scrollbar scrollbarComponent = scrollbar.AddComponent<Scrollbar>();
        scrollbarComponent.direction = Scrollbar.Direction.BottomToTop;
        
        // Scrollbar handle
        GameObject handle = new GameObject("Handle");
        handle.transform.SetParent(scrollbar.transform, false);
        
        RectTransform handleRect = handle.AddComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(0.4f, 0.4f, 0.4f, 0.8f);
        
        scrollbarComponent.targetGraphic = handleImage;
        scrollbarComponent.handleRect = handleRect;
        
        scrollRectComponent.verticalScrollbar = scrollbarComponent;
        scrollRectComponent.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        
        return scrollView;
    }
    private void CreateTerrainSettingsSection(Transform parent)
    {
        GameObject section = CreateSettingsSection("Terrain Settings", parent);
        Transform content = section.transform.Find("Content");
        
        // Terrain size
        CreateSliderSetting(content, "Terrain Size", "terrainSize", 100, 2000, 500);
        
        // Terrain resolution
        CreateDropdownSetting(content, "Terrain Resolution", "terrainResolution", 
            new string[] { "129", "257", "513", "1025", "2049" }, 2);
        
        // Height multiplier
        CreateSliderSetting(content, "Height Multiplier", "heightMultiplier", 1, 100, 30);
        
        // Water generation
        CreateToggleSetting(content, "Generate Water", "generateWater", true);
        
        // Water height
        CreateSliderSetting(content, "Water Height", "waterHeight", 0, 0.5f, 0.1f);
    }
    
    private void CreateAISettingsSection(Transform parent)
    {
        GameObject section = CreateSettingsSection("AI Settings", parent);
        Transform content = section.transform.Find("Content");
        
        // Model quality
        CreateDropdownSetting(content, "Analysis Quality", "analysisQuality", 
            new string[] { "Fast", "Balanced", "High Quality" }, 1);
        
        // Max objects
        CreateSliderSetting(content, "Max Objects to Process", "maxObjects", 10, 200, 100);
        
        // Object grouping
        CreateToggleSetting(content, "Group Similar Objects", "groupObjects", true);
        
        // Grouping threshold
        CreateSliderSetting(content, "Grouping Distance", "groupingDistance", 0.01f, 0.5f, 0.1f);
        
        // Use OpenAI
        CreateToggleSetting(content, "Use OpenAI Enhancement", "useOpenAI", true);
    }
    
    private void CreatePerformanceSettingsSection(Transform parent)
    {
        GameObject section = CreateSettingsSection("Performance Settings", parent);
        Transform content = section.transform.Find("Content");
        
        // Batch size
        CreateSliderSetting(content, "Processing Batch Size", "batchSize", 1, 10, 5);
        
        // GPU acceleration
        CreateToggleSetting(content, "Use GPU Acceleration", "useGPU", true);
        
        // Memory limit
        CreateDropdownSetting(content, "Memory Limit", "memoryLimit", 
            new string[] { "2 GB", "4 GB", "8 GB", "16 GB", "Unlimited" }, 2);
        
        // Debug logging
        CreateToggleSetting(content, "Enable Debug Logging", "debugLogging", false);
    }
    
    private GameObject CreateSettingsSection(string title, Transform parent)
    {
        GameObject section = new GameObject($"{title} Section");
        section.transform.SetParent(parent, false);
        
        VerticalLayoutGroup layout = section.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 10, 10);
        layout.spacing = 10;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        
        // Section header
        GameObject headerObj = CreateUIText("Header", section.transform, title.ToUpper());
        Text headerText = headerObj.GetComponent<Text>();
        headerText.fontSize = 18;
        headerText.fontStyle = FontStyle.Bold;
        headerText.color = new Color(0.8f, 0.8f, 0.8f);
        
        LayoutElement headerLayout = headerObj.AddComponent<LayoutElement>();
        headerLayout.preferredHeight = 30;
        
        // Separator
        CreateUISeparator(section.transform);
        
        // Content container
        GameObject content = new GameObject("Content");
        content.transform.SetParent(section.transform, false);
        
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(20, 0, 0, 0);
        contentLayout.spacing = 8;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        
        return section;
    }
    
    private void CreateSliderSetting(Transform parent, string label, string key, float min, float max, float defaultValue)
    {
        GameObject setting = new GameObject($"{label} Setting");
        setting.transform.SetParent(parent, false);
        
        HorizontalLayoutGroup layout = setting.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        
        LayoutElement settingLayout = setting.AddComponent<LayoutElement>();
        settingLayout.preferredHeight = 25;
        
        // Label
        GameObject labelObj = CreateUIText("Label", setting.transform, label);
        Text labelText = labelObj.GetComponent<Text>();
        labelText.fontSize = 14;
        labelText.color = new Color(0.8f, 0.8f, 0.8f);
        
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;
        
        // Slider
        GameObject sliderObj = new GameObject("Slider");
        sliderObj.transform.SetParent(setting.transform, false);
        
        Slider slider = sliderObj.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = EditorPrefs.GetFloat($"Traversify_{key}", defaultValue);
        
        LayoutElement sliderLayout = sliderObj.AddComponent<LayoutElement>();
        sliderLayout.preferredWidth = 200;
        sliderLayout.preferredHeight = 20;
        
        // Value text
        GameObject valueObj = CreateUIText("Value", setting.transform, slider.value.ToString("F2"));
        Text valueText = valueObj.GetComponent<Text>();
        valueText.fontSize = 14;
        valueText.color = new Color(0.6f, 0.6f, 0.6f);
        valueText.alignment = TextAnchor.MiddleRight;
        
        LayoutElement valueLayout = valueObj.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 60;
        
        // Update value text when slider changes
        slider.onValueChanged.AddListener((value) => {
            valueText.text = value.ToString("F2");
            EditorPrefs.SetFloat($"Traversify_{key}", value);
        });
    }
    
    private void CreateToggleSetting(Transform parent, string label, string key, bool defaultValue)
    {
        GameObject setting = new GameObject($"{label} Setting");
        setting.transform.SetParent(parent, false);
        
        HorizontalLayoutGroup layout = setting.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        
        LayoutElement settingLayout = setting.AddComponent<LayoutElement>();
        settingLayout.preferredHeight = 25;
        
        // Toggle
        GameObject toggleObj = new GameObject("Toggle");
        toggleObj.transform.SetParent(setting.transform, false);
        
        Toggle toggle = toggleObj.AddComponent<Toggle>();
        toggle.isOn = EditorPrefs.GetBool($"Traversify_{key}", defaultValue);
        
        LayoutElement toggleLayout = toggleObj.AddComponent<LayoutElement>();
        toggleLayout.preferredWidth = 20;
        toggleLayout.preferredHeight = 20;
        
        // Checkbox background
        GameObject checkboxBg = new GameObject("Background");
        checkboxBg.transform.SetParent(toggleObj.transform, false);
        
        RectTransform bgRect = checkboxBg.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        
        Image bgImage = checkboxBg.AddComponent<Image>();
        bgImage.color = new Color(0.2f, 0.2f, 0.2f);
        
        toggle.targetGraphic = bgImage;
        
        // Checkmark
        GameObject checkmark = new GameObject("Checkmark");
        checkmark.transform.SetParent(toggleObj.transform, false);
        
        RectTransform checkRect = checkmark.AddComponent<RectTransform>();
        checkRect.anchorMin = new Vector2(0.1f, 0.1f);
        checkRect.anchorMax = new Vector2(0.9f, 0.9f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;
        
        Image checkImage = checkmark.AddComponent<Image>();
        checkImage.color = new Color(0.2f, 0.8f, 0.4f);
        
        toggle.graphic = checkImage;
        
        // Label
        GameObject labelObj = CreateUIText("Label", setting.transform, label);
        Text labelText = labelObj.GetComponent<Text>();
        labelText.fontSize = 14;
        labelText.color = new Color(0.8f, 0.8f, 0.8f);
        
        // Save value when toggle changes
        toggle.onValueChanged.AddListener((value) => {
            EditorPrefs.SetBool($"Traversify_{key}", value);
        });
    }
    
    private void CreateDropdownSetting(Transform parent, string label, string key, string[] options, int defaultIndex)
    {
        GameObject setting = new GameObject($"{label} Setting");
        setting.transform.SetParent(parent, false);
        
        HorizontalLayoutGroup layout = setting.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        
        LayoutElement settingLayout = setting.AddComponent<LayoutElement>();
        settingLayout.preferredHeight = 30;
        
        // Label
        GameObject labelObj = CreateUIText("Label", setting.transform, label);
        Text labelText = labelObj.GetComponent<Text>();
        labelText.fontSize = 14;
        labelText.color = new Color(0.8f, 0.8f, 0.8f);
        
        LayoutElement labelLayout = labelObj.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200;
        
        // Dropdown
        GameObject dropdownObj = new GameObject("Dropdown");
        dropdownObj.transform.SetParent(setting.transform, false);
        
        Image dropdownBg = dropdownObj.AddComponent<Image>();
        dropdownBg.color = new Color(0.2f, 0.2f, 0.2f);
        
        Dropdown dropdown = dropdownObj.AddComponent<Dropdown>();
        dropdown.targetGraphic = dropdownBg;
        dropdown.options.Clear();
        
        foreach (string option in options)
        {
            dropdown.options.Add(new Dropdown.OptionData(option));
        }
        
        dropdown.value = EditorPrefs.GetInt($"Traversify_{key}", defaultIndex);
        
        LayoutElement dropdownLayout = dropdownObj.AddComponent<LayoutElement>();
        dropdownLayout.preferredWidth = 200;
        dropdownLayout.preferredHeight = 30;
        
        // Save value when dropdown changes
        dropdown.onValueChanged.AddListener((value) => {
            EditorPrefs.SetInt($"Traversify_{key}", value);
        });
    }
    
    private void ConnectUIReferences(TraversifyMain controller, GameObject uiRoot)
    {
        // Find UI elements
        Transform mainPanel = uiRoot.transform.Find("MainPanel");
        Transform sidePanel = mainPanel.Find("SidePanel");
        Transform loadingPanel = uiRoot.transform.Find("LoadingPanel");
        Transform loadingContainer = loadingPanel.Find("LoadingContainer");
        
        // Get buttons
        Button uploadButton = sidePanel.Find("UploadButton").GetComponent<Button>();
        Button generateButton = sidePanel.Find("GenerateButton").GetComponent<Button>();
        
        // Get other UI elements
        RawImage mapPreview = sidePanel.Find("PreviewContainer/MapPreview").GetComponent<RawImage>();
        Text statusText = sidePanel.Find("StatusContainer/StatusText").GetComponent<Text>();
        GameObject loadingPanelObj = loadingPanel.gameObject;
        Slider progressBar = loadingContainer.Find("ProgressContainer").GetComponent<Slider>();
        Text progressText = loadingContainer.Find("ProgressText").GetComponent<Text>();
        Text stageText = loadingContainer.Find("StageText").GetComponent<Text>();
        Text detailsText = loadingContainer.Find("DetailsText").GetComponent<Text>();
        Button cancelButton = loadingContainer.Find("CancelButton").GetComponent<Button>();
        
        // Get settings panel references
        Transform settingsPanel = uiRoot.transform.Find("SettingsPanel");
        Button settingsButton = sidePanel.Find("SettingsButton").GetComponent<Button>();
        Button settingsCloseButton = settingsPanel.Find("SettingsWindow/CloseButton").GetComponent<Button>();
        Button settingsOverlayButton = settingsPanel.GetComponent<Button>();
        
        // Connect references using serialized properties
        SerializedObject serializedController = new SerializedObject(controller);
        
        // UI References
        serializedController.FindProperty("uploadButton").objectReferenceValue = uploadButton;
        serializedController.FindProperty("generateButton").objectReferenceValue = generateButton;
        serializedController.FindProperty("mapPreviewImage").objectReferenceValue = mapPreview;
        serializedController.FindProperty("statusText").objectReferenceValue = statusText;
        serializedController.FindProperty("loadingPanel").objectReferenceValue = loadingPanelObj;
        serializedController.FindProperty("progressBar").objectReferenceValue = progressBar;
        serializedController.FindProperty("progressText").objectReferenceValue = progressText;
        serializedController.FindProperty("stageText").objectReferenceValue = stageText;
        serializedController.FindProperty("detailsText").objectReferenceValue = detailsText;
        serializedController.FindProperty("cancelButton").objectReferenceValue = cancelButton;
        serializedController.FindProperty("settingsPanel").objectReferenceValue = settingsPanel.gameObject;
        
        // API Key
        serializedController.FindProperty("openAIApiKey").stringValue = openAIKey;
        
        // Apply settings from preferences
        serializedController.FindProperty("terrainSize").vector3Value = new Vector3(
            EditorPrefs.GetFloat("Traversify_terrainSize", 500),
            100,
            EditorPrefs.GetFloat("Traversify_terrainSize", 500)
        );
        
        int resolutionIndex = EditorPrefs.GetInt("Traversify_terrainResolution", 2);
        int[] resolutions = { 129, 257, 513, 1025, 2049 };
        serializedController.FindProperty("terrainResolution").intValue = resolutions[resolutionIndex];
        
        serializedController.FindProperty("generateWater").boolValue = EditorPrefs.GetBool("Traversify_generateWater", true);
        serializedController.FindProperty("waterHeight").floatValue = EditorPrefs.GetFloat("Traversify_waterHeight", 0.1f);
        serializedController.FindProperty("maxObjectsToProcess").intValue = (int)EditorPrefs.GetFloat("Traversify_maxObjects", 100);
        serializedController.FindProperty("groupSimilarObjects").boolValue = EditorPrefs.GetBool("Traversify_groupObjects", true);
        serializedController.FindProperty("useHighQualityAnalysis").boolValue = EditorPrefs.GetInt("Traversify_analysisQuality", 1) == 2;
        
        serializedController.ApplyModifiedProperties();
        
        // Wire up settings panel buttons
        settingsButton.onClick.AddListener(() => {
            settingsPanel.gameObject.SetActive(true);
        });
        
        settingsCloseButton.onClick.AddListener(() => {
            settingsPanel.gameObject.SetActive(false);
        });
        
        settingsOverlayButton.onClick.AddListener(() => {
            settingsPanel.gameObject.SetActive(false);
        });
        
        Debug.Log("[Traversify] UI references connected to controller");
    }
    
    private void SetupAIModels()
    {
        Debug.Log("[Traversify] Setting up AI models...");
        
        // Create AI Controller object
        GameObject aiControllerObj = new GameObject("AIController");
        
        // Add AI components
        Traversify.MapAnalyzer mapAnalyzer = aiControllerObj.AddComponent<Traversify.MapAnalyzer>();
        
        // Configure component using serialized properties
        SerializedObject serializedAnalyzer = new SerializedObject(mapAnalyzer);
        
        // API key
        serializedAnalyzer.FindProperty("openAIApiKey").stringValue = openAIKey;
        
        // Model paths
        string yoloPath = GetModelPath("yolov8n.onnx");
        string fasterRcnnPath = GetModelPath("FasterRCNN-12.onnx");
        string samPath = GetModelPath("sam2_hiera_base.onnx");
        
        serializedAnalyzer.FindProperty("yoloModelPath").stringValue = yoloPath;
        serializedAnalyzer.FindProperty("fasterRcnnModelPath").stringValue = fasterRcnnPath;
        serializedAnalyzer.FindProperty("sam2ModelPath").stringValue = samPath;
        
        // Processing settings
        serializedAnalyzer.FindProperty("maxObjectsToProcess").intValue = (int)EditorPrefs.GetFloat("Traversify_maxObjects", 100);
        serializedAnalyzer.FindProperty("groupingThreshold").floatValue = EditorPrefs.GetFloat("Traversify_groupingDistance", 0.1f);
        serializedAnalyzer.FindProperty("useHighQuality").boolValue = EditorPrefs.GetInt("Traversify_analysisQuality", 1) >= 1;
        
        // Performance settings
        int batchSize = (int)EditorPrefs.GetFloat("Traversify_batchSize", 5);
        serializedAnalyzer.FindProperty("maxAPIRequestsPerFrame").intValue = batchSize;
        
        serializedAnalyzer.ApplyModifiedProperties();
        
        Debug.Log("[Traversify] AI models configured");
    }
    
    private void SetupTerrain()
    {
        Debug.Log("[Traversify] Setting up terrain system...");
        
        // Find or create the terrain controller objects
        GameObject terrainControllerObj = new GameObject("TerrainController");
        
        // Add terrain components
        Traversify.TerrainGenerator terrainGenerator = terrainControllerObj.AddComponent<Traversify.TerrainGenerator>();
        SegmentationVisualizer segmentVisualizer = terrainControllerObj.AddComponent<SegmentationVisualizer>();
        ModelGenerator modelGenerator = terrainControllerObj.AddComponent<ModelGenerator>();
        
        // Configure TerrainGenerator
        SerializedObject serializedTerrainGen = new SerializedObject(terrainGenerator);
        serializedTerrainGen.FindProperty("heightMapMultiplier").floatValue = EditorPrefs.GetFloat("Traversify_heightMultiplier", 30);
        serializedTerrainGen.FindProperty("generateWaterPlane").boolValue = EditorPrefs.GetBool("Traversify_generateWater", true);
        serializedTerrainGen.FindProperty("waterHeight").floatValue = EditorPrefs.GetFloat("Traversify_waterHeight", 0.1f);
        serializedTerrainGen.ApplyModifiedProperties();
        
        // Configure SegmentationVisualizer
        SerializedObject serializedVisualizer = new SerializedObject(segmentVisualizer);
        serializedVisualizer.FindProperty("showTerrainLabels").boolValue = EditorPrefs.GetBool("Traversify_showTerrainLabels", true);
        serializedVisualizer.FindProperty("showObjectLabels").boolValue = EditorPrefs.GetBool("Traversify_showObjectLabels", true);
        serializedVisualizer.ApplyModifiedProperties();
        
        // Configure ModelGenerator
        SerializedObject serializedModelGen = new SerializedObject(modelGenerator);
        serializedModelGen.FindProperty("openAIApiKey").stringValue = openAIKey;
        serializedModelGen.FindProperty("groupSimilarObjects").boolValue = EditorPrefs.GetBool("Traversify_groupObjects", true);
        serializedModelGen.ApplyModifiedProperties();
        
        Debug.Log("[Traversify] Terrain system configured");
    }
    
    private void CreateSampleScene()
    {
        Debug.Log("[Traversify] Creating production scene...");
        
        try
        {
            // Create new scene with default objects
            UnityEngine.SceneManagement.Scene productionScene = EditorSceneManager.NewScene(
                NewSceneSetup.DefaultGameObjects, 
                NewSceneMode.Single);
            
            // Set up enhanced lighting
            GameObject lightObj = GameObject.Find("Directional Light");
            if (lightObj != null)
            {
                Light light = lightObj.GetComponent<Light>();
                light.intensity = 1.5f;
                light.color = new Color(1f, 0.98f, 0.92f);
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0);
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.8f;
                
                // Add light settings component if it exists in the project
                System.Type lightSettingsType = System.Type.GetType("Traversify.Core.LightSettings");
                if (lightSettingsType != null)
                {
                    lightObj.AddComponent(lightSettingsType);
                }
            }
            
            // Set up professional camera
            GameObject cameraObj = GameObject.Find("Main Camera");
            if (cameraObj != null)
            {
                Camera camera = cameraObj.GetComponent<Camera>();
                camera.transform.position = new Vector3(250, 180, -150);
                camera.transform.rotation = Quaternion.Euler(35, 15, 0);
                camera.farClipPlane = 3000;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.allowHDR = true;
                camera.allowMSAA = true;
                
                // Add post-processing if available
                System.Type postProcessType = System.Type.GetType("Traversify.Core.PostProcessingController");
                if (postProcessType != null)
                {
                    cameraObj.AddComponent(postProcessType);
                }
                
                // Add production camera controller
                FreeCameraController cameraController = cameraObj.AddComponent<FreeCameraController>();
                cameraController.moveSpeed = 75f;
                cameraController.fastMoveSpeed = 200f;
            }
            
            // Create Traversify Manager GameObject - Central control object
            GameObject traversifyManager = new GameObject("TraversifyManager");
            traversifyManager.transform.position = Vector3.zero;
            
            // Add core system components to manager
            System.Type systemManagerType = System.Type.GetType("Traversify.Core.SystemManager");
            if (systemManagerType != null)
            {
                traversifyManager.AddComponent(systemManagerType);
            }
            
            System.Type configManagerType = System.Type.GetType("Traversify.Core.ConfigManager");
            if (configManagerType != null)
            {
                var configManager = traversifyManager.AddComponent(configManagerType);
                
                // Set API key via reflection if the property exists
                if (configManager != null)
                {
                    System.Reflection.PropertyInfo apiKeyProperty = configManagerType.GetProperty("ApiKey");
                    if (apiKeyProperty != null && !string.IsNullOrEmpty(openAIKey))
                    {
                        apiKeyProperty.SetValue(configManager, openAIKey);
                    }
                }
            }
            
            // Create terrain GameObject
            GameObject terrainObj = new GameObject("Terrain");
            
            // Add proper terrain component
            Terrain terrain = terrainObj.AddComponent<Terrain>();
            TerrainCollider terrainCollider = terrainObj.AddComponent<TerrainCollider>();
            
            // Configure terrain data
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = 513;
            terrainData.size = new Vector3(2000, 600, 2000);
            terrainData.SetDetailResolution(1024, 16);
            
            // Apply terrain settings
            terrain.terrainData = terrainData;
            terrainCollider.terrainData = terrainData;
            
            // Store terrain data asset
            string terrainDataPath = "Assets/Traversify/Terrain/TerrainData.asset";
            
            // Create directory if it doesn't exist
            string directory = Path.GetDirectoryName(terrainDataPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            AssetDatabase.CreateAsset(terrainData, terrainDataPath);
            
            // Add Traversify terrain components
            System.Type terrainGeneratorType = System.Type.GetType("Traversify.TerrainGenerator");
            if (terrainGeneratorType != null)
            {
                Component terrainGenerator = terrainObj.AddComponent(terrainGeneratorType);
                
                // Configure TerrainGenerator settings via reflection
                if (terrainGenerator != null)
                {
                    // Set heightMapMultiplier
                    System.Reflection.FieldInfo heightMultiplierField = terrainGeneratorType.GetField("heightMapMultiplier");
                    if (heightMultiplierField != null)
                    {
                        heightMultiplierField.SetValue(terrainGenerator, EditorPrefs.GetFloat("Traversify_heightMultiplier", 30f));
                    }
                    
                    // Set water generation
                    System.Reflection.FieldInfo generateWaterField = terrainGeneratorType.GetField("generateWaterPlane");
                    if (generateWaterField != null)
                    {
                        generateWaterField.SetValue(terrainGenerator, EditorPrefs.GetBool("Traversify_generateWater", true));
                    }
                    
                    // Set water height
                    System.Reflection.FieldInfo waterHeightField = terrainGeneratorType.GetField("waterHeight");
                    if (waterHeightField != null)
                    {
                        waterHeightField.SetValue(terrainGenerator, EditorPrefs.GetFloat("Traversify_waterHeight", 0.1f));
                    }
                }
            }
            
            // Add visualization component
            System.Type visualizerType = System.Type.GetType("Traversify.SegmentationVisualizer");
            if (visualizerType != null)
            {
                terrainObj.AddComponent(visualizerType);
            }
            
            // Create AI Controller GameObject
            GameObject aiController = new GameObject("AIController");
            aiController.transform.position = Vector3.zero;
            
            // Add map analyzer component
            System.Type mapAnalyzerType = System.Type.GetType("Traversify.MapAnalyzer");
            if (mapAnalyzerType != null)
            {
                Component mapAnalyzer = aiController.AddComponent(mapAnalyzerType);
                
                // Configure MapAnalyzer settings via reflection
                if (mapAnalyzer != null)
                {
                    // Set API key
                    System.Reflection.FieldInfo apiKeyField = mapAnalyzerType.GetField("openAIApiKey");
                    if (apiKeyField != null && !string.IsNullOrEmpty(openAIKey))
                    {
                        apiKeyField.SetValue(mapAnalyzer, openAIKey);
                    }
                    
                    // Set max objects to process
                    System.Reflection.FieldInfo maxObjectsField = mapAnalyzerType.GetField("maxObjectsToProcess");
                    if (maxObjectsField != null)
                    {
                        maxObjectsField.SetValue(mapAnalyzer, (int)EditorPrefs.GetFloat("Traversify_maxObjects", 100));
                    }
                    
                    // Set grouping threshold
                    System.Reflection.FieldInfo groupThresholdField = mapAnalyzerType.GetField("groupingThreshold");
                    if (groupThresholdField != null)
                    {
                        groupThresholdField.SetValue(mapAnalyzer, EditorPrefs.GetFloat("Traversify_groupingDistance", 0.1f));
                    }
                    
                    // Set high quality flag
                    System.Reflection.FieldInfo highQualityField = mapAnalyzerType.GetField("useHighQuality");
                    if (highQualityField != null)
                    {
                        highQualityField.SetValue(mapAnalyzer, EditorPrefs.GetInt("Traversify_analysisQuality", 1) >= 1);
                    }
                }
            }
            
            // Add model generator component
            System.Type modelGeneratorType = System.Type.GetType("Traversify.ModelGenerator");
            if (modelGeneratorType != null)
            {
                Component modelGenerator = aiController.AddComponent(modelGeneratorType);
                
                // Configure ModelGenerator settings via reflection
                if (modelGenerator != null)
                {
                    // Set API key
                    System.Reflection.FieldInfo apiKeyField = modelGeneratorType.GetField("openAIApiKey");
                    if (apiKeyField != null && !string.IsNullOrEmpty(openAIKey))
                    {
                        apiKeyField.SetValue(modelGenerator, openAIKey);
                    }
                    
                    // Set group similar objects flag
                    System.Reflection.FieldInfo groupObjectsField = modelGeneratorType.GetField("groupSimilarObjects");
                    if (groupObjectsField != null)
                    {
                        groupObjectsField.SetValue(modelGenerator, EditorPrefs.GetBool("Traversify_groupObjects", true));
                    }
                }
            }
            
            // Create worker factory controller
            GameObject workerFactoryObj = new GameObject("WorkerFactory");
            workerFactoryObj.transform.position = Vector3.zero;
            
            // Add worker factory component if available
            System.Type workerFactoryType = System.Type.GetType("Traversify.Core.WorkerFactory");
            if (workerFactoryType != null)
            {
                workerFactoryObj.AddComponent(workerFactoryType);
            }
            
            // Create UI Canvas with proper hierarchy
            GameObject canvasObj = new GameObject("TraversifyCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler canvasScaler = canvasObj.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.matchWidthOrHeight = 0.5f;
            
            canvasObj.AddComponent<GraphicRaycaster>();
            
            // Create UI root
            GameObject uiRoot = new GameObject("TraversifyUI");
            uiRoot.transform.SetParent(canvasObj.transform, false);
            
            // Set up main UI panels
            SetupMainPanel(uiRoot.transform);
            SetupLoadingPanel(uiRoot.transform);
            SetupSettingsPanel(uiRoot.transform);
            
            // Add controller GameObject
            GameObject controllerObj = new GameObject("TraversifyController");
            controllerObj.transform.position = Vector3.zero;
            
            // Add main controller component
            System.Type mainControllerType = System.Type.GetType("Traversify.TraversifyMain");
            if (mainControllerType != null)
            {
                Component mainController = controllerObj.AddComponent(mainControllerType);
                
                // Connect UI references
                if (mainController != null)
                {
                    ConnectUIReferences(mainController, uiRoot);
                }
            }
            
            // Add debugger component
            System.Type debuggerType = System.Type.GetType("Traversify.Core.TraversifyDebugger");
            if (debuggerType != null)
            {
                controllerObj.AddComponent(debuggerType);
            }
            
            // Add water plane if needed
            if (EditorPrefs.GetBool("Traversify_generateWater", true))
            {
                GameObject waterObj = GameObject.CreatePrimitive(PrimitiveType.Plane);
                waterObj.name = "WaterPlane";
                waterObj.transform.position = new Vector3(1000, EditorPrefs.GetFloat("Traversify_waterHeight", 0.1f) * 600, 1000);
                waterObj.transform.localScale = new Vector3(200, 1, 200);
                
                // Try to find and apply water material
                string[] waterMaterials = AssetDatabase.FindAssets("t:Material Water");
                if (waterMaterials.Length > 0)
                {
                    string waterMatPath = AssetDatabase.GUIDToAssetPath(waterMaterials[0]);
                    Material waterMat = AssetDatabase.LoadAssetAtPath<Material>(waterMatPath);
                    if (waterMat != null)
                    {
                        waterObj.GetComponent<Renderer>().material = waterMat;
                    }
                }
                else
                {
                    // Create basic water material
                    Material waterMat = new Material(Shader.Find("Standard"));
                    waterMat.color = new Color(0.0f, 0.4f, 0.7f, 0.6f);
                    waterMat.SetFloat("_Glossiness", 0.9f);
                    waterMat.SetFloat("_Metallic", 0.0f);
                    waterMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    waterMat.renderQueue = 3000;
                    waterMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    waterMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    waterMat.SetInt("_ZWrite", 0);
                    waterMat.DisableKeyword("_ALPHATEST_ON");
                    waterMat.EnableKeyword("_ALPHABLEND_ON");
                    waterMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    
                    // Save material asset
                    string waterMatPath = "Assets/Traversify/Materials/Water.mat";
                    directory = Path.GetDirectoryName(waterMatPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    AssetDatabase.CreateAsset(waterMat, waterMatPath);
                    
                    waterObj.GetComponent<Renderer>().material = waterMat;
                }
            }
            
            // Set up environment settings
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.2f;
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.9f, 0.95f, 1.0f, 1.0f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0005f;
            
            // Ensure the Traversify scenes folder exists
            string scenePath = "Assets/Traversify/Scenes/TraversifyScene.unity";
            directory = Path.GetDirectoryName(scenePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Create a sample scene path
            string sampleScenePath = "Assets/Traversify/Scenes/TraversifySampleScene.unity";
            
            // Save the scene as both the main scene and sample scene
            EditorSceneManager.SaveScene(productionScene, scenePath);
            AssetDatabase.CopyAsset(scenePath, sampleScenePath);
            
            Debug.Log($"[Traversify] Production scene created at: {scenePath}");
            Debug.Log($"[Traversify] Sample scene created at: {sampleScenePath}");
            
            // Organize objects in hierarchy for better scene management
            if (terrainObj != null) terrainObj.transform.SetSiblingIndex(0);
            if (lightObj != null) lightObj.transform.SetSiblingIndex(1);
            if (cameraObj != null) cameraObj.transform.SetSiblingIndex(2);
            if (traversifyManager != null) traversifyManager.transform.SetSiblingIndex(3);
            if (aiController != null) aiController.transform.SetSiblingIndex(4);
            if (workerFactoryObj != null) workerFactoryObj.transform.SetSiblingIndex(5);
            if (controllerObj != null) controllerObj.transform.SetSiblingIndex(6);
            if (canvasObj != null) canvasObj.transform.SetSiblingIndex(7);
            
            // Mark scene as dirty
            EditorSceneManager.MarkSceneDirty(productionScene);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Traversify] Failed to create scene: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("Scene Creation Error", 
                $"Failed to create scene: {ex.Message}\n\nCheck the console for details.", 
                "OK");
        }
    }
    
    private IEnumerator DownloadAllModels()
    {
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
            Debug.Log("[Traversify] All models already downloaded");
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
                    break;
                }
                else
                {
                    Debug.LogWarning($"[Traversify] Failed to download {modelInfo.name} from {url}");
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
        
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.downloadHandler = new DownloadHandlerFile(tempPath);
            
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            
            while (!operation.isDone)
            {
                onProgress?.Invoke(request.downloadProgress);
                yield return new WaitForSeconds(0.1f);
            }
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                // Move temp file to final location
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }
                File.Move(tempPath, downloadPath);
                
                Debug.Log($"[Traversify] Successfully downloaded {fileName} ({sizeMB:F1} MB)");
                onComplete?.Invoke(true);
            }
            else
            {
                // Clean up temp file
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                
                Debug.LogError($"[Traversify] Download failed: {request.error}");
                onComplete?.Invoke(false);
            }
        }
    }
    
    private void CancelDownload()
    {
        if (downloadCoroutine != null)
        {
            EditorCoroutineUtility.StopCoroutine(downloadCoroutine);
            downloadCoroutine = null;
        }
        
        isDownloading = false;
        currentDownloadProgress = 0;
        downloadStatus = "Download cancelled";
        
        // Clean up any temp files
        string[] tempFiles = Directory.GetFiles(Application.dataPath, "*.tmp", SearchOption.AllDirectories);
        foreach (string tempFile in tempFiles)
        {
            try
            {
                File.Delete(tempFile);
            }
            catch { }
        }
        
        Repaint();
    }
    
    private void CompleteSetup()
    {
        AssetDatabase.Refresh();
        
        EditorUtility.DisplayDialog("Setup Complete", 
            "Traversify has been successfully set up!\n\n" +
            "• All components have been configured\n" +
            "• AI models are ready to use\n" +
            "• Sample scene has been created\n\n" +
            "You can now use Traversify to generate terrains from map images.", 
            "OK");
        
        // Open sample scene
        if (createSampleScene)
        {
            string scenePath = "Assets/Traversify/Scenes/TraversifySampleScene.unity";
            if (File.Exists(scenePath))
            {
                EditorSceneManager.OpenScene(scenePath);
            }
        }
        
        Debug.Log("[Traversify] Setup completed successfully!");
    }
    
    private class ModelDownloadInfo
    {
        public string name;
        public string fileName;
        public string[] urls;
        public float size;
    }
}

// Simple free camera controller for the sample scene
public class FreeCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 50f;
    public float fastMoveSpeed = 150f;
    public float rotationSpeed = 2f;
    public float smoothTime = 0.1f;
    
    private Vector3 velocity = Vector3.zero;
    private float rotationX = 0f;
    private float rotationY = 0f;
    
    void Start()
    {
        Vector3 rotation = transform.eulerAngles;
        rotationX = rotation.y;
        rotationY = rotation.x;
    }
    
    void Update()
    {
        // Only control camera when right mouse button is held
        if (Input.GetMouseButton(1))
        {
            // Mouse look
            rotationX += Input.GetAxis("Mouse X") * rotationSpeed;
            rotationY -= Input.GetAxis("Mouse Y") * rotationSpeed;
            rotationY = Mathf.Clamp(rotationY, -90f, 90f);
            
            transform.rotation = Quaternion.Euler(rotationY, rotationX, 0);
            
            // Hide cursor
            Cursor.lockState = CursorLockMode.Locked;
        }
        else
        {
            // Show cursor
            Cursor.lockState = CursorLockMode.None;
        }
        
        // Movement
        float currentSpeed = Input.GetKey(KeyCode.LeftShift) ? fastMoveSpeed : moveSpeed;
        Vector3 targetVelocity = Vector3.zero;
        
        if (Input.GetKey(KeyCode.W)) targetVelocity += transform.forward;
        if (Input.GetKey(KeyCode.S)) targetVelocity -= transform.forward;
        if (Input.GetKey(KeyCode.A)) targetVelocity -= transform.right;
        if (Input.GetKey(KeyCode.D)) targetVelocity += transform.right;
        if (Input.GetKey(KeyCode.Q)) targetVelocity -= transform.up;
        if (Input.GetKey(KeyCode.E)) targetVelocity += transform.up;
        
        targetVelocity = targetVelocity.normalized * currentSpeed;
        velocity = Vector3.Lerp(velocity, targetVelocity, smoothTime);
        transform.position += velocity * Time.deltaTime;
    }
}
#endif
