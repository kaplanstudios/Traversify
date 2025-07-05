/*************************************************************************
 *  Traversify – ModelGenerator.cs                                       *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-07-04                                                  *
 *  Desc   : Advanced model generation and placement system for          *
 *           Traversify. Handles 3D model creation via Tripo3D API,      *
 *           optimization, instancing, and precise terrain alignment.    *
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
using Traversify.Terrain;
using Piglet;

namespace Traversify.AI {
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
        [Header("Tripo3D API Settings")]
        [Tooltip("Tripo3D API key")]
        [SerializeField] private string tripoApiKey;
        
        /// <summary>
        /// Public property for Tripo3D API key access
        /// </summary>
        public string Tripo3DApiKey 
        { 
            get => tripoApiKey; 
            set => tripoApiKey = value; 
        }
        
        // OpenAI API key for description enhancement
        public string openAIApiKey { get; set; }
        
        [Tooltip("Tripo3D API endpoint URL")]
        [SerializeField] private string tripoApiUrl = "https://api.tripo3d.ai/v2/openapi";
        
        [Tooltip("Maximum time to wait for model generation (seconds)")]
        [SerializeField] private float maxGenerationTime = 300f;
        
        [Tooltip("Polling interval for checking generation status (seconds)")]
        [SerializeField] private float pollingInterval = 5f;

        [Header("Generation Settings")]
        [Tooltip("Group similar objects into instances instead of unique models")]
        public bool groupSimilarObjects = true;
        
        [Tooltip("Similarity threshold (0–1) for instancing")]
        [Range(0f, 1f)] public float instancingSimilarity = 0.8f;
        
        [Tooltip("Maximum number of objects in a single group")]
        public int maxGroupSize = 50;
        
        [Tooltip("Enable level of detail (LOD) for generated models")]
        public bool useLOD = true;
        
        [Tooltip("Maximum number of LOD levels to generate")]
        [Range(1, 5)] public int maxLODLevels = 3;

        [Header("Placement Settings")]
        [Tooltip("Apply terrain adaptation to placed objects")]
        public bool adaptToTerrain = true;
        
        [Tooltip("Maximum slope angle for placement (degrees)")]
        [Range(0f, 90f)] public float maxPlacementSlope = 45f;
        
        [Tooltip("Enable collision detection between objects")]
        public bool avoidCollisions = true;
        
        [Tooltip("Distance to maintain between objects (meters)")]
        public float objectSpacing = 1.0f;
        
        [Tooltip("Object sink depth for grounding")]
        [Range(0f, 1f)] public float groundingDepth = 0.05f;

        [Header("Performance")]
        [Tooltip("Use multithreading for model generation")]
        public bool useMultithreading = true;
        
        [Tooltip("Maximum concurrent model generation requests")]
        [Range(1, 10)] public int maxConcurrentRequests = 3;
        
        [Tooltip("Cache generated models for reuse")]
        public bool cacheModels = true;
        
        [Tooltip("Maximum models to keep in cache")]
        [Range(10, 100)] public int maxCacheSize = 50;
        #endregion

        #region Private Fields
        private TraversifyDebugger _debugger;
        private ObjectPlacer _objectPlacer;
        private Dictionary<string, GameObject> _modelCache = new Dictionary<string, GameObject>();
        private Dictionary<string, string> _generationTaskIds = new Dictionary<string, string>();
        private List<ModelGenerationRequest> _generationQueue = new List<ModelGenerationRequest>();
        private bool _isGenerating = false;
        private int _activeRequests = 0;
        #endregion

        #region Data Structures
        
        /// <summary>
        /// Represents a model generation request for Tripo3D.
        /// </summary>
        [System.Serializable]
        public class ModelGenerationRequest
        {
            public string objectType;
            public string enhancedDescription;
            public List<DetectedObject> detectedObjects;
            public Vector3 averageScale;
            public float confidence;
            public string cacheKey;
            public bool isProcessing;
            public string taskId;
            public GameObject generatedModel;
        }
        
        /// <summary>
        /// Represents the result of model generation.
        /// </summary>
        [System.Serializable]
        public class ModelGenerationResult
        {
            public string objectType;
            public GameObject model;
            public Vector3 recommendedScale;
            public bool success;
            public string errorMessage;
        }
        
        /// <summary>
        /// Tripo3D API response structures.
        /// </summary>
        [System.Serializable]
        public class TripoGenerationResponse
        {
            public int code;
            public string message;
            public TripoTaskData data;
        }
        
        [System.Serializable]
        public class TripoTaskData
        {
            public string task_id;
            public string status; // "queued", "running", "success", "failed"
            public TripoResult result;
        }
        
        [System.Serializable]
        public class TripoResult
        {
            public string model_url;
            public string thumbnail_url;
        }
        
        #endregion

        #region TraversifyComponent Implementation
        
        protected override bool OnInitialize(object config)
        {
            try
            {
                _debugger = GetComponent<TraversifyDebugger>();
                if (_debugger == null)
                {
                    _debugger = gameObject.AddComponent<TraversifyDebugger>();
                }
                
                // Find ObjectPlacer component
                _objectPlacer = FindObjectOfType<ObjectPlacer>();
                if (_objectPlacer == null)
                {
                    var placerGO = new GameObject("ObjectPlacer");
                    _objectPlacer = placerGO.AddComponent<ObjectPlacer>();
                }
                
                Log("ModelGenerator initialized successfully", LogCategory.System);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize ModelGenerator: {ex.Message}", LogCategory.System);
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
        
        #endregion

        #region Main Generation Pipeline
        
        /// <summary>
        /// Generate 3D models for all detected objects using enhanced descriptions.
        /// </summary>
        public IEnumerator GenerateModelsFromAnalysis(AnalysisResults analysisResults,
            System.Action<List<ModelGenerationResult>> onComplete = null,
            System.Action<string> onError = null,
            System.Action<string, float> onProgress = null)
        {
            if (analysisResults == null || analysisResults.nonTerrainObjects.Count == 0)
            {
                onError?.Invoke("No non-terrain objects found for model generation");
                yield break;
            }
            
            _isGenerating = true;
            
            Log("Starting model generation from analysis results", LogCategory.AI);
            
            // Step 1: Group similar objects for instancing (10% progress)
            onProgress?.Invoke("Grouping similar objects...", 0.1f);
            var objectGroups = GroupSimilarObjects(analysisResults.nonTerrainObjects);
            
            // Step 2: Create generation requests (20% progress)
            onProgress?.Invoke("Creating generation requests...", 0.2f);
            var generationRequests = CreateGenerationRequests(objectGroups, analysisResults.enhancedDescriptions);
            
            // Step 3: Submit requests to Tripo3D (30% progress)
            onProgress?.Invoke("Submitting requests to Tripo3D...", 0.3f);
            yield return StartCoroutine(SubmitGenerationRequestsSafe(generationRequests, onProgress, onError));
            
            // Step 4: Wait for completion and download models (80% progress)
            onProgress?.Invoke("Downloading generated models...", 0.8f);
            yield return StartCoroutine(WaitForGenerationCompletionSafe(generationRequests, onProgress, onError));
            
            // Step 5: Place objects in scene (90% progress)
            onProgress?.Invoke("Placing objects in scene...", 0.9f);
            yield return StartCoroutine(PlaceGeneratedObjectsSafe(generationRequests, analysisResults, onError));
            
            // Step 6: Build results (100% progress)
            onProgress?.Invoke("Finalizing model generation...", 1.0f);
            var results = BuildGenerationResults(generationRequests);
            
            Log($"Model generation completed successfully with {results.Count} models", LogCategory.AI);
            onComplete?.Invoke(results);
            
            _isGenerating = false;
        }
        
        /// <summary>
        /// Safe wrapper for SubmitGenerationRequests that handles exceptions.
        /// </summary>
        private IEnumerator SubmitGenerationRequestsSafe(List<ModelGenerationRequest> requests, 
            System.Action<string, float> onProgress, System.Action<string> onError)
        {
            bool hasError = false;
            string errorMessage = null;
            
            yield return StartCoroutine(ExecuteWithErrorHandling(
                () => StartCoroutine(SubmitGenerationRequests(requests, onProgress)),
                (error) => { hasError = true; errorMessage = error; }
            ));
            
            if (hasError)
            {
                LogError($"Failed to submit generation requests: {errorMessage}", LogCategory.AI);
                onError?.Invoke($"Failed to submit generation requests: {errorMessage}");
            }
        }
        
        /// <summary>
        /// Safe wrapper for WaitForGenerationCompletion that handles exceptions.
        /// </summary>
        private IEnumerator WaitForGenerationCompletionSafe(List<ModelGenerationRequest> requests, 
            System.Action<string, float> onProgress, System.Action<string> onError)
        {
            bool hasError = false;
            string errorMessage = null;
            
            yield return StartCoroutine(ExecuteWithErrorHandling(
                () => StartCoroutine(WaitForGenerationCompletion(requests, onProgress)),
                (error) => { hasError = true; errorMessage = error; }
            ));
            
            if (hasError)
            {
                LogError($"Failed during generation completion: {errorMessage}", LogCategory.AI);
                onError?.Invoke($"Failed during generation completion: {errorMessage}");
            }
        }
        
        /// <summary>
        /// Safe wrapper for PlaceGeneratedObjects that handles exceptions.
        /// </summary>
        private IEnumerator PlaceGeneratedObjectsSafe(List<ModelGenerationRequest> requests, 
            AnalysisResults analysisResults, System.Action<string> onError)
        {
            bool hasError = false;
            string errorMessage = null;
            
            yield return StartCoroutine(ExecuteWithErrorHandling(
                () => StartCoroutine(PlaceGeneratedObjects(requests, analysisResults)),
                (error) => { hasError = true; errorMessage = error; }
            ));
            
            if (hasError)
            {
                LogError($"Failed to place generated objects: {errorMessage}", LogCategory.AI);
                onError?.Invoke($"Failed to place generated objects: {errorMessage}");
            }
        }
        
        /// <summary>
        /// Execute a coroutine with error handling without try-catch around yield.
        /// </summary>
        private IEnumerator ExecuteWithErrorHandling(System.Func<Coroutine> coroutineFunc, System.Action<string> onError)
        {
            Exception caughtException = null;
            Coroutine targetCoroutine = null;
            
            try
            {
                targetCoroutine = coroutineFunc();
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
            
            if (caughtException != null)
            {
                onError?.Invoke(caughtException.Message);
                yield break;
            }
            
            if (targetCoroutine != null)
            {
                yield return targetCoroutine;
            }
        }
        
        /// <summary>
        /// Generate a single 3D model from a description.
        /// </summary>
        public IEnumerator GenerateModel(string objectType, string description, System.Action<GameObject> onComplete)
        {
            var request = new ModelGenerationRequest
            {
                objectType = objectType,
                enhancedDescription = description,
                detectedObjects = new List<DetectedObject>(),
                averageScale = Vector3.one,
                confidence = 1.0f,
                cacheKey = GenerateCacheKey(objectType, description),
                isProcessing = false,
                taskId = null,
                generatedModel = null
            };
            
            // Check cache first
            if (cacheModels && _modelCache.ContainsKey(request.cacheKey))
            {
                onComplete?.Invoke(_modelCache[request.cacheKey]);
                yield break;
            }
            
            // Submit to Tripo3D
            yield return StartCoroutine(SubmitSingleRequest(request));
            
            if (!string.IsNullOrEmpty(request.taskId))
            {
                // Wait for completion
                yield return StartCoroutine(WaitForSingleModelCompletion(request));
            }
            
            onComplete?.Invoke(request.generatedModel);
        }
        
        /// <summary>
        /// Generate a single 3D model from a description with error callback.
        /// </summary>
        public IEnumerator GenerateModel(string objectType, string description, System.Action<GameObject> onComplete, System.Action<string> onError)
        {
            var request = new ModelGenerationRequest
            {
                objectType = objectType,
                enhancedDescription = description,
                detectedObjects = new List<DetectedObject>(),
                averageScale = Vector3.one,
                confidence = 1.0f,
                cacheKey = GenerateCacheKey(objectType, description),
                isProcessing = false,
                taskId = null,
                generatedModel = null
            };
            
            // Check cache first
            if (cacheModels && _modelCache.ContainsKey(request.cacheKey))
            {
                onComplete?.Invoke(_modelCache[request.cacheKey]);
                yield break;
            }
            
            // Submit to Tripo3D
            yield return StartCoroutine(SubmitSingleRequestWithErrorHandling(request, onError));
            
            if (!string.IsNullOrEmpty(request.taskId))
            {
                // Wait for completion
                yield return StartCoroutine(WaitForSingleModelCompletionWithErrorHandling(request, onError));
            }
            
            onComplete?.Invoke(request.generatedModel);
        }
        
        /// <summary>
        /// Submit a single generation request with error handling.
        /// </summary>
        private IEnumerator SubmitSingleRequestWithErrorHandling(ModelGenerationRequest request, System.Action<string> onError)
        {
            if (string.IsNullOrEmpty(tripoApiKey))
            {
                onError?.Invoke("Tripo3D API key not set");
                yield break;
            }
            
            // Prepare request data
            var requestData = new {
                type = "text_to_model",
                prompt = request.enhancedDescription,
                model_version = "v2.0-20240919",
                face_limit = 10000,
                texture_resolution = 1024
            };
            
            string jsonData = JsonUtility.ToJson(requestData);
            
            using (var webRequest = new UnityWebRequest($"{tripoApiUrl}/task", "POST"))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {tripoApiKey}");
                
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<TripoGenerationResponse>(webRequest.downloadHandler.text);
                        
                        if (response.code == 0 && response.data != null)
                        {
                            request.taskId = response.data.task_id;
                            request.isProcessing = true;
                            Log($"Submitted generation request for {request.objectType}, task ID: {request.taskId}", LogCategory.AI);
                        }
                        else
                        {
                            string errorMsg = $"Tripo3D API error: {response.message}";
                            LogError(errorMsg, LogCategory.AI);
                            onError?.Invoke(errorMsg);
                        }
                    }
                    catch (Exception ex)
                    {
                        string errorMsg = $"Failed to parse Tripo3D response: {ex.Message}";
                        LogError(errorMsg, LogCategory.AI);
                        onError?.Invoke(errorMsg);
                    }
                }
                else
                {
                    string errorMsg = $"Tripo3D request failed: {webRequest.error}";
                    LogError(errorMsg, LogCategory.AI);
                    onError?.Invoke(errorMsg);
                }
            }
        }
        
        /// <summary>
        /// Wait for a single model generation to complete with error handling.
        /// </summary>
        private IEnumerator WaitForSingleModelCompletionWithErrorHandling(ModelGenerationRequest request, System.Action<string> onError)
        {
            float startTime = Time.time;
            request.isProcessing = true;
            
            while (request.isProcessing && (Time.time - startTime) < maxGenerationTime)
            {
                yield return StartCoroutine(CheckTaskStatusWithErrorHandling(request, onError));
                yield return new WaitForSeconds(pollingInterval);
            }
            
            if (request.isProcessing)
            {
                string errorMsg = $"Model generation timed out for {request.objectType}";
                LogError(errorMsg, LogCategory.AI);
                onError?.Invoke(errorMsg);
                request.isProcessing = false;
            }
        }
        
        /// <summary>
        /// Check the status of a generation task with error handling.
        /// </summary>
        private IEnumerator CheckTaskStatusWithErrorHandling(ModelGenerationRequest request, System.Action<string> onError)
        {
            if (string.IsNullOrEmpty(request.taskId))
            {
                yield break;
            }
            
            using (var webRequest = UnityWebRequest.Get($"{tripoApiUrl}/task/{request.taskId}"))
            {
                webRequest.SetRequestHeader("Authorization", $"Bearer {tripoApiKey}");
                
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    var responseText = webRequest.downloadHandler.text;
                    yield return StartCoroutine(ProcessTaskStatusResponseWithErrorHandling(request, responseText, onError));
                }
                else
                {
                    string errorMsg = $"Failed to check task status: {webRequest.error}";
                    LogError(errorMsg, LogCategory.AI);
                    onError?.Invoke(errorMsg);
                }
            }
        }
        
        /// <summary>
        /// Process the task status response with error handling.
        /// </summary>
        private IEnumerator ProcessTaskStatusResponseWithErrorHandling(ModelGenerationRequest request, string responseText, System.Action<string> onError)
        {
            var response = ParseTaskStatusResponse(responseText);
            
            if (response != null && response.code == 0 && response.data != null)
            {
                switch (response.data.status)
                {
                    case "success":
                        if (response.data.result != null && !string.IsNullOrEmpty(response.data.result.model_url))
                        {
                            yield return StartCoroutine(DownloadModelWithErrorHandling(request, response.data.result.model_url, onError));
                        }
                        request.isProcessing = false;
                        break;
                        
                    case "failed":
                        string errorMsg = $"Model generation failed for {request.objectType}";
                        LogError(errorMsg, LogCategory.AI);
                        onError?.Invoke(errorMsg);
                        request.isProcessing = false;
                        break;
                        
                    case "queued":
                    case "running":
                        // Still processing, continue waiting
                        break;
                }
            }
            else
            {
                string errorMsg = "Invalid response from Tripo3D API";
                LogError(errorMsg, LogCategory.AI);
                onError?.Invoke(errorMsg);
            }
        }
        
        /// <summary>
        /// Download a generated model with error handling.
        /// </summary>
        private IEnumerator DownloadModelWithErrorHandling(ModelGenerationRequest request, string modelUrl, System.Action<string> onError)
        {
            using (var webRequest = UnityWebRequest.Get(modelUrl))
            {
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    var modelData = webRequest.downloadHandler.data;
                    yield return StartCoroutine(ProcessDownloadedModelWithErrorHandling(request, modelData, onError));
                }
                else
                {
                    string errorMsg = $"Failed to download model from {modelUrl}: {webRequest.error}";
                    LogError(errorMsg, LogCategory.AI);
                    onError?.Invoke(errorMsg);
                }
            }
        }
        
        /// <summary>
        /// Process downloaded model data with error handling.
        /// </summary>
        private IEnumerator ProcessDownloadedModelWithErrorHandling(ModelGenerationRequest request, byte[] modelData, System.Action<string> onError)
        {
            var filePath = SaveModelToFile(request, modelData);
            if (!string.IsNullOrEmpty(filePath))
            {
                yield return StartCoroutine(LoadModelFromFileWithErrorHandling(request, filePath, onError));
                Log($"Downloaded and loaded model for {request.objectType}", LogCategory.AI);
            }
            else
            {
                onError?.Invoke("Failed to save model to file");
            }
        }
        
        /// <summary>
        /// Load a model from file with error handling.
        /// </summary>
        private IEnumerator LoadModelFromFileWithErrorHandling(ModelGenerationRequest request, string filePath, System.Action<string> onError)
        {
            bool loadComplete = false;
            GameObject loadedModel = null;
            string errorMessage = null;
            
            var loadResult = LoadModelWithPiglet(filePath, 
                (model) => { loadedModel = model; loadComplete = true; },
                (error) => { errorMessage = error; loadComplete = true; });
            
            if (!loadResult)
            {
                errorMessage = "Failed to initialize Piglet import";
                loadComplete = true;
            }
            
            // Wait for import to complete
            while (!loadComplete)
            {
                yield return null;
            }
            
            if (!string.IsNullOrEmpty(errorMessage))
            {
                LogError($"Failed to load model: {errorMessage}", LogCategory.AI);
                onError?.Invoke(errorMessage);
            }
            else if (loadedModel != null)
            {
                request.generatedModel = loadedModel;
                
                // Add to cache
                if (cacheModels && !_modelCache.ContainsKey(request.cacheKey))
                {
                    _modelCache[request.cacheKey] = loadedModel;
                    
                    // Manage cache size
                    if (_modelCache.Count > maxCacheSize)
                    {
                        var oldestKey = _modelCache.Keys.First();
                        Destroy(_modelCache[oldestKey]);
                        _modelCache.Remove(oldestKey);
                    }
                }
            }
            else
            {
                onError?.Invoke("Model loading completed but no model was created");
            }
        }
        
        /// <summary>
        /// Load model using Piglet with proper error handling.
        /// </summary>
        private bool LoadModelWithPiglet(string filePath, System.Action<GameObject> onSuccess, System.Action<string> onError)
        {
            try
            {
                // Use Piglet's static import method
                var gltfImportTask = RuntimeGltfImporter.GetImportTask(filePath);
                
                gltfImportTask.OnCompleted = (gameObject) => onSuccess?.Invoke(gameObject);
                gltfImportTask.OnException = (exception) => onError?.Invoke(exception.Message);
                
                return true;
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Failed to initialize Piglet import: {ex.Message}");
                return false;
            }
        }
        
        #endregion

        #region Object Placement
        
        /// <summary>
        /// Place generated objects in the scene using ObjectPlacer.
        /// </summary>
        private IEnumerator PlaceGeneratedObjects(List<ModelGenerationRequest> requests, AnalysisResults analysisResults)
        {
            if (_objectPlacer == null)
            {
                LogError("ObjectPlacer not found", LogCategory.AI);
                yield break;
            }
            
            foreach (var request in requests)
            {
                if (request.generatedModel != null)
                {
                    yield return StartCoroutine(PlaceObjectGroup(request, analysisResults));
                }
                
                yield return null; // Prevent frame drops
            }
        }
        
        /// <summary>
        /// Place a group of objects using the generated model.
        /// </summary>
        private IEnumerator PlaceObjectGroup(ModelGenerationRequest request, AnalysisResults analysisResults)
        {
            foreach (var detectedObject in request.detectedObjects)
            {
                // Calculate world position from image coordinates
                Vector3 worldPosition = ConvertImageToWorldPosition(
                    detectedObject.boundingBox.center,
                    analysisResults.sourceImage,
                    analysisResults.terrainSize
                );
                
                // Create placement data
                var placementData = new ObjectPlacer.PlacementData
                {
                    prefab = request.generatedModel,
                    position = worldPosition,
                    rotation = Quaternion.identity,
                    scale = request.averageScale,
                    adaptToTerrain = adaptToTerrain,
                    avoidCollisions = avoidCollisions,
                    objectSpacing = objectSpacing,
                    maxSlope = maxPlacementSlope,
                    groundingDepth = groundingDepth
                };
                
                // Place object
                yield return StartCoroutine(_objectPlacer.PlaceObject(placementData));
            }
        }
        
        /// <summary>
        /// Convert image coordinates to world position.
        /// </summary>
        private Vector3 ConvertImageToWorldPosition(Vector2 imagePosition, Texture2D sourceImage, Vector2 terrainSize)
        {
            // Normalize image coordinates (0-1)
            float normalizedX = imagePosition.x / sourceImage.width;
            float normalizedY = imagePosition.y / sourceImage.height;
            
            // Convert to world coordinates
            float worldX = (normalizedX - 0.5f) * terrainSize.x;
            float worldZ = (normalizedY - 0.5f) * terrainSize.y;
            
            return new Vector3(worldX, 0f, worldZ);
        }
        
        #endregion

        #region Results Building
        
        /// <summary>
        /// Build the final generation results.
        /// </summary>
        private List<ModelGenerationResult> BuildGenerationResults(List<ModelGenerationRequest> requests)
        {
            var results = new List<ModelGenerationResult>();
            
            foreach (var request in requests)
            {
                var result = new ModelGenerationResult
                {
                    objectType = request.objectType,
                    model = request.generatedModel,
                    recommendedScale = request.averageScale,
                    success = request.generatedModel != null,
                    errorMessage = request.generatedModel == null ? "Model generation failed" : null
                };
                
                results.Add(result);
            }
            
            return results;
        }
        
        #endregion

        #region Utility Methods
        
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

        #region Complete Methods

        /// <summary>
        /// Generate and place multiple models based on analysis results.
        /// </summary>
        public IEnumerator GenerateAndPlaceModels(AnalysisResults analysisResults, UnityEngine.Terrain terrain, System.Action<List<ModelGenerationResult>> onComplete)
        {
            yield return StartCoroutine(GenerateModelsFromAnalysis(
                analysisResults,
                onComplete,
                error => LogError($"Model generation error: {error}", LogCategory.AI),
                (message, progress) => Log($"Progress: {message} ({progress:P0})", LogCategory.AI)
            ));
        }
        
        /// <summary>
        /// Generate and place multiple models based on analysis results.
        /// </summary>
        public IEnumerator GenerateAndPlaceModels(AnalysisResults analysisResults, UnityEngine.Terrain terrain, System.Action<List<GameObject>> onComplete, System.Action<string> onError = null, System.Action<float> onProgress = null)
        {
            List<GameObject> gameObjects = new List<GameObject>();
            
            yield return StartCoroutine(GenerateModelsFromAnalysis(
                analysisResults,
                (results) => {
                    // Convert ModelGenerationResult to GameObject list
                    gameObjects = results.Where(r => r.success && r.model != null)
                                        .Select(r => r.model)
                                        .ToList();
                },
                onError,
                (message, progress) => onProgress?.Invoke(progress)
            ));
            
            onComplete?.Invoke(gameObjects);
        }
        
        /// <summary>
        /// Wait for a single model generation to complete.
        /// </summary>
        private IEnumerator WaitForSingleModelCompletion(ModelGenerationRequest request)
        {
            float startTime = Time.time;
            request.isProcessing = true;
            
            while (request.isProcessing && (Time.time - startTime) < maxGenerationTime)
            {
                yield return StartCoroutine(CheckTaskStatus(request));
                yield return new WaitForSeconds(pollingInterval);
            }
            
            if (request.isProcessing)
            {
                LogError($"Model generation timed out for {request.objectType}", LogCategory.AI);
                request.isProcessing = false;
            }
        }
        
        /// <summary>
        /// Place a model on terrain with proper positioning.
        /// </summary>
        public GameObject PlaceModelOnTerrain(GameObject model, Vector3 position, UnityEngine.Terrain terrain)
        {
            if (model == null || terrain == null)
            {
                return null;
            }
            
            GameObject instance = Instantiate(model);
            
            // Sample terrain height at position
            float terrainHeight = terrain.SampleHeight(position);
            Vector3 terrainPosition = new Vector3(position.x, terrainHeight, position.z);
            
            // Apply grounding depth
            terrainPosition.y -= groundingDepth;
            
            instance.transform.position = terrainPosition;
            
            // Adapt to terrain normal if enabled
            if (adaptToTerrain)
            {
                Vector3 terrainNormal = terrain.terrainData.GetInterpolatedNormal(
                    position.x / terrain.terrainData.size.x,
                    position.z / terrain.terrainData.size.z
                );
                
                // Check slope angle
                float slope = Vector3.Angle(Vector3.up, terrainNormal);
                if (slope <= maxPlacementSlope)
                {
                    instance.transform.rotation = Quaternion.FromToRotation(Vector3.up, terrainNormal);
                }
            }
            
            return instance;
        }
        
        /// <summary>
        /// Place a model on terrain with custom rotation and scale.
        /// </summary>
        public GameObject PlaceModelOnTerrain(GameObject model, Vector3 position, Quaternion rotation, Vector3 scale, UnityEngine.Terrain terrain)
        {
            if (model == null || terrain == null)
            {
                return null;
            }
            
            GameObject instance = Instantiate(model);
            
            // Sample terrain height at position
            float terrainHeight = terrain.SampleHeight(position);
            Vector3 terrainPosition = new Vector3(position.x, terrainHeight, position.z);
            
            // Apply grounding depth
            terrainPosition.y -= groundingDepth;
            
            instance.transform.position = terrainPosition;
            instance.transform.rotation = rotation;
            instance.transform.localScale = scale;
            
            // Adapt to terrain normal if enabled (combine with provided rotation)
            if (adaptToTerrain)
            {
                Vector3 terrainNormal = terrain.terrainData.GetInterpolatedNormal(
                    position.x / terrain.terrainData.size.x,
                    position.z / terrain.terrainData.size.z
                );
                
                // Check slope angle
                float slope = Vector3.Angle(Vector3.up, terrainNormal);
                if (slope <= maxPlacementSlope)
                {
                    Quaternion terrainRotation = Quaternion.FromToRotation(Vector3.up, terrainNormal);
                    instance.transform.rotation = terrainRotation * rotation;
                }
            }
            
            return instance;
        }
        
        #endregion

        #region Object Grouping
        
        /// <summary>
        /// Group similar objects for efficient instancing.
        /// </summary>
        private Dictionary<string, List<DetectedObject>> GroupSimilarObjects(List<DetectedObject> objects)
        {
            var groups = new Dictionary<string, List<DetectedObject>>();
            
            if (!groupSimilarObjects)
            {
                // Create individual groups for each object
                for (int i = 0; i < objects.Count; i++)
                {
                    string key = $"{objects[i].className}_{i}";
                    groups[key] = new List<DetectedObject> { objects[i] };
                }
                return groups;
            }
            
            // Group by class name and similarity
            foreach (var obj in objects)
            {
                string groupKey = FindSimilarGroup(obj, groups);
                
                if (string.IsNullOrEmpty(groupKey))
                {
                    // Create new group
                    groupKey = $"{obj.className}_{groups.Count}";
                    groups[groupKey] = new List<DetectedObject>();
                }
                
                if (groups[groupKey].Count < maxGroupSize)
                {
                    groups[groupKey].Add(obj);
                }
                else
                {
                    // Create overflow group
                    string overflowKey = $"{obj.className}_{groups.Count}";
                    groups[overflowKey] = new List<DetectedObject> { obj };
                }
            }
            
            Log($"Grouped {objects.Count} objects into {groups.Count} groups", LogCategory.AI);
            return groups;
        }
        
        /// <summary>
        /// Find a similar group for the given object.
        /// </summary>
        private string FindSimilarGroup(DetectedObject obj, Dictionary<string, List<DetectedObject>> groups)
        {
            foreach (var kvp in groups)
            {
                if (kvp.Value.Count > 0)
                {
                    var representative = kvp.Value[0];
                    float similarity = CalculateObjectSimilarity(obj, representative);
                    
                    if (similarity >= instancingSimilarity)
                    {
                        return kvp.Key;
                    }
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Calculate similarity between two detected objects.
        /// </summary>
        private float CalculateObjectSimilarity(DetectedObject obj1, DetectedObject obj2)
        {
            // Basic similarity based on class name and size
            if (obj1.className != obj2.className)
            {
                return 0f;
            }
            
            // Calculate size similarity
            float sizeRatio = Mathf.Min(obj1.boundingBox.width, obj2.boundingBox.width) / 
                             Mathf.Max(obj1.boundingBox.width, obj2.boundingBox.width);
            
            float heightRatio = Mathf.Min(obj1.boundingBox.height, obj2.boundingBox.height) / 
                               Mathf.Max(obj1.boundingBox.height, obj2.boundingBox.height);
            
            return (sizeRatio + heightRatio) * 0.5f;
        }
        
        #endregion

        #region Generation Requests
        
        /// <summary>
        /// Create model generation requests for object groups.
        /// </summary>
        private List<ModelGenerationRequest> CreateGenerationRequests(
            Dictionary<string, List<DetectedObject>> objectGroups,
            Dictionary<string, string> enhancedDescriptions)
        {
            var requests = new List<ModelGenerationRequest>();
            
            foreach (var group in objectGroups)
            {
                var representative = group.Value[0];
                string description = enhancedDescriptions.ContainsKey(representative.className) 
                    ? enhancedDescriptions[representative.className]
                    : representative.shortDescription ?? $"3D model of {representative.className}";
                
                var request = new ModelGenerationRequest
                {
                    objectType = representative.className,
                    enhancedDescription = description,
                    detectedObjects = group.Value,
                    averageScale = CalculateAverageScale(group.Value),
                    confidence = group.Value.Average(o => o.confidence),
                    cacheKey = GenerateCacheKey(representative.className, description),
                    isProcessing = false,
                    taskId = null,
                    generatedModel = null
                };
                
                requests.Add(request);
            }
            
            Log($"Created {requests.Count} generation requests", LogCategory.AI);
            return requests;
        }
        
        /// <summary>
        /// Calculate average scale for a group of objects.
        /// </summary>
        private Vector3 CalculateAverageScale(List<DetectedObject> objects)
        {
            if (objects.Count == 0) return Vector3.one;
            
            Vector3 totalScale = Vector3.zero;
            foreach (var obj in objects)
            {
                // Estimate scale based on bounding box size
                float scale = Mathf.Sqrt(obj.boundingBox.width * obj.boundingBox.height) / 100f;
                totalScale += new Vector3(scale, scale, scale);
            }
            
            return totalScale / objects.Count;
        }
        
        /// <summary>
        /// Generate cache key for model caching.
        /// </summary>
        private string GenerateCacheKey(string objectType, string description)
        {
            return $"{objectType}_{description.GetHashCode()}";
        }
        
        #endregion

        #region Tripo3D Integration
        
        /// <summary>
        /// Submit generation requests to Tripo3D API.
        /// </summary>
        private IEnumerator SubmitGenerationRequests(List<ModelGenerationRequest> requests,
            System.Action<string, float> onProgress = null)
        {
            int completed = 0;
            
            foreach (var request in requests)
            {
                // Check cache first
                if (cacheModels && _modelCache.ContainsKey(request.cacheKey))
                {
                    request.generatedModel = _modelCache[request.cacheKey];
                    Log($"Using cached model for {request.objectType}", LogCategory.AI);
                    completed++;
                    continue;
                }
                
                // Submit to Tripo3D
                yield return StartCoroutine(SubmitSingleRequest(request));
                completed++;
                
                float progress = 0.3f + (completed / (float)requests.Count) * 0.2f; // 30% to 50%
                onProgress?.Invoke($"Submitted {completed}/{requests.Count} requests...", progress);
                
                // Respect rate limits
                yield return new WaitForSeconds(1f);
            }
        }
        
        /// <summary>
        /// Submit a single generation request to Tripo3D.
        /// </summary>
        private IEnumerator SubmitSingleRequest(ModelGenerationRequest request)
        {
            if (string.IsNullOrEmpty(tripoApiKey))
            {
                LogError("Tripo3D API key not set", LogCategory.AI);
                yield break;
            }
            
            // Prepare request data
            var requestData = new {
                type = "text_to_model",
                prompt = request.enhancedDescription,
                model_version = "v2.0-20240919",
                face_limit = 10000,
                texture_resolution = 1024
            };
            
            string jsonData = JsonUtility.ToJson(requestData);
            
            using (var webRequest = new UnityWebRequest($"{tripoApiUrl}/task", "POST"))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData));
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {tripoApiKey}");
                
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<TripoGenerationResponse>(webRequest.downloadHandler.text);
                        
                        if (response.code == 0 && response.data != null)
                        {
                            request.taskId = response.data.task_id;
                            request.isProcessing = true;
                            Log($"Submitted generation request for {request.objectType}, task ID: {request.taskId}", LogCategory.AI);
                        }
                        else
                        {
                            LogError($"Tripo3D API error: {response.message}", LogCategory.AI);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to parse Tripo3D response: {ex.Message}", LogCategory.AI);
                    }
                }
                else
                {
                    LogError($"Tripo3D request failed: {webRequest.error}", LogCategory.AI);
                }
            }
        }
        
        /// <summary>
        /// Wait for all generation requests to complete and download models.
        /// </summary>
        private IEnumerator WaitForGenerationCompletion(List<ModelGenerationRequest> requests,
            System.Action<string, float> onProgress = null)
        {
            float startTime = Time.time;
            var pendingRequests = requests.Where(r => r.isProcessing).ToList();
            
            while (pendingRequests.Count > 0 && (Time.time - startTime) < maxGenerationTime)
            {
                for (int i = pendingRequests.Count - 1; i >= 0; i--)
                {
                    var request = pendingRequests[i];
                    yield return StartCoroutine(CheckTaskStatus(request));
                    
                    if (!request.isProcessing)
                    {
                        pendingRequests.RemoveAt(i);
                    }
                    
                    yield return new WaitForSeconds(pollingInterval);
                }
                
                int completed = requests.Count - pendingRequests.Count;
                float progress = 0.5f + (completed / (float)requests.Count) * 0.3f; // 50% to 80%
                onProgress?.Invoke($"Generated {completed}/{requests.Count} models...", progress);
            }
            
            if (pendingRequests.Count > 0)
            {
                LogError($"Model generation timed out for {pendingRequests.Count} requests", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Check the status of a generation task.
        /// </summary>
        private IEnumerator CheckTaskStatus(ModelGenerationRequest request)
        {
            if (string.IsNullOrEmpty(request.taskId))
            {
                yield break;
            }
            
            using (var webRequest = UnityWebRequest.Get($"{tripoApiUrl}/task/{request.taskId}"))
            {
                webRequest.SetRequestHeader("Authorization", $"Bearer {tripoApiKey}");
                
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    var responseText = webRequest.downloadHandler.text;
                    yield return StartCoroutine(ProcessTaskStatusResponse(request, responseText));
                }
                else
                {
                    LogError($"Failed to check task status: {webRequest.error}", LogCategory.AI);
                }
            }
        }
        
        /// <summary>
        /// Process the task status response without try-catch around yield.
        /// </summary>
        private IEnumerator ProcessTaskStatusResponse(ModelGenerationRequest request, string responseText)
        {
            var response = ParseTaskStatusResponse(responseText);
            
            if (response != null && response.code == 0 && response.data != null)
            {
                switch (response.data.status)
                {
                    case "success":
                        if (response.data.result != null && !string.IsNullOrEmpty(response.data.result.model_url))
                        {
                            yield return StartCoroutine(DownloadModel(request, response.data.result.model_url));
                        }
                        request.isProcessing = false;
                        break;
                        
                    case "failed":
                        LogError($"Model generation failed for {request.objectType}", LogCategory.AI);
                        request.isProcessing = false;
                        break;
                        
                    case "queued":
                    case "running":
                        // Still processing, continue waiting
                        break;
                }
            }
        }
        
        /// <summary>
        /// Parse task status response with error handling.
        /// </summary>
        private TripoGenerationResponse ParseTaskStatusResponse(string responseText)
        {
            try
            {
                return JsonUtility.FromJson<TripoGenerationResponse>(responseText);
            }
            catch (Exception ex)
            {
                LogError($"Failed to parse task status response: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Download a generated model from Tripo3D.
        /// </summary>
        private IEnumerator DownloadModel(ModelGenerationRequest request, string modelUrl)
        {
            using (var webRequest = UnityWebRequest.Get(modelUrl))
            {
                yield return webRequest.SendWebRequest();
                
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    var modelData = webRequest.downloadHandler.data;
                    yield return StartCoroutine(ProcessDownloadedModel(request, modelData));
                }
                else
                {
                    LogError($"Failed to download model from {modelUrl}: {webRequest.error}", LogCategory.AI);
                }
            }
        }
        
        /// <summary>
        /// Process downloaded model data without try-catch around yield.
        /// </summary>
        private IEnumerator ProcessDownloadedModel(ModelGenerationRequest request, byte[] modelData)
        {
            var filePath = SaveModelToFile(request, modelData);
            if (!string.IsNullOrEmpty(filePath))
            {
                yield return StartCoroutine(LoadModelFromFile(request, filePath));
                Log($"Downloaded and loaded model for {request.objectType}", LogCategory.AI);
            }
        }
        
        /// <summary>
        /// Save model data to file with error handling.
        /// </summary>
        private string SaveModelToFile(ModelGenerationRequest request, byte[] modelData)
        {
            try
            {
                string fileName = $"{request.objectType}_{request.taskId}.glb";
                string filePath = Path.Combine(Application.persistentDataPath, "GeneratedModels", fileName);
                
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllBytes(filePath, modelData);
                
                return filePath;
            }
            catch (Exception ex)
            {
                LogError($"Failed to save model to file: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Load a model from file using Piglet.
        /// </summary>
        private IEnumerator LoadModelFromFile(ModelGenerationRequest request, string filePath)
        {
            bool loadComplete = false;
            GameObject loadedModel = null;
            string errorMessage = null;
            
            var loadResult = LoadModelWithPiglet(filePath, 
                (model) => { loadedModel = model; loadComplete = true; },
                (error) => { errorMessage = error; loadComplete = true; });
            
            if (!loadResult)
            {
                errorMessage = "Failed to initialize Piglet import";
                loadComplete = true;
            }
            
            // Wait for import to complete
            while (!loadComplete)
            {
                yield return null;
            }
            
            if (!string.IsNullOrEmpty(errorMessage))
            {
                LogError($"Failed to load model: {errorMessage}", LogCategory.AI);
            }
            else if (loadedModel != null)
            {
                request.generatedModel = loadedModel;
                
                // Add to cache
                if (cacheModels && !_modelCache.ContainsKey(request.cacheKey))
                {
                    _modelCache[request.cacheKey] = loadedModel;
                    
                    // Manage cache size
                    if (_modelCache.Count > maxCacheSize)
                    {
                        var oldestKey = _modelCache.Keys.First();
                        Destroy(_modelCache[oldestKey]);
                        _modelCache.Remove(oldestKey);
                    }
                }
            }
        }
        
        #endregion
    }
}
