// Ported from: src/game/bg_pmove.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Core;
using ET.Network;

namespace ET.Game
{
    public partial class PlayerMovement
    {
        // -----------------------------------------------------------------------
        // Event ID constants (subset used by movement code)
        // -----------------------------------------------------------------------
        private const int EV_JUMP          = 17;
        private const int EV_FOOTSTEP      = 18;
        private const int EV_FOOTSPLASH    = 19;
        private const int EV_SWIM          = 20;
        private const int EV_STEP_4        = 21;
        private const int EV_STEP_8        = 22;
        private const int EV_STEP_12       = 23;
        private const int EV_STEP_16       = 24;
        private const int EV_FALL_SHORT    = 25;
        private const int EV_FALL_DMG_10   = 26;
        private const int EV_FALL_DMG_15   = 27;
        private const int EV_FALL_DMG_25   = 28;
        private const int EV_FALL_DMG_50   = 29;
        private const int EV_FALL_NDIE     = 30;
        private const int EV_WATER_TOUCH   = 31;
        private const int EV_WATER_LEAVE   = 32;
        private const int EV_WATER_UNDER   = 33;
        private const int EV_WATER_CLEAR   = 34;
        private const int EV_NOAMMO        = 35;

        // Prone-leg bounding box (playerlegsProneMins / playerlegsProneMaxs equivalents)
        private static readonly Vector3 PlayerLegsProneMins = new Vector3(-8f, -8f, -2f);
        private static readonly Vector3 PlayerLegsProneMaxs = new Vector3( 8f,  8f,  2f);

        // -----------------------------------------------------------------------
        // Movement parameters (module-level constants, matching C globals)
        // -----------------------------------------------------------------------
        private const float PM_StopSpeed         = 100f;
        private const float PM_WaterSwimScale    = 0.50f;
        private const float PM_SlagSwimScale     = 0.30f;
        private const float PM_ProneSpeedScale   = 0.21f;
        private const float PM_FlyAccelerate     = 8f;
        private const float PM_AirAccelerate     = 1f;
        private const float PM_WaterAccelerate   = 4f;
        private const float PM_SlagAccelerate    = 2f;
        private const float PM_FrictionVal       = 6f;
        private const float PM_WaterFriction     = 1f;
        private const float PM_SlagFriction      = 1f;
        private const float PM_LadderFriction    = 14f;
        private const float PM_SpectatorFriction = 5f;
        private const float PM_AccelerateVal     = 10f;

        // Lean constants
        private const float LeanMax    = 28f;
        private const float LeanTimeTo = 200f;
        private const float LeanTimeFr = 300f;

        // Sprint bar maximum (SPRINTTIME)
        private const int SprintTimeMax = 20000;

        // Weapon anim IDs (WEAP_*)
        private const int WEAP_IDLE1           = 0;
        private const int WEAP_IDLE2           = 1;
        private const int WEAP_ATTACK1         = 2;
        private const int WEAP_ATTACK2         = 3;
        private const int WEAP_ATTACK_LASTSHOT = 4;
        private const int WEAP_DROP            = 5;
        private const int WEAP_DROP2           = 6;
        private const int WEAP_RAISE           = 7;
        private const int WEAP_RELOAD1         = 8;
        private const int WEAP_RELOAD2         = 9;
        private const int WEAP_RELOAD3         = 10;
        private const int WEAP_ALTSWITCHFROM   = 11;
        private const int WEAP_ALTSWITCHTO     = 12;

        // Local aliases for GameConst weapon IDs (avoids GameConst. prefix in switch/case)
        private const int WP_GPG40             = GameConst.WP_GPG40;             // 39
        private const int WP_M7                = GameConst.WP_M7;                // 40
        private const int WP_MEDIC_ADRENALINE  = GameConst.WP_MEDIC_ADRENALINE; // 46
        private const int WP_DYNAMITE          = GameConst.WP_DYNAMITE;          // 15
        private const int WP_GRENADE_LAUNCHER  = GameConst.WP_GRENADE_LAUNCHER; // 4
        private const int WP_GRENADE_PINEAPPLE = GameConst.WP_GRENADE_PINEAPPLE;// 9

        // Local aliases for GameConst EFlags
        private const int EF_DEAD        = GameConst.EF_DEAD;        // 0x00000001
        private const int EF_BREATH      = GameConst.EF_BREATH;      // 0x00000100
        private const int EF_MOUNTEDTANK = GameConst.EF_MOUNTEDTANK; // 0x00001000
        private const int EF_MG42_ACTIVE = GameConst.EF_MG42_ACTIVE; // 0x00000020
        private const int EF_AAGUN_ACTIVE= GameConst.EF_AAGUN_ACTIVE;// 0x00010000
        private const int EF_ZOOMING_INT = GameConst.EF_ZOOMING;     // 0x00040000

        // Local aliases for stat/inv/skill indices
        private const int STAT_KEYS       = GameConst.STAT_KEYS;       // 9
        private const int INV_BINOCS      = GameConst.INV_BINOCS;      // 1
        private const int SK_BATTLE_SENSE = GameConst.SK_BATTLE_SENSE; // 0

        // Powerup indices
        private const int PW_ADRENALINE = 5;
        private const int PW_NOFATIGUE  = 6;

        // Weapon state
        private const int WEAPON_FIRING  = 3;

        // MG42
        private const float MG42_YAWSPEED = 300f;

        // -----------------------------------------------------------------------
        // Instance state
        // -----------------------------------------------------------------------
        private PmoveInput  _pm  = null!;
        private PmoveLocal  _pml;

        // Ladder state (file-scope in C)
        private bool    _ladderForward;
        private Vector3 _ladderVec;

        // -----------------------------------------------------------------------
        // AddEvent — BG_AddPredictableEventToPlayerstate stub
        // -----------------------------------------------------------------------
        private void AddEvent(int eventId, int parm = 0)
        {
            var ps  = _pm.Ps;
            int seq = ps.EventSequence & 3;
            ps.Events[seq]     = eventId;
            ps.EventParms[seq] = parm;
            ps.EventSequence   = (ps.EventSequence + 1) & 0xFF;
        }

        // -----------------------------------------------------------------------
        // Stubs for BG helpers that cross layer boundaries
        // -----------------------------------------------------------------------
        private static bool BG_IsLightWeaponSupportingFastReload(int weapon) => false;
        private static bool BG_IsScopedWeapon(int weapon) => false;
        private static bool BG_PlayerMounted(int eFlags)
            => (eFlags & (EF_MG42_ACTIVE | EF_AAGUN_ACTIVE | EF_MOUNTEDTANK)) != 0;

        private int BG_FootstepForSurface(int surfaceFlags)
        {
            if ((surfaceFlags & Surf.NoSteps) != 0) return 0;
            if ((surfaceFlags & Surf.Metal)   != 0) return 1;
            if ((surfaceFlags & Surf.Wood)    != 0) return 2;
            if ((surfaceFlags & Surf.Grass)   != 0) return 3;
            if ((surfaceFlags & Surf.Gravel)  != 0) return 4;
            if ((surfaceFlags & Surf.Snow)    != 0) return 5;
            if ((surfaceFlags & Surf.Roof)    != 0) return 6;
            if ((surfaceFlags & Surf.Carpet)  != 0) return 7;
            return 0;
        }

        // -----------------------------------------------------------------------
        // Weapon animation helpers
        // -----------------------------------------------------------------------
        private int PM_IdleAnimForWeapon(int weapon)
        {
            switch (weapon)
            {
                case WP_GPG40:
                case WP_M7:
                case GameConst.WP_SATCHEL_DET:
                case GameConst.WP_MORTAR_SET:
                case WP_MEDIC_ADRENALINE:
                case GameConst.WP_MOBILE_MG42_SET:
                    return WEAP_IDLE2;
                default:
                    return WEAP_IDLE1;
            }
        }

        private int PM_AltSwitchFromForWeapon(int weapon) => WEAP_ALTSWITCHFROM;

        private int PM_AltSwitchToForWeapon(int weapon)
        {
            switch (weapon)
            {
                case WP_GPG40:
                case WP_M7:
                case GameConst.WP_MORTAR:
                case GameConst.WP_MOBILE_MG42:
                    return WEAP_ALTSWITCHFROM;
                default:
                    return WEAP_ALTSWITCHTO;
            }
        }

        private int PM_AttackAnimForWeapon(int weapon)
        {
            switch (weapon)
            {
                case WP_GPG40:
                case WP_M7:
                case GameConst.WP_SATCHEL_DET:
                case WP_MEDIC_ADRENALINE:
                case GameConst.WP_MOBILE_MG42_SET:
                    return WEAP_ATTACK2;
                default:
                    return WEAP_ATTACK1;
            }
        }

        private int PM_LastAttackAnimForWeapon(int weapon)
        {
            switch (weapon)
            {
                case WP_GPG40:
                case WP_M7:
                case GameConst.WP_MOBILE_MG42_SET:
                    return WEAP_ATTACK2;
                case GameConst.WP_MORTAR_SET:
                    return WEAP_ATTACK1;
                default:
                    return WEAP_ATTACK_LASTSHOT;
            }
        }

        private int PM_ReloadAnimForWeapon(int weapon)
        {
            switch (weapon)
            {
                case WP_GPG40:
                case WP_M7:
                    return WEAP_RELOAD2;
                case GameConst.WP_MOBILE_MG42_SET:
                    return WEAP_RELOAD3;
                default:
                    return (_pm.Skill[GameConst.SK_LIGHT_WEAPONS] >= 2 &&
                            BG_IsLightWeaponSupportingFastReload(weapon))
                        ? WEAP_RELOAD2
                        : WEAP_RELOAD1;
            }
        }

        private int PM_RaiseAnimForWeapon(int weapon)
        {
            switch (weapon)
            {
                case WP_GPG40:
                case WP_M7:
                    return WEAP_RELOAD3;
                case GameConst.WP_MOBILE_MG42_SET:
                    return WEAP_DROP2;
                case GameConst.WP_SATCHEL_DET:
                    return WEAP_RELOAD2;
                default:
                    return WEAP_RAISE;
            }
        }

        private int PM_DropAnimForWeapon(int weapon)
        {
            switch (weapon)
            {
                case WP_GPG40:
                case WP_M7:
                    return WEAP_DROP2;
                case GameConst.WP_SATCHEL_DET:
                    return WEAP_RELOAD1;
                default:
                    return WEAP_DROP;
            }
        }

        // -----------------------------------------------------------------------
        // PM_StartWeaponAnim / PM_ContinueWeaponAnim
        // -----------------------------------------------------------------------
        private void PM_StartWeaponAnim(int anim)
        {
            if (_pm.Ps.PmType >= GameConst.PM_DEAD) return;
            if (_pm.Pmext.WeapAnimTimer > 0) return;
            if (_pm.Cmd.Weapon == GameConst.WP_NONE) return;
            _pm.Ps.WeapAnim =
                ((_pm.Ps.WeapAnim & GameConst.ANIM_TOGGLEBIT) ^ GameConst.ANIM_TOGGLEBIT) | anim;
        }

        private void PM_ContinueWeaponAnim(int anim)
        {
            if (_pm.Cmd.Weapon == GameConst.WP_NONE) return;
            if ((_pm.Ps.WeapAnim & ~GameConst.ANIM_TOGGLEBIT) == anim) return;
            if (_pm.Pmext.WeapAnimTimer > 0) return;
            PM_StartWeaponAnim(anim);
        }

        // -----------------------------------------------------------------------
        // PM_AddTouchEnt
        // -----------------------------------------------------------------------
        private void PM_AddTouchEnt(int entityNum)
        {
            if (entityNum == GameConst.ENTITYNUM_WORLD) return;
            if (_pm.NumTouch == GameConst.MAXTOUCH) return;
            for (int i = 0; i < _pm.NumTouch; i++)
                if (_pm.TouchEnts[i] == entityNum) return;
            _pm.TouchEnts[_pm.NumTouch++] = entityNum;
        }

        // -----------------------------------------------------------------------
        // Trace helpers
        // -----------------------------------------------------------------------
        private TraceResult PM_TraceAll(Vector3 start, Vector3 end)
        {
            return PM_TraceAllLegs(start, end, out _);
        }

        private TraceResult PM_TraceAllLegs(Vector3 start, Vector3 end, out float legsOffset)
        {
            legsOffset = 0f;
            TraceResult bodyTrace = _pm.Trace(
                start, _pm.Mins, _pm.Maxs, end, _pm.Ps.ClientNum, _pm.TraceMask);

            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                float legOfs;
                Vector3 viewAngles = new Vector3(
                    _pm.Ps.ViewAngles0, _pm.Ps.ViewAngles1, _pm.Ps.ViewAngles2);
                TraceResult legTrace = PM_TraceLegsInternal(
                    start, end, viewAngles, _pm.Ps.ClientNum, _pm.TraceMask,
                    bodyTrace, out legOfs);

                if (legTrace.Fraction < bodyTrace.Fraction ||
                    legTrace.StartSolid || legTrace.AllSolid)
                {
                    legsOffset = legOfs;
                    _pm.Pmext.ProneLegsOffset = legOfs;
                    Vector3 dir = end - start;
                    legTrace.EndPos = start + dir * legTrace.Fraction;
                    return legTrace;
                }
            }
            return bodyTrace;
        }

        private TraceResult PM_TraceLegsInternal(
            Vector3 start, Vector3 end,
            Vector3 viewAngles, int ignoreEnt, int traceMask,
            TraceResult bodyTrace, out float legsOffset)
        {
            legsOffset = 0f;
            int mask = traceMask &
                ~(Contents.Body | Contents.Corpse);

            float angle = viewAngles.y * Mathf.Deg2Rad;
            Vector3 flatforward = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 ofs   = flatforward * -32f;
            Vector3 org   = start + ofs;
            Vector3 point = end   + ofs;

            TraceResult trace = _pm.Trace(
                org, PlayerLegsProneMins, PlayerLegsProneMaxs, point, ignoreEnt, mask);

            bool useBody = trace.Fraction >= bodyTrace.Fraction && !trace.AllSolid;
            if (!useBody)
            {
                Vector3 ofsStep = ofs;
                ofsStep.z += GameConst.STEPSIZE;
                Vector3 orgS   = start + ofsStep;
                Vector3 pointS = end   + ofsStep;
                TraceResult stepTr = _pm.Trace(
                    orgS, PlayerLegsProneMins, PlayerLegsProneMaxs, pointS, ignoreEnt, mask);

                if (!stepTr.AllSolid && !stepTr.StartSolid &&
                    stepTr.Fraction > trace.Fraction)
                {
                    trace      = stepTr;
                    legsOffset = ofsStep.z;

                    Vector3 sp = stepTr.EndPos;
                    Vector3 dp = sp;
                    dp.z -= GameConst.STEPSIZE;
                    TraceResult downTr = _pm.Trace(
                        sp, PlayerLegsProneMins, PlayerLegsProneMaxs, dp, ignoreEnt, mask);
                    if (!downTr.AllSolid)
                        legsOffset = ofsStep.z - (sp.z - downTr.EndPos.z);
                }
            }
            return trace;
        }

        // Convenience overload used by StepSlideMove leg-check
        private TraceResult PM_TraceLegs(
            Vector3 start, Vector3 end,
            Vector3 viewAngles, int ignoreEnt, int traceMask)
        {
            return PM_TraceLegsInternal(
                start, end, viewAngles, ignoreEnt, traceMask,
                default, out _);
        }

        // -----------------------------------------------------------------------
        // PM_Friction
        // -----------------------------------------------------------------------
        private void PM_Friction()
        {
            Vector3 vel = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            Vector3 vec = vel;
            if (_pml.Walking) vec.z = 0f;

            float speed = vec.magnitude;
            if (speed < 1f &&
                _pm.Ps.PmType != GameConst.PM_SPECTATOR &&
                _pm.Ps.PmType != GameConst.PM_NOCLIP)
            {
                _pm.Ps.Velocity0 = 0f;
                _pm.Ps.Velocity1 = 0f;
                return;
            }

            float drop = 0f;

            int dodgeDelta = _pm.Cmd.ServerTime - _pm.Pmext.DodgeTime;
            if (dodgeDelta > 250 && dodgeDelta < 350)
                drop += speed * 20f * _pml.FrameTime;

            if (_pm.WaterLevel <= 1 && _pml.Walking &&
                (_pml.GroundTrace.SurfaceFlags & Surf.Slick) == 0)
            {
                if ((_pm.Ps.PmFlags & GameConst.PMF_TIME_KNOCKBACK) == 0)
                {
                    float control = speed < PM_StopSpeed ? PM_StopSpeed : speed;
                    drop += control * PM_FrictionVal * _pml.FrameTime;
                }
            }

            if (_pm.WaterLevel > 0)
            {
                drop += speed *
                    (_pm.WaterType == Contents.Slime
                        ? PM_SlagFriction : PM_WaterFriction) *
                    _pm.WaterLevel * _pml.FrameTime;
            }

            if (_pm.Ps.PmType == GameConst.PM_SPECTATOR)
                drop += speed * PM_SpectatorFriction * _pml.FrameTime;

            if (_pml.Ladder)
                drop += speed * PM_LadderFriction * _pml.FrameTime;

            float newspeed = speed - drop;
            if (newspeed < 0f) newspeed = 0f;
            newspeed /= speed;

            if ((_pm.Ps.PmType == GameConst.PM_SPECTATOR ||
                 _pm.Ps.PmType == GameConst.PM_NOCLIP) &&
                drop < 1f && speed < 3f)
            {
                newspeed = 0f;
            }

            WriteVelocity(vel * newspeed);
        }

        // -----------------------------------------------------------------------
        // PM_Accelerate
        // -----------------------------------------------------------------------
        private void PM_Accelerate(Vector3 wishdir, float wishspeed, float accel)
        {
            Vector3 vel = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            float currentspeed = Vector3.Dot(vel, wishdir);
            float addspeed = wishspeed - currentspeed;
            if (addspeed <= 0f) return;

            float accelspeed = accel * _pml.FrameTime * wishspeed;
            if (accelspeed > addspeed) accelspeed = addspeed;

            if (_pm.Ps.GroundEntityNum != GameConst.ENTITYNUM_NONE &&
                _pm.Ps.Friction > 0f)
            {
                accelspeed *= 1f / _pm.Ps.Friction;
            }
            if (accelspeed > addspeed) accelspeed = addspeed;

            WriteVelocity(vel + wishdir * accelspeed);
        }

