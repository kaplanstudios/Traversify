using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Traversify.AI {
    public class OpenAIResponse : MonoBehaviour {
        // Singleton
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

        [Header("OpenAI Settings")]
        [Tooltip("Set your OpenAI API key here or via TraversifyManager")]
        [SerializeField] private string apiKey = "";

        [Tooltip("Model to use for text completions")]
        [SerializeField] private string model = "gpt-4o";
        [Tooltip("Maximum tokens per response")]
        [SerializeField] private int maxTokens = 150;
        [Tooltip("Temperature for creativity (0–1)")]
        [Range(0f,1f)] [SerializeField] private float temperature = 0.7f;

        private const string endpoint = "https://api.openai.com/v1/chat/completions";

        private void Awake() {
            if (_instance != null && _instance != this) {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Set or update the API key at runtime.
        /// </summary>
        public void SetApiKey(string key) {
            apiKey = key;
        }

        /// <summary>
        /// Sends a chat-based completion request to OpenAI.
        /// </summary>
        public void RequestCompletion(
            string prompt,
            Action<string> onSuccess,
            Action<string> onError
        ) {
            if (string.IsNullOrEmpty(apiKey)) {
                onError?.Invoke("OpenAI API key not set.");
                return;
            }
            StartCoroutine(SendCompletionRequest(prompt, onSuccess, onError));
        }

        private IEnumerator SendCompletionRequest(
            string prompt,
            Action<string> onSuccess,
            Action<string> onError
        ) {
            var payload = new {
                model = model,
                temperature = temperature,
                max_tokens = maxTokens,
                messages = new[] {
                    new { role = "system", content = "You are a helpful assistant for 3D world generation." },
                    new { role = "user", content = prompt }
                }
            };
            string jsonData = JsonUtility.ToJson(payload);

            using var req = new UnityWebRequest(endpoint, "POST") {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) {
                onError?.Invoke($"OpenAI request failed: {req.error}");
                yield break;
            }

            try {
                var responseJson = req.downloadHandler.text;
                var response = JsonUtility.FromJson<ChatCompletionResponse>(responseJson);
                if (response.choices != null && response.choices.Length > 0) {
                    onSuccess?.Invoke(response.choices[0].message.content.Trim());
                } else {
                    onError?.Invoke("OpenAI returned no choices.");
                }
            } catch (Exception ex) {
                onError?.Invoke($"Failed to parse OpenAI response: {ex.Message}");
            }
        }

        [Serializable]
        private class ChatCompletionResponse {
            public Choice[] choices;
        }

        [Serializable]
        private class Choice {
            public Message message;
        }

        [Serializable]
        private class Message {
            public string role;
            public string content;
        }
    }
}
