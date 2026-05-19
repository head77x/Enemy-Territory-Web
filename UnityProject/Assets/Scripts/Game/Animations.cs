// Ported from: src/game/bg_animation.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// Data-driven animation script system: parses .anim clip tables and .script
// condition/movement-type blocks, evaluates conditions each frame, and selects
// the appropriate animation clip per body part.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;

namespace ET.Game
{
    // -----------------------------------------------------------------------
    // Enumerations
    // -----------------------------------------------------------------------

    /// <summary>
    /// scriptAnimMoveTypes_t — locomotion states driving animation selection.
    /// </summary>
    public enum AnimMoveType
    {
        Idle        = 0,
        IdleCr      = 1,   // idle crouched
        Walk        = 2,
        WalkBack    = 3,
        WalkCr      = 4,   // crouch walk
        WalkCrBack  = 5,
        Run         = 6,
        RunBack     = 7,
        Swim        = 8,
        SwimBack    = 9,
        Fallen      = 10,  // dead, before limbo
        Flailing    = 11,  // falling out of control
        Ladder      = 12,
        LadderDown  = 13,
        LadderIdle  = 14,
        Prone       = 15,
        ProneBack   = 16,
        ProneIdle   = 17,
        Count       = 18,
    }

    /// <summary>
    /// Simplified set of runtime animation conditions evaluated from PlayerState.
    /// Maps to the ANIM_COND_* values used by BG_EvaluateConditions.
    /// </summary>
    public enum AnimCondition
    {
        WeaponClass = 0,
        OnGround    = 1,
        InWater     = 2,
        Crouching   = 3,
        Prone       = 4,
        Firing      = 5,
        Mounted     = 6,
        Moving      = 7,
        Count       = 8,
    }

    /// <summary>
    /// Weapon animation groups — maps WP_* IDs to broad animation categories.
    /// </summary>
    public enum AnimWeaponClass
    {
        None    = 0,
        Pistol  = 1,
        Rifle   = 2,
        MG      = 3,
        SMG     = 4,
        Sniper  = 5,
        Thrown  = 6,
        Heavy   = 7,
        Knife   = 8,
        Count   = 9,
    }

    /// <summary>
    /// scriptAnimEventTypes_t — one-shot animation events (jump, land, footstep…).
    /// </summary>
    public enum AnimEventType
    {
        Jump            = 0,
        JumpBack        = 1,
        Land            = 2,
        FootStep        = 3,
        FootStepMetal   = 4,
        FootStepWood    = 5,
        FootStepGrass   = 6,
        FootStepGravel  = 7,
        FootStepSnow    = 8,
        FootStepCarpet  = 9,
        FootStepRoof    = 10,
        Splash          = 11,
        Count           = 12,
    }

    /// <summary>
    /// animBodyPart_t — which skeletal parts an animation targets.
    /// </summary>
    public enum AnimBodyPart
    {
        Legs  = 0,
        Torso = 1,
        Head  = 2,
        Count = 3,
    }

    // -----------------------------------------------------------------------
    // Data structures
    // -----------------------------------------------------------------------

    /// <summary>
    /// One condition entry inside an AnimScriptItem — mirrors animScriptCondition_t.
    /// </summary>
    public struct AnimScriptCondition
    {
        /// <summary>Which condition axis this tests.</summary>
        public AnimCondition Condition;
        /// <summary>
        /// Expected value.  For boolean conditions (OnGround, InWater, etc.) this
        /// is 0 or 1.  For WeaponClass it is the AnimWeaponClass int value.
        /// </summary>
        public int Value;
    }

    /// <summary>
    /// One row in an animation script — a set of conditions and the clip names that
    /// should play when all conditions pass.  Mirrors animScriptItem_t / animScriptCommand_t.
    /// </summary>
    public class AnimScriptItem
    {
        /// <summary>All conditions must match for this item to be selected.</summary>
        public AnimScriptCondition[] Conditions = Array.Empty<AnimScriptCondition>();

        /// <summary>
        /// Clip name per body part (indexed by AnimBodyPart).  Empty string means
        /// "no animation assigned for this part".
        /// </summary>
        public string[] Animations = new string[(int)AnimBodyPart.Count];

        /// <summary>
        /// Resolved animation indices into AnimModelInfo.Animations (set after parse).
        /// -1 means unresolved / not present.
        /// </summary>
        public int[] AnimNums = new int[(int)AnimBodyPart.Count]
        {
            -1, -1, -1,
        };
    }

    /// <summary>
    /// All script items for one movement type — mirrors animScript_t.
    /// </summary>
    public class AnimScript
    {
        public AnimMoveType         MoveType;
        public List<AnimScriptItem> Items = new List<AnimScriptItem>();
    }

