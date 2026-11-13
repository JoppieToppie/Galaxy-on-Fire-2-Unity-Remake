// FarShipMarkersTests.cs
// CLAUDE.md "NPCs and combat" > HUD: the remake's far ship markers (Options > Gameplay "Far ship markers" / "Distance on
// ship markers", FarShipMarkers). Original keeps Radar::draw's dot / ring; the remake's styles leave the ship's middle clear;
// the distance labels are rewritten exactly when Navigation.FormatDistance's text changes.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class FarShipMarkersTests
    {
        [Test]
        public void OriginalKeepsTheDotAndTheLockedRing()
        {
            Assert.AreEqual((FarShipMarkers.Shape.Dot, 1f, 0f), FarShipMarkers.Look(Settings.FarMarkerOriginal, false));
            Assert.AreEqual((FarShipMarkers.Shape.Ring, 1f, 0f), FarShipMarkers.Look(Settings.FarMarkerOriginal, true));
        }

        [Test]
        public void RingAndSmallDotLeaveTheShipInView()
        {
            Assert.AreEqual(FarShipMarkers.Shape.OpenRing, FarShipMarkers.Look(Settings.FarMarkerRing, false).shape);
            var small = FarShipMarkers.Look(Settings.FarMarkerSmallDot, false);
            Assert.AreEqual(FarShipMarkers.Shape.Dot, small.shape);
            Assert.Less(small.scale, 1f);
            // The scaled dot (36 px) ends above the ship's centre.
            Assert.Greater(small.rise - 36f * small.scale / 2f, 0f);
            // Locked: the bracket around the ship (open in the middle), never the original's ring with its dot.
            Assert.AreEqual(FarShipMarkers.Shape.Bracket, FarShipMarkers.Look(Settings.FarMarkerRing, true).shape);
            Assert.AreEqual(FarShipMarkers.Shape.Bracket, FarShipMarkers.Look(Settings.FarMarkerSmallDot, true).shape);
        }

        [Test]
        public void TheLockedShipAlwaysShowsItsDistanceTheOthersWithTheOption()
        {
            Assert.IsTrue(FarShipMarkers.ShowsDistance(true, false));
            Assert.IsFalse(FarShipMarkers.ShowsDistance(false, false));
            Assert.IsTrue(FarShipMarkers.ShowsDistance(false, true));
            Assert.AreEqual(61f, FarShipMarkers.LabelTop(Settings.FarMarkerRing, true));   // the original's place
            foreach (int style in new[] { Settings.FarMarkerOriginal, Settings.FarMarkerRing, Settings.FarMarkerSmallDot })
                Assert.Less(FarShipMarkers.LabelTop(style, false), 61f);
        }

        [Test]
        public void TheDistanceKeyChangesExactlyWhenTheTextDoes()
        {
            // From 1.2 km (the near limit) to 60 km in steps finer than the label's 8 m / 100 m.
            string lastText = null;
            int lastKey = int.MinValue;
            for (float units = 0f; units < 1_200_000f; units += 37f)
            {
                string text = Navigation.FormatDistance(units);
                int key = FarShipMarkers.DistanceKey(units);
                Assert.AreEqual(text != lastText, key != lastKey, $"at {units} units: '{lastText}' -> '{text}', key {lastKey} -> {key}");
                lastText = text;
                lastKey = key;
            }
        }

        [Test]
        public void TheOptionsDefaultToTheOriginal()
        {
            using var prefs = new DataUiTestKit.PrefsSnapshot();
            Settings.Reset(new[] { "farShipMarkers", "markerDistance" });
            Assert.AreEqual(Settings.FarMarkerOriginal, Settings.FarShipMarkers);
            Assert.IsFalse(Settings.MarkerDistance);
            Settings.FarShipMarkers = 9;
            Assert.AreEqual(Settings.FarMarkerSmallDot, Settings.FarShipMarkers);
            CollectionAssert.Contains(Settings.GameplayKeys, "farShipMarkers");
            CollectionAssert.Contains(Settings.GameplayKeys, "markerDistance");
        }

        // The Minimal HUD shows the lock plate again for a new name only (FlightHud.LateUpdate), not for a hull percentage.
        [Test]
        public void PlateNameDropsTheHullPercentage()
        {
            Assert.AreEqual("Terran", FlightHud.PlateName("Terran 85%"));
            Assert.AreEqual(FlightHud.PlateName("Mido Pirate 100%"), FlightHud.PlateName("Mido Pirate 7%"));
            Assert.AreEqual("Hijacker", FlightHud.PlateName("Hijacker"), "a name alone");
            Assert.AreEqual("85%", FlightHud.PlateName("85%"));
            Assert.IsNull(FlightHud.PlateName(null));
        }
    }
}
