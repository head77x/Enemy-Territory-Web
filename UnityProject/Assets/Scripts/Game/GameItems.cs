// Ported from: src/game/g_items.c
// Original: Wolfenstein: Enemy Territory GPL Source Code
// Copyright (C) 1999-2010 id Software LLC, a ZeniMax Media company.
//
// Item pickup, ammo resupply, health packs, and weapon drop logic.
// Does NOT mutate entity or player state directly — computes outcomes and
// raises events.  Callers are responsible for applying PickupInfo quantities
// to PlayerState and for spawning/removing item entities.

using System;
using UnityEngine;
using ET.Network;

namespace ET.Game
{
    // -------------------------------------------------------------------------
    // Item type enumeration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Broad category of a world item, mirroring itemType_t from bg_public.h.
    /// </summary>
    public enum ItemType
    {
        Bad      = 0,
        Weapon   = 1,   // IT_WEAPON
        Ammo     = 2,   // IT_AMMO
        Armor    = 3,   // IT_ARMOR    (unused in ET MP)
        Health   = 4,   // IT_HEALTH
        PowerUp  = 5,   // IT_POWERUP  (unused in ET MP)
        Holdable = 6,   // IT_HOLDABLE (unused in ET MP)
        Key      = 7,   // IT_KEY      (unused in ET MP)
        Team     = 8,   // IT_TEAM (flags, objectives)
    }

    // -------------------------------------------------------------------------
    // Per-item static descriptor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Static descriptor for a world item, mirroring gitem_t from bg_public.h.
    /// </summary>
    public struct ItemInfo
    {
        /// <summary>Entity class name used in map files (e.g. "weapon_mp40").</summary>
        public string   ClassName;
        /// <summary>Sound alias played on pickup (client-side, informational only).</summary>
        public string   PickupSound;
        /// <summary>Path to the world .md3 model.</summary>
        public string   WorldModel;
        /// <summary>Shader or icon name shown in the HUD.</summary>
        public string   Icon;
        /// <summary>Human-readable display name used in pickup messages.</summary>
        public string   PickupName;
        /// <summary>Ammo quantity granted (health items: HP amount; ammo items: clip count).</summary>
        public int      Quantity;
        /// <summary>Broad item category.</summary>
        public ItemType Type;
        /// <summary>
        /// Weapon ID (WP_*) for IT_WEAPON entries, ammo-owner WP_* for IT_AMMO
        /// entries.  Zero / WP_NONE for other categories.
        /// </summary>
        public int      Tag;
    }

    // -------------------------------------------------------------------------
    // Event payloads
    // -------------------------------------------------------------------------

    /// <summary>
    /// Payload raised by <see cref="GameItems.OnItemPickup"/> when a player
    /// successfully picks up a world item.
    /// </summary>
    public struct PickupInfo
    {
        /// <summary>Index of the picking-up client (0-63).</summary>
        public int      ClientNum;
        /// <summary>Entity slot of the consumed item.</summary>
        public int      ItemEntityNum;
        /// <summary>Broad item category.</summary>
        public ItemType Type;
        /// <summary>WP_* for weapons/ammo; 0 for health and team items.</summary>
        public int      Tag;
        /// <summary>Amount actually granted (HP added, ammo rounds added, etc.).</summary>
        public int      Quantity;
    }

    /// <summary>
    /// Payload raised by <see cref="GameItems.OnItemRespawn"/> after
    /// <see cref="GameItems.G_SetRespawn"/> schedules a respawn.
    /// </summary>
    public struct RespawnInfo
    {
        /// <summary>Entity slot of the item that should be respawned.</summary>
        public int ItemEntityNum;
        /// <summary>Delay in milliseconds before the item becomes available.</summary>
        public int DelayMs;
    }

    /// <summary>
    /// Payload raised by <see cref="GameItems.OnItemDrop"/> when a player
    /// drops or loses a weapon (death, /drop command, etc.).
    /// </summary>
    public struct DropInfo
    {
        /// <summary>Client that dropped the item.</summary>
        public int     ClientNum;
        /// <summary>WP_* identifier of the dropped weapon.</summary>
        public int     Weapon;
        /// <summary>World-space origin where the item should be spawned.</summary>
        public Vector3 Origin;
        /// <summary>Initial velocity of the dropped item entity.</summary>
        public Vector3 Velocity;
    }

