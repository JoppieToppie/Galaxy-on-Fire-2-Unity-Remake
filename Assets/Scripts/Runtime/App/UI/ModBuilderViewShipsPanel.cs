// ModBuilderViewShipsPanel.cs
// ModBuilderView: the ships panel and the test flight (part of the partial ModBuilderView; its fields are in ModBuilderView.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        void ImportModel() => PickGlb(T("mbPickModel", "Choose a ship model"), path =>
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(pickedName ?? "ship");
            string id = Project.ShipIdFor(name);
            string model = Project.AddModel(path, id);
            var s = Project.AddNewShip(id, Pretty(name) == "New" ? T("mbNewShipName", "New ship") : Pretty(name), model);
            if (pickedAemLength > 0f) s.ModelLength = pickedAemLength;   // the game's own model: the size it had there
            ModBuilder.Ship = s;
            Project.LastShip = s.Ref;
            gizmo.Selected = -1;
            RequestReload(false);
            RebuildShips();
            RebuildPages();
            Status(T("mbLoadingModel", "Loading the model…"), false);
        });
        void PickModel(ProjectShip s) => PickGlb(T("mbPickModel", "Choose a ship model"), path =>
        {
            string baseName = s.IsOverride ? "ship_" + s.OverrideIndex : s.LocalId;
            s.Model = Project.AddModel(path, baseName);
            if (pickedAemLength > 0f) s.ModelLength = pickedAemLength;   // the game's own model: the size it had there
            else if (s.IsOverride && s.ModelLength <= 0f) s.ModelLength = 1000f;
            RequestReload(false);
            BuildShipPage();
            Status(T("mbLoadingModel", "Loading the model…"), false);
        });
        /// <summary>A picker of the game's flyable ships (with their icons) for a game model.</summary>
        void PickGameShip(string title, Action<int> pick)
        {
            var db = Database.Shared;
            var choices = new List<(string name, Texture2D icon, Action pick)>();
            for (int i = 0; i < GameNames.OriginalShips; i++)
            {
                if (i == 13 || i == 14 || i == 15 || db.Ship(i) == null || db.ShipAssembly(i, original: true) == null) continue;
                int index = i;
                choices.Add((GameNames.Ship(i), ItemInfo.ShipIcon(i), () => pick(index)));
            }
            ShowPicker(title, choices);
        }
        /// <summary>A new ship on game ship 'index''s model (GameModels): its stats, slots, race and hardpoints to start from,
        /// its icon unless one is set; nothing to import.</summary>
        void NewShipOnGameModel(int index)
        {
            var db = Database.Shared;
            string name = string.Format(T("mbCustomShip", "Custom {0}"), GameNames.Ship(index));
            string id = Project.ShipIdFor(name);
            var asm = db.ShipAssembly(index, original: true);
            var s = Project.AddNewShip(id, name, GameModels.Ref(asm.name));
            s.UseGameModel(asm.name, db.Ship(index), Database.GameMounts(index), Shop.RaceOfShip(index));
            Project.SetLoadout(s, ModBuilder.DefaultLoadout(db, index));
            ModBuilder.Ship = s;
            Project.LastShip = s.Ref;
            gizmo.Selected = -1;
            RequestReload(false);
            RebuildShips();
            RebuildPages();
            Status(string.Format(T("mbShipOnGameModel", "A new ship on the {0}'s model: give it a name and its own stats."), GameNames.Ship(index)), false);
        }
        /// <summary>The ship flies game ship 'index''s model instead (its hardpoints come along; the stats stay).</summary>
        void UseGameShipModel(ProjectShip s, int index)
        {
            var db = Database.Shared;
            var asm = db.ShipAssembly(index, original: true);
            if (asm == null) return;
            string old = s.HasGameModel ? null : s.Model;
            s.UseGameModel(asm.name, null, Database.GameMounts(index));
            if (!string.IsNullOrEmpty(old) && !Project.Ships.Any(o => o != s && o.Model == old))
                try { System.IO.File.Delete(System.IO.Path.Combine(Project.Folder, old)); } catch { }
            gizmo.Selected = -1;
            RequestReload(false);
            RebuildPages();
            Status(string.Format(T("mbShipModelSet", "{0}'s model, with its hardpoints."), GameNames.Ship(index)), false);
        }
        /// <summary>A picker of the game's ships for "Change a game ship" (not those already in the project).</summary>
        void PickOriginal()
        {
            var db = Database.Shared;
            var taken = new HashSet<int>(Project.Ships.Where(s => s.IsOverride).Select(s => s.OverrideIndex));
            var choices = new List<(string name, Texture2D icon, Action pick)>();
            for (int i = 0; i < GameNames.OriginalShips; i++)
            {
                if (i == 13 || i == 14 || i == 15 || taken.Contains(i) || db.Ship(i) == null || db.ShipAssembly(i) == null) continue;
                int index = i;
                choices.Add((GameNames.Ship(i), ItemInfo.ShipIcon(i), () =>
                {
                    var s = Project.AddOverride(db, index);
                    ModBuilder.Ship = s;
                    Project.LastShip = s.Ref;
                    gizmo.Selected = -1;
                    Project.SetLoadout(s, ModBuilder.DefaultLoadout(db, index));
                    RequestReload(false);
                    RebuildShips();
                    RebuildPages();
                }));
            }
            ShowPicker(T("mbChangeShipTitle", "Change a game ship"), choices);
        }
        /// <summary>The kind of hangar a station has ("Vossk"), for the top bar's Hangar button.</summary>
        static string HangarTypeName(int station)
        {
            var db = Database.Shared;
            var st = db.Stations.ElementAtOrDefault(station);
            if (st == null) return "?";
            int room = StationTables.HangarIndex(station, db.Systems.ElementAtOrDefault(st.system)?.raceId ?? 0);
            return room >= 0 && room < RoomNames.Length && RoomNames[room] != null ? T("mbRoom" + room, RoomNames[room]) : "?";
        }
        /// <summary>One station per kind of hangar (StationTables.HangarIndex): picking one moves the builder there, and its
        /// orbit is the test flights'. Dis (Terran) and Var Hastra (Midorian) where they can, else the first that can host.</summary>
        /// <summary>The hangar kinds the builder can sit in: (room, its English name, the station that has it); 'current' the
        /// room the builder is in now (-1 none).</summary>
        static List<(int room, string name, int station)> HangarChoices(out int current)
        {
            var db = Database.Shared;
            var rooms = RoomNames;
            int[] preferred = { 70, -1, -1, 78 };
            int RoomOf(int i) => StationTables.HangarIndex(i, db.Systems.ElementAtOrDefault(db.Stations[i].system)?.raceId ?? 0);
            var hosts = Enumerable.Range(0, db.Stations.Count).Where(i => ModBuilder.CanHost(i, db)).ToList();
            current = hosts.Contains(Session.StationIndex) ? RoomOf(Session.StationIndex) : -1;
            var list = new List<(int, string, int)>();
            for (int room = 0; room < rooms.Length; room++)
            {
                if (rooms[room] == null) continue;
                int r = room;
                int station = room < preferred.Length && preferred[room] >= 0 && hosts.Contains(preferred[room]) && RoomOf(preferred[room]) == room
                    ? preferred[room] : hosts.Where(i => RoomOf(i) == r).DefaultIfEmpty(-1).First();
                if (station >= 0) list.Add((room, rooms[room], station));
            }
            return list;
        }
        void PickHangar()
        {
            var db = Database.Shared;
            var choices = new List<(string, Texture2D, Action)>();
            foreach (var (room, name, station) in HangarChoices(out int current))
            {
                var st = db.Stations[station];
                int race = db.Systems.ElementAtOrDefault(st.system)?.raceId ?? 0;
                string label = $"{T("mbRoom" + room, name)}\n{st.name}" + (room == current ? " ✓" : "");
                var emblem = GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/race_" + (race >= 0 && race < 4 ? race : 0));
                int r = room, s = station;
                choices.Add((label, emblem, () =>
                {
                    if (r == current) return;
                    SaveNow();
                    ModBuilder.SwitchStation(s);
                }));
            }
            ShowPicker(T("mbHangarTitle", "Build and test-fly at"), choices);
        }
        void AskRemove()
        {
            var s = Current;
            if (s == null) return;
            Confirm(T("mbRemoveShip", "Remove ship"), string.Format(T("mbRemoveShipText", "Remove \"{0}\" from the project?"), ShipName(s, Database.Shared)), () =>
            {
                Project.Ships.Remove(s);
                // Its model file goes too when no other ship uses it.
                if (!string.IsNullOrEmpty(s.Model) && !s.HasGameModel && !Project.Ships.Any(o => o.Model == s.Model))
                    try { System.IO.File.Delete(System.IO.Path.Combine(Project.Folder, s.Model)); } catch { }
                ModBuilder.Ship = Project.Ships.FirstOrDefault();
                Project.LastShip = ModBuilder.Ship?.Ref;
                gizmo.Selected = -1;
                RequestReload(false);
                RebuildShips();
                RebuildPages();
            });
        }
        void PickItem(int kind, int slot)
        {
            var db = Database.Shared;
            var choices = new List<(string, Texture2D, Action)> { (T("mbEmptySlot", "— empty —"), null, () => SetSlot(kind, slot, -1)) };
            foreach (var it in ModBuilder.ItemsFor(db, kind))
            {
                int index = it.index;
                choices.Add((GameNames.Item(index), ItemInfo.ItemIcon(index), () => SetSlot(kind, slot, index)));
            }
            ShowPicker(T("mbChooseItem", "Choose"), choices);
        }
        // ---- test flight --------------------------------------------------------------------------------------
        void StartTestFlight()
        {
            if (!Flies(kind)) return;
            bool skin = kind == ModBuilder.Kind.Skins && ModBuilder.SkinShip >= 0;
            if (Current == null && !skin) { Status(T("mbNoShipToFly", "Add a ship first."), true); return; }
            if (!InHangar) return;
            if (launchPending || level.PlayerFlying) return;
            sound(false);
            launchPending = true;
            RequestReload(false);   // the game flies what the files say now
            Status(T("mbPreparing", "Preparing the test flight…"), false);
        }
        void Launch()
        {
            launchPending = false;
            if (ModManager.BuilderProject == null || ModManager.BuilderProject.Broken) { ShowProblems(); return; }
            ModBuilder.StartTestFlight();
            if (level.PlayerShip == null || boundShip != Session.ShipIndex) { level.ReplacePlayerShip(Session.ShipIndex); boundShip = -2; }
            engineLevel = 0f;   // the take-off lights them itself (and moves, so no stationary plume)
            engineSlider.SetValueWithoutNotify(0f);
            ApplyEngines();
            gizmo.Selected = -1;
            gizmo.Bind(null, null);
            boundTransform = null;
            placing = -1;
            level.Launch();
        }
    }
}
