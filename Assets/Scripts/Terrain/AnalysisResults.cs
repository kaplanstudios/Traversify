/*************************************************************************
 *  Traversify – AnalysisResults.cs                                      *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 02:52:59 UTC                                     *
 *  Desc   : Advanced data structures for storing map analysis results,  *
 *           including terrain features, object detection, segmentation, *
 *           and associated metadata. Provides comprehensive information *
 *           for terrain generation and object placement.                *
 *************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;

namespace Traversify.AI {
    /// <summary>
    /// Comprehensive results from map analysis, including terrain features, 
    /// object detection, segmentation data, and associated metadata.
    /// </summary>
    [Serializable]
    public class AnalysisResults {
        #region Properties
        
        /// <summary>
        /// Detected terrain features (mountains, water, plains, etc.).
        /// </summary>
        public List<TerrainFeature> terrainFeatures = new List<TerrainFeature>();
        
        /// <summary>
        /// Detected objects on the map (buildings, trees, etc.).
        /// </summary>
        public List<MapObject> mapObjects = new List<MapObject>();
        
        /// <summary>
        /// Groups of similar objects, for instancing and optimization.
        /// </summary>
        public List<ObjectGroup> objectGroups = new List<ObjectGroup>();
        
        /// <summary>
        /// Combined heightmap texture (normalized 0-1 values).
        /// </summary>
        public Texture2D heightMap;
        
        /// <summary>
        /// Segmentation mask texture showing region classifications.
        /// </summary>
        public Texture2D segmentationMap;
        
        /// <summary>
        /// Classified regions by terrain type.
        /// </summary>
        public Dictionary<string, List<TerrainRegion>> classifiedRegions = new Dictionary<string, List<TerrainRegion>>();
        
        /// <summary>
        /// Path networks detected in the map.
        /// </summary>
        public List<PathNetwork> pathNetworks = new List<PathNetwork>();
        
        /// <summary>
        /// Water regions detected in the map.
        /// </summary>
        public List<WaterBody> waterBodies = new List<WaterBody>();
        
        /// <summary>
        /// Vegetation distribution data.
        /// </summary>
        public VegetationData vegetationData = new VegetationData();
        
        /// <summary>
        /// General metadata about the analysis process.
        /// </summary>
        public AnalysisMetadata metadata = new AnalysisMetadata();
        
        /// <summary>
        /// Analysis timing information.
        /// </summary>
        public AnalysisTimings timings = new AnalysisTimings();
        
        /// <summary>
        /// Detected elevation range in the map.
        /// </summary>
        public ElevationRange elevationRange = new ElevationRange();
        
        /// <summary>
        /// Statistical information about the analysis.
        /// </summary>
        public AnalysisStatistics statistics = new AnalysisStatistics();
        
        /// <summary>
        /// Time taken for the analysis in seconds.
        /// </summary>
        public float analysisTime;
        
        /// <summary>
        /// Raw detection results from different AI models.
        /// </summary>
        public Dictionary<string, object> rawDetections = new Dictionary<string, object>();
        
        #endregion
        
        #region Methods
        
        /// <summary>
        /// Gets all objects of a specific type.
        /// </summary>
        /// <param name="type">Object type to filter by</param>
        /// <returns>List of matching objects</returns>
        public List<MapObject> GetObjectsByType(string type) {
            if (string.IsNullOrEmpty(type)) return new List<MapObject>();
            return mapObjects.Where(o => o.type == type).ToList();
        }
        
        /// <summary>
        /// Gets terrain features of a specific type.
        /// </summary>
        /// <param name="type">Terrain type to filter by</param>
        /// <returns>List of matching terrain features</returns>
        public List<TerrainFeature> GetTerrainFeaturesByType(string type) {
            if (string.IsNullOrEmpty(type)) return new List<TerrainFeature>();
            return terrainFeatures.Where(t => t.type == type).ToList();
        }
        
        /// <summary>
        /// Gets an object group by its ID.
        /// </summary>
        /// <param name="groupId">Group ID to search for</param>
        /// <returns>Matching object group or null if not found</returns>
        public ObjectGroup GetObjectGroupById(string groupId) {
            if (string.IsNullOrEmpty(groupId)) return null;
            return objectGroups.FirstOrDefault(g => g.groupId == groupId);
        }
        
        /// <summary>
        /// Gets all objects within a specific area of the map.
        /// </summary>
        /// <param name="area">Area to search within</param>
        /// <returns>List of objects in the area</returns>
        public List<MapObject> GetObjectsInArea(Rect area) {
            return mapObjects.Where(o => area.Overlaps(o.boundingBox)).ToList();
        }
        
        /// <summary>
        /// Gets all terrain features within a specific area of the map.
        /// </summary>
        /// <param name="area">Area to search within</param>
        /// <returns>List of terrain features in the area</returns>
        public List<TerrainFeature> GetTerrainFeaturesInArea(Rect area) {
            return terrainFeatures.Where(t => area.Overlaps(t.boundingBox)).ToList();
        }
        
        /// <summary>
        /// Gets the terrain feature at a specific position.
        /// </summary>
        /// <param name="position">Normalized position (0-1)</param>
        /// <returns>Terrain feature at position or null if none found</returns>
        public TerrainFeature GetTerrainFeatureAtPosition(Vector2 position) {
            return terrainFeatures.FirstOrDefault(t => {
                float normX = position.x * heightMap.width;
                float normY = position.y * heightMap.height;
                return t.boundingBox.Contains(new Vector2(normX, normY));
            });
        }
        
        /// <summary>
        /// Gets the estimated height at a specific position.
        /// </summary>
        /// <param name="position">Normalized position (0-1)</param>
        /// <returns>Estimated height (0-1)</returns>
        public float GetHeightAtPosition(Vector2 position) {
            if (heightMap == null) return 0f;
            
            int x = Mathf.Clamp(Mathf.RoundToInt(position.x * heightMap.width), 0, heightMap.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(position.y * heightMap.height), 0, heightMap.height - 1);
            
            return heightMap.GetPixel(x, y).r;
        }
        
        /// <summary>
        /// Gets the nearest object to a specific position.
        /// </summary>
        /// <param name="position">Normalized position (0-1)</param>
        /// <param name="maxDistance">Maximum normalized distance (0-1)</param>
        /// <returns>Nearest object or null if none found within maxDistance</returns>
        public MapObject GetNearestObject(Vector2 position, float maxDistance = 0.1f) {
            if (mapObjects.Count == 0) return null;
            
            float minDistSq = maxDistance * maxDistance;
            MapObject nearest = null;
            
            foreach (var obj in mapObjects) {
                float distSq = (obj.position - position).sqrMagnitude;
                if (distSq < minDistSq) {
                    minDistSq = distSq;
                    nearest = obj;
                }
            }
            
            return nearest;
        }
        
        /// <summary>
        /// Gets the water level at a specific position.
        /// </summary>
        /// <param name="position">Normalized position (0-1)</param>
        /// <returns>Water level (0 if no water)</returns>
        public float GetWaterLevelAtPosition(Vector2 position) {
            foreach (var water in waterBodies) {
                if (water.containsPoint(position)) {
                    return water.waterLevel;
                }
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Gets a summarized report of the analysis results.
        /// </summary>
        /// <returns>Text summary of analysis results</returns>
        public string GetSummary() {
            StringBuilder sb = new StringBuilder();
            
            sb.AppendLine("ANALYSIS RESULTS SUMMARY");
            sb.AppendLine($"Time: {metadata.timestamp}");
            sb.AppendLine($"Source: {metadata.sourceImageName}");
            sb.AppendLine($"Resolution: {metadata.imageWidth}x{metadata.imageHeight}");
            sb.AppendLine($"Total processing time: {timings.totalTime:F2}s");
            sb.AppendLine();
            
            sb.AppendLine("TERRAIN FEATURES:");
            var terrainTypes = terrainFeatures.GroupBy(t => t.type);
            foreach (var group in terrainTypes) {
                sb.AppendLine($"  • {group.Key}: {group.Count()}");
            }
            sb.AppendLine();
            
            sb.AppendLine("MAP OBJECTS:");
            var objectTypes = mapObjects.GroupBy(o => o.type);
            foreach (var group in objectTypes) {
                sb.AppendLine($"  • {group.Key}: {group.Count()}");
            }
            sb.AppendLine();
            
            sb.AppendLine("OBJECT GROUPS:");
            sb.AppendLine($"  • Total groups: {objectGroups.Count}");
            sb.AppendLine();
            
            sb.AppendLine("ELEVATION RANGE:");
            sb.AppendLine($"  • Min: {elevationRange.minHeight:F2}m");
            sb.AppendLine($"  • Max: {elevationRange.maxHeight:F2}m");
            sb.AppendLine($"  • Range: {elevationRange.range:F2}m");
            sb.AppendLine();
            
            sb.AppendLine("WATER BODIES:");
            sb.AppendLine($"  • Count: {waterBodies.Count}");
            float totalWaterArea = waterBodies.Sum(w => w.normalizedArea);
            sb.AppendLine($"  • Total coverage: {totalWaterArea * 100:F1}%");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Creates a minimal version of the results with just essential data.
        /// </summary>
        /// <returns>Lightweight version of the results</returns>
        public AnalysisResults CreateLightweightVersion() {
            // Create a copy with only essential data for performance
            AnalysisResults lightweight = new AnalysisResults();
            
            lightweight.metadata = this.metadata;
            lightweight.elevationRange = this.elevationRange;
            lightweight.statistics = this.statistics;
            
            // Only copy object references, not textures
            lightweight.terrainFeatures = this.terrainFeatures;
            lightweight.mapObjects = this.mapObjects;
            lightweight.objectGroups = this.objectGroups;
            lightweight.waterBodies = this.waterBodies;
            
            // Don't copy potentially large textures
            lightweight.heightMap = null;
            lightweight.segmentationMap = null;
            
            return lightweight;
        }
        
        #endregion
    }
    
    #region Terrain Features
    
    /// <summary>
    /// Represents a detected terrain feature like mountain, lake, forest, etc.
    /// </summary>
    [Serializable]
    public class TerrainFeature {
        /// <summary>
        /// Feature type (mountain, water, forest, etc.).
        /// </summary>
        public string type;
        
        /// <summary>
        /// Human-readable label for display.
        /// </summary>
        public string label;
        
        /// <summary>
        /// Bounding box in image coordinates.
        /// </summary>
        public Rect boundingBox;
        
        /// <summary>
        /// Segmentation mask for precise boundary.
        /// </summary>
        public Texture2D segmentMask;
        
        /// <summary>
        /// Visualization color for the feature.
        /// </summary>
        public Color segmentColor;
        
        /// <summary>
        /// Detection confidence (0-1).
        /// </summary>
        public float confidence;
        
        /// <summary>
        /// Estimated elevation in meters.
        /// </summary>
        public float elevation;
        
        /// <summary>
        /// Detailed description of the terrain feature.
        /// </summary>
        public string description;
        
        /// <summary>
        /// Approximate slope of the terrain feature in degrees.
        /// </summary>
        public float slope;
        
        /// <summary>
        /// Texture type for this terrain (rock, grass, sand, etc.).
        /// </summary>
        public string textureType;
        
        /// <summary>
        /// Additional metadata for this feature.
        /// </summary>
        public Dictionary<string, object> metadata = new Dictionary<string, object>();
        
        /// <summary>
        /// Area of this feature in square meters.
        /// </summary>
        public float areaInSquareMeters;
        
        /// <summary>
        /// Feature roughness (0-1, higher means more rugged).
        /// </summary>
        public float roughness;
        
        /// <summary>
        /// Whether this feature can have vegetation.
        /// </summary>
        public bool canHaveVegetation;
        
        /// <summary>
        /// Whether this feature is a water body.
        /// </summary>
        public bool isWater;
        
        /// <summary>
        /// Whether this feature is suitable for buildings.
        /// </summary>
        public bool canHaveBuildings;
        
        /// <summary>
        /// Geological class of the terrain.
        /// </summary>
        public string geologicalClass;
        
        /// <summary>
        /// Local height variation within the feature.
        /// </summary>
        public float heightVariation;
        
        /// <summary>
        /// Gets a precise normalized boundary contour of the feature.
        /// </summary>
        /// <returns>List of normalized contour points</returns>
        public List<Vector2> GetNormalizedContour() {
            if (segmentMask == null) {
                // Fallback to bounding box corners if no mask
                return new List<Vector2> {
                    new Vector2(boundingBox.x / segmentMask.width, boundingBox.y / segmentMask.height),
                    new Vector2((boundingBox.x + boundingBox.width) / segmentMask.width, boundingBox.y / segmentMask.height),
                    new Vector2((boundingBox.x + boundingBox.width) / segmentMask.width, (boundingBox.y + boundingBox.height) / segmentMask.height),
                    new Vector2(boundingBox.x / segmentMask.width, (boundingBox.y + boundingBox.height) / segmentMask.height)
                };
            }
            
            // Extract contour from mask
            // This is a simplified implementation - a real one would use computer vision techniques
            List<Vector2> contour = new List<Vector2>();
            
            // Placeholder - in a real implementation, we'd trace the mask boundary
            // For now, return a circle approximation
            int steps = 36;
            for (int i = 0; i < steps; i++) {
                float angle = i * (2 * Mathf.PI / steps);
                float radius = Mathf.Min(boundingBox.width, boundingBox.height) * 0.5f;
                float centerX = boundingBox.x + boundingBox.width * 0.5f;
                float centerY = boundingBox.y + boundingBox.height * 0.5f;
                
                float x = centerX + Mathf.Cos(angle) * radius;
                float y = centerY + Mathf.Sin(angle) * radius;
                
                contour.Add(new Vector2(x / segmentMask.width, y / segmentMask.height));
            }
            
            return contour;
        }
    }
    
    /// <summary>
    /// Represents a specific region of terrain with consistent properties.
    /// </summary>
    [Serializable]
    public class TerrainRegion {
        /// <summary>
        /// Region ID.
        /// </summary>
        public string id = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Region type (mountain, valley, plain, etc.).
        /// </summary>
        public string type;
        
        /// <summary>
        /// Boundary in image coordinates.
        /// </summary>
        public List<Vector2> boundary = new List<Vector2>();
        
        /// <summary>
        /// Region area in normalized units (0-1).
        /// </summary>
        public float normalizedArea;
        
        /// <summary>
        /// Median elevation in meters.
        /// </summary>
        public float elevation;
        
        /// <summary>
        /// Average slope in degrees.
        /// </summary>
        public float slope;
        
        /// <summary>
        /// Parent terrain feature if applicable.
        /// </summary>
        public TerrainFeature parentFeature;
        
        /// <summary>
        /// Vegetation density (0-1).
        /// </summary>
        public float vegetationDensity;
        
        /// <summary>
        /// Checks if the region contains a point.
        /// </summary>
        /// <param name="point">Normalized point (0-1)</param>
        /// <returns>True if the point is inside the region</returns>
        public bool ContainsPoint(Vector2 point) {
            if (boundary.Count < 3) return false;
            
            // Simple point-in-polygon test
            bool inside = false;
            for (int i = 0, j = boundary.Count - 1; i < boundary.Count; j = i++) {
                if (((boundary[i].y > point.y) != (boundary[j].y > point.y)) &&
                    (point.x < (boundary[j].x - boundary[i].x) * (point.y - boundary[i].y) / 
                     (boundary[j].y - boundary[i].y) + boundary[i].x)) {
                    inside = !inside;
                }
            }
            
            return inside;
        }
    }
    
    /// <summary>
    /// Represents a water body like a lake, river, or ocean.
    /// </summary>
    [Serializable]
    public class WaterBody {
        /// <summary>
        /// Water body ID.
        /// </summary>
        public string id = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Water type (lake, river, ocean, etc.).
        /// </summary>
        public string type;
        
        /// <summary>
        /// Water boundary in normalized coordinates.
        /// </summary>
        public List<Vector2> boundary = new List<Vector2>();
        
        /// <summary>
        /// Bounding box in image coordinates.
        /// </summary>
        public Rect boundingBox;
        
        /// <summary>
        /// Water level in meters.
        /// </summary>
        public float waterLevel;
        
        /// <summary>
        /// Water depth in meters.
        /// </summary>
        public float depth;
        
        /// <summary>
        /// Flow direction for rivers (0-360 degrees).
        /// </summary>
        public float flowDirection;
        
        /// <summary>
        /// Flow speed for rivers (m/s).
        /// </summary>
        public float flowSpeed;
        
        /// <summary>
        /// Turbidity level (0-1).
        /// </summary>
        public float turbidity;
        
        /// <summary>
        /// Water color.
        /// </summary>
        public Color waterColor;
        
        /// <summary>
        /// Normalized area (0-1).
        /// </summary>
        public float normalizedArea;
        
        /// <summary>
        /// Checks if the water body contains a point.
        /// </summary>
        /// <param name="point">Normalized point (0-1)</param>
        /// <returns>True if the point is inside the water body</returns>
        public bool containsPoint(Vector2 point) {
            if (boundary.Count < 3) {
                // Fallback to bounding box check
                return boundingBox.Contains(new Vector2(
                    point.x * boundingBox.width,
                    point.y * boundingBox.height
                ));
            }
            
            // Simple point-in-polygon test
            bool inside = false;
            for (int i = 0, j = boundary.Count - 1; i < boundary.Count; j = i++) {
                if (((boundary[i].y > point.y) != (boundary[j].y > point.y)) &&
                    (point.x < (boundary[j].x - boundary[i].x) * (point.y - boundary[i].y) / 
                     (boundary[j].y - boundary[i].y) + boundary[i].x)) {
                    inside = !inside;
                }
            }
            
            return inside;
        }
    }
    
    /// <summary>
    /// Data about vegetation distribution across the map.
    /// </summary>
    [Serializable]
    public class VegetationData {
        /// <summary>
        /// Vegetation density map (0-1).
        /// </summary>
        public Texture2D densityMap;
        
        /// <summary>
        /// Vegetation types and their distributions.
        /// </summary>
        public List<VegetationType> vegetationTypes = new List<VegetationType>();
        
        /// <summary>
        /// Global density factor (0-1).
        /// </summary>
        public float globalDensity = 0.5f;
        
        /// <summary>
        /// Elevation influence on vegetation (higher means more effect).
        /// </summary>
        public float elevationInfluence = 0.5f;
        
        /// <summary>
        /// Slope influence on vegetation (higher means more effect).
        /// </summary>
        public float slopeInfluence = 0.5f;
    }
    
    /// <summary>
    /// Represents a type of vegetation (trees, bushes, grass, etc.).
    /// </summary>
    [Serializable]
    public class VegetationType {
        /// <summary>
        /// Vegetation type name.
        /// </summary>
        public string name;
        
        /// <summary>
        /// Distribution probability (0-1).
        /// </summary>
        public float probability;
        
        /// <summary>
        /// Minimum elevation in meters.
        /// </summary>
        public float minElevation;
        
        /// <summary>
        /// Maximum elevation in meters.
        /// </summary>
        public float maxElevation;
        
        /// <summary>
        /// Maximum slope in degrees.
        /// </summary>
        public float maxSlope;
        
        /// <summary>
        /// Density factor (0-1).
        /// </summary>
        public float density;
        
        /// <summary>
        /// Model scale variation (0-1).
        /// </summary>
        public float scaleVariation;
    }
    
    /// <summary>
    /// Range of elevations in the terrain.
    /// </summary>
    [Serializable]
    public class ElevationRange {
        /// <summary>
        /// Minimum height in meters.
        /// </summary>
        public float minHeight;
        
        /// <summary>
        /// Maximum height in meters.
        /// </summary>
        public float maxHeight;
        
        /// <summary>
        /// Average height in meters.
        /// </summary>
        public float averageHeight;
        
        /// <summary>
        /// Total height range in meters.
        /// </summary>
        public float range => maxHeight - minHeight;
        
        /// <summary>
        /// Standard deviation of heights in meters.
        /// </summary>
        public float standardDeviation;
        
        /// <summary>
        /// Distribution of heights by percentile.
        /// </summary>
        public Dictionary<int, float> heightPercentiles = new Dictionary<int, float>();
    }
    
    #endregion
    
    #region Map Objects
    
    /// <summary>
    /// Represents an object detected on the map like building, tree, etc.
    /// </summary>
    [Serializable]
    public class MapObject {
        /// <summary>
        /// Object type (building, tree, vehicle, etc.).
        /// </summary>
        public string type;
        
        /// <summary>
        /// Human-readable label for display.
        /// </summary>
        public string label;
        
        /// <summary>
        /// Enhanced description from OpenAI or similar.
        /// </summary>
        public string enhancedDescription;
        
        /// <summary>
        /// Normalized position (0-1, 0-1).
        /// </summary>
        public Vector2 position;
        
        /// <summary>
        /// Bounding box in image coordinates.
        /// </summary>
        public Rect boundingBox;
        
        /// <summary>
        /// Segmentation mask for precise boundary.
        /// </summary>
        public Texture2D segmentMask;
        
        /// <summary>
        /// Visualization color for the object.
        /// </summary>
        public Color segmentColor;
        
        /// <summary>
        /// World-space scale.
        /// </summary>
        public Vector3 scale = Vector3.one;
        
        /// <summary>
        /// Y-axis rotation in degrees.
        /// </summary>
        public float rotation;
        
        /// <summary>
        /// Detection confidence (0-1).
        /// </summary>
        public float confidence;
        
        /// <summary>
        /// Whether this object is part of a group.
        /// </summary>
        public bool isGrouped;
        
        /// <summary>
        /// Parent group ID if grouped.
        /// </summary>
        public string groupId;
        
        /// <summary>
        /// Additional metadata for this object.
        /// </summary>
        public Dictionary<string, object> metadata = new Dictionary<string, object>();
        
        /// <summary>
        /// Unique identifier for this object.
        /// </summary>
        public string id = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Importance score (0-1) for object importance.
        /// </summary>
        public float importanceScore = 0.5f;
        
        /// <summary>
        /// Associated 3D model path if available.
        /// </summary>
        public string modelPath;
        
        /// <summary>
        /// Tags for categorization.
        /// </summary>
        public List<string> tags = new List<string>();
        
        /// <summary>
        /// Object height in meters.
        /// </summary>
        public float heightMeters;
        
        /// <summary>
        /// Object width in meters.
        /// </summary>
        public float widthMeters;
        
        /// <summary>
        /// Material types for this object.
        /// </summary>
        public List<string> materials = new List<string>();
        
        /// <summary>
        /// Whether this object casts shadows.
        /// </summary>
        public bool castsShadows = true;
        
        /// <summary>
        /// Gets the normalized size of the object.
        /// </summary>
        public Vector2 NormalizedSize {
            get {
                return new Vector2(
                    boundingBox.width / (segmentMask?.width ?? 1),
                    boundingBox.height / (segmentMask?.height ?? 1)
                );
            }
        }
        
        /// <summary>
        /// Gets the estimated world-space dimensions based on scale.
        /// </summary>
        public Vector3 EstimatedDimensions {
            get {
                // This is a simplified calculation - in a real implementation
                // we'd use the actual dimensions from the model
                return new Vector3(
                    widthMeters > 0 ? widthMeters : scale.x * 1.0f,
                    heightMeters > 0 ? heightMeters : scale.y * 1.0f,
                    widthMeters > 0 ? widthMeters : scale.z * 1.0f
                );
            }
        }
    }
    
    /// <summary>
    /// Group of similar objects for instancing and optimization.
    /// </summary>
    [Serializable]
    public class ObjectGroup {
        /// <summary>
        /// Unique group identifier.
        /// </summary>
        public string groupId = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Object type in this group.
        /// </summary>
        public string type;
        
        /// <summary>
        /// Objects in this group.
        /// </summary>
        public List<MapObject> objects = new List<MapObject>();
        
        /// <summary>
        /// Average position of objects in group.
        /// </summary>
        public Vector2 averagePosition {
            get {
                if (objects.Count == 0) return Vector2.zero;
                return objects.Aggregate(Vector2.zero, (sum, obj) => sum + obj.position) / objects.Count;
            }
        }
        
        /// <summary>
        /// Average scale of objects in group.
        /// </summary>
        public Vector3 averageScale {
            get {
                if (objects.Count == 0) return Vector3.one;
                return objects.Aggregate(Vector3.zero, (sum, obj) => sum + obj.scale) / objects.Count;
            }
        }
        
        /// <summary>
        /// Average confidence of objects in group.
        /// </summary>
        public float averageConfidence {
            get {
                if (objects.Count == 0) return 0f;
                return objects.Average(obj => obj.confidence);
            }
        }
        
        /// <summary>
        /// Enhanced description for this group.
        /// </summary>
        public string groupDescription;
        
        /// <summary>
        /// Tags for categorization.
        /// </summary>
        public List<string> tags = new List<string>();
        
        /// <summary>
        /// Model path for instanced rendering.
        /// </summary>
        public string instancedModelPath;
        
        /// <summary>
        /// Whether to use hardware instancing.
        /// </summary>
        public bool useHardwareInstancing = true;
        
        /// <summary>
        /// LOD settings for this group.
        /// </summary>
        public string lodSettings;
        
        /// <summary>
        /// Distribution pattern (grid, random, clustered, etc.).
        /// </summary>
        public string distributionPattern;
        
        /// <summary>
        /// Gets a list of all positions for objects in this group.
        /// </summary>
        /// <returns>List of positions</returns>
        public List<Vector2> GetAllPositions() {
            return objects.Select(o => o.position).ToList();
        }
        
        /// <summary>
        /// Gets a random object from this group.
        /// </summary>
        /// <returns>Random map object</returns>
        public MapObject GetRandomObject() {
            if (objects.Count == 0) return null;
            int index = UnityEngine.Random.Range(0, objects.Count);
            return objects[index];
        }
    }
    
    /// <summary>
    /// Represents a network of paths (roads, trails, etc.).
    /// </summary>
    [Serializable]
    public class PathNetwork {
        /// <summary>
        /// Network ID.
        /// </summary>
        public string id = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Path type (road, trail, river, etc.).
        /// </summary>
        public string type;
        
        /// <summary>
        /// Path segments.
        /// </summary>
        public List<PathSegment> segments = new List<PathSegment>();
        
        /// <summary>
        /// Path nodes (intersections, endpoints).
        /// </summary>
        public List<PathNode> nodes = new List<PathNode>();
        
        /// <summary>
        /// Path width in meters.
        /// </summary>
        public float widthMeters;
        
        /// <summary>
        /// Path material type.
        /// </summary>
        public string materialType;
        
        /// <summary>
        /// Traffic density (0-1).
        /// </summary>
        public float trafficDensity;
        
        /// <summary>
        /// Gets the total length of the path network in meters.
        /// </summary>
        public float TotalLengthMeters {
            get {
                return segments.Sum(s => s.lengthMeters);
            }
        }
        
        /// <summary>
        /// Gets all path points in sequence.
        /// </summary>
        /// <returns>List of path points</returns>
        public List<Vector2> GetAllPoints() {
            List<Vector2> points = new List<Vector2>();
            foreach (var segment in segments) {
                points.AddRange(segment.points);
            }
            return points;
        }
    }
    
    /// <summary>
    /// Represents a segment of a path.
    /// </summary>
    [Serializable]
    public class PathSegment {
        /// <summary>
        /// Segment ID.
        /// </summary>
        public string id = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Path points in normalized coordinates.
        /// </summary>
        public List<Vector2> points = new List<Vector2>();
        
        /// <summary>
        /// Start node ID.
        /// </summary>
        public string startNodeId;
        
        /// <summary>
        /// End node ID.
        /// </summary>
        public string endNodeId;
        
        /// <summary>
        /// Segment width in meters.
        /// </summary>
        public float widthMeters;
        
        /// <summary>
        /// Length in meters.
        /// </summary>
        public float lengthMeters;
        
        /// <summary>
        /// Segment properties.
        /// </summary>
        public Dictionary<string, object> properties = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// Represents a node in a path network (intersection, endpoint).
    /// </summary>
    [Serializable]
    public class PathNode {
        /// <summary>
        /// Node ID.
        /// </summary>
        public string id = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Node position in normalized coordinates.
        /// </summary>
        public Vector2 position;
        
        /// <summary>
        /// Connected segment IDs.
        /// </summary>
        public List<string> connectedSegments = new List<string>();
        
        /// <summary>
        /// Node type (intersection, endpoint, etc.).
        /// </summary>
        public string type;
        
        /// <summary>
        /// Whether this is a junction.
        /// </summary>
        public bool isJunction;
        
        /// <summary>
        /// Node properties.
        /// </summary>
        public Dictionary<string, object> properties = new Dictionary<string, object>();
    }
    
    #endregion
    
    #region Metadata
    
    /// <summary>
    /// Metadata about the analysis process.
    /// </summary>
    [Serializable]
    public class AnalysisMetadata {
        /// <summary>
        /// Timestamp of the analysis.
        /// </summary>
        public string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        
        /// <summary>
        /// Source image name.
        /// </summary>
        public string sourceImageName;
        
        /// <summary>
        /// Source image width.
        /// </summary>
        public int imageWidth;
        
        /// <summary>
        /// Source image height.
        /// </summary>
        public int imageHeight;
        
        /// <summary>
        /// Analysis version.
        /// </summary>
        public string version = "2.0";
        
        /// <summary>
        /// YOLO model version used.
        /// </summary>
        public string yoloVersion = "v12";
        
        /// <summary>
        /// SAM model version used.
        /// </summary>
        public string samVersion = "2";
        
        /// <summary>
        /// Analysis settings.
        /// </summary>
        public Dictionary<string, object> settings = new Dictionary<string, object>();
        
        /// <summary>
        /// Tags for categorization.
        /// </summary>
        public List<string> tags = new List<string>();
        
        /// <summary>
        /// User who ran the analysis.
        /// </summary>
        public string user = "dkaplan73";
        
        /// <summary>
        /// Current user's email.
        /// </summary>
        public string userEmail;
        
        /// <summary>
        /// Whether enhanced descriptions were used.
        /// </summary>
        public bool usedEnhancedDescriptions;
        
        /// <summary>
        /// Whether SAM segmentation was used.
        /// </summary>
        public bool usedSAMSegmentation;
        
        /// <summary>
        /// Whether Faster R-CNN was used.
        /// </summary>
        public bool usedFasterRCNN;
        
        /// <summary>
        /// System environment information.
        /// </summary>
        public Dictionary<string, string> environment = new Dictionary<string, string>();
    }
    
    /// <summary>
    /// Timing information for the analysis process.
    /// </summary>
    [Serializable]
    public class AnalysisTimings {
        /// <summary>
        /// Total analysis time in seconds.
        /// </summary>
        public float totalTime;
        
        /// <summary>
        /// YOLO detection time in seconds.
        /// </summary>
        public float yoloTime;
        
        /// <summary>
        /// SAM segmentation time in seconds.
        /// </summary>
        public float samTime;
        
        /// <summary>
        /// Faster R-CNN time in seconds.
        /// </summary>
        public float fasterRCNNTime;
        
        /// <summary>
        /// OpenAI description enhancement time in seconds.
        /// </summary>
        public float openAITime;
        
        /// <summary>
        /// Heightmap generation time in seconds.
        /// </summary>
        public float heightmapTime;
        
        /// <summary>
        /// Detailed stage timings.
        /// </summary>
        public Dictionary<string, float> stageTimes = new Dictionary<string, float>();
        
        /// <summary>
        /// Gets the performance breakdown by percentage.
        /// </summary>
        /// <returns>Dictionary of stage names to percentage of total time</returns>
        public Dictionary<string, float> GetPerformanceBreakdown() {
            Dictionary<string, float> breakdown = new Dictionary<string, float>();
            if (totalTime <= 0f) return breakdown;
            
            foreach (var entry in stageTimes) {
                breakdown[entry.Key] = entry.Value / totalTime * 100f;
            }
            
            return breakdown;
        }
    }
    
    /// <summary>
    /// Statistical information about the analysis.
    /// </summary>
    [Serializable]
    public class AnalysisStatistics {
        /// <summary>
        /// Total number of detected objects.
        /// </summary>
        public int objectCount;
        
        /// <summary>
        /// Total number of terrain features.
        /// </summary>
        public int terrainFeatureCount;
        
        /// <summary>
        /// Total number of object groups.
        /// </summary>
        public int groupCount;
        
        /// <summary>
        /// Average confidence of detections.
        /// </summary>
        public float averageConfidence;
        
        /// <summary>
        /// Water coverage percentage.
        /// </summary>
        public float waterCoveragePercent;
        
        /// <summary>
        /// Forest coverage percentage.
        /// </summary>
        public float forestCoveragePercent;
        
        /// <summary>
        /// Urban coverage percentage.
        /// </summary>
        public float urbanCoveragePercent;
        
        /// <summary>
        /// Object density (objects per square km).
        /// </summary>
        public float objectDensity;
        
        /// <summary>
        /// Object type counts.
        /// </summary>
        public Dictionary<string, int> objectTypeCounts = new Dictionary<string, int>();
        
        /// <summary>
        /// Terrain type counts.
        /// </summary>
        public Dictionary<string, int> terrainTypeCounts = new Dictionary<string, int>();
        
        /// <summary>
        /// Gets the most common object type.
        /// </summary>
        public string MostCommonObjectType {
            get {
                if (objectTypeCounts.Count == 0) return "None";
                return objectTypeCounts.OrderByDescending(kvp => kvp.Value).First().Key;
            }
        }
        
        /// <summary>
        /// Gets the most common terrain type.
        /// </summary>
        public string MostCommonTerrainType {
            get {
                if (terrainTypeCounts.Count == 0) return "None";
                return terrainTypeCounts.OrderByDescending(kvp => kvp.Value).First().Key;
            }
        }
    }
    
    #endregion
}

