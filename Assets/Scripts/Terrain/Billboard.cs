using UnityEngine;

namespace Traversify {
    /// <summary>Attach to labels so they always face the main camera.</summary>
    public class Billboard : MonoBehaviour {
        private Camera _cam;
        private void Awake() {
            _cam = Camera.main;
        }
        private void LateUpdate() {
            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
                transform.forward = _cam.transform.forward;
        }
        public static void MakeBillboard(GameObject go) {
            if (go.GetComponent<Billboard>() == null)
                go.AddComponent<Billboard>();
        }
    }
}