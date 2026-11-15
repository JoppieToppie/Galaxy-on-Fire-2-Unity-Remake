// ModScannerTests.cs
// Scanner readouts from Joppie's upstream scanner work combined with the project's class scanner (143).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModScannerTests : SavesModsTest
    {
        static List<ItemStack> Hold => new List<ItemStack> { new ItemStack(0, 0), new ItemStack(105, 5), new ItemStack(106, 2) };

        [Test]
        public void ScannerStatsKeepTheirAgreedNumbers()
        {
            Assert.AreEqual(CombatRadar.ScanShipAttr, ItemStats.IdOf("scanShip"));
            Assert.AreEqual(CombatRadar.ScanHitpointsAttr, ItemStats.IdOf("scanHitpoints"));
            Assert.AreEqual(CombatRadar.ScanWeaponsAttr, ItemStats.IdOf("scanWeapons"));
            Assert.AreEqual(143, ItemStats.IdOf("scanClass"));
            WriteMod("scan", "t_scan", "1.0", "", ("items.json", @"[
                { 'id': 'deep', 'base': 84, 'name': 'Deep Scan', 'stats': { 'scanShip': 1, 'scanHitpoints': 1, 'scanWeapons': 1, 'radarShowsCargo': 2, 'scanClass': 1 } }
            ]"));
            ScanAndEnable("t_scan");
            Assert.IsFalse(Installed("t_scan").Broken, AllMessages(Installed("t_scan")));
            var deep = Database.Load().Item(ModContent.ItemIndexOf("t_scan:deep"));
            Assert.AreEqual(1, deep.Attr(140));
            Assert.AreEqual(1, deep.Attr(141));
            Assert.AreEqual(1, deep.Attr(142));
            Assert.AreEqual(1, deep.Attr(143));
            Assert.AreEqual(2, deep.Attr(31));
        }

        [Test]
        public void OriginalCargoReadoutRemainsTheFirstEntry()
        {
            Assert.AreEqual($"5t {GameNames.Item(105)}", CombatRadar.ScanReadout(1, Hold, null, null));
            Assert.AreEqual(Localization.Get(542), CombatRadar.ScanReadout(1, null, null, null));
            Assert.IsNull(CombatRadar.ScanReadout(1, new List<ItemStack> { new ItemStack(105, 0) }, null, null));
            Assert.IsNull(CombatRadar.ScanReadout(0, Hold, null, null));
        }

        [Test]
        public void ModScannerCombinesWholeHoldHitpointsWeaponsAndClass()
        {
            Assert.AreEqual($"5t {GameNames.Item(105)}, 2t {GameNames.Item(106)}", CombatRadar.ScanReadout(2, Hold, null, null));
            var hp = new Hitpoints(300, 80, 40) { shield = 39.2f, hull = 250 };
            string hull = string.Format(Localization.Extra("scanHull", "Hull {0}/{1}"), 250, 300);
            string all = CombatRadar.ScanReadout(0, null, hp, new[] { 3, -1 }, "Light Fighter");
            Assert.That(all, Does.Contain("Light Fighter"));
            Assert.That(all, Does.Contain(hull));
            Assert.That(all, Does.Contain(GameNames.Item(3)));
            Assert.AreEqual(string.Format(Localization.Extra("scanWeapons", "Weapons: {0}"), GameNames.Item(3)),
                            CombatRadar.ScanReadout(0, null, null, new[] { 3, 3 }));
        }

        [Test]
        public void LockPlateNameIsTheShipsOwnOrItsRace()
        {
            var go = new UnityEngine.GameObject("scanned");
            try
            {
                var t = go.AddComponent<Target>();
                t.race = 8;
                Assert.AreEqual(GameNames.Race(8), CombatRadar.PlateName(t));
                t.displayName = "Red Baron";
                Assert.AreEqual("Red Baron", CombatRadar.PlateName(t));
                Assert.AreEqual("", CombatRadar.PlateName(null));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
