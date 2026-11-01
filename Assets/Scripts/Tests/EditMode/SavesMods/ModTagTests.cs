// ModTagTests.cs
// Feature: docs/notes/mods.md "Mod tags" (TorchOP's request, 2026-10-23): one tag per mod (mod.json "tag", a string or
// per language), shown dim after the names of everything the mod adds ("Veteran [GoF3]"): its new ships, items, systems
// and stations, never what it only overrides (ModContent.TagOf, GameNames.*Tag, UI.ItemInfo.WithTag).

using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModTagTests : SavesModsTest
    {
        void WriteTagged(string tag) =>
            WriteMod("t", "t_tag", "1.0", tag,
                     ("ships.json", "[ { 'id': 'veteran', 'name': 'Veteran', 'model': 'gof2:ship_005_terran' }, { 'override': '3', 'armor': 200 } ]"),
                     ("items.json", "[ { 'id': 'gun', 'base': 2, 'name': 'Gun' }, { 'override': '4', 'price': 900 } ]"),
                     ("systems.json", "[ { 'id': 'home', 'name': 'Home', 'race': 2, 'position': [50, 50, 50] } ]"),
                     ("stations.json", "[ { 'id': 'port', 'system': 't_tag:home', 'name': 'Port' } ]"));

        [Test]
        public void EverythingTheModAddsCarriesItsTag()
        {
            WriteTagged(", 'tag': 'GoF3'");
            ScanAndEnable("t_tag");
            Assert.IsEmpty(Installed("t_tag").Errors, AllMessages(Installed("t_tag")));
            Database.Load();
            Assert.AreEqual("GoF3", GameNames.ShipTag(ModContent.ShipIndexOf("t_tag:veteran")));
            Assert.AreEqual("GoF3", GameNames.ItemTag(ModContent.ItemIndexOf("t_tag:gun")));
            Assert.AreEqual("GoF3", GameNames.SystemTag(ModWorld.SystemIndexOf("t_tag:home")));
            Assert.AreEqual("GoF3", GameNames.StationTag(ModWorld.StationIndexOf("t_tag:port")));
            Assert.IsNull(GameNames.ShipTag(3), "an original ship it only changes");
            Assert.IsNull(GameNames.ItemTag(4), "an original item it only changes");
            Assert.IsNull(GameNames.SystemTag(0));
            Assert.IsNull(GameNames.StationTag(0));
            Assert.AreEqual("Veteran", GameNames.Ship(ModContent.ShipIndexOf("t_tag:veteran")), "the name itself stays plain");
        }

        [Test]
        public void NoTagNoneAndPerLanguage()
        {
            WriteTagged("");
            ScanAndEnable("t_tag");
            Database.Load();
            Assert.IsNull(GameNames.ShipTag(ModContent.ShipIndexOf("t_tag:veteran")), "a mod without one");

            WriteTagged(", 'tag': { 'en': 'Star Wars', 'de': 'Sternenkriege' }");
            ModManager.Scan();
            Database.Load();
            Assert.AreEqual(Localization.Language == "de" ? "Sternenkriege" : "Star Wars", GameNames.ItemTag(ModContent.ItemIndexOf("t_tag:gun")));
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
