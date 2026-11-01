// ModBuilderProjects.cs
// Remake-only: the main menu's Mod Builder page (Mods > Mod Builder): the player's mod projects (Modding.ModProject,
// Application.persistentDataPath/ModProjects). On the left the projects (the newest first) under a "new project" row
// (a name and Create); on the right the selected project: its name, author, version and description (saved as they are
// typed), what it holds by kind of content (each opens the builder on that kind, where it is edited in the game: the hangar,
// the Space Lounge, the star map or space), the mods it depends on,
// and Open in hangar, Export as mod (zips it into the Mods folder, asks before replacing an installed mod with its id),
// Open folder (desktop) and Delete (asks).
// Import mod (players' request: edit a downloaded mod): the list shows the installed mods instead (and From a file...
// for a downloaded .zip); a pick makes a project of the mod (ModProject.CopyMod on a worker thread: its files as they
// are, its own id kept, so Export replaces the installed copy), asking first when a project of it exists. The Mod
// Browser's Edit in Mod Builder starts the same import (ImportInstalled).
// Plain C#: MainMenu creates it in its panel host and hands it its sound, focus, dialog hooks and the opening of a project
// (Modding.ModBuilder.Begin, then the Station scene behind the menu's fade).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ModBuilderProjects
    {
        public VisualElement Panel { get; }

        readonly Action<VisualElement> hookFocus;
        readonly Action click;
        readonly Action<ModProject> open;
        readonly Action<string, string, Action> ask;
        readonly Action<string, string> notice;
        readonly ScrollView list;
        readonly Label count, empty, note, title, meta, depsLine;
        readonly VisualElement detail, content;
        readonly TextField newName, nameField, authorField, versionField, descField;
        readonly Label idHint;
        readonly Button openButton, exportButton, deleteButton, createButton, importButton;
        bool importing, importBusy;   // the list shows the mods to import / a copy is running
        List<ModProject> projects = new List<ModProject>();
        ModProject selected;
        bool filling;

        static string T(string key, string english) => Localization.Extra(key, english);
        static string Count(int n, string oneKey, string one, string manyKey, string many) => n == 1 ? T(oneKey, one) : string.Format(T(manyKey, many), n);

        /// <summary>The kinds of content a mod can hold, in the order the page shows them, with the builder's tab.</summary>
        static readonly (string key, string english, string desc, ModBuilder.Kind kind)[] Kinds =
        {
            ("mbShips", "Ships", "New ships from a .glb model or a game model, or changes to the game's ships: stats, hardpoints, test flights. In the hangar.", ModBuilder.Kind.Ships),
            ("mbItems", "Items", "New weapons and equipment, or changes to the game's items. In the hangar.", ModBuilder.Kind.Items),
            ("mbFx", "Weapon fx", "Projectiles, muzzle flashes, impacts and shot sounds, on a firing range in space.", ModBuilder.Kind.WeaponFx),
            ("mbSkins", "Skins", "New textures for the game's ships: hull paint, lights and engine glow, for one ship or all that share them.", ModBuilder.Kind.Skins),
            ("mbKits", "Ship kits", "Customizable ships: parts (.glb) the players combine in the hangar's Customize screen, with colours and stats. In the hangar.", ModBuilder.Kind.Kits),
            ("mbBlueprints", "Blueprints", "Ships and items built from ingredients, and where they are found. In the hangar.", ModBuilder.Kind.Blueprints),
            ("mbCharacters", "Characters", "Portraits and names for conversations: your own characters, or new looks for the story's. In the Space Lounge.", ModBuilder.Kind.Characters),
            ("mbQuests", "Missions", "Story missions and bar missions as steps: conversations, fights, rewards. In the Space Lounge.", ModBuilder.Kind.Quests),
            ("mbAudio", "Audio", "Replace or add music, sound effects and voice lines. In the Space Lounge.", ModBuilder.Kind.Audio),
            ("mbSystems", "Systems", "New star systems and their routes, placed on the star map.", ModBuilder.Kind.Systems),
            ("mbModRaces", "Races", "Races of your own beside the Terrans, Vossk, Nivelians and Midorians: who owns systems, flies ships and raids, and who fights whom. On the star map.", ModBuilder.Kind.Races),
            ("mbStations", "Stations", "New stations and changes to the game's: models, rooms, planets. In their own orbit.", ModBuilder.Kind.Stations),
            ("mbNpcShips", "NPC ships", "Capital ships, freighters and bosses nobody flies, new or the game's changed: turrets, escorts, loot, where they come, a dock with a shop. In space.", ModBuilder.Kind.NpcShips),
            ("mbInteriors", "Hangars & bars", "Hangar and Space Lounge rooms of your own, with their markers. In the room itself.", ModBuilder.Kind.Interiors),
            ("mbCampaigns", "Campaign", "A new game of your own: start, galaxy, ships; new-game options.", ModBuilder.Kind.Campaigns),
        };

        public ModBuilderProjects(VisualElement host, Action back, Action<VisualElement> hookFocus, Action click, Action<ModProject> open,
                                  Action<string, string, Action> ask, Action<string, string> notice)
        {
            this.hookFocus = hookFocus;
            this.click = click;
            this.open = open;
            this.ask = ask;
            this.notice = notice;
            Panel = new VisualElement { name = ModBuilder.ProjectsPanel };
            Panel.AddToClassList("panel");
            Panel.AddToClassList("mods-panel");
            Panel.usageHints = UsageHints.DynamicTransform;

            var titleRow = Add(Panel, new VisualElement(), "mods-title-row");
            Add(titleRow, new Label(T("mbTitle", "Mod Builder").ToUpperInvariant()), "panel-title", "gof-semibold");
            Add(titleRow, new Label(T("mpExperimental", "Experimental").ToUpperInvariant()), "mp-badge");
            Add(titleRow, new VisualElement(), "mp-title-spacer");
            count = Add(titleRow, new Label(), "mods-count");
            Add(Panel, new VisualElement(), "panel-accent");

            var body = Add(Panel, new VisualElement(), "mods-body");
            var left = Add(body, new VisualElement(), "mods-left");
            var newRow = Add(left, new VisualElement(), "mb-new-row");
            newName = Add(newRow, new TextField { maxLength = 40 }, "mb-field", "mb-new-name");
            // One field for both: what is typed filters the list (every word must match the name, id, author or
            // description), and Create / Enter makes a new project of that name.
            // Modified: the placeholder is a label of its own that slides when longer than the field (TextMarquee), as the
            // options' drop-downs: its length varies with the language and the field's own placeholder was cut off.
            var hintClip = Add(newName, new VisualElement { pickingMode = PickingMode.Ignore }, "mb-placeholder-clip");
            var hint = Add(hintClip, new Label(T("mbNewPlaceholder", "Search, or name a new project")) { pickingMode = PickingMode.Ignore }, "mb-placeholder");
            TextMarquee.Attach(hintClip, hint, centre: false);
            newName.RegisterValueChangedCallback(e => hintClip.style.display = string.IsNullOrEmpty(e.newValue) ? DisplayStyle.Flex : DisplayStyle.None);
            newName.RegisterValueChangedCallback(_ => RebuildRows());
            newName.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                e.StopPropagation();
                if (!importing) Create();   // while the import list shows the field only searches it
            }, TrickleDown.TrickleDown);
            createButton = ActionButton(newRow, T("mbCreate", "Create"), Create, "mb-create");
            importButton = ActionButton(newRow, T("modsImport", "Import mod"), ToggleImport, "mb-create");
            list = Add(left, new ScrollView(ScrollViewMode.Vertical), "mods-list");
            list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            new DragScroll(list);
            empty = Add(left, new Label(), "mods-empty", "mb-empty");

            detail = Add(body, new VisualElement(), "mods-detail");
            var detailScroll = Add(detail, new ScrollView(ScrollViewMode.Vertical), "mb-detail-scroll");
            detailScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            detailScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            title = Add(detailScroll, new Label(), "mods-name", "gof-semibold");
            meta = Add(detailScroll, new Label(), "mods-meta");
            var fields = Add(detailScroll, new VisualElement(), "mb-fields");
            nameField = Field(fields, T("mbName", "Name"), 60, v => { selected.Name = v.Trim().Length > 0 ? v.Trim() : selected.Id; title.text = selected.Name; });
            idHint = Add(nameField.parent, new Label(), "mb-id-hint");
            nameField.RegisterValueChangedCallback(_ => UpdateIdHint());
            // Leaving the name (or Enter) also moves an id that doesn't match it yet (a project renamed before the id
            // followed the name) without the name being edited.
            nameField.RegisterCallback<FocusOutEvent>(_ => { SaveIfDirty(); FollowName(); });
            nameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
                SaveIfDirty();
                FollowName();
            });
            var pair = Add(fields, new VisualElement(), "mb-field-pair");
            authorField = Field(pair, T("mbAuthor", "Author"), 40, v => selected.Author = v.Trim());
            authorField.parent.AddToClassList("mb-field-box--wide");   // USS has no :first-child
            versionField = Field(pair, T("mbVersion", "Version"), 16, v => selected.Version = v.Trim().Length > 0 ? v.Trim() : "1.0");
            descField = Field(fields, T("mbDescription", "Description"), 600, v => selected.Description = v, true);
            Add(detailScroll, new Label(T("mbContent", "Content").ToUpperInvariant()), "mb-section", "gof-semibold");
            content = Add(detailScroll, new VisualElement(), "mb-kinds");
            depsLine = Add(detailScroll, new Label(), "mods-note");

            var actions = Add(detail, new VisualElement(), "mods-actions", "mb-actions");
            openButton = ActionButton(actions, T("mbOpenProject", "Open"), () => { if (selected != null) { selected.Save(); open(selected); } }, "mods-toggle");
            exportButton = ActionButton(actions, T("mbExport", "Export as mod"), Export);
            if (!Application.isMobilePlatform)
                ActionButton(actions, T("mbFolder", "Open folder"), () => { if (selected != null) Application.OpenURL("file:///" + selected.Folder.Replace('\\', '/')); });
            deleteButton = ActionButton(actions, T("modsDelete", "Delete"), AskDelete, "mods-delete");

            var footer = Add(Panel, new VisualElement(), "mods-footer");
            note = Add(footer, new Label(), "mods-note");
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

        /// <summary>A labelled text field whose edits go straight into the selected project (saved when the page is left).</summary>
        TextField Field(VisualElement parent, string label, int max, Action<string> set, bool multiline = false)
        {
            var box = Add(parent, new VisualElement(), "mb-field-box");
            if (parent.childCount == 1) box.AddToClassList("mb-field-box--first");   // USS has no :first-child
            Add(box, new Label(label.ToUpperInvariant()), "mb-field-label");
            var f = Add(box, new TextField { maxLength = max, multiline = multiline }, "mb-field");
            if (multiline) f.AddToClassList("mb-field--multi");
            f.RegisterValueChangedCallback(e => { if (!filling && selected != null) { set(e.newValue); dirty = true; } });
            f.RegisterCallback<FocusOutEvent>(_ => SaveIfDirty());
            return f;
        }

        /// <summary>The line under the name (like GitHub's "will be created as"): the mod id the name gives, in grey, and
        /// the id it had while that differs; the project takes it when the name is saved (FollowName).</summary>
        void UpdateIdHint()
        {
            if (selected == null) { idHint.text = ""; return; }
            string id = selected.IdFromName(nameField.value);
            idHint.text = id == selected.Id
                ? string.Format(T("mbIdHint", "Mod id: {0}"), id)
                : string.Format(T("mbIdHintChange", "Mod id: {0}  (was {1})"), id, selected.Id);
        }

        /// <summary>The name was saved: the mod id follows it (ModProject.ChangeId: mod.json, the project's own "id:" keys,
        /// its folder; the next export replaces the mod installed under the old id).</summary>
        void FollowName()
        {
            if (selected == null || selected.LoadError != null) return;
            string id = selected.IdFromName(selected.Name);
            if (id == selected.Id) return;
            var p = selected;
            var renamed = p.ChangeId(id, out string error);
            if (renamed == null) { notice(T("mbTitle", "Mod Builder"), string.Format(T("mbIdFailed", "The mod id couldn't follow the name: {0}"), error)); return; }
            int at = projects.IndexOf(p);
            if (at >= 0) projects[at] = renamed;
            selected = renamed;
            RebuildRows();
            Refresh();
        }

        bool dirty;

        void SaveIfDirty()
        {
            if (!dirty || selected == null) return;
            dirty = false;
            selected.Save();
            FollowName();
            RebuildRows();
        }

        /// <summary>Opening the page: the projects read again.</summary>
        public void Open()
        {
            string keep = selected?.Folder;
            projects = ModProject.List();
            selected = projects.FirstOrDefault(p => p.Folder == keep) ?? projects.FirstOrDefault();
            RebuildRows();
            Refresh();
        }

        /// <summary>Leaving the page: what was typed is kept.</summary>
        public void Close() => SaveIfDirty();

        void RebuildRows()
        {
            if (importing) { RebuildImportRows(); return; }
            list.Clear();
            var shown = projects.Where(Matches).ToList();
            foreach (var p in shown)
            {
                var row = new Button(() => { click(); Select(p); }) { userData = p };
                row.AddToClassList("mods-row");
                var thumb = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-thumb", "mb-thumb");
                var icon = ProjectIcon(p);
                if (icon != null) thumb.style.backgroundImage = new StyleBackground(icon);
                var text = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-row-text");
                Add(text, new Label(p.Name) { pickingMode = PickingMode.Ignore }, "mods-row-name");
                Add(text, new Label(Sub(p)) { pickingMode = PickingMode.Ignore }, "mods-row-sub");
                row.EnableInClassList("mods-row--selected", p == selected);
                int hoverFrame = -10;
                row.RegisterCallback<PointerEnterEvent>(_ => hoverFrame = Time.frameCount);
                hookFocus(row);
                row.RegisterCallback<FocusInEvent>(_ => { if (Time.frameCount - hoverFrame > 1) Select(p); });
                list.Add(row);
            }
            empty.text = projects.Count == 0
                ? T("mbEmpty", "No projects yet. Type a name above and press Create.")
                : T("mbNoMatch", "No project matches. Press Create to start one with this name.");
            empty.style.display = shown.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            count.text = projects.Count == 1 ? T("mbCountOne", "1 project") : string.Format(T("mbCount", "{0} projects"), projects.Count);
            note.text = string.Format(T("mbNote", "Projects are in {0}. Export as mod puts one into your Mods folder."),
                                      ModProject.Root);
        }

        /// <summary>The search field's filter: every word typed is in the project's name, id, author or description.</summary>
        bool Matches(ModProject p)
        {
            string q = newName?.value?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            string hay = $"{p.Name} {p.Id} {p.Author} {p.Description}";
            foreach (var w in q.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                if (hay.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        // Icons read from the projects' folders, by path and change time (the list is rebuilt on every visit).
        static readonly Dictionary<string, Texture2D> fileIcons = new Dictionary<string, Texture2D>();

        /// <summary>The row's picture: the first ship's shop icon (its own made by the builder, an original's for an
        /// override), else the first re-skinned ship's, else the first item's (its own icon or its original's).</summary>
        static Texture2D ProjectIcon(ModProject p)
        {
            foreach (var s in p.Ships)
            {
                var own = FileIcon(p, s.Icon);
                if (own != null) return own;
                if (s.IsOverride && s.OverrideIndex >= 0) return ItemInfo.ShipIcon(s.OverrideIndex);
            }
            var skins = p.SkinShips();
            if (skins.Count > 0) return ItemInfo.ShipIcon(skins[0]);
            foreach (var i in p.Items)
            {
                var own = FileIcon(p, i.Icon);
                if (own != null) return own;
                if (i.Original >= 0) return ItemInfo.ItemIcon(i.Original);
            }
            return null;
        }

        static Texture2D FileIcon(ModProject p, string rel)
        {
            if (string.IsNullOrEmpty(rel)) return null;
            string path = System.IO.Path.Combine(p.Folder, rel);
            if (!System.IO.File.Exists(path)) return null;
            string key = path + "|" + System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            if (fileIcons.TryGetValue(key, out var t) && t != null) return t;
            try { t = ImageImport.Decode(System.IO.File.ReadAllBytes(path), rel, false, false, out _); }
            catch { t = null; }
            if (t != null) t.wrapMode = TextureWrapMode.Clamp;
            fileIcons[key] = t;
            return t;
        }

        /// <summary>What the project holds, kind by kind ("2 ships · 1 skin"), or that it is still empty.</summary>
        static string Contents(ModProject p)
        {
            var parts = new List<string>();
            void Part(int n, string oneKey, string one, string manyKey, string many) { if (n > 0) parts.Add(Count(n, oneKey, one, manyKey, many)); }
            Part(p.Ships.Count, "mbOneShip", "1 ship", "mbShipsN", "{0} ships");
            Part(p.Items.Count, "mbOneItem", "1 item", "mbItemsN", "{0} items");
            Part(p.Characters.Count, "mbOneCharacter", "1 character", "mbCharactersN", "{0} characters");
            Part(p.SkinShips().Count, "mbOneSkin", "1 skin", "mbSkinsN", "{0} skins");
            Part(p.Blueprints.Count, "mbOneBlueprint", "1 blueprint", "mbBlueprintsN", "{0} blueprints");
            Part(p.Quests.Count, "mbOneQuest", "1 mission", "mbQuestsN", "{0} missions");
            Part(AudioCount(p), "mbOneAudio", "1 sound", "mbAudioN", "{0} sounds");
            Part(p.Systems.Count, "mbOneSystem", "1 system", "mbSystemsN", "{0} systems");
            Part(p.Races.Count, "mbOneModRace", "1 race", "mbModRacesN", "{0} races");
            Part(p.Stations.Count, "mbOneStation", "1 station", "mbStationsN", "{0} stations");
            Part(p.Interiors.Count, "mbOneRoom", "1 room", "mbRoomsN", "{0} rooms");
            if (p.Campaign != null) parts.Add(T("mbHasCampaign", "a campaign"));
            return parts.Count > 0 ? string.Join("  ·  ", parts) : T("mbProjectEmpty", "Empty");
        }

        static int AudioCount(ModProject p) =>
            p.AudioFiles(ModProject.MusicFolder).Count + p.AudioFiles(ModProject.SoundsFolder).Count + p.AudioFiles(ModProject.VoicesFolder).Count
            + p.AudioFiles(ModProject.VoicesFolder + "/de").Count + p.AudioFiles(ModProject.VoicesFolder + "/en").Count;

        /// <summary>The weapons with a look of their own (items.json "fx") and the weapon effects (weaponfx.json).</summary>
        static int FxCount(ModProject p) => p.Items.Count(i => i.Get("fx") != null) + p.WeaponEffects.Count;

        static bool HasContent(ModProject p) => p.Ships.Count + p.Items.Count + p.WeaponEffects.Count + p.Characters.Count + p.SkinShips().Count + p.Blueprints.Count + p.Quests.Count
                                               + p.Systems.Count + p.Stations.Count + p.Interiors.Count + p.GameOptions.Count + AudioCount(p) > 0 || p.Campaign != null
                                               || p.EventGraphs().Count > 0;

        static string Sub(ModProject p) => $"{Contents(p)}  ·  v{p.Version}  ·  {p.Modified.ToLocalTime():yyyy-MM-dd HH:mm}";

        void Select(ModProject p)
        {
            if (selected == p) return;
            SaveIfDirty();
            selected = p;
            foreach (var row in list.Children())
                row.EnableInClassList("mods-row--selected", row.userData == p);
            Refresh();
        }

        void Refresh()
        {
            detail.style.visibility = selected != null ? Visibility.Visible : Visibility.Hidden;
            if (selected == null) return;
            filling = true;
            title.text = selected.Name;
            meta.text = selected.LoadError != null ? "× " + selected.LoadError : "";
            meta.style.display = selected.LoadError != null ? DisplayStyle.Flex : DisplayStyle.None;
            nameField.value = selected.Name;
            UpdateIdHint();
            authorField.value = selected.Author;
            versionField.value = selected.Version;
            descField.value = selected.Description;
            filling = false;
            content.Clear();
            for (int i = 0; i < Kinds.Length; i++)
            {
                var (key, english, desc, kind) = Kinds[i];
                var tile = new Button(() => { if (selected != null) { click(); selected.Save(); ModBuilder.OpenKind = kind; open(selected); } });
                tile.AddToClassList("mb-kind");
                Add(tile, new Label(T(key, english).ToUpperInvariant()) { pickingMode = PickingMode.Ignore }, "mb-kind-title", "gof-semibold");
                string state = kind switch
                {
                    ModBuilder.Kind.Ships => Count(selected.Ships.Count, "mbOneShip", "1 ship", "mbShipsN", "{0} ships"),
                    ModBuilder.Kind.Items => Count(selected.Items.Count, "mbOneItem", "1 item", "mbItemsN", "{0} items"),
                    ModBuilder.Kind.WeaponFx => Count(FxCount(selected), "mbOneFx", "1 look", "mbFxN", "{0} looks"),
                    ModBuilder.Kind.Characters => Count(selected.Characters.Count, "mbOneCharacter", "1 character", "mbCharactersN", "{0} characters"),
                    ModBuilder.Kind.Skins => Count(selected.SkinShips().Count, "mbOneSkin", "1 skin", "mbSkinsN", "{0} skins"),
                    ModBuilder.Kind.Blueprints => Count(selected.Blueprints.Count, "mbOneBlueprint", "1 blueprint", "mbBlueprintsN", "{0} blueprints"),
                    ModBuilder.Kind.Kits => Count(selected.Kits.Count, "mbOneKit", "1 kit", "mbKitsN", "{0} kits"),
                    ModBuilder.Kind.Quests => Count(selected.Quests.Count + selected.EventGraphs().Count, "mbOneQuest", "1 mission", "mbQuestsN", "{0} missions"),
                    ModBuilder.Kind.Audio => Count(AudioCount(selected), "mbOneAudio", "1 sound", "mbAudioN", "{0} sounds"),
                    ModBuilder.Kind.Systems => Count(selected.Systems.Count, "mbOneSystem", "1 system", "mbSystemsN", "{0} systems"),
                    ModBuilder.Kind.Races => Count(selected.Races.Count, "mbOneModRace", "1 race", "mbModRacesN", "{0} races"),
                    ModBuilder.Kind.Stations => Count(selected.Stations.Count, "mbOneStation", "1 station", "mbStationsN", "{0} stations"),
                    ModBuilder.Kind.NpcShips => Count(selected.NpcShips.Count, "mbOneNpcShip", "1 NPC ship", "mbNpcShipsN", "{0} NPC ships"),
                    ModBuilder.Kind.Interiors => Count(selected.Interiors.Count, "mbOneRoom", "1 room", "mbRoomsN", "{0} rooms"),
                    ModBuilder.Kind.Campaigns => selected.Campaign != null ? T("mbYes", "yes") + (selected.GameOptions.Count > 0 ? " · " + Count(selected.GameOptions.Count, "mbOneOption", "1 option", "mbOptionsN", "{0} options") : "")
                                               : selected.GameOptions.Count > 0 ? Count(selected.GameOptions.Count, "mbOneOption", "1 option", "mbOptionsN", "{0} options") : T("mbNone", "none"),
                    _ => "",
                };
                Add(tile, new Label(state.ToUpperInvariant()) { pickingMode = PickingMode.Ignore }, "mb-kind-state");
                tile.tooltip = T(key + "Desc", desc);
                hookFocus(tile);
                content.Add(tile);
            }
            // The mods it uses (picked anywhere in the builder: another mod's ship, item, station or character).
            var deps = selected.Dependencies;
            depsLine.text = deps.Count == 0 ? T("mbNoDeps", "Uses no other mods.")
                          : string.Format(T("mbDepsLine", "Uses the mods: {0}"), string.Join(", ", deps.Select(d => ModManager.Find(d)?.Name ?? d + " (" + T("mbDepMissing", "not installed") + ")")));
            openButton.SetEnabled(selected.LoadError == null);
            deleteButton.SetEnabled(true);
            exportButton.SetEnabled(selected.LoadError == null);
        }

        void Create()
        {
            string name = newName.value?.Trim();
            if (string.IsNullOrEmpty(name)) { newName.Focus(); return; }
            ModProject p;
            try { p = ModProject.Create(name, GoF2Remake.Multiplayer.NetGame.PlayerName); }
            catch (Exception e) { notice(T("mbTitle", "Mod Builder"), string.Format(T("mbCreateFailed", "The project couldn't be created: {0}"), e.Message)); return; }
            newName.value = "";
            projects.Insert(0, p);
            selected = p;
            RebuildRows();
            Refresh();
        }

        // ---- import ------------------------------------------------------------------------------------------

        /// <summary>Import mod: the list shows the mods to make a project of instead of the projects, the button reads
        /// Cancel; again (or Esc / Back, CancelImport) the projects come back.</summary>
        void ToggleImport()
        {
            if (importBusy) return;
            importing = !importing;
            importButton.text = (importing ? T("kbCancel", "Cancel") : T("modsImport", "Import mod")).ToUpperInvariant();
            createButton.SetEnabled(!importing);
            RebuildRows();
        }

        /// <summary>Back while the import list shows: the projects again (true); false = nothing to undo here.</summary>
        public bool CancelImport()
        {
            if (!importing || importBusy) return false;
            ToggleImport();
            return true;
        }

        /// <summary>The import list: From a file... (where there is a file picker), then every installed mod the search
        /// field matches, marked when a project of it exists.</summary>
        void RebuildImportRows()
        {
            list.Clear();
            if (ModImport.Available)
                ImportRow(null, T("mbImportFile", "From a file..."), T("mbImportFileSub", "A mod you downloaded (.zip)"), PickFile);
            int mods = 0;
            foreach (var m in ModManager.Installed)
            {
                if (m.Manifest == null || !MatchesMod(m)) continue;
                var mod = m;
                string sub = "v" + m.Manifest.version + (string.IsNullOrEmpty(m.Manifest.author) ? "" : "  ·  " + m.Manifest.author)
                           + (projects.Any(p => string.Equals(p.Id, m.Id, StringComparison.OrdinalIgnoreCase))
                              ? "  ·  " + T("mbImportHasProject", "has a project") : "");
                ImportRow(m.Preview, m.Name, sub, () => ImportInstalled(mod));
                mods++;
            }
            empty.text = ModManager.Installed.Count == 0
                ? (ModImport.Available ? T("mbImportNone", "No mods installed: pick a downloaded .zip with From a file.")
                                       : T("mbImportNoneNoPicker", "No mods installed: put one into your Mods folder first."))
                : T("mbImportNoMatch", "No installed mod matches.");
            empty.style.display = mods == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            count.text = T("mbImportPick", "Pick a mod to import");
            note.text = T("mbImportNote", "The mod's files are copied into a new project. It keeps its id: Export as mod replaces the installed copy.");
        }

        bool MatchesMod(ModInfo m)
        {
            string q = newName?.value?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            string hay = $"{m.Name} {m.Id} {m.Manifest?.author}";
            foreach (var w in q.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
                if (hay.IndexOf(w, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        void ImportRow(Texture2D picture, string name, string sub, Action pick)
        {
            var row = new Button(() => { if (importBusy) return; click(); pick(); });
            row.AddToClassList("mods-row");
            var thumb = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-thumb", "mb-thumb");
            if (picture != null) thumb.style.backgroundImage = new StyleBackground(picture);
            var text = Add(row, new VisualElement { pickingMode = PickingMode.Ignore }, "mods-row-text");
            Add(text, new Label(name) { pickingMode = PickingMode.Ignore }, "mods-row-name");
            Add(text, new Label(sub) { pickingMode = PickingMode.Ignore }, "mods-row-sub");
            hookFocus(row);
            list.Add(row);
        }

        /// <summary>An installed mod as a project (the import list's rows, the Mod Browser's Edit in Mod Builder).</summary>
        public void ImportInstalled(ModInfo mod)
        {
            if (mod?.Manifest == null || mod.Source == null || importBusy) return;
            StartImport(mod.Manifest, () => mod.Source, null);
        }

        /// <summary>From a file...: the system's picker (ModImport.Pick, polled until it answers), the file read as a mod.</summary>
        void PickFile()
        {
            string heading = T("modsImport", "Import mod");
            importBusy = true;
            UiBusy.Mark(importButton, true);
            var poll = ModImport.Pick();
            ModImport.Picked picked = null;
            Panel.schedule.Execute(() =>
            {
                picked = poll();
                if (picked == null) return;
                importBusy = false;
                UiBusy.Mark(importButton, false);
                if (picked.cancelled) return;
                if (picked.error != null) { notice(heading, string.Format(T("mbImportFailed", "The mod couldn't be imported: {0}"), picked.error)); return; }
                // A mod for the original game (its .aem / .aei files, no mod.json): made into a project of its own.
                if (OriginalModImport.Looks(picked.path)) { StartOriginalImport(picked.path, picked.name); return; }
                var manifest = ModImport.Inspect(picked.path, out string error);
                if (manifest == null)
                {
                    ModImport.Discard(picked.path);
                    notice(heading, string.Format(T("mbImportFailed", "The mod couldn't be imported: {0}"), error));
                    return;
                }
                string path = picked.path;
                ZipSource zip = null;
                StartImport(manifest, () => zip = new ZipSource(path), () => { zip?.Dispose(); ModImport.Discard(path); });
            }).Every(200).Until(() => picked != null);
        }

        /// <summary>An original-game mod's zip as a new project (OriginalModImport, on a worker thread): the ships whose hull it
        /// replaces as overrides with that model and its textures, the other textures as skins; then what became of it.</summary>
        void StartOriginalImport(string path, string fileName)
        {
            string heading = T("modsImport", "Import mod");
            string name = System.IO.Path.GetFileNameWithoutExtension(fileName ?? path);
            ModProject project;
            try { project = ModProject.Create(string.IsNullOrWhiteSpace(name) ? "Original mod" : name, ""); }
            catch (Exception e) { ModImport.Discard(path); notice(heading, string.Format(T("mbImportFailed", "The mod couldn't be imported: {0}"), e.Message)); return; }
            var db = Database.Shared;
            var ships = OriginalModImport.ShipsByModel(db);
            importBusy = true;
            UiBusy.Mark(importButton, true);
            OriginalModImport.Result result = null;
            var work = System.Threading.Tasks.Task.Run(() => result = OriginalModImport.Import(path, project, db, ships));
            bool finished = false;
            Panel.schedule.Execute(() =>
            {
                if (!work.IsCompleted) return;
                finished = true;
                importBusy = false;
                UiBusy.Mark(importButton, false);
                ModImport.Discard(path);
                if (result == null || result.project == null)
                {
                    notice(heading, string.Format(T("mbImportFailed", "The mod couldn't be imported: {0}"), result?.error ?? work.Exception?.GetBaseException().Message));
                    projects = ModProject.List();
                    RebuildRows();
                    return;
                }
                projects = ModProject.List();
                selected = projects.FirstOrDefault(p => string.Equals(System.IO.Path.GetFullPath(p.Folder), System.IO.Path.GetFullPath(result.project.Folder),
                                                                      StringComparison.OrdinalIgnoreCase)) ?? projects.FirstOrDefault();
                if (importing) ToggleImport(); else RebuildRows();
                Refresh();
                notice(heading, OriginalImportReport(result, project.Name));
            }).Every(100).Until(() => finished);
        }

        /// <summary>What the import made of an original-game mod's files.</summary>
        static string OriginalImportReport(OriginalModImport.Result r, string name)
        {
            var lines = new List<string> { string.Format(T("mbOgDone", "\"{0}\" is a project now, made from a mod for the original game:"), name) };
            foreach (var (ship, file, triangles, textured) in r.hulls)
                lines.Add("• " + string.Format(T("mbOgShip", "{0}: a new hull from {1} ({2} triangles)"), GameNames.Ship(ship), System.IO.Path.GetFileName(file),
                                                triangles.ToString("N0")) + (textured ? "" : " " + T("mbOgNoTexture", "(no texture in the mod: choose its hull textures in the builder)")));
            foreach (var ship in r.parts.Select(p => p.ship).Distinct())
                lines.Add("• " + string.Format(T("mbOgParts", "{0}: its {1} kept, drawn like the game's own"), GameNames.Ship(ship),
                                                string.Join(", ", r.parts.Where(p => p.ship == ship).Select(p => ModBuilderScreen.PartRoleName(p.role)))));
            if (r.skins.Count > 0) lines.Add("• " + string.Format(T("mbOgSkins", "{0} textures replacing the game's of the same name"), r.skins.Count));
            if (r.hullCopies > 0) lines.Add("• " + string.Format(T("mbOgCopies", "{0} copies of a hull left out (the new hull is the whole ship)"), r.hullCopies));
            if (r.unused.Count > 0)
                lines.Add("• " + string.Format(T("mbOgUnused", "Not used: {0}"), string.Join(", ", r.unused.Take(6).Select(System.IO.Path.GetFileName))
                                                + (r.unused.Count > 6 ? $" (+{r.unused.Count - 6})" : "")));
            if (r.hulls.Count == 0 && r.skins.Count == 0) lines.Add(T("mbOgNothing", "Nothing in it the builder can use yet: no game ship's .aem and no .aei texture."));
            else lines.Add(T("mbOgOptimize", "Its textures are large: Menu > Optimize models and textures makes the mod a smaller download."));
            return string.Join("\n", lines);
        }

        /// <summary>Asks first when a project of the mod exists (it is replaced), then copies the mod's files on a worker
        /// thread (ModProject.CopyMod) and selects the new project. 'open' gives the mod's files, 'done' cleans up after.</summary>
        void StartImport(ModManifest manifest, Func<ModSource> open, Action done)
        {
            string heading = T("modsImport", "Import mod");
            var existing = projects.FirstOrDefault(p => string.Equals(p.Id, manifest.id, StringComparison.OrdinalIgnoreCase));
            void Run()
            {
                ModSource src;
                try { src = open(); }
                catch (Exception e) { done?.Invoke(); notice(heading, string.Format(T("mbImportFailed", "The mod couldn't be imported: {0}"), e.Message)); return; }
                if (existing != null && existing == selected) { SaveIfDirty(); selected = null; }
                importBusy = true;
                UiBusy.Mark(importButton, true);   // a mod of models and textures takes a while (docs/UI-DESIGN.md "Waiting")
                string root = ModProject.Root, id = manifest.id, replace = existing?.Folder, error = null, folder = null;
                var copy = System.Threading.Tasks.Task.Run(() => folder = ModProject.CopyMod(src, root, id, replace, out error));
                bool finished = false;
                Panel.schedule.Execute(() =>
                {
                    if (!copy.IsCompleted) return;
                    finished = true;
                    importBusy = false;
                    UiBusy.Mark(importButton, false);
                    done?.Invoke();
                    if (folder == null)
                    {
                        notice(heading, string.Format(T("mbImportFailed", "The mod couldn't be imported: {0}"), error ?? copy.Exception?.GetBaseException().Message));
                        return;
                    }
                    projects = ModProject.List();
                    selected = projects.FirstOrDefault(p => string.Equals(System.IO.Path.GetFullPath(p.Folder), System.IO.Path.GetFullPath(folder),
                                                                          StringComparison.OrdinalIgnoreCase)) ?? projects.FirstOrDefault();
                    if (importing) ToggleImport(); else RebuildRows();
                    Refresh();
                    string text = string.Format(T("mbImported", "\"{0}\" is a project now. The builder edits what it knows (ships, items, characters...); its other files stay as they are. Export as mod installs it, replacing an installed copy."), manifest.Name);
                    if (selected?.LoadError != null)
                        text += "\n\n" + string.Format(T("mbImportedBroken", "Some of it can't be read, so the project can't be opened until it is fixed in its folder: {0}"), selected.LoadError);
                    notice(heading, text);
                }).Every(100).Until(() => finished);
            }
            if (existing != null)
                ask(heading, string.Format(T("mbImportReplace", "There is a project of \"{0}\" already (\"{1}\"). Replace it with this copy of the mod? Its changes are lost."),
                                           manifest.Name, existing.Name), Run);
            else Run();
        }

        void Export()
        {
            if (selected == null) return;
            SaveIfDirty();
            string heading = T("mbExport", "Export as mod");
            void Run()
            {
                string path = selected.Export(out string error);
                if (path == null) notice(heading, string.Format(T("mbExportFailed", "The mod couldn't be exported: {0}"), error));
                else notice(heading, string.Format(T("mbExported", "\"{0}\" is in your Mods folder:\n{1}\nTurn it on in the Mod Browser."), selected.Name, path));
            }
            if (!HasContent(selected)) { notice(heading, T("mbExportEmpty", "This project is empty: add some content first.")); return; }
            var old = selected.InstalledMod;
            if (old != null)
                ask(heading, string.Format(T("mbExportReplace", "A mod \"{0}\" is installed (\"{1}\" v{2}). Replace it?"),
                                           selected.Id, old.Name, old.Version), Run);
            else Run();
        }

        void AskDelete()
        {
            if (selected == null) return;
            var p = selected;
            string heading = T("mbDeleteTitle", "Delete project");
            ask(heading, string.Format(T("mbDeleteText", "Delete \"{0}\" and all its files? An exported mod stays installed."), p.Name), () =>
            {
                if (!p.Delete(out string error)) { notice(heading, string.Format(T("mbDeleteFailed", "The project couldn't be deleted: {0}"), error)); return; }
                projects.Remove(p);
                selected = projects.FirstOrDefault();
                RebuildRows();
                Refresh();
            });
        }
    }
}
