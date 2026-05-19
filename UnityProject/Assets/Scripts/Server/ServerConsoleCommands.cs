// Ports: sv_ccmds.c + sv_bot.c + sv_net_chan.c
// Server operator console commands, bot server integration, and the
// netchan XOR encode/decode layer (used between ServerClient and NetChannel).

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ET.Core;
using ET.Game;
using ET.Network;

namespace ET.Server
{
    // -------------------------------------------------------------------------
    // ServerConsoleCommands — sv_ccmds.c
    // Operator (server console) commands registered via CmdSystem.
    // -------------------------------------------------------------------------
    public static class ServerConsoleCommands
    {
        private static bool _initialized;

        // SV_AddOperatorCommands
        public static void Register()
        {
            if (_initialized) return;
            _initialized = true;

            
            CmdSystem.Cmd_AddCommand("heartbeat",         Cmd_Heartbeat);
            CmdSystem.Cmd_AddCommand("status",             Cmd_Status);
            CmdSystem.Cmd_AddCommand("serverinfo",         Cmd_Serverinfo);
            CmdSystem.Cmd_AddCommand("systeminfo",         Cmd_Systeminfo);
            CmdSystem.Cmd_AddCommand("dumpuser",           Cmd_DumpUser);
            CmdSystem.Cmd_AddCommand("map",                Cmd_Map);
            CmdSystem.Cmd_AddCommand("devmap",             Cmd_Map);
            CmdSystem.Cmd_AddCommand("map_restart",        Cmd_MapRestart);
            CmdSystem.Cmd_AddCommand("killserver",         Cmd_KillServer);
            CmdSystem.Cmd_AddCommand("say",                Cmd_ConSay);
            CmdSystem.Cmd_AddCommand("gameCompleteStatus", Cmd_GameCompleteStatus);
            CmdSystem.Cmd_AddCommand("kick",               Cmd_Kick);
            CmdSystem.Cmd_AddCommand("clientkick",         Cmd_KickNum);
            CmdSystem.Cmd_AddCommand("ban",                Cmd_Ban);
        }

        // SV_RemoveOperatorCommands — in ET these are intentionally not removed
        public static void Unregister() { /* keep commands alive for server restart */ }

        // ------------------------------------------------------------------ //

        // SV_Heartbeat_f — forces an immediate heartbeat to master servers
        private static void Cmd_Heartbeat(string[] args) =>
            ServerMain.ForceMasterHeartbeat();

        // SV_Status_f — prints connected client list
        private static void Cmd_Status(string[] args)
        {
            var level = ServerGameLogic.Level;
            Debug.Log($"map: {level.MapName}");
            Debug.Log("num score ping name            lastmsg address               rate");
            Debug.Log("--- ----- ---- --------------- ------- --------------------- -----");

            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var cl = ServerMain.Svs.Clients[i];
                if (cl == null || !cl.Active) continue;

                var ps   = ServerGameLogic.Entities[i]?.Client?.PS;
                int score = ps?.Persistant[GameConstants.PERS_SCORE] ?? 0;
                string pingStr = cl.Connected ? "CNCT" : $"{cl.Ping,4}";
                Debug.Log($"{i,3} {score,5} {pingStr} {cl.Name,-16} {cl.Address}  {cl.Rate,5}");
            }
        }

        // SV_Serverinfo_f
        private static void Cmd_Serverinfo(string[] args)
        {
            
            Debug.Log("Server info settings:");
            Debug.Log(CvarSystem.GetInfoString(CvarFlags.ServerInfo));
        }

        // SV_Systeminfo_f
        private static void Cmd_Systeminfo(string[] args)
        {
            
            Debug.Log("System info settings:");
            Debug.Log(CvarSystem.GetInfoString(CvarFlags.SystemInfo));
        }

        // SV_DumpUser_f
        private static void Cmd_DumpUser(string[] args)
        {
            if (args.Length < 2) { Debug.Log("Usage: dumpuser <userid>"); return; }
            if (!int.TryParse(args[1], out int num) || num < 0 || num >= GameConstants.MAX_CLIENTS)
            { Debug.Log("Invalid client ID"); return; }
            string info = ServerGameLogic.GetClientUserinfo(num);
            Debug.Log($"userinfo\n--------\n{info}");
        }

