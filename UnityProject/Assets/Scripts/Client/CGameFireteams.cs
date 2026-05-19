using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    // =========================================================================
    // CGFireTeam — client-side view of a single fireteam slot
    // =========================================================================
    public class CGFireTeam
    {
        public int    Num;
        public int    Team;
        public string Name    = "";
        public int[]  Members = new int[6];
        public int    Leader;
        public bool   Active;
        public bool   Private_;

        public CGFireTeam()
        {
            for (int i = 0; i < Members.Length; i++)
                Members[i] = -1;
        }
    }

    // =========================================================================
    // CGameFireteams
    // =========================================================================
    public static class CGameFireteams
    {
        // ET uses configstrings 760–767 for the 8 fireteam slots
        private const int CS_FIRETEAMS       = 760;
        private const int MAX_FIRETEAMS     = 8;
        public  const int MAX_FT_MEMBERS    = 6;

        public static CGFireTeam[] FireTeams = new CGFireTeam[MAX_FIRETEAMS];

        public static event Action<int> OnFireteamJoined;
        public static event Action      OnFireteamLeft;

        static CGameFireteams()
        {
            for (int i = 0; i < MAX_FIRETEAMS; i++)
                FireTeams[i] = new CGFireTeam { Num = i };
        }

        // CG_ParseFireteams — rebuild FireTeams[] from configstrings.
        // CS format: \name\Alpha\leader\3\members\1 2 5 -1 -1 -1\team\1\priv\0
        public static void CG_ParseFireteams()
        {
            for (int i = 0; i < MAX_FIRETEAMS; i++)
            {
                string cs = CGameMain.CGS.GameState[CS_FIRETEAMS + i];
                CGFireTeam ft = FireTeams[i];
                ft.Num = i;

                if (string.IsNullOrEmpty(cs))
                {
                    ft.Active = false;
                    ft.Name   = "";
                    for (int m = 0; m < MAX_FT_MEMBERS; m++) ft.Members[m] = -1;
                    continue;
                }

                ft.Active   = true;
                ft.Name     = InfoKey(cs, "name");
                ft.Leader   = InfoKeyInt(cs, "leader");
                ft.Team     = InfoKeyInt(cs, "team");
                ft.Private_ = InfoKeyInt(cs, "priv") != 0;

                string membersStr = InfoKey(cs, "members");
                string[] parts    = membersStr.Split(' ');
                for (int m = 0; m < MAX_FT_MEMBERS; m++)
                    ft.Members[m] = (m < parts.Length && int.TryParse(parts[m], out int v)) ? v : -1;
            }
        }

        public static CGFireTeam CG_FireteamForClient(int clientNum)
        {
            for (int i = 0; i < MAX_FIRETEAMS; i++)
            {
                CGFireTeam ft = FireTeams[i];
                if (!ft.Active) continue;
                foreach (int m in ft.Members)
                    if (m == clientNum) return ft;
            }
            return null;
        }

        public static bool CG_IsOnSameFireteam(int targetClientNum)
        {
            CGFireTeam local  = CG_FireteamForClient(CGameMain.CG.ClientNum);
            CGFireTeam target = CG_FireteamForClient(targetClientNum);
            return local != null && local == target;
        }

        // A fireteam "has a human" when at least one member slot is in-use and
        // the corresponding clientInfo entry is populated (non-zero means connected).
        public static bool CG_FireteamHasHuman(CGFireTeam ft)
        {
            if (ft == null || !ft.Active) return false;
            foreach (int m in ft.Members)
                if (m >= 0 && m < GameConst.MAX_CLIENTS && CGameMain.CGS.ClientInfo[m] != 0)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Command handlers — send server commands via the command buffer
        // ------------------------------------------------------------------

        public static void CG_CreateFireteam_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("vsay create_fireteam\n");
        }

        public static void CG_DisbandFireteam_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("vsay disband_fireteam\n");
        }

        public static void CG_JoinFireteam_f(string[] args)
        {
            if (args.Length < 2) return;
            CmdSystem.Cbuf_AddText($"vsay join_fireteam {args[1]}\n");
        }

        public static void CG_LeaveFireteam_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("vsay leave_fireteam\n");
            OnFireteamLeft?.Invoke();
        }

        public static void CG_InviteFireteam_f(string[] args)
        {
            if (args.Length < 2) return;
            CmdSystem.Cbuf_AddText($"vsay invite_fireteam {args[1]}\n");
        }

        // Called from CGameMain when a fireteam configstring changes so the
        // overlay and any listeners stay in sync.
        public static void CG_FireteamConfigstringChanged(int csIndex)
        {
            if (csIndex < CS_FIRETEAMS || csIndex >= CS_FIRETEAMS + MAX_FIRETEAMS)
                return;

            CG_ParseFireteams();

            int localClient = CGameMain.CG.ClientNum;
            CGFireTeam ft   = CG_FireteamForClient(localClient);
            if (ft != null)
                OnFireteamJoined?.Invoke(ft.Num);
        }

        // ------------------------------------------------------------------
        // Info-string helpers (local copy — avoids exposing private CGameMain methods)
        // ------------------------------------------------------------------

        private static string InfoKey(string info, string key)
        {
            if (string.IsNullOrEmpty(info) || string.IsNullOrEmpty(key))
                return "";
            string search = "\\" + key + "\\";
            int pos = info.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) return "";
            int start = pos + search.Length;
            int end   = info.IndexOf('\\', start);
            return end < 0 ? info.Substring(start) : info.Substring(start, end - start);
        }

        private static int InfoKeyInt(string info, string key)
        {
            string val = InfoKey(info, key);
            return int.TryParse(val, out int i) ? i : 0;
        }
    }

    // =========================================================================
    // CGameFireteamOverlay
    // =========================================================================
    public static class CGameFireteamOverlay
    {
        public struct FireteamMemberData
        {
            public int     ClientNum;
            public string  Name;
            public float   HealthFraction;
            public int     Class;
            public bool    IsDead;
            public bool    IsRevivable;
            public Vector2 ScreenPos;
        }

        public static event Action<FireteamMemberData[]> OnFireteamOverlayUpdate;

        // Called from CGameView.CG_Draw2D each frame.
        public static void CG_DrawFireteamOverlay()
        {
            FireteamMemberData[] data = CG_BuildFireteamOverlayData();
            if (data.Length > 0)
                OnFireteamOverlayUpdate?.Invoke(data);
        }

        public static FireteamMemberData[] CG_BuildFireteamOverlayData()
        {
            int localClient = CGameMain.CG.ClientNum;
            CGFireTeam ft   = CGameFireteams.CG_FireteamForClient(localClient);
            if (ft == null || !ft.Active)
                return Array.Empty<FireteamMemberData>();

            var snap = CGameMain.CG.Snap;
            if (snap == null)
                return Array.Empty<FireteamMemberData>();

            var result = new List<FireteamMemberData>(CGameFireteams.MAX_FT_MEMBERS);

            foreach (int memberNum in ft.Members)
            {
                if (memberNum < 0 || memberNum >= GameConst.MAX_CLIENTS)
                    continue;
                if (CGameMain.CGS.ClientInfo[memberNum] == 0)
                    continue;

                // Pull the player state for this member from snapshot entities.
                // For the local client we use PredictedPlayerState directly.
                PlayerState ps = (memberNum == localClient)
                    ? CGameMain.CG.PredictedPlayerState
                    : FindPlayerStateInSnap(snap, memberNum);

                if (ps == null) continue;

                short hp    = ps.Stats[GameConst.STAT_HEALTH];
                int   cls   = ps.Stats[GameConst.STAT_PLAYER_CLASS];
                bool  dead  = hp <= 0;

                // Revivable: medic can revive when health is in the corpse range (-40..0).
                bool revivable = hp < 0 && hp >= -40;

                // Clamp to valid max-health reference (100 as default).
                float maxHp = 100f;
                float frac  = dead ? 0f : Mathf.Clamp01(hp / maxHp);

                result.Add(new FireteamMemberData
                {
                    ClientNum      = memberNum,
                    Name           = $"Client{memberNum}",   // replaced by client-info parse when available
                    HealthFraction = frac,
                    Class          = cls,
                    IsDead         = dead,
                    IsRevivable    = revivable,
                    ScreenPos      = Vector2.zero,           // UI layer sets actual position
                });
            }

            return result.ToArray();
        }

        private static PlayerState FindPlayerStateInSnap(ClientSnapshot snap, int clientNum)
        {
            // Snapshot only carries the local PS; for other players we return null —
            // the overlay will show last-known data until the next full update.
            return null;
        }
    }

    // =========================================================================
    // CGameCommandMap
    // =========================================================================
    public class CommandMapIcon
    {
        public Vector2 MapPos;
        public int     IconType;
        public string  Label   = "";
        public bool    Visible;
        public int     Team;
    }

    public static class CGameCommandMap
    {
        public const int CM_ICON_PLAYER    = 0;
        public const int CM_ICON_OBJECTIVE = 1;
        public const int CM_ICON_SPAWN     = 2;
        public const int CM_ICON_AMMO      = 3;
        public const int CM_ICON_HEALTH    = 4;
        public const int CM_ICON_COMPASS   = 5;

        private static List<CommandMapIcon> _icons      = new List<CommandMapIcon>();
        private static Texture2D            _mapTexture;
        private static Vector2              _mapMins;
        private static Vector2              _mapMaxs;

        // _entityIcons[entityNum] — keeps dynamic icons addressable by entity so
        // CG_AddCommandMapEntity can update in-place rather than accumulate duplicates.
        private const  int              MAX_CM_ENTITIES = GameConst.MAX_CLIENTS * 4;
        private static CommandMapIcon[] _entityIcons    = new CommandMapIcon[MAX_CM_ENTITIES];

        public static event Action<List<CommandMapIcon>, Texture2D> OnCommandMapDraw;

        public static void CG_InitCommandMap()
        {
            _icons.Clear();
            for (int i = 0; i < _entityIcons.Length; i++) _entityIcons[i] = null;

            // Read map coordinate bounds from CS_WOLFINFO (index 2).
            string wolfInfo = CGameMain.CGS.GameState[2];
            _mapMins = ParseVec2(wolfInfo, "mapcoordsmins");
            _mapMaxs = ParseVec2(wolfInfo, "mapcoordsmaxs");

            // Guard against degenerate bounds.
            if (_mapMaxs.x <= _mapMins.x) { _mapMins.x = -4096f; _mapMaxs.x = 4096f; }
            if (_mapMaxs.y <= _mapMins.y) { _mapMins.y = -4096f; _mapMaxs.y = 4096f; }

            string mapBase  = CGameMain.CGS.MapNameClean;
            string texPath  = "maps/commandmap/" + mapBase + "_cc";
            _mapTexture     = Resources.Load<Texture2D>(texPath);
        }

        public static void CG_UpdateCommandMap()
        {
            var snap = CGameMain.CG.Snap;
            if (snap == null) return;

            // Refresh the local-player icon every frame so it tracks movement.
            int localNum = CGameMain.CG.ClientNum;
            Vector3 origin = new Vector3(
                CGameMain.CG.PredictedPlayerState.Origin0,
                CGameMain.CG.PredictedPlayerState.Origin1,
                CGameMain.CG.PredictedPlayerState.Origin2);

            // ET→Unity axis conversion (as per project convention): new Vector3(-et.y, et.z, et.x)
            // For a 2D map we only need the horizontal ET plane (x,y).
            Vector3 unityOrigin = new Vector3(-origin.y, origin.z, origin.x);
            Vector3 etWorld     = new Vector3(origin.x, origin.y, origin.z);

            CG_AddCommandMapEntity(localNum, CM_ICON_PLAYER, etWorld,
                CGameMain.CG.PredictedPlayerState.TeamNum);
        }

        public static void CG_DrawCommandMap(bool isFullscreen)
        {
            CG_UpdateCommandMap();
            OnCommandMapDraw?.Invoke(_icons, _mapTexture);
        }

        // Convert ET world coords to normalised UV (0..1) on the command map.
        public static Vector2 CG_WorldToMapCoords(Vector3 worldPos)
        {
            float rangeX = _mapMaxs.x - _mapMins.x;
            float rangeY = _mapMaxs.y - _mapMins.y;
            if (rangeX == 0f) rangeX = 1f;
            if (rangeY == 0f) rangeY = 1f;

            float u = (worldPos.x - _mapMins.x) / rangeX;
            float v = (worldPos.y - _mapMins.y) / rangeY;
            return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
        }

        public static void CG_AddCommandMapEntity(int entityNum, int iconType, Vector3 worldPos, int team)
        {
            int slot = Mathf.Clamp(entityNum, 0, _entityIcons.Length - 1);

            CommandMapIcon icon = _entityIcons[slot];
            if (icon == null)
            {
                icon             = new CommandMapIcon();
                _entityIcons[slot] = icon;
                _icons.Add(icon);
            }

            icon.MapPos   = CG_WorldToMapCoords(worldPos);
            icon.IconType = iconType;
            icon.Team     = team;
            icon.Visible  = true;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Vector2 ParseVec2(string info, string key)
        {
            string val = InfoKey(info, key);
            if (string.IsNullOrEmpty(val)) return Vector2.zero;
            string[] parts = val.Split(' ');
            float x = parts.Length > 0 && float.TryParse(parts[0], out float px) ? px : 0f;
            float y = parts.Length > 1 && float.TryParse(parts[1], out float py) ? py : 0f;
            return new Vector2(x, y);
        }

        private static string InfoKey(string info, string key)
        {
            if (string.IsNullOrEmpty(info) || string.IsNullOrEmpty(key)) return "";
            string search = "\\" + key + "\\";
            int pos = info.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) return "";
            int start = pos + search.Length;
            int end   = info.IndexOf('\\', start);
            return end < 0 ? info.Substring(start) : info.Substring(start, end - start);
        }
    }

    // =========================================================================
    // CGameConsoleCommands
    // =========================================================================
    public static class CGameConsoleCommands
    {
        public static void CG_InitConsoleCommands()
        {
            CmdSystem.Cmd_AddCommand("+zoom",        (a) => { CGameMain.CG.Zoomed = true;  CGameMain.CG.ZoomTime = CGameMain.CG.Time; });
            CmdSystem.Cmd_AddCommand("-zoom",        (a) => { CGameMain.CG.Zoomed = false; CGameMain.CG.ZoomTime = CGameMain.CG.Time; });

            CmdSystem.Cmd_AddCommand("weapnext",     CG_NextWeapon_f);
            CmdSystem.Cmd_AddCommand("weapprev",     CG_PrevWeapon_f);
            CmdSystem.Cmd_AddCommand("weapon",       CG_Weapon_f);

            CmdSystem.Cmd_AddCommand("+scores",      (a) => CmdSystem.Cbuf_AddText("showscores 1\n"));
            CmdSystem.Cmd_AddCommand("-scores",      (a) => CmdSystem.Cbuf_AddText("showscores 0\n"));

            CmdSystem.Cmd_AddCommand("sizeup",       CG_SizeUp_f);
            CmdSystem.Cmd_AddCommand("sizedown",     CG_SizeDown_f);

            CmdSystem.Cmd_AddCommand("say_team",     CG_TeamMessage_f);
            CmdSystem.Cmd_AddCommand("vsay",         CG_VoiceChat_f);
            CmdSystem.Cmd_AddCommand("vsay_team",    CG_VoiceChatTeam_f);

            CmdSystem.Cmd_AddCommand("reload",       CG_Reload_f);
            CmdSystem.Cmd_AddCommand("prevmenu",     CG_PrevMenu_f);
            CmdSystem.Cmd_AddCommand("nextmenu",     CG_NextMenu_f);

            CmdSystem.Cmd_AddCommand("create_ft",    CGameFireteams.CG_CreateFireteam_f);
            CmdSystem.Cmd_AddCommand("disband_ft",   CGameFireteams.CG_DisbandFireteam_f);
            CmdSystem.Cmd_AddCommand("join_ft",      CGameFireteams.CG_JoinFireteam_f);
            CmdSystem.Cmd_AddCommand("leave_ft",     CGameFireteams.CG_LeaveFireteam_f);
            CmdSystem.Cmd_AddCommand("invite_ft",    CGameFireteams.CG_InviteFireteam_f);

            CmdSystem.Cmd_AddCommand("commandmap",   (a) => CGameCommandMap.CG_DrawCommandMap(isFullscreen: false));
        }

        // +sizeup / +sizedown adjust the view-size cvar by ±10 (ET used cg_viewsize).
        private static void CG_SizeUp_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("set cg_viewsize 110\n");
        }

        private static void CG_SizeDown_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("set cg_viewsize 90\n");
        }

        private static void CG_NextWeapon_f(string[] args)
        {
            CGameWeapons.CG_NextWeapon();
        }

        private static void CG_PrevWeapon_f(string[] args)
        {
            CGameWeapons.CG_PrevWeapon();
        }

        private static void CG_Weapon_f(string[] args)
        {
            CGameWeapons.CG_Weapon_f(args);
        }

        private static void CG_Scores_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("showscores 1\n");
        }

        private static void CG_TeamMessage_f(string[] args)
        {
            if (args.Length < 2) return;
            string msg = string.Join(" ", args, 1, args.Length - 1);
            CmdSystem.Cbuf_AddText($"say_team \"{msg}\"\n");
        }

        private static void CG_VoiceChat_f(string[] args)
        {
            if (args.Length < 2) return;
            CmdSystem.Cbuf_AddText($"vsay {args[1]}\n");
        }

        private static void CG_VoiceChatTeam_f(string[] args)
        {
            if (args.Length < 2) return;
            CmdSystem.Cbuf_AddText($"vsay_team {args[1]}\n");
        }

        private static void CG_ZoomIn_f(string[] args)
        {
            CGameMain.CG.Zoomed    = true;
            CGameMain.CG.ZoomTime  = CGameMain.CG.Time;
        }

        private static void CG_ZoomOut_f(string[] args)
        {
            CGameMain.CG.Zoomed    = false;
            CGameMain.CG.ZoomTime  = CGameMain.CG.Time;
        }

        private static void CG_Reload_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("reload\n");
        }

        private static void CG_PrevMenu_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("prevmenu\n");
        }

        private static void CG_NextMenu_f(string[] args)
        {
            CmdSystem.Cbuf_AddText("nextmenu\n");
        }

        // Fallback dispatcher — returns true if the command was consumed here.
        public static bool CG_ConsoleCommand(string[] args)
        {
            if (args == null || args.Length == 0) return false;

            switch (args[0].ToLowerInvariant())
            {
                case "sizeup":      CG_SizeUp_f(args);        return true;
                case "sizedown":    CG_SizeDown_f(args);       return true;
                case "weapnext":    CG_NextWeapon_f(args);     return true;
                case "weapprev":    CG_PrevWeapon_f(args);     return true;
                case "weapon":      CG_Weapon_f(args);         return true;
                case "scores":      CG_Scores_f(args);         return true;
                case "say_team":    CG_TeamMessage_f(args);    return true;
                case "vsay":        CG_VoiceChat_f(args);      return true;
                case "vsay_team":   CG_VoiceChatTeam_f(args);  return true;
                case "reload":      CG_Reload_f(args);         return true;
                case "prevmenu":    CG_PrevMenu_f(args);       return true;
                case "nextmenu":    CG_NextMenu_f(args);       return true;
                case "create_ft":   CGameFireteams.CG_CreateFireteam_f(args);  return true;
                case "disband_ft":  CGameFireteams.CG_DisbandFireteam_f(args); return true;
                case "join_ft":     CGameFireteams.CG_JoinFireteam_f(args);    return true;
                case "leave_ft":    CGameFireteams.CG_LeaveFireteam_f(args);   return true;
                case "invite_ft":   CGameFireteams.CG_InviteFireteam_f(args);  return true;
                default:            return false;
            }
        }
    }
}
