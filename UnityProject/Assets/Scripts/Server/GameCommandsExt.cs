// Ports: g_cmds_ext.c — OSP-style extended player commands
// Integrates with GameCommands.cs (ET.Server) and GameMatch.cs (GameServerStats).

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ET.Game;

namespace ET.Server
{
    // WS_* weapon-stat indices (22 entries matching the original aWeaponInfo table)
    public enum WeaponStatIndex
    {
        Knife = 0, Luger, Colt, Mp40, Thompson, Sten, Fg42,
        Panzerfaust, Flamethrower, Mg42, Mortar, Garand,
        Snooper, Gpg13, Nade, Dynamite, Airstrike, Arty,
        Syringe, Smg, Grenadepistol, Landmine,
        MAX
    }

    public struct WeaponStatEntry
    {
        public string Code;
        public string Name;
    }

    public static class WeaponStatInfo
    {
        public static readonly WeaponStatEntry[] Table =
        {
            new WeaponStatEntry { Code = "knife",    Name = "Knife"           },
            new WeaponStatEntry { Code = "luger",    Name = "Luger"           },
            new WeaponStatEntry { Code = "colt",     Name = "Colt"            },
            new WeaponStatEntry { Code = "mp40",     Name = "MP 40"           },
            new WeaponStatEntry { Code = "thompson",  Name = "Thompson"        },
            new WeaponStatEntry { Code = "sten",     Name = "Sten"            },
            new WeaponStatEntry { Code = "fg42",     Name = "FG 42"           },
            new WeaponStatEntry { Code = "panzerfaust", Name = "Panzerfaust"  },
            new WeaponStatEntry { Code = "flamethrower", Name = "Flamethrower"},
            new WeaponStatEntry { Code = "mg42",     Name = "MG 42"           },
            new WeaponStatEntry { Code = "mortar",   Name = "Mortar"          },
            new WeaponStatEntry { Code = "garand",   Name = "Garand"          },
            new WeaponStatEntry { Code = "snooper",  Name = "Snooper Rifle"   },
            new WeaponStatEntry { Code = "gpg13",    Name = "GPG-13"          },
            new WeaponStatEntry { Code = "nade",     Name = "Grenade"         },
            new WeaponStatEntry { Code = "dynamite", Name = "Dynamite"        },
            new WeaponStatEntry { Code = "airstrike", Name = "Airstrike"      },
            new WeaponStatEntry { Code = "arty",     Name = "Artillery"       },
            new WeaponStatEntry { Code = "syringe",  Name = "Syringe"         },
            new WeaponStatEntry { Code = "smg",      Name = "SMG"             },
            new WeaponStatEntry { Code = "gpistol",  Name = "Grenade Pistol"  },
            new WeaponStatEntry { Code = "landmine", Name = "Land Mine"       },
        };

        // Minimum shots required before a player qualifies for accuracy rankings
        public static readonly int[] QualifyingShots =
        {
            10, 15, 15, 30, 30, 30, 30, 3, 100, 5, 5, 5, 3, 3, 5, 3, 3, 5, 10, 100, 30, 30
        };
    }

    // -------------------------------------------------------------------------
    // GameCommandsExt — extended OSP-style match commands
    // -------------------------------------------------------------------------
    public static class GameCommandsExt
    {
        private const int CMD_DEBOUNCE_MS  = 5000;
        private const int HELP_COLUMNS     = 4;
        private const int PAUSE_NONE       = 0;
        private const int PAUSE_UNPAUSING  = -1;

        // ------------------------------------------------------------------ //
        // Command dispatch table entry
        // ------------------------------------------------------------------ //
        private struct CmdInfo
        {
            public string Name;
            public bool AnyTime;        // can run outside warmup
            public bool Value;          // default bool argument
            public string HelpText;
            public Action<GEntity, int, bool> Fn;
        }

        private static readonly CmdInfo[] _commands;

