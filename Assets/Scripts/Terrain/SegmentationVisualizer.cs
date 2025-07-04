using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using TMPro;
using Traversify.AI;
using Traversify.Core;

namespace Traversify {
    [RequireComponent(typeof(TraversifyDebugger))]
    public class SegmentationVisualizer : TraversifyComponent {
        #region Configuration
        [Header("Debug & Progress")]
        public TraversifyDebugger debugger;
        public bool enableDebugVisualization = true;
        public bool showPerformanceStats = true;
        
        [Header("Overlay Settings")]
        public GameObject overlayPrefab;
        public Material overlayMaterial;
        [Range(0f, 5f)] public float overlayYOffset = 0.1f;
        [Range(0f, 3f)] public float fadeDuration = 0.5f;
        [Range(0f, 1f)] public float overlayOpacity = 0.6f;
        public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public bool useDistanceBasedOpacity = true;
        [Range(10f, 200f)] public float fadeStartDistance = 50f;
        [Range(50f, 500f)] public float fadeEndDistance = 200f;
        
        [Header("Label Settings")]
        public GameObject labelPrefab;
        [Range(0.5f, 5f)] public float labelYOffset = 2f;
        public TMP_FontAsset labelFont; // Changed from Font to TMP_FontAsset
        public int labelFontSize = 24;
        public Color labelTextColor = Color.white;
        public Color labelBackgroundColor = new Color(0, 0, 0, 0.8f);
        public bool billboardLabels = true;
        public bool scaleLabelsWithDistance = true;
        [Range(0.1f, 2f)] public float labelMinScale = 0.5f;
        [Range(1f, 5f)] public float labelMaxScale = 2f;
        
        [Header("Visualization Modes")]
        public VisualizationMode visualizationMode = VisualizationMode.Overlay;
        public bool showSegmentBorders = true;
        public float borderWidth = 2f;
        public Color borderColor = Color.white;
        public bool animateSegments = true;
        public float animationSpeed = 1f;
        public AnimationStyle animationStyle = AnimationStyle.Pulse;
        
        [Header("Interaction")]
        public bool enableInteraction = true;
        public LayerMask interactionLayerMask = -1;
        public float interactionDistance = 100f;
        public bool highlightOnHover = true;
        public Color highlightColor = Color.yellow;
        public float highlightIntensity = 0.3f;
        public bool showTooltips = true;
        public GameObject tooltipPrefab;
        
        [Header("Segment Grouping")]
        public bool groupSimilarSegments = true;
        public float groupingSimilarityThreshold = 0.8f;
        public bool showGroupConnections = true;
        public LineRenderer connectionLinePrefab;
        public Color connectionColor = new Color(1, 1, 1, 0.3f);
        
        [Header("Performance")]
        public bool useLOD = true;
        public float[] lodDistances = { 25f, 50f, 100f, 200f };
        public bool batchSegments = true;
        public int maxSegmentsPerBatch = 50;
        public bool useGPUInstancing = true;
        public bool cullInvisibleSegments = true;
        public float cullingUpdateInterval = 0.5f;
        
        [Header("Effects")]
        public bool usePostProcessing = true;
        public Material outlinePostProcessMaterial;
        public bool castShadows = false;
        public bool receiveShadows = false;
        public bool useVertexColors = true;
        
        [Header("Data Display")]
        public bool showSegmentInfo = true;
        public InfoDisplayMode infoDisplayMode = InfoDisplayMode.Simplified;
        public bool showConfidenceIndicators = true;
        public Gradient confidenceGradient;
        public bool showAreaMetrics = true;
        public bool showElevationData = true;
        #endregion

        #region Enums and Data Structures
        public enum VisualizationMode {
            Overlay, Outline, Fill, Wireframe, Heatmap, Contour
        }
        
        public enum AnimationStyle {
            None, Pulse, Wave, Rotate, Float, Shimmer
        }
        
        public enum InfoDisplayMode {
            None, Simplified, Detailed, Technical
        }
        
        [Serializable]
        public class SegmentVisualization {
            public string id;
            public GameObject overlayObject;
            public GameObject labelObject;
            public List<GameObject> borderObjects;
            public Material instanceMaterial;
            public Mesh segmentMesh;
            public Bounds bounds;
            public float opacity = 0f;
            public bool isVisible = true;
            public bool isHighlighted = false;
            public float animationPhase = 0f;
            public LODGroup lodGroup;
            public int currentLOD = 0;
            public TerrainFeature terrainFeature;
            public MapObject mapObject;
            public List<Vector3> borderPoints;
            public Dictionary<string, object> metadata;
        }
        
        [Serializable]
        public class SegmentGroup {
            public string groupId;
            public List<SegmentVisualization> segments;
            public Vector3 centerPoint;
            public Color groupColor;
            public List<GameObject> connectionLines;
            public bool isExpanded = true;
        }
        
        [Serializable]
        public class InteractionState {
            public SegmentVisualization hoveredSegment;
            public SegmentVisualization selectedSegment;
            public GameObject tooltipInstance;
            public float hoverTime;
            public Vector3 lastMousePosition;
        }
        
        [Serializable]
        public class PerformanceMetrics {
            public int totalSegments;
            public int visibleSegments;
            public int culledSegments;
            public float averageFrameTime;
            public float peakMemoryUsage;
            public int drawCalls;
            public int triangleCount;
            public int vertexCount;
        }
        #endregion

        #region Private Fields
        private Dictionary<string, SegmentVisualization> _segmentVisualizations;
        private Dictionary<string, SegmentGroup> _segmentGroups;
        private List<GameObject> _createdObjects;
        private InteractionState _interactionState;
        private PerformanceMetrics _performanceMetrics;
        private Camera _mainCamera;
        private Canvas _uiCanvas;
        private MaterialPropertyBlock _propertyBlock;
        private ComputeShader _segmentationShader;
        private RenderTexture _segmentationRT;
        private Coroutine _animationCoroutine;
        private Coroutine _cullingCoroutine;
        private Coroutine _interactionCoroutine;
        private JobHandle _currentJob;
        private NativeArray<float3> _segmentPositions;
        private NativeArray<float> _segmentVisibility;
        private bool _isProcessing = false;
        
        // Initialize collections
        private Dictionary<string, SegmentVisualization> _activeSegments = new Dictionary<string, SegmentVisualization>();
        private Camera _camera;
        
        // Additional fields for TraversifyComponent implementation
        private Dictionary<string, GameObject> _segmentOverlays = new Dictionary<string, GameObject>();
        private Dictionary<string, GameObject> _segmentLabels = new Dictionary<string, GameObject>();
        private List<SegmentVisualization> _fadingSegments = new List<SegmentVisualization>();
        private bool _isVisualizationActive = false;
        private AnalysisResults _currentAnalysisResults = null;
        #endregion

        #region Initialization
        private void Awake() {
            Initialize();
        }
        
        private void Initialize() {
            debugger = GetComponent<TraversifyDebugger>();
            if (debugger == null) {
                debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            _segmentVisualizations = new Dictionary<string, SegmentVisualization>();
            _segmentGroups = new Dictionary<string, SegmentGroup>();
            _createdObjects = new List<GameObject>();
            _interactionState = new InteractionState();
            _performanceMetrics = new PerformanceMetrics();
            _propertyBlock = new MaterialPropertyBlock();
            
            _mainCamera = Camera.main;
            if (_mainCamera == null) {
                _mainCamera = FindObjectOfType<Camera>();
            }
            
            SetupUI();
            LoadResources();
            InitializeGradients();
            
            debugger.Log("SegmentationVisualizer initialized", LogCategory.Visualization);
        }
        
        private void SetupUI() {
            // Create UI canvas for labels and tooltips
            GameObject canvasGO = new GameObject("SegmentationUI");
            canvasGO.transform.SetParent(transform);
            _uiCanvas = canvasGO.AddComponent<Canvas>();
            _uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _uiCanvas.sortingOrder = 100;
            
            var canvasScaler = canvasGO.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasGO.AddComponent<GraphicRaycaster>();
        }
        
        private void LoadResources() {
            // Load compute shaders for GPU processing
            if (SystemInfo.supportsComputeShaders) {
                _segmentationShader = Resources.Load<ComputeShader>("Shaders/SegmentationCompute");
                if (_segmentationShader == null) {
                    debugger.LogWarning("Segmentation compute shader not found", LogCategory.Visualization);
                }
            }
            
            // Create default materials if not assigned
            if (overlayMaterial == null) {
                // Try URP shader first, then fallback to built-in
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) {
                    shader = Shader.Find("Unlit/Transparent");
                    if (shader == null) {
                        shader = Shader.Find("Sprites/Default");
                        if (shader == null) {
                            debugger.LogError("No suitable shader found for overlay material", LogCategory.Visualization);
                            return;
                        }
                    }
                }
                
                overlayMaterial = new Material(shader);
                overlayMaterial.name = "DefaultOverlayMaterial";
                
                // Only set URP-specific properties if using URP shader
                if (shader.name.Contains("Universal Render Pipeline")) {
                    overlayMaterial.SetFloat("_Surface", 1); // Transparent
                    overlayMaterial.SetFloat("_AlphaClip", 0);
                    overlayMaterial.SetFloat("_Blend", 0);
                }
            }
            
            // Create default prefabs if not assigned
            if (overlayPrefab == null) {
                overlayPrefab = CreateDefaultOverlayPrefab();
            }
            
            if (labelPrefab == null) {
                labelPrefab = CreateDefaultLabelPrefab();
            }
            
            if (tooltipPrefab == null) {
                tooltipPrefab = CreateDefaultTooltipPrefab();
            }
        }
        
        private void InitializeGradients() {
            if (confidenceGradient == null) {
                confidenceGradient = new Gradient();
                var colorKeys = new GradientColorKey[] {
                    new GradientColorKey(Color.red, 0f),
                    new GradientColorKey(Color.yellow, 0.5f),
                    new GradientColorKey(Color.green, 1f)
                };
                var alphaKeys = new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                };
                confidenceGradient.SetKeys(colorKeys, alphaKeys);
            }
        }
        #endregion

