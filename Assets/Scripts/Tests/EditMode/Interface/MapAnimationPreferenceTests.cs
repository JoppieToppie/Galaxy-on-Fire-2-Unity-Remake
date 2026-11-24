// CLAUDE.md "System map animation speed": shared preference and selective sound suppression.
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class MapAnimationPreferenceTests : SandboxTest
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
        static void Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);

        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(4, false)]
        public void OnlyAcceleratedZoomSoundsAreSuppressed(int speed, bool zoomDispatched)
        {
            using var preference = new SettingsScope<int>(() => Settings.SystemMapAnimationSpeed, v => Settings.SystemMapAnimationSpeed = v);
            Settings.SystemMapAnimationSpeed = speed;
            var go = new GameObject("Map sound policy"); go.SetActive(false);
            var assets = ScriptableObject.CreateInstance<StarMapAssets>();
            var zoom = AudioClip.Create("Zoom", 64, 1, 8000, false);
            var click = AudioClip.Create("Click", 64, 1, 8000, false);
            try
            {
                assets.zoomIn = assets.zoomOut = zoom;
                var map = go.AddComponent<StarMap>(); Set(map, "assets", assets);
                Call(map, "Play", zoom);
                Assert.AreEqual(zoomDispatched, go.GetComponent<AudioSource>() != null, "zoom reaches the source only at original speed");
                Call(map, "Play", click);
                Assert.IsNotNull(go.GetComponent<AudioSource>(), "ordinary map controls keep their sounds at every speed");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(assets); Object.DestroyImmediate(zoom); Object.DestroyImmediate(click); }
        }

        [Test]
        public void MapButtonCyclesTheSamePreferenceUsedByGameplayAndRefreshesItsLabel()
        {
            using var preference = new SettingsScope<int>(() => Settings.SystemMapAnimationSpeed, v => Settings.SystemMapAnimationSpeed = v);
            Settings.SystemMapAnimationSpeed = 1;
            var go = new GameObject("Map animation preference"); go.SetActive(false);
            var assets = ScriptableObject.CreateInstance<StarMapAssets>();
            try
            {
                var map = go.AddComponent<StarMap>(); Set(map, "assets", assets);
                var button = new Button(); Set(map, "animationSpeedButton", button);
                foreach (int expected in new[] { 2, 4, 1 })
                {
                    Call(map, "CycleAnimationSpeed");
                    Assert.AreEqual(expected, Settings.SystemMapAnimationSpeed);
                    StringAssert.Contains(expected.ToString(), button.text);
                }
                Settings.SystemMapAnimationSpeed = 4;
                Call(map, "RefreshAnimationSpeed");
                StringAssert.Contains("4", button.text, "external Gameplay changes are read from the same preference");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(assets); }
        }
    }
}
