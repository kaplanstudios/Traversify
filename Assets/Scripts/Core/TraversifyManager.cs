// ----- TraversifyManager (Part 1/4): Initialization and Configuration -----
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.AI.Navigation;
using TMPro;
using Unity.Barracuda;

namespace Traversify
{
    [RequireComponent(typeof(Traversify.Core.TraversifyDebugger))]
    public class TraversifyManager : MonoBehaviour
    {
        // Singleton instance
        private static TraversifyManager _instance;
        public static TraversifyManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<TraversifyManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("Traversify");
                        _instance = go.AddComponent<TraversifyManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("UI References")]
        public Button uploadButton;
        public Button generateButton;
        public RawImage mapPreviewImage;
        public Text statusText;
        public GameObject loadingPanel;
        public Slider progressBar;
        public Text progressText;
        public Text stageText;
        public Text detailsText;
        public Button cancelButton;
        public GameObject settingsPanel;

        [Header("Terrain Settings")]
        [SerializeField] public Vector3 terrainSize = new Vector3(500, 100, 500);
        [SerializeField] public int terrainResolution = 513;
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private float heightMapMultiplier = 30f;

        [Header("Processing Settings")]
        [SerializeField] private bool useHighQualityAnalysis = true;
        [SerializeField] private bool groupSimilarObjects = true;
        [SerializeField] private int maxObjectsToProcess = 100;
        [SerializeField] private float processingTimeout = 300f;
        [SerializeField] [Range(0f, 1f)] private float instancingSimilarity = 0.8f;
        [SerializeField] [Range(0.1f, 1f)] private float detectionThreshold = 0.5f;
        [SerializeField] private float nmsThreshold = 0.45f;
        [SerializeField] private bool useFasterRCNN = true;
        [SerializeField] private bool useSAM = true;

        [Header("API Settings")]
        [SerializeField] private string openAIApiKey = "";
        [SerializeField] private int maxConcurrentAPIRequests = 3;
        [SerializeField] private float apiRateLimitDelay = 0.5f;

        [Header("Performance Settings")]
        [SerializeField] private bool useGPUAcceleration = true;
        [SerializeField] private Traversify.Core.WorkerFactory.Type inferenceBackend = Traversify.Core.WorkerFactory.Type.Auto;
        [SerializeField] private int processingBatchSize = 5;
        [SerializeField] private bool enableDebugVisualization = false;

        [Header("AI Model Files")]
        [SerializeField] private OnnxModelAsset yoloModel;
        [SerializeField] private OnnxModelAsset sam2Model;
        [SerializeField] private OnnxModelAsset fasterRcnnModel;
        [SerializeField] private TextAsset labelsFile;

        [Header("Output Settings")]
        [SerializeField] private bool saveGeneratedAssets = true;
        [SerializeField] private string assetSavePath = "Assets/GeneratedTerrains";
        [SerializeField] private bool generateMetadata = true;

        [Header("Water Settings")]
        [SerializeField] private bool generateWater = true;
        [SerializeField] private float waterHeight = 0.5f;

        [Header("Visualization Settings")]
        [SerializeField] private GameObject overlayPrefab;
        [SerializeField] private GameObject labelPrefab;
        [SerializeField] private Material overlayMaterial;
        [SerializeField] private float overlayYOffset = 0.5f;
        [SerializeField] private float labelYOffset = 2.0f;
        [SerializeField] [Range(0f, 3f)] private float overlayFadeDuration = 0.5f;

        [Header("Object Generation Settings")]
        [SerializeField] private Material defaultObjectMaterial;

        // Internal references and state
        private Traversify.Core.TraversifyDebugger debugger;
        private IWorker yoloWorker;
        private IWorker sam2Worker;
        private IWorker rcnnWorker;
        private string[] classLabels;
        private Texture2D uploadedMapTexture;
        private UnityEngine.Terrain generatedTerrain;
        private GameObject waterPlane;
        private bool isProcessing = false;
        private bool isCancelled = false;
        private AnalysisResults analysisResults;
        private List<GameObject> generatedObjects = new List<GameObject>();
        private Coroutine processingCoroutine;
        private float processingStartTime;
        private Dictionary<string, float> performanceMetrics = new Dictionary<string, float>();

        // Events
        public event Action<AnalysisResults> OnAnalysisComplete;
        public event Action<UnityEngine.Terrain> OnTerrainGenerated;
        public event Action<List<GameObject>> OnModelsPlaced;
        public event Action<string> OnError;
        public event Action<float> OnProgressUpdate;
        public event Action OnProcessingComplete;
        public event Action OnProcessingCancelled;

        private void Awake()
        {
            // Singleton enforcement
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Initialize debugger
            debugger = GetComponent<Traversify.Core.TraversifyDebugger>();
            if (debugger == null)
                debugger = gameObject.AddComponent<Traversify.Core.TraversifyDebugger>();
            debugger.Log("Traversify singleton initializing...", Traversify.Core.LogCategory.System);

            // Configure inference backend and load models
            if (useGPUAcceleration && SystemInfo.supportsComputeShaders)
            {
                inferenceBackend = Traversify.Core.WorkerFactory.Type.ComputePrecompiled;
                debugger.Log($"GPU acceleration enabled - {SystemInfo.graphicsDeviceName}", Traversify.Core.LogCategory.System);
            }
            else
            {
                inferenceBackend = Traversify.Core.WorkerFactory.Type.CSharpBurst;
                debugger.Log("Using CPU inference (Burst compiled)", Traversify.Core.LogCategory.System);
            }
            LoadModels();
            // (OpenAI HTTP client configuration could be done here if needed)
        }

        private void Start()
        {
            try
            {
                SetupUIEventHandlers();
                InitializeUI();
                InitializeComponents();
                ConfigureComponents();
                ValidateModelFiles();
                LoadPreferences();
                debugger.Log($"Traversify v2.0.1 initialized successfully - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", Traversify.Core.LogCategory.System);
                debugger.Log($"User: {SystemInfo.deviceName}", Traversify.Core.LogCategory.System);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error during initialization: {ex.Message}\n{ex.StackTrace}", Traversify.Core.LogCategory.System);
                UpdateStatus("Error during initialization. Check console for details.", true);
            }
        }

