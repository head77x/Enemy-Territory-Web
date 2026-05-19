// Ports: g_buddy_list.c + g_sv_entities.c + g_systemmsg.c

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Game;

namespace ET.Server
{
    // -------------------------------------------------------------------------
    // Waypoint entity (g_buddy_list.c)
    // ET waypoints are ET_WAYPOINT entities visible to all players. They are
    // spawned server-side and replicated via EntityState. In Unity the visual
    // is drawn by a subscriber to OnWaypointSpawned.
    // -------------------------------------------------------------------------
    public enum WayPointType
    {
        None = 0,
        Default,
        MG42,
        Mortar,
        NUM_WAYPOINTTYPES
    }

    public static class WaypointSystem
    {
        private const int WAYPOINTSET_POSTDELAY_MS = 3000;

        // G_SetWayPoint — spawns or updates a player's personal waypoint entity
        public static void SetWayPoint(GEntity owner, WayPointType type, Vector3 loc)
        {
            if (owner?.Client == null) return;
            if (type == WayPointType.None || type >= WayPointType.NUM_WAYPOINTTYPES)
            {
                Debug.LogWarning($"G_SetWayPoint: bad waypoint type {type}");
                return;
            }

            var client = owner.Client;
            var level  = ServerGameLogic.Level;

            // Debounce: can only set once per 3 seconds
            if (client.Pers.WayPoint != null &&
                level.Time - client.Pers.LastWaypointSetTime < WAYPOINTSET_POSTDELAY_MS)
                return;

            // Reuse existing entity or spawn a new one
            var wp = client.Pers.WayPoint ?? ServerGameLogic.G_Spawn();
            client.Pers.WayPoint = wp;
            client.Pers.LastWaypointSetTime = level.Time;

            wp.S.EType      = GameConstants.ET_WAYPOINT;
            wp.S.Pos.TrType = GameConstants.TR_STATIONARY;
            wp.S.Pos.TrBase = loc;
            wp.S.Frame      = (int)type;
            wp.S.ClientNum  = owner.EntityNum;
            wp.S.OtherEntityNum = owner.EntityNum;
            wp.Flags        |= GameConstants.SVF_BROADCAST;
            wp.ClassName    = "waypoint";

            ServerGameLogic.G_SetOrigin(wp, loc);
        }

        // G_ClearWaypoints — removes a player's waypoint on disconnect/death
        public static void ClearWayPoint(GEntity owner)
        {
            if (owner?.Client?.Pers.WayPoint == null) return;
            ServerGameLogic.G_FreeEntity(owner.Client.Pers.WayPoint);
            owner.Client.Pers.WayPoint = null;
        }
    }

    // -------------------------------------------------------------------------
    // ServerEntity — g_sv_entities.c
    // Lightweight position-only entities used as AI markers and seek-cover spots.
    // In ET SP these lived in a separate pool to avoid consuming MAX_GENTITIES
    // slots. In multiplayer ET they exist but CreateMapServerEntities() is empty.
    // -------------------------------------------------------------------------
    public class ServerEntity
    {
        public int     Number;
        public bool    InUse;
        public string  ClassName = "";
        public string  Name      = "";   // targetname
        public string  Target    = "";
        public int     SpawnFlags;
        public int     Team;
        public Vector3 Origin;
        public Vector3 Angles;
        public int     AreaNum  = -1;   // AAS area; -1 = uncalculated

        public Action<ServerEntity> Setup;
    }

    public static class ServerEntityPool
    {
        public const int MAX_SERVER_ENTITIES = 4096;
        private static readonly ServerEntity[] _pool = new ServerEntity[MAX_SERVER_ENTITIES];
        private static int _count;

        // InitServerEntities
        public static void Init()
        {
            for (int i = 0; i < _count; i++) _pool[i] = null;
            _count = 0;
        }

        // GetServerEntity — by number (numbers start at MAX_GENTITIES = 1024)
        public static ServerEntity Get(int num)
        {
            int idx = num - GameConstants.MAX_GENTITIES;
            if (idx < 0 || idx >= _count) return null;
            return _pool[idx];
        }

        // GetFreeServerEntity
        public static ServerEntity Alloc()
        {
            if (_count >= MAX_SERVER_ENTITIES)
                throw new InvalidOperationException("GetFreeServerEntity: pool exhausted");
            var e = new ServerEntity
            {
                Number = GameConstants.MAX_GENTITIES + _count,
                InUse  = true,
                AreaNum = -1
            };
            _pool[_count++] = e;
            return e;
        }

