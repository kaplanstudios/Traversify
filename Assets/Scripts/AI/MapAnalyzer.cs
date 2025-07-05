/*************************************************************************
 *  Traversify – MapAnalyzer.cs                                          *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Updated: 2025-07-05                                                  *
 *  Desc   : Advanced map analyzer that uses AI models to detect         *
 *           objects, recognize terrain, analyze height data,            *
 *           and produce detailed segmentation results for               *
 *           environment generation.                                     *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Unity.Sentis;
using TensorFloat = Unity.Sentis.Tensor;
using SentisModelAsset = Unity.Sentis.ModelAsset;
using Traversify.Core;
using System.Text;

namespace Traversify.AI {
    /// <summary>
    /// Advanced map analyzer that uses YOLOv12, SAM2, and Faster R-CNN with OpenAI 
    /// for enhanced object detection, segmentation, and contextual description.
    /// </summary>
    [RequireComponent(typeof(TraversifyDebugger))]
    public class MapAnalyzer : TraversifyComponent {
        #region Singleton Pattern
        
        private static MapAnalyzer _instance;
        
        /// <summary>
        /// Singleton instance of the MapAnalyzer.
        /// </summary>
        public static MapAnalyzer Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<MapAnalyzer>();
                    if (_instance == null) {
                        GameObject go = new GameObject("MapAnalyzer");
                        _instance = go.AddComponent<MapAnalyzer>();
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Fields
        
        [Header("Detection Settings")]
        [Tooltip("Confidence threshold for object detection (0-1)")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _confidenceThreshold = 0.5f;
        
        [Tooltip("Non-maximum suppression threshold (0-1)")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _nmsThreshold = 0.45f;
        
        [Tooltip("Use high quality models and processing")]
        [SerializeField] private bool _useHighQuality = true;
        
        [Tooltip("Use GPU acceleration when available")]
        [SerializeField] private bool _useGPU = true;
        
        [Tooltip("Maximum number of objects to process")]
        [Range(1, 1000)]
        [SerializeField] private int _maxObjectsToProcess = 100;
        
        [Header("API Settings")]
        [Tooltip("OpenAI API Key for enhanced descriptions")]
        [SerializeField] private string _openAIApiKey = "";
        
        [Header("Model Paths")]
        [Tooltip("Path to YOLO model file")]
        [SerializeField] private string _yoloModelPath = "Assets/Scripts/AI/Models/yolov8n.onnx";
        
        [Tooltip("Path to SAM2 model file")]
        [SerializeField] private string _sam2ModelPath = "Assets/Scripts/AI/Models/sam2_hiera_base.onnx";
        
        [Tooltip("Path to Faster R-CNN model file")]
        [SerializeField] private string _fasterRCNNModelPath = "Assets/Scripts/AI/Models/FasterRCNN-12.onnx";
        
        [Tooltip("Path to labels file")]
        [SerializeField] private string _labelsPath = "Assets/Scripts/AI/Models/coco_labels.txt";
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Gets or sets the confidence threshold for object detection.
        /// </summary>
        public float confidenceThreshold {
            get => _confidenceThreshold;
            set => _confidenceThreshold = Mathf.Clamp01(value);
        }
        
        /// <summary>
        /// Gets or sets the non-maximum suppression threshold.
        /// </summary>
        public float nmsThreshold {
            get => _nmsThreshold;
            set => _nmsThreshold = Mathf.Clamp01(value);
        }
        
        /// <summary>
        /// Gets or sets whether to use high quality models and processing.
        /// </summary>
        public bool useHighQuality {
            get => _useHighQuality;
            set => _useHighQuality = value;
        }
        
        /// <summary>
        /// Gets or sets whether to use GPU acceleration.
        /// </summary>
        public bool useGPU {
            get => _useGPU;
            set => _useGPU = value;
        }
        
        /// <summary>
        /// Gets or sets the maximum number of objects to process.
        /// </summary>
        public int maxObjectsToProcess {
            get => _maxObjectsToProcess;
            set => _maxObjectsToProcess = Mathf.Max(1, value);
        }
        
        /// <summary>
        /// Gets or sets the OpenAI API key.
        /// </summary>
        public string openAIApiKey {
            get => _openAIApiKey;
            set => _openAIApiKey = value;
        }
        
        #endregion
        
        #region Private Fields
        
        private TraversifyDebugger _debugger;
        private bool _isInitialized = false;
        private bool _isModelLoaded = false;
        private bool _isProcessing = false;
        private CancellationTokenSource _cancellationTokenSource;
        
        // AI Models
        private SentisModelAsset _yoloModelAsset;
        private IWorker _yoloWorker;
        
        private SentisModelAsset _sam2ModelAsset;
        private IWorker _sam2Worker;
        
        private SentisModelAsset _fasterRCNNModelAsset;
        private IWorker _fasterRCNNWorker;
        
        // Class labels
        private string[] _classLabels;
        
        // Analysis state
        private Texture2D _currentImage;
        private List<DetectedObject> _detectedObjects = new List<DetectedObject>();
        private List<DetectedObject> _terrainObjects = new List<DetectedObject>();
        private List<DetectedObject> _manMadeObjects = new List<DetectedObject>();
        private float[,] _heightMap;
        private Dictionary<string, float> _performanceMetrics = new Dictionary<string, float>();
        private float _lastImageWidth;
        private float _lastImageHeight;
        
        // Model constants
        private readonly int _yoloInputSize = 640;
        private readonly int _sam2InputSize = 1024;
        private readonly int _fasterRCNNInputSize = 800;
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Class representing an object detected in the map.
        /// </summary>
        [System.Serializable]
        public class DetectedObject {
            public string className;
            public float confidence;
            public Rect boundingBox;
            public Vector2 centroid;
            public List<ImageSegment> segments = new List<ImageSegment>();
            public string shortDescription;
            public string enhancedDescription;
            public Dictionary<string, object> metadata = new Dictionary<string, object>();
            public bool isManMade = false;
            public bool isTerrain = false;
            public float estimatedHeight = 1f;
            public float estimatedWidth = 1f;
            public List<string> estimatedMaterials = new List<string>();
            public Color color = Color.white;
        }
        
        /// <summary>
        /// Class representing a segment in the image.
        /// </summary>
        [System.Serializable]
        public class ImageSegment {
            public Texture2D maskTexture;
            public Vector2 centroid;
            public float area;
            public List<Vector2> contourPoints = new List<Vector2>();
            public string segmentType;
            public Dictionary<string, object> metadata = new Dictionary<string, object>();
        }
        
        /// <summary>
        /// Class representing the results of the analysis.
        /// </summary>
        [System.Serializable]
        public class AnalysisResults {
            public Texture2D sourceImage;
            public List<DetectedObject> detectedObjects = new List<DetectedObject>();
            public List<DetectedObject> terrainObjects = new List<DetectedObject>();
            public List<DetectedObject> manMadeObjects = new List<DetectedObject>();
            public Texture2D heightMap;
            public Texture2D segmentationOverlay;
            public Dictionary<string, float> performanceMetrics = new Dictionary<string, float>();
            public List<string> detectedTerrainTypes = new List<string>();
            public List<MapObject> mapObjects = new List<MapObject>();
            public List<TerrainFeature> terrainFeatures = new List<TerrainFeature>();
            public DateTime analysisTimestamp;
            public string mapName;
        }
        
        /// <summary>
        /// Class representing a terrain feature.
        /// </summary>
        [System.Serializable]
        public class TerrainFeature {
            public string featureType;
            public Rect bounds;
            public float elevation;
            public List<Vector2> contourPoints = new List<Vector2>();
            public string description;
            public Dictionary<string, object> metadata = new Dictionary<string, object>();
        }
        
        /// <summary>
        /// Class representing a map object.
        /// </summary>
        [System.Serializable]
        public class MapObject {
            public string objectType;
            public string objectName;
            public Vector2 position;
            public float rotation;
            public Vector2 scale;
            public string description;
            public float estimatedHeight;
            public List<string> materials = new List<string>();
            public Dictionary<string, object> metadata = new Dictionary<string, object>();
        }
        
        #endregion
        
        #region Unity Lifecycle & Initialization
        
        protected override bool OnInitialize(object config) {
            try {
                _debugger = GetComponent<TraversifyDebugger>();
                if (_debugger == null) {
                    _debugger = gameObject.AddComponent<TraversifyDebugger>();
                }
                
                InitializeModels();
                
                Log("MapAnalyzer initialized successfully", LogCategory.System);
                _isInitialized = true;
                return true;
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to initialize MapAnalyzer: {ex.Message}");
                if (_debugger != null) {
                    LogError($"Failed to initialize MapAnalyzer: {ex.Message}", LogCategory.System);
                }
                _isInitialized = false;
                return false;
            }
        }
        
        private void Awake() {
            if (_instance == null) {
                _instance = this;
                if (transform.root == gameObject) {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this) {
                Destroy(gameObject);
                return;
            }
            
            if (!_isInitialized) {
                Initialize();
            }
        }
        
        private void OnDestroy() {
            CleanupResources();
        }
        
        /// <summary>
        /// Initialize AI models needed for analysis.
        /// </summary>
        private void InitializeModels() {
            try {
                // Attempt to load models
                bool yoloLoaded = LoadYOLOModel();
                bool sam2Loaded = LoadSAM2Model();
                bool fasterRCNNLoaded = LoadFasterRCNNModel();
                
                // Load class labels
                LoadClassLabels();
                
                // Set initialization status
                _isModelLoaded = yoloLoaded && (sam2Loaded || !_useHighQuality) && (fasterRCNNLoaded || !_useHighQuality);
                
                if (_isModelLoaded) {
                    Log("All AI models initialized successfully", LogCategory.AI);
                }
                else {
                    LogWarning("Some AI models failed to initialize", LogCategory.AI);
                }
            }
            catch (Exception ex) {
                LogError($"Failed to initialize models: {ex.Message}", LogCategory.AI);
                _isModelLoaded = false;
            }
        }
        
        /// <summary>
        /// Initialize models if they haven't been loaded yet.
        /// </summary>
        public void InitializeModelsIfNeeded() {
            if (!_isModelLoaded) {
                InitializeModels();
            }
        }
        
        #endregion
        
        #region Model Loading
        
        /// <summary>
        /// Load the YOLO object detection model.
        /// </summary>
        private bool LoadYOLOModel() {
            try {
                // Check if model file exists
                if (!File.Exists(_yoloModelPath) && Application.isEditor) {
                    _yoloModelPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", "yolov8n.onnx");
                }
                if (!File.Exists(_yoloModelPath)) {
                    LogError($"YOLO model file not found at: {_yoloModelPath}", LogCategory.AI);
                    return false;
                }
                // Load Sentis model asset
                _yoloModelAsset = SentisModelAsset.Load(_yoloModelPath);
                if (_yoloModelAsset == null) {
                    LogError("Failed to load YOLO Sentis model asset", LogCategory.AI);
                    return false;
                }
                // Create Sentis worker
                var yoloBackend = _useGPU ? Unity.Sentis.BackendType.GpuCompute : Unity.Sentis.BackendType.Cpu;
                _yoloWorker = new SentisWorker(_yoloModelAsset, yoloBackend);
                Log($"YOLO model loaded with Sentis {yoloBackend} backend", LogCategory.AI);
                return true;
            }
            catch (Exception ex) {
                LogError($"Failed to load YOLO model: {ex.Message}", LogCategory.AI);
                return false;
            }
        }
        
        /// <summary>
        /// Load the SAM2 segmentation model.
        /// </summary>
        private bool LoadSAM2Model() {
            if (!_useHighQuality) {
                Log("High quality segmentation disabled, skipping SAM2 model load", LogCategory.AI);
                return true;
            }
            try {
                // Check if model file exists
                if (!File.Exists(_sam2ModelPath) && Application.isEditor) {
                    _sam2ModelPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", "sam2_hiera_base.onnx");
                }
                if (!File.Exists(_sam2ModelPath)) {
                    LogWarning($"SAM2 model file not found at: {_sam2ModelPath}", LogCategory.AI);
                    return false;
                }
                // Load Sentis segmentation model asset
                _sam2ModelAsset = SentisModelAsset.Load(_sam2ModelPath);
                if (_sam2ModelAsset == null) {
                    LogWarning("Failed to load SAM2 Sentis model asset", LogCategory.AI);
                    return false;
                }
                // Create Sentis worker
                var sam2Backend = _useGPU ? Unity.Sentis.BackendType.GpuCompute : Unity.Sentis.BackendType.Cpu;
                _sam2Worker = new SentisWorker(_sam2ModelAsset, sam2Backend);
                Log($"SAM2 model loaded with Sentis {sam2Backend} backend", LogCategory.AI);
                return true;
            }
            catch (Exception ex) {
                LogWarning($"Failed to load SAM2 model: {ex.Message}", LogCategory.AI);
                return false;
            }
        }
        
        /// <summary>
        /// Load the Faster R-CNN classification model.
        /// </summary>
        private bool LoadFasterRCNNModel() {
            if (!_useHighQuality) {
                Log("High quality classification disabled, skipping Faster R-CNN model load", LogCategory.AI);
                return true;
            }
            try {
                // Check if model file exists
                if (!File.Exists(_fasterRCNNModelPath) && Application.isEditor) {
                    _fasterRCNNModelPath = Path.Combine(Application.dataPath, "Scripts", "AI", "Models", "FasterRCNN-12.onnx");
                }
                if (!File.Exists(_fasterRCNNModelPath)) {
                    LogWarning($"Faster R-CNN model file not found at: {_fasterRCNNModelPath}", LogCategory.AI);
                    return false;
                }
                // Load Sentis classification model asset
                _fasterRCNNModelAsset = SentisModelAsset.Load(_fasterRCNNModelPath);
                if (_fasterRCNNModelAsset == null) {
                    LogWarning("Failed to load Faster R-CNN Sentis model asset", LogCategory.AI);
                    return false;
                }
                // Create Sentis worker
                var frcnnBackend = _useGPU ? Unity.Sentis.BackendType.GpuCompute : Unity.Sentis.BackendType.Cpu;
                _fasterRCNNWorker = new SentisWorker(_fasterRCNNModelAsset, frcnnBackend);
                Log($"Faster R-CNN model loaded with Sentis {frcnnBackend} backend", LogCategory.AI);
                return true;
            }
            catch (Exception ex) {
                LogWarning($"Failed to load Faster R-CNN model: {ex.Message}", LogCategory.AI);
                return false;
            }
        }
        
        /// <summary>
        /// Load class labels from file or use defaults.
        /// </summary>
        private void LoadClassLabels() {
            try {
                // Check if labels file exists
                if (File.Exists(_labelsPath)) {
                    string[] lines = File.ReadAllLines(_labelsPath);
                    _classLabels = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
                    Log($"Loaded {_classLabels.Length} class labels from {_labelsPath}", LogCategory.AI);
                }
                else {
                    // Use default COCO labels
                    _classLabels = GetDefaultCOCOLabels();
                    LogWarning("Labels file not found, using default COCO labels", LogCategory.AI);
                }
            }
            catch (Exception ex) {
                LogError($"Failed to load class labels: {ex.Message}", LogCategory.AI);
                _classLabels = GetDefaultCOCOLabels();
            }
        }
        
        /// <summary>
        /// Get default COCO labels.
        /// </summary>
        private string[] GetDefaultCOCOLabels() {
            return new string[] {
                "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
                "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
                "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
                "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard",
                "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
                "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
                "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone",
                "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors", "teddy bear",
                "hair drier", "toothbrush", "tree", "grass", "water", "mountain", "building", "road", "forest", "river",
                "lake", "bridge", "hill", "field", "wall", "fence", "rock", "house", "tower", "castle", "temple"
            };
        }
        
        #endregion
        #region Map Analysis Pipeline
        
        /// <summary>
        /// Analyze a map image and return detailed results.
        /// </summary>
        public IEnumerator AnalyzeMapImage(Texture2D mapTexture, 
                                          System.Action<AnalysisResults> onComplete = null, 
                                          System.Action<string> onError = null, 
                                          System.Action<string, float> onProgress = null) {
            if (!_isInitialized) {
                string errorMessage = "MapAnalyzer not initialized";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                yield break;
            }
            
            if (_isProcessing) {
                string errorMessage = "Map analysis already in progress";
                LogWarning(errorMessage, LogCategory.Process);
                onError?.Invoke(errorMessage);
                yield break;
            }
            
            if (mapTexture == null) {
                string errorMessage = "Map texture is null";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                yield break;
            }
            
            try {
                // Initialize resources
                _isProcessing = true;
                _performanceMetrics.Clear();
                _detectedObjects.Clear();
                _terrainObjects.Clear();
                _manMadeObjects.Clear();
                _currentImage = mapTexture;
                _lastImageWidth = mapTexture.width;
                _lastImageHeight = mapTexture.height;
                
                // Create cancellation token
                if (_cancellationTokenSource != null) {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Track analysis start time
                float startTime = Time.realtimeSinceStartup;
                
                // 1. Preprocess image
                onProgress?.Invoke("Preprocessing image...", 0.05f);
                Texture2D processedImage = PreprocessImage(mapTexture);
                yield return new WaitForEndOfFrame();
                
                // 2. Run YOLO detection
                onProgress?.Invoke("Detecting objects...", 0.2f);
                yield return StartCoroutine(RunYOLODetection(processedImage));
                if (_cancellationTokenSource.IsCancellationRequested) {
                    throw new OperationCanceledException("Analysis was cancelled");
                }
                
                // 3. Run segmentation on detected objects
                if (_useHighQuality && _sam2Worker != null) {
                    onProgress?.Invoke("Segmenting objects...", 0.4f);
                    yield return StartCoroutine(RunSAM2Segmentation(processedImage, _detectedObjects));
                    if (_cancellationTokenSource.IsCancellationRequested) {
                        throw new OperationCanceledException("Analysis was cancelled");
                    }
                }
                
                // 4. Run classification on detected objects
                if (_useHighQuality && _fasterRCNNWorker != null) {
                    onProgress?.Invoke("Classifying objects...", 0.6f);
                    yield return StartCoroutine(RunFasterRCNNClassification(processedImage, _detectedObjects));
                    if (_cancellationTokenSource.IsCancellationRequested) {
                        throw new OperationCanceledException("Analysis was cancelled");
                    }
                }
                
                // 5. Generate basic descriptions
                onProgress?.Invoke("Generating object descriptions...", 0.7f);
                GenerateShortDescriptions();
                
                // 6. Enhance descriptions with OpenAI
                if (!string.IsNullOrEmpty(_openAIApiKey)) {
                    onProgress?.Invoke("Enhancing descriptions with AI...", 0.8f);
                    yield return StartCoroutine(EnhanceDescriptionsWithOpenAI());
                    if (_cancellationTokenSource.IsCancellationRequested) {
                        throw new OperationCanceledException("Analysis was cancelled");
                    }
                }
                
                // 7. Generate height estimation data
                onProgress?.Invoke("Estimating terrain heights...", 0.9f);
                GenerateHeightEstimationData(mapTexture);
                
                // 8. Build and return analysis results
                onProgress?.Invoke("Finalizing analysis results...", 0.95f);
                AnalysisResults results = BuildAnalysisResults(mapTexture);
                
                // Track total analysis time
                float totalTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["TotalAnalysisTime"] = totalTime;
                
                // Add performance metrics to results
                results.performanceMetrics = new Dictionary<string, float>(_performanceMetrics);
                
                // Log analysis summary
                Log($"Map analysis completed in {totalTime:F2} seconds. " +
                    $"Detected {results.detectedObjects.Count} objects, " +
                    $"{results.terrainObjects.Count} terrain features, " +
                    $"{results.manMadeObjects.Count} man-made objects.", 
                    LogCategory.AI);
                
                onProgress?.Invoke("Analysis complete", 1.0f);
                onComplete?.Invoke(results);
            }
            catch (OperationCanceledException) {
                LogWarning("Map analysis was cancelled", LogCategory.Process);
                onError?.Invoke("Analysis was cancelled");
            }
            catch (Exception ex) {
                string errorMessage = $"Error during map analysis: {ex.Message}";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
            }
            finally {
                // Clean up resources
                _isProcessing = false;
            }
        }
        
        /// <summary>
        /// Alternative method name to match conventions in some code.
        /// </summary>
        public IEnumerator AnalyzeImage(Texture2D mapTexture, 
                                       System.Action<AnalysisResults> onComplete = null, 
                                       System.Action<string> onError = null, 
                                       System.Action<string, float> onProgress = null) {
            return AnalyzeMapImage(mapTexture, onComplete, onError, onProgress);
        }
        
        /// <summary>
        /// Preprocess the input image for analysis.
        /// </summary>
        private Texture2D PreprocessImage(Texture2D input) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Create a copy of the texture so we don't modify the original
                Texture2D processed = new Texture2D(input.width, input.height, TextureFormat.RGBA32, false);
                processed.SetPixels(input.GetPixels());
                processed.Apply();
                
                // Image enhancement
                Color[] pixels = processed.GetPixels();
                
                // Apply contrast enhancement and noise reduction
                for (int i = 0; i < pixels.Length; i++) {
                    // Simple contrast enhancement
                    pixels[i].r = Mathf.Clamp01((pixels[i].r - 0.5f) * 1.2f + 0.5f);
                    pixels[i].g = Mathf.Clamp01((pixels[i].g - 0.5f) * 1.2f + 0.5f);
                    pixels[i].b = Mathf.Clamp01((pixels[i].b - 0.5f) * 1.2f + 0.5f);
                }
                
                processed.SetPixels(pixels);
                processed.Apply();
                
                float processingTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["PreprocessingTime"] = processingTime;
                
                return processed;
            }
            catch (Exception ex) {
                LogError($"Error preprocessing image: {ex.Message}", LogCategory.AI);
                return input; // Return original on error
            }
        }
        
        /// <summary>
        /// Run YOLO object detection on the image.
        /// </summary>
        private IEnumerator RunYOLODetection(Texture2D image) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Initialize list of detected objects
                _detectedObjects.Clear();
                
                // Check if YOLO model is available
                if (_yoloWorker == null) {
                    LogError("YOLO model not loaded", LogCategory.AI);
                    yield break;
                }
                
                // Prepare input tensor
                Tensor inputTensor = PrepareImageTensorForYOLO(image);
                
                // Execute YOLO model
                _yoloWorker.Execute(new Dictionary<string, object>{{"input", inputTensor}});
                TensorFloat outputTensor = _yoloWorker.PeekOutput("output") as TensorFloat;
                
                if (outputTensor == null) {
                    LogError("YOLO inference failed to produce output", LogCategory.AI);
                    yield break;
                }
                
                // Decode YOLO output
                List<DetectedObject> detections = DecodeYOLOOutput(outputTensor, image.width, image.height);
                
                // Apply non-maximum suppression
                detections = ApplyNonMaximumSuppression(detections);
                
                // Limit number of objects to process
                if (detections.Count > _maxObjectsToProcess) {
                    detections = detections.OrderByDescending(obj => obj.confidence).Take(_maxObjectsToProcess).ToList();
                }
                
                // Store results
                _detectedObjects = detections;
                
                // Categorize objects
                foreach (var obj in _detectedObjects) {
                    if (IsTerrainObject(obj.className)) {
                        obj.isTerrain = true;
                        _terrainObjects.Add(obj);
                    }
                    else if (IsManMadeObject(obj.className)) {
                        obj.isManMade = true;
                        _manMadeObjects.Add(obj);
                    }
                }
                
                float detectionTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["YOLODetectionTime"] = detectionTime;
                
                // Clean up resources
                inputTensor.Dispose();
                
                Log($"YOLO detection completed in {detectionTime:F2} seconds. " +
                    $"Found {_detectedObjects.Count} objects " +
                    $"({_terrainObjects.Count} terrain, {_manMadeObjects.Count} man-made).", 
                    LogCategory.AI);
            }
            catch (Exception ex) {
                LogError($"Error during YOLO detection: {ex.Message}", LogCategory.AI);
            }
            
            yield return null;
        }
        
        /// <summary>
        /// Prepare image tensor for YOLO processing.
        /// </summary>
        private TensorFloat PrepareImageTensorForYOLO(Texture2D image) {
            // Resize image to YOLO input size
            Texture2D resizedImage = ResizeTexture(image, _yoloInputSize, _yoloInputSize);
            
            // Create tensor with shape [1, 3, inputSize, inputSize]
            TensorFloat tensor = new TensorFloat(new long[]{1,3,_yoloInputSize,_yoloInputSize});
            
            // Fill tensor with normalized pixel values
            float[] rgbValues = new float[_yoloInputSize * _yoloInputSize * 3];
            Color[] pixels = resizedImage.GetPixels();
            
            for (int i = 0; i < pixels.Length; i++) {
                // YOLO expects RGB normalized to [0,1]
                rgbValues[i * 3] = pixels[i].r;
                rgbValues[i * 3 + 1] = pixels[i].g;
                rgbValues[i * 3 + 2] = pixels[i].b;
            }
            
            // Convert from HWC to CHW format (pixel interleaved to channel planar)
            float[] chwValues = new float[_yoloInputSize * _yoloInputSize * 3];
            for (int c = 0; c < 3; c++) {
                for (int h = 0; h < _yoloInputSize; h++) {
                    for (int w = 0; w < _yoloInputSize; w++) {
                        int inputIdx = (h * _yoloInputSize + w) * 3 + c;
                        int outputIdx = c * _yoloInputSize * _yoloInputSize + h * _yoloInputSize + w;
                        chwValues[outputIdx] = rgbValues[inputIdx];
                    }
                }
            }
            
            tensor.data.Fill(chwValues);
            
            // Clean up temporary texture
            if (resizedImage != image) {
                Destroy(resizedImage);
            }
            
            return tensor;
        }
        
        /// <summary>
        /// Decode YOLO output tensor into detected objects.
        /// </summary>
        private List<DetectedObject> DecodeYOLOOutput(TensorFloat output, int imageWidth, int imageHeight) {
            List<DetectedObject> detectedObjects = new List<DetectedObject>();
            
            // YOLO v8 output format: [batch, boxes, 85] where 85 = 4 (box) + 1 (confidence) + 80 (class scores)
            // Box format: [cx, cy, w, h, conf, class1, class2, ...]
            int boxesCount = output.shape[1];
            int dimensions = output.shape[2];
            int numClasses = dimensions - 5;
            
            // Limit class count to available labels
            numClasses = Mathf.Min(numClasses, _classLabels.Length);
            
            // Process each detection box
            for (int i = 0; i < boxesCount; i++) {
                // Get confidence
                float confidence = output[0, i, 4];
                
                // Skip low-confidence detections
                if (confidence < _confidenceThreshold) continue;
                
                // Find class with highest score
                int bestClassIndex = -1;
                float bestClassScore = 0;
                
                for (int c = 0; c < numClasses; c++) {
                    float classScore = output[0, i, 5 + c];
                    if (classScore > bestClassScore) {
                        bestClassScore = classScore;
                        bestClassIndex = c;
                    }
                }
                
                // Skip if no good class found
                if (bestClassIndex < 0) continue;
                
                // Final confidence is object confidence * class confidence
                float finalConfidence = confidence * bestClassScore;
                if (finalConfidence < _confidenceThreshold) continue;
                
                // Get box coordinates (centered format)
                float centerX = output[0, i, 0];
                float centerY = output[0, i, 1];
                float width = output[0, i, 2];
                float height = output[0, i, 3];
                
                // Convert to corner format and scale to image dimensions
                float x = (centerX - width / 2) * imageWidth / _yoloInputSize;
                float y = (centerY - height / 2) * imageHeight / _yoloInputSize;
                float w = width * imageWidth / _yoloInputSize;
                float h = height * imageHeight / _yoloInputSize;
                
                // Create detection object
                DetectedObject detection = new DetectedObject {
                    className = GetClassName(bestClassIndex),
                    confidence = finalConfidence,
                    boundingBox = new Rect(x, y, w, h),
                    centroid = new Vector2(centerX * imageWidth / _yoloInputSize, 
                                         centerY * imageHeight / _yoloInputSize)
                };
                
                detectedObjects.Add(detection);
            }
            
            return detectedObjects;
        }
        
        /// <summary>
        /// Apply non-maximum suppression to remove overlapping detections.
        /// </summary>
        private List<DetectedObject> ApplyNonMaximumSuppression(List<DetectedObject> detections) {
            // Sort by confidence (descending)
            var sortedDetections = detections.OrderByDescending(d => d.confidence).ToList();
            var selectedDetections = new List<DetectedObject>();
            
            while (sortedDetections.Count > 0) {
                // Pick the highest confidence detection
                var currentDetection = sortedDetections[0];
                selectedDetections.Add(currentDetection);
                sortedDetections.RemoveAt(0);
                
                // Remove detections that overlap too much with the current one
                sortedDetections.RemoveAll(d => 
                    CalculateIoU(currentDetection.boundingBox, d.boundingBox) > _nmsThreshold);
            }
            
            return selectedDetections;
        }
        
        /// <summary>
        /// Calculate Intersection over Union for two bounding boxes.
        /// </summary>
        private float CalculateIoU(Rect box1, Rect box2) {
            // Calculate intersection area
            float x1 = Mathf.Max(box1.x, box2.x);
            float y1 = Mathf.Max(box1.y, box2.y);
            float x2 = Mathf.Min(box1.x + box1.width, box2.x + box2.width);
            float y2 = Mathf.Min(box1.y + box1.height, box2.y + box2.height);
            
            if (x2 < x1 || y2 < y1) return 0; // No intersection
            
            float intersectionArea = (x2 - x1) * (y2 - y1);
            
            // Calculate union area
            float box1Area = box1.width * box1.height;
            float box2Area = box2.width * box2.height;
            float unionArea = box1Area + box2Area - intersectionArea;
            
            return intersectionArea / unionArea;
        }
        
        #endregion
        #region Segmentation and Classification
        
        /// <summary>
        /// Run SAM2 segmentation on detected objects.
        /// </summary>
        private IEnumerator RunSAM2Segmentation(Texture2D image, List<DetectedObject> detections) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Check if SAM2 model is available
                if (_sam2Worker == null) {
                    LogWarning("SAM2 model not loaded, skipping segmentation", LogCategory.AI);
                    yield break;
                }
                
                // Process each detected object
                int processedCount = 0;
                foreach (var detection in detections) {
                    // Break processing if cancelled
                    if (_cancellationTokenSource.IsCancellationRequested) break;
                    
                    // Process the detection
                    ImageSegment segment = RunSAM2ForPoint(image, detection.centroid, detection);
                    if (segment != null) {
                        detection.segments.Add(segment);
                    }
                    
                    // Yield periodically to prevent frame drops
                    processedCount++;
                    if (processedCount % 5 == 0) {
                        yield return null;
                    }
                }
                
                float segmentationTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["SAM2SegmentationTime"] = segmentationTime;
                
                Log($"SAM2 segmentation completed in {segmentationTime:F2} seconds for {processedCount} objects", 
                    LogCategory.AI);
            }
            catch (Exception ex) {
                LogError($"Error during SAM2 segmentation: {ex.Message}", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Run SAM2 segmentation for a specific point in the image.
        /// </summary>
        private ImageSegment RunSAM2ForPoint(Texture2D image, Vector2 point, DetectedObject detection) {
            try {
                // Resize image to SAM2 input size
                Texture2D resizedImage = ResizeTexture(image, _sam2InputSize, _sam2InputSize);
                
                // Scale point to resized image coordinates
                Vector2 scaledPoint = new Vector2(
                    point.x * _sam2InputSize / image.width,
                    point.y * _sam2InputSize / image.height
                );
                
                // Create input tensors
                TensorFloat imageTensor = PrepareImageTensorForSAM2(resizedImage);
                TensorFloat pointTensor = new TensorFloat(new long[] { 1, 1, 2 });
                pointTensor[0, 0, 0] = scaledPoint.x / _sam2InputSize;
                pointTensor[0, 0, 1] = scaledPoint.y / _sam2InputSize;
                
                // Execute SAM2 model
                _sam2Worker.SetInput("image", imageTensor);
                _sam2Worker.SetInput("point", pointTensor);
                _sam2Worker.Execute();
                
                // Get output mask
                TensorFloat maskTensor = _sam2Worker.PeekOutput("mask") as TensorFloat;
                
                if (maskTensor == null) {
                    LogWarning("SAM2 inference failed to produce mask output", LogCategory.AI);
                    return null;
                }
                
                // Convert mask tensor to texture
                Texture2D maskTexture = ConvertMaskTensorToTexture(maskTensor, _sam2InputSize, _sam2InputSize);
                
                // Resize mask to original image size
                Texture2D resizedMask = ResizeTexture(maskTexture, image.width, image.height);
                
                // Calculate mask centroid and area
                Vector2 centroid = CalculateMaskCentroid(maskTensor);
                float area = CalculateMaskArea(maskTensor);
                
                // Extract contour points
                List<Vector2> contourPoints = ExtractContourPoints(resizedMask, 0.5f);
                
                // Create segment
                ImageSegment segment = new ImageSegment {
                    maskTexture = resizedMask,
                    centroid = new Vector2(
                        centroid.x * image.width / _sam2InputSize,
                        centroid.y * image.height / _sam2InputSize
                    ),
                    area = area * (image.width * image.height) / (_sam2InputSize * _sam2InputSize),
                    contourPoints = contourPoints,
                    segmentType = detection.className
                };
                
                // Clean up resources
                imageTensor.Dispose();
                pointTensor.Dispose();
                Destroy(resizedImage);
                Destroy(maskTexture);
                
                return segment;
            }
            catch (Exception ex) {
                LogError($"Error in SAM2 segmentation for point: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Extract contour points from a mask texture.
        /// </summary>
        private List<Vector2> ExtractContourPoints(Texture2D maskTexture, float threshold = 0.5f) {
            List<Vector2> contourPoints = new List<Vector2>();
            
            try {
                Color[] pixels = maskTexture.GetPixels();
                int width = maskTexture.width;
                int height = maskTexture.height;
                
                // Detect edge pixels
                for (int y = 1; y < height - 1; y++) {
                    for (int x = 1; x < width - 1; x++) {
                        int idx = y * width + x;
                        float val = pixels[idx].r;
                        
                        if (val > threshold) {
                            // Check 4-neighbors
                            float left = pixels[idx - 1].r;
                            float right = pixels[idx + 1].r;
                            float up = pixels[(y - 1) * width + x].r;
                            float down = pixels[(y + 1) * width + x].r;
                            
                            // If any neighbor is below threshold, this is an edge
                            if (left <= threshold || right <= threshold || up <= threshold || down <= threshold) {
                                contourPoints.Add(new Vector2(x, y));
                            }
                        }
                    }
                }
                
                // Simplify contour if too many points
                if (contourPoints.Count > 100) {
                    contourPoints = SimplifyContour(contourPoints, contourPoints.Count / 100);
                }
            }
            catch (Exception ex) {
                LogError($"Error extracting contour points: {ex.Message}", LogCategory.AI);
            }
            
            return contourPoints;
        }
        
        /// <summary>
        /// Simplify a contour by keeping only a subset of points.
        /// </summary>
        private List<Vector2> SimplifyContour(List<Vector2> points, int targetCount) {
            if (points.Count <= targetCount) return points;
            
            List<Vector2> simplified = new List<Vector2>();
            float step = (float)points.Count / targetCount;
            
            for (float i = 0; i < points.Count; i += step) {
                simplified.Add(points[Mathf.FloorToInt(i)]);
            }
            
            return simplified;
        }
        
        /// <summary>
        /// Prepare image tensor for SAM2 processing.
        /// </summary>
        private TensorFloat PrepareImageTensorForSAM2(Texture2D image) {
            // Create tensor with shape [1, 3, inputSize, inputSize]
            TensorFloat tensor = new TensorFloat(new long[] { 1, 3, _sam2InputSize, _sam2InputSize });
            
            // Fill tensor with normalized pixel values
            float[] rgbValues = new float[_sam2InputSize * _sam2InputSize * 3];
            Color[] pixels = image.GetPixels();
            
            // SAM2 expects images normalized with mean [0.485, 0.456, 0.406] and std [0.229, 0.224, 0.225]
            float[] mean = new float[] { 0.485f, 0.456f, 0.406f };
            float[] std = new float[] { 0.229f, 0.224f, 0.225f };
            
            for (int i = 0; i < pixels.Length; i++) {
                rgbValues[i * 3] = (pixels[i].r - mean[0]) / std[0];
                rgbValues[i * 3 + 1] = (pixels[i].g - mean[1]) / std[1];
                rgbValues[i * 3 + 2] = (pixels[i].b - mean[2]) / std[2];
            }
            
            // Convert from HWC to CHW format
            float[] chwValues = new float[_sam2InputSize * _sam2InputSize * 3];
            for (int c = 0; c < 3; c++) {
                for (int h = 0; h < _sam2InputSize; h++) {
                    for (int w = 0; w < _sam2InputSize; w++) {
                        int inputIdx = (h * _sam2InputSize + w) * 3 + c;
                        int outputIdx = c * _sam2InputSize * _sam2InputSize + h * _sam2InputSize + w;
                        chwValues[outputIdx] = rgbValues[inputIdx];
                    }
                }
            }
            
            tensor.data.Fill(chwValues);
            return tensor;
        }
        
        /// <summary>
        /// Convert mask tensor to texture.
        /// </summary>
        private Texture2D ConvertMaskTensorToTexture(TensorFloat maskTensor, int width, int height) {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] colors = new Color[width * height];
            
            // SAM2 output is a mask with values from 0 to 1
            for (int h = 0; h < height; h++) {
                for (int w = 0; w < width; w++) {
                    float value = maskTensor[0, 0, h, w]; // Shape is [1, 1, H, W]
                    colors[h * width + w] = new Color(value, value, value, value);
                }
            }
            
            texture.SetPixels(colors);
            texture.Apply();
            return texture;
        }
        
        /// <summary>
        /// Calculate centroid of a mask tensor.
        /// </summary>
        private Vector2 CalculateMaskCentroid(TensorFloat maskTensor) {
            float sumX = 0, sumY = 0, sumWeight = 0;
            int height = maskTensor.shape[2]; // For shape [1, 1, H, W]
            int width = maskTensor.shape[3];
            
            for (int h = 0; h < height; h++) {
                for (int w = 0; w < width; w++) {
                    float value = maskTensor[0, 0, h, w];
                    sumX += w * value;
                    sumY += h * value;
                    sumWeight += value;
                }
            }
            
            if (sumWeight > 0) {
                return new Vector2(sumX / sumWeight, sumY / sumWeight);
            }
            
            // Fallback to center if mask is empty
            return new Vector2(width / 2, height / 2);
        }
        
        /// <summary>
        /// Calculate area of a mask tensor.
        /// </summary>
        private float CalculateMaskArea(TensorFloat maskTensor) {
            float sum = 0;
            int height = maskTensor.shape[2]; // For shape [1, 1, H, W]
            int width = maskTensor.shape[3];
            
            for (int h = 0; h < height; h++) {
                for (int w = 0; w < width; w++) {
                    sum += maskTensor[0, 0, h, w];
                }
            }
            
            return sum / (width * height); // Normalized area (0-1)
        }
        
        /// <summary>
        /// Run Faster R-CNN classification on detected objects.
        /// </summary>
        private IEnumerator RunFasterRCNNClassification(Texture2D image, List<DetectedObject> detections) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Check if Faster R-CNN model is available
                if (_fasterRCNNWorker == null) {
                    LogWarning("Faster R-CNN model not loaded, skipping classification", LogCategory.AI);
                    yield break;
                }
                
                // Process each detected object
                int processedCount = 0;
                foreach (var detection in detections) {
                    // Break processing if cancelled
                    if (_cancellationTokenSource.IsCancellationRequested) break;
                    
                    // Extract region of interest
                    Texture2D roi = ExtractROI(image, detection.boundingBox);
                    
                    // Classify the ROI
                    (string className, float confidence) = ClassifyWithFasterRCNN(roi);
                    
                    // Update detection if classification confidence is higher
                    if (confidence > detection.confidence) {
                        detection.className = className;
                        detection.confidence = confidence;
                    }
                    
                    // Clean up
                    Destroy(roi);
                    
                    // Yield periodically to prevent frame drops
                    processedCount++;
                    if (processedCount % 5 == 0) {
                        yield return null;
                    }
                }
                
                float classificationTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["FasterRCNNClassificationTime"] = classificationTime;
                
                Log($"Faster R-CNN classification completed in {classificationTime:F2} seconds for {processedCount} objects", 
                    LogCategory.AI);
            }
            catch (Exception ex) {
                LogError($"Error during Faster R-CNN classification: {ex.Message}", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Classify an image region using Faster R-CNN.
        /// </summary>
        private (string className, float confidence) ClassifyWithFasterRCNN(Texture2D roi) {
            try {
                // Resize ROI to Faster R-CNN input size
                Texture2D resizedROI = ResizeTexture(roi, _fasterRCNNInputSize, _fasterRCNNInputSize);
                
                // Prepare input tensor
                TensorFloat inputTensor = PrepareImageTensorForFasterRCNN(resizedROI);
                
                // Execute Faster R-CNN model
                _fasterRCNNWorker.Execute(inputTensor);
                
                // Get outputs
                TensorFloat classScores = _fasterRCNNWorker.PeekOutput("scores") as TensorFloat;
                TensorInt classIndices = _fasterRCNNWorker.PeekOutput("classes") as TensorInt;
                
                if (classScores == null || classIndices == null) {
                    LogWarning("Faster R-CNN inference failed to produce valid outputs", LogCategory.AI);
                    return ("unknown", 0);
                }
                
                // Find best class
                float bestScore = 0;
                int bestClassIdx = 0;
                
                for (int i = 0; i < classScores.shape[0]; i++) {
                    float score = classScores[i];
                    if (score > bestScore) {
                        bestScore = score;
                        bestClassIdx = i;
                    }
                }
                
                int classIndex = classIndices[bestClassIdx];
                string className = GetClassName(classIndex);
                
                // Clean up resources
                inputTensor.Dispose();
                Destroy(resizedROI);
                
                return (className, bestScore);
            }
            catch (Exception ex) {
                LogError($"Error in Faster R-CNN classification: {ex.Message}", LogCategory.AI);
                return ("unknown", 0);
            }
        }
        
        /// <summary>
        /// Prepare image tensor for Faster R-CNN processing.
        /// </summary>
        private TensorFloat PrepareImageTensorForFasterRCNN(Texture2D image) {
            // Create tensor with shape [1, 3, inputSize, inputSize]
            TensorFloat tensor = new TensorFloat(new long[] { 1, 3, _fasterRCNNInputSize, _fasterRCNNInputSize });
            
            // Fill tensor with normalized pixel values
            float[] rgbValues = new float[_fasterRCNNInputSize * _fasterRCNNInputSize * 3];
            Color[] pixels = image.GetPixels();
            
            // Faster R-CNN expects images normalized with mean [0.485, 0.456, 0.406] and std [0.229, 0.224, 0.225]
            float[] mean = new float[] { 0.485f, 0.456f, 0.406f };
            float[] std = new float[] { 0.229f, 0.224f, 0.225f };
            
            for (int i = 0; i < pixels.Length; i++) {
                rgbValues[i * 3] = (pixels[i].r - mean[0]) / std[0];
                rgbValues[i * 3 + 1] = (pixels[i].g - mean[1]) / std[1];
                rgbValues[i * 3 + 2] = (pixels[i].b - mean[2]) / std[2];
            }
            
            // Convert from HWC to CHW format
            float[] chwValues = new float[_fasterRCNNInputSize * _fasterRCNNInputSize * 3];
            for (int c = 0; c < 3; c++) {
                for (int h = 0; h < _fasterRCNNInputSize; h++) {
                    for (int w = 0; w < _fasterRCNNInputSize; w++) {
                        int inputIdx = (h * _fasterRCNNInputSize + w) * 3 + c;
                        int outputIdx = c * _fasterRCNNInputSize * _fasterRCNNInputSize + h * _fasterRCNNInputSize + w;
                        chwValues[outputIdx] = rgbValues[inputIdx];
                    }
                }
            }
            
            tensor.data.Fill(chwValues);
            return tensor;
        }
        
        #endregion
        #region Description Generation
        
        /// <summary>
        /// Generate short descriptions for detected objects.
        /// </summary>
        private void GenerateShortDescriptions() {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                foreach (var detection in _detectedObjects) {
                    detection.shortDescription = GenerateBasicDescription(detection);
                }
                
                float descriptionTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["ShortDescriptionTime"] = descriptionTime;
                
                Log($"Generated short descriptions in {descriptionTime:F2} seconds", LogCategory.AI);
            }
            catch (Exception ex) {
                LogError($"Error generating short descriptions: {ex.Message}", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Generate a basic description for a detected object.
        /// </summary>
        private string GenerateBasicDescription(DetectedObject detection) {
            string sizeDescription = "";
            float area = detection.boundingBox.width * detection.boundingBox.height;
            float imageArea = _lastImageWidth * _lastImageHeight;
            float relativeSize = area / imageArea;
            
            if (relativeSize > 0.25f) {
                sizeDescription = "very large ";
            }
            else if (relativeSize > 0.1f) {
                sizeDescription = "large ";
            }
            else if (relativeSize > 0.01f) {
                sizeDescription = "medium-sized ";
            }
            else {
                sizeDescription = "small ";
            }
            
            // Determine position
            string positionDescription = "";
            float normalizedX = detection.centroid.x / _lastImageWidth;
            float normalizedY = detection.centroid.y / _lastImageHeight;
            
            if (normalizedX < 0.33f) {
                positionDescription += "on the left ";
            }
            else if (normalizedX > 0.66f) {
                positionDescription += "on the right ";
            }
            else {
                positionDescription += "in the center ";
            }
            
            if (normalizedY < 0.33f) {
                positionDescription += "of the bottom ";
            }
            else if (normalizedY > 0.66f) {
                positionDescription += "of the top ";
            }
            else {
                positionDescription += "of the middle ";
            }
            
            // Build the description
            string objectType = detection.className;
            
            if (detection.isTerrain) {
                return $"A {sizeDescription}{objectType} terrain feature {positionDescription}of the map.";
            }
            else {
                return $"A {sizeDescription}{objectType} {positionDescription}of the map.";
            }
        }
        
        /// <summary>
        /// Enhance descriptions with OpenAI.
        /// </summary>
        private IEnumerator EnhanceDescriptionsWithOpenAI() {
            if (string.IsNullOrEmpty(_openAIApiKey)) {
                LogWarning("OpenAI API key not provided, skipping description enhancement", LogCategory.AI);
                yield break;
            }
            
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Find OpenAIResponse component
                OpenAIResponse openAIResponse = FindObjectOfType<OpenAIResponse>();
                if (openAIResponse == null) {
                    LogWarning("OpenAIResponse component not found, skipping description enhancement", LogCategory.AI);
                    yield break;
                }
                
                // Set API key
                openAIResponse.SetApiKey(_openAIApiKey);
                
                // Process objects in batches to avoid too many API calls
                int batchSize = 5;
                int processedCount = 0;
                
                for (int i = 0; i < _detectedObjects.Count; i += batchSize) {
                    // Process a batch of objects
                    List<DetectedObject> batch = _detectedObjects.Skip(i).Take(batchSize).ToList();
                    
                    foreach (var detection in batch) {
                        // Skip if cancelled
                        if (_cancellationTokenSource.IsCancellationRequested) yield break;
                        
                        // Build prompt
                        string prompt = BuildEnhancementPrompt(detection);
                        
                        // Request completion
                        string enhancedDescription = detection.shortDescription;
                        bool completed = false;
                        
                        yield return StartCoroutine(openAIResponse.GetCompletion(
                            prompt,
                            (response) => {
                                enhancedDescription = response;
                                completed = true;
                            },
                            (error) => {
                                LogWarning($"OpenAI error: {error}", LogCategory.AI);
                                completed = true;
                            }
                        ));
                        
                        // Wait for completion
                        while (!completed && !_cancellationTokenSource.IsCancellationRequested) {
                            yield return null;
                        }
                        
                        // Update description
                        detection.enhancedDescription = enhancedDescription;
                        processedCount++;
                    }
                    
                    // Yield to prevent blocking
                    yield return null;
                }
                
                float enhancementTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["OpenAIEnhancementTime"] = enhancementTime;
                
                Log($"Enhanced descriptions with OpenAI in {enhancementTime:F2} seconds for {processedCount} objects", 
                    LogCategory.AI);
            }
            catch (Exception ex) {
                LogError($"Error enhancing descriptions with OpenAI: {ex.Message}", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Build a prompt for OpenAI to enhance a description.
        /// </summary>
        private string BuildEnhancementPrompt(DetectedObject detection) {
            StringBuilder sb = new StringBuilder();
            
            if (detection.isTerrain) {
                sb.AppendLine("You are a terrain analysis expert who can provide detailed descriptions of geographical features.");
                sb.AppendLine("Provide a rich, detailed description of the following terrain feature:");
                sb.AppendLine($"Feature: {detection.className}");
                sb.AppendLine($"Basic Description: {detection.shortDescription}");
                sb.AppendLine("Include details about typical elevation, formation process, vegetation, and appearance.");
                sb.AppendLine("Keep the description to 2-3 sentences, professional in tone.");
            }
            else if (detection.isManMade) {
                sb.AppendLine("You are a 3D modeling expert who can describe man-made objects for realistic rendering.");
                sb.AppendLine("Provide a detailed description of the following object for 3D model generation:");
                sb.AppendLine($"Object: {detection.className}");
                sb.AppendLine($"Basic Description: {detection.shortDescription}");
                sb.AppendLine("Include details about materials, dimensions, colors, and notable features.");
                sb.AppendLine("Keep the description to 2-3 sentences, technical but accessible.");
            }
            else {
                sb.AppendLine("You are an expert in environmental detail who can provide rich descriptions of natural elements.");
                sb.AppendLine("Provide a detailed description of the following object:");
                sb.AppendLine($"Object: {detection.className}");
                sb.AppendLine($"Basic Description: {detection.shortDescription}");
                sb.AppendLine("Include details about appearance, typical size, and context in the environment.");
                sb.AppendLine("Keep the description to 2-3 sentences, informative and clear.");
            }
            
            return sb.ToString();
        }
        
        #endregion
        
        #region Height Estimation
        
        /// <summary>
        /// Generate height estimation data.
        /// </summary>
        private void GenerateHeightEstimationData(Texture2D mapTexture) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Initialize height map
                int resolution = 256; // Adjust based on performance needs
                _heightMap = new float[resolution, resolution];
                
                // Use both color information and detected terrain objects
                GenerateHeightMapFromColor(mapTexture, resolution);
                ApplyTerrainObjectsToHeightMap(resolution);
                SmoothHeightMap(resolution);
                
                float heightmapTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["HeightEstimationTime"] = heightmapTime;
                
                Log($"Generated height estimation data in {heightmapTime:F2} seconds", LogCategory.AI);
            }
            catch (Exception ex) {
                LogError($"Error generating height estimation data: {ex.Message}", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Generate height map from image color information.
        /// </summary>
        private void GenerateHeightMapFromColor(Texture2D mapTexture, int resolution) {
            // Get image pixels
            Color[] pixels = mapTexture.GetPixels();
            int width = mapTexture.width;
            int height = mapTexture.height;
            
            // Estimate height from color brightness
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    // Sample image at corresponding position
                    int imgX = (int)(x * width / (float)resolution);
                    int imgY = (int)(y * height / (float)resolution);
                    int pixelIndex = imgY * width + imgX;
                    
                    if (pixelIndex < pixels.Length) {
                        Color pixel = pixels[pixelIndex];
                        
                        // Calculate brightness (higher is brighter)
                        float brightness = (pixel.r + pixel.g + pixel.b) / 3f;
                        
                        // Normalize and map to height value (0-1)
                        // Assumption: Brighter areas are higher, darker areas are lower
                        float heightValue = Mathf.Pow(brightness, 1.5f); // Non-linear mapping
                        
                        // Different method for potential water detection
                        bool potentialWater = IsLikelyWater(pixel);
                        if (potentialWater) {
                            heightValue *= 0.1f; // Water is much lower
                        }
                        
                        _heightMap[x, y] = heightValue;
                    }
                }
            }
        }
        
        /// <summary>
        /// Detect if a pixel likely represents water.
        /// </summary>
        private bool IsLikelyWater(Color pixel) {
            // Blue channel is significantly higher than others
            return (pixel.b > 0.5f && pixel.b > pixel.r * 1.5f && pixel.b > pixel.g * 1.2f);
        }
        
        /// <summary>
        /// Apply terrain objects to height map.
        /// </summary>
        private void ApplyTerrainObjectsToHeightMap(int resolution) {
            foreach (var terrainObj in _terrainObjects) {
                // Calculate region on heightmap
                int startX = Mathf.FloorToInt(terrainObj.boundingBox.x * resolution / _lastImageWidth);
                int startY = Mathf.FloorToInt(terrainObj.boundingBox.y * resolution / _lastImageHeight);
                int endX = Mathf.CeilToInt((terrainObj.boundingBox.x + terrainObj.boundingBox.width) * resolution / _lastImageWidth);
                int endY = Mathf.CeilToInt((terrainObj.boundingBox.y + terrainObj.boundingBox.height) * resolution / _lastImageHeight);
                
                // Clamp to heightmap bounds
                startX = Mathf.Clamp(startX, 0, resolution - 1);
                startY = Mathf.Clamp(startY, 0, resolution - 1);
                endX = Mathf.Clamp(endX, 0, resolution - 1);
                endY = Mathf.Clamp(endY, 0, resolution - 1);
                
                // Calculate center
                int centerX = (startX + endX) / 2;
                int centerY = (startY + endY) / 2;
                
                // Get estimated height for terrain type
                float heightValue = EstimateHeightForTerrainType(terrainObj.className);
                
                // Apply to heightmap with radial falloff
                for (int y = startY; y <= endY; y++) {
                    for (int x = startX; x <= endX; x++) {
                        // Calculate distance from center (normalized 0-1)
                        float distX = (x - centerX) / (float)(endX - startX + 1);
                        float distY = (y - centerY) / (float)(endY - startY + 1);
                        float dist = Mathf.Sqrt(distX * distX + distY * distY);
                        
                        // Apply falloff based on distance
                        float falloff = Mathf.Clamp01(1f - dist * 1.5f);
                        
                        // Terrain type specific application
                        if (IsWaterType(terrainObj.className)) {
                            // Water features should be below surrounding terrain
                            _heightMap[x, y] = Mathf.Min(_heightMap[x, y], heightValue);
                        }
                        else if (IsMountainType(terrainObj.className)) {
                            // Mountain features create peaks
                            float peakValue = heightValue * falloff;
                            _heightMap[x, y] = Mathf.Max(_heightMap[x, y], peakValue);
                        }
                        else {
                            // Default blend
                            _heightMap[x, y] = Mathf.Lerp(_heightMap[x, y], heightValue, falloff * 0.7f);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Smooth the height map to reduce artifacts.
        /// </summary>
        private void SmoothHeightMap(int resolution) {
            float[,] smoothed = new float[resolution, resolution];
            int kernelSize = 3;
            
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    float sum = 0f;
                    int count = 0;
                    
                    // Apply smoothing kernel
                    for (int ky = -kernelSize; ky <= kernelSize; ky++) {
                        for (int kx = -kernelSize; kx <= kernelSize; kx++) {
                            int sampleX = x + kx;
                            int sampleY = y + ky;
                            
                            if (sampleX >= 0 && sampleX < resolution && sampleY >= 0 && sampleY < resolution) {
                                // Weight samples by distance (gaussian-like)
                                float weight = 1f / (1f + Mathf.Sqrt(kx * kx + ky * ky));
                                sum += _heightMap[sampleX, sampleY] * weight;
                                count += 1;
                            }
                        }
                    }
                    
                    if (count > 0) {
                        smoothed[x, y] = sum / count;
                    }
                }
            }
            
            // Replace with smoothed values
            _heightMap = smoothed;
        }
        
        /// <summary>
        /// Estimate height value for a terrain type.
        /// </summary>
        private float EstimateHeightForTerrainType(string terrainType) {
            terrainType = terrainType.ToLower();
            
            switch (terrainType) {
                case "mountain": return 1.0f;
                case "hill": return 0.7f;
                case "plateau": return 0.6f;
                case "highland": return 0.65f;
                case "cliff": return 0.8f;
                case "ridge": return 0.75f;
                case "valley": return 0.3f;
                case "canyon": return 0.25f;
                case "ravine": return 0.2f;
                case "water":
                case "lake":
                case "river":
                case "ocean":
                case "pond": return 0.05f;
                case "plain":
                case "grassland": return 0.4f;
                case "forest":
                case "woods": return 0.5f;
                case "desert": return 0.35f;
                case "beach": return 0.15f;
                case "swamp":
                case "marsh": return 0.1f;
                default: return 0.5f;
            }
        }
        
        /// <summary>
        /// Check if terrain type is water.
        /// </summary>
        private bool IsWaterType(string terrainType) {
            terrainType = terrainType.ToLower();
            return terrainType == "water" || terrainType == "lake" || terrainType == "river" || 
                   terrainType == "ocean" || terrainType == "pond" || terrainType == "swamp" || 
                   terrainType == "marsh";
        }
        
        /// <summary>
        /// Check if terrain type is mountainous.
        /// </summary>
        private bool IsMountainType(string terrainType) {
            terrainType = terrainType.ToLower();
            return terrainType == "mountain" || terrainType == "hill" || terrainType == "peak" || 
                   terrainType == "ridge" || terrainType == "highland" || terrainType == "plateau";
        }
        
        #endregion
        #region Results and Cleanup
        
        /// <summary>
        /// Build analysis results from processed data.
        /// </summary>
        private AnalysisResults BuildAnalysisResults(Texture2D sourceImage) {
            try {
                AnalysisResults results = new AnalysisResults {
                    sourceImage = sourceImage,
                    detectedObjects = new List<DetectedObject>(_detectedObjects),
                    terrainObjects = new List<DetectedObject>(_terrainObjects),
                    manMadeObjects = new List<DetectedObject>(_manMadeObjects),
                    heightMap = GenerateHeightMapTexture(),
                    segmentationOverlay = GenerateSegmentationOverlay(),
                    performanceMetrics = new Dictionary<string, float>(_performanceMetrics),
                    analysisTimestamp = DateTime.Now,
                    mapName = sourceImage.name
                };
                
                // Extract unique terrain types
                HashSet<string> terrainTypes = new HashSet<string>();
                foreach (var obj in _terrainObjects) {
                    terrainTypes.Add(obj.className);
                }
                results.detectedTerrainTypes = terrainTypes.ToList();
                
                // Create MapObjects and TerrainFeatures
                foreach (var detection in _detectedObjects) {
                    if (detection.isTerrain) {
                        TerrainFeature feature = CreateTerrainFeatureFromDetection(detection);
                        results.terrainFeatures.Add(feature);
                    }
                    else {
                        MapObject mapObject = CreateMapObjectFromDetection(detection);
                        results.mapObjects.Add(mapObject);
                    }
                }
                
                return results;
            }
            catch (Exception ex) {
                LogError($"Error building analysis results: {ex.Message}", LogCategory.AI);
                return new AnalysisResults();
            }
        }
        
        /// <summary>
        /// Create a terrain feature from a detection.
        /// </summary>
        private TerrainFeature CreateTerrainFeatureFromDetection(DetectedObject detection) {
            TerrainFeature feature = new TerrainFeature {
                featureType = detection.className,
                bounds = detection.boundingBox,
                elevation = EstimateHeightForTerrainType(detection.className),
                description = detection.enhancedDescription ?? detection.shortDescription,
                metadata = new Dictionary<string, object>(detection.metadata)
            };
            
            // Add contour points if available
            if (detection.segments.Count > 0) {
                feature.contourPoints = detection.segments[0].contourPoints;
            }
            
            return feature;
        }
        
        /// <summary>
        /// Create a map object from a detection.
        /// </summary>
        private MapObject CreateMapObjectFromDetection(DetectedObject detection) {
            MapObject mapObject = new MapObject {
                objectType = detection.className,
                objectName = detection.className,
                position = detection.centroid,
                rotation = UnityEngine.Random.Range(0f, 360f), // Default to random rotation
                scale = new Vector2(detection.boundingBox.width / _lastImageWidth, detection.boundingBox.height / _lastImageHeight),
                description = detection.enhancedDescription ?? detection.shortDescription,
                estimatedHeight = detection.estimatedHeight,
                materials = detection.estimatedMaterials,
                metadata = new Dictionary<string, object>(detection.metadata)
            };
            
            return mapObject;
        }
        
        /// <summary>
        /// Generate a texture from the height map.
        /// </summary>
        private Texture2D GenerateHeightMapTexture() {
            if (_heightMap == null) return null;
            
            int resolution = _heightMap.GetLength(0);
            Texture2D heightMapTexture = new Texture2D(resolution, resolution, TextureFormat.RFloat, false);
            
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    float height = _heightMap[x, y];
                    Color color = new Color(height, height, height, 1f);
                    heightMapTexture.SetPixel(x, y, color);
                }
            }
            
            heightMapTexture.Apply();
            return heightMapTexture;
        }
        
        /// <summary>
        /// Generate a segmentation overlay texture.
        /// </summary>
        private Texture2D GenerateSegmentationOverlay() {
            if (_currentImage == null) return null;
            
            // Create overlay with original image dimensions
            Texture2D overlay = new Texture2D((int)_lastImageWidth, (int)_lastImageHeight, TextureFormat.RGBA32, false);
            
            // Fill with transparent color
            Color[] transparentPixels = new Color[overlay.width * overlay.height];
            for (int i = 0; i < transparentPixels.Length; i++) {
                transparentPixels[i] = new Color(0, 0, 0, 0);
            }
            overlay.SetPixels(transparentPixels);
            
            // Apply segmentation masks with different colors
            for (int i = 0; i < _detectedObjects.Count; i++) {
                var detection = _detectedObjects[i];
                
                // Skip if no segments
                if (detection.segments.Count == 0) continue;
                
                // Get segment mask
                var segment = detection.segments[0];
                if (segment.maskTexture == null) continue;
                
                // Generate color for this segment
                Color segmentColor = GenerateObjectColor(detection, i);
                detection.color = segmentColor;
                
                // Apply segment to overlay
                ApplySegmentToOverlay(overlay, segment.maskTexture, segmentColor);
            }
            
            overlay.Apply();
            return overlay;
        }
        
        /// <summary>
        /// Apply a segment mask to the overlay texture.
        /// </summary>
        private void ApplySegmentToOverlay(Texture2D overlay, Texture2D mask, Color segmentColor) {
            // Ensure same dimensions
            if (mask.width != overlay.width || mask.height != overlay.height) {
                mask = ResizeTexture(mask, overlay.width, overlay.height);
            }
            
            // Get pixels
            Color[] overlayPixels = overlay.GetPixels();
            Color[] maskPixels = mask.GetPixels();
            
            // Apply mask
            for (int i = 0; i < overlayPixels.Length; i++) {
                float maskValue = maskPixels[i].r;
                if (maskValue > 0.5f) { // Threshold
                    // Semi-transparent overlay
                    Color blendedColor = segmentColor;
                    blendedColor.a = 0.6f; // Adjust transparency
                    
                    // Alpha blend
                    overlayPixels[i] = Color.Lerp(overlayPixels[i], blendedColor, maskValue);
                }
            }
            
            overlay.SetPixels(overlayPixels);
        }
        
        /// <summary>
        /// Generate a color for an object.
        /// </summary>
        private Color GenerateObjectColor(DetectedObject detection, int index) {
            // Different color schemes for different object types
            if (detection.isTerrain) {
                // Terrain color scheme (natural colors)
                switch (detection.className.ToLower()) {
                    case "water":
                    case "lake":
                    case "river":
                    case "ocean":
                        return new Color(0.2f, 0.5f, 0.9f, 0.7f);
                    case "mountain":
                    case "hill":
                        return new Color(0.5f, 0.4f, 0.3f, 0.7f);
                    case "forest":
                    case "tree":
                    case "woods":
                        return new Color(0.2f, 0.6f, 0.3f, 0.7f);
                    case "grass":
                    case "grassland":
                    case "plain":
                        return new Color(0.4f, 0.8f, 0.4f, 0.7f);
                    case "sand":
                    case "desert":
                    case "beach":
                        return new Color(0.9f, 0.8f, 0.5f, 0.7f);
                    case "snow":
                    case "ice":
                        return new Color(0.9f, 0.9f, 1.0f, 0.7f);
                    default:
                        // Use hue based on terrain name hash
                        float hue = (detection.className.GetHashCode() % 100) / 100f;
                        return Color.HSVToRGB(hue, 0.5f, 0.8f);
                }
            }
            else {
                // Man-made objects (more vibrant colors)
                float hue = (index % 10) / 10f;
                return Color.HSVToRGB(hue, 0.7f, 1f);
            }
        }
        
        /// <summary>
        /// Clean up resources.
        /// </summary>
        private void CleanupResources() {
            try {
                // Cancel any ongoing processing
                if (_cancellationTokenSource != null) {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }
                
                // Dispose workers
                _yoloWorker?.Dispose();
                _sam2Worker?.Dispose();
                _fasterRCNNWorker?.Dispose();
                
                _yoloWorker = null;
                _sam2Worker = null;
                _fasterRCNNWorker = null;
                
                // Dispose models
                _yoloModel = null;
                _sam2Model = null;
                _fasterRCNNModel = null;
                
                // Dispose model assets
                _yoloModelAsset = null;
                _sam2ModelAsset = null;
                _fasterRCNNModelAsset = null;
                
                _isModelLoaded = false;
                _isProcessing = false;
                
                Log("MapAnalyzer resources cleaned up", LogCategory.System);
            }
            catch (Exception ex) {
                LogError($"Error during cleanup: {ex.Message}", LogCategory.System);
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Resize a texture to the specified dimensions.
        /// </summary>
        private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight) {
            // Skip if already the right size
            if (source.width == targetWidth && source.height == targetHeight) {
                return source;
            }
            
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            
            RenderTexture prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }
        
        /// <summary>
        /// Extract a region of interest from a texture.
        /// </summary>
        private Texture2D ExtractROI(Texture2D source, Rect bbox) {
            // Ensure bbox is within image bounds
            int x = Mathf.Clamp(Mathf.FloorToInt(bbox.x), 0, source.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(bbox.y), 0, source.height - 1);
            int width = Mathf.Clamp(Mathf.CeilToInt(bbox.width), 1, source.width - x);
            int height = Mathf.Clamp(Mathf.CeilToInt(bbox.height), 1, source.height - y);
            
            // Create result texture
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            
            // Get pixels from source
            Color[] pixels = source.GetPixels(x, y, width, height);
            result.SetPixels(pixels);
            result.Apply();
            
            return result;
        }
        
        /// <summary>
        /// Get class name from class index.
        /// </summary>
        private string GetClassName(int classIndex) {
            if (_classLabels != null && classIndex >= 0 && classIndex < _classLabels.Length) {
                return _classLabels[classIndex];
            }
            return "unknown";
        }
        
        /// <summary>
        /// Check if an object is likely a terrain feature.
        /// </summary>
        private bool IsTerrainObject(string className) {
            string name = className.ToLower();
            
            // List of common terrain features
            string[] terrainFeatures = {
                "mountain", "hill", "water", "lake", "river", "ocean", "forest", "tree", "grass",
                "plain", "plateau", "valley", "canyon", "cliff", "beach", "desert", "snow", "ice",
                "swamp", "marsh", "meadow", "field", "dune", "rock", "boulder", "terrain", "land",
                "island", "peninsula", "bay", "coast", "shore", "woods", "ridge", "peak", "hill"
            };
            
            return terrainFeatures.Contains(name);
        }
        
        /// <summary>
        /// Check if an object is likely man-made.
        /// </summary>
        private bool IsManMadeObject(string className) {
            string name = className.ToLower();
            
            // List of common man-made objects
            string[] manMadeObjects = {
                "building", "house", "tower", "bridge", "road", "path", "fence", "wall", "car",
                "truck", "boat", "ship", "airplane", "train", "bench", "sign", "monument", "statue",
                "windmill", "lighthouse", "dam", "factory", "farm", "barn", "church", "castle",
                "temple", "well", "gate", "streetlight", "powerline", "pipeline", "dock", "pier"
            };
            
            return manMadeObjects.Contains(name);
        }
        
        /// <summary>
        /// Log a message using the debugger.
        /// </summary>
        private void Log(string message, LogCategory category) {
            _debugger?.Log(message, category);
        }
        
        /// <summary>
        /// Log a warning using the debugger.
        /// </summary>
        private void LogWarning(string message, LogCategory category) {
            _debugger?.LogWarning(message, category);
        }
        
        /// <summary>
        /// Log an error using the debugger.
        /// </summary>
        private void LogError(string message, LogCategory category) {
            _debugger?.LogError(message, category);
        }
        
        #endregion
    }
}
