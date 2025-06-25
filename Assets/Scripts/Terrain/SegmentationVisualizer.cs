using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Traversify; // Add this to reference the AnalysisResults class

public class SegmentationVisualizer : MonoBehaviour
{
    [Header("Visualization Settings")]
    [SerializeField] private GameObject labelPrefab;
    [SerializeField] private float labelHeightOffset = 5f;
    [SerializeField] private float labelScale = 1f;
    [SerializeField] private Material segmentOverlayMaterial;
    [SerializeField] private float overlayHeight = 0.5f;
    
    [Header("Label Style")]
    [SerializeField] private Font labelFont;
    [SerializeField] private int fontSize = 14;
    [SerializeField] private Color terrainLabelColor = Color.white;
    [SerializeField] private Color objectLabelColor = Color.yellow;
    [SerializeField] private bool showTerrainLabels = true;
    [SerializeField] private bool showObjectLabels = true;
    
    private GameObject segmentationContainer;
    private GameObject labelsContainer;
    
    private List<GameObject> createdObjects = new List<GameObject>();
    
    public IEnumerator VisualizeSegments(
        AnalysisResults analysisResults,
        Terrain terrain,
        Texture2D mapTexture)
    {
        Debug.Log("[SegmentationVisualizer] Starting segmentation visualization");
        
        // Clear any previous visualization
        ClearVisualization();
        
        // Create containers for organization
        segmentationContainer = new GameObject("SegmentationOverlays");
        labelsContainer = new GameObject("SegmentationLabels");
        
        // Add containers to the list of created objects
        createdObjects.Add(segmentationContainer);
        createdObjects.Add(labelsContainer);
        
        // Visualize terrain features
        if (showTerrainLabels)
        {
            foreach (var feature in analysisResults.terrainFeatures)
            {
                CreateSegmentOverlay(feature.boundingBox, feature.segmentMask, feature.segmentColor, terrain, mapTexture);
                CreateLabel(feature.boundingBox, feature.label, terrain, mapTexture, terrainLabelColor, true);
                yield return null;
            }
        }
        
        // Visualize map objects
        if (showObjectLabels)
        {
            foreach (var mapObj in analysisResults.mapObjects)
            {
                CreateSegmentOverlay(mapObj.boundingBox, mapObj.segmentMask, mapObj.segmentColor, terrain, mapTexture);
                
                // Use enhanced description if available, otherwise use the basic label
                string labelText = !string.IsNullOrEmpty(mapObj.enhancedDescription) ? 
                    mapObj.enhancedDescription : mapObj.label;
                
                CreateLabel(mapObj.boundingBox, labelText, terrain, mapTexture, objectLabelColor, false);
                yield return null;
            }
        }
        
        Debug.Log("[SegmentationVisualizer] Segmentation visualization complete");
    }
    
    private void CreateSegmentOverlay(
        Rect boundingBox, 
        Texture2D segmentMask, 
        Color segmentColor,
        Terrain terrain,
        Texture2D mapTexture)
    {
        if (segmentMask == null) return;
        
        // Create a quad for the segment overlay
        GameObject overlayObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        overlayObject.name = "SegmentOverlay";
        overlayObject.transform.SetParent(segmentationContainer.transform);
        
        // Remove the collider as it's not needed
        Destroy(overlayObject.GetComponent<Collider>());
        
        // Set up the material
        Shader shader = segmentOverlayMaterial != null ? segmentOverlayMaterial.shader : Shader.Find("Transparent/Diffuse");
        Material overlayMaterial = new Material(shader);
        
        overlayMaterial.color = segmentColor;
        overlayMaterial.mainTexture = segmentMask;
        
        MeshRenderer renderer = overlayObject.GetComponent<MeshRenderer>();
        renderer.material = overlayMaterial;
        
        // Set position based on terrain
        Vector3 terrainSize = terrain.terrainData.size;
        float terrainHeight = terrain.terrainData.GetHeight(
            Mathf.FloorToInt(boundingBox.center.x / mapTexture.width * terrain.terrainData.heightmapResolution),
            Mathf.FloorToInt(boundingBox.center.y / mapTexture.height * terrain.terrainData.heightmapResolution)
        );
        
        Vector3 position = new Vector3(
            boundingBox.center.x / mapTexture.width * terrainSize.x,
            terrainHeight + overlayHeight,
            (1 - boundingBox.center.y / mapTexture.height) * terrainSize.z
        );
        
        // Scale to match the segment size
        Vector3 scale = new Vector3(
            boundingBox.width / mapTexture.width * terrainSize.x,
            boundingBox.height / mapTexture.height * terrainSize.z,
            1
        );
        
        // Apply position and rotation
        overlayObject.transform.position = position;
        overlayObject.transform.eulerAngles = new Vector3(90, 0, 0); // Make it parallel to the terrain
        overlayObject.transform.localScale = scale;
        
        // Add to the list of created objects
        createdObjects.Add(overlayObject);
    }
    
