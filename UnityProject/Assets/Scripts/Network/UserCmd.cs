// Ported from: src/game/q_shared.h  (usercmd_t, button defines, dtType_t)
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

namespace ET.Network
{
    // usercmd_t->button bits
    public static class Button
    {
        public const int Attack     = 1;
        public const int Talk       = 2;
        public const int Gesture    = 8;
        public const int Walking    = 16;
        public const int Sprint     = 32;
        public const int Activate   = 64;
        public const int Any        = 128;
    }

    // usercmd_t->wbuttons bits (Wolf-specific)
    public static class WButton
    {
        public const int Attack2    = 1;
        public const int Zoom       = 2;
        public const int Reload     = 8;
        public const int LeanLeft   = 16;
        public const int LeanRight  = 32;
        public const int Drop       = 64;
        public const int Prone      = 128;
    }

    public enum DoubleTapType
    {
        None,
        MoveLeft,
        MoveRight,
        Forward,
        Back,
        LeanLeft,
        LeanRight,
        Up,
        Count,  // DT_NUM
    }

    /// <summary>
    /// Client command sent to the server each frame (usercmd_t).
    /// All fields are int-width to match the original's 32-bit-aligned layout.
    /// </summary>
    public sealed class UserCmd
    {
        public int ServerTime;
        public int Buttons;      // byte in C, stored as int for delta encoding
        public int WButtons;     // byte
        public int Weapon;       // byte
        public int Flags;        // byte
        public int[] Angles = new int[3];
        public int ForwardMove;  // signed char → int
        public int RightMove;    // signed char → int
        public int UpMove;       // signed char → int
        public int DoubleTap;    // 3 bits used
        public int IdentClient;  // byte

        public void CopyFrom(UserCmd src)
        {
            ServerTime  = src.ServerTime;
            Buttons     = src.Buttons;
            WButtons    = src.WButtons;
            Weapon      = src.Weapon;
            Flags       = src.Flags;
            Angles[0]   = src.Angles[0];
            Angles[1]   = src.Angles[1];
            Angles[2]   = src.Angles[2];
            ForwardMove = src.ForwardMove;
            RightMove   = src.RightMove;
            UpMove      = src.UpMove;
            DoubleTap   = src.DoubleTap;
            IdentClient = src.IdentClient;
        }

        public bool Equals(UserCmd other)
        {
            return ServerTime  == other.ServerTime
                && Buttons     == other.Buttons
                && WButtons    == other.WButtons
                && Weapon      == other.Weapon
                && Flags       == other.Flags
                && Angles[0]   == other.Angles[0]
                && Angles[1]   == other.Angles[1]
                && Angles[2]   == other.Angles[2]
                && ForwardMove == other.ForwardMove
                && RightMove   == other.RightMove
                && UpMove      == other.UpMove
                && DoubleTap   == other.DoubleTap
                && IdentClient == other.IdentClient;
        }
    }
}