        private void SetupUIEventHandlers()
        {
            if (uploadButton != null)
            {
                uploadButton.onClick.RemoveAllListeners();
                uploadButton.onClick.AddListener(OpenFileExplorer);
            }
            if (generateButton != null)
            {
                generateButton.onClick.RemoveAllListeners();
                generateButton.onClick.AddListener(StartTerrainGeneration);
                generateButton.interactable = false;
            }
            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveAllListeners();
                cancelButton.onClick.AddListener(CancelProcessing);
            }
        }

        private void InitializeUI()
        {
            if (loadingPanel != null) loadingPanel.SetActive(false);
            if (progressBar != null) progressBar.value = 0;
            if (progressText != null) progressText.text = "0%";
            if (stageText != null) stageText.text = "";
            if (detailsText != null) detailsText.text = "";
        }

        private void InitializeComponents()
        {
            // Ensure UI references are set
            FindButtonReferences();
            try
            {
                GameObject compContainer = GameObject.Find("TraversifyComponents");
                if (compContainer == null)
                {
                    compContainer = new GameObject("TraversifyComponents");
                    compContainer.transform.SetParent(transform);
                }
                // All processing components are integrated into this manager
            }
            catch (Exception ex)
            {
                debugger.LogError($"Failed to initialize components: {ex.Message}", Traversify.Core.LogCategory.System);
                throw;
            }
        }

        private void FindButtonReferences()
        {
            if (uploadButton == null)
            {
                uploadButton = GameObject.Find("UploadButton")?.GetComponent<Button>();
                if (uploadButton == null)
                    debugger?.LogWarning("Upload button reference not found", Traversify.Core.LogCategory.UI);
            }
            if (generateButton == null)
            {
                generateButton = GameObject.Find("GenerateButton")?.GetComponent<Button>();
                if (generateButton == null)
                    debugger?.LogWarning("Generate button reference not found", Traversify.Core.LogCategory.UI);
            }
            if (cancelButton == null)
            {
                cancelButton = GameObject.Find("CancelButton")?.GetComponent<Button>();
            }
            // Set up settings panel toggling if applicable
            Button settingsButton = GameObject.Find("SettingsButton")?.GetComponent<Button>();
            if (settingsButton != null && settingsPanel != null)
            {
                settingsButton.onClick.RemoveAllListeners();
                settingsButton.onClick.AddListener(() => settingsPanel.SetActive(true));
                Button closeButton = settingsPanel.transform.Find("SettingsWindow/CloseButton")?.GetComponent<Button>();
                if (closeButton != null)
                {
                    closeButton.onClick.RemoveAllListeners();
                    closeButton.onClick.AddListener(() => settingsPanel.SetActive(false));
                }
                Button overlayButton = settingsPanel.GetComponent<Button>();
                if (overlayButton != null)
                {
                    overlayButton.onClick.RemoveAllListeners();
                    overlayButton.onClick.AddListener(() => settingsPanel.SetActive(false));
                }
            }
        }

        private void ConfigureComponents()
        {
            // Apply configuration to integrated components (if any)
            if (string.IsNullOrEmpty(openAIApiKey))
            {
                debugger.LogWarning("OpenAI API key is not set. Enhanced descriptions will be limited.", Traversify.Core.LogCategory.API);
            }
        }

        private string GetModelPath(string modelFileName)
        {
            return Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", modelFileName);
        }

        private void ValidateModelFiles()
        {
            string[] requiredModels = { "yolov12.onnx", "FasterRCNN-12.onnx", "sam2_hiera_base.onnx" };
            List<string> missing = new List<string>();
            string modelsDir = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models");
            if (!Directory.Exists(modelsDir))
            {
                Directory.CreateDirectory(modelsDir);
                debugger.Log($"Created models directory at {modelsDir}", Traversify.Core.LogCategory.System);
            }
            foreach (string modelFile in requiredModels)
            {
                string modelPath = GetModelPath(modelFile);
                if (!File.Exists(modelPath))
                {
                    missing.Add(modelFile);
                    string sourcePath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", modelFile);
                    if (File.Exists(sourcePath))
                    {
                        try
                        {
                            File.Copy(sourcePath, modelPath);
                            debugger.Log($"Copied model {modelFile} to StreamingAssets", Traversify.Core.LogCategory.System);
                            missing.Remove(modelFile);
                        }
                        catch (Exception ex)
                        {
                            debugger.LogError($"Failed to copy model {modelFile}: {ex.Message}", Traversify.Core.LogCategory.System);
                        }
                    }
                }
            }
            if (missing.Count > 0)
            {
                debugger.LogWarning($"Missing models: {string.Join(", ", missing)}. Ensure model files are present in StreamingAssets/Traversify/Models.", Traversify.Core.LogCategory.System);
            }
        }

        private void LoadPreferences()
        {
            // (Optional) Load saved user preferences for settings
        }

        private void SavePreferences()
        {
            // (Optional) Save user preferences for settings
        }
// ----- TraversifyManager (Part 2/4): User Input and Controls -----
        private void OpenFileExplorer()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("Select Map Image", "", "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(path))
            {
                StartCoroutine(LoadImageFromPath(path));
            }
#else
            UpdateStatus("File upload not supported on this platform");
#endif
        }

        private IEnumerator LoadImageFromPath(string path)
        {
            // Use SafeCoroutine for robust error handling while loading image
            return SafeCoroutine(InnerLoadImageFromPath(path), errorMsg =>
            {
                debugger.LogError($"Error loading image: {errorMsg}", Traversify.Core.LogCategory.IO);
                UpdateStatus($"Error loading image: {errorMsg}", true);
                if (generateButton != null) generateButton.interactable = false;
                if (mapPreviewImage != null) mapPreviewImage.gameObject.SetActive(false);
            });
        }

        private IEnumerator InnerLoadImageFromPath(string path)
        {
            debugger.Log($"Loading image from: {path}", Traversify.Core.LogCategory.IO);
            FileInfo fileInfo = new FileInfo(path);
            float fileSizeMB = fileInfo.Length / (1024f * 1024f);
            if (fileSizeMB > 50f)
            {
                debugger.LogWarning($"Large image file ({fileSizeMB:F1} MB) may take longer to process", Traversify.Core.LogCategory.IO);
            }
            // Load image file as texture
            UnityWebRequest request = UnityWebRequestTexture.GetTexture("file://" + path);
            request.timeout = 30;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new Exception(request.error);
            }
            uploadedMapTexture = DownloadHandlerTexture.GetContent(request);
            uploadedMapTexture.name = Path.GetFileNameWithoutExtension(path);
            ValidateLoadedImage();
            DisplayLoadedImage();
            if (generateButton != null) generateButton.interactable = true;
            UpdateStatus($"Map loaded: {uploadedMapTexture.width}x{uploadedMapTexture.height} ({fileSizeMB:F1} MB)");
            debugger.Log($"Image loaded successfully: {uploadedMapTexture.width}x{uploadedMapTexture.height} ({fileSizeMB:F1} MB)", Traversify.Core.LogCategory.IO);
        }

        private void ValidateLoadedImage()
        {
            if (uploadedMapTexture.width < 128 || uploadedMapTexture.height < 128)
            {
                throw new Exception("Image is too small. Minimum size is 128x128 pixels.");
            }
            if (uploadedMapTexture.width > 8192 || uploadedMapTexture.height > 8192)
            {
                debugger.LogWarning("Very large image detected. Processing may be slow.", Traversify.Core.LogCategory.IO);
            }
            float aspectRatio = (float)uploadedMapTexture.width / uploadedMapTexture.height;
            if (aspectRatio < 0.5f || aspectRatio > 2f)
            {
                debugger.LogWarning($"Unusual aspect ratio ({aspectRatio:F2}). Results may vary.", Traversify.Core.LogCategory.IO);
            }
        }

        private void DisplayLoadedImage()
        {
            if (mapPreviewImage != null)
            {
                mapPreviewImage.texture = uploadedMapTexture;
                mapPreviewImage.gameObject.SetActive(true);
                // Adjust preview aspect ratio
                AspectRatioFitter aspectFitter = mapPreviewImage.GetComponent<AspectRatioFitter>();
                if (aspectFitter != null)
                {
                    aspectFitter.aspectRatio = (float)uploadedMapTexture.width / uploadedMapTexture.height;
                }
                mapPreviewImage.SetNativeSize();
            }
        }

        private void StartTerrainGeneration()
        {
            if (isProcessing)
            {
                debugger.LogWarning("Terrain generation already in progress", Traversify.Core.LogCategory.User);
                return;
            }
            if (uploadedMapTexture == null)
            {
                debugger.LogWarning("No map image uploaded", Traversify.Core.LogCategory.User);
                UpdateStatus("Please upload a map image first", true);
                return;
            }
            SavePreferences();
            isProcessing = true;
            isCancelled = false;
            processingStartTime = Time.time;
            performanceMetrics.Clear();
            if (generateButton != null) generateButton.interactable = false;
            if (uploadButton != null) uploadButton.interactable = false;
            // Begin the generation process coroutine
            processingCoroutine = StartCoroutine(GenerateTerrainFromMap());
        }

        private void CancelProcessing()
        {
            if (!isProcessing) return;
            debugger.Log("Cancelling processing...", Traversify.Core.LogCategory.User);
            isCancelled = true;
            if (processingCoroutine != null)
            {
                StopCoroutine(processingCoroutine);
                processingCoroutine = null;
            }
            CleanupGeneratedObjects();
            ResetUI();
            UpdateStatus("Processing cancelled by user");
            OnProcessingCancelled?.Invoke();
        }
