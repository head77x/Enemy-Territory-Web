// Ported from: src/game/g_match.c, src/game/g_session.c, src/game/g_stats.c,
//              src/game/g_fireteams.c, src/game/g_antilag.c
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
    // GameMatch — g_match.c
    // =========================================================================
    public static class GameMatch
    {
        private static System.IO.StreamWriter _logFile;
        private static int[]   _weaponLog  = new int[256];
        private static int[]   _deathLog   = new int[256];
        private static Cvar    _cvLog;
        private static Cvar    _cvTimelimit;

        public static event Action<int, int> OnTeamScoreChanged;
        public static event Action           OnMatchOver;
        public static event Action           OnMatchInit;
        public static event Action<string>   OnBroadcast;

        public static void G_InitMatch()
        {
            _cvLog       = CvarSystem.Get("g_log",       "",   CvarFlags.Archive);
            _cvTimelimit = CvarSystem.Get("g_timelimit", "0",  CvarFlags.ServerInfo | CvarFlags.Archive);

            Level.TeamScores0 = 0;
            Level.TeamScores1 = 0;
            Level.GameState   = ServerGameLogic.GS_WARMUP;

            Array.Clear(_weaponLog, 0, _weaponLog.Length);
            Array.Clear(_deathLog,  0, _deathLog.Length);

            string logPath = _cvLog.StringValue;
            if (!string.IsNullOrEmpty(logPath))
            {
                try
                {
                    _logFile = new System.IO.StreamWriter(logPath, append: true);
                    _logFile.AutoFlush = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GameMatch] Cannot open log '{logPath}': {e.Message}");
                    _logFile = null;
                }
            }

            G_LogPrintf("------------------------------------------------------------");
            G_LogPrintf("InitGame: {0}", Level.MapName);

            OnMatchInit?.Invoke();
        }

        public static void G_MatchOver()
        {
            Level.GameState = ServerGameLogic.GS_INTERMISSION;
            ServerMain.SV_SetConfigstring(ServerMain.CS_INTERMISSION, "1");
            G_LogPrintf("ShutdownGame:");
            G_LogPrintf("------------------------------------------------------------");
            _logFile?.Close();
            _logFile = null;
            OnMatchOver?.Invoke();
        }

        public static void G_LogPrintf(string format, params object[] args)
        {
            string msg = args.Length > 0 ? string.Format(format, args) : format;
            string stamped = $"[{Level.Time / 1000:F1}] {msg}";
            _logFile?.WriteLine(stamped);
            Debug.Log($"[G_LOG] {stamped}");
        }

        public static void G_LogWeapon(int clientNum, int weaponNum)
        {
            if (weaponNum < 0 || weaponNum >= _weaponLog.Length) return;
            _weaponLog[weaponNum]++;
            G_LogPrintf("Weapon: {0} used {1}", clientNum, weaponNum);
        }

        public static void G_LogDeath(int victimNum, int killerNum, int meansOfDeath)
        {
            G_LogPrintf("Kill: {0} {1} {2}", killerNum, victimNum, meansOfDeath);
            if (meansOfDeath >= 0 && meansOfDeath < _deathLog.Length)
                _deathLog[meansOfDeath]++;
        }

        public static void G_CalculateRanks()
        {
            int maxClients = Level.MaxClients;
            var ranked = new List<(int score, int clientNum)>(maxClients);
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                ranked.Add((ent.Client.Pers.Score, i));
            }
            ranked.Sort((a, b) => b.score.CompareTo(a.score));

            for (int rank = 0; rank < ranked.Count; rank++)
            {
                int clientNum = ranked[rank].clientNum;
                ServerMain.SV_SetConfigstring(
                    ServerMain.CS_MODELS + clientNum,
                    $"rank\\{rank}\\score\\{ranked[rank].score}");
            }
        }

        public static void G_CheckForCvars()
        {
            // Timelimit expiry check — called once per frame from G_RunFrame.
            // Actual cvar-change polling is handled by CvarSystem; this only
            // enforces time-based match termination.
            if (Level.GameState != ServerGameLogic.GS_PLAYING) return;

            int limit = _cvTimelimit != null ? _cvTimelimit.IntValue * 60000 : 0;
            if (limit > 0 && Level.Time - Level.StartTime >= limit)
                G_MatchOver();
        }

        public static void G_Broadcast(string msg)
        {
            int maxClients = Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
                G_PrintMessage(i, msg);
            OnBroadcast?.Invoke(msg);
        }

        public static void G_PrintMessage(int clientNum, string msg)
        {
            if (clientNum < 0 || clientNum >= ServerMain.Svs.Clients.Length) return;
            ServerClient cl = ServerMain.Svs.Clients[clientNum];
            if (cl.State != ClientState.Active) return;
            ServerMain.SV_AddServerCommand(cl, $"print \"{msg}\"");
        }

        public static int G_TeamCount(int team)
        {
            int count = 0;
            int maxClients = Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                if (ent.Client.Pers.Team == team) count++;
            }
            return count;
        }

        public static void G_SendMatchInfo()
        {
            ServerMain.SV_SetConfigstring(ServerMain.CS_SCORES1, Level.TeamScores0.ToString());
            ServerMain.SV_SetConfigstring(ServerMain.CS_SCORES2, Level.TeamScores1.ToString());
        }

        public static void G_SetTeamScore(int team, int score)
        {
            // team 1 = Axis, team 2 = Allies; scores stored in Level.TeamScores0/1 (0-based)
            if (team == DamageSystem.TEAM_AXIS)
                Level.TeamScores0 = score;
            else if (team == DamageSystem.TEAM_ALLIES)
                Level.TeamScores1 = score;

            G_SendMatchInfo();
            OnTeamScoreChanged?.Invoke(team, score);
        }

        public static void PrintMatchInfo(GEntity ent)
        {
            if (ent == null) return;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"^3Map:^7 {Level.MapName}");
            sb.AppendLine($"^3Scores:^7 Axis {Level.TeamScores0} - Allies {Level.TeamScores1}");
            sb.AppendLine($"^3Time:^7 {Level.Time / 1000}s elapsed");
            ServerGameLogic.ClientPrint(ent, $"print \"{sb}\"");
        }

        public static void MakeReady(GEntity ent)
        {
            if (ent?.Client == null) return;
            ent.Client.Pers.Ready = true;
        }

        public static void MakeUnready(GEntity ent)
        {
            if (ent?.Client == null) return;
            ent.Client.Pers.Ready = false;
        }

        public static void CheckReadyMatchState()
        {
            if (Level.GameState == ServerGameLogic.GS_PLAYING ||
                Level.GameState == ServerGameLogic.GS_INTERMISSION) return;

            int maxClients = Level.MaxClients;
            int readyCount = 0;
            int totalCount = 0;

            for (int i = 0; i < maxClients; i++)
            {
                var ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                int t = ent.Client.Sess.SessionTeam;
                if (t == DamageSystem.TEAM_AXIS || t == DamageSystem.TEAM_ALLIES)
                {
                    totalCount++;
                    if (ent.Client.Pers.Ready) readyCount++;
                }
            }

            if (totalCount > 0 && readyCount == totalCount)
            {
                Level.GameState = ServerGameLogic.GS_WARMUP_COUNTDOWN;
                G_Broadcast("^3All players ready! Match starting...");
            }
        }

        private static LevelLocals Level => ServerGameLogic.Level;
    }

    // =========================================================================
    // GameSession — g_session.c
    // =========================================================================
    public static class GameSession
    {
        private const string KeyPrefix = "et_session_";

        public static void G_InitSessionData(GClient client, string userinfo, bool isBot)
        {
            client.Sess.SessionTeam    = (int)ClientSessionTeam.Spectator;
            client.Sess.Spectator      = 1;
            client.Sess.SpectatorNum   = 0;
            client.Sess.SpectatorClient= 0;
            client.Sess.Wins           = 0;
            client.Sess.Losses         = 0;
            client.Sess.TelePorted     = 0;

            if (isBot)
            {
                // Bots join the team with fewer players automatically.
                int axis   = GameMatch.G_TeamCount(DamageSystem.TEAM_AXIS);
                int allies = GameMatch.G_TeamCount(DamageSystem.TEAM_ALLIES);
                client.Sess.SessionTeam = axis <= allies
                    ? DamageSystem.TEAM_AXIS
                    : DamageSystem.TEAM_ALLIES;
                client.Sess.Spectator = 0;
            }
        }

        public static void G_ReadSessionData(GClient client)
        {
            // Client index is inferred from the Clients array position.
            int idx = FindClientIndex(client);
            if (idx < 0) return;

            string key = KeyPrefix + idx;
            if (!PlayerPrefs.HasKey(key)) return;

            string[] parts = PlayerPrefs.GetString(key).Split(';');
            if (parts.Length < 6) return;

            if (int.TryParse(parts[0], out int team))    client.Sess.SessionTeam = team;
            if (int.TryParse(parts[1], out int spec))    client.Sess.Spectator   = spec;
            if (int.TryParse(parts[2], out int wins))    client.Sess.Wins        = wins;
            if (int.TryParse(parts[3], out int losses))  client.Sess.Losses      = losses;
            if (int.TryParse(parts[4], out int tele))    client.Sess.TelePorted  = tele;
            if (int.TryParse(parts[5], out int specNum)) client.Sess.SpectatorNum= specNum;
        }

        public static void G_WriteSessionData(GClient client)
        {
            int idx = FindClientIndex(client);
            if (idx < 0) return;

            string key = KeyPrefix + idx;
            string val = string.Join(";",
                client.Sess.SessionTeam,
                client.Sess.Spectator,
                client.Sess.Wins,
                client.Sess.Losses,
                client.Sess.TelePorted,
                client.Sess.SpectatorNum);
            PlayerPrefs.SetString(key, val);
            PlayerPrefs.Save();
        }

        public static void G_InitWorldSession()
        {
            // Wipe stale per-client session keys when a fresh map loads with
            // NewSession set, so stale team assignments don't persist.
            if (!ServerGameLogic.Level.NewSession) return;
            for (int i = 0; i < GameConst.MAX_CLIENTS; i++)
                PlayerPrefs.DeleteKey(KeyPrefix + i);
            PlayerPrefs.Save();
        }

        public static void G_WriteSessionDataToFile()
        {
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GClient cl = ServerGameLogic.Clients[i];
                if (cl == null) continue;
                G_WriteSessionData(cl);
                GameMatch.G_LogPrintf(
                    "Session: client {0} team {1} wins {2} losses {3}",
                    i, cl.Sess.SessionTeam, cl.Sess.Wins, cl.Sess.Losses);
            }
        }

        private static int FindClientIndex(GClient client)
        {
            GClient[] clients = ServerGameLogic.Clients;
            for (int i = 0; i < clients.Length; i++)
                if (clients[i] == client) return i;
            return -1;
        }
    }

    // =========================================================================
    // GameServerStats — g_stats.c
    // =========================================================================
    public static class GameServerStats
    {
        public const int XP_KILL_NORMAL   = 3;
        public const int XP_KILL_HEADSHOT = 5;
        public const int XP_REVIVE        = 4;
        public const int XP_AMMO_PACK     = 1;
        public const int XP_CONSTRUCT     = 5;
        public const int XP_REPAIR        = 3;
        public const int XP_DYNAMITE      = 10;
        public const int XP_OBJECTIVE     = 10;

        // Objective sub-types
        public const int OBJ_GENERIC   = 0;
        public const int OBJ_DYNAMITE  = 1;
        public const int OBJ_FLAG      = 2;
        public const int OBJ_DOCUMENT  = 3;

        public static void G_CalcClientStats(GEntity ent)
        {
            if (ent?.Client == null) return;
            GClient cl = ent.Client;
            // XP total is sum of all skill points — kept in sync so HUD can display it.
            if (cl.PS.Stats == null) return;
            int total = 0;
            for (int i = 0; i < (int)SkillType.Count; i++)
                total += GetStats(cl).SkillPoints[i];
            cl.XP = total;
        }

        public static void G_AddKillSkillPoints(GEntity killer, GEntity victim, int mod)
        {
            if (killer?.Client == null) return;
            PlayerStats stats = GetStats(killer.Client);
            int killerNum = killer.EntityNum;

            if (mod == WeaponSystem.MOD_SNIPER)
            {
                GameStats.BG_AddXP(stats, SkillType.LightWeapons, XP_KILL_HEADSHOT, killerNum);
            }
            else
            {
                int classType = killer.Client.Pers.ClassType;
                SkillType classSkill = classType switch
                {
                    GameConst.PC_SOLDIER  => SkillType.HeavyWeapons,
                    GameConst.PC_MEDIC    => SkillType.FirstAid,
                    _                     => SkillType.LightWeapons,
                };
                GameStats.BG_AddXP(stats, classSkill, XP_KILL_NORMAL, killerNum);
            }

            GameStats.BG_AddXP(stats, SkillType.BattleSense, 1, killerNum);

            if (victim?.Client != null)
                victim.Client.Pers.Deaths++;
            killer.Client.Pers.Kills++;

            GetStats(killer.Client).Kills++;
            G_CalcClientStats(killer);
        }

        public static void G_AddConstructionSkillPoints(GEntity ent, int type, bool isRepair)
        {
            if (ent?.Client == null) return;
            PlayerStats stats = GetStats(ent.Client);
            int xp = isRepair ? XP_REPAIR : XP_CONSTRUCT;
            GameStats.BG_AddXP(stats, SkillType.Engineering, xp, ent.EntityNum);
            if (!isRepair) stats.Constructions++;
            G_CalcClientStats(ent);
        }

        public static void G_AddDestructionSkillPoints(GEntity ent)
        {
            if (ent?.Client == null) return;
            PlayerStats stats = GetStats(ent.Client);
            GameStats.BG_AddXP(stats, SkillType.Engineering,  XP_DYNAMITE, ent.EntityNum);
            GameStats.BG_AddXP(stats, SkillType.BattleSense,  1,            ent.EntityNum);
            stats.Destructions++;
            G_CalcClientStats(ent);
        }

        public static void G_AddReviveSkillPoints(GEntity medic)
        {
            if (medic?.Client == null) return;
            PlayerStats stats = GetStats(medic.Client);
            GameStats.BG_AddXP(stats, SkillType.FirstAid, XP_REVIVE, medic.EntityNum);
            stats.Revives++;
            G_CalcClientStats(medic);
        }

        public static void G_AddAmmoSkillPoints(GEntity fieldOps)
        {
            if (fieldOps?.Client == null) return;
            PlayerStats stats = GetStats(fieldOps.Client);
            GameStats.BG_AddXP(stats, SkillType.Signals, XP_AMMO_PACK, fieldOps.EntityNum);
            G_CalcClientStats(fieldOps);
        }

        public static void G_AddObjectiveSkillPoints(GEntity ent, int objType)
        {
            if (ent?.Client == null) return;
            PlayerStats stats = GetStats(ent.Client);
            SkillType skill = objType switch
            {
                OBJ_DYNAMITE => SkillType.Engineering,
                OBJ_FLAG     => SkillType.BattleSense,
                OBJ_DOCUMENT => SkillType.CovertOps,
                _            => SkillType.BattleSense,
            };
            GameStats.BG_AddXP(stats, skill, XP_OBJECTIVE, ent.EntityNum);
            stats.ObjectivesCompleted++;
            G_CalcClientStats(ent);
        }

        public static void G_ResetPlayerStats(GEntity ent)
        {
            if (ent?.Client == null) return;
            PlayerStats stats = GetStats(ent.Client);
            stats.XP           = 0;
            stats.Kills        = 0;
            stats.Deaths       = 0;
            stats.Revives      = 0;
            stats.Constructions= 0;
            stats.Destructions = 0;
            stats.ObjectivesCompleted = 0;
            Array.Clear(stats.SkillPoints, 0, stats.SkillPoints.Length);
            Array.Clear(stats.SkillLevel,  0, stats.SkillLevel.Length);
            ent.Client.XP = 0;
        }

        public static string CreateStats(GEntity ent)
        {
            if (ent?.Client == null) return "";
            var stats = GetStats(ent.Client);
            return $"{ent.EntityNum} {stats.Kills} {stats.Deaths} {stats.XP}";
        }

        public static void PrintStats(GEntity ent, int wIdx)
        {
            if (ent?.Client == null) return;
            var stats = GetStats(ent.Client);
            string msg = $"^3Stats for {ent.Client.Pers.Name}:^7 Kills={stats.Kills} Deaths={stats.Deaths} XP={stats.XP}";
            ServerGameLogic.ClientPrint(ent, $"print \"{msg}\n\"");
        }

        private static readonly Dictionary<GClient, PlayerStats> _statsCache =
            new Dictionary<GClient, PlayerStats>();

        private static PlayerStats GetStats(GClient cl)
        {
            if (!_statsCache.TryGetValue(cl, out PlayerStats s))
            {
                s = new PlayerStats();
                _statsCache[cl] = s;
            }
            return s;
        }
    }

    // =========================================================================
    // FireTeam — g_fireteams.c
    // =========================================================================
    public class FireTeam
    {
        public const int MAX_FIRETEAMS        = 8;
        public const int MAX_FIRETEAM_MEMBERS = 6;

        public int    TeamNum;
        public int    Team;
        public string Name;
        public int[]  Members = new int[FireTeam.MAX_FIRETEAM_MEMBERS];
        public int    Leader;
        public bool   Active;
        public bool   PrivateTeam;

        public FireTeam()
        {
            for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
                Members[i] = -1;
        }
    }

    public static class FireTeamSystem
    {
        private static readonly string[] _axisNames   =
            { "Wolfsrudel", "Wolfsbrigade", "Wolfsgruppe", "Wolfstruppe",
              "Wolfssturm", "Wolfsbatallion", "Wolfsschar", "Wolfsstaffel" };
        private static readonly string[] _alliedNames =
            { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel" };

        public static FireTeam[] FireTeams = new FireTeam[FireTeam.MAX_FIRETEAMS];

        public static event Action<int, int> OnFireteamJoin;
        public static event Action<int>      OnFireteamLeave;

        static FireTeamSystem()
        {
            for (int i = 0; i < FireTeam.MAX_FIRETEAMS; i++)
                FireTeams[i] = new FireTeam { TeamNum = i };
        }

        public static FireTeam G_RegisterFireteam(int clientNum)
        {
            GEntity ent = ServerGameLogic.Entities[clientNum];
            if (ent?.Client == null) return null;
            int team = ent.Client.Pers.Team;

            // Find a free slot on the right side (axis = 0-3, allies = 4-7)
            int start = (team == DamageSystem.TEAM_AXIS) ? 0 : FireTeam.MAX_FIRETEAMS / 2;
            int end   = start + FireTeam.MAX_FIRETEAMS / 2;
            for (int i = start; i < end; i++)
            {
                if (FireTeams[i].Active) continue;
                FireTeam ft  = FireTeams[i];
                ft.Active      = true;
                ft.Team        = team;
                ft.Leader      = clientNum;
                ft.PrivateTeam = false;
                ft.Name        = team == DamageSystem.TEAM_AXIS
                    ? _axisNames[i]
                    : _alliedNames[i - FireTeam.MAX_FIRETEAMS / 2];
                for (int m = 0; m < FireTeam.MAX_FIRETEAM_MEMBERS; m++)
                    ft.Members[m] = -1;
                ft.Members[0] = clientNum;
                G_UpdateFireteamConfigStrings();
                return ft;
            }
            return null;
        }

        public static void G_DestroyFireteam(FireTeam ft)
        {
            if (ft == null || !ft.Active) return;
            for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
            {
                if (ft.Members[i] >= 0)
                    OnFireteamLeave?.Invoke(ft.Members[i]);
                ft.Members[i] = -1;
            }
            ft.Active      = false;
            ft.Leader      = -1;
            G_UpdateFireteamConfigStrings();
        }

        public static bool G_AddClientToFireteam(int clientNum, FireTeam ft)
        {
            if (ft == null || !ft.Active) return false;
            for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
            {
                if (ft.Members[i] != -1) continue;
                ft.Members[i] = clientNum;
                G_UpdateFireteamConfigStrings();
                OnFireteamJoin?.Invoke(clientNum, ft.TeamNum);
                return true;
            }
            return false;
        }

        public static void G_RemoveClientFromFireteam(int clientNum)
        {
            FireTeam ft = G_FireteamForClient(clientNum);
            if (ft == null) return;

            for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
            {
                if (ft.Members[i] != clientNum) continue;
                ft.Members[i] = -1;
                OnFireteamLeave?.Invoke(clientNum);
                break;
            }

            // Disband if empty or elect a new leader.
            bool hasMembers = false;
            for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
                if (ft.Members[i] >= 0) { hasMembers = true; break; }

            if (!hasMembers)
            {
                G_DestroyFireteam(ft);
                return;
            }

            if (ft.Leader == clientNum)
            {
                for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
                {
                    if (ft.Members[i] < 0) continue;
                    ft.Leader = ft.Members[i];
                    break;
                }
            }
            G_UpdateFireteamConfigStrings();
        }

        public static FireTeam G_FireteamForClient(int clientNum)
        {
            foreach (FireTeam ft in FireTeams)
            {
                if (!ft.Active) continue;
                for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
                    if (ft.Members[i] == clientNum) return ft;
            }
            return null;
        }

        public static GEntity G_FireteamLeader(FireTeam ft)
        {
            if (ft == null || !ft.Active || ft.Leader < 0) return null;
            return ServerGameLogic.Entities[ft.Leader];
        }

        public static bool G_IsOnSameFireteam(int clientA, int clientB)
        {
            FireTeam ft = G_FireteamForClient(clientA);
            if (ft == null) return false;
            for (int i = 0; i < FireTeam.MAX_FIRETEAM_MEMBERS; i++)
                if (ft.Members[i] == clientB) return true;
            return false;
        }

        public static void G_UpdateFireteamConfigStrings()
        {
            for (int i = 0; i < FireTeam.MAX_FIRETEAMS; i++)
            {
                FireTeam ft = FireTeams[i];
                string val  = ft.Active
                    ? $"name\\{ft.Name}\\team\\{ft.Team}\\leader\\{ft.Leader}\\priv\\{(ft.PrivateTeam ? 1 : 0)}"
                    : "";
                // CS_FIRETEAMS starts after CS_PLAYERS block (offset 256+)
                ServerMain.SV_SetConfigstring(ServerConst.MAX_CONFIGSTRINGS - FireTeam.MAX_FIRETEAMS + i, val);
            }
        }

        // --- Client commands ---

        public static void Cmd_FireteamCreate_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            if (G_FireteamForClient(ent.EntityNum) != null)
            {
                GameMatch.G_PrintMessage(ent.EntityNum, "You are already in a fireteam.\n");
                return;
            }
            FireTeam ft = G_RegisterFireteam(ent.EntityNum);
            if (ft == null)
                GameMatch.G_PrintMessage(ent.EntityNum, "No fireteam slots available.\n");
            else
                GameMatch.G_PrintMessage(ent.EntityNum, $"Fireteam '{ft.Name}' created.\n");
        }

        public static void Cmd_FireteamDisband_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            FireTeam ft = G_FireteamForClient(ent.EntityNum);
            if (ft == null || ft.Leader != ent.EntityNum)
            {
                GameMatch.G_PrintMessage(ent.EntityNum, "You are not a fireteam leader.\n");
                return;
            }
            G_DestroyFireteam(ft);
        }

        public static void Cmd_FireteamJoin_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            int clientNum = ent.EntityNum;
            if (G_FireteamForClient(clientNum) != null)
            {
                GameMatch.G_PrintMessage(clientNum, "Already in a fireteam. Leave first.\n");
                return;
            }

            string teamNumStr = CmdSystem.Cmd_Argv(1);
            if (!int.TryParse(teamNumStr, out int num) || num < 0 || num >= FireTeam.MAX_FIRETEAMS)
            {
                GameMatch.G_PrintMessage(clientNum, "Usage: fireteam join <0-7>\n");
                return;
            }

            FireTeam ft = FireTeams[num];
            if (!ft.Active || ft.Team != ent.Client.Pers.Team)
            {
                GameMatch.G_PrintMessage(clientNum, "Invalid fireteam.\n");
                return;
            }
            if (ft.PrivateTeam)
            {
                GameMatch.G_PrintMessage(clientNum, "That fireteam is private.\n");
                return;
            }
            if (!G_AddClientToFireteam(clientNum, ft))
                GameMatch.G_PrintMessage(clientNum, "Fireteam is full.\n");
        }

        public static void Cmd_FireteamLeave_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            int clientNum = ent.EntityNum;
            if (G_FireteamForClient(clientNum) == null)
            {
                GameMatch.G_PrintMessage(clientNum, "You are not in a fireteam.\n");
                return;
            }
            G_RemoveClientFromFireteam(clientNum);
        }

        public static void Cmd_FireteamInvite_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            int clientNum = ent.EntityNum;
            FireTeam ft = G_FireteamForClient(clientNum);
            if (ft == null || ft.Leader != clientNum)
            {
                GameMatch.G_PrintMessage(clientNum, "You must be a fireteam leader to invite.\n");
                return;
            }

            string targetStr = CmdSystem.Cmd_Argv(1);
            if (!int.TryParse(targetStr, out int targetNum) ||
                targetNum < 0 || targetNum >= GameConst.MAX_CLIENTS)
            {
                GameMatch.G_PrintMessage(clientNum, "Usage: fireteam invite <clientNum>\n");
                return;
            }

            GEntity target = ServerGameLogic.Entities[targetNum];
            if (target?.Client == null || !target.InUse)
            {
                GameMatch.G_PrintMessage(clientNum, "Invalid target client.\n");
                return;
            }
            if (target.Client.Pers.Team != ent.Client.Pers.Team)
            {
                GameMatch.G_PrintMessage(clientNum, "Cannot invite players from another team.\n");
                return;
            }

            GameMatch.G_PrintMessage(targetNum,
                $"{ent.Client.Pers.Name} has invited you to fireteam '{ft.Name}'. Use: fireteam join {ft.TeamNum}\n");
        }
    }

    // =========================================================================
    // AntiLag — g_antilag.c
    // =========================================================================
    public static class AntiLag
    {
        public const int MAX_ANTILAG_HISTORY = 10;

        public class ClientHistory
        {
            public Vector3 Origin;
            public Vector3 Mins;
            public Vector3 Maxs;
            public int     Time;
        }

        private static ClientHistory[][] _history =
            new ClientHistory[GameConst.MAX_CLIENTS][];

        // Saved real positions before rewind, restored after trace.
        private static Vector3[] _savedOrigins = new Vector3[GameConst.MAX_CLIENTS];
        private static int[]     _historyHead  = new int[GameConst.MAX_CLIENTS];

        static AntiLag()
        {
            for (int i = 0; i < GameConst.MAX_CLIENTS; i++)
            {
                _history[i] = new ClientHistory[MAX_ANTILAG_HISTORY];
                for (int j = 0; j < MAX_ANTILAG_HISTORY; j++)
                    _history[i][j] = new ClientHistory();
            }
        }

        public static void G_ResetAntilag()
        {
            for (int i = 0; i < GameConst.MAX_CLIENTS; i++)
            {
                _historyHead[i] = 0;
                for (int j = 0; j < MAX_ANTILAG_HISTORY; j++)
                    _history[i][j] = new ClientHistory();
            }
        }

        public static void G_StoreClientPosition(int clientNum, Vector3 origin, Vector3 mins, Vector3 maxs, int time)
        {
            int head = (_historyHead[clientNum] + 1) % MAX_ANTILAG_HISTORY;
            _historyHead[clientNum] = head;
            ClientHistory h = _history[clientNum][head];
            h.Origin = origin;
            h.Mins   = mins;
            h.Maxs   = maxs;
            h.Time   = time;
        }

        public static void G_AdjustSinglePlayerAntilag(int clientNum, int time)
        {
            GEntity ent = ServerGameLogic.Entities[clientNum];
            if (ent == null || !ent.InUse) return;

            _savedOrigins[clientNum] = ent.Origin;

            // Find the history frame closest to and not after the requested time.
            ClientHistory best = null;
            int bestDelta = int.MaxValue;
            for (int j = 0; j < MAX_ANTILAG_HISTORY; j++)
            {
                ClientHistory h = _history[clientNum][j];
                if (h.Time == 0) continue;
                int delta = time - h.Time;
                if (delta < 0) continue;        // frame is in the future
                if (delta < bestDelta) { bestDelta = delta; best = h; }
            }

            if (best != null)
                ent.Origin = best.Origin;
        }

        public static void G_ResetPlayerAntilag()
        {
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse) continue;
                ent.Origin = _savedOrigins[i];
            }
        }

        public static TraceResult G_HistoricalTrace(
            Vector3 start, Vector3 end,
            Vector3 mins, Vector3 maxs,
            int attackerNum, int time)
        {
            int maxClients = ServerGameLogic.Level.MaxClients;

            // Rewind all clients except the attacker to the historical time.
            for (int i = 0; i < maxClients; i++)
            {
                if (i == attackerNum) continue;
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                G_AdjustSinglePlayerAntilag(i, time);
            }

            TraceResult result = CollisionSystem.CM_BoxTrace(start, end, mins, maxs, 0, -1);

            // Restore everyone.
            for (int i = 0; i < maxClients; i++)
            {
                if (i == attackerNum) continue;
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                ent.Origin = _savedOrigins[i];
            }

            return result;
        }
    }
}
