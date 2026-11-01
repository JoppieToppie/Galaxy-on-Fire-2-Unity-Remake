// ModBuilder.cs
// Remake-only: the in-game Mod Builder's session (main menu > Mods > Mod Builder). Opening a project (Begin) makes a
// throwaway free-play game docked at the project's station (Dis, 70, a Terran hangar, unless the builder's Hangar button
// picked another: builder.json "station", SwitchStation) with the project as the only active mod
// (ModManager.SetBuilderProject), and loads the Station scene, where UI.ModBuilderView replaces the station menu: the
// project's ship sits on the turntable, its hardpoints are edited with World.ModBuilderGizmo, its test loadout chosen.
// Test fly (StartTestFlight) is the normal take-off into that station's orbit with World.ModBuilderRange there: no traffic, a few
// pirates that keep coming, no jumps; docking (or the pause menu's "Back to Mod Builder", or a death) lands back in the
// builder. Nothing of this game is ever saved (SaveGame refuses while Active). End returns to the main menu's projects.
// Plain C# (static: the session outlives the scene changes).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Modding
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class ModBuilder
    {
        /// <summary>The station the builder starts at (Dis, Terran) until a project picks another.</summary>
        public const int DefaultStation = 70;
        /// <summary>The station whose hangar hosts the builder and whose orbit the test flights use: the project's pick.</summary>
        public static int Station => StationFor(Project);

        static int StationFor(ModProject project)
        {
            int s = project?.BuilderStation ?? -1;
            return s >= 0 && s < Database.Shared.Stations.Count && CanHost(s) ? s : DefaultStation;
        }

        /// <summary>A station the builder can sit at and test-fly from: one with a station in its orbit (not an empty orbit,
        /// in the finished game's world the builder plays in), and none whose orbit brings an event of its own (the Kaamo
        /// Club's siege, the pirate bases' outposts, Loma's toll).</summary>
        public static bool CanHost(int station) => CanHost(station, Database.Shared);

        /// <summary>CanHost with the tables already read: Database.Load parses every data file again (~6 ms), and the
        /// hangar picker asks for all ~130 stations (it took close to a second to open).</summary>
        public static bool CanHost(int station, Database db)
        {
            var st = station >= 0 && station < db.Stations.Count ? db.Stations[station] : null;
            if (st == null || string.IsNullOrEmpty(st.name) || ModWorld.IsMissingStation(station)) return false;
            if (station == 108 || System.Array.IndexOf(PirateBases.Stations, station) >= 0 || st.system == 25) return false;
            return !World.OrbitLayout.IsEmptyOrbit(station, Session.WorldIndex);
        }

        /// <summary>The builder moves to another station's hangar (the Station scene again; its orbit is the test flights').</summary>
        public static void SwitchStation(int station)
        {
            if (Project == null || !CanHost(station)) return;
            Project.BuilderStation = station;
            Project.Save();
            Session.StationIndex = station;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            SceneManager.LoadScene("Station");
        }
        /// <summary>The ship shown while the project has none (the Phantom).</summary>
        public const int PlaceholderShip = 10;
        public const string ProjectsPanel = "modBuilderPanel";

        /// <summary>A project is open (the builder's game runs).</summary>
        public static bool Active { get => ModManager.BuilderOpen; private set => ModManager.BuilderOpen = value; }
        /// <summary>The open project.</summary>
        public static ModProject Project { get; private set; }
        /// <summary>The open project's undo / redo steps (null while none is open).</summary>
        public static ModProjectHistory History { get; private set; }
        /// <summary>The ship being worked on (null = the project has none yet).</summary>
        public static ProjectShip Ship { get; set; }
        /// <summary>The kinds of content the builder edits (its top tabs).</summary>
        /// <summary>The kinds of content (and AiTools: the MCP server's tab, ModBuilderScreen; shown wherever the builder is).</summary>
        // Remake: Races (the mod's own races, races.json) before AiTools, edited on the star map beside Systems.
        // Remake: NpcShips (npcships.json: capital ships, freighters, bosses; ModNpcShips), edited in space, before AiTools.
        public enum Kind { Ships, Items, Characters, Skins, WeaponFx, Systems, Stations, Interiors, Quests, Campaigns, Audio, Blueprints, Kits, Races, NpcShips, AiTools }
        public const int KindCount = 16;

        /// <summary>Where a kind is edited: in the game's own place for it.</summary>
        public enum Place { Hangar, Lounge, Map, Space }

        /// <summary>The place of a kind (Interiors: the hangar or the lounge by the room picked, InteriorIsBar).</summary>
        public static Place PlaceOf(Kind k) => k switch
        {
            Kind.Characters or Kind.Quests or Kind.Audio => Place.Lounge,
            Kind.Systems or Kind.Races => Place.Map,
            Kind.Stations or Kind.WeaponFx or Kind.NpcShips => Place.Space,
            Kind.Interiors => InteriorIsBar ? Place.Lounge : Place.Hangar,
            _ => Place.Hangar,
        };

        /// <summary>The kind the builder opens on (the projects page's tile; kept through test flights).</summary>
        public static Kind OpenKind { get; set; }

        /// <summary>The Interiors kind shows a bar room (else a hangar room).</summary>
        public static bool InteriorIsBar { get; set; }
        /// <summary>The project's room the Interiors kind previews (its interiors.json key), null = none: the station's own.</summary>
        public static string PreviewInterior { get; set; }
        /// <summary>The rooms editor's free camera (kept through the scene reloads that rebuild a room): the point it orbits
        /// and (yaw, pitch, distance); RoomViewSet false = none yet.</summary>
        public static Vector3 RoomPivot, RoomOrbit;
        public static bool RoomViewSet;

        /// <summary>The builder works in space (Stations, Weapon fx): the Space scene in the builder's station orbit, nothing
        /// flying, the builder's own camera (not a test flight).</summary>
        public static bool InSpace { get; private set; }
        /// <summary>The station whose orbit the space kinds show (a project station's), -1 = the builder's station.</summary>
        public static int SpaceStation { get; set; } = -1;
        /// <summary>The project's station to pick there (its Ref), null = the first.</summary>
        public static string SpaceStationRef { get; set; }

        /// <summary>The builder moves to the scene a kind is edited in (the Station scene's hangar / lounge / map, or the
        /// Space scene); false when it is there already (the caller only switches its panels).</summary>
        public static bool GoTo(Kind k)
        {
            if (Project == null) return false;
            OpenKind = k;
            bool space = PlaceOf(k) == Place.Space;
            if (space == InSpace && !(space && SpaceStationChanged)) return false;
            Project.Save();
            InSpace = space;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            if (space)
            {
                int st = SpaceStation >= 0 && SpaceStation < Database.Shared.Stations.Count && !ModWorld.IsMissingStation(SpaceStation) ? SpaceStation : Station;
                shownSpaceStation = st;
                Session.StationIndex = st;
                Session.ArrivedByTravel = false;
                SceneManager.LoadScene("Space");
            }
            else
            {
                Session.StationIndex = Station;
                SceneManager.LoadScene("Station");
            }
            return true;
        }

        static int shownSpaceStation = -1;
        /// <summary>The space kinds show another orbit than the one loaded (a station picked there).</summary>
        static bool SpaceStationChanged => InSpace && SpaceStation >= 0 && SpaceStation != shownSpaceStation;

        /// <summary>The Station scene again (an interior picked: its room is built when the scene loads).</summary>
        public static void ReloadStation()
        {
            if (Project == null) return;
            Project.Save();
            Session.StationIndex = Station;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            SceneManager.LoadScene("Station");
        }

        /// <summary>The Space scene again, in 'station''s orbit (a station's model changed, or another station picked).</summary>
        public static void ReloadSpace(int station)
        {
            if (Project == null) return;
            Project.Save();
            SpaceStation = station;
            int st = station >= 0 && station < Database.Shared.Stations.Count && !ModWorld.IsMissingStation(station) ? station : Station;
            shownSpaceStation = st;
            InSpace = true;
            Session.StationIndex = st;
            Session.LaunchedFromStation = false;
            Session.ArrivedByTravel = false;
            SceneManager.LoadScene("Space");
        }
        /// <summary>The game ship the Skins kind shows (its turntable and test flights), -1 = none.</summary>
        public static int SkinShip { get; set; } = -1;
        /// <summary>The Skins kind shows a game ship instead of the project's own.</summary>
        static bool ShowingSkin => OpenKind == Kind.Skins && SkinShip >= 0;

        /// <summary>The ship the Blueprints and Campaign kinds put on the turntable (a blueprint's product, the campaign's
        /// start ship: a game number in this game), -1 = none; PreviewLoadout its equipment (null = a default one).</summary>
        public static int PreviewShip { get; set; } = -1;
        public static List<ItemStack> PreviewLoadout { get; set; }
        static bool ShowingPreview => (OpenKind == Kind.Blueprints || OpenKind == Kind.Campaigns) && PreviewShip >= 0 && Database.Shared.Ship(PreviewShip) != null;

        /// <summary>The build the turntable and test flights show of a customizable ship in the Kits kind (the part being
        /// edited in place of the default build's), build text; null = its default build (Session.ShipBuilds is cleared for
        /// the builder's ships, ApplyShip).</summary>
        public static string PreviewBuild { get; set; }

        /// <summary>A reference (a game number or a key) as this game's ship / item / station number, -1 = not in this game.</summary>
        public static int ShipNumber(string r) => string.IsNullOrEmpty(r) ? -1 : int.TryParse(r, out int n) ? n : ModContent.ShipIndexOf(r);
        public static int ItemNumber(string r) => string.IsNullOrEmpty(r) ? -1 : int.TryParse(r, out int n) ? n : ModContent.ItemIndexOf(r);
        public static int StationNumber(string r) => string.IsNullOrEmpty(r) ? -1 : int.TryParse(r, out int n) ? n : ModWorld.StationIndexOf(r);
        public static int SystemNumber(string r) => string.IsNullOrEmpty(r) ? -1 : int.TryParse(r, out int n) ? n : ModWorld.SystemIndexOf(r);

        /// <summary>A test flight runs (between Test fly and landing back in the builder).</summary>
        public static bool TestFlying { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = TestFlying = InSpace = PlayTesting = false; Project = null; Ship = null; History = null; OpenKind = Kind.Ships; SkinShip = -1;
            InteriorIsBar = false; PreviewInterior = null; SpaceStation = -1; shownSpaceStation = -1; PreviewShip = -1; PreviewLoadout = null; RoomViewSet = false; SpaceStationRef = null; PreviewBuild = null;
        }

        /// <summary>Opens 'project': the builder's game; the caller loads the Station scene (after the mods have loaded).</summary>
        public static void Begin(ModProject project)
        {
            ResetGame(project);
            Project = project;
            Active = true;
            TestFlying = false;
            InSpace = false;
            SpaceStation = shownSpaceStation = -1;
            PreviewInterior = null;
            ModManager.SetBuilderProject(project.Folder);
            var db = Database.Shared;
            foreach (var s in project.Ships) if (s.IsOverride) s.FillFromGame(db.Ship(s.OverrideIndex));
            Ship = project.Ships.FirstOrDefault(s => s.Ref == project.LastShip) ?? project.Ships.FirstOrDefault();
            var skins = project.SkinShips();
            SkinShip = skins.Count > 0 ? skins[0] : -1;
            ApplyShip();
            History?.Dispose();
            ModProjectHistory.PruneStores();
            History = new ModProjectHistory(project);
            Debug.Log($"Mod Builder: opened \"{project.Name}\" ({project.Folder}), {project.Ships.Count} ship(s)");
        }

        /// <summary>The builder's throwaway game as it starts: free play in the finished game's world, docked at the project's
        /// station, no money, no cheats.</summary>
        static void ResetGame(ModProject project)
        {
            Session.ResetNewGame();
            Session.UseCompletedWorld();   // free play: every original ship and item exists, no story
            Session.Difficulty = Session.DifficultyNormal;
            Session.Economy = Economy.Android;
            Session.StationIndex = StationFor(project);
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            Session.Credits = 0;
            Cheats.ClearGranted();
        }

        // ---- play tests -----------------------------------------------------------------------------------------

        /// <summary>A play test runs: the game as a player has it, with the project on (its quests, stations, systems...), the
        /// station menu instead of the builder's screen, traffic in the orbits; the pause menu / the station's Menu go back to
        /// the builder (EndPlayTest), and so does a death or a failed quest. Nothing is saved.</summary>
        public static bool PlayTesting { get; private set; }

        /// <summary>Starts a play test from the builder's station (the caller sets up what it tests: a quest started, a
        /// campaign's start, then loads the Station scene).</summary>
        public static void BeginPlayTest()
        {
            Project.Save();
            PlayTesting = true;
            TestFlying = InSpace = false;
            PreviewInterior = null;
            Cheats.ClearGranted();
            Session.Credits = Mathf.Max(Session.Credits, 100000);
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            Debug.Log("Mod Builder: play test");
        }

        /// <summary>Back in the builder from a play test: the game's state as it was when the project opened (the caller has
        /// ended the quests the test ran), the builder's hangar.</summary>
        public static void EndPlayTest()
        {
            if (!PlayTesting) return;
            PlayTesting = false;
            ResetGame(Project);
            ApplyShip();
            Debug.Log("Mod Builder: play test over");
            SceneManager.LoadScene("Station");
        }

        /// <summary>Closes the project and returns to the main menu's project list.</summary>
        public static void End()
        {
            PlayTesting = false;
            if (Project != null) Project.Save();
            History?.Dispose();
            History = null;
            Active = TestFlying = InSpace = false;
            Project = null;
            Ship = null;
            PreviewInterior = null;
            Cheats.ClearGranted();
            ModManager.SetBuilderProject(null);
            Session.ResetNewGame();
            GoF2Remake.Data.MenuRequest.OpenPanelOnStart = ProjectsPanel;
            SceneManager.LoadScene("MainMenu");
        }

        /// <summary>The builder's session dropped without a scene change (the Editor leaving Play mode with a project open:
        /// Play mode runs without a domain reload, and these statics would otherwise keep the project as the active mod and
        /// saving refused until the next Play, which the edit-mode tests then saw).</summary>
        public static void CloseAfterPlayMode()
        {
            if (!Active && Project == null) return;
            Project?.Save();
            History?.Dispose();
            History = null;
            Active = TestFlying = InSpace = PlayTesting = false;
            Project = null;
            Ship = null;
            PreviewInterior = null;
            Cheats.ClearGranted();
            ModManager.SetBuilderProject(null);
        }

        /// <summary>The game's ship number of a project ship: an override's original, a new ship's mod number (-1 while the
        /// project can't be read).</summary>
        public static int IndexOf(ProjectShip s)
        {
            if (s == null || Project == null) return -1;
            if (s.IsOverride) return s.OverrideIndex;
            return ModContent.ShipIndexOf(Project.Id + ":" + s.LocalId);
        }

        /// <summary>The ship number on the turntable: the skinned ship in the Skins kind, else the current ship's, else the
        /// placeholder.</summary>
        public static int CurrentIndex
        {
            get
            {
                if (ShowingSkin) return SkinShip;
                if (ShowingPreview) return PreviewShip;
                int i = IndexOf(Ship);
                return i >= 0 && Database.Shared.Ship(i) != null ? i : PlaceholderShip;
            }
        }

        /// <summary>The project's files changed: the game reads them again (the ship models rebuild in the background).</summary>
        public static void Reload()
        {
            if (Project == null) return;
            Project.Save();
            History?.Commit();
            ModManager.SetBuilderProject(Project.Folder);
        }

        /// <summary>The current ship and its test loadout into the game's state (the flown ship, its equipment).</summary>
        public static void ApplyShip()
        {
            var db = Database.Shared;
            Session.ShipIndex = CurrentIndex;
            // A customizable ship shows its default build, or in the Kits kind the build with the part being edited.
            string buildKey = ModShipKits.BuildKey(Session.ShipIndex);
            if (buildKey != null)
            {
                if (OpenKind == Kind.Kits && !string.IsNullOrEmpty(PreviewBuild)) Session.ShipBuilds[buildKey] = PreviewBuild;
                else Session.ShipBuilds.Remove(buildKey);
            }
            // A skinned game ship flies with a loadout of its own slots (it has none saved).
            var loadout = ShowingSkin ? DefaultLoadout(db, Session.ShipIndex)
                        : ShowingPreview ? PreviewLoadout ?? DefaultLoadout(db, Session.ShipIndex)
                        : Ship != null ? Project.Loadout(Ship) : null;
            if (loadout == null && Ship != null) { loadout = DefaultLoadout(db, Session.ShipIndex); Project.SetLoadout(Ship, loadout); }
            Session.Equipment = (loadout ?? new List<ItemStack>()).Where(i => db.Item(i.item) != null && !i.builtIn).Select(i => i.Clone()).ToList();
            Session.Cargo = new List<ItemStack>();
            Hangar.SyncBuiltIn(db);
            Hangar.FitToSlots(db);
            Session.Cargo.Clear();   // what didn't fit isn't wanted in the hold
            Session.SelectedSecondary = -1;
            Session.PlayerHull = Session.PlayerArmor = -1;
            Session.PlayerShield = -1f;
        }

        // ---- loadouts -------------------------------------------------------------------------------------------

        /// <summary>Secondary weapons are mounted with this much ammo.</summary>
        public const int Ammo = 50;

        /// <summary>Items a slot kind (0 primary, 1 secondary, 2 turret, 3 equipment) can take, the original ones, sorted by
        /// category and price.</summary>
        public static List<ItemData> ItemsFor(Database db, int kind) =>
            db.Items.Where(i => i.TypeId == kind && !Session.Unsaleable.Contains(i.index) && !ModContent.IsMissingItem(i.index) && i.maxPrice > 0)
                    .OrderBy(i => i.categoryId).ThenBy(i => i.maxPrice).ToList();

        /// <summary>A loadout to start from: the dearest primary, secondary and turret in every slot, and a shield, armor,
        /// booster and scanner while the equipment slots last.</summary>
        public static List<ItemStack> DefaultLoadout(Database db, int ship)
        {
            var l = new List<ItemStack>();
            var s = db.Ship(ship)?.slots;
            if (s == null) return l;
            ItemData Best(int kind, string category = null) =>
                ItemsFor(db, kind).Where(i => category == null || i.category == category).OrderByDescending(i => i.maxPrice).FirstOrDefault();
            var primary = ItemsFor(db, 0).Where(i => i.category == "Laser" || i.category == "Blaster").OrderByDescending(i => i.maxPrice).FirstOrDefault() ?? Best(0);
            for (int i = 0; i < s.primary && primary != null; i++) l.Add(new ItemStack(primary.index, 1));
            var secondary = Best(1, "Missile") ?? Best(1);
            for (int i = 0; i < s.secondary && secondary != null; i++) l.Add(new ItemStack(secondary.index, Ammo));
            var turret = Best(2, "Turret");
            for (int i = 0; i < s.turret && turret != null; i++) l.Add(new ItemStack(turret.index, 1));
            int eq = 0;
            foreach (var cat in new[] { "Shield", "Armor", "Booster", "Scanner", "Tractor beam", "Steering nozzle" })
            {
                if (eq >= s.equipment) break;
                var e = Best(3, cat);
                if (e != null) { l.Add(new ItemStack(e.index, 1)); eq++; }
            }
            return l;
        }

        // ---- test flights ---------------------------------------------------------------------------------------

        /// <summary>God mode for the test flights (builder.json).</summary>
        public static bool TestGodMode { get => Project?.Flag("godMode", true) ?? true; set => Project?.SetFlag("godMode", value); }
        /// <summary>Infinite ammo for the test flights (builder.json).</summary>
        public static bool TestInfiniteAmmo { get => Project?.Flag("infiniteAmmo", true) ?? true; set => Project?.SetFlag("infiniteAmmo", value); }

        /// <summary>Test fly: the ship and its loadout set, the cheats granted; the caller takes off (StationLevel.Launch),
        /// which loads Dis' orbit.</summary>
        public static void StartTestFlight()
        {
            Project.Save();
            ApplyShip();
            Session.StationIndex = Station;
            Cheats.ClearGranted();
            Cheats.Grant("godMode", TestGodMode);
            Cheats.Grant("infiniteAmmo", TestInfiniteAmmo);
            TestFlying = true;
            Debug.Log($"Mod Builder: test flight in ship {Session.ShipIndex}");
        }

        /// <summary>Back in the builder's hangar from a test flight ('flyIn': the ship flies in through the forcefield).</summary>
        public static void EndTestFlight(bool flyIn)
        {
            TestFlying = false;
            Cheats.ClearGranted();
            Session.StationIndex = Station;
            Session.DockedFromSpace = flyIn;
            SceneManager.LoadScene("Station");
        }

        /// <summary>The scene the builder was left in has landed: whatever the flight did is undone (ammo spent, damage).</summary>
        public static void OnDocked()
        {
            TestFlying = false;
            Cheats.ClearGranted();
            ApplyShip();
        }
    }
}
