// RuntimeResourceLoader — loads ET game resources from etmain at runtime.
//
// Replaces the Unity Editor ScriptedImporter pipeline for live play:
//   FileSystem (PK3/loose) → parse bytes → build Unity objects
//
// Entry points used by ETGameManager:
//   LoadBspScene(mapName, parent)  — parses .bsp, builds scene GameObject tree
//   LoadMd3(path)                  — parses .md3, returns GameObject
//   LoadMds(path)                  — parses .mds, returns SkinnedMeshRenderer root
//   LoadTexture(path)              — loads TGA/JPG/PNG → Texture2D
//   LoadAudioClip(path)            — loads WAV → AudioClip

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using ET.Core;

public static class RuntimeResourceLoader
{
    // Shader name to use for opaque surfaces in URP / Built-in RP
    private const string OPAQUE_SHADER       = "Universal Render Pipeline/Lit";
    private const string TRANSPARENT_SHADER  = "Universal Render Pipeline/Lit";
    private const string FALLBACK_SHADER     = "Standard";

    // Entity string from the most recently loaded BSP (for G_SpawnEntitiesFromString)
    public static string LastBspEntityString { get; private set; } = "";

    // =========================================================================
    // Texture cache — keyed by normalized virtual path
    // =========================================================================
    private static readonly Dictionary<string, Texture2D> _texCache =
        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Material> _matCache =
        new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

    // =========================================================================
    // Public: LoadBspScene
    // Parses mapName.bsp from etmain and builds a Unity GameObject hierarchy.
    // Returns the root GameObject (caller should position it in the scene).
    // =========================================================================
    public static GameObject LoadBspScene(string mapName, Transform parent = null)
    {
        string bspPath = $"maps/{mapName}.bsp";
        byte[] data = FileSystem.FS_ReadFile(bspPath);

        // Some PK3s store entries with uppercase extension; try alternate casing.
        if (data == null)
            data = FileSystem.FS_ReadFile($"maps/{mapName}.BSP");

        if (data == null)
        {
            // List all .bsp files found so the developer can see what IS available.
            var found = FileSystem.FS_GetFileList("maps", ".bsp");
            if (found.Length == 0)
                Debug.LogError($"[RuntimeResourceLoader] Cannot find BSP '{bspPath}'. " +
                               "No .bsp files found in any PK3/directory under 'maps/'.");
            else
                Debug.LogError($"[RuntimeResourceLoader] Cannot find BSP '{bspPath}'. " +
                               $"Available maps: {string.Join(", ", found)}");
            return null;
        }

        BspData bsp;
        try   { bsp = BspData.Load(data); }
        catch (Exception ex)
        {
            Debug.LogError($"[RuntimeResourceLoader] BSP parse error '{bspPath}': {ex.Message}");
            return null;
        }

        LastBspEntityString = bsp.EntityString ?? "";
        return BuildBspScene(bsp, mapName, parent);
    }

    // =========================================================================
    // Public: LoadMd3
    // =========================================================================
    public static GameObject LoadMd3(string virtualPath, Transform parent = null)
    {
        byte[] data = FileSystem.FS_ReadFile(virtualPath);
        if (data == null)
        {
            Debug.LogWarning($"[RuntimeResourceLoader] MD3 not found: {virtualPath}");
            return null;
        }

        Md3Data md3;
        try   { md3 = Md3Data.Load(data); }
        catch (Exception ex)
        {
            Debug.LogError($"[RuntimeResourceLoader] MD3 parse error '{virtualPath}': {ex.Message}");
            return null;
        }

        return BuildMd3Object(md3, Path.GetFileNameWithoutExtension(virtualPath), parent);
    }

    // =========================================================================
    // Public: LoadMds
    // =========================================================================
    public static GameObject LoadMds(string virtualPath, Transform parent = null)
    {
        byte[] data = FileSystem.FS_ReadFile(virtualPath);
        if (data == null)
        {
            Debug.LogWarning($"[RuntimeResourceLoader] MDS not found: {virtualPath}");
            return null;
        }

        MdsData mds;
        try   { mds = MdsData.Load(data); }
        catch (Exception ex)
        {
            Debug.LogError($"[RuntimeResourceLoader] MDS parse error '{virtualPath}': {ex.Message}");
            return null;
        }

        return BuildMdsObject(mds, Path.GetFileNameWithoutExtension(virtualPath), parent);
    }

