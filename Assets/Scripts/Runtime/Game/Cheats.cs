// Cheats.cs
// Remake-only testing tools behind the main menu's Debug panel (F10 / LB + RB held / five taps on the version text) and, once that
// has been opened, the in-flight pause menu's Debug page. Toggles live in PlayerPrefs (they stay on across scenes and
// restarts); actions change the running game's Session (and the live ship in flight). Nothing here is in the original.
//   God mode         the player takes no damage, the gamma pool doesn't drain, volatile goods don't blow up
//   Infinite ammo    secondaries (missiles, mines, bombs...) aren't used up
//   No secondary cooldown  a secondary fires again at once (its reload time skipped; the ones still in flight count)
//   One-hit kills    the player's shots destroy whatever they hit (story ships the script keeps invulnerable excepted)
//   Instant locks    scanner / station / planet / asteroid locks complete at once
//   Free shopping    items and ships cost nothing
//   Free jumps       Khador jumps and the cloak need no energy cells
// Multiplayer: only when the session allows it (the Host card's Debug menu switch, a dedicated server's -allowdebug; off
// by default; then every player gets the Debug pages, unlocked or not): otherwise the Debug pages are gone and every
// cheat flag reads off (Allowed), so toggles left on in single player don't follow a player into a session. The toggles
// themselves stay saved for single player. An admin's /cheat (NetAdmin) grants a flag to one player for the session
// (Grant: not saved, whatever the session allows).
// Plain C#: the hooks read the flags (Target, PlayerHealth, VolatileCargo, WeaponSystem, CombatRadar, Mining,
// Navigation, Hangar, GalaxyMap, PlayerCloak).

