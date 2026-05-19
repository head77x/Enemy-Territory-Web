// Ported from: src/server/server.h
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    public static class ServerConst
    {
        public const int MAX_CONFIGSTRINGS       = 1024;
        public const int MAX_MODELS              = 256;
        public const int MAX_SOUNDS              = 256;
        public const int MAX_CLIENTS             = 64;
        public const int MAX_MASTER_SERVERS      = 8;
        public const int MAX_CHALLENGES          = 1024;
        public const int MAX_TEMPBAN_ADDRESSES   = MAX_CLIENTS;
        public const int PACKET_BACKUP           = 32;   // number of old messages that must be kept on client+server
        public const int PACKET_MASK             = PACKET_BACKUP - 1;
        public const int MAX_PACKET_USERCMDS     = 32;   // max number of usercmd_t in a packet
        public const int MAX_SNAPSHOT_ENTITIES   = 256;
        public const int MAX_ENT_CLUSTERS        = 16;
        public const int MAX_MAP_AREA_BYTES      = 32;   // bit vector of area visibility
        public const int MAX_RELIABLE_COMMANDS   = 64;   // max string commands buffered for restransmit
        public const int MAX_BPS_WINDOW          = 20;
        public const int AUTHORIZE_TIMEOUT       = 5000;
        public const int SERVER_PERFORMANCECOUNTER_FRAMES  = 600;
        public const int SERVER_PERFORMANCECOUNTER_SAMPLES = 6;
        public const int MIN_RATE                = 1000;
        public const int DEFAULT_RATE            = 10000;
        public const int SNAPFLAG_RATE_DELAYED   = 1;
        public const int SNAPFLAG_NOT_ACTIVE     = 2;
        public const int SNAPFLAG_SERVERCOUNT    = 4;
    }

    // -----------------------------------------------------------------------
    // Server state machine
    // -----------------------------------------------------------------------
    public enum ServerState
    {
        Dead,       // SS_DEAD — no map loaded
        Loading,    // SS_LOADING — spawning level entities
        Game,       // SS_GAME — actively running
    }

    // -----------------------------------------------------------------------
    // Client connection state
    // -----------------------------------------------------------------------
    public enum ClientState
    {
        Free,       // CS_FREE — can be reused
        Zombie,     // CS_ZOMBIE — disconnected, hold for a bit
        Connected,  // CS_CONNECTED — assigned but no gamestate yet
        Primed,     // CS_PRIMED — gamestate sent, waiting for first usercmd
        Active,     // CS_ACTIVE — fully in game
    }

    // -----------------------------------------------------------------------
    // svEntity_t — server-side entity (PVS tracking)
    // -----------------------------------------------------------------------
    public sealed class SvEntity
    {
        public int   NumClusters;
        public int[] ClusterNums = new int[ServerConst.MAX_ENT_CLUSTERS];
        public int   LastCluster;
        public int   AreaNum, AreaNum2;
        public int   SnapshotCounter;
        public int   OriginCluster;
        public EntityState Baseline = new EntityState();
    }

    // -----------------------------------------------------------------------
    // clientSnapshot_t — one built frame for a client
    // -----------------------------------------------------------------------
    public sealed class ClientSnapshot
    {
        public byte[] AreaBits = new byte[ServerConst.MAX_MAP_AREA_BYTES];
        public int    AreaBytes;
        public PlayerState Ps = new PlayerState();
        public int    NumEntities;
        public int    FirstEntity;     // index into ServerStatic.SnapshotEntities[]
        public int    MessageSent;
        public int    MessageAcked;
        public int    MessageSize;
    }

    // -----------------------------------------------------------------------
    // challenge_t — DDoS protection
    // -----------------------------------------------------------------------
    public struct Challenge
    {
        public NetAddress Adr;
        public int        ChallengeNum;
        public int        Time;
        public int        PingTime;
        public int        FirstTime;
        public int        FirstPing;
        public bool       Connected;
    }

    // -----------------------------------------------------------------------
    // tempBan_t
    // -----------------------------------------------------------------------
    public struct TempBan
    {
        public NetAddress Adr;
        public int        EndTime;
    }

    // -----------------------------------------------------------------------
    // netchan_buffer_t — queued outgoing fragmented messages
    // -----------------------------------------------------------------------
    public sealed class NetchanBuffer
    {
        public NetMessage       Msg;
        public string           LastClientCommandString;
        public NetchanBuffer    Next;
    }

    // -----------------------------------------------------------------------
    // client_t — one connected (or recently disconnected) client
    // -----------------------------------------------------------------------
    public sealed class ServerClient
    {
        public ClientState State;
        public string      UserInfo = "";

        // reliable commands queue
        public string[] ReliableCommands = new string[ServerConst.MAX_RELIABLE_COMMANDS];
        public int      ReliableSequence;
        public int      ReliableAcknowledge;
        public int      ReliableSent;

        public int MessageAcknowledge;

        // binary message support
        public byte[] BinaryMessage = new byte[1024];
        public int    BinaryMessageLength;
        public bool   BinaryMessageOverflowed;

        public int   GamestateMessageNum;
        public int   Challenge;

        public UserCmd     LastUsercmd = new UserCmd();
        public int         LastMessageNum;
        public int         LastClientCommand;
        public string      LastClientCommandString = "";
        public int         EntityNum;     // index into sv.Entities[]
        public string      Name = "";

        // download state
        public string DownloadName = "";
        public int    DownloadSize;
        public int    DownloadCount;
        public int    DownloadClientBlock;
        public int    DownloadCurrentBlock;
        public int    DownloadXmitBlock;
        public bool   DownloadEOF;
        public int    DownloadSendTime;

        // www download
        public bool   BDlOK;
        public string DownloadURL = "";
        public bool   BWWWDl;
        public bool   BWWWing;
        public bool   BFallback;

        // timing
        public int    DeltaMessage;
        public int    NextReliableTime;
        public int    LastPacketTime;
        public int    LastConnectTime;
        public int    NextSnapshotTime;
        public bool   RateDelayed;
        public int    TimeoutCount;

        public ClientSnapshot[] Frames = new ClientSnapshot[ServerConst.PACKET_BACKUP];

        public int  Ping;
        public int  Rate;
        public int  SnapshotMsec;
        public int  PureAuthentic;
        public bool GotCP;

        public NetChannel Netchan = new NetChannel();

        // outgoing fragmented message queue
        public NetchanBuffer NetchanStartQueue;
        public NetchanBuffer NetchanEndQueue;

        public int DownloadNotify;

        // Convenience properties
        public bool   IsBot    { get; set; }
        public bool   Active   => State == ClientState.Active;
        public bool   Connected => State >= ClientState.Connected;
        public string Address  => Netchan?.RemoteAddress?.ToString() ?? "loopback";

        public ServerClient()
        {
            for (int i = 0; i < ServerConst.PACKET_BACKUP; i++)
                Frames[i] = new ClientSnapshot();
        }
    }

    // -----------------------------------------------------------------------
    // server_t — per-level server state (cleared on map change)
    // -----------------------------------------------------------------------
    public sealed class ServerState_t
    {
        public ServerState State;
        public bool        Restarting;
        public int         ServerId;
        public int         RestartedServerId;
        public int         ChecksumFeed;
        public int         ChecksumFeedServerId;
        public int         SnapshotCounter;
        public int         TimeResidual;
        public int         NextFrameTime;

        public string[]    Configstrings          = new string[ServerConst.MAX_CONFIGSTRINGS];
        public bool[]      ConfigstringsModified  = new bool [ServerConst.MAX_CONFIGSTRINGS];

        public SvEntity[]  SvEntities = new SvEntity[GameConst.MAX_GENTITIES];

        // entities (set by game logic each frame)
        public EntityState[]  Entities     = new EntityState[GameConst.MAX_GENTITIES];
        public int            NumEntities;

        // player states (set by game logic each frame via pmove)
        public PlayerState[]  GameClients  = new PlayerState[GameConst.MAX_CLIENTS];

        public int RestartTime;

        // net debugging
        public int[] BpsWindow  = new int[ServerConst.MAX_BPS_WINDOW];
        public int   BpsWindowSteps;
        public int   BpsTotalBytes;
        public int   BpsMaxBytes;
        public int[] UbpsWindow = new int[ServerConst.MAX_BPS_WINDOW];
        public int   UbpsTotalBytes;
        public int   UbpsMaxBytes;
        public float UcompAve;
        public int   UcompNum;

        public ServerState_t()
        {
            for (int i = 0; i < GameConst.MAX_GENTITIES; i++)
            {
                SvEntities[i] = new SvEntity();
                Entities[i]   = new EntityState();
            }
            for (int i = 0; i < GameConst.MAX_CLIENTS; i++)
                GameClients[i] = new PlayerState();
        }
    }

    // -----------------------------------------------------------------------
    // serverStatic_t — persists across map changes
    // -----------------------------------------------------------------------
    public sealed class ServerStatic
    {
        public bool Initialized;
        public int  Time;           // monotonically increasing server time
        public int  SnapFlagServerBit;

        public ServerClient[] Clients;
        public int            NumSnapshotEntities;
        public int            NextSnapshotEntities;
        public EntityState[]  SnapshotEntities;

        public int NextHeartbeatTime;

        public Challenge[] Challenges = new Challenge[ServerConst.MAX_CHALLENGES];
        public NetAddress  RedirectAddress;

        public TempBan[]   TempBanAddresses = new TempBan[ServerConst.MAX_TEMPBAN_ADDRESSES];

        public int[]  SampleTimes = new int[ServerConst.SERVER_PERFORMANCECOUNTER_SAMPLES];
        public int    CurrentSampleIndex;
        public int    TotalFrameTime;
        public int    CurrentFrameIndex;
        public int    ServerLoad;

        public void Init(int maxClients)
        {
            Initialized = true;
            int numSnapshot = maxClients * ServerConst.PACKET_BACKUP * ServerConst.MAX_SNAPSHOT_ENTITIES;
            Clients = new ServerClient[maxClients];
            for (int i = 0; i < maxClients; i++)
                Clients[i] = new ServerClient();

            NumSnapshotEntities  = numSnapshot;
            NextSnapshotEntities = 0;
            SnapshotEntities     = new EntityState[numSnapshot];
            for (int i = 0; i < numSnapshot; i++)
                SnapshotEntities[i] = new EntityState();
        }
    }
}
