// ModCharactersTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Characters (ModCharacters.cs) and the example Modding/Examples/frontier_systems
// (characters.json: "vega", Captain Vega, terran, female): characters.json [ { id, name (per language), portrait (a PNG
// in the mod), race (terran 0, vossk 1, nivelian 2, midorian 3, pirate, void), gender (male / female), mirrored, frame,
// background, replaces (a story speaker by name or number) } ]; found by "mod:id", by the id, or by a name in any language
// (none when several match).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModCharactersTests : SavesModsTest
    {
        void CharactersOn(string json, string id = "t_chars")
        {
            string dir = WriteMod("ch_" + id, id, "1.0", "", ("characters.json", json));
            WriteFile(dir, "p.png", Dummy);
            ScanAndEnable(id);
        }

        const string Two = @"[
            { 'id': 'vega', 'name': { 'en': 'Captain Vega', 'de': 'Kapitaenin Vega' }, 'portrait': 'p.png', 'race': 'vossk', 'gender': 'female',
              'mirrored': true, 'frame': false, 'background': false, 'replaces': '2' },
            { 'id': 'bob', 'name': 'Bob', 'portrait': 'P.PNG' }
        ]";

        [Test]
        public void PilotSelectionRequiresAnOwnPortraitForModRaces()
        {
            string dir = WriteMod("pilot_races", "t_pilots", "1.0", "",
                ("races.json", "[ { 'id': 'shared', 'name': 'Shared', 'look': 'terran' }, { 'id': 'own', 'name': 'Own', 'look': 'terran' } ]"),
                ("characters.json", "[ { 'id': 'npc', 'portrait': 'p.png', 'race': 'shared' }, { 'id': 'pilot', 'portrait': 'p.png', 'race': 'own', 'playerPortrait': true } ]"));
            WriteFile(dir, "p.png", Dummy);
            ScanAndEnable("t_pilots");
            int shared = ModRaces.Get("t_pilots:shared").race, own = ModRaces.Get("t_pilots:own").race;
            Assert.IsFalse(GoF2Remake.Multiplayer.PlayerIdentity.IsSelectableRace(shared));
            Assert.IsEmpty(GoF2Remake.Multiplayer.PlayerIdentity.Portraits(shared));
            Assert.IsTrue(GoF2Remake.Multiplayer.PlayerIdentity.IsSelectableRace(own));
            CollectionAssert.AreEqual(new[] { "t_pilots:pilot" },
                GoF2Remake.Multiplayer.PlayerIdentity.Portraits(own).Select(p => p.Key).ToArray());
            Assert.IsTrue(GoF2Remake.Multiplayer.PlayerIdentity.TryResolveSelectionPortrait(own, "t_pilots:pilot", out _, out _));
            Assert.IsFalse(GoF2Remake.Multiplayer.PlayerIdentity.TryResolveSelectionPortrait(own, "stock/t_pilots:own/0-0", out _, out _));
            Assert.IsTrue(GoF2Remake.Multiplayer.PlayerIdentity.TryResolvePortrait(own, "stock/t_pilots:own/0-0", out _, out _),
                "Existing saved identities can still be displayed.");
        }

        [Test]
        public void CharactersAreRead()
        {
            CharactersOn(Two);
            Assert.AreEqual(2, ModCharacters.All().Count);
            var v = ModCharacters.Find("t_chars:vega");
            Assert.IsNotNull(v);
            Assert.AreEqual("vega", v.id);
            Assert.AreEqual("t_chars:vega", v.key);
            Assert.AreEqual("p.png", v.portrait);
            Assert.AreEqual(1, v.race);
            Assert.IsTrue(v.hasRace);
            Assert.IsFalse(v.male);
            Assert.IsTrue(v.hasGender);
            Assert.IsTrue(v.mirrored);
            Assert.IsFalse(v.frame);
            Assert.IsFalse(v.background);
            Assert.AreEqual("2", v.replaces);
            Assert.IsTrue(v.HasName);
            var b = ModCharacters.Find("bob");
            Assert.AreEqual(0, b.race, "Terran unless named");
            Assert.IsFalse(b.hasRace, "an omitted race inherits the bar-mission client race");
            Assert.IsTrue(b.male);
            Assert.IsFalse(b.hasGender, "an omitted gender inherits the mission client gender");
            Assert.IsFalse(b.mirrored);
            Assert.IsTrue(b.frame);
            Assert.IsTrue(b.background);
            Assert.IsNull(b.replaces);
            Assert.AreEqual("Bob", b.Name);
        }

        [Test]
        public void ACharacterCanUseItsOwnModsCustomRace()
        {
            string dir = WriteMod("custom_race_chars", "t_custom", "1.0", "",
                ("races.json", "[ { 'id': 'aliens', 'name': 'Aliens', 'visitor': 'models/alien.glb', 'visitorScale': 1.25 } ]"),
                ("characters.json", "[ { 'id': 'pilot', 'portrait': 'p.png', 'race': 'aliens' } ]"));
            WriteFile(dir, "p.png", Dummy);
            WriteFile(dir, "models/alien.glb", Dummy);
            ScanAndEnable("t_custom");

            var race = ModRaces.Get("t_custom:aliens");
            Assert.IsNotNull(race);
            Assert.AreEqual("models/alien.glb", race.visitor);
            Assert.AreEqual(1.25f, race.visitorScale);
            var character = ModCharacters.Find("t_custom:pilot");
            Assert.IsNotNull(character);
            Assert.IsTrue(character.hasRace);
            Assert.AreEqual(race.race, character.race);
        }

        [Test]
        public void ACharacterCanOverrideItsCustomRaceMeshWithABuiltInSilhouette()
        {
            string dir = WriteMod("custom_race_silhouette", "t_custom_sil", "1.0", "",
                ("races.json", "[ { 'id': 'aliens', 'name': 'Aliens', 'visitor': 'models/alien.glb' } ]"),
                ("characters.json", "[ { 'id': 'pilot', 'portrait': 'p.png', 'race': 'aliens', 'silhouette': 'cyborg' } ]"));
            WriteFile(dir, "p.png", Dummy);
            WriteFile(dir, "models/alien.glb", Dummy);
            ScanAndEnable("t_custom_sil");

            var character = ModCharacters.Find("t_custom_sil:pilot");
            Assert.IsNotNull(character);
            Assert.AreEqual(ModRaces.Get("t_custom_sil:aliens").race, character.race);
            Assert.AreEqual("cyborg", character.silhouette);
        }

        [Test]
        public void CharactersAreFoundByKeyIdOrName()
        {
            CharactersOn(Two);
            var v = ModCharacters.Find("t_chars:vega");
            Assert.AreSame(v, ModCharacters.Find("vega"));
            Assert.AreSame(v, ModCharacters.Find("VEGA"));
            Assert.AreSame(v, ModCharacters.Find("Captain Vega"));
            Assert.AreSame(v, ModCharacters.Find("kapitaenin vega"), "a name in any language");
            Assert.AreSame(v, ModCharacters.Find("  vega  "));
            Assert.IsNull(ModCharacters.Find("Nobody"));
            Assert.IsNull(ModCharacters.Find(""));
            Assert.IsNull(ModCharacters.Find(null));
        }

        [Test]
        public void AnIdSharedByTwoModsNeedsItsKey()
        {
            CharactersOn("[ { 'id': 'vega', 'name': 'Vega A', 'portrait': 'p.png' } ]", "t_a");
            CharactersOn("[ { 'id': 'vega', 'name': 'Vega B', 'portrait': 'p.png' } ]", "t_b");
            Assert.IsNull(ModCharacters.Find("vega"));
            Assert.AreEqual("Vega A", ModCharacters.Find("t_a:vega").Name);
            Assert.AreEqual("Vega B", ModCharacters.Find("t_b:vega").Name);
            Assert.AreEqual("Vega B", ModCharacters.Find("Vega B").Name);
        }

        [Test]
        public void ANamelessCharacterIsCalledByItsId()
        {
            CharactersOn("[ { 'id': 'ghost', 'portrait': 'p.png' } ]");
            var g = ModCharacters.Find("ghost");
            Assert.IsFalse(g.HasName);
            Assert.AreEqual("ghost", g.Name);
        }

        [Test]
        public void ACharacterReplacesAStorySpeakerByNumber()
        {
            CharactersOn(Two);
            Assume.That(StoryTable.SpeakerCount, Is.GreaterThan(3));
            Assert.AreSame(ModCharacters.Find("vega"), ModCharacters.ReplacementFor(2));
            Assert.IsNull(ModCharacters.ReplacementFor(3));
            Assert.IsNull(ModCharacters.ReplacementFor(-1));
        }

        [TestCase("terran", 0)]
        [TestCase("Vossk", 1)]
        [TestCase("nivelian", 2)]
        [TestCase("midorian", 3)]
        [TestCase("pirate", 8)]
        [TestCase("void", 9)]
        public void Races(string race, int id)
        {
            CharactersOn("[ { 'id': 'x', 'portrait': 'p.png', 'race': '" + race + "' } ]");
            Assert.AreEqual(id, ModCharacters.Find("x").race);
        }

        [TestCase("terran", StationTables.Visitor.TerranMale)]
        [TestCase("vossk", StationTables.Visitor.Vossk)]
        [TestCase("nivelian", StationTables.Visitor.Nivelian)]
        [TestCase("midorian", StationTables.Visitor.TerranMale)]
        [TestCase("void", StationTables.Visitor.TerranMale)]
        [TestCase("cyborg", StationTables.Visitor.TerranMale)]
        [TestCase("octopod", StationTables.Visitor.Multipod)]
        [TestCase("bobolian", StationTables.Visitor.Bobolan)]
        [TestCase("grey", StationTables.Visitor.Grey)]
        public void BuiltInSilhouettesUseTheOriginalVisitorMeshes(string silhouette, StationTables.Visitor expected)
        {
            Assert.AreEqual(expected, StationTables.VisitorForSilhouette(silhouette, 0, false));
        }

        [Test]
        public void FemaleTerranSilhouetteUsesTheFemaleVisitor()
        {
            Assert.AreEqual(StationTables.Visitor.TerranFemale, StationTables.VisitorForSilhouette("terran", 0, true));
        }

        [Test]
        public void AutomaticSilhouetteKeepsTheOriginalRaceMapping()
        {
            Assert.AreEqual(StationTables.Visitor.Nivelian, StationTables.VisitorForSilhouette(null, 2, false));
        }

        string Warning(string json)
        {
            CharactersOn(json, "t_bad");
            Assert.AreEqual(0, ModCharacters.All().Count);
            return string.Join(" | ", Installed("t_bad").Warnings);
        }

        [Test] public void APortraitIsRequired() => StringAssert.Contains("\"portrait\" (a PNG) is missing", Warning("[ { 'id': 'x' } ]"));
        [Test] public void ThePortraitMustBeInTheMod() => StringAssert.Contains("isn't in the mod", Warning("[ { 'id': 'x', 'portrait': 'none.png' } ]"));
        [Test] public void AnIdIsRequired() => StringAssert.Contains("\"id\" is missing", Warning("[ { 'portrait': 'p.png' } ]"));
        [Test] public void AGenderIsMaleOrFemale() => StringAssert.Contains("male or female", Warning("[ { 'id': 'x', 'portrait': 'p.png', 'gender': 'robot' } ]"));
        [Test] public void AnUnknownRace() => StringAssert.Contains("isn't a game or mod race", Warning("[ { 'id': 'x', 'portrait': 'p.png', 'race': 'klingon' } ]"));
        [Test] public void AnUnknownSilhouette() => StringAssert.Contains("must be terran", Warning("[ { 'id': 'x', 'portrait': 'p.png', 'silhouette': 'klingon' } ]"));
        [Test] public void CharactersMustBeAList() => StringAssert.Contains("must be a list", Warning("{ 'id': 'x' }"));

        [Test]
        public void AnIdIsUsedOnce()
        {
            CharactersOn("[ { 'id': 'x', 'portrait': 'p.png' }, { 'id': 'x', 'portrait': 'p.png' } ]", "t_bad");
            ModCharacters.All();   // characters are read on first use: the mod's warning, the file's characters dropped
            Assert.IsTrue(Installed("t_bad").Warnings.Any(w => w.Contains("used twice")));
        }

        [Test]
        public void TheExampleCharacter()
        {
            CopyExample("frontier_systems");
            ScanAndEnable("frontier_systems");
            var v = ModCharacters.Find("frontier_systems:vega");
            Assert.IsNotNull(v);
            Assert.AreSame(v, ModCharacters.Find("Captain Vega"));
            Assert.AreEqual(0, v.race);
            Assert.IsFalse(v.male);
            Assert.AreEqual("portraits/vega.png", v.portrait);
        }
    }
}
