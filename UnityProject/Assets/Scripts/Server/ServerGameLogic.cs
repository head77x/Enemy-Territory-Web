// Ported from: src/game/g_main.c, src/game/g_active.c, src/game/g_client.c, src/game/g_spawn.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    public enum GameType
    {
        FFA            = 0,
        Tournament     = 1,
        Objective      = 2,
        TeamDeathmatch = 3,
        Stopwatch      = 5,
        Campaign       = 6,
        LastManStanding= 7,
    }

    public enum ClientSessionTeam
    {
        Free      = 0,
        Red       = 1,
        Blue      = 2,
        Spectator = 3,
    }

    public class WeaponStat
    {
        public int Hits;
        public int Atts;
        public int Kills;
        public int Deaths;
    }

    public class GEntity
    {
        public EntityState  S           = new EntityState();
        public bool         InUse;
        public string       ClassName;
        public Vector3      Origin;
        public Vector3      Angles;
        public Vector3      Mins;
        public Vector3      Maxs;
        public int          EntityNum;
        public int          Health;
        public int          MaxHealth;
        public int          Team;
        public int          SpawnFlags;
        public float        NextThink;
        public Action<GEntity>                    Think;
        public Action<GEntity, GEntity>           Touch;
        public Action<GEntity, GEntity, GEntity>  Use;
        public Action<GEntity, GEntity>           Pain;
        public Action<GEntity, GEntity, string>   Die;
        public GClient      Client;
        public GEntity      Enemy;
        public GEntity      Target;
        public string       TargetName;
        public string       TargetShaderName;
        public int          Timestamp;
        public float        Angle;
        public int          Count;
        public int          Damage;
        public int          SplashDamage;
        public int          SplashRadius;
        public int          MeansOfDeath;
        public bool         PhysicsObject;
        public float        PhysicsBounce;
        public int          Flags;
        public EntityScript Script;
    }

    public class GClient
    {
        public PlayerState   PS              = new PlayerState();
        public ClientPersist Pers            = new ClientPersist();
        public ClientSession Sess            = new ClientSession();
        public bool          ReadyToExit;
        public bool          Noclip;
        public int           LastCmdTime;
        public int           Buttons;
        public int           OldButtons;
        public int           LatchedButtons;
        public UserCmd       LastCmd         = new UserCmd();
        public int           RespawnTime;
        public int           RewardTime;
        public int           AirOutTime;
        public int           FireHeld;
        public int           LastKillTime;
        public float         KillerYaw;
        public bool          InactivityWarning;
        public int           InactivityTime;
        public int           VoiceChatTime;
        public int           VoiceChatCount;
        public int           DeathAnimTime;
        public int           DeathTime;
        public int           LastAmmoPickupTime;
        public int           SpawnTime;
        public int           XP;
    }

    public class ClientPersist
    {
        public string Name;
        public int    Team;
        public int    ClassType;
        public int    Weapon;
        public int    Score;
        public int    Deaths;
        public int    Kills;
        public int    CmdDebounce;
        public bool   Ready;
        public string NetName => Name;
        public int    CharacterIndex;
        public object Character;
        public GEntity WayPoint;
        public int    LastWaypointSetTime;
    }

    public class ClientSession
    {
        public int   SessionTeam;
        public int   Spectator;
        public int   SpectatorNum;
        public int   SpectatorClient;
        public int   Wins;
        public int   Losses;
        public int   TelePorted;
        public int   SpectatorState;
        public int   SpectatorTeam;
        public int   Referee;
        public int   SpecInvite;
        public int   PlayerType;
        public WeaponStat[] WeaponStats;

        public ClientSession()
        {
            WeaponStats = new WeaponStat[(int)WeaponStatIndex.MAX];
            for (int i = 0; i < WeaponStats.Length; i++)
                WeaponStats[i] = new WeaponStat();
        }
    }

    public class LevelLocals
    {
        public int       Time;
        public int       PreviousTime;
        public int       StartTime;
        public int       FrameTime;
        public int       NumSpawnVars;
        public string[,] SpawnVars       = new string[GameConst.MAX_GENTITIES, 2];
        public bool      Spawning;
        public int       NumEntities;
        public int       MaxClients;
        public GEntity[] Entities;
        public GClient[] Clients;
        public int       WarmupTime;
        public string    MapName;
        public int       TeamScores0;
        public int       TeamScores1;
        public int       LastTeamLocationTime;
        public bool      NewSession;
        public bool      RestartsAllowed;
        public int       GameState;
        public bool      VoteInProgress;
        public string    VoteString;
        public int       VoteYes;
        public int       VoteNo;
        public int       VoteTime;
        public int       NumVotingClients;
        public int       SuddenDeathBegin;
        public float     FrameStartTime;
        public int       MatchPause;
        public int       NumConnectedClients;
        public int[]     SortedClients = new int[GameConst.MAX_CLIENTS];
        public int       NumPlayingClients;
    }

    public static class ServerGameLogic
    {
        public const int MAX_GENTITIES = GameConst.MAX_GENTITIES;
        public const int MAX_CLIENTS   = GameConst.MAX_CLIENTS;

        // GameState constants (GS_*)
        public const int GS_PLAYING              = 0;
        public const int GS_WARMUP               = 1;
        public const int GS_WARMUP_COUNTDOWN     = 2;
        public const int GS_INTERMISSION         = 3;
        public const int GS_WAITING_FOR_PLAYERS  = 4;
        public const int GS_RESET                = 5;

        public static LevelLocals Level   = new LevelLocals();
        public static GEntity[]   Entities = new GEntity[MAX_GENTITIES];
        public static GClient[]   Clients  = new GClient[MAX_CLIENTS];

        // Events for inter-system communication
        public static event Action<int, string> OnClientConnect;
        public static event Action<int>         OnClientDisconnect;
        public static event Action<int>         OnClientBegin;
        public static event Action<int, UserCmd> OnClientUserCmd;
#pragma warning disable CS0067
        public static event Action<int>         OnClientDeath;
#pragma warning restore CS0067
        public static event Action              OnGameInit;
        public static event Action              OnGameShutdown;
        public static event Action<int>         OnRunFrame;

        private static readonly System.Random _rng = new System.Random();

        // Registered cvars — populated in G_InitGame
        private static Cvar _cvGravity;
        private static Cvar _cvSpeed;
        private static Cvar _cvTimelimit;
        private static Cvar _cvFraglimit;
        private static Cvar _cvWarmup;
        private static Cvar _cvInactivity;
        private static Cvar _cvFriendlyFire;
        private static Cvar _cvGametype;
        private static Cvar _cvMaxClients;

        // -----------------------------------------------------------------------
        // G_InitGame — called when a map loads (g_main.c: G_InitGame)
        // -----------------------------------------------------------------------
        public static void G_InitGame(int levelTime, int randomSeed, bool restart)
        {
            _cvGravity      = CvarSystem.Get("g_gravity",      "800",  CvarFlags.ServerInfo);
            _cvSpeed        = CvarSystem.Get("g_speed",        "320",  CvarFlags.ServerInfo);
            _cvTimelimit    = CvarSystem.Get("g_timelimit",    "0",    CvarFlags.ServerInfo | CvarFlags.Archive);
            _cvFraglimit    = CvarSystem.Get("g_fraglimit",    "0",    CvarFlags.ServerInfo | CvarFlags.Archive);
            _cvWarmup       = CvarSystem.Get("g_warmup",       "20",   CvarFlags.Archive);
            _cvInactivity   = CvarSystem.Get("g_inactivity",  "0",    CvarFlags.None);
            _cvFriendlyFire = CvarSystem.Get("g_friendlyFire","1",    CvarFlags.Archive);
            _cvGametype     = CvarSystem.Get("g_gametype",    "2",    CvarFlags.ServerInfo | CvarFlags.Latch);
            _cvMaxClients   = CvarSystem.Get("g_maxclients",  "20",   CvarFlags.ServerInfo | CvarFlags.Latch);

            Level = new LevelLocals();
            Level.Time        = levelTime;
            Level.StartTime   = levelTime;
            Level.MaxClients  = Math.Min(_cvMaxClients.IntValue, MAX_CLIENTS);
            Level.MapName     = CvarSystem.GetString("sv_mapname", "unknown");

            // Allocate entity and client arrays
            Entities = new GEntity[MAX_GENTITIES];
            Clients  = new GClient[MAX_CLIENTS];
            for (int i = 0; i < MAX_GENTITIES; i++)
            {
                Entities[i] = new GEntity { EntityNum = i };
            }
            for (int i = 0; i < Level.MaxClients; i++)
            {
                Clients[i]  = new GClient();
                // Link client to its entity
                Entities[i].Client = Clients[i];
            }

            Level.Entities  = Entities;
            Level.Clients   = Clients;

            // World entity always occupies slot ENTITYNUM_WORLD
            var worldEnt = Entities[GameConst.ENTITYNUM_WORLD];
            worldEnt.InUse    = true;
            worldEnt.ClassName = "worldspawn";

            // Warm-up state
            bool doWarmup = CvarSystem.GetInt("g_doWarmup") != 0;
            if (doWarmup && !restart)
            {
                Level.WarmupTime  = levelTime + _cvWarmup.IntValue * 1000;
                Level.GameState   = GS_WARMUP;
            }
            else
            {
                Level.GameState   = GS_PLAYING;
            }

            Level.RestartsAllowed = true;
            Level.NumEntities     = Level.MaxClients;

            SetConfigstring(ServerMain.CS_WOLFINFO,
                $"g_gametype={_cvGametype.IntValue}\\g_gravity={_cvGravity.IntValue}");

            if (restart)
                CvarSystem.Set("g_restarted", "1", force: true);

            // Wire up the hitscan/projectile dispatcher and health provider.
            HitscanSystem.Init();
            WeaponSystem.HealthProvider = idx =>
                ((uint)idx < (uint)Entities.Length && Entities[idx] != null)
                    ? Entities[idx].Health : 0;

            OnGameInit?.Invoke();
            Debug.Log($"[ServerGameLogic] G_InitGame level={Level.MapName} time={levelTime} restart={restart}");
        }

        // -----------------------------------------------------------------------
        // G_ShutdownGame — cleanup on map unload (g_main.c: G_ShutdownGame)
        // -----------------------------------------------------------------------
        public static void G_ShutdownGame(bool restart)
        {
            OnGameShutdown?.Invoke();

            for (int i = 0; i < Level.MaxClients; i++)
            {
                var ent = Entities[i];
                if (ent == null || !ent.InUse || ent.Client == null) continue;
                ClientDisconnect(i);
            }

            if (restart)
                CvarSystem.ApplyLatchedValues();

            Debug.Log($"[ServerGameLogic] G_ShutdownGame restart={restart}");
        }

        // -----------------------------------------------------------------------
        // G_RunFrame — main game frame (g_main.c: G_RunFrame)
        // -----------------------------------------------------------------------
        public static void G_RunFrame(int levelTime)
        {
            Level.PreviousTime = Level.Time;
            Level.Time         = levelTime;
            Level.FrameTime    = levelTime - Level.PreviousTime;

            // Entity thinks
            for (int i = 0; i < MAX_GENTITIES; i++)
            {
                var ent = Entities[i];
                if (ent == null || !ent.InUse) continue;
                if (ent.NextThink > 0f && ent.NextThink <= levelTime)
                    ent.Think?.Invoke(ent);
            }

            // Run all active clients
            for (int i = 0; i < Level.MaxClients; i++)
            {
                var ent = Entities[i];
                if (ent != null && ent.InUse)
                    G_RunClient(ent);
            }

            CheckExitRules();

            OnRunFrame?.Invoke(levelTime);
        }

        // -----------------------------------------------------------------------
        // Entity management (g_main.c / g_utils.c)
        // -----------------------------------------------------------------------
        public static GEntity G_Spawn()
        {
            // Slots 0..MaxClients-1 are reserved for players
            for (int i = Level.MaxClients; i < MAX_GENTITIES; i++)
            {
                var candidate = Entities[i];
                if (candidate == null)
                {
                    candidate = new GEntity { EntityNum = i };
                    Entities[i] = candidate;
                }
                if (candidate.InUse) continue;

                candidate.InUse       = true;
                candidate.ClassName   = "noclass";
                candidate.Health      = 0;
                candidate.Flags       = 0;
                candidate.NextThink   = 0f;
                candidate.Think       = null;
                candidate.Touch       = null;
                candidate.Use         = null;
                candidate.Pain        = null;
                candidate.Die         = null;
                candidate.Enemy       = null;
                candidate.Target      = null;
                candidate.TargetName  = null;
                candidate.S           = new EntityState();
                candidate.S.Number    = i;
                candidate.Client      = null;

                if (i >= Level.NumEntities)
                    Level.NumEntities = i + 1;

                return candidate;
            }

            Debug.LogError("[ServerGameLogic] G_Spawn: no free entity slots");
            return null;
        }

        public static void G_FreeEntity(GEntity ent)
        {
            if (ent == null) return;
            ent.InUse     = false;
            ent.ClassName = "freed";
            ent.Client    = null;
            ent.Think     = null;
            ent.Touch     = null;
            ent.Use       = null;
            ent.Pain      = null;
            ent.Die       = null;
            ent.Enemy     = null;
            ent.Target    = null;
            ent.NextThink = 0f;
        }

        public static GEntity G_TempEntity(Vector3 origin, int event_)
        {
            var ent = G_Spawn();
            if (ent == null) return null;

            ent.S.EType   = (int)EntityType.General;
            ent.S.Event   = event_;
            G_SetOrigin(ent, origin);

            // temp entities are freed next frame
            ent.NextThink = Level.Time + 1;
            ent.Think     = G_FreeEntity;

            return ent;
        }

        public static void G_Sound(GEntity ent, int channel, int soundIndex)
        {
            var speaker = G_TempEntity(ent.Origin, soundIndex);
            if (speaker == null) return;
            speaker.S.EType  = (int)EntityType.Speaker;
            speaker.S.OtherEntityNum = ent.EntityNum;
        }

        public static void G_SetOrigin(GEntity ent, Vector3 origin)
        {
            ent.Origin     = origin;
            ent.S.Pos.TrBase0 = origin.x;
            ent.S.Pos.TrBase1 = origin.y;
            ent.S.Pos.TrBase2 = origin.z;
        }

        public static void G_UseTargets(GEntity ent, GEntity activator)
        {
            if (ent == null || string.IsNullOrEmpty(ent.TargetName)) return;
            string name = ent.TargetName;

            for (int i = 0; i < Level.NumEntities; i++)
            {
                var target = Entities[i];
                if (target == null || !target.InUse) continue;
                if (target.TargetName != name) continue;
                target.Use?.Invoke(target, ent, activator);
            }
        }

        public static GEntity G_PickTarget(string targetName)
        {
            if (string.IsNullOrEmpty(targetName)) return null;

            var choices = new List<GEntity>(8);
            for (int i = 0; i < Level.NumEntities; i++)
            {
                var e = Entities[i];
                if (e != null && e.InUse && e.TargetName == targetName)
                    choices.Add(e);
            }
            if (choices.Count == 0) return null;
            return choices[_rng.Next(choices.Count)];
        }

        // -----------------------------------------------------------------------
        // g_active.c
        // -----------------------------------------------------------------------
        public static void ClientThink_real(GClient client, UserCmd cmd)
        {
            if (client == null) return;

            var ps = client.PS;

            // Inactivity check
            bool moving = cmd.ForwardMove != 0 || cmd.RightMove != 0
                       || cmd.UpMove != 0
                       || (cmd.Buttons & (Button.Attack | Button.Sprint)) != 0;
            int inactivityTime = _cvInactivity?.IntValue ?? 0;
            if (inactivityTime > 0)
            {
                if (moving)
                {
                    client.InactivityTime    = Level.Time + inactivityTime * 1000;
                    client.InactivityWarning = false;
                }
                else if (!client.InactivityWarning && Level.Time > client.InactivityTime - 10000)
                {
                    client.InactivityWarning = true;
                }
            }

            // Intermission — no movement processing
            if (Level.GameState == GS_INTERMISSION)
            {
                ClientIntermissionThink(client);
                return;
            }

            client.OldButtons     = client.Buttons;
            client.Buttons        = cmd.Buttons;
            client.LatchedButtons |= client.Buttons & ~client.OldButtons;

            // Weapon change request
            if (cmd.Weapon != ps.Weapon && cmd.Weapon != GameConst.WP_NONE)
            {
                // WeaponSystem handles the actual switch; just flag the request
                ps.NextWeapon = cmd.Weapon;
            }

            // Store last cmd for interpolation / animation
            client.LastCmd.CopyFrom(cmd);
            client.LastCmdTime = Level.Time;

            // Apply pmove gravity from cvar into playerstate so pmove can read it
            ps.Gravity = _cvGravity?.IntValue ?? 800;
            ps.Speed   = _cvSpeed?.IntValue   ?? 320;

            // Noclip shortcut
            if (client.Noclip)
                ps.PmType = GameConst.PM_NOCLIP;

            // Fire weapon if attack button is held and weapon ready
            bool attackDown = (cmd.Buttons & Button.Attack) != 0;
            if (attackDown && ps.PmType == GameConst.PM_NORMAL)
            {
                WeaponSystem.FireWeaponFromCmd(ps.ClientNum, ps, cmd);
                client.FireHeld = Level.Time;
            }

            ps.CommandTime = cmd.ServerTime;

            OnClientUserCmd?.Invoke(ps.ClientNum, cmd);
        }

        public static void G_RunClient(GEntity ent)
        {
            if (ent == null || ent.Client == null) return;
            var client = ent.Client;
            var ps     = client.PS;

            // Skip spectators and limbo
            if ((ps.PmFlags & GameConst.PMF_LIMBO) != 0) return;
            if (ps.PmType == GameConst.PM_SPECTATOR &&
                client.Sess.SessionTeam == (int)ClientSessionTeam.Spectator) return;

            // Respawn countdown
            if (!ent.InUse) return;
            if (ps.Stats[GameConst.STAT_HEALTH] <= 0 && Level.Time >= client.RespawnTime)
            {
                // Only auto-respawn in FFA/TDM; objective modes need explicit request
                int gt = _cvGametype?.IntValue ?? GameConst.GT_WOLF;
                bool autoRespawn = gt == GameConst.GT_SINGLE_PLAYER;
                if (autoRespawn)
                    ClientSpawn(ent);
            }

            ClientThink_real(client, client.LastCmd);

            int msec = Level.FrameTime;
            if (msec > 0)
                ClientTimerActions(ent, msec);
        }

        public static void ClientTimerActions(GEntity ent, int msec)
        {
            if (ent == null || ent.Client == null) return;
            var client = ent.Client;
            var ps     = client.PS;

            // Medic regeneration — 1 HP per second up to 125% of max
            bool isMedic = client.Pers.ClassType == GameConst.PC_MEDIC;
            if (isMedic && ps.Stats[GameConst.STAT_HEALTH] > 0)
            {
                int regenCap = (int)(client.Pers.ClassType == GameConst.PC_MEDIC ? 1.25f * MaxHealthForClient(client) : 0);
                if (regenCap > 0 && ps.Stats[GameConst.STAT_HEALTH] < regenCap)
                {
                    // regen every 1000ms
                    if (Level.Time - client.RewardTime > 1000)
                    {
                        ps.Stats[GameConst.STAT_HEALTH]++;
                        client.RewardTime = Level.Time;
                    }
                }
            }

            // Drowning — 2 HP per second when submerged past air supply
            if (ps.PmType != GameConst.PM_DEAD && ps.PmType != GameConst.PM_SPECTATOR)
            {
                if (client.AirOutTime != 0 && client.AirOutTime < Level.Time)
                {
                    WeaponSystem.G_Damage(ent.EntityNum, GameConst.ENTITYNUM_WORLD,
                        2, DamageSystem.DF_NO_LOCDAMAGE, 0);
                    client.AirOutTime = Level.Time + 1000;
                }
            }
        }

        public static void ClientIntermissionThink(GClient client)
        {
            if (client == null) return;
            // Any button press marks ready-to-exit during intermission
            if ((client.Buttons & (Button.Attack | Button.Activate)) != 0 &&
                (client.OldButtons & (Button.Attack | Button.Activate)) == 0)
            {
                client.ReadyToExit = true;
            }
        }

        // -----------------------------------------------------------------------
        // g_client.c
        // -----------------------------------------------------------------------
        public static string ClientConnect(int clientNum, bool firstTime, bool isBot)
        {
            if (clientNum < 0 || clientNum >= MAX_CLIENTS)
                return "bad clientNum";

            var ent    = Entities[clientNum];
            var client = Clients[clientNum];

            if (client == null)
            {
                client        = new GClient();
                Clients[clientNum] = client;
            }

            ent.Client = client;
            ent.InUse  = true;

            var ps = client.PS;
            ps.ClientNum = clientNum;

            // Preserve session data across map changes
            if (!firstTime)
            {
                // session already populated; retain team/score/etc.
            }
            else
            {
                client.Sess.SessionTeam      = (int)ClientSessionTeam.Spectator;
                client.Sess.SpectatorClient  = 0;
                client.Sess.SpectatorNum     = 0;
                client.Pers.Score            = 0;
                client.Pers.Deaths           = 0;
                client.Pers.Kills            = 0;
                client.Pers.ClassType        = GameConst.PC_SOLDIER;
            }

            client.InactivityTime    = Level.Time + (_cvInactivity?.IntValue ?? 0) * 1000;
            client.InactivityWarning = false;

            SetConfigstring(ServerMain.CS_MODELS + clientNum,
                isBot ? "bots/axis_pm" : "");

            OnClientConnect?.Invoke(clientNum, "");
            Debug.Log($"[ServerGameLogic] ClientConnect: client {clientNum} firstTime={firstTime} bot={isBot}");

            return null;
        }

        public static void ClientDisconnect(int clientNum)
        {
            if (clientNum < 0 || clientNum >= MAX_CLIENTS) return;

            var ent    = Entities[clientNum];
            var client = Clients[clientNum];
            if (client == null || !ent.InUse) return;

            OnClientDisconnect?.Invoke(clientNum);

            ent.InUse  = false;
            ent.Client = null;

            SetConfigstring(ServerMain.CS_MODELS + clientNum, "");
            Debug.Log($"[ServerGameLogic] ClientDisconnect: client {clientNum}");
        }

        public static void ClientBegin(int clientNum)
        {
            if (clientNum < 0 || clientNum >= MAX_CLIENTS) return;

            var ent    = Entities[clientNum];
            var client = ent.Client;
            if (client == null) return;

            // Move out of spectator if first begin after connecting
            if (client.Sess.SessionTeam == (int)ClientSessionTeam.Spectator)
            {
                // Auto-assign team in non-objective modes
                int gt = _cvGametype?.IntValue ?? GameConst.GT_WOLF;
                if (gt == GameConst.GT_COOP || gt == GameConst.GT_SINGLE_PLAYER)
                    client.Sess.SessionTeam = (int)ClientSessionTeam.Red;
            }

            ClientSpawn(ent);

            OnClientBegin?.Invoke(clientNum);
            Debug.Log($"[ServerGameLogic] ClientBegin: client {clientNum}");
        }

        public static void ClientUserinfoChanged(int clientNum, string userinfo)
        {
            if (clientNum < 0 || clientNum >= MAX_CLIENTS) return;
            var client = Clients[clientNum];
            if (client == null) return;

            string name = ETShared.Info_ValueForKey(userinfo, "name");
            if (string.IsNullOrEmpty(name)) name = $"Player{clientNum}";
            client.Pers.Name = name;

            int.TryParse(ETShared.Info_ValueForKey(userinfo, "team"), out int team);
            if (team >= 0 && team <= (int)ClientSessionTeam.Spectator)
                client.Sess.SessionTeam = team;

            int.TryParse(ETShared.Info_ValueForKey(userinfo, "playerclass"), out int cls);
            if (cls >= GameConst.PC_SOLDIER && cls <= GameConst.PC_COVERTOPS)
                client.Pers.ClassType = cls;

            // Broadcast updated name config string
            SetConfigstring(ServerMain.CS_MODELS + clientNum, name);
        }

        public static void ClientSpawn(GEntity ent)
        {
            if (ent == null || ent.Client == null) return;

            var client = ent.Client;
            var ps     = client.PS;
            int clientNum = ent.EntityNum;

            // Find spawn point
            Vector3 origin = Vector3.zero;
            Vector3 angles = Vector3.zero;
            var spawnPoint = SelectSpawnPoint(Vector3.zero, client.Sess.SessionTeam,
                ref origin, ref angles);

            // Reset player state for spawn
            int savedScore = client.Pers.Score;
            int savedKills  = client.Pers.Kills;
            int savedDeaths = client.Pers.Deaths;
            int savedClass  = client.Pers.ClassType;
            int savedTeam   = client.Sess.SessionTeam;

            ps.PmType  = GameConst.PM_NORMAL;
            ps.PmFlags = 0;
            ps.PmTime  = 0;
            ps.EFlags  = 0;
            ps.Gravity = _cvGravity?.IntValue ?? 800;
            ps.Speed   = _cvSpeed?.IntValue   ?? 320;
            ps.RunSpeedScale    = 1.0f;
            ps.SprintSpeedScale = 1.1f;
            ps.CrouchSpeedScale = 0.5f;

            int maxHp = MaxHealthForClient(client);
            ps.Stats[GameConst.STAT_HEALTH]       = (short)maxHp;
            ps.Stats[GameConst.STAT_PLAYER_CLASS]  = (short)savedClass;
            ps.Persistant[GameConst.PERS_SCORE]    = (short)savedScore;
            ps.Persistant[GameConst.PERS_KILLED]   = (short)savedDeaths;
            ps.TeamNum = savedTeam;
            ps.ClientNum = clientNum;
            ps.Weapon  = DefaultWeaponForClass(savedClass, savedTeam);

            // Position
            ps.Origin0 = origin.x;
            ps.Origin1 = origin.y;
            ps.Origin2 = origin.z;
            ps.ViewAngles0 = angles.x;
            ps.ViewAngles1 = angles.y;
            ps.ViewAngles2 = angles.z;
            ps.ViewHeight       = GameConst.DEFAULT_VIEWHEIGHT;
            ps.StandViewHeight  = GameConst.DEFAULT_VIEWHEIGHT;
            ps.CrouchViewHeight = GameConst.CROUCH_VIEWHEIGHT;
            ps.DeadViewHeight   = GameConst.DEAD_VIEWHEIGHT;
            ps.CrouchMaxZ       = GameConst.CROUCH_VIEWHEIGHT;   // maxs.z when crouching

            ps.Mins0 = -15f; ps.Mins1 = -15f; ps.Mins2 = -24f;
            ps.Maxs0 =  15f; ps.Maxs1 =  15f; ps.Maxs2 =  32f;

            // Sync entity state
            ent.Health    = maxHp;
            ent.MaxHealth = maxHp;
            ent.Origin    = origin;
            ent.Angles    = angles;
            ent.InUse     = true;

            G_SetOrigin(ent, origin);

            client.SpawnTime   = Level.Time;
            client.RespawnTime = Level.Time;
            client.Pers.Kills  = savedKills;
            client.Pers.Deaths = savedDeaths;

            // Add a small invulnerability window after spawn (300ms)
            client.RewardTime  = Level.Time + 300;

            ps.Persistant[GameConst.PERS_SPAWN_COUNT]++;

            Debug.Log($"[ServerGameLogic] ClientSpawn: client {clientNum} at {origin}");
        }

        public static GEntity SelectSpawnPoint(Vector3 avoidPoint, int team,
            ref Vector3 origin, ref Vector3 angles)
        {
            GEntity best     = null;
            float   bestDist = -1f;

            for (int i = 0; i < Level.NumEntities; i++)
            {
                var e = Entities[i];
                if (e == null || !e.InUse) continue;

                bool isTeamSpawn  = e.ClassName == "info_player_axis"
                                 || e.ClassName == "info_player_allies";
                bool isGenSpawn   = e.ClassName == "info_player_deathmatch"
                                 || e.ClassName == "info_player_start";

                if (!isTeamSpawn && !isGenSpawn) continue;

                if (isTeamSpawn)
                {
                    if (team == DamageSystem.TEAM_AXIS && e.ClassName != "info_player_axis")
                        continue;
                    if (team == DamageSystem.TEAM_ALLIES && e.ClassName != "info_player_allies")
                        continue;
                }

                float dist = (e.Origin - avoidPoint).sqrMagnitude;
                if (bestDist < 0f || dist > bestDist)
                {
                    bestDist = dist;
                    best     = e;
                }
            }

            if (best != null)
            {
                origin = best.Origin;
                // bbox Mins.z = -24, so we need offset > 24 to place the bbox bottom
                // above the floor.  PhysX AllSolid blocks all movement when inside
                // solid, unlike the original ET engine which handles shallow overlaps.
                origin.z += 36f;
                angles = best.Angles;
            }

            return best;
        }

        // -----------------------------------------------------------------------
        // g_spawn.c — spawn variable helpers
        // -----------------------------------------------------------------------
        public static bool G_SpawnString(string key, string defaultStr, out string value)
        {
            for (int i = 0; i < Level.NumSpawnVars; i++)
            {
                if (Level.SpawnVars[i, 0] == key)
                {
                    value = Level.SpawnVars[i, 1];
                    return true;
                }
            }
            value = defaultStr;
            return false;
        }

        public static bool G_SpawnFloat(string key, string defaultStr, out float value)
        {
            G_SpawnString(key, defaultStr, out string s);
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
                return true;
            value = 0f;
            return false;
        }

        public static bool G_SpawnInt(string key, string defaultStr, out int value)
        {
            G_SpawnString(key, defaultStr, out string s);
            if (int.TryParse(s, out value))
                return true;
            value = 0;
            return false;
        }

        public static bool G_SpawnVector(string key, string defaultStr, out Vector3 value)
        {
            G_SpawnString(key, defaultStr, out string s);
            value = Vector3.zero;
            if (string.IsNullOrEmpty(s)) return false;

            var parts = s.Split(' ');
            if (parts.Length < 3) return false;

            float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float x);
            float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y);
            float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float z);

            value = new Vector3(x, y, z);
            return true;
        }

        public static bool G_ParseSpawnVars(ref int parsePos, string entityString)
        {
            if (parsePos >= entityString.Length) return false;

            // Skip to opening brace
            while (parsePos < entityString.Length && entityString[parsePos] != '{')
                parsePos++;

            if (parsePos >= entityString.Length) return false;
            parsePos++; // consume '{'

            Level.NumSpawnVars = 0;
            Level.SpawnVars    = new string[GameConst.MAX_GENTITIES, 2];

            while (parsePos < entityString.Length)
            {
                SkipWhitespace(entityString, ref parsePos);
                if (parsePos >= entityString.Length) break;

                if (entityString[parsePos] == '}')
                {
                    parsePos++;
                    return true;
                }

                string key   = ParseToken(entityString, ref parsePos);
                string val   = ParseToken(entityString, ref parsePos);

                if (string.IsNullOrEmpty(key)) break;

                if (Level.NumSpawnVars < GameConst.MAX_GENTITIES)
                {
                    Level.SpawnVars[Level.NumSpawnVars, 0] = key;
                    Level.SpawnVars[Level.NumSpawnVars, 1] = val;
                    Level.NumSpawnVars++;
                }
            }

            return Level.NumSpawnVars > 0;
        }

        public static GEntity G_SpawnGEntityFromSpawnVars()
        {
            var ent = G_Spawn();
            if (ent == null) return null;

            G_SpawnString("classname", "noclass", out ent.ClassName);
            G_SpawnString("targetname", "", out ent.TargetName);
            G_SpawnString("target", "", out string target);
            if (!string.IsNullOrEmpty(target))
                ent.TargetName = target;

            G_SpawnVector("origin", "0 0 0", out ent.Origin);
            G_SpawnVector("angles", "0 0 0", out ent.Angles);
            G_SpawnFloat("angle", "0", out ent.Angle);
            G_SpawnInt("spawnflags", "0", out ent.SpawnFlags);
            G_SpawnInt("health", "0", out ent.Health);
            G_SpawnInt("damage", "0", out ent.Damage);
            G_SpawnInt("splashdamage", "0", out ent.SplashDamage);
            G_SpawnInt("splashradius", "0", out ent.SplashRadius);
            G_SpawnInt("count", "0", out ent.Count);
            G_SpawnInt("team", "0", out ent.Team);

            G_SetOrigin(ent, ent.Origin);

            G_CallSpawn(ent);

            return ent;
        }

        public static void G_SpawnEntitiesFromString(string entityString)
        {
            Level.Spawning = true;
            int parsePos   = 0;

            while (G_ParseSpawnVars(ref parsePos, entityString))
                G_SpawnGEntityFromSpawnVars();

            Level.Spawning = false;
            Debug.Log($"[ServerGameLogic] Spawned {Level.NumEntities} entities from BSP string");
        }

        public static void G_CallSpawn(GEntity ent)
        {
            if (ent == null) return;

            switch (ent.ClassName)
            {
                case "worldspawn":
                    SP_worldspawn(ent);
                    break;
                case "info_player_deathmatch":
                case "info_player_start":
                    SP_info_player_deathmatch(ent);
                    break;
                case "info_player_axis":
                case "info_player_allies":
                    SP_info_player_team(ent);
                    break;
                case "target_kill":
                    SP_target_kill(ent);
                    break;
                case "trigger_hurt":
                    SP_trigger_hurt(ent);
                    break;
                case "func_door":
                case "func_rotating":
                    // Geometry movers — MoverSystem handles simulation;
                    // we only need the entity in place so it can be linked.
                    ent.S.EType = (int)EntityType.Mover;
                    break;
                default:
                    break;
            }
        }

        // -----------------------------------------------------------------------
        // Spawn functions
        // -----------------------------------------------------------------------
        private static void SP_worldspawn(GEntity ent)
        {
            G_SpawnString("message", "", out string message);
            G_SpawnFloat("gravity", "800", out float gravity);
            G_SpawnString("music", "", out string music);

            CvarSystem.Set("g_gravity", gravity.ToString(
                System.Globalization.CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(music))
                SetConfigstring(ServerMain.CS_MUSIC, music);

            if (!string.IsNullOrEmpty(message))
                SetConfigstring(ServerMain.CS_MESSAGE, message);

            ent.S.EType = (int)EntityType.General;
        }

        private static void SP_info_player_deathmatch(GEntity ent)
        {
            ent.S.EType = (int)EntityType.General;
            // No special logic required beyond being positioned correctly
        }

        private static void SP_info_player_team(GEntity ent)
        {
            ent.S.EType = (int)EntityType.General;
            // Team side is encoded in ClassName; SelectSpawnPoint reads it
        }

        private static void SP_target_kill(GEntity ent)
        {
            ent.Use = (self, other, activator) =>
            {
                if (activator == null) return;
                int hp = activator.Health;
                if (hp > 0)
                {
                    // Deal lethal damage; DamageSystem event chain handles the kill
                    WeaponSystem.G_Damage(activator.EntityNum, self.EntityNum,
                        hp + 1, DamageSystem.DF_NO_LOCDAMAGE, 0);
                }
            };
        }

        private static void SP_trigger_hurt(GEntity ent)
        {
            G_SpawnInt("dmg", "5", out ent.Damage);

            ent.Touch = (self, other) =>
            {
                if (other == null || other.Health <= 0) return;
                WeaponSystem.G_Damage(other.EntityNum, self.EntityNum,
                    self.Damage, 0, DamageSystem.DF_NO_LOCDAMAGE);
            };
        }

        // -----------------------------------------------------------------------
        // Game flow helpers
        // -----------------------------------------------------------------------
        private static void CheckExitRules()
        {
            if (Level.GameState == GS_INTERMISSION) return;

            int timelimit = _cvTimelimit?.IntValue ?? 0;
            if (timelimit > 0)
            {
                int elapsed = Level.Time - Level.StartTime;
                if (elapsed >= timelimit * 60000)
                {
                    BeginIntermission("Time limit hit");
                    return;
                }
            }

            // Warm-up countdown transition
            if (Level.GameState == GS_WARMUP)
            {
                if (Level.Time >= Level.WarmupTime)
                {
                    Level.GameState = GS_PLAYING;
                    Level.StartTime = Level.Time;
                    SetConfigstring(ServerMain.CS_WARMUP, "");
                }
            }
        }

        private static void BeginIntermission(string reason)
        {
            if (Level.GameState == GS_INTERMISSION) return;

            Level.GameState = GS_INTERMISSION;
            SetConfigstring(ServerMain.CS_INTERMISSION, "1");

            Debug.Log($"[ServerGameLogic] Intermission: {reason}");
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------
        private static int MaxHealthForClient(GClient client)
        {
            return client.Pers.ClassType == GameConst.PC_MEDIC ? 125 : 100;
        }

        private static int DefaultWeaponForClass(int classType, int team)
        {
            bool axis = team == DamageSystem.TEAM_AXIS;
            switch (classType)
            {
                case GameConst.PC_SOLDIER:   return axis ? GameConst.WP_MP40       : GameConst.WP_THOMPSON;
                case GameConst.PC_MEDIC:     return axis ? GameConst.WP_MP40       : GameConst.WP_THOMPSON;
                case GameConst.PC_ENGINEER:  return axis ? GameConst.WP_KAR98      : GameConst.WP_CARBINE;
                case GameConst.PC_FIELDOPS:  return axis ? GameConst.WP_MP40       : GameConst.WP_THOMPSON;
                case GameConst.PC_COVERTOPS: return axis ? GameConst.WP_FG42       : GameConst.WP_STEN;
                default:                     return GameConst.WP_KNIFE;
            }
        }

        private static void SetConfigstring(int index, string value)
        {
            ServerMain.SV_SetConfigstring(index, value);
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos]))
                pos++;
            // skip // comments
            if (pos < s.Length - 1 && s[pos] == '/' && s[pos + 1] == '/')
            {
                while (pos < s.Length && s[pos] != '\n')
                    pos++;
            }
        }

        private static string ParseToken(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) return "";

            bool quoted = s[pos] == '"';
            if (quoted) pos++;

            int start = pos;
            if (quoted)
            {
                while (pos < s.Length && s[pos] != '"')
                    pos++;
                string result = s.Substring(start, pos - start);
                if (pos < s.Length) pos++; // consume closing quote
                return result;
            }
            else
            {
                while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}')
                    pos++;
                return s.Substring(start, pos - start);
            }
        }

        // GetClientUserinfo — returns the userinfo string for a client slot
        public static string GetClientUserinfo(int clientNum)
        {
            if (clientNum < 0 || clientNum >= MAX_CLIENTS) return "";
            return ServerMain.Svs.Clients[clientNum]?.UserInfo ?? "";
        }

        public static void ClientPrint(GEntity ent, string msg)
        {
            if (ent == null) return;
            SendServerCommand(ent.EntityNum, msg);
        }

        public static void AllPrint(string msg)
        {
            for (int i = 0; i < Level.MaxClients; i++)
            {
                var e = Entities[i];
                if (e != null && e.InUse && e.Client != null)
                    SendServerCommand(i, msg);
            }
        }

        public static string GetConfigstring(int index)
        {
            return ServerMain.SV_GetConfigstring(index);
        }

        public static void SendServerCommand(int clientNum, string cmd)
        {
            if (clientNum < 0 || clientNum >= MAX_CLIENTS) return;
            var cl = ServerMain.Svs.Clients[clientNum];
            if (cl != null)
                ServerMain.SV_AddServerCommand(cl, cmd);
        }

        public static int G_ModelIndex(string model)
        {
            if (string.IsNullOrEmpty(model)) return 0;
            return ServerMain.SV_ModelIndex(model);
        }
    }
}
