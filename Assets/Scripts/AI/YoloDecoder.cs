/*************************************************************************
 *  Traversify – YoloDecoder.cs                                          *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:37:33 UTC                                     *
 *  Desc   : Decodes raw output from YOLO neural network models into     *
 *           DetectedObject instances. Provides robust non-maximum       *
 *           suppression, confidence thresholding, and coordinate        *
 *           transformation. Supports YOLOv8 through YOLOv12 formats.    *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;

namespace Traversify.AI {
    /// <summary>
    /// Decodes outputs from YOLO neural network models into usable object detections.
    /// Supports YOLOv8 through YOLOv12 output formats with flexible decoding options.
    /// </summary>
    public static class YoloDecoder {
        #region Public Decode Methods
        
        /// <summary>
        /// Decodes YOLO output tensor into a list of detected objects.
        /// </summary>
        /// <param name="outputTensor">The raw output tensor from the YOLO model</param>
        /// <param name="confidenceThreshold">Minimum confidence threshold (0-1)</param>
        /// <param name="nmsThreshold">Non-maximum suppression IoU threshold (0-1)</param>
        /// <param name="imageWidth">Original image width</param>
        /// <param name="imageHeight">Original image height</param>
        /// <returns>List of detected objects</returns>
        public static List<DetectedObject> Decode(
            Tensor outputTensor,
            float confidenceThreshold,
            float nmsThreshold,
            int imageWidth,
            int imageHeight
        ) {
            if (outputTensor == null) {
                TraversifyDebugger.Instance?.LogError("YOLO output tensor is null", LogCategory.AI);
                return new List<DetectedObject>();
            }
            
            // Determine YOLO model format based on tensor shape
            YoloModelFormat format = DetermineModelFormat(outputTensor);
            
            List<DetectedObject> detections;
            switch (format) {
                case YoloModelFormat.v8_Detect:
                    detections = DecodeYOLOv8Detect(outputTensor, confidenceThreshold, imageWidth, imageHeight);
                    break;
                case YoloModelFormat.v8_Segment:
                    detections = DecodeYOLOv8Segment(outputTensor, confidenceThreshold, imageWidth, imageHeight);
                    break;
                case YoloModelFormat.v12_Detect:
                    detections = DecodeYOLOv12Detect(outputTensor, confidenceThreshold, imageWidth, imageHeight);
                    break;
                default:
                    detections = DecodeGenericYOLO(outputTensor, confidenceThreshold, imageWidth, imageHeight);
                    break;
            }
            
            // Apply non-maximum suppression to filter out overlapping detections
            detections = ApplyNonMaximumSuppression(detections, nmsThreshold);
            
            TraversifyDebugger.Instance?.Log(
                $"Decoded {detections.Count} objects from YOLO ({format}) with confidence threshold {confidenceThreshold:F2}",
                LogCategory.AI
            );
            
            return detections;
        }
        
        /// <summary>
        /// Decodes YOLO output tensor into a list of detected objects with multi-class support.
        /// </summary>
        /// <param name="outputTensor">The raw output tensor from the YOLO model</param>
        /// <param name="confidenceThreshold">Minimum confidence threshold (0-1)</param>
        /// <param name="nmsThreshold">Non-maximum suppression IoU threshold (0-1)</param>
        /// <param name="imageWidth">Original image width</param>
        /// <param name="imageHeight">Original image height</param>
        /// <param name="classLabels">Array of class labels</param>
        /// <param name="maxDetectionsPerClass">Maximum detections per class (0 for unlimited)</param>
        /// <returns>List of detected objects</returns>
        public static List<DetectedObject> DecodeMultiClass(
            Tensor outputTensor,
            float confidenceThreshold,
            float nmsThreshold,
            int imageWidth,
            int imageHeight,
            string[] classLabels = null,
            int maxDetectionsPerClass = 0
        ) {
            if (outputTensor == null) {
                TraversifyDebugger.Instance?.LogError("YOLO output tensor is null", LogCategory.AI);
                return new List<DetectedObject>();
            }
            
            // Determine YOLO model format based on tensor shape
            YoloModelFormat format = DetermineModelFormat(outputTensor);
            
            // Decode all detections across all classes
            List<DetectedObject> allDetections;
            switch (format) {
                case YoloModelFormat.v8_Detect:
                    allDetections = DecodeYOLOv8Detect(outputTensor, 0, imageWidth, imageHeight);
                    break;
                case YoloModelFormat.v8_Segment:
                    allDetections = DecodeYOLOv8Segment(outputTensor, 0, imageWidth, imageHeight);
                    break;
                case YoloModelFormat.v12_Detect:
                    allDetections = DecodeYOLOv12Detect(outputTensor, 0, imageWidth, imageHeight);
                    break;
                default:
                    allDetections = DecodeGenericYOLO(outputTensor, 0, imageWidth, imageHeight);
                    break;
            }
            
            // Group by class
            var detectionsByClass = allDetections
                .Where(d => d.confidence >= confidenceThreshold)
                .GroupBy(d => d.classId)
                .ToDictionary(g => g.Key, g => g.ToList());
            
            // Apply NMS per class and collect results
            List<DetectedObject> finalDetections = new List<DetectedObject>();
            
            foreach (var classGroup in detectionsByClass) {
                List<DetectedObject> classDetections = ApplyNonMaximumSuppression(classGroup.Value, nmsThreshold);
                
                // Limit detections per class if specified
                if (maxDetectionsPerClass > 0 && classDetections.Count > maxDetectionsPerClass) {
                    classDetections = classDetections
                        .OrderByDescending(d => d.confidence)
                        .Take(maxDetectionsPerClass)
                        .ToList();
                }
                
                // Assign class names if available
                if (classLabels != null && classGroup.Key < classLabels.Length) {
                    foreach (var detection in classDetections) {
                        detection.className = classLabels[detection.classId];
                    }
                }
                
                finalDetections.AddRange(classDetections);
            }
            
            TraversifyDebugger.Instance?.Log(
                $"Decoded {finalDetections.Count} objects from YOLO ({format}) with confidence threshold {confidenceThreshold:F2}",
                LogCategory.AI
            );
            
            return finalDetections;
        }
        
        #endregion
        
        #region Model-Specific Decoders
        
        /// <summary>
        /// Decodes output from YOLOv8 detection model.
        /// </summary>
        private static List<DetectedObject> DecodeYOLOv8Detect(
            Tensor outputTensor, 
            float confidenceThreshold,
            int imageWidth, 
            int imageHeight
        ) {
            List<DetectedObject> detections = new List<DetectedObject>();
            int rows = outputTensor.shape[1];
            int cols = outputTensor.shape[2];
            
            // YOLOv8 Detect outputs: [batch, num_detections, num_classes+4]
            // First 4 values are [x, y, w, h], followed by class scores
            
            for (int i = 0; i < rows; i++) {
                // Get box confidence (maximum class score)
                float maxClassScore = 0;
                int maxClassIndex = 0;
                
                for (int j = 4; j < cols; j++) {
                    float score = outputTensor[0, i, j];
                    if (score > maxClassScore) {
                        maxClassScore = score;
                        maxClassIndex = j - 4;
                    }
                }
                
                if (maxClassScore >= confidenceThreshold) {
                    // Get bounding box coordinates (normalized)
                    float centerX = outputTensor[0, i, 0];
                    float centerY = outputTensor[0, i, 1];
                    float width = outputTensor[0, i, 2];
                    float height = outputTensor[0, i, 3];
                    
                    // Convert to pixel coordinates
                    float pixelX = centerX * imageWidth;
                    float pixelY = centerY * imageHeight;
                    float pixelWidth = width * imageWidth;
                    float pixelHeight = height * imageHeight;
                    
                    // Create rectangle with top-left origin
                    Rect boundingBox = new Rect(
                        pixelX - pixelWidth / 2,
                        pixelY - pixelHeight / 2,
                        pixelWidth,
                        pixelHeight
                    );
                    
                    detections.Add(new DetectedObject {
                        classId = maxClassIndex,
                        className = $"class_{maxClassIndex}",
                        confidence = maxClassScore,
                        boundingBox = boundingBox
                    });
                }
            }
            
            return detections;
        }
        
        /// <summary>
        /// Decodes output from YOLOv8 segmentation model.
        /// </summary>
        private static List<DetectedObject> DecodeYOLOv8Segment(
            Tensor outputTensor, 
            float confidenceThreshold,
            int imageWidth, 
            int imageHeight
        ) {
            List<DetectedObject> detections = new List<DetectedObject>();
            
            // YOLOv8-seg outputs multiple tensors, but here we assume the main detection tensor
            // is passed in, which has format [batch, num_detections, num_classes+4+num_mask_coeffs]
            
            int rows = outputTensor.shape[1];
            int cols = outputTensor.shape[2];
            int numClasses = 0;
            
            // Determine number of classes and mask coefficients
            // In YOLOv8-seg, we typically have 4 box coords, then class scores, then mask coefficients
            for (int j = 4; j < cols; j++) {
                // Check a few random rows to find where class scores end and mask coefficients begin
                // This is a heuristic since we don't know the exact model configuration
                float sumVal = 0;
                for (int i = 0; i < Math.Min(rows, 10); i++) {
                    sumVal += outputTensor[0, i, j];
                }
                
                // Mask coefficients have a different statistical distribution than class scores
                // Class scores often sum to near 1.0 (when sigmoid activated)
                if (sumVal > 0.1f && sumVal < 10f) {
                    numClasses++;
                }
            }
            
            // Process each detection
            for (int i = 0; i < rows; i++) {
                // Get box confidence (maximum class score)
                float maxClassScore = 0;
                int maxClassIndex = 0;
                
                for (int j = 4; j < 4 + numClasses; j++) {
                    float score = outputTensor[0, i, j];
                    if (score > maxClassScore) {
                        maxClassScore = score;
                        maxClassIndex = j - 4;
                    }
                }
                
                if (maxClassScore >= confidenceThreshold) {
                    // Get bounding box coordinates (normalized)
                    float centerX = outputTensor[0, i, 0];
                    float centerY = outputTensor[0, i, 1];
                    float width = outputTensor[0, i, 2];
                    float height = outputTensor[0, i, 3];
                    
                    // Convert to pixel coordinates
                    float pixelX = centerX * imageWidth;
                    float pixelY = centerY * imageHeight;
                    float pixelWidth = width * imageWidth;
                    float pixelHeight = height * imageHeight;
                    
                    // Create rectangle with top-left origin
                    Rect boundingBox = new Rect(
                        pixelX - pixelWidth / 2,
                        pixelY - pixelHeight / 2,
                        pixelWidth,
                        pixelHeight
                    );
                    
                    // Extract mask coefficients if available
                    float[] maskCoeffs = null;
                    if (cols > 4 + numClasses) {
                        int numCoeffs = cols - 4 - numClasses;
                        maskCoeffs = new float[numCoeffs];
                        for (int j = 0; j < numCoeffs; j++) {
                            maskCoeffs[j] = outputTensor[0, i, 4 + numClasses + j];
                        }
                    }
                    
                    var detection = new DetectedObject {
                        classId = maxClassIndex,
                        className = $"class_{maxClassIndex}",
                        confidence = maxClassScore,
                        boundingBox = boundingBox
                    };
                    
                    // Store mask coefficients for later processing
                    if (maskCoeffs != null) {
                        detection.metadata = new Dictionary<string, object> {
                            { "maskCoefficients", maskCoeffs }
                        };
                    }
                    
                    detections.Add(detection);
                }
            }
            
            return detections;
        }
        
        /// <summary>
        /// Decodes output from YOLOv12 detection model.
        /// </summary>
        private static List<DetectedObject> DecodeYOLOv12Detect(
            Tensor outputTensor, 
            float confidenceThreshold,
            int imageWidth, 
            int imageHeight
        ) {
            List<DetectedObject> detections = new List<DetectedObject>();
            
            // YOLOv12 output has format [batch, num_detections, 6+num_classes]
            // where 6 values are [x, y, w, h, objectness, largest_class_score]
            
            int rows = outputTensor.shape[1];
            int cols = outputTensor.shape[2];
            
            for (int i = 0; i < rows; i++) {
                // Get objectness score (confidence that box contains an object)
                float objectness = outputTensor[0, i, 4];
                
                // Short-circuit if objectness is too low
                if (objectness < confidenceThreshold) {
                    continue;
                }
                
                // Get largest class score and index
                float maxClassScore = 0;
                int maxClassIndex = 0;
                
                for (int j = 6; j < cols; j++) {
                    float score = outputTensor[0, i, j];
                    if (score > maxClassScore) {
                        maxClassScore = score;
                        maxClassIndex = j - 6;
                    }
                }
                
                // Compute final confidence as objectness * class score
                float confidence = objectness * maxClassScore;
                
                if (confidence >= confidenceThreshold) {
                    // Get bounding box coordinates (normalized)
                    float centerX = outputTensor[0, i, 0];
                    float centerY = outputTensor[0, i, 1];
                    float width = outputTensor[0, i, 2];
                    float height = outputTensor[0, i, 3];
                    
                    // Convert to pixel coordinates
                    float pixelX = centerX * imageWidth;
                    float pixelY = centerY * imageHeight;
                    float pixelWidth = width * imageWidth;
                    float pixelHeight = height * imageHeight;
                    
                    // Create rectangle with top-left origin
                    Rect boundingBox = new Rect(
                        pixelX - pixelWidth / 2,
                        pixelY - pixelHeight / 2,
                        pixelWidth,
                        pixelHeight
                    );
                    
                    detections.Add(new DetectedObject {
                        classId = maxClassIndex,
                        className = $"class_{maxClassIndex}",
                        confidence = confidence,
                        boundingBox = boundingBox
                    });
                }
            }
            
            return detections;
        }
        
        /// <summary>
        /// Generic YOLO decoder that attempts to handle various YOLO formats.
        /// </summary>
        private static List<DetectedObject> DecodeGenericYOLO(
            Tensor outputTensor, 
            float confidenceThreshold,
            int imageWidth, 
            int imageHeight
        ) {
            List<DetectedObject> detections = new List<DetectedObject>();
            
            // Check tensor dimensions
            if (outputTensor.shape.Length < 3) {
                TraversifyDebugger.Instance?.LogError(
                    $"Invalid YOLO output tensor shape: {string.Join(",", outputTensor.shape)}",
                    LogCategory.AI
                );
                return detections;
            }
            
            int batchSize = outputTensor.shape[0];
            int rows = outputTensor.shape[1];
            int cols = outputTensor.shape[2];
            
            // Assume first 4 values are [x, y, w, h]
            // Followed by objectness (if present) and class scores
            
            bool hasObjectness = false;
            int classOffset = 4;
            
            // Heuristic to detect if model has objectness score
            // Check a few rows to see if values at index 4 behave like objectness scores
            int checkRows = Math.Min(10, rows);
            float sum = 0;
            for (int i = 0; i < checkRows; i++) {
                sum += outputTensor[0, i, 4];
            }
            float average = sum / checkRows;
            
            // Objectness scores are typically between 0 and 1
            if (average >= 0 && average <= 1) {
                hasObjectness = true;
                classOffset = 5;
            }
            
            for (int i = 0; i < rows; i++) {
                float confidence;
                int bestClassId = 0;
                float bestClassScore = 0;
                
                // Find best class
                for (int j = classOffset; j < cols; j++) {
                    float score = outputTensor[0, i, j];
                    if (score > bestClassScore) {
                        bestClassScore = score;
                        bestClassId = j - classOffset;
                    }
                }
                
                // Calculate confidence
                if (hasObjectness) {
                    float objectness = outputTensor[0, i, 4];
                    confidence = objectness * bestClassScore;
                } else {
                    confidence = bestClassScore;
                }
                
                if (confidence >= confidenceThreshold) {
                    // Get bounding box coordinates (normalized)
                    float centerX = outputTensor[0, i, 0];
                    float centerY = outputTensor[0, i, 1];
                    float width = outputTensor[0, i, 2];
                    float height = outputTensor[0, i, 3];
                    
                    // Some YOLO variants output coordinates in different formats
                    // Check if coordinates are within 0-1 range (normalized)
                    bool isNormalized = true;
                    if (centerX > 1.0f || centerY > 1.0f || width > 1.0f || height > 1.0f) {
                        isNormalized = false;
                    }
                    
                    // Convert to pixel coordinates
                    float pixelX, pixelY, pixelWidth, pixelHeight;
                    
                    if (isNormalized) {
                        pixelX = centerX * imageWidth;
                        pixelY = centerY * imageHeight;
                        pixelWidth = width * imageWidth;
                        pixelHeight = height * imageHeight;
                    } else {
                        // Already in pixel coordinates
                        pixelX = centerX;
                        pixelY = centerY;
                        pixelWidth = width;
                        pixelHeight = height;
                    }
                    
                    // Create rectangle with top-left origin
                    Rect boundingBox = new Rect(
                        pixelX - pixelWidth / 2,
                        pixelY - pixelHeight / 2,
                        pixelWidth,
                        pixelHeight
                    );
                    
                    detections.Add(new DetectedObject {
                        classId = bestClassId,
                        className = $"class_{bestClassId}",
                        confidence = confidence,
                        boundingBox = boundingBox
                    });
                }
            }
            
            return detections;
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Determines the YOLO model format based on the output tensor shape.
        /// </summary>
        private static YoloModelFormat DetermineModelFormat(Tensor tensor) {
            if (tensor == null || tensor.shape.Length < 3) {
                return YoloModelFormat.Unknown;
            }
            
            int batchSize = tensor.shape[0];
            int rows = tensor.shape[1];
            int cols = tensor.shape[2];
            
            // Check if tensor has extra batch dimension
            if (batchSize != 1) {
                return YoloModelFormat.Unknown;
            }
            
            // Check for YOLOv8 detection format
            if (cols >= 85) {  // 4 box coords + 80 COCO classes + 1 objectness
                // Check if it's a segmentation model by heuristic
                // Segmentation models typically have many more columns for mask coefficients
                if (cols > 100) {
                    return YoloModelFormat.v8_Segment;
                }
                return YoloModelFormat.v8_Detect;
            }
            
            // Check for YOLOv12 format (has built-in objectness + max_score)
            if (cols >= 86) {  // 4 box coords + 1 objectness + 1 max_score + 80 classes
                return YoloModelFormat.v12_Detect;
            }
            
            // Default to generic format
            return YoloModelFormat.Generic;
        }
        
        /// <summary>
        /// Applies non-maximum suppression to filter out overlapping detections.
        /// </summary>
        private static List<DetectedObject> ApplyNonMaximumSuppression(
            List<DetectedObject> detections,
            float nmsThreshold
        ) {
            if (detections == null || detections.Count == 0) {
                return new List<DetectedObject>();
            }
            
            // Sort by confidence (descending)
            var sortedDetections = detections
                .OrderByDescending(d => d.confidence)
                .ToList();
            
            List<DetectedObject> result = new List<DetectedObject>();
            HashSet<int> indicesToRemove = new HashSet<int>();
            
            for (int i = 0; i < sortedDetections.Count; i++) {
                if (indicesToRemove.Contains(i)) {
                    continue;
                }
                
                result.Add(sortedDetections[i]);
                
                for (int j = i + 1; j < sortedDetections.Count; j++) {
                    if (indicesToRemove.Contains(j)) {
                        continue;
                    }
                    
                    float iou = CalculateIoU(
                        sortedDetections[i].boundingBox,
                        sortedDetections[j].boundingBox
                    );
                    
                    if (iou > nmsThreshold) {
                        indicesToRemove.Add(j);
                    }
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Calculates the Intersection over Union (IoU) between two bounding boxes.
        /// </summary>
        private static float CalculateIoU(Rect boxA, Rect boxB) {
            // Calculate intersection area
            float xMin = Mathf.Max(boxA.xMin, boxB.xMin);
            float yMin = Mathf.Max(boxA.yMin, boxB.yMin);
            float xMax = Mathf.Min(boxA.xMax, boxB.xMax);
            float yMax = Mathf.Min(boxA.yMax, boxB.yMax);
            
            float intersectionWidth = Mathf.Max(0, xMax - xMin);
            float intersectionHeight = Mathf.Max(0, yMax - yMin);
            float intersectionArea = intersectionWidth * intersectionHeight;
            
            // Calculate union area
            float boxAArea = boxA.width * boxA.height;
            float boxBArea = boxB.width * boxB.height;
            float unionArea = boxAArea + boxBArea - intersectionArea;
            
            // Calculate IoU
            if (unionArea > 0) {
                return intersectionArea / unionArea;
            }
            
            return 0;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Enum representing different YOLO model formats.
    /// </summary>
    public enum YoloModelFormat {
        Unknown,
        Generic,
        v8_Detect,
        v8_Segment,
        v12_Detect
    }
}