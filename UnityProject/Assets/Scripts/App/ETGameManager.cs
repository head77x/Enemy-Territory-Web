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
        private int       _viewmodelWeapon = GameConst.WP_NONE;
        private int       _viewmodelNumFrames    = 0;   // total MD3 frames including frame 0
        private float     _viewmodelAnimTime     = 0f;  // elapsed seconds for current anim range
        private int       _viewmodelLastWeapAnim = -1;  // last ps.WeapAnim value (for restart detection)
        private float     _viewmodelRecoilTime   = -1f; // >=0 while recoil anim plays; -1=idle
        private Vector3   _viewmodelBasePos;            // local position at rest
        private Quaternion _viewmodelBaseRot;           // local rotation at rest

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
            _viewmodelCam.nearClipPlane = 0.5f;
            _viewmodelCam.farClipPlane  = 600f;
            _viewmodelCam.fieldOfView   = 60f;
            _viewmodelCam.depth         = cam.depth + 1;

            _viewmodelRoot = new GameObject("ViewWeaponRoot").transform;
            _viewmodelRoot.SetParent(vmGo.transform, false);
        }

        /// <summary>
        /// Loads the real MD3 weapon model for <paramref name="weapon"/> from the
        /// PK3 virtual filesystem and attaches it to the viewmodel camera.
        /// Called every DriveLocalPlayer frame; no-ops if the weapon hasn't changed.
        /// </summary>
        private void UpdateViewmodel(int weapon)
        {
            if (weapon == _viewmodelWeapon) return;
            _viewmodelWeapon = weapon;

            // Remove old mesh
            if (_viewmodelRoot != null)
            {
                for (int i = _viewmodelRoot.childCount - 1; i >= 0; i--)
                    Destroy(_viewmodelRoot.GetChild(i).gameObject);
            }

            if (weapon <= GameConst.WP_NONE || _viewmodelRoot == null) return;

            var wri = ET.Client.CGameWeapons.CG_GetWeaponRenderInfo(weapon);
            if (wri == null || string.IsNullOrEmpty(wri.ModelPath)) return;

            bool usingFallback = false;
            string handPath = null;

            var go = RuntimeResourceLoader.LoadMd3(wri.ModelPath, _viewmodelRoot);
            if (go == null)
            {
                Debug.LogWarning($"[ETGameManager] Viewmodel MD3 not found: {wri.ModelPath}");

                // Fallback: akimbo sidearm viewmodels are always present in mp_bin.pk3.
                // AXIS → luger, Allies → colt.
                var ps = ServerGameLogic.Clients[LocalClientNum]?.PS;
                bool axis = ps != null && ps.TeamNum == ET.Game.DamageSystem.TEAM_AXIS;
                string fallbackGun  = axis
                    ? "models/weapons2/akimbo_luger/v_akimbo_luger.md3"
                    : "models/weapons2/akimbo_colt/v_akimbo_colt.md3";
                handPath = axis
                    ? "models/weapons2/akimbo_luger/v_akimbo_luger_hand.md3"
                    : "models/weapons2/akimbo_colt/v_akimbo_colt_hand.md3";

                go = RuntimeResourceLoader.LoadMd3(fallbackGun, _viewmodelRoot);
                if (go == null)
                {
                    Debug.LogWarning($"[ETGameManager] Fallback viewmodel also not found: {fallbackGun}");
                    return;
                }
                usingFallback = true;
                Debug.Log($"[ETGameManager] Using fallback viewmodel: {fallbackGun}");

                // Log tags for debugging
                foreach (Transform child in go.transform)
                    if (child.name.StartsWith("tag_", System.StringComparison.OrdinalIgnoreCase))
                        Debug.Log($"[ETGameManager] Found tag: {child.name} at {child.localPosition}");

                // Load the barrel (slide) sub-model and attach it to the tag on the main model.
                // The barrel is a separate MD3 that snaps onto tag_barrel or tag_weapon.
                string barrelPath = axis
                    ? "models/weapons2/akimbo_luger/v_akimbo_luger_barrel.md3"
                    : "models/weapons2/akimbo_colt/v_akimbo_colt_barrel.md3";
                LoadAndAttachSubModel(barrelPath, go, new[] { "tag_barrel", "tag_weapon", "tag_flash" });
            }

            // Position mirrors ET's CG_CalculateWeaponPosition offset:
            // +8 forward, +4 right, -8 up (quake units) → Unity (4, -8, 8).
            // The MD3 vertex coordinates already encode the gun's position relative to
            // this origin, so no further scaling is needed.
            go.transform.localPosition = new Vector3(4f, -8f, 8f);
            go.transform.localRotation = Quaternion.Euler(-5f, -10f, 0f);
            go.transform.localScale    = Vector3.one;

            SetLayerRecursive(go, ViewmodelLayer);

            // Capture total frame count from first SkinnedMeshRenderer found (blend shapes + 1 for frame 0)
            var firstSmr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            _viewmodelNumFrames = firstSmr != null ? firstSmr.sharedMesh.blendShapeCount + 1 : 0;
            _viewmodelAnimTime  = 0f;
            _viewmodelLastWeapAnim = -1;
            _viewmodelRecoilTime   = -1f;
            _viewmodelBasePos = go.transform.localPosition;
            _viewmodelBaseRot = go.transform.localRotation;
            Debug.Log($"[ETGameManager] Viewmodel frames loaded: {_viewmodelNumFrames} total (weapon={weapon})");

            // Also load the separate hand model for akimbo fallback weapons
            if (usingFallback && handPath != null)
            {
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

            // Viewmodel never casts or receives world shadows
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows    = false;
            }

            Debug.Log($"[ETGameManager] Viewmodel loaded: {wri.ModelPath}");
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

        // -------------------------------------------------------------------
        // Weapon animation frame ranges — ported from ET bg_misc.c bg_weaponlist[].
        // Each entry is (animState, startFrame, numFrames, fps, looping).
        // These are the frame ranges baked into each weapon's viewmodel MD3.
        // -------------------------------------------------------------------
        private struct WeapAnimRange
        {
            public int   Anim;
            public int   Start;
            public int   Count;
            public float Fps;
            public bool  Loop;
        }

        // Frame ranges for akimbo_colt / akimbo_luger viewmodel MD3s.
        // Source: ET bg_misc.c bg_weaponlist entries for WP_AKIMBO_COLT / WP_AKIMBO_LUGER.
        private static readonly WeapAnimRange[] s_AkimboAnimRanges = new WeapAnimRange[]
        {
            new WeapAnimRange { Anim = GameConst.WEAP_IDLE1,           Start =   0, Count = 40, Fps = 20f, Loop = true  },
            new WeapAnimRange { Anim = GameConst.WEAP_IDLE2,           Start =  40, Count =  4, Fps = 20f, Loop = true  },
            new WeapAnimRange { Anim = GameConst.WEAP_ATTACK1,         Start =  44, Count = 10, Fps = 20f, Loop = false },
            new WeapAnimRange { Anim = GameConst.WEAP_ATTACK2,         Start =  54, Count = 10, Fps = 20f, Loop = false },
            new WeapAnimRange { Anim = GameConst.WEAP_ATTACK_LASTSHOT, Start =  64, Count = 10, Fps = 20f, Loop = false },
            new WeapAnimRange { Anim = GameConst.WEAP_RAISE,           Start =  74, Count = 15, Fps = 20f, Loop = false },
            new WeapAnimRange { Anim = GameConst.WEAP_RELOAD1,         Start =  89, Count = 60, Fps = 20f, Loop = false },
        };

        // Returns the animation range for a given weapon and WEAP_* animState.
        // Falls back to IDLE1 if not found.
        private static WeapAnimRange GetWeapAnimRange(int weapon, int animState)
        {
            // All currently available models are akimbo variants — use same table.
            var table = s_AkimboAnimRanges;
            foreach (var r in table)
                if (r.Anim == animState) return r;
            return table[0]; // IDLE1 fallback
        }

        // Drives viewmodel animation by reading ps.WeapAnim (set by PlayerMovement).
        // When the MD3 has multiple frames, blend-shape morphing is used (one shape per frame).
        // When the MD3 has only 1 frame (no shapes — common with fallback akimbo models),
        // a transform-based recoil kick is used instead so firing is still visible.
        private void DriveViewmodelAnimation()
        {
            if (_viewmodelRoot == null) return;

            var gc = ServerGameLogic.Clients[LocalClientNum];
            int rawWeapAnim = gc?.PS?.WeapAnim ?? GameConst.WEAP_IDLE1;
            int animState   = rawWeapAnim & ~GameConst.ANIM_TOGGLEBIT;

            if (rawWeapAnim != _viewmodelLastWeapAnim)
            {
                Debug.Log($"[Viewmodel] WeapAnim changed: {_viewmodelLastWeapAnim} → {rawWeapAnim} (animState={animState} frames={_viewmodelNumFrames})");
                _viewmodelAnimTime     = 0f;
                _viewmodelLastWeapAnim = rawWeapAnim;

                // Trigger recoil kick whenever a fire anim starts.
                if (animState == GameConst.WEAP_ATTACK1 || animState == GameConst.WEAP_ATTACK2)
                    _viewmodelRecoilTime = 0f;
            }

            // ---- Fallback: transform recoil for single-frame models ----
            if (_viewmodelNumFrames <= 1)
            {
                if (_viewmodelRecoilTime >= 0f)
                {
                    const float recoilDur = 0.08f;  // seconds to peak
                    const float returnDur = 0.12f;  // seconds back to rest
                    const float totalDur  = recoilDur + returnDur;

                    _viewmodelRecoilTime += Time.deltaTime;

                    float t;
                    if (_viewmodelRecoilTime < recoilDur)
                    {
                        // Going to peak — move back and up in local space
                        t = _viewmodelRecoilTime / recoilDur;
                    }
                    else if (_viewmodelRecoilTime < totalDur)
                    {
                        // Returning to rest
                        t = 1f - (_viewmodelRecoilTime - recoilDur) / returnDur;
                    }
                    else
                    {
                        t = 0f;
                        _viewmodelRecoilTime = -1f;
                    }

                    // Ease in-out
                    t = t * t * (3f - 2f * t);

                    // Kick: backward (local -Z) and slightly up (local +Y)
                    Vector3 kick = new Vector3(0f, 0.8f, -2.0f) * t;
                    _viewmodelRoot.localPosition = _viewmodelBasePos + _viewmodelRoot.localRotation * kick;
                    _viewmodelRoot.localRotation = _viewmodelBaseRot * Quaternion.Euler(-6f * t, 0f, 0f);
                }
                else
                {
                    // Snap back to base when idle (avoid drift)
                    _viewmodelRoot.localPosition = _viewmodelBasePos;
                    _viewmodelRoot.localRotation = _viewmodelBaseRot;
                }
                return;
            }

            // ---- Blend-shape morph-target animation (multi-frame MD3) ----
            var range = GetWeapAnimRange(_viewmodelWeapon, animState);

            int rangeStart = Mathf.Min(range.Start, _viewmodelNumFrames - 1);
            int rangeCount = Mathf.Min(range.Count, _viewmodelNumFrames - rangeStart);
            if (rangeCount <= 0) rangeCount = 1;

            _viewmodelAnimTime += Time.deltaTime * range.Fps;
            int localFrame = range.Loop
                ? (int)_viewmodelAnimTime % rangeCount
                : Mathf.Min((int)_viewmodelAnimTime, rangeCount - 1);
            int frameIndex = rangeStart + localFrame;

            foreach (var smr in _viewmodelRoot.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                int shapeCount = smr.sharedMesh != null ? smr.sharedMesh.blendShapeCount : 0;
                for (int i = 0; i < shapeCount; i++)
                    smr.SetBlendShapeWeight(i, (i + 1 == frameIndex) ? 100f : 0f);
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
