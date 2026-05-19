// Ports: cg_panelhandling.c + cg_missionbriefing.c
// Panel button system + campaign/arena briefing loader for the cgame layer.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Game;

namespace ET.Client
{
    // -------------------------------------------------------------------------
    // Panel button types — cg_panelhandling.c
    // -------------------------------------------------------------------------
    public enum PanelButtonType { None, Label, Edit, Button, Dropdown }

    public class PanelRect
    {
        public float X, Y, W, H;
    }

    public class PanelFont
    {
        public float ScaleX = 1f;
        public float ScaleY = 1f;
        public string FontName = "default";
    }

    public class PanelButton
    {
        public string       Text;
        public PanelButtonType Type;
        public PanelRect    Rect    = new PanelRect();
        public PanelFont    Font    = new PanelFont();
        public int[]        Data    = new int[8];
        public bool         Focused;

        // Callbacks
        public Func<PanelButton, int, bool> OnKeyDown;
        public Func<PanelButton, int, bool> OnKeyUp;
        public Action<PanelButton>          OnRender;
        public Action<PanelButton>          OnFinish;
    }

    // -------------------------------------------------------------------------
    // CGamePanelSystem — generic panel/button manager
    // -------------------------------------------------------------------------
    public class CGamePanelSystem
    {
        public static PanelButton FocusButton { get; private set; }

        private readonly List<PanelButton> _buttons = new List<PanelButton>();

        public void RegisterButton(PanelButton btn) => _buttons.Add(btn);

        // CG_DrawPanelButtons — render pass for one panel
        public void Draw()
        {
            foreach (var btn in _buttons)
            {
                btn.OnRender?.Invoke(btn);
            }
        }

        // Key event routing — returns true if consumed
        public bool KeyEvent(int key, bool down)
        {
            if (FocusButton == null) return false;
            bool consumed = down
                ? FocusButton.OnKeyDown?.Invoke(FocusButton, key) ?? false
                : FocusButton.OnKeyUp?.Invoke(FocusButton, key) ?? false;
            return consumed;
        }

        // Mouse-click hit test — sets focus
        public void MouseClick(float x, float y)
        {
            FocusButton = null;
            foreach (var btn in _buttons)
            {
                if (x >= btn.Rect.X && x <= btn.Rect.X + btn.Rect.W &&
                    y >= btn.Rect.Y && y <= btn.Rect.Y + btn.Rect.H)
                {
                    FocusButton = btn;
                    break;
                }
            }
        }

        // CG_PanelButton_RenderEdit helper data — blinking cursor
        public static bool ShowCursor(int cgTime) => (cgTime / 1000) % 2 == 0;

        // CG_PanelButton_RenderEdit — builds display string for editable field
        public static string BuildEditDisplayString(PanelButton btn, string value, int cgTime, bool overStrike)
        {
            string display = value;
            if (btn.Focused && ShowCursor(cgTime))
                display += overStrike ? "^0|" : "^0_";
            else
                display += " ";
            return display;
        }

        // Focus management
        public static void SetFocus(PanelButton btn) => FocusButton = btn;
        public static void ClearFocus()              => FocusButton = null;
    }

    // -------------------------------------------------------------------------
    // MapPosition — campaign map coordinate info
    // -------------------------------------------------------------------------
    public struct MapPosition
    {
        public float X, Y;
    }

    // -------------------------------------------------------------------------
    // ArenaInfo — parsed from scripts/<mapname>.arena
    // -------------------------------------------------------------------------
    public class ArenaInfo
    {
        public string LongName      = "";
        public string Description   = "";
        public string LmsDescription = "";
        public string AlliedWinText  = "";
        public string AxisWinText    = "";
        public MapPosition MapPos;
    }

    // -------------------------------------------------------------------------
    // CampaignMapInfo — one map entry in a campaign
    // -------------------------------------------------------------------------
    public class CampaignMapInfo
    {
        public string   MapName = "";
        public ArenaInfo Arena;
        public float[,] MapTC   = new float[2, 2];  // texture coords [corner][uv]
    }

