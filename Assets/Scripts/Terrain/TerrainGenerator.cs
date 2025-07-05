/*************************************************************************
 *  Traversify – TerrainGenerator.cs                                     *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-07-04                                                  *
 *  Desc   : Advanced terrain generation system that creates complete    *
 *           textured terrain meshes from MapAnalyzer results with       *
 *           height estimation data and terrain object classification.   *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.AI.Navigation;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Traversify.AI;
using Traversify.Core;

namespace Traversify.Terrain {
    [RequireComponent(typeof(UnityEngine.Terrain))]
    [RequireComponent(typeof(TerrainCollider))]
    public class TerrainGenerator : TraversifyComponent {
        #region Singleton
        private static TerrainGenerator _instance;
        public static TerrainGenerator Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<TerrainGenerator>();
                    if (_instance == null) {
                        GameObject go = new GameObject("TerrainGenerator");
                        _instance = go.AddComponent<TerrainGenerator>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        #endregion

        [System.Serializable]
        public class TerrainTexture {
            public Texture2D diffuse;
            public Texture2D normal;
            public Vector2 tileSize;
            public Vector2 tileOffset;
            public float metallic;
            public float smoothness;
        }

        #region Configuration
        [Header("Terrain Dimensions")]
        public Vector3 terrainSize = new Vector3(1000, 100, 1000);
        public int heightmapResolution = 513;
        public int detailResolution = 1024;
        public int alphamapResolution = 512;
        
        [Header("Generation Settings")]
        public bool useGPUAcceleration = true;
        public bool useMultithreading = true;
        public float generationTimeout = 300f;
        
        [Header("Texture Settings")]
        public TerrainTexture[] terrainTextures = new TerrainTexture[4];
        public Material[] terrainMaterials = new Material[4];
        public float textureBlendDistance = 20f;
        
        [Header("Water Settings")]
        public bool generateWater = true;
        public GameObject waterPrefab;
        public Material waterMaterial;
        public float waterLevel = 0.3f;
        
        [Header("Detail Settings")]
        public bool generateDetails = true;
        public DetailPrototype[] detailPrototypes = new DetailPrototype[0];
        public int detailDensity = 50;
        
        [Header("Tree Settings")]
        public bool generateTrees = true;
        public TreePrototype[] treePrototypes = new TreePrototype[0];
        public int treeDensity = 100;
        
        #endregion

        #region Private Fields
        
        private TraversifyDebugger _debugger;
        private UnityEngine.Terrain _terrain;
        private TerrainData _terrainData;
        private TerrainCollider _terrainCollider;
        private NavMeshSurface _navMeshSurface;
        
        // Generation state
        private bool _isGenerating = false;
        private AnalysisResults _currentAnalysisResults;
        private float[,] _heightMap;
        private float[,,] _alphaMap;
        private List<GameObject> _waterBodies = new List<GameObject>();
        
        // Terrain type mappings
        private Dictionary<string, int> _terrainTypeToTextureIndex = new Dictionary<string, int>
        {
            { "grass", 0 },
            { "grassland", 0 },
            { "forest", 1 },
            { "mountain", 2 },
            { "hill", 2 },
            { "cliff", 2 },
            { "desert", 3 },
            { "sand", 3 }
        };
        
        #endregion

        #region Data Structures
        
        /// <summary>
        /// Request object for terrain generation containing all necessary parameters.
        /// </summary>
        [System.Serializable]
        public class TerrainGenerationRequest
        {
            public Vector3 size = new Vector3(1000, 100, 1000);
            public int resolution = 513;
            public Texture2D heightmapTexture;
            public AnalysisResults analysisResults;
            public bool generateWater = true;
            public float waterLevel = 0.3f;
            public bool applyErosion = true;
            public int erosionIterations = 1000;
            public Material terrainMaterial;
            public TerrainTexture[] terrainTextures;
        }
        
        /// <summary>
        /// Result object containing the generated terrain and related data.
        /// </summary>
        [System.Serializable]
        public class TerrainGenerationResult
        {
            public UnityEngine.Terrain terrain;
            public List<GameObject> waterBodies = new List<GameObject>();
            public bool success;
            public string errorMessage;
            public Dictionary<string, object> metadata = new Dictionary<string, object>();
            public float generationTime;
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
                
                // Get or create terrain components
                _terrain = GetComponent<UnityEngine.Terrain>();
                if (_terrain == null)
                {
                    _terrain = gameObject.AddComponent<UnityEngine.Terrain>();
                }
                
                _terrainCollider = GetComponent<TerrainCollider>();
                if (_terrainCollider == null)
                {
                    _terrainCollider = gameObject.AddComponent<TerrainCollider>();
                }
                
                // Get or create NavMesh surface
                _navMeshSurface = GetComponent<NavMeshSurface>();
                if (_navMeshSurface == null)
                {
                    _navMeshSurface = gameObject.AddComponent<NavMeshSurface>();
                    _navMeshSurface.collectObjects = CollectObjects.Children;
                }
                
                InitializeTerrainData();
                
                Log("TerrainGenerator initialized successfully", LogCategory.System);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize TerrainGenerator: {ex.Message}", LogCategory.System);
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

        #region Terrain Data Initialization
        
        /// <summary>
        /// Initialize terrain data with proper settings.
        /// </summary>
        private void InitializeTerrainData()
        {
            _terrainData = new TerrainData();
            _terrainData.size = terrainSize;
            _terrainData.heightmapResolution = heightmapResolution;
            _terrainData.alphamapResolution = alphamapResolution;
            
            // Set up terrain textures
            SetupTerrainTextures();
            
            // Apply terrain data
            _terrain.terrainData = _terrainData;
            _terrainCollider.terrainData = _terrainData;
            
            Log("Terrain data initialized", LogCategory.System);
        }
        
        /// <summary>
        /// Set up terrain texture layers.
        /// </summary>
        private void SetupTerrainTextures()
        {
            var layers = new TerrainLayer[terrainTextures.Length];
            
            for (int i = 0; i < terrainTextures.Length; i++)
            {
                if (terrainTextures[i] != null)
                {
                    var layer = new TerrainLayer();
                    layer.diffuseTexture = terrainTextures[i].diffuse;
                    layer.normalMapTexture = terrainTextures[i].normal;
                    layer.tileSize = terrainTextures[i].tileSize;
                    layer.tileOffset = terrainTextures[i].tileOffset;
                    layer.metallic = terrainTextures[i].metallic;
                    layer.smoothness = terrainTextures[i].smoothness;
                    layers[i] = layer;
                }
            }
            
            _terrainData.terrainLayers = layers;
        }
        
        #endregion

        #region Main Generation Pipeline
        
        /// <summary>
        /// Generate complete textured terrain from MapAnalyzer results.
        /// </summary>
        public IEnumerator GenerateTerrainFromAnalysis(AnalysisResults analysisResults,
            System.Action<TerrainGenerationResult> onComplete = null,
            System.Action<string> onError = null,
            System.Action<string, float> onProgress = null)
        {
            if (analysisResults == null)
            {
                onError?.Invoke("Analysis results are null");
                yield break;
            }

            _isGenerating = true;
            _currentAnalysisResults = analysisResults;

            Log("Starting terrain generation from analysis results", LogCategory.Terrain);
            float startTime = Time.time;

            // Step 1: Generate height map
            onProgress?.Invoke("Generating height map from analysis...", 0.2f);
            yield return StartCoroutine(GenerateHeightMapFromAnalysis(analysisResults));

            // Step 2: Apply height map
            onProgress?.Invoke("Applying height map to terrain...", 0.4f);
            ApplyHeightMapToTerrain();

            // Step 3: Textures
            onProgress?.Invoke("Applying terrain textures...", 0.6f);
            yield return StartCoroutine(GenerateTerrainTextures(analysisResults));

            // Step 4: Water bodies
            onProgress?.Invoke("Generating water bodies...", 0.8f);
            yield return StartCoroutine(GenerateWaterBodies(analysisResults));

            // Step 5: Details & trees
            onProgress?.Invoke("Adding terrain details...", 0.9f);
            yield return StartCoroutine(GenerateTerrainDetails(analysisResults));

            // Step 6: Finalize
            onProgress?.Invoke("Finalizing terrain...", 1.0f);
            FinalizeTerrain();

            float generationTime = Time.time - startTime;
            var result = new TerrainGenerationResult
            {
                terrain = _terrain,
                waterBodies = new List<GameObject>(_waterBodies),
                success = true,
                generationTime = generationTime
            };

            Log($"Terrain generation completed successfully in {generationTime:F2} seconds", LogCategory.Terrain);
            onComplete?.Invoke(result);

            _isGenerating = false;
        }

        // New wrapper for manager
        public IEnumerator GenerateTerrain(TerrainGenerationRequest request,
            System.Action<UnityEngine.Terrain> onComplete,
            System.Action<string> onError,
            System.Action<float, string> onProgress)
        {
            // configure parameters
            applyErosion = request.applyErosion;
            erosionIterations = request.erosionIterations;
            terrainSize = request.size;
            terrainResolution = request.resolution;
            heightMapMultiplier = 1f; // adjust if needed
            generateWater = request.generateWater;
            waterLevel = request.waterLevel;

            // perform generation
            TerrainGenerationResult callbackResult = null;
            yield return StartCoroutine(GenerateTerrainFromAnalysis(request.analysisResults,
                res => callbackResult = res,
                err => onError?.Invoke(err),
                (msg, prog) => onProgress?.Invoke(prog, msg)
            ));

            if (callbackResult != null && callbackResult.success)
                onComplete?.Invoke(callbackResult.terrain);
        }

        /// <summary>
        /// Synchronous stub for CreateTerrainFromAnalysis
        /// </summary>
        public UnityEngine.Terrain GenerateTerrainFromAnalysis(AnalysisResults analysisResults, Texture2D sourceTexture)
        {
            // Basic synchronous call stub
            StartCoroutine(GenerateTerrain(new TerrainGenerationRequest {
                size = terrainSize,
                resolution = heightmapResolution,
                heightmapTexture = sourceTexture,
                analysisResults = analysisResults,
                generateWater = generateWater,
                waterLevel = waterLevel,
                applyErosion = applyErosion,
                erosionIterations = erosionIterations
            }, _ => {}, _ => {}, (_,_)=>{}));
            return _terrain;
        }

        #region Height Map Generation
        
        /// <summary>
        /// Generate height map from analysis results and height estimation data.
        /// </summary>
        private IEnumerator GenerateHeightMapFromAnalysis(AnalysisResults analysisResults)
        {
            _heightMap = new float[heightmapResolution, heightmapResolution];
            
            // Start with base height map from analysis if available
            if (analysisResults.heightMap != null)
            {
                yield return StartCoroutine(ApplyAnalysisHeightMap(analysisResults.heightMap));
            }
            else
            {
                // Generate base procedural height map
                GenerateBaseHeightMap();
            }
            
            // Apply terrain object influences
            ApplyTerrainObjectInfluences(analysisResults.terrainObjects);
            
            // Smooth height map
            SmoothHeightMap();
            
            Log("Height map generation completed", LogCategory.Terrain);
        }
        
        /// <summary>
        /// Apply height map from analysis results.
        /// </summary>
        private IEnumerator ApplyAnalysisHeightMap(Texture2D heightMapTexture)
        {
            var pixels = heightMapTexture.GetPixels();
            int textureWidth = heightMapTexture.width;
            int textureHeight = heightMapTexture.height;
            
            for (int y = 0; y < heightmapResolution; y++)
            {
                for (int x = 0; x < heightmapResolution; x++)
                {
                    // Map terrain coordinates to texture coordinates
                    int texX = Mathf.FloorToInt((float)x / heightmapResolution * textureWidth);
                    int texY = Mathf.FloorToInt((float)y / heightmapResolution * textureHeight);
                    
                    texX = Mathf.Clamp(texX, 0, textureWidth - 1);
                    texY = Mathf.Clamp(texY, 0, textureHeight - 1);
                    
                    int pixelIndex = texY * textureWidth + texX;
                    float height = pixels[pixelIndex].r; // Use red channel for height
                    
                    _heightMap[x, y] = height;
                }
                
                // Yield periodically to prevent frame drops
                if (y % 10 == 0)
                {
                    yield return null;
                }
            }
        }
        
        /// <summary>
        /// Generate base procedural height map if no analysis height map is available.
        /// </summary>
        private void GenerateBaseHeightMap()
        {
            for (int y = 0; y < heightmapResolution; y++)
            {
                for (int x = 0; x < heightmapResolution; x++)
                {
                    float height = Mathf.PerlinNoise(
                        (float)x / heightmapResolution * 10f,
                        (float)y / heightmapResolution * 10f
                    ) * 0.1f; // Low base height
                    
                    _heightMap[x, y] = height;
                }
            }
        }
        
        /// <summary>
        /// Apply influences from detected terrain objects.
        /// </summary>
        private void ApplyTerrainObjectInfluences(List<DetectedObject> terrainObjects)
        {
            foreach (var terrainObj in terrainObjects)
            {
                ApplyTerrainObjectInfluence(terrainObj);
            }
        }
        
        /// <summary>
        /// Apply influence from a single terrain object.
        /// </summary>
        private void ApplyTerrainObjectInfluence(DetectedObject terrainObj)
        {
            // Convert image coordinates to height map coordinates
            float normalizedX = terrainObj.boundingBox.center.x / _currentAnalysisResults.sourceImage.width;
            float normalizedY = terrainObj.boundingBox.center.y / _currentAnalysisResults.sourceImage.height;
            
            int centerX = Mathf.FloorToInt(normalizedX * heightmapResolution);
            int centerY = Mathf.FloorToInt(normalizedY * heightmapResolution);
            
            // Calculate influence radius based on bounding box size
            float radiusX = (terrainObj.boundingBox.width / _currentAnalysisResults.sourceImage.width) * heightmapResolution * 0.5f;
            float radiusY = (terrainObj.boundingBox.height / _currentAnalysisResults.sourceImage.height) * heightmapResolution * 0.5f;
            float radius = Mathf.Max(radiusX, radiusY);
            
            // Get height influence for terrain type
            float heightInfluence = GetHeightInfluenceForTerrainType(terrainObj.className);
            
            // Apply influence in circular area
            int minX = Mathf.Max(0, centerX - Mathf.CeilToInt(radius));
            int maxX = Mathf.Min(heightmapResolution - 1, centerX + Mathf.CeilToInt(radius));
            int minY = Mathf.Max(0, centerY - Mathf.CeilToInt(radius));
            int maxY = Mathf.Min(heightmapResolution - 1, centerY + Mathf.CeilToInt(radius));
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
                    if (distance <= radius)
                    {
                        float falloff = 1f - (distance / radius);
                        falloff = Mathf.SmoothStep(0f, 1f, falloff);
                        
                        float currentHeight = _heightMap[x, y];
                        float targetHeight = heightInfluence * falloff;
                        
                        // Blend based on terrain type
                        switch (terrainObj.className.ToLower())
                        {
                            case "water":
                            case "lake":
                            case "river":
                            case "ocean":
                                _heightMap[x, y] = Mathf.Min(currentHeight, targetHeight);
                                break;
                            default:
                                _heightMap[x, y] = Mathf.Max(currentHeight, targetHeight);
                                break;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Get height influence value for a terrain type.
        /// </summary>
        private float GetHeightInfluenceForTerrainType(string terrainType)
        {
            switch (terrainType.ToLower())
            {
                case "mountain": return 0.8f;
                case "hill": return 0.4f;
                case "plateau": return 0.6f;
                case "cliff": return 0.7f;
                case "water":
                case "lake":
                case "river":
                case "ocean": return 0.0f;
                case "valley": return 0.1f;
                default: return 0.2f;
            }
        }
        
        /// <summary>
        /// Smooth the height map to remove sharp transitions.
        /// </summary>
        private void SmoothHeightMap()
        {
            float[,] smoothed = new float[heightmapResolution, heightmapResolution];
            int kernelSize = 3;
            
            for (int y = 0; y < heightmapResolution; y++)
            {
                for (int x = 0; x < heightmapResolution; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    
                    for (int ky = -kernelSize; ky <= kernelSize; ky++)
                    {
                        for (int kx = -kernelSize; kx <= kernelSize; kx++)
                        {
                            int nx = x + kx;
                            int ny = y + ky;
                            
                            if (nx >= 0 && nx < heightmapResolution && ny >= 0 && ny < heightmapResolution)
                            {
                                sum += _heightMap[nx, ny];
                                count++;
                            }
                        }
                    }
                    
                    smoothed[x, y] = count > 0 ? sum / count : _heightMap[x, y];
                }
            }
            
            _heightMap = smoothed;
        }
        
        /// <summary>
        /// Apply generated height map to terrain.
        /// </summary>
        private void ApplyHeightMapToTerrain()
        {
            _terrainData.SetHeights(0, 0, _heightMap);
            Log("Applied height map to terrain", LogCategory.Terrain);
        }
        
        #endregion

        #region Texture Generation
        
        /// <summary>
        /// Generate terrain textures based on detected terrain types.
        /// </summary>
        private IEnumerator GenerateTerrainTextures(AnalysisResults analysisResults)
        {
            _alphaMap = new float[alphamapResolution, alphamapResolution, terrainTextures.Length];
            
            // Initialize with default texture (usually grass)
            for (int y = 0; y < alphamapResolution; y++)
            {
                for (int x = 0; x < alphamapResolution; x++)
                {
                    _alphaMap[x, y, 0] = 1f; // Default to first texture
                }
            }
            
            // Apply terrain object textures
            foreach (var terrainObj in analysisResults.terrainObjects)
            {
                ApplyTerrainObjectTexture(terrainObj);
                yield return null; // Prevent frame drops
            }
            
            // Normalize alpha maps
            NormalizeAlphaMaps();
            
            // Apply to terrain
            _terrainData.SetAlphamaps(0, 0, _alphaMap);
            
            Log("Applied terrain textures", LogCategory.Terrain);
        }
        
        /// <summary>
        /// Apply texture for a terrain object.
        /// </summary>
        private void ApplyTerrainObjectTexture(DetectedObject terrainObj)
        {
            // Get texture index for terrain type
            int textureIndex = GetTextureIndexForTerrainType(terrainObj.className);
            if (textureIndex < 0 || textureIndex >= terrainTextures.Length) return;
            
            // Convert to alpha map coordinates
            float normalizedX = terrainObj.boundingBox.center.x / _currentAnalysisResults.sourceImage.width;
            float normalizedY = terrainObj.boundingBox.center.y / _currentAnalysisResults.sourceImage.height;
            
            int centerX = Mathf.FloorToInt(normalizedX * alphamapResolution);
            int centerY = Mathf.FloorToInt(normalizedY * alphamapResolution);
            
            float radiusX = (terrainObj.boundingBox.width / _currentAnalysisResults.sourceImage.width) * alphamapResolution * 0.5f;
            float radiusY = (terrainObj.boundingBox.height / _currentAnalysisResults.sourceImage.height) * alphamapResolution * 0.5f;
            float radius = Mathf.Max(radiusX, radiusY);
            
            // Apply texture in area
            int minX = Mathf.Max(0, centerX - Mathf.CeilToInt(radius));
            int maxX = Mathf.Min(alphamapResolution - 1, centerX + Mathf.CeilToInt(radius));
            int minY = Mathf.Max(0, centerY - Mathf.CeilToInt(radius));
            int maxY = Mathf.Min(alphamapResolution - 1, centerY + Mathf.CeilToInt(radius));
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
                    if (distance <= radius)
                    {
                        float falloff = 1f - (distance / radius);
                        falloff = Mathf.SmoothStep(0f, 1f, falloff);
                        
                        // Set texture weight
                        _alphaMap[x, y, textureIndex] = Mathf.Max(_alphaMap[x, y, textureIndex], falloff);
                    }
                }
            }
        }
        
        /// <summary>
        /// Get texture index for terrain type.
        /// </summary>
        private int GetTextureIndexForTerrainType(string terrainType)
        {
            string lowerType = terrainType.ToLower();
            return _terrainTypeToTextureIndex.ContainsKey(lowerType) ? _terrainTypeToTextureIndex[lowerType] : 0;
        }
        
        /// <summary>
        /// Normalize alpha maps so they sum to 1.
        /// </summary>
        private void NormalizeAlphaMaps()
        {
            for (int y = 0; y < alphamapResolution; y++)
            {
                for (int x = 0; x < alphamapResolution; x++)
                {
                    float sum = 0f;
                    for (int i = 0; i < terrainTextures.Length; i++)
                    {
                        sum += _alphaMap[x, y, i];
                    }
                    
                    if (sum > 0f)
                    {
                        for (int i = 0; i < terrainTextures.Length; i++)
                        {
                            _alphaMap[x, y, i] /= sum;
                        }
                    }
                }
            }
        }
        
        #endregion

        #region Water Bodies Generation
        
        /// <summary>
        /// Generate water bodies from detected water terrain objects.
        /// </summary>
        private IEnumerator GenerateWaterBodies(AnalysisResults analysisResults)
        {
            if (!generateWater) yield break;
            
            // Clear existing water bodies
            foreach (var water in _waterBodies)
            {
                if (water != null) DestroyImmediate(water);
            }
            _waterBodies.Clear();
            
            // Generate water for each water terrain object
            var waterObjects = analysisResults.terrainObjects.Where(obj => IsWaterTerrain(obj.className)).ToList();
            
            foreach (var waterObj in waterObjects)
            {
                yield return StartCoroutine(CreateWaterBody(waterObj));
            }
            
            Log($"Generated {_waterBodies.Count} water bodies", LogCategory.Terrain);
        }
        
        /// <summary>
        /// Check if terrain type is water.
        /// </summary>
        private bool IsWaterTerrain(string terrainType)
        {
            string lowerType = terrainType.ToLower();
            return lowerType.Contains("water") || lowerType.Contains("lake") || 
                   lowerType.Contains("river") || lowerType.Contains("ocean");
        }
        
        /// <summary>
        /// Create a water body for a detected water terrain object.
        /// </summary>
        private IEnumerator CreateWaterBody(DetectedObject waterObj)
        {
            if (waterPrefab == null)
            {
                // Create simple water plane
                GameObject waterGO = GameObject.CreatePrimitive(PrimitiveType.Plane);
                waterGO.name = $"Water_{waterObj.className}";
                waterGO.transform.parent = transform;
                
                // Position and scale
                Vector3 worldPos = ConvertImageToWorldPosition(
                    waterObj.boundingBox.center,
                    _currentAnalysisResults.sourceImage,
                    new Vector2(terrainSize.x, terrainSize.z)
                );
                
                waterGO.transform.position = new Vector3(worldPos.x, waterLevel * terrainSize.y, worldPos.z);
                
                float scaleX = (waterObj.boundingBox.width / _currentAnalysisResults.sourceImage.width) * terrainSize.x * 0.1f;
                float scaleZ = (waterObj.boundingBox.height / _currentAnalysisResults.sourceImage.height) * terrainSize.z * 0.1f;
                waterGO.transform.localScale = new Vector3(scaleX, 1f, scaleZ);
                
                // Apply water material
                if (waterMaterial != null)
                {
                    waterGO.GetComponent<Renderer>().material = waterMaterial;
                }
                
                _waterBodies.Add(waterGO);
            }
            else
            {
                // Use water prefab
                GameObject waterGO = Instantiate(waterPrefab, transform);
                waterGO.name = $"Water_{waterObj.className}";
                
                Vector3 worldPos = ConvertImageToWorldPosition(
                    waterObj.boundingBox.center,
                    _currentAnalysisResults.sourceImage,
                    new Vector2(terrainSize.x, terrainSize.z)
                );
                
                waterGO.transform.position = new Vector3(worldPos.x, waterLevel * terrainSize.y, worldPos.z);
                _waterBodies.Add(waterGO);
            }
            
            yield return null;
        }
        
        #endregion

        #region Terrain Details Generation
        
        /// <summary>
        /// Generate terrain details (grass, rocks, etc.) based on analysis results.
        /// </summary>
        private IEnumerator GenerateTerrainDetails(AnalysisResults analysisResults)
        {
            if (!generateDetails && !generateTrees) yield break;
            
            // Set up detail prototypes
            if (generateDetails && detailPrototypes.Length > 0)
            {
                _terrainData.detailPrototypes = detailPrototypes;
                _terrainData.SetDetailResolution(detailResolution, 8);
                
                // Generate detail maps
                for (int i = 0; i < detailPrototypes.Length; i++)
                {
                    var detailMap = GenerateDetailMap(i, analysisResults);
                    _terrainData.SetDetailLayer(0, 0, i, detailMap);
                    yield return null;
                }
            }
            
            // Set up tree prototypes
            if (generateTrees && treePrototypes.Length > 0)
            {
                _terrainData.treePrototypes = treePrototypes;
                GenerateTreeInstances(analysisResults);
                yield return null;
            }
            
            Log("Generated terrain details", LogCategory.Terrain);
        }
        
        /// <summary>
        /// Generate detail map for a specific detail prototype.
        /// </summary>
        private int[,] GenerateDetailMap(int detailIndex, AnalysisResults analysisResults)
        {
            var detailMap = new int[detailResolution, detailResolution];
            
            // Generate details based on terrain type
            for (int y = 0; y < detailResolution; y++)
            {
                for (int x = 0; x < detailResolution; x++)
                {
                    // Sample height and determine if suitable for detail
                    float normalizedX = (float)x / detailResolution;
                    float normalizedY = (float)y / detailResolution;
                    
                    float height = SampleHeightAtNormalizedPosition(normalizedX, normalizedY);
                    float slope = CalculateSlope(normalizedX, normalizedY);
                    
                    // Generate details based on conditions
                    if (height > waterLevel && slope < 0.5f) // Not underwater and not too steep
                    {
                        float density = UnityEngine.Random.Range(0f, 1f);
                        if (density < (detailDensity / 100f))
                        {
                            detailMap[x, y] = UnityEngine.Random.Range(1, 5);
                        }
                    }
                }
            }
            
            return detailMap;
        }
        
        /// <summary>
        /// Generate tree instances.
        /// </summary>
        private void GenerateTreeInstances(AnalysisResults analysisResults)
        {
            var treeInstances = new List<TreeInstance>();
            
            // Generate trees based on forest terrain objects
            var forestObjects = analysisResults.terrainObjects.Where(obj => 
                obj.className.ToLower().Contains("forest") || 
                obj.className.ToLower().Contains("tree")).ToList();
            
            foreach (var forestObj in forestObjects)
            {
                int treeCount = UnityEngine.Random.Range(treeDensity / 2, treeDensity);
                
                for (int i = 0; i < treeCount; i++)
                {
                    Vector2 normalizedPos = GetRandomPositionInBoundingBox(forestObj.boundingBox);
                    float height = SampleHeightAtNormalizedPosition(normalizedPos.x, normalizedPos.y);
                    
                    if (height > waterLevel) // Don't place trees underwater
                    {
                        var treeInstance = new TreeInstance
                        {
                            position = new Vector3(normalizedPos.x, height, normalizedPos.y),
                            prototypeIndex = UnityEngine.Random.Range(0, treePrototypes.Length),
                            widthScale = UnityEngine.Random.Range(0.8f, 1.2f),
                            heightScale = UnityEngine.Random.Range(0.8f, 1.2f),
                            rotation = UnityEngine.Random.Range(0f, 2f * Mathf.PI),
                            color = Color.white,
                            lightmapColor = Color.white
                        };
                        
                        treeInstances.Add(treeInstance);
                    }
                }
            }
            
            _terrainData.treeInstances = treeInstances.ToArray();
        }
        
        #endregion

        #region Terrain Finalization
        
        /// <summary>
        /// Finalize terrain generation with NavMesh and optimizations.
        /// </summary>
        private void FinalizeTerrain()
        {
            // Refresh terrain
            _terrain.Flush();
            
            // Build NavMesh
            if (_navMeshSurface != null)
            {
                _navMeshSurface.BuildNavMesh();
                Log("NavMesh generated", LogCategory.Terrain);
            }
            
            Log("Terrain finalized", LogCategory.Terrain);
        }
        
        #endregion  // closes Terrain Finalization
        #endregion  // closes Main Generation Pipeline

        #region Utility Methods
        
        /// <summary>
        /// Convert image coordinates to world position.
        /// </summary>
        private Vector3 ConvertImageToWorldPosition(Vector2 imagePosition, Texture2D sourceImage, Vector2 worldSize)
        {
            float normalizedX = imagePosition.x / sourceImage.width;
            float normalizedY = imagePosition.y / sourceImage.height;
            
            float worldX = (normalizedX - 0.5f) * worldSize.x;
            float worldZ = (normalizedY - 0.5f) * worldSize.y;
            
            return new Vector3(worldX, 0f, worldZ);
        }
        
        /// <summary>
        /// Sample height at normalized position.
        /// </summary>
        private float SampleHeightAtNormalizedPosition(float x, float y)
        {
            int heightX = Mathf.FloorToInt(x * (heightmapResolution - 1));
            int heightY = Mathf.FloorToInt(y * (heightmapResolution - 1));
            
            heightX = Mathf.Clamp(heightX, 0, heightmapResolution - 1);
            heightY = Mathf.Clamp(heightY, 0, heightmapResolution - 1);
            
            return _heightMap[heightX, heightY];
        }
        
        /// <summary>
        /// Calculate slope at normalized position.
        /// </summary>
        private float CalculateSlope(float x, float y)
        {
            float height = SampleHeightAtNormalizedPosition(x, y);
            float heightRight = SampleHeightAtNormalizedPosition(x + 0.01f, y);
            float heightUp = SampleHeightAtNormalizedPosition(x, y + 0.01f);
            
            Vector3 normal = Vector3.Cross(
                new Vector3(0.01f, heightRight - height, 0f),
                new Vector3(0f, heightUp - height, 0.01f)
            ).normalized;
            
            return 1f - Vector3.Dot(normal, Vector3.up);
        }
        
        /// <summary>
        /// Get random position within bounding box.
        /// </summary>
        private Vector2 GetRandomPositionInBoundingBox(Rect boundingBox)
        {
            float normalizedX = (boundingBox.x + UnityEngine.Random.Range(0f, boundingBox.width)) / _currentAnalysisResults.sourceImage.width;
            float normalizedY = (boundingBox.y + UnityEngine.Random.Range(0f, boundingBox.height)) / _currentAnalysisResults.sourceImage.height;
            
            return new Vector2(normalizedX, normalizedY);
        }
        
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

        // Properties for Manager reflection
        public float terrainHeight { get { return terrainSize.y; } set { terrainSize = new Vector3(terrainSize.x, value, terrainSize.z); } }
        public float waterThreshold { get { return waterLevel; } set { waterLevel = value; } }
        public int terrainResolution { get { return heightmapResolution; } set { heightmapResolution = value; } }
        public float heightMapMultiplier { get; set; } = 1f;
        public bool applyErosion { get; set; } = true;
        public int erosionIterations { get; set; } = 1000;
    }
}
