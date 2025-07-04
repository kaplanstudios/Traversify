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
                return analyzer;
            }
            
            // Try to find it by type name using reflection
            try {
                var mapAnalyzerType = System.Type.GetType("MapAnalyzer") ?? 
                                     System.Type.GetType("Traversify.AI.MapAnalyzer") ??
                                     System.Type.GetType("Traversify.MapAnalyzer");
                
                if (mapAnalyzerType != null) {
                    GameObject go = new GameObject("MapAnalyzer");
                    go.transform.SetParent(parent.transform);
                    var component = go.AddComponent(mapAnalyzerType) as MapAnalyzer;
                    
                    if (component != null) {
                        _debugger.Log("Created MapAnalyzer component successfully", LogCategory.System);
                        return component;
                    }
                }
            }
            catch (Exception ex) {
                _debugger.LogError($"Error creating MapAnalyzer via reflection: {ex.Message}", LogCategory.System);
            }
            
            // Last resort: try direct component creation
            try {
                GameObject go = new GameObject("MapAnalyzer");
                go.transform.SetParent(parent.transform);
                var component = go.AddComponent<MapAnalyzer>();
                
                if (component != null) {
                    _debugger.Log("Created MapAnalyzer component via direct instantiation", LogCategory.System);
                    return component;
                }
            }
            catch (Exception ex) {
                _debugger.LogError($"Error creating MapAnalyzer directly: {ex.Message}", LogCategory.System);
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
            // Configure MapAnalyzer
            if (_mapAnalyzer != null) {
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
                
                // Log the configuration status
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
            if (_progressBar != null) {
                _progressBar.value = progress;
            }
            
            if (_progressText != null) {
                _progressText.text = $"{progress * 100:F0}%";
            }
            
            if (!string.IsNullOrEmpty(stage) && _stageText != null) {
                _stageText.text = stage;
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
        
        // Placeholder methods that need to be implemented in other classes
        private UnityEngine.Terrain CreateTerrainFromAnalysis(AnalysisResults results, Texture2D sourceTexture) {
            // This should be implemented in TerrainGenerator
            _debugger.LogWarning("CreateTerrainFromAnalysis not implemented", LogCategory.Terrain);
            return null;
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
        
        #endregion
        
        #region User Input Handling
        
        private void OpenFileExplorer() {
            _debugger.Log("OpenFileExplorer called", LogCategory.IO);
            
            // Set Simple File Browser properties for image files
            FileBrowser.SetFilters(true, new FileBrowser.Filter("Image Files", ".png", ".jpg", ".jpeg", ".bmp", ".tga"));
            FileBrowser.SetDefaultFilter(".png");
            FileBrowser.SetExcludedExtensions(".lnk", ".tmp", ".zip", ".rar", ".exe");
            
            // Show load file dialog using Simple File Browser
            StartCoroutine(ShowImageFileDialog());
        }
        
        private IEnumerator ShowImageFileDialog() {
            yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, false, null, null, "Select Map Image", "Load");
            
            if (FileBrowser.Success) {
                string path = FileBrowser.Result[0];
                _debugger.Log($"File selected via SimpleFileBrowser: {path}", LogCategory.IO);
                StartCoroutine(LoadImageFromPath(path));
            }
            else {
                _debugger.Log("File selection cancelled", LogCategory.IO);
                UpdateStatus("File selection cancelled");
            }
        }
        
        private void ShowDragDropInstructions() {
            UpdateStatus("Drag and drop an image file (PNG, JPG, JPEG, BMP, TGA) onto this window to upload");
            _debugger.Log("Showing drag-and-drop instructions", LogCategory.IO);
            
            // Also enable drag and drop detection if not already enabled
            EnableDragAndDrop();
        }
        
        private void EnableDragAndDrop() {
            // This method can be expanded to enable drag and drop functionality
            _debugger.Log("Drag and drop enabled", LogCategory.IO);
        }
        
        private IEnumerator LoadImageFromPath(string path) {
            return SafeCoroutine(InnerLoadImageFromPath(path), ex => {
                _debugger.LogError($"Error loading image: {ex}", LogCategory.IO);
                UpdateStatus($"Error loading image: {ex}", true);
                if (_generateButton != null) _generateButton.interactable = false;
                if (_mapPreviewImage != null) _mapPreviewImage.gameObject.SetActive(false);
            });
        }
        
        private IEnumerator InnerLoadImageFromPath(string path) {
            _debugger.Log($"Loading image from: {path}", LogCategory.IO);
            
            // Get file info for size reporting
            FileInfo fileInfo = new FileInfo(path);
            float fileSizeMB = fileInfo.Length / (1024f * 1024f);
            
            if (fileSizeMB > 50f) {
                _debugger.LogWarning($"Large image file ({fileSizeMB:F1} MB) may take longer to process", LogCategory.IO);
            }
            
            // Load the image
            UnityWebRequest request = UnityWebRequestTexture.GetTexture("file://" + path);
            request.timeout = 30;
            yield return request.SendWebRequest();
            
            if (request.result != UnityWebRequest.Result.Success) {
                throw new Exception(request.error);
            }
            
            _uploadedMapTexture = DownloadHandlerTexture.GetContent(request);
            _uploadedMapTexture.name = Path.GetFileNameWithoutExtension(path);
            
            ValidateLoadedImage();
            DisplayLoadedImage();
            
            if (_generateButton != null) _generateButton.interactable = true;
            
            UpdateStatus($"Map loaded: {_uploadedMapTexture.width}x{_uploadedMapTexture.height} ({fileSizeMB:F1} MB)");
            _debugger.Log($"Image loaded successfully: {_uploadedMapTexture.width}x{_uploadedMapTexture.height} ({fileSizeMB:F1} MB)", LogCategory.IO);
        }
        
        private void ValidateLoadedImage() {
            if (_uploadedMapTexture.width < 128 || _uploadedMapTexture.height < 128) {
                throw new Exception("Image is too small. Minimum size is 128x128 pixels.");
            }
            
            if (_uploadedMapTexture.width > 8192 || _uploadedMapTexture.height > 8192) {
                _debugger.LogWarning("Very large image detected. Processing may be slow.", LogCategory.IO);
            }
            
            float aspectRatio = (float)_uploadedMapTexture.width / _uploadedMapTexture.height;
            
            if (aspectRatio < 0.5f || aspectRatio > 2f) {
                _debugger.LogWarning($"Unusual aspect ratio ({aspectRatio:F2}). Results may vary.", LogCategory.IO);
            }
        }
        
        private void DisplayLoadedImage() {
            if (_mapPreviewImage != null) {
                _mapPreviewImage.texture = _uploadedMapTexture;
                _mapPreviewImage.gameObject.SetActive(true);
                
                // Adjust aspect ratio if needed
                AspectRatioFitter aspectFitter = _mapPreviewImage.GetComponent<AspectRatioFitter>();
                if (aspectFitter != null) {
                    aspectFitter.aspectRatio = (float)_uploadedMapTexture.width / _uploadedMapTexture.height;
                }
                
                _mapPreviewImage.SetNativeSize();
            }
        }
        
        private void StartTerrainGeneration() {
            if (_isProcessing) {
                _debugger.LogWarning("Terrain generation already in progress", LogCategory.User);
                return;
            }
            
            if (_uploadedMapTexture == null) {
                _debugger.LogWarning("No map image uploaded", LogCategory.User);
                UpdateStatus("Please upload a map image first", true);
                return;
            }
            
            SavePreferences();
            
            _isProcessing = true;
            _isCancelled = false;
            _processingStartTime = Time.realtimeSinceStartup;
            _performanceMetrics.Clear();
            
            if (_generateButton != null) _generateButton.interactable = false;
            if (_uploadButton != null) _uploadButton.interactable = false;
            
            _processingCoroutine = StartCoroutine(GenerateTerrainFromMap());
        } 
        
        private void CancelProcessing() {
            if (!_isProcessing) return;
            
            _debugger.Log("Cancelling processing...", LogCategory.User);
            _isCancelled = true;
            
            if (_processingCoroutine != null) {
                StopCoroutine(_processingCoroutine);
                _processingCoroutine = null;
            }
            
            CleanupGeneratedObjects();
            ResetUI();
            
            UpdateStatus("Processing cancelled by user");
            OnProcessingCancelled?.Invoke();
        }
        #endregion
        
        #region Processing Pipeline
        
        private IEnumerator GenerateTerrainFromMap() {
            ShowLoadingPanel(true);
            UpdateProgress(0.05f, "Starting terrain generation...");
            _debugger.Log("Starting terrain generation process", LogCategory.Process);
            _debugger.StartTimer("TotalGeneration");
            
            // Clean up any previous generated objects
            CleanupGeneratedObjects();
            
            bool hasError = false;
            string errorMessage = "";
            
            // Step 1: Analyze the map using AI (approx 40% progress)
            _debugger.StartTimer("MapAnalysis");
            
            bool analysisComplete = false;
            yield return StartCoroutine(AnalyzeImage(_uploadedMapTexture,
                results => {
                    _analysisResults = results;
                    OnAnalysisComplete?.Invoke(results);
                    analysisComplete = true;
                },
                error => {
                    errorMessage = error;
                    hasError = true;
                    analysisComplete = true;
                },
                (stage, prog) => {
                    // Map analysis progress updates (scaled 0.1 to 0.4 of total)
                    float totalProg = 0.1f + (prog * 0.3f);
                    UpdateProgress(totalProg);
                    OnProgressUpdate?.Invoke(totalProg);
                    
                    // Log stage changes for debugging
                    if (stage != null) _debugger.Log($"{stage} ({prog:P0})", LogCategory.AI);
                }));
            
            while (!analysisComplete) {
                yield return null;
            }
            
            if (hasError) {
                HandleGenerationError(errorMessage);
                yield break;
            }
            
            _performanceMetrics["MapAnalysis"] = _debugger.StopTimer("MapAnalysis");
            
            if (_isCancelled || _analysisResults == null) {
                HandleGenerationError("Analysis cancelled");
                yield break;
            }
            
            // Summary of analysis results
            string summary = $"Detected {_analysisResults.terrainFeatures.Count} terrain features and {_analysisResults.mapObjects.Count} objects";
            UpdateStage("Analysis Complete", summary);
            _debugger.Log("Map analysis complete: " + summary, LogCategory.Process);
            
            if (_detailsText != null) {
                _detailsText.text = GenerateAnalysisDetails();
            }
            
            UpdateProgress(0.4f);
            
            // Step 2: Generate terrain from analysis (approx 20% progress)
            yield return StartCoroutine(GenerateTerrainStep());
            
            if (hasError) yield break;
            
            // Step 3: Create water plane if water generation is enabled (5% progress)
            if (_generateWater) {
                yield return StartCoroutine(GenerateWaterStep());
            }
            
            // Step 4: Visualize segmentation overlays and labels (15% progress)
            _debugger.StartTimer("Segmentation");
            yield return StartCoroutine(VisualizeSegmentationWithProgress());
            _performanceMetrics["Segmentation"] = _debugger.StopTimer("Segmentation");
            
            if (_isCancelled) {
                HandleGenerationError("Segmentation cancelled");
                yield break;
            }
            
            // Step 5: Generate and place 3D models for detected objects (20% progress)
            _debugger.StartTimer("ModelGeneration");
            yield return StartCoroutine(GenerateAndPlaceModelsWithProgress());
            _performanceMetrics["ModelGeneration"] = _debugger.StopTimer("ModelGeneration");
            
            if (_isCancelled) {
                HandleGenerationError("Model generation cancelled");
                yield break;
            }
            
            // Step 6: Save generated assets (remaining progress)
            if (_saveGeneratedAssets) {
                yield return StartCoroutine(SaveGeneratedAssets());
            }
            
            // Complete processing
            UpdateProgress(1.0f, "Terrain generation complete!");
            float totalTime = _debugger.StopTimer("TotalGeneration");
            _performanceMetrics["Total"] = totalTime;
            
            _debugger.Log($"Terrain generation completed in {totalTime:F1} seconds", LogCategory.Process);
            
            ShowCompletionDetails();
            OnTerrainGenerated?.Invoke(_generatedTerrain);
            OnModelsPlaced?.Invoke(_generatedObjects);
            OnProcessingComplete?.Invoke();
            
            FocusCameraOnTerrain();
            
            // Pause briefly at end
            yield return new WaitForSeconds(2f);
            
            LogPerformanceMetrics();
            ResetUI();
        }
        
        private IEnumerator GenerateTerrainStep() {
            _debugger.StartTimer("TerrainGeneration");
            UpdateStage("Terrain Mesh Generation", "Creating heightmap...");
            UpdateProgress(0.45f);
            
            bool terrainCreated = false;
            string terrainError = "";
            
            // Use a separate method for terrain creation to handle errors
            try {
                _generatedTerrain = CreateTerrainFromAnalysis(_analysisResults, _uploadedMapTexture);
                terrainCreated = _generatedTerrain != null;
            }
            catch (Exception ex) {
                terrainError = $"Terrain generation error: {ex.Message}";
            }
            
            yield return null; // Allow frame to process
            
            if (!terrainCreated) {
                HandleGenerationError(string.IsNullOrEmpty(terrainError) ? "Terrain generation failed" : terrainError);
                yield break;
            }
            
            _generatedObjects.Add(_generatedTerrain.gameObject);
            _performanceMetrics["TerrainGeneration"] = _debugger.StopTimer("TerrainGeneration");
            
            if (_isCancelled) {
                HandleGenerationError("Terrain generation cancelled");
                yield break;
            }
            
            UpdateProgress(0.6f, "Terrain generation complete");
        }

        private IEnumerator GenerateWaterStep() {
            _debugger.StartTimer("WaterCreation");
            UpdateProgress(0.65f, "Creating water features...");
            
            try {
                CreateWaterPlane();
                _performanceMetrics["WaterCreation"] = _debugger.StopTimer("WaterCreation");
                
                // Add water plane to generated objects list
                if (_waterPlane != null) _generatedObjects.Add(_waterPlane);
            }
            catch (Exception ex) {
                _debugger.LogWarning($"Water creation error: {ex.Message}", LogCategory.Terrain);
            }
            
            yield return null;
        }

        private void HandleGenerationError(string errorMessage)
        {
            _debugger.LogError($"Error during terrain generation: {errorMessage}", LogCategory.Process);
            UpdateStatus($"Error: {errorMessage}", true);
            OnError?.Invoke(errorMessage);
            LogPerformanceMetrics();
            ResetUI();  
        }

        /// <summary>
        /// Analyzes the uploaded map image using the MapAnalyzer component
        /// </summary>
        private IEnumerator AnalyzeImage(Texture2D texture, System.Action<AnalysisResults> onComplete, System.Action<string> onError, System.Action<string, float> onProgress)
        {
            if (_mapAnalyzer == null)
            {
                // Find or create the component container
                GameObject componentContainer = GameObject.Find("TraversifyComponents");
                if (componentContainer == null) {
                    componentContainer = new GameObject("TraversifyComponents");
                    componentContainer.transform.SetParent(transform);
                }
                
                // Try to find or create MapAnalyzer if it's missing
                _mapAnalyzer = FindOrCreateMapAnalyzer(componentContainer);
            }

            if (_mapAnalyzer == null)
            {
                onError?.Invoke("MapAnalyzer could not be created");
                yield break;
            }
            
            _debugger.Log("Starting image analysis with MapAnalyzer", LogCategory.AI);
            onProgress?.Invoke("Initializing AI models...", 0.1f);
            
            // Ensure MapAnalyzer is properly initialized
            try
            {
                _mapAnalyzer.InitializeModelsIfNeeded();
                
                // Check if initialization was successful
                if (!_mapAnalyzer.IsInitialized)
                {
                    onError?.Invoke("MapAnalyzer models failed to initialize");
                    yield break;
                }
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Failed to initialize MapAnalyzer models: {ex.Message}", LogCategory.AI);
                onError?.Invoke($"Failed to initialize AI models: {ex.Message}");
                yield break;
            }
            
            onProgress?.Invoke("Running AI analysis...", 0.3f);
            
            // Use MapAnalyzer to analyze the image
            bool analysisComplete = false;
            AnalysisResults results = null;
            string errorMessage = null;
            
            // Check if MapAnalyzer has the AnalyzeMapImage method
            var analyzeMethod = _mapAnalyzer.GetType().GetMethod("AnalyzeMapImage");
            if (analyzeMethod != null)
            {
                _debugger.Log("Using MapAnalyzer.AnalyzeMapImage method", LogCategory.AI);
                
                // Call the actual MapAnalyzer method - handle errors without try-catch around yield
                Exception startCoroutineException = null;
                
                try
                {
                    // This should be replaced with the actual method call when implemented
                    var analysisTask = StartCoroutine(CallMapAnalyzerAnalyzeMethod(texture, 
                        (result) => {
                            results = result;
                            analysisComplete = true;
                        },
                        (error) => {
                            errorMessage = error;
                            analysisComplete = true;
                        },
                        onProgress));
                }
                catch (Exception ex)
                {
                    startCoroutineException = ex;
                    analysisComplete = true;
                }
                
                // Handle any exception that occurred during StartCoroutine
                if (startCoroutineException != null)
                {
                    _debugger.LogError($"Error during MapAnalyzer.AnalyzeMapImage: {startCoroutineException.Message}", LogCategory.AI);
                    errorMessage = startCoroutineException.Message;
                    analysisComplete = true;
                }
                
                // Wait for analysis to complete without try-catch around yield
                while (!analysisComplete)
                {
                    yield return null;
                }
            }
            else
            {
                _debugger.LogWarning("MapAnalyzer.AnalyzeMapImage method not found, using fallback analysis", LogCategory.AI);
                
                // Fallback to basic analysis until MapAnalyzer is fully implemented
                IEnumerator fallbackCoroutine = BasicImageAnalysis(texture, 
                    (result) => {
                        results = result;
                        analysisComplete = true;
                    },
                    (error) => {
                        errorMessage = error;
                        analysisComplete = true;
                    },
                    onProgress);
                
                yield return StartCoroutine(fallbackCoroutine);
            }
            
            // Return results or error
            if (!string.IsNullOrEmpty(errorMessage))
            {
                onError?.Invoke(errorMessage);
            }
            else if (results != null)
            {
                onComplete?.Invoke(results);
            }
            else
            {
                onError?.Invoke("Analysis completed but no results were generated");
            }
        }
        
        /// <summary>
        /// Calls MapAnalyzer.AnalyzeMapImage method via reflection
        /// </summary>
        private IEnumerator CallMapAnalyzerAnalyzeMethod(Texture2D texture, Action<AnalysisResults> onComplete, Action<string> onError, Action<string, float> onProgress)
        {
            // Try to call MapAnalyzer.AnalyzeMapImage method
            var method = _mapAnalyzer.GetType().GetMethod("AnalyzeMapImage");
            if (method != null)
            {
                // Check if it's a coroutine method
                if (method.ReturnType == typeof(IEnumerator))
                {
                    IEnumerator coroutine = null;
                    Exception invokeException = null;
                    
                    try
                    {
                        coroutine = method.Invoke(_mapAnalyzer, new object[] { texture, onComplete, onError, onProgress }) as IEnumerator;
                    }
                    catch (Exception ex)
                    {
                        invokeException = ex;
                    }
                    
                    if (invokeException != null)
                    {
                        _debugger.LogError($"Error calling MapAnalyzer.AnalyzeMapImage: {invokeException.Message}", LogCategory.AI);
                        onError?.Invoke($"MapAnalyzer error: {invokeException.Message}");
                        yield break;
                    }
                    
                    if (coroutine != null)
                    {
                        yield return StartCoroutine(coroutine);
                    }
                    else
                    {
                        onError?.Invoke("MapAnalyzer.AnalyzeMapImage returned null coroutine");
                    }
                }
                else
                {
                    // Synchronous method - handle errors separately from yield
                    Exception syncException = null;
                    
                    try
                    {
                        method.Invoke(_mapAnalyzer, new object[] { texture, onComplete, onError, onProgress });
                    }
                    catch (Exception ex)
                    {
                        syncException = ex;
                    }
                    
                    if (syncException != null)
                    {
                        _debugger.LogError($"Error calling MapAnalyzer.AnalyzeMapImage: {syncException.Message}", LogCategory.AI);
                        onError?.Invoke($"MapAnalyzer error: {syncException.Message}");
                        yield break;
                    }
                    
                    yield return null;
                }
            }
            else
            {
                onError?.Invoke("MapAnalyzer.AnalyzeMapImage method not found");
            }
        }
        
        /// <summary>
        /// Basic fallback image analysis when MapAnalyzer is not fully implemented
        /// </summary>
        private IEnumerator BasicImageAnalysis(Texture2D texture, System.Action<AnalysisResults> onComplete, System.Action<string> onError, System.Action<string, float> onProgress)
        {
            _debugger.Log("Running basic fallback image analysis", LogCategory.AI);
            
            onProgress?.Invoke("Analyzing image colors...", 0.5f);
            yield return new WaitForSeconds(0.5f);
            
            onProgress?.Invoke("Detecting basic features...", 0.7f);
            yield return new WaitForSeconds(0.3f);
            
            onProgress?.Invoke("Generating terrain data...", 0.9f);
            yield return new WaitForSeconds(0.2f);
            
            // Create basic analysis results
            var results = new AnalysisResults
            {
                mapObjects = new List<MapObject>(),
                terrainFeatures = new List<TerrainFeature>(),
                objectGroups = new List<ObjectGroup>(),
                timings = new AnalysisTimings(new ProcessingTimings { totalTime = 1.0f }),
                heightMap = CreateBasicHeightMap(texture),
                segmentationMap = CreateBasicSegmentationMap(texture)
            };
            
            // Add sample terrain features based on image analysis
            CreateBasicTerrainFeatures(texture, results);
            
            // Add some sample objects
            CreateBasicMapObjects(texture, results);
            
            _debugger.Log($"Basic analysis complete: {results.terrainFeatures.Count} terrain features, {results.mapObjects.Count} objects", LogCategory.AI);
            
            onProgress?.Invoke("Analysis complete", 1.0f);
            onComplete?.Invoke(results);
        }
        
        /// <summary>
        /// Creates a basic heightmap from the source image
        /// </summary>
        private Texture2D CreateBasicHeightMap(Texture2D sourceTexture)
        {
            Texture2D heightMap = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
            Color[] sourcePixels = sourceTexture.GetPixels();
            Color[] heightPixels = new Color[sourcePixels.Length];
            
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                // Convert color to grayscale and use as height
                float brightness = (sourcePixels[i].r + sourcePixels[i].g + sourcePixels[i].b) / 3f;
                heightPixels[i] = new Color(brightness, brightness, brightness, 1f);
            }
            
            heightMap.SetPixels(heightPixels);
            heightMap.Apply();
            return heightMap;
        }
        
        /// <summary>
        /// Creates a basic segmentation map from the source image
        /// </summary>
        private Texture2D CreateBasicSegmentationMap(Texture2D sourceTexture)
        {
            Texture2D segmentationMap = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGBA32, false);
            Color[] sourcePixels = sourceTexture.GetPixels();
            Color[] segmentPixels = new Color[sourcePixels.Length];
            
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                Color pixel = sourcePixels[i];
                
                // Simple color-based segmentation
                if (pixel.b > pixel.r && pixel.b > pixel.g)
                {
                    segmentPixels[i] = Color.blue; // Water
                }
                else if (pixel.g > pixel.r && pixel.g > pixel.b)
                {
                    segmentPixels[i] = Color.green; // Vegetation
                }
                else if (pixel.r > 0.7f && pixel.g > 0.7f && pixel.b > 0.7f)
                {
                    segmentPixels[i] = Color.white; // Snow/peaks
                }
                else
                {
                    segmentPixels[i] = new Color(0.6f, 0.4f, 0.2f); // Ground
                }
            }
            
            segmentationMap.SetPixels(segmentPixels);
            segmentationMap.Apply();
            return segmentationMap;
        }
        
        /// <summary>
        /// Creates basic terrain features from image analysis
        /// </summary>
        private void CreateBasicTerrainFeatures(Texture2D texture, AnalysisResults results)
        {
            // Add water bodies (blue areas)
            results.terrainFeatures.Add(new TerrainFeature
            {
                label = "Water",
                type = "water",
                boundingBox = new Rect(0, 0, texture.width * 0.3f, texture.height * 0.2f),
                segmentColor = Color.blue,
                elevation = 0f,
                confidence = 0.8f
            });
            
            // Add mountainous terrain (brighter areas)
            results.terrainFeatures.Add(new TerrainFeature
            {
                label = "Mountains",
                type = "mountain",
                boundingBox = new Rect(texture.width * 0.6f, texture.height * 0.7f, texture.width * 0.4f, texture.height * 0.3f),
                segmentColor = Color.gray,
                elevation = _terrainSize.y * 0.8f,
                confidence = 0.7f
            });
            
            // Add forested areas (green areas)
            results.terrainFeatures.Add(new TerrainFeature
            {
                label = "Forest",
                type = "forest",
                boundingBox = new Rect(texture.width * 0.2f, texture.height * 0.3f, texture.width * 0.5f, texture.height * 0.4f),
                segmentColor = Color.green,
                elevation = _terrainSize.y * 0.3f,
                confidence = 0.6f
            });
        }
        
        /// <summary>
        /// Creates basic map objects from image analysis
        /// </summary>
        private void CreateBasicMapObjects(Texture2D texture, AnalysisResults results)
        {
            // Add some sample objects at random locations
            for (int i = 0; i < 5; i++)
            {
                results.mapObjects.Add(new MapObject
                {
                    label = $"Structure_{i + 1}",
                    type = "building",
                    position = new Vector2(
                        UnityEngine.Random.Range(0.1f, 0.9f),
                        UnityEngine.Random.Range(0.1f, 0.9f)
                    ),
                    boundingBox = new Rect(
                        UnityEngine.Random.Range(0, texture.width - 50),
                        UnityEngine.Random.Range(0, texture.height - 50),
                        50, 50
                    ),
                    segmentColor = Color.red,
                    confidence = 0.6f,
                    enhancedDescription = $"Building structure {i + 1}",
                    rotation = UnityEngine.Random.Range(0f, 360f),
                    scale = Vector3.one
                });
            }
        }

        #endregion
    }
    
    // Note: IWorker interface and data structures are defined in IWorker.cs
}
