/*************************************************************************
 *  Traversify – OpenAIResponse.cs                                       *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Updated: 2025-07-05                                                  *
 *  Desc   : Enhanced OpenAI integration for contextual descriptions,    *
 *           advanced analysis, and AI-powered content generation.       *
 *************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Traversify.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Traversify.AI {
    /// <summary>
    /// Enhanced OpenAI integration for generating contextual descriptions,
    /// performing analysis, and AI-powered content generation.
    /// </summary>
    [RequireComponent(typeof(TraversifyDebugger))]
    public class OpenAIResponse : TraversifyComponent {
        #region Inspector Fields
        
        [Header("API Settings")]
        [Tooltip("OpenAI API key")]
        [SerializeField] private string _apiKey = "";
        
        [Tooltip("OpenAI API base URL")]
        [SerializeField] private string _apiBaseUrl = "https://api.openai.com/v1/";
        
        [Tooltip("Model to use for completions")]
        [SerializeField] private string _defaultModel = "gpt-4o";
        
        [Tooltip("Temperature for completions (0-2)")]
        [Range(0f, 2f)]
        [SerializeField] private float _temperature = 0.7f;
        
        [Tooltip("Maximum tokens for completions")]
        [SerializeField] private int _maxTokens = 256;
        
        [Header("Response Settings")]
        [Tooltip("Enable structured responses in JSON format")]
        [SerializeField] private bool _useStructuredResponses = false;
        
        [Tooltip("Enable response caching")]
        [SerializeField] private bool _useResponseCache = true;
        
        [Tooltip("Maximum cache size")]
        [SerializeField] private int _maxCacheSize = 100;
        
        [Header("Security")]
        [Tooltip("Content filter level")]
        [SerializeField] private ContentFilterLevel _contentFilterLevel = ContentFilterLevel.Standard;
        
        [Tooltip("Enable sensitive data detection")]
        [SerializeField] private bool _detectSensitiveData = true;
        
        [Header("Rate Limiting")]
        [Tooltip("Enable rate limiting")]
        [SerializeField] private bool _enableRateLimiting = true;
        
        [Tooltip("Maximum requests per minute")]
        [SerializeField] private int _maxRequestsPerMinute = 30;
        
        [Tooltip("Request timeout in seconds")]
        [SerializeField] private float _requestTimeout = 10f;
        
        #endregion
        
        #region Public Properties
        
        /// <summary>
        /// OpenAI API key.
        /// </summary>
        public string apiKey {
            get => _apiKey;
            set => _apiKey = value;
        }
        
        /// <summary>
        /// Model to use for completions.
        /// </summary>
        public string defaultModel {
            get => _defaultModel;
            set => _defaultModel = value;
        }
        
        /// <summary>
        /// Temperature for completions.
        /// </summary>
        public float temperature {
            get => _temperature;
            set => _temperature = Mathf.Clamp(value, 0f, 2f);
        }
        
        /// <summary>
        /// Maximum tokens for completions.
        /// </summary>
        public int maxTokens {
            get => _maxTokens;
            set => _maxTokens = Mathf.Max(1, value);
        }
        
        /// <summary>
        /// Whether response caching is enabled.
        /// </summary>
        public bool useResponseCache {
            get => _useResponseCache;
            set => _useResponseCache = value;
        }
        
        /// <summary>
        /// Whether structured responses are enabled.
        /// </summary>
        public bool useStructuredResponses {
            get => _useStructuredResponses;
            set => _useStructuredResponses = value;
        }
        
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Content filter level.
        /// </summary>
        public enum ContentFilterLevel {
            None,
            Low,
            Standard,
            High
        }
        
        /// <summary>
        /// Message role type.
        /// </summary>
        public enum MessageRole {
            System,
            User,
            Assistant,
            Function
        }
        
        /// <summary>
        /// Chat message structure.
        /// </summary>
        [System.Serializable]
        public class ChatMessage {
            public string role;
            public string content;
            
            public ChatMessage(MessageRole role, string content) {
                this.role = role.ToString().ToLower();
                this.content = content;
            }
        }
        
        /// <summary>
        /// Response function definition.
        /// </summary>
        [System.Serializable]
        public class FunctionDefinition {
            public string name;
            public string description;
            public object parameters;
        }
        
        /// <summary>
        /// Cached response data.
        /// </summary>
        private class CachedResponse {
            public string prompt;
            public string response;
            public string model;
            public DateTime timestamp;
        }
        
        /// <summary>
        /// Request rate limiting data.
        /// </summary>
        private class RateLimitData {
            public Queue<DateTime> requestTimestamps = new Queue<DateTime>();
        }
        
        #endregion
        
        #region Private Fields
        
        private TraversifyDebugger _debugger;
        private Dictionary<string, CachedResponse> _responseCache = new Dictionary<string, CachedResponse>();
        private RateLimitData _rateLimitData = new RateLimitData();
        private int _requestCount = 0;
        private int _successCount = 0;
        private int _errorCount = 0;
        private int _cacheHitCount = 0;
        private int _cacheMissCount = 0;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isProcessing = false;
        private Queue<string> _sensitiveDataPatterns = new Queue<string>();
        
        // Events
        public event Action<string> OnResponseReceived;
        public event Action<string> OnRequestSent;
        public event Action<string> OnError;
        
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
                
                // Initialize sensitive data patterns
                InitializeSensitiveDataPatterns();
                
                // Create cancellation token
                _cancellationTokenSource = new CancellationTokenSource();
                
                Log("OpenAIResponse initialized successfully", LogCategory.AI);
                return true;
            }
            catch (Exception ex) {
                Debug.LogError($"Failed to initialize OpenAIResponse: {ex.Message}");
                return false;
            }
        }
        
        private void OnDestroy() {
            // Cancel any pending operations
            if (_cancellationTokenSource != null) {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
        }
        
        /// <summary>
        /// Apply configuration from object.
        /// </summary>
        private void ApplyConfiguration(object config) {
            // Handle dictionary config
            if (config is Dictionary<string, object> configDict) {
                // Extract API key
                if (configDict.TryGetValue("apiKey", out object apiKeyObj) && apiKeyObj is string apiKey) {
                    _apiKey = apiKey;
                }
                
                // Extract model
                if (configDict.TryGetValue("model", out object modelObj) && modelObj is string model) {
                    _defaultModel = model;
                }
                
                // Extract temperature
                if (configDict.TryGetValue("temperature", out object tempObj) && tempObj is float temp) {
                    _temperature = Mathf.Clamp(temp, 0f, 2f);
                }
                
                // Extract max tokens
                if (configDict.TryGetValue("maxTokens", out object tokensObj) && tokensObj is int tokens) {
                    _maxTokens = Mathf.Max(1, tokens);
                }
                
                // Extract cache settings
                if (configDict.TryGetValue("useCache", out object cacheObj) && cacheObj is bool useCache) {
                    _useResponseCache = useCache;
                }
            }
        }
        
        /// <summary>
        /// Initialize sensitive data patterns.
        /// </summary>
        private void InitializeSensitiveDataPatterns() {
            if (_detectSensitiveData) {
                _sensitiveDataPatterns = new Queue<string>();
                
                // Add common sensitive data patterns
                _sensitiveDataPatterns.Enqueue(@"\b\d{3}-\d{2}-\d{4}\b"); // SSN
                _sensitiveDataPatterns.Enqueue(@"\b\d{16}\b"); // Credit card number
                _sensitiveDataPatterns.Enqueue(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b"); // Email
                _sensitiveDataPatterns.Enqueue(@"\b\d{3}-\d{3}-\d{4}\b"); // Phone number
                _sensitiveDataPatterns.Enqueue(@"\b(password|passwd|pwd)[:=]\s*\S+\b"); // Password
                _sensitiveDataPatterns.Enqueue(@"\bapi[_-]?key[:=]\s*\S+\b"); // API key
            }
        }
        
        /// <summary>
        /// Set API key.
        /// </summary>
        public void SetApiKey(string apiKey) {
            _apiKey = apiKey;
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Get completion for a prompt.
        /// </summary>
        public IEnumerator GetCompletion(
            string prompt,
            System.Action<string> onComplete,
            System.Action<string> onError = null,
            string model = null,
            float? temperature = null,
            int? maxTokens = null)
        {
            if (string.IsNullOrEmpty(_apiKey)) {
                string errorMessage = "OpenAI API key not provided";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            if (string.IsNullOrEmpty(prompt)) {
                string errorMessage = "Prompt is empty";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            try {
                _isProcessing = true;
                
                // Check for sensitive data
                if (_detectSensitiveData && ContainsSensitiveData(prompt)) {
                    string errorMessage = "Prompt contains sensitive data";
                    LogWarning(errorMessage, LogCategory.AI);
                    onError?.Invoke(errorMessage);
                    OnError?.Invoke(errorMessage);
                    _isProcessing = false;
                    yield break;
                }
                
                // Check cache
                string cacheKey = GenerateCacheKey(prompt, model ?? _defaultModel);
                if (_useResponseCache && _responseCache.TryGetValue(cacheKey, out CachedResponse cachedResponse)) {
                    _cacheHitCount++;
                    Log($"Cache hit for prompt: {TruncateString(prompt, 50)}", LogCategory.AI);
                    onComplete?.Invoke(cachedResponse.response);
                    OnResponseReceived?.Invoke(cachedResponse.response);
                    _isProcessing = false;
                    yield break;
                }
                
                _cacheMissCount++;
                
                // Check rate limits
                if (_enableRateLimiting) {
                    yield return StartCoroutine(EnforceRateLimit());
                }
                
                // Prepare API request
                string apiUrl = $"{_apiBaseUrl}chat/completions";
                string requestJson = PrepareRequestJson(prompt, model, temperature, maxTokens);
                
                // Log request
                Log($"Sending request to OpenAI: {TruncateString(prompt, 100)}", LogCategory.AI);
                OnRequestSent?.Invoke(prompt);
                
                // Send request
                using (UnityWebRequest webRequest = new UnityWebRequest(apiUrl, "POST")) {
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
                    webRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    
                    // Set timeout
                    webRequest.timeout = Mathf.CeilToInt(_requestTimeout);
                    
                    // Send request
                    _requestCount++;
                    yield return webRequest.SendWebRequest();
                    
                    // Handle response
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        string errorMessage = $"API request failed: {webRequest.error}";
                        if (!string.IsNullOrEmpty(webRequest.downloadHandler.text)) {
                            errorMessage += $" - {webRequest.downloadHandler.text}";
                        }
                        
                        LogError(errorMessage, LogCategory.AI);
                        _errorCount++;
                        onError?.Invoke(errorMessage);
                        OnError?.Invoke(errorMessage);
                    }
                    else {
                        // Parse response
                        string responseJson = webRequest.downloadHandler.text;
                        string responseText = ExtractResponseText(responseJson);
                        
                        if (string.IsNullOrEmpty(responseText)) {
                            string errorMessage = "Failed to extract response text";
                            LogError(errorMessage, LogCategory.AI);
                            _errorCount++;
                            onError?.Invoke(errorMessage);
                            OnError?.Invoke(errorMessage);
                        }
                        else {
                            // Cache response
                            if (_useResponseCache) {
                                CacheResponse(cacheKey, prompt, responseText, model ?? _defaultModel);
                            }
                            
                            Log($"Received response from OpenAI: {TruncateString(responseText, 100)}", LogCategory.AI);
                            _successCount++;
                            onComplete?.Invoke(responseText);
                            OnResponseReceived?.Invoke(responseText);
                        }
                    }
                }
            }
            catch (Exception ex) {
                string errorMessage = $"Error getting completion: {ex.Message}";
                LogError(errorMessage, LogCategory.AI);
                _errorCount++;
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
            }
            finally {
                _isProcessing = false;
            }
        }
        
        /// <summary>
        /// Get structured completion as JSON.
        /// </summary>
        public IEnumerator GetStructuredCompletion<T>(
            string prompt,
            System.Action<T> onComplete,
            System.Action<string> onError = null,
            string model = null,
            float? temperature = null,
            int? maxTokens = null) where T : class
        {
            // Modify prompt to request JSON
            StringBuilder structuredPrompt = new StringBuilder(prompt);
            structuredPrompt.AppendLine("\nProvide your response as a valid JSON object with the following structure:");
            
            // Add structure based on type T
            structuredPrompt.AppendLine(GenerateJsonStructureFromType(typeof(T)));
            structuredPrompt.AppendLine("\nEnsure the response is properly formatted as JSON.");
            
            // Get completion
            bool completed = false;
            T result = null;
            string error = null;
            
            yield return StartCoroutine(GetCompletion(
                structuredPrompt.ToString(),
                (response) => {
                    try {
                        // Try to parse JSON directly
                        result = JsonConvert.DeserializeObject<T>(response);
                        
                        // If null, try to extract JSON from text
                        if (result == null) {
                            string jsonText = ExtractJsonFromText(response);
                            if (!string.IsNullOrEmpty(jsonText)) {
                                result = JsonConvert.DeserializeObject<T>(jsonText);
                            }
                        }
                        
                        if (result == null) {
                            error = "Failed to parse JSON response";
                        }
                    }
                    catch (Exception ex) {
                        error = $"Error parsing JSON: {ex.Message}";
                    }
                    completed = true;
                },
                (errorMessage) => {
                    error = errorMessage;
                    completed = true;
                },
                model,
                temperature,
                maxTokens
            ));
            
            // Wait for completion
            while (!completed) {
                yield return null;
            }
            
            // Return result or error
            if (result != null) {
                onComplete?.Invoke(result);
            }
            else {
                onError?.Invoke(error ?? "Unknown error parsing JSON");
            }
        }
        /// <summary>
        /// Get chat completion with conversation history.
        /// </summary>
        public IEnumerator GetChatCompletion(
            List<ChatMessage> messages,
            System.Action<string> onComplete,
            System.Action<string> onError = null,
            string model = null,
            float? temperature = null,
            int? maxTokens = null)
        {
            if (string.IsNullOrEmpty(_apiKey)) {
                string errorMessage = "OpenAI API key not provided";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            if (messages == null || messages.Count == 0) {
                string errorMessage = "No messages provided";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            try {
                _isProcessing = true;
                
                // Check for sensitive data in all messages
                if (_detectSensitiveData) {
                    foreach (var message in messages) {
                        if (ContainsSensitiveData(message.content)) {
                            string errorMessage = "Message contains sensitive data";
                            LogWarning(errorMessage, LogCategory.AI);
                            onError?.Invoke(errorMessage);
                            OnError?.Invoke(errorMessage);
                            _isProcessing = false;
                            yield break;
                        }
                    }
                }
                
                // Check rate limits
                if (_enableRateLimiting) {
                    yield return StartCoroutine(EnforceRateLimit());
                }
                
                // Prepare API request
                string apiUrl = $"{_apiBaseUrl}chat/completions";
                string requestJson = PrepareChatRequestJson(messages, model, temperature, maxTokens);
                
                // Log request
                Log($"Sending chat request to OpenAI with {messages.Count} messages", LogCategory.AI);
                OnRequestSent?.Invoke($"Chat request with {messages.Count} messages");
                
                // Send request
                using (UnityWebRequest webRequest = new UnityWebRequest(apiUrl, "POST")) {
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
                    webRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    
                    // Set timeout
                    webRequest.timeout = Mathf.CeilToInt(_requestTimeout);
                    
                    // Send request
                    _requestCount++;
                    yield return webRequest.SendWebRequest();
                    
                    // Handle response
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        string errorMessage = $"API request failed: {webRequest.error}";
                        if (!string.IsNullOrEmpty(webRequest.downloadHandler.text)) {
                            errorMessage += $" - {webRequest.downloadHandler.text}";
                        }
                        
                        LogError(errorMessage, LogCategory.AI);
                        _errorCount++;
                        onError?.Invoke(errorMessage);
                        OnError?.Invoke(errorMessage);
                    }
                    else {
                        // Parse response
                        string responseJson = webRequest.downloadHandler.text;
                        string responseText = ExtractResponseText(responseJson);
                        
                        if (string.IsNullOrEmpty(responseText)) {
                            string errorMessage = "Failed to extract response text";
                            LogError(errorMessage, LogCategory.AI);
                            _errorCount++;
                            onError?.Invoke(errorMessage);
                            OnError?.Invoke(errorMessage);
                        }
                        else {
                            Log($"Received chat response from OpenAI: {TruncateString(responseText, 100)}", LogCategory.AI);
                            _successCount++;
                            onComplete?.Invoke(responseText);
                            OnResponseReceived?.Invoke(responseText);
                        }
                    }
                }
            }
            catch (Exception ex) {
                string errorMessage = $"Error getting chat completion: {ex.Message}";
                LogError(errorMessage, LogCategory.AI);
                _errorCount++;
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
            }
            finally {
                _isProcessing = false;
            }
        }
        
        /// <summary>
        /// Get function calling completion.
        /// </summary>
        public IEnumerator GetFunctionCompletion(
            string prompt,
            List<FunctionDefinition> functions,
            System.Action<string, string> onComplete,
            System.Action<string> onError = null,
            string model = null,
            float? temperature = null,
            int? maxTokens = null)
        {
            if (string.IsNullOrEmpty(_apiKey)) {
                string errorMessage = "OpenAI API key not provided";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            if (string.IsNullOrEmpty(prompt)) {
                string errorMessage = "Prompt is empty";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            if (functions == null || functions.Count == 0) {
                string errorMessage = "No functions provided";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            try {
                _isProcessing = true;
                
                // Check for sensitive data
                if (_detectSensitiveData && ContainsSensitiveData(prompt)) {
                    string errorMessage = "Prompt contains sensitive data";
                    LogWarning(errorMessage, LogCategory.AI);
                    onError?.Invoke(errorMessage);
                    OnError?.Invoke(errorMessage);
                    _isProcessing = false;
                    yield break;
                }
                
                // Check rate limits
                if (_enableRateLimiting) {
                    yield return StartCoroutine(EnforceRateLimit());
                }
                
                // Prepare API request
                string apiUrl = $"{_apiBaseUrl}chat/completions";
                string requestJson = PrepareFunctionRequestJson(prompt, functions, model, temperature, maxTokens);
                
                // Log request
                Log($"Sending function request to OpenAI: {TruncateString(prompt, 100)}", LogCategory.AI);
                OnRequestSent?.Invoke(prompt);
                
                // Send request
                using (UnityWebRequest webRequest = new UnityWebRequest(apiUrl, "POST")) {
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
                    webRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    
                    // Set timeout
                    webRequest.timeout = Mathf.CeilToInt(_requestTimeout);
                    
                    // Send request
                    _requestCount++;
                    yield return webRequest.SendWebRequest();
                    
                    // Handle response
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        string errorMessage = $"API request failed: {webRequest.error}";
                        if (!string.IsNullOrEmpty(webRequest.downloadHandler.text)) {
                            errorMessage += $" - {webRequest.downloadHandler.text}";
                        }
                        
                        LogError(errorMessage, LogCategory.AI);
                        _errorCount++;
                        onError?.Invoke(errorMessage);
                        OnError?.Invoke(errorMessage);
                    }
                    else {
                        // Parse response
                        string responseJson = webRequest.downloadHandler.text;
                        
                        // Extract function call and arguments
                        try {
                            JObject jsonObject = JObject.Parse(responseJson);
                            JArray choices = (JArray)jsonObject["choices"];
                            
                            if (choices != null && choices.Count > 0) {
                                JObject message = (JObject)choices[0]["message"];
                                
                                if (message != null && message.ContainsKey("function_call")) {
                                    JObject functionCall = (JObject)message["function_call"];
                                    string functionName = (string)functionCall["name"];
                                    string functionArgs = (string)functionCall["arguments"];
                                    
                                    Log($"Received function call: {functionName}", LogCategory.AI);
                                    _successCount++;
                                    onComplete?.Invoke(functionName, functionArgs);
                                }
                                else {
                                    // Extract normal response text
                                    string responseText = ExtractResponseText(responseJson);
                                    
                                    if (string.IsNullOrEmpty(responseText)) {
                                        string errorMessage = "Failed to extract response text";
                                        LogError(errorMessage, LogCategory.AI);
                                        _errorCount++;
                                        onError?.Invoke(errorMessage);
                                        OnError?.Invoke(errorMessage);
                                    }
                                    else {
                                        Log($"Received text response from OpenAI: {TruncateString(responseText, 100)}", LogCategory.AI);
                                        _successCount++;
                                        onComplete?.Invoke("", responseText);
                                        OnResponseReceived?.Invoke(responseText);
                                    }
                                }
                            }
                            else {
                                string errorMessage = "No choices in response";
                                LogError(errorMessage, LogCategory.AI);
                                _errorCount++;
                                onError?.Invoke(errorMessage);
                                OnError?.Invoke(errorMessage);
                            }
                        }
                        catch (Exception ex) {
                            string errorMessage = $"Error parsing function response: {ex.Message}";
                            LogError(errorMessage, LogCategory.AI);
                            _errorCount++;
                            onError?.Invoke(errorMessage);
                            OnError?.Invoke(errorMessage);
                        }
                    }
                }
            }
            catch (Exception ex) {
                string errorMessage = $"Error getting function completion: {ex.Message}";
                LogError(errorMessage, LogCategory.AI);
                _errorCount++;
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
            }
            finally {
                _isProcessing = false;
            }
        }
        
        /// <summary>
        /// Clear response cache.
        /// </summary>
        public void ClearCache() {
            _responseCache.Clear();
            Log("Response cache cleared", LogCategory.AI);
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Prepare JSON for API request.
        /// </summary>
        private string PrepareRequestJson(string prompt, string model, float? temperature, int? maxTokens) {
            // Create message list
            List<object> messages = new List<object>();
            
            // Add user message
            messages.Add(new {
                role = "user",
                content = prompt
            });
            
            // Create request object
            var requestObj = new {
                model = model ?? _defaultModel,
                messages = messages,
                temperature = temperature ?? _temperature,
                max_tokens = maxTokens ?? _maxTokens,
                top_p = 1,
                frequency_penalty = 0,
                presence_penalty = 0
            };
            
            // Convert to JSON
            return JsonConvert.SerializeObject(requestObj);
        }
        
        /// <summary>
        /// Prepare JSON for chat API request.
        /// </summary>
        private string PrepareChatRequestJson(List<ChatMessage> messages, string model, float? temperature, int? maxTokens) {
            // Create request object
            var requestObj = new {
                model = model ?? _defaultModel,
                messages = messages,
                temperature = temperature ?? _temperature,
                max_tokens = maxTokens ?? _maxTokens,
                top_p = 1,
                frequency_penalty = 0,
                presence_penalty = 0
            };
            
            // Convert to JSON
            return JsonConvert.SerializeObject(requestObj);
        }
        
        /// <summary>
        /// Prepare JSON for function calling API request.
        /// </summary>
        private string PrepareFunctionRequestJson(string prompt, List<FunctionDefinition> functions, string model, float? temperature, int? maxTokens) {
            // Create message list
            List<object> messages = new List<object>();
            
            // Add user message
            messages.Add(new {
                role = "user",
                content = prompt
            });
            
            // Create request object
            var requestObj = new {
                model = model ?? _defaultModel,
                messages = messages,
                functions = functions,
                function_call = "auto",
                temperature = temperature ?? _temperature,
                max_tokens = maxTokens ?? _maxTokens,
                top_p = 1,
                frequency_penalty = 0,
                presence_penalty = 0
            };
            
            // Convert to JSON
            return JsonConvert.SerializeObject(requestObj);
        }
        /// <summary>
        /// Extract response text from JSON.
        /// </summary>
        private string ExtractResponseText(string json) {
            try {
                JObject jsonObject = JObject.Parse(json);
                JArray choices = (JArray)jsonObject["choices"];
                
                if (choices != null && choices.Count > 0) {
                    JObject message = (JObject)choices[0]["message"];
                    
                    if (message != null) {
                        return (string)message["content"];
                    }
                }
                
                return null;
            }
            catch (Exception ex) {
                LogError($"Error extracting response text: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Extract JSON from text.
        /// </summary>
        private string ExtractJsonFromText(string text) {
            try {
                // Try to find JSON between backticks or code blocks
                int startJson = text.IndexOf("```json");
                if (startJson >= 0) {
                    startJson += 7; // Length of "```json"
                    int endJson = text.IndexOf("```", startJson);
                    if (endJson >= 0) {
                        return text.Substring(startJson, endJson - startJson).Trim();
                    }
                }
                
                // Try to find JSON between curly braces
                startJson = text.IndexOf("{");
                if (startJson >= 0) {
                    // Find matching closing brace
                    int depth = 1;
                    int endJson = startJson + 1;
                    
                    while (depth > 0 && endJson < text.Length) {
                        if (text[endJson] == '{') depth++;
                        else if (text[endJson] == '}') depth--;
                        endJson++;
                    }
                    
                    if (depth == 0) {
                        return text.Substring(startJson, endJson - startJson).Trim();
                    }
                }
                
                return null;
            }
            catch (Exception ex) {
                LogError($"Error extracting JSON from text: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Generate JSON structure from type.
        /// </summary>
        private string GenerateJsonStructureFromType(Type type) {
            StringBuilder sb = new StringBuilder();
            
            sb.AppendLine("```json");
            sb.AppendLine("{");
            
            // Get properties
            var properties = type.GetProperties();
            
            for (int i = 0; i < properties.Length; i++) {
                var property = properties[i];
                string propertyName = property.Name;
                string propertyType = GetJsonTypeForProperty(property.PropertyType);
                
                sb.Append($"    \"{propertyName}\": {propertyType}");
                
                if (i < properties.Length - 1) {
                    sb.AppendLine(",");
                }
                else {
                    sb.AppendLine();
                }
            }
            
            sb.AppendLine("}");
            sb.AppendLine("```");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Get JSON type for property type.
        /// </summary>
        private string GetJsonTypeForProperty(Type type) {
            if (type == typeof(string)) {
                return "\"string value\"";
            }
            else if (type == typeof(int) || type == typeof(float) || type == typeof(double)) {
                return "0";
            }
            else if (type == typeof(bool)) {
                return "false";
            }
            else if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))) {
                return "[]";
            }
            else if (type.IsClass && type != typeof(string)) {
                return "{}";
            }
            else {
                return "null";
            }
        }
        
        /// <summary>
        /// Generate cache key from prompt and model.
        /// </summary>
        private string GenerateCacheKey(string prompt, string model) {
            // Simple hash for cache key
            string input = $"{prompt}:{model}";
            
            // Generate MD5 hash
            using (var md5 = System.Security.Cryptography.MD5.Create()) {
                byte[] inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                
                // Convert byte array to hex string
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++) {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                
                return sb.ToString();
            }
        }
        
        /// <summary>
        /// Cache response.
        /// </summary>
        private void CacheResponse(string cacheKey, string prompt, string response, string model) {
            // Manage cache size
            if (_responseCache.Count >= _maxCacheSize) {
                // Find oldest entry
                DateTime oldestTime = DateTime.MaxValue;
                string oldestKey = null;
                
                foreach (var entry in _responseCache) {
                    if (entry.Value.timestamp < oldestTime) {
                        oldestTime = entry.Value.timestamp;
                        oldestKey = entry.Key;
                    }
                }
                
                // Remove oldest entry
                if (oldestKey != null) {
                    _responseCache.Remove(oldestKey);
                }
            }
            
            // Add to cache
            _responseCache[cacheKey] = new CachedResponse {
                prompt = prompt,
                response = response,
                model = model,
                timestamp = DateTime.Now
            };
            
            Log($"Added response to cache with key: {cacheKey}", LogCategory.AI);
        }
        
        /// <summary>
        /// Check if text contains sensitive data.
        /// </summary>
        private bool ContainsSensitiveData(string text) {
            if (string.IsNullOrEmpty(text) || !_detectSensitiveData) {
                return false;
            }
            
            foreach (string pattern in _sensitiveDataPatterns) {
                if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern)) {
                    LogWarning($"Detected sensitive data matching pattern: {pattern}", LogCategory.AI);
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Enforce rate limit.
        /// </summary>
        private IEnumerator EnforceRateLimit() {
            if (!_enableRateLimiting) {
                yield break;
            }
            
            // Check if rate limit reached
            while (IsRateLimitReached()) {
                // Calculate wait time
                float waitTime = CalculateRateLimitWaitTime();
                
                LogWarning($"Rate limit reached, waiting for {waitTime:F1} seconds", LogCategory.AI);
                
                // Wait before trying again
                yield return new WaitForSeconds(waitTime);
            }
            
            // Add current request to rate limit data
            _rateLimitData.requestTimestamps.Enqueue(DateTime.Now);
            
            // Clean up old timestamps
            CleanupRateLimitData();
        }
        
        /// <summary>
        /// Check if rate limit is reached.
        /// </summary>
        private bool IsRateLimitReached() {
            // Clean up old timestamps
            CleanupRateLimitData();
            
            // Check if number of recent requests exceeds limit
            return _rateLimitData.requestTimestamps.Count >= _maxRequestsPerMinute;
        }
        
        /// <summary>
        /// Calculate wait time for rate limit.
        /// </summary>
        private float CalculateRateLimitWaitTime() {
            if (_rateLimitData.requestTimestamps.Count == 0) {
                return 0f;
            }
            
            // Get oldest timestamp
            DateTime oldest = _rateLimitData.requestTimestamps.Peek();
            
            // Calculate time until oldest timestamp is one minute ago
            TimeSpan waitTime = oldest.AddMinutes(1) - DateTime.Now;
            
            // Return wait time in seconds (or 0 if negative)
            return Mathf.Max(0f, (float)waitTime.TotalSeconds);
        }
        
        /// <summary>
        /// Clean up old rate limit data.
        /// </summary>
        private void CleanupRateLimitData() {
            // Remove timestamps older than one minute
            DateTime oneMinuteAgo = DateTime.Now.AddMinutes(-1);
            
            while (_rateLimitData.requestTimestamps.Count > 0 && 
                   _rateLimitData.requestTimestamps.Peek() < oneMinuteAgo) {
                _rateLimitData.requestTimestamps.Dequeue();
            }
        }
        
        /// <summary>
        /// Truncate string to specified length.
        /// </summary>
        private string TruncateString(string str, int maxLength) {
            if (string.IsNullOrEmpty(str)) {
                return str;
            }
            
            if (str.Length <= maxLength) {
                return str;
            }
            
            return str.Substring(0, maxLength) + "...";
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
        
        #region Advanced Features
        
        /// <summary>
        /// Get completion with streaming response.
        /// </summary>
        public IEnumerator GetStreamingCompletion(
            string prompt,
            System.Action<string, bool> onChunkReceived,
            System.Action<string> onError = null,
            string model = null,
            float? temperature = null,
            int? maxTokens = null)
        {
            if (string.IsNullOrEmpty(_apiKey)) {
                string errorMessage = "OpenAI API key not provided";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            if (string.IsNullOrEmpty(prompt)) {
                string errorMessage = "Prompt is empty";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            try {
                _isProcessing = true;
                
                // Check for sensitive data
                if (_detectSensitiveData && ContainsSensitiveData(prompt)) {
                    string errorMessage = "Prompt contains sensitive data";
                    LogWarning(errorMessage, LogCategory.AI);
                    onError?.Invoke(errorMessage);
                    OnError?.Invoke(errorMessage);
                    _isProcessing = false;
                    yield break;
                }
                
                // Check rate limits
                if (_enableRateLimiting) {
                    yield return StartCoroutine(EnforceRateLimit());
                }
                
                // Prepare API request
                string apiUrl = $"{_apiBaseUrl}chat/completions";
                
                // Create request object with stream enabled
                var requestObj = new {
                    model = model ?? _defaultModel,
                    messages = new[] {
                        new { role = "user", content = prompt }
                    },
                    temperature = temperature ?? _temperature,
                    max_tokens = maxTokens ?? _maxTokens,
                    top_p = 1,
                    frequency_penalty = 0,
                    presence_penalty = 0,
                    stream = true
                };
                
                string requestJson = JsonConvert.SerializeObject(requestObj);
                
                // Log request
                Log($"Sending streaming request to OpenAI: {TruncateString(prompt, 100)}", LogCategory.AI);
                OnRequestSent?.Invoke(prompt);
                
                // Create request
                UnityWebRequest webRequest = new UnityWebRequest(apiUrl, "POST");
                byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
                webRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                
                // Send request
                _requestCount++;
                webRequest.SendWebRequest();
                
                // Buffer for accumulating response
                StringBuilder responseBuffer = new StringBuilder();
                string completeResponse = "";
                string lastDownloadedChunk = "";
                
                // Process streaming response
                while (!webRequest.isDone) {
                    // Check for new data
                    string downloadedChunk = webRequest.downloadHandler.text;
                    if (downloadedChunk.Length > lastDownloadedChunk.Length) {
                        // Extract new chunk
                        string newData = downloadedChunk.Substring(lastDownloadedChunk.Length);
                        lastDownloadedChunk = downloadedChunk;
                        
                        // Process new data
                        responseBuffer.Append(newData);
                        string bufferText = responseBuffer.ToString();
                        
                        // Split by data: prefix (SSE format)
                        string[] chunks = bufferText.Split(new[] { "data: " }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string chunk in chunks) {
                            if (chunk.Trim() == "[DONE]") {
                                // End of stream
                                onChunkReceived?.Invoke("", true);
                            }
                            else if (!string.IsNullOrEmpty(chunk.Trim())) {
                                try {
                                    // Parse chunk as JSON
                                    JObject jsonChunk = JObject.Parse(chunk);
                                    JArray choices = (JArray)jsonChunk["choices"];
                                    
                                    if (choices != null && choices.Count > 0) {
                                        JObject delta = (JObject)choices[0]["delta"];
                                        
                                        if (delta != null && delta.ContainsKey("content")) {
                                            string content = (string)delta["content"];
                                            completeResponse += content;
                                            onChunkReceived?.Invoke(content, false);
                                        }
                                    }
                                }
                                catch (Exception ex) {
                                    // Likely an incomplete JSON, store in buffer
                                    continue;
                                }
                            }
                        }
                        
                        // Clear buffer
                        responseBuffer.Clear();
                    }
                    
                    yield return null;
                }
                
                // Handle response
                if (webRequest.result != UnityWebRequest.Result.Success) {
                    string errorMessage = $"API request failed: {webRequest.error}";
                    if (!string.IsNullOrEmpty(webRequest.downloadHandler.text)) {
                        errorMessage += $" - {webRequest.downloadHandler.text}";
                    }
                    
                    LogError(errorMessage, LogCategory.AI);
                    _errorCount++;
                    onError?.Invoke(errorMessage);
                    OnError?.Invoke(errorMessage);
                }
                else {
                    // Final notification
                    onChunkReceived?.Invoke("", true);
                    
                    // Cache complete response
                    if (_useResponseCache && !string.IsNullOrEmpty(completeResponse)) {
                        string cacheKey = GenerateCacheKey(prompt, model ?? _defaultModel);
                        CacheResponse(cacheKey, prompt, completeResponse, model ?? _defaultModel);
                    }
                    
                    Log($"Completed streaming response from OpenAI", LogCategory.AI);
                    _successCount++;
                    OnResponseReceived?.Invoke(completeResponse);
                }
                
                // Dispose request
                webRequest.Dispose();
            }
            catch (Exception ex) {
                string errorMessage = $"Error in streaming completion: {ex.Message}";
                LogError(errorMessage, LogCategory.AI);
                _errorCount++;
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
            }
            finally {
                _isProcessing = false;
            }
        }
        /// <summary>
        /// Generate image from text prompt using DALL-E.
        /// </summary>
        public IEnumerator GenerateImage(
            string prompt,
            System.Action<Texture2D> onComplete,
            System.Action<string> onError = null,
            int width = 1024,
            int height = 1024,
            string quality = "standard")
        {
            if (string.IsNullOrEmpty(_apiKey)) {
                string errorMessage = "OpenAI API key not provided";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            if (string.IsNullOrEmpty(prompt)) {
                string errorMessage = "Prompt is empty";
                LogError(errorMessage, LogCategory.AI);
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
                yield break;
            }
            
            try {
                _isProcessing = true;
                
                // Check for sensitive data
                if (_detectSensitiveData && ContainsSensitiveData(prompt)) {
                    string errorMessage = "Prompt contains sensitive data";
                    LogWarning(errorMessage, LogCategory.AI);
                    onError?.Invoke(errorMessage);
                    OnError?.Invoke(errorMessage);
                    _isProcessing = false;
                    yield break;
                }
                
                // Check rate limits
                if (_enableRateLimiting) {
                    yield return StartCoroutine(EnforceRateLimit());
                }
                
                // Prepare API request
                string apiUrl = $"{_apiBaseUrl}images/generations";
                
                // Create request object
                var requestObj = new {
                    model = "dall-e-3",
                    prompt = prompt,
                    n = 1,
                    size = $"{width}x{height}",
                    quality = quality,
                    response_format = "url"
                };
                
                string requestJson = JsonConvert.SerializeObject(requestObj);
                
                // Log request
                Log($"Sending image generation request to OpenAI: {TruncateString(prompt, 100)}", LogCategory.AI);
                OnRequestSent?.Invoke(prompt);
                
                // Send request
                using (UnityWebRequest webRequest = new UnityWebRequest(apiUrl, "POST")) {
                    byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(requestJson);
                    webRequest.uploadHandler = new UploadHandlerRaw(jsonBytes);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {_apiKey}");
                    
                    // Set timeout
                    webRequest.timeout = Mathf.CeilToInt(_requestTimeout * 3); // Longer timeout for images
                    
                    // Send request
                    _requestCount++;
                    yield return webRequest.SendWebRequest();
                    
                    // Handle response
                    if (webRequest.result != UnityWebRequest.Result.Success) {
                        string errorMessage = $"API request failed: {webRequest.error}";
                        if (!string.IsNullOrEmpty(webRequest.downloadHandler.text)) {
                            errorMessage += $" - {webRequest.downloadHandler.text}";
                        }
                        
                        LogError(errorMessage, LogCategory.AI);
                        _errorCount++;
                        onError?.Invoke(errorMessage);
                        OnError?.Invoke(errorMessage);
                    }
                    else {
                        // Parse response
                        string responseJson = webRequest.downloadHandler.text;
                        
                        try {
                            JObject jsonObject = JObject.Parse(responseJson);
                            JArray data = (JArray)jsonObject["data"];
                            
                            if (data != null && data.Count > 0) {
                                string imageUrl = (string)data[0]["url"];
                                
                                if (!string.IsNullOrEmpty(imageUrl)) {
                                    // Download image
                                    Log($"Downloading generated image from: {imageUrl}", LogCategory.AI);
                                    
                                    using (UnityWebRequest imageRequest = UnityWebRequestTexture.GetTexture(imageUrl)) {
                                        yield return imageRequest.SendWebRequest();
                                        
                                        if (imageRequest.result != UnityWebRequest.Result.Success) {
                                            string errorMessage = $"Failed to download image: {imageRequest.error}";
                                            LogError(errorMessage, LogCategory.AI);
                                            _errorCount++;
                                            onError?.Invoke(errorMessage);
                                            OnError?.Invoke(errorMessage);
                                        }
                                        else {
                                            // Get texture
                                            Texture2D texture = DownloadHandlerTexture.GetContent(imageRequest);
                                            
                                            Log($"Image downloaded successfully: {texture.width}x{texture.height}", LogCategory.AI);
                                            _successCount++;
                                            onComplete?.Invoke(texture);
                                        }
                                    }
                                }
                                else {
                                    string errorMessage = "No image URL in response";
                                    LogError(errorMessage, LogCategory.AI);
                                    _errorCount++;
                                    onError?.Invoke(errorMessage);
                                    OnError?.Invoke(errorMessage);
                                }
                            }
                            else {
                                string errorMessage = "No data in response";
                                LogError(errorMessage, LogCategory.AI);
                                _errorCount++;
                                onError?.Invoke(errorMessage);
                                OnError?.Invoke(errorMessage);
                            }
                        }
                        catch (Exception ex) {
                            string errorMessage = $"Error parsing image response: {ex.Message}";
                            LogError(errorMessage, LogCategory.AI);
                            _errorCount++;
                            onError?.Invoke(errorMessage);
                            OnError?.Invoke(errorMessage);
                        }
                    }
                }
            }
            catch (Exception ex) {
                string errorMessage = $"Error generating image: {ex.Message}";
                LogError(errorMessage, LogCategory.AI);
                _errorCount++;
                onError?.Invoke(errorMessage);
                OnError?.Invoke(errorMessage);
            }
            finally {
                _isProcessing = false;
            }
        }
        
        /// <summary>
        /// Get statistics about API usage.
        /// </summary>
        public Dictionary<string, int> GetStatistics() {
            return new Dictionary<string, int> {
                { "TotalRequests", _requestCount },
                { "SuccessfulRequests", _successCount },
                { "FailedRequests", _errorCount },
                { "CacheHits", _cacheHitCount },
                { "CacheMisses", _cacheMissCount },
                { "CacheSize", _responseCache.Count }
            };
        }
        
        /// <summary>
        /// Get memory usage details.
        /// </summary>
        public Dictionary<string, float> GetMemoryUsage() {
            Dictionary<string, float> memory = new Dictionary<string, float>();
            
            try {
                // Get total managed memory
                memory["ManagedMemoryMB"] = GC.GetTotalMemory(false) / (1024f * 1024f);
                
                // Get system memory
                memory["SystemMemoryMB"] = SystemInfo.systemMemorySize;
                
                // Calculate memory percentage
                memory["MemoryPercentage"] = memory["ManagedMemoryMB"] / memory["SystemMemoryMB"];
                
                // Get texture memory if possible
                #if UNITY_5_6_OR_NEWER
                memory["TextureMemoryMB"] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
                #endif
                
                // Get reserved memory if possible
                #if UNITY_2018_3_OR_NEWER
                memory["ReservedMemoryMB"] = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f);
                #endif
                
                // Get unused reserved memory if possible
                #if UNITY_2018_3_OR_NEWER
                memory["UnusedReservedMemoryMB"] = UnityEngine.Profiling.Profiler.GetTotalUnusedReservedMemoryLong() / (1024f * 1024f);
                #endif
            }
            catch (Exception ex) {
                LogError($"Error getting memory usage: {ex.Message}", LogCategory.System);
            }
            
            return memory;
        }
        
        #endregion
    }
} 