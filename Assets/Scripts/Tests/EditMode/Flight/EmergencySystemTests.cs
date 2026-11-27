// EmergencySystemTests.cs
// The emergency system (CLAUDE.md "Combat equipment", Emergency system; item 185): the hull running out sets it to 1
// and the ship is invulnerable for 10 s. Expected: a second fatal hit in the same frame as the save (a laser beam ticking,
// several shooters) is absorbed; before the fix the ship stayed vulnerable until PlayerHealth's next Update, so the item
// was used up and the ship died anyway (Discord bug report "Hitboxes", 2026-10-22).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class EmergencySystemTests : SandboxTest
    {
        [Test]
        public void AFatalHitRightAfterTheSaveIsAbsorbed()
        {
            Session.Equipment.RemoveAll(e => e.item == 185);
            Session.Equipment.Add(new ItemStack(185, 1));
            var go = new GameObject("Player");
            try
            {
                var ship = go.AddComponent<ShipController>();
                var health = go.AddComponent<PlayerHealth>();
                health.Setup(Database.Shared, ship, null, null);

                health.Target.Damage(100000f, byNpc: true);
                Assert.IsTrue(health.Target.Alive, "the first fatal hit is saved");
                Assert.AreEqual(1, health.Hp.hull);
                Assert.IsFalse(Session.Equipment.Exists(e => e.item == 185), "used up");

                health.Target.Damage(100000f, byNpc: true);   // same frame: no Update between
                Assert.IsTrue(health.Target.Alive, "invulnerable from the save on");
                Assert.AreEqual(1, health.Hp.hull);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
