/*************************************************************************
 *  Traversify – AnalysisTypes                                           *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Date   : 2025-06-27                                                  *
 *  Desc   : Common DTOs, enums, and interfaces shared across the       *
 *           Traversify analysis pipeline. Designed for flexibility      *
 *           with no hard-coded object classes or labels.                *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Traversify.AI {
    #region Enumerations
    /// <summary>
    /// High-level step identifiers for progress callbacks.
    /// </summary>
    public enum AnalysisStage {
        None = 0,
        Initialization = 1,
        YoloDetection = 2,
        Sam2Segmentation = 3,
        FasterRcnn = 4,
        OpenAIEnhance = 5,
        HeightEstimation = 6,
        TerrainAnalysis = 7,
        ObjectClassification = 8,
        PathDetection = 9,
        WaterBodyDetection = 10,
        VegetationAnalysis = 11,
        StructureRecognition = 12,
        Finalizing = 13,
        PostProcessing = 14,
        Complete = 15
    }

    /// <summary>
    /// Similarity metric used during clustering.
    /// </summary>
    public enum SimilarityMetric {
        Cosine,
        Euclidean,
        Manhattan,
        JaccardIndex,
        StructuralSimilarity,
        FeatureVector,
        SemanticEmbedding,
        HybridMetric
    }

    /// <summary>
    /// Object detection confidence levels.
    /// </summary>
    public enum ConfidenceLevel {
        VeryLow = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        VeryHigh = 4,
        Absolute = 5
    }

    /// <summary>
    /// Terrain classification types detected by the system.
    /// </summary>
    public enum TerrainClassification {
        Unknown = 0,
        Water = 1,
        Land = 2,
        Mountain = 3,
        Valley = 4,
        Plateau = 5,
        Desert = 6,
        Forest = 7,
        Urban = 8,
        Agricultural = 9,
        Wetland = 10,
        Tundra = 11,
        Coastal = 12,
        Volcanic = 13,
        Glacial = 14
    }

    /// <summary>
    /// Object placement strategies for 3D world generation.
    /// </summary>
    public enum PlacementStrategy {
        Exact,              // Place exactly as detected
        Clustered,          // Group similar objects
        Distributed,        // Evenly distribute
        Natural,            // Nature-inspired placement
        Grid,               // Grid-based placement
        Random,             // Random with constraints
        Hierarchical,       // Parent-child relationships
        PathAligned,        // Align to detected paths
        TerrainAdaptive     // Adapt to terrain features
    }

    /// <summary>
    /// Analysis quality presets.
    /// </summary>
    public enum AnalysisQuality {
        Draft,              // Fast, low quality
        Standard,           // Balanced
        High,               // High quality, slower
        Ultra,              // Maximum quality
        Custom              // User-defined settings
    }

    /// <summary>
    /// Segment interaction modes.
    /// </summary>
    public enum InteractionMode {
        None,
        Hover,
        Select,
        MultiSelect,
        Edit,
        Delete,
        Transform,
        Annotate
    }
    #endregion

    #region Interfaces
    /// <summary>
    /// Interface for objects that can be analyzed.
    /// </summary>
    public interface IAnalyzable {
        string Id { get; }
        string Type { get; }
        float Confidence { get; }
        Dictionary<string, object> Metadata { get; }
        BoundingBox GetBounds();
        void UpdateMetadata(string key, object value);
    }

    /// <summary>
    /// Interface for segmentable regions.
    /// </summary>
    public interface ISegmentable {
        Texture2D GetMask();
        Color GetSegmentColor();
        float GetArea();
        Vector2 GetCentroid();
        List<Vector2> GetContour();
    }

    /// <summary>
    /// Interface for enhanced AI descriptions.
    /// </summary>
    public interface IDescribable {
        string GetDescription();
        string GetEnhancedDescription();
        List<string> GetTags();
        Dictionary<string, float> GetAttributes();
    }

    /// <summary>
    /// Interface for 3D placeable objects.
    /// </summary>
    public interface IPlaceable {
        Vector3 GetPosition();
        Quaternion GetRotation();
        Vector3 GetScale();
        PlacementConstraints GetConstraints();
        void SetTransform(Vector3 position, Quaternion rotation, Vector3 scale);
    }
    #endregion

    #region Core Data Structures
    /// <summary>
    /// Represents a bounding box in 2D space with additional metrics.
    /// </summary>
    [Serializable]
    public class BoundingBox {
        public float x;
        public float y;
        public float width;
        public float height;
        
        // Additional properties
        public float confidence;
        public float overlap;
        public int classId;
        public string className;
        
        // Computed properties
        public float Area => width * height;
        public Vector2 Center => new Vector2(x + width / 2f, y + height / 2f);
        public float AspectRatio => width / height;
        
        public Rect ToRect() => new Rect(x, y, width, height);
        
        public bool Contains(Vector2 point) {
            return point.x >= x && point.x <= x + width &&
                   point.y >= y && point.y <= y + height;
        }
        
        public float IntersectionOverUnion(BoundingBox other) {
            float x1 = Mathf.Max(x, other.x);
            float y1 = Mathf.Max(y, other.y);
            float x2 = Mathf.Min(x + width, other.x + other.width);
            float y2 = Mathf.Min(y + height, other.y + other.height);
            
            if (x2 < x1 || y2 < y1) return 0f;
            
            float intersection = (x2 - x1) * (y2 - y1);
            float union = Area + other.Area - intersection;
            
            return intersection / union;
        }
    }


    /// <summary>
    /// Represents an analyzed segment with enhanced properties.
    /// </summary>
    [Serializable]
    public class AnalyzedSegment : IAnalyzable, IDescribable, IPlaceable {
        public string id;
        public ImageSegment originalSegment;
        public BoundingBox boundingBox;
        public bool isTerrain;
        public string objectType;
        public string detailedClassification;
        public string enhancedDescription;
        public float classificationConfidence;
        public Dictionary<string, float> features;
        public float estimatedHeight;
        public Texture2D heightMap;
        public Dictionary<string, float> topologyFeatures;
        public Vector2 normalizedPosition;
        public Vector3 estimatedScale;
        public float estimatedRotation;
        public float placementConfidence;
        public List<string> tags;
        public Dictionary<string, float> attributes;
        public PlacementConstraints constraints;
        public Dictionary<string, object> metadata;
        
        // IAnalyzable implementation
        public string Id => id;
        public string Type => objectType;
        public float Confidence => classificationConfidence;
        public Dictionary<string, object> Metadata => metadata ?? (metadata = new Dictionary<string, object>());
        
        public BoundingBox GetBounds() => boundingBox;
        
        public void UpdateMetadata(string key, object value) {
            if (metadata == null) metadata = new Dictionary<string, object>();
            metadata[key] = value;
        }
        
        // IDescribable implementation
        public string GetDescription() => detailedClassification;
        public string GetEnhancedDescription() => enhancedDescription;
        public List<string> GetTags() => tags ?? new List<string>();
        public Dictionary<string, float> GetAttributes() => attributes ?? new Dictionary<string, float>();
        
        // IPlaceable implementation
        public Vector3 GetPosition() => new Vector3(normalizedPosition.x, estimatedHeight, normalizedPosition.y);
        public Quaternion GetRotation() => Quaternion.Euler(0, estimatedRotation, 0);
        public Vector3 GetScale() => estimatedScale;
        public PlacementConstraints GetConstraints() => constraints ?? new PlacementConstraints();
        
        public void SetTransform(Vector3 position, Quaternion rotation, Vector3 scale) {
            normalizedPosition = new Vector2(position.x, position.z);
            estimatedHeight = position.y;
            estimatedRotation = rotation.eulerAngles.y;
            estimatedScale = scale;
        }
        
        public AnalyzedSegment() {
            id = Guid.NewGuid().ToString();
            features = new Dictionary<string, float>();
            topologyFeatures = new Dictionary<string, float>();
            tags = new List<string>();
            attributes = new Dictionary<string, float>();
            metadata = new Dictionary<string, object>();
            estimatedScale = Vector3.one;
        }
    }

    /// <summary>
    /// Placement constraints for objects in 3D space.
    /// </summary>
    [Serializable]
    public class PlacementConstraints {
        public bool requiresGroundContact = true;
        public bool canOverlap = false;
        public float minimumDistance = 0f;
        public float maximumSlope = 45f;
        public float minimumHeight = 0f;
        public float maximumHeight = float.MaxValue;
        public List<string> requiredNearbyTypes = new List<string>();
        public List<string> prohibitedNearbyTypes = new List<string>();
        public float nearbySearchRadius = 10f;
        public bool alignToTerrain = true;
        public bool allowUnderwater = false;
        public Vector3 rotationConstraints = new Vector3(0, 360, 0); // Min/max for each axis
        public Vector3 scaleConstraints = new Vector3(0.5f, 2f, 1f); // Min/max/uniform
    }

    /// <summary>
    /// Request structure for segment analysis.
    /// </summary>
    [Serializable]
    public class SegmentAnalysisRequest {
        public ImageSegment segment;
        public Texture2D sourceImage;
        public AnalysisQuality quality;
        public Dictionary<string, object> parameters;
        public Action<AnalyzedSegment> onComplete;
        public Action<string> onError;
        
        public SegmentAnalysisRequest() {
            quality = AnalysisQuality.Standard;
            parameters = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Request structure for model generation.
    /// </summary>
    [Serializable]
    public class ModelGenerationRequest {
        public string objectType;
        public string description;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public float confidence;
        public bool isGrouped;
        public Dictionary<string, object> styleParameters;
        public Action<GameObject> onComplete;
        public Action<string> onError;
        
        public ModelGenerationRequest() {
            rotation = Quaternion.identity;
            scale = Vector3.one;
            styleParameters = new Dictionary<string, object>();
        }
    }
    #endregion

    #region Analysis Results
    
    // AnalysisResults class moved to separate file: Scripts/Terrain/AnalysisResults.cs
    // TerrainFeature class moved to separate file: Scripts/Terrain/AnalysisResults.cs  
    // MapObject class moved to separate file: Scripts/Terrain/AnalysisResults.cs
    // ObjectGroup class moved to separate file: Scripts/Terrain/AnalysisResults.cs
    // PathSegment class moved to separate file: Scripts/Terrain/AnalysisResults.cs
    // WaterBody class moved to separate file: Scripts/Terrain/AnalysisResults.cs
    // VegetationType enum moved to separate file: Scripts/Terrain/AnalysisResults.cs
    // AnalysisStatistics class moved to separate file: Scripts/Terrain/AnalysisResults.cs
    // to avoid duplicate class definitions
    
    #endregion

    #region Helper Classes
    
    // Model3DData class moved to separate file: Scripts/AI/Model3DData.cs
    // to avoid duplicate class definition

    /// <summary>
    /// Configuration for analysis operations.
    /// </summary>
    [Serializable]
    public class AnalysisConfiguration {
        public AnalysisQuality quality;
        public bool useGPU;
        public bool enableParallelProcessing;
        public int maxThreads;
        public float confidenceThreshold;
        public float nmsThreshold;
        public int maxObjectsPerFrame;
        public bool enableHeightEstimation;
        public bool enablePathDetection;
        public bool enableWaterDetection;
        public bool enableVegetationAnalysis;
        public bool enableStructureRecognition;
        public bool enableSemanticSegmentation;
        public Dictionary<string, object> customParameters;
        
        public AnalysisConfiguration() {
            quality = AnalysisQuality.Standard;
            useGPU = true;
            enableParallelProcessing = true;
            maxThreads = 4;
            confidenceThreshold = 0.5f;
            nmsThreshold = 0.45f;
            maxObjectsPerFrame = 100;
            enableHeightEstimation = true;
            enablePathDetection = true;
            enableWaterDetection = true;
            enableVegetationAnalysis = true;
            enableStructureRecognition = true;
            enableSemanticSegmentation = true;
            customParameters = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Utility class for working with analysis types.
    /// </summary>
    public static class AnalysisTypeUtils {
        /// <summary>
        /// Determine if a class name represents terrain.
        /// </summary>
        public static bool IsTerrainClass(string className) {
            if (string.IsNullOrEmpty(className)) return false;
            
            string lower = className.ToLowerInvariant();
            string[] terrainKeywords = {
                "terrain", "ground", "land", "mountain", "hill", "valley",
                "plateau", "plain", "desert", "forest", "grass", "sand",
                "rock", "cliff", "slope", "ridge", "canyon", "gorge"
            };
            
            return terrainKeywords.Any(keyword => lower.Contains(keyword));
        }
        
        /// <summary>
        /// Determine if a class name represents water.
        /// </summary>
        public static bool IsWaterClass(string className) {
            if (string.IsNullOrEmpty(className)) return false;
            
            string lower = className.ToLowerInvariant();
            string[] waterKeywords = {
                "water", "ocean", "sea", "lake", "river", "stream",
                "pond", "pool", "canal", "creek", "bay", "lagoon"
            };
            
            return waterKeywords.Any(keyword => lower.Contains(keyword));
        }
        
        /// <summary>
        /// Determine if a class name represents vegetation.
        /// </summary>
        public static bool IsVegetationClass(string className) {
            if (string.IsNullOrEmpty(className)) return false;
            
            string lower = className.ToLowerInvariant();
            string[] vegetationKeywords = {
                "tree", "forest", "vegetation", "plant", "grass", "bush",
                "shrub", "foliage", "leaves", "canopy", "garden", "crop"
            };
            
            return vegetationKeywords.Any(keyword => lower.Contains(keyword));
        }
        
        /// <summary>
        /// Determine if a class name represents a structure.
        /// </summary>
        public static bool IsStructureClass(string className) {
            if (string.IsNullOrEmpty(className)) return false;
            
            string lower = className.ToLowerInvariant();
            string[] structureKeywords = {
                "building", "house", "structure", "bridge", "tower", "wall",
                "fence", "road", "path", "railway", "construction", "facility"
            };
            
            return structureKeywords.Any(keyword => lower.Contains(keyword));
        }
        
        /// <summary>
        /// Get confidence level from numeric confidence.
        /// </summary>
        public static ConfidenceLevel GetConfidenceLevel(float confidence) {
            if (confidence >= 0.95f) return ConfidenceLevel.Absolute;
            if (confidence >= 0.85f) return ConfidenceLevel.VeryHigh;
            if (confidence >= 0.70f) return ConfidenceLevel.High;
            if (confidence >= 0.50f) return ConfidenceLevel.Medium;
            if (confidence >= 0.30f) return ConfidenceLevel.Low;
            return ConfidenceLevel.VeryLow;
        }
        
        /// <summary>
        /// Calculate similarity between two objects.
        /// </summary>
        public static float CalculateSimilarity(IAnalyzable obj1, IAnalyzable obj2, SimilarityMetric metric) {
            switch (metric) {
                case SimilarityMetric.Cosine:
                    return CalculateCosineSimilarity(obj1, obj2);
                    
                case SimilarityMetric.Euclidean:
                    return CalculateEuclideanSimilarity(obj1, obj2);
                    
                case SimilarityMetric.JaccardIndex:
                    return CalculateJaccardSimilarity(obj1, obj2);
                    
                default:
                    return obj1.Type == obj2.Type ? 1f : 0f;
            }
        }
        
        private static float CalculateCosineSimilarity(IAnalyzable obj1, IAnalyzable obj2) {
            // Simple implementation based on metadata
            var keys = obj1.Metadata.Keys.Union(obj2.Metadata.Keys).ToList();
            if (keys.Count == 0) return 0f;
            
            float dotProduct = 0f;
            float mag1 = 0f, mag2 = 0f;
            
            foreach (var key in keys) {
                float v1 = 0f, v2 = 0f;
                
                if (obj1.Metadata.ContainsKey(key) && obj1.Metadata[key] is float)
                    v1 = (float)obj1.Metadata[key];
                if (obj2.Metadata.ContainsKey(key) && obj2.Metadata[key] is float)
                    v2 = (float)obj2.Metadata[key];
                
                dotProduct += v1 * v2;
                mag1 += v1 * v1;
                mag2 += v2 * v2;
            }
            
            if (mag1 == 0 || mag2 == 0) return 0f;
            return dotProduct / (Mathf.Sqrt(mag1) * Mathf.Sqrt(mag2));
        }
        
        private static float CalculateEuclideanSimilarity(IAnalyzable obj1, IAnalyzable obj2) {
            var bounds1 = obj1.GetBounds();
            var bounds2 = obj2.GetBounds();
            
            float distance = Vector2.Distance(bounds1.Center, bounds2.Center);
            float maxDistance = Mathf.Sqrt(bounds1.width * bounds1.width + bounds1.height * bounds1.height);
            
            return 1f - Mathf.Clamp01(distance / maxDistance);
        }
        
        private static float CalculateJaccardSimilarity(IAnalyzable obj1, IAnalyzable obj2) {
            var bounds1 = obj1.GetBounds();
            var bounds2 = obj2.GetBounds();
            
            return bounds1.IntersectionOverUnion(bounds2);
        }
    }
    #endregion
}

