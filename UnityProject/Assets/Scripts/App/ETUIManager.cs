// ETUIManager — Unity IMGUI front-end for all ET UI systems.
//
// Subscribes to events fired by UISystem, CGameView, ClientConsole,
// CGameScoreboard, CGamePopupMessages, and CGameLimboPanel, then renders
// every screen (HUD, menus, console, scoreboard, limbo) via OnGUI.
//
// Attach this component to the same GameObject as ETGameManager.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ET.Client;
using ET.Core;
using ET.Game;
using ET.Network;

namespace ET.App
{
    [DisallowMultipleComponent]
    public class ETUIManager : MonoBehaviour
    {
        // =====================================================================
        // Inspector
        // =====================================================================
        [Header("HUD Colors")]
        public Color HealthColor   = new Color(0.85f, 0.15f, 0.15f);
        public Color AmmoColor     = new Color(0.95f, 0.80f, 0.10f);
        public Color StaminaColor  = new Color(0.20f, 0.75f, 0.30f);
        public Color AlliesColor   = new Color(0.25f, 0.50f, 0.95f);
        public Color AxisColor     = new Color(0.95f, 0.25f, 0.25f);

        // =====================================================================
        // Private state
        // =====================================================================

        // HUD
        private HudData    _hud;
        private bool       _hudActive;
        private bool       _hudDataReady;

        // Menu
        private UIMenu     _currentMenu;
        private bool       _menuVisible;
        private UIMenuType _menuType;

        // Console
        private bool       _consoleVisible;
        private float      _consoleFrac;
        private string     _consoleInput = "";

        // Scoreboard
        private bool                    _scoreboardVisible;
        private List<ScoreboardEntry>   _scores = new();
        private int                     _score1, _score2;

        // Popup messages
        private PopupMessage[] _popups;

        // Limbo panel
        private bool        _limboVisible;
        private LimboState  _limboState;

        // Free-camera (spectator) mode — active when no menu is open and player not spawned
        private bool  _freeCamActive;
        private float _camYaw;
        private float _camPitch;
        private const float CamMouseSens = 2f;
        private const float CamMoveSpeed = 400f;

        // Exposed so ETGameManager can check whether a menu blocks input
        public static bool MenuIsOpen { get; private set; }

        // IMGUI styles — lazily initialized in OnGUI
        private GUIStyle _boxStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _smallStyle;
        private bool     _stylesReady;

        // Scaling from 640×480 logical space to actual screen
        private float ScaleX => Screen.width  / 640f;
        private float ScaleY => Screen.height / 480f;

        // =====================================================================
        // Unity lifecycle
        // =====================================================================

        private void Awake()
        {
            CGameView.OnDrawHUD                    += HandleHUD;
            UISystem.OnMenuDraw                    += HandleMenuDraw;
            UISystem.OnMenuChanged                 += HandleMenuChanged;
            UISystem.OnMenuClosed                  += HandleMenuClosed;
            ClientConsole.OnDrawConsole            += HandleConsole;
            CGameScoreboard.OnScoreboardDraw       += HandleScoreboardDraw;
            CGameScoreboard.OnScoreboardHide       += HandleScoreboardHide;
            CGamePopupMessages.OnPopupMessagesDraw += HandlePopups;
            CGameLimboPanel.OnLimboPanelOpen       += HandleLimboOpen;
            CGameLimboPanel.OnLimboPanelClose      += HandleLimboClose;
            CGameLimboPanel.OnLimboPanelDraw       += HandleLimboDraw;
        }

        private void OnDestroy()
        {
            CGameView.OnDrawHUD                    -= HandleHUD;
            UISystem.OnMenuDraw                    -= HandleMenuDraw;
            UISystem.OnMenuChanged                 -= HandleMenuChanged;
            UISystem.OnMenuClosed                  -= HandleMenuClosed;
            ClientConsole.OnDrawConsole            -= HandleConsole;
            CGameScoreboard.OnScoreboardDraw       -= HandleScoreboardDraw;
            CGameScoreboard.OnScoreboardHide       -= HandleScoreboardHide;
            CGamePopupMessages.OnPopupMessagesDraw -= HandlePopups;
            CGameLimboPanel.OnLimboPanelOpen       -= HandleLimboOpen;
            CGameLimboPanel.OnLimboPanelClose      -= HandleLimboClose;
            CGameLimboPanel.OnLimboPanelDraw       -= HandleLimboDraw;
        }

        private void Start()
        {
            // Show main menu on startup
            UISystem.UI_SetActiveMenu(UIMenuType.Main);
            // Ensure the static gate matches actual menu state even if events fire early
            MenuIsOpen = _menuVisible;
            Debug.Log($"[ETUIManager] Start — menuVisible={_menuVisible} menuType={_menuType} currentMenu={(UISystem.CurrentMenu != null ? UISystem.CurrentMenu.Type.ToString() : "null")}");
        }