        // -----------------------------------------------------------------------
        // PM_CmdScale
        // -----------------------------------------------------------------------
        private float PM_CmdScale(UserCmd cmd)
        {
            int max = Math.Abs(cmd.ForwardMove);
            if (Math.Abs(cmd.RightMove) > max) max = Math.Abs(cmd.RightMove);
            if (Math.Abs(cmd.UpMove)    > max) max = Math.Abs(cmd.UpMove);
            if (max == 0) return 0f;

            float total = Mathf.Sqrt(
                cmd.ForwardMove * cmd.ForwardMove +
                cmd.RightMove   * cmd.RightMove   +
                cmd.UpMove      * cmd.UpMove);
            float scale = (float)_pm.Ps.Speed * max / (127f * total);

            if ((cmd.Buttons & Button.Sprint) != 0 && _pm.Pmext.SprintTime > 50)
                scale *= _pm.Ps.SprintSpeedScale;
            else
                scale *= _pm.Ps.RunSpeedScale;

            if (_pm.Ps.PmType == GameConst.PM_NOCLIP) scale *= 3f;

            int wp = _pm.Ps.Weapon;
            if (wp == GameConst.WP_PANZERFAUST ||
                wp == GameConst.WP_MOBILE_MG42  ||
                wp == GameConst.WP_MOBILE_MG42_SET ||
                wp == GameConst.WP_MORTAR)
            {
                scale *= _pm.Skill[GameConst.SK_HEAVY_WEAPONS] >= 3 ? 0.75f : 0.5f;
            }

            if (wp == GameConst.WP_FLAMETHROWER &&
                (_pm.Skill[GameConst.SK_HEAVY_WEAPONS] < 3 ||
                 (cmd.Buttons & Button.Attack) != 0))
            {
                scale *= 0.7f;
            }

            return scale;
        }

        // -----------------------------------------------------------------------
        // PM_SetMovementDir
        // -----------------------------------------------------------------------
        private void PM_SetMovementDir()
        {
            Vector3 moved = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2)
                          - _pml.PreviousOrigin;
            float speed = moved.magnitude;

