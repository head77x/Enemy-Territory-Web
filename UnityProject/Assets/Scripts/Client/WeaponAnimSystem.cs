// Ported from: src/cgame/cg_weapons.c
//   CG_ParseWeaponConfig, CG_RunWeapLerpFrame, CG_WeaponAnimation,
//   CG_SetWeapLerpFrameAnimation, CG_ClearWeapLerpFrame
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using ET.Core;
using ET.Game;

namespace ET.Client
{
    // -------------------------------------------------------------------
    // animation_t equivalent
    // -------------------------------------------------------------------
    public class WeapAnim
    {
        public int FirstFrame;
        public int NumFrames;
        public int FrameLerp;    // milliseconds per frame = 1000 / fps
        public int InitialLerp;  // same as FrameLerp on init
        public int LoopFrames;   // 0 = play once; >0 = loop last N frames
        public int MoveSpeed;    // barrel-anim bits (W_PART_*) + hide bits
    }

    // -------------------------------------------------------------------
    // lerpFrame_t equivalent — per-weapon instance state
    // -------------------------------------------------------------------
    public class WeapLerpFrame
    {
        public int      OldFrame;
        public int      Frame;
        public int      OldFrameTime;   // ms
        public int      FrameTime;      // ms — when Frame becomes current
        public float    Backlerp;       // 0=fully at Frame, 1=fully at OldFrame
        public int      AnimationNumber;    // current anim index (may include ANIM_TOGGLEBIT)
        public WeapAnim Animation;          // null until first set
        public int      AnimationTime;      // ms — when anim started
    }

    // -------------------------------------------------------------------
    // weaponInfo_t.weapAnimations[] equivalent
    // Parsed from "weapons/<name>.weap" — same format as CG_ParseWeaponConfig.
    // -------------------------------------------------------------------
    public class WeaponAnimData
    {
        // MAX_WP_ANIMATIONS = 13  (bg_public.h)
        public const int MAX_WP_ANIMATIONS = 13;

        public readonly WeapAnim[] Animations = new WeapAnim[MAX_WP_ANIMATIONS];
        public bool Loaded;

        public WeaponAnimData()
        {
            for (int i = 0; i < MAX_WP_ANIMATIONS; i++)
                Animations[i] = new WeapAnim { FrameLerp = 100, InitialLerp = 100 };
        }

