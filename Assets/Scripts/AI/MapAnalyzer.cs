using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Traversify.Core;

namespace Traversify.AI
{
    /// <summary>
    /// Worker interface wrapper to handle both global and Traversify.Core namespaces
    /// </summary>
    public class WorkerWrapper
    {
        private global::IInferenceModel _globalWorker;
        private Traversify.Core.IInferenceModel _traversifyWorker;
        
        public WorkerWrapper(object worker)
        {
            if (worker is global::IInferenceModel globalModel)
                _globalWorker = globalModel;
            else if (worker is Traversify.Core.IInferenceModel traversifyModel)
                _traversifyWorker = traversifyModel;
        }
        
        public void Dispose()
        {
            _globalWorker?.Dispose();
            _traversifyWorker?.Dispose();
        }
        
        public global::IInferenceModel AsGlobal()
        {
            return _globalWorker ?? (_globalWorker as global::IInferenceModel);
        }
        
        public Traversify.Core.IInferenceModel AsTraversify()
        {
            return _traversifyWorker ?? (_traversifyWorker as Traversify.Core.IInferenceModel);
        }
        
        // Add Execute method to forward to the appropriate worker
        public void Execute(Dictionary<string, Tensor> inputs)
        {
            if (_globalWorker != null)
            {
                InferenceModelExtensions.Execute(_globalWorker, inputs);
            }
            else if (_traversifyWorker != null)
            {
                InferenceModelExtensions.Execute(_traversifyWorker, inputs);
            }
            else
            {
                Debug.LogWarning("No worker instance available to execute");
            }
        }
        
        // Add PeekOutput method to forward to the appropriate worker
        public Tensor PeekOutput(string name)
        {
            if (_globalWorker != null)
            {
                return InferenceModelExtensions.PeekOutput(_globalWorker, name);
            }
            else if (_traversifyWorker != null)
            {
                return InferenceModelExtensions.PeekOutput(_traversifyWorker, name);
            }
            
            Debug.LogWarning("No worker instance available to peek output");
            return null;
        }
    }
    
    /// <summary>
    /// Advanced map analysis using AI.Inference for segment analysis and classification
    /// </summary>
    public class MapAnalyzer : MonoBehaviour
    {
        [Header("AI Models")]
        public OnnxModelAsset yoloModel;
        public OnnxModelAsset fasterRcnnModel;
        public OnnxModelAsset sam2Model;
        public OnnxModelAsset classificationModel; // Model for terrain/non-terrain classification
        public OnnxModelAsset heightEstimationModel; // Model for height estimation
        
        // Fallback paths for runtime loading
        public string yoloModelPath;
        public string fasterRcnnModelPath;
        public string sam2ModelPath;
        public string classificationModelPath;
        public string heightEstimationModelPath;
        
        [Header("API Configuration")]
        public string openAIApiKey;
        
        [Header("Processing Settings")]
        public int maxObjectsToProcess = 100;
        public float groupingThreshold = 0.1f;
        public bool useHighQuality = true;
        public int maxAPIRequestsPerFrame = 5;
        public float confidenceThreshold = 0.5f;
        public float iouThreshold = 0.45f;
        public bool useGPU = false; // Add this to control GPU acceleration
        
        [Header("Segmentation Settings")]
        public int segmentationResolution = 512;
        public float minSegmentArea = 0.001f;
        public bool mergeOverlappingSegments = true;
        
        [Header("Height Estimation")]
        public float maxTerrainHeight = 100f;
        public int heightMapResolution = 1024;
        public float heightSmoothingRadius = 2f;
        
        // Neural network models and workers
        private WorkerWrapper yoloWorker;
        private WorkerWrapper fasterRcnnWorker;
        private WorkerWrapper sam2Worker;
        private WorkerWrapper classificationWorker;
        private WorkerWrapper heightEstimationWorker;
        
        // Component references
        private TerrainGenerator terrainGenerator;
        private ModelGenerator modelGenerator;
        
        // Processing state
        private Queue<SegmentAnalysisRequest> analysisQueue = new Queue<SegmentAnalysisRequest>();
        private int activeAnalyses = 0;
        private bool isProcessing = false;
        
        // Analyzed segments cache
        private List<AnalyzedSegment> analyzedSegments = new List<AnalyzedSegment>();
        
        private void Awake()
        {
            // Get component references
            terrainGenerator = GetComponent<TerrainGenerator>();
            if (terrainGenerator == null)
                terrainGenerator = FindObjectOfType<TerrainGenerator>();
            
            modelGenerator = GetComponent<ModelGenerator>();
            if (modelGenerator == null)
                modelGenerator = FindObjectOfType<ModelGenerator>();
            
            LoadModels();
        }
        
        private void OnDestroy()
        {
            // Cleanup all workers
            yoloWorker?.Dispose();
            fasterRcnnWorker?.Dispose();
            sam2Worker?.Dispose();
            classificationWorker?.Dispose();
            heightEstimationWorker?.Dispose();
        }
        
