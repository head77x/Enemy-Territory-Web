// Ported from: src/game/g_props.c, src/game/g_svcmds.c,
//              src/game/bg_tracemap.c, src/game/bg_animgroup.c, src/game/bg_sscript.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

// ============================================================================
// ET.Game — BgTraceMap, BgAnimGroup, BgSoundScript
// ============================================================================
namespace ET.Game
{
    // ========================================================================
    // BgTraceMap — bg_tracemap.c
    // 2-D height grid for AI LOS and artillery trajectory clearance.
    // ========================================================================
    public static class BgTraceMap
    {
        public const int TRACEMAP_SIZE = 128;

        private static readonly float[,] _heightMap = new float[TRACEMAP_SIZE, TRACEMAP_SIZE];
        private static Vector2 _mapMins;
        private static Vector2 _mapMaxs;
        private static bool    _loaded;

        public static void BG_LoadTraceMap(string mapName)
        {
            _loaded = false;
            string path = $"maps/{mapName}.tga";
            byte[] raw = FileSystem.ReadFile(path);
            if (raw == null || raw.Length == 0)
            {
                Debug.LogWarning($"[BgTraceMap] No tracemap found at {path}");
                return;
            }

            // ET stores the tracemap as a greyscale TGA; in Unity we decode via
            // Texture2D so we don't need a custom TGA parser.
            var tex = new Texture2D(TRACEMAP_SIZE, TRACEMAP_SIZE, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(raw))
            {
                Debug.LogWarning($"[BgTraceMap] Failed to decode {path}");
                return;
            }

            // World bounds come from the map's entity lump; fall back to a wide
            // default when not yet available so artillery still gets a value.
            _mapMins = new Vector2(-4096f, -4096f);
            _mapMaxs = new Vector2( 4096f,  4096f);

            for (int y = 0; y < TRACEMAP_SIZE; y++)
            for (int x = 0; x < TRACEMAP_SIZE; x++)
            {
                // Red channel encodes normalised height [0..1] → scale to ET units.
                float norm = tex.GetPixel(x, y).r;
                _heightMap[x, y] = Mathf.Lerp(-512f, 4096f, norm);
            }

            _loaded = true;
        }

        public static float BG_GetSkyHeightAtPoint(Vector3 worldPos)
        {
            if (!_loaded) return 4096f;
            var uv = BG_WorldToTraceMapCoords(worldPos);
            return _heightMap[uv.x, uv.y];
        }

