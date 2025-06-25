using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Linq;
using System.Runtime; // Corrected namespace for GCSettings
using UnityEngine;
using UnityEngine.Profiling; // Added for Profiler methods
using Debug = UnityEngine.Debug;

namespace Traversify.Core
{
    /// <summary>
    /// Log level enum to determine the severity of log messages
    /// </summary>
    public enum LogLevel
    {
        Debug,      // Detailed debugging information
        Info,       // General information
        Warning,    // Warnings that don't stop execution
        Error,      // Errors that may impact functionality
        Critical    // Critical errors that halt execution
    }

    /// <summary>
    /// Handles debugging, logging, and error tracking for the Traversify system
    /// </summary>
    [ExecuteInEditMode]
    public class TraversifyDebugger : MonoBehaviour
    {
        // Configuration options
        [Header("Debug Settings")]
        [Tooltip("Enable or disable all debugging features")]
        [SerializeField] private bool enableDebugging = true;
        
        [Tooltip("Display debug messages in the Unity console")]
        [SerializeField] private bool logToConsole = true;
        
        [Tooltip("Write debug messages to a log file")]
        [SerializeField] private bool logToFile = false;
        
        [Tooltip("Minimum log level to record")]
        [SerializeField] private LogLevel minimumLogLevel = LogLevel.Info;
        
        [Tooltip("Categories to include in logging (empty = all)")]
        [SerializeField] private List<LogCategory> logCategories = new List<LogCategory>();
        
        [Tooltip("Stack trace depth for error logs")]
        [SerializeField] private int stackTraceDepth = 5;
        
        [Header("Log File Settings")]
        [Tooltip("Path for log files (relative to Application.persistentDataPath)")]
        [SerializeField] private string logFolderPath = "Logs";
        
        [Tooltip("Maximum log file size in MB before rotation")]
        [SerializeField] private float maxLogFileSizeMB = 10f;
        
        [Tooltip("Maximum number of log files to keep")]
        [SerializeField] private int maxLogFileCount = 5;
        
        [Header("Performance Monitoring")]
        [Tooltip("Track performance metrics")]
        [SerializeField] private bool trackPerformance = true;
        
        [Tooltip("Show performance warnings when thresholds are exceeded")]
        [SerializeField] private bool showPerformanceWarnings = true;
        
        [Tooltip("Threshold in ms for slow operations warnings")]
        [SerializeField] private float slowOperationThresholdMs = 100f;

        // Runtime properties
        private Dictionary<string, Stopwatch> timers = new Dictionary<string, Stopwatch>();
        private Dictionary<string, float> performanceMetrics = new Dictionary<string, float>();
        private Dictionary<string, int> errorCounts = new Dictionary<string, int>();
        private StringBuilder logBuffer = new StringBuilder();
        private string logFilePath;
        private DateTime sessionStartTime;
        private int totalLogCount = 0;
        private int errorLogCount = 0;
        private int warningLogCount = 0;
        private bool isInitialized = false;

        // Constants
        private const int LOG_BUFFER_CAPACITY = 4096;
        private const float LOG_FLUSH_INTERVAL_SECONDS = 5.0f;
        private const string LOG_FILE_PREFIX = "traversify_log_";
        private const string LOG_FILE_EXTENSION = ".txt";
        private const string PERFORMANCE_LOG_FILE = "performance_metrics.csv";

        #region Unity Lifecycle Methods

        private void Awake()
        {
            InitializeDebugger();
        }

        private void OnEnable()
        {
            if (!isInitialized)
            {
                InitializeDebugger();
            }
            
            if (Application.isPlaying)
            {
                InvokeRepeating(nameof(FlushLogBuffer), LOG_FLUSH_INTERVAL_SECONDS, LOG_FLUSH_INTERVAL_SECONDS);
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                CancelInvoke(nameof(FlushLogBuffer));
                FlushLogBuffer();
            }
        }

        private void OnDestroy()
        {
            FlushLogBuffer();
            SavePerformanceMetrics();
        }

        private void OnApplicationQuit()
        {
            FlushLogBuffer();
            SavePerformanceMetrics();
            LogSessionSummary();
        }

        #endregion

        #region Initialization

