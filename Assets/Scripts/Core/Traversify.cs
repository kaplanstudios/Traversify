/*************************************************************************
 *  Traversify – Traversify.cs                                           *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 02:59:44 UTC                                     *
 *  Desc   : Central controller for the Traversify environment generation*
 *           system. Coordinates all components including map analysis,  *
 *           terrain generation, model creation, and visualization.      *
 *           Provides the main API entry point for the entire system.    *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Traversify.AI;
using Traversify.Core;

namespace Traversify {
    /// <summary>
    /// Main controller for the Traversify environment generation system.
    /// Coordinates all components and provides the primary API for the entire system.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TraversifyDebugger))]
    public class Traversify : MonoBehaviour {
        #region Singleton Pattern
        
        private static Traversify _instance;
        
        /// <summary>
        /// Singleton instance of the Traversify controller.
        /// </summary>
        public static Traversify Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<Traversify>();
                    if (_instance == null) {
                        GameObject go = new GameObject("Traversify");
                        _instance = go.AddComponent<Traversify>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Properties
        
        [Header("Core Components")]
        [Tooltip("Debug and logging system")]
        [SerializeField] private TraversifyDebugger _debugger;
        
        [Header("AI Components")]
        [Tooltip("Map analysis component")]
        [SerializeField] private MapAnalyzer _mapAnalyzer;
        
        [Tooltip("OpenAI integration component")]
        [SerializeField] private OpenAIResponse _openAIResponse;
        
        [Header("Terrain Components")]
        [Tooltip("Terrain generation component")]
        [SerializeField] private TerrainGenerator _terrainGenerator;
        
        [Tooltip("Environment manager component")]
        [SerializeField] private EnvironmentManager _environmentManager;
        
        [Header("Model Components")]
        [Tooltip("Model generation component")]
        [SerializeField] private ModelGenerator _modelGenerator;
        
        [Tooltip("Object placement component")]
        [SerializeField] private ObjectPlacer _objectPlacer;
        
        [Header("Visualization Components")]
        [Tooltip("Segmentation visualization component")]
        [SerializeField] private SegmentationVisualizer _segmentationVisualizer;
        
        [Header("Configuration")]
        [Tooltip("API keys configuration")]
        [SerializeField] private APIConfiguration _apiConfig = new APIConfiguration();
        
        [Tooltip("System settings")]
        [SerializeField] private SystemSettings _systemSettings = new SystemSettings();
        
        [Tooltip("Performance settings")]
        [SerializeField] private PerformanceSettings _performanceSettings = new PerformanceSettings();
        
        [Tooltip("Visualization settings")]
        [SerializeField] private VisualizationSettings _visualizationSettings = new VisualizationSettings();
        
        [Header("Assets")]
        [Tooltip("YOLO model asset")]
        [SerializeField] private UnityEngine.Object _yoloModel;
        
        [Tooltip("SAM2 model asset")]
        [SerializeField] private UnityEngine.Object _sam2Model;
        
        [Tooltip("Faster R-CNN model asset")]
        [SerializeField] private UnityEngine.Object _fasterRcnnModel;
        
        [Tooltip("Text asset containing class labels")]
        [SerializeField] private TextAsset _classLabels;
        
        [Header("Output Settings")]
        [Tooltip("Save generated assets")]
        [SerializeField] private bool _saveGeneratedAssets = true;
        
        [Tooltip("Directory to save generated assets")]
        [SerializeField] private string _assetSavePath = "Assets/Generated";
        
        [Tooltip("Generate metadata for saved assets")]
        [SerializeField] private bool _generateMetadata = true;
        
        #endregion
        
        #region Private Fields
        
        // System state
        private bool _isInitialized = false;
        private bool _isProcessing = false;
        private CancellationTokenSource _cancellationTokenSource;
        
        // Processing state
        private AnalysisResults _currentAnalysisResults;
        private Texture2D _currentMapTexture;
        private UnityEngine.Terrain _generatedTerrain;
        private List<GameObject> _generatedObjects = new List<GameObject>();
        private string _currentSessionId = string.Empty;
        
        // Component cache
        private Dictionary<Type, TraversifyComponent> _componentCache = new Dictionary<Type, TraversifyComponent>();
        
        // Performance tracking
        private Dictionary<string, float> _performanceMetrics = new Dictionary<string, float>();
        private float _processingStartTime;
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when the system has been initialized.
        /// </summary>
        public UnityEvent OnInitialized = new UnityEvent();
        
        /// <summary>
        /// Fired when processing starts.
        /// </summary>
        public UnityEvent OnProcessingStarted = new UnityEvent();
        
        /// <summary>
        /// Fired when processing completes successfully.
        /// </summary>
        public UnityEvent OnProcessingComplete = new UnityEvent();
        
        /// <summary>
        /// Fired when processing is cancelled.
        /// </summary>
        public UnityEvent OnProcessingCancelled = new UnityEvent();
        
        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public UnityEvent<string> OnError = new UnityEvent<string>();
        
        /// <summary>
        /// Fired when map analysis is complete.
        /// </summary>
        public UnityEvent<AnalysisResults> OnAnalysisComplete = new UnityEvent<AnalysisResults>();
        
        /// <summary>
        /// Fired when a terrain has been generated.
        /// </summary>
        public UnityEvent<UnityEngine.Terrain> OnTerrainGenerated = new UnityEvent<UnityEngine.Terrain>();
        
        /// <summary>
        /// Fired when models have been generated and placed.
        /// </summary>
        public UnityEvent<List<GameObject>> OnModelsPlaced = new UnityEvent<List<GameObject>>();
        
        /// <summary>
        /// Fired when visualization is complete.
        /// </summary>
        public UnityEvent<List<GameObject>> OnVisualizationComplete = new UnityEvent<List<GameObject>>();
        
        /// <summary>
        /// Fired when processing progress updates.
        /// </summary>
        public UnityEvent<float> OnProgressUpdate = new UnityEvent<float>();
        
        /// <summary>
        /// Fired when processing stage changes.
        /// </summary>
        public UnityEvent<string, float> OnStageUpdate = new UnityEvent<string, float>();
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Whether the system is currently initialized.
        /// </summary>
        public bool IsInitialized => _isInitialized;
        
        /// <summary>
        /// Whether the system is currently processing.
        /// </summary>
        public bool IsProcessing => _isProcessing;
        
        /// <summary>
        /// Current analysis results, if any.
        /// </summary>
        public AnalysisResults CurrentAnalysisResults => _currentAnalysisResults;
        
        /// <summary>
        /// Current map texture being processed, if any.
        /// </summary>
        public Texture2D CurrentMapTexture => _currentMapTexture;
        
        /// <summary>
        /// Most recently generated terrain, if any.
        /// </summary>
        public UnityEngine.Terrain GeneratedTerrain => _generatedTerrain;
        
        /// <summary>
        /// List of generated objects from the current session.
        /// </summary>
        public List<GameObject> GeneratedObjects => new List<GameObject>(_generatedObjects);
        
        /// <summary>
        /// ID of the current processing session.
        /// </summary>
        public string CurrentSessionId => _currentSessionId;
        
        /// <summary>
        /// System configuration settings.
        /// </summary>
        public SystemSettings Settings => _systemSettings;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Get required components
            if (_debugger == null) {
                _debugger = GetComponent<TraversifyDebugger>();
                if (_debugger == null) {
                    _debugger = gameObject.AddComponent<TraversifyDebugger>();
                }
            }
            
            // Log startup
            _debugger.Log("Traversify controller initializing", LogCategory.System);
        }
        
        private void Start() {
            if (!InitializeComponents()) {
                _debugger.LogError("Failed to initialize Traversify components", LogCategory.System);
                return;
            }
            
            _isInitialized = true;
            _debugger.Log("Traversify controller initialized successfully", LogCategory.System);
            OnInitialized?.Invoke();
        }
        
        private void OnDestroy() {
            // Cancel any ongoing processing
            if (_isProcessing) {
                CancelProcessing();
            }
            
            // Clean up generated objects
            CleanupGeneratedObjects();
            
            // Dispose components
            DisposeComponents();
            
            _debugger.Log("Traversify controller destroyed", LogCategory.System);
        }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initialize all required components.
        /// </summary>
        private bool InitializeComponents() {
            try {
                // Create component container if needed
                GameObject componentContainer = GameObject.Find("TraversifyComponents");
                if (componentContainer == null) {
                    componentContainer = new GameObject("TraversifyComponents");
                    componentContainer.transform.SetParent(transform);
                }
                
                // Initialize MapAnalyzer
                if (_mapAnalyzer == null) {
                    _mapAnalyzer = componentContainer.GetComponentInChildren<MapAnalyzer>();
                    if (_mapAnalyzer == null) {
                        _mapAnalyzer = componentContainer.AddComponent<MapAnalyzer>();
                    }
                }
                _componentCache[typeof(MapAnalyzer)] = _mapAnalyzer;
                
                // Initialize OpenAIResponse
                if (_openAIResponse == null) {
                    _openAIResponse = componentContainer.GetComponentInChildren<OpenAIResponse>();
                    if (_openAIResponse == null) {
                        _openAIResponse = componentContainer.AddComponent<OpenAIResponse>();
                    }
                }
                _openAIResponse.SetApiKey(_apiConfig.openAIApiKey);
                
                // Initialize TerrainGenerator
                if (_terrainGenerator == null) {
                    _terrainGenerator = componentContainer.GetComponentInChildren<TerrainGenerator>();
                    if (_terrainGenerator == null) {
                        _terrainGenerator = componentContainer.AddComponent<TerrainGenerator>();
                    }
                }
                _componentCache[typeof(TerrainGenerator)] = _terrainGenerator;
                
                // Initialize ModelGenerator
                if (_modelGenerator == null) {
                    _modelGenerator = componentContainer.GetComponentInChildren<ModelGenerator>();
                    if (_modelGenerator == null) {
                        _modelGenerator = componentContainer.AddComponent<ModelGenerator>();
                    }
                }
                _componentCache[typeof(ModelGenerator)] = _modelGenerator;
                
                // Initialize SegmentationVisualizer
                if (_segmentationVisualizer == null) {
                    _segmentationVisualizer = componentContainer.GetComponentInChildren<SegmentationVisualizer>();
                    if (_segmentationVisualizer == null) {
                        _segmentationVisualizer = componentContainer.AddComponent<SegmentationVisualizer>();
                    }
                }
                _componentCache[typeof(SegmentationVisualizer)] = _segmentationVisualizer;
                
                // Initialize EnvironmentManager
                if (_environmentManager == null) {
                    _environmentManager = componentContainer.GetComponentInChildren<EnvironmentManager>();
                    if (_environmentManager == null) {
                        _environmentManager = componentContainer.AddComponent<EnvironmentManager>();
                    }
                }
                _componentCache[typeof(EnvironmentManager)] = _environmentManager;
                
                // Initialize ObjectPlacer
                if (_objectPlacer == null) {
                    _objectPlacer = componentContainer.GetComponentInChildren<ObjectPlacer>();
                    if (_objectPlacer == null) {
                        _objectPlacer = componentContainer.AddComponent<ObjectPlacer>();
                    }
                }
                _componentCache[typeof(ObjectPlacer)] = _objectPlacer;
                
                // Initialize all components
                foreach (var component in _componentCache.Values) {
                    // Check if component implements initialization interface
                    if (component is MonoBehaviour monoBehaviour) {
                        // Try to call Initialize method if it exists
                        var initMethod = component.GetType().GetMethod("Initialize");
                        if (initMethod != null) {
                            try {
                                initMethod.Invoke(component, null);
                            } catch (Exception ex) {
                                _debugger?.LogError($"Failed to initialize component {component.GetType().Name}: {ex.Message}", LogCategory.System);
                            }
                        }
                    }
                }
                
                // Configure worker factory
                WorkerFactory.Initialize((message, category, level) => {
                    switch (level) {
                        case LogLevel.Info:
                            _debugger.Log(message, category);
                            break;
                        case LogLevel.Warning:
                            _debugger.LogWarning(message, category);
                            break;
                        case LogLevel.Error:
                        case LogLevel.Critical:
                            _debugger.LogError(message, category);
                            break;
                        default:
                            _debugger.LogVerbose(message, category);
                            break;
                    }
                });
                
                // Create unique session ID
                _currentSessionId = Guid.NewGuid().ToString();
                
                return true;
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to initialize components: {ex.Message}\n{ex.StackTrace}", LogCategory.System);
                OnError?.Invoke($"Initialization error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Dispose of all components.
        /// </summary>
        private void DisposeComponents() {
            foreach (var component in _componentCache.Values) {
                if (component is TraversifyComponent traversifyComponent) {
                    // Most components don't need explicit disposal,
                    // but if they did, we'd call it here
                }
            }
            
            // Clean up WorkerFactory
            WorkerFactory.DisposeAll();
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Generate a complete environment from a map image.
        /// </summary>
        /// <param name="mapTexture">Map texture to analyze</param>
        /// <param name="onComplete">Callback when generation is complete</param>
        /// <param name="onError">Callback when an error occurs</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <returns>Coroutine enumerator</returns>
        public Coroutine GenerateEnvironment(
            Texture2D mapTexture,
            Action onComplete = null,
            Action<string> onError = null,
            Action<float> onProgress = null
        ) {
            if (!_isInitialized) {
                onError?.Invoke("Traversify not initialized");
                return null;
            }
            
            if (_isProcessing) {
                onError?.Invoke("Processing already in progress");
                return null;
            }
            
            if (mapTexture == null) {
                onError?.Invoke("Map texture cannot be null");
                return null;
            }
            
            // Start generation coroutine
            return StartCoroutine(GenerateEnvironmentCoroutine(mapTexture, onComplete, onError, onProgress));
        }
        
        /// <summary>
        /// Cancel the current processing operation.
        /// </summary>
        public void CancelProcessing() {
            if (!_isProcessing) return;
            
            _debugger.Log("Cancelling processing", LogCategory.Process);
            
            // Signal cancellation
            _cancellationTokenSource?.Cancel();
            
            // Reset processing state
            _isProcessing = false;
            
            // Fire cancelled event
            OnProcessingCancelled?.Invoke();
            
            _debugger.Log("Processing cancelled", LogCategory.Process);
        }
        
        /// <summary>
        /// Clear all generated objects and terrain.
        /// </summary>
        public void ClearGeneratedEnvironment() {
            _debugger.Log("Clearing generated environment", LogCategory.System);
            
            // Clean up generated objects
            CleanupGeneratedObjects();
            
            // Reset state
            _currentAnalysisResults = null;
            _currentMapTexture = null;
            _generatedTerrain = null;
            
            _debugger.Log("Environment cleared", LogCategory.System);
        }
        
        /// <summary>
        /// Analyze a map without generating terrain.
        /// </summary>
        /// <param name="mapTexture">Map texture to analyze</param>
        /// <param name="onComplete">Callback when analysis is complete</param>
        /// <param name="onError">Callback when an error occurs</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <returns>Coroutine enumerator</returns>
        public Coroutine AnalyzeMap(
            Texture2D mapTexture,
            Action<AnalysisResults> onComplete = null,
            Action<string> onError = null,
            Action<float> onProgress = null
        ) {
            if (!_isInitialized) {
                onError?.Invoke("Traversify not initialized");
                return null;
            }
            
            if (_isProcessing) {
                onError?.Invoke("Processing already in progress");
                return null;
            }
            
            if (mapTexture == null) {
                onError?.Invoke("Map texture cannot be null");
                return null;
            }
            
            // Start analysis coroutine
            return StartCoroutine(AnalyzeMapCoroutine(mapTexture, onComplete, onError, onProgress));
        }
        
        /// <summary>
        /// Generate terrain from existing analysis results.
        /// </summary>
        /// <param name="analysisResults">Analysis results to use</param>
        /// <param name="onComplete">Callback when generation is complete</param>
        /// <param name="onError">Callback when an error occurs</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <returns>Coroutine enumerator</returns>
        public Coroutine GenerateTerrainFromAnalysis(
            AnalysisResults analysisResults,
            Action<UnityEngine.Terrain> onComplete = null,
            Action<string> onError = null,
            Action<float> onProgress = null
        ) {
            if (!_isInitialized) {
                onError?.Invoke("Traversify not initialized");
                return null;
            }
            
            if (_isProcessing) {
                onError?.Invoke("Processing already in progress");
                return null;
            }
            
            if (analysisResults == null) {
                onError?.Invoke("Analysis results cannot be null");
                return null;
            }
            
            // Start terrain generation coroutine
            return StartCoroutine(GenerateTerrainCoroutine(analysisResults, onComplete, onError, onProgress));
        }
        
        /// <summary>
        /// Generate and place models from existing analysis results and terrain.
        /// </summary>
        /// <param name="analysisResults">Analysis results to use</param>
        /// <param name="terrain">Terrain to place models on</param>
        /// <param name="onComplete">Callback when generation is complete</param>
        /// <param name="onError">Callback when an error occurs</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <returns>Coroutine enumerator</returns>
        public Coroutine GenerateModelsFromAnalysis(
            AnalysisResults analysisResults,
            UnityEngine.Terrain terrain,
            Action<List<GameObject>> onComplete = null,
            Action<string> onError = null,
            Action<float> onProgress = null
        ) {
            if (!_isInitialized) {
                onError?.Invoke("Traversify not initialized");
                return null;
            }
            
            if (_isProcessing) {
                onError?.Invoke("Processing already in progress");
                return null;
            }
            
            if (analysisResults == null) {
                onError?.Invoke("Analysis results cannot be null");
                return null;
            }
            
            if (terrain == null) {
                onError?.Invoke("Terrain cannot be null");
                return null;
            }
            
            // Start model generation coroutine
            return StartCoroutine(GenerateModelsCoroutine(analysisResults, terrain, onComplete, onError, onProgress));
        }
        
        /// <summary>
        /// Visualize segmentation from existing analysis results.
        /// </summary>
        /// <param name="analysisResults">Analysis results to visualize</param>
        /// <param name="terrain">Terrain to visualize on</param>
        /// <param name="onComplete">Callback when visualization is complete</param>
        /// <param name="onError">Callback when an error occurs</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <returns>Coroutine enumerator</returns>
        public Coroutine VisualizeSegmentation(
            AnalysisResults analysisResults,
            UnityEngine.Terrain terrain,
            Action<List<GameObject>> onComplete = null,
            Action<string> onError = null,
            Action<float> onProgress = null
        ) {
            if (!_isInitialized) {
                onError?.Invoke("Traversify not initialized");
                return null;
            }
            
            if (analysisResults == null) {
                onError?.Invoke("Analysis results cannot be null");
                return null;
            }
            
            if (terrain == null) {
                onError?.Invoke("Terrain cannot be null");
                return null;
            }
            
            // Start visualization coroutine
            return StartCoroutine(VisualizeSegmentationCoroutine(
                analysisResults, terrain, _currentMapTexture, onComplete, onError, onProgress));
        }
        
        /// <summary>
        /// Save the current environment to disk.
        /// </summary>
        /// <param name="path">Path to save to (null for default)</param>
        /// <param name="includeTextures">Whether to include textures</param>
        /// <param name="includeMeshes">Whether to include meshes</param>
        /// <returns>Path to saved environment, or null if save failed</returns>
        public string SaveEnvironment(string path = null, bool includeTextures = true, bool includeMeshes = true) {
            if (!_isInitialized) {
                _debugger.LogError("Cannot save environment: Traversify not initialized", LogCategory.IO);
                return null;
            }
            
            if (_currentAnalysisResults == null || _generatedTerrain == null) {
                _debugger.LogError("Cannot save environment: No environment generated", LogCategory.IO);
                return null;
            }
            
            try {
                // Generate path if not specified
                if (string.IsNullOrEmpty(path)) {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string baseName = _currentMapTexture != null ? _currentMapTexture.name : "Environment";
                    path = Path.Combine(_assetSavePath, $"{baseName}_{timestamp}");
                }
                
                // Ensure directory exists
                Directory.CreateDirectory(path);
                
                // Save analysis results as JSON
                string analysisPath = Path.Combine(path, "analysis.json");
                //File.WriteAllText(analysisPath, JsonUtility.ToJson(_currentAnalysisResults, true));
                
                // Save textures if requested
                if (includeTextures && _currentMapTexture != null) {
                    string mapPath = Path.Combine(path, "map.png");
                    File.WriteAllBytes(mapPath, _currentMapTexture.EncodeToPNG());
                    
                    if (_currentAnalysisResults.heightMap != null) {
                        string heightmapPath = Path.Combine(path, "heightmap.png");
                        File.WriteAllBytes(heightmapPath, _currentAnalysisResults.heightMap.EncodeToPNG());
                    }
                    
                    if (_currentAnalysisResults.segmentationMap != null) {
                        string segmentationPath = Path.Combine(path, "segmentation.png");
                        File.WriteAllBytes(segmentationPath, _currentAnalysisResults.segmentationMap.EncodeToPNG());
                    }
                }
                
                // Save metadata
                if (_generateMetadata) {
                    SaveMetadata(path);
                }
                
                _debugger.Log($"Environment saved to {path}", LogCategory.IO);
                return path;
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to save environment: {ex.Message}", LogCategory.IO);
                return null;
            }
        }
        
        /// <summary>
        /// Get performance metrics from the current or last processing session.
        /// </summary>
        /// <returns>Dictionary of performance metrics</returns>
        public Dictionary<string, float> GetPerformanceMetrics() {
            return new Dictionary<string, float>(_performanceMetrics);
        }
        
        #endregion
        
        #region Implementation Coroutines
        
        /// <summary>
        /// Main environment generation coroutine.
        /// </summary>
        private IEnumerator GenerateEnvironmentCoroutine(
            Texture2D mapTexture,
            Action onComplete,
            Action<string> onError,
            Action<float> onProgress
        ) {
            // Use SafeCoroutine to handle the main logic with error handling
            yield return SafeCoroutine.Wrap(
                InnerGenerateEnvironmentCoroutine(mapTexture, onComplete, onProgress),
                ex => {
                    _debugger.LogError($"Environment generation failed: {ex.Message}", LogCategory.Process);
                    onError?.Invoke($"Generation error: {ex.Message}");
                    OnError?.Invoke($"Generation error: {ex.Message}");
                    _isProcessing = false;
                    _cancellationTokenSource = null;
                },
                LogCategory.Process
            );
        }
        
        /// <summary>
        /// Internal implementation of environment generation without try-catch around yields.
        /// </summary>
        private IEnumerator InnerGenerateEnvironmentCoroutine(
            Texture2D mapTexture,
            Action onComplete,
            Action<float> onProgress
        ) {
            // Initialize
            _isProcessing = true;
            _processingStartTime = Time.realtimeSinceStartup;
            _currentMapTexture = mapTexture;
            _cancellationTokenSource = new CancellationTokenSource();
            
            // Log
            _debugger.Log("Starting environment generation", LogCategory.Process);
            _debugger.StartTimer("TotalGeneration");
            
            // Fire started event
            OnProcessingStarted?.Invoke();
            
            // Update progress
            UpdateProgress(0.05f, "Initializing environment generation", onProgress);
            
            // Step 1: Analyze map (30% of progress)
            _debugger.StartTimer("MapAnalysis");
            
            AnalysisResults analysisResults = null;
            Exception analysisError = null;
            
            yield return StartCoroutine(AnalyzeMapCoroutine(
                mapTexture,
                results => analysisResults = results,
                error => analysisError = new Exception(error),
                progress => UpdateProgress(0.05f + progress * 0.25f, "Analyzing map", onProgress)
            ));
            
            // Check for cancellation
            if (_cancellationTokenSource.IsCancellationRequested) {
                throw new OperationCanceledException("Operation cancelled");
            }
            
            // Check for analysis error
            if (analysisError != null) {
                throw analysisError;
            }
            
            // Check for analysis results
            if (analysisResults == null) {
                throw new Exception("Map analysis failed to produce results");
            }
            
            // Store analysis results
            _currentAnalysisResults = analysisResults;
            
            // Log analysis performance
            float analysisTime = _debugger.StopTimer("MapAnalysis");
            _performanceMetrics["MapAnalysis"] = analysisTime;
            
            // Fire analysis complete event
            OnAnalysisComplete?.Invoke(analysisResults);
            
            // Step 2: Generate terrain (30% of progress)
            _debugger.StartTimer("TerrainGeneration");
            
            UpdateProgress(0.3f, "Generating terrain", onProgress);
            
            UnityEngine.Terrain terrain = null;
            Exception terrainError = null;
            
            yield return StartCoroutine(GenerateTerrainCoroutine(
                analysisResults,
                result => terrain = result,
                error => terrainError = new Exception(error),
                progress => UpdateProgress(0.3f + progress * 0.3f, "Generating terrain", onProgress)
            ));
            
            // Check for cancellation
            if (_cancellationTokenSource.IsCancellationRequested) {
                throw new OperationCanceledException("Operation cancelled");
            }
            
            // Check for terrain error
            if (terrainError != null) {
                throw terrainError;
            }
            
            // Check for terrain
            if (terrain == null) {
                throw new Exception("Terrain generation failed to produce a terrain");
            }
            
            // Store terrain
            _generatedTerrain = terrain;
            
            // Log terrain performance
            float terrainTime = _debugger.StopTimer("TerrainGeneration");
            _performanceMetrics["TerrainGeneration"] = terrainTime;
            
            // Fire terrain complete event
            OnTerrainGenerated?.Invoke(terrain);
            
            // Step 3: Generate models (30% of progress)
            _debugger.StartTimer("ModelGeneration");
            
            UpdateProgress(0.6f, "Generating models", onProgress);
            
            List<GameObject> models = null;
            Exception modelError = null;
            
            yield return StartCoroutine(GenerateModelsCoroutine(
                analysisResults,
                terrain,
                result => models = result,
                error => modelError = new Exception(error),
                progress => UpdateProgress(0.6f + progress * 0.3f, "Generating models", onProgress)
            ));
            
            // Check for cancellation
            if (_cancellationTokenSource.IsCancellationRequested) {
                throw new OperationCanceledException("Operation cancelled");
            }
            
            // Check for model error
            if (modelError != null) {
                throw modelError;
            }
            
            // Store models
            if (models != null) {
                _generatedObjects.AddRange(models);
            }
            
            // Log model performance
            float modelTime = _debugger.StopTimer("ModelGeneration");
            _performanceMetrics["ModelGeneration"] = modelTime;
            
            // Fire models complete event
            OnModelsPlaced?.Invoke(models);
            
            // Step 4: Visualize segmentation (10% of progress)
            _debugger.StartTimer("Visualization");
            
            UpdateProgress(0.9f, "Visualizing segmentation", onProgress);
            
            List<GameObject> visualizations = null;
            Exception vizError = null;
            
            yield return StartCoroutine(VisualizeSegmentationCoroutine(
                analysisResults,
                terrain,
                mapTexture,
                result => visualizations = result,
                error => vizError = new Exception(error),
                progress => UpdateProgress(0.9f + progress * 0.1f, "Visualizing segmentation", onProgress)
            ));
            
            // Check for cancellation
            if (_cancellationTokenSource.IsCancellationRequested) {
                throw new OperationCanceledException("Operation cancelled");
            }
            
            // Check for visualization error (non-critical)
            if (vizError != null) {
                _debugger.LogWarning($"Visualization error: {vizError.Message}", LogCategory.Visualization);
            }
            
            // Store visualizations
            if (visualizations != null) {
                _generatedObjects.AddRange(visualizations);
            }
            
            // Log visualization performance
            float vizTime = _debugger.StopTimer("Visualization");
            _performanceMetrics["Visualization"] = vizTime;
            
            // Fire visualization complete event
            OnVisualizationComplete?.Invoke(visualizations);
            
            // Complete processing
            UpdateProgress(1.0f, "Environment generation complete", onProgress);
            
            float totalTime = _debugger.StopTimer("TotalGeneration");
            _performanceMetrics["Total"] = totalTime;
            
            _debugger.Log($"Environment generation completed in {totalTime:F2} seconds", LogCategory.Process);
            
            // Fire completed event
            OnProcessingComplete?.Invoke();
            
            // Call complete callback
            onComplete?.Invoke();
            
            // Reset processing state
            _isProcessing = false;
            _cancellationTokenSource = null;
        }
        
        /// <summary>
        /// Map analysis coroutine.
        /// </summary>
        private IEnumerator AnalyzeMapCoroutine(
            Texture2D mapTexture,
            Action<AnalysisResults> onComplete,
            Action<string> onError,
            Action<float> onProgress
        ) {
            _debugger.Log("Starting map analysis", LogCategory.AI);
            
            try {
                // Check for MapAnalyzer
                if (_mapAnalyzer == null) {
                    throw new Exception("MapAnalyzer component not found");
                }
                
                // Configure MapAnalyzer
                _mapAnalyzer.confidenceThreshold = _systemSettings.detectionThreshold;
                _mapAnalyzer.nmsThreshold = _systemSettings.nmsThreshold;
                _mapAnalyzer.useHighQuality = _performanceSettings.useHighQuality;
                _mapAnalyzer.useGPU = _performanceSettings.useGPU;
                _mapAnalyzer.maxObjectsToProcess = _performanceSettings.maxObjectsToProcess;
                _mapAnalyzer.openAIApiKey = _apiConfig.openAIApiKey;
                
                // Analyze image
                AnalysisResults results = null;
                Exception analysisError = null;
                
                yield return _mapAnalyzer.AnalyzeImage(
                    mapTexture,
                    analysisResults => results = analysisResults,
                    error => analysisError = new Exception(error),
                    (stage, progress) => {
                        OnStageUpdate?.Invoke(stage, progress);
                        onProgress?.Invoke(progress);
                    }
                );
                
                // Check for errors
                if (analysisError != null) {
                    throw analysisError;
                }
                
                // Check for results
                if (results == null) {
                    throw new Exception("MapAnalyzer failed to produce results");
                }
                
                // Return results
                onComplete?.Invoke(results);
            }
            catch (Exception ex) {
                _debugger.LogError($"Map analysis failed: {ex.Message}", LogCategory.AI);
                onError?.Invoke($"Analysis error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Terrain generation coroutine.
        /// </summary>
        private IEnumerator GenerateTerrainCoroutine(
            AnalysisResults analysisResults,
            Action<UnityEngine.Terrain> onComplete,
            Action<string> onError,
            Action<float> onProgress
        ) {
            _debugger.Log("Starting terrain generation", LogCategory.Terrain);
            
            try {
                // Check for TerrainGenerator
                if (_terrainGenerator == null) {
                    throw new Exception("TerrainGenerator component not found");
                }
                
                // Configure TerrainGenerator
                _terrainGenerator.terrainSize = _systemSettings.terrainSize;
                _terrainGenerator.terrainResolution = _systemSettings.terrainResolution;
                _terrainGenerator.heightMapMultiplier = _systemSettings.heightMapMultiplier;
                _terrainGenerator.generateWater = _systemSettings.generateWater;
                _terrainGenerator.waterLevel = _systemSettings.waterLevel;
                _terrainGenerator.applyErosion = _performanceSettings.applyErosion;
                _terrainGenerator.erosionIterations = _performanceSettings.erosionIterations;
                
                // Generate terrain
                UnityEngine.Terrain terrain = null;
                Exception terrainError = null;
                
                yield return _terrainGenerator.GenerateTerrain(
                    analysisResults,
                    _currentMapTexture,
                    generatedTerrain => terrain = generatedTerrain,
                    error => terrainError = new Exception(error),
                    progress => onProgress?.Invoke(progress)
                );
                
                // Check for errors
                if (terrainError != null) {
                    throw terrainError;
                }
                
                // Check for terrain
                if (terrain == null) {
                    throw new Exception("TerrainGenerator failed to produce a terrain");
                }
                
                // Return terrain
                onComplete?.Invoke(terrain);
            }
            catch (Exception ex) {
                _debugger.LogError($"Terrain generation failed: {ex.Message}", LogCategory.Terrain);
                onError?.Invoke($"Terrain error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Model generation and placement coroutine.
        /// </summary>
        private IEnumerator GenerateModelsCoroutine(
            AnalysisResults analysisResults,
            UnityEngine.Terrain terrain,
            Action<List<GameObject>> onComplete,
            Action<string> onError,
            Action<float> onProgress
        ) {
            _debugger.Log("Starting model generation", LogCategory.Models);
            
            try {
                // Check for ModelGenerator
                if (_modelGenerator == null) {
                    throw new Exception("ModelGenerator component not found");
                }
                
                // Configure ModelGenerator
                _modelGenerator.groupSimilarObjects = _systemSettings.groupSimilarObjects;
                _modelGenerator.instancingSimilarity = _systemSettings.instancingSimilarity;
                _modelGenerator.openAIApiKey = _apiConfig.openAIApiKey;
                _modelGenerator.tripo3DApiKey = _apiConfig.tripo3DApiKey;
                _modelGenerator.generationTimeout = _performanceSettings.modelGenerationTimeout;
                _modelGenerator.maxConcurrentRequests = _performanceSettings.maxConcurrentAPIRequests;
                
                // Generate models
                List<GameObject> models = null;
                Exception modelError = null;
                
                yield return _modelGenerator.GenerateAndPlaceModels(
                    analysisResults,
                    terrain,
                    generatedModels => models = generatedModels,
                    error => modelError = new Exception(error),
                    (current, total) => onProgress?.Invoke(current / (float)total)
                );
                
                // Check for errors
                if (modelError != null) {
                    throw modelError;
                }
                
                // Return models
                onComplete?.Invoke(models ?? new List<GameObject>());
            }
            catch (Exception ex) {
                _debugger.LogError($"Model generation failed: {ex.Message}", LogCategory.Models);
                onError?.Invoke($"Model error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Segmentation visualization coroutine.
        /// </summary>
        private IEnumerator VisualizeSegmentationCoroutine(
            AnalysisResults analysisResults,
            UnityEngine.Terrain terrain,
            Texture2D mapTexture,
            Action<List<GameObject>> onComplete,
            Action<string> onError,
            Action<float> onProgress
        ) {
            _debugger.Log("Starting segmentation visualization", LogCategory.Visualization);
            
            try {
                // Check for SegmentationVisualizer
                if (_segmentationVisualizer == null) {
                    throw new Exception("SegmentationVisualizer component not found");
                }
                
                // Configure SegmentationVisualizer
                _segmentationVisualizer.enableDebugVisualization = _visualizationSettings.enableDebugVisualization;
                
                // Visualize segmentation
                List<GameObject> visualizations = null;
                Exception vizError = null;
                
                yield return _segmentationVisualizer.VisualizeSegments(
                    analysisResults,
                    terrain,
                    mapTexture,
                    vizObjects => visualizations = vizObjects,
                    error => vizError = new Exception(error),
                    progress => onProgress?.Invoke(progress)
                );
                
                // Check for errors
                if (vizError != null) {
                    throw vizError;
                }
                
                // Return visualizations
                onComplete?.Invoke(visualizations ?? new List<GameObject>());
            }
            catch (Exception ex) {
                _debugger.LogError($"Segmentation visualization failed: {ex.Message}", LogCategory.Visualization);
                onError?.Invoke($"Visualization error: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Update progress and fire progress events.
        /// </summary>
        private void UpdateProgress(float progress, string stage, Action<float> progressCallback) {
            // Clamp progress
            progress = Mathf.Clamp01(progress);
            
            // Fire events
            OnProgressUpdate?.Invoke(progress);
            OnStageUpdate?.Invoke(stage, progress);
            
            // Call callback
            progressCallback?.Invoke(progress);
            
            // Log
            _debugger.Log($"Progress: {progress:P0} - {stage}", LogCategory.Process);
        }
        
        /// <summary>
        /// Clean up generated objects.
        /// </summary>
        private void CleanupGeneratedObjects() {
            foreach (var obj in _generatedObjects) {
                if (obj != null) {
                    Destroy(obj);
                }
            }
            
            _generatedObjects.Clear();
            
            if (_generatedTerrain != null) {
                Destroy(_generatedTerrain.gameObject);
                _generatedTerrain = null;
            }
        }
        
        /// <summary>
        /// Save metadata about the current environment.
        /// </summary>
        private void SaveMetadata(string path) {
            try {
                // Create metadata object
                var metadata = new EnvironmentMetadata {
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    generatedBy = "dkaplan73",
                    sourceMapName = _currentMapTexture != null ? _currentMapTexture.name : "Unknown",
                    sourceMapResolution = _currentMapTexture != null ? $"{_currentMapTexture.width}x{_currentMapTexture.height}" : "Unknown",
                    terrainSize = _systemSettings.terrainSize,
                    terrainResolution = _systemSettings.terrainResolution,
                    terrainFeatureCount = _currentAnalysisResults?.terrainFeatures.Count ?? 0,
                    objectCount = _currentAnalysisResults?.mapObjects.Count ?? 0,
                    performanceMetrics = _performanceMetrics
                };
                
                // Save as JSON
                string metadataPath = Path.Combine(path, "metadata.json");
                File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, true));
                
                _debugger.Log($"Metadata saved to {metadataPath}", LogCategory.IO);
            }
            catch (Exception ex) {
                _debugger.LogError($"Failed to save metadata: {ex.Message}", LogCategory.IO);
            }
        }
        
        #endregion
        
        #region Settings Classes
        
        /// <summary>
        /// API configuration settings.
        /// </summary>
        [Serializable]
        public class APIConfiguration {
            /// <summary>
            /// OpenAI API key for text completion and image generation.
            /// </summary>
            [Tooltip("OpenAI API key for text completion and image generation")]
            public string openAIApiKey;
            
            /// <summary>
            /// Tripo3D API key for 3D model generation.
            /// </summary>
            [Tooltip("Tripo3D API key for 3D model generation")]
            public string tripo3DApiKey;
        }
        
        /// <summary>
        /// System settings for Traversify.
        /// </summary>
        [Serializable]
        public class SystemSettings {
            /// <summary>
            /// Size of the generated terrain in world units.
            /// </summary>
            [Tooltip("Size of the generated terrain in world units")]
            public Vector3 terrainSize = new Vector3(500, 100, 500);
            
            /// <summary>
            /// Resolution of the terrain heightmap.
            /// </summary>
            [Tooltip("Resolution of the terrain heightmap")]
            public int terrainResolution = 513;
            
            /// <summary>
            /// Multiplier for the heightmap values.
            /// </summary>
            [Tooltip("Multiplier for the heightmap values")]
            public float heightMapMultiplier = 30f;
            
            /// <summary>
            /// Confidence threshold for object detection (0-1).
            /// </summary>
            [Tooltip("Confidence threshold for object detection (0-1)")]
            [Range(0f, 1f)]
            public float detectionThreshold = 0.5f;
            
            /// <summary>
            /// Non-maximum suppression threshold for object detection (0-1).
            /// </summary>
            [Tooltip("Non-maximum suppression threshold for object detection (0-1)")]
            [Range(0f, 1f)]
            public float nmsThreshold = 0.45f;
            
            /// <summary>
            /// Whether to generate water features.
            /// </summary>
            [Tooltip("Whether to generate water features")]
            public bool generateWater = true;
            
            /// <summary>
            /// Water level as a fraction of terrain height (0-1).
            /// </summary>
            [Tooltip("Water level as a fraction of terrain height (0-1)")]
            [Range(0f, 1f)]
            public float waterLevel = 0.25f;
            
            /// <summary>
            /// Whether to group similar objects for instancing.
            /// </summary>
            [Tooltip("Whether to group similar objects for instancing")]
            public bool groupSimilarObjects = true;
            
            /// <summary>
            /// Similarity threshold for object instancing (0-1).
            /// </summary>
            [Tooltip("Similarity threshold for object instancing (0-1)")]
            [Range(0f, 1f)]
            public float instancingSimilarity = 0.8f;
        }
        
        /// <summary>
        /// Performance settings for Traversify.
        /// </summary>
        [Serializable]
        public class PerformanceSettings {
            /// <summary>
            /// Whether to use high quality analysis.
            /// </summary>
            [Tooltip("Whether to use high quality analysis")]
            public bool useHighQuality = true;
            
            /// <summary>
            /// Whether to use GPU acceleration.
            /// </summary>
            [Tooltip("Whether to use GPU acceleration")]
            public bool useGPU = true;
            
            /// <summary>
            /// Maximum number of objects to process.
            /// </summary>
            [Tooltip("Maximum number of objects to process")]
            public int maxObjectsToProcess = 100;
            
            /// <summary>
            /// Whether to apply erosion to the terrain.
            /// </summary>
            [Tooltip("Whether to apply erosion to the terrain")]
            public bool applyErosion = true;
            
            /// <summary>
            /// Number of erosion iterations.
            /// </summary>
            [Tooltip("Number of erosion iterations")]
            public int erosionIterations = 1000;
            
            /// <summary>
            /// Timeout for model generation in seconds.
            /// </summary>
            [Tooltip("Timeout for model generation in seconds")]
            public float modelGenerationTimeout = 20f;
            
            /// <summary>
            /// Maximum number of concurrent API requests.
            /// </summary>
            [Tooltip("Maximum number of concurrent API requests")]
            public int maxConcurrentAPIRequests = 4;
            
            /// <summary>
            /// Delay between API requests in seconds.
            /// </summary>
            [Tooltip("Delay between API requests in seconds")]
            public float apiRateLimitDelay = 0.5f;
        }
        
        /// <summary>
        /// Visualization settings for Traversify.
        /// </summary>
        [Serializable]
        public class VisualizationSettings {
            /// <summary>
            /// Whether to enable debug visualization.
            /// </summary>
            [Tooltip("Whether to enable debug visualization")]
            public bool enableDebugVisualization = false;
            
            /// <summary>
            /// Whether to show terrain feature outlines.
            /// </summary>
            [Tooltip("Whether to show terrain feature outlines")]
            public bool showTerrainFeatureOutlines = true;
            
            /// <summary>
            /// Whether to show object labels.
            /// </summary>
            [Tooltip("Whether to show object labels")]
            public bool showObjectLabels = true;
            
            /// <summary>
            /// Whether to show segmentation masks.
            /// </summary>
            [Tooltip("Whether to show segmentation masks")]
            public bool showSegmentationMasks = true;
            
            /// <summary>
            /// Whether to show heightmap visualization.
            /// </summary>
            [Tooltip("Whether to show heightmap visualization")]
            public bool showHeightmapVisualization = false;
        }
        
        /// <summary>
        /// Metadata for a saved environment.
        /// </summary>
        [Serializable]
        public class EnvironmentMetadata {
            /// <summary>
            /// Timestamp when the environment was generated.
            /// </summary>
            public string timestamp;
            
            /// <summary>
            /// User who generated the environment.
            /// </summary>
            public string generatedBy;
            
            /// <summary>
            /// Name of the source map.
            /// </summary>
            public string sourceMapName;
            
            /// <summary>
            /// Resolution of the source map.
            /// </summary>
            public string sourceMapResolution;
            
            /// <summary>
            /// Size of the terrain in world units.
            /// </summary>
            public Vector3 terrainSize;
            
            /// <summary>
            /// Resolution of the terrain heightmap.
            /// </summary>
            public int terrainResolution;
            
            /// <summary>
            /// Number of terrain features.
            /// </summary>
            public int terrainFeatureCount;
            
            /// <summary>
            /// Number of objects.
            /// </summary>
            public int objectCount;
            
            /// <summary>
            /// Performance metrics.
            /// </summary>
            public Dictionary<string, float> performanceMetrics;
        }
        
        #endregion
    }
}
