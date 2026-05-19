// Ported from: src/game/g_cmds.c, src/game/g_misc.c
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
    public enum ChatType { All = 0, Team = 1, Fireteam = 2, Private = 3 }

    // =========================================================================
    // GameCommands — g_cmds.c
    // =========================================================================
    public static class GameCommands
    {
        public static event Action<int, ChatType, string> OnChatMessage;
        public static event Action<string, int, int>      OnVoteResult;
        public static event Action<int, int>              OnTeamChange;
        public static event Action<int, int>              OnClassChange;

        private static readonly Dictionary<string, Action<GEntity>> _cmds =
            new Dictionary<string, Action<GEntity>>(StringComparer.OrdinalIgnoreCase);

        public static void G_InitGameCommands()
        {
            _cmds["team"]       = Cmd_Team_f;
            _cmds["class"]      = Cmd_Class_f;
            _cmds["weapon"]     = Cmd_Weapon_f;
            _cmds["vote"]       = Cmd_Vote_f;
            _cmds["callvote"]   = Cmd_CallVote_f;
            _cmds["say"]        = e => Cmd_Say_f(e, ChatType.All);
            _cmds["say_team"]   = Cmd_SayTeam_f;
            _cmds["kill"]       = Cmd_Kill_f;
            _cmds["revive"]     = Cmd_Revive_f;
            _cmds["dropweapon"] = Cmd_DropWeapon_f;
            _cmds["give"]       = Cmd_Give_f;
            _cmds["god"]        = Cmd_God_f;
            _cmds["noclip"]     = Cmd_Noclip_f;
            _cmds["score"]      = Cmd_Score_f;
            _cmds["where"]      = Cmd_Where_f;
            _cmds["stats"]      = Cmd_Stats_f;
        }

        public static bool G_ProcessClientCommand(GEntity ent, string cmd)
        {
            if (ent == null || string.IsNullOrEmpty(cmd)) return false;

            if (!_cmds.TryGetValue(cmd, out var handler)) return false;

            // Cheat-gated commands
            if ((cmd == "give" || cmd == "god" || cmd == "noclip") &&
                CvarSystem.GetFloat("g_cheats") == 0)
            {
                return false;
            }

            handler(ent);
            return true;
        }

        // -----------------------------------------------------------------------
        // Client commands
        // -----------------------------------------------------------------------

        private static void Cmd_Team_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            string arg = CmdSystem.Cmd_Argv(1).ToLowerInvariant();
            int newTeam;
            switch (arg)
            {
                case "axis":               newTeam = (int)ClientSessionTeam.Red;       break;
                case "allies":             newTeam = (int)ClientSessionTeam.Blue;      break;
                case "s":
                case "spectator":          newTeam = (int)ClientSessionTeam.Spectator; break;
                default:
                    return;
            }

            // Block mid-round switches when the cvar says so
            if (CvarSystem.GetInt("g_noTeamSwitching") != 0 &&
                ServerGameLogic.Level.GameState == ServerGameLogic.GS_PLAYING &&
                ent.Client.Sess.SessionTeam != (int)ClientSessionTeam.Spectator)
            {
                return;
            }

            int clientNum = ent.EntityNum;
            ent.Client.Sess.SessionTeam = newTeam;
            ent.Client.Pers.Team        = newTeam;

            OnTeamChange?.Invoke(clientNum, newTeam);
            ServerGameLogic.ClientSpawn(ent);
        }

        private static void Cmd_Class_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            string arg = CmdSystem.Cmd_Argv(1).ToLowerInvariant();
            int newClass;
            switch (arg)
            {
                case "soldier":    newClass = GameConst.PC_SOLDIER;    break;
                case "medic":      newClass = GameConst.PC_MEDIC;      break;
                case "engineer":   newClass = GameConst.PC_ENGINEER;   break;
                case "fieldops":
                case "fieldop":    newClass = GameConst.PC_FIELDOPS;   break;
                case "covertops":
                case "covertop":   newClass = GameConst.PC_COVERTOPS;  break;
                default:
                    if (!int.TryParse(arg, out newClass) ||
                        newClass < GameConst.PC_SOLDIER || newClass > GameConst.PC_COVERTOPS)
                        return;
                    break;
            }

            int clientNum = ent.EntityNum;
            ent.Client.Pers.ClassType = newClass;

            OnClassChange?.Invoke(clientNum, newClass);
            ServerGameLogic.ClientSpawn(ent);
        }

        private static void Cmd_Weapon_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            string arg = CmdSystem.Cmd_Argv(1);
            if (!int.TryParse(arg, out int weapon)) return;
            if (weapon < 0 || weapon >= GameConst.WP_NUM_WEAPONS) return;

            ent.Client.PS.NextWeapon = weapon;
        }

        private static void Cmd_Vote_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            if (!ServerGameLogic.Level.VoteInProgress) return;

            // Each client may only vote once
            if (ent.Client.PS.EFlags != 0 &&
                (ent.Client.PS.EFlags & GameConst.EF_VOTED) != 0)
                return;

            string arg = CmdSystem.Cmd_Argv(1).ToLowerInvariant();
            if (arg == "yes" || arg == "1")
                ServerGameLogic.Level.VoteYes++;
            else if (arg == "no" || arg == "0")
                ServerGameLogic.Level.VoteNo++;
            else
                return;

            ent.Client.PS.EFlags |= GameConst.EF_VOTED;
        }

        private static void Cmd_CallVote_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            if (ServerGameLogic.Level.VoteInProgress) return;
            if (CvarSystem.GetInt("g_allowVote") == 0) return;

            string voteCmd  = CmdSystem.Cmd_Argv(1);
            string voteArg  = CmdSystem.Cmd_Argv(2);
            if (string.IsNullOrEmpty(voteCmd)) return;

            // Only a small whitelist of callvote commands is permitted
            switch (voteCmd.ToLowerInvariant())
            {
                case "map":
                case "kick":
                case "timelimit":
                case "nextmap":
                case "restart":
                    break;
                default:
                    return;
            }

            ServerGameLogic.Level.VoteString    = string.IsNullOrEmpty(voteArg)
                                                    ? voteCmd
                                                    : $"{voteCmd} {voteArg}";
            ServerGameLogic.Level.VoteTime      = ServerGameLogic.Level.Time + 30000;
            ServerGameLogic.Level.VoteYes       = 0;
            ServerGameLogic.Level.VoteNo        = 0;
            ServerGameLogic.Level.VoteInProgress = true;

            // Tally how many clients can vote
            int voting = 0;
            for (int i = 0; i < ServerGameLogic.Level.MaxClients; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e != null && e.InUse && e.Client != null &&
                    e.Client.Sess.SessionTeam != (int)ClientSessionTeam.Spectator)
                    voting++;
            }
            ServerGameLogic.Level.NumVotingClients = voting;
        }

        private static void Cmd_Give_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            string item = CmdSystem.Cmd_Argv(1).ToLowerInvariant();
            switch (item)
            {
                case "health":
                    ent.Health = ent.MaxHealth;
                    ent.Client.PS.Stats[GameConst.STAT_HEALTH] = ent.MaxHealth;
                    break;
                case "ammo":
                    WeaponSystem.GiveAmmo(ent.Client.PS, -1, true);
                    break;
                case "all":
                    ent.Health = ent.MaxHealth;
                    ent.Client.PS.Stats[GameConst.STAT_HEALTH] = ent.MaxHealth;
                    WeaponSystem.GiveAmmo(ent.Client.PS, -1, true);
                    break;
                default:
                    if (int.TryParse(item, out int wpNum) &&
                        wpNum >= 0 && wpNum < GameConst.WP_NUM_WEAPONS)
                    {
                        WeaponSystem.AddWeapon(ent.Client.PS, wpNum);
                        WeaponSystem.GiveAmmo(ent.Client.PS, wpNum, true);
                    }
                    break;
            }
        }

        private static void Cmd_God_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            bool godOn = (ent.Flags & GameConst.FL_GODMODE) == 0;
            if (godOn) ent.Flags |=  GameConst.FL_GODMODE;
            else       ent.Flags &= ~GameConst.FL_GODMODE;
        }

        private static void Cmd_Noclip_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            ent.Client.Noclip = !ent.Client.Noclip;
            ent.Client.PS.PmType = ent.Client.Noclip
                ? GameConst.PM_NOCLIP
                : GameConst.PM_NORMAL;
        }

        private static void Cmd_Kill_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            if (ent.Health <= 0) return;
            // Self-inflicted death via damage system; MOD_SUICIDE = 33 in ET
            WeaponSystem.G_Damage(ent.EntityNum, GameConst.ENTITYNUM_WORLD,
                ent.Health + 999, 0, 33);
        }

        private static void Cmd_Say_f(GEntity ent, ChatType chatType)
        {
            if (ent?.Client == null) return;

            string text = CmdSystem.Cmd_Args();
            if (string.IsNullOrEmpty(text)) return;

            G_Say(ent, null, chatType, text);
        }

        private static void Cmd_SayTeam_f(GEntity ent)
        {
            Cmd_Say_f(ent, ChatType.Team);
        }

        private static void Cmd_Score_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            var sb = new System.Text.StringBuilder();
            sb.Append("scores");
            for (int i = 0; i < ServerGameLogic.Level.MaxClients; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e == null || !e.InUse || e.Client == null) continue;
                sb.Append($" {i} {e.Client.Pers.Score} {e.Client.Pers.Kills} {e.Client.Pers.Deaths}");
            }
            // Relay to network layer; stub call — real implementation routes through
            // ServerMain.SV_SendServerCommand
            NetworkManager.SendReliableCommand(ent.EntityNum, sb.ToString());
        }

        private static void Cmd_Where_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            Vector3 o = ent.Origin;
            NetworkManager.SendReliableCommand(ent.EntityNum,
                $"print \"origin {o.x:F1} {o.y:F1} {o.z:F1}\n\"");
        }

        private static void Cmd_Stats_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            var c = ent.Client;
            NetworkManager.SendReliableCommand(ent.EntityNum,
                $"print \"XP: {c.XP}  Kills: {c.Pers.Kills}  Deaths: {c.Pers.Deaths}\n\"");
        }

        private static void Cmd_Revive_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            if (ent.Client.Pers.ClassType != GameConst.PC_MEDIC) return;

            string arg = CmdSystem.Cmd_Argv(1);
            if (!int.TryParse(arg, out int targetNum)) return;
            if (targetNum < 0 || targetNum >= ServerGameLogic.Level.MaxClients) return;

            var target = ServerGameLogic.Entities[targetNum];
            if (target == null || !target.InUse || target.Client == null) return;
            if (target.Health > 0) return;

            // Distance check — medic must be within 96 units
            if ((target.Origin - ent.Origin).sqrMagnitude > 96f * 96f) return;

            target.Health = 1;
            target.Client.PS.Stats[GameConst.STAT_HEALTH] = 1;
            ServerGameLogic.ClientSpawn(target);
        }

        private static void Cmd_DropWeapon_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            int weapon = ent.Client.PS.Weapon;
            if (weapon == GameConst.WP_NONE) return;

            WeaponSystem.DropWeapon(ent, weapon);
        }

        // -----------------------------------------------------------------------
        // Vote processing
        // -----------------------------------------------------------------------

        public static void G_ProcessVote()
        {
            var level = ServerGameLogic.Level;
            if (!level.VoteInProgress) return;

            bool expired  = level.Time >= level.VoteTime;
            int  majority = level.NumVotingClients / 2 + 1;
            bool passed   = level.VoteYes >= majority;
            bool failed   = level.VoteNo  >= majority || (expired && !passed);

            if (!passed && !failed) return;

            OnVoteResult?.Invoke(level.VoteString, level.VoteYes, level.VoteNo);

            if (passed)
                ExecuteVote(level.VoteString);

            level.VoteInProgress = false;
            level.VoteString     = string.Empty;
        }

        private static void ExecuteVote(string voteString)
        {
            if (string.IsNullOrEmpty(voteString)) return;

            string[] parts = voteString.Split(' ');
            string   verb  = parts[0].ToLowerInvariant();
            string   arg   = parts.Length > 1 ? parts[1] : string.Empty;

            switch (verb)
            {
                case "map":
                    if (!string.IsNullOrEmpty(arg))
                        CmdSystem.Cmd_ExecuteString($"map {arg}");
                    break;
                case "kick":
                    if (int.TryParse(arg, out int kickNum))
                        ServerGameLogic.ClientDisconnect(kickNum);
                    break;
                case "timelimit":
                    if (float.TryParse(arg, out float tl))
                        CvarSystem.Set("g_timelimit", tl.ToString());
                    break;
                case "nextmap":
                    CmdSystem.Cmd_ExecuteString("vstr nextmap");
                    break;
                case "restart":
                    CmdSystem.Cmd_ExecuteString("map_restart");
                    break;
            }
        }

        // -----------------------------------------------------------------------
        // Chat
        // -----------------------------------------------------------------------

        public static void G_Say(GEntity ent, GEntity target, ChatType type, string text)
        {
            if (ent == null || string.IsNullOrEmpty(text)) return;

            int   senderNum  = ent.EntityNum;
            int   senderTeam = ent.Client?.Sess.SessionTeam ?? 0;
            string name      = ent.Client?.Pers.Name ?? "unknown";
            string msg       = $"chat \"{name}: {text}\"";

            for (int i = 0; i < ServerGameLogic.Level.MaxClients; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e == null || !e.InUse || e.Client == null) continue;

                bool send = type switch
                {
                    ChatType.All      => true,
                    ChatType.Team     => e.Client.Sess.SessionTeam == senderTeam,
                    ChatType.Fireteam => e.Client.Sess.SessionTeam == senderTeam,
                    ChatType.Private  => e == target || e == ent,
                    _                 => false,
                };

                if (send)
                    NetworkManager.SendReliableCommand(i, msg);
            }

            OnChatMessage?.Invoke(senderNum, type, text);
        }
    }

    // =========================================================================
    // GameMiscEntities — g_misc.c
    // =========================================================================
    public static class GameMiscEntities
    {
        // -----------------------------------------------------------------------
        // Marker / null entities
        // -----------------------------------------------------------------------

        public static void SP_info_null(GEntity ent)
        {
            ServerGameLogic.G_FreeEntity(ent);
        }

        public static void SP_info_notnull(GEntity ent)
        {
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        public static void SP_light(GEntity ent)
        {
            // Server-side nothing to do; renderer consumes keyvalues directly
            ServerGameLogic.G_FreeEntity(ent);
        }

        // -----------------------------------------------------------------------
        // Path / movement
        // -----------------------------------------------------------------------

        public static void SP_path_corner(GEntity ent)
        {
            if (string.IsNullOrEmpty(ent.TargetName))
                Debug.LogWarning($"[GameMiscEntities] path_corner at {ent.Origin} missing targetname");
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        // -----------------------------------------------------------------------
        // Sounds
        // -----------------------------------------------------------------------

        public static void SP_target_speaker(GEntity ent)
        {
            G_SpawnString(ent, "noise",  "",  out string soundPath);
            G_SpawnFloat (ent, "wait",   "0", out float  wait);
            G_SpawnFloat (ent, "random", "0", out float  random);

            ent.ClassName = "target_speaker";

            if (string.IsNullOrEmpty(soundPath)) return;

            // Looping ambient — install a Think that fires the sound periodically
            if (wait > 0f)
            {
                float interval = wait;
                float rnd      = random;
                var   origin   = ent.Origin;

                ent.Think = self =>
                {
                    AudioSystem.S_StartSoundAtOrigin(origin, soundPath);
                    self.NextThink = ServerGameLogic.Level.Time +
                                     (int)((interval + UnityEngine.Random.Range(-rnd, rnd)) * 1000f);
                };
                ent.NextThink = ServerGameLogic.Level.Time + (int)(interval * 1000f);
            }
            else
            {
                // One-shot: trigger via Use
                ent.Use = (self, other, activator) =>
                    AudioSystem.S_StartSoundAtOrigin(self.Origin, soundPath);
            }
        }

        // -----------------------------------------------------------------------
        // Targets
        // -----------------------------------------------------------------------

        public static void SP_target_print(GEntity ent)
        {
            G_SpawnString(ent, "message", "", out string msg);
            ent.ClassName = "target_print";

            ent.Use = (self, other, activator) =>
            {
                for (int i = 0; i < ServerGameLogic.Level.MaxClients; i++)
                {
                    var e = ServerGameLogic.Entities[i];
                    if (e != null && e.InUse && e.Client != null)
                        NetworkManager.SendReliableCommand(i, $"cp \"{msg}\"");
                }
            };
        }

        public static void SP_target_delay(GEntity ent)
        {
            G_SpawnFloat(ent, "wait",   "1", out float wait);
            G_SpawnFloat(ent, "random", "0", out float random);

            ent.ClassName = "target_delay";

            ent.Use = (self, other, activator) =>
            {
                // Schedule deferred firing
                float delay = wait + UnityEngine.Random.Range(-random, random);
                int   fireAt = ServerGameLogic.Level.Time + (int)(delay * 1000f);

                GEntity saved   = other;
                GEntity savedAc = activator;
                self.Think = s =>
                {
                    ServerGameLogic.G_UseTargets(s, savedAc);
                    s.Think     = null;
                    s.NextThink = 0;
                };
                self.NextThink = fireAt;
            };
        }

        public static void SP_target_score(GEntity ent)
        {
            ent.ClassName = "target_score";
            ent.Use = (self, other, activator) =>
            {
                if (activator?.Client == null) return;
                activator.Client.Pers.Score += self.Count != 0 ? self.Count : 1;
            };
        }

        public static void SP_target_teleporter(GEntity ent)
        {
            ent.ClassName = "target_teleporter";
            ent.Use = (self, other, activator) =>
            {
                if (activator?.Client == null) return;

                GEntity dest = ServerGameLogic.G_PickTarget(self.TargetName);
                if (dest == null) return;

                ServerGameLogic.G_SetOrigin(activator, dest.Origin);
                activator.Client.PS.Velocity = Vector3.zero;
                activator.Client.Sess.TelePorted++;
            };
        }

        public static void SP_target_relay(GEntity ent)
        {
            // spawnflags: 1 = axis only, 2 = allies only
            ent.ClassName = "target_relay";

            int flags = ent.SpawnFlags;
            ent.Use = (self, other, activator) =>
            {
                if (flags != 0 && activator?.Client != null)
                {
                    int team = activator.Client.Sess.SessionTeam;
                    if ((flags & 1) != 0 && team != (int)ClientSessionTeam.Red)   return;
                    if ((flags & 2) != 0 && team != (int)ClientSessionTeam.Blue)  return;
                }
                ServerGameLogic.G_UseTargets(self, activator);
            };
        }

        public static void SP_target_counter(GEntity ent)
        {
            ent.ClassName = "target_counter";
            // Count field holds the target activation threshold; reuse Timestamp as hit counter
            ent.Timestamp = 0;

            ent.Use = (self, other, activator) =>
            {
                self.Timestamp++;
                if (self.Timestamp < self.Count) return;
                self.Timestamp = 0;
                ServerGameLogic.G_UseTargets(self, activator);
            };
        }

        // -----------------------------------------------------------------------
        // Models / visual entities
        // -----------------------------------------------------------------------

        public static void SP_misc_model(GEntity ent)
        {
            G_SpawnString(ent, "model", "", out string model);
            ent.ClassName = "misc_model";
            ent.S.ModelIndex = NetworkManager.RegisterModel(model);
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        public static void SP_misc_gamemodel(GEntity ent)
        {
            G_SpawnString(ent, "model", "", out string model);
            ent.ClassName = "misc_gamemodel";
            ent.S.ModelIndex = NetworkManager.RegisterModel(model);
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);

            GameScript.G_Script_ScriptEvent(ent, ScriptEventType.Spawn, string.Empty);
        }

        public static void SP_misc_portal_surface(GEntity ent)
        {
            ent.ClassName = "misc_portal_surface";
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
            ent.S.EType = (int)EntityType.PortalSurface;
        }

        public static void SP_misc_portal_camera(GEntity ent)
        {
            ent.ClassName = "misc_portal_camera";
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
            ent.S.EType = (int)EntityType.PortalCamera;
        }

        public static void SP_env_humus(GEntity ent)
        {
            // Detail particle system — server just marks and links; client renders it
            ent.ClassName = "env_humus";
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        // -----------------------------------------------------------------------
        // Mounted weapons
        // -----------------------------------------------------------------------

        public static void SP_misc_mg42(GEntity ent)
        {
            G_SpawnFloat(ent, "speed", "50", out float speed);

            ent.ClassName  = "misc_mg42";
            ent.S.EType    = (int)EntityType.MG42Mount;
            ent.S.Weapon   = GameConst.WP_MOBILE_MG42;
            ent.Health     = 100;
            ent.MaxHealth  = 100;

            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        public static void SP_misc_aagun(GEntity ent)
        {
            ent.ClassName = "misc_aagun";
            ent.S.EType   = (int)EntityType.AAGun;
            ent.Health    = 100;
            ent.MaxHealth = 100;
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        public static void SP_misc_flak(GEntity ent)
        {
            ent.ClassName = "misc_flak";
            ent.S.EType   = (int)EntityType.Flak;
            ent.Health    = 100;
            ent.MaxHealth = 100;
            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        // -----------------------------------------------------------------------
        // Triggers
        // -----------------------------------------------------------------------

        public static void SP_trigger_objective_info(GEntity ent)
        {
            G_SpawnString(ent, "track", "", out string track);

            ent.ClassName  = "trigger_objective_info";
            ent.TargetName = track;

            ent.Touch = (self, other, _) =>
            {
                if (other?.Client == null) return;
                GameScript.G_Script_ScriptEvent(self, ScriptEventType.Trigger, string.Empty);
            };

            ServerGameLogic.G_SetOrigin(ent, ent.Origin);
        }

        public static void SP_trigger_multiple(GEntity ent)
        {
            G_SpawnFloat(ent, "wait", "0.5", out float wait);

            ent.ClassName = "trigger_multiple";

            ent.Touch = (self, other, _) =>
            {
                if (other?.Client == null) return;
                if (ServerGameLogic.Level.Time < self.Timestamp) return;

                self.Timestamp = ServerGameLogic.Level.Time + (int)(wait * 1000f);
                ServerGameLogic.G_UseTargets(self, other);
            };
        }

        public static void SP_trigger_once(GEntity ent)
        {
            ent.ClassName = "trigger_once";

            ent.Touch = (self, other, _) =>
            {
                if (other?.Client == null) return;
                ServerGameLogic.G_UseTargets(self, other);
                // Disable after first activation
                self.Touch = null;
            };
        }

        public static void SP_trigger_push(GEntity ent)
        {
            G_SpawnFloat(ent, "speed", "1000", out float speed);

            ent.ClassName = "trigger_push";

            ent.Touch = (self, other, _) =>
            {
                if (other?.Client == null) return;

                GEntity dest = ServerGameLogic.G_PickTarget(self.TargetName);
                if (dest == null) return;

                Vector3 dir = (dest.Origin - other.Origin).normalized;
                other.Client.PS.Velocity = dir * speed;
            };
        }

        // -----------------------------------------------------------------------
        // Spawn key helpers (mirrors G_SpawnString / G_SpawnFloat / G_SpawnInt)
        // -----------------------------------------------------------------------

        private static void G_SpawnString(GEntity ent, string key, string def, out string value)
        {
            value = SpawnVarGet(ent, key) ?? def;
        }

        private static void G_SpawnFloat(GEntity ent, string key, string def, out float value)
        {
            string raw = SpawnVarGet(ent, key) ?? def;
            if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                float.TryParse(def, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value);
            }
        }

        private static void G_SpawnInt(GEntity ent, string key, string def, out int value)
        {
            string raw = SpawnVarGet(ent, key) ?? def;
            if (!int.TryParse(raw, out value))
                int.TryParse(def, out value);
        }

        // Reads a spawn key from LevelLocals.SpawnVars by matching the entity
        // number as a proxy index — real impl would look up by entity's spawn row.
        private static string SpawnVarGet(GEntity ent, string key)
        {
            var vars = ServerGameLogic.Level.SpawnVars;
            int rows = vars.GetLength(0);
            for (int r = 0; r < rows; r++)
            {
                if (string.Equals(vars[r, 0], key, StringComparison.OrdinalIgnoreCase))
                    return vars[r, 1];
            }
            return null;
        }
    }
}
