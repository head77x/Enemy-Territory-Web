// Ported from: src/game/g_bot.c, src/botai/ai_main.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using ET.Game;
using ET.Network;
using UnityEngine;
using UnityEngine.AI;

namespace ET.BotAI
{
    // Bot difficulty levels
    public enum BotSkill { Easy = 1, Medium = 2, Hard = 3, Expert = 4 }

    // Bot states (mirrors ET's bot goal states)
    public enum BotState
    {
        Idle,
        Roaming,          // no objective — wander
        Attacking,        // engaging an enemy
        Retreating,       // low health, seeking medic/cover
        Capturing,        // moving to objective
        Defending,        // guarding objective
        Reviving,         // medic going to revive teammate
        Planting,         // engineer planting dynamite
        Defusing,         // engineer defusing
        Resupplying,      // seeking ammo
        FollowingOrder    // responding to a voice command
    }

    public class BotAgent
    {
        public int          ClientNum;
        public string       Name;
        public BotSkill     Skill;
        public BotState     State;
        public int          Team;             // TEAM_AXIS or TEAM_ALLIES
        public int          PlayerClass;      // PC_SOLDIER etc.
        public Vector3      GoalPos;
        public int          TargetEntityNum;  // enemy target, or -1
        public float        ReactionTime;     // seconds before reacting to new threat
        public float        AimNoise;         // max aim error in degrees
        public float        LastThinkTime;
        public float        StateTimer;       // time in current state
        public NavMeshAgent NavAgent;         // Unity NavMesh component

        // Cached entity state for the current frame (populated by the game layer)
        public int          Health;
        public Vector3      EnemyPos;         // last known enemy world position

        public BotAgent(int clientNum, string name, BotSkill skill, int team, int playerClass)
        {
            ClientNum       = clientNum;
            Name            = name;
            Skill           = skill;
            Team            = team;
            PlayerClass     = playerClass;
            State           = BotState.Idle;
            TargetEntityNum = -1;
            LastThinkTime   = 0f;
            StateTimer      = 0f;
            Health          = 100;
            GoalPos         = Vector3.zero;
            EnemyPos        = Vector3.zero;

            // Higher skill = faster reaction, tighter aim
            ReactionTime = 0.3f - (int)skill * 0.05f;  // Easy=0.25 … Expert=0.10
            AimNoise     = 15f  - (int)skill * 3f;      // Easy=12° … Expert=3°
        }
    }

    public static class BotMain
    {
        private static readonly List<BotAgent> _bots = new List<BotAgent>();

        // Random wander radius used in StateRoaming
        private const float ROAM_RADIUS         = 200f;
        // Health threshold below which a bot retreats
        private const float RETREAT_HEALTH      = 30f;
        // Distance at which an objective is "nearby" and worth capturing
        private const float OBJECTIVE_NEAR_DIST = 150f;

        // Events raised instead of direct VM_Call into the game
        /// <summary>Raised each frame for each bot with the synthesised UserCmd.</summary>
        public static event Action<int, UserCmd> OnBotCmd;
        /// <summary>Raised whenever a bot sends a chat message.</summary>
        public static event Action<int, string>  OnBotChat;

        // -----------------------------------------------------------------------
        // G_BotConnect — spawn a new bot (mirrors G_BotConnect in g_bot.c)
        // -----------------------------------------------------------------------
        public static BotAgent G_BotConnect(int clientNum, string name, BotSkill skill,
                                             int team, int playerClass)
        {
            // Remove any stale entry for this clientNum
            G_BotDisconnect(clientNum);

            var bot = new BotAgent(clientNum, name, skill, team, playerClass);
            _bots.Add(bot);
            return bot;
        }

        // -----------------------------------------------------------------------
        // G_BotDisconnect — remove a bot
        // -----------------------------------------------------------------------
        public static void G_BotDisconnect(int clientNum)
        {
            _bots.RemoveAll(b => b.ClientNum == clientNum);
        }

        // -----------------------------------------------------------------------
        // BotAI_Think — called each server frame for all bots (mirrors BotAISetupClient)
        // deltaTime in seconds
        // -----------------------------------------------------------------------
        public static void BotAI_Think(float deltaTime)
        {
            foreach (var bot in _bots)
                ThinkBot(bot, deltaTime);
        }

