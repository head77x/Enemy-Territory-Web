// Ports: cl_cin.c + cg_loadpanel.c + ui_loadpanel.c + ui_util.c
//
// ET cinematics use the RoQ format (id Software proprietary codec).
// In Unity, actual video playback is handled by Unity's VideoPlayer component;
// this layer manages the handle table, state machine, and draw events so the
// rest of the codebase can call CIN_PlayCinematic / CIN_RunCinematic / etc.
//
// Load panel (cg_loadpanel.c / ui_loadpanel.c): mission description + progress bar.
// UI utilities (ui_util.c): string/memory helpers already covered by C# BCL.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;
using ET.Game;

namespace ET.Client
{
    // =========================================================================
    // Cinematic status (e_status)
    // =========================================================================
    public enum CinStatus
    {
        FMV_IDLE    = 0,
        FMV_PLAY    = 1,
        FMV_EOF     = 2,
        FMV_ID_BLT  = 3,
        FMV_ID_IDLE = 4,
        FMV_LOOPED  = 5,
        FMV_ID_WAIT = 6,
    }

    [Flags]
    public enum CinSystemBits
    {
        CIN_system   = 1,
        CIN_loop     = 2,
        CIN_hold     = 4,
        CIN_silent   = 8,
        CIN_shader   = 16,
    }

    // =========================================================================
    // CinematicHandle — per-video state
    // =========================================================================
    public class CinematicHandle
    {
        public int       Index       = -1;
        public string    Filename    = "";
        public CinStatus Status      = CinStatus.FMV_IDLE;
        public int       X, Y, Width, Height;
        public bool      Looping;
        public bool      Silent;
        public bool      UseShader;
        public uint      StartTime;
        public uint      LastTime;
        public VideoPlayer Player;           // Unity VideoPlayer driving this cinematic
        public RenderTexture RenderTex;
    }

    // =========================================================================
    // CinematicSystem — cl_cin.c public interface
    // MAX_VIDEO_HANDLES = 16
    // =========================================================================
    public static class CinematicSystem
    {
        private const int MAX_VIDEO_HANDLES = 16;
        private static readonly CinematicHandle[] _handles =
            new CinematicHandle[MAX_VIDEO_HANDLES];
        private static int _currentHandle = -1;
        private static int _clHandle      = -1;    // handle reserved for loading screen

        // Draw event — subscriber renders the cinematic frame
        public static event Action<int, RenderTexture> OnDrawCinematic;

        static CinematicSystem()
        {
            for (int i = 0; i < MAX_VIDEO_HANDLES; i++)
                _handles[i] = new CinematicHandle { Index = i };
        }

        // CIN_CloseAllVideos
        public static void CloseAll()
        {
            for (int i = 0; i < MAX_VIDEO_HANDLES; i++)
                if (_handles[i].Status != CinStatus.FMV_IDLE)
                    Stop(i);
        }

        // CIN_PlayCinematic — opens a video file and returns a handle
        public static int Play(string filename, int x, int y, int w, int h,
                               CinSystemBits systemBits)
        {
            // Find a free slot
            int handle = -1;
            for (int i = 0; i < MAX_VIDEO_HANDLES; i++)
            {
                if (_handles[i].Status == CinStatus.FMV_IDLE)
                { handle = i; break; }
            }
            if (handle < 0)
            {
                Debug.LogError("CIN_PlayCinematic: no free handles");
                return -1;
            }

            var h_ = _handles[handle];
            h_.Filename  = filename;
            h_.X = x; h_.Y = y; h_.Width = w; h_.Height = h;
            h_.Looping   = (systemBits & CinSystemBits.CIN_loop)   != 0;
            h_.Silent    = (systemBits & CinSystemBits.CIN_silent)  != 0;
            h_.UseShader = (systemBits & CinSystemBits.CIN_shader)  != 0;
            h_.StartTime = (uint)(Time.time * 1000);
            h_.LastTime  = h_.StartTime;
            h_.Status    = CinStatus.FMV_PLAY;

            // Create Unity VideoPlayer on a hidden GameObject
            var go = new GameObject($"Cinematic_{handle}");
            go.hideFlags = HideFlags.HideAndDontSave;
            h_.Player = go.AddComponent<VideoPlayer>();
            h_.Player.playOnAwake = false;
            h_.Player.isLooping   = h_.Looping;
            h_.Player.renderMode  = VideoRenderMode.RenderTexture;
            h_.RenderTex          = new RenderTexture(512, 512, 0);
            h_.Player.targetTexture = h_.RenderTex;

            // Resolve path via FileSystem
            string absPath = FileSystem.Instance.GetRealPath(filename);
            if (!string.IsNullOrEmpty(absPath))
            {
                h_.Player.url = "file://" + absPath;
                h_.Player.Play();
            }
            else
            {
                Debug.LogWarning($"CIN_PlayCinematic: file not found: {filename}");
                h_.Status = CinStatus.FMV_EOF;
            }

            _currentHandle = handle;
            if ((systemBits & CinSystemBits.CIN_system) != 0)
                _clHandle = handle;

            return handle;
        }

