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
using Traversify.Core;
using Traversify.AI;
using USentis = Unity.Sentis;

namespace Traversify.Core {
    /// <summary>
    /// Interface for AI inference workers used throughout the Traversify system
    /// </summary>
    public interface IWorker : IDisposable
    {
        /// <summary>
        /// Execute the model with the given inputs
        /// </summary>
        void Execute(Dictionary<string, object> inputs);

        /// <summary>
        /// Gets the output tensor by name
        /// </summary>
        object PeekOutput(string name);
    }

    /// <summary>
    /// Worker for AI model inference using Sentis backend
    /// </summary>
    public class SentisWorker : IWorker
    {
        private USentis.Worker worker;
        private USentis.Model model;

        /// <summary>
        /// Create a new SentisWorker with the specified model and backend type
        /// </summary>
        public SentisWorker(USentis.ModelAsset modelAsset, USentis.BackendType backendType)
        {
            if (modelAsset == null)
                throw new ArgumentNullException(nameof(modelAsset));
                
            model = USentis.ModelLoader.Load(modelAsset);
            worker = new USentis.Worker(model, backendType);
        }

        /// <summary>
        /// Execute the model with the given inputs
        /// </summary>
        public void Execute(Dictionary<string, object> inputs)
        {
            // Set inputs using the correct Sentis API
            foreach (var kvp in inputs)
            {
                if (kvp.Value is USentis.Tensor tensor)
                {
                    worker.SetInput(kvp.Key, tensor);
                }
            }
            worker.Schedule();
        }

        /// <summary>
        /// Gets the output tensor by name
        /// </summary>
        public object PeekOutput(string name)
        {
            return worker.PeekOutput(name);
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        public void Dispose()
        {
            worker?.Dispose();
        }
    }

    /// <summary>
    /// Wrapper for Unity Sentis Worker to implement IWorker interface
    /// </summary>
    public class SentisWorkerWrapper : IWorker
    {
        private readonly USentis.Worker _worker;
        private readonly USentis.Model _model;

        public SentisWorkerWrapper(USentis.Worker worker, USentis.Model model)
        {
            _worker = worker ?? throw new ArgumentNullException(nameof(worker));
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        public void Execute(Dictionary<string, object> inputs)
        {
            // Set inputs using the correct Sentis API
            foreach (var kvp in inputs)
            {
                if (kvp.Value is USentis.Tensor tensor)
                {
                    _worker.SetInput(kvp.Key, tensor);
                }
            }
            _worker.Schedule();
        }

        public object PeekOutput(string name)
        {
            return _worker.PeekOutput(name);
        }

        public void Dispose()
        {
            _worker?.Dispose();
        }
    }

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
    /// Utility class for converting between WorkerFactory.Type and Unity.Sentis.BackendType
    /// </summary>
    public static class WorkerTypeConverter {
        /// <summary>
        /// Converts WorkerFactory.Type to Unity.Sentis.BackendType
        /// </summary>
        public static USentis.BackendType ToSentisBackendType(WorkerFactory.Type workerType) {
            return workerType switch {
                WorkerFactory.Type.ComputePrecompiled => USentis.BackendType.GPUCompute,
                WorkerFactory.Type.CSharpBurst => USentis.BackendType.CPU,
                _ => USentis.BackendType.CPU
            };
        }
        
        /// <summary>
        /// Converts Unity.Sentis.BackendType to WorkerFactory.Type
        /// </summary>
        public static WorkerFactory.Type FromSentisBackendType(USentis.BackendType backendType) {
            return backendType switch {
                USentis.BackendType.GPUCompute => WorkerFactory.Type.ComputePrecompiled,
                USentis.BackendType.CPU => WorkerFactory.Type.CSharpBurst,
                _ => WorkerFactory.Type.CSharpBurst
            };
        }
    }
}

// Essential types needed by TraversifyManager (keeping minimal set here)
namespace Traversify.AI {
    /// <summary>
    /// Represents timing information for processing operations
    /// </summary>
    [System.Serializable]
    public class ProcessingTimings
    {
        public float totalTime;
        public float analysisTime;
        public float segmentationTime;
        public float terrainGenerationTime;
        public float modelGenerationTime;
        public float postProcessingTime;
    }
}
