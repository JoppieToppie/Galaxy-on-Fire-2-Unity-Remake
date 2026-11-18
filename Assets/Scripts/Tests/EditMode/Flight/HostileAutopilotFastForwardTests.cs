// CLAUDE.md "Navigation": opting in waives only hostile contacts, on single-player autopilot.
using GoF2Remake.Data;
using System.Linq;
using GoF2Remake.Flight;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class HostileAutopilotFastForwardTests : StoryAreaTestBase
    {
        [TestCase(false, true, false, false, false, false)]
        [TestCase(true, true, false, false, false, true)]
        [TestCase(true, true, true, false, false, false)]
        [TestCase(true, true, false, true, false, false)]
        [TestCase(true, false, false, false, false, false)]
        [TestCase(true, true, false, false, true, false)]
        public void HostileBypassIsLimitedToDistantSinglePlayerAutopilot(bool enabled, bool autopilot, bool multiplayer,
            bool near, bool cruise, bool expected)
        {
            using var setting = new SettingsScope<bool>(() => Settings.HostileAutopilotFastForward, v => Settings.HostileAutopilotFastForward = v);
            Settings.HostileAutopilotFastForward = enabled;
            using var cruiseSetting = new SettingsScope<bool>(() => Settings.FastForwardCruise, v => Settings.FastForwardCruise = v);
            Settings.FastForwardCruise = cruise;
            NetSession.ActiveProvider = () => multiplayer;
            var go = new GameObject("HostileAutopilotFastForwardTests");
            try
            {
                var nav = go.AddComponent<Navigation>();
                nav.HostilesPresent = true;
                if (autopilot) typeof(Navigation).GetProperty("AutopilotTarget").SetValue(nav, new Navigation.Target());
                typeof(Navigation).GetProperty("AboutToReach").SetValue(nav, near);
                Assert.AreEqual(expected, nav.CanFastForward);
                nav.FastForwardRadioBlocked = true;
                Assert.IsFalse(nav.CanFastForward, "radio/chatter remains blocked even when hostile contacts are allowed");
                nav.FastForwardRadioBlocked = false;
                typeof(Navigation).GetProperty("Jumping").SetValue(nav, true);
                Assert.IsFalse(nav.CanFastForward, "jumping is never waived by this option");
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void HostileTimeSpeedupRemainsEditableInEitherFastForwardMode()
        {
            using var prefs = new DataUiTestKit.PrefsSnapshot();
            var active = NetSession.ActiveProvider;
            try
            {
                NetSession.ActiveProvider = () => false;
                var row = OptionsCatalog.All().Single(o => o.id == "hostileAutopilotFastForward");
                Settings.FastForwardCruise = false;
                Assert.IsTrue(row.enabled());
                row.setBool(true);
                Settings.FastForwardCruise = true;
                Assert.IsTrue(row.enabled(), "Ship cruise only removes the effect, not access to the preference");
                Assert.IsTrue(row.getBool(), "the choice survives mode changes");
                row.setBool(false);
                Settings.FastForwardCruise = false;
                Assert.IsFalse(row.getBool());
                NetSession.ActiveProvider = () => true;
                Assert.IsFalse(row.enabled(), "multiplayer restrictions remain");
            }
            finally { NetSession.ActiveProvider = active; }
        }
    }
}
