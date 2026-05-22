// Ported from: src/renderer/tr_mesh.c (MD3 format reading)
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
// MD3 constants
// ---------------------------------------------------------------------------

public static class Md3Const
{
    public const int Ident   = 0x33504449; // "IDP3"
    public const int Version = 15;
    public const int MaxQPath = 64;
}

// ---------------------------------------------------------------------------
// MD3 data structures
// ---------------------------------------------------------------------------

public struct Md3Frame
{
    public Vector3 Mins;
    public Vector3 Maxs;
    public Vector3 LocalOrigin;
    public float   Radius;
    public string  Name;
}

public struct Md3Tag
{
    public string  Name;
    public Vector3 Origin;
    public Vector3 AxisX;
    public Vector3 AxisY;
    public Vector3 AxisZ;
}

public class Md3Surface
{
    public string     Name;
    public string[]   ShaderNames;
    public int[]      Indexes;           // flat triangle index list
    public Vector2[]  UVs;              // per-vertex, frame-independent
    public Vector3[]  Positions;        // frame 0 positions (Unity coords)
    public Vector3[]  Normals;          // frame 0 normals  (Unity coords)
    // Per-frame data for morph-target / blend-shape animation
    public Vector3[][] FramePositions;  // [frame][vert]
    public Vector3[][] FrameNormals;    // [frame][vert]
    internal int       _ofsEnd;        // relative offset to next surface (binary reader use only)
}

public class Md3Data
{
    public string       Name;
    public int          NumFrames;
    public Md3Frame[]   Frames;
    public Md3Tag[]     Tags;         // frame 0 tags (static attachment points)
    public Md3Tag[][]   FrameTags;    // [frame][tagIndex] — set by MDC loader for animated models
    public Md3Surface[] Surfaces;

    // -----------------------------------------------------------------------
    // Load from raw bytes
    // -----------------------------------------------------------------------
    public static Md3Data Load(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var r  = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

        // --- Header --------------------------------------------------------
        int ident = r.ReadInt32();
        if (ident != Md3Const.Ident)
            throw new Exception($"MD3: bad ident 0x{ident:X8} (expected 0x{Md3Const.Ident:X8})");

        int version = r.ReadInt32();
        if (version != Md3Const.Version)
            throw new Exception($"MD3: bad version {version} (expected {Md3Const.Version})");

        string name      = ReadFixedString(r, Md3Const.MaxQPath);
        int    flags     = r.ReadInt32();
        int    numFrames = r.ReadInt32();
        int    numTags   = r.ReadInt32();
        int    numSurfs  = r.ReadInt32();
        int    numSkins  = r.ReadInt32(); // unused in ET
        int    ofsFrames    = r.ReadInt32();
        int    ofsTags      = r.ReadInt32();
        int    ofsSurfaces  = r.ReadInt32();
        int    ofsEof       = r.ReadInt32();

        var md3 = new Md3Data
        {
            Name      = name,
            NumFrames = numFrames,
            Frames    = new Md3Frame[numFrames],
            Tags      = new Md3Tag[numTags],
            Surfaces  = new Md3Surface[numSurfs],
        };

        // --- Frames (56 bytes each) -----------------------------------------
        ms.Position = ofsFrames;
        for (int i = 0; i < numFrames; i++)
        {
            var mins   = ReadEtVec3(r); // bounds[0]
            var maxs   = ReadEtVec3(r); // bounds[1]
            var origin = ReadEtVec3(r);
            float rad  = r.ReadSingle();
            string fn  = ReadFixedString(r, 16);
            md3.Frames[i] = new Md3Frame
            {
                Mins        = mins,
                Maxs        = maxs,
                LocalOrigin = origin,
                Radius      = rad,
                Name        = fn,
            };
        }

        // --- Tags (112 bytes each) ------------------------------------------
        // char name[64] + vec3_t origin + vec3_t axis[3]
        ms.Position = ofsTags;
        for (int i = 0; i < numTags; i++)
        {
            string tagName = ReadFixedString(r, Md3Const.MaxQPath);
            var origin = ReadEtVec3(r);
            var axX    = ReadEtVec3(r);
            var axY    = ReadEtVec3(r);
            var axZ    = ReadEtVec3(r);
            md3.Tags[i] = new Md3Tag
            {
                Name    = tagName,
                Origin  = origin,
                AxisX   = axX,
                AxisY   = axY,
                AxisZ   = axZ,
            };
        }

        // --- Surfaces -------------------------------------------------------
        long surfBase = ofsSurfaces;
        for (int si = 0; si < numSurfs; si++)
        {
            ms.Position = surfBase;
            md3.Surfaces[si] = ReadSurface(r, ms, surfBase, numFrames);
            // advance to next surface using ofsEnd stored in the surface
            surfBase += md3.Surfaces[si]._ofsEnd;
        }

        return md3;
    }