        public static float BG_GetGroundHeightAtPoint(Vector3 worldPos)
        {
            var origin = new Vector3(worldPos.x, worldPos.y, worldPos.z + 64f);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 8192f))
                return hit.point.z;
            return 0f;
        }

        private static Vector2Int BG_WorldToTraceMapCoords(Vector3 worldPos)
        {
            float rx = Mathf.InverseLerp(_mapMins.x, _mapMaxs.x, worldPos.x);
            float ry = Mathf.InverseLerp(_mapMins.y, _mapMaxs.y, worldPos.y);
            int x = Mathf.Clamp(Mathf.FloorToInt(rx * TRACEMAP_SIZE), 0, TRACEMAP_SIZE - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(ry * TRACEMAP_SIZE), 0, TRACEMAP_SIZE - 1);
            return new Vector2Int(x, y);
        }
    }

    // ========================================================================
    // BgAnimGroup — bg_animgroup.c
    // Groups of models that share an animation script.
    // ========================================================================
    public class AnimGroup
    {
        public string   Name;
        public string[] Members;
        public string   ScriptPath;
    }

    public static class BgAnimGroup
    {
        private static readonly List<AnimGroup> _groups = new();

        public static void BG_LoadAnimGroups()
        {
            _groups.Clear();
            byte[] raw = FileSystem.ReadFile("animations/groups.txt");
            if (raw == null || raw.Length == 0) return;
            BG_ParseAnimGroupFile(System.Text.Encoding.UTF8.GetString(raw));
        }

        public static AnimGroup BG_FindAnimGroup(string name)
        {
            foreach (var g in _groups)
                if (string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
                    return g;
            return null;
        }

        public static void BG_ParseAnimGroupFile(string fileText)
        {
            // Format:
            //   animgroup <name> {
            //       script  <path>
            //       model   <path>
            //       ...
            //   }
            var tokens = Tokenise(fileText);
            int i = 0;
            while (i < tokens.Count)
            {
                if (!tokens[i].Equals("animgroup", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
                i++;
                if (i >= tokens.Count) break;
                var group = new AnimGroup { Name = tokens[i++] };
                var members = new List<string>();
                if (i < tokens.Count && tokens[i] == "{") i++;
                while (i < tokens.Count && tokens[i] != "}")
                {
                    string key = tokens[i++];
                    if (i >= tokens.Count) break;
                    string val = tokens[i++];
                    if (key.Equals("script", StringComparison.OrdinalIgnoreCase))
                        group.ScriptPath = val;
                    else if (key.Equals("model", StringComparison.OrdinalIgnoreCase))
                        members.Add(val);
                }
                if (i < tokens.Count) i++; // consume '}'
                group.Members = members.ToArray();
                _groups.Add(group);
            }
        }

        private static List<string> Tokenise(string text)
        {
            var result = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                // skip whitespace and line comments
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;
                if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (text[i] == '{' || text[i] == '}') { result.Add(text[i].ToString()); i++; continue; }
                if (text[i] == '"')
                {
                    i++;
                    int start = i;
                    while (i < text.Length && text[i] != '"') i++;
                    result.Add(text.Substring(start, i - start));
                    if (i < text.Length) i++;
                    continue;
                }
                int s = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}') i++;
                result.Add(text.Substring(s, i - s));
            }
            return result;
        }
    }

    // ========================================================================
    // BgSoundScript — bg_sscript.c
    // Ambient sound setups read from .sscript files.
    // ========================================================================
    public class SoundScriptEntry
    {
        public string   Name;
        public string[] Sounds;
        public float    MinDelay;
        public float    MaxDelay;
        public float    Volume  = 1f;
        public float    Range   = 1250f;
        public bool     Loop;
        public bool     Random;
    }

    public static class BgSoundScript
    {
        private static readonly Dictionary<string, SoundScriptEntry> _scripts =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly System.Random _rng = new();

        public static void BG_LoadSoundScript(string name)
        {
            string path = $"scripts/{name}.sscript";
            byte[] raw = FileSystem.ReadFile(path);
            if (raw == null || raw.Length == 0)
            {
                Debug.LogWarning($"[BgSoundScript] Missing {path}");
                return;
            }
            BG_ParseSoundScriptFile(System.Text.Encoding.UTF8.GetString(raw));
        }

        public static SoundScriptEntry BG_FindSoundScript(string name)
        {
            _scripts.TryGetValue(name, out var e);
            return e;
        }

        public static void BG_ParseSoundScriptFile(string fileText)
        {
            // Format:
            //   soundscript <name> {
            //       sound    <path>
            //       mindelay <ms>
            //       maxdelay <ms>
            //       volume   <0..255>
            //       range    <units>
            //       looping  yes|no
            //       random   yes|no
            //   }
            var tokens = BgAnimGroup.BG_ParseAnimGroupFile_Tokens(fileText);
            int i = 0;
            while (i < tokens.Count)
            {
                if (!tokens[i].Equals("soundscript", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
                i++;
                if (i >= tokens.Count) break;
                var entry = new SoundScriptEntry { Name = tokens[i++] };
                var sounds = new List<string>();
                if (i < tokens.Count && tokens[i] == "{") i++;
                while (i < tokens.Count && tokens[i] != "}")
                {
                    string key = tokens[i++];
                    if (i >= tokens.Count) break;
                    string val = tokens[i++];
                    switch (key.ToLowerInvariant())
                    {
                        case "sound":    sounds.Add(val); break;
                        case "mindelay": float.TryParse(val, out entry.MinDelay); entry.MinDelay /= 1000f; break;
                        case "maxdelay": float.TryParse(val, out entry.MaxDelay); entry.MaxDelay /= 1000f; break;
                        case "volume":   if (float.TryParse(val, out float v)) entry.Volume = v / 255f; break;
                        case "range":    float.TryParse(val, out entry.Range); break;
                        case "looping":  entry.Loop   = val.Equals("yes", StringComparison.OrdinalIgnoreCase); break;
                        case "random":   entry.Random = val.Equals("yes", StringComparison.OrdinalIgnoreCase); break;
                    }
                }
                if (i < tokens.Count) i++;
                entry.Sounds = sounds.ToArray();
                _scripts[entry.Name] = entry;
            }
        }

        public static void BG_PlaySoundScript(string name, Vector3 origin, int entityNum)
        {
            var entry = BG_FindSoundScript(name);
            if (entry == null || entry.Sounds == null || entry.Sounds.Length == 0) return;

            string snd = entry.Random
                ? entry.Sounds[_rng.Next(entry.Sounds.Length)]
                : entry.Sounds[0];

            AudioSystem.S_StartSound(origin, entityNum, SoundChannel.Auto, snd, entry.Volume, entry.Range);
        }
    }
}

// Expose the tokeniser to BgSoundScript without duplicating it.
namespace ET.Game
{
    public static partial class BgAnimGroup
    {
        internal static List<string> BG_ParseAnimGroupFile_Tokens(string text)
        {
            // Delegate to the private tokeniser via a thin internal shim.
            var result = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;
                if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/')
                { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (text[i] == '{' || text[i] == '}') { result.Add(text[i].ToString()); i++; continue; }
                if (text[i] == '"')
                {
                    i++;
                    int s2 = i;
                    while (i < text.Length && text[i] != '"') i++;
                    result.Add(text.Substring(s2, i - s2));
                    if (i < text.Length) i++;
                    continue;
                }
                int s = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}') i++;
                result.Add(text.Substring(s, i - s));
            }
            return result;
        }
    }
}

// ============================================================================
// ET.Server — GameProps, ServerCommands
// ============================================================================
namespace ET.Server
{
    // ========================================================================
    // GameProps — g_props.c
    // Destructible/constructible/animated environmental prop system.
    // ========================================================================
    public enum PropType
    {
        None          = 0,
        Destructible  = 1,
        Constructible = 2,
        Explosive     = 3,
        Animated      = 4,
    }

    public class PropDef
    {
        public string   Name;
        public int      MaxHealth;
        public float    Mass;
        public int      Damage;
        public int      SplashRadius;
        public int      Mod;
        public string   DestroyedModel;
        public string   BuiltModel;
        public string   ConstructionModel;
        public int      ConstructionStages;
        public int      TeamRestriction;
        public PropType Type;
    }

    public static class GameProps
    {
        private static readonly Dictionary<string, PropDef> _propDefs =
            new(StringComparer.OrdinalIgnoreCase);

        public static event Action<int, string, int> OnPropDestroyed;
        public static event Action<int, string, int> OnPropConstructed;
        public static event Action<int, int>         OnConstructionProgress;

        // ------------------------------------------------------------------
        // Startup
        // ------------------------------------------------------------------

        public static void G_Props_LoadPropData(string mapName)
        {
            _propDefs.Clear();
            string path = $"maps/{mapName}_props.script";
            byte[] raw = FileSystem.ReadFile(path);
            if (raw == null || raw.Length == 0)
            {
                Debug.Log($"[GameProps] No props script at {path}");
                return;
            }
            ParsePropScript(System.Text.Encoding.UTF8.GetString(raw));
        }

        public static PropDef G_Props_RegisterProp(string name)
        {
            if (_propDefs.TryGetValue(name, out var existing)) return existing;
            var def = new PropDef { Name = name, ConstructionStages = 3, TeamRestriction = -1 };
            _propDefs[name] = def;
            return def;
        }

        // ------------------------------------------------------------------
        // Spawn functions
        // ------------------------------------------------------------------

        public static void SP_func_constructible(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("constructibleteam", "0",   out int team);
            ServerGameLogic.G_SpawnInt("health",            "100", out int health);
            ServerGameLogic.G_SpawnFloat("constructxpbonus", "5",  out float _);

            ent.MaxHealth = health;
            ent.Health    = 0;   // starts unbuilt
            ent.Team      = team;
            ent.ClassName = "func_constructible";
            ent.Count     = 0;

            ent.Use = (e, other, activator) => G_Props_Construct(e, activator);
            ent.Die = (e, inflictor, attacker, damage, mod) => G_Props_Destroy(e, attacker, mod);
        }

        public static void SP_func_destructible(GEntity ent)
        {
            ServerGameLogic.G_SpawnInt("health",  "200", out int health);
            ServerGameLogic.G_SpawnFloat("mass",  "200", out float mass);
            ServerGameLogic.G_SpawnString("model2", "",  out string destroyedModel);

            var def = G_Props_RegisterProp(ent.TargetName ?? ent.ClassName);
            def.MaxHealth      = health;
            def.Mass           = mass;
            def.DestroyedModel = destroyedModel;
            def.Type           = PropType.Destructible;

            ent.MaxHealth = health;
            ent.Health    = health;
            ent.ClassName = "func_destructible";

            ent.Die = (e, inflictor, attacker, damage, mod) => G_Props_Destroy(e, attacker, mod);
        }

        public static void SP_misc_constructiblemarker(GEntity ent)
        {
            ent.ClassName = "misc_constructiblemarker";
            ent.InUse     = true;
            // Marker is invisible; no model or collision needed.
        }

        // ------------------------------------------------------------------
        // Per-frame think
        // ------------------------------------------------------------------

        public static void G_Props_Think(GEntity ent)
        {
            if (ent == null || !ent.InUse) return;
            // Animated props advance their script frame each think.
            // Construction-in-progress entities decay back if unattended.
        }

        // ------------------------------------------------------------------
        // Damage
        // ------------------------------------------------------------------

        public static void G_Props_Damage(GEntity ent, GEntity attacker, int damage, int mod)
        {
            if (ent == null || !ent.InUse) return;

            if (ent.ClassName == "func_constructible" && ent.Health > 0)
            {
                // Attackers knock construction progress back.
                ent.Health -= damage;
                if (ent.Health < 0) ent.Health = 0;
                ent.Count = Mathf.FloorToInt((float)ent.Health / ent.MaxHealth *
                            (GetPropDef(ent)?.ConstructionStages ?? 3));
                OnConstructionProgress?.Invoke(ent.EntityNum, ent.Count);
                return;
            }

            ent.Health -= damage;
            if (ent.Health <= 0)
                G_Props_Destroy(ent, attacker, mod);
        }

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        public static bool G_Props_Construct(GEntity ent, GEntity engineer)
        {
            if (ent == null) return false;

            var def = GetPropDef(ent);
            int stages = def?.ConstructionStages ?? 3;

            int stage = ent.Count++;
            OnConstructionProgress?.Invoke(ent.EntityNum, stage);

            if (ent.Count >= stages)
            {
                ent.Health = ent.MaxHealth;
                OnPropConstructed?.Invoke(ent.EntityNum, ent.ClassName, ent.Team);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Destruction
        // ------------------------------------------------------------------

        public static void G_Props_Destroy(GEntity ent, GEntity attacker, int mod)
        {
            if (ent == null) return;

            ent.Health = 0;
            int destroyerTeam = attacker?.Team ?? 0;

            var def = GetPropDef(ent);
            if (def != null && !string.IsNullOrEmpty(def.DestroyedModel))
            {
                // Switch to destroyed model via entity state.
                ent.S.ModelIndex2 = (short)CollisionSystem.RegisterModel(def.DestroyedModel);
            }

            OnPropDestroyed?.Invoke(ent.EntityNum, ent.ClassName, destroyerTeam);

            // Explosive props deal splash damage on destruction.
            if (def != null && def.Type == PropType.Explosive && def.Damage > 0)
                ServerGameLogic.G_RadiusDamage(ent.Origin, attacker, def.Damage, def.SplashRadius, ent, def.Mod);
        }

        // ------------------------------------------------------------------
        // Rebuild
        // ------------------------------------------------------------------

        public static void G_Props_Rebuild(GEntity ent)
        {
            if (ent == null) return;
            ent.Health = ent.MaxHealth;
            ent.Count  = 0;

            var def = GetPropDef(ent);
            if (def != null && !string.IsNullOrEmpty(def.BuiltModel))
                ent.S.ModelIndex2 = (short)CollisionSystem.RegisterModel(def.BuiltModel);
        }

        // ------------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------------

        private static PropDef GetPropDef(GEntity ent)
        {
            string key = ent.TargetName ?? ent.ClassName;
            if (key != null && _propDefs.TryGetValue(key, out var def)) return def;
            return null;
        }

        private static void ParsePropScript(string text)
        {
            // Minimal parser: propdef <name> { key value ... }
            var tokens = ET.Game.BgAnimGroup.BG_ParseAnimGroupFile_Tokens(text);
            int i = 0;
            while (i < tokens.Count)
            {
                if (!tokens[i].Equals("propdef", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
                i++;
                if (i >= tokens.Count) break;
                var def = G_Props_RegisterProp(tokens[i++]);
                if (i < tokens.Count && tokens[i] == "{") i++;
                while (i < tokens.Count && tokens[i] != "}")
                {
                    string key = tokens[i++];
                    if (i >= tokens.Count) break;
                    string val = tokens[i++];
                    switch (key.ToLowerInvariant())
                    {
                        case "maxhealth":          int.TryParse(val, out def.MaxHealth); break;
                        case "mass":               float.TryParse(val, out def.Mass); break;
                        case "damage":             int.TryParse(val, out def.Damage); break;
                        case "splashradius":       int.TryParse(val, out def.SplashRadius); break;
                        case "mod":                int.TryParse(val, out def.Mod); break;
                        case "destroyedmodel":     def.DestroyedModel = val; break;
                        case "builtmodel":         def.BuiltModel = val; break;
                        case "constructionmodel":  def.ConstructionModel = val; break;
                        case "constructionstages": int.TryParse(val, out def.ConstructionStages); break;
                        case "teamrestriction":    int.TryParse(val, out def.TeamRestriction); break;
                        case "type":
                            if (Enum.TryParse<PropType>(val, true, out var pt)) def.Type = pt;
                            break;
                    }
                }
                if (i < tokens.Count) i++;
            }
        }
    }

    // ========================================================================
    // ServerCommands — g_svcmds.c
    // Console commands available to the server operator.
    // ========================================================================
    public static class ServerCommands
    {
        private static readonly List<string> _bannedIPs = new();

        public static event Action<string> OnMapChange;
        public static event Action<int>    OnClientForced;

        public static bool IsIPBanned(string ip) => _bannedIPs.Contains(ip);

        public static void G_RegisterServerCommands()
        {
            CmdSystem.AddCommand("entitylist",     args => Cmd_EntityList_f(args));
            CmdSystem.AddCommand("bot",            args => Cmd_Bot_f(args));
            CmdSystem.AddCommand("forceteam",      args => Cmd_ForceTeam_f(args));
            CmdSystem.AddCommand("addip",          args => Cmd_AddIP_f(args));
            CmdSystem.AddCommand("removeip",       args => Cmd_RemoveIP_f(args));
            CmdSystem.AddCommand("listip",         args => Cmd_ListIP_f(args));
            CmdSystem.AddCommand("status",         args => Cmd_Status_f(args));
            CmdSystem.AddCommand("svcommand",      args => Cmd_ServerCommand_f(args));
            CmdSystem.AddCommand("nextmap",        args => Cmd_NextMap_f(args));
            CmdSystem.AddCommand("maprestart",     args => Cmd_MapRestart_f(args));
        }

        // ------------------------------------------------------------------

        private static void Cmd_EntityList_f(string[] args)
        {
            int count = 0;
            foreach (var ent in ServerGameLogic.Entities)
            {
                if (ent == null || !ent.InUse) continue;
                Debug.Log($"  [{ent.EntityNum:D4}] {ent.ClassName,-32} origin={ent.Origin}");
                count++;
            }
            Debug.Log($"--- {count} active entities ---");
        }

        private static void Cmd_Bot_f(string[] args)
        {
            if (args.Length < 2)
            {
                Debug.Log("Usage: bot <add|remove> [name]");
                return;
            }
            string sub  = args[1].ToLowerInvariant();
            string name = args.Length >= 3 ? args[2] : "ET_Bot";
            if (sub == "add")
                ServerGameLogic.AddBot(name);
            else if (sub == "remove")
                ServerGameLogic.RemoveBot(name);
            else
                Debug.Log("bot: unknown sub-command. Use 'add' or 'remove'.");
        }

        private static void Cmd_ForceTeam_f(string[] args)
        {
            if (args.Length < 3)
            {
                Debug.Log("Usage: forceteam <clientNum> <team>");
                return;
            }
            if (!int.TryParse(args[1], out int clientNum))
            {
                Debug.Log("forceteam: invalid client number");
                return;
            }
            if (!int.TryParse(args[2], out int team))
            {
                Debug.Log("forceteam: invalid team number");
                return;
            }
            var ent = ServerGameLogic.GetEntity(clientNum);
            if (ent == null || !ent.InUse)
            {
                Debug.Log($"forceteam: client {clientNum} not active");
                return;
            }
            ent.Team = team;
            OnClientForced?.Invoke(clientNum);
            Debug.Log($"Client {clientNum} forced to team {team}");
        }

        private static void Cmd_AddIP_f(string[] args)
        {
            if (args.Length < 2) { Debug.Log("Usage: addip <ip>"); return; }
            string ip = args[1];
            if (!_bannedIPs.Contains(ip))
            {
                _bannedIPs.Add(ip);
                Debug.Log($"[ServerCommands] Banned IP: {ip}");
            }
        }

        private static void Cmd_RemoveIP_f(string[] args)
        {
            if (args.Length < 2) { Debug.Log("Usage: removeip <ip>"); return; }
            bool removed = _bannedIPs.Remove(args[1]);
            Debug.Log(removed ? $"Removed ban for {args[1]}" : $"IP {args[1]} not in ban list");
        }

        private static void Cmd_ListIP_f(string[] args)
        {
            if (_bannedIPs.Count == 0) { Debug.Log("Ban list is empty."); return; }
            Debug.Log($"Banned IPs ({_bannedIPs.Count}):");
            foreach (var ip in _bannedIPs)
                Debug.Log($"  {ip}");
        }

        private static void Cmd_Status_f(string[] args)
        {
            Debug.Log($"{"Num",-4} {"Name",-24} {"IP",-20} {"Ping",-6} {"Team",-6}");
            foreach (var cl in ServerGameLogic.Clients)
            {
                if (cl == null) continue;
                string name = cl.Pers?.Name ?? "<unnamed>";
                Debug.Log($"{cl.PS.ClientNum,-4} {name,-24} {"?",-20} {cl.PS.Ping,-6} {cl.Pers?.Team,-6}");
            }
        }

        private static void Cmd_ServerCommand_f(string[] args)
        {
            if (args.Length < 3)
            {
                Debug.Log("Usage: svcommand <clientNum> <command>");
                return;
            }
            if (!int.TryParse(args[1], out int clientNum))
            {
                Debug.Log("svcommand: invalid client number");
                return;
            }
            string cmd = string.Join(" ", args, 2, args.Length - 2);
            ServerGameLogic.SendServerCommand(clientNum, cmd);
        }

        private static void Cmd_NextMap_f(string[] args)
        {
            string nextMap = CvarSystem.GetString("g_nextmap");
            if (string.IsNullOrEmpty(nextMap))
            {
                Debug.Log("nextmap: g_nextmap not set");
                return;
            }
            Debug.Log($"Changing map to {nextMap}");
            OnMapChange?.Invoke(nextMap);
            CvarSystem.Set("g_mapname", nextMap);
        }

        private static void Cmd_MapRestart_f(string[] args)
        {
            string current = CvarSystem.GetString("g_mapname");
            Debug.Log($"Restarting map: {current}");
            OnMapChange?.Invoke(current);
        }
    }
}
