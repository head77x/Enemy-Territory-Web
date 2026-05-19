// Ported from: src/game/g_script.c + src/game/g_script_actions.c
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
    public enum ScriptEventType
    {
        Spawn       = 0,
        Trigger     = 1,
        Pain        = 2,
        Death       = 3,
        Activate    = 4,
        Reached     = 5,
        Blocked     = 6,
        Use         = 7,
        Scripted    = 8,
        MissionFail = 9,
        NumEvents   = 10,
    }

    public class ScriptAction
    {
        public string       ActionString;
        public string[]     Params;
        public int          Delay;
        public ScriptAction Next;
    }

    public class EntityScript
    {
        public string         FileName;
        public ScriptAction[] Events      = new ScriptAction[(int)ScriptEventType.NumEvents];
        public ScriptAction   CurrentAction;
        public bool           Waiting;
        public int            WakeTime;
        public bool           InScript;
        public string         AccumBuffer;
    }

    public static class GameScript
    {
        private static Dictionary<string, EntityScript> _scripts = new Dictionary<string, EntityScript>(StringComparer.OrdinalIgnoreCase);

        // Entity list — populated by ServerGameLogic; GameScript only reads it.
        public static GEntity[]    Entities;
        public static int          NumEntities;
        public static LevelLocals  Level = new LevelLocals();

        // -----------------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------------
        public static event Action<string, string> OnAnnouncement;
        public static event Action<int, int, int>  OnObjectiveStatus;
        public static event Action<bool, int>      OnRespawnTimeChange;
        public static event Action                 OnRoundEnd;

        // -----------------------------------------------------------------------
        // Event name table — must match ScriptEventType order
        // -----------------------------------------------------------------------
        private static readonly string[] _eventNames =
        {
            "spawn", "trigger", "pain", "death", "activate",
            "reached", "blocked", "use", "scripted", "mission_fail",
        };

        // -----------------------------------------------------------------------
        // G_Script_ScriptLoad
        // -----------------------------------------------------------------------
        public static void G_Script_ScriptLoad()
        {
            _scripts.Clear();

            string mapName   = CvarSystem.sv_mapname.StringValue;
            string scriptPath = $"maps/{mapName}.script";
            string text      = FileSystem.FS_ReadFileText(scriptPath);

            if (string.IsNullOrEmpty(text))
            {
                Debug.Log($"[GameScript] No script file found: {scriptPath}");
                return;
            }

            ParseScriptFile(text);
            Debug.Log($"[GameScript] Loaded {_scripts.Count} entity scripts from {scriptPath}");
        }

        // -----------------------------------------------------------------------
        // G_Script_ScriptEvent
        // -----------------------------------------------------------------------
        public static void G_Script_ScriptEvent(GEntity ent, ScriptEventType eventType, string params_)
        {
            if (ent == null) return;

            EntityScript script = null;
            if (ent.TargetName != null)
                _scripts.TryGetValue(ent.TargetName, out script);

            if (script == null) return;

            ent.Script = script;

            int idx = (int)eventType;
            if (idx < 0 || idx >= (int)ScriptEventType.NumEvents) return;

            ScriptAction action = script.Events[idx];
            if (action == null) return;

            script.CurrentAction = action;
            script.Waiting       = false;
            script.WakeTime      = 0;
            script.InScript      = true;

            G_Script_ScriptRun(ent);
        }

        // -----------------------------------------------------------------------
        // G_Script_ScriptRun
        // -----------------------------------------------------------------------
        public static void G_Script_ScriptRun(GEntity ent)
        {
            if (ent?.Script == null) return;

            EntityScript script = ent.Script;

            if (script.Waiting && Level.Time < script.WakeTime) return;
            script.Waiting = false;

            ScriptAction action = script.CurrentAction;
            while (action != null)
            {
                bool completed = DispatchAction(ent, action);
                if (!completed) return;
                action = action.Next;
                script.CurrentAction = action;
            }

            script.InScript = false;
        }

        // -----------------------------------------------------------------------
        // G_Script_Parse
        // -----------------------------------------------------------------------
        public static EntityScript G_Script_Parse(string scriptText, string targetName)
        {
            var script = new EntityScript { FileName = targetName };
            ParseEntityBlock(scriptText, 0, scriptText.Length, script);
            return script;
        }

        // -----------------------------------------------------------------------
        // Internal parser
        // -----------------------------------------------------------------------
        private static void ParseScriptFile(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                SkipWhitespaceAndComments(text, ref i);
                if (i >= text.Length) break;

                // Read targetname token
                string targetName = ReadToken(text, ref i);
                if (string.IsNullOrEmpty(targetName)) break;

                SkipWhitespaceAndComments(text, ref i);
                if (i >= text.Length || text[i] != '{')
                {
                    Debug.LogWarning($"[GameScript] Expected '{{' after targetname '{targetName}'");
                    SkipToNextLine(text, ref i);
                    continue;
                }
                i++; // consume '{'

                int blockStart = i;
                int blockEnd   = FindMatchingBrace(text, i);
                if (blockEnd < 0)
                {
                    Debug.LogWarning($"[GameScript] Unmatched '{{' for entity '{targetName}'");
                    break;
                }

                string blockText = text.Substring(blockStart, blockEnd - blockStart);
                var script = new EntityScript { FileName = targetName };
                ParseEntityBlock(blockText, 0, blockText.Length, script);
                _scripts[targetName] = script;

                i = blockEnd + 1; // skip closing '}'
            }
        }

        private static void ParseEntityBlock(string text, int start, int end, EntityScript script)
        {
            int i = start;
            while (i < end)
            {
                SkipWhitespaceAndComments(text, ref i);
                if (i >= end) break;

                string eventName = ReadToken(text, ref i);
                if (string.IsNullOrEmpty(eventName)) break;

                int eventIdx = Array.IndexOf(_eventNames, eventName.ToLowerInvariant());

                SkipWhitespaceAndComments(text, ref i);
                if (i >= end || text[i] != '{')
                {
                    SkipToNextLine(text, ref i);
                    continue;
                }
                i++; // consume '{'

                int blockStart = i;
                int blockEnd   = FindMatchingBrace(text, i);
                if (blockEnd < 0) break;

                string actionText = text.Substring(blockStart, blockEnd - blockStart);
                ScriptAction head = ParseActionBlock(actionText);

                if (eventIdx >= 0 && eventIdx < (int)ScriptEventType.NumEvents)
                    script.Events[eventIdx] = head;

                i = blockEnd + 1;
            }
        }

        private static ScriptAction ParseActionBlock(string text)
        {
            ScriptAction head = null;
            ScriptAction tail = null;
            int i = 0;

            while (i < text.Length)
            {
                SkipWhitespaceAndComments(text, ref i);
                if (i >= text.Length) break;

                // Read rest of line as a tokenized action
                int lineEnd = text.IndexOf('\n', i);
                if (lineEnd < 0) lineEnd = text.Length;

                string line = text.Substring(i, lineEnd - i).Trim();
                i = lineEnd + 1;

                if (string.IsNullOrEmpty(line)) continue;
                // skip comment-only lines
                if (line.StartsWith("//")) continue;

                string[] tokens = CmdSystem.Tokenize(line);
                if (tokens.Length == 0) continue;

                var action = new ScriptAction
                {
                    ActionString = tokens[0].ToLowerInvariant(),
                    Params       = tokens.Length > 1
                                       ? tokens[1..]
                                       : Array.Empty<string>(),
                    Delay        = 0,
                };

                if (head == null) { head = tail = action; }
                else              { tail.Next = action; tail = action; }
            }

            return head;
        }

        // Advances i past whitespace and // line-comments.
        private static void SkipWhitespaceAndComments(string text, ref int i)
        {
            while (i < text.Length)
            {
                if (char.IsWhiteSpace(text[i])) { i++; continue; }
                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                break;
            }
        }

        private static void SkipToNextLine(string text, ref int i)
        {
            while (i < text.Length && text[i] != '\n') i++;
            if (i < text.Length) i++;
        }

        private static string ReadToken(string text, ref int i)
        {
            SkipWhitespaceAndComments(text, ref i);
            if (i >= text.Length) return null;

            if (text[i] == '"')
            {
                i++;
                int start = i;
                while (i < text.Length && text[i] != '"') i++;
                string t = text.Substring(start, i - start);
                if (i < text.Length) i++;
                return t;
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}') i++;
                return text.Substring(start, i - start);
            }
        }

        // Returns the index of the '}' that closes the '{' whose content starts at i.
        // i should be positioned just AFTER the opening '{'.
        private static int FindMatchingBrace(string text, int i)
        {
            int depth = 1;
            bool inQuote = false;
            while (i < text.Length)
            {
                char c = text[i];
                if (inQuote)
                {
                    if (c == '"') inQuote = false;
                }
                else if (c == '"') { inQuote = true; }
                else if (c == '{') { depth++; }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
                i++;
            }
            return -1;
        }

        // -----------------------------------------------------------------------
        // Action dispatcher
        // -----------------------------------------------------------------------
        private static bool DispatchAction(GEntity ent, ScriptAction action)
        {
            switch (action.ActionString)
            {
                case "wait":                return Action_Wait(ent, action);
                case "wait_for_trigger":    return Action_WaitForTrigger(ent, action);
                case "trigger":             return Action_Trigger(ent, action);
                case "set_health":          return Action_SetHealth(ent, action);
                case "cvar_set":            return Action_CvarSet(ent, action);
                case "wm_announce":         return Action_WmAnnounce(ent, action);
                case "wm_endround":         return Action_WmEndRound(ent, action);
                case "wm_set_objective_status": return Action_WmSetObjectiveStatus(ent, action);
                case "wm_axis_respawn_time":    return Action_WmRespawnTime(ent, action, isAxis: true);
                case "wm_allied_respawn_time":  return Action_WmRespawnTime(ent, action, isAxis: false);
                case "play_sound":          return Action_PlaySound(ent, action);
                case "println":             return Action_Println(ent, action);
                case "accum":               return Action_Accum(ent, action);
                case "if":                  return Action_If(ent, action);
                case "remove":              return Action_Remove(ent, action);
                default:
                    Debug.LogWarning($"[GameScript] Unknown action '{action.ActionString}' on entity '{ent.TargetName}'");
                    return true;
            }
        }

        // -----------------------------------------------------------------------
        // Action handlers
        // -----------------------------------------------------------------------

        private static bool Action_Wait(GEntity ent, ScriptAction action)
        {
            if (ent.Script.Waiting) return false;

            int ms = 0;
            if (action.Params.Length > 0)
                int.TryParse(action.Params[0], out ms);

            ent.Script.Waiting  = true;
            ent.Script.WakeTime = Level.Time + ms;
            return false;
        }

        private static bool Action_WaitForTrigger(GEntity ent, ScriptAction action)
        {
            // Suspend until the next Trigger event fires on this entity.
            // The event handler sets CurrentAction and re-runs the script,
            // so we simply halt here; the Trigger event path will resume us.
            ent.Script.Waiting  = true;
            ent.Script.WakeTime = int.MaxValue;
            return false;
        }

        private static bool Action_Trigger(GEntity ent, ScriptAction action)
        {
            if (action.Params.Length < 2) return true;

            string targetName = action.Params[0];
            string eventName  = action.Params[1].ToLowerInvariant();

            int eventIdx = Array.IndexOf(_eventNames, eventName);
            if (eventIdx < 0)
            {
                Debug.LogWarning($"[GameScript] trigger: unknown event '{eventName}'");
                return true;
            }

            ScriptEventType evtType = (ScriptEventType)eventIdx;

            if (Entities == null) return true;
            for (int i = 0; i < NumEntities; i++)
            {
                GEntity target = Entities[i];
                if (target == null) continue;
                if (!string.Equals(target.TargetName, targetName, StringComparison.OrdinalIgnoreCase)) continue;
                G_Script_ScriptEvent(target, evtType, "");
            }

            return true;
        }

        private static bool Action_SetHealth(GEntity ent, ScriptAction action)
        {
            if (action.Params.Length > 0 && int.TryParse(action.Params[0], out int hp))
                ent.Health = hp;
            return true;
        }

        private static bool Action_CvarSet(GEntity ent, ScriptAction action)
        {
            if (action.Params.Length < 2) return true;
            CvarSystem.Set(action.Params[0], action.Params[1]);
            return true;
        }

        private static bool Action_WmAnnounce(GEntity ent, ScriptAction action)
        {
            string message = action.Params.Length > 0 ? action.Params[0] : "";
            string sound   = action.Params.Length > 1 ? action.Params[1] : "";
            OnAnnouncement?.Invoke(message, sound);
            // Also send as a server command so all clients see it.
            ServerGame.SV_GameSendServerCommand(-1, $"cp \"{message}\"");
            return true;
        }

        private static bool Action_WmEndRound(GEntity ent, ScriptAction action)
        {
            OnRoundEnd?.Invoke();
            return true;
        }

        private static bool Action_WmSetObjectiveStatus(GEntity ent, ScriptAction action)
        {
            // wm_set_objective_status <team> <objnum> <status>
            if (action.Params.Length < 3) return true;

            int.TryParse(action.Params[0], out int team);
            int.TryParse(action.Params[1], out int objNum);
            int.TryParse(action.Params[2], out int status);

            OnObjectiveStatus?.Invoke(team, objNum, status);
            return true;
        }

        private static bool Action_WmRespawnTime(GEntity ent, ScriptAction action, bool isAxis)
        {
            int seconds = 0;
            if (action.Params.Length > 0)
                int.TryParse(action.Params[0], out seconds);

            OnRespawnTimeChange?.Invoke(isAxis, seconds);

            string cvarName = isAxis ? "g_userAxisRespawnTime" : "g_userAlliedRespawnTime";
            CvarSystem.Set(cvarName, seconds.ToString());
            return true;
        }

        private static bool Action_PlaySound(GEntity ent, ScriptAction action)
        {
            if (action.Params.Length == 0) return true;
            string soundName = action.Params[0];
            // Broadcast a sound command; clients resolve the asset name.
            ServerGame.SV_GameSendServerCommand(-1, $"sound \"{soundName}\"");
            return true;
        }

        private static bool Action_Println(GEntity ent, ScriptAction action)
        {
            string msg = action.Params.Length > 0 ? action.Params[0] : "";
            Debug.Log($"[Script:{ent.TargetName}] {msg}");
            return true;
        }

        private static bool Action_Accum(GEntity ent, ScriptAction action)
        {
            // accum <bufnum> <op> <val>
            // bufnum is ignored; we use EntityScript.AccumBuffer as a single integer store.
            if (action.Params.Length < 2) return true;

            string op  = action.Params.Length > 1 ? action.Params[1].ToLowerInvariant() : "";
            string val = action.Params.Length > 2 ? action.Params[2] : "0";

            int.TryParse(ent.Script.AccumBuffer ?? "0", out int current);
            int.TryParse(val, out int operand);

            switch (op)
            {
                case "set":
                    current = operand;
                    break;
                case "inc":
                    current += operand;
                    break;
                case "dec":
                    current -= operand;
                    break;
                case "abort_if_not_equal":
                    if (current != operand)
                    {
                        ent.Script.CurrentAction = null;
                        ent.Script.InScript      = false;
                        return false;
                    }
                    break;
                case "abort_if_equal":
                    if (current == operand)
                    {
                        ent.Script.CurrentAction = null;
                        ent.Script.InScript      = false;
                        return false;
                    }
                    break;
                default:
                    Debug.LogWarning($"[GameScript] accum: unknown op '{op}'");
                    break;
            }

            ent.Script.AccumBuffer = current.ToString();
            return true;
        }

        private static bool Action_If(GEntity ent, ScriptAction action)
        {
            // if <cvarname> <op> <value>
            // ops: ==, !=, <, >, <=, >=
            if (action.Params.Length < 3) return true;

            string cvarName = action.Params[0];
            string op       = action.Params[1];
            string rhs      = action.Params[2];

            Cvar cv = CvarSystem.Find(cvarName);
            string lhsStr = cv != null ? cv.StringValue : "0";

            bool condition;
            // Try numeric comparison first, fall back to string
            if (float.TryParse(lhsStr, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float lhsF) &&
                float.TryParse(rhs,    System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float rhsF))
            {
                condition = op switch
                {
                    "==" => lhsF == rhsF,
                    "!=" => lhsF != rhsF,
                    "<"  => lhsF <  rhsF,
                    ">"  => lhsF >  rhsF,
                    "<=" => lhsF <= rhsF,
                    ">=" => lhsF >= rhsF,
                    _    => false,
                };
            }
            else
            {
                int cmp = string.Compare(lhsStr, rhs, StringComparison.OrdinalIgnoreCase);
                condition = op switch
                {
                    "==" => cmp == 0,
                    "!=" => cmp != 0,
                    "<"  => cmp <  0,
                    ">"  => cmp >  0,
                    "<=" => cmp <= 0,
                    ">=" => cmp >= 0,
                    _    => false,
                };
            }

            // If condition is false, skip the next action in the chain.
            if (!condition && ent.Script.CurrentAction?.Next != null)
                ent.Script.CurrentAction = ent.Script.CurrentAction.Next;

            return true;
        }

        private static bool Action_Remove(GEntity ent, ScriptAction action)
        {
            // Signal removal; actual entity free is handled by ServerGameLogic.
            ent.Health = -1;
            ent.Script = null;
            return true;
        }
    }
}
