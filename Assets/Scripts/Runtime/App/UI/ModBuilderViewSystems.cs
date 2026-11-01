// ModBuilderViewSystems.cs
// The Mod Builder's Systems kind (part of ModBuilderView), edited on the star map (StarMap.OpenBuilder: every system shown,
// the project's own dragged to their place, a tap picks one): the project's systems.json (ProjectSystem, Modding/README.md
// "Systems", "Skies"). Left: the project's systems and the changes to other systems, a new system (where the map is centred),
// a change to a game system (or another mod's), Remove. Right: the picked system's name, owner, security, whether it is on
// the map from the start, its map position, its sky, its jumpgate's routes (to the game's, the project's or another mod's
// systems: those become dependencies) and gate station, its own sun, sunlight and sky images, its music; its stations
// (the project's: a new one here; Edit in space opens the Stations kind in its orbit). Edits change the map at once where
// they can (a drag), else when the game has read the project (the map opens again).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        VisualElement systemList, systemPage;
        ProjectSystem currentSystem;
        /// <summary>A game system picked on the map that the project doesn't change (its page offers Change).</summary>
        int pickedGameSystem = -1;
        StarMap builderMap;
        bool mapStale;

        static readonly string[] SecurityNames = { "Lawless", "Dangerous", "Average", "Secure" };

        void BuildSystemsPanels(VisualElement left, VisualElement right)
        {
            systemList = ListPanel(left, T("mbSystems", "Systems"));
            Act(left, T("mbNewSystem", "+ New system"), NewSystem, "mbv-add");
            Act(left, T("mbChangeSystem", "Change a system"), () => PickRef(RefKind.System, T("mbChangeSystemTitle", "Change a system"), e => ChangeSystem(e.value),
                                                                             e => !e.own && !Project.Systems.Any(s => s.IsOverride && s.OverrideRef == e.value)), "mbv-add", "mbv-change");
            Act(left, T("mbRemoveSystem", "Remove"), AskRemoveSystem, "mbv-danger", "mbv-remove");
            systemPage = PagePanel(right);
        }

        /// <summary>A project system's number in this game (-1: the game hasn't read it yet).</summary>
        int SystemIndex(ProjectSystem s) => s == null ? -1 : s.IsOverride ? ModBuilder.SystemNumber(s.OverrideRef) : ModWorld.SystemIndexOf(Project.Key(s.LocalId));

        string SystemTitle(ProjectSystem s) => !string.IsNullOrEmpty(s.Name) ? s.Name : s.IsOverride ? RefLabel(RefKind.System, s.OverrideRef, s.OverrideRef) : s.LocalId;

        void RebuildSystems()
        {
            systemList.Clear();
            if (Project == null) return;
            if (currentSystem != null && !Project.Systems.Contains(currentSystem)) currentSystem = null;
            if (currentSystem == null && pickedGameSystem < 0) currentSystem = Project.Systems.FirstOrDefault();
            foreach (var sys in Project.Systems)
            {
                var s = sys;
                string sub = s.IsOverride ? string.Format(T("mbChanges", "Changes {0}"), RefLabel(RefKind.System, s.OverrideRef, s.OverrideRef))
                           : $"{OwnerLabel(s)}  ·  {Project.Stations.Count(st => st.System == Project.Key(s.LocalId))} {T("mbStationsLower", "stations")}";
                ListRow(systemList, SystemTitle(s), sub, s == currentSystem, () =>
                {
                    currentSystem = s;
                    pickedGameSystem = -1;
                    RebuildSystems();
                    BuildSystemPage();
                    builderMap?.FocusSystem(SystemIndex(s));
                }, null, null);
            }
            if (Project.Systems.Count == 0)
                Note(systemList, T("mbNoSystems", "No systems yet. A new one goes where the map is centred; drag it into place."));
        }

        // ---- the map ------------------------------------------------------------------------------------------------

        void OpenMap()
        {
            if (builderMap != null) return;
            // A copy of the tables: the drags move its suns.
            var db = Database.Load();
            builderMap = StarMap.OpenBuilder(db, new StarMap.Builder
            {
                movable = i => kind == ModBuilder.Kind.Systems && Project.Systems.Any(s => !s.IsOverride && SystemIndex(s) == i),
                picked = OnMapPicked,
                moved = OnMapMoved,
                stationPicked = OnMapStation,
            });
            mapStale = false;
            if (builderMap != null && currentSystem != null) builderMap.FocusSystem(SystemIndex(currentSystem));
        }

        void CloseMap()
        {
            if (builderMap != null) builderMap.CloseBuilder();
            builderMap = null;
        }

        /// <summary>The game has read the project: the map shows it again (only after an edit the map can't show itself).</summary>
        void RefreshMap()
        {
            if (!mapStale || !OnMap(kind)) return;
            CloseMap();
            OpenMap();
        }

        void OnMapPicked(int system)
        {
            if (system < 0) return;
            if (kind == ModBuilder.Kind.Races) { ModRaceMapPicked(system); return; }
            var own = Project.Systems.FirstOrDefault(s => SystemIndex(s) == system);
            currentSystem = own;
            pickedGameSystem = own == null ? system : -1;
            RebuildSystems();
            BuildSystemPage();
        }

        void OnMapMoved(int system, Vector3 position)
        {
            var s = Project.Systems.FirstOrDefault(x => !x.IsOverride && SystemIndex(x) == system);
            if (s == null) return;
            var old = s.Position;
            s.Position = new Vector3(position.x, position.y, old.z);
            currentSystem = s;
            pickedGameSystem = -1;
            MarkDirty();
            RequestReload(true);   // prices and routes follow the distance; the map keeps its own copy meanwhile
            BuildSystemPage();
        }

        void OnMapStation(int station)
        {
            var own = Project.Stations.FirstOrDefault(st => StationNumberOf(st) == station);
            Status(own != null ? string.Format(T("mbStationOwn", "{0}: edit it in the Stations kind (in space)."), own.Name ?? own.LocalId)
                               : string.Format(T("mbStationGame", "{0}: a station of the game. Stations > Change a station changes it."), Database.Shared.Stations.ElementAtOrDefault(station)?.name), false);
        }

        int StationNumberOf(ProjectStation st) => st.IsOverride ? ModBuilder.StationNumber(st.OverrideRef) : ModWorld.StationIndexOf(Project.Key(st.LocalId));

        string SystemsHint() => builderMap != null ? T("mbMapHint", "Drag your systems (amber) into place; tap one to pick it, again to look inside.") : "";

        bool EscapeSystems() => builderMap != null && builderMap.InSystemView;

        // ---- editing ----------------------------------------------------------------------------------------------

        void NewSystem() => AskText(T("mbNewSystem", "+ New system"), T("mbName", "Name"), "", name =>
        {
            var at = builderMap != null ? builderMap.CentrePosition() : new Vector3(50, 50, 50);
            var s = Project.AddSystem(name, new Vector3(Mathf.Clamp(at.x, 0, 110), Mathf.Clamp(at.y, 0, 110), 50), 0);
            s.Sky = 4;
            currentSystem = s;
            pickedGameSystem = -1;
            SystemEdited();
            RebuildSystems();
            Status(T("mbSystemNeedsStation", "Give it a station and a jumpgate route, or only a Khador Drive reaches it."), false);
        });

        void ChangeSystem(string reference)
        {
            var s = Project.AddSystemOverride(reference);
            currentSystem = s;
            pickedGameSystem = -1;
            SystemEdited();
            RebuildSystems();
        }

        void AskRemoveSystem()
        {
            var s = currentSystem;
            if (s == null) return;
            string key = s.IsOverride ? null : Project.Key(s.LocalId);
            int stations = key == null ? 0 : Project.Stations.Count(st => st.System == key);
            Confirm(T("mbRemoveSystem", "Remove"), string.Format(T("mbRemoveText", "Remove \"{0}\" from the project?"), SystemTitle(s))
                    + (stations > 0 ? "\n" + string.Format(T("mbRemoveSystemStations", "Its {0} stations go too."), stations) : ""), () =>
            {
                Project.Systems.Remove(s);
                if (key != null) Project.Stations.RemoveAll(st => st.System == key);
                foreach (var o in Project.Systems) o.Gates = o.Gates.Where(g => g != key).ToList();
                currentSystem = null;
                SystemEdited();
                RebuildSystems();
            });
        }

        /// <summary>An edit the map shows once the game has read it again.</summary>
        void SystemEdited(bool page = true)
        {
            MarkDirty();
            mapStale = true;
            RequestReload(true);
            if (page) BuildSystemPage();
        }

        void BuildSystemPage()
        {
            systemPage.Clear();
            if (currentSystem == null)
            {
                if (pickedGameSystem >= 0)
                {
                    var g = Database.Shared.Systems.Find(x => x.index == pickedGameSystem);
                    string reference = ModWorld.ActiveSystemKeys().FirstOrDefault(kv => kv.Key == pickedGameSystem).Value ?? pickedGameSystem.ToString();
                    Heading(systemPage, g?.name ?? "?");
                    Note(systemPage, T("mbGameSystemNote", "A system of the game (or of a mod this one depends on)."));
                    Act(systemPage, T("mbChangeIt", "Change it"), () => ChangeSystem(reference), "mbv-change");
                    Act(systemPage, T("mbAddGateTo", "+ A gate route from one of your systems to here"), () =>
                    {
                        var own = Project.Systems.FirstOrDefault(s => !s.IsOverride);
                        if (own == null) { Status(T("mbNoOwnSystem", "Make a system first."), true); return; }
                        var l = own.Gates; if (!l.Contains(reference)) l.Add(reference); own.Gates = l;
                        currentSystem = own;
                        pickedGameSystem = -1;
                        SystemEdited();
                        RebuildSystems();
                    });
                    return;
                }
                Note(systemPage, T("mbPickSystem", "Pick a system on the left or on the map."));
                return;
            }
            JsonButton(systemPage, currentSystem.Json, () => { SystemEdited(); RebuildSystems(); BuildSystemPage(); },
                       T("mbJsonSystemNote", "Also in systems.json: skybox, sunTexture / sunColor, spaceMusic / stationMusic."));
            var s = currentSystem;
            bool isNew = !s.IsOverride;
            Heading(systemPage, isNew ? T("mbNewSystemSub", "New system") : string.Format(T("mbChanges", "Changes {0}"), RefLabel(RefKind.System, s.OverrideRef, s.OverrideRef)));
            if (isNew) Note(systemPage, string.Format(T("mbSystemKey", "Other content names it {0}."), Project.Key(s.LocalId)));
            var name = Text(systemPage, T("mbName", "Name"), s.Name, 40, v => { s.Name = v.Trim(); SystemEdited(false); RebuildSystems(); });
            if (isNew) SystemOwnerField(s);   // a game race or a mod's own (ModBuilderViewRaces)
            Segments(systemPage, T("mbSecurity", "Security"), SecurityNames.Select(n => T("mbSec" + n, n)).ToArray(), s.Security, n => { s.Security = n; SystemEdited(); });
            SystemRaceLists(s);   // locals, raiders, mods' traffic only
            var vrow = Row(systemPage);
            FlagButton(vrow, T("mbVisible", "On the map from the start"), s.Visible, v => s.Visible = v, () => SystemEdited());
            // systems.json "available" (ModUnlocks): on the map only while it holds, e.g. quest(name) == 1 while a quest runs.
            ConditionField(systemPage, T("mbSystemAvailable", "On the map only when"), (string)s.Get("available"), v => { s.Set("available", v); SystemEdited(); });
            Note(systemPage, T("mbSystemAvailableNote", "Empty = as revealed. A condition shows the system only while it holds, e.g. quest(smuggler_run) == 1."));

            if (isNew)
            {
                Caption(systemPage, T("mbMapPosition", "Map position (drag it on the map)"));
                var p = s.Position;
                var prow = Row(systemPage, "mbv-xyz");
                Int(prow, "X", Mathf.RoundToInt(p.x), -50, 150, v => { s.Position = new Vector3(v, s.Position.y, s.Position.z); SystemEdited(false); });
                Int(prow, "Y", Mathf.RoundToInt(p.y), -50, 150, v => { s.Position = new Vector3(s.Position.x, v, s.Position.z); SystemEdited(false); });
                Int(prow, "Z", Mathf.RoundToInt(p.z), -50, 150, v => { s.Position = new Vector3(s.Position.x, s.Position.y, v); SystemEdited(false); });
                Note(systemPage, T("mbMapPositionNote", "The game's systems lie within X 15-94, Y 2-96, Z 10-90; prices follow the distance."));
            }

            // ---- jumpgate ----
            Heading(systemPage, T("mbJumpgate", "Jumpgate"));
            var gates = s.Gates;
            foreach (var g in gates)
            {
                string gate = g;
                var row = Row(systemPage);
                Add(row, new Label("→ " + RefLabel(RefKind.System, gate, gate)), "mbv-inline-label");
                Act(row, "×", () => { s.Gates = s.Gates.Where(x => x != gate).ToList(); SystemEdited(); }, "mbv-step");
            }
            if (gates.Count == 0) Note(systemPage, isNew ? T("mbNoGates", "No routes: reached only with a Khador Drive.") : T("mbNoGatesAdded", "No routes added (it keeps its own)."));
            Act(systemPage, T("mbAddGate", "+ Route to a system"), () => PickRef(RefKind.System, T("mbGateTo", "Route to"), e =>
            {
                var l = s.Gates; if (!l.Contains(e.value)) l.Add(e.value); s.Gates = l;
                SystemEdited();
            }, e => isNew ? e.value != Project.Key(s.LocalId) : e.value != s.OverrideRef), "mbv-add");
            Note(systemPage, T("mbGatesNote", "Routes work both ways: the other system gets the route back."));
            if (isNew)
                RefField(systemPage, T("mbGateStation", "The jumpgate's orbit"), RefKind.Station, s.GateStation, v => { s.GateStation = v; SystemEdited(); },
                         T("mbFirstStation", "its first station"), filter: e => Project.Stations.Any(st => !st.IsOverride && Project.Key(st.LocalId) == e.value && st.System == Project.Key(s.LocalId)));

            // ---- sky and sun ----
            Heading(systemPage, T("mbSky", "Sky and sun"));
            var srow = Row(systemPage);
            Add(srow, new Label(string.Format(T("mbSkyN", "The game's sky {0}"), s.Sky)), "mbv-inline-label");
            Act(srow, "−", () => { s.Sky = (s.Sky + 18) % 19; SystemEdited(); }, "mbv-step");
            Act(srow, "+", () => { s.Sky = (s.Sky + 1) % 19; SystemEdited(); }, "mbv-step");
            Note(systemPage, T("mbSkyNote", "Which of the game's 19 skies it uses; an image of your own below replaces it."));
            ImageRow(systemPage, T("mbNebula", "Nebula image (2:1 panorama or a 6:1 cube strip)"), s.Nebula, "sky", (s.LocalId ?? "system") + "_nebula", v => { s.Nebula = v; SystemEdited(); });
            if (s.Nebula != null) Float(systemPage, T("mbNebulaBrightness", "Nebula brightness"), s.NebulaBrightness, 0f, 4f, v => { s.NebulaBrightness = v; SystemEdited(false); });
            Float(systemPage, T("mbStarsBrightness", "Stars brightness"), s.StarsBrightness, 0f, 4f, v => { s.StarsBrightness = v; SystemEdited(false); });
            {
                var sky = s.Get("skybox") as JObject;
                var rot = sky?["rotation"] as JArray;
                bool sunAligned = sky?["sunAligned"] is JValue sa && sa.Type == JTokenType.Boolean && (bool)sa;
                FlagButton(Row(systemPage), T("mbSkySunAligned", "The sky's top toward the sun in every orbit"), sunAligned, on =>
                {
                    var o = s.Get("skybox") as JObject ?? new JObject();
                    if (on) o["sunAligned"] = true; else o.Remove("sunAligned");
                    s.Set("skybox", o.HasValues ? o : null);
                    SystemEdited(false);
                }, () => BuildSystemPage());
                Note(systemPage, T("mbSkySunAlignedNote", "Paint the image's bright side at its top (+Y): each orbit turns it to that orbit's sun, like the supernova sky. A fixed turn then turns the image first."));
                FlagButton(Row(systemPage), T("mbSkyFixedTurn", "The sky turned the same at every station"), rot != null, on =>
                {
                    var o = s.Get("skybox") as JObject ?? new JObject();
                    if (on) o["rotation"] = new JArray(0, 0, 0); else o.Remove("rotation");
                    s.Set("skybox", o.HasValues ? o : null);
                    SystemEdited(false);
                }, () => BuildSystemPage());
                if (rot != null && rot.Count >= 3)
                {
                    var rrow = Row(systemPage, "mbv-xyz");
                    for (int k = 0; k < 3; k++)
                    {
                        int c = k;
                        Float(rrow, "XYZ".Substring(c, 1), (float)rot[c], -360f, 360f, v => { rot[c] = Math.Round(v, 1); SystemEdited(false); });
                    }
                }
            }
            string stars = s.Stars;
            int starMode = stars == null ? 0 : stars == "0" ? 1 : stars == "1" ? 2 : stars == "2" ? 3 : stars == "none" ? 4 : 5;
            Segments(systemPage, T("mbStars", "Stars"), new[] { T("mbUsual", "Usual"), "1", "2", "3", T("mbNoStars", "None"), T("mbOwnImage", "Image") }, starMode, n =>
            {
                if (n == 5) { PickImage(T("mbPickStars", "Choose the star image"), path => { s.Stars = Project.AddFile(path, "sky", (s.LocalId ?? "system") + "_stars", ".png"); SystemEdited(); }); return; }
                s.Stars = n == 0 ? null : n == 4 ? "none" : (n - 1).ToString();
                SystemEdited();
            });
            ImageRow(systemPage, T("mbSunImage", "Sun image (512 x 512, on black)"), s.SunTexture, "textures", (s.LocalId ?? "system") + "_sun", v => { s.SunTexture = v; SystemEdited(); });
            ColourRow(systemPage, T("mbSunlight", "Sunlight (0..1)"), s.SunColor, c => { s.SunColor = c; SystemEdited(); });

            // ---- music ----
            Heading(systemPage, T("mbMusic", "Music"));
            RefField(systemPage, T("mbSpaceMusic", "In its orbits"), RefKind.Music, s.SpaceMusic, v => { s.SpaceMusic = v; SystemEdited(false); }, T("mbRaceMusic", "its race's"),
                     filter: e => e.group != ModRefs.GameGroup);
            RefField(systemPage, T("mbStationMusic", "Docked at its stations"), RefKind.Music, s.StationMusic, v => { s.StationMusic = v; SystemEdited(false); }, T("mbRaceMusic", "its race's"),
                     filter: e => e.group != ModRefs.GameGroup);

            // ---- its stations ----
            if (isNew)
            {
                Heading(systemPage, T("mbItsStations", "Its stations"));
                string key = Project.Key(s.LocalId);
                foreach (var st in Project.Stations.Where(x => x.System == key))
                {
                    var station = st;
                    var row = Row(systemPage);
                    Add(row, new Label(st.Name ?? st.LocalId), "mbv-inline-label");
                    Act(row, T("mbEditInSpace", "Edit in space"), () => EditStationInSpace(station), "mbv-step");
                }
                int count = Project.Stations.Count(x => x.System == key);
                if (count < ModWorld.MaxStationsPerSystem)
                    Act(systemPage, T("mbNewStationHere", "+ New station here"), () => AskText(T("mbNewStationHere", "+ New station here"), T("mbName", "Name"), "", n =>
                    {
                        var st = Project.AddStation(n, key, 5, (count * 3 + 2) % 23);
                        st.LooksLike = DefaultLook(s.Race >= 0 ? s.Race : Project.OwnRace(s.ModRace)?.LookRace ?? 0);
                        SystemEdited();
                    }), "mbv-add");
                Note(systemPage, T("mbStationsNote", "At most 7 stations per system; edit their look in space (Stations)."));
            }
        }

        /// <summary>The first original station of a race (a new station's stand-in look).</summary>
        static int DefaultLook(int race) => race switch { 1 => 41, 2 => 40, 3 => 78, _ => 70 };

        /// <summary>An image field: its file (copied into 'folder' as PNG), Choose…, ×.</summary>
        void ImageRow(VisualElement parent, string label, string current, string folder, string baseName, Action<string> set)
        {
            var row = Row(parent);
            Add(row, new Label($"{label}: {(current != null ? System.IO.Path.GetFileName(current) : T("mbNone", "none"))}"), "mbv-inline-label");
            Act(row, T("mbChoose", "Choose…"), () => PickImage(label, path =>
            {
                string old = current;
                set(Project.AddFile(path, folder, baseName, ".png"));
                Project.DeleteFile(old, false);
            }), "mbv-step");
            if (current != null) Act(row, "×", () => { set(null); Project.DeleteFile(current, false); }, "mbv-step");
        }

        /// <summary>The Stations kind in space, in that station's orbit.</summary>
        void EditStationInSpace(ProjectStation st)
        {
            SaveNow();
            ModBuilder.Reload();
            int index = StationNumberOf(st);
            ModBuilder.SpaceStation = index;
            ModBuilder.SpaceStationRef = st.Ref;
            SelectKind(ModBuilder.Kind.Stations);
        }
    }
}
