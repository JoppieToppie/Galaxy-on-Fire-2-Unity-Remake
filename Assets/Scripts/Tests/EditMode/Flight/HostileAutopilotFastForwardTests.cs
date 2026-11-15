// CLAUDE.md "Navigation": opting in waives only hostile contacts, on single-player autopilot.
using GoF2Remake.Data;
using GoF2Remake.Flight;
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
    }
}
