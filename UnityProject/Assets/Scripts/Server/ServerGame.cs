// Ported from: src/server/sv_game.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using UnityEngine;
using ET.Core;
using ET.Network;
using ET.Game;

namespace ET.Server
{
    /// <summary>
    /// Bridge between the server and the game VM (sv_game.c port).
    ///
    /// In the original C code the game logic lived in a DLL loaded via dlopen and
    /// called through VM_Call.  In Unity that boundary is replaced by C# events
    /// (established in ServerMain/ServerInit/ServerClient) and direct calls.
    /// </summary>
    public static class ServerGame
    {
        // -----------------------------------------------------------------------
        // SV_GameSendServerCommand — send a reliable command string to one client
        // or to all active clients when clientNum == -1.
        //
        // Mirrors SV_GameSendServerCommand in sv_game.c.
        // -----------------------------------------------------------------------
        public static void SV_GameSendServerCommand(int clientNum, string cmd)
        {
            var svs       = ServerMain.Svs;
            int maxClients = CvarSystem.sv_maxclients.IntValue;

            if (clientNum == -1)
            {
                // Broadcast to every connected client
                for (int i = 0; i < maxClients; i++)
                {
                    var cl = svs.Clients[i];
                    if (cl.State < ClientState.Active) continue;
                    ServerMain.SV_AddServerCommand(cl, cmd);
                }
            }
            else
            {
                if (clientNum < 0 || clientNum >= maxClients)
                {
                    Debug.LogWarning($"SV_GameSendServerCommand: bad clientNum {clientNum}");
                    return;
                }

                var cl = svs.Clients[clientNum];
                if (cl.State < ClientState.Connected) return;
                ServerMain.SV_AddServerCommand(cl, cmd);
            }
        }

        // -----------------------------------------------------------------------
        // SV_GameDropClient — forcibly disconnect a client with a reason string.
        // -----------------------------------------------------------------------
        public static void SV_GameDropClient(int clientNum, string reason)
        {
            var svs = ServerMain.Svs;
            int maxClients = CvarSystem.sv_maxclients.IntValue;

            if (clientNum < 0 || clientNum >= maxClients)
            {
                Debug.LogWarning($"SV_GameDropClient: bad clientNum {clientNum}");
                return;
            }

            ServerMain.SV_DropClient(svs.Clients[clientNum], reason ?? "");
        }

        // -----------------------------------------------------------------------
        // SV_SetBrushModel — mark an entity as using a BSP sub-model.
        // Stub — no-op until BSP is wired up.
        // -----------------------------------------------------------------------
        public static void SV_SetBrushModel(int entityNum, string name) { }

        // -----------------------------------------------------------------------
        // SV_GetServerinfo — return the CS_SERVERINFO configstring.
        // -----------------------------------------------------------------------
        public static string SV_GetServerinfo()
        {
            return ServerInit.SV_GetConfigstring(ServerMain.CS_SERVERINFO);
        }

        // -----------------------------------------------------------------------
        // SV_AdjustAreaPortalState — open or close a BSP area portal.
        // Stub — no-op until BSP is wired up.
        // -----------------------------------------------------------------------
        public static void SV_AdjustAreaPortalState(int entityNum, bool open) { }

        // -----------------------------------------------------------------------
        // SV_EntityContact — test whether the supplied AABB [mins, maxs] (in world
        // space) overlaps the bounding box of the given entity.
        //
        // Uses the same AABB logic as ServerWorld.SV_ClipToEntity but just returns
        // a bool instead of a full trace result.
        // -----------------------------------------------------------------------
        public static bool SV_EntityContact(Vector3 mins, Vector3 maxs, int entityNum)
        {
            var sv = ServerMain.Sv;
            if (entityNum < 0 || entityNum >= sv.NumEntities) return false;

            EntityState ent = sv.Entities[entityNum];
            if (ent == null || ent.EType == 0) return false;

            const float halfX = 18f;
            const float halfY = 18f;
            const float halfZ = 36f;

            float ox = ent.Origin0;
            float oy = ent.Origin1;
            float oz = ent.Origin2;

            // AABB overlap on all three axes
            if (maxs.x < ox - halfX || mins.x > ox + halfX) return false;
            if (maxs.y < oy - halfY || mins.y > oy + halfY) return false;
            if (maxs.z < oz - halfZ || mins.z > oz + halfZ) return false;

            return true;
        }

        // -----------------------------------------------------------------------
        // SV_GetUsercmd — return the last usercmd recorded for a client.
        // -----------------------------------------------------------------------
        public static UserCmd SV_GetUsercmd(int clientNum)
        {
            var svs = ServerMain.Svs;
            int maxClients = CvarSystem.sv_maxclients.IntValue;

            if (clientNum < 0 || clientNum >= maxClients)
            {
                Debug.LogWarning($"SV_GetUsercmd: bad clientNum {clientNum}");
                return new UserCmd();
            }

            return svs.Clients[clientNum].LastUsercmd;
        }

        // -----------------------------------------------------------------------
        // SV_GetEntityToken — return the next token from the map entity string.
        // Stub — returns empty string until entity-string parsing is implemented.
        // -----------------------------------------------------------------------
        public static string SV_GetEntityToken() => "";

        // -----------------------------------------------------------------------
        // SV_inPVS — test whether two points share a PVS leaf.
        // Stub — returns true (conservative: never cull) until BSP PVS is wired up.
        // -----------------------------------------------------------------------
        public static bool SV_inPVS(Vector3 p1, Vector3 p2) => true;

        // -----------------------------------------------------------------------
        // SV_inPVSIgnorePortals — same as SV_inPVS but ignoring area portals.
        // Stub — returns true.
        // -----------------------------------------------------------------------
        public static bool SV_inPVSIgnorePortals(Vector3 p1, Vector3 p2) => true;

        // -----------------------------------------------------------------------
        // Configstring accessors — delegate to ServerInit / ServerMain.
        // -----------------------------------------------------------------------
        public static string SV_GetConfigstring(int index)     => ServerInit.SV_GetConfigstring(index);
        public static void   SV_SetConfigstring(int index, string val) => ServerMain.SV_SetConfigstring(index, val);

        // -----------------------------------------------------------------------
        // Userinfo accessors — delegate to ServerInit.
        // -----------------------------------------------------------------------
        public static string SV_GetUserinfo(int clientNum)            => ServerInit.SV_GetUserinfo(clientNum);
        public static void   SV_SetUserinfo(int clientNum, string val) => ServerInit.SV_SetUserinfo(clientNum, val);
    }
}