    // -----------------------------------------------------------------------
    // Load from file path
    // -----------------------------------------------------------------------
    public static Md3Data LoadFromFile(string path)
        => Load(File.ReadAllBytes(path));

    // -----------------------------------------------------------------------
    // Surface reader
    // -----------------------------------------------------------------------

    // Temporary holder so we can advance the outer loop by ofsEnd
    // (ofsEnd is relative to surface start, not file start)

    private static Md3Surface ReadSurface(BinaryReader r, MemoryStream ms, long surfStart, int numModelFrames)
    {
        // Surface header fields (offsets relative to surfStart)
        int surfIdent     = r.ReadInt32();
        string surfName   = ReadFixedString(r, Md3Const.MaxQPath);
        int surfFlags     = r.ReadInt32();
        int numFrames     = r.ReadInt32();
        int numShaders    = r.ReadInt32();
        int numVerts      = r.ReadInt32();
        int numTris       = r.ReadInt32();
        int ofsTris       = r.ReadInt32();
        int ofsShaders    = r.ReadInt32();
        int ofsST         = r.ReadInt32();
        int ofsXyzNormals = r.ReadInt32();
        int ofsEnd        = r.ReadInt32();

        var surf = new Md3Surface
        {
            Name          = surfName,
            ShaderNames   = new string[numShaders],
            Indexes       = new int[numTris * 3],
            UVs           = new Vector2[numVerts],
            Positions     = new Vector3[numVerts],
            Normals       = new Vector3[numVerts],
            FramePositions = new Vector3[numModelFrames][],
            FrameNormals   = new Vector3[numModelFrames][],
            _ofsEnd        = ofsEnd,
        };

        // Shaders (68 bytes each: char[64] + int shaderIndex)
        ms.Position = surfStart + ofsShaders;
        for (int i = 0; i < numShaders; i++)
        {
            surf.ShaderNames[i] = ReadFixedString(r, Md3Const.MaxQPath);
            int _idx = r.ReadInt32(); // shaderIndex, unused at load time
        }

        // Triangles (12 bytes each: int[3])
        ms.Position = surfStart + ofsTris;
        for (int i = 0; i < numTris; i++)
        {
            surf.Indexes[i * 3 + 0] = r.ReadInt32();
            surf.Indexes[i * 3 + 1] = r.ReadInt32();
            surf.Indexes[i * 3 + 2] = r.ReadInt32();
        }

        // Texture coordinates (8 bytes each: float[2])
        ms.Position = surfStart + ofsST;
        for (int i = 0; i < numVerts; i++)
        {
            float u = r.ReadSingle();
            float v = r.ReadSingle();
            surf.UVs[i] = new Vector2(u, 1f - v);   // ET/OpenGL V=0 is bottom; Unity V=0 is top
        }

        // XyzNormals — 8 bytes per vertex per frame: short[3] xyz + short normal
        ms.Position = surfStart + ofsXyzNormals;
        for (int f = 0; f < numModelFrames; f++)
        {
            var fpos = new Vector3[numVerts];
            var fnrm = new Vector3[numVerts];
            for (int v = 0; v < numVerts; v++)
            {
                short sx = r.ReadInt16();
                short sy = r.ReadInt16();
                short sz = r.ReadInt16();
                short sn = r.ReadInt16();

                // Position: fixed-point /64, then ET→Unity
                var etPos = new Vector3(sx / 64f, sy / 64f, sz / 64f);
                fpos[v] = EtToUnity(etPos);
                fnrm[v] = DecodeNormal(sn);
            }
            surf.FramePositions[f] = fpos;
            surf.FrameNormals[f]   = fnrm;
        }

        // Frame 0 is the canonical bind pose
        if (numModelFrames > 0)
        {
            surf.Positions = surf.FramePositions[0];
            surf.Normals   = surf.FrameNormals[0];
        }

        return surf;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Read ET vec3 and convert to Unity coordinate system.</summary>
    private static Vector3 ReadEtVec3(BinaryReader r)
    {
        float x = r.ReadSingle();
        float y = r.ReadSingle();
        float z = r.ReadSingle();
        return EtToUnity(new Vector3(x, y, z));
    }

    /// <summary>ET (X=forward, Y=left, Z=up) → Unity (X=right, Y=up, Z=forward)</summary>
    public static Vector3 EtToUnity(Vector3 et)
        => new Vector3(-et.y, et.z, et.x);

    /// <summary>
    /// Decode the 16-bit packed normal from an XyzNormal entry.
    /// Q3/ET encoding: lat = high byte (azimuth), lng = low byte (zenith/inclination from +z).
    /// ET normal = (cos(lat)*sin(lng), sin(lat)*sin(lng), cos(lng)).
    /// Reference: Q3 tr_mesh.c — x=cos(lat)*sin(lng), y=sin(lat)*sin(lng), z=cos(lng).
    /// </summary>
    public static Vector3 DecodeNormal(short packed)
    {
        float lat = ((packed >> 8) & 0xFF) * (2f * Mathf.PI / 256f);
        float lng = (packed & 0xFF)         * (2f * Mathf.PI / 256f);
        var et = new Vector3(
            Mathf.Cos(lat) * Mathf.Sin(lng),
            Mathf.Sin(lat) * Mathf.Sin(lng),
            Mathf.Cos(lng));
        return EtToUnity(et);
    }

    /// <summary>Read a null-terminated fixed-length string from the stream.</summary>
    public static string ReadFixedString(BinaryReader r, int length)
    {
        byte[] buf = r.ReadBytes(length);
        int end = Array.IndexOf(buf, (byte)0);
        if (end < 0) end = length;
        return Encoding.ASCII.GetString(buf, 0, end);
    }

}

// ---------------------------------------------------------------------------
// Unity Editor importer
// ---------------------------------------------------------------------------

#if UNITY_EDITOR
[ScriptedImporter(1, "md3")]
public class Md3Importer : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        Md3Data md3;
        try
        {
            md3 = Md3Data.LoadFromFile(ctx.assetPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Md3Importer] Failed to load {ctx.assetPath}: {ex.Message}");
            return;
        }

