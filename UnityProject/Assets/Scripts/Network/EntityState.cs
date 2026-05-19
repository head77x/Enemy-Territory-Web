// Ported from: src/game/q_shared.h  (entityState_t, trajectory_t, trType_t, entityType_t)
//              src/qcommon/msg.c     (entityStateFields[])
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;

namespace ET.Network
{
    public enum TrajectoryType
    {
        Stationary,
        Interpolate,
        Linear,
        LinearStop,
        LinearStopBack,
        Sine,
        Gravity,
        GravityLow,
        GravityFloat,
        GravityPaused,
        Accelerate,
        Decelerate,
        Spline,
        LinearPath,
    }

    public enum EntityType
    {
        General,
        Player,
        Item,
        Missile,
        Mover,
        Beam,
        Portal,
        Speaker,
        PushTrigger,
        TeleportTrigger,
        Invisible,
        ConcussiveTrigger,
        OidTrigger,
        ExplosiveIndicator,
        Explosive,
        EfSpotlight,
        Alarmbox,
        Corona,
        Trap,
        Gamemodel,
        Footlocker,
        FlameBarrel,
        FpParts,
        FireColumn,
        FireColumnSmoke,
        Ramjet,
        FlamethrowerChunk,
        ExploPart,
        Prop,
        AiEffect,
        Camera,
        MoverScaled,
        ConstructibleIndicator,
        Constructible,
        ConstructibleMarker,
        Bomb,
        Waypoint,
        Beam2,
        TankIndicator,
        TankIndicatorDead,
        BotgoalIndicator,
        Corpse,
        Smoker,
        TempHead,
        Mg42Barrel,
        TempLegs,
        TriggerMultiple,
        TriggerFlagonly,
        TriggerFlagonlyMultiple,
        Gamemanager,
        Aagun,
        CabinetH,
        CabinetA,
        Healer,
        Supplier,
        LandmineHint,
        AttractorHint,
        SniperHint,
        LandminespotHint,
        CommandmapMarker,
        WolfObjective,
        Events,   // base — add eventNum to get specific event entity
        PortalSurface,
        PortalCamera,
        MG42Mount,
        AAGun,
        Flak,
    }

    /// <summary>trajectory_t — position/angular interpolation descriptor.</summary>
    public sealed class Trajectory
    {
        public int   TrType;       // TrajectoryType stored as int
        public int   TrTime;
        public int   TrDuration;
        public float TrBase0, TrBase1, TrBase2;
        public float TrDelta0, TrDelta1, TrDelta2;

        public void CopyFrom(Trajectory src)
        {
            TrType     = src.TrType;
            TrTime     = src.TrTime;
            TrDuration = src.TrDuration;
            TrBase0    = src.TrBase0;  TrBase1  = src.TrBase1;  TrBase2  = src.TrBase2;
            TrDelta0   = src.TrDelta0; TrDelta1 = src.TrDelta1; TrDelta2 = src.TrDelta2;
        }
    }

    /// <summary>
    /// entityState_t — per-entity network snapshot state.
    /// All fields are int or float, matching the C struct layout constraint.
    /// Floats exposed directly; the NetField descriptors reinterpret them as int bits.
    /// </summary>
    public sealed class EntityState
    {
        public int Number;           // entity index
        public int EType;            // EntityType
        public int EFlags;

        public readonly Trajectory Pos  = new();  // position trajectory
        public readonly Trajectory APos = new();  // angular trajectory

        public int Time;
        public int Time2;

        public float Origin0, Origin1, Origin2;
        public float Origin20, Origin21, Origin22;

        public float Angles0, Angles1, Angles2;
        public float Angles20, Angles21, Angles22;

        public int OtherEntityNum;
        public int OtherEntityNum2;
        public int GroundEntityNum;

        public int LoopSound;
        public int ConstantLight;
        public int DlIntensity;

        public int ModelIndex;
        public int ModelIndex2;
        public int Frame;
        public int ClientNum;

        public int Solid;

        public int Event;
        public int EventParm;
        public int EventSequence;
        public int[] Events     = new int[4];
        public int[] EventParms = new int[4];

        public int Powerups;
        public int Weapon;
        public int LegsAnim;
        public int TorsoAnim;

        public int Density;
        public int DmgFlags;

        public int OnFireStart;
        public int OnFireEnd;

        public int NextWeapon;
        public int TeamNum;

        public int Effect1Time;
        public int Effect2Time;
        public int Effect3Time;

        public int AnimMovetype;
        public int AiState;

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        public void Clear()
        {
            Number = EType = EFlags = 0;
            Pos.CopyFrom(new Trajectory()); APos.CopyFrom(new Trajectory());
            Time = Time2 = 0;
            Origin0=Origin1=Origin2=0f; Origin20=Origin21=Origin22=0f;
            Angles0=Angles1=Angles2=0f; Angles20=Angles21=Angles22=0f;
            OtherEntityNum=OtherEntityNum2=GroundEntityNum=0;
            LoopSound=ConstantLight=DlIntensity=0;
            ModelIndex=ModelIndex2=Frame=ClientNum=0;
            Solid=Event=EventParm=EventSequence=0;
            Array.Clear(Events,0,4); Array.Clear(EventParms,0,4);
            Powerups=Weapon=LegsAnim=TorsoAnim=0;
            Density=DmgFlags=OnFireStart=OnFireEnd=0;
            NextWeapon=TeamNum=0;
            Effect1Time=Effect2Time=Effect3Time=0;
            AnimMovetype=AiState=0;
        }