        // SV_Map_f / SV_DevMap_f
        private static void Cmd_Map(string[] args)
        {
            if (args.Length < 2) { Debug.Log("Usage: map <mapname>"); return; }
            string mapName = args[1];
            bool cheat = args[0].StartsWith("dev", StringComparison.OrdinalIgnoreCase);

            // Verify BSP exists via FileSystem
            if (!FileSystem.FS_FileExists($"maps/{mapName}.bsp"))
            { Debug.LogError($"Can't find map {mapName}"); return; }

            
            CvarSystem.Set("gamestate",       GameConstants.GS_INITIALIZE.ToString());
            CvarSystem.Set("g_currentRound",  "0");
            CvarSystem.Set("g_nextTimeLimit", "0");
            CvarSystem.Set("sv_cheats",       cheat ? "1" : "0");

            ServerInit.SV_SpawnServer(mapName);
        }

        // SV_MapRestart_f
        private static void Cmd_MapRestart(string[] args)
        {
            int delay = 0;
            if (args.Length > 1) int.TryParse(args[1], out delay);
            int newGs = GameConstants.GS_WARMUP;
            if (args.Length > 2) int.TryParse(args[2], out newGs);

            
            CvarSystem.Set("gamestate", newGs.ToString());
            ServerInit.SV_SpawnServer(ServerGameLogic.Level.MapName);
        }

        // SV_ConSay_f — broadcasts a console chat message
        private static void Cmd_ConSay(string[] args)
        {
            if (args.Length < 2) return;
            string msg = string.Join(" ", args, 1, args.Length - 1).Trim('"');
            ServerMain.SendServerCommand(null, $"chat \"console: {msg}\"");
        }

        // SV_GameCompleteStatus_f
        private static void Cmd_GameCompleteStatus(string[] args) =>
            ServerMain.SendMasterGameCompleteStatus();

        // SV_Kick_f — kick by name
        private static void Cmd_Kick(string[] args)
        {
            if (args.Length < 2) { Debug.Log("Usage: kick <name>"); return; }
            string name = args[1];
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var cl = ServerMain.Svs.Clients[i];
                if (cl != null && cl.Active &&
                    string.Equals(cl.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    SV_Client.DropClient(i, "kicked");
                    return;
                }
            }
            Debug.Log($"kick: player '{name}' not found");
        }

