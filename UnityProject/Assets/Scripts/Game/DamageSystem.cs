// Ported from: src/game/g_combat.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// Damage resolution, death handling, scoring, and XP system.
// Does NOT mutate entity state directly — computes outcomes and raises events.

using System;
using UnityEngine;

namespace ET.Game
{
    public static class DamageSystem
    {
        // ------------------------------------------------------------------
        // Initialisation — subscribe to WeaponSystem events
        // ------------------------------------------------------------------

        static DamageSystem()
        {
            WeaponSystem.OnDamage       += HandleDamage;
            WeaponSystem.OnRadiusDamage += HandleDamage;
        }

        // ------------------------------------------------------------------
        // Team constants
        // ------------------------------------------------------------------

        public const int TEAM_FREE      = 0;
        public const int TEAM_AXIS      = 1;
        public const int TEAM_ALLIES    = 2;
        public const int TEAM_SPECTATOR = 3;

        // ------------------------------------------------------------------
        // Damage flags (DF_*) — local copies for clarity
        // ------------------------------------------------------------------

        public const int DF_RADIUS_DAMAGE = 1;
        public const int DF_NO_LOCDAMAGE  = 2;
        public const int DF_NOFATIGUE     = 4;

        // ------------------------------------------------------------------
        // Health / death thresholds
        // ------------------------------------------------------------------

        /// <summary>GIB_HEALTH — below this value a player is gibbed (g_combat.c).</summary>
        public const int GIB_HEALTH = -40;

        /// <summary>Bleed-out window in milliseconds (~30 s).</summary>
        public const int BLEEDOUT_MS = 30000;

        /// <summary>Is the player dead (health &lt;= 0)?</summary>
        public static bool IsDead(int health) => health <= 0;

        /// <summary>Is the player gibbed (health &lt;= GIB_HEALTH)?</summary>
        public static bool IsGibbed(int health) => health <= GIB_HEALTH;

        // ------------------------------------------------------------------
        // XP awards
        // ------------------------------------------------------------------

        public const int XP_HEADSHOT_BONUS   =  1;
        public const int XP_TEAMKILL_PENALTY = -3;

        /// <summary>
        /// Base kill XP by victim player class (Kill_Awards approximation).
        /// Soldier=3, Medic=4, Engineer=3, FieldOps=3, CovertOps=4.
        /// </summary>
        public static int GetKillXP(int victimClass)
        {
            switch (victimClass)
            {
                case GameConst.PC_MEDIC:     return 4;
                case GameConst.PC_COVERTOPS: return 4;
                default:                     return 3;   // Soldier, Engineer, FieldOps
            }
        }

        // ------------------------------------------------------------------
        // Locational damage
        // ------------------------------------------------------------------

        public enum HitLocation { Body = 0, Head, Neck, Shoulder, Arm, Hand, Leg, Foot }

        /// <summary>
        /// Returns the damage multiplier for the given hit location.
        /// Mirrors the loc_damage_t table in g_combat.c.
        /// </summary>
        public static float GetLocationDamageMultiplier(HitLocation loc)
        {
            switch (loc)
            {
                case HitLocation.Head:     return 2.00f;
                case HitLocation.Neck:     return 1.50f;
                case HitLocation.Shoulder: return 0.90f;
                case HitLocation.Arm:      return 0.75f;
                case HitLocation.Hand:     return 0.60f;
                case HitLocation.Leg:      return 0.75f;
                case HitLocation.Foot:     return 0.60f;
                default:                   return 1.00f;   // Body
            }
        }

        // ------------------------------------------------------------------
        // Team damage helpers
        // ------------------------------------------------------------------

        /// <summary>Returns true when both team indices represent the same combat team.</summary>
        public static bool OnSameTeam(int teamA, int teamB)
        {
            // Spectators are never "on the same team" for damage purposes.
            if (teamA == TEAM_SPECTATOR || teamB == TEAM_SPECTATOR) return false;
            if (teamA == TEAM_FREE      || teamB == TEAM_FREE)      return false;
            return teamA == teamB;
        }

        /// <summary>
        /// Friendly-fire damage scale derived from g_friendlyFire cvar.
        ///   0 = FF off  → scale 0.0 (block all FF damage)
        ///   1 = FF on   → scale 0.5
        ///   2 = reflected → scale 1.0 (full — reflection handled upstream)
        /// </summary>
        public static float GetFriendlyFireScale()
        {
            int ff = CvarSystem.GetInt("g_friendlyFire", 0);
            return ff * 0.5f;
        }

        // ------------------------------------------------------------------
        // Outcome event structs
        // ------------------------------------------------------------------

        public struct DamageOutcome
        {
            public int  TargetEntityNum;
            public int  AttackerEntityNum;
            public int  FinalDamage;        // after all multipliers
            public int  Mod;
            public bool Headshot;
            public bool FriendlyFire;
            public bool WillKill;           // true when FinalDamage >= target's remaining health
            public HitLocation Location;
        }

        public struct DeathInfo
        {
            public int  VictimEntityNum;
            public int  AttackerEntityNum;
            public int  Mod;
            public bool Headshot;
            public bool Suicide;
            public bool TeamKill;
        }

