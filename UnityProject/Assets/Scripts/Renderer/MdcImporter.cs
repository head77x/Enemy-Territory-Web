// Ported from: src/renderer/tr_model.c + qfiles.h (MDC format reading)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// MDC (compressed MD3) format loader.
// ET's RE_RegisterModel (tr_model.c:252) replaces .md3 with .mdc and tries it first.
// Animated weapon viewmodels are stored as MDC. This loader decodes MDC and returns
// a fully-expanded Md3Data (FramePositions[f][v] for all frames) so that BuildMd3Object
// in RuntimeResourceLoader can consume it unchanged.

using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class MdcLoader
{
    // MDC magic: "IDPC" = ('I')|('D'<<8)|('P'<<16)|('C'<<24)
    private const int MDC_IDENT   = 0x43504449;
    private const int MDC_VERSION = 2;

    // -----------------------------------------------------------------------
    // r_anormals table (256 entries) from anorms256.h — ET coordinate space
    // (x=forward, y=left, z=up). EtToUnity is applied when writing normals.
    // -----------------------------------------------------------------------
    private static readonly Vector3[] AnormsTable = new Vector3[256]
    {
        new Vector3( 1.000000f,  0.000000f,  0.000000f),
        new Vector3( 0.980785f,  0.195090f,  0.000000f),
        new Vector3( 0.923880f,  0.382683f,  0.000000f),
        new Vector3( 0.831470f,  0.555570f,  0.000000f),
        new Vector3( 0.707107f,  0.707107f,  0.000000f),
        new Vector3( 0.555570f,  0.831470f,  0.000000f),
        new Vector3( 0.382683f,  0.923880f,  0.000000f),
        new Vector3( 0.195090f,  0.980785f,  0.000000f),
        new Vector3(-0.000000f,  1.000000f,  0.000000f),
        new Vector3(-0.195090f,  0.980785f,  0.000000f),
        new Vector3(-0.382683f,  0.923880f,  0.000000f),
        new Vector3(-0.555570f,  0.831470f,  0.000000f),
        new Vector3(-0.707107f,  0.707107f,  0.000000f),
        new Vector3(-0.831470f,  0.555570f,  0.000000f),
        new Vector3(-0.923880f,  0.382683f,  0.000000f),
        new Vector3(-0.980785f,  0.195090f,  0.000000f),
        new Vector3(-1.000000f, -0.000000f,  0.000000f),
        new Vector3(-0.980785f, -0.195090f,  0.000000f),
        new Vector3(-0.923880f, -0.382683f,  0.000000f),
        new Vector3(-0.831470f, -0.555570f,  0.000000f),
        new Vector3(-0.707107f, -0.707107f,  0.000000f),
        new Vector3(-0.555570f, -0.831469f,  0.000000f),
        new Vector3(-0.382684f, -0.923880f,  0.000000f),
        new Vector3(-0.195090f, -0.980785f,  0.000000f),
        new Vector3( 0.000000f, -1.000000f,  0.000000f),
        new Vector3( 0.195090f, -0.980785f,  0.000000f),
        new Vector3( 0.382684f, -0.923879f,  0.000000f),
        new Vector3( 0.555570f, -0.831470f,  0.000000f),
        new Vector3( 0.707107f, -0.707107f,  0.000000f),
        new Vector3( 0.831470f, -0.555570f,  0.000000f),
        new Vector3( 0.923880f, -0.382683f,  0.000000f),
        new Vector3( 0.980785f, -0.195090f,  0.000000f),
        // z = -0.195090
        new Vector3( 0.980785f,  0.000000f, -0.195090f),
        new Vector3( 0.956195f,  0.218245f, -0.195090f),
        new Vector3( 0.883657f,  0.425547f, -0.195090f),
        new Vector3( 0.766809f,  0.611510f, -0.195090f),
        new Vector3( 0.611510f,  0.766809f, -0.195090f),
        new Vector3( 0.425547f,  0.883657f, -0.195090f),
        new Vector3( 0.218245f,  0.956195f, -0.195090f),
        new Vector3(-0.000000f,  0.980785f, -0.195090f),
        new Vector3(-0.218245f,  0.956195f, -0.195090f),
        new Vector3(-0.425547f,  0.883657f, -0.195090f),
        new Vector3(-0.611510f,  0.766809f, -0.195090f),
        new Vector3(-0.766809f,  0.611510f, -0.195090f),
        new Vector3(-0.883657f,  0.425547f, -0.195090f),
        new Vector3(-0.956195f,  0.218245f, -0.195090f),
        new Vector3(-0.980785f, -0.000000f, -0.195090f),
        new Vector3(-0.956195f, -0.218245f, -0.195090f),
        new Vector3(-0.883657f, -0.425547f, -0.195090f),
        new Vector3(-0.766809f, -0.611510f, -0.195090f),
        new Vector3(-0.611510f, -0.766809f, -0.195090f),
        new Vector3(-0.425547f, -0.883657f, -0.195090f),
        new Vector3(-0.218245f, -0.956195f, -0.195090f),
        new Vector3( 0.000000f, -0.980785f, -0.195090f),
        new Vector3( 0.218245f, -0.956195f, -0.195090f),
        new Vector3( 0.425547f, -0.883657f, -0.195090f),
        new Vector3( 0.611510f, -0.766809f, -0.195090f),
        new Vector3( 0.766809f, -0.611510f, -0.195090f),
        new Vector3( 0.883657f, -0.425547f, -0.195090f),
        new Vector3( 0.956195f, -0.218245f, -0.195090f),
        // z = -0.382683
        new Vector3( 0.923880f,  0.000000f, -0.382683f),
        new Vector3( 0.892399f,  0.239118f, -0.382683f),
        new Vector3( 0.800103f,  0.461940f, -0.382683f),
        new Vector3( 0.653281f,  0.653281f, -0.382683f),
        new Vector3( 0.461940f,  0.800103f, -0.382683f),
        new Vector3( 0.239118f,  0.892399f, -0.382683f),
        new Vector3(-0.000000f,  0.923880f, -0.382683f),
        new Vector3(-0.239118f,  0.892399f, -0.382683f),
        new Vector3(-0.461940f,  0.800103f, -0.382683f),
        new Vector3(-0.653281f,  0.653281f, -0.382683f),
        new Vector3(-0.800103f,  0.461940f, -0.382683f),
        new Vector3(-0.892399f,  0.239118f, -0.382683f),
        new Vector3(-0.923880f, -0.000000f, -0.382683f),
        new Vector3(-0.892399f, -0.239118f, -0.382683f),
        new Vector3(-0.800103f, -0.461940f, -0.382683f),
        new Vector3(-0.653282f, -0.653281f, -0.382683f),
        new Vector3(-0.461940f, -0.800103f, -0.382683f),
        new Vector3(-0.239118f, -0.892399f, -0.382683f),
        new Vector3( 0.000000f, -0.923880f, -0.382683f),
        new Vector3( 0.239118f, -0.892399f, -0.382683f),
        new Vector3( 0.461940f, -0.800103f, -0.382683f),
        new Vector3( 0.653281f, -0.653282f, -0.382683f),
        new Vector3( 0.800103f, -0.461940f, -0.382683f),
        new Vector3( 0.892399f, -0.239117f, -0.382683f),
        // z = -0.555570
        new Vector3( 0.831470f,  0.000000f, -0.555570f),
        new Vector3( 0.790775f,  0.256938f, -0.555570f),
        new Vector3( 0.672673f,  0.488726f, -0.555570f),
        new Vector3( 0.488726f,  0.672673f, -0.555570f),
        new Vector3( 0.256938f,  0.790775f, -0.555570f),
        new Vector3(-0.000000f,  0.831470f, -0.555570f),
        new Vector3(-0.256938f,  0.790775f, -0.555570f),
        new Vector3(-0.488726f,  0.672673f, -0.555570f),
        new Vector3(-0.672673f,  0.488726f, -0.555570f),
        new Vector3(-0.790775f,  0.256938f, -0.555570f),
        new Vector3(-0.831470f, -0.000000f, -0.555570f),
        new Vector3(-0.790775f, -0.256938f, -0.555570f),
        new Vector3(-0.672673f, -0.488726f, -0.555570f),
        new Vector3(-0.488725f, -0.672673f, -0.555570f),
        new Vector3(-0.256938f, -0.790775f, -0.555570f),
        new Vector3( 0.000000f, -0.831470f, -0.555570f),
        new Vector3( 0.256938f, -0.790775f, -0.555570f),
        new Vector3( 0.488725f, -0.672673f, -0.555570f),
        new Vector3( 0.672673f, -0.488726f, -0.555570f),
        new Vector3( 0.790775f, -0.256938f, -0.555570f),
        // z = -0.707107
        new Vector3( 0.707107f,  0.000000f, -0.707107f),
        new Vector3( 0.653281f,  0.270598f, -0.707107f),
        new Vector3( 0.500000f,  0.500000f, -0.707107f),
        new Vector3( 0.270598f,  0.653281f, -0.707107f),
        new Vector3(-0.000000f,  0.707107f, -0.707107f),
        new Vector3(-0.270598f,  0.653282f, -0.707107f),
        new Vector3(-0.500000f,  0.500000f, -0.707107f),
        new Vector3(-0.653281f,  0.270598f, -0.707107f),
        new Vector3(-0.707107f, -0.000000f, -0.707107f),
        new Vector3(-0.653281f, -0.270598f, -0.707107f),
        new Vector3(-0.500000f, -0.500000f, -0.707107f),
        new Vector3(-0.270598f, -0.653281f, -0.707107f),
        new Vector3( 0.000000f, -0.707107f, -0.707107f),
        new Vector3( 0.270598f, -0.653281f, -0.707107f),
        new Vector3( 0.500000f, -0.500000f, -0.707107f),
        new Vector3( 0.653282f, -0.270598f, -0.707107f),
        // z = -0.831470
        new Vector3( 0.555570f,  0.000000f, -0.831470f),
        new Vector3( 0.481138f,  0.277785f, -0.831470f),
        new Vector3( 0.277785f,  0.481138f, -0.831470f),
        new Vector3(-0.000000f,  0.555570f, -0.831470f),
        new Vector3(-0.277785f,  0.481138f, -0.831470f),
        new Vector3(-0.481138f,  0.277785f, -0.831470f),
        new Vector3(-0.555570f, -0.000000f, -0.831470f),
        new Vector3(-0.481138f, -0.277785f, -0.831470f),
        new Vector3(-0.277785f, -0.481138f, -0.831470f),
        new Vector3( 0.000000f, -0.555570f, -0.831470f),
        new Vector3( 0.277785f, -0.481138f, -0.831470f),
        new Vector3( 0.481138f, -0.277785f, -0.831470f),
        // z = -0.923880
        new Vector3( 0.382683f,  0.000000f, -0.923880f),
        new Vector3( 0.270598f,  0.270598f, -0.923880f),
        new Vector3(-0.000000f,  0.382683f, -0.923880f),
        new Vector3(-0.270598f,  0.270598f, -0.923880f),
        new Vector3(-0.382683f, -0.000000f, -0.923880f),
        new Vector3(-0.270598f, -0.270598f, -0.923880f),
        new Vector3( 0.000000f, -0.382683f, -0.923880f),
        new Vector3( 0.270598f, -0.270598f, -0.923880f),
        // z = -0.980785
        new Vector3( 0.195090f,  0.000000f, -0.980785f),
        new Vector3(-0.000000f,  0.195090f, -0.980785f),
        new Vector3(-0.195090f, -0.000000f, -0.980785f),
        new Vector3( 0.000000f, -0.195090f, -0.980785f),
        // z = +0.195090
        new Vector3( 0.980785f,  0.000000f,  0.195090f),
        new Vector3( 0.956195f,  0.218245f,  0.195090f),
        new Vector3( 0.883657f,  0.425547f,  0.195090f),
        new Vector3( 0.766809f,  0.611510f,  0.195090f),
        new Vector3( 0.611510f,  0.766809f,  0.195090f),
        new Vector3( 0.425547f,  0.883657f,  0.195090f),
        new Vector3( 0.218245f,  0.956195f,  0.195090f),
        new Vector3(-0.000000f,  0.980785f,  0.195090f),
        new Vector3(-0.218245f,  0.956195f,  0.195090f),
        new Vector3(-0.425547f,  0.883657f,  0.195090f),
        new Vector3(-0.611510f,  0.766809f,  0.195090f),
        new Vector3(-0.766809f,  0.611510f,  0.195090f),
        new Vector3(-0.883657f,  0.425547f,  0.195090f),
        new Vector3(-0.956195f,  0.218245f,  0.195090f),
        new Vector3(-0.980785f, -0.000000f,  0.195090f),
        new Vector3(-0.956195f, -0.218245f,  0.195090f),
        new Vector3(-0.883657f, -0.425547f,  0.195090f),
        new Vector3(-0.766809f, -0.611510f,  0.195090f),
        new Vector3(-0.611510f, -0.766809f,  0.195090f),
        new Vector3(-0.425547f, -0.883657f,  0.195090f),
        new Vector3(-0.218245f, -0.956195f,  0.195090f),
        new Vector3( 0.000000f, -0.980785f,  0.195090f),
        new Vector3( 0.218245f, -0.956195f,  0.195090f),
        new Vector3( 0.425547f, -0.883657f,  0.195090f),
        new Vector3( 0.611510f, -0.766809f,  0.195090f),
        new Vector3( 0.766809f, -0.611510f,  0.195090f),
        new Vector3( 0.883657f, -0.425547f,  0.195090f),
        new Vector3( 0.956195f, -0.218245f,  0.195090f),
        // z = +0.382683
        new Vector3( 0.923880f,  0.000000f,  0.382683f),
        new Vector3( 0.892399f,  0.239118f,  0.382683f),
        new Vector3( 0.800103f,  0.461940f,  0.382683f),
        new Vector3( 0.653281f,  0.653281f,  0.382683f),
        new Vector3( 0.461940f,  0.800103f,  0.382683f),
        new Vector3( 0.239118f,  0.892399f,  0.382683f),
        new Vector3(-0.000000f,  0.923880f,  0.382683f),
        new Vector3(-0.239118f,  0.892399f,  0.382683f),
        new Vector3(-0.461940f,  0.800103f,  0.382683f),
        new Vector3(-0.653281f,  0.653281f,  0.382683f),
        new Vector3(-0.800103f,  0.461940f,  0.382683f),
        new Vector3(-0.892399f,  0.239118f,  0.382683f),
        new Vector3(-0.923880f, -0.000000f,  0.382683f),
        new Vector3(-0.892399f, -0.239118f,  0.382683f),
        new Vector3(-0.800103f, -0.461940f,  0.382683f),
        new Vector3(-0.653282f, -0.653281f,  0.382683f),
        new Vector3(-0.461940f, -0.800103f,  0.382683f),
        new Vector3(-0.239118f, -0.892399f,  0.382683f),
        new Vector3( 0.000000f, -0.923880f,  0.382683f),
        new Vector3( 0.239118f, -0.892399f,  0.382683f),
        new Vector3( 0.461940f, -0.800103f,  0.382683f),
        new Vector3( 0.653281f, -0.653282f,  0.382683f),
        new Vector3( 0.800103f, -0.461940f,  0.382683f),
        new Vector3( 0.892399f, -0.239117f,  0.382683f),
        // z = +0.555570
        new Vector3( 0.831470f,  0.000000f,  0.555570f),
        new Vector3( 0.790775f,  0.256938f,  0.555570f),
        new Vector3( 0.672673f,  0.488726f,  0.555570f),
        new Vector3( 0.488726f,  0.672673f,  0.555570f),
        new Vector3( 0.256938f,  0.790775f,  0.555570f),
        new Vector3(-0.000000f,  0.831470f,  0.555570f),
        new Vector3(-0.256938f,  0.790775f,  0.555570f),
        new Vector3(-0.488726f,  0.672673f,  0.555570f),
        new Vector3(-0.672673f,  0.488726f,  0.555570f),
        new Vector3(-0.790775f,  0.256938f,  0.555570f),
        new Vector3(-0.831470f, -0.000000f,  0.555570f),
        new Vector3(-0.790775f, -0.256938f,  0.555570f),
        new Vector3(-0.672673f, -0.488726f,  0.555570f),
        new Vector3(-0.488725f, -0.672673f,  0.555570f),
        new Vector3(-0.256938f, -0.790775f,  0.555570f),
        new Vector3( 0.000000f, -0.831470f,  0.555570f),
        new Vector3( 0.256938f, -0.790775f,  0.555570f),
        new Vector3( 0.488725f, -0.672673f,  0.555570f),
        new Vector3( 0.672673f, -0.488726f,  0.555570f),
        new Vector3( 0.790775f, -0.256938f,  0.555570f),
        // z = +0.707107
        new Vector3( 0.707107f,  0.000000f,  0.707107f),
        new Vector3( 0.653281f,  0.270598f,  0.707107f),
        new Vector3( 0.500000f,  0.500000f,  0.707107f),
        new Vector3( 0.270598f,  0.653281f,  0.707107f),
        new Vector3(-0.000000f,  0.707107f,  0.707107f),
        new Vector3(-0.270598f,  0.653282f,  0.707107f),
        new Vector3(-0.500000f,  0.500000f,  0.707107f),
        new Vector3(-0.653281f,  0.270598f,  0.707107f),
        new Vector3(-0.707107f, -0.000000f,  0.707107f),
        new Vector3(-0.653281f, -0.270598f,  0.707107f),
        new Vector3(-0.500000f, -0.500000f,  0.707107f),
        new Vector3(-0.270598f, -0.653281f,  0.707107f),
        new Vector3( 0.000000f, -0.707107f,  0.707107f),
        new Vector3( 0.270598f, -0.653281f,  0.707107f),
        new Vector3( 0.500000f, -0.500000f,  0.707107f),
        new Vector3( 0.653282f, -0.270598f,  0.707107f),
        // z = +0.831470
        new Vector3( 0.555570f,  0.000000f,  0.831470f),
        new Vector3( 0.481138f,  0.277785f,  0.831470f),
        new Vector3( 0.277785f,  0.481138f,  0.831470f),
        new Vector3(-0.000000f,  0.555570f,  0.831470f),
        new Vector3(-0.277785f,  0.481138f,  0.831470f),
        new Vector3(-0.481138f,  0.277785f,  0.831470f),
        new Vector3(-0.555570f, -0.000000f,  0.831470f),
        new Vector3(-0.481138f, -0.277785f,  0.831470f),
        new Vector3(-0.277785f, -0.481138f,  0.831470f),
        new Vector3( 0.000000f, -0.555570f,  0.831470f),
        new Vector3( 0.277785f, -0.481138f,  0.831470f),
        new Vector3( 0.481138f, -0.277785f,  0.831470f),
        // z = +0.923880
        new Vector3( 0.382683f,  0.000000f,  0.923880f),
        new Vector3( 0.270598f,  0.270598f,  0.923880f),
        new Vector3(-0.000000f,  0.382683f,  0.923880f),
        new Vector3(-0.270598f,  0.270598f,  0.923880f),
        new Vector3(-0.382683f, -0.000000f,  0.923880f),
        new Vector3(-0.270598f, -0.270598f,  0.923880f),
        new Vector3( 0.000000f, -0.382683f,  0.923880f),
        new Vector3( 0.270598f, -0.270598f,  0.923880f),
        // z = +0.980785
        new Vector3( 0.195090f,  0.000000f,  0.980785f),
        new Vector3(-0.000000f,  0.195090f,  0.980785f),
        new Vector3(-0.195090f, -0.000000f,  0.980785f),
        new Vector3( 0.000000f, -0.195090f,  0.980785f),
    };

    // -----------------------------------------------------------------------
    // Public entry point
    // -----------------------------------------------------------------------

    /// <summary>
    /// Decode an MDC file and return a fully-expanded Md3Data.
    /// All per-frame vertex positions and normals are decoded and stored in
    /// surf.FramePositions[f] / surf.FrameNormals[f] for every model frame f.
    /// surf.Positions / surf.Normals point to frame 0.
    /// </summary>
    public static Md3Data Load(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var r  = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);

        // ----------------------------------------------------------------
        // MDC header (52 bytes of ints + 64-byte name)
        // ----------------------------------------------------------------
        int ident = r.ReadInt32();
        if (ident != MDC_IDENT)
            throw new Exception($"MDC: bad ident 0x{ident:X8} (expected 0x{MDC_IDENT:X8})");

        int version = r.ReadInt32();
        // Accept version 2; warn but continue for other versions
        if (version != MDC_VERSION)
            Debug.LogWarning($"[MdcLoader] Unexpected MDC version {version} (expected {MDC_VERSION})");

        string name       = Md3Data.ReadFixedString(r, 64); // MAX_QPATH
        int    flags      = r.ReadInt32();
        int    numFrames  = r.ReadInt32();
        int    numTags    = r.ReadInt32();
        int    numSurfs   = r.ReadInt32();
        int    numSkins   = r.ReadInt32(); // unused

        int ofsFrames     = r.ReadInt32();
        int ofsTagNames   = r.ReadInt32();
        int ofsTags       = r.ReadInt32();
        int ofsSurfaces   = r.ReadInt32();
        int ofsEnd        = r.ReadInt32();

        Debug.Log($"[MdcLoader] '{name}': numFrames={numFrames} numTags={numTags} numSurfs={numSurfs} " +
                  $"ofsFrames={ofsFrames} ofsTagNames={ofsTagNames} ofsTags={ofsTags} " +
                  $"ofsSurfaces={ofsSurfaces} ofsEnd={ofsEnd} fileSize={data.Length}");

        var md3 = new Md3Data
        {
            Name      = name,
            NumFrames = numFrames,
            Frames    = new Md3Frame[numFrames],
            Tags      = new Md3Tag[numTags],
            Surfaces  = new Md3Surface[numSurfs],
        };

        // ----------------------------------------------------------------
        // Frames — same md3Frame_t layout as MD3 (56 bytes each)
        // bounds[0] vec3, bounds[1] vec3, localOrigin vec3, radius float, name char[16]
        // ----------------------------------------------------------------
        ms.Position = ofsFrames;
        for (int i = 0; i < numFrames; i++)
        {
            float minX = r.ReadSingle(); float minY = r.ReadSingle(); float minZ = r.ReadSingle();
            float maxX = r.ReadSingle(); float maxY = r.ReadSingle(); float maxZ = r.ReadSingle();
            float orgX = r.ReadSingle(); float orgY = r.ReadSingle(); float orgZ = r.ReadSingle();
            float rad  = r.ReadSingle();
            string fn  = Md3Data.ReadFixedString(r, 16);

            md3.Frames[i] = new Md3Frame
            {
                Mins        = Md3Data.EtToUnity(new Vector3(minX, minY, minZ)),
                Maxs        = Md3Data.EtToUnity(new Vector3(maxX, maxY, maxZ)),
                LocalOrigin = Md3Data.EtToUnity(new Vector3(orgX, orgY, orgZ)),
                Radius      = rad,
                Name        = fn,
            };
        }

        // ----------------------------------------------------------------
        // Tag names — numTags * char[64]
        // ----------------------------------------------------------------
        var tagNames = new string[numTags];
        ms.Position = ofsTagNames;
        for (int i = 0; i < numTags; i++)
            tagNames[i] = Md3Data.ReadFixedString(r, 64);

        // ----------------------------------------------------------------
        // Tags — numFrames * numTags * mdcTag_t (12 bytes each)
        // Layout: frame-major — all tags for frame 0, then frame 1, etc.
        // Format: short xyz[3] (pos*64), short angles[3] (PITCH=0, YAW=1, ROLL=2)
        // Read ALL frames so per-frame tag animation works for tag-only models.
        // ----------------------------------------------------------------
        ms.Position = ofsTags;
        var frameTags = new Md3Tag[numFrames][];
        for (int f = 0; f < numFrames; f++)
        {
            frameTags[f] = new Md3Tag[numTags];
            for (int i = 0; i < numTags; i++)
            {
                short tx     = r.ReadInt16();
                short ty     = r.ReadInt16();
                short tz     = r.ReadInt16();
                short aPitch = r.ReadInt16(); // angles[0] = PITCH
                short aYaw   = r.ReadInt16(); // angles[1] = YAW
                short aRoll  = r.ReadInt16(); // angles[2] = ROLL

                var etPos = new Vector3(tx / 64f, ty / 64f, tz / 64f);
                // SHORT2ANGLE: ET short angles are 65536 units per 360 degrees
                float pitch = aPitch * (360f / 65536f);
                float yaw   = aYaw   * (360f / 65536f);
                float roll  = aRoll  * (360f / 65536f);

                // Port of ET AngleVectors (q_math.c): produces forward/right/up in ET space
                float pR = pitch * Mathf.Deg2Rad;
                float yR = yaw   * Mathf.Deg2Rad;
                float rR = roll  * Mathf.Deg2Rad;
                float sp = Mathf.Sin(pR), cp = Mathf.Cos(pR);
                float sy = Mathf.Sin(yR), cy = Mathf.Cos(yR);
                float sr = Mathf.Sin(rR), cr = Mathf.Cos(rR);

                var etForward = new Vector3( cp*cy,               cp*sy,              -sp);
                var etRight   = new Vector3(-sr*sp*cy + cr*sy,  -sr*sp*sy - cr*cy,  -sr*cp);
                // q_math.c up[0] = cr*sp*cy + (-1)*(-sr)*(-sy) = cr*sp*cy - sr*sy
                // q_math.c up[1] = cr*sp*sy + (-1)*(-sr)*(cy)  = cr*sp*sy + sr*cy
                var etUp      = new Vector3( cr*sp*cy - sr*sy,   cr*sp*sy + sr*cy,   cr*cp);

                // Convert to Unity space (EtToUnity: x=-et.y, y=et.z, z=et.x)
                // AxisX=right, AxisY=up, AxisZ=forward matches BuildMd3Object column layout
                frameTags[f][i] = new Md3Tag
                {
                    Name   = tagNames[i],
                    Origin = Md3Data.EtToUnity(etPos),
                    AxisX  = Md3Data.EtToUnity(etRight),    // Unity X (right)
                    AxisY  = Md3Data.EtToUnity(etUp),       // Unity Y (up)
                    AxisZ  = Md3Data.EtToUnity(etForward),  // Unity Z (forward)
                };
            }
        }
        if (numFrames > 0) md3.Tags = frameTags[0];
        md3.FrameTags = frameTags;

        // ----------------------------------------------------------------
        // Surfaces
        // ----------------------------------------------------------------
        long surfBase = ofsSurfaces;
        for (int si = 0; si < numSurfs; si++)
        {
            ms.Position = surfBase;
            md3.Surfaces[si] = ReadSurface(r, ms, surfBase, numFrames);
            surfBase += md3.Surfaces[si]._ofsEnd;
        }

        return md3;
    }

    // -----------------------------------------------------------------------
    // Surface reader
    // -----------------------------------------------------------------------
    private static Md3Surface ReadSurface(BinaryReader r, MemoryStream ms, long surfStart, int numModelFrames)
    {
        // mdcSurface_t header — 124 bytes (17 ints + char[64])
        int   surfIdent          = r.ReadInt32();
        string surfName          = Md3Data.ReadFixedString(r, 64);
        int   surfFlags          = r.ReadInt32();
        int   numCompFrames      = r.ReadInt32();
        int   numBaseFrames      = r.ReadInt32();
        int   numShaders         = r.ReadInt32();
        int   numVerts           = r.ReadInt32();
        int   numTriangles       = r.ReadInt32();
        Debug.Log($"[MdcLoader] surf '{surfName}': numBaseFrames={numBaseFrames} numCompFrames={numCompFrames} numVerts={numVerts} numTris={numTriangles}");
        int   ofsTriangles       = r.ReadInt32();
        int   ofsShaders         = r.ReadInt32();
        int   ofsST              = r.ReadInt32();
        int   ofsXyzNormals      = r.ReadInt32(); // base frames: numVerts * numBaseFrames * 8 bytes
        int   ofsXyzCompressed   = r.ReadInt32(); // comp frames: numVerts * numCompFrames * 4 bytes
        int   ofsFrameBaseFrames = r.ReadInt32(); // numFrames * 2 bytes (short)
        int   ofsFrameCompFrames = r.ReadInt32(); // numFrames * 2 bytes (short)
        int   surfOfsEnd         = r.ReadInt32();

        var surf = new Md3Surface
        {
            Name           = surfName,
            ShaderNames    = new string[numShaders],
            Indexes        = new int[numTriangles * 3],
            UVs            = new Vector2[numVerts],
            Positions      = new Vector3[numVerts],
            Normals        = new Vector3[numVerts],
            FramePositions = new Vector3[numModelFrames][],
            FrameNormals   = new Vector3[numModelFrames][],
            _ofsEnd        = surfOfsEnd,
        };

        // ---- Shaders (68 bytes each: char[64] name + int shaderIndex) ----
        ms.Position = surfStart + ofsShaders;
        for (int i = 0; i < numShaders; i++)
        {
            surf.ShaderNames[i] = Md3Data.ReadFixedString(r, 64);
            r.ReadInt32(); // shaderIndex — unused at load time
        }

        // ---- Triangles (12 bytes each: int[3] indices) -------------------
        ms.Position = surfStart + ofsTriangles;
        for (int i = 0; i < numTriangles; i++)
        {
            surf.Indexes[i * 3 + 0] = r.ReadInt32();
            surf.Indexes[i * 3 + 1] = r.ReadInt32();
            surf.Indexes[i * 3 + 2] = r.ReadInt32();
        }

        // ---- Texture coordinates (8 bytes each: float[2]) ----------------
        ms.Position = surfStart + ofsST;
        for (int i = 0; i < numVerts; i++)
        {
            float u = r.ReadSingle();
            float v = r.ReadSingle();
            surf.UVs[i] = new Vector2(u, 1f - v); // ET/OpenGL V=0 bottom → Unity V=0 top
        }

        // ---- Read all base-frame XyzNormals into a flat array ------------
        // Layout: numBaseFrames * numVerts * 8 bytes (short x,y,z,normal)
        var basePositions = new Vector3[numBaseFrames * numVerts];
        var baseNormals   = new Vector3[numBaseFrames * numVerts];

        ms.Position = surfStart + ofsXyzNormals;
        for (int bf = 0; bf < numBaseFrames; bf++)
        {
            for (int v = 0; v < numVerts; v++)
            {
                short sx = r.ReadInt16();
                short sy = r.ReadInt16();
                short sz = r.ReadInt16();
                short sn = r.ReadInt16();

                var etPos = new Vector3(sx / 64f, sy / 64f, sz / 64f);
                basePositions[bf * numVerts + v] = Md3Data.EtToUnity(etPos);
                baseNormals  [bf * numVerts + v] = Md3Data.DecodeNormal(sn);
            }
        }

        // ---- Read all compressed-frame XyzCompressed into a flat array ---
        // Layout: numCompFrames * numVerts * 4 bytes (uint32 ofsVec)
        var compOfsVec = new uint[numCompFrames * numVerts];

        if (numCompFrames > 0)
        {
            ms.Position = surfStart + ofsXyzCompressed;
            for (int cf = 0; cf < numCompFrames; cf++)
                for (int v = 0; v < numVerts; v++)
                    compOfsVec[cf * numVerts + v] = r.ReadUInt32();
        }

        // ---- Read per-frame lookup tables --------------------------------
        // ofsFrameBaseFrames: numModelFrames * short — index into XyzNormals base array
        var frameBaseFrames = new short[numModelFrames];
        ms.Position = surfStart + ofsFrameBaseFrames;
        for (int f = 0; f < numModelFrames; f++)
            frameBaseFrames[f] = r.ReadInt16();

        // ofsFrameCompFrames: numModelFrames * short — index into XyzCompressed, or -1
        var frameCompFrames = new short[numModelFrames];
        ms.Position = surfStart + ofsFrameCompFrames;
        for (int f = 0; f < numModelFrames; f++)
            frameCompFrames[f] = r.ReadInt16();

        // ---- Expand all model frames -------------------------------------
        for (int f = 0; f < numModelFrames; f++)
        {
            int   baseIdx = frameBaseFrames[f];
            short compIdx = frameCompFrames[f];

            var fpos = new Vector3[numVerts];
            var fnrm = new Vector3[numVerts];

            for (int v = 0; v < numVerts; v++)
            {
                // Base position and normal from the XyzNormals base frame
                Vector3 bPos = basePositions[baseIdx * numVerts + v];
                Vector3 bNrm = baseNormals  [baseIdx * numVerts + v];

                if (compIdx >= 0)
                {
                    // Compressed delta — decode and add to base position
                    uint ofsVec = compOfsVec[(int)compIdx * numVerts + v];

                    float dx = ((ofsVec       & 0xFF) - 127f) * 0.05f;
                    float dy = ((ofsVec >>  8 & 0xFF) - 127f) * 0.05f;
                    float dz = ((ofsVec >> 16 & 0xFF) - 127f) * 0.05f;

                    // Delta is in ET space; convert to Unity then add to base
                    var etDelta = new Vector3(dx, dy, dz);
                    fpos[v] = bPos + Md3Data.EtToUnity(etDelta);

                    // Normal from anormals table — already ET-space, convert to Unity
                    int normIdx = (int)((ofsVec >> 24) & 0xFF);
                    fnrm[v] = Md3Data.EtToUnity(AnormsTable[normIdx]);
                }
                else
                {
                    // Pure base frame — use base position and MD3-packed normal
                    fpos[v] = bPos;
                    fnrm[v] = bNrm;
                }
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
}
