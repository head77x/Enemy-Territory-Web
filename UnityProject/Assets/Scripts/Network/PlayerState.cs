// Ported from: src/game/q_shared.h  (playerState_t)
//              src/qcommon/msg.c     (playerStateFields[])
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;

namespace ET.Network
{
    public enum PmoveType
    {
        Normal,
        NoclipMode,
        Spectator,
        Dead,
        Freeze,
        Intermission,
    }

    public enum AiStateEnum
    {
        Relaxed,
        Query,
        Alert,
        Sight,
        Count,
    }

    /// <summary>
    /// playerState_t — full client-side game state snapshot.
    /// Arrays use short[] (16 bits each) for Stats/Persistant/Holdable/Powerups/Ammo/AmmoClip
    /// because the net protocol transmits them as 16-bit deltas.
    /// All other fields are int or float.
    /// </summary>
    public sealed class PlayerState
    {
        // --- core movement ---
        public int CommandTime;
        public int PmType;           // PmoveType
        public int BobCycle;
        public int PmFlags;
        public int PmTime;

        public float Origin0, Origin1, Origin2;
        public float Velocity0, Velocity1, Velocity2;

        public int WeaponTime;
        public int WeaponDelay;
        public int GrenadeTimeLeft;
        public int Gravity;
        public float Leanf;
        public int Speed;
        public int[] DeltaAngles = new int[3];

        public int GroundEntityNum;

        public int LegsTimer;
        public int LegsAnim;
        public int TorsoTimer;
        public int TorsoAnim;

        public int MovementDir;

        public int EFlags;
        public int EventSequence;
        public int[] Events     = new int[4];
        public int[] EventParms = new int[4];
        public int OldEventSequence;

        public int ExternalEvent;
        public int ExternalEventParm;
        public int ExternalEventTime;

        public int ClientNum;

        // --- weapon ---
        public int Weapon;
        public int Weaponstate;
        public int Item;
        public int WeapAnim;
        public int NextWeapon;

        // --- view ---
        public float ViewAngles0, ViewAngles1, ViewAngles2;
        public int   ViewHeight;
        public int   ViewLocked;
        public int   ViewLockedEntNum;

        // --- damage ---
        public int DamageEvent;
        public int DamageYaw;
        public int DamagePitch;
        public int DamageCount;

        // --- arrays (16-bit each for efficient delta) ---
        public short[] Stats     = new short[16];   // MAX_STATS
        public short[] Persistant= new short[16];   // MAX_PERSISTANT
        public int[]   Powerups  = new int[16];     // MAX_POWERUPS — stores level.time (full 32-bit)
        public short[] Ammo      = new short[64];   // MAX_WEAPONS
        public short[] AmmoClip  = new short[64];   // MAX_WEAPONS
        public short[] Holdable  = new short[16];   // delta'd as short[16] in net
        public int[]   Weapons   = new int[2];      // 64-bit weapon bitmask split across 2 ints

        // --- bounding box (variable) ---
        public float Mins0, Mins1, Mins2;
        public float Maxs0, Maxs1, Maxs2;
        public float CrouchMaxZ;
        public float CrouchViewHeight, StandViewHeight, DeadViewHeight;
        public float RunSpeedScale, SprintSpeedScale, CrouchSpeedScale;

        public float Friction;

        // --- misc ---
        public int TeamNum;
        public int OnFireStart;
        public int CurWeapHeat;
        public int AimSpreadScale;
        public int ServerCursorHint;
        public int ServerCursorHintVal;
        public int ClassWeaponTime;
        public int IdentifyClient;
        public int IdentifyClientHealth;
        public int AiState;

