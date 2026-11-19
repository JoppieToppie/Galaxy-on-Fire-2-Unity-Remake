// ModCatalogTests.cs
// Feature: CLAUDE.md "Mod catalog" (docs/notes/mods.md): the Mod Browser's Online tab reads the community catalog
// (github.com/JoppieToppie/gof2r-mods catalog.json); only links into that repository's releases are fetched, an entry
// without a valid id, version, SHA-256 or size is left out, a catalog of a newer format is refused; an installed version
// is the same, older (an update) or newer than the catalog's (ModManifest.VersionAtLeast); counts read 999 / 1.2k / 12k /
// 1.2M; a download must match the catalog's size and SHA-256.
// Expected values: hand-written catalogs, the SHA-256 of "abc" (FIPS 180-2's example).

using System.IO;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModCatalogTests
    {
        const string Sha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";   // SHA-256("abc")
        const string Rel = ModCatalog.ReleasePrefix + "my_mod-v1.2/";

        static string Mod(string id = "my_mod", string download = Rel + "my_mod.zip", string extra = "") =>
            "{ \"id\": \"" + id + "\", \"version\": \"1.2\", \"author\": \"Ann\", \"name\": { \"en\": \"Mine\", \"de\": \"Meins\" },"
            + " \"description\": \"What it does\", \"size\": 3, \"sha256\": \"" + Sha + "\", \"download\": \"" + download + "\","
            + " \"preview\": \"" + Rel + "my_mod-preview.jpg\", \"thumb\": \"https://example.com/t.jpg\", \"views\": 1234,"
            + " \"downloads\": 56, \"scripts\": true, \"dependencies\": [\"base>=1.0\", \"other\"]" + extra + " }";

        static string Catalog(params string[] mods) =>
            "{ \"format\": 1, \"updated\": \"2026-11-19T04:55:34Z\", \"mods\": [" + string.Join(",", mods) + "] }";

        [Test]
        public void ACatalogEntryIsRead()
        {
            var list = ModCatalog.Parse(Catalog(Mod()), out string error, out string updated);
            Assert.IsNull(error);
            Assert.AreEqual("2026-11-19T04:55:34Z", updated, "kept as written (no date parsing)");
            Assert.AreEqual(1, list.Count);
            var e = list[0];
            Assert.AreEqual("my_mod", e.id);
            Assert.AreEqual("1.2", e.version);
            Assert.AreEqual("Ann", e.author);
            Assert.AreEqual("What it does", e.Description);
            Assert.AreEqual(1234, e.views);
            Assert.AreEqual(56, e.downloads);
            Assert.IsTrue(e.scripts);
            CollectionAssert.AreEqual(new[] { "base", "other" }, e.dependencies);
            Assert.AreEqual("1.0", e.dependencyVersions["base"]);
            Assert.AreEqual(Rel + "my_mod-preview.jpg", e.preview);
            Assert.IsNull(e.thumb, "an image from elsewhere is dropped");
        }

        [Test]
        public void OnlyTheCatalogsReleasesAreFetched()
        {
            Assert.IsTrue(ModCatalog.Trusted(Rel + "my_mod.zip"));
            Assert.IsFalse(ModCatalog.Trusted("https://example.com/my_mod.zip"));
            Assert.IsFalse(ModCatalog.Trusted("https://github.com/someone/else/releases/download/v1/x.zip"));
            Assert.IsFalse(ModCatalog.Trusted(ModCatalog.ReleasePrefix + "../../other/x.zip"));
            Assert.IsFalse(ModCatalog.Trusted(null));
        }

        [Test]
        public void BadEntriesAreLeftOut()
        {
            var list = ModCatalog.Parse(Catalog(
                Mod("Bad Id"),
                Mod("elsewhere", "https://example.com/x.zip"),
                Mod("no_hash", extra: ", \"sha256\": \"12\""),
                Mod("good"),
                Mod("good")), out string error, out _);
            Assert.IsNull(error);
            Assert.AreEqual(1, list.Count, "the bad ones and the second of an id are dropped");
            Assert.AreEqual("good", list[0].id);
        }

        [Test]
        public void ANewerOrBrokenCatalogIsRefused()
        {
            Assert.IsNull(ModCatalog.Parse("{ \"format\": 99, \"mods\": [] }", out string error, out _));
            StringAssert.Contains("update the game", error);
            Assert.IsNull(ModCatalog.Parse("<html>", out error, out _));
            Assert.IsNotNull(error);
            Assert.AreEqual(0, ModCatalog.Parse("{ \"mods\": [] }", out error, out _).Count, "an empty catalog");
        }

        [Test]
        public void TheInstalledVersionAgainstTheCatalogs()
        {
            Assert.AreEqual(ModCatalog.Installed.No, ModCatalog.Compare("1.2", null));
            Assert.AreEqual(ModCatalog.Installed.Same, ModCatalog.Compare("1.2", "1.2"));
            Assert.AreEqual(ModCatalog.Installed.Same, ModCatalog.Compare("1.2", "1.2.0"), "missing parts are 0");
            Assert.AreEqual(ModCatalog.Installed.Older, ModCatalog.Compare("1.10", "1.9"), "an update is out");
            Assert.AreEqual(ModCatalog.Installed.Newer, ModCatalog.Compare("1.2", "2.0"));
        }

        [Test]
        public void CountsAndSizesReadShort()
        {
            Assert.AreEqual("0", ModCatalog.Count(0));
            Assert.AreEqual("999", ModCatalog.Count(999));
            Assert.AreEqual("1.2k", ModCatalog.Count(1234));
            Assert.AreEqual("1k", ModCatalog.Count(1000));
            Assert.AreEqual("12k", ModCatalog.Count(12345));
            Assert.AreEqual("1.2M", ModCatalog.Count(1234567));
            Assert.AreEqual("512 B", ModCatalog.Size(512));
            Assert.AreEqual("160 KB", ModCatalog.Size(160 * 1024 + 5));
            Assert.AreEqual("1.5 MB", ModCatalog.Size(1536 * 1024));
            Assert.AreEqual("120 MB", ModCatalog.Size(120L * 1024 * 1024));
        }

        [Test]
        public void ADownloadMustMatchTheCatalog()
        {
            string path = Path.Combine(Path.GetTempPath(), "gof2_catalog_test.zip");
            try
            {
                File.WriteAllText(path, "abc");
                Assert.IsNull(ModCatalog.Verify(path, 3, Sha));
                Assert.IsNull(ModCatalog.Verify(path, 3, Sha.ToUpperInvariant()), "hex in either case");
                StringAssert.Contains("bytes", ModCatalog.Verify(path, 4, Sha));
                StringAssert.Contains("doesn't match", ModCatalog.Verify(path, 3, new string('0', 64)));
                File.Delete(path);
                Assert.IsNotNull(ModCatalog.Verify(path, 3, Sha));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