        private void LoadModels()
        {
            try
            {
                string streamingAssetsModelDir = Path.Combine(Application.streamingAssetsPath, "Traversify", "Models");
                
                // Ensure the directory exists
                if (!Directory.Exists(streamingAssetsModelDir))
                {
                    Directory.CreateDirectory(streamingAssetsModelDir);
                    Debug.Log($"[MapAnalyzer] Created models directory at {streamingAssetsModelDir}");
                }

                // Load YOLO model
                if (yoloModel == null && !string.IsNullOrEmpty(yoloModelPath))
                {
                    string yoloName = Path.GetFileNameWithoutExtension(yoloModelPath);
                    string yoloFilePath = Path.Combine(streamingAssetsModelDir, $"{yoloName}.onnx");
                    
                    if (File.Exists(yoloFilePath))
                    {
                        Debug.Log($"[MapAnalyzer] Found YOLO model file at {yoloFilePath}");
                        
                        // Load model from StreamingAssets directly
                        try
                        {
                            if (yoloModel == null)
                            {
                                // Create a model asset from the file (requires model to be imported in Unity first)
                                yoloModel = ScriptableObject.CreateInstance<OnnxModelAsset>();
                                yoloModel.name = yoloName;
                                Debug.Log($"[MapAnalyzer] Created YOLO model asset instance");
                            }
                            
                            if (yoloModel != null)
                            {
                                var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, yoloModel);
                                yoloWorker = new WorkerWrapper(worker);
                                Debug.Log($"[MapAnalyzer] Loaded YOLO model from {yoloFilePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MapAnalyzer] Error loading YOLO model: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[MapAnalyzer] Could not find YOLO model file. Please place it at: {yoloFilePath}");
                    }
                }
                else if (yoloModel != null)
                {
                    var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, yoloModel);
                    yoloWorker = new WorkerWrapper(worker);
                    Debug.Log("[MapAnalyzer] Loaded YOLO model successfully from assigned asset");
                }

                // Load Faster R-CNN model
                if (fasterRcnnModel == null && !string.IsNullOrEmpty(fasterRcnnModelPath))
                {
                    string frcnnName = Path.GetFileNameWithoutExtension(fasterRcnnModelPath);
                    string frcnnFilePath = Path.Combine(streamingAssetsModelDir, $"{frcnnName}.onnx");
                    
                    if (File.Exists(frcnnFilePath))
                    {
                        Debug.Log($"[MapAnalyzer] Found Faster R-CNN model file at {frcnnFilePath}");
                        
                        // Load model from StreamingAssets directly
                        try
                        {
                            if (fasterRcnnModel == null)
                            {
                                // Create a model asset from the file
                                fasterRcnnModel = ScriptableObject.CreateInstance<OnnxModelAsset>();
                                fasterRcnnModel.name = frcnnName;
                                Debug.Log($"[MapAnalyzer] Created Faster R-CNN model asset instance");
                            }
                            
                            if (fasterRcnnModel != null)
                            {
                                var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, fasterRcnnModel);
                                fasterRcnnWorker = new WorkerWrapper(worker);
                                Debug.Log($"[MapAnalyzer] Loaded Faster R-CNN model from {frcnnFilePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MapAnalyzer] Error loading Faster R-CNN model: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[MapAnalyzer] Could not find Faster R-CNN model file. Please place it at: {frcnnFilePath}");
                    }
                }
                else if (fasterRcnnModel != null)
                {
                    var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, fasterRcnnModel);
                    fasterRcnnWorker = new WorkerWrapper(worker);
                    Debug.Log("[MapAnalyzer] Loaded Faster R-CNN model successfully from assigned asset");
                }

                // Load SAM2 model
                if (sam2Model == null && !string.IsNullOrEmpty(sam2ModelPath))
                {
                    string sam2Name = Path.GetFileNameWithoutExtension(sam2ModelPath);
                    string sam2FilePath = Path.Combine(streamingAssetsModelDir, $"{sam2Name}.onnx");
                    
                    if (File.Exists(sam2FilePath))
                    {
                        Debug.Log($"[MapAnalyzer] Found SAM2 model file at {sam2FilePath}");
                        
                        // Load model from StreamingAssets directly
                        try
                        {
                            if (sam2Model == null)
                            {
                                // Create a model asset from the file
                                sam2Model = ScriptableObject.CreateInstance<OnnxModelAsset>();
                                sam2Model.name = sam2Name;
                                Debug.Log($"[MapAnalyzer] Created SAM2 model asset instance");
                            }
                            
                            if (sam2Model != null)
                            {
                                var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, sam2Model);
                                sam2Worker = new WorkerWrapper(worker);
                                Debug.Log($"[MapAnalyzer] Loaded SAM2 model from {sam2FilePath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[MapAnalyzer] Error loading SAM2 model: {ex.Message}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[MapAnalyzer] Could not find SAM2 model file. Please place it at: {sam2FilePath}");
                    }
                }
                else if (sam2Model != null)
                {
                    var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, sam2Model);
                    sam2Worker = new WorkerWrapper(worker);
                    Debug.Log("[MapAnalyzer] Loaded SAM2 model successfully from assigned asset");
                }

                // Load classification model
                if (classificationModel == null && !string.IsNullOrEmpty(classificationModelPath))
                {
                    string clsName = Path.GetFileNameWithoutExtension(classificationModelPath);
                    var resourceModel = Resources.Load<OnnxModelAsset>(clsName);
                    
                    if (resourceModel != null)
                    {
                        classificationModel = resourceModel;
                    }
                    else
                    {
                        Debug.LogWarning($"[MapAnalyzer] Could not find classification model at {classificationModelPath}");
                    }
                }
                
                if (classificationModel != null)
                {
                    var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, classificationModel);
                    classificationWorker = new WorkerWrapper(worker);
                    Debug.Log("[MapAnalyzer] Loaded classification model successfully");
                }

                // Load height estimation model
                if (heightEstimationModel == null && !string.IsNullOrEmpty(heightEstimationModelPath))
                {
                    string heightName = Path.GetFileNameWithoutExtension(heightEstimationModelPath);
                    var resourceModel = Resources.Load<OnnxModelAsset>(heightName);
                    
                    if (resourceModel != null)
                    {
                        heightEstimationModel = resourceModel;
                    }
                    else
                    {
                        Debug.LogWarning($"[MapAnalyzer] Could not find height estimation model at {heightEstimationModelPath}");
                    }
                }
                
                if (heightEstimationModel != null)
                {
                    var worker = WorkerFactory.CreateWorker(useGPU ? WorkerFactory.Type.GPU : WorkerFactory.Type.CSharpBurst, heightEstimationModel);
                    heightEstimationWorker = new WorkerWrapper(worker);
                    Debug.Log("[MapAnalyzer] Loaded height estimation model successfully");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[MapAnalyzer] Failed to load models: {e.Message}");
            }
        }
        
        public IEnumerator AnalyzeImage(Texture2D imageTexture, Action<AnalysisResults> onComplete, 
            Action<string> onError, Action<string, float> onProgress)
        {
            if (isProcessing)
            {
                onError?.Invoke("Analysis already in progress");
                yield break;
            }
            
            isProcessing = true;
            analyzedSegments.Clear();
            AnalysisResults results = new AnalysisResults();
            
            // Step 1: Object Detection with YOLO
            onProgress?.Invoke("Detecting objects with YOLO...", 0.1f);
            List<DetectedObject> detectedObjects = null;
            yield return DetectObjectsWithYOLO(imageTexture, 
                (objects) => detectedObjects = objects,
                (error) => Debug.LogError($"YOLO detection failed: {error}"));
            
            // Step 2: Segmentation with SAM2
            onProgress?.Invoke("Segmenting image with SAM2...", 0.2f);
            Debug.Log("[MapAnalyzer] Actively using SAM2 model for segmentation.");
            List<ImageSegment> segments = null;
            yield return SegmentImageWithSAM2(imageTexture, detectedObjects,
                (segs) => segments = segs,
                (error) => Debug.LogError($"SAM2 segmentation failed: {error}"));
            Debug.Log("[MapAnalyzer] SAM2 model segmentation finished.");

            // Step 3: Analyze each segment with WorkerFactory
            onProgress?.Invoke("Analyzing segments...", 0.3f);
            yield return AnalyzeSegmentsWithWorkerFactory(segments, imageTexture, onProgress);
            
            // Step 4: Enhance descriptions with OpenAI
            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                onProgress?.Invoke("Enhancing descriptions with AI...", 0.6f);
                yield return EnhanceSegmentDescriptions();
            }
            
            // Step 5: Process terrain segments
            onProgress?.Invoke("Processing terrain topology...", 0.7f);
            yield return ProcessTerrainSegments(imageTexture, onProgress);
            
            // Step 6: Process non-terrain objects
            onProgress?.Invoke("Processing object placements...", 0.8f);
            yield return ProcessNonTerrainSegments();
            
            // Step 7: Convert to final results
            onProgress?.Invoke("Finalizing analysis...", 0.9f);
            results = ConvertAnalyzedSegmentsToResults(imageTexture);
            onProgress?.Invoke("Analysis complete", 1.0f);
            onComplete?.Invoke(results);
            isProcessing = false;
        }
        
