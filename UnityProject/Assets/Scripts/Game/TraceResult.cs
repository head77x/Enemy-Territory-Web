// Ported from: src/game/q_shared.h  (trace_t, cplane_t)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using UnityEngine;

namespace ET.Game
{
    /// <summary>
    /// cplane_t — collision plane.
    /// </summary>
    public struct CollisionPlane
    {
        public Vector3 Normal;
        public float   Dist;
        public byte    Type;      // 0=X, 1=Y, 2=Z, 3=non-axial
        public byte    SignBits;  // (normal.x<0) | (normal.y<0)<<1 | (normal.z<0)<<2

        public static CollisionPlane FromNormalDist(Vector3 normal, float dist)
        {
            var p = new CollisionPlane { Normal = normal, Dist = dist };
            p.SignBits = (byte)(
                (normal.x < 0f ? 1 : 0) |
                (normal.y < 0f ? 2 : 0) |
                (normal.z < 0f ? 4 : 0));
            if      (normal.x == 1f || normal.x == -1f) p.Type = 0;
            else if (normal.y == 1f || normal.y == -1f) p.Type = 1;
            else if (normal.z == 1f || normal.z == -1f) p.Type = 2;
            else                                        p.Type = 3;
            return p;
        }
    }

    /// <summary>
    /// trace_t — result of sweeping a box through the world.
    /// </summary>
    public struct TraceResult
    {
        public bool           AllSolid;    // started and ended in solid
        public bool           StartSolid;  // started in solid area
        public float          Fraction;    // 1.0 = didn't hit anything
        public Vector3        EndPos;      // final position
        public CollisionPlane Plane;       // surface normal at impact
        public int            SurfaceFlags;
        public int            Contents;
        public int            EntityNum;

        /// <summary>Convenience: shortcut to plane normal.</summary>
        public Vector3 PlaneNormal => Plane.Normal;

        /// <summary>Returns a no-hit trace result (fraction = 1, ends at end).</summary>
        public static TraceResult NoHit(Vector3 end) => new TraceResult
        {
            Fraction    = 1f,
            EndPos      = end,
            EntityNum   = GameConst.ENTITYNUM_NONE,
        };
    }
}