        // --- non-networked (local simulation only) ---
        public int Ping;
        public int PmoveFramecount;
        public int EntityEventSequence;
        public int SprintExertTime;
        public int JumpTime;
        public bool ReleasedFire;
        public float AimSpreadScaleFloat;
        public int LastFireTime;
        public int QuickGrenTime;
        public int LeanStopDebounceTime;
        public int[] WeapHeat = new int[64];   // per-weapon heat (not networked per-weapon)

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        public void CopyFrom(PlayerState src)
        {
            CommandTime = src.CommandTime;
            PmType      = src.PmType;
            BobCycle    = src.BobCycle;
            PmFlags     = src.PmFlags;
            PmTime      = src.PmTime;
            Origin0     = src.Origin0;    Origin1    = src.Origin1;    Origin2    = src.Origin2;
            Velocity0   = src.Velocity0;  Velocity1  = src.Velocity1;  Velocity2  = src.Velocity2;
            WeaponTime  = src.WeaponTime; WeaponDelay= src.WeaponDelay; GrenadeTimeLeft=src.GrenadeTimeLeft;
            Gravity     = src.Gravity;
            Leanf       = src.Leanf;
            Speed       = src.Speed;
            DeltaAngles[0]=src.DeltaAngles[0]; DeltaAngles[1]=src.DeltaAngles[1]; DeltaAngles[2]=src.DeltaAngles[2];
            GroundEntityNum = src.GroundEntityNum;
            LegsTimer   = src.LegsTimer;  LegsAnim   = src.LegsAnim;
            TorsoTimer  = src.TorsoTimer; TorsoAnim  = src.TorsoAnim;
            MovementDir = src.MovementDir;
            EFlags      = src.EFlags;
            EventSequence = src.EventSequence;
            Array.Copy(src.Events,     Events,     4);
            Array.Copy(src.EventParms, EventParms, 4);
            OldEventSequence    = src.OldEventSequence;
            ExternalEvent       = src.ExternalEvent;
            ExternalEventParm   = src.ExternalEventParm;
            ExternalEventTime   = src.ExternalEventTime;
            ClientNum           = src.ClientNum;
            Weapon              = src.Weapon;
            Weaponstate         = src.Weaponstate;
            Item                = src.Item;
            WeapAnim            = src.WeapAnim;
            NextWeapon          = src.NextWeapon;
            ViewAngles0=src.ViewAngles0; ViewAngles1=src.ViewAngles1; ViewAngles2=src.ViewAngles2;
            ViewHeight          = src.ViewHeight;
            ViewLocked          = src.ViewLocked;
            ViewLockedEntNum    = src.ViewLockedEntNum;
            DamageEvent         = src.DamageEvent;
            DamageYaw           = src.DamageYaw;
            DamagePitch         = src.DamagePitch;
            DamageCount         = src.DamageCount;
            Array.Copy(src.Stats,      Stats,      16);
            Array.Copy(src.Persistant, Persistant, 16);
            Array.Copy(src.Powerups,   Powerups,   16);
            Array.Copy(src.Ammo,       Ammo,       64);
            Array.Copy(src.AmmoClip,   AmmoClip,   64);
            Array.Copy(src.Holdable,   Holdable,   16);
            Array.Copy(src.Weapons,    Weapons,    2);
            Mins0=src.Mins0; Mins1=src.Mins1; Mins2=src.Mins2;
            Maxs0=src.Maxs0; Maxs1=src.Maxs1; Maxs2=src.Maxs2;
            CrouchMaxZ          = src.CrouchMaxZ;
            CrouchViewHeight    = src.CrouchViewHeight;
            StandViewHeight     = src.StandViewHeight;
            DeadViewHeight      = src.DeadViewHeight;
            RunSpeedScale       = src.RunSpeedScale;
            SprintSpeedScale    = src.SprintSpeedScale;
            CrouchSpeedScale    = src.CrouchSpeedScale;
            Friction            = src.Friction;
            TeamNum             = src.TeamNum;
            OnFireStart         = src.OnFireStart;
            CurWeapHeat         = src.CurWeapHeat;
            AimSpreadScale      = src.AimSpreadScale;
            ServerCursorHint    = src.ServerCursorHint;
            ServerCursorHintVal = src.ServerCursorHintVal;
            ClassWeaponTime     = src.ClassWeaponTime;
            IdentifyClient      = src.IdentifyClient;
            IdentifyClientHealth= src.IdentifyClientHealth;
            AiState             = src.AiState;
        }

        // -------------------------------------------------------
        // NetField descriptors — mirrors playerStateFields[] from msg.c
        // Negative bits in the original (-16, -8) → abs value used; sign is preserved
        // naturally since C# int/WriteBits handles two's complement.
        // -------------------------------------------------------
        private static int FI(float f) => BitConverter.SingleToInt32Bits(f);
        private static float IF(int i)  => BitConverter.Int32BitsToSingle(i);

        private const int GB = NetConst.GENTITYNUM_BITS;
        private const int AB = NetConst.ANIM_BITS;