        private IEnumerator AnalyzeSegmentsWithWorkerFactory(List<ImageSegment> segments, Texture2D sourceImage, 
            Action<string, float> onProgress)
        {
            int totalSegments = segments.Count;
            int processedSegments = 0;
            
            foreach (var segment in segments)
            {
                // Create analysis request
                var request = new SegmentAnalysisRequest
                {
                    segment = segment,
                    sourceImage = sourceImage
                };
                
                analysisQueue.Enqueue(request);
                processedSegments++;

                // Update progress and start processing queue
                float progress = processedSegments / (float)totalSegments * 0.6f;
                onProgress?.Invoke($"Analyzing segment {processedSegments}/{totalSegments}...", progress);

                while (analysisQueue.Count > 0 && activeAnalyses < maxAPIRequestsPerFrame)
                {
                    var req = analysisQueue.Dequeue();
                    yield return StartCoroutine(AnalyzeSegmentWithWorker(req));
                }

                yield return null;
            }
            
            // Wait for all analyses to complete
            while (activeAnalyses > 0)
            {
                yield return null;
            }
        }
        
        private IEnumerator AnalyzeSegmentWithWorker(SegmentAnalysisRequest request)
        {
            activeAnalyses++;
            
            var analyzedSegment = new AnalyzedSegment
            {
                originalSegment = request.segment,
                boundingBox = request.segment.boundingBox
            };
            
            // Step 1: Classify terrain vs non-terrain
            yield return ClassifySegmentType(request, analyzedSegment);
            
            // Step 2: Detailed classification with Faster R-CNN
            yield return ClassifySegmentDetails(request, analyzedSegment);
            
            // Step 3: If terrain, estimate height and topology
            if (analyzedSegment.isTerrain)
            {
                yield return EstimateTerrainHeight(request, analyzedSegment);
            }
            else
            {
                // For non-terrain objects, calculate placement parameters
                CalculateObjectPlacement(request, analyzedSegment);
            }
            
            analyzedSegments.Add(analyzedSegment);
            activeAnalyses--;
        }
        
        private IEnumerator ClassifySegmentType(SegmentAnalysisRequest request, AnalyzedSegment result)
        {
            if (classificationWorker == null)
            {
                // Fallback classification based on detected class
                result.isTerrain = IsTerrainClass(request.segment.detectedObject.className);
                yield break;
            }
            
            // Extract segment region
            var segmentTexture = ExtractSegmentTexture(request.sourceImage, request.segment);
            var inputTensor = PrepareImageTensor(segmentTexture, 224, 224);
            
            var inputs = new Dictionary<string, Tensor> { { "input", inputTensor } };
            classificationWorker.Execute(inputs);
            
            var output = classificationWorker.PeekOutput("classification");
            
            // Binary classification: 0 = non-terrain, 1 = terrain
            float terrainScore = output[0, 1];
            float nonTerrainScore = output[0, 0];
            
            result.isTerrain = terrainScore > nonTerrainScore;
            result.classificationConfidence = Mathf.Max(terrainScore, nonTerrainScore);
            
            // Cleanup
            Destroy(segmentTexture);
            inputTensor.Dispose();
            output.Dispose();
            
            Debug.Log($"[MapAnalyzer] Segment classified as {(result.isTerrain ? "TERRAIN" : "NON-TERRAIN")} " +
                     $"with confidence {result.classificationConfidence:P0}");
            
            yield return null;
        }
        
        private IEnumerator ClassifySegmentDetails(SegmentAnalysisRequest request, AnalyzedSegment result)
        {
            if (fasterRcnnWorker == null)
            {
                // Use basic classification from YOLO
                result.objectType = request.segment.detectedObject.className;
                result.detailedClassification = request.segment.detectedObject.className;
                yield break;
            }
            
            var segmentTexture = ExtractSegmentTexture(request.sourceImage, request.segment);
            var inputTensor = PrepareImageTensor(segmentTexture, 300, 300);
            
            var inputs = new Dictionary<string, Tensor> { { "input", inputTensor } };
            fasterRcnnWorker.Execute(inputs);
            
            var classOutput = fasterRcnnWorker.PeekOutput("class_predictions");
            var featureOutput = fasterRcnnWorker.PeekOutput("features");
            
            // Get detailed classification
            result.detailedClassification = ProcessDetailedClassification(classOutput, result.isTerrain);
            result.objectType = result.detailedClassification;
            
            // Extract features for further analysis
            result.features = ExtractFeatures(featureOutput);
            
            // Cleanup
            Destroy(segmentTexture);
            inputTensor.Dispose();
            classOutput.Dispose();
            featureOutput.Dispose();
            
            yield return null;
        }
        
        private IEnumerator EstimateTerrainHeight(SegmentAnalysisRequest request, AnalyzedSegment result)
        {
            if (heightEstimationWorker == null)
            {
                // Simple height estimation based on terrain type
                result.estimatedHeight = EstimateHeightByType(result.objectType);
                result.heightMap = GenerateSimpleHeightMap(request.segment);
                yield break;
            }
            
            var segmentTexture = ExtractSegmentTexture(request.sourceImage, request.segment);
            var inputTensor = PrepareImageTensor(segmentTexture, 256, 256);
            
            var inputs = new Dictionary<string, Tensor> { { "image", inputTensor } };
            heightEstimationWorker.Execute(inputs);
            
            var heightOutput = heightEstimationWorker.PeekOutput("height_map");
            
            // Convert height map tensor to texture
            result.heightMap = ConvertTensorToHeightMap(heightOutput, request.segment.boundingBox);
            result.estimatedHeight = CalculateAverageHeight(heightOutput);
            
            // Calculate topology features
            result.topologyFeatures = CalculateTopologyFeatures(heightOutput);
            
            // Cleanup
            Destroy(segmentTexture);
            inputTensor.Dispose();
            heightOutput.Dispose();
            
            Debug.Log($"[MapAnalyzer] Terrain height estimated: {result.estimatedHeight:F2} with " +
                     $"slope {result.topologyFeatures["slope"]:F2} degrees");
            
            yield return null;
        }
        
        private void CalculateObjectPlacement(SegmentAnalysisRequest request, AnalyzedSegment result)
        {
            var segment = request.segment;
            var imageWidth = request.sourceImage.width;
            var imageHeight = request.sourceImage.height;
            
            // Calculate normalized position (0-1)
            result.normalizedPosition = new Vector2(
                segment.boundingBox.center.x / imageWidth,
                segment.boundingBox.center.y / imageHeight
            );
            
            // Estimate scale based on bounding box size and object type
            float relativeSize = (segment.boundingBox.width * segment.boundingBox.height) / (imageWidth * imageHeight);
            result.estimatedScale = CalculateObjectScale(result.objectType, relativeSize, segment.boundingBox);
            
            // Estimate rotation based on bounding box aspect ratio and orientation
            result.estimatedRotation = CalculateObjectRotation(segment.boundingBox, segment.mask);
            
            // Calculate placement confidence
            result.placementConfidence = segment.detectedObject.confidence * result.classificationConfidence;
            
            Debug.Log($"[MapAnalyzer] Object placement calculated: {result.objectType} at " +
                     $"({result.normalizedPosition.x:F3}, {result.normalizedPosition.y:F3}) " +
                     $"scale: {result.estimatedScale}, rotation: {result.estimatedRotation:F1}°");
        }
        
