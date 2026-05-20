// Ports: ai_team.c + ai_cmd.c + ai_script.c + ai_script_actions.c
// Bot team coordination, voice command parsing, and bot scripting system.
// NavMesh/AAS path-finding is handled by Unity NavMesh in BotMain.cs + PathFinding.cs.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ET.Core;
using ET.Game;
using ET.Server;

namespace ET.BotAI
{
    // =========================================================================
    // BotScriptEvent — events that trigger bot script execution
    // =========================================================================
    public enum BotScriptEventType
    {
        Spawn = 0,
        Death,
        Pain,
        Trigger,
        Activate,
    }

    // =========================================================================
    // BotScriptAction — a single action in a bot script
    // =========================================================================
    public class BotScriptAction
    {
        public string Name   = "";
        public string Params = "";
        public BotScriptAction Next;
    }

    // =========================================================================
    // BotScript — loaded script for a bot
    // =========================================================================
    public class BotScript
    {
        public string Name = "";
        public BotScriptAction[] Events = new BotScriptAction[(int)BotScriptEventType.Trigger + 1];
        public BotScriptAction   CurrentAction;
        public int               WaitTime;
        public int[]             Accum       = new int[8];
        public bool              NoTarget;
        public bool              ResetScript;
    }

    // =========================================================================
    // BotScriptSystem — ai_script.c + ai_script_actions.c
    // =========================================================================
    public static class BotScriptSystem
    {
        private const int MAX_BOT_SCRIPTS = 64;
        private static readonly BotScript[] _scripts = new BotScript[MAX_BOT_SCRIPTS];
        private static int _numScripts;

        // Bot_LoadScripts — loads all .script files from scripts/bots/
        public static void LoadScripts()
        {
            string[] files = FileSystem.FS_GetFileList("scripts/bots", ".script");
            foreach (string f in files)
            {
                string text = FileSystem.ReadAllText($"scripts/bots/{f}");
                if (!string.IsNullOrEmpty(text))
                    ParseScript(f.Replace(".script", ""), text);
            }
        }

        private static void ParseScript(string name, string text)
        {
            if (_numScripts >= MAX_BOT_SCRIPTS) return;
            var script = new BotScript { Name = name };
            _scripts[_numScripts++] = script;

            var tokens = text.Split(new[]{'\n','\r'}, StringSplitOptions.RemoveEmptyEntries);
            BotScriptAction currentChain = null;
            int eventIdx = -1;

            foreach (string rawLine in tokens)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

                string[] parts = line.Split(new[]{' ','\t'}, 2,
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                string cmd = parts[0].ToLower();
                string prm = parts.Length > 1 ? parts[1] : "";

                switch (cmd)
                {
                    case "spawn":    eventIdx = (int)BotScriptEventType.Spawn;    currentChain = null; break;
                    case "death":    eventIdx = (int)BotScriptEventType.Death;    currentChain = null; break;
                    case "pain":     eventIdx = (int)BotScriptEventType.Pain;     currentChain = null; break;
                    case "trigger":  eventIdx = (int)BotScriptEventType.Trigger;  currentChain = null; break;
                    case "activate": eventIdx = (int)BotScriptEventType.Activate; currentChain = null; break;
                    default:
                        if (eventIdx >= 0)
                        {
                            var action = new BotScriptAction { Name = cmd, Params = prm };
                            if (script.Events[eventIdx] == null)
                            { script.Events[eventIdx] = action; currentChain = action; }
                            else
                            { currentChain!.Next = action; currentChain = action; }
                        }
                        break;
                }
            }
        }

