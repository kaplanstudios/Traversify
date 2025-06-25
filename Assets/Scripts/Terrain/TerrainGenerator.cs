using UnityEngine;

namespace Traversify
{
    /// <summary>
    /// Handles terrain generation from processed map data
    /// </summary>
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("Terrain Settings")]
        public float heightMapMultiplier = 30f;
        public bool generateWaterPlane = true;
        public float waterHeight = 0.1f;
        
        /// <summary>
        /// Generates a Unity Terrain from analysis results and a source texture
        /// </summary>
        /// <param name="analysisResults">The analysis results containing terrain features</param>
        /// <param name="sourceTexture">The original map texture</param>
        /// <param name="terrainSize">Size of the terrain (x,y,z)</param>
        /// <param name="terrainResolution">Resolution of the terrain heightmap</param>
        /// <param name="terrainMaterial">Optional material to apply to the terrain</param>
        /// <returns>The generated Unity Terrain component</returns>
        public UnityEngine.Terrain GenerateTerrain(
            AnalysisResults analysisResults,
            Texture2D sourceTexture,
            Vector3 terrainSize,
            int terrainResolution,
            Material terrainMaterial = null)
        {
            Debug.Log($"Generating terrain with resolution {terrainResolution}");
            
            // Create terrain game object and add components
            GameObject terrainObject = new GameObject("GeneratedTerrain");
            UnityEngine.Terrain terrain = terrainObject.AddComponent<UnityEngine.Terrain>();
            TerrainCollider terrainCollider = terrainObject.AddComponent<TerrainCollider>();
            
            // Create and configure terrain data
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = terrainResolution;
            terrainData.size = terrainSize;
            
            // Apply the terrain data
            terrain.terrainData = terrainData;
            terrainCollider.terrainData = terrainData;
            
            // Set terrain material if provided
            if (terrainMaterial != null)
            {
                terrain.materialTemplate = terrainMaterial;
            }
            
            // Generate and apply heightmap
            float[,] heights = GenerateHeightmap(analysisResults, sourceTexture, terrainResolution);
            terrainData.SetHeights(0, 0, heights);
            
            // Add terrain textures/layers based on analysis
            ApplyTerrainTextures(terrain, analysisResults);
            
            // Position the terrain at origin
            terrainObject.transform.position = Vector3.zero;
            
            return terrain;
        }
        
        private float[,] GenerateHeightmap(AnalysisResults analysisResults, Texture2D sourceTexture, int resolution)
        {
            // Create a heightmap based on analysis results and the source texture
            float[,] heights = new float[resolution, resolution];
            
            // If we have a heightmap in the analysis results, use it
            if (analysisResults.heightMap != null)
            {
                // Sample the height map and convert to heightmap values (0-1)
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        float u = (float)x / resolution;
                        float v = (float)y / resolution;
                        Color pixel = analysisResults.heightMap.GetPixelBilinear(u, v);
                        heights[y, x] = pixel.grayscale * heightMapMultiplier / 100f;
                    }
                }
            }
            else
            {
                // If no height map is available, generate a simple one based on the source texture
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        float u = (float)x / resolution;
                        float v = (float)y / resolution;
                        Color pixel = sourceTexture.GetPixelBilinear(u, v);
                        
                        // Convert color to grayscale and use as height
                        float grayscale = pixel.grayscale;
                        heights[y, x] = grayscale * heightMapMultiplier / 100f;
                    }
                }
            }
            
            return heights;
        }
        
        private void ApplyTerrainTextures(UnityEngine.Terrain terrain, AnalysisResults analysisResults)
        {
            // Create and apply basic terrain textures
            TerrainLayer[] layers = new TerrainLayer[1];
            
            // Create a default grass layer
            TerrainLayer grassLayer = new TerrainLayer();
            grassLayer.diffuseTexture = CreateDefaultTexture(Color.green);
            grassLayer.tileSize = new Vector2(10, 10);
            layers[0] = grassLayer;
            
            // Set the terrain layers
            terrain.terrainData.terrainLayers = layers;
        }
        
        private Texture2D CreateDefaultTexture(Color color)
        {
            // Create a simple colored texture when no actual textures are available
            Texture2D texture = new Texture2D(256, 256);
            Color[] pixels = new Color[256 * 256];
            
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            return texture;
        }
    }
}
