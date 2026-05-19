// Ported from: src/game/bg_classes.c, src/game/bg_character.c,
//              src/game/bg_campaign.c, src/game/bg_stats.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;

namespace ET.Game
{
    // =========================================================================
    // Enums
    // =========================================================================

    public enum PlayerClass
    {
        Soldier   = 0,
        Medic     = 1,
        Engineer  = 2,
        FieldOps  = 3,
        CovertOps = 4,
        Count     = 5,
    }

    public enum SkillType
    {
        BattleSense  = 0,
        Engineering  = 1,
        FirstAid     = 2,
        Signals      = 3,
        LightWeapons = 4,
        HeavyWeapons = 5,
        CovertOps    = 6,
        Count        = 7,
    }

    // =========================================================================
    // Data classes
    // =========================================================================

    public class ClassInfo
    {
        public PlayerClass Class;
        public string      Name;
        public string      ModelName;
        public string      HeadModelName;
        public float       MoveScale;
        public int[]       StartWeapons;
        public int         StartAmmo;
        public int         MaxHealth;
        public bool        CanRevive;
        public bool        CanConstruct;
        public bool        CanPlantDynamite;
        public bool        CanPlantLandmine;
        public bool        CanDisarm;
        public bool        HasBinoculars;
        public bool        HasMedPacks;
        public bool        HasAmmoPacks;
    }

    public class CharacterInfo
    {
        public string Name;
        public string ModelPath;
        public string SkinPath;
        public string HeadModelPath;
        public string HeadSkinPath;
        public string AnimScriptPath;
        public float  MaxChestFlexion;
        public string AnimationGroup;
        public string AnimationScript;
    }

    public class CampaignInfo
    {
        public string   Name;
        public string   ShortName;
        public string   Desc;
        public string   MapScripts;
        public string[] Maps;
        public int      MapCount;
        public int      CurrentMap;
        public int      AxisWins, AlliesWins;
        public bool     Active;
    }

    public class PlayerStats
    {
        public int   XP;
        public int[] SkillPoints = new int[(int)SkillType.Count];
        public int[] SkillLevel  = new int[(int)SkillType.Count];
        public int   Kills, Deaths, Revives, Constructions, Destructions;
        public int   ObjectivesCompleted;
    }

    // =========================================================================
    // GameClasses — bg_classes.c port
    // =========================================================================

    public static class GameClasses
    {
        // Team constants from bg_public.h
        private const int TEAM_AXIS   = 1;
        private const int TEAM_ALLIES = 2;

        private static ClassInfo[] _classInfo = new ClassInfo[(int)PlayerClass.Count];
        private static bool _initialized;

        public static ClassInfo BG_ClassInfoForClass(PlayerClass cls)
        {
            if (!_initialized) BG_InitClassInfo();
            int idx = (int)cls;
            if (idx < 0 || idx >= (int)PlayerClass.Count)
                idx = (int)PlayerClass.Soldier;
            return _classInfo[idx];
        }

