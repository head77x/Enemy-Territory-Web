// Ports: cl_keys.c + cl_console.c + cl_scrn.c + cl_ui.c
// Key binding system, developer console, screen utilities, UI bridge.
// In Unity, actual rendering delegates to Unity UI / IMGUI via events.

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ET.Core;
using ET.Game;

namespace ET.Client
{
    // =========================================================================
    // KeySystem — cl_keys.c
    // Key binding: string↔keynum mapping, bind/unbind commands, console input.
    // =========================================================================

    public enum ETKey
    {
        None = 0,
        Tab = 9, Enter = 13, Escape = 27, Space = 32,
        Backspace = 127,
        // Extended keys (256+)
        UpArrow = 256, DownArrow, LeftArrow, RightArrow,
        Alt, Ctrl, Shift, Ins, Del, PgDn, PgUp, Home, End,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        CapsLock, Pause,
        Mouse1, Mouse2, Mouse3, Mouse4, Mouse5,
        MWheelDown, MWheelUp,
        Joy1, Joy2, Joy3, Joy4, Joy5, Joy6, Joy7, Joy8,
        KP_Home, KP_UpArrow, KP_PgUp, KP_LeftArrow, KP_5, KP_RightArrow,
        KP_End, KP_DownArrow, KP_PgDn, KP_Enter, KP_Ins, KP_Del,
        KP_Slash, KP_Minus, KP_Plus, KP_NumLock, KP_Star, KP_Equals,
        Command,
        MAX_KEYS = 320
    }

    public class KeyBinding
    {
        public string Binding = "";
        public bool   Down;
        public int    Repeats;
    }

    public static class KeySystem
    {
        private static readonly KeyBinding[] _keys = new KeyBinding[(int)ETKey.MAX_KEYS];
        private static readonly Dictionary<string, int> _nameToKey =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static KeySystem()
        {
            for (int i = 0; i < (int)ETKey.MAX_KEYS; i++)
                _keys[i] = new KeyBinding();
            BuildNameTable();
            RegisterCommands();
        }

        private static void BuildNameTable()
        {
            var pairs = new (string name, ETKey key)[]
            {
                ("TAB",        ETKey.Tab),       ("ENTER",     ETKey.Enter),
                ("ESCAPE",     ETKey.Escape),    ("SPACE",     ETKey.Space),
                ("BACKSPACE",  ETKey.Backspace), ("UPARROW",   ETKey.UpArrow),
                ("DOWNARROW",  ETKey.DownArrow), ("LEFTARROW", ETKey.LeftArrow),
                ("RIGHTARROW", ETKey.RightArrow),("ALT",       ETKey.Alt),
                ("CTRL",       ETKey.Ctrl),      ("SHIFT",     ETKey.Shift),
                ("INS",        ETKey.Ins),        ("DEL",       ETKey.Del),
                ("PGDN",       ETKey.PgDn),      ("PGUP",      ETKey.PgUp),
                ("HOME",       ETKey.Home),       ("END",       ETKey.End),
                ("CAPSLOCK",   ETKey.CapsLock),  ("PAUSE",     ETKey.Pause),
                ("F1",ETKey.F1),  ("F2",ETKey.F2),  ("F3",ETKey.F3),  ("F4",ETKey.F4),
                ("F5",ETKey.F5),  ("F6",ETKey.F6),  ("F7",ETKey.F7),  ("F8",ETKey.F8),
                ("F9",ETKey.F9),  ("F10",ETKey.F10),("F11",ETKey.F11),("F12",ETKey.F12),
                ("MOUSE1",ETKey.Mouse1),("MOUSE2",ETKey.Mouse2),("MOUSE3",ETKey.Mouse3),
                ("MOUSE4",ETKey.Mouse4),("MOUSE5",ETKey.Mouse5),
                ("MWHEELDOWN",ETKey.MWheelDown),("MWHEELUP",ETKey.MWheelUp),
                ("JOY1",ETKey.Joy1),("JOY2",ETKey.Joy2),("JOY3",ETKey.Joy3),
                ("JOY4",ETKey.Joy4),("JOY5",ETKey.Joy5),
                ("KP_HOME",ETKey.KP_Home),("KP_UPARROW",ETKey.KP_UpArrow),
                ("KP_PGUP",ETKey.KP_PgUp),("KP_LEFTARROW",ETKey.KP_LeftArrow),
                ("KP_5",ETKey.KP_5),  ("KP_RIGHTARROW",ETKey.KP_RightArrow),
                ("KP_END",ETKey.KP_End),("KP_DOWNARROW",ETKey.KP_DownArrow),
                ("KP_PGDN",ETKey.KP_PgDn),("KP_ENTER",ETKey.KP_Enter),
                ("KP_INS",ETKey.KP_Ins),("KP_DEL",ETKey.KP_Del),
                ("KP_SLASH",ETKey.KP_Slash),("KP_MINUS",ETKey.KP_Minus),
                ("KP_PLUS",ETKey.KP_Plus),("KP_NUMLOCK",ETKey.KP_NumLock),
                ("KP_STAR",ETKey.KP_Star),("KP_EQUALS",ETKey.KP_Equals),
                ("COMMAND",ETKey.Command),
            };
            foreach (var (name, key) in pairs)
                _nameToKey[name] = (int)key;
        }

