using UnityEngine;

namespace Traversify
{
    /// <summary>
    /// Manages map analysis using AI models to detect and classify objects
    /// </summary>
    public class MapAnalyzer : MonoBehaviour
    {
        [Header("AI Models")]
        public string yoloModelPath;
        public string fasterRcnnModelPath;
        public string sam2ModelPath;
        
        [Header("API Configuration")]
        public string openAIApiKey;
        
        [Header("Processing Settings")]
        public int maxObjectsToProcess = 100;
        public float groupingThreshold = 0.1f;
        public bool useHighQuality = true;
        public int maxAPIRequestsPerFrame = 5;
        
        // Add your implementation methods here
    }
}
