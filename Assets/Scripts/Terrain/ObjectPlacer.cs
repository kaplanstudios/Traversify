/*************************************************************************
 *  Traversify – ObjectPlacer.cs                                         *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:53:13 UTC                                     *
 *  Desc   : Handles precise placement, orientation, and scaling of      *
 *           objects on terrain based on analysis results. Provides      *
 *           intelligent collision avoidance, terrain adaptation,        *
 *           and natural grouping capabilities for realistic             *
 *           environment generation.                                     *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;
using Traversify.AI;

namespace Traversify {
    /// <summary>
    /// Manages the placement, orientation, and scaling of objects on terrain
    /// based on analysis results, with intelligent collision avoidance and
    /// terrain adaptation for realistic environment generation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TraversifyDebugger))]
    public class ObjectPlacer : TraversifyComponent {
        #region Singleton Pattern
        
        private static ObjectPlacer _instance;
        
        /// <summary>
        /// Singleton instance of the ObjectPlacer.
        /// </summary>
        public static ObjectPlacer Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<ObjectPlacer>();
                    if (_instance == null) {
                        GameObject go = new GameObject("ObjectPlacer");
                        _instance = go.AddComponent<ObjectPlacer>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Inspector Properties
        
        [Header("Core Components")]
        [Tooltip("Debug and logging system")]
        [SerializeField] private TraversifyDebugger _debugger;
        
        [Header("Placement Settings")]
        [Tooltip("Surface alignment strength (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float _surfaceAlignmentStrength = 0.8f;
        
        [Tooltip("Maximum allowed surface slope angle in degrees")]
        [Range(0f, 90f)]
        [SerializeField] private float _maxSlopeAngle = 45f;
        
        [Tooltip("Surface alignment stability threshold (higher for more stable alignment)")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _surfaceStabilityThreshold = 0.1f;
        
        [Tooltip("Object spacing factor (minimum distance as multiplier of object size)")]
        [Range(0.5f, 5f)]
        [SerializeField] private float _objectSpacingFactor = 1.2f;
        
        [Tooltip("Maximum placement iterations per object")]
        [Range(1, 100)]
        [SerializeField] private int _maxPlacementIterations = 20;
        
        [Tooltip("Placement jitter amount (randomness in placement)")]
        [Range(0f, 10f)]
        [SerializeField] private float _placementJitter = 0.5f;
        
        [Tooltip("Rotation jitter amount in degrees")]
        [Range(0f, 180f)]
        [SerializeField] private float _rotationJitter = 15f;
        
        [Tooltip("Scale jitter amount (as percentage of original scale)")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _scaleJitter = 0.1f;
        
        [Tooltip("Enable object grouping for similar objects")]
        [SerializeField] private bool _enableObjectGrouping = true;
        
        [Header("Performance Settings")]
        [Tooltip("Maximum concurrent placement operations")]
        [Range(1, 50)]
        [SerializeField] private int _maxConcurrentPlacements = 10;
        
        [Tooltip("Use GPU instancing for similar objects")]
        [SerializeField] private bool _useGPUInstancing = true;
        
        [Tooltip("Enable precise collider checks")]
        [SerializeField] private bool _useColliderChecks = true;
        
        [Tooltip("Collision layers to check for placement")]
        [SerializeField] private LayerMask _collisionLayers = -1;
        
        [Header("Adaptive Placement")]
        [Tooltip("Apply additional rules based on object type")]
        [SerializeField] private bool _enableAdaptivePlacement = true;
        
        [Tooltip("List of object placement rules")]
        [SerializeField] private List<ObjectPlacementRule> _placementRules = new List<ObjectPlacementRule>();
        
        [Header("Debug Visualization")]
        [Tooltip("Show debug visualization of placement attempts")]
        [SerializeField] private bool _showDebugVisualization = false;
        
        [Tooltip("Debug visualization duration in seconds")]
        [Range(0f, 10f)]
        [SerializeField] private float _debugVisualizationDuration = 2f;
        
        #endregion
        
        #region Private Fields
        
        private Dictionary<string, GameObject> _placedObjects = new Dictionary<string, GameObject>();
        private HashSet<Vector3> _occupiedPositions = new HashSet<Vector3>();
        private Dictionary<string, List<PlacementAttempt>> _placementAttempts = new Dictionary<string, List<PlacementAttempt>>();
        private float _terrainHeight;
        private int _activeOperations = 0;
        private UnityEngine.Terrain _currentTerrain;
        private Vector3 _terrainSize;
        private Dictionary<string, Material> _instancedMaterials = new Dictionary<string, Material>();
        private Dictionary<string, GameObject> _prototypeObjects = new Dictionary<string, GameObject>();
        private Dictionary<string, int> _failedPlacements = new Dictionary<string, int>();
        private int _totalPlacedObjects = 0;
        private int _totalPlacementAttempts = 0;
        private List<GameObject> _lastPlacedObjects = new List<GameObject>();
        
        // Spatial partitioning for faster collision checks
        private const float GRID_CELL_SIZE = 5.0f;
        private Dictionary<Vector3Int, List<GameObject>> _spatialGrid = new Dictionary<Vector3Int, List<GameObject>>();
        
        // Callbacks for placement operations
        private Action<GameObject, string> _onObjectPlaced;
        private Action<string> _onPlacementFailed;
        private Action<int, int> _onProgressUpdated;
        private Action<List<GameObject>> _onPlacementComplete;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake() {
            // Singleton enforcement
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // Initialize debugger
            _debugger = GetComponent<TraversifyDebugger>();
            if (_debugger == null) {
                _debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            _debugger.Log("ObjectPlacer initializing...", LogCategory.System);
        }
        
        private void OnDestroy() {
            ClearPlacementData();
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Places objects on terrain based on analysis results.
        /// </summary>
        /// <param name="analysisResults">Analysis results containing object data</param>
        /// <param name="terrain">Target terrain for placement</param>
        /// <param name="modelProvider">Provider for object models</param>
        /// <param name="onComplete">Callback when placement is complete</param>
        /// <param name="onProgress">Callback for progress updates</param>
        /// <param name="onError">Callback for errors</param>
        /// <returns>Coroutine for tracking progress</returns>
        public IEnumerator PlaceObjects(
            AnalysisResults analysisResults,
            UnityEngine.Terrain terrain,
            IModelProvider modelProvider,
            Action<List<GameObject>> onComplete = null,
            Action<int, int> onProgress = null,
            Action<string> onError = null
        ) {
            if (analysisResults == null) {
                onError?.Invoke("Analysis results are null");
                yield break;
            }
            
            if (terrain == null) {
                onError?.Invoke("Terrain is null");
                yield break;
            }
            
            if (modelProvider == null) {
                onError?.Invoke("Model provider is null");
                yield break;
            }
            
            _debugger.Log($"Starting object placement for {analysisResults.mapObjects.Count} objects", LogCategory.Models);
            _debugger.StartTimer("ObjectPlacement");
            
            // Initialize state
            ClearPlacementData();
            _currentTerrain = terrain;
            _terrainSize = terrain.terrainData.size;
            _terrainHeight = terrain.terrainData.size.y;
            _onProgressUpdated = onProgress;
            _onPlacementComplete = onComplete;
            _lastPlacedObjects.Clear();
            
            // Process object groups if enabled, otherwise process individual objects
            List<PlacementTask> placementTasks = new List<PlacementTask>();
            
            if (_enableObjectGrouping && analysisResults.objectGroups != null && analysisResults.objectGroups.Count > 0) {
                // Group similar objects for more efficient placement
                foreach (var group in analysisResults.objectGroups) {
                    // Skip empty groups
                    if (group.objects == null || group.objects.Count == 0) continue;
                    
                    // Create a placement task for the group
                    PlacementTask groupTask = new PlacementTask {
                        ObjectType = group.type,
                        GroupId = group.groupId,
                        Objects = group.objects.ToList(),
                        Priority = CalculateGroupPriority(group)
                    };
                    
                    placementTasks.Add(groupTask);
                }
            } else {
                // Process individual objects
                foreach (var mapObject in analysisResults.mapObjects) {
                    PlacementTask task = new PlacementTask {
                        ObjectType = mapObject.type,
                        GroupId = Guid.NewGuid().ToString(),
                        Objects = new List<MapObject> { mapObject },
                        Priority = 0
                    };
                    
                    placementTasks.Add(task);
                }
            }
            
            // Sort tasks by priority (higher priority first)
            placementTasks.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            
            // Report total objects to place
            int totalObjectCount = placementTasks.Sum(task => task.Objects.Count);
            int placedObjectCount = 0;
            _totalPlacementAttempts = 0;
            
            // Process placement tasks with limited concurrency
            Queue<PlacementTask> taskQueue = new Queue<PlacementTask>(placementTasks);
            List<Coroutine> activeCoroutines = new List<Coroutine>();
            
            while (taskQueue.Count > 0 || activeCoroutines.Count > 0) {
                // Start new tasks up to concurrency limit
                while (taskQueue.Count > 0 && activeCoroutines.Count < _maxConcurrentPlacements) {
                    PlacementTask task = taskQueue.Dequeue();
                    
                    Coroutine placementCoroutine = StartCoroutine(
                        ProcessPlacementTask(task, modelProvider, (placedObjects, taskObjectCount) => {
                            placedObjectCount += taskObjectCount;
                            _lastPlacedObjects.AddRange(placedObjects);
                            onProgress?.Invoke(placedObjectCount, totalObjectCount);
                        })
                    );
                    
                    activeCoroutines.Add(placementCoroutine);
                }
                
                // Wait for at least one task to complete
                yield return new WaitUntil(() => activeCoroutines.Any(c => c == null || c.IsCompleted()));
                
                // Remove completed coroutines
                activeCoroutines.RemoveAll(c => c == null || c.IsCompleted());
                
                // Update progress
                onProgress?.Invoke(placedObjectCount, totalObjectCount);
                
                yield return null;
            }
            
            // Finalize placements
            yield return FinalizePlacements();
            
            // Log placement statistics
            float placementTime = _debugger.StopTimer("ObjectPlacement");
            _debugger.Log($"Placed {_totalPlacedObjects} objects in {placementTime:F2} seconds", LogCategory.Models);
            _debugger.Log($"Average placement time: {(placementTime / Math.Max(1, _totalPlacedObjects)):F4} seconds per object", LogCategory.Models);
            _debugger.Log($"Total placement attempts: {_totalPlacementAttempts}", LogCategory.Models);
            _debugger.Log($"Placement efficiency: {(float)_totalPlacedObjects / Math.Max(1, _totalPlacementAttempts):P2}", LogCategory.Models);
            
            // Report failed placements
            if (_failedPlacements.Count > 0) {
                foreach (var failure in _failedPlacements) {
                    _debugger.LogWarning($"Failed to place {failure.Value} objects of type '{failure.Key}'", LogCategory.Models);
                }
            }
            
            // Return the list of placed objects
            onComplete?.Invoke(_lastPlacedObjects);
        }
        
        /// <summary>
        /// Retrieves a placed object by its unique ID.
        /// </summary>
        /// <param name="objectId">The unique ID of the object</param>
        /// <returns>The placed GameObject, or null if not found</returns>
        public GameObject GetPlacedObject(string objectId) {
            if (string.IsNullOrEmpty(objectId) || !_placedObjects.ContainsKey(objectId)) {
                return null;
            }
            
            return _placedObjects[objectId];
        }
        
        /// <summary>
        /// Clears all placed objects and placement data.
        /// </summary>
        public void ClearPlacedObjects() {
            foreach (var obj in _placedObjects.Values) {
                if (obj != null) {
                    Destroy(obj);
                }
            }
            
            ClearPlacementData();
            _lastPlacedObjects.Clear();
            _totalPlacedObjects = 0;
            _debugger.Log("Cleared all placed objects", LogCategory.Models);
        }
        
        /// <summary>
        /// Adds a new placement rule for adaptive placement.
        /// </summary>
        /// <param name="rule">The placement rule to add</param>
        public void AddPlacementRule(ObjectPlacementRule rule) {
            if (rule == null || string.IsNullOrEmpty(rule.objectTypePattern)) {
                _debugger.LogWarning("Cannot add invalid placement rule", LogCategory.Models);
                return;
            }
            
            _placementRules.Add(rule);
            _debugger.Log($"Added placement rule for '{rule.objectTypePattern}'", LogCategory.Models);
        }
        
        /// <summary>
        /// Gets the terrain height at the specified position.
        /// </summary>
        /// <param name="worldPosition">Position in world space</param>
        /// <returns>Terrain height at the position</returns>
        public float GetTerrainHeight(Vector3 worldPosition) {
            if (_currentTerrain == null) {
                return 0f;
            }
            
            return _currentTerrain.SampleHeight(worldPosition) + _currentTerrain.transform.position.y;
        }
        
        /// <summary>
        /// Gets the terrain normal at the specified position.
        /// </summary>
        /// <param name="worldPosition">Position in world space</param>
        /// <returns>Terrain normal at the position</returns>
        public Vector3 GetTerrainNormal(Vector3 worldPosition) {
            if (_currentTerrain == null) {
                return Vector3.up;
            }
            
            // Convert world position to normalized terrain coordinates
            Vector3 terrainPos = worldPosition - _currentTerrain.transform.position;
            float normalizedX = terrainPos.x / _terrainSize.x;
            float normalizedZ = terrainPos.z / _terrainSize.z;
            
            // Get terrain normal
            return _currentTerrain.terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
        }
        
        /// <summary>
        /// Gets the slope angle at the specified position.
        /// </summary>
        /// <param name="worldPosition">Position in world space</param>
        /// <returns>Slope angle in degrees (0 = flat, 90 = vertical)</returns>
        public float GetTerrainSlope(Vector3 worldPosition) {
            Vector3 normal = GetTerrainNormal(worldPosition);
            return Vector3.Angle(normal, Vector3.up);
        }
        
        /// <summary>
        /// Checks if a position is suitable for object placement.
        /// </summary>
        /// <param name="worldPosition">Position to check</param>
        /// <param name="objectSize">Size of the object to place</param>
        /// <param name="objectType">Type of object to place (for rule checking)</param>
        /// <returns>True if the position is suitable, false otherwise</returns>
        public bool IsPositionSuitable(Vector3 worldPosition, Vector3 objectSize, string objectType) {
            // Check if position is within terrain bounds
            if (!IsWithinTerrainBounds(worldPosition)) {
                return false;
            }
            
            // Check terrain slope
            float slope = GetTerrainSlope(worldPosition);
            float maxAllowedSlope = GetMaxAllowedSlope(objectType);
            
            if (slope > maxAllowedSlope) {
                return false;
            }
            
            // Check for collision with other objects
            if (_useColliderChecks && CheckForCollision(worldPosition, objectSize)) {
                return false;
            }
            
            // Check against adaptive placement rules
            if (_enableAdaptivePlacement && !CheckAdaptivePlacementRules(worldPosition, objectType)) {
                return false;
            }
            
            return true;
        }
        
        #endregion
        
        #region Private Methods
        
        /// <summary>
        /// Processes a placement task for a group of objects.
        /// </summary>
        private IEnumerator ProcessPlacementTask(
            PlacementTask task,
            IModelProvider modelProvider,
            Action<List<GameObject>, int> onTaskComplete
        ) {
            _debugger.Log($"Processing placement task for {task.Objects.Count} objects of type '{task.ObjectType}'", LogCategory.Models);
            
            List<GameObject> placedObjects = new List<GameObject>();
            List<MapObject> objectsToPlace = task.Objects;
            
            // Get or create prototype object
            GameObject prototypeObject = null;
            yield return StartCoroutine(
                modelProvider.GetModelForType(
                    task.ObjectType,
                    task.Objects.FirstOrDefault()?.enhancedDescription ?? task.ObjectType,
                    obj => prototypeObject = obj,
                    error => _debugger.LogError($"Failed to get model for '{task.ObjectType}': {error}", LogCategory.Models)
                )
            );
            
            if (prototypeObject == null) {
                _debugger.LogWarning($"Failed to get prototype object for '{task.ObjectType}'", LogCategory.Models);
                if (!_failedPlacements.ContainsKey(task.ObjectType)) {
                    _failedPlacements[task.ObjectType] = 0;
                }
                _failedPlacements[task.ObjectType] += objectsToPlace.Count;
                onTaskComplete?.Invoke(placedObjects, objectsToPlace.Count);
                yield break;
            }
            
            // Cache the prototype
            if (!_prototypeObjects.ContainsKey(task.ObjectType)) {
                _prototypeObjects[task.ObjectType] = prototypeObject;
                prototypeObject.SetActive(false);
            }
            
            // Set up instancing if enabled
            if (_useGPUInstancing && objectsToPlace.Count > 1) {
                SetupInstancingForType(task.ObjectType, prototypeObject);
            }
            
            // Determine placement approach based on object count
            bool useOptimizedPlacement = objectsToPlace.Count > 10;
            
            if (useOptimizedPlacement) {
                // For large groups, use optimized batch placement
                yield return StartCoroutine(
                    PlaceObjectsBatch(
                        task.ObjectType,
                        task.GroupId,
                        objectsToPlace,
                        prototypeObject,
                        placedObjects
                    )
                );
            } else {
                // For smaller groups, place objects individually for more precision
                foreach (var mapObject in objectsToPlace) {
                    yield return StartCoroutine(
                        PlaceSingleObject(
                            task.ObjectType,
                            mapObject,
                            prototypeObject,
                            obj => {
                                if (obj != null) {
                                    placedObjects.Add(obj);
                                }
                            }
                        )
                    );
                    
                    yield return null;
                }
            }
            
            onTaskComplete?.Invoke(placedObjects, objectsToPlace.Count);
        }
        
        /// <summary>
        /// Places a batch of objects efficiently.
        /// </summary>
        private IEnumerator PlaceObjectsBatch(
            string objectType,
            string groupId,
            List<MapObject> objects,
            GameObject prototype,
            List<GameObject> placedObjects
        ) {
            _debugger.Log($"Batch placing {objects.Count} objects of type '{objectType}'", LogCategory.Models);
            
            // Sort objects by placement priority
            objects.Sort((a, b) => CalculateObjectPriority(b).CompareTo(CalculateObjectPriority(a)));
            
            // Create container for instanced objects
            GameObject instanceContainer = null;
            if (_useGPUInstancing && objects.Count > 1) {
                instanceContainer = new GameObject($"Group_{objectType}_{groupId}");
                instanceContainer.transform.position = Vector3.zero;
            }
            
            // Place objects in batches
            int batchSize = Math.Min(20, objects.Count);
            int totalBatches = Mathf.CeilToInt((float)objects.Count / batchSize);
            
            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++) {
                int startIndex = batchIndex * batchSize;
                int endIndex = Mathf.Min(startIndex + batchSize, objects.Count);
                int batchCount = endIndex - startIndex;
                
                List<Task<GameObject>> placementTasks = new List<Task<GameObject>>();
                
                for (int i = startIndex; i < endIndex; i++) {
                    var mapObject = objects[i];
                    
                    // Create placement task
                    Task<GameObject> task = new Task<GameObject>(() => {
                        return TryPlaceObject(objectType, mapObject, prototype, instanceContainer);
                    });
                    
                    placementTasks.Add(task);
                    task.Start();
                    
                    // Slight delay to avoid CPU spikes
                    if (i % 5 == 0) {
                        yield return null;
                    }
                }
                
                // Wait for all tasks in batch to complete
                while (placementTasks.Any(t => !t.IsCompleted)) {
                    yield return null;
                }
                
                // Collect placed objects
                foreach (var task in placementTasks) {
                    if (task.Result != null) {
                        placedObjects.Add(task.Result);
                        _totalPlacedObjects++;
                    }
                }
                
                yield return null;
            }
            
            // Update instanced renderer if used
            if (instanceContainer != null && instanceContainer.transform.childCount > 0) {
                yield return StartCoroutine(OptimizeInstancedGroup(instanceContainer, objectType));
            }
        }
        
        /// <summary>
        /// Places a single object on the terrain.
        /// </summary>
        private IEnumerator PlaceSingleObject(
            string objectType,
            MapObject mapObject,
            GameObject prototype,
            Action<GameObject> onObjectPlaced
        ) {
            GameObject placedObject = null;
            
            try {
                // Calculate target position from normalized coordinates
                Vector3 targetPosition = ConvertToWorldPosition(mapObject.position);
                
                // Check if position is suitable
                Vector3 objectSize = prototype.GetComponent<Renderer>()?.bounds.size ?? Vector3.one;
                
                if (IsPositionSuitable(targetPosition, objectSize, objectType)) {
                    // Position is suitable, place object
                    placedObject = InstantiateObject(prototype, targetPosition, objectType);
                    
                    if (placedObject != null) {
                        // Apply object properties
                        string objectId = $"{objectType}_{mapObject.GetHashCode()}";
                        placedObject.name = $"{objectType}_{_totalPlacedObjects}";
                        
                        // Store object reference
                        _placedObjects[objectId] = placedObject;
                        
                        // Apply rotation and scale
                        ApplyTransformProperties(placedObject, mapObject, targetPosition);
                        
                        // Add to spatial grid
                        AddToSpatialGrid(placedObject, targetPosition);
                        
                        // Mark position as occupied
                        _occupiedPositions.Add(targetPosition);
                        
                        // Increment placed count
                        _totalPlacedObjects++;
                        
                        // Log success
                        _debugger.Log($"Placed object '{placedObject.name}' at {targetPosition}", LogCategory.Models);
                    } else {
                        // Failed to instantiate
                        _debugger.LogWarning($"Failed to instantiate object of type '{objectType}'", LogCategory.Models);
                        RecordFailedPlacement(objectType);
                    }
                } else {
                    // Original position not suitable, try alternative positions
                    yield return StartCoroutine(TryAlternativePositions(
                        objectType,
                        mapObject,
                        prototype,
                        obj => placedObject = obj
                    ));
                    
                    if (placedObject == null) {
                        // All placement attempts failed
                        _debugger.LogWarning($"Failed to find suitable position for object of type '{objectType}'", LogCategory.Models);
                        RecordFailedPlacement(objectType);
                    }
                }
            } catch (Exception ex) {
                _debugger.LogError($"Error placing object of type '{objectType}': {ex.Message}", LogCategory.Models);
                RecordFailedPlacement(objectType);
            }
            
            onObjectPlaced?.Invoke(placedObject);
        }
        
        /// <summary>
        /// Tries alternative positions for object placement.
        /// </summary>
        private IEnumerator TryAlternativePositions(
            string objectType,
            MapObject mapObject,
            GameObject prototype,
            Action<GameObject> onObjectPlaced
        ) {
            GameObject placedObject = null;
            Vector3 originalTargetPos = ConvertToWorldPosition(mapObject.position);
            Vector3 objectSize = prototype.GetComponent<Renderer>()?.bounds.size ?? Vector3.one;
            List<Vector3> alternativePositions = new List<Vector3>();
            
            // Add jittered positions around the original target
            for (int i = 0; i < _maxPlacementIterations; i++) {
                float radius = _placementJitter * (1f + i * 0.5f); // Increase radius for each iteration
                float angle = UnityEngine.Random.Range(0f, 360f);
                float distance = UnityEngine.Random.Range(0f, radius);
                
                Vector3 offset = new Vector3(
                    distance * Mathf.Cos(angle * Mathf.Deg2Rad),
                    0f,
                    distance * Mathf.Sin(angle * Mathf.Deg2Rad)
                );
                
                Vector3 alternativePos = originalTargetPos + offset;
                alternativePositions.Add(alternativePos);
                
                _totalPlacementAttempts++;
            }
            
            // Try positions in order of distance from original
            alternativePositions.Sort((a, b) => 
                Vector3.Distance(a, originalTargetPos).CompareTo(Vector3.Distance(b, originalTargetPos)));
            
            foreach (var pos in alternativePositions) {
                // Check if this position is suitable
                if (IsPositionSuitable(pos, objectSize, objectType)) {
                    // Position is suitable, place object
                    placedObject = InstantiateObject(prototype, pos, objectType);
                    
                    if (placedObject != null) {
                        // Apply object properties
                        string objectId = $"{objectType}_{mapObject.GetHashCode()}";
                        placedObject.name = $"{objectType}_{_totalPlacedObjects}";
                        
                        // Store object reference
                        _placedObjects[objectId] = placedObject;
                        
                        // Apply rotation and scale
                        ApplyTransformProperties(placedObject, mapObject, pos);
                        
                        // Add to spatial grid
                        AddToSpatialGrid(placedObject, pos);
                        
                        // Mark position as occupied
                        _occupiedPositions.Add(pos);
                        
                        // Increment placed count
                        _totalPlacedObjects++;
                        
                        // Log success
                        _debugger.Log($"Placed object '{placedObject.name}' at alternative position {pos}", LogCategory.Models);
                        
                        // Visualize successful placement
                        if (_showDebugVisualization) {
                            Debug.DrawLine(originalTargetPos, pos, Color.green, _debugVisualizationDuration);
                        }
                        
                        break;
                    }
                } else if (_showDebugVisualization) {
                    // Visualize failed attempt
                    Debug.DrawLine(originalTargetPos, pos, Color.red, _debugVisualizationDuration);
                }
                
                // Wait a frame to avoid blocking
                yield return null;
            }
            
            onObjectPlaced?.Invoke(placedObject);
        }
        
        /// <summary>
        /// Tries to place an object directly (used for batch placement).
        /// </summary>
        private GameObject TryPlaceObject(
            string objectType,
            MapObject mapObject,
            GameObject prototype,
            GameObject instanceContainer = null
        ) {
            _totalPlacementAttempts++;
            
            try {
                // Calculate target position from normalized coordinates
                Vector3 targetPosition = ConvertToWorldPosition(mapObject.position);
                
                // Check if position is suitable
                Vector3 objectSize = prototype.GetComponent<Renderer>()?.bounds.size ?? Vector3.one;
                
                if (IsPositionSuitable(targetPosition, objectSize, objectType)) {
                    // Position is suitable, place object
                    GameObject placedObject = InstantiateObject(prototype, targetPosition, objectType, instanceContainer);
                    
                    if (placedObject != null) {
                        // Apply object properties
                        string objectId = $"{objectType}_{mapObject.GetHashCode()}";
                        placedObject.name = $"{objectType}_{_totalPlacedObjects}";
                        
                        // Store object reference
                        _placedObjects[objectId] = placedObject;
                        
                        // Apply rotation and scale
                        ApplyTransformProperties(placedObject, mapObject, targetPosition);
                        
                        // Add to spatial grid
                        AddToSpatialGrid(placedObject, targetPosition);
                        
                        // Mark position as occupied
                        _occupiedPositions.Add(targetPosition);
                        
                        return placedObject;
                    }
                }
                
                // Try alternative positions
                for (int i = 0; i < _maxPlacementIterations; i++) {
                    _totalPlacementAttempts++;
                    
                    // Generate jittered position
                    float radius = _placementJitter * (1f + i * 0.5f); // Increase radius for each iteration
                    float angle = UnityEngine.Random.Range(0f, 360f);
                    float distance = UnityEngine.Random.Range(0f, radius);
                    
                    Vector3 offset = new Vector3(
                        distance * Mathf.Cos(angle * Mathf.Deg2Rad),
                        0f,
                        distance * Mathf.Sin(angle * Mathf.Deg2Rad)
                    );
                    
                    Vector3 alternativePos = targetPosition + offset;
                    
                    // Check if this position is suitable
                    if (IsPositionSuitable(alternativePos, objectSize, objectType)) {
                        // Position is suitable, place object
                        GameObject placedObject = InstantiateObject(prototype, alternativePos, objectType, instanceContainer);
                        
                        if (placedObject != null) {
                            // Apply object properties
                            string objectId = $"{objectType}_{mapObject.GetHashCode()}";
                            placedObject.name = $"{objectType}_{_totalPlacedObjects}";
                            
                            // Store object reference
                            _placedObjects[objectId] = placedObject;
                            
                            // Apply rotation and scale
                            ApplyTransformProperties(placedObject, mapObject, alternativePos);
                            
                            // Add to spatial grid
                            AddToSpatialGrid(placedObject, alternativePos);
                            
                            // Mark position as occupied
                            _occupiedPositions.Add(alternativePos);
                            
                            return placedObject;
                        }
                    }
                }
                
                // All placement attempts failed
                RecordFailedPlacement(objectType);
                return null;
            } catch (Exception ex) {
                _debugger.LogError($"Error placing object of type '{objectType}': {ex.Message}", LogCategory.Models);
                RecordFailedPlacement(objectType);
                return null;
            }
        }
        
        /// <summary>
        /// Instantiates an object at the specified position.
        /// </summary>
        private GameObject InstantiateObject(
            GameObject prototype,
            Vector3 position,
            string objectType,
            GameObject instanceContainer = null
        ) {
            GameObject instance = null;
            
            try {
                // Adjust position to terrain height
                position.y = GetTerrainHeight(position);
                
                if (instanceContainer != null && _useGPUInstancing) {
                    // Create instance as child of container
                    instance = Instantiate(prototype, instanceContainer.transform);
                    
                    // Apply instanced material if available
                    if (_instancedMaterials.TryGetValue(objectType, out Material instancedMaterial)) {
                        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                        foreach (var renderer in renderers) {
                            Material[] sharedMaterials = renderer.sharedMaterials;
                            for (int i = 0; i < sharedMaterials.Length; i++) {
                                sharedMaterials[i] = instancedMaterial;
                            }
                            renderer.sharedMaterials = sharedMaterials;
                        }
                    }
                } else {
                    // Create standalone instance
                    instance = Instantiate(prototype);
                }
                
                // Enable the instance
                instance.SetActive(true);
                
                // Set position
                instance.transform.position = position;
                
                return instance;
            } catch (Exception ex) {
                _debugger.LogError($"Error instantiating object: {ex.Message}", LogCategory.Models);
                
                if (instance != null) {
                    Destroy(instance);
                }
                
                return null;
            }
        }
        
        /// <summary>
        /// Applies rotation and scale to a placed object.
        /// </summary>
        private void ApplyTransformProperties(GameObject obj, MapObject mapObject, Vector3 position) {
            try {
                // Get terrain normal for alignment
                Vector3 terrainNormal = GetTerrainNormal(position);
                
                // Apply rotation
                float yRotation = mapObject.rotation;
                
                // Add jitter to rotation if enabled
                if (_rotationJitter > 0f) {
                    yRotation += UnityEngine.Random.Range(-_rotationJitter, _rotationJitter);
                }
                
                // Create rotation from terrain normal and Y-axis rotation
                Quaternion terrainAlignment = Quaternion.FromToRotation(Vector3.up, terrainNormal);
                Quaternion yAxisRotation = Quaternion.Euler(0f, yRotation, 0f);
                
                // Interpolate between upright and terrain-aligned based on alignment strength
                Quaternion targetRotation = Quaternion.Slerp(
                    yAxisRotation,
                    terrainAlignment * yAxisRotation,
                    _surfaceAlignmentStrength
                );
                
                obj.transform.rotation = targetRotation;
                
                // Apply scale
                Vector3 scale = mapObject.scale;
                
                // Add jitter to scale if enabled
                if (_scaleJitter > 0f) {
                    float scaleJitterValue = UnityEngine.Random.Range(-_scaleJitter, _scaleJitter);
                    scale *= (1f + scaleJitterValue);
                }
                
                obj.transform.localScale = scale;
                
                // Apply final position adjustments if needed
                // (e.g., sink object into ground based on mesh bounds)
                AdjustObjectPosition(obj);
            } catch (Exception ex) {
                _debugger.LogWarning($"Error applying transform properties: {ex.Message}", LogCategory.Models);
            }
        }
        
        /// <summary>
        /// Adjusts the final position of an object to ensure proper placement.
        /// </summary>
        private void AdjustObjectPosition(GameObject obj) {
            try {
                Renderer renderer = obj.GetComponent<Renderer>();
                if (renderer == null) {
                    renderer = obj.GetComponentInChildren<Renderer>();
                }
                
                if (renderer != null) {
                    Bounds bounds = renderer.bounds;
                    Vector3 position = obj.transform.position;
                    
                    // Adjust Y position to have object sit properly on terrain
                    // Calculate how much we need to sink the object based on its bounds
                    float sinkAmount = 0.05f * bounds.size.y; // Sink 5% of height by default
                    
                    // Check if object type has specific placement rule
                    float customSinkAmount = GetCustomSinkAmount(obj.name);
                    if (customSinkAmount >= 0f) {
                        sinkAmount = customSinkAmount * bounds.size.y;
                    }
                    
                    // Apply position adjustment
                    position.y -= sinkAmount;
                    obj.transform.position = position;
                }
            } catch (Exception ex) {
                _debugger.LogWarning($"Error adjusting object position: {ex.Message}", LogCategory.Models);
            }
        }
        
        /// <summary>
        /// Sets up instanced materials for a prototype object.
        /// </summary>
        private void SetupInstancingForType(string objectType, GameObject prototype) {
            if (!_useGPUInstancing || _instancedMaterials.ContainsKey(objectType)) {
                return;
            }
            
            try {
                // Get all renderers from prototype
                Renderer[] renderers = prototype.GetComponentsInChildren<Renderer>();
                foreach (var renderer in renderers) {
                    // Create instanced materials for each material
                    for (int i = 0; i < renderer.sharedMaterials.Length; i++) {
                        Material originalMat = renderer.sharedMaterials[i];
                        if (originalMat != null) {
                            // Create instanced material
                            Material instancedMat = new Material(originalMat);
                            instancedMat.enableInstancing = true;
                            
                            // Store instanced material
                            _instancedMaterials[objectType] = instancedMat;
                        }
                    }
                }
            } catch (Exception ex) {
                _debugger.LogWarning($"Error setting up instancing for '{objectType}': {ex.Message}", LogCategory.Models);
            }
        }
        
        /// <summary>
        /// Optimizes an instanced group by combining meshes or setting up instancing.
        /// </summary>
        private IEnumerator OptimizeInstancedGroup(GameObject container, string objectType) {
            if (container == null || container.transform.childCount == 0) {
                yield break;
            }
            
            _debugger.Log($"Optimizing instance group for '{objectType}' with {container.transform.childCount} objects", LogCategory.Models);
            
            try {
                // Check if we should use GPU instancing
                if (_useGPUInstancing && container.transform.childCount > 5) {
                    // Convert to GPU instancing
                    yield return null; // Give the system a frame to process
                    
                    // Get reference mesh and material from first child
                    Transform firstChild = container.transform.GetChild(0);
                    MeshFilter meshFilter = firstChild.GetComponent<MeshFilter>();
                    MeshRenderer meshRenderer = firstChild.GetComponent<MeshRenderer>();
                    
                    if (meshFilter != null && meshRenderer != null) {
                        // Collect transform matrices for all instances
                        Matrix4x4[] matrices = new Matrix4x4[container.transform.childCount];
                        for (int i = 0; i < container.transform.childCount; i++) {
                            matrices[i] = container.transform.GetChild(i).localToWorldMatrix;
                        }
                        
                        // Setup GPU instancing component
                        GPUInstancer instancer = container.AddComponent<GPUInstancer>();
                        instancer.mesh = meshFilter.sharedMesh;
                        instancer.material = _instancedMaterials.TryGetValue(objectType, out Material instancedMaterial) 
                            ? instancedMaterial 
                            : meshRenderer.sharedMaterial;
                        instancer.instanceMatrices = matrices;
                        
                        // Activate instancing
                        instancer.Initialize();
                        
                        // Disable original renderers to save draw calls
                        for (int i = 0; i < container.transform.childCount; i++) {
                            Transform child = container.transform.GetChild(i);
                            Renderer renderer = child.GetComponent<Renderer>();
                            if (renderer != null) {
                                renderer.enabled = false;
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                _debugger.LogWarning($"Error optimizing instance group: {ex.Message}", LogCategory.Models);
            }
        }
        
        /// <summary>
        /// Finalizes placements by applying batched operations.
        /// </summary>
        private IEnumerator FinalizePlacements() {
            // Apply any final optimization
            yield return null;
        }
        
        /// <summary>
        /// Converts normalized position to world position on terrain.
        /// </summary>
        private Vector3 ConvertToWorldPosition(Vector2 normalizedPosition) {
            if (_currentTerrain == null) {
                return Vector3.zero;
            }
            
            float worldX = normalizedPosition.x * _terrainSize.x;
            float worldZ = normalizedPosition.y * _terrainSize.z;
            
            Vector3 worldPosition = _currentTerrain.transform.position + new Vector3(worldX, 0f, worldZ);
            worldPosition.y = GetTerrainHeight(worldPosition);
            
            return worldPosition;
        }
        
        /// <summary>
        /// Checks if a position is within terrain bounds.
        /// </summary>
        private bool IsWithinTerrainBounds(Vector3 worldPosition) {
            if (_currentTerrain == null) {
                return false;
            }
            
            Vector3 terrainPos = _currentTerrain.transform.position;
            
            return worldPosition.x >= terrainPos.x && worldPosition.x <= terrainPos.x + _terrainSize.x &&
                   worldPosition.z >= terrainPos.z && worldPosition.z <= terrainPos.z + _terrainSize.z;
        }
        
        /// <summary>
        /// Checks for collision with other objects.
        /// </summary>
        private bool CheckForCollision(Vector3 position, Vector3 objectSize) {
            // Check for nearby objects in spatial grid
            Vector3Int gridCell = GetGridCell(position);
            float checkRadius = Mathf.Max(objectSize.x, objectSize.z) * _objectSpacingFactor * 0.5f;
            
            // Check current cell and adjacent cells
            for (int xOffset = -1; xOffset <= 1; xOffset++) {
                for (int zOffset = -1; zOffset <= 1; zOffset++) {
                    Vector3Int neighborCell = new Vector3Int(
                        gridCell.x + xOffset,
                        gridCell.y,
                        gridCell.z + zOffset
                    );
                    
                    if (_spatialGrid.TryGetValue(neighborCell, out List<GameObject> cellObjects)) {
                        foreach (var obj in cellObjects) {
                            if (obj == null) continue;
                            
                            float distance = Vector3.Distance(position, obj.transform.position);
                            if (distance < checkRadius) {
                                return true; // Collision detected
                            }
                        }
                    }
                }
            }
            
            // Check for collision using physics if more precise check is needed
            if (_useColliderChecks) {
                Collider[] colliders = Physics.OverlapSphere(position, checkRadius, _collisionLayers);
                return colliders.Length > 0;
            }
            
            return false;
        }
        
        /// <summary>
        /// Checks if a position satisfies adaptive placement rules.
        /// </summary>
        private bool CheckAdaptivePlacementRules(Vector3 position, string objectType) {
            if (!_enableAdaptivePlacement || _placementRules.Count == 0) {
                return true;
            }
            
            // Find applicable rules
            List<ObjectPlacementRule> applicableRules = _placementRules
                .Where(r => objectType.Contains(r.objectTypePattern) || 
                           (r.objectTypePattern.StartsWith("*") && true)) // Wildcard rule
                .ToList();
            
            if (applicableRules.Count == 0) {
                return true; // No rules, so placement is allowed
            }
            
            // Check all applicable rules
            foreach (var rule in applicableRules) {
                // Check terrain slope
                float slope = GetTerrainSlope(position);
                if (slope < rule.minSlopeAngle || slope > rule.maxSlopeAngle) {
                    return false;
                }
                
                // Check proximity to water (if required)
                if (rule.waterProximityCheck != WaterProximityCheck.Ignore) {
                    bool isNearWater = IsNearWater(position, rule.waterProximityDistance);
                    
                    if (rule.waterProximityCheck == WaterProximityCheck.MustBeNear && !isNearWater) {
                        return false;
                    }
                    
                    if (rule.waterProximityCheck == WaterProximityCheck.MustNotBeNear && isNearWater) {
                        return false;
                    }
                }
                
                // Check height restrictions
                float height = position.y - _currentTerrain.transform.position.y;
                float normalizedHeight = height / _terrainHeight;
                
                if (normalizedHeight < rule.minRelativeHeight || normalizedHeight > rule.maxRelativeHeight) {
                    return false;
                }
                
                // Check for collisions with other objects of same type (for spacing)
                if (rule.minSpacingToSameType > 0f) {
                    if (HasNearbyObjectsOfType(position, objectType, rule.minSpacingToSameType)) {
                        return false;
                    }
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Checks if a position is near water.
        /// </summary>
        private bool IsNearWater(Vector3 position, float maxDistance) {
            // This is a simplified example - in a real implementation,
            // you would check against actual water features in the terrain.
            // For now, we'll just check if position is below a certain height.
            
            float waterHeight = _terrainHeight * 0.3f; // Assuming water is at 30% of terrain height
            return position.y < waterHeight + maxDistance;
        }
        
        /// <summary>
        /// Checks if there are nearby objects of the same type.
        /// </summary>
        private bool HasNearbyObjectsOfType(Vector3 position, string objectType, float maxDistance) {
            Vector3Int gridCell = GetGridCell(position);
            
            // Check current cell and adjacent cells
            for (int xOffset = -1; xOffset <= 1; xOffset++) {
                for (int zOffset = -1; zOffset <= 1; zOffset++) {
                    Vector3Int neighborCell = new Vector3Int(
                        gridCell.x + xOffset,
                        gridCell.y,
                        gridCell.z + zOffset
                    );
                    
                    if (_spatialGrid.TryGetValue(neighborCell, out List<GameObject> cellObjects)) {
                        foreach (var obj in cellObjects) {
                            if (obj == null) continue;
                            
                            if (obj.name.StartsWith(objectType)) {
                                float distance = Vector3.Distance(position, obj.transform.position);
                                if (distance < maxDistance) {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the maximum allowed slope for an object type.
        /// </summary>
        private float GetMaxAllowedSlope(string objectType) {
            if (!_enableAdaptivePlacement || _placementRules.Count == 0) {
                return _maxSlopeAngle;
            }
            
            // Find applicable rules
            var applicableRules = _placementRules
                .Where(r => objectType.Contains(r.objectTypePattern) || 
                           (r.objectTypePattern.StartsWith("*") && true)) // Wildcard rule
                .ToList();
            
            if (applicableRules.Count == 0) {
                return _maxSlopeAngle;
            }
            
            // Return the most restrictive max slope
            return applicableRules.Min(r => r.maxSlopeAngle);
        }
        
        /// <summary>
        /// Gets the custom sink amount for an object type.
        /// </summary>
        private float GetCustomSinkAmount(string objectName) {
            if (!_enableAdaptivePlacement || _placementRules.Count == 0) {
                return -1f;
            }
            
            // Find applicable rules
            var applicableRules = _placementRules
                .Where(r => objectName.Contains(r.objectTypePattern) || 
                           (r.objectTypePattern.StartsWith("*") && true)) // Wildcard rule
                .ToList();
            
            if (applicableRules.Count == 0) {
                return -1f;
            }
            
            // Return the first matching rule's sink amount
            return applicableRules[0].sinkIntoGroundAmount;
        }
        
        /// <summary>
        /// Gets the spatial grid cell for a world position.
        /// </summary>
        private Vector3Int GetGridCell(Vector3 worldPosition) {
            return new Vector3Int(
                Mathf.FloorToInt(worldPosition.x / GRID_CELL_SIZE),
                Mathf.FloorToInt(worldPosition.y / GRID_CELL_SIZE),
                Mathf.FloorToInt(worldPosition.z / GRID_CELL_SIZE)
            );
        }
        
        /// <summary>
        /// Adds an object to the spatial grid.
        /// </summary>
        private void AddToSpatialGrid(GameObject obj, Vector3 position) {
            Vector3Int cell = GetGridCell(position);
            
            if (!_spatialGrid.ContainsKey(cell)) {
                _spatialGrid[cell] = new List<GameObject>();
            }
            
            _spatialGrid[cell].Add(obj);
        }
        
        /// <summary>
        /// Records a failed placement for statistics.
        /// </summary>
        private void RecordFailedPlacement(string objectType) {
            if (!_failedPlacements.ContainsKey(objectType)) {
                _failedPlacements[objectType] = 0;
            }
            
            _failedPlacements[objectType]++;
        }
        
        /// <summary>
        /// Calculates priority for group placement.
        /// </summary>
        private int CalculateGroupPriority(ObjectGroup group) {
            if (group == null || group.objects == null || group.objects.Count == 0) {
                return 0;
            }
            
            // Base priority on size of group and average confidence
            float avgConfidence = group.objects.Average(o => o.confidence);
            return Mathf.RoundToInt(group.objects.Count * avgConfidence * 10f);
        }
        
        /// <summary>
        /// Calculates priority for individual object placement.
        /// </summary>
        private float CalculateObjectPriority(MapObject obj) {
            if (obj == null) {
                return 0f;
            }
            
            // Base priority on confidence and size
            return obj.confidence * (obj.boundingBox.width * obj.boundingBox.height);
        }
        
        /// <summary>
        /// Clears all placement data.
        /// </summary>
        private void ClearPlacementData() {
            _placedObjects.Clear();
            _occupiedPositions.Clear();
            _placementAttempts.Clear();
            _spatialGrid.Clear();
            _failedPlacements.Clear();
            
            // Clean up instanced materials
            foreach (var material in _instancedMaterials.Values) {
                if (material != null) {
                    Destroy(material);
                }
            }
            _instancedMaterials.Clear();
            
            // Clean up prototypes
            foreach (var prototype in _prototypeObjects.Values) {
                if (prototype != null) {
                    Destroy(prototype);
                }
            }
            _prototypeObjects.Clear();
        }
        
        #endregion
        
        #region Utility Classes
        
        /// <summary>
        /// Represents a task for processing a batch of object placements.
        /// </summary>
        private class PlacementTask {
            public string ObjectType { get; set; }
            public string GroupId { get; set; }
            public List<MapObject> Objects { get; set; }
            public int Priority { get; set; }
        }
        
        /// <summary>
        /// Represents a single placement attempt.
        /// </summary>
        private class PlacementAttempt {
            public Vector3 Position { get; set; }
            public bool Success { get; set; }
            public string FailureReason { get; set; }
        }
        
        /// <summary>
        /// Represents a simple task with result.
        /// </summary>
        private class Task<T> {
            private Func<T> _action;
            private T _result;
            private bool _isCompleted;
            private Exception _exception;
            
            public bool IsCompleted => _isCompleted;
            public T Result => _result;
            public Exception Exception => _exception;
            
            public Task(Func<T> action) {
                _action = action;
                _isCompleted = false;
            }
            
            public void Start() {
                try {
                    _result = _action();
                    _isCompleted = true;
                } catch (Exception ex) {
                    _exception = ex;
                    _isCompleted = true;
                }
            }
        }
        
        /// <summary>
        /// Provides GPU instancing functionality.
        /// </summary>
        private class GPUInstancer : MonoBehaviour {
            public Mesh mesh;
            public Material material;
            public Matrix4x4[] instanceMatrices;
            
            private bool _initialized;
            
            public void Initialize() {
                _initialized = true;
            }
            
            private void Update() {
                if (_initialized && mesh != null && material != null && instanceMatrices != null) {
                    // Draw all instances in a single draw call
                    Graphics.DrawMeshInstanced(mesh, 0, material, instanceMatrices);
                }
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Interface for model providers used by ObjectPlacer.
    /// </summary>
    public interface IModelProvider {
        /// <summary>
        /// Gets a model for the specified type.
        /// </summary>
        /// <param name="objectType">Type of object to get model for</param>
        /// <param name="description">Optional description for model generation</param>
        /// <param name="onComplete">Callback when model is ready</param>
        /// <param name="onError">Callback for errors</param>
        /// <returns>Coroutine for tracking progress</returns>
        IEnumerator GetModelForType(
            string objectType,
            string description,
            Action<GameObject> onComplete,
            Action<string> onError
        );
    }
    
    /// <summary>
    /// Defines rules for adaptive object placement.
    /// </summary>
    [Serializable]
    public class ObjectPlacementRule {
        [Tooltip("Object type pattern to match (supports wildcards with *)")]
        public string objectTypePattern;
        
        [Header("Terrain Rules")]
        [Tooltip("Minimum slope angle in degrees")]
        [Range(0f, 90f)]
        public float minSlopeAngle = 0f;
        
        [Tooltip("Maximum slope angle in degrees")]
        [Range(0f, 90f)]
        public float maxSlopeAngle = 30f;
        
        [Tooltip("Minimum relative height (0-1)")]
        [Range(0f, 1f)]
        public float minRelativeHeight = 0f;
        
        [Tooltip("Maximum relative height (0-1)")]
        [Range(0f, 1f)]
        public float maxRelativeHeight = 1f;
        
        [Header("Spacing Rules")]
        [Tooltip("Minimum spacing to objects of same type")]
        [Range(0f, 50f)]
        public float minSpacingToSameType = 2f;
        
        [Tooltip("Minimum spacing to objects of different type")]
        [Range(0f, 50f)]
        public float minSpacingToDifferentType = 0f;
        
        [Header("Water Rules")]
        [Tooltip("Water proximity check")]
        public WaterProximityCheck waterProximityCheck = WaterProximityCheck.Ignore;
        
        [Tooltip("Distance for water proximity check")]
        [Range(0f, 100f)]
        public float waterProximityDistance = 5f;
        
        [Header("Placement Adjustments")]
        [Tooltip("How much to sink the object into the ground (as % of object height)")]
        [Range(0f, 1f)]
        public float sinkIntoGroundAmount = 0.05f;
        
        [Tooltip("Random rotation range in degrees")]
        [Range(0f, 360f)]
        public float randomRotationRange = 15f;
        
        [Tooltip("Random scale range (as % of original scale)")]
        [Range(0f, 0.5f)]
        public float randomScaleRange = 0.1f;
    }
    
    /// <summary>
    /// Defines water proximity check modes.
    /// </summary>
    public enum WaterProximityCheck {
        Ignore,
        MustBeNear,
        MustNotBeNear
    }
    
    /// <summary>
    /// Extensions for coroutines.
    /// </summary>
    public static class CoroutineExtensions {
        /// <summary>
        /// Checks if a coroutine is completed.
        /// </summary>
        public static bool IsCompleted(this Coroutine coroutine) {
            return coroutine == null;
        }
    }
}
