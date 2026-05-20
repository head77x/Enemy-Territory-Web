// Ported from: src/qcommon/cm_load.c, cm_trace.c, cm_patch.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// Unity port: ET-compatible collision API backed by PhysX.
// The actual geometry collision is performed by Physics.BoxCast /
// Physics.OverlapSphere; the BSP-level functions are stubs used only
// when running in headless server mode with a raw BSP buffer.

using System;
using UnityEngine;
using ET.Core;

namespace ET.Game
{
    // -----------------------------------------------------------------------
    // IETEntity — lightweight interface implemented by any MonoBehaviour that
    // represents a server-side entity in the Unity scene.  Lets CollisionSystem
    // resolve hit colliders to their ET entity numbers without a hard dependency
    // on any concrete server-entity class.
    // -----------------------------------------------------------------------
    public interface IETEntity
    {
        /// <summary>Server entity index (0 .. ENTITYNUM_WORLD).</summary>
        int EntityNum { get; }
    }


    /// <summary>
    /// CollisionSystem — ET-compatible cm_*.c API over Unity PhysX.
    ///
    /// Call CM_BoxTrace / CM_Trace from pmove and server world queries exactly
    /// as you would call the original C functions.  LayerMaskForBrushmask
    /// converts ET content flags to the Unity layer mask used for the cast.
    /// </summary>
    public static class CollisionSystem
    {
        // -----------------------------------------------------------------------
        // Unity physics layer numbers used in ET scenes
        // -----------------------------------------------------------------------
        private const int UnityLayer_Default = 0;   // CONTENTS_SOLID geometry
        private const int UnityLayer_Trigger = 2;   // passable triggers
        private const int UnityLayer_Water   = 4;   // CONTENTS_WATER volumes
        private const int UnityLayer_World   = 8;   // additional world brushes

        // -----------------------------------------------------------------------
        // BSP state — populated only in headless / pure-server mode
        // -----------------------------------------------------------------------
        private static string  s_mapName      = string.Empty;
        private static int     s_checksum     = 0;

        private static string  s_entityString = string.Empty;

        // -----------------------------------------------------------------------
        // ET→Unity and Unity→ET coordinate-space helpers
        //
        // ET (Quake) is right-handed: +X forward, +Y left, +Z up.
        // Unity is left-handed: +X right, +Y up, +Z forward.
        // Mapping chosen to keep Z-up in ET == Y-up in Unity:
        //   ET (x, y, z) → Unity (-y, z, x)
        //   Unity (ux, uy, uz) → ET (uz, -ux, uy)
        // -----------------------------------------------------------------------
        private static Vector3 ToUnity(Vector3 et)
            => new Vector3(-et.y, et.z, et.x);

        private static Vector3 ToET(Vector3 unity)
            => new Vector3(unity.z, -unity.x, unity.y);

        // -----------------------------------------------------------------------
        // CM_LoadMap — load a BSP for collision (cm_load.c)
        //
        // In Unity the map is already loaded as a scene, so this is a no-op for
        // client builds.  In headless/pure-server mode a raw BSP byte[] would be
        // parsed here; that path is left as a stub until the BSP loader is ready.
        // -----------------------------------------------------------------------
        public static void CM_LoadMap(string mapName, bool clientLoad, ref int checksum)
        {
            s_mapName   = mapName ?? string.Empty;
            s_checksum  = checksum;


            // Client load: Unity scene already contains all geometry as PhysX
            // colliders — nothing to parse.  Server headless BSP parse goes here.
            if (!clientLoad)
            {
                // TODO: headless BSP load from raw bytes via a future BspLoader.
                Debug.Log($"[CollisionSystem] CM_LoadMap: headless map '{mapName}' (stub)");
            }
        }

        /// <summary>Free clip map data (cm_load.c: CM_ClearMap).</summary>
        public static void CM_ClearMap()
        {
            s_mapName      = string.Empty;
            s_checksum     = 0;

            s_entityString = string.Empty;
        }

