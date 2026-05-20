// Integration MonoBehaviour — wires all ported ET systems together.
// Attach to an empty GameObject in the scene as the game entry point.
// Layer 8 — Integration

using System;
using System.IO;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;
using ET.Server;
using ET.Client;
using ET.BotAI;

namespace ET.App
{
    /// <summary>
    /// Top-level integration MonoBehaviour for the ET→Unity port.
    /// Initialises all subsystems (FileSystem, CvarSystem, CmdSystem, AudioSystem,
    /// ServerMain, ClientMain, BotAI) and drives their per-frame updates.
    /// Attach once to an empty GameObject in the startup scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class ETGameManager : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // Inspector-configurable fields
        // ----------------------------------------------------------------

        [Header("Server Settings")]
        public string   MapName        = "goldrush";
        public int      MaxClients     = 8;
        public int      BotCount       = 4;
        public BotSkill BotSkillLevel  = BotSkill.Medium;

        [Header("Network")]
        public bool   StartServer    = true;
        public bool   StartClient    = true;
        public string ServerAddress  = "127.0.0.1:27960";

        [Header("Audio")]
        public float  MasterVolume   = 1f;

        // ----------------------------------------------------------------
        // Local player state
        // ----------------------------------------------------------------
        // Whether the local player has been spawned and is actively playing
        public static bool LocalPlayerActive { get; private set; }

        private const int LocalClientNum = 0;

        // Camera yaw/pitch accumulated from mouse input while in-game
        private float _camYaw;
        private float _camPitch;
        private const float MouseSens = 0.15f;

        // Pmove reused each server frame (avoids per-frame alloc)
        private readonly PlayerMovement _pm = new PlayerMovement();
        private PmoveInput _pmInput;

        // ET content mask: CONTENTS_SOLID | CONTENTS_PLAYERCLIP | CONTENTS_BODY
        private const int MASK_PLAYERSOLID = 1 | 0x10000 | 0x2000;

        // ----------------------------------------------------------------
        // MonoBehaviour lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            // ---- Wire CommonSystem delegates (avoids circular assembly deps) ----
            CommonSystem.CvarSetDelegate     = (k, v) => CvarSystem.Set(k, v);
            CommonSystem.CvarGetIntDelegate  = (k) => CvarSystem.GetInt(k);
            CommonSystem.CvarWriteDelegate   = CvarSystem.WriteToString;
            CommonSystem.ConsolePrintDelegate= ClientConsole.Print;
            CommonSystem.KeyEventDelegate    = KeySystem.OnKeyEvent;
            CommonSystem.WriteBindingsDelegate = KeySystem.WriteBindings;

            // ---- File system ----
            string etBase = Path.Combine(Application.streamingAssetsPath, "etmain");
            FileSystem.FS_AddGameDirectory(etBase);

            // ---- Cvars ----
            CvarSystem.Set("sv_maxclients", MaxClients.ToString());
            CvarSystem.Set("sv_fps",        "20");
            CvarSystem.Set("sv_timeout",    "30");
            CvarSystem.Set("g_gametype",    "2");   // GT_WOLF

            // ---- Console commands ----
            CmdSystem.Cmd_AddCommand("map",
                args => ServerInit.SV_SpawnServer(args.Length > 1 ? args[1] : MapName));
            CmdSystem.Cmd_AddCommand("disconnect",
                args => ClientMain.CL_Disconnect("user"));
            CmdSystem.Cmd_AddCommand("quit",
                args => Application.Quit());

            // ---- Audio ----
            AudioSystem.S_SetMasterVolume(MasterVolume);

            // ---- Wire runtime resource loaders (breaks ET.Game → Assembly-CSharp dep) ----
            AudioSystem.RuntimeAudioLoader = RuntimeResourceLoader.LoadAudioClip;

