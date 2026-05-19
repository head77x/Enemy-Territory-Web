// Ported from: src/game/bg_misc.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using UnityEngine;
using ET.Network;

namespace ET.Game
{
    /// <summary>
    /// Static utility functions ported from bg_misc.c.
    /// BG_ (both-game) functions are pure data transforms with no I/O or server state.
    /// </summary>
    public static class GameMisc
    {
        // -------------------------------------------------------
        // Local constants not yet in GameConst
        // -------------------------------------------------------

        // Gravity (DEFAULT_GRAVITY in q_shared.h)
        private const float DEFAULT_GRAVITY = 800f;

        // Health threshold below which a player is gibbed / treated as invisible
        private const int GIB_HEALTH = -40;

        // Circular event ring size (MAX_EVENTS in bg_public.h)
        private const int MAX_EVENTS = 4;

        // Powerup array size (MAX_POWERUPS in bg_public.h)
        private const int MAX_POWERUPS = 16;

        // Powerup index for OPS disguise (PW_OPS_DISGUISED)
        private const int PW_OPS_DISGUISED = 12;


        // EntityType int values (matching EntityType enum in EntityState.cs)
        private const int ET_PLAYER    = (int)EntityType.Player;
        private const int ET_INVISIBLE = (int)EntityType.Invisible;

        // TrajectoryType int values (matching TrajectoryType enum in EntityState.cs)
        private const int TR_STATIONARY    = (int)TrajectoryType.Stationary;
        private const int TR_INTERPOLATE   = (int)TrajectoryType.Interpolate;
        private const int TR_LINEAR        = (int)TrajectoryType.Linear;
        private const int TR_LINEAR_STOP   = (int)TrajectoryType.LinearStop;
        private const int TR_SINE          = (int)TrajectoryType.Sine;
        private const int TR_GRAVITY       = (int)TrajectoryType.Gravity;
        private const int TR_GRAVITY_LOW   = (int)TrajectoryType.GravityLow;
        private const int TR_GRAVITY_FLOAT = (int)TrajectoryType.GravityFloat;
        private const int TR_GRAVITY_PAUSED= (int)TrajectoryType.GravityPaused;
        private const int TR_ACCELERATE    = (int)TrajectoryType.Accelerate;
        private const int TR_DECCELERATE   = (int)TrajectoryType.Decelerate;
        // TR_SPLINE and TR_LINEAR_PATH are skipped (spline path not ported)

        // -------------------------------------------------------
        // BG_AddPredictableEventToPlayerstate
        // -------------------------------------------------------

        /// <summary>
        /// Appends a new predictable event to the player-state event ring.
        /// Ported from BG_AddPredictableEventToPlayerstate (bg_misc.c:3982).
        /// </summary>
        public static void AddPredictableEventToPlayerstate(int newEvent, int eventParm, PlayerState ps)
        {
            ps.Events[ps.EventSequence & (MAX_EVENTS - 1)]     = newEvent;
            ps.EventParms[ps.EventSequence & (MAX_EVENTS - 1)] = eventParm;
            ps.EventSequence++;
        }

        // -------------------------------------------------------
        // BG_PlayerStateToEntityState
        // -------------------------------------------------------

