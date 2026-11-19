// Real map transitions: the shared speed scales both directions of Full Map, with the control below Key.
using System.Collections;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class MapOverviewSpeedTests : PlayModeTest
    {
        [UnityTest]
        public IEnumerator FullMapUsesTheSelectedSpeedBothWays([Values(1, 2, 4)] int speed)
        {
            using var preference = new SettingsScope<int>(() => Settings.SystemMapAnimationSpeed, v => Settings.SystemMapAnimationSpeed = v);
            Settings.SystemMapAnimationSpeed = speed;
            FreePlayAt(78);
            yield return Load("Station");
            var map = StarMap.Open(Db, StarMapMode.Mission, false, _ => { });
            yield return WaitFor(() => Get<GameObject>(map, "world") != null && !Get<bool>(map, "revealing"), 15f, "the map");
            yield return null;
            var root = Get<VisualElement>(map, "root");
            var button = root.Q<Button>("animationSpeedButton");
            var key = root.Q<Button>("keyButton");
            Assert.AreSame(root.Q("mapActionStack"), button.parent, "speed belongs to the bottom-right action stack");
            Assert.GreaterOrEqual(button.worldBound.yMin, key.worldBound.yMax - 1f, "speed stays below Key");
            Assert.AreEqual(key.worldBound.xMax, button.worldBound.xMax, 1f, "right edges stay aligned");
            float expected = 32768f / 15f / 1000f / speed;
            foreach (bool overview in new[] { true, false })
            {
                float started = Time.realtimeSinceStartup;
                Invoke(map, "SetOverview", overview);
                yield return WaitFor(() => Mathf.Approximately(Get<float>(map, "overviewT"), overview ? 1f : 0f), 5f, "Full Map transition");
                float elapsed = Time.realtimeSinceStartup - started;
                Assert.AreEqual(expected, elapsed, Mathf.Max(0.15f, Time.unscaledDeltaTime * 2f), overview ? "enter" : "exit");
            }
        }
    }
}
