// Ported from: src/game/bg_pmove.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Network;

namespace ET.Game
{
    public partial class PlayerMovement
    {
        // -----------------------------------------------------------------------
        // Constants (EV_* aliases for GameEvent.* — used by SlideMove.cs too)
        // -----------------------------------------------------------------------
        private const int EV_FOOTSTEP       = GameEvent.FOOTSTEP;
        private const int EV_FOOTSPLASH     = GameEvent.FOOTSTEP;      // no swim events in GameEvent yet
        private const int EV_SWIM           = GameEvent.FOOTSTEP;
        private const int EV_STEP_4         = GameEvent.STEP_4;
        private const int EV_STEP_8         = GameEvent.STEP_8;
        private const int EV_STEP_12        = GameEvent.STEP_12;
        private const int EV_STEP_16        = GameEvent.STEP_16;
        private const int EV_FALL_SHORT     = GameEvent.FALL_SHORT;
        private const int EV_FALL_MEDIUM    = GameEvent.FALL_MEDIUM;
        private const int EV_FALL_FAR       = GameEvent.FALL_FAR;
        private const int EV_FALL_NDIE      = GameEvent.FALL_NDIE;
        private const int EV_FALL_DMG_10    = GameEvent.FALL_DMG_10;
        private const int EV_FALL_DMG_15    = GameEvent.FALL_DMG_15;
        private const int EV_FALL_DMG_25    = GameEvent.FALL_DMG_25;
        private const int EV_FALL_DMG_50    = GameEvent.FALL_DMG_50;
        private const int EV_JUMP           = GameEvent.JUMP;
        private const int EV_WATER_TOUCH    = GameEvent.WATER_TOUCH;
        private const int EV_WATER_LEAVE    = GameEvent.WATER_LEAVE;
        private const int EV_WATER_UNDER    = GameEvent.WATER_UNDER;
        private const int EV_WATER_CLEAR    = GameEvent.WATER_CLEAR;
        private const int EV_FILL_CLIP      = 0;   // stub — not in GameEvent yet
        private const int EV_CHANGE_WEAPON  = GameEvent.CHANGE_WEAPON;
        private const int EV_FIRE_WEAPON    = GameEvent.FIRE_WEAPON;

        // physics tunables
        private const float PM_STOPSPEED          = 100f;
        private const float PM_WATER_SWIM_SCALE   = 0.5f;
        private const float PM_WATER_WADE_SCALE   = 0.70f;
        private const float PM_SLAG_SWIM_SCALE    = 0.30f;
        private const float PM_SLAG_WADE_SCALE    = 0.70f;
        private const float PM_PRONE_SPEED_SCALE  = 0.21f;
        private const float PM_ACCELERATE        = 10f;
        private const float PM_AIRACCELERATE     = 1f;
        private const float PM_WATERACCELERATE   = 4f;
        private const float PM_SLAGACCELERATE    = 2f;
        private const float PM_FLYACCELERATE     = 8f;
        private const float PM_FRICTION          = 6f;
        private const float PM_WATERFRICTION     = 1f;
        private const float PM_SLAGFRICTION      = 1f;
        private const float PM_FLIGHTFRICTION    = 3f;
        private const float PM_LADDERFRICTION    = 14f;
        private const float PM_SPECTATORFRICTION = 5f;
        private const float TRACE_LADDER_DIST    = 48f;
        private const float MG42_YAWSPEED        = 300f;
        private const float SPRINTTIME           = 20000f;

        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------
        private PmoveInput  _pm;
        private PmoveLocal  _pml;

        // ladder state (pml_t extension)
        private bool    _ladderForward;
        private Vector3 _ladderVec;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------
        public PlayerMovement(PmoveInput pm)
        {
            _pm = pm;
        }

        // -----------------------------------------------------------------------
        // PM_AddEvent / PM_AddTouchEnt
        // -----------------------------------------------------------------------
        private void AddEvent(int newEvent)
        {
            var ps = _pm.Ps;
            int seq = ps.EventSequence & 3;
            ps.Events[seq]     = newEvent;
            ps.EventParms[seq] = 0;
            ps.EventSequence++;
        }

        private void AddEventExt(int newEvent, int parm)
        {
            var ps = _pm.Ps;
            int seq = ps.EventSequence & 3;
            ps.Events[seq]     = newEvent;
            ps.EventParms[seq] = parm;
            ps.EventSequence++;
        }

        private void PM_AddTouchEnt(int entityNum)
        {
            if (entityNum == GameConst.ENTITYNUM_NONE) return;
            if (_pm.NumTouch >= GameConst.MAXTOUCH) return;
            for (int i = 0; i < _pm.NumTouch; i++)
                if (_pm.TouchEnts[i] == entityNum) return;
            _pm.TouchEnts[_pm.NumTouch++] = entityNum;
        }

        // -----------------------------------------------------------------------
        // PM_TraceAll / PM_TraceLegs
        // -----------------------------------------------------------------------
        private TraceResult PM_TraceAll(Vector3 start, Vector3 end)
        {
            return _pm.Trace(start, _pm.Mins, _pm.Maxs, end, _pm.Ps.ClientNum, _pm.TraceMask);
        }

        private TraceResult PM_TraceLegs(Vector3 start, Vector3 end,
            Vector3 viewAngles, int clientNum, int traceMask)
        {
            // When prone, trace legs separately with a smaller offset bbox.
            // In full port this uses PM_TraceAllLegs; here we share the main trace.
            return _pm.Trace(start, _pm.Mins, _pm.Maxs, end, clientNum, traceMask);
        }

        // -----------------------------------------------------------------------
        // PM_UpdateViewAngles  (simplified — angle clamping only)
        // -----------------------------------------------------------------------
        public static void PM_UpdateViewAngles(PlayerState ps, PmoveExt pmext,
            UserCmd cmd, TraceFunc trace, int traceMask)
        {
            if (ps.PmType == GameConst.PM_INTERMISSION ||
                (ps.PmFlags & GameConst.PMF_TIME_LOCKPLAYER) != 0)
                return;

            if (ps.PmType != GameConst.PM_SPECTATOR &&
                ps.Stats[GameConst.STAT_HEALTH] <= 0)
                return;

            for (int i = 0; i < 3; i++)
            {
                short temp = (short)(cmd.Angles[i] + ps.DeltaAngles[i]);
                if (i == 0) // PITCH
                {
                    if (temp > 16000)
                    {
                        ps.DeltaAngles[i] = (int)(16000 - cmd.Angles[i]);
                        temp = 16000;
                    }
                    else if (temp < -16000)
                    {
                        ps.DeltaAngles[i] = (int)(-16000 - cmd.Angles[i]);
                        temp = -16000;
                    }
                }
                float angle = temp * (360f / 65536f);
                switch (i)
                {
                    case 0: ps.ViewAngles0 = angle; break;
                    case 1: ps.ViewAngles1 = angle; break;
                    case 2: ps.ViewAngles2 = angle; break;
                }
            }
        }