    // -------------------------------------------------------------------------
    // CampaignInfo — parsed from scripts/*.campaign
    // -------------------------------------------------------------------------
    public class CampaignInfo
    {
        public string   ShortName   = "";
        public string   Name        = "";
        public string   Description = "";
        public List<CampaignMapInfo> Maps = new List<CampaignMapInfo>();
    }

    // -------------------------------------------------------------------------
    // CGameMissionBriefing — cg_missionbriefing.c
    // Loads campaign + arena briefing info for the pre-game / loading screen.
    // -------------------------------------------------------------------------
    public class CGameMissionBriefing
    {
        public CampaignInfo CurrentCampaign { get; private set; }
        public ArenaInfo    CurrentArena    { get; private set; }
        public bool         CampaignLoaded  => CurrentCampaign != null;
        public bool         ArenaLoaded     => CurrentArena    != null;

        // Fired when briefing data is ready; subscribers update the loading screen
        public event Action<CampaignInfo> OnCampaignLoaded;
        public event Action<ArenaInfo>    OnArenaLoaded;

        public CGameMissionBriefing() { }

        // CG_LocateCampaign — scans scripts/*.campaign for currentCampaign short name
        public void LocateCampaign(string currentCampaignShortName)
        {
            if (string.IsNullOrEmpty(currentCampaignShortName)) return;

            string[] files = FileSystem.FS_GetFileList("scripts", ".campaign");
            foreach (string f in files)
            {
                string path = "scripts/" + f;
                var info = ParseCampaignFile(path, currentCampaignShortName);
                if (info != null)
                {
                    // Load arena info for each map in campaign
                    foreach (var mi in info.Maps)
                    {
                        string arenaFile = $"scripts/{mi.MapName}.arena";
                        mi.Arena = ParseArenaFile(arenaFile, mi.MapName);
                        if (mi.Arena == null)
                        {
                            Debug.LogWarning(
                                $"CG_LocateCampaign: no arena info for {mi.MapName}");
                        }
                    }

                    CurrentCampaign = info;
                    OnCampaignLoaded?.Invoke(info);
                    return;
                }
            }
        }

        // CG_LocateArena — loads arena briefing for the current map
        public void LocateArena(string rawMapName)
        {
            if (string.IsNullOrEmpty(rawMapName)) return;

            string filename = $"scripts/{rawMapName}.arena";
            var info = ParseArenaFile(filename, rawMapName);
            if (info != null)
            {
                CurrentArena = info;
                OnArenaLoaded?.Invoke(info);
            }
        }

        // CG_DescriptionForCampaign
        public string GetCampaignDescription() => CurrentCampaign?.Description;

        // CG_NameForCampaign
        public string GetCampaignName() => CurrentCampaign?.Name;

        // ------------------------------------------------------------------ //
        // Parsers
        // ------------------------------------------------------------------ //

        // CG_FindCampaignInFile — brace-depth block parser for .campaign files
        private CampaignInfo ParseCampaignFile(string path, string shortName)
        {
            string text = FileSystem.ReadAllText(path);
            if (string.IsNullOrEmpty(text)) return null;

            var tokens = Tokenize(text);
            int pos = 0;

            if (!Consume(tokens, ref pos, "{")) return null;

            CampaignInfo result   = null;
            CampaignInfo current  = new CampaignInfo();

            while (pos < tokens.Count)
            {
                string tok = tokens[pos++];
                if (tok == "}")
                {
                    if (result != null)
                        return result;              // already found, done
                    if (current.ShortName.Equals(shortName, StringComparison.OrdinalIgnoreCase))
                        result = current;
                    current = new CampaignInfo();

                    // peek for next opening brace or eof
                    if (pos >= tokens.Count) break;
                    if (tokens[pos] == "{") pos++;
                    continue;
                }

                string key = tok.ToLower();
                if (pos >= tokens.Count) break;

                switch (key)
                {
                    case "shortname":
                        current.ShortName = tokens[pos++];
                        break;
                    case "name":
                        current.Name = tokens[pos++];
                        break;
                    case "description":
                        current.Description = tokens[pos++];
                        break;
                    case "next":
                    case "image":
                        pos++;   // skip value
                        break;
                    case "maps":
                    {
                        string mapList = tokens[pos++];
                        foreach (string part in mapList.Split(';'))
                        {
                            string m = part.Trim();
                            if (m.Length > 0) current.Maps.Add(new CampaignMapInfo { MapName = m });
                        }
                        break;
                    }
                    case "maptc":
                    {
                        // 2 floats — applied to the last added map
                        if (current.Maps.Count > 0 && pos + 1 < tokens.Count)
                        {
                            var mi = current.Maps[current.Maps.Count - 1];
                            if (float.TryParse(tokens[pos],   out float u)) mi.MapTC[0, 0] = u;
                            if (float.TryParse(tokens[pos+1], out float v)) mi.MapTC[0, 1] = v;
                            mi.MapTC[1, 0] = 650 + mi.MapTC[0, 0];
                            mi.MapTC[1, 1] = 650 + mi.MapTC[0, 1];
                            pos += 2;
                        }
                        break;
                    }
                    default:
                        pos++;  // unknown key, skip value
                        break;
                }
            }

            return result;
        }

