using System;
using System.Net.Sockets;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    public static class ClientMain
    {
        public static readonly ClientConnection Clc = new ClientConnection();
        public static readonly ClientState      Cl  = new ClientState();

        private static int   _lastResendTime;
        private static int   _realtime;          // accumulated ms since init
        private static int   _lastSendTime;
        private static float _sendResidue;

        public static event Action<int> OnCLFrame;

        public static void CL_Init()
        {
            CvarSystem.Get("cl_maxpackets",     "30",   0);
            CvarSystem.Get("cl_timenudge",      "0",    0);
            CvarSystem.Get("cl_shownet",        "0",    0);
            CvarSystem.Get("cl_packetdup",      "1",    0);
            CvarSystem.Get("rate",              "25000",0);
            Debug.Log("CL_Init");
        }

        public static void CL_Disconnect(string reason)
        {
            if (Clc.State == ClientConnectionState.Disconnected)
                return;

            if (!string.IsNullOrEmpty(reason))
                Debug.Log("CL_Disconnect: " + reason);

            Clc.State = ClientConnectionState.Disconnected;
            Cl.ServerTime = 0;
            Cl.NewSnapshots = false;
            Cl.ExtrapolatedSnapshot = false;
        }

        public static void CL_Connect(string serverAddress)
        {
            if (string.IsNullOrEmpty(serverAddress))
            {
                Debug.LogWarning("CL_Connect: empty address");
                return;
            }

            CL_Disconnect("reconnecting");

            Clc.ServerAddress = NetAddress.Parse(serverAddress);
            if (Clc.ServerAddress == null)
            {
                Debug.LogWarning("CL_Connect: bad address " + serverAddress);
                return;
            }

            Clc.State = ClientConnectionState.Challenging;
            _lastResendTime = -99999;

            // Send initial challenge request immediately
            NetChannel.SendOOBPrint(Clc.ServerAddress, NetSrc.Client, "getchallenge");
            _lastResendTime = _realtime;

            Debug.Log("CL_Connect: challenging " + serverAddress);
        }

        public static void CL_CheckForResend()
        {
            if (Clc.State == ClientConnectionState.Challenging)
            {
                if (_realtime - _lastResendTime > 3000)
                {
                    NetChannel.SendOOBPrint(Clc.ServerAddress, NetSrc.Client, "getchallenge");
                    _lastResendTime = _realtime;
                    Debug.Log("CL_CheckForResend: resending getchallenge");
                }
            }
            else if (Clc.State == ClientConnectionState.Connecting)
            {
                if (_realtime - _lastResendTime > 3000)
                {
                    string connectString = string.Format(
                        "connect \"{0} {1} {2}\"",
                        Clc.Challenge,
                        Clc.Netchan.QPort,
                        Clc.ClientNum);
                    NetChannel.SendOOBPrint(Clc.ServerAddress, NetSrc.Client, connectString);
                    _lastResendTime = _realtime;
                    Debug.Log("CL_CheckForResend: resending connect");
                }
            }
        }

        public static void CL_SendCmd()
        {
            if (Clc.State < ClientConnectionState.Connected)
                return;

            int maxPackets = CvarSystem.GetInt("cl_maxpackets");
            if (maxPackets < 1)  maxPackets = 1;
            if (maxPackets > 125) maxPackets = 125;

            // Rate-limit: only send if enough time has elapsed
            int minInterval = 1000 / maxPackets;
            if (_realtime - _lastSendTime < minInterval)
                return;

            _lastSendTime = _realtime;

            byte[] buf = new byte[ClientConst.MAX_MSGLEN];
            NetMessage msg = new NetMessage(buf, buf.Length);

            // Reliable acknowledge
            msg.WriteLong(Clc.ReliableAcknowledge);

            // Determine how many cmds to send (1..3 most recent)
            int packetdup = CvarSystem.GetInt("cl_packetdup");
            if (packetdup < 0) packetdup = 0;
            if (packetdup > 5) packetdup = 5;
            int cmdCount = packetdup + 1;
            if (cmdCount > Cl.CmdNumber)
                cmdCount = Cl.CmdNumber;
            if (cmdCount < 1)
                cmdCount = 1;
            if (cmdCount > 3)
                cmdCount = 3;

            // clc_move opcode
            msg.WriteByte(1);
            msg.WriteByte(cmdCount);

            // Write delta-compressed cmds from oldest to newest
            UserCmd oldCmd = new UserCmd();
            int startCmd = Cl.CmdNumber - cmdCount;
            for (int i = 0; i < cmdCount; i++)
            {
                int idx = (startCmd + i + 1) & ClientConst.CMD_MASK;
                UserCmd cmd = Cl.Cmds[idx];
                msg.WriteDeltaUsercmd(oldCmd, cmd);
                oldCmd = cmd;
            }

            Clc.Netchan.Transmit(msg.CurSize, buf);
        }

        public static void CL_Frame(int msec)
        {
            if (msec < 0) msec = 0;
            if (msec > 200) msec = 200; // guard against large gaps

            _realtime += msec;

            if (!Cl.Paused)
                Cl.ServerTime += msec;

            CL_CheckForResend();
            CL_SendCmd();

            OnCLFrame?.Invoke(msec);
        }

        public static void CL_PacketEvent(byte[] data, int len)
        {
            if (data == null || len <= 0)
                return;

            byte[] buf = new byte[ClientConst.MAX_MSGLEN];
            int copyLen = len < buf.Length ? len : buf.Length;
            Array.Copy(data, buf, copyLen);

            NetMessage msg = new NetMessage(buf, buf.Length);
            msg.CurSize = copyLen;

            if (!Clc.Netchan.Process(msg))
                return;

            ClientParse.CL_ParseServerMessage(msg);
        }

        public static void CL_Shutdown()
        {
            CL_Disconnect("shutdown");
            Debug.Log("CL_Shutdown");
        }
    }
}