// ----- TraversifyManager (Part 3/4): World Generation Pipeline -----
        private IEnumerator GenerateTerrainFromMap()
        {
            ShowLoadingPanel(true);
            UpdateProgress(0.05f, "Starting terrain generation...");
            debugger.Log("Starting terrain generation process", Traversify.Core.LogCategory.Process);
            debugger.StartTimer("TotalGeneration");

            // Run generation in a protected coroutine to handle errors
            yield return StartCoroutine(SafeCoroutine(InnerGenerateTerrainFromMap(), ex =>
            {
                // On error: log and display error status, trigger OnError event
                debugger.LogError($"Error during terrain generation: {ex}", Traversify.Core.LogCategory.Process);
                UpdateStatus($"Error: {ex}", true);
                OnError?.Invoke(ex.Message);
            }));

            LogPerformanceMetrics();
            ResetUI();
        }

        private IEnumerator InnerGenerateTerrainFromMap()
        {
            // Clean up any previous generated objects
            CleanupGeneratedObjects();

            // Step 1: Analyze the map using AI (approx 40% progress)
            debugger.StartTimer("MapAnalysis");
            yield return StartCoroutine(AnalyzeImage(uploadedMapTexture,
                results =>
                {
                    analysisResults = results;
                    OnAnalysisComplete?.Invoke(results);
                },
                error =>
                {
                    throw new Exception(error);
                },
                (stage, prog) =>
                {
                    // Map analysis progress updates (scaled 0.1 to 0.4 of total)
                    float totalProg = 0.1f + (prog * 0.3f);
                    UpdateProgress(totalProg);
                    OnProgressUpdate?.Invoke(totalProg);
                    // Log stage changes for debugging
                    if (stage != null) debugger.Log($"{stage} ({prog:P0})", Traversify.Core.LogCategory.AI);
                }));
            performanceMetrics["MapAnalysis"] = debugger.StopTimer("MapAnalysis");
            if (isCancelled || analysisResults == null)
            {
                throw new OperationCanceledException("Analysis cancelled");
            }
            // Summary of analysis results
            string summary = $"Detected {analysisResults.terrainFeatures.Count} terrain features and {analysisResults.mapObjects.Count} objects";
            UpdateStage("Analysis Complete", summary);
            debugger.Log("Map analysis complete: " + summary, Traversify.Core.LogCategory.Process);
            if (detailsText != null)
            {
                detailsText.text = GenerateAnalysisDetails();
            }
            UpdateProgress(0.4f);

            // Step 2: Generate terrain from analysis (approx 20% progress)
            debugger.StartTimer("TerrainGeneration");
            UpdateStage("Terrain Mesh Generation", "Creating heightmap...");
            UpdateProgress(0.45f);
            // Create Unity Terrain based on analysis results
            generatedTerrain = CreateTerrainFromAnalysis(analysisResults, uploadedMapTexture);
            if (generatedTerrain == null)
            {
                throw new Exception("Terrain generation failed");
            }
            generatedObjects.Add(generatedTerrain.gameObject);
            performanceMetrics["TerrainGeneration"] = debugger.StopTimer("TerrainGeneration");
            if (isCancelled)
            {
                throw new OperationCanceledException("Terrain generation cancelled");
            }
            UpdateProgress(0.6f, "Terrain generated");

            // Step 3: Create water plane if water generation is enabled (5% progress)
            if (generateWater)
            {
                debugger.StartTimer("WaterCreation");
                UpdateProgress(0.65f, "Creating water features...");
                CreateWaterPlane();
                performanceMetrics["WaterCreation"] = debugger.StopTimer("WaterCreation");
                // Add water plane to generated objects list
                if (waterPlane != null) generatedObjects.Add(waterPlane);
                yield return null;
            }

            // Step 4: Visualize segmentation overlays and labels (15% progress)
            debugger.StartTimer("Segmentation");
            yield return StartCoroutine(VisualizeSegmentationWithProgress());
            performanceMetrics["Segmentation"] = debugger.StopTimer("Segmentation");
            if (isCancelled)
            {
                throw new OperationCanceledException("Segmentation cancelled");
            }

            // Step 5: Generate and place 3D models for detected objects (20% progress)
            debugger.StartTimer("ModelGeneration");
            yield return StartCoroutine(GenerateAndPlaceModelsWithProgress());
            performanceMetrics["ModelGeneration"] = debugger.StopTimer("ModelGeneration");
            if (isCancelled)
            {
                throw new OperationCanceledException("Model generation cancelled");
            }

            // Step 6: Save generated assets (remaining progress)
            if (saveGeneratedAssets)
            {
                yield return StartCoroutine(SaveGeneratedAssets());
            }

            // Complete processing
            UpdateProgress(1.0f, "Terrain generation complete!");
            float totalTime = debugger.StopTimer("TotalGeneration");
            performanceMetrics["Total"] = totalTime;
            debugger.Log($"Terrain generation completed in {totalTime:F1} seconds", Traversify.Core.LogCategory.Process);
            ShowCompletionDetails();
            OnTerrainGenerated?.Invoke(generatedTerrain);
            OnModelsPlaced?.Invoke(generatedObjects);
            OnProcessingComplete?.Invoke();
            FocusCameraOnTerrain();
            // Pause briefly at end
            yield return new WaitForSeconds(2f);
        }

        private IEnumerator VisualizeSegmentationWithProgress()
        {
            UpdateStage("Overlay Visualization", "Creating segmentation overlay...");
            UpdateProgress(0.7f);
            debugger.Log("Visualizing segmentation results", Traversify.Core.LogCategory.Visualization);

            List<GameObject> visualizationObjects = new List<GameObject>();
            int totalItems = (analysisResults?.terrainFeatures.Count ?? 0) + (analysisResults?.mapObjects.Count ?? 0);
            int completed = 0;
            // Create overlay quads for terrain features
            foreach (var feat in analysisResults.terrainFeatures)
            {
                if (isCancelled) yield break;
                GameObject quad = CreateOverlayQuad(feat, generatedTerrain, uploadedMapTexture);
                visualizationObjects.Add(quad);
                completed++;
                float segProgress = (float)completed / totalItems;
                float totalProg = 0.7f + segProgress * 0.2f;
                UpdateProgress(totalProg);
                OnProgressUpdate?.Invoke(totalProg);
                // Fade in the overlay quad
                yield return FadeIn(quad);
            }
            // Create floating labels for map objects
            foreach (var obj in analysisResults.mapObjects)
            {
                if (isCancelled) yield break;
                GameObject label = CreateLabelObject(obj, generatedTerrain);
                visualizationObjects.Add(label);
                completed++;
                float segProgress = (float)completed / totalItems;
                float totalProg = 0.7f + segProgress * 0.2f;
                UpdateProgress(totalProg);
                OnProgressUpdate?.Invoke(totalProg);
                yield return null;
            }
            debugger.Log($"Created {visualizationObjects.Count} visualization objects", Traversify.Core.LogCategory.Visualization);
            // Add visualization objects to generated list for cleanup later
            generatedObjects.AddRange(visualizationObjects);
            UpdateProgress(0.9f, "Segmentation visualization complete");
        }

        private IEnumerator GenerateAndPlaceModelsWithProgress()
        {
            UpdateStage("Generating 3D Models", "Processing object placements...");
            UpdateProgress(0.85f);
            debugger.Log("Generating and placing 3D models", Traversify.Core.LogCategory.Models);

            // If no objects detected, skip
            if (analysisResults.mapObjects.Count == 0)
            {
                UpdateProgress(0.95f, "No objects to place");
                yield break;
            }
            int totalObjects = analysisResults.mapObjects.Count;
            int placedCount = 0;
            // Group objects by type for instancing
            foreach (ObjectGroup group in analysisResults.objectGroups)
            {
                // Generate or retrieve a template mesh for this object type
                Mesh templateMesh = GeneratePlaceholderMeshForType(group.type);
                foreach (MapObject obj in group.objects)
                {
                    if (isCancelled) yield break;
                    // Create a GameObject for the object and place it in the scene
                    GameObject objGo = new GameObject(obj.label ?? group.type);
                    MeshFilter mf = objGo.AddComponent<MeshFilter>();
                    MeshRenderer mr = objGo.AddComponent<MeshRenderer>();
                    mf.sharedMesh = templateMesh;
                    // Assign material (use default or user-provided)
                    mr.material = defaultObjectMaterial ? defaultObjectMaterial : new Material(Shader.Find("Standard"));
                    // Calculate world position on terrain and orientation
                    Vector3 worldPos = GetWorldPositionFromNormalized(obj.position, generatedTerrain);
                    float terrainY = generatedTerrain.SampleHeight(worldPos);
                    worldPos.y = terrainY;
                    objGo.transform.position = worldPos;
                    // Align rotation with terrain normal or keep upright
                    Vector3 normal = generatedTerrain.terrainData.GetInterpolatedNormal(obj.position.x, obj.position.y);
                    if (group.type.ToLower().Contains("tree"))
                    {
                        // Keep trees upright, random yaw
                        objGo.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                    }
                    else
                    {
                        // Align to terrain normal and add random yaw
                        Quaternion align = Quaternion.FromToRotation(Vector3.up, normal);
                        objGo.transform.rotation = align * Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                    }
                    // Random scale variation based on object type
                    float scaleFactor = 1f;
                    string typeLower = group.type.ToLower();
                    if (typeLower.Contains("tree"))
                        scaleFactor = UnityEngine.Random.Range(0.8f, 1.3f);
                    else if (typeLower.Contains("rock") || typeLower.Contains("boulder"))
                        scaleFactor = UnityEngine.Random.Range(0.5f, 1.1f);
                    else if (typeLower.Contains("structure") || typeLower.Contains("building"))
                        scaleFactor = UnityEngine.Random.Range(0.9f, 1.1f);
                    else
                        scaleFactor = UnityEngine.Random.Range(0.9f, 1.2f);
                    objGo.transform.localScale = Vector3.one * scaleFactor;
                    generatedObjects.Add(objGo);
                    placedCount++;
                    // Update progress for each model placed
                    float modelProg = (float)placedCount / totalObjects;
                    float totalProg = 0.85f + modelProg * 0.1f;
                    UpdateProgress(totalProg, $"Placing model {placedCount} of {totalObjects}...");
                    OnProgressUpdate?.Invoke(totalProg);
                    yield return null;
                }
            }
            debugger.Log($"Generated and placed {analysisResults.mapObjects.Count} models", Traversify.Core.LogCategory.Models);
            UpdateProgress(0.95f, "Model generation complete");
        }
