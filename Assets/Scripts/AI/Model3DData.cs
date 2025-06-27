/*************************************************************************
 *  Traversify – Model3DData.cs                                          *
 *  Author : David Kaplan (dkaplan73)                                    *
 *  Created: 2025-06-27                                                  *
 *  Updated: 2025-06-27 03:53:59 UTC                                     *
 *  Desc   : Represents a 3D model's geometry and metadata within the    *
 *           Traversify environment generation pipeline. Stores vertex   *
 *           data, material properties, and generation parameters for    *
 *           models created via Tripo3D or other generation services.    *
 *************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using UnityEngine;

namespace Traversify.AI {
    /// <summary>
    /// Represents a 3D model's geometry and metadata within the Traversify environment
    /// generation pipeline. Used for transferring model data between generation services
    /// (like Tripo3D) and Unity scene instantiation.
    /// </summary>
    [Serializable]
    public class Model3DData {
        #region Core Geometry Properties
        
        /// <summary>
        /// Vertex positions in local space.
        /// </summary>
        public Vector3[] vertices;
        
        /// <summary>
        /// Triangle indices (groups of 3 integers indexing into vertices array).
        /// </summary>
        public int[] triangles;
        
        /// <summary>
        /// Vertex normals (same length as vertices).
        /// </summary>
        public Vector3[] normals;
        
        /// <summary>
        /// UV coordinates for texture mapping (same length as vertices).
        /// </summary>
        public Vector2[] uvs;
        
        /// <summary>
        /// Tangent vectors for normal mapping (same length as vertices).
        /// </summary>
        public Vector4[] tangents;
        
        /// <summary>
        /// Vertex colors (same length as vertices).
        /// </summary>
        public Color[] colors;
        
        /// <summary>
        /// UV2 coordinates for lightmapping (optional).
        /// </summary>
        public Vector2[] uv2;
        
        #endregion
        
        #region Material Properties
        
        /// <summary>
        /// Albedo/base color texture.
        /// </summary>
        public Texture2D albedoTexture;
        
        /// <summary>
        /// Normal map texture.
        /// </summary>
        public Texture2D normalTexture;
        
        /// <summary>
        /// Metallic-smoothness map texture.
        /// </summary>
        public Texture2D metallicTexture;
        
        /// <summary>
        /// Occlusion map texture.
        /// </summary>
        public Texture2D occlusionTexture;
        
        /// <summary>
        /// Emission map texture.
        /// </summary>
        public Texture2D emissionTexture;
        
        /// <summary>
        /// Base color tint.
        /// </summary>
        public Color albedoColor = Color.white;
        
        /// <summary>
        /// Metallic value (0-1).
        /// </summary>
        public float metallic = 0f;
        
        /// <summary>
        /// Smoothness value (0-1).
        /// </summary>
        public float smoothness = 0.5f;
        
        /// <summary>
        /// Emission color intensity.
        /// </summary>
        public Color emissionColor = Color.black;
        
        /// <summary>
        /// Normal map intensity.
        /// </summary>
        public float normalScale = 1f;
        
        /// <summary>
        /// Occlusion strength.
        /// </summary>
        public float occlusionStrength = 1f;
        
        /// <summary>
        /// List of submesh materials when model has multiple materials.
        /// </summary>
        public List<MaterialData> materials;
        
        /// <summary>
        /// Submesh index mappings if model uses multiple materials.
        /// </summary>
        public int[] submeshIndices;
        
        #endregion
        
        #region Metadata
        
        /// <summary>
        /// Name of the model.
        /// </summary>
        public string name;
        
        /// <summary>
        /// Description used to generate the model.
        /// </summary>
        public string generationPrompt;
        
        /// <summary>
        /// Type or category of the model.
        /// </summary>
        public string category;
        
        /// <summary>
        /// Service that generated the model (e.g., "Tripo3D").
        /// </summary>
        public string generationService;
        
        /// <summary>
        /// Generation parameters used.
        /// </summary>
        public Dictionary<string, object> generationParameters;
        
        /// <summary>
        /// Time when the model was generated.
        /// </summary>
        public DateTime generationTime;
        
        /// <summary>
        /// Generation version or model version.
        /// </summary>
        public string version;
        
        /// <summary>
        /// User who generated the model.
        /// </summary>
        public string creator;
        
        /// <summary>
        /// Unique identifier for this model.
        /// </summary>
        public string id;
        
        /// <summary>
        /// Additional metadata.
        /// </summary>
        public Dictionary<string, object> metadata;
        
        /// <summary>
        /// Flag indicating whether this model has been processed.
        /// </summary>
        public bool isProcessed;
        
        /// <summary>
        /// Optional bounds of the model.
        /// </summary>
        public Bounds bounds;
        
        /// <summary>
        /// Optional LOD levels for this model.
        /// </summary>
        public List<LODData> lodLevels;
        
        #endregion
        
        #region Constructors
        
        /// <summary>
        /// Default constructor.
        /// </summary>
        public Model3DData() {
            generationTime = DateTime.UtcNow;
            id = Guid.NewGuid().ToString();
            generationParameters = new Dictionary<string, object>();
            metadata = new Dictionary<string, object>();
            materials = new List<MaterialData>();
            lodLevels = new List<LODData>();
            creator = "dkaplan73"; // Current user
        }
        
        /// <summary>
        /// Constructor with basic mesh data.
        /// </summary>
        /// <param name="vertices">Vertex positions</param>
        /// <param name="triangles">Triangle indices</param>
        /// <param name="normals">Vertex normals</param>
        /// <param name="uvs">UV coordinates</param>
        public Model3DData(Vector3[] vertices, int[] triangles, Vector3[] normals, Vector2[] uvs) : this() {
            this.vertices = vertices;
            this.triangles = triangles;
            this.normals = normals;
            this.uvs = uvs;
            
            CalculateBounds();
        }
        
        /// <summary>
        /// Constructor from a Unity Mesh.
        /// </summary>
        /// <param name="mesh">Source mesh</param>
        public Model3DData(Mesh mesh) : this() {
            if (mesh == null) {
                throw new ArgumentNullException(nameof(mesh));
            }
            
            // Copy basic mesh data
            vertices = mesh.vertices;
            triangles = mesh.triangles;
            normals = mesh.normals;
            uvs = mesh.uv;
            
            // Copy additional data if available
            if (mesh.colors != null && mesh.colors.Length > 0) {
                colors = mesh.colors;
            }
            
            if (mesh.tangents != null && mesh.tangents.Length > 0) {
                tangents = mesh.tangents;
            }
            
            if (mesh.uv2 != null && mesh.uv2.Length > 0) {
                uv2 = mesh.uv2;
            }
            
            // Copy submesh data if available
            if (mesh.subMeshCount > 1) {
                submeshIndices = new int[mesh.triangles.Length / 3];
                for (int i = 0; i < mesh.subMeshCount; i++) {
                    int[] submeshTris = mesh.GetTriangles(i);
                    for (int j = 0; j < submeshTris.Length; j += 3) {
                        int triIndex = Array.IndexOf(mesh.triangles, submeshTris[j]) / 3;
                        if (triIndex >= 0) {
                            submeshIndices[triIndex] = i;
                        }
                    }
                }
            }
            
            name = mesh.name;
            bounds = mesh.bounds;
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Creates a Unity Mesh from this model data.
        /// </summary>
        /// <returns>A new Unity Mesh</returns>
        public Mesh CreateMesh() {
            Mesh mesh = new Mesh();
            
            // Check for 16-bit index buffer limit
            if (vertices != null && vertices.Length > 65535) {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            
            // Assign basic mesh data
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            
            // Assign additional data if available
            if (normals != null && normals.Length == vertices.Length) {
                mesh.normals = normals;
            } else {
                mesh.RecalculateNormals();
            }
            
            if (uvs != null && uvs.Length == vertices.Length) {
                mesh.uv = uvs;
            }
            
            if (tangents != null && tangents.Length == vertices.Length) {
                mesh.tangents = tangents;
            }
            
            if (colors != null && colors.Length == vertices.Length) {
                mesh.colors = colors;
            }
            
            if (uv2 != null && uv2.Length == vertices.Length) {
                mesh.uv2 = uv2;
            }
            
            // Set submesh data if available
            if (submeshIndices != null && submeshIndices.Length > 0 && materials != null && materials.Count > 0) {
                int submeshCount = materials.Count;
                mesh.subMeshCount = submeshCount;
                
                // Group triangles by submesh
                Dictionary<int, List<int>> submeshTriangles = new Dictionary<int, List<int>>();
                for (int i = 0; i < submeshCount; i++) {
                    submeshTriangles[i] = new List<int>();
                }
                
                for (int i = 0; i < triangles.Length / 3; i++) {
                    int submeshIndex = submeshIndices[i];
                    if (submeshIndex >= 0 && submeshIndex < submeshCount) {
                        submeshTriangles[submeshIndex].Add(triangles[i * 3]);
                        submeshTriangles[submeshIndex].Add(triangles[i * 3 + 1]);
                        submeshTriangles[submeshIndex].Add(triangles[i * 3 + 2]);
                    }
                }
                
                // Set triangles for each submesh
                for (int i = 0; i < submeshCount; i++) {
                    mesh.SetTriangles(submeshTriangles[i].ToArray(), i);
                }
            }
            
            // Set bounds if available, otherwise recalculate
            if (bounds.size != Vector3.zero) {
                mesh.bounds = bounds;
            } else {
                mesh.RecalculateBounds();
            }
            
            // Set name
            mesh.name = string.IsNullOrEmpty(name) ? "GeneratedMesh" : name;
            
            return mesh;
        }
        
        /// <summary>
        /// Creates a Unity Material from this model's material data.
        /// </summary>
        /// <returns>A new Unity Material</returns>
        public Material CreateMaterial() {
            Material material = new Material(Shader.Find("Standard"));
            
            // Set basic properties
            material.name = string.IsNullOrEmpty(name) ? "GeneratedMaterial" : $"{name}_Material";
            material.color = albedoColor;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            material.SetFloat("_BumpScale", normalScale);
            material.SetFloat("_OcclusionStrength", occlusionStrength);
            material.SetColor("_EmissionColor", emissionColor);
            
            // Set emission if needed
            if (emissionColor != Color.black) {
                material.EnableKeyword("_EMISSION");
            }
            
            // Assign textures if available
            if (albedoTexture != null) {
                material.mainTexture = albedoTexture;
            }
            
            if (normalTexture != null) {
                material.SetTexture("_BumpMap", normalTexture);
                material.EnableKeyword("_NORMALMAP");
            }
            
            if (metallicTexture != null) {
                material.SetTexture("_MetallicGlossMap", metallicTexture);
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            
            if (occlusionTexture != null) {
                material.SetTexture("_OcclusionMap", occlusionTexture);
            }
            
            if (emissionTexture != null) {
                material.SetTexture("_EmissionMap", emissionTexture);
                material.EnableKeyword("_EMISSION");
            }
            
            return material;
        }
        
        /// <summary>
        /// Creates an array of Unity Materials from this model's submesh materials.
        /// </summary>
        /// <returns>Array of Unity Materials</returns>
        public Material[] CreateMaterialArray() {
            if (materials == null || materials.Count == 0) {
                return new Material[] { CreateMaterial() };
            }
            
            Material[] result = new Material[materials.Count];
            
            for (int i = 0; i < materials.Count; i++) {
                MaterialData materialData = materials[i];
                Material material = new Material(Shader.Find("Standard"));
                
                material.name = string.IsNullOrEmpty(materialData.name) ? $"Material_{i}" : materialData.name;
                material.color = materialData.albedoColor;
                material.SetFloat("_Metallic", materialData.metallic);
                material.SetFloat("_Glossiness", materialData.smoothness);
                material.SetFloat("_BumpScale", materialData.normalScale);
                material.SetFloat("_OcclusionStrength", materialData.occlusionStrength);
                material.SetColor("_EmissionColor", materialData.emissionColor);
                
                if (materialData.emissionColor != Color.black) {
                    material.EnableKeyword("_EMISSION");
                }
                
                if (materialData.albedoTexture != null) {
                    material.mainTexture = materialData.albedoTexture;
                }
                
                if (materialData.normalTexture != null) {
                    material.SetTexture("_BumpMap", materialData.normalTexture);
                    material.EnableKeyword("_NORMALMAP");
                }
                
                if (materialData.metallicTexture != null) {
                    material.SetTexture("_MetallicGlossMap", materialData.metallicTexture);
                    material.EnableKeyword("_METALLICGLOSSMAP");
                }
                
                if (materialData.occlusionTexture != null) {
                    material.SetTexture("_OcclusionMap", materialData.occlusionTexture);
                }
                
                if (materialData.emissionTexture != null) {
                    material.SetTexture("_EmissionMap", materialData.emissionTexture);
                    material.EnableKeyword("_EMISSION");
                }
                
                result[i] = material;
            }
            
            return result;
        }
        
        /// <summary>
        /// Creates a GameObject with this model's mesh and materials.
        /// </summary>
        /// <returns>A new GameObject with mesh and materials</returns>
        public GameObject CreateGameObject() {
            GameObject go = new GameObject(string.IsNullOrEmpty(name) ? "GeneratedModel" : name);
            
            // Add mesh components
            MeshFilter meshFilter = go.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            
            // Assign mesh
            meshFilter.sharedMesh = CreateMesh();
            
            // Assign materials
            if (materials != null && materials.Count > 0) {
                meshRenderer.sharedMaterials = CreateMaterialArray();
            } else {
                meshRenderer.sharedMaterial = CreateMaterial();
            }
            
            // Add mesh collider if needed
            if (metadata != null && metadata.ContainsKey("addCollider") && (bool)metadata["addCollider"]) {
                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
            }
            
            // Add LOD Group if needed
            if (lodLevels != null && lodLevels.Count > 0) {
                LODGroup lodGroup = go.AddComponent<LODGroup>();
                LOD[] lods = new LOD[lodLevels.Count + 1];
                
                // Add original mesh as LOD0
                lods[0] = new LOD(1.0f, new Renderer[] { meshRenderer });
                
                // Add LOD levels
                for (int i = 0; i < lodLevels.Count; i++) {
                    LODData lodData = lodLevels[i];
                    GameObject lodGo = new GameObject($"LOD{i+1}");
                    lodGo.transform.SetParent(go.transform, false);
                    
                    MeshFilter lodMeshFilter = lodGo.AddComponent<MeshFilter>();
                    MeshRenderer lodMeshRenderer = lodGo.AddComponent<MeshRenderer>();
                    
                    Mesh lodMesh = new Mesh();
                    lodMesh.vertices = lodData.vertices;
                    lodMesh.triangles = lodData.triangles;
                    lodMesh.normals = lodData.normals ?? new Vector3[lodData.vertices.Length];
                    lodMesh.uv = lodData.uvs ?? new Vector2[lodData.vertices.Length];
                    lodMesh.RecalculateBounds();
                    
                    lodMeshFilter.sharedMesh = lodMesh;
                    lodMeshRenderer.sharedMaterial = meshRenderer.sharedMaterial;
                    
                    lods[i+1] = new LOD(lodData.screenRelativeTransitionHeight, new Renderer[] { lodMeshRenderer });
                }
                
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
            }
            
            return go;
        }
        
        /// <summary>
        /// Calculates the bounds of the model.
        /// </summary>
        public void CalculateBounds() {
            if (vertices == null || vertices.Length == 0) {
                bounds = new Bounds(Vector3.zero, Vector3.zero);
                return;
            }
            
            Vector3 min = vertices[0];
            Vector3 max = vertices[0];
            
            for (int i = 1; i < vertices.Length; i++) {
                min = Vector3.Min(min, vertices[i]);
                max = Vector3.Max(max, vertices[i]);
            }
            
            bounds = new Bounds((min + max) * 0.5f, max - min);
        }
        
        /// <summary>
        /// Recalculates the normals of the model.
        /// </summary>
        public void RecalculateNormals() {
            if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length == 0) {
                return;
            }
            
            normals = new Vector3[vertices.Length];
            
            for (int i = 0; i < triangles.Length; i += 3) {
                int i1 = triangles[i];
                int i2 = triangles[i + 1];
                int i3 = triangles[i + 2];
                
                Vector3 v1 = vertices[i1];
                Vector3 v2 = vertices[i2];
                Vector3 v3 = vertices[i3];
                
                Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;
                
                normals[i1] += normal;
                normals[i2] += normal;
                normals[i3] += normal;
            }
            
            for (int i = 0; i < normals.Length; i++) {
                if (normals[i] != Vector3.zero) {
                    normals[i].Normalize();
                } else {
                    normals[i] = Vector3.up;
                }
            }
        }
        
        /// <summary>
        /// Recalculates the tangents of the model.
        /// </summary>
        public void RecalculateTangents() {
            if (vertices == null || triangles == null || normals == null || uvs == null ||
                vertices.Length == 0 || triangles.Length == 0 || normals.Length == 0 || uvs.Length == 0) {
                return;
            }
            
            tangents = new Vector4[vertices.Length];
            Vector3[] tan1 = new Vector3[vertices.Length];
            Vector3[] tan2 = new Vector3[vertices.Length];
            
            for (int i = 0; i < triangles.Length; i += 3) {
                int i1 = triangles[i];
                int i2 = triangles[i + 1];
                int i3 = triangles[i + 2];
                
                Vector3 v1 = vertices[i1];
                Vector3 v2 = vertices[i2];
                Vector3 v3 = vertices[i3];
                
                Vector2 w1 = uvs[i1];
                Vector2 w2 = uvs[i2];
                Vector2 w3 = uvs[i3];
                
                float x1 = v2.x - v1.x;
                float x2 = v3.x - v1.x;
                float y1 = v2.y - v1.y;
                float y2 = v3.y - v1.y;
                float z1 = v2.z - v1.z;
                float z2 = v3.z - v1.z;
                
                float s1 = w2.x - w1.x;
                float s2 = w3.x - w1.x;
                float t1 = w2.y - w1.y;
                float t2 = w3.y - w1.y;
                
                float r = 1.0f / (s1 * t2 - s2 * t1);
                Vector3 sdir = new Vector3((t2 * x1 - t1 * x2) * r, (t2 * y1 - t1 * y2) * r, (t2 * z1 - t1 * z2) * r);
                Vector3 tdir = new Vector3((s1 * x2 - s2 * x1) * r, (s1 * y2 - s2 * y1) * r, (s1 * z2 - s2 * z1) * r);
                
                tan1[i1] += sdir;
                tan1[i2] += sdir;
                tan1[i3] += sdir;
                
                tan2[i1] += tdir;
                tan2[i2] += tdir;
                tan2[i3] += tdir;
            }
            
            for (int i = 0; i < vertices.Length; i++) {
                Vector3 n = normals[i];
                Vector3 t = tan1[i];
                
                // Gram-Schmidt orthogonalize
                Vector3 tangent = (t - n * Vector3.Dot(n, t)).normalized;
                
                // Calculate handedness
                float w = (Vector3.Dot(Vector3.Cross(n, t), tan2[i]) < 0.0f) ? -1.0f : 1.0f;
                
                tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, w);
            }
        }
        
        /// <summary>
        /// Optimizes the model data by removing duplicate vertices and reindexing.
        /// </summary>
        public void Optimize() {
            if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length == 0) {
                return;
            }
            
            Dictionary<VertexKey, int> uniqueVertices = new Dictionary<VertexKey, int>();
            List<Vector3> newVertices = new List<Vector3>();
            List<Vector3> newNormals = new List<Vector3>();
            List<Vector2> newUvs = new List<Vector2>();
            List<Vector4> newTangents = new List<Vector4>();
            List<Color> newColors = new List<Color>();
            List<Vector2> newUv2 = new List<Vector2>();
            List<int> newTriangles = new List<int>();
            
            // Build new vertex list and remap triangles
            for (int i = 0; i < triangles.Length; i++) {
                int oldIndex = triangles[i];
                VertexKey key = new VertexKey(
                    vertices[oldIndex],
                    normals != null && oldIndex < normals.Length ? normals[oldIndex] : Vector3.zero,
                    uvs != null && oldIndex < uvs.Length ? uvs[oldIndex] : Vector2.zero
                );
                
                if (!uniqueVertices.TryGetValue(key, out int newIndex)) {
                    newIndex = newVertices.Count;
                    uniqueVertices.Add(key, newIndex);
                    
                    newVertices.Add(vertices[oldIndex]);
                    
                    if (normals != null && oldIndex < normals.Length) {
                        newNormals.Add(normals[oldIndex]);
                    }
                    
                    if (uvs != null && oldIndex < uvs.Length) {
                        newUvs.Add(uvs[oldIndex]);
                    }
                    
                    if (tangents != null && oldIndex < tangents.Length) {
                        newTangents.Add(tangents[oldIndex]);
                    }
                    
                    if (colors != null && oldIndex < colors.Length) {
                        newColors.Add(colors[oldIndex]);
                    }
                    
                    if (uv2 != null && oldIndex < uv2.Length) {
                        newUv2.Add(uv2[oldIndex]);
                    }
                }
                
                newTriangles.Add(newIndex);
            }
            
            // Update model data
            vertices = newVertices.ToArray();
            triangles = newTriangles.ToArray();
            
            if (newNormals.Count > 0) {
                normals = newNormals.ToArray();
            }
            
            if (newUvs.Count > 0) {
                uvs = newUvs.ToArray();
            }
            
            if (newTangents.Count > 0) {
                tangents = newTangents.ToArray();
            }
            
            if (newColors.Count > 0) {
                colors = newColors.ToArray();
            }
            
            if (newUv2.Count > 0) {
                uv2 = newUv2.ToArray();
            }
            
            // Recalculate bounds
            CalculateBounds();
            
            // Invalidate submesh indices as they're no longer valid
            submeshIndices = null;
        }
        
        /// <summary>
        /// Creates a simplified version of this model as a new LOD level.
        /// </summary>
        /// <param name="targetPercentage">Target percentage of triangles to keep (0-1)</param>
        public void AddLODLevel(float targetPercentage) {
            if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length == 0) {
                return;
            }
            
            // Simple decimation - remove random triangles
            int targetTriCount = Mathf.FloorToInt(triangles.Length / 3 * targetPercentage) * 3;
            int[] simplifiedTriangles = new int[targetTriCount];
            Array.Copy(triangles, simplifiedTriangles, targetTriCount);
            
            // Create vertex map
            HashSet<int> usedIndices = new HashSet<int>();
            for (int i = 0; i < targetTriCount; i++) {
                usedIndices.Add(simplifiedTriangles[i]);
            }
            
            // Create remapped arrays
            Vector3[] simplifiedVertices = new Vector3[usedIndices.Count];
            Vector3[] simplifiedNormals = normals != null ? new Vector3[usedIndices.Count] : null;
            Vector2[] simplifiedUvs = uvs != null ? new Vector2[usedIndices.Count] : null;
            
            Dictionary<int, int> indexMap = new Dictionary<int, int>();
            int newIndex = 0;
            
            foreach (int oldIndex in usedIndices) {
                indexMap[oldIndex] = newIndex;
                
                simplifiedVertices[newIndex] = vertices[oldIndex];
                
                if (simplifiedNormals != null) {
                    simplifiedNormals[newIndex] = normals[oldIndex];
                }
                
                if (simplifiedUvs != null) {
                    simplifiedUvs[newIndex] = uvs[oldIndex];
                }
                
                newIndex++;
            }
            
            // Remap triangle indices
            for (int i = 0; i < targetTriCount; i++) {
                simplifiedTriangles[i] = indexMap[simplifiedTriangles[i]];
            }
            
            // Add to LOD levels
            if (lodLevels == null) {
                lodLevels = new List<LODData>();
            }
            
            lodLevels.Add(new LODData {
                vertices = simplifiedVertices,
                triangles = simplifiedTriangles,
                normals = simplifiedNormals,
                uvs = simplifiedUvs,
                screenRelativeTransitionHeight = 0.125f * (1f - targetPercentage)
            });
        }
        
        /// <summary>
        /// Returns a string representation of this model data.
        /// </summary>
        public override string ToString() {
            return $"Model3DData[{id}]: Name={name}, " +
                   $"Verts={vertices?.Length ?? 0}, Tris={(triangles?.Length ?? 0) / 3}, " +
                   $"Materials={materials?.Count ?? 0}, " +
                   $"LODs={lodLevels?.Count ?? 0}";
        }
        
        #endregion
        
        #region Serialization Helpers
        
        /// <summary>
        /// Serializes this model to a byte array.
        /// </summary>
        /// <returns>Serialized data as byte array</returns>
        public byte[] Serialize() {
            // Use Unity's JSON serialization for the class metadata
            string json = JsonUtility.ToJson(this);
            byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
            
            // Write binary data for large arrays
            using (MemoryStream ms = new MemoryStream()) {
                using (BinaryWriter writer = new BinaryWriter(ms)) {
                    // Write the JSON data length
                    writer.Write(jsonBytes.Length);
                    writer.Write(jsonBytes);
                    
                    // Write vertex data
                    SerializeVector3Array(writer, vertices);
                    SerializeIntArray(writer, triangles);
                    SerializeVector3Array(writer, normals);
                    SerializeVector2Array(writer, uvs);
                    SerializeVector4Array(writer, tangents);
                    SerializeColorArray(writer, colors);
                    SerializeVector2Array(writer, uv2);
                    
                    // Write textures
                    SerializeTexture(writer, albedoTexture);
                    SerializeTexture(writer, normalTexture);
                    SerializeTexture(writer, metallicTexture);
                    SerializeTexture(writer, occlusionTexture);
                    SerializeTexture(writer, emissionTexture);
                }
                
                return ms.ToArray();
            }
        }
        
        /// <summary>
        /// Deserializes a model from a byte array.
        /// </summary>
        /// <param name="data">Serialized data</param>
        /// <returns>Deserialized Model3DData</returns>
        public static Model3DData Deserialize(byte[] data) {
            using (MemoryStream ms = new MemoryStream(data)) {
                using (BinaryReader reader = new BinaryReader(ms)) {
                    // Read the JSON data
                    int jsonLength = reader.ReadInt32();
                    byte[] jsonBytes = reader.ReadBytes(jsonLength);
                    string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                    
                    // Create the model from JSON
                    Model3DData model = JsonUtility.FromJson<Model3DData>(json);
                    
                    // Read binary data
                    model.vertices = DeserializeVector3Array(reader);
                    model.triangles = DeserializeIntArray(reader);
                    model.normals = DeserializeVector3Array(reader);
                    model.uvs = DeserializeVector2Array(reader);
                    model.tangents = DeserializeVector4Array(reader);
                    model.colors = DeserializeColorArray(reader);
                    model.uv2 = DeserializeVector2Array(reader);
                    
                    // Read textures
                    model.albedoTexture = DeserializeTexture(reader);
                    model.normalTexture = DeserializeTexture(reader);
                    model.metallicTexture = DeserializeTexture(reader);
                    model.occlusionTexture = DeserializeTexture(reader);
                    model.emissionTexture = DeserializeTexture(reader);
                    
                    return model;
                }
            }
        }
        
        // Helpers for serializing arrays
        private static void SerializeVector3Array(BinaryWriter writer, Vector3[] array) {
            if (array == null) {
                writer.Write(0);
                return;
            }
            
            writer.Write(array.Length);
            foreach (Vector3 v in array) {
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
            }
        }
        
        private static void SerializeVector2Array(BinaryWriter writer, Vector2[] array) {
            if (array == null) {
                writer.Write(0);
                return;
            }
            
            writer.Write(array.Length);
            foreach (Vector2 v in array) {
                writer.Write(v.x);
                writer.Write(v.y);
            }
        }
        
        private static void SerializeVector4Array(BinaryWriter writer, Vector4[] array) {
            if (array == null) {
                writer.Write(0);
                return;
            }
            
            writer.Write(array.Length);
            foreach (Vector4 v in array) {
                writer.Write(v.x);
                writer.Write(v.y);
                writer.Write(v.z);
                writer.Write(v.w);
            }
        }
        
        private static void SerializeIntArray(BinaryWriter writer, int[] array) {
            if (array == null) {
                writer.Write(0);
                return;
            }
            
            writer.Write(array.Length);
            foreach (int i in array) {
                writer.Write(i);
            }
        }
        
        private static void SerializeColorArray(BinaryWriter writer, Color[] array) {
            if (array == null) {
                writer.Write(0);
                return;
            }
            
            writer.Write(array.Length);
            foreach (Color c in array) {
                writer.Write(c.r);
                writer.Write(c.g);
                writer.Write(c.b);
                writer.Write(c.a);
            }
        }
        
        private static void SerializeTexture(BinaryWriter writer, Texture2D texture) {
            if (texture == null) {
                writer.Write(false);
                return;
            }
            
            writer.Write(true);
            writer.Write(texture.width);
            writer.Write(texture.height);
            writer.Write((int)texture.format);
            
            byte[] textureBytes = texture.EncodeToPNG();
            writer.Write(textureBytes.Length);
            writer.Write(textureBytes);
        }
        
        // Helpers for deserializing arrays
        private static Vector3[] DeserializeVector3Array(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length == 0) {
                return null;
            }
            
            Vector3[] array = new Vector3[length];
            for (int i = 0; i < length; i++) {
                array[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            return array;
        }
        
        private static Vector2[] DeserializeVector2Array(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length == 0) {
                return null;
            }
            
            Vector2[] array = new Vector2[length];
            for (int i = 0; i < length; i++) {
                array[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }
            return array;
        }
        
        private static Vector4[] DeserializeVector4Array(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length == 0) {
                return null;
            }
            
            Vector4[] array = new Vector4[length];
            for (int i = 0; i < length; i++) {
                array[i] = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            return array;
        }
        
        private static int[] DeserializeIntArray(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length == 0) {
                return null;
            }
            
            int[] array = new int[length];
            for (int i = 0; i < length; i++) {
                array[i] = reader.ReadInt32();
            }
            return array;
        }
        
        private static Color[] DeserializeColorArray(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length == 0) {
                return null;
            }
            
            Color[] array = new Color[length];
            for (int i = 0; i < length; i++) {
                array[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }
            return array;
        }
        
        private static Texture2D DeserializeTexture(BinaryReader reader) {
            bool hasTexture = reader.ReadBoolean();
            if (!hasTexture) {
                return null;
            }
            
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            TextureFormat format = (TextureFormat)reader.ReadInt32();
            
            int byteLength = reader.ReadInt32();
            byte[] textureBytes = reader.ReadBytes(byteLength);
            
            Texture2D texture = new Texture2D(width, height, format, false);
            texture.LoadImage(textureBytes);
            
            return texture;
        }
        
        #endregion
    }
    
    /// <summary>
    /// Represents material data for a submesh.
    /// </summary>
    [Serializable]
    public class MaterialData {
        /// <summary>
        /// Name of the material.
        /// </summary>
        public string name;
        
        /// <summary>
        /// Albedo/base color texture.
        /// </summary>
        public Texture2D albedoTexture;
        
        /// <summary>
        /// Normal map texture.
        /// </summary>
        public Texture2D normalTexture;
        
        /// <summary>
        /// Metallic-smoothness map texture.
        /// </summary>
        public Texture2D metallicTexture;
        
        /// <summary>
        /// Occlusion map texture.
        /// </summary>
        public Texture2D occlusionTexture;
        
        /// <summary>
        /// Emission map texture.
        /// </summary>
        public Texture2D emissionTexture;
        
        /// <summary>
        /// Base color tint.
        /// </summary>
        public Color albedoColor = Color.white;
        
        /// <summary>
        /// Metallic value (0-1).
        /// </summary>
        public float metallic = 0f;
        
        /// <summary>
        /// Smoothness value (0-1).
        /// </summary>
        public float smoothness = 0.5f;
        
        /// <summary>
        /// Emission color intensity.
        /// </summary>
        public Color emissionColor = Color.black;
        
        /// <summary>
        /// Normal map intensity.
        /// </summary>
        public float normalScale = 1f;
        
        /// <summary>
        /// Occlusion strength.
        /// </summary>
        public float occlusionStrength = 1f;
    }
    
    /// <summary>
    /// Represents data for a Level of Detail (LOD) level.
    /// </summary>
    [Serializable]
    public class LODData {
        /// <summary>
        /// Vertices for this LOD level.
        /// </summary>
        public Vector3[] vertices;
        
        /// <summary>
        /// Triangle indices for this LOD level.
        /// </summary>
        public int[] triangles;
        
        /// <summary>
        /// Normals for this LOD level.
        /// </summary>
        public Vector3[] normals;
        
        /// <summary>
        /// UVs for this LOD level.
        /// </summary>
        public Vector2[] uvs;
        
        /// <summary>
        /// Screen-relative transition height for this LOD level.
        /// </summary>
        public float screenRelativeTransitionHeight = 0.125f;
    }
    
    /// <summary>
    /// Key for optimizing vertices by combining identical ones.
    /// </summary>
    internal struct VertexKey {
        private readonly Vector3 position;
        private readonly Vector3 normal;
        private readonly Vector2 uv;
        
        public VertexKey(Vector3 position, Vector3 normal, Vector2 uv) {
            this.position = position;
            this.normal = normal;
            this.uv = uv;
        }
        
        public override bool Equals(object obj) {
            if (!(obj is VertexKey)) {
                return false;
            }
            
            VertexKey other = (VertexKey)obj;
            return position == other.position && normal == other.normal && uv == other.uv;
        }
        
        public override int GetHashCode() {
            unchecked {
                int hash = 17;
                hash = hash * 23 + position.GetHashCode();
                hash = hash * 23 + normal.GetHashCode();
                hash = hash * 23 + uv.GetHashCode();
                return hash;
            }
        }
    }
}