        // -----------------------------------------------------------------------
        // AngleVectors  (id Software right-hand Z-up convention)
        // -----------------------------------------------------------------------
        private static void AngleVectors(float pitch, float yaw, float roll,
            out Vector3 forward, out Vector3 right, out Vector3 up)
        {
            float angle;
            float sr, sp, sy, cr, cp, cy;

            angle = yaw   * (Mathf.PI * 2f / 360f);
            sy = Mathf.Sin(angle); cy = Mathf.Cos(angle);
            angle = pitch * (Mathf.PI * 2f / 360f);
            sp = Mathf.Sin(angle); cp = Mathf.Cos(angle);
            angle = roll  * (Mathf.PI * 2f / 360f);
            sr = Mathf.Sin(angle); cr = Mathf.Cos(angle);

            forward = new Vector3(cp * cy, cp * sy, -sp);
            right   = new Vector3(-sr*sp*cy + cr*sy, -sr*sp*sy - cr*cy, -sr*cp);
            up      = new Vector3(cr*sp*cy + sr*sy,   cr*sp*sy - sr*cy,  cr*cp);
        }

        // -----------------------------------------------------------------------
        // PM_Friction
        // -----------------------------------------------------------------------
        private void PM_Friction()
        {
            var ps = _pm.Ps;
            var vel = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            var vec = vel;
            if (_pml.Walking) vec.z = 0f;

            float speed = vec.magnitude;
            if (speed < 1f && ps.PmType != GameConst.PM_SPECTATOR &&
                ps.PmType != GameConst.PM_NOCLIP)
            {
                ps.Velocity0 = 0f;
                ps.Velocity1 = 0f;
                return;
            }

            float drop = 0f;

            // dodge friction
            int dodgeAge = _pm.Cmd.ServerTime - _pm.Pmext.DodgeTime;
            if (dodgeAge > 250 && dodgeAge < 350)
                drop += speed * 20f * _pml.FrameTime;

            // ground friction
            if (_pm.WaterLevel <= 1)
            {
                if (_pml.Walking &&
                    (_pml.GroundTrace.SurfaceFlags & (int)Surf.Slick) == 0)
                {
                    if ((ps.PmFlags & GameConst.PMF_TIME_KNOCKBACK) == 0)
                    {
                        float control = speed < PM_STOPSPEED ? PM_STOPSPEED : speed;
                        drop += control * PM_FRICTION * _pml.FrameTime;
                    }
                }
            }

            // water friction
            if (_pm.WaterLevel > 0)
            {
                float wfric = (_pm.WaterType == Contents.Slime)
                    ? PM_SLAGFRICTION : PM_WATERFRICTION;
                drop += speed * wfric * _pm.WaterLevel * _pml.FrameTime;
            }

            if (ps.PmType == GameConst.PM_SPECTATOR)
                drop += speed * PM_SPECTATORFRICTION * _pml.FrameTime;

            if (_pml.Ladder)
                drop += speed * PM_LADDERFRICTION * _pml.FrameTime;

            float newspeed = speed - drop;
            if (newspeed < 0f) newspeed = 0f;
            newspeed /= speed;

            if ((ps.PmType == GameConst.PM_SPECTATOR || ps.PmType == GameConst.PM_NOCLIP)
                && drop < 1f && speed < 3f)
                newspeed = 0f;

            ps.Velocity0 = vel.x * newspeed;
            ps.Velocity1 = vel.y * newspeed;
            ps.Velocity2 = vel.z * newspeed;
        }

        // -----------------------------------------------------------------------
        // PM_Accelerate
        // -----------------------------------------------------------------------
        private void PM_Accelerate(Vector3 wishdir, float wishspeed, float accel)
        {
            var ps = _pm.Ps;
            var vel = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            float currentspeed = Vector3.Dot(vel, wishdir);
            float addspeed     = wishspeed - currentspeed;
            if (addspeed <= 0f) return;

            float accelspeed = accel * _pml.FrameTime * wishspeed;
            if (accelspeed > addspeed) accelspeed = addspeed;

            if (ps.GroundEntityNum != GameConst.ENTITYNUM_NONE)
                accelspeed *= 1f / ps.Friction;
            if (accelspeed > addspeed) accelspeed = addspeed;

            ps.Velocity0 += accelspeed * wishdir.x;
            ps.Velocity1 += accelspeed * wishdir.y;
            ps.Velocity2 += accelspeed * wishdir.z;
        }

        // -----------------------------------------------------------------------
        // PM_CmdScale
        // -----------------------------------------------------------------------
        private float PM_CmdScale()
        {
            var ps  = _pm.Ps;
            var cmd = _pm.Cmd;

            int max = Math.Abs(cmd.ForwardMove);
            if (Math.Abs(cmd.RightMove) > max)  max = Math.Abs(cmd.RightMove);
            if (Math.Abs(cmd.UpMove)    > max)  max = Math.Abs(cmd.UpMove);
            if (max == 0) return 0f;

            float total = Mathf.Sqrt(
                cmd.ForwardMove * cmd.ForwardMove +
                cmd.RightMove   * cmd.RightMove   +
                cmd.UpMove      * cmd.UpMove);

            float scale = (float)ps.Speed * max / (127f * total);

            bool sprinting = (cmd.Buttons & (int)Button.Sprint) != 0 &&
                             _pm.Pmext.SprintTime > 50;
            scale *= sprinting ? ps.SprintSpeedScale : ps.RunSpeedScale;

            if (ps.PmType == GameConst.PM_NOCLIP) scale *= 3f;

            if (ps.Weapon == GameConst.WP_PANZERFAUST ||
                ps.Weapon == GameConst.WP_MOBILE_MG42 ||
                ps.Weapon == GameConst.WP_MOBILE_MG42_SET ||
                ps.Weapon == GameConst.WP_MORTAR)
            {
                scale *= (_pm.Skill[GameConst.SK_HEAVY_WEAPONS] >= 3) ? 0.75f : 0.5f;
            }

            if (ps.Weapon == GameConst.WP_FLAMETHROWER)
            {
                if (_pm.Skill[GameConst.SK_HEAVY_WEAPONS] < 3 ||
                    (cmd.Buttons & (int)Button.Attack) != 0)
                    scale *= 0.7f;
            }

            if (_pm.GameType == GameConst.GT_SINGLE_PLAYER ||
                _pm.GameType == GameConst.GT_COOP)
                scale *= 127f / 127f; // movespeed cvar normalised to 127 default

            return scale;
        }

