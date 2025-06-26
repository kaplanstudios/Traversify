using System;
using System.Collections.Generic;
using UnityEngine;

namespace Traversify.Core
{
    /// <summary>
    /// Interface for inference tensors - our compatibility layer for Unity.AI.Inference
    /// </summary>
    public interface IInferenceTensor
    {
        int[] Shape { get; }
        int DataCount { get; }
        void CopyTo(float[] data);
    }

    /// <summary>
    /// Interface for inference models - our compatibility layer for Unity.AI.Inference
    /// </summary>
    public interface IInferenceModel : IDisposable
    {
        void SetInputData(string name, float[] data);
        void ExecuteInference();
        object GetOutputTensor(string name);
    }

    /// <summary>
    /// Wrapper for tensor operations that can work without Unity.AI.Inference
    /// </summary>
    public class Tensor : IDisposable
    {
        private float[] _data;
        private int[] _shape;
        private bool _isOutput;

        public int[] shape => _shape;
        public int length => _data?.Length ?? 0;

        // Constructor for creating input tensor from texture
        public Tensor(Texture2D texture, int channels = 3)
        {
            _isOutput = false;
            int width = texture.width;
            int height = texture.height;
            
            // Shape: [batch, height, width, channels]
            _shape = new int[] { 1, height, width, channels };
            _data = new float[height * width * channels];
            
            Color[] pixels = texture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                int baseIdx = i * channels;
                _data[baseIdx] = pixels[i].r;
                if (channels > 1)
                    _data[baseIdx + 1] = pixels[i].g;
                if (channels > 2)
                    _data[baseIdx + 2] = pixels[i].b;
                if (channels > 3)
                    _data[baseIdx + 3] = pixels[i].a;
            }
        }
        
        // Constructor for creating a new tensor with specified shape
        public Tensor(params int[] shape)
        {
            _isOutput = false;
            _shape = shape;
            
            int size = 1;
            for (int i = 0; i < shape.Length; i++)
                size *= shape[i];
                
            _data = new float[size];
        }
        
        // Constructor for creating a tensor with initial data
        public Tensor(int batch, int dim1, float[] data)
        {
            _isOutput = false;
            _shape = new int[] { batch, dim1 };
            _data = data != null ? (float[])data.Clone() : new float[batch * dim1];
        }
        
        // Constructor for creating a tensor from IInferenceTensor
        public Tensor(object inferenceOutput)
        {
            _isOutput = true;
            
            // Check if it's our IInferenceTensor interface
            var inferenceInterface = inferenceOutput as IInferenceTensor;
            if (inferenceInterface != null)
            {
                _shape = inferenceInterface.Shape;
                _data = new float[inferenceInterface.DataCount];
                inferenceInterface.CopyTo(_data);
                return;
            }

            // Fallback for other types of tensor outputs
            if (inferenceOutput is float[] floatArray)
            {
                _data = (float[])floatArray.Clone();
                _shape = new int[] { 1, _data.Length };
                return;
            }
            
            // Default empty tensor if we can't handle the input
            _data = new float[0];
            _shape = new int[] { 1, 0 };
        }
        
        // Indexer for accessing tensor data like a multi-dimensional array
        public float this[params int[] indices]
        {
            get
            {
                int flatIndex = GetFlatIndex(indices);
                return _data[flatIndex];
            }
            set
            {
                int flatIndex = GetFlatIndex(indices);
                _data[flatIndex] = value;
            }
        }
        
        // Helper method to convert multi-dimensional indices to flat array index
        private int GetFlatIndex(int[] indices)
        {
            if (indices.Length != _shape.Length)
                throw new ArgumentException($"Indices dimensions ({indices.Length}) don't match tensor dimensions ({_shape.Length})");
                
            int index = 0;
            int stride = 1;
            
            // Calculate flat index using strides (row-major order)
            for (int i = _shape.Length - 1; i >= 0; i--)
            {
                index += indices[i] * stride;
                stride *= _shape[i];
            }
            
            return index;
        }
        
        // Get raw data as read-only array
        public float[] ToReadOnlyArray()
        {
            return (float[])_data.Clone();
        }
        
        // Dispose method to clean up resources
        public void Dispose()
        {
            _data = null;
        }
    }
    
    // Extension methods for IInferenceModel to maintain compatibility with Barracuda's API
    public static class InferenceModelExtensions
    {
        // Execute model with input tensors
        public static void Execute(this IInferenceModel model, Dictionary<string, Tensor> inputs)
        {
            // Prepare inputs for the inference model
            foreach (var kvp in inputs)
            {
                string name = kvp.Key;
                Tensor tensor = kvp.Value;
                
                // Create inference tensor and copy data
                model.SetInputData(name, tensor.ToReadOnlyArray());
            }
            
            // Run inference
            model.ExecuteInference();
        }
        
        // Execute method that works with the global IInferenceModel interface
        public static void Execute(this global::IInferenceModel model, Dictionary<string, Tensor> inputs)
        {
            // Prepare inputs for the inference model
            foreach (var kvp in inputs)
            {
                string name = kvp.Key;
                Tensor tensor = kvp.Value;
                
                // Create inference tensor and copy data
                model.SetInputData(name, tensor.ToReadOnlyArray());
            }
            
            // Run inference
            model.ExecuteInference();
        }
        
        // Get output tensor by name
        public static Tensor PeekOutput(this IInferenceModel model, string name)
        {
            var output = model.GetOutputTensor(name);
            return new Tensor(output);
        }
        
        // PeekOutput method that works with the global IInferenceModel interface
        public static Tensor PeekOutput(this global::IInferenceModel model, string name)
        {
            var output = model.GetOutputTensor(name);
            return new Tensor(output);
        }
    }
}
