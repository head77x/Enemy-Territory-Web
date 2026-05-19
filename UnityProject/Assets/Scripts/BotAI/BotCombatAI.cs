// Ports: ai_dmgoal_mp.c + ai_dmnet_mp.c + ai_dmq3.c
// Bot goal selection, AI state machine, combat/weapon/movement logic.
// AAS travel calculations replaced by Unity NavMesh.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ET.Game;
using ET.Server;

namespace ET.BotAI
{
    // =========================================================================
    // BotGoalType — botgoalFindType_t (ai_dmgoal_mp.c)
    // =========================================================================
    public enum BotGoalType
    {
        None = 0,
        AttackEnemy,
        DefendArea,
        GetHealth,
        GetAmmo,
        PlantExplosive,
        DisarmExplosive,
        RepairMG42,
        UseMG42,
        ReviveFriend,
        GiveAmmo,
        GiveHealth,
        ConstructObj,
        DestroyObj,
        CaptureFlag,
        ReturnFlag,
        NUM_BOT_GOAL_TYPES
    }

    // =========================================================================
    // BotGoal — current pursuit target
    // =========================================================================
    public class BotGoal
    {
        public BotGoalType  Type;
        public Vector3      Position;
        public int          EntityNum = -1;
        public int          AreaNum   = -1;
        public float        Priority;
        public string       Description = "";
    }

    // =========================================================================
    // BotAIState — ai_dmnet_mp.c state machine nodes
    // =========================================================================
    public enum BotAIState
    {
        Intermission,
        Observer,
        Stand,
        Respawn,
        SeekGoal,
        SeekActivateEntity,
        SeekNBG,
        AvoidDanger,
        AttackTarget,
        Battle_MobileMG42,
        MG42Scan,
        PanzerTarget,
        GiveAmmo,
        MedicGiveHealth,
        MedicRevive,
        FixMG42,
        Combat,
    }

    // =========================================================================
    // BotState — per-bot runtime data (shared across all BotAI files)
    // Defined here; BotMain.cs references it via assembly reference.
    // =========================================================================
    public class BotState
    {
        public int       ClientNum;
        public string    Name        = "";
        public int       Team;
        public Vector3   Position;
        public Vector3   TargetPosition;
        public Vector3   BasePosition;
        public Vector3   CampPosition;
        public int       CurrentTarget = -1;
        public int       FollowTarget  = -1;
        public bool      FollowLeader;
        public bool      IsCamping;
        public bool      IsPatrolling;
        public bool      IsRushing;
        public int       DesiredWeapon;
        public int       DesiredClass;
        public int       CurrentWeapon;
        public float     Health;
        public float     Armor;
        public int[]     Ammo         = new int[GameConstants.MAX_WEAPONS];
        public BotGoal   CurrentGoal  = new BotGoal();
        public BotGoal   LongRangeGoal = new BotGoal();
        public BotGoal   NearByGoal   = new BotGoal();
        public BotScript Script;
        public BotAIState AIState     = BotAIState.Respawn;
        public NavMeshAgent Agent;
        public string    CurrentGoalDescription = "";
        public List<Vector3> PatrolWaypoints = new List<Vector3>();
        public int       PatrolIndex;
        public int       LastDangerTime;
        public int       LastAttackTime;
        public int       RespawnTime;
        public bool      IsDead;
        public int       EnemyEntityNum = -1;
        public float     EnemyVisibleTime;
    }