        // -----------------------------------------------------------------------
        // ThinkBot — per-bot frame: state machine + command generation
        // -----------------------------------------------------------------------
        private static void ThinkBot(BotAgent bot, float deltaTime)
        {
            if (bot.NavAgent == null)
                return;

            bot.StateTimer    += deltaTime;
            bot.LastThinkTime += deltaTime;

            // Re-evaluate state every ReactionTime seconds
            if (bot.LastThinkTime >= bot.ReactionTime)
            {
                bot.LastThinkTime = 0f;
                EvaluateState(bot);
            }

            // Execute the current state
            switch (bot.State)
            {
                case BotState.Idle:            StateIdle(bot, deltaTime);       break;
                case BotState.Roaming:         StateRoaming(bot, deltaTime);    break;
                case BotState.Attacking:       StateAttacking(bot, deltaTime);  break;
                case BotState.Retreating:      StateRetreating(bot, deltaTime); break;
                case BotState.Capturing:       StateCapturing(bot, deltaTime);  break;
                case BotState.Defending:       StateDefending(bot, deltaTime);  break;
                case BotState.Reviving:        StateReviving(bot, deltaTime);   break;
                case BotState.FollowingOrder:  StateFollowingOrder(bot, deltaTime); break;
                // Remaining states fall back to idle behaviour for now
                default:                       StateIdle(bot, deltaTime);       break;
            }

            // Build and raise the UserCmd for this frame
            var cmd = BuildBotCmd(bot, deltaTime);
            OnBotCmd?.Invoke(bot.ClientNum, cmd);
        }

        // -----------------------------------------------------------------------
        // EvaluateState — choose the best state given current conditions
        // -----------------------------------------------------------------------
        private static void EvaluateState(BotAgent bot)
        {
            // 1. Low health → retreat
            if (bot.Health < RETREAT_HEALTH)
            {
                ChangeState(bot, BotState.Retreating);
                return;
            }

            // 2. Visible enemy → attack
            if (bot.TargetEntityNum != -1 && CanSeeTarget(bot))
            {
                ChangeState(bot, BotState.Attacking);
                return;
            }

            // 3. Objective nearby → capture
            if (bot.GoalPos != Vector3.zero &&
                Vector3.Distance(bot.NavAgent.transform.position, bot.GoalPos) < OBJECTIVE_NEAR_DIST)
            {
                ChangeState(bot, BotState.Capturing);
                return;
            }

            // 4. Default: roam
            if (bot.State == BotState.Idle || bot.State == BotState.Attacking)
                ChangeState(bot, BotState.Roaming);
        }

        private static void ChangeState(BotAgent bot, BotState next)
        {
            if (bot.State == next) return;
            bot.State      = next;
            bot.StateTimer = 0f;
        }

        // -----------------------------------------------------------------------
        // State handlers
        // -----------------------------------------------------------------------

        private static void StateIdle(BotAgent bot, float deltaTime)
        {
            // Stand still — transition out happens in EvaluateState
            bot.NavAgent.ResetPath();
        }