        /// <summary>
        /// Converts a playerState_t to an entityState_t after each pmove.
        /// Ported from BG_PlayerStateToEntityState (bg_misc.c:4029).
        /// </summary>
        /// <param name="ps">Source player state (may be mutated for EFlags/EntityEventSequence).</param>
        /// <param name="s">Destination entity state.</param>
        /// <param name="snap">When true, snap position/angle bases to integer grid.</param>
        public static void PlayerStateToEntityState(PlayerState ps, EntityState s, bool snap)
        {
            // Entity type
            if (ps.PmType == GameConst.PM_INTERMISSION || ps.PmType == GameConst.PM_SPECTATOR)
                s.EType = ET_INVISIBLE;
            else if (ps.Stats[GameConst.STAT_HEALTH] <= GIB_HEALTH)
                s.EType = ET_INVISIBLE;
            else
                s.EType = ET_PLAYER;

            s.Number    = ps.ClientNum;

            // Position trajectory — interpolate
            s.Pos.TrType = TR_INTERPOLATE;
            s.Pos.TrBase0 = ps.Origin0;
            s.Pos.TrBase1 = ps.Origin1;
            s.Pos.TrBase2 = ps.Origin2;
            if (snap)
            {
                s.Pos.TrBase0 = Mathf.Round(s.Pos.TrBase0);
                s.Pos.TrBase1 = Mathf.Round(s.Pos.TrBase1);
                s.Pos.TrBase2 = Mathf.Round(s.Pos.TrBase2);
            }

            // Angular trajectory — interpolate
            s.APos.TrType  = TR_INTERPOLATE;
            s.APos.TrBase0 = ps.ViewAngles0;
            s.APos.TrBase1 = ps.ViewAngles1;
            s.APos.TrBase2 = ps.ViewAngles2;
            if (snap)
            {
                s.APos.TrBase0 = Mathf.Round(s.APos.TrBase0);
                s.APos.TrBase1 = Mathf.Round(s.APos.TrBase1);
                s.APos.TrBase2 = Mathf.Round(s.APos.TrBase2);
            }

            // Movement direction is stored in angles2[YAW] (index 1 = YAW)
            // Original: if movementDir > 128 → angles2[YAW] = movementDir - 256
            // (handles signed representation packed into a byte)
            // We don't have a separate Angles2 on EntityState; store in APos unused channel
            // NOTE: the C original writes to s->angles2[YAW] which is a separate angles2 vec3.
            // EntityState doesn't have Angles2 as a named field – the closest is Angles20/21/22.
            if (ps.MovementDir > 128)
                s.Angles21 = (float)(ps.MovementDir - 256);  // YAW component of angles2
            else
                s.Angles21 = ps.MovementDir;

            s.LegsAnim  = ps.LegsAnim;
            s.TorsoAnim = ps.TorsoAnim;
            s.ClientNum = ps.ClientNum;

            // Mounted gun EFlags fixup (SETUP_MOUNTEDGUN_STATUS macro)
            SetupMountedGunStatus(ps);

            s.EFlags = ps.EFlags;

            // Dead flag
            if (ps.Stats[GameConst.STAT_HEALTH] <= 0)
                s.EFlags |=  GameConst.EF_DEAD;
            else
                s.EFlags &= ~GameConst.EF_DEAD;

            // External / entity events
            if (ps.ExternalEvent != 0)
            {
                s.Event     = ps.ExternalEvent;
                s.EventParm = ps.ExternalEventParm;
            }
            else if (ps.EntityEventSequence < ps.EventSequence)
            {
                if (ps.EntityEventSequence < ps.EventSequence - MAX_EVENTS)
                    ps.EntityEventSequence = ps.EventSequence - MAX_EVENTS;

                int seq = ps.EntityEventSequence & (MAX_EVENTS - 1);
                s.Event     = ps.Events[seq] | ((ps.EntityEventSequence & 3) << 8);
                s.EventParm = ps.EventParms[seq];
                ps.EntityEventSequence++;
            }

            // Circular event list: copy new events from ps into s
            for (int i = ps.OldEventSequence; i != ps.EventSequence; i++)
            {
                s.Events[s.EventSequence & (MAX_EVENTS - 1)]     = ps.Events[i & (MAX_EVENTS - 1)];
                s.EventParms[s.EventSequence & (MAX_EVENTS - 1)] = ps.EventParms[i & (MAX_EVENTS - 1)];
                s.EventSequence++;
            }
            ps.OldEventSequence = ps.EventSequence;

            s.Weapon          = ps.Weapon;
            s.GroundEntityNum = ps.GroundEntityNum;

            // Powerup bitmask — one bit per active powerup
            s.Powerups = 0;
            for (int i = 0; i < MAX_POWERUPS; i++)
            {
                if (ps.Powerups[i] != 0)
                    s.Powerups |= 1 << i;
            }

            s.NextWeapon = ps.NextWeapon;
            s.TeamNum    = ps.TeamNum;
            s.AiState    = ps.AiState;
        }

        // -------------------------------------------------------
        // BG_PlayerStateToEntityStateExtraPolate
        // -------------------------------------------------------

