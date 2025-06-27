/*************************************************************************
 *  Traversify – Billboard.cs                                            *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 02:55:53 UTC                                     *
 *  Desc   : Advanced billboard component for making objects consistently*
 *           face the camera. Supports multiple billboarding modes,      *
 *           axis constraints, and performance optimizations. Used for   *
 *           labels, markers, and vegetation rendering in the Traversify *
 *           environment generation system.                              *
 *************************************************************************/

using System;
using UnityEngine;
using Traversify.Core;

namespace Traversify {
    /// <summary>
    /// Makes objects consistently face the camera, with support for multiple billboarding modes,
    /// axis constraints, and performance optimizations.
    /// </summary>
    [AddComponentMenu("Traversify/Utilities/Billboard")]
    public class Billboard : MonoBehaviour {
        #region Enumerations
        
        /// <summary>
        /// Defines how the billboard faces the camera.
        /// </summary>
        public enum BillboardMode {
            /// <summary>Look directly at the camera position.</summary>
            LookAtCamera,
            /// <summary>Orient to match camera forward direction.</summary>
            CameraForward,
            /// <summary>Orient to match camera rotation.</summary>
            CameraRotation,
            /// <summary>Spherical billboarding (full rotation).</summary>
            Spherical,
            /// <summary>Cylindrical billboarding (Y-axis locked).</summary>
            Cylindrical,
            /// <summary>Horizontal billboarding (X-Z plane).</summary>
            Horizontal
        }
        
        /// <summary>
        /// Axis to lock during billboarding.
        /// </summary>
        [Flags]
        public enum AxisConstraint {
            /// <summary>No axis constraints.</summary>
            None = 0,
            /// <summary>Lock X axis rotation.</summary>
            LockX = 1,
            /// <summary>Lock Y axis rotation.</summary>
            LockY = 2,
            /// <summary>Lock Z axis rotation.</summary>
            LockZ = 4,
            /// <summary>Lock X and Z axes (vertical alignment).</summary>
            VerticalAlign = LockX | LockZ,
            /// <summary>Lock all axes (disable billboarding).</summary>
            LockAll = LockX | LockY | LockZ
        }
        
        /// <summary>
        /// When to update the billboard orientation.
        /// </summary>
        public enum UpdateMode {
            /// <summary>Update every frame in LateUpdate.</summary>
            EveryFrame,
            /// <summary>Update at fixed intervals.</summary>
            FixedInterval,
            /// <summary>Update when the camera moves significantly.</summary>
            CameraMovement,
            /// <summary>Update once on start only.</summary>
            OnStart
        }
        
        #endregion
        
        #region Inspector Properties
        
        [Header("Billboard Settings")]
        [Tooltip("How the billboard should face the camera")]
        [SerializeField] private BillboardMode _mode = BillboardMode.LookAtCamera;
        
        [Tooltip("Constraints on rotation axes")]
        [SerializeField] private AxisConstraint _axisConstraint = AxisConstraint.None;
        
        [Tooltip("Camera to face (uses main camera if null)")]
        [SerializeField] private Camera _targetCamera;
        
        [Tooltip("Flip forward direction")]
        [SerializeField] private bool _flipForward = false;
        
        [Header("Performance Settings")]
        [Tooltip("When to update the billboard orientation")]
        [SerializeField] private UpdateMode _updateMode = UpdateMode.EveryFrame;
        
        [Tooltip("Update interval in seconds (for FixedInterval mode)")]
        [SerializeField] private float _updateInterval = 0.1f;
        
        [Tooltip("Minimum camera movement to trigger update (for CameraMovement mode)")]
        [SerializeField] private float _cameraMovementThreshold = 0.1f;
        
        [Tooltip("Disable billboarding when not visible")]
        [SerializeField] private bool _disableWhenInvisible = true;
        
        [Header("Offset and Alignment")]
        [Tooltip("Rotation offset in degrees (applied after billboarding)")]
        [SerializeField] private Vector3 _rotationOffset = Vector3.zero;
        
        [Tooltip("Apply offset in local space")]
        [SerializeField] private bool _localOffset = true;
        