        #region Main Visualization Pipeline
        /// <summary>
        /// Visualize segmentation results on terrain.
        /// </summary>
        public IEnumerator VisualizeSegments(
            AnalysisResults results,
            UnityEngine.Terrain terrain,
            Texture2D sourceImage,
            Action<List<GameObject>> onComplete,
            Action<string> onError,
            Action<float> onProgress
        ) {
            // Use SafeCoroutine to handle the main logic with error handling
            yield return SafeCoroutine.Wrap(
                InnerVisualizeSegments(results, terrain, sourceImage, onComplete, onProgress),
                ex => {
                    debugger.LogError($"Segmentation visualization failed: {ex.Message}", LogCategory.Visualization);
                    onError?.Invoke($"Visualization error: {ex.Message}");
                },
                LogCategory.Visualization
            );
        }
        
        /// <summary>
        /// Internal implementation of segment visualization without try-catch around yields.
        /// </summary>
        private IEnumerator InnerVisualizeSegments(
            AnalysisResults results,
            UnityEngine.Terrain terrain,
            Texture2D sourceImage,
            Action<List<GameObject>> onComplete,
            Action<float> onProgress
        ) {
            ClearPrevious();
            
            if (results == null || terrain == null) {
                throw new ArgumentException("Invalid parameters for visualization");
            }
            
            debugger.Log($"Starting visualization of {results.terrainFeatures.Count} terrain features and {results.mapObjects.Count} objects", 
                LogCategory.Visualization);
            
            // Step 1: Create segment visualizations
            yield return CreateSegmentVisualizations(results, terrain, sourceImage, onProgress);
            
            // Step 2: Group similar segments if enabled
            if (groupSimilarSegments) {
                yield return GroupSegments(onProgress);
            }
            
            // Step 3: Generate segment meshes
            yield return GenerateSegmentMeshes(terrain, onProgress);
            
            // Step 4: Setup LODs if enabled
            if (useLOD) {
                SetupLODs();
            }
            
            // Step 5: Create visual elements
            yield return CreateVisualElements(terrain, onProgress);
            
            // Step 6: Setup animations
            if (animateSegments) {
                _animationCoroutine = StartCoroutine(AnimateSegments());
            }
            
            // Step 7: Setup culling
            if (cullInvisibleSegments) {
                _cullingCoroutine = StartCoroutine(UpdateCulling());
            }
            
            // Step 8: Setup interaction
            if (enableInteraction) {
                _interactionCoroutine = StartCoroutine(HandleInteraction());
            }
            
            // Collect all created objects
            var allObjects = new List<GameObject>();
            allObjects.AddRange(_createdObjects);
            foreach (var viz in _segmentVisualizations.Values) {
                if (viz.overlayObject != null) allObjects.Add(viz.overlayObject);
                if (viz.labelObject != null) allObjects.Add(viz.labelObject);
                allObjects.AddRange(viz.borderObjects);
            }
            
            float totalTime = debugger.StopTimer("SegmentVisualization");
            debugger.Log($"Visualization completed in {totalTime:F2}s - Created {allObjects.Count} objects", 
                LogCategory.Visualization);
            
            UpdatePerformanceMetrics();
            onComplete?.Invoke(allObjects);
        }
        #endregion

        #region Segment Creation
        private IEnumerator CreateSegmentVisualizations(
            AnalysisResults results,
            UnityEngine.Terrain terrain,
            Texture2D sourceImage,
            Action<float> onProgress
        ) {
            int totalSegments = results.terrainFeatures.Count + results.mapObjects.Count;
            int processedSegments = 0;
            
            // Process terrain features
            foreach (var feature in results.terrainFeatures) {
                var viz = CreateTerrainSegmentVisualization(feature, terrain, sourceImage);
                _segmentVisualizations[viz.id] = viz;
                
                processedSegments++;
                float progress = processedSegments / (float)totalSegments * 0.2f;
                onProgress?.Invoke(progress);
                
                if (processedSegments % 10 == 0) yield return null;
            }
            
            // Process map objects
            foreach (var obj in results.mapObjects) {
                var viz = CreateObjectSegmentVisualization(obj, terrain, sourceImage);
                _segmentVisualizations[viz.id] = viz;
                
                processedSegments++;
                float progress = processedSegments / (float)totalSegments * 0.2f;
                onProgress?.Invoke(progress);
                
                if (processedSegments % 10 == 0) yield return null;
            }
            
            debugger.Log($"Created {_segmentVisualizations.Count} segment visualizations", LogCategory.Visualization);
        }
        
        private SegmentVisualization CreateTerrainSegmentVisualization(
            TerrainFeature feature,
            UnityEngine.Terrain terrain,
            Texture2D sourceImage
        ) {
            var viz = new SegmentVisualization {
                id = Guid.NewGuid().ToString(),
                terrainFeature = feature,
                bounds = CalculateWorldBounds(feature.boundingBox, terrain, sourceImage),
                metadata = new Dictionary<string, object> {
                    { "type", "terrain" },
                    { "label", feature.label },
                    { "elevation", feature.elevation },
                    { "confidence", feature.confidence }
                }
            };
            
            // Extract border points from mask
            if (feature.segmentMask != null) {
                viz.borderPoints = ExtractBorderPoints(feature.segmentMask, feature.boundingBox);
            }
            
            return viz;
        }
        
        private SegmentVisualization CreateObjectSegmentVisualization(
            MapObject mapObject,
            UnityEngine.Terrain terrain,
            Texture2D sourceImage
        ) {
            var viz = new SegmentVisualization {
                id = Guid.NewGuid().ToString(),
                mapObject = mapObject,
                bounds = CalculateWorldBounds(mapObject.boundingBox, terrain, sourceImage),
                metadata = new Dictionary<string, object> {
                    { "type", "object" },
                    { "label", mapObject.label },
                    { "objectType", mapObject.type },
                    { "confidence", mapObject.confidence },
                    { "scale", mapObject.scale },
                    { "rotation", mapObject.rotation }
                }
            };
            
            // Extract border points from mask
            if (mapObject.segmentMask != null) {
                viz.borderPoints = ExtractBorderPoints(mapObject.segmentMask, mapObject.boundingBox);
            }
            
            return viz;
        }
        
        private Bounds CalculateWorldBounds(Rect imageBounds, UnityEngine.Terrain terrain, Texture2D sourceImage) {
            Vector3 terrainSize = terrain.terrainData.size;
            Vector3 terrainPos = terrain.transform.position;
            
            // Convert image coordinates to world coordinates
            float xMin = terrainPos.x + (imageBounds.xMin / sourceImage.width) * terrainSize.x;
            float xMax = terrainPos.x + (imageBounds.xMax / sourceImage.width) * terrainSize.x;
            float zMin = terrainPos.z + (imageBounds.yMin / sourceImage.height) * terrainSize.z;
            float zMax = terrainPos.z + (imageBounds.yMax / sourceImage.height) * terrainSize.z;
            
            // Sample terrain height at corners
            float[] heights = new float[4];
            heights[0] = terrain.SampleHeight(new Vector3(xMin, 0, zMin));
            heights[1] = terrain.SampleHeight(new Vector3(xMax, 0, zMin));
            heights[2] = terrain.SampleHeight(new Vector3(xMin, 0, zMax));
            heights[3] = terrain.SampleHeight(new Vector3(xMax, 0, zMax));
            
            float minHeight = heights.Min();
            float maxHeight = heights.Max();
            
            Vector3 center = new Vector3((xMin + xMax) / 2f, (minHeight + maxHeight) / 2f + overlayYOffset, (zMin + zMax) / 2f);
            Vector3 size = new Vector3(xMax - xMin, maxHeight - minHeight + overlayYOffset * 2f, zMax - zMin);
            
            return new Bounds(center, size);
        }
        
        private List<Vector3> ExtractBorderPoints(Texture2D mask, Rect bounds) {
            var borderPoints = new List<Vector3>();
            
            int width = mask.width;
            int height = mask.height;
            Color[] pixels = mask.GetPixels();
            
            // Find border pixels using edge detection
            for (int y = 1; y < height - 1; y++) {
                for (int x = 1; x < width - 1; x++) {
                    int idx = y * width + x;
                    
                    if (pixels[idx].a > 0.5f) {
                        // Check if this is a border pixel
                        bool isBorder = false;
                        
                        // Check 4-connected neighbors
                        if (pixels[idx - 1].a < 0.5f || pixels[idx + 1].a < 0.5f ||
                            pixels[idx - width].a < 0.5f || pixels[idx + width].a < 0.5f) {
                            isBorder = true;
                        }
                        
                        if (isBorder) {
                            // Convert to world position (relative to bounds)
                            float u = x / (float)(width - 1);
                            float v = y / (float)(height - 1);
                            
                            borderPoints.Add(new Vector3(u, 0, v));
                        }
                    }
                }
            }
            
            // Simplify border points using Douglas-Peucker algorithm
            if (borderPoints.Count > 100) {
                borderPoints = SimplifyPath(borderPoints, 0.01f);
            }
            
            return borderPoints;
        }
        
        private List<Vector3> SimplifyPath(List<Vector3> points, float tolerance) {
            if (points.Count < 3) return points;
            
            // Douglas-Peucker algorithm
            int firstPoint = 0;
            int lastPoint = points.Count - 1;
            List<int> pointIndexesToKeep = new List<int> { firstPoint, lastPoint };
            
            while (true) {
                float maxDistance = 0;
                int indexToAdd = -1;
                
                for (int i = 0; i < pointIndexesToKeep.Count - 1; i++) {
                    for (int j = pointIndexesToKeep[i] + 1; j < pointIndexesToKeep[i + 1]; j++) {
                        float distance = PerpendicularDistance(
                            points[j],
                            points[pointIndexesToKeep[i]],
                            points[pointIndexesToKeep[i + 1]]
                        );
                        
                        if (distance > maxDistance) {
                            maxDistance = distance;
                            indexToAdd = j;
                        }
                    }
                }
                
                if (maxDistance > tolerance && indexToAdd != -1) {
                    pointIndexesToKeep.Add(indexToAdd);
                    pointIndexesToKeep.Sort();
                } else {
                    break;
                }
            }
            
            // Build simplified path
            var simplified = new List<Vector3>();
            foreach (int index in pointIndexesToKeep) {
                simplified.Add(points[index]);
            }
            
            return simplified;
        }
        
