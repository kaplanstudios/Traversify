using System;
using System.Collections.Generic;
using UnityEngine;

namespace Traversify.AI {
    [Serializable]
    public class AnalysisResults {
        /// <summary>
        /// Final terrain features detected (e.g., forest, mountain, water bodies).
        /// </summary>
        public List<TerrainFeature> terrainFeatures = new List<TerrainFeature>();

        /// <summary>
        /// Final object instances detected (non-terrain).
        /// </summary>
        public List<MapObject> mapObjects = new List<MapObject>();

        /// <summary>
        /// Groups of similar objects, for instancing.
        /// </summary>
        public List<ObjectGroup> objectGroups = new List<ObjectGroup>();

        /// <summary>
        /// Combined heightmap texture (normalized 0–1).
        /// </summary>
        public Texture2D heightMap;

        /// <summary>
        /// Segmentation mask texture: each pixel color corresponds to a segment.
        /// </summary>
        public Texture2D segmentationMap;
    }

    [Serializable]
    public class TerrainFeature {
        public string label;
        public Rect boundingBox;
        public Texture2D segmentMask;
        public Color segmentColor;
        public float elevation;
        public Dictionary<string, object> metadata = new Dictionary<string, object>();
    }

    [Serializable]
    public class MapObject {
        public string type;
        public Vector2 position;
        public Vector3 scale;
        public float rotation;
        public string label;
        public string enhancedDescription;
        public float confidence;
        public Texture2D segmentMask;
        public Color segmentColor;
        public Dictionary<string, object> metadata = new Dictionary<string, object>();
    }

    [Serializable]
    public class ObjectGroup {
        public string groupId;
        public string type;
        public List<MapObject> objects = new List<MapObject>();
    }
}