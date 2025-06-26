using System.Collections.Generic;
using UnityEngine;

namespace Traversify.AI
{
    /// <summary>
    /// Represents a segmented region of the source image with associated detection info.
    /// </summary>
    public class ImageSegment
    {
        public Rect boundingBox;
        public Texture2D mask;
        public DetectedObject detectedObject;
        public Color color; // add segment color for rendering and mask overlay
        public float area; // Add area property for segment size calculation
    }

    /// <summary>
    /// Request payload for per-segment analysis routines.
    /// </summary>
    public class SegmentAnalysisRequest
    {
        public ImageSegment segment;
        public Texture2D sourceImage;
    }

    /// <summary>
    /// Stores results of analyzing a segment (classification, height, features, placement data).
    /// </summary>
    public class AnalyzedSegment
    {
        public ImageSegment originalSegment;
        public Rect boundingBox;
        public bool isTerrain;
        public float classificationConfidence;
        public string objectType;
        public string detailedClassification;
        public string enhancedDescription;
        public Dictionary<string, float> features;
        
        // Terrain-specific
        public float estimatedHeight;
        public Texture2D heightMap;
        public Dictionary<string, float> topologyFeatures;
        
        // Object-specific
        public Vector2 normalizedPosition;
        public Vector3 estimatedScale;
        public float estimatedRotation;
        public float placementConfidence;
    }

    /// <summary>
    /// Groups multiple AnalyzedSegments of the same type together for bulk processing.
    /// </summary>
    public class ObjectGrouping
    {
        public string groupId;
        public string objectType;
        public List<AnalyzedSegment> segments = new List<AnalyzedSegment>();
    }

    /// <summary>
    /// Simple result from object detection (class + confidence).
    /// </summary>
    public class DetectedObject
    {
        public string className;
        public float confidence;
        public Rect boundingBox; // Add bounding box property
        public int classIndex; // Add class index property
    }
}
