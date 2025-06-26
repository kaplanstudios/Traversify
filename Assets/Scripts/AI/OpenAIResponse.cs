using System.Collections.Generic;

namespace Traversify.AI
{
    /// <summary>
    /// Represents a response from OpenAI API
    /// </summary>
    public class OpenAIResponse
    {
        public string id;
        public string model;
        public List<OpenAIChoice> choices;
        public OpenAIUsage usage;
    }

    /// <summary>
    /// Represents a choice in OpenAI API response
    /// </summary>
    public class OpenAIChoice
    {
        public int index;
        public OpenAIMessage message;
        public string finish_reason;
    }

    /// <summary>
    /// Represents a message in OpenAI API response
    /// </summary>
    public class OpenAIMessage
    {
        public string role;
        public string content;
    }

    /// <summary>
    /// Represents usage metrics in OpenAI API response
    /// </summary>
    public class OpenAIUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }
}