    // =========================================================================
    // BotGoalSystem — ai_dmgoal_mp.c
    // Selects the best goal for each bot based on class, priorities, and map state.
    // =========================================================================
    public static class BotGoalSystem
    {
        // Class-specific priority tables (matching C botgoalPriorities_*)
        private static readonly float[][] _classPriorities = new float[][]
        {
            // Soldier
            new float[] { 0.9f, 0.8f, 0.5f, 0.5f, 0.0f, 0.0f, 0.3f, 0.4f, 0.0f, 0.0f, 0.0f, 0.2f, 0.0f, 0.7f, 0.3f, 0.1f },
            // Medic
            new float[] { 0.6f, 0.7f, 0.8f, 0.4f, 0.0f, 0.0f, 0.2f, 0.3f, 0.9f, 0.0f, 0.7f, 0.1f, 0.0f, 0.5f, 0.2f, 0.1f },
            // Engineer
            new float[] { 0.7f, 0.6f, 0.5f, 0.5f, 0.9f, 0.9f, 0.4f, 0.5f, 0.0f, 0.0f, 0.0f, 0.8f, 0.8f, 0.6f, 0.4f, 0.1f },
            // FieldOps
            new float[] { 0.6f, 0.7f, 0.5f, 0.8f, 0.0f, 0.0f, 0.3f, 0.4f, 0.0f, 0.8f, 0.6f, 0.2f, 0.0f, 0.5f, 0.3f, 0.1f },
            // CovertOps
            new float[] { 0.7f, 0.6f, 0.4f, 0.5f, 0.5f, 0.7f, 0.3f, 0.4f, 0.0f, 0.0f, 0.0f, 0.3f, 0.5f, 0.6f, 0.3f, 0.8f },
        };

        // BotMP_FindGoal — selects the highest-priority available goal
        public static bool FindGoal(BotState bs)
        {
            var level = ServerGameLogic.Level;
            int cls   = Mathf.Clamp(bs.DesiredClass, 0, _classPriorities.Length - 1);
            var prio  = _classPriorities[cls];

            float   bestScore = -1f;
            BotGoal best      = null;

            // --- Attack nearest visible enemy ---
            if ((int)BotGoalType.AttackEnemy < prio.Length && prio[(int)BotGoalType.AttackEnemy] > 0)
            {
                var enemy = FindNearestEnemy(bs);
                if (enemy >= 0)
                {
                    float score = prio[(int)BotGoalType.AttackEnemy] * 100f
                        - Vector3.Distance(bs.Position,
                              ServerGameLogic.Entities[enemy]?.Origin ?? Vector3.zero);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new BotGoal
                        {
                            Type        = BotGoalType.AttackEnemy,
                            EntityNum   = enemy,
                            Position    = ServerGameLogic.Entities[enemy]?.Origin ?? Vector3.zero,
                            Priority    = score,
                            Description = $"Attack player {enemy}",
                        };
                    }
                }
            }

            // --- Revive fallen teammate (Medic) ---
            if (cls == 1 && prio[(int)BotGoalType.ReviveFriend] > 0)
            {
                var fallen = FindFallenTeammate(bs);
                if (fallen >= 0)
                {
                    float score = prio[(int)BotGoalType.ReviveFriend] * 200f
                        - Vector3.Distance(bs.Position,
                              ServerGameLogic.Entities[fallen]?.Origin ?? Vector3.zero);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new BotGoal
                        {
                            Type        = BotGoalType.ReviveFriend,
                            EntityNum   = fallen,
                            Position    = ServerGameLogic.Entities[fallen]?.Origin ?? Vector3.zero,
                            Priority    = score,
                            Description = $"Revive teammate {fallen}",
                        };
                    }
                }
            }