        public static BotScript FindScript(string name)
        {
            for (int i = 0; i < _numScripts; i++)
                if (string.Equals(_scripts[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _scripts[i];
            return null;
        }

        // Bot_ScriptRun — executes the current event chain for a bot
        public static bool Run(BotState bs, int cgTime)
        {
            var script = bs.Script;
            if (script == null || script.CurrentAction == null) return false;

            if (script.WaitTime > cgTime) return true;   // still waiting

            var action = script.CurrentAction;
            bool done  = ExecuteAction(bs, action.Name, action.Params, cgTime);
            if (done)
                script.CurrentAction = action.Next;

            return script.CurrentAction != null;
        }

        // Bot_ScriptEvent — starts executing a script event
        public static void StartEvent(BotState bs, BotScriptEventType ev, string trigger)
        {
            if (bs.Script == null) return;
            int idx = (int)ev;
            if (idx < bs.Script.Events.Length)
                bs.Script.CurrentAction = bs.Script.Events[idx];
        }

        // ------------------------------------------------------------------ //
        // Action implementations (Bot_ScriptAction_*)
        // ------------------------------------------------------------------ //

        private static bool ExecuteAction(BotState bs, string action, string prm, int cgTime)
        {
            switch (action.ToLower())
            {
                case "print":
                    Debug.Log($"[Bot {bs.ClientNum}] {prm}");
                    return true;

                case "wait":
                    int ms = 0;
                    int.TryParse(prm, out ms);
                    bs.Script.WaitTime = cgTime + ms;
                    return true;

                case "accum":
                {
                    string[] p = prm.Split(' ');
                    if (p.Length >= 3 && int.TryParse(p[0], out int idx)
                        && idx >= 0 && idx < 8)
                    {
                        switch (p[1].ToLower())
                        {
                            case "set":   if (int.TryParse(p[2], out int sv)) bs.Script.Accum[idx] = sv; break;
                            case "inc":   bs.Script.Accum[idx]++; break;
                            case "dec":   bs.Script.Accum[idx]--; break;
                            case "abs":   bs.Script.Accum[idx] = Mathf.Abs(bs.Script.Accum[idx]); break;
                        }
                    }
                    return true;
                }

                case "if":
                {
                    string[] p = prm.Split(' ');
                    if (p.Length < 3) return true;
                    if (!int.TryParse(p[0], out int idx) || idx < 0 || idx >= 8) return true;
                    int val = 0; int.TryParse(p[2], out val);
                    bool cond = p[1].ToLower() switch
                    {
                        "==" => bs.Script.Accum[idx] == val,
                        "!=" => bs.Script.Accum[idx] != val,
                        ">"  => bs.Script.Accum[idx] >  val,
                        "<"  => bs.Script.Accum[idx] <  val,
                        _    => false
                    };
                    if (!cond) bs.Script.CurrentAction = null;   // skip rest
                    return true;
                }

                case "setweapon":
                    if (int.TryParse(prm, out int wpn))
                        bs.DesiredWeapon = wpn;
                    return true;

                case "setclass":
                    if (int.TryParse(prm, out int cls))
                        bs.DesiredClass = cls;
                    return true;

                case "notarget":
                    bs.Script.NoTarget = string.Equals(prm, "on", StringComparison.OrdinalIgnoreCase);
                    return true;

                case "resetscript":
                    bs.Script.CurrentAction = null;
                    return true;

                case "movetomarker":
                    MoveToMarker(bs, prm);
                    return true;

                case "followleader":
                    bs.FollowLeader = string.Equals(prm, "true", StringComparison.OrdinalIgnoreCase);
                    return true;

                default:
                    return true;   // unknown action — skip
            }
        }

        private static void MoveToMarker(BotState bs, string markerName)
        {
            var marker = ServerEntityPool.Find(null, e => e.Name, markerName);
            if (marker != null && bs.Agent != null)
                bs.Agent.SetDestination(marker.Origin);
        }
    }

    // =========================================================================
    // BotTeamAI — ai_team.c
    // High-level team goal coordination: objective targeting, flag logic,
    // construction/destruction decisions, spawn wave analysis.
    // =========================================================================
    public static class BotTeamAI
    {
        // BotNumTeamMates — counts bots on same team
        public static int NumTeamMates(BotState bs, List<int> outList, int maxList)
        {
            outList.Clear();
            var level = ServerGameLogic.Level;
            for (int i = 0; i < GameConstants.MAX_CLIENTS && outList.Count < maxList; i++)
            {
                var cl = level.Clients[i];
                if (cl == null || i == bs.ClientNum) continue;
                if (cl.Sess.SessionTeam == bs.Team)
                    outList.Add(i);
            }
            return outList.Count;
        }

        // BotNumTeamMatesWithTarget — teammates attacking the same target
        public static int NumTeamMatesWithTarget(BotState bs, int targetEnt,
            List<int> outList, int maxList)
        {
            outList.Clear();
            var level = ServerGameLogic.Level;
            for (int i = 0; i < GameConstants.MAX_CLIENTS && outList.Count < maxList; i++)
            {
                if (i == bs.ClientNum) continue;
                var cl = level.Clients[i];
                if (cl == null || cl.Sess.SessionTeam != bs.Team) continue;
                var other = BotMain.GetBotState(i);
                if (other != null && other.CurrentTarget == targetEnt)
                    outList.Add(i);
            }
            return outList.Count;
        }

        // BotValidTeamLeader
        public static bool ValidTeamLeader(BotState bs)
        {
            var level = ServerGameLogic.Level;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var cl = level.Clients[i];
                if (cl == null || cl.Sess.SessionTeam != bs.Team) continue;
                // The lowest-numbered real human player is the leader
                if (!ServerBotIntegration.IsBot(i)) return i == bs.ClientNum;
            }
            return false;
        }

        // BotSortTeamMatesByBaseTravelTime — orders by NavMesh distance to base
        public static List<int> SortTeamMatesByTravelTime(BotState bs, List<int> teammates)
        {
            var level = ServerGameLogic.Level;
            var sorted = new List<(int num, float dist)>();
            foreach (int t in teammates)
            {
                var cl = level.Clients[t];
                if (cl == null) continue;
                float dist = Vector3.Distance(
                    ServerGameLogic.Entities[t]?.Origin ?? Vector3.zero,
                    bs.BasePosition);
                sorted.Add((t, dist));
            }
            sorted.Sort((a, b) => a.dist.CompareTo(b.dist));
            var result = new List<int>(sorted.Count);
            foreach (var (num, _) in sorted) result.Add(num);
            return result;
        }

        // BotGetTeamFlagCarrier — finds the enemy flag carrier
        public static int GetTeamFlagCarrier(BotState bs)
        {
            var level = ServerGameLogic.Level;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var ent = ServerGameLogic.Entities[i];
                if (ent?.Client == null || !ent.InUse) continue;
                if (ent.Client.Sess.SessionTeam == bs.Team) continue;
                if (ent.Client.PS.Powerups[GameConstants.PW_REDFLAG] != 0 ||
                    ent.Client.PS.Powerups[GameConstants.PW_BLUEFLAG] != 0)
                    return i;
            }
            return -1;
        }

        // G_ConstructionBegun / IsFullyBuilt / IsPartlyBuilt / IsDestroyable
        public static bool ConstructionBegun(GEntity ent)
            => ent != null && ent.S.Frame > 0;

        public static bool ConstructionFullyBuilt(GEntity ent)
            => ent != null && ent.S.Frame >= ent.S.Angles22;

        public static bool ConstructionPartlyBuilt(GEntity ent)
            => ent != null && ent.S.Frame > 0 && ent.S.Frame < ent.S.Angles22;

        public static bool ConstructionDestroyable(GEntity ent)
            => ent != null && ent.S.Frame > 0;

        // BotGetTargetExplosives — finds dynamite/satchels targeting a team
        public static List<int> GetTargetExplosives(int team, bool ignoreDynamite)
        {
            var result = new List<int>();
            var level  = ServerGameLogic.Level;
            for (int i = 0; i < level.NumEntities; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e == null || !e.InUse) continue;
                if (e.Team == team) continue;
                if (!ignoreDynamite &&
                    (e.S.Weapon == GameConstants.WP_DYNAMITE ||
                     e.S.Weapon == GameConstants.WP_SATCHELCHARGE))
                    result.Add(i);
            }
            return result;
        }

