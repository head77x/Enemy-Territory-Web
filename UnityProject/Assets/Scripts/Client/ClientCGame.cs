// Ported from: src/client/cl_cgame.c
using System;
using System.Collections.Generic;
using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Client
{
    /// <summary>
    /// Bridge between the client engine and the cgame layer.
    /// In the original C code all cgame calls cross a VM boundary via VM_Call.
    /// In this Unity port every VM_Call boundary is replaced with a C# event so
    /// that a MonoBehaviour (or any subscriber) can implement cgame logic without
    /// a separate shared library.
    /// </summary>
    public static class ClientCGame
    {
        // -------------------------------------------------------------------------
        // Events — replace VM_Call(cgvm, CG_*) boundaries
        // -------------------------------------------------------------------------
#pragma warning disable CS0067 // Events are part of the public cgame boundary API

        /// <summary>Raised when a new snapshot is available for the cgame to consume.</summary>
        public static event Action<ClientSnapshot> OnCGameSnapshot;

        /// <summary>
        /// Raised each client frame to drive cgame rendering.
        /// Parameters: (serverTime, demoPlayback)
        /// Replaces VM_Call(cgvm, CG_DRAW_ACTIVE_FRAME, serverTime, stereoView, demoPlayback).
        /// </summary>
        public static event Action<int, bool> OnCGameDrawActiveFrame;

        /// <summary>
        /// Raised when a server command is forwarded to cgame for execution.
        /// Parameters: (cmd, args)
        /// Replaces VM_Call(cgvm, CG_CONSOLE_COMMAND).
        /// </summary>
        public static event Action<string, string> OnCGameConsoleCommand;

        /// <summary>
        /// Raised when a key event is passed to cgame.
        /// Parameters: (key, down, time)
        /// Replaces VM_Call(cgvm, CG_KEY_EVENT, key, down, time).
        /// </summary>
        public static event Action<int, bool, int> OnCGameKeyEvent;

        /// <summary>
        /// Raised on cgame initialisation for a new map.
        /// Parameters: (serverMessageNum, serverCommandSequence, clientNum)
        /// Replaces VM_Call(cgvm, CG_INIT, ...).
        /// </summary>
        public static event Action<int, int, int> OnCGameInit;

        /// <summary>
        /// Raised on cgame shutdown.
        /// Replaces VM_Call(cgvm, CG_SHUTDOWN).
        /// </summary>
        public static event Action OnCGameShutdown;
#pragma warning restore CS0067

        // -------------------------------------------------------------------------
        // Config-string cache
        // Populated by CL_SetConfigString (called from the parse layer) and read
        // back by cgame via CL_GetConfigString.
        // -------------------------------------------------------------------------

        private static readonly Dictionary<int, string> _configStrings = new Dictionary<int, string>();

        // -------------------------------------------------------------------------
        // CL_CGameInit
        // Replaces VM_Create(cgvm) + VM_Call(CG_INIT, ...) in cl_cgame.c.
        // Reads connection metadata from ClientMain.Clc and fires OnCGameInit so
        // the cgame subscriber can set up its internal state.
        // -------------------------------------------------------------------------
        public static void CL_CGameInit()
        {
            int serverMessageNum      = ClientMain.Clc.ServerMessageSequence;
            int serverCommandSequence = ClientMain.Clc.ServerCommandSequence;
            int clientNum             = ClientMain.Clc.ClientNum;

            Debug.Log(string.Format(
                "CL_CGameInit: clientNum={0} serverMessageNum={1} serverCommandSequence={2}",
                clientNum, serverMessageNum, serverCommandSequence));

            OnCGameInit?.Invoke(serverMessageNum, serverCommandSequence, clientNum);
        }

        // -------------------------------------------------------------------------
        // CL_CGameShutdown
        // Replaces VM_Call(CG_SHUTDOWN) + VM_Free(cgvm) in cl_cgame.c.
        // -------------------------------------------------------------------------
        public static void CL_CGameShutdown()
        {
            Debug.Log("CL_CGameShutdown");
            OnCGameShutdown?.Invoke();
        }

        // -------------------------------------------------------------------------
        // CL_CGameReceiveSnapshot
        // Called from CL_Frame after ClientParse has written a new validated snapshot
        // into ClientMain.Cl.Snap.  Forwards it to cgame via event so the cgame
        // layer can interpolate entities and update the HUD.
        // Replaces the CG_SNAPSHOT_RECEIVED path in the C client.
        // -------------------------------------------------------------------------
        public static void CL_CGameReceiveSnapshot(ClientSnapshot snap)
        {
            if (snap == null || !snap.Valid)
                return;

            OnCGameSnapshot?.Invoke(snap);
        }

        // -------------------------------------------------------------------------
        // CL_CGameDrawActiveFrame
        // Called once per rendered frame.  Forwards serverTime and demo-playback
        // state to cgame so it can interpolate and draw.
        // Replaces VM_Call(cgvm, CG_DRAW_ACTIVE_FRAME, serverTime, stereoView, demoPlayback).
        // -------------------------------------------------------------------------
        public static void CL_CGameDrawActiveFrame(int serverTime, bool demoPlayback)
        {
            OnCGameDrawActiveFrame?.Invoke(serverTime, demoPlayback);
        }

        // -------------------------------------------------------------------------
        // CL_GetEntityToken / CL_SetEntityToken
        // In the C code the entity string is a large blob written during CG_INIT.
        // In this port cgame sets up its entity string directly on CG_INIT, so we
        // have nothing to tokenise here.  Return empty string as a safe stub.
        // -------------------------------------------------------------------------
        public static string CL_GetEntityToken()
        {
            return string.Empty;
        }

        // -------------------------------------------------------------------------
        // CL_GetConfigString / CL_SetConfigString
        // Backed by _configStrings.  CL_SetConfigString is called by the parse layer
        // (or by CL_ParseGamestate) when the server sends an SVC_CONFIGSTRING update.
        // CL_GetConfigString is called by cgame (via trap_GetConfigString) to read
        // server-authoritative configuration values.
        // -------------------------------------------------------------------------
        public static string CL_GetConfigString(int index)
        {
            if (_configStrings.TryGetValue(index, out string val))
                return val;
            return string.Empty;
        }

        public static void CL_SetConfigString(int index, string val)
        {
            _configStrings[index] = val ?? string.Empty;
        }

        // -------------------------------------------------------------------------
        // Asset registration stubs
        // In the C client these delegate to the renderer / sound system via the
        // cgame trap interface.  In this Unity port asset loading is handled by
        // Unity's own asset pipeline, so these stubs exist only to satisfy the API
        // surface expected by code that calls trap_RegisterSound / trap_RegisterShader.
        // -------------------------------------------------------------------------
        public static int CL_RegisterSound(string name)       => 0;
        public static int CL_RegisterShader(string name)      => 0;
        public static int CL_RegisterShaderNoMip(string name) => 0;
        public static int CL_RegisterModel(string name)       => 0;
        public static int CL_RegisterSkin(string name)        => 0;
    }
}
