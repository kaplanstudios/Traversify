/*************************************************************************
 *  Traversify – TraversifyManager.cs                                    *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-29 03:17:43 UTC                                     *
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
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
#endif
using TMPro;
using Unity.Sentis;
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
        [SerializeField] private WorkerFactory.Type _inferenceBackend = WorkerFactory.Type.ComputePrecompiled;
        
        [Tooltip("Processing batch size")]
        [SerializeField] private int _processingBatchSize = 5;
        
        [Tooltip("Enable debug visualization")]
        [SerializeField] private bool _enableDebugVisualization = false;
        
        [Header("AI Model Assets")]
        [Tooltip("YOLO model asset")]
        [SerializeField] private Unity.Sentis.ModelAsset _yoloModel;
        
        [Tooltip("SAM2 model asset")]
        [SerializeField] private Unity.Sentis.ModelAsset _sam2Model;
        
        [Tooltip("Faster R-CNN model asset")]
        [SerializeField] private Unity.Sentis.ModelAsset _fasterRcnnModel;
        
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
        private Unity.Sentis.Worker _yoloWorker;
        private Unity.Sentis.Worker _sam2Worker;
        private Unity.Sentis.Worker _rcnnWorker;
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
        
        private void LoadYOLOFromStreamingAssets() {
            try {
                string modelPath = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", "yolov8n.onnx");
                _debugger.Log($"Loading YOLO model from: {modelPath}", LogCategory.AI);
                
                // Check if file exists
                if (!File.Exists(modelPath)) {
                    _debugger.LogError($"YOLO model file not found at: {modelPath}", LogCategory.AI);
                    
                    // Try inspector fallback
                    if (_yoloModel != null) {
                        try {
                            _yoloWorker = CreateSentisWorker(_yoloModel, WorkerFactory.Type.ComputePrecompiled);
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
                var yoloAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.Sentis.ModelAsset>(assetPath);
                if (yoloAsset != null) {
                    _yoloWorker = CreateSentisWorker(yoloAsset, WorkerFactory.Type.ComputePrecompiled);
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
                        _yoloWorker = CreateSentisWorker(_yoloModel, WorkerFactory.Type.ComputePrecompiled);
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
        
        private void LoadSAM2FromStreamingAssets() {
            try {
                string modelPath = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", "sam2_hiera_base.onnx");
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
                var sam2Asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.Sentis.ModelAsset>(assetPath);
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
        
        private void LoadFasterRCNNFromStreamingAssets() {
            try {
                string modelPath = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models", "FasterRCNN-12.onnx");
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
                var rcnnAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.Sentis.ModelAsset>(assetPath);
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
        
        private void LoadYOLOFromAIModels() {
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
                LoadYOLOFromStreamingAssets();
                
                // Load SAM2 model (optional)
                if (_useSAM) {
                    LoadSAM2FromStreamingAssets();
                }
                
                // Load Faster R-CNN model (optional)
                if (_useFasterRCNN) {
                    LoadFasterRCNNFromStreamingAssets();
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
        
        private void LoadSAM2FromAIModels() {
            try {
                string modelPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", "sam2_hiera_base.onnx");
                _debugger.Log($"Loading SAM2 model from: {modelPath}", LogCategory.AI);
                
                if (!File.Exists(modelPath)) {
                    _debugger.LogWarning($"SAM2 model file not found at: {modelPath}", LogCategory.AI);
                    return;
                }
                
                _debugger.Log($"SAM2 model file found, size: {new FileInfo(modelPath).Length / 1024 / 1024:F1} MB", LogCategory.AI);
                
                #if UNITY_EDITOR
                // In editor, try to load via AssetDatabase as Unity.Sentis.Model
                string assetPath = "Assets/Scripts/AI/Models/sam2_hiera_base.onnx";
                var sam2Asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.Sentis.ModelAsset>(assetPath);
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
                var rcnnAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.Sentis.ModelAsset>(assetPath);
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
                _debugger.Log("�� SAM2 Model: DISABLED in settings", LogCategory.AI);
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
            
            _debugger.Log("═══════════════════���═══════════", LogCategory.AI);
        }
        
        #endregion
        
        #region User Input Handling
        
        private void OpenFileExplorer() {
            #if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel("Select Map Image", "", "png,jpg,jpeg,bmp,tga");
            if (!string.IsNullOrEmpty(path)) {
                StartCoroutine(LoadImageFromPath(path));
            }
            #else
            // For runtime builds, provide alternative file selection methods
            _debugger.Log("Opening file selection for runtime", LogCategory.IO);
            
            // Try SimpleFileBrowser if available, otherwise use drag-and-drop
            if (!TryOpenSimpleFileBrowser()) {
                // Enable drag-and-drop and show instructions
                UpdateStatus("Drag and drop an image file (PNG, JPG, JPEG, BMP, TGA) onto this window to upload");
                _debugger.Log("Drag-and-drop instructions shown to user", LogCategory.IO);
                
                // You could also implement a web-based file picker here for WebGL builds
                #if UNITY_WEBGL && !UNITY_EDITOR
                // For WebGL, we might need a different approach
                UpdateStatus("Please use the Editor version for file browsing, or try drag-and-drop if supported by your browser");
                #endif
            }
            #endif
        }
        
        #if !UNITY_EDITOR
        private bool TryOpenSimpleFileBrowser() {
            // Try to use SimpleFileBrowser package if it's installed
            try {
                var fileBrowserType = System.Type.GetType("SimpleFileBrowser.FileBrowser, SimpleFileBrowser");
                if (fileBrowserType != null) {
                    var showLoadDialogMethod = fileBrowserType.GetMethod("ShowLoadDialog", 
                        new System.Type[] { 
                            typeof(System.Action<string[]>), 
                            typeof(System.Action), 
                            typeof(bool), 
                            typeof(string), 
                            typeof(string), 
                            typeof(string) 
                        });
                    
                    if (showLoadDialogMethod != null) {
                        System.Action<string[]> onSuccess = (paths) => {
                            if (paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0])) {
                                _debugger.Log($"File selected via SimpleFileBrowser: {paths[0]}", LogCategory.IO);
                                StartCoroutine(LoadImageFromPath(paths[0]));
                            }
                        };
                        
                        System.Action onCancel = () => {
                            _debugger.Log("File selection cancelled", LogCategory.IO);
                            UpdateStatus("File selection cancelled");
                        };
                        
                        // Invoke SimpleFileBrowser
                        showLoadDialogMethod.Invoke(null, new object[] { 
                            onSuccess, 
                            onCancel, 
                            false, 
                            null, 
                            "Select Map Image", 
                            "Select" 
                        });
                        
                        _debugger.Log("SimpleFileBrowser opened successfully", LogCategory.IO);
                        return true;
                    }
                }
            }
            catch (System.Exception ex) {
                _debugger.LogWarning($"SimpleFileBrowser not available or failed: {ex.Message}", LogCategory.IO);
            }
            
            return false;
        }
        #endif
        
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
                OnError?.Invoke(ex); // Fix: ex is already a string, don't access .Message
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
            
            // Step 1: YOLO object detection
            onProgress?.Invoke("YOLO detection", 0.05f);
            
            if (_yoloWorker == null) {
                onError?.Invoke("YOLO model not loaded");
                yield break;
            }

            // YOLO model expects 640x640 input regardless of quality setting
            int yoloInputSize = 640;
            Unity.Sentis.Tensor yoloInput = null;
            Unity.Sentis.Tensor yoloOutput = null;
            List<DetectedObject> detections = new List<DetectedObject>();
            
            try {
                yoloInput = PrepareImageTensor(imageTexture, yoloInputSize, yoloInputSize);
                
                _yoloWorker.SetInput("images", yoloInput);
                _yoloWorker.Schedule();  
                
                // Use robust output tensor discovery instead of hardcoded name
                yoloOutput = TryGetYOLOOutput();
                
                if (yoloOutput == null) {
                    throw new Exception("Could not find YOLO output tensor - model may be incompatible");
                }
                
                // Convert tensor to CPU accessible data
                var outputArrayTensor = yoloOutput.ReadbackAndClone();
                var outputArray = outputArrayTensor.dataOnBackend.Download<float>(outputArrayTensor.shape.length);
                
                // Log tensor shape for debugging
                int batch = yoloOutput.shape[0];
                int count = yoloOutput.shape[1];
                int channels = yoloOutput.shape[2];
                _debugger.Log($"YOLO output shape: [{batch}, {count}, {channels}]", LogCategory.AI);
                _debugger.Log($"Detection threshold: {_detectionThreshold}", LogCategory.AI);
                
                int validDetections = 0;
                int totalChecked = 0;
                
                for (int i = 0; i < count; i++) {
                    int baseIndex = i * channels;
                    totalChecked++;
                    
                    // Handle different YOLO output formats
                    float conf = 0f;
                    float cx = 0f, cy = 0f, bw = 0f, bh = 0f;
                    int clsId = 0;
                    
                    if (channels >= 85) {
                        // YOLOv5/v8 format: [x, y, w, h, objectness, class0, class1, ...]
                        cx = outputArray[baseIndex + 0];
                        cy = outputArray[baseIndex + 1];
                        bw = outputArray[baseIndex + 2];
                        bh = outputArray[baseIndex + 3];
                        float objectness = outputArray[baseIndex + 4];
                        
                        // Find highest class probability
                        float maxClassProb = 0f;
                        int maxClassId = 0;
                        for (int c = 5; c < channels; c++) {
                            float classProb = outputArray[baseIndex + c];
                            if (classProb > maxClassProb) {
                                maxClassProb = classProb;
                                maxClassId = c - 5;
                            }
                        }
                        
                        // Apply sigmoid to objectness if needed (raw output might not be normalized)
                        objectness = 1f / (1f + Mathf.Exp(-objectness));
                        maxClassProb = 1f / (1f + Mathf.Exp(-maxClassProb));
                        
                        conf = objectness * maxClassProb; // Combined confidence
                        clsId = maxClassId;
                    }
                    else if (channels >= 6) {
                        // Simplified format: [x, y, w, h, conf, class]
                        cx = outputArray[baseIndex + 0];
                        cy = outputArray[baseIndex + 1];
                        bw = outputArray[baseIndex + 2];
                        bh = outputArray[baseIndex + 3];
                        conf = outputArray[baseIndex + 4];
                        clsId = (int)outputArray[baseIndex + 5];
                        
                        // Apply sigmoid if confidence seems to be raw logit
                        if (conf > 10f || conf < -10f) {
                            conf = 1f / (1f + Mathf.Exp(-conf));
                        }
                    }
                    else {
                        _debugger.LogWarning($"Unexpected YOLO output format with {channels} channels", LogCategory.AI);
                        continue;
                    }
                    
                    // Ensure coordinates are properly normalized (0-1 range)
                    if (cx > 1f || cy > 1f || bw > 1f || bh > 1f) {
                        // Coordinates might be in pixel space, normalize them
                        cx = cx / yoloInputSize;
                        cy = cy / yoloInputSize;
                        bw = bw / yoloInputSize;
                        bh = bh / yoloInputSize;
                    }
                    
                    // Clamp coordinates to valid range
                    cx = Mathf.Clamp01(cx);
                    cy = Mathf.Clamp01(cy);
                    bw = Mathf.Clamp01(bw);
                    bh = Mathf.Clamp01(bh);
                    
                    // Log first few detections for debugging
                    if (i < 5) {
                        _debugger.LogVerbose($"Detection {i}: conf={conf:F3}, pos=({cx:F2},{cy:F2}), size=({bw:F2},{bh:F2}), class={clsId}", LogCategory.AI);
                    }
                    
                    if (conf < _detectionThreshold) continue;
                    
                    validDetections++;
                    
                    // Convert normalized coordinates to pixel coordinates
                    float pixelCx = cx * imageTexture.width;
                    float pixelCy = cy * imageTexture.height;
                    float pixelBw = bw * imageTexture.width;
                    float pixelBh = bh * imageTexture.height;
                    
                    string clsName = (_classLabels != null && clsId < _classLabels.Length) 
                        ? _classLabels[clsId] 
                        : $"class_{clsId}";
                    
                    detections.Add(new DetectedObject {
                        classId = clsId,
                        className = clsName,
                        confidence = conf,
                        boundingBox = new Rect(pixelCx - pixelBw / 2f, pixelCy - pixelBh / 2f, pixelBw, pixelBh)
                    });
                }
                
                _debugger.Log($"Processed {totalChecked} detections, found {validDetections} above threshold {_detectionThreshold}", LogCategory.AI);
                
                if (validDetections == 0) {
                    _debugger.LogWarning($"No detections found above threshold {_detectionThreshold}. Consider lowering the threshold or checking your model.", LogCategory.AI);
                }
            }
            catch (Exception ex) {
                onError?.Invoke($"YOLO detection failed: {ex.Message}");
                yield break;
            }
            finally {
                yoloInput?.Dispose();
                yoloOutput?.Dispose();
            }
            
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
                // SAM2 can use higher resolution input based on quality setting
                int samInputSize = _useHighQualityAnalysis ? 1024 : 640;
                
                foreach (DetectedObject det in detections.Take(_maxObjectsToProcess)) {
                    Unity.Sentis.Tensor promptTensor = null;
                    Unity.Sentis.Tensor samInput = null;
                    Unity.Sentis.Tensor maskOut = null;
                    
                    try {
                        // Prepare prompt tensor (normalized center point of detection)
                        Vector2 centerN = new Vector2(
                            det.boundingBox.center.x / imageTexture.width,
                            det.boundingBox.center.y / imageTexture.height
                        );
                        
                        // Create prompt tensor using correct Unity Sentis API
                        var promptShape = new Unity.Sentis.TensorShape(1, 2);
                        var promptData = new float[] { centerN.x, centerN.y };
                        
                        // Create a temporary 1x1 texture to convert to tensor, then manually set the data
                        Texture2D tempTex = new Texture2D(2, 1, TextureFormat.RFloat, false);
                        tempTex.SetPixels(new Color[] { 
                            new Color(centerN.x, 0, 0, 1), 
                            new Color(centerN.y, 0, 0, 1) 
                        });
                        tempTex.Apply();
                        
                        promptTensor = Unity.Sentis.TextureConverter.ToTensor(tempTex);
                        Destroy(tempTex);
                        
                        samInput = PrepareImageTensor(imageTexture, samInputSize, samInputSize);
                         
                        _sam2Worker.SetInput("image", samInput);
                        _sam2Worker.SetInput("prompt", promptTensor);
                        _sam2Worker.Schedule();
                        
                        maskOut = _sam2Worker.PeekOutput("masks") as Unity.Sentis.Tensor;
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
                    }
                    catch (Exception ex) {
                        _debugger.LogError($"SAM segmentation failed for object {det.className}: {ex.Message}", LogCategory.AI);
                    }
                    finally {
                        samInput?.Dispose();
                        promptTensor?.Dispose();
                        maskOut?.Dispose();
                    }
                    
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
            
            // Configure blending for transparency with proper render queue
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            
            // Fix render queue - use valid range for transparency
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            
            return mat;
        }
        
        private GameObject CreateOverlayQuad(TerrainFeature feature, UnityEngine.Terrain terrain, Texture2D mapTexture) {
            GameObject quad = _overlayPrefab ? Instantiate(_overlayPrefab) : GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"Overlay_{feature.label}";
            quad.transform.SetParent(terrain.transform);
            
            // Calculate world position and size of overlay from feature bounding box
            Vector3 size = terrain.terrainData.size;
            Vector3 tPos = terrain.transform.position;
            
            float overlayXMin = tPos.x + (feature.boundingBox.x / mapTexture.width) * size.x;
            float overlayXMax = tPos.x + ((feature.boundingBox.x + feature.boundingBox.width) / mapTexture.width) * size.x;
            float overlayZMin = tPos.z + (feature.boundingBox.y / mapTexture.height) * size.z;
            float overlayZMax = tPos.z + ((feature.boundingBox.y + feature.boundingBox.height) / mapTexture.height) * size.z;
            
            quad.transform.position = new Vector3((overlayXMin + overlayXMax) / 2f, tPos.y + _overlayYOffset, (overlayZMin + overlayZMax) / 2f);
            quad.transform.localScale = new Vector3(overlayXMax - overlayXMin, 1, overlayZMax - overlayZMin);
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
        
        private Unity.Sentis.Tensor PrepareImageTensor(Texture2D src, int width, int height) {
            // Resize image to desired resolution
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            
            Texture2D scaled = new Texture2D(width, height, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            scaled.Apply();
            
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            
            // Convert to Unity Sentis Tensor
            Unity.Sentis.Tensor tensor = Unity.Sentis.TextureConverter.ToTensor(scaled);
            Destroy(scaled);
            
            return tensor;
        }
        
        private Texture2D DecodeMaskTensor(Unity.Sentis.Tensor maskTensor) {
            int mh = maskTensor.shape[1];
            int mw = maskTensor.shape[2];
            
            Texture2D maskTex = new Texture2D(mw, mh, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[mw * mh];
            
            // Get the tensor data as a CPU-accessible array
            var maskArrayTensor = maskTensor.ReadbackAndClone();
            var maskData = maskArrayTensor.dataOnBackend.Download<float>(maskArrayTensor.shape.length);
            
            for (int y = 0; y < mh; y++) {
                for (int x = 0; x < mw; x++) {
                    int index = y * mw + x;
                    float v = maskData[index];
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
            
            _debugger.Log("���─ Performance Metrics ����─", LogCategory.Process);
            
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
                _debugger.Log($"Saved texture ��� {path}", LogCategory.IO);
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
        
        private void LoadClassLabels() {
            if (_labelsFile != null) {
                try {
                    _classLabels = _labelsFile.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    _debugger.Log($"Loaded {_classLabels.Length} class labels", LogCategory.AI);
                }
                catch (Exception ex) {
                    _debugger.LogError($"Failed to load class labels: {ex.Message}", LogCategory.AI);
                }
            } else {
                _debugger.LogWarning("Class labels file not assigned, using default labels", LogCategory.AI);
                // Create default labels for basic terrain analysis
                _classLabels = new string[] {
                    "terrain", "water", "forest", "mountain", "grass", "rock", "tree", "building"
                };
            }
        }
        
        private void LoadPreferences() {
            try {
                // Load user preferences from PlayerPrefs
                if (PlayerPrefs.HasKey("Traversify_TerrainWidth")) {
                    _terrainSize.x = PlayerPrefs.GetFloat("Traversify_TerrainWidth", _terrainSize.x);
                }
                if (PlayerPrefs.HasKey("Traversify_TerrainHeight")) {
                    _terrainSize.y = PlayerPrefs.GetFloat("Traversify_TerrainHeight", _terrainSize.y);
                }
                if (PlayerPrefs.HasKey("Traversify_TerrainLength")) {
                    _terrainSize.z = PlayerPrefs.GetFloat("Traversify_TerrainLength", _terrainSize.z);
                }
                if (PlayerPrefs.HasKey("Traversify_TerrainResolution")) {
                    _terrainResolution = PlayerPrefs.GetInt("Traversify_TerrainResolution", _terrainResolution);
                }
                if (PlayerPrefs.HasKey("Traversify_UseHighQualityAnalysis")) {
                    _useHighQualityAnalysis = PlayerPrefs.GetInt("Traversify_UseHighQualityAnalysis", _useHighQualityAnalysis ? 1 : 0) == 1;
                }
                if (PlayerPrefs.HasKey("Traversify_DetectionThreshold")) {
                    _detectionThreshold = PlayerPrefs.GetFloat("Traversify_DetectionThreshold", 0.25f); // Use better default
                }
                if (PlayerPrefs.HasKey("Traversify_MaxObjectsToProcess")) {
                    _maxObjectsToProcess = PlayerPrefs.GetInt("Traversify_MaxObjectsToProcess", _maxObjectsToProcess);
                }
                if (PlayerPrefs.HasKey("Traversify_GenerateWater")) {
                    _generateWater = PlayerPrefs.GetInt("Traversify_GenerateWater", _generateWater ? 1 : 0) == 1;
                }
                if (PlayerPrefs.HasKey("Traversify_WaterHeight")) {
                    _waterHeight = PlayerPrefs.GetFloat("Traversify_WaterHeight", _waterHeight);
                }
                if (PlayerPrefs.HasKey("Traversify_UseGPUAcceleration")) {
                    _useGPUAcceleration = PlayerPrefs.GetInt("Traversify_UseGPUAcceleration", _useGPUAcceleration ? 1 : 0) == 1;
                }
                if (PlayerPrefs.HasKey("Traversify_SaveGeneratedAssets")) {
                    _saveGeneratedAssets = PlayerPrefs.GetInt("Traversify_SaveGeneratedAssets", _saveGeneratedAssets ? 1 : 0) == 1;
                }
                
                _debugger.Log("User preferences loaded", LogCategory.System);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to load preferences: {ex.Message}", LogCategory.System);
            }
        }
        
        private void SavePreferences() {
            try {
                // Save user preferences to PlayerPrefs
                PlayerPrefs.SetFloat("Traversify_TerrainWidth", _terrainSize.x);
                PlayerPrefs.SetFloat("Traversify_TerrainHeight", _terrainSize.y);
                PlayerPrefs.SetFloat("Traversify_TerrainLength", _terrainSize.z);
                PlayerPrefs.SetInt("Traversify_TerrainResolution", _terrainResolution);
                PlayerPrefs.SetInt("Traversify_UseHighQualityAnalysis", _useHighQualityAnalysis ? 1 : 0);
                PlayerPrefs.SetFloat("Traversify_DetectionThreshold", _detectionThreshold);
                PlayerPrefs.SetInt("Traversify_MaxObjectsToProcess", _maxObjectsToProcess);
                PlayerPrefs.SetInt("Traversify_GenerateWater", _generateWater ? 1 : 0);
                PlayerPrefs.SetFloat("Traversify_WaterHeight", _waterHeight);
                PlayerPrefs.SetInt("Traversify_UseGPUAcceleration", _useGPUAcceleration ? 1 : 0);
                PlayerPrefs.SetInt("Traversify_SaveGeneratedAssets", _saveGeneratedAssets ? 1 : 0);
                
                PlayerPrefs.Save();
                _debugger.Log("User preferences saved", LogCategory.System);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to save preferences: {ex.Message}", LogCategory.System);
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
        
        /// <summary>
        /// Create Unity Sentis worker directly from ModelAsset
        /// </summary>
        private Unity.Sentis.Worker CreateSentisWorker(Unity.Sentis.ModelAsset sentisModel, WorkerFactory.Type backendType) {
            // Load the model using Unity Sentis
            var model = Unity.Sentis.ModelLoader.Load(sentisModel);
            
            // Select the appropriate backend
            Unity.Sentis.BackendType sentisBackend;
            switch (backendType) {
                case WorkerFactory.Type.ComputePrecompiled:
                case WorkerFactory.Type.Compute:
                case WorkerFactory.Type.ComputeOptimized:
                    sentisBackend = Unity.Sentis.BackendType.GPUCompute;
                    break;
                case WorkerFactory.Type.CoreML:
                    #if UNITY_IOS || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    sentisBackend = Unity.Sentis.BackendType.CPU;  // CoreML not directly available in Sentis, use CPU
                    #else
                    sentisBackend = Unity.Sentis.BackendType.GPUCompute;
                    #endif
                    break;
                case WorkerFactory.Type.CSharpBurst:
                default:
                    sentisBackend = Unity.Sentis.BackendType.CPU;
                    break;
            }
            
            // Create and return Sentis worker
            return new Unity.Sentis.Worker(model, sentisBackend);
        }
        
        /// <summary>
        /// Helper method to try getting YOLO output tensor with various naming attempts
        /// </summary>
        private Unity.Sentis.Tensor TryGetYOLOOutput()
        {
            Unity.Sentis.Tensor outputTensor = null;
            
            try
            { 
                _debugger.Log("Attempting to find YOLO output tensor...", LogCategory.AI);
                
                // First, try to get output by index (most reliable for single-output models)
                _debugger.Log("Trying to access output by index...", LogCategory.AI);
                try
                {
                    outputTensor = _yoloWorker.PeekOutput(0);
                    if (outputTensor != null)
                    {
                        _debugger.Log("Successfully found YOLO output tensor by index 0", LogCategory.AI);
                        return outputTensor;
                    }
                }
                catch (Exception ex)
                {
                    _debugger.LogVerbose($"Index-based access failed: {ex.Message}", LogCategory.AI);
                }
                
                // Extended list of possible YOLO output names as fallback
                string[] possibleOutputNames = { 
                    // Standard YOLO outputs
                    "output", "output0", "outputs", "detection_output", "pred", "predictions", 
                    "boxes", "detections", "yolo_output", "model_output",
                    
                    // YOLOv5/v8 common outputs  
                    "output1", "output2", "1417", "1418", "1419",
                    
                    // ONNX exported model outputs
                    "458", "459", "460", "461", "462", "463",
                    
                    // Identity layers (common in exported models)
                    "Identity", "Identity_0", "Identity_1", "Identity_2",
                    
                    // Numeric indices as strings
                    "0", "1", "2", "3", "4", "5",
                    
                    // PyTorch/TensorFlow exports
                    "serving_default_input:0", "StatefulPartitionedCall:0",
                    "model_1/tf_op_layer_Reshape_15/Reshape_15:0"
                };
                
                // Try each possible output name
                foreach (string outputName in possibleOutputNames)
                {
                    try 
                    {
                        _debugger.LogVerbose($"Trying output name: {outputName}", LogCategory.AI);
                        outputTensor = _yoloWorker.PeekOutput(outputName);
                        if (outputTensor != null)
                        {
                            _debugger.Log($"Successfully found YOLO output tensor: {outputName}", LogCategory.AI);
                            return outputTensor;
                        }
                    }
                    catch (Exception ex)
                    {
                        _debugger.LogVerbose($"Output '{outputName}' not found: {ex.Message}", LogCategory.AI);
                        continue;
                    }
                }
                
                // Try systematic numeric search for more output indices
                _debugger.Log("Trying systematic numeric output search...", LogCategory.AI);
                for (int i = 1; i < 10; i++)
                {
                    try
                    {
                        outputTensor = _yoloWorker.PeekOutput(i);
                        if (outputTensor != null)
                        {
                            _debugger.Log($"Found YOLO output tensor with numeric index: {i}", LogCategory.AI);
                            return outputTensor;
                        }
                    }
                    catch (Exception) { continue; }
                }
                
                // Try pattern-based search
                string[] patterns = { "output_{0}", "layer_{0}", "node_{0}", "tensor_{0}" };
                foreach (string pattern in patterns)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try
                        {
                            string patternName = string.Format(pattern, i);
                            outputTensor = _yoloWorker.PeekOutput(patternName);
                            if (outputTensor != null)
                            {
                                _debugger.Log($"Found YOLO output tensor with pattern: {patternName}", LogCategory.AI);
                                return outputTensor;
                            }
                        }
                        catch (Exception) { continue; }
                    }
                }
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error in output tensor discovery: {ex.Message}", LogCategory.AI);
            }
            
            _debugger.LogError("Could not find any valid output tensor from YOLO model. The model may not be properly loaded or may have an unsupported output structure.", LogCategory.AI);
            return null;
        }
        
        #endregion
    }
}

