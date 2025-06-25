using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using System.Reflection;
using Piglet;
using Battlehub.RTCommon;
using Battlehub.RTEditor;
using Battlehub.RTHandles; // Required for runtime transform handles
// Add required namespaces for the integration
using TripoForUnity;
using AiToolbox;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// WorldManager singleton that handles 3D model generation, import, and scene management
/// with integrated Runtime Transform Editor support
/// </summary>
public class WorldManager : MonoBehaviour
{
    #region Singleton Pattern
    private static WorldManager _instance;
    public static WorldManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<WorldManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("WorldManager");
                    _instance = go.AddComponent<WorldManager>();
                    DontDestroyOnLoad(go);
                }
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        
        // Ensure this GameObject is at root level before DontDestroyOnLoad
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }
        
        DontDestroyOnLoad(gameObject);
        
        // Initialize RTE system early in the lifecycle
        InitializeRTESystem();
        
        // Create loading modal
        CreateLoadingModal();
    }
    #endregion

    [Header("API Configuration")]
    [SerializeField] private string openAIApiKey = "";
    [SerializeField] private string tripo3DApiKey = "";

    [Header("UI References")]
    [SerializeField] private Button generateModelsButton;
    [SerializeField] private InputField descriptionInput;
    [SerializeField] private Text statusText;
    [SerializeField] private Transform modelsParent;
    
    [Header("Camera Control")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float cameraSpeed = 5f;
    [SerializeField] private float mouseSensitivity = 2f;

    [Header("AI Integration")]
    [SerializeField] private ChatGptParameters chatGptParameters;
    
    [Header("Runtime Transform Editor")]
    [SerializeField] private KeyCode transformModeToggleKey = KeyCode.T; // Key to cycle transform modes
    [SerializeField] private Color positionHandleColor = new Color(1f, 0.2f, 0.2f, 0.7f); // Red
    [SerializeField] private Color rotationHandleColor = new Color(0.2f, 0.8f, 0.2f, 0.7f); // Green
    [SerializeField] private Color scaleHandleColor = new Color(0.2f, 0.2f, 1f, 0.7f); // Blue
    [SerializeField] private float handleSize = 0.05f;
    [SerializeField] private bool showModeText = true;
    [SerializeField] private float modeTextDisplayTime = 2.0f;
    
    [Header("Loading Screen")]
    [SerializeField] private float minLoadingTime = 1.0f; // Minimum time to show loading screen
    [SerializeField] private float fadeSpeed = 1.5f; // Speed for fading out loading screen
    
    // Properties
    private List<GameObject> generatedModels = new List<GameObject>();
    private string modelsFolder => Path.Combine(Application.dataPath, "Inventory", "Models");
    private string thumbnailsFolder => Path.Combine(Application.dataPath, "Inventory", "Thumbnails");
    
    // Component references
    private TripoRuntimeCore tripoCore;
    
    // RTE related fields
    private IRTE runtimeEditor;
    private RuntimeTools runtimeTools;
    private RuntimeHandlesComponent handlesComponent;
    private GameObject modeTextObj;
    private Text modeTextComponent;
    private RuntimeTool currentTransformTool = RuntimeTool.Move;
    private bool rteInitialized = false;
    private Coroutine hideModeTextCoroutine;
    private Canvas inventoryCanvas;
    
    // Loading modal UI elements
    private GameObject loadingModalPanel;
    private Image loadingProgressBar;
    private Text loadingProgressText;
    private Text loadingStatusText;
    private bool isLoading = true;
    private float currentProgress = 0f;
    private float targetProgress = 0f;
    private Coroutine progressAnimationCoroutine;
    private float loadingStartTime;

    private void Start()
    {
        // Show loading modal before starting initialization
        ShowLoadingModal("Initializing systems...");
        loadingStartTime = Time.time;
        
        // Ensure inventory system is initialized first
        StartCoroutine(InitializeInventorySystem());
    }
    
    private void Update()
    {
        // Handle T key for cycling transform tools
        if (Input.GetKeyDown(transformModeToggleKey) && rteInitialized)
        {
            CycleTransformMode();
        }
        
        // Animate progress bar
        if (isLoading && loadingProgressBar != null)
        {
            // Smooth interpolation of progress bar
            currentProgress = Mathf.Lerp(currentProgress, targetProgress, Time.deltaTime * 3f);
            loadingProgressBar.fillAmount = currentProgress;
        }
    }
    
    /// <summary>
    /// Creates the loading modal UI with progress bar
    /// </summary>
    private void CreateLoadingModal()
    {
        // Create canvas for loading modal if needed
        Canvas modalCanvas = null;
        GameObject canvasObj = GameObject.Find("LoadingModalCanvas");
        if (canvasObj == null)
        {
            canvasObj = new GameObject("LoadingModalCanvas");
            modalCanvas = canvasObj.AddComponent<Canvas>();
            modalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            modalCanvas.sortingOrder = 1000; // Ensure it's on top of everything
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(canvasObj);
        }
        else
        {
            modalCanvas = canvasObj.GetComponent<Canvas>();
        }
        
        // Create modal panel
        loadingModalPanel = new GameObject("LoadingModalPanel");
        loadingModalPanel.transform.SetParent(modalCanvas.transform, false);
        
        // Set up panel rect
        RectTransform panelRect = loadingModalPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        // Add background image
        Image panelImage = loadingModalPanel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        
        // Create loading content container
        GameObject contentContainer = new GameObject("ContentContainer");
        contentContainer.transform.SetParent(loadingModalPanel.transform, false);
        
        RectTransform contentRect = contentContainer.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0.5f, 0.5f);
        contentRect.anchorMax = new Vector2(0.5f, 0.5f);
        contentRect.pivot = new Vector2(0.5f, 0.5f);
        contentRect.sizeDelta = new Vector2(400f, 200f);
        
        // Add title text
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(contentContainer.transform, false);
        
        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(0f, 50f);
        titleRect.anchoredPosition = new Vector2(0f, 0f);
        
        Text titleText = titleObj.AddComponent<Text>();
        titleText.text = "LOADING";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 28;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;
        
        // Add status text
        GameObject statusObj = new GameObject("StatusText");
        statusObj.transform.SetParent(contentContainer.transform, false);
        
        RectTransform statusRect = statusObj.AddComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 1f);
        statusRect.anchorMax = new Vector2(1f, 1f);
        statusRect.pivot = new Vector2(0.5f, 1f);
        statusRect.sizeDelta = new Vector2(0f, 30f);
        statusRect.anchoredPosition = new Vector2(0f, -50f);
        
        loadingStatusText = statusObj.AddComponent<Text>();
        loadingStatusText.text = "Initializing...";
        loadingStatusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        loadingStatusText.fontSize = 18;
        loadingStatusText.alignment = TextAnchor.MiddleCenter;
        loadingStatusText.color = new Color(0.8f, 0.8f, 0.8f);
        
        // Add progress bar background
        GameObject progressBgObj = new GameObject("ProgressBarBg");
        progressBgObj.transform.SetParent(contentContainer.transform, false);
        
        RectTransform progressBgRect = progressBgObj.AddComponent<RectTransform>();
        progressBgRect.anchorMin = new Vector2(0.1f, 0.5f);
        progressBgRect.anchorMax = new Vector2(0.9f, 0.5f);
        progressBgRect.pivot = new Vector2(0.5f, 0.5f);
        progressBgRect.sizeDelta = new Vector2(0f, 20f);
        progressBgRect.anchoredPosition = Vector2.zero;
        
        Image progressBgImage = progressBgObj.AddComponent<Image>();
        progressBgImage.color = new Color(0.2f, 0.2f, 0.2f);
        
        // Add progress bar fill
        GameObject progressFillObj = new GameObject("ProgressBarFill");
        progressFillObj.transform.SetParent(progressBgObj.transform, false);
        
        RectTransform progressFillRect = progressFillObj.AddComponent<RectTransform>();
        progressFillRect.anchorMin = Vector2.zero;
        progressFillRect.anchorMax = Vector2.one;
        progressFillRect.offsetMin = Vector2.zero;
        progressFillRect.offsetMax = Vector2.zero;
        
        loadingProgressBar = progressFillObj.AddComponent<Image>();
        loadingProgressBar.color = new Color(0.2f, 0.7f, 1f);
        loadingProgressBar.type = Image.Type.Filled;
        loadingProgressBar.fillMethod = Image.FillMethod.Horizontal;
        loadingProgressBar.fillOrigin = 0;
        loadingProgressBar.fillAmount = 0f;
        
        // Add progress percentage text
        GameObject progressTextObj = new GameObject("ProgressText");
        progressTextObj.transform.SetParent(contentContainer.transform, false);
        
        RectTransform progressTextRect = progressTextObj.AddComponent<RectTransform>();
        progressTextRect.anchorMin = new Vector2(0f, 0.5f);
        progressTextRect.anchorMax = new Vector2(1f, 0.5f);
        progressTextRect.pivot = new Vector2(0.5f, 0.5f);
        progressTextRect.sizeDelta = new Vector2(0f, 30f);
        progressTextRect.anchoredPosition = new Vector2(0f, -30f);
        
        loadingProgressText = progressTextObj.AddComponent<Text>();
        loadingProgressText.text = "0%";
        loadingProgressText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        loadingProgressText.fontSize = 16;
        loadingProgressText.alignment = TextAnchor.MiddleCenter;
        loadingProgressText.color = Color.white;
        
        // Initially hide the modal
        loadingModalPanel.SetActive(false);
    }
    
    /// <summary>
    /// Shows the loading modal with the specified status message
    /// </summary>
    public void ShowLoadingModal(string statusMessage = "Loading...")
    {
        if (loadingModalPanel != null)
        {
            loadingModalPanel.SetActive(true);
            
            if (loadingStatusText != null)
                loadingStatusText.text = statusMessage;
                
            if (loadingProgressText != null)
                loadingProgressText.text = "0%";
                
            if (loadingProgressBar != null)
                loadingProgressBar.fillAmount = 0f;
                
            currentProgress = 0f;
            targetProgress = 0f;
            isLoading = true;
        }
    }
    
    /// <summary>
    /// Updates the loading progress value and message
    /// </summary>
    public void UpdateLoadingProgress(float progress, string statusMessage = null)
    {
        targetProgress = Mathf.Clamp01(progress);
        
        if (loadingProgressText != null)
            loadingProgressText.text = $"{Mathf.Round(targetProgress * 100)}%";
            
        if (statusMessage != null && loadingStatusText != null)
            loadingStatusText.text = statusMessage;
            
        // Debug log for tracking loading progress
        Debug.Log($"[WorldManager] Loading progress: {targetProgress * 100}% - {statusMessage ?? "No status"}");
    }
    
    /// <summary>
    /// Hides the loading modal with a fade out effect
    /// </summary>
    public void HideLoadingModal()
    {
        // Calculate elapsed time since loading started
        float elapsedTime = Time.time - loadingStartTime;
        float remainingTime = Mathf.Max(0, minLoadingTime - elapsedTime);
        
        // Complete the progress bar for visual satisfaction
        if (progressAnimationCoroutine != null)
            StopCoroutine(progressAnimationCoroutine);
            
        progressAnimationCoroutine = StartCoroutine(CompleteProgressAndFadeOut(remainingTime));
    }
    
    /// <summary>
    /// Animates the progress bar to 100% and fades out the loading modal
    /// </summary>
    private IEnumerator CompleteProgressAndFadeOut(float delay)
    {
        // First complete the progress bar to 100%
        float startProgress = currentProgress;
        float animDuration = 0.5f;
        float timer = 0f;
        
        while (timer < animDuration)
        {
            timer += Time.deltaTime;
            float t = timer / animDuration;
            currentProgress = Mathf.Lerp(startProgress, 1f, t);
            
            if (loadingProgressBar != null)
                loadingProgressBar.fillAmount = currentProgress;
                
            if (loadingProgressText != null)
                loadingProgressText.text = $"{Mathf.Round(currentProgress * 100)}%";
                
            yield return null;
        }
        
        // Ensure it's exactly 100%
        if (loadingProgressBar != null)
            loadingProgressBar.fillAmount = 1f;
            
        if (loadingProgressText != null)
            loadingProgressText.text = "100%";
        
        // Wait for any additional delay to ensure minimum loading time
        if (delay > 0)
            yield return new WaitForSeconds(delay);
        
        // Then fade out the entire modal
        CanvasGroup canvasGroup = loadingModalPanel.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = loadingModalPanel.AddComponent<CanvasGroup>();
            
        canvasGroup.alpha = 1f;
        
        while (canvasGroup.alpha > 0)
        {
            canvasGroup.alpha -= Time.deltaTime * fadeSpeed;
            yield return null;
        }
        
        // Finally deactivate the modal
        loadingModalPanel.SetActive(false);
        isLoading = false;
        
        // Reset progress tracking
        currentProgress = 0f;
        targetProgress = 0f;
        
        Debug.Log("[WorldManager] Loading complete, modal hidden");
    }
    
    /// <summary>
    /// Initialize the Runtime Transform Editor system
    /// </summary>
    private void InitializeRTESystem()
    {
        Debug.Log("[WorldManager] Initializing Runtime Transform Editor...");
        try
        {
            // Try to resolve RTE from IOC container
            runtimeEditor = IOC.Resolve<IRTE>();
            
            if (runtimeEditor == null)
            {
                // No RTE found, try to create one
                Debug.Log("[WorldManager] No RTE instance found, creating one...");
                GameObject rteObject = new GameObject("RuntimeEditor");
                var editorComponent = rteObject.AddComponent<RuntimeEditor>();
                DontDestroyOnLoad(rteObject);
                
                // Give it a frame to initialize
                StartCoroutine(DelayedRTESetup());
            }
            else
            {
                // RTE already exists, just set it up
                Debug.Log("[WorldManager] Found existing RTE instance");
                SetupRTE();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WorldManager] Error initializing RTE: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Delayed RTE setup to give the RuntimeEditor component time to initialize
    /// </summary>
    private IEnumerator DelayedRTESetup()
    {
        yield return null; // Wait a frame
        
        // Try to resolve RTE again
        runtimeEditor = IOC.Resolve<IRTE>();
        
        if (runtimeEditor != null)
        {
            Debug.Log("[WorldManager] Successfully resolved RTE after delay");
            SetupRTE();
        }
        else
        {
            Debug.LogError("[WorldManager] Failed to resolve RTE even after delay");
            
            // One more attempt with slightly longer delay
            yield return new WaitForSeconds(0.2f);
            runtimeEditor = IOC.Resolve<IRTE>();
            
            if (runtimeEditor != null)
            {
                Debug.Log("[WorldManager] Successfully resolved RTE after extended delay");
                SetupRTE();
            }
            else
            {
                Debug.LogError("[WorldManager] Failed to resolve RTE after multiple attempts");
            }
        }
    }
    
    /// <summary>
    /// Set up RTE components and configuration
    /// </summary>
    private void SetupRTE()
    {
        if (runtimeEditor == null)
        {
            Debug.LogError("[WorldManager] Cannot setup RTE - runtimeEditor is null");
            return;
        }
        
        // Get tools and configure them
        runtimeTools = runtimeEditor.Tools;
        
        if (runtimeTools != null)
        {
            runtimeTools.Current = currentTransformTool;
            
            // Subscribe to selection changed to ensure handles appear
            runtimeEditor.Selection.SelectionChanged += OnSelectionChanged;
            
            Debug.Log($"[WorldManager] Set initial transform tool to: {currentTransformTool}");
        }
        else
        {
            Debug.LogWarning("[WorldManager] RuntimeTools not available");
        }
        
        // Setup transform handles on camera
        SetupTransformHandles();
        
        // Ensure canvas exists for UI
        EnsureInventoryCanvas();
        
        // Create UI for transform mode text
        CreateTransformModeText();
        
        rteInitialized = true;
        Debug.Log("[WorldManager] RTE setup complete");
    }
    
    /// <summary>
    /// Ensure canvas exists for UI elements
    /// </summary>
    private void EnsureInventoryCanvas()
    {
        inventoryCanvas = FindObjectOfType<Canvas>();
        if (inventoryCanvas == null)
        {
            GameObject canvasObj = new GameObject("InventoryCanvas");
            inventoryCanvas = canvasObj.AddComponent<Canvas>();
            inventoryCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            DontDestroyOnLoad(canvasObj);
            
            Debug.Log("[WorldManager] Created canvas for UI elements");
        }
    }
    
    /// <summary>
    /// Respond to selection changes to ensure transform handles work correctly
    /// </summary>
    private void OnSelectionChanged(UnityEngine.Object[] unselectedObjects)
    {
        if (runtimeEditor == null || runtimeTools == null) return;
        
        // Ensure the current tool is applied to newly selected objects
        if (runtimeEditor.Selection.activeGameObject != null)
        {
            // Force refresh of handles by briefly toggling the tool
            RuntimeTool currentTool = runtimeTools.Current;
            runtimeTools.Current = currentTool == RuntimeTool.Move ? RuntimeTool.Rotate : RuntimeTool.Move;
            runtimeTools.Current = currentTool;
            
            Debug.Log($"[WorldManager] Selection changed, refreshed handles for: {runtimeEditor.Selection.activeGameObject.name}");
        }
    }
    
    /// <summary>
    /// Setup transform handles component on the camera
    /// </summary>
    private void SetupTransformHandles()
    {
        // Find a suitable camera
        Camera targetCamera = mainCamera;
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null)
            targetCamera = FindObjectOfType<Camera>();
        
        if (targetCamera == null)
        {
            Debug.LogError("[WorldManager] No camera found for transform handles");
            return;
        }
        
        // Check if handles already exist
        handlesComponent = targetCamera.GetComponent<RuntimeHandlesComponent>();
        if (handlesComponent == null)
        {
            handlesComponent = targetCamera.gameObject.AddComponent<RuntimeHandlesComponent>();
            Debug.Log($"[WorldManager] Added RuntimeHandlesComponent to {targetCamera.name}");
        }
        
        // Configure handles appearance
        ConfigureHandlesAppearance();
    }
    
    /// <summary>
    /// Configure the appearance of transform handles
    /// </summary>
    private void ConfigureHandlesAppearance()
    {
        if (handlesComponent == null) return;
        
        try
        {
            // Use reflection to access private fields if needed
            var posHandleColorField = typeof(RuntimeHandlesComponent).GetField("m_positionHandleColor", 
                BindingFlags.NonPublic | BindingFlags.Instance);
                
            var rotHandleColorField = typeof(RuntimeHandlesComponent).GetField("m_rotationHandleColor", 
                BindingFlags.NonPublic | BindingFlags.Instance);
                
            var scaleHandleColorField = typeof(RuntimeHandlesComponent).GetField("m_scaleHandleColor", 
                BindingFlags.NonPublic | BindingFlags.Instance);
                
            var handleScaleField = typeof(RuntimeHandlesComponent).GetField("m_handleScale", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            
            // Apply colors and size if fields were found
            if (posHandleColorField != null) posHandleColorField.SetValue(handlesComponent, positionHandleColor);
            if (rotHandleColorField != null) rotHandleColorField.SetValue(handlesComponent, rotationHandleColor);
            if (scaleHandleColorField != null) scaleHandleColorField.SetValue(handlesComponent, scaleHandleColor);
            if (handleScaleField != null) handleScaleField.SetValue(handlesComponent, handleSize);
            
            Debug.Log("[WorldManager] Successfully configured transform handles appearance");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WorldManager] Error configuring handles appearance: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Create UI text element to show current transform mode
    /// </summary>
    private void CreateTransformModeText()
    {
        if (!showModeText || inventoryCanvas == null) return;
        
        // Create parent object for mode text
        modeTextObj = new GameObject("TransformModeText");
        modeTextObj.transform.SetParent(inventoryCanvas.transform, false);
        
        // Add UI components
        RectTransform rectTransform = modeTextObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.95f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.95f);
        rectTransform.pivot = new Vector2(0.5f, 1.0f);
        rectTransform.sizeDelta = new Vector2(300f, 40f);
        
        // Add background image
        Image background = modeTextObj.AddComponent<Image>();
        background.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        
        // Add text component
        GameObject textChild = new GameObject("Text");
        textChild.transform.SetParent(modeTextObj.transform, false);
        
        RectTransform textRect = textChild.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(5f, 5f);
        textRect.offsetMax = new Vector2(-5f, -5f);
        
        modeTextComponent = textChild.AddComponent<Text>();
        modeTextComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        modeTextComponent.fontSize = 18;
        modeTextComponent.alignment = TextAnchor.MiddleCenter;
        modeTextComponent.color = Color.white;
        modeTextComponent.text = "MOVE MODE (Press T to change)";
        
        // Initially hide
        modeTextObj.SetActive(false);
        
        Debug.Log("[WorldManager] Created transform mode text UI");
    }
    
    /// <summary>
    /// Cycle through transform modes (Move -> Rotate -> Scale -> Move)
    /// </summary>
    private void CycleTransformMode()
    {
        if (!rteInitialized || runtimeTools == null)
        {
            Debug.LogWarning("[WorldManager] Cannot cycle transform mode - RTE not initialized");
            return;
        }
        
        // Get current tool and cycle to next
        RuntimeTool currentTool = runtimeTools.Current;
        RuntimeTool nextTool;
        
        // Determine next tool in cycle
        switch (currentTool)
        {
            case RuntimeTool.Move:
                nextTool = RuntimeTool.Rotate;
                break;
            case RuntimeTool.Rotate:
                nextTool = RuntimeTool.Scale;
                break;
            case RuntimeTool.Scale:
            default:
                nextTool = RuntimeTool.Move;
                break;
        }
        
        // Set the next tool
        runtimeTools.Current = nextTool;
        currentTransformTool = nextTool;
        
        // Show feedback
        ShowTransformModeText(nextTool);
        
        Debug.Log($"[WorldManager] Transform mode changed to: {nextTool}");
    }
    
    /// <summary>
    /// Show transform mode text with auto-hide after delay
    /// </summary>
    private void ShowTransformModeText(RuntimeTool mode)
    {
        if (!showModeText || modeTextObj == null || modeTextComponent == null) 
        {
            // Show in status text if mode text display is not available
            if (statusText != null)
            {
                statusText.text = $"Transform Mode: {mode}";
            }
            return;
        }
        
        // Set appropriate text based on mode
        string modeText = "";
        switch (mode)
        {
            case RuntimeTool.Move:
                modeText = "▶ MOVE MODE ◀ (Press T to change)";
                break;
            case RuntimeTool.Rotate:
                modeText = "↻ ROTATE MODE ↺ (Press T to change)";
                break;
            case RuntimeTool.Scale:
                modeText = "⤧ SCALE MODE ⤧ (Press T to change)";
                break;
            default:
                modeText = $"{mode} MODE (Press T to change)";
                break;
        }
        
        modeTextComponent.text = modeText;
        modeTextObj.SetActive(true);
        
        // Cancel previous hide coroutine if running
        if (hideModeTextCoroutine != null)
        {
            StopCoroutine(hideModeTextCoroutine);
        }
        
        // Start new hide coroutine
        hideModeTextCoroutine = StartCoroutine(HideModeTextAfterDelay());
    }
    
    /// <summary>
    /// Hide mode text after specified delay
    /// </summary>
    private IEnumerator HideModeTextAfterDelay()
    {
        yield return new WaitForSeconds(modeTextDisplayTime);
        
        if (modeTextObj != null)
        {
            modeTextObj.SetActive(false);
        }
        
        hideModeTextCoroutine = null;
    }
    
    private IEnumerator InitializeInventorySystem()
    {
        // Update loading progress
        UpdateLoadingProgress(0.05f, "Initializing inventory system...");
        Debug.Log("[WorldManager] 🚀 Starting inventory system initialization...");
        
        // Create a list to track initialization steps
        List<string> initSteps = new List<string>();
        
        // Wait a frame to ensure all singletons are ready
        yield return null;
        UpdateLoadingProgress(0.1f, "Preparing database...");
        
        // Initialize InventoryDatabase first with retry logic
        Debug.Log("[WorldManager] Initializing InventoryDatabase...");
        bool databaseInitialized = false;
        int retryCount = 0;
        InventoryDatabase inventoryDatabase = null;
        
        while (!databaseInitialized && retryCount < 3)
        {
            bool shouldWait = false;
            try
            {
                inventoryDatabase = InventoryDatabase.Instance;
                if (inventoryDatabase != null)
                {
                    databaseInitialized = true;
                    Debug.Log("[WorldManager] ✅ InventoryDatabase initialized successfully");
                    initSteps.Add("Database initialized");
                }
                else
                {
                    retryCount++;
                    Debug.LogWarning($"[WorldManager] ⚠️ InventoryDatabase is null, retrying ({retryCount}/3)...");
                    shouldWait = true;
                }
            }
            catch (Exception ex)
            {
                retryCount++;
                Debug.LogError($"[WorldManager] InventoryDatabase initialization error: {ex.Message}");
                shouldWait = true;
            }
            
            // Wait outside the try-catch block
            if (shouldWait)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        if (!databaseInitialized)
        {
            Debug.LogError("[WorldManager] ❌ Failed to initialize InventoryDatabase after multiple attempts");
            initSteps.Add("Database initialization FAILED");
        }
        
        // Wait to ensure database is ready
        yield return new WaitForSeconds(0.2f);
        UpdateLoadingProgress(0.3f, "Loading inventory manager...");
        
        // Force initialization of InventoryManager with error handling and retry
        Debug.Log("[WorldManager] Initializing InventoryManager...");
        bool managerInitialized = false;
        retryCount = 0;
        InventoryManager inventoryManager = null;
        
        while (!managerInitialized && retryCount < 3)
        {
            bool shouldWait = false;
            try
            {
                inventoryManager = InventoryManager.Instance;
                if (inventoryManager != null)
                {
                    managerInitialized = true;
                    Debug.Log("[WorldManager] ✅ InventoryManager initialized successfully");
                    initSteps.Add("Inventory manager initialized");
                }
                else
                {
                    retryCount++;
                    Debug.LogWarning($"[WorldManager] ⚠️ InventoryManager is null, retrying ({retryCount}/3)...");
                    shouldWait = true;
                }
            }
            catch (Exception ex)
            {
                retryCount++;
                Debug.LogError($"[WorldManager] InventoryManager initialization error: {ex.Message}");
                shouldWait = true;
            }
            
            // Wait outside the try-catch block
            if (shouldWait)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        if (!managerInitialized)
        {
            Debug.LogError("[WorldManager] ❌ Failed to initialize InventoryManager after multiple attempts");
            initSteps.Add("Inventory manager initialization FAILED");
        }
        
        // Wait to ensure manager is ready
        yield return new WaitForSeconds(0.2f);
        UpdateLoadingProgress(0.5f, "Loading inventory items...");
        
        // Wait for inventory to load
        if (managerInitialized && inventoryManager != null)
        {
            float timeoutSeconds = 10f;
            float elapsed = 0f;
            
            while (inventoryManager.IsCurrentlyLoading && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                UpdateLoadingProgress(0.5f + (elapsed / timeoutSeconds * 0.2f), "Loading inventory items...");
                yield return null;
            }
            
            if (elapsed >= timeoutSeconds)
            {
                Debug.LogWarning("[WorldManager] ⚠️ Inventory loading timed out");
                initSteps.Add("Inventory loading timed out");
            }
            else
            {
                Debug.Log("[WorldManager] ✅ Inventory items loaded successfully");
                initSteps.Add("Inventory items loaded");
            }
            
            // Get inventory item count for validation
            int itemCount = inventoryManager.GetItemCount();
            Debug.Log($"[WorldManager] Inventory contains {itemCount} items");
            initSteps.Add($"Found {itemCount} inventory items");
        }
        
        // Initialize UI
        UpdateLoadingProgress(0.75f, "Setting up inventory UI...");
        
        Debug.Log("[WorldManager] Initializing InventoryUI...");
        bool uiInitialized = false;
        retryCount = 0;
        InventoryUI inventoryUI = null;
        
        while (!uiInitialized && retryCount < 3)
        {
            bool shouldWait = false;
            
            try
            {
                inventoryUI = InventoryUI.Instance;
                if (inventoryUI != null)
                {
                    uiInitialized = true;
                    Debug.Log("[WorldManager] ✅ InventoryUI initialized successfully");
                    initSteps.Add("Inventory UI initialized");
                }
                else
                {
                    retryCount++;
                    Debug.LogWarning($"[WorldManager] ⚠️ InventoryUI is null, retrying ({retryCount}/3)...");
                    shouldWait = true;
                }
            }
            catch (Exception ex)
            {
                retryCount++;
                Debug.LogError($"[WorldManager] InventoryUI initialization error: {ex.Message}");
                shouldWait = true;
            }
            
            // Wait outside the try-catch block
            if (shouldWait)
            {
                yield return new WaitForSeconds(0.5f);
            }
        }
        
        if (!uiInitialized)
        {
            Debug.LogWarning("[WorldManager] ⚠️ InventoryUI not initialized - may not be in scene");
            initSteps.Add("Inventory UI initialization skipped");
        }
        
        // Force a UI refresh if initialized
        if (uiInitialized && inventoryUI != null)
        {
            try
            {
                var refreshMethod = typeof(InventoryUI).GetMethod("RefreshInventoryDisplay", 
                    BindingFlags.Public | BindingFlags.Instance);
                    
                if (refreshMethod != null)
                {
                    refreshMethod.Invoke(inventoryUI, null);
                    Debug.Log("[WorldManager] ✅ Refreshed inventory UI display");
                    initSteps.Add("Refreshed inventory display");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[WorldManager] Error refreshing inventory UI: {ex.Message}");
            }
        }
        
        // Wait longer before proceeding to ensure stability
        yield return new WaitForSeconds(0.5f);
        UpdateLoadingProgress(0.9f, "Completing initialization...");
        
        Debug.Log("[WorldManager] ✅ Inventory system initialization complete");
        Debug.Log("[WorldManager] Initialization steps:");
        foreach (string step in initSteps)
        {
            Debug.Log($" - {step}");
        }
        
        // Wait another frame then initialize WorldManager
        yield return null;
        InitializeWorldManager();
        
        // Complete loading progress
        UpdateLoadingProgress(1.0f, "Ready!");
        
        // Hide loading modal
        HideLoadingModal();
    }

    /// <summary>
    /// Sets up button event handlers for the UI
    /// </summary>
    private void SetupButtonEvents()
    {
        if (generateModelsButton != null)
        {
            generateModelsButton.onClick.AddListener(OnGenerateModelsButtonClick);
            Debug.Log("[WorldManager] Generate Models button event handler connected");
        }
        else
        {
            Debug.LogWarning("[WorldManager] Generate Models button is not assigned in the inspector");
        }
    }

    /// <summary>
    /// Handles the Generate Models button click event
    /// Implements the complete workflow: OpenAI → Tripo3D → Piglet
    /// </summary>
    private void OnGenerateModelsButtonClick()
    {
        GenerateModelWithDescription();
    }
    
    /// <summary>
    /// Public method to generate a model with a specific description (for testing/external calls)
    /// </summary>
    public void GenerateModelWithDescription(string description = "")
    {
        // Validate API keys
        if (string.IsNullOrEmpty(openAIApiKey) || string.IsNullOrEmpty(tripo3DApiKey))
        {
            UpdateStatus("Error: API keys not set. Please configure OpenAI and Tripo3D API keys.", true);
            Debug.LogError("[WorldManager] API keys are required but not set. OpenAI Key: " + 
                          (string.IsNullOrEmpty(openAIApiKey) ? "Missing" : "Set") + 
                          ", Tripo3D Key: " + (string.IsNullOrEmpty(tripo3DApiKey) ? "Missing" : "Set"));
            return;
        }

        // Get description from parameter or UI
        string userDescription = description;
        if (string.IsNullOrEmpty(userDescription))
        {
            userDescription = descriptionInput?.text ?? "";
        }
        
        if (string.IsNullOrEmpty(userDescription))
        {
            UpdateStatus("Error: Please enter a description for the model to generate.", true);
            Debug.LogError("[WorldManager] No description provided for model generation");
            return;
        }

        UpdateStatus("Starting model generation workflow...", false);
        Debug.Log($"[WorldManager] Starting model generation for: {userDescription}");
        Debug.Log($"[WorldManager] Using OpenAI API key: {(openAIApiKey.Length > 10 ? openAIApiKey.Substring(0, 10) + "..." : "Short key")}");
        Debug.Log($"[WorldManager] Using Tripo3D API key: {(tripo3DApiKey.Length > 10 ? tripo3DApiKey.Substring(0, 10) + "..." : "Short key")}");

        // Disable the button to prevent multiple simultaneous requests
        if (generateModelsButton != null)
            generateModelsButton.interactable = false;

        // Start the complete workflow
        StartCoroutine(GenerateModelWorkflow(userDescription));
    }

    /// <summary>
    /// Complete model generation workflow that integrates OpenAI, Tripo3D, and Piglet
    /// </summary>
    private IEnumerator GenerateModelWorkflow(string userDescription)
    {
        string enhancedDescription = "";
        bool openAICompleted = false;
        bool openAIFailed = false;

        // Step 1: Enhance description using OpenAI ChatGPT
        UpdateStatus("Step 1/3: Enhancing description with OpenAI...", false);
        
        yield return StartCoroutine(EnhanceDescriptionWithOpenAI(userDescription, 
            (result) => {
                enhancedDescription = result;
                openAICompleted = true;
            },
            (error) => {
                Debug.LogError($"[WorldManager] OpenAI failed: {error}");
                enhancedDescription = userDescription; // Fallback to original
                openAICompleted = true;
                openAIFailed = true;
            }));

        if (!openAICompleted)
        {
            UpdateStatus("Error: OpenAI request timed out", true);
            goto Cleanup;
        }

        if (openAIFailed)
        {
            UpdateStatus("Warning: Using original description (OpenAI enhancement failed)", false);
        }

        Debug.Log($"[WorldManager] Enhanced description: {enhancedDescription}");

        // Step 2: Generate 3D model using Tripo3D
        UpdateStatus("Step 2/3: Generating 3D model with Tripo3D...", false);

        bool tripoCompleted = false;
        string modelUrl = "";
        
        // Setup TripoRuntimeCore component
        tripoCore = GetComponent<TripoRuntimeCore>();
        if (tripoCore == null)
        {
            tripoCore = gameObject.AddComponent<TripoRuntimeCore>();
            Debug.Log("[WorldManager] Added TripoRuntimeCore component");
        }

        // Configure Tripo3D
        try
        {
            tripoCore.set_api_key(tripo3DApiKey);
            tripoCore.textPrompt = enhancedDescription;
            
            Debug.Log($"[WorldManager] Tripo3D configured with prompt: {enhancedDescription}");
            
            // Subscribe to completion event
            tripoCore.OnDownloadComplete.AddListener((url) => {
                modelUrl = url;
                tripoCompleted = true;
                Debug.Log($"[WorldManager] Tripo3D model generated: {url}");
            });

            // Start the generation
            tripoCore.Text_to_Model_func();
            Debug.Log("[WorldManager] Tripo3D generation started");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Error starting Tripo3D generation: {ex.Message}", true);
            Debug.LogError($"[WorldManager] Tripo3D configuration error: {ex.Message}");
            goto Cleanup;
        }

        // Wait for completion (with timeout)
        float timeout = 300f; // 5 minutes timeout
        float elapsed = 0f;
        while (!tripoCompleted && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!tripoCompleted)
        {
            UpdateStatus("Error: Tripo3D model generation timed out", true);
            goto Cleanup;
        }

        if (string.IsNullOrEmpty(modelUrl))
        {
            UpdateStatus("Error: Tripo3D did not return a valid model URL", true);
            goto Cleanup;
        }

        // Step 3: Import model using Piglet
        UpdateStatus("Step 3/3: Importing model with Piglet...", false);
        yield return StartCoroutine(ImportModelWithPiglet(modelUrl, enhancedDescription));

        Cleanup:
        // Re-enable the button
        if (generateModelsButton != null)
            generateModelsButton.interactable = true;
    }

    /// <summary>
    /// Enhances the user description using OpenAI ChatGPT
    /// </summary>
    private IEnumerator EnhanceDescriptionWithOpenAI(string originalDescription, Action<string> onSuccess, Action<string> onFailure)
    {
        // Setup ChatGPT parameters if not already configured
        if (chatGptParameters == null)
        {
            chatGptParameters = new ChatGptParameters(openAIApiKey);
        }
        else
        {
            chatGptParameters.apiKey = openAIApiKey;
        }

        // Create enhancement prompt
        string enhancementPrompt = $"Enhance this 3D model description for optimal 3D generation. Make it more detailed and specific while keeping it concise. Focus on shape, materials, and key visual elements: {originalDescription}";

        bool requestCompleted = false;
        string result = "";
        string error = "";

        // Make the ChatGPT request
        ChatGpt.Request(enhancementPrompt, chatGptParameters,
            // Success callback
            (response) => {
                result = response;
                requestCompleted = true;
                Debug.Log($"[WorldManager] OpenAI enhanced description: {response}");
            },
            // Failure callback
            (errorCode, errorMessage) => {
                error = $"OpenAI Error {errorCode}: {errorMessage}";
                requestCompleted = true;
                Debug.LogError($"[WorldManager] OpenAI request failed: {error}");
            });

        // Wait for completion with timeout
        float timeout = 30f;
        float elapsed = 0f;
        while (!requestCompleted && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!requestCompleted)
        {
            onFailure?.Invoke("OpenAI request timed out");
        }
        else if (!string.IsNullOrEmpty(error))
        {
            onFailure?.Invoke(error);
        }
        else
        {
            onSuccess?.Invoke(result);
        }
    }

    /// <summary>
    /// Imports the generated model using Piglet glTF importer
    /// </summary>
    private IEnumerator ImportModelWithPiglet(string modelUrl, string description)
    {
        UpdateStatus("Step 3/3: Importing model with Piglet...", false);
        
        bool importCompleted = false;
        bool importFailed = false;
        GameObject importedModel = null;
        string errorMessage = "";
        
        // Create the import task
        var importOptions = new GltfImportOptions();
        var importTask = RuntimeGltfImporter.GetImportTask(modelUrl);
        
        // Set up callbacks
        importTask.OnCompleted = (model) => {
            importedModel = model;
            importCompleted = true;
        };
        
        importTask.OnException = (exception) => {
            errorMessage = exception.Message;
            importFailed = true;
            Debug.LogError($"[WorldManager] Piglet import exception: {exception}");
        };
        
        // Execute the import task by calling MoveNext() until completion
        while (!importCompleted && !importFailed)
        {
            try
            {
                if (!importTask.MoveNext())
                {
                    // Task completed normally - wait for callback
                    break;
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                importFailed = true;
                Debug.LogError($"[WorldManager] Piglet import MoveNext exception: {ex}");
                break;
            }
            
            yield return null; // Wait one frame before next iteration
        }
        
        // Handle the results
        if (importFailed)
        {
            UpdateStatus($"Error importing model: {errorMessage}", true);
            yield break;
        }
        
        if (importedModel == null)
        {
            UpdateStatus("Error: Failed to import model with Piglet - no model returned", true);
            yield break;
        }
        
        // Set up the imported model with a unique, short name
        string shortDescription = description.Length > 15 ? description.Substring(0, 15) : description;
        shortDescription = shortDescription.Replace(" ", "").Replace("_", "");
        string uniqueId = System.Guid.NewGuid().ToString("N").Substring(0, 6); // 6-character unique ID
        string modelName = $"{shortDescription}_{uniqueId}";
        importedModel.name = modelName;
        
        // 🔍 DEBUG: Inspect what Piglet actually imported
        Debug.Log($"[WorldManager] 🔍 INSPECTING PIGLET IMPORTED MODEL: {modelName}");
        InspectImportedModelTextures(importedModel);
        
        // Position the model
        Vector3 spawnPosition = mainCamera != null ? 
            mainCamera.transform.position + mainCamera.transform.forward * 5f : 
            Vector3.zero;
        
        SetupPlacedModel(importedModel, spawnPosition);
        
        // Add to inventory
        bool addedToInventory = AddModelToInventory(importedModel, description);
        
        Debug.Log($"[WorldManager] 📊 MODEL GENERATION STATISTICS:");
        Debug.Log($"  - Model Name: {modelName}");
        Debug.Log($"  - Added to Inventory: {addedToInventory}");
        Debug.Log($"  - Description: {description}");
        
        if (addedToInventory)
        {
            UpdateStatus($"Success! Model '{modelName}' generated and added to inventory", false);
        }
        else
        {
            UpdateStatus($"Model generated but failed to add to inventory: {modelName}", false);
        }

        Debug.Log($"[WorldManager] Model generation workflow completed successfully: {modelName}");
    }

    /// <summary>
    /// Updates the status text in the UI
    /// </summary>
    private void UpdateStatus(string message, bool isError = false)
    {
        if (statusText != null)
        {
            statusText.text = message;
            statusText.color = isError ? Color.red : Color.white;
        }
        
        Debug.Log($"[WorldManager] Status: {message}");
    }

    private void OnDestroy()
    {
        // Clean up RTE event subscriptions
        if (runtimeEditor != null && runtimeEditor.Selection != null)
        {
            runtimeEditor.Selection.SelectionChanged -= OnSelectionChanged;
        }
        
        // Cleanup event handlers
        if (generateModelsButton != null)
            generateModelsButton.onClick.RemoveListener(OnGenerateModelsButtonClick);
            
        if (tripoCore != null && tripoCore.OnDownloadComplete != null)
            tripoCore.OnDownloadComplete.RemoveAllListeners();

        // Unsubscribe from events using reflection to prevent memory leaks
        try
        {
            var inventoryUIType = Type.GetType("InventoryUI");
            if (inventoryUIType != null)
            {
                try
                {
                    object inventoryUIInstance = inventoryUIType.GetProperty("Instance")?.GetValue(null);
                    if (inventoryUIInstance != null)
                    {
                        var eventField = inventoryUIType.GetField("OnModelPlacedInScene");
                        if (eventField != null)
                        {
                            var delegateType = typeof(Action<GameObject>);
                            var method = typeof(WorldManager).GetMethod("PlaceModelInScene", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(GameObject) }, null);
                            if (method != null)
                            {
                                var handler = Delegate.CreateDelegate(delegateType, this, method);
                                eventField.SetValue(inventoryUIInstance, Delegate.Remove((Delegate)eventField.GetValue(inventoryUIInstance), handler));
                                Debug.Log("[WorldManager] Successfully unsubscribed from InventoryUI events via reflection");
                            }
                            else
                            {
                                Debug.LogWarning("[WorldManager] PlaceModelInScene method not found during cleanup");
                            }
                        }
                        else
                        {
                            Debug.LogWarning("[WorldManager] OnModelPlacedInScene event field not found during cleanup");
                        }
                    }
                }
                catch (Exception cleanupEx)
                {
                    Debug.LogWarning($"[WorldManager] Failed to unsubscribe from InventoryUI events: {cleanupEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WorldManager] Error during cleanup: {ex.Message}");
        }
    }

    private void InitializeWorldManager()
    {
        Debug.Log("[WorldManager] Starting initialization...");
        
        // Create necessary directories
        CreateDirectories();
        
        // Use reflection to access inventory classes to avoid circular dependency
        try
        {
            Debug.Log("[WorldManager] Searching for inventory types...");
            
            // List all assemblies for debugging
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            Debug.Log($"[WorldManager] Found {assemblies.Length} assemblies");
            
            // Try multiple ways to find the types
            var inventoryManagerType = Type.GetType("InventoryManager") ?? 
                                     System.Reflection.Assembly.GetExecutingAssembly().GetType("InventoryManager") ??
                                     System.AppDomain.CurrentDomain.GetAssemblies()
                                         .SelectMany(a => {
                                             try { return a.GetTypes(); }
                                             catch { return new Type[0]; }
                                         })
                                         .FirstOrDefault(t => t.Name == "InventoryManager");
                                         
            var inventoryDatabaseType = Type.GetType("InventoryDatabase") ?? 
                                      System.Reflection.Assembly.GetExecutingAssembly().GetType("InventoryDatabase") ??
                                      System.AppDomain.CurrentDomain.GetAssemblies()
                                          .SelectMany(a => {
                                              try { return a.GetTypes(); }
                                              catch { return new Type[0]; }
                                          })
                                          .FirstOrDefault(t => t.Name == "InventoryDatabase");
                                          
            var inventoryUIType = Type.GetType("InventoryUI") ?? 
                                System.Reflection.Assembly.GetExecutingAssembly().GetType("InventoryUI") ??
                                System.AppDomain.CurrentDomain.GetAssemblies()
                                    .SelectMany(a => {
                                        try { return a.GetTypes(); }
                                        catch { return new Type[0]; }
                                    })
                                    .FirstOrDefault(t => t.Name == "InventoryUI");
            
            Debug.Log($"[WorldManager] InventoryManager Type: {(inventoryManagerType != null ? $"Found ({inventoryManagerType.FullName})" : "Not Found")}");
            Debug.Log($"[WorldManager] InventoryDatabase Type: {(inventoryDatabaseType != null ? $"Found ({inventoryDatabaseType.FullName})" : "Not Found")}");
            Debug.Log($"[WorldManager] InventoryUI Type: {(inventoryUIType != null ? $"Found ({inventoryUIType.FullName})" : "Not Found")}");
            
            if (inventoryUIType == null)
            {
                Debug.LogWarning("[WorldManager] InventoryUI type not found in any assembly. Available types:");
                var allTypes = System.AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => {
                        try { return a.GetTypes(); }
                        catch { return new Type[0]; }
                    })
                    .Where(t => t.Name.Contains("Inventory"))
                    .ToArray();
                foreach (var type in allTypes)
                {
                    Debug.Log($"[WorldManager] Found inventory-related type: {type.FullName}");
                }
            }
            
            // Subscribe to InventoryUI events using reflection
            if (inventoryUIType != null)
            {
                try
                {
                    // Force initialization of InventoryUI singleton
                    object inventoryUIInstance = inventoryUIType.GetProperty("Instance")?.GetValue(null);
                    if (inventoryUIInstance == null)
                    {
                        Debug.LogWarning("[WorldManager] InventoryUI instance is null, attempting to create...");
                        // Try to find existing InventoryUI in scene or create one
                        var existingUI = FindObjectOfType(inventoryUIType);
                        if (existingUI == null)
                        {
                            GameObject uiGO = new GameObject("InventoryUI");
                            inventoryUIInstance = uiGO.AddComponent(inventoryUIType);
                            DontDestroyOnLoad(uiGO);
                            Debug.Log("[WorldManager] Created new InventoryUI instance");
                        }
                        else
                        {
                            inventoryUIInstance = existingUI;
                            Debug.Log("[WorldManager] Found existing InventoryUI in scene");
                        }
                    }
                    
                    if (inventoryUIInstance != null)
                    {
                        // Note: Removed OnModelPlacedInScene subscription to prevent double positioning
                        // Models are already correctly positioned by PlaceModelFromInventory
                        Debug.Log("[WorldManager] Successfully subscribed to InventoryUI events via reflection");
                    }
                }
                catch (Exception eventEx)
                {
                    Debug.LogWarning($"[WorldManager] Failed to subscribe to InventoryUI events: {eventEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WorldManager] Error during reflection-based initialization: {ex.Message}");
        }
        
        // Setup button event handlers after initialization
        SetupButtonEvents();
        
        Debug.Log("[WorldManager] Initialization complete - using reflection-based approach!");
    }

    /// <summary>
    /// Creates necessary directories for the inventory system
    /// </summary>
    private void CreateDirectories()
    {
        try
        {
            if (!Directory.Exists(modelsFolder))
            {
                Directory.CreateDirectory(modelsFolder);
                Debug.Log($"[WorldManager] Created models folder: {modelsFolder}");
            }

            if (!Directory.Exists(thumbnailsFolder))
            {
                Directory.CreateDirectory(thumbnailsFolder);
                Debug.Log($"[WorldManager] Created thumbnails folder: {thumbnailsFolder}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Error creating directories: {ex.Message}");
        }
    }
    
    #region Model Placement and Scene Management
    
    /// <summary>
    /// Places a model from inventory into the scene at the specified position
    /// Uses reflection to avoid circular dependency
    /// </summary>
    public GameObject PlaceModelFromInventory(object inventoryItem, Vector3 position)
    {
        if (inventoryItem == null)
        {
            Debug.LogError("[WorldManager] Cannot place null inventory item");
            return null;
        }

        GameObject placedModel = null;

        try
        {
            // Use reflection to access inventory item properties
            var itemType = inventoryItem.GetType();
            
            // Try to get gameObject property/field
            object gameObjectValue = null;
            var gameObjectProperty = itemType.GetProperty("gameObject");
            if (gameObjectProperty != null)
                gameObjectValue = gameObjectProperty.GetValue(inventoryItem);
            else
            {
                var gameObjectField = itemType.GetField("gameObject");
                if (gameObjectField != null)
                    gameObjectValue = gameObjectField.GetValue(inventoryItem);
            }
            
            // Try to get name property/field
            object nameValue = null;
            var nameProperty = itemType.GetProperty("name");
            if (nameProperty != null)
                nameValue = nameProperty.GetValue(inventoryItem);
            else
            {
                var nameField = itemType.GetField("name");
                if (nameField != null)
                    nameValue = nameField.GetValue(inventoryItem);
            }
            
            // Try to get modelPath property/field
            object modelPathValue = null;
            var modelPathProperty = itemType.GetProperty("modelPath");
            if (modelPathProperty != null)
                modelPathValue = modelPathProperty.GetValue(inventoryItem);
            else
            {
                var modelPathField = itemType.GetField("modelPath");
                if (modelPathField != null)
                    modelPathValue = modelPathField.GetValue(inventoryItem);
            }
            
            GameObject itemGameObject = gameObjectValue as GameObject;
            string itemName = nameValue as string ?? "UnknownItem";
            string modelPath = modelPathValue as string;

            // If the item already has a GameObject, instantiate a copy
            if (itemGameObject != null)
            {
                placedModel = Instantiate(itemGameObject);
                placedModel.name = $"{itemName}_Instance_{System.Guid.NewGuid().ToString("N").Substring(0, 8)}";
                SetupPlacedModel(placedModel, position);
            }
            // Otherwise, try to load from model file path
            else if (!string.IsNullOrEmpty(modelPath))
            {
                Debug.Log($"[WorldManager] Attempting to load model from path: {modelPath}");
                Debug.Log($"[WorldManager] File exists check: {File.Exists(modelPath)}");
                
                if (modelPath.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[WorldManager] Loading GLB model from: {modelPath}");
                    if (File.Exists(modelPath))
                    {
                        StartCoroutine(LoadGLBModelCoroutine(modelPath, position, itemName));
                        // Return a placeholder to indicate async loading
                        return CreateAsyncLoadingPlaceholder(itemName, position);
                    }
                    else
                    {
                        Debug.LogError($"[WorldManager] GLB file not found: {modelPath}");
                        return null;
                    }
                }
                else if (modelPath.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[WorldManager] Loading Unity prefab from: {modelPath}");
                    if (File.Exists(modelPath))
                    {
                        // For prefabs, try to load immediately using Resources or AssetDatabase
                        placedModel = LoadPrefabImmediately(modelPath, position, itemName);
                        if (placedModel == null)
                        {
                            Debug.LogWarning($"[WorldManager] Immediate prefab load failed, falling back to async load");
                            StartCoroutine(LoadGLBModelCoroutine(modelPath, position, itemName));
                            // Return a placeholder to indicate async loading
                            return CreateAsyncLoadingPlaceholder(itemName, position);
                        }
                        else
                        {
                            Debug.Log($"[WorldManager] ✅ Successfully loaded prefab immediately: {placedModel.name}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[WorldManager] Prefab file not found: {modelPath}");
                        return null;
                    }
                }
                else
                {
                    Debug.LogWarning($"[WorldManager] Unsupported model format: {modelPath}. Supported formats: .glb, .prefab");
                    return null;
                }
            }
            else
            {
                Debug.LogError($"[WorldManager] Cannot place model - no GameObject or valid file path for item: {itemName}");
                return null;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Error placing model in scene: {ex.Message}");
        }

        return placedModel;
    }

    /// <summary>
    /// Places a model GameObject into the scene
    /// </summary>
    public void PlaceModelInScene(GameObject model)
    {
        if (model == null)
        {
            Debug.LogError("[WorldManager] Cannot place null model");
            return;
        }

        // Set position to a reasonable default if not specified
        Vector3 position = new Vector3(0, 1, 0);
        SetupPlacedModel(model, position);
    }
    
    /// <summary>
    /// Places a model GameObject into the scene at a specific position
    /// </summary>
    public void PlaceModelInScene(GameObject model, Vector3 position)
    {
        if (model == null)
        {
            Debug.LogError("[WorldManager] Cannot place null model");
            return;
        }

        Debug.Log($"[WorldManager] 🎯 Placing model at precise position: {position}");
        SetupPlacedModel(model, position);
    }
    
    public GameObject PlaceModelInSceneWithReturn(GameObject model)
    {
        PlaceModelInScene(model);
        return model;
    }

    /// <summary>
    /// Sets up a placed model with proper positioning and components
    /// </summary>
    private void SetupPlacedModel(GameObject model, Vector3 position)
    {
        if (model == null) return;

        Debug.Log($"[WorldManager] 🔧 Setting up placed model: {model.name}");

        // Set position
        model.transform.position = position;
        
        // Ensure model visibility and fix common issues
        EnsureModelVisibility(model);
        
        // Validate materials and textures
        ValidateInstantiatedModel(model);
        
        // Add to generated models list
        if (!generatedModels.Contains(model))
        {
            generatedModels.Add(model);
        }
        
        // Set parent if available
        if (modelsParent != null)
        {
            model.transform.SetParent(modelsParent);
        }
        
        // Add ExposeToEditor component for Runtime Editor integration
        if (model.GetComponent<ExposeToEditor>() == null)
        {
            var exposeComponent = model.AddComponent<ExposeToEditor>();
            Debug.Log($"[WorldManager] Added ExposeToEditor component to {model.name}");
        }
        
        // Ensure the model has proper components for interaction
        EnsureModelHasCollider(model);
        
        // Final visibility verification
        VerifyModelVisibility(model);
        
        // Select this model to show transform handles
        if (runtimeEditor != null && runtimeEditor.Selection != null)
        {
            // Clear previous selection and select this model
            runtimeEditor.Selection.activeGameObject = model;
            Debug.Log($"[WorldManager] Selected model for transform handles: {model.name}");
            
            // Make sure the current transform tool is applied
            if (runtimeTools != null)
            {
                runtimeTools.Current = currentTransformTool;
            }
        }
        
        Debug.Log($"[WorldManager] ✅ Model setup complete: {model.name} at position {position}");
    }

    /// <summary>
    /// Ensures a model has a collider for interaction
    /// </summary>
    private void EnsureModelHasCollider(GameObject model)
    {
        if (model.GetComponent<Collider>() == null)
        {
            // Add a mesh collider if the model has a mesh
            MeshFilter meshFilter = model.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.mesh != null)
            {
                MeshCollider meshCollider = model.AddComponent<MeshCollider>();
                meshCollider.convex = true; // Make it convex for better performance
            }
            else
            {
                // Fallback to box collider
                model.AddComponent<BoxCollider>();
            }
        }
    }
    
    /// <summary>
    /// Ensures a model is visible by fixing common rendering issues
    /// </summary>
    private void EnsureModelVisibility(GameObject model)
    {
        Debug.Log($"[WorldManager] 🔍 Ensuring visibility for model: {model.name}");
        
        // Ensure the GameObject and all children are active
        if (!model.activeInHierarchy)
        {
            model.SetActive(true);
            Debug.Log($"[WorldManager] ✅ Activated model: {model.name}");
        }
        
        // Fix scale issues
        if (model.transform.localScale == Vector3.zero)
        {
            model.transform.localScale = Vector3.one;
            Debug.Log($"[WorldManager] ✅ Fixed zero scale for model: {model.name}");
        }
        else if (model.transform.localScale.magnitude < 0.001f)
        {
            model.transform.localScale = Vector3.one;
            Debug.Log($"[WorldManager] ✅ Fixed tiny scale for model: {model.name}");
        }
        else if (model.transform.localScale.magnitude > 1000f)
        {
            model.transform.localScale = Vector3.one;
            Debug.Log($"[WorldManager] ✅ Fixed huge scale for model: {model.name}");
        }
        
        // Fix rendering components
        var renderers = model.GetComponentsInChildren<MeshRenderer>();
        Debug.Log($"[WorldManager] Found {renderers.Length} MeshRenderer components");
        
        foreach (var renderer in renderers)
        {
            // Enable renderer
            if (!renderer.enabled)
            {
                renderer.enabled = true;
                Debug.Log($"[WorldManager] ✅ Enabled MeshRenderer on {renderer.gameObject.name}");
            }
            
            // Fix materials
            if (renderer.materials == null || renderer.materials.Length == 0)
            {
                renderer.material = CreateDefaultMaterial();
                Debug.Log($"[WorldManager] ✅ Added default material to {renderer.gameObject.name}");
            }
            else
            {
                // Check and fix each material
                var materials = renderer.materials;
                bool materialsFixed = false;
                
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        materials[i] = CreateDefaultMaterial();
                        materialsFixed = true;
                        Debug.Log($"[WorldManager] ✅ Replaced null material in {renderer.gameObject.name}");
                    }
                    else if (materials[i].shader == null || 
                             materials[i].shader.name.Contains("Hidden") || 
                             materials[i].shader.name.Contains("Error"))
                    {
                        materials[i].shader = Shader.Find("Standard");
                        materialsFixed = true;
                        Debug.Log($"[WorldManager] ✅ Fixed shader for material in {renderer.gameObject.name}");
                    }
                }
                
                if (materialsFixed)
                {
                    renderer.materials = materials;
                }
            }
        }
        
        // Ensure proper layer (use Default layer if it's on a culled layer)
        var camera = mainCamera ?? Camera.main ?? FindObjectOfType<Camera>();
        if (camera != null)
        {
            int currentLayer = model.layer;
            if ((camera.cullingMask & (1 << currentLayer)) == 0)
            {
                model.layer = 0; // Default layer
                Debug.Log($"[WorldManager] ✅ Moved model to Default layer for camera visibility");
            }
        }
        
        // Check and fix MeshFilter components
        var meshFilters = model.GetComponentsInChildren<MeshFilter>();
        foreach (var meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh == null)
            {
                Debug.LogWarning($"[WorldManager] ⚠️ MeshFilter on {meshFilter.gameObject.name} has null mesh");
                
                // Fix null mesh by creating a primitive cube mesh
                GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh cubeMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(tempCube);
                
                if (cubeMesh != null)
                {
                    meshFilter.sharedMesh = cubeMesh;
                    Debug.Log($"[WorldManager] 🔧 Fixed null mesh by assigning cube mesh to {meshFilter.gameObject.name}");
                }
                else
                {
                    Debug.LogError($"[WorldManager] ❌ Could not create cube mesh to fix null mesh on {meshFilter.gameObject.name}");
                }
            }
            else
            {
                Debug.Log($"[WorldManager] ✅ MeshFilter on {meshFilter.gameObject.name} has valid mesh: {meshFilter.sharedMesh.name}");
            }
        }
    }
    
    /// <summary>
    /// Creates a default material for models missing materials
    /// </summary>
    private Material CreateDefaultMaterial()
    {
        var material = new Material(Shader.Find("Standard"));
        material.color = Color.white;
        material.name = "WorldManager_Default_Material";
        
        // Make it slightly emissive to ensure visibility
        material.SetColor("_EmissionColor", Color.white * 0.1f);
        material.EnableKeyword("_EMISSION");
        
        return material;
    }
    
    /// <summary>
    /// Verifies that a model should be visible and logs final status
    /// </summary>
    private void VerifyModelVisibility(GameObject model)
    {
        Debug.Log($"[WorldManager] 🔍 Verifying visibility for model: {model.name}");
        
        var issues = new System.Collections.Generic.List<string>();
        
        // Check basic properties
        if (!model.activeInHierarchy)
            issues.Add("GameObject inactive");
        
        if (model.transform.localScale == Vector3.zero)
            issues.Add("Scale is zero");
        
        if (model.transform.localScale.magnitude < 0.001f)
            issues.Add("Scale too small");
        
        // Check renderers
        var renderers = model.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
        {
            issues.Add("No MeshRenderer components");
        }
        else
        {
            int activeRenderers = 0;
            int renderersWithMaterials = 0;
            
            foreach (var renderer in renderers)
            {
                if (renderer.enabled) activeRenderers++;
                if (renderer.materials != null && renderer.materials.Length > 0 && renderer.materials[0] != null)
                    renderersWithMaterials++;
            }
            
            if (activeRenderers == 0)
                issues.Add("All MeshRenderers disabled");
            
            if (renderersWithMaterials == 0)
                issues.Add("No valid materials");
            
            Debug.Log($"[WorldManager] Renderers: {activeRenderers}/{renderers.Length} active, {renderersWithMaterials}/{renderers.Length} with materials");
        }
        
        // Check camera visibility
        var camera = mainCamera ?? Camera.main ?? FindObjectOfType<Camera>();
        if (camera != null)
        {
            int layer = model.layer;
            if ((camera.cullingMask & (1 << layer)) == 0)
                issues.Add($"Layer {layer} not visible to camera");
        }
        
        // Calculate bounds
        var bounds = CalculateModelBounds(model);
        Debug.Log($"[WorldManager] Model bounds: center={bounds.center}, size={bounds.size}");
        
        if (bounds.size == Vector3.zero)
            issues.Add("Model has zero bounds");
        
        // Report results
        if (issues.Count == 0)
        {
            Debug.Log($"[WorldManager] ✅ Model visibility verified: {model.name} should be visible");
        }
        else
        {
            Debug.LogWarning($"[WorldManager] ⚠️ Model visibility issues: {model.name} - {string.Join(", ", issues)}");
        }
    }
    
    /// <summary>
    /// Calculates the combined bounds of a model including all its renderers
    /// </summary>
    private Bounds CalculateModelBounds(GameObject model)
    {
        var bounds = new Bounds(model.transform.position, Vector3.zero);
        var renderers = model.GetComponentsInChildren<Renderer>();
        
        bool hasBounds = false;
        foreach (var renderer in renderers)
        {
            if (hasBounds)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
        }
        
        return bounds;
    }
    
    #endregion
    
    #region Inventory Integration
    
    /// <summary>
    /// Adds a model to the inventory system using reflection to avoid circular dependency
    /// </summary>
    public bool AddModelToInventory(GameObject modelObject, string description, string modelPath = "", string thumbnailPath = "")
    {
        if (modelObject == null)
        {
            Debug.LogError("[WorldManager] Cannot add null model to inventory");
            return false;
        }

        try
        {
            // Generate a unique GUID for the model
            string modelGuid = System.Guid.NewGuid().ToString();
            Debug.Log($"[WorldManager] 🔄 Starting AddModelToInventory workflow:");
            Debug.Log($"  - Model Object: {modelObject.name}");
            Debug.Log($"  - Description: {description}");
            Debug.Log($"  - Generated GUID: {modelGuid}");
            
            // Step 1: Save the model as GLB file with enhanced asset pipeline
            string actualModelPath = SaveModelAsGLBWithAssets(modelObject, description, modelGuid);
            if (string.IsNullOrEmpty(actualModelPath))
            {
                Debug.LogError("[WorldManager] Failed to save model with enhanced asset pipeline, falling back to basic GLB save");
                actualModelPath = SaveModelAsGLB(modelObject, modelGuid, description);
                if (string.IsNullOrEmpty(actualModelPath))
                {
                    Debug.LogError("[WorldManager] Failed to save model as GLB file");
                    return false;
                }
            }
            
            // Step 2: Generate thumbnail
            string actualThumbnailPath = GenerateModelThumbnail(modelObject, modelGuid);
            if (string.IsNullOrEmpty(actualThumbnailPath))
            {
                Debug.LogWarning("[WorldManager] Failed to generate thumbnail, using default");
                actualThumbnailPath = "";
            }
            
            // Step 3: Save metadata file
            SaveModelMetadata(modelGuid, description, actualModelPath, actualThumbnailPath);
            
            // Step 4: Add to inventory systems using reflection (SINGLE CALL TO DATABASE)
            bool addedToInventory = AddToInventorySystems(modelGuid, description, actualModelPath, actualThumbnailPath, modelObject);
            
            if (addedToInventory)
            {
                Debug.Log($"[WorldManager] ✅ Successfully added model to inventory: {description} (GUID: {modelGuid})");
                
                // Refresh inventory UI
                RefreshInventoryUI();
            }
            else
            {
                Debug.LogError($"[WorldManager] ❌ Failed to add model to inventory systems: {description}");
            }
            
            return addedToInventory;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Error adding model to inventory: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Saves a model as GLB file in the inventory models folder
    /// </summary>
    private string SaveModelAsGLB(GameObject modelObject, string guid, string description)
    {
        try
        {
            // Save as Unity prefab instead of GLB to avoid parsing issues
            string fileName = $"{guid}.prefab";
            string filePath = Path.Combine(modelsFolder, fileName);
            
            Debug.Log($"[WorldManager] Saving model as Unity prefab to: {filePath}");
            
            // Ensure the models folder exists
            if (!Directory.Exists(modelsFolder))
            {
                Directory.CreateDirectory(modelsFolder);
                Debug.Log($"[WorldManager] Created models directory: {modelsFolder}");
            }

#if UNITY_EDITOR
            // Create relative path for Unity Asset Database
            string relativePath = "Assets" + filePath.Substring(Application.dataPath.Length).Replace('\\', '/');
            
            // Create a copy of the model for saving
            GameObject tempModel = Instantiate(modelObject);
            tempModel.name = $"{description}_{guid}";
            tempModel.transform.position = Vector3.zero;
            
            try
            {
                // Save as Unity prefab
                GameObject prefabAsset = UnityEditor.PrefabUtility.SaveAsPrefabAsset(tempModel, relativePath);
                
                if (prefabAsset != null)
                {
                    UnityEditor.AssetDatabase.Refresh();
                    Debug.Log($"[WorldManager] Successfully saved model as prefab: {filePath}");
                    return filePath;
                }
                else
                {
                    Debug.LogError($"[WorldManager] Failed to create prefab asset");
                    return "";
                }
            }
            finally
            {
                // Clean up the temporary model
                if (tempModel != null)
                {
                    UnityEditor.EditorApplication.delayCall += () => {
                        if (tempModel != null)
                            DestroyImmediate(tempModel);
                    };
                }
            }
#else
            Debug.LogWarning("[WorldManager] Model saving is only supported in Editor mode");
            return "";
#endif
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Error saving model: {ex.Message}");
            return "";
        }
    }
    
    #endregion
    
    #region GLB Asset Import Pipeline Integration
    
    /// <summary>
    /// Enhanced model saving that creates proper GLB files and Unity assets
    /// </summary>
    public string SaveModelAsGLBWithAssets(GameObject modelObject, string itemName, string modelGuid = "")
    {
        if (modelObject == null)
        {
            Debug.LogError("[WorldManager] Cannot save null model object");
            return null;
        }

        try
        {
            Debug.Log($"[WorldManager] 💾 Starting enhanced asset save process for: {itemName}");
            
            // Use provided GUID or generate new one
            if (string.IsNullOrEmpty(modelGuid))
            {
                modelGuid = System.Guid.NewGuid().ToString();
            }
            
            // Create UUID-based filename (no description in filename)
            string fileName = modelGuid;
            
            // Determine file paths using UUID
            string prefabPath = $"Assets/Inventory/Models/{fileName}.prefab";
            
            // Ensure directory exists
            string directory = Path.Combine(Application.dataPath, "Inventory", "Models");
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            Debug.Log($"[WorldManager] 🔧 Creating Unity assets with UUID: {modelGuid}");
            
#if UNITY_EDITOR
            // Create enhanced prefab with proper Unity assets directly
            try 
            {
                GameObject enhancedPrefab = CreateEnhancedFallbackPrefab(modelObject, prefabPath, modelGuid);
                if (enhancedPrefab != null)
                {
                    Debug.Log($"[WorldManager] ✅ Enhanced prefab created with UUID-based assets: {prefabPath}");
                    ValidateCreatedPrefab(enhancedPrefab);
                    return prefabPath;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[WorldManager] ❌ Enhanced prefab creation failed: {ex.Message}");
            }
#endif
            
            Debug.LogWarning($"[WorldManager] ⚠️ Enhanced save failed, falling back to original method");
            return SaveModelAsGLB(modelObject, System.Guid.NewGuid().ToString(), itemName);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Exception during enhanced prefab save: {ex.Message}");
            return SaveModelAsGLB(modelObject, System.Guid.NewGuid().ToString(), itemName);
        }
    }
    
#if UNITY_EDITOR
    /// <summary>
    /// Create an enhanced fallback prefab that extracts actual mesh/material data
    /// </summary>
    private GameObject CreateEnhancedFallbackPrefab(GameObject sourceModel, string prefabPath, string modelGuid)
    {
        Debug.Log($"[WorldManager] Creating enhanced prefab with UUID: {modelGuid}");
        
        try
        {
            // Create root object with UUID-based name
            GameObject prefabRoot = new GameObject(modelGuid);
            
            // Extract mesh and material data from source
            ExtractAndApplyModelAssets(sourceModel, prefabRoot, prefabPath, modelGuid);
            
            // Create the prefab
            GameObject prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            
            // Clean up temporary object
            DestroyImmediate(prefabRoot);
            
            Debug.Log($"[WorldManager] ✅ Created enhanced UUID-based prefab: {prefabPath}");
            return prefab;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Failed to create enhanced prefab: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Extract mesh and material assets from source model and apply to target with UUID-based naming
    /// </summary>
    private void ExtractAndApplyModelAssets(GameObject source, GameObject target, string prefabPath, string modelGuid)
    {
        string basePath = Path.GetDirectoryName(prefabPath);
        
        // Copy transform
        target.transform.localPosition = source.transform.localPosition;
        target.transform.localRotation = source.transform.localRotation;
        target.transform.localScale = source.transform.localScale;
        
        // Process MeshFilter and MeshRenderer
        MeshFilter sourceMeshFilter = source.GetComponent<MeshFilter>();
        MeshRenderer sourceMeshRenderer = source.GetComponent<MeshRenderer>();
        
        if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null)
        {
            // Create mesh asset with UUID-based name
            Mesh meshCopy = Instantiate(sourceMeshFilter.sharedMesh);
            meshCopy.name = $"{modelGuid}_Mesh";
            
            string meshAssetPath = Path.Combine(basePath, $"{meshCopy.name}.asset");
            UnityEditor.AssetDatabase.CreateAsset(meshCopy, meshAssetPath);
            
            // Add MeshFilter to target
            MeshFilter targetMeshFilter = target.AddComponent<MeshFilter>();
            targetMeshFilter.sharedMesh = meshCopy;
            
            Debug.Log($"[WorldManager] ✅ Created mesh asset: {meshAssetPath}");
        }
        
        if (sourceMeshRenderer != null)
        {
            // Add MeshRenderer to target
            MeshRenderer targetMeshRenderer = target.AddComponent<MeshRenderer>();
            
            if (sourceMeshRenderer.sharedMaterials != null && sourceMeshRenderer.sharedMaterials.Length > 0)
            {
                List<Material> materialAssets = new List<Material>();
                
                Debug.Log($"[WorldManager] 🎨 Processing {sourceMeshRenderer.sharedMaterials.Length} materials from source renderer");
                
                for (int i = 0; i < sourceMeshRenderer.sharedMaterials.Length; i++)
                {
                    Material sourceMat = sourceMeshRenderer.sharedMaterials[i];
                    if (sourceMat != null)
                    {
                        Debug.Log($"[WorldManager] 🎨 Processing material {i}: {sourceMat.name} (Shader: {sourceMat.shader.name})");
                        
                        // Create material asset with UUID-based name
                        Material materialCopy = new Material(sourceMat);
                        materialCopy.name = $"{modelGuid}_Material_{i:D2}";
                        
                        // Ensure material copy has all properties from source
                        CopyMaterialProperties(sourceMat, materialCopy);
                        
                        // Handle textures properly - THIS IS THE CRITICAL STEP
                        Debug.Log($"[WorldManager] 🎨 CALLING ProcessMaterialTextures for material {i}");
                        ProcessMaterialTextures(sourceMat, materialCopy, modelGuid, i, basePath);
                        
                        string materialAssetPath = Path.Combine(basePath, $"{materialCopy.name}.mat");
                        UnityEditor.AssetDatabase.CreateAsset(materialCopy, materialAssetPath);
                        materialAssets.Add(materialCopy);
                        
                        Debug.Log($"[WorldManager] ✅ Created material asset: {materialAssetPath}");
                    }
                }
                
                targetMeshRenderer.sharedMaterials = materialAssets.ToArray();
            }
            else
            {
                // Create default material with UUID-based name
                Material defaultMaterial = new Material(Shader.Find("Standard"));
                defaultMaterial.name = $"{modelGuid}_Material_Default";
                defaultMaterial.color = Color.white;
                
                string defaultMatPath = Path.Combine(basePath, $"{defaultMaterial.name}.mat");
                UnityEditor.AssetDatabase.CreateAsset(defaultMaterial, defaultMatPath);
                
                targetMeshRenderer.sharedMaterial = defaultMaterial;
                Debug.Log($"[WorldManager] ✅ Created default material: {defaultMatPath}");
            }
        }
        
        // Add collider for interaction
        if (target.GetComponent<Collider>() == null)
        {
            // Try to copy collider from source
            Collider sourceCollider = source.GetComponent<Collider>();
            if (sourceCollider != null)
            {
                CopyCollider(sourceCollider, target);
            }
            else
            {
                // Add mesh collider as fallback
                MeshCollider meshCollider = target.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                meshCollider.isTrigger = false;
            }
        }
        
        // Recursively process child objects
        ProcessChildObjects(source, target, modelGuid, basePath);
        
        // Force asset database refresh
        UnityEditor.AssetDatabase.SaveAssets();
        UnityEditor.AssetDatabase.Refresh();
    }

    /// <summary>
    /// Copy all material properties from source to target material
    /// </summary>
    private void CopyMaterialProperties(Material source, Material target)
    {
        // Copy basic properties
        target.shader = source.shader;
        target.color = source.color;
        target.enableInstancing = source.enableInstancing;
        target.doubleSidedGI = source.doubleSidedGI;
        target.globalIlluminationFlags = source.globalIlluminationFlags;
        
        // Copy shader keywords
        target.shaderKeywords = source.shaderKeywords;
        
        // Copy render queue
        target.renderQueue = source.renderQueue;
    }

    /// <summary>
    /// Process and copy textures from source material to target material
    /// </summary>
    private void ProcessMaterialTextures(Material sourceMat, Material targetMat, string modelGuid, int materialIndex, string basePath)
    {
        try
        {
            Debug.Log($"[WorldManager] 🎨 STARTING TEXTURE PROCESSING for material: {sourceMat.name}");
            Debug.Log($"[WorldManager] 🎨 Source material shader: {sourceMat.shader.name}");
            
            // Determine which texture properties to process based on shader
            string[] textureProperties = GetTexturePropertiesForShader(sourceMat.shader);
            
            Debug.Log($"[WorldManager] 🎨 Processing {textureProperties.Length} texture properties for shader: {sourceMat.shader.name}");
            Debug.Log($"[WorldManager] 🎨 Texture properties to check: [{string.Join(", ", textureProperties)}]");
            
            bool anyTexturesFound = false;
            
            foreach (string propName in textureProperties)
            {
                Debug.Log($"[WorldManager] 🔍 Checking texture property: {propName}");
                
                if (sourceMat.HasProperty(propName))
                {
                    Texture sourceTexture = sourceMat.GetTexture(propName);
                    Debug.Log($"[WorldManager] 🔍 Property {propName} exists, texture: {(sourceTexture != null ? sourceTexture.name : "NULL")}");
                    
                    if (sourceTexture != null)
                    {
                        anyTexturesFound = true;
                        Debug.Log($"[WorldManager] ✅ FOUND TEXTURE: {propName} = {sourceTexture.name} (Type: {sourceTexture.GetType()})");
                        Debug.Log($"[WorldManager] ✅ Texture details: {sourceTexture.width}x{sourceTexture.height}, format: {sourceTexture.GetType()}");
                        
                        // Try to copy/reference the texture properly
                        Texture2D texture2D = sourceTexture as Texture2D;
                        if (texture2D != null)
                        {
                            // For runtime-generated textures, we may need to create texture assets
                            string textureName = $"{modelGuid}_Material_{materialIndex:D2}_{GetTextureNameSuffix(propName)}";
                            
                            // Check if this is a built-in Unity texture that we can reference directly
                            string assetPath = UnityEditor.AssetDatabase.GetAssetPath(texture2D);
                            Debug.Log($"[WorldManager] 🔍 Texture asset path: '{assetPath}' (empty = runtime texture)");
                            
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                // Use existing texture asset
                                targetMat.SetTexture(propName, texture2D);
                                Debug.Log($"[WorldManager] ✅ Referenced existing texture asset: {assetPath} for {propName}");
                            }
                            else
                            {
                                // This is a runtime-generated texture, copy it to assets
                                Debug.Log($"[WorldManager] 🔄 Creating texture asset for runtime texture: {sourceTexture.name}");
                                
                                try
                                {
                                    Texture2D textureCopy = DuplicateTexture(texture2D);
                                    textureCopy.name = textureName;
                                    
                                    string textureAssetPath = Path.Combine(basePath, $"{textureName}.asset");
                                    UnityEditor.AssetDatabase.CreateAsset(textureCopy, textureAssetPath);
                                    
                                    targetMat.SetTexture(propName, textureCopy);
                                    Debug.Log($"[WorldManager] ✅ SUCCESSFULLY CREATED texture asset: {textureAssetPath} for {propName}");
                                }
                                catch (System.Exception texEx)
                                {
                                    Debug.LogError($"[WorldManager] ❌ FAILED to create texture asset for {propName}: {texEx.Message}");
                                    
                                    // Try to reference the original texture as fallback
                                    targetMat.SetTexture(propName, sourceTexture);
                                    Debug.Log($"[WorldManager] 🔄 Used original texture as fallback for {propName}");
                                }
                            }
                        }
                        else
                        {
                            // Non-Texture2D textures (like RenderTexture), reference directly
                            targetMat.SetTexture(propName, sourceTexture);
                            Debug.Log($"[WorldManager] ✅ Referenced non-Texture2D: {sourceTexture.name} ({sourceTexture.GetType()}) for {propName}");
                        }
                        
                        // Copy texture scale and offset if the property supports it
                        try
                        {
                            targetMat.SetTextureScale(propName, sourceMat.GetTextureScale(propName));
                            targetMat.SetTextureOffset(propName, sourceMat.GetTextureOffset(propName));
                            Debug.Log($"[WorldManager] ✅ Copied texture scale/offset for {propName}");
                        }
                        catch (System.Exception)
                        {
                            // Some shaders don't support scale/offset for all texture properties
                            Debug.Log($"[WorldManager] ⚠️ Texture scale/offset not supported for {propName}");
                        }
                    }
                    else
                    {
                        Debug.Log($"[WorldManager] ❌ Texture property {propName} is NULL");
                    }
                }
                else
                {
                    Debug.Log($"[WorldManager] ❌ Material doesn't have texture property: {propName}");
                }
            }
            
            if (!anyTexturesFound)
            {
                Debug.LogWarning($"[WorldManager] ⚠️ NO TEXTURES FOUND in source material {sourceMat.name}!");
                
                // List ALL properties on the material for debugging
                Debug.Log($"[WorldManager] 🔍 ALL MATERIAL PROPERTIES:");
                var shader = sourceMat.shader;
                for (int i = 0; i < shader.GetPropertyCount(); i++)
                {
                    var propType = shader.GetPropertyType(i);
                    var propName = shader.GetPropertyName(i);
                    Debug.Log($"[WorldManager] 🔍   {i}: {propName} (Type: {propType})");
                    
                    if (propType == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        var tex = sourceMat.GetTexture(propName);
                        Debug.Log($"[WorldManager] 🔍     Texture value: {(tex != null ? tex.name : "NULL")}");
                    }
                }
            }
            else
            {
                Debug.Log($"[WorldManager] ✅ TEXTURE PROCESSING COMPLETED for {sourceMat.name} - found textures!");
            }
            
            // Copy float and color properties
            CopyMaterialFloatAndColorProperties(sourceMat, targetMat);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] ❌ FAILED to process textures for material {materialIndex}: {ex.Message}");
            Debug.LogError($"[WorldManager] ❌ Exception details: {ex}");
        }
    }
    
    /// <summary>
    /// Get texture property names based on shader type
    /// </summary>
    private string[] GetTexturePropertiesForShader(Shader shader)
    {
        if (shader == null) return new string[0];
        
        string shaderName = shader.name;
        
        if (shaderName.Contains("Piglet/MetallicRoughness"))
        {
            // Piglet glTF shader properties (note: lowercase property names)
            return new string[] { "_baseColorTexture", "_metallicRoughnessTexture", "_normalTexture", "_occlusionTexture", "_emissiveTexture" };
        }
        else if (shaderName.Contains("Standard"))
        {
            // Unity Standard shader properties
            return new string[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap", "_DetailMask", "_DetailAlbedoMap", "_DetailNormalMap" };
        }
        else if (shaderName.Contains("URP/Lit") || shaderName.Contains("Universal Render Pipeline"))
        {
            // URP Lit shader properties
            return new string[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" };
        }
        else
        {
            // Generic fallback - try common property names
            return new string[] { "_MainTex", "_BaseColorTexture", "_BaseMap", "_Albedo", "_Diffuse" };
        }
    }
    
    /// <summary>
    /// Get a clean texture name suffix from property name
    /// </summary>
    private string GetTextureNameSuffix(string propertyName)
    {
        // Remove underscore prefix and convert to readable names
        switch (propertyName)
        {
            case "_MainTex":
            case "_BaseColorTexture":
            case "_baseColorTexture":
            case "_BaseMap":
                return "Albedo";
            case "_BumpMap":
            case "_NormalTexture":
            case "_normalTexture":
                return "Normal";
            case "_MetallicGlossMap":
            case "_MetallicRoughnessTexture":
            case "_metallicRoughnessTexture":
                return "MetallicRoughness";
            case "_OcclusionMap":
            case "_OcclusionTexture":
            case "_occlusionTexture":
                return "Occlusion";
            case "_EmissionMap":
            case "_EmissiveTexture":
            case "_emissiveTexture":
                return "Emission";
            default:
                return propertyName.StartsWith("_") ? propertyName.Substring(1) : propertyName;
        }
    }

    /// <summary>
    /// Copy float and color properties from source to target material
    /// </summary>
    private void CopyMaterialFloatAndColorProperties(Material source, Material target)
    {
        string shaderName = source.shader.name;
        
        if (shaderName.Contains("Piglet/MetallicRoughness"))
        {
            CopyPigletMaterialProperties(source, target);
        }
        else if (shaderName.Contains("Standard"))
        {
            CopyStandardMaterialProperties(source, target);
        }
        else
        {
            CopyGenericMaterialProperties(source, target);
        }
    }
    
    /// <summary>
    /// Copy Piglet shader specific properties
    /// </summary>
    private void CopyPigletMaterialProperties(Material source, Material target)
    {
        // Piglet shader properties (note: lowercase names to match actual shader)
        string[] floatProps = { "_metallicFactor", "_roughnessFactor", "_normalScale", "_occlusionStrength", "_alphaCutoff", "_linear", "_runtime" };
        string[] colorProps = { "_baseColorFactor", "_emissiveFactor" };
        string[] vectorProps = { "_baseColorTexture_ST", "_metallicRoughnessTexture_ST", "_normalTexture_ST", "_occlusionTexture_ST", "_emissiveTexture_ST" };
        
        foreach (string prop in floatProps)
        {
            if (source.HasProperty(prop) && target.HasProperty(prop))
            {
                target.SetFloat(prop, source.GetFloat(prop));
                Debug.Log($"[WorldManager] Copied float property {prop}: {source.GetFloat(prop)}");
            }
        }
        
        foreach (string prop in colorProps)
        {
            if (source.HasProperty(prop) && target.HasProperty(prop))
            {
                target.SetColor(prop, source.GetColor(prop));
                Debug.Log($"[WorldManager] Copied color property {prop}: {source.GetColor(prop)}");
            }
        }
        
        foreach (string prop in vectorProps)
        {
            if (source.HasProperty(prop) && target.HasProperty(prop))
            {
                target.SetVector(prop, source.GetVector(prop));
                Debug.Log($"[WorldManager] Copied vector property {prop}: {source.GetVector(prop)}");
            }
        }
    }
    
    /// <summary>
    /// Copy Standard shader specific properties
    /// </summary>
    private void CopyStandardMaterialProperties(Material source, Material target)
    {
        // Standard shader properties
        string[] floatProps = { "_Cutoff", "_Glossiness", "_GlossMapScale", "_SmoothnessTextureChannel", "_Metallic", "_BumpScale", "_Parallax", "_OcclusionStrength", "_DetailNormalMapScale" };
        string[] colorProps = { "_Color", "_EmissionColor" };
        
        foreach (string prop in floatProps)
        {
            if (source.HasProperty(prop) && target.HasProperty(prop))
            {
                target.SetFloat(prop, source.GetFloat(prop));
            }
        }
        
        foreach (string prop in colorProps)
        {
            if (source.HasProperty(prop) && target.HasProperty(prop))
            {
                target.SetColor(prop, source.GetColor(prop));
            }
        }
    }
    
    /// <summary>
    /// Copy generic material properties
    /// </summary>
    private void CopyGenericMaterialProperties(Material source, Material target)
    {
        // Try common property names
        string[] commonFloats = { "_Metallic", "_Smoothness", "_Roughness", "_Glossiness", "_BumpScale", "_Cutoff" };
        string[] commonColors = { "_Color", "_BaseColor", "_Albedo", "_Tint", "_EmissionColor" };
        
        foreach (string prop in commonFloats)
        {
            if (source.HasProperty(prop) && target.HasProperty(prop))
            {
                target.SetFloat(prop, source.GetFloat(prop));
            }
        }
        
        foreach (string prop in commonColors)
        {
            if (source.HasProperty(prop) && target.HasProperty(prop))
            {
                target.SetColor(prop, source.GetColor(prop));
            }
        }
    }

    /// <summary>
    /// Create a copy of a Texture2D
    /// </summary>
    private Texture2D DuplicateTexture(Texture2D source)
    {
        try
        {
            Debug.Log($"[WorldManager] 🔄 Duplicating texture: {source.name} ({source.width}x{source.height}, format: {source.format}, readable: {source.isReadable})");
            
            // Create readable copy using Graphics.Blit
            RenderTexture renderTex = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, renderTex);
            
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;
            
            Texture2D readableText = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            readableText.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);
            
            Debug.Log($"[WorldManager] ✅ Successfully duplicated texture: {readableText.name}");
            return readableText;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] ❌ Failed to duplicate texture {source.name}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Process child objects recursively
    /// </summary>
    private void ProcessChildObjects(GameObject source, GameObject target, string modelGuid, string basePath)
    {
        for (int i = 0; i < source.transform.childCount; i++)
        {
            Transform sourceChild = source.transform.GetChild(i);
            
            // Create child object
            GameObject targetChild = new GameObject(sourceChild.name);
            targetChild.transform.SetParent(target.transform);
            
            // Copy transform properties
            targetChild.transform.localPosition = sourceChild.localPosition;
            targetChild.transform.localRotation = sourceChild.localRotation;
            targetChild.transform.localScale = sourceChild.localScale;
            
            // Process child's components directly without creating separate prefab
            ProcessChildObjectComponents(sourceChild.gameObject, targetChild, modelGuid, basePath, i);
        }
    }
    
    /// <summary>
    /// Process components for a child object
    /// </summary>
    private void ProcessChildObjectComponents(GameObject source, GameObject target, string modelGuid, string basePath, int childIndex)
    {
        // Process MeshFilter and MeshRenderer for child
        MeshFilter sourceMeshFilter = source.GetComponent<MeshFilter>();
        MeshRenderer sourceMeshRenderer = source.GetComponent<MeshRenderer>();
        
        if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null)
        {
            // Create mesh asset with child-specific UUID-based name
            Mesh meshCopy = Instantiate(sourceMeshFilter.sharedMesh);
            meshCopy.name = $"{modelGuid}_Child_{childIndex}_Mesh";
            
            string meshAssetPath = Path.Combine(basePath, $"{meshCopy.name}.asset");
            UnityEditor.AssetDatabase.CreateAsset(meshCopy, meshAssetPath);
            
            // Add MeshFilter to target
            MeshFilter targetMeshFilter = target.AddComponent<MeshFilter>();
            targetMeshFilter.sharedMesh = meshCopy;
            
            Debug.Log($"[WorldManager] ✅ Created child mesh asset: {meshAssetPath}");
        }
        
        if (sourceMeshRenderer != null)
        {
            // Add MeshRenderer to target
            MeshRenderer targetMeshRenderer = target.AddComponent<MeshRenderer>();
            
            if (sourceMeshRenderer.sharedMaterials != null && sourceMeshRenderer.sharedMaterials.Length > 0)
            {
                List<Material> materialAssets = new List<Material>();
                
                for (int i = 0; i < sourceMeshRenderer.sharedMaterials.Length; i++)
                {
                    Material sourceMat = sourceMeshRenderer.sharedMaterials[i];
                    if (sourceMat != null)
                    {
                        // Create material asset with child-specific UUID-based name
                        Material materialCopy = new Material(sourceMat);
                        materialCopy.name = $"{modelGuid}_Child_{childIndex}_Material_{i:D2}";
                        
                        // Ensure material copy has all properties from source
                        CopyMaterialProperties(sourceMat, materialCopy);
                        
                        // Handle textures properly
                        ProcessMaterialTextures(sourceMat, materialCopy, $"{modelGuid}_Child_{childIndex}", i, basePath);
                        
                        string materialAssetPath = Path.Combine(basePath, $"{materialCopy.name}.mat");
                        UnityEditor.AssetDatabase.CreateAsset(materialCopy, materialAssetPath);
                        materialAssets.Add(materialCopy);
                        
                        Debug.Log($"[WorldManager] ✅ Created child material asset: {materialAssetPath}");
                    }
                }
                
                targetMeshRenderer.sharedMaterials = materialAssets.ToArray();
            }
            else
            {
                // Create default material with child-specific UUID-based name
                Material defaultMaterial = new Material(Shader.Find("Standard"));
                defaultMaterial.name = $"{modelGuid}_Child_{childIndex}_Material_Default";
                defaultMaterial.color = Color.white;
                
                string defaultMatPath = Path.Combine(basePath, $"{defaultMaterial.name}.mat");
                UnityEditor.AssetDatabase.CreateAsset(defaultMaterial, defaultMatPath);
                
                targetMeshRenderer.sharedMaterial = defaultMaterial;
                Debug.Log($"[WorldManager] ✅ Created child default material: {defaultMatPath}");
            }
        }
        
        // Add collider for child if needed
        if (target.GetComponent<Collider>() == null)
        {
            Collider sourceCollider = source.GetComponent<Collider>();
            if (sourceCollider != null)
            {
                CopyCollider(sourceCollider, target);
            }
            else if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null)
            {
                // Add mesh collider as fallback for child objects with meshes
                MeshCollider meshCollider = target.AddComponent<MeshCollider>();
                meshCollider.convex = true;
                meshCollider.isTrigger = false;
            }
        }
        
        // Recursively process nested children
        ProcessChildObjects(source, target, modelGuid, basePath);
    }

    /// <summary>
    /// Copy collider component from source to target
    /// </summary>
    private void CopyCollider(Collider sourceCollider, GameObject target)
    {
        if (sourceCollider is BoxCollider)
        {
            BoxCollider newCollider = target.AddComponent<BoxCollider>();
            BoxCollider sourceBox = sourceCollider as BoxCollider;
            newCollider.center = sourceBox.center;
            newCollider.size = sourceBox.size;
            newCollider.isTrigger = sourceBox.isTrigger;
        }
        else if (sourceCollider is SphereCollider)
        {
            SphereCollider newCollider = target.AddComponent<SphereCollider>();
            SphereCollider sourceSphere = sourceCollider as SphereCollider;
            newCollider.center = sourceSphere.center;
            newCollider.radius = sourceSphere.radius;
            newCollider.isTrigger = sourceSphere.isTrigger;
        }
        else if (sourceCollider is MeshCollider)
        {
            MeshCollider newCollider = target.AddComponent<MeshCollider>();
            MeshCollider sourceMesh = sourceCollider as MeshCollider;
            newCollider.sharedMesh = sourceMesh.sharedMesh;
            newCollider.convex = sourceMesh.convex;
            newCollider.isTrigger = sourceMesh.isTrigger;
        }
        else
        {
            // Fallback to mesh collider
            MeshCollider meshCollider = target.AddComponent<MeshCollider>();
            meshCollider.convex = true;
            meshCollider.isTrigger = false;
        }
    }
    
    /// <summary>
    /// Validate that a created prefab has proper mesh and material references
    /// </summary>
    private void ValidateCreatedPrefab(GameObject prefab)
    {
        if (prefab == null) return;
        
        Debug.Log($"[WorldManager] 🔍 Validating created prefab: {prefab.name}");
        
        MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>();
        MeshRenderer[] meshRenderers = prefab.GetComponentsInChildren<MeshRenderer>();
        
        bool hasValidMesh = false;
        bool hasValidMaterial = false;
        int meshCount = 0;
        int materialCount = 0;
        int textureCount = 0;
        
        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                hasValidMesh = true;
                meshCount++;
                
                string meshAssetPath = UnityEditor.AssetDatabase.GetAssetPath(mf.sharedMesh);
                if (string.IsNullOrEmpty(meshAssetPath))
                {
                    Debug.LogWarning($"[WorldManager] ⚠️ Mesh not saved as asset: {mf.sharedMesh.name} on {mf.gameObject.name}");
                }
                else
                {
                    Debug.Log($"[WorldManager] ✅ Mesh asset valid: {mf.sharedMesh.name} ({mf.sharedMesh.vertexCount} vertices) at {meshAssetPath}");
                }
                
                if (mf.sharedMesh.name.Contains("Cube"))
                {
                    Debug.LogWarning($"[WorldManager] ⚠️ Prefab still using cube mesh: {mf.sharedMesh.name}");
                }
            }
            else
            {
                Debug.LogError($"[WorldManager] ❌ MeshFilter has null mesh on {mf.gameObject.name}");
            }
        }
        
        foreach (MeshRenderer mr in meshRenderers)
        {
            if (mr.sharedMaterials != null && mr.sharedMaterials.Length > 0)
            {
                foreach (Material mat in mr.sharedMaterials)
                {
                    if (mat != null)
                    {
                        hasValidMaterial = true;
                        materialCount++;
                        
                        string materialAssetPath = UnityEditor.AssetDatabase.GetAssetPath(mat);
                        if (string.IsNullOrEmpty(materialAssetPath))
                        {
                            Debug.LogWarning($"[WorldManager] ⚠️ Material not saved as asset: {mat.name} on {mr.gameObject.name}");
                        }
                        else
                        {
                            Debug.Log($"[WorldManager] ✅ Material asset valid: {mat.name} at {materialAssetPath}");
                        }
                        
                        // Check material textures
                        Texture mainTex = mat.mainTexture;
                        if (mainTex != null)
                        {
                            textureCount++;
                            string textureAssetPath = UnityEditor.AssetDatabase.GetAssetPath(mainTex);
                            if (string.IsNullOrEmpty(textureAssetPath))
                            {
                                Debug.LogWarning($"[WorldManager] ⚠️ Texture not saved as asset: {mainTex.name}");
                            }
                            else
                            {
                                Debug.Log($"[WorldManager] ✅ Texture asset valid: {mainTex.name} at {textureAssetPath}");
                            }
                        }
                        
                        // Check shader
                        if (mat.shader == null)
                        {
                            Debug.LogError($"[WorldManager] ❌ Material has null shader: {mat.name}");
                        }
                        else
                        {
                            Debug.Log($"[WorldManager] ✅ Material shader: {mat.shader.name}");
                        }
                    }
                    else
                    {
                        Debug.LogError($"[WorldManager] ❌ MeshRenderer has null material on {mr.gameObject.name}");
                    }
                }
            }
            else
            {
                Debug.LogError($"[WorldManager] ❌ MeshRenderer has no materials on {mr.gameObject.name}");
            }
        }
        
        // Validation summary
        Debug.Log($"[WorldManager] 📊 Prefab validation summary for {prefab.name}:");
        Debug.Log($"  - Meshes: {meshCount}");
        Debug.Log($"  - Materials: {materialCount}");
        Debug.Log($"  - Textures: {textureCount}");
        
        if (hasValidMesh && hasValidMaterial)
        {
            Debug.Log($"[WorldManager] ✅ Prefab validation PASSED: {prefab.name}");
        }
        else
        {
            Debug.LogError($"[WorldManager] ❌ Prefab validation FAILED: {prefab.name} - missing valid mesh or material");
        }
    }
    
    /// <summary>
    /// Validate instantiated model in scene to ensure proper material/texture references
    /// </summary>
    public void ValidateInstantiatedModel(GameObject instantiatedModel)
    {
        if (instantiatedModel == null) return;
        
        Debug.Log($"[WorldManager] 🔍 Validating instantiated model: {instantiatedModel.name}");
        
        MeshRenderer[] renderers = instantiatedModel.GetComponentsInChildren<MeshRenderer>();
        bool allMaterialsValid = true;
        int validMaterials = 0;
        int validTextures = 0;
        
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer.materials != null)
            {
                foreach (Material mat in renderer.materials)
                {
                    if (mat == null)
                    {
                        Debug.LogError($"[WorldManager] ❌ Null material found on {renderer.gameObject.name}");
                        allMaterialsValid = false;
                    }
                    else
                    {
                        validMaterials++;
                        
                        if (mat.shader == null)
                        {
                            Debug.LogError($"[WorldManager] ❌ Material {mat.name} has null shader on {renderer.gameObject.name}");
                            allMaterialsValid = false;
                        }
                        
                        // Check textures
                        if (mat.mainTexture != null)
                        {
                            validTextures++;
                            Debug.Log($"[WorldManager] ✅ Material {mat.name} has texture: {mat.mainTexture.name}");
                        }
                        else
                        {
                            Debug.Log($"[WorldManager] ℹ️ Material {mat.name} has no main texture (may be intentional)");
                        }
                    }
                }
            }
            else
            {
                Debug.LogError($"[WorldManager] ❌ Renderer on {renderer.gameObject.name} has no materials array");
                allMaterialsValid = false;
            }
        }
        
        Debug.Log($"[WorldManager] 📊 Instantiated model validation summary for {instantiatedModel.name}:");
        Debug.Log($"  - Valid materials: {validMaterials}");
        Debug.Log($"  - Valid textures: {validTextures}");
        Debug.Log($"  - Overall status: {(allMaterialsValid ? "✅ PASSED" : "❌ FAILED")}");
    }
#endif
    
    /// <summary>
    /// Sanitize filename for safe file system usage
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "GeneratedModel";
        }
        
        string sanitized = fileName;
        
        // Remove invalid characters
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(c, '_');
        }
        
        // Limit filename length to prevent filesystem errors
        // Reserve space for timestamp and extension: "_yyyyMMdd_HHmmss.glb" = ~20 chars
        // Safe limit: 100 characters for the description part
        const int maxLength = 100;
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength).Trim();
            // Add ellipsis to indicate truncation
            if (sanitized.Length > 3)
            {
                sanitized = sanitized.Substring(0, sanitized.Length - 3) + "...";
            }
        }
        
        // Remove any trailing periods or spaces that might cause issues
        sanitized = sanitized.TrimEnd('.', ' ');
        
        // Ensure we have a valid filename
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "GeneratedModel";
        }
        
        return sanitized;
    }