        public static PlayerClass BG_PlayerClassForName(string name)
        {
            if (!_initialized) BG_InitClassInfo();
            if (string.IsNullOrEmpty(name)) return PlayerClass.Soldier;
            for (int i = 0; i < (int)PlayerClass.Count; i++)
            {
                if (string.Equals(_classInfo[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return (PlayerClass)i;
            }
            // legacy alias from BG_ClassTextToClass
            if (string.Equals(name, "lieutenant", StringComparison.OrdinalIgnoreCase))
                return PlayerClass.FieldOps;
            return PlayerClass.Soldier;
        }

        public static void BG_InitClassInfo()
        {
            _initialized = true;

            _classInfo[(int)PlayerClass.Soldier] = new ClassInfo
            {
                Class            = PlayerClass.Soldier,
                Name             = "Soldier",
                ModelName        = "multi/ksoldat",
                HeadModelName    = "multi/ksoldat_head",
                MoveScale        = 1.0f,
                StartWeapons     = new[]
                {
                    GameConst.WP_KNIFE,
                    GameConst.WP_LUGER,           // axis default; BG_GetStartingWeapons swaps per-team
                    GameConst.WP_MP40,
                    GameConst.WP_GRENADE_LAUNCHER,
                    GameConst.WP_PANZERFAUST,
                    GameConst.WP_FLAMETHROWER,
                    GameConst.WP_MORTAR,
                    GameConst.WP_MOBILE_MG42,
                },
                StartAmmo        = 90,
                MaxHealth        = 140,
                CanRevive        = false,
                CanConstruct     = false,
                CanPlantDynamite = false,
                CanPlantLandmine = false,
                CanDisarm        = false,
                HasBinoculars    = false,
                HasMedPacks      = false,
                HasAmmoPacks     = false,
            };

            _classInfo[(int)PlayerClass.Medic] = new ClassInfo
            {
                Class            = PlayerClass.Medic,
                Name             = "Medic",
                ModelName        = "multi/kmedic",
                HeadModelName    = "multi/kmedic_head",
                MoveScale        = 1.0f,
                StartWeapons     = new[]
                {
                    GameConst.WP_KNIFE,
                    GameConst.WP_LUGER,
                    GameConst.WP_MP40,
                    GameConst.WP_GRENADE_LAUNCHER,
                    GameConst.WP_MEDIC_SYRINGE,
                    GameConst.WP_MEDKIT,
                    GameConst.WP_MEDIC_ADRENALINE,
                },
                StartAmmo        = 60,
                MaxHealth        = 100,
                CanRevive        = true,
                CanConstruct     = false,
                CanPlantDynamite = false,
                CanPlantLandmine = false,
                CanDisarm        = false,
                HasBinoculars    = false,
                HasMedPacks      = true,
                HasAmmoPacks     = false,
            };

            _classInfo[(int)PlayerClass.Engineer] = new ClassInfo
            {
                Class            = PlayerClass.Engineer,
                Name             = "Engineer",
                ModelName        = "multi/kengineer",
                HeadModelName    = "multi/kengineer_head",
                MoveScale        = 1.0f,
                StartWeapons     = new[]
                {
                    GameConst.WP_KNIFE,
                    GameConst.WP_LUGER,
                    GameConst.WP_MP40,
                    GameConst.WP_GRENADE_LAUNCHER,
                    GameConst.WP_PLIERS,
                    GameConst.WP_DYNAMITE,
                    GameConst.WP_LANDMINE,
                    GameConst.WP_GPG40,
                    GameConst.WP_KAR98,
                },
                StartAmmo        = 60,
                MaxHealth        = 100,
                CanRevive        = false,
                CanConstruct     = true,
                CanPlantDynamite = true,
                CanPlantLandmine = false,
                CanDisarm        = true,
                HasBinoculars    = false,
                HasMedPacks      = false,
                HasAmmoPacks     = false,
            };

            _classInfo[(int)PlayerClass.FieldOps] = new ClassInfo
            {
                Class            = PlayerClass.FieldOps,
                Name             = "Field Ops",
                ModelName        = "multi/kfops",
                HeadModelName    = "multi/kfops_head",
                MoveScale        = 1.0f,
                StartWeapons     = new[]
                {
                    GameConst.WP_KNIFE,
                    GameConst.WP_LUGER,
                    GameConst.WP_MP40,
                    GameConst.WP_GRENADE_LAUNCHER,
                    GameConst.WP_BINOCULARS,
                    GameConst.WP_AMMO,
                    GameConst.WP_ARTY,
                    GameConst.WP_SMOKE_MARKER,
                },
                StartAmmo        = 60,
                MaxHealth        = 100,
                CanRevive        = false,
                CanConstruct     = false,
                CanPlantDynamite = false,
                CanPlantLandmine = false,
                CanDisarm        = false,
                HasBinoculars    = true,
                HasMedPacks      = false,
                HasAmmoPacks     = true,
            };

            _classInfo[(int)PlayerClass.CovertOps] = new ClassInfo
            {
                Class            = PlayerClass.CovertOps,
                Name             = "Covert Ops",
                ModelName        = "multi/kcvops",
                HeadModelName    = "multi/kcvops_head",
                MoveScale        = 1.0f,
                StartWeapons     = new[]
                {
                    GameConst.WP_KNIFE,
                    GameConst.WP_LUGER,
                    GameConst.WP_SILENCER,
                    GameConst.WP_STEN,
                    GameConst.WP_FG42,
                    GameConst.WP_FG42SCOPE,
                    GameConst.WP_K43,
                    GameConst.WP_K43_SCOPE,
                    GameConst.WP_GARAND,
                    GameConst.WP_GARAND_SCOPE,
                    GameConst.WP_GRENADE_LAUNCHER,
                    GameConst.WP_BINOCULARS,
                    GameConst.WP_LANDMINE,
                    GameConst.WP_SATCHEL,
                    GameConst.WP_SATCHEL_DET,
                    GameConst.WP_SMOKE_BOMB,
                    GameConst.WP_LOCKPICK,
                },
                StartAmmo        = 60,
                MaxHealth        = 100,
                CanRevive        = false,
                CanConstruct     = false,
                CanPlantDynamite = false,
                CanPlantLandmine = true,
                CanDisarm        = false,
                HasBinoculars    = true,
                HasMedPacks      = false,
                HasAmmoPacks     = false,
            };
        }

        public static bool BG_CanPlayerUseWeapon(PlayerClass cls, int weaponNum)
        {
            if (!_initialized) BG_InitClassInfo();
            ClassInfo info = BG_ClassInfoForClass(cls);
            if (info.StartWeapons == null) return false;
            foreach (int w in info.StartWeapons)
                if (w == weaponNum) return true;
            return false;
        }

        // Returns weapons available to a class on a given team.
        // axis = TEAM_AXIS(1), allies = TEAM_ALLIES(2).
        // The swap is: axis pistol=luger/SMG=mp40, allies pistol=colt/SMG=thompson.
        public static int[] BG_GetStartingWeapons(PlayerClass cls, int team)
        {
            if (!_initialized) BG_InitClassInfo();
            int[] raw = BG_ClassInfoForClass(cls).StartWeapons;
            if (raw == null) return Array.Empty<int>();

            if (team == TEAM_ALLIES)
            {
                int[] allied = new int[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                {
                    int w = raw[i];
                    if      (w == GameConst.WP_LUGER)    w = GameConst.WP_COLT;
                    else if (w == GameConst.WP_MP40)     w = GameConst.WP_THOMPSON;
                    else if (w == GameConst.WP_SILENCER) w = GameConst.WP_SILENCED_COLT;
                    else if (w == GameConst.WP_K43)      w = GameConst.WP_GARAND;
                    else if (w == GameConst.WP_K43_SCOPE)w = GameConst.WP_GARAND_SCOPE;
                    else if (w == GameConst.WP_KAR98)    w = GameConst.WP_CARBINE;
                    else if (w == GameConst.WP_GPG40)    w = GameConst.WP_M7;
                    allied[i] = w;
                }
                return allied;
            }

            // axis — return as-is
            int[] copy = new int[raw.Length];
            Array.Copy(raw, copy, raw.Length);
            return copy;
        }
    }

    // =========================================================================
    // GameCharacters — bg_character.c port
    // =========================================================================

    public static class GameCharacters
    {
        private static Dictionary<string, CharacterInfo> _characters = new Dictionary<string, CharacterInfo>(StringComparer.OrdinalIgnoreCase);

        public static CharacterInfo BG_FindCharacter(string characterPath)
        {
            if (_characters.TryGetValue(characterPath, out CharacterInfo cached))
                return cached;
            return BG_RegisterCharacter(characterPath);
        }

        public static CharacterInfo BG_ParseCharacterFile(string filename, string fileText)
        {
            CharacterInfo info = new CharacterInfo { Name = filename };
            if (string.IsNullOrEmpty(fileText)) return info;

            string[] lines = fileText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            bool inBlock = false;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("//")) continue;
                if (line == "characterDef" || line == "{") { inBlock = line == "{"; continue; }
                if (line == "}") break;
                if (!inBlock) continue;

                int sep = line.IndexOf(' ');
                if (sep < 0) continue;
                string key = line.Substring(0, sep).Trim().ToLowerInvariant();
                string val = line.Substring(sep + 1).Trim().Trim('"');

                switch (key)
                {
                    case "mesh":              info.ModelPath      = val; break;
                    case "skin":              info.SkinPath       = val; break;
                    case "hudhead":           info.HeadModelPath  = val; break;
                    case "hudheadskin":       info.HeadSkinPath   = val; break;
                    case "animationscript":   info.AnimScriptPath = val; break;
                    case "maxchestflexion":
                        float.TryParse(val, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out info.MaxChestFlexion);
                        break;
                }
            }
            return info;
        }

        public static CharacterInfo BG_RegisterCharacter(string characterPath)
        {
            if (_characters.TryGetValue(characterPath, out CharacterInfo existing))
                return existing;

            string fileText = FileSystem.FS_ReadFileText(characterPath);
            CharacterInfo info;
            if (fileText != null)
            {
                info = BG_ParseCharacterFile(characterPath, fileText);
            }
            else
            {
                Debug.LogWarning($"[GameCharacters] Could not load: {characterPath}");
                info = new CharacterInfo { Name = characterPath };
            }

            _characters[characterPath] = info;
            return info;
        }
    }

    // =========================================================================
    // GameCampaign — bg_campaign.c port
    // =========================================================================

    public static class GameCampaign
    {
        public const int MAX_CAMPAIGNS = 16;

        private static CampaignInfo[] _campaigns = new CampaignInfo[MAX_CAMPAIGNS];
        private static int _numCampaigns = 0;

        public static void BG_LoadCampaigns()
        {
            _numCampaigns = 0;
            string[] files = FileSystem.FS_GetFileList("scripts/campaigns", ".campaign");
            foreach (string path in files)
            {
                if (_numCampaigns >= MAX_CAMPAIGNS) break;
                string text = FileSystem.FS_ReadFileText(path);
                if (text == null) continue;
                CampaignInfo info = BG_ParseCampaignFile(path, text);
                if (info != null)
                    _campaigns[_numCampaigns++] = info;
            }
        }

        public static CampaignInfo BG_FindCampaign(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < _numCampaigns; i++)
            {
                if (string.Equals(_campaigns[i].ShortName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(_campaigns[i].Name,      name, StringComparison.OrdinalIgnoreCase))
                    return _campaigns[i];
            }
            return null;
        }

        public static CampaignInfo BG_ParseCampaignFile(string filename, string fileText)
        {
            CampaignInfo info = new CampaignInfo();
            if (string.IsNullOrEmpty(fileText)) return null;

            string[] lines = fileText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            bool inBlock = false;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("//")) continue;
                if (line == "{") { inBlock = true; continue; }
                if (line == "}") break;
                if (!inBlock && line != "campaignDef") continue;
                if (line == "campaignDef") continue;

                int sep = line.IndexOf(' ');
                if (sep < 0) continue;
                string key = line.Substring(0, sep).Trim().ToLowerInvariant();
                string val = line.Substring(sep + 1).Trim().Trim('"');

                switch (key)
                {
                    case "name":       info.Name       = val; break;
                    case "shortname":  info.ShortName  = val; break;
                    case "desc":       info.Desc       = val; break;
                    case "maps":
                        info.MapScripts = val;
                        info.Maps       = val.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        info.MapCount   = info.Maps.Length;
                        break;
                }
            }

            if (string.IsNullOrEmpty(info.ShortName))
                info.ShortName = System.IO.Path.GetFileNameWithoutExtension(filename);

            return info;
        }

