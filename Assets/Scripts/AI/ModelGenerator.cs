/*************************************************************************
 *  Traversify – ModelGenerator.cs                                       *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Desc   : Advanced model generation and placement system for          *
 *           Traversify. Handles 3D model creation, optimization,        *
 *           instancing, and precise terrain alignment.                  *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Traversify.Core;
using Traversify.AI;

namespace Traversify {
    [RequireComponent(typeof(TraversifyDebugger))]
    public class ModelGenerator : TraversifyComponent {
        #region Singleton
        private static ModelGenerator _instance;
        public static ModelGenerator Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<ModelGenerator>();
                    if (_instance == null) {
                        var go = new GameObject("ModelGenerator");
                        _instance = go.AddComponent<ModelGenerator>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Configuration
        [Header("Generation Settings")]
        [Tooltip("Group similar objects into instances instead of unique models")]
        public bool groupSimilarObjects = true;
        
        [Tooltip("Similarity threshold (0–1) for instancing")]
        [Range(0f, 1f)] public float instancingSimilarity = 0.8f;
        
        [Tooltip("Similarity metric to use for comparison")]
        public SimilarityMetric similarityMetric = SimilarityMetric.FeatureVector;
        
        [Tooltip("Maximum number of objects in a single group")]
        public int maxGroupSize = 50;
        
        [Tooltip("Enable level of detail (LOD) for generated models")]
        public bool useLOD = true;
        
        [Tooltip("Maximum number of LOD levels to generate")]
        [Range(1, 5)] public int maxLODLevels = 3;
        
        [Tooltip("LOD level quality reduction factor")]
        [Range(0.1f, 0.9f)] public float lodQualityFactor = 0.5f;

        [Header("Placement Settings")]
        [Tooltip("Apply IK/procedural adaptation to terrain")]
        public bool adaptToTerrain = true;
        
        [Tooltip("Maximum slope angle for placement (degrees)")]
        [Range(0f, 90f)] public float maxPlacementSlope = 45f;
        
        [Tooltip("Enable collision detection between objects")]
        public bool avoidCollisions = true;
        
        [Tooltip("Distance to maintain between objects (meters)")]
        public float objectSpacing = 1.0f;
        
        [Tooltip("Use ambient occlusion for placement")]
        public bool useAmbientOcclusion = true;
        
        [Tooltip("Sample size for terrain normal computation")]
        public int normalSampleSize = 5;
        
        [Tooltip("Enable object floating correction")]
        public bool fixFloatingObjects = true;
        
        [Tooltip("Enable object embedding correction")]
        public bool fixEmbeddedObjects = true;
        
        [Tooltip("Object sink depth for grounding")]
        [Range(0f, 1f)] public float groundingDepth = 0.05f;

        [Header("Performance")]
        [Tooltip("Use multithreading for model generation")]
        public bool useMultithreading = true;
        
        [Tooltip("Number of worker threads")]
        [Range(1, 16)] public int workerThreadCount = 4;
        
        [Tooltip("Maximum models to generate concurrently")]
        [Range(1, 16)] public int maxConcurrentModels = 4;
        
        [Tooltip("Maximum API requests concurrently")]
        [Range(1, 16)] public int maxConcurrentAPIRequests = 4;
        
        [Tooltip("Delay between API requests (seconds)")]
        [Range(0f, 5f)] public float apiRateLimitDelay = 0.5f;
        
        [Tooltip("Enable GPU instancing for similar models")]
        public bool useGPUInstancing = true;
        
        [Tooltip("Use mesh combining for performance")]
        public bool useMeshCombining = true;
        
        [Tooltip("Maximum vertices per combined mesh")]
        public int maxVerticesPerMesh = 65000;
        
        [Tooltip("Timeout seconds per model request")]
        [Range(5f, 120f)] public float generationTimeout = 60f;

        [Header("AI & External Services")]
        [Tooltip("OpenAI key for description enhancement")]
        public string openAIApiKey;
        
        [Header("Tripo3D Settings")]
        [Tooltip("Enable Tripo3D generation for detected objects")]
        public bool useTripo3D = true;
        
        [Tooltip("Tripo3D API key for model generation")]
        [SerializeField] private string tripo3DApiKey;
        
        [Tooltip("Quality setting for Tripo3D generation")]
        public Tripo3DQuality tripo3DQuality = Tripo3DQuality.High;
        
        [Tooltip("Maximum concurrent Tripo3D requests")]
        [Range(1, 10)] public int maxConcurrentRequests = 3;
        
        [Tooltip("Timeout for Tripo3D requests (seconds)")]
        [Range(30, 300)] public float requestTimeout = 120f;
        
        [Tooltip("Generate terrain features using Tripo3D")]
        public bool generateTerrainWith3D = true;
        
        [Tooltip("Generate buildings and structures using Tripo3D")]
        public bool generateBuildingsWith3D = true;
        
        [Tooltip("Generate vegetation using Tripo3D")]
        public bool generateVegetationWith3D = false; // Often better with Unity's terrain system
        
        public enum Tripo3DQuality
        {
            Draft,
            Standard,
            High,
            Ultra
        }

        [Header("Materials & Texturing")]
        [Tooltip("Default material for generated models")]
        public Material defaultMaterial;
        
        [Tooltip("Enable PBR material generation")]
        public bool generatePBRMaterials = true;
        
        [Tooltip("Enable texture generation")]
        public bool generateTextures = true;
        
        [Tooltip("Texture resolution")]
        public Vector2Int textureResolution = new Vector2Int(1024, 1024);
        
        [Tooltip("Enable normal map generation")]
        public bool generateNormalMaps = true;
        
        [Tooltip("Enable metallic/roughness map generation")]
        public bool generateMetallicMaps = true;

        [Header("Debug")]
        public TraversifyDebugger debugger;
        
        [Tooltip("Show debug gizmos for placement")]
        public bool showPlacementGizmos = false;
        
        [Tooltip("Show model generation progress")]
        public bool showProgress = true;
        
        [Tooltip("Log detailed generation steps")]
        public bool verboseLogging = false;
        
        [Tooltip("Generate model validation report")]
        public bool generateValidationReport = false;

        [Header("Cache Settings")]
        [Tooltip("Enable caching of generated models")]
        public bool useCachedModels = true;
        
        [Tooltip("Path to fallback models in Resources folder")]
        private string fallbackModelPath = "Fallback/Models";

        #endregion

        #region Private Fields
        // Processing queues
        private Queue<ModelGenerationRequest> _generationQueue = new Queue<ModelGenerationRequest>();
        private List<ModelGenerationJob> _activeJobs = new List<ModelGenerationJob>();
        private Dictionary<string, GameObject> _modelCache = new Dictionary<string, GameObject>();
        private Dictionary<string, ModelGenerationStatus> _generationStatus = new Dictionary<string, ModelGenerationStatus>();
        
        // Threading
        private SemaphoreSlim _apiSemaphore;
        private CancellationTokenSource _cancellationTokenSource;
        private TaskCompletionSource<bool> _shutdownCompletionSource;
        
        // Utility components
        private GameObject _modelContainer;
        private Material _instancedMaterial;
        private ComputeShader _placementComputeShader;
        private Dictionary<string, UnityEngine.Terrain> _terrainCache = new Dictionary<string, UnityEngine.Terrain>();
        
        // Terrain configuration
        private Vector2 terrainSize = new Vector2(500f, 500f);
        
        // Metrics
        private int _totalModelsGenerated = 0;
        private int _totalInstancesCreated = 0;
        private int _failedGenerations = 0;
        private float _averageGenerationTime = 0;
        private Dictionary<string, int> _modelTypeDistribution = new Dictionary<string, int>();
        
        // State
        private bool _isProcessing = false;
        private bool _isInitialized = false;
        private ModelGenerationConfig _generationConfig;
        private List<ModelGenerationRequest> _activeRequests = new List<ModelGenerationRequest>();
        
        // Tripo3D generation state
        private Dictionary<string, List<DetectedObject>> _objectGroups = new Dictionary<string, List<DetectedObject>>();
        private Dictionary<string, GameObject> _generatedModels = new Dictionary<string, GameObject>();
        private Queue<Tripo3DRequest> _tripo3DQueue = new Queue<Tripo3DRequest>();
        private int _activeTripo3DRequests = 0;
        #endregion

        #region Initialization
        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            InitializeModelGenerator();
        }
        
        private void InitializeModelGenerator() {
            // Get or add debugger
            debugger = GetComponent<TraversifyDebugger>();
            if (debugger == null) {
                debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            // Initialize threading components
            _apiSemaphore = new SemaphoreSlim(maxConcurrentRequests);
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Create model container
            _modelContainer = new GameObject("GeneratedModels");
            _modelContainer.transform.SetParent(transform);
            
            // Load compute shader if available
            if (useGPUInstancing || adaptToTerrain) {
                _placementComputeShader = Resources.Load<ComputeShader>("Shaders/ObjectPlacement");
                if (_placementComputeShader == null) {
                    debugger.LogWarning("Object placement compute shader not found. Falling back to CPU placement.", LogCategory.Models);
                }
            }
            
            // Create instanced material
            if (useGPUInstancing) {
                InitializeInstancedMaterial();
            }
            
            // Initialize configuration
            _generationConfig = new ModelGenerationConfig {
                UseGPUInstancing = useGPUInstancing && SystemInfo.supportsInstancing,
                UsePBRMaterials = generatePBRMaterials,
                GenerateTextures = generateTextures,
                TextureResolution = textureResolution,
                GenerateNormalMaps = generateNormalMaps,
                GenerateMetallicMaps = generateMetallicMaps,
                UseLOD = useLOD,
                MaxLODLevels = maxLODLevels,
                LodQualityFactor = lodQualityFactor,
                AdaptToTerrain = adaptToTerrain,
                AvoidCollisions = avoidCollisions,
                MaxPlacementSlope = maxPlacementSlope,
                ObjectSpacing = objectSpacing,
                GroupSimilarObjects = groupSimilarObjects,
                InstancingSimilarity = instancingSimilarity,
                SimilarityMetric = similarityMetric,
                MaxGroupSize = maxGroupSize,
                GroundingDepth = groundingDepth,
                FixFloatingObjects = fixFloatingObjects,
                FixEmbeddedObjects = fixEmbeddedObjects
            };
            
            LoadFallbackModels();
            
            _isInitialized = true;
            debugger.Log("ModelGenerator initialized", LogCategory.Models);
        }
        
        private void InitializeInstancedMaterial() {
            // Create a material that supports GPU instancing
            if (defaultMaterial != null) {
                _instancedMaterial = new Material(defaultMaterial);
            } else {
                _instancedMaterial = new Material(Shader.Find("Standard"));
            }
            
            _instancedMaterial.enableInstancing = true;
            _instancedMaterial.name = "InstancedModelMaterial";
        }
        
        private void LoadFallbackModels() {
            try {
                var fallbackModels = Resources.LoadAll<GameObject>(fallbackModelPath);
                debugger.Log($"Loaded {fallbackModels.Length} fallback models", LogCategory.Models);
            }
            catch (Exception ex) {
                debugger.LogWarning($"Failed to load fallback models: {ex.Message}", LogCategory.Models);
            }
        }
        
        private void OnDestroy() {
            ShutdownAsync().ContinueWith(_ => {
                _apiSemaphore?.Dispose();
                _cancellationTokenSource?.Dispose();
            });
            
            if (_modelContainer != null) {
                Destroy(_modelContainer);
            }
            
            debugger?.Log("ModelGenerator destroyed", LogCategory.Models);
        }
        
        private async Task ShutdownAsync() {
            if (!_isInitialized) return;
            
            _shutdownCompletionSource = new TaskCompletionSource<bool>();
            _cancellationTokenSource.Cancel();
            
            // Wait for all active requests to complete or timeout after 5 seconds
            await Task.WhenAny(_shutdownCompletionSource.Task, Task.Delay(5000));
            
            _isInitialized = false;
        }
        #endregion

        #region Public API
        /// <summary>
        /// Generate and place 3D models based on analysis results and terrain.
        /// </summary>
        public IEnumerator GenerateAndPlaceModels(
            AnalysisResults analysis,
            UnityEngine.Terrain terrain,
            Action<List<GameObject>> onComplete = null,
            Action<string> onError = null,
            Action<int, int> onProgress = null,
            bool showProgress = true
        ) {
            if (!_isInitialized) {
                onError?.Invoke("ModelGenerator not initialized");
                yield break;
            }
            
            if (analysis == null) {
                onError?.Invoke("No analysis results provided");
                yield break;
            }
            
            if (terrain == null) {
                onError?.Invoke("No terrain provided for model placement");
                yield break;
            }
            
            debugger.StartTimer("ModelGeneration");
            _isProcessing = true;
            
            // Store error state instead of using try-catch around yield
            string errorMessage = null;
            List<GameObject> createdObjects = new List<GameObject>();
            
            // Clear existing generation queue
            _generationQueue.Clear();
            
            // Cache terrain for fast lookups
            string terrainId = terrain.GetInstanceID().ToString();
            if (!_terrainCache.ContainsKey(terrainId)) {
                _terrainCache[terrainId] = terrain;
            }
            
            // Process object groups or individual objects
            List<ModelGenerationRequest> requests = new List<ModelGenerationRequest>();
            
            if (groupSimilarObjects && analysis.objectGroups != null && analysis.objectGroups.Count > 0) {
                // Use pre-grouped objects
                foreach (var group in analysis.objectGroups) {
                    requests.AddRange(CreateRequestsFromGroup(group, terrain));
                }
            } 
            else if (groupSimilarObjects) {
                // Group objects ourselves
                var groups = GroupObjectsBySimilarity(analysis.mapObjects);
                foreach (var group in groups) {
                    requests.AddRange(CreateRequestsFromGroup(group, terrain));
                }
            } 
            else {
                // Process individual objects
                foreach (var obj in analysis.mapObjects) {
                    var request = CreateRequestFromMapObject(obj, terrain);
                    requests.Add(request);
                }
            }
            
            // Report total to process
            int total = requests.Count;
            debugger.Log($"Preparing to generate {total} models", LogCategory.Models);
            
            // Add all requests to queue
            foreach (var request in requests) {
                _generationQueue.Enqueue(request);
            }
            
            var processingTasks = new List<Task>();
            int completed = 0;
            
            // Start processing the queue
            while (_generationQueue.Count > 0 || _activeRequests.Count > 0) {
                // Process new requests if slots available
                while (_generationQueue.Count > 0 && _activeRequests.Count < maxConcurrentRequests) {
                    var request = _generationQueue.Dequeue();
                    
                    // Start model generation with callback to track completion
                    _activeRequests.Add(request);
                    
                    var processTask = Task.Run(async () => {
                        try {
                            await _apiSemaphore.WaitAsync(_cancellationTokenSource.Token);
                            
                            GameObject result = null;
                            try {
                                result = await GenerateModelAsync(request);
                            }
                            finally {
                                _apiSemaphore.Release();
                            }
                            
                            if (result != null) {
                                lock (createdObjects) {
                                    createdObjects.Add(result);
                                }
                            }
                        }
                        catch (Exception ex) {
                            errorMessage = ex.Message;
                        }
                        
                        completed++;
                        onProgress?.Invoke(completed, total);
                        
                        lock (_activeRequests) {
                            _activeRequests.Remove(request);
                        }
                    });
                    
                    processingTasks.Add(processTask);
                }
                
                // Progress update
                if (showProgress) {
                    float progress = (float)completed / total;
                    debugger.ReportProgress($"Generating models ({completed}/{total})", progress);
                }
                
                yield return new WaitForSeconds(0.1f);
            }
            
            // Wait for all tasks to complete
            while (_activeRequests.Count > 0) {
                yield return new WaitForSeconds(0.1f);
            }
            
            // Apply final optimizations to models
            if (useMeshCombining) {
                createdObjects = CombineSimilarMeshes(createdObjects);
            }
            
            // Report performance metrics
            float generationTime = debugger.StopTimer("ModelGeneration");
            debugger.Log($"Model generation completed in {generationTime:F2}s - " +
                         $"Generated {createdObjects.Count} models ({_totalInstancesCreated} instances)", 
                         LogCategory.Models);
            
            LogModelDistribution(createdObjects);
            
            _isProcessing = false;
            
            // Handle any errors that occurred
            if (!string.IsNullOrEmpty(errorMessage)) {
                debugger.LogError($"GenerateAndPlaceModels failed: {errorMessage}", LogCategory.Models);
                onError?.Invoke(errorMessage);
            } else {
                // Return all created objects
                onComplete?.Invoke(createdObjects);
            }
        }
        
        /// <summary>
        /// Generate models using Tripo3D and place them precisely using ObjectPlacer
        /// </summary>
        public IEnumerator GenerateAndPlaceModelsWithObjectPlacer(
            AnalysisResults analysis,
            UnityEngine.Terrain terrain,
            Action<List<GameObject>> onComplete = null,
            Action<string> onError = null,
            Action<int, int> onProgress = null
        ) {
            if (!_isInitialized) {
                onError?.Invoke("ModelGenerator not initialized");
                yield break;
            }
            
            if (analysis == null) {
                onError?.Invoke("No analysis results provided");
                yield break;
            }
            
            if (terrain == null) {
                onError?.Invoke("No terrain provided for model placement");
                yield break;
            }
            
            debugger.StartTimer("ModelGenerationWithObjectPlacer");
            _isProcessing = true;
            
            string errorMessage = null;
            List<GameObject> generatedModels = new List<GameObject>();
            List<GameObject> placedObjects = new List<GameObject>();
            
            // Step 1: Generate models using Tripo3D
            debugger.Log("Starting model generation phase...", LogCategory.Models);
            
            yield return StartCoroutine(GenerateModelsOnly(analysis, 
                (models) => {
                    generatedModels.AddRange(models);
                    debugger.Log($"Generated {models.Count} models", LogCategory.Models);
                },
                (error) => {
                    errorMessage = error;
                },
                (completed, total) => {
                    // Report generation progress (first 70% of total progress)
                    onProgress?.Invoke(Mathf.RoundToInt(completed * 0.7f), total);
                }
            ));
            
            if (!string.IsNullOrEmpty(errorMessage)) {
                onError?.Invoke(errorMessage);
                _isProcessing = false;
                yield break;
            }
            
            // Step 2: Use ObjectPlacer for precise placement
            if (generatedModels.Count > 0) {
                debugger.Log("Starting precise placement phase with ObjectPlacer...", LogCategory.Models);
                
                // Create a model provider for ObjectPlacer
                var modelProvider = new GeneratedModelProvider(generatedModels, analysis.mapObjects);
                
                // Use ObjectPlacer for precise placement
                yield return StartCoroutine(ObjectPlacer.Instance.PlaceObjects(
                    analysis,
                    terrain,
                    modelProvider,
                    (objects) => {
                        placedObjects.AddRange(objects);
                        debugger.Log($"Placed {objects.Count} objects using ObjectPlacer", LogCategory.Models);
                    },
                    (completed, total) => {
                        // Report placement progress (remaining 30% of total progress)
                        int totalProgress = Mathf.RoundToInt(0.7f * analysis.mapObjects.Count + completed * 0.3f);
                        onProgress?.Invoke(totalProgress, analysis.mapObjects.Count);
                    },
                    (error) => {
                        errorMessage = error;
                    }
                ));
            }
            
            float generationTime = debugger.StopTimer("ModelGenerationWithObjectPlacer");
            debugger.Log($"Model generation and placement completed in {generationTime:F2}s - " +
                         $"Generated {generatedModels.Count} models, placed {placedObjects.Count} objects", 
                         LogCategory.Models);
            
            _isProcessing = false;
            
            if (!string.IsNullOrEmpty(errorMessage)) {
                onError?.Invoke(errorMessage);
            } else {
                onComplete?.Invoke(placedObjects);
            }
        }
        
        /// <summary>
        /// Generate models only without placement (for use with ObjectPlacer)
        /// </summary>
        private IEnumerator GenerateModelsOnly(
            AnalysisResults analysis,
            Action<List<GameObject>> onComplete,
            Action<string> onError,
            Action<int, int> onProgress
        ) {
            List<GameObject> generatedModels = new List<GameObject>();
            string errorMessage = null;
            
            // Group similar objects for efficient generation
            var groups = groupSimilarObjects ? GroupObjectsBySimilarity(analysis.mapObjects) : 
                         analysis.mapObjects.Select(obj => new ObjectGroup { 
                             groupId = Guid.NewGuid().ToString(), 
                             type = obj.type, 
                             objects = new List<MapObject> { obj } 
                         }).ToList();
            
            int totalGroups = groups.Count;
            int completedGroups = 0;
            
            // Generate one model per group (master models for instancing)
            foreach (var group in groups) {
                if (group.objects.Count == 0) continue;
                
                MapObject template = group.objects.First();
                
                // Create generation request for the master model
                var request = new ModelGenerationRequest {
                    objectType = template.type,
                    description = template.enhancedDescription ?? template.label,
                    position = Vector3.zero, // Position will be handled by ObjectPlacer
                    rotation = Quaternion.identity,
                    scale = Vector3.one, // Scale will be handled by ObjectPlacer
                    confidence = template.confidence,
                    isGrouped = true
                };
                
                bool generationComplete = false;
                GameObject generatedModel = null;
                string generationError = null;
                
                // Generate the model asynchronously
                var task = Task.Run(async () => {
                    try {
                        await _apiSemaphore.WaitAsync(_cancellationTokenSource.Token);
                        
                        try {
                            generatedModel = await GenerateModelAsync(request);
                        }
                        finally {
                            _apiSemaphore.Release();
                        }
                    }
                    catch (Exception ex) {
                        generationError = ex.Message;
                    }
                    finally {
                        generationComplete = true;
                    }
                });
                
                // Wait for generation to complete
                while (!generationComplete) {
                    yield return new WaitForSeconds(0.1f);
                }
                
                if (!string.IsNullOrEmpty(generationError)) {
                    errorMessage = generationError;
                    break;
                }
                
                if (generatedModel != null) {
                    // Store metadata for ObjectPlacer to use
                    var modelInfo = generatedModel.AddComponent<GeneratedModelInfo>();
                    modelInfo.sourceGroup = group;
                    modelInfo.objectType = template.type;
                    modelInfo.description = template.enhancedDescription ?? template.label;
                    
                    generatedModels.Add(generatedModel);
                }
                
                completedGroups++;
                onProgress?.Invoke(completedGroups, totalGroups);
            }
            
            if (!string.IsNullOrEmpty(errorMessage)) {
                onError?.Invoke(errorMessage);
            } else {
                onComplete?.Invoke(generatedModels);
            }
        }
        
        /// <summary>
        /// Generate a single model based on a type and description.
        /// </summary>
        public IEnumerator GenerateModel(
            string modelType,
            string description,
            Action<GameObject> onComplete = null,
            Action<string> onError = null,
            Action<float> onProgress = null
        ) {
            if (!_isInitialized) {
                onError?.Invoke("ModelGenerator not initialized");
                yield break;
            }
            
            var request = new ModelGenerationRequest {
                objectType = modelType,
                description = description,
                position = Vector3.zero,
                rotation = Quaternion.identity,
                scale = Vector3.one,
                confidence = 1.0f,
                isGrouped = false
            };
            
            debugger.StartTimer($"SingleModel_{modelType}");
            
            GameObject result = null;
            Exception error = null;
            
            var task = Task.Run(async () => {
                try {
                    await _apiSemaphore.WaitAsync();
                    result = await GenerateModelAsync(request);
                }
                catch (Exception ex) {
                    error = ex;
                }
                finally {
                    _apiSemaphore.Release();
                }
            });
            
            while (!task.IsCompleted) {
                onProgress?.Invoke(GetGenerationProgress(request.objectType));
                yield return null;
            }
            
            float generationTime = debugger.StopTimer($"SingleModel_{modelType}");
            
            if (error != null) {
                debugger.LogError($"GenerateModel failed: {error.Message}", LogCategory.Models);
                onError?.Invoke(error.Message);
                yield break;
            }
            
            if (result != null) {
                debugger.Log($"Model '{modelType}' generated in {generationTime:F2}s", LogCategory.Models);
                onComplete?.Invoke(result);
            } else {
                onError?.Invoke("Failed to generate model");
            }
        }
        
        /// <summary>
        /// Places a pre-generated model on the terrain.
        /// </summary>
        public GameObject PlaceModelOnTerrain(
            GameObject model,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            UnityEngine.Terrain terrain
        ) {
            if (model == null || terrain == null) return null;
            
            // Clone the model
            GameObject instance = Instantiate(model, _modelContainer.transform);
            instance.name = model.name + "_Instance";
            
            // Apply transforms
            PlacementTransform transform = new PlacementTransform {
                Position = position,
                Rotation = rotation,
                Scale = scale
            };
            
            // Adapt to terrain
            PlacementTransform adaptedTransform = adaptToTerrain ? 
                AdaptTransformToTerrain(transform, instance, terrain) : 
                transform;
            
            // Apply final transform
            instance.transform.position = adaptedTransform.Position;
            instance.transform.rotation = adaptedTransform.Rotation;
            instance.transform.localScale = adaptedTransform.Scale;
            
            // Add collider if needed
            if (instance.GetComponent<Collider>() == null && model.GetComponent<Collider>() == null) {
                var meshFilter = instance.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.mesh != null) {
                    var collider = instance.AddComponent<MeshCollider>();
                    collider.sharedMesh = meshFilter.mesh;
                    collider.convex = true;
                }
            }
            
            // Add instance tracking component for later updates
            var tracker = instance.AddComponent<ModelInstanceTracker>();
            tracker.sourceType = model.name;
            tracker.originalPosition = position;
            
            _totalInstancesCreated++;
            return instance;
        }
        
        /// <summary>
        /// Updates model placement on terrain after terrain changes.
        /// </summary>
        public void UpdateModelPlacement(GameObject model, UnityEngine.Terrain terrain) {
            if (model == null || terrain == null) return;
            
            var tracker = model.GetComponent<ModelInstanceTracker>();
            if (tracker == null) return;
            
            PlacementTransform transform = new PlacementTransform {
                Position = tracker.originalPosition,
                Rotation = model.transform.rotation,
                Scale = model.transform.localScale
            };
            
            PlacementTransform adaptedTransform = AdaptTransformToTerrain(transform, model, terrain);
            
            model.transform.position = adaptedTransform.Position;
            model.transform.rotation = adaptedTransform.Rotation;
        }
        
        /// <summary>
        /// Updates placement of all models managed by this generator.
        /// </summary>
        public void UpdateAllPlacements(UnityEngine.Terrain terrain) {
            if (terrain == null || _modelContainer == null) return;
            
            var trackers = _modelContainer.GetComponentsInChildren<ModelInstanceTracker>();
            foreach (var tracker in trackers) {
                UpdateModelPlacement(tracker.gameObject, terrain);
            }
        }
        
        /// <summary>
        /// Clears all generated models.
        /// </summary>
        public void ClearGeneratedModels() {
            if (_modelContainer != null) {
                foreach (Transform child in _modelContainer.transform) {
                    Destroy(child.gameObject);
                }
            }
            
            _totalModelsGenerated = 0;
            _totalInstancesCreated = 0;
            _modelTypeDistribution.Clear();
            debugger.Log("Cleared all generated models", LogCategory.Models);
        }
        #endregion

        #region Model Generation
        private async Task<GameObject> GenerateModelAsync(ModelGenerationRequest request) {
            string modelId = GenerateModelId(request.objectType, request.description);
            
            try {
                // Check cache first if enabled
                if (useCachedModels && _modelCache.TryGetValue(modelId, out GameObject cachedModel)) {
                    debugger.Log($"Using cached model for '{request.objectType}'", LogCategory.Models);
                    TrackModelGeneration(request.objectType);
                    
                    // Place the model on terrain if position is specified
                    if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrain)) {
                        return PlaceModelOnTerrain(
                            cachedModel,
                            request.position,
                            request.rotation,
                            request.scale,
                            terrain
                        );
                    }
                    
                    return Instantiate(cachedModel, _modelContainer.transform);
                }
                
                // Start tracking status
                UpdateGenerationStatus(modelId, 0.1f, "Preparing request");
                
                // Enhance description if OpenAI is available
                string enhancedDescription = request.description;
                if (!string.IsNullOrEmpty(openAIApiKey) && !string.IsNullOrEmpty(request.description)) {
                    enhancedDescription = await EnhanceDescriptionAsync(request.description, request.objectType);
                }
                
                // Format prompt with template
                string prompt = FormatPrompt(enhancedDescription, request.objectType);
                UpdateGenerationStatus(modelId, 0.2f, "Sending to Tripo3D");
                
                // Generate the model with Tripo3D
                if (string.IsNullOrEmpty(tripo3DApiKey)) {
                    // Use fallback if no API key
                    GameObject fallbackModel = GetFallbackModel(request.objectType);
                    if (fallbackModel != null) {
                        debugger.LogWarning($"Using fallback model for '{request.objectType}' (no API key)", LogCategory.Models);
                        TrackModelGeneration(request.objectType);
                        _modelCache[modelId] = fallbackModel;
                        
                        // Place the model if position is provided
                        if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrainForPlacement)) {
                            return PlaceModelOnTerrain(
                                fallbackModel,
                                request.position,
                                request.rotation,
                                request.scale,
                                terrainForPlacement
                            );
                        }
                        
                        return Instantiate(fallbackModel, _modelContainer.transform);
                    } else {
                        debugger.LogError($"No Tripo3D API key and no fallback model available for '{request.objectType}'", LogCategory.Models);
                        _failedGenerations++;
                        return null;
                    }
                }
                
                // Request model from Tripo3D
                GameObject modelData = await RequestTripo3DModelAsync(prompt, request.objectType);
                if (modelData == null) {
                    GameObject fallbackModel = GetFallbackModel(request.objectType);
                    if (fallbackModel != null) {
                        debugger.LogWarning($"Using fallback model for '{request.objectType}' (API failure)", LogCategory.Models);
                        TrackModelGeneration(request.objectType);
                        _modelCache[modelId] = fallbackModel;
                        
                        if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrainForFallback)) {
                            return PlaceModelOnTerrain(
                                fallbackModel,
                                request.position,
                                request.rotation,
                                request.scale,
                                terrainForFallback
                            );
                        }
                        
                        return Instantiate(fallbackModel, _modelContainer.transform);
                    }
                    
                    _failedGenerations++;
                    return null;
                }
                
                UpdateGenerationStatus(modelId, 0.7f, "Instantiating model");
                
                // Create the model GameObject
                GameObject modelObject = await InstantiateModelAsync(modelData, request.position, request.rotation, request.scale, request.targetTerrain);
                if (modelObject == null) {
                    _failedGenerations++;
                    return null;
                }
                
                // Cache the model for reuse
                _modelCache[modelId] = modelObject;
                TrackModelGeneration(request.objectType);
                
                UpdateGenerationStatus(modelId, 0.9f, "Placing on terrain");
                
                // Place on terrain if position is provided
                if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrainForModel)) {
                    GameObject instance = PlaceModelOnTerrain(
                        modelObject,
                        request.position,
                        request.rotation,
                        request.scale,
                        terrainForModel
                    );
                    
                    UpdateGenerationStatus(modelId, 1.0f, "Complete");
                    return instance;
                }
                
                // Otherwise just return the model
                GameObject result = Instantiate(modelObject, _modelContainer.transform);
                UpdateGenerationStatus(modelId, 1.0f, "Complete");
                return result;
            }
            catch (Exception ex) {
                debugger.LogError($"Failed to generate model '{request.objectType}': {ex.Message}", LogCategory.Models);
                UpdateGenerationStatus(modelId, -1f, $"Failed: {ex.Message}");
                _failedGenerations++;
                
                // Try fallback
                GameObject fallback = GetFallbackModel(request.objectType);
                if (fallback != null) {
                    debugger.LogWarning($"Using fallback model for '{request.objectType}' (exception)", LogCategory.Models);
                    
                    if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrain)) {
                        return PlaceModelOnTerrain(
                            fallback,
                            request.position,
                            request.rotation,
                            request.scale,
                            terrain
                        );
                    }
                    
                    return Instantiate(fallback, _modelContainer.transform);
                }
                
                return null;
            }
            finally {
                // Ensure we don't hold up the semaphore with a delay between API calls
                if (apiRateLimitDelay > 0) {
                    await Task.Delay(Mathf.RoundToInt(apiRateLimitDelay * 1000));
                }
            }
        }
        #endregion

        #region ObjectPlacer Integration

        /// <summary>
        /// Model provider for ObjectPlacer that uses generated models
        /// </summary>
        private class GeneratedModelProvider : IModelProvider
        {
            private Dictionary<string, GameObject> _modelsByType;
            private List<MapObject> _mapObjects;

            public GeneratedModelProvider(List<GameObject> models, List<MapObject> mapObjects)
            {
                _modelsByType = new Dictionary<string, GameObject>();
                _mapObjects = mapObjects;

                foreach (var model in models)
                {
                    var modelInfo = model.GetComponent<GeneratedModelInfo>();
                    if (modelInfo != null && !_modelsByType.ContainsKey(modelInfo.objectType))
                    {
                        _modelsByType[modelInfo.objectType] = model;
                    }
                }
            }
            
            /// <summary>
            /// Gets a model for the specified type (implements IModelProvider interface)
            /// </summary>
            public IEnumerator GetModelForType(
                string objectType,
                string description,
                Action<GameObject> onComplete,
                Action<string> onError
            )
            {
                if (_modelsByType.TryGetValue(objectType, out GameObject model))
                {
                    onComplete?.Invoke(model);
                }
                else
                {
                    onError?.Invoke($"No model available for object type: {objectType}");
                }

                yield break; // Return immediately since models are already generated
            }

            // Helper methods for backward compatibility
            public GameObject GetModelForObject(MapObject mapObject)
            {
                return _modelsByType.TryGetValue(mapObject.type, out GameObject model) ? model : null;
            }

            public bool HasModelForType(string objectType)
            {
                return _modelsByType.ContainsKey(objectType);
            }

            public IEnumerable<string> GetAvailableTypes()
            {
                return _modelsByType.Keys;
            }
        }

        /// <summary>
        /// Component to store information about generated models
        /// </summary>
        private class GeneratedModelInfo : MonoBehaviour
        {
            public ObjectGroup sourceGroup;
            public string objectType;
            public string description;
        }

        #endregion

        #region Missing Data Structures and Methods

        // Missing properties and fields
        private string promptTemplate = "Generate a detailed 3D model of {description} suitable for a game environment with realistic proportions and materials.";
        private float[] lodDistances = { 100f, 300f, 600f, 1000f };

        /// <summary>
        /// Component-specific initialization required by TraversifyComponent
        /// </summary>
        protected override bool OnInitialize(object config = null)
        {
            try
            {
                InitializeModelGenerator();
                return true;
            }
            catch (System.Exception ex)
            {
                LogError($"Failed to initialize ModelGenerator: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Data Structures

        /// <summary>
        /// Represents a model generation job for tracking and processing
        /// </summary>
        [System.Serializable]
        public class ModelGenerationJob
        {
            public string jobId;
            public string objectType;
            public string description;
            public Vector3 targetPosition;
            public Quaternion targetRotation;
            public Vector3 targetScale;
            public MapObject sourceMapObject;
            public DetectedObject sourceDetectedObject;
            public float progress;
            public bool isComplete;
            public bool hasFailed;
            public string errorMessage;
            public GameObject resultModel;
            public DateTime startTime;
            public DateTime endTime;
        }

        /// <summary>
        /// Status tracking for model generation
        /// </summary>
        [System.Serializable]
        public class ModelGenerationStatus
        {
            public string ModelId;
            public float Progress;
            public string Status;
            public DateTime StartTime;
            public DateTime LastUpdateTime;
            public DateTime EndTime;
            public bool IsComplete;
            public bool HasError;
            public string ErrorMessage;
        }

        /// <summary>
        /// Configuration for model generation
        /// </summary>
        [System.Serializable]
        public class ModelGenerationConfig
        {
            public bool UseGPUInstancing;
            public bool UsePBRMaterials;
            public bool GenerateTextures;
            public Vector2Int TextureResolution;
            public bool GenerateNormalMaps;
            public bool GenerateMetallicMaps;
            public bool UseLOD;
            public int MaxLODLevels;
            public float LodQualityFactor;
            public bool AdaptToTerrain;
            public bool AvoidCollisions;
            public float MaxPlacementSlope;
            public float ObjectSpacing;
            public bool GroupSimilarObjects;
            public float InstancingSimilarity;
            public SimilarityMetric SimilarityMetric;
            public int MaxGroupSize;
            public float GroundingDepth;
            public bool FixFloatingObjects;
            public bool FixEmbeddedObjects;
        }

        /// <summary>
        /// Request for generating a 3D model from a detected object
        /// </summary>
        [System.Serializable]
        public partial class ModelGenerationRequest
        {
            public string objectType;
            public string description;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public MapObject sourceMapObject;
            public DetectedObject sourceDetectedObject;
            public UnityEngine.Terrain targetTerrain;
            public string requestId;
            public DateTime requestTime;
            public float confidence = 1.0f;
            public bool isGrouped = false;
        }

        /// <summary>
        /// Request for Tripo3D model generation
        /// </summary>
        [System.Serializable]
        public class Tripo3DRequest
        {
            public string prompt;
            public string objectType;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Tripo3DQuality quality;
            public string requestId;
            public DateTime requestTime;
            public UnityEngine.Terrain targetTerrain;
        }

        /// <summary>
        /// Similarity metrics for object grouping
        /// </summary>
        public enum SimilarityMetric
        {
            FeatureVector,
            VisualSimilarity,
            BoundingBoxSize,
            SemanticSimilarity
        }

        /// <summary>
        /// Model instance tracker for performance optimization
        /// </summary>
        public partial class ModelInstanceTracker : MonoBehaviour
        {
            public GameObject originalModel;
            public List<Matrix4x4> instanceMatrices = new List<Matrix4x4>();
            public List<MaterialPropertyBlock> propertyBlocks = new List<MaterialPropertyBlock>();
            public string modelType;
            public int instanceCount;
            public Mesh mesh;
            public Material[] materials;
            public bool supportsInstancing;
            public string sourceType;
            public Vector3 originalPosition;
        }

        #endregion

        #region Missing Method Implementations

        /// <summary>
        /// Public property to access the Tripo3D API key
        /// </summary>
        public string Tripo3DApiKey
        {
            get => tripo3DApiKey;
            set => tripo3DApiKey = value;
        }

        /// <summary>
        /// Creates model generation requests from a group of similar objects
        /// </summary>
        private List<ModelGenerationRequest> CreateRequestsFromGroup(ObjectGroup group, UnityEngine.Terrain terrain)
        {
            var requests = new List<ModelGenerationRequest>();

            if (group.objects == null || group.objects.Count == 0)
                return requests;

            // For grouped objects, we can create a single model and instance it
            var representative = group.objects[0];
            
            var baseRequest = new ModelGenerationRequest
            {
                objectType = representative.type,
                description = representative.enhancedDescription ?? representative.label,
                position = new Vector3(representative.position.x * terrainSize.x, 0, representative.position.y * terrainSize.y),
                rotation = Quaternion.Euler(0, representative.rotation, 0),
                scale = representative.scale,
                sourceMapObject = representative,
                targetTerrain = terrain,
                requestId = System.Guid.NewGuid().ToString(),
                requestTime = System.DateTime.Now
            };

            requests.Add(baseRequest);

            // Add variation requests for diversity
            foreach (var obj in group.objects.Skip(1))
            {
                var request = new ModelGenerationRequest
                {
                    objectType = obj.type,
                    description = obj.enhancedDescription ?? obj.label,
                    position = new Vector3(obj.position.x * terrainSize.x, 0, obj.position.y * terrainSize.y),
                    rotation = Quaternion.Euler(0, obj.rotation, 0),
                    scale = obj.scale,
                    sourceMapObject = obj,
                    targetTerrain = terrain,
                    requestId = System.Guid.NewGuid().ToString(),
                    requestTime = System.DateTime.Now
                };
                requests.Add(request);
            }

            return requests;
        }

        /// <summary>
        /// Groups objects by similarity for instancing
        /// </summary>
        private List<ObjectGroup> GroupObjectsBySimilarity(List<MapObject> objects)
        {
            var groups = new List<ObjectGroup>();

            if (objects == null || objects.Count == 0)
                return groups;

            var remaining = new List<MapObject>(objects);
            int groupId = 0;

            while (remaining.Count > 0)
            {
                var seed = remaining[0];
                remaining.RemoveAt(0);

                var group = new ObjectGroup
                {
                    groupId = $"group_{groupId++}",
                    type = seed.type,
                    objects = new List<MapObject> { seed }
                };

                // Find similar objects
                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    var candidate = remaining[i];
                    float similarity = CalculateObjectSimilarity(seed, candidate);

                    if (similarity >= instancingSimilarity && group.objects.Count < maxGroupSize)
                    {
                        group.objects.Add(candidate);
                        remaining.RemoveAt(i);
                    }
                }

                groups.Add(group);
            }

            debugger.Log($"Grouped {objects.Count} objects into {groups.Count} groups", LogCategory.Models);
            return groups;
        }

        /// <summary>
        /// Calculates similarity between two map objects
        /// </summary>
        private float CalculateObjectSimilarity(MapObject objA, MapObject objB)
        {
            if (objA.type != objB.type)
                return 0f;

            float similarity = 0.5f; // Base similarity for same type

            switch (similarityMetric)
            {
                case SimilarityMetric.BoundingBoxSize:
                    var sizeA = objA.boundingBox.size;
                    var sizeB = objB.boundingBox.size;
                    float sizeSimilarity = 1f - Vector2.Distance(sizeA, sizeB) / Mathf.Max(sizeA.magnitude, sizeB.magnitude);
                    similarity += sizeSimilarity * 0.3f;
                    break;

                case SimilarityMetric.VisualSimilarity:
                    // Compare segmentation colors as a proxy for visual similarity
                    if (objA.segmentColor != Color.clear && objB.segmentColor != Color.clear)
                    {
                        float colorDistance = Vector4.Distance(
                            new Vector4(objA.segmentColor.r, objA.segmentColor.g, objA.segmentColor.b, objA.segmentColor.a),
                            new Vector4(objB.segmentColor.r, objB.segmentColor.g, objB.segmentColor.b, objB.segmentColor.a)
                        );
                        float colorSimilarity = 1f - Mathf.Clamp01(colorDistance / 2f);
                        similarity += colorSimilarity * 0.2f;
                    }
                    break;

                case SimilarityMetric.FeatureVector:
                case SimilarityMetric.SemanticSimilarity:
                default:
                    // Use metadata features if available
                    if (objA.metadata?.ContainsKey("features") == true && objB.metadata?.ContainsKey("features") == true)
                    {
                        var featuresA = objA.metadata["features"] as Dictionary<string, float>;
                        var featuresB = objB.metadata["features"] as Dictionary<string, float>;
                        if (featuresA != null && featuresB != null)
                        {
                            similarity += CalculateFeatureSimilarity(featuresA, featuresB) * 0.3f;
                        }
                    }
                    break;
            }

            return Mathf.Clamp01(similarity);
        }

        /// <summary>
        /// Calculates feature vector similarity
        /// </summary>
        private float CalculateFeatureSimilarity(Dictionary<string, float> featuresA, Dictionary<string, float> featuresB)
        {
            var commonKeys = featuresA.Keys.Intersect(featuresB.Keys).ToList();
            if (commonKeys.Count == 0) return 0f;

            float dotProduct = 0f;
            float magnitudeA = 0f;
            float magnitudeB = 0f;

            foreach (var key in commonKeys)
            {
                float valueA = featuresA[key];
                float valueB = featuresB[key];
                
                dotProduct += valueA * valueB;
                magnitudeA += valueA * valueA;
                magnitudeB += valueB * valueB;
            }

            magnitudeA = Mathf.Sqrt(magnitudeA);
            magnitudeB = Mathf.Sqrt(magnitudeB);

            if (magnitudeA > 0 && magnitudeB > 0)
            {
                return dotProduct / (magnitudeA * magnitudeB);
            }

            return 0f;
        }

        /// <summary>
        /// Creates a model generation request from a single map object
        /// </summary>
        private ModelGenerationRequest CreateRequestFromMapObject(MapObject mapObject, UnityEngine.Terrain terrain)
        {
            return new ModelGenerationRequest
            {
                objectType = mapObject.type,
                description = mapObject.enhancedDescription ?? mapObject.label,
                position = new Vector3(mapObject.position.x * terrainSize.x, 0, mapObject.position.y * terrainSize.y),
                rotation = Quaternion.Euler(0, mapObject.rotation, 0),
                scale = mapObject.scale,
                sourceMapObject = mapObject,
                targetTerrain = terrain,
                requestId = System.Guid.NewGuid().ToString(),
                requestTime = System.DateTime.Now
            };
        }

        /// <summary>
        /// Combines similar meshes for performance optimization
        /// </summary>
        private List<GameObject> CombineSimilarMeshes(List<GameObject> models)
        {
            var combinedModels = new List<GameObject>();
            var meshGroups = new Dictionary<string, List<GameObject>>();

            // Group models by type
            foreach (var model in models)
            {
                var modelInfo = model.GetComponent<GeneratedModelInfo>();
                string key = modelInfo?.objectType ?? "unknown";
                
                if (!meshGroups.ContainsKey(key))
                    meshGroups[key] = new List<GameObject>();
                
                meshGroups[key].Add(model);
            }

            // Combine meshes within each group
            foreach (var group in meshGroups)
            {
                if (group.Value.Count > 1 && useMeshCombining)
                {
                    var combined = CombineMeshGroup(group.Value, group.Key);
                    if (combined != null)
                        combinedModels.Add(combined);
                }
                else
                {
                    combinedModels.AddRange(group.Value);
                }
            }

            return combinedModels;
        }

        /// <summary>
        /// Combines a group of similar meshes
        /// </summary>
        private GameObject CombineMeshGroup(List<GameObject> models, string groupType)
        {
            try
            {
                var combines = new List<CombineInstance>();
                var materials = new List<Material>();

                foreach (var model in models)
                {
                    var meshRenderers = model.GetComponentsInChildren<MeshRenderer>();
                    foreach (var renderer in meshRenderers)
                    {
                        var modelMeshFilter = renderer.GetComponent<MeshFilter>();
                        if (modelMeshFilter?.sharedMesh != null)
                        {
                            var combine = new CombineInstance();
                            combine.mesh = modelMeshFilter.sharedMesh;
                            combine.transform = renderer.transform.localToWorldMatrix;
                            combines.Add(combine);

                            if (renderer.sharedMaterial != null && !materials.Contains(renderer.sharedMaterial))
                                materials.Add(renderer.sharedMaterial);
                        }
                    }
                }

                if (combines.Count == 0) return null;

                // Create combined mesh
                var combinedMesh = new Mesh();
                combinedMesh.CombineMeshes(combines.ToArray());
                
                // Check vertex limit
                if (combinedMesh.vertexCount > maxVerticesPerMesh)
                {
                    DestroyImmediate(combinedMesh);
                    return null; // Too many vertices, keep separate
                }

                // Create combined game object
                var combinedObject = new GameObject($"Combined_{groupType}");
                combinedObject.transform.SetParent(_modelContainer.transform);

                var combinedMeshFilter = combinedObject.AddComponent<MeshFilter>();
                var meshRenderer = combinedObject.AddComponent<MeshRenderer>();

                combinedMeshFilter.mesh = combinedMesh;
                meshRenderer.materials = materials.ToArray();

                var modelInfo = combinedObject.AddComponent<GeneratedModelInfo>();
                modelInfo.objectType = groupType;
                modelInfo.description = $"Combined mesh of {models.Count} {groupType} objects";

                // Destroy original models
                foreach (var model in models)
                {
                    DestroyImmediate(model);
                }

                return combinedObject;
            }
            catch (System.Exception ex)
            {
                debugger.LogError($"Error combining meshes: {ex.Message}", LogCategory.Models);
                return null;
            }
        }

        /// <summary>
        /// Logs model distribution statistics
        /// </summary>
        private void LogModelDistribution(List<GameObject> models)
        {
            var distribution = new Dictionary<string, int>();

            foreach (var model in models)
            {
                var modelInfo = model.GetComponent<GeneratedModelInfo>();
                string type = modelInfo?.objectType ?? "unknown";
                
                if (!distribution.ContainsKey(type))
                    distribution[type] = 0;
                distribution[type]++;
            }

            debugger.Log("Model Distribution:", LogCategory.Models);
            foreach (var kvp in distribution.OrderByDescending(x => x.Value))
            {
                debugger.Log($"  {kvp.Key}: {kvp.Value} models", LogCategory.Models);
            }
        }

        /// <summary>
        /// Gets generation progress for a specific model
        /// </summary>
        private float GetGenerationProgress(string modelId)
        {
            if (_generationStatus.TryGetValue(modelId, out var status))
            {
                return status.Progress;
            }
            return 0f;
        }

        /// <summary>
        /// Adapts transform to terrain height and slope
        /// </summary>
        private void AdaptTransformToTerrain(Transform transform, UnityEngine.Terrain terrain)
        {
            if (!adaptToTerrain || terrain == null) return;

            Vector3 worldPos = transform.position;
            float terrainHeight = terrain.SampleHeight(worldPos);
            
            // Adjust Y position
            worldPos.y = terrainHeight - groundingDepth;
            transform.position = worldPos;

            // Calculate terrain normal for rotation adaptation
            Vector3 terrainNormal = terrain.terrainData.GetInterpolatedNormal(
                worldPos.x / terrain.terrainData.size.x,
                worldPos.z / terrain.terrainData.size.z
            );

            // Calculate slope angle
            float slopeAngle = Vector3.Angle(Vector3.up, terrainNormal);
            
            // Only place if slope is acceptable
            if (slopeAngle <= maxPlacementSlope)
            {
                // Align to terrain normal
                Quaternion terrainRotation = Quaternion.FromToRotation(Vector3.up, terrainNormal);
                transform.rotation = terrainRotation * transform.rotation;
            }
        }

        /// <summary>
        /// Generates a unique model ID
        /// </summary>
        private string GenerateModelId()
        {
            return $"model_{System.Guid.NewGuid().ToString("N")[..8]}_{System.DateTime.Now.Ticks}";
        }

        /// <summary>
        /// Overloaded method to generate model ID with parameters
        /// </summary>
        private string GenerateModelId(string objectType, string description)
        {
            string hash = $"{objectType}_{description}".GetHashCode().ToString("X");
            return $"model_{objectType}_{hash}_{System.DateTime.Now.Ticks}";
        }

        /// <summary>
        /// Tracks model generation for metrics
        /// </summary>
        private void TrackModelGeneration(string modelId, string objectType, DateTime startTime)
        {
            var status = new ModelGenerationStatus
            {
                ModelId = modelId,
                Progress = 0f,
                Status = "Starting",
                StartTime = startTime,
                LastUpdateTime = startTime,
                IsComplete = false,
                HasError = false
            };

            _generationStatus[modelId] = status;
            
            if (!_modelTypeDistribution.ContainsKey(objectType))
                _modelTypeDistribution[objectType] = 0;
            _modelTypeDistribution[objectType]++;
        }

        /// <summary>
        /// Overloaded method to track model generation with just object type
        /// </summary>
        private void TrackModelGeneration(string objectType)
        {
            _totalModelsGenerated++;
            if (!_modelTypeDistribution.ContainsKey(objectType))
                _modelTypeDistribution[objectType] = 0;
            _modelTypeDistribution[objectType]++;
        }

        /// <summary>
        /// Updates generation status for a model
        /// </summary>
        private void UpdateGenerationStatus(string modelId, float progress, string status)
        {
            if (_generationStatus.TryGetValue(modelId, out var genStatus))
            {
                genStatus.Progress = progress;
                genStatus.Status = status;
                genStatus.LastUpdateTime = System.DateTime.Now;
                
                if (progress >= 1f || progress < 0f)
                {
                    genStatus.IsComplete = true;
                    genStatus.EndTime = System.DateTime.Now;
                    
                    if (progress < 0f)
                    {
                        genStatus.HasError = true;
                        genStatus.ErrorMessage = status;
                    }
                }
            }
        }

        /// <summary>
        /// Enhances description using OpenAI API
        /// </summary>
        private async Task<string> EnhanceDescriptionAsync(string baseDescription, string objectType)
        {
            if (string.IsNullOrEmpty(openAIApiKey)) return baseDescription;

            try
            {
                string prompt = $"Enhance this 3D model description for game asset generation: '{baseDescription}' for object type '{objectType}'. Focus on visual details, materials, and proportions. Keep under 100 words.";
                
                // Use OpenAI API (simplified implementation)
                string enhancedDescription = await CallOpenAIAPI(prompt);
                return string.IsNullOrEmpty(enhancedDescription) ? baseDescription : enhancedDescription;
            }
            catch (System.Exception ex)
            {
                debugger.LogWarning($"Failed to enhance description: {ex.Message}", LogCategory.API);
                return baseDescription;
            }
        }

        /// <summary>
        /// Simple OpenAI API call implementation
        /// </summary>
        private async Task<string> CallOpenAIAPI(string prompt)
        {
            // This is a simplified implementation - in practice you'd use a proper OpenAI client
            await Task.Delay(100); // Simulate API call
            return prompt; // Return original for now
        }

        /// <summary>
        /// Formats prompt for model generation
        /// </summary>
        private string FormatPrompt(string description, string objectType)
        {
            return promptTemplate
                .Replace("{description}", description)
                .Replace("{objectType}", objectType)
                .Replace("{timestamp}", System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        /// <summary>
        /// Gets fallback model for object type with proper materials applied
        /// </summary>
        private GameObject GetFallbackModel(string objectType)
        {
            try
            {
                string lowerType = objectType.ToLowerInvariant();
                string fallbackName = "default";

                // Map object types to fallback models
                if (lowerType.Contains("building") || lowerType.Contains("house"))
                    fallbackName = "building";
                else if (lowerType.Contains("tree") || lowerType.Contains("vegetation"))
                    fallbackName = "tree";
                else if (lowerType.Contains("vehicle") || lowerType.Contains("car"))
                    fallbackName = "vehicle";
                else if (lowerType.Contains("rock") || lowerType.Contains("stone"))
                    fallbackName = "rock";

                string resourcePath = $"{fallbackModelPath}/{fallbackName}";
                GameObject fallback = Resources.Load<GameObject>(resourcePath);
                
                if (fallback != null)
                {
                    debugger.Log($"Using fallback model '{fallbackName}' for '{objectType}'", LogCategory.Models);
                    GameObject instance = Instantiate(fallback);
                    instance.name = $"Fallback_{objectType}";
                    
                    // Ensure proper materials are applied
                    ApplyMaterialsToGameObject(instance, objectType);
                    return instance;
                }

                // Create a simple cube as ultimate fallback
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Fallback_{objectType}";
                
                // Always apply proper materials to prevent neon pink appearance
                ApplyMaterialsToGameObject(cube, objectType);
                
                debugger.Log($"Created fallback cube for '{objectType}' with proper materials", LogCategory.Models);
                return cube;
            }
            catch (System.Exception ex)
            {
                debugger.LogError($"Error creating fallback model: {ex.Message}", LogCategory.Models);
                
                // Emergency fallback with guaranteed material
                GameObject emergencyCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                emergencyCube.name = $"Emergency_{objectType}";
                
                // Apply basic material to prevent neon pink
                Renderer renderer = emergencyCube.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material safeMaterial = new Material(Shader.Find("Standard"));
                    safeMaterial.color = GetObjectTypeColor(objectType);
                    safeMaterial.name = $"Emergency_{objectType}_Material";
                    renderer.material = safeMaterial;
                }
                
                return emergencyCube;
            }
        }

        /// <summary>
        /// Finds terrain for object placement
        /// </summary>
        private bool FindTerrain(out UnityEngine.Terrain terrain)
        {
            terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null) return true;

            // Find any terrain in scene
            terrain = FindObjectOfType<UnityEngine.Terrain>();
            return terrain != null;
        }

        /// <summary>
        /// Requests model generation from Tripo3D API
        /// </summary>
        private async Task<GameObject> RequestTripo3DModelAsync(string prompt, string objectType)
        {
            try
            {
                debugger.Log($"Requesting Tripo3D model for: {objectType}", LogCategory.API);
                
                // Simulate API call - in practice, you'd implement actual Tripo3D API integration
                await Task.Delay(UnityEngine.Random.Range(2000, 5000));
                
                // For now, return a fallback
                return GetFallbackModel(objectType);
            }
            catch (System.Exception ex)
            {
                debugger.LogError($"Tripo3D API error: {ex.Message}", LogCategory.API);
                return null;
            }
        }

        /// <summary>
        /// Instantiates and places model on terrain
        /// </summary>
        private async Task<GameObject> InstantiateModelAsync(GameObject modelPrefab, Vector3 position, Quaternion rotation, Vector3 scale, UnityEngine.Terrain terrain)
        {
            if (modelPrefab == null) return null;

            try
            {
                GameObject instance = Instantiate(modelPrefab, _modelContainer.transform);
                instance.transform.position = position;
                instance.transform.rotation = rotation;
                instance.transform.localScale = scale;

                // Adapt to terrain
                AdaptTransformToTerrain(instance.transform, terrain);

                // Add model info component
                var modelInfo = instance.GetComponent<GeneratedModelInfo>();
                if (modelInfo == null)
                {
                    modelInfo = instance.AddComponent<GeneratedModelInfo>();
                }

                await Task.Yield();
                return instance;
            }
            catch (System.Exception ex)
            {
                debugger.LogError($"Error instantiating model: {ex.Message}", LogCategory.Models);
                return null;
            }
        }

        /// <summary>
        /// Overloaded AdaptTransformToTerrain that returns adapted transform
        /// </summary>
        private PlacementTransform AdaptTransformToTerrain(PlacementTransform originalTransform, GameObject modelObject, UnityEngine.Terrain terrain)
        {
            if (!adaptToTerrain || terrain == null) return originalTransform;

            PlacementTransform adaptedTransform = originalTransform;
            Vector3 worldPos = originalTransform.Position;
            float terrainHeight = terrain.SampleHeight(worldPos);
            
            // Adjust Y position
            worldPos.y = terrainHeight - groundingDepth;
            adaptedTransform.Position = worldPos;

            // Calculate terrain normal for rotation adaptation
            Vector3 terrainNormal = terrain.terrainData.GetInterpolatedNormal(
                worldPos.x / terrain.terrainData.size.x,
                worldPos.z / terrain.terrainData.size.z
            );

            // Calculate slope angle
            float slopeAngle = Vector3.Angle(Vector3.up, terrainNormal);
            
            // Only place if slope is acceptable
            if (slopeAngle <= maxPlacementSlope)
            {
                // Align to terrain normal
                Quaternion terrainRotation = Quaternion.FromToRotation(Vector3.up, terrainNormal);
                adaptedTransform.Rotation = terrainRotation * originalTransform.Rotation;
            }

            return adaptedTransform;
        }

        /// <summary>
        /// Missing data structure for 3D model data
        /// </summary>
        [System.Serializable]
        public class Model3DData
        {
            public string modelId;
            public byte[] meshData;
            public byte[] textureData;
            public string format; // "obj", "fbx", "gltf", etc.
            public Dictionary<string, object> metadata;
        }

        /// <summary>
        /// Missing data structure for placement transform
        /// </summary>
        [System.Serializable]
        public struct PlacementTransform
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        #endregion

        #region Neon Pink Material Fix

        /// <summary>
        /// Creates a default material for an object type with proper visual appearance
        /// </summary>
        private Material CreateDefaultMaterialForObject(string objectType, Color? baseColor = null)
        {
            try
            {
                // Use existing default material if available and valid
                if (defaultMaterial != null)
                {
                    // Create a copy to avoid modifying the original
                    Material materialCopy = new Material(defaultMaterial);
                    materialCopy.name = $"Generated_{objectType}_Material";
                    
                    // Apply object-specific colors if available
                    if (baseColor.HasValue)
                    {
                        materialCopy.color = baseColor.Value;
                        if (materialCopy.HasProperty("_BaseColor"))
                            materialCopy.SetColor("_BaseColor", baseColor.Value);
                        if (materialCopy.HasProperty("_Color"))
                            materialCopy.SetColor("_Color", baseColor.Value);
                    }
                    
                    return materialCopy;
                }
                
                // Create a new standard material if no default exists
                Material newMaterial;
                
                // Try to use URP/HDRP materials if available, otherwise use Standard
                if (generatePBRMaterials)
                {
                    // Try Universal Render Pipeline material first
                    Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
                    if (urpShader != null)
                    {
                        newMaterial = new Material(urpShader);
                    }
                    else
                    {
                        // Fall back to HDRP if available
                        Shader hdrpShader = Shader.Find("HDRP/Lit");
                        if (hdrpShader != null)
                        {
                            newMaterial = new Material(hdrpShader);
                        }
                        else
                        {
                            // Fall back to Standard shader
                            newMaterial = new Material(Shader.Find("Standard"));
                        }
                    }
                }
                else
                {
                    // Use simple Standard shader for better compatibility
                    newMaterial = new Material(Shader.Find("Standard"));
                }
                
                newMaterial.name = $"Generated_{objectType}_Material";
                
                // Set object-type-specific material properties
                Color objectColor = GetObjectTypeColor(objectType);
                if (baseColor.HasValue)
                    objectColor = baseColor.Value;
                
                // Apply material properties based on shader type
                if (newMaterial.HasProperty("_BaseColor"))
                {
                    newMaterial.SetColor("_BaseColor", objectColor);
                }
                else if (newMaterial.HasProperty("_Color"))
                {
                    newMaterial.SetColor("_Color", objectColor);
                }
                
                // Set standard properties for better appearance
                if (newMaterial.HasProperty("_Metallic"))
                {
                    newMaterial.SetFloat("_Metallic", GetMetallicValueForObjectType(objectType));
                }
                
                if (newMaterial.HasProperty("_Smoothness"))
                {
                    newMaterial.SetFloat("_Smoothness", GetSmoothnessValueForObjectType(objectType));
                }
                else if (newMaterial.HasProperty("_Glossiness"))
                {
                    newMaterial.SetFloat("_Glossiness", GetSmoothnessValueForObjectType(objectType));
                }
                
                // Enable GPU instancing if supported
                newMaterial.enableInstancing = true;
                
                debugger.Log($"Created material '{newMaterial.name}' with shader '{newMaterial.shader.name}'", LogCategory.Models);
                return newMaterial;
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error creating material for {objectType}: {ex.Message}", LogCategory.Models);
                
                // Ultimate fallback - create the simplest possible material
                Material fallbackMaterial = new Material(Shader.Find("Standard"));
                fallbackMaterial.name = $"Fallback_{objectType}_Material";
                fallbackMaterial.color = Color.gray;
                return fallbackMaterial;
            }
        }
        
        /// <summary>
        /// Gets appropriate color for different object types
        /// </summary>
        private Color GetObjectTypeColor(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "building":
                case "house":
                case "structure":
                    return new Color(0.8f, 0.75f, 0.7f, 1f); // Light gray/beige
                    
                case "tree":
                case "forest":
                    return new Color(0.2f, 0.6f, 0.2f, 1f); // Green
                    
                case "road":
                case "path":
                    return new Color(0.3f, 0.3f, 0.3f, 1f); // Dark gray
                    
                case "water":
                case "lake":
                case "river":
                case "ocean":
                    return new Color(0.2f, 0.4f, 0.8f, 1f); // Blue
                    
                case "mountain":
                case "hill":
                case "rock":
                    return new Color(0.5f, 0.45f, 0.4f, 1f); // Brown/gray
                    
                case "field":
                case "grass":
                case "grassland":
                    return new Color(0.4f, 0.7f, 0.3f, 1f); // Light green
                    
                case "vehicle":
                case "car":
                case "truck":
                    return new Color(0.7f, 0.2f, 0.2f, 1f); // Red
                    
                case "bridge":
                    return new Color(0.6f, 0.6f, 0.6f, 1f); // Medium gray
                    
                default:
                    return new Color(0.7f, 0.7f, 0.7f, 1f); // Default light gray
            }
        }
        
        /// <summary>
        /// Gets appropriate metallic value for object types
        /// </summary>
        private float GetMetallicValueForObjectType(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "vehicle":
                case "car":
                case "truck":
                case "bridge":
                    return 0.8f; // Metallic vehicles and infrastructure
                    
                case "building":
                case "house":
                case "structure":
                    return 0.1f; // Slightly metallic for modern buildings
                    
                case "water":
                case "lake":
                case "river":
                case "ocean":
                    return 0.9f; // Water has metallic-like reflections
                    
                default:
                    return 0.0f; // Non-metallic by default
            }
        }
        
        /// <summary>
        /// Gets appropriate smoothness/glossiness value for object types
        /// </summary>
        private float GetSmoothnessValueForObjectType(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "water":
                case "lake":
                case "river":
                case "ocean":
                    return 0.95f; // Very smooth water
                    
                case "vehicle":
                case "car":
                case "truck":
                    return 0.7f; // Glossy vehicles
                    
                case "building":
                case "house":
                case "structure":
                    return 0.3f; // Somewhat smooth buildings
                    
                case "road":
                case "path":
                    return 0.2f; // Slightly rough roads
                    
                case "tree":
                case "forest":
                case "field":
                case "grass":
                case "grassland":
                    return 0.1f; // Rough natural materials
                    
                case "mountain":
                case "hill":
                case "rock":
                    return 0.05f; // Very rough terrain
                    
                default:
                    return 0.4f; // Medium smoothness by default
            }
        }
        
        /// <summary>
        /// Applies proper materials to all renderers in a GameObject hierarchy
        /// </summary>
        private void ApplyMaterialsToGameObject(GameObject gameObject, string objectType)
        {
            try
            {
                // Get all renderers in the object and its children
                Renderer[] renderers = gameObject.GetComponentsInChildren<Renderer>(true);
                
                foreach (Renderer renderer in renderers)
                {
                    // Skip if renderer is null or already has valid materials
                    if (renderer == null) continue;
                    
                    // Check if renderer has valid materials
                    bool needsMaterial = false;
                    if (renderer.materials == null || renderer.materials.Length == 0)
                    {
                        needsMaterial = true;
                    }
                    else
                    {
                        // Check if any materials are null or using missing shader
                        for (int i = 0; i < renderer.materials.Length; i++)
                        {
                            Material mat = renderer.materials[i];
                            if (mat == null || mat.shader == null || mat.shader.name.Contains("Hidden/InternalErrorShader"))
                            {
                                needsMaterial = true;
                                break;
                            }
                        }
                    }
                    
                    if (needsMaterial)
                    {
                        // Create and assign new material
                        Material newMaterial = CreateDefaultMaterialForObject(objectType);
                        Material[] materials = new Material[Mathf.Max(1, renderer.materials.Length)];
                        
                        // Fill all material slots with the new material
                        for (int i = 0; i < materials.Length; i++)
                        {
                            materials[i] = newMaterial;
                        }
                        
                        renderer.materials = materials;
                        debugger.Log($"Applied material '{newMaterial.name}' to renderer on '{renderer.gameObject.name}'", LogCategory.Models);
                    }
                }
                
                debugger.Log($"Applied materials to {renderers.Length} renderers in '{gameObject.name}'", LogCategory.Models);
            }
            catch (Exception ex)
            {
                debugger.LogError($"Error applying materials to {gameObject.name}: {ex.Message}", LogCategory.Models);
            }
        }
        #endregion
    }

    // Fallback Tripo3D integration namespace - moved outside of ModelGenerator class
    namespace TripoForUnity
    {
        public enum ModelQuality
        {
            Draft,
            Standard,
            High,
            Ultra
        }
        
        public class TripoSDK : MonoBehaviour
        {
            public void GenerateModelFromText(
                string description, 
                ModelQuality quality,
                System.Action<GameObject> onSuccess,
                System.Action<string> onError)
            {
                // Fallback implementation - would be replaced with actual Tripo3D SDK
                StartCoroutine(MockTripoGeneration(description, quality, onSuccess, onError));
            }
            
            private IEnumerator MockTripoGeneration(
                string description,
                ModelQuality quality,
                System.Action<GameObject> onSuccess,
                System.Action<string> onError)
            {
                // Simulate generation time
                yield return new WaitForSeconds(UnityEngine.Random.Range(2f, 5f));
                
                // Create a simple mock model
                GameObject mockModel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mockModel.name = $"Mock_{description.Substring(0, Mathf.Min(10, description.Length))}";
                
                onSuccess?.Invoke(mockModel);
            }
        }
    }
}
