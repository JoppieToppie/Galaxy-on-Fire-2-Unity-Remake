// CheatsCatalog.cs
// The Debug rows (remake-only, see Cheats) as OptionDefs, so the main menu's Debug panel, the pause menu's and the
// station system menu's Debug pages build them with OptionControl like the options. Toggles everywhere; the actions
// only where a game runs (the pause menu and the station), 'notify' reports what they did.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class CheatsCatalog
    {
        static string X(string key, string english) => Localization.Extra(key, english);

        public static List<OptionDef> Toggles() => new List<OptionDef>
        {
            Toggle("cheatGod", () => X("cheatGodMode", "God mode"), () => Cheats.GodMode, v => Cheats.GodMode = v),
            Toggle("cheatAmmo", () => X("cheatInfiniteAmmo", "Infinite ammo"), () => Cheats.InfiniteAmmo, v => Cheats.InfiniteAmmo = v),
            Toggle("cheatSecondaryCooldown", () => X("cheatNoSecondaryCooldown", "No secondary weapon cooldown"),
                   () => Cheats.NoSecondaryCooldown, v => Cheats.NoSecondaryCooldown = v),
            Toggle("cheatBoostCooldown", () => X("cheatNoBoostCooldown", "No boost cooldown"),
                   () => Cheats.NoBoostCooldown, v => Cheats.NoBoostCooldown = v),
            Toggle("cheatOneHit", () => X("cheatOneHitKills", "One-hit kills"), () => Cheats.OneHitKills, v => Cheats.OneHitKills = v),
            Toggle("cheatLocks", () => X("cheatInstantLocks", "Instant locks"), () => Cheats.InstantLocks, v => Cheats.InstantLocks = v),
            Toggle("cheatShop", () => X("cheatFreeShopping", "Free shopping"), () => Cheats.FreeShopping, v => Cheats.FreeShopping = v),
            Toggle("cheatJumps", () => X("cheatFreeJumps", "Free jumps (no energy cells)"), () => Cheats.FreeJumps, v => Cheats.FreeJumps = v),
        };

        public static List<OptionDef> Actions(Database db, Action<string> notify, World.SpaceLevel flight = null) => new List<OptionDef>
        {
            Button("cheat100k", () => X("cheatCredits100k", "+100 000 credits"), () => { Cheats.AddCredits(100000); notify?.Invoke(Credits()); }),
            Button("cheat1m", () => X("cheatCredits1m", "+1 000 000 credits"), () => { Cheats.AddCredits(1000000); notify?.Invoke(Credits()); }),
            Button("cheatRepair", () => X("cheatRepair", "Repair ship"), () => { Cheats.Repair(); notify?.Invoke(X("cheatRepaired", "Hull, shield and armor repaired.")); }),
            Button("cheatAmmoRefill", () => X("cheatRefillAmmo", "Refill secondaries (50 each)"), () => { Cheats.RefillAmmo(db); notify?.Invoke(X("cheatRefilled", "Secondaries refilled.")); }),
            Button("cheatCells", () => X("cheatEnergyCells", "+20 energy cells"), () => { Cheats.AddEnergyCells(20); notify?.Invoke(X("cheatCellsAdded", "20 energy cells added to the hold.")); }),
            Button("cheatReveal", () => X("cheatRevealMap", "Reveal all systems"), () => { Cheats.RevealAllSystems(); notify?.Invoke(X("cheatRevealed", "Every system is on the star map.")); }),
            Button("cheatPeace", () => X("cheatMakePeace", "Make peace with all races"), () => { Cheats.MakePeace(); notify?.Invoke(X("cheatPeaceMade", "Standing neutral with every race.")); }),
        };

        static string Credits() => $"{X("cheatCreditsNow", "Credits")}: {Session.Credits:N0}";

        // ---- any item (the pause menu and the station) -----------------------------------------------------------

        static int itemCategory, itemPick, amountPick;
        static readonly int[] Amounts = { 1, 5, 10, 50, 100, 1000 };

        static string ItemName(ItemData it)
        {
            string n = Localization.Get(1274 + it.index);
            return string.IsNullOrEmpty(n) ? it.name : n;
        }

        static string CategoryName(int id)
        {
            string n = Localization.Get(221 + id);
            return string.IsNullOrEmpty(n) ? "#" + id : n;
        }

        static List<int> ItemCategories(Database db)
        {
            var list = new List<int>();
            foreach (var it in db.Items) if (!list.Contains(it.categoryId)) list.Add(it.categoryId);
            list.Sort();
            return list;
        }

        static List<ItemData> ItemsOf(Database db, int category)
        {
            var list = db.Items.FindAll(it => it.categoryId == category);
            list.Sort((a, b) => a.index.CompareTo(b.index));
            return list;
        }

        /// <summary>Category, item, amount, then "Add to hold" (and "Add and mount" when 'dockedStock' is the station's).</summary>
        public static List<OptionDef> Items(Database db, StationStock dockedStock, Action<string> notify)
        {
            var categories = ItemCategories(db);
            List<ItemData> Current() => ItemsOf(db, categories[Math.Clamp(itemCategory, 0, categories.Count - 1)]);
            ItemData Picked()
            {
                var items = Current();
                return items.Count > 0 ? items[Math.Clamp(itemPick, 0, items.Count - 1)] : null;
            }
            var list = new List<OptionDef>
            {
                Choice("debugItemCategory", () => X("debugItemCategory", "Item type"), false,
                    () => categories.ConvertAll(CategoryName).ToArray(), () => itemCategory, i => { itemCategory = i; itemPick = 0; }),
                Choice("debugItem", () => X("debugItem", "Item"), false,
                    () => Current().ConvertAll(it => $"{it.index} · {ItemName(it)}").ToArray(), () => itemPick, i => itemPick = i),
                Choice("debugAmount", () => X("debugAmount", "Amount"), true,
                    () => Array.ConvertAll(Amounts, a => a.ToString()), () => amountPick, i => amountPick = i),
                Button("debugGiveItem", () => X("debugGiveItem", "Add to cargo hold"), () =>
                {
                    var it = Picked();
                    if (it == null) return;
                    Cheats.GiveItem(it.index, Amounts[amountPick]);
                    notify?.Invoke($"+{Amounts[amountPick]} {ItemName(it)}");
                }),
            };
            if (dockedStock != null)
                list.Add(Button("debugMountItem", () => X("debugMountItem", "Add and mount"), () =>
                {
                    var it = Picked();
                    if (it == null) return;
                    notify?.Invoke($"{ItemName(it)}: {Cheats.GiveAndMount(db, dockedStock, it.index, Amounts[amountPick])}");
                }));
            return list;
        }

        // ---- fly any ship (World.PlayerHull; the pause menu and the station) ----------------------------------------

        static int hullCategory = -1, hullPick;

        /// <summary>The Ships tab, like Give items: the ship type (the races, Other, Not normally flyable), the ship, then
        /// "Fly this ship" and "Back to your own ship". In flight the hull swaps where the player is ('flight'); docked
        /// ('docked') only a ship the player can normally own.</summary>
        public static List<OptionDef> Hulls(Database db, World.SpaceLevel flight, World.StationLevel docked, Action<string> notify)
        {
            var categories = World.PlayerHull.Categories(db);
            if (hullCategory < 0)
            {
                // First opened: on the hull flown now.
                var current = World.PlayerHull.Current(db);
                hullCategory = Math.Max(0, current != null ? categories.IndexOf(current.category) : 0);
                hullPick = current != null ? Math.Max(0, World.PlayerHull.OfCategory(db, categories[hullCategory]).IndexOf(current)) : 0;
            }
            List<World.PlayerHull.Hull> Current() =>
                categories.Count > 0 ? World.PlayerHull.OfCategory(db, categories[Math.Clamp(hullCategory, 0, categories.Count - 1)]) : new List<World.PlayerHull.Hull>();
            World.PlayerHull.Hull Picked()
            {
                var list = Current();
                return list.Count > 0 ? list[Math.Clamp(hullPick, 0, list.Count - 1)] : null;
            }
            var pick = Choice("debugHull", () => X("debugHull", "Ship"), false,
                () => Current().ConvertAll(h => h.label).ToArray(), () => Math.Clamp(hullPick, 0, Math.Max(0, Current().Count - 1)), i => hullPick = i);
            pick.description = () => X("debugHullHelp",
                "Not normally flyable ships can't land in a hangar; the capital ships keep their turrets as your auto turrets.");
            return new List<OptionDef>
            {
                Choice("debugHullCategory", () => X("debugHullCategory", "Ship type"), false,
                    () => categories.ToArray(), () => Math.Clamp(hullCategory, 0, categories.Count - 1), i => { hullCategory = i; hullPick = 0; }),
                pick,
                Button("debugHullFly", () => X("debugHullFly", "Fly this ship"), () => notify?.Invoke(World.PlayerHull.Fly(Picked(), flight, docked))),
                Button("debugHullBack", () => X("cheatLeaveBattleship", "Back to your own ship"), () => notify?.Invoke(World.PlayerHull.Restore(flight, docked))),
            };
        }

        // ---- spawn any ship or object (the pause menu, in flight) ---------------------------------------------------

        static int shipRace, shipPick, behaviourPick, objectCategory, objectPick;
        static readonly int[] Races = { 0, 1, 2, 3, Flight.Standing.Pirate, Flight.Standing.Void, Flight.Standing.Specter };

        static string RaceName(int race)
        {
            string n = Localization.Get(406 + race);
            return string.IsNullOrEmpty(n) ? "#" + race : n;
        }

        static List<string> ObjectCategories(Database db)
        {
            var list = new List<string>();
            foreach (var a in db.Assemblies) if (!list.Contains(a.category)) list.Add(a.category);
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static List<AssemblyData> ObjectsOf(Database db, string category)
        {
            var list = db.Assemblies.FindAll(a => a.category == category);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        /// <summary>Race, ship, behaviour, "Spawn ship"; object type, object, "Spawn object" (World.DebugSpawner).</summary>
        public static List<OptionDef> Spawns(Database db, World.SpaceLevel level, Action<string> notify)
        {
            var ships = db.Ships.ConvertAll(sh => sh.index).FindAll(CustomShips.Offered);   // no custom ships while they're off
            ships.Sort();
            var categories = ObjectCategories(db);
            List<AssemblyData> Objects() => ObjectsOf(db, categories[Math.Clamp(objectCategory, 0, categories.Count - 1)]);
            return new List<OptionDef>
            {
                Choice("debugShipRace", () => X("debugShipRace", "Ship race"), false,
                    () => Array.ConvertAll(Races, RaceName), () => shipRace, i => shipRace = i),
                Choice("debugShip", () => X("debugShip", "Ship"), false,
                    () => ships.ConvertAll(i => $"{i} · {World.DebugSpawner.ShipName(db, i)}").ToArray(), () => shipPick, i => shipPick = i),
                Choice("debugBehaviour", () => X("debugBehaviour", "Behaviour"), true,
                    () => new[] { X("debugHostile", "Hostile"), X("debugNormal", "By standing"), X("debugFriendly", "Friendly"), X("debugNeutral", "Neutral") },
                    () => behaviourPick, i => behaviourPick = i),
                Button("debugSpawnShip", () => X("debugSpawnShip", "Spawn ship"), () =>
                    notify?.Invoke(World.DebugSpawner.SpawnShip(level, Races[shipRace], ships[Math.Clamp(shipPick, 0, ships.Count - 1)],
                                                                (World.DebugSpawner.Behaviour)behaviourPick))),
                Choice("debugObjectCategory", () => X("debugObjectCategory", "Object type"), false,
                    () => categories.ToArray(), () => objectCategory, i => { objectCategory = i; objectPick = 0; }),
                Choice("debugObject", () => X("debugObject", "Object"), false,
                    () => Objects().ConvertAll(a => a.name).ToArray(), () => objectPick, i => objectPick = i),
                Button("debugSpawnObject", () => X("debugSpawnObject", "Spawn object"), () =>
                {
                    var objects = Objects();
                    if (objects.Count == 0) return;
                    notify?.Invoke(World.DebugSpawner.SpawnObject(level, objects[Math.Clamp(objectPick, 0, objects.Count - 1)].name));
                }),
            };
        }

        static OptionDef Choice(string id, Func<string> label, bool segmented, Func<string[]> choices, Func<int> get, Action<int> set) =>
            new OptionDef
            {
                id = id, page = OptionPage.Gameplay, kind = OptionKind.Choice, label = label, segmented = segmented,
                choices = choices, getIndex = get, setIndex = set,
            };

        static OptionDef Toggle(string id, Func<string> label, Func<bool> get, Action<bool> set) =>
            new OptionDef { id = id, page = OptionPage.Gameplay, kind = OptionKind.Toggle, label = label, getBool = get, setBool = set };

        static OptionDef Button(string id, Func<string> label, Action action) =>
            new OptionDef { id = id, page = OptionPage.Gameplay, kind = OptionKind.Button, label = label, action = action };
    }
}