        /// <summary>
        /// Extrapolating version of PlayerStateToEntityState — sets up TR_LINEAR_STOP
        /// so the entity position can be forward-predicted up to 50 ms.
        /// Ported from BG_PlayerStateToEntityStateExtraPolate (bg_misc.c:4132).
        /// </summary>
        /// <param name="ps">Source player state (may be mutated).</param>
        /// <param name="s">Destination entity state.</param>
        /// <param name="time">Current server time, used as trTime for linear prediction.</param>
        /// <param name="snap">When true, snap position base to integer grid.</param>
        public static void PlayerStateToEntityStateExtraPolate(PlayerState ps, EntityState s, int time, bool snap)
        {
            // Entity type
            if (ps.PmType == GameConst.PM_INTERMISSION || ps.PmType == GameConst.PM_SPECTATOR)
                s.EType = ET_INVISIBLE;
            else if (ps.Stats[GameConst.STAT_HEALTH] <= GIB_HEALTH)
                s.EType = ET_INVISIBLE;
            else
                s.EType = ET_PLAYER;

            s.Number = ps.ClientNum;

            // Position — linear-stop so clients can extrapolate up to trDuration ms
            s.Pos.TrType = TR_LINEAR_STOP;
            s.Pos.TrBase0 = ps.Origin0;
            s.Pos.TrBase1 = ps.Origin1;
            s.Pos.TrBase2 = ps.Origin2;
            if (snap)
            {
                s.Pos.TrBase0 = Mathf.Round(s.Pos.TrBase0);
                s.Pos.TrBase1 = Mathf.Round(s.Pos.TrBase1);
                s.Pos.TrBase2 = Mathf.Round(s.Pos.TrBase2);
            }
            // Velocity delta for linear prediction
            s.Pos.TrDelta0 = ps.Velocity0;
            s.Pos.TrDelta1 = ps.Velocity1;
            s.Pos.TrDelta2 = ps.Velocity2;
            s.Pos.TrTime     = time;
            s.Pos.TrDuration = 50;   // 1000 / sv_fps (default sv_fps = 20)

            // Angular trajectory — interpolate only
            s.APos.TrType  = TR_INTERPOLATE;
            s.APos.TrBase0 = ps.ViewAngles0;
            s.APos.TrBase1 = ps.ViewAngles1;
            s.APos.TrBase2 = ps.ViewAngles2;
            if (snap)
            {
                s.APos.TrBase0 = Mathf.Round(s.APos.TrBase0);
                s.APos.TrBase1 = Mathf.Round(s.APos.TrBase1);
                s.APos.TrBase2 = Mathf.Round(s.APos.TrBase2);
            }

            // Movement direction (no >128 correction in the extrapolate variant)
            s.Angles21 = ps.MovementDir;

            s.LegsAnim  = ps.LegsAnim;
            s.TorsoAnim = ps.TorsoAnim;
            s.ClientNum = ps.ClientNum;

            // Mounted gun EFlags fixup
            SetupMountedGunStatus(ps);

            s.EFlags = ps.EFlags;
            if (ps.Stats[GameConst.STAT_HEALTH] <= 0)
                s.EFlags |=  GameConst.EF_DEAD;
            else
                s.EFlags &= ~GameConst.EF_DEAD;

            // External / entity events
            if (ps.ExternalEvent != 0)
            {
                s.Event     = ps.ExternalEvent;
                s.EventParm = ps.ExternalEventParm;
            }
            else if (ps.EntityEventSequence < ps.EventSequence)
            {
                if (ps.EntityEventSequence < ps.EventSequence - MAX_EVENTS)
                    ps.EntityEventSequence = ps.EventSequence - MAX_EVENTS;

                int seq = ps.EntityEventSequence & (MAX_EVENTS - 1);
                s.Event     = ps.Events[seq] | ((ps.EntityEventSequence & 3) << 8);
                s.EventParm = ps.EventParms[seq];
                ps.EntityEventSequence++;
            }

            // Circular event list
            for (int i = ps.OldEventSequence; i != ps.EventSequence; i++)
            {
                s.Events[s.EventSequence & (MAX_EVENTS - 1)]     = ps.Events[i & (MAX_EVENTS - 1)];
                s.EventParms[s.EventSequence & (MAX_EVENTS - 1)] = ps.EventParms[i & (MAX_EVENTS - 1)];
                s.EventSequence++;
            }
            ps.OldEventSequence = ps.EventSequence;

            s.Weapon          = ps.Weapon;
            s.GroundEntityNum = ps.GroundEntityNum;

            s.Powerups = 0;
            for (int i = 0; i < MAX_POWERUPS; i++)
            {
                if (ps.Powerups[i] != 0)
                    s.Powerups |= 1 << i;
            }

            s.NextWeapon = ps.NextWeapon;
            s.TeamNum    = ps.TeamNum;
            s.AiState    = ps.AiState;
        }

