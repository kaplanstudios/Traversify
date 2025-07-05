/*************************************************************************
 *  Traversify – ModelGenerator.cs                                       *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Updated: 2025-07-05                                                  *
 *  Desc   : Advanced model generation system with optimized 3D model    *
 *           creation, transformation, and terrain placement.            *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Traversify.Core;
using Traversify.AI;
using Traversify.Terrain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Traversify.Models {
    /// <summary>
    /// Generates and manages 3D models from analysis results with
    /// optimized placement, transformation, and instancing.
    /// </summary>
    [RequireComponent(typeof(TraversifyDebugger))]
    public class ModelGenerator : TraversifyComponent {
        #region Singleton Pattern
        
        private static ModelGenerator _instance;
        
        /// <summary>
        /// Singleton instance of the ModelGenerator.
        /// </summary>
        public static ModelGenerator Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<ModelGenerator>();
                    if (_instance == null) {
                        GameObject go = new GameObject("ModelGenerator");
                        _instance = go.AddComponent<ModelGenerator>();
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Fields
        
        [Header("Generation Settings")]
        [Tooltip("API key for Tripo3D service")]
        [SerializeField] private string _tripo3DApiKey = "";
        
        [Tooltip("Base URL for Tripo3D API")]
        [SerializeField] private string _tripo3DBaseUrl = "https://api.tripo3d.ai/v1/";
        
        [Tooltip("Default model scale")]
        [SerializeField] private Vector3 _defaultModelScale = Vector3.one;
        
        [Tooltip("Maximum concurrent generation requests")]
        [Range(1, 10)]
        [SerializeField] private int _maxConcurrentRequests = 3;
        
        [Tooltip("Default material for models")]
        [SerializeField] private Material _defaultMaterial;
        
        [Tooltip("Prefer using cached models when possible")]
        [SerializeField] private bool _useModelCache = true;
        
        [Header("Optimization Settings")]
        [Tooltip("Use instancing for similar objects")]
        [SerializeField] private bool _useInstancing = true;
        
        [Tooltip("Similarity threshold for instancing (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float _similarityThreshold = 0.8f;
        
        [Tooltip("Automatically optimize models")]
        [SerializeField] private bool _autoOptimizeModels = true;
        
        [Tooltip("Level of detail reduction for optimized models")]
        [Range(0f, 1f)]
        [SerializeField] private float _lodReductionFactor = 0.5f;
        
        [Tooltip("Maximum polygon count per model")]
        [SerializeField] private int _maxPolyCount = 10000;
        
        [Header("Placement Settings")]
        [Tooltip("Parent object for generated models")]
        [SerializeField] private Transform _modelsParent;
        
        [Tooltip("Terrain reference for placement")]
        [SerializeField] private UnityEngine.Terrain _terrain;
        
        [Tooltip("Y-offset for model placement")]
        [SerializeField] private float _placementYOffset = 0f;
        
        [Tooltip("Place objects on terrain surface")]
        [SerializeField] private bool _placeOnTerrain = true;
        
        [Tooltip("Align models with terrain normal")]
        [SerializeField] private bool _alignWithTerrain = true;
        
        [Tooltip("Add colliders to models")]
        [SerializeField] private bool _addColliders = true;
        
        [Header("Fallback Models")]
        [Tooltip("Fallback models for common object types")]
        [SerializeField] private List<FallbackModelMapping> _fallbackModels = new List<FallbackModelMapping>();
        
        #endregion
        
        #region Public Properties
        
        /// <summary>
        /// API key for Tripo3D service.
        /// </summary>
        public string tripo3DApiKey {
            get => _tripo3DApiKey;
            set => _tripo3DApiKey = value;
        }
        
        /// <summary>
        /// Whether to use instancing for similar objects.
        /// </summary>
        public bool useInstancing {
            get => _useInstancing;
            set => _useInstancing = value;
        }
        
        /// <summary>
        /// Whether to place objects on terrain surface.
        /// </summary>
        public bool placeOnTerrain {
            get => _placeOnTerrain;
            set => _placeOnTerrain = value;
        }
        
        /// <summary>
        /// Whether to align models with terrain normal.
        /// </summary>
        public bool alignWithTerrain {
            get => _alignWithTerrain;
            set => _alignWithTerrain = value;
        }
        
        /// <summary>
        /// Whether to add colliders to models.
        /// </summary>
        public bool addColliders {
            get => _addColliders;
            set => _addColliders = value;
        }
        
        /// <summary>
        /// Status of current generation.
        /// </summary>
        public GenerationStatus status { get; private set; }
        
        /// <summary>
        /// Progress of current generation (0-1).
        /// </summary>
        public float progress { get; private set; }
        
        /// <summary>
        /// Current generation message.
        /// </summary>
        public string statusMessage { get; private set; }
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Model generation status.
        /// </summary>
        public enum GenerationStatus {
            Idle,
            Preparing,
            Generating,
            Downloading,
            Placing,
            Completed,
            Failed
        }
        
        /// <summary>
        /// Model generation request data.
        /// </summary>
        [System.Serializable]
        public class ModelGenerationRequest {
            public string objectType;
            public string description;
            public string style;
            public List<string> tags;
            public int objectId;
            public bool isManMade;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
            public Dictionary<string, object> metadata;
        }
        
        /// <summary>
        /// Model generation response data.
        /// </summary>
        [System.Serializable]
        public class ModelGenerationResponse {
            public string modelId;
            public string downloadUrl;
            public string thumbnailUrl;
            public string status;
            public float progress;
            public string message;
            public int polyCount;
            public int objectId;
            public Dictionary<string, object> metadata;
        }
        
        /// <summary>
        /// Fallback model mapping.
        /// </summary>
        [System.Serializable]
        public class FallbackModelMapping {
            public string objectType;
            public GameObject modelPrefab;
            public List<string> aliases = new List<string>();
        }
        
        /// <summary>
        /// Generated model data.
        /// </summary>
        [System.Serializable]
        public class GeneratedModel {
            public int objectId;
            public string objectType;
            public GameObject modelObject;
            public Vector3 originalPosition;
            public Vector3 originalRotation;
            public Vector3 originalScale;
            public Mesh mesh;
            public Material material;
            public bool isInstance;
            public int instanceSourceId;
            public string modelId;
            public Dictionary<string, object> metadata;
        }
        
        /// <summary>
        /// Object similarity group for instancing.
        /// </summary>
        private class SimilarityGroup {
            public int sourceObjectId;
            public string objectType;
            public List<int> similarObjectIds = new List<int>();
            public Vector3 averageScale = Vector3.one;
            public GeneratedModel sourceModel;
        }
        
        #endregion
        #region Private Fields
        
        private TraversifyDebugger _debugger;
        private Dictionary<int, GeneratedModel> _generatedModels = new Dictionary<int, GeneratedModel>();
        private Dictionary<string, GameObject> _modelCache = new Dictionary<string, GameObject>();
        private Dictionary<int, ModelGenerationRequest> _pendingRequests = new Dictionary<int, ModelGenerationRequest>();
        private Dictionary<int, ModelGenerationResponse> _pendingResponses = new Dictionary<int, ModelGenerationResponse>();
        private List<int> _generationQueue = new List<int>();
        private List<int> _activeGenerations = new List<int>();
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isGenerating = false;
        private int _processedCount = 0;
        private int _totalCount = 0;
        private float _startTime = 0f;
        private List<SimilarityGroup> _similarityGroups = new List<SimilarityGroup>();
        private GameObject _modelsContainer;
        
        // Events
        public event Action<GeneratedModel> OnModelGenerated;
        public event Action<string, float> OnGenerationProgress;
        public event Action<string> OnGenerationComplete;
        public event Action<string> OnGenerationFailed;
        
        #endregion
        
        #region Initialization
        
        protected override bool OnInitialize(object config) {
            try {
                _debugger = GetComponent<TraversifyDebugger>();
                if (_debugger == null) {
                    _debugger = gameObject.AddComponent<TraversifyDebugger>();
                }
                
                // Apply config if provided
                if (config != null) {
                    ApplyConfiguration(config);
                }
                
                // Create models parent if not set
                if (_modelsParent == null) {
                    _modelsContainer = new GameObject("GeneratedModels");
                    _modelsParent = _modelsContainer.transform;
                }
                
                // Initialize status
                status = GenerationStatus.Idle;
                progress = 0f;
                statusMessage = "Ready";
                
                Log("ModelGenerator initialized successfully", LogCategory.Models);
                return true;
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to initialize ModelGenerator: {ex.Message}");
                return false;
            }
        }
        
        private void Awake() {
            if (_instance == null) {
                _instance = this;
                if (transform.root == gameObject) {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this) {
                Destroy(gameObject);
                return;
            }
            
            if (!IsInitialized) {
                Initialize();
            }
        }
        
        private void OnDestroy() {
            // Cancel any pending operations
            if (_cancellationTokenSource != null) {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }
        
        /// <summary>
        /// Apply configuration from object.
        /// </summary>
        private void ApplyConfiguration(object config) {
            // Handle dictionary config
            if (config is Dictionary<string, object> configDict) {
                // Extract API key
                if (configDict.TryGetValue("tripo3DApiKey", out object apiKeyObj) && apiKeyObj is string apiKey) {
                    _tripo3DApiKey = apiKey;
                }
                
                // Extract model scale
                if (configDict.TryGetValue("defaultModelScale", out object scaleObj) && scaleObj is Vector3 scale) {
                    _defaultModelScale = scale;
                }
                
                // Extract instancing settings
                if (configDict.TryGetValue("useInstancing", out object instancingObj) && instancingObj is bool instancing) {
                    _useInstancing = instancing;
                }
                
                // Extract placement settings
                if (configDict.TryGetValue("placeOnTerrain", out object placeObj) && placeObj is bool place) {
                    _placeOnTerrain = place;
                }
                
                // Extract terrain reference
                if (configDict.TryGetValue("terrain", out object terrainObj) && terrainObj is UnityEngine.Terrain terrain) {
                    _terrain = terrain;
                }
            }
        }
        
        /// <summary>
        /// Set terrain reference for placement.
        /// </summary>
        public void SetTerrain(UnityEngine.Terrain terrain) {
            _terrain = terrain;
        }
        
        #endregion
        
        #region Model Generation API
        
        /// <summary>
        /// Generate models from analysis results.
        /// </summary>
        public IEnumerator GenerateModelsFromAnalysis(
            AnalysisResults results,
            System.Action<List<GeneratedModel>> onComplete = null,
            System.Action<string> onError = null,
            System.Action<string, float> onProgress = null)
        {
            if (results == null) {
                onError?.Invoke("Analysis results are null");
                yield break;
            }
            
            if (_isGenerating) {
                onError?.Invoke("Model generation already in progress");
                yield break;
            }
            
            try {
                _isGenerating = true;
                status = GenerationStatus.Preparing;
                progress = 0f;
                statusMessage = "Preparing for model generation";
                onProgress?.Invoke(statusMessage, progress);
                
                _startTime = Time.realtimeSinceStartup;
                
                // Reset state
                _generatedModels.Clear();
                _pendingRequests.Clear();
                _pendingResponses.Clear();
                _generationQueue.Clear();
                _activeGenerations.Clear();
                _processedCount = 0;
                
                // Create new cancellation token
                if (_cancellationTokenSource != null) {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                }
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Process objects for model generation
                List<MapObject> objectsToGenerate = new List<MapObject>();
                
                if (results.mapObjects != null && results.mapObjects.Count > 0) {
                    // Use map objects if available
                    objectsToGenerate.AddRange(results.mapObjects);
                }
                else if (results.detectedObjects != null && results.detectedObjects.Count > 0) {
                    // Convert detected objects to map objects
                    foreach (var obj in results.detectedObjects) {
                        if (!obj.isTerrain) {
                            MapObject mapObj = ConvertDetectedObjectToMapObject(obj);
                            objectsToGenerate.Add(mapObj);
                        }
                    }
                }
                
                _totalCount = objectsToGenerate.Count;
                
                if (_totalCount == 0) {
                    _isGenerating = false;
                    status = GenerationStatus.Completed;
                    progress = 1f;
                    statusMessage = "No objects to generate";
                    onProgress?.Invoke(statusMessage, progress);
                    onComplete?.Invoke(new List<GeneratedModel>());
                    yield break;
                }
                
                Log($"Preparing to generate {_totalCount} models", LogCategory.Models);
                
                // Group similar objects if instancing is enabled
                if (_useInstancing) {
                    yield return StartCoroutine(GroupSimilarObjects(objectsToGenerate));
                    onProgress?.Invoke("Grouped similar objects for instancing", 0.1f);
                }
                else {
                    // Create generation requests for all objects
                    foreach (var obj in objectsToGenerate) {
                        CreateGenerationRequest(obj);
                    }
                }
                
                // Start generation process
                status = GenerationStatus.Generating;
                progress = 0.1f;
                statusMessage = "Generating models";
                onProgress?.Invoke(statusMessage, progress);
                
                // Process generation queue
                yield return StartCoroutine(ProcessGenerationQueue(onProgress));
                
                // Check if generation was canceled
                if (_cancellationTokenSource.IsCancellationRequested) {
                    _isGenerating = false;
                    status = GenerationStatus.Failed;
                    progress = 0f;
                    statusMessage = "Model generation canceled";
                    onProgress?.Invoke(statusMessage, progress);
                    onError?.Invoke("Model generation was canceled");
                    yield break;
                }
                
                // Place models on terrain
                status = GenerationStatus.Placing;
                progress = 0.9f;
                statusMessage = "Placing models on terrain";
                onProgress?.Invoke(statusMessage, progress);
                
                PlaceModelsOnTerrain();
                
                // Complete generation
                float totalTime = Time.realtimeSinceStartup - _startTime;
                _isGenerating = false;
                status = GenerationStatus.Completed;
                progress = 1f;
                statusMessage = $"Generated {_generatedModels.Count} models in {totalTime:F2} seconds";
                
                Log(statusMessage, LogCategory.Models);
                onProgress?.Invoke(statusMessage, progress);
                
                // Return generated models
                List<GeneratedModel> generatedModels = _generatedModels.Values.ToList();
                onComplete?.Invoke(generatedModels);
                
                // Trigger event
                OnGenerationComplete?.Invoke(statusMessage);
            }
            catch (Exception ex) {
                _isGenerating = false;
                status = GenerationStatus.Failed;
                progress = 0f;
                statusMessage = $"Error during model generation: {ex.Message}";
                
                LogError(statusMessage, LogCategory.Models);
                onProgress?.Invoke(statusMessage, progress);
                onError?.Invoke(statusMessage);
                
                // Trigger event
                OnGenerationFailed?.Invoke(statusMessage);
            }
        }
        /// <summary>
        /// Generate a single model.
        /// </summary>
        public IEnumerator GenerateModel(
            ModelGenerationRequest request,
            System.Action<GeneratedModel> onComplete = null,
            System.Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(_tripo3DApiKey)) {
                onError?.Invoke("Tripo3D API key not provided");
                yield break;
            }
            
            try {
                // Create generation request
                int objectId = request.objectId;
                _pendingRequests[objectId] = request;
                
                // Try to use fallback model first
                GameObject fallbackModel = GetFallbackModel(request.objectType);
                if (fallbackModel != null) {
                    GeneratedModel model = CreateModelFromFallback(request, fallbackModel);
                    
                    // Apply transformations
                    TransformModel(model, request.position, request.rotation, request.scale);
                    
                    // Add to generated models
                    _generatedModels[objectId] = model;
                    
                    // Complete
                    onComplete?.Invoke(model);
                    yield break;
                }
                
                // Send request to Tripo3D
                string requestJson = PrepareGenerationRequestJson(request);
                
                using (UnityWebRequest webRequest = new UnityWebRequest($"{_tripo3DBaseUrl}models", "POST")) {
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
                    webRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {_tripo3DApiKey}");
                    
                    yield return webRequest.SendWebRequest();
                    
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        throw new Exception($"API request failed: {webRequest.error}");
                    }
                    
                    // Parse response
                    string responseJson = webRequest.downloadHandler.text;
                    ModelGenerationResponse response = JsonConvert.DeserializeObject<ModelGenerationResponse>(responseJson);
                    
                    if (response == null) {
                        throw new Exception("Failed to parse API response");
                    }
                    
                    // Store response
                    response.objectId = objectId;
                    _pendingResponses[objectId] = response;
                    
                    // Wait for model generation to complete
                    while (response.status != "completed") {
                        if (response.status == "failed") {
                            throw new Exception($"Model generation failed: {response.message}");
                        }
                        
                        // Check status
                        yield return StartCoroutine(CheckGenerationStatus(objectId));
                        
                        // Get updated response
                        response = _pendingResponses[objectId];
                        
                        // Wait before checking again
                        yield return new WaitForSeconds(2f);
                    }
                    
                    // Download model
                    GameObject modelObject = null;
                    yield return StartCoroutine(DownloadModel(response.downloadUrl, (GameObject obj) => {
                        modelObject = obj;
                    }, onError));
                    
                    if (modelObject == null) {
                        throw new Exception("Failed to download model");
                    }
                    
                    // Create generated model
                    GeneratedModel generatedModel = new GeneratedModel {
                        objectId = objectId,
                        objectType = request.objectType,
                        modelObject = modelObject,
                        originalPosition = request.position,
                        originalRotation = request.rotation,
                        originalScale = request.scale,
                        mesh = modelObject.GetComponentInChildren<MeshFilter>()?.sharedMesh,
                        material = modelObject.GetComponentInChildren<MeshRenderer>()?.sharedMaterial,
                        isInstance = false,
                        instanceSourceId = -1,
                        modelId = response.modelId,
                        metadata = request.metadata
                    };
                    
                    // Apply transformations
                    TransformModel(generatedModel, request.position, request.rotation, request.scale);
                    
                    // Add to generated models
                    _generatedModels[objectId] = generatedModel;
                    
                    // Add to model cache
                    if (_useModelCache) {
                        _modelCache[response.modelId] = modelObject;
                    }
                    
                    // Complete
                    onComplete?.Invoke(generatedModel);
                }
            }
            catch (Exception ex) {
                LogError($"Error generating model: {ex.Message}", LogCategory.Models);
                onError?.Invoke($"Model generation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clear all generated models.
        /// </summary>
        public void ClearGeneratedModels() {
            // Destroy model objects
            foreach (var model in _generatedModels.Values) {
                if (model.modelObject != null && !model.isInstance) {
                    Destroy(model.modelObject);
                }
            }
            
            // Clear collections
            _generatedModels.Clear();
            _pendingRequests.Clear();
            _pendingResponses.Clear();
            _generationQueue.Clear();
            _activeGenerations.Clear();
            _similarityGroups.Clear();
            
            // Reset state
            _processedCount = 0;
            _totalCount = 0;
            status = GenerationStatus.Idle;
            progress = 0f;
            statusMessage = "Ready";
            
            Log("Cleared all generated models", LogCategory.Models);
        }
        
        #endregion
        
        #region Model Generation Process
        
        /// <summary>
        /// Group similar objects for instancing.
        /// </summary>
        private IEnumerator GroupSimilarObjects(List<MapObject> objects) {
            _similarityGroups.Clear();
            
            // First pass: Create initial groups based on object type
            Dictionary<string, List<MapObject>> typeGroups = new Dictionary<string, List<MapObject>>();
            
            foreach (var obj in objects) {
                string objType = obj.objectType.ToLower();
                
                if (!typeGroups.ContainsKey(objType)) {
                    typeGroups[objType] = new List<MapObject>();
                }
                
                typeGroups[objType].Add(obj);
            }
            
            // Second pass: Create similarity groups
            foreach (var typeGroup in typeGroups) {
                if (typeGroup.Value.Count <= 1) {
                    // Only one object of this type, no instancing needed
                    var obj = typeGroup.Value[0];
                    CreateGenerationRequest(obj);
                    continue;
                }
                
                // Check descriptions for similarity
                List<List<MapObject>> similarGroups = new List<List<MapObject>>();
                
                foreach (var obj in typeGroup.Value) {
                    bool addedToGroup = false;
                    
                    // Try to add to existing group
                    foreach (var group in similarGroups) {
                        if (group.Count > 0 && IsSimilarObject(obj, group[0])) {
                            group.Add(obj);
                            addedToGroup = true;
                            break;
                        }
                    }
                    
                    // Create new group if not added
                    if (!addedToGroup) {
                        similarGroups.Add(new List<MapObject> { obj });
                    }
                }
                
                // Create generation requests for each group
                foreach (var group in similarGroups) {
                    if (group.Count == 1) {
                        // Single object
                        CreateGenerationRequest(group[0]);
                    }
                    else {
                        // Create similarity group for instancing
                        SimilarityGroup simGroup = new SimilarityGroup {
                            sourceObjectId = group[0].id,
                            objectType = group[0].objectType
                        };
                        
                        // Calculate average scale
                        Vector3 totalScale = Vector3.zero;
                        foreach (var obj in group) {
                            totalScale += obj.scale;
                            simGroup.similarObjectIds.Add(obj.id);
                        }
                        simGroup.averageScale = totalScale / group.Count;
                        
                        // Create generation request for source object
                        CreateGenerationRequest(group[0], simGroup.averageScale);
                        
                        // Add to similarity groups
                        _similarityGroups.Add(simGroup);
                    }
                }
                
                // Yield to prevent blocking
                yield return null;
            }
            
            Log($"Created {_pendingRequests.Count} generation requests from {objects.Count} objects", LogCategory.Models);
            Log($"Using {_similarityGroups.Count} similarity groups for instancing", LogCategory.Models);
            
            // Add all requests to generation queue
            _generationQueue.AddRange(_pendingRequests.Keys);
        }
        
        /// <summary>
        /// Process the generation queue.
        /// </summary>
        private IEnumerator ProcessGenerationQueue(System.Action<string, float> onProgress = null) {
            if (_generationQueue.Count == 0) {
                yield break;
            }
            
            // Process queue
            while (_generationQueue.Count > 0 || _activeGenerations.Count > 0) {
                // Start new generations up to max concurrent limit
                while (_generationQueue.Count > 0 && _activeGenerations.Count < _maxConcurrentRequests) {
                    int objectId = _generationQueue[0];
                    _generationQueue.RemoveAt(0);
                    
                    if (_pendingRequests.TryGetValue(objectId, out ModelGenerationRequest request)) {
                        // Add to active generations
                        _activeGenerations.Add(objectId);
                        
                        // Start generation
                        StartCoroutine(GenerateModelAndInstance(request, (GeneratedModel model) => {
                            // Add to generated models
                            _generatedModels[objectId] = model;
                            
                            // Remove from active generations
                            _activeGenerations.Remove(objectId);
                            
                            // Update progress
                            _processedCount++;
                            UpdateProgress(onProgress);
                            
                            // Create instances if this is a source object
                            if (_useInstancing) {
                                var simGroup = _similarityGroups.Find(g => g.sourceObjectId == objectId);
                                if (simGroup != null) {
                                    simGroup.sourceModel = model;
                                    CreateInstancesForGroup(simGroup);
                                }
                            }
                            
                            // Trigger event
                            OnModelGenerated?.Invoke(model);
                        }));
                    }
                }
                
                // Wait before checking again
                yield return new WaitForSeconds(0.2f);
                
                // Check if canceled
                if (_cancellationTokenSource.IsCancellationRequested) {
                    break;
                }
            }
        }
        /// <summary>
        /// Generate model and handle instancing.
        /// </summary>
        private IEnumerator GenerateModelAndInstance(ModelGenerationRequest request, System.Action<GeneratedModel> onComplete) {
            GeneratedModel model = null;
            
            // Try to use fallback model first
            GameObject fallbackModel = GetFallbackModel(request.objectType);
            if (fallbackModel != null) {
                model = CreateModelFromFallback(request, fallbackModel);
            }
            else {
                // Try to use existing model from cache
                bool usedCache = false;
                if (_useModelCache) {
                    usedCache = TryUseModelFromCache(request, (GeneratedModel cachedModel) => {
                        model = cachedModel;
                    });
                }
                
                // Generate new model if not using cache
                if (!usedCache) {
                    // Skip model generation if API key is not provided
                    if (string.IsNullOrEmpty(_tripo3DApiKey)) {
                        model = CreateDefaultModel(request);
                    }
                    else {
                        // Generate model using Tripo3D
                        yield return StartCoroutine(GenerateModelWithTripo3D(request, (GeneratedModel generatedModel) => {
                            model = generatedModel;
                        }, (string error) => {
                            LogWarning($"Error generating model: {error}. Using default model.", LogCategory.Models);
                            model = CreateDefaultModel(request);
                        }));
                    }
                }
            }
            
            // Apply transformations
            if (model != null) {
                TransformModel(model, request.position, request.rotation, request.scale);
                
                // Add collider if needed
                if (_addColliders && model.modelObject != null) {
                    AddColliderToModel(model.modelObject);
                }
                
                // Complete
                onComplete?.Invoke(model);
            }
            else {
                // Fallback to default model
                model = CreateDefaultModel(request);
                TransformModel(model, request.position, request.rotation, request.scale);
                onComplete?.Invoke(model);
            }
        }
        
        /// <summary>
        /// Generate model using Tripo3D API.
        /// </summary>
        private IEnumerator GenerateModelWithTripo3D(
            ModelGenerationRequest request,
            System.Action<GeneratedModel> onComplete,
            System.Action<string> onError)
        {
            try {
                int objectId = request.objectId;
                
                // Send request to Tripo3D
                string requestJson = PrepareGenerationRequestJson(request);
                
                using (UnityWebRequest webRequest = new UnityWebRequest($"{_tripo3DBaseUrl}models", "POST")) {
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
                    webRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {_tripo3DApiKey}");
                    
                    yield return webRequest.SendWebRequest();
                    
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        throw new Exception($"API request failed: {webRequest.error}");
                    }
                    
                    // Parse response
                    string responseJson = webRequest.downloadHandler.text;
                    ModelGenerationResponse response = JsonConvert.DeserializeObject<ModelGenerationResponse>(responseJson);
                    
                    if (response == null) {
                        throw new Exception("Failed to parse API response");
                    }
                    
                    // Store response
                    response.objectId = objectId;
                    _pendingResponses[objectId] = response;
                    
                    // Wait for model generation to complete
                    while (response.status != "completed") {
                        if (response.status == "failed") {
                            throw new Exception($"Model generation failed: {response.message}");
                        }
                        
                        // Check status
                        yield return StartCoroutine(CheckGenerationStatus(objectId));
                        
                        // Get updated response
                        response = _pendingResponses[objectId];
                        
                        // Wait before checking again
                        yield return new WaitForSeconds(2f);
                        
                        // Check if canceled
                        if (_cancellationTokenSource.IsCancellationRequested) {
                            throw new Exception("Model generation was canceled");
                        }
                    }
                    
                    // Download model
                    GameObject modelObject = null;
                    status = GenerationStatus.Downloading;
                    yield return StartCoroutine(DownloadModel(response.downloadUrl, (GameObject obj) => {
                        modelObject = obj;
                    }, onError));
                    
                    if (modelObject == null) {
                        throw new Exception("Failed to download model");
                    }
                    
                    // Optimize model if needed
                    if (_autoOptimizeModels) {
                        OptimizeModel(modelObject);
                    }
                    
                    // Create generated model
                    GeneratedModel generatedModel = new GeneratedModel {
                        objectId = objectId,
                        objectType = request.objectType,
                        modelObject = modelObject,
                        originalPosition = request.position,
                        originalRotation = request.rotation,
                        originalScale = request.scale,
                        mesh = modelObject.GetComponentInChildren<MeshFilter>()?.sharedMesh,
                        material = modelObject.GetComponentInChildren<MeshRenderer>()?.sharedMaterial,
                        isInstance = false,
                        instanceSourceId = -1,
                        modelId = response.modelId,
                        metadata = request.metadata
                    };
                    
                    // Add to model cache
                    if (_useModelCache) {
                        _modelCache[response.modelId] = modelObject;
                    }
                    
                    // Set name
                    modelObject.name = $"{request.objectType}_{objectId}";
                    
                    // Parent to models container
                    if (_modelsParent != null) {
                        modelObject.transform.SetParent(_modelsParent);
                    }
                    
                    // Complete
                    onComplete?.Invoke(generatedModel);
                }
            }
            catch (Exception ex) {
                LogError($"Error generating model with Tripo3D: {ex.Message}", LogCategory.Models);
                onError?.Invoke(ex.Message);
            }
        }
        
        /// <summary>
        /// Check status of model generation.
        /// </summary>
        private IEnumerator CheckGenerationStatus(int objectId) {
            if (!_pendingResponses.TryGetValue(objectId, out ModelGenerationResponse response)) {
                yield break;
            }
            
            try {
                using (UnityWebRequest webRequest = new UnityWebRequest($"{_tripo3DBaseUrl}models/{response.modelId}", "GET")) {
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Authorization", $"Bearer {_tripo3DApiKey}");
                    
                    yield return webRequest.SendWebRequest();
                    
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        throw new Exception($"Status check failed: {webRequest.error}");
                    }
                    
                    // Parse response
                    string responseJson = webRequest.downloadHandler.text;
                    ModelGenerationResponse updatedResponse = JsonConvert.DeserializeObject<ModelGenerationResponse>(responseJson);
                    
                    if (updatedResponse == null) {
                        throw new Exception("Failed to parse status response");
                    }
                    
                    // Update stored response
                    updatedResponse.objectId = objectId;
                    _pendingResponses[objectId] = updatedResponse;
                }
            }
            catch (Exception ex) {
                LogWarning($"Error checking model status: {ex.Message}", LogCategory.Models);
            }
        }
        
        /// <summary>
        /// Download model from URL.
        /// </summary>
        private IEnumerator DownloadModel(string url, System.Action<GameObject> onComplete, System.Action<string> onError) {
            try {
                using (UnityWebRequest webRequest = UnityWebRequestAssetBundle.GetAssetBundle(url)) {
                    yield return webRequest.SendWebRequest();
                    
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        throw new Exception($"Download failed: {webRequest.error}");
                    }
                    
                    AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(webRequest);
                    if (bundle == null) {
                        throw new Exception("Failed to download asset bundle");
                    }
                    
                    // Load main asset
                    string[] assetNames = bundle.GetAllAssetNames();
                    if (assetNames.Length == 0) {
                        throw new Exception("Asset bundle contains no assets");
                    }
                    
                    // Find model asset
                    string modelAssetName = null;
                    foreach (string name in assetNames) {
                        if (name.EndsWith(".fbx") || name.EndsWith(".obj") || name.EndsWith(".prefab")) {
                            modelAssetName = name;
                            break;
                        }
                    }
                    
                    if (string.IsNullOrEmpty(modelAssetName)) {
                        modelAssetName = assetNames[0]; // Fallback to first asset
                    }
                    
                    // Load asset
                    GameObject modelPrefab = bundle.LoadAsset<GameObject>(modelAssetName);
                    if (modelPrefab == null) {
                        throw new Exception("Failed to load model from asset bundle");
                    }
                    
                    // Instantiate model
                    GameObject modelObject = Instantiate(modelPrefab);
                    
                    // Unload bundle
                    bundle.Unload(false);
                    
                    // Complete
                    onComplete?.Invoke(modelObject);
                }
            }
            catch (Exception ex) {
                LogError($"Error downloading model: {ex.Message}", LogCategory.Models);
                onError?.Invoke(ex.Message);
            }
        }
        /// <summary>
        /// Create instances for a similarity group.
        /// </summary>
        private void CreateInstancesForGroup(SimilarityGroup group) {
            if (group.sourceModel == null || group.sourceModel.modelObject == null) {
                LogWarning($"Cannot create instances: source model is missing", LogCategory.Models);
                return;
            }
            
            // Skip the source object itself
            foreach (int objectId in group.similarObjectIds) {
                if (objectId == group.sourceObjectId) continue;
                
                if (_pendingRequests.TryGetValue(objectId, out ModelGenerationRequest request)) {
                    // Create instance
                    GameObject instanceObject = Instantiate(group.sourceModel.modelObject);
                    instanceObject.name = $"{request.objectType}_Instance_{objectId}";
                    
                    // Parent to models container
                    if (_modelsParent != null) {
                        instanceObject.transform.SetParent(_modelsParent);
                    }
                    
                    // Create generated model
                    GeneratedModel model = new GeneratedModel {
                        objectId = objectId,
                        objectType = request.objectType,
                        modelObject = instanceObject,
                        originalPosition = request.position,
                        originalRotation = request.rotation,
                        originalScale = request.scale,
                        mesh = group.sourceModel.mesh,
                        material = group.sourceModel.material,
                        isInstance = true,
                        instanceSourceId = group.sourceObjectId,
                        modelId = group.sourceModel.modelId,
                        metadata = request.metadata
                    };
                    
                    // Apply transformations
                    TransformModel(model, request.position, request.rotation, request.scale);
                    
                    // Add collider if needed
                    if (_addColliders) {
                        AddColliderToModel(instanceObject);
                    }
                    
                    // Add to generated models
                    _generatedModels[objectId] = model;
                    
                    // Update progress
                    _processedCount++;
                    UpdateProgress();
                    
                    // Trigger event
                    OnModelGenerated?.Invoke(model);
                }
            }
            
            Log($"Created {group.similarObjectIds.Count - 1} instances of {group.objectType}", LogCategory.Models);
        }
        
        /// <summary>
        /// Place models on terrain.
        /// </summary>
        private void PlaceModelsOnTerrain() {
            if (!_placeOnTerrain || _terrain == null) {
                return;
            }
            
            foreach (var model in _generatedModels.Values) {
                if (model.modelObject != null) {
                    // Get terrain height at position
                    Vector3 position = model.modelObject.transform.position;
                    float terrainHeight = _terrain.SampleHeight(position) + _terrain.transform.position.y;
                    
                    // Apply height
                    position.y = terrainHeight + _placementYOffset;
                    model.modelObject.transform.position = position;
                    
                    // Align with terrain normal if enabled
                    if (_alignWithTerrain) {
                        AlignWithTerrainNormal(model.modelObject, position);
                    }
                }
            }
            
            Log($"Placed {_generatedModels.Count} models on terrain", LogCategory.Models);
        }
        
        /// <summary>
        /// Align object with terrain normal.
        /// </summary>
        private void AlignWithTerrainNormal(GameObject obj, Vector3 position) {
            // Get terrain normal
            Vector3 normal = _terrain.terrainData.GetInterpolatedNormal(
                (position.x - _terrain.transform.position.x) / _terrain.terrainData.size.x,
                (position.z - _terrain.transform.position.z) / _terrain.terrainData.size.z
            );
            
            // Create rotation to align with normal
            Quaternion normalRotation = Quaternion.FromToRotation(Vector3.up, normal);
            
            // Apply rotation
            obj.transform.rotation = normalRotation * obj.transform.rotation;
        }
        
        /// <summary>
        /// Update progress status.
        /// </summary>
        private void UpdateProgress(System.Action<string, float> onProgress = null) {
            if (_totalCount <= 0) return;
            
            progress = (float)_processedCount / _totalCount;
            statusMessage = $"Generated {_processedCount} of {_totalCount} models";
            
            onProgress?.Invoke(statusMessage, progress);
            OnGenerationProgress?.Invoke(statusMessage, progress);
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Create a generation request from a map object.
        /// </summary>
        private void CreateGenerationRequest(MapObject obj, Vector3? overrideScale = null) {
            ModelGenerationRequest request = new ModelGenerationRequest {
                objectId = obj.id,
                objectType = obj.objectType,
                description = obj.description,
                style = "realistic",
                tags = new List<string> { obj.objectType },
                isManMade = obj.objectType.Contains("building") || obj.objectType.Contains("structure"),
                position = obj.position,
                rotation = new Vector3(0, obj.rotation, 0),
                scale = overrideScale ?? obj.scale,
                metadata = new Dictionary<string, object>()
            };
            
            // Add metadata
            if (obj.metadata != null) {
                foreach (var pair in obj.metadata) {
                    request.metadata[pair.Key] = pair.Value;
                }
            }
            
            // Add to pending requests
            _pendingRequests[obj.id] = request;
        }
        
        /// <summary>
        /// Convert a detected object to a map object.
        /// </summary>
        private MapObject ConvertDetectedObjectToMapObject(DetectedObject obj) {
            MapObject mapObj = new MapObject {
                id = obj.id,
                objectType = obj.className,
                objectName = obj.className,
                position = new Vector3(obj.centroid.x, 0, obj.centroid.y),
                rotation = UnityEngine.Random.Range(0f, 360f),
                scale = new Vector3(
                    obj.boundingBox.width / 100f,
                    obj.estimatedHeight > 0 ? obj.estimatedHeight / 100f : 1f,
                    obj.boundingBox.height / 100f
                ),
                description = obj.enhancedDescription ?? obj.shortDescription,
                estimatedHeight = obj.estimatedHeight,
                materials = obj.estimatedMaterials,
                metadata = obj.metadata != null ? new Dictionary<string, object>(obj.metadata) : new Dictionary<string, object>()
            };
            
            return mapObj;
        }
        
        /// <summary>
        /// Check if objects are similar enough for instancing.
        /// </summary>
        private bool IsSimilarObject(MapObject obj1, MapObject obj2) {
            // Same type is required
            if (obj1.objectType != obj2.objectType) {
                return false;
            }
            
            // Similar size (within 20%)
            float sizeRatio = obj1.scale.magnitude / obj2.scale.magnitude;
            if (sizeRatio < 0.8f || sizeRatio > 1.2f) {
                return false;
            }
            
            // Similar description if available
            if (!string.IsNullOrEmpty(obj1.description) && !string.IsNullOrEmpty(obj2.description)) {
                float similarity = CalculateStringSimilarity(obj1.description, obj2.description);
                return similarity >= _similarityThreshold;
            }
            
            // Default to similar if descriptions not available
            return true;
        }
        
        /// <summary>
        /// Calculate string similarity using Levenshtein distance.
        /// </summary>
        private float CalculateStringSimilarity(string s1, string s2) {
            s1 = s1.ToLower();
            s2 = s2.ToLower();
            
            int[,] distance = new int[s1.Length + 1, s2.Length + 1];
            
            // Initialize
            for (int i = 0; i <= s1.Length; i++) {
                distance[i, 0] = i;
            }
            for (int j = 0; j <= s2.Length; j++) {
                distance[0, j] = j;
            }
            
            // Calculate distance
            for (int i = 1; i <= s1.Length; i++) {
                for (int j = 1; j <= s2.Length; j++) {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    distance[i, j] = Mathf.Min(
                        distance[i - 1, j] + 1,       // Deletion
                        distance[i, j - 1] + 1,       // Insertion
                        distance[i - 1, j - 1] + cost // Substitution
                    );
                }
            }
            
            // Calculate similarity (0-1)
            int maxLength = Mathf.Max(s1.Length, s2.Length);
            if (maxLength == 0) return 1f; // Both strings empty
            
            return 1f - (float)distance[s1.Length, s2.Length] / maxLength;
        }
        /// <summary>
        /// Prepare JSON for generation request.
        /// </summary>
        private string PrepareGenerationRequestJson(ModelGenerationRequest request) {
            // Create request object
            var requestObj = new {
                prompt = GetGenerationPrompt(request),
                style = request.style ?? "realistic",
                tags = request.tags ?? new List<string>(),
                format = "glb",
                settings = new {
                    quality = "high",
                    polycount = _maxPolyCount,
                    textures = true,
                    animation = false
                }
            };
            
            // Convert to JSON
            return JsonConvert.SerializeObject(requestObj);
        }
        
        /// <summary>
        /// Get prompt for model generation.
        /// </summary>
        private string GetGenerationPrompt(ModelGenerationRequest request) {
            // Start with object type
            string prompt = request.objectType;
            
            // Add description if available
            if (!string.IsNullOrEmpty(request.description)) {
                prompt += $", {request.description}";
            }
            
            // Add additional context based on object type
            if (request.isManMade) {
                prompt += ", man-made object, 3D asset";
            }
            else {
                prompt += ", natural object, 3D asset";
            }
            
            // Add scale reference
            float averageScale = (request.scale.x + request.scale.y + request.scale.z) / 3f;
            if (averageScale < 0.5f) {
                prompt += ", small object";
            }
            else if (averageScale > 2f) {
                prompt += ", large object";
            }
            
            return prompt;
        }
        
        /// <summary>
        /// Get fallback model for object type.
        /// </summary>
        private GameObject GetFallbackModel(string objectType) {
            string type = objectType.ToLower();
            
            // Check exact match
            foreach (var mapping in _fallbackModels) {
                if (mapping.objectType.ToLower() == type && mapping.modelPrefab != null) {
                    return mapping.modelPrefab;
                }
            }
            
            // Check aliases
            foreach (var mapping in _fallbackModels) {
                if (mapping.aliases != null) {
                    foreach (var alias in mapping.aliases) {
                        if (alias.ToLower() == type && mapping.modelPrefab != null) {
                            return mapping.modelPrefab;
                        }
                    }
                }
            }
            
            // Check partial match
            foreach (var mapping in _fallbackModels) {
                if (type.Contains(mapping.objectType.ToLower()) || mapping.objectType.ToLower().Contains(type)) {
                    return mapping.modelPrefab;
                }
                
                if (mapping.aliases != null) {
                    foreach (var alias in mapping.aliases) {
                        if (type.Contains(alias.ToLower()) || alias.ToLower().Contains(type)) {
                            return mapping.modelPrefab;
                        }
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Create model from fallback prefab.
        /// </summary>
        private GeneratedModel CreateModelFromFallback(ModelGenerationRequest request, GameObject fallbackPrefab) {
            // Instantiate fallback model
            GameObject modelObject = Instantiate(fallbackPrefab);
            modelObject.name = $"{request.objectType}_Fallback_{request.objectId}";
            
            // Parent to models container
            if (_modelsParent != null) {
                modelObject.transform.SetParent(_modelsParent);
            }
            
            // Create generated model
            GeneratedModel model = new GeneratedModel {
                objectId = request.objectId,
                objectType = request.objectType,
                modelObject = modelObject,
                originalPosition = request.position,
                originalRotation = request.rotation,
                originalScale = request.scale,
                mesh = modelObject.GetComponentInChildren<MeshFilter>()?.sharedMesh,
                material = modelObject.GetComponentInChildren<MeshRenderer>()?.sharedMaterial,
                isInstance = false,
                instanceSourceId = -1,
                modelId = $"fallback_{request.objectType}",
                metadata = request.metadata
            };
            
            Log($"Created fallback model for {request.objectType}", LogCategory.Models);
            return model;
        }
        
        /// <summary>
        /// Create default primitive model.
        /// </summary>
        private GeneratedModel CreateDefaultModel(ModelGenerationRequest request) {
            // Create primitive based on object type
            PrimitiveType primitiveType = PrimitiveType.Cube;
            
            string type = request.objectType.ToLower();
            if (type.Contains("sphere") || type.Contains("ball") || type.Contains("round")) {
                primitiveType = PrimitiveType.Sphere;
            }
            else if (type.Contains("cylinder") || type.Contains("tube") || type.Contains("pipe")) {
                primitiveType = PrimitiveType.Cylinder;
            }
            else if (type.Contains("capsule") || type.Contains("pill")) {
                primitiveType = PrimitiveType.Capsule;
            }
            
            // Create primitive
            GameObject modelObject = GameObject.CreatePrimitive(primitiveType);
            modelObject.name = $"{request.objectType}_Default_{request.objectId}";
            
            // Apply default material
            if (_defaultMaterial != null) {
                MeshRenderer renderer = modelObject.GetComponent<MeshRenderer>();
                if (renderer != null) {
                    renderer.material = _defaultMaterial;
                }
            }
            
            // Parent to models container
            if (_modelsParent != null) {
                modelObject.transform.SetParent(_modelsParent);
            }
            
            // Create generated model
            GeneratedModel model = new GeneratedModel {
                objectId = request.objectId,
                objectType = request.objectType,
                modelObject = modelObject,
                originalPosition = request.position,
                originalRotation = request.rotation,
                originalScale = request.scale,
                mesh = modelObject.GetComponentInChildren<MeshFilter>()?.sharedMesh,
                material = modelObject.GetComponentInChildren<MeshRenderer>()?.sharedMaterial,
                isInstance = false,
                instanceSourceId = -1,
                modelId = $"default_{request.objectType}",
                metadata = request.metadata
            };
            
            Log($"Created default model for {request.objectType}", LogCategory.Models);
            return model;
        }
        
        /// <summary>
        /// Try to use model from cache.
        /// </summary>
        private bool TryUseModelFromCache(ModelGenerationRequest request, System.Action<GeneratedModel> onFound) {
            // Find similar model in cache
            foreach (var pair in _generatedModels) {
                var model = pair.Value;
                if (model.objectType == request.objectType && !model.isInstance) {
                    // Found a potential match
                    if (model.modelObject != null) {
                        // Create instance
                        GameObject instanceObject = Instantiate(model.modelObject);
                        instanceObject.name = $"{request.objectType}_Cached_{request.objectId}";
                        
                        // Parent to models container
                        if (_modelsParent != null) {
                            instanceObject.transform.SetParent(_modelsParent);
                        }
                        
                        // Create generated model
                        GeneratedModel cachedModel = new GeneratedModel {
                            objectId = request.objectId,
                            objectType = request.objectType,
                            modelObject = instanceObject,
                            originalPosition = request.position,
                            originalRotation = request.rotation,
                            originalScale = request.scale,
                            mesh = model.mesh,
                            material = model.material,
                            isInstance = true,
                            instanceSourceId = model.objectId,
                            modelId = model.modelId,
                            metadata = request.metadata
                        };
                        
                        Log($"Used cached model for {request.objectType}", LogCategory.Models);
                        onFound?.Invoke(cachedModel);
                        return true;
                    }
                }
            }
            
            // Try model cache dictionary
            if (_modelCache.Count > 0) {
                // Look for similar model IDs
                foreach (var pair in _modelCache) {
                    if (pair.Key.Contains(request.objectType) || request.objectType.Contains(pair.Key)) {
                        if (pair.Value != null) {
                            // Create instance
                            GameObject instanceObject = Instantiate(pair.Value);
                            instanceObject.name = $"{request.objectType}_Cached_{request.objectId}";
                            
                            // Parent to models container
                            if (_modelsParent != null) {
                                instanceObject.transform.SetParent(_modelsParent);
                            }
                            
                            // Create generated model
                            GeneratedModel cachedModel = new GeneratedModel {
                                objectId = request.objectId,
                                objectType = request.objectType,
                                modelObject = instanceObject,
                                originalPosition = request.position,
                                originalRotation = request.rotation,
                                originalScale = request.scale,
                                mesh = instanceObject.GetComponentInChildren<MeshFilter>()?.sharedMesh,
                                material = instanceObject.GetComponentInChildren<MeshRenderer>()?.sharedMaterial,
                                isInstance = true,
                                instanceSourceId = -1,
                                modelId = pair.Key,
                                metadata = request.metadata
                            };
                            
                            Log($"Used cached model from dictionary for {request.objectType}", LogCategory.Models);
                            onFound?.Invoke(cachedModel);
                            return true;
                        }
                    }
                }
            }
            
            return false;
        }
        /// <summary>
        /// Transform model to position, rotation, and scale.
        /// </summary>
        private void TransformModel(GeneratedModel model, Vector3 position, Vector3 rotation, Vector3 scale) {
            if (model.modelObject != null) {
                // Apply position
                model.modelObject.transform.position = position;
                
                // Apply rotation
                model.modelObject.transform.eulerAngles = rotation;
                
                // Apply scale
                model.modelObject.transform.localScale = Vector3.Scale(scale, _defaultModelScale);
                
                // Update model data
                model.originalPosition = position;
                model.originalRotation = rotation;
                model.originalScale = scale;
            }
        }
        
        /// <summary>
        /// Add collider to model.
        /// </summary>
        private void AddColliderToModel(GameObject modelObject) {
            // Check if model already has a collider
            Collider existingCollider = modelObject.GetComponentInChildren<Collider>();
            if (existingCollider != null) {
                return;
            }
            
            // Find all mesh renderers
            MeshRenderer[] renderers = modelObject.GetComponentsInChildren<MeshRenderer>();
            
            if (renderers.Length == 0) {
                // No renderers, add box collider to root
                modelObject.AddComponent<BoxCollider>();
                return;
            }
            
            // Add mesh colliders to each mesh
            foreach (var renderer in renderers) {
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null) {
                    MeshCollider collider = renderer.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = meshFilter.sharedMesh;
                    collider.convex = true;
                }
            }
        }
        
        /// <summary>
        /// Optimize a model to reduce polygon count.
        /// </summary>
        private void OptimizeModel(GameObject modelObject) {
            // Find all mesh filters
            MeshFilter[] meshFilters = modelObject.GetComponentsInChildren<MeshFilter>();
            
            if (meshFilters.Length == 0) {
                return;
            }
            
            // Optimize each mesh
            foreach (var meshFilter in meshFilters) {
                Mesh originalMesh = meshFilter.sharedMesh;
                if (originalMesh == null) continue;
                
                // Check if optimization is needed
                if (originalMesh.triangles.Length / 3 <= _maxPolyCount) {
                    continue;
                }
                
                try {
                    // Create simplified mesh
                    Mesh simplifiedMesh = new Mesh();
                    simplifiedMesh.name = originalMesh.name + "_Simplified";
                    
                    // Copy vertices, uvs, etc.
                    simplifiedMesh.vertices = originalMesh.vertices;
                    simplifiedMesh.uv = originalMesh.uv;
                    simplifiedMesh.normals = originalMesh.normals;
                    simplifiedMesh.colors = originalMesh.colors;
                    simplifiedMesh.tangents = originalMesh.tangents;
                    
                    // Reduce triangle count
                    int[] triangles = originalMesh.triangles;
                    int targetTriangleCount = Mathf.CeilToInt(triangles.Length / 3 * _lodReductionFactor);
                    targetTriangleCount = Mathf.Min(targetTriangleCount, _maxPolyCount);
                    
                    // Simple decimation by skipping triangles
                    if (triangles.Length / 3 > targetTriangleCount) {
                        int skipFactor = Mathf.CeilToInt(triangles.Length / 3 / (float)targetTriangleCount);
                        List<int> newTriangles = new List<int>();
                        
                        for (int i = 0; i < triangles.Length; i += 3 * skipFactor) {
                            if (i + 2 < triangles.Length) {
                                newTriangles.Add(triangles[i]);
                                newTriangles.Add(triangles[i + 1]);
                                newTriangles.Add(triangles[i + 2]);
                            }
                        }
                        
                        simplifiedMesh.triangles = newTriangles.ToArray();
                    }
                    else {
                        simplifiedMesh.triangles = triangles;
                    }
                    
                    // Recalculate mesh data
                    simplifiedMesh.RecalculateNormals();
                    simplifiedMesh.RecalculateBounds();
                    
                    // Apply simplified mesh
                    meshFilter.sharedMesh = simplifiedMesh;
                    
                    // Update collider if present
                    MeshCollider collider = meshFilter.GetComponent<MeshCollider>();
                    if (collider != null) {
                        collider.sharedMesh = simplifiedMesh;
                    }
                    
                    Log($"Optimized mesh from {triangles.Length / 3} to {simplifiedMesh.triangles.Length / 3} triangles", LogCategory.Models);
                }
                catch (Exception ex) {
                    LogWarning($"Error optimizing mesh: {ex.Message}", LogCategory.Models);
                }
            }
        }
        
        /// <summary>
        /// Log a message using the debugger.
        /// </summary>
        private void Log(string message, LogCategory category) {
            _debugger?.Log(message, category);
        }
        
        /// <summary>
        /// Log a warning using the debugger.
        /// </summary>
        private void LogWarning(string message, LogCategory category) {
            _debugger?.LogWarning(message, category);
        }
        
        /// <summary>
        /// Log an error using the debugger.
        /// </summary>
        private void LogError(string message, LogCategory category) {
            _debugger?.LogError(message, category);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Simple water flow component.
    /// </summary>
    public class WaterFlow : MonoBehaviour {
        public Vector3 flowDirection = Vector3.right;
        public float flowSpeed = 0.5f;
        private Material _material;
        
        private void Start() {
            // Get material
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null) {
                _material = renderer.material;
            }
        }
        
        private void Update() {
            // Update material if available
            if (_material != null) {
                // Update flow offset
                Vector2 offset = new Vector2(flowDirection.x, flowDirection.z) * flowSpeed * Time.time;
                _material.SetVector("_FlowOffset", new Vector4(offset.x, offset.y, 0, 0));
            }
        }
    }
}