    /// <summary>
    /// Per-character animation data, combining the .anim clip table and the parsed
    /// .script condition blocks.  Mirrors animModelInfo_t.
    /// </summary>
    public class AnimModelInfo
    {
        public string              Name;
        /// <summary>Scripts indexed by (int)AnimMoveType.</summary>
        public AnimScript[]        Scripts     = new AnimScript[(int)AnimMoveType.Count];
        public List<AnimClipInfo>  Animations  = new List<AnimClipInfo>();
        public AnimWeaponClass     WeaponClass;
        public bool                Initialized;
    }

    /// <summary>
    /// One animation clip row parsed from a .anim file — mirrors animation_t.
    /// </summary>
    public struct AnimClipInfo
    {
        public string Name;
        public int    FirstFrame;
        public int    NumFrames;
        /// <summary>Number of frames to loop; -1 = play once and hold last frame.</summary>
        public int    LoopFrames;
        public float  FrameRate;
        public bool   Reversed;
    }

    /// <summary>
    /// Snapshot of evaluated animation conditions for one client, computed each frame
    /// by BG_AnimUpdatePlayerStateConditions and consumed by BG_SelectPlayerSkin.
    /// </summary>
    public struct AnimConditionState
    {
        public AnimWeaponClass WeaponClass;
        public bool OnGround;
        public bool InWater;
        public bool Crouching;
        public bool Prone;
        public bool Firing;
        public bool Mounted;
        public bool Moving;
    }

    // -----------------------------------------------------------------------
    // Core static class
    // -----------------------------------------------------------------------

    /// <summary>
    /// bg_animation.c port — data-driven animation script evaluation system.
    ///
    /// Lifecycle:
    ///   1. Call BG_InitializeAnimationPool() once at startup.
    ///   2. For each character model, call BG_ParseAnimationFile() then
    ///      BG_ParseAnimationScript() to populate an AnimModelInfo.
    ///   3. Each frame: call BG_AnimUpdatePlayerStateConditions() to fill an
    ///      AnimConditionState, then BG_AnimScriptAnimation() / BG_AnimScriptEvent()
    ///      to fire the appropriate clips via the OnAnimMove / OnAnimEvent events.
    /// </summary>
    public static class Animations
    {
        // -----------------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------------

        /// <summary>
        /// Raised when a one-shot animation event fires (jump, land, footstep, …).
        /// Arguments: (clientNum, eventType).
        /// </summary>
        public static event Action<int, AnimEventType> OnAnimEvent;

        /// <summary>
        /// Raised when the locomotion animation for a body part should change.
        /// Arguments: (clientNum, bodyPart, clipName).
        /// </summary>
        public static event Action<int, AnimBodyPart, string> OnAnimMove;

        // -----------------------------------------------------------------------
        // Module-level state
        // -----------------------------------------------------------------------

        private static bool s_initialized;

        // Keyword tables for script parsing — mirrors animMoveTypesStr[].
        private static readonly Dictionary<string, AnimMoveType> s_moveTypeKeywords =
            new Dictionary<string, AnimMoveType>(StringComparer.OrdinalIgnoreCase)
        {
            { "idle",       AnimMoveType.Idle       },
            { "idlecr",     AnimMoveType.IdleCr     },
            { "walk",       AnimMoveType.Walk       },
            { "walkbk",     AnimMoveType.WalkBack   },
            { "walkback",   AnimMoveType.WalkBack   },
            { "walkcr",     AnimMoveType.WalkCr     },
            { "walkcrbk",   AnimMoveType.WalkCrBack },
            { "walkcrback", AnimMoveType.WalkCrBack },
            { "run",        AnimMoveType.Run        },
            { "runbk",      AnimMoveType.RunBack    },
            { "runback",    AnimMoveType.RunBack    },
            { "swim",       AnimMoveType.Swim       },
            { "swimbk",     AnimMoveType.SwimBack   },
            { "swimback",   AnimMoveType.SwimBack   },
            { "fallen",     AnimMoveType.Fallen     },
            { "flailing",   AnimMoveType.Flailing   },
            { "climbup",    AnimMoveType.Ladder     },
            { "ladder",     AnimMoveType.Ladder     },
            { "climbdown",  AnimMoveType.LadderDown },
            { "ladderdown", AnimMoveType.LadderDown },
            { "ladderidle", AnimMoveType.LadderIdle },
            { "prone",      AnimMoveType.Prone      },
            { "pronebk",    AnimMoveType.ProneBack  },
            { "proneback",  AnimMoveType.ProneBack  },
            { "idleprone",  AnimMoveType.ProneIdle  },
        };

