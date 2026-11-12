// ModFlightBonusTests.cs
// Feature: CLAUDE.md "Mods" > Flight styles, the full-stop and low-speed passives (ModFlight: ships.json "flight"
// fullStopDamage / lowSpeedResistance, items' stats 138 / 139, after EVERSPACE 2's ship passives). Expected values: the
// numbers written into the mod, summed as the guide says (a ship's "flight" field plus every mounted item's stat, both
// styles for the item's, the "free" object for free flight only).

using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModFlightBonusTests : SavesModsTest
    {
        const string Mod = "t_flightbonus";

        [Test]
        public void FullStopAndLowSpeedFieldsReachTheShipAndItsItems()
        {
            string dir = WriteMod("flightbonus", Mod, "1.0", "",
                ("items.json", "[ { 'id': 'vanes', 'base': 77, 'name': 'Vanes', 'price': 1000, 'stats': { 'fullStopDamage': 10, 'lowSpeedResistance': 5 } } ]"),
                ("ships.json", "[ { 'id': 'tank', 'model': 't.glb', 'name': 'Tank', 'price': 80000, 'armor': 100, 'cargo': 30, "
                             + "'slots': { 'primary': 2, 'equipment': 3 }, 'flight': { 'fullStopDamage': 25, 'free': { 'lowSpeedResistance': 15 } } } ]"));
            WriteFile(dir, "t.glb", Dummy);
            ScanAndEnable(Mod);
            var mod = Installed(Mod);
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            Assert.IsFalse(mod.Warnings.Any(w => w.Contains("unknown flight field")), AllMessages(mod));

            var db = Database.Load();
            var ship = db.Ships.First(s => s.name == "Tank");
            Assert.AreEqual(25f, ship.flightBoth.fullStopDamage);
            Assert.AreEqual(0f, ship.flightBoth.lowSpeedResistance);
            Assert.AreEqual(15f, ship.flightFree.lowSpeedResistance);
            var item = db.Items.First(i => i.name == "Vanes");
            Assert.AreEqual(10, item.Attr(138));
            Assert.AreEqual(5, item.Attr(139));

            var stats = Database.BuildFlightStats(ship, new[] { item });
            Assert.AreEqual(35f, stats.free.bonus.fullStopDamage, "the ship's 25 + the item's 10");
            Assert.AreEqual(35f, stats.original.bonus.fullStopDamage, "both styles");
            Assert.AreEqual(20f, stats.free.bonus.lowSpeedResistance, "the ship's free-only 15 + the item's 5");
            Assert.AreEqual(5f, stats.original.bonus.lowSpeedResistance, "the item's alone");
        }
    }
}
