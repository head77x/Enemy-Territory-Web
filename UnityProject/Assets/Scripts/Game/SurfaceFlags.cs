// Ported from: src/game/surfaceflags.h
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.

namespace ET.Game
{
    public static class Contents
    {
        public const int Solid           = 0x00000001;
        public const int LightGrid       = 0x00000004;
        public const int Lava            = 0x00000008;
        public const int Slime           = 0x00000010;
        public const int Water           = 0x00000020;
        public const int Fog             = 0x00000040;
        public const int MissileClip     = 0x00000080;
        public const int Item            = 0x00000100;
        public const int Mover           = 0x00004000;
        public const int AreaPortal      = 0x00008000;
        public const int PlayerClip      = 0x00010000;
        public const int MonsterClip     = 0x00020000;
        public const int Teleporter      = 0x00040000;
        public const int JumpPad         = 0x00080000;
        public const int ClusterPortal  = 0x00100000;
        public const int DoNotEnter      = 0x00200000;
        public const int DoNotEnterLarge = 0x00400000;
        public const int Origin          = 0x01000000;
        public const int Body            = 0x02000000;
        public const int Corpse          = 0x04000000;
        public const int Detail          = 0x08000000;
        public const int Structural      = 0x10000000;
        public const int Translucent     = 0x20000000;
        public const int Trigger         = 0x40000000;
        public const int NoDrop          = unchecked((int)0x80000000);

        // Common masks
        public const int MaskPlayerSolid  = Solid | PlayerClip | Body;
        public const int MaskShot         = Solid | Body | Corpse;
        public const int MaskWater        = Water | Lava | Slime;
        public const int MaskSolid        = Solid | Body;
        public const int MaskAll          = -1;
    }

    public static class Surf
    {
        public const int NoDamage        = 0x00000001;
        public const int Slick           = 0x00000002;
        public const int Sky             = 0x00000004;
        public const int Ladder          = 0x00000008;
        public const int NoImpact        = 0x00000010;
        public const int NoMarks         = 0x00000020;
        public const int Splash          = 0x00000040;
        public const int NoDraw          = 0x00000080;
        public const int Hint            = 0x00000100;
        public const int Skip            = 0x00000200;
        public const int NoLightmap      = 0x00000400;
        public const int PointLight      = 0x00000800;
        public const int Metal           = 0x00001000;
        public const int NoSteps         = 0x00002000;
        public const int NonSolid        = 0x00004000;
        public const int LightFilter     = 0x00008000;
        public const int AlphaShadow     = 0x00010000;
        public const int NoDLight        = 0x00020000;
        public const int Wood            = 0x00040000;
        public const int Grass           = 0x00080000;
        public const int Gravel          = 0x00100000;
        public const int Glass           = 0x00200000;
        public const int Snow            = 0x00400000;
        public const int Roof            = 0x00800000;
        public const int Rubble          = 0x01000000;
        public const int Carpet          = 0x02000000;
        public const int MonsterSlick    = 0x04000000;
        public const int MonSlickW       = 0x08000000;
        public const int MonSlickN       = 0x10000000;
        public const int MonSlickE       = 0x20000000;
        public const int MonSlickS       = 0x40000000;
        public const int Landmine        = unchecked((int)0x80000000);
    }
}