        // CG_FindArenaInfo — brace-depth block parser for .arena files
        private ArenaInfo ParseArenaFile(string path, string mapName)
        {
            string text = FileSystem.ReadAllText(path);
            if (string.IsNullOrEmpty(text)) return null;

            var tokens = Tokenize(text);
            int pos = 0;

            if (!Consume(tokens, ref pos, "{")) return null;

            ArenaInfo result  = null;
            ArenaInfo current = new ArenaInfo();
            bool found        = false;

            while (pos < tokens.Count)
            {
                string tok = tokens[pos++];
                if (tok == "}")
                {
                    if (found)
                    {
                        result = current;
                        return result;
                    }
                    current = new ArenaInfo();
                    found   = false;
                    if (pos < tokens.Count && tokens[pos] == "{") pos++;
                    continue;
                }

                string key = tok.ToLower();
                if (pos >= tokens.Count) break;

                switch (key)
                {
                    case "map":
                        string mname = tokens[pos++];
                        if (string.Equals(mname, mapName, StringComparison.OrdinalIgnoreCase))
                            found = true;
                        break;
                    case "longname":
                        current.LongName = tokens[pos++];
                        break;
                    case "briefing":
                    case "description":
                        current.Description = tokens[pos++];
                        break;
                    case "lmsbriefing":
                        current.LmsDescription = tokens[pos++];
                        break;
                    case "alliedwintext":
                        current.AlliedWinText = tokens[pos++];
                        break;
                    case "axiswintext":
                        current.AxisWinText = tokens[pos++];
                        break;
                    case "mapposition_x":
                        if (float.TryParse(tokens[pos++], out float mx)) current.MapPos.X = mx;
                        break;
                    case "mapposition_y":
                        if (float.TryParse(tokens[pos++], out float my)) current.MapPos.Y = my;
                        break;
                    default:
                        pos++;   // objectives/type/timelimit etc — skip value
                        break;
                }
            }

            return result;
        }

        // Simple whitespace + quoted-string tokenizer (handles "quoted strings")
        private static List<string> Tokenize(string text)
        {
            var result = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                // skip whitespace and C-style // comments
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                if (i >= text.Length) break;
                if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }

                if (text[i] == '"')
                {
                    i++;
                    int start = i;
                    while (i < text.Length && text[i] != '"') i++;
                    result.Add(text.Substring(start, i - start));
                    if (i < text.Length) i++;
                }
                else if (text[i] == '{' || text[i] == '}')
                {
                    result.Add(text[i].ToString());
                    i++;
                }
                else
                {
                    int start = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i])
                           && text[i] != '{' && text[i] != '}') i++;
                    result.Add(text.Substring(start, i - start));
                }
            }
            return result;
        }

        private static bool Consume(List<string> tokens, ref int pos, string expected)
        {
            while (pos < tokens.Count && string.IsNullOrWhiteSpace(tokens[pos])) pos++;
            if (pos < tokens.Count && tokens[pos] == expected) { pos++; return true; }
            return false;
        }
    }
}