    // -------------------------------------------------------------------------
    // GameItems
    // -------------------------------------------------------------------------

    /// <summary>
    /// Static class owning all server-side item pickup/drop/respawn logic,
    /// ported from g_items.c.  All outcomes are communicated through events;
    /// no entity or player state is mutated here.
    /// </summary>
    public static class GameItems
    {
        // ---------------------------------------------------------------------
        // Health / respawn constants
        // ---------------------------------------------------------------------

        /// <summary>Normal maximum health for a player.</summary>
        public const int MAX_HEALTH          = 100;

        /// <summary>Bonus HP granted to a medic using the self-heal syringe.</summary>
        public const int MEDIC_BONUS_HP      = 10;

        /// <summary>Respawn delay for standard pickups (health, ammo) in milliseconds.</summary>
        public const int ITEM_RESPAWN_NORMAL  = 30_000;

        /// <summary>Respawn delay for weapon pickups in milliseconds.</summary>
        public const int ITEM_RESPAWN_WEAPON  = 60_000;

        // ---------------------------------------------------------------------
        // Drop velocity
        // ---------------------------------------------------------------------

        /// <summary>Forward speed (units/s) imparted when dropping an item.</summary>
        private const float DROP_SPEED_FORWARD = 150f;

        /// <summary>Upward component added to the drop velocity.</summary>
        private const float DROP_SPEED_UP      = 50f;

        // ---------------------------------------------------------------------
        // Events
        // ---------------------------------------------------------------------

        /// <summary>
        /// Raised by <see cref="G_PickupItem"/> after all eligibility checks pass.
        /// The game layer must apply <see cref="PickupInfo.Quantity"/> to the
        /// appropriate <see cref="PlayerState"/> field and remove / flag the item
        /// entity as consumed.
        /// </summary>
        public static event Action<PickupInfo>  OnItemPickup;

        /// <summary>
        /// Raised by <see cref="G_SetRespawn"/> to schedule a future respawn.
        /// The game layer must hide the item entity for <see cref="RespawnInfo.DelayMs"/>
        /// milliseconds, then make it available again.
        /// </summary>
        public static event Action<RespawnInfo> OnItemRespawn;

        /// <summary>
        /// Raised by <see cref="G_DropItem"/> when a client drops or loses a
        /// weapon.  The game layer must spawn a new item entity at
        /// <see cref="DropInfo.Origin"/> with <see cref="DropInfo.Velocity"/>.
        /// </summary>
        public static event Action<DropInfo>    OnItemDrop;

        // ---------------------------------------------------------------------
        // Item table
        // ---------------------------------------------------------------------

        /// <summary>
        /// Static item descriptor table, indexed by a sequential item ID
        /// (not by WP_*).  Use <see cref="FindItem"/> to search by class name
        /// or <see cref="FindItemForWeapon"/> to look up by WP_* tag.
        /// </summary>
        public static readonly ItemInfo[] Items = BuildItemTable();