        // CreateServerEntity — from an existing GEntity (map spawn)
        public static ServerEntity CreateFromEntity(GEntity ent)
        {
            var se = Alloc();
            se.ClassName  = ent.ClassName ?? "";
            se.Name       = ent.TargetName ?? "";
            se.Target     = ent.TargetName ?? "";
            se.SpawnFlags = ent.SpawnFlags;
            se.Team       = ent.Team;
            se.Origin     = ent.Origin;
            se.Angles     = ent.Angles;
            return se;
        }

        // CreateFromData — from raw spawn vars
        public static ServerEntity CreateFromData(
            string className, string targetName, string target,
            Vector3 origin, int spawnFlags, Vector3 angles)
        {
            var se = Alloc();
            se.ClassName  = className ?? "";
            se.Name       = targetName ?? "";
            se.Target     = target ?? "";
            se.SpawnFlags = spawnFlags;
            se.Origin     = origin;
            se.Angles     = angles;
            SetupFunctionForClass(se);
            return se;
        }

        // FindServerEntity — linear search by field value (like G_Find)
        public static ServerEntity Find(ServerEntity from, Func<ServerEntity, string> field, string match)
        {
            int start = from != null ? Array.IndexOf(_pool, from) + 1 : 0;
            for (int i = start; i < _count; i++)
            {
                if (_pool[i] == null || !_pool[i].InUse) continue;
                if (string.Equals(field(_pool[i]), match, StringComparison.OrdinalIgnoreCase))
                    return _pool[i];
            }
            return null;
        }

        // InitialServerEntitySetup — called after all map entities are created
        public static void InitialSetup()
        {
            CreateMapServerEntities();
            for (int i = 0; i < _count; i++)
            {
                var e = _pool[i];
                if (e != null && e.InUse) e.Setup?.Invoke(e);
            }
        }

        private static void SetupFunctionForClass(ServerEntity e)
        {
            switch (e.ClassName)
            {
                case "ai_marker":
                    e.Setup = AIMarkerSetup;
                    break;
                case "bot_seek_cover_spot":
                    e.Team  = GameConstants.TEAM_ALLIES;
                    e.Setup = SeekCoverSetup;
                    break;
                case "bot_axis_seek_cover_spot":
                    e.Team  = GameConstants.TEAM_AXIS;
                    e.Setup = SeekCoverSetup;
                    break;
            }
        }

        // Minimal setup stubs — bot nav data handled by Unity NavMesh in BotAI layer
        private static void AIMarkerSetup(ServerEntity e)   { /* NavMesh handles AI markers */ }
        private static void SeekCoverSetup(ServerEntity e)  { /* NavMesh handles cover spots */ }

        // CreateMapServerEntities — ET multiplayer has this as an empty stub
        private static void CreateMapServerEntities()
        {
            // ET multiplayer maps do not use separate server entity files.
            // SP bot cover spots would be loaded from map-specific .ai files here.
        }
    }

    // -------------------------------------------------------------------------
    // SystemMessages — g_systemmsg.c
    // Voice/chat system messages broadcast to a specific team.
    // -------------------------------------------------------------------------
    public enum SysMsg
    {
        NeedMedic      = 0,
        NeedEngineer   = 1,
        NeedLT         = 2,
        NeedCovertOps  = 3,
        LostMen        = 4,
        ObjCaptured    = 5,
        ObjLost        = 6,
        ObjDestroyed   = 7,
        ConstructComplete = 8,
        ConstructFailed   = 9,
        Destroyed         = 10,
        NUM_SYS_MSGS
    }

    public static class SystemMessageSystem
    {
        private const int SYS_MSG_COOLDOWN_MS = 15000;

        private static readonly string[] _messageNames =
        {
            "SYS_NeedMedic",
            "SYS_NeedEngineer",
            "SYS_NeedLT",
            "SYS_NeedCovertOps",
            "SYS_MenDown",
            "SYS_ObjCaptured",
            "SYS_ObjLost",
            "SYS_ObjDestroyed",
            "SYS_ConstructComplete",
            "SYS_ConstructFailed",
            "SYS_Destroyed",
        };

        // Per-team cooldown timers [0=axis, 1=allies]
        private static readonly int[] _lastMsgTime = new int[2];

