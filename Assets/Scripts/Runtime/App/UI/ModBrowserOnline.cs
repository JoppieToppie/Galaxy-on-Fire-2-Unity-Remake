// ModBrowserOnline.cs
// The Mod Browser's Online tab (remake, 2026-11-19; Discord "Mod browser browsing": players wanted to browse and download
// mods in the game, with pictures and descriptions): the community catalog (ModCatalog, github.com/JoppieToppie/gof2r-mods).
// The list: each mod's thumbnail, name, version, author and its views and downloads, a badge when it is installed or has
// an update; a search field (name, author, id, description) and the sort (most downloaded, newest, most viewed, name).
// The selected mod: its preview (loading it counts a view), texts, author, numbers, size, what it needs, a script mod's
// warning, and Install / Update to vX (its dependencies from the catalog too, after a question), Turn on / off once
// installed, Cancel while downloading. A download is checked against the catalog's size and SHA-256, then installed like
// an imported zip (ModImport.Inspect / Install: off when new, an update keeps on / off and the load order).
// The Installed tab shows "Update: vX" on mods the catalog has newer, with an Update button (UpdateSelected).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBrowser
    {
        Button TabButton(VisualElement parent, string text, bool toOnline)
        {
            var b = new Button(() => { click(); ShowOnline(toOnline); }) { text = text.UiCaps() };
            b.AddToClassList("tab-button");
            hookFocus(b);
            parent.Add(b);
            return b;
        }

        void ShowOnline(bool on)
        {
            if (online == on) return;
            online = on;
            ApplyMode();
            if (on) ModCatalog.Fetch();
            Rebuild();
        }

        /// <summary>The tabs' look and which parts of the panel each shows.</summary>
        void ApplyMode()
        {
            installedTab.EnableInClassList("tab-button--active", !online);
            onlineTab.EnableInClassList("tab-button--active", online);
            Panel.EnableInClassList("mods-panel--online", online);
            DisplayStyle Show(bool b) => b ? DisplayStyle.Flex : DisplayStyle.None;
            onlineBar.style.display = Show(online);
            installedActions.style.display = Show(!online);
            onlineActions.style.display = Show(online);
            foreach (var b in installedFooter) b.style.display = Show(!online);
            submitButton.style.display = Show(online);
        }

        void CatalogChanged()
        {
            if (Panel.panel == null) return;
            if (online) Rebuild();
            else if (ModCatalog.Status == ModCatalog.State.Ready) Rebuild();   // the update tags
        }

        // ---- the list ----

        static readonly string[] SortKeys = { "modsSortDownloads", "modsSortNewest", "modsSortViews", "modsSortName" };
        static readonly string[] SortNames = { "Most downloaded", "Newest", "Most viewed", "Name" };

        void CycleSort()
        {
            sortMode = (sortMode + 1) % SortKeys.Length;
            Rebuild();
        }

        static bool Has(string text, string q) => !string.IsNullOrEmpty(text) && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>The catalog's mods that match the search, in the chosen order.</summary>
        List<ModCatalog.Entry> Listed()
        {
            string q = search.value?.Trim() ?? "";
            var l = ModCatalog.Mods.Where(e => q.Length == 0 || Has(e.Name, q) || Has(e.author, q) || Has(e.id, q) || Has(e.Description, q));
            IOrderedEnumerable<ModCatalog.Entry> sorted = sortMode switch
            {
                1 => l.OrderByDescending(e => e.updated ?? "", StringComparer.Ordinal),   // ISO dates
                2 => l.OrderByDescending(e => e.views),
                3 => l.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
                _ => l.OrderByDescending(e => e.downloads),
            };
            return sorted.ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        void RebuildOnline()
        {
            list.Clear();
            onlineRows.Clear();
            sortButton.text = string.Format(T("modsSort", "Sort: {0}"), T(SortKeys[sortMode], SortNames[sortMode])).UiCaps();
            var mods = ModCatalog.Status == ModCatalog.State.Ready ? Listed() : new List<ModCatalog.Entry>();
            foreach (var e in mods)
            {
                var entry = e;
                var row = new Button(() => { click(); SelectOnline(entry.id); });
                row.AddToClassList("mods-row");
                var thumb = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-thumb");
                ModCatalog.Image(e.thumb, tex => { if (tex != null) thumb.style.backgroundImage = new StyleBackground(tex); });
                var text = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-row-text");
                Add(text, new Label(e.Name) { pickingMode = PickingMode.Ignore }, "mods-row-name");
                Add(text, new Label(OnlineSub(e)) { pickingMode = PickingMode.Ignore }, "mods-row-sub");
                Add(text, new Label(Numbers(e)) { pickingMode = PickingMode.Ignore }, "mods-row-sub", "mods-row-stats");
                Add(row, new Label { pickingMode = PickingMode.Ignore }, "mods-badge");
                // As the Installed tab: keyboard / controller focus selects, the mouse's hover focus doesn't.
                int hoverFrame = -10;
                row.RegisterCallback<PointerEnterEvent>(_ => hoverFrame = Time.frameCount);
                hookFocus(row);
                row.RegisterCallback<FocusInEvent>(_ => { if (Time.frameCount - hoverFrame > 1) SelectOnline(entry.id); });
                list.Add(row);
                onlineRows[e.id] = row;
            }
            string q = search.value?.Trim() ?? "";
            empty.text = ModCatalog.Status switch
            {
                ModCatalog.State.Loading or ModCatalog.State.None => T("modsCatalogLoading", "Loading the mod catalog…"),
                ModCatalog.State.Failed => string.Format(T("modsCatalogFailed", "The mod catalog couldn't be loaded ({0}). Check your connection, then press Refresh."), ModCatalog.Error),
                _ => ModCatalog.Mods.Count == 0 ? T("modsCatalogEmpty", "No mods in the catalog yet. Share yours: press Share your mod.")
                   : string.Format(T("modsCatalogNoMatch", "No mods match \"{0}\"."), q),
            };
            empty.style.display = mods.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (onlineSelected == null || !onlineRows.ContainsKey(onlineSelected)) onlineSelected = mods.FirstOrDefault()?.id;
            Refresh();
            if (onlineSelected != null && onlineRows.TryGetValue(onlineSelected, out var picked))
                list.schedule.Execute(() => { if (list.contentContainer.Contains(picked)) list.ScrollTo(picked); });
        }

        static string OnlineSub(ModCatalog.Entry e)
        {
            var parts = new List<string> { "v" + e.version };
            if (!string.IsNullOrEmpty(e.author)) parts.Add(e.author);
            if (e.scripts) parts.Add(T("modsScripts", "Scripts"));
            return string.Join("  ·  ", parts);
        }

        /// <summary>"1.2k views  ·  345 downloads".</summary>
        static string Numbers(ModCatalog.Entry e) =>
            string.Format(e.views == 1 ? T("modsView", "{0} view") : T("modsViews", "{0} views"), ModCatalog.Count(e.views)) + "  ·  "
            + string.Format(e.downloads == 1 ? T("modsDownload", "{0} download") : T("modsDownloads", "{0} downloads"), ModCatalog.Count(e.downloads));

        void SelectOnline(string id)
        {
            if (onlineSelected == id) return;
            onlineSelected = id;
            Refresh();
        }

        // ---- the details ----

        void RefreshOnline()
        {
            count.text = ModCatalog.Status == ModCatalog.State.Ready
                ? string.Format(T("modsOnlineCount", "{0} mods in the catalog"), ModCatalog.Mods.Count) : "";
            note.text = T("modsOnlineNote", "Mods shared by players (github.com/JoppieToppie/gof2r-mods). They install turned off: turn them on before starting or loading a game.");
            var downloading = ModCatalog.Busy ? ModCatalog.Current.entry.id : null;
            foreach (var pair in onlineRows)
            {
                var e = ModCatalog.Find(pair.Key);
                if (e == null) continue;
                var row = pair.Value;
                var badge = row.Q<Label>(className: "mods-badge");
                var state = ModCatalog.StateOf(e);
                bool on = state != ModCatalog.Installed.No && ModManager.IsEnabled(e.id);
                badge.text = pair.Key == downloading ? Mathf.RoundToInt(ModCatalog.Current.Progress * 100) + "%"
                    : state == ModCatalog.Installed.Older ? T("modsBadgeUpdate", "Update").UiCaps()
                    : state != ModCatalog.Installed.No ? (on ? T("on", "On") : T("modsBadgeInstalled", "Installed")).UiCaps() : "";
                badge.style.display = badge.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                badge.EnableInClassList("mods-badge--on", on && state != ModCatalog.Installed.Older);
                badge.EnableInClassList("mods-badge--update", state == ModCatalog.Installed.Older);
                row.EnableInClassList("mods-row--selected", pair.Key == onlineSelected);
                row.EnableInClassList("mods-row--off", false);
            }

            var mod = onlineSelected != null ? ModCatalog.Find(onlineSelected) : null;
            detail.style.visibility = mod != null && onlineRows.ContainsKey(mod.id) ? Visibility.Visible : Visibility.Hidden;
            if (mod == null) return;
            ShowPreview(mod);
            name.text = mod.Name;
            var metaParts = new List<string> { "v" + mod.version };
            if (!string.IsNullOrEmpty(mod.author)) metaParts.Add(string.Format(T("modsBy", "by {0}"), mod.author));
            metaParts.Add(mod.id);
            if (!string.IsNullOrEmpty(mod.website)) metaParts.Add(mod.website);
            var second = new List<string> { Numbers(mod), ModCatalog.Size(mod.size) };
            if (!string.IsNullOrEmpty(mod.updated)) second.Add(string.Format(T("modsUpdatedOn", "updated {0}"), mod.updated));
            meta.text = string.Join("  ·  ", metaParts) + "\n" + string.Join("  ·  ", second);
            description.text = mod.Description;
            if (!string.IsNullOrEmpty(mod.Credits))
                description.text += "\n\n" + T("modsCredits", "Credits").UiCaps() + "\n" + mod.Credits;

            var state0 = ModCatalog.StateOf(mod);
            var installed = ModManager.Find(mod.id);
            bool isOn = installed != null && ModManager.IsEnabled(mod.id);
            var lines = new List<string>();
            if (failures.TryGetValue(mod.id, out string failed))
                lines.Add("× " + string.Format(T("modsDownloadFailed", "The download failed: {0}"), failed));
            switch (state0)
            {
                case ModCatalog.Installed.Same:
                    lines.Add("✓ " + (justInstalled == mod.id && !isOn ? T("modsJustInstalled", "Installed. Turn it on to use it.")
                                      : isOn ? T("modsInstalledOn", "Installed and on.") : T("modsInstalledOff", "Installed, turned off.")));
                    break;
                case ModCatalog.Installed.Older:
                    lines.Add("↑ " + string.Format(T("modsInstalledOlder", "v{0} is installed: this is a newer version."), installed.Version));
                    break;
                case ModCatalog.Installed.Newer:
                    lines.Add("✓ " + string.Format(T("modsInstalledNewer", "v{0} is installed, newer than the catalog's."), installed.Version));
                    break;
            }
            if (mod.dependencies.Count > 0)
                lines.Add(string.Format(T("modsNeeds", "Needs: {0}"), string.Join(", ", mod.dependencies.Select(d =>
                {
                    var dm = ModManager.Find(d);
                    var dc = ModCatalog.Find(d);
                    string label = (dm?.Name ?? dc?.Name ?? d) + (mod.dependencyVersions.TryGetValue(d, out string v) ? " " + v + "+" : "");
                    bool tooOld = dm != null && v != null && !ModManifest.VersionAtLeast(dm.Version, v);
                    string where = dm != null && !tooOld ? T("modsDepInstalled", "installed")
                                 : tooOld ? string.Format(T("modsDepOld", "v{0} installed"), dm.Version)
                                 : dc != null ? T("modsDepInCatalog", "installed with it") : T("modsDepMissing", "not installed");
                    return $"{label} ({where})";
                }))));
            if (mod.scripts)
                lines.Add("! " + T("modsOnlineScripts", "Runs scripts: they can change anything in the game, your saves included. Only use script mods from authors you trust."));
            problems.text = string.Join("\n", lines);
            problems.style.display = lines.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            bool busy = ModCatalog.Busy, thisOne = busy && ModCatalog.Current.entry.id == mod.id || installQueue.Any(q => q.id == mod.id);
            installButton.style.display = thisOne ? DisplayStyle.None : DisplayStyle.Flex;
            installButton.text = (state0 == ModCatalog.Installed.No ? T("modsInstall", "Install")
                : state0 == ModCatalog.Installed.Older ? string.Format(T("modsUpdateTo", "Update to v{0}"), mod.version)
                : T("modsBadgeInstalled", "Installed")).UiCaps();
            installButton.SetEnabled(!busy && (state0 == ModCatalog.Installed.No || state0 == ModCatalog.Installed.Older));
            onlineToggle.style.display = installed != null && !thisOne ? DisplayStyle.Flex : DisplayStyle.None;
            onlineToggle.text = (isOn ? T("modsTurnOff", "Turn off") : T("modsTurnOn", "Turn on")).UiCaps();
            onlineToggle.SetEnabled(installed != null && (!installed.Broken || isOn));
            cancelButton.style.display = busy ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The selected mod's preview: its thumbnail at once when loaded, the big image when it comes (that fetch is
        /// the mod's view; kept for the run, so selecting it again counts none).</summary>
        void ShowPreview(ModCatalog.Entry mod)
        {
            preview.EnableInClassList("mods-preview--none", false);
            string url = mod.preview ?? mod.thumb;
            if (ModCatalog.HasImage(url)) { ModCatalog.Image(url, tex => preview.style.backgroundImage = new StyleBackground(tex)); return; }
            preview.style.backgroundImage = new StyleBackground();
            if (ModCatalog.HasImage(mod.thumb)) ModCatalog.Image(mod.thumb, tex => preview.style.backgroundImage = new StyleBackground(tex));
            string id = mod.id;
            ModCatalog.Image(url, tex =>
            {
                if (tex != null && online && onlineSelected == id) preview.style.backgroundImage = new StyleBackground(tex);
            });
        }

        // ---- installing ----

        void InstallSelected()
        {
            var e = onlineSelected != null ? ModCatalog.Find(onlineSelected) : null;
            if (e != null) Install(e);
        }

        /// <summary>The Installed tab's Update to vX: the catalog's newer version of the selected mod.</summary>
        void UpdateSelected()
        {
            var e = ModCatalog.UpdateFor(selected != null ? ModManager.Find(selected) : null);
            if (e != null) Install(e);
        }

        /// <summary>Downloads and installs 'e' (not when it is installed in this or a newer version); the dependencies it
        /// lacks that the catalog has are asked about first and installed before it.</summary>
        void Install(ModCatalog.Entry e)
        {
            if (ModCatalog.Busy) return;
            var state = ModCatalog.StateOf(e);
            if (state == ModCatalog.Installed.Same || state == ModCatalog.Installed.Newer) return;
            var needs = ModCatalog.Needed(e, out _);
            void Go(bool withNeeds)
            {
                installQueue.Clear();
                if (withNeeds) foreach (var n in needs) installQueue.Enqueue(n);
                installQueue.Enqueue(e);
                NextInstall();
            }
            if (needs.Count == 0) { Go(false); return; }
            ask(T("modsInstallTitle", "Install mod"),
                string.Format(T("modsInstallNeeds", "\"{0}\" needs {1}. Install them too?"), e.Name, string.Join(", ", needs.Select(n => "\"" + n.Name + "\""))),
                () => Go(true));
        }

        void NextInstall()
        {
            if (installQueue.Count == 0) return;
            var e = installQueue.Dequeue();
            failures.Remove(e.id);
            var d = ModCatalog.Start(e);
            if (d == null) { installQueue.Clear(); Refresh(); return; }
            Refresh();
            bool finished = false;
            Panel.schedule.Execute(() =>
            {
                if (!d.done) { RefreshProgress(); if (online) RefreshBadges(); return; }
                finished = true;
                Downloaded(d);
            }).Every(150).Until(() => finished);
        }

        /// <summary>Only the downloading row's percentage (a full Refresh every 150 ms would redo the details).</summary>
        void RefreshBadges()
        {
            var d = ModCatalog.Current;
            if (d == null || !onlineRows.TryGetValue(d.entry.id, out var row)) return;
            var badge = row.Q<Label>(className: "mods-badge");
            badge.text = Mathf.RoundToInt(d.Progress * 100) + "%";
            badge.style.display = DisplayStyle.Flex;
        }

        /// <summary>The line above the buttons: what is downloading and how far.</summary>
        void RefreshProgress()
        {
            var d = ModCatalog.Current;
            bool show = d != null && !d.done;
            progress.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            progress.text = d.verifying
                ? string.Format(T("modsChecking", "Checking \"{0}\"…"), d.entry.Name)
                : string.Format(T("modsDownloading", "Downloading \"{0}\": {1}% ({2} of {3})"), d.entry.Name,
                                Mathf.RoundToInt(d.Progress * 100), ModCatalog.Size(d.Received), ModCatalog.Size(d.entry.size));
        }

        void CancelDownload()
        {
            installQueue.Clear();
            ModCatalog.Cancel();
        }

        /// <summary>A download ended: checked as a mod (the catalog's id), installed, the next in the queue started.</summary>
        void Downloaded(ModCatalog.Download d)
        {
            var e = d.entry;
            string error = null;
            if (d.cancelled) installQueue.Clear();
            else if (d.error != null) error = d.error;
            else
            {
                var manifest = ModImport.Inspect(d.path, out string why);
                if (manifest == null || manifest.id != e.id)
                {
                    ModImport.Discard(d.path);
                    error = manifest == null ? why : $"the zip holds \"{manifest.id}\", not \"{e.id}\"";
                }
                else if (ModImport.Install(d.path, e.id + ".zip", manifest, out why) == null) error = why;
                else justInstalled = e.id;
            }
            if (error != null)
            {
                failures[e.id] = error;
                installQueue.Clear();
                Debug.LogWarning($"Mods: \"{e.id}\" from the catalog: {error}");
            }
            if (installQueue.Count > 0) NextInstall();
            // The Online tab keeps its rows (and the focus); the Installed tab's list changed.
            if (online) Refresh();
            else Rebuild();
        }
    }
}
