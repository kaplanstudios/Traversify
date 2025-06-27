/*************************************************************************
 *  Traversify – MaskUtils.cs                                            *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:32:49 UTC                                     *
 *  Desc   : Utility methods for creating, analyzing, comparing, and     *
 *           manipulating segmentation masks. Provides essential         *
 *           functionality for terrain and object segmentation in the    *
 *           Traversify environment generation pipeline.                 *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;

namespace Traversify.AI {
    /// <summary>
    /// Provides utility methods for working with segmentation masks.
    /// </summary>
    public static class MaskUtils {
        #region Mask Creation
        
        /// <summary>
        /// Creates a solid mask (all white) for the specified bounding box.
        /// </summary>
        /// <param name="bounds">Bounds of the mask in pixels</param>
        /// <param name="imageWidth">Width of the reference image</param>
        /// <param name="imageHeight">Height of the reference image</param>
        /// <returns>A solid mask texture</returns>
        public static Texture2D CreateSolidMask(Rect bounds, int imageWidth, int imageHeight) {
            int width = Mathf.RoundToInt(bounds.width);
            int height = Mathf.RoundToInt(bounds.height);
            
            // Create a white mask texture
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = Enumerable.Repeat(Color.white, width * height).ToArray();
            mask.SetPixels(pixels);
            mask.Apply();
            
            return mask;
        }
        
        /// <summary>
        /// Creates a circular mask centered in the specified bounding box.
        /// </summary>
        /// <param name="bounds">Bounds of the mask in pixels</param>
        /// <param name="featherRadius">Radius of soft edge in pixels (0 for hard edge)</param>
        /// <returns>A circular mask texture</returns>
        public static Texture2D CreateCircularMask(Rect bounds, float featherRadius = 0) {
            int width = Mathf.RoundToInt(bounds.width);
            int height = Mathf.RoundToInt(bounds.height);
            
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            float centerX = width / 2f;
            float centerY = height / 2f;
            float radius = Mathf.Min(centerX, centerY);
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = y * width + x;
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));
                    
                    if (featherRadius > 0) {
                        // Soft edge
                        float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(radius - featherRadius, radius, distance));
                        pixels[index] = new Color(1, 1, 1, alpha);
                    } else {
                        // Hard edge
                        pixels[index] = distance <= radius ? Color.white : Color.clear;
                    }
                }
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            
            return mask;
        }
        
        /// <summary>
        /// Creates a rectangular mask with optional rounded corners.
        /// </summary>
        /// <param name="bounds">Bounds of the mask in pixels</param>
        /// <param name="cornerRadius">Radius of rounded corners in pixels (0 for sharp corners)</param>
        /// <param name="featherRadius">Radius of soft edge in pixels (0 for hard edge)</param>
        /// <returns>A rectangular mask texture</returns>
        public static Texture2D CreateRectangularMask(Rect bounds, float cornerRadius = 0, float featherRadius = 0) {
            int width = Mathf.RoundToInt(bounds.width);
            int height = Mathf.RoundToInt(bounds.height);
            
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = y * width + x;
                    
                    if (cornerRadius <= 0) {
                        // Simple rectangle
                        pixels[index] = Color.white;
                        continue;
                    }
                    
                    // Calculate distance to nearest corner
                    float distToCorner = float.MaxValue;
                    
                    // Top-left
                    if (x < cornerRadius && y < cornerRadius) {
                        distToCorner = Vector2.Distance(new Vector2(x, y), new Vector2(cornerRadius, cornerRadius));
                    }
                    // Top-right
                    else if (x > width - cornerRadius && y < cornerRadius) {
                        distToCorner = Vector2.Distance(new Vector2(x, y), new Vector2(width - cornerRadius, cornerRadius));
                    }
                    // Bottom-left
                    else if (x < cornerRadius && y > height - cornerRadius) {
                        distToCorner = Vector2.Distance(new Vector2(x, y), new Vector2(cornerRadius, height - cornerRadius));
                    }
                    // Bottom-right
                    else if (x > width - cornerRadius && y > height - cornerRadius) {
                        distToCorner = Vector2.Distance(new Vector2(x, y), new Vector2(width - cornerRadius, height - cornerRadius));
                    }
                    // Inside rectangle bounds
                    else {
                        pixels[index] = Color.white;
                        continue;
                    }
                    
                    if (featherRadius > 0) {
                        // Soft edge
                        float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(cornerRadius - featherRadius, cornerRadius, distToCorner));
                        pixels[index] = new Color(1, 1, 1, alpha);
                    } else {
                        // Hard edge
                        pixels[index] = distToCorner <= cornerRadius ? Color.white : Color.clear;
                    }
                }
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            
            return mask;
        }
        
        /// <summary>
        /// Creates a mask from a polygon defined by points.
        /// </summary>
        /// <param name="points">Array of points defining the polygon</param>
        /// <param name="width">Width of the output mask</param>
        /// <param name="height">Height of the output mask</param>
        /// <param name="featherRadius">Radius of soft edge in pixels (0 for hard edge)</param>
        /// <returns>A polygon mask texture</returns>
        public static Texture2D CreatePolygonMask(Vector2[] points, int width, int height, float featherRadius = 0) {
            if (points == null || points.Length < 3) {
                throw new ArgumentException("At least 3 points are required to create a polygon mask");
            }
            
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = y * width + x;
                    Vector2 point = new Vector2(x, y);
                    
                    if (IsPointInPolygon(point, points)) {
                        if (featherRadius > 0) {
                            float distToBoundary = DistanceToPolygonEdge(point, points);
                            float alpha = Mathf.Clamp01(Mathf.InverseLerp(0, featherRadius, distToBoundary));
                            pixels[index] = new Color(1, 1, 1, alpha);
                        } else {
                            pixels[index] = Color.white;
                        }
                    } else if (featherRadius > 0) {
                        float distToBoundary = DistanceToPolygonEdge(point, points);
                        if (distToBoundary <= featherRadius) {
                            float alpha = Mathf.Clamp01(1f - Mathf.InverseLerp(0, featherRadius, distToBoundary));
                            pixels[index] = new Color(1, 1, 1, alpha);
                        } else {
                            pixels[index] = Color.clear;
                        }
                    } else {
                        pixels[index] = Color.clear;
                    }
                }
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            
            return mask;
        }
        
        /// <summary>
        /// Creates a mask from a 2D grayscale heightmap.
        /// </summary>
        /// <param name="heightmap">Heightmap texture</param>
        /// <param name="threshold">Height threshold (0-1) for inclusion in mask</param>
        /// <returns>A mask based on height threshold</returns>
        public static Texture2D CreateMaskFromHeightmap(Texture2D heightmap, float threshold = 0.5f) {
            int width = heightmap.width;
            int height = heightmap.height;
            
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            Color[] heightPixels = heightmap.GetPixels();
            
            for (int i = 0; i < pixels.Length; i++) {
                // For grayscale heightmaps, red channel contains the height value
                float heightValue = heightPixels[i].r;
                pixels[i] = heightValue >= threshold ? Color.white : Color.clear;
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            
            return mask;
        }
        
        #endregion
        
        #region Mask Analysis
        
        /// <summary>
        /// Computes the similarity between two mask textures.
        /// </summary>
        /// <param name="maskA">First mask</param>
        /// <param name="maskB">Second mask</param>
        /// <returns>Similarity score between 0 (completely different) and 1 (identical)</returns>
        public static float ComputeMaskSimilarity(Texture2D maskA, Texture2D maskB) {
            if (maskA == null || maskB == null) {
                return 0f;
            }
            
            // If dimensions don't match, resize one to match the other
            Texture2D normalizedA = maskA;
            Texture2D normalizedB = maskB;
            bool createdTemp = false;
            
            if (maskA.width != maskB.width || maskA.height != maskB.height) {
                normalizedB = ResizeMask(maskB, maskA.width, maskA.height);
                createdTemp = true;
            }
            
            // Calculate Intersection over Union (IoU)
            Color[] pixelsA = normalizedA.GetPixels();
            Color[] pixelsB = normalizedB.GetPixels();
            
            int intersection = 0;
            int union = 0;
            
            for (int i = 0; i < pixelsA.Length; i++) {
                bool isSetA = pixelsA[i].a > 0.5f;
                bool isSetB = pixelsB[i].a > 0.5f;
                
                if (isSetA && isSetB) intersection++;
                if (isSetA || isSetB) union++;
            }
            
            if (createdTemp) {
                UnityEngine.Object.Destroy(normalizedB);
            }
            
            return union > 0 ? (float)intersection / union : 0f;
        }
        
        /// <summary>
        /// Computes more detailed similarity metrics between two masks.
        /// </summary>
        /// <param name="maskA">First mask</param>
        /// <param name="maskB">Second mask</param>
        /// <returns>Dictionary with various similarity metrics</returns>
        public static Dictionary<string, float> ComputeDetailedMaskSimilarity(Texture2D maskA, Texture2D maskB) {
            var metrics = new Dictionary<string, float>();
            
            if (maskA == null || maskB == null) {
                metrics["iou"] = 0f;
                metrics["dice"] = 0f;
                metrics["precision"] = 0f;
                metrics["recall"] = 0f;
                return metrics;
            }
            
            // If dimensions don't match, resize one to match the other
            Texture2D normalizedA = maskA;
            Texture2D normalizedB = maskB;
            bool createdTemp = false;
            
            if (maskA.width != maskB.width || maskA.height != maskB.height) {
                normalizedB = ResizeMask(maskB, maskA.width, maskA.height);
                createdTemp = true;
            }
            
            // Calculate metrics
            Color[] pixelsA = normalizedA.GetPixels();
            Color[] pixelsB = normalizedB.GetPixels();
            
            int truePositives = 0;
            int falsePositives = 0;
            int falseNegatives = 0;
            
            for (int i = 0; i < pixelsA.Length; i++) {
                bool isSetA = pixelsA[i].a > 0.5f;
                bool isSetB = pixelsB[i].a > 0.5f;
                
                if (isSetA && isSetB) truePositives++;
                if (!isSetA && isSetB) falsePositives++;
                if (isSetA && !isSetB) falseNegatives++;
            }
            
            int union = truePositives + falsePositives + falseNegatives;
            
            // Calculate IoU (Intersection over Union)
            metrics["iou"] = union > 0 ? (float)truePositives / union : 0f;
            
            // Calculate Dice coefficient (F1 score)
            metrics["dice"] = (truePositives > 0) 
                ? (2f * truePositives) / (2f * truePositives + falsePositives + falseNegatives) 
                : 0f;
            
            // Calculate precision
            metrics["precision"] = (truePositives + falsePositives > 0) 
                ? (float)truePositives / (truePositives + falsePositives) 
                : 0f;
            
            // Calculate recall
            metrics["recall"] = (truePositives + falseNegatives > 0) 
                ? (float)truePositives / (truePositives + falseNegatives) 
                : 0f;
            
            if (createdTemp) {
                UnityEngine.Object.Destroy(normalizedB);
            }
            
            return metrics;
        }
        
        /// <summary>
        /// Calculates the center of mass of a mask.
        /// </summary>
        /// <param name="mask">The mask texture</param>
        /// <returns>Center of mass as a normalized position (0-1)</returns>
        public static Vector2 CalculateCenterOfMass(Texture2D mask) {
            if (mask == null) {
                return Vector2.zero;
            }
            
            Color[] pixels = mask.GetPixels();
            float totalWeight = 0f;
            float weightedX = 0f;
            float weightedY = 0f;
            
            for (int y = 0; y < mask.height; y++) {
                for (int x = 0; x < mask.width; x++) {
                    int index = y * mask.width + x;
                    float weight = pixels[index].a;
                    
                    totalWeight += weight;
                    weightedX += x * weight;
                    weightedY += y * weight;
                }
            }
            
            if (totalWeight > 0) {
                return new Vector2(
                    weightedX / totalWeight / mask.width,
                    weightedY / totalWeight / mask.height
                );
            }
            
            return new Vector2(0.5f, 0.5f);
        }
        
        /// <summary>
        /// Calculates the axis-aligned bounding box of a mask.
        /// </summary>
        /// <param name="mask">The mask texture</param>
        /// <param name="threshold">Alpha threshold for inclusion (0-1)</param>
        /// <returns>Bounding box in pixel coordinates</returns>
        public static Rect CalculateBoundingBox(Texture2D mask, float threshold = 0.5f) {
            if (mask == null) {
                return new Rect(0, 0, 0, 0);
            }
            
            Color[] pixels = mask.GetPixels();
            int minX = mask.width;
            int minY = mask.height;
            int maxX = 0;
            int maxY = 0;
            bool foundPixel = false;
            
            for (int y = 0; y < mask.height; y++) {
                for (int x = 0; x < mask.width; x++) {
                    int index = y * mask.width + x;
                    if (pixels[index].a >= threshold) {
                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                        foundPixel = true;
                    }
                }
            }
            
            if (!foundPixel) {
                return new Rect(0, 0, mask.width, mask.height);
            }
            
            return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        
        /// <summary>
        /// Calculates the principal axis orientation of a mask.
        /// </summary>
        /// <param name="mask">The mask texture</param>
        /// <returns>Orientation angle in degrees</returns>
        public static float CalculateOrientation(Texture2D mask) {
            if (mask == null) {
                return 0f;
            }
            
            Color[] pixels = mask.GetPixels();
            Vector2 centroid = CalculateCenterOfMass(mask);
            centroid = new Vector2(centroid.x * mask.width, centroid.y * mask.height);
            
            float mxx = 0f, myy = 0f, mxy = 0f;
            float totalMass = 0f;
            
            for (int y = 0; y < mask.height; y++) {
                for (int x = 0; x < mask.width; x++) {
                    int index = y * mask.width + x;
                    float mass = pixels[index].a;
                    
                    if (mass > 0) {
                        float dx = x - centroid.x;
                        float dy = y - centroid.y;
                        
                        mxx += mass * dx * dx;
                        myy += mass * dy * dy;
                        mxy += mass * dx * dy;
                        totalMass += mass;
                    }
                }
            }
            
            if (totalMass > 0) {
                mxx /= totalMass;
                myy /= totalMass;
                mxy /= totalMass;
                
                // Calculate orientation using the covariance matrix
                if (Math.Abs(mxy) < 1e-6 && Math.Abs(mxx - myy) < 1e-6) {
                    return 0f; // Perfectly circular or symmetrical
                }
                
                float theta = 0.5f * Mathf.Atan2(2f * mxy, mxx - myy);
                return theta * Mathf.Rad2Deg;
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Calculates the area of a mask (count of non-transparent pixels).
        /// </summary>
        /// <param name="mask">The mask texture</param>
        /// <param name="threshold">Alpha threshold for inclusion (0-1)</param>
        /// <returns>Area in pixels</returns>
        public static int CalculateArea(Texture2D mask, float threshold = 0.5f) {
            if (mask == null) {
                return 0;
            }
            
            Color[] pixels = mask.GetPixels();
            int area = 0;
            
            for (int i = 0; i < pixels.Length; i++) {
                if (pixels[i].a >= threshold) {
                    area++;
                }
            }
            
            return area;
        }
        
        /// <summary>
        /// Calculates the perimeter of a mask (boundary length).
        /// </summary>
        /// <param name="mask">The mask texture</param>
        /// <param name="threshold">Alpha threshold for inclusion (0-1)</param>
        /// <returns>Perimeter length in pixels</returns>
        public static int CalculatePerimeter(Texture2D mask, float threshold = 0.5f) {
            if (mask == null) {
                return 0;
            }
            
            Color[] pixels = mask.GetPixels();
            int perimeter = 0;
            int width = mask.width;
            int height = mask.height;
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = y * width + x;
                    
                    if (pixels[index].a >= threshold) {
                        // Check if this pixel is on the boundary
                        bool isBoundary = false;
                        
                        // Check the 4 adjacent pixels
                        if (x > 0 && pixels[index - 1].a < threshold) isBoundary = true;
                        else if (x < width - 1 && pixels[index + 1].a < threshold) isBoundary = true;
                        else if (y > 0 && pixels[index - width].a < threshold) isBoundary = true;
                        else if (y < height - 1 && pixels[index + width].a < threshold) isBoundary = true;
                        
                        if (isBoundary) {
                            perimeter++;
                        }
                    }
                }
            }
            
            return perimeter;
        }
        
        /// <summary>
        /// Extracts the contour points from a mask.
        /// </summary>
        /// <param name="mask">The mask texture</param>
        /// <param name="threshold">Alpha threshold for inclusion (0-1)</param>
        /// <param name="simplifyTolerance">Distance tolerance for simplification (0 for no simplification)</param>
        /// <returns>Array of contour points</returns>
        public static Vector2[] ExtractContour(Texture2D mask, float threshold = 0.5f, float simplifyTolerance = 0) {
            if (mask == null) {
                return new Vector2[0];
            }
            
            // Find contour pixels
            List<Vector2> contourPixels = new List<Vector2>();
            Color[] pixels = mask.GetPixels();
            int width = mask.width;
            int height = mask.height;
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = y * width + x;
                    
                    if (pixels[index].a >= threshold) {
                        // Check if this pixel is on the boundary
                        bool isBoundary = false;
                        
                        // Check the 4 adjacent pixels
                        if (x > 0 && pixels[index - 1].a < threshold) isBoundary = true;
                        else if (x < width - 1 && pixels[index + 1].a < threshold) isBoundary = true;
                        else if (y > 0 && pixels[index - width].a < threshold) isBoundary = true;
                        else if (y < height - 1 && pixels[index + width].a < threshold) isBoundary = true;
                        
                        if (isBoundary) {
                            contourPixels.Add(new Vector2(x, y));
                        }
                    }
                }
            }
            
            // Sort contour pixels
            Vector2[] sortedContour = SortContourPoints(contourPixels.ToArray());
            
            // Simplify contour if needed
            if (simplifyTolerance > 0 && sortedContour.Length > 2) {
                return SimplifyPolyline(sortedContour, simplifyTolerance);
            }
            
            return sortedContour;
        }
        
        #endregion
        
        #region Mask Operations
        
        /// <summary>
        /// Combines two masks using the specified blend operation.
        /// </summary>
        /// <param name="maskA">First mask</param>
        /// <param name="maskB">Second mask</param>
        /// <param name="blendOp">Blend operation to use</param>
        /// <returns>Combined mask texture</returns>
        public static Texture2D CombineMasks(Texture2D maskA, Texture2D maskB, MaskBlendOperation blendOp) {
            if (maskA == null) return maskB ? new Texture2D(maskB.width, maskB.height) : null;
            if (maskB == null) return maskA ? new Texture2D(maskA.width, maskA.height) : null;
            
            // If dimensions don't match, resize one to match the other
            Texture2D normalizedA = maskA;
            Texture2D normalizedB = maskB;
            bool createdTemp = false;
            
            if (maskA.width != maskB.width || maskA.height != maskB.height) {
                normalizedB = ResizeMask(maskB, maskA.width, maskA.height);
                createdTemp = true;
            }
            
            int width = normalizedA.width;
            int height = normalizedA.height;
            
            Color[] pixelsA = normalizedA.GetPixels();
            Color[] pixelsB = normalizedB.GetPixels();
            Color[] result = new Color[pixelsA.Length];
            
            // Apply blend operation
            for (int i = 0; i < pixelsA.Length; i++) {
                float alphaA = pixelsA[i].a;
                float alphaB = pixelsB[i].a;
                
                switch (blendOp) {
                    case MaskBlendOperation.Add:
                        result[i] = new Color(1, 1, 1, Mathf.Clamp01(alphaA + alphaB));
                        break;
                    case MaskBlendOperation.Subtract:
                        result[i] = new Color(1, 1, 1, Mathf.Clamp01(alphaA - alphaB));
                        break;
                    case MaskBlendOperation.Multiply:
                        result[i] = new Color(1, 1, 1, alphaA * alphaB);
                        break;
                    case MaskBlendOperation.Minimum:
                        result[i] = new Color(1, 1, 1, Mathf.Min(alphaA, alphaB));
                        break;
                    case MaskBlendOperation.Maximum:
                        result[i] = new Color(1, 1, 1, Mathf.Max(alphaA, alphaB));
                        break;
                    case MaskBlendOperation.Average:
                        result[i] = new Color(1, 1, 1, (alphaA + alphaB) * 0.5f);
                        break;
                    case MaskBlendOperation.Difference:
                        result[i] = new Color(1, 1, 1, Mathf.Abs(alphaA - alphaB));
                        break;
                    default:
                        result[i] = new Color(1, 1, 1, Mathf.Max(alphaA, alphaB));
                        break;
                }
            }
            
            Texture2D combinedMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            combinedMask.SetPixels(result);
            combinedMask.Apply();
            
            if (createdTemp) {
                UnityEngine.Object.Destroy(normalizedB);
            }
            
            return combinedMask;
        }
        
        /// <summary>
        /// Erodes a mask by reducing its area.
        /// </summary>
        /// <param name="mask">The mask to erode</param>
        /// <param name="iterations">Number of erosion iterations</param>
        /// <returns>Eroded mask texture</returns>
        public static Texture2D ErodeMask(Texture2D mask, int iterations = 1) {
            if (mask == null || iterations <= 0) {
                return mask;
            }
            
            int width = mask.width;
            int height = mask.height;
            Color[] pixels = mask.GetPixels();
            Color[] result = new Color[pixels.Length];
            
            for (int iter = 0; iter < iterations; iter++) {
                // Copy current pixels for this iteration
                Array.Copy(pixels, result, pixels.Length);
                
                for (int y = 0; y < height; y++) {
                    for (int x = 0; x < width; x++) {
                        int index = y * width + x;
                        
                        // Skip transparent pixels
                        if (pixels[index].a < 0.5f) {
                            continue;
                        }
                        
                        // Check 4-neighborhood
                        bool shouldErode = false;
                        
                        // Left
                        if (x > 0 && pixels[index - 1].a < 0.5f) shouldErode = true;
                        // Right
                        else if (x < width - 1 && pixels[index + 1].a < 0.5f) shouldErode = true;
                        // Down
                        else if (y > 0 && pixels[index - width].a < 0.5f) shouldErode = true;
                        // Up
                        else if (y < height - 1 && pixels[index + width].a < 0.5f) shouldErode = true;
                        
                        if (shouldErode) {
                            result[index] = Color.clear;
                        }
                    }
                }
                
                // Update pixels for next iteration
                Array.Copy(result, pixels, pixels.Length);
            }
            
            Texture2D erodedMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            erodedMask.SetPixels(result);
            erodedMask.Apply();
            
            return erodedMask;
        }
        
        /// <summary>
        /// Dilates a mask by expanding its area.
        /// </summary>
        /// <param name="mask">The mask to dilate</param>
        /// <param name="iterations">Number of dilation iterations</param>
        /// <returns>Dilated mask texture</returns>
        public static Texture2D DilateMask(Texture2D mask, int iterations = 1) {
            if (mask == null || iterations <= 0) {
                return mask;
            }
            
            int width = mask.width;
            int height = mask.height;
            Color[] pixels = mask.GetPixels();
            Color[] result = new Color[pixels.Length];
            
            for (int iter = 0; iter < iterations; iter++) {
                // Copy current pixels for this iteration
                Array.Copy(pixels, result, pixels.Length);
                
                for (int y = 0; y < height; y++) {
                    for (int x = 0; x < width; x++) {
                        int index = y * width + x;
                        
                        // Skip opaque pixels
                        if (pixels[index].a >= 0.5f) {
                            continue;
                        }
                        
                        // Check 4-neighborhood
                        bool shouldDilate = false;
                        
                        // Left
                        if (x > 0 && pixels[index - 1].a >= 0.5f) shouldDilate = true;
                        // Right
                        else if (x < width - 1 && pixels[index + 1].a >= 0.5f) shouldDilate = true;
                        // Down
                        else if (y > 0 && pixels[index - width].a >= 0.5f) shouldDilate = true;
                        // Up
                        else if (y < height - 1 && pixels[index + width].a >= 0.5f) shouldDilate = true;
                        
                        if (shouldDilate) {
                            result[index] = Color.white;
                        }
                    }
                }
                
                // Update pixels for next iteration
                Array.Copy(result, pixels, pixels.Length);
            }
            
            Texture2D dilatedMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            dilatedMask.SetPixels(result);
            dilatedMask.Apply();
            
            return dilatedMask;
        }
        
        /// <summary>
        /// Applies a Gaussian blur to a mask.
        /// </summary>
        /// <param name="mask">The mask to blur</param>
        /// <param name="kernelSize">Size of the blur kernel (must be odd)</param>
        /// <param name="sigma">Standard deviation of the Gaussian function</param>
        /// <returns>Blurred mask texture</returns>
        public static Texture2D BlurMask(Texture2D mask, int kernelSize = 5, float sigma = 1.0f) {
            if (mask == null) {
                return null;
            }
            
            if (kernelSize < 3 || kernelSize % 2 == 0) {
                kernelSize = Mathf.Max(3, kernelSize + 1);
            }
            
            int width = mask.width;
            int height = mask.height;
            
            // Create Gaussian kernel
            float[] kernel = CreateGaussianKernel(kernelSize, sigma);
            
            // Apply horizontal pass
            Color[] pixelsH = new Color[width * height];
            Color[] srcPixels = mask.GetPixels();
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    float sum = 0f;
                    float weightSum = 0f;
                    
                    for (int i = 0; i < kernelSize; i++) {
                        int kx = x + i - kernelSize / 2;
                        
                        if (kx >= 0 && kx < width) {
                            int idx = y * width + kx;
                            float weight = kernel[i];
                            
                            sum += srcPixels[idx].a * weight;
                            weightSum += weight;
                        }
                    }
                    
                    float alpha = weightSum > 0 ? sum / weightSum : 0;
                    pixelsH[y * width + x] = new Color(1, 1, 1, alpha);
                }
            }
            
            // Apply vertical pass
            Color[] pixelsV = new Color[width * height];
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    float sum = 0f;
                    float weightSum = 0f;
                    
                    for (int i = 0; i < kernelSize; i++) {
                        int ky = y + i - kernelSize / 2;
                        
                        if (ky >= 0 && ky < height) {
                            int idx = ky * width + x;
                            float weight = kernel[i];
                            
                            sum += pixelsH[idx].a * weight;
                            weightSum += weight;
                        }
                    }
                    
                    float alpha = weightSum > 0 ? sum / weightSum : 0;
                    pixelsV[y * width + x] = new Color(1, 1, 1, alpha);
                }
            }
            
            Texture2D blurredMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            blurredMask.SetPixels(pixelsV);
            blurredMask.Apply();
            
            return blurredMask;
        }
        
        /// <summary>
        /// Inverts a mask (transparent becomes opaque and vice versa).
        /// </summary>
        /// <param name="mask">The mask to invert</param>
        /// <returns>Inverted mask texture</returns>
        public static Texture2D InvertMask(Texture2D mask) {
            if (mask == null) {
                return null;
            }
            
            int width = mask.width;
            int height = mask.height;
            Color[] pixels = mask.GetPixels();
            Color[] result = new Color[pixels.Length];
            
            for (int i = 0; i < pixels.Length; i++) {
                result[i] = new Color(1, 1, 1, 1f - pixels[i].a);
            }
            
            Texture2D invertedMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            invertedMask.SetPixels(result);
            invertedMask.Apply();
            
            return invertedMask;
        }
        
        /// <summary>
        /// Resizes a mask to the specified dimensions.
        /// </summary>
        /// <param name="mask">The mask to resize</param>
        /// <param name="newWidth">New width</param>
        /// <param name="newHeight">New height</param>
        /// <returns>Resized mask texture</returns>
        public static Texture2D ResizeMask(Texture2D mask, int newWidth, int newHeight) {
            if (mask == null) {
                return null;
            }
            
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
            RenderTexture prevRT = RenderTexture.active;
            
            Graphics.Blit(mask, rt);
            RenderTexture.active = rt;
            
            Texture2D resizedMask = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
            resizedMask.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            resizedMask.Apply();
            
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);
            
            return resizedMask;
        }
        
        /// <summary>
        /// Extracts a mask from the specified color channel of a texture.
        /// </summary>
        /// <param name="texture">Source texture</param>
        /// <param name="channel">Color channel to extract (0=R, 1=G, 2=B, 3=A)</param>
        /// <returns>Mask from the specified channel</returns>
        public static Texture2D ExtractChannelMask(Texture2D texture, int channel) {
            if (texture == null || channel < 0 || channel > 3) {
                return null;
            }
            
            int width = texture.width;
            int height = texture.height;
            Color[] pixels = texture.GetPixels();
            Color[] result = new Color[pixels.Length];
            
            for (int i = 0; i < pixels.Length; i++) {
                float value = 0f;
                
                switch (channel) {
                    case 0: value = pixels[i].r; break; // Red
                    case 1: value = pixels[i].g; break; // Green
                    case 2: value = pixels[i].b; break; // Blue
                    case 3: value = pixels[i].a; break; // Alpha
                }
                
                result[i] = new Color(1, 1, 1, value);
            }
            
            Texture2D channelMask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            channelMask.SetPixels(result);
            channelMask.Apply();
            
            return channelMask;
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Creates a Gaussian kernel for blurring.
        /// </summary>
        private static float[] CreateGaussianKernel(int size, float sigma) {
            float[] kernel = new float[size];
            float sum = 0f;
            int radius = size / 2;
            
            for (int i = 0; i < size; i++) {
                int x = i - radius;
                kernel[i] = Mathf.Exp(-(x * x) / (2f * sigma * sigma));
                sum += kernel[i];
            }
            
            // Normalize
            for (int i = 0; i < size; i++) {
                kernel[i] /= sum;
            }
            
            return kernel;
        }
        
        /// <summary>
        /// Determines if a point is inside a polygon.
        /// </summary>
        private static bool IsPointInPolygon(Vector2 point, Vector2[] polygon) {
            bool inside = false;
            int j = polygon.Length - 1;
            
            for (int i = 0; i < polygon.Length; i++) {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)) {
                    inside = !inside;
                }
                j = i;
            }
            
            return inside;
        }
        
        /// <summary>
        /// Calculates the distance from a point to the nearest edge of a polygon.
        /// </summary>
        private static float DistanceToPolygonEdge(Vector2 point, Vector2[] polygon) {
            float minDistance = float.MaxValue;
            int j = polygon.Length - 1;
            
            for (int i = 0; i < polygon.Length; i++) {
                Vector2 edge0 = polygon[j];
                Vector2 edge1 = polygon[i];
                
                float dist = DistanceToLineSegment(point, edge0, edge1);
                minDistance = Mathf.Min(minDistance, dist);
                
                j = i;
            }
            
            return minDistance;
        }
        
        /// <summary>
        /// Calculates the distance from a point to a line segment.
        /// </summary>
        private static float DistanceToLineSegment(Vector2 point, Vector2 a, Vector2 b) {
            Vector2 ab = b - a;
            Vector2 ap = point - a;
            
            float abLenSq = ab.sqrMagnitude;
            float dot = Vector2.Dot(ap, ab);
            float t = Mathf.Clamp01(dot / abLenSq);
            
            Vector2 closest = a + ab * t;
            return Vector2.Distance(point, closest);
        }
        
        /// <summary>
        /// Sorts contour points in clockwise or counter-clockwise order.
        /// </summary>
        private static Vector2[] SortContourPoints(Vector2[] points) {
            if (points == null || points.Length < 3) {
                return points;
            }
            
            // Find centroid
            Vector2 center = Vector2.zero;
            foreach (var point in points) {
                center += point;
            }
            center /= points.Length;
            
            // Sort points by angle around centroid
            return points.OrderBy(p => {
                return Mathf.Atan2(p.y - center.y, p.x - center.x);
            }).ToArray();
        }
        
        /// <summary>
        /// Simplifies a polyline using the Ramer-Douglas-Peucker algorithm.
        /// </summary>
        private static Vector2[] SimplifyPolyline(Vector2[] points, float tolerance) {
            if (points == null || points.Length < 3) {
                return points;
            }
            
            List<int> keepers = new List<int> { 0, points.Length - 1 };
            SimplifySection(points, 0, points.Length - 1, tolerance, keepers);
            keepers.Sort();
            
            Vector2[] simplified = new Vector2[keepers.Count];
            for (int i = 0; i < keepers.Count; i++) {
                simplified[i] = points[keepers[i]];
            }
            
            return simplified;
        }
        
        /// <summary>
        /// Recursive helper for polyline simplification.
        /// </summary>
        private static void SimplifySection(Vector2[] points, int start, int end, float tolerance, List<int> keepers) {
            if (end <= start + 1) {
                return;
            }
            
            float maxDistance = 0;
            int maxIndex = start;
            
            Vector2 startPoint = points[start];
            Vector2 endPoint = points[end];
            
            for (int i = start + 1; i < end; i++) {
                float distance = PerpendicularDistance(points[i], startPoint, endPoint);
                
                if (distance > maxDistance) {
                    maxDistance = distance;
                    maxIndex = i;
                }
            }
            
            if (maxDistance > tolerance) {
                keepers.Add(maxIndex);
                SimplifySection(points, start, maxIndex, tolerance, keepers);
                SimplifySection(points, maxIndex, end, tolerance, keepers);
            }
        }
        
        /// <summary>
        /// Calculates the perpendicular distance from a point to a line.
        /// </summary>
        private static float PerpendicularDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd) {
            float area = Mathf.Abs(0.5f * (lineStart.x * (lineEnd.y - point.y) + 
                                           lineEnd.x * (point.y - lineStart.y) + 
                                           point.x * (lineStart.y - lineEnd.y)));
            float bottom = Vector2.Distance(lineStart, lineEnd);
            
            return area / bottom * 2f;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Blend operations for combining masks.
    /// </summary>
    public enum MaskBlendOperation {
        /// <summary>Add the alpha values of both masks</summary>
        Add,
        /// <summary>Subtract the alpha value of the second mask from the first</summary>
        Subtract,
        /// <summary>Multiply the alpha values of both masks</summary>
        Multiply,
        /// <summary>Take the minimum alpha value of both masks</summary>
        Minimum,
        /// <summary>Take the maximum alpha value of both masks</summary>
        Maximum,
        /// <summary>Take the average alpha value of both masks</summary>
        Average,
        /// <summary>Take the absolute difference of alpha values</summary>
        Difference
    }
}