        private IEnumerator EnhanceSegmentDescriptions()
        {
            var terrainSegments = analyzedSegments.Where(s => s.isTerrain).ToList();
            var objectSegments = analyzedSegments.Where(s => !s.isTerrain).ToList();
            
            // Enhance terrain descriptions
            foreach (var segment in terrainSegments)
            {
                yield return EnhanceTerrainDescription(segment);
            }
            
            // Enhance object descriptions
            foreach (var segment in objectSegments)
            {
                yield return EnhanceObjectDescription(segment);
            }
        }
        
        private IEnumerator EnhanceTerrainDescription(AnalyzedSegment segment)
        {
            string prompt = $@"You are analyzing a terrain segment for 3D world generation.
Terrain type: {segment.objectType}
Height: {segment.estimatedHeight:F1} meters
Features: {string.Join(", ", segment.features.Select(f => f.Key + "=" + f.Value))}

Provide a detailed description for terrain generation including:
1. Geological characteristics
2. Surface texture and materials
3. Vegetation or features typically found
4. Color palette and visual appearance

Keep response under 100 words and focus on 3D terrain generation details.";

            yield return SendOpenAIRequest(prompt, (response) => {
                segment.enhancedDescription = response;
                Debug.Log($"[MapAnalyzer] Enhanced terrain description: {response}");
            });
        }
        
        private IEnumerator EnhanceObjectDescription(AnalyzedSegment segment)
        {
            string prompt = $@"You are analyzing a map object for 3D model generation.
Object type: {segment.objectType}
Scale: {segment.estimatedScale}
Context: Located on a map for 3D world generation

Provide a concise description for 3D model generation including:
1. Architectural or structural style
2. Materials and textures
3. Key visual features
4. Appropriate details for the scale

Keep response under 50 words, optimized for Tripo3D model generation.";

            yield return SendOpenAIRequest(prompt, (response) => {
                segment.enhancedDescription = response;
                Debug.Log($"[MapAnalyzer] Enhanced object description: {response}");
            });
        }
        
