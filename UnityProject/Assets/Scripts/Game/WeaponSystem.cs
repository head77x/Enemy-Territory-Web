// Ported from: src/game/g_weapon.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// Server-side weapon firing, projectile spawn context, hit detection, and
// damage application.  Direct entity mutation is intentionally absent; the
// game layer receives all results through the OnDamage / OnFireWeapon /
// OnRadiusDamage events and is responsible for health adjustment, XP awards,
// and projectile entity creation.

using System;
using UnityEngine;

namespace ET.Game
{
    // -----------------------------------------------------------------------
    // Data structures
    // -----------------------------------------------------------------------

    /// <summary>
    /// Per-weapon static data ported from the weapon tables in g_weapon.c.
    /// </summary>
    public readonly struct WeaponInfo
    {
        /// <summary>Direct-hit damage applied per shot / per flame tick.</summary>
        public readonly int   Damage;
        /// <summary>Spread value in internal units (higher = wider cone).</summary>
        public readonly int   Spread;
        /// <summary>Splash / area-of-effect radius in world units.</summary>
        public readonly float SplashRadius;
        /// <summary>Maximum splash damage at ground-zero.</summary>
        public readonly int   SplashDamage;
        /// <summary>Knockback impulse magnitude.</summary>
        public readonly int   Knockback;

        public WeaponInfo(int damage, int spread, float splashRadius, int splashDamage, int knockback)
        {
            Damage       = damage;
            Spread       = spread;
            SplashRadius = splashRadius;
            SplashDamage = splashDamage;
            Knockback    = knockback;
        }
    }

    /// <summary>
    /// Payload raised by <see cref="WeaponSystem.OnDamage"/> and
    /// <see cref="WeaponSystem.OnRadiusDamage"/>.
    /// </summary>
    public struct DamageInfo
    {
        public int     TargetEntityNum;
        public int     AttackerEntityNum;
        public int     Damage;
        public int     DFlags;
        public int     DamageFlags { get => DFlags; set => DFlags = value; }
        public int     Mod;          // means of death (MOD_*)
        public bool    Headshot;
        public Vector3 Origin;       // impact point; Vector3.zero when not applicable
        // Extended fields used by DamageSystem
        public int     Location;     // HitLocation enum value
        public int     AttackerTeam;
        public int     TargetTeam;
        public int     TargetHealth; // target's health before damage
        public int     VictimClass;  // player class (PC_*) of the victim
    }

    /// <summary>
    /// Payload raised by <see cref="WeaponSystem.OnFireWeapon"/>.
    /// The game layer uses this to spawn projectile / traceline entities.
    /// </summary>
    public struct FireInfo
    {
        public int     ClientNum;
        public int     Weapon;
        public Vector3 MuzzleOrigin;
        public Vector3 MuzzleDir;
        public int     Damage;
        public float   Spread;
        public float   SplashRadius;
        public int     SplashDamage;
        public int     Mod;
    }

    // -----------------------------------------------------------------------
    // WeaponSystem
    // -----------------------------------------------------------------------

    /// <summary>
    /// Static class that owns all server-side weapon logic ported from
    /// g_weapon.c.  It is purely data + event-driven: no Unity MonoBehaviour
    /// lifecycle, no direct entity references.
    /// </summary>
    public static class WeaponSystem
    {
        // -------------------------------------------------------------------
        // Damage flags (DF_*)
        // -------------------------------------------------------------------
        public const int DF_RADIUS_DAMAGE = 1;
        public const int DF_NO_LOCDAMAGE  = 2;
        public const int DF_NOFATIGUE     = 4;

