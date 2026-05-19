// Ported from: src/renderer/tr_bsp.c, src/qcommon/cm_load.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AssetImporters;
#endif

// ---------------------------------------------------------------------------
// Data structures
// ---------------------------------------------------------------------------

public struct BspShader
{
    public string Name;
    public int    SurfaceFlags;
    public int    ContentFlags;
}

public struct BspVertex
{
    public Vector3 Position;
    public Vector2 UV;
    public Vector2 LightmapUV;
    public Vector3 Normal;
    public Color32 Color;
}

public struct BspSurface
{
    public int ShaderNum;
    public int SurfaceType;
    public int FirstVert;
    public int NumVerts;
    public int FirstIndex;
    public int NumIndexes;
    public int PatchWidth;
    public int PatchHeight;
    public int LightmapNum;
}

public struct BspModel
{
    public Vector3 Mins;
    public Vector3 Maxs;
    public int     FirstSurface;
    public int     NumSurfaces;
}

// ---------------------------------------------------------------------------
// BSP surface type constants  (tr_public.h / tr_local.h)
// ---------------------------------------------------------------------------
public static class SurfaceType
{
    public const int MST_BAD           = 0;
    public const int MST_PLANAR        = 1;
    public const int MST_PATCH         = 2;
    public const int MST_TRIANGLE_SOUP = 3;
    public const int MST_FLARE         = 4;
}

// ---------------------------------------------------------------------------
// BspData — parser (works both in Editor and at runtime)
// ---------------------------------------------------------------------------
public class BspData
{
    // Lump indices defined by the Quake 3 / ET BSP format
    private const int LUMP_ENTITIES      = 0;
    private const int LUMP_SHADERS       = 1;
    private const int LUMP_PLANES        = 2;
    private const int LUMP_NODES         = 3;
    private const int LUMP_LEAFS         = 4;
    private const int LUMP_LEAFSURFACES  = 5;
    private const int LUMP_LEAFBRUSHES   = 6;
    private const int LUMP_MODELS        = 7;
    private const int LUMP_BRUSHES       = 8;
    private const int LUMP_BRUSHSIDES    = 9;
    private const int LUMP_DRAWVERTS     = 10;
    private const int LUMP_DRAWINDEXES   = 11;
    private const int LUMP_FOGS          = 12;
    private const int LUMP_SURFACES      = 13;
    private const int LUMP_LIGHTMAPS     = 14;
    private const int LUMP_LIGHTGRID     = 15;
    private const int LUMP_VISIBILITY    = 16;
    private const int HEADER_LUMPS       = 17;

    private const int BSP_IDENT_ET = 46;
    private const int BSP_IDENT_Q3 = 47;

    // Lump sizes (bytes per element)
    private const int SHADER_SIZE   = 72;  // 64 name + 4 surfFlags + 4 contentFlags
    private const int DRAWVERT_SIZE = 44;  // xyz(12)+st(8)+lmst(8)+normal(12)+color(4)
    private const int SURFACE_SIZE  = 104;
    private const int MODEL_SIZE    = 40;  // bounds(24)+firstSurf(4)+numSurf(4)+firstBrush(4)+numBrush(4)
    private const int LIGHTMAP_W    = 128;
    private const int LIGHTMAP_H    = 128;

    public string       EntityString;
    public BspShader[]  Shaders;
    public BspSurface[] Surfaces;
    public BspVertex[]  Vertices;
    public int[]        Indices;
    public BspModel[]   Models;
    public byte[]       LightmapData;   // raw bytes; each map = 128*128*3 bytes
    public int          LightmapCount;

    // ------------------------------------------------------------------
    // ET→Unity coordinate conversion
    //   ET:    X=forward  Y=left   Z=up   (right-hand Z-up)
    //   Unity: X=right    Y=up     Z=forward (left-hand Y-up)
    // ------------------------------------------------------------------
    private static Vector3 ETtoUnity(float x, float y, float z)
        => new Vector3(-y, z, x);

    private static Vector3 ETtoUnity(Vector3 v)
        => new Vector3(-v.y, v.z, v.x);

    // ------------------------------------------------------------------
    public static BspData LoadFromFile(string path)
    {
        return Load(File.ReadAllBytes(path));
    }

    public static BspData Load(byte[] data)
    {
        var bsp = new BspData();
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);

        // ---- Header ----
        var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
        if (magic != "IBSP")
            throw new Exception($"BSP: bad magic '{magic}' (expected 'IBSP')");