        public static readonly NetField<PlayerState>[] NetFields =
        {
            new(32, ps=>ps.CommandTime,     (ps,v)=>ps.CommandTime=v),
            new(8,  ps=>ps.PmType,          (ps,v)=>ps.PmType=v),
            new(8,  ps=>ps.BobCycle,        (ps,v)=>ps.BobCycle=v),
            new(16, ps=>ps.PmFlags,         (ps,v)=>ps.PmFlags=v),
            new(16, ps=>ps.PmTime,          (ps,v)=>ps.PmTime=v),           // -16 → 16
            new(0,  ps=>FI(ps.Origin0),     (ps,v)=>ps.Origin0=IF(v)),
            new(0,  ps=>FI(ps.Origin1),     (ps,v)=>ps.Origin1=IF(v)),
            new(0,  ps=>FI(ps.Origin2),     (ps,v)=>ps.Origin2=IF(v)),
            new(0,  ps=>FI(ps.Velocity0),   (ps,v)=>ps.Velocity0=IF(v)),
            new(0,  ps=>FI(ps.Velocity1),   (ps,v)=>ps.Velocity1=IF(v)),
            new(0,  ps=>FI(ps.Velocity2),   (ps,v)=>ps.Velocity2=IF(v)),
            new(16, ps=>ps.WeaponTime,      (ps,v)=>ps.WeaponTime=v),       // -16
            new(16, ps=>ps.WeaponDelay,     (ps,v)=>ps.WeaponDelay=v),      // -16
            new(16, ps=>ps.GrenadeTimeLeft, (ps,v)=>ps.GrenadeTimeLeft=v),  // -16
            new(16, ps=>ps.Gravity,         (ps,v)=>ps.Gravity=v),
            new(0,  ps=>FI(ps.Leanf),       (ps,v)=>ps.Leanf=IF(v)),
            new(16, ps=>ps.Speed,           (ps,v)=>ps.Speed=v),
            new(16, ps=>ps.DeltaAngles[0],  (ps,v)=>ps.DeltaAngles[0]=v),
            new(16, ps=>ps.DeltaAngles[1],  (ps,v)=>ps.DeltaAngles[1]=v),
            new(16, ps=>ps.DeltaAngles[2],  (ps,v)=>ps.DeltaAngles[2]=v),
            new(GB, ps=>ps.GroundEntityNum, (ps,v)=>ps.GroundEntityNum=v),
            new(16, ps=>ps.LegsTimer,       (ps,v)=>ps.LegsTimer=v),
            new(16, ps=>ps.TorsoTimer,      (ps,v)=>ps.TorsoTimer=v),
            new(AB, ps=>ps.LegsAnim,        (ps,v)=>ps.LegsAnim=v),
            new(AB, ps=>ps.TorsoAnim,       (ps,v)=>ps.TorsoAnim=v),
            new(8,  ps=>ps.MovementDir,     (ps,v)=>ps.MovementDir=v),
            new(24, ps=>ps.EFlags,          (ps,v)=>ps.EFlags=v),
            new(8,  ps=>ps.EventSequence,   (ps,v)=>ps.EventSequence=v),
            new(8,  ps=>ps.Events[0],       (ps,v)=>ps.Events[0]=v),
            new(8,  ps=>ps.Events[1],       (ps,v)=>ps.Events[1]=v),
            new(8,  ps=>ps.Events[2],       (ps,v)=>ps.Events[2]=v),
            new(8,  ps=>ps.Events[3],       (ps,v)=>ps.Events[3]=v),
            new(8,  ps=>ps.EventParms[0],   (ps,v)=>ps.EventParms[0]=v),
            new(8,  ps=>ps.EventParms[1],   (ps,v)=>ps.EventParms[1]=v),
            new(8,  ps=>ps.EventParms[2],   (ps,v)=>ps.EventParms[2]=v),
            new(8,  ps=>ps.EventParms[3],   (ps,v)=>ps.EventParms[3]=v),
            new(8,  ps=>ps.ClientNum,       (ps,v)=>ps.ClientNum=v),
            new(32, ps=>ps.Weapons[0],      (ps,v)=>ps.Weapons[0]=v),
            new(32, ps=>ps.Weapons[1],      (ps,v)=>ps.Weapons[1]=v),
            new(7,  ps=>ps.Weapon,          (ps,v)=>ps.Weapon=v),
            new(4,  ps=>ps.Weaponstate,     (ps,v)=>ps.Weaponstate=v),
            new(AB, ps=>ps.WeapAnim,        (ps,v)=>ps.WeapAnim=v),
            new(0,  ps=>FI(ps.ViewAngles0), (ps,v)=>ps.ViewAngles0=IF(v)),
            new(0,  ps=>FI(ps.ViewAngles1), (ps,v)=>ps.ViewAngles1=IF(v)),
            new(0,  ps=>FI(ps.ViewAngles2), (ps,v)=>ps.ViewAngles2=IF(v)),
            new(8,  ps=>ps.ViewHeight,      (ps,v)=>ps.ViewHeight=v),       // -8
            new(8,  ps=>ps.DamageEvent,     (ps,v)=>ps.DamageEvent=v),
            new(8,  ps=>ps.DamageYaw,       (ps,v)=>ps.DamageYaw=v),
            new(8,  ps=>ps.DamagePitch,     (ps,v)=>ps.DamagePitch=v),
            new(8,  ps=>ps.DamageCount,     (ps,v)=>ps.DamageCount=v),
            new(0,  ps=>FI(ps.Mins0),       (ps,v)=>ps.Mins0=IF(v)),
            new(0,  ps=>FI(ps.Mins1),       (ps,v)=>ps.Mins1=IF(v)),
            new(0,  ps=>FI(ps.Mins2),       (ps,v)=>ps.Mins2=IF(v)),
            new(0,  ps=>FI(ps.Maxs0),       (ps,v)=>ps.Maxs0=IF(v)),
            new(0,  ps=>FI(ps.Maxs1),       (ps,v)=>ps.Maxs1=IF(v)),
            new(0,  ps=>FI(ps.Maxs2),       (ps,v)=>ps.Maxs2=IF(v)),
            new(0,  ps=>FI(ps.CrouchMaxZ),  (ps,v)=>ps.CrouchMaxZ=IF(v)),
            new(0,  ps=>FI(ps.CrouchViewHeight),(ps,v)=>ps.CrouchViewHeight=IF(v)),
            new(0,  ps=>FI(ps.StandViewHeight),(ps,v)=>ps.StandViewHeight=IF(v)),
            new(0,  ps=>FI(ps.DeadViewHeight),(ps,v)=>ps.DeadViewHeight=IF(v)),
            new(0,  ps=>FI(ps.RunSpeedScale),(ps,v)=>ps.RunSpeedScale=IF(v)),
            new(0,  ps=>FI(ps.SprintSpeedScale),(ps,v)=>ps.SprintSpeedScale=IF(v)),
            new(0,  ps=>FI(ps.CrouchSpeedScale),(ps,v)=>ps.CrouchSpeedScale=IF(v)),
            new(0,  ps=>FI(ps.Friction),    (ps,v)=>ps.Friction=IF(v)),
            new(8,  ps=>ps.ViewLocked,      (ps,v)=>ps.ViewLocked=v),
            new(16, ps=>ps.ViewLockedEntNum,(ps,v)=>ps.ViewLockedEntNum=v),
            new(8,  ps=>ps.NextWeapon,      (ps,v)=>ps.NextWeapon=v),
            new(8,  ps=>ps.TeamNum,         (ps,v)=>ps.TeamNum=v),
            new(32, ps=>ps.OnFireStart,     (ps,v)=>ps.OnFireStart=v),
            new(8,  ps=>ps.CurWeapHeat,     (ps,v)=>ps.CurWeapHeat=v),
            new(8,  ps=>ps.AimSpreadScale,  (ps,v)=>ps.AimSpreadScale=v),
            new(8,  ps=>ps.ServerCursorHint,(ps,v)=>ps.ServerCursorHint=v),
            new(8,  ps=>ps.ServerCursorHintVal,(ps,v)=>ps.ServerCursorHintVal=v),
            new(32, ps=>ps.ClassWeaponTime, (ps,v)=>ps.ClassWeaponTime=v),
            new(8,  ps=>ps.IdentifyClient,  (ps,v)=>ps.IdentifyClient=v),
            new(8,  ps=>ps.IdentifyClientHealth,(ps,v)=>ps.IdentifyClientHealth=v),
            new(2,  ps=>ps.AiState,         (ps,v)=>ps.AiState=v),
        };
    }
}