        // -----------------------------------------------------------------------
        // PM_SetMovementDir
        // -----------------------------------------------------------------------
        private void PM_SetMovementDir()
        {
            var cmd = _pm.Cmd;
            if (cmd.ForwardMove != 0 || cmd.RightMove != 0)
            {
                if (cmd.RightMove == 0 && cmd.ForwardMove > 0)
                    _pm.Ps.MovementDir = 0;
                else if (cmd.RightMove < 0 && cmd.ForwardMove > 0)
                    _pm.Ps.MovementDir = 1;
                else if (cmd.RightMove < 0 && cmd.ForwardMove == 0)
                    _pm.Ps.MovementDir = 2;
                else if (cmd.RightMove < 0 && cmd.ForwardMove < 0)
                    _pm.Ps.MovementDir = 3;
                else if (cmd.RightMove == 0 && cmd.ForwardMove < 0)
                    _pm.Ps.MovementDir = 4;
                else if (cmd.RightMove > 0 && cmd.ForwardMove < 0)
                    _pm.Ps.MovementDir = 5;
                else if (cmd.RightMove > 0 && cmd.ForwardMove == 0)
                    _pm.Ps.MovementDir = 6;
                else if (cmd.RightMove > 0 && cmd.ForwardMove > 0)
                    _pm.Ps.MovementDir = 7;
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
            var ps  = _pm.Ps;
            var cmd = _pm.Cmd;

            if ((ps.EFlags & GameConst.EF_PRONE) != 0) return false;
            if (cmd.ServerTime - _pm.Pmext.JumpTime < 850) return false;
            if ((ps.PmFlags & GameConst.PMF_RESPAWNED) != 0) return false;
            if (cmd.UpMove < 10) return false;

            if ((ps.PmFlags & GameConst.PMF_JUMP_HELD) != 0)
            {
                cmd.UpMove = 0;
                return false;
            }

            _pml.GroundPlane = false;
            _pml.Walking     = false;
            ps.PmFlags |= GameConst.PMF_JUMP_HELD;
            ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
            ps.Velocity2 = GameConst.JUMP_VELOCITY;
            AddEvent(EV_JUMP);

            if (cmd.ForwardMove >= 0)
                ps.PmFlags &= ~GameConst.PMF_BACKWARDS_JUMP;
            else
                ps.PmFlags |= GameConst.PMF_BACKWARDS_JUMP;

            return true;
        }

        // -----------------------------------------------------------------------
        // PM_CheckWaterJump
        // -----------------------------------------------------------------------
        private bool PM_CheckWaterJump()
        {
            var ps = _pm.Ps;
            if (ps.PmTime != 0) return false;
            if (_pm.WaterLevel != 2) return false;

            var flatforward = new Vector3(_pml.Forward.x, _pml.Forward.y, 0f).normalized;
            var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
            var spot   = origin + flatforward * 30f;
            spot.z += 4f;

            int cont = _pm.PointContents(spot, ps.ClientNum);
            if ((cont & Contents.Solid) == 0) return false;

            spot.z += 16f;
            cont = _pm.PointContents(spot, ps.ClientNum);
            if (cont != 0) return false;

            ps.Velocity0 = _pml.Forward.x * 200f;
            ps.Velocity1 = _pml.Forward.y * 200f;
            ps.Velocity2 = 350f;
            ps.PmFlags  |= GameConst.PMF_TIME_WATERJUMP;
            ps.PmTime    = 2000;
            return true;
        }

        // -----------------------------------------------------------------------
        // PM_CheckDodge  (stub — multiplayer dodge logic)
        // -----------------------------------------------------------------------
        private bool PM_CheckDodge()
        {
            return false;
        }

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
            if (PM_CheckWaterJump())
            {
                PM_WaterJumpMove();
                return;
            }

            PM_Friction();

            float scale = PM_CmdScale();
            Vector3 wishvel;
            if (scale == 0f)
            {
                wishvel = new Vector3(0f, 0f, -60f);
            }
            else
            {
                var cmd = _pm.Cmd;
                wishvel = _pml.Forward * (scale * cmd.ForwardMove) +
                          _pml.Right   * (scale * cmd.RightMove);
                wishvel.z += scale * cmd.UpMove;
            }

            var wishdir   = wishvel.normalized;
            float wishspeed = wishvel.magnitude;

            if (_pm.WaterType == Contents.Slime)
            {
                float cap = _pm.Ps.Speed * PM_SLAG_SWIM_SCALE;
                if (wishspeed > cap) wishspeed = cap;
                PM_Accelerate(wishdir, wishspeed, PM_SLAGACCELERATE);
            }
            else
            {
                float cap = _pm.Ps.Speed * PM_WATER_SWIM_SCALE;
                if (wishspeed > cap) wishspeed = cap;
                PM_Accelerate(wishdir, wishspeed, PM_WATERACCELERATE);
            }

            var ps  = _pm.Ps;
            var vel = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            if (_pml.GroundPlane && Vector3.Dot(vel, _pml.GroundTrace.PlaneNormal) < 0f)
            {
                float len = vel.magnitude;
                vel = PM_ClipVelocity(vel, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP).normalized * len;
                ps.Velocity0 = vel.x;
                ps.Velocity1 = vel.y;
                ps.Velocity2 = vel.z;
            }

            PM_SlideMove(false);
        }

        // -----------------------------------------------------------------------
        // PM_FlyMove
        // -----------------------------------------------------------------------
        private void PM_FlyMove()
        {
            PM_Friction();

            float scale = PM_CmdScale();
            Vector3 wishvel;
            if (scale == 0f)
            {
                wishvel = Vector3.zero;
            }
            else
            {
                var cmd = _pm.Cmd;
                wishvel = _pml.Forward * (scale * cmd.ForwardMove) +
                          _pml.Right   * (scale * cmd.RightMove);
                wishvel.z += scale * cmd.UpMove;
            }

            var wishdir   = wishvel.normalized;
            float wishspeed = wishvel.magnitude;
            PM_Accelerate(wishdir, wishspeed, PM_FLYACCELERATE);
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

            Vector3 fwd = _pml.Forward;
            Vector3 rgt = _pml.Right;

            int dodgeAge = _pm.Cmd.ServerTime - _pm.Pmext.DodgeTime;
            if (dodgeAge < 350)
            {
                fwd.z = fmove = 0f;
                smove = _pm.Pmext.DtMove == (int)DoubleTapType.MoveLeft ? -2070f : 2070f;
                scale = 1f;
            }
            else
            {
                scale = PM_CmdScale();
                fwd.z = 0f;
                rgt.z = 0f;
            }

            fwd = fwd.normalized;
            rgt = rgt.normalized;

            var wishvel = fwd * fmove + rgt * smove;
            wishvel.z   = 0f;

            var wishdir   = wishvel.normalized;
            float wishspeed = wishvel.magnitude * scale;

            PM_Accelerate(wishdir, wishspeed, PM_AIRACCELERATE);

            if (_pml.GroundPlane)
            {
                var ps  = _pm.Ps;
                var vel = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
                vel = PM_ClipVelocity(vel, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP);
                ps.Velocity0 = vel.x;
                ps.Velocity1 = vel.y;
                ps.Velocity2 = vel.z;
            }

            PM_StepSlideMove(true);
            PM_SetMovementDir();
        }

