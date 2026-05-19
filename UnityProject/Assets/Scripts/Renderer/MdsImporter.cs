// Ported from: src/renderer/tr_animation.c (MDS format reading)
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
// MDS constants
// ---------------------------------------------------------------------------

public static class MdsConst
{
    public const int Ident    = unchecked((int)0x4D445321); // "MDS!"
    public const int Version  = 10;
    public const int MaxQPath = 64;

    // mdsBoneInfo_t flags
    public const int BoneFlagTag = 0x0001; // bone is a tag (attachment point)
}

// ---------------------------------------------------------------------------
// MDS data structures
// ---------------------------------------------------------------------------

public struct MdsBone
{
    public string Name;
    public int    Parent;      // -1 = root
    public float  TorsoWeight;
    public float  ParentDist;
    public int    Flags;
}

public struct MdsWeight
{
    public int     BoneIndex;
    public float   BoneWeight;
    public Vector3 Offset;    // in bone-local space (ET→Unity applied)
}

public struct MdsVertex
{
    public Vector3    Normal;
    public Vector2    UV;
    public MdsWeight[] Weights;
}

public class MdsSurface
{
    public string      Name;
    public string      ShaderName;
    public MdsVertex[] Vertices;
    public int[]       Indices;
    // Bone references for this surface (indices into MdsData.Bones)
    public int[]       BoneReferences;
}

public struct MdsTag
{
    public string Name;
    public int    BoneIndex;
    public float  TorsoWeight;
}

public class MdsData
{
    public string         Name;
    public int            NumFrames;
    public int            NumBones;
    public float          LodScale;
    public float          LodBias;
    public int            TorsoParent; // bone index
    public MdsBone[]      Bones;
    public MdsSurface[]   Surfaces;
    public MdsTag[]       Tags;
    public Vector3[]      FrameOrigins;       // root origin per frame (Unity space)
    public Quaternion[][] FrameBoneAngles;    // per-frame per-bone rotation (Unity space)