        // G_GetSysMessageNumber
        public static int GetMessageNumber(string sysMsg)
        {
            for (int i = 0; i < _messageNames.Length; i++)
                if (string.Equals(_messageNames[i], sysMsg, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        // G_SendSystemMessage — sends a vschat command to all players on the team
        public static void Send(SysMsg message, int team)
        {
            var level = ServerGameLogic.Level;
            int ti = team == GameConstants.TEAM_AXIS ? 0 : 1;

            if (_lastMsgTime[ti] != 0 &&
                level.Time - _lastMsgTime[ti] < SYS_MSG_COOLDOWN_MS)
                return;

            _lastMsgTime[ti] = level.Time;

            string msgName = _messageNames[(int)message];
            for (int j = 0; j < GameConstants.MAX_CLIENTS; j++)
            {
                var ent = ServerGameLogic.Entities[j];
                if (ent?.Client == null || !ent.InUse) continue;
                if (ent.Client.Sess.SessionTeam != team) continue;
                ServerGameLogic.SendServerCommand(j,
                    $"vschat 0 {j} 3 {msgName} 0 0 0");
            }
        }

        // G_NeedEngineers — scans for constructible/explosive/tank indicator entities
        public static bool NeedEngineers(int team)
        {
            var level = ServerGameLogic.Level;
            for (int i = GameConstants.MAX_CLIENTS; i < level.NumEntities; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e == null || !e.InUse) continue;
                int eType = e.S.EType;
                if (eType == GameConstants.ET_CONSTRUCTIBLE_INDICATOR ||
                    eType == GameConstants.ET_EXPLOSIVE_INDICATOR     ||
                    eType == GameConstants.ET_TANK_INDICATOR)
                {
                    int tn = e.S.TeamNum;
                    if (tn == 3) return true;
                    if (team == GameConstants.TEAM_AXIS  && tn == 2) return true;
                    if (team == GameConstants.TEAM_ALLIES && tn == 1) return true;
                }
            }
            return false;
        }

        // G_CheckForNeededClasses — periodic check, sends voice messages if class is missing
        private static int _lastClassCheck;

        public static void CheckForNeededClasses()
        {
            var level = ServerGameLogic.Level;
            if (_lastClassCheck != 0 && level.Time - _lastClassCheck < 60000) return;
            _lastClassCheck = level.Time;

            // hasClass[classIndex][0=axis,1=allies]
            bool[,] hasClass   = new bool[5, 2];
            int[]   teamCounts = new int[2];

            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e?.Client == null || !e.InUse) continue;
                int t = e.Client.Sess.SessionTeam;
                if (t != GameConstants.TEAM_AXIS && t != GameConstants.TEAM_ALLIES) continue;
                int ti = t == GameConstants.TEAM_AXIS ? 0 : 1;
                teamCounts[ti]++;
                int cls = e.Client.Sess.PlayerType;
                if (cls > 0 && cls < 5) hasClass[cls, ti] = true;
            }

            for (int ti = 0; ti < 2; ti++)
            {
                if (teamCounts[ti] <= 3) continue;
                int team = ti == 0 ? GameConstants.TEAM_AXIS : GameConstants.TEAM_ALLIES;

                // Engineer check
                if (!hasClass[1, ti] && !NeedEngineers(team))
                    hasClass[1, ti] = true;

                // Find missing class and send a random one
                var missing = new List<int>();
                for (int cls = 0; cls < 5; cls++)
                    if (!hasClass[cls, ti]) missing.Add(cls);

                if (missing.Count > 0)
                {
                    int pick = missing[UnityEngine.Random.Range(0, missing.Count)];
                    Send((SysMsg)(SysMsg.NeedMedic + pick), team);
                }
            }
        }

        // G_CheckMenDown — sends SM_LOST_MEN if ≥75% of team is dead
        public static void CheckMenDown()
        {
            int[] alive = new int[2], dead = new int[2];
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e?.Client == null || !e.InUse) continue;
                int t = e.Client.Sess.SessionTeam;
                if (t != GameConstants.TEAM_AXIS && t != GameConstants.TEAM_ALLIES) continue;
                int ti = t == GameConstants.TEAM_AXIS ? 0 : 1;
                if (e.Health <= 0) dead[ti]++; else alive[ti]++;
            }

            for (int ti = 0; ti < 2; ti++)
            {
                int total = dead[ti] + alive[ti];
                if (total >= 4 && dead[ti] >= total * 0.75f)
                    Send(SysMsg.LostMen, ti == 0 ? GameConstants.TEAM_AXIS : GameConstants.TEAM_ALLIES);
            }
        }
    }
}