        public void CopyFrom(EntityState src)
        {
            Number = src.Number; EType = src.EType; EFlags = src.EFlags;
            Pos.CopyFrom(src.Pos); APos.CopyFrom(src.APos);
            Time = src.Time; Time2 = src.Time2;
            Origin0  = src.Origin0;  Origin1  = src.Origin1;  Origin2  = src.Origin2;
            Origin20 = src.Origin20; Origin21 = src.Origin21; Origin22 = src.Origin22;
            Angles0  = src.Angles0;  Angles1  = src.Angles1;  Angles2  = src.Angles2;
            Angles20 = src.Angles20; Angles21 = src.Angles21; Angles22 = src.Angles22;
            OtherEntityNum  = src.OtherEntityNum;
            OtherEntityNum2 = src.OtherEntityNum2;
            GroundEntityNum = src.GroundEntityNum;
            LoopSound=src.LoopSound; ConstantLight=src.ConstantLight; DlIntensity=src.DlIntensity;
            ModelIndex=src.ModelIndex; ModelIndex2=src.ModelIndex2; Frame=src.Frame; ClientNum=src.ClientNum;
            Solid=src.Solid;
            Event=src.Event; EventParm=src.EventParm; EventSequence=src.EventSequence;
            Array.Copy(src.Events,     Events,     4);
            Array.Copy(src.EventParms, EventParms, 4);
            Powerups=src.Powerups; Weapon=src.Weapon; LegsAnim=src.LegsAnim; TorsoAnim=src.TorsoAnim;
            Density=src.Density; DmgFlags=src.DmgFlags;
            OnFireStart=src.OnFireStart; OnFireEnd=src.OnFireEnd;
            NextWeapon=src.NextWeapon; TeamNum=src.TeamNum;
            Effect1Time=src.Effect1Time; Effect2Time=src.Effect2Time; Effect3Time=src.Effect3Time;
            AnimMovetype=src.AnimMovetype; AiState=src.AiState;
        }

        // -------------------------------------------------------
        // NetField descriptors — mirrors entityStateFields[] from msg.c
        // bits==0 → float (reinterpreted); bits>0 → integer with that width
        // -------------------------------------------------------
        private static int FI(float f) => BitConverter.SingleToInt32Bits(f);
        private static float IF(int i)  => BitConverter.Int32BitsToSingle(i);

        private const int GB = NetConst.GENTITYNUM_BITS;
        private const int AB = NetConst.ANIM_BITS;

