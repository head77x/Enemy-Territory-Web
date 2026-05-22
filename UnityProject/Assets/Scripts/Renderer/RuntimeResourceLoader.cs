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

/// <summary>
/// Stores per-frame MDC tag transforms for tag-only animated viewmodels (numSurfs=0).
/// DriveViewmodelAnimation reads this to reposition attached sub-models each frame.
/// Ported from CG_PositionRotatedEntityOnTag (cg_weapons.c).
/// </summary>
public class MdcTagAnimation : MonoBehaviour
{
    public int           NumFrames;
    public string[]      TagNames;
    public Transform[]   TagTransforms;   // live child transforms (one per tag)
    public Vector3[][]   TagPositions;    // [frame][tagIndex] local position
    public Quaternion[][] TagRotations;   // [frame][tagIndex] local rotation
}

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
        // Mirror ET RE_RegisterModel: try .mdc first (changes last char '3' → 'c').
        // If caller already supplied a .mdc path, use it directly as the MDC probe path.
        string mdcPath = virtualPath.EndsWith(".md3", StringComparison.OrdinalIgnoreCase)
            ? virtualPath.Substring(0, virtualPath.Length - 1) + "c"
            : virtualPath.EndsWith(".mdc", StringComparison.OrdinalIgnoreCase)
            ? virtualPath
            : null;

        byte[] data = null;
        bool isMdc = false;
        if (mdcPath != null)
        {
            data = FileSystem.FS_ReadFile(mdcPath);
            isMdc = (data != null);
            Debug.Log($"[LoadMd3] MDC probe '{mdcPath}': {(isMdc ? $"FOUND {data.Length} bytes" : "not found")}");
        }
        if (data == null)
        {
            data = FileSystem.FS_ReadFile(virtualPath);
            if (data != null)
                Debug.Log($"[LoadMd3] MD3 fallback '{virtualPath}': FOUND {data.Length} bytes");
        }

        if (data == null)
        {
            // Try v_ prefix for viewmodel variant (e.g. models/weapons2/mp40/v_mp40.md3)
            string dir      = Path.GetDirectoryName(virtualPath)?.Replace('\\', '/') ?? "";
            string fileName = Path.GetFileName(virtualPath);
            string viewPath = string.IsNullOrEmpty(dir)
                ? "v_" + fileName
                : dir + "/v_" + fileName;

            // Try v_ MDC variant first, then v_ MD3
            string viewMdcPath = viewPath.EndsWith(".md3", StringComparison.OrdinalIgnoreCase)
                ? viewPath.Substring(0, viewPath.Length - 1) + "c"
                : null;
            if (viewMdcPath != null)
            {
                data = FileSystem.FS_ReadFile(viewMdcPath);
                if (data != null) { virtualPath = viewPath; mdcPath = viewMdcPath; isMdc = true; }
            }
            if (data == null)
            {
                data = FileSystem.FS_ReadFile(viewPath);
                if (data != null) { virtualPath = viewPath; isMdc = false; }
            }

            if (data == null)
            {
                // Log what IS available in the same directory to help diagnose path issues
                var avail = FileSystem.FS_GetFileList(dir, ".md3");
                var availMdc = FileSystem.FS_GetFileList(dir, ".mdc");
                Debug.LogWarning($"[RuntimeResourceLoader] MD3/MDC not found: {virtualPath}" +
                    (avail.Length > 0 || availMdc.Length > 0
                        ? $". Available in '{dir}': md3=[{string.Join(", ", avail)}] mdc=[{string.Join(", ", availMdc)}]"
                        : $". No .md3/.mdc files found in '{dir}'"));
                // Also log top-level weapons dirs
                var weapDirs = FileSystem.FS_GetFileList("models/weapons2", ".md3");
                if (weapDirs.Length > 0)
                    Debug.Log($"[RuntimeResourceLoader] models/weapons2 .md3 files: {string.Join(", ", weapDirs)}");
                return null;
            }
        }

        Md3Data md3;
        if (isMdc)
        {
            try { md3 = MdcLoader.Load(data); }
            catch (Exception ex)
            {
                Debug.LogError($"[RuntimeResourceLoader] MDC parse error '{mdcPath}': {ex.Message}");
                return null;
            }
        }
        else
        {
            try { md3 = Md3Data.Load(data); }
            catch (Exception ex)
            {
                Debug.LogError($"[RuntimeResourceLoader] MD3 parse error '{virtualPath}': {ex.Message}");
                return null;
            }
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

            BspVertex[] surfVerts;
            int[]       surfInds;

            if (surf.SurfaceType == SurfaceType.MST_PLANAR ||
                surf.SurfaceType == SurfaceType.MST_TRIANGLE_SOUP)
            {
                surfVerts = new BspVertex[surf.NumVerts];
                for (int k = 0; k < surf.NumVerts; k++)
                    surfVerts[k] = bsp.Vertices[surf.FirstVert + k];
                surfInds = new int[surf.NumIndexes];
                for (int k = 0; k < surf.NumIndexes; k++)
                    surfInds[k] = bsp.Indices[surf.FirstIndex + k];
            }
            else if (surf.SurfaceType == SurfaceType.MST_PATCH)
            {
                if (surf.PatchWidth < 3 || surf.PatchHeight < 3 ||
                    surf.NumVerts < surf.PatchWidth * surf.PatchHeight)
                    continue;
                var cpGrid = new BspVertex[surf.PatchWidth * surf.PatchHeight];
                for (int k = 0; k < cpGrid.Length; k++)
                    cpGrid[k] = bsp.Vertices[surf.FirstVert + k];
                BspData.TesselatePatch(cpGrid, surf.PatchWidth, surf.PatchHeight, 3,
                    out surfVerts, out surfInds);
            }
            else continue;

            if (surfVerts == null || surfVerts.Length == 0 || surfInds == null || surfInds.Length == 0)
                continue;

            var m = BuildBspMesh(surfVerts, surfInds, "col");
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
            mesh.uv        = surf.UVs;
            // EtToUnity has det=-1 (reflection), which turns CCW ET faces into CW Unity faces.
            // CW in Unity = back-face by convention, but _Cull=Off renders them anyway.
            // RecalculateNormals on the unflipped (CW) winding gives normals pointing TOWARD the
            // camera (outward in ET's sense), which is correct for lighting.
            // Applying FlipWinding would invert those normals to point AWAY from the camera,
            // producing the inside-out/reversed-shadow appearance.
            mesh.triangles = surf.Indexes;
            mesh.RecalculateNormals();

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

            // Diagnostic: log frames + first recalculated normal
            var recalcNormals = mesh.normals;
            if (recalcNormals != null && recalcNormals.Length > 0)
            {
                var n0 = recalcNormals[0];
                var p0 = surf.Positions.Length > 0 ? surf.Positions[0] : Vector3.zero;
                Debug.Log($"[BuildMd3Object] '{name}' surf='{surf.Name}' frames={md3.NumFrames} " +
                    $"blendShapes={mesh.blendShapeCount} verts={recalcNormals.Length} pos[0]={p0:F1}");
            }

            var child = new GameObject(surf.Name);
            child.transform.SetParent(root.transform, false);

            string shaderName = (surf.ShaderNames != null && surf.ShaderNames.Length > 0)
                ? surf.ShaderNames[0] : surf.Name;
            // SkinnedMeshRenderer is required to drive blend-shape animation at runtime.
            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh     = mesh;
            smr.sharedMaterial = GetOrBuildGenericMaterial(shaderName);
        }

        // Tags → empty child transforms (frame 0 pose)
        var tagTransforms = new Transform[md3.Tags.Length];
        for (int ti = 0; ti < md3.Tags.Length; ti++)
        {
            var tag = md3.Tags[ti];
            var tagGo = new GameObject(tag.Name);
            tagGo.transform.SetParent(root.transform, false);
            tagGo.transform.localPosition = tag.Origin;
            var rm = new Matrix4x4();
            rm.SetColumn(0, new Vector4(tag.AxisX.x, tag.AxisX.y, tag.AxisX.z, 0f));
            rm.SetColumn(1, new Vector4(tag.AxisY.x, tag.AxisY.y, tag.AxisY.z, 0f));
            rm.SetColumn(2, new Vector4(tag.AxisZ.x, tag.AxisZ.y, tag.AxisZ.z, 0f));
            rm.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            tagGo.transform.localRotation = rm.rotation;
            tagTransforms[ti] = tagGo.transform;
        }

        // Per-frame tag animation for tag-only MDC models (e.g. v_thompson_hand.mdc, numSurfs=0).
        // Attach MdcTagAnimation so DriveViewmodelAnimation can interpolate tag transforms.
        if (md3.FrameTags != null && md3.FrameTags.Length > 1 && md3.Tags.Length > 0)
        {
            var tagAnim = root.AddComponent<MdcTagAnimation>();
            tagAnim.NumFrames     = md3.FrameTags.Length;
            tagAnim.TagNames      = System.Array.ConvertAll(md3.Tags, t => t.Name);
            tagAnim.TagTransforms = tagTransforms;
            tagAnim.TagPositions  = new Vector3[md3.FrameTags.Length][];
            tagAnim.TagRotations  = new Quaternion[md3.FrameTags.Length][];

            for (int f = 0; f < md3.FrameTags.Length; f++)
            {
                tagAnim.TagPositions[f] = new Vector3[md3.Tags.Length];
                tagAnim.TagRotations[f] = new Quaternion[md3.Tags.Length];
                for (int ti = 0; ti < md3.Tags.Length; ti++)
                {
                    var ft = md3.FrameTags[f][ti];
                    tagAnim.TagPositions[f][ti] = ft.Origin;
                    var rm = new Matrix4x4();
                    rm.SetColumn(0, new Vector4(ft.AxisX.x, ft.AxisX.y, ft.AxisX.z, 0f));
                    rm.SetColumn(1, new Vector4(ft.AxisY.x, ft.AxisY.y, ft.AxisY.z, 0f));
                    rm.SetColumn(2, new Vector4(ft.AxisZ.x, ft.AxisZ.y, ft.AxisZ.z, 0f));
                    rm.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
                    tagAnim.TagRotations[f][ti] = rm.rotation;
                }
            }
            Debug.Log($"[BuildMd3Object] '{name}': MdcTagAnimation frames={tagAnim.NumFrames} tags=[{string.Join(",", tagAnim.TagNames)}]");
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

        // ET shader defs store names WITHOUT extension (e.g. "models/weapons2/fg42/fg42_2").
        // MD3 surfaces store shader names WITH extension (e.g. "...fg42_2.tga").
        // Strip extension before the shader lookup so they match.
        string shaderKey = shaderName;
        string ext = Path.GetExtension(shaderName);
        if (!string.IsNullOrEmpty(ext))
        {
            string dir2 = Path.GetDirectoryName(shaderName)?.Replace('\\', '/') ?? "";
            string stem  = Path.GetFileNameWithoutExtension(shaderName);
            shaderKey = string.IsNullOrEmpty(dir2) ? stem : dir2 + "/" + stem;
        }

        var etShader = ShaderParser.FindShaderRuntime(shaderKey);
        if (etShader == null && shaderKey != shaderName)
            etShader = ShaderParser.FindShaderRuntime(shaderName);   // fallback: try with extension

        // Build a plain opaque material — ET shader definitions for model surfaces describe
        // multi-pass envmap/glow effects (not useful in Unity) and their FindFirstTextureMap()
        // often returns the envmap texture rather than the diffuse. Load the diffuse directly
        // from the shader key (i.e. the MD3 surface name, which IS the texture path).
        var opaqueSh = UnityEngine.Shader.Find(OPAQUE_SHADER)
                    ?? UnityEngine.Shader.Find(FALLBACK_SHADER);
        Material mat = new Material(opaqueSh) { name = shaderName };

        // Disable back-face culling: ET→Unity coordinate conversion can invert winding
        // on model surfaces, causing faces to be culled when viewed from the correct side.
        // ET shaders also rarely set NoCull/TwoSided for model surfaces.
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        // Find the diffuse texture. Strategy:
        // 1. Check ET shader stages for a map in the same directory as the shader — this is the
        //    model's own diffuse texture (e.g. fg42_yd.tga when shader key is fg42_2).
        //    envmap/effects textures are in a different directory (textures/effects/...) so they
        //    are naturally skipped by the directory-match filter.
        // 2. Try loading shaderKey directly (handles cases where shader name == texture name).
        // 3. Fall back to shaderName (with extension) to catch cached-extension paths.
        string shaderDir = Path.GetDirectoryName(shaderKey)?.Replace('\\', '/') ?? "";
        string diffuseMap = null;

        if (etShader != null)
        {
            foreach (var stage in etShader.Stages)
            {
                if (stage.IsLightmap || string.IsNullOrEmpty(stage.Map)) continue;
                if (stage.Map.StartsWith("$") || stage.Map.StartsWith("*")) continue;
                string stageDir = Path.GetDirectoryName(stage.Map)?.Replace('\\', '/') ?? "";
                if (string.Equals(stageDir, shaderDir, StringComparison.OrdinalIgnoreCase))
                {
                    diffuseMap = Path.GetFileNameWithoutExtension(stage.Map);
                    if (!string.IsNullOrEmpty(stageDir))
                        diffuseMap = stageDir + "/" + diffuseMap;
                    break;
                }
            }
        }

        Texture2D tex = null;
        if (!string.IsNullOrEmpty(diffuseMap))
            tex = LoadTexture(diffuseMap);
        if (tex == null)
            tex = LoadTexture(shaderKey);
        if (tex == null && shaderKey != shaderName)
            tex = LoadTexture(shaderName);

        if (tex == null)
        {
            string texDir = Path.GetDirectoryName(shaderKey)?.Replace('\\', '/') ?? "";
            var available = ET.Core.FileSystem.FS_GetFileList(texDir, ".tga");
            var availJpg  = ET.Core.FileSystem.FS_GetFileList(texDir, ".jpg");
            Debug.LogWarning($"[RuntimeResourceLoader] Texture not found for shader '{shaderName}'. " +
                $"diffuseMap='{diffuseMap ?? "null"}' " +
                $"Dir '{texDir}' has .tga: [{string.Join(", ", available)}] .jpg: [{string.Join(", ", availJpg)}]");
        }
        else
        {
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        }

        // Disable PBR specular/reflections — model surfaces should render as flat diffuse.
        // URP Lit defaults to _Smoothness=0.5 which causes prominent environment map reflections.
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0f);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
        if (mat.HasProperty("_SpecColor"))  mat.SetColor("_SpecColor", Color.black);

        // Diagnostic: confirm final material state before caching
        Texture finalTex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : mat.mainTexture;
        Debug.Log($"[RuntimeResourceLoader] GenericMat '{shaderName}': " +
            $"stages={etShader?.Stages.Count.ToString() ?? "no-etshader"} " +
            $"rq={mat.renderQueue} " +
            $"tex={(finalTex != null ? finalTex.name : "NULL")}");

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
