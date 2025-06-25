using UnityEngine;

namespace Traversify.Terrain
{
    /// <summary>
    /// Stores analysis results for terrain generation.
    /// </summary>
    public class AnalysisResults
    {
        public Texture2D heightMap;
        public TerrainFeature[] terrainFeatures;
    }

    /// <summary>
    /// Represents a terrain feature with position and properties.
    /// </summary>
    public class TerrainFeature
    {
        public Rect boundingBox;
        public Texture2D segmentMask;
        public float estimatedHeight;
        public string featureType;
    }
}