        public static int BG_GetCampaignMapCount(CampaignInfo campaign)
        {
            if (campaign == null) return 0;
            return campaign.MapCount;
        }

        // winner: TEAM_AXIS=1, TEAM_ALLIES=2
        public static void BG_AdvanceCampaign(CampaignInfo campaign, int winner)
        {
            if (campaign == null) return;

            if (winner == 1) campaign.AxisWins++;
            else if (winner == 2) campaign.AlliesWins++;

            campaign.CurrentMap++;
            if (campaign.CurrentMap >= campaign.MapCount)
            {
                campaign.Active     = false;
                campaign.CurrentMap = 0;
            }
        }
    }

    // =========================================================================
    // GameStats — bg_stats.c port
    // =========================================================================

    public static class GameStats
    {
        private static readonly int[] XP_Levels = { 0, 20, 50, 90, 140 };

        public static event Action<int, SkillType, int, int> OnSkillLevelUp;
        public static event Action<int, SkillType, int>      OnXPGained;

        public static int BG_GetSkillLevel(PlayerStats stats, SkillType skill)
        {
            if (stats == null) return 0;
            int xp  = stats.SkillPoints[(int)skill];
            int lvl = 0;
            for (int i = XP_Levels.Length - 1; i >= 0; i--)
            {
                if (xp >= XP_Levels[i]) { lvl = i; break; }
            }
            return lvl;
        }

