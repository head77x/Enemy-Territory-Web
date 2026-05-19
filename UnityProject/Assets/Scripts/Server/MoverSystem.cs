// Ported from: src/game/g_mover.c
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
    public enum MoverState
    {
        Pos1 = 0,   // at start position (door closed)
        Pos2 = 1,   // at end position (door open)
        To1  = 2,   // moving to pos1
        To2  = 3,   // moving to pos2
    }

    // Per-entity mover data not present on GEntity itself.
    internal sealed class MoverData
    {
        public MoverState State    = MoverState.Pos1;
        public Vector3    Pos1;
        public Vector3    Pos2;
        public Vector3    Ang1;
        public Vector3    Ang2;
        public float      Speed    = 400f;
        public float      Wait     = 2f;    // seconds before auto-close; -1 = stay
        public int        Lip      = 8;
        public bool       Rotating;         // true for func_door_rotating / func_rotating
        public string     TeamKey  = "";    // "team" spawnvar value
        public int        Sounds   = 1;     // 0=silent 1=default
        public float      RotSpeed;         // for func_rotating
    }

    public static class MoverSystem
    {
        // ET trajectory type ints
        private const int TR_LINEAR      = (int)TrajectoryType.Linear;
        private const int TR_STATIONARY  = (int)TrajectoryType.Stationary;

        // ET entity type
        private const int ET_MOVER       = (int)EntityType.Mover;
        private const int ET_EXPLOSIVE   = (int)EntityType.Explosive;

        // Brush content mask for movers pushing entities
        private const int MASK_SOLID     = 1;   // CONTENTS_SOLID

        // Per-entity mover bookkeeping
        private static readonly Dictionary<int, MoverData> s_data = new();

        private static MoverData GetData(GEntity ent)
        {
            if (!s_data.TryGetValue(ent.EntityNum, out var d))
            {
                d = new MoverData();
                s_data[ent.EntityNum] = d;
            }
            return d;
        }

        // -----------------------------------------------------------------------
        // G_RunMover
        // -----------------------------------------------------------------------
        public static void G_RunMover(GEntity ent)
        {
            int now = ServerGameLogic.Level.Time;

            var md = GetData(ent);

            if (md.Rotating)
            {
                // Spin entity at constant angular velocity
                float dt      = ServerGameLogic.Level.FrameTime * 0.001f;
                ent.Angles    += new Vector3(0f, md.RotSpeed * dt, 0f);
                ent.S.APos.TrBase0 = ent.Angles.x;
                ent.S.APos.TrBase1 = ent.Angles.y;
                ent.S.APos.TrBase2 = ent.Angles.z;
                ent.NextThink = now + 1;
                return;
            }

            if (ent.NextThink == 0f || now < (int)ent.NextThink) return;

            ent.Think?.Invoke(ent);
        }

        // -----------------------------------------------------------------------
        // InitMover
        // -----------------------------------------------------------------------
        public static void InitMover(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("speed",  "400", out float speed);
            ServerGameLogic.G_SpawnFloat("wait",   "2",   out float wait);
            ServerGameLogic.G_SpawnInt("lip",      "8",   out int lip);
            ServerGameLogic.G_SpawnString("team",  "",    out string team);
            ServerGameLogic.G_SpawnInt("sounds",   "1",   out int sounds);

            var md  = GetData(ent);
            md.Speed  = Mathf.Max(1f, speed);
            md.Wait   = wait;
            md.Lip    = lip;
            md.TeamKey = team ?? "";
            md.Sounds  = sounds;

            ent.S.EType = ET_MOVER;
            ent.S.Pos.TrType = TR_STATIONARY;

            CalcMoverEndPositions(ent, out md.Pos1, out md.Pos2);

            SetMoverState(ent, MoverState.Pos1, ServerGameLogic.Level.Time);

            ent.Think     = null;
            ent.NextThink = 0f;
        }

        // -----------------------------------------------------------------------
        // MatchTeam — synchronize all movers sharing the same team key
        // -----------------------------------------------------------------------
        public static void MatchTeam(GEntity teamLeader, MoverState moverState, float time)
        {
            var ldm = GetData(teamLeader);
            if (string.IsNullOrEmpty(ldm.TeamKey)) return;

            var entities = ServerGameLogic.Entities;
            int count    = ServerGameLogic.Level.NumEntities;

            for (int i = 0; i < count; i++)
            {
                var e = entities[i];
                if (e == null || !e.InUse || e == teamLeader) continue;
                if (!s_data.TryGetValue(e.EntityNum, out var md)) continue;
                if (md.TeamKey != ldm.TeamKey) continue;

                SetMoverState(e, moverState, (int)time);
            }
        }

        // -----------------------------------------------------------------------
        // SetMoverState
        // -----------------------------------------------------------------------
        public static void SetMoverState(GEntity ent, MoverState state, float time)
        {
            var md  = GetData(ent);
            md.State = state;
            int now  = (int)time;

            Vector3 goal;
            Vector3 delta;

            switch (state)
            {
                case MoverState.Pos1:
                    goal  = md.Pos1;
                    delta = goal - ent.Origin;
                    break;
                case MoverState.Pos2:
                    goal  = md.Pos2;
                    delta = goal - ent.Origin;
                    break;
                case MoverState.To2:
                    goal  = md.Pos2;
                    delta = goal - ent.Origin;
                    break;
                case MoverState.To1:
                    goal  = md.Pos1;
                    delta = goal - ent.Origin;
                    break;
                default:
                    goal  = ent.Origin;
                    delta = Vector3.zero;
                    break;
            }

            float dist = delta.magnitude;

            if (dist < 0.1f || md.Speed <= 0f)
            {
                ent.Origin = goal;
                ent.S.Pos.TrType  = TR_STATIONARY;
                ent.S.Pos.TrBase0 = goal.x;
                ent.S.Pos.TrBase1 = goal.y;
                ent.S.Pos.TrBase2 = goal.z;

                if (state == MoverState.To1 || state == MoverState.Pos1)
                    md.State = MoverState.Pos1;
                else
                    md.State = MoverState.Pos2;

                ent.Think     = Reached_BinaryMover;
                ent.NextThink = now;
                return;
            }

            float duration = (dist / md.Speed) * 1000f;  // ms
            Vector3 vel    = delta.normalized * md.Speed;

            ent.S.Pos.TrType   = TR_LINEAR;
            ent.S.Pos.TrTime   = now;
            ent.S.Pos.TrDuration = (int)duration;
            ent.S.Pos.TrBase0  = ent.Origin.x;
            ent.S.Pos.TrBase1  = ent.Origin.y;
            ent.S.Pos.TrBase2  = ent.Origin.z;
            ent.S.Pos.TrDelta0 = vel.x;
            ent.S.Pos.TrDelta1 = vel.y;
            ent.S.Pos.TrDelta2 = vel.z;

            ent.Think     = Reached_BinaryMover;
            ent.NextThink = now + (int)duration;
        }

        // -----------------------------------------------------------------------
        // ReturnToPos1
        // -----------------------------------------------------------------------
        public static void ReturnToPos1(GEntity ent)
        {
            SetMoverState(ent, MoverState.To1, ServerGameLogic.Level.Time);
            MatchTeam(ent, MoverState.To1, ServerGameLogic.Level.Time);
        }

        // -----------------------------------------------------------------------
        // Reached_BinaryMover
        // -----------------------------------------------------------------------
        public static void Reached_BinaryMover(GEntity ent)
        {
            var md  = GetData(ent);
            int now = ServerGameLogic.Level.Time;

            if (md.State == MoverState.To2)
            {
                md.State  = MoverState.Pos2;
                ent.Origin = md.Pos2;
                ent.S.Pos.TrType  = TR_STATIONARY;
                ent.S.Pos.TrBase0 = md.Pos2.x;
                ent.S.Pos.TrBase1 = md.Pos2.y;
                ent.S.Pos.TrBase2 = md.Pos2.z;

                if (md.Wait >= 0f)
                {
                    ent.Think     = ReturnToPos1;
                    ent.NextThink = now + (int)(md.Wait * 1000f);
                }
                else
                {
                    ent.Think     = null;
                    ent.NextThink = 0f;
                }
            }
            else if (md.State == MoverState.To1)
            {
                md.State  = MoverState.Pos1;
                ent.Origin = md.Pos1;
                ent.S.Pos.TrType  = TR_STATIONARY;
                ent.S.Pos.TrBase0 = md.Pos1.x;
                ent.S.Pos.TrBase1 = md.Pos1.y;
                ent.S.Pos.TrBase2 = md.Pos1.z;

                ent.Think     = null;
                ent.NextThink = 0f;
            }
        }

        // -----------------------------------------------------------------------
        // Use_BinaryMover
        // -----------------------------------------------------------------------
        public static void Use_BinaryMover(GEntity ent, GEntity other, GEntity activator)
        {
            var md  = GetData(ent);
            int now = ServerGameLogic.Level.Time;

            if (md.State == MoverState.Pos1)
            {
                SetMoverState(ent, MoverState.To2, now);
                MatchTeam(ent, MoverState.To2, now);
            }
            else if (md.State == MoverState.Pos2)
            {
                if (md.Wait < 0f)
                {
                    // Toggling: manually close
                    SetMoverState(ent, MoverState.To1, now);
                    MatchTeam(ent, MoverState.To1, now);
                }
                // else wait-timer already scheduled
            }
            else if (md.State == MoverState.To1)
            {
                // Reverse mid-travel
                SetMoverState(ent, MoverState.To2, now);
                MatchTeam(ent, MoverState.To2, now);
            }
            else if (md.State == MoverState.To2)
            {
                // Reverse mid-travel
                SetMoverState(ent, MoverState.To1, now);
                MatchTeam(ent, MoverState.To1, now);
            }
        }

        // -----------------------------------------------------------------------
        // Blocked_Door — crush or reverse depending on damage setting
        // -----------------------------------------------------------------------
        public static void Blocked_Door(GEntity ent, GEntity other)
        {
            if (other == null) return;

            if (ent.Damage > 0)
                WeaponSystem.G_Damage(other.EntityNum, ent.EntityNum,
                    ent.Damage, 0, WeaponSystem.MOD_CRUSH);

            // Retract to avoid trapping the entity
            ReturnToPos1(ent);
        }

        // -----------------------------------------------------------------------
        // Touch_DoorTrigger — open a door when a player walks into its trigger zone
        // -----------------------------------------------------------------------
        public static void Touch_DoorTrigger(GEntity ent, GEntity other, TraceResult trace)
        {
            if (other == null || other.Client == null) return;

            // Retrieve the door this trigger is associated with via Enemy link
            GEntity door = ent.Enemy;
            if (door == null) return;

            var md = GetData(door);
            if (md.State != MoverState.Pos1 && md.State != MoverState.To1) return;

            Use_BinaryMover(door, other, other);
        }

        // -----------------------------------------------------------------------
        // SP_func_door
        // -----------------------------------------------------------------------
        public static void SP_func_door(GEntity ent)
        {
            InitMover(ent);

            ServerGameLogic.G_SpawnInt("dmg",    "2",  out int dmg);
            ServerGameLogic.G_SpawnInt("health", "0",  out int health);
            ServerGameLogic.G_SpawnInt("sounds", "1",  out int sounds);

            ent.Damage = dmg;

            if (health > 0)
            {
                ent.Health    = health;
                ent.MaxHealth = health;
                ent.Die       = DoorDie;
            }

            ent.Use     = Use_BinaryMover;
            ent.Touch   = null;

            // Spawn a trigger brush in front of the door for auto-open
            GEntity trigger = ServerGameLogic.G_Spawn();
            if (trigger != null)
            {
                trigger.ClassName = "door_trigger";
                trigger.InUse     = true;
                trigger.Enemy     = ent;
                trigger.Touch     = (trig, other) => Touch_DoorTrigger(trig, other, default);
                trigger.Mins      = ent.Mins - new Vector3(60f, 60f, 0f);
                trigger.Maxs      = ent.Maxs + new Vector3(60f, 60f, 0f);
                trigger.S.EType   = (int)EntityType.TriggerMultiple;
            }
        }

        // -----------------------------------------------------------------------
        // SP_func_door_rotating
        // -----------------------------------------------------------------------
        public static void SP_func_door_rotating(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("speed",  "400", out float speed);
            ServerGameLogic.G_SpawnFloat("wait",   "2",   out float wait);
            ServerGameLogic.G_SpawnFloat("distance", "90", out float distance);
            ServerGameLogic.G_SpawnString("team",  "",    out string team);
            ServerGameLogic.G_SpawnInt("dmg",      "2",   out int dmg);

            var md        = GetData(ent);
            md.Speed      = Mathf.Max(1f, speed);
            md.Wait       = wait;
            md.TeamKey    = team ?? "";
            md.Rotating   = false;          // rotation is discrete, not continuous

            ent.Damage    = dmg;
            ent.S.EType   = ET_MOVER;

            // Closed = current angles; open = current + distance around Z
            md.Ang1       = ent.Angles;
            md.Ang2       = ent.Angles + new Vector3(0f, distance, 0f);
            md.Pos1       = ent.Origin;
            md.Pos2       = ent.Origin;

            SetMoverState(ent, MoverState.Pos1, ServerGameLogic.Level.Time);

            ent.Use   = Use_BinaryMover;
            ent.Touch = null;
        }

        // -----------------------------------------------------------------------
        // SP_func_plat
        // -----------------------------------------------------------------------
        public static void SP_func_plat(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("speed",  "200", out float speed);
            ServerGameLogic.G_SpawnFloat("height", "0",   out float height);
            ServerGameLogic.G_SpawnFloat("wait",   "1",   out float wait);

            var md    = GetData(ent);
            md.Speed  = Mathf.Max(1f, speed);
            md.Wait   = wait;

            ent.S.EType = ET_MOVER;

            // pos2 is down; use height if provided, otherwise use size along Z
            float travel = height > 0f
                ? height
                : (ent.Maxs.z - ent.Mins.z - 8f);

            md.Pos1 = ent.Origin;
            md.Pos2 = ent.Origin - new Vector3(0f, 0f, travel);

            // Plat starts at top (pos1 = top, pos2 = bottom) — inverted from door
            SetMoverState(ent, MoverState.Pos2, ServerGameLogic.Level.Time);

            ent.Use   = Use_BinaryMover;
            ent.Touch = (e, other) =>
            {
                if (other?.Client == null) return;
                var pmd = GetData(e);
                if (pmd.State == MoverState.Pos2)
                    Use_BinaryMover(e, other, other);
            };
        }

        // -----------------------------------------------------------------------
        // SP_func_train
        // -----------------------------------------------------------------------
        public static void SP_func_train(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("speed", "100", out float speed);
            ServerGameLogic.G_SpawnString("target", "", out string target);

            var md    = GetData(ent);
            md.Speed  = Mathf.Max(1f, speed);
            md.Wait   = 0f;

            ent.S.EType = ET_MOVER;
            ent.S.Pos.TrType = TR_STATIONARY;

            // Resolve first path_corner via Target; defer actual path following to
            // the first Think tick so all entities are spawned.
            ent.Think     = TrainNextPath;
            ent.NextThink = ServerGameLogic.Level.Time + 1;
        }

        private static void TrainNextPath(GEntity ent)
        {
            GEntity corner = ent.Target;
            if (corner == null) return;

            var md = GetData(ent);
            md.Pos1 = ent.Origin;
            md.Pos2 = corner.Origin;

            SetMoverState(ent, MoverState.To2, ServerGameLogic.Level.Time);

            // Chain to next corner when reached
            ent.Think = (e) =>
            {
                e.Target = corner.Target;
                TrainNextPath(e);
            };
        }

        // -----------------------------------------------------------------------
        // SP_func_rotating
        // -----------------------------------------------------------------------
        public static void SP_func_rotating(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("speed", "100", out float speed);

            var md       = GetData(ent);
            md.Rotating  = true;
            md.RotSpeed  = speed;

            ent.S.EType = ET_MOVER;
            ent.S.APos.TrType = TR_LINEAR;
            ent.S.APos.TrBase0 = ent.Angles.x;
            ent.S.APos.TrBase1 = ent.Angles.y;
            ent.S.APos.TrBase2 = ent.Angles.z;
            // Angular velocity stored in TrDelta1 (yaw axis)
            ent.S.APos.TrDelta0 = 0f;
            ent.S.APos.TrDelta1 = speed;
            ent.S.APos.TrDelta2 = 0f;

            ent.Think     = G_RunMover;
            ent.NextThink = ServerGameLogic.Level.Time + 1;
        }

        // -----------------------------------------------------------------------
        // SP_func_button
        // -----------------------------------------------------------------------
        public static void SP_func_button(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("speed", "400", out float speed);
            ServerGameLogic.G_SpawnFloat("wait",  "1",   out float wait);
            ServerGameLogic.G_SpawnInt("health",  "0",   out int health);

            var md    = GetData(ent);
            md.Speed  = Mathf.Max(1f, speed);
            md.Wait   = wait;

            ent.S.EType = ET_MOVER;

            CalcMoverEndPositions(ent, out md.Pos1, out md.Pos2);
            SetMoverState(ent, MoverState.Pos1, ServerGameLogic.Level.Time);

            if (health > 0)
            {
                ent.Health    = health;
                ent.MaxHealth = health;
                ent.Die       = ButtonDie;
            }

            // Buttons activate their target when pressed, then return
            ent.Use   = (e, other, activator) =>
            {
                var bmd = GetData(e);
                if (bmd.State != MoverState.Pos1) return;
                SetMoverState(e, MoverState.To2, ServerGameLogic.Level.Time);
                FireTargets(e, activator);
            };

            ent.Touch = (e, other) => e.Use?.Invoke(e, other, other);
        }

        // -----------------------------------------------------------------------
        // SP_func_static — non-moving solid brush
        // -----------------------------------------------------------------------
        public static void SP_func_static(GEntity ent)
        {
            ent.S.EType = ET_MOVER;
            ent.S.Pos.TrType  = TR_STATIONARY;
            ent.S.Pos.TrBase0 = ent.Origin.x;
            ent.S.Pos.TrBase1 = ent.Origin.y;
            ent.S.Pos.TrBase2 = ent.Origin.z;
            ent.Think     = null;
            ent.NextThink = 0f;
        }

        // -----------------------------------------------------------------------
        // SP_func_explosive
        // -----------------------------------------------------------------------
        public static void SP_func_explosive(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("health", "100", out int health);
            ServerGameLogic.G_SpawnInt("mass",   "75",  out int mass);

            ent.S.EType  = ET_EXPLOSIVE;
            ent.Health   = health;
            ent.MaxHealth = health;
            ent.Damage   = mass;

            ent.S.Pos.TrType  = TR_STATIONARY;
            ent.S.Pos.TrBase0 = ent.Origin.x;
            ent.S.Pos.TrBase1 = ent.Origin.y;
            ent.S.Pos.TrBase2 = ent.Origin.z;

            ent.Die = (e, inflictor, method) =>
            {
                e.InUse  = false;
                // Raise a radius damage event centered on the brush origin
                MissileSystem.G_RadiusDamage(e.Origin, inflictor,
                    e.SplashDamage > 0 ? e.SplashDamage : e.Damage,
                    e.SplashRadius  > 0 ? e.SplashRadius  : 120f,
                    e, WeaponSystem.MOD_DYNAMITE_SPLASH);
            };
        }

        // -----------------------------------------------------------------------
        // CalcMoverEndPositions
        // -----------------------------------------------------------------------
        private static void CalcMoverEndPositions(GEntity ent, out Vector3 pos1, out Vector3 pos2)
        {
            pos1 = ent.Origin;

            // Derive movement axis from angle
            float ang = ent.Angle;
            Vector3 dir;

            if (ang == -1f)       dir = new Vector3(0f, 0f, 1f);   // up
            else if (ang == -2f)  dir = new Vector3(0f, 0f, -1f);  // down
            else
            {
                float rad = ang * Mathf.Deg2Rad;
                dir       = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            }

            // Travel distance: full size along movement axis minus lip
            var  md   = GetData(ent);
            float size = Mathf.Abs(Vector3.Dot(ent.Maxs - ent.Mins, dir));
            float dist = size - md.Lip;
            if (dist < 4f) dist = 4f;

            pos2 = pos1 + dir * dist;
        }

        // -----------------------------------------------------------------------
        // DirectionToAngles  — mirrors VectorToAngles
        // -----------------------------------------------------------------------
        private static Vector3 DirectionToAngles(Vector3 dir)
        {
            if (dir == Vector3.zero) return Vector3.zero;

            float yaw   = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(dir.z, new Vector2(dir.x, dir.y).magnitude) * Mathf.Rad2Deg;
            return new Vector3(pitch, yaw, 0f);
        }

        // -----------------------------------------------------------------------
        // Internal helpers
        // -----------------------------------------------------------------------

        private static void DoorDie(GEntity ent, GEntity inflictor, string method)
        {
            ent.Health = -999;
            // Remove blocking solid so players can pass through
            Use_BinaryMover(ent, inflictor, inflictor);
            ent.Use  = null;
            ent.Die  = null;
            ent.Touch = null;
        }

        private static void ButtonDie(GEntity ent, GEntity inflictor, string method)
        {
            FireTargets(ent, inflictor);
            // Buttons stay in pressed position after being shot
            var md = GetData(ent);
            SetMoverState(ent, MoverState.To2, ServerGameLogic.Level.Time);
            ent.Die  = null;
            ent.Use  = null;
        }

        private static void FireTargets(GEntity ent, GEntity activator)
        {
            if (ent.Target == null) return;
            ent.Target.Use?.Invoke(ent.Target, ent, activator);
        }
    }
}