        private void InitializeDebugger()
        {
            if (isInitialized)
                return;

            logBuffer.Clear();
            logBuffer.Capacity = LOG_BUFFER_CAPACITY;
            sessionStartTime = DateTime.Now;
            
            if (logToFile)
            {
                InitializeLogFile();
            }
            
            isInitialized = true;
            
            Log($"Traversify Debugger initialized - {Application.productName} v{Application.version}", LogCategory.System, LogLevel.Info);
            Log($"Unity version: {Application.unityVersion}, Platform: {Application.platform}", LogCategory.System, LogLevel.Debug);
            Log($"System: {SystemInfo.operatingSystem}, Device: {SystemInfo.deviceModel}", LogCategory.System, LogLevel.Debug);
            Log($"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})", LogCategory.System, LogLevel.Debug);
            Log($"CPU: {SystemInfo.processorType}, {SystemInfo.processorCount} cores", LogCategory.System, LogLevel.Debug);
            Log($"Memory: {SystemInfo.systemMemorySize} MB", LogCategory.System, LogLevel.Debug);
        }

        private void InitializeLogFile()
        {
            try
            {
                string baseLogPath = Path.Combine(Application.persistentDataPath, logFolderPath);
                
                if (!Directory.Exists(baseLogPath))
                {
                    Directory.CreateDirectory(baseLogPath);
                }
                
                // Create a timestamped log file
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                logFilePath = Path.Combine(baseLogPath, $"{LOG_FILE_PREFIX}{timestamp}{LOG_FILE_EXTENSION}");
                
                // Write log file header
                StringBuilder header = new StringBuilder();
                header.AppendLine("=================================================================");
                header.AppendLine($"Traversify Log File - Session started: {timestamp}");
                header.AppendLine($"Application: {Application.productName} v{Application.version}");
                header.AppendLine($"Unity version: {Application.unityVersion}, Platform: {Application.platform}");
                header.AppendLine($"System: {SystemInfo.operatingSystem}, Device: {SystemInfo.deviceModel}");
                header.AppendLine("=================================================================");
                header.AppendLine();
                
                File.WriteAllText(logFilePath, header.ToString());
                
                // Perform log rotation if needed
                RotateLogFiles(baseLogPath);
            }
            catch (Exception ex)
            {
                // Fallback to console-only logging if file initialization fails
                logToFile = false;
                Debug.LogError($"Failed to initialize log file: {ex.Message}");
            }
        }

