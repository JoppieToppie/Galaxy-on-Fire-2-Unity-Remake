// ModBuilderViewItems.cs
// The Mod Builder's Items kind (part of ModBuilderView): the project's items.json (ProjectItem). Left: the items, new ones
// made from a game item (a copy of it: its type, category, look and behaviour, Modding/README.md "New items") or changes to
// a game item ("override"), Remove. Right: the selected one's name, description, shop values (tech level, occurrence, the
// price range), its own shop icon (a PNG, else the base's) and the stats its base item has, by their readable names
// (Modding.ItemStats); a value left at the base's
// isn't written, so the base's stays. The remake-only stats its kind takes come under them (ExtraStats: missiles and
// salvos for guns and turrets, a turret's range, the mining beam, the phase cloak), and a turret gets its own model (fx
// "turret": the .glb, its size, muzzle and shot start), shown on the turntable once fitted. "Fit to the test ship" mounts it on the Ships tab's ship for Test fly (once the game
// has read the project and numbered a new item). Edits are written at once; the game reads them at the next test flight or
// when the builder leaves this kind (the loadout's pickers then list them).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        VisualElement itemList, itemPage;
        ProjectItem currentItem;
        /// <summary>Item edits the game hasn't read yet.</summary>
        bool itemsChanged;

        static readonly string[] TypeLabels = { "Primary weapon", "Secondary weapon", "Turret", "Equipment", "Commodity" };

        void BuildItemsPanels(VisualElement left, VisualElement right)
        {
            Add(left, new Label(T("mbItems", "Items").ToUpperInvariant()), "mbv-heading", "gof-semibold");
            var scroll = Add(left, new ScrollView(ScrollViewMode.Vertical), "mbv-ship-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(scroll);
            itemList = scroll;
            Act(left, T("mbNewItem", "+ New item"), PickItemBase, "mbv-add");
            Act(left, T("mbChangeItem", "Change a game item"), PickItemOverride, "mbv-add", "mbv-change");
            Act(left, T("mbRemoveItem", "Remove item"), AskRemoveItem, "mbv-danger", "mbv-remove");

            var page = Add(right, new ScrollView(ScrollViewMode.Vertical), "mbv-right-scroll");
            page.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            page.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            itemPage = Add(page, new VisualElement(), "mbv-page");
        }

        /// <summary>The item's number in the game: an override's item, a new item's once the game has read it (-1 before).</summary>
        int ItemIndex(ProjectItem it) =>
            it == null ? -1 : it.IsOverride ? it.OverrideIndex : ModContent.ItemIndexOf(Project.Id + ":" + it.LocalId);

        /// <summary>The icon the builder shows for an item: its own PNG (read from the project as it is now), else the game's
        /// (a new item's once numbered), else its base's.</summary>
        Texture2D ItemIconOf(ProjectItem it)
        {
            if (GameModels.Is(it.Icon) && GameModels.Icon(it.Icon) is Texture2D game) return game;
            if (!string.IsNullOrEmpty(it.Icon) && !GameModels.Is(it.Icon) && ModManager.BuilderProject != null)
            {
                var own = ModMaterials.Texture(ModManager.BuilderProject, it.Icon, false);
                if (own != null) return own;
            }
            int index = ItemIndex(it);
            return index >= 0 ? ItemInfo.ItemIcon(index) : it.Original >= 0 ? ItemInfo.ItemIcon(it.Original) : null;
        }

        string ItemTitle(ProjectItem it) =>
            !string.IsNullOrEmpty(it.Name) ? it.Name : it.Original >= 0 ? GameNames.Item(it.Original) : it.LocalId ?? "?";

        void RebuildItems()
        {
            itemList.Clear();
            if (Project == null) return;
            if (currentItem != null && !Project.Items.Contains(currentItem)) currentItem = null;
            currentItem ??= Project.Items.FirstOrDefault();
            foreach (var it in Project.Items)
            {
                var item = it;
                var row = new Button(() => { sound(false); currentItem = item; RebuildItems(); BuildItemPage(); }) { focusable = false };
                row.AddToClassList("mbv-ship-row");
                row.EnableInClassList("mbv-ship-row--on", it == currentItem);
                var icon = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-ship-icon");
                var tex = ItemIconOf(it);
                if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
                var text = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-ship-text");
                Add(text, new Label(ItemTitle(it)) { pickingMode = PickingMode.Ignore }, "mbv-ship-name");
                string sub = it.IsOverride
                    ? string.Format(T("mbChanges", "Changes {0}"), it.Original >= 0 ? GameNames.Item(it.Original) : (string)it.ToJson()["override"])
                    : string.Format(T("mbBasedOn", "New · based on {0}"), it.Original >= 0 ? GameNames.Item(it.Original) : (string)it.ToJson()["base"]);
                Add(text, new Label(sub) { pickingMode = PickingMode.Ignore }, "mbv-ship-sub");
                itemList.Add(row);
            }
            if (Project.Items.Count == 0)
                Note(itemList, T("mbNoItems", "No items yet. Make one from a game item, or change a game item."));
        }

        void BuildItemPage()
        {
            itemPage.Clear();
            var it = currentItem;
            if (it == null) { Note(itemPage, T("mbPickItem", "Add an item on the left to start.")); return; }
            JsonButton(itemPage, it.Json, () => { ItemEdited(); RebuildItems(); BuildItemPage(); },
                       T("mbJsonItemNote", "Also in items.json: attributes by number, defaultEconomy, fx, available."));
            var db = Database.Shared;
            var original = it.Original >= 0 ? db.Item(it.Original) : null;
            bool isNew = !it.IsOverride;

            var head = Add(itemPage, new VisualElement(), "mbv-item-head");
            var icon = Add(head, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-item-head-icon");
            var tex = ItemIconOf(it);
            if (tex != null) icon.style.backgroundImage = new StyleBackground(tex);
            var headText = Add(head, new VisualElement(), "mbv-ship-text");
            Add(headText, new Label((isNew ? T("mbNewItemSub", "New item") : string.Format(T("mbChanges", "Changes {0}"), GameNames.Item(it.Original))).ToUpperInvariant()), "mbv-heading", "gof-semibold");
            if (original != null)
                Add(headText, new Label($"{T("mbItemType" + original.TypeId, TypeLabels[Mathf.Clamp(original.TypeId, 0, 4)])} · {ItemInfo.Category(original)}"
                                        + (isNew ? "  ·  " + string.Format(T("mbBasedOnShort", "based on {0}"), GameNames.Item(it.Original)) : "")), "mbv-note");

            var name = Text(itemPage, T("mbName", "Name"), it.Name, 40, v => { it.Name = v.Trim(); ItemEdited(); RebuildItems(); });
            if (original != null) name.textEdition.placeholder = GameNames.Item(it.Original);
            var desc = Text(itemPage, T("mbDescription", "Description"), it.Description, 600, v => { it.Description = v; ItemEdited(); }, true);
            if (original != null && it.IsOverride) desc.textEdition.placeholder = GameNames.ItemDescription(it.Original);
            if (original == null)
            {
                Note(itemPage, T("mbItemOtherMod", "Built on another mod's item: set its other values in items.json."));
                return;
            }

            // A new item's values equal to its base's aren't written (the base's stay); an override's are once changed.
            int? Opt(int v, int baseValue) => isNew && v == baseValue ? (int?)null : v;
            Heading(itemPage, T("mbShop", "Shop"));
            var irow = Add(itemPage, new VisualElement(), "mbv-row");
            string iconText = it.Icon == null ? T("mbBaseIcon", "the base item's")
                            : GameModels.Is(it.Icon) ? T("mbGameIcon", "a game item's") : System.IO.Path.GetFileName(it.Icon);
            Add(irow, new Label($"{T("mbIcon", "Icon")}: {iconText}"), "mbv-inline-label");
            // Any game item's icon, no file needed (items.json "icon": "gof2:item_NNN", GameModels.Icon).
            Act(irow, T("mbGameItemButton", "Game item…"), () => ShowPicker(T("mbPickGameIcon", "Use a game item's icon"), GameItemChoices(index =>
            {
                string old = GameModels.Is(it.Icon) ? null : it.Icon;
                it.Icon = GameModels.Ref($"item_{ModContent.ItemLook(index):000}");
                if (old != null) Project.DeleteFile(old, Project.Items.Any(o => o != it && o.Icon == old));
                ItemEdited();
                RebuildItems();
                BuildItemPage();
            })));
            if (ModImport.Available)
                Act(irow, T("mbChoose", "Choose…"), () => PickImage(T("mbPickIcon", "Choose a shop icon (180 x 88)"), path =>
                    {
                        string old = it.Icon;
                        it.Icon = Project.AddFile(path, "icons", it.IsOverride ? "item_" + it.OverrideIndex : it.LocalId, ".png");
                        if (!GameModels.Is(old)) Project.DeleteFile(old, Project.Items.Any(o => o != it && o.Icon == old));
                        ItemEdited();
                        RebuildItems();
                        BuildItemPage();
                    }));
            if (it.Icon != null)
                Act(irow, "×", () =>
                {
                    string old = it.Icon;
                    it.Icon = null;
                    if (!GameModels.Is(old)) Project.DeleteFile(old, Project.Items.Any(o => o != it && o.Icon == old));
                    ItemEdited();
                    RebuildItems();
                    BuildItemPage();
                }, "mbv-step");
            Note(itemPage, T("mbIconNote", "180 x 88 like the game's icons, frame included."));
            Int(itemPage, T("mbItemTechLevel", "Tech level (11-20 sell at tech level 10 stations)"), it.TechLevel ?? original.techLevel, 1, Shop.MaxItemTech, v => { it.TechLevel = Opt(v, original.techLevel); ItemEdited(); });
            Int(itemPage, T("mbOccurrence", "How often shops stock it (0 never - 90)"), it.Occurrence ?? original.occurrence, 0, 100,
                v => { it.Occurrence = Opt(v, original.occurrence); ItemEdited(); });
            // Remake mods: "stock", the units a shop holds (Shop.ModStock); 0 = the amount the game gives its kind of item.
            IntegerField stockField = null, stockMaxField = null;
            void Stock()
            {
                int min = stockField.value, max = Mathf.Max(min, stockMaxField.value);
                if (max != stockMaxField.value) stockMaxField.SetValueWithoutNotify(max);
                it.SetStock(min <= 0 ? (int?)null : min, min <= 0 ? (int?)null : max);
                ItemEdited();
            }
            stockField = Int(itemPage, T("mbStock", "Units a shop stocks (0 = the usual amount for its kind)"), it.StockMin ?? 0, 0, Shop.MaxStock, _ => Stock());
            stockMaxField = Int(itemPage, T("mbStockMax", "At most, for a random amount"), it.StockMax ?? 0, 0, Shop.MaxStock, _ => Stock());
            IntegerField minField = null, maxField = null;
            void Prices()
            {
                int min = minField.value, max = Mathf.Max(min, maxField.value);
                if (max != maxField.value) maxField.SetValueWithoutNotify(max);
                if (isNew && min == original.minPrice && max == original.maxPrice) it.SetPrices(null, null);
                else it.SetPrices(min, max);
                ItemEdited();
            }
            minField = Int(itemPage, T("mbMinPrice", "Lowest price"), it.MinPrice ?? original.minPrice, 0, 999999999, _ => Prices());
            maxField = Int(itemPage, T("mbMaxPrice", "Highest price"), it.MaxPrice ?? original.maxPrice, 0, 999999999, _ => Prices());
            BuildItemMore(it, original);

            Heading(itemPage, T("mbItemStats", "Stats"));
            var names = StatNames();
            int shown = 0;
            foreach (int id in original.attrKeys)
            {
                if (!names.TryGetValue(id, out var statName)) continue;
                int attr = id, baseValue = original.Attr(id);
                Int(itemPage, StatLabel(statName), it.Stat(id) ?? baseValue, -999999999, 999999999, v =>
                {
                    it.SetStat(attr, statName, isNew && v == baseValue ? (int?)null : v);
                    ItemEdited();
                });
                shown++;
            }
            if (shown == 0) Note(itemPage, T("mbNoStats", "This kind of item has no stats to change."));
            else Note(itemPage, T("mbStatsNote", "Times in ms, speeds in game units per ms (20 = 1 m); range = speed x lifetime."));

            BuildExtraStats(it, original);
            if (original.categoryId == 8) BuildTurretModel(it);
            if (original.TypeId <= 2) BuildLook(it, original);

            Heading(itemPage, T("mbTry", "Try it"));
            var tryRow = Add(itemPage, new VisualElement(), "mbv-row");
            if (original.TypeId <= 3) Act(tryRow, T("mbFitItem", "Fit to the test ship"), () => FitItem(it));
            if (it.IsOverride) Act(tryRow, T("mbUndoItem", "Undo all changes"), () => Confirm(T("mbUndoItem", "Undo all changes"),
                string.Format(T("mbUndoItemText", "Put {0} back as the game has it?"), GameNames.Item(it.Original)), () =>
                {
                    int at = Project.Items.IndexOf(it);
                    Project.Items[at] = currentItem = new ProjectItem(new Newtonsoft.Json.Linq.JObject { ["override"] = it.OverrideIndex });
                    ItemEdited();
                    RebuildItems();
                    BuildItemPage();
                }), "mbv-danger");
            Note(itemPage, original.TypeId <= 3
                ? T("mbFitNote", "Mounts it on the ship being built; then press Test fly.")
                : T("mbCommodityNote", "Commodities are bought and sold in shops; they can't be mounted."));
        }

        void ItemEdited()
        {
            itemsChanged = true;
            MarkDirty();
        }

        /// <summary>A remake-only stat (ItemStats 100+) a kind of item takes although no game item has it: its attribute, name,
        /// whether it is an on / off switch, the value the game uses without it, its range and a note.</summary>
        struct ExtraStat
        {
            public int attr, def, min, max;
            public string name, note;
            public bool flag;
        }

        /// <summary>The remake-only stats this item's kind takes (Flight.Gun.FiresPlainShots: missiles and salvos; turrets:
        /// their range, automatic and turn speed; drills: the mining beam; cloaks: the phase cloak; tractor beams: several
        /// containers at once).</summary>
        List<ExtraStat> ExtraStats(ItemData item)
        {
            var list = new List<ExtraStat>();
            void Add(int attr, int def, int min, int max, string note = null, bool flag = false)
            {
                var names = StatNames();
                if (names.TryGetValue(attr, out var name)) list.Add(new ExtraStat { attr = attr, name = name, def = def, min = min, max = max, note = note, flag = flag });
            }
            if (item.TypeId <= 2 && Flight.Gun.FiresPlainShots(item))
            {
                Add(105, 0, 0, 1, T("mbStatNoteMissiles", "Its shots are homing missiles: a turret's home on its aim, a gun's on the lock."), true);
                Add(106, 1, 1, 12, T("mbStatNoteSalvo", "Shots fired side by side at each trigger pull."));
                Add(107, 1, 1, 20, T("mbStatNoteSalvoCount", "Salvos per magazine, a salvo gap apart; then the reload."));
                Add(108, 0, 0, 60000);
            }
            if (item.categoryId == 8)
            {
                Add(16, 0, 0, 1, T("mbStatNoteAuto", "An automatic turret fires at hostiles by itself; else the player aims it."), true);
                Add(17, 100, 1, 1000, T("mbStatNoteTurn", "How fast it turns (100 = the game's standard turrets)."));
                Add(109, (int)Flight.PlayerTurret.AutoRangeUnits, 1000, 1000000, T("mbStatNoteRange", "How far an automatic turret looks for targets (20 = 1 m)."));
            }
            if (item.categoryId == 19)
            {
                Add(100, 0, 0, 1, T("mbStatNoteBeam", "A mining beam: cuts a locked asteroid while fire is held, no mining game."), true);
                Add(101, (int)Flight.Mining.BeamDefaultRangeUnits, 1000, 200000);
                Add(102, (int)Flight.MiningGame.LayerMs, 500, 60000);
                Add(103, 228, 0, 300, T("mbStatNoteBeamLook", "The beam laser item whose beam it draws."));
            }
            if (item.categoryId == 13)
            {
                Add(Flight.CombatRadar.TractorTargetsAttr, 1, 1, Flight.CombatRadar.MaxTractorTargets,
                    T("mbStatNoteTractorTargets", "2 or more: pulls that many containers at once by itself, without the lock."));
                Add(Flight.CombatRadar.TractorRangeAttr, (int)Flight.CombatRadar.DefaultTractorRangeUnits, 1000, 200000,
                    T("mbStatNoteTractorRange", "How far the extra beams reach for containers (20 = 1 m)."));
            }
            if (item.categoryId == 21)
                Add(104, 0, 0, 1, T("mbStatNotePhase", "While cloaked the ship passes through everything and nothing hits it."), true);
            return list;
        }

        /// <summary>The remake-only stats of this kind its base doesn't have (those it has are in the list above).</summary>
        void BuildExtraStats(ProjectItem it, ItemData original)
        {
            var extras = ExtraStats(original).Where(e => !original.HasAttr(e.attr)).ToList();
            if (extras.Count == 0) return;
            Heading(itemPage, T("mbItemExtras", "Remake stats"));
            foreach (var e in extras)
            {
                var stat = e;
                int value = it.Stat(stat.attr) ?? stat.def;
                if (stat.flag)
                {
                    var row = Add(itemPage, new VisualElement(), "mbv-row");
                    var b = Act(row, $"{StatLabel(stat.name)}: {(value == 1 ? T("mbOn", "on") : T("mbOff", "off"))}", () =>
                    {
                        it.SetStat(stat.attr, stat.name, value == 1 ? (int?)null : 1);
                        ItemEdited();
                        BuildItemPage();
                    });
                    b.EnableInClassList("mbv-toggle--on", value == 1);
                }
                else
                    Int(itemPage, StatLabel(stat.name), value, stat.min, stat.max, v =>
                    {
                        it.SetStat(stat.attr, stat.name, v == stat.def ? (int?)null : v);
                        ItemEdited();
                    });
                if (stat.note != null) Note(itemPage, stat.note);
            }
        }

        /// <summary>A turret's own model (items.json fx "turret", ModWeapons.BuildTurret): a .glb, its size, where the muzzle
        /// flash sits and where the shots start along the barrel. Shown on the turntable once fitted to the test ship.</summary>
        void BuildTurretModel(ProjectItem it)
        {
            Heading(itemPage, T("mbTurretModel", "Turret model"));
            var row = Add(itemPage, new VisualElement(), "mbv-row");
            string model = it.TurretModel;
            int? fromItem = it.TurretItem;
            string modelText = fromItem != null ? string.Format(T("mbModelOfItem", "{0}'s"), GameNames.Item(fromItem.Value))
                             : model == null ? T("mbBaseModel", "the base item's")
                             : GameModels.Is(model, out string gameName) ? gameName : System.IO.Path.GetFileName(model);
            Add(row, new Label($"{T("mbModel", "Model")}: {modelText}"), "mbv-inline-label");
            // A game turret's model, no file needed (fx "turret" "item": ModWeapons copies its ship-mounted model).
            Act(row, T("mbGameTurret", "Game turret…"), () => ShowPicker(T("mbPickGameTurret", "Use a game turret's model"),
                GameItemChoices(index =>
                {
                    string old = it.TurretModel;
                    it.TurretItem = index;
                    if (old != null && !GameModels.Is(old)) Project.DeleteFile(old, Project.Items.Any(o => o != it && o.TurretModel == old));
                    TurretEdited();
                }, index => Database.Shared.Item(index) is ItemData d && d.categoryId != 8 && d.categoryId != 35)));
            if (ModImport.Available)
                Act(row, T("mbOwnGlb", "Your .glb…"), () => PickGlb(T("mbPickTurret", "Choose a turret model"), path =>
                    {
                        string old = it.TurretModel;
                        it.TurretModel = Project.AddFile(path, "models", (it.IsOverride ? "item_" + it.OverrideIndex : it.LocalId) + "_turret", ".glb");
                        if (old != null && !GameModels.Is(old)) Project.DeleteFile(old, Project.Items.Any(o => o != it && o.TurretModel == old));
                        TurretEdited();
                        Status(T("mbLoadingModel", "Loading the model…"), false);
                    }));
            if (model != null || fromItem != null)
                Act(row, "×", () =>
                {
                    it.TurretModel = null;
                    it.TurretItem = null;
                    if (model != null && !GameModels.Is(model)) Project.DeleteFile(model, Project.Items.Any(o => o != it && o.TurretModel == model));
                    TurretEdited();
                }, "mbv-step");
            // Size and place work for the base item's model too (ModWeapons copies it).
            Int(itemPage, T("mbTurretSize", "Size (%)"), Mathf.RoundToInt((it.TurretScale ?? 1f) * 100f), 1, 100000, v =>
            {
                it.TurretScale = v == 100 ? (float?)null : v / 100f;
                TurretEdited();
            });
            var off = it.TurretOffset;
            var xyz = Add(itemPage, new VisualElement(), "mbv-row", "mbv-xyz");
            Int(xyz, "X", off.x, -100000, 100000, v => { var o = it.TurretOffset; o.x = v; it.TurretOffset = o; TurretEdited(); });
            Int(xyz, "Y", off.y, -100000, 100000, v => { var o = it.TurretOffset; o.y = v; it.TurretOffset = o; TurretEdited(); });
            Int(xyz, "Z", off.z, -100000, 100000, v => { var o = it.TurretOffset; o.z = v; it.TurretOffset = o; TurretEdited(); });
            Note(itemPage, T("mbTurretOffsetNote", "Moves the model off its mount: X left, Y up, Z forward (20 = 1 m)."));
            if (model != null && !GameModels.Is(model))
            {
                Int(itemPage, T("mbTurretMuzzle", "Muzzle flash, along the barrel"), Mathf.RoundToInt(it.TurretMuzzle ?? 200f), 0, 100000, v =>
                {
                    it.TurretMuzzle = v;
                    TurretEdited();
                });
                Int(itemPage, T("mbTurretShotStart", "Shots start, along the barrel"), Mathf.RoundToInt(it.TurretShotStart ?? 300f), 0, 100000, v =>
                {
                    it.TurretShotStart = v;
                    TurretEdited();
                });
            }
            Note(itemPage, T("mbTurretGameNote", "Any of the game's turret models, with its own muzzle; no file needed."));
            Note(itemPage, T("mbTurretNote", "Your .glb: 1 unit = 1 m, origin on the mount, barrel along +Z. A \"pivot\" node turns it, a *_gun node under it tilts."));
        }

        static readonly string[] PartLabels = { "Projectile", "Muzzle flash", "Impact" };

        /// <summary>A weapon's look and sound from the game's other weapons, no files needed (items.json fx parts' "item" /
        /// "scale", "shot" / "explosionSound" as an item's number; ModWeapons): what flies, the flash at the gun, the hit, and
        /// the shot and blast sounds, each the base item's unless picked. Parts a hand edit gave a sprite or model are left.</summary>
        void BuildLook(ProjectItem it, ItemData original)
        {
            Heading(itemPage, T("mbLook", "Look and sound"));
            // The weapons whose parts and sounds can be borrowed.
            List<(string, Texture2D, Action)> Weapons(Action<int> pick) =>
                GameItemChoices(pick, index => !(Database.Shared.Item(index) is ItemData d) || d.TypeId > 2);
            for (int k = 0; k < ProjectItem.FxParts.Length; k++)
            {
                string part = ProjectItem.FxParts[k], label = T("mbPart_" + part, PartLabels[k]);
                var row = Add(itemPage, new VisualElement(), "mbv-row");
                if (it.FxPartIsCustom(part))
                {
                    Add(row, new Label($"{label}: {T("mbCustomInFile", "your own (items.json)")}"), "mbv-inline-label");
                    continue;
                }
                int? from = it.FxPartItem(part);
                Add(row, new Label($"{label}: {(from != null ? string.Format(T("mbModelOfItem", "{0}'s"), GameNames.Item(from.Value)) : T("mbBaseItems", "the base item's"))}"), "mbv-inline-label");
                Act(row, T("mbGameItemButton", "Game item…"), () => ShowPicker(string.Format(T("mbPickPartFrom", "{0} from"), label), Weapons(index =>
                {
                    it.SetFxPartItem(part, index);
                    LookEdited();
                })));
                if (from != null) Act(row, "×", () => { it.SetFxPartItem(part, null); LookEdited(); }, "mbv-step");
                Int(itemPage, string.Format(T("mbPartSize", "{0} size (%)"), label), Mathf.RoundToInt((it.FxPartScale(part) ?? 1f) * 100f), 1, 10000, v =>
                {
                    it.SetFxPartScale(part, v / 100f);
                    LookEdited(false);
                });
            }
            foreach (var (key, english) in new[] { ("shot", "Shot sound"), ("explosionSound", "Blast sound") })
            {
                string k = key, label = T("mbSound_" + key, english);
                var row = Add(itemPage, new VisualElement(), "mbv-row");
                if (it.SoundIsCustom(k))
                {
                    Add(row, new Label($"{label}: {T("mbCustomInFile", "your own (items.json)")}"), "mbv-inline-label");
                    continue;
                }
                int? from = it.SoundItem(k);
                Add(row, new Label($"{label}: {(from != null ? string.Format(T("mbModelOfItem", "{0}'s"), GameNames.Item(from.Value)) : T("mbBaseItems", "the base item's"))}"), "mbv-inline-label");
                Act(row, T("mbGameItemButton", "Game item…"), () => ShowPicker(string.Format(T("mbPickPartFrom", "{0} from"), label), Weapons(index =>
                {
                    it.SetSoundItem(k, index);
                    LookEdited();
                })));
                if (from != null) Act(row, "×", () => { it.SetSoundItem(k, null); LookEdited(); }, "mbv-step");
            }
            Note(itemPage, T("mbLookNote", "Borrow any game weapon's projectile, muzzle flash, impact or sounds; no files needed."));
        }

        void LookEdited(bool rebuild = true)
        {
            ItemEdited();
            if (rebuild) BuildItemPage();
        }

        /// <summary>A turret model change: the game reads the project again, so the turntable shows it.</summary>
        void TurretEdited()
        {
            itemsChanged = false;
            RequestReload(true);
            BuildItemPage();
        }

        /// <summary>The first readable stat name of every attribute (ItemStats).</summary>
        static Dictionary<int, string> StatNames()
        {
            var d = new Dictionary<int, string>();
            foreach (var kv in ItemStats.Readable()) if (!d.ContainsKey(kv.Value)) d[kv.Value] = kv.Key;
            return d;
        }

        /// <summary>"reloadMs" -> "Reload (ms)", "shieldCapacity" -> "Shield capacity".</summary>
        static string StatLabel(string name)
        {
            string key = "mbStat_" + name;
            var words = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsUpper(c) && words.Length > 0) words.Append(' ');
                words.Append(words.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            }
            string label = words.ToString();
            if (label.EndsWith(" ms")) label = label.Substring(0, label.Length - 3) + " (ms)";
            return T(key, label);
        }

        /// <summary>The game items a new item can be based on / changed: every original one that can be bought.</summary>
        List<(string, Texture2D, Action)> GameItemChoices(Action<int> pick, Func<int, bool> skip = null)
        {
            var db = Database.Shared;
            var choices = new List<(string, Texture2D, Action)>();
            foreach (var it in db.Items.Where(i => !i.modded && i.maxPrice > 0 && !Session.Unsaleable.Contains(i.index) && !ModContent.IsMissingItem(i.index))
                                       .OrderBy(i => i.TypeId).ThenBy(i => i.categoryId).ThenBy(i => i.maxPrice))
            {
                int index = it.index;
                if (skip != null && skip(index)) continue;
                choices.Add((GameNames.Item(index), ItemInfo.ItemIcon(index), () => pick(index)));
            }
            return choices;
        }

        void PickItemBase() => ShowPicker(T("mbNewItemTitle", "Base the new item on"), GameItemChoices(index =>
        {
            string name = string.Format(T("mbCustomItem", "Custom {0}"), GameNames.Item(index));
            currentItem = Project.AddItem(index, name);
            AfterItemAdded();
            Status(string.Format(T("mbItemMade", "A copy of {0}: give it a name and its own stats."), GameNames.Item(index)), false);
        }));

        void PickItemOverride()
        {
            var taken = new HashSet<int>(Project.Items.Where(i => i.IsOverride).Select(i => i.OverrideIndex));
            ShowPicker(T("mbChangeItemTitle", "Change a game item"), GameItemChoices(index =>
            {
                currentItem = Project.AddItemOverride(index);
                AfterItemAdded();
            }, taken.Contains));
        }

        void AfterItemAdded()
        {
            SaveNow();
            itemsChanged = false;
            RequestReload(false);   // a new item gets its number (the loadout and Fit use it)
            RebuildItems();
            BuildItemPage();
        }

        void AskRemoveItem()
        {
            var it = currentItem;
            if (it == null) return;
            Confirm(T("mbRemoveItem", "Remove item"), string.Format(T("mbRemoveItemText", "Remove \"{0}\" from the project?"), ItemTitle(it)), () =>
            {
                Project.Items.Remove(it);
                currentItem = Project.Items.FirstOrDefault();
                SaveNow();
                itemsChanged = false;
                RequestReload(false);
                RebuildItems();
                BuildItemPage();
            });
        }

        /// <summary>Mounts the item on the test ship: a weapon in every slot of its kind, equipment in place of one of its
        /// category (else a free slot, else the last). A new item waits for the game to read the project first.</summary>
        void FitItem(ProjectItem it)
        {
            if (Current == null) { Status(T("mbNoShipToFit", "Add a ship in the Ships tab first: the item is fitted to it."), true); return; }
            int index = ItemIndex(it);
            if (index < 0 || itemsChanged || reloadPending || reloading)
            {
                itemsChanged = false;
                afterReload.Add(() => FitItem(it));
                if (!reloadPending && !reloading) RequestReload(false);
                Status(T("mbUpdating", "Updating the preview…"), false);
                return;
            }
            var db = Database.Shared;
            var data = db.Item(index);
            if (data == null || data.TypeId > 3) { Status(T("mbCantFit", "That item can't be mounted."), true); return; }
            int kind = data.TypeId;
            var s = Current;
            if (s.SlotCount(kind) <= 0)
            {
                Status(string.Format(T("mbNoSlotFor", "{0} has no slot for it: give it one in the Ships tab."), ShipName(s, db)), true);
                return;
            }
            var slots = SlotsOf(db, s);
            if (kind < 3)
                for (int i = 0; i < s.SlotCount(kind); i++) SetSlot(kind, i, index);
            else
            {
                int slot = slots[3].FindIndex(i => db.Item(i)?.category == data.category);
                if (slot < 0) slot = slots[3].Count < s.SlotCount(3) ? slots[3].Count : s.SlotCount(3) - 1;
                SetSlot(3, slot, index);
            }
            Status(string.Format(T("mbFitted", "{0} is on {1}. Press Test fly to try it."), ItemTitle(it), ShipName(s, db)), false);
        }
    }
}
