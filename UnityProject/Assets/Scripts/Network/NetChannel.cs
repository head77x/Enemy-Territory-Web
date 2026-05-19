// Ported from: src/qcommon/net_chan.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// UDP channel with sequence numbers, packet fragmentation, and loopback support.
// Replaces the platform-specific Sys_SendPacket with System.Net.Sockets.UdpClient.

using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace ET.Network
{
    public sealed class NetChannel
    {
        // -------------------------------------------------------
        // Constants (mirrors net_chan.c defines)
        // -------------------------------------------------------
        public const int MAX_PACKETLEN    = 1400;
        public const int FRAGMENT_SIZE    = MAX_PACKETLEN - 100;
        public const int PACKET_HEADER   = 10;
        public const int MAX_MSGLEN      = 32768;
        private const int FRAGMENT_BIT   = unchecked((int)0x80000000);
        private const int MAX_LOOPBACK   = 16;

        // -------------------------------------------------------
        // State (mirrors netchan_t)
        // -------------------------------------------------------
        public NetSrc     Sock            { get; private set; }
        public int        Dropped         { get; private set; }
        public NetAddress RemoteAddress   { get; private set; }
        public int        QPort           { get; private set; }

        public int IncomingSequence       { get; private set; }
        public int OutgoingSequence       { get; private set; }

        // incoming fragment reassembly
        private int    _fragmentSequence;
        private int    _fragmentLength;
        private byte[] _fragmentBuffer = new byte[MAX_MSGLEN];

        // outgoing fragment queue
        private bool   _unsentFragments;
        private int    _unsentFragmentStart;
        private int    _unsentLength;
        private byte[] _unsentBuffer = new byte[MAX_MSGLEN];

        // -------------------------------------------------------
        // Loopback (shared static, mirrors loopbacks[2])
        // -------------------------------------------------------
        private struct LoopMsg { public byte[] Data; public int DataLen; }
        private class Loopback
        {
            public LoopMsg[] Msgs = new LoopMsg[MAX_LOOPBACK];
            public int Get, Send;
        }
        private static readonly Loopback[] _loopbacks = { new Loopback(), new Loopback() };

        // -------------------------------------------------------
        // Setup
        // -------------------------------------------------------
        public void Setup(NetSrc sock, NetAddress remote, int qport)
        {
            Sock              = sock;
            RemoteAddress     = remote;
            QPort             = qport;
            IncomingSequence  = 0;
            OutgoingSequence  = 1;
            _fragmentSequence = 0;
            _fragmentLength   = 0;
            _unsentFragments  = false;
        }

        // -------------------------------------------------------
        // Transmit
        // -------------------------------------------------------

        /// <summary>Send data, fragmenting if necessary. Mirrors Netchan_Transmit.</summary>
        public void Transmit(int length, byte[] data, UdpClient socket = null)
        {
            if (length > MAX_MSGLEN)
                throw new Exception($"NetChannel.Transmit: oversized message ({length} bytes)");

            _unsentFragmentStart = 0;
            if (length >= FRAGMENT_SIZE)
            {
                _unsentFragments = true;
                _unsentLength    = length;
                Buffer.BlockCopy(data, 0, _unsentBuffer, 0, length);
                TransmitNextFragment(socket);
                return;
            }

            byte[] send = new byte[MAX_PACKETLEN];
            var msg = new NetMessage(send, MAX_PACKETLEN) { Oob = true };
            msg.WriteLong(OutgoingSequence);
            OutgoingSequence++;

            if (Sock == NetSrc.Client)
                msg.WriteShort(QPort);

            msg.WriteData(data, length);

            SendRaw(Sock, msg.CurSize, send, RemoteAddress, socket);
        }

        /// <summary>Send one pending fragment. Mirrors Netchan_TransmitNextFragment.</summary>
        public void TransmitNextFragment(UdpClient socket = null)
        {
            byte[] send = new byte[MAX_PACKETLEN];
            var msg = new NetMessage(send, MAX_PACKETLEN) { Oob = true };

            msg.WriteLong(OutgoingSequence | FRAGMENT_BIT);
            if (Sock == NetSrc.Client)
                msg.WriteShort(QPort);

            int fragLen = FRAGMENT_SIZE;
            if (_unsentFragmentStart + fragLen > _unsentLength)
                fragLen = _unsentLength - _unsentFragmentStart;

            msg.WriteShort(_unsentFragmentStart);
            msg.WriteShort(fragLen);
            byte[] fragSlice = new byte[fragLen];
            Buffer.BlockCopy(_unsentBuffer, _unsentFragmentStart, fragSlice, 0, fragLen);
            msg.WriteData(fragSlice, fragLen);

            SendRaw(Sock, msg.CurSize, send, RemoteAddress, socket);

            _unsentFragmentStart += fragLen;

            if (_unsentFragmentStart == _unsentLength && fragLen != FRAGMENT_SIZE)
            {
                OutgoingSequence++;
                _unsentFragments = false;
            }
        }

        public bool HasPendingFragments => _unsentFragments;

        // -------------------------------------------------------
        // Process  (mirrors Netchan_Process)
        // -------------------------------------------------------

        /// <summary>
        /// Process an incoming packet.
        /// Returns true if the message payload is ready for consumption.
        /// msg.Data/CurSize are updated on reassembly.
        /// </summary>
        public bool Process(NetMessage msg)
        {
            msg.BeginReadingOOB();
            int sequence = msg.ReadLong();

            bool fragmented = (sequence & FRAGMENT_BIT) != 0;
            if (fragmented) sequence &= ~FRAGMENT_BIT;

            int fragmentStart = 0, fragmentLength = 0;

            if (Sock == NetSrc.Server)
                msg.ReadShort(); // consume qport

            if (fragmented)
            {
                fragmentStart  = msg.ReadShort();
                fragmentLength = msg.ReadShort();
            }

            // discard out-of-order or duplicates
            if (sequence <= IncomingSequence)
            {
                Debug.Log($"NetChannel: out-of-order packet {sequence} at {IncomingSequence} from {RemoteAddress}");
                return false;
            }

            Dropped = sequence - (IncomingSequence + 1);

            if (fragmented)
            {
                if (sequence != _fragmentSequence)
                {
                    _fragmentSequence = sequence;
                    _fragmentLength   = 0;
                }

                if (fragmentStart != _fragmentLength)
                {
                    Debug.Log($"NetChannel: dropped fragment (seq={sequence})");
                    return false;
                }

                if (fragmentLength < 0 ||
                    msg.ReadCount + fragmentLength > msg.CurSize ||
                    _fragmentLength + fragmentLength > _fragmentBuffer.Length)
                {
                    Debug.LogWarning("NetChannel: illegal fragment length");
                    return false;
                }

                Buffer.BlockCopy(msg.Data, msg.ReadCount, _fragmentBuffer, _fragmentLength, fragmentLength);
                _fragmentLength += fragmentLength;

                if (fragmentLength == FRAGMENT_SIZE)
                    return false; // more fragments coming

                if (_fragmentLength > msg.MaxSize)
                {
                    Debug.LogWarning($"NetChannel: fragmentLength {_fragmentLength} > maxSize");
                    return false;
                }

                // Reassemble: write sequence int back, then copy fragment buffer
                BitConverter.TryWriteBytes(new Span<byte>(msg.Data, 0, 4),
                    IPAddress.HostToNetworkOrder(sequence));
                Buffer.BlockCopy(_fragmentBuffer, 0, msg.Data, 4, _fragmentLength);
                msg.CurSize    = _fragmentLength + 4;
                _fragmentLength = 0;
                msg.ReadCount  = 4;
                msg.Bit        = 32;

                IncomingSequence = sequence;
                return true;
            }

            IncomingSequence = sequence;
            return true;
        }

        // -------------------------------------------------------
        // Loopback (mirrors NET_SendLoopPacket / NET_GetLoopPacket)
        // -------------------------------------------------------
        public static void SendLoopPacket(NetSrc sock, int length, byte[] data)
        {
            var lb = _loopbacks[(int)sock ^ 1];
            int i  = lb.Send & (MAX_LOOPBACK - 1);
            lb.Msgs[i].Data    = new byte[length];
            lb.Msgs[i].DataLen = length;
            Buffer.BlockCopy(data, 0, lb.Msgs[i].Data, 0, length);
            lb.Send++;
        }

        public static bool GetLoopPacket(NetSrc sock, out NetAddress from, NetMessage msg)
        {
            from = null;
            var lb = _loopbacks[(int)sock];

            if (lb.Send - lb.Get > MAX_LOOPBACK)
                lb.Get = lb.Send - MAX_LOOPBACK;

            if (lb.Get >= lb.Send)
                return false;

            int i = lb.Get & (MAX_LOOPBACK - 1);
            lb.Get++;

            Buffer.BlockCopy(lb.Msgs[i].Data, 0, msg.Data, 0, lb.Msgs[i].DataLen);
            msg.CurSize = lb.Msgs[i].DataLen;
            from        = NetAddress.Loopback();
            return true;
        }

        // -------------------------------------------------------
        // Raw UDP send (mirrors NET_SendPacket / Sys_SendPacket)
        // -------------------------------------------------------
        public static void SendRaw(NetSrc sock, int length, byte[] data,
                                   NetAddress to, UdpClient socket)
        {
            if (to.Type == NetAddressType.Loopback)
            {
                SendLoopPacket(sock, length, data);
                return;
            }

            if (socket == null) return;

            try
            {
                var ep = to.ToEndPoint();
                socket.Send(data, length, ep);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"NetChannel.SendRaw: {e.Message}");
            }
        }

        // -------------------------------------------------------
        // OOB helpers
        // -------------------------------------------------------

        /// <summary>Send a connectionless out-of-band packet with a string payload.</summary>
        public static void SendOOBPrint(NetSrc sock, NetAddress to, string message,
                                        UdpClient socket = null)
        {
            byte[] data = NetMessage.BuildOOBPrint(message);
            SendRaw(sock, data.Length, data, to, socket);
        }

        /// <summary>
        /// Parse an incoming packet: returns true if it is an OOB packet
        /// (first 4 bytes are 0xFF). Leaves msg.ReadCount pointing past the header.
        /// </summary>
        public static bool IsOOB(NetMessage msg)
        {
            if (msg.CurSize < 4) return false;
            return msg.Data[0] == 0xFF && msg.Data[1] == 0xFF
                && msg.Data[2] == 0xFF && msg.Data[3] == 0xFF;
        }
    }
}
