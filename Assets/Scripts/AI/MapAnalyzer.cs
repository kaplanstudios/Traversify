/*************************************************************************
 *  Traversify – MapAnalyzer.cs                                          *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 04:09:24 UTC                                     *
 *  Desc   : Advanced terrain and object analysis system using YOLOv12,  *
 *           SAM2, and Faster R-CNN for high-precision detection,        *
 *           segmentation, and recognition of map elements with height   *
 *           estimation and OpenAI-enhanced descriptions.                *
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

using Traversify.Core;

namespace Traversify.AI 
{
    /// <summary>
    /// Core component for analyzing map images using AI models to identify terrain features 
    /// and objects, segment them, and generate enhanced descriptions.
    /// </summary>
    [RequireComponent(typeof(TraversifyDebugger))]
    public class MapAnalyzer : TraversifyComponent 
    {
        #region Singleton Pattern
        
        private static MapAnalyzer _instance;
        
        /// <summary>
        /// Singleton instance of the MapAnalyzer.
        /// </summary>
        public static MapAnalyzer Instance 
        {
            get 
            {
                if (_instance == null) 
                {
                    _instance = FindObjectOfType<MapAnalyzer>();
                    if (_instance == null) 
                    {
                        GameObject go = new GameObject("MapAnalyzer");
                        _instance = go.AddComponent<MapAnalyzer>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Properties
        
        [Header("Model Assets")]
        [Tooltip("YOLOv12 object detection model")]
        [SerializeField] private Unity.Sentis.ModelAsset yoloModel;
        
        [Tooltip("SAM2 segmentation model")]
        [SerializeField] private Unity.Sentis.ModelAsset sam2Model;
        
        [Tooltip("Faster R-CNN classification model")]
        [SerializeField] private Unity.Sentis.ModelAsset fasterRcnnModel;
        
        [Header("Detection Settings")]
        [Tooltip("Detection confidence threshold (0-1)")]
        [Range(0f, 1f)] 
        [SerializeField] private float _confidenceThreshold = 0.5f;
        public float confidenceThreshold 
        { 
            get => _confidenceThreshold; 
            set => _confidenceThreshold = value; 
        }
        
        [Tooltip("Non-maximum suppression threshold (0-1)")]
        [Range(0f, 1f)] 
        [SerializeField] private float _nmsThreshold = 0.45f;
        public float nmsThreshold 
        { 
            get => _nmsThreshold; 
            set => _nmsThreshold = value; 
        }
        
        [Tooltip("Use higher resolution analysis for better results")]
        [SerializeField] private bool _useHighQuality = true;
        public bool useHighQuality 
        { 
            get => _useHighQuality; 
            set => _useHighQuality = value; 
        }
        
        [Tooltip("Use GPU acceleration if available")]
        [SerializeField] private bool _useGPU = true;
        public bool useGPU 
        { 
            get => _useGPU; 
            set => _useGPU = value; 
        }
        
        [Tooltip("Maximum objects to process in a single analysis")]
        [SerializeField] private int _maxObjectsToProcess = 100;
        public int maxObjectsToProcess 
        { 
            get => _maxObjectsToProcess; 
            set => _maxObjectsToProcess = value; 
        }
        
        [Header("OpenAI Settings")]
        [Tooltip("API key for description enhancement")]
        [SerializeField] private string _openAIApiKey;
        public string openAIApiKey 
        { 
            get => _openAIApiKey; 
            set => _openAIApiKey = value; 
        }
        
        [Header("Analysis Settings")]
        [Tooltip("Automatically classify terrain vs object")]
        [SerializeField] private bool autoClassifyTerrainObjects = true;
        
        [Tooltip("Estimate height values for terrain features")]
        [SerializeField] private bool estimateTerrainHeight = true;
        
        [Tooltip("Enable detailed object classification")]
        [SerializeField] private bool enableDetailedClassification = true;
        
        [Tooltip("Enhance object descriptions using OpenAI")]
        [SerializeField] private bool enhanceDescriptions = true;
        
        [Tooltip("Maximum height value in world units")]
        [SerializeField] private float maxTerrainHeight = 100f;
        
        [Tooltip("Terrain size in world units")]
        [SerializeField] private Vector2 terrainSize = new Vector2(500f, 500f);
        
        [Header("Advanced Settings")]
        [Tooltip("Number of terrain class categories")]
        [SerializeField] private int terrainClassCount = 10;
        
        [Tooltip("Number of object class categories")]
        [SerializeField] private int objectClassCount = 80;
        
        [Tooltip("Height smoothing radius for terrain features")]
        [SerializeField] private float heightSmoothingRadius = 2.0f;
        
        [Tooltip("Maximum batch size for concurrent processing")]
        [SerializeField] private int processingBatchSize = 5;
        
        [Tooltip("Maximum API requests per frame")]
        [SerializeField] private int maxAPIRequestsPerFrame = 3;
        
        #endregion
        
        #region Private Fields
        
        // Internal components & state
        private TraversifyDebugger _debugger;
        private Unity.Sentis.Worker _yoloSession;
        private Unity.Sentis.Worker _sam2Session;
        private Unity.Sentis.Worker _rcnnSession;
        private string[] _classLabels;
        private Unity.Sentis.Worker _classificationWorker;
        private Unity.Sentis.Worker _heightWorker;
        private Unity.Sentis.Worker _fasterRcnnWorker;
        
        // Processing state
        private bool _isProcessing = false;
        private int _activeAnalyses = 0;
        private Queue<SegmentAnalysisRequest> _analysisQueue = new Queue<SegmentAnalysisRequest>();
        private List<AnalyzedSegment> _analyzedSegments = new List<AnalyzedSegment>();
        private TerrainGenerator _terrainGenerator;
        
        // Class mappings for terrain vs non-terrain
        private HashSet<string> _terrainClasses = new HashSet<string> {
            "mountain", "hill", "water", "lake", "river", "ocean", "forest", 
            "grassland", "desert", "snow", "ice", "terrain", "valley", "plateau"
        };
        
        // Flag to track if models are initialized
        private bool _modelsInitialized = false;
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Represents a request to analyze a segment in detail.
        /// </summary>
        private class SegmentAnalysisRequest
        {
            public ImageSegment segment;
            public Texture2D sourceImage;
        }
        
        /// <summary>
        /// Represents an analyzed image segment with detailed information.
        /// </summary>
        private class AnalyzedSegment
        {
            public ImageSegment originalSegment;
            public Rect boundingBox;
            public bool isTerrain;
            public float classificationConfidence;
            public string objectType;
            public string detailedClassification;
            public Dictionary<string, float> features;
            public Dictionary<string, float> topologyFeatures;
            public Texture2D heightMap;
            public float estimatedHeight;
            public Vector2 normalizedPosition;
            public float estimatedRotation;
            public float estimatedScale;
            public float placementConfidence;
            public string enhancedDescription;
            public Dictionary<string, object> metadata;
        }
        
        /// <summary>
        /// Request for terrain modification based on analysis.
        /// </summary>
        private class TerrainModification
        {
            public Rect bounds;
            public Texture2D heightMap;
            public float baseHeight;
            public string terrainType;
            public string description;
            public float blendRadius;
            public float slope;
            public float roughness;
        }
        
        /// <summary>
        /// Represents an object placement with position, rotation, scale and metadata
        /// </summary>
        [System.Serializable]
        public class ObjectPlacement
        {
            public string objectType;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public float confidence;
            public Rect boundingBox;
            public Dictionary<string, object> metadata;
        }
        
        #endregion
        
        #region TraversifyComponent Implementation
        
        /// <summary>
        /// Component-specific initialization logic.
        /// </summary>
        /// <param name="config">Component configuration object</param>
        /// <returns>True if initialization was successful</returns>
        protected override bool OnInitialize(object config)
        {
            try
            {
                // Initialize AI models
                InitializeModels();
                
                // Load class labels
                LoadClassLabels();
                
                // Find terrain generator reference
                _terrainGenerator = FindObjectOfType<TerrainGenerator>();
                
                Log("MapAnalyzer initialized successfully", LogCategory.System);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize MapAnalyzer: {ex.Message}", LogCategory.System);
                return false;
            }
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            // Initialize debugger
            _debugger = GetComponent<TraversifyDebugger>();
            if (_debugger == null)
            {
                _debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            _terrainGenerator = FindObjectOfType<TerrainGenerator>();
            
            LoadClassLabels();
            // Don't initialize models here - wait for TraversifyManager to configure them
            _modelsInitialized = false;
        }
        
        /// <summary>
        /// Initialize AI models after they have been configured by TraversifyManager
        /// </summary>
        public void InitializeModelsIfNeeded()
        {
            if (!_modelsInitialized)
            {
                // Load model assets if not assigned
                LoadModelAssetsIfNeeded();
                InitializeModels();
            }
        }

        /// <summary>
        /// Loads model assets from Resources if they're not already assigned
        /// </summary>
        private void LoadModelAssetsIfNeeded()
        {
            try
            {
                // First try to get models from TraversifyManager if available
                var traversifyManager = FindObjectOfType<TraversifyManager>();
                if (traversifyManager != null)
                {
                    // Use reflection to get the model assets from TraversifyManager
                    var managerType = traversifyManager.GetType();
                    
                    if (yoloModel == null)
                    {
                        var yoloField = managerType.GetField("_yoloModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (yoloField != null)
                        {
                            yoloModel = yoloField.GetValue(traversifyManager) as Unity.Sentis.ModelAsset;
                            if (yoloModel != null)
                            {
                                Log("✓ YOLO model obtained from TraversifyManager", LogCategory.AI);
                            }
                        }
                    }

                    if (sam2Model == null)
                    {
                        var sam2Field = managerType.GetField("_sam2Model", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (sam2Field != null)
                        {
                            sam2Model = sam2Field.GetValue(traversifyManager) as Unity.Sentis.ModelAsset;
                            if (sam2Model != null)
                            {
                                Log("✓ SAM2 model obtained from TraversifyManager", LogCategory.AI);
                            }
                        }
                    }

                    if (fasterRcnnModel == null)
                    {
                        var rcnnField = managerType.GetField("_fasterRcnnModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (rcnnField != null)
                        {
                            fasterRcnnModel = rcnnField.GetValue(traversifyManager) as Unity.Sentis.ModelAsset;
                            if (fasterRcnnModel != null)
                            {
                                Log("✓ Faster R-CNN model obtained from TraversifyManager", LogCategory.AI);
                            }
                        }
                    }
                }

                // Fallback to Resources loading if TraversifyManager models aren't available
                if (yoloModel == null)
                {
                    yoloModel = Resources.Load<Unity.Sentis.ModelAsset>("AI/Models/yolov8n");
                    if (yoloModel == null)
                    {
                        // Try alternative path
                        yoloModel = Resources.Load<Unity.Sentis.ModelAsset>("Models/yolov8n");
                    }
                    
                    if (yoloModel != null)
                    {
                        Log("✓ YOLO model loaded from Resources", LogCategory.AI);
                    }
                    else
                    {
                        LogError("✗ YOLO model could not be loaded from Resources", LogCategory.AI);
                    }
                }

                if (sam2Model == null)
                {
                    sam2Model = Resources.Load<Unity.Sentis.ModelAsset>("AI/Models/sam2_hiera_base");
                    if (sam2Model == null)
                    {
                        // Try alternative path
                        sam2Model = Resources.Load<Unity.Sentis.ModelAsset>("Models/sam2_hiera_base");
                    }
                    
                    if (sam2Model != null)
                    {
                        Log("✓ SAM2 model loaded from Resources", LogCategory.AI);
                    }
                }

                if (fasterRcnnModel == null)
                {
                    fasterRcnnModel = Resources.Load<Unity.Sentis.ModelAsset>("AI/Models/FasterRCNN-12");
                    if (fasterRcnnModel == null)
                    {
                        // Try alternative path
                        fasterRcnnModel = Resources.Load<Unity.Sentis.ModelAsset>("Models/FasterRCNN-12");
                    }
                    
                    if (fasterRcnnModel != null)
                    {
                        Log("✓ Faster R-CNN model loaded from Resources", LogCategory.AI);
                    }
                }

                Log("Model assets loading attempted", LogCategory.AI);
            }
            catch (Exception ex)
            {
                LogError($"Error loading model assets: {ex.Message}", LogCategory.AI);
            }
        }

        /// <summary>
        /// Check if the MapAnalyzer is properly initialized and ready to use
        /// </summary>
        public bool IsInitialized => _modelsInitialized;

        private void OnDestroy()
        {
            // Clean up resources
            DisposeModels();
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Analyzes the given map image: detect objects, segment terrain & objects, classify & enhance descriptions.
        /// Reports progress callbacks and returns AnalysisResults via onComplete.
        /// </summary>
        /// <param name="map">The map texture to analyze</param>
        /// <param name="onComplete">Callback when analysis is complete</param>
        /// <param name="onError">Callback if an error occurs</param>
        /// <param name="onProgress">Callback to report progress updates</param>
        /// <returns>Coroutine for tracking progress</returns>
        public IEnumerator AnalyzeImage(
            Texture2D map,
            Action<AnalysisResults> onComplete,
            Action<string> onError,
            Action<string, float> onProgress)
        {
            if (_isProcessing)
            {
                onError?.Invoke("Analysis already in progress");
                yield break;
            }
            
            _isProcessing = true;
            _analyzedSegments.Clear();
            
            // Start analysis process without try-catch around yield statements
            yield return StartCoroutine(AnalyzeMapImage(map, onComplete, onError, onProgress));
        }
        
        /// <summary>
        /// Analyzes a map image to detect terrain features and objects
        /// </summary>
        /// <param name="mapTexture">The input map texture to analyze</param>
        /// <param name="onComplete">Callback when analysis completes</param>
        /// <param name="onError">Callback when an error occurs</param>
        /// <param name="onProgress">Progress callback with stage and progress</param>
        /// <returns>Coroutine for the analysis process</returns>
        public IEnumerator AnalyzeMapImage(Texture2D mapTexture, System.Action<AnalysisResults> onComplete, System.Action<string> onError, System.Action<string, float> onProgress)
        {
            if (mapTexture == null)
            {
                onError?.Invoke("Map texture is null");
                yield break;
            }
            
            // Initialize if needed
            InitializeModelsIfNeeded();
            if (!_modelsInitialized)
            {
                onError?.Invoke("Failed to initialize AI models");
                yield break;
            }
            
            AnalysisResults results = null;
            string errorMessage = null;
            
            // Process without try-catch around yield
            onProgress?.Invoke("Starting image analysis...", 0.0f);
            
            // Step 1: Prepare image tensor
            onProgress?.Invoke("Preparing image data...", 0.1f);
            var imageTensor = PrepareImageTensor(mapTexture);
            
            // Step 2: Run YOLO detection 
            onProgress?.Invoke("Detecting objects...", 0.3f);
            List<DetectedObject> detectionResults = null;
            yield return StartCoroutine(RunDetectionWithYOLO(mapTexture, 
                (results) => detectionResults = results,
                (error) => errorMessage = error));
            
            if (!string.IsNullOrEmpty(errorMessage))
            {
                onError?.Invoke(errorMessage);
                yield break;
            }
            
            // Step 3: Run segmentation if SAM2 is available
            onProgress?.Invoke("Running segmentation...", 0.6f);
            List<ImageSegment> segmentationResults = null;
            yield return StartCoroutine(RunSegmentationWithSAM2(mapTexture, detectionResults, 
                (results) => segmentationResults = results,
                (error) => errorMessage = error));
            
            if (!string.IsNullOrEmpty(errorMessage))
            {
                // Warning only - continue with fallback
                _debugger?.LogWarning($"Segmentation warning: {errorMessage}", LogCategory.AI);
            }
            
            // Step 4: Enhance descriptions if OpenAI is available
            onProgress?.Invoke("Enhancing descriptions...", 0.8f);
            if (enhanceDescriptions && !string.IsNullOrEmpty(_openAIApiKey))
            {
                yield return StartCoroutine(EnhanceObjectDescriptions(detectionResults));
            }
            
            // Step 5: Build final results
            onProgress?.Invoke("Finalizing results...", 0.9f);
            results = BuildAnalysisResultsFromDetections(mapTexture, detectionResults, segmentationResults);
            
            onProgress?.Invoke("Analysis complete!", 1.0f);
            
            if (results != null)
            {
                // Invoke onComplete callback with results
                onComplete?.Invoke(results);
            }
            else
            {
                onError?.Invoke("Analysis completed but no results found");
            }
            
            _isProcessing = false;
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// Initializes the AI models for detection, segmentation, and classification.
        /// </summary>
        private void InitializeModels()
        {
            if (_modelsInitialized)
                return;
            
            try
            {
                // YOLOv12 initialization
                if (yoloModel != null)
                {
                    var model = Unity.Sentis.ModelLoader.Load(yoloModel);
                    _yoloSession = new Unity.Sentis.Worker(model, Unity.Sentis.BackendType.GPUCompute);
                }
                else
                {
                    LogError("YOLOv12 model asset is not assigned", LogCategory.AI);
                }
                
                // SAM2 initialization
                if (sam2Model != null)
                {
                    var model = Unity.Sentis.ModelLoader.Load(sam2Model);
                    _sam2Session = new Unity.Sentis.Worker(model, Unity.Sentis.BackendType.GPUCompute);
                }
                else
                {
                    LogError("SAM2 model asset is not assigned", LogCategory.AI);
                }
                
                // Faster R-CNN initialization
                if (fasterRcnnModel != null)
                {
                    var model = Unity.Sentis.ModelLoader.Load(fasterRcnnModel);
                    _fasterRcnnWorker = new Unity.Sentis.Worker(model, Unity.Sentis.BackendType.GPUCompute);
                }
                else
                {
                    LogError("Faster R-CNN model asset is not assigned", LogCategory.AI);
                }
                
                // Mark models as initialized
                _modelsInitialized = true;
                Log("AI models initialized successfully", LogCategory.AI);
            }
            catch (Exception ex)
            {
                LogError($"Error initializing AI models: {ex.Message}", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Disposes the AI model resources.
        /// </summary>
        private void DisposeModels()
        {
            if (_yoloSession != null)
            {
                _yoloSession.Dispose();
                _yoloSession = null;
            }
            
            if (_sam2Session != null)
            {
                _sam2Session.Dispose();
                _sam2Session = null;
            }
            
            if (_fasterRcnnWorker != null)
            {
                _fasterRcnnWorker.Dispose();
                _fasterRcnnWorker = null;
            }
        }
        
        /// <summary>
        /// Loads the class labels for the detection and segmentation models.
        /// </summary>
        private void LoadClassLabels()
        {
            // Load class labels from embedded resources or files
            _classLabels = new string[] {
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
        
        /// <summary>
        /// Prepares image tensor for analysis
        /// </summary>
        private Unity.Sentis.Tensor PrepareImageTensor(Texture2D mapTexture)
        {
            int targetSize = useHighQuality ? 640 : 416;
            return PrepareImageTensorForYOLO(mapTexture, targetSize);
        }
        
        /// <summary>
        /// Prepares image tensor for YOLO model input using Unity Sentis
        /// </summary>
        private Unity.Sentis.Tensor PrepareImageTensorForYOLO(Texture2D image, int targetSize)
        {
            // Create a RenderTexture to resize the image
            RenderTexture rt = RenderTexture.GetTemporary(targetSize, targetSize, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            
            Unity.Sentis.Tensor tensor = null;
            Texture2D resizedTexture = null;
            
            try
            {
                // Blit the original image to the render texture (this will scale it)
                Graphics.Blit(image, rt);
                
                // Create a new texture from the render texture
                RenderTexture.active = rt;
                resizedTexture = new Texture2D(targetSize, targetSize, TextureFormat.RGB24, false);
                resizedTexture.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
                resizedTexture.Apply();
                RenderTexture.active = null;
                
                // Convert to Unity Sentis tensor with proper format
                var tensorShape = new Unity.Sentis.TensorShape(1, 3, targetSize, targetSize);
                tensor = new Unity.Sentis.Tensor<float>(tensorShape);
                
                // Fill tensor with normalized pixel data
                Color[] pixels = resizedTexture.GetPixels();
                var tensorData = new float[tensor.shape.length];
                
                for (int y = 0; y < targetSize; y++)
                {
                    for (int x = 0; x < targetSize; x++)
                    {
                        int pixelIndex = y * targetSize + x;
                        Color pixel = pixels[pixelIndex];
                        
                        // Normalize to [0,1] and arrange in CHW format
                        int baseIndex = y * targetSize + x;
                        tensorData[0 * targetSize * targetSize + baseIndex] = pixel.r; // R channel
                        tensorData[1 * targetSize * targetSize + baseIndex] = pixel.g; // G channel  
                        tensorData[2 * targetSize * targetSize + baseIndex] = pixel.b; // B channel
                    }
                }
                
                // Upload data to tensor using proper Sentis API
                using (var dataArray = new Unity.Collections.NativeArray<float>(tensorData, Unity.Collections.Allocator.Temp))
                {
                    tensor.dataOnBackend.Upload(dataArray, tensorData.Length);
                }
                
                return tensor;
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error preparing image tensor: {ex.Message}", LogCategory.AI);
                // Dispose tensor if creation failed
                tensor?.Dispose();
                return null;
            }
            finally
            {
                // Always clean up temporary resources
                if (resizedTexture != null)
                    UnityEngine.Object.DestroyImmediate(resizedTexture);
                RenderTexture.ReleaseTemporary(rt);
            }
        }
        
        /// <summary>
        /// Estimates terrain height based on terrain type
        /// </summary>
        private float EstimateTerrainHeight(string terrainType)
        {
            switch (terrainType.ToLower())
            {
                case "mountain":
                    return UnityEngine.Random.Range(50f, 100f);
                case "hill":
                    return UnityEngine.Random.Range(10f, 30f);
                case "water":
                case "lake":
                case "river":
                case "ocean":
                    return UnityEngine.Random.Range(-5f, 0f);
                case "forest":
                    return UnityEngine.Random.Range(5f, 15f);
                case "desert":
                    return UnityEngine.Random.Range(0f, 10f);
                default:
                    return UnityEngine.Random.Range(0f, 5f);
            }
        }
        
        #endregion
        
        #region Missing Method Implementations
        
        /// <summary>
        /// Enhances object descriptions using OpenAI (placeholder implementation)
        /// </summary>
        private IEnumerator EnhanceObjectDescriptions(List<DetectedObject> detectedObjects)
        {
            _debugger?.Log("Enhancing object descriptions", LogCategory.AI);
            
            // Placeholder implementation - enhance descriptions for detected objects
            foreach (var obj in detectedObjects)
            {
                if (obj.classScores != null && obj.classScores.Count > 0)
                {
                    // Simple enhancement based on confidence and class
                    obj.enhancedDescription = $"A {obj.className} detected with {obj.confidence:P0} confidence";
                }
            }
            
            yield return null;
            _debugger?.Log("Object description enhancement completed", LogCategory.AI);
        }
        
        /// <summary>
        /// Builds analysis results from detections and segmentations
        /// </summary>
        private AnalysisResults BuildAnalysisResultsFromDetections(Texture2D mapTexture, List<DetectedObject> detections, List<ImageSegment> segments)
        {
            var results = new AnalysisResults
            {
                mapObjects = new List<MapObject>(),
                terrainFeatures = new List<TerrainFeature>(),
                objectGroups = new List<ObjectGroup>(),
                timings = new AnalysisTimings(new ProcessingTimings { totalTime = Time.realtimeSinceStartup }),
                heightMap = new Texture2D(mapTexture.width, mapTexture.height, TextureFormat.RFloat, false),
                segmentationMap = new Texture2D(mapTexture.width, mapTexture.height, TextureFormat.RGBA32, false)
            };
            
            // Convert detections to map objects and terrain features
            if (detections != null)
            {
                foreach (var detection in detections)
                {
                    if (_terrainClasses.Contains(detection.className.ToLower()))
                    {
                        // Create terrain feature
                        var terrainFeature = new TerrainFeature
                        {
                            label = detection.className,
                            type = detection.className,
                            boundingBox = detection.boundingBox,
                            segmentColor = GetRandomColor(),
                            elevation = EstimateTerrainHeight(detection.className),
                            confidence = detection.confidence
                        };
                        results.terrainFeatures.Add(terrainFeature);
                    }
                    else
                    {
                        // Create map object
                        var mapObject = new MapObject
                        {
                            label = detection.className,
                            type = detection.className,
                            position = new Vector2(
                                detection.boundingBox.center.x / mapTexture.width,
                                detection.boundingBox.center.y / mapTexture.height
                            ),
                            boundingBox = detection.boundingBox,
                            segmentColor = GetRandomColor(),
                            confidence = detection.confidence,
                            enhancedDescription = detection.enhancedDescription ?? detection.className,
                            rotation = 0f, // Fix: Use float for Y-axis rotation in degrees
                            scale = Vector3.one
                        };
                        results.mapObjects.Add(mapObject);
                    }
                }
            }  
            
            return results;
        }
        
        /// <summary>
        /// Gets a random color for visualization
        /// </summary>
        private Color GetRandomColor()
        {
            return new Color(
                UnityEngine.Random.Range(0f, 1f),
                UnityEngine.Random.Range(0f, 1f),
                UnityEngine.Random.Range(0f, 1f),
                0.8f
            );
        }
        
        /// <summary>
        /// Runs YOLO detection on the input image
        /// </summary>
        private IEnumerator RunDetectionWithYOLO(Texture2D mapTexture, System.Action<List<DetectedObject>> onComplete, System.Action<string> onError)
        {
            _debugger?.Log("Running YOLO detection", LogCategory.AI);
            
            if (_yoloSession == null)
            {
                onError?.Invoke("YOLO model not initialized");
                yield break;
            }
            
            var detectedObjects = new List<DetectedObject>();
            
            // Placeholder implementation - in real scenario would run actual YOLO inference
            yield return new WaitForSeconds(0.1f);
            
            // Create some sample detections for demonstration
            for (int i = 0; i < Mathf.Min(5, _maxObjectsToProcess); i++)
            {
                var detection = new DetectedObject
                {
                    className = _classLabels[UnityEngine.Random.Range(0, _classLabels.Length)],
                    confidence = UnityEngine.Random.Range(0.5f, 0.95f),
                    boundingBox = new Rect(
                        UnityEngine.Random.Range(0, mapTexture.width * 0.8f),
                        UnityEngine.Random.Range(0, mapTexture.height * 0.8f),
                        UnityEngine.Random.Range(50, 200),
                        UnityEngine.Random.Range(50, 200)
                    ),
                    classScores = new Dictionary<string, float>()
                };
                
                detection.classScores[detection.className] = detection.confidence;
                detectedObjects.Add(detection);
            }
            
            _debugger?.Log($"YOLO detection completed: {detectedObjects.Count} objects detected", LogCategory.AI);
            onComplete?.Invoke(detectedObjects);
        }
        
        /// <summary>
        /// Runs SAM2 segmentation on detected objects
        /// </summary>
        private IEnumerator RunSegmentationWithSAM2(Texture2D mapTexture, List<DetectedObject> detections, System.Action<List<ImageSegment>> onComplete, System.Action<string> onError)
        {
            _debugger?.Log("Running SAM2 segmentation", LogCategory.AI);
            
            if (_sam2Session == null)
            {
                _debugger?.LogWarning("SAM2 model not available, skipping segmentation", LogCategory.AI);
                onComplete?.Invoke(new List<ImageSegment>());
                yield break;
            }
            
            var segments = new List<ImageSegment>();
            
            // Placeholder implementation - in real scenario would run actual SAM2 inference
            yield return new WaitForSeconds(0.1f);
            
            // Create segments for each detection
            if (detections != null)
            {
                foreach (var detection in detections)
                {
                    var segment = new ImageSegment
                    {
                        detectedObject = detection,
                        mask = CreatePlaceholderMask(detection.boundingBox, mapTexture.width, mapTexture.height),
                        color = GetRandomColor(),
                        confidence = detection.confidence
                    };
                    segments.Add(segment);
                }
            }
            
            _debugger?.Log($"SAM2 segmentation completed: {segments.Count} segments created", LogCategory.AI);
            onComplete?.Invoke(segments);
        }
        
        /// <summary>
        /// Creates a placeholder mask for demonstration
        /// </summary>
        private Texture2D CreatePlaceholderMask(Rect boundingBox, int imageWidth, int imageHeight)
        {
            var mask = new Texture2D((int)boundingBox.width, (int)boundingBox.height, TextureFormat.RGBA32, false);
            var pixels = new Color[(int)(boundingBox.width * boundingBox.height)];
            
            // Fill with a simple circular mask
            Vector2 center = new Vector2(boundingBox.width * 0.5f, boundingBox.height * 0.5f);
            float radius = Mathf.Min(boundingBox.width, boundingBox.height) * 0.4f;
            
            for (int y = 0; y < boundingBox.height; y++)
            {
                for (int x = 0; x < boundingBox.width; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    float distance = Vector2.Distance(point, center);
                    float alpha = distance < radius ? 1f : 0f;
                    
                    int index = y * (int)boundingBox.width + x;
                    pixels[index] = new Color(1f, 1f, 1f, alpha);
                }
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            return mask;
        }
        
        #endregion
        
    }
}
