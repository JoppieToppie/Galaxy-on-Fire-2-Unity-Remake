using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Multiplayer;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed partial class StationManagementWindow
    {
        void AddSection(string title, string description)
        {
            var section = new VisualElement(); section.AddToClassList("player-station-section");
            var label = new Label(title) { enableRichText = false }; label.AddToClassList("player-station-section-title"); section.Add(label);
            if (!string.IsNullOrEmpty(description))
            {
                var note = new Label(description);
                note.AddToClassList("player-station-note");
                section.Add(note);
            }
            body.Add(section);
        }

        void AddInfo(string label, string value) => AddInfoTo(body, label, value);

        void AddInfoTo(VisualElement parent, string label, string value)
        {
            var row = new VisualElement(); row.AddToClassList("player-station-info");
            row.style.flexWrap = Wrap.Wrap; row.style.minHeight = 32; row.style.flexShrink = 0;
            var name = new Label(label.TrimEnd().TrimEnd(':') + ":") { enableRichText = false };
            name.style.whiteSpace = WhiteSpace.Normal; name.style.flexShrink = 0; name.style.marginRight = 8;
            var detail = new Label(value ?? ""); detail.enableRichText = false; detail.style.whiteSpace = WhiteSpace.Normal;
            detail.style.flexShrink = 1; detail.style.minWidth = 0; detail.style.flexGrow = 1;
            row.Add(name); row.Add(detail); parent.Add(row);
        }

        void AddItemChoice(string key, string label, HashSet<int> allowed = null)
        {
            var items = NetGame.Db?.Items;
            var all = items == null ? new List<ItemData>() : items
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.name) &&
                    (allowed == null || allowed.Contains(item.index)))
                .OrderBy(item => item.name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.index).ToList();
            var names = all.GroupBy(item => item.name).ToDictionary(group => group.Key, group => group.Count());
            var options = new List<(string value, string label)> { ("", "Select an item") };
            options.AddRange(all.Select(item => (item.index.ToString(), names[item.name] == 1 ? item.name :
                item.name + " (" + item.type + ", " + (all.Where(other => other.name == item.name).ToList().IndexOf(item) + 1) + ")")));
            AddChoice(key, label, options, null, true);
        }

        static void StyleChoice(Button button, bool selected, bool focused = false)
        {
            var border = focused ? Color.white : selected ? new Color(.52f,.87f,1f) : new Color(.20f,.34f,.44f);
            button.style.backgroundColor = selected ? new Color(.12f,.29f,.39f) : new Color(.045f,.10f,.15f);
            button.style.borderTopColor = button.style.borderBottomColor = button.style.borderLeftColor = button.style.borderRightColor = border;
            button.style.borderTopWidth = button.style.borderBottomWidth = button.style.borderLeftWidth = button.style.borderRightWidth = 2;
            button.style.color = Color.white;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.minHeight = 48; button.style.height = StyleKeyword.Auto; button.style.flexShrink = 0;
            button.style.marginBottom = 6;
            button.style.alignSelf = Align.Stretch;
            button.style.minWidth = 0;
            button.style.maxWidth = StyleKeyword.None;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        void AddChoice(string key, string label, List<(string value, string label)> options, Action<string> changed = null, bool items = false)
        {
            if (options.Count == 0) { drafts.Remove(key); AddInfo(label, "No choices available."); return; }
            int index = drafts.TryGetValue(key, out string saved) ? options.FindIndex(option => option.value == saved) : -1;
            index = Math.Max(0, index);
            drafts[key] = options[index].value;
            var caption = new Label(label); caption.AddToClassList("player-station-note"); caption.style.whiteSpace = WhiteSpace.Normal; body.Add(caption);
            var button = new Button { text = options[index].label + "  ▾", name = "choice-" + key, enableRichText = false };
            button.AddToClassList("player-station-field"); button.AddToClassList("player-station-action");
            StyleChoice(button, false);
            button.clicked += () => OpenChoicePicker(key, label, options, changed, items);
            body.Add(button);
            if (items && int.TryParse(drafts[key], out int item))
            {
                var preview = new VisualElement { pickingMode = PickingMode.Ignore };
                preview.style.width = 90; preview.style.height = 44;
                preview.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item)); body.Add(preview);
            }
        }

        void OpenChoicePicker(string key, string label, List<(string value, string label)> options, Action<string> changed, bool items)
        {
            DestroyPlacementPreview();
            choicePickerKey = key;
            choiceReturnScroll = body.scrollOffset;
            heading.text = label.ToUpperInvariant();
            pageBar.style.display = DisplayStyle.None; primaryNav.style.display = DisplayStyle.None; stationList.style.display = DisplayStyle.None;
            panel.AddToClassList("player-station-picking-model");
            body.Clear(); fields.Clear();
            var toolbar = new VisualElement(); toolbar.style.flexDirection = FlexDirection.Row; toolbar.style.flexWrap = Wrap.Wrap; body.Add(toolbar);
            var results = new VisualElement(); body.Add(results);
            const int pageSize = 24;
            int page = Math.Max(0, options.FindIndex(option => option.value == Value(key))) / pageSize;
            int pages = Math.Max(1, (options.Count + pageSize - 1) / pageSize);
            void Show()
            {
                toolbar.Clear(); results.Clear();
                void Tool(string text, Action action, bool enabled)
                {
                    var b = new Button(action) { text = text }; b.AddToClassList("player-station-action"); StyleChoice(b, false); b.SetEnabled(enabled); toolbar.Add(b);
                }
                if (pages > 1)
                {
                    Tool("Previous", () => { page--; Show(); }, page > 0);
                    var count = new Label($"{page + 1} / {pages}");
                    count.style.unityTextAlign = TextAnchor.MiddleCenter;
                    count.style.flexGrow = 1;
                    toolbar.Add(count);
                    Tool("Next", () => { page++; Show(); }, page + 1 < pages);
                }
                foreach (var option in options.Skip(page * pageSize).Take(pageSize))
                {
                    bool selected = Value(key) == option.value;
                    var b = new Button(() =>
                    {
                        drafts[key] = option.value;
                        changed?.Invoke(option.value);
                        CloseChoicePicker();
                    }) { text = option.label, enableRichText = false };
                    if (selected) b.name = "selected-choice";
                    b.AddToClassList("player-station-row"); StyleChoice(b, selected);
                    if (items && int.TryParse(option.value, out int item))
                    {
                        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                        b.style.paddingLeft = 64;
                        icon.style.position = Position.Absolute;
                        icon.style.left = 8;
                        icon.style.top = 6;
                        icon.style.bottom = 6;
                        icon.style.width = 48;
                        icon.style.backgroundImage = new StyleBackground(ItemInfo.ItemIcon(item));
                        b.Add(icon);
                    }
                    b.RegisterCallback<FocusInEvent>(_ => StyleChoice(b, selected, true));
                    b.RegisterCallback<FocusOutEvent>(_ => StyleChoice(b, selected));
                    results.Add(b);
                }
                body.scrollOffset = Vector2.zero;
                if (InputMode.Current == InputKind.Gamepad)
                    (results.Q<Button>("selected-choice") ?? results.Q<Button>())?.Focus();
            }
            Show();
        }

        void CloseChoicePicker()
        {
            string key = choicePickerKey; choicePickerKey = null;
            panel.RemoveFromClassList("player-station-picking-model");
            heading.text = marketplaceOnly ? "MARKETPLACE" : "PLAYER STATIONS";
            RebuildBody(choiceReturnScroll);
            if (InputMode.Current == InputKind.Gamepad) body.Q<Button>("choice-" + key)?.Focus();
        }

        static List<(string value, string label, string race)> StationModelChoices()
        {
            var db = NetGame.Db;
            var models = db.Stations.Where(station => station != null && NetPlayerStations.IsStationModelPreset(station.index))
                .Select(station => (station, race: db.Systems.Find(system => system != null && system.index == station.system)?.raceId ?? -1))
                .OrderBy(entry => entry.race).ThenBy(entry => entry.station.index);
            var result = new List<(string, string, string)>();
            var counts = new Dictionary<int, int>();
            var assemblies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in models)
            {
                string assembly = OrbitBuilder.StationPresetAssembly(db, entry.station);
                if (string.IsNullOrEmpty(assembly)) continue;
                // Identical stock models are shown once per race; distinct custom presets retain their identities.
                if (!Modding.ModWorld.IsModStation(entry.station.index) && Modding.ModWorld.ModelOf(entry.station.index) == null &&
                    !assemblies.Add(entry.race + ":" + assembly)) continue;
                counts.TryGetValue(entry.race, out int number); counts[entry.race] = ++number;
                string race = entry.race >= 0 ? GoF2Remake.Flight.Standing.RaceName(entry.race) : "Unassigned";
                result.Add((entry.station.index.ToString(), $"{race} {number} · {entry.station.name} [#{entry.station.index}]", race));
            }
            return result;
        }

        void AddStationModelPicker(string key)
        {
            var models = StationModelChoices();
            if (models.Count == 0)
            {
                drafts.Remove(key);
                AddInfo("Station model", "No station models are configured.");
                return;
            }

            var chosen = models.Find(model => model.value == Value(key));
            if (chosen.value == null) drafts.Remove(key);
            AddAction(chosen.value == null ? "Choose station model" : "Station model: " + chosen.label,
                () => OpenModelPicker(key, models));
            if (!int.TryParse(chosen.value, out int index)) return;
            var image = Modding.ModPlayerInfrastructure.StationModelImage(index);
            if (image.texture != null)
            {
                var preview = new VisualElement();
                preview.AddToClassList("player-station-model-preview");
                preview.style.backgroundImage = new StyleBackground(image.texture);
                body.Add(preview);
            }
            if (!string.IsNullOrEmpty(image.id)) AddInfo("Image ID", image.id);
        }

        void OpenModelPicker(string key, List<(string value, string label, string race)> models)
        {
            DestroyPlacementPreview();
            modelPickerKey = key;
            pageBar.style.display = DisplayStyle.None; primaryNav.style.display = DisplayStyle.None; stationList.style.display = DisplayStyle.None;
            modelPickerReturnScroll = body.scrollOffset;
            panel.AddToClassList("player-station-picking-model");
            heading.text = "CHOOSE STATION MODEL";
            string selectedModel = Value(key);
            body.Clear();
            fields.Clear();
            var races = new List<string> { "All races" }; races.AddRange(models.Select(model => model.race).Distinct().OrderBy(race => race));
            var raceFilter = new VisualElement(); raceFilter.style.flexDirection = FlexDirection.Row; raceFilter.style.flexWrap = Wrap.Wrap; body.Add(raceFilter);
            var results = new VisualElement();
            body.Add(results);

            void ShowMatches(string query)
            {
                results.Clear();
                foreach (var group in models.Where(model => query == "All races" || model.race == query).GroupBy(model => model.race))
                {
                    var label = new Label(group.Key);
                    label.AddToClassList("player-station-section-title");
                    results.Add(label);
                    foreach (var model in group)
                    {
                        var button = new Button(() =>
                        {
                            drafts[key] = model.value;
                            CloseModelPicker();
                        }) { text = model.label };
                        button.AddToClassList("player-station-row");
                        button.EnableInClassList("player-station-row--selected", model.value == selectedModel);
                        button.clicked += () => menu?.PlayRelease();
                        results.Add(button);
                    }
                }
                if (results.childCount == 0)
                {
                    var empty = new Label("No station models match your search.");
                    empty.AddToClassList("player-station-note");
                    results.Add(empty);
                }
            }

            foreach (string race in races)
            {
                var filter = new Button(() => ShowMatches(race)) { text = race };
                filter.AddToClassList("player-station-action"); StyleChoice(filter, false); raceFilter.Add(filter);
            }
            ShowMatches("All races");
            body.scrollOffset = Vector2.zero;
            if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing)
                results.Q<Button>()?.Focus();
        }

        void CloseModelPicker()
        {
            modelPickerKey = null;
            pageBar.style.display = DisplayStyle.Flex;
            panel.RemoveFromClassList("player-station-picking-model");
            heading.text = "PLAYER STATIONS";
            RebuildBody(modelPickerReturnScroll);
            if (InputMode.Current != InputKind.Touch && !InputMode.MousePointing)
                body.Query<Button>().ToList().Find(button => button.text.StartsWith("Station model:", StringComparison.Ordinal) ||
                    button.text == "Choose station model")?.Focus();
        }

        void AddDirectoryPager()
        {
            if (managementCatalogue == null || managementCatalogue.directoryPages < 2) return;
            AddInfo("Pilot and faction directory", $"Page {directoryPage + 1} of {managementCatalogue.directoryPages}");
            void Move(int delta)
            {
                directoryPage += delta;
                RefreshManagementCatalogue();
            }
            if (directoryPage > 0) AddAction("Previous directory page", () => Move(-1));
            if (directoryPage + 1 < managementCatalogue.directoryPages) AddAction("Next directory page", () => Move(1));
        }

        void AddCatalogueChoice(string key, string label, string group)
        {
            var options = new List<(string value, string label)> { ("", "Select " + label.ToLowerInvariant() + "…") };
            if (managementCatalogue?.stationId == selected?.id)
                options.AddRange(managementCatalogue.options.Where(option => option.group == group).Select(option => (option.value, option.label)));
            AddChoice(key, label, options);
            if (options.Count == 1) AddInfo(label, managementCatalogue == null ? "Waiting for server data." : "No eligible entries available.");
        }

        void AddAccountField(string key, string label)
        {
            string group = key == "transferAccount" && Value("transferKind") == "faction" ||
                key == "successorAccount" && Value("successorType") == "faction" ? "factionLeader" :
                key.StartsWith("gateBlockTarget_", StringComparison.Ordinal) &&
                Value(key.Replace("Target_", "Type_")) == "faction" ? "faction" : "pilot";
            AddCatalogueChoice(key, label, group);
        }

        void AddRaceChoice(string key, string label)
        {
            var races = Modding.ModRefs.RaceWords.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(race => (value: PlayerIdentity.RaceKey(race.race), label: race.name))
                .Concat(Modding.ModRaces.All.Where(entry => PlayerIdentity.IsSelectableRace(entry.race)).Select(race => (value: race.key, label: Modding.ModRaces.Name(race.race))))
                .GroupBy(race => race.value).Select(group => group.First()).OrderBy(race => race.label).ToList();
            races.Insert(0, ("", "Choose race…"));
            AddChoice(key, label, races);
        }

        // Button steppers preserve the complete numeric range without a keyboard or arbitrary preset values.
        void AddNumber(string key, string label, long initial, long min, long max, long quantum = 1)
        {
            long value = drafts.TryGetValue(key, out string existing) && long.TryParse(existing, out long parsed) ? parsed : initial;
            value = Math.Clamp(value, min, max);
            value = min + (value - min) / quantum * quantum;
            drafts[key] = value.ToString();
            var caption = new Label(label); caption.AddToClassList("player-station-note"); body.Add(caption);
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
            var current = new Label(value.ToString("N0")); current.style.flexGrow = 1; current.style.unityTextAlign = TextAnchor.MiddleCenter;
            long[] steps = new[] { quantum, quantum * 10, quantum * 100, quantum * 1000, quantum * 10000, quantum * 100000, quantum * 1000000, quantum * 10000000, quantum * 100000000 }.Where(step => step <= Math.Max(quantum, max - min)).ToArray();
            int stepIndex = drafts.TryGetValue(key + "_step", out string storedStep) && int.TryParse(storedStep, out int savedStep) ? Math.Clamp(savedStep, 0, steps.Length - 1) : 0;
            var picker = new Button { text = "Step: " + steps[stepIndex].ToString("N0") }; picker.AddToClassList("player-station-action");
            picker.clicked += () => { stepIndex = (stepIndex + 1) % steps.Length; drafts[key + "_step"] = stepIndex.ToString(); picker.text = "Step: " + steps[stepIndex].ToString("N0"); };
            void Set(long next)
            {
                value = Math.Clamp(next, min, max);
                value = min + (value - min) / quantum * quantum;
                drafts[key] = value.ToString(); current.text = value.ToString("N0");
            }
            Button Control(string title, Action action)
            {
                var button = new Button(action) { text = title };
                button.AddToClassList("player-station-action"); button.style.minWidth = 42; button.style.minHeight = 40;
                return button;
            }
            row.Add(Control("Min", () => Set(min)));
            row.Add(Control("−", () => Set(value - steps[stepIndex])));
            row.Add(current);
            row.Add(Control("+", () => Set(value + steps[stepIndex])));
            row.Add(Control("Max", () => Set(max)));
            body.Add(row);
            body.Add(picker);
        }

        void AddField(string key, string label, string initial)
        {
            var field = new TextField(label) { name = "field-" + key, value = drafts.TryGetValue(key, out string draft) ? draft : initial ?? "" };
            if (key == "newStationName" || key == "renameStationName") field.maxLength = NetPlayerStations.StationNameMaxLength;
            else if (key == "stationAnnouncement" || key.IndexOf("Reason", StringComparison.OrdinalIgnoreCase) >= 0)
            { field.maxLength = key == "stationAnnouncement" ? NetNews.StationAnnouncementLimit : 160; field.multiline = true; field.style.minHeight = 76; }
            field.RegisterValueChangedCallback(change => drafts[key] = change.newValue);
            field.AddToClassList("player-station-field");
            fields[key] = field; body.Add(field);
        }

        void AddAction(string label, string command) => AddAction(label, () => Send(command));

        void AddAction(string label, Action action)
        {
            var button = new Button(action) { text = label };
            button.AddToClassList("player-station-action");
            button.clicked += () => menu?.PlayRelease();
            button.RegisterCallback<PointerDownEvent>(_ => menu?.PlayPush(), TrickleDown.TrickleDown);
            button.style.whiteSpace = WhiteSpace.Normal; button.style.height = StyleKeyword.Auto; button.style.minHeight = 42; button.style.flexShrink = 0;
            body.Add(button);
        }

        string Value(string key) => fields.TryGetValue(key, out var field) ? System.Text.RegularExpressions.Regex.Replace((field.value ?? "").Trim(), @"\s+", " ") :
            drafts.TryGetValue(key, out var choice) ? choice : "";
        void SendStationName(string action, string name)
        {
            string nameError = NetPlayerStations.StationNameError(name);
            if (nameError != null)
            {
                feedback.text = nameError;
                feedbackTime = 8f;
                return;
            }
            Dispatch("/station " + action + " " + name);
        }

        void SendWithOptionalReason(string command, string reasonKey)
        {
            string reason = System.Text.RegularExpressions.Regex.Replace(NetPlayerStations.NormalizeBlacklistReason(Value(reasonKey)) ?? "", @"\s+", " ").Trim();
            Send(string.IsNullOrEmpty(reason) ? command : command + " " + reason);
        }

        void SendWithReason(string command, string reasonKey, string separator = " ")
        {
            string reason = System.Text.RegularExpressions.Regex.Replace(
                NetPlayerStations.NormalizeBlacklistReason(Value(reasonKey)), @"\s+", " ");
            if (string.IsNullOrWhiteSpace(reason))
            {
                feedback.text = "Enter a reason before applying this restriction.";
                feedbackTime = 8f;
                return;
            }
            if (command.TrimEnd() != command)
            {
                Send(command);
                return;
            }
            Send(command + separator + reason);
        }

        void Send(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || command.TrimEnd() != command || command.Contains("  "))
            {
                feedback.text = "Complete the required fields before submitting.";
                feedbackTime = 8f;
                return;
            }
            Dispatch(command);
        }

        bool Dispatch(string command, Action<string> onResult = null)
        {
            string submittedSnapshot = NetState.Instance?.PlayerStations;
            feedback.text = "Request sent. The server response will appear here.";
            feedbackTime = 8f;
            if (NetPlayer.Local?.RequestStationUiCommand(command.TrimEnd(), result =>
            {
                if (!open) return;
                feedback.text = string.IsNullOrEmpty(result) ? "Request completed." : result;
                feedbackTime = 12f;
                refresh = 0;
                actionRefreshPending = true;
                // A private bank change may not publish a public snapshot at all. Always
                // refresh the catalogue, and also catch a later public ownership/module update.
                actionSnapshotDeadline = ReferenceEquals(submittedSnapshot, NetState.Instance?.PlayerStations)
                    ? Time.unscaledTime + 10f : 0;
                nextPlannerQuery = 0;
                nextManagementQuery = 0;
                onResult?.Invoke(result);
            }) == true) return true;
            feedback.text = "A station request is already pending, or the server is unavailable.";
            feedbackTime = 8f;
            return false;
        }

        static string ModuleLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Unknown module";
            return id == "drydock" ? "Dry Dock" : id == "hangar" ? "Ship Hangar" : id == "dockarm" ? "Docking arms" :
                char.ToUpperInvariant(id[0]) + id.Substring(1);
        }
    }
}
