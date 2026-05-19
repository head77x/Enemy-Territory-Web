// Ported from: src/game/q_math.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;

namespace ET.Core
{
    // ET coordinate conventions differ from Unity:
    //   ET:    X=forward, Y=left,  Z=up   (right-handed, Z-up)
    //   Unity: X=right,   Y=up,    Z=forward (left-handed, Y-up)
    // This class preserves all ET math exactly as-is for gameplay logic.
    // Coordinate conversion to Unity space is done at the rendering boundary only.
    public static class ETMath
    {
        // Angle component indices (ET convention)
        public const int PITCH = 0;
        public const int YAW   = 1;
        public const int ROLL  = 2;

        public const int NUMVERTEXNORMALS = 162;

        public static readonly Vector3[] ByteDirs = new Vector3[NUMVERTEXNORMALS]
        {
            new(-0.525731f,  0.000000f,  0.850651f), new(-0.442863f,  0.238856f,  0.864188f),
            new(-0.295242f,  0.000000f,  0.955423f), new(-0.309017f,  0.500000f,  0.809017f),
            new(-0.162460f,  0.262866f,  0.951056f), new( 0.000000f,  0.000000f,  1.000000f),
            new( 0.000000f,  0.850651f,  0.525731f), new(-0.147621f,  0.716567f,  0.681718f),
            new( 0.147621f,  0.716567f,  0.681718f), new( 0.000000f,  0.525731f,  0.850651f),
            new( 0.309017f,  0.500000f,  0.809017f), new( 0.525731f,  0.000000f,  0.850651f),
            new( 0.295242f,  0.000000f,  0.955423f), new( 0.442863f,  0.238856f,  0.864188f),
            new( 0.162460f,  0.262866f,  0.951056f), new(-0.681718f,  0.147621f,  0.716567f),
            new(-0.809017f,  0.309017f,  0.500000f), new(-0.587785f,  0.425325f,  0.688191f),
            new(-0.850651f,  0.525731f,  0.000000f), new(-0.864188f,  0.442863f,  0.238856f),
            new(-0.716567f,  0.681718f,  0.147621f), new(-0.688191f,  0.587785f,  0.425325f),
            new(-0.500000f,  0.809017f,  0.309017f), new(-0.238856f,  0.864188f,  0.442863f),
            new(-0.425325f,  0.688191f,  0.587785f), new(-0.716567f,  0.681718f, -0.147621f),
            new(-0.500000f,  0.809017f, -0.309017f), new(-0.525731f,  0.850651f,  0.000000f),
            new( 0.000000f,  0.850651f, -0.525731f), new(-0.238856f,  0.864188f, -0.442863f),
            new( 0.000000f,  0.955423f, -0.295242f), new(-0.262866f,  0.951056f, -0.162460f),
            new( 0.000000f,  1.000000f,  0.000000f), new( 0.000000f,  0.955423f,  0.295242f),
            new(-0.262866f,  0.951056f,  0.162460f), new( 0.238856f,  0.864188f,  0.442863f),
            new( 0.262866f,  0.951056f,  0.162460f), new( 0.500000f,  0.809017f,  0.309017f),
            new( 0.238856f,  0.864188f, -0.442863f), new( 0.262866f,  0.951056f, -0.162460f),
            new( 0.500000f,  0.809017f, -0.309017f), new( 0.850651f,  0.525731f,  0.000000f),
            new( 0.716567f,  0.681718f,  0.147621f), new( 0.716567f,  0.681718f, -0.147621f),
            new( 0.525731f,  0.850651f,  0.000000f), new( 0.425325f,  0.688191f,  0.587785f),
            new( 0.864188f,  0.442863f,  0.238856f), new( 0.688191f,  0.587785f,  0.425325f),
            new( 0.809017f,  0.309017f,  0.500000f), new( 0.681718f,  0.147621f,  0.716567f),
            new( 0.587785f,  0.425325f,  0.688191f), new( 0.955423f,  0.295242f,  0.000000f),
            new( 1.000000f,  0.000000f,  0.000000f), new( 0.951056f,  0.162460f,  0.262866f),
            new( 0.850651f, -0.525731f,  0.000000f), new( 0.955423f, -0.295242f,  0.000000f),
            new( 0.864188f, -0.442863f,  0.238856f), new( 0.951056f, -0.162460f,  0.262866f),
            new( 0.809017f, -0.309017f,  0.500000f), new( 0.681718f, -0.147621f,  0.716567f),
            new( 0.850651f,  0.000000f,  0.525731f), new( 0.864188f,  0.442863f, -0.238856f),
            new( 0.809017f,  0.309017f, -0.500000f), new( 0.951056f,  0.162460f, -0.262866f),
            new( 0.525731f,  0.000000f, -0.850651f), new( 0.681718f,  0.147621f, -0.716567f),
            new( 0.681718f, -0.147621f, -0.716567f), new( 0.850651f,  0.000000f, -0.525731f),
            new( 0.809017f, -0.309017f, -0.500000f), new( 0.864188f, -0.442863f, -0.238856f),
            new( 0.951056f, -0.162460f, -0.262866f), new( 0.147621f,  0.716567f, -0.681718f),
            new( 0.309017f,  0.500000f, -0.809017f), new( 0.425325f,  0.688191f, -0.587785f),
            new( 0.442863f,  0.238856f, -0.864188f), new( 0.587785f,  0.425325f, -0.688191f),
            new( 0.688191f,  0.587785f, -0.425325f), new(-0.147621f,  0.716567f, -0.681718f),
            new(-0.309017f,  0.500000f, -0.809017f), new( 0.000000f,  0.525731f, -0.850651f),
            new(-0.525731f,  0.000000f, -0.850651f), new(-0.442863f,  0.238856f, -0.864188f),
            new(-0.295242f,  0.000000f, -0.955423f), new(-0.162460f,  0.262866f, -0.951056f),
            new( 0.000000f,  0.000000f, -1.000000f), new( 0.295242f,  0.000000f, -0.955423f),
            new( 0.162460f,  0.262866f, -0.951056f), new(-0.442863f, -0.238856f, -0.864188f),
            new(-0.309017f, -0.500000f, -0.809017f), new(-0.162460f, -0.262866f, -0.951056f),
            new( 0.000000f, -0.850651f, -0.525731f), new(-0.147621f, -0.716567f, -0.681718f),
            new( 0.147621f, -0.716567f, -0.681718f), new( 0.000000f, -0.525731f, -0.850651f),
            new( 0.309017f, -0.500000f, -0.809017f), new( 0.442863f, -0.238856f, -0.864188f),
            new( 0.162460f, -0.262866f, -0.951056f), new( 0.238856f, -0.864188f, -0.442863f),
            new( 0.500000f, -0.809017f, -0.309017f), new( 0.425325f, -0.688191f, -0.587785f),
            new( 0.716567f, -0.681718f, -0.147621f), new( 0.688191f, -0.587785f, -0.425325f),
            new( 0.587785f, -0.425325f, -0.688191f), new( 0.000000f, -0.955423f, -0.295242f),
            new( 0.000000f, -1.000000f,  0.000000f), new( 0.262866f, -0.951056f, -0.162460f),
            new( 0.000000f, -0.850651f,  0.525731f), new( 0.000000f, -0.955423f,  0.295242f),
            new( 0.238856f, -0.864188f,  0.442863f), new( 0.262866f, -0.951056f,  0.162460f),
            new( 0.500000f, -0.809017f,  0.309017f), new( 0.716567f, -0.681718f,  0.147621f),
            new( 0.525731f, -0.850651f,  0.000000f), new(-0.238856f, -0.864188f, -0.442863f),
            new(-0.500000f, -0.809017f, -0.309017f), new(-0.262866f, -0.951056f, -0.162460f),
            new(-0.850651f, -0.525731f,  0.000000f), new(-0.716567f, -0.681718f, -0.147621f),
            new(-0.716567f, -0.681718f,  0.147621f), new(-0.525731f, -0.850651f,  0.000000f),
            new(-0.500000f, -0.809017f,  0.309017f), new(-0.238856f, -0.864188f,  0.442863f),
            new(-0.262866f, -0.951056f,  0.162460f), new(-0.864188f, -0.442863f,  0.238856f),
            new(-0.809017f, -0.309017f,  0.500000f), new(-0.688191f, -0.587785f,  0.425325f),
            new(-0.681718f, -0.147621f,  0.716567f), new(-0.442863f, -0.238856f,  0.864188f),
            new(-0.587785f, -0.425325f,  0.688191f), new(-0.309017f, -0.500000f,  0.809017f),
            new(-0.147621f, -0.716567f,  0.681718f), new(-0.425325f, -0.688191f,  0.587785f),
            new(-0.162460f, -0.262866f,  0.951056f), new( 0.442863f, -0.238856f,  0.864188f),
            new( 0.162460f, -0.262866f,  0.951056f), new( 0.309017f, -0.500000f,  0.809017f),
            new( 0.147621f, -0.716567f,  0.681718f), new( 0.000000f, -0.525731f,  0.850651f),
            new( 0.425325f, -0.688191f,  0.587785f), new( 0.587785f, -0.425325f,  0.688191f),
            new( 0.688191f, -0.587785f,  0.425325f), new(-0.955423f,  0.295242f,  0.000000f),
            new(-0.951056f,  0.162460f,  0.262866f), new(-1.000000f,  0.000000f,  0.000000f),
            new(-0.850651f,  0.000000f,  0.525731f), new(-0.955423f, -0.295242f,  0.000000f),
            new(-0.951056f, -0.162460f,  0.262866f), new(-0.864188f,  0.442863f, -0.238856f),
            new(-0.951056f,  0.162460f, -0.262866f), new(-0.809017f,  0.309017f, -0.500000f),
            new(-0.864188f, -0.442863f, -0.238856f), new(-0.951056f, -0.162460f, -0.262866f),
            new(-0.809017f, -0.309017f, -0.500000f), new(-0.681718f,  0.147621f, -0.716567f),
            new(-0.681718f, -0.147621f, -0.716567f), new(-0.850651f,  0.000000f, -0.525731f),
            new(-0.688191f,  0.587785f, -0.425325f), new(-0.587785f,  0.425325f, -0.688191f),
            new(-0.425325f,  0.688191f, -0.587785f), new(-0.425325f, -0.688191f, -0.587785f),
            new(-0.587785f, -0.425325f, -0.688191f), new(-0.688191f, -0.587785f, -0.425325f),
        };