        private static void StateRoaming(BotAgent bot, float deltaTime)
        {
            // Pick a new random destination whenever the agent has reached its goal
            if (!bot.NavAgent.pathPending && bot.NavAgent.remainingDistance < 1.0f)
            {
                Vector3 randomDir  = UnityEngine.Random.insideUnitSphere * ROAM_RADIUS;
                Vector3 targetPos  = bot.NavAgent.transform.position + randomDir;

                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, ROAM_RADIUS, NavMesh.AllAreas))
                    bot.NavAgent.SetDestination(hit.position);
            }
        }

        private static void StateAttacking(BotAgent bot, float deltaTime)
        {
            if (bot.TargetEntityNum == -1)
            {
                ChangeState(bot, BotState.Roaming);
                return;
            }

            // Move toward last known enemy position
            bot.NavAgent.SetDestination(bot.EnemyPos);
        }

        private static void StateRetreating(BotAgent bot, float deltaTime)
        {
            if (bot.TargetEntityNum == -1)
            {
                ChangeState(bot, BotState.Roaming);
                return;
            }

            // Navigate to a point directly opposite the threat
            Vector3 selfPos    = bot.NavAgent.transform.position;
            Vector3 awayDir    = (selfPos - bot.EnemyPos).normalized;
            Vector3 retreatPos = selfPos + awayDir * ROAM_RADIUS;

            if (NavMesh.SamplePosition(retreatPos, out NavMeshHit hit, ROAM_RADIUS, NavMesh.AllAreas))
                bot.NavAgent.SetDestination(hit.position);
        }

        private static void StateCapturing(BotAgent bot, float deltaTime)
        {
            if (bot.GoalPos != Vector3.zero)
                bot.NavAgent.SetDestination(bot.GoalPos);
        }

        private static void StateDefending(BotAgent bot, float deltaTime)
        {
            // Hold position near goal; engage any visible enemy
            if (bot.TargetEntityNum != -1 && CanSeeTarget(bot))
            {
                ChangeState(bot, BotState.Attacking);
                return;
            }

            if (bot.GoalPos != Vector3.zero &&
                Vector3.Distance(bot.NavAgent.transform.position, bot.GoalPos) > 5f)
            {
                bot.NavAgent.SetDestination(bot.GoalPos);
            }
            else
            {
                bot.NavAgent.ResetPath();
            }
        }

        private static void StateReviving(BotAgent bot, float deltaTime)
        {
            // Medic navigates to the downed teammate's last known position
            if (bot.EnemyPos != Vector3.zero)
                bot.NavAgent.SetDestination(bot.EnemyPos);
        }

        private static void StateFollowingOrder(BotAgent bot, float deltaTime)
        {
            // EnemyPos is reused here to track the order-giver's position
            if (bot.EnemyPos != Vector3.zero)
                bot.NavAgent.SetDestination(bot.EnemyPos);
        }

        // -----------------------------------------------------------------------
        // Combat helpers
        // -----------------------------------------------------------------------

        /// <summary>Returns true when no solid geometry blocks the line of sight to the target.</summary>
        private static bool CanSeeTarget(BotAgent bot)
        {
            if (bot.TargetEntityNum == -1 || bot.NavAgent == null)
                return false;

            Vector3 origin    = bot.NavAgent.transform.position + Vector3.up * 1.6f; // eye height
            Vector3 targetPos = bot.EnemyPos + Vector3.up * 1.0f;

            return !Physics.Linecast(origin, targetPos);
        }

        private static float DistToTarget(BotAgent bot)
        {
            if (bot.NavAgent == null) return float.MaxValue;
            return Vector3.Distance(bot.NavAgent.transform.position, bot.EnemyPos);
        }

        /// <summary>
        /// Returns a direction vector toward targetPos with Gaussian-like aim noise applied.
        /// AimNoise is the maximum angular error in degrees.
        /// </summary>
        private static Vector3 AimWithNoise(BotAgent bot, Vector3 targetPos)
        {
            Vector3 dir     = (targetPos - bot.NavAgent.transform.position).normalized;
            float   noise   = UnityEngine.Random.Range(-bot.AimNoise, bot.AimNoise);
            // Rotate around the world-up axis by the noise angle
            Quaternion rot  = Quaternion.AngleAxis(noise, Vector3.up);
            // Additional vertical noise (half magnitude for vertical axis)
            float   vNoise  = UnityEngine.Random.Range(-bot.AimNoise * 0.5f, bot.AimNoise * 0.5f);
            Quaternion vRot = Quaternion.AngleAxis(vNoise, bot.NavAgent.transform.right);
            return vRot * rot * dir;
        }

        // -----------------------------------------------------------------------
        // BuildBotCmd — synthesise a UserCmd from the bot's NavMesh state
        // Mirrors the usercmd_t construction in the original bot code
        // -----------------------------------------------------------------------
        private static UserCmd BuildBotCmd(BotAgent bot, float deltaTime)
        {
            var cmd = new UserCmd();

            if (bot.NavAgent == null)
                return cmd;

            // --- Movement ---
            // NavAgent.desiredVelocity is in world space; project onto local axes
            Vector3 vel       = bot.NavAgent.desiredVelocity;
            Transform t       = bot.NavAgent.transform;
            float forward     = Vector3.Dot(vel, t.forward);
            float right       = Vector3.Dot(vel, t.right);

            // Clamp to [-127, 127] to match ET's byte-range UserCmd fields
            cmd.ForwardMove   = (sbyte)Mathf.Clamp(Mathf.RoundToInt(forward * 127f /
                                    Mathf.Max(bot.NavAgent.speed, 1f)), -127, 127);
            cmd.RightMove     = (sbyte)Mathf.Clamp(Mathf.RoundToInt(right   * 127f /
                                    Mathf.Max(bot.NavAgent.speed, 1f)), -127, 127);
            cmd.UpMove        = 0;

            // --- View angles ---
            if (bot.State == BotState.Attacking && bot.TargetEntityNum != -1)
            {
                Vector3 aimDir        = AimWithNoise(bot, bot.EnemyPos);
                Quaternion aimRot     = Quaternion.LookRotation(aimDir);
                Vector3 euler         = aimRot.eulerAngles;
                cmd.ViewAngles        = new Vector3(euler.x, euler.y, euler.z);
            }
            else if (vel.sqrMagnitude > 0.01f)
            {
                // Face movement direction while not attacking
                Quaternion moveRot    = Quaternion.LookRotation(vel.normalized);
                cmd.ViewAngles        = moveRot.eulerAngles;
            }

            // --- Buttons ---
            if (bot.State == BotState.Attacking && CanSeeTarget(bot))
                cmd.Buttons |= ButtonFlags.Attack;

            return cmd;
        }
    }
}