        // Keyword tables for condition names in script files.
        private static readonly Dictionary<string, AnimCondition> s_conditionKeywords =
            new Dictionary<string, AnimCondition>(StringComparer.OrdinalIgnoreCase)
        {
            { "weapon_class",  AnimCondition.WeaponClass },
            { "weapons",       AnimCondition.WeaponClass },
            { "on_ground",     AnimCondition.OnGround    },
            { "onground",      AnimCondition.OnGround    },
            { "in_water",      AnimCondition.InWater     },
            { "underwater",    AnimCondition.InWater     },
            { "crouching",     AnimCondition.Crouching   },
            { "prone",         AnimCondition.Prone       },
            { "firing",        AnimCondition.Firing      },
            { "mounted",       AnimCondition.Mounted     },
            { "moving",        AnimCondition.Moving      },
            { "movetype",      AnimCondition.Moving      },
        };

        // Weapon class name resolution used in script condition values.
        private static readonly Dictionary<string, AnimWeaponClass> s_weaponClassKeywords =
            new Dictionary<string, AnimWeaponClass>(StringComparer.OrdinalIgnoreCase)
        {
            { "none",    AnimWeaponClass.None   },
            { "pistol",  AnimWeaponClass.Pistol },
            { "rifle",   AnimWeaponClass.Rifle  },
            { "mg",      AnimWeaponClass.MG     },
            { "smg",     AnimWeaponClass.SMG    },
            { "sniper",  AnimWeaponClass.Sniper },
            { "thrown",  AnimWeaponClass.Thrown },
            { "heavy",   AnimWeaponClass.Heavy  },
            { "knife",   AnimWeaponClass.Knife  },
        };

        // -----------------------------------------------------------------------
        // BG_InitializeAnimationPool
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_InitializeAnimationPool — prepare the animation script system.
        /// Call once before any BG_ParseAnimation* calls.
        /// </summary>
        public static void BG_InitializeAnimationPool()
        {
            s_initialized = true;
            OnAnimEvent   = null;
            OnAnimMove    = null;
        }

        // -----------------------------------------------------------------------
        // BG_ParseAnimationFile
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_ParseAnimationFile — parse a character's .anim clip table.
        ///
        /// File format (one clip per non-comment line):
        ///   NAME  firstFrame  numFrames  loopFrames  fps
        ///
        /// Lines starting with '//' or '/' are comments.
        /// Lines whose first token is "sex", "fixedtorso", or "fixedlegs" are metadata
        /// and are skipped.
        /// </summary>
        /// <param name="filename">Used only for error messages.</param>
        /// <param name="fileText">Full text content of the .anim file.</param>
        /// <returns>Populated AnimModelInfo, or null on parse failure.</returns>
        public static AnimModelInfo BG_ParseAnimationFile(string filename, string fileText)
        {
            if (!s_initialized)
                BG_InitializeAnimationPool();

            if (string.IsNullOrEmpty(fileText))
            {
                Debug.LogWarning($"[Animations] BG_ParseAnimationFile: empty file '{filename}'");
                return null;
            }

            var model = new AnimModelInfo
            {
                Name        = filename,
                Initialized = false,
            };

            // Initialise all script slots so BG_ParseAnimationScript can populate them.
            for (int i = 0; i < (int)AnimMoveType.Count; i++)
            {
                model.Scripts[i] = new AnimScript { MoveType = (AnimMoveType)i };
            }

            string[] lines = fileText.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                // Skip blank lines and comment lines.
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("//") || line.StartsWith("/"))
                    continue;

                string[] parts = line.Split(new char[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0)
                    continue;

                // Metadata keywords — skip.
                string firstToken = parts[0].ToLowerInvariant();
                if (firstToken == "sex"         ||
                    firstToken == "fixedtorso"  ||
                    firstToken == "fixedlegs"   ||
                    firstToken == "headoffset"  ||
                    firstToken == "footsteps")
                    continue;

                // Expect: NAME firstFrame numFrames loopFrames fps [reversed]
                if (parts.Length < 5)
                    continue;

                if (!int.TryParse(parts[1], out int firstFrame)  ||
                    !int.TryParse(parts[2], out int numFrames)   ||
                    !int.TryParse(parts[3], out int loopFrames)  ||
                    !float.TryParse(parts[4], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float fps))
                {
                    // Not a clip line — might be an unrecognised metadata row; skip.
                    continue;
                }

                bool reversed = parts.Length >= 6 &&
                    parts[5].Equals("reversed", StringComparison.OrdinalIgnoreCase);

                model.Animations.Add(new AnimClipInfo
                {
                    Name       = parts[0],
                    FirstFrame = firstFrame,
                    NumFrames  = numFrames,
                    LoopFrames = loopFrames,
                    FrameRate  = fps,
                    Reversed   = reversed,
                });
            }

            model.Initialized = model.Animations.Count > 0;

            if (!model.Initialized)
                Debug.LogWarning($"[Animations] BG_ParseAnimationFile: no clips parsed from '{filename}'");

            return model;
        }