        static GameCommandsExt()
        {
            _commands = new CmdInfo[]
            {
                new CmdInfo { Name = "?",            AnyTime = true,  Value = true,  HelpText = ":^7 Gives a list of OSP-specific commands",                               Fn = Cmd_Commands    },
                new CmdInfo { Name = "commands",     AnyTime = true,  Value = true,  HelpText = ":^7 Gives a list of OSP-specific commands",                               Fn = Cmd_Commands    },
                new CmdInfo { Name = "follow",       AnyTime = false, Value = true,  HelpText = " <player_ID|allies|axis>:^7 Spectates a particular player or team",       Fn = Cmd_Follow      },
                new CmdInfo { Name = "lock",         AnyTime = true,  Value = true,  HelpText = ":^7 Locks a player's team to prevent others from joining",                 Fn = Cmd_Lock        },
                new CmdInfo { Name = "unlock",       AnyTime = true,  Value = false, HelpText = ":^7 Unlocks a player's team, allowing others to join",                    Fn = Cmd_Lock        },
                new CmdInfo { Name = "notready",     AnyTime = true,  Value = false, HelpText = ":^7 Sets your status to ^5not ready^7 to start a match",                  Fn = Cmd_Ready       },
                new CmdInfo { Name = "ready",        AnyTime = true,  Value = true,  HelpText = ":^7 Sets your status to ^5ready^7 to start a match",                      Fn = Cmd_Ready       },
                new CmdInfo { Name = "unready",      AnyTime = true,  Value = false, HelpText = ":^7 Sets your status to ^5not ready^7 to start a match",                  Fn = Cmd_Ready       },
                new CmdInfo { Name = "readyteam",    AnyTime = false, Value = true,  HelpText = ":^7 Sets an entire team's status to ^5ready^7 to start a match",           Fn = Cmd_TeamReady   },
                new CmdInfo { Name = "pause",        AnyTime = false, Value = true,  HelpText = ":^7 Allows a team to pause a match",                                       Fn = Cmd_Pause       },
                new CmdInfo { Name = "timeout",      AnyTime = false, Value = true,  HelpText = ":^7 Allows a team to pause a match",                                       Fn = Cmd_Pause       },
                new CmdInfo { Name = "timein",       AnyTime = false, Value = false, HelpText = ":^7 Unpauses a match (if initiated by the issuing team)",                  Fn = Cmd_Pause       },
                new CmdInfo { Name = "unpause",      AnyTime = false, Value = false, HelpText = ":^7 Unpauses a match (if initiated by the issuing team)",                  Fn = Cmd_Pause       },
                new CmdInfo { Name = "players",      AnyTime = true,  Value = true,  HelpText = ":^7 Lists all active players and their IDs/information",                   Fn = Cmd_Players     },
                new CmdInfo { Name = "ref",          AnyTime = true,  Value = true,  HelpText = " <password>:^7 Become a referee (admin access)",                           Fn = Cmd_Ref         },
                new CmdInfo { Name = "say_teamnl",   AnyTime = true,  Value = true,  HelpText = "<msg>:^7 Sends a team chat without location info",                         Fn = Cmd_SayTeamNL   },
                new CmdInfo { Name = "scores",       AnyTime = true,  Value = true,  HelpText = ":^7 Displays current match stat info",                                     Fn = Cmd_Scores      },
                new CmdInfo { Name = "specinvite",   AnyTime = true,  Value = true,  HelpText = ":^7 Invites a player to spectate a speclock'ed team",                      Fn = Cmd_SpecInvite  },
                new CmdInfo { Name = "speclock",     AnyTime = true,  Value = true,  HelpText = ":^7 Locks a player's team from spectators",                                Fn = Cmd_SpecLock    },
                new CmdInfo { Name = "specunlock",   AnyTime = true,  Value = false, HelpText = ":^7 Unlocks a player's team from spectators",                              Fn = Cmd_SpecLock    },
                new CmdInfo { Name = "statsall",     AnyTime = true,  Value = false, HelpText = ":^7 Shows weapon accuracy stats for all players",                          Fn = Cmd_StatsAll    },
                new CmdInfo { Name = "topshots",     AnyTime = true,  Value = true,  HelpText = ":^7 Shows BEST player for each weapon",                                    Fn = Cmd_WeaponRankings },
                new CmdInfo { Name = "bottomshots",  AnyTime = true,  Value = false, HelpText = ":^7 Shows WORST player for each weapon",                                   Fn = Cmd_WeaponRankings },
                new CmdInfo { Name = "weaponstats",  AnyTime = true,  Value = false, HelpText = " [player_ID]:^7 Shows weapon accuracy stats for a player",                 Fn = Cmd_WeaponStats },
            };
        }

