/*************************************************************************
 *  Traversify – ObjectPlacer.cs                                         *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-07-04                                                  *
 *  Desc   : Handles precise placement, orientation, and scaling of      *
 *           objects on terrain using transforms with intelligent        *
 *           collision avoidance, terrain adaptation, and natural        *
 *           positioning for realistic environment generation.           *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;
using Traversify.AI;

namespace Traversify.Terrain {
    /// <summary>
    /// Manages the precise placement, orientation, and scaling of objects on terrain
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
        
        [Header("Placement Settings")]
        [Tooltip("Surface alignment strength (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float _surfaceAlignmentStrength = 0.8f;
        
        [Tooltip("Maximum allowed surface slope angle in degrees")]
        [Range(0f, 90f)]
        [SerializeField] private float _maxSlopeAngle = 45f;
        
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
        
        [Header("Performance Settings")]
        [Tooltip("Maximum concurrent placement operations")]
        [Range(1, 50)]
        [SerializeField] private int _maxConcurrentPlacements = 10;
        
        [Tooltip("Use multithreading for placement calculations")]
        [SerializeField] private bool _useMultithreading = true;
        
        [Tooltip("Placement timeout in seconds")]
        [SerializeField] private float _placementTimeout = 30f;
        
        #endregion
        
        #region Private Fields
        
        private TraversifyDebugger _debugger;
        private UnityEngine.Terrain _terrain;
        private TerrainData _terrainData;
        private List<PlacedObject> _placedObjects = new List<PlacedObject>();
        private bool _isPlacing = false;
        private int _activePlacements = 0;
        
        // Spatial grid for efficient collision detection
        private Dictionary<Vector2Int, List<PlacedObject>> _spatialGrid = new Dictionary<Vector2Int, List<PlacedObject>>();
        private float _gridCellSize = 10f;
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Represents placement data for an object.
        /// </summary>
        [System.Serializable]
        public class PlacementData
        {
            public GameObject prefab;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool adaptToTerrain;
            public bool avoidCollisions;
            public float objectSpacing;
            public float maxSlope;
            public float groundingDepth;
            public LayerMask collisionLayers = -1;
            public string objectType;
            public float confidence;
        }
        
        /// <summary>
        /// Represents a placed object with metadata.
        /// </summary>
        [System.Serializable]
        public class PlacedObject
        {
            public GameObject gameObject;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public Bounds bounds;
            public string objectType;
            public float confidence;
            public DateTime placementTime;
        }
        
        /// <summary>
        /// Represents the result of object placement.
        /// </summary>
        [System.Serializable]
        public class PlacementResult
        {
            public GameObject placedObject;
            public Vector3 finalPosition;
            public Quaternion finalRotation;
            public Vector3 finalScale;
            public bool success;
            public string errorMessage;
            public float placementTime;
        }
        
        #endregion
        
        #region TraversifyComponent Implementation
        
        protected override bool OnInitialize(object config)
        {
            try
            {
                _debugger = GetComponent<TraversifyDebugger>();
                if (_debugger == null)
                {
                    _debugger = gameObject.AddComponent<TraversifyDebugger>();
                }
                
                // Find terrain in scene
                _terrain = FindObjectOfType<UnityEngine.Terrain>();
                if (_terrain != null)
                {
                    _terrainData = _terrain.terrainData;
                }
                
                InitializeSpatialGrid();
                
                Log("ObjectPlacer initialized successfully", LogCategory.System);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to initialize ObjectPlacer: {ex.Message}", LogCategory.System);
                return false;
            }
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }
        
        #endregion
        
        #region Spatial Grid Management
        
        /// <summary>
        /// Initialize spatial grid for efficient collision detection.
        /// </summary>
        private void InitializeSpatialGrid()
        {
            _spatialGrid.Clear();
            
            if (_terrain != null)
            {
                _gridCellSize = Mathf.Max(10f, _terrain.terrainData.size.x / 100f);
            }
            
            Log("Spatial grid initialized", LogCategory.System);
        }
        
        /// <summary>
        /// Add object to spatial grid.
        /// </summary>
        private void AddToSpatialGrid(PlacedObject placedObject)
        {
            var gridPos = WorldToGridPosition(placedObject.position);
            
            if (!_spatialGrid.ContainsKey(gridPos))
            {
                _spatialGrid[gridPos] = new List<PlacedObject>();
            }
            
            _spatialGrid[gridPos].Add(placedObject);
        }
        
        /// <summary>
        /// Convert world position to grid position.
        /// </summary>
        private Vector2Int WorldToGridPosition(Vector3 worldPosition)
        {
            return new Vector2Int(
                Mathf.FloorToInt(worldPosition.x / _gridCellSize),
                Mathf.FloorToInt(worldPosition.z / _gridCellSize)
            );
        }
        
        /// <summary>
        /// Get nearby objects for collision checking.
        /// </summary>
        private List<PlacedObject> GetNearbyObjects(Vector3 position, float radius)
        {
            var nearbyObjects = new List<PlacedObject>();
            var centerGrid = WorldToGridPosition(position);
            int gridRadius = Mathf.CeilToInt(radius / _gridCellSize);
            
            for (int x = centerGrid.x - gridRadius; x <= centerGrid.x + gridRadius; x++)
            {
                for (int y = centerGrid.y - gridRadius; y <= centerGrid.y + gridRadius; y++)
                {
                    var gridPos = new Vector2Int(x, y);
                    if (_spatialGrid.ContainsKey(gridPos))
                    {
                        nearbyObjects.AddRange(_spatialGrid[gridPos]);
                    }
                }
            }
            
            return nearbyObjects;
        }
        
        #endregion
        
        #region Main Placement Methods
        
        /// <summary>
        /// Place a single object using the provided placement data.
        /// </summary>
        public IEnumerator PlaceObject(PlacementData placementData,
            System.Action<PlacementResult> onComplete = null,
            System.Action<string> onError = null)
        {
            if (placementData == null || placementData.prefab == null)
            {
                onError?.Invoke("Invalid placement data or prefab");
                yield break;
            }

            yield return StartCoroutine(PlaceObjectInternal(placementData, onComplete, onError));
        }

        /// <summary>
        /// Internal method for placing a single object without yield in try-catch.
        /// </summary>
        private IEnumerator PlaceObjectInternal(PlacementData placementData,
            System.Action<PlacementResult> onComplete = null,
            System.Action<string> onError = null)
        {
            _activePlacements++;
            
            PlacementResult result = null;
            string errorMessage = null;
            
            try
            {
                float startTime = Time.time;
                
                // Find optimal placement position
                var optimalPosition = FindOptimalPlacement(placementData);
                
                if (optimalPosition.HasValue)
                {
                    // Create and place object
                    var placedObject = CreateAndPlaceObject(placementData, optimalPosition.Value);
                    
                    if (placedObject != null)
                    {
                        result = new PlacementResult
                        {
                            placedObject = placedObject.gameObject,
                            finalPosition = placedObject.position,
                            finalRotation = placedObject.rotation,
                            finalScale = placedObject.scale,
                            success = true,
                            placementTime = Time.time - startTime
                        };
                        
                        Log($"Successfully placed {placementData.objectType} at {placedObject.position}", LogCategory.Terrain);
                    }
                    else
                    {
                        errorMessage = "Failed to create object";
                    }
                }
                else
                {
                    errorMessage = "Could not find suitable placement location";
                }
            }
            catch (Exception ex)
            {
                LogError($"Object placement failed: {ex.Message}", LogCategory.Terrain);
                errorMessage = $"Object placement failed: {ex.Message}";
            }
            finally
            {
                _activePlacements--;
            }
            
            // Handle callbacks after try-catch
            if (result != null)
            {
                onComplete?.Invoke(result);
            }
            else if (errorMessage != null)
            {
                onError?.Invoke(errorMessage);
            }
            
            yield return null;
        }
        
        /// <summary>
        /// Place multiple objects efficiently.
        /// </summary>
        public IEnumerator PlaceObjects(List<PlacementData> placementDataList,
            System.Action<List<PlacementResult>> onComplete = null,
            System.Action<string> onError = null,
            System.Action<string, float> onProgress = null)
        {
            if (placementDataList == null || placementDataList.Count == 0)
            {
                onError?.Invoke("No placement data provided");
                yield break;
            }

            yield return StartCoroutine(PlaceObjectsInternal(placementDataList, onComplete, onError, onProgress));
        }

        /// <summary>
        /// Internal method for placing multiple objects without yield in try-catch.
        /// </summary>
        private IEnumerator PlaceObjectsInternal(List<PlacementData> placementDataList,
            System.Action<List<PlacementResult>> onComplete = null,
            System.Action<string> onError = null,
            System.Action<string, float> onProgress = null)
        {
            _isPlacing = true;
            var results = new List<PlacementResult>();
            string errorMessage = null;
            bool hasError = false;
            
            // Execute placement logic without try-catch around yield
            yield return StartCoroutine(ExecutePlacementLogic(placementDataList, results, onProgress,
                (error) => { hasError = true; errorMessage = error; }));
            
            // Handle final cleanup and callbacks
            _isPlacing = false;
            
            if (hasError)
            {
                onError?.Invoke(errorMessage);
            }
            else
            {
                onComplete?.Invoke(results);
            }
        }
        
        /// <summary>
        /// Execute the placement logic without try-catch around yield statements.
        /// </summary>
        private IEnumerator ExecutePlacementLogic(List<PlacementData> placementDataList, 
            List<PlacementResult> results, 
            System.Action<string, float> onProgress,
            System.Action<string> onError)
        {
            Exception caughtException = null;
            
            try
            {
                Log($"Starting placement of {placementDataList.Count} objects", LogCategory.Terrain);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
            
            if (caughtException != null)
            {
                onError?.Invoke($"Failed to start placement: {caughtException.Message}");
                yield break;
            }
            
            for (int i = 0; i < placementDataList.Count; i++)
            {
                var placementData = placementDataList[i];
                bool placementComplete = false;
                PlacementResult result = null;
                
                // Start placement coroutine
                yield return StartCoroutine(PlaceObjectInternal(placementData,
                    (res) => {
                        result = res;
                        placementComplete = true;
                    },
                    (error) => {
                        result = new PlacementResult
                        {
                            success = false,
                            errorMessage = error
                        };
                        placementComplete = true;
                    }));
                
                // Wait for completion
                while (!placementComplete)
                {
                    yield return null;
                }
                
                if (result != null)
                {
                    results.Add(result);
                }
                
                // Update progress
                float progress = (float)(i + 1) / placementDataList.Count;
                onProgress?.Invoke($"Placed {i + 1}/{placementDataList.Count} objects", progress);
                
                // Wait for concurrent placement limit
                while (_activePlacements >= _maxConcurrentPlacements)
                {
                    yield return null;
                }
            }
            
            // Wait for all placements to complete
            while (_activePlacements > 0)
            {
                yield return null;
            }
            
            try
            {
                Log($"Completed placement of {results.Count} objects", LogCategory.Terrain);
            }
            catch (Exception ex)
            {
                LogError($"Error during placement completion logging: {ex.Message}", LogCategory.Terrain);
            }
        }
        
        #endregion
        
        #region Placement Logic
        
        /// <summary>
        /// Find optimal placement position for an object.
        /// </summary>
        private Vector3? FindOptimalPlacement(PlacementData placementData)
        {
            Vector3 targetPosition = placementData.position;
            
            for (int attempt = 0; attempt < _maxPlacementIterations; attempt++)
            {
                // Add jitter to placement position
                Vector3 testPosition = targetPosition + new Vector3(
                    UnityEngine.Random.Range(-_placementJitter, _placementJitter),
                    0f,
                    UnityEngine.Random.Range(-_placementJitter, _placementJitter)
                );
                
                // Get terrain height and normal at test position
                if (GetTerrainInfoAtPosition(testPosition, out Vector3 terrainPosition, out Vector3 terrainNormal))
                {
                    // Check slope
                    float slope = Vector3.Angle(Vector3.up, terrainNormal);
                    if (slope <= placementData.maxSlope)
                    {
                        // Check collisions if enabled
                        if (!placementData.avoidCollisions || !HasCollisions(terrainPosition, placementData))
                        {
                            // Apply grounding depth
                            terrainPosition.y -= placementData.groundingDepth;
                            return terrainPosition;
                        }
                    }
                }
            }
            
            return null; // Could not find suitable placement
        }
        
        /// <summary>
        /// Get terrain information at a specific position.
        /// </summary>
        private bool GetTerrainInfoAtPosition(Vector3 worldPosition, out Vector3 terrainPosition, out Vector3 terrainNormal)
        {
            terrainPosition = Vector3.zero;
            terrainNormal = Vector3.up;
            
            if (_terrain == null)
            {
                // Fallback to raycast
                if (Physics.Raycast(worldPosition + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f))
                {
                    terrainPosition = hit.point;
                    terrainNormal = hit.normal;
                    return true;
                }
                return false;
            }
            
            // Get terrain-relative position
            Vector3 terrainLocalPos = worldPosition - _terrain.transform.position;
            Vector3 terrainSize = _terrainData.size;
            
            // Normalize to 0-1 range
            float normalizedX = Mathf.Clamp01(terrainLocalPos.x / terrainSize.x);
            float normalizedZ = Mathf.Clamp01(terrainLocalPos.z / terrainSize.z);
            
            // Sample terrain height
            float terrainHeight = _terrain.SampleHeight(worldPosition);
            terrainPosition = new Vector3(worldPosition.x, terrainHeight, worldPosition.z);
            
            // Calculate terrain normal
            terrainNormal = _terrainData.GetInterpolatedNormal(normalizedX, normalizedZ);
            
            return true;
        }
        
        /// <summary>
        /// Check for collisions at placement position.
        /// </summary>
        private bool HasCollisions(Vector3 position, PlacementData placementData)
        {
            // Get object bounds estimate
            Bounds objectBounds = GetObjectBounds(placementData.prefab, placementData.scale);
            
            // Check against nearby placed objects
            var nearbyObjects = GetNearbyObjects(position, objectBounds.size.magnitude * _objectSpacingFactor);
            
            foreach (var nearbyObject in nearbyObjects)
            {
                float distance = Vector3.Distance(position, nearbyObject.position);
                float requiredSpacing = (objectBounds.size.magnitude + nearbyObject.bounds.size.magnitude) * 0.5f * _objectSpacingFactor;
                
                if (distance < requiredSpacing)
                {
                    return true; // Collision detected
                }
            }
            
            // Check against existing colliders
            Collider[] overlapping = Physics.OverlapBox(
                position + objectBounds.center,
                objectBounds.extents,
                Quaternion.identity,
                placementData.collisionLayers
            );
            
            return overlapping.Length > 0;
        }
        
        /// <summary>
        /// Get estimated bounds for a prefab.
        /// </summary>
        private Bounds GetObjectBounds(GameObject prefab, Vector3 scale)
        {
            var renderer = prefab.GetComponent<Renderer>();
            if (renderer != null)
            {
                var bounds = renderer.bounds;
                bounds.size = Vector3.Scale(bounds.size, scale);
                return bounds;
            }
            
            var collider = prefab.GetComponent<Collider>();
            if (collider != null)
            {
                var bounds = collider.bounds;
                bounds.size = Vector3.Scale(bounds.size, scale);
                return bounds;
            }
            
            // Default bounds
            return new Bounds(Vector3.zero, Vector3.Scale(Vector3.one, scale));
        }
        
        #endregion
        
        #region Object Creation
        
        /// <summary>
        /// Create and place object with proper transforms.
        /// </summary>
        private PlacedObject CreateAndPlaceObject(PlacementData placementData, Vector3 position)
        {
            try
            {
                // Instantiate object
                GameObject obj = Instantiate(placementData.prefab);
                obj.name = $"{placementData.objectType}_{_placedObjects.Count}";
                
                // Calculate final transforms
                Vector3 finalPosition = position;
                Quaternion finalRotation = CalculateFinalRotation(placementData, position);
                Vector3 finalScale = CalculateFinalScale(placementData.scale);
                
                // Apply transforms
                obj.transform.position = finalPosition;
                obj.transform.rotation = finalRotation;
                obj.transform.localScale = finalScale;
                
                // Create placed object record
                var placedObject = new PlacedObject
                {
                    gameObject = obj,
                    position = finalPosition,
                    rotation = finalRotation,
                    scale = finalScale,
                    bounds = GetObjectBounds(obj, finalScale),
                    objectType = placementData.objectType,
                    confidence = placementData.confidence,
                    placementTime = DateTime.Now
                };
                
                // Add to tracking lists
                _placedObjects.Add(placedObject);
                AddToSpatialGrid(placedObject);
                
                return placedObject;
            }
            catch (Exception ex)
            {
                LogError($"Failed to create and place object: {ex.Message}", LogCategory.Terrain);
                return null;
            }
        }
        
        /// <summary>
        /// Calculate final rotation with terrain adaptation and jitter.
        /// </summary>
        private Quaternion CalculateFinalRotation(PlacementData placementData, Vector3 position)
        {
            Quaternion finalRotation = placementData.rotation;
            
            // Apply terrain adaptation if enabled
            if (placementData.adaptToTerrain && GetTerrainInfoAtPosition(position, out Vector3 terrainPos, out Vector3 terrainNormal))
            {
                // Calculate terrain-aligned rotation
                Vector3 forward = Vector3.ProjectOnPlane(finalRotation * Vector3.forward, terrainNormal).normalized;
                Quaternion terrainRotation = Quaternion.LookRotation(forward, terrainNormal);
                
                // Blend with original rotation
                finalRotation = Quaternion.Slerp(finalRotation, terrainRotation, _surfaceAlignmentStrength);
            }
            
            // Apply rotation jitter
            if (_rotationJitter > 0f)
            {
                float jitterAngle = UnityEngine.Random.Range(-_rotationJitter, _rotationJitter);
                finalRotation *= Quaternion.AngleAxis(jitterAngle, Vector3.up);
            }
            
            return finalRotation;
        }
        
        /// <summary>
        /// Calculate final scale with jitter.
        /// </summary>
        private Vector3 CalculateFinalScale(Vector3 baseScale)
        {
            Vector3 finalScale = baseScale;
            
            // Apply scale jitter
            if (_scaleJitter > 0f)
            {
                float jitterFactor = 1f + UnityEngine.Random.Range(-_scaleJitter, _scaleJitter);
                finalScale *= jitterFactor;
            }
            
            return finalScale;
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Clear all placed objects.
        /// </summary>
        public void ClearAllPlacedObjects()
        {
            foreach (var placedObject in _placedObjects)
            {
                if (placedObject.gameObject != null)
                {
                    DestroyImmediate(placedObject.gameObject);
                }
            }
            
            _placedObjects.Clear();
            _spatialGrid.Clear();
            
            Log("Cleared all placed objects", LogCategory.Terrain);
        }
        
        /// <summary>
        /// Get statistics about placed objects.
        /// </summary>
        public Dictionary<string, int> GetPlacementStatistics()
        {
            var stats = new Dictionary<string, int>();
            
            foreach (var placedObject in _placedObjects)
            {
                if (stats.ContainsKey(placedObject.objectType))
                {
                    stats[placedObject.objectType]++;
                }
                else
                {
                    stats[placedObject.objectType] = 1;
                }
            }
            
            return stats;
        }
        
        /// <summary>
        /// Log message using the debugger component.
        /// </summary>
        private void Log(string message, LogCategory category)
        {
            _debugger?.Log(message, category);
        }
        
        /// <summary>
        /// Log error message using the debugger component.
        /// </summary>
        private void LogError(string message, LogCategory category)
        {
            _debugger?.LogError(message, category);
        }
        
        #endregion
    }
}