        private void Update()
        {
            // Console toggle — backtick
            if (Input.GetKeyDown(KeyCode.BackQuote))
            {
                ClientConsole.ToggleConsole();
                _consoleInput = "";
                if (_freeCamActive)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible   = true;
                }
            }

            if (_consoleVisible)
            {
                HandleConsoleKeyInput();
                return; // consume all keys while console is open
            }

            // ESC — open ingame menu or pop current menu
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_menuVisible)
                {
                    UISystem.UI_PopMenu();
                }
                else
                {
                    // Pause free-cam and open ingame/main menu
                    if (_freeCamActive)
                    {
                        Cursor.lockState = CursorLockMode.None;
                        Cursor.visible   = true;
                    }
                    UISystem.UI_SetActiveMenu(_hudActive ? UIMenuType.Ingame : UIMenuType.Main);
                }
            }

            // While a menu is open don't drive free-cam
            if (_menuVisible) return;

            // Scoreboard — hold TAB
            if (Input.GetKeyDown(KeyCode.Tab))
                CGameScoreboard.CG_ShowScoreboard();
            if (Input.GetKeyUp(KeyCode.Tab))
                CGameScoreboard.CG_HideScoreboard();

            // Limbo panel — L key
            if (Input.GetKeyDown(KeyCode.L) && _hudActive)
                CGameLimboPanel.CG_LimboPanelOpen();

            // Free-camera movement — skipped once local player has spawned
            if (_freeCamActive && !ETGameManager.LocalPlayerActive)
                HandleFreeCameraInput();
        }

        private void HandleFreeCameraInput()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Mouse look (only when cursor is locked)
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                _camYaw   += Input.GetAxis("Mouse X") * CamMouseSens;
                _camPitch -= Input.GetAxis("Mouse Y") * CamMouseSens;
                _camPitch = Mathf.Clamp(_camPitch, -89f, 89f);
                cam.transform.rotation = Quaternion.Euler(_camPitch, _camYaw, 0f);
            }

            // WASD + EQ for up/down; Shift to sprint
            float speed = CamMoveSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                speed *= 4f;

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W))            move += cam.transform.forward;
            if (Input.GetKey(KeyCode.S))            move -= cam.transform.forward;
            if (Input.GetKey(KeyCode.A))            move -= cam.transform.right;
            if (Input.GetKey(KeyCode.D))            move += cam.transform.right;
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space))        move += Vector3.up;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl))  move -= Vector3.up;

            cam.transform.position += move * speed;
        }

        // =====================================================================
        // Event handlers — store received data for OnGUI
        // =====================================================================

        private void HandleHUD(PlayerState ps)
        {
            _hud          = CGameView.CG_BuildHudData();
            _hudActive    = true;
            _hudDataReady = true;
        }

        private void HandleMenuDraw(UIMenu menu)   => _currentMenu = menu;
        private void HandleMenuChanged(UIMenuType t)
        {
            _menuType    = t;
            _menuVisible = t != UIMenuType.None;
            MenuIsOpen   = _menuVisible;
            _currentMenu = _menuVisible ? UISystem.CurrentMenu : null;
            // Release cursor when menu opens; re-lock when menu closes via HandleMenuClosed
            if (_menuVisible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible   = true;
            }
        }
        private void HandleMenuClosed()
        {
            _menuVisible = false;
            MenuIsOpen   = false;
            _currentMenu = null;
            _hudActive   = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
            Debug.Log($"[ETUIManager] HandleMenuClosed — LocalPlayerActive={ETGameManager.LocalPlayerActive} MenuIsOpen={MenuIsOpen}");

            // Only activate free-cam if the local player hasn't spawned yet.
            // Once spawned, ETGameManager drives the camera from PlayerState.
            if (!ETGameManager.LocalPlayerActive)
            {
                _freeCamActive = true;
                var cam = Camera.main;
                if (cam != null)
                {
                    _camYaw   = cam.transform.eulerAngles.y;
                    _camPitch = cam.transform.eulerAngles.x;
                    if (cam.transform.position.y > 50f)
                        cam.transform.position = new Vector3(cam.transform.position.x, 50f, cam.transform.position.z);
                }
            }
        }

        private void HandleConsole(float frac)
        {
            _consoleFrac    = frac;
            _consoleVisible = frac > 0.01f;
        }

        private void HandleScoreboardDraw(List<ScoreboardEntry> entries, int s1, int s2)
        {
            _scoreboardVisible = true;
            _scores  = entries;
            _score1  = s1;
            _score2  = s2;
        }

        private void HandleScoreboardHide() => _scoreboardVisible = false;

        private void HandlePopups(PopupMessage[] msgs) => _popups = msgs;

        private void HandleLimboOpen()               => _limboVisible = true;
        private void HandleLimboClose()              => _limboVisible = false;
        private void HandleLimboDraw(LimboState s)   => _limboState = s;

        // =====================================================================
        // Console keyboard input (called from Update when console is open)
        // =====================================================================

        private void HandleConsoleKeyInput()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                string cmd = _consoleInput.Trim();
                if (cmd.Length > 0)
                {
                    ClientConsole.AddHistory(cmd);
                    ClientConsole.Print($"] {cmd}\n");
                    CmdSystem.Cbuf_AddText(cmd);
                }
                _consoleInput = "";
            }
            else if (Input.GetKeyDown(KeyCode.Backspace) && _consoleInput.Length > 0)
            {
                _consoleInput = _consoleInput.Substring(0, _consoleInput.Length - 1);
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                _consoleInput = ClientConsole.GetPrevHistory();
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                _consoleInput = ClientConsole.GetNextHistory();
            }
            else if (Input.GetKeyDown(KeyCode.PageUp))
            {
                ClientConsole.PageUp();
            }
            else if (Input.GetKeyDown(KeyCode.PageDown))
            {
                ClientConsole.PageDown();
            }
            else
            {
                foreach (char c in Input.inputString)
                    if (c >= 32 && c != '`')
                        _consoleInput += c;
            }
        }

        // =====================================================================
        // OnGUI — main render entry point
        // =====================================================================

        private bool _onGuiLogged;
        private void OnGUI()
        {
            if (!_onGuiLogged)
            {
                _onGuiLogged = true;
                Debug.Log($"[ETUIManager] OnGUI first call — menuVisible={_menuVisible} currentMenu={(_currentMenu != null ? _currentMenu.Type.ToString() : "null")} hudActive={_hudActive}");
            }
            EnsureStyles();

            // Scale all drawing from 640×480 logical space to screen resolution
            Matrix4x4 prevMatrix = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(ScaleX, ScaleY), Vector2.zero);

            // Layering order (back → front):
            // HUD → popup messages → limbo → scoreboard → menu → console
            if (_hudActive && !_menuVisible)
                DrawHUD();

            if (_popups != null)
                DrawPopupMessages();

            if (_limboVisible && _limboState != null)
                DrawLimboPanel();

            if (_scoreboardVisible)
                DrawScoreboard();

            if (_menuVisible && _currentMenu != null)
                DrawMenu(_currentMenu);

            if (_consoleVisible)
                DrawConsole();

            GUI.matrix = prevMatrix;
        }

        // =====================================================================
        // HUD
        // =====================================================================

        private void DrawHUD()
        {
            int fps = Mathf.RoundToInt(1f / Mathf.Max(Time.deltaTime, 0.0001f));
            GUI.Label(new Rect(4, 4, 60, 14), $"FPS {fps}", _smallStyle);

            if (ETGameManager.LocalPlayerActive)
            {
                // Show live player state directly from ServerGameLogic
                var gc = ET.Server.ServerGameLogic.Clients[0];
                if (gc != null)
                {
                    var ps = gc.PS;
                    int hp    = ps.Stats[ET.Game.GameConst.STAT_HEALTH];
                    int maxHp = 100;
                    DrawFilledBar(10, 440, 120, 14, HealthColor, 0.25f,
                        maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f);
                    GUI.Label(new Rect(10, 440, 120, 14), $"HP  {hp}", _smallStyle);

                    GUI.Label(new Rect(4, 20, 300, 14),
                        $"WASD=move  Mouse=look  LMB=fire  ESC=menu", _smallStyle);

                    // Position debug
                    GUI.Label(new Rect(4, 36, 300, 14),
                        $"pos ({ps.Origin0:F0}, {ps.Origin1:F0}, {ps.Origin2:F0})", _smallStyle);
                }
                return;
            }

            if (_freeCamActive)
            {
                GUI.Label(new Rect(4, 20, 200, 14), "WASD/EQ to move  Shift=sprint  ESC=menu", _smallStyle);
                return;
            }
            if (!_hudDataReady) return;

            // ---- Health bar (bottom-left) ----
            DrawFilledBar(10, 440, 120, 14, HealthColor, 0.25f,
                _hud.MaxHealth > 0 ? (float)_hud.Health / _hud.MaxHealth : 0f);
            GUI.Label(new Rect(10, 440, 120, 14),
                $"HP  {_hud.Health}", _smallStyle);

            // ---- Stamina bar (bottom-left, above health) ----
            DrawFilledBar(10, 424, 120, 8, StaminaColor, 0.20f, _hud.Stamina);

            // ---- Ammo display (bottom-right) ----
            string ammoText = _hud.ClipAmmo >= 0
                ? $"{_hud.ClipAmmo} / {_hud.Ammo}"
                : $"{_hud.Ammo}";

            var ammoRect = new Rect(510, 440, 120, 14);
            DrawFilledBar(510, 440, 120, 14, AmmoColor, 0.20f,
                _hud.MaxAmmo > 0 ? (float)_hud.Ammo / _hud.MaxAmmo : 0f);
            GUI.Label(ammoRect, $"AMMO  {ammoText}", _smallStyle);

            // ---- Team indicator (top-right corner) ----
            if (_hud.Team == 1 || _hud.Team == 2)
            {
                Color teamCol  = _hud.Team == 1 ? AlliesColor : AxisColor;
                string teamStr = _hud.Team == 1 ? "ALLIES" : "AXIS";
                DrawColorBox(new Rect(580, 4, 56, 14), new Color(teamCol.r, teamCol.g, teamCol.b, 0.6f));
                GUI.Label(new Rect(580, 4, 56, 14), teamStr, _smallStyle);
            }

        }

        // =====================================================================
        // Popup messages (kill feed, objectives, XP)
        // =====================================================================

        private void DrawPopupMessages()
        {
            float y = 20f;
            float now = Time.time;
            foreach (var msg in _popups)
            {
                if (msg == null || string.IsNullOrEmpty(msg.Text)) continue;
                if (now > msg.AddTime + msg.Duration) continue;

                Color c = msg.Color;
                Color prev = GUI.color;
                GUI.color = new Color(c.r, c.g, c.b, 0.9f);
                float textW = _labelStyle.CalcSize(new GUIContent(msg.Text)).x + 10f;
                float x = 320f - textW * 0.5f;
                DrawColorBox(new Rect(x - 4, y - 1, textW + 8, 16), new Color(0, 0, 0, 0.55f));
                GUI.Label(new Rect(x, y, textW, 16), msg.Text, _labelStyle);
                GUI.color = prev;
                y += 18f;
            }
        }

        // =====================================================================
        // Scoreboard
        // =====================================================================

        private void DrawScoreboard()
        {
            DrawColorBox(new Rect(40, 30, 560, 420), new Color(0.02f, 0.02f, 0.05f, 0.93f));
            DrawOutline(new Rect(40, 30, 560, 420), new Color(0.4f, 0.4f, 0.5f, 0.8f));

            GUI.Label(new Rect(200, 36, 240, 22), "SCOREBOARD", _titleStyle);

            // Team score bar
            DrawColorBox(new Rect(40, 56, 270, 20), new Color(AlliesColor.r, AlliesColor.g, AlliesColor.b, 0.4f));
            GUI.Label(new Rect(44, 56, 120, 20), $"ALLIES  {_score1}", _labelStyle);

            DrawColorBox(new Rect(330, 56, 270, 20), new Color(AxisColor.r, AxisColor.g, AxisColor.b, 0.4f));
            GUI.Label(new Rect(334, 56, 120, 20), $"AXIS  {_score2}", _labelStyle);

            // Column headers
            float hdrY = 80f;
            GUI.Label(new Rect(50,  hdrY, 180, 14), "Name",   _smallStyle);
            GUI.Label(new Rect(230, hdrY, 40,  14), "Score",  _smallStyle);
            GUI.Label(new Rect(280, hdrY, 40,  14), "K",      _smallStyle);
            GUI.Label(new Rect(310, hdrY, 40,  14), "D",      _smallStyle);
            GUI.Label(new Rect(350, hdrY, 40,  14), "Ping",   _smallStyle);
            DrawColorBox(new Rect(44, hdrY + 14, 552, 1), new Color(0.5f, 0.5f, 0.6f, 0.5f));

            float rowY = 98f;
            int lastTeam = -1;
            foreach (var e in _scores)
            {
                if (e == null) continue;
                if (rowY > 420f) break;

                // Team separator
                if (e.Team != lastTeam && lastTeam != -1)
                {
                    DrawColorBox(new Rect(44, rowY, 552, 1), new Color(0.4f, 0.4f, 0.5f, 0.4f));
                    rowY += 4f;
                }
                lastTeam = e.Team;

                Color rowBg = e.Team == 1
                    ? new Color(AlliesColor.r, AlliesColor.g, AlliesColor.b, 0.12f)
                    : new Color(AxisColor.r,   AxisColor.g,   AxisColor.b,   0.12f);
                DrawColorBox(new Rect(44, rowY, 552, 16), rowBg);

                GUI.Label(new Rect(50,  rowY, 175, 16), e.Name   ?? "?",          _smallStyle);
                GUI.Label(new Rect(230, rowY, 40,  16), e.Score.ToString(),        _smallStyle);
                GUI.Label(new Rect(280, rowY, 40,  16), e.Kills.ToString(),        _smallStyle);
                GUI.Label(new Rect(310, rowY, 40,  16), e.Deaths.ToString(),       _smallStyle);
                GUI.Label(new Rect(350, rowY, 40,  16), e.Ping.ToString() + " ms", _smallStyle);

                rowY += 18f;
            }
        }

        // =====================================================================
        // Limbo panel (spawn selection)
        // =====================================================================

        private static readonly string[] TeamNames  = { "", "ALLIES", "AXIS" };
        private static readonly string[] ClassNames =
            { "Soldier", "Medic", "Engineer", "Field Ops", "Covert Ops" };

        private void DrawLimboPanel()
        {
            const float W = 360f, H = 280f;
            float x = (640f - W) * 0.5f, y = (480f - H) * 0.5f;

            DrawColorBox(new Rect(x, y, W, H), new Color(0.04f, 0.04f, 0.07f, 0.95f));
            DrawOutline(new Rect(x, y, W, H), new Color(0.5f, 0.5f, 0.6f, 0.9f));
            GUI.Label(new Rect(x, y + 4f, W, 22f), "SPAWN SELECTION", _titleStyle);

            // Team selection
            GUI.Label(new Rect(x + 10f, y + 30f, 100f, 16f), "Team:", _smallStyle);
            if (GUI.Button(new Rect(x + 10f, y + 48f, 80f, 24f), "Allies", _buttonStyle))
                CGameLimboPanel.CG_LimboPanelSelectTeam(1);
            if (GUI.Button(new Rect(x + 100f, y + 48f, 80f, 24f), "Axis", _buttonStyle))
                CGameLimboPanel.CG_LimboPanelSelectTeam(2);

            string selTeam = _limboState.SelectedTeam >= 1 && _limboState.SelectedTeam <= 2
                ? TeamNames[_limboState.SelectedTeam] : "None";
            GUI.Label(new Rect(x + 190f, y + 52f, 160f, 16f), $"Selected: {selTeam}", _smallStyle);

            // Class selection
            GUI.Label(new Rect(x + 10f, y + 84f, 100f, 16f), "Class:", _smallStyle);
            for (int i = 0; i < ClassNames.Length; i++)
            {
                float bx = x + 10f + (i % 3) * 108f;
                float by = y + 102f + (i / 3) * 28f;
                if (GUI.Button(new Rect(bx, by, 100f, 22f), ClassNames[i], _buttonStyle))
                    CGameLimboPanel.CG_LimboPanelSelectClass(i);
            }

            string selClass = _limboState.SelectedClass >= 0 && _limboState.SelectedClass < ClassNames.Length
                ? ClassNames[_limboState.SelectedClass] : "None";
            GUI.Label(new Rect(x + 10f, y + 162f, W - 20f, 16f),
                $"Class: {selClass}", _smallStyle);

            // Spawn timer
            float spawnSec = Mathf.Max(0f, (_limboState.SpawnTimer - Time.time * 1000f) / 1000f);
            if (spawnSec > 0)
                GUI.Label(new Rect(x + 10f, y + 182f, W - 20f, 16f),
                    $"Spawning in {spawnSec:F0}s", _smallStyle);

            // Confirm / Cancel
            if (GUI.Button(new Rect(x + 60f, y + H - 36f, 100f, 28f), "Spawn", _buttonStyle))
            {
                CGameLimboPanel.CG_LimboPanelConfirm();
                CGameLimboPanel.CG_LimboPanelClose();
            }
            if (GUI.Button(new Rect(x + 200f, y + H - 36f, 100f, 28f), "Cancel", _buttonStyle))
                CGameLimboPanel.CG_LimboPanelClose();
        }

        // =====================================================================
        // Menu system
        // =====================================================================

        private void DrawMenu(UIMenu menu)
        {
            bool fullScreen = menu.Type == UIMenuType.Main || menu.Type == UIMenuType.Credits;

            if (fullScreen)
                DrawColorBox(new Rect(0, 0, 640, 480), new Color(0.01f, 0.01f, 0.03f, 0.97f));

            switch (menu.Type)
            {
                case UIMenuType.Main:    DrawMainMenu();    break;
                case UIMenuType.Ingame:  DrawIngameMenu();  break;
                case UIMenuType.Options: DrawOptionsMenu(); break;
                case UIMenuType.Team:    DrawTeamMenu();    break;
                case UIMenuType.Class:   DrawClassMenu();   break;
                case UIMenuType.Credits: DrawCreditsScreen();break;
                default:
                    DrawGenericMenu(menu);
                    break;
            }
        }

        private void DrawMainMenu()
        {
            // Title
            GUI.Label(new Rect(0, 60, 640, 50),
                "WOLFENSTEIN: ENEMY TERRITORY", _titleStyle);
            GUI.Label(new Rect(0, 108, 640, 20),
                "Unity Web Port", _labelStyle);

            // Menu panel
            const float W = 220f, H = 220f;
            float x = (640f - W) * 0.5f, y = 155f;
            DrawColorBox(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.05f, 0.05f, 0.10f, 0.88f));
            DrawOutline(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.4f, 0.4f, 0.55f, 0.7f));

            float bw = W, bh = 30f, gap = 8f;
            float by = y;

            if (MenuButton(new Rect(x, by, bw, bh), "PLAY"))
            {
                UISystem.UI_ForceMenuOff();
            }
            by += bh + gap;

            if (MenuButton(new Rect(x, by, bw, bh), "TEAM SELECT"))
                UISystem.UI_SetActiveMenu(UIMenuType.Team);
            by += bh + gap;

            if (MenuButton(new Rect(x, by, bw, bh), "OPTIONS"))
                UISystem.UI_SetActiveMenu(UIMenuType.Options);
            by += bh + gap;

            if (MenuButton(new Rect(x, by, bw, bh), "CREDITS"))
                UISystem.UI_SetActiveMenu(UIMenuType.Credits);
            by += bh + gap;

            if (MenuButton(new Rect(x, by, bw, bh), "QUIT"))
                Application.Quit();
        }

        private void DrawIngameMenu()
        {
            const float W = 200f, H = 200f;
            float x = (640f - W) * 0.5f, y = (480f - H) * 0.5f;
            DrawColorBox(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.04f, 0.04f, 0.08f, 0.92f));
            DrawOutline(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.4f, 0.4f, 0.55f, 0.7f));

            GUI.Label(new Rect(x, y - 4f, W, 22f), "MENU", _titleStyle);

            float bw = W, bh = 28f, gap = 8f;
            float by = y + 22f;

            if (MenuButton(new Rect(x, by, bw, bh), "RESUME"))
                UISystem.UI_ForceMenuOff();
            by += bh + gap;

            if (MenuButton(new Rect(x, by, bw, bh), "OPTIONS"))
                UISystem.UI_SetActiveMenu(UIMenuType.Options);
            by += bh + gap;

            if (MenuButton(new Rect(x, by, bw, bh), "DISCONNECT"))
            {
                UISystem.UI_ForceMenuOff();
                CmdSystem.Cbuf_AddText("disconnect");
            }
            by += bh + gap;

            if (MenuButton(new Rect(x, by, bw, bh), "QUIT"))
                Application.Quit();
        }

        private void DrawOptionsMenu()
        {
            const float W = 320f, H = 220f;
            float x = (640f - W) * 0.5f, y = (480f - H) * 0.5f;
            DrawColorBox(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.04f, 0.04f, 0.08f, 0.92f));
            DrawOutline(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.4f, 0.4f, 0.55f, 0.7f));

            GUI.Label(new Rect(x, y - 4f, W, 22f), "OPTIONS", _titleStyle);

            float ry = y + 24f;

            // Master volume
            GUI.Label(new Rect(x, ry, 130f, 18f), "Master Volume", _smallStyle);
            float vol = AudioListener.volume;
            float newVol = GUI.HorizontalSlider(new Rect(x + 140f, ry + 4f, 140f, 12f), vol, 0f, 1f);
            if (Math.Abs(newVol - vol) > 0.001f) AudioListener.volume = newVol;
            GUI.Label(new Rect(x + 288f, ry, 32f, 18f), $"{(int)(newVol * 100)}", _smallStyle);
            ry += 28f;

            // Mouse sensitivity placeholder
            GUI.Label(new Rect(x, ry, 130f, 18f), "Sensitivity", _smallStyle);
            float sens = CvarSystem.GetFloat("sensitivity");
            if (sens <= 0f) sens = 5f;
            float newSens = GUI.HorizontalSlider(new Rect(x + 140f, ry + 4f, 140f, 12f), sens, 1f, 20f);
            if (Math.Abs(newSens - sens) > 0.01f)
                CvarSystem.Set("sensitivity", newSens.ToString("F1"));
            GUI.Label(new Rect(x + 288f, ry, 32f, 18f), $"{newSens:F1}", _smallStyle);
            ry += 28f;

            // Fullscreen toggle
            GUI.Label(new Rect(x, ry, 130f, 18f), "Fullscreen", _smallStyle);
            bool fs = Screen.fullScreen;
            bool newFs = GUI.Toggle(new Rect(x + 140f, ry + 2f, 18f, 18f), fs, "");
            if (newFs != fs)
                Screen.fullScreen = newFs;
            ry += 36f;

            if (MenuButton(new Rect(x + W * 0.5f - 50f, ry, 100f, 28f), "BACK"))
                UISystem.UI_PopMenu();
        }

        private void DrawTeamMenu()
        {
            const float W = 300f, H = 160f;
            float x = (640f - W) * 0.5f, y = (480f - H) * 0.5f;
            DrawColorBox(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.04f, 0.04f, 0.08f, 0.92f));
            DrawOutline(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.4f, 0.4f, 0.55f, 0.7f));

            GUI.Label(new Rect(x, y - 4f, W, 22f), "SELECT TEAM", _titleStyle);

            float by = y + 30f;
            if (MenuButton(new Rect(x + 20f, by, 110f, 36f), "ALLIES"))
            {
                CGameLimboPanel.CG_LimboPanelSelectTeam(1);
                UISystem.UI_SetActiveMenu(UIMenuType.Class);
            }
            if (MenuButton(new Rect(x + W - 130f, by, 110f, 36f), "AXIS"))
            {
                CGameLimboPanel.CG_LimboPanelSelectTeam(2);
                UISystem.UI_SetActiveMenu(UIMenuType.Class);
            }
            by += 52f;
            if (MenuButton(new Rect(x + (W - 100f) * 0.5f, by + 20f, 100f, 28f), "BACK"))
                UISystem.UI_PopMenu();
        }

        private void DrawClassMenu()
        {
            const float W = 340f, H = 220f;
            float x = (640f - W) * 0.5f, y = (480f - H) * 0.5f;
            DrawColorBox(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.04f, 0.04f, 0.08f, 0.92f));
            DrawOutline(new Rect(x - 10f, y - 8f, W + 20f, H + 16f),
                new Color(0.4f, 0.4f, 0.55f, 0.7f));

            GUI.Label(new Rect(x, y - 4f, W, 22f), "SELECT CLASS", _titleStyle);

            float bw = 140f, bh = 28f, gap = 8f;
            float by = y + 26f;
            for (int i = 0; i < ClassNames.Length; i++)
            {
                float bx = x + (i % 2 == 0 ? 10f : W - bw - 10f);
                if (i == ClassNames.Length - 1) bx = x + (W - bw) * 0.5f;
                if (MenuButton(new Rect(bx, by, bw, bh), ClassNames[i]))
                {
                    CGameLimboPanel.CG_LimboPanelSelectClass(i);
                    UISystem.UI_ForceMenuOff();
                }
                if (i % 2 == 1 || i == ClassNames.Length - 1) by += bh + gap;
            }
            by += 8f;
            if (MenuButton(new Rect(x + (W - 100f) * 0.5f, by, 100f, 28f), "BACK"))
                UISystem.UI_PopMenu();
        }

        private void DrawCreditsScreen()
        {
            GUI.Label(new Rect(0, 60, 640, 50), "CREDITS", _titleStyle);
            string[] lines =
            {
                "Wolfenstein: Enemy Territory",
                "© 1999–2010 id Software LLC / Splash Damage",
                "Released as GPLv3 open source",
                "",
                "Unity Port",
                "Enemy Territory Web Project",
                "",
                "Built with Unity Engine",
            };
            float y = 130f;
            foreach (var line in lines)
            {
                GUI.Label(new Rect(0, y, 640, 20), line, _labelStyle);
                y += 22f;
            }
            if (MenuButton(new Rect(270f, 400f, 100f, 30f), "BACK"))
                UISystem.UI_PopMenu();
        }

        private void DrawGenericMenu(UIMenu menu)
        {
            const float W = 240f;
            float totalH = 30f + menu.Items.Count * 38f + 20f;
            float x = (640f - W) * 0.5f, y = (480f - totalH) * 0.5f;

            DrawColorBox(new Rect(x - 10f, y - 8f, W + 20f, totalH + 16f),
                new Color(0.04f, 0.04f, 0.08f, 0.92f));
            DrawOutline(new Rect(x - 10f, y - 8f, W + 20f, totalH + 16f),
                new Color(0.4f, 0.4f, 0.55f, 0.7f));

            GUI.Label(new Rect(x, y, W, 22f), menu.Title ?? menu.Type.ToString(), _titleStyle);

            float by = y + 28f;
            foreach (var item in menu.Items)
            {
                if (!item.Visible) continue;
                if (MenuButton(new Rect(x, by, W, 30f), item.Text ?? item.Name))
                    UISystem.UI_ExecuteMenuItem(item);
                by += 38f;
            }
        }

        // =====================================================================
        // Developer console
        // =====================================================================

        private void DrawConsole()
        {
            float h = _consoleFrac * 240f;
            if (h < 2f) return;

            // Background
            DrawColorBox(new Rect(0, 0, 640, h), new Color(0.02f, 0.02f, 0.06f, 0.93f));
            DrawColorBox(new Rect(0, h, 640, 2), new Color(0.45f, 0.45f, 0.60f, 1f));

            // Header
            GUI.Label(new Rect(4, 2, 400, 14),
                "Enemy Territory — Developer Console", _smallStyle);

            // Console text lines
            var con = ClientConsole.Con;
            int lineH = 12;
            int visLines = Mathf.FloorToInt((h - 30f) / lineH);
            int startLine = Mathf.Max(0, con.Current - visLines);

            for (int i = 0; i < visLines; i++)
            {
                int lineIdx = startLine + i;
                if (lineIdx >= con.TotalLines) break;
                int start = (lineIdx % con.TotalLines) * con.LineWidth;
                int end   = Mathf.Min(start + con.LineWidth, con.Text.Length);
                if (start >= con.Text.Length) break;

                var sb = new StringBuilder();
                for (int j = start; j < end; j++)
                {
                    char c = con.Text[j];
                    if (c == 0) break;
                    sb.Append(c);
                }
                string line = sb.ToString().TrimEnd();
                if (line.Length > 0)
                    GUI.Label(new Rect(4, h - 28f - (visLines - i) * lineH, 632, lineH), line, _smallStyle);
            }

            // Input line
            DrawColorBox(new Rect(0, h - 18f, 640, 18f), new Color(0.06f, 0.06f, 0.12f, 0.95f));
            GUI.Label(new Rect(4, h - 16f, 12, 14), "]", _smallStyle);
            GUI.Label(new Rect(16, h - 16f, 610, 14),
                _consoleInput + (Mathf.Sin(Time.time * 4f) > 0 ? "_" : " "), _smallStyle);
        }

        // =====================================================================
        // IMGUI helper methods
        // =====================================================================

        private void DrawFilledBar(float x, float y, float w, float h,
                                   Color fillColor, float bgAlpha, float fraction)
        {
            // Background
            DrawColorBox(new Rect(x, y, w, h), new Color(0.1f, 0.1f, 0.1f, bgAlpha));
            // Fill
            float fw = Mathf.Clamp01(fraction) * (w - 2f);
            if (fw > 0)
                DrawColorBox(new Rect(x + 1f, y + 1f, fw, h - 2f),
                    new Color(fillColor.r, fillColor.g, fillColor.b, 0.80f));
        }

        private void DrawColorBox(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void DrawOutline(Rect r, Color c)
        {
            // top, bottom, left, right borders (1px)
            DrawColorBox(new Rect(r.x,              r.y,              r.width, 1), c);
            DrawColorBox(new Rect(r.x,              r.yMax - 1,       r.width, 1), c);
            DrawColorBox(new Rect(r.x,              r.y,              1, r.height), c);
            DrawColorBox(new Rect(r.xMax - 1,       r.y,              1, r.height), c);
        }

        private bool MenuButton(Rect r, string label)
        {
            bool isHover = r.Contains(Event.current.mousePosition);
            DrawColorBox(r, isHover
                ? new Color(0.20f, 0.20f, 0.35f, 0.92f)
                : new Color(0.08f, 0.08f, 0.16f, 0.88f));
            DrawOutline(r, isHover
                ? new Color(0.65f, 0.65f, 0.80f, 0.9f)
                : new Color(0.30f, 0.30f, 0.45f, 0.7f));
            GUI.Label(r, label, _labelStyle);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // =====================================================================
        // Style initialisation (runs once on first OnGUI call)
        // =====================================================================

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 11,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = false,
            };
            _labelStyle.normal.textColor = Color.white;

            _titleStyle = new GUIStyle(_labelStyle)
            {
                fontSize  = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _titleStyle.normal.textColor = new Color(1f, 0.80f, 0.20f);

            _smallStyle = new GUIStyle(_labelStyle)
            {
                fontSize  = 10,
                alignment = TextAnchor.MiddleLeft,
            };
            _smallStyle.normal.textColor = new Color(0.90f, 0.90f, 0.90f);

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleCenter,
            };
            _buttonStyle.normal.textColor   = Color.white;
            _buttonStyle.hover.textColor    = new Color(1f, 1f, 0.7f);
            _buttonStyle.active.textColor   = new Color(1f, 1f, 0.4f);

            _inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 11,
            };
            _inputStyle.normal.textColor = Color.white;
        }
    }
}
