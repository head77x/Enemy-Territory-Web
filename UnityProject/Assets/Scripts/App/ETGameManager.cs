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
        // MonoBehaviour lifecycle
        // ----------------------------------------------------------------

        /// <summary>
        /// One-time initialisation: FileSystem, CvarSystem, CmdSystem, AudioSystem.
        /// Runs before Start so subsystems are ready for any dependent Awake calls
        /// in other components.
        /// </summary>
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
        }

        /// <summary>
        /// Server/client startup and bot spawning.  Subscriptions to cross-system
        /// events are established here so they can be cleanly removed in OnDestroy.
        /// </summary>
        // Root transform that holds the loaded BSP scene.
        // Destroyed and rebuilt each time a new map is loaded.
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

                // Subscribe BEFORE SV_SpawnServer so the BSP is loaded when the event fires
                ServerInit.OnSpawnServer += OnMapSpawn;

                ServerInit.SV_SpawnServer(MapName);

                // Subscribe server events
                ServerMain.OnGameRunFrame        += OnGameRunFrame;
                SV_Client.OnClientEnterWorld     += OnClientEnterWorld;
                SV_Client.OnClientThink          += OnClientThink;
            }

            if (StartClient)
            {
                ClientMain.CL_Init();
                if (StartServer)
                    ClientMain.CL_Connect("127.0.0.1:27960");

                ClientParse.OnSnapshotParsed += OnSnapshotParsed;
                ClientParse.OnServerCommand  += OnServerCommand;
            }

            // Wire collision for pmove
            // (CollisionSystem.DefaultTraceFunc is injected wherever PmoveInput is built)

            // Wire weapon → damage pipeline
            // (DamageSystem subscribes WeaponSystem.OnDamage in its static ctor automatically)

            // Spawn bots
            BotMain.OnBotCmd  += OnBotCmd;
            BotMain.OnBotChat += OnBotChat;

            for (int i = 0; i < BotCount; i++)
            {
                int team = (i % 2 == 0) ? DamageSystem.TEAM_AXIS : DamageSystem.TEAM_ALLIES;
                int cls  = i % 5;   // rotate through PC_SOLDIER..PC_COVERTOPS
                BotMain.G_BotConnect(
                    clientNum: MaxClients - BotCount + i,
                    name:      $"Bot_{i}",
                    skill:     BotSkillLevel,
                    team:      team,
                    playerClass: cls);
            }
        }

        /// <summary>
        /// Per-frame driver: flushes the command buffer then ticks server, client,
        /// and bot AI.  Also updates the audio listener position each frame.
        /// </summary>
        private void Update()
        {
            int msec = Mathf.RoundToInt(Time.deltaTime * 1000f);

            CmdSystem.Cbuf_Execute();

            if (StartServer) ServerMain.SV_Frame(msec);
            if (StartClient) ClientMain.CL_Frame(msec);

            BotMain.BotAI_Think(Time.deltaTime);

            // Keep the audio listener at the local client's viewpoint.
            // A full implementation would pass the real camera entity's position
            // and orientation here; zeroed values are correct for a fixed observer.
            AudioSystem.S_Respatialize(
                entityNum: 0,
                origin:    Vector3.zero,
                axis0:     Vector3.forward,
                axis1:     Vector3.right,
                axis2:     Vector3.up,
                inWater:   false);
        }

        /// <summary>
        /// Unsubscribes all events and shuts down every subsystem in reverse
        /// initialisation order so nothing references a destroyed object.
        /// </summary>
        private void OnDestroy()
        {
            ServerInit.OnSpawnServer -= OnMapSpawn;
            // Unsubscribe — guards against null-ref if subsystems were never started
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
        /// Called by ServerMain each time the game simulation advances one frame.
        /// Hook point for any per-tick server-side game logic that lives outside
        /// the normal g_main.c RunFrame path.
        /// </summary>
        private void OnGameRunFrame(int serverTime)
        {
            // game simulation hook
        }

        /// <summary>
        /// Called when a client's initial world state has been confirmed and
        /// it is fully in-game (mirrors ClientEnterWorld in g_client.c).
        /// </summary>
        private void OnClientEnterWorld(int clientNum, ServerClient cl)
        {
            Debug.Log($"Client {clientNum} ({cl.Name}) joined");
        }

        /// <summary>
        /// Called every server frame for each connected client with the latest
        /// UserCmd received from that client (mirrors ClientThink in g_client.c).
        /// </summary>
        private void OnClientThink(int clientNum, UserCmd cmd)
        {
            // apply cmd to game entity
        }

        // ----------------------------------------------------------------
        // Client event handlers
        // ----------------------------------------------------------------

        /// <summary>
        /// Called by ClientParse each time a new server snapshot has been
        /// decoded; use this to drive local prediction reconciliation and
        /// entity interpolation.
        /// </summary>
        private void OnSnapshotParsed(ET.Client.ClientSnapshot snap)
        {
            // update local game state
        }

        /// <summary>
        /// Called when the server sends a reliable command string.
        /// Forwards straight to the command interpreter so ET server commands
        /// (e.g. "cs", "print", "disconnect") work as in the original client.
        /// </summary>
        private void OnServerCommand(string cmd)
        {
            CmdSystem.Cmd_ExecuteString(cmd);
        }

        // ----------------------------------------------------------------
        // Bot event handlers
        // ----------------------------------------------------------------

        /// <summary>
        /// Called by BotMain when a bot has computed its UserCmd for this frame.
        /// Forward the command to the server so the bot entity moves and fires.
        /// </summary>
        private void OnBotCmd(int clientNum, UserCmd cmd)
        {
            // forward bot cmd to server
        }

        /// <summary>
        /// Called when a bot wants to send a chat message.
        /// In a full implementation this would route through the server's
        /// say/teamsay command path.
        /// </summary>
        private void OnBotChat(int clientNum, string message)
        {
            Debug.Log($"[Bot {clientNum}] {message}");
        }
    }
}
