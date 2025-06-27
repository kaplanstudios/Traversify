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
using Unity.AI.Navigation;    // for NavMeshSurface (AI Navigation package)
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

        // Internal component references
        private Traversify.Core.TraversifyDebugger debugger;

        // AI model runtime workers
        private IWorker yoloWorker;
        private IWorker sam2Worker;
        private IWorker rcnnWorker;
        private string[] classLabels;

        // Generated world references
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
            // Singleton pattern enforcement
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Initialize debugger component
            debugger = GetComponent<Traversify.Core.TraversifyDebugger>();
            if (debugger == null) debugger = gameObject.AddComponent<Traversify.Core.TraversifyDebugger>();
            debugger.Log("Traversify singleton initializing...", Traversify.Core.LogCategory.System);

            // Configure inference backend (GPU/CPU) and load AI models
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
            // Configure HTTP client for OpenAI if needed (not making actual calls in this version)
            // (Note: actual OpenAI calls would be implemented here if required)
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
            // Find and assign button references if not set in inspector
            FindButtonReferences();
            try
            {
                // Ensure a container GameObject exists for components (if needed)
                GameObject componentContainer = GameObject.Find("TraversifyComponents");
                if (componentContainer == null)
                {
                    componentContainer = new GameObject("TraversifyComponents");
                    componentContainer.transform.SetParent(transform);
                }
                // TraversifyDebugger is required component (already on this GameObject via RequireComponent)
                // All other generation logic is integrated in this manager now (monolithic), no extra components needed.
            }
            catch (Exception ex)
            {
                debugger.LogError($"Failed to initialize components: {ex.Message}", Traversify.Core.LogCategory.System);
                throw;
            }
        }

        private void FindButtonReferences()
        {
            // If UI buttons were not assigned in inspector, try to find them by name in scene
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
            // If there's a settings panel and button, hook it up
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
                // If the panel background itself is a button (overlay), close on background click
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
            // Apply settings to internal components (if any external components existed)
            // Since logic is integrated, just log any warnings about missing API key
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
            List<string> missingModels = new List<string>();
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
                    missingModels.Add(modelFile);
                    // If model exists in Assets/Scripts/AI/Models, try to copy it over
                    string sourcePath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", modelFile);
                    if (File.Exists(sourcePath))
                    {
                        try
                        {
                            File.Copy(sourcePath, modelPath);
                            debugger.Log($"Copied model {modelFile} to StreamingAssets", Traversify.Core.LogCategory.System);
                            missingModels.Remove(modelFile);
                        }
                        catch (Exception ex)
                        {
                            debugger.LogError($"Failed to copy model {modelFile}: {ex.Message}", Traversify.Core.LogCategory.System);
                        }
                    }
                }
            }
            if (missingModels.Count > 0)
            {
                debugger.LogWarning($"Missing models: {string.Join(", ", missingModels)}. Please ensure model files are present in StreamingAssets/Traversify/Models.", Traversify.Core.LogCategory.System);
            }
        }

        private void LoadPreferences()
        {
            // Optional: Load user preferences for settings (stub - not implemented)
        }

        private void SavePreferences()
        {
            // Optional: Save user preferences for settings (stub - not implemented)
        }

        private void OpenFileExplorer()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("Select Map Image", "", "png,jpg,jpeg");
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
            // Wrap in SafeCoroutine to catch errors
            return SafeCoroutine(InnerLoadImageFromPath(path), ex => {
                debugger.LogError($"Error loading image: {ex}", Traversify.Core.LogCategory.IO);
                UpdateStatus($"Error loading image: {ex}", true);
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