        // ET color table (indexed by ^N chat color codes)
        public static readonly Color[] ColorTable = new Color[32]
        {
            new(0.0f,  0.0f,   0.0f,  1.0f), // 0 black
            new(1.0f,  0.0f,   0.0f,  1.0f), // 1 red
            new(0.0f,  1.0f,   0.0f,  1.0f), // 2 green
            new(1.0f,  1.0f,   0.0f,  1.0f), // 3 yellow
            new(0.0f,  0.0f,   1.0f,  1.0f), // 4 blue
            new(0.0f,  1.0f,   1.0f,  1.0f), // 5 cyan
            new(1.0f,  0.0f,   1.0f,  1.0f), // 6 purple
            new(1.0f,  1.0f,   1.0f,  1.0f), // 7 white
            new(1.0f,  0.5f,   0.0f,  1.0f), // 8 orange
            new(0.5f,  0.5f,   0.5f,  1.0f), // 9 md.grey
            new(0.75f, 0.75f,  0.75f, 1.0f), // : lt.grey
            new(0.75f, 0.75f,  0.75f, 1.0f), // ; lt.grey
            new(0.0f,  0.5f,   0.0f,  1.0f), // < md.green
            new(0.5f,  0.5f,   0.0f,  1.0f), // = md.yellow
            new(0.0f,  0.0f,   0.5f,  1.0f), // > md.blue
            new(0.5f,  0.0f,   0.0f,  1.0f), // ? md.red
            new(0.5f,  0.25f,  0.0f,  1.0f), // @ md.orange
            new(1.0f,  0.6f,   0.1f,  1.0f), // A lt.orange
            new(0.0f,  0.5f,   0.5f,  1.0f), // B md.cyan
            new(0.5f,  0.0f,   0.5f,  1.0f), // C md.purple
            new(0.0f,  0.5f,   1.0f,  1.0f), // D
            new(0.5f,  0.0f,   1.0f,  1.0f), // E
            new(0.2f,  0.6f,   0.8f,  1.0f), // F
            new(0.8f,  1.0f,   0.8f,  1.0f), // G
            new(0.0f,  0.4f,   0.2f,  1.0f), // H
            new(1.0f,  0.0f,   0.2f,  1.0f), // I
            new(0.7f,  0.1f,   0.1f,  1.0f), // J
            new(0.6f,  0.2f,   0.0f,  1.0f), // K
            new(0.8f,  0.6f,   0.2f,  1.0f), // L
            new(0.6f,  0.6f,   0.2f,  1.0f), // M
            new(1.0f,  1.0f,   0.75f, 1.0f), // N
            new(1.0f,  1.0f,   0.5f,  1.0f), // O
        };