        // -------------------------------------------------------
        // BG_EvaluateTrajectory
        // -------------------------------------------------------

        /// <summary>
        /// Evaluates a trajectory's world position at <paramref name="atTime"/>.
        /// Spline types (TR_SPLINE, TR_LINEAR_PATH) are not implemented — returns
        /// the base position unchanged for those cases.
        /// Ported from BG_EvaluateTrajectory (bg_misc.c:3461).
        /// </summary>
        public static Vector3 EvaluateTrajectory(Trajectory tr, int atTime)
        {
            float deltaTime, phase;
            Vector3 trBase  = new Vector3(tr.TrBase0,  tr.TrBase1,  tr.TrBase2);
            Vector3 trDelta = new Vector3(tr.TrDelta0, tr.TrDelta1, tr.TrDelta2);

            switch (tr.TrType)
            {
                case TR_STATIONARY:
                case TR_INTERPOLATE:
                case TR_GRAVITY_PAUSED:
                    return trBase;

                case TR_LINEAR:
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    return trBase + trDelta * deltaTime;

                case TR_SINE:
                    deltaTime = (atTime - tr.TrTime) / (float)tr.TrDuration;
                    phase     = Mathf.Sin(deltaTime * Mathf.PI * 2f);
                    return trBase + trDelta * phase;

                case TR_LINEAR_STOP:
                {
                    int clampedTime = atTime;
                    if (clampedTime > tr.TrTime + tr.TrDuration)
                        clampedTime = tr.TrTime + tr.TrDuration;
                    deltaTime = (clampedTime - tr.TrTime) * 0.001f;
                    if (deltaTime < 0f) deltaTime = 0f;
                    return trBase + trDelta * deltaTime;
                }

                case TR_GRAVITY:
                {
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    Vector3 result = trBase + trDelta * deltaTime;
                    result.z -= 0.5f * DEFAULT_GRAVITY * deltaTime * deltaTime;
                    return result;
                }

                case TR_GRAVITY_LOW:
                {
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    Vector3 result = trBase + trDelta * deltaTime;
                    result.z -= 0.5f * (DEFAULT_GRAVITY * 0.3f) * deltaTime * deltaTime;
                    return result;
                }

                case TR_GRAVITY_FLOAT:
                {
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    Vector3 result = trBase + trDelta * deltaTime;
                    result.z -= 0.5f * (DEFAULT_GRAVITY * 0.2f) * deltaTime;
                    return result;
                }

                case TR_ACCELERATE:
                {
                    // trDelta is the ultimate speed; accelerate toward it
                    int clampedTime = atTime;
                    if (clampedTime > tr.TrTime + tr.TrDuration)
                        clampedTime = tr.TrTime + tr.TrDuration;
                    deltaTime = (clampedTime - tr.TrTime) * 0.001f;
                    phase     = trDelta.magnitude / (tr.TrDuration * 0.001f);   // acceleration constant
                    Vector3 dir = trDelta.normalized;
                    return trBase + dir * (phase * 0.5f * deltaTime * deltaTime);
                }

                case TR_DECCELERATE:
                {
                    // trDelta is starting speed; apply braking
                    int clampedTime = atTime;
                    if (clampedTime > tr.TrTime + tr.TrDuration)
                        clampedTime = tr.TrTime + tr.TrDuration;
                    deltaTime = (clampedTime - tr.TrTime) * 0.001f;
                    phase     = trDelta.magnitude / (tr.TrDuration * 0.001f);   // breaking constant
                    Vector3 dir = trDelta.normalized;
                    // distance without braking minus braking force
                    Vector3 v = trBase + trDelta * deltaTime;
                    return v - dir * (phase * 0.5f * deltaTime * deltaTime);
                }

                // Spline types: not implemented — fall back to base position
                default:
                    return trBase;
            }
        }

