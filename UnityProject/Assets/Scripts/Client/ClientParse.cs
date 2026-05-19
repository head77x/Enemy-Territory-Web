using System;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    public static class ClientParse
    {
        // SVC opcodes (matching server)
        private const int SVC_NOP           = 1;
        private const int SVC_GAMESTATE     = 2;
        private const int SVC_CONFIGSTRING  = 3;
        private const int SVC_BASELINE      = 4;
        private const int SVC_SERVERCOMMAND = 5;
        private const int SVC_SNAPSHOT      = 6;
        private const int SVC_DOWNLOAD      = 7;

        public static event Action<int, int>       OnGamestateReceived;
        public static event Action<ClientSnapshot> OnSnapshotParsed;
        public static event Action<string>         OnServerCommand;

        public static void CL_ParseServerMessage(NetMessage msg)
        {
            if (msg == null || msg.Overflowed)
            {
                Debug.LogWarning("CL_ParseServerMessage: bad message");
                return;
            }

            // Reliable acknowledge
            ClientMain.Clc.ReliableAcknowledge = msg.ReadLong();

            while (true)
            {
                if (msg.Overflowed)
                {
                    Debug.LogWarning("CL_ParseServerMessage: message overflowed");
                    break;
                }

                int cmd = msg.ReadByte();
                if (cmd == -1)
                    break;

                switch (cmd)
                {
                    case SVC_NOP:
                        break;

                    case SVC_GAMESTATE:
                        CL_ParseGamestate(msg);
                        break;

                    case SVC_CONFIGSTRING:
                        CL_ParseConfigstring(msg);
                        break;

                    case SVC_BASELINE:
                        CL_ParseBaseline(msg);
                        break;

                    case SVC_SERVERCOMMAND:
                        CL_ParseServerCommand(msg);
                        break;

                    case SVC_SNAPSHOT:
                        CL_ParseSnapshot(msg);
                        break;

                    case SVC_DOWNLOAD:
                        // Download handling not implemented in this port
                        Debug.Log("CL_ParseServerMessage: SVC_DOWNLOAD ignored");
                        return;

                    default:
                        Debug.LogWarning("CL_ParseServerMessage: unknown opcode " + cmd);
                        return;
                }
            }
        }

        private static void CL_ParseSnapshot(NetMessage msg)
        {
            ClientSnapshot newSnap = new ClientSnapshot();

            newSnap.ServerTime  = (int)msg.ReadLong();
            newSnap.DeltaNum    = msg.ReadByte();
            newSnap.SnapFlags   = msg.ReadByte();
            newSnap.MessageNum  = ClientMain.Clc.ServerMessageSequence;

            newSnap.AreaBytes = msg.ReadByte();
            for (int i = 0; i < newSnap.AreaBytes; i++)
                newSnap.AreaBits[i] = (byte)msg.ReadByte();

            // Delta playerstate — base is old snapshot ps or null for full update
            PlayerState oldPs = null;
            if (newSnap.DeltaNum != 0)
            {
                ClientSnapshot deltaBase = ClientMain.Cl.Snapshots[newSnap.DeltaNum & ClientConst.PACKET_MASK];
                if (deltaBase.Valid && deltaBase.ServerTime == newSnap.DeltaNum)
                    oldPs = deltaBase.Ps;
            }
            msg.ReadDeltaPlayerstate(oldPs, newSnap.Ps);

            // Packet entities
            int parseEntitiesNum = ClientMain.Cl.ParseEntitiesNum;
            newSnap.ParseEntitiesNum = parseEntitiesNum;
            newSnap.NumEntities = 0;

            while (true)
            {
                // Read next entity from stream
                // We use a temporary entity and rely on ReadDeltaEntity
                // Entity number sentinel == MAX_GENTITIES-1 signals end of list
                int newNum = ReadEntityNum(msg);
                if (newNum == GameConst.MAX_GENTITIES - 1)
                    break;

                if (msg.Overflowed)
                    break;

                int entIdx = parseEntitiesNum & (ClientConst.MAX_PARSE_ENTITIES - 1);
                EntityState oldEnt = ClientMain.Cl.ParseEntities[entIdx];
                EntityState newEnt = new EntityState();

                msg.ReadDeltaEntity(oldEnt, newEnt, newNum);

                ClientMain.Cl.ParseEntities[parseEntitiesNum & (ClientConst.MAX_PARSE_ENTITIES - 1)] = newEnt;
                parseEntitiesNum++;
                newSnap.NumEntities++;
            }

            ClientMain.Cl.ParseEntitiesNum = parseEntitiesNum;

            newSnap.ServerCommandNum = ClientMain.Clc.ServerCommandSequence;
            newSnap.Valid = true;

            // Store in ring
            int snapIdx = newSnap.MessageNum & ClientConst.PACKET_MASK;
            ClientMain.Cl.Snapshots[snapIdx] = newSnap;

            ClientMain.Cl.OldSnap = ClientMain.Cl.Snap;
            ClientMain.Cl.Snap    = newSnap;
            ClientMain.Cl.NewSnapshots = true;

            OnSnapshotParsed?.Invoke(newSnap);
        }

        private static void CL_ParseGamestate(NetMessage msg)
        {
            // Server sends its reliable sequence so we can ack
            ClientMain.Clc.ServerCommandSequence = msg.ReadLong();

            // Loop reading embedded configstrings and baselines
            while (true)
            {
                int cmd = msg.ReadByte();
                if (cmd == -1 || cmd == SVC_SERVERCOMMAND)
                    break;

                if (cmd == SVC_CONFIGSTRING)
                {
                    int index = msg.ReadShort();
                    string val = msg.ReadBigString();
                    // Store raw — higher layer can interpret
                    _ = index;
                    _ = val;
                }
                else if (cmd == SVC_BASELINE)
                {
                    // Read a baseline entity into the parse entity ring
                    int entNum = ReadEntityNum(msg);
                    int entIdx = entNum & (ClientConst.MAX_PARSE_ENTITIES - 1);
                    EntityState baseline = ClientMain.Cl.ParseEntities[entIdx];
                    EntityState newBaseline = new EntityState();
                    msg.ReadDeltaEntity(baseline, newBaseline, entNum);
                    ClientMain.Cl.ParseEntities[entIdx] = newBaseline;
                }
                else
                {
                    Debug.LogWarning("CL_ParseGamestate: unexpected cmd " + cmd);
                    break;
                }
            }

            ClientMain.Clc.ClientNum     = msg.ReadLong();
            ClientMain.Clc.ChecksumFeed  = msg.ReadLong();

            ClientMain.Clc.State = ClientConnectionState.Loading;

            OnGamestateReceived?.Invoke(ClientMain.Clc.ClientNum, ClientMain.Clc.ChecksumFeed);
        }

        private static void CL_ParseServerCommand(NetMessage msg)
        {
            int seq = msg.ReadLong();
            string text = msg.ReadString();

            if (seq <= ClientMain.Clc.LastExecutedServerCommand)
                return;

            if (seq > ClientMain.Clc.ServerCommandSequence)
                ClientMain.Clc.ServerCommandSequence = seq;

            int idx = seq & (ClientConst.MAX_RELIABLE_COMMANDS - 1);
            ClientMain.Clc.ServerCommands[idx] = text;
            ClientMain.Clc.LastExecutedServerCommand = seq;

            OnServerCommand?.Invoke(text);
        }

        private static void CL_ParseConfigstring(NetMessage msg)
        {
            int index  = msg.ReadShort();
            string val = msg.ReadBigString();
            // Configstring storage belongs to a higher layer (e.g. cgame)
            _ = index;
            _ = val;
        }

        private static void CL_ParseBaseline(NetMessage msg)
        {
            int entNum = ReadEntityNum(msg);
            int entIdx = entNum & (ClientConst.MAX_PARSE_ENTITIES - 1);
            EntityState old = ClientMain.Cl.ParseEntities[entIdx];
            EntityState updated = new EntityState();
            msg.ReadDeltaEntity(old, updated, entNum);
            ClientMain.Cl.ParseEntities[entIdx] = updated;
        }

        // Read an entity number encoded in GENTITYNUM_BITS
        // NetMessage exposes the constant as NetMessage.GENTITYNUM_BITS if available;
        // fall back to reading a short and masking to 10 bits (1023 max vanilla ET).
        private static int ReadEntityNum(NetMessage msg)
        {
            // ET uses 10 bits for entity numbers (MAX_GENTITIES = 1024)
            return msg.ReadShort() & (GameConst.MAX_GENTITIES - 1);
        }
    }
}
