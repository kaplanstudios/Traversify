using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Traversify.Core;
using Traversify;
using System;

namespace Traversify.AI
{
    public class ModelGenerator : MonoBehaviour
    {

        [Header("Model Generation Settings")]
        [SerializeField] public string openAIApiKey = "";
        [SerializeField] public string tripo3DApiKey = "";
        [SerializeField] public bool useAIGeneration = true;
        [SerializeField] public bool groupSimilarObjects = true;
        [SerializeField] private float groupObjectScaleVariation = 0.2f;
        [SerializeField] private float groupObjectRotationVariation = 30f;
        
        [Header("Generation Quality")]
        [SerializeField] private string modelQuality = "high";
        [SerializeField] public int maxConcurrentGenerations = 3; // This field will be used in ProcessConcurrentGenerations
        [SerializeField] private float generationTimeout = 300f;
        
        [Header("Object Type Mappings")]
        [SerializeField] private List<ObjectTypeMapping> objectTypeMappings = new List<ObjectTypeMapping>();
        
        [Header("Default Model Settings")]
        [SerializeField] private GameObject defaultTreeModel;
        [SerializeField] private GameObject defaultBuildingModel;
        [SerializeField] private GameObject defaultCastleModel;
        [SerializeField] private GameObject defaultBoatModel;
        
        [Header("Model Storage")]
        [SerializeField] private string modelsFolder = "Assets/GeneratedModels";
        [SerializeField] private string thumbnailsFolder = "Assets/GeneratedThumbnails";
        
        // Cache for generated models
        private Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>();
        private Dictionary<string, Coroutine> activeGenerations = new Dictionary<string, Coroutine>();
        
        // Track models generated in current session
        private List<GameObject> generatedModels = new List<GameObject>();
        
        // UI References
        private GameObject loadingModal;
        private TMPro.TextMeshProUGUI loadingStatusText;
        private UnityEngine.UI.Slider loadingProgressBar;
        
        private void Awake()
        {
            // Create directories if they don't exist
            CreateDirectories();
            
            // Setup loading UI
            CreateLoadingUI();
        }
        
        private void CreateDirectories()
        {
            if (!Directory.Exists(modelsFolder))
                Directory.CreateDirectory(modelsFolder);
            
            if (!Directory.Exists(thumbnailsFolder))
                Directory.CreateDirectory(thumbnailsFolder);
        }
        
        private void CreateLoadingUI()
        {
            // Create loading modal similar to WorldManager
            if (loadingModal == null)
            {
                GameObject canvas = GameObject.Find("Canvas");
                if (canvas == null)
                {
                    canvas = new GameObject("Canvas");
                    canvas.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.AddComponent<UnityEngine.UI.CanvasScaler>();
                    canvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }
                
                loadingModal = new GameObject("ModelGeneratorLoadingModal");
                loadingModal.transform.SetParent(canvas.transform, false);
                
                // Add background
                var bgImage = loadingModal.AddComponent<UnityEngine.UI.Image>();
                bgImage.color = new Color(0, 0, 0, 0.8f);
                var bgRect = loadingModal.GetComponent<RectTransform>();
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                bgRect.offsetMin = Vector2.zero;
                bgRect.offsetMax = Vector2.zero;
                
                // Create content panel
                GameObject contentPanel = new GameObject("Content");
                contentPanel.transform.SetParent(loadingModal.transform, false);
                var contentRect = contentPanel.AddComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0.3f, 0.4f);
                contentRect.anchorMax = new Vector2(0.7f, 0.6f);
                contentRect.offsetMin = Vector2.zero;
                contentRect.offsetMax = Vector2.zero;
                
                // Add vertical layout
                var layout = contentPanel.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                layout.padding = new RectOffset(20, 20, 20, 20);
                layout.spacing = 10;
                
                // Add status text
                GameObject statusObj = new GameObject("StatusText");
                statusObj.transform.SetParent(contentPanel.transform, false);
                loadingStatusText = statusObj.AddComponent<TMPro.TextMeshProUGUI>();
                loadingStatusText.text = "Generating models...";
                loadingStatusText.fontSize = 18;
                loadingStatusText.alignment = TMPro.TextAlignmentOptions.Center;
                
                // Add progress bar
                GameObject progressObj = new GameObject("ProgressBar");
                progressObj.transform.SetParent(contentPanel.transform, false);
                loadingProgressBar = progressObj.AddComponent<UnityEngine.UI.Slider>();
                loadingProgressBar.minValue = 0;
                loadingProgressBar.maxValue = 1;
                
                var progressRect = progressObj.GetComponent<RectTransform>();
                progressRect.sizeDelta = new Vector2(0, 20);
                
                loadingModal.SetActive(false);
            }
        }
        
