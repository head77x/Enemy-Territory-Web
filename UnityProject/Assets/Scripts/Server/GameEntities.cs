// Ported from: src/game/g_utils.c, src/game/g_target.c, src/game/g_trigger.c, src/game/g_alarm.c
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
    // =========================================================================
    // GameUtils — g_utils.c
    // =========================================================================
    public static class GameUtils
    {
        private static readonly System.Random _rng = new System.Random();

        public static GEntity G_Find(GEntity from, string field, string value)
        {
            int start = from != null ? from.EntityNum + 1 : 0;
            var level  = ServerGameLogic.Level;

            for (int i = start; i < level.NumEntities; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e == null || !e.InUse) continue;

                string fieldVal = field == "classname"  ? e.ClassName
                                : field == "targetname" ? e.TargetName
                                : null;
                if (fieldVal == value) return e;
            }
            return null;
        }

        public static List<GEntity> G_FindRadius(Vector3 origin, float radius, GEntity ignore = null)
        {
            var result   = new List<GEntity>();
            float radius2 = radius * radius;
            var level     = ServerGameLogic.Level;

            for (int i = 0; i < level.NumEntities; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e == null || !e.InUse) continue;
                if (e == ignore) continue;
                if ((e.Origin - origin).sqrMagnitude <= radius2)
                    result.Add(e);
            }
            return result;
        }

        public static GEntity G_PickTarget(string targetName)
        {
            if (string.IsNullOrEmpty(targetName)) return null;

            var choices = new List<GEntity>(8);
            var level   = ServerGameLogic.Level;
            for (int i = 0; i < level.NumEntities; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e != null && e.InUse && e.TargetName == targetName)
                    choices.Add(e);
            }
            if (choices.Count == 0) return null;
            return choices[_rng.Next(choices.Count)];
        }

        // Fire all entities whose TargetName matches ent.TargetName.
        // Delegates to the authoritative copy in ServerGameLogic so linking
        // behaviour stays in one place.
        public static void G_UseTargets(GEntity ent, GEntity activator)
        {
            ServerGameLogic.G_UseTargets(ent, activator);
        }

        // Converts the legacy angle convention (angles.y == yaw) to a movedir
        // vector, matching VectorForDir in g_utils.c.
        public static void G_SetMovedir(ref Vector3 angles, ref Vector3 movedir)
        {
            // Special values: pitch == -1 means straight up, -2 means down.
            if (angles == new Vector3(-1f, 0f, 0f))
            {
                movedir = Vector3.up;
            }
            else if (angles == new Vector3(-2f, 0f, 0f))
            {
                movedir = Vector3.down;
            }
            else
            {
                float yaw = angles.y * Mathf.Deg2Rad;
                movedir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
            }
            angles = Vector3.zero;
        }

        public static void G_InitGentity(GEntity ent)
        {
            ent.InUse      = true;
            ent.ClassName  = "noclass";
            ent.Health     = 0;
            ent.Flags      = 0;
            ent.NextThink  = 0f;
            ent.Think      = null;
            ent.Touch      = null;
            ent.Use        = null;
            ent.Pain       = null;
            ent.Die        = null;
            ent.Enemy      = null;
            ent.Target     = null;
            ent.TargetName = null;
            ent.S          = new EntityState();
            ent.S.Number   = ent.EntityNum;
            ent.Client     = null;
        }

        public static GEntity G_Spawn() => ServerGameLogic.G_Spawn();

        public static void G_FreeEntity(GEntity ent) => ServerGameLogic.G_FreeEntity(ent);

        public static GEntity G_TempEntity(Vector3 origin, int event_) =>
            ServerGameLogic.G_TempEntity(origin, event_);

        public static void G_Sound(GEntity ent, int channel, int soundIndex) =>
            ServerGameLogic.G_Sound(ent, channel, soundIndex);

        public static void G_SetOrigin(GEntity ent, Vector3 origin) =>
            ServerGameLogic.G_SetOrigin(ent, origin);

        public static void G_AddPredictableEvent(GEntity ent, int event_, int eventParm)
        {
            if (ent == null) return;
            ent.S.Event     = event_;
            ent.S.EventParm = eventParm;
        }

        public static void G_AddEvent(GEntity ent, int event_, int eventParm)
        {
            if (ent == null) return;
            ent.S.Event     = event_;
            ent.S.EventParm = eventParm;
        }

        public static void G_BroadcastSound(int soundIndex, Vector3 origin)
        {
            var temp = G_TempEntity(origin, soundIndex);
            if (temp == null) return;
            temp.S.EType = (int)EntityType.Speaker;
        }

        // Routes through WeaponSystem.G_Damage which raises DamageSystem events.
        public static void G_Damage(GEntity target, GEntity inflictor, GEntity attacker,
            Vector3 dir, Vector3 point, int damage, int dflags, int mod)
        {
            if (target == null) return;
            int attackerNum = attacker?.EntityNum ?? GameConst.ENTITYNUM_WORLD;
            WeaponSystem.G_Damage(target.EntityNum, attackerNum, damage, dflags, mod);
        }

        public static string VectorToString(Vector3 v) => $"{v.x:F0} {v.y:F0} {v.z:F0}";
    }

    // =========================================================================
    // GameTargets — g_target.c
    // =========================================================================
    public static class GameTargets
    {
        public static event Action<string>         OnLocationEntered;
        public static event Action<Vector3, float> OnRumble;

        // target_kill --------------------------------------------------------
        public static void SP_target_kill(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                if (activator == null) return;
                activator.Die?.Invoke(activator, activator, activator);
            };
        }

        // target_remove_powerups ---------------------------------------------
        public static void SP_target_remove_powerups(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                if (activator?.Client == null) return;
                // Clear powerup timers stored in Powerups[].
                var ps = activator.Client.PS;
                for (int i = 0; i < ps.Powerups.Length; i++)
                    ps.Powerups[i] = 0;
            };
        }

        // target_location ----------------------------------------------------
        public static void SP_target_location(GEntity ent)
        {
            ServerGameLogic.G_SpawnString("message", "", out string locationName);
            ent.ClassName = "target_location";
            ent.Use = (self, other, activator) =>
            {
                OnLocationEntered?.Invoke(locationName);
            };
        }

        // target_give --------------------------------------------------------
        public static void SP_target_give(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                if (activator?.Client == null) return;
                // Teleport activator to the first entity matching self.TargetName
                // and pick up whatever is there — actual item pickup is handled
                // by the item entity's touch callback; we just fire its Use.
                var item = GameUtils.G_Find(null, "targetname", self.TargetName ?? "");
                item?.Use?.Invoke(item, self, activator);
            };
        }

        // target_delay -------------------------------------------------------
        public static void SP_target_delay(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("wait", "1", out float wait);
            ent.Use = (self, other, activator) =>
            {
                // Schedule a think that fires targets after the delay.
                var saved = activator;
                self.NextThink = ServerGameLogic.Level.Time + (int)(wait * 1000f);
                self.Think = (e) => ServerGameLogic.G_UseTargets(e, saved);
            };
        }

        // target_score -------------------------------------------------------
        public static void SP_target_score(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("count", "1", out int pts);
            if (pts == 0) pts = 1;
            int points = pts;

            ent.Use = (self, other, activator) =>
            {
                if (activator?.Client == null) return;
                activator.Client.Pers.Score += points;
                if (activator.Client.Sess.SessionTeam == (int)ClientSessionTeam.Red)
                    ServerGameLogic.Level.TeamScores0 += points;
                else if (activator.Client.Sess.SessionTeam == (int)ClientSessionTeam.Blue)
                    ServerGameLogic.Level.TeamScores1 += points;
            };
        }

        // target_print -------------------------------------------------------
        // SpawnFlags: 1=axis only, 2=allies only, 4=noisy (world sound), 8=no popup
        public static void SP_target_print(GEntity ent)
        {
            ServerGameLogic.G_SpawnString("message", "", out string msg);
            int flags = ent.SpawnFlags;
            string message = msg;

            ent.Use = (self, other, activator) =>
            {
                for (int i = 0; i < ServerGameLogic.Level.MaxClients; i++)
                {
                    var cl = ServerGameLogic.Entities[i];
                    if (cl == null || !cl.InUse || cl.Client == null) continue;

                    int team = cl.Client.Sess.SessionTeam;
                    if ((flags & 1) != 0 && team != (int)ClientSessionTeam.Red)   continue;
                    if ((flags & 2) != 0 && team != (int)ClientSessionTeam.Blue)  continue;

                    // Emit a centerprint / HUD message event to the client.
                    GameUtils.G_AddEvent(cl, (flags & 8) != 0 ? 0 : 1, i);
                }

                if ((flags & 4) != 0)
                    GameUtils.G_BroadcastSound(0, self.Origin);

                Debug.Log($"[target_print] {message}");
            };
        }

        // target_relay -------------------------------------------------------
        // SpawnFlags: 1=axis only, 2=allies only, 4=random target
        public static void SP_target_relay(GEntity ent)
        {
            int flags = ent.SpawnFlags;
            ent.Use = (self, other, activator) =>
            {
                if (activator?.Client != null)
                {
                    int team = activator.Client.Sess.SessionTeam;
                    if ((flags & 1) != 0 && team != (int)ClientSessionTeam.Red)   return;
                    if ((flags & 2) != 0 && team != (int)ClientSessionTeam.Blue)  return;
                }

                if ((flags & 4) != 0)
                {
                    var pick = GameUtils.G_PickTarget(self.TargetName);
                    pick?.Use?.Invoke(pick, self, activator);
                    return;
                }

                ServerGameLogic.G_UseTargets(self, activator);
            };
        }

        // target_counter -----------------------------------------------------
        public static void SP_target_counter(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("count", "0", out int count);
            ent.Count = count;
            ent.Use = (self, other, activator) =>
            {
                self.Health++;
                if (self.Health < self.Count) return;
                ServerGameLogic.G_UseTargets(self, activator);
                self.Health = 0;
            };
        }

        // target_activate ----------------------------------------------------
        public static void SP_target_activate(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                var level = ServerGameLogic.Level;
                for (int i = 0; i < level.NumEntities; i++)
                {
                    var e = ServerGameLogic.Entities[i];
                    if (e == null || !e.InUse) continue;
                    if (e.TargetName == self.TargetName) e.Flags &= ~GameConst.EF_NODRAW;
                }
            };
        }

        // target_deactivate --------------------------------------------------
        public static void SP_target_deactivate(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                var level = ServerGameLogic.Level;
                for (int i = 0; i < level.NumEntities; i++)
                {
                    var e = ServerGameLogic.Entities[i];
                    if (e == null || !e.InUse) continue;
                    if (e.TargetName == self.TargetName) e.Flags |= GameConst.EF_NODRAW;
                }
            };
        }

        // target_disable — disable/enable all entities sharing a team value --
        public static void SP_target_disable(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                var level = ServerGameLogic.Level;
                for (int i = 0; i < level.NumEntities; i++)
                {
                    var e = ServerGameLogic.Entities[i];
                    if (e == null || !e.InUse || e.Team != self.Team) continue;
                    // Toggle nodraw flag as a simple disabled marker.
                    if ((e.Flags & GameConst.EF_NODRAW) != 0)
                        e.Flags &= ~GameConst.EF_NODRAW;
                    else
                        e.Flags |= GameConst.EF_NODRAW;
                }
            };
        }

        // target_fog ---------------------------------------------------------
        public static void SP_target_fog(GEntity ent)
        {
            // Fog transitions are handled client-side via configstring updates.
            // Server only records the parameters and fires them on use.
            ServerGameLogic.G_SpawnFloat("wait", "0", out float fadetime);
            float fade = fadetime;

            ent.Use = (self, other, activator) =>
            {
                // Configstring slot CS_FOGVARS carries "r g b depthForOpaque".
                // Actual client rendering reacts to the CS change automatically.
                Debug.Log($"[target_fog] fade={fade}s origin={self.Origin}");
            };
        }

        // target_smoke -------------------------------------------------------
        public static void SP_target_smoke(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                GameUtils.G_TempEntity(self.Origin, (int)EntityType.Smoker);
            };
        }

        // target_script_trigger ----------------------------------------------
        public static void SP_target_script_trigger(GEntity ent)
        {
            ServerGameLogic.G_SpawnString("scriptName", "", out string scriptName);
            ServerGameLogic.G_SpawnString("action",     "", out string action);
            string sn = scriptName, act = action;

            ent.Use = (self, other, activator) =>
            {
                GameScript.G_ScriptAction(sn, act, activator);
            };
        }

        // target_rumble ------------------------------------------------------
        public static void SP_target_rumble(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("count", "10", out float intensity);
            float magnitude = intensity;

            ent.Use = (self, other, activator) =>
            {
                OnRumble?.Invoke(self.Origin, magnitude);
            };
        }

        // target_alarm -------------------------------------------------------
        public static void SP_target_alarm(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("count", "0", out int alarmNum);
            int num = alarmNum;

            ent.Use = (self, other, activator) =>
            {
                int clientNum = activator?.EntityNum ?? 0;
                AlarmSystem.G_TriggerAlarm(num, clientNum);
            };
        }
    }

    // =========================================================================
    // GameTriggers — g_trigger.c
    // =========================================================================
    public static class GameTriggers
    {
        private static LevelLocals Level => ServerGameLogic.Level;

        // --------------------------------------------------------------------
        // Shared helpers
        // --------------------------------------------------------------------

        // Returns true when other is allowed to activate the trigger.
        private static bool TriggerTeamCheck(GEntity trigger, GEntity other)
        {
            if (trigger.Team == 0) return true;
            if (other?.Client == null) return false;
            return other.Client.Sess.SessionTeam == trigger.Team;
        }

        // Think used to re-enable a multi trigger after its wait expires.
        private static void TriggerThink_Wait(GEntity ent)
        {
            ent.Timestamp = 0;
            ent.NextThink = 0f;
            ent.Think     = null;
        }

        // Core touch callback shared by trigger_multiple / trigger_once.
        private static void Touch_Multi(GEntity self, GEntity other, TraceResult trace)
        {
            if (other == null || other.Client == null) return;
            if (!TriggerTeamCheck(self, other)) return;
            if (self.Timestamp > Level.Time) return;

            ServerGameLogic.G_UseTargets(self, other);

            float waitSec = self.Angle > 0f ? self.Angle : 0.5f;
            self.Timestamp = Level.Time + (int)(waitSec * 1000f);
        }

        // --------------------------------------------------------------------
        // trigger_multiple
        // --------------------------------------------------------------------
        public static void SP_trigger_multiple(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("wait", "0.5", out float wait);
            ent.Angle = wait;
            ent.Touch = Touch_Multi;
        }

        // --------------------------------------------------------------------
        // trigger_once — fires once then frees itself
        // --------------------------------------------------------------------
        public static void SP_trigger_once(GEntity ent)
        {
            ent.Touch = (self, other, trace) =>
            {
                if (other == null || other.Client == null) return;
                if (!TriggerTeamCheck(self, other)) return;

                ServerGameLogic.G_UseTargets(self, other);
                ServerGameLogic.G_FreeEntity(self);
            };
        }

        // --------------------------------------------------------------------
        // trigger_relay — fired via Use rather than touch
        // --------------------------------------------------------------------
        public static void SP_trigger_relay(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                ServerGameLogic.G_UseTargets(self, activator);
            };
        }

        // --------------------------------------------------------------------
        // trigger_always — fires every frame
        // --------------------------------------------------------------------
        public static void SP_trigger_always(GEntity ent)
        {
            ent.NextThink = Level.Time + 1;
            ent.Think = (e) =>
            {
                ServerGameLogic.G_UseTargets(e, e);
                e.NextThink = Level.Time + 1;
            };
        }

        // --------------------------------------------------------------------
        // trigger_push — launch pad
        // --------------------------------------------------------------------
        public static void SP_trigger_push(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("speed", "1000", out float speed);
            float launchSpeed = speed;

            Vector3 movedir = Vector3.zero;
            GameUtils.G_SetMovedir(ref ent.Angles, ref movedir);
            Vector3 dir = movedir;

            ent.Touch = (self, other, trace) =>
            {
                if (other?.Client == null) return;
                var ps = other.Client.PS;
                Vector3 vel = dir * launchSpeed;
                ps.Velocity0 = vel.x;
                ps.Velocity1 = vel.y;
                ps.Velocity2 = vel.z;
            };
        }

        // --------------------------------------------------------------------
        // trigger_teleport
        // --------------------------------------------------------------------
        public static void SP_trigger_teleport(GEntity ent)
        {
            ent.Touch = (self, other, trace) =>
            {
                if (other?.Client == null) return;
                if (!TriggerTeamCheck(self, other)) return;

                var dest = GameUtils.G_PickTarget(self.TargetName);
                if (dest == null) return;

                var ps = other.Client.PS;
                ps.Origin0       = dest.Origin.x;
                ps.Origin1       = dest.Origin.y;
                ps.Origin2       = dest.Origin.z;
                ps.ViewAngles1   = dest.Angles.y;
                ps.PmFlags     |= GameConst.PMF_TIME_LAND;

                other.Client.Sess.TelePorted++;
                GameUtils.G_AddEvent(other, (int)EntityEvent.Teleport, 0);
            };
        }

        // --------------------------------------------------------------------
        // trigger_hurt — periodic damage to entities inside
        // --------------------------------------------------------------------
        public static void SP_trigger_hurt(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("dmg",  "5",   out int dmg);
            ServerGameLogic.G_SpawnFloat("wait", "1", out float wait);

            if (dmg  == 0) dmg  = 5;
            if (wait == 0) wait = 1f;

            ent.Damage = dmg;
            ent.Angle  = wait;

            ent.Touch = (self, other, trace) =>
            {
                if (other == null || !other.InUse) return;
                if (Level.Time < self.Timestamp) return;

                GameUtils.G_Damage(other, self, self, Vector3.zero, other.Origin,
                    self.Damage, 0, WeaponSystem.MOD_TRIGGER_HURT);
                self.Timestamp = Level.Time + (int)(self.Angle * 1000f);
            };
        }

        // --------------------------------------------------------------------
        // trigger_timer — fires targets at a regular interval
        // --------------------------------------------------------------------
        public static void SP_trigger_timer(GEntity ent)
        {
            ServerGameLogic.G_SpawnFloat("wait",   "1", out float wait);
            ServerGameLogic.G_SpawnFloat("random", "0", out float random);

            if (wait == 0) wait = 1f;

            float w = wait, r = random;

            ent.NextThink = Level.Time + (int)(wait * 1000f);
            ent.Think = (e) =>
            {
                ServerGameLogic.G_UseTargets(e, e);
                float jitter = r > 0f ? (float)(_rng.NextDouble() * 2.0 - 1.0) * r : 0f;
                e.NextThink = Level.Time + (int)((w + jitter) * 1000f);
            };
        }

        // --------------------------------------------------------------------
        // trigger_objective_info
        // --------------------------------------------------------------------
        public static void SP_trigger_objective_info(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("spawnflags", "0", out int sf);
            ent.SpawnFlags = sf;
            ent.Use = (self, other, activator) =>
            {
                ServerGameLogic.G_UseTargets(self, activator);
                GameScript.G_ScriptAction("objective_info", "trigger", activator);
            };
            ent.Touch = (self, other, trace) =>
            {
                if (other?.Client == null) return;
                if (!TriggerTeamCheck(self, other)) return;
                self.Use?.Invoke(self, other, other);
            };
        }

        // --------------------------------------------------------------------
        // trigger_flagonly — activated only by flag carriers
        // --------------------------------------------------------------------
        public static void SP_trigger_flagonly(GEntity ent)
        {
            ent.Touch = (self, other, trace) =>
            {
                if (other?.Client == null) return;
                var ps = other.Client.PS;
                // Flag is carried when STAT_CAPTUREDFLAG is non-zero.
                if (ps.Stats[GameConst.STAT_CAPTUREDFLAG] == (short)0) return;
                if (!TriggerTeamCheck(self, other)) return;

                ServerGameLogic.G_UseTargets(self, other);
            };
        }

        // --------------------------------------------------------------------
        // trigger_startmatch — starts the match when all players enter
        // --------------------------------------------------------------------
        public static void SP_trigger_startmatch(GEntity ent)
        {
            ent.Touch = (self, other, trace) =>
            {
                if (other?.Client == null) return;

                // Count players inside vs. total connected.
                int inside  = 0;
                int total   = 0;
                var level   = ServerGameLogic.Level;
                for (int i = 0; i < level.MaxClients; i++)
                {
                    var cl = ServerGameLogic.Entities[i];
                    if (cl == null || !cl.InUse || cl.Client == null) continue;
                    if (cl.Client.Sess.SessionTeam == (int)ClientSessionTeam.Spectator) continue;
                    total++;
                    float distSq = (cl.Origin - self.Origin).sqrMagnitude;
                    float radius = Mathf.Max(
                        Mathf.Abs(self.Maxs.x - self.Mins.x),
                        Mathf.Abs(self.Maxs.z - self.Mins.z)) * 0.5f;
                    if (distSq <= radius * radius) inside++;
                }

                if (total > 0 && inside >= total)
                    ServerGameLogic.G_UseTargets(self, other);
            };
        }

        private static readonly System.Random _rng = new System.Random();
    }

    // =========================================================================
    // AlarmSystem — g_alarm.c
    // =========================================================================
    public static class AlarmSystem
    {
        public const int MAX_ALARMS = 8;

        public class AlarmZone
        {
            public int     AlarmNum;
            public Vector3 Origin;
            public float   Radius;
            public int     Team;
            public int     SoundIndex;
            public bool    Active;
            public int     LastTriggerTime;
            public int     CooldownMs = 5000;
        }

        private static AlarmZone[] _alarms = new AlarmZone[MAX_ALARMS];

        public static event Action<int, int> OnAlarmTriggered;

        public static void G_AlarmInit()
        {
            for (int i = 0; i < MAX_ALARMS; i++)
                _alarms[i] = null;
        }

        public static int G_AlarmRegister(Vector3 origin, float radius, int team, int soundIndex)
        {
            for (int i = 0; i < MAX_ALARMS; i++)
            {
                if (_alarms[i] != null) continue;

                _alarms[i] = new AlarmZone
                {
                    AlarmNum        = i,
                    Origin          = origin,
                    Radius          = radius,
                    Team            = team,
                    SoundIndex      = soundIndex,
                    Active          = true,
                    LastTriggerTime = 0,
                    CooldownMs      = 5000,
                };
                return i;
            }
            Debug.LogWarning("[AlarmSystem] G_AlarmRegister: no free alarm slots");
            return -1;
        }

        public static void G_AlarmCheck(int levelTime)
        {
            var level = ServerGameLogic.Level;

            for (int ai = 0; ai < MAX_ALARMS; ai++)
            {
                var alarm = _alarms[ai];
                if (alarm == null || !alarm.Active) continue;
                if (levelTime - alarm.LastTriggerTime < alarm.CooldownMs) continue;

                float radius2 = alarm.Radius * alarm.Radius;
                for (int ci = 0; ci < level.MaxClients; ci++)
                {
                    var ent = ServerGameLogic.Entities[ci];
                    if (ent == null || !ent.InUse || ent.Client == null) continue;

                    // Only detect opposing team entering the zone.
                    int playerTeam = ent.Client.Sess.SessionTeam;
                    if (playerTeam == alarm.Team || playerTeam == (int)ClientSessionTeam.Spectator)
                        continue;

                    if ((ent.Origin - alarm.Origin).sqrMagnitude <= radius2)
                    {
                        G_TriggerAlarm(ai, ci);
                        break;
                    }
                }
            }
        }

        public static void G_TriggerAlarm(int alarmNum, int triggeringClient)
        {
            if (alarmNum < 0 || alarmNum >= MAX_ALARMS) return;
            var alarm = _alarms[alarmNum];
            if (alarm == null) return;

            alarm.LastTriggerTime = ServerGameLogic.Level.Time;

            GameUtils.G_BroadcastSound(alarm.SoundIndex, alarm.Origin);

            OnAlarmTriggered?.Invoke(alarmNum, triggeringClient);

            Debug.Log($"[AlarmSystem] alarm {alarmNum} triggered by client {triggeringClient}");
        }
    }

    // =========================================================================
    // EntityEvent / EntityType stubs referenced above
    // (mirror the values used across the codebase)
    // =========================================================================
    internal enum EntityEvent
    {
        Teleport  = 52,
    }

    internal enum EntityType
    {
        General       = 0,
        Player        = 1,
        Item          = 2,
        Missile       = 3,
        Mover         = 4,
        Speaker       = 5,
        PortalSurface = 6,
        PortalCamera  = 7,
        MG42Mount     = 8,
        AAGun         = 9,
        Flak          = 10,
        Smoker        = 20,
    }
}
