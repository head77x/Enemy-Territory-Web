// Ported from: src/cgame/cg_servercmds.c, cg_playerstate.c, cg_spawn.c,
//              cg_flamethrower.c, cg_multiview.c
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
    // CGameServerCmds — cg_servercmds.c
    // =========================================================================

    public static class CGameServerCmds
    {
        public static event Action<string>   OnCenterPrint;
        public static event Action<string>   OnAnnouncement;
        public static event Action<int, int> OnTeamScores;
        public static event Action<int, int> OnObjectiveUpdate;
        public static event Action<int, int> OnRespawnCountdown;

        // CG_ExecuteNewServerCommands
        // Walk the reliable command ring up to latestSequence and dispatch each
        // command that CGS hasn't seen yet.
        public static void CG_ExecuteNewServerCommands(int latestSequence)
        {
            while (CGameMain.CGS.ServerCommandSequence < latestSequence)
            {
                CGameMain.CGS.ServerCommandSequence++;
                int  idx = CGameMain.CGS.ServerCommandSequence & (ClientConst.MAX_RELIABLE_COMMANDS - 1);
                string cmd = ClientMain.Clc.ServerCommands[idx];
                if (!string.IsNullOrEmpty(cmd))
                    CG_ParseServerCommand(cmd);
            }
        }

        public static void CG_ParseServerCommand(string cmd)
        {
            var args = CmdSystem.Tokenize(cmd);
            if (args.Length == 0) return;
            switch (args[0])
            {
                case "cs":              CG_CS(args);            break;
                case "print":           CG_Print(args);         break;
                case "cp":              CG_CenterPrint(args);   break;
                case "chat":            CG_Chat(args);          break;
                case "tchat":           CG_TeamChat(args);      break;
                case "scores":          CG_ParseScores(args);   break;
                case "map_restart":     CG_MapRestart();        break;
                case "remapShader":     CG_RemapShader(args);   break;
                case "spawnserver":     CG_SpawnServer(args);   break;
                case "loaddeferred":    CG_LoadDeferred();      break;
                case "disconnect":      CG_Disconnect();        break;
                case "wm_announce":     CG_WmAnnounce(args);   break;
                case "wm_teamscores":   CG_WmTeamScores(args); break;
                case "wm_objective":    CG_WmObjective(args);  break;
                case "wm_respawn":      CG_WmRespawn(args);    break;
                case "lockteamsprinting": CG_LockTeamSprinting(args); break;
            }
        }

        // "cs <num> <value>"
        private static void CG_CS(string[] args)
        {
            if (args.Length < 3) return;
            if (!int.TryParse(args[1], out int num)) return;
            string val = args[2];
            CGameMain.CG_ParseConfigString(num, val);
            ClientCGame.CL_SetConfigString(num, val);
        }

        // "print <text>"
        private static void CG_Print(string[] args)
        {
            if (args.Length < 2) return;
            Debug.Log("[Server] " + args[1]);
        }

        // "cp <text>"
        private static void CG_CenterPrint(string[] args)
        {
            if (args.Length < 2) return;
            OnCenterPrint?.Invoke(args[1]);
        }

        // "chat <text>"
        private static void CG_Chat(string[] args)
        {
            if (args.Length < 2) return;
            Debug.Log("[Chat] " + args[1]);
        }

        // "tchat <text>"
        private static void CG_TeamChat(string[] args)
        {
            if (args.Length < 2) return;
            Debug.Log("[TeamChat] " + args[1]);
        }

        // "scores <data...>"
        // ET sends variable-length score data; parse client num + score pairs.
        private static void CG_ParseScores(string[] args)
        {
            // args[1] = numScores, then pairs of (clientNum, score)
            if (args.Length < 2) return;
            if (!int.TryParse(args[1], out int count)) return;
            int idx = 2;
            for (int i = 0; i < count && idx + 1 < args.Length; i++, idx += 2)
            {
                if (int.TryParse(args[idx], out int clientNum) &&
                    int.TryParse(args[idx + 1], out int score))
                {
                    if (clientNum >= 0 && clientNum < GameConst.MAX_CLIENTS)
                        CGameMain.CGS.ClientInfo[clientNum] = score;
                }
            }
        }

        // "map_restart"
        private static void CG_MapRestart()
        {
            CGameMain.CGS.ServerCommandSequence = 0;
            CGameSpawn.CG_ParseSpawnpoints();
            Debug.Log("CG_MapRestart");
        }

        // "remapShader <old> <new> <timeOffset>"
        private static void CG_RemapShader(string[] args)
        {
            if (args.Length < 4) return;
            // Delegate to renderer when available; no-op here.
            Debug.Log($"CG_RemapShader: {args[1]} -> {args[2]} t={args[3]}");
        }

        // "spawnserver <mapname>"
        private static void CG_SpawnServer(string[] args)
        {
            string mapName = args.Length > 1 ? args[1] : "unknown";
            CGameMain.CGS.MapName = mapName;
            Debug.Log("CG_SpawnServer: " + mapName);
        }

        // "loaddeferred"
        private static void CG_LoadDeferred()
        {
            CGameFlamethrower.CG_RegisterFlamethrowerSounds();
        }

        // "disconnect"
        private static void CG_Disconnect()
        {
            ClientMain.CL_Disconnect("server disconnected");
        }

        // "wm_announce <text>"
        private static void CG_WmAnnounce(string[] args)
        {
            if (args.Length < 2) return;
            OnAnnouncement?.Invoke(args[1]);
        }

        // "wm_teamscores <axis> <allies>"
        private static void CG_WmTeamScores(string[] args)
        {
            if (args.Length < 3) return;
            int.TryParse(args[1], out int axis);
            int.TryParse(args[2], out int allies);
            CGameMain.CGS.Scores1 = axis;
            CGameMain.CGS.Scores2 = allies;
            OnTeamScores?.Invoke(axis, allies);
        }

        // "wm_objective <num> <status>"
        private static void CG_WmObjective(string[] args)
        {
            if (args.Length < 3) return;
            int.TryParse(args[1], out int objNum);
            int.TryParse(args[2], out int status);
            OnObjectiveUpdate?.Invoke(objNum, status);
        }

        // "wm_respawn <team> <time>"
        private static void CG_WmRespawn(string[] args)
        {
            if (args.Length < 3) return;
            int.TryParse(args[1], out int team);
            int.TryParse(args[2], out int msecs);
            OnRespawnCountdown?.Invoke(team, msecs);
        }

        // "lockteamsprinting"
        private static void CG_LockTeamSprinting(string[] args)
        {
            // Carries an optional 0/1 toggle; no cgame state to track yet.
            bool locked = args.Length > 1 && args[1] == "1";
            Debug.Log("CG_LockTeamSprinting: " + locked);
        }
    }

    // =========================================================================
    // CGamePlayerState — cg_playerstate.c
    // =========================================================================

    public static class CGamePlayerState
    {
        public static event Action              OnRespawn;
        public static event Action<int, float>  OnDamageIndicator;  // (directionDeg, intensity)
        public static event Action<bool>        OnLowAmmo;          // (isCritical)

        private static int _sndFlamethrower;
        private static int _sndLowAmmo;

        // CG_TransitionPlayerState
        // Called when a new snapshot arrives and the player state changed.
        public static void CG_TransitionPlayerState(PlayerState current, PlayerState previous)
        {
            // Respawn: spawn count incremented means player just respawned.
            if (current.Persistant[GameConst.PERS_SPAWN_COUNT] !=
                previous.Persistant[GameConst.PERS_SPAWN_COUNT])
            {
                CG_Respawn();
            }

            // Weapon change: play draw sound handled by weapons subsystem;
            // here we just track state for ammo checks.
            CG_CheckAmmo(current, previous);
            CG_CheckPlayerstateEvents(current, previous);
            CG_CheckLocalSounds(current, previous);
        }

        // CG_CheckLocalSounds
        // Trigger sounds based on stat/flag transitions.
        public static void CG_CheckLocalSounds(PlayerState ps, PlayerState ops)
        {
            // Health at 25 % — pain grunt
            int health = ps.Stats[GameConst.STAT_HEALTH];
            int oldHealth = ops.Stats[GameConst.STAT_HEALTH];

            if (health > 0 && oldHealth > 0)
            {
                if (health <= 25 && oldHealth > 25)
                    AudioSystem.S_StartLocalSound(AudioSystem.SND_PAIN_25, 0);
                else if (health <= 50 && oldHealth > 50)
                    AudioSystem.S_StartLocalSound(AudioSystem.SND_PAIN_50, 0);
            }

            // Transition into death
            if (health <= 0 && oldHealth > 0)
                AudioSystem.S_StartLocalSound(AudioSystem.SND_DEATH, 0);
        }

        // CG_CheckAmmo
        public static void CG_CheckAmmo(PlayerState ps, PlayerState ops)
        {
            if (ps.Weapon == GameConst.WP_NONE) return;
            int wpIdx = ps.Weapon;
            if (wpIdx < 0 || wpIdx >= GameConst.MAX_WEAPONS) return;

            int cur = ps.Ammo[wpIdx] + ps.AmmoClip[wpIdx];
            int old = ops.Ammo[wpIdx] + ops.AmmoClip[wpIdx];

            // Only warn on decrease to avoid spurious triggers.
            if (cur >= old) return;

            bool isCritical = cur <= 5;
            if (cur <= 10)
                OnLowAmmo?.Invoke(isCritical);
        }

        // CG_DamageFeedback
        // yawByte / pitchByte are 0-255 encoded angle bytes from the server.
        public static void CG_DamageFeedback(int yawByte, int pitchByte, int damage)
        {
            float yaw       = yawByte   * (360f / 255f);
            float intensity = Mathf.Clamp01(damage / 200f);
            OnDamageIndicator?.Invoke((int)yaw, intensity);
        }

        // CG_Respawn
        public static void CG_Respawn()
        {
            CGameMain.CG.ThisFrameTeleport = true;
            OnRespawn?.Invoke();
        }

        // CG_CheckPlayerstateEvents
        // Fire entity-style events stored inside the player state event queue.
        public static void CG_CheckPlayerstateEvents(PlayerState ps, PlayerState ops)
        {
            if (ps.EventSequence == ops.EventSequence) return;

            int newEvents = ps.EventSequence - ops.EventSequence;
            // Clamp: ET only stores last 4 events.
            if (newEvents > 4) newEvents = 4;

            for (int i = newEvents - 1; i >= 0; i--)
            {
                int evIdx = (ps.EventSequence - 1 - i) & 3;
                int ev    = ps.Events[evIdx];
                // Forward to the entity event system using the local client entity.
                Vector3 origin = new Vector3(-ps.Origin1, ps.Origin2, ps.Origin0); // ET→Unity
                CGameEntities.RaiseEntityEvent(ps.ClientNum, ev, origin);
            }
        }
    }

    // =========================================================================
    // SpawnPointInfo / CGameSpawn — cg_spawn.c
    // =========================================================================

    public class SpawnPointInfo
    {
        public int     EntityNum;
        public Vector3 Origin;
        public int     Team;
        public string  Name;
        public bool    Available;
        public bool    IsInitialSpawn;
    }

    public static class CGameSpawn
    {
        private static List<SpawnPointInfo> _spawnPoints = new List<SpawnPointInfo>();

        public static event Action<List<SpawnPointInfo>> OnSpawnpointsUpdated;

        // CG_ParseSpawnpoints
        // ET stores spawn points in configstrings starting at CS_SPAWNPOINTS (= 694).
        // Each configstring encodes: "origin_x origin_y origin_z team name".
        public static void CG_ParseSpawnpoints()
        {
            _spawnPoints.Clear();

            // CS_SPAWNPOINTS region: 694 .. 694+MAX_SPAWNS (32)
            const int CS_SPAWNPOINTS = 694;
            const int MAX_SPAWNS     = 32;

            for (int i = 0; i < MAX_SPAWNS; i++)
            {
                string cs = ClientCGame.CL_GetConfigString(CS_SPAWNPOINTS + i);
                if (string.IsNullOrEmpty(cs)) continue;

                var tokens = CmdSystem.Tokenize(cs);
                if (tokens.Length < 5) continue;

                float.TryParse(tokens[0], out float ex);
                float.TryParse(tokens[1], out float ey);
                float.TryParse(tokens[2], out float ez);
                int.TryParse(tokens[3], out int team);
                string name = tokens[4];
                bool available = tokens.Length > 5 && tokens[5] == "1";
                bool initial   = tokens.Length > 6 && tokens[6] == "1";

                // ET→Unity coordinate conversion
                var info = new SpawnPointInfo
                {
                    EntityNum      = i,
                    Origin         = new Vector3(-ey, ez, ex),
                    Team           = team,
                    Name           = name,
                    Available      = available,
                    IsInitialSpawn = initial,
                };
                _spawnPoints.Add(info);
            }

            OnSpawnpointsUpdated?.Invoke(_spawnPoints);
        }

        // CG_GetSpawnpoints
        public static List<SpawnPointInfo> CG_GetSpawnpoints(int team)
        {
            var result = new List<SpawnPointInfo>();
            foreach (var sp in _spawnPoints)
                if (sp.Team == team) result.Add(sp);
            return result;
        }

        // CG_FindSpawnPointForClient
        // Returns the first available initial spawn, then any available spawn.
        public static SpawnPointInfo CG_FindSpawnPointForClient(int team)
        {
            SpawnPointInfo fallback = null;
            foreach (var sp in _spawnPoints)
            {
                if (sp.Team != team || !sp.Available) continue;
                if (sp.IsInitialSpawn) return sp;
                fallback ??= sp;
            }
            return fallback;
        }

        // CG_DrawSpawnpoints
        // Raises the update event so the command-map overlay can refresh.
        public static void CG_DrawSpawnpoints()
        {
            OnSpawnpointsUpdated?.Invoke(_spawnPoints);
        }
    }

    // =========================================================================
    // FlameChunk / CGameFlamethrower — cg_flamethrower.c
    // =========================================================================

    public class FlameChunk
    {
        public Vector3 Origin;
        public Vector3 Velocity;
        public float   SpawnTime;
        public float   LifeTime;
        public float   Size;
        public bool    Active;
    }

    public static class CGameFlamethrower
    {
        public const int MAX_FLAME_CHUNKS = 256;

        private static readonly FlameChunk[] _chunks = new FlameChunk[MAX_FLAME_CHUNKS];
        private static ParticleSystem _flameParticles;

        private static int _sndFire;
        private static int _sndSmoke;

        // CG_InitFlamethrower
        public static void CG_InitFlamethrower()
        {
            for (int i = 0; i < MAX_FLAME_CHUNKS; i++)
                _chunks[i] = new FlameChunk();

            var go = new GameObject("[FlameParticles]");
            _flameParticles = go.AddComponent<ParticleSystem>();
            var main = _flameParticles.main;
            main.loop           = false;
            main.startLifetime  = 1.2f;
            main.startSpeed     = 0f;    // velocity managed manually
            main.startSize      = 0.5f;
            main.maxParticles   = MAX_FLAME_CHUNKS;

            var emission = _flameParticles.emission;
            emission.enabled = false;   // driven via CG_UpdateFlameChunks

            CG_RegisterFlamethrowerSounds();
        }

        // CG_AddFlameChunk
        // Returns the slot index, or -1 if the pool is full.
        public static int CG_AddFlameChunk(Vector3 origin, Vector3 velocity, float lifetime)
        {
            for (int i = 0; i < MAX_FLAME_CHUNKS; i++)
            {
                if (_chunks[i].Active) continue;
                _chunks[i].Origin    = origin;
                _chunks[i].Velocity  = velocity;
                _chunks[i].SpawnTime = Time.time;
                _chunks[i].LifeTime  = lifetime;
                _chunks[i].Size      = 0.4f + UnityEngine.Random.value * 0.4f;
                _chunks[i].Active    = true;
                return i;
            }
            return -1;
        }

        // CG_UpdateFlameChunks
        // Integrate, fade, and emit particles for each active chunk.
        public static void CG_UpdateFlameChunks()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < MAX_FLAME_CHUNKS; i++)
            {
                var c = _chunks[i];
                if (!c.Active) continue;

                float age = Time.time - c.SpawnTime;
                if (age >= c.LifeTime)
                {
                    c.Active = false;
                    continue;
                }

                c.Origin   += c.Velocity * dt;
                // Gravity drag: flames rise slightly
                c.Velocity  += Vector3.up * (2f * dt);

                if (_flameParticles != null)
                {
                    float alpha = 1f - (age / c.LifeTime);
                    var ep = new ParticleSystem.EmitParams
                    {
                        position       = c.Origin,
                        startSize      = c.Size * (0.8f + alpha * 0.4f),
                        startColor     = new Color(1f, 0.4f + alpha * 0.4f, 0f, alpha),
                        startLifetime  = 0.05f,
                    };
                    _flameParticles.Emit(ep, 1);
                }
            }
        }

        // CG_FlamethrowerSmoke
        // Rising smoke after the flame has expired.
        public static void CG_FlamethrowerSmoke(Vector3 origin)
        {
            if (_flameParticles == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position      = origin + Vector3.up * 0.2f,
                startSize     = 0.8f + UnityEngine.Random.value * 0.4f,
                startColor    = new Color(0.4f, 0.4f, 0.4f, 0.5f),
                startLifetime = 1.5f,
                velocity      = new Vector3(
                    (UnityEngine.Random.value - 0.5f) * 0.5f,
                    1.0f + UnityEngine.Random.value * 0.5f,
                    (UnityEngine.Random.value - 0.5f) * 0.5f),
            };
            _flameParticles.Emit(ep, 1);
        }

        // CG_RegisterFlamethrowerSounds
        public static void CG_RegisterFlamethrowerSounds()
        {
            _sndFire  = AudioSystem.S_RegisterSound("sound/weapons/flamethrower/fire.wav", false);
            _sndSmoke = AudioSystem.S_RegisterSound("sound/weapons/flamethrower/smoke.wav", false);
        }
    }

    // =========================================================================
    // MultiviewTarget / CGameMultiview — cg_multiview.c
    // =========================================================================

    public class MultiviewTarget
    {
        public int    ClientNum;
        public string PlayerName;
        public Rect   ViewRect;
        public bool   Active;
    }

    public static class CGameMultiview
    {
        public const int MAX_MV_VIEWS = 4;

        private static readonly MultiviewTarget[] _views =
            new MultiviewTarget[MAX_MV_VIEWS];
        private static bool _multiviewActive = false;

        public static event Action<MultiviewTarget[]> OnMultiviewDraw;

        static CGameMultiview()
        {
            for (int i = 0; i < MAX_MV_VIEWS; i++)
                _views[i] = new MultiviewTarget();
        }

        // CG_mvCreate
        // Returns the slot index, or -1 if no free slot.
        public static int CG_mvCreate(int clientNum)
        {
            // Refuse duplicate
            for (int i = 0; i < MAX_MV_VIEWS; i++)
                if (_views[i].Active && _views[i].ClientNum == clientNum) return i;

            for (int i = 0; i < MAX_MV_VIEWS; i++)
            {
                if (_views[i].Active) continue;
                _views[i].ClientNum  = clientNum;
                _views[i].PlayerName = $"Player{clientNum}";
                _views[i].Active     = true;
                CG_RecalcViewRects();
                return i;
            }
            return -1;
        }

        // CG_mvRemove
        public static void CG_mvRemove(int clientNum)
        {
            for (int i = 0; i < MAX_MV_VIEWS; i++)
            {
                if (!_views[i].Active || _views[i].ClientNum != clientNum) continue;
                _views[i].Active = false;
                CG_RecalcViewRects();
                return;
            }
        }

        // CG_mvUpdateClientList
        // Refreshes player names from current CGS client info strings.
        public static void CG_mvUpdateClientList()
        {
            for (int i = 0; i < MAX_MV_VIEWS; i++)
            {
                if (!_views[i].Active) continue;
                int cn = _views[i].ClientNum;
                // CS_PLAYERS starts at CS offset 32
                string cs = ClientCGame.CL_GetConfigString(32 + cn);
                if (!string.IsNullOrEmpty(cs))
                {
                    var tokens = CmdSystem.Tokenize(cs);
                    // InfoString "key\value\key\value" — find "n" key
                    for (int t = 0; t + 1 < tokens.Length; t += 2)
                        if (tokens[t] == "n") { _views[i].PlayerName = tokens[t + 1]; break; }
                }
            }
        }

        // CG_mvDraw
        public static void CG_mvDraw()
        {
            if (!_multiviewActive) return;
            OnMultiviewDraw?.Invoke(_views);
        }

        public static bool CG_mvIsActive() => _multiviewActive;

        // CG_mvToggle
        public static void CG_mvToggle()
        {
            _multiviewActive = !_multiviewActive;
        }

        // Divide the screen into equal tiles for each active view.
        private static void CG_RecalcViewRects()
        {
            int count = 0;
            for (int i = 0; i < MAX_MV_VIEWS; i++)
                if (_views[i].Active) count++;
            if (count == 0) return;

            // Simple horizontal split; a real implementation would use 2×2 for 4 views.
            float w = 1f / count;
            int slot = 0;
            for (int i = 0; i < MAX_MV_VIEWS; i++)
            {
                if (!_views[i].Active) continue;
                _views[i].ViewRect = new Rect(slot * w, 0f, w, 1f);
                slot++;
            }
        }
    }
}
