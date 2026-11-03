// ShipCustomizePanel.cs
// Remake mods: the hangar's Customize screen for a customizable ship (Modding.ModShipKits, EVERSPACE 2's ship modules): a
// panel over Inspect ship's orbit view (StationMenu) with one row per slot of the ship's kit and its tier, each cycling
// through what the ship's type may fit, then its skin (the kit's paint jobs for the type, "Standard" = the model's own), its
// weapon colour (the energy weapons' and energy turrets' shots, Flight.WeaponTint; "Original" = their own), its weapon models
// shown or hidden (a kit whose hardpoints have "toggle", ModHardpoints.Toggles: the guns, pylons and turret), its exhaust (the
// mods' exhaust styles, Modding.ModExhausts, on any ship; "Ship's own" = the model's) with the style's two colours, then the kit's
// colours: a preset (sets every colour) and a row per colour slot with a swatch. Up / down (W / S, the D-pad, the left stick) pick a row, left / right (A / D) cycle
// it, a click on a row's arrows too; Enter / the controller's A applies, Esc / the controller's B cancels. Every change is
// shown on the turntable at once (StationMenu's Changed: the build is the player's while the screen is open, Cancel puts the
// old one back). Plain UI Toolkit, built in code; the styles are inline like the multiplayer window's.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class ShipCustomizePanel
    {
        public readonly VisualElement Root;
        public bool IsOpen { get; private set; }

        /// <summary>The build changed (shown at once); Apply / Cancel pressed.</summary>
        public event Action<ModShipKits.Build> Changed;
        public event Action Applied, Cancelled;
        /// <summary>A button sound (StationMenu's push / release).</summary>
        public Action ClickSound;
        /// <summary>The ship as a build makes it, one line (StationMenu: hull, cargo, handling, slots, what goes to the hold).</summary>
        public Func<ModShipKits.Build, string> StatsText;

        ModShipKits.Kit kit;
        string type;
        int ship = -1;   // the ship it customizes (the exhaust styles offered on it, ModExhausts.For)
        ModShipKits.Build build;
        int row;
        float holdMs;
        enum Kind { Part, Tier, Skin, Livery, Decal, Trail, WeaponColor, WeaponModels, Exhaust, ExhaustSize, ExhaustColor, Preset, Color, Strength }
        readonly List<(Kind kind, string key, Label value, VisualElement line, VisualElement swatch)> rows = new List<(Kind, string, Label, VisualElement, VisualElement)>();
        int presetIndex = -1;
        readonly ScrollView list;
        /// <summary>The panel's top (panel px) and the gap kept under it: the rows scroll when the panel would leave the screen.</summary>
        const float PanelTop = 96f, PanelBottomGap = 24f;
        readonly Label title, help, stats;

        static readonly Color Amber = new Color(1f, 0.7f, 0.2f), Line = new Color(1f, 1f, 1f, 0.12f), Panel = new Color(0.03f, 0.05f, 0.08f, 0.82f);
        static string X(string key, string english) => Localization.Extra(key, english);

        public ShipCustomizePanel()
        {
            Root = new VisualElement { name = "shipCustomize", pickingMode = PickingMode.Position };
            Root.AddToClassList(SafeArea.EdgeClass);   // clear of the notch / Dynamic Island (SafeArea.KeepClear)
            var s = Root.style;
            s.position = Position.Absolute; s.left = 48; s.top = PanelTop; s.width = 620;
            s.flexDirection = FlexDirection.Column;
            s.backgroundColor = Panel;
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = new Color(1f, 0.7f, 0.2f, 0.5f);
            s.paddingTop = s.paddingBottom = 10; s.paddingLeft = s.paddingRight = 18;
            s.display = DisplayStyle.None;
            title = new Label();
            title.AddToClassList("gof-semibold");
            title.style.fontSize = UiLayout.Type(30); title.style.color = Amber; title.style.marginBottom = 10;
            Root.Add(title);
            // The rows scroll (wheel, drag, the selection kept in view) so the stats, buttons and help stay on screen: a kit
            // with every colour slot had 19 rows and pushed them off the bottom.
            list = new ScrollView(ScrollViewMode.Vertical);
            list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.style.flexShrink = 1;
            list.style.flexGrow = 0;
            new DragScroll(list);
            Root.Add(list);
            Root.RegisterCallback<AttachToPanelEvent>(_ => FitToScreen());
            Root.RegisterCallback<GeometryChangedEvent>(_ => FitToScreen());
            list.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => FitToScreen());
            stats = new Label();
            stats.style.fontSize = UiLayout.Type(19); stats.style.color = new Color(0.85f, 0.95f, 1f, 0.95f); stats.style.marginTop = 10;
            stats.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(stats);
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row; buttons.style.marginTop = 14;
            buttons.Add(Button(X("kitApply", "APPLY"), () => { ClickSound?.Invoke(); Applied?.Invoke(); }, true));
            buttons.Add(Button(X("kitCancel", "CANCEL"), () => { ClickSound?.Invoke(); Cancelled?.Invoke(); }, false));
            Root.Add(buttons);
            help = new Label();
            help.style.fontSize = UiLayout.Type(18); help.style.color = new Color(1f, 1f, 1f, 0.6f); help.style.marginTop = 10; help.style.whiteSpace = WhiteSpace.Normal;
            Root.Add(help);
        }

        /// <summary>The rows' list as tall as its rows, but no taller than the screen leaves under the panel's top once the
        /// title, stats, buttons and help have their room (a ScrollView takes no height from its content by itself).</summary>
        void FitToScreen()
        {
            var parent = Root.parent;
            if (parent == null) return;
            float screen = parent.resolvedStyle.height, panel = Root.layout.height, current = list.layout.height,
                  content = list.contentContainer.layout.height;
            if (float.IsNaN(screen) || screen <= 0f || float.IsNaN(panel) || float.IsNaN(content)) return;
            float others = panel - (float.IsNaN(current) ? 0f : current);
            float room = Mathf.Max(120f, screen - PanelTop - PanelBottomGap - others);
            float want = Mathf.Round(Mathf.Min(content, room));
            if (want > 0f && Mathf.Abs(list.resolvedStyle.height - want) > 0.5f) list.style.height = want;
        }

        static Button Button(string text, Action click, bool primary)
        {
            var b = new Button(click) { text = text, focusable = false };
            b.AddToClassList("station-button");
            b.AddToClassList("gof-semibold");
            b.style.flexGrow = 1; b.style.marginRight = primary ? 10 : 0;
            if (primary) b.AddToClassList("choice-button--start");
            return b;
        }

        /// <summary>Opens the panel on a kit ship's current build.</summary>
        public void Show(string shipName, ModShipKits.Kit kit, string type, ModShipKits.Build current, int ship = -1)
        {
            this.kit = kit;
            this.ship = ship;
            this.type = type;
            build = current.Clone();
            title.text = string.Format(X("kitCustomizeTitle", "CUSTOMIZE {0}"), shipName.UiCaps());
            help.text = X("kitCustomizeHelp", "Up / down: part. Left / right: change. Enter: apply. Esc: cancel.");
            row = 0;
            BuildRows();
            Refresh();
            Root.style.display = DisplayStyle.Flex;
            IsOpen = true;
        }

        /// <summary>The rows for the kit and the build (the exhaust's colour rows only with an exhaust style picked).</summary>
        void BuildRows()
        {
            rows.Clear();
            list.Clear();
            foreach (string slot in kit.slots)
            {
                if (kit.PartsFor(slot, type).Count <= 1) continue;   // nothing to choose (a fixed base, interior)
                AddRow(Kind.Part, slot, SlotName(slot));
            }
            if (kit.MaxTier > 0) AddRow(Kind.Tier, null, X("kitTier", "Tier"));
            if (kit.SkinsFor(type).Count > 0) AddRow(Kind.Skin, null, X("kitSkin", "Skin"));
            if (kit.LiveriesFor(type).Count > 0) AddRow(Kind.Livery, null, X("kitLivery", "Livery"));
            if (kit.DecalsFor(type).Count > 0) AddRow(Kind.Decal, null, X("kitDecal", "Decal"));
            if (kit.trails.Count > 0)
            {
                AddRow(Kind.Trail, "engine", X("kitEngineTrail", "Engine trail"));
                AddRow(Kind.Trail, "boost", X("kitBoosterTrail", "Booster trail"));
            }
            if (kit.weaponColorRow) AddRow(Kind.WeaponColor, null, X("kitWeaponColor", "Weapon colour"));
            if (ModHardpoints.Toggles(ship)) AddRow(Kind.WeaponModels, null, X("kitWeaponModels", "Weapon models"));
            if (ModExhausts.For(ship).Count > 0)
            {
                AddRow(Kind.Exhaust, null, X("kitExhaust", "Exhaust"));
                if (ModExhausts.Find(build.exhaust, ship) is ModExhausts.Style style)
                {
                    AddRow(Kind.ExhaustSize, null, X("kitExhaustSize", "Exhaust size"));
                    AddRow(Kind.ExhaustColor, "1", style.MainName);
                    AddRow(Kind.ExhaustColor, "2", style.SecondName);
                }
            }
            presetIndex = MatchingPreset();
            if (kit.presets.Count > 0) AddRow(Kind.Preset, null, X("kitPreset", "Colour scheme"));
            foreach (var cs in kit.colorSlots) AddRow(cs.IsStrength ? Kind.Strength : Kind.Color, cs.id, cs.Name);
            row = Mathf.Clamp(row, 0, Math.Max(0, rows.Count - 1));
        }

        /// <summary>The selected row is a boost colour (the exhaust style's second colour of a flame style, or a kit colour
        /// slot recolouring the boost) or the booster trail: StationMenu shows the engines boosting meanwhile.</summary>
        public bool BoostRowSelected
        {
            get
            {
                if (!IsOpen || row < 0 || row >= rows.Count) return false;
                var (kind, slot, _, _, _) = rows[row];
                if (kind == Kind.ExhaustColor) return slot == "2" && ModExhausts.Find(build.exhaust, ship) is ModExhausts.Style s && s.IsFlame;
                if (kind == Kind.Trail) return slot == "boost";   // the booster trail shows while boosting
                return kind == Kind.Color && kit.colorSlots.Find(c => c.id == slot) is ModShipKits.ColorSlot cs && cs.targets.Exists(t => t.property == "boost");
            }
        }

        public void Hide()
        {
            Root.style.display = DisplayStyle.None;
            IsOpen = false;
        }

        static string SlotName(string slot)
        {
            string english = slot.Length == 0 ? slot : char.ToUpperInvariant(slot[0]) + slot.Substring(1).Replace('_', ' ');
            return X("kitSlot_" + slot.ToLowerInvariant(), english);
        }

        void AddRow(Kind kind, string slot, string label)
        {
            int index = rows.Count;
            var line = new VisualElement { pickingMode = PickingMode.Position };
            var ls = line.style;
            ls.flexDirection = FlexDirection.Row; ls.alignItems = Align.Center; ls.height = 38;
            ls.borderBottomWidth = 1; ls.borderBottomColor = Line;
            ls.paddingLeft = ls.paddingRight = 6;
            var name = new Label(label.UiCaps());
            name.AddToClassList("gof-semibold");
            name.style.width = 210; name.style.fontSize = UiLayout.Type(20); name.style.color = new Color(1f, 1f, 1f, 0.75f);
            NoWrap(name);
            line.Add(name);
            line.Add(Arrow("‹", () => { row = index; Cycle(-1); }));
            var value = new Label();
            value.AddToClassList("gof-semibold");
            value.style.flexGrow = 1; value.style.flexShrink = 1; value.style.fontSize = UiLayout.Type(21); value.style.unityTextAlign = TextAnchor.MiddleCenter; value.style.color = Color.white;
            NoWrap(value);
            VisualElement swatch = null;
            if (kind == Kind.Color || kind == Kind.WeaponColor || kind == Kind.ExhaustColor)
            {
                swatch = new VisualElement { pickingMode = PickingMode.Ignore };
                var ws = swatch.style;
                ws.width = 22; ws.height = 22; ws.marginLeft = 4; ws.marginRight = 4;
                ws.borderTopWidth = ws.borderBottomWidth = ws.borderLeftWidth = ws.borderRightWidth = 1;
                ws.borderTopColor = ws.borderBottomColor = ws.borderLeftColor = ws.borderRightColor = new Color(1f, 1f, 1f, 0.5f);
                line.Add(swatch);
            }
            line.Add(value);
            line.Add(Arrow("›", () => { row = index; Cycle(1); }));
            line.RegisterCallback<PointerDownEvent>(_ => { row = index; Refresh(); });
            list.Add(line);
            rows.Add((kind, slot, value, line, swatch));
        }

        static void NoWrap(Label l)
        {
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
        }

        Button Arrow(string text, Action click)
        {
            var b = new Button(click) { text = text, focusable = false };
            var s = b.style;
            s.width = 44; s.height = 40; s.fontSize = UiLayout.Type(30); s.color = Amber;
            s.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 0;
            s.unityTextAlign = TextAnchor.MiddleCenter;
            return b;
        }

        /// <summary>The selected row's part (or tier) one step on, wrapping.</summary>
        public void Cycle(int dir)
        {
            if (rows.Count == 0) return;
            var (kind, slot, _, _, _) = rows[row];
            switch (kind)
            {
                case Kind.Tier:
                    build.tier = (build.tier + dir + kit.MaxTier + 1) % (kit.MaxTier + 1);
                    break;
                case Kind.Skin:
                {
                    // The model's own look (index 0), then the type's skins.
                    var skins = kit.SkinsFor(type);
                    int n = skins.Count + 1;
                    int at = skins.FindIndex(k => string.Equals(k.id, build.skin, StringComparison.OrdinalIgnoreCase)) + 1;
                    at = (at + dir + n) % n;
                    build.skin = at == 0 ? ModShipKits.Build.NoSkin : skins[at - 1].id;
                    // A skin with colours and a livery (EVERSPACE 2's skins) brings them along; the rows can change them after.
                    if (at > 0)
                    {
                        foreach (var kv in skins[at - 1].colors) build.colors[kv.Key] = kv.Value;
                        if (skins[at - 1].livery != null) build.livery = skins[at - 1].livery;
                        if (skins[at - 1].decal != null) build.decal = skins[at - 1].decal;
                        presetIndex = MatchingPreset();
                    }
                    break;
                }
                case Kind.Livery:
                {
                    // No livery (index 0), then the type's liveries.
                    var liveries = kit.LiveriesFor(type);
                    int n = liveries.Count + 1;
                    int at = liveries.IndexOf(kit.LiveryOf(build)) + 1;
                    at = (at + dir + n) % n;
                    build.livery = at == 0 ? ModShipKits.Build.NoSkin : liveries[at - 1].id;
                    break;
                }
                case Kind.Decal:
                {
                    // No decal (index 0), then the type's decals.
                    var decals = kit.DecalsFor(type);
                    int n = decals.Count + 1;
                    int at = decals.IndexOf(kit.DecalOf(build)) + 1;
                    at = (at + dir + n) % n;
                    build.decal = at == 0 ? ModShipKits.Build.NoSkin : decals[at - 1].id;
                    break;
                }
                case Kind.Trail:
                {
                    // EVERSPACE 2's trails in the kit's order (the Default veil first when the kit doesn't list it).
                    var choices = TrailChoices();
                    int n = choices.Count;
                    int at = (TrailIndex(choices, slot == "boost" ? build.boostTrail : build.trail) + dir + n) % n;
                    string id = choices[at]?.id;
                    if (slot == "boost") build.boostTrail = id; else build.trail = id;
                    break;
                }
                case Kind.WeaponColor:
                {
                    // The weapons' own look (index 0), then the palette.
                    var colors = kit.WeaponColors;
                    int n = colors.Count + 1;
                    int at = colors.FindIndex(c => string.Equals(c.id, build.weaponColor, StringComparison.OrdinalIgnoreCase)) + 1;
                    at = (at + dir + n) % n;
                    build.weaponColor = at == 0 ? ModShipKits.Build.NoSkin : colors[at - 1].id;
                    break;
                }
                case Kind.WeaponModels:
                    build.weapons = build.WeaponsHidden ? ModShipKits.Build.WeaponsOn : ModShipKits.Build.WeaponsOff;
                    break;
                case Kind.Exhaust:
                {
                    // The ship's own exhaust (index 0), then the mods' styles; the colour rows come and go with it.
                    var styles = ModExhausts.For(ship);
                    int n = styles.Count + 1;
                    int at = styles.FindIndex(x => string.Equals(x.Key, build.exhaust, StringComparison.OrdinalIgnoreCase)) + 1;
                    at = (at + dir + n) % n;
                    build.exhaust = at == 0 ? ModShipKits.Build.NoSkin : styles[at - 1].Key;
                    var style = ModExhausts.Find(build.exhaust, ship);
                    if (!ModExhausts.ResolveColor(style, build.exhaustColor, out _)) build.exhaustColor = null;
                    if (!ModExhausts.ResolveColor(style, build.exhaustColor2, out _)) build.exhaustColor2 = null;
                    BuildRows();
                    break;
                }
                case Kind.ExhaustSize:
                {
                    // x the fitted size, a step at a time (no wrapping: it's a scale).
                    float size = ModExhausts.ParseSize(build.exhaustSize) + dir * ModExhausts.SizeStep;
                    size = Mathf.Clamp(size, ModExhausts.MinSize, ModExhausts.MaxSize);
                    string text = ModExhausts.SizeText(ModExhausts.ParseSize(size.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    if (text == build.exhaustSize) return;
                    build.exhaustSize = text;
                    break;
                }
                case Kind.ExhaustColor:
                {
                    // The style's own colour (index 0), then its palette.
                    var style = ModExhausts.Find(build.exhaust, ship);
                    if (style == null) return;
                    var colors = style.Colors;
                    string cur = slot == "1" ? build.exhaustColor : build.exhaustColor2;
                    int n = colors.Count + 1;
                    int at = colors.FindIndex(c => string.Equals(c.id, cur, StringComparison.OrdinalIgnoreCase)) + 1;
                    at = (at + dir + n) % n;
                    string pick = at == 0 ? null : colors[at - 1].id;
                    if (slot == "1") build.exhaustColor = pick; else build.exhaustColor2 = pick;
                    break;
                }
                case Kind.Part:
                {
                    var parts = kit.PartsFor(slot, type);
                    if (parts.Count <= 1) return;
                    int at = Math.Max(0, parts.FindIndex(p => build.parts.TryGetValue(slot, out var id) && string.Equals(p.id, id, StringComparison.OrdinalIgnoreCase)));
                    build.parts[slot] = parts[(at + dir + parts.Count) % parts.Count].id;
                    break;
                }
                case Kind.Preset:
                {
                    int n = kit.presets.Count;
                    presetIndex = presetIndex < 0 ? (dir > 0 ? 0 : n - 1) : (presetIndex + dir + n) % n;
                    foreach (var kv in kit.presets[presetIndex].colors) build.colors[kv.Key] = kv.Value;
                    break;
                }
                case Kind.Strength:
                {
                    var cs = kit.colorSlots.Find(c => c.id == slot);
                    float cur = ModShipKits.Resolve(kit, cs, build.colors.TryGetValue(slot, out var sv) ? sv : cs.defaultValue, out var sc, out _, out _) ? sc.r : cs.steps[0];
                    int at = 0;
                    for (int i = 1; i < cs.steps.Length; i++) if (Mathf.Abs(cs.steps[i] - cur) < Mathf.Abs(cs.steps[at] - cur)) at = i;
                    at = Mathf.Clamp(at + dir, 0, cs.steps.Length - 1);
                    build.colors[slot] = cs.steps[at].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    presetIndex = MatchingPreset();
                    break;
                }
                case Kind.Color:
                {
                    var cs = kit.colorSlots.Find(c => c.id == slot);
                    var palette = kit.PaletteOf(cs);
                    if (palette.Count == 0) return;
                    int at = build.colors.TryGetValue(slot, out var v) ? palette.FindIndex(c => string.Equals(c.id, v, StringComparison.OrdinalIgnoreCase)) : -1;
                    at = at < 0 ? (dir > 0 ? 0 : palette.Count - 1) : (at + dir + palette.Count) % palette.Count;
                    build.colors[slot] = palette[at].id;
                    presetIndex = MatchingPreset();
                    break;
                }
            }
            ClickSound?.Invoke();
            Refresh();
            Changed?.Invoke(build.Clone());
        }

        /// <summary>The engine trails to pick from: the kit's, with a null (the Default veil) first unless the kit lists one.</summary>
        List<ModShipKits.EngineTrail> TrailChoices()
        {
            var l = new List<ModShipKits.EngineTrail>(kit.trails);
            if (!l.Exists(t => t.IsDefault)) l.Insert(0, null);
            return l;
        }

        /// <summary>A trail id's place among the choices (none or unknown: the Default's).</summary>
        static int TrailIndex(List<ModShipKits.EngineTrail> choices, string id)
        {
            int at = string.IsNullOrEmpty(id) ? -1 : choices.FindIndex(t => t != null && string.Equals(t.id, id, StringComparison.OrdinalIgnoreCase));
            if (at >= 0) return at;
            at = choices.FindIndex(t => t == null || t.IsDefault);
            return Math.Max(0, at);
        }

        /// <summary>The preset whose colours the build has (-1: none, the colours were picked one by one).</summary>
        int MatchingPreset() => kit.presets.FindIndex(p => p.colors.Count > 0 && p.colors.All(kv =>
            build.colors.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase)));

        void Refresh()
        {
            stats.text = StatsText != null ? StatsText(build) : "";
            stats.style.display = stats.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < rows.Count; i++)
            {
                var (kind, slot, value, line, swatch) = rows[i];
                switch (kind)
                {
                    case Kind.Tier: value.text = build.tier == 0 ? X("kitTierNone", "Base") : build.tier.ToString(); break;
                    case Kind.Skin:
                    {
                        var skins = kit.SkinsFor(type);
                        var skin = kit.FindSkin(build.skin);
                        int at = skin == null ? 0 : skins.IndexOf(skin) + 1;
                        value.text = (skin?.Name ?? X("kitSkinStandard", "Standard")) + $"  ({at + 1}/{skins.Count + 1})";
                        break;
                    }
                    case Kind.Livery:
                    {
                        var liveries = kit.LiveriesFor(type);
                        var lv = kit.LiveryOf(build);
                        int at = lv == null ? 0 : liveries.IndexOf(lv) + 1;
                        value.text = (lv?.Name ?? X("kitLiveryNone", "None")) + $"  ({at + 1}/{liveries.Count + 1})";
                        break;
                    }
                    case Kind.Decal:
                    {
                        var decals = kit.DecalsFor(type);
                        var dc = kit.DecalOf(build);
                        int at = dc == null ? 0 : decals.IndexOf(dc) + 1;
                        value.text = (dc?.Name ?? X("kitLiveryNone", "None")) + $"  ({at + 1}/{decals.Count + 1})";
                        break;
                    }
                    case Kind.Trail:
                    {
                        var choices = TrailChoices();
                        int at = TrailIndex(choices, slot == "boost" ? build.boostTrail : build.trail);
                        value.text = (choices[at]?.Name ?? X("kitTrailDefault", "Default")) + $"  ({at + 1}/{choices.Count})";
                        break;
                    }
                    case Kind.Part:
                    {
                        var parts = kit.PartsFor(slot, type);
                        var part = build.parts.TryGetValue(slot, out var id) ? kit.Find(id) : null;
                        int at = part == null ? -1 : parts.IndexOf(part);
                        value.text = (part?.Name ?? "-") + (parts.Count > 1 ? $"  ({at + 1}/{parts.Count})" : "");
                        break;
                    }
                    case Kind.WeaponColor:
                    {
                        var colors = kit.WeaponColors;
                        var choice = colors.Find(c => string.Equals(c.id, build.weaponColor, StringComparison.OrdinalIgnoreCase));
                        int at = choice == null ? 0 : colors.IndexOf(choice) + 1;
                        bool custom = choice == null && ModShipKits.ResolveWeaponColor(kit, build.weaponColor, out _);
                        value.text = (choice != null ? X("weaponColor_" + choice.id, choice.Name) : custom ? X("kitColorScheme", "Scheme colour") : X("kitWeaponColorOriginal", "Original"))
                                   + $"  ({at + 1}/{colors.Count + 1})";
                        if (swatch != null)
                        {
                            bool has = ModShipKits.ResolveWeaponColor(kit, build.weaponColor, out var wc);
                            swatch.style.backgroundColor = has ? wc : Color.clear;
                        }
                        break;
                    }
                    case Kind.WeaponModels:
                        value.text = (build.WeaponsHidden ? X("kitWeaponModelsHidden", "Hidden") : X("kitWeaponModelsShown", "Shown"))
                                   + $"  ({(build.WeaponsHidden ? 2 : 1)}/2)";
                        break;
                    case Kind.Exhaust:
                    {
                        var styles = ModExhausts.For(ship);
                        var style = ModExhausts.Find(build.exhaust, ship);
                        int at = style == null ? 0 : styles.IndexOf(style) + 1;
                        value.text = (style?.Name ?? X("kitExhaustOwn", "Ship's own")) + $"  ({at + 1}/{styles.Count + 1})";
                        break;
                    }
                    case Kind.ExhaustSize:
                        value.text = Mathf.RoundToInt(ModExhausts.ParseSize(build.exhaustSize) * 100f) + " %";
                        break;
                    case Kind.ExhaustColor:
                    {
                        var style = ModExhausts.Find(build.exhaust, ship);
                        string v = slot == "1" ? build.exhaustColor : build.exhaustColor2;
                        var colors = style?.Colors ?? new List<ModShipKits.ColorChoice>();
                        var choice = colors.Find(c => string.Equals(c.id, v, StringComparison.OrdinalIgnoreCase));
                        int at = choice == null ? 0 : colors.IndexOf(choice) + 1;
                        bool custom = choice == null && ModExhausts.ResolveColor(style, v, out _);
                        value.text = (choice != null ? choice.Name : custom ? X("kitColorScheme", "Scheme colour") : X("kitExhaustColorOwn", "Original"))
                                   + $"  ({at + 1}/{colors.Count + 1})";
                        if (swatch != null) swatch.style.backgroundColor = ModExhausts.ResolveColor(style, v, out var ec) ? ec : Color.clear;
                        break;
                    }
                    case Kind.Preset:
                        value.text = presetIndex >= 0 ? kit.presets[presetIndex].Name : X("kitPresetCustom", "Custom");
                        break;
                    case Kind.Strength:
                    {
                        var cs = kit.colorSlots.Find(c => c.id == slot);
                        string v = build.colors.TryGetValue(slot, out var cv) ? cv : cs.defaultValue;
                        value.text = ModShipKits.Resolve(kit, cs, v, out var sc, out _, out _) ? Mathf.RoundToInt(sc.r * 100f) + " %" : "-";
                        break;
                    }
                    case Kind.Color:
                    {
                        var cs = kit.colorSlots.Find(c => c.id == slot);
                        string v = build.colors.TryGetValue(slot, out var cv) ? cv : null;
                        var choice = v == null ? null : kit.PaletteOf(cs).Find(c => string.Equals(c.id, v, StringComparison.OrdinalIgnoreCase));
                        value.text = choice != null ? choice.Name : v != null ? X("kitColorScheme", "Scheme colour") : "-";
                        if (swatch != null && ModShipKits.Resolve(kit, cs, v, out var col, out _, out _)) swatch.style.backgroundColor = col;
                        break;
                    }
                }
                bool sel = i == row;
                line.style.backgroundColor = sel ? new Color(1f, 0.7f, 0.2f, 0.16f) : Color.clear;
                line.style.borderLeftWidth = sel ? 3 : 0;
                line.style.borderLeftColor = Amber;
            }
            // The selected row kept in view (after the layout; the list may have been rebuilt meanwhile).
            if (row >= 0 && row < rows.Count)
            {
                var selected = rows[row].line;
                list.schedule.Execute(() => { if (list.contentContainer.Contains(selected)) list.ScrollTo(selected); });
            }
        }

        /// <summary>The panel's keys and controller (StationMenu.UpdateCustomize, every frame while open). A held direction
        /// repeats after 350 ms, every 120 ms.</summary>
        public void HandleInput(Keyboard kb, Gamepad pad, float dtMs)
        {
            if (!IsOpen) return;
            bool Pressed(Func<Keyboard, bool> k, Func<Gamepad, bool> p) => (kb != null && k(kb)) || (pad != null && p(pad));
            if (Pressed(k => k.escapeKey.wasPressedThisFrame || k.backspaceKey.wasPressedThisFrame, p => p.buttonEast.wasPressedThisFrame))
            { ClickSound?.Invoke(); Cancelled?.Invoke(); return; }
            if (Pressed(MenuConfirm.Pressed, p => p.buttonSouth.wasPressedThisFrame))
            { ClickSound?.Invoke(); Applied?.Invoke(); return; }
            var stick = pad != null ? pad.leftStick.ReadValue() + pad.dpad.ReadValue() : Vector2.zero;
            int v = (kb != null && (kb.upArrowKey.isPressed || kb.wKey.isPressed) ? 1 : 0) - (kb != null && (kb.downArrowKey.isPressed || kb.sKey.isPressed) ? 1 : 0)
                    + (stick.y > 0.6f ? 1 : stick.y < -0.6f ? -1 : 0);
            int h = (kb != null && (kb.rightArrowKey.isPressed || kb.dKey.isPressed) ? 1 : 0) - (kb != null && (kb.leftArrowKey.isPressed || kb.aKey.isPressed) ? 1 : 0)
                    + (stick.x > 0.6f ? 1 : stick.x < -0.6f ? -1 : 0);
            v = Math.Sign(v); h = Math.Sign(h);
            if (v == 0 && h == 0) { holdMs = 0f; return; }
            bool first = holdMs == 0f;
            holdMs += Math.Max(1f, dtMs);
            if (!first && holdMs < 350f) return;
            if (!first) holdMs -= 120f;
            if (v != 0 && rows.Count > 0) { row = (row - v + rows.Count) % rows.Count; ClickSound?.Invoke(); Refresh(); }
            else if (h != 0) Cycle(h);
        }

        /// <summary>A screen point (pixels, y up) over the panel: a press there isn't an orbit drag.</summary>
        public bool Contains(VisualElement root, Vector2 screen)
        {
            if (!IsOpen || root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            return Root.worldBound.Contains(p);
        }
    }
}
