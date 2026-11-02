// ModBuilderViewRaces.cs
// Remake-only: the Mod Builder's Races kind (part of ModBuilderView): the mod's own races beside the game's (races.json,
// ProjectRace; ModRaces, Modding/README.md "Races"), edited on the star map beside Systems (they own its systems; a tap on
// a system picks the project's race that owns it, or says whose it is). Left: the project's races (emblem, look, standing
// or hostile, how many systems they own), a new one, Remove (with every reference to it in the project: ModProject.
// RemoveRace). Right: name, description, look (the game race whose hangar, bar, visitors, music, guns and gate it borrows),
// emblem (a PNG), colour, the player's start standing, hostile, lounge diplomats, its NPC fighters' gun, its relations to
// the game's and the mods' races (friend / neutral / enemy) and the systems it owns.
// Also the race fields of the other kinds, here so they read the same: a ship's builder and its traffic races, a system's
// owner, locals, raiders and mods-only traffic, and a mission step's Race field (the mods' races after the game's).
// Every pick of a race is the game's race (a number or word) or a mod race's key ("my_mod:guild"; another mod's makes it a
// dependency); a reference the builder didn't pick is never rewritten.

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Modding;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        /// <summary>The kinds the star map shows (they share it: leaving one for the other keeps it open).</summary>
        static bool OnMap(ModBuilder.Kind k) => (int)k >= 0 && ModBuilder.PlaceOf(k) == ModBuilder.Place.Map;

        static readonly string[] RelationLabels = { "Friend", "Neutral", "Enemy" };

        void BuildRacesPanels(VisualElement left, VisualElement right)
        {
            modRaceList = ListPanel(left, T("mbModRaces", "Races"));
            Act(left, T("mbNewModRace", "+ New race"), NewModRace, "mbv-add");
            Act(left, T("mbRemoveModRace", "Remove"), AskRemoveModRace, "mbv-danger", "mbv-remove");
            modRacePage = PagePanel(right);
        }

        string ModRaceTitle(ProjectRace r) => !string.IsNullOrEmpty(r.Name) ? r.Name : r.Id;

        static string LookName(int look) => T("race" + RaceNames[look], RaceNames[look]);

        /// <summary>A mod race's emblem from the project (null: none, or the project not read yet).</summary>
        static Texture2D ModRaceIcon(ProjectRace r) =>
            r?.Icon != null && ModManager.BuilderProject != null ? ModMaterials.Texture(ModManager.BuilderProject, r.Icon, false, true) : null;

        /// <summary>The project's systems a race owns.</summary>
        List<ProjectSystem> OwnedSystems(ProjectRace r) => Project.Systems.Where(s => s.ModRace != null && Project.OwnRace(s.ModRace) == r).ToList();

        void RebuildModRaces()
        {
            modRaceList.Clear();
            if (Project == null) return;
            if (currentModRace != null && !Project.Races.Contains(currentModRace)) currentModRace = null;
            currentModRace ??= Project.Races.FirstOrDefault();
            foreach (var race in Project.Races)
            {
                var r = race;
                string sub = LookName(r.Look) + "  ·  " + (r.Hostile ? T("mbModRaceHostileShort", "hostile")
                                                                     : string.Format(T("mbModRaceStandingShort", "standing {0}"), r.Standing));
                int owned = OwnedSystems(r).Count;
                if (owned > 0) sub += "  ·  " + string.Format(T("mbModRaceSystemsN", "{0} systems"), owned);
                var row = ListRow(modRaceList, ModRaceTitle(r), sub, r == currentModRace, () =>
                {
                    currentModRace = r;
                    RebuildModRaces();
                    BuildModRacePage();
                    var first = OwnedSystems(r).FirstOrDefault();
                    if (first != null) builderMap?.FocusSystem(SystemIndex(first));
                }, ModRaceIcon(r), "mbv-race-thumb");
                // Its colour as the emblem's frame (and its fill without an emblem).
                if (r.Colour is Color c && row.Q(className: "mbv-race-thumb") is VisualElement thumb)
                {
                    thumb.style.borderTopColor = thumb.style.borderBottomColor = thumb.style.borderLeftColor = thumb.style.borderRightColor = c;
                    if (r.Icon == null) thumb.style.backgroundColor = new Color(c.r, c.g, c.b, 0.35f);
                }
            }
            if (Project.Races.Count == 0)
                Note(modRaceList, T("mbNoModRaces", "No races of your own yet. A race owns systems, flies ships and raids, beside the game's four."));
        }

        // ---- the map --------------------------------------------------------------------------------------------------

        /// <summary>A system tapped on the map: the project's race that owns it is picked; whoever owns it is named.</summary>
        void ModRaceMapPicked(int system)
        {
            var sys = Database.Shared.Systems.Find(x => x.index == system);
            if (sys == null) return;
            int race = sys.raceId;
            var def = ModRaces.Get(race);
            var own = def != null && string.Equals(def.mod?.Id, Project.Id, StringComparison.OrdinalIgnoreCase) ? Project.FindRace(def.localId) : null;
            if (own != null) { currentModRace = own; RebuildModRaces(); BuildModRacePage(); }
            string owner = def != null ? ModRaces.Name(race) : race >= 0 && race < ModRefs.RaceNames.Length ? T("race" + ModRefs.RaceNames[race], ModRefs.RaceNames[race]) : "?";
            Status(string.Format(T("mbModRaceMapOwner", "{0}: owned by {1}."), sys.name, owner), false);
        }

        string ModRacesHint() => builderMap != null ? T("mbModRaceMapHint", "Tap a system to see who owns it; Systems gives a system its owner.") : "";

        // ---- editing ----------------------------------------------------------------------------------------------

        void NewModRace() => AskText(T("mbNewModRace", "+ New race"), T("mbName", "Name"), "", name =>
        {
            currentModRace = Project.AddRace(name);
            ModRaceEdited();
            RebuildModRaces();
            Status(T("mbModRaceNext", "Give it systems in Systems (Owned by) and ships in Ships (Built by)."), false);
        });

        void AskRemoveModRace()
        {
            var r = currentModRace;
            if (r == null) return;
            int uses = Project.RaceUses(r);
            Confirm(T("mbRemoveModRace", "Remove"), string.Format(T("mbRemoveText", "Remove \"{0}\" from the project?"), ModRaceTitle(r))
                    + (uses > 0 ? "\n" + string.Format(T("mbRemoveModRaceUses", "The {0} places in this mod that name it change too: its systems and ships go back to its look race."), uses) : ""), () =>
            {
                Project.RemoveRace(r);
                currentModRace = null;
                ModRaceEdited();
                RebuildModRaces();
            });
        }

        /// <summary>An edit the game (and its map) shows once it has read the project again.</summary>
        void ModRaceEdited(bool page = true)
        {
            MarkDirty();
            mapStale = true;
            RequestReload(true);
            if (page) BuildModRacePage();
        }

        void BuildModRacePage()
        {
            modRacePage.Clear();
            var r = currentModRace;
            if (r == null) { Note(modRacePage, T("mbPickModRace", "Add a race on the left to start.")); return; }
            JsonButton(modRacePage, r.Json, () => { ModRaceEdited(); RebuildModRaces(); },
                       T("mbJsonModRaceNote", "Every races.json field: id, name, description, look, icon, color, standing, hostile, diplomats, relations, gun."));
            Heading(modRacePage, ModRaceTitle(r));
            Note(modRacePage, string.Format(T("mbModRaceKey", "Systems, ships and other mods name it {0}."), Project.Key(r.Id)));
            Text(modRacePage, T("mbName", "Name"), r.Name, 40, v => { r.Name = v.Trim(); ModRaceEdited(false); RebuildModRaces(); });
            Text(modRacePage, T("mbDescription", "Description"), r.Description, 600, v => { r.Description = v; ModRaceEdited(false); }, true);
            Segments(modRacePage, T("mbModRaceLook", "Looks like"), RaceNames.Select((n, i) => LookName(i)).ToArray(), r.Look,
                     n => { r.Look = n; ModRaceEdited(); RebuildModRaces(); });
            Note(modRacePage, T("mbModRaceLookNote", "The game race whose hangars, bars, lounge visitors, music, NPC guns and jumpgate it borrows."));

            // ---- emblem and colour ----
            Heading(modRacePage, T("mbModRaceEmblem", "Emblem and colour"));
            if (ModRaceIcon(r) is Texture2D icon)
            {
                var big = Add(modRacePage, new VisualElement { pickingMode = PickingMode.Ignore }, "mbv-race-emblem");
                big.style.backgroundImage = new StyleBackground(icon);
            }
            ImageRow(modRacePage, T("mbModRaceIcon", "Emblem"), r.Icon, "icons", "race_" + r.Id, v => { r.Icon = v; ModRaceEdited(); RebuildModRaces(); });
            Note(modRacePage, T("mbModRaceIconNote", "A square PNG, transparent around it, like the game's race emblems. None: its look race's."));
            ColourRow(modRacePage, T("mbModRaceColour", "Colour (0..1)"), r.Colour, c => { r.Colour = c; ModRaceEdited(); RebuildModRaces(); });

            // ---- the player ----
            Heading(modRacePage, T("mbModRacePlayer", "The player"));
            Int(modRacePage, T("mbModRaceStanding", "Standing at the start (-100 to 100)"), r.Standing, -100, 100, v => { r.Standing = v; ModRaceEdited(false); RebuildModRaces(); });
            Note(modRacePage, T("mbModRaceStandingNote", "-71 and below: at war with the player; 71 and above: a friend. Killing its ships costs 5."));
            FlagButton(Row(modRacePage), T("mbModRaceHostile", "Always hostile, like the pirates"), r.Hostile, v => r.Hostile = v, () => { ModRaceEdited(); RebuildModRaces(); });
            FlagButton(Row(modRacePage), T("mbModRaceDiplomats", "Lounge diplomats make peace with it"), r.Diplomats, v => r.Diplomats = v, () => ModRaceEdited());
            RefField(modRacePage, T("mbModRaceGun", "Its fighters' gun (look and sound)"), RefKind.Item, r.Gun, v => { r.Gun = v; ModRaceEdited(); },
                     T("mbModRaceGunNone", "its look race's"), filter: e => e.type == 0 || e.type == -1);

            // ---- relations ----
            Heading(modRacePage, T("mbModRaceRelations", "Relations"));
            var relations = r.Relations;
            foreach (var (other, relation) in relations)
            {
                string o = other;
                Caption(modRacePage, RefLabel(RefKind.Race, o, o));
                var seg = Add(modRacePage, new VisualElement(), "mbv-seg-row");
                int now = Array.IndexOf(ProjectRace.RelationWords, relation);
                for (int i = 0; i < RelationLabels.Length; i++)
                {
                    int n = i;
                    var b = Act(seg, T("mbModRaceRel" + RelationLabels[i], RelationLabels[i]), () => { r.SetRelation(o, ProjectRace.RelationWords[n]); ModRaceEdited(); }, "mbv-seg");
                    b.EnableInClassList("mbv-seg--on", i == now);
                }
                Act(seg, "×", () => { r.SetRelation(o, null); ModRaceEdited(); }, "mbv-step");
            }
            if (relations.Count == 0)
                Note(modRacePage, T("mbModRaceNoRelations", "None: neutral to the others, at war with the pirates, the Void and the Specters (with everyone, when hostile)."));
            string self = Project.Key(r.Id);
            Act(modRacePage, T("mbModRaceAddRelation", "+ Relation to a race"), () => PickRef(RefKind.Race, T("mbModRaceRelations", "Relations"), e =>
            {
                if (e == null) return;
                r.SetRelation(e.value, "neutral");
                ModRaceEdited();
            }, e => e.value != self && !relations.Any(x => ModRefs.RaceValue(x.other, Project) == e.value)), "mbv-add");
            Note(modRacePage, T("mbModRaceRelationsNote", "Who its ships fight. A relation one side leaves out is the other side's."));

            // ---- its systems ----
            Heading(modRacePage, T("mbModRaceItsSystems", "Its systems"));
            var owned = OwnedSystems(r);
            foreach (var sys in owned)
            {
                var s = sys;
                var row = Row(modRacePage);
                Add(row, new Label(SystemTitle(s)), "mbv-inline-label");
                Act(row, T("mbModRaceEditSystem", "Edit"), () => { currentSystem = s; pickedGameSystem = -1; SelectKind(ModBuilder.Kind.Systems); builderMap?.FocusSystem(SystemIndex(s)); }, "mbv-step");
            }
            if (owned.Count == 0) Note(modRacePage, T("mbModRaceNoSystems", "None yet: Systems > Owned by gives it one of the mod's systems."));
        }

        // ---- race fields of the other kinds ---------------------------------------------------------------------------

        /// <summary>Picks a mod race (the project's or another mod's: the game's are the segments beside it).</summary>
        void PickModRace(string title, Action<string> set)
        {
            if (!ModRefs.List(RefKind.Race, Project).Any(e => e.group != ModRefs.GameGroup))
            {
                Status(T("mbModRaceNone", "No races of your own yet: make one in Races."), true);
                return;
            }
            PickRef(RefKind.Race, title, e => { if (e != null) set(e.value); }, e => e.group != ModRefs.GameGroup);
        }

        /// <summary>A game race's segments and a last one for a mod race (picked from the list, then named under it).</summary>
        void RaceOrModRace(VisualElement parent, string caption, int[] races, string[] names, int race, string modRace, Action<int> setRace, Action<string> setModRace)
        {
            var labels = names.Select(n => T("race" + n, n)).Append(T("mbModRaceSeg", "Mod race…")).ToArray();
            int cur = modRace != null ? races.Length : Array.IndexOf(races, race);
            Segments(parent, caption, labels, cur, n =>
            {
                if (n < races.Length) setRace(races[n]);
                else PickModRace(caption, setModRace);
            });
            if (modRace != null)
            {
                var row = Row(parent);
                Add(row, new Label("→ " + RefLabel(RefKind.Race, modRace, modRace)), "mbv-inline-label");
                Act(row, T("mbChoose", "Choose…"), () => PickModRace(caption, setModRace), "mbv-step");
            }
        }

        /// <summary>The Ship tab's builder: a game race (Terran ... Void) or a mod race.</summary>
        void ShipRaceField(ProjectShip s) =>
            RaceOrModRace(ship, T("mbRace", "Built by"), Races, RaceNames, s.Race, s.ModRace,
                          race => { s.Race = race; MarkDirty(); BuildShipPage(); },
                          modRace => { s.ModRace = modRace; MarkDirty(); BuildShipPage(); });

        /// <summary>A new system's owner: one of the four game races or a mod race.</summary>
        void SystemOwnerField(ProjectSystem s) =>
            RaceOrModRace(systemPage, T("mbOwner", "Owned by"), new[] { 0, 1, 2, 3 }, ModRefs.RaceNames, s.Race, s.ModRace,
                          race => { s.Race = race; SystemEdited(); RebuildSystems(); },
                          modRace => { s.ModRace = modRace; SystemEdited(); RebuildSystems(); });

        /// <summary>A system's owner for its list row.</summary>
        string OwnerLabel(ProjectSystem s) => s.ModRace != null ? RefLabel(RefKind.Race, s.ModRace, s.ModRace) : T("race" + ModRefs.RaceNames[s.Race], ModRefs.RaceNames[s.Race]);

        /// <summary>A system's patrols' and raiders' races, and whether only the mods' ships fly there.</summary>
        void SystemRaceLists(ProjectSystem s)
        {
            RaceListField(systemPage, T("mbModRaceLocals", "Patrolled by"), s.Locals, l => { s.Locals = l; SystemEdited(); },
                          T("mbModRaceLocalsNone", "Its owner's patrols."));
            RaceListField(systemPage, T("mbModRaceRaiders", "Raided by"), s.Raiders, l => { s.Raiders = l; SystemEdited(); },
                          T("mbModRaceRaidersNone", "The game's pick: pirates, or an enemy of its owner."));
            FlagButton(Row(systemPage), T("mbModRaceModTraffic", "Only the mods' ships fly here"), s.ModTraffic, v => s.ModTraffic = v, () => SystemEdited());
            Note(systemPage, T("mbModRaceModTrafficNote", "No game freighters, pirate outposts or bosses; its raiders are those above."));
            FlagButton(Row(systemPage), T("mbPirateHeld", "Held by pirates"), s.PirateHeld, v => s.PirateHeld = v, () => SystemEdited());
            Note(systemPage, T("mbPirateHeldNote", "Every station here is besieged like the Kaamo Club at the start, on every visit: pirate outposts and pirates, no docking, gate or Khador Drive until the outposts fall. Driven out, they come back after 30 minutes. A station's orbit can say otherwise."));
        }

        /// <summary>A list of races (game races by word, or by number when 'numbers'; mod races by key): each with ×, and
        /// + to add one; 'empty' says what an empty list means.</summary>
        void RaceListField(VisualElement parent, string caption, List<string> current, Action<List<string>> set, string empty, bool numbers = false)
        {
            Caption(parent, caption);
            foreach (var value in current)
            {
                string v = value;
                var row = Row(parent);
                Add(row, new Label(RefLabel(RefKind.Race, v, v)), "mbv-inline-label");
                Act(row, "×", () => set(current.Where(x => x != v).ToList()), "mbv-step");
            }
            if (current.Count == 0) Note(parent, empty);
            Act(parent, T("mbModRaceAdd", "+ A race"), () => PickRef(RefKind.Race, caption, e =>
            {
                if (e == null) return;
                int game = numbers && e.group == ModRefs.GameGroup ? ModRaces.GameRaceWord(e.value) : -1;
                string v = game >= 0 ? game.ToString() : e.value;
                var l = current.ToList();
                if (!l.Contains(v)) l.Add(v);
                set(l);
            }, e => !current.Any(c => ModRefs.RaceValue(c, Project) == e.value) && !(numbers && e.value == "specters")), "mbv-add");
        }

        /// <summary>A mission step's Race field's choices: its own values first (so steps read and write as before), then the
        /// project's races and those of the mods that are on, by key.</summary>
        List<(string value, string label)> RaceFieldChoices(QuestField f)
        {
            var l = new List<(string value, string label)>();
            for (int i = 0; i < f.values.Length; i++)
                l.Add((f.values[i], T("mbChoice_" + f.key + "_" + f.values[i], i < f.labels.Length ? f.labels[i] : f.values[i])));
            var keys = new HashSet<string>(l.Select(c => c.value));
            if (Project != null)
                foreach (var r in Project.Races.Where(r => r.Id != null))
                    if (keys.Add(Project.Key(r.Id))) l.Add((Project.Key(r.Id), ModRaceTitle(r)));
            foreach (var d in ModRaces.All)
                if (keys.Add(d.key)) l.Add((d.key, ModRaces.Name(d.race)));
            return l;
        }
    }
}
