// HangarWindowKaamoVip.cs
// HangarWindow: the Kaamo Club VIP Card's tools (part of the partial HangarWindow; its fields are in HangarWindow.cs).
// Remake (players' suggestion, Discord "Kaamo Club features"; KaamoClub.VipFeatures, see docs/notes/kaamo-club.md):
//   - the card itself is the Store tab's first row while it is for sale (KaamoClub.VipForSale): its perks in the details,
//     BUY asks and pays KaamoClub.VipPrice;
//   - with the card, in the club's storage: a search box and a sort button on the header bar (F / R, the controller's
//     right / left stick press: the search opens the controller's keyboard), and the stored hulls in a Ships tab of their
//     own (key 4) between the Store and Blueprints tabs;
//   - everywhere else: a dealer's ship of a type already parked at the club carries an AT KAAMO badge with the map's house.
// Only the club's storage gets the search and the sorting: the stations' shops stay the original's.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class HangarWindow
    {
        Button tabShips, sortButton;
        VisualElement vipTools, screenRoot;
        TextField searchField;
        string search = "";
        bool searchFocused, hintsForVip;
        /// <summary>How the storage's lists are sorted, kept while the game runs (Sort).</summary>
        static int sortMode;
        /// <summary>The club's order, by name (A-Z), by value (highest first), by race (ships) / type (items).</summary>
        enum Sort { Default, Name, Price, Race }
        const int SortModes = 4;

        /// <summary>The VIP Card's tools are shown: the window open on the owned club's storage with the card's perks on.</summary>
        public bool VipTools => IsOpen && hangar != null && hangar.Storage && KaamoClub.VipFeatures;

        /// <summary>The search box and the sort button are shown: the VIP tools on the Store or Ships tab.</summary>
        public bool SearchTab => VipTools && (tab == Tab.Shop || tab == Tab.Ships) && editing < 0;

        /// <summary>The search box has the keyboard (StationMenu leaves the hangar's keys alone meanwhile: NetChat.Keys).</summary>
        public bool Searching => searchFocused;

        void BuildVipTools(VisualElement root)
        {
            screenRoot = root;
            tabShips = new Button { focusable = false };
            tabShips.AddToClassList("hangar-tab");
            tabShips.AddToClassList("gof-semibold");
            tabShips.style.display = DisplayStyle.None;
            tabShips.clicked += () => { menu.PlayRelease(); SetTab(Tab.Ships); };
            tabShop.parent.Insert(tabShop.parent.IndexOf(tabShop) + 1, tabShips);

            vipTools = new VisualElement();
            vipTools.AddToClassList("hangar-tools");
            vipTools.style.display = DisplayStyle.None;
            searchField = new TextField { maxLength = 40 };
            searchField.AddToClassList("gof-field");
            searchField.AddToClassList("hangar-search");
            searchField.RegisterValueChangedCallback(e =>
            {
                string q = (e.newValue ?? "").Trim();
                if (q == search) return;
                search = q;
                if (IsOpen) Rebuild();
            });
            // The game's own keys rest while it is typed into (NetChat.SetTyping, as the multiplayer window's fields do):
            // W A S D, Q / E, the digits and Enter belong to the box then. Enter or Esc gives the keys back.
            searchField.RegisterCallback<FocusInEvent>(_ => SetSearchFocus(true));
            searchField.RegisterCallback<FocusOutEvent>(e =>
            {
                if (e.relatedTarget is VisualElement to && Inside(to, searchField)) return;
                SetSearchFocus(false);
            });
            searchField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                e.StopPropagation();
                searchField.Blur();
            }, TrickleDown.TrickleDown);
            TextFieldKeys.Guard(searchField);
            PhoneKeyboard.Attach(searchField);
            vipTools.Add(searchField);
            sortButton = new Button { focusable = false };
            sortButton.AddToClassList("hangar-sort");
            sortButton.AddToClassList("gof-semibold");
            sortButton.clicked += CycleSort;
            vipTools.Add(sortButton);
            // A press anywhere else in the window (a row, a tab) ends the typing: the list's rows take no focus of their own.
            window.RegisterCallback<PointerDownEvent>(e =>
            {
                if (searchFocused && !(e.target is VisualElement v && Inside(v, searchField))) searchField.Blur();
            }, TrickleDown.TrickleDown);
            RefreshVipTexts();
        }

        static bool Inside(VisualElement v, VisualElement of)
        {
            for (var p = v; p != null; p = p.parent) if (p == of) return true;
            return false;
        }

        void SetSearchFocus(bool on)
        {
            if (searchFocused == on) return;
            searchFocused = on;
            NetChat.SetTyping(on);
        }

        void RefreshVipTexts()
        {
            if (tabShips == null) return;
            tabShips.text = Localization.Get(173).UiCaps();   // "Ships"
            searchField.textEdition.placeholder = Localization.Extra("shopSearch", "Search");
            string by = (Sort)sortMode switch
            {
                Sort.Name => Localization.Extra("shopSortName", "A-Z"),
                Sort.Price => Localization.Extra("shopSortPrice", "Price"),
                Sort.Race => tab == Tab.Ships ? Localization.Extra("shopSortRace", "Race") : Localization.Extra("shopSortType", "Type"),
                _ => Localization.Extra("shopSortDefault", "Default"),
            };
            sortButton.text = string.Format(Localization.Extra("shopSortBy", "Sort: {0}"), by).OgCase();
        }

        /// <summary>The tools on the header bar: the Remake's after the tabs, the Original's between its title and its tabs
        /// (the original's tabs sit flush right). Checked on every open, like Back's place.</summary>
        void PlaceVipTools()
        {
            if (vipTools == null || closeTop == null) return;
            vipTools.RemoveFromHierarchy();
            if (Settings.OriginalUi) closeTop.Insert(closeTop.IndexOf(ogTitle) + 1, vipTools);
            else
            {
                var spacer = closeTop.Q(className: "hangar-top-spacer");
                closeTop.Insert(spacer != null && spacer.parent == closeTop ? closeTop.IndexOf(spacer) : closeTop.childCount, vipTools);
            }
        }

        /// <summary>Rebuild: the Ships tab and the tools shown with the card's perks (a card bought just now shows them at
        /// once); without them the Ships tab falls back to the Store.</summary>
        void UpdateVipTools()
        {
            if (vipTools == null) return;
            bool on = VipTools;
            if (!on && tab == Tab.Ships) tab = Tab.Shop;
            tabShips.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            tabShips.EnableInClassList("hangar-tab--active", tab == Tab.Ships);
            bool shown = SearchTab;
            vipTools.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (!shown && searchFocused) searchField.Blur();
            RefreshVipTexts();
        }

        /// <summary>Open: the search starts empty; the sort stays.</summary>
        void ResetVipTools()
        {
            search = "";
            searchField?.SetValueWithoutNotify("");
            PlaceVipTools();
        }

        /// <summary>F / the controller's right stick press: into the search box (with a controller its keyboard opens over the
        /// screen, PadKeyboard). Focused a frame later, so the F itself isn't typed into it.</summary>
        public void FocusSearch(bool pad)
        {
            if (!SearchTab) return;
            menu.PlayPush();
            if (pad) { PadKeyboard.Open(screenRoot, searchField); return; }
            searchField.schedule.Execute(() => { if (SearchTab) searchField.Focus(); });
        }

        /// <summary>The sort button / R / the controller's left stick press: the next order.</summary>
        public void CycleSort()
        {
            if (!SearchTab) return;
            menu.PlayPush();
            sortMode = (sortMode + 1) % SortModes;
            RefreshVipTexts();
            Rebuild();
        }

        /// <summary>The tabs in the order Q / E (LB / RB) walk them: the Ships tab after the Store while the card's on.</summary>
        Tab StepTab(int step)
        {
            var order = VipTools ? new[] { Tab.Ship, Tab.Shop, Tab.Ships, Tab.Blueprints } : new[] { Tab.Ship, Tab.Shop, Tab.Blueprints };
            int i = Array.IndexOf(order, tab);
            if (i < 0) i = 1;
            return order[(i + step + order.Length) % order.Length];
        }

        // ---- search and sorting (the club's storage only) -------------------------------------------------------

        static StringComparer NameOrder => StringComparer.Create(CultureInfo.CurrentCulture, true);

        bool Matches(params string[] texts)
        {
            if (!VipTools || search.Length == 0) return true;
            var cmp = CultureInfo.CurrentCulture.CompareInfo;
            foreach (var t in texts)
                if (!string.IsNullOrEmpty(t) && cmp.IndexOf(t, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0) return true;
            return false;
        }

        bool ItemMatches(int item) => Matches(ItemInfo.ItemName(item), ItemInfo.Category(level.Database.Item(item)), GameNames.ItemTag(item));

        bool ShipMatches(int ship) => Matches(ItemInfo.ShipName(ship), ItemInfo.ShipRaceText(ship), GameNames.ShipTag(ship));

        /// <summary>An item's worth for the Price order: the middle of its price range (the storage shows no prices).</summary>
        long ItemValue(int item)
        {
            var it = level.Database.Item(item);
            return it == null ? 0 : ((long)it.minPrice + it.maxPrice) / 2;
        }

        /// <summary>One type's storage items in the chosen order (the club's own order without the card or by default).</summary>
        List<int> SortItems(IEnumerable<int> items)
        {
            if (!VipTools) return items.ToList();
            var names = NameOrder;
            switch ((Sort)sortMode)
            {
                case Sort.Name: return items.OrderBy(ItemInfo.ItemName, names).ToList();
                case Sort.Price: return items.OrderByDescending(ItemValue).ThenBy(ItemInfo.ItemName, names).ToList();
                case Sort.Race:
                    return items.OrderBy(i => ItemInfo.Category(level.Database.Item(i)), names).ThenBy(ItemInfo.ItemName, names).ToList();
                default: return items.ToList();
            }
        }

        /// <summary>The stored hulls (indices into Session.KaamoShips) the search finds, in the chosen order.</summary>
        List<int> StoredShips()
        {
            var names = NameOrder;
            var found = Enumerable.Range(0, Session.KaamoShips.Count).Where(i => ShipMatches(Session.KaamoShips[i].ship));
            string Name(int i) => ItemInfo.ShipName(Session.KaamoShips[i].ship);
            switch ((Sort)sortMode)
            {
                case Sort.Name: return found.OrderBy(Name, names).ToList();
                case Sort.Price: return found.OrderByDescending(i => hangar.StoredPrice(i)).ThenBy(Name, names).ToList();
                case Sort.Race:
                    return found.OrderBy(i => { int r = Shop.ShipMakerRace(Session.KaamoShips[i].ship); return r < 0 ? int.MaxValue : r; })
                                .ThenBy(Name, names).ToList();
                default: return found.ToList();
            }
        }

        /// <summary>The Ships tab: the stored hulls (the Store tab lists only the goods while the card's perks are on).</summary>
        void AddStoredShipsTab()
        {
            AddHeader(Localization.Get(173).UiCaps());
            var order = StoredShips();
            foreach (int i in order) AddRow(new Row { kind = RowKind.StoredShip, ship = Session.KaamoShips[i].ship, equipment = i });
            if (order.Count > 0) return;
            AddNote(Session.KaamoShips.Count == 0
                ? Localization.Extra("kaamoNoShips", "No ships stored yet. Keep your old ship when you buy a new one and it is parked here.")
                : NoMatchText());
        }

        string NoMatchText() => string.Format(Localization.Extra("shopSearchNone", "Nothing matches \"{0}\"."), search);

        // ---- the card -----------------------------------------------------------------------------------------

        static Texture2D vipIcon, homeIcon;
        static Texture2D VipIcon => vipIcon != null ? vipIcon : vipIcon = Resources.Load<Texture2D>("GoF2Races/kaamo_club_icon");
        static Texture2D HomeIcon => homeIcon != null ? homeIcon : homeIcon = Resources.Load<Texture2D>("GoF2Hud/map_home");

        static string VipPerks() => Localization.Extra("kaamoVipText",
            "Become a VIP member of the hottest club in the universe. Your card upgrades the Kaamo Club's storage:\n\n"
            + "• a search box and sorting by name, value and race or type\n"
            + "• your stored ships in a Ships tab of their own\n"
            + "• a Kaamo marker on every ship for sale anywhere that you already keep here");

        /// <summary>The card's row: the club's icon, its name (80), the perks in a line, the price at the right edge.</summary>
        void FillVipRow(VisualElement e, Label name, Label subText, Label price, out Texture2D tex)
        {
            tex = VipIcon;
            e.AddToClassList("list-row--vip");
            name.text = Localization.Get(80);
            subText.text = Localization.Extra("kaamoVipRow", "Search, sorting and a Ships tab for your club");
            price.text = ItemInfo.Credits(KaamoClub.VipPrice);
            price.EnableInClassList("row-price--expensive", KaamoClub.VipPrice > Session.Credits);
        }

        void ShowVipDetails()
        {
            detailIcon.style.backgroundImage = new StyleBackground(VipIcon);
            detailName.text = Localization.Get(80);
            detailSub.text = Localization.Get(78);   // "Kaamo Club"
            detailText.text = VipPerks();
            ShowAction($"{Localization.Extra("shopBuy", "BUY").UiCaps()}   {ItemInfo.Credits(KaamoClub.VipPrice)}", true);
            detailScroll.scrollOffset = Vector2.zero;
        }

        /// <summary>BUY on the card: 203 when the credits don't reach, else a Yes / No, then the card and a welcome.</summary>
        void AskBuyVip()
        {
            if (!KaamoClub.VipForSale) return;
            if (Session.Credits < KaamoClub.VipPrice)
            {
                menu.ShowToast(Localization.Get(203).Replace("#C", ItemInfo.Credits(KaamoClub.VipPrice - Session.Credits)));
                return;
            }
            menu.PlayRelease();
            menu.ShowDialog(string.Format(Localization.Extra("kaamoVipConfirm", "Buy the Kaamo Club VIP Card for {0}?"), ItemInfo.Credits(KaamoClub.VipPrice)), () =>
            {
                if (!KaamoClub.BuyVip()) return;
                menu.PlayClip(menu.shopBuy);
                selected = null;
                Rebuild();
                menu.ShowDialog(Localization.Extra("kaamoVipBought",
                    "Welcome to the club, VIP! Your storage now has a search box, sorting and a Ships tab, and ships you already keep here are marked wherever they are sold."), null, true);
            });
        }

        /// <summary>A dealer's ship of a type parked at the club: AT KAAMO with the map's orange house.</summary>
        static VisualElement KaamoBadge()
        {
            var b = new VisualElement { pickingMode = PickingMode.Ignore };
            b.AddToClassList("row-badge");
            b.AddToClassList("row-badge--kaamo");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("row-badge-icon");
            if (HomeIcon != null) icon.style.backgroundImage = new StyleBackground(HomeIcon);
            b.Add(icon);
            var l = new Label(Localization.Extra("shopAtKaamo", "AT KAAMO").OgCase()) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("row-badge-text");
            l.AddToClassList("gof-semibold");
            b.Add(l);
            return b;
        }

        /// <summary>The marker's rule: the card's perks on, a ship of a type the club keeps, not in the club's own storage.</summary>
        bool MarkAtKaamo(int ship) => KaamoClub.VipFeatures && hangar != null && !hangar.Storage && KaamoClub.HasShip(ship);
    }
}
