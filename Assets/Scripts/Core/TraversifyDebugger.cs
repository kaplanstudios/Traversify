/*************************************************************************
 *  Traversify – TraversifyDebugger.cs                                   *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 02:45:19 UTC                                     *
 *  Desc   : Advanced debugging and logging system for Traversify with   *
 *           performance tracking, system diagnostics, error handling,   *
 *           and visual debug display. Provides comprehensive logging    *
 *           with category filtering and log file management.            *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Traversify.Core {
    /// <summary>
    /// Advanced debugging system for Traversify with comprehensive logging, 
    /// performance tracking, and visual debug displays.
    /// </summary>
    [DisallowMultipleComponent]
    public class TraversifyDebugger : MonoBehaviour {
        #region Singleton
        
        private static TraversifyDebugger _instance;
        
        /// <summary>
        /// Singleton instance of the debugger.
        /// </summary>
        public static TraversifyDebugger Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<TraversifyDebugger>();
                    if (_instance == null) {
                        GameObject go = new GameObject("TraversifyDebugger");
                        _instance = go.AddComponent<TraversifyDebugger>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Properties
        
        [Header("Logging Options")]
        [Tooltip("Enable or disable logging")]
        [SerializeField] private bool _loggingEnabled = true;
        
        [Tooltip("Log levels to include")]
        [SerializeField] private LogLevel _logLevel = LogLevel.Info;
        
        [Tooltip("Log categories to include (empty for all)")]
        [SerializeField] private List<LogCategory> _enabledCategories = new List<LogCategory>();
        
        [Tooltip("Write logs to file")]
        [SerializeField] private bool _writeToFile = true;
        
        [Tooltip("Maximum log file size in MB")]
        [SerializeField] private int _maxLogFileSizeMB = 10;
        
        [Tooltip("Maximum number of log files to keep")]
        [SerializeField] private int _maxLogFileCount = 5;
        
        [Tooltip("Log file directory (relative to Application.persistentDataPath)")]
        [SerializeField] private string _logDirectory = "Logs";
        
        [Header("Performance Tracking")]
        [Tooltip("Track performance metrics")]
        [SerializeField] private bool _trackPerformance = true;
        
        [Tooltip("Keep performance history")]
        [SerializeField] private bool _keepPerformanceHistory = true;
        
        [Tooltip("Maximum number of performance samples to keep")]
        [SerializeField] private int _maxPerformanceSamples = 1000;
        
        [Tooltip("Warning threshold for frame time (ms)")]
        [SerializeField] private float _frameTimeWarningThreshold = 33.3f; // 30 FPS
        
        [Tooltip("Critical threshold for frame time (ms)")]
        [SerializeField] private float _frameTimeCriticalThreshold = 66.7f; // 15 FPS
        
        [Header("Memory Tracking")]
        [Tooltip("Track memory usage")]
        [SerializeField] private bool _trackMemory = true;
        
        [Tooltip("Memory usage check interval (seconds)")]
        [SerializeField] private float _memoryCheckInterval = 5f;
        
        [Tooltip("Warning threshold for memory usage (MB)")]
        [SerializeField] private float _memoryWarningThreshold = 1000f; // 1 GB
        
        [Tooltip("Critical threshold for memory usage (MB)")]
        [SerializeField] private float _memoryCriticalThreshold = 1800f; // 1.8 GB
        
        [Header("Visual Debug")]
        [Tooltip("Show on-screen debug information")]
        [SerializeField] private bool _showOnScreen = true;
        
        [Tooltip("Show performance graph")]
        [SerializeField] private bool _showPerformanceGraph = true;
        
        [Tooltip("Font for on-screen display")]
        [SerializeField] private Font _debugFont;
        
        [Tooltip("Font size for on-screen display")]
        [Range(8, 24)]
        [SerializeField] private int _fontSize = 14;
        
        [Tooltip("Maximum lines in on-screen log")]
        [SerializeField] private int _maxVisibleLines = 50;
        
        [Tooltip("Log background opacity")]
        [Range(0f, 1f)]
        [SerializeField] private float _logBackgroundOpacity = 0.5f;
        
        [Header("Debug UI Elements")]
        [Tooltip("Canvas for debug UI (created if null)")]
        [SerializeField] private Canvas _debugCanvas;
        
        [Tooltip("Text component for log display")]
        [SerializeField] private Text _logText;
        
        [Tooltip("Scroll rect for log scrolling")]
        [SerializeField] private ScrollRect _logScrollRect;
        
        [Tooltip("Panel for performance graph")]
        [SerializeField] private RectTransform _graphPanel;
        
        [Header("System Status")]
        [Tooltip("Perform system status checks")]
        [SerializeField] private bool _systemStatusChecks = true;
        
        [Tooltip("System status check interval (seconds)")]
        [SerializeField] private float _statusCheckInterval = 10f;
        
        [Tooltip("Auto-recovery for non-critical issues")]
        [SerializeField] private bool _autoRecovery = true;
        
        #endregion
        
        #region Internal Fields
        
        private StreamWriter _logFileWriter;
        private string _logFilePath;
        private long _currentLogFileSize = 0;
        private bool _isInitialized = false;
        
        private List<LogEntry> _logBuffer = new List<LogEntry>();
        private Dictionary<string, Stopwatch> _timers = new Dictionary<string, Stopwatch>();
        private Dictionary<string, TimingResult> _timingResults = new Dictionary<string, TimingResult>();
        
        private Dictionary<string, Queue<float>> _performanceHistory = new Dictionary<string, Queue<float>>();
        private float _lastMemoryCheckTime = 0f;
        private float _lastStatusCheckTime = 0f;
        private float _lastLogUpdateTime = 0f;
        
        private GameObject _logPanel;
        private GameObject _graphContainer;
        private List<GameObject> _graphBars = new List<GameObject>();
        
        private System.Diagnostics.Stopwatch _startupTimer = new System.Diagnostics.Stopwatch();
        private int _criticalErrorCount = 0;
        private int _warningCount = 0;
        private bool _debugUIInitialized = false;
        
        private readonly object _logLock = new object();
        private readonly object _timerLock = new object();
        
        // Track active operation counters
        private Dictionary<string, int> _activeOperations = new Dictionary<string, int>();
        
        // Thread safety for UI updates
        private Queue<Action> _mainThreadActions = new Queue<Action>();
        
        #endregion
        
        #region Initialization & Lifecycle
        
        private void Awake() {
            // Singleton enforcement
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            _startupTimer.Start();
            
            // Initialize logging system
            InitializeLogger();
            
            // Register Unity log callback
            Application.logMessageReceived += HandleUnityLogMessage;
        }
        
        private void Start() {
            // Set up UI components if enabled
            if (_showOnScreen) {
                InitializeDebugUI();
            }
            
            _startupTimer.Stop();
            LogInternal($"Traversify Debugger initialized in {_startupTimer.ElapsedMilliseconds}ms", LogLevel.Info, LogCategory.System);
            
            // Log system information
            LogSystemInfo();
            
            _isInitialized = true;
        }
        
        private void OnEnable() {
            // Start background update coroutine
            StartCoroutine(BackgroundUpdateCoroutine());
        }
        
        private void OnDisable() {
            StopAllCoroutines();
            CloseLogFile();
        }
        
        private void OnDestroy() {
            StopAllCoroutines();
            CloseLogFile();
            Application.logMessageReceived -= HandleUnityLogMessage;
            
            // Clean up UI
            if (_logPanel != null) {
                Destroy(_logPanel);
            }
            if (_graphContainer != null) {
                Destroy(_graphContainer);
            }
        }
        
        private void Update() {
            // Performance tracking
            if (_trackPerformance) {
                TrackFrameTime();
            }
            
            // Process main thread actions
            ProcessMainThreadActions();
            
            // Update on-screen log if needed
            if (_showOnScreen && Time.time - _lastLogUpdateTime > 0.25f) {
                UpdateLogDisplay();
                _lastLogUpdateTime = Time.time;
            }
        }
        
        /// <summary>
        /// Initialize the logging system, creating log directory and file.
        /// </summary>
        private void InitializeLogger() {
            if (_writeToFile) {
                try {
                    // Create log directory
                    string baseDir = Application.persistentDataPath;
                    string logDir = Path.Combine(baseDir, _logDirectory);
                    
                    if (!Directory.Exists(logDir)) {
                        Directory.CreateDirectory(logDir);
                    }
                    
                    // Clean old log files if needed
                    CleanOldLogFiles(logDir);
                    
                    // Create new log file
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string filename = $"Traversify_{timestamp}.log";
                    _logFilePath = Path.Combine(logDir, filename);
                    
                    _logFileWriter = new StreamWriter(_logFilePath, true, Encoding.UTF8);
                    _logFileWriter.AutoFlush = true;
                    
                    // Write log header
                    _logFileWriter.WriteLine("=====================================================");
                    _logFileWriter.WriteLine($"Traversify Log - Started on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    _logFileWriter.WriteLine($"System: {SystemInfo.operatingSystem} | Device: {SystemInfo.deviceModel} | User: dkaplan73");
                    _logFileWriter.WriteLine("=====================================================");
                    _logFileWriter.WriteLine();
                    
                    _currentLogFileSize = 0;
                }
                catch (Exception ex) {
                    Debug.LogError($"Failed to initialize log file: {ex.Message}");
                    _writeToFile = false;
                }
            }
        }
        
        /// <summary>
        /// Initialize debug UI components for on-screen display.
        /// </summary>
        private void InitializeDebugUI() {
            if (_debugUIInitialized) return;
            
            try {
                // Create or use canvas
                if (_debugCanvas == null) {
                    GameObject canvasObj = new GameObject("DebugCanvas");
                    _debugCanvas = canvasObj.AddComponent<Canvas>();
                    _debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _debugCanvas.sortingOrder = 100; // Always on top
                    canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    canvasObj.AddComponent<GraphicRaycaster>();
                    DontDestroyOnLoad(canvasObj);
                }
                
                // Create log panel
                _logPanel = new GameObject("LogPanel");
                _logPanel.transform.SetParent(_debugCanvas.transform, false);
                
                RectTransform logPanelRect = _logPanel.AddComponent<RectTransform>();
                logPanelRect.anchorMin = new Vector2(0, 0);
                logPanelRect.anchorMax = new Vector2(0.3f, 0.7f);
                logPanelRect.offsetMin = new Vector2(10, 10);
                logPanelRect.offsetMax = new Vector2(-10, -10);
                
                Image logPanelImage = _logPanel.AddComponent<Image>();
                logPanelImage.color = new Color(0, 0, 0, _logBackgroundOpacity);
                
                // Create scroll view
                GameObject scrollViewObj = new GameObject("ScrollView");
                scrollViewObj.transform.SetParent(_logPanel.transform, false);
                
                RectTransform scrollViewRect = scrollViewObj.AddComponent<RectTransform>();
                scrollViewRect.anchorMin = new Vector2(0, 0);
                scrollViewRect.anchorMax = new Vector2(1, 1);
                scrollViewRect.offsetMin = new Vector2(5, 5);
                scrollViewRect.offsetMax = new Vector2(-5, -5);
                
                _logScrollRect = scrollViewObj.AddComponent<ScrollRect>();
                
                // Create content container
                GameObject contentObj = new GameObject("Content");
                contentObj.transform.SetParent(scrollViewObj.transform, false);
                
                RectTransform contentRect = contentObj.AddComponent<RectTransform>();
                contentRect.anchorMin = new Vector2(0, 1);
                contentRect.anchorMax = new Vector2(1, 1);
                contentRect.pivot = new Vector2(0.5f, 1);
                contentRect.offsetMin = new Vector2(5, -1000);
                contentRect.offsetMax = new Vector2(-5, 0);
                
                _logScrollRect.content = contentRect;
                _logScrollRect.vertical = true;
                _logScrollRect.horizontal = false;
                _logScrollRect.scrollSensitivity = 20;
                _logScrollRect.movementType = ScrollRect.MovementType.Clamped;
                
                // Create log text
                GameObject textObj = new GameObject("LogText");
                textObj.transform.SetParent(contentRect, false);
                
                RectTransform textRect = textObj.AddComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0, 0);
                textRect.anchorMax = new Vector2(1, 1);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                
                _logText = textObj.AddComponent<Text>();
                _logText.font = _debugFont ? _debugFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
                _logText.fontSize = _fontSize;
                _logText.color = Color.white;
                _logText.supportRichText = true;
                _logText.verticalOverflow = VerticalWrapMode.Overflow;
                _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
                
                // Add mask to scroll view
                scrollViewObj.AddComponent<Mask>().showMaskGraphic = false;
                
                // Create performance graph if enabled
                if (_showPerformanceGraph) {
                    CreatePerformanceGraph();
                }
                
                _debugUIInitialized = true;
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to initialize debug UI: {ex.Message}");
                _showOnScreen = false;
            }
        }
        
        /// <summary>
        /// Create performance graph UI components.
        /// </summary>
        private void CreatePerformanceGraph() {
            _graphContainer = new GameObject("PerformanceGraph");
            _graphContainer.transform.SetParent(_debugCanvas.transform, false);
            
            RectTransform graphRect = _graphContainer.AddComponent<RectTransform>();
            graphRect.anchorMin = new Vector2(0.7f, 0);
            graphRect.anchorMax = new Vector2(1, 0.25f);
            graphRect.offsetMin = new Vector2(10, 10);
            graphRect.offsetMax = new Vector2(-10, -10);
            
            Image graphImage = _graphContainer.AddComponent<Image>();
            graphImage.color = new Color(0, 0, 0, _logBackgroundOpacity);
            
            // Create graph title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(_graphContainer.transform, false);
            
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.9f);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.offsetMin = new Vector2(5, 0);
            titleRect.offsetMax = new Vector2(-5, 0);
            
            Text titleText = titleObj.AddComponent<Text>();
            titleText.font = _debugFont ? _debugFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = _fontSize;
            titleText.color = Color.white;
            titleText.text = "Performance (ms/frame)";
            titleText.alignment = TextAnchor.MiddleCenter;
            
            // Create graph panel
            _graphPanel = new GameObject("GraphPanel").AddComponent<RectTransform>();
            _graphPanel.transform.SetParent(_graphContainer.transform, false);
            _graphPanel.anchorMin = new Vector2(0, 0);
            _graphPanel.anchorMax = new Vector2(1, 0.9f);
            _graphPanel.offsetMin = new Vector2(5, 5);
            _graphPanel.offsetMax = new Vector2(-5, -5);
            
            // Create frame time threshold lines
            CreateThresholdLine(_graphPanel, _frameTimeWarningThreshold, new Color(1, 0.7f, 0, 0.5f));
            CreateThresholdLine(_graphPanel, _frameTimeCriticalThreshold, new Color(1, 0, 0, 0.5f));
            
            // Initialize bar array
            for (int i = 0; i < 60; i++) {
                GameObject bar = new GameObject($"Bar_{i}");
                bar.transform.SetParent(_graphPanel, false);
                
                RectTransform barRect = bar.AddComponent<RectTransform>();
                float width = _graphPanel.rect.width / 60f;
                barRect.anchorMin = new Vector2((float)i / 60f, 0);
                barRect.anchorMax = new Vector2((float)(i + 1) / 60f, 0);
                barRect.offsetMin = new Vector2(1, 0);
                barRect.offsetMax = new Vector2(-1, 0);
                
                Image barImage = bar.AddComponent<Image>();
                barImage.color = Color.green;
                
                _graphBars.Add(bar);
            }
        }
        
        /// <summary>
        /// Create a threshold line for the performance graph.
        /// </summary>
        private void CreateThresholdLine(RectTransform parent, float thresholdMs, Color color) {
            GameObject line = new GameObject($"Threshold_{thresholdMs}ms");
            line.transform.SetParent(parent, false);
            
            RectTransform lineRect = line.AddComponent<RectTransform>();
            
            // Position based on threshold value (normalized to 0-100ms range)
            float normalizedPos = Mathf.Clamp01(thresholdMs / 100f);
            lineRect.anchorMin = new Vector2(0, normalizedPos);
            lineRect.anchorMax = new Vector2(1, normalizedPos);
            lineRect.sizeDelta = new Vector2(0, 1);
            
            Image lineImage = line.AddComponent<Image>();
            lineImage.color = color;
            
            // Add label
            GameObject label = new GameObject("Label");
            label.transform.SetParent(line.transform, false);
            
            RectTransform labelRect = label.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0, 0);
            labelRect.anchorMax = new Vector2(0, 1);
            labelRect.offsetMin = new Vector2(2, -12);
            labelRect.offsetMax = new Vector2(50, 12);
            
            Text labelText = label.AddComponent<Text>();
            labelText.font = _debugFont ? _debugFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = Mathf.Max(10, _fontSize - 2);
            labelText.text = $"{thresholdMs}ms";
            labelText.color = color;
            labelText.alignment = TextAnchor.MiddleLeft;
        }
        
        /// <summary>
        /// Log system information.
        /// </summary>
        private void LogSystemInfo() {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("System Information:");
            sb.AppendLine($"  OS: {SystemInfo.operatingSystem}");
            sb.AppendLine($"  Device: {SystemInfo.deviceModel}");
            sb.AppendLine($"  Processor: {SystemInfo.processorType} ({SystemInfo.processorCount} cores)");
            sb.AppendLine($"  GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)");
            sb.AppendLine($"  Memory: {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"  Unity Version: {Application.unityVersion}");
            sb.AppendLine($"  Product: {Application.productName} ({Application.version})");
            sb.AppendLine($"  Current Date: 2025-06-27 02:45:19");
            sb.AppendLine($"  Current User: dkaplan73");
            
            LogInternal(sb.ToString(), LogLevel.Info, LogCategory.System);
        }
        
        /// <summary>
        /// Clean old log files to maintain maximum file count.
        /// </summary>
        private void CleanOldLogFiles(string logDir) {
            try {
                DirectoryInfo di = new DirectoryInfo(logDir);
                FileInfo[] logFiles = di.GetFiles("Traversify_*.log")
                    .OrderByDescending(f => f.CreationTime)
                    .ToArray();
                
                if (logFiles.Length >= _maxLogFileCount) {
                    for (int i = _maxLogFileCount - 1; i < logFiles.Length; i++) {
                        logFiles[i].Delete();
                    }
                }
            }
            catch (Exception ex) {
                Debug.LogWarning($"Failed to clean old log files: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Close the current log file.
        /// </summary>
        private void CloseLogFile() {
            if (_logFileWriter != null) {
                try {
                    _logFileWriter.WriteLine();
                    _logFileWriter.WriteLine("=====================================================");
                    _logFileWriter.WriteLine($"Traversify Log - Closed on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    _logFileWriter.WriteLine("=====================================================");
                    
                    _logFileWriter.Close();
                    _logFileWriter.Dispose();
                    _logFileWriter = null;
                }
                catch (Exception ex) {
                    Debug.LogError($"Error closing log file: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Background update coroutine for periodic checks.
        /// </summary>
        private IEnumerator BackgroundUpdateCoroutine() {
            WaitForSeconds wait = new WaitForSeconds(1f);
            
            while (true) {
                // Memory check
                if (_trackMemory && Time.time - _lastMemoryCheckTime >= _memoryCheckInterval) {
                    CheckMemoryUsage();
                    _lastMemoryCheckTime = Time.time;
                }
                
                // System status check
                if (_systemStatusChecks && Time.time - _lastStatusCheckTime >= _statusCheckInterval) {
                    CheckSystemStatus();
                    _lastStatusCheckTime = Time.time;
                }
                
                yield return wait;
            }
        }
        
        #endregion
        
        #region Public Logging API
        
        /// <summary>
        /// Log an informational message.
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="category">Log category</param>
        public void Log(string message, LogCategory category = LogCategory.General) {
            if (!_loggingEnabled || _logLevel > LogLevel.Info) return;
            
            if (_enabledCategories.Count > 0 && !_enabledCategories.Contains(category)) {
                return;
            }
            
            LogInternal(message, LogLevel.Info, category);
        }
        
        /// <summary>
        /// Log a warning message.
        /// </summary>
        /// <param name="message">Warning message</param>
        /// <param name="category">Log category</param>
        public void LogWarning(string message, LogCategory category = LogCategory.General) {
            if (!_loggingEnabled || _logLevel > LogLevel.Warning) return;
            
            if (_enabledCategories.Count > 0 && !_enabledCategories.Contains(category)) {
                return;
            }
            
            LogInternal(message, LogLevel.Warning, category);
            _warningCount++;
        }
        
        /// <summary>
        /// Log an error message.
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="category">Log category</param>
        public void LogError(string message, LogCategory category = LogCategory.General) {
            if (!_loggingEnabled || _logLevel > LogLevel.Error) return;
            
            if (_enabledCategories.Count > 0 && !_enabledCategories.Contains(category)) {
                return;
            }
            
            LogInternal(message, LogLevel.Error, category);
        }
        
        /// <summary>
        /// Log a critical error message.
        /// </summary>
        /// <param name="message">Critical error message</param>
        /// <param name="category">Log category</param>
        /// <param name="exception">Optional exception</param>
        public void LogCritical(string message, LogCategory category = LogCategory.General, Exception exception = null) {
            if (!_loggingEnabled) return;
            
            string exMessage = exception != null ? $"\nException: {exception.Message}\nStack Trace: {exception.StackTrace}" : "";
            LogInternal($"{message}{exMessage}", LogLevel.Critical, category);
            _criticalErrorCount++;
        }
        
        /// <summary>
        /// Log a verbose debug message (only shown in debug builds or with verbose logging enabled).
        /// </summary>
        /// <param name="message">Debug message</param>
        /// <param name="category">Log category</param>
        public void LogVerbose(string message, LogCategory category = LogCategory.General) {
            if (!_loggingEnabled || _logLevel > LogLevel.Verbose) return;
            
            if (_enabledCategories.Count > 0 && !_enabledCategories.Contains(category)) {
                return;
            }
            
            #if UNITY_EDITOR || DEVELOPMENT_BUILD || TRAVERSIFY_DEBUG
            LogInternal(message, LogLevel.Verbose, category);
            #endif
        }
        
        /// <summary>
        /// Log with format arguments.
        /// </summary>
        /// <param name="format">Format string</param>
        /// <param name="args">Format arguments</param>
        /// <param name="category">Log category</param>
        public void LogFormat(string format, LogCategory category = LogCategory.General, params object[] args) {
            if (!_loggingEnabled || _logLevel > LogLevel.Info) return;
            
            if (_enabledCategories.Count > 0 && !_enabledCategories.Contains(category)) {
                return;
            }
            
            LogInternal(string.Format(format, args), LogLevel.Info, category);
        }
        
        /// <summary>
        /// Report system status.
        /// </summary>
        /// <param name="component">Component name</param>
        /// <param name="status">Status message</param>
        /// <param name="isOperational">Whether the component is operational</param>
        public void ReportStatus(string component, string status, bool isOperational) {
            LogLevel level = isOperational ? LogLevel.Info : LogLevel.Warning;
            LogInternal($"System Status - {component}: {status}", level, LogCategory.System);
        }
        
        /// <summary>
        /// Report progress for an operation.
        /// </summary>
        /// <param name="operation">Operation name</param>
        /// <param name="progress">Progress (0-1)</param>
        /// <param name="status">Optional status message</param>
        public void ReportProgress(string operation, float progress, string status = null) {
            if (!_loggingEnabled) return;
            
            string message = string.IsNullOrEmpty(status) 
                ? $"{operation}: {progress:P0}"
                : $"{operation}: {progress:P0} - {status}";
            
            LogInternal(message, LogLevel.Info, LogCategory.Process);
        }
        
        /// <summary>
        /// Start tracking an operation.
        /// </summary>
        /// <param name="operation">Operation name</param>
        public void StartOperation(string operation) {
            lock (_logLock) {
                if (!_activeOperations.ContainsKey(operation)) {
                    _activeOperations[operation] = 0;
                }
                
                _activeOperations[operation]++;
                
                LogInternal($"Operation started: {operation} (Active: {_activeOperations[operation]})", 
                    LogLevel.Verbose, LogCategory.Process);
            }
        }
        
        /// <summary>
        /// End tracking an operation.
        /// </summary>
        /// <param name="operation">Operation name</param>
        /// <param name="success">Whether the operation was successful</param>
        public void EndOperation(string operation, bool success = true) {
            lock (_logLock) {
                if (_activeOperations.ContainsKey(operation)) {
                    _activeOperations[operation]--;
                    
                    LogLevel level = success ? LogLevel.Verbose : LogLevel.Warning;
                    LogInternal($"Operation {(success ? "completed" : "failed")}: {operation} (Active: {_activeOperations[operation]})", 
                        level, LogCategory.Process);
                    
                    if (_activeOperations[operation] <= 0) {
                        _activeOperations.Remove(operation);
                    }
                }
            }
        }
        
        #endregion
        
        #region Performance Tracking
        
        /// <summary>
        /// Start a performance timer.
        /// </summary>
        /// <param name="name">Timer name</param>
        public void StartTimer(string name) {
            if (!_trackPerformance) return;
            
            lock (_timerLock) {
                if (_timers.ContainsKey(name)) {
                    _timers[name].Reset();
                    _timers[name].Start();
                }
                else {
                    Stopwatch timer = new Stopwatch();
                    timer.Start();
                    _timers[name] = timer;
                }
            }
        }
        
        /// <summary>
        /// Stop a performance timer and get elapsed time in seconds.
        /// </summary>
        /// <param name="name">Timer name</param>
        /// <returns>Elapsed time in seconds</returns>
        public float StopTimer(string name) {
            if (!_trackPerformance) return 0f;
            
            lock (_timerLock) {
                if (_timers.TryGetValue(name, out Stopwatch timer)) {
                    timer.Stop();
                    float seconds = (float)timer.Elapsed.TotalSeconds;
                    
                    // Record timing result
                    if (!_timingResults.ContainsKey(name)) {
                        _timingResults[name] = new TimingResult();
                    }
                    
                    var result = _timingResults[name];
                    result.callCount++;
                    result.totalTime += seconds;
                    result.lastTime = seconds;
                    
                    if (seconds < result.minTime || result.minTime == 0) {
                        result.minTime = seconds;
                    }
                    
                    if (seconds > result.maxTime) {
                        result.maxTime = seconds;
                    }
                    
                    // Keep history if enabled
                    if (_keepPerformanceHistory) {
                        if (!_performanceHistory.ContainsKey(name)) {
                            _performanceHistory[name] = new Queue<float>(_maxPerformanceSamples);
                        }
                        
                        var history = _performanceHistory[name];
                        if (history.Count >= _maxPerformanceSamples) {
                            history.Dequeue();
                        }
                        
                        history.Enqueue(seconds);
                    }
                    
                    // Log if significantly slow
                    if (seconds > 0.1f) { // More than 100ms
                        LogVerbose($"Performance: {name} took {seconds:F3}s", LogCategory.Performance);
                    }
                    
                    return seconds;
                }
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Get a timing result for a specific timer.
        /// </summary>
        /// <param name="name">Timer name</param>
        /// <returns>Timing result</returns>
        public TimingResult GetTimingResult(string name) {
            lock (_timerLock) {
                if (_timingResults.TryGetValue(name, out TimingResult result)) {
                    return result;
                }
                
                return null;
            }
        }
        
        /// <summary>
        /// Get all timing results.
        /// </summary>
        /// <returns>Dictionary of timing results</returns>
        public Dictionary<string, TimingResult> GetAllTimingResults() {
            lock (_timerLock) {
                // Return a copy
                return new Dictionary<string, TimingResult>(_timingResults);
            }
        }
        
        /// <summary>
        /// Get performance history for a specific metric.
        /// </summary>
        /// <param name="name">Metric name</param>
        /// <returns>Array of historical values</returns>
        public float[] GetPerformanceHistory(string name) {
            lock (_timerLock) {
                if (_performanceHistory.TryGetValue(name, out Queue<float> history)) {
                    return history.ToArray();
                }
                
                return new float[0];
            }
        }
        
        /// <summary>
        /// Track frame time for performance monitoring.
        /// </summary>
        private void TrackFrameTime() {
            float frameTime = Time.unscaledDeltaTime * 1000f; // Convert to milliseconds
            
            // Track frame time history
            if (_keepPerformanceHistory) {
                if (!_performanceHistory.ContainsKey("FrameTime")) {
                    _performanceHistory["FrameTime"] = new Queue<float>(_maxPerformanceSamples);
                }
                
                var history = _performanceHistory["FrameTime"];
                if (history.Count >= _maxPerformanceSamples) {
                    history.Dequeue();
                }
                
                history.Enqueue(frameTime);
            }
            
            // Update graph if visible
            if (_showOnScreen && _showPerformanceGraph) {
                UpdatePerformanceGraph(frameTime);
            }
            
            // Log warnings for slow frames
            if (frameTime > _frameTimeCriticalThreshold) {
                LogWarning($"Critical frame time: {frameTime:F1}ms", LogCategory.Performance);
            }
            else if (frameTime > _frameTimeWarningThreshold) {
                LogVerbose($"Slow frame time: {frameTime:F1}ms", LogCategory.Performance);
            }
        }
        
        /// <summary>
        /// Update performance graph with current frame time.
        /// </summary>
        /// <param name="frameTime">Current frame time in milliseconds</param>
        private void UpdatePerformanceGraph(float frameTime) {
            if (_graphBars.Count == 0 || _graphPanel == null) return;
            
            // Shift bars to the left
            for (int i = 0; i < _graphBars.Count - 1; i++) {
                Color color = _graphBars[i + 1].GetComponent<Image>().color;
                _graphBars[i].GetComponent<Image>().color = color;
                
                RectTransform rt = _graphBars[i].GetComponent<RectTransform>();
                RectTransform nextRt = _graphBars[i + 1].GetComponent<RectTransform>();
                rt.sizeDelta = nextRt.sizeDelta;
            }
            
            // Add new bar at the end
            float normalizedHeight = Mathf.Clamp01(frameTime / 100f); // Normalize to 0-100ms range
            RectTransform lastBar = _graphBars[_graphBars.Count - 1].GetComponent<RectTransform>();
            lastBar.sizeDelta = new Vector2(lastBar.sizeDelta.x, _graphPanel.rect.height * normalizedHeight);
            
            // Set color based on performance thresholds
            Image lastBarImage = _graphBars[_graphBars.Count - 1].GetComponent<Image>();
            if (frameTime > _frameTimeCriticalThreshold) {
                lastBarImage.color = Color.red;
            }
            else if (frameTime > _frameTimeWarningThreshold) {
                lastBarImage.color = new Color(1, 0.7f, 0); // Orange
            }
            else {
                lastBarImage.color = Color.green;
            }
        }
        
        /// <summary>
        /// Check memory usage and log warnings if thresholds are exceeded.
        /// </summary>
        private void CheckMemoryUsage() {
            if (!_trackMemory) return;
            
            try {
                // Get memory usage
                long totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // MB
                long managedMemory = totalMemory;
                
                // Add Unity profiler data if available
                #if UNITY_EDITOR
                long totalAllocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
                long totalReserved = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
                long unusedReserved = UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong() / (1024 * 1024);
                totalMemory = totalAllocated;
                #endif
                
                // Track memory history
                if (_keepPerformanceHistory) {
                    if (!_performanceHistory.ContainsKey("Memory")) {
                        _performanceHistory["Memory"] = new Queue<float>(_maxPerformanceSamples);
                    }
                    
                    var history = _performanceHistory["Memory"];
                    if (history.Count >= _maxPerformanceSamples) {
                        history.Dequeue();
                    }
                    
                    history.Enqueue(totalMemory);
                }
                
                // Log warnings if thresholds are exceeded
                if (totalMemory > _memoryCriticalThreshold) {
                    LogWarning($"Critical memory usage: {totalMemory}MB", LogCategory.Performance);
                    
                    // Force garbage collection if auto-recovery is enabled
                    if (_autoRecovery) {
                        GC.Collect();
                        LogWarning("Forced garbage collection due to critical memory usage", LogCategory.Performance);
                    }
                }
                else if (totalMemory > _memoryWarningThreshold) {
                    LogWarning($"High memory usage: {totalMemory}MB", LogCategory.Performance);
                }
                else {
                    LogVerbose($"Memory usage: {totalMemory}MB (Managed: {managedMemory}MB)", LogCategory.Performance);
                }
            }
            catch (Exception ex) {
                LogError($"Error checking memory usage: {ex.Message}", LogCategory.System);
            }
        }
        
        /// <summary>
        /// Check system status and log warnings if issues are detected.
        /// </summary>
        private void CheckSystemStatus() {
            if (!_systemStatusChecks) return;
            
            try {
                StringBuilder status = new StringBuilder();
                bool hasIssues = false;
                
                // Check memory
                long totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // MB
                if (totalMemory > _memoryCriticalThreshold) {
                    status.AppendLine($"⚠️ Critical memory usage: {totalMemory}MB");
                    hasIssues = true;
                }
                else if (totalMemory > _memoryWarningThreshold) {
                    status.AppendLine($"⚠️ High memory usage: {totalMemory}MB");
                    hasIssues = true;
                }
                
                // Check active operations
                int totalOperations = 0;
                lock (_logLock) {
                    foreach (var op in _activeOperations) {
                        if (op.Value > 0) {
                            status.AppendLine($"🔄 Active operation: {op.Key} ({op.Value})");
                            totalOperations += op.Value;
                        }
                    }
                }
                
                if (totalOperations > 10) {
                    status.AppendLine($"⚠️ High number of active operations: {totalOperations}");
                    hasIssues = true;
                }
                
                // Check error counts
                if (_criticalErrorCount > 0) {
                    status.AppendLine($"⚠️ Critical errors: {_criticalErrorCount}");
                    hasIssues = true;
                }
                
                if (_warningCount > 20) {
                    status.AppendLine($"⚠️ High warning count: {_warningCount}");
                    hasIssues = true;
                }
                
                // Log results
                if (hasIssues) {
                    LogWarning($"System status check found issues:\n{status}", LogCategory.System);
                    
                    // Auto-recovery actions
                    if (_autoRecovery) {
                        if (totalMemory > _memoryWarningThreshold) {
                            GC.Collect();
                            LogWarning("Forced garbage collection due to high memory usage", LogCategory.System);
                        }
                        
                        // Other recovery actions could be added here
                    }
                }
                else {
                    LogVerbose("System status check: All systems operational", LogCategory.System);
                }
            }
            catch (Exception ex) {
                LogError($"Error checking system status: {ex.Message}", LogCategory.System);
            }
        }
        
        #endregion
        
        #region Internal Logging
        
        /// <summary>
        /// Internal log implementation.
        /// </summary>
        /// <param name="message">Message to log</param>
        /// <param name="level">Log level</param>
        /// <param name="category">Log category</param>
        private void LogInternal(string message, LogLevel level, LogCategory category) {
            lock (_logLock) {
                // Create log entry
                LogEntry entry = new LogEntry {
                    message = message,
                    level = level,
                    category = category,
                    timestamp = DateTime.Now,
                    threadId = Thread.CurrentThread.ManagedThreadId
                };
                
                // Add to buffer
                _logBuffer.Add(entry);
                
                // Trim buffer if needed
                if (_logBuffer.Count > _maxVisibleLines * 2) {
                    _logBuffer.RemoveRange(0, _logBuffer.Count - _maxVisibleLines);
                }
                
                // Write to file
                WriteToLogFile(entry);
                
                // Forward to Unity console
                ForwardToUnityConsole(entry);
                
                // Schedule UI update if needed
                if (_showOnScreen) {
                    QueueMainThreadAction(() => {
                        UpdateLogDisplay();
                    });
                }
            }
        }
        
        /// <summary>
        /// Write a log entry to the log file.
        /// </summary>
        /// <param name="entry">Log entry</param>
        private void WriteToLogFile(LogEntry entry) {
            if (!_writeToFile || _logFileWriter == null) return;
            
            try {
                string levelPrefix = GetLevelPrefix(entry.level);
                string timestamp = entry.timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string logLine = $"[{timestamp}] [{levelPrefix}] [{entry.category}] [{entry.threadId}] {entry.message}";
                
                _logFileWriter.WriteLine(logLine);
                _currentLogFileSize += logLine.Length + 2; // +2 for line ending
                
                // Check if we need to rotate log file
                if (_currentLogFileSize > _maxLogFileSizeMB * 1024 * 1024) {
                    RotateLogFile();
                }
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to write to log file: {ex.Message}");
                _writeToFile = false;
            }
        }
        
        /// <summary>
        /// Rotate log file when it reaches maximum size.
        /// </summary>
        private void RotateLogFile() {
            try {
                // Close current log file
                _logFileWriter.Close();
                _logFileWriter.Dispose();
                
                // Create new log file
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string baseDir = Path.GetDirectoryName(_logFilePath);
                string filename = $"Traversify_{timestamp}.log";
                _logFilePath = Path.Combine(baseDir, filename);
                
                _logFileWriter = new StreamWriter(_logFilePath, true, Encoding.UTF8);
                _logFileWriter.AutoFlush = true;
                
                // Write log header
                _logFileWriter.WriteLine("=====================================================");
                _logFileWriter.WriteLine($"Traversify Log - Started on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _logFileWriter.WriteLine($"System: {SystemInfo.operatingSystem} | Device: {SystemInfo.deviceModel}");
                _logFileWriter.WriteLine("=====================================================");
                _logFileWriter.WriteLine();
                
                _currentLogFileSize = 0;
                
                // Clean old log files
                CleanOldLogFiles(baseDir);
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to rotate log file: {ex.Message}");
                _writeToFile = false;
            }
        }
        
        /// <summary>
        /// Forward a log entry to the Unity console.
        /// </summary>
        /// <param name="entry">Log entry</param>
        private void ForwardToUnityConsole(LogEntry entry) {
            string message = $"[{entry.category}] {entry.message}";
            
            switch (entry.level) {
                case LogLevel.Verbose:
                    Debug.Log($"<color=#7f7f7f>{message}</color>");
                    break;
                case LogLevel.Info:
                    Debug.Log(message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    Debug.LogError(message);
                    break;
            }
        }
        
        /// <summary>
        /// Handle Unity log messages.
        /// </summary>
        /// <param name="logString">Log message</param>
        /// <param name="stackTrace">Stack trace</param>
        /// <param name="type">Log type</param>
        private void HandleUnityLogMessage(string logString, string stackTrace, LogType type) {
            // Skip log messages that originated from this class to avoid infinite loops
            if (logString.StartsWith("[") && (
                logString.Contains(nameof(LogCategory.General)) || 
                logString.Contains(nameof(LogCategory.System)) ||
                logString.Contains(nameof(LogCategory.AI)) ||
                logString.Contains(nameof(LogCategory.Performance)))) {
                return;
            }
            
            // Add to our log
            LogLevel level;
            switch (type) {
                case LogType.Warning:
                    level = LogLevel.Warning;
                    break;
                case LogType.Error:
                case LogType.Exception:
                    level = LogLevel.Error;
                    break;
                case LogType.Assert:
                    level = LogLevel.Critical;
                    break;
                default:
                    level = LogLevel.Info;
                    break;
            }
            
            if (level != LogLevel.Info || !logString.StartsWith("[")) { // Skip already formatted info logs
                LogInternal($"Unity: {logString}", level, LogCategory.General);
            }
        }
        
        /// <summary>
        /// Update the on-screen log display.
        /// </summary>
        private void UpdateLogDisplay() {
            if (!_showOnScreen || _logText == null) return;
            
            lock (_logLock) {
                // Get the last N entries
                int startIndex = Math.Max(0, _logBuffer.Count - _maxVisibleLines);
                int count = Math.Min(_maxVisibleLines, _logBuffer.Count);
                
                // Build log text
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < count; i++) {
                    LogEntry entry = _logBuffer[startIndex + i];
                    sb.AppendLine(FormatLogEntryForDisplay(entry));
                }
                
                // Update text
                _logText.text = sb.ToString();
                
                // Scroll to bottom if needed
                Canvas.ForceUpdateCanvases();
                if (_logScrollRect != null) {
                    _logScrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }
        
        /// <summary>
        /// Format a log entry for display.
        /// </summary>
        /// <param name="entry">Log entry</param>
        /// <returns>Formatted log entry</returns>
        private string FormatLogEntryForDisplay(LogEntry entry) {
            // Colors for different log levels
            string colorStart = "";
            switch (entry.level) {
                case LogLevel.Verbose:
                    colorStart = "<color=#7f7f7f>"; // Gray
                    break;
                case LogLevel.Info:
                    colorStart = "<color=white>"; // White
                    break;
                case LogLevel.Warning:
                    colorStart = "<color=#ffcc00>"; // Yellow
                    break;
                case LogLevel.Error:
                    colorStart = "<color=#ff6666>"; // Light Red
                    break;
                case LogLevel.Critical:
                    colorStart = "<color=#ff0000>"; // Red
                    break;
            }
            
            string colorEnd = entry.level != LogLevel.Info ? "</color>" : "";
            string timestamp = entry.timestamp.ToString("HH:mm:ss");
            
            return $"{colorStart}[{timestamp}] [{entry.category}] {entry.message}{colorEnd}";
        }
        
        /// <summary>
        /// Get a prefix for a log level.
        /// </summary>
        /// <param name="level">Log level</param>
        /// <returns>Level prefix</returns>
        private string GetLevelPrefix(LogLevel level) {
            switch (level) {
                case LogLevel.Verbose:
                    return "VERB";
                case LogLevel.Info:
                    return "INFO";
                case LogLevel.Warning:
                    return "WARN";
                case LogLevel.Error:
                    return "ERR ";
                case LogLevel.Critical:
                    return "CRIT";
                default:
                    return "    ";
            }
        }
        
        /// <summary>
        /// Queue an action to be executed on the main thread.
        /// </summary>
        /// <param name="action">Action to execute</param>
        private void QueueMainThreadAction(Action action) {
            lock (_mainThreadActions) {
                _mainThreadActions.Enqueue(action);
            }
        }
        
        /// <summary>
        /// Process actions queued for execution on the main thread.
        /// </summary>
        private void ProcessMainThreadActions() {
            lock (_mainThreadActions) {
                while (_mainThreadActions.Count > 0) {
                    Action action = _mainThreadActions.Dequeue();
                    try {
                        action.Invoke();
                    }
                    catch (Exception ex) {
                        Debug.LogError($"Error executing main thread action: {ex.Message}");
                    }
                }
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Get a string with system status information.
        /// </summary>
        /// <returns>System status string</returns>
        public string GetSystemStatus() {
            StringBuilder sb = new StringBuilder();
            
            // Device info
            sb.AppendLine("System Status:");
            sb.AppendLine($"  Application: {Application.productName} {Application.version}");
            sb.AppendLine($"  Unity Version: {Application.unityVersion}");
            sb.AppendLine($"  Device: {SystemInfo.deviceModel}");
            sb.AppendLine($"  OS: {SystemInfo.operatingSystem}");
            
            // Performance
            sb.AppendLine("Performance:");
            sb.AppendLine($"  FPS: {(1.0f / Time.smoothDeltaTime):F1}");
            sb.AppendLine($"  Memory: {GC.GetTotalMemory(false) / (1024 * 1024)}MB");
            
            // Add frame time history
            if (_performanceHistory.TryGetValue("FrameTime", out Queue<float> frameTimeHistory) && frameTimeHistory.Count > 0) {
                float avgFrameTime = frameTimeHistory.Average();
                float minFrameTime = frameTimeHistory.Min();
                float maxFrameTime = frameTimeHistory.Max();
                sb.AppendLine($"  Frame Time: {avgFrameTime:F2}ms (Min: {minFrameTime:F2}ms, Max: {maxFrameTime:F2}ms)");
            }
            
            // Add error counts
            sb.AppendLine("Errors:");
            sb.AppendLine($"  Critical: {_criticalErrorCount}");
            sb.AppendLine($"  Warnings: {_warningCount}");
            
            // Add active operations
            sb.AppendLine("Active Operations:");
            lock (_logLock) {
                if (_activeOperations.Count == 0) {
                    sb.AppendLine("  None");
                }
                else {
                    foreach (var op in _activeOperations) {
                        sb.AppendLine($"  {op.Key}: {op.Value}");
                    }
                }
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Export logs to a file.
        /// </summary>
        /// <param name="path">Export path</param>
        /// <returns>Whether export was successful</returns>
        public bool ExportLogs(string path) {
            try {
                if (string.IsNullOrEmpty(path)) {
                    path = Path.Combine(Application.persistentDataPath, "Exports", $"TraversifyExport_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                }
                
                // Ensure directory exists
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }
                
                // Write logs to file
                using (StreamWriter writer = new StreamWriter(path, false, Encoding.UTF8)) {
                    writer.WriteLine("=====================================================");
                    writer.WriteLine($"Traversify Log Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"System: {SystemInfo.operatingSystem} | Device: {SystemInfo.deviceModel}");
                    writer.WriteLine("=====================================================");
                    writer.WriteLine();
                    
                    lock (_logLock) {
                        foreach (LogEntry entry in _logBuffer) {
                            string levelPrefix = GetLevelPrefix(entry.level);
                            string timestamp = entry.timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
                            writer.WriteLine($"[{timestamp}] [{levelPrefix}] [{entry.category}] {entry.message}");
                        }
                    }
                    
                    writer.WriteLine();
                    writer.WriteLine("=====================================================");
                    writer.WriteLine(GetSystemStatus());
                    writer.WriteLine("=====================================================");
                }
                
                LogInternal($"Logs exported to {path}", LogLevel.Info, LogCategory.System);
                return true;
            }
            catch (Exception ex) {
                LogError($"Failed to export logs: {ex.Message}", LogCategory.System);
                return false;
            }
        }
        
        /// <summary>
        /// Set the logging level.
        /// </summary>
        /// <param name="level">New log level</param>
        public void SetLogLevel(LogLevel level) {
            _logLevel = level;
            LogInternal($"Log level set to {level}", LogLevel.Info, LogCategory.System);
        }
        
        /// <summary>
        /// Set the enabled log categories.
        /// </summary>
        /// <param name="categories">Categories to enable</param>
        public void SetEnabledCategories(params LogCategory[] categories) {
            _enabledCategories.Clear();
            _enabledCategories.AddRange(categories);
            
            if (categories.Length == 0) {
                LogInternal("All log categories enabled", LogLevel.Info, LogCategory.System);
            }
            else {
                LogInternal($"Enabled log categories: {string.Join(", ", categories)}", LogLevel.Info, LogCategory.System);
            }
        }
        
        /// <summary>
        /// Toggle on-screen debug display.
        /// </summary>
        /// <param name="visible">Whether the debug display should be visible</param>
        public void SetDebugDisplayVisible(bool visible) {
            _showOnScreen = visible;
            
            if (_logPanel != null) {
                _logPanel.SetActive(visible);
            }
            
            if (_graphContainer != null) {
                _graphContainer.SetActive(visible && _showPerformanceGraph);
            }
            
            LogInternal($"Debug display {(visible ? "shown" : "hidden")}", LogLevel.Info, LogCategory.System);
        }
        
        /// <summary>
        /// Clear the log buffer.
        /// </summary>
        public void ClearLogs() {
            lock (_logLock) {
                _logBuffer.Clear();
                UpdateLogDisplay();
            }
            
            LogInternal("Logs cleared", LogLevel.Info, LogCategory.System);
        }
        
        /// <summary>
        /// Reset error and warning counters.
        /// </summary>
        public void ResetErrorCounters() {
            _criticalErrorCount = 0;
            _warningCount = 0;
            
            LogInternal("Error counters reset", LogLevel.Info, LogCategory.System);
        }
        
        /// <summary>
        /// Get the count of recent logs by level.
        /// </summary>
        /// <returns>Dictionary with counts by log level</returns>
        public Dictionary<LogLevel, int> GetLogCounts() {
            var counts = new Dictionary<LogLevel, int>();
            
            lock (_logLock) {
                foreach (LogEntry entry in _logBuffer) {
                    if (!counts.ContainsKey(entry.level)) {
                        counts[entry.level] = 0;
                    }
                    
                    counts[entry.level]++;
                }
            }
            
            return counts;
        }
        
        /// <summary>
        /// Get filtered logs by level and/or category.
        /// </summary>
        /// <param name="level">Log level filter</param>
        /// <param name="category">Category filter</param>
        /// <returns>Filtered log entries</returns>
        public LogEntry[] GetFilteredLogs(LogLevel? level = null, LogCategory? category = null) {
            lock (_logLock) {
                return _logBuffer.Where(entry => 
                    (!level.HasValue || entry.level == level.Value) &&
                    (!category.HasValue || entry.category == category.Value)
                ).ToArray();
            }
        }
        
        #endregion
        
        #region Helper Types
        
        /// <summary>
        /// Structure for a log entry.
        /// </summary>
        [Serializable]
        public struct LogEntry {
            public string message;
            public LogLevel level;
            public LogCategory category;
            public DateTime timestamp;
            public int threadId;
        }
        
        /// <summary>
        /// Structure for timing results.
        /// </summary>
        [Serializable]
        public class TimingResult {
            public int callCount;
            public float totalTime;
            public float lastTime;
            public float minTime;
            public float maxTime;
            
            public float AverageTime => callCount > 0 ? totalTime / callCount : 0f;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Log levels for filtering log messages.
    /// </summary>
    public enum LogLevel {
        Verbose = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }
}