        // -------------------------------------------------------
        // Random number generation (seed-based, matches ET behavior)
        // -------------------------------------------------------

        public static int Q_rand(ref int seed)
        {
            seed = 69069 * seed + 1;
            return seed;
        }

        public static float Q_random(ref int seed)
        {
            return (Q_rand(ref seed) & 0xffff) / (float)0x10000;
        }

        public static float Q_crandom(ref int seed)
        {
            return 2.0f * (Q_random(ref seed) - 0.5f);
        }

        // -------------------------------------------------------
        // Clamp helpers
        // -------------------------------------------------------

        public static sbyte ClampChar(int i)
        {
            if (i < -128) return -128;
            if (i >  127) return  127;
            return (sbyte)i;
        }

        public static short ClampShort(int i)
        {
            if (i < -32768) return -32768;
            if (i >  32767) return  32767;
            return (short)i;
        }

        // -------------------------------------------------------
        // Normal compression: Vector3 <-> byte index
        // -------------------------------------------------------

        public static int DirToByte(Vector3 dir)
        {
            float bestd = 0f;
            int best = 0;
            for (int i = 0; i < NUMVERTEXNORMALS; i++)
            {
                float d = Vector3.Dot(dir, ByteDirs[i]);
                if (d > bestd)
                {
                    bestd = d;
                    best = i;
                }
            }
            return best;
        }

