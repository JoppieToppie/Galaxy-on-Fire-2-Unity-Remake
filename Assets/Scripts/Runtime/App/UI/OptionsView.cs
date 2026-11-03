// OptionsView.cs
// Remake: the in-game Options page (the flight pause menu's and the station's system menu), built like the main menu's
// Options panel (MainMenu.uxml optionsPanel) with the same classes (GoF2Common.uss): the title with its amber accent,
// the tabs Sound / Graphics / Controls / Gameplay / Language (only those with rows), one scrolling page of OptionsCatalog
// rows per tab, then Back and Default settings (497) under them; Default settings asks first, in the footer's place,
// whether to reset only the shown tab or every tab (or Cancel). Every option of the catalog is here, the in-game ones
// (OptionDef.inGameOnly) too, the text language among them: the station menu and the flight HUD re-label themselves on
// Localization.Changed, so it applies without leaving the game (the main menu keeps its own language buttons).
// The host drives the keys / controller: NavItems is what Up / Down walks (the tab row, the active tab's rows, Back,
// Default settings); Q / E and LB / RB switch tabs (StepTab). Rows are focusable for a host that uses the panel's focus
// (the station); the pause menu moves a highlight instead (Select).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class OptionsView
    {
        static readonly OptionPage[] PageOrder = { OptionPage.Sound, OptionPage.Graphics, OptionPage.Controls, OptionPage.Bindings, OptionPage.Gameplay, OptionPage.Accessibility, OptionPage.Language };

        sealed class Tab
        {
            public OptionPage page;
            public Button button;
            public ScrollView scroll;
            public readonly List<OptionControl> rows = new List<OptionControl>();
        }

        readonly List<Tab> tabs = new List<Tab>();
        readonly List<OptionControl> all = new List<OptionControl>();
        readonly Dictionary<VisualElement, OptionControl> byField = new Dictionary<VisualElement, OptionControl>();
        readonly bool focusableRows;
        readonly Label title;
        VisualElement selected;

        /// <summary>The panel: add it where the menu's panel goes.</summary>
        public readonly VisualElement Root;
        public readonly Button BackButton, DefaultsButton;
        public int TabIndex { get; private set; }

        /// <summary>The player changed an option through a row (after every other row has followed).</summary>
        public event Action<OptionControl> Changed;
        /// <summary>Another tab is shown (a click, StepTab).</summary>
        public event Action TabChanged;
        /// <summary>The defaults prompt reset this tab or every tab (Default settings, 497).</summary>
        public event Action DefaultsRestored;

        static string T(int id) => Localization.Get(id).UiCaps();

        readonly Action back;

        public OptionsView(Action back, bool focusableRows)
        {
            this.back = back;
            this.focusableRows = focusableRows;
            Root = new VisualElement();
            Root.AddToClassList("options-panel");
            title = new Label(T(31)) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("panel-title");
            title.AddToClassList("gof-semibold");
            Root.Add(title);
            var accent = new VisualElement { pickingMode = PickingMode.Ignore };
            accent.AddToClassList("panel-accent");
            Root.Add(accent);

            var tabRow = new VisualElement();
            tabRow.AddToClassList("tab-row");
            Root.Add(tabRow);
            var defs = OptionsCatalog.All();
            foreach (var page in PageOrder)
            {
                if (!defs.Exists(d => d.page == page)) continue;
                var tab = new Tab { page = page };
                int index = tabs.Count;
                tab.button = new Button { text = OptionsCatalog.PageTitle(page).UiCaps(), focusable = focusableRows };
                tab.button.AddToClassList("tab-button");
                tab.button.AddToClassList("gof-semibold");
                tab.button.clicked += () => SelectTab(index);
                tabRow.Add(tab.button);
                // Like the main menu's pages: wheel, touch and focus scrolling, no scroll bars (a drag would fight the sliders).
                tab.scroll = new ScrollView(ScrollViewMode.Vertical)
                {
                    horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                    verticalScrollerVisibility = ScrollerVisibility.Hidden,
                };
                tab.scroll.AddToClassList("tab-page");
                tab.scroll.AddToClassList("options-scroll");
                Root.Add(tab.scroll);
                tabs.Add(tab);
            }
            foreach (var def in defs)
            {
                if (def.menuOnly) continue;   // only in the main menu (the save folder)
                var tab = tabs.Find(t => t.page == def.page);
                if (tab == null) continue;
                var c = new OptionControl(def);
                c.Field.AddToClassList("option-row");
                c.Field.focusable = focusableRows;
                // Options depend on each other (STP turns MSAA off): every row follows a change.
                c.Changed += () => { foreach (var o in all) if (o != c) o.Refresh(); Changed?.Invoke(c); };
                var row = c.Root;
                var scroll = tab.scroll;
                if (focusableRows) c.Field.RegisterCallback<FocusInEvent>(_ => { if (!DragScroll.PointerActive) scroll.ScrollTo(row); });
                tab.scroll.Add(c.Root);
                tab.rows.Add(c);
                all.Add(c);
                byField[c.Field] = c;
            }

            var footer = new VisualElement();
            footer.AddToClassList("options-footer");
            Root.Add(footer);
            BackButton = new Button(() => back?.Invoke()) { text = "‹  " + T(170), focusable = focusableRows };
            BackButton.AddToClassList("menu-button");
            BackButton.AddToClassList("back-button");
            BackButton.AddToClassList("gof-semibold");
            InputGlyph.PadPrompt(BackButton, PadButton.B);   // B for a controller (docs/UI-DESIGN.md "Button prompts")
            footer.Add(BackButton);
            DefaultsButton = new Button(OpenResetPrompt) { text = T(497), focusable = focusableRows };
            DefaultsButton.AddToClassList("menu-button");
            DefaultsButton.AddToClassList("back-button");
            DefaultsButton.AddToClassList("options-defaults");
            DefaultsButton.AddToClassList("gof-semibold");
            footer.Add(DefaultsButton);

            // Remake (players' request): Default settings asks first, in place of the footer: this tab only, every tab, or
            // Cancel (Esc / B too), so a stray press resets nothing.
            prompt = new VisualElement();
            prompt.AddToClassList("options-reset-prompt");
            question = new Label(OptionsCatalog.ResetQuestion) { pickingMode = PickingMode.Ignore };
            question.AddToClassList("options-reset-question");
            prompt.Add(question);
            CancelButton = PromptButton(Localization.Get(425).UiCaps(), () => CloseResetPrompt());   // Cancel
            ResetTabButton = PromptButton("", () => Reset(false));
            ResetAllButton = PromptButton(OptionsCatalog.ResetAllLabel.UiCaps(), () => Reset(true));
            ResetAllButton.AddToClassList("options-reset-all");
            prompt.style.display = DisplayStyle.None;
            footer.Add(prompt);
            // Rework: the page's keys (select, change a value, switch tab, confirm, back) as a prompt row under the footer
            // (docs/UI-DESIGN.md "Button prompts"; it was an "i" at the tab row's end).
            prompts = MenuKeys.Bar(MenuKeys.Rows.Select | MenuKeys.Rows.Change | MenuKeys.Rows.Tabs | MenuKeys.Rows.Confirm | MenuKeys.Rows.Back);
            prompts.AddToClassList("prompt-bar--row");
            Root.Add(prompts);

            SelectTab(0, false);
            // Modified: the language row (OptionsCatalog "language") changes the table while this page is open: re-label it
            // then. Only while it is on a panel, so the views the menus drop on leaving the page don't stay subscribed.
            Root.RegisterCallback<AttachToPanelEvent>(_ => Localization.Changed += RefreshTexts);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => Localization.Changed -= RefreshTexts);
        }

        /// <summary>Modified: every text of the page from the table loaded now: the title, the tabs, the footer and the
        /// defaults prompt, and each row (OptionControl.Refresh re-reads its label, values and description).</summary>
        public void RefreshTexts()
        {
            title.text = T(31);
            foreach (var tab in tabs) tab.button.text = OptionsCatalog.PageTitle(tab.page).UiCaps();
            InputGlyph.SetText(BackButton, "‹  " + T(170));
            DefaultsButton.text = T(497);
            question.text = OptionsCatalog.ResetQuestion;
            CancelButton.text = Localization.Get(425).UiCaps();
            ResetTabButton.text = OptionsCatalog.ResetTabLabel(tabs[TabIndex].page).UiCaps();
            ResetAllButton.text = OptionsCatalog.ResetAllLabel.UiCaps();
            RefreshAll();
        }

        readonly VisualElement prompt;
        readonly Label question;
        readonly VisualElement prompts;
        public readonly Button CancelButton, ResetTabButton, ResetAllButton;

        Button PromptButton(string text, Action action)
        {
            var b = new Button(action) { text = text, focusable = focusableRows };
            b.AddToClassList("menu-button");
            b.AddToClassList("back-button");
            b.AddToClassList("gof-semibold");
            prompt.Add(b);
            return b;
        }

        /// <summary>The defaults prompt is showing (in place of Back and Default settings).</summary>
        public bool ResetPromptOpen => prompt.style.display == DisplayStyle.Flex;

        /// <summary>The footer's buttons as shown, left to right: Back and Default settings, or the prompt's Cancel, this
        /// tab, every tab.</summary>
        public IReadOnlyList<Button> FooterButtons => ResetPromptOpen
            ? new[] { CancelButton, ResetTabButton, ResetAllButton }
            : new[] { BackButton, DefaultsButton };

        public bool IsFooter(VisualElement e) => e is Button b && (b == BackButton || b == DefaultsButton || b == CancelButton || b == ResetTabButton || b == ResetAllButton);

        /// <summary>The footer button beside 'e' (dir -1 left, +1 right; stays at the ends).</summary>
        public Button FooterStep(VisualElement e, int dir)
        {
            var list = FooterButtons;
            int i = -1;
            for (int k = 0; k < list.Count; k++) if (list[k] == e) i = k;
            return list[Mathf.Clamp(i < 0 ? 0 : i + dir, 0, list.Count - 1)];
        }

        /// <summary>What a host with its own highlight (the pause menu) does on Enter / A for a footer button.</summary>
        public void Activate(VisualElement e)
        {
            if (e == BackButton) back?.Invoke();
            else if (e == DefaultsButton) OpenResetPrompt();
            else if (e == CancelButton) CloseResetPrompt();
            else if (e == ResetTabButton) Reset(false);
            else if (e == ResetAllButton) Reset(true);
        }

        /// <summary>The prompt opened or closed: the host moves its highlight / focus to 'FooterFocus'.</summary>
        public event Action FooterChanged;
        /// <summary>Where the highlight goes after the footer changed: "only this tab" when the prompt opens (the gentler
        /// choice), Default settings when it closes.</summary>
        public Button FooterFocus => ResetPromptOpen ? ResetTabButton : DefaultsButton;

        void OpenResetPrompt()
        {
            if (tabs.Count == 0) return;
            ResetTabButton.text = OptionsCatalog.ResetTabLabel(tabs[TabIndex].page).UiCaps();
            BackButton.style.display = DisplayStyle.None;
            DefaultsButton.style.display = DisplayStyle.None;
            prompt.style.display = DisplayStyle.Flex;
            FooterChanged?.Invoke();
        }

        /// <summary>Closes the defaults prompt (Cancel, Esc / B). False when it wasn't open.</summary>
        public bool CloseResetPrompt()
        {
            if (!ResetPromptOpen) return false;
            prompt.style.display = DisplayStyle.None;
            BackButton.style.display = StyleKeyword.Null;
            DefaultsButton.style.display = StyleKeyword.Null;
            FooterChanged?.Invoke();
            return true;
        }

        void Reset(bool everything)
        {
            if (everything) Settings.ResetToDefaults();
            else OptionsCatalog.ResetPage(tabs[TabIndex].page);
            RefreshAll();
            CloseResetPrompt();
            DefaultsRestored?.Invoke();
        }

        public bool IsShown => Root.resolvedStyle.display != DisplayStyle.None && Root.style.display != DisplayStyle.None;

        public void SetShown(bool shown) => Root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>The active tab's button (the tab row's place in NavItems).</summary>
        public Button ActiveTab => tabs.Count > 0 ? tabs[TabIndex].button : null;

        public bool IsTab(VisualElement e) => e != null && tabs.Exists(t => t.button == e);

        /// <summary>The row whose field this is (null for the tabs and the footer).</summary>
        public OptionControl RowOf(VisualElement field) => field != null && byField.TryGetValue(field, out var c) ? c : null;

        /// <summary>What Up / Down walks: the active tab, the active page's rows, then the footer's buttons (Back and Default
        /// settings, or the defaults prompt's); Up / Down treat the footer as one row (the hosts: FooterStep for left / right).</summary>
        public List<VisualElement> NavItems()
        {
            var list = new List<VisualElement>();
            if (ActiveTab != null) list.Add(ActiveTab);
            if (tabs.Count > 0) foreach (var c in tabs[TabIndex].rows) if (c.Shown && c.Enabled) list.Add(c.Field);
            list.AddRange(FooterButtons);
            return list;
        }

        /// <summary>The first footer button in NavItems (Up from the footer goes to the item before it).</summary>
        public int FooterStart(List<VisualElement> nav) => nav.Count - FooterButtons.Count;

        public void SelectTab(int index) => SelectTab(index, true);

        void SelectTab(int index, bool notify)
        {
            if (tabs.Count == 0) return;
            TabIndex = Mathf.Clamp(index, 0, tabs.Count - 1);
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].button.EnableInClassList("tab-button--active", i == TabIndex);
                tabs[i].scroll.EnableInClassList("tab-page--active", i == TabIndex);
            }
            tabs[TabIndex].scroll.scrollOffset = Vector2.zero;
            if (ResetPromptOpen) ResetTabButton.text = OptionsCatalog.ResetTabLabel(tabs[TabIndex].page).UiCaps();
            if (notify) TabChanged?.Invoke();
        }

        /// <summary>The next / previous tab, wrapping (Q / E, LB / RB, left / right on the tab row).</summary>
        public void StepTab(int dir)
        {
            if (tabs.Count > 1) SelectTab(((TabIndex + dir) % tabs.Count + tabs.Count) % tabs.Count);
        }

        /// <summary>The pause menu's highlight (its rows don't take focus): the row, tab or footer button looks focused.
        /// Modified: 'scrollTo' false leaves the page where it is (the row under the mouse).</summary>
        public void Select(VisualElement item, bool scrollTo = true)
        {
            if (selected != null) Mark(selected, false);
            selected = item;
            if (selected == null) return;
            Mark(selected, true);
            var row = RowOf(selected);
            if (row != null && scrollTo) tabs[TabIndex].scroll.ScrollTo(row.Root);
        }

        static void Mark(VisualElement e, bool on)
        {
            e.EnableInClassList("options-selected", on);
            // The shared controls' selected look (ChoiceRow values, BindingRow cells) keys on this class too.
            e.EnableInClassList("autopilot-menu-item--selected", on);
        }

        /// <summary>Every row re-reads its setting (a change made elsewhere).</summary>
        public void RefreshAll()
        {
            foreach (var c in all) c.Refresh();
        }

    }
}