        private static void RegisterCommands()
        {
            
            CmdSystem.Cmd_AddCommand("bind",      args => Cmd_Bind(args));
            CmdSystem.Cmd_AddCommand("unbind",    args => Cmd_Unbind(args));
            CmdSystem.Cmd_AddCommand("unbindall", args => Cmd_UnbindAll(args));
            CmdSystem.Cmd_AddCommand("bindlist",  args => Cmd_BindList(args));
        }

        // Key_StringToKeynum
        public static int StringToKeynum(string str)
        {
            if (string.IsNullOrEmpty(str)) return -1;
            if (str.Length == 1) return str[0];
            if (_nameToKey.TryGetValue(str, out int k)) return k;
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(str.Substring(2), System.Globalization.NumberStyles.HexNumber,
                    null, out int hex)) return hex;
            return -1;
        }

        // Key_KeynumToString
        public static string KeynumToString(int keynum)
        {
            if (keynum == -1) return "<invalid key>";
            if (keynum < 256 && keynum > 32 && keynum != 127)
                return ((char)keynum).ToString().ToLower();
            foreach (var kv in _nameToKey)
                if (kv.Value == keynum) return kv.Key;
            return $"0x{keynum:x2}";
        }

        // Key_SetBinding
        public static void SetBinding(int keynum, string binding)
        {
            if (keynum < 0 || keynum >= (int)ETKey.MAX_KEYS) return;
            _keys[keynum].Binding = binding ?? "";
        }

        // Key_GetBinding
        public static string GetBinding(int keynum)
        {
            if (keynum < 0 || keynum >= (int)ETKey.MAX_KEYS) return "";
            return _keys[keynum].Binding;
        }

        // Key_GetBindingByString — returns up to 2 keys that have the given binding
        public static void GetBindingByString(string binding, out int key1, out int key2)
        {
            key1 = key2 = -1;
            for (int i = 0; i < (int)ETKey.MAX_KEYS; i++)
            {
                if (string.Equals(_keys[i].Binding, binding, StringComparison.OrdinalIgnoreCase))
                {
                    if (key1 < 0) key1 = i;
                    else if (key2 < 0) { key2 = i; return; }
                }
            }
        }