        int version = br.ReadInt32();
        if (version != BSP_IDENT_ET && version != BSP_IDENT_Q3)
            throw new Exception($"BSP: unsupported version {version} (expected 46 or 47)");

        // ---- Lump directory ----
        var lumpOffsets = new int[HEADER_LUMPS];
        var lumpLengths = new int[HEADER_LUMPS];
        for (int i = 0; i < HEADER_LUMPS; i++)
        {
            lumpOffsets[i] = br.ReadInt32();
            lumpLengths[i] = br.ReadInt32();
        }

        // ---- LUMP_ENTITIES ----
        bsp.EntityString = ReadString(data, lumpOffsets[LUMP_ENTITIES], lumpLengths[LUMP_ENTITIES]);

        // ---- LUMP_SHADERS ----
        {
            int count = lumpLengths[LUMP_SHADERS] / SHADER_SIZE;
            bsp.Shaders = new BspShader[count];
            ms.Position = lumpOffsets[LUMP_SHADERS];
            for (int i = 0; i < count; i++)
            {
                byte[] nameBytes = br.ReadBytes(64);
                int nameLen = Array.IndexOf(nameBytes, (byte)0);
                bsp.Shaders[i] = new BspShader
                {
                    Name         = Encoding.ASCII.GetString(nameBytes, 0, nameLen < 0 ? 64 : nameLen),
                    SurfaceFlags = br.ReadInt32(),
                    ContentFlags = br.ReadInt32(),
                };
            }
        }

        // ---- LUMP_DRAWVERTS ----
        {
            int count = lumpLengths[LUMP_DRAWVERTS] / DRAWVERT_SIZE;
            bsp.Vertices = new BspVertex[count];
            ms.Position = lumpOffsets[LUMP_DRAWVERTS];
            for (int i = 0; i < count; i++)
            {
                float ex = br.ReadSingle();
                float ey = br.ReadSingle();
                float ez = br.ReadSingle();
                float su = br.ReadSingle();
                float sv = br.ReadSingle();
                float lu = br.ReadSingle();
                float lv = br.ReadSingle();
                float nx = br.ReadSingle();
                float ny = br.ReadSingle();
                float nz = br.ReadSingle();
                byte cr = br.ReadByte();
                byte cg = br.ReadByte();
                byte cb = br.ReadByte();
                byte ca = br.ReadByte();

                bsp.Vertices[i] = new BspVertex
                {
                    Position   = ETtoUnity(ex, ey, ez),
                    UV         = new Vector2(su, 1f - sv),   // flip V for Unity
                    LightmapUV = new Vector2(lu, 1f - lv),
                    Normal     = ETtoUnity(nx, ny, nz),
                    Color      = new Color32(cr, cg, cb, ca),
                };
            }
        }

        // ---- LUMP_DRAWINDEXES ----
        {
            int count = lumpLengths[LUMP_DRAWINDEXES] / 4;
            bsp.Indices = new int[count];
            ms.Position = lumpOffsets[LUMP_DRAWINDEXES];
            for (int i = 0; i < count; i++)
                bsp.Indices[i] = br.ReadInt32();
        }

        // ---- LUMP_SURFACES ----
        {
            int count = lumpLengths[LUMP_SURFACES] / SURFACE_SIZE;
            bsp.Surfaces = new BspSurface[count];
            ms.Position = lumpOffsets[LUMP_SURFACES];
            for (int i = 0; i < count; i++)
            {
                int shaderNum    = br.ReadInt32();
                int fogNum       = br.ReadInt32();
                int surfaceType  = br.ReadInt32();
                int firstVert    = br.ReadInt32();
                int numVerts     = br.ReadInt32();
                int firstIndex   = br.ReadInt32();
                int numIndexes   = br.ReadInt32();
                int lightmapNum  = br.ReadInt32();
                int lightmapX    = br.ReadInt32();
                int lightmapY    = br.ReadInt32();
                int lightmapW    = br.ReadInt32();
                int lightmapH    = br.ReadInt32();
                // lightmapOrigin (12 bytes) + lightmapVecs[3][3] (36 bytes) = 48 bytes — skip
                br.ReadBytes(48);
                int patchWidth   = br.ReadInt32();
                int patchHeight  = br.ReadInt32();

                bsp.Surfaces[i] = new BspSurface
                {
                    ShaderNum   = shaderNum,
                    SurfaceType = surfaceType,
                    FirstVert   = firstVert,
                    NumVerts    = numVerts,
                    FirstIndex  = firstIndex,
                    NumIndexes  = numIndexes,
                    PatchWidth  = patchWidth,
                    PatchHeight = patchHeight,
                    LightmapNum = lightmapNum,
                };
            }
        }

