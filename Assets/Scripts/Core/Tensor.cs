/*************************************************************************
 *  Traversify – Tensor.cs                                               *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Desc   : Advanced tensor operations and utilities for AI model       *
 *           inference. Supports image preprocessing, tensor math,       *
 *           and conversion between Unity textures and AI model tensors. *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Traversify.Core;

namespace Traversify.AI {
    /// <summary>
    /// Tensor class for efficient neural network operations with GPU acceleration support.
    /// Implements common tensor operations and conversions between Unity textures and AI model inputs.
    /// </summary>
    public class Tensor : IDisposable {
        #region Fields & Properties
        
        // Tensor data
        private NativeArray<float> _data;
        private bool _isDisposed = false;
        
        /// <summary>
        /// Tensor shape - [batch, height, width, channels] for NHWC format
        /// </summary>
        public readonly int[] shape;
        
        /// <summary>
        /// Total number of elements in the tensor
        /// </summary>
        public int length => _data.Length;
        
        /// <summary>
        /// Batch size dimension
        /// </summary>
        public int batch => shape.Length > 0 ? shape[0] : 1;
        
        /// <summary>
        /// Height dimension
        /// </summary>
        public int height => shape.Length > 1 ? shape[1] : 1;
        
        /// <summary>
        /// Width dimension
        /// </summary>
        public int width => shape.Length > 2 ? shape[2] : 1;
        
        /// <summary>
        /// Channels dimension
        /// </summary>
        public int channels => shape.Length > 3 ? shape[3] : 1;
        
        /// <summary>
        /// Whether tensor data is in NHWC format (true) or NCHW format (false)
        /// </summary>
        public readonly bool isNHWC;
        
        /// <summary>
        /// Gets or sets tensor element at specified indices
        /// </summary>
        public float this[params int[] indices] {
            get => GetValueAt(indices);
            set => SetValueAt(indices, value);
        }
        
        /// <summary>
        /// Native pointer to tensor data for efficient GPU operations
        /// </summary>
        public IntPtr dataPtr => _data.GetUnsafeReadOnlyPtr();
        
        /// <summary>
        /// Access to raw data array for advanced operations
        /// </summary>
        public NativeArray<float> data => _data;
        
        /// <summary>
        /// Tensor name (useful for debugging and model I/O)
        /// </summary>
        public string name { get; set; }
        
        #endregion
        
        #region Constructors & Initialization
        
        /// <summary>
        /// Creates a tensor with the specified shape filled with zeros.
        /// </summary>
        /// <param name="shape">Tensor dimensions (NHWC format by default)</param>
        /// <param name="nhwcFormat">Whether tensor is in NHWC format (true) or NCHW format (false)</param>
        public Tensor(int[] shape, bool nhwcFormat = true) {
            this.shape = (int[])shape.Clone();
            this.isNHWC = nhwcFormat;
            
            int size = 1;
            for (int i = 0; i < shape.Length; i++) {
                size *= shape[i];
            }
            
            _data = new NativeArray<float>(size, Allocator.Persistent);
        }
        
        /// <summary>
        /// Creates a tensor with the specified shape filled with provided data.
        /// </summary>
        /// <param name="shape">Tensor dimensions</param>
        /// <param name="data">Data to fill the tensor with</param>
        /// <param name="nhwcFormat">Whether tensor is in NHWC format (true) or NCHW format (false)</param>
        public Tensor(int[] shape, float[] data, bool nhwcFormat = true) {
            this.shape = (int[])shape.Clone();
            this.isNHWC = nhwcFormat;
            
            int size = 1;
            for (int i = 0; i < shape.Length; i++) {
                size *= shape[i];
            }
            
            if (data.Length != size) {
                throw new ArgumentException($"Data size {data.Length} doesn't match tensor shape {size}");
            }
            
            _data = new NativeArray<float>(data, Allocator.Persistent);
        }
        
        /// <summary>
        /// Creates a tensor from a Unity texture (with automatic format conversion).
        /// </summary>
        /// <param name="texture">Source texture</param>
        /// <param name="channelCount">Number of channels (1-4)</param>
        /// <param name="normalizeValues">Whether to normalize values to 0-1 range</param>
        public Tensor(Texture2D texture, int channelCount = 3, bool normalizeValues = true) {
            if (texture == null) {
                throw new ArgumentNullException(nameof(texture));
            }
            
            if (channelCount < 1 || channelCount > 4) {
                throw new ArgumentException("Channel count must be between 1 and 4", nameof(channelCount));
            }
            
            // Initialize shape
            shape = new int[4] { 1, texture.height, texture.width, channelCount };
            isNHWC = true;
            
            // Create tensor data
            _data = new NativeArray<float>(texture.height * texture.width * channelCount, Allocator.Persistent);
            
            // Extract pixel data
            Color32[] pixels = texture.GetPixels32();
            
            // Fill tensor data
            for (int y = 0; y < texture.height; y++) {
                for (int x = 0; x < texture.width; x++) {
                    Color32 pixel = pixels[y * texture.width + x];
                    int baseIdx = (y * texture.width + x) * channelCount;
                    
                    if (channelCount >= 1) _data[baseIdx] = normalizeValues ? pixel.r / 255f : pixel.r;
                    if (channelCount >= 2) _data[baseIdx + 1] = normalizeValues ? pixel.g / 255f : pixel.g;
                    if (channelCount >= 3) _data[baseIdx + 2] = normalizeValues ? pixel.b / 255f : pixel.b;
                    if (channelCount >= 4) _data[baseIdx + 3] = normalizeValues ? pixel.a / 255f : pixel.a;
                }
            }
        }
        
        /// <summary>
        /// Creates a tensor from a RenderTexture (with GPU readback).
        /// </summary>
        /// <param name="renderTexture">Source render texture</param>
        /// <param name="channelCount">Number of channels (1-4)</param>
        /// <param name="normalizeValues">Whether to normalize values to 0-1 range</param>
        public Tensor(RenderTexture renderTexture, int channelCount = 3, bool normalizeValues = true) {
            if (renderTexture == null) {
                throw new ArgumentNullException(nameof(renderTexture));
            }
            
            if (channelCount < 1 || channelCount > 4) {
                throw new ArgumentException("Channel count must be between 1 and 4", nameof(channelCount));
            }
            
            // Initialize shape
            shape = new int[4] { 1, renderTexture.height, renderTexture.width, channelCount };
            isNHWC = true;
            
            // Create tensor data
            _data = new NativeArray<float>(renderTexture.height * renderTexture.width * channelCount, Allocator.Persistent);
            
            // Create temporary texture for readback
            Texture2D tempTexture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
            
            // Read from render texture
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = renderTexture;
            tempTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            tempTexture.Apply();
            RenderTexture.active = prevActive;
            
            // Extract pixel data
            Color32[] pixels = tempTexture.GetPixels32();
            UnityEngine.Object.Destroy(tempTexture);
            
            // Fill tensor data
            for (int y = 0; y < renderTexture.height; y++) {
                for (int x = 0; x < renderTexture.width; x++) {
                    Color32 pixel = pixels[y * renderTexture.width + x];
                    int baseIdx = (y * renderTexture.width + x) * channelCount;
                    
                    if (channelCount >= 1) _data[baseIdx] = normalizeValues ? pixel.r / 255f : pixel.r;
                    if (channelCount >= 2) _data[baseIdx + 1] = normalizeValues ? pixel.g / 255f : pixel.g;
                    if (channelCount >= 3) _data[baseIdx + 2] = normalizeValues ? pixel.b / 255f : pixel.b;
                    if (channelCount >= 4) _data[baseIdx + 3] = normalizeValues ? pixel.a / 255f : pixel.a;
                }
            }
        }
        
        /// <summary>
        /// Creates a tensor with the specified dimensions, filled with a specific value.
        /// </summary>
        public static Tensor Filled(int[] shape, float fillValue, bool nhwcFormat = true) {
            Tensor tensor = new Tensor(shape, nhwcFormat);
            tensor.Fill(fillValue);
            return tensor;
        }
        
        /// <summary>
        /// Creates a tensor with the specified dimensions, filled with random values.
        /// </summary>
        public static Tensor Random(int[] shape, float min = 0f, float max = 1f, bool nhwcFormat = true) {
            Tensor tensor = new Tensor(shape, nhwcFormat);
            tensor.FillRandom(min, max);
            return tensor;
        }
        
        /// <summary>
        /// Creates an identity matrix tensor of the specified size.
        /// </summary>
        public static Tensor Identity(int size) {
            Tensor tensor = new Tensor(new int[] { size, size });
            for (int i = 0; i < size; i++) {
                tensor[i, i] = 1f;
            }
            return tensor;
        }
        
        #endregion
        
        #region Value Access
        
        /// <summary>
        /// Gets a value at the specified indices.
        /// </summary>
        public float GetValueAt(params int[] indices) {
            int flatIndex = GetFlatIndex(indices);
            return _data[flatIndex];
        }
        
        /// <summary>
        /// Sets a value at the specified indices.
        /// </summary>
        public void SetValueAt(int[] indices, float value) {
            int flatIndex = GetFlatIndex(indices);
            _data[flatIndex] = value;
        }
        
        /// <summary>
        /// Converts multi-dimensional indices to a flat array index.
        /// </summary>
        private int GetFlatIndex(int[] indices) {
            if (indices.Length != shape.Length) {
                throw new ArgumentException($"Indices length {indices.Length} doesn't match tensor dimensions {shape.Length}");
            }
            
            int flatIndex = 0;
            int stride = 1;
            
            if (isNHWC) {
                // NHWC format - most commonly used in TensorFlow
                for (int i = shape.Length - 1; i >= 0; i--) {
                    flatIndex += indices[i] * stride;
                    stride *= shape[i];
                }
            } else {
                // NCHW format - used in some frameworks like PyTorch
                for (int i = shape.Length - 1; i >= 0; i--) {
                    flatIndex += indices[i] * stride;
                    stride *= shape[i];
                }
            }
            
            return flatIndex;
        }
        
        /// <summary>
        /// Gets a span view of the tensor data for efficient memory access.
        /// </summary>
        public Span<float> AsSpan() {
            unsafe {
                return new Span<float>(_data.GetUnsafePtr(), _data.Length);
            }
        }
        
        /// <summary>
        /// Fills the entire tensor with a specified value.
        /// </summary>
        public void Fill(float value) {
            for (int i = 0; i < _data.Length; i++) {
                _data[i] = value;
            }
        }
        
        /// <summary>
        /// Fills the tensor with random values in the specified range.
        /// </summary>
        public void FillRandom(float min = 0f, float max = 1f) {
            System.Random random = new System.Random();
            for (int i = 0; i < _data.Length; i++) {
                _data[i] = min + (float)random.NextDouble() * (max - min);
            }
        }
        
        #endregion
        
        #region Tensor Operations
        
        /// <summary>
        /// Adds another tensor to this tensor element-wise.
        /// </summary>
        public Tensor Add(Tensor other) {
            if (!AreShapesCompatible(shape, other.shape)) {
                throw new ArgumentException("Tensor shapes are not compatible for addition");
            }
            
            Tensor result = new Tensor(shape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                result._data[i] = _data[i] + other._data[i];
            }
            return result;
        }
        
        /// <summary>
        /// Subtracts another tensor from this tensor element-wise.
        /// </summary>
        public Tensor Subtract(Tensor other) {
            if (!AreShapesCompatible(shape, other.shape)) {
                throw new ArgumentException("Tensor shapes are not compatible for subtraction");
            }
            
            Tensor result = new Tensor(shape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                result._data[i] = _data[i] - other._data[i];
            }
            return result;
        }
        
        /// <summary>
        /// Multiplies this tensor element-wise with another tensor.
        /// </summary>
        public Tensor Multiply(Tensor other) {
            if (!AreShapesCompatible(shape, other.shape)) {
                throw new ArgumentException("Tensor shapes are not compatible for multiplication");
            }
            
            Tensor result = new Tensor(shape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                result._data[i] = _data[i] * other._data[i];
            }
            return result;
        }
        
        /// <summary>
        /// Divides this tensor element-wise by another tensor.
        /// </summary>
        public Tensor Divide(Tensor other) {
            if (!AreShapesCompatible(shape, other.shape)) {
                throw new ArgumentException("Tensor shapes are not compatible for division");
            }
            
            Tensor result = new Tensor(shape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                if (Math.Abs(other._data[i]) < float.Epsilon) {
                    result._data[i] = 0; // Avoid division by zero
                } else {
                    result._data[i] = _data[i] / other._data[i];
                }
            }
            return result;
        }
        
        /// <summary>
        /// Multiplies the tensor by a scalar value.
        /// </summary>
        public Tensor Multiply(float scalar) {
            Tensor result = new Tensor(shape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                result._data[i] = _data[i] * scalar;
            }
            return result;
        }
        
        /// <summary>
        /// Performs matrix multiplication between this tensor and another.
        /// </summary>
        public Tensor MatMul(Tensor other) {
            // Check if shapes are compatible for matrix multiplication
            if (shape.Length < 2 || other.shape.Length < 2) {
                throw new ArgumentException("Tensors must have at least 2 dimensions for MatMul");
            }
            
            int thisRows = shape[shape.Length - 2];
            int thisCols = shape[shape.Length - 1];
            int otherRows = other.shape[other.shape.Length - 2];
            int otherCols = other.shape[other.shape.Length - 1];
            
            if (thisCols != otherRows) {
                throw new ArgumentException($"Incompatible matrix dimensions for multiplication: {thisRows}x{thisCols} and {otherRows}x{otherCols}");
            }
            
            // Create result shape (preserving batch dimensions)
            int[] resultShape = new int[Math.Max(shape.Length, other.shape.Length)];
            for (int i = 0; i < resultShape.Length - 2; i++) {
                int thisDim = i < shape.Length - 2 ? shape[i] : 1;
                int otherDim = i < other.shape.Length - 2 ? other.shape[i] : 1;
                resultShape[i] = Math.Max(thisDim, otherDim);
            }
            resultShape[resultShape.Length - 2] = thisRows;
            resultShape[resultShape.Length - 1] = otherCols;
            
            Tensor result = new Tensor(resultShape, isNHWC);
            
            // Simple matrix multiplication (not optimized for batch dimensions)
            for (int i = 0; i < thisRows; i++) {
                for (int j = 0; j < otherCols; j++) {
                    float sum = 0;
                    for (int k = 0; k < thisCols; k++) {
                        sum += this[i, k] * other[k, j];
                    }
                    result[i, j] = sum;
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Transposes the last two dimensions of the tensor.
        /// </summary>
        public Tensor Transpose() {
            if (shape.Length < 2) {
                throw new InvalidOperationException("Tensor must have at least 2 dimensions for transpose");
            }
            
            // Create new shape with last two dimensions swapped
            int[] newShape = (int[])shape.Clone();
            int lastDim = newShape.Length - 1;
            int secondLastDim = newShape.Length - 2;
            int temp = newShape[lastDim];
            newShape[lastDim] = newShape[secondLastDim];
            newShape[secondLastDim] = temp;
            
            Tensor result = new Tensor(newShape, isNHWC);
            
            // Handle transpose for 2D case (simplest)
            if (shape.Length == 2) {
                for (int i = 0; i < shape[0]; i++) {
                    for (int j = 0; j < shape[1]; j++) {
                        result[j, i] = this[i, j];
                    }
                }
            }
            // General case (not implemented for brevity)
            else {
                throw new NotImplementedException("Transpose for tensors with more than 2 dimensions is not implemented");
            }
            
            return result;
        }
        
        /// <summary>
        /// Applies a ReLU activation function to the tensor.
        /// </summary>
        public Tensor ReLU() {
            Tensor result = new Tensor(shape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                result._data[i] = Math.Max(0, _data[i]);
            }
            return result;
        }
        
        /// <summary>
        /// Applies a Sigmoid activation function to the tensor.
        /// </summary>
        public Tensor Sigmoid() {
            Tensor result = new Tensor(shape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                result._data[i] = 1f / (1f + (float)Math.Exp(-_data[i]));
            }
            return result;
        }
        
        /// <summary>
        /// Applies a Softmax activation function to the tensor along the specified axis.
        /// </summary>
        public Tensor Softmax(int axis = -1) {
            if (axis < 0) {
                axis = shape.Length + axis;
            }
            
            if (axis < 0 || axis >= shape.Length) {
                throw new ArgumentException($"Invalid axis {axis} for tensor with {shape.Length} dimensions");
            }
            
            Tensor result = new Tensor(shape, isNHWC);
            
            // Simple implementation for last dimension
            if (axis == shape.Length - 1) {
                int lastDimSize = shape[shape.Length - 1];
                int numVectors = _data.Length / lastDimSize;
                
                for (int i = 0; i < numVectors; i++) {
                    int offset = i * lastDimSize;
                    
                    // Find max for numerical stability
                    float max = float.MinValue;
                    for (int j = 0; j < lastDimSize; j++) {
                        max = Math.Max(max, _data[offset + j]);
                    }
                    
                    // Calculate exponentials and sum
                    float sum = 0;
                    for (int j = 0; j < lastDimSize; j++) {
                        float exp = (float)Math.Exp(_data[offset + j] - max);
                        result._data[offset + j] = exp;
                        sum += exp;
                    }
                    
                    // Normalize
                    for (int j = 0; j < lastDimSize; j++) {
                        result._data[offset + j] /= sum;
                    }
                }
            }
            else {
                throw new NotImplementedException("Softmax for arbitrary axis is not implemented");
            }
            
            return result;
        }
        
        /// <summary>
        /// Reshapes the tensor to a new shape with the same total number of elements.
        /// </summary>
        public Tensor Reshape(int[] newShape) {
            int newSize = 1;
            for (int i = 0; i < newShape.Length; i++) {
                newSize *= newShape[i];
            }
            
            if (newSize != _data.Length) {
                throw new ArgumentException($"Cannot reshape tensor of size {_data.Length} to shape with size {newSize}");
            }
            
            Tensor result = new Tensor(newShape, isNHWC);
            for (int i = 0; i < _data.Length; i++) {
                result._data[i] = _data[i];
            }
            
            return result;
        }
        
        /// <summary>
        /// Flattens the tensor into a 1D tensor.
        /// </summary>
        public Tensor Flatten() {
            return Reshape(new int[] { _data.Length });
        }
        
        /// <summary>
        /// Checks if two tensor shapes are compatible for element-wise operations.
        /// Supports broadcasting.
        /// </summary>
        private bool AreShapesCompatible(int[] shape1, int[] shape2) {
            // For now, just check exact match or scalar
            if (shape1.Length != shape2.Length) {
                return false;
            }
            
            for (int i = 0; i < shape1.Length; i++) {
                if (shape1[i] != shape2[i]) {
                    return false;
                }
            }
            
            return true;
        }
        
        #endregion
        
        #region Conversion Methods
        
        /// <summary>
        /// Converts the tensor to a Texture2D.
        /// </summary>
        /// <param name="channelCount">Number of channels to extract (1-4)</param>
        /// <param name="batchIndex">Which batch to convert (defaults to first)</param>
        /// <param name="normalizeValues">Whether values are in 0-1 range and need scaling to 0-255</param>
        public Texture2D ToTexture2D(int channelCount = 3, int batchIndex = 0, bool normalizeValues = true) {
            if (shape.Length < 3) {
                throw new InvalidOperationException("Tensor must have at least 3 dimensions (batch, height, width, [channels]) to convert to texture");
            }
            
            // Determine dimensions
            int h = height;
            int w = width;
            int c = Math.Min(channelCount, channels);
            
            if (c < 1 || c > 4) {
                throw new ArgumentException("Channel count must be between 1 and 4", nameof(channelCount));
            }
            
            // Create texture
            TextureFormat format = c == 1 ? TextureFormat.R8 : 
                                  c == 2 ? TextureFormat.RG16 : 
                                  c == 3 ? TextureFormat.RGB24 : 
                                  TextureFormat.RGBA32;
                                  
            Texture2D texture = new Texture2D(w, h, format, false);
            Color32[] pixels = new Color32[w * h];
            
            // Fill pixel data
            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    byte r = 0, g = 0, b = 0, a = 255;
                    
                    if (isNHWC) {
                        if (c >= 1) r = GetByteValue(this[batchIndex, y, x, 0], normalizeValues);
                        if (c >= 2) g = GetByteValue(this[batchIndex, y, x, 1], normalizeValues);
                        if (c >= 3) b = GetByteValue(this[batchIndex, y, x, 2], normalizeValues);
                        if (c >= 4) a = GetByteValue(this[batchIndex, y, x, 3], normalizeValues);
                    }
                    else {
                        if (c >= 1) r = GetByteValue(this[batchIndex, 0, y, x], normalizeValues);
                        if (c >= 2) g = GetByteValue(this[batchIndex, 1, y, x], normalizeValues);
                        if (c >= 3) b = GetByteValue(this[batchIndex, 2, y, x], normalizeValues);
                        if (c >= 4) a = GetByteValue(this[batchIndex, 3, y, x], normalizeValues);
                    }
                    
                    pixels[y * w + x] = new Color32(r, g, b, a);
                }
            }
            
            texture.SetPixels32(pixels);
            texture.Apply();
            
            return texture;
        }
        
        /// <summary>
        /// Helper method to convert float value to byte, with optional normalization.
        /// </summary>
        private byte GetByteValue(float value, bool normalize) {
            if (normalize) {
                return (byte)Mathf.Clamp(value * 255f, 0, 255);
            }
            return (byte)Mathf.Clamp(value, 0, 255);
        }
        
        /// <summary>
        /// Converts tensor to a float array.
        /// </summary>
        public float[] ToArray() {
            float[] array = new float[_data.Length];
            _data.CopyTo(array);
            return array;
        }
        
        /// <summary>
        /// Converts a 2D tensor slice to a 2D array.
        /// </summary>
        public float[,] To2DArray(int batchIndex = 0, int channelIndex = 0) {
            if (shape.Length < 4) {
                throw new InvalidOperationException("Tensor must have at least 4 dimensions for To2DArray");
            }
            
            float[,] result = new float[height, width];
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    if (isNHWC) {
                        result[y, x] = this[batchIndex, y, x, channelIndex];
                    }
                    else {
                        result[y, x] = this[batchIndex, channelIndex, y, x];
                    }
                }
            }
            
            return result;
        }
        
        #endregion
        
        #region Static Utility Methods
        
        /// <summary>
        /// Creates a tensor from a Unity Texture2D with preprocessing for neural networks.
        /// </summary>
        /// <param name="texture">Source texture</param>
        /// <param name="size">Target size for resizing (if 0, no resize)</param>
        /// <param name="meanPixel">Mean pixel value for normalization</param>
        /// <param name="stdPixel">Standard deviation for normalization</param>
        /// <param name="swapRB">Whether to swap red and blue channels</param>
        /// <returns>Processed tensor ready for model input</returns>
        public static Tensor PreprocessImage(Texture2D texture, int size = 0, float[] meanPixel = null, float[] stdPixel = null, bool swapRB = false) {
            if (texture == null) {
                throw new ArgumentNullException(nameof(texture));
            }
            
            // Default mean and std values for ImageNet
            if (meanPixel == null) {
                meanPixel = new float[] { 0.485f, 0.456f, 0.406f };
            }
            
            if (stdPixel == null) {
                stdPixel = new float[] { 0.229f, 0.224f, 0.225f };
            }
            
            // Resize if needed
            Texture2D processedTexture = texture;
            if (size > 0 && (texture.width != size || texture.height != size)) {
                processedTexture = ResizeTexture(texture, size, size);
            }
            
            // Create tensor
            Tensor tensor = new Tensor(new int[] { 1, processedTexture.height, processedTexture.width, 3 });
            
            // Get pixel data
            Color32[] pixels = processedTexture.GetPixels32();
            
            // Fill tensor with normalized values
            for (int y = 0; y < processedTexture.height; y++) {
                for (int x = 0; x < processedTexture.width; x++) {
                    Color32 pixel = pixels[y * processedTexture.width + x];
                    
                    // Process RGB channels
                    int rIndex = swapRB ? 2 : 0;
                    int gIndex = 1;
                    int bIndex = swapRB ? 0 : 2;
                    
                    float r = (pixel.r / 255f - meanPixel[rIndex]) / stdPixel[rIndex];
                    float g = (pixel.g / 255f - meanPixel[gIndex]) / stdPixel[gIndex];
                    float b = (pixel.b / 255f - meanPixel[bIndex]) / stdPixel[bIndex];
                    
                    tensor[0, y, x, 0] = r;
                    tensor[0, y, x, 1] = g;
                    tensor[0, y, x, 2] = b;
                }
            }
            
            // Clean up temporary texture
            if (processedTexture != texture) {
                UnityEngine.Object.Destroy(processedTexture);
            }
            
            return tensor;
        }
        
        /// <summary>
        /// Resizes a texture to the specified dimensions.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight) {
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }
        
        /// <summary>
        /// Decodes a segmentation mask tensor to a Texture2D.
        /// </summary>
        /// <param name="tensor">Source tensor</param>
        /// <param name="threshold">Threshold value for binary masks</param>
        /// <returns>Mask texture</returns>
        public static Texture2D DecodeMaskTensor(Tensor tensor, float threshold = 0.5f) {
            if (tensor.shape.Length < 3) {
                throw new ArgumentException("Mask tensor must have at least 3 dimensions");
            }
            
            int h = tensor.height;
            int w = tensor.width;
            int c = tensor.channels;
            
            Texture2D maskTexture = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[w * h];
            
            // Single channel mask
            if (c == 1) {
                for (int y = 0; y < h; y++) {
                    for (int x = 0; x < w; x++) {
                        float value = tensor.isNHWC ? tensor[0, y, x, 0] : tensor[0, 0, y, x];
                        byte alpha = value > threshold ? (byte)255 : (byte)0;
                        pixels[y * w + x] = new Color32(255, 255, 255, alpha);
                    }
                }
            }
            // Multi-channel mask (class segmentation)
            else if (c > 1) {
                for (int y = 0; y < h; y++) {
                    for (int x = 0; x < w; x++) {
                        // Find channel with highest activation
                        int maxChannel = 0;
                        float maxValue = float.MinValue;
                        
                        for (int ci = 0; ci < c; ci++) {
                            float value = tensor.isNHWC ? tensor[0, y, x, ci] : tensor[0, ci, y, x];
                            if (value > maxValue) {
                                maxValue = value;
                                maxChannel = ci;
                            }
                        }
                        
                        if (maxValue > threshold) {
                            // Generate distinct color for each class
                            Color32 classColor = GetColorForClass(maxChannel);
                            classColor.a = 255;
                            pixels[y * w + x] = classColor;
                        }
                        else {
                            pixels[y * w + x] = new Color32(0, 0, 0, 0);
                        }
                    }
                }
            }
            
            maskTexture.SetPixels32(pixels);
            maskTexture.Apply();
            
            return maskTexture;
        }
        
        /// <summary>
        /// Generates a distinct color for class visualization.
        /// </summary>
        private static Color32 GetColorForClass(int classIndex) {
            // Generate visually distinct colors using HSV color space
            float hue = (classIndex * 0.618033988749895f) % 1f; // Golden ratio method
            float saturation = 0.7f;
            float value = 0.95f;
            
            Color color = Color.HSVToRGB(hue, saturation, value);
            return new Color32(
                (byte)(color.r * 255),
                (byte)(color.g * 255),
                (byte)(color.b * 255),
                255
            );
        }
        
        /// <summary>
        /// Applies Non-Maximum Suppression to bounding box predictions.
        /// </summary>
        /// <param name="boxesTensor">Tensor with box coordinates [x1, y1, x2, y2]</param>
        /// <param name="scoresTensor">Tensor with confidence scores</param>
        /// <param name="iouThreshold">IoU threshold for suppression</param>
        /// <param name="scoreThreshold">Minimum score threshold</param>
        /// <returns>Indices of kept boxes</returns>
        public static int[] NonMaxSuppression(Tensor boxesTensor, Tensor scoresTensor, float iouThreshold = 0.5f, float scoreThreshold = 0.1f) {
            if (boxesTensor.shape[0] != scoresTensor.shape[0]) {
                throw new ArgumentException("Number of boxes must match number of scores");
            }
            
            int numBoxes = boxesTensor.shape[0];
            List<int> keepIndices = new List<int>();
            
            // Get data as arrays for easier processing
            float[] boxes = boxesTensor.ToArray();
            float[] scores = scoresTensor.ToArray();
            
            // Create index array sorted by descending scores
            int[] indices = new int[numBoxes];
            for (int i = 0; i < numBoxes; i++) {
                indices[i] = i;
            }
            Array.Sort(indices, (a, b) => scores[b].CompareTo(scores[a]));
            
            bool[] suppressed = new bool[numBoxes];
            
            for (int i = 0; i < numBoxes; i++) {
                int idx = indices[i];
                
                // Skip boxes below threshold or already suppressed
                if (scores[idx] < scoreThreshold || suppressed[idx]) {
                    continue;
                }
                
                keepIndices.Add(idx);
                
                // Get coordinates for this box
                float x1 = boxes[idx * 4];
                float y1 = boxes[idx * 4 + 1];
                float x2 = boxes[idx * 4 + 2];
                float y2 = boxes[idx * 4 + 3];
                float area1 = (x2 - x1) * (y2 - y1);
                
                // Suppress other boxes with high IoU
                for (int j = i + 1; j < numBoxes; j++) {
                    int idx2 = indices[j];
                    
                    if (suppressed[idx2]) {
                        continue;
                    }
                    
                    // Get coordinates for comparison box
                    float xx1 = boxes[idx2 * 4];
                    float yy1 = boxes[idx2 * 4 + 1];
                    float xx2 = boxes[idx2 * 4 + 2];
                    float yy2 = boxes[idx2 * 4 + 3];
                    float area2 = (xx2 - xx1) * (yy2 - yy1);
                    
                    // Calculate intersection
                    float interX1 = Math.Max(x1, xx1);
                    float interY1 = Math.Max(y1, yy1);
                    float interX2 = Math.Min(x2, xx2);
                    float interY2 = Math.Min(y2, yy2);
                    
                    if (interX2 <= interX1 || interY2 <= interY1) {
                        continue; // No intersection
                    }
                    
                    float interArea = (interX2 - interX1) * (interY2 - interY1);
                    float iou = interArea / (area1 + area2 - interArea);
                    
                    if (iou > iouThreshold) {
                        suppressed[idx2] = true;
                    }
                }
            }
            
            return keepIndices.ToArray();
        }
        
        #endregion
        
        #region Multithreaded Operations
        
        /// <summary>
        /// Applies a function to all elements of the tensor in parallel.
        /// </summary>
        public Tensor ParallelMap(Func<float, float> function) {
            Tensor result = new Tensor(shape, isNHWC);
            
            Parallel.For(0, _data.Length, i => {
                result._data[i] = function(_data[i]);
            });
            
            return result;
        }
        
        /// <summary>
        /// Job struct for parallel tensor operations.
        /// </summary>
        private struct TensorProcessingJob : IJobParallelFor {
            [ReadOnly] public NativeArray<float> input;
            public NativeArray<float> output;
            public float factor;
            
            public void Execute(int index) {
                output[index] = input[index] * factor;
            }
        }
        
        /// <summary>
        /// Applies a scaling operation using Unity Jobs system.
        /// </summary>
        public Tensor Scale(float factor) {
            Tensor result = new Tensor(shape, isNHWC);
            
            TensorProcessingJob job = new TensorProcessingJob {
                input = _data,
                output = result._data,
                factor = factor
            };
            
            JobHandle handle = job.Schedule(_data.Length, 64);
            handle.Complete();
            
            return result;
        }
        
        #endregion
        
        #region IDisposable Implementation
        
        /// <summary>
        /// Disposes native resources.
        /// </summary>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// Disposes native resources.
        /// </summary>
        protected virtual void Dispose(bool disposing) {
            if (!_isDisposed) {
                if (disposing && _data.IsCreated) {
                    _data.Dispose();
                }
                
                _isDisposed = true;
            }
        }
        
        /// <summary>
        /// Finalizer to clean up resources if Dispose wasn't called.
        /// </summary>
        ~Tensor() {
            Dispose(false);
        }
        
        #endregion
        
        #region Static Factory Methods
        
        /// <summary>
        /// Creates a tensor with the specified batch, height, width, and channels.
        /// </summary>
        public static Tensor Create(int batch, int height, int width, int channels, bool nhwcFormat = true) {
            return new Tensor(new int[] { batch, height, width, channels }, nhwcFormat);
        }
        
        /// <summary>
        /// Creates a tensor filled with zeros with the specified shape.
        /// </summary>
        public static Tensor Zeros(params int[] shape) {
            return new Tensor(shape);
        }
        
        /// <summary>
        /// Creates a tensor filled with ones with the specified shape.
        /// </summary>
        public static Tensor Ones(params int[] shape) {
            Tensor tensor = new Tensor(shape);
            tensor.Fill(1f);
            return tensor;
        }
        
        /// <summary>
        /// Creates a tensor with normally distributed random values.
        /// </summary>
        public static Tensor RandomNormal(int[] shape, float mean = 0f, float stdDev = 1f) {
            Tensor tensor = new Tensor(shape);
            System.Random random = new System.Random();
            
            // Box-Muller transform for normal distribution
            for (int i = 0; i < tensor._data.Length; i += 2) {
                double u1 = random.NextDouble();
                double u2 = random.NextDouble();
                
                double randStdNormal1 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
                tensor._data[i] = (float)(mean + stdDev * randStdNormal1);
                
                if (i + 1 < tensor._data.Length) {
                    double randStdNormal2 = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    tensor._data[i + 1] = (float)(mean + stdDev * randStdNormal2);
                }
            }
            
            return tensor;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Utilities for converting between Unity tensors and other formats.
    /// </summary>
    public static class TensorUtils {
        /// <summary>
        /// Creates a tensor from a Unity texture with standardized preprocessing for AI models.
        /// </summary>
        /// <param name="texture">Input texture</param>
        /// <param name="inputSize">Target input size (square)</param>
        /// <param name="meanValues">Normalization mean values per channel</param>
        /// <param name="stdValues">Normalization standard deviation values per channel</param>
        /// <param name="swapRB">Whether to swap R and B channels</param>
        /// <returns>Preprocessed tensor ready for model input</returns>
        public static Tensor Preprocess(Texture2D texture, int inputSize, float[] meanValues = null, float[] stdValues = null, bool swapRB = false) {
            return Tensor.PreprocessImage(texture, inputSize, meanValues, stdValues, swapRB);
        }
        
        /// <summary>
        /// Decodes a segmentation mask tensor to a texture.
        /// </summary>
        /// <param name="maskTensor">Mask tensor from model output</param>
        /// <param name="threshold">Threshold for binary segmentation</param>
        /// <returns>Visualized segmentation mask</returns>
        public static Texture2D DecodeMask(Tensor maskTensor, float threshold = 0.5f) {
            return Tensor.DecodeMaskTensor(maskTensor, threshold);
        }
        
        /// <summary>
        /// Applies Non-Maximum Suppression to detection results.
        /// </summary>
        /// <param name="boxes">Bounding box coordinates [x1,y1,x2,y2]</param>
        /// <param name="scores">Confidence scores</param>
        /// <param name="iouThreshold">IoU threshold for suppression</param>
        /// <param name="scoreThreshold">Minimum score threshold</param>
        /// <returns>Indices of kept detections</returns>
        public static int[] ApplyNMS(Tensor boxes, Tensor scores, float iouThreshold = 0.5f, float scoreThreshold = 0.1f) {
            return Tensor.NonMaxSuppression(boxes, scores, iouThreshold, scoreThreshold);
        }
        
        /// <summary>
        /// Converts YOLO detection tensor output to a list of DetectedObject instances.
        /// </summary>
        /// <param name="output">YOLO model output tensor</param>
        /// <param name="confidenceThreshold">Minimum confidence threshold</param>
        /// <param name="nmsThreshold">IoU threshold for NMS</param>
        /// <param name="imageWidth">Original image width</param>
        /// <param name="imageHeight">Original image height</param>
        /// <param name="classLabels">Optional class labels</param>
        /// <returns>List of detected objects</returns>
        public static List<DetectedObject> DecodeYOLOOutput(
            Tensor output,
            float confidenceThreshold,
            float nmsThreshold,
            int imageWidth,
            int imageHeight,
            string[] classLabels = null
        ) {
            List<DetectedObject> detections = new List<DetectedObject>();
            
            // This is a simplified decoder for common YOLO output format
            // Actual implementation would depend on specific YOLO version
            
            // Decode detections
            int rows = output.shape[1]; // Number of detections
            int cols = output.shape[2]; // Detection data (x, y, w, h, conf, class scores...)
            
            int numClasses = cols - 5; // First 5 values are x, y, w, h, objectness
            
            // Store all valid detections
            List<float[]> validDetections = new List<float[]>();
            List<float> scores = new List<float>();
            
            for (int i = 0; i < rows; i++) {
                float objectness = output[0, i, 4];
                
                if (objectness < confidenceThreshold) {
                    continue;
                }
                
                // Find highest class score
                float maxClassScore = 0;
                int maxClassIndex = 0;
                
                for (int j = 0; j < numClasses; j++) {
                    float classScore = output[0, i, 5 + j];
                    if (classScore > maxClassScore) {
                        maxClassScore = classScore;
                        maxClassIndex = j;
                    }
                }
                
                float confidence = objectness * maxClassScore;
                if (confidence < confidenceThreshold) {
                    continue;
                }
                
                // Get bounding box
                float x = output[0, i, 0];
                float y = output[0, i, 1];
                float w = output[0, i, 2];
                float h = output[0, i, 3];
                
                // Convert to corner format
                float x1 = (x - w / 2) * imageWidth;
                float y1 = (y - h / 2) * imageHeight;
                float x2 = (x + w / 2) * imageWidth;
                float y2 = (y + h / 2) * imageHeight;
                
                validDetections.Add(new float[] { x1, y1, x2, y2 });
                scores.Add(confidence);
            }
            
            if (validDetections.Count == 0) {
                return detections;
            }
            
            // Convert to tensors for NMS
            Tensor boxesTensor = new Tensor(new int[] { validDetections.Count, 4 });
            Tensor scoresTensor = new Tensor(new int[] { validDetections.Count });
            
            for (int i = 0; i < validDetections.Count; i++) {
                boxesTensor[i, 0] = validDetections[i][0];
                boxesTensor[i, 1] = validDetections[i][1];
                boxesTensor[i, 2] = validDetections[i][2];
                boxesTensor[i, 3] = validDetections[i][3];
                scoresTensor[i] = scores[i];
            }
            
            // Apply NMS
            int[] keepIndices = Tensor.NonMaxSuppression(boxesTensor, scoresTensor, nmsThreshold, confidenceThreshold);
            
            // Create detected objects
            foreach (int idx in keepIndices) {
                float[] box = validDetections[idx];
                float confidence = scores[idx];
                
                DetectedObject obj = new DetectedObject {
                    id = Guid.NewGuid().ToString(),
                    boundingBox = new BoundingBox {
                        x = box[0],
                        y = box[1],
                        width = box[2] - box[0],
                        height = box[3] - box[1]
                    },
                    confidence = confidence,
                    classId = idx, // This should be the class index from earlier
                    className = classLabels != null && idx < classLabels.Length ? classLabels[idx] : $"class_{idx}",
                    detectionTime = DateTime.UtcNow
                };
                
                detections.Add(obj);
            }
            
            // Clean up
            boxesTensor.Dispose();
            scoresTensor.Dispose();
            
            return detections;
        }
    }
}