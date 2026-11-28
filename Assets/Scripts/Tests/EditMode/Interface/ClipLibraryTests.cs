// ClipLibraryTests.cs
// Feature: docs/notes/platforms.md "Screen recorder": the clip's size from the settings (the screen's shape, never above the
// screen, multiples of 8), its bit rate, and the clips folder's list, rename and delete with the thumbnail and length.
// Expected values: worked by hand from Settings.RecordHeights / RecordBitsPerPixel and the rules in UI.ClipLibrary.

using System.IO;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ClipLibraryTests : SandboxTest
    {
        [Test]
        public void TheClipKeepsTheScreensShapeAndNeverGrows()
        {
            Assert.AreEqual(new Vector2Int(1920, 1080), ClipLibrary.Size(2560, 1440, 3), "1080p of a 1440p screen");
            Assert.AreEqual(new Vector2Int(2560, 1440), ClipLibrary.Size(2560, 1440, 1), "2160p asked: the screen's own");
            Assert.AreEqual(new Vector2Int(2560, 1440), ClipLibrary.Size(2560, 1440, 0), "Screen");
            Assert.AreEqual(new Vector2Int(1720, 720), ClipLibrary.Size(3440, 1440, 4), "21:9 at 720p");
            Assert.AreEqual(new Vector2Int(1560, 720), ClipLibrary.Size(3120, 1440, 4), "a phone's 19.5:9");
            Assert.AreEqual(new Vector2Int(1360, 768), ClipLibrary.Size(1366, 768, 0), "rounded down to multiples of 8");
        }

        [Test]
        public void TheBitrateFollowsTheQuality()
        {
            Assert.AreEqual(17418, ClipLibrary.BitrateKbps(new Vector2Int(1920, 1080), 60, 2), "1080p60 High: 0.14 bit per pixel per frame");
            Assert.AreEqual(1500, ClipLibrary.BitrateKbps(new Vector2Int(640, 360), 24, 0), "never under 1.5 Mbit/s");
        }

        [Test]
        public void ClipsAreListedRenamedAndDeletedWithTheirThumbnails()
        {
            string first = ClipLibrary.NewPath("My film!");
            StringAssert.StartsWith("My film_", Path.GetFileName(first));
            File.WriteAllBytes(first, new byte[2048]);
            string name = Path.GetFileNameWithoutExtension(first);
            ClipLibrary.SaveInfo(name, 42.5f, new byte[] { 1, 2, 3 });
            Assert.AreNotEqual(first, ClipLibrary.NewPath("My film!"), "a second clip the same second gets its own name");

            var list = ClipLibrary.List();
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(42.5f, list[0].seconds);
            Assert.IsNotNull(list[0].thumbnail);
            Assert.AreEqual("0:42", ClipLibrary.Duration(list[0].seconds));
            Assert.AreEqual("2 KB", ClipLibrary.Bytes(list[0].bytes));

            Assert.IsNull(ClipLibrary.Rename(list[0], "Ambush take 3"));
            list = ClipLibrary.List();
            Assert.AreEqual("Ambush take 3", list[0].name);
            Assert.AreEqual(42.5f, list[0].seconds, "the length moved with it");
            Assert.IsNotNull(list[0].thumbnail, "the thumbnail moved with it");
            Assert.IsNotNull(ClipLibrary.Rename(list[0], "  "), "an empty name is refused");

            Assert.IsTrue(ClipLibrary.Delete(list[0]));
            Assert.AreEqual(0, ClipLibrary.List().Count);
            Assert.IsFalse(File.Exists(Path.Combine(ClipLibrary.ThumbFolder, "Ambush take 3.jpg")));
        }
    }
}
