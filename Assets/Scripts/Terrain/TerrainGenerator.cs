/*************************************************************************
 *  Traversify – TerrainGenerator.cs                                     *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Updated: 2025-07-05                                                  *
 *  Desc   : Advanced terrain generation system that creates Unity       *
 *           terrains from analysis results with efficient height map    *
 *           application, water generation, and accurate pathfinding.    *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;
using Traversify.AI;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;

namespace Traversify.Terrain {
    /// <summary>
    /// Generates Unity terrain meshes from analysis results with support for
    /// height maps, texturing, water bodies, and pathfinding.
    /// </summary>
    [RequireComponent(typeof(TraversifyDebugger))]
    public class TerrainGenerator : TraversifyComponent {
        #region Singleton Pattern
        
        private static TerrainGenerator _instance;
        
        /// <summary>
        /// Singleton instance of the TerrainGenerator.
        /// </summary>
        public static TerrainGenerator Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<TerrainGenerator>();
                    if (_instance == null) {
                        GameObject go = new GameObject("TerrainGenerator");
                        _instance = go.AddComponent<TerrainGenerator>();
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Fields
        
        [Header("Terrain Settings")]
        [Tooltip("Size of the terrain in Unity units")]
        [SerializeField] private Vector3 _terrainSize = new Vector3(500, 100, 500);
        
        [Tooltip("Resolution of the heightmap (must be 2^n+1)")]
        [SerializeField] private int _heightmapResolution = 513;
        
        [Tooltip("Resolution of detail maps")]
        [SerializeField] private int _detailResolution = 1024;
        
        [Tooltip("Height map multiplier")]
        [Range(0.1f, 10f)]
        [SerializeField] private float _heightMapMultiplier = 1.0f;
        
        [Tooltip("Base material for terrain")]
        [SerializeField] private Material _terrainMaterial;
        
        [Header("Water Settings")]
        [Tooltip("Generate water features")]
        [SerializeField] private bool _generateWater = true;
        
        [Tooltip("Water level (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float _waterLevel = 0.3f;
        
        [Tooltip("Water material")]
        [SerializeField] private Material _waterMaterial;
        
        [Header("Erosion Settings")]
        [Tooltip("Apply erosion to heightmap")]
        [SerializeField] private bool _applyErosion = true;
        
        [Tooltip("Erosion iterations")]
        [Range(100, 10000)]
        [SerializeField] private int _erosionIterations = 1000;
        
        [Tooltip("Erosion strength")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _erosionStrength = 0.3f;
        
        [Header("Terrain Textures")]
        [Tooltip("Textures for different terrain types")]
        [SerializeField] private List<TerrainTextureMapping> _terrainTextures = new List<TerrainTextureMapping>();
        
        [Header("Performance")]
        [Tooltip("Use multi-threading for terrain generation")]
        [SerializeField] private bool _useMultithreading = true;
        
        [Tooltip("Use GPU acceleration when available")]
        [SerializeField] private bool _useGPU = true;
        
        [Tooltip("Generate terrain in chunks")]
        [SerializeField] private bool _generateInChunks = false;
        
        [Tooltip("Chunk size in vertices")]
        [SerializeField] private int _chunkSize = 128;
        
        [Header("Detail Objects")]
        [Tooltip("Detail prototypes for terrain")]
        [SerializeField] private List<DetailPrototypeMapping> _detailPrototypes = new List<DetailPrototypeMapping>();
        
        [Tooltip("Tree prototypes for terrain")]
        [SerializeField] private List<TreePrototypeMapping> _treePrototypes = new List<TreePrototypeMapping>();
        
        #endregion
        
        #region Public Properties
        
        /// <summary>
        /// Size of the terrain in Unity units.
        /// </summary>
        public Vector3 terrainSize {
            get => _terrainSize;
            set => _terrainSize = value;
        }
        
        /// <summary>
        /// Resolution of the heightmap.
        /// </summary>
        public int heightmapResolution {
            get => _heightmapResolution;
            set {
                // Ensure resolution is 2^n + 1
                int n = Mathf.FloorToLog(value - 1, 2);
                _heightmapResolution = Mathf.Clamp(Mathf.FloorToInt(Mathf.Pow(2, n)) + 1, 33, 4097);
            }
        }
        
        /// <summary>
        /// Whether to generate water features.
        /// </summary>
        public bool generateWater {
            get => _generateWater;
            set => _generateWater = value;
        }
        
        /// <summary>
        /// Water level as a fraction of terrain height.
        /// </summary>
        public float waterLevel {
            get => _waterLevel;
            set => _waterLevel = Mathf.Clamp01(value);
        }
        
        /// <summary>
        /// Whether to apply erosion to heightmap.
        /// </summary>
        public bool applyErosion {
            get => _applyErosion;
            set => _applyErosion = value;
        }
        
        /// <summary>
        /// Number of erosion iterations.
        /// </summary>
        public int erosionIterations {
            get => _erosionIterations;
            set => _erosionIterations = Mathf.Clamp(value, 100, 10000);
        }
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Mapping between terrain type and texture.
        /// </summary>
        [System.Serializable]
        public class TerrainTextureMapping {
            public string terrainType;
            public Texture2D diffuseTexture;
            public Texture2D normalMap;
            public Vector2 tileSize = new Vector2(20, 20);
            public float metallic = 0;
            public float smoothness = 0;
        }
        
        /// <summary>
        /// Mapping between terrain type and detail prototype.
        /// </summary>
        [System.Serializable]
        public class DetailPrototypeMapping {
            public string terrainType;
            public GameObject prefab;
            public Texture2D texture;
            public bool usePrototypeMesh;
            public float minWidth = 1;
            public float maxWidth = 2;
            public float minHeight = 1;
            public float maxHeight = 2;
            public Color healthyColor = Color.green;
            public Color dryColor = new Color(0.8f, 0.8f, 0.5f);
            public float noiseSpread = 0.5f;
            public float bendFactor = 0.5f;
            public float density = 0.5f;
        }
        
        /// <summary>
        /// Mapping between terrain type and tree prototype.
        /// </summary>
        [System.Serializable]
        public class TreePrototypeMapping {
            public string terrainType;
            public GameObject prefab;
            public float bendFactor = 0.5f;
            public float density = 0.5f;
            public float minScale = 0.75f;
            public float maxScale = 1.25f;
        }
        
        /// <summary>
        /// Terrain generation request data.
        /// </summary>
        [System.Serializable]
        public class TerrainGenerationRequest {
            public Vector3 size = new Vector3(500, 100, 500);
            public int resolution = 513;
            public Texture2D heightmapTexture;
            public AnalysisResults analysisResults;
            public bool generateWater = true;
            public float waterLevel = 0.3f;
            public bool applyErosion = true;
            public int erosionIterations = 1000;
        }
        
        /// <summary>
        /// Path data for terrain paths.
        /// </summary>
        [System.Serializable]
        public class PathData {
            public string pathType;
            public List<Vector2> points = new List<Vector2>();
            public float width = 5f;
            public float height = 0f;
            public float smoothing = 0.5f;
            public Material material;
        }
        
        /// <summary>
        /// Water body data.
        /// </summary>
        [System.Serializable]
        public class WaterBodyData {
            public string waterType;
            public List<Vector2> contourPoints = new List<Vector2>();
            public Rect boundingBox;
            public float depth = 1f;
            public Material material;
            public bool isFlowing = false;
            public Vector2 flowDirection = Vector2.zero;
            public float flowSpeed = 0.5f;
            public bool generateShore = true;
            public float shoreWidth = 2f;
        }
        
        #endregion
        
        #region Private Fields
        
        private TraversifyDebugger _debugger;
        private UnityEngine.Terrain _currentTerrain;
        private TerrainData _terrainData;
        private GameObject _terrainObject;
        private List<GameObject> _waterObjects = new List<GameObject>();
        private Dictionary<string, Texture2D> _alphaMaps = new Dictionary<string, Texture2D>();
        private Dictionary<string, float[,]> _detailMaps = new Dictionary<string, float[,]>();
        private List<PathData> _paths = new List<PathData>();
        private List<WaterBodyData> _waterBodies = new List<WaterBodyData>();
        private float[,] _heightMap;
        private bool _isGenerating = false;
        private Vector2 _lastImageSize;
        private Dictionary<string, int> _terrainTypeToTextureIndex = new Dictionary<string, int>();
        private Dictionary<string, int> _terrainTypeToDetailIndex = new Dictionary<string, int>();
        private Dictionary<string, int> _terrainTypeToTreeIndex = new Dictionary<string, int>();
        
        // Performance tracking
        private Dictionary<string, float> _performanceMetrics = new Dictionary<string, float>();
        
        // Job system
        private JobHandle _currentJobHandle;
        private NativeArray<float> _heightMapNative;
        
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
                
                Log("TerrainGenerator initialized successfully", LogCategory.Terrain);
                return true;
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to initialize TerrainGenerator: {ex.Message}");
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
        
        /// <summary>
        /// Apply configuration from object.
        /// </summary>
        private void ApplyConfiguration(object config) {
            // Handle dictionary config
            if (config is Dictionary<string, object> configDict) {
                // Extract terrain size
                if (configDict.TryGetValue("terrainSize", out object sizeObj) && sizeObj is Vector3 size) {
                    _terrainSize = size;
                }
                
                // Extract heightmap resolution
                if (configDict.TryGetValue("heightmapResolution", out object resolutionObj) && resolutionObj is int resolution) {
                    heightmapResolution = resolution;
                }
                
                // Extract detail resolution
                if (configDict.TryGetValue("detailResolution", out object detailResObj) && detailResObj is int detailResolution) {
                    _detailResolution = detailResolution;
                }
                
                // Extract height multiplier
                if (configDict.TryGetValue("heightMapMultiplier", out object heightMultObj) && heightMultObj is float heightMult) {
                    _heightMapMultiplier = heightMult;
                }
                
                // Extract water settings
                if (configDict.TryGetValue("generateWater", out object genWaterObj) && genWaterObj is bool genWater) {
                    _generateWater = genWater;
                }
                
                if (configDict.TryGetValue("waterLevel", out object waterLevelObj) && waterLevelObj is float waterLevel) {
                    _waterLevel = Mathf.Clamp01(waterLevel);
                }
                
                // Extract erosion settings
                if (configDict.TryGetValue("applyErosion", out object applyErosionObj) && applyErosionObj is bool applyErosion) {
                    _applyErosion = applyErosion;
                }
                
                if (configDict.TryGetValue("erosionIterations", out object erosionIterObj) && erosionIterObj is int erosionIter) {
                    _erosionIterations = erosionIter;
                }
            }
            // Handle TerrainGenerationRequest
            else if (config is TerrainGenerationRequest request) {
                _terrainSize = request.size;
                heightmapResolution = request.resolution;
                _generateWater = request.generateWater;
                _waterLevel = request.waterLevel;
                _applyErosion = request.applyErosion;
                _erosionIterations = request.erosionIterations;
            }
        }
        
        /// <summary>
        /// Initialize terrain data with current settings.
        /// </summary>
        private void InitializeTerrainData() {
            try {
                // Create new terrain data
                _terrainData = new TerrainData();
                
                // Apply settings
                _terrainData.heightmapResolution = _heightmapResolution;
                _terrainData.size = _terrainSize;
                _terrainData.SetDetailResolution(_detailResolution, 16);
                
                // Initialize textures
                SetupTerrainTextures();
                
                // Initialize detail prototypes
                SetupDetailPrototypes();
                
                // Initialize tree prototypes
                SetupTreePrototypes();
                
                Log($"Initialized terrain data: {_heightmapResolution}x{_heightmapResolution}, " +
                    $"Size: {_terrainSize.x}x{_terrainSize.y}x{_terrainSize.z}", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Failed to initialize terrain data: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Setup terrain textures from mappings.
        /// </summary>
        private void SetupTerrainTextures() {
            try {
                // Create default texture if none defined
                if (_terrainTextures.Count == 0) {
                    _terrainTextures.Add(new TerrainTextureMapping {
                        terrainType = "default",
                        diffuseTexture = CreateDefaultTexture(Color.gray),
                        tileSize = new Vector2(20, 20)
                    });
                }
                
                // Setup texture mapping
                _terrainTypeToTextureIndex.Clear();
                List<TerrainLayer> terrainLayers = new List<TerrainLayer>();
                
                for (int i = 0; i < _terrainTextures.Count; i++) {
                    var mapping = _terrainTextures[i];
                    
                    // Create terrain layer
                    TerrainLayer layer = new TerrainLayer();
                    
                    // Set properties
                    layer.diffuseTexture = mapping.diffuseTexture ?? CreateDefaultTexture(Color.gray);
                    layer.normalMapTexture = mapping.normalMap;
                    layer.tileSize = mapping.tileSize;
                    layer.metallic = mapping.metallic;
                    layer.smoothness = mapping.smoothness;
                    
                    // Add to list
                    terrainLayers.Add(layer);
                    
                    // Add to mapping
                    _terrainTypeToTextureIndex[mapping.terrainType.ToLower()] = i;
                }
                
                // Apply layers to terrain data
                _terrainData.terrainLayers = terrainLayers.ToArray();
                
                Log($"Setup {terrainLayers.Count} terrain textures", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Failed to setup terrain textures: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Setup detail prototypes from mappings.
        /// </summary>
        private void SetupDetailPrototypes() {
            try {
                // Skip if no detail prototypes
                if (_detailPrototypes.Count == 0) {
                    return;
                }
                
                // Setup mapping
                _terrainTypeToDetailIndex.Clear();
                List<DetailPrototype> detailPrototypes = new List<DetailPrototype>();
                
                for (int i = 0; i < _detailPrototypes.Count; i++) {
                    var mapping = _detailPrototypes[i];
                    
                    // Create detail prototype
                    DetailPrototype prototype = new DetailPrototype();
                    
                    // Set properties
                    prototype.usePrototypeMesh = mapping.usePrototypeMesh;
                    prototype.prototype = mapping.prefab;
                    prototype.prototypeTexture = mapping.texture;
                    prototype.minWidth = mapping.minWidth;
                    prototype.maxWidth = mapping.maxWidth;
                    prototype.minHeight = mapping.minHeight;
                    prototype.maxHeight = mapping.maxHeight;
                    prototype.healthyColor = mapping.healthyColor;
                    prototype.dryColor = mapping.dryColor;
                    prototype.noiseSpread = mapping.noiseSpread;
                    prototype.bendFactor = mapping.bendFactor;
                    
                    // Add to list
                    detailPrototypes.Add(prototype);
                    
                    // Add to mapping
                    _terrainTypeToDetailIndex[mapping.terrainType.ToLower()] = i;
                }
                
                // Apply prototypes to terrain data
                _terrainData.detailPrototypes = detailPrototypes.ToArray();
                
                Log($"Setup {detailPrototypes.Count} detail prototypes", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Failed to setup detail prototypes: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Setup tree prototypes from mappings.
        /// </summary>
        private void SetupTreePrototypes() {
            try {
                // Skip if no tree prototypes
                if (_treePrototypes.Count == 0) {
                    return;
                }
                
                // Setup mapping
                _terrainTypeToTreeIndex.Clear();
                List<TreePrototype> treePrototypes = new List<TreePrototype>();
                
                for (int i = 0; i < _treePrototypes.Count; i++) {
                    var mapping = _treePrototypes[i];
                    
                    // Create tree prototype
                    TreePrototype prototype = new TreePrototype();
                    
                    // Set properties
                    prototype.prefab = mapping.prefab;
                    prototype.bendFactor = mapping.bendFactor;
                    
                    // Add to list
                    treePrototypes.Add(prototype);
                    
                    // Add to mapping
                    _terrainTypeToTreeIndex[mapping.terrainType.ToLower()] = i;
                }
                
                // Apply prototypes to terrain data
                _terrainData.treePrototypes = treePrototypes.ToArray();
                
                Log($"Setup {treePrototypes.Count} tree prototypes", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Failed to setup tree prototypes: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Create a default texture with the specified color.
        /// </summary>
        private Texture2D CreateDefaultTexture(Color color) {
            Texture2D texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[128 * 128];
            
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = color;
            }
            
            texture.SetPixels(pixels);
            texture.Apply();
            
            return texture;
        }
        
        #endregion
        #region Terrain Generation API
        
        /// <summary>
        /// Generate terrain from analysis results.
        /// </summary>
        public IEnumerator GenerateTerrain(
            TerrainGenerationRequest request,
            System.Action<UnityEngine.Terrain> onComplete = null,
            System.Action<string> onError = null,
            System.Action<float, string> onProgress = null)
        {
            if (_isGenerating) {
                onError?.Invoke("Terrain generation already in progress");
                yield break;
            }
            
            if (request == null) {
                onError?.Invoke("Terrain generation request is null");
                yield break;
            }
            
            try {
                _isGenerating = true;
                _performanceMetrics.Clear();
                float startTime = Time.realtimeSinceStartup;
                
                // Apply request settings
                _terrainSize = request.size;
                heightmapResolution = request.resolution;
                _generateWater = request.generateWater;
                _waterLevel = request.waterLevel;
                _applyErosion = request.applyErosion;
                _erosionIterations = request.erosionIterations;
                
                // Step 1: Initialize terrain data
                onProgress?.Invoke(0.05f, "Initializing terrain data...");
                InitializeTerrainData();
                yield return null;
                
                // Step 2: Generate heightmap
                onProgress?.Invoke(0.1f, "Generating heightmap...");
                
                if (request.analysisResults != null) {
                    // Generate from analysis results
                    yield return StartCoroutine(GenerateHeightMapFromAnalysis(request.analysisResults));
                }
                else if (request.heightmapTexture != null) {
                    // Generate from texture
                    yield return StartCoroutine(ApplyHeightMapFromTexture(request.heightmapTexture));
                }
                else {
                    // Generate default
                    GenerateDefaultHeightMap();
                }
                
                yield return null;
                
                // Step 3: Apply height map to terrain
                onProgress?.Invoke(0.3f, "Applying heightmap to terrain...");
                ApplyHeightMapToTerrain();
                yield return null;
                
                // Step 4: Apply erosion if enabled
                if (_applyErosion) {
                    onProgress?.Invoke(0.4f, "Applying erosion simulation...");
                    yield return StartCoroutine(ApplyErosion());
                }
                
                // Step 5: Apply terrain textures
                onProgress?.Invoke(0.5f, "Applying terrain textures...");
                if (request.analysisResults != null) {
                    yield return StartCoroutine(GenerateTerrainTextures(request.analysisResults));
                }
                else {
                    GenerateDefaultTerrainTextures();
                }
                yield return null;
                
                // Step 6: Generate water bodies
                if (_generateWater) {
                    onProgress?.Invoke(0.7f, "Generating water bodies...");
                    if (request.analysisResults != null) {
                        yield return StartCoroutine(GenerateWaterBodies(request.analysisResults));
                    }
                    else {
                        GenerateDefaultWaterBody();
                    }
                    yield return null;
                }
                
                // Step 7: Generate terrain details
                onProgress?.Invoke(0.8f, "Generating terrain details...");
                if (request.analysisResults != null) {
                    yield return StartCoroutine(GenerateTerrainDetails(request.analysisResults));
                }
                yield return null;
                
                // Step 8: Create terrain object
                onProgress?.Invoke(0.9f, "Creating terrain object...");
                CreateTerrainObject();
                yield return null;
                
                // Complete
                float totalTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["TotalGenerationTime"] = totalTime;
                
                Log($"Terrain generation completed in {totalTime:F2} seconds", LogCategory.Terrain);
                onProgress?.Invoke(1.0f, "Terrain generation complete");
                onComplete?.Invoke(_currentTerrain);
            }
            catch (Exception ex) {
                LogError($"Error during terrain generation: {ex.Message}", LogCategory.Terrain);
                onError?.Invoke($"Terrain generation failed: {ex.Message}");
            }
            finally {
                _isGenerating = false;
            }
        }
        
        /// <summary>
        /// Generate terrain from analysis results.
        /// </summary>
        public UnityEngine.Terrain GenerateTerrainFromAnalysis(AnalysisResults analysisResults, Texture2D sourceTexture) {
            try {
                if (_isGenerating) {
                    LogError("Terrain generation already in progress", LogCategory.Terrain);
                    return null;
                }
                
                _isGenerating = true;
                float startTime = Time.realtimeSinceStartup;
                
                // Initialize terrain data
                InitializeTerrainData();
                
                // Generate height map
                if (analysisResults != null && analysisResults.heightMap != null) {
                    StartCoroutine(ApplyHeightMapFromTexture(analysisResults.heightMap));
                }
                else if (sourceTexture != null) {
                    StartCoroutine(GenerateHeightMapFromTexture(sourceTexture));
                }
                else {
                    GenerateDefaultHeightMap();
                }
                
                // Apply height map to terrain
                ApplyHeightMapToTerrain();
                
                // Create terrain object
                CreateTerrainObject();
                
                float totalTime = Time.realtimeSinceStartup - startTime;
                Log($"Quick terrain generation completed in {totalTime:F2} seconds", LogCategory.Terrain);
                
                _isGenerating = false;
                return _currentTerrain;
            }
            catch (Exception ex) {
                LogError($"Error during terrain generation: {ex.Message}", LogCategory.Terrain);
                _isGenerating = false;
                return null;
            }
        }
        
        /// <summary>
        /// Generate terrain from a height map texture.
        /// </summary>
        public UnityEngine.Terrain GenerateTerrainFromHeightMap(Texture2D heightMapTexture) {
            try {
                if (_isGenerating) {
                    LogError("Terrain generation already in progress", LogCategory.Terrain);
                    return null;
                }
                
                _isGenerating = true;
                float startTime = Time.realtimeSinceStartup;
                
                // Initialize terrain data
                InitializeTerrainData();
                
                // Generate height map
                StartCoroutine(ApplyHeightMapFromTexture(heightMapTexture));
                
                // Apply height map to terrain
                ApplyHeightMapToTerrain();
                
                // Create terrain object
                CreateTerrainObject();
                
                float totalTime = Time.realtimeSinceStartup - startTime;
                Log($"Terrain generation from height map completed in {totalTime:F2} seconds", LogCategory.Terrain);
                
                _isGenerating = false;
                return _currentTerrain;
            }
            catch (Exception ex) {
                LogError($"Error during terrain generation: {ex.Message}", LogCategory.Terrain);
                _isGenerating = false;
                return null;
            }
        }
        
        #endregion
        #region Height Map Generation
        
        /// <summary>
        /// Generate height map from analysis results.
        /// </summary>
        private IEnumerator GenerateHeightMapFromAnalysis(AnalysisResults analysisResults) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Use provided height map if available
                if (analysisResults.heightMap != null) {
                    Log("Using height map from analysis results", LogCategory.Terrain);
                    yield return StartCoroutine(ApplyHeightMapFromTexture(analysisResults.heightMap));
                    yield break;
                }
                
                // Initialize height map
                _heightMap = new float[_heightmapResolution, _heightmapResolution];
                
                // Prepare for conversion
                if (analysisResults.sourceImage != null) {
                    _lastImageSize = new Vector2(analysisResults.sourceImage.width, analysisResults.sourceImage.height);
                }
                
                // Generate base height map
                GenerateBaseHeightMap();
                
                // Apply terrain objects influence
                Log("Applying terrain features to height map", LogCategory.Terrain);
                yield return null;
                
                if (analysisResults.terrainFeatures != null && analysisResults.terrainFeatures.Count > 0) {
                    foreach (var feature in analysisResults.terrainFeatures) {
                        ApplyTerrainFeatureInfluence(feature);
                        
                        // Yield periodically
                        if (Time.realtimeSinceStartup - startTime > 0.1f) {
                            yield return null;
                            startTime = Time.realtimeSinceStartup;
                        }
                    }
                }
                else if (analysisResults.terrainObjects != null && analysisResults.terrainObjects.Count > 0) {
                    foreach (var terrainObj in analysisResults.terrainObjects) {
                        ApplyTerrainObjectInfluence(terrainObj);
                        
                        // Yield periodically
                        if (Time.realtimeSinceStartup - startTime > 0.1f) {
                            yield return null;
                            startTime = Time.realtimeSinceStartup;
                        }
                    }
                }
                
                // Apply multi-scale noise
                Log("Applying multi-scale noise to height map", LogCategory.Terrain);
                ApplyMultiScaleNoise();
                yield return null;
                
                // Smooth height map
                Log("Smoothing height map", LogCategory.Terrain);
                SmoothHeightMap();
                yield return null;
                
                // Normalize height map
                // Normalize height map
                NormalizeHeightMap();
                
                float heightmapTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["HeightMapGenerationTime"] = heightmapTime;
                
                Log($"Height map generation completed in {heightmapTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating height map from analysis: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Generate height map from texture.
        /// </summary>
        private IEnumerator GenerateHeightMapFromTexture(Texture2D texture) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Initialize height map
                _heightMap = new float[_heightmapResolution, _heightmapResolution];
                _lastImageSize = new Vector2(texture.width, texture.height);
                
                // Extract height data from texture
                if (_useMultithreading) {
                    yield return StartCoroutine(ExtractHeightDataMultithreaded(texture));
                }
                else {
                    ExtractHeightData(texture);
                    yield return null;
                }
                
                // Apply multi-scale noise
                ApplyMultiScaleNoise();
                yield return null;
                
                // Smooth height map
                SmoothHeightMap();
                yield return null;
                
                // Normalize height map
                NormalizeHeightMap();
                
                float heightmapTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["HeightMapGenerationTime"] = heightmapTime;
                
                Log($"Height map generation from texture completed in {heightmapTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating height map from texture: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Apply height map from texture.
        /// </summary>
        private IEnumerator ApplyHeightMapFromTexture(Texture2D heightMapTexture) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Create height map array
                _heightMap = new float[_heightmapResolution, _heightmapResolution];
                
                // Get pixels
                Color[] pixels = heightMapTexture.GetPixels();
                int texWidth = heightMapTexture.width;
                int texHeight = heightMapTexture.height;
                
                // Convert to height map
                if (_useMultithreading) {
                    yield return StartCoroutine(ConvertPixelsToHeightMapMultithreaded(pixels, texWidth, texHeight));
                }
                else {
                    ConvertPixelsToHeightMap(pixels, texWidth, texHeight);
                    yield return null;
                }
                
                // Apply scale
                ApplyHeightMapScale();
                
                float applyTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["ApplyHeightMapTime"] = applyTime;
                
                Log($"Applied height map from texture in {applyTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error applying height map from texture: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Generate a default height map.
        /// </summary>
        private void GenerateDefaultHeightMap() {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Initialize height map
                _heightMap = new float[_heightmapResolution, _heightmapResolution];
                
                // Generate base terrain
                GenerateBaseHeightMap();
                
                // Apply multi-scale noise
                ApplyMultiScaleNoise();
                
                // Smooth height map
                SmoothHeightMap();
                
                // Normalize height map
                NormalizeHeightMap();
                
                float heightmapTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["HeightMapGenerationTime"] = heightmapTime;
                
                Log($"Default height map generation completed in {heightmapTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating default height map: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Generate base height map.
        /// </summary>
        private void GenerateBaseHeightMap() {
            // Generate basic height values
            float scale = 20f;
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    float xCoord = x / (float)_heightmapResolution * scale;
                    float yCoord = y / (float)_heightmapResolution * scale;
                    
                    // Use Perlin noise for base terrain
                    float noise = Mathf.PerlinNoise(xCoord, yCoord);
                    
                    // Apply exponential curve to create more plains and fewer peaks
                    noise = Mathf.Pow(noise, 2f);
                    
                    _heightMap[x, y] = noise;
                }
            }
        }
        
        /// <summary>
        /// Extract height data from texture.
        /// </summary>
        private void ExtractHeightData(Texture2D texture) {
            // Get pixels
            Color[] pixels = texture.GetPixels();
            int texWidth = texture.width;
            int texHeight = texture.height;
            
            // Convert to height map
            ConvertPixelsToHeightMap(pixels, texWidth, texHeight);
        }
        
        /// <summary>
        /// Extract height data using multi-threading.
        /// </summary>
        private IEnumerator ExtractHeightDataMultithreaded(Texture2D texture) {
            // Get pixels
            Color[] pixels = texture.GetPixels();
            int texWidth = texture.width;
            int texHeight = texture.height;
            
            // Use job system for parallel processing
            yield return StartCoroutine(ConvertPixelsToHeightMapMultithreaded(pixels, texWidth, texHeight));
        }
        
        /// <summary>
        /// Convert pixels to height map.
        /// </summary>
        private void ConvertPixelsToHeightMap(Color[] pixels, int texWidth, int texHeight) {
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    // Calculate source pixel coordinates
                    int sourceX = Mathf.FloorToInt(x * texWidth / (float)_heightmapResolution);
                    int sourceY = Mathf.FloorToInt(y * texHeight / (float)_heightmapResolution);
                    
                    // Get pixel
                    int pixelIndex = sourceY * texWidth + sourceX;
                    if (pixelIndex < pixels.Length) {
                        Color pixel = pixels[pixelIndex];
                        
                        // Extract height from grayscale
                        float height = (pixel.r + pixel.g + pixel.b) / 3f;
                        
                        // Apply height to map
                        _heightMap[x, y] = height;
                    }
                }
            }
        }
        
        /// <summary>
        /// Convert pixels to height map using multi-threading.
        /// </summary>
        private IEnumerator ConvertPixelsToHeightMapMultithreaded(Color[] pixels, int texWidth, int texHeight) {
            // Define the job
            int totalPixels = _heightmapResolution * _heightmapResolution;
            
            // Create native arrays for job system
            NativeArray<Color> pixelsNative = new NativeArray<Color>(pixels, Allocator.TempJob);
            _heightMapNative = new NativeArray<float>(totalPixels, Allocator.TempJob);
            
            // Create the job
            PixelsToHeightMapJob job = new PixelsToHeightMapJob {
                pixels = pixelsNative,
                heightMap = _heightMapNative,
                heightmapResolution = _heightmapResolution,
                textureWidth = texWidth,
                textureHeight = texHeight
            };
            
            // Schedule the job
            _currentJobHandle = job.Schedule(totalPixels, 64);
            
            // Wait for job completion
            while (!_currentJobHandle.IsCompleted) {
                yield return null;
            }
            
            // Complete the job
            _currentJobHandle.Complete();
            
            // Copy data back to height map
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    int index = y * _heightmapResolution + x;
                    _heightMap[x, y] = _heightMapNative[index];
                }
            }
            
            // Dispose native arrays
            pixelsNative.Dispose();
            _heightMapNative.Dispose();
        }
        
        /// <summary>
        /// Apply terrain feature influence to height map.
        /// </summary>
        private void ApplyTerrainFeatureInfluence(TerrainFeature feature) {
            // Determine feature strength and profile
            float strength = GetFeatureStrength(feature.featureType);
            float radius = Mathf.Max(feature.bounds.width, feature.bounds.height) / 2f;
            Vector2 center = new Vector2(feature.bounds.x + feature.bounds.width / 2f, feature.bounds.y + feature.bounds.height / 2f);
            
            // Convert to terrain space
            Vector2 terrainCenter = ConvertToTerrainSpace(center);
            float terrainRadius = ConvertToTerrainSpace(radius);
            
            // Apply influence based on feature type
            if (IsWaterType(feature.featureType)) {
                ApplyWaterFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            else if (IsMountainType(feature.featureType)) {
                ApplyMountainFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            else if (IsValleyType(feature.featureType)) {
                ApplyValleyFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            else {
                ApplyGenericTerrainFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            
            // Apply contour points if available
            if (feature.contourPoints != null && feature.contourPoints.Count > 0) {
                ApplyContourInfluence(feature.contourPoints, feature.featureType);
            }
        }
        
        /// <summary>
        /// Apply terrain object influence to height map.
        /// </summary>
        private void ApplyTerrainObjectInfluence(DetectedObject terrainObj) {
            // Skip if not terrain
            if (!terrainObj.isTerrain) return;
            
            // Determine feature strength and profile
            float strength = GetFeatureStrength(terrainObj.className);
            float radius = Mathf.Max(terrainObj.boundingBox.width, terrainObj.boundingBox.height) / 2f;
            Vector2 center = new Vector2(
                terrainObj.boundingBox.x + terrainObj.boundingBox.width / 2f,
                terrainObj.boundingBox.y + terrainObj.boundingBox.height / 2f
            );
            
            // Convert to terrain space
            Vector2 terrainCenter = ConvertToTerrainSpace(center);
            float terrainRadius = ConvertToTerrainSpace(radius);
            
            // Apply influence based on terrain type
            if (IsWaterType(terrainObj.className)) {
                ApplyWaterFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            else if (IsMountainType(terrainObj.className)) {
                ApplyMountainFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            else if (IsValleyType(terrainObj.className)) {
                ApplyValleyFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            else {
                ApplyGenericTerrainFeatureInfluence(terrainCenter, terrainRadius, strength);
            }
            
            // Apply segments if available
            if (terrainObj.segments != null && terrainObj.segments.Count > 0) {
                foreach (var segment in terrainObj.segments) {
                    if (segment.contourPoints != null && segment.contourPoints.Count > 0) {
                        ApplyContourInfluence(segment.contourPoints, terrainObj.className);
                    }
                }
            }
        }
        /// <summary>
        /// Apply water feature influence to height map.
        /// </summary>
        private void ApplyWaterFeatureInfluence(Vector2 center, float radius, float strength) {
            // Calculate region to affect
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply depression
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor (higher at center, lower at edges)
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        influence = Mathf.Pow(influence, 2f); // Sharper falloff
                        
                        // Water features create depressions
                        _heightMap[x, y] = Mathf.Min(_heightMap[x, y], 
                            _heightMap[x, y] - influence * strength);
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply mountain feature influence to height map.
        /// </summary>
        private void ApplyMountainFeatureInfluence(Vector2 center, float radius, float strength) {
            // Calculate region to affect
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply elevation
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor (higher at center, lower at edges)
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        influence = Mathf.Pow(influence, 3f); // Sharper falloff for mountains
                        
                        // Apply noise to create more natural peaks
                        float noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.2f + 0.9f;
                        
                        // Mountain features create elevations
                        _heightMap[x, y] = Mathf.Max(_heightMap[x, y], 
                            _heightMap[x, y] + influence * strength * noise);
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply valley feature influence to height map.
        /// </summary>
        private void ApplyValleyFeatureInfluence(Vector2 center, float radius, float strength) {
            // Calculate region to affect
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply valley
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor - V-shaped valleys
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        influence = Mathf.Pow(influence, 1.5f); // More gradual falloff
                        
                        // Valley features create depressions, but more gradual than water
                        _heightMap[x, y] = Mathf.Lerp(_heightMap[x, y], 
                            _heightMap[x, y] * (1f - influence * strength), influence);
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply generic terrain feature influence to height map.
        /// </summary>
        private void ApplyGenericTerrainFeatureInfluence(Vector2 center, float radius, float strength) {
            // Calculate region to affect
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(_heightmapResolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply generic influence
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        
                        // Generic features just blend toward a target height
                        float targetHeight = 0.5f; // Middle height
                        _heightMap[x, y] = Mathf.Lerp(_heightMap[x, y], targetHeight, influence * strength);
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply contour influence to height map.
        /// </summary>
        private void ApplyContourInfluence(List<Vector2> contourPoints, string featureType) {
            if (contourPoints.Count < 3) return;
            
            // Convert contour points to terrain space
            List<Vector2> terrainContour = new List<Vector2>();
            foreach (var point in contourPoints) {
                terrainContour.Add(ConvertToTerrainSpace(point));
            }
            
            // Determine feature properties
            float strength = GetFeatureStrength(featureType);
            bool isDepression = IsWaterType(featureType) || IsValleyType(featureType);
            bool isElevation = IsMountainType(featureType);
            
            // Calculate bounding box for optimization
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            
            foreach (var point in terrainContour) {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            
            // Add padding
            float padding = 10f;
            minX = Mathf.Max(0, minX - padding);
            minY = Mathf.Max(0, minY - padding);
            maxX = Mathf.Min(_heightmapResolution - 1, maxX + padding);
            maxY = Mathf.Min(_heightmapResolution - 1, maxY + padding);
            
            // Apply influence only within bounding box
            for (int y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++) {
                for (int x = Mathf.FloorToInt(minX); x <= Mathf.CeilToInt(maxX); x++) {
                    if (PointInPolygon(new Vector2(x, y), terrainContour)) {
                        // Point is inside contour
                        
                        // Calculate distance to edge for falloff
                        float edgeDistance = DistanceToPolygonEdge(new Vector2(x, y), terrainContour);
                        float normalizedDistance = Mathf.Clamp01(edgeDistance / 10f); // Adjust falloff distance
                        
                        // Apply based on feature type
                        if (isDepression) {
                            // Water/valley features create depressions
                            float targetHeight = _waterLevel - 0.1f;
                            _heightMap[x, y] = Mathf.Lerp(_heightMap[x, y], targetHeight, strength * (1 - normalizedDistance));
                        }
                        else if (isElevation) {
                            // Mountain features create elevations
                            float noise = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.3f + 0.7f;
                            float elevationFactor = strength * (1 - normalizedDistance) * noise;
                            _heightMap[x, y] = Mathf.Lerp(_heightMap[x, y], _heightMap[x, y] * (1 + elevationFactor), elevationFactor);
                        }
                        else {
                            // Generic features blend toward a target height
                            float targetHeight = 0.5f;
                            _heightMap[x, y] = Mathf.Lerp(_heightMap[x, y], targetHeight, strength * (1 - normalizedDistance));
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply multi-scale noise to height map.
        /// </summary>
        private void ApplyMultiScaleNoise() {
            // Apply multiple octaves of noise
            float[,] noiseLayer = new float[_heightmapResolution, _heightmapResolution];
            
            // Define noise parameters
            float[] frequencies = { 0.01f, 0.02f, 0.04f, 0.08f };
            float[] amplitudes = { 0.4f, 0.2f, 0.1f, 0.05f };
            
            // Generate noise layers
            for (int octave = 0; octave < frequencies.Length; octave++) {
                float frequency = frequencies[octave];
                float amplitude = amplitudes[octave];
                
                for (int y = 0; y < _heightmapResolution; y++) {
                    for (int x = 0; x < _heightmapResolution; x++) {
                        float noiseValue = Mathf.PerlinNoise(x * frequency, y * frequency);
                        noiseLayer[x, y] += noiseValue * amplitude;
                    }
                }
            }
            
            // Apply noise layer
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    _heightMap[x, y] = Mathf.Lerp(_heightMap[x, y], _heightMap[x, y] * noiseLayer[x, y], 0.3f);
                }
            }
        }
        
        /// <summary>
        /// Smooth height map.
        /// </summary>
        private void SmoothHeightMap() {
            float[,] smoothed = new float[_heightmapResolution, _heightmapResolution];
            int kernelSize = 3;
            
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    float sum = 0f;
                    int count = 0;
                    
                    // Apply smoothing kernel
                    for (int ky = -kernelSize; ky <= kernelSize; ky++) {
                        for (int kx = -kernelSize; kx <= kernelSize; kx++) {
                            int sampleX = x + kx;
                            int sampleY = y + ky;
                            
                            if (sampleX >= 0 && sampleX < _heightmapResolution && sampleY >= 0 && sampleY < _heightmapResolution) {
                                // Weight samples by distance (gaussian-like)
                                float weight = 1f / (1f + Mathf.Sqrt(kx * kx + ky * ky));
                                sum += _heightMap[sampleX, sampleY] * weight;
                                count += 1;
                            }
                        }
                    }
                    
                    if (count > 0) {
                        smoothed[x, y] = sum / count;
                    }
                }
            }
            
            // Replace with smoothed values
            _heightMap = smoothed;
        }
        
        /// <summary>
        /// Normalize height map values to 0-1 range.
        /// </summary>
        private void NormalizeHeightMap() {
            // Find min/max values
            float min = float.MaxValue;
            float max = float.MinValue;
            
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    min = Mathf.Min(min, _heightMap[x, y]);
                    max = Mathf.Max(max, _heightMap[x, y]);
                }
            }
            
            // Skip if range is too small
            if (Mathf.Approximately(max, min)) return;
            
            // Normalize
            float range = max - min;
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    _heightMap[x, y] = (_heightMap[x, y] - min) / range;
                }
            }
        }
        
        /// <summary>
        /// Apply scale to height map.
        /// </summary>
        private void ApplyHeightMapScale() {
            // Apply multiplier
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    _heightMap[x, y] *= _heightMapMultiplier;
                }
            }
        }
        
        /// <summary>
        /// Apply height map to terrain.
        /// </summary>
        private void ApplyHeightMapToTerrain() {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                if (_terrainData == null) {
                    LogError("Terrain data is null, cannot apply height map", LogCategory.Terrain);
                    return;
                }
                
                // Apply height map
                _terrainData.SetHeights(0, 0, _heightMap);
                
                float applyTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["ApplyHeightMapToTerrainTime"] = applyTime;
                
                Log($"Applied height map to terrain in {applyTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error applying height map to terrain: {ex.Message}", LogCategory.Terrain);
            }
        }
        #endregion
        
        #region Erosion Simulation
        
        /// <summary>
        /// Apply hydraulic erosion to the terrain.
        /// </summary>
        private IEnumerator ApplyErosion() {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Get current heights
                float[,] heights = _terrainData.GetHeights(0, 0, _heightmapResolution, _heightmapResolution);
                
                // Apply erosion
                if (_useMultithreading) {
                    yield return StartCoroutine(ApplyErosionMultithreaded(heights));
                }
                else {
                    ApplyErosionSimulation(heights);
                    yield return null;
                }
                
                // Set back to terrain
                _terrainData.SetHeights(0, 0, heights);
                
                float erosionTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["ErosionTime"] = erosionTime;
                
                Log($"Applied erosion in {erosionTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error applying erosion: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Apply erosion simulation to heights.
        /// </summary>
        private void ApplyErosionSimulation(float[,] heights) {
            // Erosion parameters
            float inertia = 0.05f;
            float sedimentCapacityFactor = 4f;
            float minSedimentCapacity = 0.01f;
            float depositSpeed = 0.3f;
            float erodeSpeed = 0.3f;
            
            // Apply erosion iterations
            for (int iter = 0; iter < _erosionIterations; iter++) {
                // Random drop position
                int posX = UnityEngine.Random.Range(0, _heightmapResolution - 1);
                int posY = UnityEngine.Random.Range(0, _heightmapResolution - 1);
                
                // Droplet properties
                float dirX = 0f;
                float dirY = 0f;
                float speed = 1f;
                float water = 1f;
                float sediment = 0f;
                
                for (int lifetime = 0; lifetime < 30; lifetime++) {
                    // Get droplet position as integer and fractional parts
                    int nodeX = Mathf.FloorToInt(posX);
                    int nodeY = Mathf.FloorToInt(posY);
                    float fracX = posX - nodeX;
                    float fracY = posY - nodeY;
                    
                    // Calculate droplet's height and direction of flow
                    float heightNW = heights[nodeX, nodeY];
                    float heightNE = heights[nodeX + 1, nodeY];
                    float heightSW = heights[nodeX, nodeY + 1];
                    float heightSE = heights[nodeX + 1, nodeY + 1];
                    
                    // Calculate height at droplet position using bilinear interpolation
                    float height = heightNW * (1 - fracX) * (1 - fracY) +
                                  heightNE * fracX * (1 - fracY) +
                                  heightSW * (1 - fracX) * fracY +
                                  heightSE * fracX * fracY;
                    
                    // Calculate gradient
                    float gradientX = (heightNE - heightNW) * (1 - fracY) + (heightSE - heightSW) * fracY;
                    float gradientY = (heightSW - heightNW) * (1 - fracX) + (heightSE - heightNE) * fracX;
                    
                    // Update droplet direction
                    dirX = dirX * inertia - gradientX * (1 - inertia);
                    dirY = dirY * inertia - gradientY * (1 - inertia);
                    
                    // Normalize direction
                    float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                    if (len != 0) {
                        dirX /= len;
                        dirY /= len;
                    }
                    
                    // Update position
                    posX += dirX;
                    posY += dirY;
                    
                    // Stop if droplet has flowed off map
                    if (posX < 0 || posX >= _heightmapResolution - 1 || posY < 0 || posY >= _heightmapResolution - 1) {
                        break;
                    }
                    
                    // Get new height and calculate height difference
                    nodeX = Mathf.FloorToInt(posX);
                    nodeY = Mathf.FloorToInt(posY);
                    fracX = posX - nodeX;
                    fracY = posY - nodeY;
                    
                    heightNW = heights[nodeX, nodeY];
                    heightNE = heights[nodeX + 1, nodeY];
                    heightSW = heights[nodeX, nodeY + 1];
                    heightSE = heights[nodeX + 1, nodeY + 1];
                    
                    float newHeight = heightNW * (1 - fracX) * (1 - fracY) +
                                     heightNE * fracX * (1 - fracY) +
                                     heightSW * (1 - fracX) * fracY +
                                     heightSE * fracX * fracY;
                    
                    float heightDiff = newHeight - height;
                    
                    // Calculate sediment capacity
                    float sedimentCapacity = Mathf.Max(minSedimentCapacity, sedimentCapacityFactor * heightDiff);
                    
                    // If flowing uphill, deposit sediment
                    if (heightDiff > 0) {
                        float depositAmount = Mathf.Min(sediment, heightDiff);
                        sediment -= depositAmount;
                        
                        // Distribute to nodes
                        heights[nodeX, nodeY] += depositAmount * (1 - fracX) * (1 - fracY);
                        heights[nodeX + 1, nodeY] += depositAmount * fracX * (1 - fracY);
                        heights[nodeX, nodeY + 1] += depositAmount * (1 - fracX) * fracY;
                        heights[nodeX + 1, nodeY + 1] += depositAmount * fracX * fracY;
                    }
                    else {
                        // Erode and carry sediment
                        float erosionAmount = Mathf.Min((sedimentCapacity - sediment) * erodeSpeed, -heightDiff);
                        
                        heights[nodeX, nodeY] -= erosionAmount * (1 - fracX) * (1 - fracY);
                        heights[nodeX + 1, nodeY] -= erosionAmount * fracX * (1 - fracY);
                        heights[nodeX, nodeY + 1] -= erosionAmount * (1 - fracX) * fracY;
                        heights[nodeX + 1, nodeY + 1] -= erosionAmount * fracX * fracY;
                        
                        sediment += erosionAmount;
                    }
                    
                    // Update water content
                    water = water * 0.99f;
                    if (water <= 0.01f) break;
                }
            }
        }
        
        /// <summary>
        /// Apply erosion using multi-threading.
        /// </summary>
        private IEnumerator ApplyErosionMultithreaded(float[,] heights) {
            // Convert to flat array for job system
            float[] flatHeights = new float[_heightmapResolution * _heightmapResolution];
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    flatHeights[y * _heightmapResolution + x] = heights[x, y];
                }
            }
            
            // Create native arrays for job system
            NativeArray<float> heightsNative = new NativeArray<float>(flatHeights, Allocator.TempJob);
            NativeArray<int> randomIndicesNative = new NativeArray<int>(_erosionIterations * 2, Allocator.TempJob);
            
            // Fill random indices
            for (int i = 0; i < _erosionIterations; i++) {
                randomIndicesNative[i * 2] = UnityEngine.Random.Range(0, _heightmapResolution - 1);
                randomIndicesNative[i * 2 + 1] = UnityEngine.Random.Range(0, _heightmapResolution - 1);
            }
            
            // Create the job
            ErosionJob job = new ErosionJob {
                heights = heightsNative,
                randomIndices = randomIndicesNative,
                resolution = _heightmapResolution,
                erosionStrength = _erosionStrength,
                iterations = _erosionIterations
            };
            
            // Schedule the job
            _currentJobHandle = job.Schedule();
            
            // Wait for job completion
            while (!_currentJobHandle.IsCompleted) {
                yield return null;
            }
            
            // Complete the job
            _currentJobHandle.Complete();
            
            // Copy data back to heights
            for (int y = 0; y < _heightmapResolution; y++) {
                for (int x = 0; x < _heightmapResolution; x++) {
                    heights[x, y] = heightsNative[y * _heightmapResolution + x];
                }
            }
            
            // Dispose native arrays
            heightsNative.Dispose();
            randomIndicesNative.Dispose();
        }
        
        #endregion
        
        #region Terrain Textures
        
        /// <summary>
        /// Generate terrain textures from analysis results.
        /// </summary>
        private IEnumerator GenerateTerrainTextures(AnalysisResults analysisResults) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Prepare alpha maps
                int alphaMapResolution = _terrainData.alphamapResolution;
                float[,,] alphaMap = new float[alphaMapResolution, alphaMapResolution, _terrainData.terrainLayers.Length];
                
                // Process terrain features
                if (analysisResults.terrainFeatures != null && analysisResults.terrainFeatures.Count > 0) {
                    foreach (var feature in analysisResults.terrainFeatures) {
                        ApplyTerrainFeatureTexture(feature, alphaMap, alphaMapResolution);
                        yield return null;
                    }
                }
                // Or process terrain objects
                else if (analysisResults.terrainObjects != null && analysisResults.terrainObjects.Count > 0) {
                    foreach (var terrainObj in analysisResults.terrainObjects) {
                        ApplyTerrainObjectTexture(terrainObj, alphaMap, alphaMapResolution);
                        yield return null;
                    }
                }
                
                // Apply height-based texturing
                ApplyHeightBasedTexturing(alphaMap, alphaMapResolution);
                yield return null;
                
                // Apply slope-based texturing
                ApplySlopeBasedTexturing(alphaMap, alphaMapResolution);
                yield return null;
                
                // Normalize alpha maps
                NormalizeAlphaMaps(alphaMap, alphaMapResolution);
                
                // Apply to terrain
                _terrainData.SetAlphamaps(0, 0, alphaMap);
                
                float textureTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["TextureGenerationTime"] = textureTime;
                
                Log($"Generated terrain textures in {textureTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating terrain textures: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Generate default terrain textures.
        /// </summary>
        private void GenerateDefaultTerrainTextures() {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Prepare alpha maps
                int alphaMapResolution = _terrainData.alphamapResolution;
                float[,,] alphaMap = new float[alphaMapResolution, alphaMapResolution, _terrainData.terrainLayers.Length];
                
                // Apply height-based texturing
                ApplyHeightBasedTexturing(alphaMap, alphaMapResolution);
                
                // Apply slope-based texturing
                ApplySlopeBasedTexturing(alphaMap, alphaMapResolution);
                
                // Normalize alpha maps
                NormalizeAlphaMaps(alphaMap, alphaMapResolution);
                
                // Apply to terrain
                _terrainData.SetAlphamaps(0, 0, alphaMap);
                
                float textureTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["TextureGenerationTime"] = textureTime;
                
                Log($"Generated default terrain textures in {textureTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating default terrain textures: {ex.Message}", LogCategory.Terrain);
            }
        }
        /// <summary>
        /// Apply terrain feature texture.
        /// </summary>
        private void ApplyTerrainFeatureTexture(TerrainFeature feature, float[,,] alphaMap, int resolution) {
            // Get texture index for this terrain type
            int textureIndex = GetTextureIndexForTerrainType(feature.featureType);
            if (textureIndex < 0) return;
            
            // Calculate region to affect
            Vector2 center = ConvertToTerrainSpace(new Vector2(
                feature.bounds.x + feature.bounds.width / 2f,
                feature.bounds.y + feature.bounds.height / 2f
            ));
            float radius = ConvertToTerrainSpace(Mathf.Max(feature.bounds.width, feature.bounds.height) / 2f);
            
            // Scale to alpha map resolution
            center.x = center.x * resolution / _heightmapResolution;
            center.y = center.y * resolution / _heightmapResolution;
            radius = radius * resolution / _heightmapResolution;
            
            // Apply texture
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply influence
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        influence = Mathf.Pow(influence, 2f); // Sharper falloff
                        
                        // Apply texture weight
                        alphaMap[y, x, textureIndex] += influence;
                    }
                }
            }
            
            // Apply contour if available
            if (feature.contourPoints != null && feature.contourPoints.Count > 0) {
                // Convert contour points to alpha map space
                List<Vector2> alphaContour = new List<Vector2>();
                foreach (var point in feature.contourPoints) {
                    Vector2 terrainPoint = ConvertToTerrainSpace(point);
                    alphaContour.Add(new Vector2(
                        terrainPoint.x * resolution / _heightmapResolution,
                        terrainPoint.y * resolution / _heightmapResolution
                    ));
                }
                
                // Calculate bounding box for optimization
                float minContourX = float.MaxValue, minContourY = float.MaxValue;
                float maxContourX = float.MinValue, maxContourY = float.MinValue;
                
                foreach (var point in alphaContour) {
                    minContourX = Mathf.Min(minContourX, point.x);
                    minContourY = Mathf.Min(minContourY, point.y);
                    maxContourX = Mathf.Max(maxContourX, point.x);
                    maxContourY = Mathf.Max(maxContourY, point.y);
                }
                
                // Add padding
                float padding = 2f;
                minContourX = Mathf.Max(0, minContourX - padding);
                minContourY = Mathf.Max(0, minContourY - padding);
                maxContourX = Mathf.Min(resolution - 1, maxContourX + padding);
                maxContourY = Mathf.Min(resolution - 1, maxContourY + padding);
                
                // Apply texture within contour
                for (int y = Mathf.FloorToInt(minContourY); y <= Mathf.CeilToInt(maxContourY); y++) {
                    for (int x = Mathf.FloorToInt(minContourX); x <= Mathf.CeilToInt(maxContourX); x++) {
                        if (PointInPolygon(new Vector2(x, y), alphaContour)) {
                            // Point is inside contour
                            
                            // Calculate distance to edge for falloff
                            float edgeDistance = DistanceToPolygonEdge(new Vector2(x, y), alphaContour);
                            float normalizedDistance = Mathf.Clamp01(edgeDistance / 5f); // Adjust falloff distance
                            
                            // Apply texture with falloff
                            alphaMap[y, x, textureIndex] += 1f - normalizedDistance;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply terrain object texture.
        /// </summary>
        private void ApplyTerrainObjectTexture(DetectedObject terrainObj, float[,,] alphaMap, int resolution) {
            // Skip if not terrain
            if (!terrainObj.isTerrain) return;
            
            // Get texture index for this terrain type
            int textureIndex = GetTextureIndexForTerrainType(terrainObj.className);
            if (textureIndex < 0) return;
            
            // Calculate region to affect
            Vector2 center = ConvertToTerrainSpace(new Vector2(
                terrainObj.boundingBox.x + terrainObj.boundingBox.width / 2f,
                terrainObj.boundingBox.y + terrainObj.boundingBox.height / 2f
            ));
            float radius = ConvertToTerrainSpace(Mathf.Max(terrainObj.boundingBox.width, terrainObj.boundingBox.height) / 2f);
            
            // Scale to alpha map resolution
            center.x = center.x * resolution / _heightmapResolution;
            center.y = center.y * resolution / _heightmapResolution;
            radius = radius * resolution / _heightmapResolution;
            
            // Apply texture
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply influence
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        influence = Mathf.Pow(influence, 2f); // Sharper falloff
                        
                        // Apply texture weight
                        alphaMap[y, x, textureIndex] += influence;
                    }
                }
            }
            
            // Apply segments if available
            if (terrainObj.segments != null && terrainObj.segments.Count > 0) {
                foreach (var segment in terrainObj.segments) {
                    if (segment.contourPoints != null && segment.contourPoints.Count > 0) {
                        // Convert contour points to alpha map space
                        List<Vector2> alphaContour = new List<Vector2>();
                        foreach (var point in segment.contourPoints) {
                            Vector2 terrainPoint = ConvertToTerrainSpace(point);
                            alphaContour.Add(new Vector2(
                                terrainPoint.x * resolution / _heightmapResolution,
                                terrainPoint.y * resolution / _heightmapResolution
                            ));
                        }
                        
                        // Apply texture within contour
                        ApplyTextureToContour(alphaMap, resolution, alphaContour, textureIndex);
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply texture to contour.
        /// </summary>
        private void ApplyTextureToContour(float[,,] alphaMap, int resolution, List<Vector2> contour, int textureIndex) {
            if (contour.Count < 3) return;
            
            // Calculate bounding box for optimization
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            
            foreach (var point in contour) {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            
            // Add padding
            float padding = 2f;
            minX = Mathf.Max(0, minX - padding);
            minY = Mathf.Max(0, minY - padding);
            maxX = Mathf.Min(resolution - 1, maxX + padding);
            maxY = Mathf.Min(resolution - 1, maxY + padding);
            
            // Apply texture within contour
            for (int y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++) {
                for (int x = Mathf.FloorToInt(minX); x <= Mathf.CeilToInt(maxX); x++) {
                    if (PointInPolygon(new Vector2(x, y), contour)) {
                        // Point is inside contour
                        
                        // Calculate distance to edge for falloff
                        float edgeDistance = DistanceToPolygonEdge(new Vector2(x, y), contour);
                        float normalizedDistance = Mathf.Clamp01(edgeDistance / 5f); // Adjust falloff distance
                        
                        // Apply texture with falloff
                        alphaMap[y, x, textureIndex] += 1f - normalizedDistance;
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply height-based texturing.
        /// </summary>
        private void ApplyHeightBasedTexturing(float[,,] alphaMap, int resolution) {
            // Get heights
            float[,] heights = _terrainData.GetHeights(0, 0, _heightmapResolution, _heightmapResolution);
            
            // Define height ranges for different textures
            Dictionary<string, Vector2> heightRanges = new Dictionary<string, Vector2> {
                { "water", new Vector2(0.0f, 0.3f) },
                { "sand", new Vector2(0.25f, 0.35f) },
                { "grass", new Vector2(0.3f, 0.7f) },
                { "rock", new Vector2(0.6f, 0.9f) },
                { "snow", new Vector2(0.85f, 1.0f) }
            };
            
            // Get texture indices
            Dictionary<string, int> textureIndices = new Dictionary<string, int>();
            foreach (var range in heightRanges) {
                textureIndices[range.Key] = GetTextureIndexForTerrainType(range.Key);
            }
            
            // Apply textures based on height
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    // Sample height at this position
                    int heightX = Mathf.FloorToInt(x * _heightmapResolution / (float)resolution);
                    int heightY = Mathf.FloorToInt(y * _heightmapResolution / (float)resolution);
                    
                    if (heightX >= _heightmapResolution) heightX = _heightmapResolution - 1;
                    if (heightY >= _heightmapResolution) heightY = _heightmapResolution - 1;
                    
                    float height = heights[heightY, heightX];
                    
                    // Apply textures based on height ranges
                    foreach (var range in heightRanges) {
                        int textureIndex = textureIndices[range.Key];
                        if (textureIndex >= 0) {
                            // Calculate weight based on how close height is to this range
                            float minHeight = range.Value.x;
                            float maxHeight = range.Value.y;
                            float weight = 0f;
                            
                            if (height >= minHeight && height <= maxHeight) {
                                // Fully in range
                                if (height <= minHeight + (maxHeight - minHeight) * 0.2f) {
                                    // Blend in
                                    weight = (height - minHeight) / ((maxHeight - minHeight) * 0.2f);
                                }
                                else if (height >= minHeight + (maxHeight - minHeight) * 0.8f) {
                                    // Blend out
                                    weight = 1f - (height - (minHeight + (maxHeight - minHeight) * 0.8f)) / ((maxHeight - minHeight) * 0.2f);
                                }
                                else {
                                    // Middle of range
                                    weight = 1f;
                                }
                            }
                            
                            // Apply weight
                            alphaMap[y, x, textureIndex] += weight;
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Apply slope-based texturing.
        /// </summary>
        private void ApplySlopeBasedTexturing(float[,,] alphaMap, int resolution) {
            // Get heights
            float[,] heights = _terrainData.GetHeights(0, 0, _heightmapResolution, _heightmapResolution);
            
            // Define slope ranges for different textures
            Dictionary<string, Vector2> slopeRanges = new Dictionary<string, Vector2> {
                { "grass", new Vector2(0.0f, 0.3f) },
                { "dirt", new Vector2(0.25f, 0.5f) },
                { "rock", new Vector2(0.45f, 1.0f) }
            };
            
            // Get texture indices
            Dictionary<string, int> textureIndices = new Dictionary<string, int>();
            foreach (var range in slopeRanges) {
                textureIndices[range.Key] = GetTextureIndexForTerrainType(range.Key);
            }
            
            // Calculate slopes
            float[,] slopes = CalculateSlopes(heights);
            
            // Apply textures based on slope
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    // Sample slope at this position
                    int slopeX = Mathf.FloorToInt(x * (_heightmapResolution - 1) / (float)resolution);
                    int slopeY = Mathf.FloorToInt(y * (_heightmapResolution - 1) / (float)resolution);
                    
                    if (slopeX >= _heightmapResolution - 1) slopeX = _heightmapResolution - 2;
                    if (slopeY >= _heightmapResolution - 1) slopeY = _heightmapResolution - 2;
                    
                    float slope = slopes[slopeY, slopeX];
                    
                    // Apply textures based on slope ranges
                    foreach (var range in slopeRanges) {
                        int textureIndex = textureIndices[range.Key];
                        if (textureIndex >= 0) {
                            // Calculate weight based on how close slope is to this range
                            float minSlope = range.Value.x;
                            float maxSlope = range.Value.y;
                            float weight = 0f;
                            
                            if (slope >= minSlope && slope <= maxSlope) {
                                // Fully in range
                                if (slope <= minSlope + (maxSlope - minSlope) * 0.2f) {
                                    // Blend in
                                    weight = (slope - minSlope) / ((maxSlope - minSlope) * 0.2f);
                                }
                                else if (slope >= minSlope + (maxSlope - minSlope) * 0.8f) {
                                    // Blend out
                                    weight = 1f - (slope - (minSlope + (maxSlope - minSlope) * 0.8f)) / ((maxSlope - minSlope) * 0.2f);
                                }
                                else {
                                    // Middle of range
                                    weight = 1f;
                                }
                            }
                            
                            // Apply weight
                            alphaMap[y, x, textureIndex] += weight * 0.5f; // Reduce influence of slope
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Calculate slopes from heights.
        /// </summary>
        private float[,] CalculateSlopes(float[,] heights) {
            int width = heights.GetLength(1);
            int height = heights.GetLength(0);
            float[,] slopes = new float[height - 1, width - 1];
            
            for (int y = 0; y < height - 1; y++) {
                for (int x = 0; x < width - 1; x++) {
                    // Calculate slope using central differences
                    float dx = heights[y, x + 1] - heights[y, x];
                    float dy = heights[y + 1, x] - heights[y, x];
                    
                    // Calculate slope as magnitude of gradient
                    float slope = Mathf.Sqrt(dx * dx + dy * dy);
                    
                    // Store normalized slope (0-1)
                    slopes[y, x] = Mathf.Clamp01(slope * 5f); // Scale for better contrast
                }
            }
            
            return slopes;
        }
        
        /// <summary>
        /// Normalize alpha maps so weights sum to 1.
        /// </summary>
        private void NormalizeAlphaMaps(float[,,] alphaMap, int resolution) {
            int layerCount = alphaMap.GetLength(2);
            
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    // Calculate sum of weights
                    float sum = 0f;
                    for (int layer = 0; layer < layerCount; layer++) {
                        sum += alphaMap[y, x, layer];
                    }
                    
                    // Skip if no weights
                    if (sum <= 0f) {
                        // Set default layer
                        alphaMap[y, x, 0] = 1f;
                        for (int layer = 1; layer < layerCount; layer++) {
                            alphaMap[y, x, layer] = 0f;
                        }
                    }
                    else {
                        // Normalize weights to sum to 1
                        for (int layer = 0; layer < layerCount; layer++) {
                            alphaMap[y, x, layer] /= sum;
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Get texture index for terrain type.
        /// </summary>
        private int GetTextureIndexForTerrainType(string terrainType) {
            // Try exact match
            string key = terrainType.ToLower();
            if (_terrainTypeToTextureIndex.TryGetValue(key, out int index)) {
                return index;
            }
            
            // Try partial match
            foreach (var mapping in _terrainTypeToTextureIndex) {
                if (key.Contains(mapping.Key) || mapping.Key.Contains(key)) {
                    return mapping.Value;
                }
            }
            
            // Try generic match
            if (IsWaterType(key) && _terrainTypeToTextureIndex.TryGetValue("water", out int waterIndex)) {
                return waterIndex;
            }
            if (IsMountainType(key) && _terrainTypeToTextureIndex.TryGetValue("mountain", out int mountainIndex)) {
                return mountainIndex;
            }
            if (IsForestType(key) && _terrainTypeToTextureIndex.TryGetValue("forest", out int forestIndex)) {
                return forestIndex;
            }
            
            // Return default if nothing else matches
            return 0;
        }
        
        #endregion
        
        #region Water Generation
        
        /// <summary>
        /// Generate water bodies from analysis results.
        /// </summary>
        private IEnumerator GenerateWaterBodies(AnalysisResults analysisResults) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Clear existing water objects
                ClearWaterObjects();
                
                // Reset water bodies list
                _waterBodies.Clear();
                
                // Process terrain features for water
                if (analysisResults.terrainFeatures != null && analysisResults.terrainFeatures.Count > 0) {
                    // Collect water features
                    List<TerrainFeature> waterFeatures = analysisResults.terrainFeatures
                        .Where(f => IsWaterType(f.featureType))
                        .ToList();
                    
                    // Process each water feature
                    foreach (var feature in waterFeatures) {
                        // Create water body data
                        WaterBodyData waterBody = new WaterBodyData {
                            waterType = feature.featureType,
                            boundingBox = feature.bounds,
                            contourPoints = feature.contourPoints != null && feature.contourPoints.Count > 0 
                                ? new List<Vector2>(feature.contourPoints) 
                                : null,
                            depth = GetWaterDepthForType(feature.featureType),
                            material = _waterMaterial,
                            isFlowing = IsFlowingWaterType(feature.featureType),
                            flowDirection = GetFlowDirectionForFeature(feature)
                        };
                        
                        // Add to list
                        _waterBodies.Add(waterBody);
                    }
                }
                // Or process terrain objects for water
                else if (analysisResults.terrainObjects != null && analysisResults.terrainObjects.Count > 0) {
                    // Collect water objects
                    List<DetectedObject> waterObjects = analysisResults.terrainObjects
                        .Where(o => o.isTerrain && IsWaterType(o.className))
                        .ToList();
                    
                    // Process each water object
                    foreach (var waterObj in waterObjects) {
                        // Get contour points from segments
                        List<Vector2> contourPoints = null;
                        if (waterObj.segments != null && waterObj.segments.Count > 0 && 
                            waterObj.segments[0].contourPoints != null) {
                            contourPoints = new List<Vector2>(waterObj.segments[0].contourPoints);
                        }
                        
                        // Create water body data
                        WaterBodyData waterBody = new WaterBodyData {
                            waterType = waterObj.className,
                            boundingBox = waterObj.boundingBox,
                            contourPoints = contourPoints,
                            depth = GetWaterDepthForType(waterObj.className),
                            material = _waterMaterial,
                            isFlowing = IsFlowingWaterType(waterObj.className),
                            flowDirection = GetFlowDirectionForObject(waterObj)
                        };
                        
                        // Add to list
                        _waterBodies.Add(waterBody);
                    }
                }
                
                // Create water meshes
                yield return StartCoroutine(CreateWaterMeshes());
                
                float waterTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["WaterGenerationTime"] = waterTime;
                
                Log($"Generated {_waterBodies.Count} water bodies in {waterTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating water bodies: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Generate a default water body.
        /// </summary>
        private void GenerateDefaultWaterBody() {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Clear existing water objects
                ClearWaterObjects();
                
                // Reset water bodies list
                _waterBodies.Clear();
                
                // Create a simple water plane
                GameObject waterObject = new GameObject("WaterPlane");
                waterObject.transform.position = new Vector3(_terrainSize.x / 2f, _terrainSize.y * _waterLevel, _terrainSize.z / 2f);
                
                // Create mesh
                MeshFilter meshFilter = waterObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = waterObject.AddComponent<MeshRenderer>();
                
                // Set material
                meshRenderer.material = _waterMaterial != null ? _waterMaterial : new Material(Shader.Find("Standard"));
                
                // Create plane mesh
                float width = _terrainSize.x;
                float length = _terrainSize.z;
                
                Mesh mesh = new Mesh();
                mesh.vertices = new Vector3[] {
                    new Vector3(-width/2, 0, -length/2),
                    new Vector3(width/2, 0, -length/2),
                    new Vector3(width/2, 0, length/2),
                    new Vector3(-width/2, 0, length/2)
                };
                mesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
                mesh.uv = new Vector2[] {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(1, 1),
                    new Vector2(0, 1)
                };
                mesh.RecalculateNormals();
                
                meshFilter.mesh = mesh;
                
                // Add to water objects list
                _waterObjects.Add(waterObject);
                
                float waterTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["WaterGenerationTime"] = waterTime;
                
                Log($"Generated default water plane in {waterTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating default water: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Create water meshes from water bodies.
        /// </summary>
        private IEnumerator CreateWaterMeshes() {
            // Ensure height map is loaded from terrain data
            if (_heightMap == null) {
                _heightMap = _terrainData.GetHeights(0, 0, _heightmapResolution, _heightmapResolution);
            }
            
            // Process each water body
            foreach (var waterBody in _waterBodies) {
                // Create water object
                GameObject waterObject = new GameObject($"WaterBody_{waterBody.waterType}");
                waterObject.transform.position = new Vector3(0, _terrainSize.y * _waterLevel, 0);
                
                // Create mesh components
                MeshFilter meshFilter = waterObject.AddComponent<MeshFilter>();
                MeshRenderer meshRenderer = waterObject.AddComponent<MeshRenderer>();
                
                // Set material
                meshRenderer.material = waterBody.material != null ? waterBody.material : _waterMaterial;
                
                // Create mesh based on contour if available
                if (waterBody.contourPoints != null && waterBody.contourPoints.Count >= 3) {
                    yield return StartCoroutine(CreateWaterMeshFromContour(waterBody, meshFilter));
                }
                else {
                    // Create simple mesh from bounding box
                    CreateWaterMeshFromBounds(waterBody, meshFilter);
                }
                
                // Add to water objects list
                _waterObjects.Add(waterObject);
                
                // Add water flow if needed
                if (waterBody.isFlowing) {
                    AddWaterFlow(waterObject, waterBody);
                }
                
                yield return null;
            }
        }
        /// <summary>
        /// Create water mesh from contour points.
        /// </summary>
        private IEnumerator CreateWaterMeshFromContour(WaterBodyData waterBody, MeshFilter meshFilter) {
            // Convert contour points to world space
            List<Vector3> worldPoints = new List<Vector3>();
            foreach (var point in waterBody.contourPoints) {
                // Convert to terrain space
                Vector2 terrainPoint = ConvertToTerrainSpace(point);
                
                // Get height at this point
                float height = SampleHeightAt(terrainPoint);
                
                // Convert to world space
                float x = terrainPoint.x * _terrainSize.x / _heightmapResolution;
                float z = terrainPoint.y * _terrainSize.z / _heightmapResolution;
                
                // Add to list
                worldPoints.Add(new Vector3(x, height * _terrainSize.y, z));
            }
            
            // Ensure CCW winding for triangulation
            if (!IsPolygonClockwise(waterBody.contourPoints)) {
                worldPoints.Reverse();
            }
            
            // Create mesh
            Mesh mesh = new Mesh();
            
            // Use triangulation for mesh
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();
            
            // Use ear clipping triangulation
            if (worldPoints.Count > 500) {
                // For large polygons, use simpler triangulation
                yield return StartCoroutine(TriangulatePolygonFan(worldPoints, vertices, triangles, uvs));
            }
            else {
                // For smaller polygons, use ear clipping
                yield return StartCoroutine(TriangulatePolygonEarClipping(worldPoints, vertices, triangles, uvs));
            }
            
            // Set mesh data
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.RecalculateNormals();
            
            // Set mesh to filter
            meshFilter.mesh = mesh;
        }
        
        /// <summary>
        /// Create water mesh from bounding box.
        /// </summary>
        private void CreateWaterMeshFromBounds(WaterBodyData waterBody, MeshFilter meshFilter) {
            // Convert bounds to terrain space
            Vector2 min = ConvertToTerrainSpace(new Vector2(waterBody.boundingBox.x, waterBody.boundingBox.y));
            Vector2 max = ConvertToTerrainSpace(new Vector2(
                waterBody.boundingBox.x + waterBody.boundingBox.width,
                waterBody.boundingBox.y + waterBody.boundingBox.height
            ));
            
            // Convert to world space
            float minX = min.x * _terrainSize.x / _heightmapResolution;
            float minZ = min.y * _terrainSize.z / _heightmapResolution;
            float maxX = max.x * _terrainSize.x / _heightmapResolution;
            float maxZ = max.y * _terrainSize.z / _heightmapResolution;
            
            // Create mesh
            Mesh mesh = new Mesh();
            
            // Create vertices
            Vector3[] vertices = new Vector3[] {
                new Vector3(minX, 0, minZ),
                new Vector3(maxX, 0, minZ),
                new Vector3(maxX, 0, maxZ),
                new Vector3(minX, 0, maxZ)
            };
            
            // Create triangles
            int[] triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            
            // Create UVs
            Vector2[] uvs = new Vector2[] {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            
            // Set mesh data
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            
            // Set mesh to filter
            meshFilter.mesh = mesh;
        }
        
        /// <summary>
        /// Add water flow component to water object.
        /// </summary>
        private void AddWaterFlow(GameObject waterObject, WaterBodyData waterBody) {
            // Add a simple flow script - this would be replaced with a more sophisticated water flow system
            // in a production environment
            WaterFlow flowComponent = waterObject.AddComponent<WaterFlow>();
            
            // Set flow properties
            flowComponent.flowDirection = new Vector3(waterBody.flowDirection.x, 0, waterBody.flowDirection.y);
            flowComponent.flowSpeed = waterBody.flowSpeed;
            
            // Add water shader properties
            MeshRenderer renderer = waterObject.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null) {
                // Set flow properties in shader if supported
                renderer.material.SetVector("_FlowDirection", new Vector4(
                    waterBody.flowDirection.x, 0, waterBody.flowDirection.y, 0));
                renderer.material.SetFloat("_FlowSpeed", waterBody.flowSpeed);
            }
        }
        
        /// <summary>
        /// Clear all water objects.
        /// </summary>
        private void ClearWaterObjects() {
            foreach (var waterObject in _waterObjects) {
                if (waterObject != null) {
                    DestroyImmediate(waterObject);
                }
            }
            
            _waterObjects.Clear();
        }
        
        /// <summary>
        /// Get water depth for water type.
        /// </summary>
        private float GetWaterDepthForType(string waterType) {
            waterType = waterType.ToLower();
            
            switch (waterType) {
                case "ocean": return 20f;
                case "lake": return 10f;
                case "river": return 5f;
                case "stream": return 2f;
                case "pond": return 3f;
                case "swamp":
                case "marsh": return 1f;
                default: return 5f;
            }
        }
        
        /// <summary>
        /// Check if water type is flowing.
        /// </summary>
        private bool IsFlowingWaterType(string waterType) {
            waterType = waterType.ToLower();
            
            return waterType == "river" || waterType == "stream" || waterType == "creek";
        }
        
        /// <summary>
        /// Get flow direction for a terrain feature.
        /// </summary>
        private Vector2 GetFlowDirectionForFeature(TerrainFeature feature) {
            // Default direction
            Vector2 direction = new Vector2(0, 1);
            
            // If contour points are available, try to determine flow direction
            if (feature.contourPoints != null && feature.contourPoints.Count > 0) {
                // For rivers, estimate direction from shape
                if (feature.contourPoints.Count >= 2) {
                    // Simplify to major axis
                    Vector2 start = feature.contourPoints[0];
                    Vector2 end = feature.contourPoints[feature.contourPoints.Count - 1];
                    
                    direction = (end - start).normalized;
                }
            }
            
            return direction;
        }
        
        /// <summary>
        /// Get flow direction for a detected object.
        /// </summary>
        private Vector2 GetFlowDirectionForObject(DetectedObject obj) {
            // Default direction
            Vector2 direction = new Vector2(0, 1);
            
            // If segments are available, try to determine flow direction
            if (obj.segments != null && obj.segments.Count > 0 && 
                obj.segments[0].contourPoints != null && 
                obj.segments[0].contourPoints.Count > 0) {
                
                var contourPoints = obj.segments[0].contourPoints;
                
                // For rivers, estimate direction from shape
                if (contourPoints.Count >= 2) {
                    // Simplify to major axis
                    Vector2 start = contourPoints[0];
                    Vector2 end = contourPoints[contourPoints.Count - 1];
                    
                    direction = (end - start).normalized;
                }
            }
            
            return direction;
        }
        
        /// <summary>
        /// Triangulate a polygon using the fan method.
        /// </summary>
        private IEnumerator TriangulatePolygonFan(List<Vector3> points, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs) {
            if (points.Count < 3) yield break;
            
            // Add vertices
            vertices.AddRange(points);
            
            // Calculate center point
            Vector3 center = Vector3.zero;
            foreach (var p in points) {
                center += p;
            }
            center /= points.Count;
            
            // Add center point
            vertices.Add(center);
            
            // Calculate UVs
            Vector3 minPoint = points[0];
            Vector3 maxPoint = points[0];
            
            foreach (var p in points) {
                minPoint.x = Mathf.Min(minPoint.x, p.x);
                minPoint.z = Mathf.Min(minPoint.z, p.z);
                maxPoint.x = Mathf.Max(maxPoint.x, p.x);
                maxPoint.z = Mathf.Max(maxPoint.z, p.z);
            }
            
            // Create UVs
            for (int i = 0; i < points.Count; i++) {
                float u = (points[i].x - minPoint.x) / (maxPoint.x - minPoint.x);
                float v = (points[i].z - minPoint.z) / (maxPoint.z - minPoint.z);
                
                uvs.Add(new Vector2(u, v));
            }
            
            // Center UV
            uvs.Add(new Vector2(0.5f, 0.5f));
            
            // Create triangles
            int centerIndex = vertices.Count - 1;
            for (int i = 0; i < points.Count; i++) {
                triangles.Add(i);
                triangles.Add(centerIndex);
                triangles.Add((i + 1) % points.Count);
            }
            
            yield return null;
        }
        /// <summary>
        /// Triangulate a polygon using ear clipping.
        /// </summary>
        private IEnumerator TriangulatePolygonEarClipping(List<Vector3> points, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs) {
            if (points.Count < 3) yield break;
            
            // Create working list of points
            List<Vector3> workingPoints = new List<Vector3>(points);
            
            // Create index mapping
            List<int> indices = new List<int>();
            for (int i = 0; i < workingPoints.Count; i++) {
                indices.Add(i);
            }
            
            // Create vertices
            vertices.AddRange(workingPoints);
            
            // Calculate UVs
            Vector3 minPoint = workingPoints[0];
            Vector3 maxPoint = workingPoints[0];
            
            foreach (var p in workingPoints) {
                minPoint.x = Mathf.Min(minPoint.x, p.x);
                minPoint.z = Mathf.Min(minPoint.z, p.z);
                maxPoint.x = Mathf.Max(maxPoint.x, p.x);
                maxPoint.z = Mathf.Max(maxPoint.z, p.z);
            }
            
            // Create UVs
            for (int i = 0; i < workingPoints.Count; i++) {
                float u = (workingPoints[i].x - minPoint.x) / (maxPoint.x - minPoint.x);
                float v = (workingPoints[i].z - minPoint.z) / (maxPoint.z - minPoint.z);
                
                uvs.Add(new Vector2(u, v));
            }
            
            // Triangulate using ear clipping
            int remainingPoints = workingPoints.Count;
            int clipCount = 0;
            int maxClips = workingPoints.Count * 2; // Safety to prevent infinite loops
            
            while (remainingPoints > 3 && clipCount < maxClips) {
                bool clippedEar = false;
                
                for (int i = 0; i < remainingPoints; i++) {
                    int prev = (i - 1 + remainingPoints) % remainingPoints;
                    int next = (i + 1) % remainingPoints;
                    
                    // Get indices
                    int prevIdx = indices[prev];
                    int currIdx = indices[i];
                    int nextIdx = indices[next];
                    
                    // Check if this vertex forms an ear
                    if (IsEar(workingPoints, indices, remainingPoints, i)) {
                        // Add triangle
                        triangles.Add(prevIdx);
                        triangles.Add(currIdx);
                        triangles.Add(nextIdx);
                        
                        // Remove the ear vertex
                        for (int j = i; j < remainingPoints - 1; j++) {
                            indices[j] = indices[j + 1];
                        }
                        
                        // Decrement remaining points
                        remainingPoints--;
                        
                        // Mark as clipped
                        clippedEar = true;
                        
                        break;
                    }
                }
                
                // If no ear was clipped, break to prevent infinite loop
                if (!clippedEar) {
                    break;
                }
                
                clipCount++;
                
                // Yield periodically
                if (clipCount % 100 == 0) {
                    yield return null;
                }
            }
            
            // Add the final triangle
            if (remainingPoints == 3) {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
            }
        }
        
        /// <summary>
        /// Check if a vertex is an ear in the polygon.
        /// </summary>
        private bool IsEar(List<Vector3> points, List<int> indices, int count, int index) {
            int prev = (index - 1 + count) % count;
            int next = (index + 1) % count;
            
            Vector3 p1 = points[indices[prev]];
            Vector3 p2 = points[indices[index]];
            Vector3 p3 = points[indices[next]];
            
            // Ignore Y for 2D triangulation
            p1.y = 0;
            p2.y = 0;
            p3.y = 0;
            
            // Check if angle is convex
            Vector3 v1 = p1 - p2;
            Vector3 v2 = p3 - p2;
            
            float cross = v1.x * v2.z - v1.z * v2.x;
            
            // If not convex, not an ear
            if (cross < 0) return false;
            
            // Check if no other points are inside the potential ear
            for (int i = 0; i < count; i++) {
                if (i == prev || i == index || i == next) continue;
                
                Vector3 pt = points[indices[i]];
                pt.y = 0;
                
                if (PointInTriangle(pt, p1, p2, p3)) {
                    return false;
                }
            }
            
            return true;
        }
        
        #endregion
        
        #region Terrain Details
        
        /// <summary>
        /// Generate terrain details from analysis results.
        /// </summary>
        private IEnumerator GenerateTerrainDetails(AnalysisResults analysisResults) {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Generate detail layers
                yield return StartCoroutine(GenerateDetailLayers(analysisResults));
                
                // Generate trees
                yield return StartCoroutine(GenerateTreeInstances(analysisResults));
                
                float detailTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["DetailGenerationTime"] = detailTime;
                
                Log($"Generated terrain details in {detailTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error generating terrain details: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        /// <summary>
        /// Generate detail layers.
        /// </summary>
        private IEnumerator GenerateDetailLayers(AnalysisResults analysisResults) {
            // Skip if no detail prototypes
            if (_terrainData.detailPrototypes.Length == 0) {
                yield break;
            }
            
            // Get detail resolution
            int detailResolution = _terrainData.detailResolution;
            
            // Create detail maps
            Dictionary<int, int[,]> detailMaps = new Dictionary<int, int[,]>();
            for (int i = 0; i < _terrainData.detailPrototypes.Length; i++) {
                detailMaps[i] = new int[detailResolution, detailResolution];
            }
            
            // Process terrain features
            if (analysisResults.terrainFeatures != null && analysisResults.terrainFeatures.Count > 0) {
                foreach (var feature in analysisResults.terrainFeatures) {
                    // Get detail index for this terrain type
                    int detailIndex = GetDetailIndexForTerrainType(feature.featureType);
                    if (detailIndex >= 0) {
                        ApplyTerrainFeatureDetail(feature, detailMaps[detailIndex], detailResolution);
                    }
                    
                    yield return null;
                }
            }
            // Or process terrain objects
            else if (analysisResults.terrainObjects != null && analysisResults.terrainObjects.Count > 0) {
                foreach (var terrainObj in analysisResults.terrainObjects) {
                    if (terrainObj.isTerrain) {
                        // Get detail index for this terrain type
                        int detailIndex = GetDetailIndexForTerrainType(terrainObj.className);
                        if (detailIndex >= 0) {
                            ApplyTerrainObjectDetail(terrainObj, detailMaps[detailIndex], detailResolution);
                        }
                    }
                    
                    yield return null;
                }
            }
            
            // Apply height-based details
            ApplyHeightBasedDetails(detailMaps, detailResolution);
            yield return null;
            
            // Apply detail maps to terrain
            for (int i = 0; i < _terrainData.detailPrototypes.Length; i++) {
                if (detailMaps.ContainsKey(i)) {
                    _terrainData.SetDetailLayer(0, 0, i, detailMaps[i]);
                }
            }
        }
        
        /// <summary>
        /// Generate tree instances.
        /// </summary>
        private IEnumerator GenerateTreeInstances(AnalysisResults analysisResults) {
            // Skip if no tree prototypes
            if (_terrainData.treePrototypes.Length == 0) {
                yield break;
            }
            
            // Create tree instances list
            List<TreeInstance> treeInstances = new List<TreeInstance>();
            
            // Process terrain features
            if (analysisResults.terrainFeatures != null && analysisResults.terrainFeatures.Count > 0) {
                foreach (var feature in analysisResults.terrainFeatures) {
                    // Get tree index for this terrain type
                    int treeIndex = GetTreeIndexForTerrainType(feature.featureType);
                    if (treeIndex >= 0) {
                        GenerateTreesForTerrainFeature(feature, treeInstances, treeIndex);
                    }
                    
                    yield return null;
                }
            }
            // Or process terrain objects
            else if (analysisResults.terrainObjects != null && analysisResults.terrainObjects.Count > 0) {
                foreach (var terrainObj in analysisResults.terrainObjects) {
                    if (terrainObj.isTerrain) {
                        // Get tree index for this terrain type
                        int treeIndex = GetTreeIndexForTerrainType(terrainObj.className);
                        if (treeIndex >= 0) {
                            GenerateTreesForTerrainObject(terrainObj, treeInstances, treeIndex);
                        }
                    }
                    
                    yield return null;
                }
            }
            
            // Add random trees in forest areas
            yield return StartCoroutine(AddRandomTrees(treeInstances, analysisResults));
            
            // Apply tree instances to terrain
            _terrainData.SetTreeInstances(treeInstances.ToArray(), true);
        }
        /// <summary>
        /// Apply terrain feature detail.
        /// </summary>
        private void ApplyTerrainFeatureDetail(TerrainFeature feature, int[,] detailMap, int resolution) {
            // Get detail prototype index
            int detailIndex = GetDetailIndexForTerrainType(feature.featureType);
            if (detailIndex < 0) return;
            
            // Get prototype for density
            float density = 0.5f;
            if (detailIndex < _detailPrototypes.Count) {
                density = _detailPrototypes[detailIndex].density;
            }
            
            // Calculate region to affect
            Vector2 center = ConvertToTerrainSpace(new Vector2(
                feature.bounds.x + feature.bounds.width / 2f,
                feature.bounds.y + feature.bounds.height / 2f
            ));
            float radius = ConvertToTerrainSpace(Mathf.Max(feature.bounds.width, feature.bounds.height) / 2f);
            
            // Scale to detail resolution
            center.x = center.x * resolution / _heightmapResolution;
            center.y = center.y * resolution / _heightmapResolution;
            radius = radius * resolution / _heightmapResolution;
            
            // Apply detail
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply influence
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        influence = Mathf.Pow(influence, 2f); // Sharper falloff
                        
                        // Apply detail density
                        float detailDensity = influence * density * 10f; // Scale to 0-10 range
                        
                        // Add some randomization
                        float random = Mathf.PerlinNoise(x * 0.1f, y * 0.1f);
                        
                        // Apply detail value
                        if (random < detailDensity) {
                            detailMap[y, x] = Mathf.Max(detailMap[y, x], Mathf.FloorToInt(detailDensity));
                        }
                    }
                }
            }
            
            // Apply contour if available
            if (feature.contourPoints != null && feature.contourPoints.Count > 0) {
                // Convert contour points to detail map space
                List<Vector2> detailContour = new List<Vector2>();
                foreach (var point in feature.contourPoints) {
                    Vector2 terrainPoint = ConvertToTerrainSpace(point);
                    detailContour.Add(new Vector2(
                        terrainPoint.x * resolution / _heightmapResolution,
                        terrainPoint.y * resolution / _heightmapResolution
                    ));
                }
                
                // Apply detail within contour
                ApplyDetailToContour(detailMap, resolution, detailContour, density);
            }
        }
        
        /// <summary>
        /// Apply terrain object detail.
        /// </summary>
        private void ApplyTerrainObjectDetail(DetectedObject terrainObj, int[,] detailMap, int resolution) {
            // Skip if not terrain
            if (!terrainObj.isTerrain) return;
            
            // Get detail prototype index
            int detailIndex = GetDetailIndexForTerrainType(terrainObj.className);
            if (detailIndex < 0) return;
            
            // Get prototype for density
            float density = 0.5f;
            if (detailIndex < _detailPrototypes.Count) {
                density = _detailPrototypes[detailIndex].density;
            }
            
            // Calculate region to affect
            Vector2 center = ConvertToTerrainSpace(new Vector2(
                terrainObj.boundingBox.x + terrainObj.boundingBox.width / 2f,
                terrainObj.boundingBox.y + terrainObj.boundingBox.height / 2f
            ));
            float radius = ConvertToTerrainSpace(Mathf.Max(terrainObj.boundingBox.width, terrainObj.boundingBox.height) / 2f);
            
            // Scale to detail resolution
            center.x = center.x * resolution / _heightmapResolution;
            center.y = center.y * resolution / _heightmapResolution;
            radius = radius * resolution / _heightmapResolution;
            
            // Apply detail
            int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maxY = Mathf.Min(resolution - 1, Mathf.CeilToInt(center.y + radius));
            
            // Apply influence
            for (int y = minY; y <= maxY; y++) {
                for (int x = minX; x <= maxX; x++) {
                    // Calculate distance from center (0-1 range)
                    float distSqr = ((x - center.x) * (x - center.x) + (y - center.y) * (y - center.y)) / (radius * radius);
                    
                    if (distSqr <= 1f) {
                        // Calculate influence factor
                        float influence = 1f - Mathf.Sqrt(distSqr);
                        influence = Mathf.Pow(influence, 2f); // Sharper falloff
                        
                        // Apply detail density
                        float detailDensity = influence * density * 10f; // Scale to 0-10 range
                        
                        // Add some randomization
                        float random = Mathf.PerlinNoise(x * 0.1f, y * 0.1f);
                        
                        // Apply detail value
                        if (random < detailDensity) {
                            detailMap[y, x] = Mathf.Max(detailMap[y, x], Mathf.FloorToInt(detailDensity));
                        }
                    }
                }
            }
            
            // Apply segments if available
            if (terrainObj.segments != null && terrainObj.segments.Count > 0) {
                foreach (var segment in terrainObj.segments) {
                    if (segment.contourPoints != null && segment.contourPoints.Count > 0) {
                        // Convert contour points to detail map space
                        List<Vector2> detailContour = new List<Vector2>();
                        foreach (var point in segment.contourPoints) {
                            Vector2 terrainPoint = ConvertToTerrainSpace(point);
                            detailContour.Add(new Vector2(
                                terrainPoint.x * resolution / _heightmapResolution,
                                terrainPoint.y * resolution / _heightmapResolution
                            ));
                        }
                        
                        // Apply detail within contour
                        ApplyDetailToContour(detailMap, resolution, detailContour, density);
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply detail to contour.
        /// </summary>
        private void ApplyDetailToContour(int[,] detailMap, int resolution, List<Vector2> contour, float density) {
            if (contour.Count < 3) return;
            
            // Calculate bounding box for optimization
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            
            foreach (var point in contour) {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            
            // Add padding
            float padding = 2f;
            minX = Mathf.Max(0, minX - padding);
            minY = Mathf.Max(0, minY - padding);
            maxX = Mathf.Min(resolution - 1, maxX + padding);
            maxY = Mathf.Min(resolution - 1, maxY + padding);
            
            // Apply detail within contour
            for (int y = Mathf.FloorToInt(minY); y <= Mathf.CeilToInt(maxY); y++) {
                for (int x = Mathf.FloorToInt(minX); x <= Mathf.CeilToInt(maxX); x++) {
                    if (PointInPolygon(new Vector2(x, y), contour)) {
                        // Point is inside contour
                        
                        // Calculate distance to edge for falloff
                        float edgeDistance = DistanceToPolygonEdge(new Vector2(x, y), contour);
                        float normalizedDistance = Mathf.Clamp01(edgeDistance / 5f); // Adjust falloff distance
                        
                        // Apply detail density
                        float detailDensity = density * (1f - normalizedDistance) * 10f; // Scale to 0-10 range
                        
                        // Add some randomization
                        float random = Mathf.PerlinNoise(x * 0.1f, y * 0.1f);
                        
                        // Apply detail value
                        if (random < detailDensity) {
                            detailMap[y, x] = Mathf.Max(detailMap[y, x], Mathf.FloorToInt(detailDensity));
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Apply height-based details.
        /// </summary>
        private void ApplyHeightBasedDetails(Dictionary<int, int[,]> detailMaps, int resolution) {
            // Get heights
            float[,] heights = _terrainData.GetHeights(0, 0, _heightmapResolution, _heightmapResolution);
            
            // Define height ranges for different details
            Dictionary<string, Vector2> heightRanges = new Dictionary<string, Vector2> {
                { "grass", new Vector2(0.3f, 0.7f) },
                { "flowers", new Vector2(0.35f, 0.6f) },
                { "rocks", new Vector2(0.5f, 0.9f) }
            };
            
            // Get detail indices
            Dictionary<string, int> detailIndices = new Dictionary<string, int>();
            foreach (var range in heightRanges) {
                detailIndices[range.Key] = GetDetailIndexForTerrainType(range.Key);
            }
            
            // Apply details based on height
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    // Sample height at this position
                    int heightX = Mathf.FloorToInt(x * _heightmapResolution / (float)resolution);
                    int heightY = Mathf.FloorToInt(y * _heightmapResolution / (float)resolution);
                    
                    if (heightX >= _heightmapResolution) heightX = _heightmapResolution - 1;
                    if (heightY >= _heightmapResolution) heightY = _heightmapResolution - 1;
                    
                    float height = heights[heightY, heightX];
                    
                    // Apply details based on height ranges
                    foreach (var range in heightRanges) {
                        int detailIndex = detailIndices[range.Key];
                        if (detailIndex >= 0 && detailMaps.ContainsKey(detailIndex)) {
                            // Calculate weight based on how close height is to this range
                            float minHeight = range.Value.x;
                            float maxHeight = range.Value.y;
                            float weight = 0f;
                            
                            if (height >= minHeight && height <= maxHeight) {
                                // Fully in range
                                if (height <= minHeight + (maxHeight - minHeight) * 0.2f) {
                                    // Blend in
                                    weight = (height - minHeight) / ((maxHeight - minHeight) * 0.2f);
                                }
                                else if (height >= minHeight + (maxHeight - minHeight) * 0.8f) {
                                    // Blend out
                                    weight = 1f - (height - (minHeight + (maxHeight - minHeight) * 0.8f)) / ((maxHeight - minHeight) * 0.2f);
                                }
                                else {
                                    // Middle of range
                                    weight = 1f;
                                }
                            }
                            
                            // Apply weight
                            if (weight > 0f) {
                                // Get detail density
                                float density = weight * 10f; // Scale to 0-10 range
                                
                                // Add some randomization
                                float random = Mathf.PerlinNoise(x * 0.1f, y * 0.1f);
                                
                                // Apply detail value
                                if (random < density) {
                                    detailMaps[detailIndex][y, x] = Mathf.Max(detailMaps[detailIndex][y, x], Mathf.FloorToInt(density));
                                }
                            }
                        }
                    }
                }
            }
        }
        /// <summary>
        /// Generate trees for terrain feature.
        /// </summary>
        private void GenerateTreesForTerrainFeature(TerrainFeature feature, List<TreeInstance> treeInstances, int treeIndex) {
            // Skip for water features
            if (IsWaterType(feature.featureType)) return;
            
            // Get tree prototype for density and scale
            float density = 0.5f;
            float minScale = 0.75f;
            float maxScale = 1.25f;
            
            if (treeIndex < _treePrototypes.Count) {
                density = _treePrototypes[treeIndex].density;
                minScale = _treePrototypes[treeIndex].minScale;
                maxScale = _treePrototypes[treeIndex].maxScale;
            }
            
            // Calculate region
            Vector2 center = ConvertToTerrainSpace(new Vector2(
                feature.bounds.x + feature.bounds.width / 2f,
                feature.bounds.y + feature.bounds.height / 2f
            ));
            float radius = ConvertToTerrainSpace(Mathf.Max(feature.bounds.width, feature.bounds.height) / 2f);
            
            // Calculate how many trees to place
            float area = Mathf.PI * radius * radius;
            int treeCount = Mathf.FloorToInt(area * density * 0.01f); // Adjust density scale
            
            // Add random trees
            for (int i = 0; i < treeCount; i++) {
                // Random position within circle
                float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                float distance = UnityEngine.Random.Range(0f, radius);
                
                float x = center.x + distance * Mathf.Cos(angle);
                float z = center.y + distance * Mathf.Sin(angle);
                
                // Normalize to 0-1 range
                float normalizedX = x / _heightmapResolution;
                float normalizedZ = z / _heightmapResolution;
                
                // Skip if outside terrain
                if (normalizedX < 0 || normalizedX > 1 || normalizedZ < 0 || normalizedZ > 1) {
                    continue;
                }
                
                // Create tree instance
                TreeInstance tree = new TreeInstance();
                tree.position = new Vector3(normalizedX, 0, normalizedZ);
                tree.rotation = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                tree.widthScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.heightScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.color = Color.white;
                tree.lightmapColor = Color.white;
                tree.prototypeIndex = treeIndex;
                
                // Add to list
                treeInstances.Add(tree);
            }
            
            // Apply contour if available
            if (feature.contourPoints != null && feature.contourPoints.Count > 0) {
                // Convert contour points to terrain space
                List<Vector2> terrainContour = new List<Vector2>();
                foreach (var point in feature.contourPoints) {
                    terrainContour.Add(ConvertToTerrainSpace(point));
                }
                
                // Add trees within contour
                AddTreesWithinContour(terrainContour, treeInstances, treeIndex, density, minScale, maxScale);
            }
        }
        
        /// <summary>
        /// Generate trees for terrain object.
        /// </summary>
        private void GenerateTreesForTerrainObject(DetectedObject terrainObj, List<TreeInstance> treeInstances, int treeIndex) {
            // Skip if not terrain or is water
            if (!terrainObj.isTerrain || IsWaterType(terrainObj.className)) return;
            
            // Get tree prototype for density and scale
            float density = 0.5f;
            float minScale = 0.75f;
            float maxScale = 1.25f;
            
            if (treeIndex < _treePrototypes.Count) {
                density = _treePrototypes[treeIndex].density;
                minScale = _treePrototypes[treeIndex].minScale;
                maxScale = _treePrototypes[treeIndex].maxScale;
            }
            
            // Calculate region
            Vector2 center = ConvertToTerrainSpace(new Vector2(
                terrainObj.boundingBox.x + terrainObj.boundingBox.width / 2f,
                terrainObj.boundingBox.y + terrainObj.boundingBox.height / 2f
            ));
            float radius = ConvertToTerrainSpace(Mathf.Max(terrainObj.boundingBox.width, terrainObj.boundingBox.height) / 2f);
            
            // Calculate how many trees to place
            float area = Mathf.PI * radius * radius;
            int treeCount = Mathf.FloorToInt(area * density * 0.01f); // Adjust density scale
            
            // Add random trees
            for (int i = 0; i < treeCount; i++) {
                // Random position within circle
                float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                float distance = UnityEngine.Random.Range(0f, radius);
                
                float x = center.x + distance * Mathf.Cos(angle);
                float z = center.y + distance * Mathf.Sin(angle);
                
                // Normalize to 0-1 range
                float normalizedX = x / _heightmapResolution;
                float normalizedZ = z / _heightmapResolution;
                
                // Skip if outside terrain
                if (normalizedX < 0 || normalizedX > 1 || normalizedZ < 0 || normalizedZ > 1) {
                    continue;
                }
                
                // Create tree instance
                TreeInstance tree = new TreeInstance();
                tree.position = new Vector3(normalizedX, 0, normalizedZ);
                tree.rotation = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                tree.widthScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.heightScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.color = Color.white;
                tree.lightmapColor = Color.white;
                tree.prototypeIndex = treeIndex;
                
                // Add to list
                treeInstances.Add(tree);
            }
            
            // Apply segments if available
            if (terrainObj.segments != null && terrainObj.segments.Count > 0) {
                foreach (var segment in terrainObj.segments) {
                    if (segment.contourPoints != null && segment.contourPoints.Count > 0) {
                        // Convert contour points to terrain space
                        List<Vector2> terrainContour = new List<Vector2>();
                        foreach (var point in segment.contourPoints) {
                            terrainContour.Add(ConvertToTerrainSpace(point));
                        }
                        
                        // Add trees within contour
                        AddTreesWithinContour(terrainContour, treeInstances, treeIndex, density, minScale, maxScale);
                    }
                }
            }
        }
        
        /// <summary>
        /// Add trees within contour.
        /// </summary>
        private void AddTreesWithinContour(List<Vector2> contour, List<TreeInstance> treeInstances, int treeIndex, 
                                          float density, float minScale, float maxScale) {
            if (contour.Count < 3) return;
            
            // Calculate bounding box for optimization
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            
            foreach (var point in contour) {
                minX = Mathf.Min(minX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x);
                maxY = Mathf.Max(maxY, point.y);
            }
            
            // Calculate area
            float width = maxX - minX;
            float height = maxY - minY;
            float area = width * height;
            
            // Calculate how many trees to place
            int treeCount = Mathf.FloorToInt(area * density * 0.01f); // Adjust density scale
            
            // Add random trees
            for (int i = 0; i < treeCount; i++) {
                // Random position within bounding box
                float x = UnityEngine.Random.Range(minX, maxX);
                float z = UnityEngine.Random.Range(minY, maxY);
                
                // Skip if outside contour
                if (!PointInPolygon(new Vector2(x, z), contour)) {
                    continue;
                }
                
                // Normalize to 0-1 range
                float normalizedX = x / _heightmapResolution;
                float normalizedZ = z / _heightmapResolution;
                
                // Skip if outside terrain
                if (normalizedX < 0 || normalizedX > 1 || normalizedZ < 0 || normalizedZ > 1) {
                    continue;
                }
                
                // Create tree instance
                TreeInstance tree = new TreeInstance();
                tree.position = new Vector3(normalizedX, 0, normalizedZ);
                tree.rotation = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                tree.widthScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.heightScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.color = Color.white;
                tree.lightmapColor = Color.white;
                tree.prototypeIndex = treeIndex;
                
                // Add to list
                treeInstances.Add(tree);
            }
        }
        
        /// <summary>
        /// Add random trees to terrain.
        /// </summary>
        private IEnumerator AddRandomTrees(List<TreeInstance> treeInstances, AnalysisResults analysisResults) {
            // Skip if no tree prototypes
            if (_terrainData.treePrototypes.Length == 0) {
                yield break;
            }
            
            // Get heights
            float[,] heights = _terrainData.GetHeights(0, 0, _heightmapResolution, _heightmapResolution);
            
            // Calculate total number of trees to add
            int totalTrees = 1000; // Default value
            
            // Add random trees
            for (int i = 0; i < totalTrees; i++) {
                // Random position
                float x = UnityEngine.Random.Range(0f, 1f);
                float z = UnityEngine.Random.Range(0f, 1f);
                
                // Get height at this position
                int heightX = Mathf.FloorToInt(x * _heightmapResolution);
                int heightZ = Mathf.FloorToInt(z * _heightmapResolution);
                
                if (heightX >= _heightmapResolution) heightX = _heightmapResolution - 1;
                if (heightZ >= _heightmapResolution) heightZ = _heightmapResolution - 1;
                
                float height = heights[heightZ, heightX];
                
                // Skip if too low (water) or too high (mountains)
                if (height < 0.3f || height > 0.8f) {
                    continue;
                }
                
                // Choose random tree prototype
                int treeIndex = UnityEngine.Random.Range(0, _terrainData.treePrototypes.Length);
                
                // Get scale range
                float minScale = 0.75f;
                float maxScale = 1.25f;
                
                if (treeIndex < _treePrototypes.Count) {
                    minScale = _treePrototypes[treeIndex].minScale;
                    maxScale = _treePrototypes[treeIndex].maxScale;
                }
                
                // Create tree instance
                TreeInstance tree = new TreeInstance();
                tree.position = new Vector3(x, 0, z);
                tree.rotation = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
                tree.widthScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.heightScale = UnityEngine.Random.Range(minScale, maxScale);
                tree.color = Color.white;
                tree.lightmapColor = Color.white;
                tree.prototypeIndex = treeIndex;
                
                // Add to list
                treeInstances.Add(tree);
                
                // Yield periodically
                if (i % 100 == 0) {
                    yield return null;
                }
            }
        }
        /// <summary>
        /// Get detail index for terrain type.
        /// </summary>
        private int GetDetailIndexForTerrainType(string terrainType) {
            // Try exact match
            string key = terrainType.ToLower();
            if (_terrainTypeToDetailIndex.TryGetValue(key, out int index)) {
                return index;
            }
            
            // Try partial match
            foreach (var mapping in _terrainTypeToDetailIndex) {
                if (key.Contains(mapping.Key) || mapping.Key.Contains(key)) {
                    return mapping.Value;
                }
            }
            
            // Try generic match
            if (IsForestType(key) && _terrainTypeToDetailIndex.TryGetValue("forest", out int forestIndex)) {
                return forestIndex;
            }
            if (IsGrassType(key) && _terrainTypeToDetailIndex.TryGetValue("grass", out int grassIndex)) {
                return grassIndex;
            }
            
            // No match
            return -1;
        }
        
        /// <summary>
        /// Get tree index for terrain type.
        /// </summary>
        private int GetTreeIndexForTerrainType(string terrainType) {
            // Try exact match
            string key = terrainType.ToLower();
            if (_terrainTypeToTreeIndex.TryGetValue(key, out int index)) {
                return index;
            }
            
            // Try partial match
            foreach (var mapping in _terrainTypeToTreeIndex) {
                if (key.Contains(mapping.Key) || mapping.Key.Contains(key)) {
                    return mapping.Value;
                }
            }
            
            // Try generic match
            if (IsForestType(key) && _terrainTypeToTreeIndex.TryGetValue("forest", out int forestIndex)) {
                return forestIndex;
            }
            
            // No match
            return -1;
        }
        
        #endregion
        
        #region Terrain Object Creation
        
        /// <summary>
        /// Create terrain object with terrain component.
        /// </summary>
        private void CreateTerrainObject() {
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Remove existing terrain
                if (_terrainObject != null) {
                    DestroyImmediate(_terrainObject);
                }
                
                // Create new terrain object
                _terrainObject = new GameObject("GeneratedTerrain");
                
                // Add terrain component
                _currentTerrain = _terrainObject.AddComponent<UnityEngine.Terrain>();
                _currentTerrain.terrainData = _terrainData;
                
                // Set terrain properties
                _currentTerrain.drawHeightmap = true;
                _currentTerrain.drawTreesAndFoliage = true;
                _currentTerrain.heightmapPixelError = 5;
                _currentTerrain.basemapDistance = 1000;
                _currentTerrain.treeBillboardDistance = 200;
                _currentTerrain.treeCrossFadeLength = 10;
                _currentTerrain.treeMaximumFullLODCount = 50;
                
                // Add terrain collider
                TerrainCollider terrainCollider = _terrainObject.AddComponent<TerrainCollider>();
                terrainCollider.terrainData = _terrainData;
                
                // Apply material if provided
                if (_terrainMaterial != null) {
                    _currentTerrain.materialTemplate = _terrainMaterial;
                }
                
                float createTime = Time.realtimeSinceStartup - startTime;
                _performanceMetrics["TerrainObjectCreationTime"] = createTime;
                
                Log($"Created terrain object in {createTime:F2} seconds", LogCategory.Terrain);
            }
            catch (Exception ex) {
                LogError($"Error creating terrain object: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Convert image space coordinates to terrain space.
        /// </summary>
        private Vector2 ConvertToTerrainSpace(Vector2 imagePoint) {
            if (_lastImageSize.x <= 0 || _lastImageSize.y <= 0) {
                return imagePoint;
            }
            
            // Convert to normalized 0-1 space
            float normalizedX = imagePoint.x / _lastImageSize.x;
            float normalizedY = imagePoint.y / _lastImageSize.y;
            
            // Convert to terrain space
            float terrainX = normalizedX * _heightmapResolution;
            float terrainY = normalizedY * _heightmapResolution;
            
            return new Vector2(terrainX, terrainY);
        }
        
        /// <summary>
        /// Convert terrain space to world space.
        /// </summary>
        private Vector3 TerrainToWorldSpace(Vector2 terrainPoint, float height = 0) {
            float x = terrainPoint.x * _terrainSize.x / _heightmapResolution;
            float y = height * _terrainSize.y;
            float z = terrainPoint.y * _terrainSize.z / _heightmapResolution;
            
            return new Vector3(x, y, z);
        }
        
        /// <summary>
        /// Get feature strength for terrain type.
        /// </summary>
        private float GetFeatureStrength(string featureType) {
            featureType = featureType.ToLower();
            
            switch (featureType) {
                case "mountain": return 0.8f;
                case "hill": return 0.5f;
                case "plateau": return 0.4f;
                case "valley": return 0.5f;
                case "canyon": return 0.7f;
                case "river": return 0.6f;
                case "lake": return 0.7f;
                case "ocean": return 0.9f;
                case "forest": return 0.3f;
                case "swamp": return 0.4f;
                default: return 0.5f;
            }
        }
        
        /// <summary>
        /// Sample height at terrain point.
        /// </summary>
        private float SampleHeightAt(Vector2 terrainPoint) {
            int x = Mathf.Clamp(Mathf.FloorToInt(terrainPoint.x), 0, _heightmapResolution - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(terrainPoint.y), 0, _heightmapResolution - 1);
            
            return _heightMap != null ? _heightMap[x, y] : 0f;
        }
        
        /// <summary>
        /// Check if point is inside polygon.
        /// </summary>
        private bool PointInPolygon(Vector2 point, List<Vector2> polygon) {
            int polygonLength = polygon.Count, i = 0;
            bool inside = false;
            
            // x, y for tested point.
            float pointX = point.x, pointY = point.y;
            
            // Start with the last vertex in polygon.
            float startX = polygon[polygonLength - 1].x, startY = polygon[polygonLength - 1].y;
            
            // Loop through polygon vertices.
            for (i = 0; i < polygonLength; i++) {
                // End point of current polygon segment.
                float endX = polygon[i].x, endY = polygon[i].y;
                
                // Connect current and previous vertices.
                bool intersect = ((startY > pointY) != (endY > pointY)) 
                    && (pointX < (endX - startX) * (pointY - startY) / (endY - startY) + startX);
                
                if (intersect) inside = !inside;
                
                // Next line segment starts from this vertex.
                startX = endX;
                startY = endY;
            }
            
            return inside;
        }
        
        /// <summary>
        /// Calculate distance from point to polygon edge.
        /// </summary>
        private float DistanceToPolygonEdge(Vector2 point, List<Vector2> polygon) {
            float minDistance = float.MaxValue;
            
            for (int i = 0; i < polygon.Count; i++) {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                
                float distance = DistanceToLineSegment(point, a, b);
                minDistance = Mathf.Min(minDistance, distance);
            }
            
            return minDistance;
        }
        
        /// <summary>
        /// Calculate distance from point to line segment.
        /// </summary>
        private float DistanceToLineSegment(Vector2 point, Vector2 a, Vector2 b) {
            Vector2 ab = b - a;
            Vector2 ap = point - a;
            
            float ab2 = ab.x * ab.x + ab.y * ab.y;
            float ap_ab = ap.x * ab.x + ap.y * ab.y;
            
            float t = Mathf.Clamp01(ap_ab / ab2);
            
            Vector2 closest = a + t * ab;
            
            return Vector2.Distance(point, closest);
        }
        
        /// <summary>
        /// Check if polygon is clockwise.
        /// </summary>
        private bool IsPolygonClockwise(List<Vector2> polygon) {
            float sum = 0;
            
            for (int i = 0; i < polygon.Count; i++) {
                Vector2 v1 = polygon[i];
                Vector2 v2 = polygon[(i + 1) % polygon.Count];
                
                sum += (v2.x - v1.x) * (v2.y + v1.y);
            }
            
            return sum > 0;
        }
        
        /// <summary>
        /// Check if point is inside triangle.
        /// </summary>
        private bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) {
            // Compute vectors
            Vector3 v0 = c - a;
            Vector3 v1 = b - a;
            Vector3 v2 = p - a;
            
            // Compute dot products
            float dot00 = Vector3.Dot(v0, v0);
            float dot01 = Vector3.Dot(v0, v1);
            float dot02 = Vector3.Dot(v0, v2);
            float dot11 = Vector3.Dot(v1, v1);
            float dot12 = Vector3.Dot(v1, v2);
            
            // Compute barycentric coordinates
            float invDenom = 1.0f / (dot00 * dot11 - dot01 * dot01);
            float u = (dot11 * dot02 - dot01 * dot12) * invDenom;
            float v = (dot00 * dot12 - dot01 * dot02) * invDenom;
            
            // Check if point is in triangle
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }
        
        /// <summary>
        /// Check if terrain type is water.
        /// </summary>
        private bool IsWaterType(string terrainType) {
            terrainType = terrainType.ToLower();
            return terrainType == "water" || terrainType == "lake" || terrainType == "river" || 
                   terrainType == "ocean" || terrainType == "pond" || terrainType == "swamp" || 
                   terrainType == "marsh";
        }
        
        /// <summary>
        /// Check if terrain type is mountainous.
        /// </summary>
        private bool IsMountainType(string terrainType) {
            terrainType = terrainType.ToLower();
            return terrainType == "mountain" || terrainType == "hill" || terrainType == "peak" || 
                   terrainType == "ridge" || terrainType == "highland" || terrainType == "plateau";
        }
        
        /// <summary>
        /// Check if terrain type is valley.
        /// </summary>
        private bool IsValleyType(string terrainType) {
            terrainType = terrainType.ToLower();
            return terrainType == "valley" || terrainType == "canyon" || terrainType == "ravine" || 
                   terrainType == "gorge" || terrainType == "basin";
        }
        
        /// <summary>
        /// Check if terrain type is forest.
        /// </summary>
        private bool IsForestType(string terrainType) {
            terrainType = terrainType.ToLower();
            return terrainType == "forest" || terrainType == "woods" || terrainType == "jungle" || 
                   terrainType == "grove" || terrainType == "woodland";
        }
        
        /// <summary>
        /// Check if terrain type is grass.
        /// </summary>
        private bool IsGrassType(string terrainType) {
            terrainType = terrainType.ToLower();
            return terrainType == "grass" || terrainType == "grassland" || terrainType == "meadow" || 
                   terrainType == "plain" || terrainType == "field" || terrainType == "savanna";
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
        
        #region Job System Classes
        
        /// <summary>
        /// Job for converting pixels to height map.
        /// </summary>
        [BurstCompile]
        private struct PixelsToHeightMapJob : IJobParallelFor {
            [ReadOnly] public NativeArray<Color> pixels;
            [WriteOnly] public NativeArray<float> heightMap;
            public int heightmapResolution;
            public int textureWidth;
            public int textureHeight;
            
            public void Execute(int index) {
                int x = index % heightmapResolution;
                int y = index / heightmapResolution;
                
                // Calculate source pixel coordinates
                int sourceX = Mathf.FloorToInt(x * textureWidth / (float)heightmapResolution);
                int sourceY = Mathf.FloorToInt(y * textureHeight / (float)heightmapResolution);
                
                // Get pixel
                int pixelIndex = Mathf.Clamp(sourceY * textureWidth + sourceX, 0, pixels.Length - 1);
                Color pixel = pixels[pixelIndex];
                
                // Extract height from grayscale
                float height = (pixel.r + pixel.g + pixel.b) / 3f;
                
                // Store height
                heightMap[index] = height;
            }
        }
        
        /// <summary>
        /// Job for hydraulic erosion.
        /// </summary>
        [BurstCompile]
        private struct ErosionJob : IJob {
            public NativeArray<float> heights;
            [ReadOnly] public NativeArray<int> randomIndices;
            public int resolution;
            public float erosionStrength;
            public int iterations;
            
            public void Execute() {
                // Erosion parameters
                float inertia = 0.05f;
                float sedimentCapacityFactor = 4f;
                float minSedimentCapacity = 0.01f;
                float depositSpeed = 0.3f;
                float erodeSpeed = 0.3f * erosionStrength;
                
                // Apply erosion iterations
                for (int iter = 0; iter < iterations; iter++) {
                    // Random drop position from pre-generated random indices
                    int posX = randomIndices[iter * 2];
                    int posY = randomIndices[iter * 2 + 1];
                    
                    // Droplet properties
                    float dirX = 0f;
                    float dirY = 0f;
                    float speed = 1f;
                    float water = 1f;
                    float sediment = 0f;
                    
                    for (int lifetime = 0; lifetime < 30; lifetime++) {
                        // Get droplet position as integer and fractional parts
                        int nodeX = Mathf.FloorToInt(posX);
                        int nodeY = Mathf.FloorToInt(posY);
                        float fracX = posX - nodeX;
                        float fracY = posY - nodeY;
                        
                        // Ensure within bounds
                        if (nodeX < 0 || nodeX >= resolution - 1 || nodeY < 0 || nodeY >= resolution - 1) {
                            break;
                        }
                        
                        // Calculate droplet's height and direction of flow
                        float heightNW = heights[nodeY * resolution + nodeX];
                        float heightNE = heights[nodeY * resolution + nodeX + 1];
                        float heightSW = heights[(nodeY + 1) * resolution + nodeX];
                        float heightSE = heights[(nodeY + 1) * resolution + nodeX + 1];
                        
                        // Calculate height at droplet position using bilinear interpolation
                        float height = heightNW * (1 - fracX) * (1 - fracY) +
                                      heightNE * fracX * (1 - fracY) +
                                      heightSW * (1 - fracX) * fracY +
                                      heightSE * fracX * fracY;
                        
                        // Calculate gradient
                        float gradientX = (heightNE - heightNW) * (1 - fracY) + (heightSE - heightSW) * fracY;
                        float gradientY = (heightSW - heightNW) * (1 - fracX) + (heightSE - heightNE) * fracX;
                        
                        // Update droplet direction
                        dirX = dirX * inertia - gradientX * (1 - inertia);
                        dirY = dirY * inertia - gradientY * (1 - inertia);
                        
                        // Normalize direction
                        float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                        if (len != 0) {
                            dirX /= len;
                            dirY /= len;
                        }
                        
                        // Update position
                        posX += dirX;
                        posY += dirY;
                        
                        // Stop if droplet has flowed off map
                        if (posX < 0 || posX >= resolution - 1 || posY < 0 || posY >= resolution - 1) {
                            break;
                        }
                        
                        // Get new height and calculate height difference
                        nodeX = Mathf.FloorToInt(posX);
                        nodeY = Mathf.FloorToInt(posY);
                        fracX = posX - nodeX;
                        fracY = posY - nodeY;
                        
                        int idx = nodeY * resolution + nodeX;
                        int idxE = nodeY * resolution + nodeX + 1;
                        int idxS = (nodeY + 1) * resolution + nodeX;
                        int idxSE = (nodeY + 1) * resolution + nodeX + 1;
                        
                        heightNW = heights[idx];
                        heightNE = heights[idxE];
                        heightSW = heights[idxS];
                        heightSE = heights[idxSE];
                        
                        float newHeight = heightNW * (1 - fracX) * (1 - fracY) +
                                         heightNE * fracX * (1 - fracY) +
                                         heightSW * (1 - fracX) * fracY +
                                         heightSE * fracX * fracY;
                        
                        float heightDiff = newHeight - height;
                        
                        // Calculate sediment capacity
                        float sedimentCapacity = Mathf.Max(minSedimentCapacity, sedimentCapacityFactor * heightDiff);
                        
                        // If flowing uphill, deposit sediment
                        if (heightDiff > 0) {
                            float depositAmount = Mathf.Min(sediment, heightDiff);
                            sediment -= depositAmount;
                            
                            // Distribute to nodes
                            heights[idx] += depositAmount * (1 - fracX) * (1 - fracY);
                            heights[idxE] += depositAmount * fracX * (1 - fracY);
                            heights[idxS] += depositAmount * (1 - fracX) * fracY;
                            heights[idxSE] += depositAmount * fracX * fracY;
                        }
                        else {
                            // Erode and carry sediment
                            float erosionAmount = Mathf.Min((sedimentCapacity - sediment) * erodeSpeed, -heightDiff);
                            
                            heights[idx] -= erosionAmount * (1 - fracX) * (1 - fracY);
                            heights[idxE] -= erosionAmount * fracX * (1 - fracY);
                            heights[idxS] -= erosionAmount * (1 - fracX) * fracY;
                            heights[idxSE] -= erosionAmount * fracX * fracY;
                            
                            sediment += erosionAmount;
                        }
                        
                        // Update water content
                        water = water * 0.99f;
                        if (water <= 0.01f) break;
                    }
                }
            }
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