// Ported from: src/game/bg_local.h, src/game/bg_public.h, src/game/q_shared.h
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

namespace ET.Game
{
    public static class GameConst
    {
        // -------------------------------------------------------
        // Physics
        // -------------------------------------------------------
        public const float OVERCLIP          = 1.001f;
        public const int   STEPSIZE          = 18;
        public const float JUMP_VELOCITY     = 270f;
        public const int   MAX_CLIP_PLANES   = 5;
        public const int   MAXTOUCH          = 32;
        public const float MIN_WALK_NORMAL   = 0.7f;
        public const int   TIMER_LAND        = 130;
        public const int   TIMER_GESTURE     = 34 * 66 + 50;
        public const int   DOUBLE_TAP_DELAY  = 400;
        public const float MAX_MG42_HEAT     = 1500f;

        // -------------------------------------------------------
        // Entities
        // -------------------------------------------------------
        public const int MAX_GENTITIES       = 1024;
        public const int ENTITYNUM_NONE      = MAX_GENTITIES - 1;   // 1023
        public const int ENTITYNUM_WORLD     = MAX_GENTITIES - 2;   // 1022
        public const int MAX_CLIENTS         = 64;

        // -------------------------------------------------------
        // View heights
        // -------------------------------------------------------
        public const int DEFAULT_VIEWHEIGHT  = 40;
        public const int CROUCH_VIEWHEIGHT   = 16;
        public const int DEAD_VIEWHEIGHT     = -16;
        public const int PRONE_VIEWHEIGHT    = -8;

        // -------------------------------------------------------
        // pmove->pm_type values
        // -------------------------------------------------------
        public const int PM_NORMAL      = 0;
        public const int PM_NOCLIP      = 1;
        public const int PM_SPECTATOR   = 2;
        public const int PM_DEAD        = 3;
        public const int PM_FREEZE      = 4;
        public const int PM_INTERMISSION = 5;

        // -------------------------------------------------------
        // pmove->pm_flags  (max 16 bits)
        // -------------------------------------------------------
        public const int PMF_DUCKED           = 1;
        public const int PMF_JUMP_HELD        = 2;
        public const int PMF_LADDER           = 4;
        public const int PMF_BACKWARDS_JUMP   = 8;
        public const int PMF_BACKWARDS_RUN    = 16;
        public const int PMF_TIME_LAND        = 32;
        public const int PMF_TIME_KNOCKBACK   = 64;
        public const int PMF_TIME_WATERJUMP   = 256;
        public const int PMF_RESPAWNED        = 512;
        public const int PMF_FLAILING         = 2048;
        public const int PMF_FOLLOW           = 4096;
        public const int PMF_TIME_LOAD        = 8192;
        public const int PMF_LIMBO            = 16384;
        public const int PMF_TIME_LOCKPLAYER  = 32768;
        public const int PMF_ALL_TIMES        = PMF_TIME_WATERJUMP | PMF_TIME_LAND
                                              | PMF_TIME_KNOCKBACK | PMF_TIME_LOCKPLAYER;

        // -------------------------------------------------------
        // Entity flags (EF_*)
        // -------------------------------------------------------
        public const int EF_DEAD            = 0x00000001;
        public const int EF_TICKING         = 0x00000002;
        public const int EF_TELEPORT_BIT    = 0x00000004;
        public const int EF_PLAYER_EVENT    = 0x00000008;
        public const int EF_CROUCHING       = 0x00000010;
        public const int EF_MG42_ACTIVE     = 0x00000020;
        public const int EF_NODRAW          = 0x00000040;
        public const int EF_FIRING          = 0x00000080;
        public const int EF_INHERITSHADER   = EF_FIRING;
        public const int EF_SPINNING        = 0x00000100;
        public const int EF_TALK            = 0x00000200;
        public const int EF_CONNECTION      = 0x00000400;
        public const int EF_VOTED           = 0x00000800;
        public const int EF_TAGCONNECT      = 0x00001000;
        public const int EF_MOUNTEDTANK     = EF_TAGCONNECT;
        public const int EF_HEADSHOT        = 0x00002000;
        public const int EF_SMOKING         = 0x00004000;
        public const int EF_SMOKING_CHAR    = 0x00008000;
        public const int EF_AAGUN_ACTIVE    = 0x00010000;
        public const int EF_PRONE           = 0x00080000;
        public const int EF_PRONE_MOVING    = 0x00100000;
        public const int EF_ZOOMING         = 0x00040000;
        public const int EF_BOUNCE          = 0x04000000;   // missiles
        public const int EF_BOUNCE_HALF     = 0x08000000;   // missiles
        public const int EF_BREATH          = EF_SPINNING;   // hijacks EF_SPINNING on character entities

