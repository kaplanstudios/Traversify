using System;
using UnityEngine;

/// <summary>
/// Interface for inference tensors
/// </summary>
public interface IInferenceTensor
{
    int[] Shape { get; }
    int DataCount { get; }
    void CopyTo(float[] data);
}

/// <summary>
/// Interface for inference models
/// </summary>
public interface IInferenceModel : IDisposable
{
    void SetInputData(string name, float[] data);
    void ExecuteInference();
    object GetOutputTensor(string name);
    void UseGpu();
    void UseCpu();
    void LoadAssetFromObject(OnnxModelAsset model);
}

/// <summary>
/// Implementation of the InferenceModel class
/// </summary>
public class InferenceModel : IInferenceModel, Traversify.Core.IInferenceModel
{
    public void SetInputData(string name, float[] data)
    {
        Debug.Log($"Setting input data: {name}");
    }

    public void ExecuteInference()
    {
        Debug.Log("Executing inference");
    }

    public object GetOutputTensor(string name)
    {
        Debug.Log($"Getting output tensor: {name}");
        return new DummyInferenceTensor();
    }

    public void UseGpu()
    {
        Debug.Log("Using GPU for inference");
    }

    public void UseCpu()
    {
        Debug.Log("Using CPU for inference");
    }

    public void LoadAssetFromObject(OnnxModelAsset model)
    {
        Debug.Log("Loading model asset");
    }

    public void Dispose()
    {
        Debug.Log("Disposing inference model");
    }
}

/// <summary>
/// Implementation of IInferenceTensor for compatibility
/// </summary>
internal class DummyInferenceTensor : IInferenceTensor, Traversify.Core.IInferenceTensor
{
    public int[] Shape => new int[] { 1, 1, 1, 1 };
    public int DataCount => 1;

    public void CopyTo(float[] data)
    {
        if (data != null && data.Length > 0)
            data[0] = 0;
    }
}

/// <summary>
/// Asset representation for ONNX models
/// </summary>
[Serializable]
public class OnnxModelAsset : ScriptableObject
{
    // Empty implementation for compatibility
}

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
        public static Traversify.Core.IInferenceModel CreateWorker(Type type, OnnxModelAsset model, bool verbose = false)
        {
            // AI.Inference doesn't have different backend types like Barracuda did,
            // so we'll create a standard model and just apply GPU setting later
            var inferenceModel = new InferenceModel();
            inferenceModel.LoadAssetFromObject(model);
            
            // Handle device placement based on type
            switch (type)
            {
                case Type.GPU:
                case Type.ComputePrecompiled:
                case Type.Compute:
                    inferenceModel.UseGpu();
                    break;
                case Type.CSharp:
                case Type.CSharpBurst:
                default:
                    inferenceModel.UseCpu();
                    break;
            }
            
            return (Traversify.Core.IInferenceModel)inferenceModel;
        }
        
        /// <summary>
        /// Create the best default worker for the current platform
        /// </summary>
        public static Traversify.Core.IInferenceModel CreateDefault(OnnxModelAsset model, bool verbose = false)
        {
            if (SystemInfo.supportsComputeShaders)
            {
                var inferenceModel = new InferenceModel();
                inferenceModel.LoadAssetFromObject(model);
                inferenceModel.UseGpu();
                return (Traversify.Core.IInferenceModel)inferenceModel;
            }
            else
            {
                var inferenceModel = new InferenceModel();
                inferenceModel.LoadAssetFromObject(model);
                inferenceModel.UseCpu();
                return (Traversify.Core.IInferenceModel)inferenceModel;
            }
        }
    }
}