    private void CreateLabel(
        Rect boundingBox,
        string labelText,
        Terrain terrain,
        Texture2D mapTexture,
        Color textColor,
        bool isTerrain)
    {
        // Create label object
        GameObject labelObject;
        
        if (labelPrefab != null)
        {
            labelObject = Instantiate(labelPrefab);
        }
        else
        {
            // Create a default TextMesh if no prefab is provided
            labelObject = new GameObject("Label");
            TextMesh labelTextMesh = labelObject.AddComponent<TextMesh>(); // Renamed to avoid conflict
            labelTextMesh.fontSize = fontSize;
            labelTextMesh.font = labelFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelTextMesh.anchor = TextAnchor.MiddleCenter;
            labelTextMesh.alignment = TextAlignment.Center;
            labelTextMesh.color = textColor;
            labelTextMesh.text = labelText;
            
            // Add a mesh renderer and configure it
            MeshRenderer meshRenderer = labelObject.GetComponent<MeshRenderer>();
            meshRenderer.material = new Material(Shader.Find("TextMeshPro/Distance Field"));
        }
        
        labelObject.transform.SetParent(labelsContainer.transform);
        
        // Set text
        Text textComponent = labelObject.GetComponent<Text>();
        TextMesh textMesh = labelObject.GetComponent<TextMesh>();
        
        if (textComponent != null)
        {
            textComponent.text = labelText;
            textComponent.color = textColor;
            textComponent.font = labelFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textComponent.fontSize = fontSize;
        }
        else if (textMesh != null)
        {
            textMesh.text = labelText;
            textMesh.color = textColor;
        }
        
        // Set position based on terrain
        Vector3 terrainSize = terrain.terrainData.size;
        float terrainHeight = terrain.terrainData.GetHeight(
            Mathf.FloorToInt(boundingBox.center.x / mapTexture.width * terrain.terrainData.heightmapResolution),
            Mathf.FloorToInt(boundingBox.center.y / mapTexture.height * terrain.terrainData.heightmapResolution)
        );
        
        Vector3 position = new Vector3(
            boundingBox.center.x / mapTexture.width * terrainSize.x,
            terrainHeight + labelHeightOffset + (isTerrain ? 0 : labelHeightOffset/2),
            (1 - boundingBox.center.y / mapTexture.height) * terrainSize.z
        );
        
        // Apply position and rotation to face the camera
        labelObject.transform.position = position;
        labelObject.transform.rotation = Quaternion.Euler(90, 0, 0); // Make it readable from above
        labelObject.transform.localScale = Vector3.one * labelScale;
        
        // Make text always face the camera
        if (labelObject.GetComponent<Billboard>() == null)
        {
            labelObject.AddComponent<Billboard>();
        }
        
        // Add to the list of created objects
        createdObjects.Add(labelObject);
    }
    
    private void ClearVisualization()
    {
        // Destroy all previously created objects
        foreach (var obj in createdObjects)
        {
            if (obj != null)
            {
                Destroy(obj);
            }
        }
        
        createdObjects.Clear();
    }
    
    private void OnDestroy()
    {
        ClearVisualization();
    }
}

// Helper component to make labels face the camera
public class Billboard : MonoBehaviour
{
    private Camera mainCamera;
    
    private void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindObjectOfType<Camera>();
        }
    }
    
    private void Update()
    {
        if (mainCamera != null)
        {
            transform.LookAt(transform.position + mainCamera.transform.rotation * Vector3.forward,
                mainCamera.transform.rotation * Vector3.up);
        }
    }
}