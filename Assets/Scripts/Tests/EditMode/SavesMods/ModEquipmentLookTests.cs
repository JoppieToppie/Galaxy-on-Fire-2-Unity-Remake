// ModEquipmentLookTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > The look, and "NPCs and combat" > the tractor beam (CombatRadar): a mod's item
// based on an original sounds and looks like it. The boost sound goes by the booster's look (PlayerEgo::PlayerEgo +0xd4:
// boosters 71-74 -> sounds 0-3 = events 38-41, 195 -> 4 = 1102; any other booster like 71); a mod booster based on 73
// sounds like 73 (it was silent). A tractor beam's "tractorTargets" (attr 122) is how many crates it pulls at once
// (1..8, 1 without it), "tractorRange" (attr 123) how far its extra beams reach; "tractorColor" (attr 136) tints its beams and
// "tractorNoLock" (attr 137) keeps the scanner off crates (a modder's request, 2026-11-02). A weapon's "shieldDamage" (attr 124) is its
// damage to a shield in percent; the Disruptor Laser (183) and items based on it 200 (remake, its description; a players'
// request 2026-10-16), the NPCs' generic guns 100.
// Expected values: the original's table (PlayerEngine's header), the remake-only attribute numbers (ItemStats).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModEquipmentLookTests : SavesModsTest
    {
        Database ModOn()
        {
            WriteMod("eq", "t_eq", "1.0", "", ("items.json", @"[
                { 'id': 'boost', 'base': 73, 'name': 'Boost' },
                { 'id': 'pull', 'base': 69, 'name': 'Pull', 'stats': { 'tractorTargets': 4, 'tractorRange': 50000 } },
                { 'id': 'tint', 'base': 68, 'name': 'Tint', 'tractorColor': '#ff8000', 'stats': { 'tractorNoLock': 1 } },
                { 'id': 'tint2', 'base': 68, 'name': 'Tint 2', 'tractorColor': [0, 128, 255] },
                { 'id': 'many', 'base': 68, 'name': 'Many', 'stats': { 'tractorTargets': 99 } },
                { 'id': 'void', 'base': 183, 'name': 'Void Disruptor', 'fx': { 'projectile': { 'item': 5 }, 'muzzle': { 'item': 5 }, 'impact': { 'item': 5 } } },
                { 'id': 'soft', 'base': 0, 'name': 'Soft', 'stats': { 'shieldDamage': 50 } },
                { 'id': 'hard', 'base': 183, 'name': 'Hard', 'stats': { 'shieldDamage': 300 } }
            ]"));
            ScanAndEnable("t_eq");
            Assert.IsFalse(Installed("t_eq").Broken, AllMessages(Installed("t_eq")));
            return Database.Load();
        }

        [Test]
        public void TheOriginalBoostersSoundsAreKept()
        {
            var db = Database.Load();
            Assert.AreEqual(-1, PlayerEngine.BoostSound(null));
            Assert.AreEqual(0, PlayerEngine.BoostSound(db.Item(71)));
            Assert.AreEqual(3, PlayerEngine.BoostSound(db.Item(74)));
            Assert.AreEqual(4, PlayerEngine.BoostSound(db.Item(195)));
        }

        [Test]
        public void AModBoosterSoundsLikeItsBase()
        {
            var db = ModOn();
            var boost = db.Item(ModContent.ItemIndexOf("t_eq:boost"));
            Assert.AreEqual(73, boost.Look);
            Assert.AreEqual(2, PlayerEngine.BoostSound(boost));
        }

        [Test]
        public void TractorTargetsAndRange()
        {
            var db = ModOn();
            Assert.AreEqual(CombatRadar.TractorTargetsAttr, ItemStats.IdOf("tractorTargets"));
            Assert.AreEqual(CombatRadar.TractorRangeAttr, ItemStats.IdOf("tractorRange"));
            Assert.AreEqual(1, CombatRadar.TractorTargets(db.Item(68)), "the original's one beam");
            Assert.AreEqual(0, CombatRadar.TractorTargets(null));
            var pull = db.Item(ModContent.ItemIndexOf("t_eq:pull"));
            Assert.AreEqual(4, CombatRadar.TractorTargets(pull));
            Assert.AreEqual(50000, pull.Attr(CombatRadar.TractorRangeAttr));
            Assert.AreEqual(69, pull.Look, "its beam is the base's");
            Assert.AreEqual(CombatRadar.MaxTractorTargets, CombatRadar.TractorTargets(db.Item(ModContent.ItemIndexOf("t_eq:many"))), "at most 8");
        }

        [Test]
        public void TractorColorAndNoLock()
        {
            // items.json "tractorColor" (attr 136 = 0x1000000 | rgb), stats "tractorNoLock" (attr 137).
            var db = ModOn();
            Assert.AreEqual(CombatRadar.TractorNoLockAttr, ItemStats.IdOf("tractorNoLock"));
            Assert.IsNull(CombatRadar.BeamColor(db.Item(68)), "the original's beam keeps its colours");
            Assert.IsNull(CombatRadar.BeamColor(null));
            var tint = db.Item(ModContent.ItemIndexOf("t_eq:tint"));
            Assert.AreEqual(new UnityEngine.Color(1f, 128f / 255f, 0f, 1f), CombatRadar.BeamColor(tint));
            Assert.AreEqual(1, tint.Attr(CombatRadar.TractorNoLockAttr));
            Assert.AreEqual(new UnityEngine.Color(0f, 128f / 255f, 1f, 1f), CombatRadar.BeamColor(db.Item(ModContent.ItemIndexOf("t_eq:tint2"))));
            Assert.AreEqual(0, db.Item(68).Attr(CombatRadar.TractorNoLockAttr));
            Assert.AreEqual(0x1000000, ModContent.TractorColorValue(new Newtonsoft.Json.Linq.JValue("000000")), "black is a colour too");
            Assert.AreEqual(-1, ModContent.TractorColorValue(new Newtonsoft.Json.Linq.JValue("orange")));
        }

        [Test]
        public void TheDisruptorAndItsClonesHitShieldsTwice()
        {
            var db = ModOn();
            Assert.AreEqual(Gun.ShieldDamageAttr, ItemStats.IdOf("shieldDamage"));
            Assert.AreEqual(2f, Gun.ShieldFactor(db.Item(183)));
            Assert.AreEqual(2f, new Gun(db.Item(183), UnityEngine.Vector3.zero, false).shieldFactor);
            Assert.AreEqual(1f, Gun.ShieldFactor(db.Item(0)), "the Nirai Impulse EX 1");
            Assert.AreEqual(1f, Gun.ShieldFactor(null));
            var clone = db.Item(ModContent.ItemIndexOf("t_eq:void"));
            Assert.AreEqual(183, clone.Look);
            Assert.AreEqual(2f, Gun.ShieldFactor(clone), "a mod Disruptor keeps the bonus");
            Assert.AreEqual(0.5f, Gun.ShieldFactor(db.Item(ModContent.ItemIndexOf("t_eq:soft"))));
            Assert.AreEqual(3f, Gun.ShieldFactor(db.Item(ModContent.ItemIndexOf("t_eq:hard"))), "shieldDamage overrides the Disruptor's");
            // the NPCs' generic gun (Level::assignGuns), the Disruptor at step 0x46 too: no bonus
            Assert.AreEqual(1f, new Gun(db.Item(183), 10f, 500f, 4, 3000f, 16f).shieldFactor);
        }
    }
}