        // -----------------------------------------------------------------------
        // CM_PointContents — return content flags at a world point (cm_trace.c)
        //
        // Uses Physics.OverlapSphere with a tiny radius to query the PhysX scene.
        // Each overlapping collider's layer is mapped to ET content flags via
        // LayerToContents(), and the results are OR-ed together.
        // -----------------------------------------------------------------------
        public static int CM_PointContents(Vector3 point, int model)
        {
            Vector3 unityPoint = ToUnity(point);

            // Use all layers so we can classify each collider ourselves.
            Collider[] hits = Physics.OverlapSphere(unityPoint, 0.05f, ~0,
                                                    QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
                return 0;

            int contents = 0;
            foreach (Collider col in hits)
                contents |= LayerToContents(col.gameObject.layer);

            return contents;
        }

        // -----------------------------------------------------------------------
        // CM_TransformedPointContents — contents at point with model transform
        // (cm_trace.c: CM_TransformedPointContents)
        //
        // Inverse-transforms the query point into model-local space before the
        // OverlapSphere, mirroring the original C implementation.
        // -----------------------------------------------------------------------
        public static int CM_TransformedPointContents(Vector3 point, int model,
            Vector3 origin, Vector3 angles)
        {
            // Translate into model-local space.
            Vector3 localPoint = point - origin;

            // Rotate by inverse of model angles (undo yaw/pitch/roll).
            if (angles != Vector3.zero)
            {
                Quaternion rot    = ETAnglesToQuaternion(angles);
                Quaternion invRot = Quaternion.Inverse(rot);
                localPoint = invRot * localPoint;
            }

            return CM_PointContents(localPoint, model);
        }

        // -----------------------------------------------------------------------
        // CM_BoxTrace — trace a box through the world (cm_trace.c)
        //
        // This is the main entry point used by pmove and server traces.
        //
        // Algorithm (Unity PhysX path):
        //   1. Convert ET mins/maxs to a Unity half-extents vector.
        //   2. Compute the initial box centre in Unity space.
        //   3. BoxCast in the direction of travel.
        //   4. Convert the hit result back to ET space.
        // -----------------------------------------------------------------------
        public static TraceResult CM_BoxTrace(
            Vector3 start, Vector3 end,
            Vector3 mins, Vector3 maxs,
            int model, int brushmask)
        {
            // Point trace: if mins == maxs == zero use a ray instead of a box.
            bool isPointTrace = (mins == Vector3.zero && maxs == Vector3.zero);

            // Half-extents in ET space.
            Vector3 etExtents  = (maxs - mins) * 0.5f;
            // Offset from entity origin to box centre (ET space).
            Vector3 etOffset   = (mins + maxs) * 0.5f;

            // Convert to Unity space.
            Vector3 unityStart    = ToUnity(start + etOffset);
            Vector3 unityEnd      = ToUnity(end   + etOffset);
            Vector3 unityExtents  = ToUnity(etExtents);

            // Extents must be positive in all components; ToUnity may flip signs.
            unityExtents.x = Mathf.Abs(unityExtents.x);
            unityExtents.y = Mathf.Abs(unityExtents.y);
            unityExtents.z = Mathf.Abs(unityExtents.z);

            // Clamp to a minimum so PhysX doesn't reject a zero-size box.
            const float kMinExtent = 0.001f;
            unityExtents.x = Mathf.Max(unityExtents.x, kMinExtent);
            unityExtents.y = Mathf.Max(unityExtents.y, kMinExtent);
            unityExtents.z = Mathf.Max(unityExtents.z, kMinExtent);

            Vector3 delta    = unityEnd - unityStart;
            float   dist     = delta.magnitude;
            Vector3 dir      = dist > 1e-6f ? delta / dist : Vector3.forward;

            int layerMask = LayerMaskForBrushmask(brushmask);

            // ------------------------------------------------------------------
            // PhysX BoxCast does not report a hit when the swept shape starts
            // in CONTACT with (touching, not penetrating) a surface — it only
            // reports new collisions found during the sweep.  To work around this,
            // we shift the BoxCast start position slightly backward (opposite to
            // the sweep direction) before calling Physics.BoxCast so that any
            // surface the box is merely touching becomes a genuine sweep hit.
            // The fraction and endPos are then remapped back to the original
            // ET-space sweep range.  The startSolid OverlapBox always uses the
            // ORIGINAL (un-shifted) position.
            // ------------------------------------------------------------------
            const float kSweepEpsilon = 0.125f;  // tiny relative to map scale
            Vector3 unityStartOrig = unityStart;
            float   distOrig       = dist;
            float   sweepEps       = 0f;

            if (!isPointTrace && dist > 0.001f)
            {
                sweepEps    = kSweepEpsilon;
                unityStart -= dir * sweepEps;   // shift back so touching faces are swept
                dist       += sweepEps;
            }

            // ------------------------------------------------------------------
            // Early-out: check if the start position is already in solid.
            // Always test the ORIGINAL (un-shifted) position.
            // ------------------------------------------------------------------
            bool startSolid = false;
            {
                Collider[] overlaps = Physics.OverlapBox(unityStartOrig, unityExtents,
                    Quaternion.identity, layerMask,
                    QueryTriggerInteraction.Ignore);
                if (overlaps != null && overlaps.Length > 0)
                {
                    startSolid = true;
                    // If there's no movement, report AllSolid immediately.
                    if (dist < 1e-6f)
                    {
                        int solidContents = 0;
                        foreach (var col in overlaps)
                            solidContents |= LayerToContents(col.gameObject.layer);

                        return new TraceResult
                        {
                            AllSolid    = true,
                            StartSolid  = true,
                            Fraction    = 0f,
                            EndPos      = start,
                            Contents    = solidContents,
                            EntityNum   = GameConst.ENTITYNUM_WORLD,
                        };
                    }
                }
            }

            // ------------------------------------------------------------------
            // Sweep test.
            // ------------------------------------------------------------------
            RaycastHit hitInfo;
            bool       didHit;

            if (isPointTrace)
            {
                // Physics.Raycast for zero-size traces (more robust than BoxCast).
                didHit = Physics.Raycast(unityStart, dir, out hitInfo, dist,
                                         layerMask, QueryTriggerInteraction.Ignore);
            }
            else
            {
                didHit = Physics.BoxCast(unityStart, unityExtents, dir,
                                         out hitInfo, Quaternion.identity, dist,
                                         layerMask, QueryTriggerInteraction.Ignore);
            }

            if (!didHit)
            {
                if (startSolid)
                {
                    // Started inside solid geometry and PhysX found no exit hit.
                    // Report AllSolid so PM_CorrectAllSolid can attempt to recover.
                    // This matches Q3/ET CM_BoxTrace behaviour: trace.fraction = 0
                    // when the trace starts in solid and no clear path exists.
                    int solidContents = 0;
                    Collider[] overlapsForContents = Physics.OverlapBox(unityStartOrig, unityExtents,
                        Quaternion.identity, layerMask, QueryTriggerInteraction.Ignore);
                    if (overlapsForContents != null)
                        foreach (var col in overlapsForContents)
                            solidContents |= LayerToContents(col.gameObject.layer);

                    return new TraceResult
                    {
                        AllSolid   = true,
                        StartSolid = true,
                        Fraction   = 0f,
                        EndPos     = start,
                        Contents   = solidContents != 0 ? solidContents : Contents.Solid,
                        EntityNum  = GameConst.ENTITYNUM_WORLD,
                    };
                }

                // No geometry in the way.
                return new TraceResult
                {
                    StartSolid = false,
                    Fraction   = 1f,
                    EndPos     = end,
                    EntityNum  = GameConst.ENTITYNUM_NONE,
                };
            }

            // Build fraction in ORIGINAL (un-shifted) ET sweep space.
            // hitInfo.distance is measured from the shifted start, so subtract the
            // epsilon before dividing by the original sweep length.
            float adjHitDist = hitInfo.distance - sweepEps;
            float fraction   = (distOrig > 1e-6f)
                ? Mathf.Clamp01(adjHitDist / distOrig)
                : 0f;

            // Convert hit point and normal back to ET space.
            // Reconstruct the box-centre position at impact using the ORIGINAL
            // (un-shifted) start so the endPos is in proper ET-space coordinates.
            Vector3 unityHitCentre = unityStartOrig + dir * (fraction * distOrig);
            Vector3 etHitCentre    = ToET(unityHitCentre);
            Vector3 etEndPos       = etHitCentre - etOffset;   // remove box offset

            Vector3 unityNormal    = hitInfo.normal;
            Vector3 etNormal       = ToET(unityNormal).normalized;

            // Entity number: try to identify a game entity via IETEntity, fall
            // back to ENTITYNUM_WORLD for static world geometry.
            int entityNum = GameConst.ENTITYNUM_WORLD;
            if (hitInfo.collider != null)
            {
                // IETEntity is a lightweight interface that any game-entity
                // MonoBehaviour can implement to expose its server entity index.
                // If no such component is present we treat the hit as world geometry.
                IETEntity etEnt = hitInfo.collider.GetComponentInParent<IETEntity>();
                if (etEnt != null)
                    entityNum = etEnt.EntityNum;
            }

            int hitContents = (hitInfo.collider != null)
                ? LayerToContents(hitInfo.collider.gameObject.layer)
                : Contents.Solid;

            return new TraceResult
            {
                StartSolid   = startSolid,
                AllSolid     = false,
                Fraction     = fraction,
                EndPos       = etEndPos,
                Plane        = CollisionPlane.FromNormalDist(etNormal,
                                   Vector3.Dot(etNormal, etEndPos)),
                SurfaceFlags = SurfaceFlagsForCollider(hitInfo.collider),
                Contents     = hitContents,
                EntityNum    = entityNum,
            };
        }

        // -----------------------------------------------------------------------
        // CM_TransformedBoxTrace — box trace with model transform
        // (cm_trace.c: CM_TransformedBoxTrace)
        //
        // Inverse-transforms start/end into model-local space, calls CM_BoxTrace,
        // then transforms the resulting plane normal back to world space.
        // -----------------------------------------------------------------------
        public static TraceResult CM_TransformedBoxTrace(
            Vector3 start, Vector3 end,
            Vector3 mins, Vector3 maxs,
            int model, int brushmask,
            Vector3 origin, Vector3 angles)
        {
            Vector3 localStart = start - origin;
            Vector3 localEnd   = end   - origin;

            Quaternion rot    = ETAnglesToQuaternion(angles);
            Quaternion invRot = Quaternion.Inverse(rot);

            if (angles != Vector3.zero)
            {
                localStart = invRot * localStart;
                localEnd   = invRot * localEnd;
            }

            TraceResult result = CM_BoxTrace(localStart, localEnd,
                                             mins, maxs, model, brushmask);

            // Rotate the result plane normal back to world space.
            if (angles != Vector3.zero && result.Fraction < 1f)
            {
                Vector3 worldNormal = rot * result.Plane.Normal;
                result.Plane = CollisionPlane.FromNormalDist(worldNormal,
                                   Vector3.Dot(worldNormal, result.EndPos));
            }

            // Translate end position back to world space.
            if (angles != Vector3.zero)
                result.EndPos = rot * result.EndPos;
            result.EndPos += origin;

            return result;
        }

        // -----------------------------------------------------------------------
        // CM_Trace — general trace used internally and by server world queries.
        //
        // Delegates to CM_BoxTrace with model = 0 (world model).
        // -----------------------------------------------------------------------
        public static TraceResult CM_Trace(
            Vector3 start, Vector3 end,
            Vector3 mins, Vector3 maxs,
            int brushmask)
        {
            return CM_BoxTrace(start, end, mins, maxs, 0, brushmask);
        }

        // -----------------------------------------------------------------------
        // CM_ClusterPVS — stub (PVS culling handled by server / network layer)
        // -----------------------------------------------------------------------
        public static byte[] CM_ClusterPVS(int cluster)
        {
            return Array.Empty<byte>();
        }

        // -----------------------------------------------------------------------
        // BSP leaf queries — stubs; used only when a real BSP is loaded
        // -----------------------------------------------------------------------

        /// <summary>Returns the leaf number containing point p (stub → 0).</summary>
        public static int CM_PointLeafnum(Vector3 p)
        {
            return 0;
        }

        /// <summary>Returns the cluster that contains leafnum (stub → 0).</summary>
        public static int CM_LeafCluster(int leafnum)
        {
            return 0;
        }

        /// <summary>Returns the area that contains leafnum (stub → 0).</summary>
        public static int CM_LeafArea(int leafnum)
        {
            return 0;
        }

        // -----------------------------------------------------------------------
        // BSP info stubs
        // -----------------------------------------------------------------------

        /// <summary>Returns total number of PVS clusters (stub → 1).</summary>
        public static int CM_NumClusters()
        {
            return 1;
        }

        /// <summary>Returns the number of inline (sub-) models (stub → 1 for world).</summary>
        public static int CM_NumInlineModels()
        {
            return 1;
        }

        /// <summary>Returns the map entity string (stub → empty).</summary>
        public static string CM_EntityString()
        {
            return s_entityString;
        }

        // -----------------------------------------------------------------------
        // Layer / content-flag mapping
        // -----------------------------------------------------------------------

        /// <summary>
        /// Maps a Unity physics layer index to ET content flags.
        ///
        /// Layers used in the ET scene:
        ///   0  (Default) → CONTENTS_SOLID
        ///   2  (Trigger)  → 0 (passable)
        ///   4  (Water)    → CONTENTS_WATER
        ///   8  (World)    → CONTENTS_SOLID
        /// </summary>
        public static int LayerToContents(int layer)
        {
            switch (layer)
            {
                case UnityLayer_Default: return Contents.Solid;
                case UnityLayer_Water:   return Contents.Water;
                case UnityLayer_World:   return Contents.Solid;
                case UnityLayer_Trigger: return 0;           // triggers are passable
                default:                return Contents.Solid;
            }
        }

        /// <summary>
        /// Builds a Unity layer mask that covers all ET content flags present
        /// in <paramref name="brushmask"/>.
        ///
        /// CONTENTS_SOLID  → Default (1&lt;&lt;0) | World (1&lt;&lt;8)
        /// CONTENTS_WATER  → Water  (1&lt;&lt;4)
        /// All traces always include Default to catch solid world geometry.
        /// </summary>
        public static int LayerMaskForBrushmask(int brushmask)
        {
            // Default layer is always included for solid world geometry.
            int mask = (1 << UnityLayer_Default) | (1 << UnityLayer_World);

            if ((brushmask & Contents.Water) != 0)
                mask |= (1 << UnityLayer_Water);

            // Triggers are never included in traces (they are passable).

            return mask;
        }

        // -----------------------------------------------------------------------
        // TraceDelegate factories
        // (compatible with TraceFunc / PointContentsFunc from PmoveInput.cs)
        // -----------------------------------------------------------------------

        /// <summary>
        /// Default TraceFunc wired to CM_Trace.
        /// Assign to PmoveInput.Trace to enable ET-compatible collision.
        /// </summary>
        public static TraceFunc DefaultTraceFunc =>
            (start, mins, maxs, end, passEnt, mask) =>
                CM_Trace(start, end, mins, maxs, mask);

        /// <summary>
        /// Default PointContentsFunc wired to CM_PointContents.
        /// Assign to PmoveInput.PointContents to enable ET-compatible contents queries.
        /// </summary>
        public static PointContentsFunc DefaultPointContentsFunc =>
            (point, passEnt) => CM_PointContents(point, 0);

        // -----------------------------------------------------------------------
        // Private helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Converts ET Euler angles (pitch, yaw, roll) to a Unity Quaternion.
        /// ET angles are in degrees: angles.x = pitch, angles.y = yaw, angles.z = roll.
        /// </summary>
        private static Quaternion ETAnglesToQuaternion(Vector3 etAngles)
        {
            // ET: pitch = rotation around Y (in ET coords = forward tilt),
            //     yaw   = rotation around Z (vertical axis),
            //     roll  = rotation around X (side tilt).
            // Unity Quaternion.Euler(pitch, yaw, roll) with our axis mapping:
            //   ET yaw   → Unity Y rotation
            //   ET pitch → Unity X rotation (negated for handedness)
            //   ET roll  → Unity Z rotation
            return Quaternion.Euler(-etAngles.x, etAngles.y, etAngles.z);
        }

        /// <summary>
        /// Returns ET surface flags for a collider.  These would normally come
        /// from the BSP brush surface data; in Unity we derive them from tags
        /// and physics materials as a best-effort approximation.
        /// </summary>
        private static int SurfaceFlagsForCollider(Collider col)
        {
            if (col == null)
                return 0;

            int flags = 0;

            // Tag-based surface classification.
            switch (col.tag)
            {
                case "Metal":  flags |= Surf.Metal;  break;
                case "Wood":   flags |= Surf.Wood;   break;
                case "Glass":  flags |= Surf.Glass;  break;
                case "Grass":  flags |= Surf.Grass;  break;
                case "Gravel": flags |= Surf.Gravel; break;
                case "Snow":   flags |= Surf.Snow;   break;
            }

            // Slippery physics material → Surf.Slick.
            if (col.sharedMaterial != null &&
                col.sharedMaterial.dynamicFriction < 0.1f)
            {
                flags |= Surf.Slick;
            }

            return flags;
        }
    }
}
