using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
#if USE_UNITY_INFERENCE
using Unity.AI.Inference;
#endif
using Traversify.Core;
using Traversify.AI;

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
        
        [Header("API Settings")]
        [SerializeField] private string openAIApiKey = "";
        [SerializeField] private int maxConcurrentAPIRequests = 3;
        [SerializeField] private float apiRateLimitDelay = 0.5f;
        
        [Header("Performance Settings")]
        [SerializeField] private bool useGPUAcceleration = true;
        [SerializeField] private Traversify.Core.WorkerFactory.Type inferenceBackend = Traversify.Core.WorkerFactory.Type.Auto;
        [SerializeField] private int processingBatchSize = 5;
        [SerializeField] private bool enableDebugVisualization = false;
        
        [Header("Output Settings")]
        [SerializeField] private bool saveGeneratedAssets = true;
        [SerializeField] private string assetSavePath = "Assets/GeneratedTerrains";
        [SerializeField] private bool generateMetadata = true;

        [Header("Water Settings")]
        [SerializeField] private bool generateWater = true;
        [SerializeField] private float waterHeight = 0.5f;

        // Component references
        private MapAnalyzer mapAnalyzer;
        private TerrainGenerator terrainGenerator;
        private SegmentationVisualizer segmentationVisualizer;
        private ModelGenerator modelGenerator;
        private TraversifyDebugger debugger;
        
        // References to generated objects
        private Texture2D uploadedMapTexture;
        private UnityEngine.Terrain generatedTerrain;
        private GameObject waterPlane;
        
        // Processing state
        private bool isProcessing = false;
        private bool isCancelled = false;
        private AnalysisResults analysisResults;
        private List<GameObject> generatedObjects = new List<GameObject>();
        private Coroutine processingCoroutine;
        private float processingStartTime;
        
        // Performance metrics
        private Dictionary<string, float> performanceMetrics = new Dictionary<string, float>();
        
        // Event callbacks
        public event Action<AnalysisResults> OnAnalysisComplete;
        public event Action<UnityEngine.Terrain> OnTerrainGenerated;
        public event Action<List<GameObject>> OnModelsPlaced;
        public event Action<string> OnError;
        public event Action<float> OnProgressUpdate;
        public event Action OnProcessingComplete;
        public event Action OnProcessingCancelled;

        private void Awake()
        {
            // Singleton pattern
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Get debugger component
            debugger = GetComponent<TraversifyDebugger>();
            if (debugger == null) debugger = gameObject.AddComponent<TraversifyDebugger>();
            
            debugger.Log("Traversify singleton initializing...", LogCategory.System);
            
            // Initialize components
            InitializeComponents();
            
            // Configure inference backend based on settings
            ConfigureInferenceBackend();
        }

        private void Start()
        {
            try
            {
                SetupUIEventHandlers();
                InitializeUI();
                ConfigureComponents();
                ValidateModelFiles();
                LoadPreferences();
                
                debugger.Log($"Traversify v2.0.0 initialized successfully - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", LogCategory.System);
                debugger.Log($"User: {SystemInfo.deviceName}", LogCategory.System);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error during initialization: {ex.Message}\n{ex.StackTrace}", LogCategory.System);
                UpdateStatus("Error during initialization. Check console for details.", true);
            }
        }
        
        private void ConfigureInferenceBackend()
        {
            if (useGPUAcceleration && SystemInfo.supportsComputeShaders)
            {
                inferenceBackend = Traversify.Core.WorkerFactory.Type.ComputePrecompiled;
                debugger.Log($"GPU acceleration enabled - {SystemInfo.graphicsDeviceName}", LogCategory.System);
            }
            else
            {
                inferenceBackend = Traversify.Core.WorkerFactory.Type.CSharpBurst;
                debugger.Log("Using CPU inference (Burst compiled)", LogCategory.System);
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
            if (statusText != null) statusText.text = "Upload a map image to begin";
            if (stageText != null) stageText.text = "";
            if (detailsText != null) detailsText.text = "";
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }
        
        private void InitializeComponents()
        {
            // Find and assign button references if they haven't been set
            FindButtonReferences();
            
            try
            {
                GameObject componentContainer = GameObject.Find("TraversifyComponents");
                if (componentContainer == null)
                {
                    componentContainer = new GameObject("TraversifyComponents");
                    componentContainer.transform.SetParent(transform);
                }
                
                mapAnalyzer = componentContainer.GetComponent<MapAnalyzer>();
                if (mapAnalyzer == null)
                {
                    mapAnalyzer = componentContainer.AddComponent<MapAnalyzer>();
                    debugger.Log("Created MapAnalyzer component", LogCategory.System);
                }
                
                terrainGenerator = componentContainer.GetComponent<TerrainGenerator>();
                if (terrainGenerator == null)
                {
                    terrainGenerator = componentContainer.AddComponent<TerrainGenerator>();
                    debugger.Log("Created TerrainGenerator component", LogCategory.System);
                }
                
                segmentationVisualizer = componentContainer.GetComponent<SegmentationVisualizer>();
                if (segmentationVisualizer == null)
                {
                    segmentationVisualizer = componentContainer.AddComponent<SegmentationVisualizer>();
                    debugger.Log("Created SegmentationVisualizer component", LogCategory.System);
                }
                
                modelGenerator = componentContainer.GetComponent<ModelGenerator>();
                if (modelGenerator == null)
                {
                    modelGenerator = componentContainer.AddComponent<ModelGenerator>();
                    debugger.Log("Created ModelGenerator component", LogCategory.System);
                }
            }
            catch (Exception ex)
            {
                debugger.LogError($"Failed to initialize components: {ex.Message}", LogCategory.System);
                throw;
            }
        }
        
        private void FindButtonReferences()
        {
            // Find UI buttons if they haven't been assigned in the inspector
            if (uploadButton == null)
            {
                uploadButton = GameObject.Find("UploadButton")?.GetComponent<Button>();
                if (uploadButton == null)
                    debugger?.LogWarning("Upload button reference not found", LogCategory.UI);
            }
            
            if (generateButton == null)
            {
                generateButton = GameObject.Find("GenerateButton")?.GetComponent<Button>();
                if (generateButton == null)
                    debugger?.LogWarning("Generate button reference not found", LogCategory.UI);
            }
            
            if (cancelButton == null)
            {
                cancelButton = GameObject.Find("CancelButton")?.GetComponent<Button>();
            }
            
            // Find settings button and panel if needed
            Button settingsButton = GameObject.Find("SettingsButton")?.GetComponent<Button>();
            if (settingsButton != null && settingsPanel != null)
            {
                // Make sure settings button opens the settings panel
                settingsButton.onClick.RemoveAllListeners();
                settingsButton.onClick.AddListener(() => settingsPanel.SetActive(true));
                
                // Find and set up the close button for settings panel
                Button closeButton = settingsPanel.transform.Find("SettingsWindow/CloseButton")?.GetComponent<Button>();
                if (closeButton != null)
                {
                    closeButton.onClick.RemoveAllListeners();
                    closeButton.onClick.AddListener(() => settingsPanel.SetActive(false));
                }
                
                // Add click listener to the panel background to close on click outside
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
            if (mapAnalyzer != null)
            {
                mapAnalyzer.maxObjectsToProcess = maxObjectsToProcess;
                mapAnalyzer.useHighQuality = useHighQualityAnalysis;
                mapAnalyzer.openAIApiKey = openAIApiKey;
                // Add GPU acceleration setting
                mapAnalyzer.useGPU = useGPUAcceleration;
                
                // Make sure API key is correctly set
                if (!string.IsNullOrEmpty(openAIApiKey))
                {
                    Debug.Log($"[Traversify] API key set successfully: {openAIApiKey.Substring(0, 3)}...{openAIApiKey.Substring(openAIApiKey.Length - 3)}");
                }
            }
            
            if (terrainGenerator != null)
            {
                // terrainGenerator.debugger = debugger;
            }
            
            if (segmentationVisualizer != null)
            {
                segmentationVisualizer.debugger = debugger;
                segmentationVisualizer.enableDebugVisualization = enableDebugVisualization;
            }
            
            if (modelGenerator != null)
            {
                modelGenerator.groupSimilarObjects = groupSimilarObjects;
                modelGenerator.maxConcurrentRequests = maxConcurrentAPIRequests;
                modelGenerator.apiRateLimitDelay = apiRateLimitDelay;
                modelGenerator.openAIApiKey = openAIApiKey;
                modelGenerator.debugger = debugger;
            }
            
            if (string.IsNullOrEmpty(openAIApiKey))
            {
                debugger.LogWarning("OpenAI API key is not set. Enhanced descriptions will be limited.", LogCategory.API);
            }
        }
        
        private string GetModelPath(string modelFileName)
        {
            // Changed to only use StreamingAssets path
            return Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", modelFileName);
        }
        
        private void ValidateModelFiles()
        {
            string[] requiredModels = { "yolov8n.onnx", "FasterRCNN-12.onnx", "sam2_hiera_base.onnx" };
            List<string> missingModels = new List<string>();
            
            // Ensure the directory exists
            string modelsDir = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models");
            if (!Directory.Exists(modelsDir))
            {
                Directory.CreateDirectory(modelsDir);
                debugger.Log($"Created models directory at {modelsDir}", LogCategory.System);
            }
            
            foreach (string modelFile in requiredModels)
            {
                string modelPath = GetModelPath(modelFile);
                if (!File.Exists(modelPath))
                {
                    missingModels.Add(modelFile);
                    
                    // Try to copy model from Assets/Scripts/AI/Models if it exists there
                    string sourcePath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", modelFile);
                    if (File.Exists(sourcePath))
                    {
                        try
                        {
                            File.Copy(sourcePath, modelPath);
                            debugger.Log($"Copied model {modelFile} to StreamingAssets", LogCategory.System);
                            missingModels.Remove(modelFile);
                        }
                        catch (Exception ex)
                        {
                            debugger.LogError($"Failed to copy model {modelFile}: {ex.Message}", LogCategory.System);
                        }
                    }
                }
            }
            
            if (missingModels.Count > 0)
            {
                string missing = string.Join(", ", missingModels);
                debugger.LogWarning($"Missing AI model files: {missing}. Run the Setup Wizard to download them.", LogCategory.System);
                UpdateStatus($"Missing AI models: {missing}. Run Setup Wizard from Tools menu.", true);
            }
            else
            {
                debugger.Log("All AI model files found", LogCategory.System);
            }
        }
        
        private void LoadPreferences()
        {
            if (PlayerPrefs.HasKey("Traversify_TerrainSize"))
            {
                float size = PlayerPrefs.GetFloat("Traversify_TerrainSize");
                terrainSize = new Vector3(size, terrainSize.y, size);
            }
            
            if (PlayerPrefs.HasKey("Traversify_TerrainResolution"))
                terrainResolution = PlayerPrefs.GetInt("Traversify_TerrainResolution");
            
            if (PlayerPrefs.HasKey("Traversify_UseHighQuality"))
                useHighQualityAnalysis = PlayerPrefs.GetInt("Traversify_UseHighQuality") == 1;
            
            if (PlayerPrefs.HasKey("Traversify_MaxObjects"))
                maxObjectsToProcess = PlayerPrefs.GetInt("Traversify_MaxObjects");
        }
        
        private void SavePreferences()
        {
            PlayerPrefs.SetFloat("Traversify_TerrainSize", terrainSize.x);
            PlayerPrefs.SetInt("Traversify_TerrainResolution", terrainResolution);
            PlayerPrefs.SetInt("Traversify_UseHighQuality", useHighQualityAnalysis ? 1 : 0);
            PlayerPrefs.SetInt("Traversify_MaxObjects", maxObjectsToProcess);
            PlayerPrefs.Save();
        }
        

        private void OpenFileExplorer()
        {
            if (isProcessing)
            {
                debugger.LogWarning("Cannot open file explorer while processing is in progress", LogCategory.User);
                return;
            }
            
            ClearPreviousResults();
            StartCoroutine(OpenFileDialog());
        }
        
        private void ClearPreviousResults()
        {
            analysisResults = null;
            performanceMetrics.Clear();
            
            if (uploadedMapTexture != null && Application.isPlaying)
            {
                Destroy(uploadedMapTexture);
                uploadedMapTexture = null;
            }
        }
        
        private IEnumerator OpenFileDialog()
        {
            // Use SafeCoroutine pattern instead of try-catch with yield return
            return SafeCoroutine(InnerOpenFileDialog(), ex => {
                debugger.LogError($"Error opening file dialog: {ex}", LogCategory.System);
                UpdateStatus($"Error opening file explorer: {ex}", true);
            });
        }
        
        private IEnumerator InnerOpenFileDialog()
        {
            #if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel(
                "Select Map Image", 
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), 
                "png,jpg,jpeg,bmp,tga,tif,tiff");
                
            if (!string.IsNullOrEmpty(path))
            {
                UpdateStatus("Loading image...");
                yield return StartCoroutine(LoadImageFromPath(path));
            }
            else
            {
                debugger.Log("File selection cancelled", LogCategory.User);
                yield break;
            }
            #else
            debugger.LogWarning("File browser not implemented for this platform", LogCategory.System);
            UpdateStatus("File upload not supported on this platform");
            yield break;
            #endif
        }
        
        private IEnumerator LoadImageFromPath(string path)
        {
            // Remove try-catch and use SafeCoroutine pattern
            return SafeCoroutine(InnerLoadImageFromPath(path), ex => {
                debugger.LogError($"Error loading image: {ex}", LogCategory.IO);
                UpdateStatus($"Error loading image: {ex}", true);
                
                if (generateButton != null) generateButton.interactable = false;
                if (mapPreviewImage != null) mapPreviewImage.gameObject.SetActive(false);
            });
        }
        
        private IEnumerator InnerLoadImageFromPath(string path)
        {
            debugger.Log($"Loading image from: {path}", LogCategory.IO);
            
            FileInfo fileInfo = new FileInfo(path);
            float fileSizeMB = fileInfo.Length / (1024f * 1024f);
            
            if (fileSizeMB > 50)
            {
                debugger.LogWarning($"Large image file ({fileSizeMB:F1} MB) may take longer to process", LogCategory.IO);
            }
            
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
            debugger.Log($"Image loaded successfully: {uploadedMapTexture.width}x{uploadedMapTexture.height} ({fileSizeMB:F1} MB)", LogCategory.IO);
        }
        
        private void ValidateLoadedImage()
        {
            if (uploadedMapTexture.width < 128 || uploadedMapTexture.height < 128)
            {
                throw new Exception("Image is too small. Minimum size is 128x128 pixels.");
            }
            
            if (uploadedMapTexture.width > 8192 || uploadedMapTexture.height > 8192)
            {
                debugger.LogWarning("Very large image detected. Processing may be slow.", LogCategory.IO);
            }
            
            float aspectRatio = (float)uploadedMapTexture.width / uploadedMapTexture.height;
            if (aspectRatio < 0.5f || aspectRatio > 2f)
            {
                debugger.LogWarning($"Unusual aspect ratio ({aspectRatio:F2}). Results may vary.", LogCategory.IO);
            }
        }
        
        private void DisplayLoadedImage()
        {
            if (mapPreviewImage != null)
            {
                mapPreviewImage.texture = uploadedMapTexture;
                mapPreviewImage.gameObject.SetActive(true);
                
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
                debugger.LogWarning("Terrain generation already in progress", LogCategory.User);
                return;
            }
            
            if (uploadedMapTexture == null)
            {
                debugger.LogWarning("No map image uploaded", LogCategory.User);
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
            
            processingCoroutine = StartCoroutine(GenerateTerrainFromMap());
        }
        
        private void CancelProcessing()
        {
            if (!isProcessing) return;
            
            debugger.Log("Cancelling processing...", LogCategory.User);
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
        
        private IEnumerator GenerateTerrainFromMap()
        {
            ShowLoadingPanel(true);
            UpdateProgress(0.05f, "Starting terrain generation...");
            
            debugger.Log("Starting terrain generation process", LogCategory.Process);
            debugger.StartTimer("TotalGeneration");
            
            // Use the SafeCoroutine pattern to handle exceptions in coroutines
            yield return StartCoroutine(SafeCoroutine(InnerGenerateTerrainFromMap(), ex => 
            {
                debugger.LogError($"Error during terrain generation: {ex}", LogCategory.Process);
                UpdateStatus($"Error: {ex}", true);
                OnError?.Invoke(ex);
            }));
            
            LogPerformanceMetrics();
            ResetUI();
        }
        
        private IEnumerator InnerGenerateTerrainFromMap()
        {
            CleanupGeneratedObjects();
            
            // Step 1: Analyze the map using AI models (40% of progress)
            debugger.StartTimer("MapAnalysis");
            yield return StartCoroutine(AnalyzeMapWithProgress());
            performanceMetrics["MapAnalysis"] = debugger.StopTimer("MapAnalysis");
            
            if (isCancelled || analysisResults == null) 
            {
                throw new OperationCanceledException("Analysis cancelled");
            }
            
            // Step 2: Generate terrain based on analysis (20% of progress)
            debugger.StartTimer("TerrainGeneration");
            yield return StartCoroutine(GenerateTerrainWithProgress());
            performanceMetrics["TerrainGeneration"] = debugger.StopTimer("TerrainGeneration");
            
            if (isCancelled || generatedTerrain == null) 
            {
                throw new OperationCanceledException("Terrain generation cancelled");
            }
            
            // Step 3: Create water plane if enabled (5% of progress)
            if (generateWater)
            {
                debugger.StartTimer("WaterCreation");
                UpdateProgress(0.65f, "Creating water features...");
                CreateWaterPlane();
                performanceMetrics["WaterCreation"] = debugger.StopTimer("WaterCreation");
                yield return null;
            }
            
            // Step 4: Visualize segmentation and labels (15% of progress)
            debugger.StartTimer("Segmentation");
            yield return StartCoroutine(VisualizeSegmentationWithProgress());
            performanceMetrics["Segmentation"] = debugger.StopTimer("Segmentation");
            
            if (isCancelled) 
            {
                throw new OperationCanceledException("Segmentation cancelled");
            }
            
            // Step 5: Generate and place 3D models (20% of progress)
            debugger.StartTimer("ModelGeneration");
            yield return StartCoroutine(GenerateAndPlaceModelsWithProgress());
            performanceMetrics["ModelGeneration"] = debugger.StopTimer("ModelGeneration");
            
            if (isCancelled) 
            {
                throw new OperationCanceledException("Model generation cancelled");
            }
            
            // Step 6: Save generated assets if enabled
            if (saveGeneratedAssets)
            {
                yield return StartCoroutine(SaveGeneratedAssets());
            }
            
            // Complete
            UpdateProgress(1.0f, "Terrain generation complete!");
            float totalTime = debugger.StopTimer("TotalGeneration");
            performanceMetrics["Total"] = totalTime;
            
            debugger.Log($"Terrain generation completed in {totalTime:F1} seconds", LogCategory.Process);
            
            ShowCompletionDetails();
            
            OnTerrainGenerated?.Invoke(generatedTerrain);
            OnModelsPlaced?.Invoke(generatedObjects);
            OnProcessingComplete?.Invoke();
            
            FocusCameraOnTerrain();
            
            yield return new WaitForSeconds(2f);
        }
        
        private IEnumerator AnalyzeMapWithProgress()
        {
            UpdateStage("Analyzing Map", "Initializing AI models...");
            UpdateProgress(0.1f);
            
            // Remove try-catch and let SafeCoroutine handle exceptions
            if (analysisResults != null && analysisResults.terrainFeatures.Count > 0)
            {
                debugger.Log("Using cached analysis results", LogCategory.Process);
                UpdateProgress(0.4f);
                yield break;
            }
            
            float lastProgress = 0.1f;
            bool analysisComplete = false;
            string analysisError = null;
            
            debugger.Log("Starting map analysis with AI models", LogCategory.AI);
            
            // Fix: Change the method call from mapAnalyzer.AnalyzeImage to directly use StartCoroutine
            yield return StartCoroutine(AnalyzeImage(uploadedMapTexture, 
                (results) => {
                    analysisResults = results;
                    analysisComplete = true;
                    OnAnalysisComplete?.Invoke(results);
                },
                (error) => {
                    analysisError = error;
                    analysisComplete = true;
                },
                (stage, progress) => {
                    float totalProgress = 0.1f + (progress * 0.3f);
                    if (totalProgress > lastProgress)
                    {
                        UpdateProgress(totalProgress, stage);
                        lastProgress = totalProgress;
                        OnProgressUpdate?.Invoke(totalProgress);
                    }
                }
            ));
            
            while (!analysisComplete && !isCancelled)
            {
                yield return null;
            }
            
            if (isCancelled) yield break;
            
            if (!string.IsNullOrEmpty(analysisError))
            {
                throw new Exception($"Map analysis failed: {analysisError}");
            }
            
            if (analysisResults == null)
            {
                throw new Exception("Map analysis did not return any results");
            }
            
            string summary = $"Detected {analysisResults.terrainFeatures.Count} terrain features and {analysisResults.mapObjects.Count} objects";
            UpdateStage("Analysis Complete", summary);
            debugger.Log($"Map analysis complete: {summary}", LogCategory.AI);
            
            if (detailsText != null)
            {
                detailsText.text = GenerateAnalysisDetails();
            }
            
            UpdateProgress(0.4f);
        }
        
        private IEnumerator GenerateTerrainWithProgress()
        {
            UpdateStage("Generating Terrain", "Creating heightmap...");
            UpdateProgress(0.45f);
            
            // Remove try-catch and let SafeCoroutine handle exceptions
            debugger.Log("Generating terrain from analysis results", LogCategory.Terrain);
            
            generatedTerrain = terrainGenerator.GenerateTerrain(
                analysisResults, 
                uploadedMapTexture,
                terrainSize,
                terrainResolution,
                terrainMaterial
            );
            
            if (generatedTerrain == null)
            {
                throw new Exception("Terrain generation failed");
            }
            
            generatedObjects.Add(generatedTerrain.gameObject);
            
            UpdateProgress(0.5f, "Applying terrain textures...");
            yield return new WaitForSeconds(0.2f);
            
            UpdateProgress(0.55f, "Smoothing terrain features...");
            yield return new WaitForSeconds(0.2f);
            
            UpdateProgress(0.6f, "Finalizing terrain...");
            yield return new WaitForSeconds(0.1f);
            
            debugger.Log("Terrain generation complete", LogCategory.Terrain);
        }
        
        private void CreateWaterPlane()
        {
            try
            {
                debugger.Log("Creating water plane", LogCategory.Terrain);
                
                waterPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                waterPlane.name = "WaterPlane";
                
                float scaleX = terrainSize.x / 10f;
                float scaleZ = terrainSize.z / 10f;
                waterPlane.transform.localScale = new Vector3(scaleX, 1, scaleZ);
                
                float waterY = waterHeight * terrainSize.y;
                waterPlane.transform.position = new Vector3(terrainSize.x / 2f, waterY, terrainSize.z / 2f);
                
                Renderer renderer = waterPlane.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material waterMaterial = CreateWaterMaterial();
                    renderer.material = waterMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                
                WaterAnimation waterAnim = waterPlane.AddComponent<WaterAnimation>();
                waterAnim.waveSpeed = 0.5f;
                waterAnim.waveHeight = 0.1f;
                
                generatedObjects.Add(waterPlane);
                
                debugger.Log("Water plane created successfully", LogCategory.Terrain);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error creating water plane: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        private Material CreateWaterMaterial()
        {
            Material waterMaterial = new Material(Shader.Find("Standard"));
            waterMaterial.name = "GeneratedWater";
            waterMaterial.color = new Color(0.15f, 0.4f, 0.7f, 0.8f);
            waterMaterial.SetFloat("_Glossiness", 0.95f);
            waterMaterial.SetFloat("_Metallic", 0.1f);
            
            waterMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            waterMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            waterMaterial.SetInt("_ZWrite", 0);
            waterMaterial.DisableKeyword("_ALPHATEST_ON");
            waterMaterial.EnableKeyword("_ALPHABLEND_ON");
            waterMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            waterMaterial.renderQueue = 3000;
            
            return waterMaterial;
        }
        
        private IEnumerator VisualizeSegmentationWithProgress()
        {
            UpdateStage("Visualizing Results", "Creating segmentation overlay...");
            UpdateProgress(0.7f);
            
            // Remove try-catch and let SafeCoroutine handle exceptions
            debugger.Log("Visualizing segmentation", LogCategory.Visualization);
            
            List<GameObject> visualizationObjects = new List<GameObject>();
            bool visualizationComplete = false;
            string visualizationError = null;
            
            // Add timeout mechanism to prevent infinite waiting
            float timeout = 30f; // 30 seconds timeout
            float startTime = Time.time;
            
            yield return StartCoroutine(segmentationVisualizer.VisualizeSegments(
                analysisResults, 
                generatedTerrain,
                uploadedMapTexture,
                (objects) => {
                    visualizationObjects = objects;
                    visualizationComplete = true;
                },
                (error) => {
                    visualizationError = error;
                    visualizationComplete = true;
                },
                (progress) => {
                    // Allocate 20% range for segmentation to move from 70% to 90%
                    float totalProgress = 0.7f + (progress * 0.2f);
                    UpdateProgress(totalProgress);
                    OnProgressUpdate?.Invoke(totalProgress);
                }
            ));
            
            // Wait with timeout
            while (!visualizationComplete && !isCancelled && (Time.time - startTime) < timeout)
            {
                yield return null;
            }
            
            // Check for timeout
            if (!visualizationComplete && !isCancelled)
            {
                debugger.LogWarning("Segmentation visualization timed out, skipping this step", LogCategory.Visualization);
                UpdateProgress(0.9f, "Segmentation visualization skipped due to timeout");
                yield break;
            }
            
            if (isCancelled) yield break;
            
            if (!string.IsNullOrEmpty(visualizationError))
            {
                debugger.LogWarning($"Segmentation visualization failed: {visualizationError}", LogCategory.Visualization);
                UpdateProgress(0.9f, "Segmentation visualization failed, continuing...");
                yield break;
            }
            
            generatedObjects.AddRange(visualizationObjects);
            
            UpdateProgress(0.9f, "Segmentation visualization complete");
            debugger.Log($"Created {visualizationObjects.Count} visualization objects", LogCategory.Visualization);
        }
        
        private IEnumerator GenerateAndPlaceModelsWithProgress()
        {
            UpdateStage("Generating 3D Models", "Processing object placements...");
            UpdateProgress(0.85f);
            
            // Remove try-catch and let SafeCoroutine handle exceptions
            debugger.Log("Generating and placing 3D models", LogCategory.Models);
            
            List<GameObject> modelObjects = new List<GameObject>();
            bool generationComplete = false;
            string generationError = null;
            
            yield return StartCoroutine(modelGenerator.GenerateAndPlaceModels(
                analysisResults,
                generatedTerrain,
                (objects) => {
                    modelObjects = objects;
                    generationComplete = true;
                },
                (error) => {
                    generationError = error;
                    generationComplete = true;
                },
                (current, total) => {
                    float modelProgress = (float)current / total;
                    float totalProgress = 0.85f + (modelProgress * 0.1f);
                    UpdateProgress(totalProgress, $"Placing model {current} of {total}...");
                    OnProgressUpdate?.Invoke(totalProgress);
                }
            ));
            
            while (!generationComplete && !isCancelled)
            {
                yield return null;
            }
            
            if (isCancelled) yield break;
            
            if (!string.IsNullOrEmpty(generationError))
            {
                throw new Exception($"Model generation failed: {generationError}");
            }
            
            generatedObjects.AddRange(modelObjects);
            
            UpdateProgress(0.95f, "Model generation complete");
            debugger.Log($"Generated and placed {modelObjects.Count} 3D models", LogCategory.Models);
        }
        
        private IEnumerator SaveGeneratedAssets()
        {
            UpdateStage("Saving Assets", "Saving generated terrain and models...");
            UpdateProgress(0.96f);
            
            try
            {
                #if UNITY_EDITOR
                if (!Directory.Exists(assetSavePath))
                {
                    Directory.CreateDirectory(assetSavePath);
                    UnityEditor.AssetDatabase.Refresh();
                }
                
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string mapName = uploadedMapTexture.name ?? "Map";
                string folderName = $"{mapName}_{timestamp}";
                string savePath = Path.Combine(assetSavePath, folderName);
                
                Directory.CreateDirectory(savePath);
                
                if (generatedTerrain != null)
                {
                    TerrainData terrainData = generatedTerrain.terrainData;
                    string terrainPath = Path.Combine(savePath, $"{folderName}_Terrain.asset");
                    UnityEditor.AssetDatabase.CreateAsset(terrainData, terrainPath);
                    debugger.Log($"Saved terrain data to {terrainPath}", LogCategory.IO);
                }
                
                if (analysisResults != null)
                {
                    if (analysisResults.heightMap != null)
                    {
                        SaveTexture(analysisResults.heightMap, Path.Combine(savePath, "HeightMap.png"));
                    }
                    
                    if (analysisResults.segmentationMap != null)
                    {
                        SaveTexture(analysisResults.segmentationMap, Path.Combine(savePath, "SegmentationMap.png"));
                    }
                }
                
                if (generateMetadata)
                {
                    SaveMetadata(savePath, folderName);
                }
                
                GameObject sceneRoot = new GameObject($"{folderName}_Scene");
                foreach (var obj in generatedObjects)
                {
                    if (obj != null)
                    {
                        GameObject copy = UnityEditor.PrefabUtility.InstantiatePrefab(obj) as GameObject;
                        if (copy == null) copy = Instantiate(obj);
                        copy.transform.SetParent(sceneRoot.transform);
                    }
                }
                
                string prefabPath = Path.Combine(savePath, $"{folderName}_Scene.prefab");
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(sceneRoot, prefabPath);
                Destroy(sceneRoot);
                
                debugger.Log($"Saved scene prefab to {prefabPath}", LogCategory.IO);
                
                UnityEditor.AssetDatabase.Refresh();
                UpdateProgress(0.98f, "Assets saved successfully");
                #else
                debugger.LogWarning("Asset saving is only available in Unity Editor", LogCategory.IO);
                #endif
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error saving assets: {ex.Message}", LogCategory.IO);
            }
            
            yield return null;
        }
        
        private void SaveTexture(Texture2D texture, string path)
        {
            try
            {
                byte[] bytes = texture.EncodeToPNG();
                File.WriteAllBytes(path, bytes);
                debugger.Log($"Saved texture to {path}", LogCategory.IO);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Failed to save texture: {ex.Message}", LogCategory.IO);
            }
        }
        
        private void SaveMetadata(string savePath, string sceneName)
        {
            try
            {
                var metadata = new
                {
                    sceneName = sceneName,
                    generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    generatedBy = "dkaplan73",
                    traversifyVersion = "2.0.0",
                    sourceMap = new
                    {
                        name = uploadedMapTexture.name,
                        width = uploadedMapTexture.width,
                        height = uploadedMapTexture.height
                    },
                    terrain = new
                    {
                        size = terrainSize,
                        resolution = terrainResolution,
                        hasWater = generateWater,
                        waterHeight = waterHeight
                    },
                    analysis = new
                    {
                        terrainFeatures = analysisResults?.terrainFeatures.Count ?? 0,
                        mapObjects = analysisResults?.mapObjects.Count ?? 0,
                        objectGroups = analysisResults?.objectGroups.Count ?? 0,
                        analysisTime = analysisResults?.analysisTime ?? 0
                    },
                    performance = performanceMetrics,
                    settings = new
                    {
                        quality = useHighQualityAnalysis ? "High" : "Fast",
                        maxObjects = maxObjectsToProcess,
                        groupSimilar = groupSimilarObjects,
                        gpuAcceleration = useGPUAcceleration
                    }
                };
                
                string json = JsonUtility.ToJson(metadata, true);
                string metadataPath = Path.Combine(savePath, "metadata.json");
                File.WriteAllText(metadataPath, json);
                
                debugger.Log($"Saved metadata to {metadataPath}", LogCategory.IO);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Failed to save metadata: {ex.Message}", LogCategory.IO);
            }
        }
        
        private string GenerateAnalysisDetails()
        {
            if (analysisResults == null) return "";
            
            System.Text.StringBuilder details = new System.Text.StringBuilder();
            
            Dictionary<string, int> terrainCounts = new Dictionary<string, int>();
            foreach (var feature in analysisResults.terrainFeatures)
            {
                if (!terrainCounts.ContainsKey(feature.type))
                    terrainCounts[feature.type] = 0;
                terrainCounts[feature.type]++;
            }
            
            details.AppendLine("Terrain Features:");
            foreach (var kvp in terrainCounts.OrderByDescending(x => x.Value))
            {
                details.AppendLine($"  • {kvp.Key}: {kvp.Value}");
            }
            
            Dictionary<string, int> objectCounts = new Dictionary<string, int>();
            foreach (var obj in analysisResults.mapObjects)
            {
                if (!objectCounts.ContainsKey(obj.type))
                    objectCounts[obj.type] = 0;
                objectCounts[obj.type]++;
            }
            
            if (objectCounts.Count > 0)
            {
                details.AppendLine("\nDetected Objects:");
                foreach (var kvp in objectCounts.OrderByDescending(x => x.Value))
                {
                    details.AppendLine($"  • {kvp.Key}: {kvp.Value}");
                }
            }
            
            if (analysisResults.objectGroups.Count > 0)
            {
                details.AppendLine($"\nObject Groups: {analysisResults.objectGroups.Count}");
            }
            
            return details.ToString();
        }
        
        private void ShowCompletionDetails()
        {
            if (detailsText == null) return;
            
            System.Text.StringBuilder summary = new System.Text.StringBuilder();
            
            summary.AppendLine("Generation Complete!");
            summary.AppendLine();
            
            if (performanceMetrics.ContainsKey("Total"))
            {
                summary.AppendLine($"Total Time: {performanceMetrics["Total"]:F1}s");
                summary.AppendLine("Breakdown:");
                
                foreach (var metric in performanceMetrics.Where(m => m.Key != "Total").OrderByDescending(m => m.Value))
                {
                    float percentage = (metric.Value / performanceMetrics["Total"]) * 100;
                    summary.AppendLine($"  • {metric.Key}: {metric.Value:F1}s ({percentage:F0}%)");
                }
            }
            
            summary.AppendLine();
            summary.AppendLine("Generated:");
            summary.AppendLine($"  • Terrain: {terrainSize.x}x{terrainSize.z} units");
            summary.AppendLine($"  • Resolution: {terrainResolution}x{terrainResolution}");
            
            if (analysisResults != null)
            {
                summary.AppendLine($"  • Terrain features: {analysisResults.terrainFeatures.Count}");
                summary.AppendLine($"  • Objects placed: {analysisResults.mapObjects.Count}");
            }
            
            summary.AppendLine($"  • Total GameObjects: {generatedObjects.Count}");
            
            long memoryUsage = GC.GetTotalMemory(false);
            summary.AppendLine($"\nMemory Usage: {memoryUsage / (1024 * 1024)} MB");
            
            detailsText.text = summary.ToString();
        }
        
        private void LogPerformanceMetrics()
        {
            if (performanceMetrics.Count == 0) return;
            
            debugger.Log("=== Performance Metrics ===", LogCategory.Process);
            
            foreach (var metric in performanceMetrics.OrderByDescending(m => m.Value))
            {
                debugger.Log($"{metric.Key}: {metric.Value:F2}s", LogCategory.Process);
            }
            
            if (performanceMetrics.ContainsKey("Total"))
            {
                float total = performanceMetrics["Total"];
                debugger.Log($"Total processing time: {total:F2}s ({total/60:F1} minutes)", LogCategory.Process);
            }
        }
        
        private void FocusCameraOnTerrain()
        {
            try
            {
                Camera mainCamera = Camera.main;
                if (mainCamera == null || generatedTerrain == null) return;
                
                Bounds terrainBounds = generatedTerrain.terrainData.bounds;
                Vector3 terrainCenter = generatedTerrain.transform.position + terrainBounds.center;
                
                float distance = Mathf.Max(terrainSize.x, terrainSize.z) * 0.8f;
                float height = distance * 0.5f + terrainSize.y;
                
                Vector3 cameraPosition = terrainCenter + new Vector3(distance * 0.5f, height, -distance * 0.5f);
                mainCamera.transform.position = cameraPosition;
                mainCamera.transform.LookAt(terrainCenter);
                
                float maxDistance = Vector3.Distance(cameraPosition, terrainCenter) + Mathf.Max(terrainSize.x, terrainSize.z);
                if (mainCamera.farClipPlane < maxDistance)
                {
                    mainCamera.farClipPlane = maxDistance * 1.5f;
                }
                
                debugger.Log("Camera focused on terrain", LogCategory.Process);
            }
            catch (Exception ex)
            {
                debugger.LogWarning($"Failed to focus camera: {ex.Message}", LogCategory.Process);
            }
        }
        
        private void CleanupGeneratedObjects()
        {
            debugger.Log($"Cleaning up {generatedObjects.Count} previously generated objects", LogCategory.System);
            
            foreach (var obj in generatedObjects)
            {
                if (obj != null)
                {
                    if (Application.isPlaying)
                        Destroy(obj);
                    else
                        DestroyImmediate(obj);
                }
            }
            
            generatedObjects.Clear();
            generatedTerrain = null;
            waterPlane = null;
        }
        
        private void UpdateStatus(string message, bool isError = false)
        {
            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = isError ? new Color(1f, 0.3f, 0.3f) : Color.white;
            }
            
            if (isError)
                debugger.LogError(message, LogCategory.UI);
            else
                debugger.Log(message, LogCategory.UI);
        }
        
        private void UpdateProgress(float progress, string details = null)
        {
            if (progressBar != null) progressBar.value = progress;
            if (progressText != null) progressText.text = $"{(progress * 100):F0}%";
            if (details != null && detailsText != null) detailsText.text = details;
        }
        
        private void UpdateStage(string stage, string details = null)
        {
            if (stageText != null) stageText.text = stage;
            if (details != null && detailsText != null) detailsText.text = details;
        }
        
        private void ShowLoadingPanel(bool show)
        {
            if (loadingPanel != null) loadingPanel.SetActive(show);
        }
        
        private void ResetUI()
        {
            ShowLoadingPanel(false);
            if (uploadButton != null) uploadButton.interactable = true;
            if (generateButton != null) generateButton.interactable = uploadedMapTexture != null;
            isProcessing = false;
            isCancelled = false;
            processingCoroutine = null;
        }
        
        private void OnDestroy()
        {
            try
            {
                if (isProcessing)
                {
                    CancelProcessing();
                }
                
                if (uploadButton != null) uploadButton.onClick.RemoveAllListeners();
                if (generateButton != null) generateButton.onClick.RemoveAllListeners();
                if (cancelButton != null) cancelButton.onClick.RemoveAllListeners();
                
                CleanupGeneratedObjects();
                
                if (uploadedMapTexture != null && Application.isPlaying)
                {
                    Destroy(uploadedMapTexture);
                }
                
                debugger.Log("Traversify destroyed, resources cleaned up", LogCategory.System);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during Traversify cleanup: {ex.Message}");
            }
        }
        
        private void OnValidate()
        {
            terrainSize = new Vector3(
                Mathf.Clamp(terrainSize.x, 10, 5000),
                Mathf.Clamp(terrainSize.y, 10, 1000),
                Mathf.Clamp(terrainSize.z, 10, 5000)
            );
            
            int[] validResolutions = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
            int closest = validResolutions[0];
            int minDiff = Mathf.Abs(terrainResolution - closest);
            
            foreach (int res in validResolutions)
            {
                int diff = Mathf.Abs(terrainResolution - res);
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
            apiRateLimitDelay = Mathf.Clamp(apiRateLimitDelay, 0.1f, 5f);
            processingBatchSize = Mathf.Clamp(processingBatchSize, 1, 20);
        }
        
        // Public API for external scripts
        public void LoadMapImage(Texture2D mapTexture)
        {
            if (mapTexture == null)
            {
                debugger.LogError("Cannot load null map texture", LogCategory.API);
                return;
            }
            
            if (isProcessing)
            {
                debugger.LogWarning("Cannot load new map while processing", LogCategory.API);
                return;
            }
            
            ClearPreviousResults();
            uploadedMapTexture = mapTexture;
            
            try
            {
                ValidateLoadedImage();
                DisplayLoadedImage();
                
                if (generateButton != null) generateButton.interactable = true;
                
                UpdateStatus($"Map image loaded ({mapTexture.width}x{mapTexture.height})");
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error loading map image: {ex.Message}", LogCategory.API);
                UpdateStatus($"Error: {ex.Message}", true);
            }
        }
        
        public void GenerateTerrain()
        {
            if (uploadedMapTexture == null)
            {
                debugger.LogError("No map image loaded", LogCategory.API);
                return;
            }
            
            StartTerrainGeneration();
        }
        
        public void SetTerrainSize(float size)
        {
            terrainSize = new Vector3(size, terrainSize.y, size);
            SavePreferences();
        }
        
        public void SetTerrainHeight(float height)
        {
            terrainSize = new Vector3(terrainSize.x, height, terrainSize.z);
            SavePreferences();
        }
        
        public void SetQuality(bool useHighQuality)
        {
            useHighQualityAnalysis = useHighQuality;
            SavePreferences();
        }
        
        public void SetMaxObjects(int maxObjects)
        {
            maxObjectsToProcess = maxObjects;
            SavePreferences();
        }
        
        public void SetOpenAIKey(string apiKey)
        {
            openAIApiKey = apiKey;
            ConfigureComponents();
        }
        
        public AnalysisResults GetAnalysisResults()
        {
            return analysisResults;
        }
        
        public UnityEngine.Terrain GetGeneratedTerrain()
        {
            return generatedTerrain;
        }
        
        public List<GameObject> GetGeneratedObjects()
        {
            return new List<GameObject>(generatedObjects);
        }
        
        public bool IsProcessing()
        {
            return isProcessing;
        }
        
        public float GetProgress()
        {
            return progressBar != null ? progressBar.value : 0f;
        }
        
        public Dictionary<string, float> GetPerformanceMetrics()
        {
            return new Dictionary<string, float>(performanceMetrics);
        }
        
        public void ClearScene()
        {
            CleanupGeneratedObjects();
            UpdateStatus("Scene cleared");
        }
        
        // Helper method for code that contains try-catch blocks with yield returns
        private IEnumerator ProcessWithErrorHandling(IEnumerator routine, Action<string> onError)
        {
            bool success = true;
            Exception caughtException = null;
            
            while (true)
            {
                object current = null;
                try
                {
                    if (routine.MoveNext() == false)
                    {
                        break;
                    }
                    current = routine.Current;
                }
                catch (Exception ex)
                {
                    success = false;
                    caughtException = ex;
                    debugger.LogError($"Error in coroutine: {ex.Message}", LogCategory.System);
                    break;
                }
                
                yield return current;
            }
            
            if (!success && caughtException != null)
            {
                onError?.Invoke(caughtException.Message);
            }
        }
        
        // Use this pattern for methods that previously had try-catch blocks with yield returns
        private IEnumerator SafeCoroutine(IEnumerator routine, Action<string> onError = null)
        {
            yield return StartCoroutine(ProcessWithErrorHandling(routine, onError));
        }
        
        // Example usage for a method that previously had try-catch with yield:
        /*
        public IEnumerator ProcessImage()
        {
            // Instead of:
            try
            {
                yield return Something();  // This causes the error
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
            
            // Use this pattern:
            yield return StartCoroutine(SafeCoroutine(InnerProcessImage(), ex => LogError(ex)));
        }
        
        private IEnumerator InnerProcessImage()
        {
            // Code that might throw exceptions
            yield return Something();
            // More code...
        }
        */
        
        // Add missing AnalyzeImage method to fix the 'MapAnalyzer' does not contain a definition for 'AnalyzeImage' error
        private IEnumerator AnalyzeImage(Texture2D imageTexture, 
            Action<AnalysisResults> onComplete, 
            Action<string> onError,
            Action<string, float> onProgress)
        {
            // Use SafeCoroutine pattern instead of try-catch with yield return
            return SafeCoroutine(InnerAnalyzeImage(imageTexture, onComplete, onError, onProgress), ex => {
                debugger.LogError($"Error in image analysis: {ex}", LogCategory.AI);
                onError?.Invoke(ex);
            });
        }
        
        private IEnumerator InnerAnalyzeImage(Texture2D imageTexture,
            Action<AnalysisResults> onComplete,
            Action<string> onError,
            Action<string, float> onProgress)
        {
            // Implementation of image analysis
            debugger.Log("Analyzing image using internal implementation", LogCategory.AI);
            
            // Simulate analysis work
            AnalysisResults results = new AnalysisResults();
            
            // Here we would typically perform the actual analysis
            // This is a placeholder implementation that would be replaced with actual analysis code
            results.terrainFeatures = new List<TerrainFeature>();
            results.mapObjects = new List<MapObject>();
            results.objectGroups = new List<ObjectGroup>();
            
            // Simulate some work
            for (float i = 0; i < 1.0f; i += 0.1f)
            {
                onProgress?.Invoke($"Processing analysis step {(i*10):F0}/10", i);
                yield return new WaitForSeconds(0.1f);
            }
            
            // Success
            onComplete?.Invoke(results);
        }
    }
}