        [Tooltip("Correct for parent rotation")]
        [SerializeField] private bool _correctParentRotation = false;
        
        [Header("Debug")]
        [Tooltip("Show debug visualization")]
        [SerializeField] private bool _showDebug = false;
        
        [Tooltip("Debug logger")]
        [SerializeField] private TraversifyDebugger _debugger;
        
        #endregion
        
        #region Private Fields
        
        private Camera _camera;
        private float _lastUpdateTime;
        private Vector3 _lastCameraPosition;
        private Quaternion _lastCameraRotation;
        private bool _isVisible = true;
        private Transform _cachedTransform;
        private Renderer _renderer;
        
        // Cached values for performance
        private Vector3 _forward = Vector3.forward;
        private Vector3 _up = Vector3.up;
        
        // For debug visualization
        private GameObject _debugArrow;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Current billboarding mode.
        /// </summary>
        public BillboardMode Mode {
            get => _mode;
            set {
                if (_mode != value) {
                    _mode = value;
                    UpdateBillboard(true);
                }
            }
        }
        
        /// <summary>
        /// Current axis constraints.
        /// </summary>
        public AxisConstraint CurrentAxisConstraint {
            get => _axisConstraint;
            set {
                if (_axisConstraint != value) {
                    _axisConstraint = value;
                    UpdateBillboard(true);
                }
            }
        }
        
        /// <summary>
        /// Target camera to face.
        /// </summary>
        public Camera TargetCamera {
            get => _targetCamera ?? _camera;
            set => _targetCamera = value;
        }
        
        /// <summary>
        /// Update mode for billboard orientation.
        /// </summary>
        public UpdateMode BillboardUpdateMode {
            get => _updateMode;
            set => _updateMode = value;
        }
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake() {
            _cachedTransform = transform;
            _renderer = GetComponent<Renderer>();
            
            if (_debugger == null) {
                _debugger = FindObjectOfType<TraversifyDebugger>();
            }
            
            // Setup default forward and up vectors
            _forward = _flipForward ? Vector3.back : Vector3.forward;
            _up = Vector3.up;
            
            // Find main camera if not specified
            if (_targetCamera == null) {
                _camera = Camera.main;
            } else {
                _camera = _targetCamera;
            }
        }
        
        private void Start() {
            // Initialize camera position tracking
            if (_camera != null) {
                _lastCameraPosition = _camera.transform.position;
                _lastCameraRotation = _camera.transform.rotation;
            }
            
            // Force update on start
            UpdateBillboard(true);
            
            // Create debug visualization if needed
            if (_showDebug) {
                CreateDebugVisualization();
            }
        }
        
        private void OnEnable() {
            // Force update when enabled
            _lastUpdateTime = -_updateInterval;
            UpdateBillboard(true);
        }
        
        private void LateUpdate() {
            // Skip updates if disabled when invisible
            if (_disableWhenInvisible && _renderer != null && !_renderer.isVisible && _isVisible) {
                _isVisible = false;
                return;
            } else if (_disableWhenInvisible && _renderer != null && _renderer.isVisible && !_isVisible) {
                _isVisible = true;
                UpdateBillboard(true);
                return;
            } else if (_disableWhenInvisible && !_isVisible) {
                return;
            }
            
            // Check for camera changes
            if (_camera == null && _targetCamera == null) {
                _camera = Camera.main;
                if (_camera == null) return;
            }
            
            // Update the camera reference if target is specified
            if (_targetCamera != null) {
                _camera = _targetCamera;
            }
            
            // Determine if update is needed based on update mode
            bool shouldUpdate = false;
            
            switch (_updateMode) {
                case UpdateMode.EveryFrame:
                    shouldUpdate = true;
                    break;
                    
                case UpdateMode.FixedInterval:
                    if (Time.time - _lastUpdateTime >= _updateInterval) {
                        shouldUpdate = true;
                        _lastUpdateTime = Time.time;
                    }
                    break;
                    
                case UpdateMode.CameraMovement:
                    float positionDelta = Vector3.Distance(_camera.transform.position, _lastCameraPosition);
                    float rotationDelta = Quaternion.Angle(_camera.transform.rotation, _lastCameraRotation);
                    
                    if (positionDelta > _cameraMovementThreshold || rotationDelta > _cameraMovementThreshold * 10f) {
                        shouldUpdate = true;
                        _lastCameraPosition = _camera.transform.position;
                        _lastCameraRotation = _camera.transform.rotation;
                    }
                    break;
                    
                case UpdateMode.OnStart:
                    // No updates after start
                    break;
            }
            
            // Update billboard if needed
            if (shouldUpdate) {
                UpdateBillboard(false);
            }
            
            // Update debug visualization
            if (_showDebug && _debugArrow != null) {
                UpdateDebugVisualization();
            }
        }
        