// ----- TraversifyManager (Part 4/4): Asset Saving and Utility Functions -----
        private IEnumerator SaveGeneratedAssets()
        {
            UpdateStage("Finalization", "Saving generated terrain and models…");
            UpdateProgress(0.96f);
#if !UNITY_EDITOR
            debugger.LogWarning("Asset saving only supported in the Unity Editor", Traversify.Core.LogCategory.IO);
            yield break;
#else
            try
            {
                string rootPath = assetSavePath.TrimEnd('/', '\\');
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string baseName = string.IsNullOrEmpty(uploadedMapTexture?.name) ? "Map" : uploadedMapTexture.name;
                string folderName = $"{baseName}_{timestamp}";
                string dir = Path.Combine(rootPath, folderName);
                Directory.CreateDirectory(dir);
                AssetDatabase.StartAssetEditing();
                // Save TerrainData asset
                if (generatedTerrain != null)
                {
                    TerrainData tData = generatedTerrain.terrainData;
                    string tdPath = Path.Combine(dir, $"{folderName}_Terrain.asset");
                    AssetDatabase.CreateAsset(tData, tdPath);
                    debugger.Log($"Saved TerrainData → {tdPath}", Traversify.Core.LogCategory.IO);
                }
                // Save analysis output textures
                if (analysisResults?.heightMap != null)
                    SaveTexture(analysisResults.heightMap, Path.Combine(dir, "HeightMap.png"));
                if (analysisResults?.segmentationMap != null)
                    SaveTexture(analysisResults.segmentationMap, Path.Combine(dir, "SegmentationMap.png"));
                // Save scene prefab containing generated objects
                GameObject sceneRoot = new GameObject($"{folderName}_Scene");
                foreach (GameObject go in generatedObjects)
                {
                    if (go != null) Instantiate(go, go.transform.position, go.transform.rotation, sceneRoot.transform);
                }
                string prefabPath = Path.Combine(dir, $"{folderName}_Scene.prefab");
                PrefabUtility.SaveAsPrefabAsset(sceneRoot, prefabPath);
                DestroyImmediate(sceneRoot);
                debugger.Log($"Saved scene prefab → {prefabPath}", Traversify.Core.LogCategory.IO);
                // Save metadata
                if (generateMetadata)
                {
                    SaveMetadata(dir, folderName);
                }
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                UpdateProgress(0.98f, "Assets saved");
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error while saving assets: {ex.Message}", Traversify.Core.LogCategory.IO);
            }
            yield return null;
#endif
        }

        private void SaveTexture(Texture2D tex, string path)
        {
            try
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                debugger.Log($"Saved texture → {path}", Traversify.Core.LogCategory.IO);
            }
            catch (Exception ex)
            {
                debugger.LogError($"SaveTexture failed: {ex.Message}", Traversify.Core.LogCategory.IO);
            }
        }

        private void SaveMetadata(string dir, string sceneName)
        {
            var meta = new
            {
                sceneName,
                generatedAt = DateTime.Now.ToString("u"),
                traversifyVersion = "2.0.1",
                terrain = new { terrainSize, terrainResolution, water = generateWater },
                counts = new
                {
                    features = analysisResults?.terrainFeatures.Count ?? 0,
                    objects = analysisResults?.mapObjects.Count ?? 0,
                    clusters = analysisResults?.objectGroups.Count ?? 0
                },
                perf = performanceMetrics
            };
            string json = JsonUtility.ToJson(meta, true);
            File.WriteAllText(Path.Combine(dir, "metadata.json"), json);
            debugger.Log("Wrote metadata.json", Traversify.Core.LogCategory.IO);
        }

        private string GenerateAnalysisDetails()
        {
            if (analysisResults == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("Terrain Features:");
            foreach (var grp in analysisResults.terrainFeatures.GroupBy(f => f.label))
            {
                sb.AppendLine($"  • {grp.Key}: {grp.Count()}");
            }
            sb.AppendLine("\nObjects:");
            foreach (var grp in analysisResults.mapObjects.GroupBy(o => o.type))
            {
                sb.AppendLine($"  • {grp.Key}: {grp.Count()}");
            }
            return sb.ToString();
        }

        private void ShowCompletionDetails()
        {
            if (detailsText == null) return;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(detailsText.text);
            sb.AppendLine($"\nTerrain: {terrainSize.x}x{terrainSize.z} units | Res {terrainResolution}");
            sb.AppendLine($"Objects placed: {analysisResults?.mapObjects.Count ?? 0}");
            sb.AppendLine($"Clusters: {analysisResults?.objectGroups.Count ?? 0}");
            detailsText.text = sb.ToString();
        }

        private void LogPerformanceMetrics()
        {
            if (performanceMetrics.Count == 0) return;
            debugger.Log("── Performance Metrics ──", Traversify.Core.LogCategory.Process);
            foreach (var entry in performanceMetrics.OrderByDescending(kv => kv.Value))
            {
                debugger.Log($"{entry.Key}: {entry.Value:F2}s", Traversify.Core.LogCategory.Process);
            }
        }

        private void FocusCameraOnTerrain()
        {
            Camera cam = Camera.main;
            if (cam == null || generatedTerrain == null) return;
            Bounds bounds = generatedTerrain.terrainData.bounds;
            Vector3 center = generatedTerrain.transform.position + bounds.center;
            float d = Mathf.Max(terrainSize.x, terrainSize.z) * 0.7f;
            cam.transform.position = center + new Vector3(d, terrainSize.y * 0.8f, -d);
            cam.transform.LookAt(center);
        }

        private UnityEngine.Terrain CreateTerrainFromAnalysis(AnalysisResults results, Texture2D sourceTexture)
        {
            // Create terrain object and data
            GameObject terrainObj = new GameObject("GeneratedTerrain");
            UnityEngine.Terrain terrain = terrainObj.AddComponent<UnityEngine.Terrain>();
            TerrainCollider tCollider = terrainObj.AddComponent<TerrainCollider>();
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = terrainResolution;
            terrainData.size = terrainSize;
            terrain.terrainData = terrainData;
            tCollider.terrainData = terrainData;
            if (terrainMaterial != null)
            {
                terrain.materialTemplate = terrainMaterial;
            }
            // Generate heightmap from analysis results
            float[,] heights = GenerateHeightmap(results, sourceTexture, terrainResolution);
            terrainData.SetHeights(0, 0, heights);
            // Apply basic terrain texture layers
            ApplyTerrainTextures(terrain, results);
            terrainObj.transform.position = Vector3.zero;
            return terrain;
        }

        private void CreateWaterPlane()
        {
            try
            {
                debugger.Log("Creating water plane", Traversify.Core.LogCategory.Terrain);
                waterPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                waterPlane.name = "WaterPlane";
                // Scale plane to terrain size (Unity Plane is 10x10 by default)
                float scaleX = terrainSize.x / 10f;
                float scaleZ = terrainSize.z / 10f;
                waterPlane.transform.localScale = new Vector3(scaleX, 1, scaleZ);
                float waterY = waterHeight * terrainSize.y;
                waterPlane.transform.position = new Vector3(terrainSize.x / 2f, waterY, terrainSize.z / 2f);
                // Apply a simple water-like material
                Renderer rend = waterPlane.GetComponent<Renderer>();
                if (rend != null)
                {
                    Material waterMat = CreateWaterMaterial();
                    rend.material = waterMat;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                // Optionally add wave animation script if available
                // (Placeholder: we could animate UV or vertices for waves)
                debugger.Log("Water plane created", Traversify.Core.LogCategory.Terrain);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error creating water plane: {ex.Message}", Traversify.Core.LogCategory.Terrain);
            }
        }

        private Material CreateWaterMaterial()
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.name = "GeneratedWater";
            mat.color = new Color(0.15f, 0.4f, 0.7f, 0.8f);
            mat.SetFloat("_Glossiness", 0.95f);
            mat.SetFloat("_Metallic", 0.1f);
            // Configure blending for transparency
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            return mat;
        }

        private GameObject CreateOverlayQuad(TerrainFeature feature, UnityEngine.Terrain terrain, Texture2D mapTexture)
        {
            GameObject quad = overlayPrefab ? Instantiate(overlayPrefab) : GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Overlay_{feature.label}";
            quad.transform.SetParent(terrain.transform);
            // Calculate world position and size of overlay from feature bounding box
            Vector3 size = terrain.terrainData.size;
            Vector3 tPos = terrain.transform.position;
            float xMin = tPos.x + (feature.boundingBox.x / mapTexture.width) * size.x;
            float xMax = tPos.x + ((feature.boundingBox.x + feature.boundingBox.width) / mapTexture.width) * size.x;
            float zMin = tPos.z + (feature.boundingBox.y / mapTexture.height) * size.z;
            float zMax = tPos.z + ((feature.boundingBox.y + feature.boundingBox.height) / mapTexture.height) * size.z;
            quad.transform.position = new Vector3((xMin + xMax) / 2f, tPos.y + overlayYOffset, (zMin + zMax) / 2f);
            quad.transform.localScale = new Vector3(xMax - xMin, 1, zMax - zMin);
            quad.transform.rotation = Quaternion.Euler(90, 0, 0);
            // Apply overlay material and color
            Renderer rend = quad.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = overlayMaterial ? Instantiate(overlayMaterial) : new Material(Shader.Find("Standard"));
                mat.color = feature.segmentColor;
                rend.material = mat;
            }
            return quad;
        }

        private GameObject CreateLabelObject(MapObject mapObj, UnityEngine.Terrain terrain)
        {
            GameObject labelObj = labelPrefab ? Instantiate(labelPrefab) : new GameObject($"Label_{mapObj.label}");
            labelObj.transform.SetParent(terrain.transform);
            // Compute world position of the object on terrain
            Vector3 worldPos = GetWorldPositionFromNormalized(mapObj.position, terrain);
            float terrainY = terrain.SampleHeight(worldPos);
            worldPos.y = terrainY + labelYOffset;
            labelObj.transform.position = worldPos;
            labelObj.transform.LookAt(Camera.main.transform);
            // Set text if TextMeshPro is attached
            if (labelObj.TryGetComponent(out TextMeshPro tmp))
            {
                tmp.text = !string.IsNullOrEmpty(mapObj.enhancedDescription) ? mapObj.enhancedDescription : mapObj.label;
                tmp.color = mapObj.segmentColor;
            }
            return labelObj;
        }

        private IEnumerator FadeIn(GameObject obj)
        {
            Renderer rend = obj.GetComponent<Renderer>();
            if (rend == null) yield break;
            Color targetColor = rend.material.color;
            // Start from fully transparent
            Color startColor = targetColor;
            startColor.a = 0f;
            rend.material.color = startColor;
            float elapsed = 0f;
            while (elapsed < overlayFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / overlayFadeDuration);
                Color c = Color.Lerp(startColor, targetColor, t);
                rend.material.color = c;
                yield return null;
            }
            rend.material.color = targetColor;
        }

        private Mesh GeneratePlaceholderMeshForType(string objectType)
        {
            string typeLower = objectType.ToLower();
            if (typeLower.Contains("tree"))
            {
                // Use a cylinder to mimic a tree trunk
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
                return mesh;
            }
            if (typeLower.Contains("rock") || typeLower.Contains("boulder"))
            {
                // Use a sphere for rocks/boulders
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
                return mesh;
            }
            if (typeLower.Contains("structure") || typeLower.Contains("building"))
            {
                // Use a cube for structures/buildings
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
                return mesh;
            }
            // Default placeholder mesh: cube
            GameObject def = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh defaultMesh = def.GetComponent<MeshFilter>().sharedMesh;
            Destroy(def);
            return defaultMesh;
        }

        private Vector3 GetWorldPositionFromNormalized(Vector2 normalizedPos, UnityEngine.Terrain terrain)
        {
            Vector3 terrainOrigin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float worldX = terrainOrigin.x + normalizedPos.x * size.x;
            float worldZ = terrainOrigin.z + normalizedPos.y * size.z;
            float worldY = terrainOrigin.y;
            return new Vector3(worldX, worldY, worldZ);
        }