        public static Vector3 ByteToDir(int b)
        {
            if (b < 0 || b >= NUMVERTEXNORMALS) return Vector3.zero;
            return ByteDirs[b];
        }

        // -------------------------------------------------------
        // Color packing
        // -------------------------------------------------------

        public static uint ColorBytes3(float r, float g, float b)
        {
            return ((uint)(r * 255)) |
                   ((uint)(g * 255) << 8) |
                   ((uint)(b * 255) << 16);
        }

        public static uint ColorBytes4(float r, float g, float b, float a)
        {
            return ((uint)(r * 255)) |
                   ((uint)(g * 255) << 8) |
                   ((uint)(b * 255) << 16) |
                   ((uint)(a * 255) << 24);
        }

        public static float NormalizeColor(Vector3 inColor, out Vector3 outColor)
        {
            float max = Mathf.Max(inColor.x, Mathf.Max(inColor.y, inColor.z));
            if (max == 0f)
            {
                outColor = Vector3.zero;
            }
            else
            {
                outColor = inColor / max;
            }
            return max;
        }

        // -------------------------------------------------------
        // Plane math
        // -------------------------------------------------------

        // Returns false if triangle is degenerate.
        // plane.xyz = normal, plane.w = distance (ET vec4 plane format)
        public static bool PlaneFromPoints(out Vector4 plane, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 d1 = b - a;
            Vector3 d2 = c - a;
            Vector3 normal = Vector3.Cross(d2, d1);
            float len = normal.magnitude;
            if (len == 0f)
            {
                plane = Vector4.zero;
                return false;
            }
            normal /= len;
            plane = new Vector4(normal.x, normal.y, normal.z, Vector3.Dot(a, normal));
            return true;
        }

        public static void SetPlaneSignbits(ref ETPlane plane)
        {
            int bits = 0;
            if (plane.Normal.x < 0f) bits |= 1;
            if (plane.Normal.y < 0f) bits |= 2;
            if (plane.Normal.z < 0f) bits |= 4;
            plane.SignBits = (byte)bits;
        }