            // --- Construct objective (Engineer) ---
            if ((cls == 2) && prio[(int)BotGoalType.ConstructObj] > 0)
            {
                var objEnt = FindConstructibleObjective(bs);
                if (objEnt >= 0)
                {
                    float dist = Vector3.Distance(bs.Position,
                        ServerGameLogic.Entities[objEnt]?.Origin ?? Vector3.zero);
                    float score = prio[(int)BotGoalType.ConstructObj] * 300f - dist;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new BotGoal
                        {
                            Type        = BotGoalType.ConstructObj,
                            EntityNum   = objEnt,
                            Position    = ServerGameLogic.Entities[objEnt]?.Origin ?? Vector3.zero,
                            Priority    = score,
                            Description = "Construct objective",
                        };
                    }
                }
            }

            if (best != null)
            {
                bs.CurrentGoal = best;
                bs.CurrentGoalDescription = best.Description;
                return true;
            }

            // Fallback: move to base area
            bs.CurrentGoal = new BotGoal
            {
                Type        = BotGoalType.DefendArea,
                Position    = bs.BasePosition,
                Priority    = 1f,
                Description = "Defending base",
            };
            return false;
        }

        // BotMP_CheckClassActions — class-specific emergency actions
        public static bool CheckClassActions(BotState bs)
        {
            // Medic: revive if low health friends nearby
            if (bs.DesiredClass == 1 && bs.Health < 50f)
            {
                bs.CurrentGoal.Type = BotGoalType.MedicGiveHealth;
                return true;
            }
            // FieldOps: give ammo if ammo needed
            if (bs.DesiredClass == 3)
            {
                var needsAmmo = FindTeammateNeedingAmmo(bs);
                if (needsAmmo >= 0)
                {
                    bs.CurrentGoal.Type     = BotGoalType.GiveAmmo;
                    bs.CurrentGoal.EntityNum = needsAmmo;
                    return true;
                }
            }
            return false;
        }

        private static int FindNearestEnemy(BotState bs)
        {
            float bestDist = 1500f;
            int   best     = -1;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var ent = ServerGameLogic.Entities[i];
                if (ent?.Client == null || !ent.InUse || i == bs.ClientNum) continue;
                if (ent.Client.Sess.SessionTeam == bs.Team) continue;
                if (ent.Health <= 0) continue;
                float d = Vector3.Distance(bs.Position, ent.Origin);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private static int FindFallenTeammate(BotState bs)
        {
            float bestDist = 600f;
            int   best     = -1;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var ent = ServerGameLogic.Entities[i];
                if (ent?.Client == null || !ent.InUse || i == bs.ClientNum) continue;
                if (ent.Client.Sess.SessionTeam != bs.Team) continue;
                if (ent.Health > 0) continue;   // not dead
                float d = Vector3.Distance(bs.Position, ent.Origin);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        private static int FindConstructibleObjective(BotState bs)
        {
            var level = ServerGameLogic.Level;
            for (int i = GameConstants.MAX_CLIENTS; i < level.NumEntities; i++)
            {
                var e = ServerGameLogic.Entities[i];
                if (e == null || !e.InUse) continue;
                if (e.S.EType != GameConstants.ET_CONSTRUCTIBLE_INDICATOR) continue;
                if (e.Team != bs.Team) continue;
                if (BotTeamAI.ConstructionFullyBuilt(e)) continue;
                return i;
            }
            return -1;
        }

        private static int FindTeammateNeedingAmmo(BotState bs)
        {
            float bestDist = 400f;
            int   best     = -1;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var ent = ServerGameLogic.Entities[i];
                if (ent?.Client == null || !ent.InUse || i == bs.ClientNum) continue;
                if (ent.Client.Sess.SessionTeam != bs.Team) continue;
                if (ent.Client.PS.Ammo[ent.Client.PS.Weapon] > 10) continue;
                float d = Vector3.Distance(bs.Position, ent.Origin);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }

    // =========================================================================
    // BotStateMachine — ai_dmnet_mp.c state machine
    // =========================================================================
    public static class BotStateMachine
    {
        // Transition to a new state
        public static void Enter(BotState bs, BotAIState newState)
        {
            bs.AIState = newState;
        }

        // AINode dispatch — called every frame
        public static void Update(BotState bs, int cgTime)
        {
            if (bs.Script != null)
                BotScriptSystem.Run(bs, cgTime);

            switch (bs.AIState)
            {
                case BotAIState.Intermission:  Node_Intermission(bs); break;
                case BotAIState.Observer:      Node_Observer(bs);     break;
                case BotAIState.Respawn:       Node_Respawn(bs, cgTime); break;
                case BotAIState.SeekGoal:      Node_SeekGoal(bs);    break;
                case BotAIState.AttackTarget:  Node_AttackTarget(bs); break;
                case BotAIState.AvoidDanger:   Node_AvoidDanger(bs, cgTime); break;
                case BotAIState.MedicRevive:   Node_MedicRevive(bs); break;
                case BotAIState.MedicGiveHealth: Node_MedicGiveHealth(bs); break;
                case BotAIState.GiveAmmo:      Node_GiveAmmo(bs);    break;
                case BotAIState.FixMG42:       Node_FixMG42(bs);     break;
                case BotAIState.MG42Scan:      Node_MG42Scan(bs);    break;
                case BotAIState.Combat:        Node_Combat(bs);      break;
                default:                       Node_Stand(bs);       break;
            }
        }

        private static void Node_Intermission(BotState bs)
        {
            if (bs.Agent != null) bs.Agent.ResetPath();
        }

        private static void Node_Observer(BotState bs)
        {
            if (bs.Agent != null) bs.Agent.ResetPath();
        }

        private static void Node_Respawn(BotState bs, int cgTime)
        {
            if (cgTime < bs.RespawnTime) return;
            if (!bs.IsDead) Enter(bs, BotAIState.SeekGoal);
        }

        private static void Node_Stand(BotState bs)
        {
            BotGoalSystem.FindGoal(bs);
            Enter(bs, BotAIState.SeekGoal);
        }

        private static void Node_SeekGoal(BotState bs)
        {
            // Check for immediate danger
            var nearEnemy = FindNearestEnemy(bs, 400f);
            if (nearEnemy >= 0)
            {
                bs.EnemyEntityNum = nearEnemy;
                Enter(bs, BotAIState.AttackTarget);
                return;
            }

            // Emergency class actions
            if (BotGoalSystem.CheckClassActions(bs)) return;

            // Move toward current goal
            if (bs.CurrentGoal != null && bs.Agent != null)
            {
                bs.Agent.SetDestination(bs.CurrentGoal.Position);
                if (Vector3.Distance(bs.Position, bs.CurrentGoal.Position) < 50f)
                    BotGoalSystem.FindGoal(bs);
            }
            else
                BotGoalSystem.FindGoal(bs);
        }

        private static void Node_AttackTarget(BotState bs)
        {
            if (bs.EnemyEntityNum < 0) { Enter(bs, BotAIState.SeekGoal); return; }
            var enemy = ServerGameLogic.Entities[bs.EnemyEntityNum];
            if (enemy == null || !enemy.InUse || enemy.Health <= 0)
            { bs.EnemyEntityNum = -1; Enter(bs, BotAIState.SeekGoal); return; }

            BotCombat.ChooseWeapon(bs);
            BotCombat.FaceTarget(bs, enemy.Origin);

            float dist = Vector3.Distance(bs.Position, enemy.Origin);
            if (dist > BotCombat.OptimalRange(bs.CurrentWeapon))
                bs.Agent?.SetDestination(enemy.Origin);
            else
                bs.Agent?.ResetPath();
        }

        private static void Node_AvoidDanger(BotState bs, int cgTime)
        {
            if (cgTime - bs.LastDangerTime > 3000)
                Enter(bs, BotAIState.SeekGoal);
            else
            {
                // Move away from danger source
                if (bs.CurrentGoal != null && bs.Agent != null)
                {
                    Vector3 away = bs.Position + (bs.Position - bs.CurrentGoal.Position).normalized * 300f;
                    bs.Agent.SetDestination(away);
                }
            }
        }

        private static void Node_MedicRevive(BotState bs)
        {
            if (bs.CurrentGoal?.EntityNum < 0) { Enter(bs, BotAIState.SeekGoal); return; }
            var target = ServerGameLogic.Entities[bs.CurrentGoal.EntityNum];
            if (target == null || target.Health > 0) { Enter(bs, BotAIState.SeekGoal); return; }
            bs.Agent?.SetDestination(target.Origin);
        }

        private static void Node_MedicGiveHealth(BotState bs)
        {
            if (bs.CurrentGoal?.EntityNum < 0) { Enter(bs, BotAIState.SeekGoal); return; }
            var target = ServerGameLogic.Entities[bs.CurrentGoal.EntityNum];
            if (target == null || target.Health >= 100) { Enter(bs, BotAIState.SeekGoal); return; }
            bs.Agent?.SetDestination(target.Origin);
        }

        private static void Node_GiveAmmo(BotState bs)
        {
            if (bs.CurrentGoal?.EntityNum < 0) { Enter(bs, BotAIState.SeekGoal); return; }
            var target = ServerGameLogic.Entities[bs.CurrentGoal.EntityNum];
            if (target == null) { Enter(bs, BotAIState.SeekGoal); return; }
            bs.Agent?.SetDestination(target.Origin);
        }

        private static void Node_FixMG42(BotState bs)
        {
            if (bs.CurrentGoal == null) { Enter(bs, BotAIState.SeekGoal); return; }
            bs.Agent?.SetDestination(bs.CurrentGoal.Position);
        }

        private static void Node_MG42Scan(BotState bs)
        {
            // Mounted on MG42 — fire at enemies in arc
            var enemy = FindNearestEnemy(bs, 1000f);
            if (enemy < 0) Enter(bs, BotAIState.SeekGoal);
            else bs.EnemyEntityNum = enemy;
        }

        private static void Node_Combat(BotState bs)
        {
            BotCombat.ChooseWeapon(bs);
            var nearEnemy = FindNearestEnemy(bs, 1200f);
            if (nearEnemy < 0) { Enter(bs, BotAIState.SeekGoal); return; }
            bs.EnemyEntityNum = nearEnemy;
            var enemy = ServerGameLogic.Entities[nearEnemy];
            if (enemy != null) BotCombat.FaceTarget(bs, enemy.Origin);
        }

        private static int FindNearestEnemy(BotState bs, float maxDist)
        {
            float best = maxDist;
            int   idx  = -1;
            for (int i = 0; i < GameConstants.MAX_CLIENTS; i++)
            {
                var ent = ServerGameLogic.Entities[i];
                if (ent?.Client == null || !ent.InUse || i == bs.ClientNum) continue;
                if (ent.Client.Sess.SessionTeam == bs.Team) continue;
                if (ent.Health <= 0) continue;
                float d = Vector3.Distance(bs.Position, ent.Origin);
                if (d < best) { best = d; idx = i; }
            }
            return idx;
        }
    }

    // =========================================================================
    // BotCombat — ai_dmq3.c weapon selection + movement
    // =========================================================================
    public static class BotCombat
    {
        // BotBestFightWeapon — selects weapon for current range/ammo
        public static void ChooseWeapon(BotState bs)
        {
            float dist = bs.EnemyEntityNum >= 0
                ? Vector3.Distance(bs.Position,
                    ServerGameLogic.Entities[bs.EnemyEntityNum]?.Origin ?? Vector3.zero)
                : 999f;

            // Prefer long-range weapons when enemy is far
            if (dist > 600f && HasAmmo(bs, GameConstants.WP_GARAND))
                bs.CurrentWeapon = GameConstants.WP_GARAND;
            else if (dist > 400f && HasAmmo(bs, GameConstants.WP_FG42))
                bs.CurrentWeapon = GameConstants.WP_FG42;
            else if (HasAmmo(bs, GameConstants.WP_MP40))
                bs.CurrentWeapon = GameConstants.WP_MP40;
            else if (HasAmmo(bs, GameConstants.WP_THOMPSON))
                bs.CurrentWeapon = GameConstants.WP_THOMPSON;
            else
                bs.CurrentWeapon = GameConstants.WP_KNIFE;
        }

        // BotChooseWeapon — fire command helper
        public static void CycleWeapon(BotState bs)
        {
            for (int i = bs.CurrentWeapon + 1; i < GameConstants.MAX_WEAPONS; i++)
                if (HasAmmo(bs, i)) { bs.CurrentWeapon = i; return; }
            for (int i = 1; i < bs.CurrentWeapon; i++)
                if (HasAmmo(bs, i)) { bs.CurrentWeapon = i; return; }
        }

        // BotBestWeaponRange — optimal engagement distance per weapon
        public static float OptimalRange(int weapon)
        {
            switch (weapon)
            {
                case GameConstants.WP_GARAND:
                case GameConstants.WP_SNIPERRIFLE: return 900f;
                case GameConstants.WP_FG42:
                case GameConstants.WP_PANZERFAUST:  return 500f;
                case GameConstants.WP_FLAMETHROWER: return 180f;
                case GameConstants.WP_KNIFE:        return 80f;
                default:                             return 300f;
            }
        }

        // BotWeaponOnlyUseIfInRange
        public static bool OnlyUseInRange(int weapon) =>
            weapon == GameConstants.WP_FLAMETHROWER ||
            weapon == GameConstants.WP_KNIFE;

        // BotGotEnoughAmmoForWeapon
        public static bool HasAmmo(BotState bs, int weapon) =>
            weapon >= 0 && weapon < bs.Ammo.Length && bs.Ammo[weapon] > 0;

        // BotChooseWeapon facing direction
        public static void FaceTarget(BotState bs, Vector3 targetPos)
        {
            Vector3 dir = (targetPos - bs.Position).normalized;
            if (bs.Agent != null) bs.Agent.transform.rotation =
                Quaternion.LookRotation(new Vector3(dir.x, 0, dir.z));
        }

        // BotIsDead
        public static bool IsDead(BotState bs) => bs.Health <= 0;

        // BotCarryingFlag
        public static bool CarryingFlag(BotState bs)
        {
            var cl = ServerGameLogic.Level.Clients[bs.ClientNum];
            if (cl == null) return false;
            return (cl.PS.PowerUps & (1 << GameConstants.PW_REDFLAG)) != 0 ||
                   (cl.PS.PowerUps & (1 << GameConstants.PW_BLUEFLAG)) != 0;
        }

        // BotUpdateInventory — syncs bot's ammo table from PlayerState
        public static void UpdateInventory(BotState bs)
        {
            var cl = ServerGameLogic.Level.Clients[bs.ClientNum];
            if (cl == null) return;
            bs.Health = cl.PS.Stats[GameConstants.STAT_HEALTH];
            bs.Armor  = cl.PS.Stats[GameConstants.STAT_ARMOR];
            for (int i = 0; i < Mathf.Min(bs.Ammo.Length, cl.PS.Ammo.Length); i++)
                bs.Ammo[i] = cl.PS.Ammo[i];
        }

        // BotTeamMatesNearEnemy — count friendly bots near a given enemy
        public static int TeamMatesNearEnemy(BotState bs, int enemy)
        {
            var enemyEnt = ServerGameLogic.Entities[enemy];
            if (enemyEnt == null) return 0;
            int cnt = 0;
            foreach (var other in BotMain.AllBotStates)
            {
                if (other == bs || other.Team != bs.Team) continue;
                if (Vector3.Distance(other.Position, enemyEnt.Origin) < 300f) cnt++;
            }
            return cnt;
        }

        // BotSetupForMovement
        public static void SetupMovement(BotState bs)
        {
            if (bs.Agent == null) return;
            bs.Agent.speed        = 240f;
            bs.Agent.acceleration = 800f;
            bs.Agent.angularSpeed = 360f;
        }
    }
}