            // ---- Allocate pmove input once ----
            _pmInput = new PmoveInput
            {
                TraceMask     = MASK_PLAYERSOLID,
                Trace         = CollisionSystem.DefaultTraceFunc,
                PointContents = CollisionSystem.DefaultPointContentsFunc,
                PmoveFixed    = 0,
            };
        }

        // Root transform that holds the loaded BSP scene.
        private Transform _mapRoot;

        private void OnMapSpawn(string mapName)
        {
            // Destroy previous map geometry
            if (_mapRoot != null)
            {
                Destroy(_mapRoot.gameObject);
                RuntimeResourceLoader.ClearCaches();
            }

            var go = RuntimeResourceLoader.LoadBspScene(mapName, transform);
            _mapRoot = go != null ? go.transform : null;

            if (go == null)
                Debug.LogError($"[ETGameManager] Failed to load BSP for map '{mapName}'.");
            else
                Debug.Log($"[ETGameManager] Map '{mapName}' loaded into scene.");

            // Initialise the game-logic layer now that the map is loaded.
            // G_InitGame allocates entity/client arrays; must come before G_SpawnEntities.
            ServerGameLogic.G_InitGame(ServerMain.Svs.Time, UnityEngine.Random.Range(0, int.MaxValue), false);

            // Spawn BSP entities (spawn points, doors, etc.) from the BSP entity string
            string entityStr = RuntimeResourceLoader.LastBspEntityString;
            if (!string.IsNullOrEmpty(entityStr))
                ServerGameLogic.G_SpawnEntitiesFromString(entityStr);
            else
                Debug.LogWarning("[ETGameManager] BSP entity string is empty — no spawn points will exist.");
        }

        private void Start()
        {
            // Ensure the UI manager is present on this GameObject
            if (GetComponent<ETUIManager>() == null)
                gameObject.AddComponent<ETUIManager>();

            // Ensure a camera exists and is configured for ET map scale
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 1f;
            cam.farClipPlane  = 16000f;
            cam.transform.position = new Vector3(0f, 100f, 0f);

            if (StartServer)
            {
                ServerInit.SV_Init();

                // Subscribe BEFORE SV_SpawnServer so the BSP and G_InitGame run first
                ServerInit.OnSpawnServer += OnMapSpawn;

                // SV_SpawnServer fires OnSpawnServer synchronously:
                //   → OnMapSpawn → G_InitGame + G_SpawnEntitiesFromString
                ServerInit.SV_SpawnServer(MapName);

                // Subscribe server-frame and client-think events
                ServerMain.OnGameRunFrame    += OnGameRunFrame;
                SV_Client.OnClientEnterWorld += OnClientEnterWorld;
                SV_Client.OnClientThink      += OnClientThink;

                // Direct local connect — bypasses the loopback network handshake.
                // This is safe for single-player / listen-server: no OOB packets needed.
                LocalConnect();
            }

            if (StartClient)
            {
                ClientMain.CL_Init();
                // Skip CL_Connect — local player is already connected via LocalConnect above.

                ClientParse.OnSnapshotParsed += OnSnapshotParsed;
                ClientParse.OnServerCommand  += OnServerCommand;
            }

            // Spawn bots
            BotMain.OnBotCmd  += OnBotCmd;
            BotMain.OnBotChat += OnBotChat;

            for (int i = 0; i < BotCount; i++)
            {
                int team = (i % 2 == 0) ? DamageSystem.TEAM_AXIS : DamageSystem.TEAM_ALLIES;
                int cls  = i % 5;
                BotMain.G_BotConnect(
                    clientNum: MaxClients - BotCount + i,
                    name:      $"Bot_{i}",
                    skill:     BotSkillLevel,
                    team:      team,
                    playerClass: cls);
            }
        }

        /// <summary>
        /// Directly connects the local player to the server without going through
        /// the OOB loopback handshake (getchallenge / challengeResponse / connect).
        /// Equivalent to what the C code does for a local listen-server client.
        /// </summary>
        private void LocalConnect()
        {
            var svs = ServerMain.Svs;
            if (svs.Clients == null || svs.Clients.Length <= LocalClientNum)
            {
                Debug.LogError("[ETGameManager] LocalConnect: client slot not available");
                return;
            }

            // Set up the server-side client slot
            var cl = svs.Clients[LocalClientNum];
            cl.State          = ET.Server.ClientState.Active;
            cl.Name           = "LocalPlayer";
            cl.LastPacketTime = svs.Time;
            // Disable network snapshot sending for the local client — we drive the
            // camera directly from PlayerState, so snapshot encoding is never needed.
            // int.MaxValue exceeds the server's time-wrap restart threshold (0x70000000),
            // so SV_SendClientSnapshot's time check always returns early.
            cl.NextSnapshotTime = int.MaxValue;

            // Register with game logic (allocates session/persistant, sets spectator)
            string err = ServerGameLogic.ClientConnect(LocalClientNum, firstTime: true, isBot: false);
            if (err != null)
            {
                Debug.LogError($"[ETGameManager] LocalConnect: ClientConnect failed: {err}");
                return;
            }

            // Set the player on Allies team so SelectSpawnPoint finds an info_player_allies
            var gc = ServerGameLogic.Clients[LocalClientNum];
            gc.Sess.SessionTeam = DamageSystem.TEAM_ALLIES;
            gc.Pers.Name        = "LocalPlayer";

            // ClientBegin → ClientSpawn: places player at a spawn point
            ServerGameLogic.ClientBegin(LocalClientNum);

            // Initialise camera angles from the spawned player state
            var ps = gc.PS;
            _camYaw   = ps.ViewAngles1;  // ET yaw
            _camPitch = ps.ViewAngles0;  // ET pitch

            LocalPlayerActive = true;
            Debug.Log($"[ETGameManager] Local player spawned at " +
                      $"ET({ps.Origin0:F0},{ps.Origin1:F0},{ps.Origin2:F0})");
        }

        private void Update()
        {
            int msec = Mathf.RoundToInt(Time.deltaTime * 1000f);

            CmdSystem.Cbuf_Execute();

            if (StartServer) ServerMain.SV_Frame(msec);
            if (StartClient) ClientMain.CL_Frame(msec);

            BotMain.BotAI_Think(Time.deltaTime);

            if (LocalPlayerActive && !ETUIManager.MenuIsOpen)
                DriveLocalPlayer();

            // Keep the audio listener at the camera's position
            var cam = Camera.main;
            if (cam != null)
            {
                AudioSystem.S_Respatialize(
                    entityNum: 0,
                    origin:    cam.transform.position,
                    axis0:     cam.transform.forward,
                    axis1:     cam.transform.right,
                    axis2:     cam.transform.up,
                    inWater:   false);
            }
            else
            {
                AudioSystem.S_Respatialize(
                    entityNum: 0,
                    origin:    Vector3.zero,
                    axis0:     Vector3.forward,
                    axis1:     Vector3.right,
                    axis2:     Vector3.up,
                    inWater:   false);
            }
        }

        /// <summary>
        /// Reads keyboard and mouse input, builds a UserCmd, stores it on the GClient,
        /// and applies the resulting PlayerState to the main camera.
        /// Called every render frame when the local player is active and no menu is open.
        /// </summary>
        private void DriveLocalPlayer()
        {
            var gc = ServerGameLogic.Clients[LocalClientNum];
            if (gc == null) return;

            // Keep the server-side slot alive (prevents timeout) and snapshots disabled
            var cl = ServerMain.Svs.Clients[LocalClientNum];
            cl.LastPacketTime   = ServerMain.Svs.Time;
            cl.NextSnapshotTime = int.MaxValue;

            var ps  = gc.PS;
            var cam = Camera.main;

            // ---- Mouse look ----
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                _camYaw   += Input.GetAxis("Mouse X") * MouseSens * 10f;
                _camPitch -= Input.GetAxis("Mouse Y") * MouseSens * 10f;
                _camPitch  = Mathf.Clamp(_camPitch, -89f, 89f);
            }

            // ---- Build UserCmd ----
            var cmd = new UserCmd();
            cmd.ServerTime = ServerMain.Svs.Time;

            // Encode view angles as 16-bit short values (ET angle-to-short convention)
            cmd.Angles[0] = (int)(short)(_camPitch * (65536f / 360f));
            cmd.Angles[1] = (int)(short)(_camYaw   * (65536f / 360f));
            cmd.Angles[2] = 0;

            // Movement axes
            float fwd  = 0f, right = 0f, up = 0f;
            if (Input.GetKey(KeyCode.W)) fwd   += 1f;
            if (Input.GetKey(KeyCode.S)) fwd   -= 1f;
            if (Input.GetKey(KeyCode.A)) right -= 1f;
            if (Input.GetKey(KeyCode.D)) right += 1f;

            // Sprint doubles move speed via BUTTON_SPRINT
            bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetKey(KeyCode.Space)) up += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) up -= 1f;

            cmd.ForwardMove = ETMath.ClampChar((int)(fwd   * 127f));
            cmd.RightMove   = ETMath.ClampChar((int)(right * 127f));
            cmd.UpMove      = ETMath.ClampChar((int)(up    * 127f));

            // Buttons
            int buttons = 0;
            if (Input.GetMouseButton(0))        buttons |= Button.Attack;
            if (sprint)                         buttons |= Button.Sprint;
            if (Input.GetKey(KeyCode.F))        buttons |= Button.Activate;
            if (buttons != 0)                   buttons |= Button.Any;
            cmd.Buttons = buttons;

            cmd.Weapon = ps.Weapon;

            // Store as last cmd — G_RunClient picks it up on the next server tick
            gc.LastCmd.CopyFrom(cmd);

            // ---- Apply PlayerState origin to camera ----
            // ET coord system: ETtoUnity(x,y,z) = Unity(-y, z+viewHeight, x)
            float eyeZ = ps.Origin2 + ps.ViewHeight;
            if (cam != null)
            {
                cam.transform.position = new Vector3(-ps.Origin1, eyeZ, ps.Origin0);
                cam.transform.rotation = Quaternion.Euler(_camPitch, _camYaw, 0f);
            }
        }

        private void OnDestroy()
        {
            LocalPlayerActive = false;

            ServerInit.OnSpawnServer -= OnMapSpawn;
            ServerMain.OnGameRunFrame       -= OnGameRunFrame;
            SV_Client.OnClientEnterWorld    -= OnClientEnterWorld;
            SV_Client.OnClientThink         -= OnClientThink;

            ClientParse.OnSnapshotParsed -= OnSnapshotParsed;
            ClientParse.OnServerCommand  -= OnServerCommand;

            BotMain.OnBotCmd  -= OnBotCmd;
            BotMain.OnBotChat -= OnBotChat;

            if (StartServer) ServerMain.SV_Shutdown("Game ending");
            if (StartClient) ClientMain.CL_Shutdown();

            FileSystem.FS_Shutdown();
            AudioSystem.S_StopAllSounds();
        }

        // ----------------------------------------------------------------
        // Server event handlers
        // ----------------------------------------------------------------

        /// <summary>
        /// Called by ServerMain each game-simulation tick (~20 fps).
        /// Drives G_RunFrame and, after entity/client thinks, runs pmove
        /// for the local player so physics actually update the PlayerState.
        /// </summary>
        private void OnGameRunFrame(int serverTime)
        {
            // Run all entity thinks and client think-real (stores LastCmd, fires inactivity, etc.)
            ServerGameLogic.G_RunFrame(serverTime);

            // Run BG_Pmove for the local player so origin/velocity advance each server tick
            if (!LocalPlayerActive) return;

            var gc = ServerGameLogic.Clients[LocalClientNum];
            if (gc == null) return;

            var ps = gc.PS;
            if (ps.PmType != GameConst.PM_NORMAL && ps.PmType != GameConst.PM_NOCLIP) return;

            // Reuse the PmoveInput struct; share the PS reference so Pmove writes back into gc.PS
            _pmInput.Ps           = ps;
            _pmInput.Cmd          = gc.LastCmd;
            _pmInput.OldCmd       = gc.LastCmd;
            _pmInput.GameType     = CvarSystem.GetInt("g_gametype");
            _pmInput.TraceMask    = MASK_PLAYERSOLID;
            _pmInput.Trace        = CollisionSystem.DefaultTraceFunc;
            _pmInput.PointContents= CollisionSystem.DefaultPointContentsFunc;

            _pm.Pmove(_pmInput);

            // Sync entity origin from updated player state
            var ent = ServerGameLogic.Entities[LocalClientNum];
            if (ent != null)
            {
                ent.Origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
                ServerGameLogic.G_SetOrigin(ent, ent.Origin);
            }
        }

        private void OnClientEnterWorld(int clientNum, ServerClient cl)
        {
            Debug.Log($"[ETGameManager] Client {clientNum} ({cl.Name}) joined");
        }

        /// <summary>
        /// Fires from SV_ClientThink (network path). For the local player we drive
        /// input directly in DriveLocalPlayer / OnGameRunFrame so nothing extra needed.
        /// For bots we forward to ServerGameLogic.
        /// </summary>
        private void OnClientThink(int clientNum, UserCmd cmd)
        {
            if (clientNum == LocalClientNum) return;

            var gc = ServerGameLogic.Clients[clientNum];
            if (gc != null)
                ServerGameLogic.ClientThink_real(gc, cmd);
        }

        // ----------------------------------------------------------------
        // Client event handlers
        // ----------------------------------------------------------------

        private void OnSnapshotParsed(ET.Client.ClientSnapshot snap)
        {
            // Snapshot reconciliation — local player uses server-authoritative PS
        }

        private void OnServerCommand(string cmd)
        {
            CmdSystem.Cmd_ExecuteString(cmd);
        }

        // ----------------------------------------------------------------
        // Bot event handlers
        // ----------------------------------------------------------------

        private void OnBotCmd(int clientNum, UserCmd cmd)
        {
            var gc = ServerGameLogic.Clients[clientNum];
            if (gc == null) return;

            // Store bot cmd and let G_RunFrame drive it next tick
            gc.LastCmd.CopyFrom(cmd);
        }

        private void OnBotChat(int clientNum, string message)
        {
            Debug.Log($"[Bot {clientNum}] {message}");
        }
    }
}
