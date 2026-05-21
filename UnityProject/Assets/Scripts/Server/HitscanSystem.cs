// Ported from: src/game/g_weapon.c (fire_lead, FireWeapon dispatch)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Core;
using ET.Game;

namespace ET.Server
{
    /// <summary>
    /// Subscribes to WeaponSystem.OnFireWeapon and DamageSystem.OnDamageResolved,
    /// completing the weapon-fire pipeline that WeaponSystem intentionally leaves
    /// to the game layer.
    ///
    ///   OnFireWeapon  → hitscan traceline (bullets) or projectile launch
    ///   OnDamageResolved → apply FinalDamage to GEntity.Health / PlayerState
    /// </summary>
    public static class HitscanSystem
    {
        // Maximum trace distance — matches ET's RANGE_AIM (g_weapon.c).
        private const float HITSCAN_RANGE = 8192f;

        // Spread integer in WeaponInfo is in 1/100-degree units.
        private const float SPREAD_TO_DEG = 0.01f;

        // MASK_SHOT: CONTENTS_SOLID | CONTENTS_BODY (same as MissileSystem)
        private const int MASK_SHOT = 0x10001;

        // Default player respawn delay in milliseconds (ET objective mode).
        private const int RESPAWN_DELAY_MS = 20000;

        static HitscanSystem()
        {
            WeaponSystem.OnFireWeapon     += HandleFireWeapon;
            DamageSystem.OnDamageResolved += ApplyDamageToEntity;
        }

        /// <summary>
        /// Force the static constructor to run.  Call once at game startup
        /// (e.g. from ServerGameLogic.G_InitGame).
        /// </summary>
        public static void Init() { }

        // -----------------------------------------------------------------------
        // OnFireWeapon handler
        // -----------------------------------------------------------------------
        private static void HandleFireWeapon(WeaponSystem.FireInfo fire)
        {
            if (WeaponSystem.IsSplashWeapon(fire.Weapon))
                LaunchProjectile(fire);
            else
                PerformHitscan(fire);
        }

        // -----------------------------------------------------------------------
        // Hitscan (fire_lead equivalent)
        // -----------------------------------------------------------------------
        private static void PerformHitscan(WeaponSystem.FireInfo fire)
        {
            Vector3 dir = fire.MuzzleDir.normalized;

            // Apply random spread cone (same formula as BulletFire in MissileSystem).
            if (fire.Spread > 0f)
            {
                float spreadDeg = fire.Spread * SPREAD_TO_DEG;
                var   rng       = new System.Random();
                float r = ((float)rng.NextDouble() * 2f - 1f) * spreadDeg * Mathf.Deg2Rad;
                float u = ((float)rng.NextDouble() * 2f - 1f) * spreadDeg * Mathf.Deg2Rad;
                dir += new Vector3(r, 0f, u);
                dir.Normalize();
            }

            Vector3 end = fire.MuzzleOrigin + dir * HITSCAN_RANGE;

            // ---- Find closest player AABB intersection ----
            int     bestTarget = GameConst.ENTITYNUM_NONE;
            float   bestFrac   = 1f;
            Vector3 bestHitPt  = end;

            for (int i = 0; i < ServerGameLogic.MAX_GENTITIES; i++)
            {
                if (i == fire.ClientNum) continue;

                var ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;

                // Skip dead players
                if (ent.Client.PS.PmType == GameConst.PM_DEAD) continue;

                // AABB extents from PlayerState (ET space)
                var ps = ent.Client.PS;
                Vector3 bMin = ent.Origin + new Vector3(ps.Mins0, ps.Mins1, ps.Mins2);
                Vector3 bMax = ent.Origin + new Vector3(ps.Maxs0, ps.Maxs1, ps.Maxs2);

                float   frac;
                Vector3 hitPt;
                if (RayIntersectsAabb(fire.MuzzleOrigin, dir, HITSCAN_RANGE, bMin, bMax,
                                      out frac, out hitPt) && frac < bestFrac)
                {
                    bestFrac   = frac;
                    bestTarget = i;
                    bestHitPt  = hitPt;
                }
            }

            // ---- World geometry check — bullet stops at walls ----
            var worldTr = CollisionSystem.CM_BoxTrace(
                fire.MuzzleOrigin, end,
                Vector3.zero, Vector3.zero,
                0, MASK_SHOT);

            if (worldTr.Fraction < bestFrac)
                return; // wall is closer than any player

            if (bestTarget == GameConst.ENTITYNUM_NONE) return;

            var target   = ServerGameLogic.Entities[bestTarget];
            bool headshot = WeaponSystem.G_IsHeadShot(bestHitPt, target.Origin);

            WeaponSystem.G_Damage(bestTarget, fire.ClientNum, fire.Damage, 0, fire.Mod, headshot);

            Debug.Log($"[HitscanSystem] Hit entity {bestTarget} by client {fire.ClientNum} " +
                      $"weapon={fire.Weapon} dmg={fire.Damage} headshot={headshot}");
        }