        // -----------------------------------------------------------------------
        // PM_WalkMove
        // -----------------------------------------------------------------------
        private void PM_WalkMove()
        {
            var ps  = _pm.Ps;

            if (_pm.WaterLevel > 2 &&
                Vector3.Dot(_pml.Forward, _pml.GroundTrace.PlaneNormal) > 0f)
            {
                PM_WaterMove();
                return;
            }

            if (PM_CheckJump())
            {
                if (_pm.WaterLevel > 1)
                    PM_WaterMove();
                else
                    PM_AirMove();

                if ((_pm.Cmd.ServerTime - _pm.Pmext.JumpTime) >= 850)
                {
                    _pm.Pmext.SprintTime -= 2500;
                    if (_pm.Pmext.SprintTime < 0) _pm.Pmext.SprintTime = 0;
                    _pm.Pmext.JumpTime = _pm.Cmd.ServerTime;
                }
                ps.JumpTime = _pm.Cmd.ServerTime;
                return;
            }
            else if (_pm.WaterLevel <= 1 && PM_CheckDodge())
            {
                PM_AirMove();
                return;
            }

            PM_Friction();

            float fmove = _pm.Cmd.ForwardMove;
            float smove = _pm.Cmd.RightMove;
            float scale = PM_CmdScale();

            var fwd = new Vector3(_pml.Forward.x, _pml.Forward.y, 0f);
            var rgt = new Vector3(_pml.Right.x,   _pml.Right.y,   0f);

            // project forward/right onto the ground plane
            fwd = PM_ClipVelocity(fwd, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP).normalized;
            rgt = PM_ClipVelocity(rgt, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP).normalized;

            var wishvel = fwd * fmove + rgt * smove;
            var wishdir   = wishvel.normalized;
            float wishspeed = wishvel.magnitude * scale;

            // clamp speed for prone / duck / water
            if ((ps.EFlags & GameConst.EF_PRONE) != 0)
            {
                float cap = ps.Speed * PM_PRONE_SPEED_SCALE;
                if (wishspeed > cap) wishspeed = cap;
            }
            else if ((ps.PmFlags & GameConst.PMF_DUCKED) != 0)
            {
                float cap = ps.Speed * ps.CrouchSpeedScale;
                if (wishspeed > cap) wishspeed = cap;
            }

            if (_pm.WaterLevel > 0)
            {
                float waterScale = _pm.WaterLevel / 3f;
                float swimScale  = (_pm.WaterType == Contents.Slime)
                    ? PM_SLAG_SWIM_SCALE : PM_WATER_SWIM_SCALE;
                waterScale = 1f - (1f - swimScale) * waterScale;
                float cap = ps.Speed * waterScale;
                if (wishspeed > cap) wishspeed = cap;
            }

            float accelerate = ((_pml.GroundTrace.SurfaceFlags & (int)Surf.Slick) != 0 ||
                                 (ps.PmFlags & GameConst.PMF_TIME_KNOCKBACK) != 0)
                ? PM_AIRACCELERATE : PM_ACCELERATE;

            PM_Accelerate(wishdir, wishspeed, accelerate);

            if ((_pml.GroundTrace.SurfaceFlags & (int)Surf.Slick) != 0 ||
                (ps.PmFlags & GameConst.PMF_TIME_KNOCKBACK) != 0)
            {
                ps.Velocity2 -= ps.Gravity * _pml.FrameTime;
            }

            // snow breath effect
            if ((_pml.GroundTrace.SurfaceFlags & (int)Surf.Snow) != 0)
                ps.EFlags |= GameConst.EF_BREATH;
            else
                ps.EFlags &= ~GameConst.EF_BREATH;

            var vel2 = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            float speed = vel2.magnitude;

            vel2 = PM_ClipVelocity(vel2, _pml.GroundTrace.PlaneNormal, GameConst.OVERCLIP);

            if (vel2.x == 0f && vel2.y == 0f)
            {
                if ((ps.EFlags & GameConst.EF_PRONE) != 0)
                    _pm.Pmext.ProneGroundTime = _pm.Cmd.ServerTime;
                return;
            }

            vel2 = vel2.normalized * speed;
            ps.Velocity0 = vel2.x;
            ps.Velocity1 = vel2.y;
            ps.Velocity2 = vel2.z;

            PM_StepSlideMove(false);
            PM_SetMovementDir();
        }

        // -----------------------------------------------------------------------
        // PM_DeadMove
        // -----------------------------------------------------------------------
        private void PM_DeadMove()
        {
            if (!_pml.Walking) return;

            var ps  = _pm.Ps;
            var vel = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            float forward = vel.magnitude;
            forward -= 20f * _pml.FrameTime;
            if (forward <= 0f)
            {
                ps.Velocity0 = 0f;
                ps.Velocity1 = 0f;
                ps.Velocity2 = 0f;
            }
            else
            {
                vel = vel.normalized * forward;
                ps.Velocity0 = vel.x;
                ps.Velocity1 = vel.y;
                ps.Velocity2 = vel.z;
            }
        }

        // -----------------------------------------------------------------------
        // PM_NoclipMove
        // -----------------------------------------------------------------------
        private void PM_NoclipMove()
        {
            var ps = _pm.Ps;
            ps.ViewHeight = GameConst.DEFAULT_VIEWHEIGHT;

            var vel = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            float speed = vel.magnitude;
            if (speed < 1f)
            {
                ps.Velocity0 = ps.Velocity1 = ps.Velocity2 = 0f;
            }
            else
            {
                float friction = PM_FRICTION * 1.5f;
                float control  = speed < PM_STOPSPEED ? PM_STOPSPEED : speed;
                float drop     = control * friction * _pml.FrameTime;
                float newspeed = (speed - drop) / speed;
                if (newspeed < 0f) newspeed = 0f;
                ps.Velocity0 = vel.x * newspeed;
                ps.Velocity1 = vel.y * newspeed;
                ps.Velocity2 = vel.z * newspeed;
            }

            float scale = PM_CmdScale();
            var cmd = _pm.Cmd;
            var wishvel = _pml.Forward * (scale * cmd.ForwardMove) +
                          _pml.Right   * (scale * cmd.RightMove);
            wishvel.z += scale * cmd.UpMove;

            var wishdir   = wishvel.normalized;
            float wishspeed = wishvel.magnitude;
            PM_Accelerate(wishdir, wishspeed, PM_ACCELERATE);

            var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
            var v2     = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            origin += v2 * _pml.FrameTime;
            WriteOrigin(origin);
        }

        // -----------------------------------------------------------------------
        // PM_FootstepForSurface
        // -----------------------------------------------------------------------
        private int PM_FootstepForSurface()
        {
            int sf = _pml.GroundTrace.SurfaceFlags;
            if ((sf & (int)Surf.Metal)   != 0) return GameEvent.FOOTSTEP_METAL;
            if ((sf & (int)Surf.Wood)    != 0) return GameEvent.FOOTSTEP_WOOD;
            if ((sf & (int)Surf.Grass)   != 0) return GameEvent.FOOTSTEP_GRASS;
            if ((sf & (int)Surf.Gravel)  != 0) return GameEvent.FOOTSTEP_GRAVEL;
            if ((sf & (int)Surf.Roof)    != 0) return GameEvent.FOOTSTEP_ROOF;
            if ((sf & (int)Surf.Snow)    != 0) return GameEvent.FOOTSTEP_SNOW;
            if ((sf & (int)Surf.Carpet)  != 0) return GameEvent.FOOTSTEP_CARPET;
            return GameEvent.FOOTSTEP;
        }

