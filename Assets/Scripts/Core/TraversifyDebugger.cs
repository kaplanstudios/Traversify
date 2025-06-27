using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace Traversify.Core {
    public enum LogCategory { System, AI, Terrain, Models, Visualization, Streaming, IO, API, Process, UI, User }

    [RequireComponent(typeof(Canvas))]
    public class TraversifyDebugger : MonoBehaviour {
        // Singleton
        private static TraversifyDebugger _instance;
        public static TraversifyDebugger Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<TraversifyDebugger>();
                    if (_instance == null) {
                        var go = new GameObject("TraversifyDebugger");
                        _instance = go.AddComponent<TraversifyDebugger>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("Options")]
        public bool logPerformance = true;
        public bool logErrors = true;
        public bool showOnScreen = true;
        public Font uiFont;
        [Range(8, 24)] public int fontSize = 14;

        [Header("UI Elements (auto-created)")]
        public Text onScreenText;
        public ScrollRect scrollRect;

        // Internal
        private Stopwatch totalStopwatch = new Stopwatch();
        private Dictionary<string, Stopwatch> timers = new Dictionary<string, Stopwatch>();
        private List<string> logBuffer = new List<string>();
        private object _lock = new object();

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            SetupUI();
            Application.logMessageReceived += HandleUnityLog;
            totalStopwatch.Start();
            Log("TraversifyDebugger initialized", LogCategory.System);
        }

        private void OnDestroy() {
            Application.logMessageReceived -= HandleUnityLog;
        }

        private void SetupUI() {
            // Create Canvas if not present
            Canvas canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            // Background panel
            var panelGO = new GameObject("DebugPanel", typeof(RectTransform), typeof(Image));
            panelGO.transform.SetParent(transform, false);
            var panelRT = panelGO.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0f, 0f);
            panelRT.anchorMax = new Vector2(0.5f, 0.4f);
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;
            var img = panelGO.GetComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.5f);

            // ScrollRect
            var srGO = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect));
            srGO.transform.SetParent(panelGO.transform, false);
            var srRT = srGO.GetComponent<RectTransform>();
            srRT.anchorMin = new Vector2(0f, 0f);
            srRT.anchorMax = new Vector2(1f, 1f);
            srRT.offsetMin = new Vector2(5f, 5f);
            srRT.offsetMax = new Vector2(-5f, -5f);
            scrollRect = srGO.GetComponent<ScrollRect>();

            // Content
            var contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(srGO.transform, false);
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 0f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 0f);
            scrollRect.content = contentRT;

            // Text
            var textGO = new GameObject("DebugText", typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(contentGO.transform, false);
            var textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = new Vector2(0f, 0f);
            textRT.anchorMax = new Vector2(1f, 1f);
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;
            onScreenText = textGO.GetComponent<Text>();
            onScreenText.font = uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("Arial.ttf");
            onScreenText.fontSize = fontSize;
            onScreenText.color = Color.white;
            onScreenText.alignment = TextAnchor.LowerLeft;
            onScreenText.horizontalOverflow = HorizontalWrapMode.Wrap;
            onScreenText.verticalOverflow = VerticalWrapMode.Overflow;

            scrollRect.horizontal = false;
            scrollRect.vertical = true;
        }

        /// <summary>
        /// Log a message with a category.
        /// </summary>
        public void Log(string message, LogCategory category = LogCategory.System) {
            string entry = $"{DateTime.Now:HH:mm:ss} [{category}] {message}";
            Debug.Log(entry);
            AppendOnScreen(entry);
        }

        /// <summary>
        /// Log a warning.
        /// </summary>
        public void LogWarning(string message, LogCategory category = LogCategory.System) {
            string entry = $"{DateTime.Now:HH:mm:ss} [WARN][{category}] {message}";
            Debug.LogWarning(entry);
            AppendOnScreen(entry, Color.yellow);
        }

        /// <summary>
        /// Log an error.
        /// </summary>
        public void LogError(string message, LogCategory category = LogCategory.System) {
            string entry = $"{DateTime.Now:HH:mm:ss} [ERROR][{category}] {message}";
            Debug.LogError(entry);
            AppendOnScreen(entry, Color.red);
        }

        private void AppendOnScreen(string entry, Color? color = null) {
            lock (_lock) {
                logBuffer.Add(entry);
                if (logBuffer.Count > 200) logBuffer.RemoveAt(0);
                if (showOnScreen && onScreenText != null) {
                    onScreenText.text = string.Join("\n", logBuffer);
                    Canvas.ForceUpdateCanvases();
                    scrollRect.verticalNormalizedPosition = 0f;
                }
            }
        }

        /// <summary>
        /// Start a named timer.
        /// </summary>
        public void StartTimer(string name) {
            if (!logPerformance) return;
            if (timers.ContainsKey(name))
                timers[name].Reset();
            else
                timers[name] = new Stopwatch();
            timers[name].Start();
        }

        /// <summary>
        /// Stop a named timer and return elapsed seconds.
        /// </summary>
        public float StopTimer(string name) {
            if (!logPerformance || !timers.ContainsKey(name)) return 0f;
            var sw = timers[name];
            sw.Stop();
            float secs = (float)sw.Elapsed.TotalSeconds;
            Log($"{name} took {secs:F3}s", LogCategory.Process);
            return secs;
        }

        /// <summary>
        /// Report arbitrary progress (0–1).
        /// </summary>
        public static void ReportProgress(string message, float percent) {
            Instance.Log($"[Progress] {message} – {percent*100f:F0}%", LogCategory.UI);
        }

        /// <summary>
        /// Global error handler for Unity logs.
        /// </summary>
        private void HandleUnityLog(string condition, string stackTrace, LogType type) {
            if (!logErrors) return;
            if (type == LogType.Error || type == LogType.Exception) {
                AppendOnScreen($"[ERR] {condition}\n{stackTrace}", Color.red);
            } else if (type == LogType.Warning) {
                AppendOnScreen($"[WRN] {condition}", Color.yellow);
            }
        }
    }
}
