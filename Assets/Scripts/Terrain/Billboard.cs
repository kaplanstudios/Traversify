using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Traversify
{
    /// <summary>
    /// Makes objects always face the camera with various billboard modes and optimization features
    /// Created by: dkaplan73
    /// Last Updated: 2025-06-25 04:44:13 UTC
    /// </summary>
    [DisallowMultipleComponent]
    public class Billboard : MonoBehaviour
    {
        public enum BillboardMode
        {
            /// <summary>Full rotation to face camera</summary>
            FullRotation,
            /// <summary>Only rotates around Y axis (vertical billboard)</summary>
            YAxisOnly,
            /// <summary>Only rotates around X axis</summary>
            XAxisOnly,
            /// <summary>Only rotates around Z axis</summary>
            ZAxisOnly,
            /// <summary>Faces camera but maintains up direction</summary>
            LookAtCamera,
            /// <summary>Faces camera position but ignores camera rotation</summary>
            FaceCameraPosition,
            /// <summary>Custom axis rotation</summary>
            CustomAxis
        }
        
        [Header("Billboard Settings")]
        [SerializeField] private BillboardMode mode = BillboardMode.FullRotation;
        [SerializeField] private bool reverseDirection = false;
        [SerializeField] private Vector3 customAxis = Vector3.up;
        [SerializeField] private bool smoothRotation = false;
        [SerializeField] private float rotationSpeed = 5f;
        
        [Header("Constraints")]
        [SerializeField] private bool lockXRotation = false;
        [SerializeField] private bool lockYRotation = false;
        [SerializeField] private bool lockZRotation = false;
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;
        
        [Header("Performance")]
        [SerializeField] private bool useMainCameraOnly = true;
        [SerializeField] private float updateInterval = 0f; // 0 = every frame
        [SerializeField] private bool disableWhenNotVisible = true;
        [SerializeField] private float maxDistance = 1000f;
        [SerializeField] private bool scaleByDistance = false;
        [SerializeField] private AnimationCurve distanceScaleCurve;
        
        [Header("Multi-Camera Support")]
        [SerializeField] private bool billboardToAllCameras = false;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private string targetCameraTag = "MainCamera";
        
        [Header("Advanced Options")]
        [SerializeField] private bool maintainWorldUp = false;
        [SerializeField] private Vector3 worldUpOverride = Vector3.up;
        [SerializeField] private bool useParentRotation = false;
        [SerializeField] private bool compensateScale = true;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        [SerializeField] private Color debugColor = Color.cyan;
        
        // Runtime variables
        private Camera mainCamera;
        private Camera[] allCameras;
        private Transform cameraTransform;
        private Quaternion originalRotation;
        private Vector3 originalScale;
        private float lastUpdateTime;
        private bool isVisible = true;
        private Renderer objectRenderer;
        private Quaternion targetRotation;
        private float currentDistance;
        
        // Optimization
        private static List<Billboard> allBillboards = new List<Billboard>();
        private static bool isUpdatingBillboards = false;
        
        // Events
        public event Action<Camera> OnCameraChanged;
        public event Action<float> OnDistanceChanged;
        
        private void Awake()
        {
            // Store original values
            originalRotation = transform.rotation;
            originalScale = transform.localScale;
            
            // Get renderer for visibility checks
            objectRenderer = GetComponent<Renderer>();
            
            // Initialize scale curve if not set
            if (distanceScaleCurve == null || distanceScaleCurve.keys.Length == 0)
            {
                InitializeDefaultScaleCurve();
            }
            
            // Add to global list for batch updates
            if (!allBillboards.Contains(this))
            {
                allBillboards.Add(this);
            }
            
            Debug.Log($"[Billboard] Initialized on {gameObject.name} with mode: {mode} - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC - User: dkaplan73");
        }
        
        private void OnEnable()
        {
            FindCamera();
            
            // Start update coroutine if using interval
            if (updateInterval > 0)
            {
                StartCoroutine(IntervalUpdate());
            }
        }
        
        private void Start()
        {
            // Final camera check
            if (mainCamera == null)
            {
                FindCamera();
            }
            
            // Start batch update manager if not running
            if (!isUpdatingBillboards && allBillboards.Count > 0)
            {
                StartCoroutine(BatchUpdateManager());
            }
        }
        
        private void InitializeDefaultScaleCurve()
        {
            distanceScaleCurve = new AnimationCurve();
            distanceScaleCurve.AddKey(0f, 1f);
            distanceScaleCurve.AddKey(50f, 1f);
            distanceScaleCurve.AddKey(100f, 1.5f);
            distanceScaleCurve.AddKey(500f, 3f);
            distanceScaleCurve.AddKey(1000f, 5f);
        }
        
        private void FindCamera()
        {
            if (targetCamera != null)
            {
                mainCamera = targetCamera;
            }
            else if (useMainCameraOnly)
            {
                mainCamera = Camera.main;
                
                // Fallback to tagged camera
                if (mainCamera == null && !string.IsNullOrEmpty(targetCameraTag))
                {
                    GameObject camObj = GameObject.FindGameObjectWithTag(targetCameraTag);
                    if (camObj != null)
                    {
                        mainCamera = camObj.GetComponent<Camera>();
                    }
                }
            }
            
            // Get all cameras if needed
            if (billboardToAllCameras)
            {
                allCameras = Camera.allCameras;
            }
            
            // Cache transform
            if (mainCamera != null)
            {
                cameraTransform = mainCamera.transform;
                OnCameraChanged?.Invoke(mainCamera);
            }
            else
            {
                Debug.LogWarning($"[Billboard] No camera found for {gameObject.name}");
            }
        }
        
        private void Update()
        {
            // Skip if using interval updates or batch updates
            if (updateInterval > 0 || isUpdatingBillboards) return;
            
            // Skip if not visible and optimization is enabled
            if (disableWhenNotVisible && !isVisible) return;
            
            UpdateBillboard();
        }
        
        private IEnumerator IntervalUpdate()
        {
            while (enabled)
            {
                yield return new WaitForSeconds(updateInterval);
                
                if (isVisible || !disableWhenNotVisible)
                {
                    UpdateBillboard();
                }
            }
        }
        
        private static IEnumerator BatchUpdateManager()
        {
            isUpdatingBillboards = true;
            
            while (allBillboards.Count > 0)
            {
                // Update all billboards in batches
                int batchSize = Mathf.Min(10, allBillboards.Count);
                
                for (int i = 0; i < allBillboards.Count; i += batchSize)
                {
                    int endIndex = Mathf.Min(i + batchSize, allBillboards.Count);
                    
                    for (int j = i; j < endIndex; j++)
                    {
                        if (allBillboards[j] != null && allBillboards[j].enabled)
                        {
                            allBillboards[j].UpdateBillboard();
                        }
                    }
                    
                    yield return null; // Yield every batch
                }
                
                yield return new WaitForSeconds(0.016f); // 60 FPS
            }
            
            isUpdatingBillboards = false;
        }
        
        private void UpdateBillboard()
        {
            if (cameraTransform == null)
            {
                FindCamera();
                if (cameraTransform == null) return;
            }
            
            // Update distance
            currentDistance = Vector3.Distance(transform.position, cameraTransform.position);
            
            // Check max distance
            if (currentDistance > maxDistance)
            {
                if (objectRenderer != null)
                    objectRenderer.enabled = false;
                return;
            }
            else if (objectRenderer != null && !objectRenderer.enabled)
            {
                objectRenderer.enabled = true;
            }
            
            // Calculate rotation based on mode
            Quaternion newRotation = CalculateRotation();
            
            // Apply rotation offset
            if (rotationOffset != Vector3.zero)
            {
                newRotation *= Quaternion.Euler(rotationOffset);
            }
            
            // Apply constraints
            if (lockXRotation || lockYRotation || lockZRotation)
            {
                Vector3 euler = newRotation.eulerAngles;
                Vector3 currentEuler = transform.rotation.eulerAngles;
                
                if (lockXRotation) euler.x = currentEuler.x;
                if (lockYRotation) euler.y = currentEuler.y;
                if (lockZRotation) euler.z = currentEuler.z;
                
                newRotation = Quaternion.Euler(euler);
            }
            
            // Apply rotation
            if (smoothRotation)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, newRotation, Time.deltaTime * rotationSpeed);
            }
            else
            {
                transform.rotation = newRotation;
            }
            
            // Apply distance scaling if enabled
            if (scaleByDistance)
            {
                UpdateDistanceScale();
            }
            
            // Fire distance event
            OnDistanceChanged?.Invoke(currentDistance);
            
            // Debug visualization
            if (showDebugInfo)
            {
                DrawDebugInfo();
            }
        }
        
        private Quaternion CalculateRotation()
        {
            Vector3 direction = cameraTransform.position - transform.position;
            
            if (reverseDirection)
            {
                direction = -direction;
            }
            
            Quaternion rotation = Quaternion.identity;
            
            switch (mode)
            {
                case BillboardMode.FullRotation:
                    if (maintainWorldUp)
                    {
                        rotation = Quaternion.LookRotation(direction, worldUpOverride);
                    }
                    else
                    {
                        rotation = cameraTransform.rotation;
                        if (reverseDirection)
                        {
                            rotation *= Quaternion.Euler(0, 180, 0);
                        }
                    }
                    break;
                    
                case BillboardMode.YAxisOnly:
                    direction.y = 0;
                    if (direction != Vector3.zero)
                    {
                        rotation = Quaternion.LookRotation(direction);
                        // Preserve original X and Z rotation
                        Vector3 euler = rotation.eulerAngles;
                        euler.x = originalRotation.eulerAngles.x;
                        euler.z = originalRotation.eulerAngles.z;
                        rotation = Quaternion.Euler(euler);
                    }
                    break;
                    
                case BillboardMode.XAxisOnly:
                    direction.x = 0;
                    if (direction != Vector3.zero)
                    {
                        rotation = Quaternion.LookRotation(direction);
                        // Preserve original Y and Z rotation
                        Vector3 euler = rotation.eulerAngles;
                        euler.y = originalRotation.eulerAngles.y;
                        euler.z = originalRotation.eulerAngles.z;
                        rotation = Quaternion.Euler(euler);
                    }
                    break;
                    
                case BillboardMode.ZAxisOnly:
                    direction.z = 0;
                    if (direction != Vector3.zero)
                    {
                        rotation = Quaternion.LookRotation(direction);
                        // Preserve original X and Y rotation
                        Vector3 euler = rotation.eulerAngles;
                        euler.x = originalRotation.eulerAngles.x;
                        euler.y = originalRotation.eulerAngles.y;
                        rotation = Quaternion.Euler(euler);
                    }
                    break;
                    
                case BillboardMode.LookAtCamera:
                    rotation = Quaternion.LookRotation(direction, worldUpOverride);
                    break;
                    
                case BillboardMode.FaceCameraPosition:
                    rotation = Quaternion.LookRotation(direction);
                    break;
                    
                case BillboardMode.CustomAxis:
                    if (customAxis != Vector3.zero)
                    {
                        float angle = Vector3.SignedAngle(
                            Vector3.forward,
                            direction,
                            customAxis.normalized
                        );
                        rotation = Quaternion.AngleAxis(angle, customAxis.normalized);
                    }
                    break;
            }
            
            // Apply parent rotation if needed
            if (useParentRotation && transform.parent != null)
            {
                rotation = transform.parent.rotation * rotation;
            }
            
            return rotation;
        }
        
        private void UpdateDistanceScale()
        {
            if (distanceScaleCurve == null) return;
            
            float scaleFactor = distanceScaleCurve.Evaluate(currentDistance);
            
            if (compensateScale)
            {
                // Compensate for parent scale
                Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
                transform.localScale = new Vector3(
                    originalScale.x * scaleFactor / parentScale.x,
                    originalScale.y * scaleFactor / parentScale.y,
                    originalScale.z * scaleFactor / parentScale.z
                );
            }
            else
            {
                transform.localScale = originalScale * scaleFactor;
            }
        }
        
        private void DrawDebugInfo()
        {
            if (cameraTransform == null) return;
            
            // Draw line to camera
            Debug.DrawLine(transform.position, cameraTransform.position, debugColor);
            
            // Draw forward direction
            Debug.DrawRay(transform.position, transform.forward * 2f, Color.blue);
            
            // Draw up direction
            Debug.DrawRay(transform.position, transform.up * 1f, Color.green);
            
            // Draw right direction
            Debug.DrawRay(transform.position, transform.right * 1f, Color.red);
        }
        
        private void OnBecameVisible()
        {
            isVisible = true;
        }
        
        private void OnBecameInvisible()
        {
            isVisible = false;
        }
        
        private void OnDisable()
        {
            StopAllCoroutines();
        }
        
        private void OnDestroy()
        {
            // Remove from global list
            if (allBillboards.Contains(this))
            {
                allBillboards.Remove(this);
            }
        }
        
        private void OnValidate()
        {
            // Validate settings
            rotationSpeed = Mathf.Max(0.1f, rotationSpeed);
            maxDistance = Mathf.Max(1f, maxDistance);
            updateInterval = Mathf.Max(0f, updateInterval);
            
            // Normalize custom axis
            if (customAxis != Vector3.zero)
            {
                customAxis = customAxis.normalized;
            }
        }
        
        #if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!showDebugInfo) return;
            
            // Draw max distance sphere
            Gizmos.color = new Color(debugColor.r, debugColor.g, debugColor.b, 0.1f);
            Gizmos.DrawWireSphere(transform.position, maxDistance);
            
            // Draw axis based on mode
            Gizmos.color = debugColor;
            switch (mode)
            {
                case BillboardMode.YAxisOnly:
                    Gizmos.DrawRay(transform.position, Vector3.up * 3f);
                    break;
                case BillboardMode.XAxisOnly:
                    Gizmos.DrawRay(transform.position, Vector3.right * 3f);
                    break;
                case BillboardMode.ZAxisOnly:
                    Gizmos.DrawRay(transform.position, Vector3.forward * 3f);
                    break;
                case BillboardMode.CustomAxis:
                    Gizmos.DrawRay(transform.position, customAxis * 3f);
                    break;
            }
            
            // Show current distance
            if (Application.isPlaying && cameraTransform != null)
            {
                UnityEditor.Handles.Label(
                    transform.position + Vector3.up * 2f,
                    $"Distance: {currentDistance:F1}m\nMode: {mode}"
                );
            }
        }
        #endif
        
        // Public API
        
        /// <summary>
        /// Sets the billboard mode at runtime
        /// </summary>
        public void SetBillboardMode(BillboardMode newMode)
        {
            mode = newMode;
        }
        
        /// <summary>
        /// Sets a specific camera to billboard towards
        /// </summary>
        public void SetTargetCamera(Camera camera)
        {
            targetCamera = camera;
            mainCamera = camera;
            cameraTransform = camera != null ? camera.transform : null;
            OnCameraChanged?.Invoke(camera);
        }
        
        /// <summary>
        /// Forces an immediate update of the billboard rotation
        /// </summary>
        public void ForceUpdate()
        {
            UpdateBillboard();
        }
        
        /// <summary>
        /// Gets the current distance to the camera
        /// </summary>
        public float GetDistanceToCamera()
        {
            return currentDistance;
        }
        
        /// <summary>
        /// Resets the billboard to its original rotation
        /// </summary>
        public void ResetRotation()
        {
            transform.rotation = originalRotation;
        }
        
        /// <summary>
        /// Resets the billboard to its original scale
        /// </summary>
        public void ResetScale()
        {
            transform.localScale = originalScale;
        }
        
        /// <summary>
        /// Enables or disables smooth rotation
        /// </summary>
        public void SetSmoothRotation(bool smooth, float speed = 5f)
        {
            smoothRotation = smooth;
            rotationSpeed = speed;
        }
        
        /// <summary>
        /// Sets rotation constraints
        /// </summary>
        public void SetRotationConstraints(bool lockX, bool lockY, bool lockZ)
        {
            lockXRotation = lockX;
            lockYRotation = lockY;
            lockZRotation = lockZ;
        }
        
        /// <summary>
        /// Static method to update all billboards at once
        /// </summary>
        public static void UpdateAllBillboards()
        {
            foreach (var billboard in allBillboards)
            {
                if (billboard != null && billboard.enabled)
                {
                    billboard.UpdateBillboard();
                }
            }
        }
        
        /// <summary>
        /// Gets all active billboards in the scene
        /// </summary>
        public static List<Billboard> GetAllBillboards()
        {
            return new List<Billboard>(allBillboards);
        }
    }
}