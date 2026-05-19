// Ported from: src/server/sv_client.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    /// <summary>
    /// Client connection management (sv_client.c port).
    /// Handles connect, disconnect, userinfo, usercmd processing.
    /// </summary>
    public static class SV_Client
    {
        // Maximum usercmds in one packet
        private const int MAX_PACKET_USERCMDS = ServerConst.MAX_PACKET_USERCMDS;

        // -----------------------------------------------------------------------
        // SV_GetChallenge — send a challenge to a connecting client
        // -----------------------------------------------------------------------
        public static void SV_GetChallenge(NetAddress from)
        {
            var svs = ServerMain.Svs;

            // reuse existing challenge if present
            int oldest = 0;
            int oldestTime = int.MaxValue;
            for (int i = 0; i < ServerConst.MAX_CHALLENGES; i++)
            {
                if (svs.Challenges[i].Adr.Compare(from)) break;
                if (svs.Challenges[i].Time < oldestTime)
                {
                    oldestTime = svs.Challenges[i].Time;
                    oldest     = i;
                }
            }

            var ch = svs.Challenges[oldest];
            ch.Adr           = from;
            ch.ChallengeNum  = (new System.Random()).Next(0x10000);
            ch.Time          = svs.Time;
            ch.PingTime      = svs.Time;
            ch.FirstPing     = 0;
            ch.Connected     = false;
            svs.Challenges[oldest] = ch;

            NetChannel.SendOOBPrint(NetSrc.Server, from,
                $"challengeResponse {ch.ChallengeNum}");
        }

        // -----------------------------------------------------------------------
        // SV_DirectConnect — a "connect" OOB packet was received
        // -----------------------------------------------------------------------
        public static void SV_DirectConnect(NetAddress from, string userInfo)
        {
            var svs = ServerMain.Svs;

            int.TryParse(ETShared.Info_ValueForKey(userInfo, "protocol"), out int version);
            if (version != ServerInit.PROTOCOL_VERSION)
            {
                NetChannel.SendOOBPrint(NetSrc.Server, from,
                    "print\n[err_prot]Protocol version mismatch");
                return;
            }

            int.TryParse(ETShared.Info_ValueForKey(userInfo, "challenge"), out int challenge);
            int.TryParse(ETShared.Info_ValueForKey(userInfo, "qport"),     out int qport);

            // validate challenge
            bool challengeOk = from.IsLocal;
            if (!challengeOk)
            {
                for (int i = 0; i < ServerConst.MAX_CHALLENGES; i++)
                {
                    ref var ch = ref svs.Challenges[i];
                    if (ch.Adr.Compare(from) && ch.ChallengeNum == challenge)
                    {
                        challengeOk = true;
                        ch.Connected = true;
                        if (ch.FirstPing == 0)
                            ch.FirstPing = svs.Time - ch.PingTime;
                        break;
                    }
                }
            }

            if (!challengeOk)
            {
                NetChannel.SendOOBPrint(NetSrc.Server, from,
                    "print\n[err_dialog]No or bad challenge");
                return;
            }

            // find a free client slot
            int maxClients = CvarSystem.sv_maxclients.IntValue;
            int clientNum  = -1;
            for (int i = 0; i < maxClients; i++)
            {
                // reuse existing connection from same IP
                var cl2 = svs.Clients[i];
                if (cl2.Netchan.RemoteAddress.CompareBase(from) &&
                    (cl2.Netchan.QPort == qport ||
                     from.Port == cl2.Netchan.RemoteAddress.Port))
                {
                    clientNum = i;
                    break;
                }
                if (cl2.State == ClientState.Free && clientNum == -1)
                    clientNum = i;
            }

            if (clientNum == -1)
            {
                NetChannel.SendOOBPrint(NetSrc.Server, from,
                    "print\n[err_dialog]Server is full");
                return;
            }

            var newcl = svs.Clients[clientNum];
            // reset for fresh connect
            newcl.State              = ClientState.Connected;
            newcl.UserInfo           = userInfo;
            newcl.Challenge          = challenge;
            newcl.LastPacketTime     = svs.Time;
            newcl.LastConnectTime    = svs.Time;
            newcl.ReliableSequence   = 0;
            newcl.ReliableAcknowledge= 0;
            newcl.DeltaMessage       = -1;
            newcl.NextSnapshotTime   = svs.Time;

            newcl.Netchan.Setup(NetSrc.Server, from, qport);

            SV_UserinfoChanged(newcl);

            // send the gamestate
            SV_SendGamestate(newcl, clientNum);

            Debug.Log($"[ServerClient] Client {clientNum} connected from {from}");
        }

        // -----------------------------------------------------------------------
        // SV_UserinfoChanged — parse and apply client userinfo string
        // -----------------------------------------------------------------------
        public static void SV_UserinfoChanged(ServerClient cl)
        {
            var info = cl.UserInfo;

            cl.Name = ETShared.Info_ValueForKey(info, "name");
            if (string.IsNullOrEmpty(cl.Name)) cl.Name = "UnnamedPlayer";

            int.TryParse(ETShared.Info_ValueForKey(info, "rate"), out int rate);
            if (rate < ServerConst.MIN_RATE)  rate = ServerConst.MIN_RATE;
            if (rate > 90000)                  rate = 90000;
            cl.Rate = rate;

            int.TryParse(ETShared.Info_ValueForKey(info, "snaps"), out int snapshotMsec);
            if (snapshotMsec > 0)
                cl.SnapshotMsec = 1000 / Math.Clamp(snapshotMsec, 1, 60);
        }

        // -----------------------------------------------------------------------
        // SV_SendGamestate — send svc_gamestate to a connecting client
        // -----------------------------------------------------------------------
        public static void SV_SendGamestate(ServerClient cl, int clientNum)
        {
            var sv  = ServerMain.Sv;
            var svs = ServerMain.Svs;

            var buf = new byte[NetConst.MAX_MSGLEN];
            var msg = new NetMessage(buf, buf.Length);

            cl.GamestateMessageNum = cl.Netchan.OutgoingSequence;

            // reliable commands pending
            ServerSnapshot.SV_UpdateServerCommandsToClient(cl, msg);

            // svc_gamestate
            msg.WriteByte(ServerSnapshot.SVC_GAMESTATE);
            msg.WriteLong(cl.ReliableSequence);

            // send all configstrings
            for (int i = 0; i < ServerConst.MAX_CONFIGSTRINGS; i++)
            {
                string cs = sv.Configstrings[i];
                if (string.IsNullOrEmpty(cs)) continue;
                msg.WriteByte(ServerSnapshot.SVC_CONFIGSTRING);
                msg.WriteShort(i);
                msg.WriteBigString(cs);
            }

            // send all baselines
            for (int i = 0; i < sv.NumEntities; i++)
            {
                var baseline = sv.SvEntities[i].Baseline;
                if (baseline.Number == 0) continue;
                msg.WriteByte(ServerSnapshot.SVC_BASELINE);
                msg.WriteDeltaEntity(null, baseline, true);
            }

            // end of gamestate
            msg.WriteByte(ServerSnapshot.SVC_SERVERCOMMAND);
            msg.WriteLong(cl.ReliableSequence);
            msg.WriteString("cs 0 \"\"");  // null command to mark end

            msg.WriteLong(clientNum);
            msg.WriteLong(sv.ChecksumFeed);

            cl.Netchan.Transmit(msg.CurSize, buf);
        }

        // -----------------------------------------------------------------------
        // SV_ClientEnterWorld — client sent first valid usercmd after gamestate
        // -----------------------------------------------------------------------
        public static void SV_ClientEnterWorld(ServerClient cl, int clientNum, UserCmd cmd)
        {
            cl.State    = ClientState.Active;
            cl.DeltaMessage = -1;
            cl.LastUsercmd.CopyFrom(cmd);

            Debug.Log($"[ServerClient] Client {clientNum} ({cl.Name}) entered world.");
            OnClientEnterWorld?.Invoke(clientNum, cl);
        }

        // Raised when a client finishes connecting (replaces GAME_CLIENT_BEGIN)
        public static event Action<int, ServerClient> OnClientEnterWorld;

        // -----------------------------------------------------------------------
        // SV_ClientThink — apply a usercmd from the client
        // -----------------------------------------------------------------------
        public static void SV_ClientThink(ServerClient cl, int clientNum, UserCmd cmd)
        {
            cl.LastUsercmd.CopyFrom(cmd);
            if (cl.State != ClientState.Active) return;

            // In C this does VM_Call(gvm, GAME_CLIENT_THINK, clientNum)
            // In Unity we raise an event for the game layer
            OnClientThink?.Invoke(clientNum, cmd);
        }

        // Raised each frame per active client (replaces GAME_CLIENT_THINK)
        public static event Action<int, UserCmd> OnClientThink;

        // -----------------------------------------------------------------------
        // SV_ProcessClientMessage — parse an incoming client packet
        // -----------------------------------------------------------------------
        public static void SV_ProcessClientMessage(ServerClient cl, int clientNum,
            NetMessage msg)
        {
            if (cl.State == ClientState.Zombie) return;

            cl.LastPacketTime = ServerMain.Svs.Time;

            // read reliable acknowledge
            int reliableAck = msg.ReadLong();
            if (reliableAck < cl.ReliableAcknowledge)
                reliableAck = cl.ReliableAcknowledge;
            if (reliableAck > cl.ReliableSequence)
            {
                ServerMain.SV_DropClient(cl, "bad reliableAcknowledge");
                return;
            }
            cl.ReliableAcknowledge = reliableAck;

            // process client commands from this packet
            while (true)
            {
                int cmd = msg.ReadByte();
                if (msg.Overflowed || cmd < 0) break;

                switch (cmd)
                {
                    case 1:   // clc_move (delta)
                        SV_UserMove(cl, clientNum, msg, true);
                        break;
                    case 2:   // clc_moveNoDelta
                        SV_UserMove(cl, clientNum, msg, false);
                        break;
                    case 3:   // clc_clientCommand
                        if (!SV_ClientCommand(cl, clientNum, msg)) return;
                        break;
                    default:
                        ServerMain.SV_DropClient(cl, "unknown client message type");
                        return;
                }
            }
        }

        // -----------------------------------------------------------------------
        // SV_UserMove — read usercmds from message and run them
        // -----------------------------------------------------------------------
        private static void SV_UserMove(ServerClient cl, int clientNum,
            NetMessage msg, bool delta)
        {
            if (delta)
                cl.DeltaMessage = cl.MessageAcknowledge;
            else
                cl.DeltaMessage = -1;

            int cmdCount = msg.ReadByte();
            if (cmdCount < 1 || cmdCount > MAX_PACKET_USERCMDS)
            {
                ServerMain.SV_DropClient(cl, "bad usercmd count");
                return;
            }

            var cmds = new UserCmd[cmdCount];
            var nullcmd = new UserCmd();
            var oldcmd  = nullcmd;

            for (int i = 0; i < cmdCount; i++)
            {
                cmds[i] = new UserCmd();
                msg.ReadDeltaUserCmd(oldcmd, cmds[i]);
                oldcmd = cmds[i];
            }

            // save ping measurement
            var frame = cl.Frames[cl.MessageAcknowledge & ServerConst.PACKET_MASK];
            frame.MessageAcked = ServerMain.Svs.Time;

            if (cl.State == ClientState.Primed)
                SV_ClientEnterWorld(cl, clientNum, cmds[cmdCount - 1]);

            if (cl.State != ClientState.Active) return;

            // run all cmds
            for (int i = 0; i < cmdCount; i++)
                SV_ClientThink(cl, clientNum, cmds[i]);
        }

        // -----------------------------------------------------------------------
        // SV_ClientCommand — process a client command string
        // -----------------------------------------------------------------------
        private static bool SV_ClientCommand(ServerClient cl, int clientNum,
            NetMessage msg)
        {
            int seq = msg.ReadLong();
            string s = msg.ReadString();

            if (seq <= cl.LastClientCommand) return true; // already processed
            cl.LastClientCommand          = seq;
            cl.LastClientCommandString    = s;

            // parse first token as command name
            int space = s.IndexOf(' ');
            string cmdName = space < 0 ? s : s.Substring(0, space);
            string args    = space < 0 ? "" : s.Substring(space + 1);

            OnClientCommand?.Invoke(clientNum, cmdName, args);
            return true;
        }

        // Raised when a client sends a command string (replaces SV_ExecuteClientCommand)
        public static event Action<int, string, string> OnClientCommand;

        // DropClient — convenience wrapper for operator commands
        public static void DropClient(int clientNum, string reason)
        {
            var svs = ServerMain.Svs;
            if (clientNum < 0 || clientNum >= svs.Clients.Length) return;
            ServerMain.SV_DropClient(svs.Clients[clientNum], reason);
        }

        // ConnectBot — mark a slot as a bot client
        public static void ConnectBot(int clientNum)
        {
            var svs = ServerMain.Svs;
            if (clientNum < 0 || clientNum >= svs.Clients.Length) return;
            var cl = svs.Clients[clientNum];
            cl.State   = ClientState.Active;
            cl.IsBot   = true;
            cl.Name    = $"Bot_{clientNum}";
        }
    }
}