        // -------------------------------------------------------
        // BG_EvaluateTrajectoryDelta
        // -------------------------------------------------------

        /// <summary>
        /// Returns the instantaneous velocity vector for a trajectory at <paramref name="atTime"/>.
        /// Spline types return Vector3.zero.
        /// Ported from BG_EvaluateTrajectoryDelta (bg_misc.c:3713).
        /// </summary>
        public static Vector3 EvaluateTrajectoryDelta(Trajectory tr, int atTime)
        {
            float deltaTime, phase;
            Vector3 trDelta = new Vector3(tr.TrDelta0, tr.TrDelta1, tr.TrDelta2);

            switch (tr.TrType)
            {
                case TR_STATIONARY:
                case TR_INTERPOLATE:
                    return Vector3.zero;

                case TR_LINEAR:
                    return trDelta;

                case TR_SINE:
                    deltaTime = (atTime - tr.TrTime) / (float)tr.TrDuration;
                    phase     = Mathf.Cos(deltaTime * Mathf.PI * 2f) * 0.5f;  // derivative of sin = cos
                    return trDelta * phase;

                case TR_LINEAR_STOP:
                    if (atTime > tr.TrTime + tr.TrDuration)
                        return Vector3.zero;
                    return trDelta;

                case TR_GRAVITY:
                {
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    Vector3 result = trDelta;
                    result.z -= DEFAULT_GRAVITY * deltaTime;
                    return result;
                }

                case TR_GRAVITY_LOW:
                {
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    Vector3 result = trDelta;
                    result.z -= (DEFAULT_GRAVITY * 0.3f) * deltaTime;
                    return result;
                }

                case TR_GRAVITY_FLOAT:
                {
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    Vector3 result = trDelta;
                    result.z -= (DEFAULT_GRAVITY * 0.2f) * deltaTime;
                    return result;
                }

                case TR_ACCELERATE:
                {
                    if (atTime > tr.TrTime + tr.TrDuration)
                        return Vector3.zero;
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    // velocity = acceleration * time; scale trDelta by deltaTime^2
                    return trDelta * (deltaTime * deltaTime);
                }

                case TR_DECCELERATE:
                {
                    if (atTime > tr.TrTime + tr.TrDuration)
                        return Vector3.zero;
                    deltaTime = (atTime - tr.TrTime) * 0.001f;
                    return trDelta * deltaTime;
                }

                // Spline types: velocity not calculable without spline data
                default:
                    return Vector3.zero;
            }
        }

        // -------------------------------------------------------
        // BG_IsAkimboWeapon
        // -------------------------------------------------------

        /// <summary>
        /// Returns true if the weapon ID is an akimbo (dual-pistol) variant.
        /// Ported from BG_IsAkimboWeapon (bg_misc.c:2672).
        /// </summary>
        public static bool IsAkimboWeapon(int weaponNum)
        {
            return weaponNum == GameConst.WP_AKIMBO_COLT
                || weaponNum == GameConst.WP_AKIMBO_SILENCEDCOLT
                || weaponNum == GameConst.WP_AKIMBO_LUGER
                || weaponNum == GameConst.WP_AKIMBO_SILENCEDLUGER;
        }

        // -------------------------------------------------------
        // BG_AkimboFireSequence
        // -------------------------------------------------------

        /// <summary>
        /// Returns true if it is the left hand's (akimbo clip) turn to fire,
        /// false if it is the right hand's (main clip) turn.
        /// Ported from BG_AkimboFireSequence (bg_misc.c:2643).
        /// </summary>
        /// <param name="weapon">Current weapon ID.</param>
        /// <param name="akimboClip">Remaining rounds in akimbo (off-hand) magazine.</param>
        /// <param name="mainClip">Remaining rounds in main magazine.</param>
        public static bool AkimboFireSequence(int weapon, int akimboClip, int mainClip)
        {
            if (!IsAkimboWeapon(weapon))
                return false;

            if (akimboClip == 0)
                return false;

            // No ammo in main weapon — must use akimbo
            if (mainClip == 0)
                return true;

            // Both have ammo: alternate based on combined count parity
            return ((akimboClip + mainClip) & 1) == 0;
        }