        // Returns 1 (front), 2 (back), or 3 (straddling)
        public static int BoxOnPlaneSide(Vector3 mins, Vector3 maxs, ETPlane p)
        {
            if (p.Type < 3)
            {
                float val = p.Type == 0 ? mins.x : (p.Type == 1 ? mins.y : mins.z);
                float vax = p.Type == 0 ? maxs.x : (p.Type == 1 ? maxs.y : maxs.z);
                if (p.Dist <= val) return 1;
                if (p.Dist >= vax) return 2;
                return 3;
            }

            float dist1, dist2;
            switch (p.SignBits)
            {
                case 0: dist1 = p.Normal.x*maxs.x + p.Normal.y*maxs.y + p.Normal.z*maxs.z; dist2 = p.Normal.x*mins.x + p.Normal.y*mins.y + p.Normal.z*mins.z; break;
                case 1: dist1 = p.Normal.x*mins.x + p.Normal.y*maxs.y + p.Normal.z*maxs.z; dist2 = p.Normal.x*maxs.x + p.Normal.y*mins.y + p.Normal.z*mins.z; break;
                case 2: dist1 = p.Normal.x*maxs.x + p.Normal.y*mins.y + p.Normal.z*maxs.z; dist2 = p.Normal.x*mins.x + p.Normal.y*maxs.y + p.Normal.z*mins.z; break;
                case 3: dist1 = p.Normal.x*mins.x + p.Normal.y*mins.y + p.Normal.z*maxs.z; dist2 = p.Normal.x*maxs.x + p.Normal.y*maxs.y + p.Normal.z*mins.z; break;
                case 4: dist1 = p.Normal.x*maxs.x + p.Normal.y*maxs.y + p.Normal.z*mins.z; dist2 = p.Normal.x*mins.x + p.Normal.y*mins.y + p.Normal.z*maxs.z; break;
                case 5: dist1 = p.Normal.x*mins.x + p.Normal.y*maxs.y + p.Normal.z*mins.z; dist2 = p.Normal.x*maxs.x + p.Normal.y*mins.y + p.Normal.z*maxs.z; break;
                case 6: dist1 = p.Normal.x*maxs.x + p.Normal.y*mins.y + p.Normal.z*mins.z; dist2 = p.Normal.x*mins.x + p.Normal.y*maxs.y + p.Normal.z*maxs.z; break;
                case 7: dist1 = p.Normal.x*mins.x + p.Normal.y*mins.y + p.Normal.z*mins.z; dist2 = p.Normal.x*maxs.x + p.Normal.y*maxs.y + p.Normal.z*maxs.z; break;
                default: dist1 = dist2 = 0f; break;
            }

            int sides = 0;
            if (dist1 >= p.Dist) sides  = 1;
            if (dist2  < p.Dist) sides |= 2;
            return sides;
        }

        // -------------------------------------------------------
        // Bounds
        // -------------------------------------------------------

        public static float RadiusFromBounds(Vector3 mins, Vector3 maxs)
        {
            Vector3 corner = new(
                Mathf.Max(Mathf.Abs(mins.x), Mathf.Abs(maxs.x)),
                Mathf.Max(Mathf.Abs(mins.y), Mathf.Abs(maxs.y)),
                Mathf.Max(Mathf.Abs(mins.z), Mathf.Abs(maxs.z))
            );
            return corner.magnitude;
        }

        public static void ClearBounds(out Vector3 mins, out Vector3 maxs)
        {
            mins = new Vector3( 99999f,  99999f,  99999f);
            maxs = new Vector3(-99999f, -99999f, -99999f);
        }

        public static void AddPointToBounds(Vector3 v, ref Vector3 mins, ref Vector3 maxs)
        {
            if (v.x < mins.x) mins.x = v.x;
            if (v.x > maxs.x) maxs.x = v.x;
            if (v.y < mins.y) mins.y = v.y;
            if (v.y > maxs.y) maxs.y = v.y;
            if (v.z < mins.z) mins.z = v.z;
            if (v.z > maxs.z) maxs.z = v.z;
        }

        public static bool PointInBounds(Vector3 v, Vector3 mins, Vector3 maxs)
        {
            return v.x >= mins.x && v.x <= maxs.x &&
                   v.y >= mins.y && v.y <= maxs.y &&
                   v.z >= mins.z && v.z <= maxs.z;
        }

