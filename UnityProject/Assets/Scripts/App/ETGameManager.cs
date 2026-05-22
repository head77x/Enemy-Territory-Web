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
        // Singleton
        // ----------------------------------------------------------------
        public static ETGameManager Instance { get; private set; }

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

        // Viewmodel (first-person weapon) — dedicated depth-only camera + real MD3 mesh
        private const int ViewmodelLayer = 30;
        private Camera    _viewmodelCam;
        private Transform _viewmodelRoot;
        private int       _viewmodelWeapon     = GameConst.WP_NONE;
        private int       _viewmodelNumFrames  = 0;    // total MD3 frames (blend shapes + 1)
        // CG_WeaponAnimation pipeline state (CG_RunWeapLerpFrame equivalent)
        private ET.Client.WeaponAnimData  _weapAnimData;   // parsed from weapons/*.weap
        private ET.Client.WeapLerpFrame   _weapLerpFrame = new ET.Client.WeapLerpFrame();
        // Per-frame tag animation component on the hands model (null for blend-shape models)
        private MdcTagAnimation           _handsTagAnim;
        // Root GameObject of the hands model (handsGo) — used for bob/kick rotation each frame
        private GameObject                _handsGo;

        // ----------------------------------------------------------------
        // CG_CalculateWeaponPosition state (cg_weapons.c port)
        // ----------------------------------------------------------------
        // kickAngles/kickAVel: spring-damped viewmodel recoil (CG_KickAngles + CG_WeaponFireRecoil)
        private readonly float[] _kickAngles = new float[3];  // [pitch, yaw, roll] degrees
        private readonly float[] _kickAVel   = new float[3];  // angular velocity, deg/s

        // ----------------------------------------------------------------
        // MonoBehaviour lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            Instance = this;

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
            FileSystem.FS_DiagnoseWeaponModels();

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

            // Ensure the BSP scene is lit. If there's no light in the scene, add one.
            if (FindObjectOfType<Light>() == null)
            {
                var lightGo = new GameObject("Sun");
                var sun = lightGo.AddComponent<Light>();
                sun.type      = LightType.Directional;
                sun.intensity = 1.2f;
                sun.color     = new Color(1f, 0.96f, 0.84f);
                sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
            RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.40f);

            if (StartServer)
            {
                ServerInit.SV_Init();

                // Subscribe BEFORE SV_SpawnServer so the BSP and G_InitGame run first
                ServerInit.OnSpawnServer += OnMapSpawn;

                // SV_SpawnServer fires OnSpawnServer synchronously:
                //   → OnMapSpawn → G_InitGame + G_SpawnEntitiesFromString
                ServerInit.SV_SpawnServer(MapName);

                // Subscribe server-frame and client-think events
                // OnGameRunFrame internally calls G_RunFrame, so only subscribe once.
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
            WeaponSystem.OnFireWeapon += OnWeaponFired;

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
        /// Registers the local player's client slot on the server without spawning.
        /// The player starts as a spectator and waits for team/class selection via the
        /// Limbo UI before <see cref="SpawnLocalPlayer"/> is called.
        /// </summary>
        private void LocalConnect()
        {
            var svs = ServerMain.Svs;
            if (svs.Clients == null || svs.Clients.Length <= LocalClientNum)
            {
                Debug.LogError("[ETGameManager] LocalConnect: client slot not available");
                return;
            }

            // Set up the server-side slot — disable snapshot sending for local client.
            var cl = svs.Clients[LocalClientNum];
            cl.State            = ET.Server.ClientState.Active;
            cl.Name             = "LocalPlayer";
            cl.LastPacketTime   = svs.Time;
            cl.NextSnapshotTime = int.MaxValue;

            // ClientConnect sets SessionTeam = Spectator; no spawn yet.
            string err = ServerGameLogic.ClientConnect(LocalClientNum, firstTime: true, isBot: false);
            if (err != null)
            {
                Debug.LogError($"[ETGameManager] LocalConnect: ClientConnect failed: {err}");
                return;
            }

            var gc = ServerGameLogic.Clients[LocalClientNum];
            gc.Pers.Name = "LocalPlayer";

            // Player remains Spectator until SpawnLocalPlayer() is called from the UI.
            LocalPlayerActive = false;
            Debug.Log("[ETGameManager] Local player connected — waiting for team/class selection.");
        }

        /// <summary>
        /// Called by the UI after the player selects team and class.
        /// Sets the server-side session and spawns the player into the world.
        /// </summary>
        public void SpawnLocalPlayer(int team, int classType)
        {
            var gc = ServerGameLogic.Clients[LocalClientNum];
            if (gc == null)
            {
                Debug.LogError("[ETGameManager] SpawnLocalPlayer: no client slot");
                return;
            }

            gc.Sess.SessionTeam  = team;
            gc.Pers.ClassType    = classType;
            gc.Pers.Name         = "LocalPlayer";

            ServerGameLogic.ClientBegin(LocalClientNum);

            // Seed CommandTime so the first Pmove gets a normal 50ms delta.
            gc.PS.CommandTime = ServerMain.Svs.Time;

            // Initialise camera angles from the spawned PlayerState.
            var ps = gc.PS;
            _camYaw   = ps.ViewAngles1;
            _camPitch = ps.ViewAngles0;

            LocalPlayerActive = true;
            Debug.Log($"[ETGameManager] Player spawned — team={team} class={classType} " +
                      $"origin=({ps.Origin0:F0},{ps.Origin1:F0},{ps.Origin2:F0})");

            SetupViewmodel();
        }

        /// <summary>Static entry point for the UI layer to trigger a spawn.</summary>
        public static void RequestSpawnLocalPlayer(int team, int classType)
        {
            Instance?.SpawnLocalPlayer(team, classType);
        }

        private void Update()
        {
            int msec = Mathf.RoundToInt(Time.deltaTime * 1000f);

            CmdSystem.Cbuf_Execute();

            // N key: toggle noclip for debug
            if (LocalPlayerActive && Input.GetKeyDown(KeyCode.N))
            {
                var gcN = ServerGameLogic.Clients[LocalClientNum];
                if (gcN?.PS != null)
                {
                    bool wasNoclip = gcN.PS.PmType == GameConst.PM_NOCLIP;
                    gcN.PS.PmType = wasNoclip ? GameConst.PM_NORMAL : GameConst.PM_NOCLIP;
                    Debug.Log($"[ETGameManager] Noclip {(wasNoclip ? "OFF" : "ON")}");
                }
            }

            // Read input first so this frame's buttons are in gc.LastCmd
            // before G_RunFrame processes them inside SV_Frame.
            if (LocalPlayerActive && !ETUIManager.MenuIsOpen)
                DriveLocalPlayer();

            if (StartServer) ServerMain.SV_Frame(msec);
            if (StartClient) ClientMain.CL_Frame(msec);

            BotMain.BotAI_Think(Time.deltaTime);

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
        /// <summary>
        /// Creates a depth-only secondary camera for the first-person viewmodel.
        /// Layer 30 is used exclusively so the main camera never renders the weapon
        /// (avoids z-fighting with walls).  The actual MD3 weapon mesh is loaded
        /// from the virtual filesystem (PK3) by <see cref="UpdateViewmodel"/>.
        /// </summary>
        private void SetupViewmodel()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Exclude viewmodel layer from main camera
            cam.cullingMask &= ~(1 << ViewmodelLayer);

            // Viewmodel camera — renders ONLY the weapon, on top of the world
            var vmGo = new GameObject("ViewmodelCamera");
            _viewmodelCam = vmGo.AddComponent<Camera>();
            _viewmodelCam.clearFlags    = CameraClearFlags.Depth;
            _viewmodelCam.cullingMask   = 1 << ViewmodelLayer;
            _viewmodelCam.nearClipPlane = 0.01f;
            _viewmodelCam.farClipPlane  = 600f;
            _viewmodelCam.fieldOfView   = 90f;  // ET default cg_fov=90; hand mesh sits ~54° below center
            _viewmodelCam.depth         = cam.depth + 1;

            _viewmodelRoot = new GameObject("ViewWeaponRoot").transform;
            _viewmodelRoot.SetParent(vmGo.transform, false);
        }

        /// <summary>
        /// Mirrors ET's CG_AddViewWeapon setup:
        ///   hand.hModel = weapon->handsModel  (animated hands model)
        ///   gun.hModel  = weapon->weaponModel[W_FP_MODEL] (gun body on tag_weapon)
        /// Loads both models from the PK3 virtual filesystem and attaches them.
        /// Called every DriveLocalPlayer frame; no-ops if the weapon hasn't changed.
        /// </summary>
        private void UpdateViewmodel(int weapon)
        {
            if (weapon == _viewmodelWeapon) return;
            _viewmodelWeapon = weapon;

            // Remove old mesh
            _handsTagAnim = null;
            _handsGo      = null;
            if (_viewmodelRoot != null)
            {
                for (int i = _viewmodelRoot.childCount - 1; i >= 0; i--)
                    Destroy(_viewmodelRoot.GetChild(i).gameObject);
            }

            if (weapon <= GameConst.WP_NONE || _viewmodelRoot == null) return;

            var wri = ET.Client.CGameWeapons.CG_GetWeaponRenderInfo(weapon);
            if (wri == null) return;

            bool usingFallback = false;

            // --- Load animation root (hand.hModel = weapon->handsModel) ---
            // This is the multi-frame animated model that drives the weapon animation.
            // The gun body will be attached to its tag_weapon.
            GameObject handsGo = null;
            if (!string.IsNullOrEmpty(wri.HandsModelPath))
            {
                handsGo = RuntimeResourceLoader.LoadMd3(wri.HandsModelPath, _viewmodelRoot);
                if (handsGo == null)
                    Debug.LogWarning($"[ETGameManager] handsModel not found: {wri.HandsModelPath}");
                else
                {
                    Debug.Log($"[ETGameManager] Loaded handsModel: {wri.HandsModelPath}");
                    _handsTagAnim = handsGo.GetComponent<MdcTagAnimation>();
                }
            }

            if (handsGo == null && !string.IsNullOrEmpty(wri.ModelPath))
            {
                // No dedicated hands model — fall back to loading the gun body directly
                // (some weapons pack everything into one model file)
                handsGo = RuntimeResourceLoader.LoadMd3(wri.ModelPath, _viewmodelRoot);
                if (handsGo != null)
                    Debug.Log($"[ETGameManager] Loaded combined viewmodel (no handsModel): {wri.ModelPath}");
            }

            if (handsGo == null)
            {
                Debug.LogWarning($"[ETGameManager] No viewmodel found for weapon {weapon}; trying akimbo fallback");

                // Fallback: akimbo sidearm viewmodels are always present in mp_bin.pk3.
                var ps = ServerGameLogic.Clients[LocalClientNum]?.PS;
                bool axis = ps != null && ps.TeamNum == ET.Game.DamageSystem.TEAM_AXIS;
                string fallbackGun  = axis
                    ? "models/weapons2/akimbo_luger/v_akimbo_luger.md3"
                    : "models/weapons2/akimbo_colt/v_akimbo_colt.md3";

                handsGo = RuntimeResourceLoader.LoadMd3(fallbackGun, _viewmodelRoot);
                if (handsGo == null)
                {
                    Debug.LogWarning($"[ETGameManager] Fallback viewmodel also not found: {fallbackGun}");
                    return;
                }
                usingFallback = true;
                Debug.Log($"[ETGameManager] Using fallback viewmodel: {fallbackGun}");

                string barrelPath = axis
                    ? "models/weapons2/akimbo_luger/v_akimbo_luger_barrel.md3"
                    : "models/weapons2/akimbo_colt/v_akimbo_colt_barrel.md3";
                LoadAndAttachSubModel(barrelPath, handsGo, new[] { "tag_barrel", "tag_weapon", "tag_flash" });

                string handPath = axis
                    ? "models/weapons2/akimbo_luger/v_akimbo_luger_hand.md3"
                    : "models/weapons2/akimbo_colt/v_akimbo_colt_hand.md3";
                var handGo = RuntimeResourceLoader.LoadMd3(handPath, _viewmodelRoot);
                if (handGo != null)
                {
                    handGo.transform.localPosition = new Vector3(4f, -8f, 8f);
                    handGo.transform.localRotation = Quaternion.Euler(-5f, -10f, 0f);
                    handGo.transform.localScale    = Vector3.one;
                    SetLayerRecursive(handGo, ViewmodelLayer);
                    foreach (var r in handGo.GetComponentsInChildren<Renderer>())
                    {
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows    = false;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(wri.HandsModelPath) && !string.IsNullOrEmpty(wri.ModelPath))
            {
                // Attach gun body (W_FP_MODEL) to tag_weapon on the hands model.
                // Mirrors CG_AddPlayerWeapon: gun.hModel = weapon->weaponModel[W_FP_MODEL]
                //   then CG_PositionRotatedEntityOnTag(&gun, parent, "tag_weapon")
                LoadAndAttachSubModel(wri.ModelPath, handsGo, new[] { "tag_weapon", "tag_weapon2" });
                Debug.Log($"[ETGameManager] Attached gun body to tag_weapon: {wri.ModelPath}");
            }

            // CG_CalculateWeaponPosition: hand.origin = cg.refdef.vieworg.
            // tag_weapon sits at camera-local z≈-2.4 at idle, and goes back to z≈-3.3
            // during the ATTACK1 animation frames. Push the whole model forward by 5 units
            // (equivalent to cg_gunX=5) so that even the deepest attack frame (+5-3.3=+1.7)
            // stays clearly in front of the camera's near clip plane.
            handsGo.transform.localPosition = new Vector3(0f, 0f, 5f);
            handsGo.transform.localRotation = Quaternion.identity;
            handsGo.transform.localScale    = Vector3.one;

            // Store for per-frame bob/kick rotation updates in DriveViewmodelAnimation
            _handsGo = handsGo;

            // Reset kick state when switching weapons
            System.Array.Clear(_kickAngles, 0, 3);
            System.Array.Clear(_kickAVel,   0, 3);

            // Diagnostic: confirm tag_weapon was found on handsGo and gun body is attached
            var tagWeaponCheck = handsGo.transform.Find("tag_weapon");
            Debug.Log($"[ETGameManager] handsGo children: " +
                      $"tag_weapon found={tagWeaponCheck != null}, " +
                      $"gun body children={tagWeaponCheck?.childCount ?? 0}, " +
                      $"handsGo localPos={handsGo.transform.localPosition}, " +
                      $"localRot={handsGo.transform.localEulerAngles}");

            SetLayerRecursive(handsGo, ViewmodelLayer);

            // Viewmodel never casts or receives world shadows.
            // Expand each mesh's local bounds to a very large box so Unity frustum
            // culling never hides the weapon mid-animation (the reload anim moves
            // tag_weapon far off-center; small bounds would make the renderer pop invisible).
            foreach (var r in _viewmodelRoot.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows    = false;
            }
            foreach (var mf in _viewmodelRoot.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.mesh != null)
                    mf.mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            }

            // --- Load animation data (CG_ParseWeaponConfig equivalent) ---
            // Use weaponConfig path from the .weap file (simple line format),
            // NOT the .weap file itself (which is PC-format / weaponDef).
            _weapAnimData = null;
            if (!string.IsNullOrEmpty(wri.WeaponConfigPath))
            {
                _weapAnimData = ET.Client.WeaponAnimData.Load(wri.WeaponConfigPath);
                Debug.Log($"[ETGameManager] WeaponConfig '{wri.WeaponConfigPath}': " +
                          $"{(_weapAnimData != null ? "loaded" : "not found")}");
            }
            if (_weapAnimData == null && usingFallback)
            {
                var ps2 = ServerGameLogic.Clients[LocalClientNum]?.PS;
                bool axis2 = ps2 != null && ps2.TeamNum == ET.Game.DamageSystem.TEAM_AXIS;
                string fbWeap = axis2 ? "weapons/akimbo_luger.weap" : "weapons/akimbo_colt.weap";
                _weapAnimData = ET.Client.WeaponAnimData.Load(fbWeap);
            }
            // Reset lerpFrame so CG_RunWeapLerpFrame re-initialises on next call
            _weapLerpFrame = new ET.Client.WeapLerpFrame();

            // Wire WeapAnimGetDurationMs: BG_GetAnimScriptAnimation equivalent.
            // Returns total duration (ms) of a one-shot weapon animation so PM_StartWeaponAnim
            // can set WeapAnimTimer and prevent idle from overriding the attack animation.
            var animDataRef = _weapAnimData;
            _pmInput.WeapAnimGetDurationMs = (animIdx) =>
            {
                if (animDataRef == null) return 0;
                int idx = animIdx & ~GameConst.ANIM_TOGGLEBIT;
                var anims = animDataRef.Animations;
                if (idx < 0 || idx >= anims.Length || anims[idx] == null) return 0;
                var a = anims[idx];
                // IDLE1 (0) and IDLE2 (1) must return 0 — idle never blocks attacks or reloads.
                // All other animations (attack, reload, raise, drop) lock for their full
                // duration so that PM_ContinueWeaponAnim cannot cut them short with idle.
                if (idx <= 1) return 0;
                return a.NumFrames * a.FrameLerp;
            };

            // Determine frame count. Tag-animated hands models use per-frame tag transforms
            // (v_thompson_hand.mdc has 66 frames, 0 surfaces). Mesh models use blend shapes.
            if (_handsTagAnim != null)
            {
                _viewmodelNumFrames = _handsTagAnim.NumFrames;
                Debug.Log($"[ETGameManager] Tag-animated hands: {_handsTagAnim.NumFrames} frames, " +
                          $"tags=[{string.Join(",", _handsTagAnim.TagNames)}]");
            }
            else
            {
                int maxBlendShapes = 0;
                foreach (var smr in _viewmodelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (smr.sharedMesh != null)
                        maxBlendShapes = Mathf.Max(maxBlendShapes, smr.sharedMesh.blendShapeCount);
                _viewmodelNumFrames = maxBlendShapes + 1;
            }

            Debug.Log($"[ETGameManager] Viewmodel ready: weapon={weapon} frames={_viewmodelNumFrames} " +
                      $"animData={(_weapAnimData != null ? "loaded" : "missing")} " +
                      $"tagAnim={(_handsTagAnim != null ? "yes" : "no")} " +
                      $"handsModel='{wri.HandsModelPath}' gunBody='{wri.ModelPath}'");

            // Diagnostic: dump renderer state for every surface in the viewmodel hierarchy.
            // This reveals whether w_handr is loaded, enabled, and where its mesh bounds are.
            foreach (var r in _viewmodelRoot.GetComponentsInChildren<Renderer>(includeInactive: true))
            {
                Mesh m = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) m = mf.sharedMesh;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) m = smr.sharedMesh;
                Debug.Log($"[VMRenderer] '{r.gameObject.name}' enabled={r.enabled} " +
                          $"isVisible={r.isVisible} " +
                          $"bounds={r.bounds} " +
                          $"verts={m?.vertexCount ?? 0} " +
                          $"mat='{r.sharedMaterial?.name ?? "null"}' " +
                          $"tex='{r.sharedMaterial?.mainTexture?.name ?? "null"}' " +
                          $"layer={r.gameObject.layer}");
            }
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        // Loads a sub-model MD3 and parents it either to a named tag on the parent model
        // (first match from tagNames list) or, if no tag is found, aligns it to the parent's
        // muzzle (+z end) so the barrel sits at the front of the gun body.
        private void LoadAndAttachSubModel(string path, GameObject parentModel, string[] tagNames)
        {
            // Find a matching tag child transform
            Transform attachPoint = null;
            foreach (string tagName in tagNames)
            {
                attachPoint = parentModel.transform.Find(tagName);
                if (attachPoint != null) break;
            }

            Transform parent = attachPoint ?? parentModel.transform;

            var subGo = RuntimeResourceLoader.LoadMd3(path, parent);
            if (subGo == null)
            {
                Debug.LogWarning($"[ETGameManager] Sub-model not found: {path}");
                return;
            }

            if (attachPoint == null)
            {
                // No tag: align the sub-model's -z face with the parent model's +z (muzzle) face.
                // Both bounds are in the same local mesh space (Unity coords, EtToUnity applied).
                Bounds gunBounds   = GetCombinedMeshBounds(parentModel);
                Bounds subBounds   = GetCombinedMeshBounds(subGo);
                bool   hasBoth     = gunBounds.size != Vector3.zero && subBounds.size != Vector3.zero;
                if (hasBoth)
                {
                    // Align barrel center (x,y) to gun body center, and place barrel
                    // back face (-z) at gun muzzle (+z face) so it looks attached.
                    float xOff = gunBounds.center.x - subBounds.center.x;
                    float yOff = gunBounds.center.y - subBounds.center.y;
                    float zOff = (gunBounds.center.z + gunBounds.extents.z)
                               - (subBounds.center.z - subBounds.extents.z);
                    subGo.transform.localPosition = new Vector3(xOff, yOff, zOff);
                    Debug.Log($"[ETGameManager] Sub-model aligned: offset=({xOff:F2},{yOff:F2},{zOff:F2})");
                }
                else
                {
                    subGo.transform.localPosition = Vector3.zero;
                }
                subGo.transform.localRotation = Quaternion.identity;
                subGo.transform.localScale    = Vector3.one;
            }

            SetLayerRecursive(subGo, ViewmodelLayer);
            foreach (var r in subGo.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows    = false;
            }
            Debug.Log($"[ETGameManager] Sub-model loaded: {path} → attached to '{parent.name}'");
        }

        private static Bounds GetCombinedMeshBounds(GameObject root)
        {
            Bounds b = default;
            bool first = true;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                if (first) { b = mf.sharedMesh.bounds; first = false; }
                else b.Encapsulate(mf.sharedMesh.bounds);
            }
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                if (first) { b = smr.sharedMesh.bounds; first = false; }
                else b.Encapsulate(smr.sharedMesh.bounds);
            }
            return b;
        }

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
            cmd.Angles[1] = (int)(short)(-_camYaw  * (65536f / 360f));
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

            // WButtons (Wolf-specific: reload, prone, etc.)
            int wbuttons = 0;
            if (Input.GetKey(KeyCode.R))        wbuttons |= WButton.Reload;
            if (Input.GetKey(KeyCode.Z))        wbuttons |= WButton.Prone;
            cmd.WButtons = wbuttons;

            if (Input.GetKeyDown(KeyCode.R))
                Debug.Log($"[DriveLocalPlayer] R pressed → wbuttons=0x{wbuttons:X} (Reload={WButton.Reload}) weapon={ps.Weapon} weaponstate={ps.Weaponstate}");

            if (Input.GetMouseButton(0))
                Debug.Log($"[DriveLocalPlayer] LMB pressed → buttons=0x{buttons:X} weapon={ps.Weapon} pmType={ps.PmType}");

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

            // Reload viewmodel when weapon changes; keep camera synced
            UpdateViewmodel(ps.Weapon);
            DriveViewmodelAnimation();
            if (_viewmodelCam != null && cam != null)
            {
                _viewmodelCam.transform.position = cam.transform.position;
                _viewmodelCam.transform.rotation = cam.transform.rotation;
            }
        }

        // ----------------------------------------------------------------
        // CG_WeaponFireRecoil port (cg_weapons.c)
        // Called whenever the local player fires.  Sets _kickAVel to a
        // weapon-specific impulse; CG_KickAngles spring-damps it each frame.
        // ----------------------------------------------------------------
        private void OnWeaponFired(ET.Game.FireInfo fire)
        {
            if (fire.ClientNum != LocalClientNum) return;

            float pitchAdd = 0f, yawRandom = 0f;

            switch (fire.Weapon)
            {
                case GameConst.WP_THOMPSON:
                case GameConst.WP_MP40:
                case GameConst.WP_STEN:
                case GameConst.WP_FG42:
                case GameConst.WP_MOBILE_MG42:
                    pitchAdd  = (1f + UnityEngine.Random.Range(0, 3)) * 0.3f;
                    yawRandom = 2f * 0.3f;
                    break;
                case GameConst.WP_GARAND:
                case GameConst.WP_KAR98:
                case GameConst.WP_CARBINE:
                case GameConst.WP_K43:
                    pitchAdd  = 2f;
                    yawRandom = 1f;
                    break;
                default:
                    return;
            }

            float recoilYaw   = (UnityEngine.Random.value * 2f - 1f) * yawRandom;
            float recoilRoll  = -recoilYaw;
            float recoilPitch = -pitchAdd;

            // VectorScale(recoil, 30) from original
            _kickAVel[0] = recoilPitch * 30f;
            _kickAVel[1] = recoilYaw   * 30f;
            _kickAVel[2] = recoilRoll  * 30f;
        }

        // ----------------------------------------------------------------
        // CG_KickAngles port (cg_view.c)
        // Spring-damps _kickAVel → _kickAngles each frame.
        // ----------------------------------------------------------------
        private void UpdateKickAngles(float dt)
        {
            const float centerSpeed  = 2400f;
            const float maxKickAngle = 10f;
            const int   STEP_MS      = 20;

            int msec = Mathf.Max(1, Mathf.RoundToInt(dt * 1000f));
            for (int t = msec; t > 0; t -= STEP_MS)
            {
                int   step = Mathf.Min(t, STEP_MS);
                float ft   = step / 1000f;

                for (int i = 0; i < 3; i++)
                {
                    if (_kickAVel[i] == 0f && _kickAngles[i] == 0f) continue;

                    if (_kickAngles[i] != 0f)
                    {
                        float idealSpeed = -Mathf.Sign(_kickAngles[i]) * centerSpeed;
                        _kickAVel[i] += idealSpeed * ft;
                    }

                    float kickChange = _kickAVel[i] * ft;
                    // returning toward center: dampen
                    if (_kickAngles[i] != 0f && ((_kickAngles[i] < 0f) != (kickChange < 0f)))
                        kickChange *= 0.06f;

                    if (_kickAngles[i] == 0f ||
                        ((_kickAngles[i] + kickChange < 0f) == (_kickAngles[i] < 0f)))
                    {
                        _kickAngles[i] += kickChange;
                        if (_kickAngles[i] == 0f)
                            _kickAVel[i] = 0f;
                        else if (Mathf.Abs(_kickAngles[i]) > maxKickAngle)
                        {
                            _kickAngles[i] = Mathf.Sign(_kickAngles[i]) * maxKickAngle;
                            _kickAVel[i]   = 0f;
                        }
                    }
                    else
                    {
                        _kickAngles[i] = 0f;
                        _kickAVel[i]   = 0f;
                    }
                }
            }
        }

        // CG_WeaponAnimation equivalent — drives viewmodel animation.
        //
        // For tag-only MDC hands models (v_thompson_hand.mdc, numSurfs=0):
        //   Interpolates per-frame tag transforms and applies them to child tag Transforms.
        //   The gun body (v_thompson.mdc) is parented to tag_weapon, so it moves with it.
        //   Mirrors CG_PositionRotatedEntityOnTag(&gun, parent, "tag_weapon").
        //
        // For mesh models with blend shapes:
        //   Sets blend-shape weights to morph between oldFrame and frame.
        private int _lastLoggedWeapAnim = -1;

        private void DriveViewmodelAnimation()
        {
            if (_viewmodelRoot == null || _viewmodelNumFrames <= 1) return;

            // ---- CG_KickAngles: advance spring-damped recoil each render frame ----
            UpdateKickAngles(Time.deltaTime);

            // ---- CG_CalculateWeaponPosition: apply bob + idle drift + kickAngles ----
            // Mirrors the angles[] modifications in cg_weapons.c CG_CalculateWeaponPosition.
            // handsGo is parented to the viewmodel camera so localRotation IS camera-local space.
            if (_handsGo != null)
            {
                var ps = ServerGameLogic.Clients[LocalClientNum]?.PS;

                // Bobbing (from ps.BobCycle — set by Pmove when walking)
                float xyspeed    = 0f;
                float bobfracsin = 0f;
                int   bobcycle   = 0;
                if (ps != null)
                {
                    xyspeed    = Mathf.Sqrt(ps.Velocity0 * ps.Velocity0 + ps.Velocity1 * ps.Velocity1);
                    bobcycle   = (ps.BobCycle & 128) >> 7;          // 0 or 1 (odd leg toggle)
                    bobfracsin = Mathf.Abs(Mathf.Sin((ps.BobCycle & 127) / 127f * Mathf.PI));
                }

                float scale = ((bobcycle & 1) != 0) ? -xyspeed : xyspeed;

                float pitchOff = xyspeed    * bobfracsin * 0.005f;
                float yawOff   = scale      * bobfracsin * 0.01f;
                float rollOff  = scale      * bobfracsin * 0.005f;

                // Idle drift: sin(time * 0.001) in original — time is ms, so 1 Hz in seconds
                float fracsin = Mathf.Sin(Time.realtimeSinceStartup * 1f);
                pitchOff += 80f * fracsin * 0.01f;   // ±0.8 deg
                yawOff   += 80f * fracsin * 0.01f;
                rollOff  += 80f * fracsin * 0.01f;

                // KickAngles — VectorMA(angles, -1.0, kickAngles, angles)
                pitchOff -= _kickAngles[0];
                yawOff   -= _kickAngles[1];
                rollOff  -= _kickAngles[2];

                _handsGo.transform.localRotation = Quaternion.Euler(pitchOff, yawOff, rollOff);
            }

            var gc = ServerGameLogic.Clients[LocalClientNum];
            int weapAnim = gc?.PS?.WeapAnim ?? GameConst.WEAP_IDLE1;
            // Use Unity real-time (ms) as cg.time — ET client used client-predicted time
            // that advances every render frame, not server tick rate (50ms steps).
            int cgTime = Mathf.RoundToInt(Time.realtimeSinceStartup * 1000f);

            // Log whenever weapAnim changes so we can confirm attack/idle transitions
            if (weapAnim != _lastLoggedWeapAnim)
            {
                int idx = weapAnim & ~GameConst.ANIM_TOGGLEBIT;
                Debug.Log($"[DriveViewmodelAnim] weapAnim changed → {weapAnim} (idx={idx}) cgTime={cgTime}");
                _lastLoggedWeapAnim = weapAnim;
            }

            if (_weapAnimData == null) return;

            ET.Client.WeaponAnimSystem.WeaponAnimation(
                _weapAnimData, _weapLerpFrame, weapAnim, cgTime,
                out int oldFrame, out int frame, out float backlerp);

            // --- Tag-based animation (tag-only MDC hands model) ---
            // Mirrors CG_PositionRotatedEntityOnTag: each frame, tag_weapon transform is
            // interpolated between oldFrame and frame. Gun body (child of tag_weapon) follows.
            if (_handsTagAnim != null)
            {
                int numF   = _handsTagAnim.NumFrames;
                int fCur   = Mathf.Clamp(frame,    0, numF - 1);
                int fOld   = Mathf.Clamp(oldFrame, 0, numF - 1);
                float lerp = 1f - backlerp; // fraction toward current frame

                int numTags = _handsTagAnim.TagTransforms?.Length ?? 0;
                for (int ti = 0; ti < numTags; ti++)
                {
                    var tagXform = _handsTagAnim.TagTransforms[ti];
                    if (tagXform == null) continue;
                    tagXform.localPosition = Vector3.Lerp(
                        _handsTagAnim.TagPositions[fOld][ti],
                        _handsTagAnim.TagPositions[fCur][ti], lerp);
                    tagXform.localRotation = Quaternion.Slerp(
                        _handsTagAnim.TagRotations[fOld][ti],
                        _handsTagAnim.TagRotations[fCur][ti], lerp);
                }

                // Diagnostic once per second: log tag_weapon world position + runtime renderer visibility
                if (Time.frameCount % 60 == 0)
                {
                    for (int ti = 0; ti < numTags; ti++)
                    {
                        var t = _handsTagAnim.TagTransforms[ti];
                        if (t != null && _handsTagAnim.TagNames[ti] == "tag_weapon")
                        {
                            Debug.Log($"[DriveViewmodelAnim] tag_weapon worldPos={t.position:F2} " +
                                      $"worldRot={t.eulerAngles:F1} localPos={t.localPosition:F2} " +
                                      $"frame={fCur} oldFrame={fOld} backlerp={backlerp:F2}");
                        }
                    }
                    // Runtime renderer visibility (camera has moved by now)
                    foreach (var r in _viewmodelRoot.GetComponentsInChildren<Renderer>(true))
                        Debug.Log($"[VMRender-rt] '{r.gameObject.name}' isVisible={r.isVisible} bounds={r.bounds.center:F1}");
                }
                return;
            }

            // --- Blend-shape animation (mesh models) ---
            // Blend shape i represents absolute MD3 frame (i+1); frame 0 = base mesh.
            foreach (var smr in _viewmodelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                int shapeCount = smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
                for (int i = 0; i < shapeCount; i++)
                {
                    int absFrame = i + 1;
                    float w = 0f;
                    if (absFrame == frame)    w += (1f - backlerp) * 100f;
                    if (absFrame == oldFrame) w += backlerp * 100f;
                    smr.SetBlendShapeWeight(i, w);
                }
            }
        }

        private void OnDestroy()
        {
            LocalPlayerActive = false;

            if (_viewmodelCam != null)
                Destroy(_viewmodelCam.gameObject);

            ServerInit.OnSpawnServer -= OnMapSpawn;
            ServerMain.OnGameRunFrame       -= OnGameRunFrame;
            SV_Client.OnClientEnterWorld    -= OnClientEnterWorld;
            SV_Client.OnClientThink         -= OnClientThink;

            ClientParse.OnSnapshotParsed -= OnSnapshotParsed;
            ClientParse.OnServerCommand  -= OnServerCommand;

            BotMain.OnBotCmd  -= OnBotCmd;
            BotMain.OnBotChat -= OnBotChat;
            WeaponSystem.OnFireWeapon -= OnWeaponFired;

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
            // Save ps.CommandTime BEFORE G_RunFrame runs ClientThink_real.
            // ClientThink_real sets ps.CommandTime = cmd.ServerTime (same as gc.LastCmd.ServerTime),
            // which would give Pmove msec = 0.  We restore the saved value afterwards so
            // Pmove always advances by one server frame (≤ 50 ms).
            // Only save when we're actually going to run Pmove (not while menu is open).
            int prevCmdTime = 0;
            if (LocalPlayerActive && !ETUIManager.MenuIsOpen)
            {
                var gc0 = ServerGameLogic.Clients[LocalClientNum];
                if (gc0 != null) prevCmdTime = gc0.PS.CommandTime;
            }

            // Run all entity thinks and client think-real (stores LastCmd, fires inactivity, etc.)
            ServerGameLogic.G_RunFrame(serverTime);

            // Run BG_Pmove for the local player so origin/velocity advance each server tick.
            // Do NOT run while the menu is open — player must not fall before pressing PLAY.
            if (!LocalPlayerActive || ETUIManager.MenuIsOpen) return;

            var gc = ServerGameLogic.Clients[LocalClientNum];
            if (gc == null) return;

            var ps = gc.PS;
            if (ps.PmType != GameConst.PM_NORMAL && ps.PmType != GameConst.PM_NOCLIP)
            {
                Debug.Log($"[ETGameManager] OnGameRunFrame: Pmove skipped PmType={ps.PmType}");
                return;
            }

            // Restore CommandTime and advance cmd.ServerTime to current tick.
            // Cap msec to one server frame (50 ms) to prevent a physics explosion
            // on the very first tick where prevCmdTime may be far in the past.
            int msec = serverTime - prevCmdTime;
            if (msec < 1)  msec = 1;
            if (msec > 50) msec = 50;
            ps.CommandTime        = serverTime - msec;
            gc.LastCmd.ServerTime = serverTime;

            _pmInput.Ps            = ps;
            _pmInput.Cmd           = gc.LastCmd;
            _pmInput.OldCmd        = gc.LastCmd;
            _pmInput.GameType      = CvarSystem.GetInt("g_gametype");
            _pmInput.TraceMask     = MASK_PLAYERSOLID;
            _pmInput.Trace         = CollisionSystem.DefaultTraceFunc;
            _pmInput.PointContents = CollisionSystem.DefaultPointContentsFunc;

            _pm.Pmove(_pmInput);

            // Diagnostic: log player state once per second to verify Pmove is running
            if (Time.frameCount % 60 == 0)
            {
                string mode = ps.PmType == GameConst.PM_NOCLIP ? "NOCLIP" : "NORMAL";
                Debug.Log($"[ETGameManager] Player[{mode}] " +
                          $"origin=({ps.Origin0:F1},{ps.Origin1:F1},{ps.Origin2:F1}) " +
                          $"vel=({ps.Velocity0:F1},{ps.Velocity1:F1},{ps.Velocity2:F1}) " +
                          $"ground={ps.GroundEntityNum} " +
                          $"fwd={gc.LastCmd.ForwardMove} rt={gc.LastCmd.RightMove} " +
                          $"pmType={ps.PmType} msec={msec}");
            }

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