        // BotClientTravelTimeToGoal — NavMesh path cost
        public static float ClientTravelTime(int clientNum, Vector3 goal)
        {
            var ent = ServerGameLogic.Entities[clientNum];
            if (ent == null) return float.MaxValue;
            var navPath = new NavMeshPath();
            bool ok = NavMesh.CalculatePath(ent.Origin, goal, NavMesh.AllAreas, navPath);
            if (!ok || navPath.status == NavMeshPathStatus.PathInvalid) return float.MaxValue;
            float dist = 0f;
            for (int i = 1; i < navPath.corners.Length; i++)
                dist += Vector3.Distance(navPath.corners[i-1], navPath.corners[i]);
            return dist;
        }

        // BotFindSparseDefendArea — finds a NavMesh point near target without many bots
        public static bool FindSparseDefendArea(BotState bs, Vector3 target, out Vector3 pos)
        {
            pos = target;
            // Try several candidate points around the target at decreasing density
            float bestScore = float.MaxValue;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float angle = attempt * (360f / 8f) * Mathf.Deg2Rad;
                float r     = 150f + attempt * 50f;
                Vector3 candidate = target + new Vector3(Mathf.Cos(angle) * r, 0,
                                                          Mathf.Sin(angle) * r);
                if (!NavMesh.SamplePosition(candidate, out var hit, 200f, NavMesh.AllAreas))
                    continue;

                // Count bots near this candidate
                int nearby = 0;
                foreach (var other in BotMain.AllBotStates)
                {
                    if (other == bs || other.Team != bs.Team) continue;
                    if (Vector3.Distance(other.Position, hit.position) < 200f) nearby++;
                }
                float score = nearby + Vector3.Distance(hit.position, target) * 0.01f;
                if (score < bestScore) { bestScore = score; pos = hit.position; }
            }
            return bestScore < float.MaxValue;
        }
    }

    // =========================================================================
    // BotCommands — ai_cmd.c
    // Parses player voice commands and chat messages directed at bots.
    // =========================================================================
    public static class BotCommands
    {
        // BotAddressedToBot — checks if a voice/chat message targets this bot
        public static bool AddressedToBot(BotState bs, string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            // Check if message contains bot name or team reference
            string lower = message.ToLower();
            string botName = bs.Name.ToLower();
            return lower.Contains(botName) ||
                   lower.Contains("all") ||
                   (bs.Team == GameConstants.TEAM_ALLIES && lower.Contains("allies")) ||
                   (bs.Team == GameConstants.TEAM_AXIS   && lower.Contains("axis"));
        }

        // BotPrintTeamGoal — sends current goal to team chat
        public static void PrintTeamGoal(BotState bs)
        {
            if (bs.CurrentGoalDescription.Length > 0)
                Debug.Log($"[Bot {bs.Name}] Goal: {bs.CurrentGoalDescription}");
        }

        // BotMatch_HelpAccompany — follow a player
        public static void MatchHelpAccompany(BotState bs, int targetClient)
        {
            bs.FollowTarget = targetClient;
            bs.CurrentGoalDescription = $"Accompanying player {targetClient}";
        }

        // BotMatch_Camp — hold a position
        public static void MatchCamp(BotState bs, Vector3 campPos)
        {
            bs.CampPosition = campPos;
            bs.IsCamping    = true;
            bs.CurrentGoalDescription = $"Camping at {campPos}";
        }

        // BotMatch_Patrol — move between waypoints
        public static void MatchPatrol(BotState bs, List<Vector3> waypoints)
        {
            bs.PatrolWaypoints = waypoints;
            bs.PatrolIndex     = 0;
            bs.IsPatrolling    = true;
            bs.CurrentGoalDescription = $"Patrolling ({waypoints.Count} waypoints)";
        }

        // BotMatch_GetItem — move to pick up an item
        public static void MatchGetItem(BotState bs, Vector3 itemPos, string itemName)
        {
            bs.TargetPosition           = itemPos;
            bs.CurrentGoalDescription   = $"Getting {itemName}";
            if (bs.Agent != null) bs.Agent.SetDestination(itemPos);
        }

        // BotMatch_RushBase — attack enemy base
        public static void MatchRushBase(BotState bs)
        {
            bs.IsRushing    = true;
            bs.FollowTarget = -1;
            bs.CurrentGoalDescription = "Rushing enemy base";
        }

        // NumPlayersOnSameTeam
        public static int NumPlayersOnSameTeam(BotState bs)
        {
            int cnt = 0;
            var level = ServerGameLogic.Level;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var cl = level.Clients[i];
                if (cl != null && cl.Sess.SessionTeam == bs.Team) cnt++;
            }
            return cnt;
        }

        // FindClientByName
        public static int FindClientByName(string name)
        {
            var level = ServerGameLogic.Level;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var cl = level.Clients[i];
                if (cl != null && string.Equals(cl.Pers.NetName, name,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        // BotGPSToPosition — parses "x y z" string to Vector3
        public static bool GPSToPosition(string buf, out Vector3 pos)
        {
            pos = Vector3.zero;
            string[] parts = buf.Split(' ');
            if (parts.Length < 3) return false;
            if (float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float y) &&
                float.TryParse(parts[2], out float z))
            { pos = new Vector3(x, y, z); return true; }
            return false;
        }
    }
}
