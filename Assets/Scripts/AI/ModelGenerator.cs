using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;
using Traversify.AI;

namespace Traversify {
    [RequireComponent(typeof(TraversifyDebugger))]
    public class ModelGenerator : MonoBehaviour {
        // Singleton
        private static ModelGenerator _instance;
        public static ModelGenerator Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<ModelGenerator>();
                    if (_instance == null) {
                        var go = new GameObject("ModelGenerator");
                        _instance = go.AddComponent<ModelGenerator>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("Debug")]
        public TraversifyDebugger debugger;

        [Header("Generation Settings")]
        [Tooltip("Group similar objects into instances instead of unique models")]
        public bool groupSimilarObjects = true;
        [Tooltip("Similarity threshold (0–1) for instancing")]
        [Range(0f,1f)] public float instancingSimilarity = 0.8f;

        [Header("AI & Tripo3D Settings")]
        [Tooltip("OpenAI key for description enhancement")]
        public string openAIApiKey;
        [Tooltip("Tripo3D API key for model generation")]
        public string tripo3DApiKey;
        [Tooltip("Timeout seconds per model request")]
        public float generationTimeout = 20f;
        [Tooltip("Max concurrent API calls")]
        public int maxConcurrentRequests = 4;
        [Tooltip("Delay seconds between requests")]
        public float apiRateLimitDelay = 0.5f;

        // Internal
        private Queue<AnalyzedSegment> _generationQueue = new Queue<AnalyzedSegment>();
        private int _activeRequests = 0;

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            debugger = GetComponent<TraversifyDebugger>();
            if (debugger == null) debugger = gameObject.AddComponent<TraversifyDebugger>();
            debugger.Log("ModelGenerator initialized", LogCategory.System);
        }

        /// <summary>
        /// Generate and place 3D models based on analysis results and terrain.
        /// </summary>
        public IEnumerator GenerateAndPlaceModels(
            AnalysisResults analysis,
            Terrain terrain,
            Action<List<GameObject>> onComplete,
            Action<string> onError,
            Action<int,int> onProgress
        ) {
            try {
                if (analysis == null) {
                    onError?.Invoke("No analysis results provided");
                    yield break;
                }

                // Prepare queue: either group or individual
                var segments = analysis.mapObjects;
                if (groupSimilarObjects) {
                    // Group by similarity threshold
                    var groups = ClusterByTypeAndSimilarity(segments);
                    foreach (var grp in groups) {
                        _generationQueue.Enqueue(grp);
                    }
                } else {
                    foreach (var seg in segments) {
                        _generationQueue.Enqueue(seg);
                    }
                }

                var createdObjects = new List<GameObject>();
                int total = _generationQueue.Count;
                int done = 0;

                // Process queue with concurrency limit
                while (_generationQueue.Count > 0) {
                    if (_activeRequests < maxConcurrentRequests) {
                        var segment = _generationQueue.Dequeue();
                        StartCoroutine(GenerateModelForSegment(segment, terrain, obj => {
                            createdObjects.Add(obj);
                            done++;
                            onProgress?.Invoke(done, total);
                        }, error => {
                            debugger.LogError($"Model generation error: {error}", LogCategory.Models);
                        }));
                    }
                    yield return null;
                }

                // Wait until all requests complete
                while (_activeRequests > 0) yield return null;

                onComplete?.Invoke(createdObjects);
            }
            catch (Exception ex) {
                debugger.LogError($"GenerateAndPlaceModels failed: {ex.Message}", LogCategory.Models);
                onError?.Invoke(ex.Message);
            }
        }

        private IEnumerator GenerateModelForSegment(
            AnalyzedSegment segment,
            Terrain terrain,
            Action<GameObject> onCreated,
            Action<string> onError
        ) {
            _activeRequests++;
            GameObject instance = null;

            try {
                string prompt = segment.enhancedDescription;
                if (string.IsNullOrEmpty(prompt)) {
                    prompt = $"Generate a 3D model of a {segment.objectType}";
                }

                // Request model from Tripo3D
                Model3DData modelData = null;
                bool done = false;
                Traversify.AI.Tripo3D.RequestModel(prompt, tripo3DApiKey,
                    (data) => { modelData = data; done = true; },
                    (err) => { onError?.Invoke(err); done = true; }
                );

                float startTime = Time.time;
                while (!done && Time.time - startTime < generationTimeout) {
                    yield return null;
                }
                if (!done || modelData == null) {
                    throw new Exception("Model generation timed out or failed");
                }

                // Instantiate model
                instance = InstantiateModelData(modelData);
                instance.name = $"Model_{segment.objectType}";
                
                // Position & scale
                Vector3 worldPos = new Vector3(
                    segment.normalizedPosition.x * terrain.terrainData.size.x,
                    0,
                    segment.normalizedPosition.y * terrain.terrainData.size.z
                );
                worldPos.y = terrain.SampleHeight(worldPos) + 0.1f;
                instance.transform.position = worldPos;
                instance.transform.rotation = Quaternion.Euler(0, segment.estimatedRotation, 0);
                instance.transform.localScale = Vector3.one * segment.estimatedScale;

                onCreated?.Invoke(instance);
            }
            catch (Exception ex) {
                onError?.Invoke(ex.Message);
            }
            finally {
                _activeRequests--;
                yield return new WaitForSeconds(apiRateLimitDelay);
            }
        }

        /// <summary>
        /// Clusters mapObjects by type and mask similarity.
        /// </summary>
        private List<AnalyzedSegment> ClusterByTypeAndSimilarity(List<MapObject> mapObjects)
        {
            var result = new List<AnalyzedSegment>();
            var visited = new HashSet<MapObject>();

            foreach (var obj in mapObjects) {
                if (visited.Contains(obj)) continue;
                var cluster = new List<MapObject> { obj };
                visited.Add(obj);

                foreach (var other in mapObjects) {
                    if (!visited.Contains(other) && other.type == obj.type) {
                        float sim = MaskUtils.ComputeMaskSimilarity(obj.segmentMask, other.segmentMask);
                        if (sim >= instancingSimilarity) {
                            cluster.Add(other);
                            visited.Add(other);
                        }
                    }
                }

                // Create a representative segment
                var rep = new AnalyzedSegment {
                    objectType = obj.type,
                    normalizedPosition = AveragePositions(cluster),
                    estimatedScale = obj.scale.x,  // assume uniform
                    estimatedRotation = obj.rotation,
                    enhancedDescription = obj.enhancedDescription
                };

                result.Add(rep);
            }
            return result;
        }

        private Vector2 AveragePositions(List<MapObject> list)
        {
            Vector2 sum = Vector2.zero;
            foreach (var o in list) sum += o.position;
            return sum / list.Count;
        }

        private GameObject InstantiateModelData(Model3DData data)
        {
            // Create parent
            var go = new GameObject(data.name ?? "GeneratedModel");
            // Create mesh
            Mesh mesh = new Mesh();
            mesh.vertices = data.vertices;
            mesh.triangles = data.triangles;
            mesh.normals = data.normals;
            mesh.uv = data.uvs;

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Standard"));

            return go;
        }
    }
}
