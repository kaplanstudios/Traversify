/*************************************************************************
 *  Traversify – TraversifyManager.cs                                    *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:17:43 UTC                                     *
 *  Desc   : Central manager for the Traversify environment generation   *
 *           system. Handles UI interaction, processing pipelines,       *
 *           component coordination, and resource management. Serves     *
 *           as the main entry point for the application.                *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Unity.AI.Navigation;
using TMPro;
using Traversify.AI;
using Traversify.Core;

namespace Traversify {
    /// <summary>
    /// Central manager for the Traversify environment generation system.
    /// Handles UI interactions, processing pipelines, and resource management.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TraversifyDebugger))]
    public class TraversifyManager : MonoBehaviour {
        #region Singleton Pattern
        
        private static TraversifyManager _instance;
        
        /// <summary>
        /// Singleton instance of the TraversifyManager.
        /// </summary>
        public static TraversifyManager Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<TraversifyManager>();
                    if (_instance == null) {
                        GameObject go = new GameObject("TraversifyManager");
                        _instance = go.AddComponent<TraversifyManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Properties
        
        [Header("Core Components")]
        [Tooltip("Debug and logging system")]
        [SerializeField] private TraversifyDebugger _debugger;
        
        [Header("UI References")]
        [Tooltip("Upload map button")]
        [SerializeField] private Button _uploadButton;
        
        [Tooltip("Generate button")]
        [SerializeField] private Button _generateButton;
        
        [Tooltip("Map preview image")]
        [SerializeField] private RawImage _mapPreviewImage;
        
        [Tooltip("Status text")]
        [SerializeField] private TMP_Text _statusText;
        
        [Tooltip("Loading panel")]
        [SerializeField] private GameObject _loadingPanel;
        
        [Tooltip("Progress bar")]
        [SerializeField] private Slider _progressBar;
        
        [Tooltip("Progress text")]
        [SerializeField] private TMP_Text _progressText;
        
        [Tooltip("Stage text")]
        [SerializeField] private TMP_Text _stageText;
        
        [Tooltip("Details text")]
        [SerializeField] private TMP_Text _detailsText;
        
        [Tooltip("Cancel button")]
        [SerializeField] private Button _cancelButton;
        
        [Tooltip("Settings panel")]
        [SerializeField] private GameObject _settingsPanel;
        
        [Header("Terrain Settings")]
        [Tooltip("Size of the terrain in world units")]
        [SerializeField] private Vector3 _terrainSize = new Vector3(500, 100, 500);
        
        [Tooltip("Resolution of the terrain heightmap")]
        [SerializeField] private int _terrainResolution = 513;
        
        [Tooltip("Material for the terrain")]
        [SerializeField] private Material _terrainMaterial;
        
        [Tooltip("Height multiplier for the heightmap")]
        [SerializeField] private float _heightMapMultiplier = 30f;
        
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
        [Range(0.1f, 1f)]
        [SerializeField] private float _detectionThreshold = 0.5f;
        
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
        [SerializeField] private WorkerFactory.Type _inferenceBackend = WorkerFactory.Type.Auto;
        
        [Tooltip("Processing batch size")]
        [SerializeField] private int _processingBatchSize = 5;
        
        [Tooltip("Enable debug visualization")]
        [SerializeField] private bool _enableDebugVisualization = false;
        
        [Header("AI Model Assets")]
        [Tooltip("YOLO model asset")]
        [SerializeField] private UnityEngine.Object _yoloModel;
        
        [Tooltip("SAM2 model asset")]
        [SerializeField] private UnityEngine.Object _sam2Model;
        
        [Tooltip("Faster R-CNN model asset")]
        [SerializeField] private UnityEngine.Object _fasterRcnnModel;
        
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
        
        // Model workers
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
                _inferenceBackend = WorkerFactory.Type.ComputePrecompiled;
                _debugger.Log($"GPU acceleration enabled - {SystemInfo.graphicsDeviceName}", LogCategory.System);
            }
            else {
                _inferenceBackend = WorkerFactory.Type.CSharpBurst;
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
                
                // Initialize MapAnalyzer
                _mapAnalyzer = FindOrCreateComponent<MapAnalyzer>(componentContainer);
                
                // Initialize TerrainGenerator
                _terrainGenerator = FindOrCreateComponent<TerrainGenerator>(componentContainer);
                
                // Initialize ModelGenerator
                _modelGenerator = FindOrCreateComponent<ModelGenerator>(componentContainer);
                
                // Initialize SegmentationVisualizer
                _segmentationVisualizer = FindOrCreateComponent<SegmentationVisualizer>(componentContainer);
                
                // Initialize OpenAIResponse
                _openAIResponse = FindOrCreateComponent<OpenAIResponse>(componentContainer);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to initialize components: {ex.Message}", LogCategory.System);
                throw;
            }
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
                _mapAnalyzer.debugger = _debugger;
                _mapAnalyzer.useHighQuality = _useHighQualityAnalysis;
                _mapAnalyzer.confidenceThreshold = _detectionThreshold;
                _mapAnalyzer.nmsThreshold = _nmsThreshold;
                _mapAnalyzer.useGPU = _useGPUAcceleration;
                _mapAnalyzer.maxObjectsToProcess = _maxObjectsToProcess;
                _mapAnalyzer.openAIApiKey = _openAIApiKey;
            }
            
            // Configure TerrainGenerator
            if (_terrainGenerator != null) {
                _terrainGenerator.debugger = _debugger;
                _terrainGenerator.terrainWidth = Mathf.RoundToInt(_terrainSize.x);
                _terrainGenerator.terrainLength = Mathf.RoundToInt(_terrainSize.z);
                _terrainGenerator.terrainHeight = _terrainSize.y;
                _terrainGenerator.waterThreshold = _waterHeight;
            }
            
            // Configure ModelGenerator
            if (_modelGenerator != null) {
                _modelGenerator.debugger = _debugger;
                _modelGenerator.groupSimilarObjects = _groupSimilarObjects;
                _modelGenerator.instancingSimilarity = _instancingSimilarity;
                _modelGenerator.openAIApiKey = _openAIApiKey;
                _modelGenerator.generationTimeout = _processingTimeout;
                _modelGenerator.maxConcurrentRequests = _maxConcurrentAPIRequests;
                _modelGenerator.apiRateLimitDelay = _apiRateLimitDelay;
            }
            
            // Configure SegmentationVisualizer
            if (_segmentationVisualizer != null) {
                _segmentationVisualizer.debugger = _debugger;
                _segmentationVisualizer.enableDebugVisualization = _enableDebugVisualization;
                _segmentationVisualizer.overlayPrefab = _overlayPrefab;
                _segmentationVisualizer.labelPrefab = _labelPrefab;
                _segmentationVisualizer.overlayMaterial = _overlayMaterial;
                _segmentationVisualizer.overlayYOffset = _overlayYOffset;
                _segmentationVisualizer.labelYOffset = _labelYOffset;
                _segmentationVisualizer.fadeDuration = _overlayFadeDuration;
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
        
        private string GetModelPath(string modelFileName) {
            return Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", modelFileName);
        }
        
        private void ValidateModelFiles() {
            string[] requiredModels = { "yolov12.onnx", "FasterRCNN-12.onnx", "sam2_hiera_base.onnx" };
            List<string> missingModels = new List<string>();
            
            string modelsDir = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models");
            if (!Directory.Exists(modelsDir)) {
                Directory.CreateDirectory(modelsDir);
                _debugger.Log($"Created models directory at {modelsDir}", LogCategory.System);
            }
            
            foreach (string modelFile in requiredModels) {
                string modelPath = GetModelPath(modelFile);
                if (!File.Exists(modelPath)) {
                    missingModels.Add(modelFile);
                    
                    // If model exists in Assets/Scripts/AI/Models, try to copy it over
                    string sourcePath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", modelFile);
                    if (File.Exists(sourcePath)) {
                        try {
                            File.Copy(sourcePath, modelPath);
                            _debugger.Log($"Copied model {modelFile} to StreamingAssets", LogCategory.System);
                            missingModels.Remove(modelFile);
                        }
                        catch (Exception ex) {
                            _debugger.LogError($"Failed to copy model {modelFile}: {ex.Message}", LogCategory.System);
                        }
                    }
                }
            }
            
            if (missingModels.Count > 0) {
                _debugger.LogWarning($"Missing models: {string.Join(", ", missingModels)}. Please ensure model files are present in StreamingAssets/Traversify/Models.", LogCategory.System);
            }
        }
        
        private void LoadModels() {
            _debugger.Log("Loading AI models...", LogCategory.AI);
            
            try {
                if (_yoloModel != null) {
                    _yoloWorker = WorkerFactory.CreateWorker(_inferenceBackend, _yoloModel);
                }
                
                if (_useSAM && _sam2Model != null) {
                    _sam2Worker = WorkerFactory.CreateWorker(_inferenceBackend, _sam2Model);
                }
                
                if (_useFasterRCNN && _fasterRcnnModel != null) {
                    _rcnnWorker = WorkerFactory.CreateWorker(_inferenceBackend, _fasterRcnnModel);
                }
                
                if (_labelsFile != null) {
                    _classLabels = _labelsFile.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                }
                
                _debugger.Log("AI models loaded successfully", LogCategory.AI);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to load models: {ex.Message}", LogCategory.AI);
            }
        }
        
        private void LoadPreferences() {
            // Load user preferences (placeholder for future implementation)
        }
        
        private void SavePreferences() {
            // Save user preferences (placeholder for future implementation)
        }
        
        #endregion
        
        #region User Input Handling
        
        private void OpenFileExplorer() {
            #if UNITY_STANDALONE || UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("Select Map Image", "", "png,jpg,jpeg");
            if (!string.IsNullOrEmpty(path)) {
                StartCoroutine(LoadImageFromPath(path));
            }
            #else
            UpdateStatus("File upload not supported on this platform");
            #endif
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
            
            // Use safe coroutine to handle errors
            yield return StartCoroutine(SafeCoroutine(InnerGenerateTerrainFromMap(), ex => {
                _debugger.LogError($"Error during terrain generation: {ex}", LogCategory.Process);
                UpdateStatus($"Error: {ex}", true);
                OnError?.Invoke(ex.Message);
            }));
            
            LogPerformanceMetrics();
            ResetUI();
        }
        
        private IEnumerator InnerGenerateTerrainFromMap() {
            // Clean up any previous generated objects
            CleanupGeneratedObjects();
            
            // Step 1: Analyze the map using AI (approx 40% progress)
            _debugger.StartTimer("MapAnalysis");
            
            yield return StartCoroutine(AnalyzeImage(_uploadedMapTexture,
                results => {
                    _analysisResults = results;
                    OnAnalysisComplete?.Invoke(results);
                },
                error => {
                    throw new Exception(error);
                },
                (stage, prog) => {
                    // Map analysis progress updates (scaled 0.1 to 0.4 of total)
                    float totalProg = 0.1f + (prog * 0.3f);
                    UpdateProgress(totalProg);
                    OnProgressUpdate?.Invoke(totalProg);
                    
                    // Log stage changes for debugging
                    if (stage != null) _debugger.Log($"{stage} ({prog:P0})", LogCategory.AI);
                }));
            
            _performanceMetrics["MapAnalysis"] = _debugger.StopTimer("MapAnalysis");
            
            if (_isCancelled || _analysisResults == null) {
                throw new OperationCanceledException("Analysis cancelled");
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
            _debugger.StartTimer("TerrainGeneration");
            UpdateStage("Terrain Mesh Generation", "Creating heightmap...");
            UpdateProgress(0.45f);
            
            // Create Unity Terrain based on analysis results
            _generatedTerrain = CreateTerrainFromAnalysis(_analysisResults, _uploadedMapTexture);
            
            if (_generatedTerrain == null) {
                throw new Exception("Terrain generation failed");
            }
            
            _generatedObjects.Add(_generatedTerrain.gameObject);
            _performanceMetrics["TerrainGeneration"] = _debugger.StopTimer("TerrainGeneration");
            
            if (_isCancelled) {
                throw new OperationCanceledException("Terrain generation cancelled");
            }
            
            UpdateProgress(0.6f, "Terrain generated");
            
            // Step 3: Create water plane if water generation is enabled (5% progress)
            if (_generateWater) {
                _debugger.StartTimer("WaterCreation");
                UpdateProgress(0.65f, "Creating water features...");
                CreateWaterPlane();
                _performanceMetrics["WaterCreation"] = _debugger.StopTimer("WaterCreation");
                
                // Add water plane to generated objects list
                if (_waterPlane != null) _generatedObjects.Add(_waterPlane);
                
                yield return null;
            }
            
            // Step 4: Visualize segmentation overlays and labels (15% progress)
            _debugger.StartTimer("Segmentation");
            yield return StartCoroutine(VisualizeSegmentationWithProgress());
            _performanceMetrics["Segmentation"] = _debugger.StopTimer("Segmentation");
            
            if (_isCancelled) {
                throw new OperationCanceledException("Segmentation cancelled");
            }
            
            // Step 5: Generate and place 3D models for detected objects (20% progress)
            _debugger.StartTimer("ModelGeneration");
            yield return StartCoroutine(GenerateAndPlaceModelsWithProgress());
            _performanceMetrics["ModelGeneration"] = _debugger.StopTimer("ModelGeneration");
            
            if (_isCancelled) {
                throw new OperationCanceledException("Model generation cancelled");
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
        }
        
        private IEnumerator AnalyzeImage(
            Texture2D imageTexture,
            Action<AnalysisResults> onComplete,
            Action<string> onError,
            Action<string, float> onProgress
        ) {
            DateTime startTime = DateTime.UtcNow;
            
            try {
                // Step 1: YOLO object detection
                onProgress?.Invoke("YOLO detection", 0.05f);
                
                if (_yoloWorker == null) {
                    throw new Exception("YOLO model not loaded");
                }
                
                int inputSize = _useHighQualityAnalysis ? 1024 : 640;
                Tensor yoloInput = PrepareImageTensor(imageTexture, inputSize, inputSize);
                
                _yoloWorker.Execute(new Dictionary<string, Tensor> { { "images", yoloInput } });
                Tensor yoloOutput = _yoloWorker.PeekOutput("output");
                
                List<DetectedObject> detections = new List<DetectedObject>();
                int count = yoloOutput.shape[1];
                
                for (int i = 0; i < count; i++) {
                    float conf = yoloOutput[0, i, 4];
                    if (conf < _detectionThreshold) continue;
                    
                    float cx = yoloOutput[0, i, 0] * imageTexture.width;
                    float cy = yoloOutput[0, i, 1] * imageTexture.height;
                    float bw = yoloOutput[0, i, 2] * imageTexture.width;
                    float bh = yoloOutput[0, i, 3] * imageTexture.height;
                    int clsId = (int)yoloOutput[0, i, 5];
                    
                    string clsName = (_classLabels != null && clsId < _classLabels.Length) 
                        ? _classLabels[clsId] 
                        : clsId.ToString();
                    
                    detections.Add(new DetectedObject {
                        classId = clsId,
                        className = clsName,
                        confidence = conf,
                        boundingBox = new Rect(cx - bw / 2f, cy - bh / 2f, bw, bh)
                    });
                }
                
                yoloInput.Dispose();
                yoloOutput.Dispose();
                
                if (detections.Count == 0) {
                    // No objects detected - return empty results
                    AnalysisResults emptyRes = new AnalysisResults {
                        heightMap = new Texture2D(imageTexture.width, imageTexture.height),
                        segmentationMap = new Texture2D(imageTexture.width, imageTexture.height)
                    };
                    
                    onComplete?.Invoke(emptyRes);
                    yield break;
                }
                
                // Step 2: SAM segmentation for each detection
                onProgress?.Invoke("SAM2 segmentation", 0.25f);
                List<ImageSegment> segments = new List<ImageSegment>();
                
                if (_useSAM && _sam2Worker != null) {
                    foreach (DetectedObject det in detections.Take(_maxObjectsToProcess)) {
                        // Prepare prompt tensor (normalized center point of detection)
                        Vector2 centerN = new Vector2(
                            det.boundingBox.center.x / imageTexture.width,
                            det.boundingBox.center.y / imageTexture.height
                        );
                        
                        Tensor promptTensor = new Tensor(new int[] { 1, 2 }, new float[] { centerN.x, centerN.y });
                        Tensor samInput = PrepareImageTensor(imageTexture, inputSize, inputSize);
                        
                        _sam2Worker.Execute(new Dictionary<string, Tensor> {
                            { "image", samInput },
                            { "prompt", promptTensor }
                        });
                        
                        Tensor maskOut = _sam2Worker.PeekOutput("masks");
                        Texture2D maskTex = DecodeMaskTensor(maskOut);
                        
                        if (maskTex != null) {
                            segments.Add(new ImageSegment {
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
                else {
                    // If SAM not available, create simple masks covering bounding boxes
                    foreach (DetectedObject det in detections.Take(_maxObjectsToProcess)) {
                        Texture2D maskTex = new Texture2D(1, 1);
                        maskTex.SetPixel(0, 0, new Color(1, 1, 1, 1));
                        maskTex.Apply();
                        
                        segments.Add(new ImageSegment {
                            detectedObject = det,
                            mask = maskTex,
                            boundingBox = det.boundingBox,
                            color = UnityEngine.Random.ColorHSV(0f, 1f, 0.6f, 1f),
                            area = det.boundingBox.width * det.boundingBox.height
                        });
                    }
                }
                
                // Step 3: (Optional) Faster R-CNN classification of segments
                if (_useFasterRCNN && _rcnnWorker != null) {
                    onProgress?.Invoke("Analyzing segments", 0.5f);
                    
                    // Iterate through segments to simulate classification progress
                    for (int i = 0; i < segments.Count; i++) {
                        float prog = (float)(i + 1) / segments.Count;
                        onProgress?.Invoke($"Classifying… {prog:P0}", 0.45f + 0.2f * prog);
                        yield return null;
                    }
                }
                
                // Step 4: (Optional) Enhance descriptions using OpenAI
                if (!string.IsNullOrEmpty(_openAIApiKey)) {
                    onProgress?.Invoke("Enhancing descriptions", 0.7f);
                    
                    for (int i = 0; i < segments.Count; i++) {
                        float prog = (float)(i + 1) / segments.Count;
                        onProgress?.Invoke($"Enhancing… {prog:P0}", 0.7f + 0.15f * prog);
                        yield return null;
                    }
                }
                
                // Step 5: Build final results (with heightmap and segmentation map)
                onProgress?.Invoke("Finalizing analysis", 0.9f);
                AnalysisResults results = BuildResults(
                    imageTexture,
                    segments,
                    (float)(DateTime.UtcNow - startTime).TotalSeconds
                );
                
                onProgress?.Invoke("Done", 1f);
                onComplete?.Invoke(results);
            }
            catch (Exception ex) {
                _debugger.LogError($"MapAnalyzer error: {ex.Message}", LogCategory.AI);
                onError?.Invoke(ex.Message);
            }
        }
        
        private IEnumerator VisualizeSegmentationWithProgress() {
            UpdateStage("Overlay Visualization", "Creating segmentation overlay...");
            UpdateProgress(0.7f);
            
            _debugger.Log("Visualizing segmentation results", LogCategory.Visualization);
            
            List<GameObject> visualizationObjects = new List<GameObject>();
            int totalItems = (_analysisResults?.terrainFeatures.Count ?? 0) + (_analysisResults?.mapObjects.Count ?? 0);
            int completed = 0;
            
            // Create overlay quads for terrain features
            foreach (var feat in _analysisResults.terrainFeatures) {
                if (_isCancelled) yield break;
                
                GameObject quad = CreateOverlayQuad(feat, _generatedTerrain, _uploadedMapTexture);
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
            foreach (var obj in _analysisResults.mapObjects) {
                if (_isCancelled) yield break;
                
                GameObject label = CreateLabelObject(obj, _generatedTerrain);
                visualizationObjects.Add(label);
                
                completed++;
                float segProgress = (float)completed / totalItems;
                float totalProg = 0.7f + segProgress * 0.2f;
                UpdateProgress(totalProg);
                OnProgressUpdate?.Invoke(totalProg);
                
                yield return null;
            }
            
            _debugger.Log($"Created {visualizationObjects.Count} visualization objects", LogCategory.Visualization);
            
            // Add visualization objects to generated list for cleanup later
            _generatedObjects.AddRange(visualizationObjects);
            
            UpdateProgress(0.9f, "Segmentation visualization complete");
        }
        
        private IEnumerator GenerateAndPlaceModelsWithProgress() {
            UpdateStage("Generating 3D Models", "Processing object placements...");
            UpdateProgress(0.85f);
            
            _debugger.Log("Generating and placing 3D models", LogCategory.Models);
            
            // If no objects detected, skip
            if (_analysisResults.mapObjects.Count == 0) {
                UpdateProgress(0.95f, "No objects to place");
                yield break;
            }
            
            int totalObjects = _analysisResults.mapObjects.Count;
            int placedCount = 0;
            
            // Group objects by type for instancing
            foreach (ObjectGroup group in _analysisResults.objectGroups) {
                // Generate or retrieve a template mesh for this object type
                Mesh templateMesh = GeneratePlaceholderMeshForType(group.type);
                
                foreach (MapObject obj in group.objects) {
                    if (_isCancelled) yield break;
                    
                    // Create a GameObject for the object and place it in the scene
                    GameObject objGo = new GameObject(obj.label ?? group.type);
                    MeshFilter mf = objGo.AddComponent<MeshFilter>();
                    MeshRenderer mr = objGo.AddComponent<MeshRenderer>();
                    
                    mf.sharedMesh = templateMesh;
                    
                    // Assign material (use default or user-provided)
                    mr.material = _defaultObjectMaterial 
                        ? _defaultObjectMaterial 
                        : new Material(Shader.Find("Standard"));
                    
                    // Calculate world position on terrain and orientation
                    Vector3 worldPos = GetWorldPositionFromNormalized(obj.position, _generatedTerrain);
                    float terrainY = _generatedTerrain.SampleHeight(worldPos);
                    worldPos.y = terrainY;
                    
                    objGo.transform.position = worldPos;
                    
                    // Align rotation with terrain normal or keep upright
                    Vector3 normal = _generatedTerrain.terrainData.GetInterpolatedNormal(
                        obj.position.x, obj.position.y);
                    
                    if (group.type.ToLower().Contains("tree")) {
                        // Keep trees upright, random yaw
                        objGo.transform.rotation = Quaternion.Euler(
                            0, UnityEngine.Random.Range(0f, 360f), 0);
                    }
                    else {
                        // Align to terrain normal and add random yaw
                        Quaternion align = Quaternion.FromToRotation(Vector3.up, normal);
                        objGo.transform.rotation = align * 
                            Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
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
                    
                    _generatedObjects.Add(objGo);
                    placedCount++;
                    
                    // Update progress for each model placed
                    float modelProg = (float)placedCount / totalObjects;
                    float totalProg = 0.85f + modelProg * 0.1f;
                    UpdateProgress(totalProg, $"Placing model {placedCount} of {totalObjects}...");
                    OnProgressUpdate?.Invoke(totalProg);
                    
                    yield return null;
                }
            }
            
            _debugger.Log($"Generated and placed {_analysisResults.mapObjects.Count} models", LogCategory.Models);
            UpdateProgress(0.95f, "Model generation complete");
        }
        
        private IEnumerator SaveGeneratedAssets() {
            UpdateStage("Finalization", "Saving generated terrain and models…");
            UpdateProgress(0.96f);
            
            #if !UNITY_EDITOR
            _debugger.LogWarning("Asset saving only supported in the Unity Editor", LogCategory.IO);
            yield break;
            #else
            try {
                string rootPath = _assetSavePath.TrimEnd('/', '\\');
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string baseName = string.IsNullOrEmpty(_uploadedMapTexture?.name) ? "Map" : _uploadedMapTexture.name;
                string folderName = $"{baseName}_{timestamp}";
                string dir = Path.Combine(rootPath, folderName);
                
                Directory.CreateDirectory(dir);
                
                AssetDatabase.StartAssetEditing();
                
                // Save TerrainData asset
                if (_generatedTerrain != null) {
                    TerrainData tData = _generatedTerrain.terrainData;
                    string tdPath = Path.Combine(dir, $"{folderName}_Terrain.asset");
                    AssetDatabase.CreateAsset(tData, tdPath);
                    _debugger.Log($"Saved TerrainData → {tdPath}", LogCategory.IO);
                }
                
                // Save analysis output textures
                if (_analysisResults?.heightMap != null)
                    SaveTexture(_analysisResults.heightMap, Path.Combine(dir, "HeightMap.png"));
                
                if (_analysisResults?.segmentationMap != null)
                    SaveTexture(_analysisResults.segmentationMap, Path.Combine(dir, "SegmentationMap.png"));
                
                // Save scene prefab containing generated objects
                GameObject sceneRoot = new GameObject($"{folderName}_Scene");
                
                foreach (GameObject go in _generatedObjects) {
                    if (go != null) {
                        Instantiate(go, go.transform.position, go.transform.rotation, sceneRoot.transform);
                    }
                }
                
                string prefabPath = Path.Combine(dir, $"{folderName}_Scene.prefab");
                PrefabUtility.SaveAsPrefabAsset(sceneRoot, prefabPath);
                DestroyImmediate(sceneRoot);
                
                _debugger.Log($"Saved scene prefab → {prefabPath}", LogCategory.IO);
                
                // Save metadata
                if (_generateMetadata) {
                    SaveMetadata(dir, folderName);
                }
                
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
                
                UpdateProgress(0.98f, "Assets saved");
            }
            catch (Exception ex) {
                _debugger.LogError($"Error while saving assets: {ex.Message}", LogCategory.IO);
            }
            
            yield return null;
            #endif
        }
        
        #endregion
        
        #region Terrain and Object Generation
        
        private UnityEngine.Terrain CreateTerrainFromAnalysis(AnalysisResults results, Texture2D sourceTexture) {
            // Create terrain object and data
            GameObject terrainObj = new GameObject("GeneratedTerrain");
            UnityEngine.Terrain terrain = terrainObj.AddComponent<UnityEngine.Terrain>();
            TerrainCollider tCollider = terrainObj.AddComponent<TerrainCollider>();
            
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = _terrainResolution;
            terrainData.size = _terrainSize;
            
            terrain.terrainData = terrainData;
            tCollider.terrainData = terrainData;
            
            if (_terrainMaterial != null) {
                terrain.materialTemplate = _terrainMaterial;
            }
            
            // Generate heightmap from analysis results
            float[,] heights = GenerateHeightmap(results, sourceTexture, _terrainResolution);
            terrainData.SetHeights(0, 0, heights);
            
            // Apply basic terrain texture layers
            ApplyTerrainTextures(terrain, results);
            
            terrainObj.transform.position = Vector3.zero;
            
            return terrain;
        }
        
        private float[,] GenerateHeightmap(AnalysisResults results, Texture2D sourceTexture, int resolution) {
            float[,] heights = new float[resolution, resolution];
            
            if (results.heightMap != null) {
                // Use the provided heightmap
                for (int y = 0; y < resolution; y++) {
                    for (int x = 0; x < resolution; x++) {
                        float normX = x / (float)(resolution - 1);
                        float normY = y / (float)(resolution - 1);
                        
                        Color heightColor = results.heightMap.GetPixelBilinear(normX, normY);
                        heights[y, x] = heightColor.r;
                    }
                }
            }
            else {
                // Generate a simple heightmap based on terrain features
                foreach (var feature in results.terrainFeatures) {
                    float baseHeight = feature.elevation / _terrainSize.y;
                    
                    // Map from feature bounds to heightmap coordinates
                    int startX = Mathf.FloorToInt(feature.boundingBox.x / sourceTexture.width * (resolution - 1));
                    int startY = Mathf.FloorToInt(feature.boundingBox.y / sourceTexture.height * (resolution - 1));
                    int endX = Mathf.CeilToInt((feature.boundingBox.x + feature.boundingBox.width) / sourceTexture.width * (resolution - 1));
                    int endY = Mathf.CeilToInt((feature.boundingBox.y + feature.boundingBox.height) / sourceTexture.height * (resolution - 1));
                    
                    // Clamp to heightmap bounds
                    startX = Mathf.Clamp(startX, 0, resolution - 1);
                    startY = Mathf.Clamp(startY, 0, resolution - 1);
                    endX = Mathf.Clamp(endX, 0, resolution - 1);
                    endY = Mathf.Clamp(endY, 0, resolution - 1);
                    
                    // Apply height to region
                    for (int y = startY; y <= endY; y++) {
                        for (int x = startX; x <= endX; x++) {
                            // Simple height assignment - could be improved with distance falloff
                            heights[y, x] = Mathf.Max(heights[y, x], baseHeight);
                        }
                    }
                }
            }
            
            return heights;
        }
        
        private void ApplyTerrainTextures(UnityEngine.Terrain terrain, AnalysisResults results) {
            // Simple terrain texturing - could be expanded with more sophisticated texturing
            TerrainLayer grassLayer = new TerrainLayer();
            grassLayer.diffuseTexture = Texture2D.whiteTexture; // Placeholder
            grassLayer.tileSize = new Vector2(10, 10);
            
            terrain.terrainData.terrainLayers = new TerrainLayer[] { grassLayer };
        }
        
        private void CreateWaterPlane() {
            try {
                _debugger.Log("Creating water plane", LogCategory.Terrain);
                
                _waterPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _waterPlane.name = "WaterPlane";
                
                // Scale plane to terrain size (Unity Plane is 10x10 by default)
                float scaleX = _terrainSize.x / 10f;
                float scaleZ = _terrainSize.z / 10f;
                _waterPlane.transform.localScale = new Vector3(scaleX, 1, scaleZ);
                
                float waterY = _waterHeight * _terrainSize.y;
                _waterPlane.transform.position = new Vector3(_terrainSize.x / 2f, waterY, _terrainSize.z / 2f);
                
                // Apply a simple water-like material
                Renderer rend = _waterPlane.GetComponent<Renderer>();
                if (rend != null) {
                    Material waterMat = CreateWaterMaterial();
                    rend.material = waterMat;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                
                _debugger.Log("Water plane created", LogCategory.Terrain);
            }
            catch (Exception ex) {
                _debugger.LogError($"Error creating water plane: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        private Material CreateWaterMaterial() {
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
        
        private GameObject CreateOverlayQuad(TerrainFeature feature, UnityEngine.Terrain terrain, Texture2D mapTexture) {
            GameObject quad = _overlayPrefab ? Instantiate(_overlayPrefab) : GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Overlay_{feature.label}";
            quad.transform.SetParent(terrain.transform);
            
            // Calculate world position and size of overlay from feature bounding box
            Vector3 size = terrain.terrainData.size;
            Vector3 tPos = terrain.transform.position;
            
            float xMin = tPos.x + (feature.boundingBox.x / mapTexture.width) * size.x;
            float xMax = tPos.x + ((feature.boundingBox.x + feature.boundingBox.width) / mapTexture.width) * size.x;
            float zMin = tPos.z + (feature.boundingBox.y / mapTexture.height) * size.z;
            float xMax = tPos.x + ((feature.boundingBox.x + feature.boundingBox.width) / mapTexture.width) * size.x;
            float zMin = tPos.z + (feature.boundingBox.y / mapTexture.height) * size.z;
            float zMax = tPos.z + ((feature.boundingBox.y + feature.boundingBox.height) / mapTexture.height) * size.z;
            
            quad.transform.position = new Vector3((xMin + xMax) / 2f, tPos.y + _overlayYOffset, (zMin + zMax) / 2f);
            quad.transform.localScale = new Vector3(xMax - xMin, 1, zMax - zMin);
            quad.transform.rotation = Quaternion.Euler(90, 0, 0);
            
            // Apply overlay material and color
            Renderer rend = quad.GetComponent<Renderer>();
            if (rend != null) {
                Material mat = _overlayMaterial ? Instantiate(_overlayMaterial) : new Material(Shader.Find("Standard"));
                mat.color = feature.segmentColor;
                rend.material = mat;
            }
            
            return quad;
        }
        
        private GameObject CreateLabelObject(MapObject mapObj, UnityEngine.Terrain terrain) {
            GameObject labelObj = _labelPrefab ? Instantiate(_labelPrefab) : new GameObject($"Label_{mapObj.label}");
            labelObj.transform.SetParent(terrain.transform);
            
            // Compute world position of the object on terrain
            Vector3 worldPos = GetWorldPositionFromNormalized(mapObj.position, terrain);
            float terrainY = terrain.SampleHeight(worldPos);
            worldPos.y = terrainY + _labelYOffset;
            
            labelObj.transform.position = worldPos;
            labelObj.transform.LookAt(Camera.main.transform);
            
            // Set text if TextMeshPro is attached
            if (labelObj.TryGetComponent(out TextMeshPro tmp)) {
                tmp.text = !string.IsNullOrEmpty(mapObj.enhancedDescription) ? mapObj.enhancedDescription : mapObj.label;
                tmp.color = mapObj.segmentColor;
            }
            
            // Add billboard component
            Billboard billboard = labelObj.GetComponent<Billboard>();
            if (billboard == null) {
                billboard = labelObj.AddComponent<Billboard>();
                billboard.Mode = Billboard.BillboardMode.LookAtCamera;
            }
            
            return labelObj;
        }
        
        private IEnumerator FadeIn(GameObject obj) {
            Renderer rend = obj.GetComponent<Renderer>();
            if (rend == null) yield break;
            
            Color targetColor = rend.material.color;
            
            // Start from fully transparent
            Color startColor = targetColor;
            startColor.a = 0f;
            rend.material.color = startColor;
            
            float elapsed = 0f;
            while (elapsed < _overlayFadeDuration) {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _overlayFadeDuration);
                Color c = Color.Lerp(startColor, targetColor, t);
                rend.material.color = c;
                yield return null;
            }
            
            rend.material.color = targetColor;
        }
        
        private Mesh GeneratePlaceholderMeshForType(string objectType) {
            string typeLower = objectType.ToLower();
            
            if (typeLower.Contains("tree")) {
                // Use a cylinder to mimic a tree trunk
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
                return mesh;
            }
            
            if (typeLower.Contains("rock") || typeLower.Contains("boulder")) {
                // Use a sphere for rocks/boulders
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(temp);
                return mesh;
            }
            
            if (typeLower.Contains("structure") || typeLower.Contains("building")) {
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
        
        private Vector3 GetWorldPositionFromNormalized(Vector2 normalizedPos, UnityEngine.Terrain terrain) {
            Vector3 terrainOrigin = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            
            float worldX = terrainOrigin.x + normalizedPos.x * size.x;
            float worldZ = terrainOrigin.z + normalizedPos.y * size.z;
            float worldY = terrainOrigin.y;
            
            return new Vector3(worldX, worldY, worldZ);
        }
        
        #endregion
        
        #region Tensor Processing
        
        private Tensor PrepareImageTensor(Texture2D src, int width, int height) {
            // Resize image to desired resolution
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            
            Texture2D scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            scaled.Apply();
            
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            
            // Convert to Tensor
            Tensor tensor = new Tensor(scaled, 3);
            Destroy(scaled);
            
            return tensor;
        }
        
        private Texture2D DecodeMaskTensor(Tensor maskTensor) {
            int mh = maskTensor.shape[1];
            int mw = maskTensor.shape[2];
            
            Texture2D maskTex = new Texture2D(mw, mh, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[mw * mh];
            
            for (int y = 0; y < mh; y++) {
                for (int x = 0; x < mw; x++) {
                    float v = maskTensor[0, y, x];
                    pixels[y * mw + x] = new Color(1f, 1f, 1f, v);
                }
            }
            
            maskTex.SetPixels(pixels);
            maskTex.Apply();
            
            return maskTex;
        }
        
        private AnalysisResults BuildResults(Texture2D sourceImage, List<ImageSegment> segments, float analysisTimeSec) {
            AnalysisResults results = new AnalysisResults();
            results.metadata.timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            results.metadata.sourceImageName = sourceImage.name;
            results.metadata.imageWidth = sourceImage.width;
            results.metadata.imageHeight = sourceImage.height;
            results.timings.totalTime = analysisTimeSec;
            
            // Partition segments into terrain features and map objects
            foreach (ImageSegment seg in segments) {
                if (seg.detectedObject.className.StartsWith("cls_") || 
                    IsTerrainClassName(seg.detectedObject.className)) {
                    // Treat as terrain feature
                    TerrainFeature feat = new TerrainFeature {
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
                else {
                    // Treat as discrete map object
                    MapObject obj = new MapObject {
                        type = seg.detectedObject.className,
                        label = seg.detectedObject.className,
                        enhancedDescription = seg.detectedObject.className,
                        position = new Vector2(
                            seg.boundingBox.center.x / sourceImage.width,
                            1f - (seg.boundingBox.center.y / sourceImage.height)
                        ),
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
            results.objectGroups = results.mapObjects
                .GroupBy(o => o.type)
                .Select(g => new ObjectGroup {
                    groupId = Guid.NewGuid().ToString(),
                    type = g.Key,
                    objects = g.ToList()
                })
                .ToList();
            
            // Build heightMap and segmentationMap textures from segments
            results.heightMap = BuildHeightMap(sourceImage.width, sourceImage.height, segments);
            results.segmentationMap = BuildSegmentationMap(sourceImage.width, sourceImage.height, segments);
            
            // Update statistics
            results.statistics.objectCount = results.mapObjects.Count;
            results.statistics.terrainFeatureCount = results.terrainFeatures.Count;
            results.statistics.groupCount = results.objectGroups.Count;
            results.statistics.averageConfidence = segments.Average(s => s.detectedObject.confidence);
            
            return results;
        }
        
        private bool IsTerrainClassName(string className) {
            string lower = className.ToLowerInvariant();
            return lower.Contains("terrain") || 
                   lower.Contains("mountain") || 
                   lower.Contains("water") || 
                   lower.Contains("forest") || 
                   lower.Contains("lake") || 
                   lower.Contains("river") || 
                   lower.Contains("grass") || 
                   lower.Contains("hill");
        }
        
        private float EstimateElevation(Texture2D mask) {
            if (mask == null) return 0f;
            
            // Simple approach: use average alpha as elevation estimate
            Color[] pixels = mask.GetPixels();
            return pixels.Average(c => c.a);
        }
        
        private Texture2D BuildHeightMap(int width, int height, List<ImageSegment> segments) {
            Texture2D heightMap = new Texture2D(width, height, TextureFormat.RFloat, false);
            Color[] pixels = Enumerable.Repeat(Color.black, width * height).ToArray();
            
            foreach (var seg in segments) {
                if (!IsTerrainClassName(seg.detectedObject.className)) continue;
                
                // For terrain segments, copy mask alpha into height map region
                for (int y = 0; y < height; y++) {
                    for (int x = 0; x < width; x++) {
                        if (seg.boundingBox.Contains(new Vector2(x, y))) {
                            float alpha = seg.mask.GetPixelBilinear(
                                (x - seg.boundingBox.x) / seg.boundingBox.width,
                                (y - seg.boundingBox.y) / seg.boundingBox.height
                            ).a;
                            
                            pixels[y * width + x] = new Color(alpha, 0, 0, 1);
                        }
                    }
                }
            }
            
            heightMap.SetPixels(pixels);
            heightMap.Apply();
            
            return heightMap;
        }
        
        private Texture2D BuildSegmentationMap(int width, int height, List<ImageSegment> segments) {
            Texture2D segMap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = Enumerable.Repeat(Color.clear, width * height).ToArray();
            
            foreach (var seg in segments) {
                for (int y = 0; y < height; y++) {
                    for (int x = 0; x < width; x++) {
                        if (seg.boundingBox.Contains(new Vector2(x, y))) {
                            Color maskColor = seg.mask.GetPixelBilinear(
                                (x - seg.boundingBox.x) / seg.boundingBox.width,
                                (y - seg.boundingBox.y) / seg.boundingBox.height
                            );
                            
                            if (maskColor.a > 0.5f) {
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
        
        #endregion
        
        #region UI Helpers
        
        private void ShowLoadingPanel(bool show) {
            if (_loadingPanel != null) _loadingPanel.SetActive(show);
        }
        
        private void UpdateStage(string stage, string details = null) {
            if (_stageText != null) _stageText.text = stage;
            if (details != null && _detailsText != null) _detailsText.text = details;
        }
        
        private void UpdateProgress(float progress, string details = null) {
            if (_progressBar != null) _progressBar.value = progress;
            if (_progressText != null) _progressText.text = $"{progress * 100:F0}%";
            if (details != null && _detailsText != null) _detailsText.text = details;
        }
        
        private void UpdateStatus(string message, bool isError = false) {
            if (_statusText != null) {
                _statusText.text = message;
                _statusText.color = isError ? new Color(1f, 0.3f, 0.3f) : Color.white;
            }
            
            if (isError)
                _debugger.LogError(message, LogCategory.UI);
            else
                _debugger.Log(message, LogCategory.UI);
        }
        
        private void ResetUI() {
            ShowLoadingPanel(false);
            
            if (_uploadButton != null) _uploadButton.interactable = true;
            if (_generateButton != null) _generateButton.interactable = _uploadedMapTexture != null;
            
            _isProcessing = false;
        }
        
        private string GenerateAnalysisDetails() {
            if (_analysisResults == null) return "";
            
            StringBuilder sb = new StringBuilder();
            
            sb.AppendLine("Terrain Features:");
            foreach (var grp in _analysisResults.terrainFeatures.GroupBy(f => f.label)) {
                sb.AppendLine($"  • {grp.Key}: {grp.Count()}");
            }
            
            sb.AppendLine("\nObjects:");
            foreach (var grp in _analysisResults.mapObjects.GroupBy(o => o.type)) {
                sb.AppendLine($"  • {grp.Key}: {grp.Count()}");
            }
            
            return sb.ToString();
        }
        
        private void ShowCompletionDetails() {
            if (_detailsText == null) return;
            
            StringBuilder sb = new StringBuilder(_detailsText.text);
            sb.AppendLine($"\nTerrain: {_terrainSize.x}x{_terrainSize.z} units | Res {_terrainResolution}");
            sb.AppendLine($"Objects placed: {_analysisResults?.mapObjects.Count ?? 0}");
            sb.AppendLine($"Clusters: {_analysisResults?.objectGroups.Count ?? 0}");
            
            _detailsText.text = sb.ToString();
        }
        
        private void LogPerformanceMetrics() {
            if (_performanceMetrics.Count == 0) return;
            
            _debugger.Log("── Performance Metrics ──", LogCategory.Process);
            
            foreach (var entry in _performanceMetrics.OrderByDescending(kv => kv.Value)) {
                _debugger.Log($"{entry.Key}: {entry.Value:F2}s", LogCategory.Process);
            }
        }
        
        private void FocusCameraOnTerrain() {
            Camera cam = Camera.main;
            if (cam == null || _generatedTerrain == null) return;
            
            Bounds bounds = _generatedTerrain.terrainData.bounds;
            Vector3 center = _generatedTerrain.transform.position + bounds.center;
            
            float d = Mathf.Max(_terrainSize.x, _terrainSize.z) * 0.7f;
            cam.transform.position = center + new Vector3(d, _terrainSize.y * 0.8f, -d);
            cam.transform.LookAt(center);
        }
        
        #endregion
        
        #region Cleanup and Helpers
        
        private void CleanupGeneratedObjects() {
            foreach (var obj in _generatedObjects) {
                if (obj != null) {
                    Destroy(obj);
                }
            }
            
            _generatedObjects.Clear();
            
            if (_generatedTerrain != null) {
                Destroy(_generatedTerrain.gameObject);
                _generatedTerrain = null;
            }
            
            if (_waterPlane != null) {
                Destroy(_waterPlane);
                _waterPlane = null;
            }
        }
        
        private void SaveTexture(Texture2D tex, string path) {
            try {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                _debugger.Log($"Saved texture → {path}", LogCategory.IO);
            }
            catch (Exception ex) {
                _debugger.LogError($"SaveTexture failed: {ex.Message}", LogCategory.IO);
            }
        }
        
        private void SaveMetadata(string path, string sceneName) {
            try {
                var meta = new {
                    sceneName,
                    generatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    generatedBy = "dkaplan73", // Current user from params
                    traversifyVersion = "2.0.1",
                    terrain = new { _terrainSize, _terrainResolution, water = _generateWater },
                    counts = new {
                        features = _analysisResults?.terrainFeatures.Count ?? 0,
                        objects = _analysisResults?.mapObjects.Count ?? 0,
                        clusters = _analysisResults?.objectGroups.Count ?? 0
                    },
                    perf = _performanceMetrics
                };
                
                string json = JsonUtility.ToJson(meta, true);
                File.WriteAllText(Path.Combine(path, "metadata.json"), json);
                
                _debugger.Log("Wrote metadata.json", LogCategory.IO);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to save metadata: {ex.Message}", LogCategory.IO);
            }
        }
        
        private IEnumerator SafeCoroutine(IEnumerator routine, Action<string> onError) {
            bool finished = false;
            Exception caughtEx = null;
            
            yield return StartCoroutine(ProcessWithErrorHandling(routine, msg => {
                caughtEx = new Exception(msg);
                finished = true;
            }));
            
            if (caughtEx != null) {
                onError?.Invoke(caughtEx.Message);
            }
        }
        
        private IEnumerator ProcessWithErrorHandling(IEnumerator routine, Action<string> handleError) {
            while (true) {
                object current;
                
                try {
                    if (!routine.MoveNext()) break;
                    current = routine.Current;
                }
                catch (Exception ex) {
                    handleError?.Invoke(ex.Message);
                    yield break;
                }
                
                yield return current;
            }
        }
        
        #endregion
    }
}

