using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Traversify.Core;

namespace Traversify.AI {
    public class OpenAIResponse : MonoBehaviour {
        #region Singleton
        private static OpenAIResponse _instance;
        public static OpenAIResponse Instance {
            get {
                if (_instance == null) {
                    _instance = FindObjectOfType<OpenAIResponse>();
                    if (_instance == null) {
                        var go = new GameObject("OpenAIResponse");
                        _instance = go.AddComponent<OpenAIResponse>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        #endregion

        #region Configuration
        [Header("OpenAI Settings")]
        [SerializeField] private string apiKey = "";
        [SerializeField] private string organizationId = "";
        [SerializeField] private string model = "gpt-4o";
        [SerializeField] private string endpoint = "https://api.openai.com/v1/chat/completions";
        
        [Header("Request Configuration")]
        [SerializeField] private int maxTokens = 500;
        [SerializeField] [Range(0f, 2f)] private float temperature = 0.7f;
        [SerializeField] [Range(0f, 1f)] private float topP = 0.9f;
        [SerializeField] [Range(0f, 2f)] private float frequencyPenalty = 0.5f;
        [SerializeField] [Range(0f, 2f)] private float presencePenalty = 0.5f;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private float retryDelay = 1f;
        
        [Header("Context Management")]
        [SerializeField] private bool enableContextMemory = true;
        [SerializeField] private int maxContextHistory = 10;
        [SerializeField] private bool useSemanticCaching = true;
        [SerializeField] private float cacheExpirationHours = 24f;
        
        [Header("Response Processing")]
        [SerializeField] private bool validateResponses = true;
        [SerializeField] private bool extractStructuredData = true;
        [SerializeField] private bool enhanceWithKnowledge = true;
        [SerializeField] private string[] knowledgeDomains = { "geography", "architecture", "nature", "urban" };
        
        [Header("Performance")]
        [SerializeField] private bool enableBatching = true;
        [SerializeField] private int batchSize = 5;
        [SerializeField] private float batchDelay = 0.5f;
        [SerializeField] private bool asyncProcessing = true;
        [SerializeField] private int maxConcurrentRequests = 3;
        
        [Header("Security")]
        [SerializeField] private bool encryptApiKey = true;
        [SerializeField] private bool sanitizeInputs = true;
        [SerializeField] private bool logRequests = false;
        [SerializeField] private string[] blockedTerms = new string[0];
        #endregion

        #region Data Structures
        [Serializable]
        public class ChatMessage {
            public string role;
            public string content;
            public Dictionary<string, object> metadata;
            public DateTime timestamp;
        }
        
        [Serializable]
        public class ChatRequest {
            public string model;
            public List<ChatMessage> messages;
            public float temperature;
            public int max_tokens;
            public float top_p;
            public float frequency_penalty;
            public float presence_penalty;
            public bool stream;
            public Dictionary<string, object> functions;
            public string function_call;
        }
        
        [Serializable]
        public class ChatResponse {
            public string id;
            public string @object;
            public long created;
            public string model;
            public List<Choice> choices;
            public Usage usage;
            public Dictionary<string, object> metadata;
        }
        
        [Serializable]
        public class Choice {
            public int index;
            public Message message;
            public string finish_reason;
            public Dictionary<string, object> logprobs;
        }
        
        [Serializable]
        public class Message {
            public string role;
            public string content;
            public Dictionary<string, object> function_call;
        }
        
        [Serializable]
        public class Usage {
            public int prompt_tokens;
            public int completion_tokens;
            public int total_tokens;
        }
        
        [Serializable]
        public class EnhancementRequest {
            public string prompt;
            public EnhancementType type;
            public Dictionary<string, object> context;
            public Action<string> onSuccess;
            public Action<string> onError;
            public int priority;
        }
        
        public enum EnhancementType {
            TerrainDescription,
            ObjectDescription,
            SceneNarrative,
            TechnicalSpecification,
            CreativeExpansion
        }
        
        [Serializable]
        public class SemanticCache {
            public Dictionary<string, CachedResponse> responses;
            public DateTime lastCleanup;
            public int hitCount;
            public int missCount;
        }
        
        [Serializable]
        public class CachedResponse {
            public string prompt;
            public string response;
            public DateTime timestamp;
            public float confidence;
            public List<string> tags;
            public int accessCount;
        }
        
        [Serializable]
        public class ContextMemory {
            public List<ChatMessage> history;
            public Dictionary<string, object> worldState;
            public Dictionary<string, float> entityRelevance;
            public string currentFocus;
        }
        
        [Serializable]
        public class ApiMetrics {
            public int totalRequests;
            public float totalResponseTime;
            public float averageResponseTime;
            public float maxResponseTime;
            public float minResponseTime = float.MaxValue;
            public int successfulRequests;
            public int failedRequests;
            public DateTime lastRequestTime;
        }
        #endregion

        #region Private Fields
        private TraversifyDebugger debugger;
        private Queue<EnhancementRequest> requestQueue;
        private Dictionary<string, Task<string>> activeTasks;
        private SemanticCache cache;
        private ContextMemory contextMemory;
        private ApiMetrics metrics;
        private int activeRequests = 0;
        private readonly object requestLock = new object();
        private Coroutine batchProcessor;
        private List<EnhancementRequest> batchBuffer;
        
        // Performance tracking
        private float totalResponseTime = 0f;
        private int totalRequests = 0;
        private Dictionary<EnhancementType, float> averageResponseTimes;
        
        // Security
        private string encryptedApiKey;
        private readonly byte[] encryptionKey = Encoding.UTF8.GetBytes("TraversifySecure2025");
        #endregion

        #region Initialization
        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            Initialize();
        }
        
        private void Initialize() {
            debugger = GetComponent<TraversifyDebugger>();
            if (debugger == null) {
                debugger = gameObject.AddComponent<TraversifyDebugger>();
            }
            
            requestQueue = new Queue<EnhancementRequest>();
            activeTasks = new Dictionary<string, Task<string>>();
            batchBuffer = new List<EnhancementRequest>();
            averageResponseTimes = new Dictionary<EnhancementType, float>();
            metrics = new ApiMetrics();
            
            if (enableContextMemory) {
                contextMemory = new ContextMemory {
                    history = new List<ChatMessage>(),
                    worldState = new Dictionary<string, object>(),
                    entityRelevance = new Dictionary<string, float>()
                };
            }
            
            if (useSemanticCaching) {
                LoadCache();
                StartCoroutine(CacheMaintenanceRoutine());
            }
            
            if (enableBatching) {
                batchProcessor = StartCoroutine(BatchProcessingRoutine());
            }
            
            // Encrypt API key if needed
            if (encryptApiKey && !string.IsNullOrEmpty(apiKey)) {
                encryptedApiKey = EncryptString(apiKey);
                apiKey = ""; // Clear plain text
            }
            
            debugger.Log("OpenAIResponse initialized", LogCategory.System);
        }
        
        private void LoadCache() {
            string cachePath = System.IO.Path.Combine(Application.persistentDataPath, "openai_cache.json");
            
            if (System.IO.File.Exists(cachePath)) {
                try {
                    string json = System.IO.File.ReadAllText(cachePath);
                    cache = JsonUtility.FromJson<SemanticCache>(json);
                    
                    // Ensure cache.responses is not null
                    if (cache == null) {
                        cache = new SemanticCache { responses = new Dictionary<string, CachedResponse>() };
                    }
                    else if (cache.responses == null) {
                        cache.responses = new Dictionary<string, CachedResponse>();
                    }
                    
                    debugger?.Log($"Loaded semantic cache: {cache.responses.Count} entries", LogCategory.AI);
                } catch (Exception ex) {
                    debugger?.LogError($"Failed to load cache: {ex.Message}", LogCategory.AI);
                    cache = new SemanticCache { responses = new Dictionary<string, CachedResponse>() };
                }
            } else {
                cache = new SemanticCache { responses = new Dictionary<string, CachedResponse>() };
            }
        }
        #endregion

        #region Public API
        public void SetApiKey(string key) {
            if (encryptApiKey) {
                encryptedApiKey = EncryptString(key);
            } else {
                apiKey = key;
            }
        }
        
        public void RequestCompletion(
            string prompt,
            Action<string> onSuccess,
            Action<string> onError,
            EnhancementType type = EnhancementType.ObjectDescription,
            Dictionary<string, object> context = null,
            int priority = 0
        ) {
            var request = new EnhancementRequest {
                prompt = prompt,
                type = type,
                context = context ?? new Dictionary<string, object>(),
                onSuccess = onSuccess,
                onError = onError,
                priority = priority
            };
            
            if (enableBatching) {
                lock (requestLock) {
                    batchBuffer.Add(request);
                }
            } else {
                requestQueue.Enqueue(request);
                ProcessNextRequest();
            }
        }
        
        public void SendPrompt(string prompt, Action<string> onSuccess, Action<string> onError) {
            RequestCompletion(prompt, onSuccess, onError);
        }
        
        public void UpdateContext(string key, object value) {
            if (contextMemory != null) {
                contextMemory.worldState[key] = value;
            }
        }
        
        public void SetFocus(string entityType) {
            if (contextMemory != null) {
                contextMemory.currentFocus = entityType;
            }
        }
        
        public float GetAverageResponseTime(EnhancementType? type = null) {
            if (type.HasValue && averageResponseTimes.ContainsKey(type.Value)) {
                return averageResponseTimes[type.Value];
            }
            return totalRequests > 0 ? totalResponseTime / totalRequests : 0f;
        }
        
        public (int hits, int misses, float ratio) GetCacheStatistics() {
            if (cache == null) return (0, 0, 0f);
            
            int total = cache.hitCount + cache.missCount;
            float ratio = total > 0 ? cache.hitCount / (float)total : 0f;
            return (cache.hitCount, cache.missCount, ratio);
        }
        #endregion

        #region Request Processing
        private void ProcessNextRequest() {
            if (requestQueue.Count == 0 || activeRequests >= maxConcurrentRequests) {
                return;
            }
            
            var request = requestQueue.Dequeue();
            
            if (asyncProcessing) {
                Task.Run(() => ProcessRequestAsync(request));
            } else {
                StartCoroutine(ProcessRequest(request));
            }
        }
        
        private IEnumerator ProcessRequest(EnhancementRequest request) {
            activeRequests++;
            float startTime = Time.realtimeSinceStartup;
            
            // Store error state instead of using try-catch around yield
            string errorMessage = null;
            string response = null;
            
            // Check cache first
            if (useSemanticCaching) {
                string cachedResponse = CheckCache(request.prompt, request.type);
                if (!string.IsNullOrEmpty(cachedResponse)) {
                    cache.hitCount++;
                    request.onSuccess?.Invoke(cachedResponse);
                    activeRequests--;
                    yield break;
                }
                cache.missCount++;
            }
            
            // Validate and sanitize input
            if (sanitizeInputs) {
                request.prompt = SanitizeInput(request.prompt);
            }
            
            // Build enhanced prompt
            string enhancedPrompt = BuildEnhancedPrompt(request);
            
            // Create messages with context
            var messages = BuildMessages(enhancedPrompt, request);
            
            // Send request
            yield return SendChatRequest(messages, 
                res => response = res,
                err => errorMessage = err
            );
            
            if (!string.IsNullOrEmpty(errorMessage)) {
                request.onError?.Invoke(errorMessage);
                activeRequests--;
                yield break;
            }
            
            if (!string.IsNullOrEmpty(response)) {
                try {
                    // Process and validate response
                    if (validateResponses) {
                        response = ValidateAndCleanResponse(response, request.type);
                    }
                    
                    // Extract structured data if needed
                    if (extractStructuredData) {
                        response = ExtractStructuredData(response, request.type);
                    }
                    
                    // Cache successful response
                    if (useSemanticCaching) {
                        CacheResponse(request.prompt, response, request.type);
                    }
                    
                    // Update context memory
                    if (enableContextMemory) {
                        UpdateContextMemory(request.prompt, response);
                    }
                    
                    request.onSuccess?.Invoke(response);
                }
                catch (Exception ex) {
                    request.onError?.Invoke($"Response processing failed: {ex.Message}");
                }
            }
            else {
                request.onError?.Invoke("Empty response received from OpenAI");
            }
            
            // Update metrics
            float responseTime = Time.realtimeSinceStartup - startTime;
            metrics.totalRequests++;
            metrics.totalResponseTime += responseTime;
            metrics.averageResponseTime = metrics.totalResponseTime / metrics.totalRequests;
            
            if (responseTime > metrics.maxResponseTime) {
                metrics.maxResponseTime = responseTime;
            }
            
            activeRequests--;
        }
        
        private async Task ProcessRequestAsync(EnhancementRequest request) {
            activeRequests++;
            
            try {
                // Similar to ProcessRequest but async
                string response = await SendChatRequestAsync(
                    BuildMessages(BuildEnhancedPrompt(request), request)
                );
                
                if (!string.IsNullOrEmpty(response)) {
                    UnityMainThreadDispatcher.Instance.Enqueue(() => {
                        request.onSuccess?.Invoke(response);
                    });
                }
            } catch (Exception ex) {
                UnityMainThreadDispatcher.Instance.Enqueue(() => {
                    request.onError?.Invoke(ex.Message);
                });
            } finally {
                activeRequests--;
                UnityMainThreadDispatcher.Instance.Enqueue(ProcessNextRequest);
            }
        }
        #endregion

        #region Prompt Building
        private string BuildEnhancedPrompt(EnhancementRequest request) {
            var sb = new StringBuilder();
            
            // Add role and context based on type
            switch (request.type) {
                case EnhancementType.TerrainDescription:
                    sb.AppendLine("You are a geological and environmental expert analyzing terrain features for 3D world generation.");
                    sb.AppendLine("Provide detailed, technical descriptions focusing on:");
                    sb.AppendLine("- Geological composition and formation");
                    sb.AppendLine("- Surface characteristics and textures");
                    sb.AppendLine("- Vegetation and ecosystem elements");
                    sb.AppendLine("- Elevation patterns and topography");
                    sb.AppendLine("- Natural landmarks and features");
                    break;
                    
                case EnhancementType.ObjectDescription:
                    sb.AppendLine("You are a 3D modeling specialist providing descriptions for procedural model generation.");
                    sb.AppendLine("Focus on:");
                    sb.AppendLine("- Precise geometric characteristics");
                    sb.AppendLine("- Material properties and textures");
                    sb.AppendLine("- Architectural or structural details");
                    sb.AppendLine("- Scale and proportions");
                    sb.AppendLine("- Unique identifying features");
                    break;
                    
                case EnhancementType.SceneNarrative:
                    sb.AppendLine("You are a creative world-builder crafting immersive environment descriptions.");
                    sb.AppendLine("Include:");
                    sb.AppendLine("- Atmospheric and mood elements");
                    sb.AppendLine("- Historical or cultural context");
                    sb.AppendLine("- Interactive possibilities");
                    sb.AppendLine("- Narrative hooks and points of interest");
                    break;
                    
                case EnhancementType.TechnicalSpecification:
                    sb.AppendLine("You are a technical documentation specialist providing precise specifications.");
                    sb.AppendLine("Deliver:");
                    sb.AppendLine("- Exact measurements and dimensions");
                    sb.AppendLine("- Material specifications");
                    sb.AppendLine("- Construction or formation processes");
                    sb.AppendLine("- Physical properties and constraints");
                    break;
                    
                case EnhancementType.CreativeExpansion:
                    sb.AppendLine("You are a creative consultant expanding on initial concepts.");
                    sb.AppendLine("Provide:");
                    sb.AppendLine("- Alternative interpretations");
                    sb.AppendLine("- Related elements and variations");
                    sb.AppendLine("- Contextual possibilities");
                    sb.AppendLine("- Creative embellishments");
                    break;
            }
            
            // Add knowledge domain context
            if (enhanceWithKnowledge && knowledgeDomains.Length > 0) {
                sb.AppendLine($"\nApply knowledge from: {string.Join(", ", knowledgeDomains)}");
            }
            
            // Add specific context
            if (request.context != null && request.context.Count > 0) {
                sb.AppendLine("\nContext:");
                foreach (var kvp in request.context) {
                    sb.AppendLine($"- {kvp.Key}: {kvp.Value}");
                }
            }
            
            // Add current world state if available
            if (enableContextMemory && contextMemory.worldState.Count > 0) {
                sb.AppendLine("\nCurrent environment:");
                foreach (var kvp in contextMemory.worldState.Take(5)) {
                    sb.AppendLine($"- {kvp.Key}: {kvp.Value}");
                }
            }
            
            // Add the actual prompt
            sb.AppendLine($"\nRequest: {request.prompt}");
            
            // Add output format instructions
            sb.AppendLine("\nProvide a concise, actionable response optimized for 3D world generation.");
            sb.AppendLine($"Maximum length: {maxTokens / 4} words.");
            
            return sb.ToString();
        }
        
        private List<ChatMessage> BuildMessages(string prompt, EnhancementRequest request) {
            var messages = new List<ChatMessage>();
            
            // Add system message
            messages.Add(new ChatMessage {
                role = "system",
                content = "You are an AI assistant specialized in creating detailed descriptions for 3D world generation. " +
                         "Your responses should be precise, visual, and optimized for procedural generation.",
                timestamp = DateTime.UtcNow
            });
            
            // Add context history if enabled
            if (enableContextMemory && contextMemory.history.Count > 0) {
                int historyCount = Math.Min(maxContextHistory, contextMemory.history.Count);
                var relevantHistory = contextMemory.history
                    .OrderByDescending(m => CalculateRelevance(m, request))
                    .Take(historyCount);
                
                foreach (var msg in relevantHistory) {
                    messages.Add(msg);
                }
            }
            
            // Add current prompt
            messages.Add(new ChatMessage {
                role = "user",
                content = prompt,
                timestamp = DateTime.UtcNow,
                metadata = request.context
            });
            
            return messages;
        }
        
        private float CalculateRelevance(ChatMessage message, EnhancementRequest request) {
            float relevance = 0f;
            
            // Time decay
            float hoursSince = (float)(DateTime.UtcNow - message.timestamp).TotalHours;
            float timeDecay = Mathf.Exp(-hoursSince / 24f); // Half-life of 24 hours
            relevance += timeDecay * 0.3f;
            
            // Content similarity
            float similarity = CalculateTextSimilarity(message.content, request.prompt);
            relevance += similarity * 0.5f;
            
            // Context overlap
            if (message.metadata != null && request.context != null) {
                int overlap = message.metadata.Keys.Intersect(request.context.Keys).Count();
                relevance += (overlap / (float)Math.Max(message.metadata.Count, request.context.Count)) * 0.2f;
            }
            
            return Mathf.Clamp01(relevance);
        }
        
        private float CalculateTextSimilarity(string text1, string text2) {
            // Simple Jaccard similarity
            var words1 = new HashSet<string>(text1.ToLower().Split(' '));
            var words2 = new HashSet<string>(text2.ToLower().Split(' '));
            
            int intersection = words1.Intersect(words2).Count();
            int union = words1.Union(words2).Count();
            
            return union > 0 ? intersection / (float)union : 0f;
        }
        #endregion

        #region API Communication
        private IEnumerator SendChatRequest(
            List<ChatMessage> messages,
            Action<string> onSuccess,
            Action<string> onError
        ) {
            string apiKeyToUse = encryptApiKey ? DecryptString(encryptedApiKey) : apiKey;
            
            if (string.IsNullOrEmpty(apiKeyToUse)) {
                onError?.Invoke("OpenAI API key not set");
                yield break;
            }
            
            var request = new ChatRequest {
                model = model,
                messages = messages.Select(m => new ChatMessage { 
                    role = m.role, 
                    content = m.content 
                }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                top_p = topP,
                frequency_penalty = frequencyPenalty,
                presence_penalty = presencePenalty,
                stream = false
            };
            
            string jsonData = JsonUtility.ToJson(request);
            
            if (logRequests) {
                debugger.Log($"OpenAI Request: {jsonData}", LogCategory.API);
            }
            
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            
            int retryCount = 0;
            while (retryCount < maxRetries) {
                using (var webRequest = new UnityWebRequest(endpoint, "POST")) {
                    webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    webRequest.downloadHandler = new DownloadHandlerBuffer();
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                    webRequest.SetRequestHeader("Authorization", $"Bearer {apiKeyToUse}");
                    
                    if (!string.IsNullOrEmpty(organizationId)) {
                        webRequest.SetRequestHeader("OpenAI-Organization", organizationId);
                    }
                    
                    yield return webRequest.SendWebRequest();
                    
                    if (webRequest.result == UnityWebRequest.Result.Success) {
                        try {
                            var response = JsonUtility.FromJson<ChatResponse>(
                                webRequest.downloadHandler.text
                            );
                            
                            if (response.choices != null && response.choices.Count > 0) {
                                string content = response.choices[0].message.content.Trim();
                                
                                // Track token usage
                                if (response.usage != null) {
                                    debugger.Log($"Tokens used: {response.usage.total_tokens} " +
                                                $"(prompt: {response.usage.prompt_tokens}, " +
                                                $"completion: {response.usage.completion_tokens})", 
                                                LogCategory.AI);
                                }
                                
                                onSuccess?.Invoke(content);
                                yield break;
                            } else {
                                onError?.Invoke("OpenAI returned no choices");
                            }
                        } catch (Exception ex) {
                            onError?.Invoke($"Failed to parse response: {ex.Message}");
                        }
                        yield break;
                    } else {
                        string error = $"Request failed: {webRequest.error}";
                        
                        // Check if it's a rate limit error
                        if (webRequest.responseCode == 429) {
                            retryCount++;
                            if (retryCount < maxRetries) {
                                float delay = retryDelay * Mathf.Pow(2, retryCount - 1); // Exponential backoff
                                debugger.LogWarning($"Rate limited, retrying in {delay}s...", LogCategory.API);
                                yield return new WaitForSeconds(delay);
                                continue;
                            }
                        }
                        
                        debugger.LogError(error, LogCategory.API);
                        onError?.Invoke(error);
                        yield break;
                    }
                }
            }
            
            onError?.Invoke($"Max retries ({maxRetries}) exceeded");
        }
        
        private async Task<string> SendChatRequestAsync(List<ChatMessage> messages) {
            string apiKeyToUse = encryptApiKey ? DecryptString(encryptedApiKey) : apiKey;
            
            if (string.IsNullOrEmpty(apiKeyToUse)) {
                throw new Exception("OpenAI API key not set");
            }
            
            var request = new ChatRequest {
                model = model,
                messages = messages.Select(m => new ChatMessage { 
                    role = m.role, 
                    content = m.content 
                }).ToList(),
                temperature = temperature,
                max_tokens = maxTokens,
                top_p = topP,
                frequency_penalty = frequencyPenalty,
                presence_penalty = presencePenalty
            };
            
            string jsonData = JsonUtility.ToJson(request);
            
            using (var client = new System.Net.Http.HttpClient()) {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKeyToUse}");
                if (!string.IsNullOrEmpty(organizationId)) {
                    client.DefaultRequestHeaders.Add("OpenAI-Organization", organizationId);
                }
                
                var content = new System.Net.Http.StringContent(jsonData, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(endpoint, content);
                
                if (response.IsSuccessStatusCode) {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    var chatResponse = JsonUtility.FromJson<ChatResponse>(responseBody);
                    
                    if (chatResponse.choices != null && chatResponse.choices.Count > 0) {
                        return chatResponse.choices[0].message.content.Trim();
                    }
                }
                
                throw new Exception($"API request failed: {response.StatusCode}");
            }
        }
        #endregion

        #region Response Processing
        private string ValidateAndCleanResponse(string response, EnhancementType type) {
            // Remove any unwanted artifacts
            response = response.Trim();
            
            // Remove markdown code blocks if present
            if (response.StartsWith("```") && response.EndsWith("```")) {
                response = response.Substring(3, response.Length - 6).Trim();
            }
            
            // Type-specific validation
            switch (type) {
                case EnhancementType.TerrainDescription:
                    // Ensure geological terms are present
                    if (!ContainsTerrainKeywords(response)) {
                        response = EnhanceWithTerrainContext(response);
                    }
                    break;
                    
                case EnhancementType.ObjectDescription:
                    // Ensure dimensional information
                    if (!ContainsDimensionalInfo(response)) {
                        response = AddDefaultDimensions(response);
                    }
                    break;
                    
                case EnhancementType.TechnicalSpecification:
                    // Ensure precision
                    response = EnsureTechnicalPrecision(response);
                    break;
            }
            
            // Length validation
            int wordCount = response.Split(' ').Length;
            if (wordCount > maxTokens / 4) {
                response = TruncateResponse(response, maxTokens / 4);
            }
            
            return response;
        }
        
        private string ExtractStructuredData(string response, EnhancementType type) {
            var structuredData = new Dictionary<string, object>();
            
            // Extract key-value pairs
            var lines = response.Split('\n');
            foreach (var line in lines) {
                if (line.Contains(':')) {
                    var parts = line.Split(':');
                    if (parts.Length >= 2) {
                        string key = parts[0].Trim();
                        string value = string.Join(":", parts.Skip(1)).Trim();
                        
                        // Parse numeric values
                        if (float.TryParse(value, out float numValue)) {
                            structuredData[key] = numValue;
                        } else {
                            structuredData[key] = value;
                        }
                    }
                }
            }
            
            // Type-specific extraction
            switch (type) {
                case EnhancementType.TerrainDescription:
                    ExtractTerrainFeatures(response, structuredData);
                    break;
                    
                case EnhancementType.ObjectDescription:
                    ExtractObjectFeatures(response, structuredData);
                    break;
            }
            
            // If structured data was extracted, format it
            if (structuredData.Count > 0) {
                return FormatStructuredResponse(response, structuredData);
            }
            
            return response;
        }
        
        private void ExtractTerrainFeatures(string text, Dictionary<string, object> data) {
            // Extract elevation indicators
            var elevationMatch = System.Text.RegularExpressions.Regex.Match(
                text, @"(\d+)\s*(meters?|feet|m|ft)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            if (elevationMatch.Success) {
                data["elevation"] = float.Parse(elevationMatch.Groups[1].Value);
                data["elevation_unit"] = elevationMatch.Groups[2].Value;
            }
            
            // Extract terrain type
            string[] terrainTypes = { "mountain", "hill", "valley", "plain", "plateau", "canyon", "ridge" };
            foreach (var terrain in terrainTypes) {
                if (text.ToLower().Contains(terrain)) {
                    data["terrain_type"] = terrain;
                    break;
                }
            }
            
            // Extract vegetation
            string[] vegetationTypes = { "forest", "grassland", "desert", "tundra", "jungle", "savanna" };
            foreach (var veg in vegetationTypes) {
                if (text.ToLower().Contains(veg)) {
                    data["vegetation"] = veg;
                    break;
                }
            }
        }
        
        private void ExtractObjectFeatures(string text, Dictionary<string, object> data) {
            // Extract dimensions
            var dimensionMatches = System.Text.RegularExpressions.Regex.Matches(
                text, @"(\d+(?:\.\d+)?)\s*x\s*(\d+(?:\.\d+)?)\s*(?:x\s*(\d+(?:\.\d+)?))?\s*(meters?|feet|m|ft)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            
            if (dimensionMatches.Count > 0) {
                var match = dimensionMatches[0];
                data["width"] = float.Parse(match.Groups[1].Value);
                data["length"] = float.Parse(match.Groups[2].Value);
                if (match.Groups[3].Success) {
                    data["height"] = float.Parse(match.Groups[3].Value);
                }
                if (match.Groups[4].Success) {
                    data["dimension_unit"] = match.Groups[4].Value;
                }
            }
            
            // Extract materials
            string[] materials = { "wood", "stone", "metal", "glass", "concrete", "brick", "steel" };
            var detectedMaterials = new List<string>();
            foreach (var material in materials) {
                if (text.ToLower().Contains(material)) {
                    detectedMaterials.Add(material);
                }
            }
            if (detectedMaterials.Count > 0) {
                data["materials"] = detectedMaterials;
            }
            
            // Extract style
            string[] styles = { "modern", "traditional", "rustic", "industrial", "classical", "futuristic" };
            foreach (var style in styles) {
                if (text.ToLower().Contains(style)) {
                    data["style"] = style;
                    break;
                }
            }
        }
        
        private string FormatStructuredResponse(string originalResponse, Dictionary<string, object> structuredData) {
            var sb = new StringBuilder();
            
            // Add original response
            sb.AppendLine(originalResponse);
            sb.AppendLine();
            
            // Add structured data in a parseable format
            sb.AppendLine("=== STRUCTURED DATA ===");
            foreach (var kvp in structuredData) {
                if (kvp.Value is List<string> list) {
                    sb.AppendLine($"{kvp.Key}: {string.Join(", ", list)}");
                } else {
                    sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
            }
            
            return sb.ToString();
        }
        #endregion

        #region Caching
        private string CheckCache(string prompt, EnhancementType type) {
            if (cache == null || cache.responses == null) return null;
            
            // Direct cache hit
            string cacheKey = GenerateCacheKey(prompt, type);
            if (cache.responses.TryGetValue(cacheKey, out var cached)) {
                // Check expiration
                if ((DateTime.UtcNow - cached.timestamp).TotalHours < cacheExpirationHours) {
                    cached.accessCount++;
                    debugger.Log($"Cache hit for: {cacheKey}", LogCategory.AI);
                    return cached.response;
                } else {
                    // Expired entry
                    cache.responses.Remove(cacheKey);
                }
            }
            
            // Semantic similarity search
            if (useSemanticCaching) {
                var similarEntry = FindSimilarCachedResponse(prompt, type);
                if (similarEntry != null) {
                    debugger.Log($"Semantic cache hit (similarity: {similarEntry.confidence:P0})", LogCategory.AI);
                    return similarEntry.response;
                }
            }
            
            return null;
        }
        
        private void CacheResponse(string prompt, string response, EnhancementType type) {
            if (cache == null) {
                cache = new SemanticCache { responses = new Dictionary<string, CachedResponse>() };
            }
            
            string cacheKey = GenerateCacheKey(prompt, type);
            
            var cachedResponse = new CachedResponse {
                prompt = prompt,
                response = response,
                timestamp = DateTime.UtcNow,
                confidence = 1f,
                tags = ExtractTags(prompt, type),
                accessCount = 1
            };
            
            cache.responses[cacheKey] = cachedResponse;
            
            // Save cache periodically
            if (cache.responses.Count % 10 == 0) {
                SaveCache();
            }
        }
        
        private CachedResponse FindSimilarCachedResponse(string prompt, EnhancementType type) {
            float maxSimilarity = 0.8f; // Threshold for semantic match
            CachedResponse bestMatch = null;
            
            foreach (var kvp in cache.responses) {
                var cached = kvp.Value;
                
                // Check if same type
                if (!kvp.Key.StartsWith(type.ToString())) continue;
                
                // Check expiration
                if ((DateTime.UtcNow - cached.timestamp).TotalHours >= cacheExpirationHours) continue;
                
                // Calculate similarity
                float similarity = CalculateSemanticSimilarity(prompt, cached.prompt, cached.tags);
                
                if (similarity > maxSimilarity) {
                    maxSimilarity = similarity;
                    bestMatch = cached;
                }
            }
            
            if (bestMatch != null) {
                // Update confidence based on similarity
                bestMatch.confidence = maxSimilarity;
                bestMatch.accessCount++;
            }
            
            return bestMatch;
        }
        
        private float CalculateSemanticSimilarity(string text1, string text2, List<string> tags) {
            // Combine multiple similarity metrics
            float textSim = CalculateTextSimilarity(text1, text2);
            float lengthSim = 1f - Math.Abs(text1.Length - text2.Length) / (float)Math.Max(text1.Length, text2.Length);
            
            // Tag matching
            float tagSim = 0f;
            if (tags != null && tags.Count > 0) {
                var text1Words = new HashSet<string>(text1.ToLower().Split(' '));
                int tagMatches = tags.Count(tag => text1Words.Contains(tag.ToLower()));
                tagSim = tagMatches / (float)tags.Count;
            }
            
            // Weighted combination
            return textSim * 0.5f + lengthSim * 0.2f + tagSim * 0.3f;
        }
        
        private string GenerateCacheKey(string prompt, EnhancementType type) {
            // Create a unique but consistent key
            int hash = prompt.GetHashCode();
            return $"{type}_{hash:X8}";
        }
        
        private List<string> ExtractTags(string text, EnhancementType type) {
            var tags = new List<string>();
            
            // Extract nouns and important keywords
            string[] words = text.Split(' ');
            
            // Common important words by type
            Dictionary<EnhancementType, string[]> typeKeywords = new Dictionary<EnhancementType, string[]> {
                { EnhancementType.TerrainDescription, new[] { "mountain", "hill", "valley", "river", "forest", "plain" } },
                { EnhancementType.ObjectDescription, new[] { "building", "structure", "vehicle", "tree", "rock", "bridge" } }
            };
            
            if (typeKeywords.ContainsKey(type)) {
                foreach (var keyword in typeKeywords[type]) {
                    if (text.ToLower().Contains(keyword)) {
                        tags.Add(keyword);
                    }
                }
            }
            
            // Add first few significant words
            foreach (var word in words.Where(w => w.Length > 4).Take(5)) {
                tags.Add(word.ToLower());
            }
            
            return tags.Distinct().ToList();
        }
        
        private void SaveCache() {
            if (cache == null) return;
            
            try {
                string cachePath = System.IO.Path.Combine(Application.persistentDataPath, "openai_cache.json");
                string json = JsonUtility.ToJson(cache, true);
                System.IO.File.WriteAllText(cachePath, json);
                debugger.Log($"Saved cache: {cache.responses.Count} entries", LogCategory.AI);
            } catch (Exception ex) {
                debugger.LogError($"Failed to save cache: {ex.Message}", LogCategory.AI);
            }
        }
        
        private IEnumerator CacheMaintenanceRoutine() {
            while (useSemanticCaching) {
                yield return new WaitForSeconds(3600f); // Every hour
                
                if (cache != null && cache.responses != null) {
                    // Remove expired entries
                    var expiredKeys = cache.responses
                        .Where(kvp => (DateTime.UtcNow - kvp.Value.timestamp).TotalHours >= cacheExpirationHours)
                        .Select(kvp => kvp.Key)
                        .ToList();
                    
                    foreach (var key in expiredKeys) {
                        cache.responses.Remove(key);
                    }
                    
                    if (expiredKeys.Count > 0) {
                        debugger.Log($"Cleaned {expiredKeys.Count} expired cache entries", LogCategory.AI);
                        SaveCache();
                    }
                    
                    cache.lastCleanup = DateTime.UtcNow;
                }
            }
        }
        #endregion

        #region Batch Processing
        private IEnumerator BatchProcessingRoutine() {
            while (enableBatching) {
                yield return new WaitForSeconds(batchDelay);
                
                List<EnhancementRequest> currentBatch;
                lock (requestLock) {
                    if (batchBuffer.Count == 0) continue;
                    
                    // Sort by priority and take batch
                    currentBatch = batchBuffer
                        .OrderByDescending(r => r.priority)
                        .Take(batchSize)
                        .ToList();
                    
                    foreach (var req in currentBatch) {
                        batchBuffer.Remove(req);
                    }
                }
                
                // Process batch
                if (currentBatch.Count > 0) {
                    yield return ProcessBatch(currentBatch);
                }
            }
        }
        
        private IEnumerator ProcessBatch(List<EnhancementRequest> batch) {
            debugger.Log($"Processing batch of {batch.Count} requests", LogCategory.AI);
            
            // Group by type for more efficient processing
            var typeGroups = batch.GroupBy(r => r.type);
            
            foreach (var group in typeGroups) {
                // Build combined prompt for the group
                var combinedPrompt = BuildBatchPrompt(group.ToList());
                
                // Send single request for the group
                string response = null;
                yield return SendChatRequest(
                    BuildMessages(combinedPrompt, group.First()),
                    res => response = res,
                    err => {
                        // On error, process individually
                        foreach (var req in group) {
                            req.onError?.Invoke(err);
                        }
                    }
                );
                
                if (!string.IsNullOrEmpty(response)) {
                    // Parse and distribute responses
                    var individualResponses = ParseBatchResponse(response, group.Count());
                    
                    int index = 0;
                    foreach (var req in group) {
                        if (index < individualResponses.Count) {
                            req.onSuccess?.Invoke(individualResponses[index]);
                        } else {
                            req.onError?.Invoke("Batch response parsing error");
                        }
                        index++;
                    }
                }
            }
        }
        
        private string BuildBatchPrompt(List<EnhancementRequest> requests) {
            var sb = new StringBuilder();
            
            sb.AppendLine("Please provide individual responses for the following requests:");
            sb.AppendLine("Format each response with '=== RESPONSE N ===' delimiter.");
            sb.AppendLine();
            
            for (int i = 0; i < requests.Count; i++) {
                sb.AppendLine($"=== REQUEST {i + 1} ===");
                sb.AppendLine(requests[i].prompt);
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        private List<string> ParseBatchResponse(string response, int expectedCount) {
            var responses = new List<string>();
            
            // Split by delimiter
            var parts = response.Split(new[] { "=== RESPONSE" }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var part in parts) {
                // Clean up the response
                var cleaned = part.Trim();
                if (cleaned.StartsWith("1 ===") || cleaned.StartsWith("2 ===") || 
                    cleaned.StartsWith("3 ===") || cleaned.StartsWith("4 ===") || 
                    cleaned.StartsWith("5 ===")) {
                    // Remove the number and === prefix
                    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^\d+\s*===\s*", "");
                }
                
                if (!string.IsNullOrWhiteSpace(cleaned)) {
                    responses.Add(cleaned);
                }
            }
            
            // If parsing failed, return the whole response for each request
            if (responses.Count != expectedCount) {
                responses.Clear();
                for (int i = 0; i < expectedCount; i++) {
                    responses.Add(response);
                }
            }
            
            return responses;
        }
        #endregion

        #region Context Memory
        private void UpdateContextMemory(string prompt, string response) {
            if (contextMemory == null) return;
            
            // Add to history
            contextMemory.history.Add(new ChatMessage {
                role = "user",
                content = prompt,
                timestamp = DateTime.UtcNow
            });
            
            contextMemory.history.Add(new ChatMessage {
                role = "assistant",
                content = response,
                timestamp = DateTime.UtcNow
            });
            
            // Maintain history size
            while (contextMemory.history.Count > maxContextHistory * 2) {
                contextMemory.history.RemoveAt(0);
            }
            
            // Update entity relevance
            UpdateEntityRelevance(prompt, response);
        }
        
        private void UpdateEntityRelevance(string prompt, string response) {
            // Extract entities from prompt and response
            var entities = ExtractEntities(prompt + " " + response);
            
            foreach (var entity in entities) {
                if (!contextMemory.entityRelevance.ContainsKey(entity)) {
                    contextMemory.entityRelevance[entity] = 0f;
                }
                
                // Increase relevance
                contextMemory.entityRelevance[entity] = Mathf.Min(
                    contextMemory.entityRelevance[entity] + 0.1f,
                    1f
                );
            }
            
            // Decay all relevances
            var keys = contextMemory.entityRelevance.Keys.ToList();
            foreach (var key in keys) {
                contextMemory.entityRelevance[key] *= 0.95f;
                
                // Remove if too low
                if (contextMemory.entityRelevance[key] < 0.01f) {
                    contextMemory.entityRelevance.Remove(key);
                }
            }
        }
        
        private List<string> ExtractEntities(string text) {
            var entities = new List<string>();
            
            // Simple entity extraction based on capitalization and known patterns
            var words = text.Split(' ');
            
            for (int i = 0; i < words.Length; i++) {
                var word = words[i].Trim('.', ',', '!', '?');
                
                // Capitalized words (potential proper nouns)
                if (word.Length > 2 && char.IsUpper(word[0])) {
                    entities.Add(word);
                }
                
                // Numbers with units
                if (i < words.Length - 1 && float.TryParse(word, out _)) {
                    entities.Add($"{word} {words[i + 1]}");
                }
            }
            
            return entities.Distinct().ToList();
        }
        #endregion

        #region Security and Validation
        private string SanitizeInput(string input) {
            if (string.IsNullOrEmpty(input)) return input;
            
            // Remove potential injection attempts
            input = System.Text.RegularExpressions.Regex.Replace(
                input,
                @"<script.*?>.*?</script>",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
            
            // Check blocked terms
            foreach (var term in blockedTerms) {
                if (input.ToLower().Contains(term.ToLower())) {
                    debugger.LogWarning($"Blocked term detected: {term}", LogCategory.Security);
                    input = input.Replace(term, "[BLOCKED]", StringComparison.OrdinalIgnoreCase);
                }
            }
            
            // Limit length
            if (input.Length > 1000) {
                input = input.Substring(0, 1000) + "...";
            }
            
            return input;
        }
        
        private string EncryptString(string plainText) {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            
            try {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = new byte[plainBytes.Length];
                
                // Simple XOR encryption (for demonstration - use proper encryption in production)
                for (int i = 0; i < plainBytes.Length; i++) {
                    encrypted[i] = (byte)(plainBytes[i] ^ encryptionKey[i % encryptionKey.Length]);
                }
                
                return Convert.ToBase64String(encrypted);
            } catch (Exception ex) {
                debugger.LogError($"Encryption error: {ex.Message}", LogCategory.Security);
                return plainText;
            }
        }
        
        private string DecryptString(string encryptedText) {
            if (string.IsNullOrEmpty(encryptedText)) return encryptedText;
            
            try {
                byte[] encrypted = Convert.FromBase64String(encryptedText);
                byte[] decrypted = new byte[encrypted.Length];
                
                // Simple XOR decryption
                for (int i = 0; i < encrypted.Length; i++) {
                    decrypted[i] = (byte)(encrypted[i] ^ encryptionKey[i % encryptionKey.Length]);
                }
                
                return Encoding.UTF8.GetString(decrypted);
            } catch (Exception ex) {
                debugger.LogError($"Decryption error: {ex.Message}", LogCategory.Security);
                return encryptedText;
            }
        }
        #endregion

        #region Helper Methods
        private bool ContainsTerrainKeywords(string text) {
            string[] keywords = { 
                "elevation", "slope", "terrain", "geological", "surface", 
                "topography", "formation", "landscape", "altitude", "gradient" 
            };
            return keywords.Any(k => text.ToLower().Contains(k));
        }
        
        private string EnhanceWithTerrainContext(string text) {
            return text + "\nThe terrain exhibits natural geological formations with varied topography.";
        }
        
        private bool ContainsDimensionalInfo(string text) {
            return System.Text.RegularExpressions.Regex.IsMatch(
                text, 
                @"\d+(?:\.\d+)?\s*(?:x|by|×)\s*\d+(?:\.\d+)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        }
        
        private string AddDefaultDimensions(string text) {
            return text + "\nApproximate dimensions: 2m x 2m x 2m (adjustable based on context).";
        }
        
        private string EnsureTechnicalPrecision(string text) {
            // Add precision indicators if missing
            if (!text.Contains("±") && !text.Contains("approximately")) {
                text = System.Text.RegularExpressions.Regex.Replace(
                    text,
                    @"(\d+(?:\.\d+)?)\s*(m|meters?|ft|feet)",
                    "$1 ± 0.1 $2"
                );
            }
            return text;
        }
        
        private string TruncateResponse(string text, int maxWords) {
            var words = text.Split(' ');
            if (words.Length <= maxWords) return text;
            
            return string.Join(" ", words.Take(maxWords)) + "...";
        }
        
        private void OnDestroy() {
            // Save cache
            if (useSemanticCaching) {
                SaveCache();
            }
            
            // Stop coroutines
            if (batchProcessor != null) {
                StopCoroutine(batchProcessor);
            }
            
            // Clear sensitive data
            if (!string.IsNullOrEmpty(apiKey)) {
                apiKey = "";
            }
            if (!string.IsNullOrEmpty(encryptedApiKey)) {
                encryptedApiKey = "";
            }
            
            debugger.Log("OpenAIResponse cleaned up", LogCategory.System);
        }
        #endregion

        #region Unity Main Thread Dispatcher
        private class UnityMainThreadDispatcher : MonoBehaviour {
            private static UnityMainThreadDispatcher _instance;
            private readonly Queue<Action> _executionQueue = new Queue<Action>();
            
            public static UnityMainThreadDispatcher Instance {
                get {
                    if (_instance == null) {
                        var go = new GameObject("UnityMainThreadDispatcher");
                        _instance = go.AddComponent<UnityMainThreadDispatcher>();
                        DontDestroyOnLoad(go);
                    }
                    return _instance;
                }
            }
            
            public void Enqueue(Action action) {
                lock (_executionQueue) {
                    _executionQueue.Enqueue(action);
                }
            }
            
            private void Update() {
                lock (_executionQueue) {
                    while (_executionQueue.Count > 0) {
                        _executionQueue.Dequeue().Invoke();
                    }
                }
            }
        }
        #endregion
    }
}

