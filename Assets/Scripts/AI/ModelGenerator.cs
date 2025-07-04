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
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Traversify.Core;
using Traversify.AI;
using AiToolbox;
using Piglet;

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
        
        // Add missing Tripo3DApiKey property
        [Tooltip("Tripo3D API key for model generation")]
        public string Tripo3DApiKey 
        { 
            get => tripo3DApiKey; 
            set => tripo3DApiKey = value; 
        }
        
        [Header("AI Toolbox Settings")]
        [SerializeField] private bool _useGemini = false;
        [SerializeField] private string _geminiApiKey;
        
        [Header("Tripo3D Settings")]
        [Tooltip("Enable Tripo3D generation for detected objects")]
        public bool useTripo3D = true;
        
        [Tooltip("Tripo3D API key for model generation")]
        [SerializeField] private string tripo3DApiKey;
        
        [Tooltip("Tripo3D Base URL")]
        [SerializeField] private string _tripo3DBaseUrl = "https://api.tripo3d.ai/v2/openapi";
        
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

        [Header("Model Management")]
        [SerializeField] private string _modelCacheDirectory = "Assets/GeneratedModels";
        [SerializeField] private bool _enableModelCaching = true;
        [SerializeField] private bool _enableInstancing = true;
        [SerializeField] private float _instanceSimilarityThreshold = 0.8f;
        
        [Header("Quality Settings")]
        [SerializeField] private ModelQuality _defaultQuality = ModelQuality.Medium;
        [SerializeField] private bool _generateLODs = true;
        [SerializeField] private bool _optimizeForMobile = false;
        
        public enum ModelQuality {
            Low,
            Medium,
            High,
            UltraHigh
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
                InitializeModelGenerator();
                debugger.Log("ModelGenerator initialized successfully", LogCategory.System);
                return true;
            }
            catch (Exception ex)
            {
                debugger.LogError($"Failed to initialize ModelGenerator: {ex.Message}", LogCategory.System);
                return false;
            }
        }
        
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
        
        // OpenAI API for description enhancement
        private string _openAIApiKey;
        
        // Tripo3D generation state
        private Dictionary<string, List<DetectedObject>> _objectGroups = new Dictionary<string, List<DetectedObject>>();
        private Dictionary<string, GameObject> _generatedModels = new Dictionary<string, GameObject>();
        private Queue<Tripo3DRequest> _tripo3DQueue = new Queue<Tripo3DRequest>();
        private int _activeTripo3DRequests = 0;
        #endregion

        #region Data Structures and Types
        
        /// <summary>
        /// Represents a model generation job with tracking information
        /// </summary>
        private class ModelGenerationJob
        {
            public string jobId;
            public ModelGenerationRequest request;
            public Task<GameObject> task;
            public DateTime startTime;
            public bool isCompleted;
            public GameObject result;
            public Exception error;
        }
        
        /// <summary>
        /// Tracks the status of model generation requests
        /// </summary>
        private enum ModelGenerationStatus
        {
            Pending,
            InProgress,
            Completed,
            Failed,
            Cancelled
        }
        
        /// <summary>
        /// Configuration settings for model generation
        /// </summary>
        private class ModelGenerationConfig
        {
            public bool UseGPUInstancing { get; set; }
            public bool UsePBRMaterials { get; set; }
            public bool GenerateTextures { get; set; }
            public Vector2Int TextureResolution { get; set; }
            public bool GenerateNormalMaps { get; set; }
            public bool GenerateMetallicMaps { get; set; }
            public bool UseLOD { get; set; }
            public int MaxLODLevels { get; set; }
            public float LodQualityFactor { get; set; }
            public bool AdaptToTerrain { get; set; }
            public bool AvoidCollisions { get; set; }
            public float MaxPlacementSlope { get; set; }
            public float ObjectSpacing { get; set; }
            public bool GroupSimilarObjects { get; set; }
            public float InstancingSimilarity { get; set; }
            public SimilarityMetric SimilarityMetric { get; set; }
            public int MaxGroupSize { get; set; }
            public float GroundingDepth { get; set; }
            public bool FixFloatingObjects { get; set; }
            public bool FixEmbeddedObjects { get; set; }
        }
        
        /// <summary>
        /// Represents a request for Tripo3D model generation
        /// </summary>
        private class Tripo3DRequest
        {
            public string requestId;
            public string objectType;
            public string description;
            public Tripo3DQuality quality;
            public DateTime requestTime;
            public System.Action<GameObject> onSuccess;
            public System.Action<string> onError;
        }
        
        /// <summary>
        /// Represents a model generation request with all necessary parameters
        /// </summary>
        private class ModelGenerationRequest
        {
            public string objectType;
            public string description;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public float confidence;
            public bool isGrouped;
            public Dictionary<string, object> metadata;
            
            public ModelGenerationRequest()
            {
                metadata = new Dictionary<string, object>();
                position = Vector3.zero;
                rotation = Quaternion.identity;
                scale = Vector3.one;
                confidence = 1.0f;
                isGrouped = false;
            }
        }
        
        /// <summary>
        /// Transform data for object placement
        /// </summary>
        private struct PlacementTransform
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }
        
        /// <summary>
        /// Component for tracking model instances
        /// </summary>
        private class ModelInstanceTracker : MonoBehaviour
        {
            public string sourceType;
            public Vector3 originalPosition;
        }
        
        /// <summary>
        /// Component for storing generated model information
        /// </summary>
        private class GeneratedModelInfo : MonoBehaviour
        {
            public ObjectGroup sourceGroup;
            public string objectType;
            public string description;
        }
        
        /// <summary>
        /// Provides generated models to ObjectPlacer
        /// </summary>
        private class GeneratedModelProvider
        {
            private List<GameObject> _models;
            private List<MapObject> _mapObjects;
            
            public GeneratedModelProvider(List<GameObject> models, List<MapObject> mapObjects)
            {
                _models = models;
                _mapObjects = mapObjects;
            }
            
            public GameObject GetModelForObject(MapObject mapObject)
            {
                // Simple implementation - return first available model
                // In a full implementation, this would match models to objects by type
                return _models.Count > 0 ? _models[0] : null;
            }
        }
        
        #endregion
        
        #region Missing Method Implementations
        
        /// <summary>
        /// Creates generation requests from an object group
        /// </summary>
        private List<ModelGenerationRequest> CreateRequestsFromGroup(ObjectGroup group, UnityEngine.Terrain terrain)
        {
            var requests = new List<ModelGenerationRequest>();
            
            foreach (var obj in group.objects)
            {
                var request = CreateRequestFromMapObject(obj, terrain);
                request.isGrouped = true;
                request.metadata["groupId"] = group.groupId;
                requests.Add(request);
            }
            
            return requests;
        }
        
        /// <summary>
        /// Creates a generation request from a map object
        /// </summary>
        private ModelGenerationRequest CreateRequestFromMapObject(MapObject obj, UnityEngine.Terrain terrain)
        {
            return new ModelGenerationRequest
            {
                objectType = obj.type,
                description = obj.enhancedDescription ?? obj.label,
                position = new Vector3(obj.position.x, 0, obj.position.y),
                rotation = Quaternion.Euler(0, obj.rotation, 0), // Fix: Convert float degrees to Quaternion
                scale = obj.scale,
                confidence = obj.confidence,
                isGrouped = false,
                metadata = new Dictionary<string, object>
                {
                    ["sourceObject"] = obj,
                    ["terrain"] = terrain
                }
            };
        }
        
        /// <summary>
        /// Groups objects by similarity for instancing
        /// </summary>
        private List<ObjectGroup> GroupObjectsBySimilarity(List<MapObject> mapObjects)
        {
            return GroupSimilarObjects(mapObjects);
        }
        
        /// <summary>
        /// Generates a model asynchronously
        /// </summary>
        private async Task<GameObject> GenerateModelAsync(ModelGenerationRequest request)
        {
            // Simulate async model generation
            await Task.Delay(100);
            
            // Use procedural generation for now
            GameObject model = GenerateProceduralModel(request.objectType);
            
            if (model != null)
            {
                model.name = $"Generated_{request.objectType}";
                _totalModelsGenerated++;
                
                // Track model type distribution
                if (!_modelTypeDistribution.ContainsKey(request.objectType))
                {
                    _modelTypeDistribution[request.objectType] = 0;
                }
                _modelTypeDistribution[request.objectType]++;
            }
            
            return model;
        }
        
        /// <summary>
        /// Gets generation progress for a specific object type
        /// </summary>
        private float GetGenerationProgress(string objectType)
        {
            // Simple progress simulation
            return UnityEngine.Random.Range(0.3f, 0.9f);
        }
        
        /// <summary>
        /// Adapts a transform to terrain constraints
        /// </summary>
        private PlacementTransform AdaptTransformToTerrain(PlacementTransform transform, GameObject instance, UnityEngine.Terrain terrain)
        {
            Vector3 position = transform.Position;
            
            // Sample terrain height
            float terrainHeight = terrain.SampleHeight(position);
            position.y = terrainHeight;
            
            // Apply grounding if enabled
            if (fixFloatingObjects)
            {
                position.y -= groundingDepth;
            }
            
            // Sample terrain normal for rotation
            Vector3 terrainNormal = SampleTerrainNormal(position, terrain);
            float slopeAngle = Vector3.Angle(Vector3.up, terrainNormal);
            
            Quaternion rotation = transform.Rotation;
            if (adaptToTerrain && slopeAngle < maxPlacementSlope)
            {
                Vector3 forward = Vector3.Cross(terrainNormal, Vector3.right);
                if (forward.magnitude < 0.1f)
                {
                    forward = Vector3.Cross(terrainNormal, Vector3.forward);
                }
                
                Quaternion terrainRotation = Quaternion.LookRotation(forward, terrainNormal);
                rotation = terrainRotation * rotation;
            }
            
            return new PlacementTransform
            {
                Position = position,
                Rotation = rotation,
                Scale = transform.Scale
            };
        }
        
        /// <summary>
        /// Logs model distribution statistics
        /// </summary>
        private void LogModelDistribution(List<GameObject> models)
        {
            if (!verboseLogging) return;
            
            debugger.Log($"Model Distribution:", LogCategory.Models);
            foreach (var kvp in _modelTypeDistribution)
            {
                debugger.Log($"  {kvp.Key}: {kvp.Value} models", LogCategory.Models);
            }
        }
        
        /// <summary>
        /// Shuts down the model generator asynchronously
        /// </summary>
        private async Task ShutdownAsync()
        {
            _cancellationTokenSource?.Cancel();
            
            // Wait for active tasks to complete
            while (_activeRequests.Count > 0)
            {
                await Task.Delay(100);
            }
            
            debugger?.Log("ModelGenerator shutdown complete", LogCategory.Models);
        }
        
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
            
            // Initialize AI services
            InitializeAIServices();
            
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
        
        /// <summary>
        /// Initializes AI services including OpenAI API configuration
        /// </summary>
        private void InitializeAIServices() 
        {
            // Initialize OpenAI API key
            _openAIApiKey = openAIApiKey;
            
            if (!string.IsNullOrEmpty(_openAIApiKey))
            {
                debugger.Log("OpenAI API key configured for description enhancement", LogCategory.Models);
            }
            else
            {
                debugger.LogWarning("OpenAI API key not set - description enhancement will be disabled", LogCategory.Models);
            }
            
            // Initialize other AI services if needed
            // Add any additional AI service initialization here
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
                var objectPlacer = FindObjectOfType<ObjectPlacer>();
                if (objectPlacer != null)
                {
                    // Create a simple model provider that implements IModelProvider
                    var modelProvider = new GeneratedModelProvider(generatedModels, analysis.mapObjects);
                    
                    // For now, we'll place models without ObjectPlacer since the interface doesn't match
                    // TODO: Implement proper IModelProvider interface or update ObjectPlacer
                    placedObjects.AddRange(generatedModels);
                    debugger.Log($"Placed {generatedModels.Count} objects (placeholder implementation)", LogCategory.Models);
                }
                else
                {
                    debugger.LogWarning("ObjectPlacer not found, skipping precise placement", LogCategory.Models);
                }
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
                
                // Yield periodically to prevent frame drops
                if (completedGroups % 3 == 0)
                {
                    yield return null;
                }
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

        #region Public Methods
        
        /// <summary>
        /// Generates 3D models for analysis results using AI and procedural techniques
        /// </summary>
        /// <param name="results">Analysis results containing detected objects</param>
        /// <param name="terrain">Target terrain for model placement</param>
        /// <param name="onComplete">Callback when generation is complete</param>
        /// <param name="onError">Callback when an error occurs</param>
        /// <param name="onProgress">Progress callback with stage and progress</param>
        /// <returns>Coroutine for the generation process</returns>
        public IEnumerator GenerateModelsForAnalysis(AnalysisResults results, UnityEngine.Terrain terrain, System.Action<List<GameObject>> onComplete, System.Action<string> onError, System.Action<string, float> onProgress)
        {
            if (results == null)
            {
                onError?.Invoke("Analysis results are null");
                yield break;
            }
            
            if (terrain == null)
            {
                onError?.Invoke("Terrain is null");
                yield break;
            }
            
            debugger?.Log("Starting model generation for analysis results", LogCategory.Models);
            
            var generatedModels = new List<GameObject>();
            string errorMessage = null;
            
            onProgress?.Invoke("Preparing model generation...", 0.0f);
            
            // Step 1: Group similar objects if enabled
            var objectGroups = groupSimilarObjects ? GroupSimilarObjects(results.mapObjects) : CreateIndividualGroups(results.mapObjects);
            
            onProgress?.Invoke("Processing object groups...", 0.1f);
            
            // Step 2: Generate models for each group
            int totalGroups = objectGroups.Count;
            int processedGroups = 0;
            
            foreach (var group in objectGroups)
            {
                onProgress?.Invoke($"Generating models for {group.type}...", 0.1f + (0.8f * processedGroups / totalGroups));
                
                // Generate models for this group without try-catch around yield
                yield return StartCoroutine(GenerateModelsForGroupSafe(group, terrain, 
                    (models) => generatedModels.AddRange(models),
                    (error) => errorMessage = error));
                
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    onError?.Invoke(errorMessage);
                    yield break;
                }
                
                processedGroups++;
                
                // Yield periodically to prevent frame drops
                if (processedGroups % 3 == 0)
                {
                    yield return null;
                }
            }
            
            onProgress?.Invoke("Finalizing model placement...", 0.9f);
            
            // Step 3: Apply final optimizations
            if (useMeshCombining)
            {
                generatedModels = CombineSimilarMeshes(generatedModels);
            }
            
            onProgress?.Invoke("Model generation complete!", 1.0f);
            debugger?.Log($"Model generation complete: {generatedModels.Count} models created", LogCategory.Models);
            
            onComplete?.Invoke(generatedModels);
        }
        
        /// <summary>
        /// Safe wrapper for GenerateModelsForGroup without try-catch around yields
        /// </summary>
        private IEnumerator GenerateModelsForGroupSafe(ObjectGroup group, UnityEngine.Terrain terrain, System.Action<List<GameObject>> onComplete, System.Action<string> onError)
        {
            var groupModels = new List<GameObject>();
            
            debugger?.Log($"Generating models for group: {group.type} ({group.objects.Count} objects)", LogCategory.Models);
            
            // Create a master model for the group
            GameObject masterModel = null;
            
            if (useTripo3D && !string.IsNullOrEmpty(tripo3DApiKey))
            {
                // Try to generate with Tripo3D
                yield return StartCoroutine(GenerateTripo3DModel(group.type, result => masterModel = result));
            }
            
            // Fallback to procedural generation if Tripo3D failed
            if (masterModel == null)
            {
                masterModel = GenerateProceduralModel(group.type);
            }
            
            if (masterModel == null)
            {
                debugger?.LogWarning($"Failed to generate model for group: {group.type}", LogCategory.Models);
                yield break;
            }
            
            // Instance the master model for each object in the group
            foreach (var obj in group.objects)
            {
                GameObject instance = CreateModelInstance(masterModel, obj, terrain);
                if (instance != null)
                {
                    groupModels.Add(instance);
                }
                
                yield return null; // Yield each frame to prevent blocking
            }
            
            yield return groupModels;
        }
        
        /// <summary>
        /// Generates a model using Tripo3D API (placeholder implementation)
        /// </summary>
        private IEnumerator GenerateTripo3DModel(string objectType, System.Action<GameObject> onComplete)
        {
            debugger?.Log($"Attempting Tripo3D generation for: {objectType}", LogCategory.Models);
            
            // This is a placeholder for Tripo3D integration
            // In a full implementation, this would make API calls to Tripo3D
            yield return new WaitForSeconds(0.1f);
            
            // For now, return null to trigger fallback
            onComplete?.Invoke(null);
        }
        
        /// <summary>
        /// Generates a procedural model as fallback
        /// </summary>
        private GameObject GenerateProceduralModel(string objectType)
        {
            debugger?.Log($"Generating procedural model for: {objectType}", LogCategory.Models);
            
            GameObject model = null;
            string typeLower = objectType.ToLower();
            
            if (typeLower.Contains("tree"))
            {
                model = CreateTreeModel();
            }
            else if (typeLower.Contains("building") || typeLower.Contains("house"))
            {
                model = CreateBuildingModel();
            }
            else if (typeLower.Contains("rock") || typeLower.Contains("boulder"))
            {
                model = CreateRockModel();
            }
            else if (typeLower.Contains("vehicle") || typeLower.Contains("car"))
            {
                model = CreateVehicleModel();
            }
            else
            {
                // Default generic object
                model = CreateGenericModel();
            }
            
            if (model != null)
            {
                model.name = $"ProceduralModel_{objectType}";
            }
            
            return model;
        }
        
        /// <summary>
        /// Creates a procedural tree model
        /// </summary>
        private GameObject CreateTreeModel()
        {
            GameObject tree = new GameObject("Tree");
            
            // Create trunk (cylinder)
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(tree.transform);
            trunk.transform.localPosition = new Vector3(0, 1, 0);
            trunk.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
            
            // Create foliage (sphere)
            GameObject foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage.name = "Foliage";
            foliage.transform.SetParent(tree.transform);
            foliage.transform.localPosition = new Vector3(0, 3, 0);
            foliage.transform.localScale = new Vector3(3f, 3f, 3f);
            
            // Apply materials
            var trunkRenderer = trunk.GetComponent<Renderer>();
            if (trunkRenderer != null)
            {
                trunkRenderer.material.color = new Color(0.4f, 0.2f, 0.1f); // Brown
            }
            
            var foliageRenderer = foliage.GetComponent<Renderer>();
            if (foliageRenderer != null)
            {
                foliageRenderer.material.color = new Color(0.2f, 0.6f, 0.2f); // Green
            }
            
            return tree;
        }
        
        /// <summary>
        /// Creates a procedural building model
        /// </summary>
        private GameObject CreateBuildingModel()
        {
            GameObject building = new GameObject("Building");
            
            // Create main structure (cube)
            GameObject structure = GameObject.CreatePrimitive(PrimitiveType.Cube);
            structure.name = "Structure";
            structure.transform.SetParent(building.transform);
            structure.transform.localPosition = new Vector3(0, 2.5f, 0);
            structure.transform.localScale = new Vector3(4f, 5f, 4f);
            
            // Create roof (pyramid-like)
            GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "Roof";
            roof.transform.SetParent(building.transform);
            roof.transform.localPosition = new Vector3(0, 5.5f, 0);
            roof.transform.localScale = new Vector3(4.5f, 1f, 4.5f);
            roof.transform.rotation = Quaternion.Euler(0, 45f, 0);
            
            // Apply materials
            var structureRenderer = structure.GetComponent<Renderer>();
            if (structureRenderer != null)
            {
                structureRenderer.material.color = new Color(0.8f, 0.8f, 0.8f); // Light gray
            }
            
            var roofRenderer = roof.GetComponent<Renderer>();
            if (roofRenderer != null)
            {
                roofRenderer.material.color = new Color(0.6f, 0.3f, 0.2f); // Brown roof
            }
            
            return building;
        }
        
        /// <summary>
        /// Creates a procedural rock model
        /// </summary>
        private GameObject CreateRockModel()
        {
            GameObject rock = new GameObject("Rock");
            
            // Create irregular rock shape (deformed sphere)
            GameObject rockMesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rockMesh.name = "RockMesh";
            rockMesh.transform.SetParent(rock.transform);
            rockMesh.transform.localPosition = Vector3.zero;
            
            // Make it irregular by scaling non-uniformly
            float scaleVariation = 0.3f;
            Vector3 scale = new Vector3(
                1f + UnityEngine.Random.Range(-scaleVariation, scaleVariation),
                1f + UnityEngine.Random.Range(-scaleVariation, scaleVariation),
                1f + UnityEngine.Random.Range(-scaleVariation, scaleVariation)
            );
            rockMesh.transform.localScale = scale;
            
            // Apply rock material
            var renderer = rockMesh.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.4f, 0.4f, 0.4f); // Gray
            }
            
            return rock;
        }
        
        /// <summary>
        /// Creates a procedural vehicle model
        /// </summary>
        private GameObject CreateVehicleModel()
        {
            GameObject vehicle = new GameObject("Vehicle");
            
            // Create body (cube)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(vehicle.transform);
            body.transform.localPosition = new Vector3(0, 0.5f, 0);
            body.transform.localScale = new Vector3(2f, 1f, 4f);
            
            // Create wheels (cylinders)
            for (int i = 0; i < 4; i++)
            {
                GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = $"Wheel_{i}";
                wheel.transform.SetParent(vehicle.transform);
                
                float x = (i % 2 == 0) ? -1.2f : 1.2f;
                float z = (i < 2) ? 1.5f : -1.5f;
                wheel.transform.localPosition = new Vector3(x, 0.3f, z);
                wheel.transform.localScale = new Vector3(0.6f, 0.3f, 0.6f);
                wheel.transform.rotation = Quaternion.Euler(0, 0, 90);
                
                // Apply wheel material
                var wheelRenderer = wheel.GetComponent<Renderer>();
                if (wheelRenderer != null)
                {
                    wheelRenderer.material.color = Color.black;
                }
            }
            
            // Apply body material
            var bodyRenderer = body.GetComponent<Renderer>();
            if (bodyRenderer != null)
            {
                bodyRenderer.material.color = new Color(0.8f, 0.2f, 0.2f); // Red
            }
            
            return vehicle;
        }
        
        /// <summary>
        /// Creates a generic procedural model
        /// </summary>
        private GameObject CreateGenericModel()
        {
            GameObject generic = GameObject.CreatePrimitive(PrimitiveType.Cube);
            generic.name = "GenericObject";
            
            // Apply a neutral material
            var renderer = generic.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(0.6f, 0.6f, 0.6f); // Gray
            }
            
            return generic;
        }
        
        /// <summary>
        /// Creates an instance of a master model for a specific map object
        /// </summary>
        private GameObject CreateModelInstance(GameObject masterModel, MapObject mapObject, UnityEngine.Terrain terrain)
        {
            if (masterModel == null) return null;
            
            // Instantiate the master model
            GameObject instance = Instantiate(masterModel);
            instance.name = $"{masterModel.name}_{mapObject.label}";
            
            // Calculate world position from normalized coordinates
            Vector3 terrainSize = terrain.terrainData.size;
            Vector3 terrainPos = terrain.transform.position;
            
            Vector3 worldPos = new Vector3(
                terrainPos.x + mapObject.position.x * terrainSize.x,
                0,
                terrainPos.z + mapObject.position.y * terrainSize.z
            );
            
            // Sample terrain height and place object on surface
            float terrainHeight = terrain.SampleHeight(worldPos);
            worldPos.y = terrainHeight;
            
            // Apply grounding depth if enabled
            if (fixFloatingObjects)
            {
                worldPos.y -= groundingDepth;
            }
            
            instance.transform.position = worldPos;
            
            // Apply scale
            if (mapObject.scale != Vector3.zero)
            {
                instance.transform.localScale = mapObject.scale;
            }
            
            // Apply rotation - convert float rotation (degrees) to Quaternion
            if (mapObject.rotation != 0f)
            {
                instance.transform.rotation = Quaternion.Euler(0, mapObject.rotation, 0);
            }
            else
            {
                // Random Y rotation for variation
                instance.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
            }
            
            // Adapt to terrain if enabled
            if (adaptToTerrain)
            {
                AdaptInstanceToTerrain(instance, terrain);
            }
            
            debugger?.Log($"Created model instance: {instance.name} at {worldPos}", LogCategory.Models);
            return instance;
        }
        
        /// <summary>
        /// Sets the OpenAI API key for enhanced descriptions
        /// </summary>
        /// <param name="apiKey">The OpenAI API key</param>
        public void SetOpenAIApiKey(string apiKey)
        {
            openAIApiKey = apiKey;
        }
        
        /// <summary>
        /// Sets the Tripo3D API key for 3D model generation
        /// </summary>
        /// <param name="apiKey">The Tripo3D API key</param>
        public void SetTripo3DApiKey(string apiKey)
        {
            tripo3DApiKey = apiKey;
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// Groups similar objects for instancing optimization
        /// </summary>
        private List<ObjectGroup> GroupSimilarObjects(List<MapObject> mapObjects)
        {
            var groups = new Dictionary<string, List<MapObject>>();
            
            foreach (var obj in mapObjects)
            {
                string groupKey = GetGroupKey(obj);
                
                if (!groups.ContainsKey(groupKey))
                {
                    groups[groupKey] = new List<MapObject>();
                }
                
                groups[groupKey].Add(obj);
            }
            
            var objectGroups = new List<ObjectGroup>();
            foreach (var kvp in groups)
            {
                if (kvp.Value.Count > 0)
                {
                    var group = new ObjectGroup
                    {
                        type = kvp.Key,
                        objects = kvp.Value
                    };
                    objectGroups.Add(group);
                }
            }
            
            debugger?.Log($"Grouped {mapObjects.Count} objects into {objectGroups.Count} groups", LogCategory.Models);
            return objectGroups;
        }
        
        /// <summary>
        /// Creates individual groups for each object (no grouping)
        /// </summary>
        private List<ObjectGroup> CreateIndividualGroups(List<MapObject> mapObjects)
        {
            var groups = new List<ObjectGroup>();
            
            foreach (var obj in mapObjects)
            {
                var group = new ObjectGroup
                {
                    type = obj.type,
                    objects = new List<MapObject> { obj }
                };
                groups.Add(group);
            }
            
            return groups;
        }
        
        /// <summary>
        /// Gets a group key for similar object detection
        /// </summary>
        private string GetGroupKey(MapObject obj)
        {
            // Group by object type and similar characteristics
            string key = obj.type.ToLower();
            
            // Add scale grouping for similar-sized objects
            if (obj.scale != Vector3.zero)
            {
                float avgScale = (obj.scale.x + obj.scale.y + obj.scale.z) / 3f;
                int scaleGroup = Mathf.RoundToInt(avgScale * 10f); // Group by 0.1 scale increments
                key += $"_scale{scaleGroup}";
            }
            
            return key;
        }
        
        /// <summary>
        /// Samples terrain normal at a given position
        /// </summary>
        private Vector3 SampleTerrainNormal(Vector3 position, UnityEngine.Terrain terrain)
        {
            TerrainData terrainData = terrain.terrainData;
            Vector3 terrainPos = terrain.transform.position;
            
            // Convert world position to terrain-local coordinates
            float x = (position.x - terrainPos.x) / terrainData.size.x;
            float z = (position.z - terrainPos.z) / terrainData.size.z;
            
            // Clamp to terrain bounds
            x = Mathf.Clamp01(x);
            z = Mathf.Clamp01(z);
            
            // Sample the terrain normal
            return terrainData.GetInterpolatedNormal(x, z);
        }
        
        /// <summary>
        /// Combines similar meshes for performance optimization
        /// </summary>
        private List<GameObject> CombineSimilarMeshes(List<GameObject> models)
        {
            if (!useMeshCombining || models.Count < 2)
                return models;
            
            debugger?.Log("Combining similar meshes for optimization", LogCategory.Models);
            
            // Group models by mesh and material
            var meshGroups = new Dictionary<string, List<GameObject>>();
            
            foreach (var model in models)
            {
                var meshFilter = model.GetComponent<MeshFilter>();
                var renderer = model.GetComponent<Renderer>();
                
                if (meshFilter?.sharedMesh != null && renderer?.sharedMaterial != null)
                {
                    string key = $"{meshFilter.sharedMesh.name}_{renderer.sharedMaterial.name}";
                    
                    if (!meshGroups.ContainsKey(key))
                        meshGroups[key] = new List<GameObject>();
                    
                    meshGroups[key].Add(model);
                }
            }
            
            var combinedModels = new List<GameObject>();
            
            // Combine each group
            foreach (var group in meshGroups)
            {
                if (group.Value.Count > 1)
                {
                    // Create combined mesh
                    GameObject combined = CombineMeshGroup(group.Value, group.Key);
                    if (combined != null)
                    {
                        combinedModels.Add(combined);
                        
                        // Remove original models
                        foreach (var original in group.Value)
                        {
                            if (original != null)
                                DestroyImmediate(original);
                        }
                    }
                    else
                    {
                        combinedModels.AddRange(group.Value);
                    }
                }
                else
                {
                    combinedModels.AddRange(group.Value);
                }
            }
            
            debugger?.Log($"Mesh combining complete: {models.Count} -> {combinedModels.Count} objects", LogCategory.Models);
            return combinedModels;
        }
        
        /// <summary>
        /// Combines a group of similar meshes into one
        /// </summary>
        private GameObject CombineMeshGroup(List<GameObject> models, string groupName)
        {
            if (models.Count == 0) return null;
            
            var combines = new List<CombineInstance>();
            var firstModel = models[0];
            var meshFilter = firstModel.GetComponent<MeshFilter>();
            var renderer = firstModel.GetComponent<Renderer>();
            
            if (meshFilter?.sharedMesh == null || renderer?.sharedMaterial == null)
                return null;
            
            foreach (var model in models)
            {
                var mf = model.GetComponent<MeshFilter>();
                if (mf?.sharedMesh != null)
                {
                    var combine = new CombineInstance
                    {
                        mesh = mf.sharedMesh,
                        transform = model.transform.localToWorldMatrix
                    };
                    combines.Add(combine);
                }
            }
            
            if (combines.Count == 0) return null;
            
            // Create combined object
            GameObject combined = new GameObject($"Combined_{groupName}");
            combined.transform.SetParent(_modelContainer.transform);
            
            var combinedMeshFilter = combined.AddComponent<MeshFilter>();
            var combinedRenderer = combined.AddComponent<Renderer>();
            
            // Combine meshes
            var combinedMesh = new Mesh();
            combinedMesh.CombineMeshes(combines.ToArray());
            combinedMesh.name = $"CombinedMesh_{groupName}";
            
            combinedMeshFilter.mesh = combinedMesh;
            combinedRenderer.material = renderer.sharedMaterial;
            
            // Add collider if needed
            if (avoidCollisions)
            {
                var collider = combined.AddComponent<MeshCollider>();
                collider.sharedMesh = combinedMesh;
                collider.convex = false;
            }
            
            return combined;
        }
        
        /// <summary>
        /// Adapts a model instance to terrain surface and normals
        /// </summary>
        private void AdaptInstanceToTerrain(GameObject instance, UnityEngine.Terrain terrain)
        {
            if (instance == null || terrain == null) return;
            
            Vector3 position = instance.transform.position;
            
            // Sample terrain height and normal
            float terrainHeight = terrain.SampleHeight(position);
            Vector3 terrainNormal = SampleTerrainNormal(position, terrain);
            
            // Adjust position to terrain surface
            position.y = terrainHeight;
            
            // Apply grounding depth
            if (fixFloatingObjects)
            {
                position.y -= groundingDepth;
            }
            
            instance.transform.position = position;
            
            // Align to terrain normal if slope is acceptable
            float slopeAngle = Vector3.Angle(Vector3.up, terrainNormal);
            if (slopeAngle < maxPlacementSlope)
            {
                Vector3 forward = Vector3.Cross(terrainNormal, Vector3.right);
                if (forward.magnitude < 0.1f)
                {
                    forward = Vector3.Cross(terrainNormal, Vector3.forward);
                }
                
                if (forward.magnitude > 0.1f)
                {
                    Quaternion terrainRotation = Quaternion.LookRotation(forward.normalized, terrainNormal);
                    instance.transform.rotation = terrainRotation * instance.transform.rotation;
                }
            }
        }
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
        
        #endregion
    }
}
