// CheatsCatalog.cs
// The Debug rows (remake-only, see Cheats) as OptionDefs, so the main menu's Debug panel, the pause menu's and the
// station system menu's Debug pages build them with OptionControl like the options. Toggles everywhere; the actions
// only where a game runs (the pause menu and the station), 'notify' reports what they did.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UIE = UnityEngine.UIElements;

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
            Toggle("cheatPrimaryCooldown", () => X("cheatNoPrimaryCooldown", "No primary weapon cooldown"),
                   () => Cheats.NoPrimaryCooldown, v => Cheats.NoPrimaryCooldown = v),
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
            Button("cheatKaamo", () => X("cheatFillKaamo", "Fill the Kaamo Club (every item and ship)"), () => notify?.Invoke(Cheats.FillKaamoClub(db))),
        };

        static string Credits() => $"{X("cheatCreditsNow", "Credits")}: {Session.Credits:N0}";

        // ---- any item (the pause menu and the station) -----------------------------------------------------------

        static int itemCategory, itemPick, amountPick;
        static readonly int[] Amounts = { 1, 5, 10, 50, 100, 1000 };

        static string ItemName(ItemData it)
        {
            string n = GameNames.Item(it.index);
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
                ItemPicker(Current),
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

        /// <summary>Give items' Item row: the type's items by their shop icons.</summary>
        static OptionDef ItemPicker(Func<List<ItemData>> current) =>
            GridPicker("debugItem", () => X("debugItem", "Item"), current, it => it.index.ToString(),
                it => ItemInfo.ItemIcon(it.index), ItemName, it => $"{it.index} · {ItemName(it)}", () => itemPick, i => itemPick = i);

        /// <summary>A Debug row picking from a grid of shop icons with their names (remake, #82: Give items' Item, the Ships
        /// tab's Ship; a tap / click picks one, the picked one framed and kept in view). The row's stepper stays as an
        /// invisible focus stop for keys and controllers: selected (the grid framed amber), left / right step through the
        /// icons. The grid follows the pick and the list by itself (a scheduled check); up / down inside it go through
        /// MoveVertical. 'key' tells entries apart (a changed list is built again), 'icon' may be null; 'hint' (optional)
        /// fills a panel under the grid for the entry under the pointer, else the picked one (the presets' equipment).</summary>
        static OptionDef GridPicker<T>(string id, Func<string> label, Func<List<T>> current, Func<T, string> key,
            Func<T, UnityEngine.Texture2D> icon, Func<T, string> name, Func<T, string> stepText, Func<int> getPick, Action<int> setPick,
            Action<T, UIE.VisualElement> hint = null)
        {
            string[] Names() => current().ConvertAll(x => stepText(x)).ToArray();
            var def = Choice(id, label, false, Names, getPick, setPick);
            def.extra = () =>
            {
                var grid = new UIE.ScrollView(UIE.ScrollViewMode.Vertical);
                grid.AddToClassList("debug-item-grid");
                grid.contentContainer.AddToClassList("debug-item-grid-content");
                grid.horizontalScrollerVisibility = UIE.ScrollerVisibility.Hidden;
                // No scroll bar (wheel, drag and the keys scroll it): one appearing narrowed the rows by a cell.
                grid.verticalScrollerVisibility = UIE.ScrollerVisibility.Hidden;
                grid.focusable = false;
                // The row's extra: the grid, and under it the hint panel when there is one.
                var box = new UIE.VisualElement();
                box.Add(grid);
                var hintBox = hint != null ? new UIE.VisualElement() : null;
                if (hintBox != null) { hintBox.AddToClassList("debug-grid-hint"); box.Add(hintBox); }
                int hover = -1;
                string hintShown = null;
                var cells = new List<UIE.VisualElement>();
                List<string> shown = null;
                int shownPick = -1;
                float cellWidth = -1f;
                int columns = 1;
                // The cells share the row's width out evenly (as many as fit at 138 or more), so no gap is left on the right.
                void FitCells()
                {
                    float w = grid.contentViewport.layout.width - 8f;   // the content's padding
                    if (float.IsNaN(w) || w <= 0f) return;
                    bool phone = false;
                    for (var e = grid.parent; e != null && !phone; e = e.parent) phone = e.ClassListContains("layout-phone");
                    int n = Math.Max(1, (int)(w / (phone ? 156f : 138f)));
                    columns = n;
                    float cw = (float)Math.Floor(w / n) - 6f;           // the cell's margins
                    if (Math.Abs(cw - cellWidth) < 0.5f) return;
                    cellWidth = cw;
                    foreach (var c in cells) c.style.width = cw;
                }
                grid.contentViewport.RegisterCallback<UIE.GeometryChangedEvent>(_ => FitCells());
                // The icons in a row as laid out (the first row's), so up / down land on the one straight above / below.
                int Columns()
                {
                    if (cells.Count == 0 || float.IsNaN(cells[0].layout.y)) return columns;
                    int n = 1;
                    while (n < cells.Count && Math.Abs(cells[n].layout.y - cells[0].layout.y) < 1f) n++;
                    return n;
                }
                void Build(List<T> items)
                {
                    grid.Clear();
                    cells.Clear();
                    for (int i = 0; i < items.Count; i++)
                    {
                        int index = i;
                        var cell = new UIE.VisualElement();
                        cell.AddToClassList("debug-item-cell");
                        var image = new UIE.VisualElement { pickingMode = UIE.PickingMode.Ignore };
                        image.AddToClassList("debug-item-icon");
                        var tex = icon(items[i]);
                        if (tex != null) image.style.backgroundImage = new UIE.StyleBackground(tex);
                        else cell.AddToClassList("debug-item-cell--text");
                        var text = new UIE.Label(name(items[i])) { pickingMode = UIE.PickingMode.Ignore };
                        text.AddToClassList("debug-item-name");
                        cell.Add(image);
                        cell.Add(text);
                        // A tap that didn't scroll the grid picks (a drag on a touch screen scrolls it).
                        UnityEngine.Vector2 down = default;
                        cell.RegisterCallback<UIE.PointerDownEvent>(e => down = e.position);
                        cell.RegisterCallback<UIE.PointerUpEvent>(e =>
                        {
                            if ((((UnityEngine.Vector2)e.position) - down).sqrMagnitude > 12f * 12f) return;
                            setPick(index);
                            if (box.parent != null) UIE.UQueryExtensions.Q<ChoiceRow>(box.parent)?.SetWithoutNotify(Names(), index);
                        });
                        cell.RegisterCallback<UIE.PointerEnterEvent>(_ => hover = index);
                        cell.RegisterCallback<UIE.PointerLeaveEvent>(_ => { if (hover == index) hover = -1; });
                        if (cellWidth > 0f) cell.style.width = cellWidth;
                        grid.Add(cell);
                        cells.Add(cell);
                    }
                    shown = items.ConvertAll(x => key(x));
                    shownPick = -1;
                }
                void Sync()
                {
                    var items = current();
                    bool same = shown != null && shown.Count == items.Count;
                    for (int i = 0; same && i < items.Count; i++) same = shown[i] == key(items[i]);
                    if (!same) { Build(items); hover = -1; }
                    int pick = Math.Clamp(getPick(), 0, Math.Max(0, cells.Count - 1));
                    if (hintBox != null && items.Count > 0)
                    {
                        // The entry under the pointer, else the picked one; filled again when it or its content changes.
                        var entry = items[hover >= 0 && hover < items.Count ? hover : pick];
                        string k = key(entry);
                        if (k != hintShown) { hintShown = k; hintBox.Clear(); hint(entry, hintBox); }
                    }
                    if (pick == shownPick || cells.Count == 0) return;
                    if (shownPick >= 0 && shownPick < cells.Count) cells[shownPick].RemoveFromClassList("debug-item-cell--picked");
                    cells[pick].AddToClassList("debug-item-cell--picked");
                    shownPick = pick;
                    var target = cells[pick];
                    grid.schedule.Execute(() => grid.ScrollTo(target));   // once it is laid out
                }
                grid.schedule.Execute(Sync).Every(100);
                // The stepper hides; its focus (the station's menu) frames the grid like the pause menu's row selection.
                box.RegisterCallback<UIE.AttachToPanelEvent>(_ =>
                {
                    var row = box.parent;
                    var stepper = row != null ? UIE.UQueryExtensions.Q<ChoiceRow>(row) : null;
                    if (stepper == null || stepper.ClassListContains("debug-item-stepper")) return;
                    stepper.AddToClassList("debug-item-stepper");
                    // Up / down move the pick a row in the grid (MoveVertical; the menus ask before moving on): only from its
                    // top / bottom row do they go on to the row above / below. A short last row: down picks its last icon.
                    stepper.userData = (Func<int, bool>)(dy =>
                    {
                        int count = cells.Count;
                        if (count == 0) return false;
                        int pick = Math.Clamp(getPick(), 0, count - 1);
                        int cols = Columns();
                        int row = pick / cols, lastRow = (count - 1) / cols;
                        if ((dy < 0 && row == 0) || (dy > 0 && row == lastRow)) return false;
                        int next = Math.Clamp(pick + dy * cols, 0, count - 1);
                        setPick(next);
                        stepper.SetWithoutNotify(Names(), next);
                        return true;
                    });
                    stepper.RegisterCallback<UIE.FocusInEvent>(e => row.AddToClassList("debug-item-row--focus"));
                    stepper.RegisterCallback<UIE.FocusOutEvent>(e => row.RemoveFromClassList("debug-item-row--focus"));
                });
                Sync();
                return box;
            };
            return def;
        }

        /// <summary>A Debug row's own up / down (the item grid's rows): true = it moved inside, the menu stays on it.</summary>
        public static bool MoveVertical(UIE.VisualElement field, int dy) => field?.userData is Func<int, bool> move && move(dy);

        // ---- ship presets (ShipPresets; the pause menu and the station) ---------------------------------------------

        static int presetSlot;

        /// <summary>The Presets tab: the slot (its ship and item count; what it holds on the line under it), then Save the ship
        /// flown now there, Load it (in flight where the player is, docked only a ship the hangar takes) and Delete.</summary>
        public static List<OptionDef> Presets(Database db, World.SpaceLevel flight, World.StationLevel docked, Action<string> notify)
        {
            // The slots as a grid (#82), each by its ship's icon and "N. ship"; an empty one by name.
            List<int> Slots() { var l = new List<int>(); for (int i = 0; i < ShipPresets.Slots; i++) l.Add(i); return l; }
            UnityEngine.Texture2D SlotIcon(int i)
            {
                var p = ShipPresets.Get(i);
                if (p == null) return null;
                var hull = ShipPresets.HullOf(i, db);
                return hull == null ? ItemInfo.ShipIcon(p.ship) : hull.playerShip ? ItemInfo.ShipIcon(hull.stats) : ItemInfo.HullIcon(hull.key);
            }
            string SlotName(int i)
            {
                var p = ShipPresets.Get(i);
                if (p == null) return $"{i + 1}. {X("presetEmpty", "empty")}";
                var hull = ShipPresets.HullOf(i, db);
                string name = hull != null ? hull.label : GameNames.Ship(p.ship);
                int dot = name.IndexOf(" · ", StringComparison.Ordinal);
                if (dot >= 0) name = name.Substring(dot + 3);
                return $"{i + 1}. {name}";   // what it holds: the line above the grid (ShipPresets.Details)
            }
            var slot = GridPicker("debugPresetSlot", () => X("debugPresetSlot", "Preset"), Slots, i => i + "|" + ShipPresets.Label(i, db) + "|" + ShipPresets.Get(i)?.saved,
                SlotIcon, SlotName, i => ShipPresets.Label(i, db), () => presetSlot, i => presetSlot = i, (i, panel) => PresetHint(db, i, panel));
            return new List<OptionDef>
            {
                slot,
                Button("debugPresetSave", () => X("debugPresetSave", "Save current ship here"), () => notify?.Invoke(ShipPresets.Save(presetSlot, db))),
                Button("debugPresetLoad", () => X("debugPresetLoad", "Load this preset"), () => notify?.Invoke(ShipPresets.Load(presetSlot, db, flight, docked))),
                Button("debugPresetDelete", () => X("debugPresetDelete", "Delete"), () => notify?.Invoke(ShipPresets.Delete(presetSlot))),
            };
        }

        /// <summary>The Presets grid's hint: the slot's ship, mods and save time over its equipment, each with its shop icon and
        /// amount (the slot under the pointer, else the picked one); an empty slot says what Save does.</summary>
        static void PresetHint(Database db, int slot, UIE.VisualElement panel)
        {
            var p = ShipPresets.Get(slot);
            var head = new UIE.Label { pickingMode = UIE.PickingMode.Ignore };
            head.AddToClassList("debug-grid-hint-title");
            panel.Add(head);
            if (p == null)
            {
                head.text = $"{slot + 1}. {X("presetEmptyHelp", "An empty slot: Save puts the ship you fly now and its equipment here.")}";
                return;
            }
            var hull = ShipPresets.HullOf(slot, db);
            string ship = hull != null ? hull.label : GameNames.Ship(p.ship);
            int dot = ship.IndexOf(" · ", StringComparison.Ordinal);
            if (dot >= 0) ship = ship.Substring(dot + 3);
            head.text = $"{slot + 1}. {ship}" + (p.mods.Count > 0 ? $"  ·  {p.mods.Count} {X("presetMods", "ship mods")}" : "")
                        + (string.IsNullOrEmpty(p.saved) ? "" : $"  ·  {p.saved}");
            var chips = new UIE.VisualElement { pickingMode = UIE.PickingMode.Ignore };
            chips.AddToClassList("debug-grid-hint-items");
            panel.Add(chips);
            int shownItems = 0;
            // One chip per item: two of the same gun read "×2", a secondary's stacks their ammo together.
            var merged = new List<ShipPresets.Stack>();
            foreach (var st in p.equipment)
            {
                var same = merged.Find(m => m.item == st.item);
                if (same != null) same.amount += Math.Max(1, st.amount);
                else merged.Add(new ShipPresets.Stack { item = st.item, amount = Math.Max(1, st.amount) });
            }
            foreach (var st in merged)
            {
                if (db.Item(st.item) == null || Modding.ModContent.IsMissingItem(st.item)) continue;
                var chip = new UIE.VisualElement { pickingMode = UIE.PickingMode.Ignore };
                chip.AddToClassList("debug-grid-hint-chip");
                var ic = new UIE.VisualElement { pickingMode = UIE.PickingMode.Ignore };
                ic.AddToClassList("debug-grid-hint-icon");
                var tex = ItemInfo.ItemIcon(st.item);
                if (tex != null) ic.style.backgroundImage = new UIE.StyleBackground(tex);
                var t = new UIE.Label(st.amount > 1 ? $"{GameNames.Item(st.item)} ×{st.amount}" : GameNames.Item(st.item)) { pickingMode = UIE.PickingMode.Ignore };
                t.AddToClassList("debug-grid-hint-text");
                chip.Add(ic);
                chip.Add(t);
                chips.Add(chip);
                shownItems++;
            }
            if (shownItems == 0) head.text += $"  ·  {X("presetNoEquipment", "no equipment")}";
        }

        // ---- fly any ship (World.PlayerHull; the pause menu and the station) ----------------------------------------

        static int hullCategory = -1, hullPick;

        /// <summary>The Ships tab, like Give items: the ship type (the races, Other, Modded, Not normally flyable), the ship, then
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
            // The ships by their shop icons (#82); the ones not normally flyable (freighters, the battleship and the capital
            // ships: no shop image, 13-15 only a "?") by the remake's rendered ones (ItemInfo.HullIcon), else by name.
            var pick = GridPicker("debugHull", () => X("debugHull", "Ship"), Current, h => h.key,
                h => h.playerShip ? ItemInfo.ShipIcon(h.stats) : ItemInfo.HullIcon(h.key),
                h => { int dot = h.label.IndexOf(" · ", StringComparison.Ordinal); return dot >= 0 ? h.label.Substring(dot + 3) : h.label; },
                h => h.label, () => Math.Clamp(hullPick, 0, Math.Max(0, Current().Count - 1)), i => hullPick = i);
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

        static int shipRace, shipPick, behaviourPick, objectCategory, objectPick, capitalPick;
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
                // A stepper like the rows around it: four segments didn't fit the Debug page's width and their labels ran together.
                Choice("debugBehaviour", () => X("debugBehaviour", "Behaviour"), false,
                    () => new[] { X("debugHostile", "Hostile"), X("debugNormal", "By standing"), X("debugFriendly", "Friendly"), X("debugNeutral", "Neutral") },
                    () => behaviourPick, i => behaviourPick = i),
                Button("debugSpawnShip", () => X("debugSpawnShip", "Spawn ship"), () =>
                    notify?.Invoke(World.DebugSpawner.SpawnShip(level, Races[shipRace], ships[Math.Clamp(shipPick, 0, ships.Count - 1)],
                                                                (World.DebugSpawner.Behaviour)behaviourPick))),
                // Remake (World.CapitalShips): a capital ship with its enhancements, or a whole fleet battle, to test them.
                Choice("debugCapital", () => X("debugCapital", "Capital ship"), false,
                    () => new[] { World.DebugSpawner.CapitalName(Flight.SpawnSpec.CapitalBattleship), World.DebugSpawner.CapitalName(Flight.SpawnSpec.CapitalCarrier),
                                  World.DebugSpawner.CapitalName(Flight.SpawnSpec.CapitalVossk) },
                    () => capitalPick, i => capitalPick = i),
                Button("debugSpawnCapital", () => X("debugSpawnCapital", "Spawn capital ship"), () =>
                    notify?.Invoke(World.DebugSpawner.SpawnCapital(level, capitalPick + 1))),
                Button("debugSpawnBattle", () => X("debugSpawnBattle", "Spawn fleet battle"), () =>
                    notify?.Invoke(World.DebugSpawner.SpawnFleetBattle(level))),
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