        // ---- LUMP_MODELS ----
        {
            int count = lumpLengths[LUMP_MODELS] / MODEL_SIZE;
            bsp.Models = new BspModel[count];
            ms.Position = lumpOffsets[LUMP_MODELS];
            for (int i = 0; i < count; i++)
            {
                float minX = br.ReadSingle(); float minY = br.ReadSingle(); float minZ = br.ReadSingle();
                float maxX = br.ReadSingle(); float maxY = br.ReadSingle(); float maxZ = br.ReadSingle();
                int firstSurface = br.ReadInt32();
                int numSurfaces  = br.ReadInt32();
                int firstBrush   = br.ReadInt32();
                int numBrushes   = br.ReadInt32();

                bsp.Models[i] = new BspModel
                {
                    Mins         = ETtoUnity(minX, minY, minZ),
                    Maxs         = ETtoUnity(maxX, maxY, maxZ),
                    FirstSurface = firstSurface,
                    NumSurfaces  = numSurfaces,
                };
            }
        }

        // ---- LUMP_LIGHTMAPS ----
        {
            int bytesPerMap = LIGHTMAP_W * LIGHTMAP_H * 3;
            bsp.LightmapCount = lumpLengths[LUMP_LIGHTMAPS] / bytesPerMap;
            bsp.LightmapData  = new byte[bsp.LightmapCount * bytesPerMap];
            ms.Position = lumpOffsets[LUMP_LIGHTMAPS];
            ms.Read(bsp.LightmapData, 0, bsp.LightmapData.Length);
        }

        return bsp;
    }

    private static string ReadString(byte[] data, int offset, int length)
    {
        int nullPos = Array.IndexOf(data, (byte)0, offset, length);
        int realLen = nullPos < 0 ? length : (nullPos - offset);
        return Encoding.ASCII.GetString(data, offset, realLen);
    }

    // ------------------------------------------------------------------
    // Patch tessellation — bicubic Bezier subdivision
    // Corresponds to R_SubdividePatchToGrid / R_CreateSurfaceGridMesh in tr_bsp.c
    //
    // controlPoints: the raw patch grid (width * height verts, row-major)
    // level:         subdivision steps per Bezier segment (LOD)
    // ------------------------------------------------------------------
    public static void TesselatePatch(
        BspVertex[] controlPoints,
        int width, int height,
        int level,
        out BspVertex[] outVerts,
        out int[] outIndices)
    {
        // Number of output verts per row/col (each pair of rows/cols is a Bezier curve)
        // width and height must be odd (2n+1 control points per axis)
        int numPatchesX = (width  - 1) / 2;
        int numPatchesY = (height - 1) / 2;
        int stepsPerPatch = (int)Mathf.Pow(2, level);  // 8 subdivisions at level=3
        int gridW = numPatchesX * stepsPerPatch + 1;
        int gridH = numPatchesY * stepsPerPatch + 1;

        outVerts = new BspVertex[gridW * gridH];

        // Evaluate the 2-D tensor-product Bezier surface
        for (int py = 0; py < numPatchesY; py++)
        {
            for (int px = 0; px < numPatchesX; px++)
            {
                // Collect 3×3 control point block for this patch
                var cp = new BspVertex[9];
                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int srcRow = py * 2 + row;
                        int srcCol = px * 2 + col;
                        cp[row * 3 + col] = controlPoints[srcRow * width + srcCol];
                    }
                }

                // Fill output grid region for this patch
                for (int sy = 0; sy <= stepsPerPatch; sy++)
                {
                    float t = sy / (float)stepsPerPatch;
                    for (int sx = 0; sx <= stepsPerPatch; sx++)
                    {
                        float s = sx / (float)stepsPerPatch;
                        int outX = px * stepsPerPatch + sx;
                        int outY = py * stepsPerPatch + sy;
                        outVerts[outY * gridW + outX] = EvalBicubic(cp, s, t);
                    }
                }
            }
        }

        // Build index list (two triangles per grid quad)
        // Winding order flipped for Unity left-hand system
        int numQuadsX = gridW - 1;
        int numQuadsY = gridH - 1;
        outIndices = new int[numQuadsX * numQuadsY * 6];
        int idx = 0;
        for (int row = 0; row < numQuadsY; row++)
        {
            for (int col = 0; col < numQuadsX; col++)
            {
                int v0 = row       * gridW + col;
                int v1 = row       * gridW + col + 1;
                int v2 = (row + 1) * gridW + col;
                int v3 = (row + 1) * gridW + col + 1;

                // Tri 1
                outIndices[idx++] = v0;
                outIndices[idx++] = v2;
                outIndices[idx++] = v1;
                // Tri 2
                outIndices[idx++] = v1;
                outIndices[idx++] = v2;
                outIndices[idx++] = v3;
            }
        }
    }

    // Evaluate a 3×3 (quadratic) Bezier surface at (s, t) in [0,1]²
    private static BspVertex EvalBicubic(BspVertex[] cp, float s, float t)
    {
        // First pass: evaluate 3 cubic curves along rows (V direction)
        var rowVerts = new BspVertex[3];
        for (int row = 0; row < 3; row++)
            rowVerts[row] = LerpVert(
                LerpVert(cp[row * 3 + 0], cp[row * 3 + 1], s),
                LerpVert(cp[row * 3 + 1], cp[row * 3 + 2], s),
                s);
        // Second pass: evaluate the resulting curve along U direction
        return LerpVert(
            LerpVert(rowVerts[0], rowVerts[1], t),
            LerpVert(rowVerts[1], rowVerts[2], t),
            t);
    }

    private static BspVertex LerpVert(BspVertex a, BspVertex b, float t)
    {
        return new BspVertex
        {
            Position   = Vector3.Lerp(a.Position,   b.Position,   t),
            Normal     = Vector3.Lerp(a.Normal,     b.Normal,     t).normalized,
            UV         = Vector2.Lerp(a.UV,         b.UV,         t),
            LightmapUV = Vector2.Lerp(a.LightmapUV, b.LightmapUV, t),
            Color      = Color32.Lerp(a.Color,      b.Color,      t),
        };
    }
}