#if UNITY_EDITOR
    // Import GLBAssetImportPipeline in runtime context by making it accessible
    private static System.Type GLBAssetImportPipelineType
    {
        get
        {
            return System.Type.GetType("GLBAssetImportPipeline");
        }
    }
#endif

    /// <summary>
    /// Load GLB model using Piglet and place in scene
    /// </summary>
    private IEnumerator LoadGLBModelCoroutine(string modelPath, Vector3 position, string itemName)
    {
        Debug.Log($"[WorldManager] Starting async GLB load for: {modelPath}");
        
        if (modelPath.EndsWith(".prefab"))
        {
            // Load Unity prefab using AssetDatabase in editor
#if UNITY_EDITOR
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath.Replace(Application.dataPath, "Assets"));
            if (prefabAsset != null)
            {
                GameObject instance = Instantiate(prefabAsset, position, Quaternion.identity);
                SetupPlacedModel(instance, position);
                Debug.Log($"[WorldManager] ✅ Successfully loaded Unity prefab: {instance.name}");
                yield break;
            }
#endif
            Debug.LogError($"[WorldManager] Failed to load Unity prefab: {modelPath}");
            yield break;
        }

        // Load GLB file using Piglet
        if (!File.Exists(modelPath))
        {
            Debug.LogError($"[WorldManager] GLB file not found: {modelPath}");
            yield break;
        }

        // Use Piglet to import GLB
        var importOptions = new GltfImportOptions();
        var importTask = RuntimeGltfImporter.GetImportTask(null, File.ReadAllBytes(modelPath), importOptions);
        
        // Set up completion callback to capture the result
        GameObject importResult = null;
        bool importCompleted = false;
        bool importFailed = false;
        string importError = "";
        
        importTask.OnCompleted = (model) => {
            importResult = model;
            importCompleted = true;
        };
        
        importTask.OnException = (ex) => {
            importError = ex.Message;
            importFailed = true;
        };
        
        // Advance the import task until completion
        while (!importCompleted && !importFailed && importTask.State == GltfImportTask.ExecutionState.Running)
        {
            importTask.MoveNext();
            yield return null;
        }

        if (importFailed)
        {
            Debug.LogError($"[WorldManager] GLB import failed: {importError}");
            yield break;
        }
        if (importResult != null)
        {
            importResult.transform.position = position;
            SetupPlacedModel(importResult, position);
            Debug.Log($"[WorldManager] ✅ Successfully loaded GLB model: {importResult.name}");
        }
        else
        {
            Debug.LogError($"[WorldManager] GLB import returned null GameObject");
        }
    }

    /// <summary>
    /// Create a temporary placeholder while loading models asynchronously
    /// </summary>
    private GameObject CreateAsyncLoadingPlaceholder(string itemName, Vector3 position)
    {
        GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        placeholder.name = $"Loading_{itemName}";
        placeholder.transform.position = position;
        placeholder.transform.localScale = Vector3.one * 0.5f;
        
        // Add a loading material/color
        Renderer renderer = placeholder.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.yellow;
        }
        
        // Add a simple identifier to show this is a placeholder
        placeholder.tag = "LoadingPlaceholder";
        
        Debug.Log($"[WorldManager] Created loading placeholder for: {itemName}");
        return placeholder;
    }

    /// <summary>
    /// Load Unity prefab immediately (synchronous)
    /// </summary>
    private GameObject LoadPrefabImmediately(string prefabPath, Vector3 position, string itemName)
    {
        try
        {
#if UNITY_EDITOR
            // In editor, use AssetDatabase
            string assetPath = prefabPath.Replace(Application.dataPath, "Assets");
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefabAsset != null)
            {
                GameObject instance = Instantiate(prefabAsset, position, Quaternion.identity);
                SetupPlacedModel(instance, position);
                return instance;
            }
#endif

            // Try Resources.Load if prefab is in Resources folder
            string resourcePath = prefabPath.Replace(Application.dataPath + "/Resources/", "").Replace(".prefab", "");
            GameObject resourcePrefab = Resources.Load<GameObject>(resourcePath);
            if (resourcePrefab != null)
            {
                GameObject instance = Instantiate(resourcePrefab, position, Quaternion.identity);
                SetupPlacedModel(instance, position);
                return instance;
            }

            Debug.LogWarning($"[WorldManager] Could not load prefab immediately: {prefabPath}");
            return null;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Error loading prefab immediately: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Generate thumbnail image for a model
    /// </summary>
    private string GenerateModelThumbnail(GameObject modelObject, string modelGuid)
    {
        try
        {
            string thumbnailPath = Path.Combine(thumbnailsFolder, $"{modelGuid}.png");
            
            // Create a temporary camera for thumbnail generation
            GameObject tempCameraObj = new GameObject("ThumbnailCamera");
            Camera thumbnailCamera = tempCameraObj.AddComponent<Camera>();
            
            // Position camera to capture the model
            Bounds modelBounds = CalculateModelBounds(modelObject);
            Vector3 cameraPos = modelBounds.center + Vector3.back * (modelBounds.size.magnitude * 2f);
            thumbnailCamera.transform.position = cameraPos;
            thumbnailCamera.transform.LookAt(modelBounds.center);
            
            // Set up camera for thumbnail
            thumbnailCamera.backgroundColor = Color.clear;
            thumbnailCamera.clearFlags = CameraClearFlags.SolidColor;
            
            // Create render texture
            RenderTexture renderTexture = new RenderTexture(256, 256, 24);
            thumbnailCamera.targetTexture = renderTexture;
            
            // Render
            thumbnailCamera.Render();
            
            // Read pixels and save as PNG
            RenderTexture.active = renderTexture;
            Texture2D thumbnail = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            thumbnail.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
            thumbnail.Apply();
            
            byte[] pngData = thumbnail.EncodeToPNG();
            File.WriteAllBytes(thumbnailPath, pngData);
            
            // Cleanup
            RenderTexture.active = null;
            DestroyImmediate(renderTexture);
            DestroyImmediate(thumbnail);
            DestroyImmediate(tempCameraObj);
            
            Debug.Log($"[WorldManager] Generated thumbnail: {thumbnailPath}");
            return thumbnailPath;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Failed to generate thumbnail: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// Save model metadata to .model file
    /// </summary>
    private void SaveModelMetadata(string modelGuid, string description, string modelPath, string thumbnailPath)
    {
        try
        {
            string metadataPath = Path.Combine(modelsFolder, $"{modelGuid}.model");
            string metadata = $"Description: {description}\nModelPath: {modelPath}\nThumbnailPath: {thumbnailPath}\nGUID: {modelGuid}\nCreated: {System.DateTime.Now}";
            
            File.WriteAllText(metadataPath, metadata);
            Debug.Log($"[WorldManager] Saved metadata: {metadataPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Failed to save metadata: {ex.Message}");
        }
    }

    /// <summary>
    /// Add model to inventory systems using reflection
    /// </summary>
    private bool AddToInventorySystems(string modelGuid, string description, string modelPath, string thumbnailPath, GameObject modelObject)
    {
        try
        {
            // Find InventoryManager
            InventoryManager inventoryManager = FindObjectOfType<InventoryManager>();
            if (inventoryManager == null)
            {
                Debug.LogWarning("[WorldManager] InventoryManager not found, creating entry manually");
                return true; // Consider this successful for now
            }

            // Generate descriptive item name from the description
            string descriptiveItemName = GenerateDescriptiveItemName(description);
            
            // Create proper InventoryItem object
            var inventoryItem = new InventoryManager.InventoryItem(modelGuid, descriptiveItemName, description)
            {
                modelPath = modelPath,
                thumbnailPath = thumbnailPath,
                gameObject = modelObject
            };
            
            // Call AddItem method directly (no need for reflection)
            inventoryManager.AddItem(inventoryItem);
            Debug.Log($"[WorldManager] Added to inventory: {description}");
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Failed to add to inventory systems: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Refresh the inventory UI
    /// </summary>
    private void RefreshInventoryUI()
    {
        try
        {
            // Find InventoryUI
            InventoryUI inventoryUI = FindObjectOfType<InventoryUI>();
            if (inventoryUI != null)
            {
                var refreshMethod = typeof(InventoryUI).GetMethod("RefreshInventoryDisplay", 
                    BindingFlags.Public | BindingFlags.Instance);
                
                if (refreshMethod != null)
                {
                    refreshMethod.Invoke(inventoryUI, null);
                    Debug.Log("[WorldManager] Inventory UI refreshed");
                }
                else
                {
                    // Try alternative method names
                    var altMethod = typeof(InventoryUI).GetMethod("RefreshDisplay", 
                        BindingFlags.Public | BindingFlags.Instance);
                        
                    if (altMethod != null)
                    {
                        altMethod.Invoke(inventoryUI, null);
                        Debug.Log("[WorldManager] Inventory UI refreshed via RefreshDisplay");
                    }
                    else
                    {
                        var forceMethod = typeof(InventoryUI).GetMethod("ForceCompleteRefresh", 
                            BindingFlags.Public | BindingFlags.Instance);
                            
                        if (forceMethod != null)
                        {
                            forceMethod.Invoke(inventoryUI, null);
                            Debug.Log("[WorldManager] Inventory UI refreshed via ForceCompleteRefresh");
                        }
                        else
                        {
                            Debug.LogWarning("[WorldManager] No suitable refresh method found on InventoryUI");
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("[WorldManager] InventoryUI not found");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WorldManager] Failed to refresh inventory UI: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate a descriptive, context-sensitive name from a longer description
    /// </summary>
    private string GenerateDescriptiveItemName(string description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return "Generated Model";
        }

        try
        {
            // Clean up the description
            string cleaned = description.Trim();
            
            // Common patterns to identify key elements
            string[] keyPhrases = {
                "depicts a", "depicts an", "shows a", "shows an", "features a", "features an",
                "represents a", "represents an", "is a", "is an", "model of a", "model of an"
            };

            // Find the main subject after key phrases
            foreach (string phrase in keyPhrases)
            {
                int index = cleaned.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    string afterPhrase = cleaned.Substring(index + phrase.Length).Trim();
                    string subject = ExtractMainSubject(afterPhrase);
                    if (!string.IsNullOrEmpty(subject))
                    {
                        return subject;
                    }
                }
            }

            // Fallback: Extract first meaningful sentence
            string[] sentences = cleaned.Split('.', '!', '?');
            if (sentences.Length > 0)
            {
                string firstSentence = sentences[0].Trim();
                if (firstSentence.Length > 10 && firstSentence.Length <= 50)
                {
                    return CapitalizeWords(firstSentence);
                }
                else if (firstSentence.Length > 50)
                {
                    // Extract key nouns and adjectives
                    return ExtractKeyWords(firstSentence);
                }
            }

            // Last resort: Use first few words
            string[] words = cleaned.Split(' ');
            if (words.Length >= 3)
            {
                return CapitalizeWords(string.Join(" ", words.Take(Math.Min(4, words.Length))));
            }

            return "Generated Model";
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[WorldManager] Failed to generate descriptive name: {ex.Message}");
            return "Generated Model";
        }
    }

    /// <summary>
    /// Extract the main subject from text after key phrases
    /// </summary>
    private string ExtractMainSubject(string text)
    {
        // Look for patterns like "fierce pirate", "stealthy ninja", etc.
        string[] words = text.Split(' ');
        List<string> subject = new List<string>();
        
        for (int i = 0; i < Math.Min(words.Length, 4); i++)
        {
            string word = words[i].Trim(',', '.', '!', '?', ';', ':');
            if (string.IsNullOrEmpty(word)) continue;
            
            // Stop at common connecting words
            if (IsConnectingWord(word.ToLower()))
            {
                break;
            }
            
            subject.Add(word);
        }
        
        if (subject.Count > 0)
        {
            return CapitalizeWords(string.Join(" ", subject));
        }
        
        return "";
    }

    /// <summary>
    /// Extract key descriptive words from longer text
    /// </summary>
    private string ExtractKeyWords(string text)
    {
        string[] words = text.Split(' ');
        List<string> keyWords = new List<string>();
        
        // Look for important descriptive words
        string[] importantWords = { "pirate", "ninja", "warrior", "knight", "robot", "creature", "monster", 
                                   "fierce", "stealthy", "armored", "mighty", "ancient", "mystical", "battle", "combat" };
        
        foreach (string word in words)
        {
            string cleanWord = word.Trim(',', '.', '!', '?', ';', ':').ToLower();
            if (importantWords.Contains(cleanWord) && keyWords.Count < 3)
            {
                keyWords.Add(CapitalizeFirstLetter(cleanWord));
            }
        }
        
        if (keyWords.Count > 0)
        {
            return string.Join(" ", keyWords);
        }
        
        // Fallback to first few meaningful words
        return CapitalizeWords(string.Join(" ", words.Take(3)));
    }

    /// <summary>
    /// Check if a word is a common connecting word that should end subject extraction
    /// </summary>
    private bool IsConnectingWord(string word)
    {
        string[] connectingWords = { "with", "and", "in", "on", "at", "by", "from", "to", "engaged", "wielding", "wearing", "sporting" };
        return connectingWords.Contains(word);
    }

    /// <summary>
    /// Capitalize each word in a string
    /// </summary>
    private string CapitalizeWords(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        
        string[] words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (!string.IsNullOrEmpty(words[i]))
            {
                words[i] = CapitalizeFirstLetter(words[i]);
            }
        }
        return string.Join(" ", words);
    }

    /// <summary>
    /// Capitalize the first letter of a word
    /// </summary>
    private string CapitalizeFirstLetter(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return char.ToUpper(input[0]) + input.Substring(1).ToLower();
    }

    #endregion

    #region Debugging and Inspection

    /// <summary>
    /// Inspect what textures and materials the imported model actually contains
    /// </summary>
    private void InspectImportedModelTextures(GameObject importedModel)
    {
        Debug.Log($"[WorldManager] 🔍 INSPECTING IMPORTED MODEL: {importedModel.name}");
        
        Renderer[] renderers = importedModel.GetComponentsInChildren<Renderer>();
        Debug.Log($"[WorldManager] 🔍 Found {renderers.Length} renderers in imported model");
        
        for (int r = 0; r < renderers.Length; r++)
        {
            Renderer renderer = renderers[r];
            Debug.Log($"[WorldManager] 🔍 Renderer {r}: {renderer.gameObject.name} (Type: {renderer.GetType()})");
            
            if (renderer.sharedMaterials != null)
            {
                Debug.Log($"[WorldManager] 🔍   Has {renderer.sharedMaterials.Length} materials");
                
                for (int m = 0; m < renderer.sharedMaterials.Length; m++)
                {
                    Material mat = renderer.sharedMaterials[m];
                    if (mat != null)
                    {
                        Debug.Log($"[WorldManager] 🔍   Material {m}: {mat.name} (Shader: {mat.shader.name})");
                        
                        // Check for textures in this material
                        var shader = mat.shader;
                        int textureCount = 0;
                        
                        for (int p = 0; p < shader.GetPropertyCount(); p++)
                        {
                            if (shader.GetPropertyType(p) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                            {
                                string propName = shader.GetPropertyName(p);
                                Texture tex = mat.GetTexture(propName);
                                
                                if (tex != null)
                                {
                                    textureCount++;
                                    Debug.Log($"[WorldManager] 🔍     TEXTURE FOUND: {propName} = {tex.name} ({tex.width}x{tex.height}, Type: {tex.GetType()})");
                                    
                                    // Check if it's a runtime texture or asset
                                    string assetPath = "";
#if UNITY_EDITOR
                                    assetPath = UnityEditor.AssetDatabase.GetAssetPath(tex);
#endif
                                    Debug.Log($"[WorldManager] 🔍       Asset path: '{assetPath}' {(string.IsNullOrEmpty(assetPath) ? "(RUNTIME TEXTURE)" : "(ASSET)")}");
                                }
                                else
                                {
                                    Debug.Log($"[WorldManager] ❌ Texture property {propName} is NULL");
                                }
                            }
                        }
                        
                        if (textureCount == 0)
                        {
                            Debug.LogWarning($"[WorldManager] ⚠️ Material {mat.name} has NO TEXTURES!");
                        }
                        else
                        {
                            Debug.Log($"[WorldManager] ✅ Material {mat.name} has {textureCount} textures");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[WorldManager] ⚠️ Material {m} is NULL!");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[WorldManager] ⚠️ Renderer {renderer.gameObject.name} has NO MATERIALS!");
            }
        }
        
        Debug.Log($"[WorldManager] 🔍 INSPECTION COMPLETE for {importedModel.name}");
    }
    
    /// <summary>
    /// Runs a simple test to check if transform mode cycling works correctly
    /// </summary>
    [UnityEngine.ContextMenu("Test Transform Mode Cycling")]
    public void TestTransformModeCycling()
    {
        Debug.Log("[WorldManager] 🧪 TESTING TRANSFORM MODE CYCLING");
        
        if (!rteInitialized || runtimeTools == null)
        {
            Debug.LogError("[WorldManager] ❌ RTE not initialized - cannot test transform mode cycling");
            return;
        }
        
        // Get current tool
        RuntimeTool startingTool = runtimeTools.Current;
        Debug.Log($"[WorldManager] Starting with transform mode: {startingTool}");
        
        // Test cycling through all modes
        Debug.Log("[WorldManager] Cycling to next mode...");
        CycleTransformMode();
        Debug.Log($"[WorldManager] Mode after first cycle: {runtimeTools.Current}");
        
        Debug.Log("[WorldManager] Cycling to next mode...");
        CycleTransformMode();
        Debug.Log($"[WorldManager] Mode after second cycle: {runtimeTools.Current}");
        
        Debug.Log("[WorldManager] Cycling to next mode...");
        CycleTransformMode();
        Debug.Log($"[WorldManager] Mode after third cycle: {runtimeTools.Current}");
        
        // Verify we're back to the starting tool
        if (runtimeTools.Current == startingTool)
        {
            Debug.Log("[WorldManager] ✅ TEST PASSED: Successfully cycled through all transform modes");
        }
        else
        {
            Debug.LogError("[WorldManager] ❌ TEST FAILED: Did not return to starting mode after cycling");
        }
    }
    
    /// <summary>
    /// Provides keyboard shortcut information for the user
    /// </summary>
    [UnityEngine.ContextMenu("Show Keyboard Shortcuts")]
    public void ShowKeyboardShortcuts()
    {
        Debug.Log("[WorldManager] ⌨️ KEYBOARD SHORTCUTS:");
        Debug.Log($"  T - Cycle transform modes (Move → Rotate → Scale)");
        Debug.Log($"  Current transform mode: {currentTransformTool}");
        
        // Display the shortcut information in the UI if possible
        if (statusText != null)
        {
            string previousText = statusText.text;
            statusText.text = $"PRESS T KEY TO CYCLE TRANSFORM MODES\nCurrent: {currentTransformTool}";
            
            // Reset status text after a few seconds
            StartCoroutine(ResetStatusTextAfterDelay(previousText, 3f));
        }
    }
    
    private IEnumerator ResetStatusTextAfterDelay(string originalText, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (statusText != null)
        {
            statusText.text = originalText;
        }
    }
    #endregion
}

