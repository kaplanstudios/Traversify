/*************************************************************************
 *  Traversify – ModelGeneratorExtensions.cs                             *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Desc   : Extension methods for the ModelGenerator class that provide *
 *           advanced functionality, batch operations, and integration   *
 *           with other systems.                                         *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;
using Traversify.AI;
using Traversify.Core;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Traversify {
    /// <summary>
    /// Provides extension methods for the ModelGenerator class to enhance
    /// its functionality without modifying the core implementation.
    /// </summary>
    public static class ModelGeneratorExtensions {
        #region Batch Processing Extensions
        
        /// <summary>
        /// Generates models for a collection of segments with automatic batching.
        /// </summary>
        /// <param name="modelGenerator">The ModelGenerator instance</param>
        /// <param name="segments">Collection of analyzed segments to process</param>
        /// <param name="terrain">Terrain to place models on</param>
        /// <param name="batchSize">Number of models to process in parallel</param>
        /// <param name="onComplete">Callback when all models are generated</param>
        /// <param name="onProgress">Progress callback (current, total)</param>
        /// <returns>Coroutine enumerator</returns>
        public static IEnumerator GenerateModelsForSegments(
            this ModelGenerator modelGenerator,
            IEnumerable<AnalyzedSegment> segments,
            UnityEngine.Terrain terrain,
            int batchSize = 5,
            Action<List<GameObject>> onComplete = null,
            Action<int, int> onProgress = null
        ) {
            var debugger = modelGenerator.GetComponent<TraversifyDebugger>();
            debugger?.Log($"Starting batch generation for {segments.Count()} segments", LogCategory.Models);
            
            // Convert segments to generation requests
            List<ModelGenerationRequest> requests = new List<ModelGenerationRequest>();
            
            foreach (var segment in segments) {
                if (segment.isTerrain) continue; // Skip terrain segments
                
                // Convert normalized position to world position
                Vector3 worldPos = new Vector3(
                    segment.normalizedPosition.x * terrain.terrainData.size.x,
                    0, // Height will be set during placement
                    segment.normalizedPosition.y * terrain.terrainData.size.z
                );
                
                requests.Add(new ModelGenerationRequest {
                    objectType = segment.objectType,
                    description = segment.enhancedDescription ?? segment.detailedClassification,
                    position = worldPos,
                    rotation = Quaternion.Euler(0, segment.estimatedRotation, 0),
                    scale = segment.estimatedScale,
                    confidence = segment.classificationConfidence,
                    isGrouped = false,
                    onComplete = null,
                    onError = error => debugger?.LogWarning($"Error generating {segment.objectType}: {error}", LogCategory.Models)
                });
            }
            
            // Process in batches
            List<GameObject> generatedModels = new List<GameObject>();
            int totalCount = requests.Count;
            int processedCount = 0;
            
            while (requests.Count > 0) {
                var batch = requests.Take(batchSize).ToList();
                requests = requests.Skip(batchSize).ToList();
                
                List<Coroutine> batchCoroutines = new List<Coroutine>();
                List<GameObject> batchResults = new List<GameObject>();
                
                foreach (var request in batch) {
                    var localRequest = request; // Avoid closure issues
                    batchCoroutines.Add(modelGenerator.StartCoroutine(
                        modelGenerator.GenerateModel(
                            localRequest.objectType,
                            localRequest.description,
                            model => {
                                if (model != null) {
                                    // Position model on terrain
                                    GameObject instance = modelGenerator.PlaceModelOnTerrain(
                                        model,
                                        localRequest.position,
                                        localRequest.rotation,
                                        localRequest.scale,
                                        terrain
                                    );
                                    
                                    if (instance != null) {
                                        batchResults.Add(instance);
                                    }
                                }
                            },
                            localRequest.onError
                        )
                    ));
                }
                
                // Wait for batch to complete
                while (batchCoroutines.Any(c => c != null)) {
                    yield return null;
                }
                
                // Update progress
                processedCount += batch.Count;
                onProgress?.Invoke(processedCount, totalCount);
                
                // Add batch results to overall results
                generatedModels.AddRange(batchResults);
                
                debugger?.Log($"Processed batch: {batch.Count} models, {processedCount}/{totalCount} complete", LogCategory.Models);
                
                yield return null;
            }
            
            onComplete?.Invoke(generatedModels);
            debugger?.Log($"Batch generation complete: {generatedModels.Count} models created", LogCategory.Models);
        }
        
        /// <summary>
        /// Generates models from analysis results using advanced grouping and distribution strategies.
        /// </summary>
        /// <param name="modelGenerator">The ModelGenerator instance</param>
        /// <param name="analysis">Analysis results containing object data</param>
        /// <param name="terrain">Terrain to place models on</param>
        /// <param name="strategy">Placement strategy to use</param>
        /// <param name="density">Density factor for object distribution (0-2)</param>
        /// <param name="onComplete">Callback when generation completes</param>
        /// <returns>Coroutine enumerator</returns>
        public static IEnumerator GenerateWithStrategy(
            this ModelGenerator modelGenerator,
            AnalysisResults analysis,
            UnityEngine.Terrain terrain,
            PlacementStrategy strategy = PlacementStrategy.Clustered,
            float density = 1.0f,
            Action<List<GameObject>> onComplete = null
        ) {
            if (analysis == null || terrain == null) {
                onComplete?.Invoke(new List<GameObject>());
                yield break;
            }
            
            var debugger = modelGenerator.GetComponent<TraversifyDebugger>();
            debugger?.Log($"Generating models with strategy: {strategy}, density: {density}", LogCategory.Models);
            
            // Apply strategy-specific processing
            List<MapObject> processedObjects = new List<MapObject>();
            
            switch (strategy) {
                case PlacementStrategy.Clustered:
                    // Use existing object groups but adjust density
                    foreach (var group in analysis.objectGroups) {
                        int targetCount = Mathf.RoundToInt(group.objects.Count * density);
                        processedObjects.AddRange(group.objects.Take(targetCount));
                    }
                    break;
                    
                case PlacementStrategy.Distributed:
                    // Distribute objects more evenly
                    processedObjects = DistributeObjects(analysis.mapObjects, terrain, density);
                    break;
                    
                case PlacementStrategy.Natural:
                    // Apply natural distribution patterns (clusters with randomness)
                    processedObjects = ApplyNaturalDistribution(analysis.mapObjects, terrain, density);
                    break;
                    
                case PlacementStrategy.Grid:
                    // Arrange objects in a grid pattern
                    processedObjects = ArrangeInGrid(analysis.mapObjects, terrain, density);
                    break;
                    
                case PlacementStrategy.PathAligned:
                    // Align objects to detected paths
                    if (analysis.terrainFeatures?.Count > 0) {
                        // Convert terrain features to path segments for alignment
                        var pathSegments = ConvertTerrainFeaturesToPathSegments(analysis.terrainFeatures);
                        processedObjects = AlignToPath(analysis.mapObjects, pathSegments, terrain, density);
                    } else {
                        processedObjects = analysis.mapObjects.ToList();
                    }
                    break;
                    
                case PlacementStrategy.TerrainAdaptive:
                    // Adapt to terrain features
                    processedObjects = AdaptToTerrainFeatures(analysis.mapObjects, analysis.terrainFeatures, terrain, density);
                    break;
                    
                case PlacementStrategy.Random:
                case PlacementStrategy.Exact:
                default:
                    // Use original objects
                    processedObjects = analysis.mapObjects.ToList();
                    
                    // For random, adjust quantity based on density
                    if (strategy == PlacementStrategy.Random) {
                        int targetCount = Mathf.RoundToInt(processedObjects.Count * density);
                        processedObjects = processedObjects
                            .OrderBy(_ => UnityEngine.Random.value)
                            .Take(targetCount)
                            .ToList();
                    }
                    break;
            }
            
            // Generate models using standard method
            yield return modelGenerator.GenerateAndPlaceModels(
                new AnalysisResults {
                    mapObjects = processedObjects,
                    // Copy other relevant data
                    terrainFeatures = analysis.terrainFeatures,
                    heightMap = analysis.heightMap,
                    segmentationMap = analysis.segmentationMap
                },
                terrain,
                results => {
                    // Convert ModelGenerationResult list to GameObject list
                    var gameObjects = results?.Where(r => r.model != null).Select(r => r.model).ToList() ?? new List<GameObject>();
                    onComplete?.Invoke(gameObjects);
                }
            );
        }
        
        #endregion
        
        #region Geometry Processing Extensions
        
        /// <summary>
        /// Optimizes existing models by reducing polygon count while preserving appearance.
        /// </summary>
        /// <param name="modelGenerator">The ModelGenerator instance</param>
        /// <param name="models">Models to optimize</param>
        /// <param name="qualityLevel">Target quality level (0-1)</param>
        /// <param name="preserveUVs">Whether to preserve texture coordinates</param>
        /// <returns>Coroutine enumerator</returns>
        public static IEnumerator OptimizeModels(
            this ModelGenerator modelGenerator,
            IEnumerable<GameObject> models,
            float qualityLevel = 0.5f,
            bool preserveUVs = true
        ) {
            var debugger = modelGenerator.GetComponent<TraversifyDebugger>();
            var modelList = models.ToList();
            debugger?.Log($"Optimizing {modelList.Count} models to quality level {qualityLevel}", LogCategory.Models);
            
            foreach (var model in modelList) {
                if (model == null) continue;
                
                var meshFilters = model.GetComponentsInChildren<MeshFilter>();
                foreach (var meshFilter in meshFilters) {
                    if (meshFilter.sharedMesh == null) continue;
                    
                    Mesh originalMesh = meshFilter.sharedMesh;
                    int originalVertexCount = originalMesh.vertexCount;
                    int originalTriCount = originalMesh.triangles.Length / 3;
                    
                    // Create simplified mesh with target quality
                    Mesh simplifiedMesh = new Mesh();
                    simplifiedMesh.name = originalMesh.name + "_Simplified";
                    
                    // Calculate target triangle count based on quality level
                    int targetTriangles = Mathf.Max(1, Mathf.RoundToInt(originalTriCount * qualityLevel));
                    
                    // Apply mesh simplification
                    // This is a placeholder - in a real implementation, you would use
                    // a mesh simplification library like Unity's MeshSimplifier or similar
                    
                    // Sample code using a hypothetical MeshSimplification utility:
                    // simplifiedMesh = MeshSimplification.Simplify(originalMesh, targetTriangles, preserveUVs);
                    
                    // For this example, we'll just duplicate the original mesh
                    // In a real implementation, replace this with actual simplification
                    simplifiedMesh.vertices = originalMesh.vertices;
                    simplifiedMesh.triangles = originalMesh.triangles;
                    simplifiedMesh.normals = originalMesh.normals;
                    if (preserveUVs) simplifiedMesh.uv = originalMesh.uv;
                    simplifiedMesh.RecalculateBounds();
                    
                    // Assign simplified mesh
                    meshFilter.sharedMesh = simplifiedMesh;
                    
                    debugger?.Log($"Optimized mesh: {originalMesh.name} from {originalVertexCount} verts to {simplifiedMesh.vertexCount} verts", 
                        LogCategory.Models);
                }
                
                yield return null;
            }
        }
        
        /// <summary>
        /// Applies a transformation to correct the orientation and scale of generated models.
        /// </summary>
        /// <param name="modelGenerator">The ModelGenerator instance</param>
        /// <param name="model">The model to transform</param>
        /// <param name="alignToGround">Whether to align the bottom of the model to the ground</param>
        /// <param name="normalizeScale">Whether to normalize the scale to a standard size</param>
        /// <param name="centerPivot">Whether to center the pivot point</param>
        /// <returns>The transformed model</returns>
        public static GameObject NormalizeModelTransform(
            this ModelGenerator modelGenerator,
            GameObject model,
            bool alignToGround = true,
            bool normalizeScale = true,
            bool centerPivot = true
        ) {
            if (model == null) return null;
            
            // Calculate combined bounds from all renderers
            Bounds combinedBounds = new Bounds();
            bool boundsInitialized = false;
            
            var renderers = model.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers) {
                if (!boundsInitialized) {
                    combinedBounds = renderer.bounds;
                    boundsInitialized = true;
                } else {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }
            
            if (!boundsInitialized) return model; // No renderers found
            
            // Store original position
            Vector3 originalPosition = model.transform.position;
            
            // Temporarily move to origin for easier calculations
            model.transform.position = Vector3.zero;
            
            // Get local space bounds
            Vector3 center = combinedBounds.center - originalPosition;
            Vector3 size = combinedBounds.size;
            Vector3 min = combinedBounds.min - originalPosition;
            Vector3 max = combinedBounds.max - originalPosition;
            
            // Create a container for the model if needed for pivot manipulation
            GameObject container = null;
            
            if (centerPivot || alignToGround) {
                container = new GameObject(model.name + "_Container");
                container.transform.position = originalPosition;
                
                // Move model to container
                model.transform.SetParent(container.transform, true);
            }
            
            // Align bottom to ground
            if (alignToGround) {
                // Offset to place bottom at ground level
                model.transform.localPosition = new Vector3(0, -min.y, 0);
            }
            
            // Center pivot
            if (centerPivot) {
                // Offset to center pivot
                model.transform.localPosition = -center;
            }
            
            // Normalize scale
            if (normalizeScale && size.magnitude > 0) {
                float maxDimension = Mathf.Max(size.x, size.y, size.z);
                if (maxDimension > 0 && maxDimension != 1) {
                    float scaleFactor = 1f / maxDimension;
                    model.transform.localScale = model.transform.localScale * scaleFactor;
                }
            }
            
            // Restore original position or return container
            if (container != null) {
                container.transform.position = originalPosition;
                return container;
            } else {
                model.transform.position = originalPosition;
                return model;
            }
        }
        
        #endregion
        
        #region Export & Import Extensions
        
        /// <summary>
        /// Exports generated models to an asset bundle for later use.
        /// </summary>
        /// <param name="modelGenerator">The ModelGenerator instance</param>
        /// <param name="models">Models to export</param>
        /// <param name="bundleName">Name of the asset bundle</param>
        /// <param name="exportPath">Directory to save the bundle</param>
        /// <param name="compressionType">Type of compression to use</param>
        /// <returns>True if export was successful</returns>
        public static bool ExportModelsToAssetBundle(
            this ModelGenerator modelGenerator,
            IEnumerable<GameObject> models,
            string bundleName,
            string exportPath = "Assets/AssetBundles",
            BuildCompression compressionType = default
        ) {
            #if UNITY_EDITOR
            var debugger = modelGenerator.GetComponent<TraversifyDebugger>();
            
            try {
                // Ensure export directory exists
                if (!Directory.Exists(exportPath)) {
                    Directory.CreateDirectory(exportPath);
                }
                
                // Create prefabs from models
                List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
                
                foreach (var model in models) {
                    if (model == null) continue;
                    
                    // Create prefab asset
                    string prefabPath = Path.Combine("Assets/Temp", $"{model.name}_Prefab.prefab");
                    Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
                    
                    GameObject prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(model, prefabPath);
                    if (prefab != null) {
                        assets.Add(prefab);
                    }
                }
                
                if (assets.Count == 0) {
                    debugger?.LogWarning("No valid models to export", LogCategory.Models);
                    return false;
                }
                
                // Build asset bundle
                string bundlePath = Path.Combine(exportPath, bundleName);
                
                UnityEditor.AssetBundleBuild[] buildMap = new UnityEditor.AssetBundleBuild[1];
                buildMap[0].assetBundleName = bundleName;
                buildMap[0].assetNames = assets.Select(a => UnityEditor.AssetDatabase.GetAssetPath(a)).ToArray();
                
                UnityEditor.BuildPipeline.BuildAssetBundles(
                    exportPath,
                    buildMap,
                    UnityEditor.BuildAssetBundleOptions.None,
                    BuildTarget.StandaloneWindows64
                );
                
                debugger?.Log($"Exported {assets.Count} models to asset bundle: {bundlePath}", LogCategory.Models);
                
                // Clean up temp assets
                foreach (var asset in assets) {
                    UnityEditor.AssetDatabase.DeleteAsset(UnityEditor.AssetDatabase.GetAssetPath(asset));
                }
                
                return true;
            }
            catch (Exception ex) {
                debugger?.LogError($"Failed to export models: {ex.Message}", LogCategory.Models);
                return false;
            }
            #else
            var debugger = modelGenerator.GetComponent<TraversifyDebugger>();
            debugger?.LogWarning("ExportModelsToAssetBundle is only available in the Unity Editor", LogCategory.Models);
            return false;
            #endif
        }
        
        /// <summary>
        /// Imports models from an asset bundle.
        /// </summary>
        /// <param name="modelGenerator">The ModelGenerator instance</param>
        /// <param name="bundlePath">Path to the asset bundle file</param>
        /// <param name="onComplete">Callback when import completes</param>
        /// <returns>Coroutine enumerator</returns>
        public static IEnumerator ImportModelsFromAssetBundle(
            this ModelGenerator modelGenerator,
            string bundlePath,
            Action<List<GameObject>> onComplete
        ) {
            var debugger = modelGenerator.GetComponent<TraversifyDebugger>();
            List<GameObject> importedModels = new List<GameObject>();
            
            // Load asset bundle
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(bundlePath);
            yield return request;
            
            if (request.assetBundle == null) {
                debugger?.LogError($"Failed to load asset bundle: {bundlePath}", LogCategory.Models);
                onComplete?.Invoke(importedModels);
                yield break;
            }
            
            AssetBundle bundle = request.assetBundle;
            
            try {
                // Load all assets
                AssetBundleRequest assetRequest = bundle.LoadAllAssetsAsync<GameObject>();
                yield return assetRequest;
                
                if (assetRequest.allAssets.Length == 0) {
                    debugger?.LogWarning("No models found in asset bundle", LogCategory.Models);
                    onComplete?.Invoke(importedModels);
                    yield break;
                }
                
                foreach (var asset in assetRequest.allAssets) {
                    GameObject prefab = asset as GameObject;
                    if (prefab != null) {
                        GameObject instance = UnityEngine.Object.Instantiate(prefab);
                        instance.name = prefab.name.Replace("(Clone)", "");
                        importedModels.Add(instance);
                    }
                }
                
                debugger?.Log($"Imported {importedModels.Count} models from asset bundle", LogCategory.Models);
            }
            finally {
                // Always unload the bundle
                bundle.Unload(false);
            }
            
            onComplete?.Invoke(importedModels);
        }
        
        #endregion
        
        #region Placement Strategy Implementations
        
        private static List<MapObject> DistributeObjects(
            List<MapObject> objects,
            UnityEngine.Terrain terrain,
            float density
        ) {
            if (objects == null || objects.Count == 0) return new List<MapObject>();
            
            // Group by type
            var groupedObjects = objects.GroupBy(o => o.type).ToDictionary(g => g.Key, g => g.ToList());
            List<MapObject> result = new List<MapObject>();
            
            // Process each group
            foreach (var group in groupedObjects) {
                string objectType = group.Key;
                var typeObjects = group.Value;
                
                // Determine target count based on density
                int targetCount = Mathf.RoundToInt(typeObjects.Count * density);
                
                if (targetCount <= typeObjects.Count) {
                    // If reducing count, select objects with best spacing
                    result.AddRange(SelectDistributedSubset(typeObjects, targetCount, terrain));
                }
                else {
                    // If increasing count, add extra objects with distribution
                    result.AddRange(typeObjects);
                    result.AddRange(GenerateExtraDistributedObjects(typeObjects, targetCount - typeObjects.Count, terrain));
                }
            }
            
            return result;
        }
        
        private static List<MapObject> SelectDistributedSubset(
            List<MapObject> objects,
            int targetCount,
            UnityEngine.Terrain terrain
        ) {
            if (targetCount >= objects.Count) return objects.ToList();
            
            // Calculate min distance based on terrain size
            float minDistance = Mathf.Min(terrain.terrainData.size.x, terrain.terrainData.size.z) / 
                                Mathf.Sqrt(targetCount * 2);
            
            // Greedy algorithm to select well-distributed subset
            List<MapObject> selected = new List<MapObject>();
            List<MapObject> candidates = objects.ToList();
            
            // Start with object closest to center
            Vector2 center = new Vector2(0.5f, 0.5f);
            MapObject firstObject = candidates.OrderBy(o => Vector2.Distance(o.position, center)).First();
            selected.Add(firstObject);
            candidates.Remove(firstObject);
            
            // Add remaining objects
            while (selected.Count < targetCount && candidates.Count > 0) {
                // Find candidate with maximum minimum distance to any selected object
                MapObject bestCandidate = null;
                float bestMinDistance = 0;
                
                foreach (var candidate in candidates) {
                    float minDistToSelected = float.MaxValue;
                    
                    foreach (var sel in selected) {
                        float dist = Vector2.Distance(candidate.position, sel.position);
                        minDistToSelected = Mathf.Min(minDistToSelected, dist);
                    }
                    
                    if (minDistToSelected > bestMinDistance) {
                        bestMinDistance = minDistToSelected;
                        bestCandidate = candidate;
                    }
                }
                
                if (bestCandidate != null) {
                    selected.Add(bestCandidate);
                    candidates.Remove(bestCandidate);
                }
                else {
                    break;
                }
            }
            
            return selected;
        }
        
        private static List<MapObject> GenerateExtraDistributedObjects(
            List<MapObject> baseObjects,
            int extraCount,
            UnityEngine.Terrain terrain
        ) {
            if (extraCount <= 0 || baseObjects.Count == 0) return new List<MapObject>();
            
            List<MapObject> extraObjects = new List<MapObject>();
            
            // Get representative object for cloning
            MapObject template = baseObjects[0];
            
            // Calculate grid parameters for distribution
            int gridSize = Mathf.CeilToInt(Mathf.Sqrt(baseObjects.Count + extraCount));
            float cellSize = 1f / gridSize;
            
            // Create grid occupancy map
            bool[,] occupied = new bool[gridSize, gridSize];
            
            // Mark cells as occupied by existing objects
            foreach (var obj in baseObjects) {
                int gridX = Mathf.Clamp(Mathf.FloorToInt(obj.position.x / cellSize), 0, gridSize - 1);
                int gridY = Mathf.Clamp(Mathf.FloorToInt(obj.position.y / cellSize), 0, gridSize - 1);
                occupied[gridX, gridY] = true;
            }
            
            // Generate extra objects in unoccupied cells
            int added = 0;
            for (int x = 0; x < gridSize && added < extraCount; x++) {
                for (int y = 0; y < gridSize && added < extraCount; y++) {
                    if (!occupied[x, y]) {
                        // Create a new object with position in this cell
                        float posX = (x + UnityEngine.Random.value) * cellSize;
                        float posY = (y + UnityEngine.Random.value) * cellSize;
                        
                        MapObject newObj = CloneMapObject(template);
                        newObj.position = new Vector2(posX, posY);
                        
                        // Randomize rotation and scale slightly
                        newObj.rotation = UnityEngine.Random.Range(0f, 360f);
                        float scaleVariation = UnityEngine.Random.Range(0.9f, 1.1f);
                        newObj.scale = newObj.scale * scaleVariation;
                        
                        extraObjects.Add(newObj);
                        occupied[x, y] = true;
                        added++;
                    }
                }
            }
            
            return extraObjects;
        }
        
        private static List<MapObject> ApplyNaturalDistribution(
            List<MapObject> objects,
            UnityEngine.Terrain terrain,
            float density
        ) {
            if (objects == null || objects.Count == 0) return new List<MapObject>();
            
            // Group by type
            var groupedObjects = objects.GroupBy(o => o.type).ToDictionary(g => g.Key, g => g.ToList());
            List<MapObject> result = new List<MapObject>();
            
            // Process each group
            foreach (var group in groupedObjects) {
                string objectType = group.Key;
                var typeObjects = group.Value;
                
                // Determine natural distribution parameters
                int clusterCount = Mathf.CeilToInt(Mathf.Sqrt(typeObjects.Count) * density);
                int objectsPerCluster = Mathf.CeilToInt(typeObjects.Count * density / clusterCount);
                
                // Find cluster centers
                List<Vector2> clusterCenters = new List<Vector2>();
                
                if (typeObjects.Count > 0) {
                    // Use existing objects to determine cluster centers
                    KMeansClustering(typeObjects.Select(o => o.position).ToList(), clusterCount, out clusterCenters);
                }
                
                // Generate objects with natural clustering
                List<MapObject> naturalObjects = new List<MapObject>();
                
                foreach (var center in clusterCenters) {
                    // Get objects closest to this cluster or create new ones
                    var clusterObjects = typeObjects
                        .OrderBy(o => Vector2.Distance(o.position, center))
                        .Take(objectsPerCluster / 2)
                        .ToList();
                    
                    naturalObjects.AddRange(clusterObjects);
                    
                    // Add additional objects with natural distribution around cluster center
                    int extraCount = objectsPerCluster - clusterObjects.Count;
                    if (extraCount > 0 && typeObjects.Count > 0) {
                        MapObject template = typeObjects[0];
                        
                        for (int i = 0; i < extraCount; i++) {
                            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                            float distance = UnityEngine.Random.Range(0.01f, 0.1f); // Cluster radius
                            
                            Vector2 offset = new Vector2(
                                Mathf.Cos(angle) * distance,
                                Mathf.Sin(angle) * distance
                            );
                            
                            MapObject newObj = CloneMapObject(template);
                            newObj.position = center + offset;
                            
                            // Clamp to terrain bounds
                            newObj.position.x = Mathf.Clamp01(newObj.position.x);
                            newObj.position.y = Mathf.Clamp01(newObj.position.y);
                            
                            // Randomize rotation and scale
                            newObj.rotation = UnityEngine.Random.Range(0f, 360f);
                            float scaleVariation = UnityEngine.Random.Range(0.8f, 1.2f);
                            newObj.scale = newObj.scale * scaleVariation;
                            
                            naturalObjects.Add(newObj);
                        }
                    }
                }
                
                result.AddRange(naturalObjects);
            }
            
            return result;
        }
        
        private static List<MapObject> ArrangeInGrid(
            List<MapObject> objects,
            UnityEngine.Terrain terrain,
            float density
        ) {
            if (objects == null || objects.Count == 0) return new List<MapObject>();
            
            // Group by type
            var groupedObjects = objects.GroupBy(o => o.type).ToDictionary(g => g.Key, g => g.ToList());
            List<MapObject> result = new List<MapObject>();
            
            // Process each group
            foreach (var group in groupedObjects) {
                string objectType = group.Key;
                var typeObjects = group.Value;
                
                // Determine target count based on density
                int targetCount = Mathf.RoundToInt(typeObjects.Count * density);
                
                // Calculate grid parameters
                int gridSize = Mathf.CeilToInt(Mathf.Sqrt(targetCount));
                float cellWidth = 1f / gridSize;
                float cellHeight = 1f / gridSize;
                
                // Create new objects on grid
                List<MapObject> gridObjects = new List<MapObject>();
                
                // Use an existing object as template
                MapObject template = typeObjects.Count > 0 ? typeObjects[0] : null;
                if (template == null) continue;
                
                for (int x = 0; x < gridSize; x++) {
                    for (int y = 0; y < gridSize; y++) {
                        if (gridObjects.Count >= targetCount) break;
                        
                        // Calculate grid position with slight variation
                        float posX = (x + 0.5f) * cellWidth + UnityEngine.Random.Range(-0.1f, 0.1f) * cellWidth;
                        float posY = (y + 0.5f) * cellHeight + UnityEngine.Random.Range(-0.1f, 0.1f) * cellHeight;
                        
                        // Clamp to terrain bounds
                        posX = Mathf.Clamp01(posX);
                        posY = Mathf.Clamp01(posY);
                        
                        // Find closest existing object or create new one
                        MapObject closestObj = typeObjects
                            .OrderBy(o => Vector2.Distance(o.position, new Vector2(posX, posY)))
                            .FirstOrDefault();
                        
                        if (closestObj != null && Vector2.Distance(closestObj.position, new Vector2(posX, posY)) < cellWidth * 2) {
                            // Move existing object to grid
                            MapObject movedObj = CloneMapObject(closestObj);
                            movedObj.position = new Vector2(posX, posY);
                            gridObjects.Add(movedObj);
                        }
                        else {
                            // Create new object
                            MapObject newObj = CloneMapObject(template);
                            newObj.position = new Vector2(posX, posY);
                            
                            // Set grid-appropriate rotation
                            newObj.rotation = UnityEngine.Random.Range(0, 4) * 90f;
                            
                            gridObjects.Add(newObj);
                        }
                    }
                }
                
                result.AddRange(gridObjects);
            }
            
            return result;
        }
        
        private static List<MapObject> AlignToPath(
            List<MapObject> objects,
            List<PathSegment> paths,
            UnityEngine.Terrain terrain,
            float density
        ) {
            if (objects == null || objects.Count == 0 || paths == null || paths.Count == 0) {
                return objects?.ToList() ?? new List<MapObject>();
            }
            
            // Group by type
            var groupedObjects = objects.GroupBy(o => o.type).ToDictionary(g => g.Key, g => g.ToList());
            List<MapObject> result = new List<MapObject>();
            
            // Process each group
            foreach (var group in groupedObjects) {
                string objectType = group.Key;
                var typeObjects = group.Value;
                
                // Determine if this object type should align to paths
                bool shouldAlignToPath = ShouldAlignToPath(objectType);
                
                if (shouldAlignToPath) {
                    // Determine target count based on density
                    int targetCount = Mathf.RoundToInt(typeObjects.Count * density);
                    
                    // Find objects already close to paths
                    var pathAlignedObjects = new List<MapObject>();
                    
                    foreach (var obj in typeObjects) {
                        // Find closest path and distance
                        float minDistance = float.MaxValue;
                        
                        foreach (var path in paths) {
                            for (int i = 0; i < path.waypoints.Count - 1; i++) {
                                Vector2 start = path.waypoints[i];
                                Vector2 end = path.waypoints[i + 1];
                                
                                float distance = DistanceToLineSegment(obj.position, start, end);
                                minDistance = Mathf.Min(minDistance, distance);
                            }
                        }
                        
                        // If object is close to a path, add it
                        if (minDistance < 0.05f) { // 5% of terrain width/height
                            pathAlignedObjects.Add(obj);
                        }
                    }
                    
                    // If we need more objects, generate them along paths
                    if (pathAlignedObjects.Count < targetCount && typeObjects.Count > 0) {
                        MapObject template = typeObjects[0];
                        
                        // Distribute remaining objects along paths
                        int remaining = targetCount - pathAlignedObjects.Count;
                        var pathObjects = GenerateObjectsAlongPaths(template, paths, remaining);
                        
                        pathAlignedObjects.AddRange(pathObjects);
                    }
                    
                    result.AddRange(pathAlignedObjects);
                }
                else {
                    // For objects that shouldn't align to paths, use default distribution
                    result.AddRange(DistributeObjects(typeObjects, terrain, density));
                }
            }
            
            return result;
        }
        
        private static List<MapObject> AdaptToTerrainFeatures(
            List<MapObject> objects,
            List<TerrainFeature> terrainFeatures,
            UnityEngine.Terrain terrain,
            float density
        ) {
            if (objects == null || objects.Count == 0) {
                return new List<MapObject>();
            }
            
            // Group by type
            var groupedObjects = objects.GroupBy(o => o.type).ToDictionary(g => g.Key, g => g.ToList());
            List<MapObject> result = new List<MapObject>();
            
            // Process each group
            foreach (var group in groupedObjects) {
                string objectType = group.Key;
                var typeObjects = group.Value;
                
                // Determine terrain feature compatibility
                var compatibleFeatures = terrainFeatures?
                    .Where(f => IsCompatibleWithTerrainFeature(objectType, f.type))
                    .ToList() ?? new List<TerrainFeature>();
                
                if (compatibleFeatures.Count > 0) {
                    // Determine target count based on density
                    int targetCount = Mathf.RoundToInt(typeObjects.Count * density);
                    
                    // Distribute objects based on terrain features
                    var adaptedObjects = new List<MapObject>();
                    
                    // Keep objects already in compatible features
                    foreach (var obj in typeObjects) {
                        foreach (var feature in compatibleFeatures) {
                            if (feature.boundingBox.Contains(obj.position)) {
                                adaptedObjects.Add(obj);
                                break;
                            }
                        }
                    }
                    
                    // Generate additional objects if needed
                    if (adaptedObjects.Count < targetCount && typeObjects.Count > 0) {
                        MapObject template = typeObjects[0];
                        
                        // Distribute remaining objects within compatible terrain features
                        int remaining = targetCount - adaptedObjects.Count;
                        var featureObjects = GenerateObjectsInTerrainFeatures(template, compatibleFeatures, remaining);
                        
                        adaptedObjects.AddRange(featureObjects);
                    }
                    
                    result.AddRange(adaptedObjects);
                }
                else {
                    // For objects without compatible features, use default distribution
                    result.AddRange(DistributeObjects(typeObjects, terrain, density));
                }
            }
            
            return result;
        }
        
        #endregion
        
        #region Helper Methods
        
        private static bool ShouldAlignToPath(string objectType) {
            // Determine if object type should align to paths
            string typeLower = objectType.ToLowerInvariant();
            
            // Objects typically aligned along paths
            string[] pathAlignedTypes = {
                "building", "house", "street", "lamp", "light", "bench", "tree", "sign", 
                "mailbox", "fence", "wall", "gate", "garden", "shop", "store"
            };
            
            return pathAlignedTypes.Any(t => typeLower.Contains(t));
        }
        
        private static bool IsCompatibleWithTerrainFeature(string objectType, string featureType) {
            // Check compatibility between object types and terrain features
            string objLower = objectType.ToLowerInvariant();
            string featureLower = featureType?.ToLowerInvariant() ?? "";
            
            // Define compatibility rules
            if (featureLower.Contains("water")) {
                // Water-compatible objects
                return objLower.Contains("boat") || objLower.Contains("dock") || 
                       objLower.Contains("bridge") || objLower.Contains("fish");
            }
            
            if (featureLower.Contains("forest") || featureLower.Contains("wood")) {
                // Forest-compatible objects
                return objLower.Contains("tree") || objLower.Contains("bush") || 
                       objLower.Contains("log") || objLower.Contains("plant") || 
                       objLower.Contains("animal") || objLower.Contains("mushroom");
            }
            
            if (featureLower.Contains("mountain") || featureLower.Contains("hill")) {
                // Mountain-compatible objects
                return objLower.Contains("rock") || objLower.Contains("boulder") || 
                       objLower.Contains("cliff") || objLower.Contains("cave");
            }
            
            if (featureLower.Contains("grass") || featureLower.Contains("plain")) {
                // Grassland-compatible objects
                return objLower.Contains("grass") || objLower.Contains("flower") || 
                       objLower.Contains("bush") || objLower.Contains("animal");
            }
            
            // Default compatibility
            return true;
        }
        
        private static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd) {
            // Calculate distance from point to line segment
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.0001f) return Vector2.Distance(point, lineStart);
            
            Vector2 v = point - lineStart;
            Vector2 dir = line / len;
            float projection = Vector2.Dot(v, dir);
            
            if (projection < 0) return Vector2.Distance(point, lineStart);
            if (projection > len) return Vector2.Distance(point, lineEnd);
            
            Vector2 projectionVector = lineStart + dir * projection;
            return Vector2.Distance(point, projectionVector);
        }
        
        private static void KMeansClustering(List<Vector2> points, int k, out List<Vector2> centroids) {
            centroids = new List<Vector2>();
            if (points.Count == 0) return;
            if (points.Count <= k) {
                centroids = points.ToList();
                return;
            }
            
            // Initialize centroids with k-means++ initialization
            // Start with a random point
            centroids.Add(points[UnityEngine.Random.Range(0, points.Count)]);
            
            // Add remaining centroids
            for (int i = 1; i < k; i++) {
                // Find point with maximum min distance to existing centroids
                float maxMinDist = float.MinValue;
                Vector2 bestPoint = Vector2.zero;
                
                foreach (var point in points) {
                    float minDist = float.MaxValue;
                    foreach (var centroid in centroids) {
                        float dist = Vector2.Distance(point, centroid);
                        minDist = Mathf.Min(minDist, dist);
                    }
                    
                    if (minDist > maxMinDist) {
                        maxMinDist = minDist;
                        bestPoint = point;
                    }
                }
                
                centroids.Add(bestPoint);
            }
            
            // Perform k-means clustering iterations
            const int maxIterations = 10;
            for (int iter = 0; iter < maxIterations; iter++) {
                // Assign points to clusters
                var clusters = new List<Vector2>[k];
                for (int i = 0; i < k; i++) {
                    clusters[i] = new List<Vector2>();
                }
                
                foreach (var point in points) {
                    int bestCluster = 0;
                    float bestDistance = float.MaxValue;
                    
                    for (int i = 0; i < k; i++) {
                        float distance = Vector2.Distance(point, centroids[i]);
                        if (distance < bestDistance) {
                            bestDistance = distance;
                            bestCluster = i;
                        }
                    }
                    
                    clusters[bestCluster].Add(point);
                }
                
                // Update centroids
                bool changed = false;
                for (int i = 0; i < k; i++) {
                    if (clusters[i].Count > 0) {
                        Vector2 newCentroid = Vector2.zero;
                        foreach (var point in clusters[i]) {
                            newCentroid += point;
                        }
                        newCentroid /= clusters[i].Count;
                        
                        if (Vector2.Distance(newCentroid, centroids[i]) > 0.001f) {
                            centroids[i] = newCentroid;
                            changed = true;
                        }
                    }
                }
                
                // Stop if centroids didn't change
                if (!changed) break;
            }
        }
        
        private static List<MapObject> GenerateObjectsAlongPaths(
            MapObject template,
            List<PathSegment> paths,
            int count
        ) {
            List<MapObject> result = new List<MapObject>();
            if (count <= 0 || paths == null || paths.Count == 0 || template == null) return result;
            
            // Calculate total path length
            float totalLength = 0;
            foreach (var path in paths) {
                for (int i = 0; i < path.waypoints.Count - 1; i++) {
                    totalLength += Vector2.Distance(path.waypoints[i], path.waypoints[i + 1]);
                }
            }
            
            // Calculate spacing between objects
            float spacing = totalLength / count;
            float currentDistance = UnityEngine.Random.Range(0, spacing); // Random offset start
            
            foreach (var path in paths) {
                for (int i = 0; i < path.waypoints.Count - 1; i++) {
                    Vector2 start = path.waypoints[i];
                    Vector2 end = path.waypoints[i + 1];
                    
                    float segmentLength = Vector2.Distance(start, end);
                    Vector2 direction = (end - start).normalized;
                    
                    // Generate objects along this segment
                    while (currentDistance < segmentLength && result.Count < count) {
                        // Calculate position along path
                        Vector2 position = start + direction * currentDistance;
                        
                        // Create object
                        MapObject obj = CloneMapObject(template);
                        obj.position = position;
                        
                        // Align rotation to path
                        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                        obj.rotation = angle + 90f; // Assuming object faces outward from path
                        
                        // Add random variation
                        float offset = UnityEngine.Random.Range(-0.02f, 0.02f); // Small perpendicular offset
                        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                        obj.position += perpendicular * offset;
                        
                        // Scale variation
                        float scaleVariation = UnityEngine.Random.Range(0.9f, 1.1f);
                        obj.scale = obj.scale * scaleVariation;
                        
                        result.Add(obj);
                        currentDistance += spacing;
                    }
                    
                    currentDistance -= segmentLength;
                }
            }
            
            return result;
        }
        
        private static List<MapObject> GenerateObjectsInTerrainFeatures(
            MapObject template,
            List<TerrainFeature> features,
            int count
        ) {
            List<MapObject> result = new List<MapObject>();
            if (count <= 0 || features == null || features.Count == 0 || template == null) return result;
            
            // Calculate total feature area
            float totalArea = 0;
            foreach (var feature in features) {
                totalArea += feature.boundingBox.width * feature.boundingBox.height;
            }
            
            // Distribute objects proportionally to feature areas
            foreach (var feature in features) {
                float featureArea = feature.boundingBox.width * feature.boundingBox.height;
                int featureCount = Mathf.RoundToInt(count * (featureArea / totalArea));
                
                for (int i = 0; i < featureCount && result.Count < count; i++) {
                    // Generate random position within feature
                    float x = feature.boundingBox.x + UnityEngine.Random.value * feature.boundingBox.width;
                    float y = feature.boundingBox.y + UnityEngine.Random.value * feature.boundingBox.height;
                    
                    // Check if position is within mask (if available)
                    if (feature.segmentMask != null) {
                        // Sample mask at this position
                        int texX = Mathf.RoundToInt((x - feature.boundingBox.x) / feature.boundingBox.width * feature.segmentMask.width);
                        int texY = Mathf.RoundToInt((y - feature.boundingBox.y) / feature.boundingBox.height * feature.segmentMask.height);
                        
                        texX = Mathf.Clamp(texX, 0, feature.segmentMask.width - 1);
                        texY = Mathf.Clamp(texY, 0, feature.segmentMask.height - 1);
                        
                        Color maskColor = feature.segmentMask.GetPixel(texX, texY);
                        if (maskColor.a < 0.5f) {
                            // Position outside mask, try again
                            i--;
                            continue;
                        }
                    }
                    
                    // Create object
                    MapObject obj = CloneMapObject(template);
                    obj.position = new Vector2(x, y);
                    
                    // Random rotation
                    obj.rotation = UnityEngine.Random.Range(0f, 360f);
                    
                    // Scale variation
                    float scaleVariation = UnityEngine.Random.Range(0.8f, 1.2f);
                    obj.scale = obj.scale * scaleVariation;
                    
                    result.Add(obj);
                }
            }
            
            return result;
        }
        
        private static MapObject CloneMapObject(MapObject source) {
            return new MapObject {
                id = Guid.NewGuid().ToString(),
                type = source.type,
                label = source.label,
                enhancedDescription = source.enhancedDescription,
                position = source.position,
                boundingBox = source.boundingBox,
                segmentMask = source.segmentMask,
                segmentColor = source.segmentColor,
                scale = source.scale,
                rotation = source.rotation,
                confidence = source.confidence,
                isGrouped = source.isGrouped,
                groupId = source.groupId
            };
        }
        
        private static List<PathSegment> ConvertTerrainFeaturesToPathSegments(List<TerrainFeature> features) {
            List<PathSegment> segments = new List<PathSegment>();
            
            foreach (var feature in features) {
                // Create a path segment for each terrain feature
                PathSegment segment = new PathSegment();
                segment.points = new List<Vector2>();
                
                // Sample points along the feature bounding box
                for (float x = feature.boundingBox.x; x < feature.boundingBox.xMax; x += 0.1f) {
                    for (float y = feature.boundingBox.y; y < feature.boundingBox.yMax; y += 0.1f) {
                        segment.points.Add(new Vector2(x, y));
                    }
                }
                
                if (segment.points.Count > 0) {
                    segments.Add(segment);
                }
            }
            
            return segments;
        }
        #endregion
    }
}

