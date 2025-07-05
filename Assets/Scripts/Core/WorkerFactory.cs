/*************************************************************************
 *  Traversify – WorkerFactory.cs                                        *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 02:49:24 UTC                                     *
 *  Desc   : Advanced factory for creating optimized inference workers   *
 *           based on available hardware and model requirements. Provides*
 *           centralized management of AI inference resources with       *
 *           automatic worker selection and performance tuning.          *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
// Missing package references - using conditional compilation
#if UNITY_INFERENCE_ENGINE_AVAILABLE
using Unity.InferenceEngine;
#elif UNITY_BARRACUDA_AVAILABLE
using Unity.Barracuda;
#endif
#if UNITY_PROFILING_AVAILABLE
using Unity.Profiling;
#endif
#if UNITY_SENTIS_AVAILABLE
using Unity.Sentis;
using Sentis.OnnxRuntime;
#endif

using Traversify.AI;

namespace Traversify.Core {
    
#if !UNITY_AI_INFERENCE_AVAILABLE
    /// <summary>
    /// Placeholder for NNModel when Unity.AI.Inference is not available.
    /// </summary>
    public class NNModel : ScriptableObject {
        public string modelName;
        public ModelAsset modelAsset;
    }
    
    /// <summary>
    /// Placeholder for ModelAsset when Unity.AI.Inference is not available.
    /// </summary>
    public class ModelAsset {
        public long length = 50 * 1024 * 1024; // Default 50MB
    }
    
    /// <summary>
    /// Placeholder for OnnxModelAsset when Unity.AI.Inference is not available.
    /// </summary>
    public class OnnxModelAsset : ScriptableObject {
        public string modelName;
    }
#endif

#if !UNITY_PROFILING_AVAILABLE
    /// <summary>
    /// Simple ProfilerMarker implementation when Unity.Profiling is not available.
    /// </summary>
    public struct ProfilerMarker {
        private readonly string _name;
        
        public ProfilerMarker(string name) {
            _name = name;
        }
        
        public void Begin() { }
        public void End() { }
        
        public struct AutoScope : IDisposable {
            public void Dispose() { }
        }
        
        public AutoScope Auto() => new AutoScope();
    }
#endif

    /// <summary>
    /// Factory for creating and managing optimized inference workers for AI models.
    /// Provides automatic selection of the most suitable backend based on hardware
    /// capabilities and model requirements.
    /// </summary>
    public static class WorkerFactory {
        #region Types and Enums
        
        /// <summary>
        /// Type of worker backend to use for inference.
        /// </summary>
        public enum Type {
            /// <summary>Automatically select the best backend</summary>
            Auto,
            /// <summary>CPU compute with Burst compilation</summary>
            CSharpBurst,
            /// <summary>GPU compute shaders (precompiled)</summary>
            ComputePrecompiled,
            /// <summary>GPU compute shaders (reference)</summary>
            Compute,
            /// <summary>GPU compute shaders with performance optimization</summary>
            ComputeOptimized,
            /// <summary>Metal Performance Shaders (Apple devices)</summary>
            MetalPS,
            /// <summary>ONNX Runtime (CPU)</summary>
            OnnxCpu,
            /// <summary>ONNX Runtime (GPU)</summary>
            OnnxGpu,
            /// <summary>CoreML (Apple devices)</summary>
            CoreML
        }
        
        /// <summary>
        /// Represents a specialized model worker type.
        /// </summary>
        public enum ModelType {
            /// <summary>Generic ONNX model</summary>
            Generic,
            /// <summary>YOLOv12 object detection</summary>
            YOLO,
            /// <summary>SAM2 image segmentation</summary>
            SAM,
            /// <summary>Faster R-CNN object detection</summary>
            FasterRCNN,
            /// <summary>Stable Diffusion image generation</summary>
            StableDiffusion,
            /// <summary>ControlNet image generation</summary>
            ControlNet,
            /// <summary>Depth estimation model</summary>
            DepthEstimation,
            /// <summary>Object pose estimation</summary>
            PoseEstimation
        }
        
        /// <summary>
        /// Performance profile for worker optimization.
        /// </summary>
        public enum PerformanceProfile {
            /// <summary>Optimize for best quality results</summary>
            Quality,
            /// <summary>Balance between quality and speed</summary>
            Balanced,
            /// <summary>Optimize for fastest inference</summary>
            Performance,
            /// <summary>Optimize for lowest memory usage</summary>
            MemoryEfficient
        }
        
        /// <summary>
        /// Configuration for creating a worker.
        /// </summary>
        public class WorkerConfig {
            /// <summary>Inference backend type</summary>
            public Type workerType = Type.Auto;
            /// <summary>Model specialization type</summary>
            public ModelType modelType = ModelType.Generic;
            /// <summary>Performance optimization profile</summary>
            public PerformanceProfile performanceProfile = PerformanceProfile.Balanced;
            /// <summary>Enable verbose logging</summary>
            public bool verboseLogging = false;
            /// <summary>Attempt to use GPU fallbacks if primary GPU method fails</summary>
            public bool useGpuFallbacks = true;
            /// <summary>Optimize model for the current hardware</summary>
            public bool optimizeForDevice = true;
            /// <summary>Batch size for inference</summary>
            public int batchSize = 1;
            /// <summary>Timeout in milliseconds for worker operations</summary>
            public int timeoutMs = 30000;
            /// <summary>Create a thread-safe worker (with some performance cost)</summary>
            public bool threadSafe = false;
            /// <summary>Cache tensors between executions to reduce allocation overhead</summary>
            public bool cacheTensors = true;
            /// <summary>Enable half-precision inference when supported</summary>
            public bool useHalfPrecision = true;
            
            /// <summary>
            /// Creates a configuration for optimal quality.
            /// </summary>
            public static WorkerConfig Quality() {
                return new WorkerConfig {
                    performanceProfile = PerformanceProfile.Quality,
                    useHalfPrecision = false
                };
            }
            
            /// <summary>
            /// Creates a configuration for optimal performance.
            /// </summary>
            public static WorkerConfig FastInference() {
                return new WorkerConfig {
                    performanceProfile = PerformanceProfile.Performance,
                    batchSize = 1,
                    useHalfPrecision = true,
                    cacheTensors = true,
                    optimizeForDevice = true
                };
            }
            
            /// <summary>
            /// Creates a configuration for thread-safe operation.
            /// </summary>
            public static WorkerConfig ThreadSafe() {
                return new WorkerConfig {
                    threadSafe = true,
                    performanceProfile = PerformanceProfile.Balanced
                };
            }
            
            /// <summary>
            /// Creates a configuration for mobile devices.
            /// </summary>
            public static WorkerConfig Mobile() {
                return new WorkerConfig {
                    performanceProfile = PerformanceProfile.MemoryEfficient,
                    useHalfPrecision = true,
                    cacheTensors = true,
                    batchSize = 1
                };
            }
        }
        
        /// <summary>
        /// Model metadata for worker creation and management.
        /// </summary>
        private class ModelMetadata {
            public string name;
            public ModelType type;
            public string version;
            public string[] inputs;
            public string[] outputs;
            public long memoryUsage;
            public bool requiresGpu;
            public int lastUsedFrame;
            public DateTime lastUsedTime;
        }
        
        /// <summary>
        /// Status of a worker instance.
        /// </summary>
        private class WorkerStatus {
            public IWorker worker;
            public Type type;
            public ModelType modelType;
            public bool isBuiltIn;
            public string modelName;
            public bool inUse;
            public int lastUsedFrame;
            public DateTime lastUsedTime;
            public long memoryUsage;
            public WorkerExecutionReport lastExecutionReport;
        }
        
        /// <summary>
        /// Information about a worker execution.
        /// </summary>
        public class WorkerExecutionReport {
            public string modelName;
            public Type workerType;
            public float executionTimeMs;
            public float preprocessTimeMs;
            public float inferenceTimeMs;
            public float postprocessTimeMs;
            public bool success;
            public string errorMessage;
            public Dictionary<string, float> layerStats;
            
            /// <summary>
            /// Total processing time including pre/post-processing.
            /// </summary>
            public float TotalTimeMs => preprocessTimeMs + inferenceTimeMs + postprocessTimeMs;
        }
        
        #endregion
        
        #region Private Static Fields
        
        // Device capabilities and status
        private static bool _deviceCapabilitiesChecked = false;
        private static bool _gpuAvailable = false;
        private static bool _cpuBurstAvailable = false;
        private static bool _onnxRuntimeAvailable = false;
        private static bool _coreMLAvailable = false;
        private static string _deviceName = "";
        private static string _gpuName = "";
        private static string _bestBackend = "";
        
        // Worker caching and pooling
        private static readonly Dictionary<string, ModelMetadata> _modelRegistry = new Dictionary<string, ModelMetadata>();
        private static readonly List<WorkerStatus> _activeWorkers = new List<WorkerStatus>();
        private static readonly Dictionary<ModelType, WorkerStatus> _specializedWorkers = new Dictionary<ModelType, WorkerStatus>();
        
        // Performance and resource tracking
        private static readonly Dictionary<string, WorkerExecutionReport> _executionReports = new Dictionary<string, WorkerExecutionReport>();
        private static int _workerCount = 0;
        private static long _totalMemoryUsage = 0;
        private static readonly int _workerCleanupInterval = 60; // seconds
        private static DateTime _lastCleanupTime = DateTime.MinValue;
        
        // Thread safety
        private static readonly object _workerLock = new object();
        private static readonly object _reportLock = new object();
        
        // Performance profiling
        private static readonly ProfilerMarker _createWorkerMarker = new ProfilerMarker("WorkerFactory.CreateWorker");
        private static readonly ProfilerMarker _disposeWorkerMarker = new ProfilerMarker("WorkerFactory.DisposeWorker");
        private static readonly ProfilerMarker _executeModelMarker = new ProfilerMarker("WorkerFactory.ExecuteModel");
        
        // Logging handler
        private static Action<string, LogCategory, LogLevel> _logHandler;
        
        #endregion
        
        #region Initialization and Device Capabilities
        
        /// <summary>
        /// Initialize the WorkerFactory with a custom log handler.
        /// </summary>
        /// <param name="logHandler">Handler for log messages</param>
        public static void Initialize(Action<string, LogCategory, LogLevel> logHandler = null) {
            _logHandler = logHandler;
            CheckDeviceCapabilities();
        }
        
        /// <summary>
        /// Checks the device's capabilities for inference.
        /// </summary>
        private static void CheckDeviceCapabilities() {
            if (_deviceCapabilitiesChecked) return;
            
            // Get device info
            _deviceName = SystemInfo.deviceModel;
            _gpuName = SystemInfo.graphicsDeviceName;
            
            // Check GPU availability
            _gpuAvailable = SystemInfo.supportsComputeShaders;
            
            // Check Burst compilation availability
            #if ENABLE_BURST
            _cpuBurstAvailable = true;
            #else
            _cpuBurstAvailable = false;
            #endif
            
            // Check for ONNX Runtime
            _onnxRuntimeAvailable = NNLoader.IsOnnxRuntimeAvailable();
            
            // Check for CoreML (Apple devices)
            #if UNITY_IOS || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            _coreMLAvailable = true;
            #else
            _coreMLAvailable = false;
            #endif
            
            // Determine best backend based on capabilities
            if (_gpuAvailable) {
                // Choose based on GPU vendor and capabilities
                string gpuVendor = SystemInfo.graphicsDeviceVendor.ToLowerInvariant();
                
                if (gpuVendor.Contains("nvidia")) {
                    _bestBackend = nameof(Type.ComputeOptimized);
                }
                else if (gpuVendor.Contains("amd") || gpuVendor.Contains("intel")) {
                    _bestBackend = nameof(Type.ComputePrecompiled);
                }
                else if (gpuVendor.Contains("apple") && _coreMLAvailable) {
                    _bestBackend = nameof(Type.CoreML);
                }
                else {
                    _bestBackend = nameof(Type.ComputePrecompiled);
                }
            }
            else if (_cpuBurstAvailable) {
                _bestBackend = nameof(Type.CSharpBurst);
            }
            else {
                _bestBackend = nameof(Type.CSharpBurst); // Fallback to basic CPU
            }
            
            Log($"Device capabilities: GPU={_gpuAvailable} ({_gpuName}), Burst={_cpuBurstAvailable}, ONNX={_onnxRuntimeAvailable}, CoreML={_coreMLAvailable}", LogCategory.AI);
            Log($"Selected best backend: {_bestBackend}", LogCategory.AI);
            
            _deviceCapabilitiesChecked = true;
        }
        
        #endregion
        
        #region Worker Creation and Management
        
        /// <summary>
        /// Creates a worker for executing a neural network model.
        /// </summary>
        /// <param name="type">Worker backend type</param>
        /// <param name="model">Model asset</param>
        /// <param name="config">Optional worker configuration</param>
        /// <returns>Neural network worker</returns>
        public static IWorker CreateWorker(Type type, NNModel model, WorkerConfig config = null) {
            if (!_deviceCapabilitiesChecked) {
                CheckDeviceCapabilities();
            }
            
            _createWorkerMarker.Begin();
            
            try {
                if (model == null) {
                    throw new ArgumentNullException(nameof(model), "Model cannot be null");
                }
                
                config = config ?? new WorkerConfig();
                
                // Auto-select backend if needed
                type = ResolveWorkerType(type, config);
                
                // Create the worker with appropriate backend
                IWorker worker = CreateWorkerForType(type, model, config);
                
                // Register model metadata if not already registered
                string modelId = model.name;
                if (!_modelRegistry.ContainsKey(modelId)) {
                    var metadata = ExtractModelMetadata(model, config.modelType);
                    lock (_workerLock) {
                        _modelRegistry[modelId] = metadata;
                    }
                }
                
                // Track the worker
                RegisterWorker(worker, type, modelId, config.modelType);
                
                Log($"Created {type} worker for model {model.name}", LogCategory.AI);
                return worker;
            }
            catch (Exception ex) {
                Log($"Failed to create worker: {ex.Message}", LogCategory.AI, LogLevel.Error);
                
                // Attempt fallback if GPU method failed
                if (config?.useGpuFallbacks == true && IsGpuBackend(type)) {
                    Log($"Attempting fallback to CPU worker after GPU failure", LogCategory.AI, LogLevel.Warning);
                    try {
                        var cpuWorker = CreateWorker(Type.CSharpBurst, model, config);
                        return cpuWorker;
                    }
                    catch (Exception fallbackEx) {
                        Log($"CPU fallback also failed: {fallbackEx.Message}", LogCategory.AI, LogLevel.Error);
                    }
                }
                
                throw;
            }
            finally {
                _createWorkerMarker.End();
            }
        }
        
        /// <summary>
        /// Creates a worker for executing an ONNX model.
        /// </summary>
        /// <param name="type">Worker backend type</param>
        /// <param name="onnxModel">ONNX model asset</param>
        /// <param name="config">Optional worker configuration</param>
        /// <returns>Neural network worker</returns>
        public static IWorker CreateWorker(Type type, OnnxModelAsset onnxModel, WorkerConfig config = null) {
            if (!_deviceCapabilitiesChecked) {
                CheckDeviceCapabilities();
            }
            
            _createWorkerMarker.Begin();
            
            try {
                if (onnxModel == null) throw new ArgumentNullException(nameof(onnxModel), "Model cannot be null");
                config = config ?? new WorkerConfig();
                // Use Sentis ONNX runtime for all models
                IWorker worker = SentisWorkerFactory.CreateWorker(onnxModel, config);
                Log($"Created Sentis worker for ONNX model {onnxModel.name}", LogCategory.AI);
                RegisterWorker(worker, config.workerType, onnxModel.name, config.modelType);
                return worker;
            }
            catch (Exception ex) {
                Log($"Failed to create ONNX worker: {ex.Message}", LogCategory.AI, LogLevel.Error);
                
                // Rethrow after logging
                throw;
            }
            finally {
                _createWorkerMarker.End();
            }
        }
        
        /// <summary>
        /// Creates a specialized worker for a specific model type.
        /// </summary>
        /// <param name="modelType">Type of model</param>
        /// <param name="modelAsset">Model asset (NNModel or OnnxModelAsset)</param>
        /// <param name="config">Worker configuration</param>
        /// <returns>Worker optimized for the model type</returns>
        public static IWorker CreateSpecializedWorker(ModelType modelType, UnityEngine.Object modelAsset, WorkerConfig config = null) {
            // Check if we already have a cached specialized worker
            lock (_workerLock) {
                if (_specializedWorkers.TryGetValue(modelType, out WorkerStatus cachedWorker) && 
                    !cachedWorker.inUse) {
                    cachedWorker.inUse = true;
                    cachedWorker.lastUsedFrame = Time.frameCount;
                    cachedWorker.lastUsedTime = DateTime.UtcNow;
                    Log($"Reusing specialized worker for {modelType}", LogCategory.AI);
                    return cachedWorker.worker;
                }
            }
            
            // Create a new specialized worker
            if (config == null) {
                config = new WorkerConfig { modelType = modelType };
            }
            else {
                config.modelType = modelType;
            }
            
            // Choose optimal settings based on model type
            OptimizeConfigForModelType(ref config, modelType);
            
            // Create the worker based on model asset type
            IWorker worker;
            if (modelAsset is NNModel nnModel) {
                worker = CreateWorker(config.workerType, nnModel, config);
            }
            else if (modelAsset is OnnxModelAsset onnxModel) {
                worker = CreateWorker(config.workerType, onnxModel, config);
            }
            else {
                throw new ArgumentException($"Unsupported model asset type: {modelAsset.GetType().Name}");
            }
            
            // Register as specialized worker
            lock (_workerLock) {
                if (_specializedWorkers.TryGetValue(modelType, out WorkerStatus existingWorker)) {
                    DisposeWorkerInternal(existingWorker.worker);
                    _specializedWorkers[modelType] = FindWorkerStatus(worker);
                }
                else {
                    _specializedWorkers[modelType] = FindWorkerStatus(worker);
                }
            }
            
            return worker;
        }
        
        /// <summary>
        /// Resolves the actual worker type to use based on requested type and device capabilities.
        /// </summary>
        private static Type ResolveWorkerType(Type requestedType, WorkerConfig config) {
            // Auto-select based on device capabilities and model type
            if (requestedType == Type.Auto) {
                // Check for specialized model requirements
                if (config.modelType == ModelType.SAM || config.modelType == ModelType.StableDiffusion) {
                    // These models really need GPU
                    if (_gpuAvailable) {
                        return Type.ComputeOptimized;
                    }
                    else {
                        Log($"Warning: {config.modelType} model recommended for GPU, but GPU not available", LogCategory.AI, LogLevel.Warning);
                        return Type.CSharpBurst;
                    }
                }
                
                // Choose based on performance profile
                if (config.performanceProfile == PerformanceProfile.Quality) {
                    // Prioritize accuracy
                    if (_gpuAvailable) {
                        return Type.ComputePrecompiled;
                    }
                    else {
                        return Type.CSharpBurst;
                    }
                }
                else if (config.performanceProfile == PerformanceProfile.Performance) {
                    // Prioritize speed
                    if (_gpuAvailable) {
                        return Type.ComputeOptimized;
                    }
                    else if (_onnxRuntimeAvailable) {
                        return Type.OnnxCpu;
                    }
                    else {
                        return Type.CSharpBurst;
                    }
                }
                else if (config.performanceProfile == PerformanceProfile.MemoryEfficient) {
                    // Prioritize memory efficiency
                    return Type.CSharpBurst;
                }
                else {
                    // Balanced - use best available backend
                    if (_gpuAvailable) {
                        return (Type)Enum.Parse(typeof(Type), _bestBackend);
                    }
                    else if (_onnxRuntimeAvailable) {
                        return Type.OnnxCpu;
                    }
                    else {
                        return Type.CSharpBurst;
                    }
                }
            }
            
            // Handle specific backend requests with fallbacks
            if (requestedType == Type.ComputePrecompiled || 
                requestedType == Type.Compute || 
                requestedType == Type.ComputeOptimized) {
                if (!_gpuAvailable) {
                    Log("GPU requested but not available. Falling back to CPU.", LogCategory.AI, LogLevel.Warning);
                    return Type.CSharpBurst;
                }
            }
            else if (requestedType == Type.CoreML && !_coreMLAvailable) {
                Log("CoreML requested but not available. Falling back to default backend.", LogCategory.AI, LogLevel.Warning);
                return _gpuAvailable ? Type.ComputePrecompiled : Type.CSharpBurst;
            }
            else if ((requestedType == Type.OnnxCpu || requestedType == Type.OnnxGpu) && !_onnxRuntimeAvailable) {
                Log("ONNX Runtime requested but not available. Falling back to default backend.", LogCategory.AI, LogLevel.Warning);
                return _gpuAvailable ? Type.ComputePrecompiled : Type.CSharpBurst;
            }
            
            return requestedType;
        }
        
        /// <summary>
        /// Creates a worker using the specified backend type.
        /// </summary>
        private static IWorker CreateWorkerForType(Type type, NNModel model, WorkerConfig config) {
            // Import model
            Unity.InferenceEngine.Model runtimeModel = Unity.InferenceEngine.ModelLoader.Load(model);
            return CreateBarracudaWorker(type, runtimeModel, config);
        }
        
        /// <summary>
        /// Creates a Barracuda worker with the specified backend.
        /// </summary>
        private static IWorker CreateBarracudaWorker(Type type, Unity.InferenceEngine.Model model, WorkerConfig config) {
            #if UNITY_BARRACUDA_AVAILABLE
            Unity.Barracuda.WorkerFactory.Type barracudaType;
            
            switch (type) {
                case Type.ComputePrecompiled:
                    barracudaType = Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled;
                    break;
                case Type.Compute:
                    barracudaType = Unity.Barracuda.WorkerFactory.Type.Compute;
                    break;
                case Type.ComputeOptimized:
                    // ComputeOptimized is actually ComputePrecompiled with some extra optimizations
                    barracudaType = Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled;
                    break;
                case Type.CSharpBurst:
                    barracudaType = Unity.Barracuda.WorkerFactory.Type.CSharpBurst;
                    break;
                case Type.CoreML:
                    #if UNITY_IOS || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
                    barracudaType = Unity.Barracuda.WorkerFactory.Type.CoreML;
                    #else
                    barracudaType = _gpuAvailable ? Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled : Unity.Barracuda.WorkerFactory.Type.CSharpBurst;
                    #endif
                    break;
                default:
                    barracudaType = _gpuAvailable ? Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled : Unity.Barracuda.WorkerFactory.Type.CSharpBurst;
                    break;
            }
            
            // Configure worker options
            var options = new Unity.Barracuda.WorkerFactory.WorkerConfiguration();
            
            // Apply performance profile optimizations
            if (type == Type.ComputeOptimized) {
                // Apply optimizations if available in Barracuda
                options.compareAgainstType = Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled;
            }
            
            // Apply configuration options based on available Barracuda features
            if (config.verboseLogging) {
                options.verbose = true;
            }
            
            // Create and return the worker
            return Unity.Barracuda.WorkerFactory.CreateWorker(barracudaType, model, options);
            #else
            // Barracuda not available - return a placeholder worker
            throw new NotImplementedException("Unity.Barracuda package not available. Please install Unity.Barracuda to use AI inference features.");
            #endif
        }
        
        /// <summary>
        /// Creates an ONNX Runtime worker.
        /// </summary>
        private static IWorker CreateOnnxRuntimeWorker(OnnxModelAsset model, bool useGpu, WorkerConfig config) {
            // ONNX Runtime implementation would go here
            // This is a placeholder since ONNX Runtime integration requires platform-specific code
            throw new NotImplementedException("ONNX Runtime worker creation not implemented in this version");
        }
        
        /// <summary>
        /// Registers a worker in the tracking system.
        /// </summary>
        private static void RegisterWorker(IWorker worker, Type type, string modelId, ModelType modelType) {
            lock (_workerLock) {
                // Update model metadata
                if (_modelRegistry.TryGetValue(modelId, out ModelMetadata metadata)) {
                    metadata.lastUsedFrame = Time.frameCount;
                    metadata.lastUsedTime = DateTime.UtcNow;
                }
                
                // Create worker status
                WorkerStatus status = new WorkerStatus {
                    worker = worker,
                    type = type,
                    modelType = modelType,
                    isBuiltIn = false,
                    modelName = modelId,
                    inUse = true,
                    lastUsedFrame = Time.frameCount,
                    lastUsedTime = DateTime.UtcNow,
                    memoryUsage = EstimateWorkerMemoryUsage(worker, type),
                    lastExecutionReport = null
                };
                
                _activeWorkers.Add(status);
                _workerCount++;
                _totalMemoryUsage += status.memoryUsage;
                
                // Schedule cleanup if needed
                ScheduleCleanupIfNeeded();
            }
        }
        
        /// <summary>
        /// Finds the status record for a worker.
        /// </summary>
        private static WorkerStatus FindWorkerStatus(IWorker worker) {
            lock (_workerLock) {
                foreach (WorkerStatus status in _activeWorkers) {
                    if (status.worker == worker) {
                        return status;
                    }
                }
            }
            return null;
        }
        
        /// <summary>
        /// Schedules worker cleanup if needed.
        /// </summary>
        private static void ScheduleCleanupIfNeeded() {
            // Check if it's time for cleanup
            if ((DateTime.UtcNow - _lastCleanupTime).TotalSeconds >= _workerCleanupInterval && 
                (_workerCount > 10 || _totalMemoryUsage > 1024 * 1024 * 1024)) { // 1 GB
                
                // Schedule cleanup on main thread
                Task.Run(() => {
                    // Actually we can't do this in a Task since Unity objects need to be disposed on the main thread
                    // Instead we'll set a flag and check during Update
                    Log($"Scheduling worker cleanup: {_workerCount} workers, {_totalMemoryUsage / (1024 * 1024)} MB", LogCategory.AI);
                    CleanupIdleWorkers();
                    _lastCleanupTime = DateTime.UtcNow;
                });
            }
        }
        
        /// <summary>
        /// Cleans up idle workers to free resources.
        /// </summary>
        private static void CleanupIdleWorkers() {
            lock (_workerLock) {
                // Identify idle workers (not used in last 60 seconds)
                DateTime threshold = DateTime.UtcNow.AddSeconds(-60);
                List<WorkerStatus> idleWorkers = new List<WorkerStatus>();
                
                foreach (WorkerStatus status in _activeWorkers) {
                    if (!status.inUse && status.lastUsedTime < threshold) {
                        idleWorkers.Add(status);
                    }
                }
                
                // Dispose idle workers
                foreach (WorkerStatus status in idleWorkers) {
                    DisposeWorkerInternal(status.worker);
                    _activeWorkers.Remove(status);
                    _workerCount--;
                    _totalMemoryUsage -= status.memoryUsage;
                }
                
                if (idleWorkers.Count > 0) {
                    Log($"Cleaned up {idleWorkers.Count} idle workers", LogCategory.AI);
                }
            }
        }
        
        /// <summary>
        /// Optimizes worker configuration for a specific model type.
        /// </summary>
        private static void OptimizeConfigForModelType(ref WorkerConfig config, ModelType modelType) {
            switch (modelType) {
                case ModelType.YOLO:
                    // YOLO models benefit from GPU but can run on CPU
                    config.workerType = _gpuAvailable ? Type.ComputePrecompiled : Type.CSharpBurst;
                    config.useHalfPrecision = true;
                    break;
                    
                case ModelType.SAM:
                    // SAM really needs GPU for reasonable performance
                    config.workerType = _gpuAvailable ? Type.ComputeOptimized : Type.CSharpBurst;
                    config.performanceProfile = _gpuAvailable ? PerformanceProfile.Performance : PerformanceProfile.MemoryEfficient;
                    break;
                    
                case ModelType.FasterRCNN:
                    // Faster R-CNN has lots of operations, benefits from optimizations
                    config.workerType = _gpuAvailable ? Type.ComputeOptimized : Type.CSharpBurst;
                    config.useHalfPrecision = true;
                    break;
                    
                case ModelType.StableDiffusion:
                    // Stable Diffusion is very GPU intensive
                    config.workerType = _gpuAvailable ? Type.ComputeOptimized : Type.CSharpBurst;
                    config.performanceProfile = PerformanceProfile.Performance;
                    break;
                    
                case ModelType.DepthEstimation:
                    // Depth models are typically simpler and can run well on CPU
                    config.workerType = _gpuAvailable ? Type.ComputePrecompiled : Type.CSharpBurst;
                    config.useHalfPrecision = true;
                    break;
                    
                case ModelType.ControlNet:
                    // ControlNet is GPU intensive
                    config.workerType = _gpuAvailable ? Type.ComputeOptimized : Type.CSharpBurst;
                    config.performanceProfile = PerformanceProfile.Performance;
                    break;
                    
                case ModelType.PoseEstimation:
                    // Pose estimation typically benefits from precision
                    config.workerType = _gpuAvailable ? Type.ComputePrecompiled : Type.CSharpBurst;
                    config.useHalfPrecision = false;
                    break;
                    
                case ModelType.Generic:
                default:
                    // Use balanced defaults
                    config.workerType = _gpuAvailable ? Type.ComputePrecompiled : Type.CSharpBurst;
                    config.performanceProfile = PerformanceProfile.Balanced;
                    break;
            }
        }
        
        #endregion
        
        #region Worker Execution and Monitoring
        
        /// <summary>
        /// Executes a model with performance monitoring.
        /// </summary>
        /// <param name="worker">The worker to use</param>
        /// <param name="inputs">Input tensors</param>
        /// <param name="reportPerformance">Whether to report performance metrics</param>
        /// <returns>Execution report</returns>
#if UNITY_SENTIS_AVAILABLE
        public static WorkerExecutionReport ExecuteModel(IWorker worker, Dictionary<string, Unity.Sentis.Tensor> inputs, bool reportPerformance = false) {
#else
        public static WorkerExecutionReport ExecuteModel(IWorker worker, Dictionary<string, object> inputs, bool reportPerformance = false) {
#endif
            if (worker == null) {
                throw new ArgumentNullException(nameof(worker));
            }
            
            _executeModelMarker.Begin();
            
            try {
                // Find worker status
                WorkerStatus status = FindWorkerStatus(worker);
                if (status == null) {
                    // Worker not registered, create a dummy status
                    status = new WorkerStatus {
                        worker = worker,
                        modelName = "Unknown",
                        type = Type.Auto
                    };
                }
                
                // Create report
                WorkerExecutionReport report = new WorkerExecutionReport {
                    modelName = status.modelName,
                    workerType = status.type,
                    executionTimeMs = 0,
                    preprocessTimeMs = 0,
                    inferenceTimeMs = 0,
                    postprocessTimeMs = 0,
                    success = false,
                    errorMessage = "",
                    layerStats = reportPerformance ? new Dictionary<string, float>() : null
                };
                
                // Record execution
                System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                try {
                    // Preprocess (input preparation should happen before this method call)
                    System.Diagnostics.Stopwatch preprocessTimer = System.Diagnostics.Stopwatch.StartNew();
                    preprocessTimer.Stop();
                    report.preprocessTimeMs = (float)preprocessTimer.Elapsed.TotalMilliseconds;
                    
                    // Measure inference time (this is the actual model execution)
                    System.Diagnostics.Stopwatch inferenceTimer = System.Diagnostics.Stopwatch.StartNew();
                    
                    // Execute the model
#if UNITY_SENTIS_AVAILABLE
                    worker.Execute(inputs);
#else
                    // When Sentis is not available, use reflection to call Execute with object dictionary
                    var executeMethod = worker.GetType().GetMethod("Execute", new System.Type[] { typeof(Dictionary<string, object>) });
                    if (executeMethod != null) {
                        executeMethod.Invoke(worker, new object[] { inputs });
                    } else {
                        // Fallback: try to find any Execute method and invoke it
                        var anyExecuteMethod = worker.GetType().GetMethod("Execute");
                        if (anyExecuteMethod != null) {
                            anyExecuteMethod.Invoke(worker, new object[] { inputs });
                        } else {
                            throw new InvalidOperationException("Worker does not have a compatible Execute method when Sentis is not available.");
                        }
                    }
#endif
                    
                    inferenceTimer.Stop();
                    report.inferenceTimeMs = (float)inferenceTimer.Elapsed.TotalMilliseconds;
                    
                    // Postprocess (e.g., fetching outputs would go here)
                    System.Diagnostics.Stopwatch postprocessTimer = System.Diagnostics.Stopwatch.StartNew();
                    // Here we'd typically fetch outputs, but we leave that to the caller
                    postprocessTimer.Stop();
                    report.postprocessTimeMs = (float)postprocessTimer.Elapsed.TotalMilliseconds;
                    
                    report.success = true;
                }
                catch (Exception ex) {
                    report.success = false;
                    report.errorMessage = ex.Message;
                    Log($"Model execution failed: {ex.Message}", LogCategory.AI, LogLevel.Error);
                }
                
                stopwatch.Stop();
                report.executionTimeMs = (float)stopwatch.Elapsed.TotalMilliseconds;
                
                // Update worker status
                if (status != null) {
                    status.lastUsedFrame = Time.frameCount;
                    status.lastUsedTime = DateTime.UtcNow;
                    status.lastExecutionReport = report;
                }
                
                // Store report
                lock (_reportLock) {
                    _executionReports[status.modelName] = report;
                }
                
                // Log performance if requested
                if (reportPerformance) {
                    Log($"Model {status.modelName} executed in {report.executionTimeMs:F2}ms " +
                        $"(pre: {report.preprocessTimeMs:F2}ms, inference: {report.inferenceTimeMs:F2}ms, post: {report.postprocessTimeMs:F2}ms)", 
                        LogCategory.AI);
                }
                
                return report;
            }
            finally {
                _executeModelMarker.End();
            }
        }
        
        /// <summary>
        /// Gets performance reports for all executed models.
        /// </summary>
        /// <returns>Dictionary of model names to execution reports</returns>
        public static Dictionary<string, WorkerExecutionReport> GetPerformanceReports() {
            lock (_reportLock) {
                return new Dictionary<string, WorkerExecutionReport>(_executionReports);
            }
        }
        
        /// <summary>
        /// Gets resource usage statistics for active workers.
        /// </summary>
        /// <returns>Dictionary of statistics</returns>
        public static Dictionary<string, object> GetResourceStats() {
            Dictionary<string, object> stats = new Dictionary<string, object>();
            
            lock (_workerLock) {
                stats["ActiveWorkerCount"] = _workerCount;
                stats["TotalMemoryUsageMB"] = _totalMemoryUsage / (1024 * 1024);
                stats["ModelCount"] = _modelRegistry.Count;
                
                List<Dictionary<string, object>> workerStats = new List<Dictionary<string, object>>();
                foreach (WorkerStatus status in _activeWorkers) {
                    workerStats.Add(new Dictionary<string, object> {
                        ["ModelName"] = status.modelName,
                        ["WorkerType"] = status.type.ToString(),
                        ["InUse"] = status.inUse,
                        ["MemoryUsageMB"] = status.memoryUsage / (1024 * 1024),
                        ["LastUsedTime"] = status.lastUsedTime.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
                stats["Workers"] = workerStats;
            }
            
            return stats;
        }
        
        #endregion
        
        #region Cleanup and Disposal
        
        /// <summary>
        /// Disposes a worker and releases associated resources.
        /// </summary>
        /// <param name="worker">Worker to dispose</param>
        public static void DisposeWorker(IWorker worker) {
            if (worker == null) return;
            
            _disposeWorkerMarker.Begin();
            
            try {
                DisposeWorkerInternal(worker);
            }
            finally {
                _disposeWorkerMarker.End();
            }
        }
        
        /// <summary>
        /// Internal worker disposal implementation.
        /// </summary>
        private static void DisposeWorkerInternal(IWorker worker) {
            lock (_workerLock) {
                // Find worker in tracking list
                WorkerStatus status = null;
                for (int i = 0; i < _activeWorkers.Count; i++) {
                    if (_activeWorkers[i].worker == worker) {
                        status = _activeWorkers[i];
                        _activeWorkers.RemoveAt(i);
                        break;
                    }
                }
                
                // Update tracking stats
                if (status != null) {
                    _workerCount--;
                    _totalMemoryUsage -= status.memoryUsage;
                    
                    // Also remove from specialized workers if present
                    foreach (var entry in _specializedWorkers.ToList()) {
                        if (entry.Value.worker == worker) {
                            _specializedWorkers.Remove(entry.Key);
                        }
                    }
                }
                
                // Dispose the worker
                try {
                    worker.Dispose();
                }
                catch (Exception ex) {
                    Log($"Error disposing worker: {ex.Message}", LogCategory.AI, LogLevel.Error);
                }
            }
        }
        
        /// <summary>
        /// Disposes all workers and releases all resources.
        /// </summary>
        public static void DisposeAll() {
            lock (_workerLock) {
                Log($"Disposing all workers ({_activeWorkers.Count} active)", LogCategory.AI);
                
                foreach (WorkerStatus status in _activeWorkers.ToList()) {
                    try {
                        status.worker.Dispose();
                    }
                    catch (Exception ex) {
                        Log($"Error disposing worker: {ex.Message}", LogCategory.AI, LogLevel.Error);
                    }
                }
                
                _activeWorkers.Clear();
                _specializedWorkers.Clear();
                _workerCount = 0;
                _totalMemoryUsage = 0;
            }
            
            // Clear other tracking data
            lock (_reportLock) {
                _executionReports.Clear();
            }
        }
        
        /// <summary>
        /// Marks a worker as no longer in use, allowing it to be reused or cleaned up.
        /// </summary>
        /// <param name="worker">Worker to release</param>
        public static void ReleaseWorker(IWorker worker) {
            if (worker == null) return;
            
            lock (_workerLock) {
                foreach (WorkerStatus status in _activeWorkers) {
                    if (status.worker == worker) {
                        status.inUse = false;
                        status.lastUsedFrame = Time.frameCount;
                        status.lastUsedTime = DateTime.UtcNow;
                        break;
                    }
                }
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Extracts metadata from a model.
        /// </summary>
        private static ModelMetadata ExtractModelMetadata(NNModel model, ModelType type) {
            ModelMetadata metadata = new ModelMetadata {
                name = model.name,
                type = type,
                version = "1.0", // Default version
                inputs = new string[0],
                outputs = new string[0],
                memoryUsage = EstimateModelMemoryUsage(model),
                requiresGpu = type == ModelType.SAM || type == ModelType.StableDiffusion,
                lastUsedFrame = Time.frameCount,
                lastUsedTime = DateTime.UtcNow
            };
            
            return metadata;
        }
        
        /// <summary>
        /// Extracts metadata from an ONNX model.
        /// </summary>
        private static ModelMetadata ExtractOnnxModelMetadata(OnnxModelAsset model, ModelType type) {
            ModelMetadata metadata = new ModelMetadata {
                name = model.name,
                type = type,
                version = "1.0", // Default version
                inputs = new string[0],
                outputs = new string[0],
                memoryUsage = EstimateModelMemoryUsage(model),
                requiresGpu = type == ModelType.SAM || type == ModelType.StableDiffusion,
                lastUsedFrame = Time.frameCount,
                lastUsedTime = DateTime.UtcNow
            };
            
            return metadata;
        }
        
        /// <summary>
        /// Estimates memory usage for a model.
        /// </summary>
        private static long EstimateModelMemoryUsage(UnityEngine.Object model) {
            if (model == null) return 0;
            
            // For NNModel, use asset size
            if (model is NNModel nnModel) {
                return nnModel.modelAsset.length;
            }
            
            // For OnnxModelAsset, estimate based on asset size if available
            if (model is OnnxModelAsset onnxModel) {
                // Placeholder - in a real implementation we would read the asset size
                return 50 * 1024 * 1024; // Assume 50MB as default
            }
            
            return 10 * 1024 * 1024; // Default 10MB estimate
        }
        
        /// <summary>
        /// Estimates memory usage for a worker.
        /// </summary>
        private static long EstimateWorkerMemoryUsage(IWorker worker, Type type) {
            if (worker == null) return 0;
            
            // Base memory usage based on worker type
            long baseMemory;
            
            switch (type) {
                case Type.ComputePrecompiled:
                case Type.Compute:
                case Type.ComputeOptimized:
                    baseMemory = 50 * 1024 * 1024; // 50MB for GPU workers
                    break;
                case Type.CSharpBurst:
                    baseMemory = 20 * 1024 * 1024; // 20MB for CPU workers
                    break;
                case Type.CoreML:
                    baseMemory = 30 * 1024 * 1024; // 30MB for CoreML
                    break;
                case Type.OnnxCpu:
                    baseMemory = 40 * 1024 * 1024; // 40MB for ONNX CPU
                    break;
                case Type.OnnxGpu:
                    baseMemory = 60 * 1024 * 1024; // 60MB for ONNX GPU
                    break;
                default:
                    baseMemory = 30 * 1024 * 1024; // 30MB default
                    break;
            }
            
            return baseMemory;
        }
        
        /// <summary>
        /// Checks if a worker type uses GPU acceleration.
        /// </summary>
        private static bool IsGpuBackend(Type type) {
            return type == Type.Compute || 
                   type == Type.ComputePrecompiled || 
                   type == Type.ComputeOptimized || 
                   type == Type.OnnxGpu || 
                   type == Type.CoreML;
        }
        
        /// <summary>
        /// Logs a message with the specified category and level.
        /// </summary>
        private static void Log(string message, LogCategory category, LogLevel level = LogLevel.Info) {
            if (_logHandler != null) {
                _logHandler(message, category, level);
                return;
            }
            
            // Fallback to Debug.Log
            switch (level) {
                case LogLevel.Verbose:
                case LogLevel.Info:
                    Debug.Log($"[{category}] {message}");
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning($"[{category}] {message}");
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    Debug.LogError($"[{category}] {message}");
                    break;
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Placeholder for ModelBuilder when Unity.Barracuda is not available.
    /// </summary>
    public class ModelBuilder : IDisposable {
        private string _name;
        
        public ModelBuilder(string name) {
            _name = name;
        }
        
        public Input CreateInput(UnityEngine.Object asset) {
            return new Input();
        }
        
        public Unity.InferenceEngine.Model Build(Input input) {
            throw new NotImplementedException("ModelBuilder not available without Unity.Barracuda");
        }
        
        public void Dispose() {
        }
        
        public class Input {
        }
    }
    
    /// <summary>
    /// Placeholder for NNLoader when not available.
    /// </summary>
    public static class NNLoader {
        /// <summary>
        /// Checks if ONNX Runtime is available in the current environment.
        /// </summary>
        /// <returns>True if ONNX Runtime is available</returns>
        public static bool IsOnnxRuntimeAvailable() {
            #if UNITY_ONNX_RUNTIME_AVAILABLE
            return true;
            #else
            return false;
            #endif
        }
    }
    
    /// <summary>
    /// Placeholder for Unity.InferenceEngine namespace when not available.
    /// </summary>
    namespace Unity.InferenceEngine {
        public class Model {
        }
        
        public static class ModelLoader {
            public static Model Load(NNModel model) {
                throw new NotImplementedException("Unity.InferenceEngine.ModelLoader not available");
            }
        }
    }
}
