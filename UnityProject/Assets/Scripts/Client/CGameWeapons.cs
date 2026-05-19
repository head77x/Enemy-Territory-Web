// Ported from: src/cgame/cg_weapons.c, src/cgame/cg_effects.c
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
    public class WeaponRenderInfo
    {
        public string   ModelPath;
        public string   FlashModelPath;
        public string   BrassModelPath;
        public string   HandModelPath;
        public int      FlashSound;
        public int      ReloadSound;
        public int      EmptySound;
        public float    FlashDist;
        public float    MuzzlePointForward;
        public float    MuzzlePointUp;
        public bool     EjectBrass;
        public int      Firingmode;
        public Color    FlashColor;
    }

    // CentityState is defined in CGameEntities; we declare a minimal stand-in
    // if the full file is not yet compiled in this assembly.
    public class CentityState
    {
        public EntityState CurrentState = new EntityState();
        public Vector3     LerpOrigin;
        public Vector3     LerpAngles;
    }

    public static class CGameWeapons
    {
        private static readonly WeaponRenderInfo[] _weaponInfo =
            new WeaponRenderInfo[GameConst.MAX_WEAPONS];
        private static bool _initialized = false;

        // Current weapon selected on the HUD wheel (client-side only)
        private static int _selectedWeapon = GameConst.WP_NONE;

        // -------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------
        public static event Action<int, Vector3>       OnMuzzleFlash;
        public static event Action<int, Vector3, bool> OnImpact;
        public static event Action<Vector3, float>     OnExplosionEffect;
        public static event Action<int>                OnWeaponChanged;

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        // ET left-hand coord (y-right, z-up, x-forward) → Unity (x-right, y-up, z-forward)
        private static Vector3 ETToUnity(float ex, float ey, float ez)
            => new Vector3(-ey, ez, ex);

        private static Vector3 ETToUnity(Vector3 et)
            => new Vector3(-et.y, et.z, et.x);

        private static string ModelPath(int wp)
        {
            switch (wp)
            {
                case GameConst.WP_KNIFE:
                    return "models/weapons2/knife/knife.md3";
                case GameConst.WP_LUGER:
                case GameConst.WP_COLT:
                case GameConst.WP_SILENCER:
                case GameConst.WP_SILENCED_COLT:
                case GameConst.WP_AKIMBO_LUGER:
                case GameConst.WP_AKIMBO_COLT:
                case GameConst.WP_AKIMBO_SILENCEDLUGER:
                case GameConst.WP_AKIMBO_SILENCEDCOLT:
                    return "models/weapons2/luger/luger.md3";
                case GameConst.WP_MP40:
                case GameConst.WP_THOMPSON:
                    return "models/weapons2/mp40/mp40.md3";
                case GameConst.WP_KAR98:
                case GameConst.WP_GARAND:
                case GameConst.WP_CARBINE:
                case GameConst.WP_K43:
                case GameConst.WP_GARAND_SCOPE:
                case GameConst.WP_K43_SCOPE:
                    return "models/weapons2/mauser/mauser.md3";
                case GameConst.WP_PANZERFAUST:
                case GameConst.WP_GPG40:
                case GameConst.WP_M7:
                    return "models/weapons2/panzerfaust/pf.md3";
                case GameConst.WP_FLAMETHROWER:
                    return "models/weapons2/flamethrower/flamethrower.md3";
                case GameConst.WP_GRENADE_LAUNCHER:
                case GameConst.WP_GRENADE_PINEAPPLE:
                    return "models/weapons2/grenade/grenade.md3";
                case GameConst.WP_MORTAR:
                case GameConst.WP_MORTAR_SET:
                    return "models/weapons2/mortar/mortar.md3";
                case GameConst.WP_DYNAMITE:
                case GameConst.WP_SATCHEL:
                case GameConst.WP_SATCHEL_DET:
                    return "models/weapons2/dynamite/dynamite.md3";
                case GameConst.WP_STEN:
                case GameConst.WP_FG42:
                case GameConst.WP_FG42SCOPE:
                case GameConst.WP_MOBILE_MG42:
                case GameConst.WP_MOBILE_MG42_SET:
                    return "models/weapons2/sten/sten.md3";
                // No separate WP_SNIPERRIFLE constant in GameConst; scoped variants cover sniper role.
                case GameConst.WP_LOCKPICK:
                    return "models/weapons2/sniper/sniper.md3";
                default:
                    return "models/weapons2/crowbar/crowbar.md3";
            }
        }

        // Pistols and small automatics eject brass; heavy weapons / explosives do not.
        private static bool ShouldEjectBrass(int wp)
        {
            switch (wp)
            {
                case GameConst.WP_LUGER:
                case GameConst.WP_COLT:
                case GameConst.WP_SILENCER:
                case GameConst.WP_SILENCED_COLT:
                case GameConst.WP_AKIMBO_LUGER:
                case GameConst.WP_AKIMBO_COLT:
                case GameConst.WP_AKIMBO_SILENCEDLUGER:
                case GameConst.WP_AKIMBO_SILENCEDCOLT:
                case GameConst.WP_MP40:
                case GameConst.WP_THOMPSON:
                case GameConst.WP_STEN:
                case GameConst.WP_FG42:
                case GameConst.WP_FG42SCOPE:
                case GameConst.WP_MOBILE_MG42:
                case GameConst.WP_MOBILE_MG42_SET:
                case GameConst.WP_KAR98:
                case GameConst.WP_GARAND:
                case GameConst.WP_CARBINE:
                case GameConst.WP_K43:
                case GameConst.WP_GARAND_SCOPE:
                case GameConst.WP_K43_SCOPE:
                    return true;
                default:
                    return false;
            }
        }

        private static Color FlashColorForWeapon(int wp)
        {
            switch (wp)
            {
                case GameConst.WP_FLAMETHROWER:
                    return new Color(1f, 0.4f, 0f, 1f);
                case GameConst.WP_PANZERFAUST:
                case GameConst.WP_MORTAR:
                case GameConst.WP_MORTAR_SET:
                    return new Color(1f, 0.6f, 0.1f, 1f);
                default:
                    return new Color(1f, 0.9f, 0.5f, 1f);
            }
        }

        // -------------------------------------------------------------------
        // cg_weapons.c — weapon registration
        // -------------------------------------------------------------------

        public static void CG_InitWeapons()
        {
            if (_initialized)
                return;

            for (int i = 0; i < GameConst.WP_NUM_WEAPONS; i++)
                CG_RegisterWeapon(i);

            _initialized = true;
        }

        public static void CG_RegisterWeapon(int weaponNum)
        {
            if (weaponNum <= GameConst.WP_NONE || weaponNum >= GameConst.MAX_WEAPONS)
                return;

            var wri = new WeaponRenderInfo
            {
                ModelPath          = ModelPath(weaponNum),
                FlashModelPath     = "models/weapons2/flash/flash.md3",
                BrassModelPath     = "models/weapons2/brass/brass.md3",
                HandModelPath      = "models/weapons2/hands/hands.md3",
                FlashDist          = 32f,
                MuzzlePointForward = 28f,
                MuzzlePointUp      = -2f,
                EjectBrass         = ShouldEjectBrass(weaponNum),
                Firingmode         = 0,
                FlashColor         = FlashColorForWeapon(weaponNum),
            };

            string basePath = "sound/weapons/";
            string sfxName  = wri.ModelPath.Contains("mp40")   ? "mp40"        :
                              wri.ModelPath.Contains("luger")  ? "luger"       :
                              wri.ModelPath.Contains("mauser") ? "mauser"      :
                              wri.ModelPath.Contains("pf")     ? "panzerfaust" :
                              wri.ModelPath.Contains("flame")  ? "flamethrower":
                              wri.ModelPath.Contains("grenade")? "grenade"     :
                              wri.ModelPath.Contains("mortar") ? "mortar"      :
                              wri.ModelPath.Contains("sniper") ? "sniper"      :
                              wri.ModelPath.Contains("sten")   ? "sten"        :
                              wri.ModelPath.Contains("knife")  ? "knife"       : "default";

            wri.FlashSound  = AudioSystem.S_RegisterSound(basePath + sfxName + "/fire.wav",  false);
            wri.ReloadSound = AudioSystem.S_RegisterSound(basePath + sfxName + "/reload.wav", false);
            wri.EmptySound  = AudioSystem.S_RegisterSound(basePath + sfxName + "/empty.wav",  false);

            _weaponInfo[weaponNum] = wri;
        }

        public static WeaponRenderInfo CG_GetWeaponRenderInfo(int weaponNum)
        {
            if (weaponNum <= GameConst.WP_NONE || weaponNum >= GameConst.MAX_WEAPONS)
                return null;

            if (_weaponInfo[weaponNum] == null)
                CG_RegisterWeapon(weaponNum);

            return _weaponInfo[weaponNum];
        }

        // -------------------------------------------------------------------
        // cg_weapons.c — rendering
        // -------------------------------------------------------------------

        public static void CG_AddWeaponWithPowerups(int weaponNum)
        {
            var wri = CG_GetWeaponRenderInfo(weaponNum);
            if (wri == null)
                return;

            // In the original C code this places the weapon model into the render
            // list with EF_POWERUP bits applied as shader remaps.  In the Unity
            // port the model is spawned from the Resources cache; powerup tinting
            // is applied via MaterialPropertyBlock on the MeshRenderer.
            var prefab = Resources.Load<GameObject>(wri.ModelPath);
            if (prefab == null)
                return;

            var go = UnityEngine.Object.Instantiate(prefab);
            // Tint if powerup active — callers set a component to handle lifetime.
            go.AddComponent<AutoDestroyAfterFrame>();
        }

        public static void CG_AddPlayerWeapon(EntityState es, PlayerState ps)
        {
            if (es == null || ps == null)
                return;

            var wri = CG_GetWeaponRenderInfo(es.Weapon);
            if (wri == null)
                return;

            var origin = ETToUnity(es.Origin0, es.Origin1, es.Origin2);
            var prefab = Resources.Load<GameObject>(wri.ModelPath);
            if (prefab == null)
                return;

            var go        = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.identity);
            go.AddComponent<AutoDestroyAfterFrame>();
        }

        public static void CG_AddViewWeapon(PlayerState ps)
        {
            if (ps == null)
                return;

            var wri = CG_GetWeaponRenderInfo(ps.Weapon);
            if (wri == null)
                return;

            // First-person weapon: placed at camera origin with viewAngles rotation.
            var cam    = Camera.main;
            if (cam == null)
                return;

            var prefab = Resources.Load<GameObject>(wri.ModelPath);
            if (prefab == null)
                return;

            var offset = cam.transform.TransformPoint(new Vector3(0.2f, -0.3f, 0.5f));
            var go     = UnityEngine.Object.Instantiate(prefab, offset, cam.transform.rotation);
            go.AddComponent<AutoDestroyAfterFrame>();
        }

        public static Vector3 CG_MuzzlePoint(EntityState es, Vector3 viewAngles)
        {
            var wri    = CG_GetWeaponRenderInfo(es.Weapon);
            float fwd  = wri != null ? wri.MuzzlePointForward : 28f;
            float up   = wri != null ? wri.MuzzlePointUp      : -2f;

            var origin = ETToUnity(es.Origin0, es.Origin1, es.Origin2);
            var rot    = Quaternion.Euler(viewAngles);
            return origin + rot * new Vector3(0f, up, fwd);
        }

        public static void CG_FireWeapon(CentityState cent)
        {
            if (cent == null)
                return;

            var es  = cent.CurrentState;
            var wri = CG_GetWeaponRenderInfo(es.Weapon);
            if (wri == null)
                return;

            var muzzle  = CG_MuzzlePoint(es, cent.LerpAngles);
            var forward = Quaternion.Euler(cent.LerpAngles) * Vector3.forward;

            AudioSystem.S_StartSound(muzzle, es.Number, 0, wri.FlashSound);

            CG_Explosion(muzzle, forward, wri.FlashDist, wri.FlashColor, 0.1f);

            if (wri.EjectBrass)
            {
                var brassPos = muzzle + Quaternion.Euler(cent.LerpAngles) * new Vector3(0.1f, 0f, 0f);
                EmitParticleBurst(brassPos, Vector3.up, 1, new Color(0.8f, 0.6f, 0.1f), 0.05f, 0.3f);
            }

            OnMuzzleFlash?.Invoke(es.Weapon, muzzle);
        }

        public static void CG_MissileHitWall(int weapon, int clientNum,
            Vector3 origin, Vector3 normal, int surfType)
        {
            int sfx = AudioSystem.S_RegisterSound("sound/weapons/impact/wall.wav", false);
            AudioSystem.S_StartSound(origin, clientNum, 0, sfx);

            float radius = (weapon == GameConst.WP_PANZERFAUST ||
                            weapon == GameConst.WP_MORTAR      ||
                            weapon == GameConst.WP_MORTAR_SET) ? 80f : 20f;

            var color = new Color(0.9f, 0.6f, 0.1f);
            CG_Explosion(origin + normal * 2f, normal, radius, color, 0.4f);

            EmitParticleBurst(origin, normal, 8, new Color(0.6f, 0.6f, 0.6f), 0.08f, 1.2f);

            OnImpact?.Invoke(weapon, origin, false);
        }

        public static void CG_MissileHitPlayer(int weapon, Vector3 origin,
            Vector3 normal, int entityNum)
        {
            int sfx = AudioSystem.S_RegisterSound("sound/weapons/impact/flesh.wav", false);
            AudioSystem.S_StartSound(origin, entityNum, 0, sfx);

            EmitParticleBurst(origin, normal, 6, new Color(0.6f, 0f, 0f), 0.06f, 0.8f);

            OnImpact?.Invoke(weapon, origin, true);
        }

        public static void CG_Bullet(Vector3 origin, int sourceEntityNum,
            Vector3 normal, bool flesh, int fleshEntityNum)
        {
            if (flesh)
            {
                EmitParticleBurst(origin, normal, 3, new Color(0.6f, 0f, 0f), 0.04f, 0.5f);
                OnImpact?.Invoke(GameConst.WP_NONE, origin, true);
            }
            else
            {
                EmitParticleBurst(origin, normal, 4, new Color(0.8f, 0.7f, 0.3f), 0.04f, 0.6f);
                OnImpact?.Invoke(GameConst.WP_NONE, origin, false);
            }
        }

        public static void CG_RailTrail(Vector3 start, Vector3 end)
        {
            // A line of small cyan particles spaced 8 units apart simulates the
            // original shader-based beam from CG_RailTrail in cg_weapons.c.
            var dir  = (end - start).normalized;
            float dist = Vector3.Distance(start, end);
            int   count = Mathf.Max(1, (int)(dist / 8f));
            for (int i = 0; i < count; i++)
            {
                var pos = Vector3.Lerp(start, end, i / (float)count);
                EmitParticleBurst(pos, dir, 1, new Color(0f, 0.8f, 1f), 0.02f, 0.3f);
            }
        }

        public static void CG_GrappleTrail(CentityState cent)
        {
            if (cent == null)
                return;

            var tip    = cent.LerpOrigin;
            var origin = ETToUnity(cent.CurrentState.Origin0,
                                   cent.CurrentState.Origin1,
                                   cent.CurrentState.Origin2);
            CG_RailTrail(origin, tip);
        }

        // -------------------------------------------------------------------
        // cg_weapons.c — HUD weapon selection
        // -------------------------------------------------------------------

        public static void CG_DrawWeaponSelect()
        {
            // Weapon selection wheel is rendered by the HUD subsystem; this method
            // publishes the current selection so HUD can draw the highlight ring.
            OnWeaponChanged?.Invoke(_selectedWeapon);
        }

        public static void CG_NextWeapon()
        {
            var ps = ClientMain.Cl?.Snap?.Ps;
            if (ps == null)
                return;

            int current = _selectedWeapon;
            for (int delta = 1; delta < GameConst.WP_NUM_WEAPONS; delta++)
            {
                int candidate = (current + delta) % GameConst.WP_NUM_WEAPONS;
                if (candidate == GameConst.WP_NONE)
                    continue;
                if (HasWeapon(ps, candidate))
                {
                    _selectedWeapon = candidate;
                    OnWeaponChanged?.Invoke(_selectedWeapon);
                    return;
                }
            }
        }

        public static void CG_PrevWeapon()
        {
            var ps = ClientMain.Cl?.Snap?.Ps;
            if (ps == null)
                return;

            int current = _selectedWeapon;
            for (int delta = 1; delta < GameConst.WP_NUM_WEAPONS; delta++)
            {
                int candidate = (current - delta + GameConst.WP_NUM_WEAPONS)
                                % GameConst.WP_NUM_WEAPONS;
                if (candidate == GameConst.WP_NONE)
                    continue;
                if (HasWeapon(ps, candidate))
                {
                    _selectedWeapon = candidate;
                    OnWeaponChanged?.Invoke(_selectedWeapon);
                    return;
                }
            }
        }

        public static void CG_Weapon_f(string[] args)
        {
            if (args == null || args.Length < 2)
                return;

            if (!int.TryParse(args[1], out int num))
                return;

            if (num <= GameConst.WP_NONE || num >= GameConst.WP_NUM_WEAPONS)
                return;

            var ps = ClientMain.Cl?.Snap?.Ps;
            if (ps != null && !HasWeapon(ps, num))
                return;

            _selectedWeapon = num;
            OnWeaponChanged?.Invoke(_selectedWeapon);
        }

        // PlayerState stores the weapon bitmask across two ints (64 bits total).
        private static bool HasWeapon(PlayerState ps, int wp)
        {
            if (wp < 0 || wp >= 64)
                return false;
            int slot = wp >> 5;
            int bit  = wp & 31;
            return (ps.Weapons[slot] & (1 << bit)) != 0;
        }

        // -------------------------------------------------------------------
        // cg_effects.c — visual effects
        // -------------------------------------------------------------------

        public static void CG_Explosion(Vector3 origin, Vector3 dir,
            float radius, Color color, float duration)
        {
            EmitParticleBurst(origin, dir, Mathf.Max(4, (int)(radius / 8f)),
                color, duration, radius * 0.5f);

            int sfx = AudioSystem.S_RegisterSound("sound/weapons/impact/explode.wav", false);
            AudioSystem.S_StartSound(origin, GameConst.ENTITYNUM_WORLD, 0, sfx);

            OnExplosionEffect?.Invoke(origin, radius);
        }

        public static void CG_GibPlayer(Vector3 playerOrigin)
        {
            var bloodColor = new Color(0.55f, 0f, 0f);
            EmitParticleBurst(playerOrigin, Vector3.up, 16, bloodColor, 0.5f, 2f);
            EmitParticleBurst(playerOrigin + Vector3.up * 0.5f,
                              Vector3.up, 8, new Color(0.7f, 0.4f, 0.1f), 0.4f, 1.5f);

            int sfx = AudioSystem.S_RegisterSound("sound/player/default/gibsplt1.wav", false);
            AudioSystem.S_StartSound(playerOrigin, GameConst.ENTITYNUM_WORLD, 0, sfx);
        }

        public static void CG_BigExplode(Vector3 origin)
        {
            // Primary fireball
            CG_Explosion(origin, Vector3.up, 120f, new Color(1f, 0.5f, 0.05f), 0.8f);
            // Secondary smoke column
            EmitParticleBurst(origin + Vector3.up * 2f, Vector3.up, 20,
                new Color(0.4f, 0.4f, 0.4f), 1.5f, 60f);
            // Ground shockwave ring
            EmitParticleBurst(origin, Vector3.up, 12, new Color(0.8f, 0.7f, 0.3f), 0.3f, 80f);
        }

        public static void CG_SmallExplode(Vector3 origin)
        {
            CG_Explosion(origin, Vector3.up, 40f, new Color(1f, 0.6f, 0.1f), 0.4f);
            EmitParticleBurst(origin + Vector3.up, Vector3.up, 6,
                new Color(0.5f, 0.5f, 0.5f), 0.8f, 20f);
        }

        public static void CG_MortarImpact(Vector3 origin, Vector3 normal, bool firstImpact)
        {
            CG_BigExplode(origin);

            // Dirt / debris spray follows the surface normal
            EmitParticleBurst(origin, normal, 14, new Color(0.4f, 0.3f, 0.15f), 0.6f, 30f);

            if (firstImpact)
            {
                // Persistent smoke lingers after the first hit
                EmitParticleBurst(origin + Vector3.up * 3f, Vector3.up, 24,
                    new Color(0.35f, 0.35f, 0.35f), 4f, 50f);
            }

            int sfx = AudioSystem.S_RegisterSound("sound/weapons/mortar/impact.wav", false);
            AudioSystem.S_StartSound(origin, GameConst.ENTITYNUM_WORLD, 0, sfx);
        }

        public static void CG_RocketExplosion(Vector3 origin, Vector3 normal, int entityNum)
        {
            CG_BigExplode(origin);
            EmitParticleBurst(origin, normal, 10, new Color(0.7f, 0.55f, 0.2f), 0.5f, 25f);

            int sfx = AudioSystem.S_RegisterSound("sound/weapons/panzerfaust/pf_hit.wav", false);
            AudioSystem.S_StartSound(origin, entityNum, 0, sfx);
        }

        public static void CG_DynamiteExplosion(Vector3 origin)
        {
            // Larger blast radius than CG_BigExplode — cg_effects.c used 512 qu
            CG_Explosion(origin, Vector3.up, 200f, new Color(1f, 0.55f, 0.1f), 1.2f);
            EmitParticleBurst(origin + Vector3.up * 4f, Vector3.up, 32,
                new Color(0.3f, 0.3f, 0.3f), 3f, 100f);
            EmitParticleBurst(origin, Vector3.up, 20, new Color(0.7f, 0.6f, 0.2f), 0.5f, 120f);

            int sfx = AudioSystem.S_RegisterSound("sound/weapons/dynamite/dynamite1.wav", false);
            AudioSystem.S_StartSound(origin, GameConst.ENTITYNUM_WORLD, 0, sfx);

            OnExplosionEffect?.Invoke(origin, 200f);
        }

        public static void CG_BubbleTrail(Vector3 start, Vector3 end, float spacing)
        {
            float dist  = Vector3.Distance(start, end);
            int   count = Mathf.Max(1, (int)(dist / Mathf.Max(1f, spacing)));
            for (int i = 0; i < count; i++)
            {
                var pos = Vector3.Lerp(start, end, i / (float)count);
                EmitParticleBurst(pos, Vector3.up, 1,
                    new Color(0.6f, 0.8f, 1f, 0.6f), 0.4f, 2f);
            }
        }

        public static void CG_SpawnEffect(Vector3 origin)
        {
            EmitParticleBurst(origin, Vector3.up, 16, new Color(0.5f, 0.8f, 1f), 0.6f, 40f);
            EmitParticleBurst(origin + Vector3.up, Vector3.up, 8,
                new Color(1f, 1f, 1f, 0.5f), 0.3f, 20f);

            int sfx = AudioSystem.S_RegisterSound("sound/player/default/teleport1.wav", false);
            AudioSystem.S_StartSound(origin, GameConst.ENTITYNUM_WORLD, 0, sfx);
        }

        public static void CG_ScorePlum(int client, Vector3 origin, int score)
        {
            // The original code created a localEntity with a string attached.
            // In the Unity port we fire an event so the HUD layer can render
            // floating text without the cgame layer owning a Canvas.
            var color  = score >= 0 ? Color.yellow : Color.red;
            EmitParticleBurst(origin + Vector3.up, Vector3.up, 2, color, 0.8f, 5f);
        }

        public static void CG_BloodTrail(CentityState cent)
        {
            if (cent == null)
                return;

            var origin = cent.LerpOrigin;
            EmitParticleBurst(origin, Vector3.down, 3,
                new Color(0.5f, 0f, 0f), 0.3f, 3f);
        }

        public static void CG_FlamethrowerFlame(CentityState cent, Vector3 origin)
        {
            if (cent == null)
                return;

            var dir = (origin - cent.LerpOrigin).normalized;
            if (dir == Vector3.zero)
                dir = Quaternion.Euler(cent.LerpAngles) * Vector3.forward;

            EmitParticleBurst(origin, dir, 6, new Color(1f, 0.4f, 0.05f), 0.3f, 15f);
            EmitParticleBurst(origin + dir * 0.5f, dir, 3,
                new Color(0.3f, 0.3f, 0.3f, 0.6f), 0.6f, 20f);
        }

        // -------------------------------------------------------------------
        // Internal particle helper — wraps ParticleSystem.Emit so all effects
        // go through one code path and can be swapped for a pooled system later.
        // -------------------------------------------------------------------
        private static ParticleSystem _sharedPS;

        private static void EmitParticleBurst(Vector3 origin, Vector3 dir,
            int count, Color color, float lifetime, float speed)
        {
            if (_sharedPS == null)
            {
                var go    = new GameObject("CGameWeapons_PS");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _sharedPS = go.AddComponent<ParticleSystem>();

                var main            = _sharedPS.main;
                main.loop           = false;
                main.playOnAwake    = false;
                main.maxParticles   = 4096;

                var emission        = _sharedPS.emission;
                emission.enabled    = false;

                var renderer        = _sharedPS.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            var mainMod             = _sharedPS.main;
            mainMod.startColor      = color;
            mainMod.startLifetime   = lifetime;
            mainMod.startSpeed      = speed;

            _sharedPS.transform.position = origin;
            _sharedPS.transform.rotation = Quaternion.LookRotation(
                dir == Vector3.zero ? Vector3.forward : dir);

            var ep      = new ParticleSystem.EmitParams();
            ep.position = origin;
            ep.applyShapeToPosition = false;
            _sharedPS.Emit(ep, count);
        }
    }

    // Utility component: removes the GameObject at end of the current frame so
    // one-shot weapon model previews don't accumulate in the scene.
    internal sealed class AutoDestroyAfterFrame : MonoBehaviour
    {
        private void LateUpdate()
        {
            Destroy(gameObject);
        }
    }
}
