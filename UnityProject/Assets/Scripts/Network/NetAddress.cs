// Ported from: src/qcommon/qcommon.h  (netadr_t, netsrc_t, netadrtype_t)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Net;

namespace ET.Network
{
    public enum NetAddressType
    {
        Bot,
        Bad,       // address lookup failed
        Loopback,
        Broadcast,
        IP,
        IPX,
        BroadcastIPX,
    }

    public enum NetSrc
    {
        Client,
        Server,
    }

    public sealed class NetAddress
    {
        public NetAddressType Type;
        public byte[]         IP  = new byte[4];
        public byte[]         IPX = new byte[10];
        public ushort         Port;

        public NetAddress() { }

        public NetAddress(NetAddressType type, byte[] ip, ushort port)
        {
            Type = type;
            if (ip != null) Buffer.BlockCopy(ip, 0, IP, 0, Math.Min(ip.Length, 4));
            Port = port;
        }

        public static NetAddress Loopback() => new NetAddress { Type = NetAddressType.Loopback };

        // Parse — parses "ip:port" or "ip" strings into a NetAddress
        public static NetAddress Parse(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (address == "localhost" || address == "loopback")
                return Loopback();
            try
            {
                int colonIdx = address.LastIndexOf(':');
                string host = colonIdx >= 0 ? address.Substring(0, colonIdx) : address;
                ushort port = colonIdx >= 0 && ushort.TryParse(address.Substring(colonIdx + 1), out var p)
                    ? p : (ushort)27960;
                var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(host), port);
                return FromEndPoint(ep);
            }
            catch { return null; }
        }

        public static NetAddress FromEndPoint(IPEndPoint ep)
        {
            var a = new NetAddress { Type = NetAddressType.IP, Port = (ushort)ep.Port };
            byte[] bytes = ep.Address.GetAddressBytes();
            Buffer.BlockCopy(bytes, 0, a.IP, 0, Math.Min(bytes.Length, 4));
            return a;
        }

        public IPEndPoint ToEndPoint()
        {
            var addr = new IPAddress(IP);
            return new IPEndPoint(addr, Port);
        }

        public void CopyFrom(NetAddress src)
        {
            Type = src.Type;
            Buffer.BlockCopy(src.IP,  0, IP,  0, 4);
            Buffer.BlockCopy(src.IPX, 0, IPX, 0, 10);
            Port = src.Port;
        }

        public bool CompareBase(NetAddress b)
        {
            if (Type != b.Type) return false;
            if (Type == NetAddressType.Loopback) return true;
            if (Type == NetAddressType.IP)
                return IP[0]==b.IP[0] && IP[1]==b.IP[1] && IP[2]==b.IP[2] && IP[3]==b.IP[3];
            if (Type == NetAddressType.IPX)
            {
                for (int i = 0; i < 10; i++) if (IPX[i] != b.IPX[i]) return false;
                return true;
            }
            return false;
        }

        public bool Compare(NetAddress b)
        {
            if (!CompareBase(b)) return false;
            if (Type == NetAddressType.Loopback) return true;
            return Port == b.Port;
        }

        public bool IsLocal => Type == NetAddressType.Loopback;

        public override string ToString()
        {
            if (Type == NetAddressType.Loopback)  return "loopback";
            if (Type == NetAddressType.Bot)        return "bot";
            if (Type == NetAddressType.IP)
                return $"{IP[0]}.{IP[1]}.{IP[2]}.{IP[3]}:{(ushort)IPAddress.NetworkToHostOrder((short)Port)}";
            return $"{BitConverter.ToString(IPX).Replace("-","").ToLower().Insert(8,".")}:{(ushort)IPAddress.NetworkToHostOrder((short)Port)}";
        }
    }
}