        // -------------------------------------------------------
        // BG_AkimboSidearm
        // -------------------------------------------------------

        /// <summary>
        /// Returns the single-handed sidearm weapon ID for an akimbo weapon,
        /// or WP_NONE if the weapon is not an akimbo type.
        /// Ported from BG_AkimboSidearm (bg_misc.c:2708).
        /// </summary>
        public static int AkimboSidearm(int weaponNum)
        {
            if (weaponNum == GameConst.WP_AKIMBO_COLT || weaponNum == GameConst.WP_AKIMBO_SILENCEDCOLT)
                return GameConst.WP_COLT;

            if (weaponNum == GameConst.WP_AKIMBO_LUGER || weaponNum == GameConst.WP_AKIMBO_SILENCEDLUGER)
                return GameConst.WP_LUGER;

            return GameConst.WP_NONE;
        }

        // -------------------------------------------------------
        // BG_WeaponInWolfMP
        // -------------------------------------------------------

        /// <summary>
        /// Returns true if the weapon is a valid multiplayer weapon in Wolf:ET.
        /// Ported from BG_WeaponInWolfMP (bg_misc.c:2815).
        /// </summary>
        public static bool WeaponInWolfMP(int weapon)
        {
            return weapon == GameConst.WP_KNIFE
                || weapon == GameConst.WP_LUGER
                || weapon == GameConst.WP_COLT
                || weapon == GameConst.WP_MP40
                || weapon == GameConst.WP_THOMPSON
                || weapon == GameConst.WP_STEN
                || weapon == GameConst.WP_GRENADE_LAUNCHER
                || weapon == GameConst.WP_GRENADE_PINEAPPLE
                || weapon == GameConst.WP_PANZERFAUST
                || weapon == GameConst.WP_FLAMETHROWER
                || weapon == GameConst.WP_AMMO
                || weapon == GameConst.WP_ARTY
                || weapon == GameConst.WP_SMOKETRAIL
                || weapon == GameConst.WP_MEDKIT
                || weapon == GameConst.WP_PLIERS
                || weapon == GameConst.WP_SMOKE_MARKER
                || weapon == GameConst.WP_DYNAMITE
                || weapon == GameConst.WP_MEDIC_SYRINGE
                || weapon == GameConst.WP_MEDIC_ADRENALINE
                || weapon == GameConst.WP_BINOCULARS
                || weapon == GameConst.WP_KAR98
                || weapon == GameConst.WP_GPG40
                || weapon == GameConst.WP_CARBINE
                || weapon == GameConst.WP_M7
                || weapon == GameConst.WP_GARAND
                || weapon == GameConst.WP_GARAND_SCOPE
                || weapon == GameConst.WP_FG42
                || weapon == GameConst.WP_FG42SCOPE
                || weapon == GameConst.WP_LANDMINE
                || weapon == GameConst.WP_SATCHEL
                || weapon == GameConst.WP_SATCHEL_DET
                || weapon == GameConst.WP_SMOKE_BOMB
                || weapon == GameConst.WP_MOBILE_MG42
                || weapon == GameConst.WP_MOBILE_MG42_SET
                || weapon == GameConst.WP_SILENCER
                || weapon == GameConst.WP_SILENCED_COLT
                || weapon == GameConst.WP_K43
                || weapon == GameConst.WP_K43_SCOPE
                || weapon == GameConst.WP_MORTAR
                || weapon == GameConst.WP_MORTAR_SET
                || weapon == GameConst.WP_AKIMBO_LUGER
                || weapon == GameConst.WP_AKIMBO_SILENCEDLUGER
                || weapon == GameConst.WP_AKIMBO_COLT
                || weapon == GameConst.WP_AKIMBO_SILENCEDCOLT;
        }