        // -----------------------------------------------------------------------
        // PM_CrashLand
        // -----------------------------------------------------------------------
        private void PM_CrashLand()
        {
            var ps = _pm.Ps;

            float dist = ps.Origin2 - _pml.PreviousOrigin.z;
            float vel  = _pml.PreviousVelocity.z;
            float acc  = -ps.Gravity;

            float a = acc * 0.5f;
            float b = vel;
            float c = -dist;
            float den = b * b - 4f * a * c;
            if (den < 0f) return;

            float t     = (-b - Mathf.Sqrt(den)) / (2f * a);
            float delta = vel + t * acc;
            delta = delta * delta * 0.0001f;

            if (_pm.WaterLevel == 3) return;
            if (_pm.WaterLevel == 2) delta *= 0.25f;
            if (_pm.WaterLevel == 1) delta *= 0.5f;
            if (delta < 1f) return;

            if ((_pml.GroundTrace.SurfaceFlags & (int)Surf.NoDamage) == 0)
            {
                if      (delta > 77f)    AddEventExt(EV_FALL_NDIE,   PM_FootstepForSurface());
                else if (delta > 67f)    AddEventExt(EV_FALL_DMG_50, PM_FootstepForSurface());
                else if (delta > 58f && ps.Stats[GameConst.STAT_HEALTH] > 0)
                    AddEventExt(EV_FALL_DMG_25, PM_FootstepForSurface());
                else if (delta > 48f && ps.Stats[GameConst.STAT_HEALTH] > 0)
                    AddEventExt(EV_FALL_DMG_15, PM_FootstepForSurface());
                else if (delta > 38.75f && ps.Stats[GameConst.STAT_HEALTH] > 0)
                    AddEventExt(EV_FALL_DMG_10, PM_FootstepForSurface());
                else if (delta > 7f)
                    AddEventExt(EV_FALL_SHORT,  PM_FootstepForSurface());
                else
                    AddEventExt(EV_FOOTSTEP,    PM_FootstepForSurface());
            }

            if (delta > 38.75f)
            {
                ps.Velocity0 = ps.Velocity1 = ps.Velocity2 = 0f;
            }

            ps.BobCycle = 0;
        }

