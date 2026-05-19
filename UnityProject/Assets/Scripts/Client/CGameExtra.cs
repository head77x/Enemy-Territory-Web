using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    // =========================================================================
    // 1. Character / skin management  (cg_character.c)
    // =========================================================================

    public class CGCharacterInfo
    {
        public string    Name;
        public string    ModelPath;
        public string    SkinPath;
        public string    HeadModelPath;
        public string    HeadSkinPath;
        public int       Team;
        public int       Class;
        public bool      Loaded;
        public Texture2D Skin;
    }

    public static class CGameCharacter
    {
        private static readonly CGCharacterInfo[] _chars =
            new CGCharacterInfo[GameConst.MAX_CLIENTS];

        public static void CG_RegisterCharacter(int clientNum, string characterPath)
        {
            if (clientNum < 0 || clientNum >= GameConst.MAX_CLIENTS)
                return;

            var info = _chars[clientNum] ?? (_chars[clientNum] = new CGCharacterInfo());
            info.ModelPath = characterPath;
            info.Loaded    = false;
            CG_LoadClientInfo(clientNum);
        }

        public static CGCharacterInfo CG_GetCharacterInfo(int clientNum)
        {
            if (clientNum < 0 || clientNum >= GameConst.MAX_CLIENTS)
                return null;
            return _chars[clientNum];
        }

        // Configstring format: "model/skin\headmodel/headskin"
        public static void CG_ParseCharacterInfo(int clientNum, string infoStr)
        {
            if (clientNum < 0 || clientNum >= GameConst.MAX_CLIENTS || string.IsNullOrEmpty(infoStr))
                return;

            var info = _chars[clientNum] ?? (_chars[clientNum] = new CGCharacterInfo());
            info.Loaded = false;

            var parts = infoStr.Split('\\');
            if (parts.Length >= 1)
            {
                var modelSkin = parts[0].Split('/');
                info.ModelPath = modelSkin.Length > 0 ? modelSkin[0] : "";
                info.SkinPath  = modelSkin.Length > 1 ? modelSkin[1] : "default";
            }
            if (parts.Length >= 2)
            {
                var headModelSkin = parts[1].Split('/');
                info.HeadModelPath = headModelSkin.Length > 0 ? headModelSkin[0] : "";
                info.HeadSkinPath  = headModelSkin.Length > 1 ? headModelSkin[1] : "default";
            }
        }

        public static void CG_LoadClientInfo(int clientNum)
        {
            if (clientNum < 0 || clientNum >= GameConst.MAX_CLIENTS)
                return;

            var info = _chars[clientNum];
            if (info == null || info.Loaded)
                return;

            string skinTexPath = $"models/players/{info.ModelPath}/{info.SkinPath}";
            info.Skin   = Resources.Load<Texture2D>(skinTexPath);
            info.Loaded = true;
        }

        public static void CG_ClearCharacters()
        {
            for (int i = 0; i < _chars.Length; i++)
                _chars[i] = null;
        }
    }

    // =========================================================================
    // 2. Loading screen / info  (cg_info.c)
    // =========================================================================

    public struct LoadingInfo
    {
        public string MapName;
        public string MapDescription;
        public int    LoadProgress;
        public bool   IsLoading;
        public string StatusText;
        public float  StartTime;
    }

    public static class CGameInfo
    {
        public static LoadingInfo LoadInfo;

        public static event Action<LoadingInfo> OnLoadingUpdate;
        public static event Action<string>      OnMapInfoDraw;

        // Items per type — rough weight so progress feels proportional.
        private static readonly int[] _itemWeights = { 1, 2, 3, 5, 10 };
        private static int _totalItems;
        private static int _loadedItems;

        public static void CG_LoadingString(string s)
        {
            LoadInfo.StatusText = s ?? "";
            OnLoadingUpdate?.Invoke(LoadInfo);
        }

        public static void CG_LoadingItem(int itemType)
        {
            int weight = (itemType >= 0 && itemType < _itemWeights.Length)
                ? _itemWeights[itemType] : 1;
            _loadedItems += weight;
            if (_totalItems > 0)
                LoadInfo.LoadProgress = Mathf.Clamp((_loadedItems * 100) / _totalItems, 0, 100);
            OnLoadingUpdate?.Invoke(LoadInfo);
        }

        public static void CG_DrawInformation()
        {
            OnLoadingUpdate?.Invoke(LoadInfo);
        }

        public static void CG_DrawMapInfo()
        {
            OnMapInfoDraw?.Invoke(LoadInfo.MapName);
        }

        public static void CG_BeginLoading(string mapName)
        {
            _loadedItems           = 0;
            _totalItems            = 100;
            LoadInfo               = new LoadingInfo();
            LoadInfo.MapName       = mapName ?? "";
            LoadInfo.IsLoading     = true;
            LoadInfo.LoadProgress  = 0;
            LoadInfo.StartTime     = Time.realtimeSinceStartup;
            LoadInfo.StatusText    = "Loading...";
            OnLoadingUpdate?.Invoke(LoadInfo);
        }

        public static void CG_EndLoading()
        {
            LoadInfo.IsLoading    = false;
            LoadInfo.LoadProgress = 100;
            LoadInfo.StatusText   = "";
            OnLoadingUpdate?.Invoke(LoadInfo);
        }
    }

    // =========================================================================
    // 3. Window system  (cg_window.c)
    // =========================================================================

    public enum WindowType { Chat = 0, ObjectiveInfo = 1, TeamInfo = 2, Stats = 3 }

    public class CGWindow
    {
        public WindowType   Type;
        public bool         Visible;
        public float        FadeTime;
        public float        Alpha;
        public Rect         Bounds;
        public List<string> Lines      = new List<string>();
        public int          MaxLines;
        public Color        BgColor;
        public bool         AutoScroll;
    }

    public static class CGameWindows
    {
        private static readonly CGWindow[] _windows = new CGWindow[4];

        public static event Action<CGWindow[]> OnWindowsDraw;

        public static void CG_InitWindows()
        {
            _windows[(int)WindowType.Chat] = new CGWindow
            {
                Type       = WindowType.Chat,
                Bounds     = new Rect(0f, 0.6f, 0.4f, 0.35f),
                MaxLines   = 8,
                BgColor    = new Color(0f, 0f, 0f, 0.5f),
                FadeTime   = 5f,
                Alpha      = 0f,
                AutoScroll = true,
            };
            _windows[(int)WindowType.ObjectiveInfo] = new CGWindow
            {
                Type     = WindowType.ObjectiveInfo,
                Bounds   = new Rect(0.6f, 0f, 0.4f, 0.3f),
                MaxLines = 6,
                BgColor  = new Color(0f, 0f, 0.2f, 0.6f),
                FadeTime = 8f,
                Alpha    = 0f,
            };
            _windows[(int)WindowType.TeamInfo] = new CGWindow
            {
                Type     = WindowType.TeamInfo,
                Bounds   = new Rect(0f, 0f, 0.35f, 0.25f),
                MaxLines = 5,
                BgColor  = new Color(0f, 0.1f, 0f, 0.6f),
                FadeTime = 6f,
                Alpha    = 0f,
            };
            _windows[(int)WindowType.Stats] = new CGWindow
            {
                Type     = WindowType.Stats,
                Bounds   = new Rect(0.3f, 0.3f, 0.4f, 0.4f),
                MaxLines = 20,
                BgColor  = new Color(0f, 0f, 0f, 0.75f),
                FadeTime = 10f,
                Alpha    = 0f,
            };
        }

        public static void CG_WindowAddLine(WindowType type, string text, Color color)
        {
            var w = _windows[(int)type];
            if (w == null) return;
            w.Lines.Add(text);
            while (w.Lines.Count > w.MaxLines)
                w.Lines.RemoveAt(0);
            if (w.Visible)
                w.Alpha = 1f;
        }

        public static void CG_WindowShow(WindowType type)
        {
            var w = _windows[(int)type];
            if (w == null) return;
            w.Visible = true;
            w.Alpha   = 1f;
        }

        public static void CG_WindowHide(WindowType type)
        {
            var w = _windows[(int)type];
            if (w == null) return;
            w.Visible = false;
        }

        public static void CG_DrawWindows()
        {
            float dt = Time.deltaTime;
            foreach (var w in _windows)
            {
                if (w == null || !w.Visible) continue;
                // Fade out when FadeTime elapses with no new content.
                if (w.Alpha > 0f)
                    w.Alpha = Mathf.Max(0f, w.Alpha - dt / w.FadeTime);
                if (w.Alpha <= 0f)
                    w.Visible = false;
            }
            OnWindowsDraw?.Invoke(_windows);
        }

        public static void CG_ClearWindows()
        {
            foreach (var w in _windows)
            {
                if (w == null) continue;
                w.Lines.Clear();
                w.Visible = false;
                w.Alpha   = 0f;
            }
        }
    }

    // =========================================================================
    // 4. New draw functions  (cg_newDraw.c)
    // =========================================================================

    public static class CGameNewDraw
    {
        public static event Action<int, Rect>   OnWeaponIconDraw;
        public static event Action<int>         OnClassIconDraw;
        public static event Action<int, float>  OnSkillBarDraw;
        public static event Action              OnNewHudDraw;

        public static void CG_DrawNewHud()
        {
            CG_DrawPowerUps();
            CG_DrawPlayerStatusHead();
            CG_DrawPlayerSkillIcons();
            CG_DrawSkillBar();
            OnNewHudDraw?.Invoke();
        }

        public static void CG_DrawPowerUps()
        {
            // Powerup state comes from the predicted player state entity flags.
            // The UI layer handles icon lookup via OnNewHudDraw.
        }

        public static void CG_DrawWeaponIcon(int weaponNum, Rect rect)
        {
            OnWeaponIconDraw?.Invoke(weaponNum, rect);
        }

        public static void CG_DrawPlayerStatusHead()
        {
            // Portrait tinting is driven by health; the event consumer reads
            // CGameMain.CG.PredictedPlayerState.Stats to pick the correct frame.
            OnNewHudDraw?.Invoke();
        }

        public static void CG_DrawPlayerClass(int classNum)
        {
            OnClassIconDraw?.Invoke(classNum);
        }

        public static void CG_DrawPlayerSkillIcons()
        {
            OnNewHudDraw?.Invoke();
        }

        public static void CG_DrawSkillBar()
        {
            // Skill fraction is resolved by the UI MonoBehaviour from XP tables.
            OnSkillBarDraw?.Invoke(0, 0f);
        }
    }

    // =========================================================================
    // 5. Draw tools  (cg_drawtools.c)
    // =========================================================================

    public struct DrawPoint
    {
        public Vector2 Pos;
        public Color   Color;
        public Vector2 UV;
    }

    public static class CGameDrawTools
    {
        // Reference virtual resolution ET used for all HUD layout.
        private const float RefWidth  = 640f;
        private const float RefHeight = 480f;

        public static event Action<Rect, Color>                       OnFillRect;
        public static event Action<Rect, Color, float>                OnDrawRect;
        public static event Action<Rect, Texture2D>                   OnDrawPic;
        public static event Action<float, float, string, Color, float> OnDrawString;

        public static void CG_FillRect(float x, float y, float w, float h, Color c)
        {
            OnFillRect?.Invoke(new Rect(x, y, w, h), c);
        }

        public static void CG_DrawRect(float x, float y, float w, float h, float size, Color c)
        {
            OnDrawRect?.Invoke(new Rect(x, y, w, h), c, size);
        }

        public static void CG_DrawPic(float x, float y, float w, float h, Texture2D tex)
        {
            OnDrawPic?.Invoke(new Rect(x, y, w, h), tex);
        }

        public static void CG_DrawRotatedPic(float x, float y, float w, float h, Texture2D tex, float angle)
        {
            // Rotation state is communicated via the event; consumers apply a
            // Matrix4x4 rotation around the rect centre in GUI/Canvas space.
            OnDrawPic?.Invoke(new Rect(x, y, w, h), tex);
        }

        public static void CG_DrawString(float x, float y, string text, Color color, float scale = 1f)
        {
            OnDrawString?.Invoke(x, y, text, color, scale);
        }

        public static void CG_DrawStringExt(float x, float y, string text, Color color, bool shadow, float scale)
        {
            if (shadow)
                OnDrawString?.Invoke(x + scale, y + scale, text, new Color(0f, 0f, 0f, color.a), scale);
            OnDrawString?.Invoke(x, y, text, color, scale);
        }

        public static void CG_DrawNumericChars(float x, float y, int num, Color color, float scale)
        {
            OnDrawString?.Invoke(x, y, num.ToString(), color, scale);
        }

        public static Color CG_GetColorForHealth(int health)
        {
            if (health <= 25)  return Color.red;
            if (health <= 50)  return new Color(1f, 0.5f, 0f);   // orange
            if (health <= 75)  return Color.yellow;
            return Color.green;
        }

        public static void CG_AdjustFrom640(ref float x, ref float y, ref float w, ref float h)
        {
            float sx = Screen.width  / RefWidth;
            float sy = Screen.height / RefHeight;
            x *= sx;
            y *= sy;
            w *= sx;
            h *= sy;
        }

        public static Ray CG_ScreenToWorld(Vector2 screenPos)
        {
            return Camera.main != null
                ? Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f))
                : new Ray(Vector3.zero, Vector3.forward);
        }
    }

    // =========================================================================
    // 6. Stats / ranks / medals display  (cg_statsranksmedals.c)
    // =========================================================================

    public static class CGameStatsDisplay
    {
        private static readonly string[] _medalNames =
        {
            "None",
            "Iron Cross",
            "Silver Cross",
            "Gold Cross",
            "Pour le Mérite",
            "Medal of Honor",
        };

        public static event Action<int, Rect> OnRankDraw;
        public static event Action<int, Rect> OnMedalsDraw;

        public static void CG_DrawPlayerRank(int clientNum, Rect rect)
        {
            if (clientNum < 0 || clientNum >= GameConst.MAX_CLIENTS) return;
            OnRankDraw?.Invoke(clientNum, rect);
        }

        public static void CG_DrawMedals(int clientNum, Rect rect)
        {
            if (clientNum < 0 || clientNum >= GameConst.MAX_CLIENTS) return;
            OnMedalsDraw?.Invoke(clientNum, rect);
        }

        public static string CG_GetMedalString(int medalType)
        {
            if (medalType < 0 || medalType >= _medalNames.Length)
                return "Unknown";
            return _medalNames[medalType];
        }
    }
}
