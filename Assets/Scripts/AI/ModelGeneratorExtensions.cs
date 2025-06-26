using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Traversify.AI;
using Traversify;  // For AnalysisResults and MapObject classes

namespace Traversify.AI
{
    // Extension class to add the missing method to ModelGenerator
    public static class ModelGeneratorExtensions
    {
        public static IEnumerator GenerateModelsForSegments(this ModelGenerator modelGenerator, List<ModelGenerationRequest> modelRequests)
        {
            Debug.Log($"[ModelGenerator] Generating models for {modelRequests.Count} segments");
            
            // Since we can't access the private methods of ModelGenerator,
            // we'll implement a simplified version that works with the public API
            
            // Get a reference to the terrain
            UnityEngine.Terrain terrain = Object.FindObjectOfType<UnityEngine.Terrain>();
            if (terrain == null)
            {
                Debug.LogError("[ModelGeneratorExtensions] No terrain found in the scene");
                yield break;
            }
            
            // Create an AnalysisResults object to work with the public GenerateAndPlaceModels method
            var results = new AnalysisResults();
            results.mapObjects = new List<MapObject>();
            
            // Convert ModelGenerationRequests to MapObjects
            foreach (var request in modelRequests)
            {
                // Create a MapObject from the ModelGenerationRequest
                var mapObj = new MapObject
                {
                    type = request.objectType,
                    label = request.objectType,
                    enhancedDescription = request.description,
                    position = request.position,
                    rotation = request.rotation,
                    scale = request.scale,
                    confidence = request.confidence,
                    isGrouped = request.isGrouped
                };
                
                results.mapObjects.Add(mapObj);
            }
            
            // Call the public method with the correct signature (2 parameters, not 5)
            yield return modelGenerator.GenerateAndPlaceModels(results, terrain);
            
            Debug.Log($"[ModelGenerator] Completed generating {modelRequests.Count} models");
        }
    }
}