        private float PerpendicularDistance(Vector3 point, Vector3 lineStart, Vector3 lineEnd) {
            Vector3 line = lineEnd - lineStart;
            float lineLength = line.magnitude;
            
            if (lineLength == 0) {
                return Vector3.Distance(point, lineStart);
            }
            
            float t = Mathf.Clamp01(Vector3.Dot(point - lineStart, line) / (lineLength * lineLength));
            Vector3 projection = lineStart + t * line;
            
            return Vector3.Distance(point, projection);
        }
        #endregion

        #region Mesh Generation
        private IEnumerator GenerateSegmentMeshes(UnityEngine.Terrain terrain, Action<float> onProgress) {
            int totalSegments = _segmentVisualizations.Count;
            int processedSegments = 0;
            
            // Prepare native arrays for job system
            if (useGPUInstancing) {
                _segmentPositions = new NativeArray<float3>(totalSegments, Allocator.Persistent);
                _segmentVisibility = new NativeArray<float>(totalSegments, Allocator.Persistent);
            }
            
            foreach (var kvp in _segmentVisualizations) {
                var viz = kvp.Value;
                
                switch (visualizationMode) {
                    case VisualizationMode.Overlay:
                        viz.segmentMesh = GenerateOverlayMesh(viz, terrain);
                        break;
                    case VisualizationMode.Outline:
                        viz.segmentMesh = GenerateOutlineMesh(viz, terrain);
                        break;
                    case VisualizationMode.Fill:
                        viz.segmentMesh = GenerateFillMesh(viz, terrain);
                        break;
                    case VisualizationMode.Wireframe:
                        viz.segmentMesh = GenerateWireframeMesh(viz, terrain);
                        break;
                    case VisualizationMode.Heatmap:
                        viz.segmentMesh = GenerateHeatmapMesh(viz, terrain);
                        break;
                    case VisualizationMode.Contour:
                        viz.segmentMesh = GenerateContourMesh(viz, terrain);
                        break;
                }
                
                processedSegments++;
                float progress = 0.2f + (processedSegments / (float)totalSegments * 0.2f);
                onProgress?.Invoke(progress);
                
                if (processedSegments % 5 == 0) yield return null;
            }
            
            debugger.Log($"Generated {processedSegments} segment meshes", LogCategory.Visualization);
        }
        
