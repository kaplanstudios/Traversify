/*************************************************************************
 *  Traversify – IWorker.cs                                              *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:30:23 UTC                                     *
 *  Desc   : Defines the interface for AI inference workers used         *
 *           throughout the Traversify system. Provides a standardized   *
 *           API for model execution, tensor management, and resource    *
 *           cleanup regardless of the underlying inference engine.      *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
// Unity.Sentis package not available - using conditional compilation
#if UNITY_SENTIS_AVAILABLE
using Unity.Sentis;
using TensorType = Unity.Sentis.Tensor;
#else
using TensorType = Traversify.AI.Tensor;
#endif

namespace Traversify.Core {
    /// <summary>
    /// Type of worker execution device.
    /// </summary>
    public enum WorkerType {
        /// <summary>CPU execution</summary>
        Cpu,
        /// <summary>GPU execution</summary>
        Gpu,
        /// <summary>Hybrid CPU/GPU execution</summary>
        Hybrid
    }
    
    /// <summary>
    /// Interface for AI model inference workers.
    /// Provides a standard API for executing models and retrieving outputs
    /// regardless of the underlying implementation (CPU, GPU, etc.).
    /// </summary>
    public interface IWorker : IDisposable {
        /// <summary>
        /// Gets the name of the model this worker is using.
        /// </summary>
        string ModelName { get; }
        
        /// <summary>
        /// Gets whether the worker is currently executing a model.
        /// </summary>
        bool IsExecuting { get; }
        
        /// <summary>
        /// Gets the execution device type (CPU, GPU, etc.).
        /// </summary>
        WorkerType WorkerType { get; }
        
        /// <summary>
        /// Executes the model with the given input tensor.
        /// </summary>
        /// <param name="input">Input tensor data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Output tensor data</returns>
#if UNITY_SENTIS_AVAILABLE
        Task<Tensor> ExecuteAsync(Tensor input, CancellationToken cancellationToken = default);
#else
        Task<float[]> ExecuteAsync(float[] input, CancellationToken cancellationToken = default);
#endif
        
        /// <summary>
        /// Executes the model with multiple input tensors.
        /// </summary>
        /// <param name="inputs">Dictionary of input tensors by name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary of output tensors by name</returns>
#if UNITY_SENTIS_AVAILABLE
        Task<Dictionary<string, Tensor>> ExecuteAsync(Dictionary<string, Tensor> inputs, CancellationToken cancellationToken = default);
#else
        Task<Dictionary<string, float[]>> ExecuteAsync(Dictionary<string, float[]> inputs, CancellationToken cancellationToken = default);
#endif

        /// <summary>
        /// Executes the model synchronously with input tensors.
        /// </summary>
        /// <param name="inputs">Dictionary of input tensors by name</param>
        void Run(Dictionary<string, TensorType> inputs);

        /// <summary>
        /// Fetches an output tensor by name after execution.
        /// </summary>
        /// <param name="outputName">Name of the output tensor</param>
        /// <returns>Output tensor</returns>
        TensorType Fetch(string outputName);
    }
    
    /// <summary>
    /// Type of backend used by a worker.
    /// </summary>
    public enum WorkerBackend {
        /// <summary>CPU backend using Burst compilation</summary>
        CpuBurst,
        /// <summary>CPU backend using reference implementation</summary>
        CpuReference,
        /// <summary>GPU backend using compute shaders</summary>
        GpuCompute,
        /// <summary>GPU backend using precompiled kernels</summary>
        GpuPrecompiled,
        /// <summary>Hybrid CPU/GPU backend</summary>
        Hybrid,
        /// <summary>Custom or third-party backend</summary>
        Custom
    }
    
    /// <summary>
    /// Represents a named tensor value for use with worker Execute methods.
    /// </summary>
    public readonly struct TensorValuePair {
        /// <summary>
        /// Name of the tensor.
        /// </summary>
        public readonly string Name;
        
        /// <summary>
        /// The tensor value.
        /// </summary>
#if UNITY_SENTIS_AVAILABLE
        public readonly Tensor Value;
        
        public TensorValuePair(string name, Tensor value) {
            Name = name;
            Value = value;
        }
#else
        public readonly float[] Value;
        
        public TensorValuePair(string name, float[] value) {
            Name = name;
            Value = value;
        }
#endif
    }
    
    /// <summary>
    /// Base implementation of IWorker that provides common functionality.
    /// Concrete worker implementations should inherit from this class.
    /// </summary>
    public abstract class WorkerBase : IWorker {
        /// <summary>
        /// Dictionary of output tensors by name.
        /// </summary>
#if UNITY_SENTIS_AVAILABLE
        protected Dictionary<string, Tensor> Outputs { get; } = new Dictionary<string, Tensor>();
#else
        protected Dictionary<string, float[]> Outputs { get; } = new Dictionary<string, float[]>();
#endif
        
        /// <summary>
        /// The model used by this worker.
        /// </summary>
        protected UnityEngine.Object Model { get; }
        
        /// <summary>
        /// Whether this worker is currently executing.
        /// </summary>
        protected bool ExecutionInProgress { get; set; }
        
        /// <summary>
        /// Whether this worker has been initialized.
        /// </summary>
        protected bool Initialized { get; set; }
        
        /// <summary>
        /// Whether this worker has been disposed.
        /// </summary>
        protected bool Disposed { get; set; }
        
        /// <summary>
        /// Gets the name of the model this worker is using.
        /// </summary>
        public string ModelName => Model?.name ?? "Unknown";
        
        /// <summary>
        /// Gets the worker type (CPU, GPU, etc.).
        /// </summary>
        public abstract WorkerType WorkerType { get; }
        
        /// <summary>
        /// Gets the backend type this worker is using.
        /// </summary>
        public abstract WorkerBackend BackendType { get; }
        
        /// <summary>
        /// Gets whether this worker is using GPU acceleration.
        /// </summary>
        public abstract bool IsUsingGPU { get; }
        
        /// <summary>
        /// Gets whether this worker has been initialized.
        /// </summary>
        public bool IsInitialized => Initialized;
        
        /// <summary>
        /// Gets whether this worker is currently executing.
        /// </summary>
        public bool IsExecuting => ExecutionInProgress;
        
        /// <summary>
        /// Creates a new worker with the specified model.
        /// </summary>
        /// <param name="model">The model to use</param>
        protected WorkerBase(UnityEngine.Object model) {
            Model = model ?? throw new ArgumentNullException(nameof(model));
        }
        
        /// <summary>
        /// Executes the model synchronously with input tensors.
        /// </summary>
        /// <param name="inputs">Dictionary of input tensors by name</param>
        public void Run(Dictionary<string, TensorType> inputs)
        {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (Disposed) throw new ObjectDisposedException(GetType().Name);
            
            ExecutionInProgress = true;
            try
            {
#if UNITY_SENTIS_AVAILABLE
                Execute(inputs);
#else
                // Convert to float array format for fallback implementation
                var floatInputs = new Dictionary<string, float[]>();
                foreach (var kvp in inputs)
                {
                    if (kvp.Value is Tensor tensor)
                    {
                        floatInputs[kvp.Key] = tensor.data;
                    }
                }
                Execute(floatInputs);
#endif
            }
            finally
            {
                ExecutionInProgress = false;
            }
        }

        /// <summary>
        /// Fetches an output tensor by name after execution.
        /// </summary>
        /// <param name="outputName">Name of the output tensor</param>
        /// <returns>Output tensor</returns>
        public TensorType Fetch(string outputName)
        {
            if (string.IsNullOrEmpty(outputName)) throw new ArgumentNullException(nameof(outputName));
            if (Disposed) throw new ObjectDisposedException(GetType().Name);
            
#if UNITY_SENTIS_AVAILABLE
            return Outputs.TryGetValue(outputName, out var tensor) ? tensor : null;
#else
            if (Outputs.TryGetValue(outputName, out var data))
            {
                // Create a tensor from the float array data
                return new Tensor(new[] { 1, data.Length }, data) as TensorType;
            }
            return default(TensorType);
#endif
        }
        
#if UNITY_SENTIS_AVAILABLE
        /// <summary>
        /// Execute the model with a single input tensor.
        /// </summary>
        /// <param name="inputName">Name of the input tensor</param>
        /// <param name="inputTensor">Input tensor</param>
        public void Execute(string inputName, Tensor inputTensor) {
            if (string.IsNullOrEmpty(inputName)) throw new ArgumentNullException(nameof(inputName));
            if (inputTensor == null) throw new ArgumentNullException(nameof(inputTensor));
            
            var inputs = new Dictionary<string, Tensor> { { inputName, inputTensor } };
            Execute(inputs);
        }
        
        /// <summary>
        /// Execute the model with a collection of input tensor key-value pairs.
        /// </summary>
        /// <param name="inputs">Input tensor key-value pairs</param>
        public void Execute(params TensorValuePair[] inputs) {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            
            var inputDict = new Dictionary<string, Tensor>();
            foreach (var pair in inputs) {
                inputDict[pair.Name] = pair.Value;
            }
            
            Execute(inputDict);
        }
        
        /// <summary>
        /// Execute the model asynchronously with the given inputs.
        /// </summary>
        /// <param name="inputs">Dictionary of named input tensors</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public virtual async Task<Dictionary<string, Tensor>> ExecuteAsync(Dictionary<string, Tensor> inputs, CancellationToken cancellationToken = default) {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (Disposed) throw new ObjectDisposedException(GetType().Name);
            
            return await Task.Run(() => {
                Execute(inputs);
                return new Dictionary<string, Tensor>(Outputs);
            }, cancellationToken);
        }
        
        /// <summary>
        /// Execute the model asynchronously with a single input.
        /// </summary>
        /// <param name="input">Input tensor</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Output tensor</returns>
        public virtual async Task<Tensor> ExecuteAsync(Tensor input, CancellationToken cancellationToken = default) {
            var inputs = new Dictionary<string, Tensor> { { "input", input } };
            var outputs = await ExecuteAsync(inputs, cancellationToken);
            return outputs.Values.FirstOrDefault();
        }
        
        /// <summary>
        /// Execute the model synchronously with the given inputs.
        /// </summary>
        /// <param name="inputs">Dictionary of named input tensors</param>
        public abstract void Execute(Dictionary<string, Tensor> inputs);
        
        /// <summary>
        /// Retrieve an output tensor by name without copying it.
        /// </summary>
        /// <param name="name">Name of the output tensor</param>
        /// <returns>The output tensor, or null if not found</returns>
        public virtual Tensor PeekOutput(string name) {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (Disposed) throw new ObjectDisposedException(GetType().Name);
            
            return Outputs.TryGetValue(name, out var tensor) ? tensor : null;
        }
        
        /// <summary>
        /// Retrieve a copy of an output tensor by name.
        /// </summary>
        /// <param name="name">Name of the output tensor</param>
        /// <returns>A copy of the output tensor, or null if not found</returns>
        public virtual Tensor FetchOutput(string name) {
            Tensor output = PeekOutput(name);
            return output?.DeepCopy();
        }
#else
        /// <summary>
        /// Execute the model asynchronously with the given inputs.
        /// </summary>
        /// <param name="inputs">Dictionary of named input arrays</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>Task representing the asynchronous operation</returns>
        public virtual async Task<Dictionary<string, float[]>> ExecuteAsync(Dictionary<string, float[]> inputs, CancellationToken cancellationToken = default) {
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));
            if (Disposed) throw new ObjectDisposedException(GetType().Name);
            
            return await Task.Run(() => {
                Execute(inputs);
                return new Dictionary<string, float[]>(Outputs);
            }, cancellationToken);
        }
        
        /// <summary>
        /// Execute the model asynchronously with a single input.
        /// </summary>
        /// <param name="input">Input array</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Output array</returns>
        public virtual async Task<float[]> ExecuteAsync(float[] input, CancellationToken cancellationToken = default) {
            var inputs = new Dictionary<string, float[]> { { "input", input } };
            var outputs = await ExecuteAsync(inputs, cancellationToken);
            return outputs.Values.FirstOrDefault();
        }
        
        /// <summary>
        /// Execute the model synchronously with the given inputs.
        /// </summary>
        /// <param name="inputs">Dictionary of named input arrays</param>
        public abstract void Execute(Dictionary<string, float[]> inputs);
#endif
        
        /// <summary>
        /// Check if an output tensor with the given name exists.
        /// </summary>
        /// <param name="name">Name of the output tensor to check</param>
        /// <returns>True if the output tensor exists, otherwise false</returns>
        public virtual bool HasOutput(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            if (Disposed) return false;
            
            return Outputs.ContainsKey(name);
        }
        
        /// <summary>
        /// Get the names of all available output tensors.
        /// </summary>
        /// <returns>Array of output tensor names</returns>
        public virtual string[] GetOutputNames() {
            if (Disposed) return Array.Empty<string>();
            
            string[] names = new string[Outputs.Count];
            Outputs.Keys.CopyTo(names, 0);
            return names;
        }
        
        /// <summary>
        /// Reset the worker to its initial state, clearing any cached tensors.
        /// </summary>
        public virtual void Reset() {
            if (Disposed) throw new ObjectDisposedException(GetType().Name);
            
#if UNITY_SENTIS_AVAILABLE
            // Dispose all output tensors
            foreach (var tensor in Outputs.Values) {
                tensor?.Dispose();
            }
#endif
            
            Outputs.Clear();
            ExecutionInProgress = false;
        }
        
        /// <summary>
        /// Dispose of all resources used by this worker.
        /// </summary>
        public virtual void Dispose() {
            if (Disposed) return;
            
#if UNITY_SENTIS_AVAILABLE
            // Dispose of all output tensors
            foreach (var tensor in Outputs.Values) {
                tensor?.Dispose();
            }
#endif
            
            Outputs.Clear();
            Disposed = true;
        }
        
        /// <summary>
        /// Throws an ObjectDisposedException if the worker has been disposed.
        /// </summary>
        protected void ThrowIfDisposed() {
            if (Disposed) throw new ObjectDisposedException(GetType().Name);
        }
    }
}
