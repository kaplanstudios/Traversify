/*************************************************************************
 *  Traversify – MapAnalyzer.cs                                          *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-07-04                                                  *
 *  Desc   : Advanced terrain and object analysis system using YOLOv8,  *
 *           SAM2, and Faster R-CNN for high-precision detection,        *
 *           segmentation, and recognition of map elements with height   *
 *           estimation and OpenAI-enhanced descriptions.                *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Unity.Sentis;
using USentis = Unity.Sentis;

using Traversify.Core;
using Traversify.Terrain;

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
        [Tooltip("YOLOv8 object detection model")]
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
        
        #endregion
        
        #region Private Fields
        
        // Internal components & state
        private TraversifyDebugger _debugger;
        private IWorker _yoloWorker;
        private IWorker _sam2Worker;
        private IWorker _rcnnWorker;
        private Model _yoloModel;
        private Model _sam2Model;
        private Model _rcnnModel;
        
        // Processing state
        private bool _isProcessing = false;
        private bool _modelsInitialized = false;
        private List<DetectedObject> _currentDetections = new List<DetectedObject>();
        private List<ImageSegment> _currentSegments = new List<ImageSegment>();
        private Dictionary<string, string> _enhancedDescriptions = new Dictionary<string, string>();
        
        // Class mappings for terrain vs non-terrain
        private HashSet<string> _terrainClasses = new HashSet<string> {
            "mountain", "hill", "water", "lake", "river", "ocean", "forest", 
            "grassland", "desert", "snow", "ice", "terrain", "valley", "plateau",
            "cliff", "beach", "swamp", "tundra", "canyon", "mesa"
        };
        
        // Height estimation data
        private float[,] _heightMap;
        private Vector2Int _heightMapSize = new Vector2Int(512, 512);
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Represents an analyzed segment with detailed information.
        /// </summary>
        [System.Serializable]
        public class AnalyzedSegment
        {
            public ImageSegment originalSegment;
            public DetectedObject detectedObject;
            public bool isTerrain;
            public string objectType;
            public string shortDescription;
            public string enhancedDescription;
            public float estimatedHeight;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public float confidence;
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
                _debugger = GetComponent<TraversifyDebugger>();
                if (_debugger == null)
                {
                    _debugger = gameObject.AddComponent<TraversifyDebugger>();
                }
                
                InitializeModels();
                
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
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }
        
        private void OnDestroy()
        {
            if (_isProcessing)
            {
                _debugger?.LogWarning("MapAnalyzer destruction attempted during processing - marking for cleanup", LogCategory.System);
                return;
            }
            
            CleanupResources();
        }
        
        #endregion
        
        #region Model Initialization
        
        /// <summary>
        /// Initialize all AI models using Sentis.
        /// </summary>
        private void InitializeModels()
        {
            try
            {
                USentis.BackendType backend = useGPU ? USentis.BackendType.GPUCompute : USentis.BackendType.CPU;
                
                // Initialize YOLO model
                if (yoloModel != null)
                {
                    _yoloWorker = new SentisWorker(yoloModel, backend);
                    _debugger?.Log("YOLO model initialized", LogCategory.AI);
                }
                
                // Initialize SAM2 model
                if (sam2Model != null)
                {
                    _sam2Worker = new SentisWorker(sam2Model, backend);
                    _debugger?.Log("SAM2 model initialized", LogCategory.AI);
                }
                
                // Initialize Faster R-CNN model
                if (fasterRcnnModel != null)
                {
                    _rcnnWorker = new SentisWorker(fasterRcnnModel, backend);
                    _debugger?.Log("Faster R-CNN model initialized", LogCategory.AI);
                }
                
                _modelsInitialized = true;
                _debugger?.Log("All AI models initialized successfully", LogCategory.AI);
            }
            catch (Exception ex)
            {
                _debugger?.LogError($"Failed to initialize models: {ex.Message}", LogCategory.AI);
                _modelsInitialized = false;
            }
        }
        
        /// <summary>
        /// Initialize Sentis model workers if not already initialized.
        /// </summary>
        public void InitializeModelsIfNeeded()
        {
            if (_modelsInitialized) return;
            try {
                // Initialize YOLO worker
                if (_yoloWorker == null && yoloModel != null)
                    _yoloWorker = new SentisWorkerWrapper(new USentis.Worker(USentis.ModelLoader.Load(yoloModel), useGPU ? USentis.BackendType.GPUCompute : USentis.BackendType.CPU), null);
                // Initialize SAM2 worker
                if (_sam2Worker == null && sam2Model != null)
                    _sam2Worker = new SentisWorkerWrapper(new USentis.Worker(USentis.ModelLoader.Load(sam2Model), useGPU ? USentis.BackendType.GPUCompute : USentis.BackendType.CPU), null);
                // Initialize R-CNN worker
                if (_rcnnWorker == null && fasterRcnnModel != null)
                    _rcnnWorker = new SentisWorkerWrapper(new USentis.Worker(USentis.ModelLoader.Load(fasterRcnnModel), useGPU ? USentis.BackendType.GPUCompute : USentis.BackendType.CPU), null);
                _modelsInitialized = true;
            } catch (Exception ex) {
                _debugger.LogError($"Error initializing models: {ex.Message}", LogCategory.AI);
            }
        }
        
        #endregion
        
        #region Main Analysis Pipeline
        
        /// <summary>
        /// Main entry point for analyzing map images.
        /// Implements the complete 11-step pipeline.
        /// </summary>
        public IEnumerator AnalyzeMapImage(Texture2D mapTexture, System.Action<AnalysisResults> onComplete = null, System.Action<string> onError = null, System.Action<string, float> onProgress = null)
{
    if (mapTexture == null) { onError?.Invoke("Map texture is null"); yield break; }
    if (!_modelsInitialized) { onError?.Invoke("AI models not initialized"); yield break; }

    _isProcessing = true;
    _debugger?.Log("Starting map image analysis", LogCategory.AI);
    // Clear previous results
    _currentDetections.Clear(); _currentSegments.Clear(); _enhancedDescriptions.Clear();
    // Step 1: Preprocess
    onProgress?.Invoke("Starting image analysis...", 0.05f);
    var processedTexture = PreprocessImage(mapTexture);
    // Step 2: YOLO Detection
    onProgress?.Invoke("Detecting terrain and objects with YOLO...", 0.15f);
    yield return RunYOLODetection(processedTexture);
    if (_currentDetections.Count == 0) { onError?.Invoke("No objects detected in the image"); _isProcessing = false; yield break; }
    // Step 3: SAM2 Segmentation
    onProgress?.Invoke("Segmenting detected objects with SAM2...", 0.35f);
    yield return RunSAM2Segmentation(processedTexture, _currentDetections);
    // Step 4: Faster R-CNN Classification
    onProgress?.Invoke("Classifying objects with Faster R-CNN...", 0.55f);
    yield return RunFasterRCNNClassification(processedTexture, _currentDetections);
    // Step 5: Descriptions
    onProgress?.Invoke("Generating object descriptions...", 0.65f);
    GenerateShortDescriptions();
    // Step 6: OpenAI Enhancement
    onProgress?.Invoke("Enhancing descriptions with OpenAI...", 0.75f);
    yield return EnhanceDescriptionsWithOpenAI();
    // Step 7: Height estimation
    onProgress?.Invoke("Generating height estimation data...", 0.85f);
    GenerateHeightEstimationData(mapTexture);
    // Step 8: Build results
    onProgress?.Invoke("Building analysis results...", 0.95f);
    var results = BuildAnalysisResults(mapTexture);
    onProgress?.Invoke("Analysis complete", 1.0f);
    _debugger?.Log("Map image analysis completed successfully", LogCategory.AI);
    onComplete?.Invoke(results);
    _isProcessing = false;
    yield break;
}
// alias for legacy reference
public IEnumerator AnalyzeImage(Texture2D mapTexture, System.Action<AnalysisResults> onComplete = null, System.Action<string> onError = null, System.Action<string, float> onProgress = null)
{
    return AnalyzeMapImage(mapTexture, onComplete, onError, onProgress);
}
        
        #endregion
        
        #region Image Preprocessing
        
        /// <summary>
        /// Preprocess the input image for AI model consumption.
        /// </summary>
        private Texture2D PreprocessImage(Texture2D input)
        {
            // Resize to model input size (typically 640x640 for YOLOv8)
            int targetSize = 640;
            var resized = TextureUtils.ResizeTexture(input, targetSize, targetSize);
            
            _debugger?.Log($"Preprocessed image to {targetSize}x{targetSize}", LogCategory.AI);
            return resized;
        }
        
        #endregion
        
        #region YOLO Detection
        
        /// <summary>
        /// Run YOLO object detection on the preprocessed image.
        /// </summary>
        private IEnumerator RunYOLODetection(Texture2D image)
        {
            if (_yoloWorker == null)
            {
                _debugger?.LogError("YOLO worker not initialized", LogCategory.AI);
                yield break;
            }

            // Convert image to tensor
            using var inputTensor = TextureConverter.ToTensor(image, 640, 640, 3);
            // Prepare inputs dictionary
            _yoloWorker.Execute(new Dictionary<string, object> { { "input_0", inputTensor } });

            // Get output tensor
            var outputTensor = _yoloWorker.PeekOutput("output_0") as USentis.Tensor<float>;

            // Decode YOLO output
            var detections = DecodeYOLOOutput(outputTensor, image.width, image.height);

            // Apply NMS
            _currentDetections = ApplyNonMaximumSuppression(detections);

            // Dispose tensors
            inputTensor?.Dispose();
            outputTensor?.Dispose();

            _debugger?.Log($"YOLO detection completed with {_currentDetections.Count} objects", LogCategory.AI);

            yield return null;
        }
        
        /// <summary>
        /// Decode YOLO output tensor to detected objects.
        /// </summary>
        private List<DetectedObject> DecodeYOLOOutput(USentis.Tensor<float> output, int imageWidth, int imageHeight)
        {
            var detections = new List<DetectedObject>();
            
            // YOLOv8 output format: [batch, 84, 8400] where 84 = 4(bbox) + 80(classes)
            var outputArray = output.ReadbackAndClone();
            int numDetections = output.shape[2]; // 8400
            int numClasses = 80; // COCO classes
            
            for (int i = 0; i < numDetections; i++)
            {
                // Extract bbox and confidence
                float centerX = outputArray[i * 84 + 0];
                float centerY = outputArray[i * 84 + 1];
                float width = outputArray[i * 84 + 2];
                float height = outputArray[i * 84 + 3];
                
                // Find best class
                float maxConf = 0f;
                int bestClass = -1;
                
                for (int c = 0; c < numClasses; c++)
                {
                    float conf = outputArray[i * 84 + 4 + c];
                    if (conf > maxConf)
                    {
                        maxConf = conf;
                        bestClass = c;
                    }
                }
                
                if (maxConf > _confidenceThreshold)
                {
                    var detection = new DetectedObject
                    {
                        boundingBox = new Rect(
                            (centerX - width / 2) * imageWidth / 640f,
                            (centerY - height / 2) * imageHeight / 640f,
                            width * imageWidth / 640f,
                            height * imageHeight / 640f
                        ),
                        confidence = maxConf,
                        classId = bestClass,
                        className = GetClassName(bestClass)
                    };
                    
                    detections.Add(detection);
                }
            }
            
            return detections;
        }
        
        /// <summary>
        /// Apply Non-Maximum Suppression to remove duplicate detections.
        /// </summary>
        private List<DetectedObject> ApplyNonMaximumSuppression(List<DetectedObject> detections)
        {
            var result = new List<DetectedObject>();
            var sorted = detections.OrderByDescending(d => d.confidence).ToList();
            
            for (int i = 0; i < sorted.Count; i++)
            {
                bool keep = true;
                for (int j = 0; j < result.Count; j++)
                {
                    float iou = CalculateIoU(sorted[i].boundingBox, result[j].boundingBox);
                    if (iou > _nmsThreshold)
                    {
                        keep = false;
                        break;
                    }
                }
                
                if (keep)
                {
                    result.Add(sorted[i]);
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Calculate Intersection over Union between two bounding boxes.
        /// </summary>
        private float CalculateIoU(Rect box1, Rect box2)
        {
            float intersectionArea = Mathf.Max(0, Mathf.Min(box1.xMax, box2.xMax) - Mathf.Max(box1.xMin, box2.xMin)) *
                                   Mathf.Max(0, Mathf.Min(box1.yMax, box2.yMax) - Mathf.Max(box1.yMin, box2.yMin));
            
            float unionArea = box1.width * box1.height + box2.width * box2.height - intersectionArea;
            
            return unionArea > 0 ? intersectionArea / unionArea : 0;
        }
        
        #endregion
        
        #region SAM2 Segmentation
        
        /// <summary>
        /// Run SAM2 segmentation on detected objects.
        /// </summary>
        private IEnumerator RunSAM2Segmentation(Texture2D image, List<DetectedObject> detections)
        {
            if (_sam2Worker == null) { _debugger?.LogWarning("SAM2 worker not initialized, skipping segmentation", LogCategory.AI); yield break; }
            _debugger?.Log("Running SAM2 segmentation", LogCategory.AI);
            foreach (var detection in detections)
            {
                // Create prompt from bounding box center
                Vector2 promptPoint = new Vector2(
                    detection.boundingBox.center.x,
                    detection.boundingBox.center.y
                );
                
                // Run SAM2 with point prompt
                var segment = RunSAM2ForPoint(image, promptPoint, detection);
                if (segment != null)
                {
                    _currentSegments.Add(segment);
                }
                
                yield return null;
            }
            _debugger?.Log($"SAM2 segmentation completed with {_currentSegments.Count} segments", LogCategory.AI);
        }
        
        /// <summary>
        /// Run SAM2 for a single point prompt.
        /// </summary>
        private ImageSegment RunSAM2ForPoint(Texture2D image, Vector2 point, DetectedObject detection)
        {
            using var imageTensor = TextureConverter.ToTensor(image, 1024, 1024, 3);
            using var pointTensor = new Unity.Sentis.Tensor<float>(new TensorShape(1, 1, 2), new float[] { point.x, point.y });
            _sam2Worker.Execute(new Dictionary<string, object>
            {
                { "image", imageTensor },
                { "point_coords", pointTensor }
            });
            var maskTensor = _sam2Worker.PeekOutput("masks") as USentis.Tensor<float>;
            if (maskTensor != null)
            {
                var segment = new ImageSegment
                {
                    boundingBox = detection.boundingBox,
                    confidence = detection.confidence,
                    mask = ConvertTensorToTexture(maskTensor),
                    area = CalculateMaskArea(maskTensor)
                    // Note: center is computed from boundingBox automatically
                };
                
                maskTensor.Dispose();
                return segment;
            }
            return null;
        }
        
        #endregion
        
        #region Faster R-CNN Classification
        
        /// <summary>
        /// Run Faster R-CNN classification on detected objects.
        /// </summary>
        private IEnumerator RunFasterRCNNClassification(Texture2D image, List<DetectedObject> detections)
        {
            if (_rcnnWorker == null) { _debugger?.LogWarning("Faster R-CNN worker not initialized, using YOLO classifications", LogCategory.AI); yield break; }
            _debugger?.Log("Running Faster R-CNN classification", LogCategory.AI);
            foreach (var detection in detections)
            {
                // Extract region of interest
                var roi = ExtractROI(image, detection.boundingBox);
                
                // Classify with Faster R-CNN
                var classification = ClassifyWithFasterRCNN(roi);
                
                // Update detection with refined classification
                detection.className = classification.className;
                detection.confidence = Mathf.Max(detection.confidence, classification.confidence);
                
                yield return null;
            }
            _debugger?.Log("Faster R-CNN classification completed", LogCategory.AI);
        }
        
        /// <summary>
        /// Classify a region of interest with Faster R-CNN.
        /// </summary>
        private (string className, float confidence) ClassifyWithFasterRCNN(Texture2D roi)
        {
            // Convert ROI to tensor
            using var inputTensor = TextureConverter.ToTensor(roi, 224, 224, 3);
            
            // Execute Faster R-CNN
            _rcnnWorker.Execute(new Dictionary<string, object> { { "input_0", inputTensor } });
            
            // Get classification output
            var outputTensor = _rcnnWorker.PeekOutput("output_0") as USentis.Tensor<float>;
            
            // Find best class using proper Sentis API
            float maxConf = 0f;
            int bestClass = 0;
            
            // Iterate through tensor elements properly
            for (int i = 0; i < outputTensor.shape.length; i++)
            {
                float conf = outputTensor[i];
                if (conf > maxConf)
                {
                    maxConf = conf;
                    bestClass = i;
                }
            }
            
            outputTensor.Dispose();
            
            return (GetClassName(bestClass), maxConf);
        }
        
        #endregion
        
        #region Description Generation
        
        /// <summary>
        /// Generate short descriptions for detected objects.
        /// </summary>
        private void GenerateShortDescriptions()
        {
            foreach (var detection in _currentDetections)
            {
                // Generate basic description based on class and characteristics
                string description = GenerateBasicDescription(detection);
                detection.shortDescription = description;
            }
            
            _debugger?.Log("Generated short descriptions for all objects", LogCategory.AI);
        }
        
        /// <summary>
        /// Generate a basic description for a detected object.
        /// </summary>
        private string GenerateBasicDescription(DetectedObject detection)
        {
            bool isTerrain = _terrainClasses.Contains(detection.className.ToLower());
            
            if (isTerrain)
            {
                return $"{detection.className} terrain feature";
            }
            else
            {
                return $"{detection.className} object";
            }
        }
        
        /// <summary>
        /// Enhance descriptions using OpenAI API.
        /// </summary>
        private IEnumerator EnhanceDescriptionsWithOpenAI()
        {
            if (string.IsNullOrEmpty(_openAIApiKey) || !enhanceDescriptions)
            {
                _debugger?.Log("Skipping OpenAI enhancement (no API key or disabled)", LogCategory.AI);
                yield break;
            }
            
            foreach (var detection in _currentDetections)
            {
                yield return StartCoroutine(EnhanceDescriptionForObject(detection));
            }
            
            _debugger?.Log("Enhanced descriptions with OpenAI", LogCategory.AI);
        }
        
        /// <summary>
        /// Enhance description for a single object using OpenAI.
        /// </summary>
        private IEnumerator EnhanceDescriptionForObject(DetectedObject detection)
        {
            var openAIResponse = FindObjectOfType<OpenAIResponse>();
            if (openAIResponse == null)
            {
                _debugger?.LogWarning("OpenAIResponse component not found", LogCategory.AI);
                yield break;
            }
            
            string prompt = $"Enhance this object description for 3D model generation: '{detection.shortDescription}'. " +
                           "Provide a detailed description suitable for creating a 3D model, including materials, textures, and scale.";
            
            bool completed = false;
            string enhancedDesc = detection.shortDescription;
            
            yield return StartCoroutine(openAIResponse.GetCompletion(prompt, 
                (response) => {
                    enhancedDesc = response;
                    completed = true;
                },
                (error) => {
                    _debugger?.LogWarning($"OpenAI enhancement failed: {error}", LogCategory.AI);
                    completed = true;
                }));
            
            while (!completed)
            {
                yield return null;
            }
            
            _enhancedDescriptions[detection.className] = enhancedDesc;
            detection.enhancedDescription = enhancedDesc;
        }
        
        #endregion
        
        #region Height Estimation
        
        /// <summary>
        /// Generate height estimation data for terrain features.
        /// </summary>
        private void GenerateHeightEstimationData(Texture2D mapTexture)
        {
            _heightMap = new float[_heightMapSize.x, _heightMapSize.y];
            
            // Generate height map based on detected terrain features
            foreach (var detection in _currentDetections.Where(d => _terrainClasses.Contains(d.className.ToLower())))
            {
                ApplyHeightToMap(detection);
            }
            
            // Smooth height map
            SmoothHeightMap();
            
            _debugger?.Log("Generated height estimation data", LogCategory.AI);
        }
        
        /// <summary>
        /// Apply height influence from a terrain detection to the height map.
        /// </summary>
        private void ApplyHeightToMap(DetectedObject detection)
        {
            float height = EstimateHeightForTerrain(detection.className);
            
            // Convert bounding box to height map coordinates
            int startX = Mathf.FloorToInt((detection.boundingBox.x / _heightMapSize.x) * _heightMapSize.x);
            int startY = Mathf.FloorToInt((detection.boundingBox.y / _heightMapSize.y) * _heightMapSize.y);
            int endX = Mathf.CeilToInt(((detection.boundingBox.x + detection.boundingBox.width) / _heightMapSize.x) * _heightMapSize.x);
            int endY = Mathf.CeilToInt(((detection.boundingBox.y + detection.boundingBox.height) / _heightMapSize.y) * _heightMapSize.y);
            
            // Apply height within bounding box
            for (int x = Mathf.Max(0, startX); x < Mathf.Min(_heightMapSize.x, endX); x++)
            {
                for (int y = Mathf.Max(0, startY); y < Mathf.Min(_heightMapSize.y, endY); y++)
                {
                    _heightMap[x, y] = Mathf.Max(_heightMap[x, y], height);
                }
            }
        }
        
        /// <summary>
        /// Estimate height value for a terrain type.
        /// </summary>
        private float EstimateHeightForTerrain(string terrainType)
        {
            switch (terrainType.ToLower())
            {
                case "mountain": return maxTerrainHeight * 0.8f;
                case "hill": return maxTerrainHeight * 0.4f;
                case "plateau": return maxTerrainHeight * 0.6f;
                case "cliff": return maxTerrainHeight * 0.7f;
                case "water":
                case "lake":
                case "river":
                case "ocean": return 0f;
                default: return maxTerrainHeight * 0.2f;
            }
        }
        
        /// <summary>
        /// Smooth the height map to remove sharp transitions.
        /// </summary>
        private void SmoothHeightMap()
        {
            float[,] smoothed = new float[_heightMapSize.x, _heightMapSize.y];
            int kernelSize = 3;
            
            for (int x = 0; x < _heightMapSize.x; x++)
            {
                for (int y = 0; y < _heightMapSize.y; y++)
                {
                    float sum = 0f;
                    int count = 0;
                    
                    for (int kx = -kernelSize; kx <= kernelSize; kx++)
                    {
                        for (int ky = -kernelSize; ky <= kernelSize; ky++)
                        {
                            int nx = x + kx;
                            int ny = y + ky;
                            
                            if (nx >= 0 && nx < _heightMapSize.x && ny >= 0 && ny < _heightMapSize.y)
                            {
                                sum += _heightMap[nx, ny];
                                count++;
                            }
                        }
                    }
                    
                    smoothed[x, y] = count > 0 ? sum / count : 0f;
                }
            }
            
            _heightMap = smoothed;
        }
        
        #endregion
        
        #region Results Building
        
        /// <summary>
        /// Build the final analysis results.
        /// </summary>
        private AnalysisResults BuildAnalysisResults(Texture2D originalTexture)
        {
            var results = new AnalysisResults
            {
                sourceImage = originalTexture,
                mapObjects = ConvertDetectedObjectsToMapObjects(_currentDetections),
                segments = new List<ImageSegment>(_currentSegments),
                enhancedDescriptions = new Dictionary<string, string>(_enhancedDescriptions),
                heightMap = ConvertHeightMapToTexture(),
                terrainSize = new Vector2(_heightMapSize.x, _heightMapSize.y),
                maxHeight = maxTerrainHeight,
                analysisTimestamp = DateTime.Now
            };
            
            // Classify terrain vs objects
            foreach (var detection in _currentDetections)
            {
                bool isTerrain = _terrainClasses.Contains(detection.className.ToLower());
                if (isTerrain)
                {
                    results.terrainObjects.Add(detection);
                }
                else
                {
                    results.nonTerrainObjects.Add(detection);
                }
            }
            
            _debugger?.Log($"Analysis results: {results.terrainObjects.Count} terrain objects, {results.nonTerrainObjects.Count} non-terrain objects", LogCategory.AI);
            
            return results;
        }
        
        /// <summary>
        /// Convert DetectedObject list to MapObject list.
        /// </summary>
        private List<MapObject> ConvertDetectedObjectsToMapObjects(List<DetectedObject> detectedObjects)
        {
            var mapObjects = new List<MapObject>();
            
            foreach (var detected in detectedObjects)
            {
                var mapObject = new MapObject
                {
                    type = detected.className,
                    label = detected.className,
                    enhancedDescription = detected.enhancedDescription ?? detected.shortDescription ?? detected.className,
                    position = new Vector2(
                        detected.boundingBox.center.x / 640f, // Normalize to 0-1
                        detected.boundingBox.center.y / 640f
                    ),
                    boundingBox = detected.boundingBox,
                    confidence = detected.confidence,
                    scale = Vector3.one,
                    rotation = 0f,
                    id = System.Guid.NewGuid().ToString(),
                    importanceScore = detected.confidence,
                    heightMeters = EstimateObjectHeight(detected.className),
                    widthMeters = EstimateObjectWidth(detected.className),
                    castsShadows = !_terrainClasses.Contains(detected.className.ToLower())
                };
                
                // Set metadata
                mapObject.metadata = new Dictionary<string, object>
                {
                    ["originalClassId"] = detected.classId,
                    ["detectionSource"] = "YOLO+SAM2+FasterRCNN",
                    ["isTerrain"] = _terrainClasses.Contains(detected.className.ToLower())
                };
                
                // Add material suggestions based on object type
                mapObject.materials = GetMaterialsForObjectType(detected.className);
                
                mapObjects.Add(mapObject);
            }
            
            return mapObjects;
        }
        
        /// <summary>
        /// Estimate object height based on type.
        /// </summary>
        private float EstimateObjectHeight(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "building": return 15f;
                case "house": return 8f;
                case "tree": return 12f;
                case "car": return 1.5f;
                case "truck": return 3f;
                case "person": return 1.7f;
                case "tower": return 30f;
                case "bridge": return 20f;
                default: return 2f;
            }
        }
        
        /// <summary>
        /// Estimate object width based on type.
        /// </summary>
        private float EstimateObjectWidth(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "building": return 20f;
                case "house": return 10f;
                case "tree": return 3f;
                case "car": return 2f;
                case "truck": return 3f;
                case "person": return 0.6f;
                case "tower": return 5f;
                case "bridge": return 8f;
                default: return 1f;
            }
        }
        
        /// <summary>
        /// Get suggested materials for object type.
        /// </summary>
        private List<string> GetMaterialsForObjectType(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "building":
                case "house":
                    return new List<string> { "concrete", "brick", "glass" };
                case "tree":
                    return new List<string> { "wood", "leaves" };
                case "car":
                case "truck":
                    return new List<string> { "metal", "glass", "rubber" };
                case "bridge":
                    return new List<string> { "steel", "concrete" };
                case "tower":
                    return new List<string> { "steel", "metal" };
                default:
                    return new List<string> { "generic" };
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Get class name from class ID.
        /// </summary>
        private string GetClassName(int classId)
        {
            // COCO class names - simplified version
            string[] cocoClasses = {
                "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat", "traffic light",
                "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat", "dog", "horse", "sheep", "cow",
                "elephant", "bear", "zebra", "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
                "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove", "skateboard", "surfboard",
                "tennis racket", "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
                "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch",
                "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard", "cell phone",
                "microwave", "oven", "toaster", "sink", "refrigerator", "book", "clock", "vase", "scissors", "teddy bear",
                "hair drier", "toothbrush"
            };
            
            return classId >= 0 && classId < cocoClasses.Length ? cocoClasses[classId] : "unknown";
        }
        
        /// <summary>
        /// Extract region of interest from image.
        /// </summary>
        private Texture2D ExtractROI(Texture2D source, Rect bbox)
        {
            int x = Mathf.FloorToInt(bbox.x);
            int y = Mathf.FloorToInt(bbox.y);
            int width = Mathf.CeilToInt(bbox.width);
            int height = Mathf.CeilToInt(bbox.height);
            
            // Clamp to image bounds
            x = Mathf.Max(0, x);
            y = Mathf.Max(0, y);
            width = Mathf.Min(source.width - x, width);
            height = Mathf.Min(source.height - y, height);
            
            var pixels = source.GetPixels(x, y, width, height);
            var roi = new Texture2D(width, height);
            roi.SetPixels(pixels);
            roi.Apply();
            
            return roi;
        }
        
        /// <summary>
        /// Convert tensor to texture (for mask visualization).
        /// </summary>
        private Texture2D ConvertTensorToTexture(USentis.Tensor<float> tensor)
        {
            var shape = tensor.shape;
            int width = shape[3];
            int height = shape[2];
            
            var texture = new Texture2D(width, height, TextureFormat.R8, false);
            var data = tensor.ReadbackAndClone();
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = data[y * width + x];
                    texture.SetPixel(x, y, new Color(value, value, value, 1f));
                }
            }
            
            texture.Apply();
            return texture;
        }
        
        /// <summary>
        /// Calculate mask area from tensor.
        /// </summary>
        private float CalculateMaskArea(USentis.Tensor<float> maskTensor)
        {
            float area = 0f;
            
            // Iterate through tensor elements using proper Sentis API
            for (int i = 0; i < maskTensor.shape.length; i++)
            {
                float pixel = maskTensor[i];
                if (pixel > 0.5f) area += 1f;
            }
            
            return area;
        }
        
        /// <summary>
        /// Calculate mask centroid from tensor.
        /// </summary>
        private Vector2 CalculateMaskCentroid(USentis.Tensor<float> maskTensor)
        {
            var shape = maskTensor.shape;
            int width = shape[3];
            int height = shape[2];
            var data = maskTensor.ReadbackAndClone();
            
            float totalX = 0f, totalY = 0f, totalWeight = 0f;
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float weight = data[y * width + x];
                    if (weight > 0.5f)
                    {
                        totalX += x * weight;
                        totalY += y * weight;
                        totalWeight += weight;
                    }
                }
            }
            
            if (totalWeight > 0)
            {
                return new Vector2(totalX / totalWeight, totalY / totalWeight);
            }
            
            return Vector2.zero;
        }
        
        /// <summary>
        /// Log message using the debugger component.
        /// </summary>
        private void Log(string message, LogCategory category)
        {
            _debugger?.Log(message, category);
        }
        
        /// <summary>
        /// Log error message using the debugger component.
        /// </summary>
        private void LogError(string message, LogCategory category)
        {
            _debugger?.LogError(message, category);
        }
        
        #endregion
        
        #region Cleanup
        
        /// <summary>
        /// Clean up all resources.
        /// </summary>
        private void CleanupResources()
        {
            try
            {
                _yoloWorker?.Dispose();
                _sam2Worker?.Dispose();
                _rcnnWorker?.Dispose();
                
                _yoloWorker = null;
                _sam2Worker = null;
                _rcnnWorker = null;
                
                _modelsInitialized = false;
                
                _debugger?.Log("MapAnalyzer resources cleaned up", LogCategory.System);
            }
            catch (Exception ex)
            {
                _debugger?.LogError($"Error during cleanup: {ex.Message}", LogCategory.System);
            }
        }
        
        #endregion

        /// <summary>
        /// Convert height map array to texture.
        /// </summary>
        private Texture2D ConvertHeightMapToTexture()
        {
            var texture = new Texture2D(_heightMapSize.x, _heightMapSize.y, TextureFormat.RFloat, false);
            
            for (int x = 0; x < _heightMapSize.x; x++)
            {
                for (int y = 0; y < _heightMapSize.y; y++)
                {
                    float normalizedHeight = _heightMap[x, y] / maxTerrainHeight;
                    texture.SetPixel(x, y, new Color(normalizedHeight, normalizedHeight, normalizedHeight, 1f));
                }
            }
            
            texture.Apply();
            return texture;
        }
    }
}