        private Mesh GenerateOverlayMesh(SegmentVisualization viz, UnityEngine.Terrain terrain) {
            var mesh = new Mesh();
            mesh.name = $"Segment_{viz.id}_Overlay";
            
            // Get mask texture
            Texture2D mask = null;
            Rect bounds = Rect.zero;
            
            if (viz.terrainFeature != null) {
                mask = viz.terrainFeature.segmentMask;
                bounds = viz.terrainFeature.boundingBox;
            } else if (viz.mapObject != null) {
                mask = viz.mapObject.segmentMask;
                bounds = viz.mapObject.boundingBox;
            }
            
            if (mask == null) {
                // Create simple quad mesh
                return CreateQuadMesh(viz.bounds);
            }
            
            // Generate mesh from mask
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            
            int resolution = Mathf.Min(mask.width, mask.height, 64);
            float stepX = mask.width / (float)resolution;
            float stepY = mask.height / (float)resolution;
            
            // Generate vertices
            for (int y = 0; y <= resolution; y++) {
                for (int x = 0; x <= resolution; x++) {
                    float u = x / (float)resolution;
                    float v = y / (float)resolution;
                    
                    // Sample mask
                    int maskX = Mathf.Clamp(Mathf.RoundToInt(x * stepX), 0, mask.width - 1);
                    int maskY = Mathf.Clamp(Mathf.RoundToInt(y * stepY), 0, mask.height - 1);
                    Color maskColor = mask.GetPixel(maskX, maskY);
                    
                    // Calculate world position
                    Vector3 worldPos = new Vector3(
                        viz.bounds.min.x + u * viz.bounds.size.x,
                        0,
                        viz.bounds.min.z + v * viz.bounds.size.z
                    );
                    
                    // Sample terrain height
                    worldPos.y = terrain.SampleHeight(worldPos) + overlayYOffset;
                    
                    vertices.Add(worldPos);
                    uvs.Add(new Vector2(u, v));
                    colors.Add(new Color(1, 1, 1, maskColor.a));
                }
            }
            
            // Generate triangles
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    int idx = y * (resolution + 1) + x;
                    
                    // Check if center of quad is visible in mask
                    float centerU = (x + 0.5f) / resolution;
                    float centerV = (y + 0.5f) / resolution;
                    int centerMaskX = Mathf.RoundToInt(centerU * mask.width);
                    int centerMaskY = Mathf.RoundToInt(centerV * mask.height);
                    
                    if (centerMaskX >= 0 && centerMaskX < mask.width &&
                        centerMaskY >= 0 && centerMaskY < mask.height) {
                        
                        Color centerColor = mask.GetPixel(centerMaskX, centerMaskY);
                        if (centerColor.a > 0.1f) {
                            // Add two triangles for the quad
                            triangles.Add(idx);
                            triangles.Add(idx + resolution + 1);
                            triangles.Add(idx + 1);
                            
                            triangles.Add(idx + 1);
                            triangles.Add(idx + resolution + 1);
                            triangles.Add(idx + resolution + 2);
                        }
                    }
                }
            }
            
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            
            // Optimize mesh
            mesh.Optimize();
            
            return mesh;
        }
        
        private Mesh GenerateOutlineMesh(SegmentVisualization viz, UnityEngine.Terrain terrain) {
            if (viz.borderPoints == null || viz.borderPoints.Count < 3) {
                return null;
            }
            
            var mesh = new Mesh();
            mesh.name = $"Segment_{viz.id}_Outline";
            
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            
            // Convert border points to world space
            foreach (var point in viz.borderPoints) {
                Vector3 worldPos = new Vector3(
                    viz.bounds.min.x + point.x * viz.bounds.size.x,
                    0,
                    viz.bounds.min.z + point.z * viz.bounds.size.z
                );
                worldPos.y = terrain.SampleHeight(worldPos) + overlayYOffset;
                vertices.Add(worldPos);
            }
            
            // Generate outline strip
            float halfWidth = borderWidth * 0.5f;
            
            for (int i = 0; i < vertices.Count; i++) {
                Vector3 current = vertices[i];
                Vector3 prev = vertices[(i - 1 + vertices.Count) % vertices.Count];
                Vector3 next = vertices[(i + 1) % vertices.Count];
                
                // Calculate normal direction
                Vector3 dir1 = (current - prev).normalized;
                Vector3 dir2 = (next - current).normalized;
                Vector3 avgDir = (dir1 + dir2).normalized;
                Vector3 normal = new Vector3(-avgDir.z, 0, avgDir.x);
                
                // Add vertices for inner and outer edge
                vertices.Add(current - normal * halfWidth);
                vertices.Add(current + normal * halfWidth);
                
                uvs.Add(new Vector2(i / (float)vertices.Count, 0));
                uvs.Add(new Vector2(i / (float)vertices.Count, 1));
            }
            
            // Generate triangles for the strip
            for (int i = 0; i < vertices.Count; i++) {
                int current = i * 2;
                int next = ((i + 1) % vertices.Count) * 2;
                
                triangles.Add(current);
                triangles.Add(next);
                triangles.Add(current + 1);
                
                triangles.Add(current + 1);
                triangles.Add(next);
                triangles.Add(next + 1);
            }
            
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        private Mesh CreateQuadMesh(Bounds bounds) {
            var mesh = new Mesh();
            
            var vertices = new Vector3[] {
                new Vector3(bounds.min.x, bounds.center.y, bounds.min.z),
                new Vector3(bounds.max.x, bounds.center.y, bounds.min.z),
                new Vector3(bounds.max.x, bounds.center.y, bounds.max.z),
                new Vector3(bounds.min.x, bounds.center.y, bounds.max.z)
            };
            
            var triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            var uvs = new Vector2[] {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
        #endregion

        #region Visual Elements Creation
        private IEnumerator CreateVisualElements(UnityEngine.Terrain terrain, Action<float> onProgress) {
            int totalSegments = _segmentVisualizations.Count;
            int processedSegments = 0;
            
            foreach (var kvp in _segmentVisualizations) {
                var viz = kvp.Value;
                
                // Create overlay object
                if (viz.segmentMesh != null) {
                    viz.overlayObject = CreateOverlayObject(viz);
                }
                
                // Create label
                if (showSegmentInfo) {
                    viz.labelObject = CreateLabelObject(viz);
                }
                
                // Create borders if enabled
                if (showSegmentBorders && viz.borderPoints != null) {
                    viz.borderObjects = CreateBorderObjects(viz, terrain);
                }
                
                processedSegments++;
                float progress = 0.4f + (processedSegments / (float)totalSegments * 0.2f);
                onProgress?.Invoke(progress);
                
                if (processedSegments % 5 == 0) yield return null;
            }
            
            debugger.Log($"Created visual elements for {processedSegments} segments", LogCategory.Visualization);
        }
        
        private GameObject CreateOverlayObject(SegmentVisualization viz) {
            GameObject overlayGO = overlayPrefab ? Instantiate(overlayPrefab) : new GameObject($"Overlay_{viz.id}");
            overlayGO.transform.SetParent(transform);
            
            // Add mesh components
            MeshFilter meshFilter = overlayGO.GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = overlayGO.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = viz.segmentMesh;
            
            MeshRenderer renderer = overlayGO.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = overlayGO.AddComponent<MeshRenderer>();
            
            // Create instance material
            viz.instanceMaterial = new Material(overlayMaterial);
            viz.instanceMaterial.name = $"SegmentMaterial_{viz.id}";
            
            // Set color
            Color segmentColor = Color.white;
            if (viz.terrainFeature != null) {
                segmentColor = viz.terrainFeature.segmentColor;
            } else if (viz.mapObject != null) {
                segmentColor = viz.mapObject.segmentColor;
            }
            
            viz.instanceMaterial.SetColor("_BaseColor", new Color(segmentColor.r, segmentColor.g, segmentColor.b, 0));
            renderer.sharedMaterial = viz.instanceMaterial;
            
            // Configure renderer
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = receiveShadows;
            
            // Add to created objects
            _createdObjects.Add(overlayGO);
            
            return overlayGO;
        }
        
        private GameObject CreateLabelObject(SegmentVisualization viz) {
            GameObject labelGO = labelPrefab ? Instantiate(labelPrefab) : new GameObject($"Label_{viz.id}");
            
            // Position label at segment center
            Vector3 labelPos = viz.bounds.center;
            labelPos.y += labelYOffset;
            labelGO.transform.position = labelPos;
            
            // Create or configure text component
            TextMeshPro textMesh = labelGO.GetComponentInChildren<TextMeshPro>();
            if (textMesh == null) {
                textMesh = labelGO.AddComponent<TextMeshPro>();
            }
            
            // Set text content
            string labelText = GetSegmentLabel(viz);
            textMesh.text = labelText;
            // Fix: Check if labelFont is actually a TMP_FontAsset, otherwise use default
            if (labelFont != null) {
                textMesh.font = labelFont;
            }
            textMesh.fontSize = labelFontSize;
            textMesh.color = labelTextColor;
            textMesh.alignment = TextAlignmentOptions.Center;
            
            // Add background if needed
            if (labelBackgroundColor.a > 0) {
                GameObject backgroundGO = new GameObject("Background");
                backgroundGO.transform.SetParent(labelGO.transform, false);
                
                Image background = backgroundGO.AddComponent<Image>();
                background.color = labelBackgroundColor;
                
                // Size background to text
                RectTransform bgRect = backgroundGO.GetComponent<RectTransform>();
                bgRect.sizeDelta = new Vector2(textMesh.preferredWidth + 20, textMesh.preferredHeight + 10);
                bgRect.anchoredPosition = Vector2.zero;
                
                // Move text in front of background
                textMesh.transform.SetAsLastSibling();
            }
            
            // Add billboard component if needed
            if (billboardLabels) {
                labelGO.AddComponent<Billboard>();
            }
            
            // Add distance scaling if enabled
            if (scaleLabelsWithDistance) {
                // Note: DistanceScaler component needs to be implemented or use existing Unity component
                // For now, we'll comment this out to avoid the missing type error
                /*
                var scaler = labelGO.AddComponent<DistanceScaler>();
                scaler.minScale = labelMinScale;
                scaler.maxScale = labelMaxScale;
                scaler.referenceDistance = fadeStartDistance;
                */
            }
            
            labelGO.transform.SetParent(transform);
            _createdObjects.Add(labelGO);
            
            return labelGO;
        }
        
        private List<GameObject> CreateBorderObjects(SegmentVisualization viz, UnityEngine.Terrain terrain) {
            var borderObjects = new List<GameObject>();
            
            if (viz.borderPoints == null || viz.borderPoints.Count < 2) {
                return borderObjects;
            }
            
            // Create line renderer for border
            GameObject borderGO = new GameObject($"Border_{viz.id}");
            borderGO.transform.SetParent(transform);
            
            LineRenderer lineRenderer = borderGO.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = borderColor;
            lineRenderer.endColor = borderColor;
            lineRenderer.startWidth = borderWidth * 0.1f;
            lineRenderer.endWidth = borderWidth * 0.1f;
            lineRenderer.useWorldSpace = true;
            
            // Convert border points to world space
            var worldPoints = new Vector3[viz.borderPoints.Count + 1];
            for (int i = 0; i < viz.borderPoints.Count; i++) {
                Vector3 point = viz.borderPoints[i];
                Vector3 worldPos = new Vector3(
                    viz.bounds.min.x + point.x * viz.bounds.size.x,
                    0,
                    viz.bounds.min.z + point.z * viz.bounds.size.z
                );
                worldPos.y = terrain.SampleHeight(worldPos) + overlayYOffset + 0.1f;
                worldPoints[i] = worldPos;
            }
            
            // Close the loop
            worldPoints[worldPoints.Length - 1] = worldPoints[0];
            
            lineRenderer.positionCount = worldPoints.Length;
            lineRenderer.SetPositions(worldPoints);
            
            borderObjects.Add(borderGO);
            _createdObjects.Add(borderGO);
            
            return borderObjects;
        }
        
        private string GetSegmentLabel(SegmentVisualization viz) {
            string label = "";
            
            switch (infoDisplayMode) {
                case InfoDisplayMode.Simplified:
                    if (viz.terrainFeature != null) {
                        label = viz.terrainFeature.label;
                    } else if (viz.mapObject != null) {
                        label = viz.mapObject.label;
                    }
                    break;
                    
                case InfoDisplayMode.Detailed:
                    if (viz.terrainFeature != null) {
                        label = $"{viz.terrainFeature.label}\n";
                        if (showElevationData) {
                            label += $"Elev: {viz.terrainFeature.elevation:F1}m\n";
                        }
                        if (showConfidenceIndicators) {
                            label += $"Conf: {viz.terrainFeature.confidence:P0}";
                        }
                    } else if (viz.mapObject != null) {
                        label = $"{viz.mapObject.label}\n{viz.mapObject.type}";
                        if (showConfidenceIndicators) {
                            label += $"\nConf: {viz.mapObject.confidence:P0}";
                        }
                    }
                    break;
                    
                case InfoDisplayMode.Technical:
                    if (viz.metadata != null) {
                        foreach (var kvp in viz.metadata.Take(5)) {
                            label += $"{kvp.Key}: {kvp.Value}\n";
                        }
                    }
                    break;
            }
            
            return label.TrimEnd('\n');
        }
        #endregion

        #region Grouping
        private IEnumerator GroupSegments(Action<float> onProgress) {
            debugger.Log("Grouping similar segments", LogCategory.Visualization);
            
            var segments = _segmentVisualizations.Values.ToList();
            var processed = new HashSet<SegmentVisualization>();
            
            foreach (var segment in segments) {
                if (processed.Contains(segment)) continue;
                
                var group = new SegmentGroup {
                    groupId = Guid.NewGuid().ToString(),
                    segments = new List<SegmentVisualization> { segment },
                    groupColor = segment.terrainFeature?.segmentColor ?? segment.mapObject?.segmentColor ?? Color.white
                };
                
                processed.Add(segment);
                
                // Find similar segments
                foreach (var other in segments) {
                    if (!processed.Contains(other) && CalculateSimilarity(segment, other) >= groupingSimilarityThreshold) {
                        group.segments.Add(other);
                        processed.Add(other);
                    }
                }
                
                // Calculate group center
                Vector3 centerSum = Vector3.zero;
                foreach (var seg in group.segments) {
                    centerSum += seg.bounds.center;
                }
                group.centerPoint = centerSum / group.segments.Count;
                
                _segmentGroups[group.groupId] = group;
                
                yield return null;
            }
            
            debugger.Log($"Created {_segmentGroups.Count} segment groups", LogCategory.Visualization);
            onProgress?.Invoke(0.3f);
            
            // Create group connections if enabled
            if (showGroupConnections) {
                yield return CreateGroupConnections();
            }
        }
        
        private float CalculateSimilarity(SegmentVisualization seg1, SegmentVisualization seg2) {
            float similarity = 0f;
            
            // Type similarity
            bool bothTerrain = seg1.terrainFeature != null && seg2.terrainFeature != null;
            bool bothObject = seg1.mapObject != null && seg2.mapObject != null;
            
            if (bothTerrain) {
                if (seg1.terrainFeature.label == seg2.terrainFeature.label) {
                    similarity += 0.5f;
                }
                
                // Elevation similarity
                float elevDiff = Mathf.Abs(seg1.terrainFeature.elevation - seg2.terrainFeature.elevation);
                similarity += (1f - Mathf.Clamp01(elevDiff / 100f)) * 0.3f;
            } else if (bothObject) {
                if (seg1.mapObject.type == seg2.mapObject.type) {
                    similarity += 0.5f;
                }
                
                // Scale similarity
                float scaleDiff = Vector3.Distance(seg1.mapObject.scale, seg2.mapObject.scale);
                similarity += (1f - Mathf.Clamp01(scaleDiff)) * 0.3f;
            } else {
                return 0f; // Different types
            }
            
            // Spatial proximity
            float distance = Vector3.Distance(seg1.bounds.center, seg2.bounds.center);
            float maxDist = Mathf.Max(fadeEndDistance, 100f);
            similarity += (1f - Mathf.Clamp01(distance / maxDist)) * 0.2f;
            
            return similarity;
        }
        
        private IEnumerator CreateGroupConnections() {
            foreach (var group in _segmentGroups.Values) {
                if (group.segments.Count < 2) continue;
                
                group.connectionLines = new List<GameObject>();
                
                // Create connections between segments in group
                for (int i = 0; i < group.segments.Count - 1; i++) {
                    for (int j = i + 1; j < group.segments.Count; j++) {
                        var seg1 = group.segments[i];
                        var seg2 = group.segments[j];
                        
                        GameObject lineGO = connectionLinePrefab ? 
                            Instantiate(connectionLinePrefab.gameObject) : 
                            new GameObject($"Connection_{group.groupId}_{i}_{j}");
                        
                        lineGO.transform.SetParent(transform);
                        
                        LineRenderer line = lineGO.GetComponent<LineRenderer>();
                        if (line == null) line = lineGO.AddComponent<LineRenderer>();
                        
                        line.startColor = connectionColor;
                        line.endColor = connectionColor;
                        line.startWidth = 0.1f;
                        line.endWidth = 0.1f;
                        
                        // Create curved connection
                        Vector3 start = seg1.bounds.center;
                        Vector3 end = seg2.bounds.center;
                        Vector3 mid = (start + end) / 2f + Vector3.up * 5f;
                        
                        var curvePoints = new Vector3[10];
                        for (int k = 0; k < 10; k++) {
                            float t = k / 9f;
                            curvePoints[k] = QuadraticBezier(start, mid, end, t);
                        }
                        
                        line.positionCount = curvePoints.Length;
                        line.SetPositions(curvePoints);
                        
                        group.connectionLines.Add(lineGO);
                        _createdObjects.Add(lineGO);
                    }
                }
                
                yield return null;
            }
        }
        
        private Vector3 QuadraticBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t) {
            float u = 1f - t;
            return u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }
        #endregion

        #region Animation
        private IEnumerator AnimateSegments() {
            while (true) {
                float deltaTime = Time.deltaTime;
                
                foreach (var viz in _segmentVisualizations.Values) {
                    if (!viz.isVisible) continue;
                    
                    // Update animation phase
                    viz.animationPhase += deltaTime * animationSpeed;
                    
                    switch (animationStyle) {
                        case AnimationStyle.Pulse:
                            AnimatePulse(viz);
                            break;
                        case AnimationStyle.Wave:
                            AnimateWave(viz);
                            break;
                        case AnimationStyle.Rotate:
                            AnimateRotate(viz);
                            break;
                        case AnimationStyle.Float:
                            AnimateFloat(viz);
                            break;
                        case AnimationStyle.Shimmer:
                            AnimateShimmer(viz);
                            break;
                    }
                    
                    // Update opacity based on distance
                    if (useDistanceBasedOpacity && _mainCamera != null) {
                        float distance = Vector3.Distance(_mainCamera.transform.position, viz.bounds.center);
                        float targetOpacity = 1f;
                        
                        if (distance > fadeEndDistance) {
                            targetOpacity = 0f;
                        } else if (distance > fadeStartDistance) {
                            targetOpacity = 1f - (distance - fadeStartDistance) / (fadeEndDistance - fadeStartDistance);
                        }
                        
                        targetOpacity *= overlayOpacity;
                        
                        // Smooth opacity transition
                        viz.opacity = Mathf.Lerp(viz.opacity, targetOpacity, deltaTime * 2f);
                        
                        if (viz.instanceMaterial != null) {
                            Color color = viz.instanceMaterial.GetColor("_BaseColor");
                            color.a = viz.opacity;
                            viz.instanceMaterial.SetColor("_BaseColor", color);
                        }
                    }
                }
                
                yield return null;
            }
        }
        
        private void AnimatePulse(SegmentVisualization viz) {
            float pulse = Mathf.Sin(viz.animationPhase * Mathf.PI * 2f) * 0.5f + 0.5f;
            
            if (viz.instanceMaterial != null) {
                // Pulse opacity
                Color color = viz.instanceMaterial.GetColor("_BaseColor");
                float baseAlpha = viz.opacity;
                color.a = baseAlpha * (0.5f + pulse * 0.5f);
                viz.instanceMaterial.SetColor("_BaseColor", color);
                
                // Pulse emission if using lit shader
                if (viz.instanceMaterial.HasProperty("_EmissionColor")) {
                    Color emissionColor = color * pulse * 0.5f;
                    viz.instanceMaterial.SetColor("_EmissionColor", emissionColor);
                }
            }
        }
        
        private void AnimateWave(SegmentVisualization viz) {
            if (viz.overlayObject == null) return;
            
            float wave = Mathf.Sin(viz.animationPhase * Mathf.PI * 2f + viz.bounds.center.x * 0.1f);
            Vector3 pos = viz.overlayObject.transform.position;
            pos.y = viz.bounds.center.y + overlayYOffset + wave * 0.5f;
            viz.overlayObject.transform.position = pos;
        }
        
        private void AnimateRotate(SegmentVisualization viz) {
            if (viz.overlayObject == null) return;
            
            viz.overlayObject.transform.rotation = Quaternion.Euler(0, viz.animationPhase * 360f, 0);
        }
        
        private void AnimateFloat(SegmentVisualization viz) {
            if (viz.overlayObject == null) return;
            
            float floatY = Mathf.Sin(viz.animationPhase * Mathf.PI) * 0.5f;
            float floatX = Mathf.Sin(viz.animationPhase * Mathf.PI * 0.7f) * 0.2f;
            
            Vector3 pos = viz.bounds.center;
            pos.y += overlayYOffset + floatY;
            pos.x += floatX;
            viz.overlayObject.transform.position = pos;
        }
        
        private void AnimateShimmer(SegmentVisualization viz) {
            if (viz.instanceMaterial == null) return;
            
            // Animate texture offset for shimmer effect
            if (viz.instanceMaterial.HasProperty("_MainTex_ST")) {
                Vector2 offset = new Vector2(
                    Mathf.Sin(viz.animationPhase) * 0.1f,
                    Mathf.Cos(viz.animationPhase * 0.7f) * 0.1f
                );
                viz.instanceMaterial.SetTextureOffset("_MainTex", offset);
            }
        }
        #endregion

        #region Culling
        private IEnumerator UpdateCulling() {
            while (cullInvisibleSegments) {
                if (_mainCamera == null) {
                    yield return new WaitForSeconds(cullingUpdateInterval);
                    continue;
                }
                
                var cameraFrustum = GeometryUtility.CalculateFrustumPlanes(_mainCamera);
                int visibleCount = 0;
                int culledCount = 0;
                
                foreach (var viz in _segmentVisualizations.Values) {
                    bool isVisible = GeometryUtility.TestPlanesAABB(cameraFrustum, viz.bounds);
                    
                    if (isVisible != viz.isVisible) {
                        viz.isVisible = isVisible;
                        
                        // Enable/disable objects
                        if (viz.overlayObject != null) viz.overlayObject.SetActive(isVisible);
                        if (viz.labelObject != null) viz.labelObject.SetActive(isVisible);
                        foreach (var border in viz.borderObjects) {
                            if (border != null) border.SetActive(isVisible);
                        }
                    }
                    
                    if (isVisible) visibleCount++;
                    else culledCount++;
                }
                
                _performanceMetrics.visibleSegments = visibleCount;
                _performanceMetrics.culledSegments = culledCount;
                
                yield return new WaitForSeconds(cullingUpdateInterval);
            }
        }
        #endregion

        #region Interaction
        private IEnumerator HandleInteraction() {
            while (enableInteraction) {
                if (_mainCamera == null) {
                    yield return null;
                    continue;
                }
                
                // Update hover state
                Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                
                SegmentVisualization hoveredSegment = null;
                
                if (Physics.Raycast(ray, out hit, interactionDistance, interactionLayerMask)) {
                    // Find segment at hit point
                    foreach (var viz in _segmentVisualizations.Values) {
                        if (viz.bounds.Contains(hit.point)) {
                            hoveredSegment = viz;
                            break;
                        }
                    }
                }
                
                // Update hover state
                if (hoveredSegment != _interactionState.hoveredSegment) {
                    // Clear previous hover
                    if (_interactionState.hoveredSegment != null && highlightOnHover) {
                        SetSegmentHighlight(_interactionState.hoveredSegment, false);
                    }
                    
                    _interactionState.hoveredSegment = hoveredSegment;
                    _interactionState.hoverTime = 0f;
                    
                    // Apply new hover
                    if (hoveredSegment != null && highlightOnHover) {
                        SetSegmentHighlight(hoveredSegment, true);
                    }
                }
                
                // Update hover time
                if (_interactionState.hoveredSegment != null) {
                    _interactionState.hoverTime += Time.deltaTime;
                    
                    // Show tooltip after delay
                    if (showTooltips && _interactionState.hoverTime > 0.5f) {
                        ShowTooltip(_interactionState.hoveredSegment);
                    }
                }
                
                // Handle click
                if (Input.GetMouseButtonDown(0) && _interactionState.hoveredSegment != null) {
                    SelectSegment(_interactionState.hoveredSegment);
                }
                
                _interactionState.lastMousePosition = Input.mousePosition;
                yield return null;
            }
        }
        
        private void SetSegmentHighlight(SegmentVisualization viz, bool highlighted) {
            viz.isHighlighted = highlighted;
            
            if (viz.instanceMaterial != null) {
                if (highlighted) {
                    viz.instanceMaterial.SetColor("_EmissionColor", highlightColor * highlightIntensity);
                    viz.instanceMaterial.EnableKeyword("_EMISSION");
                } else {
                    viz.instanceMaterial.DisableKeyword("_EMISSION");
                }
            }
            
            // Scale label if highlighted
            if (viz.labelObject != null) {
                float targetScale = highlighted ? 1.2f : 1f;
                viz.labelObject.transform.localScale = Vector3.one * targetScale;
            }
        }
        
        private void SelectSegment(SegmentVisualization viz) {
            // Deselect previous
            if (_interactionState.selectedSegment != null) {
                // Handle deselection
            }
            
            _interactionState.selectedSegment = viz;
            
            // Trigger selection event
            debugger.Log($"Selected segment: {viz.id}", LogCategory.Visualization);
            
            // Show detailed info
            if (infoDisplayMode != InfoDisplayMode.None) {
                UpdateSegmentInfo(viz);
            }
        }
        
        private void ShowTooltip(SegmentVisualization viz) {
            if (_interactionState.tooltipInstance != null) {
                Destroy(_interactionState.tooltipInstance);
            }
            
            _interactionState.tooltipInstance = Instantiate(tooltipPrefab, _uiCanvas.transform);
            
            // Position tooltip near cursor
            RectTransform rect = _interactionState.tooltipInstance.GetComponent<RectTransform>();
            if (rect != null) {
                rect.position = Input.mousePosition + new Vector3(20, -20, 0);
            }
            
            // Set tooltip content
            TextMeshProUGUI tooltipText = _interactionState.tooltipInstance.GetComponentInChildren<TextMeshProUGUI>();
            if (tooltipText != null) {
                tooltipText.text = GetTooltipText(viz);
            }
        }
        
        private string GetTooltipText(SegmentVisualization viz) {
            var sb = new System.Text.StringBuilder();
            
            if (viz.terrainFeature != null) {
                sb.AppendLine($"<b>{viz.terrainFeature.label}</b>");
                sb.AppendLine($"Type: Terrain Feature");
                sb.AppendLine($"Elevation: {viz.terrainFeature.elevation:F1}m");
                sb.AppendLine($"Confidence: {viz.terrainFeature.confidence:P0}");
                
                if (showAreaMetrics) {
                    float area = viz.bounds.size.x * viz.bounds.size.z;
                    sb.AppendLine($"Area: {area:F0} m²");
                }
            } else if (viz.mapObject != null) {
                sb.AppendLine($"<b>{viz.mapObject.label}</b>");
                sb.AppendLine($"Type: {viz.mapObject.type}");
                sb.AppendLine($"Confidence: {viz.mapObject.confidence:P0}");
                
                if (!string.IsNullOrEmpty(viz.mapObject.enhancedDescription)) {
                    sb.AppendLine($"Description: {viz.mapObject.enhancedDescription}");
                }
            }
            
            return sb.ToString().TrimEnd();
        }
        
        private void UpdateSegmentInfo(SegmentVisualization viz) {
            // Update UI panel with detailed segment information
            // This would update a dedicated UI panel with comprehensive info
        }
        #endregion

        #region LOD Setup
        private void SetupLODs() {
            foreach (var viz in _segmentVisualizations.Values) {
                if (viz.overlayObject == null) continue;
                
                LODGroup lodGroup = viz.overlayObject.GetComponent<LODGroup>();
                if (lodGroup == null) {
                    lodGroup = viz.overlayObject.AddComponent<LODGroup>();
                }
                
                viz.lodGroup = lodGroup;
                
                // Create LOD levels
                var lods = new LOD[lodDistances.Length + 1];
                
                // LOD 0 - Full detail
                var renderers = viz.overlayObject.GetComponentsInChildren<Renderer>();
                lods[0] = new LOD(                0.6f, renderers);
                
                // Create simplified meshes for other LODs
                if (viz.segmentMesh != null) {
                    for (int i = 0; i < lodDistances.Length; i++) {
                        float screenRelativeHeight = 1f / (lodDistances[i] / 10f);
                        
                        // Create LOD mesh
                        Mesh lodMesh = CreateLODMesh(viz.segmentMesh, i + 1);
                        if (lodMesh != null) {
                            // Create LOD object
                            GameObject lodObject = new GameObject($"LOD{i + 1}");
                            lodObject.transform.SetParent(viz.overlayObject.transform, false);
                            
                            MeshFilter lodMeshFilter = lodObject.AddComponent<MeshFilter>();
                            lodMeshFilter.sharedMesh = lodMesh;
                            
                            MeshRenderer lodRenderer = lodObject.AddComponent<MeshRenderer>();
                            lodRenderer.sharedMaterial = viz.instanceMaterial;
                            lodRenderer.shadowCastingMode = renderers[0].shadowCastingMode;
                            lodRenderer.receiveShadows = renderers[0].receiveShadows;
                            
                            lods[i + 1] = new LOD(screenRelativeHeight, new Renderer[] { lodRenderer });
                        }
                    }
                }
                
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }
        }
        
        private Mesh CreateLODMesh(Mesh originalMesh, int lodLevel) {
            // Simple mesh decimation based on LOD level
            float reductionFactor = 1f / (lodLevel + 1);
            
            var lodMesh = Instantiate(originalMesh);
            lodMesh.name = originalMesh.name + $"_LOD{lodLevel}";
            
            // For now, just use Unity's built-in simplification
            // In production, use a proper mesh decimation algorithm
            if (lodLevel == 1) {
                // 50% reduction
                lodMesh = SimplifyMesh(lodMesh, 0.5f);
            } else if (lodLevel == 2) {
                // 75% reduction
                lodMesh = SimplifyMesh(lodMesh, 0.25f);
            } else {
                // 90% reduction
                lodMesh = SimplifyMesh(lodMesh, 0.1f);
            }
            
            return lodMesh;
        }
        
        private Mesh SimplifyMesh(Mesh mesh, float quality) {
            // Basic mesh simplification
            // In production, use Unity.Mesh.Simplifier or similar
            var simplifiedMesh = new Mesh();
            simplifiedMesh.name = mesh.name + "_Simplified";
            
            // For demonstration, just copy with reduced vertex count
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            
            // Skip vertices based on quality
            int step = Mathf.Max(1, Mathf.RoundToInt(1f / quality));
            var newVertices = new List<Vector3>();
            var vertexMap = new Dictionary<int, int>();
            
            for (int i = 0; i < vertices.Length; i += step) {
                vertexMap[i] = newVertices.Count;
                newVertices.Add(vertices[i]);
            }
            
            // Remap triangles
            var newTriangles = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3) {
                int v0 = triangles[i];
                int v1 = triangles[i + 1];
                int v2 = triangles[i + 2];
                
                // Find nearest mapped vertices
                int newV0 = FindNearestMappedVertex(v0, vertexMap, step);
                int newV1 = FindNearestMappedVertex(v1, vertexMap, step);
                int newV2 = FindNearestMappedVertex(v2, vertexMap, step);
                
                // Skip degenerate triangles
                if (newV0 != newV1 && newV1 != newV2 && newV2 != newV0) {
                    newTriangles.Add(vertexMap[newV0]);
                    newTriangles.Add(vertexMap[newV1]);
                    newTriangles.Add(vertexMap[newV2]);
                }
            }
            
            simplifiedMesh.vertices = newVertices.ToArray();
            simplifiedMesh.triangles = newTriangles.ToArray();
            simplifiedMesh.RecalculateNormals();
            simplifiedMesh.RecalculateBounds();
            
            return simplifiedMesh;
        }
        
        private int FindNearestMappedVertex(int vertex, Dictionary<int, int> vertexMap, int step) {
            // Find the nearest vertex that was included in the simplified mesh
            int nearestMapped = (vertex / step) * step;
            if (vertexMap.ContainsKey(nearestMapped)) {
                return nearestMapped;
            }
            
            // Search nearby vertices
            for (int offset = 1; offset < step; offset++) {
                if (vertexMap.ContainsKey(nearestMapped + offset)) {
                    return nearestMapped + offset;
                }
                if (nearestMapped - offset >= 0 && vertexMap.ContainsKey(nearestMapped - offset)) {
                    return nearestMapped - offset;
                }
            }
            
            return nearestMapped;
        }
        #endregion

        #region Performance Monitoring
        private void UpdatePerformanceMetrics() {
            _performanceMetrics.totalSegments = _segmentVisualizations.Count;
            _performanceMetrics.drawCalls = 0;
            _performanceMetrics.triangleCount = 0;
            _performanceMetrics.vertexCount = 0;
            
            foreach (var viz in _segmentVisualizations.Values) {
                if (viz.segmentMesh != null) {
                    _performanceMetrics.triangleCount += viz.segmentMesh.triangles.Length / 3;
                    _performanceMetrics.vertexCount += viz.segmentMesh.vertexCount;
                }
                
                if (viz.overlayObject != null && viz.overlayObject.activeInHierarchy) {
                    _performanceMetrics.drawCalls++;
                }
            }
            
            // Update memory usage
            _performanceMetrics.peakMemoryUsage = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
            
            if (showPerformanceStats) {
                LogPerformanceStats();
            }
        }
        
        private void LogPerformanceStats() {
            debugger.Log($"=== Segmentation Performance ===", LogCategory.Visualization);
            debugger.Log($"Total Segments: {_performanceMetrics.totalSegments}", LogCategory.Visualization);
            debugger.Log($"Visible: {_performanceMetrics.visibleSegments} | Culled: {_performanceMetrics.culledSegments}", LogCategory.Visualization);
            debugger.Log($"Draw Calls: {_performanceMetrics.drawCalls}", LogCategory.Visualization);
            debugger.Log($"Triangles: {_performanceMetrics.triangleCount:N0} | Vertices: {_performanceMetrics.vertexCount:N0}", LogCategory.Visualization);
            debugger.Log($"Memory: {_performanceMetrics.peakMemoryUsage:F1} MB", LogCategory.Visualization);
        }
        #endregion

        #region Specialized Visualization Modes
        private Mesh GenerateFillMesh(SegmentVisualization viz, UnityEngine.Terrain terrain) {
            // Create a filled mesh that follows terrain contours
            var mesh = GenerateOverlayMesh(viz, terrain);
            if (mesh == null) return null;
            
            // Make it double-sided for fill visualization
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var newTriangles = new int[triangles.Length * 2];
            
            // Copy original triangles
            System.Array.Copy(triangles, 0, newTriangles, 0, triangles.Length);
            
            // Add reversed triangles for back face
            for (int i = 0; i < triangles.Length; i += 3) {
                newTriangles[triangles.Length + i] = triangles[i + 2];
                newTriangles[triangles.Length + i + 1] = triangles[i + 1];
                newTriangles[triangles.Length + i + 2] = triangles[i];
            }
            
            mesh.triangles = newTriangles;
            return mesh;
        }
        
        private Mesh GenerateWireframeMesh(SegmentVisualization viz, UnityEngine.Terrain terrain) {
            // Generate a wireframe representation
            var baseMesh = GenerateOverlayMesh(viz, terrain);
            if (baseMesh == null) return null;
            
            var wireframeMesh = new Mesh();
            wireframeMesh.name = baseMesh.name + "_Wireframe";
            
            var vertices = baseMesh.vertices;
            var triangles = baseMesh.triangles;
            
            // Create line list from triangles
            var lineVertices = new List<Vector3>();
            var lineIndices = new List<int>();
            
            for (int i = 0; i < triangles.Length; i += 3) {
                // Add each edge of the triangle
                int v0 = triangles[i];
                int v1 = triangles[i + 1];
                int v2 = triangles[i + 2];
                
                // Edge 0-1
                lineVertices.Add(vertices[v0]);
                lineVertices.Add(vertices[v1]);
                lineIndices.Add(lineVertices.Count - 2);
                lineIndices.Add(lineVertices.Count - 1);
                
                // Edge 1-2
                lineVertices.Add(vertices[v1]);
                lineVertices.Add(vertices[v2]);
                lineIndices.Add(lineVertices.Count - 2);
                lineIndices.Add(lineVertices.Count - 1);
                
                // Edge 2-0
                lineVertices.Add(vertices[v2]);
                lineVertices.Add(vertices[v0]);
                lineIndices.Add(lineVertices.Count - 2);
                lineIndices.Add(lineVertices.Count - 1);
            }
            
            wireframeMesh.SetVertices(lineVertices);
            wireframeMesh.SetIndices(lineIndices, MeshTopology.Lines, 0);
            wireframeMesh.RecalculateBounds();
            
            return wireframeMesh;
        }
        
        private Mesh GenerateHeatmapMesh(SegmentVisualization viz, UnityEngine.Terrain terrain) {
            var mesh = GenerateOverlayMesh(viz, terrain);
            if (mesh == null) return null;
            
            // Apply vertex colors based on confidence or other metrics
            var vertices = mesh.vertices;
            var colors = new Color[vertices.Length];
            
            float confidence = 0.5f;
            if (viz.terrainFeature != null) {
                confidence = viz.terrainFeature.confidence;
            } else if (viz.mapObject != null) {
                confidence = viz.mapObject.confidence;
            }
            
            Color heatmapColor = confidenceGradient.Evaluate(confidence);
            
            for (int i = 0; i < colors.Length; i++) {
                colors[i] = heatmapColor;
            }
            
            mesh.colors = colors;
            return mesh;
        }
        
        private Mesh GenerateContourMesh(SegmentVisualization viz, UnityEngine.Terrain terrain) {
            // Generate contour lines at regular elevation intervals
            var mesh = new Mesh();
            mesh.name = $"Segment_{viz.id}_Contour";
            
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            
            float contourInterval = 5f; // 5 meter intervals
            float minHeight = viz.bounds.min.y;
            float maxHeight = viz.bounds.max.y;
            
            for (float height = minHeight; height <= maxHeight; height += contourInterval) {
                // Generate contour at this height
                var contourPoints = GenerateContourAtHeight(viz, terrain, height);
                
                if (contourPoints.Count > 1) {
                    int startIndex = vertices.Count;
                    vertices.AddRange(contourPoints);
                    
                    // Create line strip
                    for (int i = 0; i < contourPoints.Count - 1; i++) {
                        indices.Add(startIndex + i);
                        indices.Add(startIndex + i + 1);
                    }
                }
            }
            
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        private List<Vector3> GenerateContourAtHeight(SegmentVisualization viz, UnityEngine.Terrain terrain, float targetHeight) {
            var contourPoints = new List<Vector3>();
            
            // Sample points along the segment boundary
            if (viz.borderPoints != null) {
                foreach (var point in viz.borderPoints) {
                    Vector3 worldPos = new Vector3(
                        viz.bounds.min.x + point.x * viz.bounds.size.x,
                        0,
                        viz.bounds.min.z + point.z * viz.bounds.size.z
                    );
                    
                    float terrainHeight = terrain.SampleHeight(worldPos);
                    if (Mathf.Abs(terrainHeight - targetHeight) < 1f) {
                        worldPos.y = targetHeight + overlayYOffset;
                        contourPoints.Add(worldPos);
                    }
                }
            }
            
            return contourPoints;
        }
        #endregion

        #region GPU Processing
        private void SetupGPUProcessing() {
            if (!SystemInfo.supportsComputeShaders || _segmentationShader == null) {
                return;
            }
            
            // Create render texture for segmentation processing
            _segmentationRT = new RenderTexture(1024, 1024, 0, RenderTextureFormat.ARGB32);
            _segmentationRT.enableRandomWrite = true;
            _segmentationRT.Create();
        }
        
        private void ProcessSegmentationGPU(Texture2D sourceImage, List<ImageSegment> segments) {
            if (_segmentationShader == null || _segmentationRT == null) {
                return;
            }
            
            int kernel = _segmentationShader.FindKernel("ProcessSegmentation");
            
            // Set textures
            _segmentationShader.SetTexture(kernel, "_SourceImage", sourceImage);
            _segmentationShader.SetTexture(kernel, "_Result", _segmentationRT);
            
            // Set parameters
            _segmentationShader.SetInt("_SegmentCount", segments.Count);
            _segmentationShader.SetFloat("_Threshold", 0.5f);
            
            // Dispatch
            int threadGroupsX = Mathf.CeilToInt(_segmentationRT.width / 8f);
            int threadGroupsY = Mathf.CeilToInt(_segmentationRT.height / 8f);
            _segmentationShader.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);
        }
        #endregion

        #region Cleanup
        private void ClearPrevious() {
            // Stop coroutines
            if (_animationCoroutine != null) {
                StopCoroutine(_animationCoroutine);
                _animationCoroutine = null;
            }
            
            if (_cullingCoroutine != null) {
                StopCoroutine(_cullingCoroutine);
                _cullingCoroutine = null;
            }
            
            if (_interactionCoroutine != null) {
                StopCoroutine(_interactionCoroutine);
                _interactionCoroutine = null;
            }
            
            // Complete any pending jobs
            _currentJob.Complete();
            
            // Dispose native arrays
            if (_segmentPositions.IsCreated) {
                _segmentPositions.Dispose();
            }
            
            if (_segmentVisibility.IsCreated) {
                _segmentVisibility.Dispose();
            }
            
            // Destroy tooltip
            if (_interactionState.tooltipInstance != null) {
                Destroy(_interactionState.tooltipInstance);
                _interactionState.tooltipInstance = null;
            }
            
            // Destroy created objects
            foreach (var obj in _createdObjects) {
                if (obj != null) Destroy(obj);
            }
            _createdObjects.Clear();
            
            // Clear visualizations
            foreach (var viz in _segmentVisualizations.Values) {
                if (viz.overlayObject != null) Destroy(viz.overlayObject);
                if (viz.labelObject != null) Destroy(viz.labelObject);
                foreach (var border in viz.borderObjects) {
                    if (border != null) Destroy(border);
                }
                if (viz.instanceMaterial != null) Destroy(viz.instanceMaterial);
                if (viz.segmentMesh != null) Destroy(viz.segmentMesh);
            }
            _segmentVisualizations.Clear();
            
            // Clear groups
            foreach (var group in _segmentGroups.Values) {
                foreach (var line in group.connectionLines) {
                    if (line != null) Destroy(line);
                }
            }
            _segmentGroups.Clear();
            
            // Reset state
            _interactionState = new InteractionState();
            _performanceMetrics = new PerformanceMetrics();
        }
        
        private void OnDestroy() {
            ClearPrevious();
            
            // Release GPU resources
            if (_segmentationRT != null) {
                _segmentationRT.Release();
                Destroy(_segmentationRT);
            }
            
            debugger.Log("SegmentationVisualizer cleaned up", LogCategory.System);
        }
        #endregion

        #region Helper Methods
        
        private void InitializeMaterials()
        {
            // Initialize default materials if not assigned
            if (overlayMaterial == null)
            {
                overlayMaterial = new Material(Shader.Find("Standard"));
                overlayMaterial.name = "DefaultOverlayMaterial";
            }
        }
        
        private GameObject CreateDefaultOverlayPrefab()
        {
            GameObject prefab = new GameObject("DefaultOverlay");
            prefab.AddComponent<MeshFilter>();
            prefab.AddComponent<MeshRenderer>();
            return prefab;
        }
        
        private GameObject CreateDefaultLabelPrefab()
        {
            GameObject prefab = new GameObject("DefaultLabel");
            prefab.AddComponent<TextMeshPro>();
            return prefab;
        }
        
        private GameObject CreateDefaultTooltipPrefab()
        {
            GameObject prefab = new GameObject("DefaultTooltip");
            Canvas canvas = prefab.AddComponent<Canvas>();
            prefab.AddComponent<CanvasScaler>();
            prefab.AddComponent<GraphicRaycaster>();
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(prefab.transform);
            textGO.AddComponent<TextMeshProUGUI>();
            
            return prefab;
        }
        
        #region TraversifyComponent Implementation
        
        /// <summary>
        /// Component-specific initialization logic.
        /// </summary>
        /// <param name="config">Optional configuration object</param>
        /// <returns>True if initialization was successful</returns>
        protected override bool OnInitialize(object config) {
            try {
                // Initialize debugger
                if (debugger == null) {
                    debugger = GetComponent<TraversifyDebugger>();
                    if (debugger == null) {
                        debugger = gameObject.AddComponent<TraversifyDebugger>();
                    }
                }
                
                // Initialize collections and state
                _segmentOverlays.Clear();
                _segmentLabels.Clear();
                _fadingSegments.Clear();
                
                // Reset state
                _isVisualizationActive = false;
                _currentAnalysisResults = null;
                
                // Initialize materials if not set
                if (overlayMaterial == null) {
                    overlayMaterial = new Material(Shader.Find("Standard"));
                    overlayMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    overlayMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    overlayMaterial.SetInt("_ZWrite", 0);
                    overlayMaterial.renderQueue = 3000;
                }
                
                debugger?.Log("SegmentationVisualizer initialized successfully", LogCategory.System);
                return true;
            }
            catch (Exception ex) {
                debugger?.LogError($"Failed to initialize SegmentationVisualizer: {ex.Message}", LogCategory.System);
                return false;
            }
        }
        
        #endregion
        
        #endregion

        #region Public Methods
        
        /// <summary>
        /// Visualizes analysis results by creating overlays and labels for detected segments
        /// </summary>
        /// <param name="results">The analysis results to visualize</param>
        /// <param name="terrain">The terrain to place visualizations on</param>
        /// <param name="onComplete">Callback when visualization is complete</param>
        /// <returns>Coroutine for the visualization process</returns>
        public IEnumerator VisualizeResults(AnalysisResults results, UnityEngine.Terrain terrain, System.Action onComplete = null)
        {
            if (results == null)
            {
                debugger?.LogError("Analysis results are null", LogCategory.Visualization);
                onComplete?.Invoke();
                yield break;
            }
            
            if (terrain == null)
            {
                debugger?.LogError("Terrain is null", LogCategory.Visualization);
                onComplete?.Invoke();
                yield break;
            }
            
            debugger?.Log("Starting segmentation visualization", LogCategory.Visualization);
            
            // Clear any existing visualizations
            ClearVisualization();
            
            int totalItems = (results.terrainFeatures?.Count ?? 0) + (results.mapObjects?.Count ?? 0);
            int processedItems = 0;
            
            // Visualize terrain features as overlays
            if (results.terrainFeatures != null)
            {
                foreach (var feature in results.terrainFeatures)
                {
                    CreateTerrainFeatureOverlay(feature, terrain);
                    processedItems++;
                    
                    // Yield every few items to prevent frame drops
                    if (processedItems % 5 == 0)
                        yield return null;
                }
            }
            
            // Visualize map objects as labels/markers
            if (results.mapObjects != null)
            {
                foreach (var mapObj in results.mapObjects)
                {
                    CreateMapObjectLabel(mapObj, terrain);
                    processedItems++;
                    
                    // Yield every few items to prevent frame drops
                    if (processedItems % 5 == 0)
                        yield return null;
                }
            }
            
            debugger?.Log($"Visualization complete: {processedItems} items visualized", LogCategory.Visualization);
            onComplete?.Invoke();
        }
        
        /// <summary>
        /// Clears all existing visualizations
        /// </summary>
        public void ClearVisualization()
        {
            // Find and destroy all existing overlay objects
            var existingOverlays = GameObject.FindGameObjectsWithTag("TerrainOverlay");
            foreach (var overlay in existingOverlays)
            {
                if (Application.isPlaying)
                    Destroy(overlay);
                else
                    DestroyImmediate(overlay);
            }
            
            // Find and destroy all existing label objects
            var existingLabels = GameObject.FindGameObjectsWithTag("ObjectLabel");
            foreach (var label in existingLabels)
            {
                if (Application.isPlaying)
                    Destroy(label);
                else
                    DestroyImmediate(label);
            }
            
            debugger?.Log("Visualization cleared", LogCategory.Visualization);
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// Creates a visual overlay for a terrain feature
        /// </summary>
        private void CreateTerrainFeatureOverlay(TerrainFeature feature, UnityEngine.Terrain terrain)
        {
            if (feature == null || terrain == null) return;
            
            try
            {
                GameObject overlay = overlayPrefab ? Instantiate(overlayPrefab) : GameObject.CreatePrimitive(PrimitiveType.Quad);
                overlay.name = $"Overlay_{feature.label}";
                overlay.tag = "TerrainOverlay";
                
                // Position and scale the overlay based on the feature's bounding box
                Vector3 terrainSize = terrain.terrainData.size;
                Vector3 terrainPos = terrain.transform.position;
                
                // Convert bounding box to world coordinates
                float worldX = terrainPos.x + (feature.boundingBox.x / 1000f) * terrainSize.x; // Assuming 1000px = terrain width
                float worldZ = terrainPos.z + (feature.boundingBox.y / 1000f) * terrainSize.z;
                float worldWidth = (feature.boundingBox.width / 1000f) * terrainSize.x;
                float worldHeight = (feature.boundingBox.height / 1000f) * terrainSize.z;
                
                // Sample terrain height at the center
                Vector3 centerPos = new Vector3(worldX + worldWidth/2, 0, worldZ + worldHeight/2);
                float terrainHeight = terrain.SampleHeight(centerPos);
                
                // Position the overlay
                overlay.transform.position = new Vector3(
                    centerPos.x, 
                    terrainHeight + overlayYOffset, 
                    centerPos.z
                );
                
                // Scale the overlay
                overlay.transform.localScale = new Vector3(worldWidth, 1, worldHeight);
                overlay.transform.rotation = Quaternion.Euler(90, 0, 0); // Flat on ground
                
                // Apply material and color
                Renderer renderer = overlay.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material mat = overlayMaterial ? Instantiate(overlayMaterial) : new Material(Shader.Find("Standard"));
                    mat.color = feature.segmentColor;
                    mat.SetFloat("_Mode", 3); // Transparent mode
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;
                    renderer.material = mat;
                }
                
                // Start fade-in animation if enabled
                if (animateSegments)
                {
                    StartCoroutine(FadeInOverlay(overlay));
                }
                
                debugger?.Log($"Created overlay for terrain feature: {feature.label}", LogCategory.Visualization);
            }
            catch (System.Exception ex)
            {
                debugger?.LogError($"Error creating terrain overlay for {feature.label}: {ex.Message}", LogCategory.Visualization);
            }
        }
        
        /// <summary>
        /// Creates a label for a map object
        /// </summary>
        private void CreateMapObjectLabel(MapObject mapObj, UnityEngine.Terrain terrain)
        {
            if (mapObj == null || terrain == null) return;
            
            try
            {
                GameObject label = labelPrefab ? Instantiate(labelPrefab) : new GameObject($"Label_{mapObj.label}");
                label.name = $"Label_{mapObj.label}";
                label.tag = "ObjectLabel";
                
                // Convert normalized position to world position
                Vector3 terrainSize = terrain.terrainData.size;
                Vector3 terrainPos = terrain.transform.position;
                
                Vector3 worldPos = new Vector3(
                    terrainPos.x + mapObj.position.x * terrainSize.x,
                    0,
                    terrainPos.z + mapObj.position.y * terrainSize.z
                );
                
                // Sample terrain height and add offset
                float terrainHeight = terrain.SampleHeight(worldPos);
                worldPos.y = terrainHeight + labelYOffset;
                
                label.transform.position = worldPos;
                
                // Create or update text component
                TMPro.TextMeshPro textMesh = label.GetComponent<TMPro.TextMeshPro>();
                if (textMesh == null)
                {
                    textMesh = label.AddComponent<TMPro.TextMeshPro>();
                }
                
                // Set text properties
                textMesh.text = !string.IsNullOrEmpty(mapObj.enhancedDescription) ? mapObj.enhancedDescription : mapObj.label;
                textMesh.fontSize = labelFontSize;
                textMesh.color = labelTextColor;
                textMesh.alignment = TMPro.TextAlignmentOptions.Center;
                
                if (labelFont != null)
                {
                    textMesh.font = labelFont;
                }
                
                // Add billboard behavior if enabled
                if (billboardLabels)
                {
                    Billboard billboard = label.GetComponent<Billboard>();
                    if (billboard == null)
                    {
                        billboard = label.AddComponent<Billboard>();
                    }
                }
                
                // Add background quad if needed
                if (labelBackgroundColor.a > 0)
                {
                    CreateLabelBackground(label, textMesh);
                }
                
                debugger?.Log($"Created label for map object: {mapObj.label}", LogCategory.Visualization);
            }
            catch (System.Exception ex)
            {
                debugger?.LogError($"Error creating object label for {mapObj.label}: {ex.Message}", LogCategory.Visualization);
            }
        }
        
        /// <summary>
        /// Creates a background for the label text
        /// </summary>
        private void CreateLabelBackground(GameObject label, TMPro.TextMeshPro textMesh)
        {
            GameObject background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            background.transform.SetParent(label.transform);
            background.transform.localPosition = Vector3.back * 0.01f; // Slightly behind text
            
            // Size background to text bounds
            Bounds textBounds = textMesh.bounds;
            background.transform.localScale = new Vector3(textBounds.size.x * 1.2f, textBounds.size.y * 1.2f, 1f);
            
            // Apply background material
            Renderer bgRenderer = background.GetComponent<Renderer>();
            if (bgRenderer != null)
            {
                Material bgMat = new Material(Shader.Find("Standard"));
                bgMat.color = labelBackgroundColor;
                bgMat.SetFloat("_Mode", 3); // Transparent mode
                bgMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                bgMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                bgMat.SetInt("_ZWrite", 0);
                bgMat.renderQueue = 2999; // Behind text
                bgRenderer.material = bgMat;
            }
        }
        
        /// <summary>
        /// Fade-in animation for overlays
        /// </summary>
        private IEnumerator FadeInOverlay(GameObject overlay)
        {
            Renderer renderer = overlay.GetComponent<Renderer>();
            if (renderer == null) yield break;
            
            Material mat = renderer.material;
            Color originalColor = mat.color;
            Color transparentColor = new Color(originalColor.r, originalColor.g, originalColor.b, 0f);
            
            // Start transparent
            mat.color = transparentColor;
            
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;
                
                // Apply animation curve if available
                if (fadeInCurve != null)
                    t = fadeInCurve.Evaluate(t);
                
                Color currentColor = Color.Lerp(transparentColor, originalColor, t);
                mat.color = currentColor;
                
                yield return null;
            }
            
            // Ensure final color is set
            mat.color = originalColor;
        }
        
        #endregion
    }
}