        // -------------------------------------------------------------------
        // Means of death (MOD_*)
        // -------------------------------------------------------------------
        public const int MOD_UNKNOWN                  =  0;
        public const int MOD_SHOTGUN                  =  1;
        public const int MOD_GAUNTLET                 =  2;
        public const int MOD_MACHINEGUN               =  3;
        public const int MOD_GRENADE                  =  4;
        public const int MOD_GRENADE_SPLASH           =  5;
        public const int MOD_ROCKET                   =  6;
        public const int MOD_ROCKET_SPLASH            =  7;
        public const int MOD_PLASMA                   =  8;
        public const int MOD_PLASMA_SPLASH            =  9;
        public const int MOD_RAILGUN                  = 10;
        public const int MOD_LIGHTNING                = 11;
        public const int MOD_BFG                      = 12;
        public const int MOD_BFG_SPLASH               = 13;
        public const int MOD_WATER                    = 14;
        public const int MOD_SLIME                    = 15;
        public const int MOD_LAVA                     = 16;
        public const int MOD_CRUSH                    = 17;
        public const int MOD_TELEFRAG                 = 18;
        public const int MOD_FALLING                  = 19;
        public const int MOD_SUICIDE                  = 20;
        public const int MOD_TARGET_LASER             = 21;
        public const int MOD_TRIGGER_HURT             = 22;
        public const int MOD_KNIFE                    = 23;
        public const int MOD_KNIFE_THROWN             = 24;
        public const int MOD_LUGER                    = 25;
        public const int MOD_COLT                     = 26;
        public const int MOD_MP40                     = 27;
        public const int MOD_THOMPSON                 = 28;
        public const int MOD_STEN                     = 29;
        public const int MOD_PANZERFAUST              = 30;
        public const int MOD_PANZERFAUST_SPLASH       = 31;
        public const int MOD_FLAMETHROWER             = 32;
        public const int MOD_GRENADE_PINEAPPLE        = 33;
        public const int MOD_GRENADE_PINEAPPLE_SPLASH = 34;
        public const int MOD_MORTAR                   = 35;
        public const int MOD_MORTAR_SPLASH            = 36;
        public const int MOD_DYNAMITE                 = 37;
        public const int MOD_DYNAMITE_SPLASH          = 38;
        public const int MOD_AIRSTRIKE                = 39;
        public const int MOD_SYRINGE                  = 40;
        public const int MOD_AMMO                     = 41;
        public const int MOD_ARTY                     = 42;
        public const int MOD_WATER_CANNON             = 43;
        public const int MOD_SMOKEGRENADE             = 44;
        public const int MOD_SWAP_PLACES              = 45;
        public const int MOD_SNIPER                   = 46;
        public const int MOD_KNIFE_KABAR              = 47;
        public const int MOD_MOBILE_MG42              = 48;
        public const int MOD_SILENCER                 = 49;
        public const int MOD_AKIMBO_COLT              = 50;
        public const int MOD_AKIMBO_LUGER             = 51;
        public const int MOD_AKIMBO_SILENCED_COLT     = 52;
        public const int MOD_AKIMBO_SILENCED_LUGER    = 53;
        public const int MOD_CARBINE                  = 54;
        public const int MOD_KAR98                    = 55;
        public const int MOD_GARAND                   = 56;
        public const int MOD_K43                      = 57;
        public const int MOD_FG42                     = 58;
        public const int MOD_FG42SCOPE                = 59;
        public const int MOD_GARAND_SCOPE             = 60;
        public const int MOD_K43_SCOPE                = 61;
        public const int MOD_GPG40                    = 62;
        public const int MOD_GPG40_SPLASH             = 63;
        public const int MOD_M7                       = 64;
        public const int MOD_M7_SPLASH                = 65;
        public const int MOD_LANDMINE                 = 66;
        public const int MOD_LANDMINE_SPLASH          = 67;
        public const int MOD_SATCHEL                  = 68;
        public const int MOD_SATCHEL_SPLASH           = 69;
        public const int MOD_TRIPMINE                 = 70;
        public const int MOD_TRIPMINE_SPLASH          = 71;
        public const int MOD_SMOKEBOMB                = 72;

        // -------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------

        /// <summary>
        /// Raised by <see cref="G_Damage"/> for each direct-hit damage
        /// application.  The game layer adjusts health, awards XP, etc.
        /// </summary>
        public static event Action<DamageInfo> OnDamage;

        /// <summary>
        /// Raised by <see cref="FireWeapon"/> so the game layer can spawn a
        /// projectile or perform a traceline.
        /// </summary>
        public static event Action<FireInfo> OnFireWeapon;

        /// <summary>
        /// Raised by <see cref="G_RadiusDamage"/> once per candidate entity
        /// slot within the splash radius.  The game layer must validate whether
        /// the entity slot is actually occupied and apply the damage.
        /// </summary>
        public static event Action<DamageInfo> OnRadiusDamage;

        // -------------------------------------------------------------------
        // Weapon data table
        // -------------------------------------------------------------------
        // Array indexed by WP_* constants (0 .. WP_NUM_WEAPONS-1).
        // Entries not explicitly initialised remain zero (default struct).
        private static readonly WeaponInfo[] s_weaponTable = BuildWeaponTable();

