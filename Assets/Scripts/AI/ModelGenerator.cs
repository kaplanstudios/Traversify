using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;
using Traversify;

public class ModelGenerator : MonoBehaviour
{
    [Header("Model Generation Settings")]
    [SerializeField] private string openAIApiKey = "";
    [SerializeField] private bool groupSimilarObjects = true;
    [SerializeField] private float groupObjectScaleVariation = 0.2f;
    [SerializeField] private float groupObjectRotationVariation = 30f;
    
    [Header("Object Type Mappings")]
    [SerializeField] private List<ObjectTypeMapping> objectTypeMappings = new List<ObjectTypeMapping>();
    
    [Header("Default Model Settings")]
    [SerializeField] private GameObject defaultTreeModel;
    [SerializeField] private GameObject defaultBuildingModel;
    [SerializeField] private GameObject defaultCastleModel;
    [SerializeField] private GameObject defaultBoatModel;
    
    [Header("AI Integration")]
    [SerializeField] private bool useAIGeneration = true;
    [SerializeField] private string tripo3DApiKey = "";
    
    // Reference to the TraversifyMain
    private TraversifyMain traversifyMain;
    
    // Reference to WorldManager for AI generation
    private WorldManager worldManager;
    
    // Cache for generated models to avoid duplicate requests
    private Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>();
    
    // Track models generated in current session
    private List<GameObject> generatedModels = new List<GameObject>();
    
    private void Awake()
    {
        // Find or create TraversifyManager
        traversifyMain = FindObjectOfType<TraversifyMain>();
        if (traversifyMain == null)
        {
            GameObject traversifyManagerObj = new GameObject("TraversifyManager");
            traversifyMain = traversifyManagerObj.AddComponent<TraversifyMain>();
            Debug.Log("[ModelGenerator] Created TraversifyManager instance");
        }
        
        // Find or create WorldManager
        worldManager = WorldManager.Instance;
        if (worldManager == null)
        {
            Debug.LogWarning("[ModelGenerator] WorldManager not found, AI generation will be disabled");
            useAIGeneration = false;
        }
        else
        {
            Debug.Log("[ModelGenerator] WorldManager found, AI generation enabled");
        }
    }
    
    public IEnumerator GenerateAndPlaceModels(AnalysisResults analysisResults, Terrain terrain)
    {
        Debug.Log("[ModelGenerator] Starting model generation...");
        generatedModels.Clear();
        
        // Process terrain objects first for better organization
        yield return StartCoroutine(ProcessMapObjects(analysisResults, terrain, true));
        
        // Process non-terrain objects
        yield return StartCoroutine(ProcessMapObjects(analysisResults, terrain, false));
        
        Debug.Log($"[ModelGenerator] Model generation complete. Generated {generatedModels.Count} models");
    }
    
    private IEnumerator ProcessMapObjects(AnalysisResults analysisResults, Terrain terrain, bool terrainObjectsOnly)
    {
        int processedCount = 0;
        
        // Process individual objects first
        foreach (var mapObj in analysisResults.mapObjects)
        {
            bool isTerrainObject = IsTerrainFeature(mapObj.type);
            
            // Skip if not matching the current filter
            if (isTerrainObject != terrainObjectsOnly) continue;
            
            // Skip grouped objects, they will be processed separately
            if (groupSimilarObjects && mapObj.isGrouped) continue;
            
            yield return StartCoroutine(GenerateAndPlaceModel(mapObj, terrain));
            
            processedCount++;
            if (processedCount % 5 == 0) yield return null; // Spread processing over frames
        }
        
        // Now process object groups if enabled
        if (groupSimilarObjects && analysisResults.objectGroups != null)
        {
            foreach (var group in analysisResults.objectGroups)
            {
                string groupId = group.Key;
                List<MapObject> groupObjects = group.objects;
                
                if (groupObjects.Count == 0) continue;
                
                // Check if this is a terrain or non-terrain group
                bool isTerrainGroup = IsTerrainFeature(groupObjects[0].type);
                
                // Skip if not matching the current filter
                if (isTerrainGroup != terrainObjectsOnly) continue;
                
                yield return StartCoroutine(GenerateAndPlaceObjectGroup(groupId, groupObjects, terrain));
                
                yield return null; // Pause after each group
            }
        }
    }
    
