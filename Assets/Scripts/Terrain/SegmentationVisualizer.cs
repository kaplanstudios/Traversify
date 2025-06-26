using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Traversify;
using TMPro;

public class SegmentationVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField] private GameObject labelPrefab;
    [SerializeField] private float labelHeightOffset = 5f;
    [SerializeField] private float labelScale = 1f;
    [SerializeField] private Material segmentOverlayMaterial;
    [SerializeField] private float overlayHeight = 0.5f;
    [SerializeField] private bool animateSegments = true;
    [SerializeField] private float animationDuration = 0.5f;
    
    [Header("Label Style")]
    [SerializeField] private Font labelFont;
    [SerializeField] private int fontSize = 14;
    [SerializeField] private Color terrainLabelColor = Color.white;
    [SerializeField] private Color objectLabelColor = Color.yellow;
    [SerializeField] private bool showTerrainLabels = true;
    [SerializeField] private bool showObjectLabels = true;
    [SerializeField] private bool showConfidenceScores = true;
    
    [Header("Overlay Effects")]
    [SerializeField] private bool useGradientOverlay = true;
    [SerializeField] private Gradient terrainGradient;
    [SerializeField] private Gradient objectGradient;
    [SerializeField] private float overlayOpacity = 0.6f;
    [SerializeField] private bool pulseHighConfidence = true;
    [SerializeField] private float pulseSpeed = 2f;
    
    [Header("Interactive Features")]
    [SerializeField] private bool enableInteractiveLabels = true;
    [SerializeField] private float labelFadeDistance = 100f;
    [SerializeField] private bool autoHideLabels = false;
    [SerializeField] private float autoHideDelay = 5f;
    
    [Header("Debug Settings")]
    [SerializeField] private bool verboseLogging = true;
    [SerializeField] private float timeoutDuration = 5f;
    
    [Header("Integration Settings")]
    public Traversify.Core.TraversifyDebugger debugger;
    public bool enableDebugVisualization = true;
    
    private GameObject segmentationContainer;
    private GameObject labelsContainer;
    private List<GameObject> createdObjects = new List<GameObject>();
    private List<SegmentVisualization> activeVisualizations = new List<SegmentVisualization>();
    private Camera mainCamera;
    
    private void Awake()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
            mainCamera = FindObjectOfType<Camera>();
        
        // Initialize gradients if not set
        if (terrainGradient == null)
        {
            terrainGradient = CreateDefaultTerrainGradient();
        }
        
        if (objectGradient == null)
        {
            objectGradient = CreateDefaultObjectGradient();
        }
    }
    
    public IEnumerator VisualizeSegments(
        AnalysisResults analysisResults,
        Terrain terrain,
        Texture2D mapTexture,
        System.Action<List<GameObject>> onComplete = null,
        System.Action<string> onError = null,
        System.Action<float> onProgress = null)
    {
        // Initial progress
        onProgress?.Invoke(0f);
        if (enableDebugVisualization && debugger != null) debugger.Log("Segmentation sub-progress: 0%", Traversify.Core.LogCategory.Visualization);
        
        if (enableDebugVisualization) Debug.Log("[SegmentationVisualizer] Starting enhanced segmentation visualization");

        bool visualizationCompleted = false;
        string visualizationError = null;
        
        // Clear any previous visualization
        ClearVisualization();
        
        // Create containers for organization
        segmentationContainer = new GameObject("SegmentationOverlays");
        labelsContainer = new GameObject("SegmentationLabels");
        createdObjects.Add(segmentationContainer);
        createdObjects.Add(labelsContainer);
        
        // Check if we have valid input data
        if (analysisResults == null)
        {
            Debug.LogError("[SegmentationVisualizer] Analysis results are null");
            onError?.Invoke("Analysis results are null");
            yield break;
        }
        
        if (terrain == null)
        {
            Debug.LogError("[SegmentationVisualizer] Terrain is null");
            onError?.Invoke("Terrain is null");
            yield break;
        }
        
        if (mapTexture == null)
        {
            Debug.LogError("[SegmentationVisualizer] Map texture is null");
            onError?.Invoke("Map texture is null");
            yield break;
        }
        
        // Process data outside the try-catch to avoid yield statements inside it
        // Sort objects by confidence for better visualization
        Debug.Log($"[SegmentationVisualizer] Processing {analysisResults.terrainFeatures.Count} terrain features and {analysisResults.mapObjects.Count} map objects");
        
        var sortedTerrainFeatures = new List<TerrainFeature>(analysisResults.terrainFeatures);
        sortedTerrainFeatures.Sort((a, b) => b.confidence.CompareTo(a.confidence));
        
        var sortedMapObjects = new List<MapObject>(analysisResults.mapObjects);
        sortedMapObjects.Sort((a, b) => b.confidence.CompareTo(a.confidence));
        
        // Calculate total items for progress reporting
        int totalItems = 0;
        if (showTerrainLabels) totalItems += sortedTerrainFeatures.Count;
        if (showObjectLabels) totalItems += sortedMapObjects.Count;
        int currentItem = 0;
        
        Debug.Log($"[SegmentationVisualizer] Will process a total of {totalItems} visualization items");
        
        // Before processing, set small progress
        onProgress?.Invoke(0.05f);
        if (enableDebugVisualization && debugger != null) debugger.Log("Segmentation sub-progress: 5% - Preparing items", Traversify.Core.LogCategory.Visualization);
        
        // Process terrain features
        if (showTerrainLabels)
        {
            int index = 0;
            foreach (var feature in sortedTerrainFeatures)
            {
                try
                {
                    Debug.Log($"[SegmentationVisualizer] Processing terrain feature {index+1}/{sortedTerrainFeatures.Count}: {feature.label} (Confidence: {feature.confidence})");
                    
                    // Check if we have required data
                    if (feature.segmentMask == null)
                    {
                        Debug.LogWarning($"[SegmentationVisualizer] Skipping terrain feature {feature.label} - no segment mask");
                        currentItem++;
                        float progressValue = (float)currentItem / totalItems;
                        onProgress?.Invoke(progressValue);
                        continue;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SegmentationVisualizer] Error checking terrain feature: {ex.Message}");
                    continue;
                }
                
                // Set timeout for this operation
                float timeoutTime = Time.realtimeSinceStartup + timeoutDuration;
                
                bool featureVisualizationComplete = false;
                System.Exception featureVisualizationException = null;
                
                StartCoroutine(CreateEnhancedVisualizationWithTimeout(
                    feature.boundingBox,
                    feature.segmentMask,
                    feature.segmentColor,
                    feature.label,
                    feature.confidence,
                    terrain,
                    mapTexture,
                    true,
                    index++,
                    () => featureVisualizationComplete = true,
                    (ex) => {
                        featureVisualizationComplete = true;
                        featureVisualizationException = ex;
                    },
                    (subProgress) => {
                        // Calculate combined progress for this item
                        float itemProgress = ((float)currentItem / totalItems) + (subProgress / totalItems);
                        Debug.Log($"[SegmentationVisualizer] Detailed visualization progress: {itemProgress:P2}");
                        onProgress?.Invoke(itemProgress);
                    }
                ));
                
                // Wait for visualization to complete - this is outside the try-catch
                while (!featureVisualizationComplete)
                {
                    // Check for timeout
                    if (Time.realtimeSinceStartup > timeoutTime)
                    {
                        Debug.LogWarning($"[SegmentationVisualizer] Timeout visualizing terrain feature: {feature.label}");
                        featureVisualizationComplete = true;
                    }
                    yield return null;
                }
                
                if (featureVisualizationException != null)
                {
                    Debug.LogError($"[SegmentationVisualizer] Error visualizing terrain feature: {featureVisualizationException.Message}");
                    // Continue with other items instead of failing completely
                }
                
                // Update progress
                currentItem++;
                float progressUpdate = (float)currentItem / totalItems;
                Debug.Log($"[SegmentationVisualizer] Progress: {progressUpdate:P0}");
                onProgress?.Invoke(progressUpdate);
            }
        }
        
        // after sorting
        onProgress?.Invoke(0.1f);
        if (enableDebugVisualization && debugger != null) debugger.Log($"Segmentation sub-progress: 10% - {sortedTerrainFeatures.Count} terrain features, {sortedMapObjects.Count} map objects", Traversify.Core.LogCategory.Visualization);

        // Process map objects
        if (showObjectLabels)
        {
            int index = 0;
            foreach (var mapObj in sortedMapObjects)
            {
                try
                {
                    Debug.Log($"[SegmentationVisualizer] Processing map object {index+1}/{sortedMapObjects.Count}: {mapObj.label} (Confidence: {mapObj.confidence})");
                    
                    // Check if we have required data
                    if (mapObj.segmentMask == null)
                    {
                        Debug.LogWarning($"[SegmentationVisualizer] Skipping map object {mapObj.label} - no segment mask");
                        currentItem++;
                        float progressValue = (float)currentItem / totalItems;
                        onProgress?.Invoke(progressValue);
                        continue;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[SegmentationVisualizer] Error checking map object: {ex.Message}");
                    continue;
                }
                
                // Set timeout for this operation
                float timeoutTime = Time.realtimeSinceStartup + timeoutDuration;
                
                bool objectVisualizationComplete = false;
                System.Exception objectVisualizationException = null;
                
                string labelText = !string.IsNullOrEmpty(mapObj.enhancedDescription) ? 
                    mapObj.enhancedDescription : mapObj.label;
                
                StartCoroutine(CreateEnhancedVisualizationWithTimeout(
                    mapObj.boundingBox,
                    mapObj.segmentMask,
                    mapObj.segmentColor,
                    labelText,
                    mapObj.confidence,
                    terrain,
                    mapTexture,
                    false,
                    index++,
                    () => objectVisualizationComplete = true,
                    (ex) => {
                        objectVisualizationComplete = true;
                        objectVisualizationException = ex;
                    },
                    (subProgress) => {
                        // Calculate combined progress for this item
                        float itemProgress = ((float)currentItem / totalItems) + (subProgress / totalItems);
                        Debug.Log($"[SegmentationVisualizer] Map object visualization progress: {itemProgress:P2}");
                        onProgress?.Invoke(itemProgress);
                    }
                ));
                
                // Wait for visualization to complete - this is outside the try-catch
                while (!objectVisualizationComplete)
                {
                    // Check for timeout
                    if (Time.realtimeSinceStartup > timeoutTime)
                    {
                        Debug.LogWarning($"[SegmentationVisualizer] Timeout visualizing map object: {mapObj.label}");
                        objectVisualizationComplete = true;
                    }
                    yield return null;
                }
                
                if (objectVisualizationException != null)
                {
                    Debug.LogError($"[SegmentationVisualizer] Error visualizing map object: {objectVisualizationException.Message}");
                    // Continue with other items instead of failing completely
                }
                
                // Update progress
                currentItem++;
                float progressUpdate = (float)currentItem / totalItems;
                Debug.Log($"[SegmentationVisualizer] Progress: {progressUpdate:P0}");
                onProgress?.Invoke(progressUpdate);
            }
        }
        
        try
        {
            // Start interactive features
            if (enableInteractiveLabels)
                StartCoroutine(UpdateInteractiveLabels());
            if (autoHideLabels)
                StartCoroutine(AutoHideLabelsAfterDelay());
            
            Debug.Log("[SegmentationVisualizer] Enhanced visualization complete");
            visualizationCompleted = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SegmentationVisualizer] Error starting interactive features: {ex.Message}\n{ex.StackTrace}");
            visualizationError = $"Segmentation visualization failed: {ex.Message}";
        }
        
        // Handle completion or error outside the try-catch block
        if (visualizationCompleted) {
            onComplete?.Invoke(createdObjects);
        } else if (visualizationError != null) {
            onError?.Invoke(visualizationError);
        }
        
        yield break;
    }
    
    private IEnumerator CreateEnhancedVisualization(
        Rect boundingBox,
        Texture2D segmentMask,
        Color segmentColor,
        string labelText,
        float confidence,
        Terrain terrain,
        Texture2D mapTexture,
        bool isTerrain,
        int index,
        System.Action<float> onSubProgress = null)
    {
        // Log AI model interaction for detailed progress tracking
        Debug.Log($"[SegmentationVisualizer] AI Model: Processing visualization for {(isTerrain ? "terrain" : "object")} segment '{labelText}'");
        
        // Create required objects outside try-catch
        GameObject overlayObject = null;
        GameObject labelObject = null;
        
        // Report starting calculation progress
        onSubProgress?.Invoke(0.1f);
        yield return null;
        
        // Step 1: Create segment overlay - moved try-catch inside
        Debug.Log($"[SegmentationVisualizer] AI Model: Creating enhanced overlay for {labelText}");
        
        // Execute overlay creation outside of try/catch
        overlayObject = CreateEnhancedSegmentOverlayWithErrorHandling(
            boundingBox, segmentMask, segmentColor, confidence, terrain, mapTexture, isTerrain);
        
        // Report progress after overlay creation
        onSubProgress?.Invoke(0.4f);
        yield return null;
        
        if (overlayObject == null) {
            Debug.LogWarning($"[SegmentationVisualizer] Failed to create overlay for {labelText}");
            yield break;
        }
        
        // Report progress before label creation
        onSubProgress?.Invoke(0.6f);
        yield return null;
        
        // Step 2: Create label - moved try-catch inside
        Debug.Log($"[SegmentationVisualizer] AI Model: Creating enhanced label for {labelText}");
        
        // Execute label creation outside of try/catch
        labelObject = CreateEnhancedLabelWithErrorHandling(
            boundingBox, labelText, confidence, terrain, mapTexture, isTerrain);
        
        // Report progress after label creation
        onSubProgress?.Invoke(0.8f);
        yield return null;
        
        if (labelObject == null) {
            Debug.LogWarning($"[SegmentationVisualizer] Failed to create label for {labelText}");
            // Clean up created objects before yielding
            if (overlayObject != null)
                Destroy(overlayObject);
            yield break;
        }
        
        // Step 3: Create visualization data
        var visualization = new SegmentVisualization
        {
            overlay = overlayObject,
            label = labelObject,
            confidence = confidence,
            isTerrain = isTerrain,
            originalScale = overlayObject.transform.localScale,
            pulsePhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f)
        };
        
        activeVisualizations.Add(visualization);
        
        // Report progress before animation
        onSubProgress?.Invoke(0.9f);
        yield return null;
        
        // Step 4: Animate entrance if enabled
        if (animateSegments)
        {
            yield return StartCoroutine(AnimateSegmentEntrance(visualization, index * 0.05f));
        }
        
        // Final progress report
        onSubProgress?.Invoke(1.0f);
        yield return null;
    }
    
    // Timeout wrapper for enhanced visualization
    private IEnumerator CreateEnhancedVisualizationWithTimeout(
        Rect boundingBox,
        Texture2D segmentMask,
        Color segmentColor,
        string labelText,
        float confidence,
        Terrain terrain,
        Texture2D mapTexture,
        bool isTerrain,
        int index,
        System.Action onSuccess,
        System.Action<System.Exception> onError,
        System.Action<float> onProgress = null)
    {
        // Store the coroutine outside the try block
        Coroutine visualizationCoroutine = null;
        System.Exception caughtException = null;
        
        try {
            // Start the coroutine and store the reference
            visualizationCoroutine = StartCoroutine(CreateEnhancedVisualization(
                boundingBox,
                segmentMask,
                segmentColor,
                labelText,
                confidence,
                terrain,
                mapTexture,
                isTerrain,
                index,
                onProgress));
                
            Debug.Log($"[SegmentationVisualizer] Started visualization process for {labelText}");
        }
        catch (System.Exception ex) {
            caughtException = ex;
            Debug.LogError($"[SegmentationVisualizer] Exception starting visualization: {ex.Message}");
        }
        
        // Only yield if we successfully started the coroutine
        if (visualizationCoroutine != null)
        {
            yield return visualizationCoroutine;
        }
        else
        {
            // If we couldn't start the coroutine, yield one frame to maintain coroutine behavior
            yield return null;
        }
        
        // Invoke callbacks outside of try-catch
        if (caughtException != null) {
            onError?.Invoke(caughtException);
        } else {
            onSuccess?.Invoke();
        }
    }
    
    // Helper method to handle errors in overlay creation
    private GameObject CreateEnhancedSegmentOverlayWithErrorHandling(
        Rect boundingBox,
        Texture2D segmentMask,
        Color segmentColor,
        float confidence,
        Terrain terrain,
        Texture2D mapTexture,
        bool isTerrain)
    {
        try
        {
            return CreateEnhancedSegmentOverlay(
                boundingBox, segmentMask, segmentColor, confidence, terrain, mapTexture, isTerrain);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SegmentationVisualizer] Error creating overlay: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    // Helper method to handle errors in label creation
    private GameObject CreateEnhancedLabelWithErrorHandling(
        Rect boundingBox,
        string labelText,
        float confidence,
        Terrain terrain,
        Texture2D mapTexture,
        bool isTerrain)
    {
        try
        {
            return CreateEnhancedLabel(
                boundingBox, labelText, confidence, terrain, mapTexture, isTerrain);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SegmentationVisualizer] Error creating label: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    private GameObject CreateEnhancedSegmentOverlay(
        Rect boundingBox,
        Texture2D segmentMask,
        Color segmentColor,
        float confidence,
        Terrain terrain,
        Texture2D mapTexture,
        bool isTerrain)
    {
        try
        {
            // Check if we have valid inputs
            if (segmentMask == null)
            {
                Debug.LogError("[SegmentationVisualizer] Segment mask is null in CreateEnhancedSegmentOverlay");
                return null;
            }
            
            if (verboseLogging) {
                Debug.Log($"[SegmentationVisualizer][Verbose] Creating segment overlay for {(isTerrain ? "terrain" : "object")} with confidence {confidence:F2}");
            }
            
            GameObject overlayObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            overlayObject.name = $"SegmentOverlay_{(isTerrain ? "Terrain" : "Object")}";
            overlayObject.transform.SetParent(segmentationContainer.transform);
            
            // Remove collider
            if (overlayObject.TryGetComponent<Collider>(out var collider)) {
                Destroy(collider);
            }
            
            // Create enhanced material
            Material overlayMat = CreateEnhancedOverlayMaterial(segmentColor, confidence, isTerrain);
            
            overlayMat.SetTexture("_MainTex", segmentMask);
            overlayMat.SetTexture("_MaskTex", segmentMask);
            
            MeshRenderer renderer = overlayObject.GetComponent<MeshRenderer>();
            if (renderer == null) {
                Debug.LogError("[SegmentationVisualizer] Failed to get MeshRenderer component");
                Destroy(overlayObject);
                return null;
            }
            
            renderer.material = overlayMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            
            // Position based on terrain
            Vector3 position = CalculateWorldPosition(boundingBox, terrain, mapTexture);
            position.y += overlayHeight;
            
            Vector3 scale = CalculateWorldScale(boundingBox, terrain, mapTexture);
            
            overlayObject.transform.position = position;
            overlayObject.transform.eulerAngles = new Vector3(90, 0, 0);
            overlayObject.transform.localScale = scale;
            
            createdObjects.Add(overlayObject);
            
            if (verboseLogging) {
                Debug.Log("[SegmentationVisualizer][Verbose] Successfully created segment overlay");
            }
            
            return overlayObject;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SegmentationVisualizer] Error creating segment overlay: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
    
    private GameObject CreateEnhancedLabel(
        Rect boundingBox,
        string labelText,
        float confidence,
        Terrain terrain,
        Texture2D mapTexture,
        bool isTerrain)
    {
        GameObject labelObject;
        
        if (labelPrefab != null)
        {
            labelObject = Instantiate(labelPrefab);
        }
        else
        {
            labelObject = CreateDefaultLabel();
        }
        
        labelObject.name = $"Label_{labelText}";
        labelObject.transform.SetParent(labelsContainer.transform);
        
        // Configure text
        if (showConfidenceScores)
        {
            labelText += $"\n({confidence:P0})";
        }
        
        // Try TextMeshPro first
        TextMeshProUGUI tmpText = labelObject.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText != null)
        {
            tmpText.text = labelText;
            tmpText.color = isTerrain ? terrainLabelColor : objectLabelColor;
            tmpText.fontSize = fontSize;
            tmpText.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            // Fallback to TextMesh
            TextMesh textMesh = labelObject.GetComponentInChildren<TextMesh>();
            if (textMesh == null)
                textMesh = labelObject.AddComponent<TextMesh>();
            
            textMesh.text = labelText;
            textMesh.color = isTerrain ? terrainLabelColor : objectLabelColor;
            textMesh.fontSize = fontSize;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.font = labelFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        
        // Position label
        Vector3 position = CalculateWorldPosition(boundingBox, terrain, mapTexture);
        position.y += labelHeightOffset + (isTerrain ? 0 : labelHeightOffset / 2);
        
        labelObject.transform.position = position;
        labelObject.transform.localScale = Vector3.one * labelScale;
        
        // Add billboard component
        if (labelObject.GetComponent<EnhancedBillboard>() == null)
        {
            labelObject.AddComponent<EnhancedBillboard>();
        }
        
        createdObjects.Add(labelObject);
        
        return labelObject;
    }
    
    private GameObject CreateDefaultLabel()
    {
        GameObject labelObject = new GameObject("Label");
        
        // Create background
        GameObject bgObject = new GameObject("Background");
        bgObject.transform.SetParent(labelObject.transform);
        
        SpriteRenderer bgRenderer = bgObject.AddComponent<SpriteRenderer>();
        bgRenderer.sprite = CreateRoundedRectSprite();
        bgRenderer.color = new Color(0, 0, 0, 0.7f);
        
        // Create text
        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(labelObject.transform);
        textObject.transform.localPosition = Vector3.forward * 0.01f;
        
        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.fontSize = fontSize;
        textMesh.font = labelFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        
        return labelObject;
    }
    
    private Material CreateEnhancedOverlayMaterial(Color baseColor, float confidence, bool isTerrain)
    {
        Material mat = segmentOverlayMaterial != null 
            ? new Material(segmentOverlayMaterial) 
            : new Material(Shader.Find("Sprites/Default"));

        // Apply gradient coloring based on confidence
        if (useGradientOverlay)
        {
            Gradient gradient = isTerrain ? terrainGradient : objectGradient;
            Color gradientColor = gradient.Evaluate(confidence);
            baseColor = Color.Lerp(baseColor, gradientColor, 0.5f);
        }
        
        // Adjust opacity
        baseColor.a = overlayOpacity * confidence;
        mat.color = baseColor;
        
        // Enable transparency
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        
        return mat;
    }
    
    private IEnumerator AnimateSegmentEntrance(SegmentVisualization visualization, float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // Scale animation
        float elapsed = 0;
        Vector3 startScale = Vector3.zero;
        Vector3 endScale = visualization.originalScale;
        
        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / animationDuration;
            float easedT = EaseOutBounce(t);
            
            if (visualization.overlay != null)
                visualization.overlay.transform.localScale = Vector3.Lerp(startScale, endScale, easedT);
            
            yield return null;
        }
        
        if (visualization.overlay != null)
            visualization.overlay.transform.localScale = endScale;
        
        // Fade in label
        if (visualization.label != null)
        {
            yield return StartCoroutine(FadeInLabel(visualization.label));
        }
    }
    
    private IEnumerator FadeInLabel(GameObject label)
    {
        float elapsed = 0;
        float fadeDuration = 0.3f;
        
        // Get all text components
        TextMeshProUGUI[] tmpTexts = label.GetComponentsInChildren<TextMeshProUGUI>();
        TextMesh[] textMeshes = label.GetComponentsInChildren<TextMesh>();
        SpriteRenderer[] sprites = label.GetComponentsInChildren<SpriteRenderer>();
        
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = elapsed / fadeDuration;
            
            foreach (var tmp in tmpTexts)
            {
                Color c = tmp.color;
                c.a = alpha;
                tmp.color = c;
            }
            
            foreach (var tm in textMeshes)
            {
                Color c = tm.color;
                c.a = alpha;
                tm.color = c;
            }
            
            foreach (var sr in sprites)
            {
                Color c = sr.color;
                c.a = alpha * 0.7f;
                sr.color = c;
            }
            
            yield return null;
        }
    }
    
    private IEnumerator UpdateInteractiveLabels()
    {
        while (enabled)
        {
            if (mainCamera == null)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }
            
            foreach (var viz in activeVisualizations)
            {
                if (viz.label == null) continue;
                
                // Calculate distance to camera
                float distance = Vector3.Distance(mainCamera.transform.position, viz.label.transform.position);
                
                // Fade based on distance
                float fadeFactor = 1f - Mathf.Clamp01((distance - labelFadeDistance * 0.7f) / (labelFadeDistance * 0.3f));
                
                // Apply fade
                SetLabelOpacity(viz.label, fadeFactor);
                
                // Pulse high confidence segments
                if (pulseHighConfidence && viz.confidence > 0.8f && viz.overlay != null)
                {
                    float pulse = (Mathf.Sin(Time.time * pulseSpeed + viz.pulsePhase) + 1f) * 0.5f;
                    float scaleFactor = 1f + pulse * 0.05f;
                    viz.overlay.transform.localScale = viz.originalScale * scaleFactor;
                }
            }
            
            yield return new WaitForSeconds(0.1f);
        }
    }
    
    private IEnumerator AutoHideLabelsAfterDelay()
    {
        yield return new WaitForSeconds(autoHideDelay);
        
        foreach (var viz in activeVisualizations)
        {
            if (viz.label != null)
            {
                StartCoroutine(FadeOutLabel(viz.label));
            }
        }
    }
    
    private IEnumerator FadeOutLabel(GameObject label)
    {
        float elapsed = 0;
        float fadeDuration = 1f;
        
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float alpha = 1f - (elapsed / fadeDuration);
            SetLabelOpacity(label, alpha);
            yield return null;
        }
        
        label.SetActive(false);
    }
    
    private void SetLabelOpacity(GameObject label, float opacity)
    {
        TextMeshProUGUI[] tmpTexts = label.GetComponentsInChildren<TextMeshProUGUI>();
        TextMesh[] textMeshes = label.GetComponentsInChildren<TextMesh>();
        SpriteRenderer[] sprites = label.GetComponentsInChildren<SpriteRenderer>();
        
        foreach (var tmp in tmpTexts)
        {
            Color c = tmp.color;
            c.a = opacity;
            tmp.color = c;
        }
        
        foreach (var tm in textMeshes)
        {
            Color c = tm.color;
            c.a = opacity;
            tm.color = c;
        }
        
        foreach (var sr in sprites)
        {
            Color c = sr.color;
            c.a = opacity * 0.7f;
            sr.color = c;
        }
    }
    
    private Vector3 CalculateWorldPosition(Rect boundingBox, Terrain terrain, Texture2D mapTexture)
    {
        Vector3 terrainSize = terrain.terrainData.size;
        float terrainHeight = terrain.terrainData.GetHeight(
            Mathf.FloorToInt(boundingBox.center.x / mapTexture.width * terrain.terrainData.heightmapResolution),
            Mathf.FloorToInt(boundingBox.center.y / mapTexture.height * terrain.terrainData.heightmapResolution)
        );
        
        return new Vector3(
            boundingBox.center.x / mapTexture.width * terrainSize.x,
            terrainHeight,
            (1 - boundingBox.center.y / mapTexture.height) * terrainSize.z
        );
    }
    
    private Vector3 CalculateWorldScale(Rect boundingBox, Terrain terrain, Texture2D mapTexture)
    {
        Vector3 terrainSize = terrain.terrainData.size;
        return new Vector3(
            boundingBox.width / mapTexture.width * terrainSize.x,
            boundingBox.height / mapTexture.height * terrainSize.z,
            1
        );
    }
    
    private float EaseOutBounce(float t)
    {
        if (t < 1 / 2.75f)
        {
            return 7.5625f * t * t;
        }
        else if (t < 2 / 2.75f)
        {
            t -= 1.5f / 2.75f;
            return 7.5625f * t * t + 0.75f;
        }
        else if (t < 2.5 / 2.75f)
        {
            t -= 2.25f / 2.75f;
            return 7.5625f * t * t + 0.9375f;
        }
        else
        {
            t -= 2.625f / 2.75f;
            return 7.5625f * t * t + 0.984375f;
        }
    }
    
    private Gradient CreateDefaultTerrainGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.2f, 0.4f, 0.8f), 0.0f), // Water
                new GradientColorKey(new Color(0.8f, 0.7f, 0.5f), 0.3f), // Sand
                new GradientColorKey(new Color(0.2f, 0.8f, 0.2f), 0.5f), // Grass
                new GradientColorKey(new Color(0.4f, 0.6f, 0.4f), 0.7f), // Forest
                new GradientColorKey(new Color(0.6f, 0.6f, 0.6f), 1.0f)  // Mountain
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }
    
    private Gradient CreateDefaultObjectGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(1f, 0.5f, 0.5f), 0.0f),   // Low confidence
                new GradientColorKey(new Color(1f, 1f, 0.5f), 0.5f),     // Medium confidence
                new GradientColorKey(new Color(0.5f, 1f, 0.5f), 1.0f)    // High confidence
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }
    
    private Sprite CreateRoundedRectSprite()
    {
        int width = 128;
        int height = 64;
        int cornerRadius = 8;
        
        Texture2D texture = new Texture2D(width, height);
        Color[] pixels = new Color[width * height];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool isInside = true;
                
                // Check corners
                if (x < cornerRadius && y < cornerRadius)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cornerRadius, cornerRadius));
                    isInside = dist <= cornerRadius;
                }
                else if (x > width - cornerRadius && y < cornerRadius)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(width - cornerRadius, cornerRadius));
                    isInside = dist <= cornerRadius;
                }
                else if (x < cornerRadius && y > height - cornerRadius)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(cornerRadius, height - cornerRadius));
                    isInside = dist <= cornerRadius;
                }
                else if (x > width - cornerRadius && y > height - cornerRadius)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(width - cornerRadius, height - cornerRadius));
                    isInside = dist <= cornerRadius;
                }
                
                pixels[y * width + x] = isInside ? Color.white : Color.clear;
            }
        }
        
        texture.SetPixels(pixels);
        texture.Apply();
        
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100);
    }
    
    private void ClearVisualization()
    {
        foreach (var obj in createdObjects)
        {
            if (obj != null)
                Destroy(obj);
        }
        
        createdObjects.Clear();
        activeVisualizations.Clear();
    }
    
    private void OnDestroy()
    {
        ClearVisualization();
    }
    
    // Support classes
    private class SegmentVisualization
    {
        public GameObject overlay;
        public GameObject label;
        public float confidence;
        public bool isTerrain;
        public Vector3 originalScale;
        public float pulsePhase;
    }
}

// Enhanced billboard component with smooth rotation
public class EnhancedBillboard : MonoBehaviour
{
    private Camera mainCamera;
    private Quaternion originalRotation;
    public bool lockYAxis = false;
    public float rotationSpeed = 5f;
    
    private void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
            mainCamera = FindObjectOfType<Camera>();
        
        originalRotation = transform.rotation;
    }
    
    private void LateUpdate()
    {
        if (mainCamera == null) return;
        
        Vector3 lookDirection = mainCamera.transform.position - transform.position;
        
        if (lockYAxis)
        {
            lookDirection.y = 0;
        }
        
        if (lookDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }
}
