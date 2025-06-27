/*************************************************************************
 *  Traversify – TerrainModification.cs                                  *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 04:00:47 UTC                                     *
 *  Desc   : Defines terrain modification operations for the Traversify   *
 *           environment generation pipeline. Handles precise terrain     *
 *           feature creation, blending, and application to height maps.  *
 *           Supports both procedural and data-driven terrain features.   *
 *************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using Traversify.Core;

namespace Traversify.Terrain {
    /// <summary>
    /// Represents a terrain modification operation to be applied to a terrain's heightmap.
    /// </summary>
    [Serializable]
    public class TerrainModification {
        #region Properties
        
        /// <summary>
        /// Bounds of the modification in terrain space (0-1 range).
        /// </summary>
        public Rect bounds;
        
        /// <summary>
        /// Base height of the modification (0-1 range, relative to terrain height).
        /// </summary>
        public float baseHeight = 0.5f;
        
        /// <summary>
        /// Height variation of the modification (0-1 range).
        /// </summary>
        public float heightVariation = 0.1f;
        
        /// <summary>
        /// Slope of the modification in degrees.
        /// </summary>
        public float slope = 0f;
        
        /// <summary>
        /// Direction of the slope in degrees (0 = north).
        /// </summary>
        public float slopeDirection = 0f;
        
        /// <summary>
        /// Roughness of the terrain (0-1 range).
        /// </summary>
        public float roughness = 0.2f;
        
        /// <summary>
        /// Blend radius for smoothly transitioning to surrounding terrain (in terrain space).
        /// </summary>
        public float blendRadius = 0.05f;
        
        /// <summary>
        /// Blend mode for combining with existing terrain.
        /// </summary>
        public BlendMode blendMode = BlendMode.Max;
        
        /// <summary>
        /// Strength of the modification (0-1 range).
        /// </summary>
        public float strength = 1.0f;
        
        /// <summary>
        /// Priority of the modification (higher values take precedence in overlapping areas).
        /// </summary>
        public int priority = 0;
        
        /// <summary>
        /// Type of terrain being modified.
        /// </summary>
        public string terrainType = "default";
        
        /// <summary>
        /// Detailed description of the terrain feature.
        /// </summary>
        public string description = "";
        
        /// <summary>
        /// Optional heightmap texture for complex modifications.
        /// </summary>
        public Texture2D heightMap;
        
        /// <summary>
        /// Optional noise parameters for procedural modifications.
        /// </summary>
        public NoiseParameters noiseParams;
        
        /// <summary>
        /// Optional mask texture defining the modification's shape (white areas are modified).
        /// </summary>
        public Texture2D mask;
        
        /// <summary>
        /// Optional control points for spline-based modifications (e.g., rivers).
        /// </summary>
        public List<Vector2> controlPoints;
        
        /// <summary>
        /// Modification type, determining how it's generated and applied.
        /// </summary>
        public ModificationType modType = ModificationType.Basic;
        
        /// <summary>
        /// Specific shape for the modification.
        /// </summary>
        public ModificationShape shape = ModificationShape.Ellipse;
        
        /// <summary>
        /// Whether to invert the modification (create a depression instead of an elevation).
        /// </summary>
        public bool invert = false;
        
        /// <summary>
        /// Whether this modification represents a water feature.
        /// </summary>
        public bool isWater = false;
        
        /// <summary>
        /// Custom metadata for specialized modifications.
        /// </summary>
        public Dictionary<string, object> metadata = new Dictionary<string, object>();
        
        /// <summary>
        /// Center point of the modification in terrain space (0-1 range).
        /// </summary>
        public Vector2 Center => new Vector2(
            bounds.x + bounds.width / 2f,
            bounds.y + bounds.height / 2f
        );
        
        #endregion
        
        #region Constructors
        
        /// <summary>
        /// Default constructor.
        /// </summary>
        public TerrainModification() {
            controlPoints = new List<Vector2>();
            noiseParams = new NoiseParameters();
        }
        
        /// <summary>
        /// Creates a basic terrain modification with a specified position and size.
        /// </summary>
        public TerrainModification(Rect bounds, float baseHeight, string terrainType = "default") {
            this.bounds = bounds;
            this.baseHeight = baseHeight;
            this.terrainType = terrainType;
            controlPoints = new List<Vector2>();
            noiseParams = new NoiseParameters();
        }
        
        /// <summary>
        /// Creates a terrain modification with a heightmap.
        /// </summary>
        public TerrainModification(Rect bounds, Texture2D heightMap, float baseHeight, string terrainType = "default") {
            this.bounds = bounds;
            this.heightMap = heightMap;
            this.baseHeight = baseHeight;
            this.terrainType = terrainType;
            this.modType = ModificationType.HeightMap;
            controlPoints = new List<Vector2>();
            noiseParams = new NoiseParameters();
        }
        
        /// <summary>
        /// Creates a procedural terrain modification.
        /// </summary>
        public TerrainModification(Rect bounds, ModificationShape shape, float baseHeight, float roughness, string terrainType = "default") {
            this.bounds = bounds;
            this.shape = shape;
            this.baseHeight = baseHeight;
            this.roughness = roughness;
            this.terrainType = terrainType;
            this.modType = ModificationType.Procedural;
            controlPoints = new List<Vector2>();
            noiseParams = new NoiseParameters();
        }
        
        /// <summary>
        /// Creates a spline-based terrain modification (e.g., river, ridge).
        /// </summary>
        public TerrainModification(List<Vector2> controlPoints, float width, float baseHeight, bool invert, string terrainType = "default") {
            this.controlPoints = new List<Vector2>(controlPoints);
            this.baseHeight = baseHeight;
            this.invert = invert;
            this.terrainType = terrainType;
            this.modType = ModificationType.Spline;
            
            // Calculate bounds from control points
            if (controlPoints != null && controlPoints.Count > 0) {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                
                foreach (var point in controlPoints) {
                    minX = Mathf.Min(minX, point.x);
                    minY = Mathf.Min(minY, point.y);
                    maxX = Mathf.Max(maxX, point.x);
                    maxY = Mathf.Max(maxY, point.y);
                }
                
                this.bounds = new Rect(minX - width/2, minY - width/2, maxX - minX + width, maxY - minY + width);
            } else {
                this.bounds = new Rect(0, 0, 0, 0);
            }
            
            noiseParams = new NoiseParameters();
        }
        
        /// <summary>
        /// Deep clone of this modification.
        /// </summary>
        public TerrainModification Clone() {
            var clone = new TerrainModification {
                bounds = this.bounds,
                baseHeight = this.baseHeight,
                heightVariation = this.heightVariation,
                slope = this.slope,
                slopeDirection = this.slopeDirection,
                roughness = this.roughness,
                blendRadius = this.blendRadius,
                blendMode = this.blendMode,
                strength = this.strength,
                priority = this.priority,
                terrainType = this.terrainType,
                description = this.description,
                modType = this.modType,
                shape = this.shape,
                invert = this.invert,
                isWater = this.isWater
            };
            
            // Clone heightmap texture if exists
            if (this.heightMap != null) {
                clone.heightMap = new Texture2D(this.heightMap.width, this.heightMap.height, this.heightMap.format, false);
                clone.heightMap.SetPixels(this.heightMap.GetPixels());
                clone.heightMap.Apply();
            }
            
            // Clone mask texture if exists
            if (this.mask != null) {
                clone.mask = new Texture2D(this.mask.width, this.mask.height, this.mask.format, false);
                clone.mask.SetPixels(this.mask.GetPixels());
                clone.mask.Apply();
            }
            
            // Clone control points
            if (this.controlPoints != null) {
                clone.controlPoints = new List<Vector2>(this.controlPoints);
            }
            
            // Clone noise parameters
            if (this.noiseParams != null) {
                clone.noiseParams = this.noiseParams.Clone();
            }
            
            // Clone metadata
            if (this.metadata != null) {
                clone.metadata = new Dictionary<string, object>(this.metadata);
            }
            
            return clone;
        }
        
        #endregion
        
        #region Application Methods
        
        /// <summary>
        /// Applies this modification to a terrain heightmap.
        /// </summary>
        /// <param name="heightMap">Heightmap to modify (normalized 0-1 values)</param>
        /// <param name="terrainSize">Size of the terrain in world units</param>
        /// <param name="resolution">Resolution of the heightmap</param>
        /// <returns>Modified heightmap</returns>
        public float[,] ApplyToHeightMap(float[,] heightMap, Vector3 terrainSize, int resolution) {
            if (heightMap == null) {
                Debug.LogError("TerrainModification.ApplyToHeightMap: Height map is null");
                return null;
            }
            
            switch (modType) {
                case ModificationType.Basic:
                    return ApplyBasicModification(heightMap, terrainSize, resolution);
                
                case ModificationType.HeightMap:
                    return ApplyHeightMapModification(heightMap, terrainSize, resolution);
                
                case ModificationType.Procedural:
                    return ApplyProceduralModification(heightMap, terrainSize, resolution);
                
                case ModificationType.Spline:
                    return ApplySplineModification(heightMap, terrainSize, resolution);
                
                default:
                    return ApplyBasicModification(heightMap, terrainSize, resolution);
            }
        }
        
        /// <summary>
        /// Applies a basic modification to a heightmap (e.g., simple hill or valley).
        /// </summary>
        private float[,] ApplyBasicModification(float[,] heightMap, Vector3 terrainSize, int resolution) {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            
            // Calculate pixel coordinates from normalized bounds
            int xStart = Mathf.FloorToInt(bounds.x * width);
            int yStart = Mathf.FloorToInt(bounds.y * height);
            int xEnd = Mathf.CeilToInt((bounds.x + bounds.width) * width);
            int yEnd = Mathf.CeilToInt((bounds.y + bounds.height) * height);
            
            // Clamp to valid range
            xStart = Mathf.Clamp(xStart, 0, width - 1);
            yStart = Mathf.Clamp(yStart, 0, height - 1);
            xEnd = Mathf.Clamp(xEnd, 0, width - 1);
            yEnd = Mathf.Clamp(yEnd, 0, height - 1);
            
            // Calculate center point
            Vector2 center = new Vector2(
                bounds.x + bounds.width / 2f,
                bounds.y + bounds.height / 2f
            );
            
            // Calculate radius (half of min dimension)
            float radiusX = bounds.width / 2f;
            float radiusY = bounds.height / 2f;
            
            // Calculate blend radius in normalized space
            float blendRadiusNormalized = blendRadius;
            
            // Apply modification to affected pixels
            for (int y = yStart; y <= yEnd; y++) {
                for (int x = xStart; x <= xEnd; x++) {
                    // Convert to normalized coordinates
                    float nx = (float)x / width;
                    float ny = (float)y / height;
                    
                    // Calculate normalized distance from center based on shape
                    float distance = 0f;
                    
                    switch (shape) {
                        case ModificationShape.Ellipse:
                            distance = Vector2.Distance(
                                new Vector2(nx, ny),
                                center
                            ) / Mathf.Min(radiusX, radiusY);
                            break;
                        
                        case ModificationShape.Rectangle:
                            float dx = Mathf.Abs(nx - center.x) / radiusX;
                            float dy = Mathf.Abs(ny - center.y) / radiusY;
                            distance = Mathf.Max(dx, dy);
                            break;
                        
                        case ModificationShape.Diamond:
                            float dxDiamond = Mathf.Abs(nx - center.x) / radiusX;
                            float dyDiamond = Mathf.Abs(ny - center.y) / radiusY;
                            distance = dxDiamond + dyDiamond;
                            break;
                    }
                    
                    // Skip pixels outside the shape
                    if (distance > 1f + blendRadiusNormalized) {
                        continue;
                    }
                    
                    // Calculate modification height
                    float modHeight = CalculateHeight(nx, ny, center, distance);
                    
                    // Calculate blend factor (1 at center, 0 at edge)
                    float blendFactor = 1f;
                    
                    if (distance > 1f) {
                        // In the blend zone
                        blendFactor = 1f - (distance - 1f) / blendRadiusNormalized;
                    } else if (blendRadius > 0) {
                        // Inside shape but apply tapering if needed
                        blendFactor = 1f - distance * (1f - Mathf.Max(0f, (1f - blendRadiusNormalized)));
                    }
                    
                    // Apply strength
                    blendFactor *= strength;
                    
                    // Apply modification based on blend mode
                    float currentHeight = heightMap[y, x];
                    float newHeight = BlendHeights(currentHeight, modHeight, blendMode, blendFactor);
                    
                    // Apply inversion if needed
                    if (invert) {
                        newHeight = currentHeight - (newHeight - currentHeight);
                    }
                    
                    // Update heightmap
                    heightMap[y, x] = newHeight;
                }
            }
            
            return heightMap;
        }
        
        /// <summary>
        /// Applies a heightmap-based modification.
        /// </summary>
        private float[,] ApplyHeightMapModification(float[,] heightMap, Vector3 terrainSize, int resolution) {
            if (heightMap == null) {
                Debug.LogError("TerrainModification.ApplyHeightMapModification: Height map is null");
                return null;
            }
            
            if (this.heightMap == null) {
                Debug.LogWarning("TerrainModification.ApplyHeightMapModification: Source height map texture is null");
                return ApplyBasicModification(heightMap, terrainSize, resolution);
            }
            
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            
            // Calculate pixel coordinates from normalized bounds
            int xStart = Mathf.FloorToInt(bounds.x * width);
            int yStart = Mathf.FloorToInt(bounds.y * height);
            int xEnd = Mathf.CeilToInt((bounds.x + bounds.width) * width);
            int yEnd = Mathf.CeilToInt((bounds.y + bounds.height) * height);
            
            // Clamp to valid range
            xStart = Mathf.Clamp(xStart, 0, width - 1);
            yStart = Mathf.Clamp(yStart, 0, height - 1);
            xEnd = Mathf.Clamp(xEnd, 0, width - 1);
            yEnd = Mathf.Clamp(yEnd, 0, height - 1);
            
            // Get heightmap texture data
            Color[] sourceColors = this.heightMap.GetPixels();
            int sourceWidth = this.heightMap.width;
            int sourceHeight = this.heightMap.height;
            
            // Get mask data if available
            Color[] maskColors = null;
            if (mask != null) {
                maskColors = mask.GetPixels();
                
                // Ensure mask has the same dimensions as the heightmap
                if (mask.width != sourceWidth || mask.height != sourceHeight) {
                    Debug.LogWarning("TerrainModification.ApplyHeightMapModification: Mask dimensions don't match heightmap");
                    maskColors = null;
                }
            }
            
            // Calculate blend radius in normalized space
            float blendRadiusNormalized = blendRadius;
            
            // Apply modification to affected pixels
            for (int y = yStart; y <= yEnd; y++) {
                for (int x = xStart; x <= xEnd; x++) {
                    // Convert to normalized coordinates
                    float nx = (float)x / width;
                    float ny = (float)y / height;
                    
                    // Skip if outside bounds
                    if (nx < bounds.x || nx > bounds.x + bounds.width || 
                        ny < bounds.y || ny > bounds.y + bounds.height) {
                        continue;
                    }
                    
                    // Calculate sampling position in source heightmap
                    float u = (nx - bounds.x) / bounds.width;
                    float v = (ny - bounds.y) / bounds.height;
                    
                    // Sample source heightmap
                    int sx = Mathf.Clamp(Mathf.FloorToInt(u * sourceWidth), 0, sourceWidth - 1);
                    int sy = Mathf.Clamp(Mathf.FloorToInt(v * sourceHeight), 0, sourceHeight - 1);
                    float sourceHeight = sourceColors[sy * sourceWidth + sx].r;
                    
                    // Apply base height and variation
                    float modHeight = baseHeight + (sourceHeight - 0.5f) * heightVariation;
                    
                    // Apply mask if available
                    float maskValue = 1f;
                    if (maskColors != null) {
                        maskValue = maskColors[sy * sourceWidth + sx].r;
                    }
                    
                    // Calculate distance from edge for blending
                    float distanceFromEdge = CalculateDistanceFromEdge(u, v);
                    
                    // Calculate blend factor
                    float blendFactor = 1f;
                    if (distanceFromEdge < blendRadiusNormalized) {
                        blendFactor = distanceFromEdge / blendRadiusNormalized;
                    }
                    
                    // Apply strength and mask
                    blendFactor *= strength * maskValue;
                    
                    // Apply modification based on blend mode
                    float currentHeight = heightMap[y, x];
                    float newHeight = BlendHeights(currentHeight, modHeight, blendMode, blendFactor);
                    
                    // Apply inversion if needed
                    if (invert) {
                        newHeight = currentHeight - (newHeight - currentHeight);
                    }
                    
                    // Update heightmap
                    heightMap[y, x] = newHeight;
                }
            }
            
            return heightMap;
        }
        
        /// <summary>
        /// Applies a procedural modification (noise-based).
        /// </summary>
        private float[,] ApplyProceduralModification(float[,] heightMap, Vector3 terrainSize, int resolution) {
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            
            // Calculate pixel coordinates from normalized bounds
            int xStart = Mathf.FloorToInt(bounds.x * width);
            int yStart = Mathf.FloorToInt(bounds.y * height);
            int xEnd = Mathf.CeilToInt((bounds.x + bounds.width) * width);
            int yEnd = Mathf.CeilToInt((bounds.y + bounds.height) * height);
            
            // Clamp to valid range
            xStart = Mathf.Clamp(xStart, 0, width - 1);
            yStart = Mathf.Clamp(yStart, 0, height - 1);
            xEnd = Mathf.Clamp(xEnd, 0, width - 1);
            yEnd = Mathf.Clamp(yEnd, 0, height - 1);
            
            // Calculate center point
            Vector2 center = new Vector2(
                bounds.x + bounds.width / 2f,
                bounds.y + bounds.height / 2f
            );
            
            // Calculate radius (half of min dimension)
            float radiusX = bounds.width / 2f;
            float radiusY = bounds.height / 2f;
            
            // Calculate blend radius in normalized space
            float blendRadiusNormalized = blendRadius;
            
            // Setup noise
            float seed = noiseParams.seed;
            if (seed == 0) {
                seed = UnityEngine.Random.Range(0, 10000f);
            }
            
            // Apply modification to affected pixels
            for (int y = yStart; y <= yEnd; y++) {
                for (int x = xStart; x <= xEnd; x++) {
                    // Convert to normalized coordinates
                    float nx = (float)x / width;
                    float ny = (float)y / height;
                    
                    // Calculate normalized distance from center based on shape
                    float distance = 0f;
                    
                    switch (shape) {
                        case ModificationShape.Ellipse:
                            distance = Mathf.Sqrt(
                                Mathf.Pow((nx - center.x) / radiusX, 2) +
                                Mathf.Pow((ny - center.y) / radiusY, 2)
                            );
                            break;
                        
                        case ModificationShape.Rectangle:
                            float dx = Mathf.Abs(nx - center.x) / radiusX;
                            float dy = Mathf.Abs(ny - center.y) / radiusY;
                            distance = Mathf.Max(dx, dy);
                            break;
                        
                        case ModificationShape.Diamond:
                            float dxDiamond = Mathf.Abs(nx - center.x) / radiusX;
                            float dyDiamond = Mathf.Abs(ny - center.y) / radiusY;
                            distance = dxDiamond + dyDiamond;
                            break;
                    }
                    
                    // Skip pixels outside the shape plus blend radius
                    if (distance > 1f + blendRadiusNormalized) {
                        continue;
                    }
                    
                    // Calculate noise value
                    float noiseValue = GenerateNoise(nx, ny, seed);
                    
                    // Apply base height and noise
                    float modHeight = baseHeight + (noiseValue - 0.5f) * roughness * 2f;
                    
                    // Apply slope
                    if (slope != 0) {
                        // Calculate direction vector
                        float radians = slopeDirection * Mathf.Deg2Rad;
                        Vector2 dir = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)).normalized;
                        
                        // Calculate normalized distance in slope direction
                        float slopeDistance = Vector2.Dot(new Vector2(nx, ny) - center, dir);
                        
                        // Apply slope (convert from degrees to height change)
                        float slopeRad = slope * Mathf.Deg2Rad;
                        float maxSlopeDist = Mathf.Max(radiusX, radiusY);
                        float heightChange = Mathf.Tan(slopeRad) * (slopeDistance / maxSlopeDist);
                        
                        modHeight += heightChange;
                    }
                    
                    // Calculate blend factor (1 at center, 0 at edge)
                    float blendFactor = 1f;
                    
                    if (distance > 1f) {
                        // In the blend zone
                        blendFactor = 1f - (distance - 1f) / blendRadiusNormalized;
                    } else if (blendRadius > 0) {
                        // Inside shape but apply tapering if needed
                        blendFactor = 1f - distance * (1f - Mathf.Max(0f, (1f - blendRadiusNormalized)));
                    }
                    
                    // Apply strength
                    blendFactor *= strength;
                    
                    // Apply modification based on blend mode
                    float currentHeight = heightMap[y, x];
                    float newHeight = BlendHeights(currentHeight, modHeight, blendMode, blendFactor);
                    
                    // Apply inversion if needed
                    if (invert) {
                        newHeight = currentHeight - (newHeight - currentHeight);
                    }
                    
                    // Update heightmap
                    heightMap[y, x] = Mathf.Clamp01(newHeight);
                }
            }
            
            return heightMap;
        }
        
        /// <summary>
        /// Applies a spline-based modification (e.g., river, ridge).
        /// </summary>
        private float[,] ApplySplineModification(float[,] heightMap, Vector3 terrainSize, int resolution) {
            if (controlPoints == null || controlPoints.Count < 2) {
                Debug.LogWarning("TerrainModification.ApplySplineModification: Not enough control points");
                return heightMap;
            }
            
            int width = heightMap.GetLength(0);
            int height = heightMap.GetLength(1);
            
            // Get spline width from metadata or use a default
            float splineWidth = 0.02f; // Default width (2% of terrain)
            if (metadata.ContainsKey("splineWidth") && metadata["splineWidth"] is float width0) {
                splineWidth = width0;
            }
            
            // Calculate blend radius in normalized space
            float blendRadiusNormalized = blendRadius;
            
            // Process each point in the heightmap
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    // Convert to normalized coordinates
                    float nx = (float)x / width;
                    float ny = (float)y / height;
                    
                    // Calculate minimum distance to spline
                    float minDistance = float.MaxValue;
                    int closestSegment = -1;
                    float segmentT = 0f;
                    
                    // Find closest spline segment
                    for (int i = 0; i < controlPoints.Count - 1; i++) {
                        Vector2 p1 = controlPoints[i];
                        Vector2 p2 = controlPoints[i + 1];
                        
                        // Calculate closest point on line segment
                        Vector2 point = new Vector2(nx, ny);
                        float t = Vector2.Dot(point - p1, p2 - p1) / (p2 - p1).sqrMagnitude;
                        t = Mathf.Clamp01(t);
                        
                        Vector2 closestPoint = p1 + t * (p2 - p1);
                        float distance = Vector2.Distance(point, closestPoint);
                        
                        if (distance < minDistance) {
                            minDistance = distance;
                            closestSegment = i;
                            segmentT = t;
                        }
                    }
                    
                    // Skip if too far from spline
                    if (minDistance > splineWidth + blendRadiusNormalized) {
                        continue;
                    }
                    
                    // Calculate modification height
                    float modHeight = baseHeight;
                    
                    // Apply variation along spline if needed
                    if (heightVariation > 0 && closestSegment >= 0) {
                        // Calculate overall t-value along spline
                        float splineT = 0f;
                        float totalLength = 0f;
                        
                        // Calculate segment lengths for proper parametrization
                        float[] segmentLengths = new float[controlPoints.Count - 1];
                        for (int i = 0; i < controlPoints.Count - 1; i++) {
                            segmentLengths[i] = Vector2.Distance(controlPoints[i], controlPoints[i + 1]);
                            totalLength += segmentLengths[i];
                        }
                        
                        // Calculate splineT
                        float accumulatedLength = 0f;
                        for (int i = 0; i < closestSegment; i++) {
                            accumulatedLength += segmentLengths[i];
                        }
                        
                        splineT = (accumulatedLength + segmentLengths[closestSegment] * segmentT) / totalLength;
                        
                        // Apply variation based on splineT
                        float variation = 0f;
                        
                        // If there's a noise parameter, use it
                        if (noiseParams.enabled) {
                            variation = GenerateNoise(splineT * noiseParams.scale, 0.5f, noiseParams.seed) - 0.5f;
                        } else {
                            // Simple sine wave variation
                            variation = Mathf.Sin(splineT * 2f * Mathf.PI * 2f) * 0.5f;
                        }
                        
                        modHeight += variation * heightVariation;
                    }
                    
                    // Calculate blend factor
                    float blendFactor = 1f;
                    
                    if (minDistance > splineWidth) {
                        // In the blend zone
                        blendFactor = 1f - (minDistance - splineWidth) / blendRadiusNormalized;
                    } else {
                        // Inside shape but apply tapering if needed
                        blendFactor = 1f - (minDistance / splineWidth) * (1f - Mathf.Max(0f, (1f - blendRadiusNormalized)));
                    }
                    
                    // Apply strength
                    blendFactor *= strength;
                    
                    // Apply modification based on blend mode
                    float currentHeight = heightMap[y, x];
                    float newHeight = BlendHeights(currentHeight, modHeight, blendMode, blendFactor);
                    
                    // Apply inversion if needed (e.g., for rivers)
                    if (invert) {
                        newHeight = currentHeight - (newHeight - currentHeight);
                    }
                    
                    // Update heightmap
                    heightMap[y, x] = Mathf.Clamp01(newHeight);
                }
            }
            
            return heightMap;
        }
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Calculates height at a specific point, including falloff.
        /// </summary>
        private float CalculateHeight(float x, float y, Vector2 center, float distance) {
            // Base height + falloff
            float height = baseHeight;
            
            // Apply falloff based on distance (1 at center, 0 at edge)
            float falloff = 1f - Mathf.Clamp01(distance);
            
            // Modify falloff curve as needed (e.g., use a power curve for steeper or gentler slopes)
            falloff = Mathf.Pow(falloff, 2f);
            
            // Apply height variation with noise
            if (roughness > 0) {
                float noiseValue = GenerateNoise(x, y, 0);
                height += (noiseValue - 0.5f) * roughness * falloff;
            }
            
            // Apply slope
            if (slope != 0) {
                // Calculate direction vector
                float radians = slopeDirection * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians)).normalized;
                
                // Calculate normalized distance in slope direction
                float slopeDistance = Vector2.Dot(new Vector2(x, y) - center, dir);
                
                // Apply slope (convert from degrees to height change)
                float slopeRad = slope * Mathf.Deg2Rad;
                float maxDist = Mathf.Max(bounds.width, bounds.height) / 2f;
                float heightChange = Mathf.Tan(slopeRad) * (slopeDistance / maxDist);
                
                height += heightChange * falloff;
            }
            
            return Mathf.Clamp01(height);
        }
        
        /// <summary>
        /// Blends two height values based on the specified blend mode and factor.
        /// </summary>
        private float BlendHeights(float currentHeight, float modHeight, BlendMode mode, float factor) {
            float result = currentHeight;
            
            switch (mode) {
                case BlendMode.Add:
                    result = currentHeight + (modHeight - 0.5f) * factor;
                    break;
                
                case BlendMode.Multiply:
                    result = currentHeight * (1f + (modHeight - 0.5f) * 2f * factor);
                    break;
                
                case BlendMode.Min:
                    result = Mathf.Lerp(currentHeight, Mathf.Min(currentHeight, modHeight), factor);
                    break;
                
                case BlendMode.Max:
                    result = Mathf.Lerp(currentHeight, Mathf.Max(currentHeight, modHeight), factor);
                    break;
                
                case BlendMode.Replace:
                    result = Mathf.Lerp(currentHeight, modHeight, factor);
                    break;
                
                case BlendMode.Subtract:
                    result = currentHeight - (modHeight - 0.5f) * factor;
                    break;
                
                case BlendMode.Overlay:
                    if (currentHeight < 0.5f) {
                        result = Mathf.Lerp(currentHeight, currentHeight * modHeight * 2f, factor);
                    } else {
                        result = Mathf.Lerp(currentHeight, 1f - 2f * (1f - currentHeight) * (1f - modHeight), factor);
                    }
                    break;
            }
            
            return Mathf.Clamp01(result);
        }
        
        /// <summary>
        /// Calculates the minimum distance from a point to the edge of the modification.
        /// </summary>
        private float CalculateDistanceFromEdge(float u, float v) {
            // Distance from each edge
            float distLeft = u;
            float distRight = 1f - u;
            float distTop = v;
            float distBottom = 1f - v;
            
            // Return minimum distance
            return Mathf.Min(distLeft, distRight, distTop, distBottom);
        }
        
        /// <summary>
        /// Generates fractal noise at the specified coordinates.
        /// </summary>
        private float GenerateNoise(float x, float y, float seed) {
            // Use noise parameters if available
            if (noiseParams != null && noiseParams.enabled) {
                return GenerateFractalNoise(
                    x * noiseParams.scale,
                    y * noiseParams.scale,
                    seed + noiseParams.seed,
                    noiseParams.octaves,
                    noiseParams.persistence,
                    noiseParams.lacunarity
                );
            }
            
            // Default noise parameters
            return GenerateFractalNoise(x * 4f, y * 4f, seed, 3, 0.5f, 2f);
        }
        
        /// <summary>
        /// Generates fractal noise with specified parameters.
        /// </summary>
        private float GenerateFractalNoise(float x, float y, float seed, int octaves, float persistence, float lacunarity) {
            float total = 0f;
            float frequency = 1f;
            float amplitude = 1f;
            float maxValue = 0f;
            
            for (int i = 0; i < octaves; i++) {
                total += Mathf.PerlinNoise(x * frequency + seed, y * frequency + seed) * amplitude;
                maxValue += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            
            // Normalize to 0-1 range
            return total / maxValue;
        }
        
        #endregion
        
        /// <summary>
        /// Returns a string representation of this modification.
        /// </summary>
        public override string ToString() {
            return $"TerrainModification[{terrainType}]: {modType} at ({bounds.x:F2},{bounds.y:F2},{bounds.width:F2},{bounds.height:F2}), " +
                   $"height={baseHeight:F2}, blend={blendMode}, strength={strength:F2}";
        }
    }
    
    /// <summary>
    /// Blend modes for combining terrain modifications.
    /// </summary>
    public enum BlendMode {
        /// <summary>Add modification height to existing height</summary>
        Add,
        /// <summary>Multiply existing height by modification height</summary>
        Multiply,
        /// <summary>Take the minimum of existing and modification height</summary>
        Min,
        /// <summary>Take the maximum of existing and modification height</summary>
        Max,
        /// <summary>Replace existing height with modification height</summary>
        Replace,
        /// <summary>Subtract modification height from existing height</summary>
        Subtract,
        /// <summary>Apply photoshop-style overlay blend</summary>
        Overlay
    }
    
    /// <summary>
    /// Types of terrain modifications.
    /// </summary>
    public enum ModificationType {
        /// <summary>Basic parameterized modification</summary>
        Basic,
        /// <summary>Modification based on a heightmap texture</summary>
        HeightMap,
        /// <summary>Procedurally generated modification</summary>
        Procedural,
        /// <summary>Spline-based modification (e.g., river, ridge)</summary>
        Spline
    }
    
    /// <summary>
    /// Shapes for terrain modifications.
    /// </summary>
    public enum ModificationShape {
        /// <summary>Elliptical/circular shape</summary>
        Ellipse,
        /// <summary>Rectangular shape</summary>
        Rectangle,
        /// <summary>Diamond shape</summary>
        Diamond
    }
    
    /// <summary>
    /// Parameters for noise-based terrain generation.
    /// </summary>
    [Serializable]
    public class NoiseParameters {
        /// <summary>Whether noise is enabled</summary>
        public bool enabled = true;
        
        /// <summary>Seed for the noise generator</summary>
        public float seed = 0f;
        
        /// <summary>Scale of the noise (higher values = more detailed)</summary>
        public float scale = 4f;
        
        /// <summary>Number of noise layers to combine</summary>
        public int octaves = 4;
        
        /// <summary>How much each octave contributes to the overall shape (0-1)</summary>
        public float persistence = 0.5f;
        
        /// <summary>How much frequency increases with each octave</summary>
        public float lacunarity = 2.0f;
        
        /// <summary>Deep clone of these parameters.</summary>
        public NoiseParameters Clone() {
            return new NoiseParameters {
                enabled = this.enabled,
                seed = this.seed,
                scale = this.scale,
                octaves = this.octaves,
                persistence = this.persistence,
                lacunarity = this.lacunarity
            };
        }
    }
    
    /// <summary>
    /// Extension methods for TerrainModification operations.
    /// </summary>
    public static class TerrainModificationExtensions {
        /// <summary>
        /// Applies a collection of terrain modifications to a heightmap.
        /// </summary>
        /// <param name="modifications">List of modifications to apply</param>
        /// <param name="heightMap">Heightmap to modify</param>
        /// <param name="terrainSize">Size of terrain in world units</param>
        /// <param name="resolution">Heightmap resolution</param>
        /// <returns>Modified heightmap</returns>
        public static float[,] ApplyModifications(this List<TerrainModification> modifications, float[,] heightMap, Vector3 terrainSize, int resolution) {
            if (modifications == null || modifications.Count == 0 || heightMap == null) {
                return heightMap;
            }
            
            // Sort modifications by priority
            var sortedModifications = modifications.OrderBy(m => m.priority).ToList();
            
            // Apply each modification in order
            foreach (var modification in sortedModifications) {
                heightMap = modification.ApplyToHeightMap(heightMap, terrainSize, resolution);
            }
            
            return heightMap;
        }
    }
}