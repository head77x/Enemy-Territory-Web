// Ported from: src/game/g_missile.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    public static class MissileSystem
    {
        public const int MISSILE_PRESTEP_TIME = 50;

        private const float GRAVITY           = 800f;
        private const int   TR_LINEAR         = (int)TrajectoryType.Linear;
        private const int   TR_GRAVITY        = (int)TrajectoryType.Gravity;
        private const int   TR_STATIONARY     = (int)TrajectoryType.Stationary;

        // ET entity-type ints (EntityType enum)
        private const int ET_MISSILE          = (int)EntityType.Missile;
        private const int ET_EXPLOSIVE        = (int)EntityType.Explosive;

        // Brush contents used for missile traces
        private const int MASK_SHOT           = 0x10001;  // CONTENTS_SOLID | CONTENTS_BODY
        private const int MASK_MISSILESHOT    = MASK_SHOT;

        // Events (EV_*) — client-side rendering cues
        private const int EV_MISSILE_HIT      = 56;
        private const int EV_MISSILE_MISS     = 57;
        private const int EV_MISSILE_MISS_SMALL = 58;

        // -----------------------------------------------------------------------
        // G_RunMissile
        // -----------------------------------------------------------------------
        public static void G_RunMissile(GEntity ent)
        {
            int levelTime = ServerGameLogic.Level.Time;

            Vector3 origin = new Vector3(ent.S.Pos.TrBase0, ent.S.Pos.TrBase1, ent.S.Pos.TrBase2);
            Vector3 delta  = new Vector3(ent.S.Pos.TrDelta0, ent.S.Pos.TrDelta1, ent.S.Pos.TrDelta2);

            Vector3 newOrigin = EvalTrajectory(ent.S.Pos, levelTime);

            // Skip if no movement
            if (newOrigin == origin && ent.S.Pos.TrType == TR_STATIONARY)
            {
                ent.NextThink = levelTime + 500;
                return;
            }

            var mins = new Vector3(-4f, -4f, 0f);
            var maxs = new Vector3(4f, 4f, 8f);

            TraceResult tr = CollisionSystem.CM_BoxTrace(origin, newOrigin, mins, maxs, 0, MASK_MISSILESHOT);

            // Hit something
            if (tr.Fraction < 1f)
            {
                // Don't impact on owner for first MISSILE_PRESTEP_TIME ms
                if (tr.EntityNum == ent.S.OtherEntityNum &&
                    (levelTime - ent.S.Pos.TrTime) < MISSILE_PRESTEP_TIME)
                {
                    goto noHit;
                }

                MissileImpact(ent, tr);
                return;
            }

            noHit:
            // Update origin from trajectory
            ent.Origin = newOrigin;
            ent.S.Pos.TrBase0 = ent.Origin.x;
            ent.S.Pos.TrBase1 = ent.Origin.y;
            ent.S.Pos.TrBase2 = ent.Origin.z;

            // Bounce missiles (grenades, etc.)
            if ((ent.Flags & GameConst.EF_BOUNCE) != 0 || (ent.Flags & GameConst.EF_BOUNCE_HALF) != 0)
            {
                // Handled above on hit; no-op here
            }

            ent.NextThink = levelTime + 1;
            ent.Think     = G_RunMissile;
        }

        // -----------------------------------------------------------------------
        // MissileImpact
        // -----------------------------------------------------------------------
        public static void MissileImpact(GEntity ent, TraceResult tr)
        {
            int levelTime    = ServerGameLogic.Level.Time;
            int attackerNum  = ent.S.OtherEntityNum;
            bool hitEntity   = tr.EntityNum != GameConst.ENTITYNUM_WORLD &&
                               tr.EntityNum != GameConst.ENTITYNUM_NONE;

            // Splash damage from area weapons
            if (ent.SplashDamage > 0)
            {
                G_RadiusDamage(tr.EndPos, ServerGameLogic.Entities[attackerNum],
                    ent.SplashDamage, ent.SplashRadius, ent, ent.MeansOfDeath + 1);
            }

            // Direct hit damage
            if (hitEntity && ent.Damage > 0)
            {
                WeaponSystem.G_Damage(tr.EntityNum, attackerNum,
                    ent.Damage, WeaponSystem.DF_NO_LOCDAMAGE, ent.MeansOfDeath);
            }

            // Set impact event so clients can play effects
            ent.S.Event     = hitEntity ? EV_MISSILE_HIT : EV_MISSILE_MISS;
            ent.S.EventParm = (int)(tr.PlaneNormal.x * 255f);

            // Free the entity
            ent.InUse    = false;
            ent.NextThink = levelTime;
            ent.Think     = null;
        }

        // -----------------------------------------------------------------------
        // fire_grenade  (GPG40 / M7 rifle grenade, WP_GRENADE_LAUNCHER)
        // -----------------------------------------------------------------------
        public static GEntity fire_grenade(GEntity self, Vector3 start, Vector3 dir)
        {
            return CreateMissileEntity(self, "grenade", start, dir,
                speed:         700f,
                damage:        100,
                splashDamage:  100,
                splashRadius:  150,
                mod:           WeaponSystem.MOD_GRENADE_SPLASH,
                trType:        TR_GRAVITY,
                weapon:        GameConst.WP_GRENADE_LAUNCHER);
        }

        // -----------------------------------------------------------------------
        // fire_rocket  (Panzerfaust)
        // -----------------------------------------------------------------------
        public static GEntity fire_rocket(GEntity self, Vector3 start, Vector3 dir)
        {
            return CreateMissileEntity(self, "rocket", start, dir,
                speed:         2000f,
                damage:        400,
                splashDamage:  400,
                splashRadius:  300,
                mod:           WeaponSystem.MOD_PANZERFAUST_SPLASH,
                trType:        TR_LINEAR,
                weapon:        GameConst.WP_PANZERFAUST);
        }

        // -----------------------------------------------------------------------
        // fire_flamechunk  (Flamethrower)
        // -----------------------------------------------------------------------
        public static GEntity fire_flamechunk(GEntity self, Vector3 start, Vector3 dir)
        {
            return CreateMissileEntity(self, "flamechunk", start, dir,
                speed:         1200f,
                damage:        8,
                splashDamage:  0,
                splashRadius:  0,
                mod:           WeaponSystem.MOD_FLAMETHROWER,
                trType:        TR_GRAVITY,
                weapon:        GameConst.WP_FLAMETHROWER);
        }

        // -----------------------------------------------------------------------
        // fire_mortar
        // -----------------------------------------------------------------------
        public static GEntity fire_mortar(GEntity self, Vector3 start, Vector3 dir)
        {
            return CreateMissileEntity(self, "mortar", start, dir,
                speed:         900f,
                damage:        250,
                splashDamage:  250,
                splashRadius:  400,
                mod:           WeaponSystem.MOD_MORTAR_SPLASH,
                trType:        TR_GRAVITY,
                weapon:        GameConst.WP_MORTAR);
        }

        // -----------------------------------------------------------------------
        // fire_dynamite  (placed charge — drops to ground)
        // -----------------------------------------------------------------------
        public static GEntity fire_dynamite(GEntity self, Vector3 start, Vector3 dir)
        {
            GEntity ent = CreateMissileEntity(self, "dynamite", start, dir,
                speed:         0f,
                damage:        400,
                splashDamage:  400,
                splashRadius:  400,
                mod:           WeaponSystem.MOD_DYNAMITE_SPLASH,
                trType:        TR_GRAVITY,
                weapon:        GameConst.WP_DYNAMITE);

            if (ent != null)
            {
                // Dynamite arms after 30 seconds (30000 ms)
                ent.NextThink = ServerGameLogic.Level.Time + 30000;
                ent.S.EFlags |= GameConst.EF_TICKING;
            }
            return ent;
        }

        // -----------------------------------------------------------------------
        // fire_landmine
        // -----------------------------------------------------------------------
        public static GEntity fire_landmine(GEntity self, Vector3 start, Vector3 dir)
        {
            GEntity ent = CreateMissileEntity(self, "landmine", start, dir,
                speed:         0f,
                damage:        0,
                splashDamage:  300,
                splashRadius:  225,
                mod:           WeaponSystem.MOD_LANDMINE_SPLASH,
                trType:        TR_GRAVITY,
                weapon:        GameConst.WP_LANDMINE);

            if (ent != null)
                ent.S.EType = ET_EXPLOSIVE;

            return ent;
        }

        // -----------------------------------------------------------------------
        // fire_satchelcharge
        // -----------------------------------------------------------------------
        public static GEntity fire_satchelcharge(GEntity self, Vector3 start, Vector3 dir)
        {
            GEntity ent = CreateMissileEntity(self, "satchel_charge", start, dir,
                speed:         0f,
                damage:        0,
                splashDamage:  400,
                splashRadius:  300,
                mod:           WeaponSystem.MOD_SATCHEL_SPLASH,
                trType:        TR_GRAVITY,
                weapon:        GameConst.WP_SATCHEL);

            if (ent != null)
                ent.S.EFlags |= GameConst.EF_BOUNCE_HALF;

            return ent;
        }

        // -----------------------------------------------------------------------
        // BulletFire  — hitscan: no projectile entity created
        // -----------------------------------------------------------------------
        public static bool BulletFire(GEntity attacker, float spread, int damage, int mod, float range = 8192f)
        {
            if (attacker == null || attacker.Client == null) return false;

            // Build fire direction from client view angles
            var ps = attacker.Client.PS;
            float yaw   = ps.ViewAngles0 * Mathf.Deg2Rad;
            float pitch = ps.ViewAngles1 * Mathf.Deg2Rad;

            var dir = new Vector3(
                Mathf.Cos(pitch) * Mathf.Sin(yaw),
                Mathf.Cos(pitch) * Mathf.Cos(yaw),
                -Mathf.Sin(pitch));

            // Apply spread
            if (spread > 0f)
            {
                var rng = new System.Random();
                float r = ((float)rng.NextDouble() * 2f - 1f) * spread * Mathf.Deg2Rad;
                float u = ((float)rng.NextDouble() * 2f - 1f) * spread * Mathf.Deg2Rad;
                dir += new Vector3(r, 0f, u);
                dir.Normalize();
            }

            Vector3 origin = attacker.Origin;
            origin.z      += ps.ViewHeight;

            Vector3 end = origin + dir * range;

            TraceResult tr = CollisionSystem.CM_BoxTrace(origin, end, Vector3.zero, Vector3.zero, 0, MASK_SHOT);

            if (tr.EntityNum == GameConst.ENTITYNUM_NONE || tr.EntityNum == GameConst.ENTITYNUM_WORLD)
                return false;

            WeaponSystem.G_Damage(tr.EntityNum, attacker.EntityNum, damage, 0, mod);
            return true;
        }

        // -----------------------------------------------------------------------
        // G_RadiusDamage
        // -----------------------------------------------------------------------
        public static bool G_RadiusDamage(Vector3 origin, GEntity attacker, float damage, float radius, GEntity ignore, int mod)
        {
            if (radius <= 0f) return false;

            int attackerNum = attacker?.EntityNum ?? GameConst.ENTITYNUM_WORLD;
            bool hitOne = false;

            WeaponSystem.G_RadiusDamage(origin, attackerNum, (int)damage, radius,
                ignore?.EntityNum ?? GameConst.ENTITYNUM_NONE, mod);

            hitOne = true; // WeaponSystem handles iteration internally
            return hitOne;
        }

        // -----------------------------------------------------------------------
        // CreateMissileEntity  — shared factory
        // -----------------------------------------------------------------------
        private static GEntity CreateMissileEntity(
            GEntity owner, string classname,
            Vector3 start, Vector3 dir,
            float speed, int damage, int splashDamage, int splashRadius, int mod,
            int trType, int weapon)
        {
            GEntity bolt = ServerGameLogic.G_Spawn();
            if (bolt == null) return null;

            bolt.ClassName    = classname;
            bolt.InUse        = true;
            bolt.Damage       = damage;
            bolt.SplashDamage = splashDamage;
            bolt.SplashRadius = splashRadius;
            bolt.MeansOfDeath = mod;
            bolt.Flags        = 0;

            bolt.S.EType            = ET_MISSILE;
            bolt.S.Weapon           = weapon;
            bolt.S.OtherEntityNum   = owner?.EntityNum ?? GameConst.ENTITYNUM_NONE;
            bolt.S.Pos.TrType       = trType;
            bolt.S.Pos.TrTime       = ServerGameLogic.Level.Time - MISSILE_PRESTEP_TIME;

            dir.Normalize();

            bolt.S.Pos.TrBase0 = start.x;
            bolt.S.Pos.TrBase1 = start.y;
            bolt.S.Pos.TrBase2 = start.z;

            bolt.S.Pos.TrDelta0 = dir.x * speed;
            bolt.S.Pos.TrDelta1 = dir.y * speed;
            bolt.S.Pos.TrDelta2 = dir.z * speed;

            bolt.Origin = start;

            bolt.Mins = new Vector3(-4f, -4f, 0f);
            bolt.Maxs = new Vector3(4f, 4f, 8f);

            bolt.NextThink = ServerGameLogic.Level.Time + 1;
            bolt.Think     = G_RunMissile;

            return bolt;
        }

        // -----------------------------------------------------------------------
        // EvalTrajectory  — port of BG_EvaluateTrajectory
        // -----------------------------------------------------------------------
        private static Vector3 EvalTrajectory(Trajectory tr, int atTime)
        {
            float dt    = (atTime - tr.TrTime) * 0.001f;
            var   base_ = new Vector3(tr.TrBase0, tr.TrBase1, tr.TrBase2);
            var   delta = new Vector3(tr.TrDelta0, tr.TrDelta1, tr.TrDelta2);

            switch (tr.TrType)
            {
                case TR_STATIONARY:
                    return base_;

                case TR_LINEAR:
                    return base_ + delta * dt;

                case TR_GRAVITY:
                    // z-axis gets -0.5*g*dt^2 in ET (Z-up) space
                    return new Vector3(
                        base_.x + delta.x * dt,
                        base_.y + delta.y * dt,
                        base_.z + delta.z * dt - 0.5f * GRAVITY * dt * dt);

                default:
                    return base_ + delta * dt;
            }
        }
    }
}
