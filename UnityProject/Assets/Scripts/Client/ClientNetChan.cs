// Ported from: src/client/cl_net_chan.c
using System.Net.Sockets;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    /// <summary>
    /// Thin client-side wrapper around NetChannel.
    /// Mirrors cl_net_chan.c: handles pending-fragment priority and provides
    /// a hook point for demo recording (stubbed in this port).
    /// </summary>
    public static class ClientNetChan
    {
        // CL_Netchan_Transmit
        // If the channel still has fragments queued from the previous packet,
        // drain one fragment and drop the new data for this frame (matches C behaviour).
        // Otherwise transmit the new data as a regular (possibly fragmented) packet.
        public static void CL_Netchan_Transmit(NetChannel chan, int length, byte[] data,
            UdpClient socket = null)
        {
            if (chan == null)
            {
                Debug.LogWarning("CL_Netchan_Transmit: null channel");
                return;
            }

            if (chan.HasPendingFragments)
            {
                chan.TransmitNextFragment(socket);
                return;
            }

            // Demo recording hook would go here in the original C code.
            // Stub: no demo recording in this port.

            chan.Transmit(length, data, socket);
        }

        // CL_Netchan_TransmitNextFragment
        // Sends the next queued fragment on the channel.
        // Called by the client loop when HasPendingFragments is true and we need
        // to keep draining without sending new data.
        public static void CL_Netchan_TransmitNextFragment(NetChannel chan, UdpClient socket = null)
        {
            if (chan == null)
            {
                Debug.LogWarning("CL_Netchan_TransmitNextFragment: null channel");
                return;
            }

            chan.TransmitNextFragment(socket);
        }

        // CL_Netchan_Process
        // Validates and reassembles an incoming packet through the netchan layer.
        // Returns true when the packet is complete and ready for the message parser.
        // In C, accepted packets are also written to the demo file; that path is
        // stubbed here — the caller (ClientMain.CL_PacketEvent) handles parsing.
        public static bool CL_Netchan_Process(NetChannel chan, NetMessage msg)
        {
            if (chan == null)
            {
                Debug.LogWarning("CL_Netchan_Process: null channel");
                return false;
            }

            if (msg == null)
            {
                Debug.LogWarning("CL_Netchan_Process: null message");
                return false;
            }

            bool accepted = chan.Process(msg);

            // Demo recording hook would go here in the original C code.
            // Stub: no demo recording in this port.

            return accepted;
        }
    }
}