        // -----------------------------------------------------------------------
        // PM_CorrectAllSolid
        // -----------------------------------------------------------------------
        private bool PM_CorrectAllSolid(out TraceResult trace)
        {
            var ps = _pm.Ps;
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        var point = new Vector3(ps.Origin0 + i, ps.Origin1 + j, ps.Origin2 + k);
                        trace = PM_TraceAll(point, point);
                        if (!trace.AllSolid)
                        {
                            var down = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2 - 0.25f);
                            var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
                            trace = PM_TraceAll(origin, down);
                            _pml.GroundTrace = trace;
                            return true;
                        }
                    }
                }
            }

            ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
            _pml.GroundPlane   = false;
            _pml.Walking       = false;
            trace = default;
            return false;
        }

        // -----------------------------------------------------------------------
        // PM_GroundTraceMissed
        // -----------------------------------------------------------------------
        private void PM_GroundTraceMissed()
        {
            var ps = _pm.Ps;
            if (ps.GroundEntityNum != GameConst.ENTITYNUM_NONE)
            {
                var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
                var point  = origin;
                point.z -= 64f;
                var tr = PM_TraceAll(origin, point);
                if (tr.Fraction == 1f)
                {
                    if (_pm.Cmd.ForwardMove >= 0)
                        ps.PmFlags &= ~GameConst.PMF_BACKWARDS_JUMP;
                    else
                        ps.PmFlags |= GameConst.PMF_BACKWARDS_JUMP;
                }
            }

            if (ps.GroundEntityNum != -1)
                ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;

            _pml.GroundPlane = false;
            _pml.Walking     = false;
        }

        // -----------------------------------------------------------------------
        // PM_GroundTrace
        // -----------------------------------------------------------------------
        private void PM_GroundTrace()
        {
            var ps = _pm.Ps;
            var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
            var point  = origin;

            if ((ps.EFlags & GameConst.EF_MG42_ACTIVE) != 0 ||
                (ps.EFlags & GameConst.EF_AAGUN_ACTIVE) != 0)
                point.z -= 1f;
            else
                point.z -= 0.25f;

            var trace = PM_TraceAll(origin, point);
            _pml.GroundTrace = trace;

            if (trace.AllSolid &&
                (ps.EFlags & GameConst.EF_MOUNTEDTANK) == 0)
            {
                if (!PM_CorrectAllSolid(out trace))
                    return;
            }

            if (trace.Fraction == 1f)
            {
                PM_GroundTraceMissed();
                _pml.GroundPlane = false;
                _pml.Walking     = false;
                return;
            }

            var vel = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            if (vel.z > 0f &&
                Vector3.Dot(vel, trace.PlaneNormal) > 10f &&
                (ps.EFlags & GameConst.EF_PRONE) == 0)
            {
                ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
                _pml.GroundPlane   = false;
                _pml.Walking       = false;
                return;
            }

            if (trace.PlaneNormal.z < GameConst.MIN_WALK_NORMAL)
            {
                ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
                _pml.GroundPlane   = true;
                _pml.Walking       = false;
                return;
            }

            _pml.GroundPlane = true;
            _pml.Walking     = true;

            if ((ps.PmFlags & GameConst.PMF_TIME_WATERJUMP) != 0)
            {
                ps.PmFlags &= ~(GameConst.PMF_TIME_WATERJUMP | GameConst.PMF_TIME_LAND);
                ps.PmTime   = 0;
            }

            if (ps.GroundEntityNum == GameConst.ENTITYNUM_NONE)
            {
                PM_CrashLand();
                if (_pml.PreviousVelocity.z < -200f)
                {
                    ps.PmFlags |= GameConst.PMF_TIME_LAND;
                    ps.PmTime   = 250;
                }
            }

            ps.GroundEntityNum = trace.EntityNum;
            PM_AddTouchEnt(trace.EntityNum);
        }

        // -----------------------------------------------------------------------
        // PM_SetWaterLevel
        // -----------------------------------------------------------------------
        private void PM_SetWaterLevel()
        {
            var ps = _pm.Ps;
            _pm.WaterLevel = 0;
            _pm.WaterType  = 0;

            var point = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2 + ps.Mins2 + 1f);
            int cont  = _pm.PointContents(point, ps.ClientNum);

            if ((cont & Contents.MaskWater) != 0)
            {
                int sample2 = (int)(ps.ViewHeight - ps.Mins2);
                int sample1 = sample2 / 2;

                _pm.WaterType  = cont;
                _pm.WaterLevel = 1;

                point.z = ps.Origin2 + ps.Mins2 + sample1;
                cont = _pm.PointContents(point, ps.ClientNum);
                if ((cont & Contents.MaskWater) != 0)
                {
                    _pm.WaterLevel = 2;
                    point.z = ps.Origin2 + ps.Mins2 + sample2;
                    cont = _pm.PointContents(point, ps.ClientNum);
                    if ((cont & Contents.MaskWater) != 0)
                        _pm.WaterLevel = 3;
                }
            }
        }

        // -----------------------------------------------------------------------
        // PM_CheckDuck
        // -----------------------------------------------------------------------
        private void PM_CheckDuck()
        {
            var ps = _pm.Ps;

            _pm.Mins = new Vector3(ps.Mins0, ps.Mins1, ps.Mins2);
            _pm.Maxs = new Vector3(ps.Maxs0, ps.Maxs1, ps.Maxs2);

            if (ps.PmType == GameConst.PM_DEAD)
            {
                _pm.Maxs = new Vector3(_pm.Maxs.x, _pm.Maxs.y, ps.Maxs2);
                ps.ViewHeight = (int)ps.DeadViewHeight;
                return;
            }

            bool wantDuck = (_pm.Cmd.UpMove < 0 &&
                             (ps.EFlags & GameConst.EF_MOUNTEDTANK) == 0 &&
                             (ps.PmFlags & GameConst.PMF_LADDER) == 0) ||
                            ps.Weapon == GameConst.WP_MORTAR_SET;

            if (wantDuck)
            {
                ps.PmFlags |= GameConst.PMF_DUCKED;
            }
            else
            {
                if ((ps.PmFlags & GameConst.PMF_DUCKED) != 0)
                {
                    var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
                    _pm.Maxs = new Vector3(_pm.Maxs.x, _pm.Maxs.y, ps.Maxs2);
                    var trace = PM_TraceAll(origin, origin);
                    if (!trace.AllSolid)
                        ps.PmFlags &= ~GameConst.PMF_DUCKED;
                }
            }

            if ((ps.PmFlags & GameConst.PMF_DUCKED) != 0)
            {
                _pm.Maxs = new Vector3(_pm.Maxs.x, _pm.Maxs.y, ps.CrouchMaxZ);
                ps.ViewHeight = (int)ps.CrouchViewHeight;
            }
            else
            {
                _pm.Maxs = new Vector3(_pm.Maxs.x, _pm.Maxs.y, ps.Maxs2);
                ps.ViewHeight = (int)ps.StandViewHeight;
            }
        }

        // -----------------------------------------------------------------------
        // PM_CheckProne  (simplified — just returns false, full logic in bg_pmove.c:868)
        // -----------------------------------------------------------------------
        private bool PM_CheckProne()
        {
            return false;
        }

        // -----------------------------------------------------------------------
        // PM_Footsteps  (simplified — bob cycle and footstep events)
        // -----------------------------------------------------------------------
        private void PM_Footsteps()
        {
            var ps  = _pm.Ps;

            if ((ps.EFlags & GameConst.EF_DEAD) != 0) return;

            var vel2D = new Vector2(ps.Velocity0, ps.Velocity1);
            _pm.XYSpeed = vel2D.magnitude;

            if (ps.GroundEntityNum == GameConst.ENTITYNUM_NONE) return;
            if (_pm.Cmd.ForwardMove == 0 && _pm.Cmd.RightMove == 0) return;

            bool prone  = (ps.EFlags & GameConst.EF_PRONE) != 0;
            bool ducked = (ps.PmFlags & GameConst.PMF_DUCKED) != 0;
            bool walking = (_pm.Cmd.Buttons & (int)Button.Walking) != 0;

            float bobmove = prone ? 0.2f : ducked ? 0.5f : walking ? 0.3f : 0.4f;
            bool footstep = !prone && !ducked;

            int old = ps.BobCycle;
            ps.BobCycle = (int)(old + bobmove * _pml.Msec) & 255;

            if (((old + 64) ^ (ps.BobCycle + 64)) >= 128)
            {
                if (_pm.WaterLevel == 0)
                {
                    if (footstep && !_pm.NoFootsteps)
                        AddEventExt(EV_FOOTSTEP, PM_FootstepForSurface());
                }
                else if (_pm.WaterLevel == 1)
                    AddEvent(EV_FOOTSPLASH);
                else if (_pm.WaterLevel == 2)
                    AddEvent(EV_SWIM);
            }
        }

        // -----------------------------------------------------------------------
        // PM_WaterEvents
        // -----------------------------------------------------------------------
        private void PM_WaterEvents()
        {
            if (_pml.PreviousWaterLevel == 0 && _pm.WaterLevel != 0)
                AddEvent(EV_WATER_TOUCH);
            if (_pml.PreviousWaterLevel != 0 && _pm.WaterLevel == 0)
                AddEvent(EV_WATER_LEAVE);
            if (_pml.PreviousWaterLevel != 3 && _pm.WaterLevel == 3)
                AddEvent(EV_WATER_UNDER);
            if (_pml.PreviousWaterLevel == 3 && _pm.WaterLevel != 3)
                AddEventExt(EV_WATER_CLEAR, _pm.Pmext.AirLeft < 6000 ? 1 : 0);
        }

        // -----------------------------------------------------------------------
        // PM_DropTimers
        // -----------------------------------------------------------------------
        private void PM_DropTimers()
        {
            var ps   = _pm.Ps;
            var pmxt = _pm.Pmext;

            if (ps.PmTime != 0)
            {
                if (_pml.Msec >= ps.PmTime)
                {
                    ps.PmFlags &= ~GameConst.PMF_ALL_TIMES;
                    ps.PmTime   = 0;
                }
                else
                {
                    ps.PmTime -= _pml.Msec;
                }
            }

            if (ps.LegsTimer > 0)
            {
                ps.LegsTimer -= _pml.Msec;
                if (ps.LegsTimer < 0) ps.LegsTimer = 0;
            }

            if (ps.TorsoTimer > 0)
            {
                ps.TorsoTimer -= _pml.Msec;
                if (ps.TorsoTimer < 0) ps.TorsoTimer = 0;
            }

            if (pmxt.WeapAnimTimer > 0)
            {
                pmxt.WeapAnimTimer -= _pml.Msec;
                if (pmxt.WeapAnimTimer < 0) pmxt.WeapAnimTimer = 0;
            }
        }

        // -----------------------------------------------------------------------
        // PM_Sprint
        // -----------------------------------------------------------------------
        private void PM_Sprint()
        {
            var ps  = _pm.Ps;
            var cmd = _pm.Cmd;
            var pmx = _pm.Pmext;

            const float SPRINTDRAIN = 5000f;
            const int   SPRINTTIME_I = (int)SPRINTTIME;

            bool sprinting = (cmd.Buttons & (int)Button.Sprint) != 0 &&
                             (cmd.ForwardMove != 0 || cmd.RightMove != 0) &&
                             (ps.PmFlags & GameConst.PMF_DUCKED) == 0 &&
                             (ps.EFlags & GameConst.EF_PRONE) == 0;

            if (sprinting)
            {
                pmx.SprintTime -= (int)(SPRINTDRAIN * _pml.FrameTime);
                if (pmx.SprintTime < 0) pmx.SprintTime = 0;
                if (ps.SprintExertTime == 0) ps.SprintExertTime = 1;
            }
            else
            {
                int rechargebase = 500;
                if (_pm.Skill[GameConst.SK_BATTLE_SENSE] >= 2)
                    rechargebase = (int)(rechargebase * 1.6f);

                pmx.SprintTime += (int)(rechargebase * _pml.FrameTime);
                if (pmx.SprintTime > SPRINTTIME_I)
                    pmx.SprintTime += (int)(rechargebase * _pml.FrameTime);

                if (pmx.SprintTime > SPRINTTIME_I) pmx.SprintTime = SPRINTTIME_I;
                ps.SprintExertTime = 0;
            }
        }

        // -----------------------------------------------------------------------
        // PM_CheckLadderMove
        // -----------------------------------------------------------------------
        private void PM_CheckLadderMove()
        {
            var ps = _pm.Ps;

            if (ps.PmTime != 0) return;
            if (ps.Stats[GameConst.STAT_HEALTH] <= 0)
            {
                ps.GroundEntityNum = GameConst.ENTITYNUM_NONE;
                _pml.GroundPlane   = false;
                _pml.Walking       = false;
                return;
            }
            if ((ps.EFlags & GameConst.EF_PRONE) != 0) return;

            bool wasOnLadder = (ps.PmFlags & GameConst.PMF_LADDER) != 0;
            _pml.Ladder = false;
            ps.PmFlags &= ~GameConst.PMF_LADDER;
            _ladderForward = false;

            float tracedist = _pml.Walking ? 1f : TRACE_LADDER_DIST;
            var flatfwd = new Vector3(_pml.Forward.x, _pml.Forward.y, 0f).normalized;
            var origin  = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
            var spot    = origin + flatfwd * tracedist;

            var trace = _pm.Trace(origin, _pm.Mins, _pm.Maxs, spot, ps.ClientNum, _pm.TraceMask);
            if (trace.Fraction < 1f && (trace.SurfaceFlags & (int)Surf.Ladder) != 0)
                _pml.Ladder = true;

            if (_pml.Ladder)
            {
                _ladderVec = trace.PlaneNormal;

                if (!_pml.Walking && trace.Fraction * tracedist > 1f)
                {
                    _pml.Ladder = false;
                    var mins = _pm.Mins;
                    mins.z = -1f;
                    var spot2 = origin + (-_ladderVec) * tracedist;
                    var tr2   = _pm.Trace(origin, mins, _pm.Maxs, spot2, ps.ClientNum, _pm.TraceMask);
                    if (tr2.Fraction < 1f && (tr2.SurfaceFlags & (int)Surf.Ladder) != 0)
                    {
                        _ladderForward = true;
                        _pml.Ladder    = true;
                        ps.PmFlags    |= GameConst.PMF_LADDER;
                    }
                }
                else
                {
                    ps.PmFlags |= GameConst.PMF_LADDER;
                }
            }

            if (_pml.Ladder && _pml.Walking && _pm.Cmd.ForwardMove <= 0)
                _pml.Ladder = false;
        }

        // -----------------------------------------------------------------------
        // PM_LadderMove
        // -----------------------------------------------------------------------
        private void PM_LadderMove()
        {
            var ps  = _pm.Ps;

            if (_ladderForward)
            {
                ps.Velocity0 = -_ladderVec.x * 200f;
                ps.Velocity1 = -_ladderVec.y * 200f;
            }

            float upscale = (_pml.Forward.z + 0.5f) * 2.5f;
            upscale = Mathf.Clamp(upscale, -1f, 1f);

            var fwd = new Vector3(_pml.Forward.x, _pml.Forward.y, 0f).normalized;
            var rgt = new Vector3(_pml.Right.x,   _pml.Right.y,   0f).normalized;

            float scale = PM_CmdScale();
            var wishvel = Vector3.zero;
            if (_pm.Cmd.ForwardMove != 0)
                wishvel.z = 0.9f * upscale * scale * _pm.Cmd.ForwardMove;

            if (_pm.Cmd.RightMove != 0)
                wishvel += rgt * (0.5f * scale * _pm.Cmd.RightMove);

            PM_Friction();

            if (ps.Velocity0 > -1f && ps.Velocity0 < 1f) ps.Velocity0 = 0f;
            if (ps.Velocity1 > -1f && ps.Velocity1 < 1f) ps.Velocity1 = 0f;

            var wishdir   = wishvel.normalized;
            float wishspeed = wishvel.magnitude;
            PM_Accelerate(wishdir, wishspeed, PM_ACCELERATE);

            if (wishvel.z == 0f)
            {
                if (ps.Velocity2 > 0f)
                {
                    ps.Velocity2 -= ps.Gravity * _pml.FrameTime;
                    if (ps.Velocity2 < 0f) ps.Velocity2 = 0f;
                }
                else
                {
                    ps.Velocity2 += ps.Gravity * _pml.FrameTime;
                    if (ps.Velocity2 > 0f) ps.Velocity2 = 0f;
                }
            }

            PM_StepSlideMove(false);
            ps.MovementDir = 0;
        }

        // -----------------------------------------------------------------------
        // PM_Weapon  (stub — full weapon logic is 1200+ lines, separate pass)
        // -----------------------------------------------------------------------
        private void PM_Weapon()
        {
            // TODO: port PM_Weapon from bg_pmove.c:3234
        }

        // -----------------------------------------------------------------------
        // PmoveSingle — one sub-step
        // -----------------------------------------------------------------------
        private void PmoveSingle()
        {
            var ps  = _pm.Ps;
            var cmd = _pm.Cmd;

            // clear results
            _pm.NumTouch  = 0;
            _pm.WaterType = 0;
            _pm.WaterLevel = 0;

            if (ps.Stats[GameConst.STAT_HEALTH] <= 0)
            {
                _pm.TraceMask &= ~Contents.Body;
                ps.EFlags     &= ~GameConst.EF_ZOOMING;
            }

            // walking button clear if running fast
            if (Math.Abs(cmd.ForwardMove) > 64 || Math.Abs(cmd.RightMove) > 64)
                cmd.Buttons &= ~(int)Button.Walking;

            // talk balloon
            if ((cmd.Buttons & (int)Button.Talk) != 0)
                ps.EFlags |= GameConst.EF_TALK;
            else
                ps.EFlags &= ~GameConst.EF_TALK;

            // firing / zooming
            ps.EFlags &= ~(GameConst.EF_FIRING | GameConst.EF_ZOOMING);

            if ((cmd.WButtons & (int)WButton.Zoom) != 0 &&
                ps.Stats[GameConst.STAT_HEALTH] >= 0 &&
                ps.WeaponDelay == 0 &&
                !IsScopedWeapon(ps.Weapon) &&
                !IsPlayerMounted(ps.EFlags) &&
                ps.Weapon != GameConst.WP_MOBILE_MG42_SET &&
                ps.Weapon != GameConst.WP_MORTAR_SET)
            {
                ps.EFlags |= GameConst.EF_ZOOMING;
            }

            // fire flag
            if ((ps.PmFlags & GameConst.PMF_RESPAWNED) == 0 &&
                ps.PmType != GameConst.PM_INTERMISSION)
            {
                if ((ps.EFlags & GameConst.EF_ZOOMING) == 0 &&
                    ps.Leanf == 0f &&
                    (ps.Weaponstate == GameConst.WEAPON_READY ||
                     ps.Weaponstate == GameConst.WEAPON_FIRING))
                {
                    if ((cmd.Buttons & (int)Button.Attack) != 0 &&
                        (cmd.Buttons & (int)Button.Talk)   == 0)
                        ps.EFlags |= GameConst.EF_FIRING;
                }
            }

            // respawn / covert ops silenced
            if ((ps.PmFlags & GameConst.PMF_RESPAWNED) != 0)
            {
                if (ps.Stats[GameConst.STAT_PLAYER_CLASS] == GameConst.PC_COVERTOPS)
                    _pm.Pmext.SilencedSideArm |= 1;
            }

            // clear respawned flag
            if (ps.Stats[GameConst.STAT_HEALTH] > 0 &&
                (cmd.Buttons & (int)Button.Attack) == 0)
                ps.PmFlags &= ~GameConst.PMF_RESPAWNED;

            // talk silences other input
            if ((cmd.Buttons & (int)Button.Talk) != 0)
            {
                cmd.Buttons     = (int)Button.Talk;
                cmd.WButtons    = 0;
                cmd.ForwardMove = 0;
                cmd.RightMove   = 0;
                cmd.UpMove      = 0;
                cmd.DoubleTap   = (int)DoubleTapType.None;
            }

            // mounted heavy weapon — no movement
            if (ps.Persistant[GameConst.PERS_HWEAPON_USE] != 0)
            {
                cmd.ForwardMove = 0;
                cmd.RightMove   = 0;
                cmd.UpMove      = 0;
            }

            // clear pml
            _pml = default;

            // determine time
            _pml.Msec = cmd.ServerTime - ps.CommandTime;
            if (_pml.Msec < 1)   _pml.Msec = 1;
            if (_pml.Msec > 200) _pml.Msec = 200;
            ps.CommandTime = cmd.ServerTime;

            _pml.PreviousOrigin   = new Vector3(ps.Origin0,   ps.Origin1,   ps.Origin2);
            _pml.PreviousVelocity = new Vector3(ps.Velocity0, ps.Velocity1, ps.Velocity2);
            _pml.FrameTime = _pml.Msec * 0.001f;

            // update view angles
            if (ps.PmType != GameConst.PM_FREEZE &&
                (ps.PmFlags & GameConst.PMF_LIMBO) == 0)
            {
                PM_UpdateViewAngles(ps, _pm.Pmext, cmd, _pm.Trace, _pm.TraceMask);
            }

            AngleVectors(ps.ViewAngles0, ps.ViewAngles1, ps.ViewAngles2,
                out _pml.Forward, out _pml.Right, out _pml.Up);

            if (cmd.UpMove < 10)
                ps.PmFlags &= ~GameConst.PMF_JUMP_HELD;

            // backwards run flag
            if (cmd.ForwardMove < 0)
                ps.PmFlags |= GameConst.PMF_BACKWARDS_RUN;
            else if (cmd.ForwardMove > 0 ||
                     (cmd.ForwardMove == 0 && cmd.RightMove != 0))
                ps.PmFlags &= ~GameConst.PMF_BACKWARDS_RUN;

            // dead / limbo / locked
            if (ps.PmType >= GameConst.PM_DEAD ||
                (ps.PmFlags & (GameConst.PMF_LIMBO | GameConst.PMF_TIME_LOCKPLAYER)) != 0)
            {
                cmd.ForwardMove = 0;
                cmd.RightMove   = 0;
                cmd.UpMove      = 0;
            }

            if (ps.PmType == GameConst.PM_SPECTATOR)
            {
                PM_CheckDuck();
                PM_FlyMove();
                PM_DropTimers();
                return;
            }

            if (ps.PmType == GameConst.PM_NOCLIP)
            {
                PM_NoclipMove();
                PM_DropTimers();
                return;
            }

            if (ps.PmType == GameConst.PM_FREEZE || ps.PmType == GameConst.PM_INTERMISSION)
                return;

            // water level and bounds
            PM_SetWaterLevel();
            _pml.PreviousWaterLevel = _pm.WaterLevel;

            if (!PM_CheckProne())
                PM_CheckDuck();

            PM_GroundTrace();

            if (ps.PmType == GameConst.PM_DEAD)
            {
                PM_DeadMove();
            }
            else
            {
                // force weapon down for mobile MG42 standing
                if (ps.Weapon == GameConst.WP_MOBILE_MG42_SET &&
                    (ps.EFlags & GameConst.EF_PRONE) == 0)
                {
                    // PM_BeginWeaponChange stub
                }
            }

            PM_CheckLadderMove();
            PM_DropTimers();

            if (_pml.Ladder)
                PM_LadderMove();
            else if ((ps.PmFlags & GameConst.PMF_TIME_WATERJUMP) != 0)
                PM_WaterJumpMove();
            else if (_pm.WaterLevel > 1)
                PM_WaterMove();
            else if (_pml.Walking && (ps.EFlags & GameConst.EF_MOUNTEDTANK) == 0)
                PM_WalkMove();
            else if ((ps.EFlags & GameConst.EF_MOUNTEDTANK) == 0)
                PM_AirMove();

            if ((ps.EFlags & GameConst.EF_MOUNTEDTANK) != 0)
            {
                ps.Velocity0  = ps.Velocity1  = ps.Velocity2  = 0f;
                ps.ViewHeight = GameConst.DEFAULT_VIEWHEIGHT;
            }

            PM_Sprint();

            PM_GroundTrace();
            PM_SetWaterLevel();

            PM_Weapon();
            PM_Footsteps();
            PM_WaterEvents();

            // snap velocity
            ps.Velocity0 = Mathf.Round(ps.Velocity0);
            ps.Velocity1 = Mathf.Round(ps.Velocity1);
            ps.Velocity2 = Mathf.Round(ps.Velocity2);
        }

        // -----------------------------------------------------------------------
        // Pmove — public entry, chops time into sub-steps
        // -----------------------------------------------------------------------
        public int Pmove()
        {
            var ps = _pm.Ps;
            int finalTime = _pm.Cmd.ServerTime;

            if (finalTime < ps.CommandTime) return 0;
            if (finalTime > ps.CommandTime + 1000)
                ps.CommandTime = finalTime - 1000;

            if ((ps.PmFlags & GameConst.PMF_TIME_LOAD) != 0 &&
                finalTime - ps.CommandTime > 50)
                ps.CommandTime = finalTime - 50;

            ps.PmoveFramecount = (ps.PmoveFramecount + 1) &
                                 ((1 << GameConst.PS_PMOVEFRAMECOUNTBITS) - 1);

            while (ps.CommandTime != finalTime)
            {
                int msec = finalTime - ps.CommandTime;
                if (_pm.PmoveFixed != 0)
                {
                    if (msec > _pm.PmoveMsec) msec = _pm.PmoveMsec;
                }
                else
                {
                    if (msec > 50) msec = 50;
                }

                _pm.Cmd.ServerTime = ps.CommandTime + msec;
                PmoveSingle();

                if ((ps.PmFlags & GameConst.PMF_JUMP_HELD) != 0)
                    _pm.Cmd.UpMove = 20;
            }

            if (ps.CurWeapHeat > 255) ps.CurWeapHeat = 255;
            else if (ps.CurWeapHeat < 0) ps.CurWeapHeat = 0;

            int surfFlags = _pml.GroundTrace.SurfaceFlags;
            if ((ps.Stats[GameConst.STAT_HEALTH] <= 0 || ps.PmType == GameConst.PM_DEAD) &&
                (surfFlags & (int)Surf.MonsterSlick) != 0)
                return surfFlags;

            return 0;
        }

        // -----------------------------------------------------------------------
        // Small helpers
        // -----------------------------------------------------------------------
        private static bool IsScopedWeapon(int weapon)
        {
            return weapon == GameConst.WP_GARAND_SCOPE ||
                   weapon == GameConst.WP_K43_SCOPE    ||
                   weapon == GameConst.WP_FG42SCOPE;
        }

        private static bool IsPlayerMounted(int eFlags)
        {
            return (eFlags & GameConst.EF_MG42_ACTIVE) != 0 ||
                   (eFlags & GameConst.EF_MOUNTEDTANK) != 0;
        }
    }
}
