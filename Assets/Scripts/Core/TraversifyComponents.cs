/*************************************************************************
 *  Traversify – TraversifyComponents.cs                                 *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 02:41:08 UTC                                     *
 *  Desc   : Core components, interfaces, and utilities for the          *
 *           Traversify framework. Provides the foundational building    *
 *           blocks for terrain generation, model placement, and         *
 *           advanced AI-driven environment analysis.                    *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Rendering;
using Unity.Mathematics;
using Traversify.AI;
using Traversify.Core;

namespace Traversify {
    #region Base Components
    
    /// <summary>
    /// Base class for all Traversify components with common functionality.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class TraversifyComponent : MonoBehaviour {
        // Component state tracking
        [SerializeField] protected bool _initialized = false;
        [SerializeField] protected bool _isProcessing = false;
        
        // References
        protected TraversifyDebugger _debugger;
        
        // Events
        public UnityEvent<string> OnError = new UnityEvent<string>();
        public UnityEvent OnInitialized = new UnityEvent();
        public UnityEvent OnProcessingStarted = new UnityEvent();
        public UnityEvent OnProcessingComplete = new UnityEvent();
        
        /// <summary>
        /// Whether the component has been initialized.
        /// </summary>
        public bool IsInitialized => _initialized;
        
        /// <summary>
        /// Whether the component is currently processing.
        /// </summary>
        public bool IsProcessing => _isProcessing;
        
        protected virtual void Awake() {
            // Get or create debugger
            _debugger = GetComponent<TraversifyDebugger>();
            if (_debugger == null) {
                _debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
        }
        
        /// <summary>
        /// Initializes the component with the specified configuration.
        /// </summary>
        /// <param name="config">Component-specific configuration object</param>
        /// <returns>True if initialization was successful</returns>
        public virtual bool Initialize(object config = null) {
            if (_initialized) {
                _debugger?.LogWarning($"{GetType().Name} already initialized", LogCategory.System);
                return true;
            }
            
            try {
                _initialized = OnInitialize(config);
                if (_initialized) {
                    _debugger?.Log($"{GetType().Name} initialized", LogCategory.System);
                    OnInitialized?.Invoke();
                }
                return _initialized;
            }
            catch (Exception ex) {
                _debugger?.LogError($"Failed to initialize {GetType().Name}: {ex.Message}", LogCategory.System);
                OnError?.Invoke($"Initialization error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Component-specific initialization logic to be implemented by derived classes.
        /// </summary>
        /// <param name="config">Component-specific configuration object</param>
        /// <returns>True if initialization was successful</returns>
        protected abstract bool OnInitialize(object config);
        
        /// <summary>
        /// Logs a message to the debugger.
        /// </summary>
        protected void Log(string message, LogCategory category = LogCategory.System) {
            _debugger?.Log(message, category);
        }
        
        /// <summary>
        /// Logs a warning to the debugger.
        /// </summary>
        protected void LogWarning(string message, LogCategory category = LogCategory.System) {
            _debugger?.LogWarning(message, category);
        }
        
        /// <summary>
        /// Logs an error to the debugger and triggers the OnError event.
        /// </summary>
        protected void LogError(string message, LogCategory category = LogCategory.System) {
            _debugger?.LogError(message, category);
            OnError?.Invoke(message);
        }
        
        /// <summary>
        /// Sets the processing state and triggers appropriate events.
        /// </summary>
        protected void SetProcessingState(bool isProcessing) {
            _isProcessing = isProcessing;
            if (isProcessing) {
                OnProcessingStarted?.Invoke();
            } else {
                OnProcessingComplete?.Invoke();
            }
        }
    }
    
    /// <summary>
    /// Base class for components that process images or analysis results.
    /// </summary>
    public abstract class TraversifyProcessor : TraversifyComponent {
        [Header("Processing Settings")]
        [Tooltip("Maximum processing time before timeout (seconds)")]
        [SerializeField] protected float _processingTimeout = 300f;
        
        [Tooltip("Whether to use GPU acceleration when available")]
        [SerializeField] protected bool _useGPUAcceleration = true;
        
        [Tooltip("Number of worker threads for CPU operations")]
        [Range(1, 16)]
        [SerializeField] protected int _workerThreadCount = 4;
        
        // Processing state
        protected CancellationTokenSource _cancellationSource;
        protected float _processingStartTime;
        protected float _processingProgress;
        protected string _processingStage;
        
        // Progress events
        public UnityEvent<float> OnProgressUpdate = new UnityEvent<float>();
        public UnityEvent<string, float> OnStageProgressUpdate = new UnityEvent<string, float>();
        
        /// <summary>
        /// Current processing progress (0-1).
        /// </summary>
        public float ProcessingProgress => _processingProgress;
        
        /// <summary>
        /// Current processing stage description.
        /// </summary>
        public string ProcessingStage => _processingStage;
        
        /// <summary>
        /// Cancels any ongoing processing operation.
        /// </summary>
        public virtual void CancelProcessing() {
            if (!_isProcessing) return;
            
            _cancellationSource?.Cancel();
            SetProcessingState(false);
            _debugger?.Log($"{GetType().Name} processing cancelled", LogCategory.Process);
        }
        
        /// <summary>
        /// Updates progress and notifies listeners.
        /// </summary>
        protected void UpdateProgress(float progress, string stage = null) {
            _processingProgress = Mathf.Clamp01(progress);
            
            if (stage != null) {
                _processingStage = stage;
                OnStageProgressUpdate?.Invoke(stage, progress);
            }
            
            OnProgressUpdate?.Invoke(progress);
            
            // Check for timeout
            if (Time.realtimeSinceStartup - _processingStartTime > _processingTimeout) {
                CancelProcessing();
                LogError($"Processing timeout after {_processingTimeout} seconds", LogCategory.Process);
            }
        }
        
        /// <summary>
        /// Creates and initializes a cancellation token source for processing.
        /// </summary>
        protected CancellationToken InitProcessing() {
            if (_isProcessing) {
                LogWarning("Already processing, cannot start new operation", LogCategory.Process);
                return CancellationToken.None;
            }
            
            // Cancel any existing operation
            _cancellationSource?.Cancel();
            _cancellationSource?.Dispose();
            _cancellationSource = new CancellationTokenSource();
            
            // Reset state
            _processingStartTime = Time.realtimeSinceStartup;
            _processingProgress = 0f;
            _processingStage = "Initializing";
            
            SetProcessingState(true);
            return _cancellationSource.Token;
        }
    }
    
    /// <summary>
    /// Base class for components that generate visual content.
    /// </summary>
    public abstract class TraversifyGenerator : TraversifyProcessor {
        [Header("Generation Settings")]
        [Tooltip("Whether to save generated assets")]
        [SerializeField] protected bool _saveGeneratedAssets = true;
        
        [Tooltip("Directory to save generated assets")]
        [SerializeField] protected string _assetSavePath = "Assets/Generated";
        
        [Tooltip("Whether to generate metadata for saved assets")]
        [SerializeField] protected bool _generateMetadata = true;
        
        /// <summary>
        /// Saves the generated asset to disk.
        /// </summary>
        /// <param name="asset">Asset to save</param>
        /// <param name="name">Asset name</param>
        /// <returns>True if save was successful</returns>
        protected virtual bool SaveAsset(UnityEngine.Object asset, string name) {
            if (!_saveGeneratedAssets || asset == null) return false;
            
            #if UNITY_EDITOR
            try {
                // Ensure directory exists
                string directory = System.IO.Path.Combine(_assetSavePath, GetType().Name);
                if (!System.IO.Directory.Exists(directory)) {
                    System.IO.Directory.CreateDirectory(directory);
                }
                
                // Create asset path
                string filename = $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}";
                string assetPath = System.IO.Path.Combine(directory, filename);
                
                // Save asset
                UnityEditor.AssetDatabase.CreateAsset(asset, assetPath);
                UnityEditor.AssetDatabase.SaveAssets();
                
                _debugger?.Log($"Saved asset to {assetPath}", LogCategory.IO);
                return true;
            }
            catch (Exception ex) {
                LogError($"Failed to save asset: {ex.Message}", LogCategory.IO);
                return false;
            }
            #else
            LogWarning("Asset saving is only supported in the Unity Editor", LogCategory.IO);
            return false;
            #endif
        }
    }
    
    /// <summary>
    /// Base class for components that visualize data.
    /// </summary>
    public abstract class TraversifyVisualizer : TraversifyComponent {
        [Header("Visualization Settings")]
        [Tooltip("Whether to show debug visualizations")]
        [SerializeField] protected bool _showDebugVisualization = false;
        
        [Tooltip("Whether to animate visualization appearance")]
        [SerializeField] protected bool _animateVisualization = true;
        
        [Tooltip("Duration of appearance animation (seconds)")]
        [Range(0f, 3f)]
        [SerializeField] protected float _animationDuration = 0.5f;
        
        /// <summary>
        /// List of visualization objects created by this component.
        /// </summary>
        protected List<GameObject> _visualizationObjects = new List<GameObject>();
        
        /// <summary>
        /// Clears all visualization objects.
        /// </summary>
        public virtual void ClearVisualizations() {
            foreach (var obj in _visualizationObjects) {
                if (obj != null) {
                    DestroyImmediate(obj);
                }
            }
            
            _visualizationObjects.Clear();
            _debugger?.Log("Cleared visualizations", LogCategory.Visualization);
        }
        
        /// <summary>
        /// Creates a visualization container object.
        /// </summary>
        protected GameObject CreateVisualizationContainer(string name) {
            GameObject container = new GameObject(name);
            _visualizationObjects.Add(container);
            return container;
        }
        
        /// <summary>
        /// Creates a debug visualization if debug visualization is enabled.
        /// </summary>
        protected GameObject CreateDebugVisualization(string name, Vector3 position, Color color) {
            if (!_showDebugVisualization) return null;
            
            GameObject visualization = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visualization.name = name;
            visualization.transform.position = position;
            visualization.transform.localScale = Vector3.one * 0.2f;
            
            var renderer = visualization.GetComponent<Renderer>();
            if (renderer != null) {
                renderer.material.color = color;
            }
            
            _visualizationObjects.Add(visualization);
            return visualization;
        }
    }
    
    #endregion
    
    #region Environment Components
    
    // EnvironmentManager class moved to separate file: Scripts/Core/EnvironmentManager.cs
    // to avoid duplicate class definition
    
    #endregion
    
    #region Utility Components
    
    // ObjectPlacer class moved to separate file: Scripts/Terrain/ObjectPlacer.cs
    // to avoid duplicate class definition
    
    /// <summary>
    /// Tracks placement information for placed objects.
    /// </summary>
    [AddComponentMenu("Traversify/Utilities/Object Placement Tracker")]
    public class ObjectPlacementTracker : MonoBehaviour {
        [HideInInspector] public Vector2 normalizedPosition;
        [HideInInspector] public Vector3 originalPosition;
        [HideInInspector] public Quaternion originalRotation;
        [HideInInspector] public Vector3 originalScale;
        [HideInInspector] public UnityEngine.Terrain terrain;
        
        [Tooltip("Custom properties for this object")]
        public Dictionary<string, object> properties = new Dictionary<string, object>();
        
        public void SetProperty(string key, object value) {
            properties[key] = value;
        }
        
        public T GetProperty<T>(string key, T defaultValue = default) {
            if (properties.TryGetValue(key, out object value) && value is T typedValue) {
                return typedValue;
            }
            return defaultValue;
        }
    }
    
    // Billboard class moved to separate file: Scripts/Terrain/Billboard.cs
    // to avoid duplicate class definition
    
    /// <summary>
    /// Billboard rotation modes.
    /// </summary>
    public enum BillboardMode {
        LookAtCamera,
        CameraForward
    }
    
    /// <summary>
    /// Manages asset caching and loading.
    /// </summary>
    [AddComponentMenu("Traversify/Utilities/Asset Cache")]
    public class AssetCache : TraversifyComponent {
        [Header("Cache Settings")]
        [Tooltip("Maximum cache size in MB")]
        [SerializeField] private int _maxCacheSizeMB = 500;
        
        [Tooltip("Path to cache directory")]
        [SerializeField] private string _cachePath = "Cache";
        
        [Tooltip("Cache expiry time in days")]
        [SerializeField] private int _cacheExpiryDays = 30;
        
        // Cache state
        private Dictionary<string, CacheEntry> _cacheEntries = new Dictionary<string, CacheEntry>();
        private long _currentCacheSize = 0;
        
        /// <summary>
        /// Gets an item from the cache or loads it.
        /// </summary>
        /// <typeparam name="T">Type of cached asset</typeparam>
        /// <param name="key">Cache key</param>
        /// <param name="loadFunc">Function to load the asset if not cached</param>
        /// <returns>The cached or loaded asset</returns>
        public T GetOrLoad<T>(string key, Func<T> loadFunc) where T : UnityEngine.Object {
            // Check if item is in memory cache
            if (_cacheEntries.TryGetValue(key, out CacheEntry entry) && entry.asset is T typedAsset) {
                // Update last access time
                entry.lastAccessTime = DateTime.UtcNow;
                return typedAsset;
            }
            
            // Load the asset
            T asset = loadFunc();
            if (asset != null) {
                // Add to cache
                AddToCache(key, asset);
            }
            
            return asset;
        }
        
        /// <summary>
        /// Adds an asset to the cache.
        /// </summary>
        /// <param name="key">Cache key</param>
        /// <param name="asset">Asset to cache</param>
        private void AddToCache(string key, UnityEngine.Object asset) {
            // Estimate asset size
            long assetSize = EstimateAssetSize(asset);
            
            // Check if we need to make room in the cache
            if (_currentCacheSize + assetSize > _maxCacheSizeMB * 1024 * 1024) {
                MakeRoomInCache(assetSize);
            }
            
            // Add to cache
            _cacheEntries[key] = new CacheEntry {
                asset = asset,
                size = assetSize,
                lastAccessTime = DateTime.UtcNow
            };
            
            _currentCacheSize += assetSize;
        }
        
        /// <summary>
        /// Makes room in the cache by removing least recently used items.
        /// </summary>
        /// <param name="requiredSize">Required size in bytes</param>
        private void MakeRoomInCache(long requiredSize) {
            // Sort entries by last access time
            var entries = _cacheEntries.OrderBy(e => e.Value.lastAccessTime).ToList();
            
            // Remove entries until we have enough space
            foreach (var entry in entries) {
                // Skip if removing this entry won't help
                if (_currentCacheSize + requiredSize <= _maxCacheSizeMB * 1024 * 1024) {
                    break;
                }
                
                // Remove entry
                _currentCacheSize -= entry.Value.size;
                _cacheEntries.Remove(entry.Key);
                
                // Log removal
                _debugger?.Log($"Removed item {entry.Key} from cache ({entry.Value.size / 1024} KB)", LogCategory.System);
            }
        }
        
        /// <summary>
        /// Estimates the size of an asset in bytes.
        /// </summary>
        /// <param name="asset">Asset to estimate</param>
        /// <returns>Estimated size in bytes</returns>
        private long EstimateAssetSize(UnityEngine.Object asset) {
            if (asset is Texture2D texture) {
                // Estimate texture size based on resolution and format
                return texture.width * texture.height * GetBytesPerPixel(texture.format);
            }
            else if (asset is Mesh mesh) {
                // Estimate mesh size based on vertex count and topology
                long vertexSize = mesh.vertexCount * 12; // 3 floats per vertex * 4 bytes per float
                long normalSize = mesh.normals.Length * 12; // 3 floats per normal * 4 bytes per float
                long uvSize = mesh.uv.Length * 8; // 2 floats per UV * 4 bytes per float
                long triangleSize = mesh.triangles.Length * 4; // 4 bytes per index
                
                return vertexSize + normalSize + uvSize + triangleSize;
            }
            else if (asset is AudioClip clip) {
                // Estimate audio clip size based on length and channels
                return Mathf.RoundToInt(clip.length * clip.frequency * clip.channels * 2); // 2 bytes per sample (16-bit)
            }
            
            // Default estimate for other asset types
            return 1024 * 10; // 10 KB
        }
        
        /// <summary>
        /// Gets the number of bytes per pixel for a texture format.
        /// </summary>
        /// <param name="format">Texture format</param>
        /// <returns>Bytes per pixel</returns>
        private int GetBytesPerPixel(TextureFormat format) {
            switch (format) {
                case TextureFormat.Alpha8:
                    return 1;
                case TextureFormat.RGB24:
                    return 3;
                case TextureFormat.RGBA32:
                    return 4;
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                case TextureFormat.RGBA5551:
                    return 2;
                case TextureFormat.DXT1:
                    return 1; // Average bytes per pixel in DXT1
                case TextureFormat.DXT5:
                    return 1; // Average bytes per pixel in DXT5
                default:
                    return 4; // Default to 4 bytes per pixel
            }
        }
        
        /// <summary>
        /// Clears the cache.
        /// </summary>
        public void ClearCache() {
            _cacheEntries.Clear();
            _currentCacheSize = 0;
            
            _debugger?.Log("Cache cleared", LogCategory.System);
        }
        
        /// <summary>
        /// Component-specific initialization.
        /// </summary>
        protected override bool OnInitialize(object config) {
            // Create cache directory if it doesn't exist
            string fullPath = System.IO.Path.Combine(Application.persistentDataPath, _cachePath);
            if (!System.IO.Directory.Exists(fullPath)) {
                System.IO.Directory.CreateDirectory(fullPath);
            }
            
            return true;
        }
        
        /// <summary>
        /// Cache entry structure.
        /// </summary>
        private struct CacheEntry {
            public UnityEngine.Object asset;
            public long size;
            public DateTime lastAccessTime;
        }
    }
    
    #endregion
    
    #region Analysis Components
    
    /// <summary>
    /// Analyzes terrain features in a map or terrain.
    /// </summary>
    [AddComponentMenu("Traversify/Analysis/Terrain Analyzer")]
    public class TerrainAnalyzer : TraversifyProcessor {
        [Header("Analysis Settings")]
        [Tooltip("Minimum height difference to detect a feature")]
        [SerializeField] private float _minHeightDifference = 1f;
        
        [Tooltip("Slope threshold for feature detection (degrees)")]
        [Range(0, 90)]
        [SerializeField] private float _slopeThreshold = 30f;
        
        [Tooltip("Minimum feature size (percentage of total area)")]
        [Range(0, 1)]
        [SerializeField] private float _minFeatureSize = 0.01f;
        
        /// <summary>
        /// Analyzes a terrain to identify features.
        /// </summary>
        /// <param name="terrain">Terrain to analyze</param>
        /// <param name="onComplete">Callback when analysis is complete</param>
        /// <returns>Coroutine enumerator</returns>
        public IEnumerator AnalyzeTerrain(UnityEngine.Terrain terrain, Action<TerrainAnalysisResult> onComplete) {
            if (terrain == null) {
                LogError("Cannot analyze null terrain", LogCategory.Terrain);
                yield break;
            }
            
            CancellationToken token = InitProcessing();
            _debugger?.StartTimer("TerrainAnalysis");
            
            try {
                TerrainData terrainData = terrain.terrainData;
                TerrainAnalysisResult result = new TerrainAnalysisResult();
                
                // Get heightmap data
                int resolution = terrainData.heightmapResolution;
                float[,] heights = terrainData.GetHeights(0, 0, resolution, resolution);
                
                UpdateProgress(0.1f, "Analyzing height distribution");
                
                // Analyze height distribution
                float minHeight = float.MaxValue;
                float maxHeight = float.MinValue;
                float totalHeight = 0f;
                
                for (int y = 0; y < resolution; y++) {
                    for (int x = 0; x < resolution; x++) {
                        float height = heights[y, x] * terrainData.size.y;
                        minHeight = Mathf.Min(minHeight, height);
                        maxHeight = Mathf.Max(maxHeight, height);
                        totalHeight += height;
                    }
                    
                    if (y % 10 == 0) {
                        UpdateProgress(0.1f + 0.2f * y / resolution, "Analyzing height distribution");
                        yield return null;
                    }
                }
                
                float averageHeight = totalHeight / (resolution * resolution);
                
                result.minHeight = minHeight;
                result.maxHeight = maxHeight;
                result.averageHeight = averageHeight;
                result.heightRange = maxHeight - minHeight;
                
                // Create slope map
                UpdateProgress(0.3f, "Calculating slopes");
                float[,] slopes = new float[resolution, resolution];
                
                for (int y = 1; y < resolution - 1; y++) {
                    for (int x = 1; x < resolution - 1; x++) {
                        // Calculate slope using central differences
                        float dx = (heights[y, x + 1] - heights[y, x - 1]) * 0.5f;
                        float dy = (heights[y + 1, x] - heights[y - 1, x]) * 0.5f;
                        
                        // Convert to slope angle in degrees
                        float slope = Mathf.Atan(new Vector2(dx, dy).magnitude / terrainData.heightmapScale.x) * Mathf.Rad2Deg;
                        slopes[y, x] = slope;
                    }
                    
                    if (y % 10 == 0) {
                        UpdateProgress(0.3f + 0.2f * y / resolution, "Calculating slopes");
                        yield return null;
                    }
                }
                
                // Detect regions using flood fill
                UpdateProgress(0.5f, "Detecting terrain regions");
                bool[,] visited = new bool[resolution, resolution];
                List<TerrainRegion> regions = new List<TerrainRegion>();
                
                for (int y = 0; y < resolution; y++) {
                    for (int x = 0; x < resolution; x++) {
                        if (!visited[y, x]) {
                            TerrainRegion region = DetectRegion(heights, slopes, visited, x, y, resolution, terrainData);
                            
                            // Filter small regions
                            float regionSize = region.pixels.Count / (float)(resolution * resolution);
                            if (regionSize >= _minFeatureSize) {
                                regions.Add(region);
                            }
                        }
                    }
                    
                    if (y % 10 == 0) {
                        UpdateProgress(0.5f + 0.3f * y / resolution, "Detecting terrain regions");
                        yield return null;
                    }
                }
                
                // Classify regions
                UpdateProgress(0.8f, "Classifying terrain regions");
                foreach (var region in regions) {
                    region.type = ClassifyTerrainRegion(region, terrainData);
                }
                
                result.regions = regions;
                
                // Calculate additional metrics
                UpdateProgress(0.9f, "Calculating terrain metrics");
                result.slopeDistribution = CalculateSlopeDistribution(slopes);
                result.roughness = CalculateRoughness(heights, terrainData);
                
                UpdateProgress(1.0f, "Terrain analysis complete");
                
                float analysisTime = _debugger.StopTimer("TerrainAnalysis");
                result.analysisTime = analysisTime;
                
                onComplete?.Invoke(result);
            }
            catch (Exception ex) {
                LogError($"Terrain analysis failed: {ex.Message}", LogCategory.Terrain);
            }
            finally {
                SetProcessingState(false);
            }
        }
        
        /// <summary>
        /// Detects a region using flood fill algorithm.
        /// </summary>
        private TerrainRegion DetectRegion(float[,] heights, float[,] slopes, bool[,] visited, int startX, int startY, int resolution, TerrainData terrainData) {
            TerrainRegion region = new TerrainRegion();
            region.pixels = new List<Vector2Int>();
            
            float regionHeight = heights[startY, startX] * terrainData.size.y;
            float regionSlope = slopes[startY, startX];
            
            // Tolerance for considering pixels part of the same region
            float heightTolerance = _minHeightDifference;
            float slopeTolerance = _slopeThreshold;
            
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(startX, startY));
            visited[startY, startX] = true;
            
            // Bounds of the region
            int minX = startX, maxX = startX, minY = startY, maxY = startY;
            
            // Total height and slope
            float totalHeight = regionHeight;
            float totalSlope = regionSlope;
            
            while (queue.Count > 0) {
                Vector2Int current = queue.Dequeue();
                region.pixels.Add(current);
                
                // Check each neighbor
                for (int dy = -1; dy <= 1; dy++) {
                    for (int dx = -1; dx <= 1; dx++) {
                        if (dx == 0 && dy == 0) continue;
                        
                        int nx = current.x + dx;
                        int ny = current.y + dy;
                        
                        if (nx >= 0 && nx < resolution && ny >= 0 && ny < resolution && !visited[ny, nx]) {
                            float neighborHeight = heights[ny, nx] * terrainData.size.y;
                            float neighborSlope = slopes[ny, nx];
                            
                            // Check if neighbor is part of the same region
                            if (Mathf.Abs(neighborHeight - regionHeight) <= heightTolerance &&
                                Mathf.Abs(neighborSlope - regionSlope) <= slopeTolerance) {
                                visited[ny, nx] = true;
                                queue.Enqueue(new Vector2Int(nx, ny));
                                
                                // Update region bounds
                                minX = Mathf.Min(minX, nx);
                                maxX = Mathf.Max(maxX, nx);
                                minY = Mathf.Min(minY, ny);
                                maxY = Mathf.Max(maxY, ny);
                                
                                // Update totals
                                totalHeight += neighborHeight;
                                totalSlope += neighborSlope;
                            }
                        }
                    }
                }
            }
            
            // Calculate region properties
            region.averageHeight = totalHeight / region.pixels.Count;
            region.averageSlope = totalSlope / region.pixels.Count;
            
            // Convert to world bounds
            float pixelSize = terrainData.size.x / (resolution - 1);
            region.bounds = new Rect(
                minX * pixelSize,
                minY * pixelSize,
                (maxX - minX + 1) * pixelSize,
                (maxY - minY + 1) * pixelSize
            );
            
            return region;
        }
        
        /// <summary>
        /// Classifies a terrain region based on its properties.
        /// </summary>
        private TerrainRegionType ClassifyTerrainRegion(TerrainRegion region, TerrainData terrainData) {
            // Simple classification rules
            if (region.averageHeight < terrainData.size.y * 0.2f) {
                if (region.averageSlope < 5f) {
                    return TerrainRegionType.Flatland;
                }
                return TerrainRegionType.Plain;
            }
            else if (region.averageHeight < terrainData.size.y * 0.5f) {
                if (region.averageSlope > 20f) {
                    return TerrainRegionType.Hill;
                }
                return TerrainRegionType.Lowland;
            }
            else if (region.averageHeight < terrainData.size.y * 0.8f) {
                if (region.averageSlope > 30f) {
                    return TerrainRegionType.Mountain;
                }
                return TerrainRegionType.Highland;
            }
            else {
                if (region.averageSlope > 40f) {
                    return TerrainRegionType.Peak;
                }
                return TerrainRegionType.Plateau;
            }
        }
        
        /// <summary>
        /// Calculates the slope distribution of the terrain.
        /// </summary>
        private Dictionary<string, float> CalculateSlopeDistribution(float[,] slopes) {
            int resolution = slopes.GetLength(0);
            int[] slopeBuckets = new int[9]; // 0-10, 10-20, ..., 80-90 degrees
            
            for (int y = 0; y < resolution; y++) {
                for (int x = 0; x < resolution; x++) {
                    float slope = slopes[y, x];
                    int bucketIndex = Mathf.Min(8, Mathf.FloorToInt(slope / 10f));
                    slopeBuckets[bucketIndex]++;
                }
            }
            
            // Convert to percentages
            Dictionary<string, float> distribution = new Dictionary<string, float>();
            float totalPixels = resolution * resolution;
            
            for (int i = 0; i < slopeBuckets.Length; i++) {
                string key = $"{i * 10}-{(i + 1) * 10}";
                distribution[key] = slopeBuckets[i] / totalPixels;
            }
            
            return distribution;
        }
        
        /// <summary>
        /// Calculates the overall roughness of the terrain.
        /// </summary>
        private float CalculateRoughness(float[,] heights, TerrainData terrainData) {
            int resolution = heights.GetLength(0);
            float totalVariation = 0f;
            
            for (int y = 1; y < resolution - 1; y++) {
                for (int x = 1; x < resolution - 1; x++) {
                    float center = heights[y, x];
                    float[] neighbors = new float[] {
                        heights[y - 1, x],
                        heights[y + 1, x],
                        heights[y, x - 1],
                        heights[y, x + 1]
                    };
                    
                    float localVariation = 0f;
                    foreach (float neighbor in neighbors) {
                        localVariation += Mathf.Abs(neighbor - center);
                    }
                    
                    totalVariation += localVariation / 4f;
                }
            }
            
            return totalVariation / ((resolution - 2) * (resolution - 2)) * terrainData.size.y;
        }
        
        /// <summary>
        /// Component-specific initialization.
        /// </summary>
        protected override bool OnInitialize(object config) {
            return true;
        }
    }
    
    /// <summary>
    /// Type of terrain region.
    /// </summary>
    public enum TerrainRegionType {
        Unknown,
        Flatland,
        Plain,
        Lowland,
        Hill,
        Highland,
        Mountain,
        Plateau,
        Peak,
        Valley,
        Canyon,
        Basin
    }
    
    /// <summary>
    /// Represents a detected terrain region.
    /// </summary>
    public class TerrainRegion {
        public TerrainRegionType type;
        public List<Vector2Int> pixels;
        public Rect bounds;
        public float averageHeight;
        public float averageSlope;
    }
    
    /// <summary>
    /// Results of terrain analysis.
    /// </summary>
    public class TerrainAnalysisResult {
        public float minHeight;
        public float maxHeight;
        public float averageHeight;
        public float heightRange;
        public float roughness;
        public Dictionary<string, float> slopeDistribution;
        public List<TerrainRegion> regions;
        public float analysisTime;
    }
    
    #endregion
}
