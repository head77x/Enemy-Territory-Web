// Ported from: src/cgame/cg_scoreboard.c, src/cgame/cg_limbopanel.c,
//              src/cgame/cg_debriefing.c, src/cgame/cg_popupmessages.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    // =========================================================================
    // Scoreboard
    // =========================================================================

    public class ScoreboardEntry
    {
        public int    ClientNum;
        public string Name;
        public int    Score;
        public int    Kills;
        public int    Deaths;
        public int    Team;
        public int    Class;
        public int    Ping;
        public int    Time;
        public bool   IsBot;
        public bool   IsLocalClient;
        public int    XP;
    }

    public static class CGameScoreboard
    {
        private static List<ScoreboardEntry> _scores = new List<ScoreboardEntry>();
        private static bool _scoresVisible = false;

        public static event Action<List<ScoreboardEntry>, int, int> OnScoreboardDraw;
        public static event Action OnScoreboardHide;

        public static void CG_DrawScoreboard()
        {
            if (!_scoresVisible)
                return;

            OnScoreboardDraw?.Invoke(_scores, CGameMain.CGS.Scores1, CGameMain.CGS.Scores2);
        }

        public static void CG_ParseScores(string[] args)
        {
            _scores.Clear();

            // args[0] = "scores", args[1] = count, then per-client blocks
            if (args == null || args.Length < 2)
                return;

            if (!int.TryParse(args[1], out int count))
                return;

            int base_ = 2;
            // Each entry: clientNum, score, kills, deaths, team, class, ping, time, xp, isBot
            const int FIELDS = 10;
            for (int i = 0; i < count && base_ + FIELDS <= args.Length; i++, base_ += FIELDS)
            {
                int clientNum = ParseInt(args, base_ + 0);
                var entry = new ScoreboardEntry
                {
                    ClientNum    = clientNum,
                    Score        = ParseInt(args, base_ + 1),
                    Kills        = ParseInt(args, base_ + 2),
                    Deaths       = ParseInt(args, base_ + 3),
                    Team         = ParseInt(args, base_ + 4),
                    Class        = ParseInt(args, base_ + 5),
                    Ping         = ParseInt(args, base_ + 6),
                    Time         = ParseInt(args, base_ + 7),
                    XP           = ParseInt(args, base_ + 8),
                    IsBot        = ParseInt(args, base_ + 9) != 0,
                    IsLocalClient = clientNum == CGameMain.CG.ClientNum,
                    Name         = ClientCGame.CL_GetConfigString(32 + clientNum),
                };
                _scores.Add(entry);
            }

            // Sort: team ascending, then score descending
            _scores.Sort((a, b) =>
            {
                int teamCmp = a.Team.CompareTo(b.Team);
                return teamCmp != 0 ? teamCmp : b.Score.CompareTo(a.Score);
            });
        }

        public static void CG_ShowScoreboard()
        {
            _scoresVisible = true;
        }

        public static void CG_HideScoreboard()
        {
            _scoresVisible = false;
            OnScoreboardHide?.Invoke();
        }

        public static void CG_ToggleScoreboard()
        {
            if (_scoresVisible)
                CG_HideScoreboard();
            else
                CG_ShowScoreboard();
        }

        private static int ParseInt(string[] args, int idx)
        {
            if (idx < 0 || idx >= args.Length) return 0;
            int.TryParse(args[idx], out int v);
            return v;
        }
    }

    // =========================================================================
    // Limbo panel
    // =========================================================================

    public class LimboState
    {
        public bool   Active;
        public int    SelectedTeam;
        public int    SelectedClass;
        public int[]  SelectedWeapons = new int[3];
        public int    SpawnTimer;
        public bool   IsRevivable;
        public int    ReviveTimer;
    }

    public static class CGameLimboPanel
    {
        private const int TEAM_AXIS   = 1;
        private const int TEAM_ALLIES = 2;

        // Weapons available per class (team, class) — mirrors bg_classes weapon loadouts.
        // Index 0 = primary, 1 = secondary, 2+ = extra choices.
        private static readonly int[][] s_axisClassWeapons = new int[(int)PlayerClass.Count][]
        {
            /* Soldier   */ new[] { 13, 14, 15, 16 }, // MP40, MG42, Mortar, Panzerfaust
            /* Medic     */ new[] { 13, 0 },           // MP40
            /* Engineer  */ new[] { 13, 21, 22 },      // MP40, Rifle, Dynamite
            /* FieldOps  */ new[] { 13, 0 },
            /* CovertOps */ new[] { 13, 23, 24 },
        };

        private static readonly int[][] s_alliesClassWeapons = new int[(int)PlayerClass.Count][]
        {
            /* Soldier   */ new[] { 19, 20, 15, 16 }, // Thompson, BAR, Mortar, Bazooka
            /* Medic     */ new[] { 19, 0 },
            /* Engineer  */ new[] { 19, 21, 22 },
            /* FieldOps  */ new[] { 19, 0 },
            /* CovertOps */ new[] { 19, 23, 24 },
        };

        public static LimboState State = new LimboState();

        public static event Action<LimboState>     OnLimboPanelDraw;
        public static event Action                 OnLimboPanelOpen;
        public static event Action                 OnLimboPanelClose;
        public static event Action<int, int, int[]> OnSpawnSelectionConfirmed;

        public static void CG_LimboPanelOpen()
        {
            State.Active = true;
            // Mirror current predicted player state so the panel starts on existing values.
            var ps = CGameMain.CG.PredictedPlayerState;
            State.SelectedTeam  = ps.Persistant[3]; // PERS_TEAM
            State.SelectedClass = ps.Persistant[4]; // PERS_CLASS
            OnLimboPanelOpen?.Invoke();
        }

        public static void CG_LimboPanelClose()
        {
            State.Active = false;
            OnLimboPanelClose?.Invoke();
        }

        public static void CG_LimboPanelSelectTeam(int team)
        {
            State.SelectedTeam = team;
            // Reset class/weapons to defaults for the new team.
            State.SelectedClass = (int)PlayerClass.Soldier;
            var weapons = CG_GetClassWeapons(team, State.SelectedClass);
            State.SelectedWeapons[0] = weapons.Length > 0 ? weapons[0] : 0;
            State.SelectedWeapons[1] = 0;
            State.SelectedWeapons[2] = 0;
        }

        public static void CG_LimboPanelSelectClass(int cls)
        {
            State.SelectedClass = cls;
            var weapons = CG_GetClassWeapons(State.SelectedTeam, cls);
            State.SelectedWeapons[0] = weapons.Length > 0 ? weapons[0] : 0;
            State.SelectedWeapons[1] = 0;
            State.SelectedWeapons[2] = 0;
        }

        public static void CG_LimboPanelSelectWeapon(int slot, int weaponNum)
        {
            if (slot < 0 || slot >= 3) return;
            State.SelectedWeapons[slot] = weaponNum;
        }

        public static void CG_LimboPanelConfirm()
        {
            string teamName  = State.SelectedTeam == TEAM_AXIS ? "axis" : "allies";
            string className = ((PlayerClass)State.SelectedClass).ToString().ToLowerInvariant();

            CmdSystem.Cbuf_AddText($"team {teamName}");
            CmdSystem.Cbuf_AddText($"class {className}");

            OnSpawnSelectionConfirmed?.Invoke(
                State.SelectedTeam,
                State.SelectedClass,
                (int[])State.SelectedWeapons.Clone());

            CG_LimboPanelClose();
        }

        public static void CG_DrawLimboPanel()
        {
            if (!State.Active)
                return;

            OnLimboPanelDraw?.Invoke(State);
        }

        public static void CG_UpdateLimboTimer(int serverTime)
        {
            // SpawnTimer counts down from the next spawn wave to zero.
            if (State.SpawnTimer > 0)
                State.SpawnTimer = Mathf.Max(0, State.SpawnTimer - (serverTime - CGameMain.CG.OldTime));

            if (State.IsRevivable && State.ReviveTimer > 0)
                State.ReviveTimer = Mathf.Max(0, State.ReviveTimer - (serverTime - CGameMain.CG.OldTime));
        }

        public static int[] CG_GetClassWeapons(int team, int cls)
        {
            int idx = Mathf.Clamp(cls, 0, (int)PlayerClass.Count - 1);
            if (team == TEAM_AXIS)
                return s_axisClassWeapons[idx];
            return s_alliesClassWeapons[idx];
        }
    }

    // =========================================================================
    // Post-game debriefing
    // =========================================================================

    public class DebriefingStats
    {
        public string   PlayerName;
        public int      ClientNum;
        public int      Kills, Deaths, Revives;
        public int      Constructions, Destructions;
        public int      XPTotal;
        public int[]    SkillXP    = new int[(int)SkillType.Count];
        public int[]    SkillLevel = new int[(int)SkillType.Count];
        public int      MedalsEarned;
        public string[] MedalNames;
        public int      WinningTeam;
        public bool     CampaignComplete;
        public string   NextMapName;
    }

    public static class CGameDebriefing
    {
        private static DebriefingStats _stats   = new DebriefingStats();
        private static bool            _visible = false;

        public static event Action<DebriefingStats> OnDebriefingDraw;
        public static event Action                  OnDebriefingHide;

        public static void CG_ShowDebriefing()
        {
            _visible = true;
        }

        public static void CG_HideDebriefing()
        {
            _visible = false;
            OnDebriefingHide?.Invoke();
        }

        public static void CG_ParseDebriefing(string[] args)
        {
            // Protocol: args[0]="debriefing", then key=value pairs.
            // Matches G_SendDebriefing on the server side.
            if (args == null || args.Length < 2)
                return;

            _stats = new DebriefingStats();

            for (int i = 1; i + 1 < args.Length; i += 2)
            {
                string key = args[i];
                string val = args[i + 1];

                switch (key)
                {
                    case "name":         _stats.PlayerName       = val;              break;
                    case "client":       _stats.ClientNum        = ParseInt(val);    break;
                    case "kills":        _stats.Kills            = ParseInt(val);    break;
                    case "deaths":       _stats.Deaths           = ParseInt(val);    break;
                    case "revives":      _stats.Revives          = ParseInt(val);    break;
                    case "constructs":   _stats.Constructions    = ParseInt(val);    break;
                    case "destructs":    _stats.Destructions     = ParseInt(val);    break;
                    case "xp":           _stats.XPTotal          = ParseInt(val);    break;
                    case "medals":       _stats.MedalsEarned     = ParseInt(val);    break;
                    case "medalnames":   _stats.MedalNames       = val.Split(',');   break;
                    case "winner":       _stats.WinningTeam      = ParseInt(val);    break;
                    case "campaign":     _stats.CampaignComplete = val == "1";       break;
                    case "nextmap":      _stats.NextMapName      = val;              break;
                    default:
                        // skillxp0..skillxp6 and skilllevel0..skilllevel6
                        if (key.StartsWith("skillxp") && key.Length == 8)
                        {
                            int idx = key[7] - '0';
                            if (idx >= 0 && idx < (int)SkillType.Count)
                                _stats.SkillXP[idx] = ParseInt(val);
                        }
                        else if (key.StartsWith("skilllevel") && key.Length == 11)
                        {
                            int idx = key[10] - '0';
                            if (idx >= 0 && idx < (int)SkillType.Count)
                                _stats.SkillLevel[idx] = ParseInt(val);
                        }
                        break;
                }
            }
        }

        public static void CG_DrawDebriefing()
        {
            if (!_visible)
                return;

            OnDebriefingDraw?.Invoke(_stats);
        }

        public static bool CG_DebriefingActive() => _visible;

        private static int ParseInt(string s)
        {
            int.TryParse(s, out int v);
            return v;
        }
    }

    // =========================================================================
    // Popup messages
    // =========================================================================

    public enum PopupType { Kill = 0, Death = 1, Objective = 2, XP = 3, Info = 4 }

    public class PopupMessage
    {
        public string    Text;
        public PopupType Type;
        public float     AddTime;
        public float     Duration;
        public Color     Color;
        public int       Icon;
    }

    public static class CGamePopupMessages
    {
        private const int MAX_POPUP_MESSAGES = 10;

        private static PopupMessage[] _messages    = new PopupMessage[MAX_POPUP_MESSAGES];
        private static int            _messageHead = 0;

        public static event Action<PopupMessage[]> OnPopupMessagesDraw;

        public static void CG_AddPopupMessage(string text, PopupType type,
            float duration = 2.5f, Color? color = null, int icon = -1)
        {
            var msg = new PopupMessage
            {
                Text     = text,
                Type     = type,
                AddTime  = Time.time,
                Duration = duration,
                Color    = color ?? Color.white,
                Icon     = icon,
            };

            _messages[_messageHead % MAX_POPUP_MESSAGES] = msg;
            _messageHead++;
        }

        public static void CG_DrawPopupMessages()
        {
            // Expire stale entries so subscribers don't need to track lifetime.
            float now = Time.time;
            for (int i = 0; i < MAX_POPUP_MESSAGES; i++)
            {
                var m = _messages[i];
                if (m != null && now - m.AddTime >= m.Duration)
                    _messages[i] = null;
            }

            OnPopupMessagesDraw?.Invoke(_messages);
        }

        public static void CG_ClearPopupMessages()
        {
            for (int i = 0; i < MAX_POPUP_MESSAGES; i++)
                _messages[i] = null;
            _messageHead = 0;
        }

        public static void CG_PopupKillMessage(string killerName, string victimName, int weaponNum)
        {
            string weaponName = GetWeaponName(weaponNum);
            CG_AddPopupMessage($"{killerName} killed {victimName} ({weaponName})",
                PopupType.Kill, 3f, Color.red, weaponNum);
        }

        // Minimal weapon-name table; kept local so this file compiles standalone.
        // Entries match ET weapon indices from bg_public.h WP_* constants.
        private static readonly string[] s_weaponNames =
        {
            "none",         // 0
            "knife",        // 1
            "luger",        // 2
            "colt",         // 3
            "mp40",         // 4
            "thompson",     // 5
            "sten",         // 6
            "fg42",         // 7
            "panzerfaust",  // 8
            "flamethrower", // 9
            "grenade",      // 10
            "mortar",       // 11
            "dynamite",     // 12
            "mp40",         // 13
            "kar98",        // 14
            "mortar",       // 15
            "panzerfaust",  // 16
            "smoke",        // 17
            "syringe",      // 18
            "thompson",     // 19
            "bar",          // 20
            "rifle",        // 21
            "dynamite",     // 22
            "sniperscope",  // 23
            "satchel",      // 24
        };

        private static string GetWeaponName(int num)
        {
            if (num >= 0 && num < s_weaponNames.Length)
                return s_weaponNames[num];
            return $"weapon{num}";
        }

        public static void CG_PopupObjectiveMessage(string text, int team)
        {
            // Team 1 = Axis (gold), Team 2 = Allies (blue), 0 = neutral (white)
            Color c = team == 1 ? new Color(1f, 0.85f, 0.1f)
                    : team == 2 ? new Color(0.3f, 0.6f, 1f)
                    : Color.white;
            CG_AddPopupMessage(text, PopupType.Objective, 4f, c);
        }

        public static void CG_PopupXPMessage(int xpAmount, string skillName)
        {
            CG_AddPopupMessage($"+{xpAmount} XP ({skillName})",
                PopupType.XP, 2.5f, new Color(0.4f, 1f, 0.4f));
        }
    }
}
