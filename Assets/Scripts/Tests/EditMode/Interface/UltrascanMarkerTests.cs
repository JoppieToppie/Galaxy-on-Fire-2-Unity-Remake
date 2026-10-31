// UltrascanMarkerTests.cs
// Feature: CLAUDE.md "Mining" > the Ultrascan's class-A letters (MiningView.UpdateMarkers / PlaceMarkers, Radar::draw
// 0x1577de). They share the HUD's navMarkers layer, which NavigationView clears and rebuilds whenever its targets change; a
// player's report (PR #223): after Junk removal / Recovery the "A" marks were gone until the next docking, as the letters
// made before the rebuild were left detached.

using System.Collections.Generic;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class UltrascanMarkerTests
    {
        static readonly List<Vector2> Two = new List<Vector2> { new Vector2(10f, 20f), new Vector2(30f, 40f) };

        [Test]
        public void OneMarkerPerPointTheRestHidden()
        {
            var layer = new VisualElement();
            var markers = new List<VisualElement>();
            MiningView.PlaceMarkers(layer, markers, Two, null);
            Assert.AreEqual(2, markers.Count);
            Assert.AreEqual(2, layer.childCount);
            foreach (var m in markers) Assert.AreEqual(DisplayStyle.Flex, m.style.display.value);
            Assert.AreEqual(30f, markers[1].style.translate.value.x.value);

            MiningView.PlaceMarkers(layer, markers, new List<Vector2> { new Vector2(5f, 5f) }, null);
            Assert.AreEqual(2, markers.Count, "kept for later");
            Assert.AreEqual(DisplayStyle.Flex, markers[0].style.display.value);
            Assert.AreEqual(DisplayStyle.None, markers[1].style.display.value);
        }

        [Test]
        public void MarkersComeBackAfterTheLayerIsRebuilt()
        {
            var layer = new VisualElement();
            var markers = new List<VisualElement>();
            MiningView.PlaceMarkers(layer, markers, Two, null);
            layer.Clear();   // NavigationView.Build: a freelance mission's waypoint went
            layer.Add(new VisualElement { name = "navigation marker" });
            MiningView.PlaceMarkers(layer, markers, Two, null);
            Assert.AreEqual(2, markers.Count, "the same letters, none made twice");
            foreach (var m in markers)
            {
                Assert.AreSame(layer, m.parent, "back in the layer");
                Assert.AreEqual(DisplayStyle.Flex, m.style.display.value);
            }
            Assert.AreEqual(3, layer.childCount);
        }

        [Test]
        public void NoLayerHidesThemAll()
        {
            var layer = new VisualElement();
            var markers = new List<VisualElement>();
            MiningView.PlaceMarkers(layer, markers, Two, null);
            MiningView.PlaceMarkers(null, markers, Two, null);
            foreach (var m in markers) Assert.AreEqual(DisplayStyle.None, m.style.display.value);
        }
    }
}
