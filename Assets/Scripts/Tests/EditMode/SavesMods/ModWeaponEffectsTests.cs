// ModWeaponEffectsTests.cs
// Feature: Modding/README.md "Weapon effects" (weaponfx.json, ModWeapons.Effects / Defs): a weapon effect of the mod's own,
// which items take with "fx": "<id>" (or another mod's "<mod id>:<id>"), a game weapon's whole look with "fx": <number>,
// the Mod Builder's project list of them (ModProject.WeaponEffects: saved, read back, removed with the items' links) and the
// builder project's effects built on their own for the range. Only the parsing: the models aren't loaded here.

using System.IO;
using System.Linq;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModWeaponEffectsTests : SavesModsTest
    {
        const string Mod = "t_fxlib";

        [TearDown]
        public void CloseProject() => ModManager.SetBuilderProject(null);

        ModWeapons.Def[] DefsOf(string effects, string items)
        {
            WriteMod("fxlib", Mod, "1.0", "", ("weaponfx.json", effects), ("items.json", items));
            ScanAndEnable(Mod);
            return ModWeapons.Defs().ToArray();
        }

        [Test]
        public void AnItemTakesAWeaponEffect()
        {
            var defs = DefsOf("[ { 'id': 'blue_bolt', 'name': 'Blue bolt', 'base': 0, 'projectile': { 'color': [0, 0, 1] } } ]",
                              "[ { 'id': 'gun', 'base': 1, 'name': 'Gun', 'fx': 'blue_bolt' } ]");
            var d = defs.Single();
            Assert.AreEqual(0, d.look, "what the effect leaves out is its base weapon's");
            Assert.IsNotNull(d.projectile);
            Assert.IsTrue(d.projectile.recolor);
            Assert.IsNull(d.muzzle);
            Assert.IsNull(d.effect, "an item's, not the builder's preview");
            Assert.IsEmpty(Installed(Mod).Warnings, AllMessages(Installed(Mod)));
        }

        [Test]
        public void AnotherModsEffectByItsKey()
        {
            var defs = DefsOf("[ { 'id': 'red', 'base': 2, 'impact': false } ]", "[ { 'id': 'gun', 'base': 1, 'fx': 't_fxlib:red' } ]");
            Assert.AreEqual(2, defs.Single().look);
            Assert.IsTrue(defs.Single().impact.remove);
        }

        [Test]
        public void AMissingEffectIsRefused()
        {
            Assert.IsEmpty(DefsOf("[ ]", "[ { 'id': 'gun', 'base': 1, 'fx': 'nope' } ]"));
            StringAssert.Contains("no weapon effect \"nope\"", AllMessages(Installed(Mod)));
        }

        [Test]
        public void AGameWeaponsWholeLookByItsNumber()
        {
            var d = DefsOf("[ ]", "[ { 'id': 'gun', 'base': 1, 'fx': 12 } ]").Single();
            Assert.AreEqual(12, d.look);
            Assert.IsNull(d.projectile);
            Assert.IsNull(d.shots);
            Assert.IsEmpty(DefsOf("[ ]", "[ { 'id': 'gun', 'base': 1, 'fx': 99999 } ]"));
            StringAssert.Contains("there is no item 99999", AllMessages(Installed(Mod)));
        }

        [Test]
        public void TheEffectsOwnFieldsAreNoUnknownFields()
        {
            DefsOf("[ { 'id': 'e', 'name': 'E', 'base': 0, 'sparkle': 1 } ]", "[ { 'id': 'gun', 'base': 1, 'fx': 'e' } ]");
            var messages = AllMessages(Installed(Mod));
            StringAssert.Contains("weaponfx.json", messages);
            StringAssert.Contains("\"sparkle\"", messages);
            StringAssert.DoesNotContain("\"name\"", messages);
            StringAssert.DoesNotContain("\"base\"", messages);
        }

        [Test]
        public void TheProjectSavesItsEffectsAndRemovesTheLinks()
        {
            var p = ModProject.Create("Effect test", "tests");
            var e = p.AddWeaponEffect(0, "Blue bolt");
            var it = p.AddItem(1, "Gun");
            it.EffectId = e.Id;
            var other = p.AddItem(2, "Other");
            other.GameLook = 12;
            p.Save();
            Assert.IsTrue(File.Exists(Path.Combine(p.Folder, ModContent.WeaponEffectsFile)));

            var back = ModProject.Load(p.Folder);
            var be = back.WeaponEffects.Single();
            Assert.AreEqual("blue_bolt", be.Id);
            Assert.AreEqual(0, be.Base);
            Assert.AreEqual("blue_bolt", back.Items.First(i => i.LocalId == it.LocalId).EffectId);
            Assert.AreEqual(12, back.Items.First(i => i.LocalId == other.LocalId).GameLook);

            back.RemoveWeaponEffect(be);
            Assert.IsEmpty(back.WeaponEffects);
            Assert.IsNull(back.Items.First(i => i.LocalId == it.LocalId).Get("fx"), "the item looks like its base again");
            Assert.AreEqual(12, back.Items.First(i => i.LocalId == other.LocalId).GameLook, "a game look stays");
        }

        [Test]
        public void TheBuilderProjectsEffectsAreBuiltOnTheirOwn()
        {
            var p = ModProject.Create("Range test", "tests");
            var e = p.AddWeaponEffect(5, "Unused look");
            p.Save();
            ModManager.SetBuilderProject(p.Folder);
            var d = ModWeapons.Defs().Single();
            Assert.AreEqual(p.Key(e.Id), d.effect);
            Assert.AreEqual(5, d.item, "fired on its base weapon");
            Assert.AreEqual(5, d.look);
        }
    }
}