        // CIN_StopCinematic
        public static CinStatus Stop(int handle)
        {
            if (!ValidHandle(handle)) return CinStatus.FMV_IDLE;
            var h = _handles[handle];
            if (h.Player != null)
            {
                h.Player.Stop();
                if (h.Player.gameObject != null)
                    UnityEngine.Object.DestroyImmediate(h.Player.gameObject);
                h.Player = null;
            }
            if (h.RenderTex != null)
            {
                h.RenderTex.Release();
                UnityEngine.Object.DestroyImmediate(h.RenderTex);
                h.RenderTex = null;
            }
            h.Status = CinStatus.FMV_IDLE;
            if (_clHandle == handle) _clHandle = -1;
            return CinStatus.FMV_IDLE;
        }

        // CIN_RunCinematic — advance playback state
        public static CinStatus Run(int handle)
        {
            if (!ValidHandle(handle)) return CinStatus.FMV_EOF;
            var h = _handles[handle];
            if (h.Status != CinStatus.FMV_PLAY) return h.Status;

            if (h.Player != null && !h.Player.isPlaying)
            {
                if (h.Looping)
                { h.Player.Play(); h.Status = CinStatus.FMV_LOOPED; }
                else
                    h.Status = CinStatus.FMV_EOF;
            }
            return h.Status;
        }

        // CIN_DrawCinematic
        public static void Draw(int handle)
        {
            if (!ValidHandle(handle)) return;
            var h = _handles[handle];
            if (h.Status == CinStatus.FMV_PLAY && h.RenderTex != null)
                OnDrawCinematic?.Invoke(handle, h.RenderTex);
        }

        // CIN_SetExtents
        public static void SetExtents(int handle, int x, int y, int w, int hh)
        {
            if (!ValidHandle(handle)) return;
            _handles[handle].X = x; _handles[handle].Y = y;
            _handles[handle].Width = w; _handles[handle].Height = hh;
        }

        // CIN_SetLooping
        public static void SetLooping(int handle, bool loop)
        {
            if (!ValidHandle(handle)) return;
            _handles[handle].Looping = loop;
            if (_handles[handle].Player != null)
                _handles[handle].Player.isLooping = loop;
        }

        // CIN_UploadCinematic — in Unity the VideoPlayer writes directly to RenderTexture
        public static void Upload(int handle) { /* no-op: VP → RenderTexture is automatic */ }

        private static bool ValidHandle(int h) =>
            h >= 0 && h < MAX_VIDEO_HANDLES && _handles[h] != null;

        public static CinematicHandle GetHandle(int h) =>
            ValidHandle(h) ? _handles[h] : null;
    }

    // =========================================================================
    // LoadPanel — cg_loadpanel.c + ui_loadpanel.c
    // Mission loading screen: map image, description, progress bar.
    // =========================================================================

    public class LoadPanelData
    {
        public string MapName        = "";
        public string CampaignName   = "";
        public string Description    = "";
        public string HeaderText     = "";
        public float  Progress;          // 0..1
        public Texture2D MapImage;
        public bool   Interactive;
    }

    public static class LoadPanel
    {
        public static bool Initialized  { get; private set; }
        public static LoadPanelData Data { get; } = new LoadPanelData();

        // Fired when data changes — Unity UI subscriber renders the panel
        public static event Action<LoadPanelData> OnRender;

        // BG_LoadLoadscreen / CG_LoadPanel_Init
        public static void Init(bool interactive)
        {
            Data.Interactive = interactive;
            Initialized      = true;
        }

        // Called by CGameInfo as loading progresses
        public static void UpdateProgress(float pct)
        {
            Data.Progress = Mathf.Clamp01(pct);
            OnRender?.Invoke(Data);
        }

        public static void SetMapName(string mapName)
        {
            Data.MapName = mapName;
            Data.MapImage = Resources.Load<Texture2D>($"levelshots/{mapName}");
        }

        public static void SetCampaignData(string campaignName, string description)
        {
            Data.CampaignName = campaignName;
            Data.Description  = description;
        }

        public static void SetHeader(string header) { Data.HeaderText = header; }

        // Render header text — panel_button render callback
        public static void RenderHeaderText(PanelButton btn)
        {
            // Unity subscriber interprets this
        }

        // Render percentage meter — 0..100%
        public static void RenderPercentageMeter(PanelButton btn)
        {
            // delegate to OnRender subscriber
        }

        // CG_DrawLoadPanel / UI_DrawLoadPanel
        public static void Draw() => OnRender?.Invoke(Data);

        public static void Shutdown() { Initialized = false; }
    }
}
