// Ports: g_character.c + g_config.c + g_mem.c + g_save.c (stub)
// g_save.c is wrapped in #ifdef SAVEGAME_SUPPORT (unused in ET release build);
// replaced by Unity PlayerPrefs in GameMatch.cs (GameSession).
// g_mem.c fixed-pool allocator is unnecessary — C# GC handles allocation.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Game;

namespace ET.Server
{
    // -------------------------------------------------------------------------
    // GameMemory — g_mem.c
    // In C this was a 4 MB static bump allocator used for level-lifetime strings
    // and script data. In C# we use normal heap allocation; this class exists
    // only to provide the status command hook.
    // -------------------------------------------------------------------------
    public static class GameMemory
    {
        private static int _bytesAllocated;

        public static T Alloc<T>() where T : new()
        {
            _bytesAllocated += System.Runtime.InteropServices.Marshal.SizeOf<T>();
            return new T();
        }

        public static void Init() => _bytesAllocated = 0;

        public static string StatusString() =>
            $"Game memory: {_bytesAllocated} bytes allocated (C# GC managed)";
    }

    // -------------------------------------------------------------------------
    // GameConfig — g_config.c
    // Competition / public server preset cvar tables.
    // -------------------------------------------------------------------------
    public enum ServerConfigMode { Competition, Public }

    public static class GameConfig
    {
        private struct ModeCvar { public string Name; public string Value; }

        private static readonly ModeCvar[] _compSettings =
        {
            new ModeCvar { Name = "g_altStopwatchMode",    Value = "0"  },
            new ModeCvar { Name = "g_complaintlimit",      Value = "0"  },
            new ModeCvar { Name = "g_ipcomplaintlimit",    Value = "0"  },
            new ModeCvar { Name = "g_doWarmup",            Value = "1"  },
            new ModeCvar { Name = "g_inactivity",          Value = "0"  },
            new ModeCvar { Name = "g_maxlives",            Value = "0"  },
            new ModeCvar { Name = "g_teamforcebalance",    Value = "0"  },
            new ModeCvar { Name = "g_lms_teamForceBalance",Value = "0"  },
            new ModeCvar { Name = "g_voicechatsallowed",   Value = "50" },
            new ModeCvar { Name = "g_warmup",              Value = "10" },
            new ModeCvar { Name = "match_latejoin",        Value = "0"  },
            new ModeCvar { Name = "match_mutespecs",       Value = "0"  },
            new ModeCvar { Name = "match_timeoutcount",    Value = "1"  },
            new ModeCvar { Name = "match_warmupDamage",    Value = "1"  },
            new ModeCvar { Name = "team_maxplayers",       Value = "10" },
            new ModeCvar { Name = "team_nocontrols",       Value = "0"  },
            new ModeCvar { Name = "sv_allowDownload",      Value = "1"  },
            new ModeCvar { Name = "sv_floodProtect",       Value = "0"  },
            new ModeCvar { Name = "sv_fps",                Value = "20" },
            new ModeCvar { Name = "vote_limit",            Value = "10" },
        };

        private static readonly ModeCvar[] _pubSettings =
        {
            new ModeCvar { Name = "g_altStopwatchMode",    Value = "0"  },
            new ModeCvar { Name = "g_complaintlimit",      Value = "6"  },
            new ModeCvar { Name = "g_ipcomplaintlimit",    Value = "3"  },
            new ModeCvar { Name = "g_doWarmup",            Value = "0"  },
            new ModeCvar { Name = "g_maxlives",            Value = "0"  },
            new ModeCvar { Name = "g_teamforcebalance",    Value = "1"  },
            new ModeCvar { Name = "g_lms_teamForceBalance",Value = "1"  },
            new ModeCvar { Name = "g_voicechatsallowed",   Value = "50" },
            new ModeCvar { Name = "g_warmup",              Value = "60" },
            new ModeCvar { Name = "match_latejoin",        Value = "1"  },
            new ModeCvar { Name = "match_mutespecs",       Value = "1"  },
            new ModeCvar { Name = "match_timeoutcount",    Value = "0"  },
            new ModeCvar { Name = "match_warmupDamage",    Value = "0"  },
            new ModeCvar { Name = "team_maxplayers",       Value = "0"  },
            new ModeCvar { Name = "team_nocontrols",       Value = "1"  },
            new ModeCvar { Name = "sv_allowDownload",      Value = "1"  },
            new ModeCvar { Name = "sv_floodProtect",       Value = "0"  },
            new ModeCvar { Name = "sv_fps",                Value = "20" },
            new ModeCvar { Name = "vote_limit",            Value = "5"  },
        };