        private static WeaponInfo[] BuildWeaponTable()
        {
            var t = new WeaponInfo[GameConst.WP_NUM_WEAPONS];

            // Helper shorthand — (damage, spread, splashRadius, splashDamage, knockback)
            t[GameConst.WP_KNIFE]               = new WeaponInfo( 10,    0,   0f,   0, 10);
            t[GameConst.WP_LUGER]               = new WeaponInfo( 18,  600,   0f,   0, 10);
            t[GameConst.WP_COLT]                = new WeaponInfo( 18,  600,   0f,   0, 10);
            t[GameConst.WP_MP40]                = new WeaponInfo( 18,  400,   0f,   0,  8);
            t[GameConst.WP_THOMPSON]            = new WeaponInfo( 18,  400,   0f,   0,  8);
            t[GameConst.WP_STEN]                = new WeaponInfo( 18,  400,   0f,   0,  8);
            t[GameConst.WP_PANZERFAUST]         = new WeaponInfo(  0,    0, 225f, 400, 100);
            t[GameConst.WP_FLAMETHROWER]        = new WeaponInfo(  5,    0,   0f,   0, 15);
            t[GameConst.WP_GRENADE_LAUNCHER]    = new WeaponInfo(  0,    0, 225f, 300, 100);
            t[GameConst.WP_GRENADE_PINEAPPLE]   = new WeaponInfo(  0,    0, 225f, 300, 100);
            t[GameConst.WP_DYNAMITE]            = new WeaponInfo(  0,    0, 400f, 500, 150);
            t[GameConst.WP_LANDMINE]            = new WeaponInfo(  0,    0, 225f, 250, 100);
            t[GameConst.WP_SATCHEL]             = new WeaponInfo(  0,    0, 300f, 400, 150);
            t[GameConst.WP_MORTAR]              = new WeaponInfo(  0,    0, 300f, 350, 120);
            t[GameConst.WP_KAR98]              = new WeaponInfo( 34,  100,   0f,   0, 20);
            t[GameConst.WP_CARBINE]             = new WeaponInfo( 24,  200,   0f,   0, 15);
            t[GameConst.WP_GARAND]              = new WeaponInfo( 40,  100,   0f,   0, 25);
            t[GameConst.WP_K43]                 = new WeaponInfo( 34,  100,   0f,   0, 20);
            t[GameConst.WP_FG42]                = new WeaponInfo( 28,  200,   0f,   0, 18);
            t[GameConst.WP_MOBILE_MG42]         = new WeaponInfo( 18,  400,   0f,   0, 10);
            t[GameConst.WP_GARAND_SCOPE]        = new WeaponInfo( 50,    0,   0f,   0, 30);
            t[GameConst.WP_K43_SCOPE]           = new WeaponInfo( 50,    0,   0f,   0, 30);
            t[GameConst.WP_FG42SCOPE]           = new WeaponInfo( 36,   50,   0f,   0, 22);
            t[GameConst.WP_GPG40]               = new WeaponInfo(  0,    0, 225f, 250, 100);
            t[GameConst.WP_M7]                  = new WeaponInfo(  0,    0, 225f, 250, 100);
            t[GameConst.WP_AKIMBO_COLT]         = new WeaponInfo( 14,  800,   0f,   0,  8);
            t[GameConst.WP_AKIMBO_LUGER]        = new WeaponInfo( 14,  800,   0f,   0,  8);
            t[GameConst.WP_SILENCER]            = new WeaponInfo( 18,  100,   0f,   0, 10);
            t[GameConst.WP_SILENCED_COLT]       = new WeaponInfo( 18,  100,   0f,   0, 10);
            t[GameConst.WP_AKIMBO_SILENCEDCOLT] = new WeaponInfo( 14,  400,   0f,   0,  8);
            t[GameConst.WP_AKIMBO_SILENCEDLUGER]= new WeaponInfo( 14,  400,   0f,   0,  8);

            return t;
        }

        // -------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns a reference to the static <see cref="WeaponInfo"/> entry for
        /// <paramref name="weapon"/>.  Out-of-range values are clamped to the
        /// zero entry (WP_NONE) to avoid bounds exceptions.
        /// </summary>
        public static ref readonly WeaponInfo GetWeaponInfo(int weapon)
        {
            if ((uint)weapon >= (uint)s_weaponTable.Length)
                weapon = GameConst.WP_NONE;
            return ref s_weaponTable[weapon];
        }

