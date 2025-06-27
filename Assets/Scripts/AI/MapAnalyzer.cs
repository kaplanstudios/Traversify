using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.AI.Inference;

namespace Traversify.AI {

    [RequireComponent(typeof(Core.TraversifyDebugger))]
    public class MapAnalyzer : MonoBehaviour {
        // Singleton
        private static MapAnalyzer _instance;
        public static MapAnalyzer Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<MapAnalyzer>();
                    if (_instance == null) {
                        GameObject go = new GameObject("MapAnalyzer");
                        _instance = go.AddComponent<MapAnalyzer>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("Model Assets")]
        public ModelAsset yoloModel;
        public ModelAsset sam2Model;
        public ModelAsset fasterRcnnModel;

        [Header("Detection Settings")]
        [Range(0f,1f)] public float confidenceThreshold = 0.5f;
        [Range(0f,1f)] public float nmsThreshold = 0.45f;
        public bool useHighQuality = true;
        public bool useGPU = true;
        public int maxObjectsToProcess = 100;

        [Header("OpenAI Settings")]
        [Tooltip("API key for description enhancement")]
        public string openAIApiKey;

        // Internal
        [NonSerialized] public Core.TraversifyDebugger debugger;
        private IInferenceSession _yoloSession;
        private IInferenceSession _sam2Session;
        private IInferenceSession _rcnnSession;
        private string[] _classLabels;

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            debugger = GetComponent<Core.TraversifyDebugger>();
            if (debugger == null) debugger = gameObject.AddComponent<Core.TraversifyDebugger>();

            LoadClassLabels();
            InitializeModels();
        }

        private void LoadClassLabels() {
            // If a labels file is provided, parse it here
            // (Could be set via inspector using a TextAsset)
            // Placeholder: no labels loaded
            _classLabels = new string[0];
        }

        private void InitializeModels() {
            try {
                var options = new InferenceOptions {
                    Device = useGPU ? InferenceDevice.GPU : InferenceDevice.CPU
                };
                if (yoloModel != null) {
                    _yoloSession = new ModelImporter(yoloModel).LoadSession(options);
                    debugger.Log("YOLO session initialized", Core.LogCategory.AI);
                } else {
                    debugger.LogWarning("YOLO model asset not assigned", Core.LogCategory.AI);
                }
                if (sam2Model != null) {
                    _sam2Session = new ModelImporter(sam2Model).LoadSession(options);
                    debugger.Log("SAM2 session initialized", Core.LogCategory.AI);
                }
                if (fasterRcnnModel != null) {
                    _rcnnSession = new ModelImporter(fasterRcnnModel).LoadSession(options);
                    debugger.Log("Faster R-CNN session initialized", Core.LogCategory.AI);
                }
            } catch (Exception ex) {
                debugger.LogError($"Failed to initialize AI sessions: {ex.Message}", Core.LogCategory.AI);
            }
        }

        /// <summary>
        /// Analyze the given map texture: detect objects, segment terrain & objects, classify & enhance descriptions.
        /// Reports progress callbacks and returns AnalysisResults via onComplete.
        /// </summary>
        public IEnumerator AnalyzeImage(
            Texture2D map,
            Action<AnalysisResults> onComplete,
            Action<string> onError,
            Action<string, float> onProgress
        ) {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try {
                // Step 1: YOLO detection
                onProgress?.Invoke("YOLO detection", 0f);
                var detections = RunYoloDetection(map, onProgress);
                if (detections == null || detections.Count == 0) {
                    debugger.Log("No objects detected by YOLO", Core.LogCategory.AI);
                }
                onProgress?.Invoke("YOLO detection", 0.2f);

                // Step 2: SAM2 segmentation
                onProgress?.Invoke("SAM segmentation", 0.2f);
                var segments = RunSamSegmentation(map, detections, onProgress);
                onProgress?.Invoke("SAM segmentation", 0.4f);

                // Step 3: Faster R-CNN refinement
                onProgress?.Invoke("RCNN classification", 0.4f);
                if (_rcnnSession != null) {
                    // Placeholder: actual RCNN classification would go here
                    yield return null;
                }
                onProgress?.Invoke("RCNN classification", 0.6f);

                // Step 4: OpenAI enhancement
                onProgress?.Invoke("Enhancing descriptions", 0.6f);
                if (!string.IsNullOrEmpty(openAIApiKey)) {
                    // Placeholder: call OpenAIResponse to enhance each segment's text
                    yield return null;
                }
                onProgress?.Invoke("Enhancing descriptions", 0.8f);

                // Step 5: Package results
                var results = BuildResults(map, detections, segments);
                onProgress?.Invoke("Finishing analysis", 1f);
                onComplete?.Invoke(results);
                debugger.Log($"Analysis completed in {stopwatch.Elapsed.TotalSeconds:F2}s", Core.LogCategory.AI);
            } catch (Exception ex) {
                debugger.LogError($"MapAnalyzer error: {ex.Message}", Core.LogCategory.AI);
                onError?.Invoke(ex.Message);
            }
        }