        // CG_ParseWeaponConfig equivalent.
        // "newfmt" header enables the extra barrel-anim / hide-bit columns.
        public static WeaponAnimData Load(string virtualPath)
        {
            string text = FileSystem.FS_ReadFileText(virtualPath);
            if (string.IsNullOrEmpty(text))
                return null;

            var data = new WeaponAnimData();
            var tokens = text.Split(new char[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            int t = 0;
            bool newfmt = false;

            // optional "newfmt" header
            if (t < tokens.Length && string.Equals(tokens[t], "newfmt",
                StringComparison.OrdinalIgnoreCase))
            {
                newfmt = true;
                t++;
            }

            for (int i = 0; i < MAX_WP_ANIMATIONS; i++)
            {
                if (t + 3 >= tokens.Length) break;

                int firstFrame  = int.Parse(tokens[t++]);
                int numFrames   = int.Parse(tokens[t++]);
                float fps       = float.Parse(tokens[t++],
                    System.Globalization.CultureInfo.InvariantCulture);
                int loopFrames  = int.Parse(tokens[t++]);

                if (fps == 0f) fps = 1f;
                int frameLerp = (int)(1000f / fps);

                if (loopFrames > numFrames) loopFrames = numFrames;
                else if (loopFrames < 0)   loopFrames = 0;

                int moveSpeed = 0;
                if (newfmt && t + 2 < tokens.Length)
                {
                    moveSpeed  = int.Parse(tokens[t++]);           // barrel anim bits
                    if (int.Parse(tokens[t++]) != 0)               // animated weapon bit
                        moveSpeed |= (1 << 7);                     // W_MAX_PARTS = 7
                    moveSpeed |= (int.Parse(tokens[t++]) << 8);    // hide bits
                }

                data.Animations[i] = new WeapAnim
                {
                    FirstFrame  = firstFrame,
                    NumFrames   = numFrames,
                    FrameLerp   = frameLerp,
                    InitialLerp = frameLerp,
                    LoopFrames  = loopFrames,
                    MoveSpeed   = moveSpeed,
                };
            }

            data.Loaded = true;
            return data;
        }
    }

    // -------------------------------------------------------------------
    // Static animation driver — CG_RunWeapLerpFrame / CG_WeaponAnimation
    // -------------------------------------------------------------------
    public static class WeaponAnimSystem
    {
        // CG_SetWeapLerpFrameAnimation
        private static void SetAnimation(WeaponAnimData wi, WeapLerpFrame lf, int newAnim)
        {
            lf.AnimationNumber = newAnim;
            int idx = newAnim & ~GameConst.ANIM_TOGGLEBIT;
            if (idx < 0 || idx >= WeaponAnimData.MAX_WP_ANIMATIONS)
                idx = GameConst.WEAP_IDLE1;

            lf.Animation     = wi.Animations[idx];
            lf.AnimationTime = lf.FrameTime + lf.Animation.InitialLerp;
        }

        // CG_ClearWeapLerpFrame
        private static void ClearLerpFrame(WeaponAnimData wi, WeapLerpFrame lf,
            int newAnim, int cgTime)
        {
            lf.FrameTime = lf.OldFrameTime = cgTime;
            SetAnimation(wi, lf, newAnim);
            lf.OldFrame = lf.Frame = lf.Animation.FirstFrame;
        }

        // CG_RunWeapLerpFrame
        public static void RunWeapLerpFrame(WeaponAnimData wi, WeapLerpFrame lf,
            int newAnim, int cgTime)
        {
            if (lf.Animation == null)
                ClearLerpFrame(wi, lf, newAnim, cgTime);
            else if (newAnim != lf.AnimationNumber)
                SetAnimation(wi, lf, newAnim);

            // Advance frame if time has passed
            if (cgTime >= lf.FrameTime)
            {
                lf.OldFrame     = lf.Frame;
                lf.OldFrameTime = lf.FrameTime;

                var anim = lf.Animation;
                if (anim.FrameLerp == 0) return;

                lf.FrameTime = (cgTime < lf.AnimationTime)
                    ? lf.AnimationTime
                    : lf.OldFrameTime + anim.FrameLerp;

                int f = (lf.FrameTime - lf.AnimationTime) / anim.FrameLerp;
                if (f >= anim.NumFrames)
                {
                    f -= anim.NumFrames;
                    if (anim.LoopFrames > 0)
                    {
                        f %= anim.LoopFrames;
                        f += anim.NumFrames - anim.LoopFrames;
                    }
                    else
                    {
                        f = anim.NumFrames - 1;
                        lf.FrameTime = cgTime; // stuck at last frame
                    }
                }
                lf.Frame = anim.FirstFrame + f;
                if (cgTime > lf.FrameTime) lf.FrameTime = cgTime;
            }

            // Clamp to sane values (mirrors the C code)
            if (lf.FrameTime  > cgTime + 200) lf.FrameTime  = cgTime;
            if (lf.OldFrameTime > cgTime)      lf.OldFrameTime = cgTime;

            // Compute backlerp
            lf.Backlerp = (lf.FrameTime == lf.OldFrameTime)
                ? 0f
                : 1f - (float)(cgTime - lf.OldFrameTime)
                       / (lf.FrameTime - lf.OldFrameTime);
        }

        // CG_WeaponAnimation — top-level call (mirrors ET's version).
        // ps.WeapAnim = newAnimation to drive.
        public static void WeaponAnimation(WeaponAnimData wi, WeapLerpFrame lf,
            int weapAnim, int cgTimeMs,
            out int oldFrame, out int frame, out float backlerp)
        {
            RunWeapLerpFrame(wi, lf, weapAnim, cgTimeMs);
            oldFrame  = lf.OldFrame;
            frame     = lf.Frame;
            backlerp  = lf.Backlerp;
        }

        // Return the .weap virtual path for a weapon number (mirrors the switch in
        // CG_RegisterWeapon inside cg_weapons.c).
        public static string WeapFilePath(int weapon)
        {
            string fn;
            switch (weapon)
            {
                case GameConst.WP_KNIFE:                fn = "knife";                  break;
                case GameConst.WP_LUGER:                fn = "luger";                  break;
                case GameConst.WP_COLT:                 fn = "colt";                   break;
                case GameConst.WP_MP40:                 fn = "mp40";                   break;
                case GameConst.WP_THOMPSON:             fn = "thompson";               break;
                case GameConst.WP_STEN:                 fn = "sten";                   break;
                case GameConst.WP_GRENADE_LAUNCHER:     fn = "grenade";                break;
                case GameConst.WP_GRENADE_PINEAPPLE:    fn = "pineapple";              break;
                case GameConst.WP_PANZERFAUST:          fn = "panzerfaust";            break;
                case GameConst.WP_FLAMETHROWER:         fn = "flamethrower";           break;
                case GameConst.WP_DYNAMITE:             fn = "dynamite";               break;
                case GameConst.WP_MEDIC_ADRENALINE:     fn = "adrenaline";             break;
                case GameConst.WP_MEDIC_SYRINGE:        fn = "syringe";                break;
                case GameConst.WP_BINOCULARS:           fn = "binocs";                 break;
                case GameConst.WP_KAR98:                fn = "kar98";                  break;
                case GameConst.WP_GPG40:                fn = "gpg40";                  break;
                case GameConst.WP_CARBINE:              fn = "m1_garand";              break;
                case GameConst.WP_M7:                   fn = "m7";                     break;
                case GameConst.WP_GARAND:
                case GameConst.WP_GARAND_SCOPE:         fn = "m1_garand_s";            break;
                case GameConst.WP_FG42:
                case GameConst.WP_FG42SCOPE:            fn = "fg42";                   break;
                case GameConst.WP_LANDMINE:             fn = "landmine";               break;
                case GameConst.WP_SATCHEL:              fn = "satchel";                break;
                case GameConst.WP_SATCHEL_DET:          fn = "satchel_det";            break;
                case GameConst.WP_MOBILE_MG42:
                case GameConst.WP_MOBILE_MG42_SET:      fn = "mg42";                   break;
                case GameConst.WP_SILENCER:             fn = "silenced_luger";         break;
                case GameConst.WP_SILENCED_COLT:        fn = "silenced_colt";          break;
                case GameConst.WP_K43:
                case GameConst.WP_K43_SCOPE:            fn = "k43";                    break;
                case GameConst.WP_MORTAR:               fn = "mortar";                 break;
                case GameConst.WP_MORTAR_SET:           fn = "mortar_set";             break;
                case GameConst.WP_AKIMBO_LUGER:         fn = "akimbo_luger";           break;
                case GameConst.WP_AKIMBO_SILENCEDLUGER: fn = "akimbo_silenced_luger";  break;
                case GameConst.WP_AKIMBO_COLT:          fn = "akimbo_colt";            break;
                case GameConst.WP_AKIMBO_SILENCEDCOLT:  fn = "akimbo_silenced_colt";   break;
                default:                                fn = null;                     break;
            }
            return fn != null ? $"weapons/{fn}.weap" : null;
        }
    }
}