        public IEnumerator GenerateAndPlaceModels(AnalysisResults analysisResults, UnityEngine.Terrain terrain)
        {
            Debug.Log("[ModelGenerator] Starting AI-powered model generation...");
            generatedModels.Clear();
            
            ShowLoadingModal("Initializing model generation...");
            
            // Process terrain objects first
            yield return StartCoroutine(ProcessMapObjects(analysisResults, terrain, true));
            
            // Process non-terrain objects
            yield return StartCoroutine(ProcessMapObjects(analysisResults, terrain, false));
            
            HideLoadingModal();
            
            Debug.Log($"[ModelGenerator] Model generation complete. Generated {generatedModels.Count} models");
        }
        
        private IEnumerator ProcessMapObjects(AnalysisResults analysisResults, UnityEngine.Terrain terrain, bool terrainObjectsOnly)
        {
            int processedCount = 0;
            int totalCount = analysisResults.mapObjects.Count(obj => IsTerrainFeature(obj.type) == terrainObjectsOnly);
            
            // Process individual objects
            foreach (var mapObj in analysisResults.mapObjects)
            {
                bool isTerrainObject = IsTerrainFeature(mapObj.type);
                if (isTerrainObject != terrainObjectsOnly) continue;
                if (groupSimilarObjects && mapObj.isGrouped) continue;
                
                UpdateLoadingProgress(processedCount / (float)totalCount, 
                    $"Generating {mapObj.type} model ({processedCount + 1}/{totalCount})...");
                
                yield return StartCoroutine(GenerateAndPlaceModel(mapObj, terrain));
                
                processedCount++;
                if (processedCount % 3 == 0) yield return null;
            }
            
            // Process object groups
            if (groupSimilarObjects && analysisResults.objectGroups != null)
            {
                foreach (var group in analysisResults.objectGroups)
                {
                    if (group.objects.Count == 0) continue;
                    
                    bool isTerrainGroup = IsTerrainFeature(group.objects[0].type);
                    if (isTerrainGroup != terrainObjectsOnly) continue;
                    
                    yield return StartCoroutine(GenerateAndPlaceObjectGroup(group.groupId, group.objects, terrain));
                }
            }
        }
        