        // Key_GetKey
        public static int GetKey(string binding)
        {
            if (binding == null) return -1;
            for (int i = 0; i < (int)ETKey.MAX_KEYS; i++)
                if (string.Equals(_keys[i].Binding, binding, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        // Key_IsDown
        public static bool IsDown(int keynum) =>
            keynum >= 0 && keynum < (int)ETKey.MAX_KEYS && _keys[keynum].Down;

        // Key_Event — called when a key event arrives
        public static void OnKeyEvent(int keynum, bool down, int time)
        {
            if (keynum < 0 || keynum >= (int)ETKey.MAX_KEYS) return;
            var key = _keys[keynum];
            key.Down = down;
            if (down) key.Repeats++; else key.Repeats = 0;

            if (!string.IsNullOrEmpty(key.Binding) && down)
                CmdSystem.Cmd_ExecuteString(key.Binding);
        }

        // Key_WriteBindings — produces "bind KEY CMD\n" lines for config save
        public static string WriteBindings()
        {
            var sb = new StringBuilder();
            sb.AppendLine("unbindall");
            for (int i = 0; i < (int)ETKey.MAX_KEYS; i++)
            {
                if (!string.IsNullOrEmpty(_keys[i].Binding))
                    sb.AppendLine($"bind {KeynumToString(i)} \"{_keys[i].Binding}\"");
            }
            return sb.ToString();
        }

        // ---- Command handlers ----
        private static void Cmd_Bind(string[] args)
        {
            if (args.Length < 2) { Debug.Log("bind <key> [cmd]"); return; }
            int k = StringToKeynum(args[1]);
            if (k < 0) { Debug.Log($"\"{args[1]}\" isn't a valid key"); return; }
            if (args.Length == 2)
            {
                string b = GetBinding(k);
                Debug.Log(string.IsNullOrEmpty(b) ? $"\"{args[1]}\" is not bound"
                                                   : $"\"{args[1]}\" = \"{b}\"");
                return;
            }
            SetBinding(k, string.Join(" ", args, 2, args.Length - 2));
        }

        private static void Cmd_Unbind(string[] args)
        {
            if (args.Length < 2) { Debug.Log("unbind <key>"); return; }
            int k = StringToKeynum(args[1]);
            if (k < 0) { Debug.Log($"\"{args[1]}\" isn't a valid key"); return; }
            SetBinding(k, "");
        }

        private static void Cmd_UnbindAll(string[] args)
        {
            for (int i = 0; i < (int)ETKey.MAX_KEYS; i++)
                _keys[i].Binding = "";
        }

        private static void Cmd_BindList(string[] args)
        {
            for (int i = 0; i < (int)ETKey.MAX_KEYS; i++)
                if (!string.IsNullOrEmpty(_keys[i].Binding))
                    Debug.Log($"{KeynumToString(i),20} \"{_keys[i].Binding}\"");
        }
    }

    // =========================================================================
    // ClientConsole — cl_console.c
    // Text console: line buffer, history, notification overlay, draw events.
    // =========================================================================

    public class ConsoleState
    {
        public const int CON_TEXTSIZE    = 32768;
        public const int NUM_CON_TIMES  = 4;
        public const int COMMAND_HISTORY = 32;

        public readonly char[] Text    = new char[CON_TEXTSIZE];
        public int   Current;           // line where next message will be printed
        public int   X;                 // offset in current line
        public int   Display;           // bottom of console displayed
        public int   LineWidth;         // pixels
        public int   TotalLines;
        public float DisplayFrac;       // 0.0=hidden, 1.0=full
        public float FinalFrac;
        public readonly int[] Times     = new int[NUM_CON_TIMES];
        public int   AcLength;          // auto-complete cursor

        // History
        public readonly string[] HistoryLines = new string[COMMAND_HISTORY];
        public int NextHistoryLine;
        public int HistoryLine;

        public string ConsoleField = "";
        public string ChatField    = "";
        public bool   ChatTeam;
        public bool   ChatBuddy;
    }

    public static class ClientConsole
    {
        public static ConsoleState Con { get; } = new ConsoleState();
        public static bool Initialized { get; private set; }

        // Draw events — Unity UI subscriber renders the console
        public static event Action<float> OnDrawConsole;         // frac = height 0..1
        public static event Action<string[]> OnDrawNotify;       // recent lines

        // Con_Init
        public static void Init()
        {
            Con.LineWidth   = 78;
            Con.TotalLines  = ConsoleState.CON_TEXTSIZE / Con.LineWidth;
            Con.Display     = Con.Current;
            Initialized     = true;

            
            CmdSystem.Cmd_AddCommand("toggleconsole", args => ToggleConsole());
            CmdSystem.Cmd_AddCommand("clear",         args => Clear());
            CmdSystem.Cmd_AddCommand("condump",       args => Dump(args));
        }

        // Con_ToggleConsole_f
        public static void ToggleConsole()
        {
            Con.DisplayFrac = Con.DisplayFrac > 0 ? 0f : 1f;
            Con.FinalFrac   = Con.DisplayFrac;
        }

        // Con_Clear_f
        public static void Clear()
        {
            Array.Clear(Con.Text, 0, Con.Text.Length);
            Con.Current = 0;
            Con.X       = 0;
            Con.Display = 0;
        }

        // Con_Dump_f
        public static void Dump(string[] args)
        {
            string filename = args.Length > 1 ? args[1] : "condump.txt";
            var sb = new StringBuilder();
            for (int i = 0; i < Con.TotalLines; i++)
            {
                int row = (Con.Current - Con.TotalLines + i + Con.TotalLines) % Con.TotalLines;
                int start = row * Con.LineWidth;
                for (int j = 0; j < Con.LineWidth; j++)
                    sb.Append(Con.Text[start + j] == 0 ? ' ' : Con.Text[start + j]);
                sb.AppendLine();
            }
            FileSystem.WriteAllText(filename, sb.ToString());
            Debug.Log($"Dumped console text to {filename}");
        }

        // CL_ConsolePrint — adds text to the console ring buffer
        public static void Print(string txt)
        {
            if (!Initialized) { Debug.Log(txt); return; }
            foreach (char c in txt)
            {
                if (c == '\n')
                {
                    Con.Current++;
                    Con.X = 0;
                    int row = Con.Current % Con.TotalLines;
                    int start = row * Con.LineWidth;
                    for (int i = 0; i < Con.LineWidth; i++)
                        Con.Text[start + i] = '\0';
                }
                else
                {
                    int idx = (Con.Current % Con.TotalLines) * Con.LineWidth + Con.X;
                    if (idx < Con.Text.Length)
                        Con.Text[idx] = c;
                    Con.X++;
                    if (Con.X >= Con.LineWidth) { Con.X = 0; Con.Current++; }
                }
            }
            Con.Display = Con.Current;
        }

        // Con_RunConsole — slides console in/out each frame
        public static void Run(float deltaTime)
        {
            float step = deltaTime * 3f;
            if (Con.FinalFrac > Con.DisplayFrac)
                Con.DisplayFrac = Mathf.Min(Con.DisplayFrac + step, Con.FinalFrac);
            else if (Con.FinalFrac < Con.DisplayFrac)
                Con.DisplayFrac = Mathf.Max(Con.DisplayFrac - step, Con.FinalFrac);
        }

        // Con_DrawConsole — fires draw event
        public static void Draw() => OnDrawConsole?.Invoke(Con.DisplayFrac);

        public static void PageUp()   => Con.Display = Math.Max(0, Con.Display - 2);
        public static void PageDown() => Con.Display = Math.Min(Con.Current, Con.Display + 2);
        public static void Top()      => Con.Display = Con.TotalLines;
        public static void Bottom()   => Con.Display = Con.Current;

        // AddHistory
        public static void AddHistory(string cmd)
        {
            Con.HistoryLines[Con.NextHistoryLine % ConsoleState.COMMAND_HISTORY] = cmd;
            Con.NextHistoryLine++;
            Con.HistoryLine = Con.NextHistoryLine;
        }

        public static string GetPrevHistory()
        {
            if (Con.HistoryLine <= 0) return "";
            Con.HistoryLine = Math.Max(0, Con.HistoryLine - 1);
            return Con.HistoryLines[Con.HistoryLine % ConsoleState.COMMAND_HISTORY] ?? "";
        }

        public static string GetNextHistory()
        {
            if (Con.HistoryLine >= Con.NextHistoryLine) return "";
            Con.HistoryLine++;
            return Con.HistoryLine >= Con.NextHistoryLine ? ""
                : Con.HistoryLines[Con.HistoryLine % ConsoleState.COMMAND_HISTORY] ?? "";
        }
    }

    // =========================================================================
    // ClientScreen — cl_scrn.c
    // Screen coordinate utilities and debug graph. Drawing is event-driven.
    // =========================================================================

    public static class ClientScreen
    {
        public static bool Initialized { get; private set; }

        // Draw event — fired once per frame with the current stereo frame type
        public static event Action<int> OnDrawScreen;    // stereoFrame: 0=center,1=left,2=right
        public static event Action<float, float, float, float, string> OnDrawNamedPic;
        public static event Action<float, float, float, float, UnityEngine.Color> OnFillRect;

        private const float BASE_WIDTH  = 640f;
        private const float BASE_HEIGHT = 480f;

        // SCR_Init
        public static void Init()
        {
            Initialized = true;
            
            CmdSystem.Cmd_AddCommand("screenshotJPEG", args => TakeScreenshot(true));
            CmdSystem.Cmd_AddCommand("screenshot",     args => TakeScreenshot(false));
        }

        // SCR_AdjustFrom640 — maps 640×480 virtual coords to screen
        public static void AdjustFrom640(ref float x, ref float y, ref float w, ref float h)
        {
            x = x / BASE_WIDTH  * Screen.width;
            y = y / BASE_HEIGHT * Screen.height;
            w = w / BASE_WIDTH  * Screen.width;
            h = h / BASE_HEIGHT * Screen.height;
        }

        // SCR_DrawScreenField
        public static void DrawScreenField(int stereoFrame) =>
            OnDrawScreen?.Invoke(stereoFrame);

        // SCR_FillRect
        public static void FillRect(float x, float y, float w, float h, UnityEngine.Color c)
        {
            AdjustFrom640(ref x, ref y, ref w, ref h);
            OnFillRect?.Invoke(x, y, w, h, c);
        }

        // SCR_DrawNamedPic
        public static void DrawNamedPic(float x, float y, float w, float h, string pic)
        {
            AdjustFrom640(ref x, ref y, ref w, ref h);
            OnDrawNamedPic?.Invoke(x, y, w, h, pic);
        }

        // SCR_GetBigStringWidth — approximate (each char ≈ 16 virtual pixels)
        public static int GetBigStringWidth(string s)
        {
            if (s == null) return 0;
            int len = 0;
            for (int i = 0; i < s.Length; i++)
                if (s[i] != '^' && (i == 0 || s[i-1] != '^')) len++;
            return len * 16;
        }

        private static void TakeScreenshot(bool jpeg)
        {
            string ext  = jpeg ? "jpg" : "png";
            string path = $"screenshots/et_{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"Screenshot: {path}");
        }

        // DebugGraph — samples ring buffer for performance graph
        private const int MAX_GRAPH_SAMPLES = 1024;
        private static readonly float[] _graphValues = new float[MAX_GRAPH_SAMPLES];
        private static int _graphCurrent;
        public static void DebugGraph(float value) =>
            _graphValues[_graphCurrent++ % MAX_GRAPH_SAMPLES] = value;
        public static float[] GetGraphValues() => _graphValues;
    }

    // =========================================================================
    // ClientUI — cl_ui.c
    // Client-side UI VM bridge and LAN server browser.
    // In ET, the UI was a separate QVM; in Unity we call UISystem directly.
    // =========================================================================

    public class ServerEntry
    {
        public string Name    = "";
        public string Address = "";
        public int    Ping    = 9999;
        public bool   Visible = true;
        public string Info    = "";
    }

    public static class ClientUI
    {
        // LAN server sources
        public const int AS_LOCAL    = 0;
        public const int AS_GLOBAL   = 1;
        public const int AS_FAVORITES = 2;
        public const int MAX_PINGREQUESTS = 32;

        private static readonly List<ServerEntry>[] _servers =
        {
            new List<ServerEntry>(),   // AS_LOCAL
            new List<ServerEntry>(),   // AS_GLOBAL
            new List<ServerEntry>(),   // AS_FAVORITES
        };

        private static bool _initialized;

        // CL_InitUI
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            LoadCachedServers();
        }

        // CL_ShutdownUI
        public static void Shutdown() { }

        // UI dispatch — in ET this called VM_Call; here we call UISystem directly
        public static void SetActiveMenu(int menuId) =>
            UISystem.UI_SetActiveMenu((UIMenuType)menuId);

        public static void KeyEvent(int key, bool down) =>
            UISystem.UI_KeyEvent(key, down);

        public static void MouseEvent(int dx, int dy) =>
            UISystem.UI_MouseEvent(dx, dy);

        public static void Refresh(int time) =>
            UISystem.UI_RefreshMenuItems();

        // LAN_LoadCachedServers — load from PlayerPrefs
        public static void LoadCachedServers()
        {
            string data = PlayerPrefs.GetString("lan_servers_global", "");
            _servers[AS_GLOBAL].Clear();
            if (!string.IsNullOrEmpty(data))
            {
                foreach (string line in data.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length >= 2)
                        _servers[AS_GLOBAL].Add(new ServerEntry { Name = parts[0], Address = parts[1] });
                }
            }
        }

        // LAN_SaveServersToCache
        public static void SaveServersToCache()
        {
            var sb = new StringBuilder();
            foreach (var s in _servers[AS_GLOBAL])
                sb.AppendLine($"{s.Name}|{s.Address}");
            PlayerPrefs.SetString("lan_servers_global", sb.ToString());
            PlayerPrefs.Save();
        }

        // LAN_GetServerCount
        public static int GetServerCount(int source)
        {
            if (source < 0 || source >= _servers.Length) return 0;
            return _servers[source].Count;
        }

        // LAN_AddServer
        public static int AddServer(int source, string name, string address)
        {
            if (source < 0 || source >= _servers.Length) return -1;
            var list = _servers[source];
            foreach (var s in list)
                if (s.Address == address) return 0;   // already present
            list.Add(new ServerEntry { Name = name, Address = address });
            return list.Count - 1;
        }

        // LAN_RemoveServer
        public static void RemoveServer(int source, string address)
        {
            if (source < 0 || source >= _servers.Length) return;
            _servers[source].RemoveAll(s => s.Address == address);
        }

        // LAN_GetServerAddressString
        public static string GetServerAddress(int source, int n)
        {
            if (source < 0 || source >= _servers.Length) return "";
            var list = _servers[source];
            if (n < 0 || n >= list.Count) return "";
            return list[n].Address;
        }

        // LAN_GetServerPing
        public static int GetServerPing(int source, int n)
        {
            if (source < 0 || source >= _servers.Length) return 9999;
            var list = _servers[source];
            if (n < 0 || n >= list.Count) return 9999;
            return list[n].Ping;
        }

        // LAN_MarkServerVisible
        public static void MarkServerVisible(int source, int n, bool visible)
        {
            if (source < 0 || source >= _servers.Length) return;
            var list = _servers[source];
            if (n >= 0 && n < list.Count) list[n].Visible = visible;
        }

        // LAN_ServerIsVisible
        public static bool ServerIsVisible(int source, int n)
        {
            if (source < 0 || source >= _servers.Length) return false;
            var list = _servers[source];
            if (n < 0 || n >= list.Count) return false;
            return list[n].Visible;
        }

        // LAN_ResetPings
        public static void ResetPings(int source)
        {
            if (source < 0 || source >= _servers.Length) return;
            foreach (var s in _servers[source]) s.Ping = 9999;
        }

        // GetClientState — fills UI with current connection info
        public static void GetClientState(out string servername, out int connectPacketCount,
                                           out int clientNum, out int lastPacketSentTime,
                                           out int lastPacketTime)
        {
            var clc = ClientMain.Clc;
            servername         = clc?.ServerAddress?.ToString() ?? "";
            connectPacketCount = 0;   // not tracked in this port
            clientNum          = clc?.ClientNum ?? 0;
            lastPacketSentTime = 0;   // not tracked in this port
            lastPacketTime     = 0;   // not tracked in this port
        }
    }
}