        public struct KillInfo
        {
            public int  KillerEntityNum;
            public int  VictimEntityNum;
            public int  Mod;
            public bool Headshot;
            public int  XpAwarded;
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        /// <summary>Raised after team checks, armor, and location multipliers are applied.</summary>
        public static event Action<DamageOutcome> OnDamageResolved;

        /// <summary>Raised when a player's health reaches zero (or below).</summary>
        public static event Action<DeathInfo> OnPlayerDeath;

        /// <summary>Raised after OnPlayerDeath for non-suicide kills; carries XP info.</summary>
        public static event Action<KillInfo> OnKill;

        // ------------------------------------------------------------------
        // Core damage resolution (G_Damage equivalent)
        // ------------------------------------------------------------------

        /// <summary>
        /// Resolves a damage event raised by WeaponSystem.
        /// Applies locational multipliers, friendly-fire scaling, then raises
        /// OnDamageResolved and — when the target dies — OnPlayerDeath / OnKill.
        /// </summary>
        private static void HandleDamage(DamageInfo info)
        {
            // Validate participants.
            if (info.TargetEntityNum  < 0) return;
            if (info.AttackerEntityNum < 0) info.AttackerEntityNum = GameConst.ENTITYNUM_WORLD;

            float damage = info.Damage;

            // 1. Locational damage multiplier (skip for radius hits flagged DF_NO_LOCDAMAGE).
            bool headshot = info.Location == (int)HitLocation.Head;
            if ((info.DamageFlags & DF_NO_LOCDAMAGE) == 0)
            {
                HitLocation loc = LocationFromRaw(info.Location);
                damage *= GetLocationDamageMultiplier(loc);
            }

            // 2. Friendly-fire check.
            bool ff = OnSameTeam(info.AttackerTeam, info.TargetTeam);
            if (ff)
            {
                float ffScale = GetFriendlyFireScale();
                if (ffScale <= 0f) return;   // FF disabled — eat the damage entirely
                damage *= ffScale;
            }

            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage));

            // 3. Build and raise DamageOutcome.
            var outcome = new DamageOutcome
            {
                TargetEntityNum   = info.TargetEntityNum,
                AttackerEntityNum = info.AttackerEntityNum,
                FinalDamage       = finalDamage,
                Mod               = info.Mod,
                Headshot          = headshot && (info.DamageFlags & DF_NO_LOCDAMAGE) == 0,
                FriendlyFire      = ff,
                WillKill          = finalDamage >= info.TargetHealth,
                Location          = LocationFromRaw(info.Location),
            };

            OnDamageResolved?.Invoke(outcome);

            // 4. Death events.
            if (!outcome.WillKill) return;

            bool suicide  = info.AttackerEntityNum == info.TargetEntityNum
                         || info.AttackerEntityNum == GameConst.ENTITYNUM_WORLD;
            bool teamKill = !suicide && ff;

            var death = new DeathInfo
            {
                VictimEntityNum   = info.TargetEntityNum,
                AttackerEntityNum = info.AttackerEntityNum,
                Mod               = info.Mod,
                Headshot          = outcome.Headshot,
                Suicide           = suicide,
                TeamKill          = teamKill,
            };

            OnPlayerDeath?.Invoke(death);

            if (!suicide)
            {
                int xp = teamKill
                    ? XP_TEAMKILL_PENALTY
                    : GetKillXP(info.VictimClass) + (outcome.Headshot ? XP_HEADSHOT_BONUS : 0);

                var kill = new KillInfo
                {
                    KillerEntityNum = info.AttackerEntityNum,
                    VictimEntityNum = info.TargetEntityNum,
                    Mod             = info.Mod,
                    Headshot        = outcome.Headshot,
                    XpAwarded       = xp,
                };

                OnKill?.Invoke(kill);
            }
        }

        // ------------------------------------------------------------------
        // Bleed-out timer (G_CheckForBleedOut)
        // ------------------------------------------------------------------

        /// <summary>
        /// Call from the game frame loop for each wounded (downed but not yet gibbed) player.
        /// Raises OnPlayerDeath when the 30-second bleed-out window has elapsed.
        /// </summary>
        /// <param name="entityNum">Entity index of the downed player.</param>
        /// <param name="health">Current health (must be &lt;= 0 and &gt; GIB_HEALTH to be wounded).</param>
        /// <param name="woundTime">Server time (ms) when the player was downed.</param>
        /// <param name="currentTime">Current server time (ms).</param>
        public static void G_CheckForBleedOut(int entityNum, int health, int woundTime, int currentTime)
        {
            // Only applies to wounded players, not gibbed ones.
            if (health > 0 || health <= GIB_HEALTH) return;

            if (currentTime - woundTime <= BLEEDOUT_MS) return;

            var death = new DeathInfo
            {
                VictimEntityNum   = entityNum,
                AttackerEntityNum = GameConst.ENTITYNUM_WORLD,
                Mod               = 0,      // MOD_UNKNOWN / bleed-out
                Headshot          = false,
                Suicide           = false,
                TeamKill          = false,
            };

            OnPlayerDeath?.Invoke(death);
        }

        // ------------------------------------------------------------------
        // Internal helpers
        // ------------------------------------------------------------------

        private static HitLocation LocationFromRaw(int raw)
        {
            if (raw >= 0 && raw <= (int)HitLocation.Foot)
                return (HitLocation)raw;
            return HitLocation.Body;
        }
    }
}
