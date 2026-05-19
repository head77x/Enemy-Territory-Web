// Ports: qcommon/common.c — Com_* functions: event loop, frame dispatch,
// configuration I/O, error handling, memory info, string utilities.
// Memory management (zone/hunk) is replaced by C# GC.
// The event loop is replaced by Unity's MonoBehaviour Update() chain.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ET.Core
{
    // =========================================================================
    // System event types (sysEvent_t)
    // =========================================================================
    public enum SysEventType
    {
        None = 0,
        Key,
        Char,
        Mouse,
        JoystickAxis,
        Console,
        Packet,
    }

    public struct SysEvent
    {
        public SysEventType Type;
        public int          Time;
        public int          Value;
        public int          Value2;
        public byte[]       Data;
    }

    // =========================================================================
    // CommonSystem — qcommon/common.c
    // =========================================================================
    public static class CommonSystem
    {
        // Error types
        public enum ErrorType { Fatal, Drop, Disconnect, NeedCD }

        public static bool Initialized     { get; private set; }
        public static bool ServerRunning   { get; set; }
        public static int  FrameTime       { get; private set; }   // ms this frame
        public static int  RealTime        { get; private set; }   // ms since start

        // Delegates injected by higher-level assemblies to avoid circular dependencies.
        // ET.Game wires CvarSetDelegate / CvarGetIntDelegate / CvarWriteDelegate.
        // ET.Client wires ConsolePrintDelegate / KeyEventDelegate / WriteBindingsDelegate.
        public static Action<string>        ConsolePrintDelegate;
        public static Action<int,bool,int>  KeyEventDelegate;
        public static Func<string>          WriteBindingsDelegate;
        public static Action<string,string> CvarSetDelegate;
        public static Func<string,int>      CvarGetIntDelegate;
        public static Func<string>          CvarWriteDelegate;

        private static readonly Queue<SysEvent> _eventQueue = new Queue<SysEvent>(256);
        private static int _lastTime;

        // Com_Init
        public static void Init(string commandLine)
        {
            if (Initialized) return;

            // Parse command line cvars before anything else
            ParseCommandLine(commandLine);

            // Register Com_* console commands
            
            CmdSystem.Cmd_AddCommand("quit",          args => Quit());
            CmdSystem.Cmd_AddCommand("writeconfig",   args => WriteConfig_f(args));
            CmdSystem.Cmd_AddCommand("meminfo",       args => Meminfo());

            Initialized = true;
            _lastTime   = (int)(Time.realtimeSinceStartup * 1000);
            Debug.Log("CommonSystem initialized");
        }

        // Com_Frame — called every Unity Update(); drives server + client frames
        public static void Frame()
        {
            int now       = (int)(Time.realtimeSinceStartup * 1000);
            FrameTime     = Mathf.Clamp(now - _lastTime, 0, 500);
            RealTime      = now;
            _lastTime     = now;

            EventLoop();
        }

        // ------------------------------------------------------------------ //
        // Error handling
        // ------------------------------------------------------------------ //

        // Com_Error — in Unity, ERR_FATAL triggers Application.Quit
        public static void Error(ErrorType type, string msg)
        {
            Debug.LogError($"ET Com_Error ({type}): {msg}");
            switch (type)
            {
                case ErrorType.Fatal:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit(1);
#endif
                    break;
                case ErrorType.Drop:
                    // Disconnect and return to main menu
                    CmdSystem.Cmd_ExecuteString("disconnect");
                    break;
                case ErrorType.Disconnect:
                    CmdSystem.Cmd_ExecuteString("disconnect");
                    break;
            }
        }

        // Com_Printf — routes to console (via delegate) + Unity debug log
        public static void Printf(string msg)
        {
            ConsolePrintDelegate?.Invoke(msg);
            Debug.Log(msg.TrimEnd('\n'));
        }

        // Com_DPrintf — developer print (only when developer cvar is set)
        public static void DPrintf(string msg)
        {
            if ((CvarGetIntDelegate?.Invoke("developer") ?? 0) != 0)
                Printf(msg);
        }

        // ------------------------------------------------------------------ //
        // Event loop — Com_EventLoop
        // ------------------------------------------------------------------ //

        public static void PushEvent(SysEvent ev)
        {
            if (_eventQueue.Count >= 256) { Debug.LogWarning("EventQueue overflow"); return; }
            _eventQueue.Enqueue(ev);
        }

        public static void EventLoop()
        {
            while (_eventQueue.Count > 0)
            {
                var ev = _eventQueue.Dequeue();
                switch (ev.Type)
                {
                    case SysEventType.Key:
                        KeyEventDelegate?.Invoke(ev.Value, ev.Value2 != 0, ev.Time);
                        break;
                    case SysEventType.Char:
                        // character input to console / chat field
                        break;
                    case SysEventType.Console:
                        if (ev.Data != null)
                            CmdSystem.Cmd_ExecuteString(
                                Encoding.UTF8.GetString(ev.Data));
                        break;
                    case SysEventType.Packet:
                        // delegate to ClientParse / ServerClient
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ //
        // Command line parsing — Com_ParseCommandLine / Com_StartupVariable
        // ------------------------------------------------------------------ //

        public static void ParseCommandLine(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) return;
            // Handle +set key value pairs and +exec commands
            string[] tokens = commandLine.Split(new char[]{' ', '\t'},
                StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "+set" && i + 2 < tokens.Length)
                {
                    CvarSetDelegate?.Invoke(tokens[i + 1], tokens[i + 2]);
                    i += 2;
                }
                else if (tokens[i].StartsWith("+"))
                {
                    CmdSystem.Cmd_ExecuteString(tokens[i].Substring(1));
                }
            }
        }

        // ------------------------------------------------------------------ //
        // Configuration I/O — Com_WriteConfiguration / Com_WriteConfigToFile
        // ------------------------------------------------------------------ //

        private static void WriteConfig_f(string[] args)
        {
            string filename = args.Length > 1 ? args[1] : "etconfig.cfg";
            WriteConfigToFile(filename);
        }

        public static void WriteConfigToFile(string filename)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// generated by ET Unity port");
            sb.AppendLine(CvarWriteDelegate?.Invoke() ?? "");
            sb.AppendLine(WriteBindingsDelegate?.Invoke() ?? "");
            FileSystem.WriteAllText(filename, sb.ToString());
            Debug.Log($"Config written to {filename}");
        }

        public static void WriteConfiguration() =>
            WriteConfigToFile("etconfig.cfg");

        // ------------------------------------------------------------------ //
        // String utilities (Com_* string functions)
        // ------------------------------------------------------------------ //

        // Com_StringContains — case-sensitive or not substring search
        public static int StringContains(string haystack, string needle, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return -1;
            var cmp = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return haystack.IndexOf(needle, cmp);
        }

        // Com_Filter — wildcard filter (*,?) matching
        public static bool Filter(string filter, string name, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string pattern = Regex.Escape(filter)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".");
            var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.IsMatch(name, "^" + pattern + "$", opts);
        }

        // Com_FilterPath — same but normalises path separators first
        public static bool FilterPath(string filter, string path, bool caseSensitive)
            => Filter(filter, path.Replace('\\', '/'), caseSensitive);

        // Com_HashKey — djb2 variant used internally
        public static int HashKey(string str, int maxLen)
        {
            int hash = 0;
            for (int i = 0; i < str.Length && i < maxLen; i++)
                hash += (i + 119) * char.ToLower(str[i]);
            return hash & 0x7FFFFFFF;
        }

        // Com_Milliseconds — ms since app start
        public static int Milliseconds() => (int)(Time.realtimeSinceStartup * 1000);

        // Com_RealTime — fills a DateTime-like structure
        public static DateTime RealTimeNow() => DateTime.Now;

        // ------------------------------------------------------------------ //
        // Memory info (stub — C# GC manages all memory)
        // ------------------------------------------------------------------ //

        private static void Meminfo()
        {
            long managed = GC.GetTotalMemory(false);
            Debug.Log($"Managed heap: {managed / 1024} KB  " +
                      $"(C# GC; ET zone/hunk allocators not used in Unity port)");
        }

        // ------------------------------------------------------------------ //
        // Game info / profile
        // ------------------------------------------------------------------ //

        // Com_GetGameInfo — reads scripts/gameinfo.dat
        public static void GetGameInfo()
        {
            string text = FileSystem.ReadAllText("scripts/gameinfo.dat");
            if (string.IsNullOrEmpty(text)) return;
            // Parse key/value pairs
            string[] lines = text.Split('\n');
            foreach (string line in lines)
            {
                string[] parts = line.Trim().Split(new[]{' ','\t'}, 2,
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                    CvarSetDelegate?.Invoke(parts[0], parts[1]);
            }
        }

        // Com_Quit_f
        public static void Quit()
        {
            WriteConfiguration();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ //
        // Com_ModifyMsec — clamps frame time to prevent physics explosions
        // ------------------------------------------------------------------ //
        public static int ModifyMsec(int msec)
        {
            int clampedMsec = msec;
            int minMsec = CvarGetIntDelegate?.Invoke("sv_minRate") ?? 0;
            if (minMsec == 0) minMsec = 0;

            if (msec > 500)
            {
                Debug.LogWarning($"Com_ModifyMsec: large msec ({msec})");
                clampedMsec = 500;
            }
            else if (msec < 1)
                clampedMsec = 1;

            return clampedMsec;
        }

        // Com_Shutdown
        public static void Shutdown()
        {
            WriteConfiguration();
            Initialized = false;
        }
    }
}
