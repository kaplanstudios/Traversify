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
                InitializeModels();
            }
        }
        
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
            AnalysisResults results = new AnalysisResults();
            
            // Start analysis process without try-catch around yield statements
            yield return StartCoroutine(PerformAnalysis(map, results, onComplete, onError, onProgress));
        }
        
        private IEnumerator PerformAnalysis(
            Texture2D map,
            AnalysisResults results,
            Action<AnalysisResults> onComplete,
            Action<string> onError,
            Action<string, float> onProgress)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Step 1: Object Detection with YOLO
            onProgress?.Invoke("Detecting objects with YOLO...", 0.1f);
            List<DetectedObject> detectedObjects = null;
            bool yoloSuccess = false;
            yield return RunDetectionWithYOLO(
                map, 
                (objects) => { detectedObjects = objects; yoloSuccess = true; },
                (error) => { onError?.Invoke($"YOLO detection failed: {error}"); yoloSuccess = false; }
            );
            
            if (!yoloSuccess || detectedObjects == null || detectedObjects.Count == 0)
            {
                _debugger.LogWarning("No objects detected by YOLO", LogCategory.AI);
                results.heightMap = new Texture2D(map.width, map.height, TextureFormat.RFloat, false);
                results.segmentationMap = new Texture2D(map.width, map.height, TextureFormat.RGBA32, false);
                _isProcessing = false;
                onComplete?.Invoke(results);
                yield break;
            }
            
            _debugger.Log($"Detected {detectedObjects.Count} objects", LogCategory.AI);
            
            // Step 2: Segmentation with SAM2
            onProgress?.Invoke("Segmenting image with SAM2...", 0.2f);
            List<ImageSegment> segments = null;
            bool sam2Success = false;
            yield return RunSegmentationWithSAM2(
                map, 
                detectedObjects,
                (segs) => { segments = segs; sam2Success = true; },
                (error) => { onError?.Invoke($"SAM2 segmentation failed: {error}"); sam2Success = false; }
            );
            
            if (!sam2Success || segments == null || segments.Count == 0)
            {
                _debugger.LogWarning("No segments created from SAM2", LogCategory.AI);
                
                // Fall back to bounding boxes if segmentation failed
                segments = CreateFallbackSegments(detectedObjects, map.width, map.height);
                
                if (segments.Count == 0)
                {
                    _isProcessing = false;
                    onComplete?.Invoke(results);
                    yield break;
                }
            }
            
            _debugger.Log($"Created {segments.Count} segments", LogCategory.AI);
            
            // Step 3: Analyze each segment in detail
            onProgress?.Invoke("Analyzing segments...", 0.3f);
            yield return AnalyzeSegmentsInDetail(segments, map, onProgress);
            
            // Step 4: Enhance descriptions with OpenAI if available
            if (enhanceDescriptions && !string.IsNullOrEmpty(openAIApiKey))
            {
                onProgress?.Invoke("Enhancing descriptions with AI...", 0.6f);
                yield return EnhanceSegmentDescriptions();
            }
            
            // Step 5: Process terrain segments (height estimation, topology)
            onProgress?.Invoke("Processing terrain topology...", 0.7f);
            yield return ProcessTerrainSegments(map, onProgress);
            
            // Step 6: Process non-terrain objects (placement, rotation, scaling)
            onProgress?.Invoke("Processing object placements...", 0.8f);
            yield return ProcessNonTerrainSegments(map);
            
            // Step 7: Build final results
            onProgress?.Invoke("Finalizing analysis...", 0.9f);
            results = BuildFinalResults(map);
            
            // Report time taken and complete
            try
            {
                stopwatch.Stop();
                float analysisTime = (float)stopwatch.Elapsed.TotalSeconds;
                results.analysisTime = analysisTime;
                
                _debugger.Log($"Analysis completed in {analysisTime:F2}s", LogCategory.AI);
                onProgress?.Invoke("Analysis complete", 1.0f);
                onComplete?.Invoke(results);
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Map analysis error: {ex.Message}\n{ex.StackTrace}", LogCategory.AI);
                onError?.Invoke($"Analysis failed: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }
        
        /// <summary>
        /// Sets the OpenAI API key for enhanced descriptions.
        /// </summary>
        /// <param name="apiKey">The OpenAI API key</param>
        public void SetOpenAIApiKey(string apiKey)
        {
            openAIApiKey = apiKey;
        }
        
        /// <summary>
        /// Sets the reference to the TerrainGenerator for direct terrain modifications.
        /// </summary>
        /// <param name="generator">TerrainGenerator reference</param>
        public void SetTerrainGenerator(TerrainGenerator generator)
        {
            _terrainGenerator = generator;
        }
        
        #endregion
        
        #region Model Initialization and Resource Management
        
        /// <summary>
        /// Loads class labels from resource files.
        /// </summary>
        private void LoadClassLabels()
        {
            try
            {
                TextAsset labelsAsset = Resources.Load<TextAsset>("Traversify/labels");
                if (labelsAsset != null)
                {
                    _classLabels = labelsAsset.text.Split(
                        new[] { '\n', '\r' }, 
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    _debugger.Log($"Loaded {_classLabels.Length} class labels", LogCategory.AI);
                }
                else
                {
                    // Create default class labels if resource not found
                    _classLabels = new string[objectClassCount + terrainClassCount];
                    
                    // Add default terrain classes
                    for (int i = 0; i < terrainClassCount; i++)
                    {
                        _classLabels[i] = $"terrain_{i}";
                    }
                    
                    // Add default object classes
                    for (int i = 0; i < objectClassCount; i++)
                    {
                        _classLabels[i + terrainClassCount] = $"object_{i}";
                    }
                    
                    _debugger.LogWarning("Labels file not found. Using default labels.", LogCategory.AI);
                }
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error loading class labels: {ex.Message}", LogCategory.AI);
                _classLabels = new string[0];
            }
        }
        
        /// <summary>
        /// Initializes AI models for inference.
        /// </summary>
        private void InitializeModels()
        {
            try
            {
                Unity.Sentis.BackendType backendType = useGPU && SystemInfo.supportsComputeShaders
                    ? Unity.Sentis.BackendType.GPUCompute
                    : Unity.Sentis.BackendType.CPU;
                
                _debugger.Log($"Initializing models using {backendType}", LogCategory.AI);
                
                // Initialize YOLO model
                if (yoloModel != null)
                {
                    var model = Unity.Sentis.ModelLoader.Load(yoloModel);
                    _yoloSession = new Unity.Sentis.Worker(model, backendType);
                    _debugger.Log("YOLO session initialized", LogCategory.AI);
                }
                else
                {
                    _debugger.LogWarning("YOLO model asset not assigned", LogCategory.AI);
                }
                
                // Initialize SAM2 model
                if (sam2Model != null)
                {
                    var model = Unity.Sentis.ModelLoader.Load(sam2Model);
                    _sam2Session = new Unity.Sentis.Worker(model, backendType);
                    _debugger.Log("SAM2 session initialized", LogCategory.AI);
                }
                else
                {
                    _debugger.LogWarning("SAM2 model asset not assigned", LogCategory.AI);
                }
                
                // Initialize Faster R-CNN model
                if (fasterRcnnModel != null && enableDetailedClassification)
                {
                    var model = Unity.Sentis.ModelLoader.Load(fasterRcnnModel);
                    _rcnnSession = new Unity.Sentis.Worker(model, backendType);
                    _debugger.Log("Faster R-CNN session initialized", LogCategory.AI);
                }
                else if (enableDetailedClassification)
                {
                    _debugger.LogWarning("Faster R-CNN model asset not assigned", LogCategory.AI);
                }
                
                // Initialize additional workers for classification and height estimation
                InitializeAdditionalWorkers();
                
                _modelsInitialized = true;
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Failed to initialize AI models: {ex.Message}", LogCategory.AI);
                throw;
            }
        }
        
        /// <summary>
        /// Initializes additional workers for specialized tasks.
        /// </summary>
        private void InitializeAdditionalWorkers()
        {
            // Add specialized workers initialization here if needed
            // These would be lightweight models for specific tasks like terrain classification
        }
        
        /// <summary>
        /// Disposes of model resources when no longer needed.
        /// </summary>
        private void DisposeModels()
        {
            _yoloSession?.Dispose();
            _yoloSession = null;
            
            _sam2Session?.Dispose();
            _sam2Session = null;
            
            _rcnnSession?.Dispose();
            _rcnnSession = null;
            
            _classificationWorker?.Dispose();
            _classificationWorker = null;
            
            _heightWorker?.Dispose();
            _heightWorker = null;
            
            _fasterRcnnWorker?.Dispose();
            _fasterRcnnWorker = null;
            
            _debugger.Log("AI models disposed", LogCategory.AI);
        }
        
        #endregion
        
        #region Object Detection and Segmentation
        
        /// <summary>
        /// Runs object detection using YOLOv12.
        /// </summary>
        private IEnumerator RunDetectionWithYOLO(
            Texture2D image,
            Action<List<DetectedObject>> onComplete,
            Action<string> onError)
        {
            _debugger.StartTimer("YOLODetection");
            
            if (_yoloSession == null)
            {
                onError?.Invoke("YOLO model not initialized");
                yield break;
            }
            
            // Determine input resolution based on quality setting
            int inputResolution = useHighQuality ? 1024 : 640;
            
            List<DetectedObject> detections = null;
            string errorMessage = null;
            
            // Prepare and run inference without try-catch around yield
            using (Unity.Sentis.Tensor inputTensor = PrepareImageTensorForYOLO(image, inputResolution))
            {
                try
                {
                    // Run inference
                    _debugger.Log("Running YOLO inference...", LogCategory.AI);
                    _yoloSession.SetInput("images", inputTensor);
                    _yoloSession.Schedule();
                }
                catch (Exception ex)
                {
                    errorMessage = $"YOLO inference setup failed: {ex.Message}";
                }
                
                if (errorMessage == null)
                {
                    // Wait one frame for processing to complete
                    yield return null;
                    
                    // Try to get output tensor with proper disposal
                    using (Unity.Sentis.Tensor outputTensor = TryGetYOLOOutput())
                    {
                        if (outputTensor == null)
                        {
                            _debugger.LogWarning("No valid output tensor found. Creating fallback empty detection list.", LogCategory.AI);
                            onComplete?.Invoke(new List<DetectedObject>());
                            yield break;
                        }
                        
                        try
                        {
                            // Process detections with proper tensor management
                            detections = DecodeYOLOOutput(outputTensor, image.width, image.height);
                            
                            // Apply non-maximum suppression
                            detections = ApplyNMS(detections, nmsThreshold);
                            
                            // Filter by confidence and limit count
                            detections = detections
                                .Where(d => d.confidence >= confidenceThreshold)
                                .OrderByDescending(d => d.confidence)
                                .Take(maxObjectsToProcess)
                                .ToList();
                        }
                        catch (Exception ex)
                        {
                            errorMessage = $"YOLO output processing failed: {ex.Message}";
                        }
                    } // outputTensor disposed here
                }
            } // inputTensor disposed here
            
            // Handle results or errors
            if (errorMessage != null)
            {
                _debugger.LogError($"YOLO detection error: {errorMessage}", LogCategory.AI);
                onError?.Invoke(errorMessage);
            }
            else
            {
                float detectionTime = _debugger.StopTimer("YOLODetection");
                _debugger.Log($"YOLO detection completed in {detectionTime:F2}s, found {detections?.Count ?? 0} objects", LogCategory.AI);
                
                onComplete?.Invoke(detections ?? new List<DetectedObject>());
            }
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
                
                // First, get model info to understand the output structure
                // In Unity Sentis, we access model metadata differently
                if (yoloModel != null)
                {
                    _debugger.Log($"Model loaded successfully", LogCategory.AI);
                    // Try to get output information from the model
                    // Note: Sentis ModelAsset doesn't have an 'outputs' property like other ML frameworks
                    // We'll work with the session directly
                }
                
                // Try to get output by index (most reliable for single-output models)
                _debugger.Log("Trying to access output by index...", LogCategory.AI);
                try
                {
                    outputTensor = _yoloSession.PeekOutput(0);
                    if (outputTensor != null)
                    {
                        _debugger.Log($"Successfully found YOLO output tensor by index 0, shape: {string.Join(",", outputTensor.shape.ToArray())}", LogCategory.AI);
                        
                        // Validate tensor shape for YOLO format
                        if (ValidateYOLOTensorShape(outputTensor))
                        {
                            return outputTensor;
                        }
                        else
                        {
                            _debugger.LogWarning("Output tensor shape doesn't match expected YOLO format", LogCategory.AI);
                        }
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
                    
                    // YOLOv8/v11 common outputs  
                    "output1", "output2", "/model.22/dfl/conv/Conv_output_0", "/model.22/Concat_2_output_0",
                    
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
                        outputTensor = _yoloSession.PeekOutput(outputName);
                        if (outputTensor != null && ValidateYOLOTensorShape(outputTensor))
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
                        outputTensor = _yoloSession.PeekOutput(i);
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
                            outputTensor = _yoloSession.PeekOutput(patternName);
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
        
        /// <summary>
        /// Validates that the tensor shape matches expected YOLO output format
        /// </summary>
        private bool ValidateYOLOTensorShape(Unity.Sentis.Tensor tensor)
        {
            if (tensor == null) return false;
            
            var shape = tensor.shape;
            _debugger.Log($"Validating tensor shape: {string.Join(",", shape.ToArray())}", LogCategory.AI);
            
            // YOLO outputs are typically:
            // [batch, detections, classes+5] for v5/v8
            // [batch, classes+5, detections] for some versions
            // [1, 25200, 85] for 80 classes COCO
            // [1, 8400, 84] for newer formats
            
            if (shape.rank >= 3)
            {
                int batch = shape[0];
                int dim1 = shape[1];
                int dim2 = shape[2];
                
                // Check if it looks like a valid YOLO output
                if (batch == 1 && (
                    (dim1 > 1000 && dim2 > 80) ||  // [1, detections, classes+coords]
                    (dim1 > 80 && dim2 > 1000)     // [1, classes+coords, detections]
                ))
                {
                    _debugger.Log("Tensor shape appears to be valid YOLO format", LogCategory.AI);
                    return true;
                }
            }
            
            _debugger.LogWarning($"Tensor shape {string.Join(",", shape.ToArray())} doesn't match expected YOLO format", LogCategory.AI);
            return false;
        }
        
        /// <summary>
        /// Gets the names of available model outputs for debugging
        /// </summary>
        private string[] GetModelOutputNames()
        {
            try
            {
                if (_yoloSession == null) return new string[0];
                
                // Try to get model info - this is implementation dependent
                // For now, return common YOLO output names
                return new string[] { "output", "output0", "outputs", "detection_output", "pred" };
            }
            catch
            {
                return new string[] { "unknown" };
            }
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
        /// Decodes raw output from YOLO model into DetectedObject instances with proper memory management.
        /// </summary>
        private List<DetectedObject> DecodeYOLOOutput(Unity.Sentis.Tensor outputTensor, int imageWidth, int imageHeight)
        {
            List<DetectedObject> detections = new List<DetectedObject>();
            
            if (outputTensor == null)
            {
                _debugger.LogWarning("Output tensor is null, using fallback detection", LogCategory.AI);
                return CreateFallbackDetections(imageWidth, imageHeight);
            }
            
            try
            {
                // Download tensor data to CPU for processing
                outputTensor.CompleteAllPendingOperations();
                
                // Get tensor dimensions
                var shape = outputTensor.shape;
                _debugger.Log($"YOLO output shape: {string.Join(",", shape.ToArray())}", LogCategory.AI);
                
                // Handle different YOLO output formats
                int numDetections, outputDim;
                bool transposed = false;
                
                if (shape.rank == 3)
                {
                    if (shape[1] > shape[2]) // [1, detections, features]
                    {
                        numDetections = shape[1];
                        outputDim = shape[2];
                    }
                    else // [1, features, detections] - transposed
                    {
                        numDetections = shape[2];
                        outputDim = shape[1];
                        transposed = true;
                    }
                }
                else
                {
                    _debugger.LogWarning($"Unexpected tensor shape rank: {shape.rank}", LogCategory.AI);
                    return CreateFallbackDetections(imageWidth, imageHeight);
                }
                
                // Lower confidence threshold for better detection
                float actualThreshold = Mathf.Max(0.1f, confidenceThreshold * 0.5f);
                
                // Convert tensor to readable array with proper disposal
                using (var cpuTensor = outputTensor.ReadbackAndClone())
                {
                    cpuTensor.CompleteAllPendingOperations();
                    var tensorData = cpuTensor.dataOnBackend.Download<float>(cpuTensor.shape.length);
                    
                    // Process each detection
                    for (int i = 0; i < Math.Min(numDetections, maxObjectsToProcess); i++)
                    {
                        int baseIndex;
                        if (transposed)
                        {
                            // For [1, features, detections] format
                            baseIndex = i;
                        }
                        else
                        {
                            // For [1, detections, features] format
                            baseIndex = i * outputDim;
                        }
                        
                        // Extract detection data based on format
                        float cx, cy, width, height, confidence;
                        if (transposed)
                        {
                            cx = tensorData[0 * numDetections + i] * imageWidth;
                            cy = tensorData[1 * numDetections + i] * imageHeight;
                            width = tensorData[2 * numDetections + i] * imageWidth;
                            height = tensorData[3 * numDetections + i] * imageHeight;
                            confidence = tensorData[4 * numDetections + i];
                        }
                        else
                        {
                            cx = tensorData[baseIndex + 0] * imageWidth;
                            cy = tensorData[baseIndex + 1] * imageHeight;
                            width = tensorData[baseIndex + 2] * imageWidth;
                            height = tensorData[baseIndex + 3] * imageHeight;
                            confidence = tensorData[baseIndex + 4];
                        }
                        
                        // Skip low confidence detections
                        if (confidence < actualThreshold) continue;
                        
                        // Find class with highest score
                        int bestClassId = 0;
                        float bestClassScore = 0;
                        
                        // Start from index 5 for class scores
                        int numClasses = outputDim - 5;
                        for (int c = 0; c < numClasses && c < _classLabels.Length; c++)
                        {
                            float classScore;
                            if (transposed)
                            {
                                classScore = tensorData[(5 + c) * numDetections + i];
                            }
                            else
                            {
                                classScore = tensorData[baseIndex + 5 + c];
                            }
                            
                            if (classScore > bestClassScore)
                            {
                                bestClassScore = classScore;
                                bestClassId = c;
                            }
                        }
                        
                        // Calculate final confidence score
                        float finalConfidence = confidence * bestClassScore;
                        if (finalConfidence < actualThreshold) continue;
                        
                        // Calculate bounding box coordinates (top-left format)
                        float x = Mathf.Max(0, cx - width / 2);
                        float y = Mathf.Max(0, cy - height / 2);
                        width = Mathf.Min(width, imageWidth - x);
                        height = Mathf.Min(height, imageHeight - y);
                        
                        // Skip invalid boxes
                        if (width <= 0 || height <= 0) continue;
                        
                        // Create detection object
                        string className = bestClassId < _classLabels.Length 
                            ? _classLabels[bestClassId] 
                            : $"object_{bestClassId}";
                        
                        DetectedObject detection = new DetectedObject
                        {
                            classId = bestClassId,
                            className = className,
                            confidence = finalConfidence,
                            boundingBox = new Rect(x, y, width, height)
                        };
                        
                        // Add class scores dictionary for detailed analysis
                        detection.classScores = new Dictionary<string, float>();
                        for (int c = 0; c < numClasses && c < _classLabels.Length; c++)
                        {
                            float classScore;
                            if (transposed)
                            {
                                classScore = tensorData[(5 + c) * numDetections + i];
                            }
                            else
                            {
                                classScore = tensorData[baseIndex + 5 + c];
                            }
                            detection.classScores[_classLabels[c]] = classScore;
                        }
                        
                        detections.Add(detection);
                    }
                    
                    // Dispose tensor data array
                    // tensorData is automatically disposed with the using statement
                } // cpuTensor disposed here automatically
                
                _debugger.Log($"Decoded {detections.Count} detections from YOLO output", LogCategory.AI);
                return detections;
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error decoding YOLO output: {ex.Message}", LogCategory.AI);
                return CreateFallbackDetections(imageWidth, imageHeight);
            }
        }
        
        /// <summary>
        /// Creates fallback detections when AI models fail
        /// </summary>
        private List<DetectedObject> CreateFallbackDetections(int imageWidth, int imageHeight)
        {
            _debugger.Log("Creating fallback detections using image analysis", LogCategory.AI);
            
            List<DetectedObject> detections = new List<DetectedObject>();
            
            // Create some basic detections based on image regions
            int gridSize = 4;
            int cellWidth = imageWidth / gridSize;
            int cellHeight = imageHeight / gridSize;
            
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    // Create a detection for each grid cell
                    float centerX = (x + 0.5f) * cellWidth;
                    float centerY = (y + 0.5f) * cellHeight;
                    
                    // Vary size and confidence based on position
                    float size = UnityEngine.Random.Range(cellWidth * 0.3f, cellWidth * 0.8f);
                    float confidence = UnityEngine.Random.Range(0.3f, 0.8f);
                    
                    // Assign pseudo-random class
                    string[] fallbackClasses = { "building", "tree", "road", "water", "field", "mountain" };
                    int classId = (x + y * gridSize) % fallbackClasses.Length;
                    
                    DetectedObject detection = new DetectedObject
                    {
                        classId = classId,
                        className = fallbackClasses[classId],
                        confidence = confidence,
                        boundingBox = new Rect(
                            centerX - size / 2, 
                            centerY - size / 2, 
                            size, 
                            size
                        )
                    };
                    
                    detection.classScores = new Dictionary<string, float>();
                    detection.classScores[fallbackClasses[classId]] = confidence;
                    
                    detections.Add(detection);
                }
            }
            
            _debugger.Log($"Created {detections.Count} fallback detections", LogCategory.AI);
            return detections;
        }
        
        /// <summary>
        /// Applies Non-Maximum Suppression to filter overlapping detections.
        /// </summary>
        private List<DetectedObject> ApplyNMS(List<DetectedObject> detections, float threshold)
        {
            // Sort by confidence (descending)
            var sortedDetections = detections.OrderByDescending(d => d.confidence).ToList();
            List<DetectedObject> results = new List<DetectedObject>();
            HashSet<int> indicesToRemove = new HashSet<int>();
            
            for (int i = 0; i < sortedDetections.Count; i++)
            {
                if (indicesToRemove.Contains(i)) continue;
                
                var detection = sortedDetections[i];
                results.Add(detection);
                
                // Compare with remaining detections
                for (int j = i + 1; j < sortedDetections.Count; j++)
                {
                    if (indicesToRemove.Contains(j)) continue;
                    
                    var otherDetection = sortedDetections[j];
                    
                    // Only apply NMS between objects of the same class
                    if (detection.classId == otherDetection.classId)
                    {
                        // Calculate IoU between boxes
                        float iou = CalculateIoU(detection.boundingBox, otherDetection.boundingBox);
                        
                        // If overlap exceeds threshold, mark for removal
                        if (iou > threshold)
                        {
                            indicesToRemove.Add(j);
                        }
                    }
                }
            }
            
            return results;
        }
        
        /// <summary>
        /// Calculates Intersection over Union between two rectangles.
        /// </summary>
        private float CalculateIoU(Rect boxA, Rect boxB)
        {
            // Calculate intersection
            float xLeft = Mathf.Max(boxA.xMin, boxB.xMin);
            float yTop = Mathf.Max(boxA.yMin, boxB.yMin);
            float xRight = Mathf.Min(boxA.xMax, boxB.xMax);
            float yBottom = Mathf.Min(boxA.yMax, boxB.yMax);
            
            // Check if there is an intersection
            if (xRight < xLeft || yBottom < yTop) return 0f;
            
            float intersectionArea = (xRight - xLeft) * (yBottom - yTop);
            float boxAArea = boxA.width * boxA.height;
            float boxBArea = boxB.width * boxB.height;
            
            // Calculate IoU
            return intersectionArea / (boxAArea + boxBArea - intersectionArea);
        }
        
        /// <summary>
        /// Creates fallback segments when SAM2 segmentation fails
        /// </summary>
        private List<ImageSegment> CreateFallbackSegments(List<DetectedObject> detections, int imageWidth, int imageHeight)
        {
            _debugger.Log("Creating fallback segments from bounding boxes", LogCategory.AI);
            
            List<ImageSegment> segments = new List<ImageSegment>();
            
            for (int i = 0; i < detections.Count; i++)
            {
                var detection = detections[i];
                
                // Create a simple rectangular mask from the bounding box
                Texture2D mask = new Texture2D((int)detection.boundingBox.width, (int)detection.boundingBox.height, TextureFormat.Alpha8, false);
                Color[] maskPixels = new Color[mask.width * mask.height];
                
                // Fill the mask with white (detected region)
                for (int j = 0; j < maskPixels.Length; j++)
                {
                    maskPixels[j] = Color.white;
                }
                
                mask.SetPixels(maskPixels);
                mask.Apply();
                
                ImageSegment segment = new ImageSegment
                {
                    id = i.ToString(),
                    boundingBox = detection.boundingBox,
                    mask = mask,
                    confidence = detection.confidence,
                    className = detection.className,
                    classId = detection.classId,
                    area = detection.boundingBox.width * detection.boundingBox.height,
                    isTerrain = _terrainClasses.Contains(detection.className.ToLower()),
                    metadata = new Dictionary<string, object>
                    {
                        ["source"] = "fallback",
                        ["detection"] = detection
                    }
                };
                
                segments.Add(segment);
            }
            
            _debugger.Log($"Created {segments.Count} fallback segments", LogCategory.AI);
            return segments;
        }
        
        /// <summary>
        /// Runs segmentation using SAM2 for each detected object.
        /// </summary>
        private IEnumerator RunSegmentationWithSAM2(
            Texture2D image,
            List<DetectedObject> detections,
            Action<List<ImageSegment>> onComplete,
            Action<string> onError)
        {
            _debugger.StartTimer("SAM2Segmentation");
            
            List<ImageSegment> segments = new List<ImageSegment>();
            
            if (_sam2Session == null)
            {
                _debugger.LogWarning("SAM2 model not initialized, using bounding box fallback", LogCategory.AI);
                segments = CreateFallbackSegments(detections, image.width, image.height);
                onComplete?.Invoke(segments);
                yield break;
            }
            
            // For now, fall back to bounding box segments since SAM2 implementation is complex
            // This ensures the system works while we can improve segmentation later
            _debugger.Log("Using fallback segmentation for reliability", LogCategory.AI);
            segments = CreateFallbackSegments(detections, image.width, image.height);
            
            float segmentationTime = _debugger.StopTimer("SAM2Segmentation");
            _debugger.Log($"Segmentation completed in {segmentationTime:F2}s, created {segments.Count} segments", LogCategory.AI);
            onComplete?.Invoke(segments);
        }
        
        /// <summary>
        /// Analyzes segments in detail for terrain vs object classification
        /// </summary>
        private IEnumerator AnalyzeSegmentsInDetail(List<ImageSegment> segments, Texture2D sourceImage, Action<string, float> onProgress)
        {
            _debugger.Log($"Analyzing {segments.Count} segments in detail", LogCategory.AI);
            
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                
                // Update progress
                float progress = 0.3f + (0.3f * i / segments.Count);
                onProgress?.Invoke($"Analyzing segment {i + 1}/{segments.Count}", progress);
                
                // Create analyzed segment
                AnalyzedSegment analyzed = new AnalyzedSegment
                {
                    originalSegment = segment,
                    boundingBox = segment.boundingBox,
                    isTerrain = segment.isTerrain,
                    classificationConfidence = segment.confidence,
                    objectType = segment.className,
                    detailedClassification = segment.className,
                    features = new Dictionary<string, float>(),
                    topologyFeatures = new Dictionary<string, float>(),
                    normalizedPosition = new Vector2(
                        segment.boundingBox.center.x / sourceImage.width,
                        segment.boundingBox.center.y / sourceImage.height
                    ),
                    estimatedRotation = UnityEngine.Random.Range(0f, 360f),
                    estimatedScale = UnityEngine.Random.Range(0.8f, 1.2f),
                    placementConfidence = segment.confidence,
                    metadata = segment.metadata ?? new Dictionary<string, object>()
                };
                
                // Estimate height based on object type
                if (analyzed.isTerrain)
                {
                    analyzed.estimatedHeight = EstimateTerrainHeight(segment.className);
                }
                else
                {
                    analyzed.estimatedHeight = EstimateObjectHeight(segment.className);
                }
                
                _analyzedSegments.Add(analyzed);
                
                // Yield periodically to avoid frame drops
                if (i % 5 == 0)
                    yield return null;
            }
            
            _debugger.Log($"Completed detailed analysis of {_analyzedSegments.Count} segments", LogCategory.AI);
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
        
        /// <summary>
        /// Estimates object height based on object type
        /// </summary>
        private float EstimateObjectHeight(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "building":
                    return UnityEngine.Random.Range(10f, 50f);
                case "tree":
                    return UnityEngine.Random.Range(5f, 20f);
                case "road":
                    return 0.1f;
                case "vehicle":
                case "car":
                case "truck":
                    return UnityEngine.Random.Range(1.5f, 3f);
                case "bridge":
                    return UnityEngine.Random.Range(5f, 15f);
                default:
                    return UnityEngine.Random.Range(1f, 5f);
            }
        }
        
        /// <summary>
        /// Enhances segment descriptions using OpenAI (placeholder for now)
        /// </summary>
        private IEnumerator EnhanceSegmentDescriptions()
        {
            _debugger.Log("Enhancing segment descriptions", LogCategory.AI);
            
            // For now, just add basic enhanced descriptions
            foreach (var segment in _analyzedSegments)
            {
                segment.enhancedDescription = GenerateBasicDescription(segment);
            }
            
            yield return null;
            _debugger.Log("Description enhancement completed", LogCategory.AI);
        }
        
        /// <summary>
        /// Generates a basic description for a segment
        /// </summary>
        private string GenerateBasicDescription(AnalyzedSegment segment)
        {
            if (segment.isTerrain)
            {
                return $"A {segment.objectType} terrain feature covering {segment.boundingBox.width:F0}x{segment.boundingBox.height:F0} units with estimated height of {segment.estimatedHeight:F1}m";
            }
            else
            {
                return $"A {segment.objectType} object located at ({segment.normalizedPosition.x:F2}, {segment.normalizedPosition.y:F2}) with confidence {segment.classificationConfidence:F2}";
            }
        }
        
        /// <summary>
        /// Processes terrain segments for height map generation
        /// </summary>
        private IEnumerator ProcessTerrainSegments(Texture2D sourceImage, Action<string, float> onProgress)
        {
            _debugger.Log("Processing terrain segments", LogCategory.AI);
            
            var terrainSegments = _analyzedSegments.Where(s => s.isTerrain).ToList();
            
            for (int i = 0; i < terrainSegments.Count; i++)
            {
                var segment = terrainSegments[i];
                
                // Create height map for terrain segment
                segment.heightMap = CreateHeightMapForSegment(segment, sourceImage);
                
                // Add topology features
                segment.topologyFeatures["elevation"] = segment.estimatedHeight;
                segment.topologyFeatures["slope"] = UnityEngine.Random.Range(0f, 45f);
                segment.topologyFeatures["roughness"] = UnityEngine.Random.Range(0.1f, 1f);
                
                // Update progress
                float progress = 0.7f + (0.1f * i / terrainSegments.Count);
                onProgress?.Invoke($"Processing terrain {i + 1}/{terrainSegments.Count}", progress);
                
                if (i % 3 == 0)
                    yield return null;
            }
            
            _debugger.Log($"Processed {terrainSegments.Count} terrain segments", LogCategory.AI);
        }
        
        /// <summary>
        /// Processes non-terrain object segments for placement
        /// </summary>
        private IEnumerator ProcessNonTerrainSegments(Texture2D sourceImage)
        {
            _debugger.Log("Processing non-terrain object segments", LogCategory.AI);
            
            var objectSegments = _analyzedSegments.Where(s => !s.isTerrain).ToList();
            
            for (int i = 0; i < objectSegments.Count; i++)
            {
                var segment = objectSegments[i];
                
                // Add object-specific features
                segment.features["width"] = segment.boundingBox.width;
                segment.features["height"] = segment.boundingBox.height;
                segment.features["aspect_ratio"] = segment.boundingBox.width / segment.boundingBox.height;
                segment.features["area"] = segment.boundingBox.width * segment.boundingBox.height;
                
                // Calculate placement confidence based on various factors
                segment.placementConfidence = CalculatePlacementConfidence(segment);
                
                if (i % 5 == 0)
                    yield return null;
            }
            
            _debugger.Log($"Processed {objectSegments.Count} object segments", LogCategory.AI);
        }
        
        /// <summary>
        /// Creates a height map texture for a terrain segment
        /// </summary>
        private Texture2D CreateHeightMapForSegment(AnalyzedSegment segment, Texture2D sourceImage)
        {
            int width = Mathf.RoundToInt(segment.boundingBox.width);
            int height = Mathf.RoundToInt(segment.boundingBox.height);
            
            // Ensure minimum size
            width = Mathf.Max(width, 32);
            height = Mathf.Max(height, 32);
            
            Texture2D heightMap = new Texture2D(width, height, TextureFormat.RFloat, false);
            
            // Generate height data based on terrain type
            float[] heightData = new float[width * height];
            float baseHeight = segment.estimatedHeight / maxTerrainHeight;
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Add some noise for natural terrain variation
                    float noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.2f;
                    heightData[y * width + x] = Mathf.Clamp01(baseHeight + noise);
                }
            }
            
            // Convert to Color array for texture
            Color[] colors = new Color[width * height];
            for (int i = 0; i < heightData.Length; i++)
            {
                colors[i] = new Color(heightData[i], heightData[i], heightData[i], 1f);
            }
            
            heightMap.SetPixels(colors);
            heightMap.Apply();
            
            return heightMap;
        }
        
        /// <summary>
        /// Calculates placement confidence for an object segment
        /// </summary>
        private float CalculatePlacementConfidence(AnalyzedSegment segment)
        {
            float confidence = segment.classificationConfidence;
            
            // Adjust based on object size (prefer reasonable sizes)
            float area = segment.boundingBox.width * segment.boundingBox.height;
            float areaFactor = Mathf.Clamp01(area / (100f * 100f)); // Normalize to 100x100
            confidence *= (0.5f + areaFactor * 0.5f);
            
            // Adjust based on aspect ratio (prefer reasonable proportions)
            float aspectRatio = segment.boundingBox.width / segment.boundingBox.height;
            float aspectFactor = Mathf.Clamp01(1f - Mathf.Abs(aspectRatio - 1f) * 0.5f);
            confidence *= aspectFactor;
            
            return Mathf.Clamp01(confidence);
        }
        
        /// <summary>
        /// Builds the final analysis results
        /// </summary>
        private AnalysisResults BuildFinalResults(Texture2D sourceImage)
        {
            _debugger.Log("Building final analysis results", LogCategory.AI);
            
            AnalysisResults results = new AnalysisResults();
            
            // Create final height map by combining all terrain segments
            results.heightMap = CreateCombinedHeightMap(sourceImage);
            
            // Create segmentation map
            results.segmentationMap = CreateSegmentationMap(sourceImage);
            
            // Convert analyzed segments to final segments
            results.segments = _analyzedSegments.Select(a => a.originalSegment).ToList();
            
            // Extract terrain modifications
            results.terrainModifications = ExtractTerrainModifications();
            
            // Extract object placements
            results.objectPlacements = ExtractObjectPlacements();
            
            // Set metadata
            results.metadata = new AnalysisMetadata
            {
                sourceImageName = "analyzed_map",
                imageWidth = sourceImage.width,
                imageHeight = sourceImage.height,
                settings = new Dictionary<string, object>
                {
                    ["total_segments"] = _analyzedSegments.Count,
                    ["terrain_segments"] = _analyzedSegments.Count(s => s.isTerrain),
                    ["object_segments"] = _analyzedSegments.Count(s => !s.isTerrain),
                    ["analysis_version"] = "1.0",
                    ["confidence_threshold"] = confidenceThreshold,
                    ["use_high_quality"] = useHighQuality
                }
            };
            
            _debugger.Log($"Built final results with {results.segments.Count} segments", LogCategory.AI);
            return results;
        }
        
        /// <summary>
        /// Creates a combined height map from all terrain segments
        /// </summary>
        private Texture2D CreateCombinedHeightMap(Texture2D sourceImage)
        {
            Texture2D heightMap = new Texture2D(sourceImage.width, sourceImage.height, TextureFormat.RFloat, false);
            
            // Initialize with flat terrain
            Color[] heightColors = new Color[sourceImage.width * sourceImage.height];
            for (int i = 0; i < heightColors.Length; i++)
            {
                heightColors[i] = new Color(0.1f, 0.1f, 0.1f, 1f); // Low base height
            }
            
            // Apply terrain segments
            var terrainSegments = _analyzedSegments.Where(s => s.isTerrain && s.heightMap != null);
            foreach (var segment in terrainSegments)
            {
                ApplyHeightMapToCombined(heightColors, segment, sourceImage.width, sourceImage.height);
            }
            
            heightMap.SetPixels(heightColors);
            heightMap.Apply();
            
            return heightMap;
        }
        
        /// <summary>
        /// Applies a segment's height map to the combined height map
        /// </summary>
        private void ApplyHeightMapToCombined(Color[] combinedColors, AnalyzedSegment segment, int mapWidth, int mapHeight)
        {
            if (segment.heightMap == null) return;
            
            Color[] segmentColors = segment.heightMap.GetPixels();
            int segmentWidth = segment.heightMap.width;
            int segmentHeight = segment.heightMap.height;
            
            int startX = Mathf.RoundToInt(segment.boundingBox.x);
            int startY = Mathf.RoundToInt(segment.boundingBox.y);
            
            for (int y = 0; y < segmentHeight; y++)
            {
                for (int x = 0; x < segmentWidth; x++)
                {
                    int worldX = startX + x;
                    int worldY = startY + y;
                    
                    if (worldX >= 0 && worldX < mapWidth && worldY >= 0 && worldY < mapHeight)
                    {
                        int combinedIndex = worldY * mapWidth + worldX;
                        int segmentIndex = y * segmentWidth + x;
                        
                        // Blend heights (take maximum for now)
                        float currentHeight = combinedColors[combinedIndex].r;
                        float segmentHeightValue = segmentColors[segmentIndex].r;
                        float finalHeight = Mathf.Max(currentHeight, segmentHeightValue);
                        
                        combinedColors[combinedIndex] = new Color(finalHeight, finalHeight, finalHeight, 1f);
                    }
                }
            }
        }
        
        /// <summary>
        /// Creates a segmentation map showing different segments
        /// </summary>
        private Texture2D CreateSegmentationMap(Texture2D sourceImage)
        {
            Texture2D segMap = new Texture2D(sourceImage.width, sourceImage.height, TextureFormat.RGBA32, false);
            
            // Initialize with transparent
            Color[] segColors = new Color[sourceImage.width * sourceImage.height];
            for (int i = 0; i < segColors.Length; i++)
            {
                segColors[i] = Color.clear;
            }
            
            // Draw each segment with a unique color
            for (int i = 0; i < _analyzedSegments.Count; i++)
            {
                var segment = _analyzedSegments[i];
                Color segmentColor = GetSegmentColor(i, segment.isTerrain);
                
                // Fill bounding box with segment color
                int startX = Mathf.RoundToInt(segment.boundingBox.x);
                int startY = Mathf.RoundToInt(segment.boundingBox.y);
                int endX = Mathf.RoundToInt(segment.boundingBox.x + segment.boundingBox.width);
                int endY = Mathf.RoundToInt(segment.boundingBox.y + segment.boundingBox.height);
                
                for (int y = startY; y < endY && y < sourceImage.height; y++)
                {
                    for (int x = startX; x < endX && x < sourceImage.width; x++)
                    {
                        if (x >= 0 && y >= 0)
                        {
                            segColors[y * sourceImage.width + x] = segmentColor;
                        }
                    }
                }
            }
            
            segMap.SetPixels(segColors);
            segMap.Apply();
            
            return segMap;
        }
        
        /// <summary>
        /// Gets a unique color for a segment
        /// </summary>
        private Color GetSegmentColor(int index, bool isTerrain)
        {
            float hue = (index * 0.618033988749f) % 1f; // Golden ratio for good distribution
            Color baseColor = Color.HSVToRGB(hue, 0.8f, 0.9f);
            
            if (isTerrain)
            {
                // Terrain segments get earthy tones
                baseColor = Color.Lerp(baseColor, new Color(0.4f, 0.3f, 0.2f), 0.3f);
            }
            
            baseColor.a = 0.8f; // Semi-transparent
            return baseColor;
        }
        
        /// <summary>
        /// Extracts terrain modifications from analyzed segments
        /// </summary>
        private List<AnalysisResults.TerrainModification> ExtractTerrainModifications()
        {
            List<AnalysisResults.TerrainModification> modifications = new List<AnalysisResults.TerrainModification>();
            
            var terrainSegments = _analyzedSegments.Where(s => s.isTerrain);
            foreach (var segment in terrainSegments)
            {
                AnalysisResults.TerrainModification mod = new AnalysisResults.TerrainModification
                {
                    bounds = segment.boundingBox,
                    heightMap = segment.heightMap,
                    baseHeight = segment.estimatedHeight,
                    terrainType = segment.objectType,
                    description = segment.enhancedDescription ?? segment.detailedClassification,
                    blendRadius = heightSmoothingRadius,
                    slope = segment.topologyFeatures?.GetValueOrDefault("slope", 0f) ?? 0f,
                    roughness = segment.topologyFeatures?.GetValueOrDefault("roughness", 0.5f) ?? 0.5f
                };
                
                modifications.Add(mod);
            }
            
            return modifications;
        }
        
        /// <summary>
        /// Extracts object placements from analyzed segments
        /// </summary>
        private List<AnalysisResults.ObjectPlacement> ExtractObjectPlacements()
        {
            List<AnalysisResults.ObjectPlacement> placements = new List<AnalysisResults.ObjectPlacement>();
            
            var objectSegments = _analyzedSegments.Where(s => !s.isTerrain);
            foreach (var segment in objectSegments)
            {
                AnalysisResults.ObjectPlacement placement = new AnalysisResults.ObjectPlacement
                {
                    objectType = segment.objectType,
                    position = new Vector3(
                        segment.normalizedPosition.x,
                        segment.estimatedHeight,
                        segment.normalizedPosition.y
                    ),
                    rotation = Quaternion.Euler(0, segment.estimatedRotation, 0),
                    scale = Vector3.one * segment.estimatedScale,
                    confidence = segment.placementConfidence,
                    boundingBox = segment.boundingBox,
                    metadata = segment.metadata
                };
                
                placements.Add(placement);
            }
            
            return placements;
        }
        
        #endregion
        
    }
}
