using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;
using Traversify; // Add this to use the data models

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
    
    // Reference to the TraversifyMain
    private TraversifyMain traversifyMain;
    
    // Cache for generated models to avoid duplicate requests
    private Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>();
    
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
    }
    
    public IEnumerator GenerateAndPlaceModels(AnalysisResults analysisResults, Terrain terrain)
    {
        Debug.Log("[ModelGenerator] Starting model generation...");
        
        // Process terrain objects first for better organization
        yield return StartCoroutine(ProcessMapObjects(analysisResults, terrain, true));
        
        // Process non-terrain objects
        yield return StartCoroutine(ProcessMapObjects(analysisResults, terrain, false));
        
        Debug.Log("[ModelGenerator] Model generation complete");
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
        
        // Use TraversifyManager to generate model
        GameObject generatedModel = null;
        bool modelGenerated = false;
        
        // Make sure the description is not empty
        if (string.IsNullOrEmpty(description))
        {
            description = "generic " + mapObj.type;
        }
        
        if (traversifyMain != null)
        {
            Debug.Log($"[ModelGenerator] Generating model for: {description}");
            
            // For demonstration purposes, use fallback models
            generatedModel = GetFallbackModel(mapObj.type);
            modelGenerated = (generatedModel != null);
            
            // Simulate model generation time
            yield return new WaitForSeconds(0.5f);
        }
        
        if (!modelGenerated)
        {
            Debug.LogWarning($"[ModelGenerator] Failed to generate model for {description}, using fallback");
            generatedModel = GetFallbackModel(mapObj.type);
        }
        
        if (generatedModel != null)
        {
            // Store in cache
            modelCache[description] = generatedModel;
            
            // Place the model
            PlaceModelInstance(generatedModel, mapObj, terrain);
        }
        else
        {
            Debug.LogError($"[ModelGenerator] Could not generate or find fallback model for {description}");
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
            else
            {
                Debug.Log($"[ModelGenerator] Generating template model for group: {description}");
                
                // For demonstration purposes, use fallback models
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
            
            yield return null; // Pause after each object to spread load
        }
    }
    
    private void PlaceModelInstance(GameObject model, MapObject mapObj, Terrain terrain)
    {
        if (model == null) return;
        
        // Calculate world position
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
        Vector3 worldPosition = new Vector3(
            worldPos.x,
            terrainHeight,
            worldPos.z
        );
        
        // Calculate appropriate scale based on object type and terrain size
        Vector3 worldScale = CalculateAppropriateScale(model, mapObj, terrainSize);
        
        // Create rotation from the float rotation value
        Quaternion worldRotation = Quaternion.Euler(0, mapObj.rotation, 0);
        
        // Apply position, rotation, and scale
        model.transform.position = worldPosition;
        model.transform.rotation = worldRotation;
        model.transform.localScale = worldScale;
        
        // Make model visible
        model.SetActive(true);
        
        Debug.Log($"[ModelGenerator] Placed model {model.name} at {worldPosition}, rotation: {worldRotation.eulerAngles}, scale: {worldScale}");
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
    
    // Helper methods to be used by the TraversifyMain class
    public IEnumerator GenerateModelWithDescription(string modelType, string description, Vector3 position, Quaternion rotation, Vector3 scale, 
        Action<GameObject> onComplete, Action<string> onError)
    {
        bool errorOccurred = false;
        string errorMessage = "";
        
        try 
        {
            GameObject model = null;
            
            // First check if we have a matching prefab
            model = GetPrefabForObjectType(modelType);
            
            if (model == null)
            {
                // If no prefab, use a fallback model
                model = GetFallbackModel(modelType);
            }
            
            if (model != null)
            {
                GameObject instance = Instantiate(model);
                instance.transform.position = position;
                instance.transform.rotation = rotation;
                instance.transform.localScale = scale;
                
                onComplete?.Invoke(instance);
            }
            else
            {
                errorOccurred = true;
                errorMessage = $"Failed to generate model for {modelType}";
            }
        }
        catch (Exception ex)
        {
            errorOccurred = true;
            errorMessage = $"Error generating model: {ex.Message}";
        }
        
        // Handle any errors outside the try-catch block
        if (errorOccurred)
        {
            onError?.Invoke(errorMessage);
        }
        
        // Safe to yield outside of try-catch
        yield return null;
    }
    
    public void PlaceModelInScene(GameObject model, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (model == null) return;
        
        model.transform.position = position;
        model.transform.rotation = rotation;
        model.transform.localScale = scale;
        
        // Ensure the model is active
        model.SetActive(true);
    }
}