        // -------------------------------------------------------
        // Fast inverse square root (Quake magic, kept for reference accuracy)
        // -------------------------------------------------------
        public static float Q_rsqrt(float number)
        {
            // C# safe version — identical numerical result to the original bit hack
            return 1f / Mathf.Sqrt(number);
        }

        public static int Q_log2(int val)
        {
            int answer = 0;
            while ((val >>= 1) != 0) answer++;
            return answer;
        }

        // -------------------------------------------------------
        // Angle utilities  (all angles in degrees, ET convention)
        // -------------------------------------------------------

        public static float LerpAngle(float from, float to, float frac)
        {
            if (to - from >  180f) to -= 360f;
            if (to - from < -180f) to += 360f;
            return from + frac * (to - from);
        }

        public static Vector3 LerpPosition(Vector3 start, Vector3 end, float frac)
        {
            return start + frac * (end - start);
        }

        public static float AngleSubtract(float a1, float a2)
        {
            float a = a1 - a2;
            while (a >  180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

        public static Vector3 AnglesSubtract(Vector3 v1, Vector3 v2)
        {
            return new Vector3(
                AngleSubtract(v1.x, v2.x),
                AngleSubtract(v1.y, v2.y),
                AngleSubtract(v1.z, v2.z)
            );
        }

        public static float AngleMod(float a)
        {
            return (360.0f / 65536f) * ((int)(a * (65536f / 360.0f)) & 65535);
        }

        public static float AngleNormalize360(float angle)
        {
            return (360.0f / 65536f) * ((int)(angle * (65536f / 360.0f)) & 65535);
        }

        public static float AngleNormalize180(float angle)
        {
            angle = AngleNormalize360(angle);
            if (angle > 180f) angle -= 360f;
            return angle;
        }

        public static float AngleDelta(float angle1, float angle2)
        {
            return AngleNormalize180(angle1 - angle2);
        }

        public static float AngleNormalize2Pi(float angle)
        {
            return Mathf.Deg2Rad * AngleNormalize360(Mathf.Rad2Deg * angle);
        }

        // Converts ET euler angles (pitch, yaw, roll) to forward/right/up axis vectors.
        // ET angle convention: pitch=down is positive, yaw=left is positive (right-hand Z-up).
        public static void AngleVectors(Vector3 angles, out Vector3 forward, out Vector3 right, out Vector3 up)
        {
            float angleY = angles[YAW]   * (Mathf.PI * 2f / 360f);
            float sy = Mathf.Sin(angleY), cy = Mathf.Cos(angleY);

            float angleP = angles[PITCH] * (Mathf.PI * 2f / 360f);
            float sp = Mathf.Sin(angleP), cp = Mathf.Cos(angleP);

            float angleR = angles[ROLL]  * (Mathf.PI * 2f / 360f);
            float sr = Mathf.Sin(angleR), cr = Mathf.Cos(angleR);

            forward = new Vector3(cp * cy, cp * sy, -sp);

            right = new Vector3(
                -1f * sr * sp * cy + -1f * cr * -sy,
                -1f * sr * sp * sy + -1f * cr *  cy,
                -1f * sr * cp
            );

            up = new Vector3(
                cr * sp * cy + -sr * -sy,
                cr * sp * sy + -sr *  cy,
                cr * cp
            );
        }

        public static Vector3 VecToAngles(Vector3 value1)
        {
            float yaw, pitch;
            if (value1.y == 0f && value1.x == 0f)
            {
                yaw = 0f;
                pitch = value1.z > 0f ? 90f : 270f;
            }
            else
            {
                yaw = value1.x != 0f
                    ? Mathf.Atan2(value1.y, value1.x) * 180f / Mathf.PI
                    : (value1.y > 0f ? 90f : 270f);
                if (yaw < 0f) yaw += 360f;

                float forward2d = Mathf.Sqrt(value1.x * value1.x + value1.y * value1.y);
                pitch = Mathf.Atan2(value1.z, forward2d) * 180f / Mathf.PI;
                if (pitch < 0f) pitch += 360f;
            }
            return new Vector3(-pitch, yaw, 0f);
        }

        public static void AnglesToAxis(Vector3 angles, out Vector3[] axis)
        {
            axis = new Vector3[3];
            AngleVectors(angles, out axis[0], out Vector3 right, out axis[2]);
            axis[1] = -right;
        }

        public static float VecToYaw(Vector3 vec)
        {
            if (vec[YAW] == 0f && vec[PITCH] == 0f) return 0f;
            float yaw;
            if (vec[PITCH] != 0f)
                yaw = Mathf.Atan2(vec[YAW], vec[PITCH]) * 180f / Mathf.PI;
            else
                yaw = vec[YAW] > 0f ? 90f : 270f;
            if (yaw < 0f) yaw += 360f;
            return yaw;
        }

        // -------------------------------------------------------
        // Vector geometry helpers
        // -------------------------------------------------------

        public static void ProjectPointOnPlane(out Vector3 dst, Vector3 p, Vector3 normal)
        {
            float invDenom = 1f / Vector3.Dot(normal, normal);
            float d = Vector3.Dot(normal, p) * invDenom;
            dst = p - d * (normal * invDenom);
        }

        public static void MakeNormalVectors(Vector3 forward, out Vector3 right, out Vector3 up)
        {
            right = new Vector3(forward.z, -forward.x, forward.y);
            float d = Vector3.Dot(right, forward);
            right -= d * forward;
            right.Normalize();
            up = Vector3.Cross(right, forward);
        }

        public static Vector3 VectorRotate(Vector3 inVec, Vector3[] matrix)
        {
            return new Vector3(
                Vector3.Dot(inVec, matrix[0]),
                Vector3.Dot(inVec, matrix[1]),
                Vector3.Dot(inVec, matrix[2])
            );
        }

        public static void RotatePointAroundVector(out Vector3 dst, Vector3 dir, Vector3 point, float degrees)
        {
            // Build orthonormal basis (dir = forward)
            PerpendicularVector(out Vector3 vr, dir);
            Vector3 vup = Vector3.Cross(vr, dir);

            float[,] m  = BuildBasis(vr, vup, dir);
            float[,] im = TransposeBasis(m);

            float rad = degrees * Mathf.Deg2Rad;
            float cosR = Mathf.Cos(rad), sinR = Mathf.Sin(rad);
            float[,] zrot = {
                { cosR,  sinR, 0 },
                {-sinR,  cosR, 0 },
                { 0,     0,    1 }
            };

            float[,] tmp = Mul3x3(m, zrot);
            float[,] rot = Mul3x3(tmp, im);

            dst = new Vector3(
                rot[0,0]*point.x + rot[0,1]*point.y + rot[0,2]*point.z,
                rot[1,0]*point.x + rot[1,1]*point.y + rot[1,2]*point.z,
                rot[2,0]*point.x + rot[2,1]*point.y + rot[2,2]*point.z
            );
        }

        public static void RotateAroundDirection(Vector3[] axis, float yaw)
        {
            PerpendicularVector(out axis[1], axis[0]);
            if (yaw != 0f)
            {
                Vector3 temp = axis[1];
                RotatePointAroundVector(out axis[1], axis[0], temp, yaw);
            }
            axis[2] = Vector3.Cross(axis[0], axis[1]);
        }

        public static void PerpendicularVector(out Vector3 dst, Vector3 src)
        {
            int pos = 0;
            float minElem = 1f;
            float[] s = { Mathf.Abs(src.x), Mathf.Abs(src.y), Mathf.Abs(src.z) };
            for (int i = 0; i < 3; i++)
            {
                if (s[i] < minElem) { pos = i; minElem = s[i]; }
            }
            Vector3 temp = Vector3.zero;
            if (pos == 0) temp.x = 1f;
            else if (pos == 1) temp.y = 1f;
            else temp.z = 1f;

            ProjectPointOnPlane(out dst, temp, src);
            dst.Normalize();
        }

        public static void GetPerpendicularViewVector(Vector3 point, Vector3 p1, Vector3 p2, out Vector3 up)
        {
            Vector3 v1 = (point - p1).normalized;
            Vector3 v2 = (point - p2).normalized;
            up = Vector3.Cross(v1, v2).normalized;
        }

        public static void ProjectPointOntoVector(Vector3 point, Vector3 vStart, Vector3 vEnd, out Vector3 vProj)
        {
            Vector3 pVec = point - vStart;
            Vector3 vec  = (vEnd - vStart).normalized;
            vProj = vStart + Vector3.Dot(pVec, vec) * vec;
        }

        public static void ProjectPointOntoVectorBounded(Vector3 point, Vector3 vStart, Vector3 vEnd, out Vector3 vProj)
        {
            ProjectPointOntoVector(point, vStart, vEnd, out vProj);
            float[] proj = { vProj.x, vProj.y, vProj.z };
            float[] s    = { vStart.x, vStart.y, vStart.z };
            float[] e    = { vEnd.x,   vEnd.y,   vEnd.z   };

            for (int j = 0; j < 3; j++)
            {
                if ((proj[j] > s[j] && proj[j] > e[j]) || (proj[j] < s[j] && proj[j] < e[j]))
                {
                    vProj = (Mathf.Abs(proj[j] - s[j]) < Mathf.Abs(proj[j] - e[j])) ? vStart : vEnd;
                    return;
                }
            }
        }

        public static float DistanceFromLineSquared(Vector3 p, Vector3 lp1, Vector3 lp2)
        {
            ProjectPointOntoVector(p, lp1, lp2, out Vector3 proj);
            float[] pr = { proj.x, proj.y, proj.z };
            float[] s  = { lp1.x,  lp1.y,  lp1.z  };
            float[] e  = { lp2.x,  lp2.y,  lp2.z  };

            for (int j = 0; j < 3; j++)
            {
                if ((pr[j] > s[j] && pr[j] > e[j]) || (pr[j] < s[j] && pr[j] < e[j]))
                {
                    Vector3 t = p - (Mathf.Abs(pr[j] - s[j]) < Mathf.Abs(pr[j] - e[j]) ? lp1 : lp2);
                    return t.sqrMagnitude;
                }
            }
            return (p - proj).sqrMagnitude;
        }

        public static float DistanceFromVectorSquared(Vector3 p, Vector3 lp1, Vector3 lp2)
        {
            ProjectPointOntoVector(p, lp1, lp2, out Vector3 proj);
            return (p - proj).sqrMagnitude;
        }

        public static void AxisToAngles(Vector3[] axis, out Vector3 angles)
        {
            angles = VecToAngles(axis[0]);
            Vector3 right = axis[1];
            RotatePointAroundVector(out Vector3 tvec,  new Vector3(0,0,1), right, -angles[YAW]);
            RotatePointAroundVector(out Vector3 right2, new Vector3(0,1,0), tvec,  -angles[PITCH]);
            Vector3 rollAngles = VecToAngles(right2);
            rollAngles.x = AngleNormalize180(rollAngles.x);
            if (Vector3.Dot(right2, new Vector3(0,1,0)) < 0f)
            {
                rollAngles.x = rollAngles.x < 0f
                    ? -90f + (-90f - rollAngles.x)
                    :  90f + ( 90f - rollAngles.x);
            }
            angles.z = -rollAngles.x;
        }

        // -------------------------------------------------------
        // 3x3 matrix helpers (internal use)
        // -------------------------------------------------------

        private static float[,] BuildBasis(Vector3 r, Vector3 u, Vector3 f)
        {
            return new float[3, 3]
            {
                { r.x, u.x, f.x },
                { r.y, u.y, f.y },
                { r.z, u.z, f.z },
            };
        }

        private static float[,] TransposeBasis(float[,] m)
        {
            return new float[3, 3]
            {
                { m[0,0], m[1,0], m[2,0] },
                { m[0,1], m[1,1], m[2,1] },
                { m[0,2], m[1,2], m[2,2] },
            };
        }

        public static float[,] Mul3x3(float[,] a, float[,] b)
        {
            var r = new float[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++)
                        r[i, j] += a[i, k] * b[k, j];
            return r;
        }
    }

    // ET plane struct (mirrors cplane_t)
    public struct ETPlane
    {
        public Vector3 Normal;
        public float   Dist;
        public byte    Type;     // for fast axial tests: 0=X, 1=Y, 2=Z, 3=non-axial
        public byte    SignBits; // bit set for negative normal components
    }
}
