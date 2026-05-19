// Ported from: src/ui/ui_main.c, src/ui/ui_shared.c, src/ui/ui_atoms.c,
//              src/ui/ui_players.c, src/ui/ui_gameinfo.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    // =========================================================================
    // Menu types and data structures
    // =========================================================================

    public enum UIMenuType
    {
        Main         = 0,
        Ingame       = 1,
        Team         = 2,
        Class        = 3,
        Weapon       = 4,
        Options      = 5,
        Controls     = 6,
        Server       = 7,
        CreateServer = 8,
        Mods         = 9,
        Demos        = 10,
        Credits      = 11,
        Limbo        = 12,
        None         = 13,
    }

    public class MenuItem
    {
        public string Name;
        public string Text;
        public string Action;       // console command on select
        public string CvarName;     // linked cvar (options menus)
        public string CvarValue;
        public Rect   Rect;         // layout in 640x480 reference space
        public bool   Enabled;
        public bool   Visible;
        public Color  FocusColor;
        public Color  TextColor;
        public int    Type;         // 0=button 1=checkbox 2=slider 3=text 4=list
    }

    public class UIMenu
    {
        public UIMenuType     Type;
        public string         Title;
        public List<MenuItem> Items      = new List<MenuItem>();
        public int            FocusedItem;
        public bool           Visible;
        public float          FadeAlpha;
        public bool           FullScreen;
    }

    // =========================================================================
    // UISystem — menu stack and input routing (ui_main.c)
    // =========================================================================

    public static class UISystem
    {
        private static Stack<UIMenu> _menuStack   = new Stack<UIMenu>();
        private static UIMenu        _currentMenu;
        private static bool          _uiActive      = false;
        private static bool          _fullscreenUI  = false;

        // Key codes matching ET's K_* constants used in ui_main.c:UI_KeyEvent
        private const int K_ESCAPE    = 27;
        private const int K_ENTER     = 13;
        private const int K_UPARROW   = 128;
        private const int K_DOWNARROW = 129;

        public static event Action<UIMenu>     OnMenuDraw;
        public static event Action<UIMenuType> OnMenuChanged;
        public static event Action             OnMenuClosed;
        public static event Action<MenuItem>   OnItemFocused;
        public static event Action<MenuItem>   OnItemActivated;

        public static bool UI_IsFullscreen() => _fullscreenUI;
        public static bool UI_IsActive()     => _uiActive;

        public static void UI_Init()
        {
            _menuStack.Clear();
            _currentMenu  = null;
            _uiActive     = false;
            _fullscreenUI = false;
            UIGameInfo.UI_LoadGameInfo();
        }

        public static void UI_Shutdown()
        {
            UI_ForceMenuOff();
            _menuStack.Clear();
        }

        public static void UI_DrawMenu()
        {
            if (!_uiActive || _currentMenu == null)
                return;

            _currentMenu.FadeAlpha = UIShared.UI_Fade(
                ref _currentMenu.FadeAlpha, 1f, Time.deltaTime * 4f);

            OnMenuDraw?.Invoke(_currentMenu);
        }

        public static void RaiseMenuDraw(UIMenu menu) => OnMenuDraw?.Invoke(menu);

        public static void UI_KeyEvent(int key, bool down)
        {
            if (!down || _currentMenu == null)
                return;

            var items = _currentMenu.Items;

            if (key == K_ESCAPE)
            {
                UI_PopMenu();
                return;
            }

            if (key == K_ENTER)
            {
                if (_currentMenu.FocusedItem >= 0 && _currentMenu.FocusedItem < items.Count)
                    UI_ExecuteMenuItem(items[_currentMenu.FocusedItem]);
                return;
            }

            if (key == K_UPARROW)
            {
                MoveFocus(-1);
                return;
            }

            if (key == K_DOWNARROW)
            {
                MoveFocus(1);
            }
        }

        public static void UI_MouseEvent(int dx, int dy)
        {
            // Mouse movement drives focus by hit-testing item rects.
            // Actual screen coords are scaled via UI_AdjustFrom640 by callers.
            // We store the raw reference-space position and let subscribers react.
        }

        public static void UI_SetActiveMenu(UIMenuType menuType)
        {
            if (menuType == UIMenuType.None)
            {
                UI_ForceMenuOff();
                return;
            }

            _uiActive     = true;
            _fullscreenUI = menuType == UIMenuType.Main || menuType == UIMenuType.Credits;

            var menu = BuildMenu(menuType);
            _menuStack.Push(menu);
            _currentMenu = menu;

            OnMenuChanged?.Invoke(menuType);
        }

        public static void UI_PopMenu()
        {
            if (_menuStack.Count == 0)
                return;

            _menuStack.Pop();

            if (_menuStack.Count == 0)
            {
                UI_ForceMenuOff();
            }
            else
            {
                _currentMenu  = _menuStack.Peek();
                _fullscreenUI = _currentMenu.FullScreen;
                OnMenuChanged?.Invoke(_currentMenu.Type);
            }
        }

        public static void UI_ForceMenuOff()
        {
            _menuStack.Clear();
            _currentMenu  = null;
            _uiActive     = false;
            _fullscreenUI = false;
            OnMenuClosed?.Invoke();
        }

        public static void UI_ExecuteMenuItem(MenuItem item)
        {
            if (item == null || !item.Enabled)
                return;

            OnItemActivated?.Invoke(item);
            UIShared.UI_ItemHandleAction(item);
        }

        public static void UI_RefreshMenuItems()
        {
            if (_currentMenu == null)
                return;

            foreach (var item in _currentMenu.Items)
            {
                if (!string.IsNullOrEmpty(item.CvarName))
                    item.CvarValue = UIShared.UI_CvarStringItem(item);
            }
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static void MoveFocus(int dir)
        {
            if (_currentMenu == null || _currentMenu.Items.Count == 0)
                return;

            int count = _currentMenu.Items.Count;
            int next  = _currentMenu.FocusedItem;

            for (int i = 0; i < count; i++)
            {
                next = (next + dir + count) % count;
                var item = _currentMenu.Items[next];
                if (item.Enabled && item.Visible)
                {
                    _currentMenu.FocusedItem = next;
                    OnItemFocused?.Invoke(item);
                    return;
                }
            }
        }

        // Builds a minimal default menu for the given type.
        // Real menus are loaded via UIShared.UI_ParseMenuScript from .menu files;
        // this provides a safe fallback when no script file is found.
        private static UIMenu BuildMenu(UIMenuType type)
        {
            var menu = new UIMenu
            {
                Type       = type,
                Title      = type.ToString(),
                Visible    = true,
                FadeAlpha  = 0f,
                FullScreen = type == UIMenuType.Main || type == UIMenuType.Credits,
            };

            // Try to load the matching script from the VFS.
            string scriptPath = $"ui/{type.ToString().ToLower()}.menu";
            string text = FileSystem.FS_ReadFileText(scriptPath);
            if (!string.IsNullOrEmpty(text))
                UIShared.UI_ParseMenuScript(text, type, menu);

            return menu;
        }
    }

    // =========================================================================
    // UIShared — widget parsing and cvar binding (ui_shared.c)
    // =========================================================================

    public static class UIShared
    {
        // Reference resolution used throughout ET's UI renderer.
        private const float RefWidth  = 640f;
        private const float RefHeight = 480f;

        // Parses a .menu script and populates an existing UIMenu.
        // Overload for building from scratch returns a new UIMenu.
        public static UIMenu UI_ParseMenuScript(string text, UIMenuType type)
        {
            var menu = new UIMenu { Type = type };
            UI_ParseMenuScript(text, type, menu);
            return menu;
        }

        public static void UI_ParseMenuScript(string text, UIMenuType type, UIMenu menu)
        {
            // Tokenise the whole file the same way CmdSystem does: split on
            // whitespace, treat quoted strings as single tokens.
            string[] tokens = CmdSystem.Tokenize(text);
            int pos = 0;

            while (pos < tokens.Length)
            {
                string tok = tokens[pos++];

                if (string.Equals(tok, "menuDef", StringComparison.OrdinalIgnoreCase))
                {
                    ParseMenuDefBlock(tokens, ref pos, menu);
                }
            }
        }

        private static void ParseMenuDefBlock(string[] tokens, ref int pos, UIMenu menu)
        {
            if (pos >= tokens.Length || tokens[pos] != "{")
                return;
            pos++; // consume '{'

            int depth = 1;
            while (pos < tokens.Length && depth > 0)
            {
                string tok = tokens[pos];

                if (tok == "{") { depth++; pos++; continue; }
                if (tok == "}") { depth--; pos++; continue; }

                if (string.Equals(tok, "name", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    menu.Title = tokens[++pos];
                    pos++;
                    continue;
                }

                if (string.Equals(tok, "fullScreen", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    menu.FullScreen = tokens[++pos] == "1";
                    pos++;
                    continue;
                }

                if (string.Equals(tok, "itemDef", StringComparison.OrdinalIgnoreCase))
                {
                    pos++;
                    var item = UI_ParseMenuItem(tokens, ref pos);
                    if (item != null)
                        menu.Items.Add(item);
                    continue;
                }

                pos++;
            }
        }

        private static MenuItem UI_ParseMenuItem(string[] tokens, ref int pos)
        {
            if (pos >= tokens.Length || tokens[pos] != "{")
                return null;
            pos++; // consume '{'

            var item = new MenuItem
            {
                Enabled    = true,
                Visible    = true,
                FocusColor = Color.yellow,
                TextColor  = Color.white,
            };

            int depth = 1;
            while (pos < tokens.Length && depth > 0)
            {
                string tok = tokens[pos];

                if (tok == "{") { depth++; pos++; continue; }
                if (tok == "}") { depth--; pos++; continue; }

                if (string.Equals(tok, "name", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    item.Name = tokens[++pos]; pos++; continue;
                }

                if (string.Equals(tok, "text", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    item.Text = tokens[++pos]; pos++; continue;
                }

                if (string.Equals(tok, "action", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    item.Action = tokens[++pos]; pos++; continue;
                }

                if (string.Equals(tok, "cvar", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    item.CvarName = tokens[++pos]; pos++; continue;
                }

                if (string.Equals(tok, "cvarFloat", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    item.CvarName = tokens[++pos];
                    item.Type     = 2; // slider
                    pos++; continue;
                }

                if (string.Equals(tok, "rect", StringComparison.OrdinalIgnoreCase) && pos + 4 < tokens.Length)
                {
                    float.TryParse(tokens[++pos], out float rx);
                    float.TryParse(tokens[++pos], out float ry);
                    float.TryParse(tokens[++pos], out float rw);
                    float.TryParse(tokens[++pos], out float rh);
                    item.Rect = new Rect(rx, ry, rw, rh);
                    pos++; continue;
                }

                if (string.Equals(tok, "type", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    int.TryParse(tokens[++pos], out item.Type); pos++; continue;
                }

                if (string.Equals(tok, "visible", StringComparison.OrdinalIgnoreCase) && pos + 1 < tokens.Length)
                {
                    item.Visible = tokens[++pos] != "0"; pos++; continue;
                }

                pos++;
            }

            return item;
        }

        // Action string format mirrors ui_shared.c:Item_Action:
        //   "close"                  — pop current menu
        //   "open <menuname>"        — push named menu (mapped to UIMenuType)
        //   "exec <command>"         — run console command
        //   raw string               — treated as console command
        public static void UI_ItemHandleAction(MenuItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Action))
                return;

            string action = item.Action.Trim();

            if (string.Equals(action, "close", StringComparison.OrdinalIgnoreCase))
            {
                UISystem.UI_PopMenu();
                return;
            }

            if (action.StartsWith("open ", StringComparison.OrdinalIgnoreCase))
            {
                string menuName = action.Substring(5).Trim();
                if (Enum.TryParse(menuName, true, out UIMenuType t))
                    UISystem.UI_SetActiveMenu(t);
                return;
            }

            if (action.StartsWith("exec ", StringComparison.OrdinalIgnoreCase))
            {
                CmdSystem.Cmd_ExecuteString(action.Substring(5).Trim());
                return;
            }

            CmdSystem.Cmd_ExecuteString(action);
        }

        public static float UI_CvarFloatItem(MenuItem item)
        {
            if (string.IsNullOrEmpty(item.CvarName))
                return 0f;
            return CvarSystem.GetFloat(item.CvarName);
        }

        public static void UI_SetCvarFloatItem(MenuItem item, float value)
        {
            if (string.IsNullOrEmpty(item.CvarName))
                return;
            CvarSystem.SetValue(item.CvarName, value);
            item.CvarValue = value.ToString("G");
        }

        public static string UI_CvarStringItem(MenuItem item)
        {
            if (string.IsNullOrEmpty(item.CvarName))
                return string.Empty;
            return CvarSystem.GetString(item.CvarName);
        }

        public static void UI_SetCvarStringItem(MenuItem item, string value)
        {
            if (string.IsNullOrEmpty(item.CvarName))
                return;
            CvarSystem.Set(item.CvarName, value);
            item.CvarValue = value;
        }

        // Lerps alpha toward target; mirrors UI_FadeColor in ui_shared.c.
        public static float UI_Fade(ref float current, float target, float rate)
        {
            if (Mathf.Abs(current - target) < 0.01f)
            {
                current = target;
                return current;
            }
            current = Mathf.MoveTowards(current, target, rate);
            return current;
        }

        // Fires a draw event rather than calling a renderer directly.
        public static void UI_DrawHandlePic(float x, float y, float w, float h, string texturePath)
        {
            UI_AdjustFrom640(ref x, ref y, ref w, ref h);
            UISystem.RaiseMenuDraw(null); // subscribers handle actual rendering
        }

        public static void UI_AdjustFrom640(ref float x, ref float y, ref float w, ref float h)
        {
            float sx = Screen.width  / RefWidth;
            float sy = Screen.height / RefHeight;
            x *= sx;
            y *= sy;
            w *= sx;
            h *= sy;
        }
    }

    // =========================================================================
    // UIPlayers — scoreboard / class selection player list (ui_players.c)
    // =========================================================================

    public class UIPlayerEntry
    {
        public int    ClientNum;
        public string Name;
        public string Model;
        public int    Team;
        public int    Class;
        public int    Ping;
        public int    Score;
        public bool   IsBot;
    }

    public static class UIPlayers
    {
        private static List<UIPlayerEntry> _players = new List<UIPlayerEntry>();

        public static event Action<List<UIPlayerEntry>> OnPlayersUpdated;

        public static void UI_PlayerInfo_SetInfo(int clientNum, string name, string model,
            int team, int cls, int ping, int score, bool isBot)
        {
            var entry = _players.Find(p => p.ClientNum == clientNum);
            if (entry == null)
            {
                entry = new UIPlayerEntry { ClientNum = clientNum };
                _players.Add(entry);
            }

            entry.Name  = name;
            entry.Model = model;
            entry.Team  = team;
            entry.Class = cls;
            entry.Ping  = ping;
            entry.Score = score;
            entry.IsBot = isBot;

            OnPlayersUpdated?.Invoke(_players);
        }

        public static UIPlayerEntry UI_PlayerInfo_GetInfo(int clientNum)
            => _players.Find(p => p.ClientNum == clientNum);

        public static List<UIPlayerEntry> UI_GetPlayers(int team = -1)
        {
            if (team < 0)
                return new List<UIPlayerEntry>(_players);

            var result = new List<UIPlayerEntry>();
            foreach (var p in _players)
                if (p.Team == team) result.Add(p);
            return result;
        }

        // Parses the compact "cs N <key\\value...>" server-info string for
        // player slots, mirroring UI_GetClientState in ui_atoms.c.
        public static void UI_ParsePlayers(string serverInfo)
        {
            _players.Clear();

            if (string.IsNullOrEmpty(serverInfo))
                return;

            // serverInfo format per slot:
            // "n\\<name>\\t\\<team>\\c\\<class>\\p\\<ping>\\s\\<score>\\b\\<isbot>"
            var sections = serverInfo.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < sections.Length; i++)
            {
                var kv = ParseKeyValues(sections[i]);

                string name = GetKV(kv, "n");
                if (string.IsNullOrEmpty(name))
                    continue;

                int.TryParse(GetKV(kv, "t"), out int team);
                int.TryParse(GetKV(kv, "c"), out int cls);
                int.TryParse(GetKV(kv, "p"), out int ping);
                int.TryParse(GetKV(kv, "s"), out int score);
                int.TryParse(GetKV(kv, "b"), out int bot);

                _players.Add(new UIPlayerEntry
                {
                    ClientNum = i,
                    Name      = name,
                    Model     = GetKV(kv, "model"),
                    Team      = team,
                    Class     = cls,
                    Ping      = ping,
                    Score     = score,
                    IsBot     = bot != 0,
                });
            }

            OnPlayersUpdated?.Invoke(_players);
        }

        // ------------------------------------------------------------------
        private static Dictionary<string, string> ParseKeyValues(string raw)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parts = raw.Split('\\');
            for (int i = 0; i + 1 < parts.Length; i += 2)
                dict[parts[i]] = parts[i + 1];
            return dict;
        }

        private static string GetKV(Dictionary<string, string> kv, string key)
        {
            kv.TryGetValue(key, out string v);
            return v ?? string.Empty;
        }
    }

    // =========================================================================
    // UIGameInfo — arena/campaign info + server browser (ui_gameinfo.c)
    // =========================================================================

    public static class UIGameInfo
    {
        public class ArenaInfo
        {
            public string Map;
            public string LongName;
            public string Type;         // "objective", "stopwatch", etc.
            public string BriefingText;
        }

        private static List<ArenaInfo>          _arenas  = new List<ArenaInfo>();
        private static List<ServerBrowserEntry>  _servers = new List<ServerBrowserEntry>();

        public static event Action<List<ServerBrowserEntry>> OnServerListUpdated;

        public static void UI_LoadGameInfo()
        {
            _arenas.Clear();

            string text = FileSystem.FS_ReadFileText("scripts/arenas.txt");
            if (string.IsNullOrEmpty(text))
                return;

            // Split on top-level '{' '}' blocks; each block is one arena entry.
            int pos = 0;
            while (pos < text.Length)
            {
                int start = text.IndexOf('{', pos);
                if (start < 0) break;
                int end = text.IndexOf('}', start);
                if (end < 0) break;

                string block = text.Substring(start + 1, end - start - 1);
                UI_ParseArenaInfo(block);
                pos = end + 1;
            }
        }

        // Parses one arena block, e.g.:
        //   map "beach"  longname "Gold Rush"  type "objective"  briefing "..."
        public static void UI_ParseArenaInfo(string arenaText)
        {
            string[] tokens = CmdSystem.Tokenize(arenaText);
            var arena = new ArenaInfo();

            for (int i = 0; i + 1 < tokens.Length; i += 2)
            {
                string key = tokens[i];
                string val = tokens[i + 1];

                if (string.Equals(key, "map",      StringComparison.OrdinalIgnoreCase)) arena.Map          = val;
                else if (string.Equals(key, "longname",  StringComparison.OrdinalIgnoreCase)) arena.LongName     = val;
                else if (string.Equals(key, "type",      StringComparison.OrdinalIgnoreCase)) arena.Type         = val;
                else if (string.Equals(key, "briefing",  StringComparison.OrdinalIgnoreCase)) arena.BriefingText = val;
            }

            if (!string.IsNullOrEmpty(arena.Map))
                _arenas.Add(arena);
        }

        public static ArenaInfo UI_FindArena(string mapName)
            => _arenas.Find(a => string.Equals(a.Map, mapName, StringComparison.OrdinalIgnoreCase));

        // Server browser — in the original code this issued LAN/internet queries;
        // here we populate from the network layer via direct injection so the UI
        // stays decoupled from the socket implementation.
        public static void UI_BuildServerList()
        {
            _servers.Clear();
            OnServerListUpdated?.Invoke(_servers);
        }

        public static void UI_AddServer(ServerBrowserEntry entry)
        {
            if (entry == null) return;
            _servers.Add(entry);
            OnServerListUpdated?.Invoke(_servers);
        }

        public static int UI_GetServerCount() => _servers.Count;

        public static ServerBrowserEntry UI_GetServerInfo(int index)
        {
            if (index < 0 || index >= _servers.Count)
                return null;
            return _servers[index];
        }
    }

    // =========================================================================
    // ServerBrowserEntry — top-level so other files can reference it directly
    // =========================================================================

    public class ServerBrowserEntry
    {
        public string HostName;
        public string MapName;
        public string GameType;
        public int    Players;
        public int    MaxPlayers;
        public int    Ping;
        public bool   PasswordRequired;
        public string Address;
    }
}
