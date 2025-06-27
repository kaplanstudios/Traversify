using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Traversify.Core;

namespace Traversify {
    public class SegmentationVisualizer : MonoBehaviour {
        [Header("Debug & Progress")]
        public TraversifyDebugger debugger;
        public bool enableDebugVisualization = false;

        [Header("Overlay Settings")]
        public GameObject overlayPrefab;       // Transparent mesh or quad prefab
        public Material overlayMaterial;       // Material used for mask overlays
        [Range(0f, 5f)] public float overlayYOffset = 0.1f;
        [Range(0f, 3f)] public float fadeDuration = 0.5f;

        [Header("Label Settings")]
        public GameObject labelPrefab;         // TextMesh or billboard prefab
        [Range(0.5f, 5f)] public float labelYOffset = 2f;

        private List<GameObject> _createdOverlays = new List<GameObject>();
        private List<GameObject> _createdLabels = new List<GameObject>();

        /// <summary>
        /// Visualize each segmented region by instantiating a colored overlay mesh over its area,
        /// and a floating label with its class name.
        /// </summary>
        public IEnumerator VisualizeSegments(
            AnalysisResults results,
            Terrain terrain,
            Texture2D sourceImage,
            Action<List<GameObject>> onComplete,
            Action<string> onError,
            Action<float> onProgress
        ) {
            try {
                ClearPrevious();
                if (results == null) {
                    onError?.Invoke("No analysis results for visualization.");
                    yield break;
                }

                int total = results.terrainFeatures.Count + results.mapObjects.Count;
                int count = 0;

                // Helper to update progress
                void ProgressStep(string stage) {
                    count++;
                    float prog = count / (float)total;
                    onProgress?.Invoke(prog);
                    debugger?.Log($"Visualizing: {stage} ({count}/{total})", LogCategory.Visualization);
                }

                // Visualize terrain areas first
                foreach (var tf in results.terrainFeatures) {
                    ProgressStep($"Terrain: {tf.label}");
                    yield return StartCoroutine(CreateOverlay(tf.segmentMask, tf.segmentColor,
                                                              terrain, tf.boundingBox, tf.label));
                }

                // Then visualize objects
                foreach (var obj in results.mapObjects) {
                    ProgressStep($"Object: {obj.type}");
                    yield return StartCoroutine(CreateOverlay(obj.segmentMask, obj.segmentColor,
                                                              terrain, obj.boundingBox, obj.type));
                }

                // Return all created GameObjects
                var all = new List<GameObject>();
                all.AddRange(_createdOverlays);
                all.AddRange(_createdLabels);
                onComplete?.Invoke(all);
            }
            catch (Exception ex) {
                debugger?.LogError($"SegmentationVisualizer error: {ex.Message}", LogCategory.Visualization);
                onError?.Invoke(ex.Message);
            }
        }

        /// <summary>
        /// Instantiates an overlay mesh and label for a single segment.
        /// </summary>
        private IEnumerator CreateOverlay(Texture2D mask, Color color,
                                         Terrain terrain, Rect bb, string labelText) {
            // Generate mesh from mask
            Mesh overlayMesh = MeshUtils.CreateMeshFromMask(mask, bb);
            if (overlayMesh == null) {
                yield break;
            }

            // Instantiate overlay GameObject
            var overlayGO = Instantiate(overlayPrefab);
            overlayGO.name = $"Overlay_{labelText}";
            overlayGO.transform.SetParent(terrain.transform, false);

            // Assign mesh and material
            var mf = overlayGO.GetComponent<MeshFilter>();
            var mr = overlayGO.GetComponent<MeshRenderer>();
            if (mf == null || mr == null) {
                Destroy(overlayGO);
                yield break;
            }
            mf.mesh = overlayMesh;
            mr.material = new Material(overlayMaterial);
            mr.material.color = new Color(color.r, color.g, color.b, 0f); // start transparent

            // Position overlay slightly above terrain surface
            Vector3 pos = new Vector3(bb.x + bb.width/2f, 0, bb.y + bb.height/2f);
            float y = SampleTerrainHeight(terrain, pos) + overlayYOffset;
            overlayGO.transform.position = new Vector3(pos.x, y, pos.z);

            // Animate fade-in
            float t = 0f;
            while (t < fadeDuration) {
                t += Time.deltaTime;
                float a = Mathf.Lerp(0f, 0.6f, t / fadeDuration);
                mr.material.color = new Color(color.r, color.g, color.b, a);
                yield return null;
            }
            mr.material.color = new Color(color.r, color.g, color.b, 0.6f);

            _createdOverlays.Add(overlayGO);

            // Instantiate label
            var labelGO = Instantiate(labelPrefab);
            labelGO.name = $"Label_{labelText}";
            labelGO.transform.SetParent(terrain.transform, false);
            var textComp = labelGO.GetComponentInChildren<TextMesh>();
            if (textComp != null) {
                textComp.text = labelText;
            }
            labelGO.transform.position = new Vector3(pos.x, y + labelYOffset, pos.z);
            Billboard.MakeBillboard(labelGO); // ensure faces camera

            _createdLabels.Add(labelGO);
        }

