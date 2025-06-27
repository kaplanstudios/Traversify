using System;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

[RequireComponent(typeof(Terrain))]
public class TerrainGenerator : MonoBehaviour
{
    [Header("Terrain Dimensions")]
    public int terrainWidth = 256;
    public int terrainLength = 256;
    public float terrainHeight = 50f;
    [Header("Noise Settings")]
    public float noiseScale = 50f;
    public int noiseOctaves = 4;
    public float noisePersistence = 0.5f;
    public float noiseLacunarity = 2.0f;
    public int seed = 0;
    [Header("Water and Biome")]
    [Range(0,1)] public float waterThreshold = 0.3f;  // fraction of max height to consider water level
    public GameObject waterPrefab;    // Prefab for water plane (optional)
    [Header("Erosion Settings")]
    public int erosionIterations = 10000;
    public float erosionStrength = 0.1f;
    [Header("Pathfinding")]
    public NavMeshSurface navMeshSurface;  // NavMeshSurface component for runtime baking

    private Terrain terrain;
    private TerrainData terrainData;
    private float[,] heights;
    private bool terrainGenerated = false;

    void Start()
    {
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;
        // Initialize TerrainData size
        terrainData.heightmapResolution = terrainWidth + 1;
        terrainData.size = new Vector3(terrainWidth, terrainHeight, terrainLength);

        GenerateTerrain();
        ApplyTerrainData();
        terrainGenerated = true;

        // Build NavMesh for pathfinding
        if (navMeshSurface != null)
        {
            navMeshSurface.BuildNavMesh();  // Bake the navmesh at runtime:contentReference[oaicite:37]{index=37}
        }
    }

    /// <summary>
    /// Generate terrain heights using fractal noise and simulate erosion and water.
    /// </summary>
    private void GenerateTerrain()
    {
        heights = new float[terrainWidth, terrainLength];
        System.Random prng = (seed != 0) ? new System.Random(seed) : new System.Random();
        // Generate base heightmap with fractal noise (fBM using Perlin)
        for (int x = 0; x < terrainWidth; x++)
        {
            for (int y = 0; y < terrainLength; y++)
            {
                float nx = (float)x / terrainWidth;
                float ny = (float)y / terrainLength;
                float amplitude = 1;
                float frequency = 1;
                float heightValue = 0;
                for (int o = 0; o < noiseOctaves; o++)
                {
                    float sampleX = nx * noiseScale * frequency;
                    float sampleY = ny * noiseScale * frequency;
                    float noiseVal = Mathf.PerlinNoise(sampleX + prng.Next(-10000, 10000), sampleY + prng.Next(-10000, 10000));
                    heightValue += noiseVal * amplitude;
                    amplitude *= noisePersistence;
                    frequency *= noiseLacunarity;
                }
                heightValue /= (2 - 1/Mathf.Pow(2, noiseOctaves)); // normalize fBM roughly to [0,1]
                heights[x, y] = heightValue;
            }
        }

        // Identify water regions by thresholding height
        float waterLevel = waterThreshold; // since heights normalized 0-1
        bool[,] isWater = new bool[terrainWidth, terrainLength];
        for (int i = 0; i < terrainWidth; i++)
        {
            for (int j = 0; j < terrainLength; j++)
            {
                if (heights[i, j] < waterLevel)
                {
                    isWater[i, j] = true;
                    heights[i, j] = waterLevel; // flatten water areas to the water level
                }
            }
        }

        // Simulate hydraulic erosion for more realistic rivers:contentReference[oaicite:38]{index=38}
        for (int iter = 0; iter < erosionIterations; iter++)
        {
            int sx = Random.Range(1, terrainWidth - 2);
            int sy = Random.Range(1, terrainLength - 2);
            float sediment = 0f;
            float px = sx;
            float py = sy;
            // Simulate a raindrop particle descending
            for (int step = 0; step < 100; step++)
            {
                int cx = Mathf.FloorToInt(px);
                int cy = Mathf.FloorToInt(py);
                // Compute normal via finite difference to decide direction:contentReference[oaicite:39]{index=39}
                float hL = heights[Mathf.Max(cx-1,0), cy];
                float hR = heights[Mathf.Min(cx+1, terrainWidth-1), cy];
                float hD = heights[cx, Mathf.Max(cy-1,0)];
                float hU = heights[cx, Mathf.Min(cy+1, terrainLength-1)];
                Vector3 normal = new Vector3(hL - hR, 2f, hD - hU).normalized;
                // If nearly flat, deposit sediment and stop
                if (normal.y < 0.01f) {
                    heights[cx, cy] += sediment; // deposit whatever sediment is carrying
                    break;
                }
                // Erode current position
                float erodeAmount = erosionStrength * (1 - normal.y);
                float taken = Mathf.Min(heights[cx, cy] - 0f, erodeAmount);
                heights[cx, cy] -= taken;
                sediment += taken;
                // Move in direction of slope (gravity)
                px += -normal.x;
                py += -normal.z;
                if (px < 1 || px >= terrainWidth-1 || py < 1 || py >= terrainLength-1)
                    break; // out of bounds
            }
        }

        // After erosion, re-apply water flattening to ensure water regions remain flat
        for (int i = 0; i < terrainWidth; i++)
        {
            for (int j = 0; j < terrainLength; j++)
            {
                if (isWater[i, j])
                {
                    heights[i, j] = waterLevel;
                }
            }
        }
    }

    /// <summary>
    /// Apply the generated heightmap to the Unity Terrain.
    /// Also spawn water objects and paint textures if applicable.
    /// </summary>
    private void ApplyTerrainData()
    {
        // Set heights on Unity TerrainData (note: Unity expects a 2D array indexed as [y,x])
        terrainData.SetHeights(0, 0, heights);

        // Add a water plane covering the water areas if a prefab is provided
        if (waterPrefab != null)
        {
            float waterY = waterThreshold * terrainHeight;
            GameObject water = Instantiate(waterPrefab, transform);
            water.transform.position = new Vector3(terrainWidth/2, waterY, terrainLength/2);
            // Scale water plane to cover entire terrain
            // Assuming waterPrefab is a unit plane, scale to terrain size
            water.transform.localScale = new Vector3(terrainWidth/10f, 1, terrainLength/10f);
        }

        // (Optional) Paint terrain textures based on height/slope (not fully implemented for brevity)
        // We could use TerrainData.SetAlphamaps to assign textures for grass, rock, etc., based on height/slope.
    }

    // Debug draw: visualize water threshold line or other gizmos
    void OnDrawGizmosSelected()
    {
        if (!terrainGenerated) return;
        Gizmos.color = Color.blue;
        // Draw a square around water level
        float waterY = waterThreshold * terrainHeight + transform.position.y;
        Gizmos.DrawWireCube(new Vector3(transform.position.x + terrainWidth/2, waterY, transform.position.z + terrainLength/2),
                             new Vector3(terrainWidth, 0.1f, terrainLength));
    }
}