        // -------------------------------------------------------
        // Stats indices (playerState_t.Stats[])
        // -------------------------------------------------------
        public const int STAT_HEALTH           = 0;
        public const int STAT_HOLDABLE_ITEM    = 1;
        public const int STAT_WPMODES          = 2;
        public const int STAT_ITEMS            = 3;
        public const int STAT_CLIENTS_READY    = 4;
        public const int STAT_PLAYER_CLASS     = 5;
        public const int STAT_CAPTUREDFLAG     = 6;
        public const int STAT_LIVESLEFT        = 7;
        public const int STAT_KEYS             = 9;
        public const int STAT_FRIENDLYFIRE     = 10;
        public const int STAT_XP               = 11;
        public const int STAT_RANK             = 12;
        public const int STAT_ENDMATCH         = 13;
        public const int STAT_OBJECTIVE        = 14;
        public const int STAT_RUNTIMER         = 15;
        public const int MAX_STATS             = 16;

        // -------------------------------------------------------
        // Persistant indices (playerState_t.Persistant[])
        // -------------------------------------------------------
        public const int PERS_SCORE           = 0;
        public const int PERS_HITS            = 1;
        public const int PERS_RANK            = 2;
        public const int PERS_TEAM            = 3;
        public const int PERS_SPAWN_COUNT     = 4;
        public const int PERS_PLAYEREVENTS    = 5;
        public const int PERS_ATTACKER        = 6;
        public const int PERS_KILLED          = 7;
        public const int PERS_IMPRESSIVE_COUNT= 8;
        public const int PERS_EXCELLENT_COUNT = 9;
        public const int PERS_GAUNTLET_FRAG_COUNT = 10;
        public const int PERS_HWEAPON_USE     = 11;
        public const int MAX_PERSISTANT       = 16;

        // -------------------------------------------------------
        // Weapon IDs (wp_t) — exact values from bg_public.h
        // WP_NUM_WEAPONS = 50 (WolfXP multiplayer)
        // -------------------------------------------------------
        public const int WP_NONE                  = 0;
        public const int WP_KNIFE                 = 1;
        public const int WP_LUGER                 = 2;
        public const int WP_MP40                  = 3;
        public const int WP_GRENADE_LAUNCHER      = 4;
        public const int WP_PANZERFAUST           = 5;
        public const int WP_FLAMETHROWER          = 6;
        public const int WP_COLT                  = 7;
        public const int WP_THOMPSON              = 8;
        public const int WP_GRENADE_PINEAPPLE     = 9;
        public const int WP_STEN                  = 10;
        public const int WP_MEDIC_SYRINGE         = 11;
        public const int WP_AMMO                  = 12;
        public const int WP_ARTY                  = 13;
        public const int WP_SILENCER              = 14;
        public const int WP_DYNAMITE              = 15;
        public const int WP_SMOKETRAIL            = 16;
        public const int WP_MAPMORTAR             = 17;
        public const int WP_MEDKIT                = 19;
        public const int WP_BINOCULARS            = 20;
        public const int WP_PLIERS                = 21;
        public const int WP_SMOKE_MARKER          = 22;
        public const int WP_KAR98                 = 23;
        public const int WP_CARBINE               = 24;
        public const int WP_GARAND                = 25;
        public const int WP_LANDMINE              = 26;
        public const int WP_SATCHEL               = 27;
        public const int WP_SATCHEL_DET           = 28;
        public const int WP_TRIPMINE              = 29;
        public const int WP_SMOKE_BOMB            = 30;
        public const int WP_MOBILE_MG42           = 31;
        public const int WP_K43                   = 32;
        public const int WP_FG42                  = 33;
        public const int WP_DUMMY_MG42            = 34;
        public const int WP_MORTAR                = 35;
        public const int WP_LOCKPICK              = 36;
        public const int WP_AKIMBO_COLT           = 37;
        public const int WP_AKIMBO_LUGER          = 38;
        public const int WP_GPG40                 = 39;
        public const int WP_M7                    = 40;
        public const int WP_SILENCED_COLT         = 41;
        public const int WP_GARAND_SCOPE          = 42;
        public const int WP_K43_SCOPE             = 43;
        public const int WP_FG42SCOPE             = 44;
        public const int WP_MORTAR_SET            = 45;
        public const int WP_MEDIC_ADRENALINE      = 46;
        public const int WP_AKIMBO_SILENCEDCOLT   = 47;
        public const int WP_AKIMBO_SILENCEDLUGER  = 48;
        public const int WP_MOBILE_MG42_SET       = 49;
        public const int WP_NUM_WEAPONS           = 50;
        public const int MAX_WEAPONS              = 64;