        // Root GameObject
        var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath));
        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);

        // ----------------------------------------------------------------
        // Surfaces → child GameObjects with MeshFilter + MeshRenderer
        // ----------------------------------------------------------------
        for (int si = 0; si < md3.Surfaces.Length; si++)
        {
            var surf = md3.Surfaces[si];

            // Build Mesh from frame 0
            var mesh = new Mesh { name = surf.Name };
            mesh.vertices  = surf.Positions;
            mesh.uv        = surf.UVs;
            // EtToUnity (det=-1) produces CW winding in Unity space; do NOT flip.
            // RecalculateNormals on CW triangles gives outward normals (toward camera).
            mesh.triangles = surf.Indexes;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // ---- Blend shapes for morph-target animation ----------------
            // Frame 0 is the base mesh; frames 1..N become blend shapes.
            for (int f = 1; f < md3.NumFrames; f++)
            {
                Vector3[] deltaPos = new Vector3[surf.Positions.Length];
                Vector3[] deltaNrm = new Vector3[surf.Normals.Length];
                for (int v = 0; v < surf.Positions.Length; v++)
                {
                    deltaPos[v] = surf.FramePositions[f][v] - surf.Positions[v];
                    deltaNrm[v] = surf.FrameNormals[f][v]  - surf.Normals[v];
                }
                mesh.AddBlendShapeFrame($"frame_{f}", 100f, deltaPos, deltaNrm, null);
            }

            ctx.AddObjectToAsset($"mesh_{si}", mesh);

            // Child GameObject
            var child = new GameObject(surf.Name);
            child.transform.SetParent(root.transform, worldPositionStays: false);

            var mf = child.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = child.AddComponent<MeshRenderer>();

            // Assign a default material named after the first shader
            string matName = (surf.ShaderNames != null && surf.ShaderNames.Length > 0)
                ? surf.ShaderNames[0]
                : surf.Name;
            var mat = new Material(Shader.Find("Standard")) { name = matName };
            mr.sharedMaterial = mat;
            ctx.AddObjectToAsset($"mat_{si}", mat);
        }

        // ----------------------------------------------------------------
        // Tags → empty child GameObjects with correct transform
        // ----------------------------------------------------------------
        for (int ti = 0; ti < md3.Tags.Length; ti++)
        {
            var tag = md3.Tags[ti];

            var tagGo = new GameObject(tag.Name);
            tagGo.transform.SetParent(root.transform, worldPositionStays: false);
            tagGo.transform.localPosition = tag.Origin;

            // Build a rotation matrix from the three axis vectors.
            // tag.AxisX/Y/Z are already in Unity coords (ET→Unity applied at load time).
            var rotMatrix = new Matrix4x4();
            rotMatrix.SetColumn(0, new Vector4(tag.AxisX.x, tag.AxisX.y, tag.AxisX.z, 0f));
            rotMatrix.SetColumn(1, new Vector4(tag.AxisY.x, tag.AxisY.y, tag.AxisY.z, 0f));
            rotMatrix.SetColumn(2, new Vector4(tag.AxisZ.x, tag.AxisZ.y, tag.AxisZ.z, 0f));
            rotMatrix.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));
            tagGo.transform.localRotation = rotMatrix.rotation;
        }
    }

    // -----------------------------------------------------------------------
    // Helper: flip triangle winding order (ET CCW → Unity CW)
    // -----------------------------------------------------------------------
    private static int[] FlipWinding(int[] src)
    {
        var dst = new int[src.Length];
        for (int i = 0; i < src.Length; i += 3)
        {
            dst[i + 0] = src[i + 0];
            dst[i + 1] = src[i + 2]; // swap v1 and v2
            dst[i + 2] = src[i + 1];
        }
        return dst;
    }
}
#endif
