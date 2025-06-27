/*************************************************************************
 *  Traversify – LogCategory.cs                                          *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 02:58:24 UTC                                     *
 *  Desc   : Defines standardized logging categories for the Traversify  *
 *           system to ensure consistent and filterable logging across   *
 *           all components. Each category represents a specific system  *
 *           area for targeted debugging and analysis.                   *
 *************************************************************************/

using System;

namespace Traversify.Core
{
    /// <summary>
    /// Defines categories for logging in the Traversify system.
    /// Categories allow for filtered logging and targeted debugging
    /// of specific system components.
    /// </summary>
    public enum LogCategory
    {
        /// <summary>
        /// General-purpose logs not specific to any subsystem.
        /// </summary>
        General = 0,
        
        /// <summary>
        /// System initialization, configuration, and lifecycle events.
        /// </summary>
        System = 1,
        
        /// <summary>
        /// Artificial intelligence operations including model loading,
        /// inference, and AI processing results.
        /// </summary>
        AI = 2,
        
        /// <summary>
        /// Terrain generation, manipulation, and analysis operations.
        /// </summary>
        Terrain = 3,
        
        /// <summary>
        /// Water feature detection, generation, and simulation.
        /// </summary>
        Water = 4,
        
        /// <summary>
        /// Vegetation detection, placement, and simulation.
        /// </summary>
        Vegetation = 5,
        
        /// <summary>
        /// 3D model generation, loading, and placement operations.
        /// </summary>
        Models = 6,
        
        /// <summary>
        /// Visualization components, rendering operations, and visual effects.
        /// </summary>
        Visualization = 7,
        
        /// <summary>
        /// Performance metrics, optimization, and resource usage.
        /// </summary>
        Performance = 8,
        
        /// <summary>
        /// File operations, asset loading, and data serialization.
        /// </summary>
        IO = 9,
        
        /// <summary>
        /// Network operations, API calls, and data transfer.
        /// </summary>
        Network = 10,
        
        /// <summary>
        /// User interface interactions and state management.
        /// </summary>
        UI = 11,
        
        /// <summary>
        /// User input, actions, and interaction with the system.
        /// </summary>
        Input = 12,
        
        /// <summary>
        /// Audio system operations, sound loading, and playback.
        /// </summary>
        Audio = 13,
        
        /// <summary>
        /// External API integrations including OpenAI, Tripo3D, etc.
        /// </summary>
        API = 14,
        
        /// <summary>
        /// Active processes, processing pipelines, and workflows.
        /// </summary>
        Process = 15,
        
        /// <summary>
        /// Error conditions, exceptions, and error handling.
        /// </summary>
        Error = 16,
        
        /// <summary>
        /// Direct user-initiated actions and their results.
        /// </summary>
        User = 17,
        
        /// <summary>
        /// Background tasks, coroutines, and asynchronous operations.
        /// </summary>
        Background = 18,
        
        /// <summary>
        /// Multithreading, job system, and parallel processing.
        /// </summary>
        Threading = 19,
        
        /// <summary>
        /// Memory management, allocation, and garbage collection.
        /// </summary>
        Memory = 20,
        
        /// <summary>
        /// Path finding, navigation mesh, and route generation.
        /// </summary>
        Navigation = 21,
        
        /// <summary>
        /// Object detection, segmentation, and recognition.
        /// </summary>
        Detection = 22,
        
        /// <summary>
        /// Physics simulation, collisions, and rigidbody interactions.
        /// </summary>
        Physics = 23,
        
        /// <summary>
        /// Lighting, shadows, and atmospheric effects.
        /// </summary>
        Lighting = 24,
        
        /// <summary>
        /// Workflow automation and procedural generation pipelines.
        /// </summary>
        Automation = 25,
        
        /// <summary>
        /// Data validation, integrity checks, and verification.
        /// </summary>
        Validation = 26,
        
        /// <summary>
        /// Scene management, loading, and organization.
        /// </summary>
        Scene = 27,
        
        /// <summary>
        /// Weather systems, environmental effects, and atmosphere.
        /// </summary>
        Weather = 28,
        
        /// <summary>
        /// Time systems, day/night cycle, and temporal effects.
        /// </summary>
        Time = 29,
        
        /// <summary>
        /// Data streaming, incremental loading, and content delivery.
        /// </summary>
        Streaming = 30,
        
        /// <summary>
        /// Security operations, permissions, and access control.
        /// </summary>
        Security = 31,
        
        /// <summary>
        /// Configuration settings, preferences, and options.
        /// </summary>
        Configuration = 32,
        
        /// <summary>
        /// Analysis results, interpretations, and derived data.
        /// </summary>
        Analysis = 33,
        
        /// <summary>
        /// Heightmap generation, manipulation, and processing.
        /// </summary>
        Heightmap = 34,
        
        /// <summary>
        /// Road, path, and network generation and analysis.
        /// </summary>
        Roads = 35,
        
        /// <summary>
        /// Metadata, tags, and descriptive information.
        /// </summary>
        Metadata = 36,
        
        /// <summary>
        /// Cache operations, memory management, and resource pooling.
        /// </summary>
        Cache = 37,
        
        /// <summary>
        /// Editor-specific operations and tools.
        /// </summary>
        Editor = 38,
        
        /// <summary>
        /// Debug-only information that should not appear in release builds.
        /// </summary>
        Debug = 39
    }
    
    /// <summary>
    /// Extension methods for LogCategory enum.
    /// </summary>
    public static class LogCategoryExtensions
    {
        /// <summary>
        /// Gets a user-friendly display name for the log category.
        /// </summary>
        /// <param name="category">The log category</param>
        /// <returns>Display name for the category</returns>
        public static string GetDisplayName(this LogCategory category)
        {
            return category.ToString();
        }
        
        /// <summary>
        /// Gets the default color for the log category.
        /// </summary>
        /// <param name="category">The log category</param>
        /// <returns>RGBA color for the category</returns>
        public static UnityEngine.Color GetColor(this LogCategory category)
        {
            switch (category)
            {
                case LogCategory.System:
                    return new UnityEngine.Color(0.5f, 0.5f, 1.0f); // Light blue
                
                case LogCategory.AI:
                    return new UnityEngine.Color(0.8f, 0.4f, 0.8f); // Purple
                
                case LogCategory.Terrain:
                    return new UnityEngine.Color(0.4f, 0.8f, 0.4f); // Green
                
                case LogCategory.Water:
                    return new UnityEngine.Color(0.3f, 0.6f, 1.0f); // Blue
                
                case LogCategory.Models:
                    return new UnityEngine.Color(1.0f, 0.6f, 0.4f); // Orange
                
                case LogCategory.Performance:
                    return new UnityEngine.Color(1.0f, 1.0f, 0.4f); // Yellow
                
                case LogCategory.Error:
                    return new UnityEngine.Color(1.0f, 0.3f, 0.3f); // Red
                
                case LogCategory.User:
                    return new UnityEngine.Color(0.8f, 0.8f, 0.8f); // Light gray
                
                default:
                    return UnityEngine.Color.white;
            }
        }
        
        /// <summary>
        /// Checks if the category should be logged in the current build configuration.
        /// </summary>
        /// <param name="category">The log category</param>
        /// <returns>True if the category should be logged</returns>
        public static bool ShouldLog(this LogCategory category)
        {
            #if UNITY_EDITOR
            // Log everything in the editor
            return true;
            #else
            // In builds, we might filter out some categories
            if (category == LogCategory.Debug)
                return false;
                
            return true;
            #endif
        }
    }
}