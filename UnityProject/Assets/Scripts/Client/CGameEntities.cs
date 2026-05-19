// Ported from: src/cgame/cg_ents.c, src/cgame/cg_players.c, src/cgame/cg_event.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    // centity_t — per-entity client-side interpolation and rendering state.
    public class CentityState
    {
        public EntityState  CurrentState    = new EntityState();
        public EntityState  NextState       = new EntityState();
        public bool         Interpolate;
        public bool         CurrentValid;
        public int          MuzzleFlashTime;
        public int          PreviousEventTime;
        public bool         Teleported;
        public int          TrailTime;
        public int          DustTrailTime;
        public int          MasterBuild;
        public int          SnapShotTime;
        public Vector3      LerpOrigin;
        public Vector3      LerpAngles;
        public int          LerpFrame;
        public bool         EFRecent;
        public bool         EFActive;
    }

    public static class CGameEntities
    {
        public static readonly CentityState[] Entities = new CentityState[GameConst.MAX_GENTITIES];

        // Fires (entNum, eventType, worldPos) for any downstream subscriber.
        public static event Action<int, int, Vector3>  OnEntityEvent;
        // Fires after a player entity's position/animation have been refreshed.
        public static event Action<int>                OnPlayerUpdated;
        // Fires when a missile/generic explosion is triggered.
        public static event Action<int, Vector3, bool> OnExplosion;
        // Fires when a looping animation clip name is chosen for a player.
        public static event Action<int, string>        OnAnimationChange;

        // -----------------------------------------------------------------------
        // Static constructor — pre-allocate all entity slots.
        // -----------------------------------------------------------------------
        static CGameEntities()
        {
            for (int i = 0; i < GameConst.MAX_GENTITIES; i++)
                Entities[i] = new CentityState();
        }

        // -----------------------------------------------------------------------
        // Event raisers — needed because C# events can only be invoked from
        // within the declaring class.
        // -----------------------------------------------------------------------
        public static void RaiseEntityEvent(int entityNum, int ev, Vector3 origin)
            => OnEntityEvent?.Invoke(entityNum, ev, origin);

        public static void RaiseExplosion(int entityNum, Vector3 origin, bool large)
            => OnExplosion?.Invoke(entityNum, origin, large);

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        // ET coordinate space → Unity: ET(x,y,z) → Unity(-y, z, x)
        private static Vector3 ETtoUnity(float x, float y, float z)
            => new Vector3(-y, z, x);

        private static Vector3 EntityOrigin(EntityState es)
            => ETtoUnity(es.Origin0, es.Origin1, es.Origin2);

        // -----------------------------------------------------------------------
        // cg_ents.c — entity state management
        // -----------------------------------------------------------------------

        public static void CG_ResetEntity(CentityState cent)
        {
            cent.CurrentState.Clear();
            cent.NextState.Clear();
            cent.Interpolate        = false;
            cent.CurrentValid       = false;
            cent.MuzzleFlashTime    = 0;
            cent.PreviousEventTime  = 0;
            cent.Teleported         = false;
            cent.TrailTime          = 0;
            cent.DustTrailTime      = 0;
            cent.MasterBuild        = 0;
            cent.SnapShotTime       = 0;
            cent.LerpOrigin         = Vector3.zero;
            cent.LerpAngles         = Vector3.zero;
            cent.LerpFrame          = 0;
            cent.EFRecent           = false;
            cent.EFActive           = false;
        }

        public static void CG_TransitionEntity(CentityState cent)
        {
            cent.CurrentState.CopyFrom(cent.NextState);
            cent.CurrentValid = true;

            // Detect teleport: EF_TELEPORT_BIT toggled between snaps.
            if ((cent.CurrentState.EFlags & GameConst.EF_TELEPORT_BIT) !=
                (cent.NextState.EFlags    & GameConst.EF_TELEPORT_BIT))
            {
                cent.Teleported = true;
            }
            else
            {
                cent.Teleported = false;
            }

            CG_ResetPlayerEntity(cent);
        }

        public static void CG_SetEntitySoundPosition(CentityState cent)
        {
            // Nothing to transmit back to AudioSystem in this frame — the
            // 3-D pan is baked into each S_StartSound call via origin.
        }

        public static void CG_EntityEffects(CentityState cent)
        {
            EntityState es  = cent.CurrentState;
            Vector3     pos = EntityOrigin(es);

            if ((es.EFlags & GameConst.EF_SMOKING) != 0)
            {
                // Looping smoke — downstream particle system subscribes to OnEntityEvent.
                OnEntityEvent?.Invoke(es.Number, GameConst.EF_SMOKING, pos);
            }

            if ((es.EFlags & GameConst.EF_SMOKING_CHAR) != 0)
            {
                OnEntityEvent?.Invoke(es.Number, GameConst.EF_SMOKING_CHAR, pos);
            }

            // EF_FIRE / EF_SHAKE / EF_ONFIRE are not in GameConst for this port;
            // forward the raw flag value so particle/FX systems can handle them.
            int fireShakeFlags = es.EFlags & ~(
                GameConst.EF_SMOKING | GameConst.EF_SMOKING_CHAR |
                GameConst.EF_DEAD    | GameConst.EF_NODRAW);
            if (fireShakeFlags != 0)
                OnEntityEvent?.Invoke(es.Number, fireShakeFlags, pos);
        }

        public static void CG_General(CentityState cent)
        {
            if ((cent.CurrentState.EFlags & GameConst.EF_NODRAW) != 0)
                return;

            CG_EntityEffects(cent);
        }

        public static void CG_Missile(CentityState cent)
        {
            if ((cent.CurrentState.EFlags & GameConst.EF_NODRAW) != 0)
                return;

            CG_InterpolateEntityPosition(cent);
            CG_EntityEffects(cent);
        }

        public static void CG_Mover(CentityState cent)
        {
            CG_InterpolateEntityPosition(cent);
        }

        public static void CG_Beam(CentityState cent)
        {
            // Beam endpoints are Origin/Origin2; downstream renderer reads LerpOrigin.
            cent.LerpOrigin = EntityOrigin(cent.CurrentState);
        }

        public static void CG_Portal(CentityState cent)
        {
            cent.LerpOrigin = EntityOrigin(cent.CurrentState);
        }

        public static void CG_Speaker(CentityState cent)
        {
            if (cent.CurrentState.LoopSound == 0)
                return;

            // Activate the looping sound on first appearance.
            if (!cent.EFActive)
            {
                cent.EFActive = true;
                AudioSystem.S_StartSound(
                    EntityOrigin(cent.CurrentState),
                    cent.CurrentState.Number,
                    0,
                    cent.CurrentState.LoopSound);
            }
        }

        public static void CG_AddCEntity(CentityState cent)
        {
            CG_CheckEvents(cent);

            EntityState es = cent.CurrentState;
            var etype = (EntityType)es.EType;

            switch (etype)
            {
                case EntityType.Player:
                    CG_Player(cent);
                    break;
                case EntityType.Missile:
                case EntityType.FlamethrowerChunk:
                case EntityType.FireColumn:
                case EntityType.FireColumnSmoke:
                    CG_Missile(cent);
                    break;
                case EntityType.Mover:
                case EntityType.MoverScaled:
                    CG_Mover(cent);
                    break;
                case EntityType.Beam:
                case EntityType.Beam2:
                    CG_Beam(cent);
                    break;
                case EntityType.Portal:
                    CG_Portal(cent);
                    break;
                case EntityType.Speaker:
                    CG_Speaker(cent);
                    break;
                default:
                    CG_General(cent);
                    break;
            }
        }

        public static void CG_AddPacketEntities()
        {
            ClientSnapshot snap = ClientMain.Cl.Snap;
            if (snap == null || !snap.Valid)
                return;

            int count = snap.NumEntities;
            EntityState[] parsePool = ClientMain.Cl.ParseEntities;
            int baseIdx = snap.ParseEntitiesNum & (ClientConst.MAX_PARSE_ENTITIES - 1);

            for (int i = 0; i < count; i++)
            {
                int idx = (baseIdx + i) & (ClientConst.MAX_PARSE_ENTITIES - 1);
                EntityState es = parsePool[idx];
                if (es == null || es.Number < 0 || es.Number >= GameConst.MAX_GENTITIES)
                    continue;

                CentityState cent = Entities[es.Number];
                cent.CurrentState.CopyFrom(es);
                cent.CurrentValid = true;
                CG_AddCEntity(cent);
            }
        }

        public static void CG_InterpolateEntityPosition(CentityState cent)
        {
            EntityState cs = cent.CurrentState;

            if (!cent.Interpolate)
            {
                cent.LerpOrigin = EntityOrigin(cs);
                cent.LerpAngles = ETtoUnity(cs.Angles0, cs.Angles1, cs.Angles2);
                return;
            }

            ClientSnapshot snap    = ClientMain.Cl.Snap;
            ClientSnapshot oldSnap = ClientMain.Cl.OldSnap;
            if (snap == null || oldSnap == null)
            {
                cent.LerpOrigin = EntityOrigin(cs);
                cent.LerpAngles = ETtoUnity(cs.Angles0, cs.Angles1, cs.Angles2);
                return;
            }

            float range = snap.ServerTime - oldSnap.ServerTime;
            float f     = (range > 0f)
                ? Mathf.Clamp01((ClientMain.Cl.ServerTime - oldSnap.ServerTime) / range)
                : 1f;

            Vector3 curPos  = EntityOrigin(cs);
            Vector3 prevPos = EntityOrigin(cent.NextState);
            cent.LerpOrigin = Vector3.Lerp(prevPos, curPos, f);

            Vector3 curAng  = ETtoUnity(cs.Angles0,            cs.Angles1,            cs.Angles2);
            Vector3 prevAng = ETtoUnity(cent.NextState.Angles0, cent.NextState.Angles1, cent.NextState.Angles2);
            cent.LerpAngles = Vector3.Lerp(prevAng, curAng, f);
        }

        // -----------------------------------------------------------------------
        // cg_players.c — player model and animation
        // -----------------------------------------------------------------------

        public static void CG_ResetPlayerEntity(CentityState cent)
        {
            cent.LerpFrame     = 0;
            cent.MuzzleFlashTime = 0;
            cent.LerpOrigin    = Vector3.zero;
            cent.LerpAngles    = Vector3.zero;
        }

        public static void CG_Player(CentityState cent)
        {
            if ((cent.CurrentState.EFlags & GameConst.EF_NODRAW) != 0)
                return;

            CG_InterpolateEntityPosition(cent);

            string legsClip  = "";
            string torsoClip = "";
            CG_PlayerAnimation(cent, ref legsClip, ref torsoClip);

            if (!string.IsNullOrEmpty(legsClip))
                OnAnimationChange?.Invoke(cent.CurrentState.Number, legsClip);
            if (!string.IsNullOrEmpty(torsoClip) && torsoClip != legsClip)
                OnAnimationChange?.Invoke(cent.CurrentState.Number, torsoClip);

            CG_AddPlayerWeapon(cent);
            CG_PlayerShadow(cent);
            CG_EntityEffects(cent);

            OnPlayerUpdated?.Invoke(cent.CurrentState.Number);
        }

        public static void CG_PlayerAngles(CentityState cent,
            out Vector3 legs, out Vector3 torso, out Vector3 head)
        {
            EntityState es = cent.CurrentState;

            // Yaw from entity angles; pitch/roll drive head and torso separation.
            float yaw   = es.Angles1;
            float pitch = es.Angles0;

            legs  = new Vector3(0f,        yaw, 0f);
            torso = new Vector3(pitch * 0.5f, yaw, 0f);
            head  = new Vector3(pitch,     yaw, 0f);
        }

        public static void CG_PlayerAnimation(CentityState cent,
            ref string legsClip, ref string torsoClip)
        {
            EntityState es = cent.CurrentState;

            // Derive a lightweight AnimMoveType from entity flags for the
            // animation script evaluator.
            AnimMoveType moveType = AnimMoveType.Idle;
            if ((es.EFlags & GameConst.EF_DEAD) != 0)
            {
                moveType = AnimMoveType.Fallen;
            }
            else if ((es.EFlags & GameConst.EF_PRONE) != 0)
            {
                moveType = ((es.EFlags & GameConst.EF_PRONE_MOVING) != 0)
                    ? AnimMoveType.Prone
                    : AnimMoveType.ProneIdle;
            }
            else if ((es.EFlags & GameConst.EF_CROUCHING) != 0)
            {
                moveType = AnimMoveType.IdleCr;
            }

            // Forward to the data-driven animation script system.
            // Animations.BG_AnimScriptAnimation fires its own OnAnimMove events;
            // we only need a PlayerState when calling through the full path.
            // For now surface the computed move type so subscribers can act.
            legsClip  = moveType.ToString();
            torsoClip = moveType.ToString();
        }

        public static void CG_AddPlayerWeapon(CentityState cent)
        {
            // Weapon attachment is deferred to the renderer layer; raise an
            // entity event so weapon render components can update their model.
            int weapon = cent.CurrentState.Weapon;
            if (weapon > 0)
                OnEntityEvent?.Invoke(cent.CurrentState.Number, -(weapon), cent.LerpOrigin);
        }

        public static void CG_PlayerShadow(CentityState cent)
        {
            // Shadow projection is handled by a Unity projector component;
            // raise the event so it can follow the lerped origin.
            OnEntityEvent?.Invoke(cent.CurrentState.Number, 0, cent.LerpOrigin);
        }

        // -----------------------------------------------------------------------
        // cg_event.c — entity event processing
        // -----------------------------------------------------------------------

        public static void CG_EntityEvent(CentityState cent, Vector3 position)
        {
            // Lower 8 bits are the event id; the high bit is a no-aggro flag in ET source
            // (EVENT_NOAGGRO = 0x100 in EV_ field) but is not ported — use raw value.
            int eventType = cent.CurrentState.Event & 0xFF;

            switch (eventType)
            {
                case GameEvent.FOOTSTEP:
                case GameEvent.FOOTSTEP_METAL:
                case GameEvent.FOOTSTEP_WOOD:
                case GameEvent.FOOTSTEP_GRASS:
                case GameEvent.FOOTSTEP_GRAVEL:
                case GameEvent.FOOTSTEP_ROOF:
                case GameEvent.FOOTSTEP_SNOW:
                case GameEvent.FOOTSTEP_CARPET:
                    EV_Footstep(cent, position, eventType);
                    break;

                case GameEvent.JUMP:
                    EV_Jump(cent, position);
                    break;

                case GameEvent.ITEM_PICKUP:
                    EV_ItemPickup(cent, position);
                    break;

                case GameEvent.GLOBAL_ITEM_PICKUP:
                    EV_GlobalItemPickup(cent, position);
                    break;

                case GameEvent.MISSILE_HIT:
                    EV_MissileHit(cent, position, true);
                    break;

                case GameEvent.MISSILE_MISS:
                case GameEvent.MISSILE_MISS_METAL:
                    EV_MissileHit(cent, position, false);
                    break;

                case GameEvent.PAIN:
                case GameEvent.CROUCH_PAIN:
                    EV_Pain(cent, position);
                    break;

                case GameEvent.DEATH1:
                    EV_Death(cent, position, 0);
                    break;
                case GameEvent.DEATH2:
                    EV_Death(cent, position, 1);
                    break;
                case GameEvent.DEATH3:
                    EV_Death(cent, position, 2);
                    break;

                case GameEvent.BULLET_HIT_FLESH:
                    EV_BulletImpact(cent, position, true);
                    break;

                case GameEvent.BULLET_HIT_WALL:
                    EV_BulletImpact(cent, position, false);
                    break;

                case GameEvent.CHANGE_WEAPON:
                    EV_ChangeWeapon(cent, position);
                    break;

                case GameEvent.FIRE_WEAPON:
                    EV_FireWeapon(cent, position);
                    break;

                case GameEvent.SHAKE:
                    OnExplosion?.Invoke(cent.CurrentState.Number, position, true);
                    break;

                default:
                    break;
            }

            OnEntityEvent?.Invoke(cent.CurrentState.Number, eventType, position);
        }

        public static void CG_CheckEvents(CentityState cent)
        {
            EntityState cs = cent.CurrentState;
            Vector3     pos = cent.LerpOrigin != Vector3.zero
                ? cent.LerpOrigin
                : EntityOrigin(cs);

            // The entity carries up to 4 buffered events per snapshot.
            int seqDiff = cs.EventSequence - (cent.PreviousEventTime & 0xFF);
            if (seqDiff <= 0)
                return;

            // Clamp replay window to 4 (the Events array length).
            int start = (seqDiff > 4) ? seqDiff - 4 : 0;

            for (int i = start; i < seqDiff; i++)
            {
                int slot = (cs.EventSequence - seqDiff + i) & 3;
                cent.CurrentState.Event     = cs.Events[slot];
                cent.CurrentState.EventParm = cs.EventParms[slot];
                CG_EntityEvent(cent, pos);
            }

            cent.PreviousEventTime = cs.EventSequence;

            // Restore the primary event field for normal dispatch.
            cent.CurrentState.Event     = cs.Event;
            cent.CurrentState.EventParm = cs.EventParm;

            if (cs.Event != 0 && cs.Event != cent.PreviousEventTime)
                CG_EntityEvent(cent, pos);
        }

        // -----------------------------------------------------------------------
        // Individual event handlers
        // -----------------------------------------------------------------------

        private static void EV_Footstep(CentityState cent, Vector3 pos, int ev)
        {
            string snd = ev switch
            {
                GameEvent.FOOTSTEP_METAL  => "sound/player/default/step_metal.wav",
                GameEvent.FOOTSTEP_WOOD   => "sound/player/default/step_wood.wav",
                GameEvent.FOOTSTEP_GRASS  => "sound/player/default/step_grass.wav",
                GameEvent.FOOTSTEP_GRAVEL => "sound/player/default/step_gravel.wav",
                GameEvent.FOOTSTEP_ROOF   => "sound/player/default/step_roof.wav",
                GameEvent.FOOTSTEP_SNOW   => "sound/player/default/step_snow.wav",
                GameEvent.FOOTSTEP_CARPET => "sound/player/default/step_carpet.wav",
                _                         => "sound/player/default/step1.wav",
            };
            int sfx = AudioSystem.S_RegisterSound(snd, false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
        }

        private static void EV_Jump(CentityState cent, Vector3 pos)
        {
            int sfx = AudioSystem.S_RegisterSound(AudioSystem.SND_JUMP, false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
        }

        private static void EV_ItemPickup(CentityState cent, Vector3 pos)
        {
            int sfx = AudioSystem.S_RegisterSound("sound/misc/pickup.wav", false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
        }

        private static void EV_GlobalItemPickup(CentityState cent, Vector3 pos)
        {
            int sfx = AudioSystem.S_RegisterSound("sound/misc/pickup.wav", false);
            AudioSystem.S_StartLocalSound(sfx, 0);
        }

        // hit==true  → missile impacted a surface/entity; false → missed (near miss).
        private static void EV_MissileHit(CentityState cent, Vector3 pos, bool hit)
        {
            string snd = hit
                ? "sound/weapons/rocket/rocklx1a.wav"
                : "sound/weapons/rocket/rockfly.wav";
            int sfx = AudioSystem.S_RegisterSound(snd, false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);

            bool isLarge = (cent.CurrentState.Weapon >= 4); // rockets, mortars, etc.
            OnExplosion?.Invoke(cent.CurrentState.Number, pos, isLarge);
        }

        private static void EV_Pain(CentityState cent, Vector3 pos)
        {
            int health = cent.CurrentState.EventParm;
            string snd = (health < 25)
                ? AudioSystem.SND_PAIN_25
                : AudioSystem.SND_PAIN_50;
            int sfx = AudioSystem.S_RegisterSound(snd, false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
        }

        private static void EV_Death(CentityState cent, Vector3 pos, int deathAnim)
        {
            string snd = deathAnim switch
            {
                1 => "sound/player/default/death2.wav",
                2 => "sound/player/default/death3.wav",
                _ => AudioSystem.SND_DEATH,
            };
            int sfx = AudioSystem.S_RegisterSound(snd, false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
            OnAnimationChange?.Invoke(cent.CurrentState.Number, $"death{deathAnim + 1}");
        }

        private static void EV_BulletImpact(CentityState cent, Vector3 pos, bool flesh)
        {
            string snd = flesh
                ? "sound/weapons/bullet/bullethit_flesh.wav"
                : "sound/weapons/bullet/bullethit_wall.wav";
            int sfx = AudioSystem.S_RegisterSound(snd, false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
            OnEntityEvent?.Invoke(cent.CurrentState.Number,
                flesh ? GameEvent.BULLET_HIT_FLESH : GameEvent.BULLET_HIT_WALL, pos);
        }

        private static void EV_Explosion(CentityState cent, Vector3 pos)
        {
            int sfx = AudioSystem.S_RegisterSound("sound/weapons/explode/expl.wav", false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
            OnExplosion?.Invoke(cent.CurrentState.Number, pos, true);
        }

        private static void EV_ChangeWeapon(CentityState cent, Vector3 pos)
        {
            int sfx = AudioSystem.S_RegisterSound("sound/weapons/change.wav", false);
            AudioSystem.S_StartSound(pos, cent.CurrentState.Number, 0, sfx);
        }

        private static void EV_FireWeapon(CentityState cent, Vector3 pos)
        {
            cent.MuzzleFlashTime = ClientMain.Cl.ServerTime;
            // Weapon-specific fire sounds are resolved by the weapon system;
            // raise the event so WeaponSystem and muzzle flash components respond.
            OnEntityEvent?.Invoke(cent.CurrentState.Number, GameEvent.FIRE_WEAPON, pos);
        }
    }
}
