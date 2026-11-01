// ShipTagTests.cs
// Feature: docs/notes/mods.md "Ship tags" (TorchOP's request, 2026-10-23): a dim tag after a ship's name in the lists
// ("Veteran [GoF3]"): mod.json "shipTag" for the mod's new ships, ships.json "tag" for one ship (new or an override,
// "" for none), a later mod's override winning (ModContent.ShipTag, UI.ItemInfo.TaggedShipName).

using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ShipTagTests : SavesModsTest
    {
        const string Ships = "[ { 'id': 'plain', 'name': 'Plain', 'model': 'gof2:ship_005_terran' }, " +
                             "{ 'id': 'own', 'name': 'Own', 'model': 'gof2:ship_005_terran', 'tag': 'Alliances' }, " +
                             "{ 'id': 'none', 'name': 'None', 'model': 'gof2:ship_005_terran', 'tag': '' }, " +
                             "{ 'override': '3', 'tag': 'HD' } ]";

        [Test]
        public void TheModsTagItsOwnTagAndOverrides()
        {
            WriteMod("t", "t_tag", "1.0", ", 'shipTag': 'GoF3'", ("ships.json", Ships));
            ScanAndEnable("t_tag");
            var mod = Installed("t_tag");
            Assert.IsEmpty(mod.Errors, AllMessages(mod));
            Assert.IsFalse(mod.Warnings.Exists(w => w.Contains("\"tag\"")), "\"tag\" is a known field");
            Database.Load();
            Assert.AreEqual("GoF3", GameNames.ShipTag(ModContent.ShipIndexOf("t_tag:plain")), "the mod's shipTag");
            Assert.AreEqual("Alliances", GameNames.ShipTag(ModContent.ShipIndexOf("t_tag:own")), "its own tag wins");
            Assert.IsNull(GameNames.ShipTag(ModContent.ShipIndexOf("t_tag:none")), "\"\" = none");
            Assert.AreEqual("HD", GameNames.ShipTag(3), "an original ship tagged by an override");
            Assert.IsNull(GameNames.ShipTag(4), "an original ship without one");
        }

        [Test]
        public void ALaterModsOverrideRetagsAModShip()
        {
            WriteMod("t", "t_tag", "1.0", ", 'shipTag': 'GoF3'", ("ships.json", Ships));
            WriteMod("u", "t_tag2", "1.0", ", 'dependencies': ['t_tag']",
                     ("ships.json", "[ { 'override': 't_tag:plain', 'tag': { 'en': 'Star Wars', 'de': 'Sternenkriege' } } ]"));
            ScanAndEnable("t_tag", "t_tag2");
            Assert.IsEmpty(Installed("t_tag2").Errors, AllMessages(Installed("t_tag2")));
            Database.Load();
            Assert.AreEqual(Localization.Language == "de" ? "Sternenkriege" : "Star Wars", GameNames.ShipTag(ModContent.ShipIndexOf("t_tag:plain")));
            Assert.AreEqual("Plain", GameNames.Ship(ModContent.ShipIndexOf("t_tag:plain")), "the name stays");
        }

        [Test]
        public void TheTaggedNameIsTheNameWithADimTag()
        {
            Assert.AreEqual("Veteran", ItemInfo.WithTag("Veteran", null));
            string tagged = ItemInfo.WithTag("Veteran", "<GoF3>");
            StringAssert.StartsWith("Veteran <alpha=", tagged);
            StringAssert.Contains("<noparse>[<GoF3>]</noparse>", tagged, "the tag's own text is never read as markup");
        }
    }
}