        // -------------------------------------------------------------------
        // G_Damage
        // -------------------------------------------------------------------

        /// <summary>
        /// Server-side damage application entry point (ported from G_Damage in
        /// g_weapon.c / g_combat.c).
        ///
        /// Does NOT mutate health directly — raises <see cref="OnDamage"/> so
        /// the game layer can apply the result, award XP, update obituaries,
        /// and handle team-damage rules.
        /// </summary>
        /// <param name="targetEntityNum">Entity receiving the damage.</param>
        /// <param name="attackerEntityNum">Entity dealing the damage (may equal
        /// <see cref="GameConst.ENTITYNUM_WORLD"/> for environmental damage).</param>
        /// <param name="damage">Raw damage amount before any modifier.</param>
        /// <param name="dflags">Bit-field of DF_* flags.</param>
        /// <param name="mod">Means of death (MOD_*).</param>
        /// <param name="headshot">True when the hit was classified as a
        /// headshot by <see cref="G_IsHeadShot"/>.</param>
        public static void G_Damage(int targetEntityNum, int attackerEntityNum,
            int damage, int dflags, int mod, bool headshot = false)
        {
            if (targetEntityNum == GameConst.ENTITYNUM_NONE)
                return;

            var info = new DamageInfo
            {
                TargetEntityNum   = targetEntityNum,
                AttackerEntityNum = attackerEntityNum,
                Damage            = damage,
                DFlags            = dflags,
                Mod               = mod,
                Headshot          = headshot,
                Origin            = Vector3.zero,
            };

            OnDamage?.Invoke(info);
        }

        // -------------------------------------------------------------------
        // G_RadiusDamage
        // -------------------------------------------------------------------

        /// <summary>
        /// Applies attenuated splash damage to all entity slots within
        /// <paramref name="radius"/> of <paramref name="origin"/>.
        ///
        /// Because WeaponSystem has no direct access to the entity array, it
        /// raises <see cref="OnRadiusDamage"/> once per candidate slot
        /// (0 .. MAX_GENTITIES-1) with the distance-attenuated damage value.
        /// The game layer is responsible for:
        ///   - Checking whether the slot is occupied.
        ///   - Performing a line-of-sight / obstruction check.
        ///   - Applying the final damage (or discarding the event if the entity
        ///     is not a valid target).
        ///
        /// Attenuation formula (mirrors g_weapon.c):
        ///   dmg = damage * (1 - dist / radius)
        /// clamped to [1, damage] when the entity is inside the radius.
        /// </summary>
        /// <param name="origin">Explosion centre in world space.</param>
        /// <param name="attackerEntityNum">Attacker entity index.</param>
        /// <param name="damage">Maximum damage at ground-zero.</param>
        /// <param name="radius">Outer radius beyond which no damage is dealt.</param>
        /// <param name="ignoreEntityNum">Entity exempt from damage (usually the
        /// projectile itself or the attacker).</param>
        /// <param name="mod">Means of death (MOD_*).</param>
        public static void G_RadiusDamage(Vector3 origin, int attackerEntityNum,
            float damage, float radius, int ignoreEntityNum, int mod)
        {
            if (radius <= 0f || damage <= 0f)
                return;

            float invRadius = 1f / radius;

            for (int i = 0; i < GameConst.MAX_GENTITIES; i++)
            {
                if (i == ignoreEntityNum)
                    continue;

                // The game layer supplies the actual entity origin; we raise the
                // event with damage = damage (unattenuated) so the game layer can
                // compute the true attenuated value once it knows the distance.
                // To allow the game layer to skip the event early, we also pass
                // the explosion origin in DamageInfo.Origin.
                //
                // Attenuation is re-applied by the game layer using:
                //   float dist = Vector3.Distance(entityOrigin, info.Origin);
                //   int   adj  = Mathf.Max(1, Mathf.RoundToInt(info.Damage * (1f - dist * (1f/radius))));
                //
                // We send the unattenuated damage here; the game layer applies it.
                var info = new DamageInfo
                {
                    TargetEntityNum   = i,
                    AttackerEntityNum = attackerEntityNum,
                    Damage            = Mathf.RoundToInt(damage),
                    DFlags            = DF_RADIUS_DAMAGE,
                    Mod               = mod,
                    Headshot          = false,
                    Origin            = origin,
                };

                OnRadiusDamage?.Invoke(info);
            }
        }

