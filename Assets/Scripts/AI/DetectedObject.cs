/*************************************************************************
 *  Traversify – DetectedObject.cs                                       *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:39:51 UTC                                     *
 *  Desc   : Represents an object detected by AI analysis in the         *
 *           Traversify environment generation pipeline. Stores          *
 *           detection coordinates, classification, confidence, and      *
 *           additional metadata for downstream processing.              *
 *************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Traversify.AI {
    /// <summary>
    /// Represents an object detected by AI models in the Traversify pipeline.
    /// Contains essential information about the detection including position,
    /// classification, confidence, and metadata for subsequent processing.
    /// </summary>
    [Serializable]
    public class DetectedObject {
        #region Core Properties
        
        /// <summary>
        /// Numeric class identifier.
        /// </summary>
        public int classId;
        
        /// <summary>
        /// Human-readable class name.
        /// </summary>
        public string className;
        
        /// <summary>
        /// Detection confidence score (0-1).
        /// </summary>
        public float confidence;
        
        /// <summary>
        /// Bounding box in pixel coordinates (top-left origin).
        /// </summary>
        public Rect boundingBox;
        
        /// <summary>
        /// Center point of the bounding box in pixel coordinates.
        /// </summary>
        public Vector2 center => new Vector2(
            boundingBox.x + boundingBox.width / 2,
            boundingBox.y + boundingBox.height / 2
        );
        
        /// <summary>
        /// Area of the bounding box in square pixels.
        /// </summary>
        public float area => boundingBox.width * boundingBox.height;
        
        /// <summary>
        /// Detection mask (if available from instance segmentation).
        /// </summary>
        public Texture2D mask;
        
        /// <summary>
        /// Optional classification details including probabilities for each class.
        /// </summary>
        public Dictionary<string, float> classScores;
        
        /// <summary>
        /// Optional metadata for additional properties.
        /// </summary>
        public Dictionary<string, object> metadata;
        
        /// <summary>
        /// Enhanced description of the object (if available).
        /// </summary>
        public string enhancedDescription;
        
        /// <summary>
        /// Indicates whether this object has been processed by the full pipeline.
        /// </summary>
        public bool isProcessed;
        
        /// <summary>
        /// Timestamp when the detection was created.
        /// </summary>
        public DateTime timestamp;
        
        /// <summary>
        /// Unique identifier for this detection.
        /// </summary>
        public string id;
        
        #endregion
        
        #region Constructors
        
        /// <summary>
        /// Default constructor.
        /// </summary>
        public DetectedObject() {
            timestamp = DateTime.UtcNow;
            id = Guid.NewGuid().ToString();
            classScores = new Dictionary<string, float>();
            metadata = new Dictionary<string, object>();
        }
        
        /// <summary>
        /// Constructor with basic detection information.
        /// </summary>
        /// <param name="classId">Numeric class identifier</param>
        /// <param name="className">Human-readable class name</param>
        /// <param name="confidence">Detection confidence (0-1)</param>
        /// <param name="boundingBox">Bounding box in pixel coordinates</param>
        public DetectedObject(int classId, string className, float confidence, Rect boundingBox)
            : this() {
            this.classId = classId;
            this.className = className;
            this.confidence = confidence;
            this.boundingBox = boundingBox;
        }
        
        /// <summary>
        /// Constructor with additional mask information.
        /// </summary>
        /// <param name="classId">Numeric class identifier</param>
        /// <param name="className">Human-readable class name</param>
        /// <param name="confidence">Detection confidence (0-1)</param>
        /// <param name="boundingBox">Bounding box in pixel coordinates</param>
        /// <param name="mask">Detection mask texture</param>
        public DetectedObject(int classId, string className, float confidence, Rect boundingBox, Texture2D mask)
            : this(classId, className, confidence, boundingBox) {
            this.mask = mask;
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Gets the normalized bounding box coordinates (0-1 range).
        /// </summary>
        /// <param name="imageWidth">Width of the source image</param>
        /// <param name="imageHeight">Height of the source image</param>
        /// <returns>Normalized rectangle with coordinates in 0-1 range</returns>
        public Rect GetNormalizedBoundingBox(int imageWidth, int imageHeight) {
            return new Rect(
                boundingBox.x / imageWidth,
                boundingBox.y / imageHeight,
                boundingBox.width / imageWidth,
                boundingBox.height / imageHeight
            );
        }
        
        /// <summary>
        /// Gets the normalized center point (0-1 range).
        /// </summary>
        /// <param name="imageWidth">Width of the source image</param>
        /// <param name="imageHeight">Height of the source image</param>
        /// <returns>Normalized center position in 0-1 range</returns>
        public Vector2 GetNormalizedCenter(int imageWidth, int imageHeight) {
            return new Vector2(
                center.x / imageWidth,
                center.y / imageHeight
            );
        }
        
        /// <summary>
        /// Gets the best class and score from the classScores dictionary.
        /// </summary>
        /// <returns>Tuple containing the best class name and score</returns>
        public (string className, float score) GetBestClass() {
            if (classScores == null || classScores.Count == 0) {
                return (className, confidence);
            }
            
            string bestClass = className;
            float bestScore = 0;
            
            foreach (var kvp in classScores) {
                if (kvp.Value > bestScore) {
                    bestScore = kvp.Value;
                    bestClass = kvp.Key;
                }
            }
            
            return (bestClass, bestScore);
        }
        
        /// <summary>
        /// Checks if this detection overlaps with another detection.
        /// </summary>
        /// <param name="other">Other detection to compare with</param>
        /// <param name="iouThreshold">IoU threshold for considering as overlap (0-1)</param>
        /// <returns>True if detections overlap above the threshold</returns>
        public bool OverlapsWith(DetectedObject other, float iouThreshold = 0.5f) {
            if (other == null) return false;
            
            float iou = CalculateIoU(this.boundingBox, other.boundingBox);
            return iou >= iouThreshold;
        }
        
        /// <summary>
        /// Calculates the distance to another detection (center to center).
        /// </summary>
        /// <param name="other">Other detection to calculate distance to</param>
        /// <returns>Distance in pixels between detection centers</returns>
        public float DistanceTo(DetectedObject other) {
            if (other == null) return float.MaxValue;
            
            return Vector2.Distance(this.center, other.center);
        }
        
        /// <summary>
        /// Creates a deep copy of this detection object.
        /// </summary>
        /// <returns>A new DetectedObject with copied properties</returns>
        public DetectedObject Clone() {
            DetectedObject clone = new DetectedObject {
                classId = this.classId,
                className = this.className,
                confidence = this.confidence,
                boundingBox = this.boundingBox,
                enhancedDescription = this.enhancedDescription,
                isProcessed = this.isProcessed,
                timestamp = this.timestamp,
                id = this.id
            };
            
            // Copy mask if exists (create new texture to avoid reference issues)
            if (this.mask != null) {
                clone.mask = new Texture2D(this.mask.width, this.mask.height, this.mask.format, false);
                clone.mask.SetPixels(this.mask.GetPixels());
                clone.mask.Apply();
            }
            
            // Copy dictionaries
            if (this.classScores != null) {
                clone.classScores = new Dictionary<string, float>(this.classScores);
            }
            
            if (this.metadata != null) {
                clone.metadata = new Dictionary<string, object>(this.metadata);
            }
            
            return clone;
        }
        
        /// <summary>
        /// Merges information from another detection into this one.
        /// </summary>
        /// <param name="other">Other detection to merge from</param>
        /// <param name="mergeConfidence">Whether to update confidence with maximum value</param>
        /// <param name="mergeBoundingBox">Whether to update bounding box with the larger one</param>
        /// <returns>This object after merging</returns>
        public DetectedObject MergeWith(DetectedObject other, bool mergeConfidence = true, bool mergeBoundingBox = false) {
            if (other == null) return this;
            
            // Merge class information
            if (mergeConfidence && other.confidence > this.confidence) {
                this.confidence = other.confidence;
                this.classId = other.classId;
                this.className = other.className;
            }
            
            // Merge bounding box (take the union)
            if (mergeBoundingBox) {
                float xMin = Mathf.Min(this.boundingBox.xMin, other.boundingBox.xMin);
                float yMin = Mathf.Min(this.boundingBox.yMin, other.boundingBox.yMin);
                float xMax = Mathf.Max(this.boundingBox.xMax, other.boundingBox.xMax);
                float yMax = Mathf.Max(this.boundingBox.yMax, other.boundingBox.yMax);
                
                this.boundingBox = new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            }
            
            // Merge class scores
            if (other.classScores != null) {
                if (this.classScores == null) {
                    this.classScores = new Dictionary<string, float>();
                }
                
                foreach (var kvp in other.classScores) {
                    if (!this.classScores.ContainsKey(kvp.Key) || this.classScores[kvp.Key] < kvp.Value) {
                        this.classScores[kvp.Key] = kvp.Value;
                    }
                }
            }
            
            // Merge metadata
            if (other.metadata != null) {
                if (this.metadata == null) {
                    this.metadata = new Dictionary<string, object>();
                }
                
                foreach (var kvp in other.metadata) {
                    if (!this.metadata.ContainsKey(kvp.Key)) {
                        this.metadata[kvp.Key] = kvp.Value;
                    }
                }
            }
            
            // Use most detailed description
            if (!string.IsNullOrEmpty(other.enhancedDescription) && 
                (string.IsNullOrEmpty(this.enhancedDescription) || 
                 other.enhancedDescription.Length > this.enhancedDescription.Length)) {
                this.enhancedDescription = other.enhancedDescription;
            }
            
            return this;
        }
        
        /// <summary>
        /// Returns a string representation of this detection.
        /// </summary>
        public override string ToString() {
            return $"DetectedObject[{id}]: Class={className} ({classId}), Confidence={confidence:F2}, " +
                   $"BBox=({boundingBox.x:F1},{boundingBox.y:F1},{boundingBox.width:F1},{boundingBox.height:F1})";
        }
        
        #endregion
        
        #region Helper Methods
        
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
}