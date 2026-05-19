// Ported from: src/server/sv_snapshot.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    /// <summary>
    /// Snapshot building and transmission (sv_snapshot.c port).
    ///
    /// Packet format per snapshot:
    ///   4 bytes  sequence (netchan)
    ///   1 byte   svc_snapshot (0x06)
    ///   4 bytes  serverTime
    ///   1 byte   deltaFrame (lastframe index)
    ///   1 byte   snapFlags
    ///   1 byte   areabytes
    ///   N bytes  areabits
    ///   delta    playerstate
    ///   delta    packet entities
    /// </summary>
    public static class ServerSnapshot
    {
        // svc_* message opcodes
        public const int SVC_BAD         = 0;
        public const int SVC_NOP         = 1;
        public const int SVC_GAMESTATE   = 2;
        public const int SVC_CONFIGSTRING= 3;
        public const int SVC_BASELINE    = 4;
        public const int SVC_SERVERCOMMAND = 5;
        public const int SVC_SNAPSHOT    = 6;
        public const int SVC_DOWNLOAD    = 7;

        // -----------------------------------------------------------------------
        // SV_BuildClientSnapshot — fill frame.NumEntities from visible entities
        // -----------------------------------------------------------------------
        private static void SV_BuildClientSnapshot(ServerClient cl, int clientNum,
            ClientSnapshot frame)
        {
            var sv  = ServerMain.Sv;
            var svs = ServerMain.Svs;

            // copy player state
            if (clientNum < sv.GameClients.Length)
                frame.Ps.CopyFrom(sv.GameClients[clientNum]);

            // populate entities visible to this client
            // Full PVS/portal logic from sv_snapshot.c:SV_AddEntitiesVisibleFromPoint
            // is stubbed here — all non-self entities are included (safe default)
            frame.FirstEntity = svs.NextSnapshotEntities;
            frame.NumEntities = 0;

            for (int i = 0; i < sv.NumEntities; i++)
            {
                if (i == clientNum) continue;  // skip self (sent as playerstate)
                var ent = sv.Entities[i];
                if (ent.EType == 0) continue;

                int idx = svs.NextSnapshotEntities % svs.NumSnapshotEntities;
                svs.SnapshotEntities[idx].CopyFrom(ent);
                svs.NextSnapshotEntities++;
                frame.NumEntities++;

                if (frame.NumEntities >= ServerConst.MAX_SNAPSHOT_ENTITIES) break;
            }

            // area bits — all visible (no PVS culling yet)
            frame.AreaBytes = ServerConst.MAX_MAP_AREA_BYTES;
            for (int i = 0; i < frame.AreaBytes; i++)
                frame.AreaBits[i] = 0xFF;
        }

        // -----------------------------------------------------------------------
        // SV_EmitPacketEntities — write delta entity list into msg
        // -----------------------------------------------------------------------
        private static void SV_EmitPacketEntities(
            ClientSnapshot from, ClientSnapshot to, NetMessage msg)
        {
            var svs          = ServerMain.Svs;
            int fromEntities = from?.NumEntities ?? 0;
            int toEntities   = to.NumEntities;

            int oldIndex = 0, newIndex = 0;

            while (newIndex < toEntities || oldIndex < fromEntities)
            {
                int oldNum, newNum;
                EntityState oldEnt = null, newEnt = null;

                if (newIndex >= toEntities)
                {
                    newNum = 9999;
                }
                else
                {
                    int ni = (to.FirstEntity + newIndex) % svs.NumSnapshotEntities;
                    newEnt = svs.SnapshotEntities[ni];
                    newNum = newEnt.Number;
                }

                if (from == null || oldIndex >= fromEntities)
                {
                    oldNum = 9999;
                }
                else
                {
                    int oi = (from.FirstEntity + oldIndex) % svs.NumSnapshotEntities;
                    oldEnt = svs.SnapshotEntities[oi];
                    oldNum = oldEnt.Number;
                }

                if (newNum == oldNum)
                {
                    msg.WriteDeltaEntity(oldEnt, newEnt, false);
                    oldIndex++;
                    newIndex++;
                }
                else if (newNum < oldNum)
                {
                    // new entity not seen before — send with null baseline
                    var baseline = ServerMain.Sv.SvEntities[newNum].Baseline;
                    msg.WriteDeltaEntity(baseline, newEnt, true);
                    newIndex++;
                }
                else  // oldNum < newNum — entity removed
                {
                    msg.WriteDeltaEntity(oldEnt, null, true);
                    oldIndex++;
                }
            }

            // end-of-entities sentinel
            msg.WriteBits(GameConst.MAX_GENTITIES - 1, NetConst.GENTITYNUM_BITS);
        }

        // -----------------------------------------------------------------------
        // SV_WriteSnapshotToClient — serialise snapshot into msg
        // -----------------------------------------------------------------------
        private static void SV_WriteSnapshotToClient(ServerClient cl, int clientNum,
            NetMessage msg)
        {
            var svs = ServerMain.Svs;

            int outSeq       = cl.Netchan.OutgoingSequence;
            int frameIndex   = outSeq & ServerConst.PACKET_MASK;
            var frame        = cl.Frames[frameIndex];

            SV_BuildClientSnapshot(cl, clientNum, frame);
            frame.MessageSent = svs.Time;

            // choose delta base frame
            ClientSnapshot oldframe = null;
            int lastframe = 0;

            if (cl.DeltaMessage > 0 && cl.State == ClientState.Active)
            {
                int age = outSeq - cl.DeltaMessage;
                if (age < ServerConst.PACKET_BACKUP - 3)
                {
                    oldframe  = cl.Frames[cl.DeltaMessage & ServerConst.PACKET_MASK];
                    lastframe = age;
                    if (oldframe.FirstEntity <= svs.NextSnapshotEntities - svs.NumSnapshotEntities)
                    {
                        oldframe  = null;
                        lastframe = 0;
                    }
                }
            }

            // write header
            msg.WriteByte(SVC_SNAPSHOT);
            msg.WriteLong(svs.Time);
            msg.WriteByte(lastframe);

            int snapFlags = svs.SnapFlagServerBit;
            if (cl.RateDelayed)            snapFlags |= ServerConst.SNAPFLAG_RATE_DELAYED;
            if (cl.State != ClientState.Active) snapFlags |= ServerConst.SNAPFLAG_NOT_ACTIVE;
            msg.WriteByte(snapFlags);

            msg.WriteByte(frame.AreaBytes);
            msg.WriteData(frame.AreaBits, frame.AreaBytes);

            // playerstate delta
            msg.WriteDeltaPlayerState(
                oldframe?.Ps,
                frame.Ps);

            // entity deltas
            SV_EmitPacketEntities(oldframe, frame, msg);
        }

        // -----------------------------------------------------------------------
        // SV_UpdateServerCommandsToClient — resend unack'd reliable commands
        // -----------------------------------------------------------------------
        public static void SV_UpdateServerCommandsToClient(ServerClient cl, NetMessage msg)
        {
            for (int i = cl.ReliableAcknowledge + 1; i <= cl.ReliableSequence; i++)
            {
                int idx = i & (ServerConst.MAX_RELIABLE_COMMANDS - 1);
                msg.WriteByte(SVC_SERVERCOMMAND);
                msg.WriteLong(i);
                msg.WriteString(cl.ReliableCommands[idx] ?? "");
            }
        }

        // -----------------------------------------------------------------------
        // SV_SendClientGameState — send the initial gamestate packet
        // -----------------------------------------------------------------------
        public static void SV_SendClientGameState(ServerClient cl, int clientNum)
        {
            if (cl.State == ClientState.Free || cl.State == ClientState.Zombie)
                return;

            var sv  = ServerMain.Sv;
            var buf = new byte[NetConst.MAX_MSGLEN];
            var msg = new NetMessage(buf, buf.Length);

            SV_UpdateServerCommandsToClient(cl, msg);
            SV_WriteSnapshotToClient(cl, clientNum, msg);

            cl.Netchan.Transmit(msg.CurSize, buf);
            cl.Frames[cl.Netchan.OutgoingSequence & ServerConst.PACKET_MASK]
                .MessageSent = ServerMain.Svs.Time;
        }
    }
}