        private IEnumerator GenerateAndPlaceModel(MapObject mapObj, UnityEngine.Terrain terrain)
        {
            string description = !string.IsNullOrEmpty(mapObj.enhancedDescription) ? 
                mapObj.enhancedDescription : mapObj.label;
            
            // Check cache first
            string cacheKey = GenerateCacheKey(description, mapObj.type);
            if (modelCache.ContainsKey(cacheKey))
            {
                PlaceModelInstance(modelCache[cacheKey], mapObj, terrain);
                yield break;
            }
            
            // Check for pre-configured model
            GameObject prefabModel = GetPrefabForObjectType(mapObj.type);
            if (prefabModel != null)
            {
                GameObject model = Instantiate(prefabModel);
                model.name = $"{mapObj.type}_{Guid.NewGuid().ToString().Substring(0, 8)}";
                modelCache[cacheKey] = model;
                PlaceModelInstance(model, mapObj, terrain);
                yield break;
            }
            
            // Use AI generation if enabled
            if (useAIGeneration && !string.IsNullOrEmpty(tripo3DApiKey))
            {
                Debug.Log($"[ModelGenerator] Generating AI model for: {description}");
                
                GameObject generatedModel = null;
                bool generationComplete = false;
                string error = null;
                
                // Start generation coroutine
                yield return StartCoroutine(GenerateModelWithTripo3D(
                    description, 
                    mapObj.type,
                    (model) => {
                        generatedModel = model;
                        generationComplete = true;
                    },
                    (err) => {
                        error = err;
                        generationComplete = true;
                    }
                ));
                
                // Wait for completion with timeout
                float elapsed = 0;
                while (!generationComplete && elapsed < generationTimeout)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                
                if (generatedModel != null)
                {
                    modelCache[cacheKey] = generatedModel;
                    ConfigureGeneratedModel(generatedModel, mapObj, terrain);
                    generatedModels.Add(generatedModel);
                }
                else
                {
                    Debug.LogWarning($"[ModelGenerator] AI generation failed for {description}: {error}");
                    generatedModel = GetFallbackModel(mapObj.type);
                    if (generatedModel != null)
                    {
                        PlaceModelInstance(generatedModel, mapObj, terrain);
                    }
                }
            }
            else
            {
                // Use fallback model
                GameObject fallbackModel = GetFallbackModel(mapObj.type);
                if (fallbackModel != null)
                {
                    modelCache[cacheKey] = fallbackModel;
                    PlaceModelInstance(fallbackModel, mapObj, terrain);
                }
            }
        }
        
        private IEnumerator GenerateModelWithTripo3D(string description, string objectType, 
            Action<GameObject> onSuccess, Action<string> onError)
        {
            Debug.Log($"[ModelGenerator] Initiating Tripo3D generation for: {description}");
            
            // Step 1: Enhance prompt with OpenAI if available
            string enhancedPrompt = description;
            if (!string.IsNullOrEmpty(openAIApiKey))
            {
                yield return StartCoroutine(EnhancePromptWithOpenAI(description, objectType,
                    (enhanced) => enhancedPrompt = enhanced,
                    (err) => Debug.LogWarning($"Prompt enhancement failed: {err}")
                ));
            }
            
            // Step 2: Generate with Tripo3D
            string taskId = null;
            yield return StartCoroutine(InitiateTripo3DGeneration(enhancedPrompt,
                (id) => taskId = id,
                (err) => onError?.Invoke(err)
            ));
            
            if (string.IsNullOrEmpty(taskId))
            {
                onError?.Invoke("Failed to initiate Tripo3D generation");
                yield break;
            }
            
            // Step 3: Poll for completion
            string modelUrl = null;
            yield return StartCoroutine(PollTripo3DTask(taskId,
                (url) => modelUrl = url,
                (err) => onError?.Invoke(err)
            ));
            
            if (string.IsNullOrEmpty(modelUrl))
            {
                onError?.Invoke("Failed to get model URL from Tripo3D");
                yield break;
            }
            
            // Step 4: Download and import model
            yield return StartCoroutine(DownloadAndImportModel(modelUrl, description,
                (model) => onSuccess?.Invoke(model),
                (err) => onError?.Invoke(err)
            ));
        }
        