        // G_configSet
        public static void Apply(ServerConfigMode mode)
        {
            var table = mode == ServerConfigMode.Competition ? _compSettings : _pubSettings;
            
            foreach (var mc in table)
            {
                CvarSystem.Set(mc.Name, mc.Value);
                Debug.Log($"config: set {mc.Name} {mc.Value}");
            }
            Debug.Log($">> {mode} settings loaded!");
            CmdSystem.Cmd_ExecuteString($"map_restart 0 {GameConstants.GS_WARMUP}");
        }
    }

    // -------------------------------------------------------------------------
    // GameCharacter — g_character.c
    // Server-side character registration: links bg_character_t to its animation
    // group + script, then calls the shared BgAnimGroup / BgAnimScript parsers.
    // -------------------------------------------------------------------------
    public class ServerCharacter
    {
        public string CharacterFile;
        public string AnimationGroup;
        public string AnimationScript;
        public BgAnimModelInfo AnimModelInfo;
    }

    public static class GameCharacterSystem
    {
        // MAX_ANIMSCRIPT_MODELS = 32
        private const int MAX_ANIMSCRIPT_MODELS = 32;
        private static readonly BgAnimModelInfo[] _modelInfoPool =
            new BgAnimModelInfo[MAX_ANIMSCRIPT_MODELS];

        private static readonly Dictionary<string, ServerCharacter> _characterCache =
            new Dictionary<string, ServerCharacter>(StringComparer.OrdinalIgnoreCase);

        // G_CheckForExistingAnimModelInfo
        private static bool TryGetOrAllocModelInfo(
            string animGroup, string animScript, out BgAnimModelInfo info)
        {
            BgAnimModelInfo firstFree = null;
            for (int i = 0; i < MAX_ANIMSCRIPT_MODELS; i++)
            {
                if (_modelInfoPool[i] == null)
                {
                    if (firstFree == null) firstFree = _modelInfoPool[i] = new BgAnimModelInfo();
                    continue;
                }
                if (string.Equals(_modelInfoPool[i].AnimationGroup, animGroup,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_modelInfoPool[i].AnimationScript, animScript,
                        StringComparison.OrdinalIgnoreCase))
                {
                    info = _modelInfoPool[i];
                    return true;   // found existing — no re-parse needed
                }
            }
            if (firstFree == null)
                throw new InvalidOperationException("No free AnimModelInfo slot");

            info = firstFree;
            return false;
        }

        // G_ParseAnimationFiles
        private static bool ParseAnimationFiles(
            ServerCharacter ch, string animGroup, string animScript)
        {
            ch.AnimModelInfo.AnimationGroup  = animGroup;
            ch.AnimModelInfo.AnimationScript = animScript;

            // Load the animation group (shared bg layer)
            if (!BgAnimGroup.RegisterAnimationGroup(animGroup, ch.AnimModelInfo))
                return false;

            // Load and parse the animation script
            string text = FileSystem.ReadAllText(animScript);
            if (string.IsNullOrEmpty(text))
            {
                Debug.LogWarning($"G_ParseAnimationFiles: cannot read {animScript}");
                return false;
            }

            BgAnimScript.ParseAnimScript(ch.AnimModelInfo, animScript, text);
            return true;
        }

