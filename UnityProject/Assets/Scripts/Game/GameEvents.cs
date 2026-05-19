// Ported from: src/game/bg_public.h  (entity_event_t)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

namespace ET.Game
{
    /// <summary>
    /// entity_event_t — events fired from pmove and game logic.
    /// These are queued into playerState_t.Events[] for client-side effects.
    /// </summary>
    public static class GameEvent
    {
        public const int NONE              = 0;
        public const int FOOTSTEP          = 1;
        public const int FOOTSTEP_METAL    = 2;
        public const int FOOTSTEP_WOOD     = 3;
        public const int FOOTSTEP_GRASS    = 4;
        public const int FOOTSTEP_GRAVEL   = 5;
        public const int FOOTSTEP_ROOF     = 6;
        public const int FOOTSTEP_SNOW     = 7;
        public const int FOOTSTEP_CARPET   = 8;
        public const int STEP_4            = 9;
        public const int STEP_8            = 10;
        public const int STEP_12           = 11;
        public const int STEP_16           = 12;
        public const int FALL_SHORT        = 13;
        public const int FALL_MEDIUM       = 14;
        public const int FALL_FAR          = 15;
        public const int FALL_NDIE         = 16;
        public const int FALL_DMG_10       = 17;
        public const int FALL_DMG_15       = 18;
        public const int FALL_DMG_25       = 19;
        public const int FALL_DMG_50       = 20;
        public const int JUMP              = 21;
        public const int WATER_TOUCH       = 22;
        public const int WATER_LEAVE       = 23;
        public const int WATER_UNDER       = 24;
        public const int WATER_CLEAR       = 25;
        public const int ITEM_PICKUP       = 26;
        public const int GLOBAL_ITEM_PICKUP= 27;
        public const int NOAMMO            = 28;
        public const int CHANGE_WEAPON     = 29;
        public const int FIRE_WEAPON       = 30;
        public const int ITEM_RESPAWN      = 31;
        public const int ITEM_POP          = 32;
        public const int PLAYER_TELEPORT_IN  = 33;
        public const int PLAYER_TELEPORT_OUT = 34;
        public const int GRENADE_BOUNCE    = 35;
        public const int GENERAL_SOUND    = 36;
        public const int GENERAL_SOUND_VOLUME = 37;
        public const int BULLET_HIT_FLESH  = 38;
        public const int BULLET_HIT_WALL   = 39;
        public const int MISSILE_HIT       = 40;
        public const int MISSILE_MISS      = 41;
        public const int MISSILE_MISS_METAL= 42;
        public const int RAILTRAIL         = 43;
        public const int SHOTGUN           = 44;
        public const int PAIN              = 48;
        public const int CROUCH_PAIN       = 49;
        public const int DEATH1            = 50;
        public const int DEATH2            = 51;
        public const int DEATH3            = 52;
        public const int OBITUARY          = 53;
        public const int POWERUP_QUAD      = 54;
        public const int POWERUP_BATTLESUIT= 55;
        public const int POWERUP_REGEN     = 56;
        public const int GIB_PLAYER        = 57;
        public const int SHAKE             = 58;
        public const int PRONE_TO_CROUCH   = 59;
    }
}
