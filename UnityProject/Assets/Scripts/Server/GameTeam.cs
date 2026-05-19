// Ported from: src/game/g_team.c, src/game/g_vote.c, src/game/g_referee.c,
//              src/game/g_teammapdata.c, src/game/g_multiview.c
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
    // GameTeam — g_team.c
    // =========================================================================
    public static class GameTeam
    {
        public const string AXIS_FLAG_CLASSNAME   = "team_CTF_redflag";
        public const string ALLIES_FLAG_CLASSNAME = "team_CTF_blueflag";

        public static event Action<int, int>    OnTeamSet;
        public static event Action<int, int>    OnTeamBalance;
        public static event Action<int, string> OnTeamCommand;

        public static void G_TeamCommand(int team, string cmd)
        {
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                if (ent.Client.Sess.SessionTeam == team)
                    GameMatch.G_PrintMessage(i, cmd);
            }
            OnTeamCommand?.Invoke(team, cmd);
        }

        public static void G_Team_SetTeam(GEntity ent, int team, bool force)
        {
            if (ent?.Client == null) return;
            GClient client = ent.Client;
            if (client.Sess.SessionTeam == team) return;

            GameMatch.G_CheckForCvars();

            int oldTeam = client.Sess.SessionTeam;
            client.Sess.SessionTeam = team;
            client.Pers.Team = team;

            OnTeamSet?.Invoke(ent.EntityNum, team);

            ServerGameLogic.ClientSpawn(ent);

            string teamName = TeamName(team);
            GameMatch.G_Broadcast($"{client.Pers.Name} joined the {teamName} team.");
        }

        public static void G_Team_ForceBalance()
        {
            G_CountTeamPlayers(out int axis, out int allies);
            int diff = axis - allies;
            if (Math.Abs(diff) < 2) return;

            // Move excess players from larger team to smaller
            int srcTeam = diff > 0 ? DamageSystem.TEAM_AXIS : DamageSystem.TEAM_ALLIES;
            int dstTeam = diff > 0 ? DamageSystem.TEAM_ALLIES : DamageSystem.TEAM_AXIS;
            int toMove  = Math.Abs(diff) / 2;

            int moved = 0;
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = maxClients - 1; i >= 0 && moved < toMove; i--)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                if (ent.Client.Sess.SessionTeam != srcTeam) continue;
                G_Team_SetTeam(ent, dstTeam, true);
                moved++;
            }

            G_CountTeamPlayers(out axis, out allies);
            OnTeamBalance?.Invoke(axis, allies);
        }

        public static int G_Team_GetTeamSize(int team)
        {
            int count = 0;
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                if (ent.Client.Sess.SessionTeam == team) count++;
            }
            return count;
        }

        public static int G_Team_GetMinTeamSize()
        {
            Cvar cv = CvarSystem.Get("g_minteamsize", "1", CvarFlags.Archive);
            return Mathf.Max(1, cv.IntValue);
        }

        public static void G_CountTeamPlayers(out int axis, out int allies)
        {
            axis   = 0;
            allies = 0;
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                int t = ent.Client.Sess.SessionTeam;
                if (t == DamageSystem.TEAM_AXIS)   axis++;
                else if (t == DamageSystem.TEAM_ALLIES) allies++;
            }
        }

        public static void G_Team_TouchFlaggedEntity(GEntity ent, GEntity flag, GEntity carrier)
        {
            if (ent == null || flag == null || carrier == null) return;
            if (carrier.Client == null) return;

            // Carrier returns flag to their base — score
            int carrierTeam = carrier.Client.Sess.SessionTeam;
            if (ent.Client != null && ent.Client.Sess.SessionTeam == carrierTeam)
            {
                GameMatch.G_Broadcast($"{carrier.Client.Pers.Name} captured the flag!");
                int scoreIdx = carrierTeam == DamageSystem.TEAM_AXIS ? 0 : 1;
                if (scoreIdx == 0) ServerGameLogic.Level.TeamScores0++;
                else               ServerGameLogic.Level.TeamScores1++;
                G_Team_ResetFlags();
            }
        }

        public static void G_Team_ResetFlags()
        {
            int maxEntities = ServerGameLogic.Level.NumEntities;
            for (int i = 0; i < maxEntities; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse) continue;
                if (ent.ClassName == AXIS_FLAG_CLASSNAME || ent.ClassName == ALLIES_FLAG_CLASSNAME)
                {
                    ent.Flags &= ~GameConst.EF_NODRAW;
                }
            }
        }

        public static void G_Team_FragBonuses(GEntity targ, GEntity inflictor, GEntity attacker)
        {
            if (targ == null || attacker == null) return;
            if (targ.Client == null || attacker.Client == null) return;
            if (targ == attacker) return;

            int targTeam    = targ.Client.Sess.SessionTeam;
            int attackTeam  = attacker.Client.Sess.SessionTeam;

            if (targTeam == attackTeam) return; // no bonus for friendly fire

            attacker.Client.Pers.Kills++;
            attacker.Client.XP += 5;
        }

        public static void G_Team_CheckDroppedItem(GEntity ent)
        {
            if (ent == null || ent.Client == null) return;
            // Stub: in ET, dropped items (ammo packs, health) are checked against
            // nearby teammates. Implementation depends on item system integration.
        }

        public static GEntity G_SelectRandomTeamSpawnPoint(int team, bool isBot)
        {
            string spawnClass = team == DamageSystem.TEAM_AXIS
                ? "info_player_axis"
                : "info_player_allies";

            var candidates = new List<GEntity>();
            int maxEntities = ServerGameLogic.Level.NumEntities;
            for (int i = 0; i < maxEntities; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse) continue;
                if (ent.ClassName == spawnClass) candidates.Add(ent);
            }

            if (candidates.Count == 0) return null;

            int idx = UnityEngine.Random.Range(0, candidates.Count);
            return candidates[idx];
        }

        private static string TeamName(int team)
        {
            return team switch
            {
                DamageSystem.TEAM_AXIS      => "Axis",
                DamageSystem.TEAM_ALLIES    => "Allies",
                DamageSystem.TEAM_SPECTATOR => "Spectators",
                _                           => "Free",
            };
        }
    }

    // =========================================================================
    // VoteSystem — g_vote.c
    // =========================================================================
    public enum VoteType
    {
        None       = 0,
        Map        = 1,
        MapRestart = 2,
        NextMap    = 3,
        Kick       = 4,
        MuteSpecs  = 5,
        Shuffle    = 6,
        SwapTeams  = 7,
        Surrender  = 8,
        Referee    = 9,
    }

    public static class VoteSystem
    {
        public static bool     VoteInProgress;
        public static string   VoteString;
        public static int      VoteYes;
        public static int      VoteNo;
        public static int      VoteTime;
        public static int      NumVotingClients;
        public static VoteType ActiveVoteType;

        private static int    _voteTargetClient = -1;
        private static string _voteArg;

        public static event Action<string, bool> OnVoteComplete;
        public static event Action<string>       OnVoteStarted;

        public static void G_CheckVote()
        {
            if (!VoteInProgress) return;

            int elapsed = ServerGameLogic.Level.Time - VoteTime;
            if (elapsed >= 30000)
            {
                G_ProcessVote(false);
                return;
            }

            NumVotingClients = CountVotingClients();
            int needed = NumVotingClients / 2 + 1;

            if (VoteYes >= needed) { G_ProcessVote(true);  return; }
            if (VoteNo  >= needed) { G_ProcessVote(false); return; }
        }

        public static string G_CallVote(GEntity caller, VoteType type, string args)
        {
            if (caller?.Client == null) return "Invalid caller.";
            if (VoteInProgress)         return "A vote is already in progress.";

            string err = type switch
            {
                VoteType.Map      => ValidateVote_Map(args),
                VoteType.Kick     => ValidateVote_Kick(args, out _voteTargetClient),
                VoteType.Referee  => ValidateVote_Referee(args, out _voteTargetClient),
                _                 => null,
            };

            if (err != null) return err;

            _voteArg       = args;
            ActiveVoteType = type;
            VoteString     = BuildVoteString(type, args);
            VoteYes        = 0;
            VoteNo         = 0;
            VoteTime       = ServerGameLogic.Level.Time;
            VoteInProgress = true;
            NumVotingClients = CountVotingClients();

            GameMatch.G_Broadcast($"Vote called: {VoteString}");
            OnVoteStarted?.Invoke(VoteString);
            return null;
        }

        public static void G_Vote(GEntity ent, bool yes)
        {
            if (!VoteInProgress || ent?.Client == null) return;

            // EF_VOTED prevents double-voting
            if ((ent.Client.PS.EFlags & GameConst.EF_VOTED) != 0) return;
            ent.Client.PS.EFlags |= GameConst.EF_VOTED;

            if (yes) VoteYes++;
            else     VoteNo++;
        }

        public static void G_ProcessVote(bool passed)
        {
            if (!VoteInProgress) return;

            string vs = VoteString;
            VoteInProgress = false;
            ActiveVoteType = VoteType.None;

            // Clear EF_VOTED on all clients
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || ent.Client == null) continue;
                ent.Client.PS.EFlags &= ~GameConst.EF_VOTED;
            }

            if (passed) ApplyVoteResult(ActiveVoteType, _voteArg);

            string outcome = passed ? "passed" : "failed";
            GameMatch.G_Broadcast($"Vote {outcome}: {vs}");
            OnVoteComplete?.Invoke(vs, passed);
        }

        public static void G_CancelVote()
        {
            if (!VoteInProgress) return;
            G_ProcessVote(false);
        }

        private static int CountVotingClients()
        {
            int count = 0;
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                int t = ent.Client.Sess.SessionTeam;
                if (t == DamageSystem.TEAM_AXIS || t == DamageSystem.TEAM_ALLIES) count++;
            }
            return count;
        }

        private static string ValidateVote_Map(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return "Please specify a map name.";
            return null;
        }

        private static string ValidateVote_Kick(string args, out int targetClientNum)
        {
            targetClientNum = -1;
            if (!int.TryParse(args, out int num)) return "Invalid client number.";
            if (num < 0 || num >= ServerGameLogic.Level.MaxClients) return "Client not found.";
            GEntity target = ServerGameLogic.Entities[num];
            if (target == null || !target.InUse || target.Client == null) return "Client not found.";
            targetClientNum = num;
            return null;
        }

        private static string ValidateVote_Referee(string args, out int targetClientNum)
        {
            targetClientNum = -1;
            if (!int.TryParse(args, out int num)) return "Invalid client number.";
            if (num < 0 || num >= ServerGameLogic.Level.MaxClients) return "Client not found.";
            GEntity target = ServerGameLogic.Entities[num];
            if (target == null || !target.InUse || target.Client == null) return "Client not found.";
            targetClientNum = num;
            return null;
        }

        private static string BuildVoteString(VoteType type, string args)
        {
            return type switch
            {
                VoteType.Map        => $"map {args}",
                VoteType.MapRestart => "map_restart",
                VoteType.NextMap    => "nextmap",
                VoteType.Kick       => $"kick {args}",
                VoteType.MuteSpecs  => "mutespecs",
                VoteType.Shuffle    => "shuffle",
                VoteType.SwapTeams  => "swapteams",
                VoteType.Surrender  => "surrender",
                VoteType.Referee    => $"referee {args}",
                _                   => args ?? "",
            };
        }

        private static void ApplyVoteResult(VoteType type, string args)
        {
            switch (type)
            {
                case VoteType.MapRestart:
                    CvarSystem.Set("sv_mapRestart", "1");
                    break;

                case VoteType.Map:
                    CvarSystem.Set("sv_nextmap", args ?? "");
                    break;

                case VoteType.Kick:
                    if (int.TryParse(args, out int kickNum))
                    {
                        GEntity target = ServerGameLogic.Entities[kickNum];
                        if (target?.Client != null)
                            GameMatch.G_Broadcast($"{target.Client.Pers.Name} was kicked by vote.");
                    }
                    break;

                case VoteType.SwapTeams:
                    SwapAllTeams();
                    break;

                case VoteType.Shuffle:
                    ShuffleTeams();
                    break;

                case VoteType.Referee:
                    if (int.TryParse(args, out int refNum))
                    {
                        GEntity target = ServerGameLogic.Entities[refNum];
                        if (target != null) RefereeSystem.G_MakeReferee(target);
                    }
                    break;
            }
        }

        private static void SwapAllTeams()
        {
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || ent.Client == null) continue;
                int t = ent.Client.Sess.SessionTeam;
                if (t == DamageSystem.TEAM_AXIS)
                {
                    ent.Client.Sess.SessionTeam = DamageSystem.TEAM_ALLIES;
                    ent.Client.Pers.Team        = DamageSystem.TEAM_ALLIES;
                }
                else if (t == DamageSystem.TEAM_ALLIES)
                {
                    ent.Client.Sess.SessionTeam = DamageSystem.TEAM_AXIS;
                    ent.Client.Pers.Team        = DamageSystem.TEAM_AXIS;
                }
            }
            GameMatch.G_Broadcast("Teams have been swapped.");
        }

        private static void ShuffleTeams()
        {
            var players = new List<GEntity>();
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                GEntity ent = ServerGameLogic.Entities[i];
                if (ent == null || ent.Client == null) continue;
                int t = ent.Client.Sess.SessionTeam;
                if (t == DamageSystem.TEAM_AXIS || t == DamageSystem.TEAM_ALLIES)
                    players.Add(ent);
            }

            // Fisher-Yates shuffle then assign alternating teams
            for (int i = players.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (players[i], players[j]) = (players[j], players[i]);
            }

            for (int i = 0; i < players.Count; i++)
            {
                int t = i % 2 == 0 ? DamageSystem.TEAM_AXIS : DamageSystem.TEAM_ALLIES;
                players[i].Client.Sess.SessionTeam = t;
                players[i].Client.Pers.Team        = t;
            }

            GameMatch.G_Broadcast("Teams have been shuffled.");
        }
    }

    // =========================================================================
    // RefereeSystem — g_referee.c
    // =========================================================================
    public static class RefereeSystem
    {
        public const int MAX_REFEREES = 4;

        private static readonly bool[] _isReferee = new bool[GameConst.MAX_CLIENTS];
        private static int _refereeCount = 0;

        public static event Action<int, bool> OnRefereeChanged;

        public static bool G_MakeReferee(GEntity ent)
        {
            if (ent?.Client == null) return false;
            int cn = ent.EntityNum;
            if (cn < 0 || cn >= GameConst.MAX_CLIENTS) return false;
            if (_isReferee[cn]) return true;
            if (_refereeCount >= MAX_REFEREES)
            {
                GameMatch.G_PrintMessage(cn, "Maximum number of referees reached.");
                return false;
            }

            _isReferee[cn] = true;
            _refereeCount++;
            GameMatch.G_Broadcast($"{ent.Client.Pers.Name} is now a referee.");
            OnRefereeChanged?.Invoke(cn, true);
            return true;
        }

        public static void G_RemoveReferee(GEntity ent)
        {
            if (ent?.Client == null) return;
            int cn = ent.EntityNum;
            if (cn < 0 || cn >= GameConst.MAX_CLIENTS) return;
            if (!_isReferee[cn]) return;

            _isReferee[cn] = false;
            _refereeCount  = Mathf.Max(0, _refereeCount - 1);
            GameMatch.G_Broadcast($"{ent.Client.Pers.Name} is no longer a referee.");
            OnRefereeChanged?.Invoke(cn, false);
        }

        public static bool G_IsReferee(int clientNum)
        {
            if (clientNum < 0 || clientNum >= GameConst.MAX_CLIENTS) return false;
            return _isReferee[clientNum];
        }

        public static void Cmd_Referee_f(GEntity ent)
        {
            if (ent?.Client == null) return;

            // Already a referee
            if (G_IsReferee(ent.EntityNum))
            {
                GameMatch.G_PrintMessage(ent.EntityNum, "You are already a referee.");
                return;
            }

            // Check referee password cvar
            string refPass = CvarSystem.GetString("g_refereePassword", "");
            if (!string.IsNullOrEmpty(refPass))
            {
                // In a real command handler the password arg would come from the command tokens.
                // Here we gate on password being set; full parsing belongs in GameCommands.
                GameMatch.G_PrintMessage(ent.EntityNum, "Referee password required.");
                return;
            }

            G_MakeReferee(ent);
        }

        public static void Cmd_Unreferee_f(GEntity ent)
        {
            if (ent?.Client == null) return;
            G_RemoveReferee(ent);
        }

        public static void G_RefereeStatus(GEntity ent)
        {
            if (ent?.Client == null) return;
            int cn = ent.EntityNum;
            bool isRef = G_IsReferee(cn);
            GameMatch.G_PrintMessage(cn, isRef ? "You are a referee." : "You are not a referee.");

            // Send full referee list
            int maxClients = ServerGameLogic.Level.MaxClients;
            for (int i = 0; i < maxClients; i++)
            {
                if (!_isReferee[i]) continue;
                GEntity r = ServerGameLogic.Entities[i];
                string name = r?.Client?.Pers.Name ?? $"client {i}";
                GameMatch.G_PrintMessage(cn, $"  Referee: {name}");
            }
        }
    }

    // =========================================================================
    // TeamMapData — g_teammapdata.c
    // =========================================================================
    public class ObjectiveInfo
    {
        public int     ObjNum;
        public string  Description;
        public int     Team;
        public int     Status;
        public Vector3 WorldPos;
        public bool    Inverted;
    }

    public class SpawnTimerInfo
    {
        public int Team;
        public int RespawnTime;
        public int NextRespawnTime;
    }

    public static class TeamMapData
    {
        public const int MAX_OBJECTIVES = 16;
        public static readonly ObjectiveInfo[]   Objectives   = new ObjectiveInfo[MAX_OBJECTIVES];
        public static readonly SpawnTimerInfo[]  SpawnTimers  = new SpawnTimerInfo[2];

        public static event Action<int, int> OnObjectiveStatusChanged;
        public static event Action<int, int> OnRespawnWave;
        public static event Action<int>      OnMatchWinner;

        public static void G_InitTeamMapData()
        {
            for (int i = 0; i < MAX_OBJECTIVES; i++)
            {
                Objectives[i] = new ObjectiveInfo
                {
                    ObjNum      = i,
                    Description = "",
                    Team        = DamageSystem.TEAM_AXIS,
                    Status      = 0,
                    WorldPos    = Vector3.zero,
                    Inverted    = false,
                };
            }

            SpawnTimers[0] = new SpawnTimerInfo { Team = DamageSystem.TEAM_AXIS,   RespawnTime = 20, NextRespawnTime = 0 };
            SpawnTimers[1] = new SpawnTimerInfo { Team = DamageSystem.TEAM_ALLIES, RespawnTime = 20, NextRespawnTime = 0 };
        }

        public static void G_ParseObjectives(string objectivesData)
        {
            if (string.IsNullOrEmpty(objectivesData)) return;

            // Format: semicolon-separated entries of "num,team,status,inverted,description,x,y,z"
            string[] entries = objectivesData.Split(';');
            foreach (string entry in entries)
            {
                string[] parts = entry.Split(',');
                if (parts.Length < 6) continue;
                if (!int.TryParse(parts[0], out int num)) continue;
                if (num < 0 || num >= MAX_OBJECTIVES) continue;

                int.TryParse(parts[1], out int team);
                int.TryParse(parts[2], out int status);
                bool.TryParse(parts[3], out bool inverted);
                string desc = parts[4];
                float.TryParse(parts[5], out float x);
                float.TryParse(parts.Length > 6 ? parts[6] : "0", out float y);
                float.TryParse(parts.Length > 7 ? parts[7] : "0", out float z);

                Objectives[num].Team        = team;
                Objectives[num].Status      = status;
                Objectives[num].Inverted    = inverted;
                Objectives[num].Description = desc;
                Objectives[num].WorldPos    = new Vector3(x, y, z);
            }
        }

        public static void G_SetObjectiveStatus(int objNum, int status)
        {
            if (objNum < 0 || objNum >= MAX_OBJECTIVES) return;
            if (Objectives[objNum].Status == status) return;

            Objectives[objNum].Status = status;
            OnObjectiveStatusChanged?.Invoke(objNum, status);

            int winner = G_CheckObjectiveComplete();
            if (winner >= 0) OnMatchWinner?.Invoke(winner);
        }

        public static int G_CheckObjectiveComplete()
        {
            // Each team wins when all objectives assigned to the opposing team are complete (or
            // all of their own inverted objectives are destroyed). Returns winning team or -1.
            bool axisWin   = true;
            bool alliesWin = true;

            for (int i = 0; i < MAX_OBJECTIVES; i++)
            {
                ObjectiveInfo obj = Objectives[i];
                if (obj.Description == "") continue; // inactive slot

                if (obj.Team == DamageSystem.TEAM_AXIS)
                {
                    // Axis must achieve this objective
                    if (obj.Status != 1) axisWin = false;
                }
                else if (obj.Team == DamageSystem.TEAM_ALLIES)
                {
                    if (obj.Status != 1) alliesWin = false;
                }
            }

            if (axisWin)   return DamageSystem.TEAM_AXIS;
            if (alliesWin) return DamageSystem.TEAM_ALLIES;
            return -1;
        }

        public static void G_SpawnWaveThink(int levelTime)
        {
            for (int t = 0; t < 2; t++)
            {
                SpawnTimerInfo timer = SpawnTimers[t];
                if (levelTime < timer.NextRespawnTime) continue;

                timer.NextRespawnTime = levelTime + timer.RespawnTime * 1000;

                int spawnTeam = t == 0 ? DamageSystem.TEAM_AXIS : DamageSystem.TEAM_ALLIES;
                int count = 0;

                int maxClients = ServerGameLogic.Level.MaxClients;
                for (int i = 0; i < maxClients; i++)
                {
                    GEntity ent = ServerGameLogic.Entities[i];
                    if (ent?.Client == null) continue;
                    if (ent.Client.Sess.SessionTeam != spawnTeam) continue;
                    if (ent.Client.PS.PmType != GameConst.PM_DEAD) continue;
                    ServerGameLogic.ClientSpawn(ent);
                    count++;
                }

                OnRespawnWave?.Invoke(spawnTeam, count);
            }
        }

        public static void G_SetRespawnTime(int team, int seconds)
        {
            if (team == DamageSystem.TEAM_AXIS)        SpawnTimers[0].RespawnTime = seconds;
            else if (team == DamageSystem.TEAM_ALLIES) SpawnTimers[1].RespawnTime = seconds;
        }

        public static int G_GetNextRespawnTime(int team, int levelTime)
        {
            SpawnTimerInfo timer = null;
            if (team == DamageSystem.TEAM_AXIS)        timer = SpawnTimers[0];
            else if (team == DamageSystem.TEAM_ALLIES) timer = SpawnTimers[1];
            if (timer == null) return 0;
            return Mathf.Max(0, timer.NextRespawnTime - levelTime);
        }
    }

    // =========================================================================
    // MultiviewSystem — g_multiview.c
    // =========================================================================
    public static class MultiviewSystem
    {
        public const int MAX_MULTIVIEW_CLIENTS = 4;

        // Per-spectator multiview entity lists; keyed by spectator entity number
        private static readonly Dictionary<int, List<int>> _mvLists =
            new Dictionary<int, List<int>>();

        public static void G_smvRunPlayer(GEntity ent)
        {
            if (ent?.Client == null) return;
            if (ent.Client.Sess.SessionTeam != DamageSystem.TEAM_SPECTATOR) return;

            // Refresh the multiview list: prune disconnected or dead targets
            if (!_mvLists.TryGetValue(ent.EntityNum, out var list)) return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                GEntity target = ServerGameLogic.Entities[list[i]];
                if (target == null || !target.InUse || target.Client == null)
                    list.RemoveAt(i);
            }

            G_smvUpdateClientCSList(ent);
        }

        public static bool G_smvLocateEntityInMVList(GEntity ent, int clientNum)
        {
            if (ent == null) return false;
            if (!_mvLists.TryGetValue(ent.EntityNum, out var list)) return false;
            return list.Contains(clientNum);
        }

        public static void G_smvAddEntityToMVList(GEntity ent, int clientNum)
        {
            if (ent == null) return;
            if (!_mvLists.TryGetValue(ent.EntityNum, out var list))
            {
                list = new List<int>(MAX_MULTIVIEW_CLIENTS);
                _mvLists[ent.EntityNum] = list;
            }

            if (list.Contains(clientNum)) return;
            if (list.Count >= MAX_MULTIVIEW_CLIENTS) return;

            list.Add(clientNum);
            G_smvUpdateClientCSList(ent);
        }

        public static void G_smvRemoveEntityFromMVList(GEntity ent, int clientNum)
        {
            if (ent == null) return;
            if (!_mvLists.TryGetValue(ent.EntityNum, out var list)) return;
            list.Remove(clientNum);
            G_smvUpdateClientCSList(ent);
        }

        public static void G_smvAllied_TeamTargetFirst(GEntity ent)
        {
            if (ent?.Client == null) return;
            if (!_mvLists.TryGetValue(ent.EntityNum, out var list) || list.Count < 2) return;

            int spectatorTeam = ent.Client.Pers.Team;

            // Stable sort: same-team targets first
            list.Sort((a, b) =>
            {
                GEntity ea = ServerGameLogic.Entities[a];
                GEntity eb = ServerGameLogic.Entities[b];
                bool aAllied = ea?.Client?.Sess.SessionTeam == spectatorTeam;
                bool bAllied = eb?.Client?.Sess.SessionTeam == spectatorTeam;
                if (aAllied == bAllied) return 0;
                return aAllied ? -1 : 1;
            });
        }

        public static void G_smvUpdateClientCSList(GEntity ent)
        {
            if (ent == null) return;

            string cs = "";
            if (_mvLists.TryGetValue(ent.EntityNum, out var list))
                cs = string.Join(",", list);

            // Configstring slot: CS_MULTIVIEW_BASE + spectator entity num
            // The base offset is project-specific; using 900 as a safe range.
            int csIdx = 900 + ent.EntityNum;
            if (csIdx < ServerConst.MAX_CONFIGSTRINGS)
                ServerMain.SV_SetConfigstring(csIdx, cs);
        }
    }
}