        // -----------------------------------------------------------------------
        // BG_ParseAnimationScript
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_ParseAnimationScript — parse a .script file and populate model.Scripts.
        ///
        /// Supported block format (simplified subset of the ET .script grammar):
        /// <code>
        /// animations
        /// {
        ///     state RELAXED
        ///     {
        ///         IDLE
        ///         {
        ///             weapon_class rifle
        ///             on_ground true
        ///             {
        ///                 legs  LEGS_IDLE
        ///                 torso TORSO_IDLE
        ///             }
        ///         }
        ///     }
        /// }
        /// </code>
        /// The outer "animations" / "state RELAXED { … }" shell is accepted and
        /// stripped; the inner movement-type blocks are parsed directly.
        ///
        /// A simplified flat format is also accepted for hand-written scripts:
        /// <code>
        /// idle {
        ///     conditions {
        ///         weapon_class rifle
        ///         on_ground true
        ///     }
        ///     animations {
        ///         legs  LEGS_IDLE
        ///         torso TORSO_IDLE
        ///     }
        /// }
        /// </code>
        /// </summary>
        public static bool BG_ParseAnimationScript(AnimModelInfo model, string scriptText)
        {
            if (model == null)
            {
                Debug.LogError("[Animations] BG_ParseAnimationScript: null model");
                return false;
            }
            if (string.IsNullOrEmpty(scriptText))
                return false;

            // Tokenise the whole file (strips // comments, splits on whitespace / braces).
            var tokens = Tokenise(scriptText);
            int pos    = 0;

            while (pos < tokens.Count)
            {
                string tok = tokens[pos];

                // Skip top-level section keywords we don't handle.
                if (tok.Equals("defines",           StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("canned_animations",  StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("statechanges",       StringComparison.OrdinalIgnoreCase) ||
                    tok.Equals("events",             StringComparison.OrdinalIgnoreCase))
                {
                    pos++;
                    // Skip the brace block that follows.
                    pos = SkipBlock(tokens, pos);
                    continue;
                }

                // "animations" section — descend.
                if (tok.Equals("animations", StringComparison.OrdinalIgnoreCase))
                {
                    pos++;
                    if (pos < tokens.Count && tokens[pos] == "{")
                    {
                        pos++; // consume '{'
                        pos = ParseAnimationsBlock(tokens, pos, model);
                        // consume closing '}'
                        if (pos < tokens.Count && tokens[pos] == "}")
                            pos++;
                    }
                    continue;
                }

                // Movement-type name at top level (flat format without outer shell).
                if (s_moveTypeKeywords.TryGetValue(tok, out AnimMoveType flatMoveType))
                {
                    pos++;
                    if (pos < tokens.Count && tokens[pos] == "{")
                    {
                        pos++; // consume '{'
                        pos = ParseMoveTypeBlock(tokens, pos, model, flatMoveType);
                        if (pos < tokens.Count && tokens[pos] == "}")
                            pos++;
                    }
                    continue;
                }

                // Anything else at top level — skip.
                pos++;
            }

            // Resolve animation name strings to indices in model.Animations.
            ResolveAnimNums(model);

            return true;
        }

        // -----------------------------------------------------------------------
        // BG_AnimUpdatePlayerStateConditions
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_AnimUpdatePlayerStateConditions — compute the current AnimConditionState
        /// from live PlayerState and PmoveInput data.
        ///
        /// Mirrors the per-frame condition evaluation done in ET's cgame before calling
        /// BG_AnimScriptAnimation / BG_AnimScriptEvent.
        /// </summary>
        public static void BG_AnimUpdatePlayerStateConditions(
            PlayerState ps, PmoveInput pm, ref AnimConditionState state)
        {
            if (ps == null)
                return;

            // WeaponClass
            state.WeaponClass = BG_GetWeaponClassForWeapon(ps.Weapon);

            // OnGround: has a valid ground entity
            state.OnGround = ps.GroundEntityNum != GameConst.ENTITYNUM_NONE;

            // InWater: pmove water level >= waist, or PM_TYPE is swimming
            bool pmTypeSwimming = ps.PmType == GameConst.PM_NORMAL &&
                                  (pm != null && pm.WaterLevel >= GameConst.WATERLEVEL_WAIST);
            state.InWater = pmTypeSwimming ||
                            ((ps.PmFlags & GameConst.PMF_TIME_WATERJUMP) != 0);

            // Crouching
            state.Crouching = (ps.PmFlags & GameConst.PMF_DUCKED) != 0;

            // Prone
            state.Prone = (ps.EFlags & GameConst.EF_PRONE) != 0;

            // Firing
            state.Firing = (ps.EFlags & GameConst.EF_FIRING) != 0;

            // Mounted (MG42 or AA gun)
            state.Mounted = (ps.EFlags & (GameConst.EF_MG42_ACTIVE | GameConst.EF_AAGUN_ACTIVE)) != 0;

            // Moving: XY velocity magnitude > 1 unit
            float vx = ps.Velocity0;
            float vy = ps.Velocity1;
            state.Moving = (vx * vx + vy * vy) > 1f;
        }

        // -----------------------------------------------------------------------
        // BG_SelectPlayerSkin
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_SelectPlayerSkin — iterate the script items for the given move type,
        /// returning the first AnimScriptItem whose conditions all pass.
        /// Returns null if no match is found (mirrors BG_FirstValidItem).
        /// </summary>
        public static AnimScriptItem BG_SelectPlayerSkin(
            AnimModelInfo model, AnimMoveType moveType, in AnimConditionState state)
        {
            if (model == null)
                return null;

            int scriptIdx = (int)moveType;
            if (scriptIdx < 0 || scriptIdx >= model.Scripts.Length)
                return null;

            AnimScript script = model.Scripts[scriptIdx];
            if (script == null)
                return null;

            foreach (AnimScriptItem item in script.Items)
            {
                if (EvaluateConditions(item, in state))
                    return item;
            }

            return null;
        }

        // -----------------------------------------------------------------------
        // BG_AnimScriptEvent
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_AnimScriptEvent — fire a one-shot animation event.
        /// Raises OnAnimEvent so the renderer / audio layer can play the appropriate
        /// clip or sound.  Mirrors BG_AnimScriptEvent from bg_animation.c.
        /// </summary>
        public static void BG_AnimScriptEvent(
            PlayerState ps, AnimEventType eventType, bool isCrouching, bool isOnGround)
        {
            if (ps == null)
                return;

            // Dead clients only respond to death/fallen events.
            if ((ps.EFlags & GameConst.EF_DEAD) != 0)
            {
                if (eventType != AnimEventType.Land)
                    return;
            }

            OnAnimEvent?.Invoke(ps.ClientNum, eventType);
        }

        // -----------------------------------------------------------------------
        // BG_AnimScriptAnimation
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_AnimScriptAnimation — select and apply the locomotion animation for the
        /// current player state.  Raises OnAnimMove for each body part that has a
        /// valid clip assigned by the matched AnimScriptItem.
        /// Mirrors BG_AnimScriptAnimation from bg_animation.c.
        /// </summary>
        public static void BG_AnimScriptAnimation(
            PlayerState ps, AnimModelInfo model, AnimMoveType moveType,
            in AnimConditionState state)
        {
            if (ps == null || model == null)
                return;

            // Dead clients may only play Fallen / Flailing.
            if ((ps.EFlags & GameConst.EF_DEAD) != 0 &&
                moveType != AnimMoveType.Fallen &&
                moveType != AnimMoveType.Flailing)
                return;

            AnimScriptItem item = BG_SelectPlayerSkin(model, moveType, in state);
            if (item == null)
                return;

            int clientNum = ps.ClientNum;

            for (int part = 0; part < (int)AnimBodyPart.Count; part++)
            {
                string clipName = item.Animations[part];
                if (!string.IsNullOrEmpty(clipName))
                {
                    OnAnimMove?.Invoke(clientNum, (AnimBodyPart)part, clipName);
                }
            }
        }

        // -----------------------------------------------------------------------
        // BG_GetWeaponClassForWeapon
        // -----------------------------------------------------------------------

        /// <summary>
        /// BG_GetWeaponClassForWeapon — map a WP_* weapon ID to an AnimWeaponClass.
        /// Mirrors the switch in bg_animation.c's condition evaluation.
        /// </summary>
        public static AnimWeaponClass BG_GetWeaponClassForWeapon(int weapon)
        {
            switch (weapon)
            {
                // Knife
                case GameConst.WP_KNIFE:
                    return AnimWeaponClass.Knife;

                // Pistols (including akimbo and silenced variants)
                case GameConst.WP_LUGER:
                case GameConst.WP_COLT:
                case GameConst.WP_SILENCER:
                case GameConst.WP_SILENCED_COLT:
                case GameConst.WP_AKIMBO_COLT:
                case GameConst.WP_AKIMBO_LUGER:
                case GameConst.WP_AKIMBO_SILENCEDCOLT:
                case GameConst.WP_AKIMBO_SILENCEDLUGER:
                    return AnimWeaponClass.Pistol;

                // Submachine guns
                case GameConst.WP_MP40:
                case GameConst.WP_THOMPSON:
                case GameConst.WP_STEN:
                    return AnimWeaponClass.SMG;

                // Heavy weapons (rockets, flame, mortar, deployed MG)
                case GameConst.WP_PANZERFAUST:
                case GameConst.WP_FLAMETHROWER:
                case GameConst.WP_MOBILE_MG42:
                case GameConst.WP_MOBILE_MG42_SET:
                case GameConst.WP_MORTAR:
                case GameConst.WP_MORTAR_SET:
                    return AnimWeaponClass.Heavy;

                // Rifles
                case GameConst.WP_KAR98:
                case GameConst.WP_CARBINE:
                case GameConst.WP_GARAND:
                case GameConst.WP_K43:
                case GameConst.WP_FG42:
                case GameConst.WP_GPG40:
                case GameConst.WP_M7:
                    return AnimWeaponClass.Rifle;

                // Scoped / sniper rifles
                case GameConst.WP_GARAND_SCOPE:
                case GameConst.WP_K43_SCOPE:
                case GameConst.WP_FG42SCOPE:
                    return AnimWeaponClass.Sniper;

                // Thrown / placed explosives
                case GameConst.WP_GRENADE_LAUNCHER:
                case GameConst.WP_GRENADE_PINEAPPLE:
                case GameConst.WP_DYNAMITE:
                case GameConst.WP_SATCHEL:
                case GameConst.WP_SATCHEL_DET:
                case GameConst.WP_LANDMINE:
                case GameConst.WP_TRIPMINE:
                case GameConst.WP_SMOKE_BOMB:
                    return AnimWeaponClass.Thrown;

                // Mounted MG42 (dummy weapon used while on the gun)
                case GameConst.WP_DUMMY_MG42:
                    return AnimWeaponClass.MG;

                default:
                    return AnimWeaponClass.None;
            }
        }

        // -----------------------------------------------------------------------
        // Private helpers — script parsing
        // -----------------------------------------------------------------------

        /// <summary>
        /// Tokenise script text into a flat list.
        /// Strips // line comments; splits on whitespace; keeps '{' and '}' as
        /// individual tokens so brace-counting is straightforward.
        /// </summary>
        private static List<string> Tokenise(string text)
        {
            var tokens = new List<string>(512);
            int i = 0;
            int n = text.Length;

            while (i < n)
            {
                char c = text[i];

                // Skip whitespace.
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                {
                    i++;
                    continue;
                }

                // Line comment.
                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    // Advance to end of line.
                    while (i < n && text[i] != '\n')
                        i++;
                    continue;
                }

                // Braces are always single-character tokens.
                if (c == '{' || c == '}')
                {
                    tokens.Add(c.ToString());
                    i++;
                    continue;
                }

                // Regular token — read until whitespace or brace.
                int start = i;
                while (i < n && text[i] != ' ' && text[i] != '\t' &&
                       text[i] != '\r' && text[i] != '\n' &&
                       text[i] != '{' && text[i] != '}')
                {
                    i++;
                }
                tokens.Add(text.Substring(start, i - start));
            }

            return tokens;
        }

        /// <summary>
        /// Skip a brace-delimited block starting at <paramref name="pos"/>.
        /// <paramref name="pos"/> should point at the first token AFTER the opening '{',
        /// or at the '{' itself.  Returns the position after the matching '}'.
        /// </summary>
        private static int SkipBlock(List<string> tokens, int pos)
        {
            // If we're sitting on a '{', consume it.
            if (pos < tokens.Count && tokens[pos] == "{")
                pos++;

            int depth = 1;
            while (pos < tokens.Count && depth > 0)
            {
                if (tokens[pos] == "{") depth++;
                else if (tokens[pos] == "}") depth--;
                pos++;
            }
            return pos;
        }

        /// <summary>
        /// Parse the contents of an "animations { … }" section (between the braces).
        /// Handles the "state RELAXED { … }" shell and descends into movement-type blocks.
        /// Returns position after the closing '}'.
        /// </summary>
        private static int ParseAnimationsBlock(List<string> tokens, int pos, AnimModelInfo model)
        {
            while (pos < tokens.Count && tokens[pos] != "}")
            {
                string tok = tokens[pos];

                // "state RELAXED { … }" — consume header and descend.
                if (tok.Equals("state", StringComparison.OrdinalIgnoreCase))
                {
                    pos++; // skip "state"
                    if (pos < tokens.Count)
                        pos++; // skip state name (RELAXED / QUERY / etc.)
                    if (pos < tokens.Count && tokens[pos] == "{")
                    {
                        pos++; // consume '{'
                        // Recurse — state block contents have the same structure.
                        pos = ParseAnimationsBlock(tokens, pos, model);
                        if (pos < tokens.Count && tokens[pos] == "}")
                            pos++;
                    }
                    continue;
                }

                // Movement-type keyword.
                if (s_moveTypeKeywords.TryGetValue(tok, out AnimMoveType moveType))
                {
                    pos++;
                    if (pos < tokens.Count && tokens[pos] == "{")
                    {
                        pos++;
                        pos = ParseMoveTypeBlock(tokens, pos, model, moveType);
                        if (pos < tokens.Count && tokens[pos] == "}")
                            pos++;
                    }
                    continue;
                }

                // Unknown token at this level — skip it.
                pos++;
            }

            return pos;
        }

        /// <summary>
        /// Parse the interior of a movement-type block, e.g.:
        /// <code>
        ///   weapon_class rifle
        ///   on_ground true
        ///   {
        ///       legs  LEGS_IDLE
        ///       torso TORSO_IDLE
        ///   }
        /// </code>
        /// or the named sub-format:
        /// <code>
        ///   conditions { … }
        ///   animations { … }
        /// </code>
        /// Both formats produce one AnimScriptItem added to model.Scripts[moveType].
        /// Returns position pointing at the closing '}' of the outer block.
        /// </summary>
        private static int ParseMoveTypeBlock(
            List<string> tokens, int pos, AnimModelInfo model, AnimMoveType moveType)
        {
            int scriptIdx = (int)moveType;
            if (scriptIdx < 0 || scriptIdx >= model.Scripts.Length)
                return SkipBlock(tokens, pos - 1); // -1: opening '{' already consumed

            AnimScript script = model.Scripts[scriptIdx];

            // Accumulate conditions until we hit a '{' (inline format) or the
            // named "conditions" / "animations" sub-blocks.
            while (pos < tokens.Count && tokens[pos] != "}")
            {
                string tok = tokens[pos];

                if (tok == "{")
                {
                    // Inline format: conditions precede a '{' animation block.
                    // Build an item using any conditions we found this pass; then
                    // parse body-part assignments from inside the brace block.
                    pos++;
                    var item = new AnimScriptItem();
                    pos = ParseInlineAnimBlock(tokens, pos, item);
                    if (pos < tokens.Count && tokens[pos] == "}")
                        pos++; // consume inner '}'

                    // Apply any pending conditions (parsed before the '{').
                    // (Conditions were not pre-parsed here; items start empty and
                    //  conditions must come from the named sub-block variant.)
                    script.Items.Add(item);
                    continue;
                }

                // Named sub-block: "conditions { … }"
                if (tok.Equals("conditions", StringComparison.OrdinalIgnoreCase))
                {
                    pos++;
                    var item = new AnimScriptItem();
                    if (pos < tokens.Count && tokens[pos] == "{")
                    {
                        pos++;
                        pos = ParseConditionsBlock(tokens, pos, item);
                        if (pos < tokens.Count && tokens[pos] == "}")
                            pos++;
                    }

                    // Expect "animations { … }" to follow.
                    if (pos < tokens.Count &&
                        tokens[pos].Equals("animations", StringComparison.OrdinalIgnoreCase))
                    {
                        pos++;
                        if (pos < tokens.Count && tokens[pos] == "{")
                        {
                            pos++;
                            pos = ParseInlineAnimBlock(tokens, pos, item);
                            if (pos < tokens.Count && tokens[pos] == "}")
                                pos++;
                        }
                    }

                    script.Items.Add(item);
                    continue;
                }

                // Inline condition token: e.g. "weapon_class rifle" / "on_ground true"
                if (s_conditionKeywords.TryGetValue(tok, out AnimCondition condType))
                {
                    pos++;
                    // The next token is the condition value.
                    int value = 1; // default for boolean conditions
                    if (pos < tokens.Count && tokens[pos] != "{" && tokens[pos] != "}")
                    {
                        string valToken = tokens[pos];
                        pos++;

                        if (condType == AnimCondition.WeaponClass)
                        {
                            if (s_weaponClassKeywords.TryGetValue(valToken, out AnimWeaponClass wc))
                                value = (int)wc;
                        }
                        else
                        {
                            // Boolean: "true"/"1" → 1, "false"/"0" → 0.
                            if (valToken.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                                valToken == "0")
                                value = 0;
                            else
                                value = 1;
                        }
                    }

                    // Peek: if the very next token is '{', start a new item with this condition.
                    if (pos < tokens.Count && tokens[pos] == "{")
                    {
                        pos++; // consume '{'
                        var item = new AnimScriptItem();
                        item.Conditions = new AnimScriptCondition[]
                        {
                            new AnimScriptCondition { Condition = condType, Value = value },
                        };
                        pos = ParseInlineAnimBlock(tokens, pos, item);
                        if (pos < tokens.Count && tokens[pos] == "}")
                            pos++;
                        script.Items.Add(item);
                    }
                    // Otherwise the condition is context-less — we skip it (no item created).
                    continue;
                }

                // Anything else — skip.
                pos++;
            }

            return pos;
        }

        /// <summary>
        /// Parse a "conditions { … }" block, populating item.Conditions.
        /// Returns position pointing at the closing '}'.
        /// </summary>
        private static int ParseConditionsBlock(List<string> tokens, int pos, AnimScriptItem item)
        {
            var conds = new List<AnimScriptCondition>();

            while (pos < tokens.Count && tokens[pos] != "}")
            {
                string tok = tokens[pos];

                if (s_conditionKeywords.TryGetValue(tok, out AnimCondition condType))
                {
                    pos++;
                    int value = 1;

                    if (pos < tokens.Count && tokens[pos] != "}" && tokens[pos] != "{")
                    {
                        string valToken = tokens[pos];
                        pos++;

                        if (condType == AnimCondition.WeaponClass)
                        {
                            if (s_weaponClassKeywords.TryGetValue(valToken, out AnimWeaponClass wc))
                                value = (int)wc;
                        }
                        else
                        {
                            if (valToken.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                                valToken == "0")
                                value = 0;
                        }
                    }

                    conds.Add(new AnimScriptCondition { Condition = condType, Value = value });
                    continue;
                }

                pos++;
            }

            item.Conditions = conds.ToArray();
            return pos;
        }

        /// <summary>
        /// Parse body-part → clip-name assignments between matching braces, e.g.:
        /// <code>
        ///   legs  LEGS_IDLE
        ///   torso TORSO_IDLE
        ///   head  HEAD_IDLE
        /// </code>
        /// Populates item.Animations.  Returns position pointing at the closing '}'.
        /// </summary>
        private static int ParseInlineAnimBlock(List<string> tokens, int pos, AnimScriptItem item)
        {
            while (pos < tokens.Count && tokens[pos] != "}")
            {
                string tok = tokens[pos];

                int partIndex = -1;
                if (tok.Equals("legs",  StringComparison.OrdinalIgnoreCase)) partIndex = (int)AnimBodyPart.Legs;
                else if (tok.Equals("torso", StringComparison.OrdinalIgnoreCase)) partIndex = (int)AnimBodyPart.Torso;
                else if (tok.Equals("head",  StringComparison.OrdinalIgnoreCase)) partIndex = (int)AnimBodyPart.Head;

                if (partIndex >= 0)
                {
                    pos++;
                    if (pos < tokens.Count && tokens[pos] != "}" && tokens[pos] != "{")
                    {
                        item.Animations[partIndex] = tokens[pos];
                        pos++;
                    }
                    continue;
                }

                pos++;
            }

            return pos;
        }

        // -----------------------------------------------------------------------
        // Private helpers — animation index resolution
        // -----------------------------------------------------------------------

        /// <summary>
        /// Resolve all animation name strings in model.Scripts to integer indices
        /// into model.Animations (BG_AnimationIndexForString equivalent).
        /// Unresolved names leave AnimNums[part] == -1.
        /// </summary>
        private static void ResolveAnimNums(AnimModelInfo model)
        {
            // Build a fast name→index lookup.
            var nameToIndex = new Dictionary<string, int>(
                model.Animations.Count, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < model.Animations.Count; i++)
                nameToIndex[model.Animations[i].Name] = i;

            foreach (AnimScript script in model.Scripts)
            {
                if (script == null) continue;
                foreach (AnimScriptItem item in script.Items)
                {
                    for (int part = 0; part < (int)AnimBodyPart.Count; part++)
                    {
                        string name = item.Animations[part];
                        if (!string.IsNullOrEmpty(name))
                        {
                            item.AnimNums[part] = nameToIndex.TryGetValue(name, out int idx)
                                ? idx : -1;
                        }
                    }
                }
            }
        }

        // -----------------------------------------------------------------------
        // Private helpers — condition evaluation
        // -----------------------------------------------------------------------

        /// <summary>
        /// EvaluateConditions — returns true if every condition in the item passes
        /// against the supplied AnimConditionState.
        /// Mirrors BG_EvaluateConditions from bg_animation.c.
        /// </summary>
        private static bool EvaluateConditions(AnimScriptItem item, in AnimConditionState state)
        {
            if (item.Conditions == null || item.Conditions.Length == 0)
                return true; // default / unconditional item

            foreach (AnimScriptCondition cond in item.Conditions)
            {
                if (!ConditionPasses(cond, in state))
                    return false;
            }
            return true;
        }

        private static bool ConditionPasses(AnimScriptCondition cond, in AnimConditionState state)
        {
            switch (cond.Condition)
            {
                case AnimCondition.WeaponClass:
                    return (int)state.WeaponClass == cond.Value;

                case AnimCondition.OnGround:
                    return BoolToInt(state.OnGround) == cond.Value;

                case AnimCondition.InWater:
                    return BoolToInt(state.InWater) == cond.Value;

                case AnimCondition.Crouching:
                    return BoolToInt(state.Crouching) == cond.Value;

                case AnimCondition.Prone:
                    return BoolToInt(state.Prone) == cond.Value;

                case AnimCondition.Firing:
                    return BoolToInt(state.Firing) == cond.Value;

                case AnimCondition.Mounted:
                    return BoolToInt(state.Mounted) == cond.Value;

                case AnimCondition.Moving:
                    return BoolToInt(state.Moving) == cond.Value;

                default:
                    return true;
            }
        }

        private static int BoolToInt(bool b) => b ? 1 : 0;
    }
}
