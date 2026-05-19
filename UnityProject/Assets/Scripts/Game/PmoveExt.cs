// Ported from: src/game/bg_public.h  (pmoveExt_t)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using UnityEngine;

namespace ET.Game
{
    /// <summary>
    /// pmoveExt_t — extra movement state shared between client and server.
    /// Persistent across frames (not zeroed before each pmove unlike pml_t).
    /// </summary>
    public sealed class PmoveExt
    {
        public bool  AutoReload;
        public int   JumpTime;          // MP: prevents jump acceleration
        public int   WeapAnimTimer;     // high-priority anim lock timer
        public int   SilencedSideArm;   // tracks whether luger/colt is silenced
        public int   SprintTime;        // sprint stamina

        public int   AirLeft;           // time left holding breath underwater

        // MG42 aiming constraints
        public float VArc, HArc;
        public Vector3 CenterAngles;

        public int   DtMove;            // doubletap move direction
        public int   DodgeTime;
        public int   ProneTime;         // timestamp when prone started/stopped
        public int   ProneGroundTime;   // time last had ground while prone
        public float ProneLegsOffset;   // legs bbox vertical offset when prone

        public Vector3 MountedWeaponAngles;  // mortar/mg42 aim angles

        // Weapon recoil
        public int   WeapRecoilTime;
        public int   WeapRecoilDuration;
        public float WeapRecoilYaw;
        public float WeapRecoilPitch;
        public int   LastRecoilDeltaTime;

        public bool  ReleasedFire;
    }
}
