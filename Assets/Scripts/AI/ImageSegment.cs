/*************************************************************************
 *  Traversify – ImageSegment.cs                                         *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:44:14 UTC                                     *
 *  Desc   : Represents a segmented region in an image, combining        *
 *           detection information with pixel-precise mask data.         *
 *           Essential for accurate terrain and object segmentation      *
 *           in the Traversify environment generation pipeline.          *
 *************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Traversify.AI {
    /// <summary>
    /// Represents a segmented region in an image, combining detection information
    /// with pixel-precise mask data for accurate environment generation.
    /// </summary>
    [Serializable]
    public class ImageSegment {
        #region Properties
        
        /// <summary>
        /// The detected object associated with this segment.
        /// </summary>
        public DetectedObject detectedObject;
        
        /// <summary>
        /// Alpha mask texture defining the segment shape (white pixels are inside the segment).
        /// </summary>
        public Texture2D mask;
        
        /// <summary>
        /// Bounding box of the segment in pixel coordinates.
        /// </summary>
        public Rect boundingBox;
        
        /// <summary>
        /// Color for visualizing this segment.
        /// </summary>
        public Color color;
        
        /// <summary>
        /// Area of the segment in square pixels.
        /// </summary>
        public float area;
        
        /// <summary>
        /// Confidence score for this segment (0-1).
        /// </summary>
        public float confidence => detectedObject?.confidence ?? 0f;
        
        /// <summary>
        /// Class name of the segment.
        /// </summary>
        public string className => detectedObject?.className ?? "unknown";
        
        /// <summary>
        /// Class ID of the segment.
        /// </summary>
        public int classId => detectedObject?.classId ?? -1;
        
        /// <summary>
        /// Center point of the segment in pixel coordinates.
        /// </summary>
        public Vector2 center => new Vector2(
            boundingBox.x + boundingBox.width / 2,
            boundingBox.y + boundingBox.height / 2
        );
        
        /// <summary>
        /// Timestamp when the segment was created.
        /// </summary>
        public DateTime timestamp;
        
        /// <summary>
        /// Unique identifier for this segment.
        /// </summary>
        public string id;
        
        /// <summary>
        /// Optional metadata for additional properties.
        /// </summary>
        public Dictionary<string, object> metadata;
        
        /// <summary>
        /// Flag indicating whether this segment represents terrain.
        /// </summary>
        public bool isTerrain;
        
        /// <summary>
        /// Estimated height value for terrain segments.
        /// </summary>
        public float estimatedHeight;
        
        /// <summary>
        /// Flag indicating whether this segment has been processed.
        /// </summary>
        public bool isProcessed;
        
        #endregion
        
        #region Constructors
        
        /// <summary>
        /// Default constructor.
        /// </summary>
        public ImageSegment() {
            timestamp = DateTime.UtcNow;
            id = Guid.NewGuid().ToString();
            metadata = new Dictionary<string, object>();
            color = UnityEngine.Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.6f, 1f);
        }
        
        /// <summary>
        /// Constructor with detection information.
        /// </summary>
        /// <param name="detectedObject">Associated detected object</param>
        public ImageSegment(DetectedObject detectedObject) : this() {
            this.detectedObject = detectedObject;
            this.boundingBox = detectedObject.boundingBox;
            this.area = detectedObject.area;
        }
        
        /// <summary>
        /// Constructor with detection and mask information.
        /// </summary>
        /// <param name="detectedObject">Associated detected object</param>
        /// <param name="mask">Segment mask texture</param>
        public ImageSegment(DetectedObject detectedObject, Texture2D mask) : this(detectedObject) {
            this.mask = mask;
            CalculateActualArea();
        }
        
        /// <summary>
        /// Constructor with all primary properties.
        /// </summary>
        /// <param name="detectedObject">Associated detected object</param>
        /// <param name="mask">Segment mask texture</param>
        /// <param name="boundingBox">Bounding box in pixel coordinates</param>
        /// <param name="color">Color for visualization</param>
        public ImageSegment(DetectedObject detectedObject, Texture2D mask, Rect boundingBox, Color color) : this() {
            this.detectedObject = detectedObject;
            this.mask = mask;
            this.boundingBox = boundingBox;
            this.color = color;
            CalculateActualArea();
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Calculates the actual area of the segment based on the mask.
        /// </summary>
        /// <returns>The calculated area in square pixels</returns>
        public float CalculateActualArea() {
            if (mask == null) {
                // If no mask, use bounding box area
                area = boundingBox.width * boundingBox.height;
                return area;
            }
            
            // Count non-transparent pixels in the mask
            Color[] pixels = mask.GetPixels();
            int count = 0;
            
            foreach (Color pixel in pixels) {
                if (pixel.a > 0.5f) {
                    count++;
                }
            }
            
            area = count;
            return area;
        }
        
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
        /// Checks if a point is inside this segment.
        /// </summary>
        /// <param name="point">Point in pixel coordinates</param>
        /// <returns>True if the point is inside this segment</returns>
        public bool ContainsPoint(Vector2 point) {
            // First, quick check with bounding box
            if (!boundingBox.Contains(point)) {
                return false;
            }
            
            // If no mask, use bounding box
            if (mask == null) {
                return true;
            }
            
            // Check against mask
            int maskX = Mathf.FloorToInt((point.x - boundingBox.x) / boundingBox.width * mask.width);
            int maskY = Mathf.FloorToInt((point.y - boundingBox.y) / boundingBox.height * mask.height);
            
            // Clamp to mask bounds
            maskX = Mathf.Clamp(maskX, 0, mask.width - 1);
            maskY = Mathf.Clamp(maskY, 0, mask.height - 1);
            
            Color pixelColor = mask.GetPixel(maskX, maskY);
            return pixelColor.a > 0.5f;
        }
        
        /// <summary>
        /// Checks if this segment overlaps with another segment.
        /// </summary>
        /// <param name="other">Other segment to compare with</param>
        /// <param name="useIoU">Whether to use IoU for comparison (more accurate but slower)</param>
        /// <param name="threshold">Overlap threshold (0-1)</param>
        /// <returns>True if segments overlap above the threshold</returns>
        public bool OverlapsWith(ImageSegment other, bool useIoU = true, float threshold = 0.5f) {
            if (other == null) return false;
            
            // Quick check with bounding boxes
            if (!boundingBox.Overlaps(other.boundingBox)) {
                return false;
            }
            
            if (useIoU && mask != null && other.mask != null) {
                // Calculate IoU using masks
                float iou = CalculateMaskIoU(this, other);
                return iou >= threshold;
            } else {
                // Fallback to bounding box IoU
                float iou = CalculateRectIoU(boundingBox, other.boundingBox);
                return iou >= threshold;
            }
        }
        
        /// <summary>
        /// Creates a deep copy of this segment.
        /// </summary>
        /// <returns>A new ImageSegment with copied properties</returns>
        public ImageSegment Clone() {
            ImageSegment clone = new ImageSegment {
                detectedObject = detectedObject?.Clone(),
                boundingBox = boundingBox,
                color = color,
                area = area,
                timestamp = timestamp,
                id = id,
                isTerrain = isTerrain,
                estimatedHeight = estimatedHeight,
                isProcessed = isProcessed
            };
            
            // Copy mask if exists
            if (mask != null) {
                clone.mask = new Texture2D(mask.width, mask.height, mask.format, false);
                clone.mask.SetPixels(mask.GetPixels());
                clone.mask.Apply();
            }
            
            // Copy metadata
            if (metadata != null) {
                clone.metadata = new Dictionary<string, object>(metadata);
            }
            
            return clone;
        }
        
        /// <summary>
        /// Extracts a cropped portion of the source image corresponding to this segment.
        /// </summary>
        /// <param name="sourceImage">Source image containing the segment</param>
        /// <param name="applyMask">Whether to apply the segment mask</param>
        /// <returns>Cropped image of the segment</returns>
        public Texture2D ExtractSegmentImage(Texture2D sourceImage, bool applyMask = true) {
            if (sourceImage == null) {
                return null;
            }
            
            // Calculate crop region
            int x = Mathf.FloorToInt(boundingBox.x);
            int y = Mathf.FloorToInt(boundingBox.y);
            int width = Mathf.FloorToInt(boundingBox.width);
            int height = Mathf.FloorToInt(boundingBox.height);
            
            // Clamp to source image bounds
            x = Mathf.Clamp(x, 0, sourceImage.width - 1);
            y = Mathf.Clamp(y, 0, sourceImage.height - 1);
            width = Mathf.Clamp(width, 1, sourceImage.width - x);
            height = Mathf.Clamp(height, 1, sourceImage.height - y);
            
            // Create cropped texture
            Texture2D croppedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = sourceImage.GetPixels(x, y, width, height);
            
            // Apply mask if requested
            if (applyMask && mask != null) {
                // Resize mask to match cropped dimensions if needed
                Texture2D resizedMask = mask;
                if (mask.width != width || mask.height != height) {
                    resizedMask = ResizeMask(mask, width, height);
                }
                
                Color[] maskPixels = resizedMask.GetPixels();
                for (int i = 0; i < pixels.Length; i++) {
                    pixels[i].a = maskPixels[i].a;
                }
                
                if (resizedMask != mask) {
                    UnityEngine.Object.Destroy(resizedMask);
                }
            }
            
            croppedTexture.SetPixels(pixels);
            croppedTexture.Apply();
            
            return croppedTexture;
        }
        
        /// <summary>
        /// Returns a string representation of this segment.
        /// </summary>
        public override string ToString() {
            return $"ImageSegment[{id}]: Class={className}, BBox=({boundingBox.x:F1},{boundingBox.y:F1}," +
                   $"{boundingBox.width:F1},{boundingBox.height:F1}), Area={area:F0}, {(isTerrain ? "Terrain" : "Object")}";
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Calculates the Intersection over Union (IoU) between two segments using their masks.
        /// </summary>
        private static float CalculateMaskIoU(ImageSegment segA, ImageSegment segB) {
            // First find the intersection of bounding boxes
            Rect intersection = new Rect {
                xMin = Mathf.Max(segA.boundingBox.xMin, segB.boundingBox.xMin),
                yMin = Mathf.Max(segA.boundingBox.yMin, segB.boundingBox.yMin),
                xMax = Mathf.Min(segA.boundingBox.xMax, segB.boundingBox.xMax),
                yMax = Mathf.Min(segA.boundingBox.yMax, segB.boundingBox.yMax)
            };
            
            if (intersection.width <= 0 || intersection.height <= 0) {
                return 0f;
            }
            
            int width = Mathf.FloorToInt(intersection.width);
            int height = Mathf.FloorToInt(intersection.height);
            
            // Count intersecting pixels in masks
            int intersectionCount = 0;
            int unionCount = 0;
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    // Map to original mask coordinates
                    float maskAX = (x + intersection.xMin - segA.boundingBox.xMin) / segA.boundingBox.width;
                    float maskAY = (y + intersection.yMin - segA.boundingBox.yMin) / segA.boundingBox.height;
                    float maskBX = (x + intersection.xMin - segB.boundingBox.xMin) / segB.boundingBox.width;
                    float maskBY = (y + intersection.yMin - segB.boundingBox.yMin) / segB.boundingBox.height;
                    
                    bool inMaskA = segA.mask.GetPixelBilinear(maskAX, maskAY).a > 0.5f;
                    bool inMaskB = segB.mask.GetPixelBilinear(maskBX, maskBY).a > 0.5f;
                    
                    if (inMaskA && inMaskB) {
                        intersectionCount++;
                    }
                    
                    if (inMaskA || inMaskB) {
                        unionCount++;
                    }
                }
            }
            
            return unionCount > 0 ? (float)intersectionCount / unionCount : 0f;
        }
        
        /// <summary>
        /// Calculates the Intersection over Union (IoU) between two rectangles.
        /// </summary>
        private static float CalculateRectIoU(Rect rectA, Rect rectB) {
            // Calculate intersection area
            float xMin = Mathf.Max(rectA.xMin, rectB.xMin);
            float yMin = Mathf.Max(rectA.yMin, rectB.yMin);
            float xMax = Mathf.Min(rectA.xMax, rectB.xMax);
            float yMax = Mathf.Min(rectA.yMax, rectB.yMax);
            
            float intersectionWidth = Mathf.Max(0, xMax - xMin);
            float intersectionHeight = Mathf.Max(0, yMax - yMin);
            float intersectionArea = intersectionWidth * intersectionHeight;
            
            // Calculate union area
            float rectAArea = rectA.width * rectA.height;
            float rectBArea = rectB.width * rectB.height;
            float unionArea = rectAArea + rectBArea - intersectionArea;
            
            // Calculate IoU
            if (unionArea > 0) {
                return intersectionArea / unionArea;
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Resizes a mask texture to the specified dimensions.
        /// </summary>
        private static Texture2D ResizeMask(Texture2D mask, int width, int height) {
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture prevRT = RenderTexture.active;
            
            Graphics.Blit(mask, rt);
            RenderTexture.active = rt;
            
            Texture2D resizedMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            resizedMask.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resizedMask.Apply();
            
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);
            
            return resizedMask;
        }
        
        #endregion
    }
}