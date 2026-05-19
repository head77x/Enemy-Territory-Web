// Ported from: src/qcommon/cmd.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ET.Core
{
    /// <summary>
    /// Console command registration and dispatch (cmd.c port).
    /// Commands are registered with a handler delegate and executed by name.
    /// </summary>
    public static class CmdSystem
    {
        public delegate void CmdFunc(string[] args);

        private static readonly Dictionary<string, CmdFunc> _cmds =
            new Dictionary<string, CmdFunc>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<string> _cmdBuffer = new List<string>();

        // Cmd_AddCommand — register a command
        public static void Cmd_AddCommand(string name, CmdFunc func)
        {
            if (string.IsNullOrEmpty(name)) return;
            _cmds[name] = func;
        }

        // Cmd_RemoveCommand
        public static void Cmd_RemoveCommand(string name)
        {
            _cmds.Remove(name ?? "");
        }

        // Cmd_Exists
        public static bool Cmd_Exists(string name) =>
            !string.IsNullOrEmpty(name) && _cmds.ContainsKey(name);

        // Cmd_ExecuteString — parse and dispatch a command string immediately
        public static void Cmd_ExecuteString(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var args = Tokenize(text);
            if (args.Length == 0) return;

            if (_cmds.TryGetValue(args[0], out var func))
            {
                func(args);
            }
            else
            {
                // Check CvarSystem before giving up
                if (args.Length >= 2 && ET.Game.CvarSystem.Find(args[0]) != null)
                {
                    ET.Game.CvarSystem.Set(args[0], args[1]);
                    return;
                }
                Debug.LogWarning($"[CmdSystem] Unknown command: {args[0]}");
            }
        }

        // Cbuf_AddText — queue a command for deferred execution
        public static void Cbuf_AddText(string text)
        {
            if (!string.IsNullOrEmpty(text))
                _cmdBuffer.Add(text);
        }

        // Cbuf_Execute — flush the deferred command buffer
        public static void Cbuf_Execute()
        {
            // Copy buffer to avoid re-entrancy issues
            var pending = _cmdBuffer.ToArray();
            _cmdBuffer.Clear();
            foreach (var cmd in pending)
                Cmd_ExecuteString(cmd);
        }

        // Cmd_Argc / Cmd_Argv helpers (for code that calls these directly)
        private static string[] _lastArgs = Array.Empty<string>();
        public static int    Cmd_Argc()            => _lastArgs.Length;
        public static string Cmd_Argv(int n)        => n < _lastArgs.Length ? _lastArgs[n] : "";
        public static string Cmd_Args()             => string.Join(" ", _lastArgs, 1, Math.Max(0, _lastArgs.Length - 1));

        // Cmd_CompleteCommand — return matching command names for tab-complete
        public static string[] Cmd_CompleteCommand(string partial)
        {
            if (string.IsNullOrEmpty(partial)) return Array.Empty<string>();
            var results = new List<string>();
            foreach (var key in _cmds.Keys)
                if (key.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(key);
            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results.ToArray();
        }

        // Simple tokenizer — splits on whitespace, respects "quoted strings"
        public static string[] Tokenize(string text)
        {
            var tokens = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                // skip whitespace and semicolons
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ';'))
                    i++;
                if (i >= text.Length) break;

                // line comment
                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
                    break;

                if (text[i] == '"')
                {
                    // quoted token
                    i++;
                    int start = i;
                    while (i < text.Length && text[i] != '"')
                        i++;
                    tokens.Add(text.Substring(start, i - start));
                    if (i < text.Length) i++; // skip closing "
                }
                else
                {
                    int start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != ';')
                        i++;
                    tokens.Add(text.Substring(start, i - start));
                }
            }
            return tokens.ToArray();
        }
    }
}
