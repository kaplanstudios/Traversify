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
        [SerializeField] private Unity.InferenceEngine.ModelAsset yoloModel;
        
        [Tooltip("SAM2 segmentation model")]
        [SerializeField] private Unity.InferenceEngine.ModelAsset sam2Model;
        
        [Tooltip("Faster R-CNN classification model")]
        [SerializeField] private Unity.InferenceEngine.ModelAsset fasterRcnnModel;
        
        [Header("Detection Settings")]
        [Tooltip("Detection confidence threshold (0-1)")]
        [Range(0f, 1f)] 
        [SerializeField] private float _confidenceThreshold = 0.5f;
        
        [Tooltip("Non-maximum suppression threshold (0-1)")]
        [Range(0f, 1f)] 
        [SerializeField] private float _nmsThreshold = 0.45f;
        
        [Tooltip("Use higher resolution analysis for better results")]
        [SerializeField] private bool _useHighQuality = true;
        
        [Tooltip("Use GPU acceleration if available")]
        [SerializeField] private bool _useGPU = true;
        
        [Tooltip("Maximum objects to process in a single analysis")]
        [SerializeField] private int _maxObjectsToProcess = 100;
        
        [Header("OpenAI Settings")]
        [Tooltip("API key for description enhancement")]
        [SerializeField] private string _openAIApiKey;
        
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
        private IWorker _yoloSession;
        private IWorker _sam2Session;
        private IWorker _rcnnSession;
        private string[] _classLabels;
        private IWorker _classificationWorker;
        private IWorker _heightWorker;
        private IWorker _fasterRcnnWorker;
        
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
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // Singleton enforcement
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize components
            _debugger = GetComponent<TraversifyDebugger>();
            if (_debugger == null)
            {
                _debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            _terrainGenerator = FindObjectOfType<TerrainGenerator>();
            
            LoadClassLabels();
            InitializeModels();
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
                Unity.InferenceEngine.BackendType backendType = useGPU && SystemInfo.supportsComputeShaders
                    ? Unity.InferenceEngine.BackendType.GPUCompute
                    : Unity.InferenceEngine.BackendType.CPU;
                
                _debugger.Log($"Initializing models using {backendType}", LogCategory.AI);
                
                // Initialize YOLO model
                if (yoloModel != null)
                {
                    _yoloSession = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, new NNModel { modelName = yoloModel.name });
                    _debugger.Log("YOLO session initialized", LogCategory.AI);
                }
                else
                {
                    _debugger.LogWarning("YOLO model asset not assigned", LogCategory.AI);
                }
                
                // Initialize SAM2 model
                if (sam2Model != null)
                {
                    _sam2Session = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, new NNModel { modelName = sam2Model.name });
                    _debugger.Log("SAM2 session initialized", LogCategory.AI);
                }
                else
                {
                    _debugger.LogWarning("SAM2 model asset not assigned", LogCategory.AI);
                }
                
                // Initialize Faster R-CNN model
                if (fasterRcnnModel != null && enableDetailedClassification)
                {
                    _rcnnSession = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, new NNModel { modelName = fasterRcnnModel.name });
                    _debugger.Log("Faster R-CNN session initialized", LogCategory.AI);
                }
                else if (enableDetailedClassification)
                {
                    _debugger.LogWarning("Faster R-CNN model asset not assigned", LogCategory.AI);
                }
                
                // Initialize additional workers for classification and height estimation
                InitializeAdditionalWorkers();
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
            try
            {
                _debugger.StartTimer("YOLODetection");
                
                if (_yoloSession == null)
                {
                    onError?.Invoke("YOLO model not initialized");
                    yield break;
                }
                
                // Determine input resolution based on quality setting
                int inputResolution = useHighQuality ? 1024 : 640;
                
                // Preprocess image to tensor
                Tensor inputTensor = TensorUtils.Preprocess(image, inputResolution);
                
                // Run inference
                _debugger.Log("Running YOLO inference...", LogCategory.AI);
                _yoloSession.Run(new Dictionary<string, Tensor> { { "images", inputTensor } });
                
                // Get output tensor
                Tensor outputTensor = _yoloSession.Fetch("output");
                
                // Process detections
                List<DetectedObject> detections = DecodeYOLOOutput(
                    outputTensor, 
                    image.width, 
                    image.height
                );
                
                // Apply non-maximum suppression
                detections = ApplyNMS(detections, nmsThreshold);
                
                // Filter by confidence and limit count
                detections = detections
                    .Where(d => d.confidence >= confidenceThreshold)
                    .OrderByDescending(d => d.confidence)
                    .Take(maxObjectsToProcess)
                    .ToList();
                
                // Clean up
                inputTensor.Dispose();
                outputTensor.Dispose();
                
                float detectionTime = _debugger.StopTimer("YOLODetection");
                _debugger.Log($"YOLO detection completed in {detectionTime:F2}s, found {detections.Count} objects", LogCategory.AI);
                
                onComplete?.Invoke(detections);
            }
            catch (Exception ex)
            {
                _debugger.LogError($"YOLO detection error: {ex.Message}", LogCategory.AI);
                onError?.Invoke(ex.Message);
            }
        }
        
        /// <summary>
        /// Decodes raw output from YOLO model into DetectedObject instances.
        /// </summary>
        private List<DetectedObject> DecodeYOLOOutput(Tensor outputTensor, int imageWidth, int imageHeight)
        {
            List<DetectedObject> detections = new List<DetectedObject>();
            
            // Get tensor dimensions
            int numDetections = outputTensor.shape[1];
            int outputDim = outputTensor.shape[2];
            
            // Process each detection
            for (int i = 0; i < numDetections; i++)
            {
                // YOLO output format: [cx, cy, width, height, confidence, class_scores...]
                float cx = outputTensor[0, i, 0] * imageWidth;
                float cy = outputTensor[0, i, 1] * imageHeight;
                float width = outputTensor[0, i, 2] * imageWidth;
                float height = outputTensor[0, i, 3] * imageHeight;
                float confidence = outputTensor[0, i, 4];
                
                // Skip low confidence detections early
                if (confidence < confidenceThreshold) continue;
                
                // Find class with highest score
                int bestClassId = 0;
                float bestClassScore = 0;
                
                // Start from index 5 for class scores
                for (int c = 5; c < outputDim; c++)
                {
                    float classScore = outputTensor[0, i, c];
                    if (classScore > bestClassScore)
                    {
                        bestClassScore = classScore;
                        bestClassId = c - 5; // Adjust for offset
                    }
                }
                
                // Calculate bounding box coordinates (top-left format)
                float x = cx - width / 2;
                float y = cy - height / 2;
                
                // Create detection object
                string className = bestClassId < _classLabels.Length 
                    ? _classLabels[bestClassId] 
                    : $"class_{bestClassId}";
                
                DetectedObject detection = new DetectedObject
                {
                    classId = bestClassId,
                    className = className,
                    confidence = confidence * bestClassScore, // Combined confidence
                    boundingBox = new Rect(x, y, width, height)
                };
                
                // Add class scores dictionary for more detailed analysis
                detection.classScores = new Dictionary<string, float>();
                for (int c = 5; c < outputDim; c++)
                {
                    int classId = c - 5;
                    if (classId < _classLabels.Length)
                    {
                        detection.classScores[_classLabels[classId]] = outputTensor[0, i, c];
                    }
                }
                
                detections.Add(detection);
            }
            
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
        /// Runs segmentation using SAM2 for each detected object.
        /// </summary>
        private IEnumerator RunSegmentationWithSAM2(
            Texture2D image,
            List<DetectedObject> detections,
            Action<List<ImageSegment>> onComplete,
            Action<string> onError)
        {
            try
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
                
                // Determine batch size
                int batchSize = processingBatchSize;
                int totalBatches = Mathf.CeilToInt((float)detections.Count / batchSize);
                
                _debugger.Log($"Processing {detections.Count} detections in {totalBatches} batches", LogCategory.AI);
                
                // Process detections in batches
                for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    int startIdx = batchIndex * batchSize;
                    int endIdx = Mathf.Min(startIdx + batchSize, detections.Count);
                    int batchCount = endIdx - startIdx;
                    
                    _debugger.Log($"Processing batch {batchIndex + 1}/{totalBatches} with {batchCount} detections", LogCategory.AI);
                    
                    List<Task<ImageSegment>> segmentationTasks = new List<Task<ImageSegment>>();
                    
                    // Start all tasks in the batch
                    for (int i = startIdx; i < endIdx; i++)
                    {
                        var detection = detections[i];
                        
                        segmentationTasks.Add(Task.Run(() => {
                            return SegmentDetection(image, detection, i);
                        }));
                    }
                    
                    // Wait for all tasks in the batch to complete
                    while (!segmentationTasks.All(t => t.IsCompleted))
                    {
                        yield return null;
                    }
                    
                    // Collect results
                    foreach (var task in segmentationTasks)
                    {
                        if (task.Result != null)
                        {
                            segments.Add(task.Result);
                        }
                    }
                    
                    // Allow a frame to process
                    yield return null;
                }
                
                float segmentationTime = _debugger.StopTimer("SAM2Segmentation");
                _debugger.Log($"SAM2 segmentation completed in {segmentationTime:F2}s, created {segments.Count} segments", LogCategory.AI);
                
                onComplete?.Invoke(segments);
            }
            catch (Exception ex)
            {
                _debugger.LogError($"SAM2 segmentation error: {ex.Message}", LogCategory.AI);
                onError?.Invoke(ex.Message);
            }
        }
        
        /// <summary>
        /// Segments a single detection using SAM2.
        /// </summary>
        private ImageSegment SegmentDetection(Texture2D image, DetectedObject detection, int index)
        {
            try
            {
                // Determine input resolution
                int inputResolution = useHighQuality ? 1024 : 640;
                
                // Preprocess image
                Tensor inputTensor = TensorUtils.Preprocess(image, inputResolution);
                
                // Create prompt tensor (normalized center point)
                Vector2 centerN = new Vector2(
                    (detection.boundingBox.center.x / image.width),
                    (detection.boundingBox.center.y / image.height)
                );
                
                Tensor promptTensor = new Tensor(new[] { 1, 2 }, new[] { centerN.x, centerN.y });
                
                // Run inference
                _sam2Session.Run(
                    new Dictionary<string, Tensor> {
                        { "image", inputTensor },
                        { "prompt", promptTensor }
                    }
                );
                
                // Get output mask
                Tensor maskTensor = _sam2Session.Fetch("masks");
                
                // Convert to texture
                Texture2D maskTexture = TensorUtils.DecodeMask(maskTensor);
                
                // Clean up tensors
                inputTensor.Dispose();
                promptTensor.Dispose();
                maskTensor.Dispose();
                
                // Resize mask to match detection size
                Texture2D resizedMask = ResizeMask(maskTexture, (int)detection.boundingBox.width, (int)detection.boundingBox.height);
                UnityEngine.Object.Destroy(maskTexture);
                
                // Create random color with high saturation
                Color color = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f, 0.7f, 0.7f);
                
                // Create segment
                ImageSegment segment = new ImageSegment
                {
                    detectedObject = detection,
                    mask = resizedMask,
                    boundingBox = detection.boundingBox,
                    color = color,
                    area = CalculateActualArea(resizedMask)
                };
                
                return segment;
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error segmenting detection {index}: {ex.Message}", LogCategory.AI);
                
                // Create fallback segment from bounding box
                return CreateFallbackSegment(detection, image.width, image.height);
            }
        }
        
        /// <summary>
        /// Creates fallback segments based on bounding boxes when segmentation fails.
        /// </summary>
        private List<ImageSegment> CreateFallbackSegments(List<DetectedObject> detections, int imageWidth, int imageHeight)
        {
            List<ImageSegment> segments = new List<ImageSegment>();
            
            foreach (var detection in detections)
            {
                segments.Add(CreateFallbackSegment(detection, imageWidth, imageHeight));
            }
            
            return segments;
        }
        
        /// <summary>
        /// Creates a fallback segment for a single detection using its bounding box.
        /// </summary>
        private ImageSegment CreateFallbackSegment(DetectedObject detection, int imageWidth, int imageHeight)
        {
            // Create a simple mask based on the bounding box
            int width = Mathf.Max(1, Mathf.RoundToInt(detection.boundingBox.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(detection.boundingBox.height));
            
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Fill with white pixels (fully opaque)
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            
            // Create random color with high saturation
            Color color = UnityEngine.Random.ColorHSV(0f, 1f, 0.7f, 1f, 0.7f, 1f, 0.7f, 0.7f);
            
            // Create segment
            ImageSegment segment = new ImageSegment
            {
                detectedObject = detection,
                mask = mask,
                boundingBox = detection.boundingBox,
                color = color,
                area = width * height
            };
            
            return segment;
        }
        
        /// <summary>
        /// Resizes a mask texture to the specified dimensions.
        /// </summary>
        private Texture2D ResizeMask(Texture2D original, int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            
            // Create render texture for resizing
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            
            // Blit to render texture
            Graphics.Blit(original, rt);
            
            // Create new texture
            Texture2D resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
            
            // Read pixels from render texture
            RenderTexture prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            RenderTexture.active = prevRT;
            
            // Release render texture
            RenderTexture.ReleaseTemporary(rt);
            
            return resized;
        }
        
        /// <summary>
        /// Calculates the actual area of a mask (counting non-transparent pixels).
        /// </summary>
        private float CalculateActualArea(Texture2D mask)
        {
            if (mask == null) return 0f;
            
            Color[] pixels = mask.GetPixels();
            int count = 0;
            
            foreach (Color pixel in pixels)
            {
                if (pixel.a > 0.5f)
                {
                    count++;
                }
            }
            
            return count;
        }
        
        #endregion
        
        #region Detailed Analysis
        
        /// <summary>
        /// Analyzes segments in detail, including classification, height estimation, etc.
        /// </summary>
        private IEnumerator AnalyzeSegmentsInDetail(
            List<ImageSegment> segments,
            Texture2D sourceImage,
            Action<string, float> onProgress)
        {
            _debugger.StartTimer("DetailedAnalysis");
            
            int totalSegments = segments.Count;
            int processedSegments = 0;
            _analysisQueue.Clear();
            _analyzedSegments.Clear();
            _activeAnalyses = 0;
            
            // Create analysis requests for all segments
            foreach (var segment in segments)
            {
                _analysisQueue.Enqueue(new SegmentAnalysisRequest {
                    segment = segment,
                    sourceImage = sourceImage
                });
            }
            
            // Process queue with limited concurrency
            while (_analysisQueue.Count > 0 || _activeAnalyses > 0)
            {
                // Start new analyses up to the concurrency limit
                while (_analysisQueue.Count > 0 && _activeAnalyses < maxAPIRequestsPerFrame)
                {
                    var request = _analysisQueue.Dequeue();
                    _activeAnalyses++;
                    
                    StartCoroutine(AnalyzeSegment(request, () => {
                        _activeAnalyses--;
                        processedSegments++;
                        
                        // Update progress
                        float progress = processedSegments / (float)totalSegments;
                        onProgress?.Invoke($"Analyzing segment {processedSegments}/{totalSegments}", 0.3f + progress * 0.3f);
                    }));
                }
                
                yield return null;
            }
            
            float analysisTime = _debugger.StopTimer("DetailedAnalysis");
            _debugger.Log($"Detailed analysis completed in {analysisTime:F2}s", LogCategory.AI);
        }
        
        /// <summary>
        /// Analyzes a single segment in detail.
        /// </summary>
        private IEnumerator AnalyzeSegment(SegmentAnalysisRequest request, Action onComplete)
        {
            try
            {
                var segment = request.segment;
                var sourceImage = request.sourceImage;
                
                // Create analyzed segment object
                var analyzedSegment = new AnalyzedSegment
                {
                    originalSegment = segment,
                    boundingBox = segment.boundingBox,
                    metadata = new Dictionary<string, object>()
                };
                
                // Step 1: Determine if this is terrain or object
                yield return StartCoroutine(ClassifySegmentType(segment, analyzedSegment));
                
                // Step 2: Extract detailed classification
                if (enableDetailedClassification)
                {
                    yield return StartCoroutine(ClassifySegmentDetails(segment, analyzedSegment));
                }
                else
                {
                    // Use basic classification from detection
                    analyzedSegment.objectType = segment.detectedObject.className;
                    analyzedSegment.detailedClassification = segment.detectedObject.className;
                }
                
                // Step 3: For terrain, estimate height and analyze topology
                if (analyzedSegment.isTerrain && estimateTerrainHeight)
                {
                    yield return StartCoroutine(EstimateTerrainHeight(segment, analyzedSegment));
                }
                else
                {
                    // For non-terrain objects, calculate placement parameters
                    CalculateObjectPlacement(segment, analyzedSegment, sourceImage);
                }
                
                // Add to analyzed segments
                _analyzedSegments.Add(analyzedSegment);
            }
            catch (Exception ex)
            {
                _debugger.LogError($"Error analyzing segment: {ex.Message}", LogCategory.AI);
            }
            finally
            {
                onComplete?.Invoke();
            }
        }
        
        /// <summary>
        /// Classifies whether a segment is terrain or object.
        /// </summary>
        private IEnumerator ClassifySegmentType(ImageSegment segment, AnalyzedSegment result)
        {
            // First try using class name heuristics
            string className = segment.detectedObject.className.ToLowerInvariant();
            
            if (_terrainClasses.Any(tc => className.Contains(tc)))
            {
                result.isTerrain = true;
                result.classificationConfidence = 0.9f;
                yield break;
            }
            
            // If not clearly terrain by name, use additional heuristics
            if (autoClassifyTerrainObjects)
            {
                // Large objects at the bottom of image are likely terrain
                float relativeSize = segment.area / (segment.boundingBox.width * segment.boundingBox.height);
                float relativeY = segment.boundingBox.y / segment.boundingBox.height;
                
                if (relativeSize > 0.8f && relativeY > 0.6f)
                {
                    result.isTerrain = true;
                    result.classificationConfidence = 0.7f;
                    yield break;
                }
            }
            
            // Default to object if no terrain indicators found
            result.isTerrain = false;
            result.classificationConfidence = 0.8f;
        }
        
        /// <summary>
        /// Performs detailed classification of a segment.
        /// </summary>
        private IEnumerator ClassifySegmentDetails(ImageSegment segment, AnalyzedSegment result)
        {
            if (_rcnnSession == null)
            {
                // Fallback to base class
                result.objectType = segment.detectedObject.className;
                result.detailedClassification = segment.detectedObject.className;
                yield break;
            }
            
            try
            {
                // Extract segment region
                Texture2D segmentTexture = ExtractSegmentTexture(segment, result.boundingBox);
                
                if (segmentTexture == null)
                {
                    result.objectType = segment.detectedObject.className;
                    result.detailedClassification = segment.detectedObject.className;
                    yield break;
                }
                
                // Preprocess for model
                Tensor inputTensor = TensorUtils.Preprocess(segmentTexture, 300);
                
                // Run inference
                _rcnnSession.Run(new Dictionary<string, Tensor> { { "input", inputTensor } });
                
                // Get class predictions
                Tensor classOutput = _rcnnSession.Fetch("class_predictions");
                Tensor featureOutput = _rcnnSession.Fetch("features");
                
                // Get detailed classification
                result.detailedClassification = GetDetailedClassification(classOutput);
                result.objectType = result.detailedClassification;
                
                // Extract features for further analysis
                result.features = ExtractFeatures(featureOutput);
                
                // Clean up
                inputTensor.Dispose();
                classOutput.Dispose();
                featureOutput.Dispose();
                UnityEngine.Object.Destroy(segmentTexture);
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error in detailed classification: {ex.Message}", LogCategory.AI);
                
                // Fallback to base class
                result.objectType = segment.detectedObject.className;
                result.detailedClassification = segment.detectedObject.className;
            }
        }
        
        /// <summary>
        /// Estimates height for terrain segments.
        /// </summary>
        private IEnumerator EstimateTerrainHeight(ImageSegment segment, AnalyzedSegment result)
        {
            try
            {
                // Extract segment region
                Texture2D segmentTexture = ExtractSegmentTexture(segment, result.boundingBox);
                
                if (segmentTexture == null)
                {
                    // Fallback to simple height estimation
                    result.estimatedHeight = EstimateHeightFromClassName(result.objectType);
                    result.heightMap = GenerateSimpleHeightMap(segment);
                    result.topologyFeatures = new Dictionary<string, float> {
                        { "slope", 0f },
                        { "roughness", 0.5f }
                    };
                    yield break;
                }
                
                // Convert to grayscale for height estimation
                Texture2D grayscaleTexture = ConvertToGrayscale(segmentTexture);
                UnityEngine.Object.Destroy(segmentTexture);
                
                // Use grayscale values for height estimation
                Color[] pixels = grayscaleTexture.GetPixels();
                float avgHeight = 0f;
                float minHeight = 1f;
                float maxHeight = 0f;
                
                foreach (Color pixel in pixels)
                {
                    float height = pixel.r; // Grayscale, so r=g=b
                    avgHeight += height;
                    minHeight = Mathf.Min(minHeight, height);
                    maxHeight = Mathf.Max(maxHeight, height);
                }
                
                avgHeight /= pixels.Length;
                
                // Scale based on terrain type
                float heightMultiplier = GetHeightMultiplierForType(result.objectType);
                result.estimatedHeight = avgHeight * heightMultiplier * maxTerrainHeight;
                
                // Calculate topology features
                result.topologyFeatures = CalculateTopologyFeatures(grayscaleTexture);
                
                // Generate height map
                result.heightMap = grayscaleTexture; // Use grayscale texture as height map
                
                _debugger.Log($"Estimated height for {result.objectType}: {result.estimatedHeight:F1}m", LogCategory.AI);
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error estimating terrain height: {ex.Message}", LogCategory.AI);
                
                // Fallback to simple height estimation
                result.estimatedHeight = EstimateHeightFromClassName(result.objectType);
                result.heightMap = GenerateSimpleHeightMap(segment);
                result.topologyFeatures = new Dictionary<string, float> {
                    { "slope", 0f },
                    { "roughness", 0.5f }
                };
            }
        }
        
        /// <summary>
        /// Calculates placement parameters for non-terrain objects.
        /// </summary>
        private void CalculateObjectPlacement(ImageSegment segment, AnalyzedSegment result, Texture2D sourceImage)
        {
            // Calculate normalized position (center of bounding box)
            result.normalizedPosition = new Vector2(
                segment.boundingBox.center.x / sourceImage.width,
                1f - (segment.boundingBox.center.y / sourceImage.height) // Flip Y for Unity coordinates
            );
            
            // Estimate rotation based on shape analysis
            result.estimatedRotation = EstimateRotation(segment);
            
            // Estimate scale based on object type and size
            result.estimatedScale = EstimateScale(segment, result.objectType, sourceImage);
            
            // Calculate placement confidence
            result.placementConfidence = segment.detectedObject.confidence * result.classificationConfidence;
            
            _debugger.Log($"Object placement: {result.objectType} at ({result.normalizedPosition.x:F2}, {result.normalizedPosition.y:F2}), " +
                         $"rotation: {result.estimatedRotation:F1}°, scale: {result.estimatedScale:F2}", LogCategory.AI);
        }
        
        /// <summary>
        /// Enhances segment descriptions using OpenAI.
        /// </summary>
        private IEnumerator EnhanceSegmentDescriptions()
        {
            if (string.IsNullOrEmpty(openAIApiKey))
            {
                _debugger.LogWarning("OpenAI API key not set, skipping description enhancement", LogCategory.AI);
                yield break;
            }
            
            _debugger.StartTimer("DescriptionEnhancement");
            
            // Split into terrain and non-terrain segments
            var terrainSegments = _analyzedSegments.Where(s => s.isTerrain).ToList();
            var objectSegments = _analyzedSegments.Where(s => !s.isTerrain).ToList();
            
            // Process terrain segments first
            int processedCount = 0;
            int totalCount = terrainSegments.Count + objectSegments.Count;
            
            foreach (var segment in terrainSegments)
            {
                yield return EnhanceTerrainDescription(segment);
                processedCount++;
            }
            
            // Then process object segments
            foreach (var segment in objectSegments)
            {
                yield return EnhanceObjectDescription(segment);
                processedCount++;
            }
            
            float enhancementTime = _debugger.StopTimer("DescriptionEnhancement");
            _debugger.Log($"Description enhancement completed in {enhancementTime:F2}s", LogCategory.AI);
        }
        
        /// <summary>
        /// Enhances terrain description using OpenAI.
        /// </summary>
        private IEnumerator EnhanceTerrainDescription(AnalyzedSegment segment)
        {
            string prompt = $@"You are analyzing a terrain segment for 3D world generation.
Terrain type: {segment.objectType}
Height: {segment.estimatedHeight:F1} meters
Features: {string.Join(", ", segment.topologyFeatures?.Select(f => $"{f.Key}={f.Value:F2}") ?? new string[0])}

Provide a detailed description for terrain generation including:
1. Geological characteristics
2. Surface texture and materials
3. Vegetation or features typically found
4. Color palette and visual appearance

Keep response under 100 words and focus on 3D terrain generation details.";
            
            bool completed = false;
            string enhancedDescription = null;
            
            OpenAIResponse.Instance.RequestCompletion(prompt, 
                response => {
                    enhancedDescription = response;
                    completed = true;
                },
                error => {
                    _debugger.LogWarning($"OpenAI error: {error}", LogCategory.API);
                    completed = true;
                }
            );
            
            // Wait for completion
            float timeout = 10f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            if (enhancedDescription != null)
            {
                segment.enhancedDescription = enhancedDescription;
                _debugger.Log($"Enhanced terrain description: {segment.objectType}", LogCategory.AI);
            }
            else
            {
                // Fallback description
                segment.enhancedDescription = $"A {segment.objectType} terrain feature with approximate height of {segment.estimatedHeight:F1} meters.";
            }
        }
        
        /// <summary>
        /// Enhances object description using OpenAI.
        /// </summary>
        private IEnumerator EnhanceObjectDescription(AnalyzedSegment segment)
        {
            string prompt = $@"You are analyzing a map object for 3D model generation.
Object type: {segment.objectType}
Scale: {segment.estimatedScale:F2}
Context: Located on a map for 3D world generation

Provide a concise description for 3D model generation including:
1. Architectural or structural style
2. Materials and textures
3. Key visual features
4. Appropriate details for the scale

Keep response under 50 words, optimized for 3D model generation.";
            
            bool completed = false;
            string enhancedDescription = null;
            
            OpenAIResponse.Instance.RequestCompletion(prompt, 
                response => {
                    enhancedDescription = response;
                    completed = true;
                },
                error => {
                    _debugger.LogWarning($"OpenAI error: {error}", LogCategory.API);
                    completed = true;
                }
            );
            
            // Wait for completion
            float timeout = 10f;
            float elapsed = 0f;
            while (!completed && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            if (enhancedDescription != null)
            {
                segment.enhancedDescription = enhancedDescription;
                _debugger.Log($"Enhanced object description: {segment.objectType}", LogCategory.AI);
            }
            else
            {
                // Fallback description
                segment.enhancedDescription = $"A {segment.objectType} with approximate scale of {segment.estimatedScale:F2}.";
            }
        }
        
        /// <summary>
        /// Processes terrain segments for terrain generation.
        /// </summary>
        private IEnumerator ProcessTerrainSegments(Texture2D sourceImage, Action<string, float> onProgress)
        {
            if (_terrainGenerator == null)
            {
                _debugger.LogWarning("TerrainGenerator not found, skipping terrain processing", LogCategory.AI);
                yield break;
            }
            
            var terrainSegments = _analyzedSegments.Where(s => s.isTerrain).ToList();
            _debugger.Log($"Processing {terrainSegments.Count} terrain segments", LogCategory.AI);
            
            // Group terrain segments by type for batch processing
            var terrainGroups = terrainSegments.GroupBy(s => s.objectType);
            
            foreach (var group in terrainGroups)
            {
                var segmentList = group.ToList();
                
                // Create terrain modification data
                var terrainMods = new List<TerrainModification>();
                
                foreach (var segment in segmentList)
                {
                    var mod = new TerrainModification
                    {
                        bounds = segment.boundingBox,
                        heightMap = segment.heightMap,
                        baseHeight = segment.estimatedHeight / maxTerrainHeight,
                        terrainType = segment.objectType,
                        description = segment.enhancedDescription,
                        blendRadius = heightSmoothingRadius
                    };
                    
                    // Add topology features
                    if (segment.topologyFeatures != null)
                    {
                        mod.slope = segment.topologyFeatures.GetValueOrDefault("slope", 0f);
                        mod.roughness = segment.topologyFeatures.GetValueOrDefault("roughness", 0.5f);
                    }
                    
                    terrainMods.Add(mod);
                }
                
                onProgress?.Invoke($"Applying terrain modifications for {group.Key}...", 0.75f);
                
                // Send to terrain generator (modified to match your actual API)
                _terrainGenerator.ApplyTerrainModifications(terrainMods, sourceImage);
                
                // Allow a frame to process
                yield return null;
            }
        }
        
        /// <summary>
        /// Processes non-terrain objects for placement.
        /// </summary>
        private IEnumerator ProcessNonTerrainSegments(Texture2D sourceImage)
        {
            var objectSegments = _analyzedSegments.Where(s => !s.isTerrain).ToList();
            _debugger.Log($"Processing {objectSegments.Count} object segments", LogCategory.AI);
            
            // For each object type, calculate grouping and variations
            var objectGroups = objectSegments.GroupBy(s => s.objectType);
            
            foreach (var group in objectGroups)
            {
                var segments = group.ToList();
                
                // Calculate similarity between objects of same type
                CalculateSimilarityWithinGroup(segments, sourceImage);
                
                // Allow a frame to process
                yield return null;
            }
        }
        
        /// <summary>
        /// Calculates similarity between objects in the same group.
        /// </summary>
        private void CalculateSimilarityWithinGroup(List<AnalyzedSegment> segments, Texture2D sourceImage)
        {
            if (segments.Count <= 1) return;
            
            string objectType = segments[0].objectType;
            _debugger.Log($"Calculating similarity for {segments.Count} {objectType} objects", LogCategory.AI);
            
            // For each segment, compare with others to determine similarity
            for (int i = 0; i < segments.Count; i++)
            {
                var segA = segments[i];
                
                // Create metadata entry for similar objects
                if (segA.metadata == null)
                {
                    segA.metadata = new Dictionary<string, object>();
                }
                
                var similarObjects = new List<string>();
                
                for (int j = 0; j < segments.Count; j++)
                {
                    if (i == j) continue;
                    
                    var segB = segments[j];
                    
                    // Calculate similarity based on features and appearance
                    float similarity = CalculateObjectSimilarity(segA, segB);
                    
                    // If similarity exceeds threshold, mark as similar
                    if (similarity > 0.8f)
                    {
                        similarObjects.Add(segB.originalSegment.id);
                    }
                }
                
                // Store similar objects
                segA.metadata["similarObjects"] = similarObjects;
            }
        }
        
        /// <summary>
        /// Builds final results from analyzed segments.
        /// </summary>
        private AnalysisResults BuildFinalResults(Texture2D sourceImage)
        {
            var results = new AnalysisResults
            {
                mapObjects = new List<MapObject>(),
                terrainFeatures = new List<TerrainFeature>(),
                objectGroups = new List<ObjectGroup>()
            };
            
            // Convert analyzed segments to results format
            foreach (var segment in _analyzedSegments)
            {
                if (segment.isTerrain)
                {
                    // Add as terrain feature
                    var feature = new TerrainFeature
                    {
                        label = segment.objectType,
                        boundingBox = segment.boundingBox,
                        segmentMask = segment.originalSegment.mask,
                        segmentColor = segment.originalSegment.color,
                        elevation = segment.estimatedHeight,
                        metadata = new Dictionary<string, object>()
                    };
                    
                    // Add topology data as metadata
                    if (segment.topologyFeatures != null)
                    {
                        foreach (var kvp in segment.topologyFeatures)
                        {
                            feature.metadata[kvp.Key] = kvp.Value;
                        }
                    }
                    
                    // Add description
                    feature.metadata["description"] = segment.enhancedDescription;
                    
                    results.terrainFeatures.Add(feature);
                }
                else
                {
                    // Add as map object
                    var mapObject = new MapObject
                    {
                        type = segment.objectType,
                        label = segment.detailedClassification,
                        enhancedDescription = segment.enhancedDescription,
                        position = segment.normalizedPosition,
                        boundingBox = segment.boundingBox,
                        segmentMask = segment.originalSegment.mask,
                        segmentColor = segment.originalSegment.color,
                        scale = Vector3.one * segment.estimatedScale,
                        rotation = segment.estimatedRotation,
                        confidence = segment.placementConfidence,
                        metadata = new Dictionary<string, object>()
                    };
                    
                    // Add features as metadata
                    if (segment.features != null)
                    {
                        mapObject.metadata["features"] = segment.features;
                    }
                    
                    // Copy over other metadata
                    if (segment.metadata != null)
                    {
                        foreach (var kvp in segment.metadata)
                        {
                            mapObject.metadata[kvp.Key] = kvp.Value;
                        }
                    }
                    
                    results.mapObjects.Add(mapObject);
                }
            }
            
            // Group similar objects
            results.objectGroups = results.mapObjects
                .GroupBy(o => o.type)
                .Select(g => new ObjectGroup
                {
                    groupId = Guid.NewGuid().ToString(),
                    type = g.Key,
                    objects = g.ToList()
                })
                .ToList();
            
            // Generate height map and segmentation map
            results.heightMap = BuildGlobalHeightMap(sourceImage, results.terrainFeatures);
            results.segmentationMap = BuildSegmentationMap(sourceImage, _analyzedSegments);
            
            return results;
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Extracts segment texture from source image.
        /// </summary>
        private Texture2D ExtractSegmentTexture(ImageSegment segment, Rect boundingBox)
        {
            try
            {
                // Get segment bounds
                int x = Mathf.FloorToInt(boundingBox.x);
                int y = Mathf.FloorToInt(boundingBox.y);
                int width = Mathf.FloorToInt(boundingBox.width);
                int height = Mathf.FloorToInt(boundingBox.height);
                
                // Ensure bounds are valid
                width = Mathf.Max(1, width);
                height = Mathf.Max(1, height);
                
                // Create texture
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                
                // Get pixels from segment mask
                Color[] pixelsMask = segment.mask.GetPixels();
                
                // Get pixels from source image
                Color[] pixelsSource = segment.detectedObject.mask.GetPixels(x, y, width, height);
                
                // Create masked pixels
                Color[] pixelsMasked = new Color[width * height];
                for (int i = 0; i < pixelsMasked.Length; i++)
                {
                    if (i < pixelsMask.Length && i < pixelsSource.Length)
                    {
                        pixelsMasked[i] = pixelsSource[i] * pixelsMask[i].a;
                    }
                }
                
                // Set pixels
                tex.SetPixels(pixelsMasked);
                tex.Apply();
                
                return tex;
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error extracting segment texture: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Converts a texture to grayscale.
        /// </summary>
        private Texture2D ConvertToGrayscale(Texture2D source)
        {
            Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            Color[] pixels = source.GetPixels();
            Color[] grayscale = new Color[pixels.Length];
            
            for (int i = 0; i < pixels.Length; i++)
            {
                // Standard grayscale conversion formula
                float gray = pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
                grayscale[i] = new Color(gray, gray, gray, pixels[i].a);
            }
            
            result.SetPixels(grayscale);
            result.Apply();
            
            return result;
        }
        
        /// <summary>
        /// Estimates terrain height from class name.
        /// </summary>
        private float EstimateHeightFromClassName(string className)
        {
            // Map common terrain types to heights
            string lowerName = className.ToLowerInvariant();
            
            if (lowerName.Contains("mountain"))
                return 0.8f * maxTerrainHeight;
            if (lowerName.Contains("hill"))
                return 0.4f * maxTerrainHeight;
            if (lowerName.Contains("valley"))
                return 0.1f * maxTerrainHeight;
            if (lowerName.Contains("plateau"))
                return 0.5f * maxTerrainHeight;
            if (lowerName.Contains("plain"))
                return 0.05f * maxTerrainHeight;
            if (lowerName.Contains("desert"))
                return 0.1f * maxTerrainHeight;
            if (lowerName.Contains("canyon"))
                return 0.6f * maxTerrainHeight;
            if (lowerName.Contains("ridge"))
                return 0.6f * maxTerrainHeight;
            
            // Default height for unknown terrain types
            return 0.2f * maxTerrainHeight;
        }
        
        /// <summary>
        /// Gets height multiplier for terrain type.
        /// </summary>
        private float GetHeightMultiplierForType(string terrainType)
        {
            string lowerType = terrainType.ToLowerInvariant();
            
            if (lowerType.Contains("mountain"))
                return 1.0f;
            if (lowerType.Contains("hill"))
                return 0.5f;
            if (lowerType.Contains("valley"))
                return 0.2f;
            if (lowerType.Contains("plateau"))
                return 0.6f;
            if (lowerType.Contains("plain"))
                return 0.1f;
            if (lowerType.Contains("desert"))
                return 0.15f;
            if (lowerType.Contains("canyon"))
                return 0.7f;
            if (lowerType.Contains("ridge"))
                return 0.7f;
            
            return 0.3f; // Default multiplier
        }
        
        /// <summary>
        /// Generates a simple height map for a segment.
        /// </summary>
        private Texture2D GenerateSimpleHeightMap(ImageSegment segment)
        {
            int width = Mathf.Max(1, (int)segment.boundingBox.width);
            int height = Mathf.Max(1, (int)segment.boundingBox.height);
            
            Texture2D heightMap = new Texture2D(width, height, TextureFormat.RFloat, false);
            Color[] colors = new Color[width * height];
            
            // Simple gradient from center to edges
            Vector2 center = new Vector2(width / 2f, height / 2f);
            float maxDist = Vector2.Distance(center, Vector2.zero);
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float normalizedDist = dist / maxDist;
                    float height01 = 1f - normalizedDist * 0.7f; // Higher in center
                    
                    colors[y * width + x] = new Color(height01, 0f, 0f, 1f);
                }
            }
            
            heightMap.SetPixels(colors);
            heightMap.Apply();
            
            return heightMap;
        }
        
        /// <summary>
        /// Calculates topology features from a height map.
        /// </summary>
        private Dictionary<string, float> CalculateTopologyFeatures(Texture2D heightMap)
        {
            Dictionary<string, float> features = new Dictionary<string, float>();
            
            try
            {
                Color[] pixels = heightMap.GetPixels();
                int width = heightMap.width;
                int height = heightMap.height;
                
                float totalSlope = 0f;
                float maxSlope = 0f;
                float minHeight = 1f;
                float maxHeight = 0f;
                float roughness = 0f;
                int sampleCount = 0;
                
                // Skip edges to avoid out of bounds
                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        int idx = y * width + x;
                        float h = pixels[idx].r; // Height at current pixel
                        
                        // Update min/max heights
                        minHeight = Mathf.Min(minHeight, h);
                        maxHeight = Mathf.Max(maxHeight, h);
                        
                        // Calculate local slopes in X and Z directions
                        float hLeft = pixels[y * width + (x - 1)].r;
                        float hRight = pixels[y * width + (x + 1)].r;
                        float hDown = pixels[(y - 1) * width + x].r;
                        float hUp = pixels[(y + 1) * width + x].r;
                        
                        float slopeX = Mathf.Abs(hRight - hLeft) / 2f;
                        float slopeZ = Mathf.Abs(hUp - hDown) / 2f;
                        
                        // Combined slope (approx. gradient magnitude)
                        float slope = Mathf.Sqrt(slopeX * slopeX + slopeZ * slopeZ);
                        
                        // Convert to degrees (rough approximation)
                        float slopeDegrees = Mathf.Atan(slope * terrainSize.x / 2f) * Mathf.Rad2Deg;
                        
                        totalSlope += slopeDegrees;
                        maxSlope = Mathf.Max(maxSlope, slopeDegrees);
                        
                        // Calculate local roughness (Laplacian)
                        float laplacian = (hLeft + hRight + hDown + hUp - 4f * h);
                        roughness += Mathf.Abs(laplacian);
                        
                        sampleCount++;
                    }
                }
                
                // Compute averages
                float avgSlope = sampleCount > 0 ? totalSlope / sampleCount : 0f;
                float avgRoughness = sampleCount > 0 ? roughness / sampleCount : 0f;
                float heightRange = maxHeight - minHeight;
                
                // Store results
                features["slope"] = avgSlope;
                features["maxSlope"] = maxSlope;
                features["roughness"] = avgRoughness * 100f; // Scale up for readability
                features["heightRange"] = heightRange;
                features["minHeight"] = minHeight;
                features["maxHeight"] = maxHeight;
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error calculating topology: {ex.Message}", LogCategory.AI);
                
                // Default values
                features["slope"] = 0f;
                features["maxSlope"] = 0f;
                features["roughness"] = 0.5f;
                features["heightRange"] = 0.2f;
                features["minHeight"] = 0.4f;
                features["maxHeight"] = 0.6f;
            }
            
            return features;
        }
        
        /// <summary>
        /// Gets detailed classification from model output.
        /// </summary>
        private string GetDetailedClassification(Tensor classTensor)
        {
            // Find class with highest score
            int classCount = Mathf.Min(classTensor.shape[1], _classLabels.Length);
            int bestClassId = 0;
            float bestScore = float.MinValue;
            
            for (int i = 0; i < classCount; i++)
            {
                float score = classTensor[0, i];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClassId = i;
                }
            }
            
            // Return class name
            return bestClassId < _classLabels.Length ? _classLabels[bestClassId] : $"class_{bestClassId}";
        }
        
        /// <summary>
        /// Extracts features from model output.
        /// </summary>
        private Dictionary<string, float> ExtractFeatures(Tensor featureTensor)
        {
            Dictionary<string, float> features = new Dictionary<string, float>();
            
            // Extract top features
            int featureCount = Mathf.Min(featureTensor.shape[1], 20);
            
            for (int i = 0; i < featureCount; i++)
            {
                features[$"f{i}"] = featureTensor[0, i];
            }
            
            return features;
        }
        
        /// <summary>
        /// Estimates rotation of an object based on segment shape.
        /// </summary>
        private float EstimateRotation(ImageSegment segment)
        {
            try
            {
                // Use PCA to find principal axis
                if (segment.mask == null) return 0f;
                
                Color[] pixels = segment.mask.GetPixels();
                List<Vector2> points = new List<Vector2>();
                
                for (int y = 0; y < segment.mask.height; y++)
                {
                    for (int x = 0; x < segment.mask.width; x++)
                    {
                        int idx = y * segment.mask.width + x;
                        if (idx < pixels.Length && pixels[idx].a > 0.5f)
                        {
                            points.Add(new Vector2(x, y));
                        }
                    }
                }
                
                if (points.Count < 10)
                {
                    return 0f; // Not enough points for reliable estimation
                }
                
                // Calculate center of mass
                Vector2 center = Vector2.zero;
                foreach (var p in points) center += p;
                center /= points.Count;
                
                // Calculate covariance matrix
                float xx = 0f, xy = 0f, yy = 0f;
                foreach (var p in points)
                {
                    Vector2 d = p - center;
                    xx += d.x * d.x;
                    xy += d.x * d.y;
                    yy += d.y * d.y;
                }
                xx /= points.Count;
                xy /= points.Count;
                yy /= points.Count;
                
                // Calculate principal axis angle
                float angle;
                if (xx == yy)
                {
                    angle = 0f; // Circle or square
                }
                else
                {
                    angle = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
                }
                
                // Convert to degrees
                float degrees = angle * Mathf.Rad2Deg;
                
                // Adjust for Unity's coordinate system
                return degrees;
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error estimating rotation: {ex.Message}", LogCategory.AI);
                return 0f;
            }
        }
        
        /// <summary>
        /// Estimates object scale based on type and relative size.
        /// </summary>
        private float EstimateScale(ImageSegment segment, string objectType, Texture2D sourceImage)
        {
            try
            {
                // Base scale on object type and relative size in image
                float relativeArea = segment.area / (sourceImage.width * sourceImage.height);
                
                // Scale different object types differently
                string lowerType = objectType.ToLowerInvariant();
                float baseScale = 1.0f;
                
                if (lowerType.Contains("building") || lowerType.Contains("structure"))
                {
                    baseScale = 1.5f;
                }
                else if (lowerType.Contains("vehicle") || lowerType.Contains("car"))
                {
                    baseScale = 0.8f;
                }
                else if (lowerType.Contains("tree") || lowerType.Contains("vegetation"))
                {
                    baseScale = 1.2f;
                }
                else if (lowerType.Contains("person") || lowerType.Contains("human"))
                {
                    baseScale = 0.6f;
                }
                
                // Adjust based on relative area in the image
                // Larger objects should have smaller adjustment to avoid massive scaling
                float sizeAdjustment = Mathf.Lerp(0.5f, 2.0f, relativeArea * 100f);
                
                return baseScale * sizeAdjustment;
            }
            catch (Exception ex)
            {
                _debugger.LogWarning($"Error estimating scale: {ex.Message}", LogCategory.AI);
                return 1.0f;
            }
        }
        
        /// <summary>
        /// Calculates similarity between two objects.
        /// </summary>
        private float CalculateObjectSimilarity(AnalyzedSegment a, AnalyzedSegment b)
        {
            // Start with base similarity score
            float similarity = 0.0f;
            
            // Same type gives a base similarity
            if (a.objectType == b.objectType)
            {
                similarity += 0.5f;
            }
            
            // Compare features if available
            if (a.features != null && b.features != null)
            {
                // Compute cosine similarity of feature vectors
                float dotProduct = 0f;
                float aMagnitude = 0f;
                float bMagnitude = 0f;
                
                foreach (var key in a.features.Keys.Intersect(b.features.Keys))
                {
                    dotProduct += a.features[key] * b.features[key];
                    aMagnitude += a.features[key] * a.features[key];
                    bMagnitude += b.features[key] * b.features[key];
                }
                
                aMagnitude = Mathf.Sqrt(aMagnitude);
                bMagnitude = Mathf.Sqrt(bMagnitude);
                
                if (aMagnitude > 0 && bMagnitude > 0)
                {
                    float featureSimilarity = dotProduct / (aMagnitude * bMagnitude);
                    similarity += featureSimilarity * 0.3f;
                }
            }
            
            // Compare size and scale
            float scaleDiff = Mathf.Abs(a.estimatedScale - b.estimatedScale);
            float scaleSimilarity = Mathf.Clamp01(1f - scaleDiff);
            similarity += scaleSimilarity * 0.2f;
            
            return Mathf.Clamp01(similarity);
        }
        
        /// <summary>
        /// Builds a global height map from terrain features.
        /// </summary>
        private Texture2D BuildGlobalHeightMap(Texture2D sourceImage, List<TerrainFeature> features)
        {
            int width = sourceImage.width;
            int height = sourceImage.height;
            
            // Create empty height map
            Texture2D heightMap = new Texture2D(width, height, TextureFormat.RFloat, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with zero height
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0f, 0f, 0f, 1f);
            }
            
            // Apply each terrain feature
            foreach (var feature in features)
            {
                if (feature.segmentMask == null) continue;
                
                // Get mask pixels
                Color[] maskPixels = feature.segmentMask.GetPixels();
                
                // Calculate normalized height
                float normalizedHeight = feature.elevation / maxTerrainHeight;
                
                // Apply to global height map
                int x = Mathf.FloorToInt(feature.boundingBox.x);
                int y = Mathf.FloorToInt(feature.boundingBox.y);
                int w = Mathf.FloorToInt(feature.boundingBox.width);
                int h = Mathf.FloorToInt(feature.boundingBox.height);
                
                for (int fy = 0; fy < h && fy < feature.segmentMask.height; fy++)
                {
                    for (int fx = 0; fx < w && fx < feature.segmentMask.width; fx++)
                    {
                        int sourceX = x + fx;
                        int sourceY = y + fy;
                        
                        // Skip if out of bounds
                        if (sourceX < 0 || sourceX >= width || sourceY < 0 || sourceY >= height)
                            continue;
                        
                        int sourceIdx = sourceY * width + sourceX;
                        int maskIdx = fy * feature.segmentMask.width + fx;
                        
                        if (maskIdx < maskPixels.Length)
                        {
                            float mask = maskPixels[maskIdx].a;
                            
                            // Blend heights with mask as weight
                            float currentHeight = pixels[sourceIdx].r;
                            float newHeight = Mathf.Lerp(currentHeight, normalizedHeight, mask);
                            
                            pixels[sourceIdx] = new Color(newHeight, 0f, 0f, 1f);
                        }
                    }
                }
            }
            
            // Apply final pixels
            heightMap.SetPixels(pixels);
            heightMap.Apply();
            
            return heightMap;
        }
        
        /// <summary>
        /// Builds a segmentation map for visualization.
        /// </summary>
        private Texture2D BuildSegmentationMap(Texture2D sourceImage, List<AnalyzedSegment> segments)
        {
            int width = sourceImage.width;
            int height = sourceImage.height;
            
            // Create empty segmentation map
            Texture2D segMap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with transparent black
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0f, 0f, 0f, 0f);
            }
            
            // Sort segments by size (smaller on top)
            var sortedSegments = segments
                .OrderByDescending(s => s.originalSegment.area)
                .ToList();
            
            // Apply each segment
            foreach (var segment in sortedSegments)
            {
                var original = segment.originalSegment;
                if (original.mask == null) continue;
                
                // Get mask pixels
                Color[] maskPixels = original.mask.GetPixels();
                
                // Apply to segmentation map
                int x = Mathf.FloorToInt(original.boundingBox.x);
                int y = Mathf.FloorToInt(original.boundingBox.y);
                int w = Mathf.FloorToInt(original.boundingBox.width);
                int h = Mathf.FloorToInt(original.boundingBox.height);
                
                for (int fy = 0; fy < h && fy < original.mask.height; fy++)
                {
                    for (int fx = 0; fx < w && fx < original.mask.width; fx++)
                    {
                        int sourceX = x + fx;
                        int sourceY = y + fy;
                        
                        // Skip if out of bounds
                        if (sourceX < 0 || sourceX >= width || sourceY < 0 || sourceY >= height)
                            continue;
                        
                        int sourceIdx = sourceY * width + sourceX;
                        int maskIdx = fy * original.mask.width + fx;
                        
                        if (maskIdx < maskPixels.Length)
                        {
                            float mask = maskPixels[maskIdx].a;
                            
                            if (mask > 0.5f)
                            {
                                // Use segment color with semi-transparency
                                Color segColor = original.color;
                                segColor.a = 0.6f;
                                
                                pixels[sourceIdx] = segColor;
                            }
                        }
                    }
                }
            }
            
            // Apply final pixels
            segMap.SetPixels(pixels);
            segMap.Apply();
            
            return segMap;
        }
        
        #endregion
    }
}