        public static readonly NetField<EntityState>[] NetFields =
        {
            new(8,  es=>es.EType,       (es,v)=>es.EType=v),
            new(24, es=>es.EFlags,      (es,v)=>es.EFlags=v),
            new(8,  es=>es.Pos.TrType,  (es,v)=>es.Pos.TrType=v),
            new(32, es=>es.Pos.TrTime,  (es,v)=>es.Pos.TrTime=v),
            new(32, es=>es.Pos.TrDuration,(es,v)=>es.Pos.TrDuration=v),
            new(0,  es=>FI(es.Pos.TrBase0), (es,v)=>es.Pos.TrBase0=IF(v)),
            new(0,  es=>FI(es.Pos.TrBase1), (es,v)=>es.Pos.TrBase1=IF(v)),
            new(0,  es=>FI(es.Pos.TrBase2), (es,v)=>es.Pos.TrBase2=IF(v)),
            new(0,  es=>FI(es.Pos.TrDelta0),(es,v)=>es.Pos.TrDelta0=IF(v)),
            new(0,  es=>FI(es.Pos.TrDelta1),(es,v)=>es.Pos.TrDelta1=IF(v)),
            new(0,  es=>FI(es.Pos.TrDelta2),(es,v)=>es.Pos.TrDelta2=IF(v)),
            new(8,  es=>es.APos.TrType, (es,v)=>es.APos.TrType=v),
            new(32, es=>es.APos.TrTime, (es,v)=>es.APos.TrTime=v),
            new(32, es=>es.APos.TrDuration,(es,v)=>es.APos.TrDuration=v),
            new(0,  es=>FI(es.APos.TrBase0),(es,v)=>es.APos.TrBase0=IF(v)),
            new(0,  es=>FI(es.APos.TrBase1),(es,v)=>es.APos.TrBase1=IF(v)),
            new(0,  es=>FI(es.APos.TrBase2),(es,v)=>es.APos.TrBase2=IF(v)),
            new(0,  es=>FI(es.APos.TrDelta0),(es,v)=>es.APos.TrDelta0=IF(v)),
            new(0,  es=>FI(es.APos.TrDelta1),(es,v)=>es.APos.TrDelta1=IF(v)),
            new(0,  es=>FI(es.APos.TrDelta2),(es,v)=>es.APos.TrDelta2=IF(v)),
            new(32, es=>es.Time,         (es,v)=>es.Time=v),
            new(32, es=>es.Time2,        (es,v)=>es.Time2=v),
            new(0,  es=>FI(es.Origin0),  (es,v)=>es.Origin0=IF(v)),
            new(0,  es=>FI(es.Origin1),  (es,v)=>es.Origin1=IF(v)),
            new(0,  es=>FI(es.Origin2),  (es,v)=>es.Origin2=IF(v)),
            new(0,  es=>FI(es.Origin20), (es,v)=>es.Origin20=IF(v)),
            new(0,  es=>FI(es.Origin21), (es,v)=>es.Origin21=IF(v)),
            new(0,  es=>FI(es.Origin22), (es,v)=>es.Origin22=IF(v)),
            new(0,  es=>FI(es.Angles0),  (es,v)=>es.Angles0=IF(v)),
            new(0,  es=>FI(es.Angles1),  (es,v)=>es.Angles1=IF(v)),
            new(0,  es=>FI(es.Angles2),  (es,v)=>es.Angles2=IF(v)),
            new(0,  es=>FI(es.Angles20), (es,v)=>es.Angles20=IF(v)),
            new(0,  es=>FI(es.Angles21), (es,v)=>es.Angles21=IF(v)),
            new(0,  es=>FI(es.Angles22), (es,v)=>es.Angles22=IF(v)),
            new(GB, es=>es.OtherEntityNum,  (es,v)=>es.OtherEntityNum=v),
            new(GB, es=>es.OtherEntityNum2, (es,v)=>es.OtherEntityNum2=v),
            new(GB, es=>es.GroundEntityNum, (es,v)=>es.GroundEntityNum=v),
            new(8,  es=>es.LoopSound,    (es,v)=>es.LoopSound=v),
            new(32, es=>es.ConstantLight,(es,v)=>es.ConstantLight=v),
            new(32, es=>es.DlIntensity,  (es,v)=>es.DlIntensity=v),
            new(9,  es=>es.ModelIndex,   (es,v)=>es.ModelIndex=v),
            new(9,  es=>es.ModelIndex2,  (es,v)=>es.ModelIndex2=v),
            new(16, es=>es.Frame,        (es,v)=>es.Frame=v),
            new(8,  es=>es.ClientNum,    (es,v)=>es.ClientNum=v),
            new(24, es=>es.Solid,        (es,v)=>es.Solid=v),
            new(10, es=>es.Event,        (es,v)=>es.Event=v),
            new(8,  es=>es.EventParm,    (es,v)=>es.EventParm=v),
            new(8,  es=>es.EventSequence,(es,v)=>es.EventSequence=v),
            new(8,  es=>es.Events[0],    (es,v)=>es.Events[0]=v),
            new(8,  es=>es.Events[1],    (es,v)=>es.Events[1]=v),
            new(8,  es=>es.Events[2],    (es,v)=>es.Events[2]=v),
            new(8,  es=>es.Events[3],    (es,v)=>es.Events[3]=v),
            new(8,  es=>es.EventParms[0],(es,v)=>es.EventParms[0]=v),
            new(8,  es=>es.EventParms[1],(es,v)=>es.EventParms[1]=v),
            new(8,  es=>es.EventParms[2],(es,v)=>es.EventParms[2]=v),
            new(8,  es=>es.EventParms[3],(es,v)=>es.EventParms[3]=v),
            new(16, es=>es.Powerups,     (es,v)=>es.Powerups=v),
            new(8,  es=>es.Weapon,       (es,v)=>es.Weapon=v),
            new(AB, es=>es.LegsAnim,     (es,v)=>es.LegsAnim=v),
            new(AB, es=>es.TorsoAnim,    (es,v)=>es.TorsoAnim=v),
            new(10, es=>es.Density,      (es,v)=>es.Density=v),
            new(32, es=>es.DmgFlags,     (es,v)=>es.DmgFlags=v),
            new(32, es=>es.OnFireStart,  (es,v)=>es.OnFireStart=v),
            new(32, es=>es.OnFireEnd,    (es,v)=>es.OnFireEnd=v),
            new(8,  es=>es.NextWeapon,   (es,v)=>es.NextWeapon=v),
            new(8,  es=>es.TeamNum,      (es,v)=>es.TeamNum=v),
            new(32, es=>es.Effect1Time,  (es,v)=>es.Effect1Time=v),
            new(32, es=>es.Effect2Time,  (es,v)=>es.Effect2Time=v),
            new(32, es=>es.Effect3Time,  (es,v)=>es.Effect3Time=v),
            new(4,  es=>es.AnimMovetype, (es,v)=>es.AnimMovetype=v),
            new(2,  es=>es.AiState,      (es,v)=>es.AiState=v),
        };
    }
}