// ----- TraversifyManager (Part 5/4): AI Analysis Integration and Cleanup -----
        private void LoadModels()
        {
            debugger.Log("Loading AI models...", Traversify.Core.LogCategory.AI);
            try
            {
                if (yoloModel != null)
                {
                    yoloWorker = WorkerFactory.CreateWorker(inferenceBackend, yoloModel);
                }
                else
                {
                    debugger.LogError("YOLO model asset not assigned", Traversify.Core.LogCategory.AI);
                }
                if (useSAM && sam2Model != null)
                {
                    sam2Worker = WorkerFactory.CreateWorker(inferenceBackend, sam2Model);
                }
                if (useFasterRCNN && fasterRcnnModel != null)
                {
                    rcnnWorker = WorkerFactory.CreateWorker(inferenceBackend, fasterRcnnModel);
                }
                if (labelsFile != null)
                {
                    classLabels = labelsFile.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                }
                debugger.Log("AI models loaded successfully", Traversify.Core.LogCategory.AI);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Failed to load models: {ex.Message}", Traversify.Core.LogCategory.AI);
            }
        }

        private IEnumerator AnalyzeImage(Texture2D image, Action<AnalysisResults> onSuccess, Action<string> onError, Action<string, float> onProgress)
        {
            DateTime startTime = DateTime.UtcNow;
            try
            {
                // Step 1: YOLO object detection
                onProgress?.Invoke("YOLO detection", 0.05f);
                if (yoloWorker == null)
                {
                    throw new Exception("YOLO model not loaded");
                }
                int inputSize = useHighQualityAnalysis ? 1024 : 640;
                Tensor yoloInput = PreprocessImage(image, inputSize, inputSize);
                yoloWorker.Execute(new Dictionary<string, Tensor> { { "images", yoloInput } });
                Tensor yoloOutput = yoloWorker.PeekOutput("output");
                List<DetectedObject> detections = new List<DetectedObject>();
                int count = yoloOutput.shape[1];
                for (int i = 0; i < count; i++)
                {
                    float conf = yoloOutput[0, i, 4];
                    if (conf < detectionThreshold) continue;
                    float cx = yoloOutput[0, i, 0] * image.width;
                    float cy = yoloOutput[0, i, 1] * image.height;
                    float bw = yoloOutput[0, i, 2] * image.width;
                    float bh = yoloOutput[0, i, 3] * image.height;
                    int clsId = (int)yoloOutput[0, i, 5];
                    string clsName = (classLabels != null && clsId < classLabels.Length) ? classLabels[clsId] : clsId.ToString();
                    detections.Add(new DetectedObject
                    {
                        classId = clsId,
                        className = clsName,
                        confidence = conf,
                        boundingBox = new Rect(cx - bw / 2f, cy - bh / 2f, bw, bh)
                    });
                }
                yoloInput.Dispose();
                yoloOutput.Dispose();
                if (detections.Count == 0)
                {
                    // No objects detected - return empty results
                    AnalysisResults emptyRes = new AnalysisResults();
                    emptyRes.heightMap = new Texture2D(image.width, image.height);
                    emptyRes.segmentationMap = new Texture2D(image.width, image.height);
                    onSuccess?.Invoke(emptyRes);
                    yield break;
                }

                // Step 2: SAM segmentation for each detection
                onProgress?.Invoke("SAM2 segmentation", 0.25f);
                List<ImageSegment> segments = new List<ImageSegment>();
                if (useSAM && sam2Worker != null)
                {
                    foreach (DetectedObject det in detections.Take(maxObjectsToProcess))
                    {
                        // Prepare prompt tensor (normalized center point of detection)
                        Vector2 centerN = new Vector2(det.boundingBox.center.x / image.width, det.boundingBox.center.y / image.height);
                        Tensor promptTensor = new Tensor(new int[] { 1, 2 }, new float[] { centerN.x, centerN.y });
                        Tensor samInput = PreprocessImage(image, inputSize, inputSize);
                        sam2Worker.Execute(new Dictionary<string, Tensor> {
                            { "image", samInput },
                            { "prompt", promptTensor }
                        });
                        Tensor maskOut = sam2Worker.PeekOutput("masks");
                        Texture2D maskTex = DecodeMaskTensor(maskOut);
                        if (maskTex != null)
                        {
                            segments.Add(new ImageSegment
                            {
                                detectedObject = det,
                                mask = maskTex,
                                boundingBox = det.boundingBox,
                                color = UnityEngine.Random.ColorHSV(0f, 1f, 0.6f, 1f),
                                area = det.boundingBox.width * det.boundingBox.height
                            });
                        }
                        samInput.Dispose();
                        promptTensor.Dispose();
                        maskOut.Dispose();
                        yield return null;
                    }
                }
                else
                {
                    // If SAM not available, create dummy masks covering bounding boxes
                    foreach (DetectedObject det in detections.Take(maxObjectsToProcess))
                    {
                        Texture2D maskTex = new Texture2D(1, 1);
                        maskTex.SetPixel(0, 0, new Color(1, 1, 1, 1));
                        maskTex.Apply();
                        segments.Add(new ImageSegment
                        {
                            detectedObject = det,
                            mask = maskTex,
                            boundingBox = det.boundingBox,
                            color = UnityEngine.Random.ColorHSV(0f, 1f, 0.6f, 1f),
                            area = det.boundingBox.width * det.boundingBox.height
                        });
                    }
                }

                // Step 3: (Optional) Faster R-CNN classification of segments
                if (useFasterRCNN && rcnnWorker != null)
                {
                    onProgress?.Invoke("Analyzing segments", 0.5f);
                    // (Placeholder) Iterate through segments to simulate classification progress
                    for (int i = 0; i < segments.Count; i++)
                    {
                        // In a full implementation, use rcnnWorker to classify each segment here
                        float prog = (float)(i + 1) / segments.Count;
                        onProgress?.Invoke($"Classifying… {prog:P0}", 0.45f + 0.2f * prog);
                        yield return null;
                    }
                }

                // Step 4: (Optional) Enhance descriptions using OpenAI
                if (!string.IsNullOrEmpty(openAIApiKey))
                {
                    onProgress?.Invoke("Enhancing descriptions", 0.7f);
                    for (int i = 0; i < segments.Count; i++)
                    {
                        float prog = (float)(i + 1) / segments.Count;
                        onProgress?.Invoke($"Enhancing… {prog:P0}", 0.7f + 0.15f * prog);
                        // (Placeholder) Simulate AI enhancement delay
                        yield return null;
                    }
                }

                // Step 5: Build final results (with heightmap and segmentation map)
                onProgress?.Invoke("Finalizing analysis", 0.9f);
                AnalysisResults results = BuildResults(image, segments, (float)(DateTime.UtcNow - startTime).TotalSeconds);
                onProgress?.Invoke("Done", 1f);
                onSuccess?.Invoke(results);
            }
            catch (Exception ex)
            {
                debugger.LogError($"MapAnalyzer error: {ex.Message}", Traversify.Core.LogCategory.AI);
                onError?.Invoke(ex.Message);
            }
        }

        private Tensor PreprocessImage(Texture2D src, int width, int height)
        {
            // Resize image to desired resolution and return as Barracuda Tensor (3-channel)
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            Texture2D scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            scaled.Apply();
            RenderTexture.ReleaseTemporary(rt);
            Tensor tensor = new Tensor(scaled, 3);
            Destroy(scaled);
            return tensor;
        }

        private Texture2D DecodeMaskTensor(Tensor maskTensor)
        {
            int mh = maskTensor.shape[1];
            int mw = maskTensor.shape[2];
            Texture2D maskTex = new Texture2D(mw, mh, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[mw * mh];
            for (int y = 0; y < mh; y++)
            {
                for (int x = 0; x < mw; x++)
                {
                    float v = maskTensor[0, y, x];
                    pixels[y * mw + x] = new Color(1f, 1f, 1f, v);
                }
            }
            maskTex.SetPixels(pixels);
            maskTex.Apply();
            return maskTex;
        }

        private AnalysisResults BuildResults(Texture2D sourceImage, List<ImageSegment> segments, float analysisTimeSec)
        {
            AnalysisResults results = new AnalysisResults();
            results.analysisTime = analysisTimeSec;
            // Partition segments into terrain features and map objects
            foreach (ImageSegment seg in segments)
            {
                if (seg.detectedObject.className.StartsWith("cls_"))
                {
                    // Treat as terrain feature
                    TerrainFeature feat = new TerrainFeature
                    {
                        type = "terrain",
                        label = seg.detectedObject.className,
                        boundingBox = seg.boundingBox,
                        segmentMask = seg.mask,
                        segmentColor = seg.color,
                        confidence = seg.detectedObject.confidence,
                        elevation = EstimateElevation(seg.mask)
                    };
                    results.terrainFeatures.Add(feat);
                }
                else
                {
                    // Treat as discrete map object
                    MapObject obj = new MapObject
                    {
                        type = seg.detectedObject.className,
                        label = seg.detectedObject.className,
                        enhancedDescription = seg.detectedObject.className,
                        position = new Vector2(seg.boundingBox.center.x / sourceImage.width, 1f - (seg.boundingBox.center.y / sourceImage.height)),
                        boundingBox = seg.boundingBox,
                        segmentMask = seg.mask,
                        segmentColor = seg.color,
                        confidence = seg.detectedObject.confidence,
                        scale = Vector3.one,
                        rotation = 0f,
                        isGrouped = false
                    };
                    results.mapObjects.Add(obj);
                }
            }
            // Group similar objects by type
            results.objectGroups = results.mapObjects.GroupBy(o => o.type).Select(g => new ObjectGroup
            {
                groupId = Guid.NewGuid().ToString(),
                type = g.Key,
                objects = g.ToList()
            }).ToList();
            // Build heightMap and segmentationMap textures from segments
            results.heightMap = BuildHeightMap(sourceImage.width, sourceImage.height, segments);
            results.segmentationMap = BuildSegmentationMap(sourceImage.width, sourceImage.height, segments);
            return results;
        }

        private float EstimateElevation(Texture2D mask)
        {
            if (mask == null) return 0f;
            return mask.GetPixels().Average(c => c.a);
        }

        private Texture2D BuildHeightMap(int width, int height, IEnumerable<ImageSegment> segments)
        {
            Texture2D heightMap = new Texture2D(width, height, TextureFormat.RFloat, false);
            Color[] pixels = Enumerable.Repeat(Color.black, width * height).ToArray();
            foreach (var seg in segments)
            {
                if (!seg.detectedObject.className.StartsWith("cls_")) continue;
                // For terrain segments, copy mask alpha into height map region
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (seg.boundingBox.Contains(new Vector2(x, y)))
                        {
                            float alpha = seg.mask.GetPixelBilinear(
                                (x - seg.boundingBox.x) / seg.boundingBox.width,
                                (y - seg.boundingBox.y) / seg.boundingBox.height).a;
                            pixels[y * width + x] = new Color(alpha, 0, 0, 1);
                        }
                    }
                }
            }
            heightMap.SetPixels(pixels);
            heightMap.Apply();
            return heightMap;
        }

        private Texture2D BuildSegmentationMap(int width, int height, IEnumerable<ImageSegment> segments)
        {
            Texture2D segMap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = Enumerable.Repeat(Color.clear, width * height).ToArray();
            foreach (var seg in segments)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (seg.boundingBox.Contains(new Vector2(x, y)))
                        {
                            Color maskColor = seg.mask.GetPixelBilinear(
                                (x - seg.boundingBox.x) / seg.boundingBox.width,
                                (y - seg.boundingBox.y) / seg.boundingBox.height);
                            if (maskColor.a > 0.5f)
                            {
                                pixels[y * width + x] = seg.color;
                            }
                        }
                    }
                }
            }
            segMap.SetPixels(pixels);
            segMap.Apply();
            return segMap;
        }

        private void OnDestroy()
        {
            try
            {
                if (isProcessing)
                {
                    CancelProcessing();
                }
                // Clean up UI event listeners
                if (uploadButton != null) uploadButton.onClick.RemoveAllListeners();
                if (generateButton != null) generateButton.onClick.RemoveAllListeners();
                if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
                // Destroy generated objects and textures
                CleanupGeneratedObjects();
                if (uploadedMapTexture != null && Application.isPlaying)
                {
                    Destroy(uploadedMapTexture);
                }
                // Dispose of AI model workers
                yoloWorker?.Dispose();
                sam2Worker?.Dispose();
                rcnnWorker?.Dispose();
                debugger.Log("Traversify destroyed, resources cleaned up", Traversify.Core.LogCategory.System);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during Traversify cleanup: {ex.Message}");
            }
        }

        private void OnValidate()
        {
            // Clamp terrain size and resolution to reasonable values
            terrainSize = new Vector3(
                Mathf.Clamp(terrainSize.x, 10, 5000),
                Mathf.Clamp(terrainSize.y, 10, 1000),
                Mathf.Clamp(terrainSize.z, 10, 5000)
            );
            int[] validRes = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
            int closest = validRes[0];
            int minDiff = Math.Abs(terrainResolution - closest);
            foreach (int res in validRes)
            {
                int diff = Math.Abs(terrainResolution - res);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    closest = res;
                }
            }
            terrainResolution = closest;
            maxObjectsToProcess = Mathf.Clamp(maxObjectsToProcess, 1, 500);
            waterHeight = Mathf.Clamp01(waterHeight);
            processingTimeout = Mathf.Clamp(processingTimeout, 30f, 600f);
            maxConcurrentAPIRequests = Mathf.Clamp(maxConcurrentAPIRequests, 1, 10);
        }

        // UI helper methods
        private void ShowLoadingPanel(bool show) { if (loadingPanel) loadingPanel.SetActive(show); }
        private void UpdateStage(string stage, string details = null)
        {
            if (stageText) stageText.text = stage;
            if (details != null && detailsText) detailsText.text = details;
        }
        private void UpdateProgress(float progress, string details = null)
        {
            if (progressBar) progressBar.value = progress;
            if (progressText) progressText.text = $"{progress * 100:F0}%";
            if (details != null && detailsText) detailsText.text = details;
        }
        private void UpdateStatus(string message, bool isError = false)
        {
            if (statusText)
            {
                statusText.text = message;
                statusText.color = isError ? new Color(1f, 0.3f, 0.3f) : Color.white;
            }
            if (isError)
                debugger.LogError(message, Traversify.Core.LogCategory.UI);
            else
                debugger.Log(message, Traversify.Core.LogCategory.UI);
        }

        // Safe coroutine wrapper for robust exception handling
        private IEnumerator SafeCoroutine(IEnumerator innerRoutine, Action<string> onError)
        {
            bool finished = false;
            Exception caughtEx = null;
            yield return StartCoroutine(ProcessWithErrorHandling(innerRoutine, msg => {
                caughtEx = new Exception(msg);
                finished = true;
            }));
            if (caughtEx != null)
            {
                onError?.Invoke(caughtEx.Message);
            }
        }
        private IEnumerator ProcessWithErrorHandling(IEnumerator routine, Action<string> handleError)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!routine.MoveNext()) break;
                    current = routine.Current;
                }
                catch (Exception ex)
                {
                    handleError?.Invoke(ex.Message);
                    yield break;
                }
                yield return current;
            }
        }
    }
}
