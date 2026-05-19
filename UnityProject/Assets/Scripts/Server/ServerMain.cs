// Ported from: src/server/sv_main.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Net;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    /// <summary>
    /// SV_Frame equivalent — drives the server simulation loop.
    /// Call SV_Frame(msec) every engine tick from a MonoBehaviour.
    /// </summary>
    public static class ServerMain
    {
        // Global server state (mirrors svs/sv globals in C)
        public static readonly ServerStatic  Svs = new ServerStatic();
        public static readonly ServerState_t Sv  = new ServerState_t();

        // Configstring indices (CS_*)
        public const int CS_SERVERINFO   = 0;
        public const int CS_SYSTEMINFO   = 1;
        public const int CS_WOLFINFO     = 2;
        public const int CS_MUSIC        = 4;
        public const int CS_MESSAGE      = 5;
        public const int CS_MOTD         = 6;
        public const int CS_WARMUP       = 7;
        public const int CS_SCORES1      = 8;
        public const int CS_SCORES2      = 9;
        public const int CS_VOTE_TIME    = 10;
        public const int CS_VOTE_STRING  = 11;
        public const int CS_VOTE_YES     = 12;
        public const int CS_VOTE_NO      = 13;
        public const int CS_TEAMVOTE_TIME   = 14;
        public const int CS_TEAMVOTE_STRING = 16;
        public const int CS_TEAMVOTE_YES    = 18;
        public const int CS_TEAMVOTE_NO     = 20;
        public const int CS_GAME_VERSION = 22;
        public const int CS_LEVEL_START_TIME = 23;
        public const int CS_INTERMISSION = 24;
        public const int CS_MODELS       = 32;

        // -----------------------------------------------------------------------
        // SV_SetConfigstring
        // -----------------------------------------------------------------------
        public static void SV_SetConfigstring(int index, string val)
        {
            if (index < 0 || index >= ServerConst.MAX_CONFIGSTRINGS)
            {
                Debug.LogError($"SV_SetConfigstring: bad index {index}");
                return;
            }
            if (val == null) val = "";
            if (Sv.Configstrings[index] == val) return;

            Sv.Configstrings[index]         = val;
            Sv.ConfigstringsModified[index] = true;

            if (Sv.State != ServerState.Game) return;

            // Send configstring update to all active clients
            int maxClients = CvarSystem.sv_maxclients.IntValue;
            for (int i = 0; i < maxClients; i++)
            {
                var cl = Svs.Clients[i];
                if (cl.State < ClientState.Active) continue;
                SV_SendConfigstring(cl, index);
            }
        }

        private static void SV_SendConfigstring(ServerClient cl, int index)
        {
            // Sends "cs <index> <value>" reliable command to client
            string val = Sv.Configstrings[index] ?? "";
            SV_AddServerCommand(cl, $"cs {index} \"{val}\"");
        }

        // -----------------------------------------------------------------------
        // SV_AddServerCommand — adds a reliable command string to client queue
        // -----------------------------------------------------------------------
        public static void SV_AddServerCommand(ServerClient cl, string cmd)
        {
            cl.ReliableSequence++;
            int index = cl.ReliableSequence & (ServerConst.MAX_RELIABLE_COMMANDS - 1);

            if (cl.ReliableSequence - cl.ReliableAcknowledge >
                ServerConst.MAX_RELIABLE_COMMANDS)
            {
                Debug.LogWarning($"SV_AddServerCommand: client reliable buffer overflow");
                SV_DropClient(cl, "Server command overflow");
                return;
            }

            cl.ReliableCommands[index] = cmd;
        }

        // -----------------------------------------------------------------------
        // SV_DropClient
        // -----------------------------------------------------------------------
        public static void SV_DropClient(ServerClient cl, string reason)
        {
            if (cl.State == ClientState.Zombie) return;

            SV_AddServerCommand(cl, $"disconnect \"{reason}\"");
            cl.State = ClientState.Zombie;
            cl.NextSnapshotTime = Svs.Time + 1000;
        }

        // -----------------------------------------------------------------------
        // SV_CalcPings
        // -----------------------------------------------------------------------
        private static void SV_CalcPings()
        {
            int maxClients = CvarSystem.sv_maxclients.IntValue;
            for (int i = 0; i < maxClients; i++)
            {
                var cl = Svs.Clients[i];
                if (cl.State != ClientState.Active)
                {
                    cl.Ping = 999;
                    continue;
                }

                int total = 0, count = 0;
                for (int j = 0; j < ServerConst.PACKET_BACKUP; j++)
                {
                    var frame = cl.Frames[j];
                    if (frame.MessageAcked <= 0) continue;
                    total += frame.MessageAcked - frame.MessageSent;
                    count++;
                }

                cl.Ping = (count == 0) ? 999 : Math.Min(total / count, 999);

                // write ping back into player state
                if (i < Sv.GameClients.Length)
                    Sv.GameClients[i].Ping = cl.Ping;
            }
        }

        // -----------------------------------------------------------------------
        // SV_CheckTimeouts
        // -----------------------------------------------------------------------
        private static void SV_CheckTimeouts()
        {
            int timeoutSec   = CvarSystem.sv_timeout.IntValue;
            int zombieSec    = 2;   // sv_zombietime default
            int dropPoint    = Svs.Time - 1000 * timeoutSec;
            int zombiePoint  = Svs.Time - 1000 * zombieSec;
            int maxClients   = CvarSystem.sv_maxclients.IntValue;

            for (int i = 0; i < maxClients; i++)
            {
                var cl = Svs.Clients[i];
                if (cl.LastPacketTime > Svs.Time)
                    cl.LastPacketTime = Svs.Time;

                if (cl.State == ClientState.Zombie)
                {
                    if (cl.LastPacketTime < zombiePoint)
                        cl.State = ClientState.Free;
                    continue;
                }

                if (cl.State >= ClientState.Connected &&
                    cl.LastPacketTime < dropPoint)
                {
                    if (++cl.TimeoutCount > 5)
                    {
                        SV_DropClient(cl, "timed out");
                        cl.State = ClientState.Free;
                    }
                }
                else
                {
                    cl.TimeoutCount = 0;
                }
            }
        }

        // -----------------------------------------------------------------------
        // SV_CheckPaused — only pause if exactly one local client connected
        // -----------------------------------------------------------------------
        private static bool SV_CheckPaused()
        {
            int maxClients = CvarSystem.sv_maxclients.IntValue;
            int count = 0;
            for (int i = 0; i < maxClients; i++)
                if (Svs.Clients[i].State >= ClientState.Connected)
                    count++;
            return count <= 1 && CvarSystem.GetInt("cl_paused") != 0;
        }

        // -----------------------------------------------------------------------
        // SV_SendClientMessages — push snapshots and reliable cmds
        // -----------------------------------------------------------------------
        private static void SV_SendClientMessages()
        {
            int maxClients = CvarSystem.sv_maxclients.IntValue;
            for (int i = 0; i < maxClients; i++)
            {
                var cl = Svs.Clients[i];
                if (cl.State == ClientState.Free) continue;
                SV_SendClientSnapshot(cl, i);
            }
        }

        private static void SV_SendClientSnapshot(ServerClient cl, int clientNum)
        {
            if (Svs.Time < cl.NextSnapshotTime) return;

            int fps = Math.Max(1, CvarSystem.sv_fps.IntValue);
            cl.SnapshotMsec = 1000 / fps;
            cl.NextSnapshotTime = Svs.Time + cl.SnapshotMsec;

            ServerSnapshot.SV_SendClientGameState(cl, clientNum);
        }

        // -----------------------------------------------------------------------
        // SV_Frame — main server tick (call from MonoBehaviour.Update or FixedUpdate)
        // -----------------------------------------------------------------------
        public static void SV_Frame(int msec)
        {
            if (!Svs.Initialized) return;
            if (Sv.State == ServerState.Dead) return;
            if (SV_CheckPaused()) return;

            int svFps = Math.Max(1, CvarSystem.sv_fps.IntValue);
            int frameMsec = 1000 / svFps;

            Sv.TimeResidual += msec;

            // time overflow protection (~23 days of continuous uptime)
            if (Svs.Time > 0x70000000)
            {
                SV_Shutdown("Restarting server due to time wrapping");
                return;
            }

            if (Svs.NextSnapshotEntities >= 0x7FFFFFFE - Svs.NumSnapshotEntities)
            {
                SV_Shutdown("Restarting server due to numSnapshotEntities wrapping");
                return;
            }

            if (Sv.RestartTime != 0 && Svs.Time >= Sv.RestartTime)
            {
                Sv.RestartTime = 0;
                // TODO: map restart
                return;
            }

            SV_CalcPings();

            // run game simulation in fixed-msec chunks
            while (Sv.TimeResidual >= frameMsec)
            {
                Sv.TimeResidual -= frameMsec;
                Svs.Time        += frameMsec;

                // === GAME_RUN_FRAME ===
                // In C this calls into the game DLL via VM_Call(gvm, GAME_RUN_FRAME, svs.time)
                // In Unity: raise an event that game logic can subscribe to
                OnGameRunFrame?.Invoke(Svs.Time);
            }

            SV_CheckTimeouts();
            SV_SendClientMessages();

            Svs.TotalFrameTime += msec;
            if (++Svs.CurrentFrameIndex >= ServerConst.SERVER_PERFORMANCECOUNTER_FRAMES)
            {
                int idx = Svs.CurrentSampleIndex++ % ServerConst.SERVER_PERFORMANCECOUNTER_SAMPLES;
                Svs.SampleTimes[idx] = Svs.TotalFrameTime;
                Svs.TotalFrameTime   = 0;
                Svs.CurrentFrameIndex = 0;
            }
        }

        // Event raised each game-logic tick (replaces VM_Call(gvm, GAME_RUN_FRAME))
        public static event Action<int> OnGameRunFrame;

        // -----------------------------------------------------------------------
        // SV_Shutdown
        // -----------------------------------------------------------------------
        public static void SV_Shutdown(string reason)
        {
            if (!Svs.Initialized) return;
            Debug.Log($"[Server] Shutting down: {reason}");

            int maxClients = CvarSystem.sv_maxclients.IntValue;
            for (int i = 0; i < maxClients; i++)
            {
                var cl = Svs.Clients[i];
                if (cl.State >= ClientState.Connected)
                    SV_DropClient(cl, reason);
            }

            Sv.State       = ServerState.Dead;
            Svs.Initialized = false;
        }
    }
}