            if ((_pm.Cmd.ForwardMove != 0 || _pm.Cmd.RightMove != 0) &&
                _pm.Ps.GroundEntityNum != GameConst.ENTITYNUM_NONE &&
                speed > _pml.FrameTime * 5f)
            {
                Vector3 dir     = moved.normalized;
                Vector3 dirAng  = ETMath.VecToAngles(dir);
                int moveyaw = (int)ETMath.AngleDelta(dirAng.y, _pm.Ps.ViewAngles1);
                if (_pm.Cmd.ForwardMove < 0)
                    moveyaw = (int)ETMath.AngleNormalize180(moveyaw + 180f);
                if (moveyaw >  75) moveyaw =  75;
                if (moveyaw < -75) moveyaw = -75;
                _pm.Ps.MovementDir = (sbyte)moveyaw;
            }
            else
            {
                _pm.Ps.MovementDir = 0;
            }
        }

        // -----------------------------------------------------------------------
        // PM_CheckJump
        // -----------------------------------------------------------------------
        private bool PM_CheckJump()
        {
            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0) return false;
            if (_pm.Cmd.ServerTime - _pm.Pmext.JumpTime < 850) return false;
            if ((_pm.Ps.PmFlags & GameConst.PMF_RESPAWNED) != 0) return false;
            if (_pm.Cmd.UpMove < 10) return false;

            if ((_pm.Ps.PmFlags & GameConst.PMF_JUMP_HELD) != 0)
            {
                _pm.Cmd.UpMove = 0;
                return false;
            }

            _pml.GroundPlane = false;
            _pml.Walking     = false;
            _pm.Ps.PmFlags  |= GameConst.PMF_JUMP_HELD;
            _pm.Ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
            _pm.Ps.Velocity2       = GameConst.JUMP_VELOCITY;
            AddEvent(EV_JUMP);

            if (_pm.Cmd.ForwardMove >= 0)
            {
                // [ANIM] BG_AnimScriptEvent ANIM_ET_JUMP stub
                _pm.Ps.PmFlags &= ~GameConst.PMF_BACKWARDS_JUMP;
            }
            else
            {
                // [ANIM] BG_AnimScriptEvent ANIM_ET_JUMPBK stub
                _pm.Ps.PmFlags |= GameConst.PMF_BACKWARDS_JUMP;
            }
            return true;
        }

        // -----------------------------------------------------------------------
        // PM_CheckWaterJump
        // -----------------------------------------------------------------------
        private bool PM_CheckWaterJump()
        {
            if (_pm.Ps.PmTime != 0) return false;
            if (_pm.WaterLevel != 2) return false;

            Vector3 fwd = new Vector3(_pml.Forward.x, _pml.Forward.y, 0f).normalized;
            Vector3 origin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
            Vector3 spot   = origin + fwd * 30f;
            spot.z += 4f;

            int cont = _pm.PointContents(spot, _pm.Ps.ClientNum);
            if ((cont & Contents.Solid) == 0) return false;

            spot.z += 16f;
            cont = _pm.PointContents(spot, _pm.Ps.ClientNum);
            if (cont != 0) return false;

            _pm.Ps.Velocity0 = fwd.x * 200f;
            _pm.Ps.Velocity1 = fwd.y * 200f;
            _pm.Ps.Velocity2 = 350f;
            _pm.Ps.PmFlags  |= GameConst.PMF_TIME_WATERJUMP;
            _pm.Ps.PmTime    = 2000;
            return true;
        }

        // -----------------------------------------------------------------------
        // PM_CheckProne
        // -----------------------------------------------------------------------
        private bool PM_CheckProne()
        {
            const int PRONE_VIEWHEIGHT = 8;
            Vector3 origin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);

            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) == 0)
            {
                if ((_pm.Ps.PmFlags  & GameConst.PMF_LADDER)    != 0) return false;
                if ((_pm.Ps.Persistant[GameConst.PERS_HWEAPON_USE]) != 0) return false;
                if ((_pm.Ps.EFlags   & EF_MOUNTEDTANK)           != 0) return false;
                if (_pm.Ps.WeaponDelay != 0 &&
                    _pm.Ps.Weapon == GameConst.WP_PANZERFAUST)          return false;
                if (_pm.Ps.Weapon == GameConst.WP_MORTAR_SET)           return false;
                if (_pm.WaterLevel > 1)                                   return false;

                bool req =
                    ((_pm.Ps.PmFlags & GameConst.PMF_DUCKED) != 0 &&
                     _pm.Cmd.DoubleTap == (int)DoubleTapType.Forward) ||
                    (_pm.Cmd.WButtons & WButton.Prone) != 0;

                if (req && _pm.Cmd.ServerTime - (-_pm.Pmext.ProneTime) > 750)
                {
                    _pm.Mins = new Vector3(_pm.Ps.Mins0, _pm.Ps.Mins1, _pm.Ps.Mins2);
                    _pm.Maxs = new Vector3(_pm.Ps.Maxs0, _pm.Ps.Maxs1, _pm.Ps.CrouchMaxZ);
                    _pm.Ps.EFlags |= GameConst.EF_PRONE;
                    TraceResult tr = PM_TraceAll(origin, origin);
                    _pm.Ps.EFlags &= ~GameConst.EF_PRONE;

                    if (!tr.StartSolid && !tr.AllSolid)
                    {
                        _pm.Ps.PmFlags  |= GameConst.PMF_DUCKED;
                        _pm.Ps.EFlags   |= GameConst.EF_PRONE;
                        _pm.Pmext.ProneTime       = _pm.Cmd.ServerTime;
                        _pm.Pmext.ProneGroundTime = _pm.Cmd.ServerTime;
                    }
                }
            }

            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                bool unProne =
                    _pm.WaterLevel > 1 ||
                    _pm.Ps.PmType == GameConst.PM_DEAD ||
                    (_pm.Ps.EFlags & EF_MOUNTEDTANK) != 0 ||
                    ((_pm.Cmd.DoubleTap == (int)DoubleTapType.Back ||
                      _pm.Cmd.UpMove > 10 ||
                      (_pm.Cmd.WButtons & WButton.Prone) != 0) &&
                     _pm.Cmd.ServerTime - _pm.Pmext.ProneTime > 750);

                if (unProne)
                {
                    _pm.Mins = new Vector3(_pm.Ps.Mins0, _pm.Ps.Mins1, _pm.Ps.Mins2);
                    _pm.Maxs = new Vector3(_pm.Ps.Maxs0, _pm.Ps.Maxs1, _pm.Ps.CrouchMaxZ);
                    _pm.Ps.EFlags &= ~GameConst.EF_PRONE;
                    TraceResult tr = PM_TraceAll(origin, origin);
                    _pm.Ps.EFlags |= GameConst.EF_PRONE;

                    if (!tr.AllSolid)
                    {
                        _pm.Ps.PmFlags  |= GameConst.PMF_DUCKED;
                        _pm.Ps.EFlags   &= ~GameConst.EF_PRONE;
                        _pm.Ps.EFlags   &= ~GameConst.EF_PRONE_MOVING;
                        _pm.Pmext.ProneTime = -_pm.Cmd.ServerTime;
                        // PM_BeginWeaponChange (WP_MOBILE_MG42_SET) — weapon layer stub
                        _pm.Pmext.JumpTime = _pm.Cmd.ServerTime - 650;
                        _pm.Ps.JumpTime    = _pm.Cmd.ServerTime - 650;
                    }
                }
            }

            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                Vector3 vel = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
                float spd = vel.magnitude;
                bool userInput = Math.Abs(_pm.Cmd.ForwardMove) + Math.Abs(_pm.Cmd.RightMove) > 10;

                if (userInput && spd > 40f && (_pm.Ps.EFlags & GameConst.EF_PRONE_MOVING) == 0)
                    _pm.Ps.EFlags |= GameConst.EF_PRONE_MOVING;
                else if (!userInput && spd < 20f && (_pm.Ps.EFlags & GameConst.EF_PRONE_MOVING) != 0)
                    _pm.Ps.EFlags &= ~GameConst.EF_PRONE_MOVING;

                _pm.Mins = new Vector3(_pm.Ps.Mins0, _pm.Ps.Mins1, _pm.Ps.Mins2);
                _pm.Maxs = new Vector3(_pm.Ps.Maxs0, _pm.Ps.Maxs1,
                    _pm.Ps.Maxs2 - _pm.Ps.StandViewHeight - PRONE_VIEWHEIGHT);
                _pm.Ps.ViewHeight = PRONE_VIEWHEIGHT;
                return true;
            }
            return false;
        }

        // -----------------------------------------------------------------------
        // PM_CheckDodge — disabled in source, always false
        // -----------------------------------------------------------------------
        private bool PM_CheckDodge() => false;

        // -----------------------------------------------------------------------
        // PM_WaterJumpMove
        // -----------------------------------------------------------------------
        private void PM_WaterJumpMove()
        {
            PM_StepSlideMove(true);
            _pm.Ps.Velocity2 -= _pm.Ps.Gravity * _pml.FrameTime;
            if (_pm.Ps.Velocity2 < 0f)
            {
                _pm.Ps.PmFlags &= ~GameConst.PMF_ALL_TIMES;
                _pm.Ps.PmTime   = 0;
            }
        }

        // -----------------------------------------------------------------------
        // PM_WaterMove
        // -----------------------------------------------------------------------
        private void PM_WaterMove()
        {
            if (PM_CheckWaterJump()) { PM_WaterJumpMove(); return; }

            PM_Friction();

            float scale = PM_CmdScale(_pm.Cmd);
            Vector3 wishvel;
            if (scale == 0f)
            {
                wishvel = new Vector3(0f, 0f, -60f);
            }
            else
            {
                wishvel = _pml.Forward * (scale * _pm.Cmd.ForwardMove)
                        + _pml.Right   * (scale * _pm.Cmd.RightMove);
                wishvel.z += scale * _pm.Cmd.UpMove;
            }

            Vector3 wishdir = wishvel;
            float wishspeed = wishdir.magnitude;
            if (wishspeed > 0f) wishdir /= wishspeed;

            if (_pm.WaterType == Contents.Slime)
            {
                float maxsp = _pm.Ps.Speed * PM_SlagSwimScale;
                if (wishspeed > maxsp) wishspeed = maxsp;
                PM_Accelerate(wishdir, wishspeed, PM_SlagAccelerate);
            }
            else
            {
                float maxsp = _pm.Ps.Speed * PM_WaterSwimScale;
                if (wishspeed > maxsp) wishspeed = maxsp;
                PM_Accelerate(wishdir, wishspeed, PM_WaterAccelerate);
            }

            Vector3 vel = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            if (_pml.GroundPlane && Vector3.Dot(vel, _pml.GroundTrace.PlaneNormal) < 0f)
            {
                float vlen = vel.magnitude;
                vel = PM_ClipVelocity(vel, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP);
                if (vel.magnitude > 0f) vel = vel.normalized * vlen;
                WriteVelocity(vel);
            }

            PM_SlideMove(false);
        }

        // -----------------------------------------------------------------------
        // PM_FlyMove
        // -----------------------------------------------------------------------
        private void PM_FlyMove()
        {
            PM_Friction();
            float scale = PM_CmdScale(_pm.Cmd);
            Vector3 wishvel = scale == 0f ? Vector3.zero
                : _pml.Forward * (scale * _pm.Cmd.ForwardMove)
                + _pml.Right   * (scale * _pm.Cmd.RightMove)
                + new Vector3(0f, 0f, scale * _pm.Cmd.UpMove);

            Vector3 wishdir = wishvel;
            float wishspeed = wishdir.magnitude;
            if (wishspeed > 0f) wishdir /= wishspeed;

            PM_Accelerate(wishdir, wishspeed, PM_FlyAccelerate);
            PM_StepSlideMove(false);
        }

        // -----------------------------------------------------------------------
        // PM_AirMove
        // -----------------------------------------------------------------------
        private void PM_AirMove()
        {
            PM_Friction();

            float fmove = _pm.Cmd.ForwardMove;
            float smove = _pm.Cmd.RightMove;
            float scale;
            Vector3 fwd   = _pml.Forward;
            Vector3 right = _pml.Right;

            if (_pm.Cmd.ServerTime - _pm.Pmext.DodgeTime < 350)
            {
                fwd.z  = 0f;
                fmove  = 0f;
                smove  = _pm.Pmext.DtMove == (int)DoubleTapType.MoveLeft ? -2070f : 2070f;
                scale  = 1f;
            }
            else
            {
                scale   = PM_CmdScale(_pm.Cmd);
                fwd.z   = 0f;
                right.z = 0f;
            }

            if (fwd.magnitude   > 0f) fwd   = fwd.normalized;
            if (right.magnitude > 0f) right = right.normalized;

            Vector3 wishvel = fwd * fmove + right * smove;
            wishvel.z = 0f;
            Vector3 wishdir = wishvel;
            float wishspeed = wishdir.magnitude;
            if (wishspeed > 0f) wishdir /= wishspeed;
            wishspeed *= scale;

            PM_Accelerate(wishdir, wishspeed, PM_AirAccelerate);

            if (_pml.GroundPlane)
            {
                Vector3 v = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
                WriteVelocity(PM_ClipVelocity(v, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP));
            }

            PM_StepSlideMove(true);
            PM_SetMovementDir();
        }

        // -----------------------------------------------------------------------
        // PM_WalkMove
        // -----------------------------------------------------------------------
        private void PM_WalkMove()
        {
            if (_pm.WaterLevel > 2 &&
                Vector3.Dot(_pml.Forward, _pml.GroundTrace.PlaneNormal) > 0f)
            {
                PM_WaterMove();
                return;
            }

            if (PM_CheckJump())
            {
                if (_pm.WaterLevel > 1) PM_WaterMove();
                else                     PM_AirMove();

                if (!(_pm.Cmd.ServerTime - _pm.Pmext.JumpTime < 850))
                {
                    _pm.Pmext.SprintTime -= 2500;
                    if (_pm.Pmext.SprintTime < 0) _pm.Pmext.SprintTime = 0;
                    _pm.Pmext.JumpTime = _pm.Cmd.ServerTime;
                }
                _pm.Ps.JumpTime = _pm.Cmd.ServerTime;
                return;
            }

            if (_pm.WaterLevel <= 1 && PM_CheckDodge()) { PM_AirMove(); return; }

            PM_Friction();

            float fmove = _pm.Cmd.ForwardMove;
            float smove = _pm.Cmd.RightMove;
            float scale = PM_CmdScale(_pm.Cmd);

            Vector3 fwd   = _pml.Forward;
            Vector3 right = _pml.Right;
            fwd.z   = 0f;
            right.z = 0f;
            fwd   = PM_ClipVelocity(fwd,   _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP);
            right = PM_ClipVelocity(right, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP);
            if (fwd.magnitude   > 0f) fwd   = fwd.normalized;
            if (right.magnitude > 0f) right = right.normalized;

            Vector3 wishvel = fwd * fmove + right * smove;
            Vector3 wishdir = wishvel;
            float wishspeed = wishdir.magnitude;
            if (wishspeed > 0f) wishdir /= wishspeed;
            wishspeed *= scale;

            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                float mx = _pm.Ps.Speed * PM_ProneSpeedScale;
                if (wishspeed > mx) wishspeed = mx;
            }
            else if ((_pm.Ps.PmFlags & GameConst.PMF_DUCKED) != 0)
            {
                float mx = _pm.Ps.Speed * _pm.Ps.CrouchSpeedScale;
                if (wishspeed > mx) wishspeed = mx;
            }

            if (_pm.WaterLevel > 0)
            {
                float wsc = _pm.WaterLevel / 3f;
                wsc = _pm.WaterType == Contents.Slime
                    ? 1f - (1f - PM_SlagSwimScale)  * wsc
                    : 1f - (1f - PM_WaterSwimScale)  * wsc;
                float mx = _pm.Ps.Speed * wsc;
                if (wishspeed > mx) wishspeed = mx;
            }

            bool slick = (_pml.GroundTrace.SurfaceFlags & Surf.Slick) != 0 ||
                         (_pm.Ps.PmFlags & GameConst.PMF_TIME_KNOCKBACK) != 0;
            PM_Accelerate(wishdir, wishspeed, slick ? PM_AirAccelerate : PM_AccelerateVal);

            if (slick)
                _pm.Ps.Velocity2 -= _pm.Ps.Gravity * _pml.FrameTime;

            // Snow breath
            if ((_pml.GroundTrace.SurfaceFlags & Surf.Snow) != 0)
                _pm.Ps.EFlags |= EF_BREATH;
            else
                _pm.Ps.EFlags &= ~EF_BREATH;

            Vector3 vel = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            float velLen = vel.magnitude;
            vel = PM_ClipVelocity(vel, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP);

            if (vel.x == 0f && vel.y == 0f)
            {
                if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0)
                    _pm.Pmext.ProneGroundTime = _pm.Cmd.ServerTime;
                return;
            }

            if (vel.magnitude > 0f) vel = vel.normalized * velLen;
            WriteVelocity(vel);

            PM_StepSlideMove(false);
            PM_SetMovementDir();
        }

        // -----------------------------------------------------------------------
        // PM_DeadMove
        // -----------------------------------------------------------------------
        private void PM_DeadMove()
        {
            if (!_pml.Walking) return;
            Vector3 vel = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            float fwd = vel.magnitude - 20f;
            WriteVelocity(fwd <= 0f ? Vector3.zero : vel.normalized * fwd);
        }

        // -----------------------------------------------------------------------
        // PM_NoclipMove
        // -----------------------------------------------------------------------
        private void PM_NoclipMove()
        {
            _pm.Ps.ViewHeight = GameConst.DEFAULT_VIEWHEIGHT;
            Vector3 vel   = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            float speed   = vel.magnitude;
            if (speed < 1f)
            {
                WriteVelocity(Vector3.zero);
            }
            else
            {
                float friction = PM_FrictionVal * 1.5f;
                float control  = speed < PM_StopSpeed ? PM_StopSpeed : speed;
                float drop     = control * friction * _pml.FrameTime;
                float ns       = (speed - drop) / speed;
                if (ns < 0f) ns = 0f;
                WriteVelocity(vel * ns);
            }

            float scale = PM_CmdScale(_pm.Cmd);
            Vector3 wishvel = _pml.Forward * (scale * _pm.Cmd.ForwardMove)
                            + _pml.Right   * (scale * _pm.Cmd.RightMove)
                            + new Vector3(0f, 0f, scale * _pm.Cmd.UpMove);
            Vector3 wishdir = wishvel;
            float ws = wishdir.magnitude;
            if (ws > 0f) wishdir /= ws;
            ws *= scale;

            PM_Accelerate(wishdir, ws, PM_AccelerateVal);

            Vector3 origin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
            origin += new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2)
                    * _pml.FrameTime;
            WriteOrigin(origin);
        }

        // -----------------------------------------------------------------------
        // PM_FootstepForSurface
        // -----------------------------------------------------------------------
        private int PM_FootstepForSurface()
            => BG_FootstepForSurface(_pml.GroundTrace.SurfaceFlags);

        // -----------------------------------------------------------------------
        // PM_CrashLand
        // -----------------------------------------------------------------------
        private void PM_CrashLand()
        {
            // [ANIM] BG_AnimScriptEvent ANIM_ET_LAND stub (when previous_velocity[2] < -220)

            float dist = _pm.Ps.Origin2 - _pml.PreviousOrigin.z;
            float vel  = _pml.PreviousVelocity.z;
            float acc  = -(float)_pm.Ps.Gravity;
            float a    = acc * 0.5f;
            float b    = vel;
            float c    = -dist;
            float den  = b * b - 4f * a * c;
            if (den < 0f) return;

            float t     = (-b - Mathf.Sqrt(den)) / (2f * a);
            float delta = vel + t * acc;
            delta = delta * delta * 0.0001f;

            if (_pm.WaterLevel == 3) return;
            if (_pm.WaterLevel == 2) delta *= 0.25f;
            if (_pm.WaterLevel == 1) delta *= 0.5f;
            if (delta < 1f) return;

            if ((_pml.GroundTrace.SurfaceFlags & Surf.NoDamage) == 0)
            {
                int surf = PM_FootstepForSurface();
                if      (delta > 77f)    AddEvent(EV_FALL_NDIE,   surf);
                else if (delta > 67f)    AddEvent(EV_FALL_DMG_50, surf);
                else if (delta > 58f && _pm.Ps.Stats[GameConst.STAT_HEALTH] > 0)
                                         AddEvent(EV_FALL_DMG_25, surf);
                else if (delta > 48f && _pm.Ps.Stats[GameConst.STAT_HEALTH] > 0)
                                         AddEvent(EV_FALL_DMG_15, surf);
                else if (delta > 38.75f && _pm.Ps.Stats[GameConst.STAT_HEALTH] > 0)
                                         AddEvent(EV_FALL_DMG_10, surf);
                else if (delta > 7f)     AddEvent(EV_FALL_SHORT,  surf);
                else                     AddEvent(EV_FOOTSTEP,    surf);
            }

            if (delta > 38.75f)
                WriteVelocity(Vector3.zero);

            _pm.Ps.BobCycle = 0;
        }

        // -----------------------------------------------------------------------
        // PM_CorrectAllSolid
        // -----------------------------------------------------------------------
        private bool PM_CorrectAllSolid(ref TraceResult traceOut)
        {
            Vector3 origin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            for (int k = -1; k <= 1; k++)
            {
                Vector3 pt = origin + new Vector3(i, j, k);
                TraceResult tr = PM_TraceAll(pt, pt);
                if (!tr.AllSolid)
                {
                    Vector3 below = new Vector3(origin.x, origin.y, origin.z - 0.25f);
                    tr = PM_TraceAll(origin, below);
                    _pml.GroundTrace = tr;
                    traceOut = tr;
                    return true;
                }
            }
            _pm.Ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
            _pml.GroundPlane = false;
            _pml.Walking     = false;
            return false;
        }

        // -----------------------------------------------------------------------
        // PM_GroundTraceMissed
        // -----------------------------------------------------------------------
        private void PM_GroundTraceMissed()
        {
            if (_pm.Ps.GroundEntityNum != GameConst.ENTITYNUM_NONE)
            {
                Vector3 origin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
                TraceResult tr = PM_TraceAll(origin, origin - new Vector3(0f, 0f, 64f));
                if (tr.Fraction == 1f)
                {
                    if (_pm.Cmd.ForwardMove >= 0)
                    {
                        // [ANIM] BG_AnimScriptEvent ANIM_ET_JUMP stub
                        _pm.Ps.PmFlags &= ~GameConst.PMF_BACKWARDS_JUMP;
                    }
                    else
                    {
                        // [ANIM] BG_AnimScriptEvent ANIM_ET_JUMPBK stub
                        _pm.Ps.PmFlags |= GameConst.PMF_BACKWARDS_JUMP;
                    }
                }
            }
            if (_pm.Ps.GroundEntityNum != -1)
                _pm.Ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
            _pml.GroundPlane = false;
            _pml.Walking     = false;
        }

        // -----------------------------------------------------------------------
        // PM_GroundTrace
        // -----------------------------------------------------------------------
        private void PM_GroundTrace()
        {
            Vector3 origin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
            Vector3 point  = origin;
            point.z -= ((_pm.Ps.EFlags & (EF_MG42_ACTIVE | EF_AAGUN_ACTIVE)) != 0) ? 1f : 0.25f;

            float legOfs;
            TraceResult trace = PM_TraceAllLegs(origin, point, out legOfs);
            _pm.Pmext.ProneLegsOffset = legOfs;
            _pml.GroundTrace = trace;

            if (trace.AllSolid && (_pm.Ps.EFlags & EF_MOUNTEDTANK) == 0)
            {
                if (!PM_CorrectAllSolid(ref trace)) return;
            }

            if (trace.Fraction == 1f)
            {
                PM_GroundTraceMissed();
                _pml.GroundPlane = false;
                _pml.Walking     = false;
                return;
            }

            Vector3 vel = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            if (vel.z > 0f &&
                Vector3.Dot(vel, trace.PlaneNormal) > 10f &&
                (_pm.Ps.EFlags & GameConst.EF_PRONE) == 0)
            {
                if (_pm.Cmd.ForwardMove >= 0)
                {
                    // [ANIM] BG_AnimScriptEvent ANIM_ET_JUMP stub
                    _pm.Ps.PmFlags &= ~GameConst.PMF_BACKWARDS_JUMP;
                }
                else
                {
                    // [ANIM] BG_AnimScriptEvent ANIM_ET_JUMPBK stub
                    _pm.Ps.PmFlags |= GameConst.PMF_BACKWARDS_JUMP;
                }
                _pm.Ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
                _pml.GroundPlane = false;
                _pml.Walking     = false;
                return;
            }

            if (trace.PlaneNormal.z < GameConst.MIN_WALK_NORMAL)
            {
                _pm.Ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
                _pml.GroundPlane = true;
                _pml.Walking     = false;
                return;
            }

            _pml.GroundPlane = true;
            _pml.Walking     = true;

            if ((_pm.Ps.PmFlags & GameConst.PMF_TIME_WATERJUMP) != 0)
            {
                _pm.Ps.PmFlags &= ~(GameConst.PMF_TIME_WATERJUMP | GameConst.PMF_TIME_LAND);
                _pm.Ps.PmTime   = 0;
            }

            if (_pm.Ps.GroundEntityNum == GameConst.ENTITYNUM_NONE)
            {
                PM_CrashLand();
                if (_pml.PreviousVelocity.z < -200f)
                {
                    _pm.Ps.PmFlags |= GameConst.PMF_TIME_LAND;
                    _pm.Ps.PmTime   = 250;
                }
            }

            _pm.Ps.GroundEntityNum = trace.EntityNum;
            PM_AddTouchEnt(trace.EntityNum);
        }

        // -----------------------------------------------------------------------
        // PM_SetWaterLevel
        // -----------------------------------------------------------------------
        private void PM_SetWaterLevel()
        {
            _pm.WaterLevel = 0;
            _pm.WaterType  = 0;

            float minsZ = _pm.Ps.Mins2;
            Vector3 pt = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2 + minsZ + 1f);
            int cont = _pm.PointContents(pt, _pm.Ps.ClientNum);

            if ((cont & Contents.MaskWater) != 0)
            {
                float sample2 = _pm.Ps.ViewHeight - minsZ;
                float sample1 = sample2 * 0.5f;

                _pm.WaterType  = cont;
                _pm.WaterLevel = 1;

                pt.z = _pm.Ps.Origin2 + minsZ + sample1;
                cont = _pm.PointContents(pt, _pm.Ps.ClientNum);
                if ((cont & Contents.MaskWater) != 0)
                {
                    _pm.WaterLevel = 2;
                    pt.z = _pm.Ps.Origin2 + minsZ + sample2;
                    cont = _pm.PointContents(pt, _pm.Ps.ClientNum);
                    if ((cont & Contents.MaskWater) != 0)
                        _pm.WaterLevel = 3;
                }
            }
            // [ANIM] BG_UpdateConditionValue ANIM_COND_UNDERWATER stub
        }

        // -----------------------------------------------------------------------
        // PM_CheckDuck
        // -----------------------------------------------------------------------
        private void PM_CheckDuck()
        {
            _pm.Mins = new Vector3(_pm.Ps.Mins0, _pm.Ps.Mins1, _pm.Ps.Mins2);
            _pm.Maxs = new Vector3(_pm.Ps.Maxs0, _pm.Ps.Maxs1, _pm.Ps.Maxs2);

            if (_pm.Ps.PmType == GameConst.PM_DEAD)
            {
                _pm.Ps.ViewHeight = (int)_pm.Ps.DeadViewHeight;
                return;
            }

            bool duckCmd = _pm.Cmd.UpMove < 0 &&
                           (_pm.Ps.EFlags  & EF_MOUNTEDTANK)          == 0 &&
                           (_pm.Ps.PmFlags & GameConst.PMF_LADDER)    == 0;

            if (duckCmd || _pm.Ps.Weapon == GameConst.WP_MORTAR_SET)
            {
                _pm.Ps.PmFlags |= GameConst.PMF_DUCKED;
            }
            else
            {
                if ((_pm.Ps.PmFlags & GameConst.PMF_DUCKED) != 0)
                {
                    _pm.Maxs = new Vector3(_pm.Ps.Maxs0, _pm.Ps.Maxs1, _pm.Ps.Maxs2);
                    Vector3 org = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
                    TraceResult tr = PM_TraceAll(org, org);
                    if (!tr.AllSolid)
                        _pm.Ps.PmFlags &= ~GameConst.PMF_DUCKED;
                }
            }

            if ((_pm.Ps.PmFlags & GameConst.PMF_DUCKED) != 0)
            {
                _pm.Maxs          = new Vector3(_pm.Ps.Maxs0, _pm.Ps.Maxs1, _pm.Ps.CrouchMaxZ);
                _pm.Ps.ViewHeight = (int)_pm.Ps.CrouchViewHeight;
            }
            else
            {
                _pm.Maxs          = new Vector3(_pm.Ps.Maxs0, _pm.Ps.Maxs1, _pm.Ps.Maxs2);
                _pm.Ps.ViewHeight = (int)_pm.Ps.StandViewHeight;
            }
        }

        // -----------------------------------------------------------------------
        // PM_Footsteps
        // -----------------------------------------------------------------------
        private void PM_Footsteps()
        {
            if ((_pm.Ps.EFlags & EF_DEAD) != 0)
            {
                // [ANIM] BG_AnimScriptAnimation ANIM_MT_FLAILING / ANIM_MT_FALLEN stub
                return;
            }

            _pm.XYSpeed = Mathf.Sqrt(
                _pm.Ps.Velocity0 * _pm.Ps.Velocity0 +
                _pm.Ps.Velocity1 * _pm.Ps.Velocity1);

            if (_pm.Ps.Persistant[GameConst.PERS_HWEAPON_USE] != 0)
            {
                // [ANIM] BG_AnimScriptAnimation ANIM_MT_IDLE stub
                return;
            }

            if (_pm.WaterLevel > 2)
            {
                // [ANIM] ANIM_MT_SWIM / ANIM_MT_SWIMBK stub
                return;
            }

            if (_pm.Ps.GroundEntityNum == GameConst.ENTITYNUM_NONE)
            {
                // [ANIM] ladder / airborne stub
                return;
            }

            if (_pm.Cmd.ForwardMove == 0 && _pm.Cmd.RightMove == 0)
            {
                if (_pm.XYSpeed < 5f)  _pm.Ps.BobCycle = 0;
                if (_pm.XYSpeed > 120f) return;
                // [ANIM] idle stubs
                return;
            }

            float bobmove;
            bool  footstep = false;

            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                bobmove = 0.2f;
                // [ANIM] ANIM_MT_PRONE / ANIM_MT_PRONEBK stub
            }
            else if ((_pm.Ps.PmFlags & GameConst.PMF_DUCKED) != 0)
            {
                bobmove = 0.5f;
                // [ANIM] ANIM_MT_WALKCR / ANIM_MT_WALKCRBK stub
            }
            else if ((_pm.Ps.PmFlags & GameConst.PMF_BACKWARDS_RUN) != 0)
            {
                if ((_pm.Cmd.Buttons & Button.Walking) == 0)
                {
                    bobmove  = 0.4f;
                    footstep = true;
                    // [ANIM] ANIM_MT_RUNBK / ANIM_MT_STRAFELEFT/RIGHT stub
                }
                else
                {
                    bobmove = 0.3f;
                    // [ANIM] ANIM_MT_WALKBK / strafe stub
                }
            }
            else
            {
                if ((_pm.Cmd.Buttons & Button.Walking) == 0)
                {
                    bobmove  = 0.4f;
                    footstep = true;
                    // [ANIM] ANIM_MT_RUN / strafe stub
                }
                else
                {
                    bobmove = 0.3f;
                    // [ANIM] ANIM_MT_WALK / strafe stub
                }
            }

            int old = _pm.Ps.BobCycle;
            _pm.Ps.BobCycle = (int)(old + bobmove * _pml.Msec) & 255;

            // Cross cycle boundary → play sound
            if (((old + 64) ^ (_pm.Ps.BobCycle + 64)) >= 128)
            {
                if (_pm.WaterLevel == 0)
                {
                    if (footstep && !_pm.NoFootsteps)
                        AddEvent(EV_FOOTSTEP, PM_FootstepForSurface());
                }
                else if (_pm.WaterLevel == 1) AddEvent(EV_FOOTSPLASH);
                else if (_pm.WaterLevel == 2) AddEvent(EV_SWIM);
            }
        }

        // -----------------------------------------------------------------------
        // PM_WaterEvents
        // -----------------------------------------------------------------------
        private void PM_WaterEvents()
        {
            if (_pml.PreviousWaterLevel == 0 && _pm.WaterLevel  > 0) AddEvent(EV_WATER_TOUCH);
            if (_pml.PreviousWaterLevel  > 0 && _pm.WaterLevel == 0) AddEvent(EV_WATER_LEAVE);
            if (_pml.PreviousWaterLevel != 3 && _pm.WaterLevel == 3) AddEvent(EV_WATER_UNDER);
            if (_pml.PreviousWaterLevel == 3 && _pm.WaterLevel != 3)
                AddEvent(EV_WATER_CLEAR, _pm.Pmext.AirLeft < 6000 ? 1 : 0);
        }

        // -----------------------------------------------------------------------
        // Weapon-system event constants
        // -----------------------------------------------------------------------
        private const int EV_EMPTYCLIP              = 34;
        private const int EV_FILL_CLIP              = 35;
        private const int EV_MG42_FIXED             = 36;
        private const int EV_WEAP_OVERHEAT          = 37;
        private const int EV_CHANGE_WEAPON          = 38;
        private const int EV_CHANGE_WEAPON_2        = 39;
        private const int EV_FIRE_WEAPON            = 40;
        private const int EV_FIRE_WEAPONB           = 41;
        private const int EV_FIRE_WEAPON_LASTSHOT   = 42;
        private const int EV_NOFIRE_UNDERWATER      = 43;
        private const int EV_FIRE_WEAPON_MG42       = 44;
        private const int EV_FIRE_WEAPON_MOUNTEDMG42= 45;
        private const int EV_SPINUP                 = 80; // after EV_MORTAREFX in C enum
        private const int EV_FIRE_WEAPON_AAGUN      = 97;

        // -----------------------------------------------------------------------
        // Extra weapon-state constants not in GameConst
        // (values above the 0-6 range already used by GameConst)
        // -----------------------------------------------------------------------
        private const int WEAPON_RAISING_TORELOAD   = 10;
        private const int WEAPON_DROPPING_TORELOAD  = 11;
        private const int WEAPON_READYING           = 12;
        private const int WEAPON_RELAXING           = 13;

        // -----------------------------------------------------------------------
        // Local aliases for GameConst weapon IDs used in PM_Weapon
        // -----------------------------------------------------------------------
        private const int WP_MEDKIT                 = GameConst.WP_MEDKIT;                // 19
        private const int WP_PLIERS                 = GameConst.WP_PLIERS;                // 21
        private const int WP_SMOKE_MARKER           = GameConst.WP_SMOKE_MARKER;          // 22
        private const int WP_LANDMINE               = GameConst.WP_LANDMINE;              // 26
        private const int WP_TRIPMINE               = GameConst.WP_TRIPMINE;              // 29
        private const int WP_SMOKE_BOMB             = GameConst.WP_SMOKE_BOMB;            // 30
        private const int WP_DUMMY_MG42             = GameConst.WP_DUMMY_MG42;            // 34
        private const int WP_LOCKPICK               = GameConst.WP_LOCKPICK;              // 36
        private const int WP_SILENCED_COLT          = GameConst.WP_SILENCED_COLT;         // 41
        private const int WP_AKIMBO_SILENCEDCOLT    = GameConst.WP_AKIMBO_SILENCEDCOLT;   // 47
        private const int WP_AKIMBO_SILENCEDLUGER   = GameConst.WP_AKIMBO_SILENCEDLUGER;  // 48
        private const int WP_NUM_WEAPONS            = GameConst.WP_NUM_WEAPONS;            // 50

        // Team constant
        private const int TEAM_SPECTATOR            = 3;

        // MG42 rate of fire constants (milliseconds per shot)
        private const int MG42_RATE_OF_FIRE_MP      = 100;
        private const int MG42_RATE_OF_FIRE_SP      = 100;
        private const int AAGUN_RATE_OF_FIRE        = 1000;

        // -----------------------------------------------------------------------
        // AmmoTableEntry stub — returned by GetAmmoTableData()
        // Full table will be ported later; defaults cover the common path.
        // -----------------------------------------------------------------------
        private struct AmmoTableEntry
        {
            public int  fireDelayTime;
            public int  nextShotTime;
            public int  maxClip;
            public int  reloadTime;
            public int  maxHeat;
            public int  coolRate;
            public int  uses;
        }

        // Returns a sensible default for any weapon until the real table is ported.
        private static AmmoTableEntry GetAmmoTableData(int weapon)
        {
            return new AmmoTableEntry
            {
                fireDelayTime = 0,
                nextShotTime  = 400,
                maxClip       = 30,
                reloadTime    = 1500,
                maxHeat       = 0,
                coolRate      = 0,
                uses          = 1,
            };
        }

        // Stub — identity map; real table ported later
        private static int BG_FindAmmoForWeapon(int weapon) => weapon;
        private static int BG_FindClipForWeapon(int weapon) => weapon;

        // Stub — no akimbo in this port yet
        private static bool BG_IsAkimboWeapon(int weapon)  => false;
        private static bool BG_AkimboFireSequence(int weapon, int clip1, int clip2) => false;
        private static int  BG_AkimboSidearm(int weapon)   => GameConst.WP_NONE;

        // Stub — BG_FindItemForWeapon; full item table ported later
        private static object BG_FindItemForWeapon(int weapon) => null;

        // weapAlts — for this port every weapon has no alt; full table ported later
        private static int WeaponAlt(int weapon)            => GameConst.WP_NONE;

        // -----------------------------------------------------------------------
        // PM_CoolWeapons
        // Ported from bg_pmove.c:3038
        // -----------------------------------------------------------------------
        private void PM_CoolWeapons()
        {
            var ps = _pm.Ps;

            for (int wp = 0; wp < WP_NUM_WEAPONS; wp++)
            {
                // if you have the weapon
                if (ETShared.COM_BitCheck(ps.Weapons, wp))
                {
                    if (ps.WeapHeat[wp] != 0)
                    {
                        int coolRate = GetAmmoTableData(wp).coolRate;
                        if (_pm.Skill[GameConst.SK_HEAVY_WEAPONS] >= 2 &&
                            ps.Stats[GameConst.STAT_PLAYER_CLASS] == GameConst.PC_SOLDIER)
                        {
                            ps.WeapHeat[wp] -= (int)(coolRate * 2f * _pml.FrameTime);
                        }
                        else
                        {
                            ps.WeapHeat[wp] -= (int)(coolRate * _pml.FrameTime);
                        }
                        if (ps.WeapHeat[wp] < 0)
                            ps.WeapHeat[wp] = 0;
                    }
                }
            }

            // Convert current weapon heat to 0-255 range
            if (ps.Weapon != 0)
            {
                if (ps.Persistant[GameConst.PERS_HWEAPON_USE] != 0 ||
                    (ps.EFlags & EF_MOUNTEDTANK) != 0)
                {
                    ps.CurWeapHeat = (int)Mathf.Floor(
                        ((float)ps.WeapHeat[WP_DUMMY_MG42] / GameConst.MAX_MG42_HEAT) * 255f);
                }
                else
                {
                    int maxHeat = GetAmmoTableData(ps.Weapon).maxHeat;
                    if (maxHeat != 0)
                    {
                        ps.CurWeapHeat = (int)Mathf.Floor(
                            ((float)ps.WeapHeat[ps.Weapon] / (float)maxHeat) * 255f);
                    }
                    else
                    {
                        ps.CurWeapHeat = 0;
                    }
                }
            }
        }

        // -----------------------------------------------------------------------
        // PM_WeaponAmmoAvailable / PM_WeaponUseAmmo / PM_WeaponClipEmpty
        // Ported from bg_pmove.c:2993 / 2968 / 3018
        // These are also declared public below; only one definition is needed.
        // The public versions declared further down in the file satisfy the interface.
        // -----------------------------------------------------------------------

        // -----------------------------------------------------------------------
        // PM_BeginWeaponReload
        // Ported from bg_pmove.c:2312
        // -----------------------------------------------------------------------
        private void PM_BeginWeaponReload(int weapon)
        {
            var ps    = _pm.Ps;
            var pmext = _pm.Pmext;

            // Only allow reload if weapon is ready or firing
            if (ps.Weaponstate != GameConst.WEAPON_READY &&
                ps.Weaponstate != WEAPON_FIRING)
                return;

            // No reloading the carbine / mobile MG42 / garand until clip is empty
            if (((weapon == GameConst.WP_CARBINE) && ps.AmmoClip[GameConst.WP_CARBINE] != 0) ||
                ((weapon == GameConst.WP_MOBILE_MG42 || weapon == GameConst.WP_MOBILE_MG42_SET) &&
                  ps.AmmoClip[GameConst.WP_MOBILE_MG42] != 0) ||
                ((weapon == GameConst.WP_GARAND || weapon == GameConst.WP_GARAND_SCOPE) &&
                  ps.AmmoClip[GameConst.WP_GARAND] != 0))
                return;

            // Range check
            if ((weapon <= GameConst.WP_NONE || weapon > WP_DYNAMITE) &&
                !(weapon >= GameConst.WP_KAR98 && weapon < WP_NUM_WEAPONS))
                return;

            // Item check (stub always returns null → return here)
            if (BG_FindItemForWeapon(weapon) == null)
                return;

            // Already full clip
            int clipIdx = BG_FindClipForWeapon(weapon);
            if (ps.AmmoClip[clipIdx] >= GetAmmoTableData(weapon).maxClip)
                return;

            // No reload when leaning
            if (ps.Leanf != 0f)
                return;

            // Skip animation call (BG_AnimScriptEvent stub)

            // Continue reload animation if not a mortar
            if (weapon != GameConst.WP_MORTAR && weapon != GameConst.WP_MORTAR_SET)
            {
                PM_ContinueWeaponAnim(PM_ReloadAnimForWeapon(ps.Weapon));
            }

            // Calculate reload time
            int reloadTime = GetAmmoTableData(weapon).reloadTime;
            if (_pm.Skill[GameConst.SK_LIGHT_WEAPONS] >= 2 &&
                BG_IsLightWeaponSupportingFastReload(weapon))
            {
                reloadTime = (int)(reloadTime * 0.65f);
            }

            if (ps.Weaponstate == GameConst.WEAPON_READY)
            {
                ps.WeaponTime += reloadTime;
            }
            else if (ps.WeaponTime < reloadTime)
            {
                ps.WeaponTime += (reloadTime - ps.WeaponTime);
            }

            ps.Weaponstate = GameConst.WEAPON_RELOADING;
            AddEvent(EV_FILL_CLIP);
        }

        // -----------------------------------------------------------------------
        // PM_ReloadClip
        // Ported from bg_pmove.c:2794
        // -----------------------------------------------------------------------
        private void PM_ReloadClip(int weapon)
        {
            var ps = _pm.Ps;

            int ammoWeap  = BG_FindAmmoForWeapon(weapon);
            int clipWeap  = BG_FindClipForWeapon(weapon);
            int ammoreserve = ps.Ammo[ammoWeap];
            int ammoclip    = ps.AmmoClip[clipWeap];
            int ammomove    = GetAmmoTableData(weapon).maxClip - ammoclip;

            if (ammoreserve < ammomove)
                ammomove = ammoreserve;

            if (ammomove != 0)
            {
                ps.Ammo[ammoWeap]    -= (short)ammomove;
                ps.AmmoClip[clipWeap]+= (short)ammomove;
            }

            // Reload akimbo sidearm too
            if (BG_IsAkimboWeapon(weapon))
                PM_ReloadClip(BG_AkimboSidearm(weapon));
        }

        // -----------------------------------------------------------------------
        // PM_FinishWeaponReload
        // Ported from bg_pmove.c:2823
        // -----------------------------------------------------------------------
        private void PM_FinishWeaponReload()
        {
            PM_ReloadClip(_pm.Ps.Weapon);
            _pm.Ps.Weaponstate = GameConst.WEAPON_READY;
            PM_StartWeaponAnim(PM_IdleAnimForWeapon(_pm.Ps.Weapon));
        }

        // -----------------------------------------------------------------------
        // PM_CheckForReload
        // Ported from bg_pmove.c:2835
        // -----------------------------------------------------------------------
        private void PM_CheckForReload(int weapon)
        {
            var ps    = _pm.Ps;
            var pmext = _pm.Pmext;

            if (_pm.NoWeapClips)
                return;

            // GPG40 and M7 don't reload
            if (weapon == WP_GPG40 || weapon == WP_M7)
                return;

            bool reloadRequested = (_pm.Cmd.WButtons & WButton.Reload) != 0;

            switch (ps.Weaponstate)
            {
                case GameConst.WEAPON_RAISING:
                case GameConst.WEAPON_DROPPING:
                case GameConst.WEAPON_RELOADING:
                    return;
                // extended states
                case int s when (s == WEAPON_RAISING_TORELOAD ||
                                 s == WEAPON_DROPPING_TORELOAD ||
                                 s == WEAPON_READYING ||
                                 s == WEAPON_RELAXING):
                    return;
                default:
                    break;
            }

            bool autoreload = pmext.AutoReload;
            int  clipWeap   = BG_FindClipForWeapon(weapon);
            int  ammoWeap   = BG_FindAmmoForWeapon(weapon);

            // Scoped weapons: swap to unscoped for reload
            if (weapon == GameConst.WP_FG42SCOPE ||
                weapon == GameConst.WP_GARAND_SCOPE ||
                weapon == GameConst.WP_K43_SCOPE)
            {
                if (reloadRequested && ps.Ammo[ammoWeap] != 0 &&
                    ps.AmmoClip[clipWeap] < GetAmmoTableData(weapon).maxClip)
                {
                    int alt = WeaponAlt(weapon);
                    PM_BeginWeaponChange(weapon, alt != GameConst.WP_NONE ? alt : weapon,
                        ps.Ammo[ammoWeap] != 0);
                }
                return;
            }

            if (ps.WeaponTime <= 0)
            {
                bool doReload = false;

                if (reloadRequested)
                {
                    if (ps.Ammo[ammoWeap] != 0)
                    {
                        if (ps.AmmoClip[clipWeap] < GetAmmoTableData(weapon).maxClip)
                            doReload = true;

                        if (BG_IsAkimboWeapon(weapon))
                        {
                            int sideClip = BG_FindClipForWeapon(BG_AkimboSidearm(weapon));
                            if (ps.AmmoClip[sideClip] <
                                GetAmmoTableData(BG_FindClipForWeapon(BG_AkimboSidearm(weapon))).maxClip)
                                doReload = true;
                        }
                    }
                }
                else if (autoreload)
                {
                    if (ps.AmmoClip[clipWeap] == 0 && ps.Ammo[ammoWeap] != 0)
                    {
                        if (BG_IsAkimboWeapon(weapon))
                        {
                            int sideClip = BG_FindClipForWeapon(BG_AkimboSidearm(weapon));
                            if (ps.AmmoClip[sideClip] == 0)
                                doReload = true;
                        }
                        else
                        {
                            doReload = true;
                        }
                    }
                }

                if (doReload)
                    PM_BeginWeaponReload(weapon);
            }
        }

        // -----------------------------------------------------------------------
        // PM_BeginWeaponChange
        // Ported from bg_pmove.c:2395
        // -----------------------------------------------------------------------
        private void PM_BeginWeaponChange(int oldweapon, int newweapon, bool reload)
        {
            var ps    = _pm.Ps;
            var pmext = _pm.Pmext;

            if ((ps.PmFlags & GameConst.PMF_RESPAWNED) != 0)
                return;

            if (newweapon <= GameConst.WP_NONE || newweapon >= WP_NUM_WEAPONS)
                return;

            if (!ETShared.COM_BitCheck(ps.Weapons, newweapon))
                return;

            if (ps.Weaponstate == GameConst.WEAPON_DROPPING ||
                ps.Weaponstate == WEAPON_DROPPING_TORELOAD)
                return;

            // Don't allow change during spinup
            if (ps.WeaponDelay != 0)
                return;

            // Don't allow switch while holding live grenade/dynamite
            if (ps.GrenadeTimeLeft > 0)
                return;

            ps.NextWeapon = newweapon;

            // Play switch sound / handle grenade timer
            bool altSwitchAnim = false;
            switch (newweapon)
            {
                case int w when (w == GameConst.WP_CARBINE || w == GameConst.WP_KAR98):
                    if (newweapon != WeaponAlt(oldweapon))
                        AddEvent(EV_CHANGE_WEAPON);
                    break;

                case int w when (w == WP_DYNAMITE ||
                                 w == WP_GRENADE_LAUNCHER ||
                                 w == WP_GRENADE_PINEAPPLE ||
                                 w == WP_SMOKE_BOMB):
                    ps.GrenadeTimeLeft = 0;
                    AddEvent(EV_CHANGE_WEAPON);
                    break;

                case int w when (w == GameConst.WP_MORTAR_SET):
                    if ((ps.EFlags & GameConst.EF_PRONE) != 0)
                        return;
                    if (_pm.WaterLevel == 3)
                        return;
                    AddEvent(EV_CHANGE_WEAPON);
                    break;

                default:
                    AddEvent(reload ? EV_CHANGE_WEAPON_2 : EV_CHANGE_WEAPON);
                    break;
            }

            // Play drop animation
            if (newweapon == WeaponAlt(oldweapon))
            {
                PM_StartWeaponAnim(PM_AltSwitchFromForWeapon(oldweapon));
                altSwitchAnim = true;
            }
            else
            {
                PM_StartWeaponAnim(PM_DropAnimForWeapon(oldweapon));
            }

            // Drop switch times
            int switchtime = 250;
            switch (oldweapon)
            {
                case int w when (w == GameConst.WP_CARBINE):
                    if (newweapon == WeaponAlt(oldweapon))
                    {
                        switchtime = 0;
                        if (ps.AmmoClip[newweapon] == 0 && ps.Ammo[newweapon] != 0)
                            PM_ReloadClip(newweapon);
                    }
                    break;
                case int w when (w == WP_M7):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 0;
                    break;
                case int w when (w == GameConst.WP_KAR98):
                    if (newweapon == WeaponAlt(oldweapon))
                    {
                        switchtime = 0;
                        if (ps.AmmoClip[newweapon] == 0 && ps.Ammo[newweapon] != 0)
                            PM_ReloadClip(newweapon);
                    }
                    break;
                case int w when (w == WP_GPG40):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 0;
                    break;
                case int w when (w == GameConst.WP_LUGER):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 0;
                    break;
                case int w when (w == GameConst.WP_SILENCER):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 1000; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_COLT):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 0;
                    break;
                case int w when (w == WP_SILENCED_COLT):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 1000; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_FG42 || w == GameConst.WP_FG42SCOPE):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 50;
                    break;
                case int w when (w == GameConst.WP_MOBILE_MG42 || w == GameConst.WP_MOBILE_MG42_SET):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 0;
                    break;
                case int w when (w == GameConst.WP_MORTAR || w == GameConst.WP_MORTAR_SET):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 0;
                    break;
            }

            // Animation for alt-switch
            // (BG_AnimScriptEvent stubs — no-op)

            if (reload)
                ps.Weaponstate = WEAPON_DROPPING_TORELOAD;
            else
                ps.Weaponstate = GameConst.WEAPON_DROPPING;

            ps.WeaponTime += switchtime;
        }

        // -----------------------------------------------------------------------
        // PM_FinishWeaponChange
        // Ported from bg_pmove.c:2586
        // -----------------------------------------------------------------------
        private void PM_FinishWeaponChange()
        {
            var ps    = _pm.Ps;
            var pmext = _pm.Pmext;

            int newweapon = ps.NextWeapon;
            if (newweapon < GameConst.WP_NONE || newweapon >= WP_NUM_WEAPONS)
                newweapon = GameConst.WP_NONE;

            if (!ETShared.COM_BitCheck(ps.Weapons, newweapon))
                newweapon = GameConst.WP_NONE;

            int oldweapon = ps.Weapon;
            ps.Weapon = newweapon;

            if (ps.Weaponstate == WEAPON_DROPPING_TORELOAD)
                ps.Weaponstate = WEAPON_RAISING_TORELOAD;
            else
                ps.Weaponstate = GameConst.WEAPON_RAISING;

            // Update silenced side-arm tracking
            switch (newweapon)
            {
                case int w when (w == GameConst.WP_SILENCER):
                    pmext.SilencedSideArm |= 1; break;
                case int w when (w == GameConst.WP_LUGER):
                    pmext.SilencedSideArm &= ~1; break;
                case int w when (w == WP_SILENCED_COLT):
                    pmext.SilencedSideArm |= 1; break;
                case int w when (w == GameConst.WP_COLT):
                    pmext.SilencedSideArm &= ~1; break;
                case int w when (w == GameConst.WP_CARBINE):
                    pmext.SilencedSideArm &= ~2; break;
                case int w when (w == WP_M7):
                    pmext.SilencedSideArm |= 2; break;
                case int w when (w == GameConst.WP_KAR98):
                    pmext.SilencedSideArm &= ~2; break;
                case int w when (w == WP_GPG40):
                    pmext.SilencedSideArm |= 2; break;
            }

            if (oldweapon == newweapon)
                return;

            // Raise switch times
            int switchtime = 250;
            bool altSwitchAnim = false;
            bool doSwitchAnim  = true;

            switch (newweapon)
            {
                case int w when (w == GameConst.WP_LUGER):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 0; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_SILENCER):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 1190; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_COLT):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 0; altSwitchAnim = true; }
                    break;
                case int w when (w == WP_SILENCED_COLT):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 1190; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_CARBINE):
                    if (newweapon == WeaponAlt(oldweapon))
                    {
                        altSwitchAnim = true;
                        if (ps.AmmoClip[BG_FindAmmoForWeapon(oldweapon)] != 0)
                            switchtime = 1347;
                        else
                        { switchtime = 0; doSwitchAnim = false; }
                    }
                    break;
                case int w when (w == WP_M7):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 2350; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_KAR98):
                    if (newweapon == WeaponAlt(oldweapon))
                    {
                        altSwitchAnim = true;
                        if (ps.AmmoClip[BG_FindAmmoForWeapon(oldweapon)] != 0)
                            switchtime = 1347;
                        else
                        { switchtime = 0; doSwitchAnim = false; }
                    }
                    break;
                case int w when (w == WP_GPG40):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 2350; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_FG42 || w == GameConst.WP_FG42SCOPE):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 50;
                    break;
                case int w when (w == GameConst.WP_MOBILE_MG42):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 1722;
                    break;
                case int w when (w == GameConst.WP_MOBILE_MG42_SET):
                    if (newweapon == WeaponAlt(oldweapon)) switchtime = 1250;
                    break;
                case int w when (w == GameConst.WP_MORTAR):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 1000; altSwitchAnim = true; }
                    break;
                case int w when (w == GameConst.WP_MORTAR_SET):
                    if (newweapon == WeaponAlt(oldweapon)) { switchtime = 1667; altSwitchAnim = true; }
                    break;
            }

            ps.WeaponTime += switchtime;

            // Play raise animation
            if (doSwitchAnim)
            {
                if (WeaponAlt(oldweapon) == newweapon)
                    PM_StartWeaponAnim(PM_AltSwitchToForWeapon(newweapon));
                else
                    PM_StartWeaponAnim(PM_RaiseAnimForWeapon(newweapon));
            }
        }

        // -----------------------------------------------------------------------
        // PM_SwitchIfEmpty
        // Ported from bg_pmove.c:2927
        // -----------------------------------------------------------------------
        private void PM_SwitchIfEmpty()
        {
            var ps = _pm.Ps;

            // Only applies to thrown/placed explosives
            if (ps.Weapon != WP_GRENADE_LAUNCHER &&
                ps.Weapon != WP_GRENADE_PINEAPPLE &&
                ps.Weapon != WP_DYNAMITE &&
                ps.Weapon != WP_SMOKE_BOMB &&
                ps.Weapon != WP_LANDMINE)
                return;

            int clipWeap = BG_FindClipForWeapon(ps.Weapon);
            int ammoWeap = BG_FindAmmoForWeapon(ps.Weapon);

            if (ps.AmmoClip[clipWeap] != 0) return;  // still got ammo in clip
            if (ps.Ammo[ammoWeap]     != 0) return;  // still got ammo in reserve

            // Remove the weapon if it's a grenade type
            switch (ps.Weapon)
            {
                case int w when (w == WP_GRENADE_LAUNCHER ||
                                 w == WP_GRENADE_PINEAPPLE ||
                                 w == WP_DYNAMITE):
                    ETShared.COM_BitClear(ps.Weapons, ps.Weapon);
                    break;
            }

            AddEvent(EV_NOAMMO);
        }

        // -----------------------------------------------------------------------
        // PM_Weapon
        // Ported from bg_pmove.c:3234
        // -----------------------------------------------------------------------
        private void PM_Weapon()
        {
            var ps    = _pm.Ps;
            var pmext = _pm.Pmext;

            int  addTime          = 0;
            int  ammoNeeded;
            bool delayedFire      = false;
            int  aimSpreadScaleAdd= 0;
            int  weapattackanim;
            bool akimboFire       = false;

            // Don't allow attack until all buttons are up after respawn
            if ((ps.PmFlags & GameConst.PMF_RESPAWNED) != 0)
                return;

            // Ignore if spectator
            if (ps.Persistant[GameConst.PERS_TEAM] == TEAM_SPECTATOR)
                return;

            // Check for dead player
            if (ps.Stats[GameConst.STAT_HEALTH] <= 0)
            {
                if ((ps.PmFlags & GameConst.PMF_LIMBO) == 0)
                    PM_CoolWeapons();
                return;
            }

            // Special mounted MG42 handling
            switch (ps.Persistant[GameConst.PERS_HWEAPON_USE])
            {
                case 1:
                    // Cool MG42 heat
                    if (ps.WeapHeat[WP_DUMMY_MG42] != 0)
                    {
                        ps.WeapHeat[WP_DUMMY_MG42] -= (int)(300f * _pml.FrameTime);
                        if (ps.WeapHeat[WP_DUMMY_MG42] < 0)
                            ps.WeapHeat[WP_DUMMY_MG42] = 0;
                        ps.CurWeapHeat = (int)Mathf.Floor(
                            ((float)ps.WeapHeat[WP_DUMMY_MG42] / GameConst.MAX_MG42_HEAT) * 255f);
                    }
                    if (ps.WeaponTime > 0)
                    {
                        ps.WeaponTime -= _pml.Msec;
                        if (ps.WeaponTime <= 0)
                        {
                            if ((_pm.Cmd.Buttons & Button.Attack) == 0)
                            { ps.WeaponTime = 0; return; }
                        }
                        else return;
                    }
                    if ((_pm.Cmd.Buttons & Button.Attack) != 0)
                    {
                        ps.WeapHeat[WP_DUMMY_MG42] += MG42_RATE_OF_FIRE_MP;
                        AddEvent(EV_FIRE_WEAPON_MG42);
                        ps.WeaponTime += MG42_RATE_OF_FIRE_MP;
                        // [ANIM] BG_AnimScriptEvent ANIM_ET_FIREWEAPON stub
                        ps.ViewLocked = 2;
                        if (ps.WeapHeat[WP_DUMMY_MG42] >= (int)GameConst.MAX_MG42_HEAT)
                        {
                            ps.WeapHeat[WP_DUMMY_MG42] = (int)GameConst.MAX_MG42_HEAT;
                            AddEvent(EV_WEAP_OVERHEAT);
                            ps.WeaponTime = 2000;
                        }
                    }
                    return;

                case 2:
                    if (ps.WeaponTime > 0)
                    {
                        ps.WeaponTime -= _pml.Msec;
                        if (ps.WeaponTime <= 0)
                        {
                            if ((_pm.Cmd.Buttons & Button.Attack) == 0)
                            { ps.WeaponTime = 0; return; }
                        }
                        else return;
                    }
                    if ((_pm.Cmd.Buttons & Button.Attack) != 0)
                    {
                        AddEvent(EV_FIRE_WEAPON_AAGUN);
                        ps.WeaponTime += AAGUN_RATE_OF_FIRE;
                        // [ANIM] BG_AnimScriptEvent ANIM_ET_FIREWEAPON stub
                    }
                    return;
            }

            // Mounted tank
            if ((ps.EFlags & EF_MOUNTEDTANK) != 0)
            {
                if (ps.WeapHeat[WP_DUMMY_MG42] != 0)
                {
                    ps.WeapHeat[WP_DUMMY_MG42] -= (int)(300f * _pml.FrameTime);
                    if (ps.WeapHeat[WP_DUMMY_MG42] < 0)
                        ps.WeapHeat[WP_DUMMY_MG42] = 0;
                    ps.CurWeapHeat = (int)Mathf.Floor(
                        ((float)ps.WeapHeat[WP_DUMMY_MG42] / GameConst.MAX_MG42_HEAT) * 255f);
                }
                if (ps.WeaponTime > 0)
                {
                    ps.WeaponTime -= _pml.Msec;
                    if (ps.WeaponTime <= 0)
                    {
                        if ((_pm.Cmd.Buttons & Button.Attack) == 0)
                        { ps.WeaponTime = 0; return; }
                    }
                    else return;
                }
                if ((_pm.Cmd.Buttons & Button.Attack) != 0)
                {
                    ps.WeapHeat[WP_DUMMY_MG42] += MG42_RATE_OF_FIRE_MP;
                    AddEvent(EV_FIRE_WEAPON_MOUNTEDMG42);
                    ps.WeaponTime += MG42_RATE_OF_FIRE_MP;
                    // [ANIM] BG_AnimScriptEvent ANIM_ET_FIREWEAPON stub
                    if (ps.WeapHeat[WP_DUMMY_MG42] >= (int)GameConst.MAX_MG42_HEAT)
                    {
                        ps.WeapHeat[WP_DUMMY_MG42] = (int)GameConst.MAX_MG42_HEAT;
                        AddEvent(EV_WEAP_OVERHEAT);
                        ps.WeaponTime = 2000;
                    }
                }
                return;
            }

            // Akimbo fire sequence
            if (BG_IsAkimboWeapon(ps.Weapon))
                akimboFire = BG_AkimboFireSequence(
                    ps.Weapon,
                    ps.AmmoClip[BG_FindClipForWeapon(ps.Weapon)],
                    ps.AmmoClip[BG_FindClipForWeapon(BG_AkimboSidearm(ps.Weapon))]);

            // Weapon cool down
            PM_CoolWeapons();

            // Weapon recoil
            if (pmext.WeapRecoilTime != 0)
            {
                int deltaTime = _pm.Cmd.ServerTime - pmext.WeapRecoilTime;

                float muzzlebounceX = ps.ViewAngles0;
                float muzzlebounceY = ps.ViewAngles1;
                float muzzlebounceZ = ps.ViewAngles2;

                if (deltaTime > pmext.WeapRecoilDuration)
                    deltaTime = pmext.WeapRecoilDuration;

                for (int i = pmext.LastRecoilDeltaTime; i < deltaTime; i += 15)
                {
                    if (pmext.WeapRecoilPitch > 0f)
                    {
                        muzzlebounceX -= 2f * pmext.WeapRecoilPitch *
                            Mathf.Cos(2.5f * i / pmext.WeapRecoilDuration);
                        muzzlebounceX -= 0.25f * UnityEngine.Random.value *
                            (1f - (float)i / pmext.WeapRecoilDuration);
                    }
                    if (pmext.WeapRecoilYaw > 0f)
                    {
                        muzzlebounceY += 0.5f * pmext.WeapRecoilYaw *
                            Mathf.Cos(1f - (float)i * 3f / pmext.WeapRecoilDuration);
                        muzzlebounceY += 0.5f * (UnityEngine.Random.value * 2f - 1f) *
                            (1f - (float)i / pmext.WeapRecoilDuration);
                    }
                }

                // Set delta angles
                ps.DeltaAngles[0] = AngleToShort(muzzlebounceX) - _pm.Cmd.Angles[0];
                ps.DeltaAngles[1] = AngleToShort(muzzlebounceY) - _pm.Cmd.Angles[1];
                ps.DeltaAngles[2] = AngleToShort(muzzlebounceZ) - _pm.Cmd.Angles[2];
                ps.ViewAngles0 = muzzlebounceX;
                ps.ViewAngles1 = muzzlebounceY;
                ps.ViewAngles2 = muzzlebounceZ;

                if (deltaTime == pmext.WeapRecoilDuration)
                {
                    pmext.WeapRecoilTime        = 0;
                    pmext.LastRecoilDeltaTime   = 0;
                }
                else
                {
                    pmext.LastRecoilDeltaTime = deltaTime;
                }
            }

            // Grenade/dynamite hold timer
            if (ps.Weapon == WP_GRENADE_LAUNCHER ||
                ps.Weapon == WP_GRENADE_PINEAPPLE ||
                ps.Weapon == WP_DYNAMITE ||
                ps.Weapon == WP_SMOKE_BOMB)
            {
                if (ps.GrenadeTimeLeft > 0)
                {
                    bool forcethrow = false;

                    if (ps.Weapon == WP_DYNAMITE)
                    {
                        ps.GrenadeTimeLeft += _pml.Msec;
                        if (ps.GrenadeTimeLeft < 5000)
                            ps.GrenadeTimeLeft = 5000;
                    }
                    else
                    {
                        ps.GrenadeTimeLeft -= _pml.Msec;
                        if (ps.GrenadeTimeLeft <= 100)
                        {
                            forcethrow         = true;
                            ps.GrenadeTimeLeft = 100;
                        }
                    }

                    if ((_pm.Cmd.Buttons & Button.Attack) == 0 ||
                        forcethrow ||
                        (ps.EFlags & GameConst.EF_PRONE_MOVING) != 0)
                    {
                        // Fire event handled below
                    }
                    else
                    {
                        return;
                    }
                }
            }

            // Weapon delay countdown
            if (ps.WeaponDelay > 0)
            {
                ps.WeaponDelay -= _pml.Msec;
                if (ps.WeaponDelay <= 0)
                {
                    ps.WeaponDelay = 0;
                    delayedFire    = true;
                }
            }

            // Relaxing → ready
            if (ps.Weaponstate == WEAPON_RELAXING)
            {
                ps.Weaponstate = GameConst.WEAPON_READY;
                return;
            }

            // Prone-moving blocks most weapon actions
            if ((ps.EFlags & GameConst.EF_PRONE_MOVING) != 0 && !delayedFire)
                return;

            // Make weapon function: count down weaponTime
            bool weaponstateFiring = (ps.Weaponstate == WEAPON_FIRING ||
                                      ps.Weaponstate == GameConst.WEAPON_FIRINGALT);

            if (ps.WeaponTime > 0)
            {
                ps.WeaponTime -= _pml.Msec;
                if ((_pm.Cmd.Buttons & Button.Attack) == 0 && ps.WeaponTime < 0)
                    ps.WeaponTime = 0;

                // Quick-fire for semi-auto pistols / rifles
                if (ps.Weapon == GameConst.WP_LUGER   ||
                    ps.Weapon == GameConst.WP_COLT     ||
                    ps.Weapon == GameConst.WP_SILENCER ||
                    ps.Weapon == WP_SILENCED_COLT      ||
                    ps.Weapon == GameConst.WP_KAR98    ||
                    ps.Weapon == GameConst.WP_K43      ||
                    ps.Weapon == GameConst.WP_CARBINE  ||
                    ps.Weapon == GameConst.WP_GARAND   ||
                    ps.Weapon == GameConst.WP_GARAND_SCOPE ||
                    ps.Weapon == GameConst.WP_K43_SCOPE    ||
                    BG_IsAkimboWeapon(ps.Weapon))
                {
                    if (pmext.ReleasedFire)
                    {
                        if ((_pm.Cmd.Buttons & Button.Attack) != 0)
                        {
                            if (BG_IsAkimboWeapon(ps.Weapon))
                            { if (ps.WeaponTime <= 50)  ps.WeaponTime = 0; }
                            else
                            { if (ps.WeaponTime <= 150) ps.WeaponTime = 0; }
                        }
                    }
                    else if ((_pm.Cmd.Buttons & Button.Attack) == 0)
                    {
                        pmext.ReleasedFire = true;
                    }
                }
            }

            // Check for weapon change
            if ((ps.WeaponTime <= 0 ||
                 (!weaponstateFiring && ps.WeaponDelay <= 0)) && !delayedFire)
            {
                if (ps.Weapon != _pm.Cmd.Weapon)
                    PM_BeginWeaponChange(ps.Weapon, _pm.Cmd.Weapon, false);
            }

            if (ps.WeaponDelay > 0)
                return;

            // Check for clip/reload
            PM_CheckForReload(ps.Weapon);

            if (ps.WeaponTime > 0 || ps.WeaponDelay > 0)
                return;

            if (ps.Weaponstate == GameConst.WEAPON_RELOADING)
                PM_FinishWeaponReload();

            // Change weapon if time
            if (ps.Weaponstate == GameConst.WEAPON_DROPPING ||
                ps.Weaponstate == WEAPON_DROPPING_TORELOAD)
            {
                PM_FinishWeaponChange();
                return;
            }

            if (ps.Weaponstate == GameConst.WEAPON_RAISING)
            {
                ps.Weaponstate = GameConst.WEAPON_READY;
                PM_StartWeaponAnim(PM_IdleAnimForWeapon(ps.Weapon));
                return;
            }
            else if (ps.Weaponstate == WEAPON_RAISING_TORELOAD)
            {
                ps.Weaponstate = GameConst.WEAPON_READY;
                PM_BeginWeaponReload(ps.Weapon);
                return;
            }

            if (ps.Weapon == GameConst.WP_NONE)
                return;

            // Charge-time checks (MP)
            if (ps.Weapon == GameConst.WP_PANZERFAUST)
            {
                if ((ps.EFlags & GameConst.EF_PRONE) != 0)
                    return;
                if (_pm.Skill[GameConst.SK_HEAVY_WEAPONS] >= 1)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.SoldierChargeTime * 0.66f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.SoldierChargeTime)         return; }
            }

            if (ps.Weapon == WP_GPG40 || ps.Weapon == WP_M7)
            {
                if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.EngineerChargeTime * 0.5f)
                    return;
            }

            if (ps.Weapon == GameConst.WP_MORTAR_SET)
            {
                if (_pm.Skill[GameConst.SK_HEAVY_WEAPONS] >= 1)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.SoldierChargeTime * 0.5f * 0.7f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.SoldierChargeTime * 0.5f)        return; }
                if (!delayedFire)
                    ps.Weaponstate = GameConst.WEAPON_READY;
            }

            if (ps.Weapon == WP_SMOKE_BOMB || ps.Weapon == GameConst.WP_SATCHEL)
            {
                if (_pm.Skill[GameConst.SK_COVERTOPS] >= 2)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.CovertopsChargeTime * 0.66f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.CovertopsChargeTime)         return; }
            }

            if (ps.Weapon == WP_LANDMINE)
            {
                if (_pm.Skill[GameConst.SK_ENGINEER] >= 2)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.EngineerChargeTime * 0.33f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.EngineerChargeTime * 0.5f) return; }
            }

            if (ps.Weapon == WP_DYNAMITE)
            {
                if (_pm.Skill[GameConst.SK_ENGINEER] >= 3)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.EngineerChargeTime * 0.66f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.EngineerChargeTime)         return; }
            }

            if (ps.Weapon == GameConst.WP_AMMO)
            {
                if (_pm.Skill[GameConst.SK_FIELDOPS] >= 1)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.LtChargeTime * 0.15f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.LtChargeTime * 0.25f) return; }
            }

            if (ps.Weapon == WP_MEDKIT)
            {
                if (_pm.Skill[GameConst.SK_MEDIC] >= 2)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.MedicChargeTime * 0.15f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.MedicChargeTime * 0.25f) return; }
            }

            if (ps.Weapon == WP_SMOKE_MARKER)
            {
                if (_pm.Skill[GameConst.SK_FIELDOPS] >= 2)
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.LtChargeTime * 0.66f) return; }
                else
                { if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.LtChargeTime)         return; }
            }

            if (ps.Weapon == WP_MEDIC_ADRENALINE)
            {
                if (_pm.Cmd.ServerTime - ps.ClassWeaponTime < _pm.MedicChargeTime)
                    return;
            }

            // Check for fire button / leaning blocks firing
            // C: !( pm->cmd.buttons & ( BUTTON_ATTACK | WBUTTON_ATTACK2 ) )
            // Here BUTTON_ATTACK is in Buttons and WBUTTON_ATTACK2 (Attack2) is in WButtons.
            bool attackHeld = ((_pm.Cmd.Buttons  & Button.Attack)   != 0) ||
                              ((_pm.Cmd.WButtons & WButton.Attack2) != 0);
            if ((!attackHeld && !delayedFire) ||
                (ps.Leanf != 0f &&
                 ps.Weapon != WP_GRENADE_LAUNCHER &&
                 ps.Weapon != WP_GRENADE_PINEAPPLE &&
                 ps.Weapon != WP_SMOKE_BOMB))
            {
                ps.WeaponTime  = 0;
                ps.WeaponDelay = 0;
                if (weaponstateFiring)
                    PM_ContinueWeaponAnim(PM_IdleAnimForWeapon(ps.Weapon));
                ps.Weaponstate = GameConst.WEAPON_READY;
                return;
            }

            // An un-mounted mortar can't fire
            if (ps.Weapon == GameConst.WP_MORTAR)
                return;

            // Zooming blocks fire (except fieldops calling artillery on server side — stub)
            if ((ps.EFlags & GameConst.EF_ZOOMING) != 0)
                return;

            // Underwater blocks fire for most weapons
            if (_pm.WaterLevel == 3)
            {
                if (ps.Weapon != GameConst.WP_KNIFE &&
                    ps.Weapon != WP_GRENADE_LAUNCHER &&
                    ps.Weapon != WP_GRENADE_PINEAPPLE &&
                    ps.Weapon != WP_DYNAMITE &&
                    ps.Weapon != WP_LANDMINE &&
                    ps.Weapon != WP_TRIPMINE &&
                    ps.Weapon != WP_SMOKE_BOMB)
                {
                    AddEvent(EV_NOFIRE_UNDERWATER);
                    ps.WeaponTime  = 500;
                    ps.WeaponDelay = 0;
                    return;
                }
            }

            // Start animation / set delay
            switch (ps.Weapon)
            {
                // Machine-guns: continue the anim rather than restart each shot
                case int w when (w == GameConst.WP_MP40   ||
                                 w == GameConst.WP_THOMPSON||
                                 w == GameConst.WP_STEN    ||
                                 w == WP_MEDKIT            ||
                                 w == WP_PLIERS            ||
                                 w == WP_SMOKE_MARKER      ||
                                 w == GameConst.WP_FG42    ||
                                 w == GameConst.WP_FG42SCOPE||
                                 w == GameConst.WP_MOBILE_MG42    ||
                                 w == GameConst.WP_MOBILE_MG42_SET||
                                 w == WP_LOCKPICK):
                    if (!weaponstateFiring)
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                    // else: [ANIM] BG_AnimScriptEvent ANIM_ET_FIREWEAPON stub
                    break;

                // Single-shot pistols / rifles
                case int w when (w == GameConst.WP_PANZERFAUST   ||
                                 w == GameConst.WP_LUGER          ||
                                 w == GameConst.WP_COLT           ||
                                 w == GameConst.WP_GARAND         ||
                                 w == GameConst.WP_K43            ||
                                 w == GameConst.WP_KAR98          ||
                                 w == GameConst.WP_CARBINE        ||
                                 w == WP_GPG40                    ||
                                 w == WP_M7                       ||
                                 w == GameConst.WP_SILENCER       ||
                                 w == WP_SILENCED_COLT            ||
                                 w == GameConst.WP_GARAND_SCOPE   ||
                                 w == GameConst.WP_K43_SCOPE      ||
                                 w == GameConst.WP_AKIMBO_COLT    ||
                                 w == WP_AKIMBO_SILENCEDCOLT      ||
                                 w == GameConst.WP_AKIMBO_LUGER   ||
                                 w == WP_AKIMBO_SILENCEDLUGER):
                    if (!weaponstateFiring)
                    {
                        if (ps.Weapon == GameConst.WP_PANZERFAUST)
                            AddEvent(EV_SPINUP);
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                    }
                    // else: [ANIM] BG_AnimScriptEvent stubs
                    break;

                case int w when (w == GameConst.WP_MORTAR_SET):
                    if (!weaponstateFiring)
                    {
                        AddEvent(EV_SPINUP);
                        PM_StartWeaponAnim(PM_AttackAnimForWeapon(GameConst.WP_MORTAR_SET));
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                    }
                    // else: [ANIM] stub
                    break;

                case int w when (w == GameConst.WP_KNIFE):
                    if (!delayedFire)
                    {
                        // [ANIM] BG_AnimScriptEvent ANIM_ET_FIREWEAPON stub
                    }
                    break;

                case int w when (w == WP_DYNAMITE             ||
                                 w == WP_GRENADE_LAUNCHER ||
                                 w == WP_GRENADE_PINEAPPLE||
                                 w == WP_SMOKE_BOMB):
                    if (!delayedFire)
                    {
                        if (PM_WeaponAmmoAvailable(ps.Weapon) != 0)
                        {
                            if (ps.Weapon == WP_DYNAMITE)
                                ps.GrenadeTimeLeft = 50;
                            else
                                ps.GrenadeTimeLeft = 4000;
                            PM_StartWeaponAnim(PM_AttackAnimForWeapon(ps.Weapon));
                        }
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                    }
                    break;

                case int w when (w == WP_LANDMINE):
                    if (!delayedFire)
                    {
                        // [ANIM] BG_AnimScriptEvent stubs
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                    }
                    break;

                case int w when (w == WP_TRIPMINE || w == GameConst.WP_SATCHEL):
                    if (!delayedFire)
                    {
                        if (PM_WeaponAmmoAvailable(ps.Weapon) != 0)
                            PM_StartWeaponAnim(PM_AttackAnimForWeapon(ps.Weapon));
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                    }
                    break;

                case int w when (w == GameConst.WP_SATCHEL_DET):
                    if (!weaponstateFiring)
                    {
                        AddEvent(EV_SPINUP);
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                        PM_ContinueWeaponAnim(PM_AttackAnimForWeapon(GameConst.WP_SATCHEL_DET));
                    }
                    // else: [ANIM] stub
                    break;

                default:
                    if (!weaponstateFiring)
                        ps.WeaponDelay = GetAmmoTableData(ps.Weapon).fireDelayTime;
                    // else: [ANIM] stub
                    break;
            }

            ps.Weaponstate = WEAPON_FIRING;

            // Check for out of ammo
            ammoNeeded = GetAmmoTableData(ps.Weapon).uses;

            if (ps.Weapon != GameConst.WP_NONE)
            {
                int ammoAvailable = PM_WeaponAmmoAvailable(ps.Weapon);

                if (ammoNeeded > ammoAvailable)
                {
                    // Check if reserve ammo could fill clip
                    int ammoWeap = BG_FindAmmoForWeapon(ps.Weapon);
                    bool reloading = (ammoNeeded <= ps.Ammo[ammoWeap]);

                    // Auto-reload check
                    if (!pmext.AutoReload && !(_pm.Cmd.WButtons != 0 && (_pm.Cmd.WButtons & WButton.Reload) != 0))
                        reloading = false;

                    bool playswitchsound = true;
                    switch (ps.Weapon)
                    {
                        case int w when (w == WP_DYNAMITE             ||
                                         w == WP_GRENADE_LAUNCHER ||
                                         w == WP_GRENADE_PINEAPPLE||
                                         w == WP_LANDMINE              ||
                                         w == WP_TRIPMINE              ||
                                         w == WP_SMOKE_BOMB):
                            playswitchsound = false;
                            break;
                        case int w when (w == GameConst.WP_FG42SCOPE  ||
                                         w == GameConst.WP_GARAND_SCOPE||
                                         w == GameConst.WP_K43_SCOPE):
                            reloading = false;
                            break;
                    }

                    if (playswitchsound)
                    {
                        if (reloading) AddEvent(EV_EMPTYCLIP);
                        else           AddEvent(EV_NOAMMO);
                    }

                    if (reloading)
                        PM_ContinueWeaponAnim(PM_ReloadAnimForWeapon(ps.Weapon));
                    else
                    {
                        PM_ContinueWeaponAnim(PM_IdleAnimForWeapon(ps.Weapon));
                        ps.WeaponTime += 500;
                    }
                    return;
                }
            }

            // If still in delay, return (animations started, events not yet fired)
            if (ps.WeaponDelay > 0)
                return;

            // Knockback on slick surface (simplified)
            // (DotProduct/VectorScale stubs — skipped, physics handled by PhysX)

            // Consume ammo
            if (PM_WeaponAmmoAvailable(ps.Weapon) != -1)
            {
                if (ps.Persistant[GameConst.PERS_HWEAPON_USE] == 0 &&
                    (ps.EFlags & EF_MOUNTEDTANK) == 0)
                {
                    PM_WeaponUseAmmo(ps.Weapon, ammoNeeded);
                }
            }

            // Add weapon heat
            if (GetAmmoTableData(ps.Weapon).maxHeat != 0)
                ps.WeapHeat[ps.Weapon] += GetAmmoTableData(ps.Weapon).nextShotTime;

            // Select attack animation
            if (BG_IsAkimboWeapon(ps.Weapon))
            {
                weapattackanim = akimboFire ? WEAP_ATTACK1 : WEAP_ATTACK2;
            }
            else
            {
                weapattackanim = (PM_WeaponClipEmpty(ps.Weapon) != 0)
                    ? PM_LastAttackAnimForWeapon(ps.Weapon)
                    : PM_AttackAnimForWeapon(ps.Weapon);
            }

            // Play attack animation
            switch (ps.Weapon)
            {
                case int w when (w == WP_GRENADE_LAUNCHER ||
                                 w == WP_GRENADE_PINEAPPLE||
                                 w == WP_DYNAMITE                   ||
                                 w == GameConst.WP_K43              ||
                                 w == GameConst.WP_KAR98            ||
                                 w == WP_GPG40                      ||
                                 w == GameConst.WP_CARBINE          ||
                                 w == WP_M7                         ||
                                 w == WP_LANDMINE                   ||
                                 w == WP_TRIPMINE                   ||
                                 w == WP_SMOKE_BOMB):
                    PM_StartWeaponAnim(weapattackanim);
                    break;

                case int w when (w == GameConst.WP_MP40          ||
                                 w == GameConst.WP_THOMPSON        ||
                                 w == GameConst.WP_STEN            ||
                                 w == WP_MEDKIT                    ||
                                 w == WP_PLIERS                    ||
                                 w == WP_SMOKE_MARKER              ||
                                 w == GameConst.WP_SATCHEL_DET     ||
                                 w == GameConst.WP_MOBILE_MG42     ||
                                 w == GameConst.WP_MOBILE_MG42_SET ||
                                 w == WP_LOCKPICK):
                    PM_ContinueWeaponAnim(weapattackanim);
                    break;

                case int w when (w == GameConst.WP_MORTAR_SET):
                    // no animation
                    break;

                default:
                    PM_StartWeaponAnim(weapattackanim);
                    break;
            }

            // Post-fire special cases
            if (ps.Weapon == GameConst.WP_PANZERFAUST ||
                ps.Weapon == WP_SMOKE_MARKER           ||
                ps.Weapon == WP_DYNAMITE               ||
                ps.Weapon == WP_SMOKE_BOMB             ||
                ps.Weapon == WP_LANDMINE               ||
                ps.Weapon == GameConst.WP_SATCHEL)
            {
                AddEvent(EV_NOAMMO);
            }

            // Satchel: arm detonator
            if (ps.Weapon == GameConst.WP_SATCHEL)
            {
                ps.AmmoClip[GameConst.WP_SATCHEL_DET] = 1;
                ps.Ammo[GameConst.WP_SATCHEL]         = 0;
                ps.AmmoClip[GameConst.WP_SATCHEL]     = 0;
                PM_BeginWeaponChange(GameConst.WP_SATCHEL, GameConst.WP_SATCHEL_DET, false);
            }

            // M7/GPG40: no ammo after last grenade
            if ((ps.Weapon == WP_M7 || ps.Weapon == WP_GPG40) &&
                ps.Ammo[BG_FindAmmoForWeapon(ps.Weapon)] == 0)
            {
                AddEvent(EV_NOAMMO);
            }

            // Mortar set: no ammo
            if (ps.Weapon == GameConst.WP_MORTAR_SET &&
                ps.Ammo[GameConst.WP_MORTAR] == 0)
            {
                AddEvent(EV_NOAMMO);
            }

            // Fire event
            if (BG_IsAkimboWeapon(ps.Weapon))
            {
                AddEvent(akimboFire ? EV_FIRE_WEAPON : EV_FIRE_WEAPONB);
            }
            else
            {
                AddEvent((PM_WeaponClipEmpty(ps.Weapon) != 0)
                    ? EV_FIRE_WEAPON_LASTSHOT : EV_FIRE_WEAPON);
            }

            pmext.ReleasedFire = false;
            ps.LastFireTime    = _pm.Cmd.ServerTime;

            aimSpreadScaleAdd = 0;

            // Per-weapon addTime and aim-spread
            switch (ps.Weapon)
            {
                case int w when (w == GameConst.WP_KNIFE            ||
                                 w == GameConst.WP_PANZERFAUST       ||
                                 w == WP_DYNAMITE                    ||
                                 w == WP_GRENADE_LAUNCHER  ||
                                 w == WP_GRENADE_PINEAPPLE ||
                                 w == GameConst.WP_FLAMETHROWER      ||
                                 w == WP_GPG40                       ||
                                 w == WP_M7                          ||
                                 w == WP_LANDMINE                    ||
                                 w == WP_TRIPMINE                    ||
                                 w == WP_SMOKE_BOMB                  ||
                                 w == GameConst.WP_MORTAR_SET):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    break;

                case int w when (w == GameConst.WP_LUGER || w == GameConst.WP_SILENCER):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = 20;
                    break;

                case int w when (w == GameConst.WP_COLT || w == WP_SILENCED_COLT):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = 20;
                    break;

                case int w when (w == GameConst.WP_AKIMBO_COLT || w == WP_AKIMBO_SILENCEDCOLT):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    if (ps.AmmoClip[BG_FindClipForWeapon(ps.Weapon)] == 0)
                    { if (!akimboFire) addTime = 2 * GetAmmoTableData(ps.Weapon).nextShotTime; }
                    else if (ps.AmmoClip[BG_FindClipForWeapon(BG_AkimboSidearm(ps.Weapon))] == 0)
                    { if (akimboFire)  addTime = 2 * GetAmmoTableData(ps.Weapon).nextShotTime; }
                    aimSpreadScaleAdd = 20;
                    break;

                case int w when (w == GameConst.WP_AKIMBO_LUGER || w == WP_AKIMBO_SILENCEDLUGER):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    if (ps.AmmoClip[BG_FindClipForWeapon(ps.Weapon)] == 0)
                    { if (!akimboFire) addTime = 2 * GetAmmoTableData(ps.Weapon).nextShotTime; }
                    else if (ps.AmmoClip[BG_FindClipForWeapon(BG_AkimboSidearm(ps.Weapon))] == 0)
                    { if (akimboFire)  addTime = 2 * GetAmmoTableData(ps.Weapon).nextShotTime; }
                    aimSpreadScaleAdd = 20;
                    break;

                case int w when (w == GameConst.WP_GARAND ||
                                 w == GameConst.WP_K43    ||
                                 w == GameConst.WP_KAR98  ||
                                 w == GameConst.WP_CARBINE):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = 50;
                    break;

                case int w when (w == GameConst.WP_GARAND_SCOPE || w == GameConst.WP_K43_SCOPE):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = 200;
                    break;

                case int w when (w == GameConst.WP_FG42 || w == GameConst.WP_FG42SCOPE):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = (int)(200 / 2f);
                    break;

                case int w when (w == GameConst.WP_MP40 || w == GameConst.WP_THOMPSON):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = 15 + UnityEngine.Random.Range(0, 10);
                    break;

                case int w when (w == GameConst.WP_STEN):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = 15 + UnityEngine.Random.Range(0, 10);
                    break;

                case int w when (w == GameConst.WP_MOBILE_MG42 || w == GameConst.WP_MOBILE_MG42_SET):
                    addTime = (weapattackanim == WEAP_ATTACK_LASTSHOT)
                        ? 2000
                        : GetAmmoTableData(ps.Weapon).nextShotTime;
                    aimSpreadScaleAdd = 20;
                    break;

                case int w when (w == GameConst.WP_MEDIC_SYRINGE ||
                                 w == WP_MEDIC_ADRENALINE         ||
                                 w == GameConst.WP_AMMO           ||
                                 w == WP_LOCKPICK):
                    addTime = GetAmmoTableData(ps.Weapon).nextShotTime;
                    break;

                case int w when (w == WP_PLIERS):
                    addTime = 50;
                    break;

                case int w when (w == WP_MEDKIT):
                    addTime = 1000;
                    break;

                case int w when (w == WP_SMOKE_MARKER):
                    addTime = 1000;
                    break;

                default:
                    break;
            }

            // Set weapon recoil
            pmext.LastRecoilDeltaTime = 0;

            if (ps.Weapon == GameConst.WP_GARAND_SCOPE ||
                ps.Weapon == GameConst.WP_K43_SCOPE)
            {
                pmext.WeapRecoilTime     = _pm.Cmd.ServerTime;
                pmext.WeapRecoilDuration = 300;
                pmext.WeapRecoilYaw      = (UnityEngine.Random.value * 2f - 1f) * 0.5f;
                pmext.WeapRecoilPitch    = (_pm.Skill[GameConst.SK_COVERTOPS] >= 3) ? 0.25f : 0.5f;
            }
            else if (ps.Weapon == GameConst.WP_MOBILE_MG42)
            {
                pmext.WeapRecoilTime     = _pm.Cmd.ServerTime;
                pmext.WeapRecoilDuration = 200;
                if ((ps.PmFlags & GameConst.PMF_DUCKED) != 0 ||
                    (ps.EFlags  & GameConst.EF_PRONE)   != 0)
                {
                    pmext.WeapRecoilYaw   = (UnityEngine.Random.value * 2f - 1f) * 0.5f;
                    pmext.WeapRecoilPitch = 0.45f * UnityEngine.Random.value * 0.15f;
                }
                else
                {
                    pmext.WeapRecoilYaw   = (UnityEngine.Random.value * 2f - 1f) * 0.25f;
                    pmext.WeapRecoilPitch = 0.75f * UnityEngine.Random.value * 0.2f;
                }
            }
            else if (ps.Weapon == GameConst.WP_FG42SCOPE)
            {
                pmext.WeapRecoilTime     = _pm.Cmd.ServerTime;
                pmext.WeapRecoilDuration = 100;
                pmext.WeapRecoilYaw      = 0f;
                pmext.WeapRecoilPitch    = 0.45f * UnityEngine.Random.value * 0.15f;
                if (_pm.Skill[GameConst.SK_COVERTOPS] >= 3)
                    pmext.WeapRecoilPitch *= 0.5f;
            }
            else if (ps.Weapon == GameConst.WP_LUGER          ||
                     ps.Weapon == GameConst.WP_SILENCER        ||
                     ps.Weapon == GameConst.WP_AKIMBO_LUGER    ||
                     ps.Weapon == WP_AKIMBO_SILENCEDLUGER      ||
                     ps.Weapon == GameConst.WP_COLT            ||
                     ps.Weapon == WP_SILENCED_COLT             ||
                     ps.Weapon == GameConst.WP_AKIMBO_COLT     ||
                     ps.Weapon == WP_AKIMBO_SILENCEDCOLT)
            {
                pmext.WeapRecoilTime     = _pm.Cmd.ServerTime;
                pmext.WeapRecoilDuration = (_pm.Skill[GameConst.SK_LIGHT_WEAPONS] >= 3) ? 70 : 100;
                pmext.WeapRecoilYaw      = 0f;
                pmext.WeapRecoilPitch    = (_pm.Skill[GameConst.SK_LIGHT_WEAPONS] >= 3)
                    ? 0.25f * UnityEngine.Random.value * 0.15f
                    : 0.45f * UnityEngine.Random.value * 0.15f;
            }
            else
            {
                pmext.WeapRecoilTime = 0;
                pmext.WeapRecoilYaw  = 0f;
            }

            // Check for overheat
            if (GetAmmoTableData(ps.Weapon).maxHeat != 0 && ps.WeapHeat[ps.Weapon] != 0)
            {
                if (ps.WeapHeat[ps.Weapon] >= GetAmmoTableData(ps.Weapon).maxHeat)
                {
                    ps.WeapHeat[ps.Weapon] = GetAmmoTableData(ps.Weapon).maxHeat;
                    AddEvent(EV_WEAP_OVERHEAT);
                    addTime = 2000;
                }
            }

            // Add aim spread
            ps.AimSpreadScaleFloat += 3.0f * aimSpreadScaleAdd;
            if (ps.AimSpreadScaleFloat > 255f)
                ps.AimSpreadScaleFloat = 255f;

            if (_pm.Skill[GameConst.SK_COVERTOPS] >= 3 &&
                ps.Stats[GameConst.STAT_PLAYER_CLASS] == GameConst.PC_COVERTOPS)
            {
                ps.AimSpreadScaleFloat *= 0.5f;
            }

            ps.AimSpreadScale = (int)ps.AimSpreadScaleFloat;
            ps.WeaponTime    += addTime;

            PM_SwitchIfEmpty();
        }

        // -----------------------------------------------------------------------
        // PM_WeaponUseAmmo / PM_WeaponAmmoAvailable / PM_WeaponClipEmpty
        // These three are called by the (future) weapon layer so are public.
        // They follow the C logic: if noWeapClips, use ammo[]; else use ammoClip[].
        // BG_FindAmmoForWeapon / BG_FindClipForWeapon are identity-mapped here
        // (weapon index == ammo index) until the weapon table is ported.
        // -----------------------------------------------------------------------
        public void PM_WeaponUseAmmo(int wp, int amount)
        {
            if (_pm.NoWeapClips)
            {
                if (wp >= 0 && wp < _pm.Ps.Ammo.Length)
                    _pm.Ps.Ammo[wp] -= (short)amount;
            }
            else
            {
                if (wp >= 0 && wp < _pm.Ps.AmmoClip.Length)
                    _pm.Ps.AmmoClip[wp] -= (short)amount;
            }
        }

        public int PM_WeaponAmmoAvailable(int wp)
        {
            if (_pm.NoWeapClips)
                return (wp >= 0 && wp < _pm.Ps.Ammo.Length)     ? _pm.Ps.Ammo[wp]     : 0;
            return     (wp >= 0 && wp < _pm.Ps.AmmoClip.Length) ? _pm.Ps.AmmoClip[wp] : 0;
        }

        public int PM_WeaponClipEmpty(int wp)
        {
            if (_pm.NoWeapClips)
                return (wp >= 0 && wp < _pm.Ps.Ammo.Length     && _pm.Ps.Ammo[wp]     == 0) ? 1 : 0;
            return     (wp >= 0 && wp < _pm.Ps.AmmoClip.Length && _pm.Ps.AmmoClip[wp] == 0) ? 1 : 0;
        }

        // -----------------------------------------------------------------------
        // PM_DropTimers
        // -----------------------------------------------------------------------
        private void PM_DropTimers()
        {
            if (_pm.Ps.PmTime > 0)
            {
                if (_pml.Msec >= _pm.Ps.PmTime)
                {
                    _pm.Ps.PmFlags &= ~GameConst.PMF_ALL_TIMES;
                    _pm.Ps.PmTime   = 0;
                }
                else _pm.Ps.PmTime -= _pml.Msec;
            }

            if (_pm.Ps.LegsTimer  > 0) { _pm.Ps.LegsTimer  -= _pml.Msec; if (_pm.Ps.LegsTimer  < 0) _pm.Ps.LegsTimer  = 0; }
            if (_pm.Ps.TorsoTimer > 0) { _pm.Ps.TorsoTimer -= _pml.Msec; if (_pm.Ps.TorsoTimer < 0) _pm.Ps.TorsoTimer = 0; }
            if (_pm.Pmext.WeapAnimTimer > 0) { _pm.Pmext.WeapAnimTimer -= _pml.Msec; if (_pm.Pmext.WeapAnimTimer < 0) _pm.Pmext.WeapAnimTimer = 0; }
        }

        // -----------------------------------------------------------------------
        // PM_UpdateLean
        // -----------------------------------------------------------------------
        private void PM_UpdateLean(
            PlayerState ps, UserCmd cmd,
            TraceFunc traceFn)
        {
            int leaning = 0;
            if (((cmd.WButtons & (WButton.LeanLeft | WButton.LeanRight)) != 0) &&
                cmd.ForwardMove == 0 && cmd.UpMove <= 0)
            {
                if ((cmd.WButtons & WButton.LeanLeft)  != 0) leaning--;
                if ((cmd.WButtons & WButton.LeanRight) != 0) leaning++;
            }

            if (BG_PlayerMounted(ps.EFlags)) leaning = 0;
            if ((ps.EFlags & GameConst.EF_FIRING) != 0) leaning = 0;
            if (ps.Weaponstate == WEAPON_FIRING && ps.Weapon == WP_DYNAMITE) leaning = 0;
            if ((ps.EFlags & GameConst.EF_PRONE) != 0 || ps.Weapon == GameConst.WP_MORTAR_SET) leaning = 0;

            float leanofs = ps.Leanf;
            float msecF   = _pml.Msec;

            if (leaning == 0)
            {
                if (leanofs > 0f)
                { leanofs -= (msecF / LeanTimeFr) * LeanMax; if (leanofs < 0f) leanofs = 0f; }
                else if (leanofs < 0f)
                { leanofs += (msecF / LeanTimeFr) * LeanMax; if (leanofs > 0f) leanofs = 0f; }
            }
            else if (leaning > 0)
            {
                leanofs += (msecF / LeanTimeTo) * LeanMax;
                if (leanofs > LeanMax) leanofs = LeanMax;
            }
            else
            {
                leanofs -= (msecF / LeanTimeTo) * LeanMax;
                if (leanofs < -LeanMax) leanofs = -LeanMax;
            }
            ps.Leanf = leanofs;

            if (leaning != 0)
            {
                Vector3 start      = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2 + ps.ViewHeight);
                Vector3 viewAngles = new Vector3(ps.ViewAngles0, ps.ViewAngles1, ps.ViewAngles2 + leanofs * 0.5f);
                ETMath.AngleVectors(viewAngles, out _, out Vector3 right, out _);
                Vector3 end = start + right * leanofs;

                Vector3 tmins = new Vector3(-8f, -8f, -7f);
                Vector3 tmaxs = new Vector3( 8f,  8f,  4f);
                TraceResult tr = traceFn(start, tmins, tmaxs, end, ps.ClientNum, Contents.MaskPlayerSolid);
                ps.Leanf *= tr.Fraction;
            }

            if (ps.Leanf != 0f) cmd.RightMove = 0;
        }

        // -----------------------------------------------------------------------
        // PM_CheckLadderMove
        // -----------------------------------------------------------------------
        private void PM_CheckLadderMove()
        {
            const float TRACE_LADDER_DIST = 48f;
            if (_pm.Ps.PmTime != 0) return;

            if (_pm.Ps.Stats[GameConst.STAT_HEALTH] <= 0)
            {
                _pm.Ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
                _pml.GroundPlane = false;
                _pml.Walking     = false;
                return;
            }

            if ((_pm.Ps.EFlags & GameConst.EF_PRONE) != 0) return;

            bool wasOnLadder = (_pm.Ps.PmFlags & GameConst.PMF_LADDER) != 0;
            _pml.Ladder      = false;
            _pm.Ps.PmFlags  &= ~GameConst.PMF_LADDER;
            _ladderForward   = false;

            float tracedist = _pml.Walking ? 1f : TRACE_LADDER_DIST;

            Vector3 ff = new Vector3(_pml.Forward.x, _pml.Forward.y, 0f);
            if (ff.magnitude > 0f) ff = ff.normalized;

            Vector3 origin = new Vector3(_pm.Ps.Origin0, _pm.Ps.Origin1, _pm.Ps.Origin2);
            TraceResult trace = _pm.Trace(
                origin, _pm.Mins, _pm.Maxs,
                origin + ff * tracedist, _pm.Ps.ClientNum, _pm.TraceMask);

            if (trace.Fraction < 1f && (trace.SurfaceFlags & Surf.Ladder) != 0)
            {
                _pml.Ladder = true;
                _ladderVec  = trace.PlaneNormal;
            }

            if (_pml.Ladder && !_pml.Walking && trace.Fraction * tracedist > 1f)
            {
                _pml.Ladder = false;
                Vector3 mins = _pm.Mins; mins.z = -1f;
                trace = _pm.Trace(
                    origin, mins, _pm.Maxs,
                    origin - _ladderVec * tracedist, _pm.Ps.ClientNum, _pm.TraceMask);
                if (trace.Fraction < 1f && (trace.SurfaceFlags & Surf.Ladder) != 0)
                {
                    _ladderForward  = true;
                    _pml.Ladder     = true;
                    _pm.Ps.PmFlags |= GameConst.PMF_LADDER;
                }
            }
            else if (_pml.Ladder)
            {
                _pm.Ps.PmFlags |= GameConst.PMF_LADDER;
            }

            if (_pml.Ladder && _pml.Walking && _pm.Cmd.ForwardMove <= 0)
                _pml.Ladder = false;

            if (!_pml.Ladder && wasOnLadder && _pm.Ps.Velocity2 > 0f)
            {
                // [ANIM] BG_AnimScriptEvent ANIM_ET_CLIMB_DISMOUNT stub
            }
            if (_pml.Ladder && !wasOnLadder && _pm.Ps.Velocity2 < 0f)
            {
                // [ANIM] BG_AnimScriptEvent ANIM_ET_CLIMB_MOUNT stub
            }
        }

        // -----------------------------------------------------------------------
        // PM_LadderMove
        // -----------------------------------------------------------------------
        private void PM_LadderMove()
        {
            if (_ladderForward)
            {
                _pm.Ps.Velocity0 = -_ladderVec.x * 200f;
                _pm.Ps.Velocity1 = -_ladderVec.y * 200f;
            }

            float upscale = Mathf.Clamp((_pml.Forward.z + 0.5f) * 2.5f, -1f, 1f);

            Vector3 fwd   = new Vector3(_pml.Forward.x, _pml.Forward.y, 0f);
            Vector3 right = new Vector3(_pml.Right.x,   _pml.Right.y,   0f);
            if (fwd.magnitude   > 0f) fwd   = fwd.normalized;
            if (right.magnitude > 0f) right = right.normalized;

            float scale    = PM_CmdScale(_pm.Cmd);
            Vector3 wishvel = Vector3.zero;

            if (_pm.Cmd.ForwardMove != 0)
                wishvel.z = 0.9f * upscale * scale * _pm.Cmd.ForwardMove;

            if (_pm.Cmd.RightMove != 0)
            {
                Vector3 ladderAng = ETMath.VecToAngles(_ladderVec);
                ETMath.AngleVectors(ladderAng, out _, out Vector3 ladderRight, out _);
                if (Vector3.Dot(_ladderVec, _pml.Forward) < 0f) ladderRight = -ladderRight;
                wishvel += ladderRight * (0.5f * scale * _pm.Cmd.RightMove);
            }

            PM_Friction();

            if (_pm.Ps.Velocity0 > -1f && _pm.Ps.Velocity0 < 1f) _pm.Ps.Velocity0 = 0f;
            if (_pm.Ps.Velocity1 > -1f && _pm.Ps.Velocity1 < 1f) _pm.Ps.Velocity1 = 0f;

            Vector3 wishdir = wishvel;
            float ws = wishdir.magnitude;
            if (ws > 0f) wishdir /= ws;

            PM_Accelerate(wishdir, ws, PM_AccelerateVal);

            if (wishvel.z == 0f)
            {
                float g = _pm.Ps.Gravity * _pml.FrameTime;
                if (_pm.Ps.Velocity2 > 0f)
                { _pm.Ps.Velocity2 -= g; if (_pm.Ps.Velocity2 < 0f) _pm.Ps.Velocity2 = 0f; }
                else
                { _pm.Ps.Velocity2 += g; if (_pm.Ps.Velocity2 > 0f) _pm.Ps.Velocity2 = 0f; }
            }

            PM_StepSlideMove(false);
            _pm.Ps.MovementDir = 0;
        }

        // -----------------------------------------------------------------------
        // PM_Sprint
        // -----------------------------------------------------------------------
        private void PM_Sprint()
        {
            bool sprinting =
                (_pm.Cmd.Buttons & Button.Sprint) != 0 &&
                (_pm.Cmd.ForwardMove != 0 || _pm.Cmd.RightMove != 0) &&
                (_pm.Ps.PmFlags & GameConst.PMF_DUCKED) == 0 &&
                (_pm.Ps.EFlags  & GameConst.EF_PRONE)   == 0;

            if (sprinting)
            {
                if (_pm.Ps.Powerups[PW_ADRENALINE] != 0)
                    _pm.Pmext.SprintTime = SprintTimeMax;
                else if (_pm.Ps.Powerups[PW_NOFATIGUE] != 0)
                {
                    _pm.Ps.Powerups[PW_NOFATIGUE] -= 50;
                    _pm.Pmext.SprintTime += 10;
                    if (_pm.Pmext.SprintTime > SprintTimeMax) _pm.Pmext.SprintTime = SprintTimeMax;
                    if (_pm.Ps.Powerups[PW_NOFATIGUE] < 0)   _pm.Ps.Powerups[PW_NOFATIGUE] = 0;
                }
                else
                {
                    _pm.Pmext.SprintTime -= (int)(5000f * _pml.FrameTime);
                }

                if (_pm.Pmext.SprintTime < 0) _pm.Pmext.SprintTime = 0;
                if (_pm.Ps.SprintExertTime == 0) _pm.Ps.SprintExertTime = 1;
            }
            else
            {
                if (_pm.Ps.Powerups[PW_ADRENALINE] != 0)
                    _pm.Pmext.SprintTime = SprintTimeMax;
                else if (_pm.Ps.Powerups[PW_NOFATIGUE] != 0)
                    _pm.Pmext.SprintTime += 10;
                else
                {
                    int rb = 500;
                    if (_pm.Skill[SK_BATTLE_SENSE] >= 2) rb = (int)(rb * 1.6f);
                    _pm.Pmext.SprintTime += (int)(rb * _pml.FrameTime);
                    if (_pm.Pmext.SprintTime > 5000)
                        _pm.Pmext.SprintTime += (int)(rb * _pml.FrameTime);
                }

                if (_pm.Pmext.SprintTime > SprintTimeMax) _pm.Pmext.SprintTime = SprintTimeMax;
                _pm.Ps.SprintExertTime = 0;
            }
        }

        // -----------------------------------------------------------------------
        // Angle helpers
        // -----------------------------------------------------------------------
        private static float Short2Angle(int s)    => s * (360f / 65536f);
        private static int   AngleToShort(float a) => (int)(a * (65536f / 360f)) & 0xFFFF;

        // -----------------------------------------------------------------------
        // UpdateViewAngles  (static public entry point)
        // -----------------------------------------------------------------------
        public static void UpdateViewAngles(
            PlayerState ps, PmoveExt pmext, UserCmd cmd,
            TraceFunc trace,
            int traceMask)
        {
            const int PITCH = 0, YAW = 1;

            if (ps.PmType == GameConst.PM_INTERMISSION ||
                (ps.PmFlags & GameConst.PMF_TIME_LOCKPLAYER) != 0)
                return;

            if (ps.PmType != GameConst.PM_SPECTATOR && ps.Stats[GameConst.STAT_HEALTH] <= 0)
            {
                // Allow dead players to look around
                ps.Stats[2] = (short)(cmd.Angles[YAW] + ps.DeltaAngles[YAW]); // STAT_DEAD_YAW=2
                return;
            }

            Vector3 oldViewAngles = new Vector3(ps.ViewAngles0, ps.ViewAngles1, ps.ViewAngles2);

            // Circularly clamp angles with deltas
            for (int i = 0; i < 3; i++)
            {
                int temp = cmd.Angles[i] + ps.DeltaAngles[i];
                if (i == PITCH)
                {
                    if (temp >  16000) { ps.DeltaAngles[i] =  16000 - cmd.Angles[i]; temp =  16000; }
                    if (temp < -16000) { ps.DeltaAngles[i] = -16000 - cmd.Angles[i]; temp = -16000; }
                }
                float ang = Short2Angle((short)temp);
                if      (i == 0) ps.ViewAngles0 = ang;
                else if (i == 1) ps.ViewAngles1 = ang;
                else              ps.ViewAngles2 = ang;
            }

            // frame time approximation for angle speed limits
            float ft = Mathf.Max((cmd.ServerTime - ps.CommandTime) * 0.001f, 0.001f);

            if (BG_PlayerMounted(ps.EFlags))
            {
                // Yaw rate limit
                float yaw = ps.ViewAngles1, oldYaw = oldViewAngles.y;
                if (yaw - oldYaw >  180f) yaw -= 360f;
                if (yaw - oldYaw < -180f) yaw += 360f;
                float maxDeg = MG42_YAWSPEED * ft;
                if (yaw > oldYaw && yaw - oldYaw > maxDeg)
                { ps.ViewAngles1 = oldYaw + maxDeg; ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }
                else if (oldYaw > yaw && oldYaw - yaw > maxDeg)
                { ps.ViewAngles1 = oldYaw - maxDeg; ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }

                // Pitch arc limits
                float arcMax = pmext.VArc, arcMin;
                if ((ps.EFlags & EF_AAGUN_ACTIVE) != 0)
                {
                    arcMin = 0f;
                }
                else if ((ps.EFlags & EF_MOUNTEDTANK) != 0)
                {
                    arcMin = 14f; arcMax = 50f;
                    float cosA = Mathf.Cos(ETMath.AngleNormalize180(pmext.CenterAngles.y - ps.ViewAngles1) * Mathf.Deg2Rad);
                    float tankA = -ETMath.AngleNormalize360(cosA * ETMath.AngleNormalize180(-pmext.CenterAngles.x));
                    pmext.CenterAngles = new Vector3(tankA, pmext.CenterAngles.y, pmext.CenterAngles.z);
                }
                else { arcMin = pmext.VArc * 0.5f; }

                float arcDiff = ETMath.AngleNormalize180(ps.ViewAngles0 - pmext.CenterAngles.x);
                if (arcDiff > arcMin)
                { ps.ViewAngles0 = ETMath.AngleNormalize180(pmext.CenterAngles.x + arcMin); ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }
                else if (arcDiff < -arcMax)
                { ps.ViewAngles0 = ETMath.AngleNormalize180(pmext.CenterAngles.x - arcMax); ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }

                if ((ps.EFlags & EF_MOUNTEDTANK) == 0)
                {
                    arcMin = arcMax = pmext.HArc;
                    arcDiff = ETMath.AngleNormalize180(ps.ViewAngles1 - pmext.CenterAngles.y);
                    if (arcDiff >  arcMin) { ps.ViewAngles1 = ETMath.AngleNormalize180(pmext.CenterAngles.y + arcMin); ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }
                    if (arcDiff < -arcMax) { ps.ViewAngles1 = ETMath.AngleNormalize180(pmext.CenterAngles.y - arcMax); ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }
                }
            }
            else if (ps.Weapon == GameConst.WP_MORTAR_SET)
            {
                const float degsSec  = 60f;
                const float pitchMax = 30f;

                // Yaw rate limit
                float yaw = ps.ViewAngles1, oldYaw = oldViewAngles.y;
                if (yaw - oldYaw >  180f) yaw -= 360f;
                if (yaw - oldYaw < -180f) yaw += 360f;
                float mx = degsSec * ft;
                if (yaw > oldYaw && yaw - oldYaw > mx)
                { ps.ViewAngles1 = oldYaw + mx; ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }
                else if (oldYaw > yaw && oldYaw - yaw > mx)
                { ps.ViewAngles1 = oldYaw - mx; ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }

                // Pitch rate limit
                float pitch = ps.ViewAngles0, oldPitch = oldViewAngles.x;
                if (pitch - oldPitch >  180f) pitch -= 360f;
                if (pitch - oldPitch < -180f) pitch += 360f;
                if (pitch > oldPitch && pitch - oldPitch > mx)
                { ps.ViewAngles0 = oldPitch + mx; ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }
                else if (oldPitch > pitch && oldPitch - pitch > mx)
                { ps.ViewAngles0 = oldPitch - mx; ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }

                // Yaw arc ±30
                float yd = ps.ViewAngles1 - pmext.MountedWeaponAngles.y;
                if (yd >  180f) yd -= 360f; if (yd < -180f) yd += 360f;
                if (yd >  30f) { ps.ViewAngles1 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.y + 30f); ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }
                if (yd < -30f) { ps.ViewAngles1 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.y - 30f); ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }

                // Pitch arc ±pitchMax / ±(pitchMax-10)
                float pd = ps.ViewAngles0 - pmext.MountedWeaponAngles.x;
                if (pd >  180f) pd -= 360f; if (pd < -180f) pd += 360f;
                if (pd > pitchMax - 10f) { ps.ViewAngles0 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.x + pitchMax - 10f); ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }
                if (pd < -pitchMax)      { ps.ViewAngles0 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.x - pitchMax);       ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }
            }
            else if ((ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                float pitchMax    = (ps.Weapon == GameConst.WP_MOBILE_MG42_SET) ? 20f : 40f;
                int   newDeltaYaw = ps.DeltaAngles[YAW];
                float oldYaw      = oldViewAngles.y;

                if (ps.Weapon == GameConst.WP_MOBILE_MG42_SET)
                {
                    float yd = ps.ViewAngles1 - pmext.MountedWeaponAngles.y;
                    if (yd >  180f) yd -= 360f; if (yd < -180f) yd += 360f;
                    if (yd >  20f) { ps.ViewAngles1 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.y + 20f); ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }
                    if (yd < -20f) { ps.ViewAngles1 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.y - 20f); ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW]; }
                }

                float pd = ps.ViewAngles0 - pmext.MountedWeaponAngles.x;
                if (pd >  180f) pd -= 360f; if (pd < -180f) pd += 360f;
                if (pd >  pitchMax) { ps.ViewAngles0 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.x + pitchMax); ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }
                if (pd < -pitchMax) { ps.ViewAngles0 = ETMath.AngleNormalize180(pmext.MountedWeaponAngles.x - pitchMax); ps.DeltaAngles[PITCH] = AngleToShort(ps.ViewAngles0) - cmd.Angles[PITCH]; }

                // Leg collision check on yaw change
                if (ps.ViewAngles1 != oldYaw)
                {
                    Vector3 origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
                    int mask = traceMask & ~(Contents.Body | Contents.Corpse);
                    float ang = ps.ViewAngles1 * Mathf.Deg2Rad;
                    Vector3 ff  = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                    Vector3 org = origin + ff * -32f;
                    TraceResult legTr = trace(org, PlayerLegsProneMins, PlayerLegsProneMaxs, org, ps.ClientNum, mask);
                    if (legTr.AllSolid)
                    {
                        ps.ViewAngles1      = oldYaw;
                        ps.DeltaAngles[YAW] = AngleToShort(ps.ViewAngles1) - cmd.Angles[YAW];
                    }
                    else
                    {
                        ps.DeltaAngles[YAW] = newDeltaYaw;
                    }
                }
            }

            // Lean — create a temporary instance to get frame time into PM_UpdateLean
            var tmp = new PlayerMovement();
            tmp._pml.Msec      = Mathf.Max(cmd.ServerTime - ps.CommandTime, 1);
            tmp._pml.FrameTime = tmp._pml.Msec * 0.001f;
            tmp.PM_UpdateLean(ps, cmd, trace);
        }

        // -----------------------------------------------------------------------
        // PmoveSingle
        // -----------------------------------------------------------------------
        public void PmoveSingle(PmoveInput pmove)
        {
            // [ANIM] BG_AnimUpdatePlayerStateConditions stub
            _pm = pmove;

            _pm.NumTouch  = 0;
            _pm.WaterType = 0;
            _pm.WaterLevel= 0;

            if (_pm.Ps.Stats[GameConst.STAT_HEALTH] <= 0)
            {
                _pm.TraceMask &= ~Contents.Body;
                _pm.Ps.EFlags &= ~EF_ZOOMING_INT;
            }

            if (Math.Abs(_pm.Cmd.ForwardMove) > 64 || Math.Abs(_pm.Cmd.RightMove) > 64)
                _pm.Cmd.Buttons &= ~Button.Walking;

            if ((_pm.Cmd.Buttons & Button.Talk) != 0) _pm.Ps.EFlags |= GameConst.EF_TALK;
            else _pm.Ps.EFlags &= ~GameConst.EF_TALK;

            _pm.Ps.EFlags &= ~(GameConst.EF_FIRING | EF_ZOOMING_INT);

            if ((_pm.Cmd.WButtons & WButton.Zoom) != 0 &&
                _pm.Ps.Stats[GameConst.STAT_HEALTH] >= 0 &&
                _pm.Ps.WeaponDelay == 0)
            {
                if ((_pm.Ps.Stats[STAT_KEYS] & (1 << INV_BINOCS)) != 0 &&
                    !BG_IsScopedWeapon(_pm.Ps.Weapon) &&
                    !BG_PlayerMounted(_pm.Ps.EFlags) &&
                    _pm.Ps.Weapon != GameConst.WP_MOBILE_MG42_SET &&
                    _pm.Ps.Weapon != GameConst.WP_MORTAR_SET)
                {
                    _pm.Ps.EFlags |= EF_ZOOMING_INT;
                }

                if ((_pm.Ps.Weapon == WP_DYNAMITE ||
                     _pm.Ps.Weapon == WP_GRENADE_LAUNCHER ||
                     _pm.Ps.Weapon == WP_GRENADE_PINEAPPLE) &&
                    _pm.Ps.GrenadeTimeLeft > 0)
                {
                    _pm.Ps.EFlags &= ~EF_ZOOMING_INT;
                }
            }

            if ((_pm.Ps.PmFlags & GameConst.PMF_RESPAWNED) == 0 &&
                _pm.Ps.PmType != GameConst.PM_INTERMISSION)
            {
                if (PM_WeaponAmmoAvailable(_pm.Ps.Weapon) > 0 &&
                    (_pm.Ps.EFlags & EF_ZOOMING_INT) == 0 &&
                    _pm.Ps.Leanf == 0f)
                {
                    if (_pm.Ps.Weaponstate == GameConst.WEAPON_READY ||
                        _pm.Ps.Weaponstate == WEAPON_FIRING)
                    {
                        if ((_pm.Cmd.Buttons & Button.Attack) != 0 &&
                            (_pm.Cmd.Buttons & Button.Talk)   == 0)
                            _pm.Ps.EFlags |= GameConst.EF_FIRING;
                    }
                }
            }

            if ((_pm.Ps.PmFlags & GameConst.PMF_RESPAWNED) != 0 &&
                _pm.Ps.Stats[GameConst.STAT_PLAYER_CLASS] == GameConst.PC_COVERTOPS)
                _pm.Pmext.SilencedSideArm |= 1;

            if (_pm.Ps.Stats[GameConst.STAT_HEALTH] > 0 &&
                (_pm.Cmd.Buttons & Button.Attack) == 0)
                _pm.Ps.PmFlags &= ~GameConst.PMF_RESPAWNED;

            if ((_pm.Cmd.Buttons & Button.Talk) != 0)
            {
                _pm.Cmd.Buttons     = Button.Talk;
                _pm.Cmd.WButtons    = 0;
                _pm.Cmd.ForwardMove = 0;
                _pm.Cmd.RightMove   = 0;
                _pm.Cmd.UpMove      = 0;
                _pm.Cmd.DoubleTap   = 0;
            }

            if (_pm.Ps.Persistant[GameConst.PERS_HWEAPON_USE] != 0)
            {
                _pm.Cmd.ForwardMove = 0;
                _pm.Cmd.RightMove   = 0;
                _pm.Cmd.UpMove      = 0;
            }

            _pml = default;

            _pml.Msec = _pm.Cmd.ServerTime - _pm.Ps.CommandTime;
            if (_pml.Msec < 1)   _pml.Msec = 1;
            if (_pml.Msec > 200) _pml.Msec = 200;
            _pm.Ps.CommandTime = _pm.Cmd.ServerTime;

            _pml.PreviousOrigin   = new Vector3(_pm.Ps.Origin0,   _pm.Ps.Origin1,   _pm.Ps.Origin2);
            _pml.PreviousVelocity = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            _pml.FrameTime        = _pml.Msec * 0.001f;

            if (_pm.Ps.PmType != GameConst.PM_FREEZE &&
                (_pm.Ps.PmFlags & GameConst.PMF_LIMBO) == 0)
            {
                UpdateViewAngles(_pm.Ps, _pm.Pmext, _pm.Cmd, _pm.Trace, _pm.TraceMask);
            }

            ETMath.AngleVectors(
                new Vector3(_pm.Ps.ViewAngles0, _pm.Ps.ViewAngles1, _pm.Ps.ViewAngles2),
                out _pml.Forward, out _pml.Right, out _pml.Up);

            if (_pm.Cmd.UpMove < 10) _pm.Ps.PmFlags &= ~GameConst.PMF_JUMP_HELD;

            if      (_pm.Cmd.ForwardMove < 0) _pm.Ps.PmFlags |= GameConst.PMF_BACKWARDS_RUN;
            else if (_pm.Cmd.ForwardMove > 0 ||
                     (_pm.Cmd.ForwardMove == 0 && _pm.Cmd.RightMove != 0))
                _pm.Ps.PmFlags &= ~GameConst.PMF_BACKWARDS_RUN;

            if (_pm.Ps.PmType >= GameConst.PM_DEAD ||
                (_pm.Ps.PmFlags & (GameConst.PMF_LIMBO | GameConst.PMF_TIME_LOCKPLAYER)) != 0)
            {
                _pm.Cmd.ForwardMove = 0;
                _pm.Cmd.RightMove   = 0;
                _pm.Cmd.UpMove      = 0;
            }

            if (_pm.Ps.PmType == GameConst.PM_SPECTATOR)
            { PM_CheckDuck(); PM_FlyMove(); PM_DropTimers(); return; }

            if (_pm.Ps.PmType == GameConst.PM_NOCLIP)
            { PM_NoclipMove(); PM_DropTimers(); return; }

            if (_pm.Ps.PmType == GameConst.PM_FREEZE)     return;
            if (_pm.Ps.PmType == GameConst.PM_INTERMISSION) return;

            // Mortar set zeros input
            if (_pm.Ps.Weapon == GameConst.WP_MORTAR_SET &&
                _pm.Ps.PmType  == GameConst.PM_NORMAL)
            {
                _pm.Cmd.ForwardMove = 0;
                _pm.Cmd.RightMove   = 0;
                _pm.Cmd.UpMove      = 0;
            }

            PM_SetWaterLevel();
            _pml.PreviousWaterLevel = _pm.WaterLevel;

            if (!PM_CheckProne()) PM_CheckDuck();

            PM_GroundTrace();

            if (_pm.Ps.PmType == GameConst.PM_DEAD)
            {
                PM_DeadMove();
                if (_pm.Ps.Weapon == GameConst.WP_MORTAR_SET)
                    _pm.Ps.Weapon = GameConst.WP_MORTAR;
            }
            else
            {
                // Weapon-change stubs — handled by weapon layer
                if (_pm.Ps.Weapon == GameConst.WP_MOBILE_MG42_SET &&
                    (_pm.Ps.EFlags & GameConst.EF_PRONE) == 0)
                {
                    // PM_BeginWeaponChange(WP_MOBILE_MG42_SET, WP_MOBILE_MG42, false) stub
                }
                if (_pm.Ps.Weapon == GameConst.WP_SATCHEL_DET &&
                    _pm.Ps.AmmoClip[GameConst.WP_SATCHEL_DET] == 0)
                {
                    // PM_BeginWeaponChange(WP_SATCHEL_DET, WP_SATCHEL, true) stub
                }
            }

            PM_CheckLadderMove();
            PM_DropTimers();

            if (_pml.Ladder)
                PM_LadderMove();
            else if ((_pm.Ps.PmFlags & GameConst.PMF_TIME_WATERJUMP) != 0)
                PM_WaterJumpMove();
            else if (_pm.WaterLevel > 1)
                PM_WaterMove();
            else if (_pml.Walking && (_pm.Ps.EFlags & EF_MOUNTEDTANK) == 0)
                PM_WalkMove();
            else if ((_pm.Ps.EFlags & EF_MOUNTEDTANK) == 0)
                PM_AirMove();

            if ((_pm.Ps.EFlags & EF_MOUNTEDTANK) != 0)
            {
                WriteVelocity(Vector3.zero);
                _pm.Ps.ViewHeight = GameConst.DEFAULT_VIEWHEIGHT;
                // [ANIM] BG_AnimScriptAnimation ANIM_MT_IDLE stub
            }

            PM_Sprint();

            // Second ground/water pass
            PM_GroundTrace();
            PM_SetWaterLevel();

            PM_Weapon();
            PM_Footsteps();
            PM_WaterEvents();

            // Snap velocity
            Vector3 sv = new Vector3(_pm.Ps.Velocity0, _pm.Ps.Velocity1, _pm.Ps.Velocity2);
            WriteVelocity(new Vector3(Mathf.Round(sv.x), Mathf.Round(sv.y), Mathf.Round(sv.z)));
        }

        // -----------------------------------------------------------------------
        // Pmove  (public entry point, returns surface flags)
        // -----------------------------------------------------------------------
        public int Pmove(PmoveInput pmove)
        {
            int finalTime = pmove.Cmd.ServerTime;

            if (finalTime < pmove.Ps.CommandTime) return 0;
            if (finalTime > pmove.Ps.CommandTime + 1000)
                pmove.Ps.CommandTime = finalTime - 1000;

            pmove.Ps.PmoveFramecount =
                (pmove.Ps.PmoveFramecount + 1) &
                ((1 << GameConst.PS_PMOVEFRAMECOUNTBITS) - 1);

            _pm = pmove;

            while (pmove.Ps.CommandTime != finalTime)
            {
                int msec = finalTime - pmove.Ps.CommandTime;
                if (pmove.PmoveFixed != 0)
                { if (msec > pmove.PmoveMsec) msec = pmove.PmoveMsec; }
                else
                { if (msec > 50) msec = 50; }

                pmove.Cmd.ServerTime = pmove.Ps.CommandTime + msec;
                PmoveSingle(pmove);

                if ((pmove.Ps.PmFlags & GameConst.PMF_JUMP_HELD) != 0)
                    pmove.Cmd.UpMove = 20;
            }

            if (pmove.Ps.CurWeapHeat > 255) pmove.Ps.CurWeapHeat = 255;
            else if (pmove.Ps.CurWeapHeat < 0) pmove.Ps.CurWeapHeat = 0;

            if ((pmove.Ps.Stats[GameConst.STAT_HEALTH] <= 0 ||
                 pmove.Ps.PmType == GameConst.PM_DEAD) &&
                (_pml.GroundTrace.SurfaceFlags & Surf.MonsterSlick) != 0)
            {
                return _pml.GroundTrace.SurfaceFlags;
            }
            return 0;
        }
    }
}
