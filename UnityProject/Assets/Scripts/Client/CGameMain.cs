using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

// ClSnapshot is the canonical name used throughout cgame; it maps to the client-layer type.
using ClSnapshot = ET.Client.ClientSnapshot;

namespace ET.Client
{
    // -------------------------------------------------------------------------
    // GameType — matches g_gametype values used in ET MP
    // -------------------------------------------------------------------------
    public enum GameType
    {
        SinglePlayer  = 0,
        Cooperative   = 1,
        Objective     = 2,
        Stopwatch     = 3,
        Campaign      = 4,
        LastManStanding = 5,
    }

    // -------------------------------------------------------------------------
    // CGameState — "cg" global in cg_local.h
    // -------------------------------------------------------------------------
    public class CGameState
    {
        public int          ServerTime;
        public int          Time;
        public int          OldTime;
        public int          FrameInterpolation;
        public int          PhysicsTime;
        public int          ClientNum;
        public ClSnapshot   Snap;
        public ClSnapshot   NextSnap;
        public PlayerState  PredictedPlayerState  = new PlayerState();
        public Vector3      PredictedError;
        public int          PredictedErrorTime;
        public bool         ThisFrameTeleport;
        public bool         NextFrameTeleport;
        public int          TeleportTime;
        public UserCmd[]    CmdQueue              = new UserCmd[ClientConst.CMD_BACKUP];
        public int          CurrentCmdNum;
        public bool         Demoplayback;
        public int          OldPlayerStateTime;
        public int          TimeDemoFrames;
        public int          TimeDemoStart;
        public bool         LagOMeterUsed;
        public Vector3      RefdefViewAngles;
        public int          WeaponSelect;
        public int          WeaponSelectTime;
        public bool         Zoomed;
        public int          ZoomTime;
        public float        ZoomSensitivity;
        public int          ScreenXScale, ScreenYScale;
        public int          ScreenXBias;

        public CGameState()
        {
            for (int i = 0; i < ClientConst.CMD_BACKUP; i++)
                CmdQueue[i] = new UserCmd();
        }
    }

    // -------------------------------------------------------------------------
    // CGameSharedState — "cgs" global in cg_local.h
    // -------------------------------------------------------------------------
    public class CGameSharedState
    {
        public int      ServerCommandSequence;
        public int      ProcessedSnapshotNum;
        public bool     Initing;
        public GameType GameType;
        public int      RebuyCost;
        // Index 0 = CS_SERVERINFO, 1 = CS_SYSTEMINFO, 2 = CS_WOLFINFO, ...
        public string[] GameState             = new string[1024]; // MAX_CONFIGSTRINGS
        public int      MaxClients;
        public string   MapName               = "";
        public string   MapNameClean          = "";
        public bool     HasTeamInfo;
        public int      Media_WhiteShader;
        public int      CursorX, CursorY;
        public int      Scores1, Scores2;
        public int[]    ClientInfo            = new int[GameConst.MAX_CLIENTS];

        public CGameSharedState()
        {
            for (int i = 0; i < GameState.Length; i++)
                GameState[i] = "";
        }
    }

    // -------------------------------------------------------------------------
    // CGameMain
    // -------------------------------------------------------------------------
    public static class CGameMain
    {
        public static CGameState      CG  = new CGameState();
        public static CGameSharedState CGS = new CGameSharedState();

        // Pmove instance reused each prediction pass (avoids per-frame alloc)
        private static readonly PlayerMovement s_pm        = new PlayerMovement();
        private static readonly PmoveInput     s_pmInput   = new PmoveInput();

        // Snapshot ring sourced from the client layer
        private const int SnapRingSize = ClientConst.PACKET_BACKUP;

        public static event Action<int>         OnCGInit;
        public static event Action              OnCGShutdown;
        public static event Action<int>         OnSnapshotProcessed;
        public static event Action<PlayerState> OnPlayerStatePredicted;

        // =====================================================================
        // Initialization
        // =====================================================================

