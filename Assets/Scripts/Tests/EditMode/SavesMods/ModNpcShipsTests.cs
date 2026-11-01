// ModNpcShipsTests.cs
// Feature: Modding/README.md "NPC ships" (npcships.json, Modding.ModNpcShips, TrafficPlanNpcShips): a mod's new NPC ships
// (a capital ship, freighter or boss on a game hull or a ships.json ship) with their hardpoints, escorts, loot, spawn rules,
// docking pads and shop; its changes to the game's own (the battleship's chance and turrets, the freighters' speed); the
// game's types untouched without mods; a bad kind breaks the mod, an unknown field warns.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModNpcShipsTests : SavesModsTest
    {
        const string Mod = "t_npc";

        static void SetXp(int xp) { Session.Kills = 0; Session.Kills = Mathf.Max(0, xp - Session.Xp); }

        void ModOn(string json)
        {
            WriteMod("npc", Mod, "1.0", "", ("npcships.json", json));
            ScanAndEnable(Mod);
            Database.Load();
        }

        const string Cruiser = @"[
            { 'id': 'cruiser', 'kind': 'capital', 'looksLike': 'carrier', 'name': 'Cruiser', 'race': 'vossk', 'gunFactor': 2,
              'escorts': { 'count': 3, 'ships': [ 5 ], 'strength': 2 },
              'loot': { 'items': [ { 'item': 113, 'amount': 4 }, { 'item': 101, 'amount': [1, 2], 'chance': 0 } ] },
              'spawn': { 'chance': 40, 'races': [ 1 ], 'minRank': 2 },
              'docking': { 'pads': [ { 'position': [ 0, 1000, 5000 ] } ], 'standing': 20, 'raceShip': false, 'markup': 2,
                           'gear': 0, 'sells': [ { 'item': 37, 'amount': 7 } ], 'energyCells': 3 },
              'launches': { 'ships': [ 9 ], 'perWave': 2, 'total': 4, 'every': 10 } } ]";

        [Test]
        public void WithoutModsTheGamesTypesAreUntouched()
        {
            ScanAndEnable();
            var all = ModNpcShips.All();
            CollectionAssert.AreEqual(ModNpcShips.GameKeys, all.Select(t => t.key));
            Assert.IsTrue(all.All(t => t.game && !t.changed));
            Assert.IsNull(ModNpcShips.Changed(ModNpcShips.Battleship));
            Assert.AreEqual(30, ModNpcShips.Chance(ModNpcShips.Carrier, 30), "the game's chance");
            Assert.IsEmpty(ModNpcShips.ModTypes());
        }

        [Test]
        public void AModsCapitalShipWithItsRules()
        {
            ModOn(Cruiser);
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            Assert.IsEmpty(Installed(Mod).Warnings, AllMessages(Installed(Mod)));
            var t = ModNpcShips.Get(Mod + ":cruiser");
            Assert.IsNotNull(t);
            Assert.AreEqual(ModNpcShips.Capital, t.kind);
            Assert.AreEqual(ModNpcShips.Carrier, t.BaseKey);
            Assert.AreEqual("Cruiser", t.name);
            Assert.AreEqual(1, t.race, "a race word");
            Assert.AreEqual(3, t.escorts.min);
            Assert.AreEqual(2f, t.escorts.hull);
            Assert.AreEqual(40f, t.spawn.chance);
            Assert.AreEqual(1, t.docking.pads.Count);
            Assert.AreEqual(new Vector3(0, 1000 + ModNpcShips.PadApproachUnits, 5000), t.docking.pads[0].approach, "the approach above the pad");
            Assert.IsTrue(t.Killable && t.NeedsCapitalShip);

            SetXp(105);   // rank 5
            var db = Database.Load();
            var list = new System.Collections.Generic.List<SpawnSpec>();
            int at = TrafficPlan.AddNpcType(db, list, t, 0, new Vector3(0, 0, 100000));
            var host = list[at];
            Assert.AreEqual("sn_carrier_terran_1", host.fixedObject, "the carrier's hull");
            Assert.AreEqual(1, host.race, "its own race");
            Assert.AreSame(t, host.npcType);
            Assert.AreEqual("Cruiser", host.name);
            Assert.IsTrue(host.capitalEnhanced);
            Assert.AreEqual(ObjectDocking.Resupply, host.dockingType);
            Assert.AreEqual(SpacePoints.NpcPadBase + t.index, host.spacePoints);
            Assert.AreEqual(NpcTables.Hull(1, 14) * 5, host.hitpoints, "killable, like the enhanced battleship");
            Assert.AreEqual(113, host.lootItem);
            Assert.AreEqual(4, host.lootAmount);
            Assert.IsNull(host.extraLoot, "a 0 % entry never comes");
            var turrets = list.Where(s => s.group == NpcGroup.Turret).ToList();
            Assert.AreEqual(TrafficPlan.CarrierTurrets.Length, turrets.Count, "the carrier's turrets");
            Assert.IsTrue(turrets.All(s => s.capitalHost == host && s.race == 1));
            var escorts = list.Where(s => s.group == NpcGroup.Escort).ToList();
            Assert.AreEqual(3, escorts.Count);
            Assert.IsTrue(escorts.All(s => s.ship == 5 && s.gunFactor == 2f && s.capitalHost == host && s.route != null));
            Assert.AreEqual((int)(NpcTables.Hull(0, 5) * 2f), escorts[0].hitpoints);

            Assert.IsTrue(CapitalShips.HasDeck(host));
            Assert.AreEqual(9, CapitalShips.LaunchShip(host));
            Assert.AreEqual(2, CapitalShips.PerWave(host));
            Assert.AreEqual(4, CapitalShips.LaunchTotal(host));
            Assert.AreEqual(10000f, CapitalShips.LaunchGapMs(host));
            Assert.AreEqual(SpacePoints.NpcPadBase + t.index, CapitalShips.PadSet(host));
            var pads = SpacePoints.Set(host.spacePoints);
            Assert.AreEqual(1, pads.Count(p => p.type == SpacePoints.Approach));
            Assert.AreEqual(new Vector3(0, 1000, 5000), pads.First(p => p.type == SpacePoints.Dock).engine);

            // Its shop: only its wares (gear 0), at its markup.
            var store = new CapitalShips.Store(1, t.docking);
            CollectionAssert.AreEqual(new[] { 37 }, store.Gear(db));
            Assert.AreEqual(7, store.Left(db, 37));
            Assert.AreEqual(3, store.Left(db, GalaxyMap.EnergyCellItem));
            Assert.AreEqual(2f, store.Markup);
        }

        [Test]
        public void ItsSpawnRulesHold()
        {
            ModOn(Cruiser);
            var t = ModNpcShips.Get(Mod + ":cruiser");
            var db = Database.Load();
            var vossk = db.Systems.First(s => s.raceId == 1);
            SetXp(105);   // rank 5
            Assert.IsTrue(TrafficPlan.NpcSpawnHolds(t, vossk.stations[0], vossk, 1));
            Assert.IsFalse(TrafficPlan.NpcSpawnHolds(t, 0, db.Systems.First(s => s.raceId == 0), 0), "not its races");
            SetXp(7);   // rank 1
            Assert.IsFalse(TrafficPlan.NpcSpawnHolds(t, vossk.stations[0], vossk, 1), "below its rank");
        }

        [Test]
        public void ChangesToTheGamesOwn()
        {
            ModOn(@"[
                { 'override': 'battleship', 'name': 'Dreadful', 'spawn': { 'chance': 0 },
                  'hardpoints': [ { 'position': [ 0, 3000, 0 ] }, { 'position': [ 0, -3000, 0 ], 'upsideDown': true } ] },
                { 'override': 'freighter', 'speed': 2.5, 'hullFactor': 2, 'spawn': { 'count': [ 1, 1 ] } } ]");
            Assert.IsEmpty(Installed(Mod).Warnings, AllMessages(Installed(Mod)));
            Assert.AreEqual(0, ModNpcShips.Chance(ModNpcShips.Battleship, 30));
            Assert.AreEqual(30, ModNpcShips.Chance(ModNpcShips.Carrier, 30), "untouched");
            Assert.IsNull(ModNpcShips.Changed(ModNpcShips.Carrier));
            var db = Database.Load();
            var list = new System.Collections.Generic.List<SpawnSpec>();
            TrafficPlan.AddCapitalShipAt(db, list, SpawnSpec.CapitalBattleship, new Vector3(0, 0, 100000), false);
            Assert.AreEqual(1, list.Count, "its hardpoints replace the game's 7 turrets");
            Assert.AreEqual("Dreadful", list[0].name);
            Assert.AreEqual(2, list[0].npcType.hardpoints.Count);
            Assert.AreEqual(Vector3.down, list[0].npcType.hardpoints[1].up);
            Assert.AreEqual(-1, list[0].npcType.hardpoints[0].item, "the game's turret");

            var f = ModNpcShips.Changed(ModNpcShips.FreighterKey);
            var freighters = new System.Collections.Generic.List<SpawnSpec> { new SpawnSpec { group = NpcGroup.Freighter, race = 0, ship = 15, freighter = true } };
            TrafficPlan.ApplyNpcType(db, freighters, 0, f, true);
            Assert.AreEqual(2.5f, freighters[0].speed);
            Assert.AreEqual(NpcTables.Hull(1, 15) * 2, freighters[0].hitpoints);
        }

        [Test]
        public void AHandedOverShipKeepsItsType()
        {
            ModOn(Cruiser);
            var t = ModNpcShips.Get(Mod + ":cruiser");
            var back = ProxySpec.Decode(new ProxySpec { model = "m", spec = new SpawnSpec { ship = 15, npcType = t } }.Encode());
            Assert.AreSame(t, back.spec.npcType);
            Assert.IsNull(ProxySpec.Decode(new ProxySpec { model = "m", spec = new SpawnSpec { ship = 3 } }.Encode()).spec.npcType);
        }

        [Test]
        public void ABadKindBreaksTheModAndUnknownFieldsWarn()
        {
            ModOn(@"[ { 'id': 'x', 'kind': 'dragon' } ]");
            Assert.IsTrue(Installed(Mod).Broken);
            ModOn(@"[ { 'id': 'y', 'kind': 'freighter', 'wings': 2, 'spawn': { 'chance': 5, 'often': 1 } }, { 'override': 'nothing' } ]");
            Assert.IsFalse(Installed(Mod).Broken, AllMessages(Installed(Mod)));
            ModNpcShips.All();
            var w = string.Join("\n", Installed(Mod).Warnings);
            StringAssert.Contains("\"wings\"", w);
            StringAssert.Contains("\"often\"", w);
            StringAssert.Contains("\"nothing\"", w);
        }
    }
}