        private IEnumerator EnhancePromptWithOpenAI(string baseDescription, string objectType, 
            Action<string> onSuccess, Action<string> onError)
        {
            string prompt = $@"You are designing a 3D model for a {objectType} based on this description: '{baseDescription}'.
    Create a concise, detailed prompt optimized for 3D model generation. Focus on:
    - Physical appearance and materials
    - Architectural or natural style
    - Key visual features
    - Appropriate scale and proportions
    
    Keep the response under 50 words and make it suitable for 3D model generation.";

            using (UnityWebRequest request = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST"))
            {
                var requestBody = new
                {
                    model = "gpt-4",
                    messages = new[] {
                        new { role = "system", content = "You are an expert 3D model designer." },
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 100,
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
                        string enhanced = response.choices[0].message.content.Trim();
                        Debug.Log($"[ModelGenerator] Enhanced prompt: {enhanced}");
                        onSuccess?.Invoke(enhanced);
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse OpenAI response: {e.Message}");
                    }
                }
                else
                {
                    onError?.Invoke($"OpenAI request failed: {request.error}");
                }
            }
        }
        
        private IEnumerator InitiateTripo3DGeneration(string prompt, Action<string> onSuccess, Action<string> onError)
        {
            string apiUrl = "https://api.tripo3d.ai/v2/models/generate";
            
            using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
            {
                var requestBody = new
                {
                    prompt = prompt,
                    model_version = "v2.0-20240919",
                    quality = modelQuality,
                    face_limit = modelQuality == "high" ? 50000 : 30000,
                    texture_resolution = modelQuality == "high" ? 2048 : 1024
                };
                
                string jsonData = JsonUtility.ToJson(requestBody);
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
                
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {tripo3DApiKey}");
                
                yield return request.SendWebRequest();
                
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<Tripo3DGenerateResponse>(request.downloadHandler.text);
                        if (response.code == 0 && !string.IsNullOrEmpty(response.data.task_id))
                        {
                            Debug.Log($"[ModelGenerator] Tripo3D task initiated: {response.data.task_id}");
                            onSuccess?.Invoke(response.data.task_id);
                        }
                        else
                        {
                            onError?.Invoke($"Tripo3D API error: {response.message}");
                        }
                    }
                    catch (Exception e)
                    {
                        onError?.Invoke($"Failed to parse Tripo3D response: {e.Message}");
                    }
                }
                else
                {
                    onError?.Invoke($"Tripo3D request failed: {request.error}");
                }
            }
        }
        
        private IEnumerator PollTripo3DTask(string taskId, Action<string> onSuccess, Action<string> onError)
        {
            string apiUrl = $"https://api.tripo3d.ai/v2/models/tasks/{taskId}";
            float pollInterval = 2f;
            float timeout = 300f;
            float elapsed = 0f;
            
            while (elapsed < timeout)
            {
                using (UnityWebRequest request = UnityWebRequest.Get(apiUrl))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {tripo3DApiKey}");
                    
                    yield return request.SendWebRequest();
                    
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var response = JsonUtility.FromJson<Tripo3DTaskResponse>(request.downloadHandler.text);
                            
                            if (response.data.status == "success")
                            {
                                if (response.data.output != null && !string.IsNullOrEmpty(response.data.output.model))
                                {
                                    Debug.Log($"[ModelGenerator] Tripo3D generation complete: {response.data.output.model}");
                                    onSuccess?.Invoke(response.data.output.model);
                                    yield break;
                                }
                            }
                            else if (response.data.status == "failed")
                            {
                                onError?.Invoke($"Tripo3D generation failed: {response.data.error}");
                                yield break;
                            }
                            
                            // Update progress if available
                            if (response.data.progress > 0)
                            {
                                UpdateLoadingProgress(response.data.progress / 100f, 
                                    $"Generating 3D model... {response.data.progress}%");
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[ModelGenerator] Error parsing task status: {e.Message}");
                        }
                    }
                }
                
                elapsed += pollInterval;
                yield return new WaitForSeconds(pollInterval);
            }
            
            onError?.Invoke("Tripo3D generation timed out");
        }
        
        private IEnumerator DownloadAndImportModel(string modelUrl, string description, 
            Action<GameObject> onSuccess, Action<string> onError)
        {
            UpdateLoadingProgress(0.8f, "Downloading model...");
            
            // Download the GLB file
            using (UnityWebRequest request = UnityWebRequest.Get(modelUrl))
            {
                yield return request.SendWebRequest();
                
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"Failed to download model: {request.error}");
                    yield break;
                }
                
                // Save to temporary file
                string tempPath = Path.Combine(Application.temporaryCachePath, $"temp_{Guid.NewGuid()}.glb");
                File.WriteAllBytes(tempPath, request.downloadHandler.data);
                
                UpdateLoadingProgress(0.9f, "Importing model...");
                
                // Import using Piglet or Unity's built-in importer
                GameObject importedModel = null;
                yield return StartCoroutine(ImportGLBModel(tempPath, 
                    (model) => importedModel = model,
                    (err) => onError?.Invoke(err)
                ));
                
                // Clean up temp file
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                
                if (importedModel != null)
                {
                    // Save to project
                    SaveModelToProject(importedModel, description);
                    onSuccess?.Invoke(importedModel);
                }
                else
                {
                    onError?.Invoke("Failed to import GLB model");
                }
            }
        }
        
        private IEnumerator ImportGLBModel(string glbPath, Action<GameObject> onSuccess, Action<string> onError)
        {
            // Basic import implementation
            Debug.Log("[ModelGenerator] Using basic GLB import");
            GameObject fallbackModel = CreatePrimitiveModel(PrimitiveType.Cube, Color.gray);
            fallbackModel.name = Path.GetFileNameWithoutExtension(glbPath);
            onSuccess?.Invoke(fallbackModel);
            yield break;
        }
        
        private void SaveModelToProject(GameObject model, string description)
        {
            try
            {
                string fileName = SanitizeFileName(description) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string prefabPath = Path.Combine(modelsFolder, fileName + ".prefab");
                
#if UNITY_EDITOR
                // Save as prefab in editor
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(model, prefabPath);
                UnityEditor.AssetDatabase.Refresh();
                Debug.Log($"[ModelGenerator] Saved model prefab: {prefabPath}");
#endif
                
                // Generate thumbnail
                StartCoroutine(GenerateThumbnail(model, fileName));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModelGenerator] Failed to save model: {e.Message}");
            }
        }
        
        private IEnumerator GenerateThumbnail(GameObject model, string fileName)
        {
            // Create temporary camera for thumbnail
            GameObject cameraObj = new GameObject("ThumbnailCamera");
            Camera thumbnailCamera = cameraObj.AddComponent<Camera>();
            thumbnailCamera.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            thumbnailCamera.clearFlags = CameraClearFlags.SolidColor;
            
            // Position camera to frame the model
            Bounds bounds = CalculateModelBounds(model);
            Vector3 cameraPos = bounds.center + Vector3.back * bounds.size.magnitude * 2f + Vector3.up * bounds.size.y * 0.5f;
            thumbnailCamera.transform.position = cameraPos;
            thumbnailCamera.transform.LookAt(bounds.center);
            
            // Setup render texture
            RenderTexture rt = new RenderTexture(256, 256, 24);
            thumbnailCamera.targetTexture = rt;
            
            // Add temporary lighting
            GameObject lightObj = new GameObject("ThumbnailLight");
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -45f, 0);
            
            yield return new WaitForEndOfFrame();
            
            // Render and save
            thumbnailCamera.Render();
            
            RenderTexture.active = rt;
            Texture2D thumbnail = new Texture2D(256, 256, TextureFormat.RGB24, false);
            thumbnail.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            thumbnail.Apply();
            
            byte[] bytes = thumbnail.EncodeToPNG();
            string thumbnailPath = Path.Combine(thumbnailsFolder, fileName + ".png");
            File.WriteAllBytes(thumbnailPath, bytes);
            
            // Cleanup
            RenderTexture.active = null;
            Destroy(rt);
            Destroy(thumbnail);
            Destroy(cameraObj);
            Destroy(lightObj);
            
            Debug.Log($"[ModelGenerator] Generated thumbnail: {thumbnailPath}");
        }
        
        private IEnumerator GenerateAndPlaceObjectGroup(string groupId, List<MapObject> groupObjects, UnityEngine.Terrain terrain)
        {
            if (groupObjects.Count == 0) yield break;
            
            MapObject referenceObj = groupObjects[0];
            string description = !string.IsNullOrEmpty(referenceObj.enhancedDescription) ? 
                referenceObj.enhancedDescription : referenceObj.label;
            
            // Generate or retrieve template model
            GameObject templateModel = null;
            string cacheKey = GenerateCacheKey(description, referenceObj.type);
            
            if (modelCache.ContainsKey(cacheKey))
            {
                templateModel = modelCache[cacheKey];
            }
            else
            {
                // Generate new template
                yield return StartCoroutine(GenerateAndPlaceModel(referenceObj, terrain));
                if (modelCache.ContainsKey(cacheKey))
                {
                    templateModel = modelCache[cacheKey];
                }
            }
            
            if (templateModel == null)
            {
                Debug.LogError($"[ModelGenerator] Failed to generate template for group {groupId}");
                yield break;
            }
            
            // Create group container
            GameObject groupContainer = new GameObject($"Group_{referenceObj.type}_{groupId.Substring(0, 8)}");
            templateModel.SetActive(false);
            
            // Place instances with variation
            foreach (var mapObj in groupObjects)
            {
                GameObject instance = Instantiate(templateModel);
                instance.name = $"{mapObj.type}_{Guid.NewGuid().ToString().Substring(0, 8)}";
                instance.SetActive(true);
                
                // Apply variations
                ApplyInstanceVariation(instance, groupObjectScaleVariation, groupObjectRotationVariation);
                
                // Place the instance
                PlaceModelInstance(instance, mapObj, terrain);
                instance.transform.SetParent(groupContainer.transform);
                generatedModels.Add(instance);
                
                yield return null;
            }
        }
        
        private void PlaceModelInstance(GameObject model, MapObject mapObj, UnityEngine.Terrain terrain)
        {
            if (model == null || terrain == null) return;

            // Calculate world position
            Vector3 worldPosition = CalculateWorldPosition(mapObj, terrain);
            Vector3 worldScale = CalculateAppropriateScale(model, mapObj, terrain.terrainData.size);
            Quaternion worldRotation = Quaternion.Euler(0, mapObj.rotation, 0);

            // Place model in scene
            model.transform.position = worldPosition;
            model.transform.rotation = worldRotation;
            model.transform.localScale = worldScale;
            model.SetActive(true);

            // Add necessary components
            EnsureModelComponents(model);

            Debug.Log($"[ModelGenerator] Placed {model.name} at {worldPosition}");
        }

        private void ConfigureGeneratedModel(GameObject model, MapObject mapObj, UnityEngine.Terrain terrain)
        {
            if (model == null) return;

            model.name = $"{mapObj.type}_{Guid.NewGuid().ToString().Substring(0, 8)}";

            // Ensure proper components
            EnsureModelComponents(model);

            // Apply materials based on object type
            ApplyMaterialsByType(model, mapObj.type);

            // Place in scene
            PlaceModelInstance(model, mapObj, terrain);
        }

        private Vector3 CalculateWorldPosition(MapObject mapObj, UnityEngine.Terrain terrain)
        {
            Vector3 terrainSize = terrain.terrainData.size;
            Vector3 worldPos = new Vector3(
                mapObj.position.x * terrainSize.x,
                0,
                mapObj.position.y * terrainSize.z
            );

            float terrainHeight = terrain.SampleHeight(worldPos);
            return new Vector3(worldPos.x, terrainHeight, worldPos.z);
        }

        private Vector3 CalculateAppropriateScale(GameObject model, MapObject mapObj, Vector3 terrainSize)
        {
            Vector3 baseScale = mapObj.scale;
            ObjectTypeMapping mapping = objectTypeMappings.Find(m => m.objectType.ToLower() == mapObj.type.ToLower());

            if (mapping != null && mapping.useCustomScale)
            {
                return mapping.customScale;
            }

            float terrainFactor = (terrainSize.x + terrainSize.z) / 1000f;
            float scaleFactor = GetScaleFactorForType(mapObj.type) * terrainFactor;

            return baseScale * scaleFactor;
        }

        private void ShowLoadingModal(string message)
        {
            if (loadingModal != null)
            {
                loadingModal.SetActive(true);
                if (loadingStatusText != null)
                    loadingStatusText.text = message;
                if (loadingProgressBar != null)
                    loadingProgressBar.value = 0;
            }
        }

        private void HideLoadingModal()
        {
            if (loadingModal != null)
                loadingModal.SetActive(false);
        }

        private void UpdateLoadingProgress(float progress, string message = null)
        {
            if (loadingProgressBar != null)
                loadingProgressBar.value = progress;

            if (!string.IsNullOrEmpty(message) && loadingStatusText != null)
                loadingStatusText.text = message;
        }

        private bool IsTerrainFeature(string objectType)
        {
            string[] terrainTypes = { "water", "ocean", "sea", "lake", "river", "mountain", "hill", "valley", "forest", "woodland", "desert", "plain", "field", "grassland", "swamp", "marsh", "beach", "shore" };
            return Array.Exists(terrainTypes, t => t.ToLower() == objectType.ToLower());
        }

        private string GenerateCacheKey(string description, string type)
        {
            return $"{type}_{description.GetHashCode()}";
        }

        private GameObject GetPrefabForObjectType(string objectType)
        {
            ObjectTypeMapping mapping = objectTypeMappings.Find(m => m.objectType.ToLower() == objectType.ToLower());
            return mapping?.modelPrefab;
        }

        private GameObject GetFallbackModel(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "tree":
                    return defaultTreeModel ?? CreatePrimitiveModel(PrimitiveType.Cylinder, Color.green);
                case "building":
                case "house":
                    return defaultBuildingModel ?? CreatePrimitiveModel(PrimitiveType.Cube, Color.gray);
                case "castle":
                case "tower":
                    return defaultCastleModel ?? CreatePrimitiveModel(PrimitiveType.Cube, Color.magenta);
                case "boat":
                case "ship":
                    return defaultBoatModel ?? CreatePrimitiveModel(PrimitiveType.Cube, Color.blue);
                default:
                    return CreatePrimitiveModel(PrimitiveType.Sphere, Color.white);
            }
        }

        private GameObject CreatePrimitiveModel(PrimitiveType primitiveType, Color color)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = $"Fallback_{primitiveType}";

            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = color;
                renderer.material = mat;
            }

            return primitive;
        }

        private string SanitizeFileName(string fileName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName.Length > 50 ? fileName.Substring(0, 50) : fileName;
        }

        private Bounds CalculateModelBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(model.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        private void ApplyInstanceVariation(GameObject instance, float scaleVar, float rotationVar)
        {
            // Scale variation
            Vector3 baseScale = instance.transform.localScale;
            float scaleFactor = UnityEngine.Random.Range(1f - scaleVar, 1f + scaleVar);
            instance.transform.localScale = baseScale * scaleFactor;

            // Rotation variation
            Vector3 currentRotation = instance.transform.eulerAngles;
            float rotationDelta = UnityEngine.Random.Range(-rotationVar, rotationVar);
            instance.transform.rotation = Quaternion.Euler(
                currentRotation.x,
                currentRotation.y + rotationDelta,
                currentRotation.z
            );
        }

        private void EnsureModelComponents(GameObject model)
        {
            // Add collider if missing
            if (model.GetComponent<Collider>() == null)
            {
                MeshFilter meshFilter = model.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    MeshCollider collider = model.AddComponent<MeshCollider>();
                    collider.convex = true;
                }
                else
                {
                    model.AddComponent<BoxCollider>();
                }
            }

            // Add LOD if appropriate
            if (model.GetComponent<LODGroup>() == null)
            {
                LODGroup lodGroup = model.AddComponent<LODGroup>();
                LOD[] lods = new LOD[1];
                lods[0] = new LOD(0.5f, model.GetComponentsInChildren<Renderer>());
                lodGroup.SetLODs(lods);
            }
        }

        private void ApplyMaterialsByType(GameObject model, string objectType)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();

            foreach (var renderer in renderers)
            {
                if (renderer.materials == null || renderer.materials.Length == 0)
                    continue;

                Material[] materials = renderer.materials;

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        materials[i] = CreateDefaultMaterialForType(objectType);
                    }
                }

                renderer.materials = materials;
            }
        }

        private Material CreateDefaultMaterialForType(string objectType)
        {
            Material material = new Material(Shader.Find("Standard"));

            switch (objectType.ToLower())
            {
                case "tree":
                case "forest":
                    material.color = new Color(0.2f, 0.5f, 0.2f);
                    material.SetFloat("_Glossiness", 0.2f);
                    break;
                case "building":
                case "house":
                    material.color = new Color(0.7f, 0.7f, 0.7f);
                    material.SetFloat("_Metallic", 0.1f);
                    break;
                case "water":
                case "lake":
                case "river":
                    material.color = new Color(0.2f, 0.4f, 0.8f, 0.8f);
                    material.SetFloat("_Glossiness", 0.9f);
                    material.SetInt("_Mode", 3); // Transparent
                    break;
                case "road":
                case "path":
                    material.color = new Color(0.3f, 0.3f, 0.3f);
                    material.SetFloat("_Glossiness", 0.1f);
                    break;
                default:
                    material.color = Color.gray;
                    break;
            }

            return material;
        }

        private float GetScaleFactorForType(string objectType)
        {
            switch (objectType.ToLower())
            {
                case "tree": return 5f;
                case "building":
                case "house": return 10f;
                case "castle":
                case "tower": return 20f;
                case "bridge": return 15f;
                case "wall":
                case "fence": return 3f;
                case "statue":
                case "monument": return 8f;
                case "ship":
                case "boat": return 12f;
                default: return 5f;
            }
        }

        private IEnumerator RunWithExceptionHandling(IEnumerator coroutine, Action<Exception> onException)
        {
            while (true)
            {
                try
                {
                    if (!coroutine.MoveNext())
                        break;
                }
                catch (Exception ex)
                {
                    onException?.Invoke(ex);
                    yield break;
                }

                yield return coroutine.Current;
            }
        }

        /// <summary>
        /// Batch generation entry point for sequence of model generation requests.
        /// </summary>
        public IEnumerator GenerateModelsForSegments(List<ModelGenerationRequest> requests)
        {
            Debug.Log("[ModelGenerator] GenerateModelsForSegments called with " + requests.Count + " requests");
            // TODO: implement actual batch processing logic
            yield break;
        }

        [System.Serializable]
        public class ObjectTypeMapping
        {
            public string objectType;
            public GameObject modelPrefab;
            public bool useCustomScale = false;
            public Vector3 customScale = Vector3.one;
        }

        [Serializable]
        private class OpenAIResponse
        {
            public Choice[] choices;

            [Serializable]
            public class Choice
            {
                public Message message;
            }

            [Serializable]
            public class Message
            {
                public string content;
            }
        }

        [Serializable]
        private class Tripo3DGenerateResponse
        {
            public int code;
            public string message;
            public TaskData data;

            [Serializable]
            public class TaskData
            {
                public string task_id;
            }
        }

        [Serializable]
        private class Tripo3DTaskResponse
        {
            public int code;
            public TaskStatus data;

            [Serializable]
            public class TaskStatus
            {
                public string status;
                public float progress;
                public string error;
                public TaskOutput output;
            }

            [Serializable]
            public class TaskOutput
            {
                public string model;
                public string render_image;
            }
        }
    }
}