        public static void CG_Init(int serverMessageNum, int serverCommandSequence, int clientNum)
        {
            CG  = new CGameState();
            CGS = new CGameSharedState();

            CG.ClientNum                  = clientNum;
            CGS.ServerCommandSequence     = serverCommandSequence;
            CGS.ProcessedSnapshotNum      = serverMessageNum;
            CGS.Initing                   = true;

            CG_RegisterCvars();

            // Parse all config strings the client layer already holds
            for (int i = 0; i < CGS.GameState.Length; i++)
            {
                string cs = ClientCGame.CL_GetConfigString(i);
                if (!string.IsNullOrEmpty(cs))
                    CG_ParseConfigString(i, cs);
            }

            CGS.Initing = false;

            OnCGInit?.Invoke(clientNum);
            Debug.Log($"CG_Init: clientNum={clientNum}");
        }

        public static void CG_Shutdown()
        {
            OnCGShutdown?.Invoke();
            CG.Snap     = null;
            CG.NextSnap = null;
        }

        public static void CG_RegisterCvars()
        {
            CvarSystem.Get("cg_drawGun",         "1",    CvarFlags.Archive);
            CvarSystem.Get("cg_fov",             "90",   CvarFlags.Archive);
            CvarSystem.Get("cg_zoomFov",         "22.5", CvarFlags.Archive);
            CvarSystem.Get("cg_lagometer",        "1",   CvarFlags.Archive);
            CvarSystem.Get("cg_showmiss",         "0",   CvarFlags.None);
            CvarSystem.Get("cg_errorDecay",       "100", CvarFlags.None);
            CvarSystem.Get("cg_nopredict",        "0",   CvarFlags.None);
            CvarSystem.Get("cg_synchronousClients","0",  CvarFlags.SystemInfo);
            CvarSystem.Get("pmove_fixed",         "0",   CvarFlags.SystemInfo);
            CvarSystem.Get("pmove_msec",          "8",   CvarFlags.SystemInfo);
        }

        // =====================================================================
        // Config string parsing
        // =====================================================================

        public static void CG_ParseConfigString(int num, string str)
        {
            if (num < 0 || num >= CGS.GameState.Length)
                return;

            CGS.GameState[num] = str;

            // CS_SERVERINFO = 0
            if (num == 0)
            {
                CG_ParseServerinfo();
                return;
            }

            // CS_WOLFINFO = 2 — objective/campaign metadata, handled by game systems
            if (num == 2 && !CGS.Initing)
                return;

            // Client info range: 32 .. 32+MAX_CLIENTS
            if (num >= 32 && num < 32 + GameConst.MAX_CLIENTS)
            {
                int ci = num - 32;
                CGS.ClientInfo[ci] = string.IsNullOrEmpty(str) ? 0 : 1;
            }
        }

        public static void CG_ParseServerinfo()
        {
            string info = CGS.GameState[0]; // CS_SERVERINFO
            if (string.IsNullOrEmpty(info))
                return;

            CGS.GameType    = (GameType)Info_ValueOfKeyInt(info, "g_gametype");
            CGS.MaxClients  = Info_ValueOfKeyInt(info, "sv_maxclients");
            CGS.RebuyCost   = Info_ValueOfKeyInt(info, "g_rebuyCost");

            string mapname  = Info_ValueOfKey(info, "mapname");
            if (!string.IsNullOrEmpty(mapname))
            {
                CGS.MapName      = "maps/" + mapname + ".bsp";
                // Strip path/extension for display name
                int slash = mapname.LastIndexOf('/');
                CGS.MapNameClean = slash >= 0 ? mapname.Substring(slash + 1) : mapname;
            }
        }

        // =====================================================================
        // Console command dispatch (cg_main.c → CG_ConsoleCommand)
        // =====================================================================