    // -----------------------------------------------------------------------
    // Load from raw bytes
    // -----------------------------------------------------------------------
    public static MdsData Load(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var r  = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

        // --- Header --------------------------------------------------------
        int ident = r.ReadInt32();
        if (ident != MdsConst.Ident)
            throw new Exception($"MDS: bad ident 0x{ident:X8} (expected 0x{MdsConst.Ident:X8})");

        int version = r.ReadInt32();
        if (version != MdsConst.Version)
            throw new Exception($"MDS: bad version {version} (expected {MdsConst.Version})");

        string name     = ReadFixedString(r, MdsConst.MaxQPath);
        float  lodScale = r.ReadSingle();
        float  lodBias  = r.ReadSingle();
        int    numFrames   = r.ReadInt32();
        int    numBones    = r.ReadInt32();
        int    ofsFrames   = r.ReadInt32();
        int    ofsBones    = r.ReadInt32();
        int    torsoParent = r.ReadInt32();
        int    numSurfaces = r.ReadInt32();
        int    ofsSurfaces = r.ReadInt32();
        int    numTags     = r.ReadInt32();
        int    ofsTags     = r.ReadInt32();
        int    ofsEnd      = r.ReadInt32();

        var mds = new MdsData
        {
            Name        = name,
            NumFrames   = numFrames,
            NumBones    = numBones,
            LodScale    = lodScale,
            LodBias     = lodBias,
            TorsoParent = torsoParent,
            Bones       = new MdsBone[numBones],
            Surfaces    = new MdsSurface[numSurfaces],
            Tags        = new MdsTag[numTags],
        };

        // --- Bone info (mdsBoneInfo_t, 128 bytes each) ---------------------
        // Layout: char name[64] + int parent + float torsoWeight + float parentDist + int flags
        // That's 64 + 4 + 4 + 4 + 4 = 80 bytes.  The struct is padded to 128 in the ET source;
        // we skip the remaining 48 bytes of padding.
        ms.Position = ofsBones;
        for (int i = 0; i < numBones; i++)
        {
            long boneStart = ms.Position;
            string boneName  = ReadFixedString(r, MdsConst.MaxQPath); // 64 bytes
            int    parent    = r.ReadInt32();
            float  torsoW    = r.ReadSingle();
            float  parentDst = r.ReadSingle();
            int    flags     = r.ReadInt32();
            // Seek to next 128-byte-aligned bone slot
            ms.Position = boneStart + 128;

            mds.Bones[i] = new MdsBone
            {
                Name        = boneName,
                Parent      = parent,
                TorsoWeight = torsoW,
                ParentDist  = parentDst,
                Flags       = flags,
            };
        }

        // --- Frames --------------------------------------------------------
        // mdsFrame_t header (28 bytes):
        //   float localOrigin[3]  (12 bytes)
        //   float radius          (4 bytes)
        //   float parentOffset[3] (12 bytes)
        // Followed by numBones × mdsBoneFrameCompressed_t (5 shorts = 10 bytes each):
        //   short angles[3]    — pitch/yaw/roll packed as short → angle * 65536/360
        //   short ofsAngles[2] — offset angles (pitch/yaw only) packed the same way
        const int compressedBoneSize = 10;
        const int frameHeaderSize    = 28;

        // Decoded per-frame bone data stored for SkinnedMeshRenderer animation
        mds.FrameOrigins    = new Vector3[numFrames];
        mds.FrameBoneAngles = new Quaternion[numFrames][];

        ms.Position = ofsFrames;
        for (int f = 0; f < numFrames; f++)
        {
            long frameStart = ms.Position;
            var etOrigin    = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float radius    = r.ReadSingle();
            var etParentOfs = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            mds.FrameOrigins[f] = EtToUnity(etOrigin);

            // Decompress bone frames (mdsBoneFrameCompressed_t)
            var boneRots = new Quaternion[numBones];
            for (int b = 0; b < numBones; b++)
            {
                // Each angle component: short → degrees via SHORT2ANGLE (value * 360/65536)
                float pitch = r.ReadInt16() * (360f / 65536f);
                float yaw   = r.ReadInt16() * (360f / 65536f);
                float roll  = r.ReadInt16() * (360f / 65536f);
                // ofsAngles: additional pitch/yaw offset applied to bone (for torso twist etc.)
                float ofsPitch = r.ReadInt16() * (360f / 65536f);
                float ofsYaw   = r.ReadInt16() * (360f / 65536f);

                // ET uses ZYX Euler order (yaw=Z, pitch=Y, roll=X in ET coords)
                // Convert to Unity Quaternion (ET X=fwd,Y=left,Z=up → Unity X=right,Y=up,Z=fwd)
                // Apply offset angles first, then base rotation
                var baseRot   = Quaternion.Euler(pitch + ofsPitch, yaw + ofsYaw, roll);
                // Remap to Unity space: swap axes via a fixed permutation
                boneRots[b]   = RemapEtBoneRotation(baseRot);
            }
            mds.FrameBoneAngles[f] = boneRots;

            ms.Position = frameStart + frameHeaderSize + numBones * compressedBoneSize;
        }

        // --- Tags (mdsBoneInfo_t reused; tag entries share the bone list) --
        // In ET, tags are stored separately after surfaces as mdsTag_t:
        //   char name[MAX_QPATH=64] + int boneIndex + float torsoWeight
        ms.Position = ofsTags;
        for (int i = 0; i < numTags; i++)
        {
            string tagName    = ReadFixedString(r, MdsConst.MaxQPath);
            int    boneIndex  = r.ReadInt32();
            float  torsoW     = r.ReadSingle();
            mds.Tags[i] = new MdsTag
            {
                Name        = tagName,
                BoneIndex   = boneIndex,
                TorsoWeight = torsoW,
            };
        }

        // --- Surfaces ------------------------------------------------------
        long surfPos = ofsSurfaces;
        for (int si = 0; si < numSurfaces; si++)
        {
            ms.Position = surfPos;
            mds.Surfaces[si] = ReadSurface(r, ms, surfPos);
            // ofsEnd is relative to surface start
            surfPos += mds.Surfaces[si]._ofsEnd;
        }

        return mds;
    }

    // -----------------------------------------------------------------------
    // Load from file path
    // -----------------------------------------------------------------------
    public static MdsData LoadFromFile(string path)
        => Load(File.ReadAllBytes(path));