        private void RotateLogFiles(string logDirectory)
        {
            try
            {
                // Get all log files sorted by creation time (oldest first)
                var logFiles = new DirectoryInfo(logDirectory)
                    .GetFiles($"{LOG_FILE_PREFIX}*{LOG_FILE_EXTENSION}")
                    .OrderBy(f => f.CreationTime)
                    .ToArray();
                
                // Delete oldest files if we have too many
                while (logFiles.Length > maxLogFileCount && logFiles.Length > 1)
                {
                    var oldestFile = logFiles[0];
                    oldestFile.Delete();
                    logFiles = logFiles.Skip(1).ToArray();
                }
                
                // Check if current log file is too large
                if (logFiles.Length > 0)
                {
                    var currentLogFile = new FileInfo(logFilePath);
                    if (currentLogFile.Exists && currentLogFile.Length > maxLogFileSizeMB * 1024 * 1024)
                    {
                        // Create a new log file
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        logFilePath = Path.Combine(logDirectory, $"{LOG_FILE_PREFIX}{timestamp}{LOG_FILE_EXTENSION}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during log rotation: {ex.Message}");
            }
        }

        #endregion

        #region Public Logging Methods

        /// <summary>
        /// Log a debug message with specified category
        /// </summary>
        public void Log(string message, LogCategory category = LogCategory.System, LogLevel level = LogLevel.Debug)
        {
            if (!enableDebugging || level < minimumLogLevel)
                return;
                
            if (logCategories.Count > 0 && !logCategories.Contains(category))
                return;
                
            totalLogCount++;
            string formattedMessage = FormatLogMessage(message, category, level);
            
            if (logToConsole)
            {
                switch (level)
                {
                    case LogLevel.Debug:
                    case LogLevel.Info:
                        Debug.Log(formattedMessage);
                        break;
                    case LogLevel.Warning:
                        Debug.LogWarning(formattedMessage);
                        warningLogCount++;
                        break;
                    case LogLevel.Error:
                    case LogLevel.Critical:
                        Debug.LogError(formattedMessage);
                        errorLogCount++;
                        break;
                }
            }
            
            if (logToFile)
            {
                lock (logBuffer)
                {
                    logBuffer.AppendLine(formattedMessage);
                    
                    // Flush immediately for critical errors
                    if (level == LogLevel.Critical)
                    {
                        FlushLogBuffer();
                    }
                }
            }
            
            // Track error types
            if (level >= LogLevel.Error)
            {
                string errorType = category.ToString();
                if (!errorCounts.ContainsKey(errorType))
                {
                    errorCounts[errorType] = 0;
                }
                errorCounts[errorType]++;
            }
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public void LogWarning(string message, LogCategory category = LogCategory.System)
        {
            Log(message, category, LogLevel.Warning);
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public void LogError(string message, LogCategory category = LogCategory.System)
        {
            Log(message, category, LogLevel.Error);
            
            // Capture and include stack trace
            string stackTrace = new System.Diagnostics.StackTrace(1, true).ToString();
            string[] stackFrames = stackTrace.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            
            StringBuilder traceBuilder = new StringBuilder();
            traceBuilder.AppendLine("Stack trace:");
            
            // Limit stack trace depth
            int frames = Math.Min(stackTraceDepth, stackFrames.Length);
            for (int i = 0; i < frames; i++)
            {
                traceBuilder.AppendLine($"  {stackFrames[i].Trim()}");
            }
            
            Log(traceBuilder.ToString(), category, LogLevel.Debug);
        }

        /// <summary>
        /// Log a critical error message
        /// </summary>
        public void LogCritical(string message, LogCategory category = LogCategory.System, bool throwException = false)
        {
            Log(message, category, LogLevel.Critical);
            
            // Capture full stack trace for critical errors
            string stackTrace = new System.Diagnostics.StackTrace(1, true).ToString();
            Log($"Full stack trace:\n{stackTrace}", category, LogLevel.Debug);
            
            if (throwException)
            {
                throw new Exception($"[Traversify/{category}] CRITICAL: {message}");
            }
        }

        /// <summary>
        /// Log an exception with stack trace
        /// </summary>
        public void LogException(Exception ex, LogCategory category = LogCategory.System, LogLevel level = LogLevel.Error)
        {
            Log($"Exception: {ex.GetType().Name} - {ex.Message}", category, level);
            Log($"Stack trace:\n{ex.StackTrace}", category, LogLevel.Debug);
            
            // Log inner exception if present
            if (ex.InnerException != null)
            {
                Log($"Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}", 
                    category, LogLevel.Debug);
            }
        }

        #endregion

        #region Performance Tracking

        /// <summary>
        /// Start a performance timer
        /// </summary>
        public void StartTimer(string name)
        {
            if (!trackPerformance)
                return;
                
            lock (timers)
            {
                if (timers.ContainsKey(name))
                {
                    timers[name].Restart();
                }
                else
                {
                    Stopwatch timer = new Stopwatch();
                    timer.Start();
                    timers[name] = timer;
                }
            }
        }

        /// <summary>
        /// Stop a performance timer and return elapsed time in seconds
        /// </summary>
        public float StopTimer(string name)
        {
            if (!trackPerformance)
                return 0f;
                
            lock (timers)
            {
                if (timers.TryGetValue(name, out Stopwatch timer))
                {
                    timer.Stop();
                    float elapsedSeconds = (float)timer.Elapsed.TotalSeconds;
                    
                    // Record the metric
                    performanceMetrics[name] = elapsedSeconds;
                    
                    // Check if operation was slow
                    float elapsedMs = (float)timer.Elapsed.TotalMilliseconds;
                    if (showPerformanceWarnings && elapsedMs > slowOperationThresholdMs)
                    {
                        LogWarning($"Slow operation detected: {name} took {elapsedMs:F1} ms", LogCategory.Performance);
                    }
                    
                    return elapsedSeconds;
                }
            }
            
            LogWarning($"Attempted to stop timer '{name}' that was never started", LogCategory.Performance);
            return 0f;
        }

        /// <summary>
        /// Record a performance metric without using a timer
        /// </summary>
        public void RecordMetric(string name, float value)
        {
            if (!trackPerformance)
                return;
                
            performanceMetrics[name] = value;
        }

        /// <summary>
        /// Get all recorded performance metrics
        /// </summary>
        public Dictionary<string, float> GetPerformanceMetrics()
        {
            return new Dictionary<string, float>(performanceMetrics);
        }

        #endregion

        #region Helper Methods

        private string FormatLogMessage(string message, LogCategory category, LogLevel level)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpper();
            
            return $"[{timestamp}] [{levelStr}] [{category}] {message}";
        }

        private void FlushLogBuffer()
        {
            if (!logToFile || string.IsNullOrEmpty(logFilePath))
                return;
                
            try
            {
                lock (logBuffer)
                {
                    if (logBuffer.Length > 0)
                    {
                        File.AppendAllText(logFilePath, logBuffer.ToString());
                        logBuffer.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to write to log file: {ex.Message}");
                logToFile = false;  // Prevent further attempts
            }
        }

        private void SavePerformanceMetrics()
        {
            if (!trackPerformance || performanceMetrics.Count == 0)
                return;
                
            try
            {
                string baseLogPath = Path.Combine(Application.persistentDataPath, logFolderPath);
                if (!Directory.Exists(baseLogPath))
                {
                    Directory.CreateDirectory(baseLogPath);
                }
                
                string perfLogPath = Path.Combine(baseLogPath, PERFORMANCE_LOG_FILE);
                bool fileExists = File.Exists(perfLogPath);
                
                StringBuilder csv = new StringBuilder();
                
                // Add header if file doesn't exist
                if (!fileExists)
                {
                    csv.AppendLine("Timestamp,Operation,Duration(s)");
                }
                
                // Add data rows
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                foreach (var metric in performanceMetrics)
                {
                    csv.AppendLine($"{timestamp},{metric.Key},{metric.Value:F4}");
                }
                
                // Append to file
                File.AppendAllText(perfLogPath, csv.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save performance metrics: {ex.Message}");
            }
        }

        private void LogSessionSummary()
        {
            TimeSpan sessionDuration = DateTime.Now - sessionStartTime;
            
            StringBuilder summary = new StringBuilder();
            summary.AppendLine("=== Session Summary ===");
            summary.AppendLine($"Session duration: {sessionDuration:hh\\:mm\\:ss}");
            summary.AppendLine($"Total log entries: {totalLogCount}");
            summary.AppendLine($"Warnings: {warningLogCount}");
            summary.AppendLine($"Errors: {errorLogCount}");
            
            if (errorCounts.Count > 0)
            {
                summary.AppendLine("Error breakdown:");
                foreach (var errorType in errorCounts.OrderByDescending(e => e.Value).ToDictionary(x => x.Key, x => x.Value))
                {
                    summary.AppendLine($"  {errorType.Key}: {errorType.Value}");
                }
            }
            
            Log(summary.ToString(), LogCategory.System, LogLevel.Info);
        }

        #endregion

        #region Memory Monitoring

        /// <summary>
        /// Log current memory usage statistics
        /// </summary>
        public void LogMemoryUsage()
        {
            long totalMemory = GC.GetTotalMemory(false) / (1024 * 1024);
            long totalSystemMemory = SystemInfo.systemMemorySize;
            float memoryPercentage = (float)totalMemory / totalSystemMemory * 100;
            
            StringBuilder memoryInfo = new StringBuilder();
            memoryInfo.AppendLine($"Memory usage: {totalMemory} MB / {totalSystemMemory} MB ({memoryPercentage:F1}%)");
            
            // Use try-catch for GC mode detection since it may not be available on all platforms
            try {
                bool isServerGC = System.Runtime.GCSettings.IsServerGC;
                memoryInfo.AppendLine($"GC Mode: {(isServerGC ? "Server" : "Workstation")}");
            }
            catch (Exception) {
                memoryInfo.AppendLine("GC Mode: Unknown (not available on this platform)");
            }
            
            // Use UnityEngine.Profiling namespace for profile methods
            memoryInfo.AppendLine($"Mono Heap Size: {Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)} MB");
            memoryInfo.AppendLine($"Mono Used Size: {Profiler.GetMonoUsedSizeLong() / (1024 * 1024)} MB"); // Fixed method name
            
            try {
                memoryInfo.AppendLine($"Texture Memory: {Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024)} MB"); // Fixed method call
            }
            catch (Exception) {
                memoryInfo.AppendLine("Texture Memory: Not available");
            }
            
            Log(memoryInfo.ToString(), LogCategory.Performance, LogLevel.Info);
            
            // Warn if memory usage is high
            if (memoryPercentage > 80)
            {
                LogWarning($"High memory usage detected: {memoryPercentage:F1}% of system memory", LogCategory.Performance);
            }
        }

        #endregion

        #region Public Configuration Methods

        /// <summary>
        /// Set the debugger configuration at runtime
        /// </summary>
        public void Configure(bool enable, bool console = true, bool file = false, LogLevel minLevel = LogLevel.Info)
        {
            enableDebugging = enable;
            logToConsole = console;
            
            // Only enable file logging if it wasn't already enabled
            if (file && !logToFile)
            {
                logToFile = true;
                InitializeLogFile();
            }
            else
            {
                logToFile = file;
            }
            
            minimumLogLevel = minLevel;
            
            Log($"Debugger configuration updated: Enabled={enable}, Console={console}, File={file}, MinLevel={minLevel}", 
                LogCategory.System, LogLevel.Info);
        }

        /// <summary>
        /// Set the categories to include in logging (empty = all)
        /// </summary>
        public void SetLogCategories(params LogCategory[] categories)
        {
            logCategories.Clear();
            if (categories != null && categories.Length > 0)
            {
                logCategories.AddRange(categories);
                Log($"Log categories set to: {string.Join(", ", categories)}", LogCategory.System, LogLevel.Info);
            }
            else
            {
                Log("Log categories set to: All", LogCategory.System, LogLevel.Info);
            }
        }

        #endregion
    }
}