        // SV_KickNum_f — kick by client number
        private static void Cmd_KickNum(string[] args)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out int num))
            { Debug.Log("Usage: clientkick <num>"); return; }
            SV_Client.DropClient(num, "kicked");
        }

        // SV_KillServer_f — shuts down the server
        private static void Cmd_KillServer(string[] args)
        {
            ServerInit.SV_Shutdown("Server killed by operator.");
        }

        // SV_Ban_f — IP ban (stored in cvar list, cleared on restart)
        private static void Cmd_Ban(string[] args)
        {
            if (args.Length < 2) { Debug.Log("Usage: ban <name>"); return; }
            // Stub: in a real deployment, record the IP and check on connect
            Debug.Log($"ban: {args[1]} (stub — no persistent ban file in Unity build)");
        }

        // SV_CheckTransitionGameState — validates state machine transition
        public static bool CheckTransitionGameState(int newGs, int oldGs)
        {
            if (oldGs == newGs && newGs != GameConstants.GS_PLAYING) return false;
            if (oldGs == GameConstants.GS_INTERMISSION && newGs != GameConstants.GS_WARMUP) return false;
            return true;
        }

        // SV_TransitionGameState
        public static bool TransitionGameState(int newGs, int oldGs, int delayMs)
        {
            if (oldGs == GameConstants.GS_INTERMISSION && newGs == GameConstants.GS_PLAYING)
                newGs = GameConstants.GS_WARMUP;

            if (!CheckTransitionGameState(newGs, oldGs)) return false;

            if (newGs == GameConstants.GS_RESET) newGs = GameConstants.GS_WARMUP;
            CvarSystem.Set("gamestate", newGs.ToString());
            return true;
        }
    }

    // -------------------------------------------------------------------------
    // ServerBotIntegration — sv_bot.c
    // Bridges the server layer with BotAI. The botlib's import functions
    // (BotImport_Trace, BotImport_PointContents, etc.) are replaced by direct
    // calls into Unity PhysX (CollisionSystem) and NavMesh (PathFinding).
    // -------------------------------------------------------------------------
    public static class ServerBotIntegration
    {
        public static bool BotEnabled => CvarSystem.GetInt("bot_enable") != 0;

        private static readonly HashSet<int> _botClients = new HashSet<int>();

        // SV_BotAllocateClient — gives a bot a client slot
        public static int AllocateBotClient(int preferredNum)
        {
            // Try preferred slot first, then any free slot
            for (int pass = 0; pass < 2; pass++)
            {
                int start = pass == 0 ? preferredNum : 0;
                int end   = pass == 0 ? preferredNum + 1 : GameConstants.MAX_CLIENTS;
                for (int i = start; i < end; i++)
                {
                    var cl = ServerMain.Svs.Clients[i];
                    if (cl == null || !cl.Active)
                    {
                        SV_Client.ConnectBot(i);
                        _botClients.Add(i);
                        return i;
                    }
                }
            }
            return -1;
        }

        // SV_BotFreeClient
        public static void FreeClient(int clientNum)
        {
            SV_Client.DropClient(clientNum, "bot freed");
            _botClients.Remove(clientNum);
        }

        // BotImport_Trace — delegates to CollisionSystem (Unity PhysX)
        public static TraceResult Trace(
            Vector3 start, Vector3 mins, Vector3 maxs, Vector3 end,
            int passEntityNum, int contentMask)
            => CollisionSystem.CM_BoxTrace(start, end, mins, maxs, passEntityNum, contentMask);

        // BotImport_PointContents
        public static int PointContents(Vector3 point)
            => CollisionSystem.CM_PointContents(point, -1);

        // BotImport_inPVS — Unity has no BSP PVS; use distance check instead
        public static bool InPVS(Vector3 p1, Vector3 p2)
        {
            float dist = Vector3.Distance(p1, p2);
            return dist < 2048f;   // approximate PVS radius
        }

        // BotImport_GetMemory / FreeMemory — C# GC handles this
        public static bool IsBot(int clientNum) => _botClients.Contains(clientNum);
    }

    // -------------------------------------------------------------------------
    // ServerNetChan — sv_net_chan.c
    // XOR encode/decode layer applied to outgoing/incoming server messages.
    // The key is derived from the client's challenge XOR the netchan sequence.
    // -------------------------------------------------------------------------
    public static class ServerNetChan
    {
        // SV_ENCODE_START — first 4 bytes (reliableAcknowledge) are skipped
        private const int SV_ENCODE_START = 4;

        // SV_Netchan_Encode — XOR-encodes outgoing server message
        // commandString is the last reliable command sent to this client
        public static void Encode(ref byte[] data, int length, int challenge,
                                  int outgoingSequence, string commandString)
        {
            if (length < SV_ENCODE_START) return;

            byte[] keyStr = Encoding.UTF8.GetBytes(commandString ?? "");
            int strIdx = 0;
            byte key = (byte)(challenge ^ outgoingSequence);

            for (int i = SV_ENCODE_START; i < length; i++)
            {
                if (strIdx >= keyStr.Length) strIdx = 0;
                byte c = keyStr.Length > 0 ? keyStr[strIdx] : (byte)0;
                if (c > 127 || c == (byte)'%')
                    key ^= (byte)('.' << (i & 1));
                else
                    key ^= (byte)(c << (i & 1));
                strIdx++;
                data[i] ^= key;
            }
        }

        // SV_Netchan_Decode — XOR-decodes incoming client message
        // SV_DECODE_START = 12: skip serverId(4) + messageAck(4) + reliableAck(4)
        private const int SV_DECODE_START = 12;

        public static void Decode(ref byte[] data, int length, int challenge,
                                  int incomingSequence, string commandString)
        {
            if (length < SV_DECODE_START) return;

            byte[] keyStr = Encoding.UTF8.GetBytes(commandString ?? "");
            int strIdx = 0;

            // Derive key from challenge XOR (serverId read from first 4 bytes)
            int serverId = BitConverter.ToInt32(data, 0);
            byte key = (byte)(challenge ^ incomingSequence ^ serverId);

            for (int i = SV_DECODE_START; i < length; i++)
            {
                if (strIdx >= keyStr.Length) strIdx = 0;
                byte c = keyStr.Length > 0 ? keyStr[strIdx] : (byte)0;
                if (c > 127 || c == (byte)'%')
                    key ^= (byte)('.' << (i & 1));
                else
                    key ^= (byte)(c << (i & 1));
                strIdx++;
                data[i] ^= key;
            }
        }

        // SV_Netchan_TransmitNextFragment — delegates to base NetChannel
        public static void TransmitNextFragment(NetChannel chan) =>
            chan.TransmitNextFragment();

        // SV_Netchan_Transmit — encodes then sends via NetChannel
        public static void Transmit(NetChannel chan, int challenge, string lastCmd,
                                    byte[] data, int length)
        {
            byte[] copy = new byte[length];
            Array.Copy(data, copy, length);
            Encode(ref copy, length, challenge, chan.OutgoingSequence, lastCmd);
            chan.Transmit(copy, length);
        }

        // SV_Netchan_Process — decodes incoming then passes to NetChannel
        public static bool Process(NetChannel chan, int challenge, string lastCmd,
                                   NetMessage msg)
        {
            if (!chan.Process(msg)) return false;
            Decode(ref msg.Data, msg.CurSize, challenge, chan.IncomingSequence, lastCmd);
            return true;
        }
    }
}
