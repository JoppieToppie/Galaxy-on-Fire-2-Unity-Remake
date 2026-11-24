// CLAUDE.md "Separate turret control": one contextual action, with a separate auto switch only for mixed mod loadouts.
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ContextualTurretControlTests : SandboxTest
    {
        [TestCase(-1, -1, false)]
        [TestCase(48, -1, false)]
        [TestCase(181, -1, false)]
        [TestCase(48, 181, true)]
        [TestCase(198, 181, true)]
        public void OnlyMixedInstalledTypesExposeTheIndependentAutoBinding(int first, int second, bool mixed)
        {
            var equipment = new List<ItemStack>();
            if (first >= 0) equipment.Add(new ItemStack(first, 1));
            if (second >= 0) equipment.Add(new ItemStack(second, 1));
            Assert.AreEqual(mixed, PlayerTurret.MixedLoadout(Db, equipment));
            var before = Session.Equipment;
            try
            {
                Session.Equipment = equipment;
                Assert.AreEqual(mixed, DataUiTestKit.Row("autoTurret").visible());
            }
            finally { Session.Equipment = before; }
        }

        [Test]
        public void ContextualActionTogglesAnAutomaticTurretWithoutEnteringItsCamera()
        {
            var player = new GameObject("Contextual auto turret test");
            player.SetActive(false);
            try
            {
                var turret = player.AddComponent<PlayerTurret>();
                typeof(PlayerTurret).GetProperty(nameof(PlayerTurret.IsAuto)).SetValue(turret, true);
                var camera = player.AddComponent<FreeLookCamera>();
                Assert.IsTrue(turret.AutoEnabled);
                camera.CycleTurretView();
                Assert.IsFalse(turret.AutoEnabled);
                Assert.IsFalse(turret.InTurretView);
                camera.CycleTurretView();
                Assert.IsTrue(turret.AutoEnabled);
            }
            finally { Object.DestroyImmediate(player); }
        }
    }
}
