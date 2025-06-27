/*************************************************************************
 *  Traversify – PerformanceMonitor.cs                                   *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 04:00:27 UTC                                     *
 *  Desc   : Comprehensive performance monitoring and optimization        *
 *           system for the Traversify environment generation pipeline.   *
 *           Tracks memory usage, frame times, operation performance,     *
 *           and provides optimization suggestions.                       *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Traversify.Core {
    /// <summary>
    /// Comprehensive performance monitoring and optimization system for the Traversify pipeline.
    /// Tracks memory usage, frame times, operation performance, and provides optimization suggestions.
    /// </summary>
    public class PerformanceMonitor : MonoBehaviour {
        #region Singleton

        private static PerformanceMonitor _instance;
        private static readonly object _lock = new object();
        
        /// <summary>
        /// Gets the singleton instance of the PerformanceMonitor.
        /// </summary>
        public static PerformanceMonitor Instance {
            get {
                if (_instance == null) {
                    lock (_lock) {
                        if (_instance == null) {
                            _instance = FindObjectOfType<PerformanceMonitor>();
                            if (_instance == null) {
                                GameObject go = new GameObject("PerformanceMonitor");
                                _instance = go.AddComponent<PerformanceMonitor>();
                                DontDestroyOnLoad(go);
                            }
                        }
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Inspector Fields

        [Header("Monitoring Settings")]
        [Tooltip("Should the monitor be active on startup")]
        [SerializeField] private bool _activeOnStartup = true;

        [Tooltip("Interval in seconds between sampling performance metrics")]
        [SerializeField] private float _samplingInterval = 0.5f;

        [Tooltip("Duration in seconds to keep performance history")]
        [SerializeField] private float _historyDuration = 60f;

        [Tooltip("Show GPU/CPU timing and memory usage overlay")]
        [SerializeField] private bool _showOverlay = false;

        [Tooltip("Level of detail for logged performance metrics")]
        [SerializeField] private MonitoringDetail _detailLevel = MonitoringDetail.Medium;

        [Tooltip("Whether to monitor memory usage")]
        [SerializeField] private bool _monitorMemory = true;

        [Tooltip("Whether to monitor frame times")]
        [SerializeField] private bool _monitorFrameTimes = true;

        [Tooltip("Whether to monitor GPU usage")]
        [SerializeField] private bool _monitorGPU = true;

        [Tooltip("Whether to automatically log warnings for performance issues")]
        [SerializeField] private bool _autoLogWarnings = true;

        [Header("Thresholds")]
        [Tooltip("Threshold for low framerate warning (fps)")]
        [SerializeField] private float _lowFramerateThreshold = 30f;

        [Tooltip("Threshold for memory usage warning (percentage)")]
        [SerializeField] private float _highMemoryThreshold = 80f;

        [Tooltip("Threshold for long frame time warning (ms)")]
        [SerializeField] private float _longFrameTimeThreshold = 33.3f;

        [Tooltip("Threshold for long operation time warning (ms)")]
        [SerializeField] private float _longOperationTimeThreshold = 100f;

        [Header("UI")]
        [Tooltip("Reference to the TraversifyDebugger component")]
        [SerializeField] private TraversifyDebugger _debugger;

        [Tooltip("UI prefab for performance overlay")]
        [SerializeField] private GameObject _overlayPrefab;

        [Tooltip("Screen position for performance overlay")]
        [SerializeField] private OverlayPosition _overlayPosition = OverlayPosition.TopRight;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the performance monitor is currently active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Current frames per second.
        /// </summary>
        public float CurrentFPS { get; private set; }

        /// <summary>
        /// Current frame time in milliseconds.
        /// </summary>
        public float CurrentFrameTimeMS { get; private set; }

        /// <summary>
        /// Current memory usage in megabytes.
        /// </summary>
        public float CurrentMemoryUsageMB { get; private set; }

        /// <summary>
        /// Current memory usage as a percentage of total available memory.
        /// </summary>
        public float CurrentMemoryUsagePercentage { get; private set; }

        /// <summary>
        /// Peak memory usage in megabytes.
        /// </summary>
        public float PeakMemoryUsageMB { get; private set; }

        /// <summary>
        /// Current GPU memory usage in megabytes (if available).
        /// </summary>
        public float CurrentGPUMemoryUsageMB { get; private set; }

        /// <summary>
        /// Average FPS over the last second.
        /// </summary>
        public float AverageFPS { get; private set; }

        /// <summary>
        /// Whether performance issues have been detected.
        /// </summary>
        public bool HasPerformanceIssues { get; private set; }

        /// <summary>
        /// Total count of performance warnings issued.
        /// </summary>
        public int WarningCount { get; private set; }

        /// <summary>
        /// Collection of active operations being timed.
        /// </summary>
        public IReadOnlyDictionary<string, OperationTimer> ActiveOperations => _activeOperations;

        /// <summary>
        /// Collection of completed operation timings.
        /// </summary>
        public IReadOnlyDictionary<string, List<OperationResult>> CompletedOperations => _completedOperations;

        #endregion

        #region Events

        /// <summary>
        /// Event triggered when a performance issue is detected.
        /// </summary>
        public event Action<PerformanceIssue> OnPerformanceIssueDetected;

        /// <summary>
        /// Event triggered when performance metrics are updated.
        /// </summary>
        public event Action<PerformanceMetrics> OnMetricsUpdated;

        /// <summary>
        /// Event triggered when an operation timing is completed.
        /// </summary>
        public event Action<OperationResult> OnOperationCompleted;

        #endregion

        #region Private Fields

        private Coroutine _monitoringCoroutine;
        private Coroutine _memoryMonitoringCoroutine;
        private Coroutine _gpuMonitoringCoroutine;
        private GameObject _overlayInstance;
        
        private float _lastSampleTime;
        private int _frameCount;
        private float _accumulatedFrameTime;
        private float _timeScale = 1.0f;
        
        private Queue<float> _fpsHistory = new Queue<float>();
        private Queue<float> _frameTimeHistory = new Queue<float>();
        private Queue<float> _memoryUsageHistory = new Queue<float>();
        private Queue<float> _gpuMemoryUsageHistory = new Queue<float>();
        
        private Dictionary<string, OperationTimer> _activeOperations = new Dictionary<string, OperationTimer>();
        private Dictionary<string, List<OperationResult>> _completedOperations = new Dictionary<string, List<OperationResult>>();
        
        private float _maxHistorySamples;
        private bool _isOverlayInitialized = false;
        private Rect _overlayRect = new Rect(10, 10, 300, 150);
        
        private StringBuilder _stringBuilder = new StringBuilder(1024);
        private TraversifyGraphics.CustomOverlayRenderer _overlayRenderer;
        private GUIStyle _overlayStyle;
        
        private class OperationTimerComparer : IComparer<OperationResult> {
            public int Compare(OperationResult x, OperationResult y) {
                return y.DurationMS.CompareTo(x.DurationMS);
            }
        }
        
        private static readonly OperationTimerComparer _operationTimerComparer = new OperationTimerComparer();
        
        #endregion

        #region Unity Lifecycle

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Find debugger if not assigned
            if (_debugger == null) {
                _debugger = FindObjectOfType<TraversifyDebugger>();
                if (_debugger == null && GetComponent<TraversifyDebugger>() != null) {
                    _debugger = GetComponent<TraversifyDebugger>();
                }
            }
            
            // Calculate max history samples based on duration and interval
            _maxHistorySamples = Mathf.Ceil(_historyDuration / _samplingInterval);
            
            // Initialize overlay if needed
            if (_showOverlay) {
                InitializeOverlay();
            }
            
            // Start monitoring if active on startup
            if (_activeOnStartup) {
                StartMonitoring();
            }
            
            Log("PerformanceMonitor initialized", LogCategory.Performance);
        }

        private void OnEnable() {
            if (IsActive) {
                StartMonitoringCoroutines();
            }
        }

        private void OnDisable() {
            StopMonitoringCoroutines();
        }

        private void OnDestroy() {
            StopMonitoring();
            
            if (_overlayInstance != null) {
                Destroy(_overlayInstance);
                _overlayInstance = null;
            }
            
            if (_instance == this) {
                _instance = null;
            }
        }

        private void Update() {
            // Update frame time tracking
            if (_monitorFrameTimes && IsActive) {
                float deltaTime = Time.unscaledDeltaTime;
                CurrentFrameTimeMS = deltaTime * 1000f;
                CurrentFPS = 1.0f / deltaTime;
                
                _frameCount++;
                _accumulatedFrameTime += deltaTime;
                
                // Check for long frames
                if (_autoLogWarnings && CurrentFrameTimeMS > _longFrameTimeThreshold) {
                    ReportPerformanceIssue(
                        PerformanceIssueType.LongFrame,
                        $"Long frame detected: {CurrentFrameTimeMS:F2}ms (target: {_longFrameTimeThreshold:F2}ms)",
                        CurrentFrameTimeMS / _longFrameTimeThreshold
                    );
                }
            }
            
            // Update overlay if active
            if (_showOverlay && _isOverlayInitialized) {
                UpdateOverlay();
            }
        }

        private void OnGUI() {
            if (_showOverlay && _overlayRenderer != null) {
                _overlayRenderer.OnGUI();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts performance monitoring.
        /// </summary>
        public void StartMonitoring() {
            if (IsActive) return;
            
            IsActive = true;
            _lastSampleTime = Time.realtimeSinceStartup;
            _frameCount = 0;
            _accumulatedFrameTime = 0f;
            
            ClearHistory();
            StartMonitoringCoroutines();
            
            if (_showOverlay && !_isOverlayInitialized) {
                InitializeOverlay();
            }
            
            Log("Performance monitoring started", LogCategory.Performance);
        }

        /// <summary>
        /// Stops performance monitoring.
        /// </summary>
        public void StopMonitoring() {
            if (!IsActive) return;
            
            IsActive = false;
            StopMonitoringCoroutines();
            
            Log("Performance monitoring stopped", LogCategory.Performance);
        }

        /// <summary>
        /// Toggles performance monitoring on/off.
        /// </summary>
        public void ToggleMonitoring() {
            if (IsActive) {
                StopMonitoring();
            } else {
                StartMonitoring();
            }
        }

        /// <summary>
        /// Toggles the performance overlay display.
        /// </summary>
        public void ToggleOverlay() {
            _showOverlay = !_showOverlay;
            
            if (_showOverlay && !_isOverlayInitialized) {
                InitializeOverlay();
            }
            
            if (_overlayInstance != null) {
                _overlayInstance.SetActive(_showOverlay);
            }
        }

        /// <summary>
        /// Starts timing an operation with the specified name.
        /// </summary>
        /// <param name="operationName">Name of the operation to time</param>
        /// <param name="category">Category of the operation</param>
        /// <returns>Timer token for the operation</returns>
        public OperationTimer StartOperation(string operationName, string category = "Default") {
            if (string.IsNullOrEmpty(operationName)) {
                throw new ArgumentException("Operation name cannot be null or empty", nameof(operationName));
            }
            
            string fullName = $"{category}:{operationName}";
            
            if (_activeOperations.ContainsKey(fullName)) {
                LogWarning($"Operation '{fullName}' is already being timed. Restarting timer.", LogCategory.Performance);
                _activeOperations[fullName].Restart();
                return _activeOperations[fullName];
            }
            
            var timer = new OperationTimer(operationName, category, this);
            _activeOperations[fullName] = timer;
            
            return timer;
        }

        /// <summary>
        /// Ends timing for the specified operation and records the result.
        /// </summary>
        /// <param name="operationName">Name of the operation</param>
        /// <param name="category">Category of the operation</param>
        /// <returns>Operation timing result</returns>
        public OperationResult EndOperation(string operationName, string category = "Default") {
            string fullName = $"{category}:{operationName}";
            
            if (!_activeOperations.TryGetValue(fullName, out var timer)) {
                LogWarning($"Attempted to end timing for operation '{fullName}' that was not started", LogCategory.Performance);
                return null;
            }
            
            OperationResult result = timer.End();
            _activeOperations.Remove(fullName);
            
            if (!_completedOperations.ContainsKey(fullName)) {
                _completedOperations[fullName] = new List<OperationResult>();
            }
            
            _completedOperations[fullName].Add(result);
            
            // Keep only the last 100 results per operation
            if (_completedOperations[fullName].Count > 100) {
                _completedOperations[fullName].RemoveAt(0);
            }
            
            // Check for long operations
            if (_autoLogWarnings && result.DurationMS > _longOperationTimeThreshold) {
                ReportPerformanceIssue(
                    PerformanceIssueType.LongOperation,
                    $"Long operation detected: {fullName} took {result.DurationMS:F2}ms (threshold: {_longOperationTimeThreshold:F2}ms)",
                    result.DurationMS / _longOperationTimeThreshold
                );
            }
            
            OnOperationCompleted?.Invoke(result);
            
            return result;
        }

        /// <summary>
        /// Creates and returns a scoped timer that automatically times an operation from creation to disposal.
        /// </summary>
        /// <param name="operationName">Name of the operation to time</param>
        /// <param name="category">Category of the operation</param>
        /// <returns>Disposable operation scope</returns>
        public OperationScope TimeOperation(string operationName, string category = "Default") {
            return new OperationScope(this, operationName, category);
        }

        /// <summary>
        /// Clears the performance history.
        /// </summary>
        public void ClearHistory() {
            _fpsHistory.Clear();
            _frameTimeHistory.Clear();
            _memoryUsageHistory.Clear();
            _gpuMemoryUsageHistory.Clear();
            _completedOperations.Clear();
        }

        /// <summary>
        /// Gets the average duration of an operation over its history.
        /// </summary>
        /// <param name="operationName">Name of the operation</param>
        /// <param name="category">Category of the operation</param>
        /// <returns>Average duration in milliseconds, or 0 if no data is available</returns>
        public float GetAverageOperationDuration(string operationName, string category = "Default") {
            string fullName = $"{category}:{operationName}";
            
            if (!_completedOperations.TryGetValue(fullName, out var results) || results.Count == 0) {
                return 0f;
            }
            
            return results.Average(r => r.DurationMS);
        }

        /// <summary>
        /// Gets the current performance metrics.
        /// </summary>
        /// <returns>Current performance metrics</returns>
        public PerformanceMetrics GetCurrentMetrics() {
            return new PerformanceMetrics {
                FPS = CurrentFPS,
                FrameTimeMS = CurrentFrameTimeMS,
                MemoryUsageMB = CurrentMemoryUsageMB,
                MemoryUsagePercentage = CurrentMemoryUsagePercentage,
                GPUMemoryUsageMB = CurrentGPUMemoryUsageMB,
                AverageFPS = AverageFPS,
                HasPerformanceIssues = HasPerformanceIssues,
                TimeScale = _timeScale,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Gets the top N operations by average duration.
        /// </summary>
        /// <param name="count">Number of operations to return</param>
        /// <returns>List of operation timings sorted by duration</returns>
        public List<OperationResult> GetTopOperations(int count) {
            var allOperations = new List<OperationResult>();
            
            foreach (var opList in _completedOperations.Values) {
                if (opList.Count > 0) {
                    // Use the most recent result for each operation
                    allOperations.Add(opList[opList.Count - 1]);
                }
            }
            
            allOperations.Sort(_operationTimerComparer);
            
            return allOperations.Take(count).ToList();
        }

        /// <summary>
        /// Takes a performance snapshot and generates a detailed report.
        /// </summary>
        /// <returns>Performance report as a string</returns>
        public string GeneratePerformanceReport() {
            StringBuilder report = new StringBuilder();
            DateTime now = DateTime.UtcNow;
            
            report.AppendLine("=== TRAVERSIFY PERFORMANCE REPORT ===");
            report.AppendLine($"Generated: {now:yyyy-MM-dd HH:mm:ss} UTC");
            report.AppendLine($"System: {SystemInfo.deviceModel} ({SystemInfo.processorType})");
            report.AppendLine($"OS: {SystemInfo.operatingSystem}");
            report.AppendLine($"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)");
            report.AppendLine($"Unity: {Application.unityVersion}");
            report.AppendLine("-----------------------------------");
            
            // Current performance
            report.AppendLine($"Current FPS: {CurrentFPS:F1}");
            report.AppendLine($"Current Frame Time: {CurrentFrameTimeMS:F2} ms");
            report.AppendLine($"Average FPS: {AverageFPS:F1}");
            report.AppendLine($"Memory Usage: {CurrentMemoryUsageMB:F1} MB ({CurrentMemoryUsagePercentage:F1}%)");
            report.AppendLine($"Peak Memory Usage: {PeakMemoryUsageMB:F1} MB");
            
            if (_monitorGPU) {
                report.AppendLine($"GPU Memory: {CurrentGPUMemoryUsageMB:F1} MB");
            }
            
            report.AppendLine("-----------------------------------");
            
            // Top operations
            var topOps = GetTopOperations(10);
            report.AppendLine("Top 10 Operations (by duration):");
            
            for (int i = 0; i < topOps.Count; i++) {
                var op = topOps[i];
                report.AppendLine($"{i+1}. {op.OperationName} ({op.Category}): {op.DurationMS:F2} ms");
            }
            
            report.AppendLine("-----------------------------------");
            
            // Performance issues
            report.AppendLine($"Performance Warnings: {WarningCount}");
            report.AppendLine($"Has Performance Issues: {HasPerformanceIssues}");
            
            // Generate recommendations
            report.AppendLine("-----------------------------------");
            report.AppendLine("Recommendations:");
            
            if (AverageFPS < _lowFramerateThreshold) {
                report.AppendLine("- Low framerate detected. Consider reducing quality settings or optimizing heavy operations.");
            }
            
            if (CurrentMemoryUsagePercentage > _highMemoryThreshold) {
                report.AppendLine("- High memory usage detected. Check for memory leaks or reduce texture/mesh quality.");
            }
            
            if (topOps.Count > 0 && topOps[0].DurationMS > _longOperationTimeThreshold) {
                report.AppendLine($"- Operation '{topOps[0].OperationName}' is taking too long ({topOps[0].DurationMS:F2} ms). Consider optimizing.");
            }
            
            report.AppendLine("===================================");
            
            return report.ToString();
        }

        /// <summary>
        /// Logs a performance-related message.
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="category">Log category</param>
        public void Log(string message, LogCategory category) {
            if (_debugger != null) {
                _debugger.Log(message, category);
            } else {
                Debug.Log($"[{category}] {message}");
            }
        }

        /// <summary>
        /// Logs a performance-related warning.
        /// </summary>
        /// <param name="message">Warning message to log</param>
        /// <param name="category">Log category</param>
        public void LogWarning(string message, LogCategory category) {
            if (_debugger != null) {
                _debugger.LogWarning(message, category);
            } else {
                Debug.LogWarning($"[{category}] {message}");
            }
        }

        /// <summary>
        /// Logs a performance-related error.
        /// </summary>
        /// <param name="message">Error message to log</param>
        /// <param name="category">Log category</param>
        public void LogError(string message, LogCategory category) {
            if (_debugger != null) {
                _debugger.LogError(message, category);
            } else {
                Debug.LogError($"[{category}] {message}");
            }
        }

        /// <summary>
        /// Sets the time scale for performance metrics.
        /// </summary>
        /// <param name="timeScale">New time scale value</param>
        public void SetTimeScale(float timeScale) {
            _timeScale = Mathf.Max(0.1f, timeScale);
            Log($"Performance time scale set to {_timeScale:F2}", LogCategory.Performance);
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Called internally by OperationTimer when an operation is ended.
        /// </summary>
        internal void HandleOperationEnd(OperationTimer timer, OperationResult result) {
            string fullName = $"{timer.Category}:{timer.OperationName}";
            
            if (_activeOperations.ContainsKey(fullName)) {
                _activeOperations.Remove(fullName);
            }
            
            if (!_completedOperations.ContainsKey(fullName)) {
                _completedOperations[fullName] = new List<OperationResult>();
            }
            
            _completedOperations[fullName].Add(result);
            
            // Keep only the last 100 results per operation
            if (_completedOperations[fullName].Count > 100) {
                _completedOperations[fullName].RemoveAt(0);
            }
            
            // Check for long operations
            if (_autoLogWarnings && result.DurationMS > _longOperationTimeThreshold) {
                ReportPerformanceIssue(
                    PerformanceIssueType.LongOperation,
                    $"Long operation detected: {fullName} took {result.DurationMS:F2}ms (threshold: {_longOperationTimeThreshold:F2}ms)",
                    result.DurationMS / _longOperationTimeThreshold
                );
            }
            
            OnOperationCompleted?.Invoke(result);
        }

        #endregion

        #region Private Methods

        private void StartMonitoringCoroutines() {
            StopMonitoringCoroutines();
            
            _monitoringCoroutine = StartCoroutine(MonitoringCoroutine());
            
            if (_monitorMemory) {
                _memoryMonitoringCoroutine = StartCoroutine(MemoryMonitoringCoroutine());
            }
            
            if (_monitorGPU) {
                _gpuMonitoringCoroutine = StartCoroutine(GPUMonitoringCoroutine());
            }
        }

        private void StopMonitoringCoroutines() {
            if (_monitoringCoroutine != null) {
                StopCoroutine(_monitoringCoroutine);
                _monitoringCoroutine = null;
            }
            
            if (_memoryMonitoringCoroutine != null) {
                StopCoroutine(_memoryMonitoringCoroutine);
                _memoryMonitoringCoroutine = null;
            }
            
            if (_gpuMonitoringCoroutine != null) {
                StopCoroutine(_gpuMonitoringCoroutine);
                _gpuMonitoringCoroutine = null;
            }
        }

        private void InitializeOverlay() {
            if (_isOverlayInitialized) return;
            
            if (_overlayPrefab != null) {
                _overlayInstance = Instantiate(_overlayPrefab, Vector3.zero, Quaternion.identity);
                _overlayInstance.transform.SetParent(transform);
                _overlayInstance.SetActive(_showOverlay);
                
                // Get custom renderer if available
                _overlayRenderer = _overlayInstance.GetComponent<TraversifyGraphics.CustomOverlayRenderer>();
            } else {
                // Create a fallback GUI style
                _overlayStyle = new GUIStyle {
                    normal = {
                        background = MakeBackgroundTexture(2, 2, new Color(0, 0, 0, 0.7f)),
                        textColor = Color.white
                    },
                    fontSize = 14,
                    padding = new RectOffset(10, 10, 10, 10)
                };
                
                // Calculate overlay position
                CalculateOverlayPosition();
                
                // Create a basic overlay renderer
                _overlayRenderer = gameObject.AddComponent<TraversifyGraphics.CustomOverlayRenderer>();
                _overlayRenderer.Initialize(_overlayRect, _overlayStyle, UpdateOverlayContent);
            }
            
            _isOverlayInitialized = true;
        }

        private void CalculateOverlayPosition() {
            float width = 300f;
            float height = 150f;
            
            switch (_overlayPosition) {
                case OverlayPosition.TopLeft:
                    _overlayRect = new Rect(10, 10, width, height);
                    break;
                case OverlayPosition.TopRight:
                    _overlayRect = new Rect(Screen.width - width - 10, 10, width, height);
                    break;
                case OverlayPosition.BottomLeft:
                    _overlayRect = new Rect(10, Screen.height - height - 10, width, height);
                    break;
                case OverlayPosition.BottomRight:
                    _overlayRect = new Rect(Screen.width - width - 10, Screen.height - height - 10, width, height);
                    break;
                case OverlayPosition.Center:
                    _overlayRect = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
                    break;
            }
        }

        private void UpdateOverlay() {
            if (_overlayRenderer != null) {
                _overlayRenderer.UpdateOverlay();
            }
        }

        private string UpdateOverlayContent() {
            _stringBuilder.Clear();
            
            // Basic performance metrics
            _stringBuilder.AppendLine($"<b>Traversify Performance</b>");
            _stringBuilder.AppendLine($"FPS: {CurrentFPS:F1} ({(CurrentFrameTimeMS):F1} ms)");
            _stringBuilder.AppendLine($"Memory: {CurrentMemoryUsageMB:F1} MB ({CurrentMemoryUsagePercentage:F1}%)");
            
            if (_monitorGPU && CurrentGPUMemoryUsageMB > 0) {
                _stringBuilder.AppendLine($"GPU Memory: {CurrentGPUMemoryUsageMB:F1} MB");
            }
            
            // Add top operations if we have any
            var topOps = GetTopOperations(3);
            if (topOps.Count > 0) {
                _stringBuilder.AppendLine();
                _stringBuilder.AppendLine("<b>Top Operations:</b>");
                
                foreach (var op in topOps) {
                    _stringBuilder.AppendLine($"- {op.OperationName}: {op.DurationMS:F1} ms");
                }
            }
            
            // Add warning if there are performance issues
            if (HasPerformanceIssues) {
                _stringBuilder.AppendLine();
                _stringBuilder.AppendLine("<color=yellow><b>⚠ Performance Issues Detected</b></color>");
            }
            
            return _stringBuilder.ToString();
        }

        private Texture2D MakeBackgroundTexture(int width, int height, Color color) {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = color;
            }
            
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            
            return texture;
        }

        private void ReportPerformanceIssue(PerformanceIssueType type, string message, float severity) {
            WarningCount++;
            HasPerformanceIssues = true;
            
            var issue = new PerformanceIssue {
                Type = type,
                Message = message,
                Severity = severity,
                Timestamp = DateTime.UtcNow
            };
            
            OnPerformanceIssueDetected?.Invoke(issue);
            
            if (_autoLogWarnings) {
                LogWarning(message, LogCategory.Performance);
            }
        }

        private IEnumerator MonitoringCoroutine() {
            while (IsActive) {
                float currentTime = Time.realtimeSinceStartup;
                float elapsed = currentTime - _lastSampleTime;
                
                if (elapsed >= _samplingInterval) {
                    // Calculate average FPS
                    if (_accumulatedFrameTime > 0) {
                        AverageFPS = _frameCount / _accumulatedFrameTime;
                    }
                    
                    // Store history
                    _fpsHistory.Enqueue(AverageFPS);
                    _frameTimeHistory.Enqueue(CurrentFrameTimeMS);
                    
                    // Trim history if needed
                    while (_fpsHistory.Count > _maxHistorySamples) {
                        _fpsHistory.Dequeue();
                    }
                    
                    while (_frameTimeHistory.Count > _maxHistorySamples) {
                        _frameTimeHistory.Dequeue();
                    }
                    
                    // Reset counters
                    _lastSampleTime = currentTime;
                    _frameCount = 0;
                    _accumulatedFrameTime = 0;
                    
                    // Check for low framerate
                    if (_autoLogWarnings && AverageFPS < _lowFramerateThreshold) {
                        ReportPerformanceIssue(
                            PerformanceIssueType.LowFramerate,
                            $"Low framerate detected: {AverageFPS:F1} FPS (target: {_lowFramerateThreshold:F1} FPS)",
                            _lowFramerateThreshold / AverageFPS
                        );
                    }
                    
                    // Notify listeners
                    OnMetricsUpdated?.Invoke(GetCurrentMetrics());
                }
                
                yield return null;
            }
        }

        private IEnumerator MemoryMonitoringCoroutine() {
            while (IsActive) {
                // Get memory usage
                CurrentMemoryUsageMB = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
                PeakMemoryUsageMB = Mathf.Max(PeakMemoryUsageMB, CurrentMemoryUsageMB);
                
                // Calculate percentage
                long totalSystemMemory = SystemInfo.systemMemorySize;
                CurrentMemoryUsagePercentage = (CurrentMemoryUsageMB / totalSystemMemory) * 100f;
                
                // Store history
                _memoryUsageHistory.Enqueue(CurrentMemoryUsageMB);
                
                // Trim history if needed
                while (_memoryUsageHistory.Count > _maxHistorySamples) {
                    _memoryUsageHistory.Dequeue();
                }
                
                // Check for high memory usage
                if (_autoLogWarnings && CurrentMemoryUsagePercentage > _highMemoryThreshold) {
                    ReportPerformanceIssue(
                        PerformanceIssueType.HighMemoryUsage,
                        $"High memory usage detected: {CurrentMemoryUsagePercentage:F1}% (threshold: {_highMemoryThreshold:F1}%)",
                        CurrentMemoryUsagePercentage / _highMemoryThreshold
                    );
                }
                
                yield return new WaitForSeconds(_samplingInterval);
            }
        }

        private IEnumerator GPUMonitoringCoroutine() {
            // Check if GPU memory monitoring is supported
            bool isSupported = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            
            if (!isSupported) {
                LogWarning("GPU memory monitoring is not supported on this platform", LogCategory.Performance);
                yield break;
            }
            
            while (IsActive) {
                try {
                    // Get GPU memory usage using Unity's Profiler
                    CurrentGPUMemoryUsageMB = Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024f * 1024f);
                    
                    // Store history
                    _gpuMemoryUsageHistory.Enqueue(CurrentGPUMemoryUsageMB);
                    
                    // Trim history if needed
                    while (_gpuMemoryUsageHistory.Count > _maxHistorySamples) {
                        _gpuMemoryUsageHistory.Dequeue();
                    }
                } catch (Exception ex) {
                    LogError($"Error monitoring GPU memory: {ex.Message}", LogCategory.Performance);
                    yield break;
                }
                
                yield return new WaitForSeconds(_samplingInterval);
            }
        }

        #endregion

        #region Nested Types

        /// <summary>
        /// Detail level for performance monitoring.
        /// </summary>
        public enum MonitoringDetail {
            /// <summary>Basic metrics only</summary>
            Low,
            /// <summary>Standard metrics and some detailed operation timings</summary>
            Medium,
            /// <summary>All metrics and detailed operation timings</summary>
            High
        }

        /// <summary>
        /// Position for the performance overlay.
        /// </summary>
        public enum OverlayPosition {
            /// <summary>Top-left corner of the screen</summary>
            TopLeft,
            /// <summary>Top-right corner of the screen</summary>
            TopRight,
            /// <summary>Bottom-left corner of the screen</summary>
            BottomLeft,
            /// <summary>Bottom-right corner of the screen</summary>
            BottomRight,
            /// <summary>Center of the screen</summary>
            Center
        }

        /// <summary>
        /// Types of performance issues that can be detected.
        /// </summary>
        public enum PerformanceIssueType {
            /// <summary>Low FPS</summary>
            LowFramerate,
            /// <summary>High memory usage</summary>
            HighMemoryUsage,
            /// <summary>Individual frame taking too long</summary>
            LongFrame,
            /// <summary>Operation taking too long</summary>
            LongOperation,
            /// <summary>High GPU memory usage</summary>
            HighGPUMemoryUsage,
            /// <summary>General performance degradation</summary>
            GeneralPerformanceDegradation,
            /// <summary>Memory leak detected</summary>
            PossibleMemoryLeak
        }

        /// <summary>
        /// Represents a performance issue detected by the monitor.
        /// </summary>
        public class PerformanceIssue {
            /// <summary>Type of performance issue</summary>
            public PerformanceIssueType Type { get; set; }
            
            /// <summary>Description of the issue</summary>
            public string Message { get; set; }
            
            /// <summary>Severity of the issue (higher is more severe)</summary>
            public float Severity { get; set; }
            
            /// <summary>When the issue was detected</summary>
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Snapshot of performance metrics at a point in time.
        /// </summary>
        public class PerformanceMetrics {
            /// <summary>Current FPS</summary>
            public float FPS { get; set; }
            
            /// <summary>Current frame time in milliseconds</summary>
            public float FrameTimeMS { get; set; }
            
            /// <summary>Current memory usage in megabytes</summary>
            public float MemoryUsageMB { get; set; }
            
            /// <summary>Current memory usage as a percentage</summary>
            public float MemoryUsagePercentage { get; set; }
            
            /// <summary>Current GPU memory usage in megabytes</summary>
            public float GPUMemoryUsageMB { get; set; }
            
            /// <summary>Average FPS over the sampling period</summary>
            public float AverageFPS { get; set; }
            
            /// <summary>Whether performance issues have been detected</summary>
            public bool HasPerformanceIssues { get; set; }
            
            /// <summary>Current time scale</summary>
            public float TimeScale { get; set; }
            
            /// <summary>When these metrics were captured</summary>
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Represents a timer for an operation.
        /// </summary>
        public class OperationTimer {
            private readonly Stopwatch _stopwatch = new Stopwatch();
            private readonly PerformanceMonitor _monitor;
            
            /// <summary>Name of the operation being timed</summary>
            public string OperationName { get; }
            
            /// <summary>Category of the operation</summary>
            public string Category { get; }
            
            /// <summary>Whether the timer is currently running</summary>
            public bool IsRunning => _stopwatch.IsRunning;
            
            /// <summary>Current elapsed time in milliseconds</summary>
            public float ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;
            
            /// <summary>
            /// Creates a new operation timer.
            /// </summary>
            /// <param name="operationName">Name of the operation</param>
            /// <param name="category">Category of the operation</param>
            /// <param name="monitor">Parent performance monitor</param>
            public OperationTimer(string operationName, string category, PerformanceMonitor monitor) {
                OperationName = operationName;
                Category = category;
                _monitor = monitor;
                _stopwatch.Start();
            }
            
            /// <summary>
            /// Restarts the timer.
            /// </summary>
            public void Restart() {
                _stopwatch.Restart();
            }
            
            /// <summary>
            /// Ends the timing operation and returns the result.
            /// </summary>
            /// <returns>Operation timing result</returns>
            public OperationResult End() {
                _stopwatch.Stop();
                var result = new OperationResult {
                    OperationName = OperationName,
                    Category = Category,
                    DurationMS = _stopwatch.ElapsedMilliseconds,
                    EndTime = DateTime.UtcNow
                };
                
                return result;
            }
        }

        /// <summary>
        /// Represents the result of a timed operation.
        /// </summary>
        public class OperationResult {
            /// <summary>Name of the operation</summary>
            public string OperationName { get; set; }
            
            /// <summary>Category of the operation</summary>
            public string Category { get; set; }
            
            /// <summary>Duration of the operation in milliseconds</summary>
            public float DurationMS { get; set; }
            
            /// <summary>When the operation ended</summary>
            public DateTime EndTime { get; set; }
        }

        /// <summary>
        /// Represents a scoped timer that automatically times an operation from creation to disposal.
        /// </summary>
        public class OperationScope : IDisposable {
            private readonly PerformanceMonitor _monitor;
            private readonly string _operationName;
            private readonly string _category;
            private bool _disposed;
            
            /// <summary>
            /// Creates a new operation scope.
            /// </summary>
            /// <param name="monitor">Parent performance monitor</param>
            /// <param name="operationName">Name of the operation</param>
            /// <param name="category">Category of the operation</param>
            public OperationScope(PerformanceMonitor monitor, string operationName, string category) {
                _monitor = monitor;
                _operationName = operationName;
                _category = category;
                _monitor.StartOperation(operationName, category);
            }
            
            /// <summary>
            /// Disposes the scope, ending the operation timer.
            /// </summary>
            public void Dispose() {
                if (!_disposed) {
                    _monitor.EndOperation(_operationName, _category);
                    _disposed = true;
                }
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Custom overlay renderer for performance monitoring.
    /// </summary>
    namespace TraversifyGraphics {
        /// <summary>
        /// Renders a custom overlay on the screen.
        /// </summary>
        public class CustomOverlayRenderer : MonoBehaviour {
            private Rect _rect;
            private GUIStyle _style;
            private Func<string> _contentProvider;
            private string _cachedContent;
            private float _lastUpdateTime;
            private const float UpdateInterval = 0.25f;
            
            /// <summary>
            /// Initializes the overlay renderer.
            /// </summary>
            /// <param name="rect">Rectangle defining the overlay position and size</param>
            /// <param name="style">GUI style for the overlay</param>
            /// <param name="contentProvider">Function that provides the overlay content</param>
            public void Initialize(Rect rect, GUIStyle style, Func<string> contentProvider) {
                _rect = rect;
                _style = style;
                _contentProvider = contentProvider;
                _cachedContent = contentProvider?.Invoke() ?? string.Empty;
                _lastUpdateTime = Time.realtimeSinceStartup;
            }
            
            /// <summary>
            /// Updates the overlay content.
            /// </summary>
            public void UpdateOverlay() {
                float currentTime = Time.realtimeSinceStartup;
                if (currentTime - _lastUpdateTime >= UpdateInterval) {
                    _cachedContent = _contentProvider?.Invoke() ?? string.Empty;
                    _lastUpdateTime = currentTime;
                }
            }
            
            /// <summary>
            /// Renders the overlay.
            /// </summary>
            public void OnGUI() {
                if (_style == null) return;
                
                GUI.Box(_rect, _cachedContent, _style);
            }
        }
    }

    #endregion
}