    // =========================================================================
    // Public: LoadTexture
    // Tries the path verbatim, then with common ET texture extensions.
    // =========================================================================
    public static Texture2D LoadTexture(string virtualPath)
    {
        if (string.IsNullOrEmpty(virtualPath)) return null;

        string key = virtualPath.Replace('\\', '/').ToLowerInvariant();
        if (_texCache.TryGetValue(key, out var cached)) return cached;

        // Strip extension so we can try multiple formats
        string noExt = Path.GetFileNameWithoutExtension(key);
        string dir   = Path.GetDirectoryName(key)?.Replace('\\', '/') ?? "";
        if (!string.IsNullOrEmpty(dir)) dir += "/";

        string[] candidates =
        {
            key,                    // exact path as given
            dir + noExt + ".tga",
            dir + noExt + ".jpg",
            dir + noExt + ".jpeg",
            dir + noExt + ".png",
            dir + noExt + ".dds",
        };

        foreach (string candidate in candidates)
        {
            byte[] raw = FileSystem.FS_ReadFile(candidate);
            if (raw == null) continue;

            var tex = LoadTextureFromBytes(raw, candidate);
            if (tex != null)
            {
                tex.name = key;
                _texCache[key] = tex;
                return tex;
            }

            // File was found in PK3 but decode failed — log and keep trying other extensions
            Debug.LogWarning($"[RuntimeResourceLoader] Decode failed for '{candidate}' " +
                             $"({raw.Length} bytes, ext={Path.GetExtension(candidate)})");
        }

        Debug.LogWarning($"[RuntimeResourceLoader] Texture not found: '{virtualPath}' " +
                         $"(tried: {string.Join(", ", candidates)})");
        _texCache[key] = null;   // negative cache
        return null;
    }

    // =========================================================================
    // Public: LoadAudioClip
    // Reads a WAV file from etmain and returns an AudioClip.
    // =========================================================================
    public static AudioClip LoadAudioClip(string virtualPath)
    {
        if (string.IsNullOrEmpty(virtualPath)) return null;

        // Normalise: try exact path, then .wav
        string[] paths =
        {
            virtualPath,
            Path.ChangeExtension(virtualPath, ".wav"),
            Path.ChangeExtension(virtualPath, ".ogg"),
        };

        foreach (string p in paths)
        {
            byte[] data = FileSystem.FS_ReadFile(p);
            if (data == null) continue;

            string ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext == ".wav")
            {
                var clip = DecodeWav(data, Path.GetFileNameWithoutExtension(p));
                if (clip != null) return clip;
            }
            // OGG/MP3 would need a native plugin; skip for now
        }

