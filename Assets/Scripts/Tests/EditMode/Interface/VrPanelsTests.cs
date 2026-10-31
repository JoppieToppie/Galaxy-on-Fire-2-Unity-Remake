// VrPanelsTests.cs
// Feature: CLAUDE.md "VR" > UI (VrPanels): every UI Toolkit panel renders into a texture on a quad of the floating screen.
// #205 (a player's report): the star map opened from the main menu stayed over the menu after it closed, as the map's
// object goes (StarMap.Close) while its PanelSettings asset stays, and its quad kept the last frame. A quad now shows only
// while a renderer of its panel runs.

using GoF2Remake.Vr;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class VrPanelsTests
    {
        GameObject screen, map;
        PanelSettings settings;
        VrPanels panels;

        [SetUp]
        public void Make()
        {
            screen = new GameObject("VR screen");
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "test map overlay";
            map = new GameObject("Star Map");
            map.AddComponent<PanelRenderer>().panelSettings = settings;
            panels = new VrPanels(screen.transform, 0);
        }

        [TearDown]
        public void Drop()
        {
            panels.Release();
            Object.DestroyImmediate(map);
            Object.DestroyImmediate(screen);
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void APanelShowsOnlyWhileItsRendererRuns()
        {
            panels.Update();
            Assert.IsTrue(panels.Shows(settings), "the map open");

            Object.DestroyImmediate(map);   // StarMap.Close
            panels.Update();
            Assert.IsFalse(panels.Shows(settings), "closed: its last frame isn't left on the screen");

            map = new GameObject("Star Map");   // opened again: a new object, the same settings
            map.AddComponent<PanelRenderer>().panelSettings = settings;
            ForceScan();
            panels.Update();
            panels.Update();
            Assert.IsTrue(panels.Shows(settings), "open again");

            map.GetComponent<PanelRenderer>().enabled = false;
            panels.Update();
            Assert.IsFalse(panels.Shows(settings), "a disabled renderer");
        }

        /// <summary>The panels are looked for twice a second: the next Update looks now.</summary>
        void ForceScan() =>
            typeof(VrPanels).GetField("scanTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(panels, 0f);
    }
}
