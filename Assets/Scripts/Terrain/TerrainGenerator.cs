using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Traversify.AI;
using Traversify.Core;

namespace Traversify {
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

        #region Configuration
        [Header("Terrain Dimensions")]
        public Vector3 terrainSize = new Vector3(1000, 100, 1000);
        public int heightmapResolution = 513;
        public int detailResolution = 1024;
        public int alphamapResolution = 512;
        public int baseTextureResolution = 1024;
        
        [Header("Generation Settings")]
        public TerrainGenerationMode generationMode = TerrainGenerationMode.Hybrid;
        public bool useGPUAcceleration = true;
        public bool useMultithreading = true;
        public int workerThreads = 4;
        public float generationTimeout = 300f;
        
        [Header("Noise Settings")]
        public NoiseType primaryNoiseType = NoiseType.Simplex;
        public float noiseScale = 0.01f;
        public int octaves = 6;
        [Range(0f, 1f)] public float persistence = 0.5f;
        [Range(1f, 4f)] public float lacunarity = 2f;
        public Vector2 noiseOffset = Vector2.zero;
        public int seed = 42;
        public AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1);
        
        [Header("Advanced Terrain Features")]
        public bool generateRivers = true;
        public bool generateRoads = true;
        public bool generateCliffs = true;
        public bool generateCaves = false;
        [Range(0f, 1f)] public float riverDensity = 0.1f;
        [Range(0f, 1f)] public float roadDensity = 0.05f;
        public float minRiverWidth = 5f;
        public float maxRiverWidth = 20f;
        public float riverDepth = 5f;
        public AnimationCurve riverProfile = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        [Header("Water Settings")]
        public bool generateWater = true;
        [Range(0f, 1f)] public float waterLevel = 0.3f;
        public GameObject waterPrefab;
        public Material waterMaterial;
        public bool dynamicWater = true;
        public float waveHeight = 0.5f;
        public float waveSpeed = 1f;
        public Color shallowWaterColor = new Color(0.2f, 0.6f, 0.8f, 0.8f);
        public Color deepWaterColor = new Color(0.1f, 0.3f, 0.6f, 0.9f);
        public float waterTransparency = 0.6f;
        
        [Header("Erosion Settings")]
        public bool applyErosion = true;
        public ErosionType erosionType = ErosionType.Hydraulic;
        public int erosionIterations = 50000;
        public float erosionBrushRadius = 3f;
        public float sedimentCapacityConstant = 4f;
        public float depositionSpeed = 0.3f;
        public float evaporationSpeed = 0.01f;
        public float gravity = 4f;
        public float startSpeed = 1f;
        public float startWater = 1f;
        [Range(0f, 1f)] public float inertia = 0.05f;
        
        [Header("Biome Settings")]
        public bool generateBiomes = true;
        public List<BiomeConfig> biomes = new List<BiomeConfig>();
        public float biomeBlendDistance = 50f;
        public bool useTemperatureMap = true;
        public bool useMoistureMap = true;
        public Gradient temperatureGradient;
        public Gradient moistureGradient;
        
        [Header("Vegetation")]
        public bool generateVegetation = true;
        public List<VegetationLayer> vegetationLayers = new List<VegetationLayer>();
        public float vegetationDensity = 0.5f;
        public bool useWindZones = true;
        public float windStrength = 1f;
        public Vector3 windDirection = new Vector3(1, 0, 0.5f);
        
        [Header("Pathfinding")]
        public bool generateNavMesh = true;
        // NavMeshSurface requires AI Navigation package - temporarily commented out
        // public NavMeshSurface navMeshSurface;
        public float agentRadius = 0.5f;
        public float agentHeight = 2f;
        public float maxSlope = 45f;
        public float stepHeight = 0.4f;
        public bool buildOffMeshLinks = true;
        public float jumpDistance = 5f;
        
        [Header("Optimization")]
        public bool useLOD = true;
        public float[] lodDistances = { 100f, 300f, 600f, 1000f };
        public bool useTerrainStreaming = false;
        public float streamingDistance = 2000f;
        public int chunkSize = 256;
        public bool cacheTerrainData = true;
        public int maxCachedChunks = 16;
        
        [Header("Debug")]
        public bool showDebugInfo = false;
        public bool visualizeFlowField = false;
        public bool visualizeBiomes = false;
        public bool visualizeErosion = false;
        public TraversifyDebugger debugger;
        #endregion

        #region Enums and Data Structures
        public enum TerrainGenerationMode {
            Procedural,
            FromHeightmap,
            FromAnalysis,
            Hybrid
        }
        
        public enum NoiseType {
            Perlin,
            Simplex,
            Worley,
            Voronoi,
            Ridged,
            Billow
        }
        
        public enum ErosionType {
            None,
            Thermal,
            Hydraulic,
            Combined
        }
        
        [Serializable]
        public class BiomeConfig {
            public string name = "Biome";
            public float minHeight = 0f;
            public float maxHeight = 1f;
            public float minTemperature = 0f;
            public float maxTemperature = 1f;
            public float minMoisture = 0f;
            public float maxMoisture = 1f;
            public TerrainLayer[] terrainLayers;
            public Color mapColor = Color.green;
            public float noiseScale = 0.05f;
            public float noiseStrength = 0.1f;
            public VegetationDensity vegetationDensity = VegetationDensity.Medium;
        }
        
        public enum VegetationDensity {
            None, Sparse, Medium, Dense, VeryDense
        }
        
        [Serializable]
        public class VegetationLayer {
            public string name = "Vegetation";
            public GameObject[] prefabs;
            public float minHeight = 0f;
            public float maxHeight = 1f;
            public float minSlope = 0f;
            public float maxSlope = 90f;
            public float density = 1f;
            public float minScale = 0.8f;
            public float maxScale = 1.2f;
            public bool randomRotation = true;
            public bool alignToNormal = false;
            public float positionJitter = 0.5f;
            public int prototypeIndex = -1;
        }
        
        [Serializable]
        public class TerrainModification {
            public Rect bounds;
            public Texture2D heightMap;
            public float baseHeight;
            public string terrainType;
            public string description;
            public float blendRadius;
            public float slope;
            public float roughness;
            public ModificationType type = ModificationType.Additive;
            public AnimationCurve falloffCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        }
        
        public enum ModificationType {
            Replace, Additive, Multiply, Smooth, Flatten
        }
        
        [Serializable]
        public class PathNode {
            public Vector3 position;
            public float width;
            public PathType type;
            public List<PathNode> connections;
            public float elevation;
            public float curvature;
            public bool isBridge;
            public bool isTunnel;
        }
        
        public enum PathType {
            Road, River, Trail, Railroad, Highway
        }
        
        [Serializable]
        public class TerrainChunk {
            public Vector2Int coordinate;
            public UnityEngine.Terrain terrain;
            public LODGroup lodGroup;
            public float[,] heightmapData;
            public int[,,] alphamapData;
            public bool isLoaded;
            public float lastAccessTime;
            public Bounds bounds;
        }
        
        private class FlowData {
            public Vector2 position;
            public Vector2 direction;
            public float velocity;
            public float water;
            public float sediment;
            public float lifetime;
        }
        #endregion

        #region Private Fields
        private UnityEngine.Terrain _terrain;
        private TerrainData _terrainData;
        private TerrainCollider _terrainCollider;
        private float[,] _heightmap;
        private float[,] _originalHeightmap;
        private float[,] _temperatureMap;
        private float[,] _moistureMap;
        private int[,] _biomeMap;
        private Vector2[,] _flowField;
        private List<PathNode> _pathNetwork;
        private Dictionary<Vector2Int, TerrainChunk> _terrainChunks;
        private Queue<TerrainModification> _modificationQueue;
        private GameObject _waterObject;
        private ComputeShader _erosionCompute;
        private ComputeShader _noiseCompute;
        private RenderTexture _heightmapRT;
        private RenderTexture _normalMapRT;
        private bool _isGenerating;
        private Coroutine _generationCoroutine;
        private JobHandle _currentJob;
        private NativeArray<float> _heightsNative;
        private NativeArray<float2> _gradientsNative;
        #endregion

        #region Initialization
        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            
            Initialize();
        }
        
        private void Initialize() {
            // Get components
            _terrain = GetComponent<Terrain>();
            if (_terrain == null) _terrain = gameObject.AddComponent<Terrain>();
            
            _terrainCollider = GetComponent<TerrainCollider>();
            if (_terrainCollider == null) _terrainCollider = gameObject.AddComponent<TerrainCollider>();
            
            debugger = FindObjectOfType<TraversifyDebugger>();
            if (debugger == null) {
                debugger = new GameObject("TraversifyDebugger").AddComponent<TraversifyDebugger>();
            }
            
            // Initialize data structures
            _pathNetwork = new List<PathNode>();
            _terrainChunks = new Dictionary<Vector2Int, TerrainChunk>();
            _modificationQueue = new Queue<TerrainModification>();
            
            // Load compute shaders if GPU acceleration is enabled
            if (useGPUAcceleration && SystemInfo.supportsComputeShaders) {
                LoadComputeShaders();
            }
            
            // Create default biomes if none exist
            if (biomes.Count == 0) {
                CreateDefaultBiomes();
            }
            
            // Initialize gradients
            InitializeGradients();
            
            debugger?.Log("TerrainGenerator initialized", LogCategory.Terrain);
        }
        
        private void LoadComputeShaders() {
            _erosionCompute = Resources.Load<ComputeShader>("Shaders/TerrainErosion");
            _noiseCompute = Resources.Load<ComputeShader>("Shaders/TerrainNoise");
            
            if (_erosionCompute == null) {
                debugger?.LogWarning("Erosion compute shader not found", LogCategory.Terrain);
                useGPUAcceleration = false;
            }
            
            if (_noiseCompute == null) {
                debugger?.LogWarning("Noise compute shader not found", LogCategory.Terrain);
            }
        }
        
        private void CreateDefaultBiomes() {
            biomes = new List<BiomeConfig> {
                new BiomeConfig {
                    name = "Ocean",
                    minHeight = 0f,
                    maxHeight = 0.3f,
                    mapColor = new Color(0.1f, 0.3f, 0.6f),
                    vegetationDensity = VegetationDensity.None
                },
                new BiomeConfig {
                    name = "Beach",
                    minHeight = 0.3f,
                    maxHeight = 0.35f,
                    mapColor = new Color(0.9f, 0.8f, 0.6f),
                    vegetationDensity = VegetationDensity.Sparse
                },
                new BiomeConfig {
                    name = "Grassland",
                    minHeight = 0.35f,
                    maxHeight = 0.6f,
                    minMoisture = 0.3f,
                    maxMoisture = 0.7f,
                    mapColor = new Color(0.3f, 0.7f, 0.2f),
                    vegetationDensity = VegetationDensity.Medium
                },
                new BiomeConfig {
                    name = "Forest",
                    minHeight = 0.35f,
                    maxHeight = 0.7f,
                    minMoisture = 0.5f,
                    maxMoisture = 1f,
                    mapColor = new Color(0.1f, 0.4f, 0.1f),
                    vegetationDensity = VegetationDensity.Dense
                },
                new BiomeConfig {
                    name = "Mountain",
                    minHeight = 0.7f,
                    maxHeight = 0.9f,
                    mapColor = new Color(0.5f, 0.4f, 0.3f),
                    vegetationDensity = VegetationDensity.Sparse
                },
                new BiomeConfig {
                    name = "Snow",
                    minHeight = 0.9f,
                    maxHeight = 1f,
                    mapColor = Color.white,
                    vegetationDensity = VegetationDensity.None
                }
            };
        }
        
        private void InitializeGradients() {
            if (temperatureGradient == null) {
                temperatureGradient = new Gradient();
                var colorKeys = new GradientColorKey[] {
                    new GradientColorKey(new Color(0.2f, 0.3f, 0.8f), 0f), // Cold
                    new GradientColorKey(new Color(0.3f, 0.7f, 0.3f), 0.5f), // Temperate
                    new GradientColorKey(new Color(0.9f, 0.7f, 0.2f), 1f) // Hot
                };
                temperatureGradient.SetKeys(colorKeys, new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            }
            
            if (moistureGradient == null) {
                moistureGradient = new Gradient();
                var colorKeys = new GradientColorKey[] {
                    new GradientColorKey(new Color(0.9f, 0.8f, 0.6f), 0f), // Dry
                    new GradientColorKey(new Color(0.3f, 0.6f, 0.3f), 0.5f), // Normal
                    new GradientColorKey(new Color(0.1f, 0.3f, 0.5f), 1f) // Wet
                };
                moistureGradient.SetKeys(colorKeys, new GradientAlphaKey[] {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            }
        }
        #endregion

        #region Public API
        public IEnumerator GenerateTerrain(
            TerrainGenerationRequest request,
            Action<UnityEngine.Terrain> onComplete,
            Action<string> onError,
            Action<float, string> onProgress
        ) {
            if (_isGenerating) {
                onError?.Invoke("Terrain generation already in progress");
                yield break;
            }
            
            _isGenerating = true;
            debugger?.StartTimer("TerrainGeneration");
            
            try {
                // Step 1: Setup terrain data
                onProgress?.Invoke(0.05f, "Initializing terrain data");
                SetupTerrainData(request);
                
                // Step 2: Generate base heightmap
                onProgress?.Invoke(0.1f, "Generating base heightmap");
                yield return GenerateBaseHeightmap(request, onProgress);
                
                // Step 3: Apply terrain features
                if (request.analysisResults != null) {
                    onProgress?.Invoke(0.3f, "Applying terrain features from analysis");
                    yield return ApplyAnalysisResults(request.analysisResults, onProgress);
                }
                
                // Step 4: Generate biomes
                if (generateBiomes) {
                    onProgress?.Invoke(0.4f, "Generating biomes");
                    yield return GenerateBiomes(onProgress);
                }
                
                // Step 5: Apply erosion
                if (applyErosion) {
                    onProgress?.Invoke(0.5f, "Simulating erosion");
                    yield return SimulateErosion(onProgress);
                }
                
                // Step 6: Generate water features
                if (generateWater || generateRivers) {
                    onProgress?.Invoke(0.6f, "Creating water features");
                    yield return GenerateWaterFeatures(onProgress);
                }
                
                // Step 7: Generate paths
                if (generateRoads || request.pathNodes != null) {
                    onProgress?.Invoke(0.7f, "Generating paths");
                    yield return GeneratePaths(request.pathNodes, onProgress);
                }
                
                // Step 8: Apply final smoothing
                onProgress?.Invoke(0.8f, "Applying final touches");
                yield return FinalizeHeightmap(onProgress);
                
                // Step 9: Generate vegetation
                if (generateVegetation) {
                    onProgress?.Invoke(0.85f, "Placing vegetation");
                    yield return GenerateVegetation(onProgress);
                }
                
                // Step 10: Build navigation mesh
                if (generateNavMesh) {
                    onProgress?.Invoke(0.9f, "Building navigation mesh");
                    yield return BuildNavigationMesh();
                }
                
                // Step 11: Setup LODs if enabled
                if (useLOD) {
                    onProgress?.Invoke(0.95f, "Setting up LODs");
                    SetupTerrainLODs();
                }
                
                // Complete
                ApplyTerrainData();
                float generationTime = debugger?.StopTimer("TerrainGeneration") ?? 0f;
                debugger?.Log($"Terrain generation completed in {generationTime:F2}s", LogCategory.Terrain);
                
                onProgress?.Invoke(1f, "Terrain generation complete");
                onComplete?.Invoke(_terrain);
                
            } catch (Exception ex) {
                debugger?.LogError($"Terrain generation failed: {ex.Message}", LogCategory.Terrain);
                onError?.Invoke(ex.Message);
            } finally {
                _isGenerating = false;
                CleanupResources();
            }
        }
        
        public IEnumerator ApplyTerrainModifications(
            List<TerrainModification> modifications,
            Texture2D sourceImage = null
        ) {
            debugger?.Log($"Applying {modifications.Count} terrain modifications", LogCategory.Terrain);
            
            foreach (var mod in modifications) {
                _modificationQueue.Enqueue(mod);
            }
            
            while (_modificationQueue.Count > 0) {
                var mod = _modificationQueue.Dequeue();
                yield return ApplyModification(mod, sourceImage);
            }
            
            // Update terrain
            _terrain.terrainData.SetHeights(0, 0, _heightmap);
            _terrain.Flush();
        }
        
        public void SetWaterLevel(float level) {
            waterLevel = Mathf.Clamp01(level);
            if (_waterObject != null) {
                UpdateWaterObject();
            }
        }
        
        public float SampleHeight(Vector3 worldPosition) {
            Vector3 terrainPos = worldPosition - _terrain.transform.position;
            Vector3 normalizedPos = new Vector3(
                terrainPos.x / _terrainData.size.x,
                0,
                terrainPos.z / _terrainData.size.z
            );
            
            return _terrain.SampleHeight(worldPosition);
        }
        
        public Vector3 GetTerrainNormal(Vector3 worldPosition) {
            Vector3 terrainPos = worldPosition - _terrain.transform.position;
            float x = terrainPos.x / _terrainData.size.x;
            float z = terrainPos.z / _terrainData.size.z;
            
            return _terrainData.GetInterpolatedNormal(x, z);
        }
        
        public BiomeConfig GetBiomeAtPosition(Vector3 worldPosition) {
            if (_biomeMap == null || biomes.Count == 0) return null;
            
            Vector3 terrainPos = worldPosition - _terrain.transform.position;
            int x = Mathf.RoundToInt(terrainPos.x / _terrainData.size.x * (_biomeMap.GetLength(0) - 1));
            int z = Mathf.RoundToInt(terrainPos.z / _terrainData.size.z * (_biomeMap.GetLength(1) - 1));
            
            x = Mathf.Clamp(x, 0, _biomeMap.GetLength(0) - 1);
            z = Mathf.Clamp(z, 0, _biomeMap.GetLength(1) - 1);
            
            int biomeIndex = _biomeMap[z, x];
            return biomeIndex >= 0 && biomeIndex < biomes.Count ? biomes[biomeIndex] : null;
        }
        #endregion

        #region Terrain Data Setup
        private void SetupTerrainData(TerrainGenerationRequest request) {
            // Create or update terrain data
            if (_terrainData == null) {
                _terrainData = new TerrainData();
                _terrain.terrainData = _terrainData;
                _terrainCollider.terrainData = _terrainData;
            }
            
            // Apply dimensions
            if (request.size != Vector3.zero) {
                terrainSize = request.size;
            }
            if (request.resolution > 0) {
                heightmapResolution = request.resolution;
            }
            
            // Validate resolution (must be power of 2 + 1)
            heightmapResolution = Mathf.ClosestPowerOfTwo(heightmapResolution - 1) + 1;
            
            // Configure terrain data
            _terrainData.heightmapResolution = heightmapResolution;
            _terrainData.size = terrainSize;
            _terrainData.name = "GeneratedTerrain";
            
            // Configure detail settings
            _terrainData.SetDetailResolution(detailResolution, 8);
            
            // Setup terrain layers
            SetupTerrainLayers();
            
            // Initialize heightmap array
            _heightmap = new float[heightmapResolution, heightmapResolution];
            _originalHeightmap = new float[heightmapResolution, heightmapResolution];
            
            debugger?.Log($"Terrain data initialized: {terrainSize} at {heightmapResolution}x{heightmapResolution}", 
                LogCategory.Terrain);
        }
        
        private void SetupTerrainLayers() {
            var layers = new List<TerrainLayer>();
            
            // Create default layers if none exist
            if (biomes.Count > 0) {
                foreach (var biome in biomes) {
                    if (biome.terrainLayers != null) {
                        layers.AddRange(biome.terrainLayers);
                    }
                }
            }
            
            // Add default layers if none were added
            if (layers.Count == 0) {
                layers.Add(CreateDefaultLayer("Grass", new Color(0.3f, 0.6f, 0.2f)));
                layers.Add(CreateDefaultLayer("Rock", new Color(0.5f, 0.5f, 0.5f)));
                layers.Add(CreateDefaultLayer("Sand", new Color(0.9f, 0.8f, 0.6f)));
                layers.Add(CreateDefaultLayer("Snow", Color.white));
            }
            
            _terrainData.terrainLayers = layers.ToArray();
        }
        
        private TerrainLayer CreateDefaultLayer(string name, Color color) {
            var layer = new TerrainLayer();
            layer.name = name;
            
            // Create a simple colored texture
            var tex = new Texture2D(512, 512);
            var pixels = new Color[512 * 512];
            
            // Add some noise for variation
            for (int i = 0; i < pixels.Length; i++) {
                float noise = UnityEngine.Random.Range(0.9f, 1.1f);
                pixels[i] = color * noise;
            }
            
            tex.SetPixels(pixels);
            tex.Apply();
            
            layer.diffuseTexture = tex;
            layer.tileSize = new Vector2(15, 15);
            
            return layer;
        }
        #endregion

        #region Heightmap Generation
        private IEnumerator GenerateBaseHeightmap(
            TerrainGenerationRequest request,
            Action<float, string> onProgress
        ) {
            switch (generationMode) {
                case TerrainGenerationMode.Procedural:
                    yield return GenerateProceduralHeightmap(onProgress);
                    break;
                    
                case TerrainGenerationMode.FromHeightmap:
                    if (request.heightmapTexture != null) {
                        yield return ImportHeightmap(request.heightmapTexture, onProgress);
                    } else {
                        yield return GenerateProceduralHeightmap(onProgress);
                    }
                    break;
                    
                case TerrainGenerationMode.FromAnalysis:
                    if (request.analysisResults != null) {
                        yield return GenerateFromAnalysis(request.analysisResults, onProgress);
                    } else {
                        yield return GenerateProceduralHeightmap(onProgress);
                    }
                    break;
                    
                case TerrainGenerationMode.Hybrid:
                    yield return GenerateProceduralHeightmap(onProgress);
                    if (request.analysisResults != null) {
                        yield return BlendWithAnalysis(request.analysisResults, onProgress);
                    }
                    break;
            }
            
            // Store original heightmap
            Array.Copy(_heightmap, _originalHeightmap, _heightmap.Length);
        }
        
        private IEnumerator GenerateProceduralHeightmap(Action<float, string> onProgress) {
            if (useGPUAcceleration && _noiseCompute != null) {
                yield return GenerateHeightmapGPU(onProgress);
            } else if (useMultithreading) {
                yield return GenerateHeightmapMultithreaded(onProgress);
            } else {
                yield return GenerateHeightmapSingleThreaded(onProgress);
            }
        }
        
        private IEnumerator GenerateHeightmapGPU(Action<float, string> onProgress) {
            // Create render texture
            _heightmapRT = new RenderTexture(heightmapResolution, heightmapResolution, 0, RenderTextureFormat.RFloat);
            _heightmapRT.enableRandomWrite = true;
            _heightmapRT.Create();
            
            // Setup compute shader
            int kernel = _noiseCompute.FindKernel("GenerateNoise");
            _noiseCompute.SetTexture(kernel, "_HeightmapTexture", _heightmapRT);
            _noiseCompute.SetInt("_Resolution", heightmapResolution);
            _noiseCompute.SetFloat("_Scale", noiseScale);
            _noiseCompute.SetInt("_Octaves", octaves);
            _noiseCompute.SetFloat("_Persistence", persistence);
            _noiseCompute.SetFloat("_Lacunarity", lacunarity);
            _noiseCompute.SetVector("_Offset", new Vector4(noiseOffset.x, noiseOffset.y, 0, 0));
            _noiseCompute.SetInt("_Seed", seed);
            
            // Dispatch
            int threadGroups = Mathf.CeilToInt(heightmapResolution / 8f);
            _noiseCompute.Dispatch(kernel, threadGroups, threadGroups, 1);
            
            yield return null;
            
            // Read back to CPU
            yield return ReadHeightmapFromGPU();
            
            onProgress?.Invoke(0.2f, "GPU heightmap generation complete");
        }
        
        private IEnumerator GenerateHeightmapMultithreaded(Action<float, string> onProgress) {
            int resolution = heightmapResolution;
            _heightsNative = new NativeArray<float>(resolution * resolution, Allocator.TempJob);
            
            // Create job
            var noiseJob = new ParallelNoiseJob {
                heights = _heightsNative,
                resolution = resolution,
                scale = noiseScale,
                octaves = octaves,
                persistence = persistence,
                lacunarity = lacunarity,
                offset = noiseOffset,
                seed = seed,
                noiseType = (int)primaryNoiseType
            };
            
            // Schedule job
            _currentJob = noiseJob.Schedule(resolution * resolution, 64);
            
            // Wait for completion with progress
            while (!_currentJob.IsCompleted) {
                onProgress?.Invoke(0.15f, "Generating heightmap (multithreaded)");
                yield return null;
            }
            
            _currentJob.Complete();
            
            // Copy results
            for (int z = 0; z < resolution; z++) {
                for (int x = 0; x < resolution; x++) {
                    _heightmap[z, x] = _heightsNative[z * resolution + x];
                }
            }
            
            _heightsNative.Dispose();
            onProgress?.Invoke(0.2f, "Multithreaded heightmap generation complete");
        }
        
        private IEnumerator GenerateHeightmapSingleThreaded(Action<float, string> onProgress) {
            System.Random prng = new System.Random(seed);
            Vector2[] octaveOffsets = new Vector2[octaves];
            
            for (int i = 0; i < octaves; i++) {
                float offsetX = prng.Next(-100000, 100000) + noiseOffset.x;
                float offsetY = prng.Next(-100000, 100000) - noiseOffset.y;
                octaveOffsets[i] = new Vector2(offsetX, offsetY);
            }
            
            float maxNoiseHeight = float.MinValue;
            float minNoiseHeight = float.MaxValue;
            
            // Generate noise values
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    float amplitude = 1;
                    float frequency = 1;
                    float noiseHeight = 0;
                    
                    for (int i = 0; i < octaves; i++) {
                        float sampleX = (x - heightmapResolution / 2f) * noiseScale * frequency + octaveOffsets[i].x;
                        float sampleZ = (z - heightmapResolution / 2f) * noiseScale * frequency + octaveOffsets[i].y;
                        
                        float noiseValue = GetNoiseValue(sampleX, sampleZ);
                        noiseHeight += noiseValue * amplitude;
                        
                        amplitude *= persistence;
                        frequency *= lacunarity;
                    }
                    
                    maxNoiseHeight = Mathf.Max(maxNoiseHeight, noiseHeight);
                    minNoiseHeight = Mathf.Min(minNoiseHeight, noiseHeight);
                    _heightmap[z, x] = noiseHeight;
                }
                
                if (z % 50 == 0) {
                    onProgress?.Invoke(0.1f + (z / (float)heightmapResolution) * 0.1f, "Generating base terrain");
                    yield return null;
                }
            }
            
            // Normalize heights
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    float normalizedHeight = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, _heightmap[z, x]);
                    _heightmap[z, x] = heightCurve.Evaluate(normalizedHeight);
                }
            }
        }
        
        private float GetNoiseValue(float x, float z) {
            switch (primaryNoiseType) {
                case NoiseType.Perlin:
                    return Mathf.PerlinNoise(x, z) * 2f - 1f;
                    
                case NoiseType.Simplex:
                    return SimplexNoise(x, z);
                    
                case NoiseType.Worley:
                    return WorleyNoise(x, z);
                    
                case NoiseType.Voronoi:
                    return VoronoiNoise(x, z);
                    
                case NoiseType.Ridged:
                    float ridge = Mathf.Abs(SimplexNoise(x, z));
                    return 1f - ridge;
                    
                case NoiseType.Billow:
                    return Mathf.Abs(SimplexNoise(x, z));
                    
                default:
                    return Mathf.PerlinNoise(x, z) * 2f - 1f;
            }
        }
        
        private float SimplexNoise(float x, float y) {
            // Simplified 2D simplex noise implementation
            const float F2 = 0.5f * (Mathf.Sqrt(3f) - 1f);
            const float G2 = (3f - Mathf.Sqrt(3f)) / 6f;
            
            float s = (x + y) * F2;
            int i = Mathf.FloorToInt(x + s);
            int j = Mathf.FloorToInt(y + s);
            
            float t = (i + j) * G2;
            float X0 = i - t;
            float Y0 = j - t;
            float x0 = x - X0;
            float y0 = y - Y0;
            
            int i1, j1;
            if (x0 > y0) {
                i1 = 1; j1 = 0;
            } else {
                i1 = 0; j1 = 1;
            }
            
            float x1 = x0 - i1 + G2;
            float y1 = y0 - j1 + G2;
            float x2 = x0 - 1f + 2f * G2;
            float y2 = y0 - 1f + 2f * G2;
            
            int gi0 = Hash(i, j) % 8;
            int gi1 = Hash(i + i1, j + j1) % 8;
            int gi2 = Hash(i + 1, j + 1) % 8;
            
            float n0 = Contribution(gi0, x0, y0);
            float n1 = Contribution(gi1, x1, y1);
            float n2 = Contribution(gi2, x2, y2);
            
            return 70f * (n0 + n1 + n2);
        }
        
        private float WorleyNoise(float x, float y) {
            Vector2 point = new Vector2(x, y);
            float minDist = float.MaxValue;
            
            int cellX = Mathf.FloorToInt(x);
            int cellY = Mathf.FloorToInt(y);
            
            // Check surrounding cells
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    int cx = cellX + dx;
                    int cy = cellY + dy;
                    
                    // Generate feature point in cell
                    System.Random cellRng = new System.Random(Hash(cx, cy));
                    Vector2 featurePoint = new Vector2(
                        cx + (float)cellRng.NextDouble(),
                        cy + (float)cellRng.NextDouble()
                    );
                    
                    float dist = Vector2.Distance(point, featurePoint);
                    minDist = Mathf.Min(minDist, dist);
                }
            }
            
            return 1f - Mathf.Clamp01(minDist);
        }
        
        private float VoronoiNoise(float x, float y) {
            // Similar to Worley but returns cell ID normalized
            Vector2 point = new Vector2(x, y);
            float minDist = float.MaxValue;
            int closestCell = 0;
            
            int cellX = Mathf.FloorToInt(x);
            int cellY = Mathf.FloorToInt(y);
            
            for (int dx = -1; dx <= 1; dx++) {
                for (int dy = -1; dy <= 1; dy++) {
                    int cx = cellX + dx;
                    int cy = cellY + dy;
                    int cellHash = Hash(cx, cy);
                    
                    System.Random cellRng = new System.Random(cellHash);
                    Vector2 featurePoint = new Vector2(
                        cx + (float)cellRng.NextDouble(),
                        cy + (float)cellRng.NextDouble()
                    );
                    
                    float dist = Vector2.Distance(point, featurePoint);
                    if (dist < minDist) {
                        minDist = dist;
                        closestCell = cellHash;
                    }
                }
            }
            
            return (closestCell % 256) / 255f;
        }
        
        private int Hash(int x, int y) {
            int h = seed;
            h = (h ^ x) * 0x27d4eb2d;
            h = (h ^ y) * 0x27d4eb2d;
            return h;
        }
        
        private float Contribution(int gi, float x, float y) {
            float t = 0.5f - x * x - y * y;
            if (t < 0) return 0f;
            
            t *= t;
            return t * t * Dot(GetGradient(gi), x, y);
        }
        
        private Vector2 GetGradient(int index) {
            float angle = index / 8f * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
        
        private float Dot(Vector2 g, float x, float y) {
            return g.x * x + g.y * y;
        }
        #endregion

        #region Analysis Integration
        private IEnumerator ApplyAnalysisResults(
            AnalysisResults results,
            Action<float, string> onProgress
        ) {
            if (results.terrainFeatures == null || results.terrainFeatures.Count == 0) {
                yield break;
            }
            
            int processed = 0;
            int total = results.terrainFeatures.Count;
            
            foreach (var feature in results.terrainFeatures) {
                // Convert feature to terrain modification
                var modification = new TerrainModification {
                    bounds = feature.boundingBox,
                    heightMap = feature.segmentMask,
                    baseHeight = feature.elevation / terrainSize.y,
                    terrainType = feature.label,
                    type = ModificationType.Additive,
                    blendRadius = 10f
                };
                
                yield return ApplyModification(modification, null);
                
                processed++;
                float progress = 0.3f + (processed / (float)total) * 0.1f;
                onProgress?.Invoke(progress, $"Applied feature: {feature.label}");
            }
        }
        
        private IEnumerator BlendWithAnalysis(
            AnalysisResults results,
            Action<float, string> onProgress
        ) {
            if (results.heightMap == null) yield break;
            
            // Blend analysis heightmap with procedural
            float[,] analysisHeights = TextureToHeightmap(results.heightMap);
            
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    float procedural = _heightmap[z, x];
                    float analysis = analysisHeights[z, x];
                    
                    // Blend based on confidence or other factors
                    _heightmap[z, x] = Mathf.Lerp(procedural, analysis, 0.5f);
                }
                
                if (z % 50 == 0) {
                    onProgress?.Invoke(0.25f + (z / (float)heightmapResolution) * 0.05f, "Blending with analysis");
                    yield return null;
                }
            }
        }
        
        private IEnumerator GenerateFromAnalysis(
            AnalysisResults results,
            Action<float, string> onProgress
        ) {
            if (results.heightMap != null) {
                _heightmap = TextureToHeightmap(results.heightMap);
            } else {
                // Generate procedural as fallback
                yield return GenerateProceduralHeightmap(onProgress);
            }
        }
        
        private float[,] TextureToHeightmap(Texture2D texture) {
            int width = heightmapResolution;
            int height = heightmapResolution;
            float[,] heights = new float[height, width];
            
            // Resize texture if needed
            RenderTexture rt = RenderTexture.GetTemporary(width, height);
            Graphics.Blit(texture, rt);
            
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D resized = new Texture2D(width, height, TextureFormat.RFloat, false);
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            
            // Convert to heightmap
            Color[] pixels = resized.GetPixels();
            for (int z = 0; z < height; z++) {
                for (int x = 0; x < width; x++) {
                    heights[z, x] = pixels[z * width + x].r;
                }
            }
            
            Destroy(resized);
            return heights;
        }
        #endregion

        #region Terrain Modification
        private IEnumerator ApplyModification(
            TerrainModification mod,
            Texture2D sourceImage
        ) {
            // Calculate affected area in heightmap coordinates
            int startX = Mathf.RoundToInt(mod.bounds.x / terrainSize.x * heightmapResolution);
            int startZ = Mathf.RoundToInt(mod.bounds.y / terrainSize.z * heightmapResolution);
            int endX = Mathf.RoundToInt((mod.bounds.x + mod.bounds.width) / terrainSize.x * heightmapResolution);
            int endZ = Mathf.RoundToInt((mod.bounds.y + mod.bounds.height) / terrainSize.z * heightmapResolution);
            
            // Clamp to valid range
            startX = Mathf.Clamp(startX, 0, heightmapResolution - 1);
            startZ = Mathf.Clamp(startZ, 0, heightmapResolution - 1);
            endX = Mathf.Clamp(endX, 0, heightmapResolution - 1);
            endZ = Mathf.Clamp(endZ, 0, heightmapResolution - 1);
            
            // Apply modification
            for (int z = startZ; z <= endZ; z++) {
                for (int x = startX; x <= endX; x++) {
                    float u = (x - startX) / (float)(endX - startX);
                    float v = (z - startZ) / (float)(endZ - startZ);
                    
                    // Sample modification heightmap
                    float modHeight = mod.baseHeight;
                    if (mod.heightMap != null) {
                        int texX = Mathf.RoundToInt(u * (mod.heightMap.width - 1));
                        int texZ = Mathf.RoundToInt(v * (mod.heightMap.height - 1));
                        Color pixel = mod.heightMap.GetPixel(texX, texZ);
                        modHeight += pixel.r * mod.baseHeight;
                    }
                    
                    // Calculate blend factor
                    float distToEdge = Mathf.Min(
                        Mathf.Min(x - startX, endX - x),
                        Mathf.Min(z - startZ, endZ - z)
                    );
                    float blendFactor = Mathf.Clamp01(distToEdge / mod.blendRadius);
                    blendFactor = mod.falloffCurve.Evaluate(blendFactor);
                    
                    // Apply modification based on type
                    float currentHeight = _heightmap[z, x];
                    float newHeight = currentHeight;
                    
                    switch (mod.type) {
                        case ModificationType.Replace:
                            newHeight = Mathf.Lerp(currentHeight, modHeight, blendFactor);
                            break;
                            
                        case ModificationType.Additive:
                            newHeight = currentHeight + modHeight * blendFactor;
                            break;
                            
                        case ModificationType.Multiply:
                            newHeight = currentHeight * (1f + (modHeight - 1f) * blendFactor);
                            break;
                            
                        case ModificationType.Smooth:
                            newHeight = SmoothHeight(x, z, (int)(mod.blendRadius * blendFactor));
                            break;
                            
                        case ModificationType.Flatten:
                            newHeight = Mathf.Lerp(currentHeight, mod.baseHeight, blendFactor);
                            break;
                    }
                    
                    _heightmap[z, x] = Mathf.Clamp01(newHeight);
                }
                
                if (z % 10 == 0) yield return null;
            }
        }
        
        private float SmoothHeight(int x, int z, int radius) {
            float sum = 0;
            int count = 0;
            
            for (int dz = -radius; dz <= radius; dz++) {
                for (int dx = -radius; dx <= radius; dx++) {
                    int nx = x + dx;
                    int nz = z + dz;
                    
                    if (nx >= 0 && nx < heightmapResolution && nz >= 0 && nz < heightmapResolution) {
                        sum += _heightmap[nz, nx];
                        count++;
                    }
                }
            }
            
            return count > 0 ? sum / count : _heightmap[z, x];
        }
        #endregion

        #region Biome Generation
        private IEnumerator GenerateBiomes(Action<float, string> onProgress) {
            // Generate temperature and moisture maps
            yield return GenerateClimateMaps(onProgress);
            
            // Assign biomes based on height, temperature, and moisture
            _biomeMap = new int[heightmapResolution, heightmapResolution];
            
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    float height = _heightmap[z, x];
                    float temperature = _temperatureMap[z, x];
                    float moisture = _moistureMap[z, x];
                    
                    // Find best matching biome
                    int bestBiome = 0;
                    float bestScore = float.MaxValue;
                    
                    for (int i = 0; i < biomes.Count; i++) {
                        var biome = biomes[i];
                        
                        // Calculate distance from biome center
                        float heightDist = 0;
                        if (height < biome.minHeight) heightDist = biome.minHeight - height;
                        else if (height > biome.maxHeight) heightDist = height - biome.maxHeight;
                        
                        float tempDist = 0;
                        if (temperature < biome.minTemperature) tempDist = biome.minTemperature - temperature;
                        else if (temperature > biome.maxTemperature) tempDist = temperature - biome.maxTemperature;
                        
                        float moistDist = 0;
                        if (moisture < biome.minMoisture) moistDist = biome.minMoisture - moisture;
                        else if (moisture > biome.maxMoisture) moistDist = moisture - biome.maxMoisture;
                        
                        float score = heightDist * 2f + tempDist + moistDist;
                        
                        if (score < bestScore) {
                            bestScore = score;
                            bestBiome = i;
                        }
                    }
                    
                    _biomeMap[z, x] = bestBiome;
                }
                
                if (z % 50 == 0) {
                    float progress = 0.4f + (z / (float)heightmapResolution) * 0.05f;
                    onProgress?.Invoke(progress, "Assigning biomes");
                    yield return null;
                }
            }
            
            // Smooth biome transitions
            yield return SmoothBiomeTransitions(onProgress);
            
            // Apply biome-specific modifications
            yield return ApplyBiomeModifications(onProgress);
        }
        
        private IEnumerator GenerateClimateMaps(Action<float, string> onProgress) {
            _temperatureMap = new float[heightmapResolution, heightmapResolution];
            _moistureMap = new float[heightmapResolution, heightmapResolution];
            
            // Generate base climate maps using noise
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    // Temperature decreases with height and latitude
                    float height = _heightmap[z, x];
                    float latitude = z / (float)heightmapResolution;
                    
                    float baseTemp = 1f - latitude * 0.5f; // Warmer at equator
                    baseTemp -= height * 0.8f; // Colder at altitude
                    
                    // Add noise
                    float tempNoise = Mathf.PerlinNoise(x * 0.02f + 100, z * 0.02f + 100);
                    _temperatureMap[z, x] = Mathf.Clamp01(baseTemp + tempNoise * 0.3f);
                    
                    // Moisture based on proximity to water and noise
                    float baseMoisture = height < waterLevel ? 1f : 0.5f;
                    float moistNoise = Mathf.PerlinNoise(x * 0.03f + 200, z * 0.03f + 200);
                    _moistureMap[z, x] = Mathf.Clamp01(baseMoisture + moistNoise * 0.4f);
                }
            }
            
            // Blur for smoother transitions
            yield return BlurMap(_temperatureMap, 3);
            yield return BlurMap(_moistureMap, 3);
        }
        
        private IEnumerator BlurMap(float[,] map, int iterations) {
            float[,] temp = new float[heightmapResolution, heightmapResolution];
            
            for (int iter = 0; iter < iterations; iter++) {
                // Copy to temp
                Array.Copy(map, temp, map.Length);
                
                // Apply box blur
                for (int z = 1; z < heightmapResolution - 1; z++) {
                    for (int x = 1; x < heightmapResolution - 1; x++) {
                        float sum = 0;
                        for (int dz = -1; dz <= 1; dz++) {
                            for (int dx = -1; dx <= 1; dx++) {
                                sum += temp[z + dz, x + dx] * w;
                            }
                        }
                        map[z, x] = sum / 9f;
                    }
                }
                
                yield return null;
            }
        }
        
        private IEnumerator SmoothBiomeTransitions(Action<float, string> onProgress) {
            // Create distance field for each biome
            float[,,] biomeWeights = new float[biomes.Count, heightmapResolution, heightmapResolution];
            
            // Calculate weights
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    int currentBiome = _biomeMap[z, x];
                    
                    // Check neighborhood
                    for (int b = 0; b < biomes.Count; b++) {
                        float weight = 0;
                        int radius = Mathf.CeilToInt(biomeBlendDistance / terrainSize.x * heightmapResolution);
                        
                        for (int dz = -radius; dz <= radius; dz++) {
                            for (int dx = -radius; dx <= radius; dx++) {
                                int nx = x + dx;
                                int nz = z + dz;
                                
                                if (nx >= 0 && nx < heightmapResolution && nz >= 0 && nz < heightmapResolution) {
                                    if (_biomeMap[nz, nx] == b) {
                                        float dist = Mathf.Sqrt(dx * dx + dz * dz);
                                        weight += Mathf.Max(0, 1f - dist / radius);
                                    }
                                }
                            }
                        }
                        
                        biomeWeights[b, z, x] = weight;
                    }
                }
                
                if (z % 50 == 0) {
                    onProgress?.Invoke(0.45f + (z / (float)heightmapResolution) * 0.02f, "Smoothing biome transitions");
                    yield return null;
                }
            }
            
            // Apply texture blending based on weights
            yield return ApplyBiomeTextures(biomeWeights);
        }
        
        private IEnumerator ApplyBiomeTextures(float[,,] weights) {
            int alphamapWidth = _terrainData.alphamapWidth;
            int alphamapHeight = _terrainData.alphamapHeight;
            int numLayers = _terrainData.terrainLayers.Length;
            
            float[,,] alphaMaps = new float[alphamapWidth, alphamapHeight, numLayers];
            
            // Map biomes to terrain layers
            Dictionary<int, List<int>> biomeToLayers = new Dictionary<int, List<int>>();
            int layerIndex = 0;
            
            for (int b = 0; b < biomes.Count; b++) {
                biomeToLayers[b] = new List<int>();
                if (biomes[b].terrainLayers != null) {
                    for (int i = 0; i < biomes[b].terrainLayers.Length && layerIndex < numLayers; i++) {
                        biomeToLayers[b].Add(layerIndex++);
                    }
                }
            }
            
            // Apply weights to alpha maps
            for (int y = 0; y < alphamapHeight; y++) {
                for (int x = 0; x < alphamapWidth; x++) {
                    // Sample biome weights
                    int hx = x * heightmapResolution / alphamapWidth;
                    int hy = y * heightmapResolution / alphamapHeight;
                    
                    float totalWeight = 0;
                    float[] layerWeights = new float[numLayers];
                    
                    for (int b = 0; b < biomes.Count; b++) {
                        float biomeWeight = weights[b, hy, hx];
                        if (biomeWeight > 0 && biomeToLayers.ContainsKey(b)) {
                            foreach (int layer in biomeToLayers[b]) {
                                layerWeights[layer] += biomeWeight;
                                totalWeight += biomeWeight;
                            }
                        }
                    }
                    
                    // Normalize
                    if (totalWeight > 0) {
                        for (int i = 0; i < numLayers; i++) {
                            alphaMaps[x, y, i] = layerWeights[i] / totalWeight;
                        }
                    }
                }
                
                if (y % 50 == 0) yield return null;
            }
            
            _terrainData.SetAlphamaps(0, 0, alphaMaps);
        }
        
        private IEnumerator ApplyBiomeModifications(Action<float, string> onProgress) {
            // Apply biome-specific height modifications
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    int biomeIndex = _biomeMap[z, x];
                    if (biomeIndex >= 0 && biomeIndex < biomes.Count) {
                        var biome = biomes[biomeIndex];
                        
                        // Add biome-specific noise
                        if (biome.noiseStrength > 0) {
                            float noise = Mathf.PerlinNoise(
                                x * biome.noiseScale + biomeIndex * 1000,
                                z * biome.noiseScale + biomeIndex * 1000
                            );
                            _heightmap[z, x] += (noise - 0.5f) * biome.noiseStrength;
                            _heightmap[z, x] = Mathf.Clamp01(_heightmap[z, x]);
                        }
                    }
                }
                
                if (z % 100 == 0) {
                    onProgress?.Invoke(0.47f + (z / (float)heightmapResolution) * 0.03f, "Applying biome features");
                    yield return null;
                }
            }
        }
        #endregion

        #region Erosion Simulation
        private IEnumerator SimulateErosion(Action<float, string> onProgress) {
            switch (erosionType) {
                case ErosionType.Thermal:
                    yield return SimulateThermalErosion(onProgress);
                    break;
                    
                case ErosionType.Hydraulic:
                    yield return SimulateHydraulicErosion(onProgress);
                    break;
                    
                case ErosionType.Combined:
                    yield return SimulateThermalErosion(onProgress);
                    yield return SimulateHydraulicErosion(onProgress);
                    break;
            }
        }
        
        private IEnumerator SimulateThermalErosion(Action<float, string> onProgress) {
            float talusAngle = Mathf.Tan(maxSlope * Mathf.Deg2Rad);
            int iterations = erosionIterations / 10; // Thermal erosion needs fewer iterations
            
            for (int iter = 0; iter < iterations; iter++) {
                bool changed = false;
                
                for (int z = 1; z < heightmapResolution - 1; z++) {
                    for (int x = 1; x < heightmapResolution - 1; x++) {
                        float currentHeight = _heightmap[z, x];
                        
                        // Check all neighbors
                        for (int dz = -1; dz <= 1; dz++) {
                            for (int dx = -1; dx <= 1; dx++) {
                                if (dx == 0 && dz == 0) continue;
                                
                                int nx = x + dx;
                                int nz = z + dz;
                                
                                float neighborHeight = _heightmap[nz, nx];
                                float heightDiff = currentHeight - neighborHeight;
                                float distance = Mathf.Sqrt(dx * dx + dz * dz);
                                float slope = heightDiff / distance;
                                
                                if (slope > talusAngle) {
                                    // Material slides down
                                    float excess = (slope - talusAngle) * distance * 0.5f;
                                    _heightmap[z, x] -= excess * depositionSpeed;
                                    _heightmap[nz, nx] += excess * depositionSpeed;
                                    changed = true;
                                }
                            }
                        }
                    }
                }
                
                if (!changed) break;
                
                if (iter % 100 == 0) {
                    float progress = 0.5f + (iter / (float)iterations) * 0.05f;
                    onProgress?.Invoke(progress, "Simulating thermal erosion");
                    yield return null;
                }
            }
        }
        
        private IEnumerator SimulateHydraulicErosion(Action<float, string> onProgress) {
            if (useGPUAcceleration && _erosionCompute != null) {
                yield return SimulateHydraulicErosionGPU(onProgress);
            } else {
                yield return SimulateHydraulicErosionCPU(onProgress);
            }
        }
        
        private IEnumerator SimulateHydraulicErosionGPU(Action<float, string> onProgress) {
            // Prepare compute buffers
            int resolution = heightmapResolution;
            ComputeBuffer heightBuffer = new ComputeBuffer(resolution * resolution, sizeof(float));
            ComputeBuffer flowBuffer = new ComputeBuffer(resolution * resolution, sizeof(float) * 2);
            ComputeBuffer sedimentBuffer = new ComputeBuffer(resolution * resolution, sizeof(float));
            
            // Convert heightmap to linear array
            float[] heightData = new float[resolution * resolution];
            for (int z = 0; z < resolution; z++) {
                for (int x = 0; x < resolution; x++) {
                    heightData[z * resolution + x] = _heightmap[z, x];
                }
            }
            heightBuffer.SetData(heightData);
            
            // Setup compute shader
            int kernel = _erosionCompute.FindKernel("HydraulicErosion");
            _erosionCompute.SetBuffer(kernel, "_Heights", heightBuffer);
            _erosionCompute.SetBuffer(kernel, "_Flow", flowBuffer);
            _erosionCompute.SetBuffer(kernel, "_Sediment", sedimentBuffer);
            _erosionCompute.SetInt("_Resolution", resolution);
            _erosionCompute.SetFloat("_BrushRadius", erosionBrushRadius);
            _erosionCompute.SetFloat("_Gravity", gravity);
            _erosionCompute.SetFloat("_EvaporationSpeed", evaporationSpeed);
            _erosionCompute.SetFloat("_DepositionSpeed", depositionSpeed);
            _erosionCompute.SetFloat("_SedimentCapacity", sedimentCapacityConstant);
            
            // Run erosion in batches
            int batchSize = 1000;
            int batches = erosionIterations / batchSize;
            
            for (int batch = 0; batch < batches; batch++) {
                _erosionCompute.SetInt("_Seed", seed + batch);
                _erosionCompute.Dispatch(kernel, batchSize / 64, 1, 1);
                
                if (batch % 10 == 0) {
                    float progress = 0.5f + (batch / (float)batches) * 0.1f;
                    onProgress?.Invoke(progress, "GPU hydraulic erosion");
                    yield return null;
                }
            }
            
            // Read back results
            heightBuffer.GetData(heightData);
            for (int z = 0; z < resolution; z++) {
                for (int x = 0; x < resolution; x++) {
                    _heightmap[z, x] = heightData[z * resolution + x];
                }
            }
            
            // Cleanup
            heightBuffer.Release();
            flowBuffer.Release();
            sedimentBuffer.Release();
        }
        
        private IEnumerator SimulateHydraulicErosionCPU(Action<float, string> onProgress) {
            System.Random erosionRng = new System.Random(seed);
            _flowField = new Vector2[heightmapResolution, heightmapResolution];
            
            // Calculate initial flow field
            CalculateFlowField();
            
            List<FlowData> activeDroplets = new List<FlowData>();
            
            for (int iter = 0; iter < erosionIterations; iter++) {
                // Spawn new water droplet
                var droplet = new FlowData {
                    position = new Vector2(
                        erosionRng.Next(1, heightmapResolution - 1),
                        erosionRng.Next(1, heightmapResolution - 1)
                    ),
                    direction = Vector2.zero,
                    velocity = startSpeed,
                    water = startWater,
                    sediment = 0,
                    lifetime = 0
                };
                
                // Simulate droplet
                for (int life = 0; life < 30; life++) {
                    int x = Mathf.FloorToInt(droplet.position.x);
                    int z = Mathf.FloorToInt(droplet.position.y);
                    
                    if (x <= 0 || x >= heightmapResolution - 1 || 
                        z <= 0 || z >= heightmapResolution - 1) break;
                    
                    // Calculate height and gradient
                    float height = BilinearSample(_heightmap, droplet.position.x, droplet.position.y);
                    Vector2 gradient = CalculateGradient(droplet.position.x, droplet.position.y);
                    
                    // Update direction with inertia
                    droplet.direction = droplet.direction * inertia - gradient * (1f - inertia);
                    
                    if (droplet.direction.magnitude < 0.01f) {
                        // Deposit all sediment if stuck
                        DepositSediment(droplet.position.x, droplet.position.y, droplet.sediment);
                        break;
                    }
                    
                    droplet.direction.Normalize();
                    
                    // Move droplet
                    Vector2 newPos = droplet.position + droplet.direction;
                    float newHeight = BilinearSample(_heightmap, newPos.x, newPos.y);
                    float heightDiff = height - newHeight;
                    
                    // Calculate carrying capacity
                    float capacity = Mathf.Max(-heightDiff, 0.01f) * droplet.velocity * droplet.water * sedimentCapacityConstant;
                    
                    if (droplet.sediment > capacity || heightDiff > 0) {
                        // Deposit sediment
                        float depositAmount = (droplet.sediment - capacity) * depositionSpeed;
                        if (heightDiff > 0) depositAmount = Mathf.Min(heightDiff, droplet.sediment);
                        
                        DepositSediment(droplet.position.x, droplet.position.y, depositAmount);
                        droplet.sediment -= depositAmount;
                    } else {
                        // Erode terrain
                        float erodeAmount = Mathf.Min((capacity - droplet.sediment) * erosionBrushRadius, -heightDiff);
                        
                        for (int ez = -Mathf.CeilToInt(erosionBrushRadius); ez <= erosionBrushRadius; ez++) {
                            for (int ex = -Mathf.CeilToInt(erosionBrushRadius); ex <= erosionBrushRadius; ex++) {
                                int erosionX = x + ex;
                                int erosionZ = z + ez;
                                
                                if (erosionX >= 0 && erosionX < heightmapResolution &&
                                    erosionZ >= 0 && erosionZ < heightmapResolution) {
                                    float distance = Mathf.Sqrt(ex * ex + ez * ez);
                                    if (distance <= erosionBrushRadius) {
                                        float weight = 1f - distance / erosionBrushRadius;
                                        float erosion = erodeAmount * weight;
                                        _heightmap[erosionZ, erosionX] -= erosion;
                                        droplet.sediment += erosion;
                                    }
                                }
                            }
                        }
                    }
                    
                    // Update droplet physics
                    droplet.velocity = Mathf.Sqrt(droplet.velocity * droplet.velocity + heightDiff * gravity);
                    droplet.water *= (1f - evaporationSpeed);
                    droplet.position = newPos;
                    droplet.lifetime++;
                    
                    if (droplet.water < 0.01f) break;
                }
                
                if (iter % 1000 == 0) {
                    // Recalculate flow field periodically
                    CalculateFlowField();
                    
                    float progress = 0.5f + (iter / (float)erosionIterations) * 0.1f;
                    onProgress?.Invoke(progress, $"Hydraulic erosion: {iter}/{erosionIterations}");
                    yield return null;
                }
            }
            
            // Final smoothing pass
            yield return SmoothTerrain(3, onProgress);
        }
        
        private void CalculateFlowField() {
            for (int z = 1; z < heightmapResolution - 1; z++) {
                for (int x = 1; x < heightmapResolution - 1; x++) {
                    Vector2 gradient = CalculateGradient(x, z);
                    _flowField[z, x] = -gradient.normalized;
                }
            }
        }
        
        private Vector2 CalculateGradient(float x, float z) {
            int ix = Mathf.FloorToInt(x);
            int iz = Mathf.FloorToInt(z);
            
            float heightL = _heightmap[iz, Mathf.Max(0, ix - 1)];
            float heightR = _heightmap[iz, Mathf.Min(heightmapResolution - 1, ix + 1)];
            float heightD = _heightmap[Mathf.Max(0, iz - 1), ix];
            float heightU = _heightmap[Mathf.Min(heightmapResolution - 1, iz + 1), ix];
            
            return new Vector2(heightR - heightL, heightU - heightD);
        }
        
        private float BilinearSample(float[,] map, float x, float z) {
            int x0 = Mathf.FloorToInt(x);
            int z0 = Mathf.FloorToInt(z);
            int x1 = Mathf.Min(x0 + 1, heightmapResolution - 1);
            int z1 = Mathf.Min(z0 + 1, heightmapResolution - 1);
            
            float fx = x - x0;
            float fz = z - z0;
            
            float h00 = map[z0, x0];
            float h10 = map[z0, x1];
            float h01 = map[z1, x0];
            float h11 = map[z1, x1];
            
            float h0 = Mathf.Lerp(h00, h10, fx);
            float h1 = Mathf.Lerp(h01, h11, fx);
            
            return Mathf.Lerp(h0, h1, fz);
        }
        
        private void DepositSediment(float x, float z, float amount) {
            int ix = Mathf.FloorToInt(x);
            int iz = Mathf.FloorToInt(z);
            
            float fx = x - ix;
            float fz = z - iz;
            
            // Distribute to four nearest points
            _heightmap[iz, ix] += amount * (1 - fx) * (1 - fz);
            
            if (ix + 1 < heightmapResolution) {
                _heightmap[iz, ix + 1] += amount * fx * (1 - fz);
            }
            
            if (iz + 1 < heightmapResolution) {
                _heightmap[iz + 1, ix] += amount * (1 - fx) * fz;
                
                if (ix + 1 < heightmapResolution) {
                    _heightmap[iz + 1, ix + 1] += amount * fx * fz;
                }
            }
        }
        
        private IEnumerator SmoothTerrain(int iterations, Action<float, string> onProgress) {
            float[,] temp = new float[heightmapResolution, heightmapResolution];
            
            for (int iter = 0; iter < iterations; iter++) {
                Array.Copy(_heightmap, temp, _heightmap.Length);
                
                for (int z = 1; z < heightmapResolution - 1; z++) {
                    for (int x = 1; x < heightmapResolution - 1; x++) {
                        float sum = 0;
                        float weight = 0;
                        
                        for (int dz = -1; dz <= 1; dz++) {
                            for (int dx = -1; dx <= 1; dx++) {
                                float w = dx == 0 && dz == 0 ? 4f : 1f;
                                sum += temp[z + dz, x + dx] * w;
                                weight += w;
                            }
                        }
                        
                        _heightmap[z, x] = sum / weight;
                    }
                }
                
                onProgress?.Invoke(0.58f + iter * 0.01f, "Smoothing terrain");
                yield return null;
            }
        }
        #endregion

        #region Water Features
        private IEnumerator GenerateWaterFeatures(Action<float, string> onProgress) {
            // Generate rivers if enabled
            if (generateRivers) {
                yield return GenerateRivers(onProgress);
            }
            
            // Create water plane
            if (generateWater) {
                CreateWaterObject();
                onProgress?.Invoke(0.65f, "Water features created");
            }
        }
        
        private IEnumerator GenerateRivers(Action<float, string> onProgress) {
            List<PathNode> riverPaths = new List<PathNode>();
            System.Random riverRng = new System.Random(seed + 1);
            
            // Find river starting points (high elevation with good flow)
            List<Vector2Int> riverStarts = new List<Vector2Int>();
            
            for (int i = 0; i < Mathf.RoundToInt(riverDensity * 10); i++) {
                int x = riverRng.Next(heightmapResolution / 4, 3 * heightmapResolution / 4);
                int z = riverRng.Next(heightmapResolution / 4, 3 * heightmapResolution / 4);
                
                float height = _heightmap[z, x];
                if (height > 0.6f && height < 0.9f) {
                    riverStarts.Add(new Vector2Int(x, z));
                }
            }
            
            // Trace river paths following flow field
            foreach (var start in riverStarts) {
                var path = TraceRiverPath(start, riverRng);
                if (path.Count > 10) {
                    riverPaths.AddRange(path);
                }
                
                onProgress?.Invoke(0.62f, $"Generating river {riverStarts.IndexOf(start) + 1}/{riverStarts.Count}");
                yield return null;
            }
            
            // Carve river channels
            foreach (var node in riverPaths) {
                yield return CarveRiverChannel(node);
            }
            
            // Add to path network
            _pathNetwork.AddRange(riverPaths);
        }
        
        private List<PathNode> TraceRiverPath(Vector2Int start, System.Random rng) {
            List<PathNode> path = new List<PathNode>();
            Vector2 pos = new Vector2(start.x, start.y);
            float width = minRiverWidth;
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            
            for (int step = 0; step < 1000; step++) {
                int x = Mathf.RoundToInt(pos.x);
                int z = Mathf.RoundToInt(pos.y);
                
                if (x < 0 || x >= heightmapResolution || z < 0 || z >= heightmapResolution) break;
                
                Vector2Int gridPos = new Vector2Int(x, z);
                if (visited.Contains(gridPos)) break;
                visited.Add(gridPos);
                
                float height = _heightmap[z, x];
                
                // Stop at water level
                if (height <= waterLevel) break;
                
                // Create path node
                var node = new PathNode {
                    position = new Vector3(
                        pos.x / heightmapResolution * terrainSize.x,
                        height * terrainSize.y,
                        pos.y / heightmapResolution * terrainSize.z
                    ),
                    width = width,
                    type = PathType.River,
                    elevation = height,
                    connections = new List<PathNode>()
                };
                
                if (path.Count > 0) {
                    path[path.Count - 1].connections.Add(node);
                }
                
                path.Add(node);
                
                // Follow gradient with some randomness
                Vector2 gradient = CalculateGradient(pos.x, pos.y);
                Vector2 flow = -gradient.normalized;
                
                // Add meander
                float meander = Mathf.PerlinNoise(pos.x * 0.1f, pos.y * 0.1f) - 0.5f;
                flow = Quaternion.Euler(0, 0, meander * 30f) * flow;
                
                pos += flow * 2f;
                
                // Increase width as we go downstream
                width = Mathf.Min(width + 0.1f, maxRiverWidth);
            }
            
            return path;
        }
        
        private IEnumerator CarveRiverChannel(PathNode node) {
            Vector2 pos = new Vector2(
                node.position.x / terrainSize.x * heightmapResolution,
                node.position.z / terrainSize.z * heightmapResolution
            );
            
            int radius = Mathf.CeilToInt(node.width / terrainSize.x * heightmapResolution);
            float targetDepth = riverDepth / terrainSize.y;
            
            for (int dz = -radius; dz <= radius; dz++) {
                for (int dx = -radius; dx <= radius; dx++) {
                    int x = Mathf.RoundToInt(pos.x) + dx;
                    int z = Mathf.RoundToInt(pos.y) + dz;
                    
                    if (x >= 0 && x < heightmapResolution && z >= 0 && z < heightmapResolution) {
                        float distance = Mathf.Sqrt(dx * dx + dz * dz);
                        if (distance <= radius) {
                            float profile = riverProfile.Evaluate(distance / radius);
                            float currentHeight = _heightmap[z, x];
                            float riverBed = node.elevation - targetDepth * profile;
                            
                            _heightmap[z, x] = Mathf.Min(currentHeight, riverBed);
                        }
                    }
                }
            }
            
            yield return null;
        }
        
        private void CreateWaterObject() {
            if (_waterObject != null) {
                Destroy(_waterObject);
            }
            
            // Create water plane
            if (waterPrefab != null) {
                _waterObject = Instantiate(waterPrefab, transform);
            } else {
                _waterObject = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _waterObject.name = "Water";
                _waterObject.transform.SetParent(transform);
                
                // Apply water material
                if (waterMaterial != null) {
                    _waterObject.GetComponent<Renderer>().material = waterMaterial;
                } else {
                    var mat = new Material(Shader.Find("Standard"));
                    mat.SetFloat("_Mode", 3); // Transparent
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                    mat.color = Color.Lerp(shallowWaterColor, deepWaterColor, 0.5f);
                    _waterObject.GetComponent<Renderer>().material = mat;
                }
            }
            
            // Position and scale water
            UpdateWaterObject();
            
            // Add dynamic water component if enabled
            if (dynamicWater) {
                var waterAnim = _waterObject.AddComponent<WaterAnimation>();
                waterAnim.waveHeight = waveHeight;
                waterAnim.waveSpeed = waveSpeed;
            }
        }
        
        private void UpdateWaterObject() {
            if (_waterObject == null) return;
            
            float waterY = waterLevel * terrainSize.y;
            _waterObject.transform.position = new Vector3(
                terrainSize.x / 2f,
                waterY,
                terrainSize.z / 2f
            );
            
            // Scale to cover terrain
            _waterObject.transform.localScale = new Vector3(
                terrainSize.x / 10f,
                1f,
                terrainSize.z / 10f
            );
        }
        #endregion

        #region Path Generation
        private IEnumerator GeneratePaths(List<PathNode> existingPaths, Action<float, string> onProgress) {
            if (existingPaths != null) {
                _pathNetwork.AddRange(existingPaths);
            }
            
            if (generateRoads) {
                yield return GenerateRoadNetwork(onProgress);
            }
            
            // Apply path modifications to terrain
            foreach (var node in _pathNetwork) {
                if (node.type != PathType.River) {
                    yield return FlattenPathTerrain(node);
                }
            }
            
            onProgress?.Invoke(0.75f, "Path network complete");
        }
        
        private IEnumerator GenerateRoadNetwork(Action<float, string> onProgress) {
            System.Random roadRng = new System.Random(seed + 2);
            
            // Find suitable locations for settlements
            List<Vector2Int> settlements = FindSettlementLocations(roadRng);
            
            // Connect settlements with roads using A* pathfinding
            for (int i = 0; i < settlements.Count - 1; i++) {
                for (int j = i + 1; j < settlements.Count; j++) {
                    float distance = Vector2Int.Distance(settlements[i], settlements[j]);
                    
                    // Only connect nearby settlements
                    if (distance < heightmapResolution * 0.3f) {
                        var path = FindPath(settlements[i], settlements[j], PathType.Road);
                        if (path != null) {
                            _pathNetwork.AddRange(path);
                        }
                    }
                }
                
                onProgress?.Invoke(0.72f + (i / (float)settlements.Count) * 0.03f, "Generating road network");
                yield return null;
            }
        }
        
        private List<Vector2Int> FindSettlementLocations(System.Random rng) {
            List<Vector2Int> locations = new List<Vector2Int>();
            int numSettlements = Mathf.RoundToInt(roadDensity * 20);
            
            for (int i = 0; i < numSettlements * 10 && locations.Count < numSettlements; i++) {
                int x = rng.Next(50, heightmapResolution - 50);
                int z = rng.Next(50, heightmapResolution - 50);
                
                float height = _heightmap[z, x];
                Vector2 gradient = CalculateGradient(x, z);
                float slope = gradient.magnitude;
                
                // Good settlement locations: moderate elevation, low slope, not in water
                if (height > waterLevel + 0.05f && height < 0.7f && slope < 0.3f) {
                    // Check minimum distance from other settlements
                    bool tooClose = false;
                    foreach (var loc in locations) {
                        if (Vector2Int.Distance(loc, new Vector2Int(x, z)) < 50) {
                            tooClose = true;
                            break;
                        }
                    }
                    
                    if (!tooClose) {
                        locations.Add(new Vector2Int(x, z));
                    }
                }
            }
            
            return locations;
        }
        
        private List<PathNode> FindPath(Vector2Int start, Vector2Int end, PathType pathType) {
            // A* pathfinding with terrain cost
            var openSet = new SortedSet<PathfindingNode>(new PathfindingNodeComparer());
            var closedSet = new HashSet<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float>();
            var fScore = new Dictionary<Vector2Int, float>();
            
            gScore[start] = 0;
            fScore[start] = Vector2Int.Distance(start, end);
            openSet.Add(new PathfindingNode { position = start, fScore = fScore[start] });
            
            while (openSet.Count > 0) {
                var current = openSet.Min;
                openSet.Remove(current);
                
                if (current.position == end) {
                    // Reconstruct path
                    return ReconstructPath(cameFrom, current.position, pathType);
                }
                
                closedSet.Add(current.position);
                
                // Check neighbors
                for (int dz = -1; dz <= 1; dz++) {
                    for (int dx = -1; dx <= 1; dx++) {
                        if (dx == 0 && dz == 0) continue;
                        
                        Vector2Int neighbor = new Vector2Int(
                            current.position.x + dx,
                            current.position.y + dz
                        );
                        
                        if (neighbor.x < 0 || neighbor.x >= heightmapResolution ||
                            neighbor.y < 0 || neighbor.y >= heightmapResolution) continue;
                        
                        if (closedSet.Contains(neighbor)) continue;
                        
                        // Calculate movement cost
                        float cost = CalculatePathCost(current.position, neighbor, pathType);
                        float tentativeGScore = gScore[current.position] + cost;
                        
                        if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor]) {
                            cameFrom[neighbor] = current.position;
                            gScore[neighbor] = tentativeGScore;
                            fScore[neighbor] = tentativeGScore + Vector2Int.Distance(neighbor, end);
                            
                            openSet.Add(new PathfindingNode { position = neighbor, fScore = fScore[neighbor] });
                        }
                    }
                }
            }
            
            return null; // No path found
        }
        
        private float CalculatePathCost(Vector2Int from, Vector2Int to, PathType pathType) {
            float distance = Vector2Int.Distance(from, to);
            float fromHeight = _heightmap[from.y, from.x];
            float toHeight = _heightmap[to.y, to.x];
            float heightDiff = Mathf.Abs(toHeight - fromHeight);
            
            // Base cost is distance
            float cost = distance;
            
            // Add slope penalty
            float slope = heightDiff / distance;
            cost += slope * 10f;
            
            // Add water penalty for roads
            if (pathType == PathType.Road && toHeight <= waterLevel) {
                cost += 100f;
            }
            
            // Prefer existing paths
            foreach (var node in _pathNetwork) {
                Vector2 nodePos = new Vector2(
                    node.position.x / terrainSize.x * heightmapResolution,
                    node.position.z / terrainSize.z * heightmapResolution
                );
                
                if (Vector2.Distance(nodePos, to) < node.width) {
                    cost *= 0.5f; // Half cost on existing paths
                    break;
                }
            }
            
            return cost;
        }
        
        private List<PathNode> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, 
                                               Vector2Int end, PathType pathType) {
            List<PathNode> path = new List<PathNode>();
            Vector2Int current = end;
            
            while (cameFrom.ContainsKey(current)) {
                var node = new PathNode {
                    position = new Vector3(
                        current.x / (float)heightmapResolution * terrainSize.x,
                        _heightmap[current.y, current.x] * terrainSize.y,
                        current.y / (float)heightmapResolution * terrainSize.z
                    ),
                    width = pathType == PathType.Road ? 5f : 3f,
                    type = pathType,
                    elevation = _heightmap[current.y, current.x],
                    connections = new List<PathNode>()
                };
                
                if (path.Count > 0) {
                    node.connections.Add(path[0]);
                    path[0].connections.Add(node);
                }
                
                path.Insert(0, node);
                current = cameFrom[current];
            }
            
            // Smooth path
            SmoothPath(path);
            
            return path;
        }
        
        private void SmoothPath(List<PathNode> path) {
            if (path.Count < 3) return;
            
            // Apply Catmull-Rom spline smoothing
            for (int i = 1; i < path.Count - 1; i++) {
                Vector3 p0 = i > 0 ? path[i - 1].position : path[i].position;
                Vector3 p1 = path[i].position;
                Vector3 p2 = path[i + 1].position;
                Vector3 p3 = i < path.Count - 2 ? path[i + 2].position : path[i + 1].position;
                
                // Smooth position
                path[i].position = CatmullRom(p0, p1, p2, p3, 0.5f);
                
                // Update elevation
                path[i].position.y = _heightmap[
                    Mathf.RoundToInt(path[i].position.z / terrainSize.z * heightmapResolution),
                    Mathf.RoundToInt(path[i].position.x / terrainSize.x * heightmapResolution)
                ] * terrainSize.y;
            }
        }
        
        private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
            Vector3 a = 2f * p1;
            Vector3 b = p2 - p0;
            Vector3 c = 2f * p0 - 5f * p1 + 4f * p2 - p3;
            Vector3 d = -p0 + 3f * p1 - 3f * p2 + p3;
            
            return 0.5f * (a + (b * t) + (c * t * t) + (d * t * t * t));
        }
        
        private IEnumerator FlattenPathTerrain(PathNode node) {
            Vector2 pos = new Vector2(
                node.position.x / terrainSize.x * heightmapResolution,
                node.position.z / terrainSize.z * heightmapResolution
            );
            
            int radius = Mathf.CeilToInt(node.width / terrainSize.x * heightmapResolution);
            float targetHeight = node.elevation;
            
            for (int dz = -radius; dz <= radius; dz++) {
                for (int dx = -radius; dx <= radius; dx++) {
                    int x = Mathf.RoundToInt(pos.x) + dx;
                    int z = Mathf.RoundToInt(pos.y) + dz;
                    
                    if (x >= 0 && x < heightmapResolution && z >= 0 && z < heightmapResolution) {
                        float distance = Mathf.Sqrt(dx * dx + dz * dz);
                        if (distance <= radius) {
                            float blend = 1f - (distance / radius);
                            blend = Mathf.SmoothStep(0, 1, blend);
                            
                            _heightmap[z, x] = Mathf.Lerp(_heightmap[z, x], targetHeight, blend * 0.8f);
                        }
                    }
                }
            }
            
            yield return null;
        }
        #endregion

        #region Vegetation
        private IEnumerator GenerateVegetation(Action<float, string> onProgress) {
            if (vegetationLayers.Count == 0) {
                yield break;
            }
            
            // Setup detail prototypes
            DetailPrototype[] prototypes = new DetailPrototype[vegetationLayers.Count];
            for (int i = 0; i < vegetationLayers.Count; i++) {
                var layer = vegetationLayers[i];
                prototypes[i] = new DetailPrototype {
                    prefab = layer.prefabs.Length > 0 ? layer.prefabs[0] : null,
                    prototypeTexture = null,
                    minWidth = layer.minScale,
                    maxWidth = layer.maxScale,
                    minHeight = layer.minScale,
                    maxHeight = layer.maxScale,
                    renderMode = DetailRenderMode.Grass,
                    usePrototypeMesh = layer.prefabs.Length > 0
                };
                
                layer.prototypeIndex = i;
            }
            
            _terrainData.detailPrototypes = prototypes;
            
            // Generate detail maps
            int detailWidth = _terrainData.detailWidth;
            int detailHeight = _terrainData.detailHeight;
            
            for (int layerIndex = 0; layerIndex < vegetationLayers.Count; layerIndex++) {
                var layer = vegetationLayers[layerIndex];
                int[,] detailMap = new int[detailHeight, detailWidth];
                
                System.Random vegRng = new System.Random(seed + layerIndex + 10);
                
                for (int y = 0; y < detailHeight; y++) {
                    for (int x = 0; x < detailWidth; x++) {
                        // Sample terrain properties
                        float heightX = x / (float)detailWidth * heightmapResolution;
                        float heightY = y / (float)detailHeight * heightmapResolution;
                        
                        int hx = Mathf.RoundToInt(heightX);
                        int hy = Mathf.RoundToInt(heightY);
                        
                        float height = _heightmap[hy, hx];
                        Vector2 gradient = CalculateGradient(hx, hy);
                        float slope = Mathf.Atan(gradient.magnitude) * Mathf.Rad2Deg;
                        
                        // Check if vegetation can grow here
                        if (height >= layer.minHeight && height <= layer.maxHeight &&
                            slope >= layer.minSlope && slope <= layer.maxSlope &&
                            height > waterLevel + 0.01f) {
                            
                            // Check biome
                            int biomeIndex = _biomeMap[hy, hx];
                            var biome = biomes[biomeIndex];
                            float biomeDensity = GetBiomeDensityMultiplier(biome.vegetationDensity);
                            
                            // Calculate density
                            float density = layer.density * vegetationDensity * biomeDensity;
                            
                            // Add position jitter
                            float jitter = (float)vegRng.NextDouble() * layer.positionJitter;
                            
                            // Spawn based on density
                            if (vegRng.NextDouble() < density) {
                                detailMap[y, x] = 1;
                            }
                        }
                    }
                }
                
                _terrainData.SetDetailLayer(0, 0, layerIndex, detailMap);
                
                float progress = 0.85f + (layerIndex / (float)vegetationLayers.Count) * 0.05f;
                onProgress?.Invoke(progress, $"Placing vegetation layer {layerIndex + 1}/{vegetationLayers.Count}");
                yield return null;
            }
        }
        
        private float GetBiomeDensityMultiplier(VegetationDensity density) {
            switch (density) {
                case VegetationDensity.None: return 0f;
                case VegetationDensity.Sparse: return 0.25f;
                case VegetationDensity.Medium: return 0.5f;
                case VegetationDensity.Dense: return 0.75f;
                case VegetationDensity.VeryDense: return 1f;
                default: return 0.5f;
            }
        }
        #endregion

        #region Navigation Mesh
        private IEnumerator BuildNavigationMesh() {
            // Configure NavMesh settings
            // navMeshSurface.agentTypeID = 0; // Default agent
            // navMeshSurface.collectObjects = CollectObjects.Children;
            // navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            
            // Apply agent settings
            // var buildSettings = navMeshSurface.GetBuildSettings();
            // buildSettings.agentRadius = agentRadius;
            // buildSettings.agentHeight = agentHeight;
            // buildSettings.agentSlope = maxSlope;
            // buildSettings.agentClimb = stepHeight;
            // navMeshSurface.defaultArea = 0; // Walkable
            
            // Build NavMesh
            // navMeshSurface.BuildNavMesh();
            
            // Generate off-mesh links if enabled
            // if (buildOffMeshLinks) {
            //     yield return GenerateOffMeshLinks();
            // }
            
            yield return null;
        }
        
        private IEnumerator GenerateOffMeshLinks() {
            // Find potential jump/climb locations
            List<NavMeshLink> links = new List<NavMeshLink>();
            
            // Check for cliffs and gaps
            for (int z = 0; z < heightmapResolution - 1; z += 10) {
                for (int x = 0; x < heightmapResolution - 1; x += 10) {
                    float currentHeight = _heightmap[z, x];
                    
                    // Check neighbors for significant height differences
                    for (int dz = -1; dz <= 1; dz++) {
                        for (int dx = -1; dx <= 1; dx++) {
                            if (dx == 0 && dz == 0) continue;
                            
                            int nx = x + dx * Mathf.RoundToInt(jumpDistance / terrainSize.x * heightmapResolution);
                            int nz = z + dz * Mathf.RoundToInt(jumpDistance / terrainSize.z * heightmapResolution);
                            
                            if (nx >= 0 && nx < heightmapResolution && nz >= 0 && nz < heightmapResolution) {
                                float neighborHeight = _heightmap[nz, nx];
                                float heightDiff = Mathf.Abs(neighborHeight - currentHeight);
                                
                                if (heightDiff > stepHeight && heightDiff < agentHeight) {
                                    // Create off-mesh link
                                    var link = new GameObject($"NavLink_{links.Count}").AddComponent<NavMeshLink>();
                                    link.transform.SetParent(transform);
                                    
                                    Vector3 startPos = new Vector3(
                                        x / (float)heightmapResolution * terrainSize.x,
                                        currentHeight * terrainSize.y + 0.5f,
                                        z / (float)heightmapResolution * terrainSize.z
                                    );
                                    
                                    Vector3 endPos = new Vector3(
                                        nx / (float)heightmapResolution * terrainSize.x,
                                        neighborHeight * terrainSize.y + 0.5f,
                                        nz / (float)heightmapResolution * terrainSize.z
                                    );
                                    
                                    link.startPoint = transform.InverseTransformPoint(startPos);
                                    link.endPoint = transform.InverseTransformPoint(endPos);
                                    link.width = agentRadius * 2f;
                                    link.bidirectional = true;
                                    link.area = 2; // Jump area
                                    
                                    links.Add(link);
                                }
                            }
                        }
                    }
                }
                
                if (z % 50 == 0) yield return null;
            }
            
            debugger?.Log($"Generated {links.Count} off-mesh links", LogCategory.Terrain);
        }
        #endregion

        #region Finalization
        private IEnumerator FinalizeHeightmap(Action<float, string> onProgress) {
            // Ensure water areas are properly flattened
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    if (_heightmap[z, x] < waterLevel) {
                        _heightmap[z, x] = waterLevel - 0.01f;
                    }
                }
            }
            
            // Final edge smoothing
            yield return SmoothTerrainEdges(onProgress);
            
            // Clamp all values
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    _heightmap[z, x] = Mathf.Clamp01(_heightmap[z, x]);
                }
            }
            
            onProgress?.Invoke(0.82f, "Finalized heightmap");
        }
        
        private IEnumerator SmoothTerrainEdges(Action<float, string> onProgress) {
            int edgeWidth = 10;
            
            // Smooth edges to prevent harsh transitions
            for (int i = 0; i < edgeWidth; i++) {
                float weight = i / (float)edgeWidth;
                
                // Top and bottom edges
                for (int x = 0; x < heightmapResolution; x++) {
                    _heightmap[i, x] *= weight;
                    _heightmap[heightmapResolution - 1 - i, x] *= weight;
                }
                
                // Left and right edges
                for (int z = 0; z < heightmapResolution; z++) {
                    _heightmap[z, i] *= weight;
                    _heightmap[z, heightmapResolution - 1 - i] *= weight;
                }
            }
            
            yield return null;
        }
        
        private void ApplyTerrainData() {
            // Apply heightmap to terrain
            _terrainData.SetHeights(0, 0, _heightmap);
            
            // Force terrain update
            _terrain.Flush();
            
            // Update collider
            _terrainCollider.terrainData = _terrainData;
            
            debugger?.Log("Terrain data applied successfully", LogCategory.Terrain);
        }
        
        private void SetupTerrainLODs() {
            if (!useLOD) return;
            
            // Create LOD group if not present
            LODGroup lodGroup = GetComponent<LODGroup>();
            if (lodGroup == null) {
                lodGroup = gameObject.AddComponent<LODGroup>();
            }
            
            // Configure LODs
            LOD[] lods = new LOD[lodDistances.Length];
            
            for (int i = 0; i < lods.Length; i++) {
                float screenRelativeHeight = 1f / (lodDistances[i] / 100f);
                
                // Terrain automatically handles LOD internally
                lods[i] = new LOD(screenRelativeHeight, new Renderer[] { });
            }
            
            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
            
            // Configure terrain LOD settings
            _terrain.heightmapPixelError = 5f;
            _terrain.basemapDistance = lodDistances[lodDistances.Length - 1];
            _terrain.castShadows = true;
            
            debugger?.Log($"Configured {lods.Length} terrain LOD levels", LogCategory.Terrain);
        }
        #endregion

        #region GPU Helpers
        private IEnumerator ReadHeightmapFromGPU() {
            // Create temporary texture
            Texture2D readback = new Texture2D(heightmapResolution, heightmapResolution, TextureFormat.RFloat, false);
            
            // Copy from render texture
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _heightmapRT;
            readback.ReadPixels(new Rect(0, 0, heightmapResolution, heightmapResolution), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            
            // Convert to heightmap array
            Color[] pixels = readback.GetPixels();
            for (int z = 0; z < heightmapResolution; z++) {
                for (int x = 0; x < heightmapResolution; x++) {
                    _heightmap[z, x] = pixels[z * heightmapResolution + x].r;
                }
            }
            
            // Cleanup
            Destroy(readback);
            _heightmapRT.Release();
            Destroy(_heightmapRT);
            
            yield return null;
        }
        #endregion

        #region Cleanup
        private void CleanupResources() {
            // Complete any pending jobs
            if (_currentJob.IsCompleted == false) {
                _currentJob.Complete();
            }
            
            // Dispose native arrays
            if (_heightsNative.IsCreated) {
                _heightsNative.Dispose();
            }
            
            if (_gradientsNative.IsCreated) {
                _gradientsNative.Dispose();
            }
            
            // Release render textures
            if (_heightmapRT != null) {
                _heightmapRT.Release();
                Destroy(_heightmapRT);
            }
            
            if (_normalMapRT != null) {
                _normalMapRT.Release();
                Destroy(_normalMapRT);
            }
        }
        
        private void OnDestroy() {
            CleanupResources();
            
            if (_waterObject != null) {
                Destroy(_waterObject);
            }
            
            debugger?.Log("TerrainGenerator destroyed", LogCategory.System);
        }
        #endregion

        #region Job System
        [BurstCompile]
        private struct ParallelNoiseJob : IJobParallelFor {
            [WriteOnly] public NativeArray<float> heights;
            [ReadOnly] public int resolution;
            [ReadOnly] public float scale;
            [ReadOnly] public int octaves;
            [ReadOnly] public float persistence;
            [ReadOnly] public float lacunarity;
            [ReadOnly] public float2 offset;
            [ReadOnly] public int seed;
            [ReadOnly] public int noiseType;
            
            public void Execute(int index) {
                int x = index % resolution;
                int z = index / resolution;
                
                float amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;
                
                Unity.Mathematics.Random rng = new Unity.Mathematics.Random((uint)(seed + index));
                
                for (int i = 0; i < octaves; i++) {
                    float sampleX = (x - resolution / 2f) * scale * frequency + offset.x + rng.NextFloat(-1000, 1000);
                    float sampleZ = (z - resolution / 2f) * scale * frequency + offset.y + rng.NextFloat(-1000, 1000);
                    
                    float noiseValue = GetNoise(sampleX, sampleZ, noiseType);
                    noiseHeight += noiseValue * amplitude;
                    
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }
                
                heights[index] = noiseHeight;
            }
            
            private float GetNoise(float x, float z, int type) {
                // Use Unity.Mathematics noise functions
                switch (type) {
                    case 1: // Simplex
                        return noise.snoise(new float2(x, z));
                    case 2: // Worley
                        return 1f - noise.cellular(new float2(x, z)).x;
                    default: // Perlin
                        return noise.cnoise(new float2(x, z));
                }
            }
        }
        
        private class PathfindingNode {
            public Vector2Int position;
            public float fScore;
        }
        
        private class PathfindingNodeComparer : IComparer<PathfindingNode> {
            public int Compare(PathfindingNode x, PathfindingNode y) {
                return x.fScore.CompareTo(y.fScore);
            }
        }
        #endregion

        #region Data Classes
        [Serializable]
        public class TerrainGenerationRequest {
            public Vector3 size = Vector3.zero;
            public int resolution = 0;
            public Texture2D heightmapTexture;
            public AnalysisResults analysisResults;
            public List<PathNode> pathNodes;
            public Dictionary<string, object> customParameters;
        }
        
        [Serializable]
        public class WaterAnimation : MonoBehaviour {
            public float waveHeight = 0.5f;
            public float waveSpeed = 1f;
            public float waveScale = 10f;
            
            private Material _material;
            private float _time;
            
            private void Start() {
                _material = GetComponent<Renderer>().material;
            }
            
            private void Update() {
                _time += Time.deltaTime * waveSpeed;
                
                // Animate water height
                float wave = Mathf.Sin(_time) * waveHeight;
                transform.position = new Vector3(
                    transform.position.x,
                    transform.position.y + wave * Time.deltaTime,
                    transform.position.z
                );
                
                // Animate material properties
                if (_material != null) {
                    _material.SetFloat("_WaveTime", _time);
                    _material.SetFloat("_WaveHeight", waveHeight);
                }
            }
        }
        #endregion
        
        #region TraversifyComponent Implementation
        
        /// <summary>
        /// Component-specific initialization logic.
        /// </summary>
        /// <param name="config">Component configuration object</param>
        /// <returns>True if initialization was successful</returns>
        protected override bool OnInitialize(object config)
        {
            try
            {
                // Get or create terrain component
                _terrain = GetComponent<UnityEngine.Terrain>();
                if (_terrain == null)
                {
                    _terrain = gameObject.AddComponent<UnityEngine.Terrain>();
                }
                
                // Get or create terrain collider
                _terrainCollider = GetComponent<TerrainCollider>();
                if (_terrainCollider == null)
                {
                    _terrainCollider = gameObject.AddComponent<TerrainCollider>();
                }
                
                // Initialize debugger
                if (_debugger == null)
                {
                    _debugger = GetComponent<TraversifyDebugger>();
                    if (_debugger == null)
                    {
                        _debugger = gameObject.AddComponent<TraversifyDebugger>();
                    }
                }
                
                // Initialize collections and state
                _isGenerating = false;
                _generationProgress = 0f;
                
                // Initialize terrain data if needed
                if (_terrain.terrainData == null)
                {
                    InitializeTerrainData();
                }
                
                Log("TerrainGenerator initialized successfully", LogCategory.System);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize TerrainGenerator: {ex.Message}", LogCategory.System);
                return false;
            }
        }
        
        /// <summary>
        /// Initializes terrain data with default settings.
        /// </summary>
        private void InitializeTerrainData()
        {
            TerrainData terrainData = new TerrainData();
            terrainData.size = terrainSize;
            terrainData.heightmapResolution = heightmapResolution;
            terrainData.alphamapResolution = alphamapResolution;
            terrainData.baseMapResolution = baseTextureResolution;
            
            _terrain.terrainData = terrainData;
            _terrainCollider.terrainData = terrainData;
        }
        
        #endregion
    }
}