        // -------------------------------------------------------
        // Weapon states
        // -------------------------------------------------------
        public const int WEAPON_READY        = 0;
        public const int WEAPON_RAISING      = 1;
        public const int WEAPON_DROPPING     = 2;
        public const int WEAPON_FIRING       = 3;
        public const int WEAPON_FIRINGALT    = 4;
        public const int WEAPON_RECHAMBERING = 5;
        public const int WEAPON_RELOADING    = 6;

        // -------------------------------------------------------
        // Weapon animation indices
        // -------------------------------------------------------
        public const int WEAP_IDLE1          = 0;
        public const int WEAP_IDLE2          = 1;
        public const int WEAP_ATTACK1        = 2;
        public const int WEAP_ATTACK2        = 3;
        public const int WEAP_ATTACK_LASTSHOT= 4;
        public const int WEAP_DROP           = 5;
        public const int WEAP_RAISE          = 6;
        public const int WEAP_RELOAD1        = 7;
        public const int WEAP_RELOAD2        = 8;
        public const int WEAP_RELOAD3        = 9;
        public const int WEAP_ALTSWITCHFROM  = 10;
        public const int WEAP_ALTSWITCHTO    = 11;
        public const int WEAP_DROP2          = 12;

        // -------------------------------------------------------
        // Player class
        // -------------------------------------------------------
        public const int PC_SOLDIER          = 0;
        public const int PC_MEDIC            = 1;
        public const int PC_ENGINEER         = 2;
        public const int PC_FIELDOPS         = 3;
        public const int PC_COVERTOPS        = 4;

        // -------------------------------------------------------
        // Skill indices (skill[])
        // -------------------------------------------------------
        public const int SK_BATTLE_SENSE     = 0;
        public const int SK_ENGINEER         = 1;
        public const int SK_MEDIC            = 2;
        public const int SK_FIELDOPS         = 3;
        public const int SK_HEAVY_WEAPONS    = 3;  // same index as SK_FIELDOPS (class dependent)
        public const int SK_LIGHT_WEAPONS    = 4;
        public const int SK_SOLDIER          = 5;
        public const int SK_COVERTOPS        = 6;

        // -------------------------------------------------------
        // Anim
        // -------------------------------------------------------
        public const int ANIM_BITS           = 10;
        public const int ANIM_TOGGLEBIT      = 1 << (ANIM_BITS - 1);   // 512

        // -------------------------------------------------------
        // Inventory keys (INV_*)
        // -------------------------------------------------------
        public const int INV_BINOCS          = 1;

        // -------------------------------------------------------
        // Pmove frame counter
        // -------------------------------------------------------
        public const int PS_PMOVEFRAMECOUNTBITS = 6;

        // -------------------------------------------------------
        // Water levels
        // -------------------------------------------------------
        public const int WATERLEVEL_NONE     = 0;
        public const int WATERLEVEL_FEET     = 1;
        public const int WATERLEVEL_WAIST    = 2;
        public const int WATERLEVEL_HEAD     = 3;

        // -------------------------------------------------------
        // Game types
        // -------------------------------------------------------
        public const int GT_SINGLE_PLAYER    = 0;
        public const int GT_COOP             = 1;
        public const int GT_WOLF             = 2;
        public const int GT_WOLF_STOPWATCH   = 3;
        public const int GT_WOLF_CP          = 4;
        public const int GT_WOLF_LMS         = 5;
    }
}