    // -----------------------------------------------------------------------
    // Surface reader
    // -----------------------------------------------------------------------
    private static MdsSurface ReadSurface(BinaryReader r, MemoryStream ms, long surfStart)
    {
        // mdsSurface_t header
        int    ident             = r.ReadInt32();
        string surfName          = ReadFixedString(r, MdsConst.MaxQPath);
        string shaderName        = ReadFixedString(r, MdsConst.MaxQPath);
        int    shaderIndex       = r.ReadInt32(); // unused at load time
        int    minLod            = r.ReadInt32();
        int    ofsHeader         = r.ReadInt32(); // negative offset back to file header (unused here)
        int    numVerts          = r.ReadInt32();
        int    ofsVerts          = r.ReadInt32(); // from surface start
        int    numTriangles      = r.ReadInt32();
        int    ofsTriangles      = r.ReadInt32(); // from surface start
        int    ofsCollapseMap   = r.ReadInt32(); // LOD data — skip
        int    numBoneRefs       = r.ReadInt32();
        int    ofsBoneRefs       = r.ReadInt32(); // from surface start
        int    ofsEnd            = r.ReadInt32(); // from surface start

        var surf = new MdsSurface
        {
            Name           = surfName,
            ShaderName     = shaderName,
            Vertices       = new MdsVertex[numVerts],
            Indices        = new int[numTriangles * 3],
            BoneReferences = new int[numBoneRefs],
            _ofsEnd        = ofsEnd,
        };

        // ---- Bone references (int[numBoneRefs]) ---------------------------
        ms.Position = surfStart + ofsBoneRefs;
        for (int i = 0; i < numBoneRefs; i++)
            surf.BoneReferences[i] = r.ReadInt32();

        // ---- Triangles (mdsTriangle_t: int[3]) ----------------------------
        ms.Position = surfStart + ofsTriangles;
        for (int i = 0; i < numTriangles; i++)
        {
            surf.Indices[i * 3 + 0] = r.ReadInt32();
            surf.Indices[i * 3 + 1] = r.ReadInt32();
            surf.Indices[i * 3 + 2] = r.ReadInt32();
        }

        // ---- Vertices (mdsVertex_t, variable-size) ------------------------
        // mdsVertex_t:
        //   float normal[3]      12 bytes
        //   float texCoords[2]    8 bytes
        //   int   numWeights      4 bytes
        //   int   fixedParent     4 bytes  (-1 if none)
        //   float fixedDist       4 bytes
        //   mdsWeight_t weights[numWeights]:
        //     int   boneIndex     4 bytes
        //     float boneWeight    4 bytes
        //     float offset[3]    12 bytes   = 20 bytes per weight
        ms.Position = surfStart + ofsVerts;
        for (int vi = 0; vi < numVerts; vi++)
        {
            var etNormal    = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var uv          = new Vector2(r.ReadSingle(), r.ReadSingle());
            int numWeights  = r.ReadInt32();
            int fixedParent = r.ReadInt32();
            float fixedDist = r.ReadSingle();

            var weights = new MdsWeight[numWeights];
            for (int wi = 0; wi < numWeights; wi++)
            {
                int   boneIdx = r.ReadInt32();
                float boneW   = r.ReadSingle();
                var   etOfs   = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                weights[wi] = new MdsWeight
                {
                    BoneIndex  = boneIdx,
                    BoneWeight = boneW,
                    Offset     = EtToUnity(etOfs),
                };
            }

            surf.Vertices[vi] = new MdsVertex
            {
                Normal  = EtToUnity(etNormal),
                UV      = uv,
                Weights = weights,
            };
        }

        return surf;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>ET (X=forward, Y=left, Z=up) → Unity (X=right, Y=up, Z=forward)</summary>
    public static Vector3 EtToUnity(Vector3 et)
        => new Vector3(-et.y, et.z, et.x);

    /// <summary>Read a null-terminated fixed-length ASCII string.</summary>
    public static string ReadFixedString(BinaryReader r, int length)
    {
        byte[] buf = r.ReadBytes(length);
        int end = Array.IndexOf(buf, (byte)0);
        if (end < 0) end = length;
        return Encoding.ASCII.GetString(buf, 0, end);
    }

    // Internal surface field so the outer loop can advance by ofsEnd
    internal int _ofsEnd;
}

// ---------------------------------------------------------------------------
// Unity Editor importer
// ---------------------------------------------------------------------------

#if UNITY_EDITOR
[ScriptedImporter(1, "mds")]
public class MdsImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        MdsData mds;
        try
        {
            mds = MdsData.LoadFromFile(ctx.assetPath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MdsImporter] Failed to load {ctx.assetPath}: {ex.Message}");
            return;
        }

