// ModFlightRulesTests.cs
// Feature: CLAUDE.md "Mods" > Flight rules (Flight.FlightRules, ModFlight: a mod's flight.json for every ship, ships.json
// "flight": { "rules" } for one). Expected values: the numbers written into the mod, the ship's winning over flight.json,
// a value past its [Range] clamped to it, and FlightRules.Default untouched without a mod.

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModFlightRulesTests : SavesModsTest
    {
        const string Mod = "t_flightrules";

        [Test]
        public void FlightJsonAndAShipsRulesReachTheFlightModel()
        {
            Assert.AreSame(FlightRules.Default, Database.BuildFlightStats(Database.Load().Ship(0), null).rules, "no mod: the defaults");

            string dir = WriteMod("flightrules", Mod, "1.0", "",
                ("flight.json", "{ 'baseSpeed': 3, 'freeTurnRateDeg': 5000, 'truncateBoostSpeed': false, 'nope': 1 }"),
                ("ships.json", "[ { 'id': 'tank', 'model': 't.glb', 'name': 'Tank', 'price': 80000, 'armor': 100, 'cargo': 30, "
                             + "'slots': { 'primary': 2, 'equipment': 3 }, 'flight': { 'rules': { 'baseSpeed': 4, 'dodgeDurationMs': 600 } } } ]"));
            WriteFile(dir, "t.glb", Dummy);
            ScanAndEnable(Mod);
            var mod = Installed(Mod);
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            Assert.IsTrue(mod.Warnings.Any(w => w.Contains("unknown flight rule \"nope\"")), AllMessages(mod));
            Assert.IsTrue(mod.Warnings.Any(w => w.Contains("freeTurnRateDeg") && w.Contains("clamped")), AllMessages(mod));

            var db = Database.Load();
            var other = Database.BuildFlightStats(db.Ship(0), null).rules;
            Assert.AreEqual(3f, other.baseSpeed, "flight.json: every ship");
            Assert.AreEqual(2000f, other.freeTurnRateDeg, "clamped to its range");
            Assert.IsFalse(other.truncateBoostSpeed);
            Assert.AreEqual(FlightRules.Default.turnRampBase, other.turnRampBase, "the rest the game's");

            var tank = Database.BuildFlightStats(db.Ships.First(s => s.name == "Tank"), null);
            Assert.AreEqual(4f, tank.rules.baseSpeed, "the ship's own wins");
            Assert.AreEqual(2000f, tank.rules.freeTurnRateDeg, "flight.json under it");
            var model = new FlightModel();
            model.Configure(tank);
            Assert.AreEqual(4f, model.TopSpeed);
            Assert.AreEqual(2f, FlightRules.Default.baseSpeed, "the defaults never change");

            var dodge = new Maneuver { durationMs = tank.rules.dodgeDurationMs };
            dodge.Start(1);
            dodge.Step(599f, 20f, out _, out _, out _);
            Assert.IsTrue(dodge.Active);
            dodge.Step(1f, 20f, out _, out _, out _);
            Assert.IsFalse(dodge.Active, "the ship's 600 ms dodge");
        }
    }
}