        private List<DetectedObject> RunYoloDetection(Texture2D map, Action<string,float> onProgress) {
            if (_yoloSession == null) return new List<DetectedObject>();
            // Preprocess texture to tensor
            var input = TensorUtils.Preprocess(map, useHighQuality ? 1024 : 640);
            _yoloSession.Run(input);
            var output = _yoloSession.Fetch("output");
            var detections = YoloDecoder.Decode(output, confidenceThreshold, nmsThreshold, map.width, map.height);
            onProgress?.Invoke("YOLO detection", 0.2f);
            input.Dispose();
            output.Dispose();
            return detections.Take(maxObjectsToProcess).ToList();
        }

        private List<ImageSegment> RunSamSegmentation(
            Texture2D map,
            List<DetectedObject> detections,
            Action<string, float> onProgress
        ) {
            var segments = new List<ImageSegment>();
            if (_sam2Session == null) {
                // Fallback: produce rectangular masks equal to bounding boxes
                foreach (var det in detections) {
                    var mask = TextureUtils.CreateSolidMask(det.boundingBox, map.width, map.height);
                    segments.Add(new ImageSegment {
                        detectedObject = det,
                        mask = mask,
                        boundingBox = det.boundingBox,
                        color = UnityEngine.Random.ColorHSV(0f,1f,0.6f,1f),
                        area = det.boundingBox.width * det.boundingBox.height
                    });
                }
                return segments;
            }

            int count = detections.Count;
            for (int i = 0; i < count; i++) {
                var det = detections[i];
                onProgress?.Invoke("SAM segmentation", 0.2f + 0.2f * (i+1)/count);
                // Build prompt tensor (center point)
                var centerN = new Vector2(
                    (det.boundingBox.x + det.boundingBox.width/2f) / map.width,
                    (det.boundingBox.y + det.boundingBox.height/2f) / map.height
                );
                var prompt = new Tensor(new []{1,2}, new []{centerN.x, centerN.y});
                var input = TensorUtils.Preprocess(map, useHighQuality ? 1024 : 640);
                _sam2Session.Run(new TensorValuePair("image", input), new TensorValuePair("prompt", prompt));
                var maskTensor = _sam2Session.Fetch("masks");
                var maskTex = TensorUtils.DecodeMask(maskTensor);
                input.Dispose();
                prompt.Dispose();
                maskTensor.Dispose();

                segments.Add(new ImageSegment {
                    detectedObject = det,
                    mask = maskTex,
                    boundingBox = det.boundingBox,
                    color = UnityEngine.Random.ColorHSV(0f,1f,0.6f,1f),
                    area = det.boundingBox.width * det.boundingBox.height
                });
            }
            return segments;
        }