        private void OnDisable() {
            if (_debugArrow != null) {
                Destroy(_debugArrow);
                _debugArrow = null;
            }
        }
        
        private void OnDestroy() {
            if (_debugArrow != null) {
                Destroy(_debugArrow);
                _debugArrow = null;
            }
        }
        
        private void OnValidate() {
            // Update debug visualization
            if (_showDebug && Application.isPlaying && _debugArrow == null) {
                CreateDebugVisualization();
            } else if (!_showDebug && _debugArrow != null) {
                Destroy(_debugArrow);
                _debugArrow = null;
            }
        }
        
        #endregion
        
        #region Billboard Methods
        
        /// <summary>
        /// Update the billboard orientation.
        /// </summary>
        /// <param name="forceUpdate">Force update regardless of conditions</param>
        public void UpdateBillboard(bool forceUpdate = false) {
            if (_camera == null) return;
            
            try {
                // Calculate rotation based on mode
                Quaternion targetRotation = CalculateTargetRotation();
                
                // Apply rotation
                ApplyRotation(targetRotation);
                
                // Log if debugging
                if (_showDebug && _debugger != null) {
                    _debugger.LogVerbose($"Billboard updated: {_mode} mode, Facing: {_cachedTransform.forward}", LogCategory.Visualization);
                }
            }
            catch (Exception ex) {
                if (_debugger != null) {
                    _debugger.LogError($"Billboard error: {ex.Message}", LogCategory.Visualization);
                } else {
                    Debug.LogError($"[Billboard] Error: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Calculate the target rotation based on the current mode.
        /// </summary>
        /// <returns>Target rotation</returns>
        private Quaternion CalculateTargetRotation() {
            Quaternion rotation;
            
            switch (_mode) {
                case BillboardMode.LookAtCamera:
                    // Look directly at camera position
                    Vector3 dirToCamera = _camera.transform.position - _cachedTransform.position;
                    rotation = Quaternion.LookRotation(_flipForward ? -dirToCamera : dirToCamera, _up);
                    break;
                    
                case BillboardMode.CameraForward:
                    // Align with camera forward
                    rotation = Quaternion.LookRotation(_flipForward ? -_camera.transform.forward : _camera.transform.forward, _up);
                    break;
                    
                case BillboardMode.CameraRotation:
                    // Match camera rotation exactly
                    rotation = _camera.transform.rotation;
                    if (_flipForward) {
                        rotation *= Quaternion.Euler(0, 180, 0);
                    }
                    break;
                    
                case BillboardMode.Spherical:
                    // Full spherical billboarding
                    Vector3 dirToCamera2 = _camera.transform.position - _cachedTransform.position;
                    rotation = Quaternion.LookRotation(_flipForward ? -dirToCamera2 : dirToCamera2);
                    break;
                    
                case BillboardMode.Cylindrical:
                    // Cylindrical billboarding (Y-axis locked)
                    Vector3 dirToCamera3 = _camera.transform.position - _cachedTransform.position;
                    dirToCamera3.y = 0; // Flatten in Y axis
                    if (dirToCamera3.sqrMagnitude < 0.001f) {
                        dirToCamera3 = _flipForward ? -_camera.transform.forward : _camera.transform.forward;
                        dirToCamera3.y = 0;
                    }
                    rotation = Quaternion.LookRotation(_flipForward ? -dirToCamera3.normalized : dirToCamera3.normalized, _up);
                    break;
                    
                case BillboardMode.Horizontal:
                    // Horizontal billboarding
                    Vector3 camForward = _camera.transform.forward;
                    camForward.y = 0;
                    if (camForward.sqrMagnitude < 0.001f) {
                        camForward = Vector3.forward;
                    }
                    rotation = Quaternion.LookRotation(_flipForward ? -camForward.normalized : camForward.normalized, _up);
                    break;
                    
                default:
                    rotation = _cachedTransform.rotation;
                    break;
            }
            
            return rotation;
        }
        
        /// <summary>
        /// Apply rotation to the transform with constraints.
        /// </summary>
        /// <param name="targetRotation">Target rotation</param>
        private void ApplyRotation(Quaternion targetRotation) {
            // Apply parent rotation correction if needed
            if (_correctParentRotation && _cachedTransform.parent != null) {
                targetRotation = Quaternion.Inverse(_cachedTransform.parent.rotation) * targetRotation;
            }
            
            // Apply axis constraints
            Vector3 eulerAngles = targetRotation.eulerAngles;
            Vector3 currentEuler = _cachedTransform.localRotation.eulerAngles;
            
            if ((_axisConstraint & AxisConstraint.LockX) != 0) {
                eulerAngles.x = currentEuler.x;
            }
            
            if ((_axisConstraint & AxisConstraint.LockY) != 0) {
                eulerAngles.y = currentEuler.y;
            }
            
            if ((_axisConstraint & AxisConstraint.LockZ) != 0) {
                eulerAngles.z = currentEuler.z;
            }
            
            // Apply rotation
            Quaternion finalRotation = Quaternion.Euler(eulerAngles);
            
            // Apply rotation offset
            if (_localOffset) {
                finalRotation *= Quaternion.Euler(_rotationOffset);
            } else {
                finalRotation = finalRotation * Quaternion.Euler(_rotationOffset);
            }
            
            // Set rotation
            if (_correctParentRotation) {
                _cachedTransform.localRotation = finalRotation;
            } else {
                _cachedTransform.rotation = finalRotation;
            }
        }
        
        #endregion
        
        #region Debug Visualization
        
        /// <summary>
        /// Creates a debug arrow to visualize billboard direction.
        /// </summary>
        private void CreateDebugVisualization() {
            if (_debugArrow != null) return;
            
            _debugArrow = new GameObject("BillboardDebugArrow");
            _debugArrow.transform.SetParent(_cachedTransform);
            _debugArrow.transform.localPosition = Vector3.zero;
            _debugArrow.transform.localRotation = Quaternion.identity;
            
            // Create simple arrow mesh
            var lineRenderer = _debugArrow.AddComponent<LineRenderer>();
            lineRenderer.startWidth = 0.02f;
            lineRenderer.endWidth = 0.02f;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, Vector3.zero);
            lineRenderer.SetPosition(1, Vector3.forward * 0.5f);
            
            // Add material
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.color = Color.green;
            lineRenderer.material = mat;
            
            UpdateDebugVisualization();
        }
        
        /// <summary>
        /// Updates the debug visualization arrow.
        /// </summary>
        private void UpdateDebugVisualization() {
            if (_debugArrow == null) return;
            
            var lineRenderer = _debugArrow.GetComponent<LineRenderer>();
            if (lineRenderer == null) return;
            
            lineRenderer.SetPosition(0, Vector3.zero);
            lineRenderer.SetPosition(1, Vector3.forward * 0.5f);
            
            // Change color based on mode
            switch (_mode) {
                case BillboardMode.LookAtCamera:
                    lineRenderer.material.color = Color.green;
                    break;
                case BillboardMode.CameraForward:
                    lineRenderer.material.color = Color.blue;
                    break;
                case BillboardMode.Cylindrical:
                    lineRenderer.material.color = Color.yellow;
                    break;
                case BillboardMode.Horizontal:
                    lineRenderer.material.color = Color.cyan;
                    break;
                default:
                    lineRenderer.material.color = Color.white;
                    break;
            }
        }
        
        #endregion
        
        #region Static Utility Methods
        
        /// <summary>
        /// Makes a GameObject use billboard behavior.
        /// </summary>
        /// <param name="target">Target GameObject</param>
        /// <param name="mode">Billboard mode</param>
        /// <param name="lockYAxis">Whether to lock the Y axis</param>
        /// <returns>The added Billboard component</returns>
        public static Billboard MakeBillboard(GameObject target, BillboardMode mode = BillboardMode.LookAtCamera, bool lockYAxis = false) {
            if (target == null) return null;
            
            Billboard billboard = target.GetComponent<Billboard>();
            if (billboard == null) {
                billboard = target.AddComponent<Billboard>();
            }
            
            billboard.Mode = mode;
            billboard.AxisConstraint = lockYAxis ? AxisConstraint.LockY : AxisConstraint.None;
            
            return billboard;
        }
        
        /// <summary>
        /// Makes a GameObject use text billboarding.
        /// </summary>
        /// <param name="target">Target GameObject</param>
        /// <param name="cameraForward">Use camera forward instead of look at</param>
        /// <returns>The added Billboard component</returns>
        public static Billboard MakeTextBillboard(GameObject target, bool cameraForward = false) {
            if (target == null) return null;
            
            Billboard billboard = target.GetComponent<Billboard>();
            if (billboard == null) {
                billboard = target.AddComponent<Billboard>();
            }
            
            billboard.Mode = cameraForward ? BillboardMode.CameraForward : BillboardMode.LookAtCamera;
            billboard.AxisConstraint = AxisConstraint.None;
            billboard.BillboardUpdateMode = UpdateMode.CameraMovement;
            billboard._updateInterval = 0.1f;
            billboard._cameraMovementThreshold = 0.2f;
            billboard._disableWhenInvisible = true;
            
            return billboard;
        }
        
        /// <summary>
        /// Creates a billboard-enabled text label at a position.
        /// </summary>
        /// <param name="text">Label text</param>
        /// <param name="position">World position</param>
        /// <param name="color">Text color</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="parent">Optional parent transform</param>
        /// <returns>Created text object</returns>
        public static GameObject CreateBillboardLabel(string text, Vector3 position, Color color, int fontSize = 12, Transform parent = null) {
            GameObject labelObj = new GameObject($"Label_{text}");
            if (parent != null) {
                labelObj.transform.SetParent(parent, false);
            }
            labelObj.transform.position = position;
            
            // Create child for text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(labelObj.transform, false);
            
            // Add TextMesh component
            TextMesh textMesh = textObj.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.color = color;
            textMesh.fontSize = fontSize;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            
            // Add billboard to parent
            MakeTextBillboard(labelObj);
            
            return labelObj;
        }
        
        /// <summary>
        /// Creates a cylindrical billboard object that always faces horizontally toward the camera.
        /// </summary>
        /// <param name="prefab">Object prefab to use</param>
        /// <param name="position">World position</param>
        /// <param name="parent">Optional parent transform</param>
        /// <returns>Created billboard object</returns>
        public static GameObject CreateCylindricalBillboard(GameObject prefab, Vector3 position, Transform parent = null) {
            if (prefab == null) return null;
            
            GameObject billboardObj = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity, parent);
            billboardObj.name = $"CylBB_{prefab.name}";
            
            // Add billboard component
            Billboard billboard = billboardObj.AddComponent<Billboard>();
            billboard.Mode = BillboardMode.Cylindrical;
            billboard.AxisConstraint = AxisConstraint.None;
            billboard.BillboardUpdateMode = UpdateMode.CameraMovement;
            billboard._updateInterval = 0.1f;
            billboard._cameraMovementThreshold = 0.2f;
            
            return billboardObj;
        }
        
        /// <summary>
        /// Updates all billboards in the scene.
        /// </summary>
        public static void UpdateAllBillboards() {
            foreach (Billboard billboard in FindObjectsOfType<Billboard>()) {
                billboard.UpdateBillboard(true);
            }
        }
        
        #endregion
    }
}