    private IEnumerator GenerateAndPlaceModel(MapObject mapObj, Terrain terrain)
    {
        // Get the appropriate description
        string description = !string.IsNullOrEmpty(mapObj.enhancedDescription) ? 
            mapObj.enhancedDescription : mapObj.label;
        
        // Check if we already have this model in cache
        if (modelCache.ContainsKey(description))
        {
            PlaceModelInstance(modelCache[description], mapObj, terrain);
            yield break;
        }
        
        // Check if we have a pre-configured model for this type
        GameObject prefabModel = GetPrefabForObjectType(mapObj.type);
        
        if (prefabModel != null)
        {
            // Use the prefab model
            GameObject model = Instantiate(prefabModel);
            model.name = $"{mapObj.type}_{Guid.NewGuid().ToString().Substring(0, 8)}";
            
            // Store in cache
            modelCache[description] = model;
            
            // Place the model
            PlaceModelInstance(model, mapObj, terrain);
            yield break;
        }
        
        // If AI generation is enabled and no prefab exists, use WorldManager
        if (useAIGeneration && worldManager != null && !string.IsNullOrEmpty(openAIApiKey) && !string.IsNullOrEmpty(tripo3DApiKey))
        {
            Debug.Log($"[ModelGenerator] Using WorldManager AI generation for: {description}");
            
            GameObject generatedModel = null;
            bool modelGenerated = false;
            
            // Calculate world position for the model
            Vector3 worldPosition = CalculateWorldPosition(mapObj, terrain);
            
            // Configure WorldManager with API keys
            ConfigureWorldManager();
            
            // Use WorldManager to generate the model
            yield return StartCoroutine(GenerateModelWithWorldManager(description, worldPosition, mapObj.type,
                (model) => {
                    generatedModel = model;
                    modelGenerated = true;
                },
                (error) => {
                    Debug.LogError($"[ModelGenerator] WorldManager generation failed: {error}");
                    modelGenerated = true; // Set to true to exit wait loop
                }
            ));
            
            // Wait for generation to complete
            float timeout = 300f; // 5 minutes timeout
            float elapsed = 0f;
            while (!modelGenerated && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            
            if (generatedModel != null)
            {
                // Store in cache
                modelCache[description] = generatedModel;
                
                // Configure the generated model
                ConfigureGeneratedModel(generatedModel, mapObj, terrain);
                
                // Add to our generated models list
                generatedModels.Add(generatedModel);
            }
            else
            {
                Debug.LogWarning($"[ModelGenerator] AI generation failed or timed out for {description}, using fallback");
                generatedModel = GetFallbackModel(mapObj.type);
                
                if (generatedModel != null)
                {
                    PlaceModelInstance(generatedModel, mapObj, terrain);
                }
            }
        }
        else
        {
            // Use fallback model when AI generation is not available
            Debug.Log($"[ModelGenerator] AI generation not available, using fallback for: {description}");
            GameObject fallbackModel = GetFallbackModel(mapObj.type);
            
            if (fallbackModel != null)
            {
                // Store in cache
                modelCache[description] = fallbackModel;
                
                // Place the model
                PlaceModelInstance(fallbackModel, mapObj, terrain);
            }
        }
    }
    
    private void ConfigureWorldManager()
    {
        if (worldManager == null) return;
        
        // Use reflection to set API keys on WorldManager
        try
        {
            var openAIKeyField = typeof(WorldManager).GetField("openAIApiKey", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var tripoKeyField = typeof(WorldManager).GetField("tripo3DApiKey", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (openAIKeyField != null)
                openAIKeyField.SetValue(worldManager, openAIApiKey);
            
            if (tripoKeyField != null)
                tripoKeyField.SetValue(worldManager, tripo3DApiKey);
                
            Debug.Log("[ModelGenerator] Successfully configured WorldManager API keys");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ModelGenerator] Failed to configure WorldManager: {ex.Message}");
        }
    }
    
    private IEnumerator GenerateModelWithWorldManager(string description, Vector3 position, string objectType, 
        Action<GameObject> onSuccess, Action<string> onError)
    {
        if (worldManager == null)
        {
            onError?.Invoke("WorldManager not available");
            yield break;
        }
        
        try
        {
            // Create a more detailed prompt based on object type
            string enhancedPrompt = CreateEnhancedPrompt(description, objectType);
            
            Debug.Log($"[ModelGenerator] Requesting AI model generation: {enhancedPrompt}");
            
            // Call WorldManager's generation method
            worldManager.GenerateModelWithDescription(enhancedPrompt);
            
            // Wait for the model to be generated and placed
            // WorldManager will handle the entire generation pipeline
            float waitTime = 0f;
            float maxWaitTime = 300f; // 5 minutes
            GameObject generatedModel = null;
            
            while (waitTime < maxWaitTime)
            {
                // Check if a new model was added to the scene at the expected position
                // This is a simplified check - you might want to implement a more robust callback system
                Collider[] nearbyObjects = Physics.OverlapSphere(position, 2f);
                foreach (var collider in nearbyObjects)
                {
                    if (collider.gameObject.name.Contains(objectType) && 
                        !generatedModels.Contains(collider.gameObject))
                    {
                        generatedModel = collider.gameObject;
                        break;
                    }
                }
                
                if (generatedModel != null)
                {
                    Debug.Log($"[ModelGenerator] Found generated model: {generatedModel.name}");
                    onSuccess?.Invoke(generatedModel);
                    yield break;
                }
                
                waitTime += Time.deltaTime;
                yield return null;
            }
            
            onError?.Invoke("Model generation timed out");
        }
        catch (Exception ex)
        {
            onError?.Invoke($"Exception during generation: {ex.Message}");
        }
    }
    
    private string CreateEnhancedPrompt(string baseDescription, string objectType)
    {
        // Create more specific prompts based on object type
        switch (objectType.ToLower())
        {
            case "tree":
                return $"A realistic 3D {baseDescription} suitable for a game environment. Include detailed bark texture and foliage.";
                
            case "building":
            case "house":
                return $"A detailed 3D {baseDescription} with architectural details, windows, doors, and realistic materials.";
                
            case "castle":
                return $"An impressive 3D medieval {baseDescription} with towers, walls, and fortifications.";
                
            case "boat":
            case "ship":
                return $"A detailed 3D {baseDescription} with sails, rigging, and nautical details.";
                
            case "bridge":
                return $"A sturdy 3D {baseDescription} suitable for crossing, with structural supports and railings.";
                
            case "statue":
            case "monument":
                return $"An ornate 3D {baseDescription} with fine sculptural details and weathered stone texture.";
                
            default:
                return $"A high-quality 3D {baseDescription} with realistic textures and appropriate scale for a game environment.";
        }
    }
    
    private void ConfigureGeneratedModel(GameObject model, MapObject mapObj, Terrain terrain)
    {
        if (model == null) return;
        
        // The model is already positioned by WorldManager, but we need to ensure proper scale
        Vector3 appropriateScale = CalculateAppropriateScale(model, mapObj, terrain.terrainData.size);
        model.transform.localScale = appropriateScale;
        
        // Apply rotation
        model.transform.rotation = Quaternion.Euler(0, mapObj.rotation, 0);
        
        // Ensure the model has necessary components
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
    }
    
    private IEnumerator GenerateAndPlaceObjectGroup(string groupId, List<MapObject> groupObjects, Terrain terrain)
    {
        if (groupObjects.Count == 0) 
        {
            yield break;
        }
        
        // Use the first object as reference
        MapObject referenceObj = groupObjects[0];
        
        // Get the appropriate description
        string description = !string.IsNullOrEmpty(referenceObj.enhancedDescription) ? 
            referenceObj.enhancedDescription : referenceObj.label;
        
        // Generate or retrieve the template model
        GameObject templateModel = null;
        
        // Check if we already have this model in cache
        if (modelCache.ContainsKey(description))
        {
            templateModel = modelCache[description];
        }
        else
        {
            // Check if we have a pre-configured model for this type
            GameObject prefabModel = GetPrefabForObjectType(referenceObj.type);
            
            if (prefabModel != null)
            {
                // Use the prefab model
                templateModel = Instantiate(prefabModel);
                templateModel.name = $"{referenceObj.type}_Template_{Guid.NewGuid().ToString().Substring(0, 8)}";
                
                // Store in cache
                modelCache[description] = templateModel;
            }
            else if (useAIGeneration && worldManager != null)
            {
                // Use AI generation for the template
                Debug.Log($"[ModelGenerator] Generating template model for group: {description}");
                
                GameObject generatedModel = null;
                bool modelGenerated = false;
                
                // Calculate position for first object in group
                Vector3 firstPosition = CalculateWorldPosition(groupObjects[0], terrain);
                
                yield return StartCoroutine(GenerateModelWithWorldManager(description, firstPosition, referenceObj.type,
                    (model) => {
                        generatedModel = model;
                        modelGenerated = true;
                    },
                    (error) => {
                        Debug.LogError($"[ModelGenerator] Template generation failed: {error}");
                        modelGenerated = true;
                    }
                ));
                
                // Wait for generation
                float timeout = 300f;
                float elapsed = 0f;
                while (!modelGenerated && elapsed < timeout)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
                
                if (generatedModel != null)
                {
                    templateModel = generatedModel;
                    modelCache[description] = templateModel;
                }
                else
                {
                    templateModel = GetFallbackModel(referenceObj.type);
                }
            }
            else
            {
                Debug.Log($"[ModelGenerator] Using fallback model for group: {description}");
                templateModel = GetFallbackModel(referenceObj.type);
                
                // Simulate model generation time
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        if (templateModel == null)
        {
            Debug.LogError($"[ModelGenerator] Could not generate or find template model for group {groupId}");
            yield break;
        }
        
        // Make template model inactive as it's just a template
        templateModel.SetActive(false);
        
        // Create a group container
        GameObject groupContainer = new GameObject($"Group_{referenceObj.type}_{groupId.Substring(0, 8)}");
        
        // Place instances for each object in the group
        foreach (var mapObj in groupObjects)
        {
            GameObject instance = Instantiate(templateModel);
            instance.name = $"{mapObj.type}_{Guid.NewGuid().ToString().Substring(0, 8)}";
            instance.SetActive(true);
            
            // Apply some variation in scale and rotation for natural look
            Vector3 originalScale = instance.transform.localScale;
            instance.transform.localScale = new Vector3(
                originalScale.x * UnityEngine.Random.Range(1f - groupObjectScaleVariation, 1f + groupObjectScaleVariation),
                originalScale.y * UnityEngine.Random.Range(1f - groupObjectScaleVariation, 1f + groupObjectScaleVariation),
                originalScale.z * UnityEngine.Random.Range(1f - groupObjectScaleVariation, 1f + groupObjectScaleVariation)
            );
            
            // Add slight rotation variation
            Vector3 originalRotation = instance.transform.rotation.eulerAngles;
            instance.transform.rotation = Quaternion.Euler(
                originalRotation.x,
                originalRotation.y + UnityEngine.Random.Range(-groupObjectRotationVariation, groupObjectRotationVariation),
                originalRotation.z
            );
            
            // Place the instance
            PlaceModelInstance(instance, mapObj, terrain);
            
            // Add to group container
            instance.transform.SetParent(groupContainer.transform);
            
            // Add to generated models list
            generatedModels.Add(instance);
            
            yield return null; // Pause after each object to spread load
        }
    }
    
    private void PlaceModelInstance(GameObject model, MapObject mapObj, Terrain terrain)
    {
        if (model == null) return;
        
        // Calculate world position
        Vector3 worldPosition = CalculateWorldPosition(mapObj, terrain);
        
        // Calculate appropriate scale based on object type and terrain size
        Vector3 worldScale = CalculateAppropriateScale(model, mapObj, terrain.terrainData.size);
        
        // Create rotation from the float rotation value
        Quaternion worldRotation = Quaternion.Euler(0, mapObj.rotation, 0);
        
        // If using WorldManager, delegate placement to it
        if (worldManager != null)
        {
            worldManager.PlaceModelInScene(model, worldPosition);
            
            // Still apply our scale and rotation
            model.transform.rotation = worldRotation;
            model.transform.localScale = worldScale;
        }
        else
        {
            // Apply position, rotation, and scale directly
            model.transform.position = worldPosition;
            model.transform.rotation = worldRotation;
            model.transform.localScale = worldScale;
            
            // Make model visible
            model.SetActive(true);
        }
        
        Debug.Log($"[ModelGenerator] Placed model {model.name} at {worldPosition}, rotation: {worldRotation.eulerAngles}, scale: {worldScale}");
    }
    
    private Vector3 CalculateWorldPosition(MapObject mapObj, Terrain terrain)
    {
        Vector3 terrainSize = terrain.terrainData.size;
        
        // Convert Vector2 position to Vector3 for the terrain
        Vector3 worldPos = new Vector3(
            mapObj.position.x * terrainSize.x,
            0,
            mapObj.position.y * terrainSize.z  // Use y component for z in 3D space
        );
        
        // Get the height at this position from the terrain
        float terrainHeight = terrain.SampleHeight(worldPos);
        
        // Calculate final position
        return new Vector3(
            worldPos.x,
            terrainHeight,
            worldPos.z
        );
    }
    
    private Vector3 CalculateAppropriateScale(GameObject model, MapObject mapObj, Vector3 terrainSize)
    {
        // Start with the base scale from the analysis
        Vector3 baseScale = mapObj.scale;
        
        // Get the object type
        string objectType = mapObj.type;
        
        // Find specific scaling settings for this object type
        ObjectTypeMapping mapping = objectTypeMappings.Find(m => m.objectType.ToLower() == objectType.ToLower());
        
        if (mapping != null && mapping.useCustomScale)
        {
            return mapping.customScale;
        }
        
        // Apply terrain-relative scaling
        float terrainFactor = (terrainSize.x + terrainSize.z) / 1000f; // Normalize for 1000x1000 terrain
        
        // Calculate different scales based on object type
        float scaleFactor;
        
        switch (objectType.ToLower())
        {
            case "tree":
                scaleFactor = 5f * terrainFactor;
                break;
            case "building":
            case "house":
                scaleFactor = 10f * terrainFactor;
                break;
            case "castle":
            case "tower":
                scaleFactor = 20f * terrainFactor;
                break;
            case "bridge":
                scaleFactor = 15f * terrainFactor;
                break;
            case "wall":
            case "fence":
                scaleFactor = 3f * terrainFactor;
                break;
            case "statue":
            case "monument":
                scaleFactor = 8f * terrainFactor;
                break;
            case "ship":
            case "boat":
                scaleFactor = 12f * terrainFactor;
                break;
            default:
                scaleFactor = 5f * terrainFactor;
                break;
        }
        
        // Calculate the final scale
        return new Vector3(
            baseScale.x * scaleFactor,
            baseScale.y * scaleFactor,
            baseScale.z * scaleFactor
        );
    }
    
    private GameObject GetPrefabForObjectType(string objectType)
    {
        // Check our mappings first
        ObjectTypeMapping mapping = objectTypeMappings.Find(m => m.objectType.ToLower() == objectType.ToLower());
        
        if (mapping != null && mapping.modelPrefab != null)
        {
            return mapping.modelPrefab;
        }
        
        return null;
    }
    
    private GameObject GetFallbackModel(string objectType)
    {
        // Return a default model based on the object type
        switch (objectType.ToLower())
        {
            case "tree":
                return defaultTreeModel != null ? defaultTreeModel : CreatePrimitiveModel(PrimitiveType.Cylinder, Color.green);
                
            case "building":
            case "house":
                return defaultBuildingModel != null ? defaultBuildingModel : CreatePrimitiveModel(PrimitiveType.Cube, Color.gray);
                
            case "castle":
            case "tower":
                return defaultCastleModel != null ? defaultCastleModel : CreatePrimitiveModel(PrimitiveType.Cube, Color.magenta);
                
            case "bridge":
                return CreatePrimitiveModel(PrimitiveType.Cube, new Color(0.6f, 0.4f, 0.2f));
                
            case "ship":
            case "boat":
                return defaultBoatModel != null ? defaultBoatModel : CreatePrimitiveModel(PrimitiveType.Cube, Color.blue);
                
            case "statue":
            case "monument":
                return CreatePrimitiveModel(PrimitiveType.Cylinder, Color.yellow);
                
            default:
                return CreatePrimitiveModel(PrimitiveType.Sphere, Color.white);
        }
    }
    
    private GameObject CreatePrimitiveModel(PrimitiveType primitiveType, Color color)
    {
        // Create a simple primitive as a fallback
        GameObject primitive = GameObject.CreatePrimitive(primitiveType);
        primitive.name = $"Fallback_{primitiveType}";
        
        // Set color
        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
        }
        
        return primitive;
    }
    
    private bool IsTerrainFeature(string objectType)
    {
        // Define what types are considered terrain features
        string[] terrainTypes = { 
            "water", "ocean", "sea", "lake", "river", 
            "mountain", "hill", "valley", 
            "forest", "woodland", 
            "desert", "plain", "field", "grassland", 
            "swamp", "marsh", "beach", "shore"
        };
        
        return Array.Exists(terrainTypes, t => t.ToLower() == objectType.ToLower());
    }
    
    [System.Serializable]
    public class ObjectTypeMapping
    {
        public string objectType;
        public GameObject modelPrefab;
        public bool useCustomScale = false;
        public Vector3 customScale = Vector3.one;
    }
    
    // Public methods for external use
    public IEnumerator GenerateAndPlaceModels(AnalysisResults analysisResults, Terrain terrain, 
        System.Action<List<GameObject>> onComplete, System.Action<string> onError, System.Action<int, int> onProgress)
    {
        Debug.Log("[ModelGenerator] Starting model generation with callbacks...");
        
        try
        {
            yield return StartCoroutine(GenerateAndPlaceModels(analysisResults, terrain));
            
            // Report completion
            onComplete?.Invoke(generatedModels);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ModelGenerator] Error during model generation: {ex.Message}");
            onError?.Invoke(ex.Message);
        }
    }
    
    // Stub properties for compatibility
    public TraversifyDebugger debugger { get; set; }
    public int maxConcurrentRequests { get; set; } = 3;
    public float apiRateLimitDelay { get; set; } = 0.5f;
}