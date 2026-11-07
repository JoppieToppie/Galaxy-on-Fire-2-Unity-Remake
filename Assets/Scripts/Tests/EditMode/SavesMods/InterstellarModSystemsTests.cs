// InterstellarModSystemsTests.cs
// Feature: docs/notes/star-map.md "Interstellar space" > Layout with mods: a mod's systems are laid out with the game's
// (InterstellarSpace.Layout, every system with a station), one put right on top of a game system's map place is pushed
// apart like any crowded pair, and its height off the disc comes from its key (mod systems are numbered per player).

using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class InterstellarModSystemsTests : SavesModsTest
    {
        [Test]
        public void AModSystemOnAnothersPlaceIsLaidOutApart()
        {
            var plain = Database.Load();
            var under = plain.Systems.Find(s => InterstellarSpace.ArrivalStation(s) >= 0);
            var p = under.mapPosition;
            string at = string.Format(CultureInfo.InvariantCulture, "[{0}, {1}, {2}]", p.x, p.y, p.z);
            WriteMod("sys", "t_space", "1.0", "", ("systems.json", "[ { 'id': 'twin', 'name': 'Twin', 'race': 0, 'position': " + at + " } ]"),
                     ("stations.json", "[ { 'id': 'port', 'system': 't_space:twin', 'name': 'Port' } ]"));
            ScanAndEnable("t_space");
            var db = Database.Load();
            int twin = ModWorld.SystemIndexOf("t_space:twin");
            var sys = db.Systems.Find(s => s.index == twin);
            Assert.IsNotNull(sys);
            Assert.GreaterOrEqual(InterstellarSpace.ArrivalStation(sys), 0, "it has a station, so it is in the space");
            Assert.AreEqual(GalaxyMap.SunPosition(under), GalaxyMap.SunPosition(sys), "the same place on the map");
            float apart = (InterstellarSpace.Position(db, sys) - InterstellarSpace.Position(db, db.Systems.Find(s => s.index == under.index))).magnitude;
            Assert.Greater(apart, InterstellarSpace.LegUnits * 0.5f, "pushed apart in the space");
            float h = InterstellarSpace.Height(twin);
            Assert.That(h, Is.InRange(-1f, 1f));
            Assert.AreEqual(h, InterstellarSpace.Height(twin), "fixed");
        }
    }
}
