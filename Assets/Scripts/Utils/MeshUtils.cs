/*************************************************************************
 *  Traversify – MeshUtils.cs                                            *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 04:10:46 UTC                                     *
 *  Desc   : Utility methods for mesh generation, manipulation, and      *
 *           optimization in the Traversify environment generation       *
 *           pipeline. Provides functionality for creating meshes from   *
 *           masks, combining meshes, and specialized terrain features.  *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Traversify.Core;

namespace Traversify.AI {
    /// <summary>
    /// Provides utility methods for mesh operations in the Traversify environment generation pipeline.
    /// </summary>
    public static class MeshUtils {
        #region Mesh Creation from Masks

        /// <summary>
        /// Creates a mesh from a mask texture.
        /// </summary>
        /// <param name="mask">The mask texture (alpha defines the shape)</param>
        /// <param name="boundingBox">The region in the source image covered by the mask</param>
        /// <param name="simplificationTolerance">Tolerance for simplifying the mesh outline (0 for no simplification)</param>
        /// <param name="heightOffset">Vertical offset of the mesh from the ground plane</param>
        /// <returns>A Unity Mesh representing the mask shape</returns>
        public static Mesh CreateMeshFromMask(Texture2D mask, Rect boundingBox, float simplificationTolerance = 0.5f, float heightOffset = 0.1f) {
            if (mask == null) {
                TraversifyDebugger.Instance?.LogError("Cannot create mesh from null mask", LogCategory.AI);
                return null;
            }

            try {
                // Extract contour points from the mask
                Vector2[] contourPoints = ExtractContourFromMask(mask, 0.5f);
                
                if (contourPoints == null || contourPoints.Length < 3) {
                    TraversifyDebugger.Instance?.LogWarning("Insufficient contour points extracted from mask", LogCategory.AI);
                    return CreateFallbackQuadMesh(boundingBox, heightOffset);
                }
                
                // Simplify the contour if needed
                if (simplificationTolerance > 0 && contourPoints.Length > 10) {
                    contourPoints = SimplifyContour(contourPoints, simplificationTolerance);
                }
                
                // Transform contour points to world space
                Vector3[] worldPoints = TransformContourToWorldSpace(contourPoints, mask.width, mask.height, boundingBox, heightOffset);
                
                // Triangulate the polygon
                Triangulator triangulator = new Triangulator(contourPoints);
                int[] triangles = triangulator.Triangulate();
                
                if (triangles == null || triangles.Length < 3) {
                    TraversifyDebugger.Instance?.LogWarning("Triangulation failed", LogCategory.AI);
                    return CreateFallbackQuadMesh(boundingBox, heightOffset);
                }
                
                // Create the mesh
                Mesh mesh = new Mesh();
                mesh.name = "MaskMesh";
                mesh.vertices = worldPoints;
                mesh.triangles = triangles;
                mesh.uv = GenerateUVs(worldPoints, boundingBox);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                
                return mesh;
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error creating mesh from mask: {ex.Message}", LogCategory.AI);
                return CreateFallbackQuadMesh(boundingBox, heightOffset);
            }
        }
        
        /// <summary>
        /// Creates a simple quad mesh as a fallback when mask-based mesh creation fails.
        /// </summary>
        private static Mesh CreateFallbackQuadMesh(Rect boundingBox, float heightOffset) {
            Vector3[] vertices = new Vector3[4];
            int[] triangles = new int[6];
            Vector2[] uvs = new Vector2[4];
            
            // Vertices (clockwise from bottom-left)
            vertices[0] = new Vector3(boundingBox.xMin, heightOffset, boundingBox.yMin);
            vertices[1] = new Vector3(boundingBox.xMax, heightOffset, boundingBox.yMin);
            vertices[2] = new Vector3(boundingBox.xMax, heightOffset, boundingBox.yMax);
            vertices[3] = new Vector3(boundingBox.xMin, heightOffset, boundingBox.yMax);
            
            // Triangles
            triangles[0] = 0;
            triangles[1] = 1;
            triangles[2] = 2;
            triangles[3] = 0;
            triangles[4] = 2;
            triangles[5] = 3;
            
            // UVs
            uvs[0] = new Vector2(0, 0);
            uvs[1] = new Vector2(1, 0);
            uvs[2] = new Vector2(1, 1);
            uvs[3] = new Vector2(0, 1);
            
            Mesh mesh = new Mesh();
            mesh.name = "FallbackQuad";
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        /// <summary>
        /// Extracts contour points from a mask texture.
        /// </summary>
        private static Vector2[] ExtractContourFromMask(Texture2D mask, float threshold = 0.5f) {
            int width = mask.width;
            int height = mask.height;
            
            // Use a marching squares algorithm to extract the contour
            List<Vector2> contourPoints = new List<Vector2>();
            bool[,] visited = new bool[width, height];
            Color[] pixels = mask.GetPixels();
            
            // Find a starting point on the contour
            int startX = -1, startY = -1;
            for (int y = 1; y < height - 1; y++) {
                for (int x = 1; x < width - 1; x++) {
                    int index = y * width + x;
                    if (pixels[index].a > threshold) {
                        // Check if this is a boundary pixel
                        if (pixels[(y - 1) * width + x].a <= threshold ||
                            pixels[(y + 1) * width + x].a <= threshold ||
                            pixels[y * width + (x - 1)].a <= threshold ||
                            pixels[y * width + (x + 1)].a <= threshold) {
                            startX = x;
                            startY = y;
                            break;
                        }
                    }
                }
                if (startX != -1) break;
            }
            
            if (startX == -1) {
                // No boundary found, return a box outline
                contourPoints.Add(new Vector2(0, 0));
                contourPoints.Add(new Vector2(width, 0));
                contourPoints.Add(new Vector2(width, height));
                contourPoints.Add(new Vector2(0, height));
                return contourPoints.ToArray();
            }
            
            // Trace the contour using Moore neighborhood tracing
            int currentX = startX;
            int currentY = startY;
            int nextX, nextY;
            
            // Direction vectors: 0=right, 1=right-down, 2=down, 3=left-down, 4=left, 5=left-up, 6=up, 7=right-up
            int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
            int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };
            
            int dir = 0;
            bool done = false;
            
            do {
                contourPoints.Add(new Vector2(currentX, currentY));
                visited[currentX, currentY] = true;
                
                // Look around in all 8 directions, starting from the right
                bool foundNext = false;
                for (int i = 0; i < 8; i++) {
                    nextX = currentX + dx[(dir + i) % 8];
                    nextY = currentY + dy[(dir + i) % 8];
                    
                    if (nextX >= 0 && nextX < width && nextY >= 0 && nextY < height &&
                        pixels[nextY * width + nextX].a > threshold && !visited[nextX, nextY]) {
                        currentX = nextX;
                        currentY = nextY;
                        dir = (dir + i) % 8;
                        foundNext = true;
                        break;
                    }
                }
                
                if (!foundNext || contourPoints.Count > width * height) {
                    done = true;
                }
                
                // Also stop if we're back at the starting point
                if (currentX == startX && currentY == startY) {
                    done = true;
                }
                
            } while (!done && contourPoints.Count < 2000); // Safety limit
            
            return contourPoints.ToArray();
        }
        
        /// <summary>
        /// Simplifies a contour using the Ramer-Douglas-Peucker algorithm.
        /// </summary>
        private static Vector2[] SimplifyContour(Vector2[] points, float tolerance) {
            if (points == null || points.Length < 3) {
                return points;
            }
            
            List<Vector2> result = new List<Vector2>();
            bool[] keepPoint = new bool[points.Length];
            
            // Always keep first and last points
            keepPoint[0] = true;
            keepPoint[points.Length - 1] = true;
            
            // Recursively simplify
            SimplifySection(points, 0, points.Length - 1, keepPoint, tolerance);
            
            // Build result array
            for (int i = 0; i < points.Length; i++) {
                if (keepPoint[i]) {
                    result.Add(points[i]);
                }
            }
            
            return result.ToArray();
        }
        
        /// <summary>
        /// Recursive helper for the Ramer-Douglas-Peucker algorithm.
        /// </summary>
        private static void SimplifySection(Vector2[] points, int start, int end, bool[] keepPoint, float tolerance) {
            if (end <= start + 1) {
                return;
            }
            
            float maxDist = 0;
            int maxIndex = start;
            
            // Find point with max distance from line
            for (int i = start + 1; i < end; i++) {
                float dist = PerpendicularDistance(points[i], points[start], points[end]);
                if (dist > maxDist) {
                    maxDist = dist;
                    maxIndex = i;
                }
            }
            
            // If the max distance is greater than tolerance, keep this point and recurse
            if (maxDist > tolerance) {
                keepPoint[maxIndex] = true;
                SimplifySection(points, start, maxIndex, keepPoint, tolerance);
                SimplifySection(points, maxIndex, end, keepPoint, tolerance);
            }
        }
        
        /// <summary>
        /// Calculates the perpendicular distance from a point to a line.
        /// </summary>
        private static float PerpendicularDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd) {
            float area = Mathf.Abs(0.5f * (lineStart.x * (lineEnd.y - point.y) + 
                                         lineEnd.x * (point.y - lineStart.y) + 
                                         point.x * (lineStart.y - lineEnd.y)));
            float bottom = Mathf.Sqrt(Mathf.Pow(lineEnd.x - lineStart.x, 2) + 
                                    Mathf.Pow(lineEnd.y - lineStart.y, 2));
            
            return area / bottom * 2.0f;
        }
        
        /// <summary>
        /// Transforms 2D contour points to 3D world space.
        /// </summary>
        private static Vector3[] TransformContourToWorldSpace(Vector2[] contourPoints, int maskWidth, int maskHeight, 
                                                             Rect boundingBox, float heightOffset) {
            Vector3[] worldPoints = new Vector3[contourPoints.Length];
            
            for (int i = 0; i < contourPoints.Length; i++) {
                // Normalize to 0-1 range
                float normalizedX = contourPoints[i].x / maskWidth;
                float normalizedY = contourPoints[i].y / maskHeight;
                
                // Map to bounding box
                float worldX = Mathf.Lerp(boundingBox.xMin, boundingBox.xMax, normalizedX);
                float worldZ = Mathf.Lerp(boundingBox.yMin, boundingBox.yMax, normalizedY);
                
                worldPoints[i] = new Vector3(worldX, heightOffset, worldZ);
            }
            
            return worldPoints;
        }
        
        /// <summary>
        /// Generates UV coordinates for a mesh.
        /// </summary>
        private static Vector2[] GenerateUVs(Vector3[] vertices, Rect boundingBox) {
            Vector2[] uvs = new Vector2[vertices.Length];
            
            // Calculate bounds
            float minX = boundingBox.xMin;
            float maxX = boundingBox.xMax;
            float minZ = boundingBox.yMin;
            float maxZ = boundingBox.yMax;
            float width = maxX - minX;
            float height = maxZ - minZ;
            
            for (int i = 0; i < vertices.Length; i++) {
                uvs[i] = new Vector2(
                    (vertices[i].x - minX) / width,
                    (vertices[i].z - minZ) / height
                );
            }
            
            return uvs;
        }
        
        #endregion
        
        #region Terrain Mesh Operations
        
        /// <summary>
        /// Creates a terrain mesh from a heightmap texture.
        /// </summary>
        /// <param name="heightmap">The heightmap texture (R channel contains height)</param>
        /// <param name="terrainSize">Size of the terrain in world units</param>
        /// <param name="heightScale">Scale factor for height values</param>
        /// <param name="resolution">Resolution of the generated mesh (vertices per side)</param>
        /// <returns>A Unity Mesh representing the terrain</returns>
        public static Mesh CreateTerrainMeshFromHeightmap(Texture2D heightmap, Vector3 terrainSize, float heightScale, int resolution) {
            if (heightmap == null) {
                TraversifyDebugger.Instance?.LogError("Cannot create terrain mesh from null heightmap", LogCategory.AI);
                return null;
            }

            try {
                // Ensure resolution is at least 2x2
                resolution = Mathf.Max(2, resolution);
                
                // Create vertex array
                Vector3[] vertices = new Vector3[resolution * resolution];
                Vector2[] uvs = new Vector2[resolution * resolution];
                int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
                
                // Sample heightmap and create vertices
                for (int z = 0; z < resolution; z++) {
                    for (int x = 0; x < resolution; x++) {
                        int index = z * resolution + x;
                        
                        // Calculate normalized position
                        float normalizedX = (float)x / (resolution - 1);
                        float normalizedZ = (float)z / (resolution - 1);
                        
                        // Sample heightmap
                        float height = SampleHeightmap(heightmap, normalizedX, normalizedZ) * heightScale;
                        
                        // Calculate vertex position
                        vertices[index] = new Vector3(
                            normalizedX * terrainSize.x,
                            height,
                            normalizedZ * terrainSize.z
                        );
                        
                        // Calculate UVs
                        uvs[index] = new Vector2(normalizedX, normalizedZ);
                    }
                }
                
                // Create triangles
                int triangleIndex = 0;
                for (int z = 0; z < resolution - 1; z++) {
                    for (int x = 0; x < resolution - 1; x++) {
                        int topLeft = z * resolution + x;
                        int topRight = topLeft + 1;
                        int bottomLeft = (z + 1) * resolution + x;
                        int bottomRight = bottomLeft + 1;
                        
                        // First triangle (top-left, bottom-left, bottom-right)
                        triangles[triangleIndex++] = topLeft;
                        triangles[triangleIndex++] = bottomLeft;
                        triangles[triangleIndex++] = bottomRight;
                        
                        // Second triangle (top-left, bottom-right, top-right)
                        triangles[triangleIndex++] = topLeft;
                        triangles[triangleIndex++] = bottomRight;
                        triangles[triangleIndex++] = topRight;
                    }
                }
                
                // Create the mesh
                Mesh mesh = new Mesh();
                mesh.name = "TerrainMesh";
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.uv = uvs;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                
                return mesh;
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error creating terrain mesh: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Samples a heightmap texture at the given normalized coordinates.
        /// </summary>
        private static float SampleHeightmap(Texture2D heightmap, float normalizedX, float normalizedY) {
            // Use bilinear sampling for smooth results
            Color color = heightmap.GetPixelBilinear(normalizedX, normalizedY);
            
            // Height value is in the red channel
            return color.r;
        }
        
        /// <summary>
        /// Creates a water plane mesh for a terrain.
        /// </summary>
        /// <param name="terrainSize">Size of the terrain in world units</param>
        /// <param name="waterHeight">Height of the water plane</param>
        /// <param name="resolution">Resolution of the water mesh (vertices per side)</param>
        /// <returns>A Unity Mesh representing the water plane</returns>
        public static Mesh CreateWaterPlaneMesh(Vector3 terrainSize, float waterHeight, int resolution = 10) {
            try {
                // Ensure resolution is at least 2x2
                resolution = Mathf.Max(2, resolution);
                
                // Create vertex array
                Vector3[] vertices = new Vector3[resolution * resolution];
                Vector2[] uvs = new Vector2[resolution * resolution];
                int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];
                
                // Create vertices
                for (int z = 0; z < resolution; z++) {
                    for (int x = 0; x < resolution; x++) {
                        int index = z * resolution + x;
                        
                        // Calculate normalized position
                        float normalizedX = (float)x / (resolution - 1);
                        float normalizedZ = (float)z / (resolution - 1);
                        
                        // Calculate vertex position
                        vertices[index] = new Vector3(
                            normalizedX * terrainSize.x,
                            waterHeight,
                            normalizedZ * terrainSize.z
                        );
                        
                        // Calculate UVs (scaled for tiling)
                        uvs[index] = new Vector2(
                            normalizedX * terrainSize.x / 10f,
                            normalizedZ * terrainSize.z / 10f
                        );
                    }
                }
                
                // Create triangles
                int triangleIndex = 0;
                for (int z = 0; z < resolution - 1; z++) {
                    for (int x = 0; x < resolution - 1; x++) {
                        int topLeft = z * resolution + x;
                        int topRight = topLeft + 1;
                        int bottomLeft = (z + 1) * resolution + x;
                        int bottomRight = bottomLeft + 1;
                        
                        // First triangle (top-left, bottom-left, bottom-right)
                        triangles[triangleIndex++] = topLeft;
                        triangles[triangleIndex++] = bottomLeft;
                        triangles[triangleIndex++] = bottomRight;
                        
                        // Second triangle (top-left, bottom-right, top-right)
                        triangles[triangleIndex++] = topLeft;
                        triangles[triangleIndex++] = bottomRight;
                        triangles[triangleIndex++] = topRight;
                    }
                }
                
                // Create the mesh
                Mesh mesh = new Mesh();
                mesh.name = "WaterPlaneMesh";
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.uv = uvs;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                
                return mesh;
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error creating water plane mesh: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        #endregion
        
        #region Mesh Optimization
        
        /// <summary>
        /// Optimizes a mesh by removing duplicate vertices and reindexing.
        /// </summary>
        /// <param name="mesh">The mesh to optimize</param>
        /// <returns>An optimized copy of the mesh</returns>
        public static Mesh OptimizeMesh(Mesh mesh) {
            if (mesh == null) {
                return null;
            }
            
            try {
                // Create vertex key dictionary to detect duplicates
                Dictionary<VertexKey, int> uniqueVertices = new Dictionary<VertexKey, int>();
                
                Vector3[] oldVertices = mesh.vertices;
                Vector2[] oldUVs = mesh.uv;
                Vector3[] oldNormals = mesh.normals;
                int[] oldTriangles = mesh.triangles;
                
                List<Vector3> newVertices = new List<Vector3>();
                List<Vector2> newUVs = new List<Vector2>();
                List<Vector3> newNormals = new List<Vector3>();
                List<int> newTriangles = new List<int>();
                
                // Process each triangle
                for (int i = 0; i < oldTriangles.Length; i += 3) {
                    int[] newIndices = new int[3];
                    
                    // Process each vertex of the triangle
                    for (int j = 0; j < 3; j++) {
                        int oldIndex = oldTriangles[i + j];
                        
                        // Create a key from position, normal, and UV
                        VertexKey key = new VertexKey(
                            oldVertices[oldIndex],
                            oldNormals.Length > oldIndex ? oldNormals[oldIndex] : Vector3.up,
                            oldUVs.Length > oldIndex ? oldUVs[oldIndex] : Vector2.zero
                        );
                        
                        // Check if we already have this vertex
                        if (!uniqueVertices.TryGetValue(key, out int newIndex)) {
                            // New unique vertex
                            newIndex = newVertices.Count;
                            uniqueVertices.Add(key, newIndex);
                            
                            newVertices.Add(oldVertices[oldIndex]);
                            
                            if (oldUVs.Length > oldIndex) {
                                newUVs.Add(oldUVs[oldIndex]);
                            }
                            
                            if (oldNormals.Length > oldIndex) {
                                newNormals.Add(oldNormals[oldIndex]);
                            }
                        }
                        
                        newIndices[j] = newIndex;
                    }
                    
                    // Add triangle indices
                    newTriangles.Add(newIndices[0]);
                    newTriangles.Add(newIndices[1]);
                    newTriangles.Add(newIndices[2]);
                }
                
                // Create optimized mesh
                Mesh optimizedMesh = new Mesh();
                optimizedMesh.name = mesh.name + "_Optimized";
                optimizedMesh.vertices = newVertices.ToArray();
                optimizedMesh.triangles = newTriangles.ToArray();
                
                if (newUVs.Count > 0) {
                    optimizedMesh.uv = newUVs.ToArray();
                }
                
                if (newNormals.Count > 0) {
                    optimizedMesh.normals = newNormals.ToArray();
                } else {
                    optimizedMesh.RecalculateNormals();
                }
                
                optimizedMesh.RecalculateBounds();
                
                return optimizedMesh;
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error optimizing mesh: {ex.Message}", LogCategory.AI);
                return mesh;
            }
        }
        
        /// <summary>
        /// Creates a simplified version of a mesh with fewer triangles.
        /// </summary>
        /// <param name="mesh">The mesh to simplify</param>
        /// <param name="quality">Quality factor (0-1) where 1 is highest quality</param>
        /// <returns>A simplified copy of the mesh</returns>
        public static Mesh SimplifyMesh(Mesh mesh, float quality) {
            if (mesh == null) {
                return null;
            }
            
            try {
                // Limit quality to valid range
                quality = Mathf.Clamp01(quality);
                
                // Calculate target triangle count
                int originalTriCount = mesh.triangles.Length / 3;
                int targetTriCount = Mathf.Max(1, Mathf.FloorToInt(originalTriCount * quality));
                
                // For this example, we'll use a simple triangle removal strategy
                // A more sophisticated approach would use quadric error metrics or similar
                
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                Vector2[] uvs = mesh.uv;
                Vector3[] normals = mesh.normals;
                
                // If target count is close to original, return a copy of the original
                if (targetTriCount >= originalTriCount * 0.9f) {
                    Mesh copy = new Mesh();
                    copy.name = mesh.name + "_Simplified";
                    copy.vertices = vertices;
                    copy.triangles = triangles;
                    copy.uv = uvs;
                    copy.normals = normals;
                    return copy;
                }
                
                // Calculate how many triangles to skip
                int skipEvery = Mathf.FloorToInt(originalTriCount / (float)(originalTriCount - targetTriCount));
                List<int> newTriangles = new List<int>();
                
                for (int i = 0; i < triangles.Length; i += 3) {
                    if ((i / 3) % skipEvery != 0) {
                        newTriangles.Add(triangles[i]);
                        newTriangles.Add(triangles[i + 1]);
                        newTriangles.Add(triangles[i + 2]);
                    }
                }
                
                // Create simplified mesh
                Mesh simplifiedMesh = new Mesh();
                simplifiedMesh.name = mesh.name + "_Simplified";
                simplifiedMesh.vertices = vertices;
                simplifiedMesh.triangles = newTriangles.ToArray();
                simplifiedMesh.uv = uvs;
                simplifiedMesh.normals = normals;
                simplifiedMesh.RecalculateBounds();
                
                return OptimizeMesh(simplifiedMesh);
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error simplifying mesh: {ex.Message}", LogCategory.AI);
                return mesh;
            }
        }
        
        /// <summary>
        /// Removes interior triangles from a mesh, keeping only the visible surface.
        /// </summary>
        /// <param name="mesh">The mesh to process</param>
        /// <returns>A copy of the mesh with interior triangles removed</returns>
        public static Mesh RemoveInteriorTriangles(Mesh mesh) {
            if (mesh == null) {
                return null;
            }
            
            try {
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                Vector2[] uvs = mesh.uv;
                
                // Count triangle occurrences for each edge
                Dictionary<Edge, int> edgeCount = new Dictionary<Edge, int>();
                
                for (int i = 0; i < triangles.Length; i += 3) {
                    int i1 = triangles[i];
                    int i2 = triangles[i + 1];
                    int i3 = triangles[i + 2];
                    
                    CountEdge(edgeCount, i1, i2);
                    CountEdge(edgeCount, i2, i3);
                    CountEdge(edgeCount, i3, i1);
                }
                
                // Keep only triangles with at least one boundary edge
                List<int> newTriangles = new List<int>();
                
                for (int i = 0; i < triangles.Length; i += 3) {
                    int i1 = triangles[i];
                    int i2 = triangles[i + 1];
                    int i3 = triangles[i + 2];
                    
                    Edge e1 = new Edge(i1, i2);
                    Edge e2 = new Edge(i2, i3);
                    Edge e3 = new Edge(i3, i1);
                    
                    // If any edge is a boundary (appears only once), keep this triangle
                    if (edgeCount[e1] == 1 || edgeCount[e2] == 1 || edgeCount[e3] == 1) {
                        newTriangles.Add(i1);
                        newTriangles.Add(i2);
                        newTriangles.Add(i3);
                    }
                }
                
                // Create clean mesh
                Mesh cleanMesh = new Mesh();
                cleanMesh.name = mesh.name + "_Clean";
                cleanMesh.vertices = vertices;
                cleanMesh.triangles = newTriangles.ToArray();
                cleanMesh.uv = uvs;
                cleanMesh.RecalculateNormals();
                cleanMesh.RecalculateBounds();
                
                return OptimizeMesh(cleanMesh);
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error removing interior triangles: {ex.Message}", LogCategory.AI);
                return mesh;
            }
        }
        
        /// <summary>
        /// Counts an edge in the edge dictionary.
        /// </summary>
        private static void CountEdge(Dictionary<Edge, int> edgeCount, int vertexA, int vertexB) {
            Edge edge = new Edge(vertexA, vertexB);
            
            if (edgeCount.ContainsKey(edge)) {
                edgeCount[edge]++;
            } else {
                edgeCount[edge] = 1;
            }
        }
        
        #endregion
        
        #region Mesh Combination
        
        /// <summary>
        /// Combines multiple meshes into a single mesh.
        /// </summary>
        /// <param name="meshes">Array of meshes to combine</param>
        /// <param name="transforms">Array of transforms for the meshes</param>
        /// <param name="mergeSubmeshes">Whether to merge submeshes</param>
        /// <returns>A combined mesh</returns>
        public static Mesh CombineMeshes(Mesh[] meshes, Transform[] transforms, bool mergeSubmeshes = true) {
            if (meshes == null || meshes.Length == 0 || transforms == null || transforms.Length != meshes.Length) {
                return null;
            }
            
            try {
                CombineInstance[] combine = new CombineInstance[meshes.Length];
                
                for (int i = 0; i < meshes.Length; i++) {
                    combine[i].mesh = meshes[i];
                    combine[i].transform = transforms[i].localToWorldMatrix;
                }
                
                Mesh combinedMesh = new Mesh();
                combinedMesh.name = "CombinedMesh";
                combinedMesh.CombineMeshes(combine, mergeSubmeshes);
                
                return combinedMesh;
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error combining meshes: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        /// <summary>
        /// Creates a mesh from a height field array.
        /// </summary>
        /// <param name="heights">2D array of height values</param>
        /// <param name="terrainSize">Size of the terrain in world units</param>
        /// <param name="heightScale">Scale factor for height values</param>
        /// <returns>A Unity Mesh representing the terrain</returns>
        public static Mesh CreateMeshFromHeightField(float[,] heights, Vector3 terrainSize, float heightScale) {
            if (heights == null) {
                return null;
            }
            
            try {
                int resolutionX = heights.GetLength(0);
                int resolutionZ = heights.GetLength(1);
                
                // Create vertex array
                Vector3[] vertices = new Vector3[resolutionX * resolutionZ];
                Vector2[] uvs = new Vector2[resolutionX * resolutionZ];
                int[] triangles = new int[(resolutionX - 1) * (resolutionZ - 1) * 6];
                
                // Create vertices
                for (int z = 0; z < resolutionZ; z++) {
                    for (int x = 0; x < resolutionX; x++) {
                        int index = z * resolutionX + x;
                        
                        // Calculate normalized position
                        float normalizedX = (float)x / (resolutionX - 1);
                        float normalizedZ = (float)z / (resolutionZ - 1);
                        
                        // Calculate vertex position
                        vertices[index] = new Vector3(
                            normalizedX * terrainSize.x,
                            heights[x, z] * heightScale,
                            normalizedZ * terrainSize.z
                        );
                        
                        // Calculate UVs
                        uvs[index] = new Vector2(normalizedX, normalizedZ);
                    }
                }
                
                // Create triangles
                int triangleIndex = 0;
                for (int z = 0; z < resolutionZ - 1; z++) {
                    for (int x = 0; x < resolutionX - 1; x++) {
                        int topLeft = z * resolutionX + x;
                        int topRight = topLeft + 1;
                        int bottomLeft = (z + 1) * resolutionX + x;
                        int bottomRight = bottomLeft + 1;
                        
                        // First triangle (top-left, bottom-left, bottom-right)
                        triangles[triangleIndex++] = topLeft;
                        triangles[triangleIndex++] = bottomLeft;
                        triangles[triangleIndex++] = bottomRight;
                        
                        // Second triangle (top-left, bottom-right, top-right)
                        triangles[triangleIndex++] = topLeft;
                        triangles[triangleIndex++] = bottomRight;
                        triangles[triangleIndex++] = topRight;
                    }
                }
                
                // Create the mesh
                Mesh mesh = new Mesh();
                mesh.name = "HeightFieldMesh";
                mesh.vertices = vertices;
                mesh.triangles = triangles;
                mesh.uv = uvs;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                
                return mesh;
            }
            catch (Exception ex) {
                TraversifyDebugger.Instance?.LogError($"Error creating mesh from height field: {ex.Message}", LogCategory.AI);
                return null;
            }
        }
        
        #endregion
        
        #region Nested Classes
        
        /// <summary>
        /// Represents a vertex with position, normal, and UV for comparison.
        /// </summary>
        private struct VertexKey {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector2 UV;
            
            public VertexKey(Vector3 position, Vector3 normal, Vector2 uv) {
                Position = position;
                Normal = normal;
                UV = uv;
            }
            
            public override bool Equals(object obj) {
                if (!(obj is VertexKey)) return false;
                
                VertexKey other = (VertexKey)obj;
                return Position == other.Position && Normal == other.Normal && UV == other.UV;
            }
            
            public override int GetHashCode() {
                int hash = 17;
                hash = hash * 31 + Position.GetHashCode();
                hash = hash * 31 + Normal.GetHashCode();
                hash = hash * 31 + UV.GetHashCode();
                return hash;
            }
        }
        
        /// <summary>
        /// Represents an edge between two vertices for boundary detection.
        /// </summary>
        private struct Edge {
            private readonly int _smallerVertex;
            private readonly int _largerVertex;
            
            public Edge(int a, int b) {
                _smallerVertex = Mathf.Min(a, b);
                _largerVertex = Mathf.Max(a, b);
            }
            
            public override bool Equals(object obj) {
                if (!(obj is Edge)) return false;
                
                Edge other = (Edge)obj;
                return _smallerVertex == other._smallerVertex && _largerVertex == other._largerVertex;
            }
            
            public override int GetHashCode() {
                return _smallerVertex.GetHashCode() ^ _largerVertex.GetHashCode();
            }
        }
        
        /// <summary>
        /// Helper class for triangulating polygon points.
        /// </summary>
        private class Triangulator {
            private readonly Vector2[] _points;
            
            public Triangulator(Vector2[] points) {
                _points = points;
            }
            
            public int[] Triangulate() {
                List<int> indices = new List<int>();
                
                int n = _points.Length;
                if (n < 3) {
                    return new int[0];
                }
                
                int[] V = new int[n];
                for (int v = 0; v < n; v++) {
                    V[v] = v;
                }
                
                int nv = n;
                int count = 2 * nv;
                
                for (int m = 0, v = nv - 1; nv > 2;) {
                    if ((count--) <= 0) {
                        return indices.ToArray();
                    }
                    
                    int u = v;
                    if (nv <= u) {
                        u = 0;
                    }
                    
                    v = u + 1;
                    if (nv <= v) {
                        v = 0;
                    }
                    
                    int w = v + 1;
                    if (nv <= w) {
                        w = 0;
                    }
                    
                    if (Snip(u, v, w, nv, V)) {
                        int a = V[u];
                        int b = V[v];
                        int c = V[w];
                        indices.Add(a);
                        indices.Add(b);
                        indices.Add(c);
                        m++;
                        
                        for (int s = v, t = v + 1; t < nv; s++, t++) {
                            V[s] = V[t];
                        }
                        
                        nv--;
                        count = 2 * nv;
                    }
                }
                
                indices.Reverse();
                return indices.ToArray();
            }
            
            private bool Snip(int u, int v, int w, int n, int[] V) {
                int p;
                Vector2 A = _points[V[u]];
                Vector2 B = _points[V[v]];
                Vector2 C = _points[V[w]];
                
                if (Mathf.Epsilon > (((B.x - A.x) * (C.y - A.y)) - ((B.y - A.y) * (C.x - A.x)))) {
                    return false;
                }
                
                for (p = 0; p < n; p++) {
                    if ((p == u) || (p == v) || (p == w)) {
                        continue;
                    }
                    
                    Vector2 P = _points[V[p]];
                    
                    if (InsideTriangle(A, B, C, P)) {
                        return false;
                    }
                }
                
                return true;
            }
            
            private bool InsideTriangle(Vector2 A, Vector2 B, Vector2 C, Vector2 P) {
                float ax, ay, bx, by, cx, cy, apx, apy, bpx, bpy, cpx, cpy;
                float cCROSSap, bCROSScp, aCROSSbp;
                
                ax = C.x - B.x;
                ay = C.y - B.y;
                bx = A.x - C.x;
                by = A.y - C.y;
                cx = B.x - A.x;
                cy = B.y - A.y;
                apx = P.x - A.x;
                apy = P.y - A.y;
                bpx = P.x - B.x;
                bpy = P.y - B.y;
                cpx = P.x - C.x;
                cpy = P.y - C.y;
                
                aCROSSbp = ax * bpy - ay * bpx;
                cCROSSap = cx * apy - cy * apx;
                bCROSScp = bx * cpy - by * cpx;
                
                return ((aCROSSbp >= 0.0f) && (bCROSScp >= 0.0f) && (cCROSSap >= 0.0f));
            }
        }
        
        #endregion
    }
}