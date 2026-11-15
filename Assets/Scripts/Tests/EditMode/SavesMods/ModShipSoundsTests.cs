// ModShipSoundsTests.cs
// ships.json "sounds" (Modding.ModShipSounds.ParseShip): the slots a ship can fill, including Chorus flight's drift, dodge
// and travel boost (2026-11-11, written by the Chorus importer from Chorus's FMOD events), files resolved in the mod.

using System.Linq;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModShipSoundsTests : SavesModsTest
    {
        [Test]
        public void ChorusFlightsSoundsAreShipSlots()
        {
            string dir = WriteMod("snd", "snd", "1.0", "",
                ("audio/drift_in.ogg", "x"), ("audio/drift.ogg", "x"), ("audio/drift_out.ogg", "x"), ("audio/roll.ogg", "x"),
                ("audio/travel_in.ogg", "x"), ("audio/travel.ogg", "x"), ("audio/travel_out_1.ogg", "x"), ("audio/travel_out_2.ogg", "x"));
            var mod = Open(dir);
            var entry = JObject.Parse(Q(@"{ 'sounds': {
                'driftStart': 'audio/drift_in.ogg', 'driftLoop': { 'file': 'audio/drift.ogg', 'volume': [0.4, 1.0] },
                'driftStop': 'audio/drift_out.ogg', 'dodge': 'audio/roll.ogg', 'travelStart': 'audio/travel_in.ogg',
                'travelLoop': 'audio/travel.ogg', 'travelStop': ['audio/travel_out_1.ogg', 'audio/travel_out_2.ogg'] } }"));
            var s = ModShipSounds.ParseShip(entry, mod, "ships.json");
            Assert.IsEmpty(mod.Warnings, AllMessages(mod));
            Assert.AreEqual("audio/drift_in.ogg", s.driftStart.files.Single());
            Assert.AreEqual(0.4f, s.driftLoop.Volume(0f), 1e-6f);
            Assert.AreEqual(1f, s.driftLoop.Volume(1f), 1e-6f);
            Assert.AreEqual("audio/drift_out.ogg", s.driftStop.files.Single());
            Assert.AreEqual("audio/roll.ogg", s.dodge.files.Single());
            Assert.AreEqual("audio/travel_in.ogg", s.travelStart.files.Single());
            Assert.AreEqual("audio/travel.ogg", s.travelLoop.files.Single());
            Assert.AreEqual(2, s.travelStop.files.Count);
            Assert.AreEqual(13, s.All.Count(), "every slot is preloaded (ModShipSounds.Preload)");
        }

        [Test]
        public void AnUnknownSlotIsAWarning()
        {
            string dir = WriteMod("snd2", "snd2", "1.0", "", ("audio/a.ogg", "x"));
            var mod = Open(dir);
            ModShipSounds.ParseShip(JObject.Parse(Q("{ 'sounds': { 'driftSqueal': 'audio/a.ogg' } }")), mod, "ships.json");
            StringAssert.Contains("driftSqueal", AllMessages(mod));
        }
    }
}
