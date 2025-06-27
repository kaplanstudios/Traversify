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
        [Range(1, 16)] public int maxConcurrentRequests = 4;
        
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
        
        [Tooltip("Tripo3D API key for model generation")]
        public string tripo3DApiKey;
        
        [Tooltip("Prompt enhancement template")]
        [TextArea(3, 10)]
        public string promptTemplate = "Generate a detailed 3D model of {description} suitable for a game environment. The model should be low-poly with clean topology and UV mapping.";
        
        [Tooltip("Fallback model library path")]
        public string fallbackModelPath = "Models/Fallbacks";
        
        [Tooltip("Use cached models when available")]
        public bool useCachedModels = true;
        
        [Tooltip("Maximum age of cached models (days)")]
        public int cacheMaxAgeDays = 30;

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
        
        // Metrics
        private int _totalModelsGenerated = 0;
        private int _totalInstancesCreated = 0;
        private int _failedGenerations = 0;
        private float _averageGenerationTime = 0;
        private Dictionary<string, int> _modelTypeDistribution = new Dictionary<string, int>();
        
        // State
        private bool _isProcessing = false;
        private bool _isInitialized = false;
        private int _activeRequests = 0;
        
        // Configuration 
        private ModelGenerationConfig _generationConfig;
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
            
            debugger.StartTimer("ModelGeneration");
            _isProcessing = true;
            
            try {
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
                
                var createdObjects = new List<GameObject>();
                var processingTasks = new List<Task>();
                int completed = 0;
                
                // Start processing the queue
                while (_generationQueue.Count > 0 || _activeRequests > 0) {
                    // Process new requests if slots available
                    while (_generationQueue.Count > 0 && _activeRequests < maxConcurrentRequests) {
                        var request = _generationQueue.Dequeue();
                        
                        // Start model generation with callback to track completion
                        _activeRequests++;
                        
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
                                
                                completed++;
                                onProgress?.Invoke(completed, total);
                            }
                            catch (OperationCanceledException) {
                                // Shutdown requested, exit gracefully
                            }
                            catch (Exception ex) {
                                debugger.LogError($"Error generating model: {ex.Message}", LogCategory.Models);
                            }
                            finally {
                                _activeRequests--;
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
                while (_activeRequests > 0) {
                    yield return new WaitForSeconds(0.1f);
                }
                
                // Apply final optimizations to models
                if (useMeshCombining) {
                    yield return StartCoroutine(CombineSimilarMeshes(createdObjects));
                }
                
                // Report performance metrics
                float generationTime = debugger.StopTimer("ModelGeneration");
                debugger.Log($"Model generation completed in {generationTime:F2}s - " +
                             $"Generated {createdObjects.Count} models ({_totalInstancesCreated} instances)", 
                             LogCategory.Models);
                
                LogModelDistribution();
                
                // Return all created objects
                onComplete?.Invoke(createdObjects);
            }
            catch (Exception ex) {
                debugger.LogError($"GenerateAndPlaceModels failed: {ex.Message}", LogCategory.Models);
                onError?.Invoke(ex.Message);
            }
            finally {
                _isProcessing = false;
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
                    enhancedDescription = await EnhanceDescriptionAsync(request.description);
                }
                
                // Format prompt with template
                string prompt = FormatPrompt(enhancedDescription);
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
                        if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrain)) {
                            return PlaceModelOnTerrain(
                                fallbackModel,
                                request.position,
                                request.rotation,
                                request.scale,
                                terrain
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
                Model3DData modelData = await RequestTripo3DModelAsync(prompt, modelId);
                if (modelData == null) {
                    GameObject fallbackModel = GetFallbackModel(request.objectType);
                    if (fallbackModel != null) {
                        debugger.LogWarning($"Using fallback model for '{request.objectType}' (API failure)", LogCategory.Models);
                        TrackModelGeneration(request.objectType);
                        _modelCache[modelId] = fallbackModel;
                        
                        if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrain)) {
                            return PlaceModelOnTerrain(
                                fallbackModel,
                                request.position,
                                request.rotation,
                                request.scale,
                                terrain
                            );
                        }
                        
                        return Instantiate(fallbackModel, _modelContainer.transform);
                    }
                    
                    _failedGenerations++;
                    return null;
                }
                
                UpdateGenerationStatus(modelId, 0.7f, "Instantiating model");
                
                // Create the model GameObject
                GameObject modelObject = await InstantiateModelAsync(modelData, request.objectType);
                if (modelObject == null) {
                    _failedGenerations++;
                    return null;
                }
                
                // Cache the model for reuse
                _modelCache[modelId] = modelObject;
                TrackModelGeneration(request.objectType);
                
                UpdateGenerationStatus(modelId, 0.9f, "Placing on terrain");
                
                // Place on terrain if position is provided
                if (request.position != Vector3.zero && FindTerrain(out UnityEngine.Terrain terrain)) {
                    GameObject instance = PlaceModelOnTerrain(
                        modelObject,
                        request.position,
                        request.rotation,
                        request.scale,
                        terrain
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
        
        private async Task<Model3DData> RequestTripo3DModelAsync(string prompt, string modelId) {
            string requestUrl = "https://api.tripo3d.ai/v2/models/generate";
            
            // Prepare request data
            var requestData = new {
                prompt = prompt,
                format = "obj",
                resolution = "high",
                animation = false,
                textures = generateTextures,
                optimization = "game_engine",
                normal_maps = generateNormalMaps,
                metallic_maps = generateMetallicMaps,
                model_id = modelId
            };
            
            string jsonData = JsonUtility.ToJson(requestData);
            
            using (UnityWebRequest request = new UnityWebRequest(requestUrl, "POST")) {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {tripo3DApiKey}");
                request.timeout = Mathf.RoundToInt(generationTimeout);
                
                // Start the request
                var asyncOperation = request.SendWebRequest();
                
                // Update progress while waiting
                while (!asyncOperation.isDone) {
                    UpdateGenerationStatus(modelId, 0.2f + asyncOperation.progress * 0.3f, "Generating with Tripo3D");
                    await Task.Delay(100);
                    
                    if (_cancellationTokenSource.Token.IsCancellationRequested) {
                        request.Abort();
                        throw new OperationCanceledException();
                    }
                }
                
                if (request.result != UnityWebRequest.Result.Success) {
                    debugger.LogError($"Tripo3D API Error: {request.error}", LogCategory.Models);
                    return null;
                }
                
                string responseJson = request.downloadHandler.text;
                UpdateGenerationStatus(modelId, 0.5f, "Processing response");
                
                // Parse the response
                try {
                    var response = JsonUtility.FromJson<Tripo3DResponse>(responseJson);
                    if (response?.modelData == null) {
                        debugger.LogError("Invalid response from Tripo3D API", LogCategory.Models);
                        return null;
                    }
                    
                    UpdateGenerationStatus(modelId, 0.6f, "Downloading model");
                    
                    // Download the model
                    using (UnityWebRequest modelRequest = UnityWebRequest.Get(response.modelData.downloadUrl)) {
                        asyncOperation = modelRequest.SendWebRequest();
                        
                        // Update progress during download
                        while (!asyncOperation.isDone) {
                            UpdateGenerationStatus(modelId, 0.6f + asyncOperation.progress * 0.1f, "Downloading model");
                            await Task.Delay(100);
                            
                            if (_cancellationTokenSource.Token.IsCancellationRequested) {
                                modelRequest.Abort();
                                throw new OperationCanceledException();
                            }
                        }
                        
                        if (modelRequest.result != UnityWebRequest.Result.Success) {
                            debugger.LogError($"Failed to download model: {modelRequest.error}", LogCategory.Models);
                            return null;
                        }
                        
                        // Parse the model data
                        return ParseModelData(modelRequest.downloadHandler.data, response.modelData);
                    }
                }
                catch (Exception ex) {
                    debugger.LogError($"Failed to parse Tripo3D response: {ex.Message}", LogCategory.Models);
                    return null;
                }
            }
        }
        
        private Model3DData ParseModelData(byte[] modelData, Tripo3DModelData responseData) {
            // Parse the OBJ data to create a Model3DData object
            // This is a simplified version - a full implementation would properly parse OBJ format
            
            // Create a placeholder model data structure
            var result = new Model3DData {
                name = responseData.name ?? "GeneratedModel",
                metadata = new Dictionary<string, object> {
                    { "source", "Tripo3D" },
                    { "timestamp", DateTime.UtcNow.ToString("o") },
                    { "format", responseData.format },
                    { "model_id", responseData.id }
                }
            };
            
            // Parse the OBJ data (simplified)
            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Vector3> normals = new List<Vector3>();
            List<int> triangles = new List<int>();
            
            string objData = System.Text.Encoding.UTF8.GetString(modelData);
            string[] lines = objData.Split('\n');
            
            Dictionary<string, int> vertexIndices = new Dictionary<string, int>();
            int currentVertexIndex = 0;
            
            foreach (string line in lines) {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#")) continue;
                
                string[] parts = trimmedLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                
                switch (parts[0]) {
                    case "v":
                        if (parts.Length >= 4) {
                            float x = float.Parse(parts[1]);
                            float y = float.Parse(parts[2]);
                            float z = float.Parse(parts[3]);
                            vertices.Add(new Vector3(x, y, z));
                        }
                        break;
                    
                    case "vt":
                        if (parts.Length >= 3) {
                            float u = float.Parse(parts[1]);
                            float v = float.Parse(parts[2]);
                            uvs.Add(new Vector2(u, v));
                        }
                        break;
                    
                    case "vn":
                        if (parts.Length >= 4) {
                            float x = float.Parse(parts[1]);
                            float y = float.Parse(parts[2]);
                            float z = float.Parse(parts[3]);
                            normals.Add(new Vector3(x, y, z));
                        }
                        break;
                    
                    case "f":
                        if (parts.Length >= 4) {
                            // Parse triangle or quad face
                            for (int i = 1; i < parts.Length - 1; i++) {
                                // Add a triangle (1, i+1, i+2)
                                string v1 = parts[1];
                                string v2 = parts[i+1];
                                string v3 = parts[i+2];
                                
                                if (!vertexIndices.TryGetValue(v1, out int idx1)) {
                                    idx1 = currentVertexIndex++;
                                    vertexIndices[v1] = idx1;
                                }
                                
                                if (!vertexIndices.TryGetValue(v2, out int idx2)) {
                                    idx2 = currentVertexIndex++;
                                    vertexIndices[v2] = idx2;
                                }
                                
                                if (!vertexIndices.TryGetValue(v3, out int idx3)) {
                                    idx3 = currentVertexIndex++;
                                    vertexIndices[v3] = idx3;
                                }
                                
                                triangles.Add(idx1);
                                triangles.Add(idx2);
                                triangles.Add(idx3);
                            }
                        }
                        break;
                }
            }
            
            // Assign parsed data to model
            result.vertices = vertices.ToArray();
            result.triangles = triangles.ToArray();
            result.normals = normals.ToArray();
            result.uvs = uvs.ToArray();
            
            return result;
        }
        
        private async Task<GameObject> InstantiateModelAsync(Model3DData modelData, string objectType) {
            try {
                // Create parent GameObject
                GameObject modelObject = new GameObject(objectType);
                
                // Create the mesh
                Mesh mesh = new Mesh();
                mesh.name = $"{objectType}_Mesh";
                
                // Set mesh data
                mesh.vertices = modelData.vertices;
                mesh.triangles = modelData.triangles;
                
                if (modelData.normals != null && modelData.normals.Length > 0) {
                    mesh.normals = modelData.normals;
                } else {
                    mesh.RecalculateNormals();
                }
                
                if (modelData.uvs != null && modelData.uvs.Length > 0) {
                    mesh.uv = modelData.uvs;
                }
                
                if (modelData.colors != null && modelData.colors.Length == modelData.vertices.Length) {
                    mesh.colors = modelData.colors;
                }
                
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                
                // Create LOD levels if enabled
                if (useLOD && maxLODLevels > 1) {
                    await CreateLODLevelsAsync(modelObject, mesh, objectType);
                } else {
                    // Add single mesh to object
                    var mf = modelObject.AddComponent<MeshFilter>();
                    var mr = modelObject.AddComponent<MeshRenderer>();
                    mf.sharedMesh = mesh;
                    
                    // Create or assign material
                    Material material = null;
                    
                    if (modelData.material != null) {
                        material = modelData.material;
                    } else if (defaultMaterial != null) {
                        material = new Material(defaultMaterial);
                        material.name = $"{objectType}_Material";
                    } else {
                        material = new Material(Shader.Find("Standard"));
                        material.name = $"{objectType}_Material";
                    }
                    
                    if (useGPUInstancing) {
                        material.enableInstancing = true;
                    }
                    
                    mr.sharedMaterial = material;
                }
                
                // Add collider
                var collider = modelObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                collider.convex = true;
                
                // Add model metadata component
                var metadata = modelObject.AddComponent<ModelMetadata>();
                metadata.modelType = objectType;
                metadata.description = modelData.name;
                metadata.vertexCount = modelData.vertices.Length;
                metadata.triangleCount = modelData.triangles.Length / 3;
                metadata.generatedDate = DateTime.UtcNow;
                
                foreach (var kvp in modelData.metadata) {
                    metadata.AddMetadata(kvp.Key, kvp.Value);
                }
                
                // Normalize scale and position
                NormalizeMeshTransform(modelObject);
                
                return modelObject;
            }
            catch (Exception ex) {
                debugger.LogError($"Failed to instantiate model: {ex.Message}", LogCategory.Models);
                return null;
            }
        }
        
        private async Task CreateLODLevelsAsync(GameObject parent, Mesh highPolyMesh, string objectType) {
            // Create LOD group
            var lodGroup = parent.AddComponent<LODGroup>();
            LOD[] lods = new LOD[maxLODLevels];
            
            // Generate each LOD level
            for (int i = 0; i < maxLODLevels; i++) {
                float reduction = Mathf.Pow(lodQualityFactor, i);
                
                GameObject lodObject = new GameObject($"LOD_{i}");
                lodObject.transform.SetParent(parent.transform, false);
                
                var mf = lodObject.AddComponent<MeshFilter>();
                var mr = lodObject.AddComponent<MeshRenderer>();
                
                // Generate simplified mesh for this LOD level
                Mesh lodMesh;
                if (i == 0) {
                    // LOD0 is the original high-poly mesh
                    lodMesh = highPolyMesh;
                } else {
                    // Simplify mesh for lower LOD levels
                    float quality = Mathf.Pow(lodQualityFactor, i);
                    lodMesh = await SimplifyMeshAsync(highPolyMesh, quality);
                }
                
                mf.sharedMesh = lodMesh;
                
                // Create material
                Material material = null;
                if (defaultMaterial != null) {
                    material = new Material(defaultMaterial);
                } else {
                    material = new Material(Shader.Find("Standard"));
                }
                
                material.name = $"{objectType}_LOD{i}_Material";
                
                if (useGPUInstancing) {
                    material.enableInstancing = true;
                }
                
                mr.sharedMaterial = material;
                
                // Calculate screen percentage for this LOD
                float screenRelativeTransitionHeight = Mathf.Pow(0.5f, i);
                lods[i] = new LOD(screenRelativeTransitionHeight, new Renderer[] { mr });
            }
            
            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
        }
        
        private async Task<Mesh> SimplifyMeshAsync(Mesh sourceMesh, float quality) {
            // Simplified mesh decimation algorithm
            // For a production environment, use a proper mesh simplification library
            
            if (quality >= 0.99f) return new Mesh(sourceMesh);
            
            int targetTriangles = Mathf.Max(1, Mathf.RoundToInt(sourceMesh.triangles.Length / 3 * quality));
            
            return await Task.Run(() => {
                Mesh simplifiedMesh = new Mesh();
                simplifiedMesh.name = sourceMesh.name + "_Simplified";
                
                // Very basic vertex welding and triangle removal
                // This is a placeholder - real implementation would use a proper decimation algorithm
                List<Vector3> vertices = new List<Vector3>(sourceMesh.vertices);
                List<Vector3> normals = new List<Vector3>(sourceMesh.normals);
                List<Vector2> uvs = new List<Vector2>(sourceMesh.uv);
                List<int> triangles = new List<int>(sourceMesh.triangles);
                
                // Simple decimation by removing every nth triangle
                if (triangles.Count / 3 > targetTriangles) {
                    int stride = triangles.Count / (targetTriangles * 3);
                    if (stride > 1) {
                        List<int> newTriangles = new List<int>();
                        for (int i = 0; i < triangles.Count; i += 3 * stride) {
                            if (i + 2 < triangles.Count) {
                                newTriangles.Add(triangles[i]);
                                newTriangles.Add(triangles[i + 1]);
                                newTriangles.Add(triangles[i + 2]);
                            }
                        }
                        triangles = newTriangles;
                    }
                }
                
                simplifiedMesh.vertices = vertices.ToArray();
                simplifiedMesh.normals = normals.ToArray();
                simplifiedMesh.uv = uvs.ToArray();
                simplifiedMesh.triangles = triangles.ToArray();
                
                simplifiedMesh.RecalculateBounds();
                return simplifiedMesh;
            });
        }
        
        private void NormalizeMeshTransform(GameObject modelObject) {
            var meshFilter = modelObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null) return;
            
            Mesh mesh = meshFilter.sharedMesh;
            
            // Calculate scale to normalize size
            Bounds bounds = mesh.bounds;
            float maxExtent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxExtent > 0 && maxExtent != 1) {
                Vector3 scale = Vector3.one / maxExtent;
                
                // Scale vertices
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) {
                    vertices[i] = Vector3.Scale(vertices[i], scale);
                }
                
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
            
            // Center the mesh if not centered
            bounds = mesh.bounds;
            if (bounds.center != Vector3.zero) {
                Vector3 offset = -bounds.center;
                
                // Offset vertices
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) {
                    vertices[i] += offset;
                }
                
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
            }
        }
        #endregion

        #region Terrain Placement
        private PlacementTransform AdaptTransformToTerrain(
            PlacementTransform transform,
            GameObject model,
            UnityEngine.Terrain terrain
        ) {
            if (terrain == null) return transform;
            
            PlacementTransform result = new PlacementTransform(transform);
            Vector3 position = transform.Position;
            
            // Sample terrain height at position
            float terrainHeight = terrain.SampleHeight(position);
            position.y = terrainHeight;
            
            // Calculate size of the object for proper placement
            Bounds bounds = CalculateModelBounds(model);
            float objectHeight = bounds.size.y * transform.Scale.y;
            float objectBottom = bounds.min.y * transform.Scale.y;
            
            // Fix floating or embedded objects
            if (fixFloatingObjects || fixEmbeddedObjects) {
                // Adjust Y position to sink the object properly
                position.y = terrainHeight - objectBottom + (groundingDepth * objectHeight);
            }
            
            // Get terrain normal for slope alignment
            Vector3 terrainNormal = GetSmoothTerrainNormal(terrain, position, normalSampleSize);
            
            // Check if slope is too steep for this object
            float slopeAngle = Vector3.Angle(terrainNormal, Vector3.up);
            if (slopeAngle > maxPlacementSlope) {
                // Find nearby position with acceptable slope
                Vector3 betterPosition = FindBetterPlacementLocation(position, terrain, maxPlacementSlope);
                if (betterPosition != position) {
                    position = betterPosition;
                    terrainHeight = terrain.SampleHeight(position);
                    position.y = terrainHeight - objectBottom + (groundingDepth * objectHeight);
                    terrainNormal = GetSmoothTerrainNormal(terrain, position, normalSampleSize);
                }
            }
            
            // Align rotation to terrain normal if needed
            Quaternion rotation = transform.Rotation;
            if (adaptToTerrain) {
                // Create rotation that aligns the model's up direction with the terrain normal
                Quaternion terrainAlign = Quaternion.FromToRotation(Vector3.up, terrainNormal);
                
                // Combine with original rotation, preserving facing direction
                rotation = terrainAlign * transform.Rotation;
            }
            
            // Update result
            result.Position = position;
            result.Rotation = rotation;
            
            return result;
        }
        
        private Bounds CalculateModelBounds(GameObject model) {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool boundsInitialized = false;
            
            // Get bounds from all renderers
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            foreach (Renderer renderer in renderers) {
                if (!boundsInitialized) {
                    bounds = renderer.bounds;
                    boundsInitialized = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            
            // If no renderers, try using colliders
            if (!boundsInitialized) {
                Collider[] colliders = model.GetComponentsInChildren<Collider>();
                foreach (Collider collider in colliders) {
                    if (!boundsInitialized) {
                        bounds = collider.bounds;
                        boundsInitialized = true;
                    } else {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }
            
            // If still no bounds, use a default size
            if (!boundsInitialized) {
                bounds = new Bounds(Vector3.zero, Vector3.one);
            }
            
            // Convert to local space
            bounds.center -= model.transform.position;
            
            return bounds;
        }
        
        private Vector3 GetSmoothTerrainNormal(UnityEngine.Terrain terrain, Vector3 worldPosition, int sampleSize) {
            if (terrain == null) return Vector3.up;
            
            Vector3 sum = Vector3.zero;
            
            // Sample multiple points around the position
            for (int x = -sampleSize/2; x <= sampleSize/2; x++) {
                for (int z = -sampleSize/2; z <= sampleSize/2; z++) {
                    // Skip center point as we'll add it with more weight later
                    if (x == 0 && z == 0) continue;
                    
                    Vector3 samplePos = worldPosition + new Vector3(x * 0.5f, 0, z * 0.5f);
                    Vector2 normPos = new Vector2(
                        (samplePos.x - terrain.transform.position.x) / terrain.terrainData.size.x,
                        (samplePos.z - terrain.transform.position.z) / terrain.terrainData.size.z
                    );
                    
                    // Ensure we're sampling within terrain bounds
                    if (normPos.x >= 0 && normPos.x <= 1 && normPos.y >= 0 && normPos.y <= 1) {
                        Vector3 normal = terrain.terrainData.GetInterpolatedNormal(normPos.x, normPos.y);
                        
                        // Weight by distance from center
                        float dist = Mathf.Sqrt(x*x + z*z);
                        float weight = 1f / (1f + dist);
                        
                        sum += normal * weight;
                    }
                }
            }
            
            // Add center point with higher weight
            Vector2 centerNormPos = new Vector2(
                (worldPosition.x - terrain.transform.position.x) / terrain.terrainData.size.x,
                (worldPosition.z - terrain.transform.position.z) / terrain.terrainData.size.z
            );
            Vector3 centerNormal = terrain.terrainData.GetInterpolatedNormal(centerNormPos.x, centerNormPos.y);
            sum += centerNormal * 2f;
            
            return sum.normalized;
        }
        
        private Vector3 FindBetterPlacementLocation(Vector3 position, UnityEngine.Terrain terrain, float maxSlope) {
            // Check points in increasing radius around position to find a better spot
            for (float radius = 0.5f; radius <= 5f; radius += 0.5f) {
                for (int i = 0; i < 8; i++) {
                    float angle = i * Mathf.PI * 2f / 8f;
                    Vector3 offset = new Vector3(
                        Mathf.Cos(angle) * radius,
                        0,
                        Mathf.Sin(angle) * radius
                    );
                    
                    Vector3 testPos = position + offset;
                    Vector3 normal = GetSmoothTerrainNormal(terrain, testPos, normalSampleSize);
                    float slopeAngle = Vector3.Angle(normal, Vector3.up);
                    
                    if (slopeAngle <= maxSlope) {
                        return testPos;
                    }
                }
            }
            
            // If no better position found, return original
            return position;
        }
        
        private bool FindTerrain(out UnityEngine.Terrain terrain) {
            terrain = null;
            
            // First try using the cached terrain
            if (_terrainCache.Count > 0) {
                terrain = _terrainCache.Values.First();
                return terrain != null;
            }
            
            // Otherwise find terrain in scene
            terrain = Terrain.activeTerrain;
            if (terrain != null) {
                _terrainCache[terrain.GetInstanceID().ToString()] = terrain;
                return true;
            }
            
            return false;
        }
        #endregion

        #region Object Grouping
        private List<ObjectGroup> GroupObjectsBySimilarity(List<MapObject> objects) {
            if (objects == null || objects.Count == 0) {
                return new List<ObjectGroup>();
            }
            
            List<ObjectGroup> groups = new List<ObjectGroup>();
            HashSet<MapObject> processedObjects = new HashSet<MapObject>();
            
            foreach (var obj in objects) {
                if (processedObjects.Contains(obj)) continue;
                
                // Start a new group with this object
                var group = new ObjectGroup {
                    groupId = Guid.NewGuid().ToString(),
                    type = obj.type,
                    objects = new List<MapObject> { obj }
                };
                
                processedObjects.Add(obj);
                
                // Find similar objects
                foreach (var other in objects) {
                    if (processedObjects.Contains(other)) continue;
                    if (group.objects.Count >= maxGroupSize) break;
                    
                    if (other.type == obj.type) {
                        // Check similarity
                        float similarity = CalculateSimilarity(obj, other);
                        if (similarity >= instancingSimilarity) {
                            group.objects.Add(other);
                            processedObjects.Add(other);
                        }
                    }
                }
                
                // Calculate group center and radius
                group.RecalculateBounds();
                groups.Add(group);
            }
            
            return groups;
        }
        
        private float CalculateSimilarity(MapObject obj1, MapObject obj2) {
            switch (similarityMetric) {
                case SimilarityMetric.Euclidean:
                    return CalculateEuclideanSimilarity(obj1, obj2);
                
                case SimilarityMetric.JaccardIndex:
                    return CalculateJaccardSimilarity(obj1, obj2);
                
                case SimilarityMetric.StructuralSimilarity:
                    return CalculateStructuralSimilarity(obj1, obj2);
                
                case SimilarityMetric.FeatureVector:
                    return CalculateFeatureVectorSimilarity(obj1, obj2);
                
                default:
                    return obj1.type == obj2.type ? 1f : 0f;
            }
        }
        
        private float CalculateEuclideanSimilarity(MapObject obj1, MapObject obj2) {
            float scaleDistance = Vector3.Distance(obj1.scale, obj2.scale);
            float rotationDistance = Mathf.Abs(obj1.rotation - obj2.rotation) / 180f;
            
            // Normalize distances to [0, 1] range
            float normalizedDistance = (scaleDistance + rotationDistance) / 2f;
            
            // Convert to similarity score (1 - distance)
            return Mathf.Clamp01(1f - normalizedDistance);
        }
        
        private float CalculateJaccardSimilarity(MapObject obj1, MapObject obj2) {
            // Use semantic attributes if available
            if (obj1.attributes != null && obj2.attributes != null) {
                var keys1 = new HashSet<string>(obj1.attributes.Keys);
                var keys2 = new HashSet<string>(obj2.attributes.Keys);
                
                int intersection = keys1.Intersect(keys2).Count();
                int union = keys1.Union(keys2).Count();
                
                return union > 0 ? (float)intersection / union : 0f;
            }
            
            // Fallback to simple type comparison
            return obj1.type == obj2.type ? 1f : 0f;
        }
        
        private float CalculateStructuralSimilarity(MapObject obj1, MapObject obj2) {
            // Base similarity on shape, aspect ratio, etc.
            if (obj1.segmentMask != null && obj2.segmentMask != null) {
                // Calculate aspect ratios of bounding boxes
                float aspectRatio1 = obj1.boundingBox.width / obj1.boundingBox.height;
                float aspectRatio2 = obj2.boundingBox.width / obj2.boundingBox.height;
                
                // Calculate relative size
                float area1 = obj1.boundingBox.width * obj1.boundingBox.height;
                float area2 = obj2.boundingBox.width * obj2.boundingBox.height;
                float relativeSize = Mathf.Min(area1, area2) / Mathf.Max(area1, area2);
                
                // Calculate aspect ratio similarity
                float aspectRatioSimilarity = 1f - Mathf.Abs(aspectRatio1 - aspectRatio2) / Mathf.Max(aspectRatio1, aspectRatio2);
                
                // Combine metrics
                return Mathf.Clamp01((aspectRatioSimilarity + relativeSize) / 2f);
            }
            
            return obj1.type == obj2.type ? 0.5f : 0f;
        }
        
        private float CalculateFeatureVectorSimilarity(MapObject obj1, MapObject obj2) {
            // Use attributes as feature vectors
            if (obj1.attributes != null && obj2.attributes != null) {
                // Get all keys
                var allKeys = new HashSet<string>(obj1.attributes.Keys.Concat(obj2.attributes.Keys));
                
                if (allKeys.Count == 0) return obj1.type == obj2.type ? 0.8f : 0f;
                
                // Calculate cosine similarity
                float dotProduct = 0f;
                float magnitude1 = 0f;
                float magnitude2 = 0f;
                
                foreach (var key in allKeys) {
                    float value1 = obj1.attributes.TryGetValue(key, out float v1) ? v1 : 0f;
                    float value2 = obj2.attributes.TryGetValue(key, out float v2) ? v2 : 0f;
                    
                    dotProduct += value1 * value2;
                    magnitude1 += value1 * value1;
                    magnitude2 += value2 * value2;
                }
                
                if (magnitude1 > 0 && magnitude2 > 0) {
                    return dotProduct / (Mathf.Sqrt(magnitude1) * Mathf.Sqrt(magnitude2));
                }
            }
            
            // Fallback similarity based on type
            return obj1.type == obj2.type ? 0.8f : 0f;
        }
        
        private List<ModelGenerationRequest> CreateRequestsFromGroup(ObjectGroup group, UnityEngine.Terrain terrain) {
            List<ModelGenerationRequest> requests = new List<ModelGenerationRequest>();
            
            if (group.objects.Count == 0) return requests;
            
            // Create one request for the template
            MapObject template = group.objects.First();
            
            requests.Add(new ModelGenerationRequest {
                objectType = template.type,
                description = template.enhancedDescription ?? template.label,
                position = Vector3.zero,  // Will be set when instancing
                rotation = Quaternion.identity,
                scale = Vector3.one,
                confidence = template.confidence,
                isGrouped = true
            });
            
            // Only do full generation for the first instance, then just instance the model
            return requests;
        }
        
        private ModelGenerationRequest CreateRequestFromMapObject(MapObject obj, UnityEngine.Terrain terrain) {
            if (terrain != null) {
                // Convert normalized position to world space
                Vector3 worldPos = new Vector3(
                    obj.position.x * terrain.terrainData.size.x,
                    0,
                    obj.position.y * terrain.terrainData.size.z
                );
                
                return new ModelGenerationRequest {
                    objectType = obj.type,
                    description = obj.enhancedDescription ?? obj.label,
                    position = worldPos,
                    rotation = Quaternion.Euler(0, obj.rotation, 0),
                    scale = obj.scale,
                    confidence = obj.confidence,
                    isGrouped = false
                };
            }
            
            return new ModelGenerationRequest {
                objectType = obj.type,
                description = obj.enhancedDescription ?? obj.label,
                position = Vector3.zero,
                rotation = Quaternion.Euler(0, obj.rotation, 0),
                scale = obj.scale,
                confidence = obj.confidence,
                isGrouped = false
            };
        }
        #endregion

        #region Mesh Optimization
        private IEnumerator CombineSimilarMeshes(List<GameObject> objects) {
            if (objects == null || objects.Count < 2) yield break;
            
            // Group objects by material
            Dictionary<Material, List<GameObject>> materialGroups = new Dictionary<Material, List<GameObject>>();
            
            foreach (var obj in objects) {
                var renderers = obj.GetComponentsInChildren<MeshRenderer>();
                foreach (var renderer in renderers) {
                    if (renderer.sharedMaterial != null) {
                        if (!materialGroups.ContainsKey(renderer.sharedMaterial)) {
                            materialGroups[renderer.sharedMaterial] = new List<GameObject>();
                        }
                        materialGroups[renderer.sharedMaterial].Add(renderer.gameObject);
                    }
                }
            }
            
            // Create combined meshes for each material group
            foreach (var group in materialGroups) {
                if (group.Value.Count < 2) continue;
                
                var material = group.Key;
                var meshObjects = group.Value;
                
                debugger.Log($"Combining {meshObjects.Count} meshes with shared material", LogCategory.Models);
                
                // Create batches to stay within vertex limits
                List<List<GameObject>> batches = new List<List<GameObject>>();
                List<GameObject> currentBatch = new List<GameObject>();
                int currentVertexCount = 0;
                
                foreach (var obj in meshObjects) {
                    var meshFilter = obj.GetComponent<MeshFilter>();
                    if (meshFilter == null || meshFilter.sharedMesh == null) continue;
                    
                    int vertexCount = meshFilter.sharedMesh.vertexCount;
                    if (currentVertexCount + vertexCount > maxVerticesPerMesh) {
                        if (currentBatch.Count > 0) {
                            batches.Add(currentBatch);
                            currentBatch = new List<GameObject>();
                            currentVertexCount = 0;
                        }
                    }
                    
                    currentBatch.Add(obj);
                    currentVertexCount += vertexCount;
                }
                
                if (currentBatch.Count > 0) {
                    batches.Add(currentBatch);
                }
                
                // Combine each batch
                foreach (var batch in batches) {
                    if (batch.Count < 2) continue;
                    
                    yield return StartCoroutine(CombineMeshBatch(batch, material));
                }
            }
        }
        
        private IEnumerator CombineMeshBatch(List<GameObject> meshObjects, Material material) {
            if (meshObjects.Count < 2) yield break;
            
            // Create combined mesh
            GameObject combinedObject = new GameObject($"Combined_{material.name}_{Guid.NewGuid().ToString().Substring(0, 8)}");
            combinedObject.transform.SetParent(_modelContainer.transform);
            
            // Prepare combine instances
            List<CombineInstance> combineInstances = new List<CombineInstance>();
            Dictionary<Transform, Matrix4x4> originalTransforms = new Dictionary<Transform, Matrix4x4>();
            
            // Collect meshes
            foreach (var obj in meshObjects) {
                var meshFilter = obj.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null) continue;
                
                // Store original transform
                originalTransforms[obj.transform] = obj.transform.localToWorldMatrix;
                
                // Add to combine instances
                CombineInstance ci = new CombineInstance {
                    mesh = meshFilter.sharedMesh,
                    transform = obj.transform.localToWorldMatrix,
                    subMeshIndex = 0
                };
                
                combineInstances.Add(ci);
            }
            
            // Create combined mesh
            Mesh combinedMesh = new Mesh();
            combinedMesh.name = $"Combined_{material.name}";
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Support >65k vertices
            combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            
            // Add to combined object
            var mf = combinedObject.AddComponent<MeshFilter>();
            var mr = combinedObject.AddComponent<MeshRenderer>();
            mf.sharedMesh = combinedMesh;
            mr.sharedMaterial = material;
            
            // Add collider
            var collider = combinedObject.AddComponent<MeshCollider>();
            collider.sharedMesh = combinedMesh;
            collider.convex = false; // Combined mesh likely not convex
            
            // Deactivate or destroy original objects
            foreach (var obj in meshObjects) {
                obj.SetActive(false);
                // Don't destroy, just hide - we might need them later
            }
            
            debugger.Log($"Created combined mesh with {combinedMesh.vertexCount} vertices, {combinedMesh.triangles.Length / 3} triangles", 
                LogCategory.Models);
            
            yield return null;
        }
        #endregion

        #region OpenAI Enhancement
        private async Task<string> EnhanceDescriptionAsync(string description) {
            if (string.IsNullOrEmpty(openAIApiKey) || string.IsNullOrEmpty(description)) {
                return description;
            }
            
            try {
                string enhancedDescription = await OpenAIResponse.Instance.GetCompletionAsync(
                    FormatDescriptionPrompt(description),
                    openAIApiKey
                );
                
                if (!string.IsNullOrEmpty(enhancedDescription)) {
                    return enhancedDescription;
                }
            }
            catch (Exception ex) {
                debugger.LogWarning($"Failed to enhance description: {ex.Message}", LogCategory.API);
            }
            
            return description;
        }
        
        private string FormatDescriptionPrompt(string description) {
            return promptTemplate.Replace("{description}", description);
        }
        
        private string FormatPrompt(string description) {
            // Process the prompt template if available, otherwise just use the description
            if (!string.IsNullOrEmpty(promptTemplate)) {
                return promptTemplate.Replace("{description}", description);
            }
            
            return $"Generate a detailed 3D model of {description} suitable for a game environment.";
        }
        #endregion

        #region Fallback and Caching
        private GameObject GetFallbackModel(string objectType) {
            // Try to load a prefab with matching type name
            GameObject prefab = Resources.Load<GameObject>($"{fallbackModelPath}/{objectType}");
            
            if (prefab != null) {
                return prefab;
            }
            
            // Try to find a similar fallback by tokenizing the object type
            string[] tokens = objectType.ToLowerInvariant().Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            
            var fallbackModels = Resources.LoadAll<GameObject>(fallbackModelPath);
            foreach (var model in fallbackModels) {
                foreach (var token in tokens) {
                    if (model.name.ToLowerInvariant().Contains(token)) {
                        return model;
                    }
                }
            }
            
            // Return a primitive if no fallback found
            if (tokens.Contains("tree") || tokens.Contains("plant")) {
                return CreatePrimitiveFallback(PrimitiveType.Cylinder, objectType);
            }
            else if (tokens.Contains("building") || tokens.Contains("house") || tokens.Contains("structure")) {
                return CreatePrimitiveFallback(PrimitiveType.Cube, objectType);
            }
            else if (tokens.Contains("rock") || tokens.Contains("boulder") || tokens.Contains("stone")) {
                return CreatePrimitiveFallback(PrimitiveType.Sphere, objectType);
            }
            
            // Default fallback
            return CreatePrimitiveFallback(PrimitiveType.Cube, objectType);
        }
        
        private GameObject CreatePrimitiveFallback(PrimitiveType type, string objectType) {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = $"Fallback_{objectType}";
            
            // Parent to this gameObject temporarily so it's not in the scene hierarchy
            primitive.transform.SetParent(transform);
            primitive.SetActive(false);
            
            return primitive;
        }
        
        private string GenerateModelId(string objectType, string description) {
            string normalizedType = string.IsNullOrEmpty(objectType) ? "unknown" : objectType.ToLowerInvariant().Trim();
            string normalizedDesc = string.IsNullOrEmpty(description) ? "" : description.ToLowerInvariant().Trim();
            
            // Use the first 50 chars of description to avoid excessively long IDs
            if (normalizedDesc.Length > 50) {
                normalizedDesc = normalizedDesc.Substring(0, 50);
            }
            
            // Create a deterministic ID for caching purposes
            return $"{normalizedType}_{string.Join("_", normalizedDesc.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))}";
        }
        #endregion

        #region Status Tracking
        private void UpdateGenerationStatus(string modelId, float progress, string status) {
            if (!_generationStatus.TryGetValue(modelId, out var currentStatus)) {
                currentStatus = new ModelGenerationStatus {
                    ModelId = modelId,
                    StartTime = DateTime.UtcNow
                };
                _generationStatus[modelId] = currentStatus;
            }
            
            currentStatus.Progress = progress;
            currentStatus.Status = status;
            currentStatus.LastUpdateTime = DateTime.UtcNow;
            
            if (progress >= 1.0f || progress < 0f) {
                currentStatus.EndTime = DateTime.UtcNow;
                currentStatus.IsComplete = true;
            }
            
            if (verboseLogging) {
                if (progress < 0) {
                    debugger.LogError($"Model {modelId}: {status}", LogCategory.Models);
                } else {
                    debugger.Log($"Model {modelId}: {progress:P0} - {status}", LogCategory.Models);
                }
            }
        }
        
        private float GetGenerationProgress(string objectType) {
            // Find matching generation by type prefix
            foreach (var status in _generationStatus.Values) {
                if (status.ModelId.StartsWith(objectType.ToLowerInvariant().Trim())) {
                    return status.Progress;
                }
            }
            return 0f;
        }
        
        private void TrackModelGeneration(string objectType) {
            _totalModelsGenerated++;
            
            // Update distribution statistics
            string type = objectType.ToLowerInvariant().Trim();
            if (!_modelTypeDistribution.ContainsKey(type)) {
                _modelTypeDistribution[type] = 0;
            }
            _modelTypeDistribution[type]++;
        }
        
        private void LogModelDistribution() {
            if (_modelTypeDistribution.Count == 0) return;
            
            var sortedTypes = _modelTypeDistribution.OrderByDescending(kvp => kvp.Value);
            var log = new System.Text.StringBuilder("Model type distribution:\n");
            
            foreach (var kvp in sortedTypes) {
                log.AppendLine($"- {kvp.Key}: {kvp.Value}");
            }
            
            debugger.Log(log.ToString(), LogCategory.Models);
        }
        #endregion

        #region Helper Classes
        [Serializable]
        private class ModelGenerationConfig {
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
            
            public override string ToString() {
                return $"ModelConfig[GPU:{UseGPUInstancing}, PBR:{UsePBRMaterials}, LOD:{UseLOD}({MaxLODLevels}), " +
                       $"Terrain:{AdaptToTerrain}, Collisions:{AvoidCollisions}, GroupObjects:{GroupSimilarObjects}]";
            }
        }
        
        [Serializable]
        private class PlacementTransform {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            
            public PlacementTransform() {
                Position = Vector3.zero;
                Rotation = Quaternion.identity;
                Scale = Vector3.one;
            }
            
            public PlacementTransform(PlacementTransform other) {
                Position = other.Position;
                Rotation = other.Rotation;
                Scale = other.Scale;
            }
            
            public override string ToString() {
                return $"Pos:{Position}, Rot:{Rotation.eulerAngles}, Scale:{Scale}";
            }
        }
        
        [Serializable]
        private class ModelGenerationStatus {
            public string ModelId;
            public float Progress;
            public string Status;
            public DateTime StartTime;
            public DateTime LastUpdateTime;
            public DateTime EndTime;
            public bool IsComplete;
            public bool IsError => Progress < 0f;
            public TimeSpan ElapsedTime => (IsComplete ? EndTime : DateTime.UtcNow) - StartTime;
        }
        
        [Serializable]
        private class ModelGenerationJob {
            public string JobId;
            public ModelGenerationRequest Request;
            public Task<GameObject> Task;
            public DateTime StartTime;
            public float Progress;
            public string Status;
            public GameObject Result;
            public Exception Error;
            public bool IsComplete => Result != null || Error != null;
        }
        
        [Serializable]
        private class Tripo3DResponse {
            public string id;
            public string status;
            public Tripo3DModelData modelData;
        }
        
        [Serializable]
        private class Tripo3DModelData {
            public string id;
            public string name;
            public string format;
            public string downloadUrl;
            public string previewUrl;
            public string status;
            public DateTime createdAt;
        }
        #endregion

        #region MonoBehaviour Components
        /// <summary>
        /// Component that tracks metadata for generated models.
        /// </summary>
        [AddComponentMenu("Traversify/Model Metadata")]
        public class ModelMetadata : MonoBehaviour {
            public string modelType;
            public string description;
            public int vertexCount;
            public int triangleCount;
            public DateTime generatedDate;
            public Dictionary<string, object> metadata = new Dictionary<string, object>();
            
            public void AddMetadata(string key, object value) {
                metadata[key] = value;
            }
            
            public T GetMetadata<T>(string key, T defaultValue = default) {
                if (metadata.TryGetValue(key, out object value) && value is T typedValue) {
                    return typedValue;
                }
                return defaultValue;
            }
        }
        
        /// <summary>
        /// Component for tracking model instances.
        /// </summary>
        [AddComponentMenu("Traversify/Model Instance Tracker")]
        public class ModelInstanceTracker : MonoBehaviour {
            public string sourceType;
            public Vector3 originalPosition;
            public Quaternion originalRotation;
            public Vector3 originalScale;
            public Dictionary<string, object> metadata = new Dictionary<string, object>();
            
            private void Awake() {
                originalRotation = transform.rotation;
                originalScale = transform.localScale;
            }
        }
        #endregion

        #region Editor Gizmos
        private void OnDrawGizmosSelected() {
            if (!showPlacementGizmos) return;
            
            // Draw placement gizmos for debugging
            if (_modelContainer != null) {
                var trackers = _modelContainer.GetComponentsInChildren<ModelInstanceTracker>();
                foreach (var tracker in trackers) {
                    Gizmos.color = Color.green;
                    Gizmos.DrawWireSphere(tracker.originalPosition, 0.5f);
                    
                    Gizmos.color = Color.blue;
                    Gizmos.DrawLine(tracker.originalPosition, tracker.transform.position);
                    
                    if (FindTerrain(out UnityEngine.Terrain terrain)) {
                        Vector3 terrainPos = tracker.originalPosition;
                        terrainPos.y = terrain.SampleHeight(terrainPos);
                        
                        Gizmos.color = Color.red;
                        Gizmos.DrawLine(terrainPos, tracker.transform.position);
                    }
                }
            }
        }
        #endregion
    }
}
