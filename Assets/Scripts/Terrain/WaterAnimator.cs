using UnityEngine;

namespace Traversify {
    /// <summary>
    /// Animates water surfaces with wave effects
    /// </summary>
    public class WaterAnimator : MonoBehaviour {
        [Header("Wave Settings")]
        public float waveHeight = 0.5f;
        public float waveSpeed = 1f;
        public float waveScale = 10f;
        
        [Header("Flow Settings")]
        public Vector2 flowDirection = new Vector2(1f, 0f);
        public float flowSpeed = 0.5f;
        
        [Header("Animation")]
        public bool animatePosition = true;
        public bool animateMaterial = true;
        
        private Material _material;
        private Renderer _renderer;
        private Vector3 _originalPosition;
        private float _time;
        private static readonly int WaveTimeProperty = Shader.PropertyToID("_WaveTime");
        private static readonly int WaveHeightProperty = Shader.PropertyToID("_WaveHeight");
        private static readonly int FlowSpeedProperty = Shader.PropertyToID("_FlowSpeed");
        private static readonly int FlowDirectionProperty = Shader.PropertyToID("_FlowDirection");
        
        private void Start() {
            _renderer = GetComponent<Renderer>();
            if (_renderer != null) {
                _material = _renderer.material;
            }
            
            _originalPosition = transform.position;
        }
        
        private void Update() {
            if (!enabled) return;
            
            _time += Time.deltaTime;
            
            // Animate position with waves
            if (animatePosition) {
                AnimateWaterPosition();
            }
            
            // Animate material properties
            if (animateMaterial && _material != null) {
                AnimateMaterialProperties();
            }
        }
        
        private void AnimateWaterPosition() {
            // Create subtle wave movement
            float waveOffset = Mathf.Sin(_time * waveSpeed) * waveHeight * 0.1f;
            
            Vector3 newPosition = _originalPosition;
            newPosition.y += waveOffset;
            
            // Add some horizontal flow
            newPosition.x += Mathf.Sin(_time * flowSpeed) * flowDirection.x * 0.1f;
            newPosition.z += Mathf.Sin(_time * flowSpeed) * flowDirection.y * 0.1f;
            
            transform.position = newPosition;
        }
        
        private void AnimateMaterialProperties() {
            // Update shader properties for wave animation
            _material.SetFloat(WaveTimeProperty, _time * waveSpeed);
            _material.SetFloat(WaveHeightProperty, waveHeight);
            _material.SetFloat(FlowSpeedProperty, flowSpeed);
            _material.SetVector(FlowDirectionProperty, new Vector4(flowDirection.x, flowDirection.y, 0, 0));
            
            // Animate tiling offset for flowing effect
            Vector2 offset = new Vector2(
                _time * flowDirection.x * flowSpeed * 0.1f,
                _time * flowDirection.y * flowSpeed * 0.1f
            );
            
            _material.mainTextureOffset = offset;
        }
        
        private void OnDestroy() {
            // Clean up material instance if we created one
            if (_material != null && _renderer != null && _renderer.material == _material) {
                DestroyImmediate(_material);
            }
        }
        
        /// <summary>
        /// Reset water position to original
        /// </summary>
        public void ResetPosition() {
            transform.position = _originalPosition;
        }
        
        /// <summary>
        /// Set new flow direction
        /// </summary>
        public void SetFlowDirection(Vector2 direction) {
            flowDirection = direction.normalized;
        }
        
        /// <summary>
        /// Set wave parameters
        /// </summary>
        public void SetWaveParameters(float height, float speed, float scale) {
            waveHeight = height;
            waveSpeed = speed;
            waveScale = scale;
        }
    }
}
