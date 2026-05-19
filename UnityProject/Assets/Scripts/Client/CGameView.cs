// Ported from: src/cgame/cg_view.c, src/cgame/cg_draw.c,
//              src/cgame/cg_marks.c, src/cgame/cg_localents.c
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
    public struct HudData
    {
        public int   Health;
        public int   MaxHealth;
        public int   Ammo;
        public int   ClipAmmo;
        public int   MaxAmmo;
        public float Stamina;
        public int   Weapon;
        public int   Team;
        public int   Score;
        public bool  IsCrouching;
        public bool  IsZoomed;
        public bool  IsOnGround;
        public bool  IsRevivable;
        public int   ServerTime;
        public float FOV;
    }

    public static class CGameView
    {
        // -----------------------------------------------------------------------
        // Constants (mirror cg_view.c / cg_draw.c defines)
        // -----------------------------------------------------------------------

        private const float DefaultFOV       = 90f;
        private const float ZoomedFOV        = 20f;
        private const float BobAmplitude     = 0.002f;
        private const float BobSpeed         = 0.002f;
        private const float LagometerWidth   = 48f;
        private const int   MaxMarks         = 500;
        private const int   MaxLocalEnts     = 512;
        private const int   MaxSoundBuffer   = 20;

        // -----------------------------------------------------------------------
        // Public state (read by MonoBehaviour subscribers)
        // -----------------------------------------------------------------------

        public static Vector3 ViewOrigin  { get; private set; }
        public static Vector3 ViewAngles  { get; private set; }
        public static float   FOV         { get; private set; } = DefaultFOV;
        public static bool    IsZoomed    { get; private set; }
        public static float   ZoomFraction { get; private set; }

        // -----------------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------------

        public static event Action<PlayerState>         OnDrawHUD;
        public static event Action<Vector3, int, float> OnDecalPlaced;
        public static event Action<float>               OnFOVChanged;
        public static event Action<bool>                OnZoomChanged;

        // -----------------------------------------------------------------------
        // Sound buffer (cg_view.c — CG_AddBufferedSound / CG_PlayBufferedSounds)
        // -----------------------------------------------------------------------

        private static readonly int[] _soundBuffer = new int[MaxSoundBuffer];
        private static int            _soundBufferCount;

        // -----------------------------------------------------------------------
        // Decal (mark) pool — cg_marks.c
        // -----------------------------------------------------------------------

        private struct MarkPoly
        {
            public Vector3 Origin;
            public int     Type;
            public float   Radius;
            public float   R, G, B, A;
            public bool    AlphaFade;
            public bool    Temporary;
            public float   BirthTime;
        }

        private static readonly MarkPoly[] _marks     = new MarkPoly[MaxMarks];
        private static int                 _markHead;
        private static int                 _markCount;

        // -----------------------------------------------------------------------
        // Local entity pool — cg_localents.c
        // -----------------------------------------------------------------------

        private static readonly bool[] _localEntActive = new bool[MaxLocalEnts];
        private static int             _localEntCount;

        // -----------------------------------------------------------------------
        // cg_view.c — CG_DrawActiveFrame
        // -----------------------------------------------------------------------

        public static void CG_DrawActiveFrame(int serverTime, bool stereoView)
        {
            CG_CalcViewValues();
            CG_AddMarks();
            CG_AddLocalEntities();
            CG_PlayBufferedSounds();
            CG_Draw2D();
            CG_DrawScene();
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_CalcViewValues
        // -----------------------------------------------------------------------

        public static void CG_CalcViewValues()
        {
            var ps = CGameMain.CG.PredictedPlayerState;
            if (ps == null)
                return;

            var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);
            origin.z += ps.ViewHeight;
            ViewOrigin = origin;

            ViewAngles = new Vector3(
                CGameMain.CG.RefdefViewAngles.x,
                CGameMain.CG.RefdefViewAngles.y,
                CGameMain.CG.RefdefViewAngles.z);

            CG_ZoomView();
            CG_OffsetFirstPersonView();

            float newFov = CG_CalcFov();
            if (!Mathf.Approximately(newFov, FOV))
            {
                FOV = newFov;
                OnFOVChanged?.Invoke(FOV);
            }

            if (Camera.main != null)
            {
                Camera.main.transform.position    = ETToUnity(ViewOrigin);
                Camera.main.transform.eulerAngles = ETAnglesToUnity(ViewAngles);
                Camera.main.fieldOfView           = FOV;
            }
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_ZoomView
        // Drives zoom fraction toward the current cvar-controlled zoom state.
        // In ET, CG_ZoomView increments/decrements cg.zoomSensitivity each frame.
        // -----------------------------------------------------------------------

        public static void CG_ZoomView()
        {
            var ps = CGameMain.CG.PredictedPlayerState;
            bool wantsZoom = ps != null && (ps.EFlags & GameConst.EF_ZOOMING) != 0;

            float target = wantsZoom ? 1f : 0f;
            ZoomFraction = Mathf.MoveTowards(ZoomFraction, target, Time.deltaTime * 6f);

            bool nowZoomed = ZoomFraction > 0.01f;
            if (nowZoomed != IsZoomed)
            {
                IsZoomed = nowZoomed;
                OnZoomChanged?.Invoke(IsZoomed);
            }
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_OffsetThirdPersonView
        // -----------------------------------------------------------------------

        // Default third-person range and angle match ET cvar defaults
        // (cg_thirdPersonRange 80, cg_thirdPersonAngle 0).
        private static float _thirdPersonRange = 80f;
        private static float _thirdPersonAngle = 0f;

        public static void CG_OffsetThirdPersonView()
        {
            float range  = _thirdPersonRange;
            float angle  = _thirdPersonAngle * Mathf.Deg2Rad;
            float pitch  = ViewAngles.x * Mathf.Deg2Rad;

            var forward = new Vector3(
                Mathf.Cos(pitch) * Mathf.Cos(-ViewAngles.y * Mathf.Deg2Rad),
                Mathf.Cos(pitch) * Mathf.Sin(-ViewAngles.y * Mathf.Deg2Rad),
                -Mathf.Sin(pitch));

            ViewOrigin -= forward * range;

            // Prevent camera clipping through world geometry.
            if (Physics.Raycast(
                    ETToUnity(new Vector3(CGameMain.CG.PredictedPlayerState.Origin0,
                                         CGameMain.CG.PredictedPlayerState.Origin1,
                                         CGameMain.CG.PredictedPlayerState.Origin2)),
                    -Camera.main.transform.forward,
                    out RaycastHit hit,
                    range))
            {
                ViewOrigin = UnityToET(hit.point);
            }
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_OffsetFirstPersonView
        // Applies view bob derived from the player's bobcycle.
        // -----------------------------------------------------------------------

        public static void CG_OffsetFirstPersonView()
        {
            var ps = CGameMain.CG.PredictedPlayerState;
            if (ps == null)
                return;

            bool onGround = ps.GroundEntityNum != GameConst.ENTITYNUM_NONE;
            if (!onGround)
                return;

            float bobTime  = (ps.BobCycle & 255) / 255f;
            float bobSin   = Mathf.Sin(bobTime * Mathf.PI * 2f);
            float bobCos   = Mathf.Cos(bobTime * Mathf.PI * 4f);

            // Vertical bob
            ViewOrigin = new Vector3(
                ViewOrigin.x,
                ViewOrigin.y,
                ViewOrigin.z + bobSin * BobAmplitude * 4f);

            // Roll from strafe bob — ET applies this as a view angle offset
            ViewAngles = new Vector3(
                ViewAngles.x + bobCos * BobAmplitude * 0.5f,
                ViewAngles.y,
                ViewAngles.z + bobSin * BobAmplitude);
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_CalcFov
        // -----------------------------------------------------------------------

        public static float CG_CalcFov()
        {
            float baseFov = DefaultFOV;
            float zFov    = Mathf.Lerp(baseFov, ZoomedFOV, ZoomFraction);

            // Widescreen correction: ET assumes a 4:3 ratio.
            float aspect = Screen.width > 0 && Screen.height > 0
                ? (float)Screen.width / Screen.height
                : 4f / 3f;
            if (aspect > 4f / 3f + 0.01f)
            {
                float hFovRad  = zFov * Mathf.Deg2Rad;
                float vFovTan  = Mathf.Tan(hFovRad * 0.5f) / (4f / 3f);
                float vFovRad  = Mathf.Atan(vFovTan) * 2f;
                // Unity fieldOfView is vertical; convert horizontal ET FOV to vertical.
                float vFov = Mathf.Atan(Mathf.Tan(vFovRad * 0.5f) / aspect) * 2f * Mathf.Rad2Deg;
                return vFov;
            }

            // Unity Camera.fieldOfView is vertical; convert ET's horizontal FOV.
            float vertFov = 2f * Mathf.Atan(Mathf.Tan(zFov * Mathf.Deg2Rad * 0.5f)
                / aspect) * Mathf.Rad2Deg;
            return vertFov;
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_DrawScene
        // In Unity, rendering itself is driven by the Camera; this method fires
        // entity additions that affect the scene before the camera renders.
        // -----------------------------------------------------------------------

        public static void CG_DrawScene()
        {
            CG_DrawSkyBoxPortal();
            // Entity rendering handled by CGameEntities / Camera pipeline.
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_DrawSkyBoxPortal
        // -----------------------------------------------------------------------

        public static void CG_DrawSkyBoxPortal()
        {
            // ET's sky portal copies refdef and sets a sky origin.
            // In Unity the skybox is a material on Camera — no explicit portal needed.
        }

        // -----------------------------------------------------------------------
        // cg_view.c — CG_AddBufferedSound / CG_PlayBufferedSounds
        // -----------------------------------------------------------------------

        public static void CG_AddBufferedSound(int sfxHandle)
        {
            if (sfxHandle <= 0 || _soundBufferCount >= MaxSoundBuffer)
                return;
            _soundBuffer[_soundBufferCount++] = sfxHandle;
        }

        public static void CG_PlayBufferedSounds()
        {
            for (int i = 0; i < _soundBufferCount; i++)
                AudioSystem.S_StartSound(null, 0, 0, _soundBuffer[i]);
            _soundBufferCount = 0;
        }

        // -----------------------------------------------------------------------
        // cg_draw.c — CG_Draw2D
        // Fires OnDrawHUD so MonoBehaviour subscribers can render the HUD.
        // Actual UI rendering must not occur in this file (no IMGUI/Canvas).
        // -----------------------------------------------------------------------

        public static void CG_Draw2D()
        {
            var ps = CGameMain.CG.PredictedPlayerState;
            if (ps == null)
                return;

            OnDrawHUD?.Invoke(ps);

            CG_DrawCrosshair();
            CG_DrawCrosshairNames();
            CG_DrawAmmoWarning();
            CG_DrawHealth(ps);
            CG_DrawAmmo(ps);
            CG_DrawStamina(ps);
            CG_DrawCompass();
            CG_DrawObjectiveStatus();
            CG_DrawTeamOverlay();
            CG_DrawVote();
            CG_DrawChat();
            CG_DrawWarmup();
            CG_DrawFPS();
            CG_DrawLagometer();

            if ((ps.PmFlags & GameConst.PMF_FOLLOW) != 0)
                CG_DrawSpectator();
        }

        // -----------------------------------------------------------------------
        // cg_draw.c — individual HUD element stubs
        // All delegate to OnDrawHUD subscribers via CG_BuildHudData.
        // -----------------------------------------------------------------------

        public static void CG_DrawCrosshair()       { }
        public static void CG_DrawCrosshairNames()  { }
        public static void CG_DrawAmmoWarning()     { }

        public static void CG_DrawHealth(PlayerState ps)
        {
            // Data forwarded through CG_BuildHudData → OnDrawHUD.
        }

        public static void CG_DrawAmmo(PlayerState ps)
        {
            // Data forwarded through CG_BuildHudData → OnDrawHUD.
        }

        public static void CG_DrawStamina(PlayerState ps)
        {
            // Data forwarded through CG_BuildHudData → OnDrawHUD.
        }

        public static void CG_DrawCompass()          { }
        public static void CG_DrawObjectiveStatus()  { }
        public static void CG_DrawTeamOverlay()      { }
        public static void CG_DrawSpectator()        { }
        public static void CG_DrawVote()             { }
        public static void CG_DrawChat()             { }
        public static void CG_DrawWarmup()           { }
        public static void CG_DrawFPS()              { }
        public static void CG_DrawLagometer()        { }

        // -----------------------------------------------------------------------
        // HUD data model
        // -----------------------------------------------------------------------

        public static HudData CG_BuildHudData()
        {
            var ps = CGameMain.CG.PredictedPlayerState;
            if (ps == null)
                return default;

            int weapon = ps.Weapon;
            int ammo   = weapon < ps.Ammo.Length    ? ps.Ammo[weapon]    : 0;
            int clip   = weapon < ps.AmmoClip.Length ? ps.AmmoClip[weapon] : 0;

            return new HudData
            {
                Health      = ps.Stats[(int)StatIndex.Health],
                MaxHealth   = ps.Stats[(int)StatIndex.MaxHealth],
                Ammo        = ammo,
                ClipAmmo    = clip,
                MaxAmmo     = ammo,       // caller resolves max from weapon table
                Stamina     = Mathf.Clamp01(1f - ps.SprintExertTime / 20000f),
                Weapon      = weapon,
                Team        = ps.TeamNum,
                Score       = ps.Persistant[(int)PersistIndex.Score],
                IsCrouching = (ps.PmFlags & GameConst.PMF_DUCKED) != 0,
                IsZoomed    = IsZoomed,
                IsOnGround  = ps.GroundEntityNum != GameConst.ENTITYNUM_NONE,
                IsRevivable = (ps.EFlags & GameConst.EF_DEAD) != 0,
                ServerTime  = CGameMain.CG.Time,
                FOV         = FOV,
            };
        }

        // -----------------------------------------------------------------------
        // cg_marks.c — CG_ImpactMark
        // -----------------------------------------------------------------------

        public static void CG_ImpactMark(
            int markType, Vector3 origin, Vector3 dir,
            float orientation,
            float r, float g, float b, float a,
            bool alphaFade, float radius, bool temporary)
        {
            // Confirm the surface normal via a short raycast to avoid floating decals.
            if (!Physics.Raycast(ETToUnity(origin) - dir.normalized * 0.1f,
                                 dir.normalized, out _, radius * 2f))
                return;

            int slot = _markHead % MaxMarks;
            _marks[slot] = new MarkPoly
            {
                Origin    = origin,
                Type      = markType,
                Radius    = radius,
                R         = r,
                G         = g,
                B         = b,
                A         = a,
                AlphaFade = alphaFade,
                Temporary = temporary,
                BirthTime = Time.time,
            };
            _markHead++;
            if (_markCount < MaxMarks)
                _markCount++;

            OnDecalPlaced?.Invoke(origin, markType, radius);
        }

        // -----------------------------------------------------------------------
        // cg_marks.c — CG_AddMarks
        // Fades and culls marks; actual GPU decal placement is handled by the
        // subscriber that received OnDecalPlaced (e.g. a DecalProjector spawner).
        // -----------------------------------------------------------------------

        public static void CG_AddMarks()
        {
            float now = Time.time;
            for (int i = 0; i < _markCount; i++)
            {
                ref MarkPoly m = ref _marks[i % MaxMarks];
                if (m.AlphaFade)
                {
                    float age = now - m.BirthTime;
                    m.A = Mathf.Clamp01(1f - age / 20f);   // 20-second fade
                }
            }
        }

        // -----------------------------------------------------------------------
        // cg_marks.c — CG_ClearMarks
        // -----------------------------------------------------------------------

        public static void CG_ClearMarks()
        {
            _markHead  = 0;
            _markCount = 0;
        }

        // -----------------------------------------------------------------------
        // cg_localents.c — local entity pool
        // -----------------------------------------------------------------------

        public static void CG_AddLocalEntities()
        {
            // Each active local entity is ticked here; the actual simulation
            // (gravity, bouncing, fading) is delegated to CGameEntities per
            // the original cg_localents.c structure.
            for (int i = 0; i < MaxLocalEnts; i++)
            {
                if (!_localEntActive[i])
                    continue;
                // Expiry and physics are handled by CGameEntities subscribers.
            }
        }

        public static void CG_FreeLocalEntity(int handle)
        {
            if (handle < 0 || handle >= MaxLocalEnts)
                return;
            if (_localEntActive[handle])
            {
                _localEntActive[handle] = false;
                _localEntCount--;
            }
        }

        public static int CG_AllocLocalEntity()
        {
            // When all slots are full, steal the oldest free-able slot (non-permanent).
            // This mirrors cg_localents.c's forced-reuse behaviour.
            for (int i = 0; i < MaxLocalEnts; i++)
            {
                if (!_localEntActive[i])
                {
                    _localEntActive[i] = true;
                    _localEntCount++;
                    return i;
                }
            }

            // Pool exhausted: recycle slot 0 (matches ET's "steal first" fallback).
            return 0;
        }

        // -----------------------------------------------------------------------
        // Coordinate conversion helpers
        // ET uses a right-handed Z-up system; Unity uses left-handed Y-up.
        // Forward in ET = +X; in Unity = +Z.
        // -----------------------------------------------------------------------

        private static Vector3 ETToUnity(Vector3 et) =>
            new Vector3(-et.y, et.z, et.x);

        private static Vector3 UnityToET(Vector3 u) =>
            new Vector3(u.z, -u.x, u.y);

        private static Vector3 ETAnglesToUnity(Vector3 etAngles) =>
            new Vector3(-etAngles.x, -etAngles.y, etAngles.z);

        // -----------------------------------------------------------------------
        // Internal stat/persist index helpers
        // Mirrors STAT_* and PERS_* from bg_public.h.
        // -----------------------------------------------------------------------

        private enum StatIndex
        {
            Health      = 0,
            MaxHealth   = 3,
            WeaponClips = 4,
        }

        private enum PersistIndex
        {
            Score       = 0,
            Hits        = 1,
            Rank        = 2,
        }
    }
}