        // G_commandCheck — called from the main command dispatcher
        public static bool CheckCommand(GEntity ent, string cmd, bool doAnytime)
        {
            for (int i = 0; i < _commands.Length; i++)
            {
                var cr = _commands[i];
                if (cr.Fn != null &&
                    cr.AnyTime == doAnytime &&
                    string.Equals(cmd, cr.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!ShowHelp(ent, cmd, i))
                        cr.Fn(ent, i, cr.Value);
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ //
        // Helper utilities
        // ------------------------------------------------------------------ //

        private static void CP(GEntity ent, string msg)   => ServerGameLogic.ClientPrint(ent, msg);
        private static void AP(string msg)                 => ServerGameLogic.AllPrint(msg);

        private static bool ShowHelp(GEntity ent, string cmdName, int idx)
        {
            string arg = CmdSystem.Cmd_Argv(1);
            if (arg == "?")
            {
                CP(ent, $"\n^3{cmdName}{_commands[idx].HelpText}\n\n");
                return true;
            }
            return false;
        }

        private static bool Debounce(GEntity ent, int cmdIdx)
        {
            int now = ServerGameLogic.Level.Time;
            if (ent.Client.Pers.CmdDebounce > now)
            {
                float secs = (ent.Client.Pers.CmdDebounce - now) / 1000f;
                CP(ent, $"print \"Wait another {secs:F1}s to issue ^3{_commands[cmdIdx].Name}\n\"");
                return false;
            }
            ent.Client.Pers.CmdDebounce = now + CMD_DEBOUNCE_MS;
            return true;
        }

        private static void NoTeamControls(GEntity ent) =>
            CP(ent, "cpm \"Team commands not enabled on this server.\n\"");

        // ------------------------------------------------------------------ //
        // Command implementations
        // ------------------------------------------------------------------ //

        // ? / commands
        private static void Cmd_Commands(GEntity ent, int idx, bool val)
        {
            int total = _commands.Length;
            int rows  = (total + HELP_COLUMNS - 1) / HELP_COLUMNS;
            CP(ent, "cpm \"^5\nAvailable OSP Game-Commands:\n----------------------------\n\"");
            for (int r = 0; r < rows; r++)
            {
                string line = "";
                for (int c = 0; c < HELP_COLUMNS; c++)
                {
                    int i = r + c * rows;
                    if (i < total) line += $"^3{_commands[i].Name,-17}";
                }
                CP(ent, $"print \"{line}\n\"");
            }
            CP(ent, "cpm \"\nType: ^3\\command_name ?^7 for more information\n\"");
        }

        // follow
        private static void Cmd_Follow(GEntity ent, int idx, bool val)
        {
            string arg = CmdSystem.Cmd_Argv(1);
            if (string.IsNullOrEmpty(arg)) { CP(ent, "print \"Usage: follow <player_ID|allies|axis>\n\""); return; }

            int team = -1;
            if (string.Equals(arg, "allies", StringComparison.OrdinalIgnoreCase)) team = GameConstants.TEAM_ALLIES;
            else if (string.Equals(arg, "axis", StringComparison.OrdinalIgnoreCase)) team = GameConstants.TEAM_AXIS;

            if (team >= 0)
            {
                ent.Client.Sess.SpectatorState = 1;   // SPECTATOR_FOLLOW
                ent.Client.Sess.SpectatorTeam  = team;
            }
            else if (int.TryParse(arg, out int pid) && pid >= 0 && pid < GameConstants.MAX_CLIENTS)
            {
                ent.Client.Sess.SpectatorState  = 1;
                ent.Client.Sess.SpectatorClient = pid;
            }
        }

        // lock / unlock
        private static void Cmd_Lock(GEntity ent, int idx, bool fLock)
        {
            if (CvarSystem.GetInt("team_nocontrols") != 0) { NoTeamControls(ent); return; }
            if (!Debounce(ent, idx)) return;

            int tteam = ent.Client.Sess.SessionTeam;
            if (tteam == GameConstants.TEAM_AXIS || tteam == GameConstants.TEAM_ALLIES)
            {
                var ti = GameTeam.TeamInfo[tteam];
                if (ti.TeamLock == fLock)
                    CP(ent, $"print \"^3Your team is already {(fLock ? "lock" : "unlock")}ed!\n\"");
                else
                {
                    ti.TeamLock = fLock;
                    string msg = $"\"The {GameTeam.TeamName(tteam)} team is now {(fLock ? "lock" : "unlock")}ed!\n\"";
                    AP($"print {msg}");
                    AP($"cp {msg}");
                }
            }
            else CP(ent, $"print \"Spectators can't {(fLock ? "lock" : "unlock")} a team!\n\"");
        }

        // pause / timein / timeout / unpause
        private static void Cmd_Pause(GEntity ent, int idx, bool fPause)
        {
            if (CvarSystem.GetInt("team_nocontrols") != 0) { NoTeamControls(ent); return; }

            var level = ServerGameLogic.Level;
            int pause = level.MatchPause;

            if ((pause >= PAUSE_UNPAUSING && !fPause) || (pause != PAUSE_NONE && fPause))
            {
                CP(ent, $"print \"The match is already {(fPause ? "^1" : "^5UN")}PAUSED^7!\n\"");
                return;
            }

            if (ent.Client.Sess.Referee != 0)
            {
                RefereeSystem.RefPause(ent, fPause);
                return;
            }

            if (!Debounce(ent, idx)) return;

            int tteam = ent.Client.Sess.SessionTeam;
            var ti = GameTeam.TeamInfo[tteam];

            if (fPause)
            {
                if (ti.Timeouts <= 0) { CP(ent, "cpm \"^3Your team has no more timeouts remaining!\n\""); return; }
                ti.Timeouts--;
                level.MatchPause = tteam + 128;
                AudioSystem.PlayGlobalSound("sound/misc/referee.wav");
                AP($"print \"^3Match is ^1PAUSED^3!\n^7[{GameTeam.TeamName(tteam)}^7: - {ti.Timeouts} Timeouts Remaining]\n\"");
                CP(ent, $"cp \"^3Match is ^1PAUSED^3! ({GameTeam.TeamName(tteam)}^3)\n\"");
            }
            else if (tteam + 128 != pause)
                CP(ent, "cpm \"^3Your team didn't call the timeout!\n\"");
            else
            {
                AP("print \"\n^3Match is ^5UNPAUSED^3 ... resuming in 10 seconds!\n\n\"");
                level.MatchPause = PAUSE_UNPAUSING;
                AudioSystem.PlayGlobalSound("sound/osp/prepare.wav");
            }
        }

        // players
        private static void Cmd_Players(GEntity ent, int idx, bool val)
        {
            var level = ServerGameLogic.Level;
            bool playing = level.GameState == GameConstants.GS_PLAYING;

            string header = playing
                ? "print \"\n^3 ID^1 : ^3Player                    Nudge  Rate  MaxPkts  Snaps\n\""
                : "print \"\n^3Status^1   : ^3ID^1 : ^3Player                    Nudge  Rate  MaxPkts  Snaps\n\"";
            CP(ent, header);

            int cnt = 0;
            for (int i = 0; i < level.NumConnectedClients; i++)
            {
                int num = level.SortedClients[i];
                var cl  = level.Clients[num];
                if (cl == null) continue;

                string name = cl.Pers.NetName;
                if (name.Length > 26) name = name.Substring(0, 26);

                string teamMark = " ";
                if (cl.Sess.SessionTeam == GameConstants.TEAM_AXIS)    teamMark = "^1X^7";
                else if (cl.Sess.SessionTeam == GameConstants.TEAM_ALLIES) teamMark = "^4L^7";

                string readyStr = "";
                if (!playing)
                {
                    if (cl.Sess.SessionTeam == GameConstants.TEAM_SPECTATOR)
                        readyStr = "^5--------^1 :";
                    else
                        readyStr = cl.Pers.Ready ? "^3(READY)^1  :" : "NOTREADY^1 :";
                }

                string refStr = cl.Sess.Referee != 0 ? "REF" : "";
                CP(ent, $"print \"{readyStr}{teamMark}{num,2}: {name,-26} {refStr}\n\"");
                cnt++;
            }
            CP(ent, $"print \"\n^3{cnt,2}^7 total players\n\n\"");
        }

        // ref
        private static void Cmd_Ref(GEntity ent, int idx, bool val)
        {
            string pw = CmdSystem.Cmd_Argv(1);
            string refpw = CvarSystem.GetString("refereePassword");
            if (!string.IsNullOrEmpty(refpw) && pw == refpw)
            {
                RefereeSystem.MakeReferee(ent);
                CP(ent, "print \"^3You are now a referee.\n\"");
            }
            else CP(ent, "print \"Invalid referee password.\n\"");
        }

        // say_teamnl
        private static void Cmd_SayTeamNL(GEntity ent, int idx, bool val)
        {
            // SAY_TEAMNL = team chat without location string
            GameCommands.SayTeamNoLocation(ent);
        }

        // scores
        private static void Cmd_Scores(GEntity ent, int idx, bool val)
        {
            GameMatch.PrintMatchInfo(ent);
        }

        // specinvite
        private static void Cmd_SpecInvite(GEntity ent, int idx, bool val)
        {
            if (CvarSystem.GetInt("team_nocontrols") != 0) { NoTeamControls(ent); return; }
            if (!Debounce(ent, idx)) return;

            int tteam = ent.Client.Sess.SessionTeam;
            if (tteam != GameConstants.TEAM_AXIS && tteam != GameConstants.TEAM_ALLIES)
            { CP(ent, "cpm \"Spectators can't specinvite players!\n\""); return; }

            if (!GameTeam.TeamInfo[tteam].SpecLock)
            { CP(ent, "cpm \"Your team isn't locked from spectators!\n\""); return; }

            string arg = CmdSystem.Cmd_Argv(1);
            if (!int.TryParse(arg, out int pid) || pid < 0 || pid >= GameConstants.MAX_CLIENTS)
            { CP(ent, "print \"Invalid player ID.\n\""); return; }

            var target = ServerGameLogic.Entities[pid];
            if (target == null || target.Client == null) return;
            if (target.Client == ent.Client) { CP(ent, "cpm \"You can't specinvite yourself!\n\""); return; }
            if (target.Client.Sess.SessionTeam != GameConstants.TEAM_SPECTATOR)
            { CP(ent, "cpm \"You can't specinvite a non-spectator!\n\""); return; }

            target.Client.Sess.SpecInvite |= tteam;
            CP(ent, $"print \"{target.Client.Pers.NetName}^7 has been sent a spectator invitation.\n\"");
            CP(target, $"print \"*** You've been invited to spectate the {GameTeam.TeamName(tteam)} team!\n\"");
        }

        // speclock / specunlock
        private static void Cmd_SpecLock(GEntity ent, int idx, bool fLock)
        {
            if (CvarSystem.GetInt("team_nocontrols") != 0) { NoTeamControls(ent); return; }
            if (!Debounce(ent, idx)) return;

            int tteam = ent.Client.Sess.SessionTeam;
            if (tteam != GameConstants.TEAM_AXIS && tteam != GameConstants.TEAM_ALLIES)
            { CP(ent, $"print \"Spectators can't {(fLock ? "lock" : "unlock")} a team from spectators!\n\""); return; }

            var ti = GameTeam.TeamInfo[tteam];
            if (ti.SpecLock == fLock)
                CP(ent, $"print \"\n^3Your team is already {(fLock ? "lock" : "unlock")}ed from spectators!\n\n\"");
            else
            {
                ti.SpecLock = fLock;
                AP($"print \"The {GameTeam.TeamName(tteam)} team is now {(fLock ? "lock" : "unlock")}ed from spectators\n\"");
                if (fLock) CP(ent, "cpm \"Use ^3specinvite^7 to invite people to spectate.\n\"");
            }
        }

        // ready / notready / unready
        private static void Cmd_Ready(GEntity ent, int idx, bool state)
        {
            var level = ServerGameLogic.Level;
            int gs = level.GameState;
            if (gs == GameConstants.GS_PLAYING || gs == GameConstants.GS_INTERMISSION)
            { CP(ent, "cpm \"Match is already in progress!\n\""); return; }

            if (!state && gs == GameConstants.GS_WARMUP_COUNTDOWN)
            { CP(ent, "cpm \"Countdown started.... ^3notready^7 ignored!\n\""); return; }

            if (ent.Client.Sess.SessionTeam == GameConstants.TEAM_SPECTATOR)
            { CP(ent, "cpm \"You must be in the game to be ^3ready^7!\n\""); return; }

            int minPlayers = CvarSystem.GetInt("match_minplayers");
            if (level.NumPlayingClients < minPlayers)
            { CP(ent, "cpm \"Not enough players to start match!\n\""); return; }

            if (!Debounce(ent, idx)) return;

            if (ent.Client.Pers.Ready == state)
                CP(ent, $"print \"You are already{(state ? "" : " NOT")} ready!\n\"");
            else
            {
                ent.Client.Pers.Ready = state;
                if (state) GameMatch.MakeReady(ent);
                else       GameMatch.MakeUnready(ent);
                AP($"print \"{ent.Client.Pers.NetName}^7 is{(state ? "" : " NOT")} ready!\n\"");
                AP($"cp \"\n{ent.Client.Pers.NetName}\n^3is{(state ? "" : " NOT")} ready!\n\"");
            }
            GameMatch.CheckReadyMatchState();
        }

        // readyteam
        private static void Cmd_TeamReady(GEntity ent, int idx, bool state)
        {
            var level = ServerGameLogic.Level;
            int gs = level.GameState;
            if (gs == GameConstants.GS_PLAYING || gs == GameConstants.GS_INTERMISSION)
            { CP(ent, "cpm \"Match is already in progress!\n\""); return; }

            if (ent.Client.Sess.SessionTeam == GameConstants.TEAM_SPECTATOR)
            { CP(ent, "cpm \"Spectators can't ready a team!\n\""); return; }

            int minPlayers = CvarSystem.GetInt("match_minplayers");
            if (level.NumPlayingClients < minPlayers)
            { CP(ent, "cpm \"Not enough players to start match!\n\""); return; }

            if (!Debounce(ent, idx)) return;

            int tteam = ent.Client.Sess.SessionTeam;
            for (int i = 0; i < level.NumPlayingClients; i++)
            {
                var cl = level.Clients[level.SortedClients[i]];
                if (cl != null && cl.Sess.SessionTeam == tteam)
                {
                    cl.Pers.Ready = true;
                    GameMatch.MakeReady(ServerGameLogic.Entities[level.SortedClients[i]]);
                }
            }
            AP($"print \"The {GameTeam.TeamName(tteam)} team is ready!\n\"");
            GameMatch.CheckReadyMatchState();
        }

        // statsall
        private static void Cmd_StatsAll(GEntity ent, int idx, bool val)
        {
            var level = ServerGameLogic.Level;
            for (int i = 0; i < level.NumConnectedClients; i++)
            {
                var cl = level.Clients[level.SortedClients[i]];
                if (cl?.Sess.SessionTeam == GameConstants.TEAM_SPECTATOR) continue;
                var e = ServerGameLogic.Entities[level.SortedClients[i]];
                CP(ent, $"ws {GameServerStats.CreateStats(e)}\n");
            }
        }

        // weaponstats
        private static void Cmd_WeaponStats(GEntity ent, int idx, bool val)
        {
            GameServerStats.PrintStats(ent, 0);
        }

        // topshots / bottomshots
        private static void Cmd_WeaponRankings(GEntity ent, int idx, bool doTop)
        {
            string arg = CmdSystem.Cmd_Argv(1);
            if (string.IsNullOrEmpty(arg))
            {
                SendWeaponLeaders(ent, doTop);
                return;
            }

            int weapIdx = -1;
            if (int.TryParse(arg, out int parsed) && parsed >= 0 && parsed < (int)WeaponStatIndex.MAX)
                weapIdx = parsed;
            else
            {
                for (int w = 0; w < WeaponStatInfo.Table.Length; w++)
                {
                    if (string.Equals(WeaponStatInfo.Table[w].Code, arg, StringComparison.OrdinalIgnoreCase))
                    { weapIdx = w; break; }
                }
            }

            if (weapIdx < 0)
            {
                ShowHelp(ent, doTop ? "topshots" : "bottomshots", idx);
                string z = "^3Available weapon codes:^7\n";
                for (int w = 0; w < WeaponStatInfo.Table.Length; w++)
                    z += $"  {WeaponStatInfo.Table[w].Code} - {WeaponStatInfo.Table[w].Name}\n";
                CP(ent, $"print \"{z}\"");
                return;
            }

            SendWeaponRankingForWeapon(ent, weapIdx, doTop);
        }

        // Sends best/worst accuracy ranking for a single weapon
        private static void SendWeaponRankingForWeapon(GEntity ent, int weapIdx, bool doTop)
        {
            var level = ServerGameLogic.Level;
            int qual  = WeaponStatInfo.QualifyingShots[weapIdx];
            int bestAcc = doTop ? 0 : 99999;
            var results = new List<(int clientNum, int hits, int atts, int kills, int deaths, float acc)>();

            for (int i = 0; i < level.NumConnectedClients; i++)
            {
                int num = level.SortedClients[i];
                var cl  = level.Clients[num];
                if (cl == null || cl.Sess.SessionTeam == GameConstants.TEAM_SPECTATOR) continue;

                var ws = cl.Sess.WeaponStats;
                if (ws == null || weapIdx >= ws.Length) continue;
                if (ws[weapIdx].Atts < qual) continue;

                float acc = ws[weapIdx].Atts > 0
                    ? (ws[weapIdx].Hits * 100f) / ws[weapIdx].Atts : 0f;
                results.Add((num, ws[weapIdx].Hits, ws[weapIdx].Atts,
                             ws[weapIdx].Kills, ws[weapIdx].Deaths, acc));
                if (doTop ? acc > bestAcc : acc < bestAcc) bestAcc = (int)acc;
            }

            string z = "";
            foreach (var r in results)
                z += $" {r.clientNum} {r.hits} {r.atts} {r.kills} {r.deaths}";

            CP(ent, $"astats{(doTop ? "" : "b")} {results.Count} {weapIdx} {bestAcc}{z}");
        }

        // Sends best/worst accuracy leaders across all weapons
        private static void SendWeaponLeaders(GEntity ent, bool doTop)
        {
            var level = ServerGameLogic.Level;
            string z = "";

            for (int w = 0; w < (int)WeaponStatIndex.MAX; w++)
            {
                int qual = WeaponStatInfo.QualifyingShots[w];
                int bestAcc = doTop ? 0 : 99999;
                int places = 0;
                var cands = new List<(int num, int hits, int atts, int kills, int deaths, float acc)>();

                for (int i = 0; i < level.NumConnectedClients; i++)
                {
                    int num = level.SortedClients[i];
                    var cl  = level.Clients[num];
                    if (cl == null || cl.Sess.SessionTeam == GameConstants.TEAM_SPECTATOR) continue;
                    var ws = cl.Sess.WeaponStats;
                    if (ws == null || w >= ws.Length || ws[w].Atts < qual) continue;

                    float acc = ws[w].Atts > 0 ? (ws[w].Hits * 100f) / ws[w].Atts : 0f;
                    cands.Add((num, ws[w].Hits, ws[w].Atts, ws[w].Kills, ws[w].Deaths, acc));
                    if (doTop ? acc > bestAcc : acc < bestAcc) { bestAcc = (int)acc; places++; }
                }

                if (!doTop && places < 2) continue;

                foreach (var c in cands)
                {
                    if (doTop ? c.acc >= bestAcc : c.acc <= bestAcc + 0.999f)
                        z += $" {w + 1} {c.num} {c.hits} {c.atts} {c.kills} {c.deaths}";
                }
            }
            CP(ent, $"bstats{(doTop ? "" : "b")} {z} 0");
        }
    }
}
