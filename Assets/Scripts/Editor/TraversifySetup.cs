/*************************************************************************
 *  Traversify – TraversifyAutoSetup.cs                                 *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 23:11:40 UTC                                     *
 *  Desc   : Comprehensive automatic setup and integration script for    *
 *           configuring all Traversify components, UI elements, and     *
 *           scene hierarchy with proper inspector parameters.           *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.AI;
using UnityEngine.Rendering;
using TMPro;
using Traversify;
using Traversify.AI;
using Traversify.Core;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace Traversify.Setup {
    /// <summary>
    /// Automatic setup and configuration tool for Traversify framework.
    /// Creates and configures all necessary GameObjects, components, and UI elements.
    /// </summary>
    public class TraversifyAutoSetup : MonoBehaviour {
        #region Constants
        
        // Scene hierarchy paths
        private const string ROOT_NAME = "Traversify System";
        private const string CORE_NAME = "Core Systems";
        private const string AI_NAME = "AI Processing";
        private const string TERRAIN_NAME = "Terrain Generation";
        private const string UI_NAME = "UI Canvas";
        private const string ENVIRONMENT_NAME = "Environment";
        private const string GENERATED_NAME = "Generated Objects";
        
        // Default paths
        private const string MODELS_PATH = "Assets/StreamingAssets/Traversify/Models";
        private const string RESOURCES_PATH = "Assets/Resources/Traversify";
        private const string MATERIALS_PATH = "Assets/Materials/Traversify";
        private const string PREFABS_PATH = "Assets/Prefabs/Traversify";
        private const string GENERATED_PATH = "Assets/Generated/Traversify";
        
        // UI Layout constants
        private const float UI_PANEL_WIDTH = 400f;
        private const float UI_HEADER_HEIGHT = 60f;
        private const float UI_BUTTON_HEIGHT = 40f;
        private const float UI_PADDING = 10f;
        
        #endregion
        
        #region Setup Entry Points
        
        #if UNITY_EDITOR
        [MenuItem("Traversify/Setup/Complete Setup", false, 0)]
        public static void SetupCompleteSystem() {
            Debug.Log("Starting Traversify complete system setup...");
            
            // Create setup instance
            GameObject setupObj = new GameObject("_TraversifySetup");
            TraversifyAutoSetup setup = setupObj.AddComponent<TraversifyAutoSetup>();
            
            // Run setup
            setup.PerformCompleteSetup();
            
            // Clean up
            DestroyImmediate(setupObj);
        }
        
        [MenuItem("Traversify/Setup/Validate Setup", false, 20)]
        public static void ValidateSetup() {
            GameObject setupObj = new GameObject("_TraversifyValidator");
            TraversifyAutoSetup setup = setupObj.AddComponent<TraversifyAutoSetup>();
            
            if (setup.ValidateSystemSetup()) {
                EditorUtility.DisplayDialog("Traversify Setup", 
                    "System setup is valid and complete!", "OK");
            } else {
                if (EditorUtility.DisplayDialog("Traversify Setup", 
                    "System setup is incomplete or invalid. Would you like to run auto-setup?", 
                    "Yes", "No")) {
                    setup.PerformCompleteSetup();
                }
            }
            
            DestroyImmediate(setupObj);
        }
        
        [MenuItem("Traversify/Setup/Reset to Defaults", false, 40)]
        public static void ResetToDefaults() {
            if (EditorUtility.DisplayDialog("Reset Traversify", 
                "This will remove all Traversify objects and recreate the system. Continue?", 
                "Yes", "Cancel")) {
                CleanupExistingSystem();
                SetupCompleteSystem();
            }
        }
        #endif
        
        /// <summary>
        /// Performs complete system setup.
        /// </summary>
        public void PerformCompleteSetup() {
            try {
                // Phase 1: Prepare environment
                Debug.Log("Phase 1: Preparing environment...");
                PrepareDirectoryStructure();
                CleanupExistingSystem();
                
                // Phase 2: Create hierarchy
                Debug.Log("Phase 2: Creating scene hierarchy...");
                GameObject rootObject = CreateSceneHierarchy();
                
                // Phase 3: Setup core systems
                Debug.Log("Phase 3: Setting up core systems...");
                SetupCoreComponents(rootObject);
                
                // Phase 4: Setup AI components
                Debug.Log("Phase 4: Setting up AI components...");
                SetupAIComponents(rootObject);
                
                // Phase 5: Setup terrain systems
                Debug.Log("Phase 5: Setting up terrain systems...");
                SetupTerrainComponents(rootObject);
                
                // Phase 6: Setup UI
                Debug.Log("Phase 6: Creating UI...");
                SetupUISystem(rootObject);
                
                // Phase 7: Configure scene settings
                Debug.Log("Phase 7: Configuring scene settings...");
                ConfigureSceneSettings();
                
                // Phase 8: Link components
                Debug.Log("Phase 8: Linking components...");
                LinkComponents(rootObject);
                
                // Phase 9: Apply default settings
                Debug.Log("Phase 9: Applying default settings...");
                ApplyDefaultSettings(rootObject);
                
                // Phase 10: Validate setup
                Debug.Log("Phase 10: Validating setup...");
                if (ValidateSystemSetup()) {
                    Debug.Log("Traversify setup completed successfully!");
                    #if UNITY_EDITOR
                    EditorUtility.DisplayDialog("Setup Complete", 
                        "Traversify has been successfully set up!\n\n" +
                        "Next steps:\n" +
                        "1. Configure your OpenAI API key in the Manager\n" +
                        "2. Ensure model files are in StreamingAssets/Traversify/Models\n" +
                        "3. Test with a sample map image", "OK");
                    
                    // Save scene
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                    #endif
                } else {
                    Debug.LogError("Setup validation failed!");
                }
            }
            catch (Exception ex) {
                Debug.LogError($"Setup failed: {ex.Message}\n{ex.StackTrace}");
                #if UNITY_EDITOR
                EditorUtility.DisplayDialog("Setup Error", 
                    $"An error occurred during setup:\n{ex.Message}", "OK");
                #endif
            }
        }
        
        #endregion
        
        #region Directory Structure
        
        private void PrepareDirectoryStructure() {
            #if UNITY_EDITOR
            // Create necessary directories
            string[] directories = {
                MODELS_PATH,
                RESOURCES_PATH,
                MATERIALS_PATH,
                PREFABS_PATH,
                GENERATED_PATH,
                Path.Combine(RESOURCES_PATH, "Shaders"),
                Path.Combine(RESOURCES_PATH, "Materials"),
                Path.Combine(RESOURCES_PATH, "Textures"),
                Path.Combine(RESOURCES_PATH, "UI"),
                Path.Combine(PREFABS_PATH, "Objects"),
                Path.Combine(PREFABS_PATH, "UI"),
                Path.Combine(GENERATED_PATH, "Terrains"),
                Path.Combine(GENERATED_PATH, "Models"),
                Path.Combine(GENERATED_PATH, "Textures")
            };
            
            foreach (string dir in directories) {
                if (!Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                    Debug.Log($"Created directory: {dir}");
                }
            }
            
            AssetDatabase.Refresh();
            #endif
        }
        
        #endregion
        
        #region Scene Hierarchy Creation
        
        private static void CleanupExistingSystem() {
            // Remove existing Traversify objects
            GameObject existingRoot = GameObject.Find(ROOT_NAME);
            if (existingRoot != null) {
                DestroyImmediate(existingRoot);
            }
            
            // Remove any orphaned Traversify components
            TraversifyComponent[] orphanedComponents = FindObjectsOfType<TraversifyComponent>();
            foreach (var comp in orphanedComponents) {
                DestroyImmediate(comp.gameObject);
            }
        }
        
        private GameObject CreateSceneHierarchy() {
            // Create root object
            GameObject root = new GameObject(ROOT_NAME);
            root.transform.position = Vector3.zero;
            
            // Create main sections
            GameObject core = new GameObject(CORE_NAME);
            core.transform.SetParent(root.transform);
            
            GameObject ai = new GameObject(AI_NAME);
            ai.transform.SetParent(root.transform);
            
            GameObject terrain = new GameObject(TERRAIN_NAME);
            terrain.transform.SetParent(root.transform);
            
            GameObject ui = new GameObject(UI_NAME);
            ui.transform.SetParent(root.transform);
            
            GameObject environment = new GameObject(ENVIRONMENT_NAME);
            environment.transform.SetParent(root.transform);
            
            GameObject generated = new GameObject(GENERATED_NAME);
            generated.transform.SetParent(environment.transform);
            
            return root;
        }
        
        #endregion
        
        #region Core Components Setup
        
        private void SetupCoreComponents(GameObject root) {
            GameObject coreParent = root.transform.Find(CORE_NAME).gameObject;
            
            // 1. Traversify Main Component
            GameObject traversifyObj = new GameObject("Traversify");
            traversifyObj.transform.SetParent(coreParent.transform);
            
            Traversify mainComponent = traversifyObj.AddComponent<Traversify>();
            TraversifyDebugger debugger = traversifyObj.AddComponent<TraversifyDebugger>();
            
            // Configure debugger
            debugger.enableLogging = true;
            debugger.logToFile = true;
            debugger.maxLogEntries = 1000;
            debugger.showTimestamps = true;
            debugger.showCategory = true;
            
            // 2. Traversify Manager
            GameObject managerObj = new GameObject("TraversifyManager");
            managerObj.transform.SetParent(coreParent.transform);
            
            TraversifyManager manager = managerObj.AddComponent<TraversifyManager>();
            managerObj.AddComponent<TraversifyDebugger>();
            
            // Configure manager defaults
            ConfigureManagerDefaults(manager);
            
            // 3. Asset Cache
            GameObject cacheObj = new GameObject("AssetCache");
            cacheObj.transform.SetParent(coreParent.transform);
            
            AssetCache cache = cacheObj.AddComponent<AssetCache>();
            cacheObj.AddComponent<TraversifyDebugger>();
            
            // 4. Environment Manager
            GameObject envManagerObj = new GameObject("EnvironmentManager");
            envManagerObj.transform.SetParent(coreParent.transform);
            
            // Add EnvironmentManager component if it exists
            // Note: Component definition not provided in the files
        }
        
        private void ConfigureManagerDefaults(TraversifyManager manager) {
            // Terrain settings
            manager.terrainSize = new Vector3(1000, 100, 1000);
            manager.terrainResolution = 513;
            manager.generateWater = true;
            manager.waterHeight = 20f;
            
            // AI settings
            manager.useHighQualityAnalysis = true;
            manager.detectionThreshold = 0.5f;
            manager.useFasterRCNN = true;
            manager.useSAM = true;
            manager.maxObjectsToProcess = 100;
            manager.groupSimilarObjects = true;
            manager.instancingSimilarity = 0.85f;
            
            // Performance settings
            manager.maxConcurrentAPIRequests = 3;
            manager.processingBatchSize = 10;
            manager.processingTimeout = 300f;
            manager.apiRateLimitDelay = 0.5f;
            manager.useGPUAcceleration = true;
            
            // Advanced settings
            manager.enableDebugVisualization = true;
            manager.saveGeneratedAssets = true;
            manager.generateMetadata = true;
            manager.assetSavePath = GENERATED_PATH;
        }
        
        #endregion
        
        #region AI Components Setup
        
        private void SetupAIComponents(GameObject root) {
            GameObject aiParent = root.transform.Find(AI_NAME).gameObject;
            
            // 1. Map Analyzer
            GameObject analyzerObj = new GameObject("MapAnalyzer");
            analyzerObj.transform.SetParent(aiParent.transform);
            
            MapAnalyzer analyzer = analyzerObj.AddComponent<MapAnalyzer>();
            analyzerObj.AddComponent<TraversifyDebugger>();
            
            // Configure analyzer
            ConfigureMapAnalyzer(analyzer);
            
            // 2. Model Generator
            GameObject modelGenObj = new GameObject("ModelGenerator");
            modelGenObj.transform.SetParent(aiParent.transform);
            
            ModelGenerator modelGen = modelGenObj.AddComponent<ModelGenerator>();
            modelGenObj.AddComponent<TraversifyDebugger>();
            
            // Configure model generator
            ConfigureModelGenerator(modelGen);
            
            // 3. OpenAI Response Handler
            GameObject openAIObj = new GameObject("OpenAIHandler");
            openAIObj.transform.SetParent(aiParent.transform);
            
            // Add OpenAIResponse component if it exists
            // Note: Component definition not provided in the files
        }
        
        private void ConfigureMapAnalyzer(MapAnalyzer analyzer) {
            // Set model paths
            analyzer.yoloModelPath = Path.Combine(MODELS_PATH, "yolov12_map_objects.onnx");
            analyzer.sam2ModelPath = Path.Combine(MODELS_PATH, "sam2_segmentation.onnx");
            analyzer.fasterRCNNModelPath = Path.Combine(MODELS_PATH, "faster_rcnn_features.onnx");
            
            // Detection settings
            analyzer.detectionConfidenceThreshold = 0.5f;
            analyzer.nmsThreshold = 0.45f;
            analyzer.maxDetections = 100;
            
            // Segmentation settings
            analyzer.segmentationQuality = 0.9f;
            analyzer.minSegmentArea = 0.001f;
            
            // Processing settings
            analyzer.useGPUProcessing = true;
            analyzer.batchSize = 8;
            analyzer.enableDetailedAnalysis = true;
            
            // Height estimation
            analyzer.heightEstimationEnabled = true;
            analyzer.baseHeightScale = 1.0f;
            analyzer.heightSmoothingFactor = 0.3f;
        }
        
        private void ConfigureModelGenerator(ModelGenerator modelGen) {
            // API configuration
            modelGen.tripoApiUrl = "https://api.tripo3d.ai/v1/generate";
            modelGen.apiRequestTimeout = 60f;
            modelGen.maxRetries = 3;
            modelGen.retryDelay = 2f;
            
            // Generation settings
            modelGen.modelQuality = ModelGenerator.ModelQuality.High;
            modelGen.generateLODs = true;
            modelGen.maxLODLevels = 3;
            modelGen.usePBRMaterials = true;
            
            // Optimization settings
            modelGen.optimizeMeshes = true;
            modelGen.combineStaticMeshes = true;
            modelGen.maxVerticesPerMesh = 65000;
            
            // Placement settings
            modelGen.adaptToTerrain = true;
            modelGen.avoidCollisions = true;
            modelGen.collisionCheckRadius = 2f;
            modelGen.maxPlacementAttempts = 10;
        }
        
        #endregion
        
        #region Terrain Components Setup
        
        private void SetupTerrainComponents(GameObject root) {
            GameObject terrainParent = root.transform.Find(TERRAIN_NAME).gameObject;
            
            // 1. Create Unity Terrain
            GameObject terrainObj = CreateUnityTerrain(terrainParent);
            
            // 2. Terrain Generator
            TerrainGenerator terrainGen = terrainObj.AddComponent<TerrainGenerator>();
            terrainObj.AddComponent<TraversifyDebugger>();
            
            // Configure terrain generator
            ConfigureTerrainGenerator(terrainGen);
            
            // 3. Segmentation Visualizer
            GameObject segVisObj = new GameObject("SegmentationVisualizer");
            segVisObj.transform.SetParent(terrainParent.transform);
            
            SegmentationVisualizer segVis = segVisObj.AddComponent<SegmentationVisualizer>();
            segVisObj.AddComponent<TraversifyDebugger>();
            
            // Configure segmentation visualizer
            ConfigureSegmentationVisualizer(segVis);
            
            // 4. Terrain Analyzer
            GameObject terrainAnalyzerObj = new GameObject("TerrainAnalyzer");
            terrainAnalyzerObj.transform.SetParent(terrainParent.transform);
            
            TerrainAnalyzer terrainAnalyzer = terrainAnalyzerObj.AddComponent<TerrainAnalyzer>();
            terrainAnalyzerObj.AddComponent<TraversifyDebugger>();
        }
        
        private GameObject CreateUnityTerrain(GameObject parent) {
            // Create terrain data
            TerrainData terrainData = new TerrainData();
            terrainData.heightmapResolution = 513;
            terrainData.size = new Vector3(1000, 100, 1000);
            terrainData.name = "TraversifyTerrainData";
            
            #if UNITY_EDITOR
            // Save terrain data asset
            string terrainDataPath = Path.Combine(GENERATED_PATH, "Terrains", "DefaultTerrainData.asset");
            AssetDatabase.CreateAsset(terrainData, terrainDataPath);
            #endif
            
            // Create terrain GameObject
            GameObject terrainObj = Terrain.CreateTerrainGameObject(terrainData);
            terrainObj.name = "Generated Terrain";
            terrainObj.transform.SetParent(parent.transform);
            terrainObj.transform.position = Vector3.zero;
            
            // Configure terrain settings
            Terrain terrain = terrainObj.GetComponent<Terrain>();
            terrain.materialTemplate = CreateDefaultTerrainMaterial();
            terrain.detailObjectDistance = 200f;
            terrain.detailObjectDensity = 1f;
            terrain.treeDistance = 2000f;
            terrain.treeBillboardDistance = 200f;
            terrain.treeCrossFadeLength = 20f;
            terrain.treeMaximumFullLODCount = 100;
            
            return terrainObj;
        }
        
        private Material CreateDefaultTerrainMaterial() {
            // Try to find built-in terrain material
            Material terrainMat = Resources.Load<Material>("TerrainMaterial");
            
            if (terrainMat == null) {
                // Create basic terrain material
                Shader terrainShader = Shader.Find("Nature/Terrain/Standard");
                if (terrainShader != null) {
                    terrainMat = new Material(terrainShader);
                    terrainMat.name = "TraversifyTerrainMaterial";
                    
                    #if UNITY_EDITOR
                    string matPath = Path.Combine(MATERIALS_PATH, "TraversifyTerrainMaterial.mat");
                    AssetDatabase.CreateAsset(terrainMat, matPath);
                    #endif
                }
            }
            
            return terrainMat;
        }
        
        private void ConfigureTerrainGenerator(TerrainGenerator generator) {
            // Generation mode
            generator.generationMode = TerrainGenerator.TerrainGenerationMode.Hybrid;
            
            // Noise settings
            generator.noiseType = TerrainGenerator.NoiseType.Simplex;
            generator.baseFrequency = 0.002f;
            generator.amplitude = 30f;
            generator.octaves = 6;
            generator.persistence = 0.5f;
            generator.lacunarity = 2f;
            generator.seed = 12345;
            
            // Height settings
            generator.minHeight = 0f;
            generator.maxHeight = 100f;
            generator.heightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            
            // Erosion settings
            generator.enableErosion = true;
            generator.erosionIterations = 5;
            generator.erosionStrength = 0.3f;
            
            // Water settings
            generator.generateWater = true;
            generator.waterLevel = 20f;
            generator.waterColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            
            // Performance settings
            generator.useGPU = true;
            generator.useMultithreading = true;
            generator.workerThreads = SystemInfo.processorCount - 1;
        }
        
        private void ConfigureSegmentationVisualizer(SegmentationVisualizer visualizer) {
            // Visualization settings
            visualizer.visualizationMode = SegmentationVisualizer.VisualizationMode.Overlay;
            visualizer.animationStyle = SegmentationVisualizer.AnimationStyle.Pulse;
            visualizer.infoDisplayMode = SegmentationVisualizer.InfoDisplayMode.Detailed;
            
            // Visual properties
            visualizer.overlayOpacity = 0.3f;
            visualizer.outlineWidth = 2f;
            visualizer.labelScale = 1f;
            visualizer.enableShadows = true;
            
            // Animation settings
            visualizer.animationSpeed = 1f;
            visualizer.pulseFrequency = 0.5f;
            visualizer.waveSpeed = 2f;
            
            // Interaction settings
            visualizer.enableInteraction = true;
            visualizer.highlightOnHover = true;
            visualizer.showTooltips = true;
            
            // Performance settings
            visualizer.useLOD = true;
            visualizer.maxVisibleSegments = 100;
            visualizer.cullingDistance = 500f;
        }
        
        #endregion
        
        #region UI System Setup
        
        private void SetupUISystem(GameObject root) {
            GameObject uiParent = root.transform.Find(UI_NAME).gameObject;
            
            // Create Canvas
            GameObject canvasObj = CreateMainCanvas(uiParent);
            Canvas canvas = canvasObj.GetComponent<Canvas>();
            
            // Create main UI panels
            CreateControlPanel(canvas);
            CreateProgressPanel(canvas);
            CreateInfoPanel(canvas);
            CreateDebugPanel(canvas);
            
            // Create event system if needed
            if (FindObjectOfType<EventSystem>() == null) {
                GameObject eventSystemObj = new GameObject("EventSystem");
                eventSystemObj.transform.SetParent(uiParent.transform);
                eventSystemObj.AddComponent<EventSystem>();
                eventSystemObj.AddComponent<StandaloneInputModule>();
            }
        }
        
        private GameObject CreateMainCanvas(GameObject parent) {
            GameObject canvasObj = new GameObject("Main Canvas");
            canvasObj.transform.SetParent(parent.transform);
            
            // Canvas component
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            
            // Canvas Scaler
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            
            // Graphic Raycaster
            canvasObj.AddComponent<GraphicRaycaster>();
            
            return canvasObj;
        }
        
        private void CreateControlPanel(Canvas canvas) {
            // Main control panel
            GameObject panelObj = CreatePanel("Control Panel", canvas.transform);
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            
            // Position on left side
            panelRect.anchorMin = new Vector2(0, 0.5f);
            panelRect.anchorMax = new Vector2(0, 0.5f);
            panelRect.anchoredPosition = new Vector2(UI_PANEL_WIDTH / 2 + UI_PADDING, 0);
            panelRect.sizeDelta = new Vector2(UI_PANEL_WIDTH, 600);
            
            // Add vertical layout
            VerticalLayoutGroup layout = panelObj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            
            // Header
            CreateHeader("Traversify Controls", panelObj.transform);
            
            // File selection section
            CreateSectionHeader("Map Image", panelObj.transform);
            CreateFileSelectionUI(panelObj.transform);
            
            // Settings section
            CreateSectionHeader("Generation Settings", panelObj.transform);
            CreateSettingsUI(panelObj.transform);
            
            // Action buttons
            CreateSectionHeader("Actions", panelObj.transform);
            CreateActionButtons(panelObj.transform);
        }
        
        private void CreateProgressPanel(Canvas canvas) {
            GameObject panelObj = CreatePanel("Progress Panel", canvas.transform);
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            
            // Position at bottom
            panelRect.anchorMin = new Vector2(0.5f, 0);
            panelRect.anchorMax = new Vector2(0.5f, 0);
            panelRect.anchoredPosition = new Vector2(0, 100);
            panelRect.sizeDelta = new Vector2(800, 180);
            
            // Initially hidden
            panelObj.SetActive(false);
            
            // Add components for progress display
            VerticalLayoutGroup layout = panelObj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.spacing = 10;
            
            // Stage text
            GameObject stageTextObj = CreateText("Stage: Initializing", panelObj.transform);
            stageTextObj.name = "StageText";
            
            // Progress bar
            GameObject progressBarObj = CreateProgressBar(panelObj.transform);
            progressBarObj.name = "ProgressBar";
            
            // Details text
            GameObject detailsTextObj = CreateText("", panelObj.transform);
            detailsTextObj.name = "DetailsText";
            TextMeshProUGUI detailsText = detailsTextObj.GetComponent<TextMeshProUGUI>();
            detailsText.fontSize = 14;
            detailsText.color = new Color(0.8f, 0.8f, 0.8f);
            
            // Cancel button
            GameObject cancelBtn = CreateButton("Cancel", panelObj.transform, () => {
                TraversifyManager.Instance?.CancelProcessing();
            });
            cancelBtn.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 40);
        }
        
        private void CreateInfoPanel(Canvas canvas) {
            GameObject panelObj = CreatePanel("Info Panel", canvas.transform);
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            
            // Position on right side
            panelRect.anchorMin = new Vector2(1, 0.5f);
            panelRect.anchorMax = new Vector2(1, 0.5f);
            panelRect.anchoredPosition = new Vector2(-UI_PANEL_WIDTH / 2 - UI_PADDING, 0);
            panelRect.sizeDelta = new Vector2(UI_PANEL_WIDTH, 400);
            
            // Initially hidden
            panelObj.SetActive(false);
            
            // Add scroll view for info display
            GameObject scrollViewObj = CreateScrollView(panelObj.transform);
            GameObject contentObj = scrollViewObj.transform.Find("Viewport/Content").gameObject;
            
            // Info text
            GameObject infoTextObj = CreateText("", contentObj.transform);
            infoTextObj.name = "InfoText";
            TextMeshProUGUI infoText = infoTextObj.GetComponent<TextMeshProUGUI>();
            infoText.fontSize = 14;
        }
        
        private void CreateDebugPanel(Canvas canvas) {
            GameObject panelObj = CreatePanel("Debug Panel", canvas.transform);
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            
            // Position at top-right corner
            panelRect.anchorMin = new Vector2(1, 1);
            panelRect.anchorMax = new Vector2(1, 1);
            panelRect.anchoredPosition = new Vector2(-200, -100);
            panelRect.sizeDelta = new Vector2(380, 180);
            
            // Initially hidden
            panelObj.SetActive(false);
            
            // Debug info display
            VerticalLayoutGroup layout = panelObj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 5;
            
            // FPS counter
            GameObject fpsTextObj = CreateText("FPS: 0", panelObj.transform);
            fpsTextObj.name = "FPSText";
            
            // Memory usage
            GameObject memoryTextObj = CreateText("Memory: 0 MB", panelObj.transform);
            memoryTextObj.name = "MemoryText";
            
            // Processing stats
            GameObject statsTextObj = CreateText("", panelObj.transform);
            statsTextObj.name = "StatsText";
            TextMeshProUGUI statsText = statsTextObj.GetComponent<TextMeshProUGUI>();
            statsText.fontSize = 12;
        }
        
        #endregion
        
        #region UI Helper Methods
        
        private GameObject CreatePanel(string name, Transform parent) {
            GameObject panelObj = new GameObject(name);
            panelObj.transform.SetParent(parent);
            
            RectTransform rect = panelObj.AddComponent<RectTransform>();
            rect.localScale = Vector3.one;
            
            Image image = panelObj.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            
            return panelObj;
        }
        
        private GameObject CreateHeader(string text, Transform parent) {
            GameObject headerObj = new GameObject("Header");
            headerObj.transform.SetParent(parent);
            
            RectTransform rect = headerObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, UI_HEADER_HEIGHT);
            
            TextMeshProUGUI tmp = headerObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 24;
            tmp.fontWeight = FontWeight.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            
            return headerObj;
        }
        
        private GameObject CreateSectionHeader(string text, Transform parent) {
            GameObject headerObj = CreateText(text, parent);
            TextMeshProUGUI tmp = headerObj.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 18;
            tmp.fontWeight = FontWeight.Bold;
            tmp.color = new Color(0.8f, 0.8f, 0.8f);
            
            RectTransform rect = headerObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 30);
            
            return headerObj;
        }
        
        private GameObject CreateText(string text, Transform parent) {
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(parent);
            
            RectTransform rect = textObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 30);
            
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 16;
            tmp.alignment = TextAlignmentOptions.Left;
            
            return textObj;
        }
        
        private GameObject CreateButton(string text, Transform parent, UnityEngine.Events.UnityAction onClick) {
            GameObject buttonObj = new GameObject("Button");
            buttonObj.transform.SetParent(parent);
            
            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, UI_BUTTON_HEIGHT);
            
            Image image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.5f, 0.8f);
            
            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
            
            GameObject textObj = CreateText(text, buttonObj.transform);
            TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            
            return buttonObj;
        }
        
        private GameObject CreateInputField(string placeholder, Transform parent) {
            GameObject inputObj = GameObject.Instantiate(Resources.Load<GameObject>("UI/InputField"));
            if (inputObj == null) {
                inputObj = new GameObject("InputField");
                inputObj.transform.SetParent(parent);
                
                RectTransform rect = inputObj.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(0, 30);
                
                Image image = inputObj.AddComponent<Image>();
                image.color = new Color(0.2f, 0.2f, 0.2f);
                
                TMP_InputField inputField = inputObj.AddComponent<TMP_InputField>();
                
                // Create text area
                GameObject textArea = new GameObject("Text Area");
                textArea.transform.SetParent(inputObj.transform);
                RectTransform textAreaRect = textArea.AddComponent<RectTransform>();
                textAreaRect.anchorMin = Vector2.zero;
                textAreaRect.anchorMax = Vector2.one;
                textAreaRect.sizeDelta = Vector2.zero;
                textAreaRect.offsetMin = new Vector2(10, 5);
                textAreaRect.offsetMax = new Vector2(-10, -5);
                
                // Placeholder
                GameObject placeholderObj = CreateText(placeholder, textArea.transform);
                placeholderObj.name = "Placeholder";
                TextMeshProUGUI placeholderText = placeholderObj.GetComponent<TextMeshProUGUI>();
                placeholderText.color = new Color(0.5f, 0.5f, 0.5f);
                
                // Text
                GameObject textObj = CreateText("", textArea.transform);
                textObj.name = "Text";
                
                inputField.textViewport = textAreaRect;
                inputField.textComponent = textObj.GetComponent<TextMeshProUGUI>();
                inputField.placeholder = placeholderText;
            }
            
            inputObj.transform.SetParent(parent);
            return inputObj;
        }
        
        private GameObject CreateSlider(float min, float max, float value, Transform parent) {
            GameObject sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(parent);
            
            RectTransform rect = sliderObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 20);
            
            Slider slider = sliderObj.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            
            // Background
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(sliderObj.transform);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.sizeDelta = new Vector2(0, 0);
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f);
            
            // Fill area
            GameObject fillAreaObj = new GameObject("Fill Area");
            fillAreaObj.transform.SetParent(sliderObj.transform);
            RectTransform fillAreaRect = fillAreaObj.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.sizeDelta = new Vector2(-20, 0);
            
            // Fill
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillAreaObj.transform);
            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.sizeDelta = new Vector2(10, 0);
            Image fillImage = fillObj.AddComponent<Image>();
            fillImage.color = new Color(0.2f, 0.5f, 0.8f);
            
            // Handle
            GameObject handleObj = new GameObject("Handle");
            handleObj.transform.SetParent(sliderObj.transform);
            RectTransform handleRect = handleObj.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(0, 1);
            handleRect.sizeDelta = new Vector2(20, 0);
            Image handleImage = handleObj.AddComponent<Image>();
            handleImage.color = Color.white;
            
            // Assign references
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            
            return sliderObj;
        }
        
        private GameObject CreateToggle(string label, bool value, Transform parent) {
            GameObject toggleObj = new GameObject("Toggle");
            toggleObj.transform.SetParent(parent);
            
            RectTransform rect = toggleObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 30);
            
            HorizontalLayoutGroup layout = toggleObj.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            
            // Checkbox
            GameObject checkboxObj = new GameObject("Checkbox");
            checkboxObj.transform.SetParent(toggleObj.transform);
            RectTransform checkRect = checkboxObj.AddComponent<RectTransform>();
            checkRect.sizeDelta = new Vector2(20, 20);
            
            Image bgImage = checkboxObj.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f);
            
            Toggle toggle = checkboxObj.AddComponent<Toggle>();
            toggle.isOn = value;
            toggle.targetGraphic = bgImage;
            
            // Checkmark
            GameObject checkmarkObj = new GameObject("Checkmark");
            checkmarkObj.transform.SetParent(checkboxObj.transform);
            RectTransform checkmarkRect = checkmarkObj.AddComponent<RectTransform>();
            checkmarkRect.anchorMin = Vector2.zero;
            checkmarkRect.anchorMax = Vector2.one;
            checkmarkRect.sizeDelta = new Vector2(-4, -4);
            checkmarkRect.anchoredPosition = Vector2.zero;
            
            Image checkmarkImage = checkmarkObj.AddComponent<Image>();
            checkmarkImage.color = new Color(0.2f, 0.8f, 0.2f);
            
            toggle.graphic = checkmarkImage;
            
            // Label
            GameObject labelObj = CreateText(label, toggleObj.transform);
            
            return toggleObj;
        }
        
        private GameObject CreateProgressBar(Transform parent) {
            GameObject progressObj = new GameObject("ProgressBar");
            progressObj.transform.SetParent(parent);
            
            RectTransform rect = progressObj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0, 30);
            
            Slider progressBar = progressObj.AddComponent<Slider>();
            progressBar.minValue = 0;
            progressBar.maxValue = 1;
            progressBar.value = 0;
            progressBar.interactable = false;
            
            // Background
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(progressObj.transform);
            RectTransform bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f);
            
            // Fill area
            GameObject fillAreaObj = new GameObject("Fill Area");
            fillAreaObj.transform.SetParent(progressObj.transform);
            RectTransform fillAreaRect = fillAreaObj.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = Vector2.zero;
            fillAreaRect.anchorMax = Vector2.one;
            fillAreaRect.sizeDelta = Vector2.zero;
            
            // Fill
            GameObject fillObj = new GameObject("Fill");
            fillObj.transform.SetParent(fillAreaObj.transform);
            RectTransform fillRect = fillObj.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0, 1);
            fillRect.sizeDelta = Vector2.zero;
            Image fillImage = fillObj.AddComponent<Image>();
            fillImage.color = new Color(0.2f, 0.8f, 0.2f);
            
            progressBar.fillRect = fillRect;
            
            // Progress text
            GameObject textObj = CreateText("0%", progressObj.transform);
            textObj.name = "ProgressText";
            TextMeshProUGUI progressText = textObj.GetComponent<TextMeshProUGUI>();
            progressText.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            
            return progressObj;
        }
        
        private GameObject CreateScrollView(Transform parent) {
            GameObject scrollViewObj = new GameObject("ScrollView");
            scrollViewObj.transform.SetParent(parent);
            
            RectTransform rect = scrollViewObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            
            ScrollRect scrollRect = scrollViewObj.AddComponent<ScrollRect>();
            Image bgImage = scrollViewObj.AddComponent<Image>();
            bgImage.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            
            // Viewport
            GameObject viewportObj = new GameObject("Viewport");
            viewportObj.transform.SetParent(scrollViewObj.transform);
            RectTransform viewportRect = viewportObj.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportRect.offsetMin = new Vector2(10, 10);
            viewportRect.offsetMax = new Vector2(-10, -10);
            
            Image viewportImage = viewportObj.AddComponent<Image>();
            viewportImage.color = new Color(1, 1, 1, 0);
            Mask viewportMask = viewportObj.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
            
            // Content
            GameObject contentObj = new GameObject("Content");
            contentObj.transform.SetParent(viewportObj.transform);
            RectTransform contentRect = contentObj.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 300);
            
            VerticalLayoutGroup contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(10, 10, 10, 10);
            contentLayout.spacing = 10;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            
            ContentSizeFitter contentFitter = contentObj.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            // Scrollbar
            GameObject scrollbarObj = new GameObject("Scrollbar");
            scrollbarObj.transform.SetParent(scrollViewObj.transform);
            RectTransform scrollbarRect = scrollbarObj.AddComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1, 0);
            scrollbarRect.anchorMax = new Vector2(1, 1);
            scrollbarRect.pivot = new Vector2(1, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(20, 0);
            scrollbarRect.anchoredPosition = new Vector2(0, 0);
            
            Image scrollbarImage = scrollbarObj.AddComponent<Image>();
            scrollbarImage.color = new Color(0.1f, 0.1f, 0.1f);
            
            Scrollbar scrollbar = scrollbarObj.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.TopToBottom;
            
            // Scrollbar handle
            GameObject handleObj = new GameObject("Handle");
            handleObj.transform.SetParent(scrollbarObj.transform);
            RectTransform handleRect = handleObj.AddComponent<RectTransform>();
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(1, 1);
            handleRect.sizeDelta = new Vector2(-4, -4);
            
            Image handleImage = handleObj.AddComponent<Image>();
            handleImage.color = new Color(0.4f, 0.4f, 0.4f);
            
            scrollbar.targetGraphic = handleImage;
            scrollbar.handleRect = handleRect;
            
            // Setup ScrollRect
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.horizontal = false;
            
            return scrollViewObj;
        }
        
        #endregion
        
        #region UI Content Creation
        
        private void CreateFileSelectionUI(Transform parent) {
            // Image preview
            GameObject previewObj = new GameObject("ImagePreview");
            previewObj.transform.SetParent(parent);
            RectTransform previewRect = previewObj.AddComponent<RectTransform>();
            previewRect.sizeDelta = new Vector2(0, 200);
            
            Image previewImage = previewObj.AddComponent<Image>();
            previewImage.color = new Color(0.2f, 0.2f, 0.2f);
            previewImage.preserveAspect = true;
            
            AspectRatioFitter aspectFitter = previewObj.AddComponent<AspectRatioFitter>();
            aspectFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            
            // File path display
            GameObject pathObj = CreateInputField("No file selected", parent);
            pathObj.name = "FilePathInput";
            TMP_InputField pathInput = pathObj.GetComponent<TMP_InputField>();
            pathInput.interactable = false;
            
            // Browse button
            GameObject browseBtn = CreateButton("Browse for Map Image", parent, () => {
                TraversifyManager.Instance?.OpenFileExplorer();
            });
        }
        
        private void CreateSettingsUI(Transform parent) {
            // Terrain size
            CreateLabeledSlider("Terrain Size", parent, 100, 2000, 1000, (value) => {
                if (TraversifyManager.Instance != null) {
                    Vector3 size = TraversifyManager.Instance.terrainSize;
                    size.x = size.z = value;
                    TraversifyManager.Instance.terrainSize = size;
                }
            });
            
            // Terrain height
            CreateLabeledSlider("Max Height", parent, 10, 500, 100, (value) => {
                if (TraversifyManager.Instance != null) {
                    Vector3 size = TraversifyManager.Instance.terrainSize;
                    size.y = value;
                    TraversifyManager.Instance.terrainSize = size;
                }
            });
            
            // Water settings
            GameObject waterToggle = CreateToggle("Generate Water", true, parent);
            waterToggle.GetComponentInChildren<Toggle>().onValueChanged.AddListener((value) => {
                if (TraversifyManager.Instance != null) {
                    TraversifyManager.Instance.generateWater = value;
                }
            });
            
            CreateLabeledSlider("Water Level", parent, 0, 50, 20, (value) => {
                if (TraversifyManager.Instance != null) {
                    TraversifyManager.Instance.waterHeight = value;
                }
            });
            
            // Quality settings
            GameObject qualityToggle = CreateToggle("High Quality Analysis", true, parent);
            qualityToggle.GetComponentInChildren<Toggle>().onValueChanged.AddListener((value) => {
                if (TraversifyManager.Instance != null) {
                    TraversifyManager.Instance.useHighQualityAnalysis = value;
                }
            });
            
            // Object settings
            CreateLabeledSlider("Max Objects", parent, 10, 200, 100, (value) => {
                if (TraversifyManager.Instance != null) {
                    TraversifyManager.Instance.maxObjectsToProcess = (int)value;
                }
            });
            
            GameObject groupToggle = CreateToggle("Group Similar Objects", true, parent);
            groupToggle.GetComponentInChildren<Toggle>().onValueChanged.AddListener((value) => {
                if (TraversifyManager.Instance != null) {
                    TraversifyManager.Instance.groupSimilarObjects = value;
                }
            });
        }
        
        private void CreateActionButtons(Transform parent) {
            // Generate button
            GameObject generateBtn = CreateButton("Generate Environment", parent, () => {
                TraversifyManager.Instance?.StartTerrainGeneration();
            });
            generateBtn.name = "GenerateButton";
            
            // Clear button
            GameObject clearBtn = CreateButton("Clear Environment", parent, () => {
                if (Traversify.Instance != null) {
                    Traversify.Instance.ClearGeneratedEnvironment();
                }
            });
            
            // Save button
            GameObject saveBtn = CreateButton("Save Environment", parent, () => {
                if (Traversify.Instance != null) {
                    string path = Traversify.Instance.SaveEnvironment();
                    if (!string.IsNullOrEmpty(path)) {
                        #if UNITY_EDITOR
                        EditorUtility.DisplayDialog("Save Complete", 
                            $"Environment saved to:\n{path}", "OK");
                        #endif
                    }
                }
            });
            
            // API Key input
            CreateSectionHeader("API Configuration", parent);
            GameObject apiKeyInput = CreateInputField("Enter OpenAI API Key", parent);
            apiKeyInput.name = "APIKeyInput";
            TMP_InputField apiInput = apiKeyInput.GetComponent<TMP_InputField>();
            apiInput.contentType = TMP_InputField.ContentType.Password;
            apiInput.onEndEdit.AddListener((value) => {
                if (TraversifyManager.Instance != null) {
                    TraversifyManager.Instance.openAIApiKey = value;
                }
            });
        }
        
        private GameObject CreateLabeledSlider(string label, Transform parent, float min, float max, float value, 
            UnityEngine.Events.UnityAction<float> onValueChanged) {
            GameObject container = new GameObject(label + " Container");
            container.transform.SetParent(parent);
            
            VerticalLayoutGroup layout = container.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 5;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            
            // Label with value
            GameObject labelObj = CreateText($"{label}: {value:F0}", container.transform);
            TextMeshProUGUI labelText = labelObj.GetComponent<TextMeshProUGUI>();
            
            // Slider
            GameObject sliderObj = CreateSlider(min, max, value, container.transform);
            Slider slider = sliderObj.GetComponent<Slider>();
            slider.onValueChanged.AddListener((v) => {
                labelText.text = $"{label}: {v:F0}";
                onValueChanged?.Invoke(v);
            });
            
            return container;
        }
        
        #endregion
        
        #region Component Linking
        
        private void LinkComponents(GameObject root) {
            // Get all main components
            Traversify traversifyMain = root.GetComponentInChildren<Traversify>();
            TraversifyManager manager = root.GetComponentInChildren<TraversifyManager>();
            MapAnalyzer analyzer = root.GetComponentInChildren<MapAnalyzer>();
            ModelGenerator modelGen = root.GetComponentInChildren<ModelGenerator>();
            TerrainGenerator terrainGen = root.GetComponentInChildren<TerrainGenerator>();
            SegmentationVisualizer segVis = root.GetComponentInChildren<SegmentationVisualizer>();
            
            // Link UI references to manager
            if (manager != null) {
                Canvas canvas = root.GetComponentInChildren<Canvas>();
                if (canvas != null) {
                    // Find UI elements
                    Transform controlPanel = canvas.transform.Find("Control Panel");
                    Transform progressPanel = canvas.transform.Find("Progress Panel");
                    
                    // Set manager UI references
                    manager.imagePreview = controlPanel?.Find("ImagePreview")?.GetComponent<Image>();
                    manager.filePathInput = controlPanel?.Find("FilePathInput")?.GetComponent<TMP_InputField>();
                    manager.generateButton = controlPanel?.Find("GenerateButton")?.GetComponent<Button>();
                    manager.apiKeyInput = controlPanel?.Find("APIKeyInput")?.GetComponent<TMP_InputField>();
                    
                    manager.loadingPanel = progressPanel?.gameObject;
                    manager.progressBar = progressPanel?.Find("ProgressBar")?.GetComponent<Slider>();
                    manager.progressText = progressPanel?.Find("ProgressBar/ProgressText")?.GetComponent<TextMeshProUGUI>();
                    manager.stageText = progressPanel?.Find("StageText")?.GetComponent<TextMeshProUGUI>();
                    manager.detailsText = progressPanel?.Find("DetailsText")?.GetComponent<TextMeshProUGUI>();
                }
            }
            
            // Link analyzer to terrain generator
            if (analyzer != null && terrainGen != null) {
                analyzer.SetTerrainGenerator(terrainGen);
            }
            
            // Set singleton references
            if (traversifyMain != null) {
                // Traversify will find its own component references through GetComponent
            }
        }
        
        #endregion
        
        #region Default Settings
        
        private void ApplyDefaultSettings(GameObject root) {
            // Load any saved preferences
            LoadUserPreferences();
            
            // Apply default material settings
            CreateDefaultMaterials();
            
            // Setup default compute shaders
            SetupComputeShaders();
            
            // Configure default LOD settings
            ConfigureLODSettings();
        }
        
        private void LoadUserPreferences() {
            // Load from PlayerPrefs or config file
            string apiKey = PlayerPrefs.GetString("TraversifyAPIKey", "");
            if (!string.IsNullOrEmpty(apiKey) && TraversifyManager.Instance != null) {
                TraversifyManager.Instance.openAIApiKey = apiKey;
            }
        }
        
        private void CreateDefaultMaterials() {
            #if UNITY_EDITOR
            // Create default materials if they don't exist
            string[] materialNames = {
                "SegmentOverlay",
                "SegmentOutline",
                "WaterSurface",
                "TerrainPath",
                "ModelDefault"
            };
            
            foreach (string matName in materialNames) {
                string path = Path.Combine(MATERIALS_PATH, $"{matName}.mat");
                if (!File.Exists(path)) {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.name = matName;
                    AssetDatabase.CreateAsset(mat, path);
                }
            }
            
            AssetDatabase.SaveAssets();
            #endif
        }
        
        private void SetupComputeShaders() {
            // Compute shaders would be loaded here if needed
            // Currently using CPU/Jobs for processing
        }
        
        private void ConfigureLODSettings() {
            // Set global LOD bias
            QualitySettings.lodBias = 2.0f;
            QualitySettings.maximumLODLevel = 0;
        }
        
        #endregion
        
        #region Scene Configuration
        
        private void ConfigureSceneSettings() {
            // Setup lighting
            SetupLighting();
            
            // Setup camera
            SetupCamera();
            
            // Setup rendering settings
            SetupRenderingSettings();
            
            // Setup navigation
            SetupNavigation();
        }
        
        private void SetupLighting() {
            // Find or create directional light
            Light sunLight = FindObjectOfType<Light>();
            if (sunLight == null || sunLight.type != LightType.Directional) {
                GameObject lightObj = new GameObject("Directional Light");
                sunLight = lightObj.AddComponent<Light>();
                sunLight.type = LightType.Directional;
            }
            
            // Configure sun light
            sunLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            sunLight.intensity = 1.2f;
            sunLight.color = new Color(1f, 0.95f, 0.8f);
            sunLight.shadows = LightShadows.Soft;
            
            // Ambient lighting
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.5f, 0.7f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.5f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.3f, 0.3f);
            
            // Fog settings
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.005f;
            RenderSettings.fogColor = new Color(0.7f, 0.8f, 0.9f);
        }
        
        private void SetupCamera() {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) {
                GameObject cameraObj = new GameObject("Main Camera");
                mainCamera = cameraObj.AddComponent<Camera>();
                cameraObj.tag = "MainCamera";
            }
            
            // Position camera
            mainCamera.transform.position = new Vector3(500, 200, -200);
            mainCamera.transform.rotation = Quaternion.Euler(30, 0, 0);
            
            // Camera settings
            mainCamera.fieldOfView = 60;
            mainCamera.nearClipPlane = 1f;
            mainCamera.farClipPlane = 2000f;
            
            // Add camera controller if needed
            if (mainCamera.GetComponent<AudioListener>() == null) {
                mainCamera.gameObject.AddComponent<AudioListener>();
            }
        }
        
        private void SetupRenderingSettings() {
            // Shadow settings
            QualitySettings.shadows = ShadowQuality.All;