        public static void BG_AddXP(PlayerStats stats, SkillType skill, int xp, int clientNum = -1)
        {
            if (stats == null || xp <= 0) return;

            stats.XP                       += xp;
            stats.SkillPoints[(int)skill]  += xp;

            int oldLevel = stats.SkillLevel[(int)skill];
            int newLevel = BG_GetSkillLevel(stats, skill);
            stats.SkillLevel[(int)skill] = newLevel;

            OnXPGained?.Invoke(clientNum, skill, xp);

            if (newLevel > oldLevel)
                OnSkillLevelUp?.Invoke(clientNum, skill, oldLevel, newLevel);
        }

        public static string BG_SkillName(SkillType skill)
        {
            switch (skill)
            {
                case SkillType.BattleSense:  return "Battle Sense";
                case SkillType.Engineering:  return "Engineering";
                case SkillType.FirstAid:     return "First Aid";
                case SkillType.Signals:      return "Signals";
                case SkillType.LightWeapons: return "Light Weapons";
                case SkillType.HeavyWeapons: return "Heavy Weapons";
                case SkillType.CovertOps:    return "Covert Ops";
                default:                     return "Unknown";
            }
        }

        public static PlayerClass BG_ClassForSkill(SkillType skill)
        {
            switch (skill)
            {
                case SkillType.HeavyWeapons: return PlayerClass.Soldier;
                case SkillType.FirstAid:     return PlayerClass.Medic;
                case SkillType.Engineering:  return PlayerClass.Engineer;
                case SkillType.Signals:      return PlayerClass.FieldOps;
                case SkillType.CovertOps:    return PlayerClass.CovertOps;
                default:                     return PlayerClass.Soldier;
            }
        }
    }
}
