using UnityEngine;

namespace Traversify
{
    /// <summary>
    /// Handles terrain generation from processed map data
    /// </summary>
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("Terrain Settings")]
        public float heightMapMultiplier = 30f;
        public bool generateWaterPlane = true;
        public float waterHeight = 0.1f;
        
        // Add your implementation methods here
    }
}