        return null;
    }

    // =========================================================================
    // Internal: build BSP scene
    // =========================================================================
    private static GameObject BuildBspScene(BspData bsp, string mapName, Transform parent)
    {
        var root = new GameObject(mapName);
        if (parent != null) root.transform.SetParent(parent, false);

        if (bsp.Models == null || bsp.Models.Length == 0)
        {
            Debug.LogWarning($"[RuntimeResourceLoader] BSP '{mapName}' has no models.");
            return root;
        }

        // Build lightmaps from raw BSP data
        var lightmaps = BuildLightmaps(bsp, mapName);

        BspModel worldModel = bsp.Models[0];
        for (int si = 0; si < worldModel.NumSurfaces; si++)
        {
            int surfIdx = worldModel.FirstSurface + si;
            if (surfIdx < 0 || surfIdx >= bsp.Surfaces.Length) continue;

            BspSurface surf = bsp.Surfaces[surfIdx];

            if (surf.SurfaceType != SurfaceType.MST_PLANAR &&
                surf.SurfaceType != SurfaceType.MST_PATCH  &&
                surf.SurfaceType != SurfaceType.MST_TRIANGLE_SOUP)
                continue;

            // Skip non-visual surfaces (SURF_NODRAW = 0x0080)
            int bspShaderFlags = (surf.ShaderNum >= 0 && surf.ShaderNum < bsp.Shaders.Length)
                ? bsp.Shaders[surf.ShaderNum].SurfaceFlags : 0;
            if ((bspShaderFlags & 0x0080) != 0) continue;

            BspVertex[] surfVerts;
            int[]       surfIndices;

            if (surf.SurfaceType == SurfaceType.MST_PATCH)
            {
                if (surf.PatchWidth < 3 || surf.PatchHeight < 3 ||
                    surf.NumVerts < surf.PatchWidth * surf.PatchHeight)
                    continue;

                var cpGrid = new BspVertex[surf.PatchWidth * surf.PatchHeight];
                for (int k = 0; k < cpGrid.Length; k++)
                    cpGrid[k] = bsp.Vertices[surf.FirstVert + k];

                BspData.TesselatePatch(cpGrid, surf.PatchWidth, surf.PatchHeight, 3,
                    out surfVerts, out surfIndices);
            }
            else
            {
                surfVerts = new BspVertex[surf.NumVerts];
                for (int k = 0; k < surf.NumVerts; k++)
                    surfVerts[k] = bsp.Vertices[surf.FirstVert + k];

                surfIndices = new int[surf.NumIndexes];
                for (int k = 0; k < surf.NumIndexes; k++)
                    surfIndices[k] = bsp.Indices[surf.FirstIndex + k];
            }

            if (surfVerts.Length == 0 || surfIndices.Length == 0) continue;

            var mesh = BuildBspMesh(surfVerts, surfIndices, $"{mapName}_surf{surfIdx}");

            var surfGo = new GameObject($"surf_{surfIdx}");
            surfGo.transform.SetParent(root.transform, false);
            surfGo.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = surfGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetOrBuildBspMaterial(bsp, surf, lightmaps);
        }

        // Add a MeshCollider for physics on the world mesh (combined)
        AddPhysicsCollider(root, bsp, worldModel);

        Debug.Log($"[RuntimeResourceLoader] BSP '{mapName}' loaded ({worldModel.NumSurfaces} surfaces).");
        return root;
    }

    // =========================================================================
    // Internal: build lightmaps from raw BSP data
    // =========================================================================
    private const int LIGHTMAP_W = 128;
    private const int LIGHTMAP_H = 128;

    private static Texture2D[] BuildLightmaps(BspData bsp, string mapName)
    {
        if (bsp.LightmapCount == 0) return Array.Empty<Texture2D>();

        int bytesPerMap = LIGHTMAP_W * LIGHTMAP_H * 3;
        var textures = new Texture2D[bsp.LightmapCount];

        for (int li = 0; li < bsp.LightmapCount; li++)
        {
            var tex = new Texture2D(LIGHTMAP_W, LIGHTMAP_H, TextureFormat.RGB24, mipChain: false)
            {
                name = $"{mapName}_lightmap{li}",
            };

            // ET lightmaps: top-to-bottom; Unity: bottom-to-top → flip rows
            int srcBase   = li * bytesPerMap;
            var flipped   = new byte[bytesPerMap];
            for (int row = 0; row < LIGHTMAP_H; row++)
            {
                int srcRow = (LIGHTMAP_H - 1 - row) * LIGHTMAP_W * 3;
                int dstRow = row * LIGHTMAP_W * 3;
                Array.Copy(bsp.LightmapData, srcBase + srcRow, flipped, dstRow, LIGHTMAP_W * 3);
            }
            tex.LoadRawTextureData(flipped);
            tex.Apply(updateMipmaps: false);
            textures[li] = tex;
        }
        return textures;
    }

    // =========================================================================
    // Internal: get or build Material for a BSP surface
    // =========================================================================
    private static Material GetOrBuildBspMaterial(
        BspData bsp, BspSurface surf, Texture2D[] lightmaps)
    {
        string shaderName = (surf.ShaderNum >= 0 && surf.ShaderNum < bsp.Shaders.Length)
            ? bsp.Shaders[surf.ShaderNum].Name
            : "unknown";

        if (_matCache.TryGetValue(shaderName, out var cached)) return cached;

        // Parse the ET .shader definition from etmain if available
        var etShader = ShaderParser.FindShaderRuntime(shaderName);
        Material mat;

        if (etShader != null)
        {
            mat = etShader.ToMaterialRuntime();
        }
        else
        {
            // Fallback: Standard/Lit material with a texture matching the shader path
            var unityShader = FindUnityShader(false);
            mat = new Material(unityShader) { name = shaderName };
            var tex = LoadTexture(shaderName);
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }
        }

        // Apply lightmap to UV2 if the surface references one
        if (surf.LightmapNum >= 0 && surf.LightmapNum < lightmaps.Length)
            mat.SetTexture("_LightmapTex", lightmaps[surf.LightmapNum]);

        _matCache[shaderName] = mat;
        return mat;
    }

    // =========================================================================
    // Internal: build BSP mesh
    // =========================================================================
    private static Mesh BuildBspMesh(BspVertex[] verts, int[] indices, string name)
    {
        var positions = new Vector3[verts.Length];
        var normals   = new Vector3[verts.Length];
        var uvs       = new Vector2[verts.Length];
        var uvs2      = new Vector2[verts.Length];
        var colors    = new Color32[verts.Length];

        for (int i = 0; i < verts.Length; i++)
        {
            positions[i] = verts[i].Position;
            normals[i]   = verts[i].Normal;
            uvs[i]       = verts[i].UV;
            uvs2[i]      = verts[i].LightmapUV;
            colors[i]    = verts[i].Color;
        }

        var mesh = new Mesh
        {
            name        = name,
            indexFormat = verts.Length > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16,
        };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs2);
        mesh.SetColors(colors);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // =========================================================================
    // Internal: add a combined physics collider to the world geometry
    // =========================================================================
    private static void AddPhysicsCollider(GameObject root, BspData bsp, BspModel worldModel)
    {
        // Combine all solid surfaces into one large Mesh for PhysX
        var combineInstances = new List<CombineInstance>();
        for (int si = 0; si < worldModel.NumSurfaces; si++)
        {
            int idx = worldModel.FirstSurface + si;
            if (idx < 0 || idx >= bsp.Surfaces.Length) continue;
            var surf = bsp.Surfaces[idx];
            if (surf.SurfaceType != SurfaceType.MST_PLANAR &&
                surf.SurfaceType != SurfaceType.MST_TRIANGLE_SOUP) continue;

            var verts = new BspVertex[surf.NumVerts];
            for (int k = 0; k < surf.NumVerts; k++)
                verts[k] = bsp.Vertices[surf.FirstVert + k];
            var inds = new int[surf.NumIndexes];
            for (int k = 0; k < surf.NumIndexes; k++)
                inds[k] = bsp.Indices[surf.FirstIndex + k];

            var m = BuildBspMesh(verts, inds, "col");
            combineInstances.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
        }

        if (combineInstances.Count == 0) return;

        var combined = new Mesh { name = "worldCollider" };
        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combined.CombineMeshes(combineInstances.ToArray(), mergeSubMeshes: true, useMatrices: false);
        combined.RecalculateBounds();

        int trisBefore = combined.triangles.Length / 3;
        Debug.Log($"[RuntimeResourceLoader] Collision mesh before subdivision: " +
                  $"{combined.vertexCount} verts, {trisBefore} tris, " +
                  $"bounds={combined.bounds}");

        // PhysX warns when any triangle edge exceeds 500 units.
        // Subdivide long edges so every triangle stays within the limit.
        combined = SubdivideLargeTriangles(combined, 490f);

        int trisAfter = combined.triangles.Length / 3;
        Debug.Log($"[RuntimeResourceLoader] Collision mesh after subdivision: " +
                  $"{combined.vertexCount} verts, {trisAfter} tris");

        if (trisAfter > 500000)
            Debug.LogWarning($"[RuntimeResourceLoader] Collision mesh has {trisAfter} triangles — " +
                             "exceeds PhysX recommended limit (500k). MeshCollider may fail silently.");

        var col = root.AddComponent<MeshCollider>();
        col.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning
                           | MeshColliderCookingOptions.WeldColocatedVertices
                           | MeshColliderCookingOptions.UseFastMidphase;
        col.sharedMesh = combined;
        Debug.Log($"[RuntimeResourceLoader] MeshCollider assigned, sharedMesh={col.sharedMesh != null}, " +
                  $"triangles={col.sharedMesh?.triangles.Length / 3 ?? 0}");
    }

    // =========================================================================
    // Internal: subdivide triangles whose longest edge exceeds maxEdge
    // Splits at the longest edge midpoint, repeating until all edges are in range.
    // =========================================================================
    private static Mesh SubdivideLargeTriangles(Mesh src, float maxEdge)
    {
        var verts = new List<Vector3>(src.vertices);
        var tris  = new List<int>(src.triangles);

        for (int pass = 0; pass < 12; pass++)
        {
            bool any = false;
            var next = new List<int>(tris.Count);

            for (int i = 0; i < tris.Count; i += 3)
            {
                int i0 = tris[i], i1 = tris[i + 1], i2 = tris[i + 2];
                Vector3 v0 = verts[i0], v1 = verts[i1], v2 = verts[i2];

                float d01 = Vector3.Distance(v0, v1);
                float d12 = Vector3.Distance(v1, v2);
                float d20 = Vector3.Distance(v2, v0);
                float longest = Mathf.Max(d01, d12, d20);

                if (longest <= maxEdge)
                {
                    next.Add(i0); next.Add(i1); next.Add(i2);
                    continue;
                }

                any = true;
                int mid = verts.Count;

                if (d01 >= d12 && d01 >= d20)
                {
                    verts.Add((v0 + v1) * 0.5f);
                    next.Add(i0); next.Add(mid); next.Add(i2);
                    next.Add(mid); next.Add(i1); next.Add(i2);
                }
                else if (d12 >= d01 && d12 >= d20)
                {
                    verts.Add((v1 + v2) * 0.5f);
                    next.Add(i0); next.Add(i1); next.Add(mid);
                    next.Add(i0); next.Add(mid); next.Add(i2);
                }
                else
                {
                    verts.Add((v2 + v0) * 0.5f);
                    next.Add(i0); next.Add(i1); next.Add(mid);
                    next.Add(mid); next.Add(i1); next.Add(i2);
                }
            }

            tris = next;
            if (!any) break;
        }

        var result = new Mesh { name = src.name };
        result.indexFormat = verts.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        result.SetVertices(verts);
        result.SetTriangles(tris, 0);
        result.RecalculateBounds();
        return result;
    }

    // =========================================================================
    // Internal: build MD3 runtime GameObject
    // =========================================================================
    private static GameObject BuildMd3Object(Md3Data md3, string name, Transform parent)
    {
        var root = new GameObject(name);
        if (parent != null) root.transform.SetParent(parent, false);

        foreach (var surf in md3.Surfaces)
        {
            var mesh = new Mesh { name = surf.Name };
            mesh.vertices  = surf.Positions;
            mesh.normals   = surf.Normals;
            mesh.uv        = surf.UVs;
            mesh.triangles = FlipWinding(surf.Indexes);

            for (int f = 1; f < md3.NumFrames; f++)
            {
                var deltaPos = new Vector3[surf.Positions.Length];
                var deltaNrm = new Vector3[surf.Normals.Length];
                for (int v = 0; v < surf.Positions.Length; v++)
                {
                    deltaPos[v] = surf.FramePositions[f][v] - surf.Positions[v];
                    deltaNrm[v] = surf.FrameNormals[f][v]  - surf.Normals[v];
                }
                mesh.AddBlendShapeFrame($"frame_{f}", 100f, deltaPos, deltaNrm, null);
            }
            mesh.RecalculateBounds();

            var child = new GameObject(surf.Name);
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;

            string shaderName = (surf.ShaderNames != null && surf.ShaderNames.Length > 0)
                ? surf.ShaderNames[0] : surf.Name;
            var mat = GetOrBuildGenericMaterial(shaderName);
            child.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // Tags → empty child transforms
        foreach (var tag in md3.Tags)
        {
            var tagGo = new GameObject(tag.Name);
            tagGo.transform.SetParent(root.transform, false);
            tagGo.transform.localPosition = tag.Origin;
            var rm = new Matrix4x4();
            rm.SetColumn(0, new Vector4(tag.AxisX.x, tag.AxisX.y, tag.AxisX.z, 0f));
            rm.SetColumn(1, new Vector4(tag.AxisY.x, tag.AxisY.y, tag.AxisY.z, 0f));
            rm.SetColumn(2, new Vector4(tag.AxisZ.x, tag.AxisZ.y, tag.AxisZ.z, 0f));
            rm.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            tagGo.transform.localRotation = rm.rotation;
        }

        return root;
    }

    // =========================================================================
    // Internal: build MDS runtime GameObject (skeletal)
    // =========================================================================
    private static GameObject BuildMdsObject(MdsData mds, string name, Transform parent)
    {
        var root = new GameObject(name);
        if (parent != null) root.transform.SetParent(parent, false);

        // Build bone transforms
        var boneTransforms = new Transform[mds.NumBones];
        for (int b = 0; b < mds.NumBones; b++)
        {
            var boneGo = new GameObject(mds.Bones[b].Name);
            boneTransforms[b] = boneGo.transform;
        }
        // Parent bones according to MDS hierarchy
        for (int b = 0; b < mds.NumBones; b++)
        {
            int parentIdx = mds.Bones[b].Parent;
            if (parentIdx >= 0 && parentIdx < mds.NumBones)
                boneTransforms[b].SetParent(boneTransforms[parentIdx], false);
            else
                boneTransforms[b].SetParent(root.transform, false);
        }

        foreach (var surf in mds.Surfaces)
        {
            var mesh = new Mesh { name = surf.Name };

            int numVerts = surf.Vertices.Length;
            var positions  = new Vector3[numVerts];
            var normals    = new Vector3[numVerts];
            var uvs        = new Vector2[numVerts];
            var boneW      = new BoneWeight[numVerts];

            for (int v = 0; v < numVerts; v++)
            {
                var mv  = surf.Vertices[v];
                normals[v] = mv.Normal;
                uvs[v]     = mv.UV;

                // Bind-pose position: weighted sum of per-weight bone-local offsets
                var pos = Vector3.zero;
                if (mv.Weights != null)
                    foreach (var w in mv.Weights)
                        pos += w.BoneWeight * w.Offset;
                positions[v] = pos;

                // Remap surface-local bone indices → global bone indices (up to 2 influences)
                var ws = mv.Weights;
                int  b0 = 0, b1 = 0;
                float wt0 = 1f, wt1 = 0f;
                if (ws != null && ws.Length > 0)
                {
                    b0  = (ws[0].BoneIndex < surf.BoneReferences.Length)
                          ? surf.BoneReferences[ws[0].BoneIndex] : 0;
                    wt0 = ws[0].BoneWeight;
                }
                if (ws != null && ws.Length > 1)
                {
                    b1  = (ws[1].BoneIndex < surf.BoneReferences.Length)
                          ? surf.BoneReferences[ws[1].BoneIndex] : 0;
                    wt1 = ws[1].BoneWeight;
                }
                float wsum = wt0 + wt1;
                if (wsum > 0f) { wt0 /= wsum; wt1 /= wsum; }
                boneW[v] = new BoneWeight
                {
                    boneIndex0 = b0, weight0 = wt0,
                    boneIndex1 = b1, weight1 = wt1,
                };
            }

            mesh.vertices    = positions;
            mesh.normals     = normals;
            mesh.uv          = uvs;
            mesh.boneWeights = boneW;

            // Flip triangle winding (ET CCW → Unity CW)
            int[] inds = new int[surf.Indices.Length];
            for (int i = 0; i < surf.Indices.Length; i += 3)
            {
                inds[i]   = surf.Indices[i];
                inds[i+1] = surf.Indices[i+2];
                inds[i+2] = surf.Indices[i+1];
            }
            mesh.triangles = inds;

            // Bind poses
            var bindPoses = new Matrix4x4[mds.NumBones];
            for (int b = 0; b < mds.NumBones; b++)
                bindPoses[b] = boneTransforms[b].worldToLocalMatrix * root.transform.localToWorldMatrix;
            mesh.bindposes = bindPoses;
            mesh.RecalculateBounds();

            var child = new GameObject(surf.Name);
            child.transform.SetParent(root.transform, false);

            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones      = boneTransforms;
            smr.rootBone   = boneTransforms.Length > 0 ? boneTransforms[0] : root.transform;
            smr.sharedMaterial = GetOrBuildGenericMaterial(surf.ShaderName);
        }

        return root;
    }

    // =========================================================================
    // Internal: get or build a generic material for a model surface
    // =========================================================================
    private static Material GetOrBuildGenericMaterial(string shaderName)
    {
        if (string.IsNullOrEmpty(shaderName)) shaderName = "default";
        if (_matCache.TryGetValue(shaderName, out var m)) return m;

        var etShader = ShaderParser.FindShaderRuntime(shaderName);
        Material mat = etShader != null
            ? etShader.ToMaterialRuntime()
            : new Material(FindUnityShader(false)) { name = shaderName };

        if (mat.mainTexture == null)
        {
            var tex = LoadTexture(shaderName);
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }
        }

        _matCache[shaderName] = mat;
        return mat;
    }

    // =========================================================================
    // Internal: load texture from raw bytes (PNG/JPG via LoadImage; TGA manual)
    // =========================================================================
    public static Texture2D LoadTextureFromBytes(byte[] data, string hint = "")
    {
        if (data == null || data.Length == 0) return null;

        string ext = Path.GetExtension(hint).ToLowerInvariant();

        // PNG and JPG: Unity's LoadImage handles these natively at runtime
        if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == "")
        {
            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(data))
                return tex;
            // fall through and try TGA decoder
            UnityEngine.Object.Destroy(tex);
        }

        // TGA: manual decode (supports 24/32-bit uncompressed and RLE)
        if (ext == ".tga" || ext == "" || ext == ".png" || ext == ".jpg")
        {
            var tex = DecodeTga(data);
            if (tex != null) return tex;
        }

        return null;
    }

    // =========================================================================
    // Internal: TGA decoder (Type 2 = true-color uncompressed, Type 10 = RLE)
    // =========================================================================
    private static Texture2D DecodeTga(byte[] data)
    {
        if (data == null || data.Length < 18) return null;

        int idLength    = data[0];
        int colorMapType= data[1];
        int imageType   = data[2];
        // Skip color map fields (bytes 3-7) — we only support true-color
        int xOrigin     = data[8]  | (data[9]  << 8);
        int yOrigin     = data[10] | (data[11] << 8);
        int width       = data[12] | (data[13] << 8);
        int height      = data[14] | (data[15] << 8);
        int bpp         = data[16];
        int descriptor  = data[17];

        if (width <= 0 || height <= 0) return null;
        if (bpp != 24 && bpp != 32) return null;
        if (imageType != 2 && imageType != 10) return null; // only true-color & RLE

        bool hasAlpha  = (bpp == 32);
        bool flipY     = (descriptor & 0x20) == 0; // bit 5 = top-left origin
        int  pixelSize = bpp / 8;

        int offset = 18 + idLength + (colorMapType != 0 ? (data[5] | (data[6] << 8)) * ((data[7] + 7) / 8) : 0);

        var pixels = new Color32[width * height];

        if (imageType == 2) // Uncompressed
        {
            for (int i = 0; i < width * height; i++)
            {
                int o = offset + i * pixelSize;
                if (o + pixelSize > data.Length) break;
                byte b2 = data[o]; byte g2 = data[o+1]; byte r2 = data[o+2];
                byte a2 = hasAlpha ? data[o+3] : (byte)255;
                pixels[i] = new Color32(r2, g2, b2, a2);
            }
        }
        else // RLE (Type 10)
        {
            int pixIdx = 0;
            int pos    = offset;
            while (pixIdx < width * height && pos < data.Length)
            {
                int rep = data[pos++];
                if ((rep & 0x80) != 0) // run-length packet
                {
                    int count = (rep & 0x7F) + 1;
                    if (pos + pixelSize > data.Length) break;
                    byte b2 = data[pos]; byte g2 = data[pos+1]; byte r2 = data[pos+2];
                    byte a2 = hasAlpha ? data[pos+3] : (byte)255;
                    pos += pixelSize;
                    for (int k = 0; k < count && pixIdx < width * height; k++)
                        pixels[pixIdx++] = new Color32(r2, g2, b2, a2);
                }
                else // raw packet
                {
                    int count = (rep & 0x7F) + 1;
                    for (int k = 0; k < count && pixIdx < width * height; k++)
                    {
                        if (pos + pixelSize > data.Length) break;
                        byte b2 = data[pos]; byte g2 = data[pos+1]; byte r2 = data[pos+2];
                        byte a2 = hasAlpha ? data[pos+3] : (byte)255;
                        pos += pixelSize;
                        pixels[pixIdx++] = new Color32(r2, g2, b2, a2);
                    }
                }
            }
        }

        // TGA is stored bottom-to-top by default; flip if needed
        if (flipY)
        {
            for (int y = 0; y < height / 2; y++)
            {
                int top = y * width;
                int bot = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    (pixels[top + x], pixels[bot + x]) = (pixels[bot + x], pixels[top + x]);
                }
            }
        }

        var tex = new Texture2D(width, height,
            hasAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, mipChain: false);
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false);
        return tex;
    }

    // =========================================================================
    // Internal: WAV decoder → AudioClip
    // Supports PCM 8/16-bit, mono/stereo (RIFF/WAVE format)
    // =========================================================================
    private static AudioClip DecodeWav(byte[] data, string clipName)
    {
        if (data == null || data.Length < 44) return null;

        try
        {
            using var ms = new MemoryStream(data);
            using var r  = new BinaryReader(ms);

            // RIFF header
            string riff = Encoding.ASCII.GetString(r.ReadBytes(4));
            if (riff != "RIFF") return null;
            r.ReadInt32(); // chunk size
            string wave = Encoding.ASCII.GetString(r.ReadBytes(4));
            if (wave != "WAVE") return null;

            int    numChannels = 1;
            int    sampleRate  = 22050;
            int    bitsPerSample = 16;
            byte[] pcmData     = null;

            while (ms.Position < ms.Length - 8)
            {
                string chunkId   = Encoding.ASCII.GetString(r.ReadBytes(4));
                int    chunkSize = r.ReadInt32();
                long   chunkEnd  = ms.Position + chunkSize;

                if (chunkId == "fmt ")
                {
                    r.ReadInt16(); // audioFormat (1 = PCM)
                    numChannels   = r.ReadInt16();
                    sampleRate    = r.ReadInt32();
                    r.ReadInt32(); // byteRate
                    r.ReadInt16(); // blockAlign
                    bitsPerSample = r.ReadInt16();
                }
                else if (chunkId == "data")
                {
                    pcmData = r.ReadBytes(chunkSize);
                }
                else
                {
                    ms.Position = chunkEnd;
                    continue;
                }

                ms.Position = chunkEnd;
            }

            if (pcmData == null || pcmData.Length == 0) return null;

            int bytesPerSample = bitsPerSample / 8;
            int totalSamples   = pcmData.Length / bytesPerSample;
            var floatSamples   = new float[totalSamples];

            if (bitsPerSample == 16)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    short s = (short)(pcmData[i*2] | (pcmData[i*2+1] << 8));
                    floatSamples[i] = s / 32768f;
                }
            }
            else if (bitsPerSample == 8)
            {
                for (int i = 0; i < totalSamples; i++)
                    floatSamples[i] = (pcmData[i] - 128) / 128f;
            }
            else return null;

            int samplesPerChannel = totalSamples / numChannels;
            var clip = AudioClip.Create(clipName, samplesPerChannel, numChannels, sampleRate,
                stream: false);
            clip.SetData(floatSamples, 0);
            return clip;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[RuntimeResourceLoader] WAV decode failed: {ex.Message}");
            return null;
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================
    private static int[] FlipWinding(int[] src)
    {
        var dst = new int[src.Length];
        for (int i = 0; i < src.Length; i += 3)
        {
            dst[i]   = src[i];
            dst[i+1] = src[i+2];
            dst[i+2] = src[i+1];
        }
        return dst;
    }

    private static Shader FindUnityShader(bool transparent)
    {
        // Try URP first, fall back to Standard (Built-in RP)
        var s = Shader.Find(transparent ? TRANSPARENT_SHADER : OPAQUE_SHADER);
        return s != null ? s : Shader.Find(FALLBACK_SHADER);
    }

    // Cache clearing (call on scene unload)
    public static void ClearCaches()
    {
        _texCache.Clear();
        _matCache.Clear();
    }
}
