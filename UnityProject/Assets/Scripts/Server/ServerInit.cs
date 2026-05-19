// Ported from: src/server/sv_init.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    /// <summary>
    /// Server initialization and map-spawn logic (sv_init.c port).
    /// </summary>
    public static class ServerInit
    {
        public const int PROTOCOL_VERSION = 84;  // ET network protocol

        // -----------------------------------------------------------------------
        // SV_Init — register all server cvars (called once at startup)
        // -----------------------------------------------------------------------
        public static void SV_Init()
        {
            // serverinfo cvars
            CvarSystem.Get("dmflags",        "0",    CvarFlags.None);
            CvarSystem.Get("fraglimit",       "0",    CvarFlags.None);
            CvarSystem.Get("timelimit",       "0",    CvarFlags.ServerInfo);
            CvarSystem.Get("sv_keywords",     "",     CvarFlags.ServerInfo);
            CvarSystem.Get("protocol", PROTOCOL_VERSION.ToString(),
                                               CvarFlags.ServerInfo | CvarFlags.ReadOnly);

            // server operational cvars
            CvarSystem.Get("sv_rconPassword",    "",    CvarFlags.Temp);
            CvarSystem.Get("sv_privatePassword", "",    CvarFlags.Temp);
            CvarSystem.Get("sv_allowDownload",   "1",   CvarFlags.Archive);
            CvarSystem.Get("sv_reconnectlimit",  "3",   CvarFlags.None);
            CvarSystem.Get("sv_showloss",        "0",   CvarFlags.None);
            CvarSystem.Get("sv_padPackets",      "0",   CvarFlags.None);
            CvarSystem.Get("sv_killserver",      "0",   CvarFlags.None);
            CvarSystem.Get("sv_mapChecksum",     "",    CvarFlags.ReadOnly);
            CvarSystem.Get("sv_lanForceRate",    "1",   CvarFlags.Archive);
            CvarSystem.Get("sv_onlyVisibleClients","0", CvarFlags.None);
            CvarSystem.Get("sv_showAverageBPS",  "0",   CvarFlags.None);
            CvarSystem.Get("sv_dl_maxRate",      "42000", CvarFlags.Archive);
            CvarSystem.Get("sv_wwwDownload",     "0",   CvarFlags.Archive);
            CvarSystem.Get("sv_cheats",          "1",   CvarFlags.SystemInfo | CvarFlags.ReadOnly);
            CvarSystem.Get("nextmap",            "",    CvarFlags.Temp);

            // game cvars
            CvarSystem.Get("g_userTimeLimit",          "0",   CvarFlags.None);
            CvarSystem.Get("g_userAlliedRespawnTime",   "0",   CvarFlags.None);
            CvarSystem.Get("g_userAxisRespawnTime",     "0",   CvarFlags.None);
            CvarSystem.Get("g_maxlives",               "0",   CvarFlags.None);
            CvarSystem.Get("g_altStopwatchMode",       "0",   CvarFlags.Archive);
            CvarSystem.Get("g_minGameClients",         "8",   CvarFlags.ServerInfo);
            CvarSystem.Get("g_complaintlimit",         "6",   CvarFlags.Archive);
            CvarSystem.Get("gamestate",                "-1",  CvarFlags.ReadOnly);
            CvarSystem.Get("g_currentRound",           "0",   CvarFlags.None);
            CvarSystem.Get("g_nextTimeLimit",          "0",   CvarFlags.None);
            CvarSystem.Get("g_voteFlags",              "0",   CvarFlags.ReadOnly | CvarFlags.ServerInfo);
            CvarSystem.Get("g_antilag",                "1",   CvarFlags.Archive | CvarFlags.ServerInfo);
            CvarSystem.Get("g_needpass",               "0",   CvarFlags.ServerInfo);

            Debug.Log("[ServerInit] SV_Init complete.");
        }

        // -----------------------------------------------------------------------
        // SV_ClearServer — wipe per-level state (before spawning new map)
        // -----------------------------------------------------------------------
        public static void SV_ClearServer()
        {
            var sv = ServerMain.Sv;
            sv.State      = ServerState.Dead;
            sv.Restarting = false;

            for (int i = 0; i < ServerConst.MAX_CONFIGSTRINGS; i++)
            {
                sv.Configstrings[i]         = "";
                sv.ConfigstringsModified[i] = false;
            }

            for (int i = 0; i < GameConst.MAX_GENTITIES; i++)
            {
                sv.Entities[i]   = new EntityState();
                sv.SvEntities[i] = new SvEntity();
            }
            sv.NumEntities   = 0;
            sv.RestartTime   = 0;
            sv.TimeResidual  = 0;
            sv.SnapshotCounter = 0;
        }

        // -----------------------------------------------------------------------
        // SV_Startup — first-time allocation of client slots
        // -----------------------------------------------------------------------
        public static void SV_Startup()
        {
            int maxClients = CvarSystem.sv_maxclients.IntValue;
            if (maxClients < 1) maxClients = 1;
            if (maxClients > ServerConst.MAX_CLIENTS) maxClients = ServerConst.MAX_CLIENTS;

            ServerMain.Svs.Init(maxClients);
            CvarSystem.Set("sv_running", "1");
            Debug.Log($"[ServerInit] Server started with max {maxClients} clients.");
        }

        // -----------------------------------------------------------------------
        // SV_SpawnServer — load a new map and begin game (sv_init.c:SV_SpawnServer)
        // -----------------------------------------------------------------------
        public static void SV_SpawnServer(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
            {
                Debug.LogError("[ServerInit] SV_SpawnServer: empty map name");
                return;
            }

            Debug.Log($"[ServerInit] ------ Server Initialization ------");
            Debug.Log($"[ServerInit] Server: {mapName}");

            SV_ClearServer();

            if (!ServerMain.Svs.Initialized)
                SV_Startup();

            var svs = ServerMain.Svs;
            var sv  = ServerMain.Sv;

            // Reset snapshot entity ring buffer
            svs.NextSnapshotEntities = 0;
            svs.SnapFlagServerBit   ^= ServerConst.SNAPFLAG_SERVERCOUNT;

            // Assign a unique server ID
            sv.ServerId          = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            sv.RestartedServerId = sv.ServerId;

            // Set base configstrings
            CvarSystem.Set("mapname", mapName, force: true);
            ServerMain.SV_SetConfigstring(ServerMain.CS_GAME_VERSION, $"ET {PROTOCOL_VERSION}");
            ServerMain.SV_SetConfigstring(ServerMain.CS_LEVEL_START_TIME, svs.Time.ToString());

            sv.State = ServerState.Loading;

            // In the C code, CM_LoadMap + VM startup happens here.
            // In Unity, map loading is done by the scene system / BSP importer.
            // Raise an event for game-layer code to initialise entities.
            OnSpawnServer?.Invoke(mapName);

            sv.State = ServerState.Game;

            // Initialise all client next-snapshot times
            int maxClients = CvarSystem.sv_maxclients.IntValue;
            int fps        = Math.Max(1, CvarSystem.sv_fps.IntValue);
            for (int i = 0; i < maxClients; i++)
            {
                var cl = svs.Clients[i];
                cl.NextSnapshotTime = svs.Time;
                cl.LastPacketTime   = svs.Time;
            }

            Debug.Log($"[ServerInit] Server ready on map '{mapName}'");
        }

        // Raised when a new map is being spawned (replaces game DLL init)
        public static event Action<string> OnSpawnServer;

        // -----------------------------------------------------------------------
        // SV_CreateBaseline — copy initial entity states into svEntities.baseline
        // -----------------------------------------------------------------------
        public static void SV_CreateBaseline()
        {
            var sv = ServerMain.Sv;
            for (int i = 1; i < sv.NumEntities; i++)
            {
                var ent      = sv.Entities[i];
                var svEntity = sv.SvEntities[i];

                if (ent.EType == 0) continue;   // ET_GENERAL default = unset
                svEntity.Baseline.CopyFrom(ent);
            }
        }

        // -----------------------------------------------------------------------
        // SV_GetConfigstring
        // -----------------------------------------------------------------------
        public static string SV_GetConfigstring(int index)
        {
            if (index < 0 || index >= ServerConst.MAX_CONFIGSTRINGS)
                return "";
            return ServerMain.Sv.Configstrings[index] ?? "";
        }

        // -----------------------------------------------------------------------
        // SV_SetUserinfo / SV_GetUserinfo
        // -----------------------------------------------------------------------
        public static void SV_SetUserinfo(int clientNum, string val)
        {
            var svs = ServerMain.Svs;
            if (svs.Clients == null || clientNum >= svs.Clients.Length) return;
            svs.Clients[clientNum].UserInfo = val ?? "";
        }

        public static string SV_GetUserinfo(int clientNum)
        {
            var svs = ServerMain.Svs;
            if (svs.Clients == null || clientNum >= svs.Clients.Length) return "";
            return svs.Clients[clientNum].UserInfo;
        }
    }
}
