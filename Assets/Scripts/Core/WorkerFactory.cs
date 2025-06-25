using System;
using Unity.Barracuda;
using UnityEngine;

namespace Traversify.Core
{
    /// <summary>
    /// Factory class for creating neural network workers with different backends
    /// </summary>
    public static class WorkerFactory
    {
        public enum Type
        {
            Auto,
            CSharp,
            CSharpBurst,
            ComputePrecompiled,
            Compute,
            GPU
        }
        
        /// <summary>
        /// Create a worker based on the specified type and model
        /// </summary>
        public static IWorker CreateWorker(Type type, Model model, bool verbose = false)
        {
            switch (type)
            {
                case Type.Auto:
                    return WorkerFactory.CreateDefault(model, verbose);
                case Type.CSharp:
                    return Unity.Barracuda.WorkerFactory.CreateWorker(Unity.Barracuda.WorkerFactory.Type.CSharp, model, verbose);
                case Type.CSharpBurst:
                    return Unity.Barracuda.WorkerFactory.CreateWorker(Unity.Barracuda.WorkerFactory.Type.CSharpBurst, model, verbose);
                case Type.ComputePrecompiled:
                    return Unity.Barracuda.WorkerFactory.CreateWorker(Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled, model, verbose);
                case Type.Compute:
                    return Unity.Barracuda.WorkerFactory.CreateWorker(Unity.Barracuda.WorkerFactory.Type.Compute, model, verbose);
                // Replace Type.GPU with ComputePrecompiled since GPU type doesn't exist
                case Type.GPU:
                    return Unity.Barracuda.WorkerFactory.CreateWorker(Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled, model, verbose);
                default:
                    return WorkerFactory.CreateDefault(model, verbose);
            }
        }
        
        /// <summary>
        /// Create the best default worker for the current platform
        /// </summary>
        public static IWorker CreateDefault(Model model, bool verbose = false)
        {
            if (SystemInfo.supportsComputeShaders)
            {
                return Unity.Barracuda.WorkerFactory.CreateWorker(Unity.Barracuda.WorkerFactory.Type.ComputePrecompiled, model, verbose);
            }
            else
            {
                return Unity.Barracuda.WorkerFactory.CreateWorker(Unity.Barracuda.WorkerFactory.Type.CSharpBurst, model, verbose);
            }
        }
    }
}
