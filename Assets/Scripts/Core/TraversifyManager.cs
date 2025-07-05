using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;
using Traversify.AI; 
using Traversify.Terrain;
using SimpleFileBrowser;
using USentis = Unity.Sentis; 

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Traversify.Core 
{
    /// <summary>
    /// Main manager class for the Traversify terrain generation system.
    /// Handles AI-powered map analysis and procedural terrain generation.
    /// </summary>
    public class TraversifyManager : MonoBehaviour
    {
        #region Singleton 
        
        private static TraversifyManager _instance;
        public static TraversifyManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<TraversifyManager>();
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region UI References
        
        [Header("UI References")]
        [Tooltip("Button for uploading map images")]
        [SerializeField] private Button _uploadButton;
        
        [Tooltip("Button for starting terrain generation")]
        [SerializeField] private Button _generateButton;
        
        [Tooltip("Button for cancelling processing")]
        [SerializeField] private Button _cancelButton;
        
        [Tooltip("Image component for map preview")]
        [SerializeField] private RawImage _mapPreviewImage;
        
        [Tooltip("Text component for status messages")]
        [SerializeField] private TMP_Text _statusText;
        
        [Tooltip("Loading panel GameObject")]
        [SerializeField] private GameObject _loadingPanel;
        
        [Tooltip("Progress bar slider")]
        [SerializeField] private Slider _progressBar;
        
        [Tooltip("Progress percentage text")]
        [SerializeField] private TMP_Text _progressText;
        
        [Tooltip("Current stage text")]
        [SerializeField] private TMP_Text _stageText;
        
        [Tooltip("Details text component")]
        [SerializeField] private TMP_Text _detailsText;
        
        [Tooltip("Settings panel GameObject")]
        [SerializeField] private GameObject _settingsPanel;
        
        #endregion
        
        #region Terrain Settings
        
        [Header("Terrain Settings")]
        [Tooltip("Size of the generated terrain in Unity units")]
        [SerializeField] private Vector3 _terrainSize = new Vector3(500, 100, 500);
        
        [Tooltip("Height of the terrain in Unity units")]
        [SerializeField] private float _terrainHeight = 100f;
        
        [Tooltip("Resolution of the terrain heightmap (must be 2^n + 1)")]
        [SerializeField] private int _heightmapResolution = 513;
        
        [Tooltip("Resolution for terrain detail maps")]
        [SerializeField] private int _detailResolution = 1024;
        
        [Tooltip("Resolution of the terrain heightmap (must be 2^n + 1)")]
        [SerializeField] private int _terrainResolution = 513;
        
        [Tooltip("Multiplier for heightmap values")]
        [SerializeField] private float _heightMapMultiplier = 1.0f;
        
        [Tooltip("Material to apply to generated terrain")]
        [SerializeField] private Material _terrainMaterial;
        
        #endregion
        
        #region Debugging
        
        private TraversifyDebugger _debugger;
        
        #endregion
        
        #region Settings
        
        [Header("Analysis Settings")]
        [Tooltip("Use high quality analysis (slower but more accurate)")]
        [SerializeField] private bool _useHighQualityAnalysis = true;
        
        [Tooltip("Group similar objects for optimization")]
        [SerializeField] private bool _groupSimilarObjects = true;
        
        [Tooltip("Maximum objects to process")]
        [SerializeField] private int _maxObjectsToProcess = 100;
        
        [Tooltip("Processing timeout in seconds")]
        [SerializeField] private float _processingTimeout = 300f;
        
        [Tooltip("Similarity threshold for object instancing")]
        [Range(0f, 1f)]
        [SerializeField] private float _instancingSimilarity = 0.8f;
        
        [Tooltip("Detection confidence threshold")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _detectionThreshold = 0.25f; // More reasonable default
        
        [Tooltip("Non-maximum suppression threshold")]
        [SerializeField] private float _nmsThreshold = 0.45f;
        
        [Tooltip("Use Faster R-CNN for fine-grained classification")]
        [SerializeField] private bool _useFasterRCNN = true;
        
        [Tooltip("Use SAM for segmentation")]
        [SerializeField] private bool _useSAM = true;
        
        [Header("API Settings")]
        [Tooltip("OpenAI API key")] 
        [SerializeField] private string _openAIApiKey = "";
        
        [Tooltip("Maximum concurrent API requests")]
        [SerializeField] private int _maxConcurrentAPIRequests = 3;
        
        [Tooltip("API rate limit delay in seconds")]
        [SerializeField] private float _apiRateLimitDelay = 0.5f;
        
        [Header("Performance Settings")]
        [Tooltip("Use GPU acceleration when available")]
        [SerializeField] private bool _useGPUAcceleration = true;
        
        [Tooltip("Inference backend type")]
        [SerializeField] private USentis.BackendType _inferenceBackend = USentis.BackendType.GPUCompute;
        
        [Tooltip("Processing batch size")]
        [SerializeField] private int _processingBatchSize = 5;
        
        [Tooltip("Enable debug visualization")]
        [SerializeField] private bool _enableDebugVisualization = false;
        
        [Header("AI Model Assets")]
        [Tooltip("YOLO model asset")]
        [SerializeField] private USentis.ModelAsset _yoloModel;
        
        [Tooltip("SAM2 model asset")]
        [SerializeField] private USentis.ModelAsset _sam2Model;
        
        [Tooltip("Faster R-CNN model asset")]
        [SerializeField] private USentis.ModelAsset _fasterRcnnModel;
        
        [Tooltip("Class labels text asset")]
        [SerializeField] private TextAsset _labelsFile;
        
        [Header("Output Settings")]
        [Tooltip("Save generated assets")]
        [SerializeField] private bool _saveGeneratedAssets = true;
        
        [Tooltip("Asset save path")]
        [SerializeField] private string _assetSavePath = "Assets/GeneratedTerrains";
        
        [Tooltip("Generate metadata for saved assets")]
        [SerializeField] private bool _generateMetadata = true;
        
        [Header("Water Settings")]
        [Tooltip("Generate water")]
        [SerializeField] private bool _generateWater = true;
        
        [Tooltip("Water height as fraction of terrain height")]
        [Range(0f, 1f)]
        [SerializeField] private float _waterHeight = 0.5f;
        
        [Header("Visualization Settings")]
        [Tooltip("Overlay prefab for visualization")]
        [SerializeField] private GameObject _overlayPrefab;
        
        [Tooltip("Label prefab for visualization")]
        [SerializeField] private GameObject _labelPrefab;
        
        [Tooltip("Material for overlays")]
        [SerializeField] private Material _overlayMaterial;
        
        [Tooltip("Y offset for overlays")]
        [SerializeField] private float _overlayYOffset = 0.5f;
        
        [Tooltip("Y offset for labels")]
        [SerializeField] private float _labelYOffset = 2.0f;
        
        [Tooltip("Fade duration for overlays")]
        [Range(0f, 3f)]
        [SerializeField] private float _overlayFadeDuration = 0.5f;
        
        [Header("Object Generation Settings")]
        [Tooltip("Default material for generated objects")]
        [SerializeField] private Material _defaultObjectMaterial;
        
        #endregion
        
        #region Public Properties
        
        // Terrain Settings Properties
        public Vector3 terrainSize {
            get => _terrainSize;
            set => _terrainSize = value;
        }
        
        public int terrainResolution {
            get => _terrainResolution;
            set => _terrainResolution = value;
        }
        
        public bool generateWater {
            get => _generateWater;
            set => _generateWater = value;
        }
        
        public float waterHeight {
            get => _waterHeight;
            set => _waterHeight = value;
        }
        
        // AI Settings Properties
        public string openAIApiKey {
            get => _openAIApiKey;
            set => _openAIApiKey = value;
        }
        
        public bool useHighQualityAnalysis {
            get => _useHighQualityAnalysis;
            set => _useHighQualityAnalysis = value;
        }
        
        public float detectionThreshold {
            get => _detectionThreshold;
            set => _detectionThreshold = value;
        }
        
        public bool useFasterRCNN {
            get => _useFasterRCNN;
            set => _useFasterRCNN = value;
        }
        
        public bool useSAM {
            get => _useSAM;
            set => _useSAM = value;
        }
        
        public int maxObjectsToProcess {
            get => _maxObjectsToProcess;
            set => _maxObjectsToProcess = value;
        }
        
        public bool groupSimilarObjects {
            get => _groupSimilarObjects;
            set => _groupSimilarObjects = value;
        }
        
        public float instancingSimilarity {
            get => _instancingSimilarity;
            set => _instancingSimilarity = value;
        }
        
        // Performance Settings Properties
        public int maxConcurrentAPIRequests {
            get => _maxConcurrentAPIRequests;
            set => _maxConcurrentAPIRequests = value;
        }
        
        public int processingBatchSize {
            get => _processingBatchSize;
            set => _processingBatchSize = value;
        }
        
        public float processingTimeout {
            get => _processingTimeout;
            set => _processingTimeout = value;
        }
        
        public float apiRateLimitDelay {
            get => _apiRateLimitDelay;
            set => _apiRateLimitDelay = value;
        }
        
        public bool useGPUAcceleration {
            get => _useGPUAcceleration;
            set => _useGPUAcceleration = value;
        }
        
        // Advanced Settings Properties
        public bool enableDebugVisualization {
            get => _enableDebugVisualization;
            set => _enableDebugVisualization = value;
        }
        
        public bool saveGeneratedAssets {
            get => _saveGeneratedAssets;
            set => _saveGeneratedAssets = value;
        }
        
        public bool generateMetadata {
            get => _generateMetadata;
            set => _generateMetadata = value;
        }
        
        public string assetSavePath {
            get => _assetSavePath;
            set => _assetSavePath = value;
        }
        
        #endregion
        
        #region Private Fields
        
        // System state
        private bool _isInitialized = false;
        private bool _isProcessing = false;
        private bool _isCancelled = false;
        private CancellationTokenSource _cancellationTokenSource;
        
        // Component references
        private MapAnalyzer _mapAnalyzer;
        private TerrainGenerator _terrainGenerator;
        private ModelGenerator _modelGenerator;
        private SegmentationVisualizer _segmentationVisualizer;
        private OpenAIResponse _openAIResponse;
        
        // Model workers - use our custom IWorker interface
        private IWorker _yoloWorker;
        private IWorker _sam2Worker;
        private IWorker _rcnnWorker;
        private string[] _classLabels;
        
        // Processing state
        private Texture2D _uploadedMapTexture;
        private AnalysisResults _analysisResults;
        private UnityEngine.Terrain _generatedTerrain;
        private GameObject _waterPlane;
        private List<GameObject> _generatedObjects = new List<GameObject>();
        private Coroutine _processingCoroutine;
        private float _processingStartTime;
        
        // Performance metrics
        private Dictionary<string, float> _performanceMetrics = new Dictionary<string, float>();
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when analysis completes.
        /// </summary>
        public event Action<AnalysisResults> OnAnalysisComplete;
        
        /// <summary>
        /// Fired when terrain generation completes.
        /// </summary>
        public event Action<UnityEngine.Terrain> OnTerrainGenerated;
        
        /// <summary>
        /// Fired when model placement completes.
        /// </summary>
        public event Action<List<GameObject>> OnModelsPlaced;
        
        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public event Action<string> OnError;
        
        /// <summary>
        /// Fired when progress updates.
        /// </summary>
        public event Action<float> OnProgressUpdate;
        
        /// <summary>
        /// Fired when processing completes.
        /// </summary>
        public event Action OnProcessingComplete;
        
        /// <summary>
        /// Fired when processing is cancelled.
        /// </summary>
        public event Action OnProcessingCancelled;
        
        /// <summary>
        /// Event fired when generation completes.
        /// </summary>
        public event Action OnGenerationComplete;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake() {
            // Singleton enforcement
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize debugger
            _debugger = GetComponent<TraversifyDebugger>();
            if (_debugger == null) {
                _debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            _debugger.Log("TraversifyManager initializing...", LogCategory.System);
            
            // Configure inference backend
            if (_useGPUAcceleration && SystemInfo.supportsComputeShaders) {
                _inferenceBackend = USentis.BackendType.GPUCompute;
                _debugger.Log($"GPU acceleration enabled - {SystemInfo.graphicsDeviceName}", LogCategory.System);
            }
            else {
                _inferenceBackend = USentis.BackendType.CPU;
                _debugger.Log("Using CPU inference (Burst compiled)", LogCategory.System);
            }
            
            // Load AI models
            LoadModels();
        }
        
        private void Start() {
            try {
                SetupUIEventHandlers();
                InitializeUI();
                InitializeComponents();
                ConfigureComponents();
                ValidateModelFiles();
                LoadPreferences();
                
                _isInitialized = true;
                _debugger.Log($"TraversifyManager initialized - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", LogCategory.System);
                _debugger.Log($"User: dkaplan73", LogCategory.System);
            }
            catch (Exception ex) {
                _debugger.LogError($"Error during initialization: {ex.Message}\n{ex.StackTrace}", LogCategory.System);
                UpdateStatus("Error during initialization. Check console for details.", true);
            }
        }
        
        private void OnDestroy() {
            try {
                if (_isProcessing) {
                    CancelProcessing();
                }
                
                // Clean up UI event listeners
                if (_uploadButton != null) _uploadButton.onClick.RemoveAllListeners();
                if (_generateButton != null) _generateButton.onClick.RemoveAllListeners();
                if (_cancelButton != null) _cancelButton.onClick.RemoveAllListeners();
                
                // Clean up generated objects
                CleanupGeneratedObjects();
                
                // Dispose model workers
                _yoloWorker?.Dispose();
                _sam2Worker?.Dispose();
                _rcnnWorker?.Dispose();
                
                _debugger.Log("TraversifyManager destroyed, resources cleaned up", LogCategory.System);
            }
            catch (Exception ex) {
                Debug.LogError($"Error during TraversifyManager cleanup: {ex.Message}");
            }
        }
        
        private void OnValidate() {
            // Clamp terrain size and resolution to reasonable values
            _terrainSize = new Vector3(
                Mathf.Clamp(_terrainSize.x, 10, 5000),
                Mathf.Clamp(_terrainSize.y, 10, 1000),
                Mathf.Clamp(_terrainSize.z, 10, 5000)
            );
            
            // Ensure terrain resolution is valid (must be 2^n + 1)
            int[] validRes = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
            int closest = validRes[0];
            int minDiff = Math.Abs(_terrainResolution - closest);
            
            foreach (int res in validRes) {
                int diff = Math.Abs(_terrainResolution - res);
                if (diff < minDiff) {
                    minDiff = diff;
                    closest = res;
                }
            }
            
            _terrainResolution = closest;
            _maxObjectsToProcess = Mathf.Clamp(_maxObjectsToProcess, 1, 500);
            _waterHeight = Mathf.Clamp01(_waterHeight);
            _processingTimeout = Mathf.Clamp(_processingTimeout, 30f, 600f);
            _maxConcurrentAPIRequests = Mathf.Clamp(_maxConcurrentAPIRequests, 1, 10);
        }
        
        #endregion
        
        #region Initialization
        
        private void SetupUIEventHandlers() {
            if (_uploadButton != null) {
                _uploadButton.onClick.RemoveAllListeners();
                _uploadButton.onClick.AddListener(OpenFileExplorer);
                _uploadButton.interactable = true; // Ensure upload button is enabled
            }
            
            if (_generateButton != null) {
                _generateButton.onClick.RemoveAllListeners();
                _generateButton.onClick.AddListener(StartTerrainGeneration);
                _generateButton.interactable = false;
            }
            
            if (_cancelButton != null) {
                _cancelButton.onClick.RemoveAllListeners();
                _cancelButton.onClick.AddListener(CancelProcessing);
            }
            
            // Set up settings panel toggling if available
            Button settingsButton = GameObject.Find("SettingsButton")?.GetComponent<Button>();
            if (settingsButton != null && _settingsPanel != null) {
                settingsButton.onClick.RemoveAllListeners();
                settingsButton.onClick.AddListener(() => _settingsPanel.SetActive(true));
                
                Button closeButton = _settingsPanel.transform.Find("SettingsWindow/CloseButton")?.GetComponent<Button>();
                if (closeButton != null) {
                    closeButton.onClick.RemoveAllListeners();
                    closeButton.onClick.AddListener(() => _settingsPanel.SetActive(false));
                }
                
                Button overlayButton = _settingsPanel.GetComponent<Button>();
                if (overlayButton != null) {
                    overlayButton.onClick.RemoveAllListeners();
                    overlayButton.onClick.AddListener(() => _settingsPanel.SetActive(false));
                }
            }
        }
        
        private void InitializeUI() {
            if (_loadingPanel != null) _loadingPanel.SetActive(false);
            if (_progressBar != null) _progressBar.value = 0;
            if (_progressText != null) _progressText.text = "0%";
            if (_stageText != null) _stageText.text = "";
            if (_detailsText != null) _detailsText.text = "";
        }
        
        private void InitializeComponents() {
            // Find UI references if not set in inspector
            FindUIReferences();
            
            try {
                // Create component container
                GameObject componentContainer = GameObject.Find("TraversifyComponents");
                if (componentContainer == null) {
                    componentContainer = new GameObject("TraversifyComponents");
                    componentContainer.transform.SetParent(transform);
                }
                
                // Initialize MapAnalyzer with better error handling
                _mapAnalyzer = FindOrCreateMapAnalyzer(componentContainer);
                if (_mapAnalyzer == null) {
                    _debugger.LogError("Failed to create MapAnalyzer component", LogCategory.System);
                    throw new System.Exception("MapAnalyzer initialization failed");
                }
                
                // Initialize TerrainGenerator
                _terrainGenerator = FindOrCreateComponent<TerrainGenerator>(componentContainer);
                
                // Initialize ModelGenerator
                _modelGenerator = FindOrCreateComponent<ModelGenerator>(componentContainer);
                
                // Initialize SegmentationVisualizer
                _segmentationVisualizer = FindOrCreateComponent<SegmentationVisualizer>(componentContainer);
                
                // Initialize OpenAIResponse
                _openAIResponse = FindOrCreateComponent<OpenAIResponse>(componentContainer);
                
                _debugger.Log("All components initialized successfully", LogCategory.System);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to initialize components: {ex.Message}", LogCategory.System);
                throw;
            }
        }
        
        private MapAnalyzer FindOrCreateMapAnalyzer(GameObject parent) {
            // First try to find existing MapAnalyzer
            MapAnalyzer analyzer = parent.GetComponentInChildren<MapAnalyzer>();
            if (analyzer != null) {
                _debugger.Log("Found existing MapAnalyzer component", LogCategory.System);
                // Ensure it's properly initialized
                if (!analyzer.IsInitialized) {
                    try {
                        // Create configuration for MapAnalyzer
                        var config = new Dictionary<string, object>
                        {
                            ["useHighQuality"] = _useHighQualityAnalysis,
                            ["confidenceThreshold"] = _detectionThreshold,
                            ["nmsThreshold"] = _nmsThreshold,
                            ["useGPU"] = _useGPUAcceleration,
                            ["maxObjectsToProcess"] = _maxObjectsToProcess,
                            ["openAIApiKey"] = _openAIApiKey,
                            ["yoloModel"] = _yoloModel,
                            ["sam2Model"] = _sam2Model,
                            ["fasterRcnnModel"] = _fasterRcnnModel,
                            ["labelsFile"] = _labelsFile,
                            ["yoloWorker"] = _yoloWorker,
                            ["sam2Worker"] = _sam2Worker,
                            ["rcnnWorker"] = _rcnnWorker,
                            ["classLabels"] = _classLabels
                        };
                        
                        if (analyzer.Initialize(config)) {
                            _debugger.Log("Initialized existing MapAnalyzer component", LogCategory.System);
                        } else {
                            _debugger.LogError("Failed to initialize existing MapAnalyzer", LogCategory.System);
                            DestroyImmediate(analyzer.gameObject);
                            analyzer = null;
                        }
                    }
                    catch (Exception ex) {
                        _debugger.LogError($"Failed to initialize existing MapAnalyzer: {ex.Message}", LogCategory.System);
                        DestroyImmediate(analyzer.gameObject);
                        analyzer = null;
                    }
                }
                if (analyzer != null) return analyzer;
            }
            
            // Try to create new MapAnalyzer
            try {
                GameObject go = new GameObject("MapAnalyzer");
                go.transform.SetParent(parent.transform);
                analyzer = go.AddComponent<MapAnalyzer>();
                
                if (analyzer != null) {
                    _debugger.Log("Created MapAnalyzer component successfully", LogCategory.System);
                    
                    // Initialize the new component with proper configuration
                    try {
                        var config = new Dictionary<string, object>
                        {
                            ["useHighQuality"] = _useHighQualityAnalysis,
                            ["confidenceThreshold"] = _detectionThreshold,
                            ["nmsThreshold"] = _nmsThreshold,
                            ["useGPU"] = _useGPUAcceleration,
                            ["maxObjectsToProcess"] = _maxObjectsToProcess,
                            ["openAIApiKey"] = _openAIApiKey,
                            ["yoloModel"] = _yoloModel,
                            ["sam2Model"] = _sam2Model,
                            ["fasterRcnnModel"] = _fasterRcnnModel,
                            ["labelsFile"] = _labelsFile,
                            ["yoloWorker"] = _yoloWorker,
                            ["sam2Worker"] = _sam2Worker,
                            ["rcnnWorker"] = _rcnnWorker,
                            ["classLabels"] = _classLabels
                        };
                        
                        if (analyzer.Initialize(config)) {
                            _debugger.Log("MapAnalyzer component initialized successfully", LogCategory.System);
                            return analyzer;
                        } else {
                            _debugger.LogError("Failed to initialize new MapAnalyzer", LogCategory.System);
                            DestroyImmediate(go);
                            analyzer = null;
                        }
                    }
                    catch (Exception ex) {
                        _debugger.LogError($"Failed to initialize new MapAnalyzer: {ex.Message}", LogCategory.System);
                        DestroyImmediate(go);
                        analyzer = null;
                    }
                }
            }
            catch (Exception ex) {
                _debugger.LogError($"Error creating MapAnalyzer: {ex.Message}", LogCategory.System);
            }
            
            _debugger.LogError("All attempts to create MapAnalyzer failed", LogCategory.System);
            return null;
        }
        
        private T FindOrCreateComponent<T>(GameObject parent) where T : Component {
            T component = parent.GetComponentInChildren<T>();
            if (component == null) {
                GameObject go = new GameObject(typeof(T).Name);
                go.transform.SetParent(parent.transform);
                component = go.AddComponent<T>();
            }
            return component;
        }
        
        private void FindUIReferences() {
            if (_uploadButton == null) {
                _uploadButton = GameObject.Find("UploadButton")?.GetComponent<Button>();
                if (_uploadButton == null) {
                    _debugger?.LogWarning("Upload button reference not found", LogCategory.UI);
                }
            }
            
            if (_generateButton == null) {
                _generateButton = GameObject.Find("GenerateButton")?.GetComponent<Button>();
                if (_generateButton == null) {
                    _debugger?.LogWarning("Generate button reference not found", LogCategory.UI);
                }
            }
            
            if (_mapPreviewImage == null) {
                _mapPreviewImage = GameObject.Find("MapPreviewImage")?.GetComponent<RawImage>();
            }
            
            if (_statusText == null) {
                _statusText = GameObject.Find("StatusText")?.GetComponent<TMP_Text>();
            }
            
            if (_loadingPanel == null) {
                _loadingPanel = GameObject.Find("LoadingPanel");
            }
            
            if (_progressBar == null) {
                _progressBar = GameObject.Find("ProgressBar")?.GetComponent<Slider>();
            }
            
            if (_progressText == null) {
                _progressText = GameObject.Find("ProgressText")?.GetComponent<TMP_Text>();
            }
            
            if (_stageText == null) {
                _stageText = GameObject.Find("StageText")?.GetComponent<TMP_Text>();
            }
            
            if (_detailsText == null) {
                _detailsText = GameObject.Find("DetailsText")?.GetComponent<TMP_Text>();
            }
            
            if (_cancelButton == null) {
                _cancelButton = GameObject.Find("CancelButton")?.GetComponent<Button>();
            }
        }
        
        private void ConfigureComponents() {
            // Configure MapAnalyzer - Skip if already initialized with configuration
            if (_mapAnalyzer != null && !_mapAnalyzer.IsInitialized) {
                // Set debugger reference if the component has this property
                var debuggerProp = _mapAnalyzer.GetType().GetProperty("debugger");
                if (debuggerProp != null) debuggerProp.SetValue(_mapAnalyzer, _debugger);
                
                _mapAnalyzer.useHighQuality = _useHighQualityAnalysis;
                _mapAnalyzer.confidenceThreshold = _detectionThreshold;
                _mapAnalyzer.nmsThreshold = _nmsThreshold;
                _mapAnalyzer.useGPU = _useGPUAcceleration;
                _mapAnalyzer.maxObjectsToProcess = _maxObjectsToProcess;
                _mapAnalyzer.openAIApiKey = _openAIApiKey;
                
                // Pass the loaded model assets to MapAnalyzer to avoid duplicate initialization
                SetPropertyIfExists(_mapAnalyzer, "yoloModel", _yoloModel);
                SetPropertyIfExists(_mapAnalyzer, "sam2Model", _sam2Model);
                SetPropertyIfExists(_mapAnalyzer, "fasterRcnnModel", _fasterRcnnModel);
                SetPropertyIfExists(_mapAnalyzer, "labelsFile", _labelsFile);
                
                // Pass the loaded workers to MapAnalyzer to reuse them
                SetPropertyIfExists(_mapAnalyzer, "yoloWorker", _yoloWorker);
                SetPropertyIfExists(_mapAnalyzer, "sam2Worker", _sam2Worker);
                SetPropertyIfExists(_mapAnalyzer, "rcnnWorker", _rcnnWorker);
                SetPropertyIfExists(_mapAnalyzer, "classLabels", _classLabels);
                
                // Now that models are configured, initialize them
                _mapAnalyzer.InitializeModelsIfNeeded();
            }
            
            // Log the MapAnalyzer status
            if (_mapAnalyzer != null) {
                if (_mapAnalyzer.IsInitialized)
                {
                    _debugger.Log("✅ MapAnalyzer configured and initialized successfully", LogCategory.AI);
                }
                else
                {
                    _debugger.LogError("❌ MapAnalyzer configuration failed", LogCategory.AI);
                }
            }
            
            // Configure TerrainGenerator
            if (_terrainGenerator != null) {
                // Set debugger reference if the component has this property
                var debuggerProp = _terrainGenerator.GetType().GetProperty("debugger");
                if (debuggerProp != null) debuggerProp.SetValue(_terrainGenerator, _debugger);
                
                // Use reflection to set properties that might not exist
                SetPropertyIfExists(_terrainGenerator, "terrainWidth", Mathf.RoundToInt(_terrainSize.x));
                SetPropertyIfExists(_terrainGenerator, "terrainLength", Mathf.RoundToInt(_terrainSize.z));
                SetPropertyIfExists(_terrainGenerator, "terrainHeight", _terrainSize.y);
                SetPropertyIfExists(_terrainGenerator, "waterThreshold", _waterHeight);
                SetPropertyIfExists(_terrainGenerator, "terrainSize", _terrainSize);
                SetPropertyIfExists(_terrainGenerator, "terrainResolution", _terrainResolution);
                SetPropertyIfExists(_terrainGenerator, "heightMapMultiplier", _heightMapMultiplier);
                SetPropertyIfExists(_terrainGenerator, "generateWater", _generateWater);
                SetPropertyIfExists(_terrainGenerator, "waterLevel", _waterHeight);
                SetPropertyIfExists(_terrainGenerator, "applyErosion", true);
                SetPropertyIfExists(_terrainGenerator, "erosionIterations", 1000);
            }
            
            // Configure ModelGenerator
            if (_modelGenerator != null) {
                // Set debugger reference if the component has this property
                var debuggerProp = _modelGenerator.GetType().GetProperty("debugger");
                if (debuggerProp != null) debuggerProp.SetValue(_modelGenerator, _debugger);
                
                _modelGenerator.groupSimilarObjects = _groupSimilarObjects;
                _modelGenerator.instancingSimilarity = _instancingSimilarity;
                _modelGenerator.openAIApiKey = _openAIApiKey;
                SetPropertyIfExists(_modelGenerator, "generationTimeout", _processingTimeout);
                SetPropertyIfExists(_modelGenerator, "maxConcurrentRequests", _maxConcurrentAPIRequests);
                SetPropertyIfExists(_modelGenerator, "apiRateLimitDelay", _apiRateLimitDelay);
            }
            
            // Configure SegmentationVisualizer
            if (_segmentationVisualizer != null) {
                // Set debugger reference if the component has this property
                var debuggerProp = _segmentationVisualizer.GetType().GetProperty("debugger");
                if (debuggerProp != null) debuggerProp.SetValue(_segmentationVisualizer, _debugger);
                
                _segmentationVisualizer.enableDebugVisualization = _enableDebugVisualization;
                SetPropertyIfExists(_segmentationVisualizer, "overlayPrefab", _overlayPrefab);
                SetPropertyIfExists(_segmentationVisualizer, "labelPrefab", _labelPrefab);
                SetPropertyIfExists(_segmentationVisualizer, "overlayMaterial", _overlayMaterial);
                SetPropertyIfExists(_segmentationVisualizer, "overlayYOffset", _overlayYOffset);
                SetPropertyIfExists(_segmentationVisualizer, "labelYOffset", _labelYOffset);
                SetPropertyIfExists(_segmentationVisualizer, "fadeDuration", _overlayFadeDuration);
            }
            
            // Configure OpenAIResponse
            if (_openAIResponse != null) {
                _openAIResponse.SetApiKey(_openAIApiKey);
            }
            
            // Log configuration warnings
            if (string.IsNullOrEmpty(_openAIApiKey)) {
                _debugger.LogWarning("OpenAI API key is not set. Enhanced descriptions will be limited.", LogCategory.API);
            }
        }
        
        private void SetPropertyIfExists(object target, string propertyName, object value) {
            try {
                var prop = target.GetType().GetProperty(propertyName);
                if (prop != null && prop.CanWrite) {
                    prop.SetValue(target, value);
                }
            }
            catch (Exception ex) {
                _debugger?.LogWarning($"Could not set property {propertyName}: {ex.Message}", LogCategory.System);
            }
        }
        
        private string GetModelPath(string modelFileName) {
            return Path.Combine(Application.dataPath, "Scripts", "AI", "Models", modelFileName);
        }
        
        private void ValidateModelFiles() {
            string[] requiredModels = { "yolov8n.onnx", "FasterRCNN-12.onnx", "sam2_hiera_base.onnx" };
            List<string> missingModels = new List<string>();
            
            string modelsDir = Path.Combine(Application.dataPath, "Scripts", "AI", "Models");
            if (!Directory.Exists(modelsDir)) {
                Directory.CreateDirectory(modelsDir);
                _debugger.Log($"Created models directory at {modelsDir}", LogCategory.System);
            }
            
            foreach (string modelFile in requiredModels) {
                string modelPath = GetModelPath(modelFile);
                if (!File.Exists(modelPath)) {
                    missingModels.Add(modelFile);
                    
                    // Check if model exists in old StreamingAssets location and copy it
                    string oldPath = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", modelFile);
                    if (File.Exists(oldPath)) {
                        try {
                            File.Copy(oldPath, modelPath);
                            _debugger.Log($"Migrated model {modelFile} from StreamingAssets to AI/Models", LogCategory.System);
                            missingModels.Remove(modelFile);
                        }
                        catch (Exception ex) {
                            _debugger.LogError($"Failed to migrate model {modelFile}: {ex.Message}", LogCategory.System);
                        }
                    }
                }
            }
            
            if (missingModels.Count > 0) {
                _debugger.LogWarning($"Missing models: {string.Join(", ", missingModels)}. Please ensure model files are present in Assets/Scripts/AI/Models/.", LogCategory.System);
            }
        }
        
        private void LoadModels() {
            _debugger.Log("Loading AI models from Assets/Scripts/AI/Models/...", LogCategory.AI);
            
            try {
                // First, validate that the AI models directory exists
                string modelsPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models");
                if (!Directory.Exists(modelsPath)) {
                    _debugger.LogError($"Models directory not found at: {modelsPath}", LogCategory.AI);
                    _debugger.LogError("CRITICAL: Cannot load AI models - terrain generation will not work", LogCategory.AI);
                    return;
                }
                
                _debugger.Log($"Models directory found at: {modelsPath}", LogCategory.AI);
                
                // Load YOLO model (required)
                LoadYOLOFromAIModels();
                
                // Load SAM2 model (optional)
                if (_useSAM) {
                    LoadSAM2FromAIModels();
                }
                
                // Load Faster R-CNN model (optional)
                if (_useFasterRCNN) {
                    LoadFasterRCNNFromAIModels();
                }
                
                // Load class labels
                LoadClassLabels();
                
                // Log comprehensive model loading summary
                LogModelLoadingSummary();
                
            }
            catch (Exception ex) {
                _debugger.LogError($"Fatal error during model loading: {ex.Message}\n{ex.StackTrace}", LogCategory.AI);
            }
        }
        
        private void LoadYOLOFromAIModels() {
            try {
                string modelPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", "yolov8n.onnx");
                _debugger.Log($"Loading YOLO model from: {modelPath}", LogCategory.AI);
                
                if (!File.Exists(modelPath)) {
                    _debugger.LogError($"YOLO model file not found at: {modelPath}", LogCategory.AI);
                    
                    // Try inspector fallback
                    if (_yoloModel != null) {
                        try {
                            _yoloWorker = CreateSentisWorker(_yoloModel, USentis.BackendType.GPUCompute);
                            _debugger.Log("✓ YOLO model loaded successfully from inspector assignment", LogCategory.AI);
                            return;
                        }
                        catch (Exception ex) {
                            _debugger.LogError($"Failed to load YOLO model from inspector: {ex.Message}", LogCategory.AI);
                        }
                    }
                    
                    _debugger.LogError("CRITICAL: YOLO model could not be loaded from any source", LogCategory.AI);
                    return;
                }
                
                _debugger.Log($"YOLO model file found, size: {new FileInfo(modelPath).Length / 1024 / 1024:F1} MB", LogCategory.AI);
                
                #if UNITY_EDITOR
                // In editor, try to load via AssetDatabase as ModelAsset
                string assetPath = "Assets/Scripts/AI/Models/yolov8n.onnx";
                var yoloAsset = AssetDatabase.LoadAssetAtPath<USentis.ModelAsset>(assetPath);
                if (yoloAsset != null) {
                    _yoloWorker = CreateSentisWorker(yoloAsset, USentis.BackendType.GPUCompute);
                    _debugger.Log("✓ YOLO model loaded successfully from AI/Models (Editor)", LogCategory.AI);
                    return;
                }
                else {
                    _debugger.LogWarning("YOLO ONNX file found but not imported as Sentis Model. Please reimport the file in Unity and set import type to 'Sentis Model'.", LogCategory.AI);
                }
                #endif
                
                // Try inspector fallback for runtime builds
                if (_yoloModel != null) {
                    try {
                        _yoloWorker = CreateSentisWorker(_yoloModel, USentis.BackendType.GPUCompute);
                        _debugger.Log("✓ YOLO model loaded successfully from inspector assignment", LogCategory.AI);
                        return;
                    }
                    catch (Exception ex) {
                        _debugger.LogError($"Failed to load YOLO model from inspector: {ex.Message}", LogCategory.AI);
                    }
                }
                
                _debugger.LogError("YOLO model exists in AI/Models but needs to be imported as Sentis Model asset. Please select the .onnx file in Unity and set import type to 'Sentis Model', then assign in inspector.", LogCategory.AI);
                
            }
            catch (Exception ex) {
                _debugger.LogError($"Exception while loading YOLO model: {ex.Message}\n{ex.StackTrace}", LogCategory.AI);
            }
        }
        
        private void LoadSAM2FromAIModels() {
            try {
                string modelPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", "sam2_hiera_base.onnx");
                _debugger.Log($"Loading SAM2 model from: {modelPath}", LogCategory.AI);
                
                if (!File.Exists(modelPath)) {
                    _debugger.LogWarning($"SAM2 model file not found at: {modelPath}", LogCategory.AI);
                    
                    // Try inspector fallback
                    if (_sam2Model != null) {
                        try {
                            _sam2Worker = CreateSentisWorker(_sam2Model, _inferenceBackend);
                            _debugger.Log("✓ SAM2 model loaded successfully from inspector assignment", LogCategory.AI);
                            return;
                        }
                        catch (Exception ex) {
                            _debugger.LogError($"Failed to load SAM2 model from inspector: {ex.Message}", LogCategory.AI);
                        } 
                    }
                    
                    _debugger.LogWarning("SAM2 model not available - segmentation will use simplified approach", LogCategory.AI);
                    return;
                }
                
                _debugger.Log($"SAM2 model file found, size: {new FileInfo(modelPath).Length / 1024 / 1024:F1} MB", LogCategory.AI);
                
                #if UNITY_EDITOR
                string assetPath = "Assets/Scripts/AI/Models/sam2_hiera_base.onnx";
                var sam2Asset = AssetDatabase.LoadAssetAtPath<USentis.ModelAsset>(assetPath);
                if (sam2Asset != null) {
                    _sam2Worker = CreateSentisWorker(sam2Asset, _inferenceBackend);
                    _debugger.Log("✓ SAM2 model loaded successfully from AI/Models (Editor)", LogCategory.AI);
                    return;
                }
                #endif
                
                // Try inspector fallback
                if (_sam2Model != null) {
                    try {
                        _sam2Worker = CreateSentisWorker(_sam2Model, _inferenceBackend);
                        _debugger.Log("✓ SAM2 model loaded successfully from inspector assignment", LogCategory.AI);
                        return;
                    }
                    catch (Exception ex) {
                        _debugger.LogError($"Failed to load SAM2 model from inspector: {ex.Message}", LogCategory.AI);
                    }
                }
                
                _debugger.LogWarning("SAM2 model exists but could not be loaded. Please assign in inspector.", LogCategory.AI);
                
            }
            catch (Exception ex) {
                _debugger.LogError($"Exception while loading SAM2 model: {ex.Message}", LogCategory.AI);
            }
        }
        
        private void LoadFasterRCNNFromAIModels() {
            try {
                string modelPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", "FasterRCNN-12.onnx");
                _debugger.Log($"Loading Faster R-CNN model from: {modelPath}", LogCategory.AI);
                
                if (!File.Exists(modelPath)) {
                    _debugger.LogWarning($"Faster R-CNN model file not found at: {modelPath}", LogCategory.AI);
                    
                    // Try inspector fallback
                    if (_fasterRcnnModel != null) {
                        try {
                            _rcnnWorker = CreateSentisWorker(_fasterRcnnModel, _inferenceBackend);
                            _debugger.Log("✓ Faster R-CNN model loaded successfully from inspector assignment", LogCategory.AI);
                            return;
                        } 
                        catch (Exception ex) {
                            _debugger.LogError($"Failed to load Faster R-CNN model from inspector: {ex.Message}", LogCategory.AI);
                        }
                    }
                    
                    _debugger.LogWarning("Faster R-CNN model not available - will use YOLO-only classification", LogCategory.AI);
                    return;
                }
                
                _debugger.Log($"Faster R-CNN model file found, size: {new FileInfo(modelPath).Length / 1024 / 1024:F1} MB", LogCategory.AI);
                
                #if UNITY_EDITOR
                string assetPath = "Assets/Scripts/AI/Models/FasterRCNN-12.onnx";
                var rcnnAsset = AssetDatabase.LoadAssetAtPath<USentis.ModelAsset>(assetPath);
                if (rcnnAsset != null) {
                    _rcnnWorker = CreateSentisWorker(rcnnAsset, _inferenceBackend);
                    _debugger.Log("✓ Faster R-CNN model loaded successfully from AI/Models (Editor)", LogCategory.AI);
                    return;
                }
                #endif
                
                // Try inspector fallback
                if (_fasterRcnnModel != null) {
                    try {
                        _rcnnWorker = CreateSentisWorker(_fasterRcnnModel, _inferenceBackend);
                        _debugger.Log("✓ Faster R-CNN model loaded successfully from inspector assignment", LogCategory.AI);
                        return;
                    }
                    catch (Exception ex) {
                        _debugger.LogError($"Failed to load Faster R-CNN model from inspector: {ex.Message}", LogCategory.AI);
                    }
                }
                
                _debugger.LogWarning("Faster R-CNN model exists but could not be loaded. Please assign in inspector.", LogCategory.AI);
                
            }
            catch (Exception ex) {
                _debugger.LogError($"Exception while loading Faster R-CNN model: {ex.Message}", LogCategory.AI);
            }
        }
        
        private void LoadClassLabels() {
            try {
                if (_labelsFile != null) {
                    string labelsText = _labelsFile.text;
                    _classLabels = labelsText.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    _debugger.Log($"✓ Loaded {_classLabels.Length} class labels", LogCategory.AI);
                }
                else {
                    // Use default COCO class labels if no labels file is provided
                    _classLabels = GetDefaultCOCOLabels();
                    _debugger.LogWarning("No labels file provided, using default COCO labels", LogCategory.AI);
                }
            }
            catch (Exception ex) {
                _debugger.LogError($"Error loading class labels: {ex.Message}", LogCategory.AI);
                _classLabels = GetDefaultCOCOLabels();
            }
        }
        
        private string[] GetDefaultCOCOLabels() {
            return new string[] {
                "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
                "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
                "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
                "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
                "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
                "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
                "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake",
                "chair", "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop",
                "mouse", "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
                "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
            };
        }
        
        private IWorker CreateSentisWorker(USentis.ModelAsset modelAsset, USentis.BackendType backendType) {
            try {
                var model = USentis.ModelLoader.Load(modelAsset);
                // Modern Unity Sentis API - WorkerFactory.CreateWorker replaced with new Worker()
                var worker = new USentis.Worker(model, backendType);
                return new SentisWorkerWrapper(worker, model);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to create Sentis worker: {ex.Message}", LogCategory.AI);
                throw;
            }
        }
        
        private void LogModelLoadingSummary() {
            _debugger.Log("═══ AI MODEL LOADING SUMMARY ═══", LogCategory.AI);
            
            // YOLO (Required)
            if (_yoloWorker != null) {
                _debugger.Log("✓ YOLO Model: LOADED (Required for terrain generation)", LogCategory.AI);
            } else {
                _debugger.LogError("✗ YOLO Model: FAILED - Terrain generation will NOT work", LogCategory.AI);
            }
            
            // SAM2 (Optional)
            if (_useSAM) {
                if (_sam2Worker != null) {
                    _debugger.Log("✓ SAM2 Model: LOADED (Enhanced segmentation enabled)", LogCategory.AI);
                } else {
                    _debugger.LogWarning("⚠ SAM2 Model: NOT LOADED (Will use simplified segmentation)", LogCategory.AI);
                }
            } else {
                _debugger.Log("○ SAM2 Model: DISABLED in settings", LogCategory.AI);
            }
            
            // Faster R-CNN (Optional)
            if (_useFasterRCNN) {
                if (_rcnnWorker != null) {
                    _debugger.Log("✓ Faster R-CNN Model: LOADED (Enhanced classification enabled)", LogCategory.AI);
                } else {
                    _debugger.LogWarning("⚠ Faster R-CNN Model: NOT LOADED (Will use YOLO-only classification)", LogCategory.AI);
                }
            } else {
                _debugger.Log("○ Faster R-CNN Model: DISABLED in settings", LogCategory.AI);
            }
            
            // Overall status
            bool canGenerate = (_yoloWorker != null);
            if (canGenerate) {
                _debugger.Log("✅ STATUS: AI models ready for terrain generation", LogCategory.AI);
            } else {
                _debugger.LogError("🚫 STATUS: Critical models missing - terrain generation BLOCKED", LogCategory.AI);
                _debugger.LogError("SOLUTION: Please assign YOLO model in inspector or ensure proper model import", LogCategory.AI);
            }
            
            _debugger.Log("═══════════════════════════════", LogCategory.AI);
        }
        
        private void LoadPreferences() {
            // Load user preferences from PlayerPrefs if needed
            _debugger.Log("Loading user preferences", LogCategory.System);
        }
        
        private void SavePreferences() {
            // Save user preferences to PlayerPrefs if needed
            _debugger.Log("Saving user preferences", LogCategory.System);
        }
        
        private void UpdateStatus(string message, bool isError = false) {
            if (_statusText != null) {
                _statusText.text = message;
                _statusText.color = isError ? Color.red : Color.white;
            }
            
            if (isError) {
                _debugger.LogError(message, LogCategory.UI);
            } else {
                _debugger.Log(message, LogCategory.UI);
            }
        }
        
        private void UpdateProgress(float progress, string stage = null) {
            // Ensure progress is clamped between 0 and 1
            progress = Mathf.Clamp01(progress);
            
            // Update both progress indicators consistently
            if (_progressBar != null) {
                _progressBar.value = progress;
            }
            
            if (_progressText != null) {
                _progressText.text = $"{progress * 100:F0}%";
            }
            
            // Update stage information with more detailed context
            if (!string.IsNullOrEmpty(stage)) {
                if (_stageText != null) {
                    _stageText.text = stage;
                }
                
                // Log stage changes with consistent formatting
                _debugger.Log($"Progress: {progress:P1} - {stage}", LogCategory.Process);
            }
            
            // Force UI update
            if (_progressBar != null) {
                Canvas.ForceUpdateCanvases();
            }
        }
        
        private void UpdateStage(string stage, string details = null) {
            if (_stageText != null) {
                _stageText.text = stage;
            }
            
            if (!string.IsNullOrEmpty(details) && _detailsText != null) {
                _detailsText.text = details;
            }
        }
        
        private void ShowLoadingPanel(bool show) {
            if (_loadingPanel != null) {
                _loadingPanel.SetActive(show);
            }
        }
        
        private void ResetUI() {
            _isProcessing = false;
            
            if (_generateButton != null) _generateButton.interactable = (_uploadedMapTexture != null);
            if (_uploadButton != null) _uploadButton.interactable = true;
            
            ShowLoadingPanel(false);
            UpdateProgress(0f);
            UpdateStage("");
            
            if (_detailsText != null) _detailsText.text = "";
        }
        
        private void CleanupGeneratedObjects() {
            foreach (var obj in _generatedObjects) {
                if (obj != null) {
                    DestroyImmediate(obj);
                }
            }
            _generatedObjects.Clear();
            
            if (_waterPlane != null) {
                DestroyImmediate(_waterPlane);
                _waterPlane = null; 
            }
        }
        
        private IEnumerator SafeCoroutine(IEnumerator coroutine, System.Action<Exception> onError) {
            bool hasError = false;
            Exception exception = null;
            
            while (true) {
                try {
                    if (!coroutine.MoveNext()) {
                        break;
                    }
                }
                catch (Exception ex) {
                    exception = ex;
                    hasError = true;
                    break;
                }
                
                yield return coroutine.Current;
            }
            
            if (hasError) {
                onError?.Invoke(exception);
            }
        }
        
        /// <summary>
        /// Main coroutine that processes the uploaded map and generates terrain.
        /// </summary>
        private IEnumerator ProcessMapAndGenerateTerrain()
        {
            if (_uploadedMapTexture == null)
            {
                HandleGenerationError("No map texture uploaded");
                yield break;
            }
            
            _processingStartTime = Time.realtimeSinceStartup;
            _performanceMetrics.Clear();
            
            // Execute the main logic without try-catch around yield statements
            yield return StartCoroutine(ExecuteTerrainGenerationLogic());
        }
        
        /// <summary>
        /// Execute the terrain generation logic without try-catch around yield statements.
        /// </summary>
        private IEnumerator ExecuteTerrainGenerationLogic()
        {
            Exception caughtException = null;
            
            try
            {
                // Initialize performance tracking
                _debugger.Log("Starting terrain generation pipeline", LogCategory.Process);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
            
            if (caughtException != null)
            {
                HandleGenerationError($"Failed to start terrain generation: {caughtException.Message}");
                yield break;
            }
            
            // Step 1: Analyze the map image (40% of progress)
            UpdateProgress(0.1f, "Analyzing map image...");
            
            AnalysisResults analysisResults = null;
            bool analysisComplete = false;
            string analysisError = null;
            
            yield return StartCoroutine(AnalyzeImage(
                _uploadedMapTexture,
                (results) => {
                    analysisResults = results;
                    analysisComplete = true;
                },
                (error) => {
                    analysisError = error;
                    analysisComplete = true;
                },
                (message, progress) => {
                    UpdateProgress(0.1f + progress * 0.3f, $"Analyzing: {message}");
                }
            ));
            
            if (!string.IsNullOrEmpty(analysisError))
            {
                HandleGenerationError($"Map analysis failed: {analysisError}");
                yield break;
            }
            
            if (analysisResults == null)
            {
                HandleGenerationError("Map analysis produced no results");
                yield break;
            }
            
            _analysisResults = analysisResults;
            OnAnalysisComplete?.Invoke(analysisResults);
            
            // Step 2: Generate terrain (30% of progress)
            UpdateProgress(0.4f, "Generating terrain...");
            
            var terrain = CreateTerrainFromAnalysis(analysisResults, _uploadedMapTexture);
            if (terrain == null)
            {
                HandleGenerationError("Failed to generate terrain");
                yield break;
            }
            
            _generatedTerrain = terrain;
            OnTerrainGenerated?.Invoke(terrain);
            
            // Step 3: Generate and place models (20% of progress)
            UpdateProgress(0.7f, "Generating and placing models...");
            
            if (_modelGenerator != null)
            {
                List<GameObject> models = null;
                bool modelsComplete = false;
                string modelsError = null;
                
                yield return StartCoroutine(_modelGenerator.GenerateAndPlaceModels(
                    analysisResults,
                    terrain,
                    (generatedModels) => {
                        models = generatedModels;
                        modelsComplete = true;
                    },
                    (error) => {
                        modelsError = error;
                        modelsComplete = true;
                    },
                    (progress) => {
                        UpdateProgress(0.7f + progress * 0.2f, "Generating models...");
                    }
                ));
                
                if (!string.IsNullOrEmpty(modelsError))
                {
                    _debugger.LogWarning($"Model generation failed: {modelsError}", LogCategory.Models);
                }
                else if (models != null && models.Count > 0)
                {
                    _generatedObjects.AddRange(models);
                    OnModelsPlaced?.Invoke(models);
                    _debugger.Log($"Generated {models.Count} models", LogCategory.Models);
                }
            }
            
            // Step 4: Visualize segmentation (5% of progress)
            UpdateProgress(0.9f, "Creating visualizations...");
            
            if (_segmentationVisualizer != null && _enableDebugVisualization)
            {
                yield return StartCoroutine(VisualizeSegmentationWithProgress());
            }
            
            // Step 5: Save assets if enabled (5% of progress)
            UpdateProgress(0.95f, "Saving generated assets...");
            
            if (_saveGeneratedAssets)
            {
                yield return StartCoroutine(SaveGeneratedAssets());
            }
            
            // Complete - handle completion outside of try-catch
            yield return StartCoroutine(CompleteTerrainGeneration());
        }
        
        /// <summary>
        /// Handle the completion of terrain generation.
        /// </summary>
        private IEnumerator CompleteTerrainGeneration()
        {
            try
            {
                // Complete
                UpdateProgress(1.0f, "Terrain generation complete!");
                
                float totalTime = Time.realtimeSinceStartup - _processingStartTime;
                _performanceMetrics["Total"] = totalTime;
                
                ShowCompletionDetails();
                FocusCameraOnTerrain();
                LogPerformanceMetrics();
                
                OnProcessingComplete?.Invoke();
                OnGenerationComplete?.Invoke();
                
                _debugger.Log($"Terrain generation completed successfully in {totalTime:F2} seconds", LogCategory.Process);
            }
            catch (Exception ex)
            {
                HandleGenerationError($"Error during completion: {ex.Message}");
                _debugger.LogError($"Exception in CompleteTerrainGeneration: {ex.Message}\n{ex.StackTrace}", LogCategory.Process);
            }
            finally
            {
                ResetUI();
            }
            
            yield return null;
        }
        
        // Placeholder methods that need to be implemented in other classes
        private UnityEngine.Terrain CreateTerrainFromAnalysis(AnalysisResults results, Texture2D sourceTexture) {
            try 
            {
                _debugger.Log("Creating terrain from analysis results", LogCategory.Terrain);
                
                // Ensure we have a TerrainGenerator
                if (_terrainGenerator == null) 
                {
                    _terrainGenerator = FindObjectOfType<TerrainGenerator>();
                    if (_terrainGenerator == null) 
                    {
                        // Create TerrainGenerator if it doesn't exist
                        GameObject terrainGenObj = new GameObject("TerrainGenerator");
                        _terrainGenerator = terrainGenObj.AddComponent<TerrainGenerator>();
                        _debugger.Log("Created new TerrainGenerator component", LogCategory.Terrain);
                    }
                }
                
                // Initialize TerrainGenerator if needed
                if (!_terrainGenerator.IsInitialized) 
                {
                    var config = new Dictionary<string, object>
                    {
                        ["terrainSize"] = _terrainSize,
                        ["terrainHeight"] = _terrainHeight,
                        ["heightmapResolution"] = _heightmapResolution,
                        ["detailResolution"] = _detailResolution
                    };
                    
                    if (!_terrainGenerator.Initialize(config))
                    {
                        _debugger.LogError("Failed to initialize TerrainGenerator", LogCategory.Terrain);
                        return null;
                    }
                }
                
                // Generate terrain using the analysis results
                var terrain = _terrainGenerator.GenerateTerrainFromAnalysis(results, sourceTexture);
                
                if (terrain != null) 
                {
                    _debugger.Log($"Terrain created successfully: {terrain.name}", LogCategory.Terrain);
                    
                    // Position terrain at origin
                    terrain.transform.position = Vector3.zero;
                    
                    // Add to generated objects for cleanup
                    if (!_generatedObjects.Contains(terrain.gameObject))
                    {
                        _generatedObjects.Add(terrain.gameObject);
                    }
                }
                else 
                {
                    _debugger.LogError("TerrainGenerator returned null terrain", LogCategory.Terrain);
                }
                
                return terrain;
            }
            catch (Exception ex) 
            {
                _debugger.LogError($"Error in CreateTerrainFromAnalysis: {ex.Message}\nStack: {ex.StackTrace}", LogCategory.Terrain);
                return null;
            }
        }
        
        private void CreateWaterPlane() {
            // This should be implemented in TerrainGenerator
            _debugger.LogWarning("CreateWaterPlane not implemented", LogCategory.Terrain);
        }
        
        private IEnumerator VisualizeSegmentationWithProgress() {
            // This should be implemented in SegmentationVisualizer
            _debugger.LogWarning("VisualizeSegmentationWithProgress not implemented", LogCategory.Visualization);
            yield return null;
        }
        
        private IEnumerator GenerateAndPlaceModelsWithProgress() {
            // This should be implemented in ModelGenerator
            _debugger.LogWarning("GenerateAndPlaceModelsWithProgress not implemented", LogCategory.Models);
            yield return null;
        }
        
        private IEnumerator SaveGeneratedAssets() {
            // Asset saving implementation
            _debugger.LogWarning("SaveGeneratedAssets not implemented", LogCategory.IO);
            yield return null;
        }
        
        private string GenerateAnalysisDetails() {
            if (_analysisResults == null) return "";
            
            var sb = new StringBuilder();
            sb.AppendLine($"Terrain Features: {_analysisResults.terrainFeatures.Count}");
            sb.AppendLine($"Map Objects: {_analysisResults.mapObjects.Count}");
            sb.AppendLine($"Object Groups: {_analysisResults.objectGroups.Count}");
            
            return sb.ToString();
        }
        
        private void ShowCompletionDetails() {
            string details = $"Generated {_generatedObjects.Count} objects";
            if (_generatedTerrain != null) {
                details += $"\nTerrain: {_terrainSize.x}x{_terrainSize.z} units";
            }
            
            UpdateStage("Complete", details);
        }
        
        private void FocusCameraOnTerrain() {
            if (_generatedTerrain != null) {
                // Focus camera on generated terrain
                Camera.main?.transform.LookAt(_generatedTerrain.transform.position);
            }
        }
        
        private void LogPerformanceMetrics() {
            _debugger.Log("═══ PERFORMANCE METRICS ═══", LogCategory.Performance);
            foreach (var metric in _performanceMetrics) {
                _debugger.Log($"{metric.Key}: {metric.Value:F2}s", LogCategory.Performance);
            }
            _debugger.Log("═════════════════════════", LogCategory.Performance);
        }
        
        /// <summary>
        /// Handle generation errors by logging and updating UI.
        /// </summary>
        private void HandleGenerationError(string errorMessage) {
            _debugger.LogError($"Generation error: {errorMessage}", LogCategory.Process);
            UpdateStatus($"Error: {errorMessage}", true);
            ResetUI();
            OnError?.Invoke(errorMessage);
        }
        
        /// <summary>
        /// Analyze image using MapAnalyzer.
        /// </summary>
        private IEnumerator AnalyzeImage(Texture2D mapTexture, 
            System.Action<AnalysisResults> onComplete,
            System.Action<string> onError,
            System.Action<string, float> onProgress) {
            
            if (_mapAnalyzer == null) {
                onError?.Invoke("MapAnalyzer not initialized");
                yield break;
            }
            
            // Verify MapAnalyzer is properly initialized before use
            if (!_mapAnalyzer.IsInitialized) {
                _debugger.LogWarning("MapAnalyzer not initialized, attempting to initialize now...", LogCategory.AI);
                try {
                    _mapAnalyzer.Initialize();
                    if (!_mapAnalyzer.IsInitialized) {
                        onError?.Invoke("MapAnalyzer initialization failed during analysis");
                        yield break;
                    }
                    _debugger.Log("MapAnalyzer successfully initialized for analysis", LogCategory.AI);
                }
                catch (Exception ex) {
                    onError?.Invoke($"MapAnalyzer initialization failed: {ex.Message}");
                    yield break;
                }
            }
            
            yield return _mapAnalyzer.AnalyzeMapImage(mapTexture, onComplete, onError, onProgress);
        }
        
        #endregion
        
        #region Helper Classes
        #endregion
        
        #region UI Event Handlers
        
        /// <summary>
        /// Cancel the current processing operation.
        /// </summary>
        private void CancelProcessing()
        {
            try
            {
                _isProcessing = false;
                
                if (_processingCoroutine != null)
                {
                    StopCoroutine(_processingCoroutine);
                    _processingCoroutine = null;
                }
                
                // Stop any active workers
                _yoloWorker?.Dispose();
                _sam2Worker?.Dispose();
                _rcnnWorker?.Dispose();
                
                // Reset UI
                if (_loadingPanel != null) _loadingPanel.SetActive(false);
                if (_generateButton != null) _generateButton.interactable = _uploadedMapTexture != null;
                if (_uploadButton != null) _uploadButton.interactable = true;
                if (_statusText != null) _statusText.text = "Processing cancelled";
                
                _debugger.Log("Processing cancelled by user", LogCategory.System);
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error during processing cancellation: {ex.Message}", LogCategory.System);
            }
        }
        
        /// <summary>
        /// Open file explorer to select a map image.
        /// </summary>
        private void OpenFileExplorer()
        {
            try
            {
                // Set file browser properties
                FileBrowser.SetFilters(true, new FileBrowser.Filter("Images", ".jpg", ".jpeg", ".png", ".bmp", ".tga"));
                FileBrowser.SetDefaultFilter(".png");
                FileBrowser.SetExcludedExtensions(".lnk", ".tmp", ".zip", ".rar", ".exe");
                
                // Show file browser
                FileBrowser.ShowLoadDialog((paths) => {
                    if (paths.Length > 0)
                    {
                        StartCoroutine(LoadImageFromPath(paths[0]));
                    }
                }, 
                () => {
                    _debugger.Log("File selection cancelled", LogCategory.System);
                }, 
                FileBrowser.PickMode.Files, 
                false, 
                null, 
                null, 
                "Select Map Image", 
                "Load");
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error opening file explorer: {ex.Message}", LogCategory.System);
                if (_statusText != null) _statusText.text = "Error: Could not open file browser";
            }
        }
        
        /// <summary>
        /// Start the terrain generation process.
        /// </summary>
        private void StartTerrainGeneration()
        {
            if (_uploadedMapTexture == null)
            {
                _debugger.LogError("No map image uploaded", LogCategory.System);
                if (_statusText != null) _statusText.text = "Error: No map image selected";
                return;
            }
            
            if (_isProcessing)
            {
                _debugger.LogError("Processing already in progress", LogCategory.System);
                return;
            }
            
            try
            {
                _isProcessing = true;
                
                // Update UI
                if (_loadingPanel != null) _loadingPanel.SetActive(true);
                if (_generateButton != null) _generateButton.interactable = false;
                if (_uploadButton != null) _uploadButton.interactable = false;
                if (_statusText != null) _statusText.text = "Starting terrain generation...";
                
                // Start processing coroutine
                _processingCoroutine = StartCoroutine(ProcessMapAndGenerateTerrain());
                
                _debugger.Log("Terrain generation started", LogCategory.System);
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error starting terrain generation: {ex.Message}", LogCategory.System);
                _isProcessing = false;
                if (_statusText != null) _statusText.text = $"Error: {ex.Message}";
                if (_loadingPanel != null) _loadingPanel.SetActive(false);
                if (_generateButton != null) _generateButton.interactable = true;
                if (_uploadButton != null) _uploadButton.interactable = true;
            }
        }
        
        /// <summary>
        /// Load an image from the specified file path.
        /// </summary>
        private IEnumerator LoadImageFromPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                _debugger.LogError($"File not found: {filePath}", LogCategory.System);
                if (_statusText != null) _statusText.text = "Error: File not found";
                yield break;
            }
            
            byte[] imageData = null;
            
            try
            {
                imageData = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error reading file: {ex.Message}", LogCategory.System);
                if (_statusText != null) _statusText.text = "Error: Could not read file";
                yield break;
            }
            
            if (imageData != null && imageData.Length > 0)
            {
                Texture2D texture = new Texture2D(2, 2);
                
                if (texture.LoadImage(imageData))
                {
                    // Dispose of previous texture
                    if (_uploadedMapTexture != null)
                    {
                        DestroyImmediate(_uploadedMapTexture);
                    }
                    
                    _uploadedMapTexture = texture;
                    
                    // Update UI
                    if (_mapPreviewImage != null)
                    {
                        _mapPreviewImage.texture = _uploadedMapTexture;
                    }
                    
                    if (_generateButton != null)
                    {
                        _generateButton.interactable = true;
                    }
                    
                    if (_statusText != null)
                    {
                        _statusText.text = $"Map loaded: {Path.GetFileName(filePath)} ({_uploadedMapTexture.width}x{_uploadedMapTexture.height})";
                    }
                    
                    _debugger.Log($"Successfully loaded map image: {filePath}", LogCategory.System);
                }
                else
                {
                    DestroyImmediate(texture);
                    _debugger.LogError("Failed to load image data into texture", LogCategory.System);
                    if (_statusText != null) _statusText.text = "Error: Invalid image format";
                }
            }
            else
            {
                _debugger.LogError("Empty or invalid image data", LogCategory.System);
                if (_statusText != null) _statusText.text = "Error: Invalid image data";
            }
            
            yield return null;
        }
        
        #endregion
    }
}
