// Ported from: src/server/sv_world.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    /// <summary>
    /// Spatial queries against world entities (sv_world.c port).
    /// BSP-level operations are stubs; AABB broad-phase is used in their place
    /// until a full BSP collision back-end is wired up.
    /// </summary>
    public static class ServerWorld
    {
        // Half-extents used for broad-phase entity bounds when an entity has no
        // explicit model box set.  Matches the default player box used in ET.
        private const float DefaultHalfX = 18f;
        private const float DefaultHalfY = 18f;
        private const float DefaultHalfZ = 36f;

        // -----------------------------------------------------------------------
        // SV_ClearWorld — called before entities are linked into the world.
        // No-op in Unity: the scene/BSP back-end handles spatial indexing.
        // -----------------------------------------------------------------------
        public static void SV_ClearWorld() { }

        // -----------------------------------------------------------------------
        // SV_UnlinkEntity — remove an entity from the spatial index.
        // Stub: Unity scene handles this.
        // -----------------------------------------------------------------------
        public static void SV_UnlinkEntity(int entityNum) { }

        // -----------------------------------------------------------------------
        // SV_LinkEntity — add/update an entity in the spatial index.
        // Stub: Unity scene handles this.
        // -----------------------------------------------------------------------
        public static void SV_LinkEntity(int entityNum) { }

        // -----------------------------------------------------------------------
        // SV_AreaEntities — return entity numbers whose broad-phase AABB overlaps
        // the supplied query box [mins, maxs].
        //
        // In the original C code this walks BSP area portals; here we iterate all
        // active entities and do a simple AABB overlap test.
        //
        // Returns the number of entities written into entityList.
        // -----------------------------------------------------------------------
        public static int SV_AreaEntities(Vector3 mins, Vector3 maxs,
                                           int[] entityList, int maxcount)
        {
            if (entityList == null || maxcount <= 0)
                return 0;

            var sv    = ServerMain.Sv;
            int count = 0;

            for (int i = 0; i < sv.NumEntities && count < maxcount; i++)
            {
                EntityState ent = sv.Entities[i];
                if (ent == null) continue;
                if (ent.EType == 0) continue;   // unset / free slot

                // Build entity AABB from origin + default half-extents
                float ox = ent.Origin0;
                float oy = ent.Origin1;
                float oz = ent.Origin2;

                float entMinX = ox - DefaultHalfX;
                float entMinY = oy - DefaultHalfY;
                float entMinZ = oz - DefaultHalfZ;
                float entMaxX = ox + DefaultHalfX;
                float entMaxY = oy + DefaultHalfY;
                float entMaxZ = oz + DefaultHalfZ;

                // AABB overlap: no separating axis exists on any of the three axes
                if (maxs.x < entMinX || mins.x > entMaxX) continue;
                if (maxs.y < entMinY || mins.y > entMaxY) continue;
                if (maxs.z < entMinZ || mins.z > entMaxZ) continue;

                entityList[count++] = i;
            }

            return count;
        }

        // -----------------------------------------------------------------------
        // SV_ClipToEntity — clip a ray against a single entity using a swept AABB.
        //
        // The entity box is centered at (ent.Origin0, ent.Origin1, ent.Origin2)
        // with half-extents (DefaultHalfX, DefaultHalfY, DefaultHalfZ).
        //
        // The moving box [mins, maxs] is swept from start to end; the Minkowski
        // sum is used so the problem reduces to a ray vs expanded box.
        //
        // trace.Fraction is updated only if a closer hit is found.
        // -----------------------------------------------------------------------
        public static void SV_ClipToEntity(ref TraceResult trace,
                                            Vector3 start, Vector3 mins, Vector3 maxs,
                                            Vector3 end, int entityNum, int contentmask)
        {
            var sv = ServerMain.Sv;
            if (entityNum < 0 || entityNum >= sv.NumEntities) return;

            EntityState ent = sv.Entities[entityNum];
            if (ent == null || ent.EType == 0) return;

            // Expanded box half-extents (Minkowski sum of entity box and moving box)
            float exHalfX = DefaultHalfX + Mathf.Abs((maxs.x - mins.x) * 0.5f);
            float exHalfY = DefaultHalfY + Mathf.Abs((maxs.y - mins.y) * 0.5f);
            float exHalfZ = DefaultHalfZ + Mathf.Abs((maxs.z - mins.z) * 0.5f);

            // Box centre
            float cx = ent.Origin0;
            float cy = ent.Origin1;
            float cz = ent.Origin2;

            // Ray in world space relative to box centre
            float rox = start.x - cx;
            float roy = start.y - cy;
            float roz = start.z - cz;

            float rdx = end.x - start.x;
            float rdy = end.y - start.y;
            float rdz = end.z - start.z;

            // Slab method — compute entry/exit t on each axis
            float tMin = 0f;
            float tMax = trace.Fraction;   // only accept hits closer than current best

            Vector3 hitNormal = Vector3.zero;

            // X axis
            if (!ComputeSlab(rox, rdx, exHalfX, ref tMin, ref tMax, ref hitNormal,
                              new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f)))
                return;

            // Y axis
            if (!ComputeSlab(roy, rdy, exHalfY, ref tMin, ref tMax, ref hitNormal,
                              new Vector3(0f, -1f, 0f), new Vector3(0f, 1f, 0f)))
                return;

            // Z axis
            if (!ComputeSlab(roz, rdz, exHalfZ, ref tMin, ref tMax, ref hitNormal,
                              new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f)))
                return;

            // A hit was found that is closer than the current trace result
            trace.Fraction  = tMin;
            trace.EntityNum = entityNum;
            trace.EndPos    = start + tMin * (end - start);
            trace.AllSolid  = false;
            trace.StartSolid = false;

            trace.Plane.Normal   = hitNormal;
            trace.Plane.Dist     = Vector3.Dot(hitNormal,
                                       new Vector3(cx, cy, cz))
                                   + (hitNormal.x < 0f ? exHalfX
                                    : hitNormal.y < 0f ? exHalfY
                                    : exHalfZ);
            trace.Plane.Type     = 0;
            trace.Plane.SignBits = ComputeSignBits(hitNormal);
        }

        // Slab helper: updates tMin/tMax and hitNormal for one axis.
        // Returns false if the ray misses the slab (no intersection possible).
        private static bool ComputeSlab(float ro, float rd, float halfExtent,
                                         ref float tMin, ref float tMax,
                                         ref Vector3 hitNormal,
                                         Vector3 negFaceNormal, Vector3 posFaceNormal)
        {
            const float Epsilon = 1e-6f;

            if (Mathf.Abs(rd) < Epsilon)
            {
                // Ray is parallel to slab — check if origin is inside
                if (ro < -halfExtent || ro > halfExtent)
                    return false;
                return true;
            }

            float invD = 1f / rd;
            float t1   = (-halfExtent - ro) * invD;
            float t2   = ( halfExtent - ro) * invD;

            Vector3 entryNormal;
            if (t1 > t2)
            {
                // Swap so t1 <= t2
                float tmp = t1; t1 = t2; t2 = tmp;
                entryNormal = posFaceNormal;
            }
            else
            {
                entryNormal = negFaceNormal;
            }

            if (t1 > tMin)
            {
                tMin      = t1;
                hitNormal = entryNormal;
            }
            if (t2 < tMax)
                tMax = t2;

            return tMin <= tMax;
        }

        // Pack normal sign bits into a byte (ET convention: bit i set if normal[i] < 0)
        private static byte ComputeSignBits(Vector3 n)
        {
            byte bits = 0;
            if (n.x < 0f) bits |= 1;
            if (n.y < 0f) bits |= 2;
            if (n.z < 0f) bits |= 4;
            return bits;
        }

        // -----------------------------------------------------------------------
        // SV_Trace — sweep [start..end] against all world entities.
        //
        // In the original C code this called CM_BoxTrace for BSP geometry plus a
        // per-entity clip.  Here we skip BSP (PhysicsWorld handles it externally)
        // and only iterate server entities.
        //
        // passEntityNum is excluded from the test (e.g. the moving entity itself).
        // Returns the closest-hit TraceResult.
        // -----------------------------------------------------------------------
        public static TraceResult SV_Trace(Vector3 start, Vector3 mins, Vector3 maxs,
                                            Vector3 end, int passEntityNum, int contentmask)
        {
            // Start with a pass-through result
            var trace = new TraceResult
            {
                Fraction  = 1f,
                EndPos    = end,
                EntityNum = GameConst.ENTITYNUM_NONE,
                AllSolid  = false,
                StartSolid = false,
            };

            var sv = ServerMain.Sv;
            for (int i = 0; i < sv.NumEntities; i++)
            {
                if (i == passEntityNum) continue;

                EntityState ent = sv.Entities[i];
                if (ent == null || ent.EType == 0) continue;

                SV_ClipToEntity(ref trace, start, mins, maxs, end, i, contentmask);
            }

            return trace;
        }

        // -----------------------------------------------------------------------
        // SV_PointContents — return content flags at a world point.
        // Stub — returns 0 until BSP collision is wired up.
        // -----------------------------------------------------------------------
        public static int SV_PointContents(Vector3 point, int passEntityNum)
        {
            return 0;
        }
    }
}
