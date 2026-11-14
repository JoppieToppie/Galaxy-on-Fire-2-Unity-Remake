// ModScannerTests.cs
// Feature: docs/notes/npcs-and-combat.md "Scanner cargo readout" and its remake-mod readouts (a Discord suggestion,
// 2026-11-14): a scanner's "scanShip" (attr 140) names a locked ship's hull on the lock plate, "scanHitpoints" (141) and
// "scanWeapons" (142) add its hitpoints and guns to the readout of a new lock, "radarShowsCargo" (31) 2 reads the whole
// hold (1 the first entry, the original's). Every completed ship lock fires the events' and quests' "on scanned".
// Expected values: the original's readout (Radar::draw with Radar+0x1a5: the first cargo entry, "Nothing to salvage." (542)
// without a cargo list, nothing when every entry is empty); the remake-only attribute numbers (ItemStats).

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
        public void TheScannerStatsHaveTheirNumbers()
        {
            Assert.AreEqual(CombatRadar.ScanShipAttr, ItemStats.IdOf("scanShip"));
            Assert.AreEqual(CombatRadar.ScanHitpointsAttr, ItemStats.IdOf("scanHitpoints"));
            Assert.AreEqual(CombatRadar.ScanWeaponsAttr, ItemStats.IdOf("scanWeapons"));

            WriteMod("scan", "t_scan", "1.0", "", ("items.json", @"[
                { 'id': 'deep', 'base': 84, 'name': 'Deep Scan', 'stats': { 'scanShip': 1, 'scanHitpoints': 1, 'scanWeapons': 1, 'radarShowsCargo': 2 } }
            ]"));
            ScanAndEnable("t_scan");
            Assert.IsFalse(Installed("t_scan").Broken, AllMessages(Installed("t_scan")));
            var deep = Database.Load().Item(ModContent.ItemIndexOf("t_scan:deep"));
            Assert.AreEqual(1, deep.Attr(CombatRadar.ScanShipAttr));
            Assert.AreEqual(1, deep.Attr(CombatRadar.ScanHitpointsAttr));
            Assert.AreEqual(1, deep.Attr(CombatRadar.ScanWeaponsAttr));
            Assert.AreEqual(2, deep.Attr(31));
        }

        [Test]
        public void TheOriginalReadoutIsTheFirstCargoEntry()
        {
            Assert.AreEqual($"5t {GameNames.Item(105)}", CombatRadar.ScanReadout(1, Hold, null, null), "the first entry with units");
            Assert.AreEqual(Localization.Get(542), CombatRadar.ScanReadout(1, null, null, null), "no cargo list");
            Assert.IsNull(CombatRadar.ScanReadout(1, new List<ItemStack> { new ItemStack(105, 0) }, null, null), "every entry empty");
            Assert.IsNull(CombatRadar.ScanReadout(0, Hold, null, null), "a scanner without the readout");
        }

        [Test]
        public void AModScannerReadsTheWholeHoldHitpointsAndWeapons()
        {
            Assert.AreEqual($"5t {GameNames.Item(105)}, 2t {GameNames.Item(106)}", CombatRadar.ScanReadout(2, Hold, null, null));

            var hp = new Hitpoints(300, 80, 40) { shield = 39.2f, hull = 250 };
            string hull = string.Format(Localization.Extra("scanHull", "Hull {0}/{1}"), 250, 300);
            string all = CombatRadar.ScanReadout(0, null, hp, new[] { 3, -1 });
            Assert.AreEqual(string.Join(" · ",
                string.Format(Localization.Extra("scanShield", "Shield {0}/{1}"), 40, 80),
                string.Format(Localization.Extra("scanArmor", "Armor {0}/{1}"), 40, 40),
                hull,
                string.Format(Localization.Extra("scanWeapons", "Weapons: {0}"), GameNames.Item(3))), all);

            // The game's NPCs have a hull only: no shield or armor part; no gun: "Unarmed"; the cargo last.
            Assert.AreEqual(string.Join(" · ", string.Format(Localization.Extra("scanHull", "Hull {0}/{1}"), 300, 300),
                                        Localization.Extra("scanUnarmed", "Unarmed"), Localization.Get(542)),
                            CombatRadar.ScanReadout(1, null, new Hitpoints(300), new[] { -1, -1 }));
            Assert.AreEqual(string.Format(Localization.Extra("scanWeapons", "Weapons: {0}"), GameNames.Item(3)),
                            CombatRadar.ScanReadout(0, null, null, new[] { 3, 3 }), "the same gun named once");
        }

        [Test]
        public void TheLockPlateNameIsTheShipsOwnElseItsRaces()
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
