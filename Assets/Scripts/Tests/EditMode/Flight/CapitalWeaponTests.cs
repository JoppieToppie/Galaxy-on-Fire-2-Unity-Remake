// CapitalWeaponTests.cs
// Feature: Modding/README.md "Item stats" (beamRadius, heatShots, heatCoolMs, overheatMs), "Devices" (22 Shield Overcharge)
// and ships.json "hullRegen": Chorus's player capital ship (its Death Ray, BP_Player_Weapon_DeathRay_PlayerCapitalShip: a
// held beam 200 m across that overheats; its Shield Overcharge; its PlayerHealthComponent HealthRegenRate). Expected: a wide
// beam hits every target near its line once, the one it met included, and leaves out what BeamSweeps refuses; a gun
// overheats at heatShots, waits out overheatMs and is cool again; an overcharged shield stops every hit; the hull mends by
// its points a second, the fractions carried over.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class CapitalWeaponTests
    {
        readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void CleanUp()
        {
            foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        /// <summary>An item with the given attribute pairs (attr, value, attr, value...).</summary>
        static ItemData Item(int index, int category, params int[] pairs)
        {
            var keys = new int[pairs.Length / 2];
            var values = new int[pairs.Length / 2];
            for (int i = 0; i < keys.Length; i++) { keys[i] = pairs[2 * i]; values[i] = pairs[2 * i + 1]; }
            return new ItemData { index = index, categoryId = category, type = "primary", attrKeys = keys, attrValues = values };
        }

        Target At(Vector3 p, string name)
        {
            var go = new GameObject(name);
            made.Add(go);
            go.transform.position = p;
            var t = go.AddComponent<Target>();
            t.radius = 10f;
            return t;
        }

        [Test]
        public void AWideBeamHitsEveryTargetNearItsLineOnce()
        {
            // The M6 A1's look (9, a beam) with a 2000-unit (100 m) radius: shot straight ahead, nothing aimed.
            var gun = new Gun(Item(9, 0, 9, 15, 11, 50, 12, 60, Gun.BeamRadiusAttr, 2000), Vector3.zero, false);
            Assert.IsTrue(gun.isBeam);
            Assert.AreEqual(2000f, gun.beamRadius);
            var near = At(new Vector3(60f, 0f, 500f), "near");
            var onLine = At(new Vector3(0f, 0f, 900f), "on the line");
            var far = At(new Vector3(300f, 0f, 500f), "far off the line");
            var friend = At(new Vector3(-40f, 0f, 700f), "friend");
            var targets = new List<Target> { near, onLine, far, friend };
            gun.BeamSweeps = t => t != friend;
            var hits = new Dictionary<Target, int>();
            gun.Hit += (_, t, __) => hits[t] = hits.TryGetValue(t, out var n) ? n + 1 : 1;
            gun.AutoAim = () => null;
            Assert.AreEqual(0, gun.TryFire(Vector3.zero, Quaternion.identity, false));
            gun.Update(16f, targets, null);
            Assert.AreEqual(1, hits.GetValueOrDefault(near), "within 100 m and its size of the line");
            Assert.AreEqual(1, hits.GetValueOrDefault(onLine), "on the line, once");
            Assert.AreEqual(0, hits.GetValueOrDefault(far), "300 m off");
            Assert.AreEqual(0, hits.GetValueOrDefault(friend), "refused by BeamSweeps");

            Assert.AreEqual(Gun.BeamRangeUnits, gun.beamReach, "the beams' reach by default");
            var longer = new Gun(Item(9, 0, 9, 15, 11, 50, 12, 60, Gun.BeamRadiusAttr, 2000, Gun.BeamReachAttr, 60000), Vector3.zero, false);
            var beyond = At(new Vector3(0f, 0f, 2500f), "2.5 km ahead");
            int hitBeyond = 0;
            longer.Hit += (_, t, __) => { if (t == beyond) hitBeyond++; };
            longer.AutoAim = () => null;
            longer.TryFire(Vector3.zero, Quaternion.identity, false);
            Assert.AreEqual(60000f, longer.BeamLengthUnits, "drawn to its reach");
            longer.Update(16f, new List<Target> { beyond }, null);
            Assert.AreEqual(1, hitBeyond, "past the beams' 1.5 km, within its 3 km");

            var plain = new Gun(Item(9, 0, 9, 15, 11, 50, 12, 60), Vector3.zero, false);
            Assert.AreEqual(0f, plain.beamRadius, "the original beams hit only what they meet");
            Assert.IsTrue(Gun.WithinBeam(new Vector3(5f, 0f, 50f), Vector3.zero, Vector3.forward * 100f, 5f));
            Assert.IsFalse(Gun.WithinBeam(new Vector3(0f, 0f, 120f), Vector3.zero, Vector3.forward * 100f, 5f), "past its end");
        }

        [Test]
        public void AGunOverheatsWaitsAndCools()
        {
            var gun = new Gun(Item(0, 1, 9, 10, 11, 50, Gun.HeatShotsAttr, 3, Gun.HeatCoolMsAttr, 3000, Gun.OverheatMsAttr, 1000),
                              Vector3.zero, false);
            int overheated = 0;
            gun.OverheatStarted += _ => overheated++;
            int Fire() { gun.reloadAcc = 1e6f; return gun.TryFire(Vector3.zero, Quaternion.identity, false); }
            Assert.GreaterOrEqual(Fire(), 0);
            Assert.GreaterOrEqual(Fire(), 0);
            Assert.AreEqual(2f / 3f, gun.Heat01, 1e-4f);
            gun.Cool(500f);   // a sixth of the 3 s from full
            Assert.AreEqual(1.5f / 3f, gun.Heat01, 1e-4f);
            Assert.GreaterOrEqual(Fire(), 0);
            Assert.GreaterOrEqual(Fire(), 0);
            Assert.IsTrue(gun.Overheated);
            Assert.AreEqual(1, overheated);
            Assert.AreEqual(-1, Fire(), "no shots while overheated");
            gun.Cool(999f);
            Assert.IsTrue(gun.Overheated);
            gun.Cool(2f);
            Assert.IsFalse(gun.Overheated);
            Assert.AreEqual(0f, gun.Heat01, "cool again");
            Assert.GreaterOrEqual(Fire(), 0);

            var plain = new Gun(Item(0, 1, 9, 10, 11, 50), Vector3.zero, false);
            for (int i = 0; i < 50; i++) { plain.reloadAcc = 1e6f; plain.bullets[i % plain.bullets.Length].timer = -1e9f; plain.TryFire(Vector3.zero, Quaternion.identity, false); }
            Assert.IsFalse(plain.Overheated, "no heatShots: never");
        }

        [Test]
        public void AChargedGunFiresOnlyAfterItsChargeUp()
        {
            var gun = new Gun(Item(0, 1, 9, 10, 11, 50, Gun.ChargeMsAttr, 1000), Vector3.zero, false);
            Assert.IsFalse(gun.Charged);
            gun.Charge(600f, true);
            Assert.AreEqual(0.6f, gun.Charge01, 1e-5f);
            Assert.IsFalse(gun.Charged);
            gun.Charge(500f, true);
            Assert.IsTrue(gun.Charged, "held through its 1 s");
            gun.Charge(16f, false);
            Assert.IsFalse(gun.Charged, "let go: it starts over");
            Assert.IsTrue(new Gun(Item(0, 1, 9, 10), Vector3.zero, false).Charged, "no charge-up");
            Assert.AreEqual(Gun.ChargeMsAttr, GoF2Remake.Modding.ItemStats.IdOf("chargeMs"));
        }

        [Test]
        public void AnOverchargedShieldStopsEveryHit()
        {
            var hp = new Hitpoints(100, 50, 20) { shield = 10f };
            hp.shieldLocked = true;
            Assert.IsFalse(hp.Damage(500));
            Assert.AreEqual(100, hp.hull);
            Assert.AreEqual(20, hp.armor);
            Assert.AreEqual(10f, hp.shield, "the shield doesn't drop");
            Assert.IsTrue(hp.shieldHit);
            hp.shieldLocked = false;
            hp.Damage(30);
            Assert.AreEqual(0f, hp.shield);
            Assert.AreEqual(0, hp.armor, "past the shield again");
        }

        [Test]
        public void TheHullMendsByItsPointsASecond()
        {
            var hp = new Hitpoints(200) { hull = 100 };
            hp.RegenerateHull(100f, 125f);
            Assert.AreEqual(112, hp.hull, "12.5 points: 12, the half carried over");
            hp.RegenerateHull(100f, 125f);
            Assert.AreEqual(125, hp.hull);
            hp.RegenerateHull(10000f, 125f);
            Assert.AreEqual(200, hp.hull, "never past its maximum");
            hp.hull = 0;
            hp.RegenerateHull(1000f, 125f);
            Assert.AreEqual(0, hp.hull, "a lost hull stays lost");
        }

        [Test]
        public void ShipsJsonReadsTheHullRegen()
        {
            Assert.AreEqual(250f, JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\", \"hullRegen\": 250 }").hullRegen);
            Assert.AreEqual(0f, JsonUtility.FromJson<CustomShipData>("{ \"id\": \"x\" }").hullRegen);
            Assert.AreEqual(Device.Type.ShieldOvercharge, (Device.Type)22);
            foreach (var (name, id) in new[] { ("beamRadius", Gun.BeamRadiusAttr), ("heatShots", Gun.HeatShotsAttr),
                                               ("heatCoolMs", Gun.HeatCoolMsAttr), ("overheatMs", Gun.OverheatMsAttr), ("beamReach", Gun.BeamReachAttr) })
                Assert.AreEqual(id, GoF2Remake.Modding.ItemStats.IdOf(name), name);
        }
    }
}