        private static ItemInfo[] BuildItemTable()
        {
            // Helper local factory — keeps the table compact.
            static ItemInfo H(string cls, string snd, string mdl, string icon, string name, int qty)
                => new ItemInfo
                {
                    ClassName   = cls,
                    PickupSound = snd,
                    WorldModel  = mdl,
                    Icon        = icon,
                    PickupName  = name,
                    Quantity    = qty,
                    Type        = ItemType.Health,
                    Tag         = 0,
                };

            static ItemInfo A(string cls, string snd, string mdl, string icon, string name, int qty, int tag)
                => new ItemInfo
                {
                    ClassName   = cls,
                    PickupSound = snd,
                    WorldModel  = mdl,
                    Icon        = icon,
                    PickupName  = name,
                    Quantity    = qty,
                    Type        = ItemType.Ammo,
                    Tag         = tag,
                };

            static ItemInfo W(string cls, string snd, string mdl, string icon, string name, int tag)
                => new ItemInfo
                {
                    ClassName   = cls,
                    PickupSound = snd,
                    WorldModel  = mdl,
                    Icon        = icon,
                    PickupName  = name,
                    Quantity    = 0,
                    Type        = ItemType.Weapon,
                    Tag         = tag,
                };

            return new ItemInfo[]
            {
                // ----------------------------------------------------------------
                // Health
                // ----------------------------------------------------------------
                H("item_health_small",
                    "sound/items/n_health.wav",
                    "models/powerups/health/small_cross.md3",
                    "icons/iconh_yellow",
                    "Small Health",
                    5),

                H("item_health",
                    "sound/items/n_health.wav",
                    "models/powerups/health/medium_cross.md3",
                    "icons/iconh_green",
                    "Health",
                    25),

                H("item_health_large",
                    "sound/items/n_health.wav",
                    "models/powerups/health/large_cross.md3",
                    "icons/iconh_red",
                    "Large Health",
                    50),

                H("item_health_mega",
                    "sound/items/n_health.wav",
                    "models/powerups/health/mega_cross.md3",
                    "icons/iconh_mega",
                    "Mega Health",
                    100),

                // ----------------------------------------------------------------
                // Ammo  (qty = default clip/pack size; tag = WP_* owner weapon)
                // ----------------------------------------------------------------

                // Pistols
                A("ammo_9mm",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/9mm/ammo_9mm.md3",
                    "icons/iconammo_9mm",
                    "9mm Ammo",
                    30, GameConst.WP_LUGER),

                A("ammo_45",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/45cal/ammo_45cal.md3",
                    "icons/iconammo_45cal",
                    ".45 Ammo",
                    30, GameConst.WP_COLT),

                // SMGs
                A("ammo_9mm_mp40",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/9mm/ammo_9mm.md3",
                    "icons/iconammo_9mm",
                    "MP40 Ammo",
                    64, GameConst.WP_MP40),

                A("ammo_45_thompson",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/45cal/ammo_45cal.md3",
                    "icons/iconammo_45cal",
                    "Thompson Ammo",
                    64, GameConst.WP_THOMPSON),

                A("ammo_9mm_sten",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/9mm/ammo_9mm.md3",
                    "icons/iconammo_9mm",
                    "Sten Ammo",
                    64, GameConst.WP_STEN),

                // Assault / auto rifles
                A("ammo_792mm_fg42",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/792mm/ammo_792mm.md3",
                    "icons/iconammo_792mm",
                    "FG42 Ammo",
                    64, GameConst.WP_FG42),

                A("ammo_30cal_kar98",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/30cal/ammo_30cal.md3",
                    "icons/iconammo_30cal",
                    "Kar98 Ammo",
                    10, GameConst.WP_KAR98),

                A("ammo_30cal_carbine",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/30cal/ammo_30cal.md3",
                    "icons/iconammo_30cal",
                    "Carbine Ammo",
                    10, GameConst.WP_CARBINE),

                A("ammo_30cal_garand",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/30cal/ammo_30cal.md3",
                    "icons/iconammo_30cal",
                    "Garand Ammo",
                    10, GameConst.WP_GARAND),

                A("ammo_792mm_k43",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/792mm/ammo_792mm.md3",
                    "icons/iconammo_792mm",
                    "K43 Ammo",
                    10, GameConst.WP_K43),

                // Heavy / special
                A("ammo_panzerfaust",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/panzerfaust/panzerfaust_ammo.md3",
                    "icons/iconammo_panzerfaust",
                    "Panzerfaust Ammo",
                    1, GameConst.WP_PANZERFAUST),

                A("ammo_flamethrower",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/flamethrower/flamethrower_ammo.md3",
                    "icons/iconammo_flamethrower",
                    "Flamethrower Fuel",
                    50, GameConst.WP_FLAMETHROWER),

                A("ammo_grenade",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/grenade/grenade_ammo.md3",
                    "icons/iconammo_grenade",
                    "Grenade",
                    2, GameConst.WP_GRENADE_LAUNCHER),

                A("ammo_grenade_pineapple",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/grenade/grenade_ammo.md3",
                    "icons/iconammo_grenade",
                    "Grenade (Pineapple)",
                    2, GameConst.WP_GRENADE_PINEAPPLE),

                A("ammo_mortar",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/mortar/mortar_ammo.md3",
                    "icons/iconammo_mortar",
                    "Mortar Round",
                    4, GameConst.WP_MORTAR),

                A("ammo_dynamite",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/dynamite/dynamite_ammo.md3",
                    "icons/iconammo_dynamite",
                    "Dynamite",
                    2, GameConst.WP_DYNAMITE),

                A("ammo_landmine",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/landmine/landmine_ammo.md3",
                    "icons/iconammo_landmine",
                    "Landmine",
                    2, GameConst.WP_LANDMINE),

                A("ammo_satchel",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/satchel/satchel_ammo.md3",
                    "icons/iconammo_satchel",
                    "Satchel Charge",
                    1, GameConst.WP_SATCHEL),

                A("ammo_mg42",
                    "sound/misc/am_pkup.wav",
                    "models/ammo/mg42/mg42_ammo.md3",
                    "icons/iconammo_mg42",
                    "MG42 Ammo",
                    150, GameConst.WP_MOBILE_MG42),

                // ----------------------------------------------------------------
                // Weapons  (Quantity unused for weapons — ammo is given separately)
                // ----------------------------------------------------------------
                W("weapon_knife",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/knife/knife.md3",
                    "icons/iconw_knife",
                    "Knife",
                    GameConst.WP_KNIFE),

                W("weapon_luger",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/luger/luger.md3",
                    "icons/iconw_luger",
                    "Luger",
                    GameConst.WP_LUGER),

                W("weapon_mp40",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/mp40/mp40.md3",
                    "icons/iconw_mp40",
                    "MP40",
                    GameConst.WP_MP40),

                W("weapon_grenade_launcher",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/grenade/grenade.md3",
                    "icons/iconw_grenade",
                    "Grenade",
                    GameConst.WP_GRENADE_LAUNCHER),

                W("weapon_panzerfaust",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/panzerfaust/pf.md3",
                    "icons/iconw_panzerfaust",
                    "Panzerfaust",
                    GameConst.WP_PANZERFAUST),

                W("weapon_flamethrower",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/flamethrower/flamethrower.md3",
                    "icons/iconw_flamethrower",
                    "Flamethrower",
                    GameConst.WP_FLAMETHROWER),

                W("weapon_colt",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/colt/colt.md3",
                    "icons/iconw_colt",
                    "Colt",
                    GameConst.WP_COLT),

                W("weapon_thompson",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/thompson/thompson.md3",
                    "icons/iconw_thompson",
                    "Thompson",
                    GameConst.WP_THOMPSON),

                W("weapon_grenade_pineapple",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/grenade/grenade.md3",
                    "icons/iconw_grenade_us",
                    "Grenade (Pineapple)",
                    GameConst.WP_GRENADE_PINEAPPLE),

                W("weapon_sten",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/sten/sten.md3",
                    "icons/iconw_sten",
                    "Sten",
                    GameConst.WP_STEN),

                W("weapon_kar98",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/kar98/kar98.md3",
                    "icons/iconw_kar98",
                    "Kar98",
                    GameConst.WP_KAR98),

                W("weapon_carbine",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/carbine/carbine.md3",
                    "icons/iconw_carbine",
                    "Carbine",
                    GameConst.WP_CARBINE),

                W("weapon_garand",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/garand/garand.md3",
                    "icons/iconw_garand",
                    "Garand",
                    GameConst.WP_GARAND),

                W("weapon_k43",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/k43/k43.md3",
                    "icons/iconw_k43",
                    "K43",
                    GameConst.WP_K43),

                W("weapon_fg42",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/fg42/fg42.md3",
                    "icons/iconw_fg42",
                    "FG42",
                    GameConst.WP_FG42),

                W("weapon_mobile_mg42",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/mg42/mg42.md3",
                    "icons/iconw_mg42",
                    "Mobile MG42",
                    GameConst.WP_MOBILE_MG42),

                W("weapon_mortar",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/mortar/mortar.md3",
                    "icons/iconw_mortar",
                    "Mortar",
                    GameConst.WP_MORTAR),

                W("weapon_dynamite",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/dynamite/dynamite.md3",
                    "icons/iconw_dynamite",
                    "Dynamite",
                    GameConst.WP_DYNAMITE),

                W("weapon_landmine",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/landmine/landmine.md3",
                    "icons/iconw_landmine",
                    "Landmine",
                    GameConst.WP_LANDMINE),

                W("weapon_satchel",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/satchel/satchel.md3",
                    "icons/iconw_satchel",
                    "Satchel Charge",
                    GameConst.WP_SATCHEL),

                W("weapon_gpg40",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/gpg40/gpg40.md3",
                    "icons/iconw_gpg40",
                    "GPG40",
                    GameConst.WP_GPG40),

                W("weapon_m7",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/m7/m7.md3",
                    "icons/iconw_m7",
                    "M7",
                    GameConst.WP_M7),

                W("weapon_silencer",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/luger/luger.md3",
                    "icons/iconw_silencer",
                    "Silenced Luger",
                    GameConst.WP_SILENCER),

                W("weapon_silenced_colt",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/colt/colt.md3",
                    "icons/iconw_silenced_colt",
                    "Silenced Colt",
                    GameConst.WP_SILENCED_COLT),

                W("weapon_akimbo_colt",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/colt/colt.md3",
                    "icons/iconw_akimbo_colt",
                    "Akimbo Colts",
                    GameConst.WP_AKIMBO_COLT),

                W("weapon_akimbo_luger",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/luger/luger.md3",
                    "icons/iconw_akimbo_luger",
                    "Akimbo Lugers",
                    GameConst.WP_AKIMBO_LUGER),

                W("weapon_akimbo_silencedcolt",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/colt/colt.md3",
                    "icons/iconw_akimbo_silenced_colt",
                    "Akimbo Silenced Colts",
                    GameConst.WP_AKIMBO_SILENCEDCOLT),

                W("weapon_akimbo_silencedluger",
                    "sound/misc/w_pkup.wav",
                    "models/weapons2/luger/luger.md3",
                    "icons/iconw_akimbo_silenced_luger",
                    "Akimbo Silenced Lugers",
                    GameConst.WP_AKIMBO_SILENCEDLUGER),
            };
        }

