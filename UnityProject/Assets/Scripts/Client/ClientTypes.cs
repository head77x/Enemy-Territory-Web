using ET.Network;

namespace ET.Client
{
    public enum ClientConnectionState
    {
        Disconnected, Connecting, Challenging, Connected, Loading, Primed, Active
    }

    public static class ClientConst
    {
        public const int MAX_RELIABLE_COMMANDS  = 64;
        public const int PACKET_BACKUP          = 32;
        public const int PACKET_MASK            = PACKET_BACKUP - 1;
        public const int CMD_BACKUP             = 64;
        public const int CMD_MASK               = CMD_BACKUP - 1;
        public const int MAX_PARSE_ENTITIES     = 2048;
        public const int MAX_SNAPSHOT_ENTITIES  = 256;
        public const int MAX_MSGLEN             = 32768;
        public const int SNAPFLAG_RATE_DELAYED  = 1;
        public const int SNAPFLAG_NOT_ACTIVE    = 2;
        public const int SNAPFLAG_SERVERCOUNT   = 4;
    }

    public class ClientSnapshot
    {
        public bool         Valid;
        public int          SnapFlags;
        public int          ServerTime;
        public int          MessageNum;
        public int          DeltaNum;
        public int          Ping;
        public PlayerState  Ps               = new PlayerState();
        public int          NumEntities;
        public int          ParseEntitiesNum;
        public int          ServerCommandNum;
        public byte[]       AreaBits         = new byte[32];
        public int          AreaBytes;
    }

    public class ClientConnection
    {
        public ClientConnectionState State                  = ClientConnectionState.Disconnected;
        public NetChannel            Netchan                = new NetChannel();
        public NetAddress            ServerAddress;
        public int                   Challenge;
        public int                   ClientNum;
        public int                   ChecksumFeed;
        public int                   ServerMessageSequence;
        public int                   ServerCommandSequence;
        public int                   LastExecutedServerCommand;
        public string[]              ServerCommands         = new string[ClientConst.MAX_RELIABLE_COMMANDS];
        public int                   ReliableSequence;
        public int                   ReliableAcknowledge;
        public string[]              ReliableCommands       = new string[ClientConst.MAX_RELIABLE_COMMANDS];
        public string                ServerInfo             = "";
        public string                GameState              = "";
        public bool                  DemoRecording;
        public bool                  DemoPlaying;
        public int                   TimeDelta;
    }

    public class ClientState
    {
        public ClientSnapshot[]  Snapshots          = new ClientSnapshot[ClientConst.PACKET_BACKUP];
        public ClientSnapshot    Snap               = new ClientSnapshot();
        public ClientSnapshot    OldSnap            = new ClientSnapshot();
        public EntityState[]     ParseEntities      = new EntityState[ClientConst.MAX_PARSE_ENTITIES];
        public UserCmd[]         Cmds               = new UserCmd[ClientConst.CMD_BACKUP];
        public int               CmdNumber;
        public int               ServerTime;
        public int               OldServerTime;
        public int               OldFrameServerTime;
        public int               ServerTimeDelta;
        public bool              ExtrapolatedSnapshot;
        public bool              NewSnapshots;
        public int               ParseEntitiesNum;
        public int               MouseDx, MouseDy;
        public float             ViewAngles0, ViewAngles1, ViewAngles2;
        public bool              Paused;
        public int               TimeSinceLastInput;

        public ClientState()
        {
            for (int i = 0; i < ClientConst.PACKET_BACKUP; i++)
                Snapshots[i] = new ClientSnapshot();
            for (int i = 0; i < ClientConst.MAX_PARSE_ENTITIES; i++)
                ParseEntities[i] = new EntityState();
            for (int i = 0; i < ClientConst.CMD_BACKUP; i++)
                Cmds[i] = new UserCmd();
        }
    }
}