        // -------------------------------------------------------
        // BG_FootstepForSurface
        // -------------------------------------------------------

        /// <summary>
        /// Returns the footstep event constant (GameEvent.FOOTSTEP_*) for the
        /// given surface flags, or GameEvent.NONE if the surface suppresses steps.
        /// Ported from BG_FootstepForSurface (bg_misc.c:5265).
        /// </summary>
        /// <param name="surfaceFlags">Surface flags (see <see cref="Surf"/>).</param>
        /// <returns>A GameEvent.FOOTSTEP_* constant, GameEvent.FOOTSTEP (normal), or GameEvent.NONE.</returns>
        public static int FootstepForSurface(int surfaceFlags)
        {
            // SURF_NOSTEPS — surface explicitly suppresses footstep sounds
            if ((surfaceFlags & Surf.NoSteps) != 0)
                return GameEvent.NONE;      // FOOTSTEP_TOTAL in C: means "no step sound"

            if ((surfaceFlags & Surf.Metal)   != 0) return GameEvent.FOOTSTEP_METAL;
            if ((surfaceFlags & Surf.Wood)    != 0) return GameEvent.FOOTSTEP_WOOD;
            if ((surfaceFlags & Surf.Grass)   != 0) return GameEvent.FOOTSTEP_GRASS;
            if ((surfaceFlags & Surf.Gravel)  != 0) return GameEvent.FOOTSTEP_GRAVEL;
            if ((surfaceFlags & Surf.Roof)    != 0) return GameEvent.FOOTSTEP_ROOF;
            if ((surfaceFlags & Surf.Snow)    != 0) return GameEvent.FOOTSTEP_SNOW;
            if ((surfaceFlags & Surf.Carpet)  != 0) return GameEvent.FOOTSTEP_CARPET;
            if ((surfaceFlags & Surf.Splash)  != 0) return GameEvent.NONE;   // splash handled separately

            return GameEvent.FOOTSTEP;   // FOOTSTEP_NORMAL — default concrete/dirt sound
        }

        // -------------------------------------------------------
        // Stubs (depend on item data / server state not yet ported)
        // -------------------------------------------------------

        /// <summary>Stub: finds the item for a weapon. Returns null until item data is ported.</summary>
        public static object FindItemForWeapon(int weapon) => null;

        /// <summary>Stub: returns true if the player can use the weapon. Always true until class/loadout logic is ported.</summary>
        public static bool CanUseWeapon(int weapon) => true;

        /// <summary>Stub: returns false until pickup radius logic is ported.</summary>
        public static bool PlayerTouchesItem(PlayerState ps, EntityState item, int atTime) => false;

        // -------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------

        /// <summary>
        /// In-place equivalent of the SETUP_MOUNTEDGUN_STATUS macro (bg_misc.c:4003).
        /// Mutates ps.EFlags to reflect the current mounted-weapon state.
        /// </summary>
        private static void SetupMountedGunStatus(PlayerState ps)
        {
            if ((ps.EFlags & GameConst.EF_MOUNTEDTANK) != 0)
            {
                // On a tank — neither individual gun flag applies
                ps.EFlags &= ~GameConst.EF_MG42_ACTIVE;
                ps.EFlags &= ~GameConst.EF_AAGUN_ACTIVE;
                return;
            }

            switch (ps.Persistant[GameConst.PERS_HWEAPON_USE])
            {
                case 1:
                    ps.EFlags |=  GameConst.EF_MG42_ACTIVE;
                    ps.EFlags &= ~GameConst.EF_AAGUN_ACTIVE;
                    ps.Powerups[PW_OPS_DISGUISED] = 0;
                    break;
                case 2:
                    ps.EFlags |=  GameConst.EF_AAGUN_ACTIVE;
                    ps.EFlags &= ~GameConst.EF_MG42_ACTIVE;
                    ps.Powerups[PW_OPS_DISGUISED] = 0;
                    break;
                default:
                    ps.EFlags &= ~GameConst.EF_MG42_ACTIVE;
                    ps.EFlags &= ~GameConst.EF_AAGUN_ACTIVE;
                    break;
            }
        }
    }
}
