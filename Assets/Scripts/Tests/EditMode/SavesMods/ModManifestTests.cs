// ModManifestTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > A mod / Dependencies, and the format in Modding/README.md and ModManifest.cs:
// mod.json { id (a-z 0-9 _ -, at most 40), name / description (a string or per language), version (default "1.0"),
// author, website, preview (default preview.png), credits, dependencies ["id", "id>=version"] }; mod JSON is read with
// comments and trailing commas allowed, a duplicate field refused, errors naming the file and line, a UTF-8 BOM ignored.
// Versions compare number by number ("1.10" is later than "1.9").

using System.Collections.Generic;
using System.IO;
using System.Text;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModManifestTests : SavesModsTest
    {
        string ModWith(string manifestText, string folder = "m")
        {
            string dir = Path.Combine(Sandbox, "manifests", folder);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), manifestText);
            return dir;
        }

        ModManifest Read(string manifestText)
        {
            using var src = ModSource.Open(ModWith(manifestText));
            return ModManifest.Read(src);
        }

        string Error(string manifestText)
        {
            using var src = ModSource.Open(ModWith(manifestText));
            var e = Assert.Throws<ModJsonException>(() => ModManifest.Read(src));
            return e.Message;
        }

        [TestCase("plasma_arsenal", true)]
        [TestCase("a", true)]
        [TestCase("mod-2_x9", true)]
        [TestCase("0123456789012345678901234567890123456789", true)]   // 40
        [TestCase("01234567890123456789012345678901234567890", false)] // 41
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("Plasma", false)]
        [TestCase("my mod", false)]
        [TestCase("mod.x", false)]
        [TestCase("mod:x", false)]
        [TestCase("mød", false)]
        public void ValidIds(string id, bool valid)
        {
            Assert.AreEqual(valid, ModManifest.ValidId(id));
            Assert.AreEqual(40, ModManifest.MaxIdLength);
        }

        [TestCase("1.10", "1.9", true)]
        [TestCase("1.9", "1.10", false)]
        [TestCase("1.0", "1", true)]
        [TestCase("1", "1.0", true)]
        [TestCase("1.0", "1.0.1", false)]
        [TestCase("2", "1.5", true)]
        [TestCase("1.2.3", "1.2.3", true)]
        [TestCase("10.0", "9.9.9", true)]
        [TestCase("0.9", "1.0", false)]
        [TestCase("1.0", null, true)]
        public void VersionsCompareNumberByNumber(string a, string b, bool atLeast)
        {
            Assert.AreEqual(atLeast, ModManifest.VersionAtLeast(a, b));
        }

        [Test]
        public void EveryFieldIsRead()
        {
            var m = Read(Q(@"{
                'id': 'my_mod', 'name': 'My Mod', 'version': '2.1', 'author': 'Me', 'website': 'https://example.org',
                'description': 'What it adds.', 'preview': 'art/cover.jpg', 'credits': 'Model: someone',
                'dependencies': ['base_pack', 'ship_pack>=1.2', ' spaced >= 3 '],
                'multipleSecondaryMounts': true
            }"));
            Assert.AreEqual("my_mod", m.id);
            Assert.AreEqual("My Mod", m.Name);
            Assert.AreEqual("2.1", m.version);
            Assert.AreEqual("Me", m.author);
            Assert.AreEqual("https://example.org", m.website);
            Assert.AreEqual("What it adds.", m.Description);
            Assert.AreEqual("art/cover.jpg", m.preview);
            Assert.AreEqual("Model: someone", m.Credits);
            CollectionAssert.AreEqual(new[] { "base_pack", "ship_pack", "spaced" }, m.dependencies);
            Assert.AreEqual("1.2", m.dependencyVersions["ship_pack"]);
            Assert.AreEqual("3", m.dependencyVersions["spaced"]);
            Assert.IsFalse(m.dependencyVersions.ContainsKey("base_pack"));
            Assert.IsTrue(m.multipleSecondaryMounts, "the mod explicitly opted in");
        }

        [Test]
        public void DefaultsForTheOptionalFields()
        {
            var m = Read(Q("{ 'id': 'bare' }"));
            Assert.AreEqual("1.0", m.version);
            Assert.AreEqual("preview.png", m.preview);
            Assert.AreEqual("bare", m.Name, "no name: the id");
            Assert.AreEqual("", m.Description);
            Assert.AreEqual("", m.Credits);
            Assert.AreEqual(0, m.dependencies.Count);
            Assert.IsFalse(m.multipleSecondaryMounts, "the default is disabled");
        }

        [Test]
        public void SquadFriendlyFireIsOptIn()
        {
            var off = Read(Q("{ 'id': 'off' }"));
            var on = Read(Q("{ 'id': 'on', 'squadFriendlyFire': true }"));
            Assert.IsFalse(off.squadFriendlyFire);
            Assert.IsTrue(on.squadFriendlyFire);
            Assert.IsFalse(ModContent.HasSquadFriendlyFire(new[] { new ModInfo(off, null) }));
            Assert.IsTrue(ModContent.HasSquadFriendlyFire(new[] { new ModInfo(off, null), new ModInfo(on, null) }));
        }

        [Test]
        public void NpcScannerRangesAreOptionalAndClassSpecific()
        {
            var m = Read(Q("{ 'id': 'sensors', 'npcScannerDetection': true, 'npcScannerRangeUnits': { 'CARRIER': 160000 } }"));
            Assert.IsTrue(m.npcScannerDetection);
            Assert.AreEqual(160000, m.npcScannerRangeUnits["carrier"]);
            Assert.IsFalse(Read(Q("{ 'id': 'legacy' }")).npcScannerDetection);
            Assert.IsEmpty(Read(Q("{ 'id': 'legacy' }")).npcScannerRangeUnits);
        }

        [TestCase("{ 'unknown': 50000 }")]
        [TestCase("{ 'carrier': 0 }")]
        [TestCase("{ 'carrier': -1 }")]
        [TestCase("{ 'carrier': 1.5 }")]
        [TestCase("[]")]
        public void InvalidNpcScannerRangesAreRejected(string map)
        {
            StringAssert.Contains("mod.json", Error(Q("{ 'id': 'bad_npc_sensor', 'npcScannerRangeUnits': " + map + " }")));
        }

        [Test]
        public void ScannerDetectionPercentagesUseCanonicalClassIds()
        {
            var m = Read(Q("{ 'id': 'scanner', 'scannerClassDetectionPercent': { 'STEALTH_FIGHTER': 50, 'carrier': 200 } }"));
            Assert.AreEqual(50, m.scannerClassDetectionPercent["stealth_fighter"]);
            Assert.AreEqual(200, m.scannerClassDetectionPercent["carrier"]);
            Assert.IsEmpty(Read(Q("{ 'id': 'legacy' }")).scannerClassDetectionPercent);
        }

        [TestCase("{ 'unknown': 50 }")]
        [TestCase("{ 'carrier': 0 }")]
        [TestCase("{ 'carrier': 1001 }")]
        [TestCase("{ 'carrier': 1.5 }")]
        [TestCase("[]")]
        public void InvalidScannerDetectionPercentagesAreRejected(string map)
        {
            StringAssert.Contains("mod.json", Error(Q("{ 'id': 'bad_scanner', 'scannerClassDetectionPercent': " + map + " }")));
        }

        [Test]
        public void InvalidMultipleSecondaryMountsFlagIsRejected()
        {
            StringAssert.Contains("\"multipleSecondaryMounts\" must be true or false",
                Error(Q("{ 'id': 'bad_flag', 'multipleSecondaryMounts': 'yes' }")));
        }

        [Test]
        public void MultipleSecondaryMountsOnlyActivatesWhenAnActiveModOptsIn()
        {
            var off = new ModInfo(new ModManifest { id = "off" }, null);
            var on = new ModInfo(new ModManifest { id = "on", multipleSecondaryMounts = true }, null);
            Assert.IsFalse(ModContent.HasMultipleSecondaryMounts(new[] { off }));
            Assert.IsTrue(ModContent.HasMultipleSecondaryMounts(new[] { off, on }));
            Assert.IsFalse(ModContent.HasMultipleSecondaryMounts(null));
        }

        [Test]
        public void TextsPerLanguageFallBackToEnglish()
        {
            var m = Read(Q("{ 'id': 'langs', 'name': { 'en': 'English Name', 'zz': 'Zed' } }"));
            Assert.AreEqual(GoF2Remake.Data.Localization.Language == "zz" ? "Zed" : "English Name", m.Name);
            Assert.AreEqual("only", ModManifest.Pick(new Dictionary<string, string> { ["qq"] = "only" }), "any text before none");
            Assert.AreEqual("plain", ModManifest.Pick(new Dictionary<string, string> { [""] = "plain" }));
            Assert.IsNull(ModManifest.Pick(new Dictionary<string, string>()));
            Assert.IsNull(ModManifest.Pick(null));
        }

        [Test]
        public void TooOldNamesTheVersionNeeded()
        {
            var m = Read(Q("{ 'id': 'child', 'dependencies': ['parent>=2.0', 'other'] }"));
            ModInfo Parent(string v) => new ModInfo(new ModManifest { id = "parent", version = v }, null);
            Assert.AreEqual("2.0", m.TooOld(Parent("1.5")));
            Assert.IsNull(m.TooOld(Parent("2.0")));
            Assert.IsNull(m.TooOld(Parent("10.0")));
            Assert.IsNull(m.TooOld(new ModInfo(new ModManifest { id = "other", version = "0.1" }, null)), "no lowest version given");
            Assert.IsNull(m.TooOld(null));
        }

        [Test]
        public void CommentsAndTrailingCommasAreAllowed()
        {
            var m = Read(Q("// the mod\n{\n  /* its id */ 'id': 'commented', // here\n  'version': '1.1',\n}\n"));
            Assert.AreEqual("commented", m.id);
            Assert.AreEqual("1.1", m.version);
        }

        [Test]
        public void AByteOrderMarkIsIgnored()
        {
            string dir = ModWith("x");
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var text = Encoding.UTF8.GetBytes(Q("{ 'id': 'bom' }"));
            var all = new byte[bom.Length + text.Length];
            bom.CopyTo(all, 0);
            text.CopyTo(all, bom.Length);
            File.WriteAllBytes(Path.Combine(dir, ModManifest.FileName), all);
            using var src = ModSource.Open(dir);
            Assert.AreEqual("bom", ModManifest.Read(src).id);
        }

        [Test]
        public void ASyntaxErrorNamesTheFileAndLine()
        {
            string msg = Error(Q("{\n  'id': 'x',\n  'version': tru\n}"));
            StringAssert.StartsWith("mod.json line 3", msg);
        }

        [Test]
        public void ADuplicateFieldIsAnError()
        {
            StringAssert.StartsWith("mod.json line", Error(Q("{ 'id': 'a',\n 'id': 'b' }")));
        }

        [Test]
        public void TheIdIsRequired()
        {
            StringAssert.Contains("\"id\" is missing", Error(Q("{ 'name': 'Nameless' }")));
        }

        [Test]
        public void AnInvalidIdIsAnError()
        {
            StringAssert.Contains("may only use a-z, 0-9, _ and -", Error(Q("{ 'id': 'Bad Id' }")));
        }

        [Test]
        public void ManifestMustBeAnObject()
        {
            StringAssert.Contains("must be an object", Error("[1, 2]"));
        }
    }
}