        /// <summary>
        /// Samples height of terrain at given world XZ position.
        /// </summary>
        private float SampleTerrainHeight(Terrain terrain, Vector3 worldPos) {
            if (terrain == null) return 0f;
            return terrain.SampleHeight(worldPos) + terrain.GetPosition().y;
        }

        /// <summary>
        /// Destroy previously created overlays and labels.
        /// </summary>
        private void ClearPrevious() {
            foreach (var go in _createdOverlays) Destroy(go);
            foreach (var go in _createdLabels) Destroy(go);
            _createdOverlays.Clear();
            _createdLabels.Clear();
        }
    }
}
// --- TerrainGenerator.cs: Part 2 of 2 ---

        [Header("Erosion & Refinement")]
        [Tooltip("Enable hydraulic erosion simulation")]
        public bool applyErosion = true;
        [Tooltip("Number of erosion iterations")]
        public int erosionIterations = 1000;
        [Tooltip("Erosion water capacity per droplet")]
        public float erosionCapacity = 1f;
        [Tooltip("Erosion deposition rate")]
        public float depositionRate = 0.1f;

        [Header("Infinite Terrain Settings")]
        [Tooltip("Enable chunk streaming")]
        public bool enableChunkStreaming = false;
        [Tooltip("Chunk size in world units")]
        public float chunkSize = 500f;
        [Tooltip("Load radius in chunks")]
        public int loadRadius = 1;

        private Dictionary<Vector2Int, Terrain> loadedChunks = new Dictionary<Vector2Int, Terrain>();
        private Transform playerTransform;

        /// <summary>
        /// Applies hydraulic erosion to the given TerrainData.
        /// </summary>
        public void ApplyHydraulicErosion(TerrainData data)
        {
            if (!applyErosion) return;
            debugger.Log($"Starting erosion ({erosionIterations} iterations)", LogCategory.Terrain);
            int res = data.heightmapResolution;
            float[,] heights = data.GetHeights(0, 0, res, res);

            System.Random random = new System.Random();
            for (int i = 0; i < erosionIterations; i++)
            {
                // Drop a single water droplet at random location
                float x = (float)random.NextDouble() * (res - 1);
                float y = (float)random.NextDouble() * (res - 1);
                float dirX = 0f, dirY = 0f;
                float speed = 1f, water = 1f, sediment = 0f;
                
                for (int step = 0; step < 30; step++)
                {
                    int cellX = Mathf.Clamp(Mathf.FloorToInt(x), 0, res - 1);
                    int cellY = Mathf.Clamp(Mathf.FloorToInt(y), 0, res - 1);
                    float height = heights[cellY, cellX];

                    // Compute height gradient
                    float gradX = (heights[cellY, cellX + 1] - heights[cellY, cellX - 1]) * 0.5f;
                    float gradY = (heights[cellY + 1, cellX] - heights[cellY - 1, cellX]) * 0.5f;

                    // Update direction
                    dirX = dirX * 0.9f - gradX * 0.1f;
                    dirY = dirY * 0.9f - gradY * 0.1f;
                    float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                    if (len > 0)
                    {
                        dirX /= len;
                        dirY /= len;
                    }

                    // Move droplet
                    x += dirX;
                    y += dirY;
                    if (x < 0 || x >= res - 1 || y < 0 || y >= res - 1) break;

                    float newHeight = heights[Mathf.FloorToInt(y), Mathf.FloorToInt(x)];
                    float deltaH = newHeight - height;

                    // Compute sediment capacity
                    float capacity = Mathf.Max(-deltaH * speed * water * erosionCapacity, 0.01f);

                    // Erode or deposit
                    if (sediment > capacity)
                    {
                        float depositAmount = (sediment - capacity) * depositionRate;
                        heights[cellY, cellX] += depositAmount;
                        sediment -= depositAmount;
                    }
                    else
                    {
                        float erodeAmount = Mathf.Min((capacity - sediment) * erosionCapacity, height);
                        heights[cellY, cellX] -= erodeAmount;
                        sediment += erodeAmount;
                    }

                    // Update speed and water
                    speed = Mathf.Sqrt(speed * speed + deltaH * 0.1f);
                    water *= 0.98f;
                }
            }

            data.SetHeights(0, 0, heights);
            debugger.Log("Hydraulic erosion complete", LogCategory.Terrain);
        }

        /// <summary>
        /// Starts managing chunk streaming around the player.
        /// </summary>
        public void InitializeChunkStreaming(Transform player)
        {
            if (!enableChunkStreaming) return;
            playerTransform = player;
            UpdateLoadedChunks();
            // Re-check every 1 second
            InvokeRepeating(nameof(UpdateLoadedChunks), 1f, 1f);
        }

        private void UpdateLoadedChunks()
        {
            if (playerTransform == null) return;
            Vector3 pos = playerTransform.position;
            int px = Mathf.FloorToInt(pos.x / chunkSize);
            int pz = Mathf.FloorToInt(pos.z / chunkSize);

            var needed = new HashSet<Vector2Int>();
            for (int dz = -loadRadius; dz <= loadRadius; dz++)
                for (int dx = -loadRadius; dx <= loadRadius; dx++)
                    needed.Add(new Vector2Int(px + dx, pz + dz));

            // Unload chunks no longer needed
            foreach (var key in new List<Vector2Int>(loadedChunks.Keys))
            {
                if (!needed.Contains(key))
                {
                    Destroy(loadedChunks[key].gameObject);
                    loadedChunks.Remove(key);
                    debugger.Log($"Unloaded chunk {key}", LogCategory.Streaming);
                }
            }

            // Load missing chunks
            foreach (var key in needed)
            {
                if (!loadedChunks.ContainsKey(key))
                {
                    GenerateChunk(key);
                }
            }
        }

        private void GenerateChunk(Vector2Int coord)
        {
            // Create height data, could blend at borders for seamless
            float[,] heights = new float[resolutionPerChunk, resolutionPerChunk];
            // Fill with noise or procedural if no analysis data
            for (int z = 0; z < resolutionPerChunk; z++)
                for (int x = 0; x < resolutionPerChunk; x++)
                    heights[z, x] = Mathf.PerlinNoise(
                        (coord.x * (resolutionPerChunk - 1) + x) * 0.01f,
                        (coord.y * (resolutionPerChunk - 1) + z) * 0.01f
                    ) * 0.5f + 0.5f;

            TerrainData td = new TerrainData
            {
                heightmapResolution = resolutionPerChunk,
                size = new Vector3(chunkSize, maxTerrainHeight, chunkSize)
            };
            td.SetHeights(0, 0, heights);

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = $"Chunk_{coord.x}_{coord.y}";
            go.transform.position = new Vector3(coord.x * chunkSize, 0, coord.y * chunkSize);

            if (buildNavMesh)
            {
                var nav = go.AddComponent<NavMeshSurface>();
                nav.collectObjects = CollectObjects.Children;
                nav.BuildNavMesh();
            }

            loadedChunks[coord] = go.GetComponent<Terrain>();
            debugger.Log($"Generated chunk {coord}", LogCategory.Streaming);
        }

        // Placeholder constants for chunk resolution
        private int resolutionPerChunk => 129;

    }
}
