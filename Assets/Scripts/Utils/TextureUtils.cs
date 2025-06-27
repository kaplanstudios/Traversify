/*************************************************************************
 *  Traversify – TextureUtils.cs                                         *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 04:12:06 UTC                                     *
 *  Desc   : Utility methods for texture generation, manipulation,       *
 *           and analysis. Provides functionality for creating heightmaps,*
 *           segmentation masks, and performing advanced texture         *
 *           operations for the Traversify environment generation system. *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;

namespace Traversify.AI {
    /// <summary>
    /// Provides utility methods for texture generation and manipulation.
    /// </summary>
    public static class TextureUtils {
        #region Heightmap Generation
        
        /// <summary>
        /// Creates a heightmap from terrain features.
        /// </summary>
        /// <param name="width">Width of the heightmap</param>
        /// <param name="height">Height of the heightmap</param>
        /// <param name="terrainFeatures">List of terrain features</param>
        /// <param name="defaultHeight">Default height for areas with no features</param>
        /// <returns>Heightmap texture</returns>
        public static Texture2D BuildHeightMap(int width, int height, List<TerrainFeature> terrainFeatures, float defaultHeight = 0.0f) {
            Color[] pixels = new Color[width * height];
            
            // Initialize with default height
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = new Color(defaultHeight, 0, 0, 1);
            }
            
            // Skip processing if no features
            if (terrainFeatures == null || terrainFeatures.Count == 0) {
                Texture2D heightmap = new Texture2D(width, height, TextureFormat.RFloat, false);
                heightmap.SetPixels(pixels);
                heightmap.Apply();
                return heightmap;
            }
            
            // Sort features by elevation (lowest first)
            var sortedFeatures = terrainFeatures.OrderBy(f => f.elevation).ToList();
            
            // Process each feature and blend into heightmap
            foreach (var feature in sortedFeatures) {
                BlendFeatureIntoHeightmap(pixels, width, height, feature);
            }
            
            // Create and return the heightmap texture
            Texture2D finalHeightmap = new Texture2D(width, height, TextureFormat.RFloat, false);
            finalHeightmap.SetPixels(pixels);
            finalHeightmap.Apply();
            
            return finalHeightmap;
        }
        
        /// <summary>
        /// Creates a global heightmap from all terrain features.
        /// </summary>
        /// <param name="width">Width of the heightmap</param>
        /// <param name="height">Height of the heightmap</param>
        /// <param name="terrainFeatures">List of terrain features</param>
        /// <returns>Global heightmap texture</returns>
        public static Texture2D BuildGlobalHeightMap(int width, int height, List<TerrainFeature> terrainFeatures) {
            // Create a height array
            float[,] heightData = new float[width, height];
            
            // Initialize with minimum height
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    heightData[x, y] = 0f;
                }
            }
            
            // Process each feature
            if (terrainFeatures != null) {
                foreach (var feature in terrainFeatures) {
                    ApplyFeatureToHeightData(heightData, width, height, feature);
                }
            }
            
            // Apply smoothing to blend heights
            SmoothHeightData(heightData, width, height);
            
            // Convert to texture
            Texture2D heightmap = new Texture2D(width, height, TextureFormat.RFloat, false);
            Color[] pixels = new Color[width * height];
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    int index = y * width + x;
                    pixels[index] = new Color(heightData[x, y], 0, 0, 1);
                }
            }
            
            heightmap.SetPixels(pixels);
            heightmap.Apply();
            
            return heightmap;
        }
        
        /// <summary>
        /// Creates a solid color mask from a bounding box.
        /// </summary>
        /// <param name="bounds">Bounding box rectangle</param>
        /// <param name="width">Width of the texture</param>
        /// <param name="height">Height of the texture</param>
        /// <param name="color">Optional fill color</param>
        /// <returns>Mask texture</returns>
        public static Texture2D CreateSolidMask(Rect bounds, int width, int height, Color? color = null) {
            Color fillColor = color ?? Color.white;
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with transparent
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = Color.clear;
            }
            
            // Fill the bounded area
            int xMin = Mathf.Max(0, Mathf.FloorToInt(bounds.x));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(bounds.y));
            int xMax = Mathf.Min(width, Mathf.CeilToInt(bounds.x + bounds.width));
            int yMax = Mathf.Min(height, Mathf.CeilToInt(bounds.y + bounds.height));
            
            for (int y = yMin; y < yMax; y++) {
                for (int x = xMin; x < xMax; x++) {
                    int index = y * width + x;
                    pixels[index] = fillColor;
                }
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            
            return mask;
        }
        
        /// <summary>
        /// Creates a gradient mask with falloff from the center.
        /// </summary>
        /// <param name="bounds">Bounding box rectangle</param>
        /// <param name="width">Width of the texture</param>
        /// <param name="height">Height of the texture</param>
        /// <param name="falloffPower">Power factor for falloff curve (1 = linear)</param>
        /// <returns>Gradient mask texture</returns>
        public static Texture2D CreateGradientMask(Rect bounds, int width, int height, float falloffPower = 2.0f) {
            Texture2D mask = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with transparent
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = Color.clear;
            }
            
            // Calculate center and radius
            Vector2 center = new Vector2(bounds.x + bounds.width / 2f, bounds.y + bounds.height / 2f);
            float radiusX = bounds.width / 2f;
            float radiusY = bounds.height / 2f;
            
            // Fill the bounded area with gradient
            int xMin = Mathf.Max(0, Mathf.FloorToInt(bounds.x));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(bounds.y));
            int xMax = Mathf.Min(width, Mathf.CeilToInt(bounds.x + bounds.width));
            int yMax = Mathf.Min(height, Mathf.CeilToInt(bounds.y + bounds.height));
            
            for (int y = yMin; y < yMax; y++) {
                for (int x = xMin; x < xMax; x++) {
                    // Calculate normalized distance from center
                    float nx = (x - center.x) / radiusX;
                    float ny = (y - center.y) / radiusY;
                    float distance = Mathf.Sqrt(nx * nx + ny * ny);
                    
                    // Apply falloff
                    float alpha = 1f - Mathf.Clamp01(distance);
                    alpha = Mathf.Pow(alpha, falloffPower);
                    
                    int index = y * width + x;
                    pixels[index] = new Color(1, 1, 1, alpha);
                }
            }
            
            mask.SetPixels(pixels);
            mask.Apply();
            
            return mask;
        }
        
        #endregion
        
        #region Segmentation Map Generation
        
        /// <summary>
        /// Builds a segmentation map from image segments.
        /// </summary>
        /// <param name="width">Width of the segmentation map</param>
        /// <param name="height">Height of the segmentation map</param>
        /// <param name="segments">List of image segments</param>
        /// <returns>Segmentation map texture</returns>
        public static Texture2D BuildSegmentationMap(int width, int height, List<ImageSegment> segments) {
            Texture2D segmentationMap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with transparent
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = Color.clear;
            }
            
            // Skip processing if no segments
            if (segments == null || segments.Count == 0) {
                segmentationMap.SetPixels(pixels);
                segmentationMap.Apply();
                return segmentationMap;
            }
            
            // Sort segments by area (smallest first, so larger segments go on top)
            var sortedSegments = segments.OrderBy(s => s.area).ToList();
            
            // Process each segment and blend into segmentation map
            foreach (var segment in sortedSegments) {
                BlendSegmentIntoMap(pixels, width, height, segment);
            }
            
            segmentationMap.SetPixels(pixels);
            segmentationMap.Apply();
            
            return segmentationMap;
        }
        
        /// <summary>
        /// Builds a global segmentation map from analyzed segments.
        /// </summary>
        /// <param name="width">Width of the segmentation map</param>
        /// <param name="height">Height of the segmentation map</param>
        /// <param name="segments">List of analyzed segments</param>
        /// <returns>Global segmentation map texture</returns>
        public static Texture2D BuildGlobalSegmentationMap(int width, int height, List<AnalyzedSegment> segments) {
            Texture2D segmentationMap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with transparent
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = Color.clear;
            }
            
            // Skip processing if no segments
            if (segments == null || segments.Count == 0) {
                segmentationMap.SetPixels(pixels);
                segmentationMap.Apply();
                return segmentationMap;
            }
            
            // Sort segments by terrain type (terrain first, then by area)
            var sortedSegments = segments
                .OrderBy(s => !s.isTerrain)
                .ThenBy(s => s.originalSegment?.area ?? 0)
                .ToList();
            
            // Process each segment and blend into segmentation map
            foreach (var segment in sortedSegments) {
                if (segment.originalSegment == null || segment.originalSegment.mask == null) {
                    continue;
                }
                
                // Get a color based on segment type
                Color segmentColor = segment.originalSegment.color;
                if (segmentColor.a < 0.1f) {
                    // Generate a new color if none is set
                    segmentColor = segment.isTerrain 
                        ? Color.HSVToRGB(UnityEngine.Random.value * 0.3f + 0.2f, 0.7f, 0.8f) // Earth tones for terrain
                        : Color.HSVToRGB(UnityEngine.Random.value * 0.6f + 0.4f, 0.8f, 0.9f); // Other colors for objects
                    segmentColor.a = 0.7f;
                }
                
                BlendSegmentIntoMap(pixels, width, height, segment.originalSegment, segmentColor);
            }
            
            segmentationMap.SetPixels(pixels);
            segmentationMap.Apply();
            
            return segmentationMap;
        }
        
        /// <summary>
        /// Creates a labeled segmentation map with different colors per class.
        /// </summary>
        /// <param name="width">Width of the segmentation map</param>
        /// <param name="height">Height of the segmentation map</param>
        /// <param name="detectedObjects">List of detected objects</param>
        /// <param name="classColors">Dictionary mapping class names to colors</param>
        /// <returns>Labeled segmentation map texture</returns>
        public static Texture2D CreateLabeledSegmentationMap(int width, int height, 
                                                          List<DetectedObject> detectedObjects,
                                                          Dictionary<string, Color> classColors = null) {
            Texture2D segmentationMap = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[width * height];
            
            // Initialize with transparent
            for (int i = 0; i < pixels.Length; i++) {
                pixels[i] = Color.clear;
            }
            
            // Generate colors for classes if not provided
            if (classColors == null) {
                classColors = new Dictionary<string, Color>();
            }
            
            // Add each object to the segmentation map
            if (detectedObjects != null) {
                foreach (var obj in detectedObjects) {
                    // Get or create color for this class
                    if (!classColors.TryGetValue(obj.className, out Color color)) {
                        color = Color.HSVToRGB(UnityEngine.Random.value, 0.8f, 0.8f);
                        classColors[obj.className] = color;
                    }
                    
                    // Fill the bounding box
                    int xMin = Mathf.Max(0, Mathf.FloorToInt(obj.boundingBox.x));
                    int yMin = Mathf.Max(0, Mathf.FloorToInt(obj.boundingBox.y));
                    int xMax = Mathf.Min(width, Mathf.CeilToInt(obj.boundingBox.xMax));
                    int yMax = Mathf.Min(height, Mathf.CeilToInt(obj.boundingBox.yMax));
                    
                    for (int y = yMin; y < yMax; y++) {
                        for (int x = xMin; x < xMax; x++) {
                            // If mask is available, use it
                            if (obj.mask != null) {
                                // Sample mask
                                float u = (x - obj.boundingBox.x) / obj.boundingBox.width;
                                float v = (y - obj.boundingBox.y) / obj.boundingBox.height;
                                Color maskColor = obj.mask.GetPixelBilinear(u, v);
                                
                                // Apply mask alpha
                                if (maskColor.a > 0.5f) {
                                    int index = y * width + x;
                                    pixels[index] = color;
                                }
                            } else {
                                // No mask, fill the entire box
                                int index = y * width + x;
                                pixels[index] = color;
                            }
                        }
                    }
                }
            }
            
            segmentationMap.SetPixels(pixels);
            segmentationMap.Apply();
            
            return segmentationMap;
        }
        
        #endregion
        
        #region Texture Analysis
        
        /// <summary>
        /// Calculates the average color of a texture.
        /// </summary>
        /// <param name="texture">Source texture</param>
        /// <param name="samplingRate">Fraction of pixels to sample (0-1)</param>
        /// <returns>Average color</returns>
        public static Color CalculateAverageColor(Texture2D texture, float samplingRate = 1.0f) {
            if (texture == null) {
                return Color.black;
            }
            
            Color[] pixels = texture.GetPixels();
            
            if (pixels.Length == 0) {
                return Color.black;
            }
            
            // If sampling rate is less than 1, sample a subset of pixels
            int sampleCount;
            if (samplingRate < 1.0f) {
                sampleCount = Mathf.Max(1, Mathf.FloorToInt(pixels.Length * samplingRate));
                int step = pixels.Length / sampleCount;
                
                float r = 0, g = 0, b = 0, a = 0;
                
                for (int i = 0; i < sampleCount; i++) {
                    int idx = i * step;
                    if (idx < pixels.Length) {
                        r += pixels[idx].r;
                        g += pixels[idx].g;
                        b += pixels[idx].b;
                        a += pixels[idx].a;
                    }
                }
                
                return new Color(r / sampleCount, g / sampleCount, b / sampleCount, a / sampleCount);
            } else {
                // Process all pixels
                float r = 0, g = 0, b = 0, a = 0;
                
                foreach (Color pixel in pixels) {
                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    a += pixel.a;
                }
                
                return new Color(r / pixels.Length, g / pixels.Length, b / pixels.Length, a / pixels.Length);
            }
        }
        
        /// <summary>
        /// Analyzes a texture to estimate dominant colors.
        /// </summary>
        /// <param name="texture">Source texture</param>
        /// <param name="maxColors">Maximum number of colors to return</param>
        /// <param name="threshold">Minimum color occurrence threshold (0-1)</param>
        /// <returns>Dictionary mapping colors to their occurrence frequency</returns>
        public static Dictionary<Color, float> AnalyzeDominantColors(Texture2D texture, int maxColors = 5, float threshold = 0.01f) {
            if (texture == null) {
                return new Dictionary<Color, float>();
            }
            
            // Resize for faster processing if texture is large
            Texture2D processTexture = texture;
            bool needsCleanup = false;
            
            if (texture.width > 256 || texture.height > 256) {
                processTexture = ResizeTexture(texture, 256, 256);
                needsCleanup = true;
            }
            
            Color[] pixels = processTexture.GetPixels();
            
            // Group similar colors and count occurrences
            Dictionary<Vector3, int> colorCounts = new Dictionary<Vector3, int>();
            
            foreach (Color pixel in pixels) {
                // Skip transparent pixels
                if (pixel.a < 0.5f) {
                    continue;
                }
                
                // Quantize to reduce color space
                Vector3 quantized = new Vector3(
                    Mathf.Round(pixel.r * 10) / 10f,
                    Mathf.Round(pixel.g * 10) / 10f,
                    Mathf.Round(pixel.b * 10) / 10f
                );
                
                if (colorCounts.TryGetValue(quantized, out int count)) {
                    colorCounts[quantized] = count + 1;
                } else {
                    colorCounts[quantized] = 1;
                }
            }
            
            // Convert to frequencies
            Dictionary<Color, float> colorFrequencies = new Dictionary<Color, float>();
            
            foreach (var kvp in colorCounts) {
                float frequency = (float)kvp.Value / pixels.Length;
                
                if (frequency >= threshold) {
                    Color color = new Color(kvp.Key.x, kvp.Key.y, kvp.Key.z);
                    colorFrequencies[color] = frequency;
                }
            }
            
            // Get top colors
            var dominantColors = colorFrequencies
                .OrderByDescending(kvp => kvp.Value)
                .Take(maxColors)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            // Cleanup temporary texture
            if (needsCleanup) {
                UnityEngine.Object.Destroy(processTexture);
            }
            
            return dominantColors;
        }
        
        /// <summary>
        /// Calculates the brightness histogram of a texture.
        /// </summary>
        /// <param name="texture">Source texture</param>
        /// <param name="bins">Number of histogram bins</param>
        /// <returns>Array of bin values</returns>
        public static float[] CalculateBrightnessHistogram(Texture2D texture, int bins = 16) {
            if (texture == null) {
                return new float[bins];
            }
            
            Color[] pixels = texture.GetPixels();
            float[] histogram = new float[bins];
            
            foreach (Color pixel in pixels) {
                // Calculate perceived brightness
                float brightness = 0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b;
                
                // Determine bin
                int bin = Mathf.Clamp(Mathf.FloorToInt(brightness * bins), 0, bins - 1);
                
                // Increment bin count
                histogram[bin]++;
            }
            
            // Normalize histogram
            for (int i = 0; i < bins; i++) {
                histogram[i] /= pixels.Length;
            }
            
            return histogram;
        }
        
        /// <summary>
        /// Estimates weather conditions from a texture.
        /// </summary>
        /// <param name="texture">Source texture</param>
        /// <returns>Dictionary mapping weather condition to probability</returns>
        public static Dictionary<string, float> EstimateWeatherConditions(Texture2D texture) {
            if (texture == null) {
                return new Dictionary<string, float>();
            }
            
            Dictionary<string, float> result = new Dictionary<string, float> {
                { "clear", 0.0f },
                { "cloudy", 0.0f },
                { "rain", 0.0f },
                { "snow", 0.0f },
                { "fog", 0.0f }
            };
            
            // Analyze color and brightness characteristics
            Color avgColor = CalculateAverageColor(texture);
            float[] histogram = CalculateBrightnessHistogram(texture, 16);
            
            // Brightness factors
            float brightness = 0.299f * avgColor.r + 0.587f * avgColor.g + 0.114f * avgColor.b;
            float brightnessDarkRegion = histogram[0] + histogram[1] + histogram[2] + histogram[3];
            float brightnessMidRegion = histogram[4] + histogram[5] + histogram[6] + histogram[7] + 
                                       histogram[8] + histogram[9] + histogram[10] + histogram[11];
            float brightnessBrightRegion = histogram[12] + histogram[13] + histogram[14] + histogram[15];
            
            // Hue and saturation
            Color.RGBToHSV(avgColor, out float hue, out float saturation, out float value);
            
            // Calculate blue sky probability
            float blueSkyFactor = avgColor.b - (avgColor.r + avgColor.g) / 2f;
            
            // Estimate probabilities
            
            // Clear: bright, blue sky, high contrast
            result["clear"] = Mathf.Clamp01(
                brightness * 0.5f + 
                blueSkyFactor + 
                brightnessBrightRegion * 0.5f
            );
            
            // Cloudy: medium brightness, low saturation, white/gray dominance
            result["cloudy"] = Mathf.Clamp01(
                brightnessMidRegion * 0.7f + 
                (1f - saturation) * 0.5f
            );
            
            // Rain: dark, low saturation, blue/gray tint
            result["rain"] = Mathf.Clamp01(
                brightnessDarkRegion * 0.6f + 
                (1f - saturation) * 0.3f + 
                (avgColor.b > avgColor.r ? 0.2f : 0f)
            );
            
            // Snow: very bright, low saturation, white dominance
            result["snow"] = Mathf.Clamp01(
                brightnessBrightRegion * 0.8f + 
                (1f - saturation) * 0.4f + 
                (avgColor.r > 0.8f && avgColor.g > 0.8f && avgColor.b > 0.8f ? 0.3f : 0f)
            );
            
            // Fog: medium brightness, very low saturation, low contrast
            result["fog"] = Mathf.Clamp01(
                brightnessMidRegion * 0.5f + 
                (1f - saturation) * 0.7f + 
                (1f - Mathf.Abs(brightnessBrightRegion - brightnessDarkRegion)) * 0.3f
            );
            
            // Normalize probabilities
            float sum = result.Values.Sum();
            if (sum > 0) {
                foreach (string key in result.Keys.ToList()) {
                    result[key] /= sum;
                }
            }
            
            return result;
        }
        
        #endregion
        
        #region Texture Manipulation
        
        /// <summary>
        /// Resizes a texture to the specified dimensions.
        /// </summary>
        /// <param name="source">Source texture</param>
        /// <param name="width">Target width</param>
        /// <param name="height">Target height</param>
        /// <returns>Resized texture</returns>
        public static Texture2D ResizeTexture(Texture2D source, int width, int height) {
            if (source == null) {
                return null;
            }
            
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            
            RenderTexture.active = rt;
            Graphics.Blit(source, rt);
            
            Texture2D result = new Texture2D(width, height, source.format, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }
        
        /// <summary>
        /// Crops a texture to the specified rectangle.
        /// </summary>
        /// <param name="source">Source texture</param>
        /// <param name="rect">Crop rectangle</param>
        /// <returns>Cropped texture</returns>
        public static Texture2D CropTexture(Texture2D source, Rect rect) {
            if (source == null) {
                return null;
            }
            
            int x = Mathf.FloorToInt(rect.x);
            int y = Mathf.FloorToInt(rect.y);
            int width = Mathf.FloorToInt(rect.width);
            int height = Mathf.FloorToInt(rect.height);
            
            // Clamp to source dimensions
            x = Mathf.Clamp(x, 0, source.width - 1);
            y = Mathf.Clamp(y, 0, source.height - 1);
            width = Mathf.Clamp(width, 1, source.width - x);
            height = Mathf.Clamp(height, 1, source.height - y);
            
            // Get pixels from the source texture
            Color[] pixels = source.GetPixels(x, y, width, height);
            
            // Create a new texture and set its pixels
            Texture2D result = new Texture2D(width, height, source.format, false);
            result.SetPixels(pixels);
            result.Apply();
            
            return result;
        }
        
        /// <summary>
        /// Combines two textures by applying a mask.
        /// </summary>
        /// <param name="background">Background texture</param>
        /// <param name="foreground">Foreground texture</param>
        /// <param name="mask">Mask texture (alpha channel determines blend factor)</param>
        /// <returns>Combined texture</returns>
        public static Texture2D CombineWithMask(Texture2D background, Texture2D foreground, Texture2D mask) {
            if (background == null) {
                return foreground;
            }
            
            if (foreground == null) {
                return background;
            }
            
            if (mask == null) {
                return foreground;
            }
            
            // Ensure all textures have the same dimensions
            int width = background.width;
            int height = background.height;
            
            if (foreground.width != width || foreground.height != height ||
                mask.width != width || mask.height != height) {
                
                // Resize textures to match background
                if (foreground.width != width || foreground.height != height) {
                    Texture2D resizedForeground = ResizeTexture(foreground, width, height);
                    foreground = resizedForeground;
                }
                
                if (mask.width != width || mask.height != height) {
                    Texture2D resizedMask = ResizeTexture(mask, width, height);
                    mask = resizedMask;
                }
            }
            
            // Get pixel data
            Color[] bgPixels = background.GetPixels();
            Color[] fgPixels = foreground.GetPixels();
            Color[] maskPixels = mask.GetPixels();
            Color[] resultPixels = new Color[width * height];
            
            // Combine pixels
            for (int i = 0; i < resultPixels.Length; i++) {
                float blend = maskPixels[i].a;
                resultPixels[i] = Color.Lerp(bgPixels[i], fgPixels[i], blend);
            }
            
            // Create result texture
            Texture2D result = new Texture2D(width, height, background.format, false);
            result.SetPixels(resultPixels);
            result.Apply();
            
            return result;
        }
        
        /// <summary>
        /// Extracts a specific color channel from a texture.
        /// </summary>
        /// <param name="source">Source texture</param>
        /// <param name="channel">Channel to extract (0=R, 1=G, 2=B, 3=A)</param>
        /// <returns>Single-channel texture</returns>
        public static Texture2D ExtractChannel(Texture2D source, int channel) {
            if (source == null) {
                return null;
            }
            
            if (channel < 0 || channel > 3) {
                throw new ArgumentOutOfRangeException(nameof(channel), "Channel must be between 0 and 3");
            }
            
            Color[] pixels = source.GetPixels();
            Color[] result = new Color[pixels.Length];
            
            for (int i = 0; i < pixels.Length; i++) {
                float value = 0;
                
                switch (channel) {
                    case 0: value = pixels[i].r; break;
                    case 1: value = pixels[i].g; break;
                    case 2: value = pixels[i].b; break;
                    case 3: value = pixels[i].a; break;
                }
                
                result[i] = new Color(value, value, value, 1);
            }
            
            Texture2D channelTex = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
            channelTex.SetPixels(result);
            channelTex.Apply();
            
            return channelTex;
        }
        
        /// <summary>
        /// Inverts a texture's colors.
        /// </summary>
        /// <param name="source">Source texture</param>
        /// <param name="invertAlpha">Whether to also invert the alpha channel</param>
        /// <returns>Inverted texture</returns>
        public static Texture2D InvertTexture(Texture2D source, bool invertAlpha = false) {
            if (source == null) {
                return null;
            }
            
            Color[] pixels = source.GetPixels();
            Color[] inverted = new Color[pixels.Length];
            
            for (int i = 0; i < pixels.Length; i++) {
                inverted[i] = new Color(
                    1f - pixels[i].r,
                    1f - pixels[i].g,
                    1f - pixels[i].b,
                    invertAlpha ? 1f - pixels[i].a : pixels[i].a
                );
            }
            
            Texture2D result = new Texture2D(source.width, source.height, source.format, false);
            result.SetPixels(inverted);
            result.Apply();
            
            return result;
        }
        
        /// <summary>
        /// Adjusts brightness and contrast of a texture.
        /// </summary>
        /// <param name="source">Source texture</param>
        /// <param name="brightness">Brightness adjustment (-1 to 1)</param>
        /// <param name="contrast">Contrast adjustment (-1 to 1)</param>
        /// <returns>Adjusted texture</returns>
        public static Texture2D AdjustBrightnessContrast(Texture2D source, float brightness, float contrast) {
            if (source == null) {
                return null;
            }
            
            Color[] pixels = source.GetPixels();
            Color[] adjusted = new Color[pixels.Length];
            
            // Convert contrast adjustment to factor
            float contrastFactor = 1f + contrast;
            
            for (int i = 0; i < pixels.Length; i++) {
                // Apply brightness
                float r = pixels[i].r + brightness;
                float g = pixels[i].g + brightness;
                float b = pixels[i].b + brightness;
                
                // Apply contrast
                r = (r - 0.5f) * contrastFactor + 0.5f;
                g = (g - 0.5f) * contrastFactor + 0.5f;
                b = (b - 0.5f) * contrastFactor + 0.5f;
                
                // Clamp values
                adjusted[i] = new Color(
                    Mathf.Clamp01(r),
                    Mathf.Clamp01(g),
                    Mathf.Clamp01(b),
                    pixels[i].a
                );
            }
            
            Texture2D result = new Texture2D(source.width, source.height, source.format, false);
            result.SetPixels(adjusted);
            result.Apply();
            
            return result;
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Blends a terrain feature into a heightmap.
        /// </summary>
        private static void BlendFeatureIntoHeightmap(Color[] pixels, int width, int height, TerrainFeature feature) {
            if (feature == null || feature.segmentMask == null) {
                return;
            }
            
            // Calculate pixel bounds
            int xMin = Mathf.Max(0, Mathf.FloorToInt(feature.boundingBox.x));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(feature.boundingBox.y));
            int xMax = Mathf.Min(width, Mathf.CeilToInt(feature.boundingBox.x + feature.boundingBox.width));
            int yMax = Mathf.Min(height, Mathf.CeilToInt(feature.boundingBox.y + feature.boundingBox.height));
            
            // Get feature height
            float featureHeight = Mathf.Clamp01(feature.elevation);
            
            // Blend feature into heightmap
            for (int y = yMin; y < yMax; y++) {
                for (int x = xMin; x < xMax; x++) {
                    // Calculate texture coordinates within the feature
                    float u = (x - feature.boundingBox.x) / feature.boundingBox.width;
                    float v = (y - feature.boundingBox.y) / feature.boundingBox.height;
                    
                    // Sample mask alpha
                    Color maskColor = feature.segmentMask.GetPixelBilinear(u, v);
                    float maskAlpha = maskColor.a;
                    
                    // Skip pixels outside the mask
                    if (maskAlpha < 0.01f) {
                        continue;
                    }
                    
                    // Blend feature height with existing height
                    int index = y * width + x;
                    float currentHeight = pixels[index].r;
                    float blendedHeight = Mathf.Lerp(currentHeight, featureHeight, maskAlpha);
                    
                    // Update pixel
                    pixels[index] = new Color(blendedHeight, 0, 0, 1);
                }
            }
        }
        
        /// <summary>
        /// Applies a terrain feature to a height data array.
        /// </summary>
        private static void ApplyFeatureToHeightData(float[,] heightData, int width, int height, TerrainFeature feature) {
            if (feature == null || feature.segmentMask == null) {
                return;
            }
            
            // Calculate pixel bounds
            int xMin = Mathf.Max(0, Mathf.FloorToInt(feature.boundingBox.x));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(feature.boundingBox.y));
            int xMax = Mathf.Min(width, Mathf.CeilToInt(feature.boundingBox.x + feature.boundingBox.width));
            int yMax = Mathf.Min(height, Mathf.CeilToInt(feature.boundingBox.y + feature.boundingBox.height));
            
            // Get feature height
            float featureHeight = Mathf.Clamp01(feature.elevation);
            
            // Apply feature to height data
            for (int y = yMin; y < yMax; y++) {
                for (int x = xMin; x < xMax; x++) {
                    // Calculate texture coordinates within the feature
                    float u = (x - feature.boundingBox.x) / feature.boundingBox.width;
                    float v = (y - feature.boundingBox.y) / feature.boundingBox.height;
                    
                    // Sample mask alpha
                    Color maskColor = feature.segmentMask.GetPixelBilinear(u, v);
                    float maskAlpha = maskColor.a;
                    
                    // Skip pixels outside the mask
                    if (maskAlpha < 0.01f) {
                        continue;
                    }
                    
                    // Apply feature height based on the feature type
                    float currentHeight = heightData[x, y];
                    float blendedHeight;
                    
                    // Check if this is a water feature
                    bool isWater = feature.label.ToLowerInvariant().Contains("water") ||
                                  feature.label.ToLowerInvariant().Contains("river") ||
                                  feature.label.ToLowerInvariant().Contains("lake") ||
                                  feature.label.ToLowerInvariant().Contains("ocean");
                    
                    if (isWater) {
                        // Water features should be depressions
                        blendedHeight = Mathf.Min(currentHeight, featureHeight) * maskAlpha + 
                                      currentHeight * (1f - maskAlpha);
                    } else if (feature.label.ToLowerInvariant().Contains("mountain") || 
                             feature.label.ToLowerInvariant().Contains("hill")) {
                        // Elevated features should be peaks
                        blendedHeight = Mathf.Max(currentHeight, featureHeight) * maskAlpha + 
                                      currentHeight * (1f - maskAlpha);
                    } else {
                        // Other features blend normally
                        blendedHeight = featureHeight * maskAlpha + currentHeight * (1f - maskAlpha);
                    }
                    
                    // Update height data
                    heightData[x, y] = blendedHeight;
                }
            }
        }
        
        /// <summary>
        /// Blends a segment into a segmentation map.
        /// </summary>
        private static void BlendSegmentIntoMap(Color[] pixels, int width, int height, ImageSegment segment, Color? overrideColor = null) {
            if (segment == null || segment.mask == null) {
                return;
            }
            
            // Calculate pixel bounds
            int xMin = Mathf.Max(0, Mathf.FloorToInt(segment.boundingBox.x));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(segment.boundingBox.y));
            int xMax = Mathf.Min(width, Mathf.CeilToInt(segment.boundingBox.x + segment.boundingBox.width));
            int yMax = Mathf.Min(height, Mathf.CeilToInt(segment.boundingBox.y + segment.boundingBox.height));
            
            // Get segment color
            Color segmentColor = overrideColor ?? segment.color;
            
            // Make sure alpha is reasonable
            if (segmentColor.a < 0.1f) {
                segmentColor.a = 0.7f;
            }
            
            // Blend segment into map
            for (int y = yMin; y < yMax; y++) {
                for (int x = xMin; x < xMax; x++) {
                    // Calculate texture coordinates within the segment
                    float u = (x - segment.boundingBox.x) / segment.boundingBox.width;
                    float v = (y - segment.boundingBox.y) / segment.boundingBox.height;
                    
                    // Sample mask alpha
                    Color maskColor = segment.mask.GetPixelBilinear(u, v);
                    float maskAlpha = maskColor.a;
                    
                    // Skip pixels outside the mask
                    if (maskAlpha < 0.01f) {
                        continue;
                    }
                    
                    // Apply segment color
                    int index = y * width + x;
                    Color currentColor = pixels[index];
                    
                    // Alpha blending
                    float resultAlpha = segmentColor.a * maskAlpha + currentColor.a * (1f - segmentColor.a * maskAlpha);
                    
                    if (resultAlpha > 0.01f) {
                        Color resultColor = new Color(
                            (segmentColor.r * segmentColor.a * maskAlpha + currentColor.r * currentColor.a * (1f - segmentColor.a * maskAlpha)) / resultAlpha,
                            (segmentColor.g * segmentColor.a * maskAlpha + currentColor.g * currentColor.a * (1f - segmentColor.a * maskAlpha)) / resultAlpha,
                            (segmentColor.b * segmentColor.a * maskAlpha + currentColor.b * currentColor.a * (1f - segmentColor.a * maskAlpha)) / resultAlpha,
                            resultAlpha
                        );
                        
                        pixels[index] = resultColor;
                    }
                }
            }
        }
        
        /// <summary>
        /// Applies smoothing to height data.
        /// </summary>
        private static void SmoothHeightData(float[,] heightData, int width, int height) {
            // Create a copy of the original data
            float[,] original = new float[width, height];
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    original[x, y] = heightData[x, y];
                }
            }
            
            // Apply simple box blur
            int kernelSize = 3;
            int halfKernel = kernelSize / 2;
            
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    float sum = 0;
                    int count = 0;
                    
                    for (int ky = -halfKernel; ky <= halfKernel; ky++) {
                        for (int kx = -halfKernel; kx <= halfKernel; kx++) {
                            int sampleX = x + kx;
                            int sampleY = y + ky;
                            
                            if (sampleX >= 0 && sampleX < width && sampleY >= 0 && sampleY < height) {
                                sum += original[sampleX, sampleY];
                                count++;
                            }
                        }
                    }
                    
                    if (count > 0) {
                        heightData[x, y] = sum / count;
                    }
                }
            }
        }
        
        /// <summary>
        /// Converts an array of pixel colors to a different size.
        /// </summary>
        private static Color[] ResizeTextureColors(Texture2D source, int targetWidth, int targetHeight) {
            // Create a resized texture
            Texture2D resized = ResizeTexture(source, targetWidth, targetHeight);
            
            // Get the colors
            Color[] colors = resized.GetPixels();
            
            // Destroy the temporary texture
            UnityEngine.Object.Destroy(resized);
            
            return colors;
        }
        
        #endregion
    }
}