        private IEnumerator ProcessTerrainSegments(Texture2D sourceImage, Action<string, float> onProgress)
        {
            if (terrainGenerator == null)
            {
                Debug.LogWarning("[MapAnalyzer] TerrainGenerator not found, skipping terrain processing");
                yield break;
            }
            
            var terrainSegments = analyzedSegments.Where(s => s.isTerrain).ToList();
            Debug.Log($"[MapAnalyzer] Processing {terrainSegments.Count} terrain segments");
            
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
                // Send to terrain generator
                yield return terrainGenerator.ApplyTerrainModifications(terrainMods, sourceImage);
            }
        }
        
        private IEnumerator ProcessNonTerrainSegments()
        {
            if (modelGenerator == null)
            {
                Debug.LogWarning("[MapAnalyzer] ModelGenerator not found, skipping object processing");
                yield break;
            }
            
            var objectSegments = analyzedSegments.Where(s => !s.isTerrain).ToList();
            Debug.Log($"[MapAnalyzer] Processing {objectSegments.Count} non-terrain segments");
            
            // Group similar objects for instancing
            var objectGroups = GroupSimilarObjects(objectSegments);
            
            foreach (var group in objectGroups)
            {
                // Create model generation requests
                var modelRequests = new List<ModelGenerationRequest>();
                
                foreach (var segment in group.segments)
                {
                    var request = new ModelGenerationRequest
                    {
                        objectType = segment.objectType,
                        description = segment.enhancedDescription ?? segment.objectType,
                        position = segment.normalizedPosition,
                        scale = segment.estimatedScale,
                        rotation = segment.estimatedRotation,
                        confidence = segment.placementConfidence,
                        isGrouped = group.segments.Count > 1,
                        groupId = group.segments.Count > 1 ? group.groupId : null
                    };
                    
                    modelRequests.Add(request);
                }
                
                // Send to model generator
                if (modelGenerator != null)
                {
                    yield return modelGenerator.GenerateModelsForSegments(modelRequests);
                }
                else
                {
                    Debug.LogError("[MapAnalyzer] ModelGenerator reference is null, cannot generate models for segments");
                }
            }
        }
        
        private AnalysisResults ConvertAnalyzedSegmentsToResults(Texture2D sourceImage)
        {
            var results = new AnalysisResults();
            results.terrainFeatures = new List<TerrainFeature>();
            results.mapObjects = new List<MapObject>();
            results.objectGroups = new List<ObjectGroup>();
            
            // Convert terrain segments
            foreach (var segment in analyzedSegments.Where(s => s.isTerrain))
            {
                results.terrainFeatures.Add(new TerrainFeature
                {
                    type = segment.objectType,
                    label = segment.detailedClassification,
                    boundingBox = segment.boundingBox,
                    segmentMask = segment.originalSegment.mask,
                    segmentColor = segment.originalSegment.color,
                    confidence = segment.classificationConfidence,
                    elevation = segment.estimatedHeight
                });
            }
            
            // Convert object segments
            var objectGroups = GroupSimilarObjects(analyzedSegments.Where(s => !s.isTerrain).ToList());
            
            foreach (var group in objectGroups)
            {
                var objectGroup = new ObjectGroup
                {
                    groupId = group.groupId,
                    type = group.objectType,
                    objects = new List<MapObject>()
                };
                
                foreach (var segment in group.segments)
                {
                    var mapObject = new MapObject
                    {
                        type = segment.objectType,
                        label = segment.detailedClassification,
                        enhancedDescription = segment.enhancedDescription,
                        position = segment.normalizedPosition,
                        boundingBox = segment.boundingBox,
                        segmentMask = segment.originalSegment.mask,
                        segmentColor = segment.originalSegment.color,
                        scale = segment.estimatedScale,
                        rotation = segment.estimatedRotation,
                        confidence = segment.classificationConfidence,
                        isGrouped = group.segments.Count > 1
                    };
                    
                    results.mapObjects.Add(mapObject);
                    objectGroup.objects.Add(mapObject);
                }
                
                if (objectGroup.objects.Count > 0)
                {
                    results.objectGroups.Add(objectGroup);
                }
            }
            
            // Generate final height and segmentation maps
            results.heightMap = GenerateFinalHeightMap(sourceImage.width, sourceImage.height);
            results.segmentationMap = GenerateFinalSegmentationMap(sourceImage.width, sourceImage.height);
            results.analysisTime = Time.time;
            
            return results;
        }
        
        // Helper methods for WorkerFactory analysis
        
        private Texture2D ExtractSegmentTexture(Texture2D source, ImageSegment segment)
        {
            var bounds = segment.boundingBox;
            int x = Mathf.Max(0, (int)bounds.x);
            int y = Mathf.Max(0, (int)bounds.y);
            int width = Mathf.Min((int)bounds.width, source.width - x);
            int height = Mathf.Min((int)bounds.height, source.height - y);
            
            var pixels = source.GetPixels(x, y, width, height);
            var segmentTexture = new Texture2D(width, height);
            
            // Apply mask if available
            if (segment.mask != null)
            {
                for (int py = 0; py < height; py++)
                {
                    for (int px = 0; px < width; px++)
                    {
                        int maskX = Mathf.RoundToInt(px / (float)width * segment.mask.width);
                        int maskY = Mathf.RoundToInt(py / (float)height * segment.mask.height);
                        
                        if (maskX >= 0 && maskX < segment.mask.width && 
                            maskY >= 0 && maskY < segment.mask.height)
                        {
                            float maskValue = segment.mask.GetPixel(maskX, maskY).a;
                            if (maskValue < 0.5f)
                            {
                                pixels[py * width + px] = Color.black;
                            }
                        }
                    }
                }
            }
            
            segmentTexture.SetPixels(pixels);
            segmentTexture.Apply();
            
            return segmentTexture;
        }
        
        private bool IsTerrainClass(string className)
        {
            string[] terrainClasses = {
                "water", "ocean", "sea", "lake", "river", "pond",
                "mountain", "hill", "valley", "cliff", "ridge",
                "forest", "woods", "grove", "jungle",
                "desert", "sand", "dune",
                "plain", "field", "meadow", "grassland",
                "swamp", "marsh", "wetland",
                "beach", "shore", "coast",
                "snow", "ice", "glacier"
            };
            
            return terrainClasses.Any(tc => className.ToLower().Contains(tc));
        }
        
        private string ProcessDetailedClassification(Tensor classOutput, bool isTerrain)
        {
            // Find the highest scoring class
            int bestClass = -1;
            float bestScore = float.MinValue;
            
            for (int i = 0; i < classOutput.shape[1]; i++)
            {
                float score = classOutput[0, i];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClass = i;
                }
            }
            
            // Map class index to detailed name based on terrain/non-terrain
            if (isTerrain)
            {
                return GetDetailedTerrainClass(bestClass);
            }
            else
            {
                return GetDetailedObjectClass(bestClass);
            }
        }
        
        private string GetDetailedTerrainClass(int classIndex)
        {
            string[] detailedTerrainClasses = {
                "deep_ocean", "shallow_water", "river", "lake", "pond",
                "mountain_peak", "rocky_hill", "grassy_hill", "valley", "canyon",
                "dense_forest", "sparse_forest", "jungle", "grove",
                "sandy_desert", "rocky_desert", "savanna",
                "grassland", "meadow", "farmland",
                "swamp", "marsh", "wetland",
                "sandy_beach", "rocky_shore", "cliff"
            };
            
            if (classIndex >= 0 && classIndex < detailedTerrainClasses.Length)
                return detailedTerrainClasses[classIndex];
            
            return "unknown_terrain";
        }
        
        private string GetDetailedObjectClass(int classIndex)
        {
            string[] detailedObjectClasses = {
                "residential_house", "apartment_building", "skyscraper", "cottage", "mansion",
                "medieval_castle", "fortress", "tower", "wall", "gate",
                "small_bridge", "large_bridge", "overpass",
                "deciduous_tree", "coniferous_tree", "palm_tree", "bush",
                "car", "truck", "bus", "train", "boat", "ship",
                "statue", "monument", "fountain",
                "road", "path", "railway",
                "windmill", "lighthouse", "dock", "pier"
            };
            
            if (classIndex >= 0 && classIndex < detailedObjectClasses.Length)
                return detailedObjectClasses[classIndex];
            
            return "unknown_object";
        }
        
        private Dictionary<string, float> ExtractFeatures(Tensor featureOutput)
        {
            var features = new Dictionary<string, float>();
            
            // Extract key features from the tensor
            if (featureOutput.length >= 8)
            {
                features["density"] = featureOutput[0, 0];
                features["complexity"] = featureOutput[0, 1];
                features["symmetry"] = featureOutput[0, 2];
                features["texture_variance"] = featureOutput[0, 3];
                features["edge_density"] = featureOutput[0, 4];
                features["color_variance"] = featureOutput[0, 5];
                features["pattern_regularity"] = featureOutput[0, 6];
                features["contrast"] = featureOutput[0, 7];
            }
            
            return features;
        }
        
        private float EstimateHeightByType(string terrainType)
        {
            switch (terrainType.ToLower())
            {
                case "mountain":
                case "mountain_peak":
                    return UnityEngine.Random.Range(0.7f, 1.0f);
                case "hill":
                case "rocky_hill":
                case "grassy_hill":
                    return UnityEngine.Random.Range(0.3f, 0.6f);
                case "valley":
                case "canyon":
                    return UnityEngine.Random.Range(-0.3f, 0.0f);
                case "water":
                case "ocean":
                case "lake":
                case "river":
                    return 0.0f;
                default:
                    return UnityEngine.Random.Range(0.0f, 0.2f);
            }
        }
        
        private Texture2D GenerateSimpleHeightMap(ImageSegment segment)
        {
            int size = 64;
            Texture2D heightMap = new Texture2D(size, size, TextureFormat.RFloat, false);
            float[,] heights = new float[size, size];
            
            // Generate simple height pattern based on segment type
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;
                    
                    // Apply mask if available
                    if (segment.mask != null)
                    {
                        int maskX = Mathf.RoundToInt(u * segment.mask.width);
                        int maskY = Mathf.RoundToInt(v * segment.mask.height);
                        
                        if (maskX >= 0 && maskX < segment.mask.width && 
                            maskY >= 0 && maskY < segment.mask.height)
                        {
                            float maskValue = segment.mask.GetPixel(maskX, maskY).a;
                            heights[x, y] = maskValue * 0.5f;
                        }
                    }
                    else
                    {
                        heights[x, y] = 0.5f;
                    }
                }
            }
            
            // Convert to texture
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < size * size; i++)
            {
                int x = i % size;
                int y = i / size;
                float h = heights[x, y];
                pixels[i] = new Color(h, h, h, 1f);
            }
            
            heightMap.SetPixels(pixels);
            heightMap.Apply();
            
            return heightMap;
        }
        
        private Texture2D ConvertTensorToHeightMap(Tensor heightTensor, Rect bounds)
        {
            int width = heightTensor.shape[2];
            int height = heightTensor.shape[1];
            
            Texture2D heightMap = new Texture2D(width, height, TextureFormat.RFloat, false);
            Color[] pixels = new Color[width * height];
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float h = Mathf.Clamp01(heightTensor[0, y, x, 0]);
                    pixels[y * width + x] = new Color(h, h, h, 1f);
                }
            }
            
            heightMap.SetPixels(pixels);
            heightMap.Apply();
            
            return heightMap;
        }
        
        private float CalculateAverageHeight(Tensor heightTensor)
        {
            float sum = 0;
            int count = 0;
            
            for (int i = 0; i < heightTensor.length; i++)
            {
                sum += heightTensor[i];
                count++;
            }
            
            return (sum / count) * maxTerrainHeight;
        }
        
        private Dictionary<string, float> CalculateTopologyFeatures(Tensor heightTensor)
        {
            var features = new Dictionary<string, float>();
            
            // Calculate slope
            float maxSlope = 0;
            float avgSlope = 0;
            int slopeCount = 0;
            
            int width = heightTensor.shape[2];
            int height = heightTensor.shape[1];
            
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    float center = heightTensor[0, y, x, 0];
                    float dx = heightTensor[0, y, x + 1, 0] - heightTensor[0, y, x - 1, 0];
                    float dy = heightTensor[0, y + 1, x, 0] - heightTensor[0, y - 1, x, 0];
                    
                    float slope = Mathf.Sqrt(dx * dx + dy * dy) * maxTerrainHeight / 2f;
                    float slopeAngle = Mathf.Atan(slope) * Mathf.Rad2Deg;
                    
                    maxSlope = Mathf.Max(maxSlope, slopeAngle);
                    avgSlope += slopeAngle;
                    slopeCount++;
                }
            }
            
            features["slope"] = avgSlope / slopeCount;
            features["max_slope"] = maxSlope;
            
            // Calculate roughness
            float variance = 0;
            float mean = CalculateAverageHeight(heightTensor) / maxTerrainHeight;
            
            for (int i = 0; i < heightTensor.length; i++)
            {
                float diff = heightTensor[i] - mean;
                variance += diff * diff;
            }
            
            features["roughness"] = Mathf.Sqrt(variance / heightTensor.length);
            
            return features;
        }
        
        private Vector3 CalculateObjectScale(string objectType, float relativeSize, Rect boundingBox)
        {
            // Base scale multipliers for different object types
            float baseScale = 1f;
            
            switch (objectType.ToLower())
            {
                case "residential_house":
                case "cottage":
                    baseScale = 10f;
                    break;
                case "apartment_building":
                case "skyscraper":
                    baseScale = 25f;
                    break;
                case "medieval_castle":
                case "fortress":
                    baseScale = 40f;
                    break;
                case "tree":
                case "deciduous_tree":
                case "coniferous_tree":
                    baseScale = 5f + relativeSize * 20f;
                    break;
                case "car":
                case "truck":
                    baseScale = 2f;
                    break;
                case "bridge":
                    baseScale = 20f;
                    break;
                default:
                    baseScale = 5f;
                    break;
            }
            
            // Adjust based on aspect ratio
            float aspectRatio = boundingBox.width / boundingBox.height;
            
            return new Vector3(baseScale * Mathf.Min(aspectRatio, 2f), 
                             baseScale, 
                             baseScale * Mathf.Min(1f / aspectRatio, 2f));
        }
        
        private float CalculateObjectRotation(Rect boundingBox, Texture2D mask)
        {
            if (mask == null)
            {
                // Simple rotation based on aspect ratio
                float aspectRatio = boundingBox.width / boundingBox.height;
                if (aspectRatio > 1.5f)
                {
                    return 0f; // Horizontal orientation
                }
                else if (aspectRatio < 0.67f)
                {
                    return 90f; // Vertical orientation
                }
                else
                {
                    return UnityEngine.Random.Range(0f, 360f);
                }
            }
            
            // More sophisticated rotation calculation using mask principal components
            // For now, return random rotation
            return UnityEngine.Random.Range(0f, 360f);
        }
        
        private List<ObjectGrouping> GroupSimilarObjects(List<AnalyzedSegment> segments)
        {
            var groups = new List<ObjectGrouping>();
            var processed = new bool[segments.Count];
            
            for (int i = 0; i < segments.Count; i++)
            {
                if (processed[i]) continue;
                
                var group = new ObjectGrouping
                {
                    groupId = Guid.NewGuid().ToString(),
                    objectType = segments[i].objectType,
                    segments = new List<AnalyzedSegment> { segments[i] }
                };
                
                processed[i] = true;
                
                // Find similar objects
                for (int j = i + 1; j < segments.Count; j++)
                {
                    if (processed[j]) continue;
                    
                    if (IsSimilarObject(segments[i], segments[j]))
                    {
                        group.segments.Add(segments[j]);
                        processed[j] = true;
                    }
                }
                
                groups.Add(group);
            }
            
            return groups;
        }
        
        private bool IsSimilarObject(AnalyzedSegment a, AnalyzedSegment b)
        {
            // Check if same type
            if (a.objectType != b.objectType) return false;
            
            // Check scale similarity
            float scaleDiff = Mathf.Abs(a.estimatedScale.magnitude - b.estimatedScale.magnitude) / Mathf.Max(a.estimatedScale.magnitude, b.estimatedScale.magnitude);
            if (scaleDiff > groupingThreshold) return false;
            
            // Check spatial proximity (optional)
            float distance = Vector2.Distance(a.normalizedPosition, b.normalizedPosition);
            if (distance > 0.2f) return false; // More than 20% of image apart
            
            return true;
        }
        
        private Texture2D GenerateFinalHeightMap(int width, int height)
        {
            Texture2D heightMap = new Texture2D(width, height, TextureFormat.RFloat, false);
            float[,] heights = new float[width, height];
            
            // Initialize with base height
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    heights[x, y] = 0.5f;
                }
            }
            
            // Apply terrain segment heights
            foreach (var segment in analyzedSegments.Where(s => s.isTerrain))
            {
                ApplySegmentHeightToMap(heights, segment, width, height);
            }
            
            // Smooth the height map
            heights = SmoothHeightMap(heights, width, height, 3);
            
            // Convert to texture
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float h = heights[x, y];
                    pixels[y * width + x] = new Color(h, h, h, 1f);
                }
            }
            
            heightMap.SetPixels(pixels);
            heightMap.Apply();
            
            return heightMap;
        }
        
        private void ApplySegmentHeightToMap(float[,] heights, AnalyzedSegment segment, int mapWidth, int mapHeight)
        {
            if (segment.heightMap == null) return;
            
            int startX = Mathf.Max(0, (int)segment.boundingBox.xMin);
            int startY = Mathf.Max(0, (int)segment.boundingBox.yMin);
            int endX = Mathf.Min(mapWidth - 1, (int)segment.boundingBox.xMax);
            int endY = Mathf.Min(mapHeight - 1, (int)segment.boundingBox.yMax);
            
            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    // Sample from segment's height map
                    float u = (x - startX) / (float)(endX - startX);
                    float v = (y - startY) / (float)(endY - startY);
                    
                    Color heightSample = segment.heightMap.GetPixelBilinear(u, v);
                    float segmentHeight = heightSample.r * (segment.estimatedHeight / maxTerrainHeight);
                    
                    // Blend with existing height
                    heights[x, y] = Mathf.Lerp(heights[x, y], segmentHeight, 0.8f);
                }
            }
        }
        
        private float[,] SmoothHeightMap(float[,] heights, int width, int height, int iterations)
        {
            float[,] smoothed = new float[width, height];
            
            for (int iter = 0; iter < iterations; iter++)
            {
                for (int y = 1; y < height - 1; y++)
                {
                    for (int x = 1; x < width - 1; x++)
                    {
                        float sum = 0;
                        int count = 0;
                        
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                sum += heights[x + dx, y + dy];
                                count++;
                            }
                        }
                        
                        smoothed[x, y] = sum / count;
                    }
                }
                
                // Copy edges
                for (int x = 0; x < width; x++)
                {
                    smoothed[x, 0] = heights[x, 0];
                    smoothed[x, height - 1] = heights[x, height - 1];
                }
                
                for (int y = 0; y < height; y++)
                {
                    smoothed[0, y] = heights[0, y];
                    smoothed[width - 1, y] = heights[width - 1, y];
                }
                
                // Swap arrays
                var temp = heights;
                heights = smoothed;
                smoothed = temp;
            }
            
            return heights;
        }
        
        private Texture2D GenerateFinalSegmentationMap(int width, int height)
        {
            Texture2D segMap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with black
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.black;
            }
            
            // Draw each analyzed segment
            int segmentIndex = 0;
            foreach (var segment in analyzedSegments)
            {
                Color segColor = GenerateSegmentColor(segmentIndex++);
                
                int startX = Mathf.Max(0, (int)segment.boundingBox.xMin);
                int startY = Mathf.Max(0, (int)segment.boundingBox.yMin);
                int endX = Mathf.Min(width - 1, (int)segment.boundingBox.xMax);
                int endY = Mathf.Min(height - 1, (int)segment.boundingBox.yMax);
                
                for (int y = startY; y <= endY; y++)
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        if (segment.originalSegment.mask != null)
                        {
                            float u = (x - startX) / (float)(endX - startX);
                            float v = (y - startY) / (float)(endY - startY);
                            
                            Color maskSample = segment.originalSegment.mask.GetPixelBilinear(u, v);
                            if (maskSample.a > 0.5f)
                            {
                                pixels[y * width + x] = segColor;
                            }
                        }
                    }
                }
            }
            
            segMap.SetPixels(pixels);
            segMap.Apply();
            
            return segMap;
        }
        
        private Color GenerateSegmentColor(int index)
        {
            UnityEngine.Random.InitState(index);
            return new Color(
                UnityEngine.Random.Range(0.3f, 0.9f),
                UnityEngine.Random.Range(0.3f, 0.9f),
                UnityEngine.Random.Range(0.3f, 0.9f),
                0.6f
            );
        }
        
        private IEnumerator SendOpenAIRequest(string prompt, Action<string> onSuccess)
        {
            using (UnityWebRequest request = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
            {
                var requestBody = new
                {
                    model = "gpt-4",
                    messages = new[] {
                        new { role = "system", content = "You are an expert in 3D world and terrain generation." },
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 150,
                    temperature = 0.7
                };
                
                string jsonData = JsonUtility.ToJson(requestBody);
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
                
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {openAIApiKey}");
                
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<OpenAIResponse>(request.downloadHandler.text);
                        string enhancedText = response.choices[0].message.content.Trim();
                        onSuccess?.Invoke(enhancedText);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[MapAnalyzer] Failed to parse OpenAI response: {e.Message}");
                        onSuccess?.Invoke("");
                    }
                }
                else
                {
                    Debug.LogWarning($"[MapAnalyzer] OpenAI request failed: {request.error}");
                    onSuccess?.Invoke("");
                }
            }
        }
        
        // Continue with existing helper methods from previous implementation...
        
        private IEnumerator DetectObjectsWithYOLO(Texture2D image, Action<List<DetectedObject>> onComplete, Action<string> onError)
        {
            if (yoloWorker == null)
            {
                onError?.Invoke("YOLO model not loaded");
                yield break;
            }
            
            List<DetectedObject> detectedObjects = new List<DetectedObject>();
            
            try
            {
                // Prepare input tensor
                var inputTensor = PrepareImageTensor(image, 640, 640);
                
                // Run inference
                var inputs = new Dictionary<string, Tensor> { { "images", inputTensor } };
                yoloWorker.Execute(inputs);
                
                // Get output
                var output = yoloWorker.PeekOutput("output0");
                
                // Process detections
                detectedObjects = ProcessYOLOOutput(output, image.width, image.height);
                
                // Apply NMS
                detectedObjects = ApplyNonMaxSuppression(detectedObjects);
                
                // Cleanup
                inputTensor.Dispose();
                output.Dispose();
                
                Debug.Log($"[MapAnalyzer] Detected {detectedObjects.Count} objects with YOLO");
                onComplete?.Invoke(detectedObjects);
            }
            catch (Exception e)
            {
                onError?.Invoke($"YOLO detection error: {e.Message}");
            }
            
            yield return null;
        }
        
        private IEnumerator SegmentImageWithSAM2(Texture2D image, List<DetectedObject> detectedObjects, 
            Action<List<ImageSegment>> onComplete, Action<string> onError)
        {
            if (sam2Worker == null)
            {
                onError?.Invoke("SAM2 model not loaded");
                yield break;
            }
            
            List<ImageSegment> segments = new List<ImageSegment>();

            // Process each detected object with SAM2
            foreach (var obj in detectedObjects)
            {
                try
                {
                    // Convert bounding box to point prompts for SAM2
                    var centerPoint = new Vector2(
                        obj.boundingBox.center.x / image.width,
                        obj.boundingBox.center.y / image.height
                    );
                    
                    // Prepare SAM2 input
                    var imageTensor = PrepareImageTensor(image, segmentationResolution, segmentationResolution);
                    var pointTensor = new Tensor(1, 2, new float[] { centerPoint.x, centerPoint.y });
                    var labelTensor = new Tensor(1, 1, new float[] { 1 }); // Positive point
                    
                    var inputs = new Dictionary<string, Tensor> {
                        { "image", imageTensor },
                        { "point_coords", pointTensor },
                        { "point_labels", labelTensor }
                    };
                    
                    sam2Worker.Execute(inputs);
                    var maskOutput = sam2Worker.PeekOutput("masks");
                    
                    // Create segment from mask
                    var segment = CreateSegmentFromMask(maskOutput, obj, image);
                    if (segment != null)
                    {
                        segments.Add(segment);
                    }
                    
                    // Cleanup
                    imageTensor.Dispose();
                    pointTensor.Dispose();
                    labelTensor.Dispose();
                    maskOutput.Dispose();
                }
                catch (Exception e)
                {
                    onError?.Invoke($"SAM2 segmentation error: {e.Message}");
                    Debug.LogWarning($"[MapAnalyzer] Error processing object {obj.className}: {e.Message}");
                    yield break;
                }
                
                yield return null; // Spread processing over frames
            }
            
            // Merge overlapping segments if enabled
            if (mergeOverlappingSegments)
            {
                segments = MergeOverlappingSegments(segments);
            }
            
            Debug.Log($"[MapAnalyzer] Created {segments.Count} segments with SAM2");
            onComplete?.Invoke(segments);
        }
        
        private Tensor PrepareImageTensor(Texture2D texture, int targetWidth, int targetHeight)
        {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
            Graphics.Blit(texture, rt);
            
            Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply();
            
            var tensor = new Tensor(resized, 3);
            
            RenderTexture.ReleaseTemporary(rt);
            Destroy(resized);
            
            return tensor;
        }
        
        private List<DetectedObject> ProcessYOLOOutput(Tensor output, int imageWidth, int imageHeight)
        {
            var detections = new List<DetectedObject>();
            var outputData = output.ToReadOnlyArray();
            
            // YOLO output format: [batch, num_detections, 85] where 85 = 4 bbox + 1 obj_conf + 80 classes
            int numDetections = output.shape[1];
            int detectionSize = output.shape[2];
            
            for (int i = 0; i < numDetections && i < maxObjectsToProcess; i++)
            {
                float objectness = outputData[i * detectionSize + 4];
                
                if (objectness < confidenceThreshold)
                    continue;
                
                // Find best class
                int bestClass = -1;
                float bestScore = 0;
                
                for (int c = 0; c < 80; c++)
                {
                    float score = outputData[i * detectionSize + 5 + c] * objectness;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }
                
                if (bestClass >= 0 && bestScore > confidenceThreshold)
                {
                    // Convert YOLO bbox to Unity Rect
                    float cx = outputData[i * detectionSize + 0] * imageWidth;
                    float cy = outputData[i * detectionSize + 1] * imageHeight;
                    float w = outputData[i * detectionSize + 2] * imageWidth;
                    float h = outputData[i * detectionSize + 3] * imageHeight;
                    
                    detections.Add(new DetectedObject {
                        boundingBox = new Rect(cx - w/2, cy - h/2, w, h),
                        className = GetClassName(bestClass),
                        confidence = bestScore,
                        classIndex = bestClass
                    });
                }
            }
            
            return detections;
        }
        
        private List<DetectedObject> ApplyNonMaxSuppression(List<DetectedObject> detections)
        {
            var sorted = detections.OrderByDescending(d => d.confidence).ToList();
            var kept = new List<DetectedObject>();
            
            while (sorted.Count > 0)
            {
                var best = sorted[0];
                kept.Add(best);
                sorted.RemoveAt(0);
                
                sorted.RemoveAll(d => CalculateIoU(best.boundingBox, d.boundingBox) > iouThreshold);
            }
            
            return kept;
        }
        
        private float CalculateIoU(Rect a, Rect b)
        {
            float intersectionArea = Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin)) *
                                   Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
            
            float unionArea = a.width * a.height + b.width * b.height - intersectionArea;
            
            return unionArea > 0 ? intersectionArea / unionArea : 0;
        }
        
        private string GetClassName(int classIndex)
        {
            // Extended COCO class names including terrain and map-specific objects
            string[] cocoClassNames = new string[] {
                "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
                "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
                "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
                "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
                "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
                "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
                "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake",
                "chair", "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop",
                "mouse", "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink",
                "refrigerator", "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush",
                "tree", "building", "bridge", "castle", "house", "tower", "lighthouse", "mountain",
                "hill", "water", "ocean", "lake", "river", "forest", "field", "road", "path"
            };
            
            if (classIndex >= 0 && classIndex < cocoClassNames.Length)
                return cocoClassNames[classIndex];
            return "unknown";
        }
        
        private ImageSegment CreateSegmentFromMask(Tensor maskOutput, DetectedObject obj, Texture2D sourceImage)
        {
            // Convert tensor mask to Texture2D
            int maskWidth = maskOutput.shape[2];
            int maskHeight = maskOutput.shape[1];
            
            Texture2D maskTexture = new Texture2D(maskWidth, maskHeight, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[maskWidth * maskHeight];
            
            for (int y = 0; y < maskHeight; y++)
            {
                for (int x = 0; x < maskWidth; x++)
                {
                    float maskValue = maskOutput[0, y, x, 0];
                    pixels[y * maskWidth + x] = new Color(1, 1, 1, maskValue);
                }
            }
            
            maskTexture.SetPixels(pixels);
            maskTexture.Apply();
            
            return new ImageSegment {
                detectedObject = obj,
                mask = maskTexture,
                boundingBox = obj.boundingBox,
                color = GenerateSegmentColor(obj.classIndex),
                area = CalculateSegmentArea(maskOutput)
            };
        }
        
        private float CalculateSegmentArea(Tensor mask)
        {
            float area = 0;
            for (int i = 0; i < mask.length; i++)
            {
                if (mask[i] > 0.5f) area++;
            }
            return area / mask.length;
        }
        
        private List<ImageSegment> MergeOverlappingSegments(List<ImageSegment> segments)
        {
            var merged = new List<ImageSegment>();
            var processed = new bool[segments.Count];
            
            for (int i = 0; i < segments.Count; i++)
            {
                if (processed[i]) continue;
                
                var current = segments[i];
                var group = new List<ImageSegment> { current };
                processed[i] = true;
                
                // Find overlapping segments of the same class
                for (int j = i + 1; j < segments.Count; j++)
                {
                    if (processed[j]) continue;
                    
                    if (segments[j].detectedObject.className == current.detectedObject.className &&
                        CalculateIoU(current.boundingBox, segments[j].boundingBox) > 0.3f)
                    {
                        group.Add(segments[j]);
                        processed[j] = true;
                    }
                }
                
                // Merge the group
                if (group.Count > 1)
                {
                    merged.Add(MergeSegmentGroup(group));
                }
                else
                {
                    merged.Add(current);
                }
            }
            
            return merged;
        }
        
        private ImageSegment MergeSegmentGroup(List<ImageSegment> group)
        {
            // Calculate merged bounding box
            float minX = group.Min(s => s.boundingBox.xMin);
            float minY = group.Min(s => s.boundingBox.yMin);
            float maxX = group.Max(s => s.boundingBox.xMax);
            float maxY = group.Max(s => s.boundingBox.yMax);
            
            var mergedBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            
            // Use the highest confidence detection
            var best = group.OrderByDescending(s => s.detectedObject.confidence).First();
            
            // Merge masks
            Texture2D mergedMask = MergeMasks(group, mergedBounds);
            
            return new ImageSegment {
                detectedObject = best.detectedObject,
                mask = mergedMask,
                boundingBox = mergedBounds,
                color = best.color,
                area = group.Sum(s => s.area)
            };
        }
        
        private Texture2D MergeMasks(List<ImageSegment> segments, Rect mergedBounds)
        {
            int width = Mathf.RoundToInt(mergedBounds.width);
            int height = Mathf.RoundToInt(mergedBounds.height);
            
            Texture2D mergedMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with transparent
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(1, 1, 1, 0);
            }
            
            // Merge each segment mask
            foreach (var segment in segments)
            {
                if (segment.mask == null) continue;
                
                int offsetX = Mathf.RoundToInt(segment.boundingBox.x - mergedBounds.x);
                int offsetY = Mathf.RoundToInt(segment.boundingBox.y - mergedBounds.y);
                
                for (int y = 0; y < segment.mask.height; y++)
                {
                    for (int x = 0; x < segment.mask.width; x++)
                    {
                        int targetX = offsetX + Mathf.RoundToInt(x * segment.boundingBox.width / segment.mask.width);
                        int targetY = offsetY + Mathf.RoundToInt(y * segment.boundingBox.height / segment.mask.height);
                        
                        if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                        {
                            Color maskPixel = segment.mask.GetPixel(x, y);
                            int idx = targetY * width + targetX;
                            
                            // Use maximum alpha
                            if (maskPixel.a > pixels[idx].a)
                            {
                                pixels[idx] = maskPixel;
                            }
                        }
                    }
                }
            }
            
            mergedMask.SetPixels(pixels);
            mergedMask.Apply();
            
            return mergedMask;
        }
    }
    
    // Extension classes for Terrain and Model generation integration
    
    public class TerrainModification
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
    
    public class ModelGenerationRequest
    {
        public string objectType;
        public string description;
        public Vector2 position;
        public Vector3 scale;
        public float rotation;
        public float confidence;
        public bool isGrouped;
        public string groupId;
    }
    
    // Extension methods for TerrainGenerator
    public static class TerrainGeneratorExtensions
    {
        public static IEnumerator ApplyTerrainModifications(this TerrainGenerator terrainGen, 
            List<TerrainModification> modifications, Texture2D sourceImage)
        {
            if (terrainGen == null)
            {
                Debug.LogWarning("[MapAnalyzer] TerrainGenerator not found, skipping terrain modifications.");
                yield break;
            }

            foreach (var mod in modifications)
            {
                // This is where you would call the actual terrain modification method on your TerrainGenerator
                // For example: terrainGen.ApplyModification(mod);
                yield return null; // Process one modification per frame
            }
        }
    }
}