using System.Collections.Generic;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Cheats
    {
        /// <summary>The flags an admin's /cheat can grant: the command's word and the flag's key.</summary>
        public static readonly (string word, string key)[] Flags =
        {
            ("god", "godMode"), ("ammo", "infiniteAmmo"), ("cooldown", "noSecondaryCooldown"), ("primary", "noPrimaryCooldown"), ("boost", "noBoostCooldown"),
            ("onehit", "oneHitKills"), ("locks", "instantLocks"), ("shopping", "freeShopping"), ("jumps", "freeJumps"),
        };

        /// <summary>Multiplayer: the flags an admin granted this player for the session (NetAdmin's /cheat).</summary>
        static readonly HashSet<string> granted = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => granted.Clear();

        /// <summary>A new session: no grants carried over.</summary>
        public static void ClearGranted() => granted.Clear();

        /// <summary>Grants (or takes back) flag 'key' for this session; on = null toggles. The flag's state now.</summary>
        public static bool Grant(string key, bool? on)
        {
            bool now = on ?? !granted.Contains(key);
            if (now) granted.Add(key); else granted.Remove(key);
            return now;
        }

        /// <summary>The Debug panel has been opened once: the pause menu shows its Debug page from then on.</summary>
        public static bool Unlocked { get => Get("unlocked"); set => Set("unlocked", value); }

        /// <summary>The Debug tools may be used now: always in single player; in a multiplayer session only when its host or
        /// dedicated server allows them (NetGame.DebugAllowed).</summary>
        public static bool Allowed => !NetSession.Active || NetSession.DebugAllowed;

        /// <summary>The pause menu and the station's system menu show their Debug page: in single player once the Debug panel
        /// has been opened (Unlocked); in a multiplayer session whenever the session allows it, for every player and device
        /// (a phone that never opened the main menu's Debug panel had no page, only the host's own device did).</summary>
        public static bool PageShown => NetSession.Active ? Allowed : Unlocked;

        public static bool GodMode { get => On("godMode"); set => Set("godMode", value); }
        public static bool InfiniteAmmo { get => On("infiniteAmmo"); set => Set("infiniteAmmo", value); }
        public static bool NoSecondaryCooldown { get => On("noSecondaryCooldown"); set => Set("noSecondaryCooldown", value); }
        /// <summary>The primary guns fire again at once while held (no reload; the bullet pool still limits the shots in flight).</summary>
        public static bool NoPrimaryCooldown { get => On("noPrimaryCooldown"); set => Set("noPrimaryCooldown", value); }
        /// <summary>The booster is ready again as soon as a boost ends (no recharge; a booster is still needed).</summary>
        public static bool NoBoostCooldown { get => On("noBoostCooldown"); set => Set("noBoostCooldown", value); }
        public static bool OneHitKills { get => On("oneHitKills"); set => Set("oneHitKills", value); }
        public static bool InstantLocks { get => On("instantLocks"); set => Set("instantLocks", value); }
        public static bool FreeShopping { get => On("freeShopping"); set => Set("freeShopping", value); }
        public static bool FreeJumps { get => On("freeJumps"); set => Set("freeJumps", value); }

        /// <summary>The lock time a scanner check should use ('ms' = the normal one).</summary>
        public static int LockMs(int ms) => InstantLocks ? 0 : ms;

        // ---- actions ----------------------------------------------------------------------------------------------

        public static void AddCredits(int amount)
        {
            Session.Credits = (int)System.Math.Min((long)Session.Credits + amount, 999999999L);   // not Mathf.Min: a float rounds 999 999 999 up to 1e9
        }

        /// <summary>Hull, shield and armor full (the live ship too, in flight), the gamma pool refilled.</summary>
        public static void Repair()
        {
            Session.PlayerHull = Session.PlayerArmor = -1;
            Session.PlayerShield = -1f;
            if (Session.PlayerGamma >= 0f) Session.PlayerGamma = 100f;
            GameHooks.RepairLiveShip?.Invoke();   // the live ship in flight (PlayerHealth.RepairLive)
        }

        /// <summary>Every system visible on the star map (Session.SystemVisible).</summary>
        public static void RevealAllSystems()
        {
            if (Session.SystemVisible == null) return;
            for (int i = 0; i < Session.SystemVisible.Length; i++) Session.SystemVisible[i] = true;
        }

        /// <summary>Both standing axes neutral and every station's grudge forgotten (no race hostile but pirates / Void).</summary>
        public static void MakePeace()
        {
            if (Session.Standing != null) for (int i = 0; i < Session.Standing.Length; i++) Session.Standing[i] = 0;
            foreach (var d in Modding.ModRaces.All) if (!d.hostile) Modding.ModRaces.SetStanding(d.race, 0);   // remake mods: mod races too
            Session.AttackedStations.Clear();
        }

        /// <summary>'amount' units of any item into the hold (a secondary's amount is its ammo once mounted).</summary>
        public static void GiveItem(int item, int amount) => Shop.AddToCargo(item, amount);

        /// <summary>Remake (a mission's hand-in, /take): up to 'amount' units of an item out of the hold (0 or less: all of
        /// them), then, with 'mounted', out of the mounted equipment too (a secondary by its ammo, anything else one per
        /// mount), like Story's step 78 takes the jump drive. The units taken; 'unmounted' when a mount changed. With none
        /// left aboard the story's unsaleable flag goes with it (the original's flag was on that Item).</summary>
        public static int TakeItem(int item, int amount, bool mounted, out bool unmounted)
        {
            unmounted = false;
            int want = amount > 0 ? amount : int.MaxValue, taken = Mathf.Min(want, Shop.CargoOf(item));
            Shop.RemoveFromCargo(item, taken);
            for (int i = Session.Equipment.Count - 1; mounted && i >= 0 && taken < want; i--)
            {
                var e = Session.Equipment[i];
                if (e.item != item || e.builtIn) continue;   // a mod hull's built-in items stay
                int units = Mathf.Max(1, e.amount), take = Mathf.Min(want - taken, units);
                if (take >= units) Session.Equipment.RemoveAt(i);
                else e.amount -= take;
                taken += take;
                unmounted = true;
            }
            if (Shop.CargoOf(item) == 0 && !Session.Equipment.Exists(e => e.item == item)) Session.Unsaleable.Remove(item);
            return taken;
        }

        /// <summary>Docked: 'amount' of the item into the hold, then mounted like the hangar does (a one-per-ship item swaps
        /// the mounted one into the hold); the result text.</summary>
        public static string GiveAndMount(Database db, StationStock stock, int item, int amount)
        {
            GiveItem(item, amount);
            if (stock == null) return Localization.Extra("cheatMountDocked", "Mounting works while docked; it is in the hold.");
            var hangar = new Hangar(db, stock);
            switch (hangar.CanMount(item, out int swapWith))
            {
                case Hangar.Result.Ok: hangar.Mount(item); return Localization.Extra("cheatMounted", "Mounted.");
                case Hangar.Result.Swap: hangar.Swap(swapWith, item); return Localization.Extra("cheatMountedSwap", "Mounted; the old one is in the hold.");
                case Hangar.Result.NoFreeSlot: return Localization.Extra("cheatNoSlot", "No free slot: it is in the hold.");
                default: return Localization.Extra("cheatNotMountable", "Not mountable: it is in the hold.");
            }
        }

        /// <summary>The Kaamo Club owned (no siege or purchase) and stocked: one of every item the storage takes (50 of each
        /// secondary, so they come with ammo; not the story's unsaleable ones, which the storage refuses, 323) and one hull of
        /// every ship a player can own (one per type, like Station::addShip; not the freighters 13 / 15, the battleship 14 or
        /// the ship flown now). Items already stored are topped up. The result text.</summary>
        public static string FillKaamoClub(Database db)
        {
            Session.KaamoState = 3;
            int items = 0, ships = 0;
            foreach (var it in db.Items)
            {
                int i = it.index;
                if (Modding.ModContent.IsMissingItem(i) || !Hangar.IsSaleable(i)) continue;
                int want = it.TypeId == 1 ? 50 : 1;
                var s = Session.KaamoItems.Find(x => x.item == i);
                if (s == null) { Session.KaamoItems.Add(new ItemStack(i, want)); items++; }
                else if (s.amount < want) s.amount = want;
            }
            for (int i = 0; i < db.Ships.Count; i++)
            {
                if (i == 13 || i == 14 || i == 15 || i == Session.ShipIndex) continue;
                if (Modding.ModContent.IsMissingShip(i) || db.ShipAssembly(i) == null) continue;   // a mod that is off; 50 / 53 have no model
                if (KaamoClub.Store(i, Shop.RaceOfShip(i), new List<int>())) ships++;
            }
            return string.Format(Localization.Extra("cheatKaamoFilled", "Kaamo Club owned: {0} items and {1} ships added to its storage (Shima system)."), items, ships);
        }

        /// <summary>'amount' more Energy Cells (122) in the hold.</summary>
        public static void AddEnergyCells(int amount) => Shop.AddToCargo(GalaxyMap.EnergyCellItem, amount);

        /// <summary>Every mounted secondary back to a full stack of 50 (missiles, mines, bombs...).</summary>
        public static void RefillAmmo(Database db)
        {
            foreach (var e in Session.Equipment)
                if (db.Item(e.item)?.TypeId == 1) e.amount = Mathf.Max(e.amount, 50);   // the gun rigs share these stacks
        }

        /// <summary>A cheat flag: its saved value, off while a session doesn't allow the Debug tools.</summary>
        // Granted flags: an admin's /cheat in a session, the Mod Builder's test-flight toggles (Modding.ModBuilder).
        static bool On(string key) => (Allowed && Get(key))
                                      || ((NetSession.Active || Modding.ModManager.BuilderOpen) && granted.Contains(key));

        static bool Get(string key) { try { return PlayerPrefs.GetInt("cheat_" + key, 0) != 0; } catch { return false; } }
        static void Set(string key, bool on) { PlayerPrefs.SetInt("cheat_" + key, on ? 1 : 0); PlayerPrefs.Save(); }
    }
}
