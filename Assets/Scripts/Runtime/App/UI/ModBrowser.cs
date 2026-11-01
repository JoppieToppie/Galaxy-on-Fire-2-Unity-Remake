// ModBrowser.cs
// The main menu's Mods panel (remake): every installed mod (Modding.ModManager) with its preview image, name, version,
// author and description; on the left the list in load order (a thumbnail, the name, ON / OFF / an error badge), on
// the right the selected mod: the big preview, the texts, its problems, and Turn on / off, Earlier / Later (the load
// order: a later mod's changes win) and Delete (asks first), plus Open mods folder (desktop; elsewhere the folder's
// path), Import mod (Android, the Editor: the system's file picker, ModImport; players couldn't reach the Android Mods
// folder) and Refresh. Mods are turned on and off only here, in the menu: the game's tables are rebuilt when a game
// starts or loads.
// Plain C#: MainMenu creates it in its panel host and hands it its sound, focus and dialog hooks.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class ModBrowser
    {
        public VisualElement Panel { get; }

        readonly Action<VisualElement> hookFocus;
        readonly Action click;
        readonly ScrollView list;
        readonly Label count, empty, note;
        readonly VisualElement preview, detail;
        readonly Label name, meta, description, problems;
        readonly Button toggle, earlier, later, delete, edit;
        /// <summary>A DLC mod's Play earlier / later buttons (its place in the DLCs' play order, ModDlcs).</summary>
        readonly Button dlcEarlier, dlcLater;
        /// <summary>Edit in Mod Builder: the selected mod as a Mod Builder project (MainMenu: ModBuilderProjects.ImportInstalled);
        /// the button shows only when this is set.</summary>
        public Action<ModInfo> EditInBuilder;
        readonly Action<string, string, Action> ask;
        readonly Action<string, string> notice;
        bool importing;
        readonly Dictionary<string, Button> rows = new Dictionary<string, Button>();
        string selected;

        static string T(string key, string english) => Localization.Extra(key, english);

        /// <summary>A folder or file as the player finds it. iOS: the app's Documents folder is the Files app's "On My iPhone
        /// › GoF2 Remake" (Info.plist UIFileSharingEnabled, the home screen name), not the sandbox path; elsewhere the path.</summary>
        static string Shown(string path)
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer || string.IsNullOrEmpty(path)) return path;
            string docs = Application.persistentDataPath.TrimEnd('/');
            if (!path.StartsWith(docs, System.StringComparison.Ordinal)) return path;
            string device = SystemInfo.deviceModel.StartsWith("iPad", System.StringComparison.Ordinal)
                ? T("filesOnMyIpad", "Files › On My iPad") : T("filesOnMyIphone", "Files › On My iPhone");
            return device + " › GoF2 Remake" + path.Substring(docs.Length).Replace("/", " › ");
        }

        /// <param name="rebuildCache">The footer's "Rebuild cache": every mod cache deleted and the mods loaded again (MainMenu,
        /// behind its loading screen); none = no button.</param>
        /// <param name="ask">The menu's Yes / No dialog (title, text, on Yes): Delete and replacing a mod by an import.</param>
        /// <param name="notice">The menu's notice (title, text): what an import or a delete came to.</param>
        public ModBrowser(VisualElement host, Action back, Action<VisualElement> hookFocus, Action click, Action rebuildCache,
                          Action<string, string, Action> ask, Action<string, string> notice)
        {
            this.hookFocus = hookFocus;
            this.click = click;
            this.ask = ask;
            this.notice = notice;
            Panel = new VisualElement { name = "modsPanel" };
            Panel.AddToClassList("panel");
            Panel.AddToClassList("mods-panel");
            Panel.usageHints = UsageHints.DynamicTransform;

            var titleRow = Add(Panel, new VisualElement(), "mods-title-row");
            Add(titleRow, new Label(T("modBrowser", "Mod Browser").ToUpperInvariant()), "panel-title", "gof-semibold");
            Add(titleRow, new VisualElement(), "mp-title-spacer");
            count = Add(titleRow, new Label(), "mods-count");
            Add(Panel, new VisualElement(), "panel-accent");

            var body = Add(Panel, new VisualElement(), "mods-body");
            var left = Add(body, new VisualElement(), "mods-left");
            list = Add(left, new ScrollView(ScrollViewMode.Vertical), "mods-list");
            list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(list);
            empty = Add(left, new Label(), "mods-empty");

            detail = Add(body, new VisualElement(), "mods-detail");
            preview = Add(detail, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-preview");
            name = Add(detail, new Label(), "mods-name", "gof-semibold");
            meta = Add(detail, new Label(), "mods-meta");
            var descScroll = Add(detail, new ScrollView(ScrollViewMode.Vertical), "mods-desc-scroll");
            descScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            descScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            description = Add(descScroll, new Label(), "mods-desc");
            problems = Add(descScroll, new Label(), "mods-problems");
            var actions = Add(detail, new VisualElement(), "mods-actions");
            toggle = ActionButton(actions, "", Toggle, "mods-toggle");
            // The controller's X toggles the selected mod from anywhere in the panel (MainMenu: ToggleSelected); its
            // glyph on the button shows while a controller is in use (.input-gamepad).
            Add(toggle, InputGlyph.Pad(PadButton.X), "mods-pad-x");   // X / square, in the style of the controller in use
            earlier = ActionButton(actions, T("modsEarlier", "Earlier"), () => Move(-1));
            later = ActionButton(actions, T("modsLater", "Later"), () => Move(1));
            edit = ActionButton(actions, T("modsEditInBuilder", "Edit in Mod Builder"), () =>
            {
                var mod = selected != null ? ModManager.Find(selected) : null;
                if (mod != null) EditInBuilder?.Invoke(mod);
            }, "mods-edit");   // Modified: its own class, so its text may wrap (MainMenu.uss)
            Add(actions, new VisualElement(), "mods-row-break");   // Modified: Delete on its own row, under Turn on / off
            delete = ActionButton(actions, T("modsDelete", "Delete"), AskDelete, "mods-delete");
            // Remake mods: a DLC's place among the DLCs that play after Supernova (ModDlcs), the player's to change; beside
            // Delete (its own row would push the detail into the footer on a TV or a phone). The place is in the meta lines.
            dlcEarlier = ActionButton(actions, T("modsDlcEarlier", "Play earlier"), () => MoveDlc(-1));
            dlcLater = ActionButton(actions, T("modsDlcLater", "Play later"), () => MoveDlc(1));

            var footer = Add(Panel, new VisualElement(), "mods-footer");
            note = Add(footer, new Label(), "mods-note");
            if (!Application.isMobilePlatform) ActionButton(footer, T("modsOpenFolder", "Open mods folder"),
                () => ModManager.OpenFolder(selected != null ? ModManager.Find(selected) : null), "mods-footer-button");   // the selected mod's folder
            if (ModImport.Available) ActionButton(footer, T("modsImport", "Import mod"), Import, "mods-footer-button").AddToClassList("mods-import");
            ActionButton(footer, T("modsRefresh", "Refresh"), () => { ModManager.Scan(); Rebuild(); }, "mods-footer-button");
            // When something looks wrong after a mod changed: the cached textures, hangar shadows and unpacked zips are made again.
            if (rebuildCache != null) ActionButton(footer, T("modsRebuildCache", "Rebuild cache"), rebuildCache, "mods-footer-button");
            var backButton = Add(Panel, new Button(back) { text = "‹  " + Localization.Get(170).ToUpperInvariant() }, "menu-button", "back-button", "gof-semibold");
            hookFocus(backButton);

            host.Add(Panel);
        }

        static TE Add<TE>(VisualElement parent, TE e, params string[] classes) where TE : VisualElement
        {
            foreach (var c in classes) e.AddToClassList(c);
            parent.Add(e);
            return e;
        }

        Button ActionButton(VisualElement parent, string text, Action onClick, string extra = null)
        {
            var b = new Button(() => { click(); onClick(); }) { text = text.ToUpperInvariant() };
            b.AddToClassList("mods-action");
            if (extra != null) b.AddToClassList(extra);
            hookFocus(b);
            parent.Add(b);
            return b;
        }

        /// <summary>Opening the panel: the folders read again, the list rebuilt.</summary>
        public void Open()
        {
            ModManager.Scan();
            Rebuild();
        }

        /// <summary>The list and the details again (after a rescan).</summary>
        public void Rebuild()
        {
            list.Clear();
            rows.Clear();
            var mods = ModManager.Installed;
            foreach (var m in mods)
            {
                var row = new Button(() => { click(); Select(m.Id); });
                row.AddToClassList("mods-row");
                var thumb = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-thumb");
                if (m.Preview != null) thumb.style.backgroundImage = new StyleBackground(m.Preview);
                var text = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-row-text");
                Add(text, new Label(m.Name) { pickingMode = PickingMode.Ignore }, "mods-row-name");
                Add(text, new Label(Sub(m)) { pickingMode = PickingMode.Ignore }, "mods-row-sub");
                Add(row, new Label { pickingMode = PickingMode.Ignore }, "mods-badge");
                // Keyboard / controller focus selects the mod; the mouse's hover focus (hookFocus) doesn't, or passing over
                // the rows on the way to Turn on changed the mod it acts on. A click selects (the button's action).
                int hoverFrame = -10;
                row.RegisterCallback<PointerEnterEvent>(_ => hoverFrame = Time.frameCount);   // before hookFocus's own
                hookFocus(row);
                row.RegisterCallback<FocusInEvent>(_ => { if (Time.frameCount - hoverFrame > 1) Select(m.Id); });
                list.Add(row);
                rows[m.Id] = row;
            }
            empty.text = ModImport.Available && Application.isMobilePlatform
                ? string.Format(T("modsEmptyImport", "No mods installed. Press Import mod, or put a mod into:\n{0}\nthen press Refresh."), Shown(ModManager.MainFolder))
                : string.Format(T("modsEmpty", "No mods installed. Put a mod into:\n{0}\nthen press Refresh."), Shown(ModManager.MainFolder));
            empty.style.display = mods.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (selected == null || !rows.ContainsKey(selected)) selected = mods.FirstOrDefault()?.Id;
            Refresh();
            // The selected row in view once laid out (a rebuilt list starts at the top).
            if (selected != null && rows.TryGetValue(selected, out var picked))
                list.schedule.Execute(() => { if (list.contentContainer.Contains(picked)) list.ScrollTo(picked); });
        }

        static string Sub(ModInfo m)
        {
            var parts = new List<string> { "v" + m.Version };
            if (!string.IsNullOrEmpty(m.Manifest?.author)) parts.Add(m.Manifest.author);
            return string.Join("  ·  ", parts);
        }

        void Select(string id)
        {
            if (selected == id) return;
            selected = id;
            Refresh();
        }

        /// <summary>The badges, the selection and the detail side for what is selected now.</summary>
        void Refresh()
        {
            var active = ModManager.Active;
            foreach (var m in ModManager.Installed)
            {
                if (!rows.TryGetValue(m.Id, out var row)) continue;
                var badge = row.Q<Label>(className: "mods-badge");
                bool on = ModManager.IsEnabled(m.Id), works = active.Contains(m);
                badge.text = m.Broken ? T("modsError", "Error").ToUpperInvariant()
                    : on && !works ? T("modsProblem", "Can't load").ToUpperInvariant()
                    : on ? T("on", "On").ToUpperInvariant() : T("off", "Off").ToUpperInvariant();
                badge.EnableInClassList("mods-badge--on", on && works);
                badge.EnableInClassList("mods-badge--error", m.Broken || on && !works);
                row.EnableInClassList("mods-row--selected", m.Id == selected);
                row.EnableInClassList("mods-row--off", !on);
            }
            int enabled = ModManager.Installed.Count(m => ModManager.IsEnabled(m.Id));
            count.text = string.Format(T("modsCount", "{0} installed  ·  {1} on"), ModManager.Installed.Count, enabled);
            note.text = T("modsNote", "Mods apply when a game starts or loads. Lower in the list wins.")
                        + (Application.isMobilePlatform ? "\n" + Shown(ModManager.MainFolder) : "");   // no folder button on a phone: where to copy them

            var mod = selected != null ? ModManager.Find(selected) : null;
            detail.style.visibility = mod != null ? Visibility.Visible : Visibility.Hidden;
            if (mod == null) return;
            preview.style.backgroundImage = mod.Preview != null ? new StyleBackground(mod.Preview) : new StyleBackground();
            preview.EnableInClassList("mods-preview--none", mod.Preview == null);
            name.text = mod.Name;
            var metaParts = new List<string> { "v" + mod.Version };
            if (!string.IsNullOrEmpty(mod.Manifest?.author)) metaParts.Add(string.Format(T("modsBy", "by {0}"), mod.Manifest.author));
            metaParts.Add(mod.Id);
            if (!string.IsNullOrEmpty(mod.Manifest?.website)) metaParts.Add(mod.Manifest.website);
            meta.text = string.Join("  ·  ", metaParts) + "\n" + Shown(mod.Source.Location);   // where it is installed
            description.text = mod.Manifest?.Description ?? "";
            if (!string.IsNullOrEmpty(mod.Manifest?.Credits))
                description.text += "\n\n" + T("modsCredits", "Credits").ToUpperInvariant() + "\n" + mod.Manifest.Credits;
            var lines = new List<string>();
            bool isOn = ModManager.IsEnabled(mod.Id);
            if (mod.Broken) lines.AddRange(mod.Errors.Select(e => "× " + e));
            else if (isOn && ModManager.InactiveReason(mod) is string why) lines.Add("× " + why);
            if (mod.Manifest?.dependencies.Count > 0)
                lines.Add(string.Format(T("modsNeeds", "Needs: {0}"), string.Join(", ", mod.Manifest.dependencies.Select(d =>
                {
                    var dm = ModManager.Find(d);
                    string label = (dm?.Name ?? d) + (mod.Manifest.dependencyVersions.TryGetValue(d, out string v) ? " " + v + "+" : "");
                    string state = dm == null ? T("modsDepMissing", "not installed") : mod.Manifest.TooOld(dm) != null ? string.Format(T("modsDepOld", "v{0} installed"), dm.Version)
                                 : ModManager.IsEnabled(dm.Id) ? T("modsDepOn", "on") : T("modsDepOff", "off");
                    return $"{label} ({state})";
                }))));
            var neededBy = isOn ? ModManager.NeededBy(mod.Id) : new List<ModInfo>();
            if (neededBy.Count > 0)
                lines.Add(string.Format(T("modsNeededBy", "Needed by: {0} (turning it off turns them off)"), string.Join(", ", neededBy.Select(m => m.Name))));
            lines.AddRange(mod.Warnings.Take(8).Select(w => "! " + w));
            if (mod.Warnings.Count > 8) lines.Add($"! … {mod.Warnings.Count - 8} more (see the log)");
            problems.text = string.Join("\n", lines);
            problems.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            toggle.text = (isOn ? T("modsTurnOff", "Turn off") : T("modsTurnOn", "Turn on")).ToUpperInvariant();
            toggle.SetEnabled(!mod.Broken || isOn);
            delete.SetEnabled(mod.Source != null);
            edit.style.display = EditInBuilder != null ? DisplayStyle.Flex : DisplayStyle.None;
            edit.SetEnabled(mod.Manifest != null && mod.Source != null);
            toggle.EnableInClassList("mods-toggle--on", isOn);
            var ids = ModManager.Installed.Where(m => ModManager.IsEnabled(m.Id)).Select(m => m.Id).ToList();
            int i = ids.IndexOf(mod.Id);
            earlier.SetEnabled(i > 0);
            later.SetEnabled(i >= 0 && i < ids.Count - 1);
            // A DLC that is on: its place in the play order.
            int place = ModDlcs.Place(mod.Id), dlcs = ModDlcs.All().Count;
            dlcEarlier.style.display = dlcLater.style.display = place > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (place > 0)
            {
                meta.text += "\n" + string.Format(T("modsDlcPlace", "DLC {0} of {1}: plays after {2}."), place, dlcs,
                                                  place == 1 ? "Supernova" : ModDlcs.All()[place - 2].Name);
                dlcEarlier.SetEnabled(place > 1);
                dlcLater.SetEnabled(place < dlcs);
            }
        }

        void MoveDlc(int delta)
        {
            if (selected == null) return;
            ModDlcs.Move(selected, delta);
            Refresh();
        }

        /// <summary>Delete: asks (naming the mods that need it, which go off with it), then ModImport.Delete.</summary>
        void AskDelete()
        {
            var mod = selected != null ? ModManager.Find(selected) : null;
            if (mod == null) return;
            string title = T("modsDeleteTitle", "Delete mod");
            string text = string.Format(T("modsDeleteText", "Delete \"{0}\" from this device? This can't be undone."), mod.Name);
            var needers = ModManager.IsEnabled(mod.Id) ? ModManager.NeededBy(mod.Id) : new List<ModInfo>();
            if (needers.Count > 0)
                text += "\n" + string.Format(T("modsDeleteNeeded", "These mods need it and are turned off: {0}"), string.Join(", ", needers.Select(m => m.Name)));
            ask(title, text, () =>
            {
                int at = ModManager.Installed.ToList().FindIndex(m => m.Id == mod.Id);
                bool deleted = ModImport.Delete(mod, out string error);
                if (!deleted) notice(title, string.Format(T("modsDeleteFailed", "The mod couldn't be deleted: {0}"), error));
                // The mod that took its place in the list (the last one when it was last), not the top one.
                var mods = ModManager.Installed;
                selected = !deleted ? mod.Id : mods.Count > 0 ? mods[Mathf.Clamp(at, 0, mods.Count - 1)].Id : null;
                Rebuild();
            });
        }

        /// <summary>Import mod: the file picker (ModImport.Pick, polled until it answers), the file checked as a mod, a
        /// replacement asked first, then installed and selected (off, like any new mod).</summary>
        void Import()
        {
            if (importing) return;
            importing = true;
            var busy = Panel.Q<Button>(className: "mods-import");
            UiBusy.Mark(busy, true);   // the picker and the copy can take a while (docs/UI-DESIGN.md "Waiting")
            var poll = ModImport.Pick();
            ModImport.Picked picked = null;
            Panel.schedule.Execute(() =>
            {
                picked = poll();
                if (picked != null) { importing = false; UiBusy.Mark(busy, false); Picked(picked); }
            }).Every(200).Until(() => picked != null);
        }

        void Picked(ModImport.Picked p)
        {
            string title = T("modsImport", "Import mod");
            if (p.cancelled) return;
            if (p.error != null) { notice(title, string.Format(T("modsImportFailed", "The mod couldn't be imported: {0}"), p.error)); return; }
            // A mod for the original game (.aem / .aei, no mod.json) is no mod of the remake's: the Mod Builder makes one of it.
            if (OriginalModImport.Looks(p.path))
            {
                ModImport.Discard(p.path);
                notice(title, T("modsImportOriginal", "That's a mod for the original game (its .aem / .aei files). The Mod Builder makes a mod of it: Mod Builder > Import mod > From a file..."));
                return;
            }
            var manifest = ModImport.Inspect(p.path, out string error);
            if (manifest == null)
            {
                ModImport.Discard(p.path);
                notice(title, T("modsImportNotMod", "That file isn't a mod: pick a mod's .zip with mod.json inside.")
                              + "\n(" + p.name + ": " + error + ")");
                return;
            }
            void Install()
            {
                if (ModImport.Install(p.path, p.name, manifest, out string why) == null)
                    notice(title, string.Format(T("modsImportFailed", "The mod couldn't be imported: {0}"), why));
                else
                {
                    selected = manifest.id;
                    Rebuild();
                    notice(title, string.Format(ModManager.IsEnabled(manifest.id)
                        ? T("modsImportedOn", "\"{0}\" {1} is installed and on.")
                        : T("modsImported", "\"{0}\" {1} is installed. Turn it on to use it."), manifest.Name, "v" + manifest.version));
                }
            }
            var old = ModManager.Find(manifest.id);
            if (old == null) Install();
            else ask(title, string.Format(T("modsImportReplace", "\"{0}\" is already installed (v{1}). Replace it with v{2}?"),
                                          old.Name, old.Version, manifest.version), Install);
        }

        /// <summary>The controller's X: turns the selected mod on or off, like its Turn on / off button (not a broken mod
        /// that is off).</summary>
        public void ToggleSelected()
        {
            if (selected == null || !toggle.enabledSelf) return;
            click();
            Toggle();
        }

        void Toggle()
        {
            if (selected == null) return;
            ModManager.SetEnabled(selected, !ModManager.IsEnabled(selected));
            ReorderRows();
        }

        void Move(int delta)
        {
            if (selected == null) return;
            ModManager.Move(selected, delta);
            ReorderRows();
        }

        /// <summary>The rows in the new load order, keeping the elements (and so the focus).</summary>
        void ReorderRows()
        {
            foreach (var m in ModManager.Installed) if (rows.TryGetValue(m.Id, out var r)) r.BringToFront();
            Refresh();
        }
    }
}