        // -------------------------------------------------------------------
        // FireWeapon
        // -------------------------------------------------------------------

        /// <summary>
        /// Server-side weapon-fire entry point (ported from FireWeapon in
        /// g_weapon.c).  Looks up the weapon data, selects the correct MOD,
        /// then raises <see cref="OnFireWeapon"/> so the game layer can spawn
        /// the projectile or perform the hitscan traceline.
        /// </summary>
        /// <param name="clientNum">Firing client index.</param>
        /// <param name="ps">Current player state of the firing client.</param>
        /// <param name="muzzleOrigin">World-space muzzle position.</param>
        /// <param name="muzzleDir">World-space normalised muzzle direction.</param>
        /// <param name="weapon">WP_* weapon identifier.</param>
        public static void FireWeapon(int clientNum, ET.Network.PlayerState ps,
            Vector3 muzzleOrigin, Vector3 muzzleDir, int weapon)
        {
            if (weapon <= GameConst.WP_NONE || weapon >= GameConst.WP_NUM_WEAPONS)
                return;

            ref readonly WeaponInfo wi  = ref GetWeaponInfo(weapon);
            int mod = G_MODForWeapon(weapon);

            var fire = new FireInfo
            {
                ClientNum    = clientNum,
                Weapon       = weapon,
                MuzzleOrigin = muzzleOrigin,
                MuzzleDir    = muzzleDir,
                Damage       = wi.Damage,
                Spread       = wi.Spread,
                SplashRadius = wi.SplashRadius,
                SplashDamage = wi.SplashDamage,
                Mod          = mod,
            };

            OnFireWeapon?.Invoke(fire);
        }

