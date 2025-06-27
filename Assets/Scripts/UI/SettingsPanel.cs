/*************************************************************************
 *  Traversify – SettingsPanel.cs                                        *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 04:02:12 UTC                                     *
 *  Desc   : Manages the settings UI for the Traversify environment      *
 *           generation system. Provides configuration for terrain,      *
 *           object placement, AI analysis, performance settings,        *
 *           and visualization options with persistent preferences.      *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Traversify.Core;
using Traversify.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Traversify.UI {
    /// <summary>
    /// Manages the settings UI for the Traversify environment generation system,
    /// including terrain, object placement, AI analysis, performance, and visualization options.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class SettingsPanel : MonoBehaviour {
        #region Inspector Properties
        
        [Header("UI References")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Button _exportButton;
        [SerializeField] private Button _importButton;
        [SerializeField] private TMP_Dropdown _presetDropdown;
        [SerializeField] private GameObject _tabButtonContainer;
        [SerializeField] private GameObject[] _tabPanels;
        
        [Header("General Settings UI")]
        [SerializeField] private TMP_InputField _projectNameInput;
        [SerializeField] private Toggle _autoSaveToggle;
        [SerializeField] private Slider _autoSaveIntervalSlider;
        [SerializeField] private TMP_Text _autoSaveIntervalText;
        [SerializeField] private Toggle _showDebugInfoToggle;
        [SerializeField] private Toggle _useGPUAccelerationToggle;
        [SerializeField] private TMP_InputField _outputPathInput;
        [SerializeField] private Button _browseOutputPathButton;
        
        [Header("Terrain Settings UI")]
        [SerializeField] private TMP_InputField _terrainWidthInput;
        [SerializeField] private TMP_InputField _terrainLengthInput;
        [SerializeField] private Slider _terrainHeightSlider;
        [SerializeField] private TMP_Text _terrainHeightText;
        [SerializeField] private Slider _terrainResolutionSlider;
        [SerializeField] private TMP_Text _terrainResolutionText;
        [SerializeField] private Toggle _generateWaterToggle;
        [SerializeField] private Slider _waterHeightSlider;
        [SerializeField] private TMP_Text _waterHeightText;
        [SerializeField] private Toggle _applyErosionToggle;
        [SerializeField] private TMP_InputField _erosionIterationsInput;
        [SerializeField] private Toggle _buildNavMeshToggle;
        
        [Header("AI Settings UI")]
        [SerializeField] private TMP_InputField _openAIKeyInput;
        [SerializeField] private Toggle _useHighQualityAnalysisToggle;
        [SerializeField] private Slider _confidenceThresholdSlider;
        [SerializeField] private TMP_Text _confidenceThresholdText;
        [SerializeField] private Toggle _useFasterRCNNToggle;
        [SerializeField] private Toggle _useSAMToggle;
        [SerializeField] private TMP_InputField _maxObjectsInput;
        [SerializeField] private Toggle _groupSimilarObjectsToggle;
        [SerializeField] private Slider _similarityThresholdSlider;
        [SerializeField] private TMP_Text _similarityThresholdText;
        
        [Header("Performance Settings UI")]
        [SerializeField] private Slider _maxConcurrentRequestsSlider;
        [SerializeField] private TMP_Text _maxConcurrentRequestsText;
        [SerializeField] private Slider _processingBatchSizeSlider;
        [SerializeField] private TMP_Text _processingBatchSizeText;
        [SerializeField] private Slider _processingTimeoutSlider;
        [SerializeField] private TMP_Text _processingTimeoutText;
        [SerializeField] private Slider _apiRateLimitSlider;
        [SerializeField] private TMP_Text _apiRateLimitText;
        [SerializeField] private TMP_Dropdown _qualityPresetDropdown;
        
        [Header("Advanced Settings UI")]
        [SerializeField] private Toggle _enableAdvancedFeaturesToggle;
        [SerializeField] private Toggle _enableChunkStreamingToggle;
        [SerializeField] private TMP_InputField _chunkSizeInput;
        [SerializeField] private TMP_InputField _loadRadiusInput;
        [SerializeField] private Toggle _generateMetadataToggle;
        [SerializeField] private Toggle _saveGeneratedAssetsToggle;
        [SerializeField] private Toggle _saveThumbnailsToggle;
        
        [Header("Debug Settings")]
        [SerializeField] private TraversifyDebugger _debugger;
        [SerializeField] private bool _logSettingsChanges = true;
        
        #endregion
        
        #region Private Fields
        
        private CanvasGroup _canvasGroup;
        private Button[] _tabButtons;
        private int _currentTabIndex = 0;
        private SettingsData _currentSettings = new SettingsData();
        private SettingsData _defaultSettings = new SettingsData();
        private Dictionary<string, SettingsData> _presetSettings = new Dictionary<string, SettingsData>();
        private bool _initialized = false;
        private bool _ignoreValueChanges = false;
        
        // For input validation
        private Dictionary<TMP_InputField, ValidatorDelegate> _inputValidators = new Dictionary<TMP_InputField, ValidatorDelegate>();
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Gets or sets whether the settings panel is visible.
        /// </summary>
        public bool IsVisible {
            get => _canvasGroup.alpha > 0;
            set {
                _canvasGroup.alpha = value ? 1 : 0;
                _canvasGroup.interactable = value;
                _canvasGroup.blocksRaycasts = value;
                
                if (value) {
                    // Refresh settings when showing panel
                    LoadCurrentSettings();
                }
            }
        }
        
        #endregion
        
        #region Unity Lifecycle Methods
        
        private void Awake() {
            _canvasGroup = GetComponent<CanvasGroup>();
            
            if (_debugger == null) {
                _debugger = FindObjectOfType<TraversifyDebugger>();
            }
            
            // Initialize default settings
            InitializeDefaultSettings();
            
            // Setup UI components
            SetupButtons();
            SetupTabs();
            SetupInputValidation();
            
            // Hide panel by default
            IsVisible = false;
        }
        
        private void Start() {
            // Initialize current settings from TraversifyManager or load from disk
            LoadCurrentSettings();
            
            // Load presets
            LoadPresets();
            
            // Mark as initialized
            _initialized = true;
        }
        
        private void OnEnable() {
            RegisterCallbacks(true);
        }
        
        private void OnDisable() {
            RegisterCallbacks(false);
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Shows the settings panel.
        /// </summary>
        public void Show() {
            IsVisible = true;
            LoadCurrentSettings();
        }
        
        /// <summary>
        /// Hides the settings panel.
        /// </summary>
        public void Hide() {
            IsVisible = false;
        }
        
        /// <summary>
        /// Toggles the visibility of the settings panel.
        /// </summary>
        public void Toggle() {
            IsVisible = !IsVisible;
        }
        
        /// <summary>
        /// Applies the current settings to the TraversifyManager.
        /// </summary>
        public void ApplySettings() {
            if (!_initialized) return;
            
            // Get TraversifyManager instance
            var manager = TraversifyManager.Instance;
            if (manager == null) {
                LogWarning("TraversifyManager not found, settings will not be applied");
                return;
            }
            
            // Apply settings to manager
            try {
                // General settings
                if (!string.IsNullOrEmpty(_currentSettings.projectName)) {
                    // Project name might be used in multiple places
                }
                
                // Terrain settings
                manager.terrainSize = new Vector3(
                    _currentSettings.terrainWidth, 
                    _currentSettings.terrainHeight,
                    _currentSettings.terrainLength
                );
                manager.terrainResolution = _currentSettings.terrainResolution;
                manager.generateWater = _currentSettings.generateWater;
                manager.waterHeight = _currentSettings.waterHeight;
                
                // AI settings
                manager.openAIApiKey = _currentSettings.openAIKey;
                manager.useHighQualityAnalysis = _currentSettings.useHighQualityAnalysis;
                manager.detectionThreshold = _currentSettings.confidenceThreshold;
                manager.useFasterRCNN = _currentSettings.useFasterRCNN;
                manager.useSAM = _currentSettings.useSAM;
                manager.maxObjectsToProcess = _currentSettings.maxObjectsToProcess;
                manager.groupSimilarObjects = _currentSettings.groupSimilarObjects;
                manager.instancingSimilarity = _currentSettings.similarityThreshold;
                
                // Performance settings
                manager.maxConcurrentAPIRequests = _currentSettings.maxConcurrentRequests;
                manager.processingBatchSize = _currentSettings.processingBatchSize;
                manager.processingTimeout = _currentSettings.processingTimeout;
                manager.apiRateLimitDelay = _currentSettings.apiRateLimit;
                manager.useGPUAcceleration = _currentSettings.useGPUAcceleration;
                
                // Advanced settings
                manager.enableDebugVisualization = _currentSettings.showDebugInfo;
                manager.saveGeneratedAssets = _currentSettings.saveGeneratedAssets;
                manager.generateMetadata = _currentSettings.generateMetadata;
                manager.assetSavePath = _currentSettings.outputPath;
                
                Log("Settings applied successfully");
            }
            catch (Exception ex) {
                LogError($"Error applying settings: {ex.Message}");
            }
            
            // Save settings to disk
            SaveSettingsToDisk();
        }
        
        /// <summary>
        /// Resets settings to default values.
        /// </summary>
        public void ResetToDefaults() {
            if (!_initialized) return;
            
            // Confirm reset
            bool confirmed = true; // In a real app, show a confirmation dialog
            
            if (confirmed) {
                _currentSettings = new SettingsData(_defaultSettings);
                UpdateUIFromSettings();
                Log("Settings reset to defaults");
            }
        }
        
        /// <summary>
        /// Exports current settings to a JSON file.
        /// </summary>
        public void ExportSettings() {
            if (!_initialized) return;
            
            #if UNITY_EDITOR
            string path = EditorUtility.SaveFilePanel(
                "Export Settings",
                Application.dataPath,
                "TraversifySettings.json",
                "json"
            );
            
            if (!string.IsNullOrEmpty(path)) {
                string json = JsonUtility.ToJson(_currentSettings, true);
                File.WriteAllText(path, json);
                Log($"Settings exported to {path}");
            }
            #else
            LogWarning("Settings export is only available in the Unity Editor");
            #endif
        }
        
        /// <summary>
        /// Imports settings from a JSON file.
        /// </summary>
        public void ImportSettings() {
            if (!_initialized) return;
            
            #if UNITY_EDITOR
            string path = EditorUtility.OpenFilePanel(
                "Import Settings",
                Application.dataPath,
                "json"
            );
            
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) {
                try {
                    string json = File.ReadAllText(path);
                    _currentSettings = JsonUtility.FromJson<SettingsData>(json);
                    UpdateUIFromSettings();
                    Log($"Settings imported from {path}");
                }
                catch (Exception ex) {
                    LogError($"Error importing settings: {ex.Message}");
                }
            }
            #else
            LogWarning("Settings import is only available in the Unity Editor");
            #endif
        }
        
        /// <summary>
        /// Shows the specified tab.
        /// </summary>
        public void ShowTab(int tabIndex) {
            if (tabIndex < 0 || tabIndex >= _tabPanels.Length) {
                LogWarning($"Invalid tab index: {tabIndex}");
                return;
            }
            
            // Hide all panels
            for (int i = 0; i < _tabPanels.Length; i++) {
                _tabPanels[i].SetActive(i == tabIndex);
                
                if (_tabButtons != null && i < _tabButtons.Length) {
                    // Update button appearance
                    _tabButtons[i].interactable = i != tabIndex;
                }
            }
            
            _currentTabIndex = tabIndex;
        }
        
        /// <summary>
        /// Saves the current settings as a preset.
        /// </summary>
        public void SaveAsPreset(string presetName) {
            if (string.IsNullOrEmpty(presetName)) {
                LogWarning("Preset name cannot be empty");
                return;
            }
            
            // Save current settings as a preset
            _presetSettings[presetName] = new SettingsData(_currentSettings);
            
            // Update preset dropdown
            UpdatePresetDropdown();
            
            // Save presets to disk
            SavePresetsToDisk();
            
            Log($"Settings saved as preset: {presetName}");
        }
        
        /// <summary>
        /// Loads a preset by name.
        /// </summary>
        public void LoadPreset(string presetName) {
            if (!_presetSettings.ContainsKey(presetName)) {
                LogWarning($"Preset not found: {presetName}");
                return;
            }
            
            // Load preset settings
            _currentSettings = new SettingsData(_presetSettings[presetName]);
            
            // Update UI
            UpdateUIFromSettings();
            
            Log($"Preset loaded: {presetName}");
        }
        
        #endregion
        
        #region UI Event Handlers
        
        private void OnCloseButtonClicked() {
            Hide();
        }
        
        private void OnSaveButtonClicked() {
            UpdateSettingsFromUI();
            ApplySettings();
            Hide();
        }
        
        private void OnResetButtonClicked() {
            ResetToDefaults();
        }
        
        private void OnExportButtonClicked() {
            ExportSettings();
        }
        
        private void OnImportButtonClicked() {
            ImportSettings();
        }
        
        private void OnTabButtonClicked(int tabIndex) {
            ShowTab(tabIndex);
        }
        
        private void OnPresetDropdownChanged(int index) {
            if (!_initialized || _ignoreValueChanges) return;
            
            string presetName = _presetDropdown.options[index].text;
            
            if (presetName == "Custom") {
                // Do nothing, keep current settings
                return;
            }
            
            LoadPreset(presetName);
        }
        
        private void OnTerrainSizeChanged(string value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            if (float.TryParse(value, out float result)) {
                UpdateSettingsFromUI();
            }
        }
        
        private void OnTerrainHeightSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _terrainHeightText.text = $"{value:F0}m";
            _currentSettings.terrainHeight = value;
        }
        
        private void OnTerrainResolutionSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            // Convert to valid resolution value (power of 2 plus 1)
            int resolution = ConvertToValidResolution(value);
            _terrainResolutionText.text = $"{resolution}";
            _currentSettings.terrainResolution = resolution;
        }
        
        private void OnWaterHeightSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _waterHeightText.text = $"{value:P0}";
            _currentSettings.waterHeight = value;
        }
        
        private void OnConfidenceThresholdSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _confidenceThresholdText.text = $"{value:P0}";
            _currentSettings.confidenceThreshold = value;
        }
        
        private void OnSimilarityThresholdSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _similarityThresholdText.text = $"{value:P0}";
            _currentSettings.similarityThreshold = value;
        }
        
        private void OnMaxConcurrentRequestsSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            int intValue = Mathf.RoundToInt(value);
            _maxConcurrentRequestsText.text = intValue.ToString();
            _currentSettings.maxConcurrentRequests = intValue;
        }
        
        private void OnProcessingBatchSizeSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            int intValue = Mathf.RoundToInt(value);
            _processingBatchSizeText.text = intValue.ToString();
            _currentSettings.processingBatchSize = intValue;
        }
        
        private void OnProcessingTimeoutSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _processingTimeoutText.text = $"{value:F0}s";
            _currentSettings.processingTimeout = value;
        }
        
        private void OnApiRateLimitSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _apiRateLimitText.text = $"{value:F1}s";
            _currentSettings.apiRateLimit = value;
        }
        
        private void OnQualityPresetDropdownChanged(int index) {
            if (!_initialized || _ignoreValueChanges) return;
            
            string presetName = _qualityPresetDropdown.options[index].text;
            
            switch (presetName) {
                case "Low":
                    ApplyQualityPreset(QualityPreset.Low);
                    break;
                case "Medium":
                    ApplyQualityPreset(QualityPreset.Medium);
                    break;
                case "High":
                    ApplyQualityPreset(QualityPreset.High);
                    break;
                case "Ultra":
                    ApplyQualityPreset(QualityPreset.Ultra);
                    break;
            }
        }
        
        private void OnBrowseOutputPathButtonClicked() {
            #if UNITY_EDITOR
            string path = EditorUtility.SaveFolderPanel(
                "Select Output Directory",
                Application.dataPath,
                ""
            );
            
            if (!string.IsNullOrEmpty(path)) {
                // Convert to relative path if within Assets folder
                if (path.StartsWith(Application.dataPath)) {
                    path = "Assets" + path.Substring(Application.dataPath.Length);
                }
                
                _outputPathInput.text = path;
                _currentSettings.outputPath = path;
            }
            #else
            LogWarning("Browse feature is only available in the Unity Editor");
            #endif
        }
        
        private void OnAutoSaveToggleChanged(bool isOn) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _currentSettings.autoSave = isOn;
            
            // Update UI
            _autoSaveIntervalSlider.interactable = isOn;
        }
        
        private void OnAutoSaveIntervalSliderChanged(float value) {
            if (!_initialized || _ignoreValueChanges) return;
            
            _autoSaveIntervalText.text = $"{value:F0}m";
            _currentSettings.autoSaveInterval = value;
        }
        
        #endregion
        
        #region Private Helper Methods
        
        private void SetupButtons() {
            // Setup main buttons
            if (_closeButton != null) _closeButton.onClick.AddListener(OnCloseButtonClicked);
            if (_saveButton != null) _saveButton.onClick.AddListener(OnSaveButtonClicked);
            if (_resetButton != null) _resetButton.onClick.AddListener(OnResetButtonClicked);
            if (_exportButton != null) _exportButton.onClick.AddListener(OnExportButtonClicked);
            if (_importButton != null) _importButton.onClick.AddListener(OnImportButtonClicked);
            if (_browseOutputPathButton != null) _browseOutputPathButton.onClick.AddListener(OnBrowseOutputPathButtonClicked);
        }
        
        private void SetupTabs() {
            // Find all tab buttons
            if (_tabButtonContainer != null) {
                _tabButtons = _tabButtonContainer.GetComponentsInChildren<Button>();
                
                // Setup click events for tab buttons
                for (int i = 0; i < _tabButtons.Length; i++) {
                    int tabIndex = i; // Needed for closure
                    _tabButtons[i].onClick.AddListener(() => OnTabButtonClicked(tabIndex));
                }
            }
            
            // Show first tab by default
            ShowTab(0);
        }
        
        private void SetupInputValidation() {
            // Setup validators for input fields
            if (_terrainWidthInput != null) {
                _inputValidators[_terrainWidthInput] = ValidateIntegerInput;
                _terrainWidthInput.onValueChanged.AddListener(OnTerrainSizeChanged);
            }
            
            if (_terrainLengthInput != null) {
                _inputValidators[_terrainLengthInput] = ValidateIntegerInput;
                _terrainLengthInput.onValueChanged.AddListener(OnTerrainSizeChanged);
            }
            
            if (_erosionIterationsInput != null) {
                _inputValidators[_erosionIterationsInput] = ValidateIntegerInput;
            }
            
            if (_maxObjectsInput != null) {
                _inputValidators[_maxObjectsInput] = ValidateIntegerInput;
            }
            
            if (_chunkSizeInput != null) {
                _inputValidators[_chunkSizeInput] = ValidateIntegerInput;
            }
            
            if (_loadRadiusInput != null) {
                _inputValidators[_loadRadiusInput] = ValidateIntegerInput;
            }
            
            // Setup slider callbacks
            if (_terrainHeightSlider != null) {
                _terrainHeightSlider.onValueChanged.AddListener(OnTerrainHeightSliderChanged);
            }
            
            if (_terrainResolutionSlider != null) {
                _terrainResolutionSlider.onValueChanged.AddListener(OnTerrainResolutionSliderChanged);
            }
            
            if (_waterHeightSlider != null) {
                _waterHeightSlider.onValueChanged.AddListener(OnWaterHeightSliderChanged);
            }
            
            if (_confidenceThresholdSlider != null) {
                _confidenceThresholdSlider.onValueChanged.AddListener(OnConfidenceThresholdSliderChanged);
            }
            
            if (_similarityThresholdSlider != null) {
                _similarityThresholdSlider.onValueChanged.AddListener(OnSimilarityThresholdSliderChanged);
            }
            
            if (_maxConcurrentRequestsSlider != null) {
                _maxConcurrentRequestsSlider.onValueChanged.AddListener(OnMaxConcurrentRequestsSliderChanged);
            }
            
            if (_processingBatchSizeSlider != null) {
                _processingBatchSizeSlider.onValueChanged.AddListener(OnProcessingBatchSizeSliderChanged);
            }
            
            if (_processingTimeoutSlider != null) {
                _processingTimeoutSlider.onValueChanged.AddListener(OnProcessingTimeoutSliderChanged);
            }
            
            if (_apiRateLimitSlider != null) {
                _apiRateLimitSlider.onValueChanged.AddListener(OnApiRateLimitSliderChanged);
            }
            
            if (_autoSaveToggle != null) {
                _autoSaveToggle.onValueChanged.AddListener(OnAutoSaveToggleChanged);
            }
            
            if (_autoSaveIntervalSlider != null) {
                _autoSaveIntervalSlider.onValueChanged.AddListener(OnAutoSaveIntervalSliderChanged);
            }
            
            // Setup dropdown callbacks
            if (_presetDropdown != null) {
                _presetDropdown.onValueChanged.AddListener(OnPresetDropdownChanged);
            }
            
            if (_qualityPresetDropdown != null) {
                _qualityPresetDropdown.onValueChanged.AddListener(OnQualityPresetDropdownChanged);
            }
        }
        
        private void RegisterCallbacks(bool register) {
            // Register or unregister input field validation callbacks
            foreach (var kvp in _inputValidators) {
                TMP_InputField inputField = kvp.Key;
                ValidatorDelegate validator = kvp.Value;
                
                if (inputField != null) {
                    if (register) {
                        inputField.onEndEdit.AddListener((value) => validator(inputField, value));
                    } else {
                        inputField.onEndEdit.RemoveAllListeners();
                    }
                }
            }
        }
        
        private void InitializeDefaultSettings() {
            // Set default values
            _defaultSettings.projectName = "New Project";
            _defaultSettings.autoSave = true;
            _defaultSettings.autoSaveInterval = 5f;
            _defaultSettings.showDebugInfo = false;
            _defaultSettings.useGPUAcceleration = true;
            _defaultSettings.outputPath = "Assets/GeneratedTerrains";
            
            // Terrain
            _defaultSettings.terrainWidth = 500f;
            _defaultSettings.terrainLength = 500f;
            _defaultSettings.terrainHeight = 100f;
            _defaultSettings.terrainResolution = 513;
            _defaultSettings.generateWater = true;
            _defaultSettings.waterHeight = 0.3f;
            _defaultSettings.applyErosion = true;
            _defaultSettings.erosionIterations = 1000;
            _defaultSettings.buildNavMesh = true;
            
            // AI
            _defaultSettings.openAIKey = "";
            _defaultSettings.useHighQualityAnalysis = true;
            _defaultSettings.confidenceThreshold = 0.5f;
            _defaultSettings.useFasterRCNN = true;
            _defaultSettings.useSAM = true;
            _defaultSettings.maxObjectsToProcess = 100;
            _defaultSettings.groupSimilarObjects = true;
            _defaultSettings.similarityThreshold = 0.8f;
            
            // Performance
            _defaultSettings.maxConcurrentRequests = 3;
            _defaultSettings.processingBatchSize = 5;
            _defaultSettings.processingTimeout = 300f;
            _defaultSettings.apiRateLimit = 0.5f;
            
            // Advanced
            _defaultSettings.enableAdvancedFeatures = false;
            _defaultSettings.enableChunkStreaming = false;
            _defaultSettings.chunkSize = 500;
            _defaultSettings.loadRadius = 1;
            _defaultSettings.generateMetadata = true;
            _defaultSettings.saveGeneratedAssets = true;
            _defaultSettings.saveThumbnails = true;
        }
        
        private void LoadCurrentSettings() {
            // First try to load from TraversifyManager
            var manager = TraversifyManager.Instance;
            
            if (manager != null) {
                _currentSettings.terrainWidth = manager.terrainSize.x;
                _currentSettings.terrainLength = manager.terrainSize.z;
                _currentSettings.terrainHeight = manager.terrainSize.y;
                _currentSettings.terrainResolution = manager.terrainResolution;
                _currentSettings.generateWater = manager.generateWater;
                _currentSettings.waterHeight = manager.waterHeight;
                
                _currentSettings.openAIKey = manager.openAIApiKey;
                _currentSettings.useHighQualityAnalysis = manager.useHighQualityAnalysis;
                _currentSettings.confidenceThreshold = manager.detectionThreshold;
                _currentSettings.useFasterRCNN = manager.useFasterRCNN;
                _currentSettings.useSAM = manager.useSAM;
                _currentSettings.maxObjectsToProcess = manager.maxObjectsToProcess;
                _currentSettings.groupSimilarObjects = manager.groupSimilarObjects;
                _currentSettings.similarityThreshold = manager.instancingSimilarity;
                
                _currentSettings.maxConcurrentRequests = manager.maxConcurrentAPIRequests;
                _currentSettings.processingBatchSize = manager.processingBatchSize;
                _currentSettings.processingTimeout = manager.processingTimeout;
                _currentSettings.apiRateLimit = manager.apiRateLimitDelay;
                _currentSettings.useGPUAcceleration = manager.useGPUAcceleration;
                
                _currentSettings.showDebugInfo = manager.enableDebugVisualization;
                _currentSettings.saveGeneratedAssets = manager.saveGeneratedAssets;
                _currentSettings.generateMetadata = manager.generateMetadata;
                _currentSettings.outputPath = manager.assetSavePath;
                
                Log("Settings loaded from TraversifyManager");
            } else {
                // Try to load from disk
                LoadSettingsFromDisk();
            }
            
            // Update UI
            UpdateUIFromSettings();
        }
        
        private void UpdateUIFromSettings() {
            // Prevent triggering change callbacks while updating UI
            _ignoreValueChanges = true;
            
            try {
                // General settings
                if (_projectNameInput != null) _projectNameInput.text = _currentSettings.projectName;
                if (_autoSaveToggle != null) _autoSaveToggle.isOn = _currentSettings.autoSave;
                if (_autoSaveIntervalSlider != null) {
                    _autoSaveIntervalSlider.value = _currentSettings.autoSaveInterval;
                    _autoSaveIntervalSlider.interactable = _currentSettings.autoSave;
                }
                if (_autoSaveIntervalText != null) _autoSaveIntervalText.text = $"{_currentSettings.autoSaveInterval:F0}m";
                if (_showDebugInfoToggle != null) _showDebugInfoToggle.isOn = _currentSettings.showDebugInfo;
                if (_useGPUAccelerationToggle != null) _useGPUAccelerationToggle.isOn = _currentSettings.useGPUAcceleration;
                if (_outputPathInput != null) _outputPathInput.text = _currentSettings.outputPath;
                
                // Terrain settings
                if (_terrainWidthInput != null) _terrainWidthInput.text = _currentSettings.terrainWidth.ToString("F0");
                if (_terrainLengthInput != null) _terrainLengthInput.text = _currentSettings.terrainLength.ToString("F0");
                if (_terrainHeightSlider != null) _terrainHeightSlider.value = _currentSettings.terrainHeight;
                if (_terrainHeightText != null) _terrainHeightText.text = $"{_currentSettings.terrainHeight:F0}m";
                if (_terrainResolutionSlider != null) _terrainResolutionSlider.value = _currentSettings.terrainResolution;
                if (_terrainResolutionText != null) _terrainResolutionText.text = _currentSettings.terrainResolution.ToString();
                if (_generateWaterToggle != null) _generateWaterToggle.isOn = _currentSettings.generateWater;
                if (_waterHeightSlider != null) _waterHeightSlider.value = _currentSettings.waterHeight;
                if (_waterHeightText != null) _waterHeightText.text = $"{_currentSettings.waterHeight:P0}";
                if (_applyErosionToggle != null) _applyErosionToggle.isOn = _currentSettings.applyErosion;
                if (_erosionIterationsInput != null) _erosionIterationsInput.text = _currentSettings.erosionIterations.ToString();
                if (_buildNavMeshToggle != null) _buildNavMeshToggle.isOn = _currentSettings.buildNavMesh;
                
                // AI settings
                if (_openAIKeyInput != null) _openAIKeyInput.text = _currentSettings.openAIKey;
                if (_useHighQualityAnalysisToggle != null) _useHighQualityAnalysisToggle.isOn = _currentSettings.useHighQualityAnalysis;
                if (_confidenceThresholdSlider != null) _confidenceThresholdSlider.value = _currentSettings.confidenceThreshold;
                if (_confidenceThresholdText != null) _confidenceThresholdText.text = $"{_currentSettings.confidenceThreshold:P0}";
                if (_useFasterRCNNToggle != null) _useFasterRCNNToggle.isOn = _currentSettings.useFasterRCNN;
                if (_useSAMToggle != null) _useSAMToggle.isOn = _currentSettings.useSAM;
                if (_maxObjectsInput != null) _maxObjectsInput.text = _currentSettings.maxObjectsToProcess.ToString();
                if (_groupSimilarObjectsToggle != null) _groupSimilarObjectsToggle.isOn = _currentSettings.groupSimilarObjects;
                if (_similarityThresholdSlider != null) _similarityThresholdSlider.value = _currentSettings.similarityThreshold;
                if (_similarityThresholdText != null) _similarityThresholdText.text = $"{_currentSettings.similarityThreshold:P0}";
                
                // Performance settings
                if (_maxConcurrentRequestsSlider != null) _maxConcurrentRequestsSlider.value = _currentSettings.maxConcurrentRequests;
                if (_maxConcurrentRequestsText != null) _maxConcurrentRequestsText.text = _currentSettings.maxConcurrentRequests.ToString();
                if (_processingBatchSizeSlider != null) _processingBatchSizeSlider.value = _currentSettings.processingBatchSize;
                if (_processingBatchSizeText != null) _processingBatchSizeText.text = _currentSettings.processingBatchSize.ToString();
                if (_processingTimeoutSlider != null) _processingTimeoutSlider.value = _currentSettings.processingTimeout;
                if (_processingTimeoutText != null) _processingTimeoutText.text = $"{_currentSettings.processingTimeout:F0}s";
                if (_apiRateLimitSlider != null) _apiRateLimitSlider.value = _currentSettings.apiRateLimit;
                if (_apiRateLimitText != null) _apiRateLimitText.text = $"{_currentSettings.apiRateLimit:F1}s";
                
                // Advanced settings
                if (_enableAdvancedFeaturesToggle != null) _enableAdvancedFeaturesToggle.isOn = _currentSettings.enableAdvancedFeatures;
                if (_enableChunkStreamingToggle != null) _enableChunkStreamingToggle.isOn = _currentSettings.enableChunkStreaming;
                if (_chunkSizeInput != null) _chunkSizeInput.text = _currentSettings.chunkSize.ToString();
                if (_loadRadiusInput != null) _loadRadiusInput.text = _currentSettings.loadRadius.ToString();
                if (_generateMetadataToggle != null) _generateMetadataToggle.isOn = _currentSettings.generateMetadata;
                if (_saveGeneratedAssetsToggle != null) _saveGeneratedAssetsToggle.isOn = _currentSettings.saveGeneratedAssets;
                if (_saveThumbnailsToggle != null) _saveThumbnailsToggle.isOn = _currentSettings.saveThumbnails;
                
                // Reset preset dropdown to "Custom"
                if (_presetDropdown != null && _presetDropdown.options.Count > 0) {
                    _presetDropdown.value = 0; // Assuming "Custom" is the first option
                }
            }
            finally {
                _ignoreValueChanges = false;
            }
        }
        
        private void UpdateSettingsFromUI() {
            // General settings
            if (_projectNameInput != null) _currentSettings.projectName = _projectNameInput.text;
            if (_autoSaveToggle != null) _currentSettings.autoSave = _autoSaveToggle.isOn;
            if (_autoSaveIntervalSlider != null) _currentSettings.autoSaveInterval = _autoSaveIntervalSlider.value;
            if (_showDebugInfoToggle != null) _currentSettings.showDebugInfo = _showDebugInfoToggle.isOn;
            if (_useGPUAccelerationToggle != null) _currentSettings.useGPUAcceleration = _useGPUAccelerationToggle.isOn;
            if (_outputPathInput != null) _currentSettings.outputPath = _outputPathInput.text;
            
            // Terrain settings
            if (_terrainWidthInput != null && float.TryParse(_terrainWidthInput.text, out float width)) {
                _currentSettings.terrainWidth = width;
            }
            if (_terrainLengthInput != null && float.TryParse(_terrainLengthInput.text, out float length)) {
                _currentSettings.terrainLength = length;
            }
            if (_terrainHeightSlider != null) _currentSettings.terrainHeight = _terrainHeightSlider.value;
            if (_terrainResolutionSlider != null) _currentSettings.terrainResolution = ConvertToValidResolution(_terrainResolutionSlider.value);
            if (_generateWaterToggle != null) _currentSettings.generateWater = _generateWaterToggle.isOn;
            if (_waterHeightSlider != null) _currentSettings.waterHeight = _waterHeightSlider.value;
            if (_applyErosionToggle != null) _currentSettings.applyErosion = _applyErosionToggle.isOn;
            if (_erosionIterationsInput != null && int.TryParse(_erosionIterationsInput.text, out int iterations)) {
                _currentSettings.erosionIterations = iterations;
            }
            if (_buildNavMeshToggle != null) _currentSettings.buildNavMesh = _buildNavMeshToggle.isOn;
            
            // AI settings
            if (_openAIKeyInput != null) _currentSettings.openAIKey = _openAIKeyInput.text;
            if (_useHighQualityAnalysisToggle != null) _currentSettings.useHighQualityAnalysis = _useHighQualityAnalysisToggle.isOn;
            if (_confidenceThresholdSlider != null) _currentSettings.confidenceThreshold = _confidenceThresholdSlider.value;
            if (_useFasterRCNNToggle != null) _currentSettings.useFasterRCNN = _useFasterRCNNToggle.isOn;
            if (_useSAMToggle != null) _currentSettings.useSAM = _useSAMToggle.isOn;
            if (_maxObjectsInput != null && int.TryParse(_maxObjectsInput.text, out int maxObjects)) {
                _currentSettings.maxObjectsToProcess = maxObjects;
            }
            if (_groupSimilarObjectsToggle != null) _currentSettings.groupSimilarObjects = _groupSimilarObjectsToggle.isOn;
            if (_similarityThresholdSlider != null) _currentSettings.similarityThreshold = _similarityThresholdSlider.value;
            
            // Performance settings
            if (_maxConcurrentRequestsSlider != null) _currentSettings.maxConcurrentRequests = Mathf.RoundToInt(_maxConcurrentRequestsSlider.value);
            if (_processingBatchSizeSlider != null) _currentSettings.processingBatchSize = Mathf.RoundToInt(_processingBatchSizeSlider.value);
            if (_processingTimeoutSlider != null) _currentSettings.processingTimeout = _processingTimeoutSlider.value;
            if (_apiRateLimitSlider != null) _currentSettings.apiRateLimit = _apiRateLimitSlider.value;
            
            // Advanced settings
            if (_enableAdvancedFeaturesToggle != null) _currentSettings.enableAdvancedFeatures = _enableAdvancedFeaturesToggle.isOn;
            if (_enableChunkStreamingToggle != null) _currentSettings.enableChunkStreaming = _enableChunkStreamingToggle.isOn;
            if (_chunkSizeInput != null && int.TryParse(_chunkSizeInput.text, out int chunkSize)) {
                _currentSettings.chunkSize = chunkSize;
            }
            if (_loadRadiusInput != null && int.TryParse(_loadRadiusInput.text, out int loadRadius)) {
                _currentSettings.loadRadius = loadRadius;
            }
            if (_generateMetadataToggle != null) _currentSettings.generateMetadata = _generateMetadataToggle.isOn;
            if (_saveGeneratedAssetsToggle != null) _currentSettings.saveGeneratedAssets = _saveGeneratedAssetsToggle.isOn;
            if (_saveThumbnailsToggle != null) _currentSettings.saveThumbnails = _saveThumbnailsToggle.isOn;
        }
        
        private void LoadSettingsFromDisk() {
            string path = GetSettingsFilePath();
            
            if (File.Exists(path)) {
                try {
                    string json = File.ReadAllText(path);
                    _currentSettings = JsonUtility.FromJson<SettingsData>(json);
                    Log($"Settings loaded from {path}");
                }
                catch (Exception ex) {
                    LogError($"Error loading settings: {ex.Message}");
                    // Use default settings if load fails
                    _currentSettings = new SettingsData(_defaultSettings);
                }
            }
            else {
                // Use default settings if file doesn't exist
                _currentSettings = new SettingsData(_defaultSettings);
                Log("Using default settings");
            }
        }
        
        private void SaveSettingsToDisk() {
            string path = GetSettingsFilePath();
            string directory = Path.GetDirectoryName(path);
            
            try {
                if (!Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }
                
                string json = JsonUtility.ToJson(_currentSettings, true);
                File.WriteAllText(path, json);
                Log($"Settings saved to {path}");
            }
            catch (Exception ex) {
                LogError($"Error saving settings: {ex.Message}");
            }
        }
        
        private void LoadPresets() {
            string path = GetPresetsFilePath();
            
            if (File.Exists(path)) {
                try {
                    string json = File.ReadAllText(path);
                    PresetsContainer container = JsonUtility.FromJson<PresetsContainer>(json);
                    
                    if (container != null && container.presets != null) {
                        _presetSettings.Clear();
                        
                        foreach (var preset in container.presets) {
                            _presetSettings[preset.name] = preset.settings;
                        }
                        
                        Log($"Loaded {_presetSettings.Count} presets");
                    }
                }
                catch (Exception ex) {
                    LogError($"Error loading presets: {ex.Message}");
                }
            }
            
            UpdatePresetDropdown();
        }
        
        private void SavePresetsToDisk() {
            string path = GetPresetsFilePath();
            string directory = Path.GetDirectoryName(path);
            
            try {
                if (!Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }
                
                PresetsContainer container = new PresetsContainer();
                container.presets = new List<PresetEntry>();
                
                foreach (var kvp in _presetSettings) {
                    container.presets.Add(new PresetEntry {
                        name = kvp.Key,
                        settings = kvp.Value
                    });
                }
                
                string json = JsonUtility.ToJson(container, true);
                File.WriteAllText(path, json);
                Log($"Presets saved to {path}");
            }
            catch (Exception ex) {
                LogError($"Error saving presets: {ex.Message}");
            }
        }
        
        private void UpdatePresetDropdown() {
            if (_presetDropdown == null) return;
            
            // Store current selection
            int currentIndex = _presetDropdown.value;
            
            // Clear options
            _presetDropdown.ClearOptions();
            
            // Add "Custom" option first
            List<string> options = new List<string> { "Custom" };
            
            // Add preset names
            options.AddRange(_presetSettings.Keys);
            
            // Update dropdown
            _presetDropdown.AddOptions(options);
            
            // Restore selection if possible
            if (currentIndex < options.Count) {
                _presetDropdown.value = currentIndex;
            }
            else {
                _presetDropdown.value = 0; // Default to "Custom"
            }
        }
        
        private void ApplyQualityPreset(QualityPreset preset) {
            switch (preset) {
                case QualityPreset.Low:
                    _currentSettings.terrainResolution = 129;
                    _currentSettings.useHighQualityAnalysis = false;
                    _currentSettings.useFasterRCNN = false;
                    _currentSettings.useSAM = false;
                    _currentSettings.maxObjectsToProcess = 50;
                    _currentSettings.maxConcurrentRequests = 2;
                    _currentSettings.processingBatchSize = 2;
                    _currentSettings.applyErosion = false;
                    _currentSettings.buildNavMesh = false;
                    _currentSettings.enableChunkStreaming = false;
                    break;
                    
                case QualityPreset.Medium:
                    _currentSettings.terrainResolution = 257;
                    _currentSettings.useHighQualityAnalysis = false;
                    _currentSettings.useFasterRCNN = true;
                    _currentSettings.useSAM = false;
                    _currentSettings.maxObjectsToProcess = 100;
                    _currentSettings.maxConcurrentRequests = 3;
                    _currentSettings.processingBatchSize = 5;
                    _currentSettings.applyErosion = true;
                    _currentSettings.erosionIterations = 500;
                    _currentSettings.buildNavMesh = true;
                    _currentSettings.enableChunkStreaming = false;
                    break;
                    
                case QualityPreset.High:
                    _currentSettings.terrainResolution = 513;
                    _currentSettings.useHighQualityAnalysis = true;
                    _currentSettings.useFasterRCNN = true;
                    _currentSettings.useSAM = true;
                    _currentSettings.maxObjectsToProcess = 200;
                    _currentSettings.maxConcurrentRequests = 4;
                    _currentSettings.processingBatchSize = 8;
                    _currentSettings.applyErosion = true;
                    _currentSettings.erosionIterations = 1000;
                    _currentSettings.buildNavMesh = true;
                    _currentSettings.enableChunkStreaming = false;
                    break;
                    
                case QualityPreset.Ultra:
                    _currentSettings.terrainResolution = 1025;
                    _currentSettings.useHighQualityAnalysis = true;
                    _currentSettings.useFasterRCNN = true;
                    _currentSettings.useSAM = true;
                    _currentSettings.maxObjectsToProcess = 500;
                    _currentSettings.maxConcurrentRequests = 5;
                    _currentSettings.processingBatchSize = 10;
                    _currentSettings.applyErosion = true;
                    _currentSettings.erosionIterations = 2000;
                    _currentSettings.buildNavMesh = true;
                    _currentSettings.enableChunkStreaming = true;
                    break;
            }
            
            UpdateUIFromSettings();
            Log($"Applied {preset} quality preset");
        }
        
        private string GetSettingsFilePath() {
            return Path.Combine(Application.persistentDataPath, "Traversify", "settings.json");
        }
        
        private string GetPresetsFilePath() {
            return Path.Combine(Application.persistentDataPath, "Traversify", "presets.json");
        }
        
        private int ConvertToValidResolution(float value) {
            // Unity terrain resolutions must be 2^n + 1
            int[] validResolutions = { 33, 65, 129, 257, 513, 1025, 2049, 4097 };
            
            // Find closest valid resolution
            int closest = validResolutions[0];
            float minDiff = Mathf.Abs(value - closest);
            
            foreach (int res in validResolutions) {
                float diff = Mathf.Abs(value - res);
                if (diff < minDiff) {
                    minDiff = diff;
                    closest = res;
                }
            }
            
            return closest;
        }
        
        private void Log(string message) {
            if (_debugger != null && _logSettingsChanges) {
                _debugger.Log(message, LogCategory.UI);
            }
            else {
                Debug.Log($"[SettingsPanel] {message}");
            }
        }
        
        private void LogWarning(string message) {
            if (_debugger != null) {
                _debugger.LogWarning(message, LogCategory.UI);
            }
            else {
                Debug.LogWarning($"[SettingsPanel] {message}");
            }
        }
        
        private void LogError(string message) {
            if (_debugger != null) {
                _debugger.LogError(message, LogCategory.UI);
            }
            else {
                Debug.LogError($"[SettingsPanel] {message}");
            }
        }
        
        #endregion
        
        #region Validation Methods
        
        // Delegate for input field validation
        private delegate void ValidatorDelegate(TMP_InputField inputField, string value);
        
        private void ValidateIntegerInput(TMP_InputField inputField, string value) {
            if (string.IsNullOrEmpty(value)) {
                // Empty input, don't validate
                return;
            }
            
            if (!int.TryParse(value, out int result)) {
                // Invalid input, revert to previous value
                inputField.text = "0";
            }
        }
        
        #endregion
        
        #region Nested Types
        
        /// <summary>
        /// Contains all settings data.
        /// </summary>
        [Serializable]
        public class SettingsData {
            // General settings
            public string projectName = "New Project";
            public bool autoSave = true;
            public float autoSaveInterval = 5f;
            public bool showDebugInfo = false;
            public bool useGPUAcceleration = true;
            public string outputPath = "Assets/GeneratedTerrains";
            
            // Terrain settings
            public float terrainWidth = 500f;
            public float terrainLength = 500f;
            public float terrainHeight = 100f;
            public int terrainResolution = 513;
            public bool generateWater = true;
            public float waterHeight = 0.3f;
            public bool applyErosion = true;
            public int erosionIterations = 1000;
            public bool buildNavMesh = true;
            
            // AI settings
            public string openAIKey = "";
            public bool useHighQualityAnalysis = true;
            public float confidenceThreshold = 0.5f;
            public bool useFasterRCNN = true;
            public bool useSAM = true;
            public int maxObjectsToProcess = 100;
            public bool groupSimilarObjects = true;
            public float similarityThreshold = 0.8f;
            
            // Performance settings
            public int maxConcurrentRequests = 3;
            public int processingBatchSize = 5;
            public float processingTimeout = 300f;
            public float apiRateLimit = 0.5f;
            
            // Advanced settings
            public bool enableAdvancedFeatures = false;
            public bool enableChunkStreaming = false;
            public int chunkSize = 500;
            public int loadRadius = 1;
            public bool generateMetadata = true;
            public bool saveGeneratedAssets = true;
            public bool saveThumbnails = true;
            
            // Default constructor
            public SettingsData() { }
            
            // Copy constructor
            public SettingsData(SettingsData other) {
                if (other == null) return;
                
                projectName = other.projectName;
                autoSave = other.autoSave;
                autoSaveInterval = other.autoSaveInterval;
                showDebugInfo = other.showDebugInfo;
                useGPUAcceleration = other.useGPUAcceleration;
                outputPath = other.outputPath;
                
                terrainWidth = other.terrainWidth;
                terrainLength = other.terrainLength;
                terrainHeight = other.terrainHeight;
                terrainResolution = other.terrainResolution;
                generateWater = other.generateWater;
                waterHeight = other.waterHeight;
                applyErosion = other.applyErosion;
                erosionIterations = other.erosionIterations;
                buildNavMesh = other.buildNavMesh;
                
                openAIKey = other.openAIKey;
                useHighQualityAnalysis = other.useHighQualityAnalysis;
                confidenceThreshold = other.confidenceThreshold;
                useFasterRCNN = other.useFasterRCNN;
                useSAM = other.useSAM;
                maxObjectsToProcess = other.maxObjectsToProcess;
                groupSimilarObjects = other.groupSimilarObjects;
                similarityThreshold = other.similarityThreshold;
                
                maxConcurrentRequests = other.maxConcurrentRequests;
                processingBatchSize = other.processingBatchSize;
                processingTimeout = other.processingTimeout;
                apiRateLimit = other.apiRateLimit;
                
                enableAdvancedFeatures = other.enableAdvancedFeatures;
                enableChunkStreaming = other.enableChunkStreaming;
                chunkSize = other.chunkSize;
                loadRadius = other.loadRadius;
                generateMetadata = other.generateMetadata;
                saveGeneratedAssets = other.saveGeneratedAssets;
                saveThumbnails = other.saveThumbnails;
            }
        }
        
        /// <summary>
        /// Container for serializing presets.
        /// </summary>
        [Serializable]
        private class PresetsContainer {
            public List<PresetEntry> presets = new List<PresetEntry>();
        }
        
        /// <summary>
        /// Entry for a single preset.
        /// </summary>
        [Serializable]
        private class PresetEntry {
            public string name;
            public SettingsData settings;
        }
        
        /// <summary>
        /// Quality presets for quick settings.
        /// </summary>
        private enum QualityPreset {
            Low,
            Medium,
            High,
            Ultra
        }
        
        #endregion
    }
}