        // ---------------------------------------------------------------------
        // Item table lookup helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns the index of the first item whose ClassName matches
        /// <paramref name="className"/> (case-insensitive), or -1 if not found.
        /// </summary>
        public static int FindItem(string className)
        {
            if (string.IsNullOrEmpty(className))
                return -1;

            for (int i = 0; i < Items.Length; i++)
            {
                if (string.Equals(Items[i].ClassName, className,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Returns the index of the <see cref="ItemType.Weapon"/> entry whose
        /// <see cref="ItemInfo.Tag"/> equals <paramref name="weapon"/> (WP_*),
        /// or -1 if no such entry exists.
        /// </summary>
        public static int FindItemForWeapon(int weapon)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                if (Items[i].Type == ItemType.Weapon && Items[i].Tag == weapon)
                    return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------------
        // Ammo max table (from bg_misc.c)
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns the maximum reserve ammo count for <paramref name="weapon"/>
        /// (WP_*), as defined in the ET ammo table in bg_misc.c.
        /// Returns 0 for weapons that do not use conventional ammo (knives,
        /// special equipment, etc.).
        /// </summary>
        public static int GetMaxAmmo(int weapon)
        {
            switch (weapon)
            {
                case GameConst.WP_LUGER:
                case GameConst.WP_SILENCER:                 return 48;

                case GameConst.WP_COLT:
                case GameConst.WP_SILENCED_COLT:            return 48;

                case GameConst.WP_MP40:                     return 192;
                case GameConst.WP_THOMPSON:                 return 192;
                case GameConst.WP_STEN:                     return 192;
                case GameConst.WP_FG42:                     return 192;

                case GameConst.WP_PANZERFAUST:              return 4;
                case GameConst.WP_FLAMETHROWER:             return 200;

                case GameConst.WP_GRENADE_LAUNCHER:
                case GameConst.WP_GRENADE_PINEAPPLE:        return 10;

                case GameConst.WP_MORTAR:                   return 15;
                case GameConst.WP_DYNAMITE:                 return 10;
                case GameConst.WP_LANDMINE:                 return 10;
                case GameConst.WP_SATCHEL:                  return 4;

                case GameConst.WP_KAR98:                    return 30;
                case GameConst.WP_CARBINE:                  return 30;
                case GameConst.WP_GARAND:                   return 30;
                case GameConst.WP_K43:                      return 30;

                case GameConst.WP_MOBILE_MG42:              return 450;

                default:                                     return 0;
            }
        }

        // ---------------------------------------------------------------------
        // Weapon bitmask helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Returns true when bit <paramref name="weapon"/> is set in the
        /// two-word weapon bitmask stored in <see cref="PlayerState.Weapons"/>.
        /// </summary>
        private static bool HasWeapon(PlayerState ps, int weapon)
        {
            if ((uint)weapon >= (uint)GameConst.MAX_WEAPONS)
                return false;

            int word = weapon >> 5;   // 0 or 1
            int bit  = weapon & 31;
            return (ps.Weapons[word] & (1 << bit)) != 0;
        }

        // ---------------------------------------------------------------------
        // Core pickup logic
        // ---------------------------------------------------------------------

        /// <summary>
        /// Called when a player's bounding box overlaps a world item entity.
        /// Performs all eligibility checks (max health, ammo full, weapon
        /// already owned, etc.) and returns true when the item was consumed.
        ///
        /// When consumed, raises <see cref="OnItemPickup"/> with the actual
        /// quantity that should be applied by the game layer.
        /// Schedules a respawn via <see cref="G_SetRespawn"/> automatically.
        /// </summary>
        /// <param name="clientNum">Picking-up client index.</param>
        /// <param name="ps">Current <see cref="PlayerState"/> of that client.</param>
        /// <param name="itemEntityNum">Entity slot of the world item.</param>
        /// <param name="type">Broad item category.</param>
        /// <param name="tag">WP_* for weapons/ammo; ignored for health.</param>
        /// <param name="quantity">Base quantity defined in the item descriptor.</param>
        /// <returns>True when the item is consumed; false when the player cannot
        /// benefit from it (health full, ammo full, etc.).</returns>
        public static bool Touch_Item(int clientNum, PlayerState ps, int itemEntityNum,
            ItemType type, int tag, int quantity)
        {
            int actualQuantity;

            switch (type)
            {
                // -- Health ---------------------------------------------------
                case ItemType.Health:
                {
                    int currentHp = ps.Stats[GameConst.STAT_HEALTH];
                    if (currentHp >= MAX_HEALTH)
                        return false;   // already at or above cap — cannot benefit

                    // Clamp so we do not exceed MAX_HEALTH.
                    actualQuantity = Math.Min(quantity, MAX_HEALTH - currentHp);
                    break;
                }

                // -- Weapon ---------------------------------------------------
                case ItemType.Weapon:
                {
                    if (tag <= GameConst.WP_NONE || tag >= GameConst.WP_NUM_WEAPONS)
                        return false;

                    if (HasWeapon(ps, tag))
                    {
                        // Player already owns this weapon — try to give reserve ammo
                        // instead (mirrors ET g_items.c pickup_weapon behaviour).
                        int maxAmmo  = GetMaxAmmo(tag);
                        int curAmmo  = ps.Ammo[tag];
                        if (maxAmmo <= 0 || curAmmo >= maxAmmo)
                            return false;   // ammo already full too

                        actualQuantity = Math.Min(maxAmmo - curAmmo, maxAmmo / 2);
                        // Recast as an ammo pickup for the event payload.
                        G_PickupItem(clientNum, ps, itemEntityNum,
                            ItemType.Ammo, tag, actualQuantity);
                        G_SetRespawn(itemEntityNum, ITEM_RESPAWN_WEAPON);
                        return true;
                    }

                    // Player does not have this weapon — grant it.
                    actualQuantity = quantity;   // quantity unused for weapons per se
                    break;
                }

                // -- Ammo -----------------------------------------------------
                case ItemType.Ammo:
                {
                    if (tag <= GameConst.WP_NONE || tag >= GameConst.WP_NUM_WEAPONS)
                        return false;

                    int maxAmmo = GetMaxAmmo(tag);
                    int curAmmo = ps.Ammo[tag];

                    if (maxAmmo <= 0 || curAmmo >= maxAmmo)
                        return false;   // ammo already maxed

                    actualQuantity = Math.Min(quantity, maxAmmo - curAmmo);
                    break;
                }

                // -- Team (flags, objectives) ---------------------------------
                case ItemType.Team:
                {
                    // Flag/objective pickups always succeed; quantity is unused.
                    actualQuantity = 1;
                    break;
                }

                // -- Holdable / PowerUp / Armor / Key / Bad -------------------
                default:
                    // Not used in ET MP; silently reject.
                    return false;
            }

            // All checks passed — raise the pickup event and schedule respawn.
            G_PickupItem(clientNum, ps, itemEntityNum, type, tag, actualQuantity);

            int respawnDelay = (type == ItemType.Weapon)
                ? ITEM_RESPAWN_WEAPON
                : ITEM_RESPAWN_NORMAL;

            G_SetRespawn(itemEntityNum, respawnDelay);
            return true;
        }

        /// <summary>
        /// Unconditionally raises <see cref="OnItemPickup"/> with the supplied
        /// parameters.  Call this directly only when the eligibility checks in
        /// <see cref="Touch_Item"/> have already been satisfied (e.g. by a
        /// scripted give command).
        /// </summary>
        public static void G_PickupItem(int clientNum, PlayerState ps, int itemEntityNum,
            ItemType type, int tag, int quantity)
        {
            var info = new PickupInfo
            {
                ClientNum     = clientNum,
                ItemEntityNum = itemEntityNum,
                Type          = type,
                Tag           = tag,
                Quantity      = quantity,
            };

            OnItemPickup?.Invoke(info);
        }

        // ---------------------------------------------------------------------
        // Respawn scheduling
        // ---------------------------------------------------------------------

        /// <summary>
        /// Schedules <paramref name="itemEntityNum"/> to respawn after
        /// <paramref name="delay"/> milliseconds by raising
        /// <see cref="OnItemRespawn"/>.
        ///
        /// The game layer is responsible for hiding the item entity for the
        /// specified delay and making it touchable again afterwards.
        /// </summary>
        /// <param name="itemEntityNum">Entity slot of the consumed item.</param>
        /// <param name="delay">Delay in milliseconds before the item reappears.</param>
        public static void G_SetRespawn(int itemEntityNum, int delay)
        {
            var info = new RespawnInfo
            {
                ItemEntityNum = itemEntityNum,
                DelayMs       = delay,
            };

            OnItemRespawn?.Invoke(info);
        }

        // ---------------------------------------------------------------------
        // Weapon drop
        // ---------------------------------------------------------------------

        /// <summary>
        /// Drops <paramref name="weapon"/> from the player described by
        /// <paramref name="ps"/>.  Raises <see cref="OnItemDrop"/> so the game
        /// layer can spawn the world item entity.
        ///
        /// The drop velocity is computed from the player's current view angles:
        /// a forward component of <see cref="DROP_SPEED_FORWARD"/> units/s is
        /// combined with a fixed upward component to produce a convincing arc.
        ///
        /// No eligibility check is performed here; the caller is responsible for
        /// ensuring the player actually carries the weapon.
        /// </summary>
        /// <param name="clientNum">Client that is dropping the weapon.</param>
        /// <param name="ps">Current <see cref="PlayerState"/> of that client.</param>
        /// <param name="weapon">WP_* identifier of the dropped weapon.</param>
        public static void G_DropItem(int clientNum, PlayerState ps, int weapon)
        {
            if (weapon <= GameConst.WP_NONE || weapon >= GameConst.WP_NUM_WEAPONS)
                return;

            // Build the drop origin from the player's current position.
            var origin = new Vector3(ps.Origin0, ps.Origin1, ps.Origin2);

            // Derive the forward direction from the player's view yaw.
            // ViewAngles: (pitch=0, yaw=1, roll=2).
            float yawRad   = ps.ViewAngles1 * Mathf.Deg2Rad;
            float pitchRad = ps.ViewAngles0 * Mathf.Deg2Rad;

            // ET coordinate convention: X=forward, Y=left, Z=up.
            float cosPitch = Mathf.Cos(pitchRad);
            var forward = new Vector3(
                Mathf.Cos(yawRad) * cosPitch,
                Mathf.Sin(yawRad) * cosPitch,
                -Mathf.Sin(pitchRad));

            var velocity = forward * DROP_SPEED_FORWARD
                         + Vector3.up * DROP_SPEED_UP;

            var info = new DropInfo
            {
                ClientNum = clientNum,
                Weapon    = weapon,
                Origin    = origin,
                Velocity  = velocity,
            };

            OnItemDrop?.Invoke(info);
        }
    }
}
