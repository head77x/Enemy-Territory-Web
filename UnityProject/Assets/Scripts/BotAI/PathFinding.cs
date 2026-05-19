// Ported from: src/botlib/be_aas_move.c, be_aas_route.c, be_aas_reach.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// AAS pathfinding replaced by Unity NavMesh. The public API mirrors the
// original AAS functions so the rest of the bot code can call them without
// knowing they are backed by Unity's navigation system.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ET.BotAI
{
    // Area flags (AAS area_t flags — kept for compatibility)
    public static class AasAreaFlags
    {
        public const int AREA_GROUNDED = 1;   // can walk here
        public const int AREA_LADDER   = 2;   // has a ladder
        public const int AREA_SWIM     = 4;   // underwater
        public const int AREA_JUMP     = 8;   // requires a jump
        public const int AREA_CROUCH   = 16;  // requires crouching
        public const int AREA_OBSTACLE = 32;  // obstacle present
        public const int AREA_BRIDGE   = 64;  // bridge/walkway
    }

    // Reachability types (travel types in the original AAS)
    public enum TravelType
    {
        Walk         = 1,
        Crouch       = 2,
        BarrierJump  = 3,
        Jump         = 4,
        Ladder       = 5,
        WalkOffLedge = 6,
        Swim         = 7,
        WaterJump    = 8,
        Teleport     = 9,
        Elevator     = 10,
        RocketJump   = 11,
        BFGJump      = 12,
        GrappleHook  = 13,
        DoubleJump   = 14,
        RampJump     = 15,
        StrafeLadder = 16,
        JumpPad      = 17,
        FuncBobbing  = 18,
    }

    // A single waypoint in a computed path
    public struct PathWaypoint
    {
        public Vector3    Position;
        public TravelType TravelType;
        public float      TravelTime; // estimated seconds to reach from previous
    }

    // Result of a route query
    public struct RouteResult
    {
        public bool           Reachable;
        public PathWaypoint[] Waypoints;
        public float          TotalTime; // estimated total travel time in seconds
    }

    // AAS-compatible pathfinding API backed by Unity NavMesh
    public static class PathFinding
    {
        // NavMesh area mask for walkable surfaces
        public const int NAVMESH_WALKABLE = NavMesh.AllAreas;

        // Maximum path corner points to request from NavMesh
        private const int MAX_PATH_CORNERS = 64;

        // Bot movement speeds (units/second in ET space, used for travel time estimates)
        private const float WALK_SPEED   = 190f;
        private const float SWIM_SPEED   = 100f;
        private const float CROUCH_SPEED = 90f;

        // Layer mask for water volumes — Water is Unity's built-in layer 4
        private static readonly int s_WaterLayerMask = 1 << 4;

        // -----------------------------------------------------------------------
        // AAS_AreaTravelTimeToGoalArea
        // Compute travel time from start to goal.
        // Returns -1 if unreachable. Time in milliseconds (matches ET's convention).
        // -----------------------------------------------------------------------
        public static int AAS_AreaTravelTimeToGoalArea(Vector3 start, Vector3 goal)
        {
            RouteResult result = BotFindRoute(start, goal);
            if (!result.Reachable)
                return -1;

            return (int)(result.TotalTime * 1000f);
        }

        // -----------------------------------------------------------------------
        // AAS_GetRouteFirstVisPos
        // Return the first move target toward goal.
        // In AAS this returned the first reachable area along the path.
        // Here: returns the next NavMesh corner on the path to goal.
        // -----------------------------------------------------------------------
        public static bool AAS_GetRouteFirstVisPos(Vector3 start, Vector3 goal, out Vector3 nextPos)
        {
            nextPos = start;

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, goal, NAVMESH_WALKABLE, path))
                return false;

            if (path.status != NavMeshPathStatus.PathComplete)
                return false;

            if (path.corners == null || path.corners.Length < 2)
                return false;

            nextPos = path.corners[1];
            return true;
        }

        // -----------------------------------------------------------------------
        // BotFindRoute
        // Full route from start to goal.
        // -----------------------------------------------------------------------
        public static RouteResult BotFindRoute(Vector3 start, Vector3 goal, int agentTypeId = 0)
        {
            RouteResult routeResult = new RouteResult
            {
                Reachable = false,
                Waypoints = Array.Empty<PathWaypoint>(),
                TotalTime = 0f,
            };

            NavMeshPath path = new NavMeshPath();
            bool found = NavMesh.CalculatePath(start, goal, NAVMESH_WALKABLE, path);

            if (!found || path.status != NavMeshPathStatus.PathComplete)
                return routeResult;

            Vector3[] corners = path.corners;
            if (corners == null || corners.Length == 0)
                return routeResult;

            // Build waypoints — one per corner (skip index 0 which is the start)
            int waypointCount = corners.Length - 1;
            PathWaypoint[] waypoints = new PathWaypoint[waypointCount];
            float totalTime = 0f;

            for (int i = 0; i < waypointCount; i++)
            {
                Vector3 from = corners[i];
                Vector3 to   = corners[i + 1];

                TravelType travelType = BotClassifyTravelType(from, to);
                float speed = travelType == TravelType.Swim ? SWIM_SPEED
                            : travelType == TravelType.Crouch ? CROUCH_SPEED
                            : WALK_SPEED;

                float segmentDistance = Vector3.Distance(from, to);
                float segmentTime     = segmentDistance / speed;
                totalTime += segmentTime;

                waypoints[i] = new PathWaypoint
                {
                    Position   = to,
                    TravelType = travelType,
                    TravelTime = segmentTime,
                };
            }

            routeResult.Reachable = true;
            routeResult.Waypoints = waypoints;
            routeResult.TotalTime = totalTime;
            return routeResult;
        }

        // -----------------------------------------------------------------------
        // BotMoveTo
        // Issue movement toward a goal for a NavMeshAgent.
        // Returns true if the agent accepted the destination.
        // -----------------------------------------------------------------------
        public static bool BotMoveTo(NavMeshAgent agent, Vector3 goal)
        {
            if (agent == null)
                return false;

            agent.SetDestination(goal);
            return !agent.pathPending;
        }

        // -----------------------------------------------------------------------
        // BotSampleRandomGoal
        // Pick a random reachable point within radius.
        // -----------------------------------------------------------------------
        public static bool BotSampleRandomGoal(Vector3 origin, float radius, out Vector3 result)
        {
            result = origin;

            const int maxRetries = 10;
            for (int i = 0; i < maxRetries; i++)
            {
                Vector3 candidate = origin + UnityEngine.Random.insideUnitSphere * radius;
                NavMeshHit hit;
                if (NavMesh.SamplePosition(candidate, out hit, radius, NAVMESH_WALKABLE))
                {
                    result = hit.position;
                    return true;
                }
            }

            return false;
        }

        // -----------------------------------------------------------------------
        // BotOnNavMesh
        // True if the point is on (or within tolerance of) the NavMesh.
        // -----------------------------------------------------------------------
        public static bool BotOnNavMesh(Vector3 point, float tolerance = 0.5f)
        {
            NavMeshHit hit;
            return NavMesh.SamplePosition(point, out hit, tolerance, NAVMESH_WALKABLE);
        }

        // -----------------------------------------------------------------------
        // BotNearestNavMeshPoint
        // Snap a world point to the nearest NavMesh surface.
        // -----------------------------------------------------------------------
        public static Vector3 BotNearestNavMeshPoint(Vector3 point, float searchRadius = 5f)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(point, out hit, searchRadius, NAVMESH_WALKABLE))
                return hit.position;

            return point;
        }

        // -----------------------------------------------------------------------
        // BotClassifyTravelType
        // Classify the movement type needed between two points.
        // Heuristic based on height difference and water volume check.
        // -----------------------------------------------------------------------
        public static TravelType BotClassifyTravelType(Vector3 from, Vector3 to)
        {
            float heightDelta = Mathf.Abs(to.y - from.y);
            if (heightDelta > 50f)
                return TravelType.Jump;

            Vector3 midpoint = (from + to) * 0.5f;
            if (Physics.CheckSphere(midpoint, 0.5f, s_WaterLayerMask))
                return TravelType.Swim;

            return TravelType.Walk;
        }

        // -----------------------------------------------------------------------
        // BotClearPath
        // True if there is line-of-sight (no static geometry) from A to B.
        // Uses Physics.Linecast with layer mask for static world geometry.
        // -----------------------------------------------------------------------
        public static bool BotClearPath(Vector3 from, Vector3 to, int layerMask = ~0)
        {
            return !Physics.Linecast(from, to, layerMask);
        }
    }
}