        public static bool CG_ConsoleCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd))
                return false;

            switch (cmd)
            {
                case "weapnext":
                    CG.WeaponSelect     = (CG.WeaponSelect + 1) % 32;
                    CG.WeaponSelectTime = CG.Time;
                    return true;

                case "weapprev":
                    CG.WeaponSelect     = (CG.WeaponSelect - 1 + 32) % 32;
                    CG.WeaponSelectTime = CG.Time;
                    return true;

                case "+zoom":
                    CG.Zoomed           = true;
                    CG.ZoomTime         = CG.Time;
                    return true;

                case "-zoom":
                    CG.Zoomed           = false;
                    CG.ZoomTime         = CG.Time;
                    return true;

                default:
                    return false;
            }
        }

        // =====================================================================
        // DrawActiveFrame — called once per rendered frame by ClientCGame
        // =====================================================================

        public static void CG_DrawActiveFrame(int serverTime, bool demoPlayback)
        {
            CG.OldTime      = CG.Time;
            CG.Time         = serverTime;
            CG.Demoplayback = demoPlayback;

            CG_ProcessSnapshots();

            if (CG.Snap == null)
                return; // still loading

            CG_PredictPlayerState();
            CG_InterpolatePlayerState(grabAngles: true);
            CG_CalcEntityLerpPositions();

            OnSnapshotProcessed?.Invoke(serverTime);
        }

        // =====================================================================
        // Snapshot processing (cg_snapshot.c)
        // =====================================================================

        public static void CG_ProcessSnapshots()
        {
            ClientState cl = ClientMain.Cl;

            // Advance ProcessedSnapshotNum to the latest snap whose serverTime <= CG.Time
            // We look ahead by one so NextSnap is always ready for interpolation.
            while (true)
            {
                int next = CGS.ProcessedSnapshotNum + 1;
                int idx  = next & ClientConst.PACKET_MASK;

                ClSnapshot candidate = cl.Snapshots[idx];
                if (!candidate.Valid)
                    break;

                // Reject stale entries that wrapped the ring
                if (candidate.MessageNum != next)
                    break;

                if ((candidate.SnapFlags & ClientConst.SNAPFLAG_NOT_ACTIVE) != 0)
                {
                    CGS.ProcessedSnapshotNum = next;
                    continue;
                }

                // The candidate is valid.  If it's still in the future keep it as NextSnap.
                if (candidate.ServerTime > CG.Time)
                {
                    CG.NextSnap = candidate;
                    break;
                }

                // Advance current snap
                ClSnapshot prev = CG.Snap;
                CGS.ProcessedSnapshotNum = next;

                if (CG.Snap == null)
                    CG_SetInitialSnapshot(candidate);
                else
                    CG_TransitionSnapshot(candidate);

                CG.NextSnap = null;
            }

            // Compute inter-snapshot interpolation fraction
            if (CG.Snap != null && CG.NextSnap != null)
            {
                int delta = CG.NextSnap.ServerTime - CG.Snap.ServerTime;
                if (delta > 0)
                    CG.FrameInterpolation = (int)(((float)(CG.Time - CG.Snap.ServerTime) / delta) * 1000f);
                else
                    CG.FrameInterpolation = 1000;
            }
        }

        public static void CG_SetInitialSnapshot(ClSnapshot snap)
        {
            CG.Snap = snap;
            CG.PredictedPlayerState.CopyFrom(snap.Ps);
            CG.OldPlayerStateTime = snap.ServerTime;

            // On teleport the entire snap is a hard cut — suppress lerp error
            CG.ThisFrameTeleport = true;
            CG.TeleportTime      = snap.ServerTime;

            CG_CheckChangedPredictableEvents(snap.Ps);
        }

        public static void CG_TransitionSnapshot(ClSnapshot newSnap)
        {
            if (CG.Snap == null)
            {
                CG_SetInitialSnapshot(newSnap);
                return;
            }

            // Detect teleport: EF_TELEPORT_BIT flipped between snaps
            bool teleport = ((newSnap.Ps.EFlags ^ CG.Snap.Ps.EFlags) & GameConst.EF_TELEPORT_BIT) != 0;
            if (teleport)
            {
                CG.NextFrameTeleport  = true;
                CG.TeleportTime       = newSnap.ServerTime;
            }

            CG.Snap = newSnap;
            CG_CheckChangedPredictableEvents(newSnap.Ps);
        }

        // =====================================================================
        // Prediction (cg_predict.c)
        // =====================================================================

        public static void CG_PredictPlayerState()
        {
            if (CG.Snap == null)
                return;

            // nopredict: just use the snapshot PS directly
            var nopredict = CvarSystem.Get("cg_nopredict", "0", CvarFlags.None);
            if (nopredict.IntValue != 0 || CG.Demoplayback)
            {
                CG.PredictedPlayerState.CopyFrom(CG.Snap.Ps);
                return;
            }

            ClientState cl  = ClientMain.Cl;
            int cmdNum       = cl.CmdNumber;

            // Find the oldest command not yet acknowledged by the server
            // (server acknowledges via Snap.Ps.CommandTime)
            int serverCmdTime = CG.Snap.Ps.CommandTime;
            int oldest        = cmdNum - ClientConst.CMD_BACKUP + 1;
            if (oldest < 0) oldest = 0;

            // Locate the first command after the server's last-ack time
            int startCmd = cmdNum;
            for (int i = cmdNum - 1; i >= oldest; i--)
            {
                UserCmd c = cl.Cmds[i & ClientConst.CMD_MASK];
                if (c.ServerTime <= serverCmdTime)
                {
                    startCmd = i + 1;
                    break;
                }
                startCmd = i;
            }

            // Re-run pmove from last server PS up through CG.Time
            s_pmInput.Ps.CopyFrom(CG.Snap.Ps);
            s_pmInput.Trace            = CollisionSystem.DefaultTraceFunc;
            s_pmInput.PointContents    = null;
            s_pmInput.TraceMask        = SurfaceFlags.MASK_PLAYERSOLID;
            s_pmInput.GameType         = (int)CGS.GameType;
            s_pmInput.PmoveFixed       = CvarSystem.Get("pmove_fixed", "0",  CvarFlags.None).IntValue;
            s_pmInput.PmoveMsec        = CvarSystem.Get("pmove_msec",  "8",  CvarFlags.None).IntValue;

            for (int i = startCmd; i <= cmdNum; i++)
            {
                UserCmd cmd = cl.Cmds[i & ClientConst.CMD_MASK];
                if (cmd.ServerTime > CG.Time + 200)
                    break; // don't over-predict

                s_pmInput.OldCmd.CopyFrom(s_pmInput.Cmd);
                s_pmInput.Cmd.CopyFrom(cmd);
                s_pm.Pmove(s_pmInput);
            }

            // Measure prediction error against server snapshot
            if (s_pmInput.Ps.CommandTime == serverCmdTime || CG.ThisFrameTeleport)
            {
                // Reset the error
                CG.PredictedError        = Vector3.zero;
                CG.PredictedErrorTime    = 0;
            }
            else
            {
                float decay = CvarSystem.Get("cg_errorDecay", "100", CvarFlags.None).FloatValue;
                if (decay <= 0f) decay = 100f;

                Vector3 serverOrigin = new Vector3(
                    -CG.Snap.Ps.Origin1,
                     CG.Snap.Ps.Origin2,
                     CG.Snap.Ps.Origin0);
                Vector3 predictOrigin = new Vector3(
                    -s_pmInput.Ps.Origin1,
                     s_pmInput.Ps.Origin2,
                     s_pmInput.Ps.Origin0);

                Vector3 delta = serverOrigin - predictOrigin;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    float t = (CG.Time - CG.PredictedErrorTime) / decay;
                    CG.PredictedError = Vector3.Lerp(CG.PredictedError, delta, Mathf.Clamp01(t));
                    CG.PredictedErrorTime = CG.Time;
                }
            }

            CG.PredictedPlayerState.CopyFrom(s_pmInput.Ps);
            CG.ThisFrameTeleport  = CG.NextFrameTeleport;
            CG.NextFrameTeleport  = false;

            OnPlayerStatePredicted?.Invoke(CG.PredictedPlayerState);
        }

        public static void CG_InterpolatePlayerState(bool grabAngles)
        {
            if (CG.Snap == null)
                return;

            PlayerState ps = CG.PredictedPlayerState;

            // When there is no next-snap to lerp toward, just keep predicted state
            if (CG.NextSnap == null || CG.FrameInterpolation <= 0)
                return;

            float f = CG.FrameInterpolation / 1000f;
            f = Mathf.Clamp01(f);

            PlayerState cur  = CG.Snap.Ps;
            PlayerState next = CG.NextSnap.Ps;

            ps.Origin0    = Mathf.Lerp(cur.Origin0,    next.Origin0,    f);
            ps.Origin1    = Mathf.Lerp(cur.Origin1,    next.Origin1,    f);
            ps.Origin2    = Mathf.Lerp(cur.Origin2,    next.Origin2,    f);
            ps.Velocity0  = Mathf.Lerp(cur.Velocity0,  next.Velocity0,  f);
            ps.Velocity1  = Mathf.Lerp(cur.Velocity1,  next.Velocity1,  f);
            ps.Velocity2  = Mathf.Lerp(cur.Velocity2,  next.Velocity2,  f);

            if (grabAngles)
            {
                ps.ViewAngles0 = Mathf.LerpAngle(cur.ViewAngles0, next.ViewAngles0, f);
                ps.ViewAngles1 = Mathf.LerpAngle(cur.ViewAngles1, next.ViewAngles1, f);
                ps.ViewAngles2 = Mathf.LerpAngle(cur.ViewAngles2, next.ViewAngles2, f);

                CG.RefdefViewAngles = new Vector3(ps.ViewAngles0, ps.ViewAngles1, ps.ViewAngles2);
            }

            ps.ViewHeight = (int)Mathf.Lerp(cur.ViewHeight, next.ViewHeight, f);
            ps.Leanf      = Mathf.Lerp(cur.Leanf, next.Leanf, f);
        }

        public static void CG_CalcEntityLerpPositions()
        {
            if (CG.Snap == null)
                return;

            // Non-player entities use the snap entity list; interpolation toward NextSnap
            // is driven by FrameInterpolation.  Actual lerp is performed by CGameEntities
            // (subscriber to OnSnapshotProcessed) which owns the entity centroid list.
            // This method exists as the hook point so the call order in CG_DrawActiveFrame
            // mirrors cg_predict.c:CG_CalcEntityLerpPositions exactly.
        }

        public static void CG_CheckChangedPredictableEvents(PlayerState ps)
        {
            if (ps == null)
                return;

            // Fire events whose sequence advanced.  CGameEntities / game-event handlers
            // subscribe to OnSnapshotProcessed and compare EventSequence themselves;
            // here we only need to update the tracking counter so prediction replay
            // does not re-fire already-processed events.
            CG.PredictedPlayerState.OldEventSequence = ps.EventSequence;
        }

        // =====================================================================
        // Info-string helpers (subset of Q3 Info_* — no allocation)
        // =====================================================================

        private static string Info_ValueOfKey(string info, string key)
        {
            if (string.IsNullOrEmpty(info) || string.IsNullOrEmpty(key))
                return "";

            // Format: \key\value\key\value...
            string search = "\\" + key + "\\";
            int pos = info.IndexOf(search, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
                return "";

            int start = pos + search.Length;
            int end   = info.IndexOf('\\', start);
            return end < 0 ? info.Substring(start) : info.Substring(start, end - start);
        }

        private static int Info_ValueOfKeyInt(string info, string key)
        {
            string val = Info_ValueOfKey(info, key);
            return int.TryParse(val, out int i) ? i : 0;
        }
    }
}
