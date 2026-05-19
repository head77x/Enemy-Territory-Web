// Lightweight Cvar system — replaces src/qcommon/cvar.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ET.Game
{
    /// <summary>
    /// Cvar flags matching the original CVAR_* defines.
    /// </summary>
    [Flags]
    public enum CvarFlags
    {
        None        = 0,
        Archive     = 1,    // saved to config file
        UserInfo    = 2,    // sent in userinfo to server
        ServerInfo  = 4,    // sent in serverinfo to clients
        SystemInfo  = 8,    // duplicated on all clients
        Init        = 16,   // set from command-line args only
        Latch       = 32,   // game restarted on change
        ReadOnly    = 64,   // hardcoded, never changed
        UserCreated = 128,  // created by user via console
        Temp        = 256,  // not saved
        Cheat       = 512,  // only changeable with cheats enabled
        NoRestart   = 1024, // don't restart on change
    }

    /// <summary>
    /// Single console variable.
    /// </summary>
    public sealed class Cvar
    {
        public string     Name;
        public string     StringValue;
        public string     LatchedString;   // pending value for LATCH cvars
        public string     DefaultString;
        public float      FloatValue;
        public int        IntValue;
        public CvarFlags  Flags;
        public bool       Modified;
        public int        ModificationCount;

        internal void UpdateNumeric()
        {
            float.TryParse(StringValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out FloatValue);
            IntValue = (int)FloatValue;
        }
    }

    /// <summary>
    /// Static cvar registry.  Drop-in replacement for Cvar_* functions.
    /// </summary>
    public static class CvarSystem
    {
        private static readonly Dictionary<string, Cvar> _vars =
            new Dictionary<string, Cvar>(StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------
        // Registration
        // ---------------------------------------------------------------
        public static Cvar Get(string name, string defaultValue, CvarFlags flags = CvarFlags.None)
        {
            if (_vars.TryGetValue(name, out var existing))
            {
                if ((flags & CvarFlags.Init) != 0)
                    existing.Flags |= flags;
                return existing;
            }

            var cv = new Cvar
            {
                Name          = name,
                StringValue   = defaultValue,
                DefaultString = defaultValue,
                LatchedString = null,
                Flags         = flags,
                Modified      = false,
                ModificationCount = 0,
            };
            cv.UpdateNumeric();
            _vars[name] = cv;
            return cv;
        }

        // ---------------------------------------------------------------
        // Read
        // ---------------------------------------------------------------
        public static float  GetFloat (string name, float  def = 0f)  => Find(name)?.FloatValue  ?? def;
        public static int    GetInt   (string name, int    def = 0)   => Find(name)?.IntValue    ?? def;
        public static string GetString(string name, string def = "")  => Find(name)?.StringValue ?? def;

        public static Cvar Find(string name) =>
            _vars.TryGetValue(name, out var cv) ? cv : null;

        // ---------------------------------------------------------------
        // Write
        // ---------------------------------------------------------------
        public static void Set(string name, string value, bool force = false)
        {
            if (!_vars.TryGetValue(name, out var cv))
            {
                cv = Get(name, value);
                return;
            }

            if ((cv.Flags & CvarFlags.ReadOnly) != 0 && !force)
            {
                Debug.LogWarning($"Cvar '{name}' is read-only.");
                return;
            }

            if ((cv.Flags & CvarFlags.Latch) != 0 && !force)
            {
                cv.LatchedString = value;
                cv.Modified      = true;
                return;
            }

            if (cv.StringValue == value) return;

            cv.StringValue = value;
            cv.UpdateNumeric();
            cv.Modified = true;
            cv.ModificationCount++;
        }

        public static void SetValue(string name, float value) =>
            Set(name, value.ToString("G", System.Globalization.CultureInfo.InvariantCulture));

        public static void SetInt(string name, int value) =>
            Set(name, value.ToString());

        public static void Reset(string name)
        {
            if (_vars.TryGetValue(name, out var cv))
                Set(name, cv.DefaultString, force: true);
        }

        // Apply latched values (call at map change / game restart)
        public static void ApplyLatchedValues()
        {
            foreach (var cv in _vars.Values)
            {
                if (cv.LatchedString != null)
                {
                    cv.StringValue   = cv.LatchedString;
                    cv.LatchedString = null;
                    cv.UpdateNumeric();
                    cv.ModificationCount++;
                }
            }
        }

        // ---------------------------------------------------------------
        // Pre-registered game cvars (mirrors sv_*.c / g_*.c declarations)
        // ---------------------------------------------------------------
        public static readonly Cvar sv_fps             = Get("sv_fps",          "20",     CvarFlags.ServerInfo);
        public static readonly Cvar sv_timeout         = Get("sv_timeout",       "200",    CvarFlags.None);
        public static readonly Cvar sv_zombietime      = Get("sv_zombietime",    "2",      CvarFlags.None);
        public static readonly Cvar sv_maxclients      = Get("sv_maxclients",    "20",     CvarFlags.ServerInfo | CvarFlags.Latch);
        public static readonly Cvar sv_hostname        = Get("sv_hostname",      "ET-Server", CvarFlags.ServerInfo | CvarFlags.Archive);
        public static readonly Cvar sv_pure            = Get("sv_pure",          "0",      CvarFlags.ServerInfo);
        public static readonly Cvar sv_mapname         = Get("sv_mapname",       "unknown",CvarFlags.ServerInfo | CvarFlags.ReadOnly);
        public static readonly Cvar sv_maxRate         = Get("sv_maxRate",       "0",      CvarFlags.Archive | CvarFlags.ServerInfo);
        public static readonly Cvar sv_minPing         = Get("sv_minPing",       "0",      CvarFlags.Archive | CvarFlags.ServerInfo);
        public static readonly Cvar sv_maxPing         = Get("sv_maxPing",       "0",      CvarFlags.Archive | CvarFlags.ServerInfo);
        public static readonly Cvar sv_privateClients  = Get("sv_privateClients","0",      CvarFlags.ServerInfo);
        public static readonly Cvar sv_friendlyFire    = Get("sv_friendlyFire",  "1",      CvarFlags.ServerInfo | CvarFlags.Archive);
        public static readonly Cvar sv_maxlives        = Get("sv_maxlives",      "0",      CvarFlags.ServerInfo | CvarFlags.Archive);
        public static readonly Cvar sv_reloading       = Get("sv_reloading",     "0",      CvarFlags.None);
        public static readonly Cvar sv_floodProtect    = Get("sv_floodProtect",  "1",      CvarFlags.Archive | CvarFlags.ServerInfo);

        public static readonly Cvar g_gametype         = Get("g_gametype",       "2",      CvarFlags.ServerInfo | CvarFlags.Latch);
        public static readonly Cvar g_gravity          = Get("g_gravity",        "800",    CvarFlags.ServerInfo);
        public static readonly Cvar g_speed            = Get("g_speed",          "320",    CvarFlags.ServerInfo);
        public static readonly Cvar g_movespeed        = Get("g_movespeed",      "127",    CvarFlags.None);
        public static readonly Cvar g_knockback        = Get("g_knockback",      "1000",   CvarFlags.ServerInfo);
        public static readonly Cvar g_friendlyFire     = Get("g_friendlyFire",   "1",      CvarFlags.Archive);
        public static readonly Cvar g_teamForceBalance = Get("g_teamForceBalance","0",     CvarFlags.None);
        public static readonly Cvar g_warmup           = Get("g_warmup",         "60",     CvarFlags.Archive);
        public static readonly Cvar g_doWarmup         = Get("g_doWarmup",       "0",      CvarFlags.Archive);
        public static readonly Cvar g_restarted        = Get("g_restarted",      "0",      CvarFlags.ReadOnly);

        public static readonly Cvar pmove_fixed        = Get("pmove_fixed",      "0",      CvarFlags.None);
        public static readonly Cvar pmove_msec         = Get("pmove_msec",       "8",      CvarFlags.None);

        public static readonly Cvar com_maxfps         = Get("com_maxfps",       "125",    CvarFlags.Archive);
        public static readonly Cvar cl_maxpackets      = Get("cl_maxpackets",    "30",     CvarFlags.Archive);
        public static readonly Cvar cl_timeNudge       = Get("cl_timeNudge",     "0",      CvarFlags.Archive);
        public static readonly Cvar cl_smoothTime      = Get("cl_smoothTime",    "100",    CvarFlags.Archive);
    }
}
