// Ported from: src/game/g_bot.c (chat), src/botai/ai_chat.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

using System;
using System.Collections.Generic;
using ET.Game;
using ET.Network;
using UnityEngine;

namespace ET.BotAI
{
    // Voice command types (subset of ET's voice chat system)
    public enum VoiceCmd
    {
        Acknowledge,      // "Acknowledged"
        NeedMedic,        // "Medic!"
        NeedAmmo,         // "Ammo!"
        FollowMe,         // "Follow me"
        AttackObjective,  // "Attack the objective"
        DefendObjective,  // "Defend the objective"
        Grenade,          // "Grenade!"
        CoverMe,          // "Cover me"
        MovingOut,        // "Moving out"
        FireInTheHole,    // "Fire in the hole!"
        Incoming,         // "Incoming!"
        PathClear,        // "Path clear"
        EnemyWeak,        // "Enemy is weakened"
    }

    public static class BotChat
    {
        // Response probability: 0..1. Bots don't always respond.
        public const float CHAT_RESPONSE_CHANCE = 0.4f;

        // -----------------------------------------------------------------------
        // Response message table — mirrors the string arrays in ai_chat.c
        // Each VoiceCmd maps to one or more response strings; a random entry is chosen.
        // -----------------------------------------------------------------------
        private static readonly Dictionary<VoiceCmd, string[]> _responseTable =
            new Dictionary<VoiceCmd, string[]>
        {
            { VoiceCmd.Acknowledge,     new[] { "Acknowledged.", "Roger that.", "Understood." } },
            { VoiceCmd.NeedMedic,       new[] { "On my way!", "Hold on, I'm coming!", "Stay alive!" } },
            { VoiceCmd.NeedAmmo,        new[] { "Ammo incoming!", "On my way with supplies!", "Got you covered." } },
            { VoiceCmd.FollowMe,        new[] { "Right behind you!", "Moving with you.", "Lead the way!" } },
            { VoiceCmd.AttackObjective, new[] { "Let's go!", "Moving to objective!", "Attack!" } },
            { VoiceCmd.DefendObjective, new[] { "Holding position!", "Defending now.", "I'll guard it." } },
            { VoiceCmd.Grenade,         new[] { "Heads up!", "Grenade!", "Get down!" } },
            { VoiceCmd.CoverMe,         new[] { "Covering!", "I've got you.", "Move — I'll cover!" } },
            { VoiceCmd.MovingOut,       new[] { "Moving out!", "Let's move.", "On the advance." } },
            { VoiceCmd.FireInTheHole,   new[] { "Fire in the hole!", "Get clear!", "Incoming blast!" } },
            { VoiceCmd.Incoming,        new[] { "Incoming!", "Take cover!", "Enemy contact!" } },
            { VoiceCmd.PathClear,       new[] { "Path is clear.", "All clear.", "No contacts ahead." } },
            { VoiceCmd.EnemyWeak,       new[] { "Enemy is weakened!", "Push now!", "They're falling back!" } },
        };

        // -----------------------------------------------------------------------
        // G_BotVoiceChat — a bot hears a voice command and may respond/act
        // Mirrors G_BotVoiceChat in g_bot.c
        // -----------------------------------------------------------------------
        public static void G_BotVoiceChat(BotAgent bot, int senderClientNum,
                                           VoiceCmd cmd, int team)
        {
            // Only respond to friendly commands
            if (bot.Team != team)
                return;

            // Probabilistic response — bots don't always acknowledge
            if (UnityEngine.Random.value >= CHAT_RESPONSE_CHANCE)
                return;

            // Possibly change bot's behavioural state
            HandleVoiceCmd(bot, cmd, senderClientNum);

            // Pick and send a response message
            string response = GetResponseMessage(bot, cmd);
            if (!string.IsNullOrEmpty(response))
                BotSayTeam(bot, response);
        }

        // -----------------------------------------------------------------------
        // BotSayTeam — bot sends a team-chat message
        // Raises BotMain.OnBotChat with the clientNum so the game layer can route it.
        // -----------------------------------------------------------------------
        public static void BotSayTeam(BotAgent bot, string message)
        {
            BotMain.OnBotChat?.Invoke(bot.ClientNum, $"[TEAM] {bot.Name}: {message}");
        }

        // -----------------------------------------------------------------------
        // BotSay — bot sends a global (all-chat) message
        // -----------------------------------------------------------------------
        public static void BotSay(BotAgent bot, string message)
        {
            BotMain.OnBotChat?.Invoke(bot.ClientNum, $"{bot.Name}: {message}");
        }

        // -----------------------------------------------------------------------
        // GetResponseMessage — pick a contextually appropriate response string
        // -----------------------------------------------------------------------
        public static string GetResponseMessage(BotAgent bot, VoiceCmd cmd)
        {
            if (!_responseTable.TryGetValue(cmd, out string[] options) || options.Length == 0)
                return string.Empty;

            return options[UnityEngine.Random.Range(0, options.Length)];
        }

        // -----------------------------------------------------------------------
        // HandleVoiceCmd — acknowledge voice commands that affect bot's goal
        // Mirrors the state-change logic scattered through ai_chat.c
        // -----------------------------------------------------------------------
        private static void HandleVoiceCmd(BotAgent bot, VoiceCmd cmd, int senderClientNum)
        {
            switch (cmd)
            {
                case VoiceCmd.FollowMe:
                    bot.State           = BotState.FollowingOrder;
                    bot.TargetEntityNum = senderClientNum;
                    break;

                case VoiceCmd.AttackObjective:
                    bot.State = BotState.Capturing;
                    break;

                case VoiceCmd.DefendObjective:
                    bot.State = BotState.Defending;
                    break;

                case VoiceCmd.NeedMedic:
                    // Only a medic responds to a medic call
                    if (bot.PlayerClass == GameConst.PC_MEDIC)
                    {
                        bot.State           = BotState.Reviving;
                        bot.TargetEntityNum = senderClientNum;
                    }
                    break;

                case VoiceCmd.NeedAmmo:
                    // Only a Field Ops responds to an ammo request
                    if (bot.PlayerClass == GameConst.PC_FIELDOPS)
                    {
                        bot.State           = BotState.Resupplying;
                        bot.TargetEntityNum = senderClientNum;
                    }
                    break;

                case VoiceCmd.CoverMe:
                    // Follow and protect the sender
                    bot.State           = BotState.FollowingOrder;
                    bot.TargetEntityNum = senderClientNum;
                    break;

                case VoiceCmd.MovingOut:
                    // Join the advance toward the current objective
                    if (bot.State == BotState.Idle || bot.State == BotState.Defending)
                        bot.State = BotState.Capturing;
                    break;

                // Informational-only commands — no state change required
                case VoiceCmd.Acknowledge:
                case VoiceCmd.Grenade:
                case VoiceCmd.FireInTheHole:
                case VoiceCmd.Incoming:
                case VoiceCmd.PathClear:
                case VoiceCmd.EnemyWeak:
                    break;
            }

            // Reset the state timer whenever a command causes a state change
            bot.StateTimer = 0f;
        }
    }
}
