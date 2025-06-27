/*************************************************************************
 *  Traversify – EnvironmentManager.cs                                   *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:48:19 UTC                                     *
 *  Desc   : Manages environmental features in generated environments    *
 *           including lighting, water, atmosphere, weather, and time    *
 *           of day. Provides a unified interface for configuring the    *
 *           visual and physical properties of the environment.          *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if URP_PRESENT
using UnityEngine.Rendering.Universal;
#endif
using Traversify.Core;

namespace Traversify {
    /// <summary>
    /// Manages environmental features in generated environments including lighting,
    /// water, atmosphere, weather, and time of day.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TraversifyDebugger))]
    public class EnvironmentManager : TraversifyComponent {
        #region Singleton Pattern
        
        private static EnvironmentManager _instance;
        
        /// <summary>
        /// Singleton instance of the EnvironmentManager.
        /// </summary>
        public static EnvironmentManager Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<EnvironmentManager>();
                    if (_instance == null) {
                        GameObject go = new GameObject("EnvironmentManager");
                        _instance = go.AddComponent<EnvironmentManager>();
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
        
        [Header("Lighting Settings")]
        [Tooltip("Main directional light for the scene")]
        [SerializeField] private Light _directionalLight;
        
        [Tooltip("Default color temperature for the sun")]
        [Range(1500f, 15000f)]
        [SerializeField] private float _defaultColorTemperature = 6500f;
        
        [Tooltip("Ambient light intensity")]
        [Range(0f, 2f)]
        [SerializeField] private float _ambientIntensity = 1f;
        
        [Tooltip("Default sun intensity")]
        [Range(0f, 8f)]
        [SerializeField] private float _sunIntensity = 1f;
        
        [Tooltip("Default sun rotation (pitch)")]
        [Range(0f, 90f)]
        [SerializeField] private float _sunPitch = 45f;
        
        [Tooltip("Default sun rotation (yaw)")]
        [Range(0f, 360f)]
        [SerializeField] private float _sunYaw = 45f;
        
        [Tooltip("Enable shadows")]
        [SerializeField] private bool _enableShadows = true;
        
        [Tooltip("Shadow resolution")]
        [SerializeField] private LightShadowResolution _shadowResolution = LightShadowResolution.Medium;
        
        [Tooltip("Shadow distance")]
        [Range(0f, 1000f)]
        [SerializeField] private float _shadowDistance = 150f;
        
        [Header("Atmosphere Settings")]
        [Tooltip("Enable atmospheric fog")]
        [SerializeField] private bool _enableFog = true;
        
        [Tooltip("Fog color")]
        [SerializeField] private Color _fogColor = new Color(0.76f, 0.85f, 0.95f, 1f);
        
        [Tooltip("Fog density")]
        [Range(0f, 0.1f)]
        [SerializeField] private float _fogDensity = 0.01f;
        
        [Tooltip("Fog start distance")]
        [Range(0f, 1000f)]
        [SerializeField] private float _fogStartDistance = 10f;
        
        [Tooltip("Fog end distance")]
        [Range(0f, 3000f)]
        [SerializeField] private float _fogEndDistance = 1000f;
        
        [Tooltip("Skybox material")]
        [SerializeField] private Material _skyboxMaterial;
        
        [Tooltip("Skybox exposure")]
        [Range(0f, 8f)]
        [SerializeField] private float _skyboxExposure = 1f;
        
        [Header("Water Settings")]
        [Tooltip("Water prefab")]
        [SerializeField] private GameObject _waterPrefab;
        
        [Tooltip("Water material")]
        [SerializeField] private Material _waterMaterial;
        
        [Tooltip("Water height as fraction of terrain height")]
        [Range(0f, 1f)]
        [SerializeField] private float _waterHeight = 0.3f;
        
        [Tooltip("Water tint color")]
        [SerializeField] private Color _waterColor = new Color(0.15f, 0.35f, 0.68f, 0.8f);
        
        [Tooltip("Water wave height")]
        [Range(0f, 2f)]
        [SerializeField] private float _waveHeight = 0.5f;
        
        [Tooltip("Water wave speed")]
        [Range(0f, 2f)]
        [SerializeField] private float _waveSpeed = 0.5f;
        
        [Header("Weather Settings")]
        [Tooltip("Current weather condition")]
        [SerializeField] private WeatherCondition _weatherCondition = WeatherCondition.Clear;
        
        [Tooltip("Weather transition time in seconds")]
        [Range(0f, 60f)]
        [SerializeField] private float _weatherTransitionTime = 5f;
        
        [Tooltip("Rain particle system")]
        [SerializeField] private ParticleSystem _rainParticleSystem;
        
        [Tooltip("Snow particle system")]
        [SerializeField] private ParticleSystem _snowParticleSystem;
        
        [Tooltip("Cloud particle system")]
        [SerializeField] private ParticleSystem _cloudParticleSystem;
        
        [Header("Time Settings")]
        [Tooltip("Enable time of day cycle")]
        [SerializeField] private bool _enableTimeOfDayCycle = false;
        
        [Tooltip("Day length in minutes")]
        [Range(1f, 1440f)]
        [SerializeField] private float _dayLengthMinutes = 24f;
        
        [Tooltip("Starting time of day (0-24)")]
        [Range(0f, 24f)]
        [SerializeField] private float _startingTimeOfDay = 12f;
        
        [Tooltip("Time scale multiplier")]
        [Range(0.01f, 100f)]
        [SerializeField] private float _timeScale = 1f;
        
        [Header("Post-Processing")]
        [Tooltip("Enable post-processing effects")]
        [SerializeField] private bool _enablePostProcessing = true;
        
        [Tooltip("Post-processing profile")]
        [SerializeField] private UnityEngine.Object _postProcessingProfile;
        
        [Tooltip("Exposure compensation")]
        [Range(-3f, 3f)]
        [SerializeField] private float _exposureCompensation = 0f;
        
        [Header("Environment Presets")]
        [Tooltip("Available environment presets")]
        [SerializeField] private List<EnvironmentPreset> _environmentPresets = new List<EnvironmentPreset>();
        
        #endregion
        
        #region Private Fields
        
        private GameObject _waterPlane;
        private float _currentTimeOfDay;
        private WeatherCondition _targetWeatherCondition;
        private float _weatherTransitionProgress;
        private Coroutine _weatherTransitionCoroutine;
        private Coroutine _timeOfDayCycleCoroutine;
        private EnvironmentPreset _activePreset;
        private Dictionary<string, object> _cachedProperties = new Dictionary<string, object>();
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Current time of day (0-24 hour format).
        /// </summary>
        public float TimeOfDay {
            get => _currentTimeOfDay;
            set {
                _currentTimeOfDay = Mathf.Repeat(value, 24f);
                if (!_enableTimeOfDayCycle) {
                    UpdateLightingForTime(_currentTimeOfDay);
                }
            }
        }
        
        /// <summary>
        /// Current weather condition.
        /// </summary>
        public WeatherCondition Weather {
            get => _weatherCondition;
            set {
                if (_weatherCondition != value) {
                    _targetWeatherCondition = value;
                    if (_weatherTransitionCoroutine != null) {
                        StopCoroutine(_weatherTransitionCoroutine);
                    }
                    _weatherTransitionCoroutine = StartCoroutine(TransitionWeather(_weatherCondition, _targetWeatherCondition, _weatherTransitionTime));
                }
            }
        }
        
        /// <summary>
        /// Current water height in world units.
        /// </summary>
        public float WaterHeight {
            get => _waterHeight;
            set {
                _waterHeight = Mathf.Clamp01(value);
                UpdateWaterPlane();
            }
        }
        
        /// <summary>
        /// Enable or disable fog.
        /// </summary>
        public bool FogEnabled {
            get => _enableFog;
            set {
                _enableFog = value;
                RenderSettings.fog = _enableFog;
            }
        }
        
        /// <summary>
        /// Enable or disable time of day cycle.
        /// </summary>
        public bool TimeOfDayCycleEnabled {
            get => _enableTimeOfDayCycle;
            set {
                _enableTimeOfDayCycle = value;
                if (_enableTimeOfDayCycle) {
                    if (_timeOfDayCycleCoroutine == null) {
                        _timeOfDayCycleCoroutine = StartCoroutine(TimeOfDayCycle());
                    }
                } else {
                    if (_timeOfDayCycleCoroutine != null) {
                        StopCoroutine(_timeOfDayCycleCoroutine);
                        _timeOfDayCycleCoroutine = null;
                    }
                }
            }
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake() {
            // Singleton enforcement
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize debugger
            _debugger = GetComponent<TraversifyDebugger>();
            if (_debugger == null) {
                _debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            _debugger.Log("EnvironmentManager initializing...", LogCategory.System);
            
            // Initialize internal state
            _currentTimeOfDay = _startingTimeOfDay;
            _targetWeatherCondition = _weatherCondition;
            _weatherTransitionProgress = 1f;
            
            // Create directional light if not assigned
            if (_directionalLight == null) {
                GameObject lightGO = new GameObject("DirectionalLight");
                lightGO.transform.SetParent(transform);
                _directionalLight = lightGO.AddComponent<Light>();
                _directionalLight.type = LightType.Directional;
                _directionalLight.shadows = _enableShadows ? LightShadows.Soft : LightShadows.None;
                _directionalLight.shadowResolution = _shadowResolution;
                _directionalLight.intensity = _sunIntensity;
                _directionalLight.color = Color.white;
                
                // Set initial rotation
                lightGO.transform.rotation = Quaternion.Euler(_sunPitch, _sunYaw, 0f);
            }
        }
        
        private void Start() {
            InitializeEnvironment();
        }
        
        private void OnDestroy() {
            if (_timeOfDayCycleCoroutine != null) {
                StopCoroutine(_timeOfDayCycleCoroutine);
            }
            
            if (_weatherTransitionCoroutine != null) {
                StopCoroutine(_weatherTransitionCoroutine);
            }
            
            CleanupWaterPlane();
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Initializes the environment with default settings.
        /// </summary>
        public void InitializeEnvironment() {
            // Configure lighting
            ConfigureLighting();
            
            // Configure atmosphere
            ConfigureAtmosphere();
            
            // Configure water
            ConfigureWater();
            
            // Configure post-processing
            ConfigurePostProcessing();
            
            // Start time of day cycle if enabled
            if (_enableTimeOfDayCycle) {
                _timeOfDayCycleCoroutine = StartCoroutine(TimeOfDayCycle());
            } else {
                UpdateLightingForTime(_currentTimeOfDay);
            }
            
            // Set initial weather
            SetWeatherCondition(_weatherCondition, 0f);
            
            _debugger.Log("Environment initialized with default settings", LogCategory.System);
        }
        
        /// <summary>
        /// Applies an environment preset to quickly configure all settings.
        /// </summary>
        /// <param name="presetName">Name of the preset to apply</param>
        /// <param name="transitionTime">Time in seconds to transition to the new settings (0 for instant)</param>
        /// <returns>True if the preset was found and applied</returns>
        public bool ApplyEnvironmentPreset(string presetName, float transitionTime = 0f) {
            EnvironmentPreset preset = _environmentPresets.Find(p => p.name == presetName);
            if (preset == null) {
                _debugger.LogWarning($"Environment preset '{presetName}' not found", LogCategory.System);
                return false;
            }
            
            return ApplyEnvironmentPreset(preset, transitionTime);
        }
        
        /// <summary>
        /// Applies an environment preset to quickly configure all settings.
        /// </summary>
        /// <param name="preset">The preset to apply</param>
        /// <param name="transitionTime">Time in seconds to transition to the new settings (0 for instant)</param>
        /// <returns>True if the preset was applied successfully</returns>
        public bool ApplyEnvironmentPreset(EnvironmentPreset preset, float transitionTime = 0f) {
            if (preset == null) {
                _debugger.LogWarning("Cannot apply null environment preset", LogCategory.System);
                return false;
            }
            
            _activePreset = preset;
            
            // Apply lighting settings
            _defaultColorTemperature = preset.colorTemperature;
            _ambientIntensity = preset.ambientIntensity;
            _sunIntensity = preset.sunIntensity;
            _sunPitch = preset.sunPitch;
            _sunYaw = preset.sunYaw;
            _enableShadows = preset.enableShadows;
            
            // Apply atmosphere settings
            _enableFog = preset.enableFog;
            _fogColor = preset.fogColor;
            _fogDensity = preset.fogDensity;
            _fogStartDistance = preset.fogStartDistance;
            _fogEndDistance = preset.fogEndDistance;
            if (preset.skyboxMaterial != null) {
                _skyboxMaterial = preset.skyboxMaterial;
            }
            _skyboxExposure = preset.skyboxExposure;
            
            // Apply water settings
            _waterHeight = preset.waterHeight;
            _waterColor = preset.waterColor;
            _waveHeight = preset.waveHeight;
            _waveSpeed = preset.waveSpeed;
            
            // Apply time settings
            _enableTimeOfDayCycle = preset.enableTimeOfDayCycle;
            _dayLengthMinutes = preset.dayLengthMinutes;
            _timeScale = preset.timeScale;
            
            // Apply post-processing settings
            _enablePostProcessing = preset.enablePostProcessing;
            if (preset.postProcessingProfile != null) {
                _postProcessingProfile = preset.postProcessingProfile;
            }
            _exposureCompensation = preset.exposureCompensation;
            
            // Apply weather
            if (transitionTime > 0f) {
                Weather = preset.weatherCondition;
            } else {
                _weatherCondition = preset.weatherCondition;
                _targetWeatherCondition = preset.weatherCondition;
                SetWeatherCondition(preset.weatherCondition, 0f);
            }
            
            // Apply all settings
            if (transitionTime > 0f) {
                StartCoroutine(TransitionEnvironment(transitionTime));
            } else {
                ConfigureLighting();
                ConfigureAtmosphere();
                ConfigureWater();
                ConfigurePostProcessing();
                
                // Update time of day cycle
                if (_enableTimeOfDayCycle) {
                    if (_timeOfDayCycleCoroutine == null) {
                        _timeOfDayCycleCoroutine = StartCoroutine(TimeOfDayCycle());
                    }
                } else {
                    if (_timeOfDayCycleCoroutine != null) {
                        StopCoroutine(_timeOfDayCycleCoroutine);
                        _timeOfDayCycleCoroutine = null;
                    }
                    UpdateLightingForTime(_currentTimeOfDay);
                }
            }
            
            _debugger.Log($"Applied environment preset '{preset.name}'", LogCategory.System);
            return true;
        }
        
        /// <summary>
        /// Creates or updates a water plane at the specified height.
        /// </summary>
        /// <param name="height">Water height as a fraction of terrain height (0-1)</param>
        /// <param name="terrainSize">Size of the terrain (for scaling the water plane)</param>
        /// <returns>The water plane GameObject</returns>
        public GameObject CreateWaterPlane(float height, Vector3 terrainSize) {
            _waterHeight = Mathf.Clamp01(height);
            
            // Cleanup existing water plane
            CleanupWaterPlane();
            
            // Create new water plane
            if (_waterPrefab != null) {
                _waterPlane = Instantiate(_waterPrefab, transform);
            } else {
                _waterPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _waterPlane.transform.SetParent(transform);
            }
            
            _waterPlane.name = "WaterPlane";
            
            // Scale to terrain size (Unity plane is 10x10 by default)
            float scaleX = terrainSize.x / 10f;
            float scaleZ = terrainSize.z / 10f;
            _waterPlane.transform.localScale = new Vector3(scaleX, 1, scaleZ);
            
            // Position at water height
            float waterY = _waterHeight * terrainSize.y;
            _waterPlane.transform.position = new Vector3(terrainSize.x / 2f, waterY, terrainSize.z / 2f);
            
            // Apply water material
            Renderer rend = _waterPlane.GetComponent<Renderer>();
            if (rend != null) {
                if (_waterMaterial != null) {
                    rend.material = new Material(_waterMaterial);
                } else {
                    Material waterMat = CreateWaterMaterial();
                    rend.material = waterMat;
                }
                
                // Apply water color
                rend.material.SetColor("_Color", _waterColor);
                rend.material.SetFloat("_WaveHeight", _waveHeight);
                rend.material.SetFloat("_WaveSpeed", _waveSpeed);
                
                // Disable shadow casting
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            
            // Add water animation component
            WaterAnimation waterAnim = _waterPlane.GetComponent<WaterAnimation>();
            if (waterAnim == null) {
                waterAnim = _waterPlane.AddComponent<WaterAnimation>();
            }
            waterAnim.waveHeight = _waveHeight;
            waterAnim.waveSpeed = _waveSpeed;
            
            _debugger.Log($"Created water plane at height {waterY:F1}", LogCategory.System);
            
            return _waterPlane;
        }
        
        /// <summary>
        /// Sets the weather condition.
        /// </summary>
        /// <param name="condition">Weather condition to set</param>
        /// <param name="transitionTime">Time in seconds to transition to the new weather (0 for instant)</param>
        public void SetWeatherCondition(WeatherCondition condition, float transitionTime = 5f) {
            _targetWeatherCondition = condition;
            
            if (transitionTime > 0f) {
                if (_weatherTransitionCoroutine != null) {
                    StopCoroutine(_weatherTransitionCoroutine);
                }
                _weatherTransitionCoroutine = StartCoroutine(TransitionWeather(_weatherCondition, _targetWeatherCondition, transitionTime));
            } else {
                _weatherCondition = condition;
                ApplyWeatherSettings(condition, 1f);
            }
            
            _debugger.Log($"Set weather to {condition}", LogCategory.System);
        }
        
        /// <summary>
        /// Updates the environment based on terrain analysis results.
        /// </summary>
        /// <param name="analysisResults">Analysis results from MapAnalyzer</param>
        public void UpdateEnvironmentFromAnalysis(AI.AnalysisResults analysisResults) {
            if (analysisResults == null) {
                _debugger.LogWarning("Cannot update environment: analysis results are null", LogCategory.System);
                return;
            }
            
            // Determine most suitable preset based on analysis
            string presetName = DeterminePresetFromAnalysis(analysisResults);
            if (!string.IsNullOrEmpty(presetName)) {
                ApplyEnvironmentPreset(presetName, 2f);
            }
            
            // Get terrain size from analysis if available
            Vector3 terrainSize = Vector3.one * 500f; // Default
            if (analysisResults.metadata.settings.ContainsKey("terrainSize")) {
                try {
                    terrainSize = (Vector3)analysisResults.metadata.settings["terrainSize"];
                } catch (Exception ex) {
                    _debugger.LogWarning($"Failed to get terrain size from analysis: {ex.Message}", LogCategory.System);
                }
            }
            
            // Create water plane based on analysis
            float waterHeight = 0.3f; // Default
            
            // Look for water features in terrain
            bool hasWater = analysisResults.terrainFeatures.Exists(tf => 
                tf.label.ToLowerInvariant().Contains("water") || 
                tf.label.ToLowerInvariant().Contains("river") || 
                tf.label.ToLowerInvariant().Contains("lake") || 
                tf.label.ToLowerInvariant().Contains("ocean"));
            
            if (hasWater) {
                // Get average water height from water features
                var waterFeatures = analysisResults.terrainFeatures.FindAll(tf => 
                    tf.label.ToLowerInvariant().Contains("water") || 
                    tf.label.ToLowerInvariant().Contains("river") || 
                    tf.label.ToLowerInvariant().Contains("lake") || 
                    tf.label.ToLowerInvariant().Contains("ocean"));
                
                if (waterFeatures.Count > 0) {
                    float avgHeight = 0f;
                    foreach (var wf in waterFeatures) {
                        avgHeight += wf.elevation;
                    }
                    avgHeight /= waterFeatures.Count;
                    
                    // Normalize to 0-1 range
                    waterHeight = avgHeight / terrainSize.y;
                    waterHeight = Mathf.Clamp01(waterHeight);
                }
                
                CreateWaterPlane(waterHeight, terrainSize);
            } else {
                CleanupWaterPlane();
            }
            
            _debugger.Log("Updated environment from analysis results", LogCategory.System);
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// Configures lighting settings.
        /// </summary>
        private void ConfigureLighting() {
            if (_directionalLight != null) {
                _directionalLight.shadows = _enableShadows ? LightShadows.Soft : LightShadows.None;
                _directionalLight.shadowResolution = _shadowResolution;
                _directionalLight.intensity = _sunIntensity;
                _directionalLight.useColorTemperature = true;
                _directionalLight.colorTemperature = _defaultColorTemperature;
                
                // Set rotation based on sun angles
                _directionalLight.transform.rotation = Quaternion.Euler(_sunPitch, _sunYaw, 0f);
            }
            
            // Configure ambient lighting
            RenderSettings.ambientIntensity = _ambientIntensity;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            
            // Configure shadow settings
            QualitySettings.shadowDistance = _shadowDistance;
            
            _debugger.Log("Configured lighting settings", LogCategory.System);
        }
        
        /// <summary>
        /// Configures atmosphere settings.
        /// </summary>
        private void ConfigureAtmosphere() {
            // Configure fog
            RenderSettings.fog = _enableFog;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = _fogDensity;
            RenderSettings.fogStartDistance = _fogStartDistance;
            RenderSettings.fogEndDistance = _fogEndDistance;
            
            // Configure skybox
            if (_skyboxMaterial != null) {
                RenderSettings.skybox = _skyboxMaterial;
                if (_skyboxMaterial.HasProperty("_Exposure")) {
                    _skyboxMaterial.SetFloat("_Exposure", _skyboxExposure);
                }
            }
            
            _debugger.Log("Configured atmosphere settings", LogCategory.System);
        }
        
        /// <summary>
        /// Configures water settings.
        /// </summary>
        private void ConfigureWater() {
            UpdateWaterPlane();
            _debugger.Log("Configured water settings", LogCategory.System);
        }
        
        /// <summary>
        /// Updates the water plane properties.
        /// </summary>
        private void UpdateWaterPlane() {
            if (_waterPlane != null) {
                Renderer rend = _waterPlane.GetComponent<Renderer>();
                if (rend != null && rend.material != null) {
                    rend.material.SetColor("_Color", _waterColor);
                    
                    // Update material properties if they exist
                    if (rend.material.HasProperty("_WaveHeight")) {
                        rend.material.SetFloat("_WaveHeight", _waveHeight);
                    }
                    
                    if (rend.material.HasProperty("_WaveSpeed")) {
                        rend.material.SetFloat("_WaveSpeed", _waveSpeed);
                    }
                }
                
                // Update animation component
                WaterAnimation waterAnim = _waterPlane.GetComponent<WaterAnimation>();
                if (waterAnim != null) {
                    waterAnim.waveHeight = _waveHeight;
                    waterAnim.waveSpeed = _waveSpeed;
                }
                
                // Update water height if terrain exists
                UnityEngine.Terrain terrain = FindObjectOfType<UnityEngine.Terrain>();
                if (terrain != null) {
                    float waterY = _waterHeight * terrain.terrainData.size.y;
                    _waterPlane.transform.position = new Vector3(
                        _waterPlane.transform.position.x,
                        waterY,
                        _waterPlane.transform.position.z
                    );
                }
            }
        }
        
        /// <summary>
        /// Configures post-processing settings.
        /// </summary>
        private void ConfigurePostProcessing() {
            #if URP_PRESENT
            var volume = FindObjectOfType<Volume>();
            if (volume != null) {
                volume.enabled = _enablePostProcessing;
                
                if (_postProcessingProfile != null && _postProcessingProfile is VolumeProfile profile) {
                    volume.profile = profile;
                    
                    // Configure exposure if available
                    if (profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var colorAdjustments)) {
                        colorAdjustments.postExposure.value = _exposureCompensation;
                    }
                }
            }
            #endif
            
            _debugger.Log("Configured post-processing settings", LogCategory.System);
        }
        
        /// <summary>
        /// Creates a water material if none is assigned.
        /// </summary>
        private Material CreateWaterMaterial() {
            Material mat = new Material(Shader.Find("Standard"));
            mat.name = "GeneratedWater";
            mat.color = _waterColor;
            mat.SetFloat("_Glossiness", 0.95f);
            mat.SetFloat("_Metallic", 0.1f);
            
            // Configure blending for transparency
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            
            return mat;
        }
        
        /// <summary>
        /// Cleans up the water plane.
        /// </summary>
        private void CleanupWaterPlane() {
            if (_waterPlane != null) {
                Destroy(_waterPlane);
                _waterPlane = null;
            }
        }
        
        /// <summary>
        /// Updates lighting based on time of day.
        /// </summary>
        private void UpdateLightingForTime(float timeOfDay) {
            if (_directionalLight == null) return;
            
            // Calculate sun rotation based on time
            float sunAngle = (timeOfDay / 24f) * 360f - 90f; // -90 to start with sunrise at 6 AM
            
            // Adjust pitch based on time (higher at noon, lower at sunrise/sunset)
            float pitchCurve = Mathf.Sin((timeOfDay / 24f) * Mathf.PI);
            float timeBasedPitch = pitchCurve * 85f;
            
            // Only show sun during day (6 AM to 6 PM)
            bool isDay = timeOfDay > 6f && timeOfDay < 18f;
            
            // Adjust color temperature based on time
            float tempCurve = Mathf.Sin((timeOfDay / 24f) * Mathf.PI);
            float colorTemp = Mathf.Lerp(2000f, 10000f, tempCurve);
            
            // Adjust intensity based on time
            float intensityCurve = Mathf.Sin((timeOfDay / 24f) * Mathf.PI);
            float timeBasedIntensity = intensityCurve * _sunIntensity;
            
            // Apply settings
            _directionalLight.transform.rotation = Quaternion.Euler(timeBasedPitch, sunAngle, 0f);
            _directionalLight.intensity = isDay ? timeBasedIntensity : 0.05f;
            _directionalLight.colorTemperature = colorTemp;
            
            // Adjust ambient lighting
            float ambientCurve = Mathf.Sin((timeOfDay / 24f) * Mathf.PI);
            RenderSettings.ambientIntensity = ambientCurve * _ambientIntensity;
            
            // Adjust fog based on time
            if (_enableFog) {
                // More fog at night and dawn/dusk
                float fogDensityCurve = 1f - Mathf.Sin((timeOfDay / 24f) * Mathf.PI);
                RenderSettings.fogDensity = Mathf.Lerp(_fogDensity * 0.5f, _fogDensity * 2f, fogDensityCurve);
                
                // Adjust fog color (bluer during day, darker at night)
                Color dayFogColor = _fogColor;
                Color nightFogColor = new Color(0.1f, 0.1f, 0.2f, 1f);
                RenderSettings.fogColor = Color.Lerp(nightFogColor, dayFogColor, ambientCurve);
            }
            
            // Adjust skybox exposure
            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure")) {
                RenderSettings.skybox.SetFloat("_Exposure", ambientCurve * _skyboxExposure);
            }
        }
        
        /// <summary>
        /// Time of day cycle coroutine.
        /// </summary>
        private IEnumerator TimeOfDayCycle() {
            while (_enableTimeOfDayCycle) {
                // Calculate time increment
                float timeIncrement = (24f / (_dayLengthMinutes * 60f)) * Time.deltaTime * _timeScale;
                
                // Update time of day
                _currentTimeOfDay = Mathf.Repeat(_currentTimeOfDay + timeIncrement, 24f);
                
                // Update lighting
                UpdateLightingForTime(_currentTimeOfDay);
                
                yield return null;
            }
        }
        
        /// <summary>
        /// Weather transition coroutine.
        /// </summary>
        private IEnumerator TransitionWeather(WeatherCondition fromCondition, WeatherCondition toCondition, float transitionTime) {
            float startTime = Time.time;
            float endTime = startTime + transitionTime;
            
            while (Time.time < endTime) {
                float t = (Time.time - startTime) / transitionTime;
                _weatherTransitionProgress = t;
                
                // Apply interpolated weather settings
                ApplyWeatherSettings(fromCondition, toCondition, t);
                
                yield return null;
            }
            
            // Final state
            _weatherCondition = toCondition;
            _weatherTransitionProgress = 1f;
            ApplyWeatherSettings(toCondition, 1f);
            
            _weatherTransitionCoroutine = null;
        }
        
        /// <summary>
        /// Applies weather settings.
        /// </summary>
        private void ApplyWeatherSettings(WeatherCondition condition, float intensity) {
            // Apply weather settings based on condition
            switch (condition) {
                case WeatherCondition.Clear:
                    // Clear weather - sunny, no precipitation
                    SetPrecipitation(false, false);
                    SetCloudDensity(0.1f * intensity);
                    if (_enableFog) {
                        RenderSettings.fogDensity = _fogDensity * 0.5f;
                    }
                    break;
                    
                case WeatherCondition.Cloudy:
                    // Cloudy weather - medium clouds, no precipitation
                    SetPrecipitation(false, false);
                    SetCloudDensity(0.6f * intensity);
                    if (_enableFog) {
                        RenderSettings.fogDensity = _fogDensity * 0.8f;
                    }
                    break;
                    
                case WeatherCondition.Overcast:
                    // Overcast weather - heavy clouds, no precipitation
                    SetPrecipitation(false, false);
                    SetCloudDensity(0.9f * intensity);
                    if (_enableFog) {
                        RenderSettings.fogDensity = _fogDensity * 1.2f;
                    }
                    break;
                    
                case WeatherCondition.Rain:
                    // Rainy weather - heavy clouds, rain
                    SetPrecipitation(true, false);
                    SetCloudDensity(0.9f * intensity);
                    if (_enableFog) {
                        RenderSettings.fogDensity = _fogDensity * 1.5f;
                    }
                    break;
                    
                case WeatherCondition.Storm:
                    // Stormy weather - very heavy clouds, heavy rain
                    SetPrecipitation(true, false, 2f);
                    SetCloudDensity(1f * intensity);
                    if (_enableFog) {
                        RenderSettings.fogDensity = _fogDensity * 2f;
                    }
                    break;
                    
                case WeatherCondition.Snow:
                    // Snowy weather - heavy clouds, snow
                    SetPrecipitation(false, true);
                    SetCloudDensity(0.8f * intensity);
                    if (_enableFog) {
                        RenderSettings.fogDensity = _fogDensity * 1.8f;
                    }
                    break;
                    
                case WeatherCondition.Foggy:
                    // Foggy weather - medium clouds, heavy fog
                    SetPrecipitation(false, false);
                    SetCloudDensity(0.5f * intensity);
                    RenderSettings.fogDensity = _fogDensity * 3f * intensity;
                    break;
                    
                default:
                    break;
            }
        }
        
        /// <summary>
        /// Applies interpolated weather settings between two conditions.
        /// </summary>
        private void ApplyWeatherSettings(WeatherCondition fromCondition, WeatherCondition toCondition, float t) {
            // Store original fog density
            float originalFogDensity = RenderSettings.fogDensity;
            
            // Apply 'from' settings with inverse intensity
            ApplyWeatherSettings(fromCondition, 1f - t);
            
            // Store intermediate values
            bool rainActiveFrom = _rainParticleSystem != null && _rainParticleSystem.isPlaying;
            bool snowActiveFrom = _snowParticleSystem != null && _snowParticleSystem.isPlaying;
            float fogDensityFrom = RenderSettings.fogDensity;
            
            // Apply 'to' settings with normal intensity
            ApplyWeatherSettings(toCondition, t);
            
            // Get 'to' values
            bool rainActiveTo = _rainParticleSystem != null && _rainParticleSystem.isPlaying;
            bool snowActiveTo = _snowParticleSystem != null && _snowParticleSystem.isPlaying;
            float fogDensityTo = RenderSettings.fogDensity;
            
            // Apply interpolated values for particle systems
            if (rainActiveFrom != rainActiveTo) {
                SetRainIntensity(rainActiveTo ? t : 1f - t);
            }
            
            if (snowActiveFrom != snowActiveTo) {
                SetSnowIntensity(snowActiveTo ? t : 1f - t);
            }
            
            // Interpolate fog density
            RenderSettings.fogDensity = Mathf.Lerp(originalFogDensity, fogDensityTo, t);
        }
        
        /// <summary>
        /// Sets precipitation state.
        /// </summary>
        private void SetPrecipitation(bool rain, bool snow, float intensity = 1f) {
            if (_rainParticleSystem != null) {
                var emission = _rainParticleSystem.emission;
                var main = _rainParticleSystem.main;
                
                if (rain) {
                    if (!_rainParticleSystem.isPlaying) {
                        _rainParticleSystem.Play();
                    }
                    
                    emission.rateOverTimeMultiplier = 1000f * intensity;
                    main.startSpeedMultiplier = 10f * intensity;
                } else {
                    if (_rainParticleSystem.isPlaying) {
                        _rainParticleSystem.Stop();
                    }
                }
            }
            
            if (_snowParticleSystem != null) {
                var emission = _snowParticleSystem.emission;
                var main = _snowParticleSystem.main;
                
                if (snow) {
                    if (!_snowParticleSystem.isPlaying) {
                        _snowParticleSystem.Play();
                    }
                    
                    emission.rateOverTimeMultiplier = 500f * intensity;
                    main.startSpeedMultiplier = 2f * intensity;
                } else {
                    if (_snowParticleSystem.isPlaying) {
                        _snowParticleSystem.Stop();
                    }
                }
            }
        }
        
        /// <summary>
        /// Sets cloud density.
        /// </summary>
        private void SetCloudDensity(float density) {
            if (_cloudParticleSystem != null) {
                var emission = _cloudParticleSystem.emission;
                
                if (density > 0.1f) {
                    if (!_cloudParticleSystem.isPlaying) {
                        _cloudParticleSystem.Play();
                    }
                    
                    emission.rateOverTimeMultiplier = 5f * density;
                } else {
                    if (_cloudParticleSystem.isPlaying) {
                        _cloudParticleSystem.Stop();
                    }
                }
            }
            
            // Update skybox cloud density if available
            if (RenderSettings.skybox != null) {
                if (RenderSettings.skybox.HasProperty("_CloudDensity")) {
                    RenderSettings.skybox.SetFloat("_CloudDensity", density);
                }
                if (RenderSettings.skybox.HasProperty("_CloudCoverage")) {
                    RenderSettings.skybox.SetFloat("_CloudCoverage", density);
                }
            }
        }
        
        /// <summary>
        /// Sets rain intensity.
        /// </summary>
        private void SetRainIntensity(float intensity) {
            if (_rainParticleSystem != null) {
                var emission = _rainParticleSystem.emission;
                var main = _rainParticleSystem.main;
                
                if (intensity > 0.01f) {
                    if (!_rainParticleSystem.isPlaying) {
                        _rainParticleSystem.Play();
                    }
                    
                    emission.rateOverTimeMultiplier = 1000f * intensity;
                    main.startSpeedMultiplier = 10f * intensity;
                } else {
                    if (_rainParticleSystem.isPlaying) {
                        _rainParticleSystem.Stop();
                    }
                }
            }
        }
        
        /// <summary>
        /// Sets snow intensity.
        /// </summary>
        private void SetSnowIntensity(float intensity) {
            if (_snowParticleSystem != null) {
                var emission = _snowParticleSystem.emission;
                var main = _snowParticleSystem.main;
                
                if (intensity > 0.01f) {
                    if (!_snowParticleSystem.isPlaying) {
                        _snowParticleSystem.Play();
                    }
                    
                    emission.rateOverTimeMultiplier = 500f * intensity;
                    main.startSpeedMultiplier = 2f * intensity;
                } else {
                    if (_snowParticleSystem.isPlaying) {
                        _snowParticleSystem.Stop();
                    }
                }
            }
        }
        
        /// <summary>
        /// Environment transition coroutine.
        /// </summary>
        private IEnumerator TransitionEnvironment(float transitionTime) {
            // Cache original values
            CacheEnvironmentValues();
            
            float startTime = Time.time;
            float endTime = startTime + transitionTime;
            
            while (Time.time < endTime) {
                float t = (Time.time - startTime) / transitionTime;
                
                // Interpolate values
                InterpolateEnvironmentValues(t);
                
                yield return null;
            }
            
            // Apply final values
            ConfigureLighting();
            ConfigureAtmosphere();
            ConfigureWater();
            ConfigurePostProcessing();
        }
        
        /// <summary>
        /// Caches original environment values for smooth transitions.
        /// </summary>
        private void CacheEnvironmentValues() {
            _cachedProperties.Clear();
            
            // Cache lighting values
            if (_directionalLight != null) {
                _cachedProperties["sunIntensity"] = _directionalLight.intensity;
                _cachedProperties["sunColor"] = _directionalLight.color;
                _cachedProperties["sunTemperature"] = _directionalLight.colorTemperature;
                _cachedProperties["sunRotation"] = _directionalLight.transform.rotation;
            }
            
            // Cache ambient values
            _cachedProperties["ambientIntensity"] = RenderSettings.ambientIntensity;
            
            // Cache fog values
            _cachedProperties["fogEnabled"] = RenderSettings.fog;
            _cachedProperties["fogColor"] = RenderSettings.fogColor;
            _cachedProperties["fogDensity"] = RenderSettings.fogDensity;
            
            // Cache skybox values
            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure")) {
                _cachedProperties["skyboxExposure"] = RenderSettings.skybox.GetFloat("_Exposure");
            }
            
            // Cache water values
            if (_waterPlane != null) {
                Renderer rend = _waterPlane.GetComponent<Renderer>();
                if (rend != null && rend.material != null) {
                    _cachedProperties["waterColor"] = rend.material.color;
                    if (rend.material.HasProperty("_WaveHeight")) {
                        _cachedProperties["waveHeight"] = rend.material.GetFloat("_WaveHeight");
                    }
                    if (rend.material.HasProperty("_WaveSpeed")) {
                        _cachedProperties["waveSpeed"] = rend.material.GetFloat("_WaveSpeed");
                    }
                }
                
                _cachedProperties["waterPosition"] = _waterPlane.transform.position;
            }
        }
        
        /// <summary>
        /// Interpolates environment values for smooth transitions.
        /// </summary>
        private void InterpolateEnvironmentValues(float t) {
            // Interpolate lighting values
            if (_directionalLight != null) {
                if (_cachedProperties.ContainsKey("sunIntensity")) {
                    _directionalLight.intensity = Mathf.Lerp((float)_cachedProperties["sunIntensity"], _sunIntensity, t);
                }
                
                if (_cachedProperties.ContainsKey("sunTemperature")) {
                    _directionalLight.colorTemperature = Mathf.Lerp((float)_cachedProperties["sunTemperature"], _defaultColorTemperature, t);
                }
                
                if (_cachedProperties.ContainsKey("sunRotation")) {
                    _directionalLight.transform.rotation = Quaternion.Slerp(
                        (Quaternion)_cachedProperties["sunRotation"],
                        Quaternion.Euler(_sunPitch, _sunYaw, 0f),
                        t
                    );
                }
            }
            
            // Interpolate ambient values
            if (_cachedProperties.ContainsKey("ambientIntensity")) {
                RenderSettings.ambientIntensity = Mathf.Lerp((float)_cachedProperties["ambientIntensity"], _ambientIntensity, t);
            }
            
            // Interpolate fog values
            if (_cachedProperties.ContainsKey("fogEnabled") && (bool)_cachedProperties["fogEnabled"] != _enableFog) {
                // If changing fog state, wait until the end to toggle
                if (t >= 0.99f) {
                    RenderSettings.fog = _enableFog;
                }
            }
            
            if (_cachedProperties.ContainsKey("fogColor")) {
                RenderSettings.fogColor = Color.Lerp((Color)_cachedProperties["fogColor"], _fogColor, t);
            }
            
            if (_cachedProperties.ContainsKey("fogDensity")) {
                RenderSettings.fogDensity = Mathf.Lerp((float)_cachedProperties["fogDensity"], _fogDensity, t);
            }
            
            // Interpolate skybox values
            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure") && _cachedProperties.ContainsKey("skyboxExposure")) {
                RenderSettings.skybox.SetFloat("_Exposure", Mathf.Lerp((float)_cachedProperties["skyboxExposure"], _skyboxExposure, t));
            }
            
            // Interpolate water values
            if (_waterPlane != null) {
                Renderer rend = _waterPlane.GetComponent<Renderer>();
                if (rend != null && rend.material != null) {
                    if (_cachedProperties.ContainsKey("waterColor")) {
                        rend.material.color = Color.Lerp((Color)_cachedProperties["waterColor"], _waterColor, t);
                    }
                    
                    if (rend.material.HasProperty("_WaveHeight") && _cachedProperties.ContainsKey("waveHeight")) {
                        rend.material.SetFloat("_WaveHeight", Mathf.Lerp((float)_cachedProperties["waveHeight"], _waveHeight, t));
                    }
                    
                    if (rend.material.HasProperty("_WaveSpeed") && _cachedProperties.ContainsKey("waveSpeed")) {
                        rend.material.SetFloat("_WaveSpeed", Mathf.Lerp((float)_cachedProperties["waveSpeed"], _waveSpeed, t));
                    }
                }
                
                if (_cachedProperties.ContainsKey("waterPosition")) {
                    _waterPlane.transform.position = Vector3.Lerp(
                        (Vector3)_cachedProperties["waterPosition"],
                        new Vector3(
                            _waterPlane.transform.position.x,
                            CalculateWaterHeight(),
                            _waterPlane.transform.position.z
                        ),
                        t
                    );
                }
            }
        }
        
        /// <summary>
        /// Calculates water height based on terrain.
        /// </summary>
        private float CalculateWaterHeight() {
            UnityEngine.Terrain terrain = FindObjectOfType<UnityEngine.Terrain>();
            if (terrain != null) {
                return _waterHeight * terrain.terrainData.size.y;
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Determines the most suitable environment preset based on analysis results.
        /// </summary>
        private string DeterminePresetFromAnalysis(AI.AnalysisResults analysisResults) {
            if (_environmentPresets == null || _environmentPresets.Count == 0) {
                return string.Empty;
            }
            
            // Check for dominant terrain types
            bool hasMountains = false;
            bool hasWater = false;
            bool hasForest = false;
            bool hasDesert = false;
            bool hasSnow = false;
            
            foreach (var feature in analysisResults.terrainFeatures) {
                string label = feature.label.ToLowerInvariant();
                
                if (label.Contains("mountain") || label.Contains("hill")) {
                    hasMountains = true;
                }
                
                if (label.Contains("water") || label.Contains("lake") || label.Contains("river") || label.Contains("ocean")) {
                    hasWater = true;
                }
                
                if (label.Contains("forest") || label.Contains("tree") || label.Contains("wood")) {
                    hasForest = true;
                }
                
                if (label.Contains("desert") || label.Contains("sand") || label.Contains("dune")) {
                    hasDesert = true;
                }
                
                if (label.Contains("snow") || label.Contains("ice") || label.Contains("glacier")) {
                    hasSnow = true;
                }
            }
            
            // Determine time of day from image brightness
            float avgBrightness = 0.5f; // Default to midday
            if (analysisResults.metadata.ContainsKey("averageBrightness")) {
                try {
                    avgBrightness = (float)analysisResults.metadata["averageBrightness"];
                } catch (Exception) {
                    // Use default
                }
            }
            
            bool isNight = avgBrightness < 0.3f;
            bool isDusk = avgBrightness >= 0.3f && avgBrightness < 0.4f;
            bool isDawn = avgBrightness >= 0.4f && avgBrightness < 0.5f;
            bool isDay = avgBrightness >= 0.5f;
            
            // Determine weather from image features
            bool isRainy = false;
            bool isCloudy = false;
            bool isFoggy = false;
            bool isSnowy = false;
            
            if (analysisResults.metadata.ContainsKey("weatherHints")) {
                try {
                    Dictionary<string, float> weatherHints = (Dictionary<string, float>)analysisResults.metadata["weatherHints"];
                    
                    if (weatherHints.ContainsKey("rain")) {
                        isRainy = weatherHints["rain"] > 0.6f;
                    }
                    
                    if (weatherHints.ContainsKey("clouds")) {
                        isCloudy = weatherHints["clouds"] > 0.6f;
                    }
                    
                    if (weatherHints.ContainsKey("fog")) {
                        isFoggy = weatherHints["fog"] > 0.6f;
                    }
                    
                    if (weatherHints.ContainsKey("snow")) {
                        isSnowy = weatherHints["snow"] > 0.6f;
                    }
                } catch (Exception) {
                    // Use default
                }
            }
            
            // Find best matching preset
            string bestPreset = string.Empty;
            float bestScore = 0f;
            
            foreach (var preset in _environmentPresets) {
                float score = 0f;
                
                // Score based on terrain types
                if (hasMountains && preset.name.ToLowerInvariant().Contains("mountain")) {
                    score += 2f;
                }
                
                if (hasWater && preset.name.ToLowerInvariant().Contains("water")) {
                    score += 2f;
                }
                
                if (hasForest && preset.name.ToLowerInvariant().Contains("forest")) {
                    score += 2f;
                }
                
                if (hasDesert && preset.name.ToLowerInvariant().Contains("desert")) {
                    score += 3f;
                }
                
                if (hasSnow && preset.name.ToLowerInvariant().Contains("snow")) {
                    score += 3f;
                }
                
                // Score based on time of day
                if (isNight && preset.name.ToLowerInvariant().Contains("night")) {
                    score += 3f;
                }
                
                if (isDusk && preset.name.ToLowerInvariant().Contains("dusk")) {
                    score += 3f;
                }
                
                if (isDawn && preset.name.ToLowerInvariant().Contains("dawn")) {
                    score += 3f;
                }
                
                if (isDay && preset.name.ToLowerInvariant().Contains("day")) {
                    score += 1f;
                }
                
                // Score based on weather
                if (isRainy && preset.weatherCondition == WeatherCondition.Rain) {
                    score += 3f;
                }
                
                if (isCloudy && (preset.weatherCondition == WeatherCondition.Cloudy || preset.weatherCondition == WeatherCondition.Overcast)) {
                    score += 2f;
                }
                
                if (isFoggy && preset.weatherCondition == WeatherCondition.Foggy) {
                    score += 3f;
                }
                
                if (isSnowy && preset.weatherCondition == WeatherCondition.Snow) {
                    score += 3f;
                }
                
                // Update best match
                if (score > bestScore) {
                    bestScore = score;
                    bestPreset = preset.name;
                }
            }
            
            // If no good match found, use a default preset
            if (bestScore < 2f) {
                return "Default";
            }
            
            return bestPreset;
        }
        
        #endregion
        
        #region TraversifyComponent Implementation
        
        /// <summary>
        /// Component-specific initialization logic.
        /// </summary>
        /// <param name="config">Environment configuration object</param>
        /// <returns>True if initialization was successful</returns>
        protected override bool OnInitialize(object config) {
            try {
                // Initialize environment with default settings
                InitializeEnvironment();
                
                // Apply configuration if provided
                if (config != null) {
                    if (config is EnvironmentPreset preset) {
                        ApplyEnvironmentPreset(preset, 0f);
                    } else if (config is string presetName) {
                        ApplyEnvironmentPreset(presetName, 0f);
                    }
                }
                
                return true;
            } catch (Exception ex) {
                _debugger?.LogError($"Failed to initialize EnvironmentManager: {ex.Message}", LogCategory.System);
                return false;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Enum defining weather conditions.
    /// </summary>
    public enum WeatherCondition {
        Clear,
        Cloudy,
        Overcast,
        Rain,
        Storm,
        Snow,
        Foggy
    }
    
    /// <summary>
    /// Environment preset for quickly switching between different environmental configurations.
    /// </summary>
    [Serializable]
    public class EnvironmentPreset {
        [Header("Preset Information")]
        public string name = "Default";
        public string description = "Default environment preset";
        
        [Header("Lighting Settings")]
        public float colorTemperature = 6500f;
        public float ambientIntensity = 1f;
        public float sunIntensity = 1f;
        public float sunPitch = 45f;
        public float sunYaw = 45f;
        public bool enableShadows = true;
        
        [Header("Atmosphere Settings")]
        public bool enableFog = true;
        public Color fogColor = new Color(0.76f, 0.85f, 0.95f, 1f);
        public float fogDensity = 0.01f;
        public float fogStartDistance = 10f;
        public float fogEndDistance = 1000f;
        public Material skyboxMaterial;
        public float skyboxExposure = 1f;
        
        [Header("Water Settings")]
        public float waterHeight = 0.3f;
        public Color waterColor = new Color(0.15f, 0.35f, 0.68f, 0.8f);
        public float waveHeight = 0.5f;
        public float waveSpeed = 0.5f;
        
        [Header("Time Settings")]
        public bool enableTimeOfDayCycle = false;
        public float dayLengthMinutes = 24f;
        public float timeScale = 1f;
        
        [Header("Weather Settings")]
        public WeatherCondition weatherCondition = WeatherCondition.Clear;
        
        [Header("Post-Processing Settings")]
        public bool enablePostProcessing = true;
        public UnityEngine.Object postProcessingProfile;
        public float exposureCompensation = 0f;
    }
    
    /// <summary>
    /// Component for animating water surfaces.
    /// </summary>
    public class WaterAnimation : MonoBehaviour {
        public float waveSpeed = 0.5f;
        public float waveHeight = 0.5f;
        
        private Renderer waterRenderer;
        private Vector2 uvOffset = Vector2.zero;
        
        private void Start() {
            waterRenderer = GetComponent<Renderer>();
        }
        
        private void Update() {
            if (waterRenderer != null && waterRenderer.material != null) {
                // Animate water by shifting UVs
                uvOffset.x = Mathf.Sin(Time.time * waveSpeed) * waveHeight * 0.01f;
                uvOffset.y = Mathf.Cos(Time.time * waveSpeed) * waveHeight * 0.01f;
                
                waterRenderer.material.mainTextureOffset = uvOffset;
            }
        }
    }
}

