// Ported from: src/game/bg_public.h  (pmove_t, pml_t)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using UnityEngine;
using ET.Network;

namespace ET.Game
{
    /// <summary>
    /// Trace function delegate — sweeps a box from start to end.
    /// Matches: void trace(trace_t*, start, mins, maxs, end, passEntityNum, contentMask)
    /// </summary>
    public delegate TraceResult TraceFunc(
        Vector3 start, Vector3 mins, Vector3 maxs,
        Vector3 end, int passEntityNum, int contentMask);

    /// <summary>
    /// PointContents delegate — returns content flags at a point.
    /// </summary>
    public delegate int PointContentsFunc(Vector3 point, int passEntityNum);

    /// <summary>
    /// pmove_t — full input/output structure for a player move.
    /// Passed to Pmove() / PmoveSingle() each server/client frame.
    /// </summary>
    public sealed class PmoveInput
    {
        // --- state (in/out) ---
        public PlayerState Ps    = new PlayerState();
        public PmoveExt    Pmext = new PmoveExt();

        // --- commands (in) ---
        public UserCmd Cmd    = new UserCmd();
        public UserCmd OldCmd = new UserCmd();

        public int  TraceMask;
        public int  DebugLevel;
        public bool NoFootsteps;
        public bool NoWeapClips;
        public bool GauntletHit;

        // --- NERVE / MP charge times (in) ---
        public int GameType;
        public int LtChargeTime;
        public int SoldierChargeTime;
        public int EngineerChargeTime;
        public int MedicChargeTime;
        public int CovertopsChargeTime;

        // --- results (out) ---
        public int   NumTouch;
        public int[] TouchEnts = new int[GameConst.MAXTOUCH];

        public Vector3 Mins, Maxs;  // bounding box (set by PM_CheckDuck / PM_CheckProne)

        public int   WaterType;
        public int   WaterLevel;

        public float XYSpeed;

        // --- player skills (in) ---
        public int[] Skill = new int[8];  // indexed by SK_*

        // --- fixed timestep (in) ---
        public int PmoveFixed;   // if nonzero, use PmoveMsec
        public int PmoveMsec;    // fixed msec per sub-step

        // --- callbacks (in) ---
        public TraceFunc         Trace;
        public PointContentsFunc PointContents;

        // BG_GetAnimScriptAnimation equivalent — returns the duration (ms) of a weapon
        // animation so PM_StartWeaponAnim can set WeapAnimTimer to lock the animation
        // against being overridden by idle.  Set to null if not available (timer stays 0).
        // Signature: (animIndex) → durationMs; returns 0 for looping anims.
        public Func<int, int> WeapAnimGetDurationMs;

        // --- character/anim (in) --- stub, filled by animation layer later
        // public BgCharacter Character;
    }

    /// <summary>
    /// pml_t — pmove local state, zeroed before every sub-step.
    /// Internal to PlayerMovement; defined here for visibility.
    /// </summary>
    public struct PmoveLocal
    {
        public Vector3 Forward, Right, Up;
        public float   FrameTime;
        public int     Msec;

        public bool       Walking;
        public bool       GroundPlane;
        public TraceResult GroundTrace;

        public float   ImpactSpeed;

        public Vector3 PreviousOrigin;
        public Vector3 PreviousVelocity;
        public int     PreviousWaterLevel;

        public bool    Ladder;   // Ridah: ladders
    }
}