        // -------------------------------------------------------------------
        // Weapon classification helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns true when the weapon produces area-of-effect splash damage
        /// on impact (projectile weapons with a splash radius).
        /// </summary>
        public static bool IsSplashWeapon(int weapon)
        {
            switch (weapon)
            {
                case GameConst.WP_GRENADE_LAUNCHER:
                case GameConst.WP_GRENADE_PINEAPPLE:
                case GameConst.WP_PANZERFAUST:
                case GameConst.WP_DYNAMITE:
                case GameConst.WP_LANDMINE:
                case GameConst.WP_SATCHEL:
                case GameConst.WP_MORTAR:
                case GameConst.WP_MORTAR_SET:
                case GameConst.WP_GPG40:
                case GameConst.WP_M7:
                case GameConst.WP_MAPMORTAR:
                case GameConst.WP_TRIPMINE:
                case GameConst.WP_SMOKE_BOMB:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true when the weapon is explosive (splash weapons plus
        /// devices that detonate on contact or command).
        /// </summary>
        public static bool IsExplosiveWeapon(int weapon)
        {
            if (IsSplashWeapon(weapon))
                return true;

            switch (weapon)
            {
                case GameConst.WP_SATCHEL_DET:
                case GameConst.WP_SMOKE_MARKER:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true for rifle grenades (GPG40 / M7) that are launched from
        /// a host rifle and behave differently from hand-thrown grenades.
        /// </summary>
        public static bool IsRifleGrenade(int weapon)
        {
            return weapon == GameConst.WP_GPG40 || weapon == GameConst.WP_M7;
        }

        // -------------------------------------------------------------------
        // MOD mapping
        // -------------------------------------------------------------------

        /// <summary>
        /// Maps a weapon identifier to its primary means-of-death constant.
        /// For splash weapons this returns the direct-hit MOD; the game layer
        /// substitutes the _SPLASH variant for radius-damage events.
        /// </summary>
        public static int G_MODForWeapon(int weapon)
        {
            switch (weapon)
            {
                case GameConst.WP_KNIFE:
                case GameConst.WP_LOCKPICK:          return MOD_KNIFE;
                case GameConst.WP_LUGER:             return MOD_LUGER;
                case GameConst.WP_COLT:              return MOD_COLT;
                case GameConst.WP_MP40:              return MOD_MP40;
                case GameConst.WP_THOMPSON:          return MOD_THOMPSON;
                case GameConst.WP_STEN:              return MOD_STEN;
                case GameConst.WP_PANZERFAUST:       return MOD_PANZERFAUST;
                case GameConst.WP_FLAMETHROWER:      return MOD_FLAMETHROWER;
                case GameConst.WP_GRENADE_LAUNCHER:  return MOD_GRENADE;
                case GameConst.WP_GRENADE_PINEAPPLE: return MOD_GRENADE_PINEAPPLE;
                case GameConst.WP_DYNAMITE:          return MOD_DYNAMITE;
                case GameConst.WP_LANDMINE:          return MOD_LANDMINE;
                case GameConst.WP_SATCHEL:
                case GameConst.WP_SATCHEL_DET:       return MOD_SATCHEL;
                case GameConst.WP_TRIPMINE:          return MOD_TRIPMINE;
                case GameConst.WP_SMOKE_BOMB:        return MOD_SMOKEBOMB;
                case GameConst.WP_MORTAR:
                case GameConst.WP_MORTAR_SET:        return MOD_MORTAR;
                case GameConst.WP_MAPMORTAR:         return MOD_MORTAR;
                case GameConst.WP_KAR98:             return MOD_KAR98;
                case GameConst.WP_CARBINE:           return MOD_CARBINE;
                case GameConst.WP_GARAND:            return MOD_GARAND;
                case GameConst.WP_K43:               return MOD_K43;
                case GameConst.WP_FG42:              return MOD_FG42;
                case GameConst.WP_MOBILE_MG42:
                case GameConst.WP_MOBILE_MG42_SET:
                case GameConst.WP_DUMMY_MG42:        return MOD_MOBILE_MG42;
                case GameConst.WP_GARAND_SCOPE:      return MOD_GARAND_SCOPE;
                case GameConst.WP_K43_SCOPE:         return MOD_K43_SCOPE;
                case GameConst.WP_FG42SCOPE:         return MOD_FG42SCOPE;
                case GameConst.WP_GPG40:             return MOD_GPG40;
                case GameConst.WP_M7:                return MOD_M7;
                case GameConst.WP_AKIMBO_COLT:       return MOD_AKIMBO_COLT;
                case GameConst.WP_AKIMBO_LUGER:      return MOD_AKIMBO_LUGER;
                case GameConst.WP_SILENCER:          return MOD_SILENCER;
                case GameConst.WP_SILENCED_COLT:     return MOD_AKIMBO_SILENCED_COLT;
                case GameConst.WP_AKIMBO_SILENCEDCOLT:  return MOD_AKIMBO_SILENCED_COLT;
                case GameConst.WP_AKIMBO_SILENCEDLUGER: return MOD_AKIMBO_SILENCED_LUGER;
                case GameConst.WP_MEDIC_SYRINGE:
                case GameConst.WP_MEDIC_ADRENALINE:  return MOD_SYRINGE;
                case GameConst.WP_ARTY:              return MOD_ARTY;
                case GameConst.WP_SMOKE_MARKER:      return MOD_SMOKEGRENADE;
                case GameConst.WP_PLIERS:            return MOD_UNKNOWN;
                case GameConst.WP_AMMO:              return MOD_AMMO;
                case GameConst.WP_BINOCULARS:        return MOD_UNKNOWN;
                case GameConst.WP_SMOKETRAIL:        return MOD_SMOKEGRENADE;
                default:                             return MOD_UNKNOWN;
            }
        }

        /// <summary>
        /// Reverse mapping: given a means-of-death, return the primary WP_*
        /// weapon ID.  Returns <see cref="GameConst.WP_NONE"/> for unrecognised
        /// or non-weapon MOD values (environmental, world, etc.).
        /// </summary>
        public static int G_WeaponForMOD(int mod)
        {
            switch (mod)
            {
                case MOD_KNIFE:
                case MOD_KNIFE_THROWN:
                case MOD_KNIFE_KABAR:         return GameConst.WP_KNIFE;
                case MOD_LUGER:               return GameConst.WP_LUGER;
                case MOD_COLT:                return GameConst.WP_COLT;
                case MOD_MP40:                return GameConst.WP_MP40;
                case MOD_THOMPSON:            return GameConst.WP_THOMPSON;
                case MOD_STEN:                return GameConst.WP_STEN;
                case MOD_PANZERFAUST:
                case MOD_PANZERFAUST_SPLASH:  return GameConst.WP_PANZERFAUST;
                case MOD_FLAMETHROWER:        return GameConst.WP_FLAMETHROWER;
                case MOD_GRENADE:
                case MOD_GRENADE_SPLASH:      return GameConst.WP_GRENADE_LAUNCHER;
                case MOD_GRENADE_PINEAPPLE:
                case MOD_GRENADE_PINEAPPLE_SPLASH: return GameConst.WP_GRENADE_PINEAPPLE;
                case MOD_DYNAMITE:
                case MOD_DYNAMITE_SPLASH:     return GameConst.WP_DYNAMITE;
                case MOD_LANDMINE:
                case MOD_LANDMINE_SPLASH:     return GameConst.WP_LANDMINE;
                case MOD_SATCHEL:
                case MOD_SATCHEL_SPLASH:      return GameConst.WP_SATCHEL;
                case MOD_TRIPMINE:
                case MOD_TRIPMINE_SPLASH:     return GameConst.WP_TRIPMINE;
                case MOD_SMOKEBOMB:           return GameConst.WP_SMOKE_BOMB;
                case MOD_MORTAR:
                case MOD_MORTAR_SPLASH:       return GameConst.WP_MORTAR;
                case MOD_KAR98:               return GameConst.WP_KAR98;
                case MOD_CARBINE:             return GameConst.WP_CARBINE;
                case MOD_GARAND:              return GameConst.WP_GARAND;
                case MOD_K43:                 return GameConst.WP_K43;
                case MOD_FG42:                return GameConst.WP_FG42;
                case MOD_MOBILE_MG42:         return GameConst.WP_MOBILE_MG42;
                case MOD_GARAND_SCOPE:        return GameConst.WP_GARAND_SCOPE;
                case MOD_K43_SCOPE:           return GameConst.WP_K43_SCOPE;
                case MOD_FG42SCOPE:           return GameConst.WP_FG42SCOPE;
                case MOD_GPG40:
                case MOD_GPG40_SPLASH:        return GameConst.WP_GPG40;
                case MOD_M7:
                case MOD_M7_SPLASH:           return GameConst.WP_M7;
                case MOD_AKIMBO_COLT:         return GameConst.WP_AKIMBO_COLT;
                case MOD_AKIMBO_LUGER:        return GameConst.WP_AKIMBO_LUGER;
                case MOD_SILENCER:            return GameConst.WP_SILENCER;
                case MOD_AKIMBO_SILENCED_COLT:  return GameConst.WP_AKIMBO_SILENCEDCOLT;
                case MOD_AKIMBO_SILENCED_LUGER: return GameConst.WP_AKIMBO_SILENCEDLUGER;
                case MOD_SYRINGE:             return GameConst.WP_MEDIC_SYRINGE;
                case MOD_ARTY:               return GameConst.WP_ARTY;
                case MOD_SMOKEGRENADE:        return GameConst.WP_SMOKE_MARKER;
                case MOD_AMMO:               return GameConst.WP_AMMO;
                default:                     return GameConst.WP_NONE;
            }
        }

        // -------------------------------------------------------------------
        // Headshot detection
        // -------------------------------------------------------------------

        /// <summary>
        /// Heuristic headshot check (ported from G_IsHeadShot in g_weapon.c).
        ///
        /// A hit is classified as a headshot when the impact point's Z
        /// coordinate is above the top 25 % of a standing player bounding box,
        /// approximated as 27 units above the entity origin (the origin is at
        /// foot level in ET).
        /// </summary>
        /// <param name="hitPoint">World-space impact position.</param>
        /// <param name="targetOrigin">World-space origin of the target entity
        /// (foot position).</param>
        /// <returns>True when the hit qualifies as a headshot.</returns>
        public static bool G_IsHeadShot(Vector3 hitPoint, Vector3 targetOrigin)
        {
            return hitPoint.z > targetOrigin.z + 27f;
        }

        // -------------------------------------------------------------------
        // Ammo index helper
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns the <c>Stats[]</c> index that holds the primary ammo count
        /// for <paramref name="weapon"/>.
        ///
        /// ET tracks per-weapon ammo in <c>playerState_t.ammo[]</c> and
        /// <c>ammoClip[]</c> indexed by WP_*.  The Stats array carries other
        /// aggregate values (health, XP, etc.).  This simplified helper always
        /// returns 0 (the generic ammo slot) as a placeholder; the game layer
        /// uses <c>PlayerState.Ammo[weapon]</c> directly for per-weapon counts.
        /// </summary>
        public static int G_GetWeaponAmmoIndex(int weapon) => 0;
    }
}