        // G_RegisterCharacter
        public static bool RegisterCharacter(string characterFile, ServerCharacter ch)
        {
            if (_characterCache.TryGetValue(characterFile, out var cached))
            {
                ch.AnimationGroup  = cached.AnimationGroup;
                ch.AnimationScript = cached.AnimationScript;
                ch.AnimModelInfo   = cached.AnimModelInfo;
                return true;
            }

            var def = GameClasses.BG_ParseCharacterFile(characterFile);
            if (def == null)
            {
                Debug.LogWarning($"G_RegisterCharacter: parse failed for {characterFile}");
                return false;
            }

            ch.CharacterFile   = characterFile;
            ch.AnimationGroup  = def.AnimationGroup;
            ch.AnimationScript = def.AnimationScript;

            bool existing = TryGetOrAllocModelInfo(def.AnimationGroup, def.AnimationScript,
                out ch.AnimModelInfo);

            if (!existing)
            {
                if (!ParseAnimationFiles(ch, def.AnimationGroup, def.AnimationScript))
                {
                    Debug.LogWarning(
                        $"G_RegisterCharacter: failed to load anim files from {characterFile}");
                    return false;
                }
            }

            _characterCache[characterFile] = ch;
            return true;
        }

        // G_RegisterPlayerClasses — called during G_InitGame
        public static void RegisterPlayerClasses()
        {
            // Teams: 1=Axis, 2=Allies; classes: 0=Soldier..4=CovertOps
            for (int team = 1; team <= 2; team++)
            {
                for (int cls = 0; cls < 5; cls++)
                {
                    var classInfo = GameClasses.GetPlayerClassInfo((PlayerClass)cls);
                    if (classInfo == null) continue;

                    string file = classInfo.CharacterFile;
                    var ch = new ServerCharacter { CharacterFile = file };
                    if (!RegisterCharacter(file, ch))
                        Debug.LogError(
                            $"G_RegisterPlayerClasses: failed to load {file} for " +
                            $"team={team} class={cls}");
                }
            }
        }

        // G_UpdateCharacter — called on ClientUserinfoChanged
        public static void UpdateCharacter(GClient client)
        {
            string infoString = ServerGameLogic.GetClientUserinfo(client.PS.ClientNum);
            string chStr = ET.Core.ETShared.Info_ValueForKey(infoString, "ch");

            if (!string.IsNullOrEmpty(chStr) && int.TryParse(chStr, out int charIndex)
                && charIndex >= 0 && charIndex < 64)
            {
                if (client.Pers.CharacterIndex != charIndex)
                {
                    client.Pers.CharacterIndex = charIndex;
                    var file = ServerGameLogic.GetConfigstring(
                        GameConstants.CS_CHARACTERS + charIndex);

                    var ch = new ServerCharacter();
                    if (!RegisterCharacter(file, ch))
                    {
                        Debug.LogWarning(
                            $"G_UpdateCharacter: failed to load {file} for {client.Pers.NetName}");
                        SetDefaultCharacter(client);
                        return;
                    }
                    client.Pers.Character = ch;

                    // Reset animations on model change
                    client.PS.LegsAnim   = 0;
                    client.PS.TorsoAnim  = 0;
                    client.PS.LegsTimer  = 0;
                    client.PS.TorsoTimer = 0;
                }
                return;
            }

            SetDefaultCharacter(client);
        }

        private static void SetDefaultCharacter(GClient client)
        {
            int team = client.Sess.SessionTeam;
            int cls  = client.Sess.PlayerType;
            var classInfo = GameClasses.GetPlayerClassInfo((PlayerClass)cls);
            if (classInfo == null) return;

            if (client.Pers.CharacterIndex == -1 &&
                client.Pers.Character?.CharacterFile == classInfo.CharacterFile)
                return;

            var ch = new ServerCharacter { CharacterFile = classInfo.CharacterFile };
            RegisterCharacter(classInfo.CharacterFile, ch);
            client.Pers.CharacterIndex = -1;
            client.Pers.Character = ch;

            client.PS.LegsAnim   = 0;
            client.PS.TorsoAnim  = 0;
            client.PS.LegsTimer  = 0;
            client.PS.TorsoTimer = 0;
        }
    }
}