        // -----------------------------------------------------------------------
        // Projectile dispatch
        // -----------------------------------------------------------------------
        private static void LaunchProjectile(WeaponSystem.FireInfo fire)
        {
            var owner = ServerGameLogic.Entities[fire.ClientNum];
            if (owner == null) return;

            Vector3 start = fire.MuzzleOrigin;
            Vector3 dir   = fire.MuzzleDir.normalized;

            switch (fire.Weapon)
            {
                case GameConst.WP_GRENADE_LAUNCHER:
                case GameConst.WP_GRENADE_PINEAPPLE:
                case GameConst.WP_GPG40:
                case GameConst.WP_M7:
                    MissileSystem.fire_grenade(owner, start, dir);
                    break;

                case GameConst.WP_PANZERFAUST:
                    MissileSystem.fire_rocket(owner, start, dir);
                    break;

                case GameConst.WP_FLAMETHROWER:
                    MissileSystem.fire_flamechunk(owner, start, dir);
                    break;

                case GameConst.WP_MORTAR:
                case GameConst.WP_MORTAR_SET:
                    MissileSystem.fire_mortar(owner, start, dir);
                    break;

                case GameConst.WP_DYNAMITE:
                    MissileSystem.fire_dynamite(owner, start, dir);
                    break;

                case GameConst.WP_LANDMINE:
                    MissileSystem.fire_landmine(owner, start, dir);
                    break;

                case GameConst.WP_SATCHEL:
                    MissileSystem.fire_satchelcharge(owner, start, dir);
                    break;
            }
        }

        // -----------------------------------------------------------------------
        // OnDamageResolved handler — apply FinalDamage to entity health
        // -----------------------------------------------------------------------
        private static void ApplyDamageToEntity(DamageSystem.DamageOutcome outcome)
        {
            int idx = outcome.TargetEntityNum;
            if ((uint)idx >= (uint)ServerGameLogic.MAX_GENTITIES) return;

            var ent = ServerGameLogic.Entities[idx];
            if (ent == null || !ent.InUse) return;

            ent.Health -= outcome.FinalDamage;

            if (ent.Client != null)
            {
                var ps = ent.Client.PS;
                ps.Stats[GameConst.STAT_HEALTH] = (short)Mathf.Max(ent.Health, short.MinValue);

                if (ent.Health <= 0 && ps.PmType != GameConst.PM_DEAD)
                {
                    ps.PmType              = GameConst.PM_DEAD;
                    ent.Client.DeathTime   = ServerGameLogic.Level.Time;
                    ent.Client.RespawnTime = ServerGameLogic.Level.Time + RESPAWN_DELAY_MS;

                    Debug.Log($"[HitscanSystem] Player {idx} killed by {outcome.AttackerEntityNum} " +
                              $"(mod={outcome.Mod} headshot={outcome.Headshot} " +
                              $"finalDmg={outcome.FinalDamage})");
                }
            }
        }

        // -----------------------------------------------------------------------
        // Ray vs AABB intersection — slab method
        // Returns true and sets fraction/hitPoint if the ray hits within [0, maxDist].
        // -----------------------------------------------------------------------
        private static bool RayIntersectsAabb(
            Vector3 origin, Vector3 dir, float maxDist,
            Vector3 bMin, Vector3 bMax,
            out float fraction, out Vector3 hitPoint)
        {
            fraction = 0f;
            hitPoint = origin;

            float tmin = 0f;
            float tmax = maxDist;

            for (int axis = 0; axis < 3; axis++)
            {
                float o  = V(origin, axis);
                float d  = V(dir,    axis);
                float lo = V(bMin,   axis);
                float hi = V(bMax,   axis);

                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (o < lo || o > hi) return false;
                }
                else
                {
                    float t1 = (lo - o) / d;
                    float t2 = (hi - o) / d;
                    if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                    tmin = Mathf.Max(tmin, t1);
                    tmax = Mathf.Min(tmax, t2);
                    if (tmin > tmax) return false;
                }
            }

            if (tmax < 0f || tmin > maxDist) return false;

            fraction = tmin / maxDist;
            hitPoint = origin + dir * tmin;
            return true;
        }

        private static float V(Vector3 v, int axis)
        {
            switch (axis) { case 0: return v.x; case 1: return v.y; default: return v.z; }
        }
    }
}