// ---------------------------------------------------------------------------
// BspImporter — Unity Editor ScriptedImporter for .bsp files
// ---------------------------------------------------------------------------
#if UNITY_EDITOR
[ScriptedImporter(1, "bsp")]
public class BspImporter : ScriptedImporter
{
    private const int LIGHTMAP_W = 128;
    private const int LIGHTMAP_H = 128;

    public override void OnImportAsset(AssetImportContext ctx)
    {
        BspData bsp;
        try
        {
            bsp = BspData.LoadFromFile(ctx.assetPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"BspImporter: failed to load '{ctx.assetPath}': {ex.Message}");
            return;
        }

        string bspName = Path.GetFileNameWithoutExtension(ctx.assetPath);

        // ---- Import lightmaps as Texture2D assets ----
        var lightmapTextures = ImportLightmaps(ctx, bsp, bspName);

        // ---- Build material cache keyed by shader name ----
        var materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        // ---- Root GameObject ----
        var root = new GameObject(bspName);

        // World model is always Models[0]
        if (bsp.Models == null || bsp.Models.Length == 0)
        {
            Debug.LogWarning("BspImporter: no models in BSP.");
            ctx.AddObjectToAsset("root", root);
            ctx.SetMainObject(root);
            return;
        }

        BspModel worldModel = bsp.Models[0];

        for (int si = 0; si < worldModel.NumSurfaces; si++)
        {
            int surfIdx = worldModel.FirstSurface + si;
            if (surfIdx < 0 || surfIdx >= bsp.Surfaces.Length)
                continue;

            BspSurface surf = bsp.Surfaces[surfIdx];

            if (surf.SurfaceType != SurfaceType.MST_PLANAR &&
                surf.SurfaceType != SurfaceType.MST_PATCH  &&
                surf.SurfaceType != SurfaceType.MST_TRIANGLE_SOUP)
                continue;

            BspVertex[] surfVerts;
            int[]       surfIndices;

            if (surf.SurfaceType == SurfaceType.MST_PATCH)
            {
                // Extract control point grid
                if (surf.PatchWidth < 3 || surf.PatchHeight < 3 ||
                    surf.NumVerts < surf.PatchWidth * surf.PatchHeight)
                {
                    Debug.LogWarning($"BspImporter: surface {surfIdx} has invalid patch dimensions.");
                    continue;
                }
                var cpGrid = new BspVertex[surf.PatchWidth * surf.PatchHeight];
                for (int k = 0; k < cpGrid.Length; k++)
                    cpGrid[k] = bsp.Vertices[surf.FirstVert + k];

                BspData.TesselatePatch(cpGrid, surf.PatchWidth, surf.PatchHeight, 3,
                    out surfVerts, out surfIndices);
            }
            else
            {
                // Planar / triangle soup — copy verts and re-index
                surfVerts = new BspVertex[surf.NumVerts];
                for (int k = 0; k < surf.NumVerts; k++)
                    surfVerts[k] = bsp.Vertices[surf.FirstVert + k];

                surfIndices = new int[surf.NumIndexes];
                for (int k = 0; k < surf.NumIndexes; k++)
                    surfIndices[k] = bsp.Indices[surf.FirstIndex + k];
            }

            if (surfVerts.Length == 0 || surfIndices.Length == 0)
                continue;

            // ---- Build Mesh ----
            var mesh = BuildMesh(surfVerts, surfIndices, $"{bspName}_surf{surfIdx}");
            ctx.AddObjectToAsset($"mesh_{surfIdx}", mesh);

            // ---- GameObject with MeshFilter + MeshRenderer ----
            var surfGo = new GameObject($"surf_{surfIdx}");
            surfGo.transform.SetParent(root.transform, false);

            var mf = surfGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = surfGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = GetOrCreateMaterial(ctx, bsp, surf, materialCache, lightmapTextures);
        }

        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);
    }

    // ------------------------------------------------------------------
    private static Mesh BuildMesh(BspVertex[] verts, int[] indices, string name)
    {
        var positions  = new Vector3[verts.Length];
        var normals    = new Vector3[verts.Length];
        var uvs        = new Vector2[verts.Length];
        var uvs2       = new Vector2[verts.Length];
        var colors     = new Color32[verts.Length];

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
            name       = name,
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

    // ------------------------------------------------------------------
    private static Texture2D[] ImportLightmaps(AssetImportContext ctx, BspData bsp, string bspName)
    {
        if (bsp.LightmapCount == 0)
            return Array.Empty<Texture2D>();

        int bytesPerMap = LIGHTMAP_W * LIGHTMAP_H * 3;
        var textures = new Texture2D[bsp.LightmapCount];

        for (int li = 0; li < bsp.LightmapCount; li++)
        {
            var tex = new Texture2D(LIGHTMAP_W, LIGHTMAP_H, TextureFormat.RGB24, mipChain: false);
            tex.name = $"{bspName}_lightmap{li}";

            // ET lightmaps are stored top-to-bottom; Unity textures are bottom-to-top.
            // Flip rows when loading.
            int srcBase = li * bytesPerMap;
            var rawFlipped = new byte[bytesPerMap];
            for (int row = 0; row < LIGHTMAP_H; row++)
            {
                int srcRow  = (LIGHTMAP_H - 1 - row) * LIGHTMAP_W * 3;
                int dstRow  = row * LIGHTMAP_W * 3;
                Array.Copy(bsp.LightmapData, srcBase + srcRow, rawFlipped, dstRow, LIGHTMAP_W * 3);
            }

            tex.LoadRawTextureData(rawFlipped);
            tex.Apply(updateMipmaps: false);
            ctx.AddObjectToAsset($"lightmap_{li}", tex);
            textures[li] = tex;
        }
        return textures;
    }

    // ------------------------------------------------------------------
    private static Material GetOrCreateMaterial(
        AssetImportContext ctx,
        BspData bsp,
        BspSurface surf,
        Dictionary<string, Material> cache,
        Texture2D[] lightmapTextures)
    {
        string shaderName = (surf.ShaderNum >= 0 && surf.ShaderNum < bsp.Shaders.Length)
            ? bsp.Shaders[surf.ShaderNum].Name
            : "unknown";

        if (cache.TryGetValue(shaderName, out var existing))
            return existing;

        // Try to find a pre-imported material asset matching the shader name
        string matPath = $"Assets/Materials/{shaderName}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        if (mat == null)
        {
            // Create a fallback Standard material
            mat = new Material(Shader.Find("Standard"))
            {
                name = shaderName,
            };

            // Try to load a matching texture from the project
            string[] texExtensions = { ".tga", ".jpg", ".png", ".dds" };
            foreach (var ext in texExtensions)
            {
                string texPath = $"Assets/Textures/{shaderName}{ext}";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex != null)
                {
                    mat.mainTexture = tex;
                    break;
                }
            }

            ctx.AddObjectToAsset($"mat_{shaderName}", mat);
        }

        cache[shaderName] = mat;
        return mat;
    }
}
#endif