        // Root GameObject carries the Animator
        var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath));
        root.AddComponent<Animator>(); // Avatar assigned below
        ctx.AddObjectToAsset("root", root);
        ctx.SetMainObject(root);

        // ----------------------------------------------------------------
        // Build bone Transform hierarchy
        // ----------------------------------------------------------------
        var boneTransforms = new Transform[mds.NumBones];
        for (int bi = 0; bi < mds.NumBones; bi++)
        {
            var boneGo = new GameObject(mds.Bones[bi].Name);
            boneTransforms[bi] = boneGo.transform;
        }

        // Parent each bone to its parent (or root if parent == -1)
        for (int bi = 0; bi < mds.NumBones; bi++)
        {
            int parentIdx = mds.Bones[bi].Parent;
            if (parentIdx >= 0 && parentIdx < mds.NumBones)
                boneTransforms[bi].SetParent(boneTransforms[parentIdx], worldPositionStays: false);
            else
                boneTransforms[bi].SetParent(root.transform, worldPositionStays: false);
        }

        // Register bone GameObjects as assets
        for (int bi = 0; bi < mds.NumBones; bi++)
            ctx.AddObjectToAsset($"bone_{bi}", boneTransforms[bi].gameObject);

        // ----------------------------------------------------------------
        // Build Avatar from bone hierarchy
        // ----------------------------------------------------------------
        // We use a generic (non-humanoid) avatar so any bone name works.
        var skeletonBones = new SkeletonBone[mds.NumBones + 1];

        // Entry 0: root transform
        skeletonBones[0] = new SkeletonBone
        {
            name        = root.name,
            position    = Vector3.zero,
            rotation    = Quaternion.identity,
            scale       = Vector3.one,
        };
        for (int bi = 0; bi < mds.NumBones; bi++)
        {
            skeletonBones[bi + 1] = new SkeletonBone
            {
                name     = mds.Bones[bi].Name,
                position = Vector3.zero,
                rotation = Quaternion.identity,
                scale    = Vector3.one,
            };
        }

        var humanDesc = new HumanDescription
        {
            skeleton          = skeletonBones,
            human             = Array.Empty<HumanBone>(),
            upperArmTwist     = 0.5f,
            lowerArmTwist     = 0.5f,
            upperLegTwist     = 0.5f,
            lowerLegTwist     = 0.5f,
            armStretch        = 0.05f,
            legStretch        = 0.05f,
            feetSpacing       = 0f,
        };

        var avatar = AvatarBuilder.BuildGenericAvatar(root, "");
        avatar.name = mds.Name + "_Avatar";
        if (!avatar.isValid)
            Debug.LogWarning($"[MdsImporter] Avatar for {mds.Name} is invalid.");

        root.GetComponent<Animator>().avatar = avatar;
        ctx.AddObjectToAsset("avatar", avatar);

        // ----------------------------------------------------------------
        // Surfaces → SkinnedMeshRenderer
        // ----------------------------------------------------------------
        for (int si = 0; si < mds.Surfaces.Length; si++)
        {
            var surf = mds.Surfaces[si];
            int numVerts = surf.Vertices.Length;

            // Collect per-vertex data
            var positions  = new Vector3[numVerts];
            var normals    = new Vector3[numVerts];
            var uvs        = new Vector2[numVerts];
            var boneWeights = new BoneWeight[numVerts];

            for (int vi = 0; vi < numVerts; vi++)
            {
                var v = surf.Vertices[vi];
                normals[vi] = v.Normal;
                uvs[vi]     = v.UV;

                // Compute bind-pose position as weighted sum of bone offsets
                var pos = Vector3.zero;
                foreach (var w in v.Weights)
                    pos += w.BoneWeight * w.Offset;
                positions[vi] = pos;

                // Pack up to 4 bone influences into BoneWeight, normalizing if needed
                boneWeights[vi] = PackBoneWeights(v.Weights, surf.BoneReferences);
            }

            // Flip winding for Unity handedness
            int[] tris = FlipWinding(surf.Indices);

            // Build bind poses: one per bone (identity in local bind pose)
            // Bind pose = inverse of bone world matrix at bind time.
            // Since we don't have frame 0 bone matrices decoded yet (TODO),
            // we use identity — this is correct when all bone transforms are at origin.
            var bindPoses = new Matrix4x4[mds.NumBones];
            for (int bi = 0; bi < mds.NumBones; bi++)
                bindPoses[bi] = boneTransforms[bi].worldToLocalMatrix * root.transform.localToWorldMatrix;

            var mesh = new Mesh { name = surf.Name };
            mesh.vertices  = positions;
            mesh.normals   = normals;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.boneWeights = boneWeights;
            mesh.bindposes   = bindPoses;
            mesh.RecalculateBounds();

            ctx.AddObjectToAsset($"mesh_{si}", mesh);

            // Child GameObject with SkinnedMeshRenderer
            var child = new GameObject(surf.Name);
            child.transform.SetParent(root.transform, worldPositionStays: false);

            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones      = boneTransforms;
            smr.rootBone   = (mds.TorsoParent >= 0 && mds.TorsoParent < mds.NumBones)
                ? boneTransforms[mds.TorsoParent]
                : root.transform;

            // Default material
            var mat = new Material(Shader.Find("Standard")) { name = surf.ShaderName };
            smr.sharedMaterial = mat;
            ctx.AddObjectToAsset($"mat_{si}", mat);
        }

        // ----------------------------------------------------------------
        // Tags → empty child GameObjects parented to their bone
        // ----------------------------------------------------------------
        for (int ti = 0; ti < mds.Tags.Length; ti++)
        {
            var tag = mds.Tags[ti];

            var tagGo = new GameObject(tag.Name);
            if (tag.BoneIndex >= 0 && tag.BoneIndex < mds.NumBones)
                tagGo.transform.SetParent(boneTransforms[tag.BoneIndex], worldPositionStays: false);
            else
                tagGo.transform.SetParent(root.transform, worldPositionStays: false);

            ctx.AddObjectToAsset($"tag_{ti}", tagGo);
        }
    }

    // -----------------------------------------------------------------------
    // Pack MdsWeight[] → Unity BoneWeight (max 4 influences)
    // -----------------------------------------------------------------------
    private static BoneWeight PackBoneWeights(MdsWeight[] weights, int[] boneRefs)
    {
        // Sort by weight descending, take up to 4
        var sorted = new List<MdsWeight>(weights);
        sorted.Sort((a, b) => b.BoneWeight.CompareTo(a.BoneWeight));

        int count = Mathf.Min(sorted.Count, 4);

        // Normalize top-4 weights so they sum to 1
        float sum = 0f;
        for (int i = 0; i < count; i++) sum += sorted[i].BoneWeight;
        if (sum < 1e-6f) sum = 1f;

        float w0 = count > 0 ? sorted[0].BoneWeight / sum : 0f;
        float w1 = count > 1 ? sorted[1].BoneWeight / sum : 0f;
        float w2 = count > 2 ? sorted[2].BoneWeight / sum : 0f;
        float w3 = count > 3 ? sorted[3].BoneWeight / sum : 0f;

        // BoneIndex refers into surf.BoneReferences, which maps to the global bone array.
        // If boneRefs is provided, resolve the indirection; otherwise use directly.
        int Resolve(int localIdx)
        {
            if (boneRefs != null && localIdx >= 0 && localIdx < boneRefs.Length)
                return boneRefs[localIdx];
            return localIdx;
        }

        return new BoneWeight
        {
            boneIndex0 = count > 0 ? Resolve(sorted[0].BoneIndex) : 0,
            weight0    = w0,
            boneIndex1 = count > 1 ? Resolve(sorted[1].BoneIndex) : 0,
            weight1    = w1,
            boneIndex2 = count > 2 ? Resolve(sorted[2].BoneIndex) : 0,
            weight2    = w2,
            boneIndex3 = count > 3 ? Resolve(sorted[3].BoneIndex) : 0,
            weight3    = w3,
        };
    }

    // -----------------------------------------------------------------------
    // Flip triangle winding (ET CCW → Unity CW)
    // -----------------------------------------------------------------------
    private static int[] FlipWinding(int[] src)
    {
        var dst = new int[src.Length];
        for (int i = 0; i < src.Length; i += 3)
        {
            dst[i + 0] = src[i + 0];
            dst[i + 1] = src[i + 2];
            dst[i + 2] = src[i + 1];
        }
        return dst;
    }
}
#endif
