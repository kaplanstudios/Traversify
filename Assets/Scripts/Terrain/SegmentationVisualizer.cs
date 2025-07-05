/*************************************************************************
 *  Traversify – SegmentationVisualizer.cs                               *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Updated: 2025-07-05                                                  *
 *  Desc   : Advanced visualization for segmentation masks with          *
 *           interactive labeling, overlays, and highlighting.           *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Traversify.Core;
using Traversify.AI;
using UnityEngine.EventSystems;

namespace Traversify.Visualization {
    /// <summary>
    /// Advanced visualization system for segmentation results with
    /// interactive features and detailed labeling.
    /// </summary>
    [RequireComponent(typeof(TraversifyDebugger))]
    public class SegmentationVisualizer : TraversifyComponent, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler {
        #region Inspector Fields
        
        [Header("Display Settings")]
        [Tooltip("Base image to display")]
        [SerializeField] private RawImage _baseImageDisplay;
        
        [Tooltip("Overlay image for segmentation visualization")]
        [SerializeField] private RawImage _overlayDisplay;
        
        [Tooltip("Alpha value for the segmentation overlay")]
        [Range(0f, 1f)]
        [SerializeField] private float _overlayAlpha = 0.7f;
        
        [Tooltip("Display segmentation outlines")]
        [SerializeField] private bool _showOutlines = true;
        
        [Tooltip("Outline thickness in pixels")]
        [Range(1f, 10f)]
        [SerializeField] private float _outlineThickness = 2f;
        
        [Tooltip("Outline color")]
        [SerializeField] private Color _outlineColor = Color.white;
        
        [Header("Label Settings")]
        [Tooltip("Parent object for dynamic labels")]
        [SerializeField] private RectTransform _labelsContainer;
        
        [Tooltip("Label prefab with TextMeshPro component")]
        [SerializeField] private GameObject _labelPrefab;
        
        [Tooltip("Show object labels")]
        [SerializeField] private bool _showLabels = true;
        
        [Tooltip("Show class names on labels")]
        [SerializeField] private bool _showClassNames = true;
        
        [Tooltip("Show confidence scores on labels")]
        [SerializeField] private bool _showConfidence = true;
        
        [Tooltip("Show object IDs on labels")]
        [SerializeField] private bool _showObjectIds = false;
        
        [Tooltip("Maximum label length")]
        [SerializeField] private int _maxLabelLength = 40;
        
        [Header("Interaction Settings")]
        [Tooltip("Enable interactive selection")]
        [SerializeField] private bool _enableInteraction = true;
        
        [Tooltip("Selection highlight color")]
        [SerializeField] private Color _selectionColor = new Color(1f, 1f, 0f, 0.3f);
        
        [Tooltip("Hover highlight color")]
        [SerializeField] private Color _hoverColor = new Color(0.8f, 0.8f, 0.8f, 0.2f);
        
        [Tooltip("Label highlight color")]
        [SerializeField] private Color _labelHighlightColor = Color.yellow;
        
        [Header("Animation Settings")]
        [Tooltip("Animate label appearance")]
        [SerializeField] private bool _animateLabels = true;
        
        [Tooltip("Label animation duration")]
        [Range(0.1f, 2f)]
        [SerializeField] private float _labelAnimDuration = 0.5f;
        
        [Tooltip("Animate selection highlight")]
        [SerializeField] private bool _animateSelection = true;
        
        [Tooltip("Selection animation duration")]
        [Range(0.1f, 1f)]
        [SerializeField] private float _selectionAnimDuration = 0.3f;
        
        [Header("Detail Panel")]
        [Tooltip("Detail panel for selected object")]
        [SerializeField] private GameObject _detailPanel;
        
        [Tooltip("Object name text in detail panel")]
        [SerializeField] private TextMeshProUGUI _detailNameText;
        
        [Tooltip("Object class text in detail panel")]
        [SerializeField] private TextMeshProUGUI _detailClassText;
        
        [Tooltip("Object description text in detail panel")]
        [SerializeField] private TextMeshProUGUI _detailDescriptionText;
        
        [Tooltip("Object confidence text in detail panel")]
        [SerializeField] private TextMeshProUGUI _detailConfidenceText;
        
        [Tooltip("Object dimensions text in detail panel")]
        [SerializeField] private TextMeshProUGUI _detailDimensionsText;
        
        [Tooltip("Object preview image in detail panel")]
        [SerializeField] private RawImage _detailPreviewImage;
        
        #endregion
        
        #region Public Properties
        
        /// <summary>
        /// Alpha value for the segmentation overlay.
        /// </summary>
        public float overlayAlpha {
            get => _overlayAlpha;
            set {
                _overlayAlpha = Mathf.Clamp01(value);
                UpdateOverlayAlpha();
            }
        }
        
        /// <summary>
        /// Whether to show object labels.
        /// </summary>
        public bool showLabels {
            get => _showLabels;
            set {
                _showLabels = value;
                UpdateLabelsVisibility();
            }
        }
        
        /// <summary>
        /// Whether to show segmentation outlines.
        /// </summary>
        public bool showOutlines {
            get => _showOutlines;
            set {
                _showOutlines = value;
                RegenerateOverlay();
            }
        }
        
        /// <summary>
        /// Whether to enable interactive selection.
        /// </summary>
        public bool enableInteraction {
            get => _enableInteraction;
            set => _enableInteraction = value;
        }
        
        /// <summary>
        /// Currently selected object.
        /// </summary>
        public DetectedObject selectedObject {
            get => _selectedObject;
        }
        
        #endregion
        #region Private Fields
        
        private TraversifyDebugger _debugger;
        private AnalysisResults _currentResults;
        private Texture2D _baseTexture;
        private Texture2D _overlayTexture;
        private Texture2D _workingOverlay;
        private List<GameObject> _labels = new List<GameObject>();
        private Dictionary<int, GameObject> _labelsByObjectId = new Dictionary<int, GameObject>();
        private Dictionary<int, RectTransform> _objectBoundRects = new Dictionary<int, RectTransform>();
        private DetectedObject _selectedObject;
        private DetectedObject _hoveredObject;
        private int _selectedObjectId = -1;
        private int _hoveredObjectId = -1;
        private bool _isInitialized = false;
        private Vector2 _baseImageSize;
        private Vector2 _displaySize;
        private Vector2 _displayOffset;
        private float _displayScale;
        private bool _isDirty = false;
        private Color[] _originalOverlayPixels;
        private Dictionary<int, Color> _objectColors = new Dictionary<int, Color>();
        private Dictionary<int, Texture2D> _objectPreviews = new Dictionary<int, Texture2D>();
        
        // Events
        public event Action<DetectedObject> OnObjectSelected;
        public event Action<DetectedObject> OnObjectDeselected;
        public event Action<DetectedObject> OnObjectHovered;
        public event Action<DetectedObject> OnObjectClicked;
        
        #endregion
        
        #region Initialization
        
        protected override bool OnInitialize(object config) {
            try {
                _debugger = GetComponent<TraversifyDebugger>();
                if (_debugger == null) {
                    _debugger = gameObject.AddComponent<TraversifyDebugger>();
                }
                
                // Apply config if provided
                if (config != null) {
                    ApplyConfiguration(config);
                }
                
                // Initialize components
                InitializeComponents();
                
                _isInitialized = true;
                Log("SegmentationVisualizer initialized successfully", LogCategory.Visualization);
                return true;
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to initialize SegmentationVisualizer: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Apply configuration from object.
        /// </summary>
        private void ApplyConfiguration(object config) {
            // Handle dictionary config
            if (config is Dictionary<string, object> configDict) {
                // Extract overlay alpha
                if (configDict.TryGetValue("overlayAlpha", out object alphaObj) && alphaObj is float alpha) {
                    _overlayAlpha = Mathf.Clamp01(alpha);
                }
                
                // Extract show outlines
                if (configDict.TryGetValue("showOutlines", out object outlinesObj) && outlinesObj is bool outlines) {
                    _showOutlines = outlines;
                }
                
                // Extract show labels
                if (configDict.TryGetValue("showLabels", out object labelsObj) && labelsObj is bool labels) {
                    _showLabels = labels;
                }
                
                // Extract show class names
                if (configDict.TryGetValue("showClassNames", out object classNamesObj) && classNamesObj is bool classNames) {
                    _showClassNames = classNames;
                }
                
                // Extract show confidence
                if (configDict.TryGetValue("showConfidence", out object confidenceObj) && confidenceObj is bool confidence) {
                    _showConfidence = confidence;
                }
                
                // Extract enable interaction
                if (configDict.TryGetValue("enableInteraction", out object interactionObj) && interactionObj is bool interaction) {
                    _enableInteraction = interaction;
                }
            }
        }
        
        /// <summary>
        /// Initialize UI components.
        /// </summary>
        private void InitializeComponents() {
            // Create label container if not assigned
            if (_labelsContainer == null) {
                GameObject containerObj = new GameObject("LabelsContainer");
                containerObj.transform.SetParent(transform);
                _labelsContainer = containerObj.AddComponent<RectTransform>();
                _labelsContainer.anchorMin = Vector2.zero;
                _labelsContainer.anchorMax = Vector2.one;
                _labelsContainer.offsetMin = Vector2.zero;
                _labelsContainer.offsetMax = Vector2.zero;
            }
            
            // Create base image display if not assigned
            if (_baseImageDisplay == null) {
                GameObject baseImageObj = new GameObject("BaseImage");
                baseImageObj.transform.SetParent(transform);
                _baseImageDisplay = baseImageObj.AddComponent<RawImage>();
                
                RectTransform rectTransform = _baseImageDisplay.GetComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
            }
            
            // Create overlay display if not assigned
            if (_overlayDisplay == null) {
                GameObject overlayObj = new GameObject("OverlayImage");
                overlayObj.transform.SetParent(transform);
                _overlayDisplay = overlayObj.AddComponent<RawImage>();
                
                RectTransform rectTransform = _overlayDisplay.GetComponent<RectTransform>();
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
            }
            
            // Create label prefab if not assigned
            if (_labelPrefab == null) {
                _labelPrefab = CreateDefaultLabelPrefab();
            }
            
            // Create detail panel if not assigned
            if (_detailPanel == null) {
                _detailPanel = CreateDefaultDetailPanel();
            }
            
            // Set initial overlay alpha
            UpdateOverlayAlpha();
            
            // Hide detail panel initially
            if (_detailPanel != null) {
                _detailPanel.SetActive(false);
            }
        }
        
        /// <summary>
        /// Create a default label prefab.
        /// </summary>
        private GameObject CreateDefaultLabelPrefab() {
            GameObject labelObj = new GameObject("LabelPrefab");
            
            // Add TextMeshPro component
            TextMeshProUGUI text = labelObj.AddComponent<TextMeshProUGUI>();
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;
            text.margin = new Vector4(4, 2, 4, 2);
            
            // Add background
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(labelObj.transform);
            
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0, 0, 0, 0.7f);
            
            RectTransform bgRect = bgImage.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            
            // Set bgObj to be first in hierarchy
            bgObj.transform.SetSiblingIndex(0);
            
            // Add button for interaction
            Button button = labelObj.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            
            // Add layout components
            ContentSizeFitter fitter = labelObj.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            // Add outline for better visibility
            Outline outline = labelObj.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1, -1);
            
            return labelObj;
        }
        /// <summary>
        /// Create a default detail panel.
        /// </summary>
        private GameObject CreateDefaultDetailPanel() {
            GameObject panelObj = new GameObject("DetailPanel");
            panelObj.transform.SetParent(transform);
            
            // Add panel components
            Image panelImage = panelObj.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            
            RectTransform panelRect = panelImage.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1, 0);
            panelRect.anchorMax = new Vector2(1, 1);
            panelRect.pivot = new Vector2(1, 0.5f);
            panelRect.sizeDelta = new Vector2(300, 0);
            panelRect.offsetMin = new Vector2(-300, 0);
            panelRect.offsetMax = Vector2.zero;
            
            // Add layout group
            VerticalLayoutGroup layout = panelObj.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            
            // Add title
            GameObject titleObj = new GameObject("Title");
            titleObj.transform.SetParent(panelObj.transform);
            
            _detailNameText = titleObj.AddComponent<TextMeshProUGUI>();
            _detailNameText.fontSize = 18;
            _detailNameText.fontStyle = FontStyles.Bold;
            _detailNameText.color = Color.white;
            _detailNameText.alignment = TextAlignmentOptions.Center;
            _detailNameText.text = "Object Details";
            
            RectTransform titleRect = _detailNameText.GetComponent<RectTransform>();
            titleRect.sizeDelta = new Vector2(0, 30);
            
            // Add preview image
            GameObject previewObj = new GameObject("Preview");
            previewObj.transform.SetParent(panelObj.transform);
            
            _detailPreviewImage = previewObj.AddComponent<RawImage>();
            _detailPreviewImage.color = Color.white;
            
            RectTransform previewRect = _detailPreviewImage.GetComponent<RectTransform>();
            previewRect.sizeDelta = new Vector2(0, 150);
            
            // Add class text
            GameObject classObj = new GameObject("Class");
            classObj.transform.SetParent(panelObj.transform);
            
            _detailClassText = classObj.AddComponent<TextMeshProUGUI>();
            _detailClassText.fontSize = 14;
            _detailClassText.fontStyle = FontStyles.Bold;
            _detailClassText.color = Color.white;
            _detailClassText.alignment = TextAlignmentOptions.Left;
            _detailClassText.text = "Class: ";
            
            RectTransform classRect = _detailClassText.GetComponent<RectTransform>();
            classRect.sizeDelta = new Vector2(0, 20);
            
            // Add confidence text
            GameObject confidenceObj = new GameObject("Confidence");
            confidenceObj.transform.SetParent(panelObj.transform);
            
            _detailConfidenceText = confidenceObj.AddComponent<TextMeshProUGUI>();
            _detailConfidenceText.fontSize = 14;
            _detailConfidenceText.color = Color.white;
            _detailConfidenceText.alignment = TextAlignmentOptions.Left;
            _detailConfidenceText.text = "Confidence: ";
            
            RectTransform confidenceRect = _detailConfidenceText.GetComponent<RectTransform>();
            confidenceRect.sizeDelta = new Vector2(0, 20);
            
            // Add dimensions text
            GameObject dimensionsObj = new GameObject("Dimensions");
            dimensionsObj.transform.SetParent(panelObj.transform);
            
            _detailDimensionsText = dimensionsObj.AddComponent<TextMeshProUGUI>();
            _detailDimensionsText.fontSize = 14;
            _detailDimensionsText.color = Color.white;
            _detailDimensionsText.alignment = TextAlignmentOptions.Left;
            _detailDimensionsText.text = "Dimensions: ";
            
            RectTransform dimensionsRect = _detailDimensionsText.GetComponent<RectTransform>();
            dimensionsRect.sizeDelta = new Vector2(0, 20);
            
            // Add description text
            GameObject descriptionObj = new GameObject("Description");
            descriptionObj.transform.SetParent(panelObj.transform);
            
            _detailDescriptionText = descriptionObj.AddComponent<TextMeshProUGUI>();
            _detailDescriptionText.fontSize = 14;
            _detailDescriptionText.color = Color.white;
            _detailDescriptionText.alignment = TextAlignmentOptions.Left;
            _detailDescriptionText.enableWordWrapping = true;
            _detailDescriptionText.text = "Description: ";
            
            RectTransform descriptionRect = _detailDescriptionText.GetComponent<RectTransform>();
            descriptionRect.sizeDelta = new Vector2(0, 100);
            
            // Add close button
            GameObject closeButtonObj = new GameObject("CloseButton");
            closeButtonObj.transform.SetParent(panelObj.transform);
            
            Button closeButton = closeButtonObj.AddComponent<Button>();
            Image closeButtonImage = closeButtonObj.AddComponent<Image>();
            closeButtonImage.color = new Color(0.7f, 0.2f, 0.2f, 1f);
            
            TextMeshProUGUI closeButtonText = new GameObject("Text").AddComponent<TextMeshProUGUI>();
            closeButtonText.transform.SetParent(closeButtonObj.transform);
            closeButtonText.text = "Close";
            closeButtonText.fontSize = 14;
            closeButtonText.fontStyle = FontStyles.Bold;
            closeButtonText.color = Color.white;
            closeButtonText.alignment = TextAlignmentOptions.Center;
            
            RectTransform closeButtonTextRect = closeButtonText.GetComponent<RectTransform>();
            closeButtonTextRect.anchorMin = Vector2.zero;
            closeButtonTextRect.anchorMax = Vector2.one;
            closeButtonTextRect.offsetMin = Vector2.zero;
            closeButtonTextRect.offsetMax = Vector2.zero;
            
            RectTransform closeButtonRect = closeButtonObj.GetComponent<RectTransform>();
            closeButtonRect.sizeDelta = new Vector2(0, 40);
            
            // Add button callback
            closeButton.onClick.AddListener(() => {
                ClearSelection();
                if (_detailPanel != null) {
                    _detailPanel.SetActive(false);
                }
            });
            
            return panelObj;
        }
        
        private void Update() {
            // Check if overlay needs to be regenerated
            if (_isDirty) {
                RegenerateOverlay();
                _isDirty = false;
            }
        }
        
        private void OnDisable() {
            ClearVisualization();
        }
        
        private void OnDestroy() {
            ClearVisualization();
        }
        
        #endregion
        
        #region Visualization Methods
        
        /// <summary>
        /// Visualize analysis results.
        /// </summary>
        public void VisualizeResults(AnalysisResults results, bool clearPrevious = true) {
            if (results == null) {
                LogError("Cannot visualize null results", LogCategory.Visualization);
                return;
            }
            
            try {
                float startTime = Time.realtimeSinceStartup;
                
                // Store results
                _currentResults = results;
                
                // Clear previous visualization if requested
                if (clearPrevious) {
                    ClearVisualization();
                }
                
                // Set base image
                if (results.sourceImage != null) {
                    _baseTexture = results.sourceImage;
                    _baseImageDisplay.texture = _baseTexture;
                    _baseImageSize = new Vector2(_baseTexture.width, _baseTexture.height);
                    
                    // Update layout to maintain aspect ratio
                    UpdateDisplayLayout();
                }
                
                // Set overlay
                if (results.segmentationOverlay != null) {
                    _overlayTexture = results.segmentationOverlay;
                    _overlayDisplay.texture = _overlayTexture;
                    
                    // Store original pixels for manipulation
                    _originalOverlayPixels = _overlayTexture.GetPixels();
                    
                    // Apply initial alpha
                    UpdateOverlayAlpha();
                }
                else {
                    // Generate overlay from detected objects
                    GenerateOverlayFromObjects(results.detectedObjects);
                }
                
                // Generate object colors
                GenerateObjectColors(results.detectedObjects);
                
                // Create object previews
                GenerateObjectPreviews(results.detectedObjects);
                
                // Create labels if enabled
                if (_showLabels) {
                    StartCoroutine(CreateLabelsForObjects(results.detectedObjects));
                }
                
                float visualizeTime = Time.realtimeSinceStartup - startTime;
                Log($"Visualization completed in {visualizeTime:F2} seconds", LogCategory.Visualization);
            }
            catch (Exception ex) {
                LogError($"Error visualizing results: {ex.Message}", LogCategory.Visualization);
            }
        }
        /// <summary>
        /// Clear current visualization.
        /// </summary>
        public void ClearVisualization() {
            // Clear selection
            ClearSelection();
            
            // Clear hover
            ClearHover();
            
            // Clear labels
            ClearLabels();
            
            // Clear textures
            _baseTexture = null;
            _overlayTexture = null;
            _workingOverlay = null;
            _originalOverlayPixels = null;
            
            // Clear display
            if (_baseImageDisplay != null) {
                _baseImageDisplay.texture = null;
            }
            
            if (_overlayDisplay != null) {
                _overlayDisplay.texture = null;
            }
            
            // Clear object data
            _objectColors.Clear();
            _objectPreviews.Clear();
            
            // Hide detail panel
            if (_detailPanel != null) {
                _detailPanel.SetActive(false);
            }
            
            // Reset state
            _currentResults = null;
            _isDirty = false;
        }
        
        /// <summary>
        /// Generate overlay from detected objects.
        /// </summary>
        private void GenerateOverlayFromObjects(List<DetectedObject> objects) {
            if (objects == null || objects.Count == 0 || _baseTexture == null) {
                return;
            }
            
            try {
                // Create new overlay texture
                _workingOverlay = new Texture2D(_baseTexture.width, _baseTexture.height, TextureFormat.RGBA32, false);
                
                // Fill with transparent color
                Color[] transparentPixels = new Color[_workingOverlay.width * _workingOverlay.height];
                for (int i = 0; i < transparentPixels.Length; i++) {
                    transparentPixels[i] = Color.clear;
                }
                _workingOverlay.SetPixels(transparentPixels);
                
                // Store original pixels
                _originalOverlayPixels = new Color[transparentPixels.Length];
                Array.Copy(transparentPixels, _originalOverlayPixels, transparentPixels.Length);
                
                // Apply segmentation masks with different colors
                for (int i = 0; i < objects.Count; i++) {
                    var obj = objects[i];
                    
                    // Skip if no segments
                    if (obj.segments == null || obj.segments.Count == 0) {
                        continue;
                    }
                    
                    // Generate color for this object
                    Color objectColor = GenerateObjectColor(obj, i);
                    
                    // Get segment mask
                    foreach (var segment in obj.segments) {
                        if (segment.maskTexture == null) continue;
                        
                        // Apply segment to overlay
                        ApplySegmentToOverlay(_workingOverlay, segment.maskTexture, objectColor);
                    }
                }
                
                // Apply outlines if enabled
                if (_showOutlines) {
                    ApplyOutlinesToOverlay(_workingOverlay, objects);
                }
                
                // Apply changes
                _workingOverlay.Apply();
                
                // Set as overlay
                _overlayTexture = _workingOverlay;
                _overlayDisplay.texture = _overlayTexture;
                
                // Apply alpha
                UpdateOverlayAlpha();
            }
            catch (Exception ex) {
                LogError($"Error generating overlay: {ex.Message}", LogCategory.Visualization);
            }
        }
        
        /// <summary>
        /// Apply segment mask to overlay texture.
        /// </summary>
        private void ApplySegmentToOverlay(Texture2D overlay, Texture2D mask, Color segmentColor) {
            // Ensure same dimensions
            if (mask.width != overlay.width || mask.height != overlay.height) {
                mask = ResizeTexture(mask, overlay.width, overlay.height);
            }
            
            // Get pixels
            Color[] overlayPixels = overlay.GetPixels();
            Color[] maskPixels = mask.GetPixels();
            
            // Apply mask
            for (int i = 0; i < overlayPixels.Length; i++) {
                float maskValue = maskPixels[i].r;
                if (maskValue > 0.5f) { // Threshold
                    // Apply segment color with transparency
                    Color color = segmentColor;
                    color.a = color.a * maskValue;
                    
                    // Alpha blend
                    overlayPixels[i] = Color.Lerp(overlayPixels[i], color, color.a);
                }
            }
            
            overlay.SetPixels(overlayPixels);
        }
        
        /// <summary>
        /// Apply outlines to overlay texture.
        /// </summary>
        private void ApplyOutlinesToOverlay(Texture2D overlay, List<DetectedObject> objects) {
            // Get pixels
            Color[] overlayPixels = overlay.GetPixels();
            
            // Apply outlines for each object
            foreach (var obj in objects) {
                // Skip if no segments
                if (obj.segments == null || obj.segments.Count == 0) {
                    continue;
                }
                
                foreach (var segment in obj.segments) {
                    if (segment.contourPoints == null || segment.contourPoints.Count < 2) {
                        continue;
                    }
                    
                    // Draw contour lines
                    for (int i = 0; i < segment.contourPoints.Count; i++) {
                        Vector2 p1 = segment.contourPoints[i];
                        Vector2 p2 = segment.contourPoints[(i + 1) % segment.contourPoints.Count];
                        
                        DrawLine(overlay, overlayPixels, p1, p2, _outlineColor, _outlineThickness);
                    }
                }
            }
            
            // Update texture
            overlay.SetPixels(overlayPixels);
        }
        
        /// <summary>
        /// Draw a line on the texture.
        /// </summary>
        private void DrawLine(Texture2D texture, Color[] pixels, Vector2 start, Vector2 end, Color color, float thickness) {
            int width = texture.width;
            int height = texture.height;
            
            // Bresenham's line algorithm
            int x0 = Mathf.RoundToInt(start.x);
            int y0 = Mathf.RoundToInt(start.y);
            int x1 = Mathf.RoundToInt(end.x);
            int y1 = Mathf.RoundToInt(end.y);
            
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            
            while (true) {
                // Draw pixel at current position with thickness
                for (int tx = -Mathf.FloorToInt(thickness); tx <= Mathf.CeilToInt(thickness); tx++) {
                    for (int ty = -Mathf.FloorToInt(thickness); ty <= Mathf.CeilToInt(thickness); ty++) {
                        int px = x0 + tx;
                        int py = y0 + ty;
                        
                        // Check if within bounds
                        if (px >= 0 && px < width && py >= 0 && py < height) {
                            int index = py * width + px;
                            
                            // Only draw if within thickness radius
                            if (tx * tx + ty * ty <= thickness * thickness) {
                                // Alpha blend
                                pixels[index] = Color.Lerp(pixels[index], color, color.a);
                            }
                        }
                    }
                }
                
                // Exit if reached endpoint
                if (x0 == x1 && y0 == y1) break;
                
                // Calculate next position
                int e2 = 2 * err;
                if (e2 > -dy) {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx) {
                    err += dx;
                    y0 += sy;
                }
            }
        }
        
        /// <summary>
        /// Generate colors for detected objects.
        /// </summary>
        private void GenerateObjectColors(List<DetectedObject> objects) {
            _objectColors.Clear();
            
            for (int i = 0; i < objects.Count; i++) {
                var obj = objects[i];
                Color color = GenerateObjectColor(obj, i);
                _objectColors[obj.id] = color;
            }
        }
        /// <summary>
        /// Generate a color for an object.
        /// </summary>
        private Color GenerateObjectColor(DetectedObject obj, int index) {
            // If object already has a color, use it
            if (obj.color != Color.clear) {
                return new Color(obj.color.r, obj.color.g, obj.color.b, _overlayAlpha);
            }
            
            // Different color schemes for different object types
            float hue;
            float saturation;
            float value;
            
            if (obj.isTerrain) {
                // Terrain color scheme (natural colors)
                switch (obj.className.ToLower()) {
                    case "water":
                    case "lake":
                    case "river":
                    case "ocean":
                        hue = 0.6f; // Blue
                        saturation = 0.8f;
                        value = 0.9f;
                        break;
                    case "mountain":
                    case "hill":
                        hue = 0.1f; // Brown
                        saturation = 0.5f;
                        value = 0.6f;
                        break;
                    case "forest":
                    case "tree":
                    case "woods":
                        hue = 0.3f; // Green
                        saturation = 0.7f;
                        value = 0.7f;
                        break;
                    case "grass":
                    case "grassland":
                    case "plain":
                        hue = 0.25f; // Light green
                        saturation = 0.6f;
                        value = 0.8f;
                        break;
                    case "sand":
                    case "desert":
                    case "beach":
                        hue = 0.12f; // Tan
                        saturation = 0.4f;
                        value = 0.9f;
                        break;
                    case "snow":
                    case "ice":
                        hue = 0.0f; // White-blue
                        saturation = 0.1f;
                        value = 1.0f;
                        break;
                    default:
                        // Use hue based on terrain name hash
                        hue = (obj.className.GetHashCode() % 100) / 100f;
                        saturation = 0.5f;
                        value = 0.8f;
                        break;
                }
            }
            else if (obj.isManMade) {
                // Man-made objects (more vibrant colors)
                hue = ((index * 37) % 360) / 360f; // Use prime number for better distribution
                saturation = 0.7f;
                value = 0.9f;
            }
            else {
                // Natural objects (more muted colors)
                hue = ((index * 41) % 360) / 360f; // Use different prime number
                saturation = 0.6f;
                value = 0.8f;
            }
            
            // Create color with alpha
            Color color = Color.HSVToRGB(hue, saturation, value);
            color.a = _overlayAlpha;
            
            return color;
        }
        
        /// <summary>
        /// Generate preview textures for objects.
        /// </summary>
        private void GenerateObjectPreviews(List<DetectedObject> objects) {
            _objectPreviews.Clear();
            
            if (_baseTexture == null) return;
            
            foreach (var obj in objects) {
                // Skip if bounding box is invalid
                if (obj.boundingBox.width <= 0 || obj.boundingBox.height <= 0) {
                    continue;
                }
                
                // Extract region from base image
                Texture2D preview = ExtractPreviewTexture(obj);
                if (preview != null) {
                    _objectPreviews[obj.id] = preview;
                }
            }
        }
        
        /// <summary>
        /// Extract preview texture for an object.
        /// </summary>
        private Texture2D ExtractPreviewTexture(DetectedObject obj) {
            try {
                // Calculate region
                int x = Mathf.FloorToInt(obj.boundingBox.x);
                int y = Mathf.FloorToInt(obj.boundingBox.y);
                int width = Mathf.CeilToInt(obj.boundingBox.width);
                int height = Mathf.CeilToInt(obj.boundingBox.height);
                
                // Ensure within bounds
                x = Mathf.Clamp(x, 0, _baseTexture.width - 1);
                y = Mathf.Clamp(y, 0, _baseTexture.height - 1);
                width = Mathf.Clamp(width, 1, _baseTexture.width - x);
                height = Mathf.Clamp(height, 1, _baseTexture.height - y);
                
                // Create texture
                Texture2D preview = new Texture2D(width, height, TextureFormat.RGBA32, false);
                
                // Copy pixels
                Color[] pixels = _baseTexture.GetPixels(x, _baseTexture.height - y - height, width, height);
                preview.SetPixels(pixels);
                preview.Apply();
                
                return preview;
            }
            catch (Exception ex) {
                LogError($"Error extracting preview: {ex.Message}", LogCategory.Visualization);
                return null;
            }
        }
        
        /// <summary>
        /// Create labels for detected objects.
        /// </summary>
        private IEnumerator CreateLabelsForObjects(List<DetectedObject> objects) {
            // Clear existing labels
            ClearLabels();
            
            // Wait a frame for layout to update
            yield return null;
            
            // Calculate display scale and offset
            UpdateDisplayLayout();
            
            // Create labels
            float delay = _animateLabels ? _labelAnimDuration / objects.Count : 0;
            
            for (int i = 0; i < objects.Count; i++) {
                var obj = objects[i];
                
                // Skip if bounding box is invalid
                if (obj.boundingBox.width <= 0 || obj.boundingBox.height <= 0) {
                    continue;
                }
                
                // Create label
                GameObject labelObj = CreateLabelForObject(obj);
                
                // Animate if enabled
                if (_animateLabels) {
                    // Start with zero scale
                    labelObj.transform.localScale = Vector3.zero;
                    
                    // Animate to full scale
                    StartCoroutine(AnimateLabel(labelObj, delay * i));
                }
                
                // Add to lists
                _labels.Add(labelObj);
                _labelsByObjectId[obj.id] = labelObj;
                
                // Yield for animation
                if (delay > 0) {
                    yield return new WaitForSeconds(delay);
                }
            }
        }
        
        /// <summary>
        /// Create a label for an object.
        /// </summary>
        private GameObject CreateLabelForObject(DetectedObject obj) {
            // Instantiate label prefab
            GameObject labelObj = Instantiate(_labelPrefab, _labelsContainer);
            
            // Get components
            TextMeshProUGUI text = labelObj.GetComponent<TextMeshProUGUI>();
            RectTransform rect = labelObj.GetComponent<RectTransform>();
            Button button = labelObj.GetComponent<Button>();
            
            // Set text
            string labelText = GetLabelText(obj);
            text.text = labelText;
            
            // Set position
            Vector2 screenPos = ConvertToScreenSpace(obj.boundingBox.center);
            rect.anchoredPosition = screenPos;
            
            // Set name
            labelObj.name = $"Label_{obj.id}_{obj.className}";
            
            // Set color
            if (_objectColors.TryGetValue(obj.id, out Color color)) {
                Image background = labelObj.GetComponentInChildren<Image>();
                if (background != null) {
                    background.color = new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, 0.8f);
                }
            }
            
            // Add click handler
            if (button != null) {
                button.onClick.AddListener(() => {
                    OnLabelClicked(obj.id);
                });
            }
            
            // Create bounding rect for interaction
            CreateBoundingRect(obj);
            
            return labelObj;
        }
        /// <summary>
        /// Create a bounding rect for object interaction.
        /// </summary>
        private void CreateBoundingRect(DetectedObject obj) {
            // Create game object
            GameObject boundObj = new GameObject($"Bound_{obj.id}");
            boundObj.transform.SetParent(_labelsContainer);
            
            // Add components
            RectTransform rect = boundObj.AddComponent<RectTransform>();
            Image image = boundObj.AddComponent<Image>();
            
            // Make transparent
            image.color = Color.clear;
            
            // Set position and size
            Vector2 screenMin = ConvertToScreenSpace(obj.boundingBox.min);
            Vector2 screenMax = ConvertToScreenSpace(obj.boundingBox.max);
            
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = screenMin;
            rect.sizeDelta = screenMax - screenMin;
            
            // Add event triggers
            EventTrigger trigger = boundObj.AddComponent<EventTrigger>();
            
            // Add pointer enter event
            EventTrigger.Entry enterEntry = new EventTrigger.Entry();
            enterEntry.eventID = EventTriggerType.PointerEnter;
            enterEntry.callback.AddListener((data) => { OnObjectHover(obj.id); });
            trigger.triggers.Add(enterEntry);
            
            // Add pointer exit event
            EventTrigger.Entry exitEntry = new EventTrigger.Entry();
            exitEntry.eventID = EventTriggerType.PointerExit;
            exitEntry.callback.AddListener((data) => { OnObjectUnhover(obj.id); });
            trigger.triggers.Add(exitEntry);
            
            // Add pointer click event
            EventTrigger.Entry clickEntry = new EventTrigger.Entry();
            clickEntry.eventID = EventTriggerType.PointerClick;
            clickEntry.callback.AddListener((data) => { OnObjectClick(obj.id); });
            trigger.triggers.Add(clickEntry);
            
            // Store in dictionary
            _objectBoundRects[obj.id] = rect;
        }
        
        /// <summary>
        /// Get text for object label.
        /// </summary>
        private string GetLabelText(DetectedObject obj) {
            string labelText = "";
            
            // Add class name
            if (_showClassNames) {
                labelText += obj.className;
            }
            
            // Add confidence
            if (_showConfidence && obj.confidence > 0) {
                if (labelText.Length > 0) {
                    labelText += " ";
                }
                labelText += $"({obj.confidence:P0})";
            }
            
            // Add object ID
            if (_showObjectIds) {
                if (labelText.Length > 0) {
                    labelText += " ";
                }
                labelText += $"[{obj.id}]";
            }
            
            // Truncate if too long
            if (labelText.Length > _maxLabelLength) {
                labelText = labelText.Substring(0, _maxLabelLength - 3) + "...";
            }
            
            return labelText;
        }
        
        /// <summary>
        /// Animate a label appearing.
        /// </summary>
        private IEnumerator AnimateLabel(GameObject label, float delay) {
            // Wait for delay
            yield return new WaitForSeconds(delay);
            
            // Animate scale
            float startTime = Time.time;
            float duration = _labelAnimDuration * 0.5f; // Half the total animation time
            
            while (Time.time - startTime < duration) {
                float t = (Time.time - startTime) / duration;
                t = Mathf.SmoothStep(0, 1, t); // Smooth animation
                
                label.transform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, t);
                
                yield return null;
            }
            
            // Ensure final scale
            label.transform.localScale = Vector3.one;
        }
        
        /// <summary>
        /// Clear all labels.
        /// </summary>
        private void ClearLabels() {
            // Destroy label objects
            foreach (var label in _labels) {
                if (label != null) {
                    Destroy(label);
                }
            }
            
            // Destroy bounding rects
            foreach (var pair in _objectBoundRects) {
                if (pair.Value != null) {
                    Destroy(pair.Value.gameObject);
                }
            }
            
            // Clear collections
            _labels.Clear();
            _labelsByObjectId.Clear();
            _objectBoundRects.Clear();
        }
        
        /// <summary>
        /// Update labels visibility.
        /// </summary>
        private void UpdateLabelsVisibility() {
            if (_labelsContainer != null) {
                _labelsContainer.gameObject.SetActive(_showLabels);
            }
        }
        
        /// <summary>
        /// Update overlay alpha.
        /// </summary>
        private void UpdateOverlayAlpha() {
            if (_overlayDisplay != null) {
                Color color = _overlayDisplay.color;
                color.a = _overlayAlpha;
                _overlayDisplay.color = color;
            }
        }
        
        /// <summary>
        /// Regenerate overlay texture.
        /// </summary>
        private void RegenerateOverlay() {
            if (_currentResults == null || _currentResults.detectedObjects == null) {
                return;
            }
            
            GenerateOverlayFromObjects(_currentResults.detectedObjects);
        }
        
        #endregion
        
        #region Interaction Handling
        
        /// <summary>
        /// Handle pointer click event.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData) {
            if (!_enableInteraction) return;
            
            // Convert to local position
            Vector2 localPoint;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)transform, eventData.position, eventData.pressEventCamera, out localPoint);
            
            // Convert to image space
            Vector2 imagePoint = ConvertToImageSpace(localPoint);
            
            // Find object at this position
            int objectId = FindObjectAtPosition(imagePoint);
            
            if (objectId >= 0) {
                OnObjectClick(objectId);
            }
            else {
                ClearSelection();
                
                // Hide detail panel
                if (_detailPanel != null) {
                    _detailPanel.SetActive(false);
                }
            }
        }
        
        /// <summary>
        /// Handle pointer enter event.
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData) {
            // Not used for the main component
        }
        
        /// <summary>
        /// Handle pointer exit event.
        /// </summary>
        public void OnPointerExit(PointerEventData eventData) {
            // Clear hover if not on a specific object
            ClearHover();
        }
        /// <summary>
        /// Handle object hover.
        /// </summary>
        private void OnObjectHover(int objectId) {
            if (!_enableInteraction) return;
            
            // Skip if same as current
            if (objectId == _hoveredObjectId) return;
            
            // Clear previous hover
            ClearHover();
            
            // Find object
            DetectedObject obj = FindObjectById(objectId);
            if (obj == null) return;
            
            // Store hover
            _hoveredObjectId = objectId;
            _hoveredObject = obj;
            
            // Highlight in overlay
            HighlightObjectInOverlay(obj, _hoverColor);
            
            // Highlight label
            HighlightLabel(objectId, true);
            
            // Trigger event
            OnObjectHovered?.Invoke(obj);
        }
        
        /// <summary>
        /// Handle object unhover.
        /// </summary>
        private void OnObjectUnhover(int objectId) {
            if (!_enableInteraction) return;
            
            // Only clear if this is the hovered object
            if (objectId != _hoveredObjectId) return;
            
            ClearHover();
        }
        
        /// <summary>
        /// Clear hover state.
        /// </summary>
        private void ClearHover() {
            if (_hoveredObjectId < 0) return;
            
            // Restore overlay
            RegenerateOverlay();
            
            // Unhighlight label
            HighlightLabel(_hoveredObjectId, false);
            
            // Clear state
            _hoveredObjectId = -1;
            _hoveredObject = null;
        }
        
        /// <summary>
        /// Handle object click.
        /// </summary>
        private void OnObjectClick(int objectId) {
            if (!_enableInteraction) return;
            
            // Find object
            DetectedObject obj = FindObjectById(objectId);
            if (obj == null) return;
            
            // Toggle selection
            if (objectId == _selectedObjectId) {
                ClearSelection();
            }
            else {
                SelectObject(objectId);
            }
            
            // Trigger event
            OnObjectClicked?.Invoke(obj);
        }
        
        /// <summary>
        /// Handle label click.
        /// </summary>
        private void OnLabelClicked(int objectId) {
            if (!_enableInteraction) return;
            
            // Same as object click
            OnObjectClick(objectId);
        }
        
        /// <summary>
        /// Select an object.
        /// </summary>
        public void SelectObject(int objectId) {
            if (!_enableInteraction) return;
            
            // Clear previous selection
            ClearSelection();
            
            // Find object
            DetectedObject obj = FindObjectById(objectId);
            if (obj == null) return;
            
            // Store selection
            _selectedObjectId = objectId;
            _selectedObject = obj;
            
            // Highlight in overlay
            HighlightObjectInOverlay(obj, _selectionColor);
            
            // Highlight label
            HighlightLabel(objectId, true);
            
            // Show detail panel
            ShowObjectDetails(obj);
            
            // Trigger event
            OnObjectSelected?.Invoke(obj);
        }
        
        /// <summary>
        /// Clear selection.
        /// </summary>
        public void ClearSelection() {
            if (_selectedObjectId < 0) return;
            
            // Restore overlay
            RegenerateOverlay();
            
            // Unhighlight label
            HighlightLabel(_selectedObjectId, false);
            
            // Trigger event
            if (_selectedObject != null) {
                OnObjectDeselected?.Invoke(_selectedObject);
            }
            
            // Clear state
            _selectedObjectId = -1;
            _selectedObject = null;
        }
        
         /// <summary>
        /// Highlight object in overlay.
        /// </summary>
        private void HighlightObjectInOverlay(DetectedObject obj, Color highlightColor) {
            if (_overlayTexture == null || _originalOverlayPixels == null) return;
            
            try {
                // Make a copy of the original pixels
                Color[] pixels = new Color[_originalOverlayPixels.Length];
                Array.Copy(_originalOverlayPixels, pixels, _originalOverlayPixels.Length);
                
                // Apply segments with highlight color
                if (obj.segments != null && obj.segments.Count > 0) {
                    foreach (var segment in obj.segments) {
                        if (segment.maskTexture == null) continue;
                        
                        // Ensure same dimensions
                        Texture2D mask = segment.maskTexture;
                        if (mask.width != _overlayTexture.width || mask.height != _overlayTexture.height) {
                            mask = ResizeTexture(mask, _overlayTexture.width, _overlayTexture.height);
                        }
                        
                        // Get mask pixels
                        Color[] maskPixels = mask.GetPixels();
                        
                        // Apply highlight
                        for (int i = 0; i < pixels.Length; i++) {
                            float maskValue = maskPixels[i].r;
                            if (maskValue > 0.5f) { // Threshold
                                // Apply highlight color
                                Color color = highlightColor;
                                color.a = color.a * maskValue;
                                
                                // Alpha blend
                                pixels[i] = Color.Lerp(pixels[i], color, color.a);
                            }
                        }
                    }
                }
                else {
                    // Use bounding box if no segments
                    int x = Mathf.FloorToInt(obj.boundingBox.x);
                    int y = Mathf.FloorToInt(obj.boundingBox.y);
                    int width = Mathf.CeilToInt(obj.boundingBox.width);
                    int height = Mathf.CeilToInt(obj.boundingBox.height);
                    
                    // Ensure within bounds
                    x = Mathf.Clamp(x, 0, _overlayTexture.width - 1);
                    y = Mathf.Clamp(y, 0, _overlayTexture.height - 1);
                    width = Mathf.Clamp(width, 1, _overlayTexture.width - x);
                    height = Mathf.Clamp(height, 1, _overlayTexture.height - y);
                    
                    // Apply highlight to bounding box
                    for (int py = y; py < y + height; py++) {
                        for (int px = x; px < x + width; px++) {
                            int index = py * _overlayTexture.width + px;
                            if (index >= 0 && index < pixels.Length) {
                                // Alpha blend
                                pixels[index] = Color.Lerp(pixels[index], highlightColor, highlightColor.a);
                            }
                        }
                    }
                }
                
                // Apply to texture
                _overlayTexture.SetPixels(pixels);
                _overlayTexture.Apply();
            }
            catch (Exception ex) {
                LogError($"Error highlighting object: {ex.Message}", LogCategory.Visualization);
            }
        }
        
        /// <summary>
        /// Highlight a label.
        /// </summary>
        private void HighlightLabel(int objectId, bool highlight) {
            if (!_labelsByObjectId.TryGetValue(objectId, out GameObject labelObj)) {
                return;
            }
            
            // Get text component
            TextMeshProUGUI text = labelObj.GetComponent<TextMeshProUGUI>();
            if (text == null) return;
            
            // Apply highlight
            if (highlight) {
                text.color = _labelHighlightColor;
                text.fontStyle = FontStyles.Bold;
                
                // Bring to front
                labelObj.transform.SetAsLastSibling();
                
                // Scale up slightly
                if (_animateSelection) {
                    StartCoroutine(AnimateLabelScale(labelObj, 1.2f));
                }
                else {
                    labelObj.transform.localScale = Vector3.one * 1.2f;
                }
            }
            else {
                text.color = Color.white;
                text.fontStyle = FontStyles.Normal;
                
                // Reset scale
                if (_animateSelection) {
                    StartCoroutine(AnimateLabelScale(labelObj, 1.0f));
                }
                else {
                    labelObj.transform.localScale = Vector3.one;
                }
            }
        }
        
        /// <summary>
        /// Animate label scale.
        /// </summary>
        private IEnumerator AnimateLabelScale(GameObject label, float targetScale) {
            float startTime = Time.time;
            float duration = _selectionAnimDuration;
            Vector3 startScale = label.transform.localScale;
            Vector3 targetVector = Vector3.one * targetScale;
            
            while (Time.time - startTime < duration) {
                float t = (Time.time - startTime) / duration;
                t = Mathf.SmoothStep(0, 1, t); // Smooth animation
                
                label.transform.localScale = Vector3.Lerp(startScale, targetVector, t);
                
                yield return null;
            }
            
            // Ensure final scale
            label.transform.localScale = targetVector;
        }
        
        /// <summary>
        /// Show object details in panel.
        /// </summary>
        private void ShowObjectDetails(DetectedObject obj) {
            if (_detailPanel == null) return;
            
            // Activate panel
            _detailPanel.SetActive(true);
            
            // Set title
            if (_detailNameText != null) {
                _detailNameText.text = obj.className;
            }
            
            // Set class
            if (_detailClassText != null) {
                string classInfo = $"Class: {obj.className}";
                if (obj.isTerrain) classInfo += " (Terrain)";
                if (obj.isManMade) classInfo += " (Man-made)";
                _detailClassText.text = classInfo;
            }
            
            // Set confidence
            if (_detailConfidenceText != null) {
                _detailConfidenceText.text = $"Confidence: {obj.confidence:P0}";
            }
            
            // Set dimensions
            if (_detailDimensionsText != null) {
                _detailDimensionsText.text = $"Dimensions: {obj.boundingBox.width:F0}x{obj.boundingBox.height:F0}";
            }
            
            // Set description
            if (_detailDescriptionText != null) {
                string description = obj.enhancedDescription;
                if (string.IsNullOrEmpty(description)) {
                    description = obj.shortDescription;
                }
                if (string.IsNullOrEmpty(description)) {
                    description = $"A {obj.className} object.";
                }
                _detailDescriptionText.text = $"Description: {description}";
            }
            
            // Set preview image
            if (_detailPreviewImage != null) {
                if (_objectPreviews.TryGetValue(obj.id, out Texture2D preview)) {
                    _detailPreviewImage.texture = preview;
                    _detailPreviewImage.gameObject.SetActive(true);
                }
                else {
                    _detailPreviewImage.gameObject.SetActive(false);
                }
            }
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Find object by ID.
        /// </summary>
        private DetectedObject FindObjectById(int id) {
            if (_currentResults == null || _currentResults.detectedObjects == null) {
                return null;
            }
            
            return _currentResults.detectedObjects.Find(obj => obj.id == id);
        }
        
        /// <summary>
        /// Find object at position.
        /// </summary>
        private int FindObjectAtPosition(Vector2 position) {
            if (_currentResults == null || _currentResults.detectedObjects == null) {
                return -1;
            }
            
            // Check each object's segments
            foreach (var obj in _currentResults.detectedObjects) {
                // Check segments first
                if (obj.segments != null && obj.segments.Count > 0) {
                    foreach (var segment in obj.segments) {
                        if (segment.contourPoints != null && segment.contourPoints.Count >= 3) {
                            if (PointInPolygon(position, segment.contourPoints)) {
                                return obj.id;
                            }
                        }
                    }
                }
                
                // Fallback to bounding box
                if (obj.boundingBox.Contains(position)) {
                    return obj.id;
                }
            }
            
            return -1;
        }
        
        /// <summary>
        /// Check if point is inside polygon.
        /// </summary>
        private bool PointInPolygon(Vector2 point, List<Vector2> polygon) {
            int polygonLength = polygon.Count, i = 0;
            bool inside = false;
            
            // x, y for tested point.
            float pointX = point.x, pointY = point.y;
            
            // Start with the last vertex in polygon.
            float startX = polygon[polygonLength - 1].x, startY = polygon[polygonLength - 1].y;
            
            // Loop through polygon vertices.
            for (i = 0; i < polygonLength; i++) {
                // End point of current polygon segment.
                float endX = polygon[i].x, endY = polygon[i].y;
                
                // Connect current and previous vertices.
                bool intersect = ((startY > pointY) != (endY > pointY)) 
                    && (pointX < (endX - startX) * (pointY - startY) / (endY - startY) + startX);
                
                if (intersect) inside = !inside;
                
                // Next line segment starts from this vertex.
                startX = endX;
                startY = endY;
            }
            
            return inside;
        }
        /// <summary>
        /// Update display layout.
        /// </summary>
        private void UpdateDisplayLayout() {
            if (_baseImageDisplay == null) return;
            
            // Get display rect
            RectTransform displayRect = _baseImageDisplay.GetComponent<RectTransform>();
            Rect rect = displayRect.rect;
            
            // Calculate display size and scale
            _displaySize = new Vector2(rect.width, rect.height);
            _displayOffset = new Vector2(rect.x, rect.y);
            
            // Calculate scale to fit image in display
            float scaleX = _displaySize.x / _baseImageSize.x;
            float scaleY = _displaySize.y / _baseImageSize.y;
            _displayScale = Mathf.Min(scaleX, scaleY);
            
            // Update base image aspect ratio
            if (_baseTexture != null) {
                _baseImageDisplay.SetNativeSize();
                _baseImageDisplay.transform.localScale = Vector3.one * _displayScale;
            }
            
            // Update overlay to match
            if (_overlayDisplay != null) {
                _overlayDisplay.SetNativeSize();
                _overlayDisplay.transform.localScale = Vector3.one * _displayScale;
            }
        }
        
        /// <summary>
        /// Convert screen space to image space.
        /// </summary>
        private Vector2 ConvertToImageSpace(Vector2 screenPoint) {
            // Adjust for display offset and scale
            Vector2 normalizedPoint = screenPoint;
            normalizedPoint /= _displayScale;
            
            // Flip Y (Unity UI has Y=0 at top, image has Y=0 at bottom)
            normalizedPoint.y = _baseImageSize.y - normalizedPoint.y;
            
            return normalizedPoint;
        }
        
        /// <summary>
        /// Convert image space to screen space.
        /// </summary>
        private Vector2 ConvertToScreenSpace(Vector2 imagePoint) {
            // Convert image coordinates to screen space
            Vector2 screenPoint = imagePoint;
            
            // Flip Y (Unity UI has Y=0 at top, image has Y=0 at bottom)
            screenPoint.y = _baseImageSize.y - screenPoint.y;
            
            // Apply scale
            screenPoint *= _displayScale;
            
            return screenPoint;
        }
        
        /// <summary>
        /// Resize a texture to the specified dimensions.
        /// </summary>
        private Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight) {
            // Skip if already the right size
            if (source.width == targetWidth && source.height == targetHeight) {
                return source;
            }
            
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            
            RenderTexture prevRT = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            result.Apply();
            
            RenderTexture.active = prevRT;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }
        
        /// <summary>
        /// Log a message using the debugger.
        /// </summary>
        private void Log(string message, LogCategory category) {
            _debugger?.Log(message, category);
        }
        
        /// <summary>
        /// Log a warning using the debugger.
        /// </summary>
        private void LogWarning(string message, LogCategory category) {
            _debugger?.LogWarning(message, category);
        }
        
        /// <summary>
        /// Log an error using the debugger.
        /// </summary>
        private void LogError(string message, LogCategory category) {
            _debugger?.LogError(message, category);
        }
        
        #endregion
    }
} 