        private AnalysisResults BuildResults(
            Texture2D map,
            List<DetectedObject> detections,
            List<ImageSegment> segments
        ) {
            var results = new AnalysisResults {
                mapObjects = new List<MapObject>(),
                terrainFeatures = new List<TerrainFeature>(),
                objectGroups = new List<ObjectGroup>()
            };

            // Separate terrain vs objects by class prefix or other logic
            foreach (var seg in segments) {
                if (seg.detectedObject.className.StartsWith("terrain")) {
                    results.terrainFeatures.Add(new TerrainFeature {
                        label = seg.detectedObject.className,
                        boundingBox = seg.boundingBox,
                        segmentMask = seg.mask,
                        segmentColor = seg.color,
                        confidence = seg.detectedObject.confidence,
                        elevation = seg.area / (map.width*map.height)
                    });
                } else {
                    results.mapObjects.Add(new MapObject {
                        type = seg.detectedObject.className,
                        label = seg.detectedObject.className,
                        enhancedDescription = seg.detectedObject.className,
                        position = new Vector2(
                            (seg.boundingBox.x + seg.boundingBox.width/2f)/map.width,
                            1f - (seg.boundingBox.y + seg.boundingBox.height/2f)/map.height
                        ),
                        boundingBox = seg.boundingBox,
                        segmentMask = seg.mask,
                        segmentColor = seg.color,
                        confidence = seg.detectedObject.confidence,
                        scale = Vector3.one,
                        rotation = 0f,
                        isGrouped = false
                    });
                }
            }

            // Group similar objects
            results.objectGroups = results.mapObjects
                .GroupBy(o => o.type)
                .Select(g => new ObjectGroup {
                    groupId = Guid.NewGuid().ToString(),
                    type = g.Key,
                    objects = g.ToList()
                })
                .ToList();

            // Build height and segmentation textures
            results.heightMap = TextureUtils.BuildHeightMap(map.width, map.height, results.terrainFeatures);
            results.segmentationMap = TextureUtils.BuildSegmentationMap(map.width, map.height, segments);

            return results;
        }
    }
}
// --- MapAnalyzer.cs: Part 2 of 3 ---
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
 
                // Wait one frame to throttle execution
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
            if (heightWorker == null)
            {
                // Simple height estimation based on terrain type
                result.estimatedHeight = EstimateHeightByType(result.objectType);
                result.heightMap = GenerateSimpleHeightMap(request.segment);
                yield break;
            }
            
            var segmentTexture = ExtractSegmentTexture(request.sourceImage, request.segment);
            var inputTensor = PrepareImageTensor(segmentTexture, 256, 256);
            
            var inputs = new Dictionary<string, Tensor> { { "image", inputTensor } };
            heightWorker.Execute(inputs);
            
            var heightOutput = heightWorker.PeekOutput("height_map");
            
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
// --- MapAnalyzer.cs: Part 3 of 3 ---

        #region Helpers & Utilities

        private Texture2D ExtractSegmentTexture(Texture2D source, ImageSegment segment)
        {
            var bb = segment.boundingBox;
            var tex = new Texture2D((int)bb.width, (int)bb.height, TextureFormat.RGBA32, false);
            tex.SetPixels(source.GetPixels((int)bb.x, (int)bb.y, (int)bb.width, (int)bb.height));
            tex.Apply();
            return tex;
        }

        private Tensor PrepareImageTensor(Texture2D tex, int targetW, int targetH)
        {
            // Resize to target dimensions
            RenderTexture rt = RenderTexture.GetTemporary(targetW, targetH, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D scaled = new Texture2D(targetW, targetH, TextureFormat.RGBA32, false);
            scaled.ReadPixels(new Rect(0, 0, targetW, targetH), 0, 0);
            scaled.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            // Convert to Tensor (NHWC)
            var floatVals = new float[targetW * targetH * 3];
            Color32[] pix = scaled.GetPixels32();
            for (int i = 0; i < pix.Length; i++)
            {
                floatVals[i * 3 + 0] = pix[i].r / 255f;
                floatVals[i * 3 + 1] = pix[i].g / 255f;
                floatVals[i * 3 + 2] = pix[i].b / 255f;
            }
            Destroy(scaled);
            return new Tensor(1, targetH, targetW, 3, floatVals);
        }

        private bool IsTerrainClass(string className)
        {
            string lower = className.ToLowerInvariant();
            return lower.Contains("terrain") || lower.Contains("grass") || lower.Contains("water") || lower.Contains("mountain");
        }

        private float EstimateHeightByType(string type)
        {
            // Simple mapping by type
            if (type.Contains("mountain")) return 50f;
            if (type.Contains("hill")) return 20f;
            if (type.Contains("valley")) return 5f;
            return 10f;
        }

        private Texture2D GenerateSimpleHeightMap(ImageSegment segment)
        {
            int w = (int)segment.boundingBox.width;
            int h = (int)segment.boundingBox.height;
            var map = new Texture2D(w, h, TextureFormat.RFloat, false);
            float baseHeight = EstimateHeightByType(segment.detectedObject.className) / maxTerrainHeight;
            Color fill = new Color(baseHeight, 0, 0, 0);
            var cols = Enumerable.Repeat(fill, w * h).ToArray();
            map.SetPixels(cols);
            map.Apply();
            return map;
        }

        private float CalculateAverageHeight(Tensor heightTensor)
        {
            float sum = 0f;
            int count = heightTensor.length;
            for (int i = 0; i < count; i++) sum += heightTensor[i];
            return sum / count * maxTerrainHeight;
        }

        private Dictionary<string, float> CalculateTopologyFeatures(Tensor heightTensor)
        {
            // Compute simple slope and roughness metrics
            int w = heightTensor.shape[2];
            int h = heightTensor.shape[1];
            float totalSlope = 0f, totalDiff = 0f;
            int count = 0;
            for (int y = 0; y < h - 1; y++)
            {
                for (int x = 0; x < w - 1; x++)
                {
                    float a = heightTensor[0, y, x, 0];
                    float b = heightTensor[0, y + 1, x, 0];
                    float c = heightTensor[0, y, x + 1, 0];
                    float slope = Mathf.Atan(Mathf.Abs(b - a) * terrainSize.y / terrainSize.x) * Mathf.Rad2Deg;
                    totalSlope += slope;
                    totalDiff += Mathf.Abs(c - a);
                    count++;
                }
            }
            return new Dictionary<string, float>
            {
                ["slope"] = totalSlope / count,
                ["roughness"] = totalDiff / count * terrainSize.y
            };
        }

        private string ProcessDetailedClassification(Tensor classTensor, bool isTerrain)
        {
            // Simple argmax on first N classes
            int numClasses = isTerrain ? terrainClassCount : objectClassCount;
            float maxVal = float.MinValue;
            int maxIdx = 0;
            for (int i = 0; i < numClasses; i++)
            {
                if (classTensor[0, i] > maxVal)
                {
                    maxVal = classTensor[0, i];
                    maxIdx = i;
                }
            }
            if (_classLabels != null && maxIdx < _classLabels.Length)
                return _classLabels[maxIdx];
            return isTerrain ? "terrain_feature" : "object";
        }

        private Dictionary<string, float> ExtractFeatures(Tensor featureTensor)
        {
            // Flatten top-K features
            var dict = new Dictionary<string, float>();
            int dims = featureTensor.length;
            for (int i = 0; i < Mathf.Min(10, dims); i++)
            {
                dict[$"f{i}"] = featureTensor[i];
            }
            return dict;
        }

        private float CalculateObjectScale(string type, float areaRatio, Rect bb)
        {
            // Heuristic: larger bounding boxes => larger scale
            return Mathf.Lerp(0.5f, 3f, Mathf.Clamp01(areaRatio * 10f));
        }

        private float CalculateObjectRotation(Rect bb, Texture2D mask)
        {
            // Estimate major axis angle via PCA on mask pixels
            var pts = new List<Vector2>();
            var pixels = mask.GetPixels32();
            int w = mask.width;
            int h = mask.height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (pixels[y * w + x].a > 128)
                        pts.Add(new Vector2(x, y));
            if (pts.Count < 2) return 0f;
            // Compute covariance
            Vector2 mean = Vector2.zero;
            foreach (var p in pts) mean += p;
            mean /= pts.Count;
            float xx=0, xy=0, yy=0;
            foreach (var p in pts)
            {
                var d = p - mean;
                xx += d.x * d.x;
                xy += d.x * d.y;
                yy += d.y * d.y;
            }
            float theta = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
            return theta * Mathf.Rad2Deg;
        }

        private Texture2D ConvertTensorToHeightMap(Tensor tensor, Rect bb)
        {
            int w = tensor.shape[2], h = tensor.shape[1];
            var tex = new Texture2D((int)bb.width, (int)bb.height, TextureFormat.RFloat, false);
            var pixels = new Color[(int)bb.width * (int)bb.height];
            for (int y = 0; y < (int)bb.height; y++)
                for (int x = 0; x < (int)bb.width; x++)
                    pixels[y * (int)bb.width + x] = new Color(tensor[0, y, x, 0], 0, 0, 0);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private IEnumerator SendOpenAIRequest(string prompt, Action<string> onResponse)
        {
            // Delegate to singleton OpenAIResponse
            bool done = false;
            OpenAIResponse.Instance.SendPrompt(prompt, response => {
                onResponse?.Invoke(response);
                done = true;
            }, error => {
                Debug.LogError($"OpenAI error: {error}");
                done = true;
            });
            while (!done) yield return null;
        }

        private AnalysisResults ConvertAnalyzedSegmentsToResults(Texture2D map)
        {
            var results = new AnalysisResults
            {
                mapObjects = new List<MapObject>(),
                terrainFeatures = new List<TerrainFeature>(),
                objectGroups = new List<ObjectGroup>()
            };

            foreach (var seg in analyzedSegments)
            {
                if (seg.isTerrain)
                {
                    results.terrainFeatures.Add(new TerrainFeature
                    {
                        label = seg.objectType,
                        segmentMask = seg.originalSegment.mask,
                        boundingBox = seg.boundingBox,
                        elevation = seg.estimatedHeight,
                        description = seg.enhancedDescription
                    });
                }
                else
                {
                    results.mapObjects.Add(new MapObject
                    {
                        type = seg.objectType,
                        label = seg.detailedClassification,
                        description = seg.enhancedDescription,
                        position = seg.normalizedPosition,
                        scale = Vector3.one * seg.estimatedScale,
                        rotation = seg.estimatedRotation,
                        boundingBox = seg.boundingBox
                    });
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

            return results;
        }

        #endregion
        #region Private Methods
        private float CalculateObjectRotation(Rect bb, Texture2D mask)
        {
            // Estimate major axis angle via PCA on mask pixels
            var pts = new List<Vector2>();
            Color32[] pixels = mask.GetPixels32();
            int w = mask.width, h = mask.height;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (pixels[y * w + x].a > 128)
                        pts.Add(new Vector2(x, y));
                }
            }
            if (pts.Count < 2) return 0f;

            // Compute covariance matrix
            Vector2 mean = Vector2.zero;
            foreach (var p in pts) mean += p;
            mean /= pts.Count;

            float mxx = 0f, mxy = 0f, myy = 0f;
            foreach (var p in pts)
            {
                var d = p - mean;
                mxx += d.x * d.x;
                mxy += d.x * d.y;
                myy += d.y * d.y;
            }
            // Solve for principal axis
            float theta = 0.5f * Mathf.Atan2(2f * mxy, mxx - myy);
            return theta * Mathf.Rad2Deg;
        }

        private IEnumerator SendOpenAIRequest(string prompt, Action<string> onResponse)
        {
            // Uses singleton OpenAIResponse to send and receive completion
            bool done = false;
            string result = null;
            Traversify.AI.OpenAIResponse.Instance.RequestCompletion(prompt, 
                response => { result = response; done = true; }, 
                error => { Debug.LogError($"OpenAI error: {error}"); done = true; });
            while (!done) yield return null;
            onResponse?.Invoke(result);
        }

        private AnalysisResults ConvertAnalyzedSegmentsToResults(Texture2D sourceImage)
        {
            var results = new AnalysisResults();
            results.mapObjects = new List<MapObject>();
            results.terrainFeatures = new List<TerrainFeature>();
            results.objectGroups = new List<ObjectGroup>();

            // Split analyzed segments
            foreach (var seg in analyzedSegments)
            {
                if (seg.isTerrain)
                {
                    results.terrainFeatures.Add(new TerrainFeature
                    {
                        label = seg.objectType,
                        boundingBox = seg.boundingBox,
                        segmentMask = seg.heightMap,  // using heightMap as mask placeholder
                        elevation = seg.estimatedHeight,
                        metadata = new Dictionary<string, object>
                        {
                            { "slope", seg.topologyFeatures?["slope"] ?? 0f },
                            { "description", seg.enhancedDescription }
                        }
                    });
                }
                else
                {
                    results.mapObjects.Add(new MapObject
                    {
                        type = seg.objectType,
                        position = seg.normalizedPosition,
                        scale = Vector3.one * seg.estimatedScale,
                        rotation = seg.estimatedRotation,
                        label = seg.objectType,
                        enhancedDescription = seg.enhancedDescription,
                        confidence = seg.placementConfidence,
                        metadata = new Dictionary<string, object>
                        {
                            { "features", seg.features },
                            { "description", seg.enhancedDescription }
                        }
                    });
                }
            }

            // Create object groups by type
            var groups = results.mapObjects
                .GroupBy(o => o.type)
                .Select(g => new ObjectGroup
                {
                    groupId = Guid.NewGuid().ToString(),
                    type = g.Key,
                    objects = g.ToList()
                })
                .ToList();
            results.objectGroups = groups;

            // Attach final textures
            results.heightMap = TextureUtils.BuildGlobalHeightMap(sourceImage.width, sourceImage.height, results.terrainFeatures);
            results.segmentationMap = TextureUtils.BuildGlobalSegmentationMap(sourceImage.width, sourceImage.height, analyzedSegments);

            return results;
        }

        #endregion
    }
}
