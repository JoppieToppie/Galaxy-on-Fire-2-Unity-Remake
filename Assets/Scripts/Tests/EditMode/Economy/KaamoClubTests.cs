// KaamoClubTests.cs
// Feature: CLAUDE.md "Kaamo Club (station 108, Shima)": the states 0-3, the purchase (>= 30 000 001 $ and 50 t Buskat), the
// storage (108's stock while owned), one hull per type, the dealer agent's candidates, the conversations' text ids, the
// siege's outpost hull, and the ship mods' levels (Kaamo mechanics, "Stackable Kaamo Club upgrades").
// Expected: Reference/research/kaamo_club.md 2 / 4 / 6 (state machine, ModStation::OnTouchEnd 0xea4ec's < 0x1c9c381 check,
// Station::addShip's one-per-type, the 459-475 + 476 and 479-484 pages, PlayerFixedObject's hull
// 5 ((game won ? 180 : 4 campaign) + 15 rank + 20) x the difficulty factor), blueprints_mods.md 2-3 (mods 0-3 once each in
// the original; KAAMO_SHIPS 55-60 from blueprint_table.py), CLAUDE.md (stacking up to level 10).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class KaamoClubTests : EconomyTest
    {
        [Test]
        public void Constants()
        {
            Assert.AreEqual(108, KaamoClub.Station);
            Assert.AreEqual(26, KaamoClub.SystemIndex);
            Assert.AreEqual(26, Shop.SystemOf(Db, KaamoClub.Station));
            Assert.AreEqual(109, KaamoClub.BuskatItem);
            Assert.AreEqual(50, KaamoClub.BuskatNeeded);
            Assert.AreEqual(30000000, KaamoClub.Price);
            CollectionAssert.AreEqual(new[] { 55, 56, 57, 58, 59, 60 }, KaamoClub.DealerShips);
        }

        [Test]
        public void StatesAndOwnership()
        {
            for (int state = 0; state <= 2; state++)
            {
                Session.KaamoState = state;
                Assert.IsFalse(KaamoClub.Owned);
                Assert.IsFalse(KaamoClub.StorageAt(108));
            }
            Session.KaamoState = 3;
            Assert.IsTrue(KaamoClub.Owned);
            Assert.IsTrue(KaamoClub.StorageAt(108));
            Assert.IsFalse(KaamoClub.StorageAt(107), "only at the club itself");
        }

        [Test]
        public void SiegeOnlyBeforeMkktBkktsCall()
        {
            Session.KaamoState = 0;
            Assert.IsTrue(KaamoClub.SiegeAt(108));
            Assert.IsFalse(KaamoClub.SiegeAt(107));
            foreach (int s in new[] { 1, 2, 3 }) { Session.KaamoState = s; Assert.IsFalse(KaamoClub.SiegeAt(108)); }
        }

        [Test]
        public void PurchaseNeedsMoreThanThirtyMillionAndFiftyBuskat()
        {
            // the original's check is credits < 0x1c9c381 (30 000 001) refuses: exactly 30 000 000 isn't enough
            Session.Credits = 30000000;
            Shop.AddToCargo(109, 50);
            Assert.IsFalse(KaamoClub.CanBuy);
            Session.Credits = 30000001;
            Assert.IsTrue(KaamoClub.CanBuy);
            Shop.RemoveFromCargo(109, 1);
            Assert.IsFalse(KaamoClub.CanBuy, "49 t Buskat");
        }

        [Test]
        public void BuyingTheClub()
        {
            Session.KaamoState = 2;
            Session.Credits = 30000500;
            Shop.AddToCargo(109, 53);
            Session.KaamoItems.Add(new ItemStack(122, 9));   // sold to Kaamo before: gone (Station::setItems(108, null))
            KaamoClub.Buy();
            Assert.AreEqual(500, Session.Credits);
            Assert.AreEqual(3, Shop.CargoOf(109));
            Assert.AreEqual(3, Session.KaamoState);
            Assert.IsEmpty(Session.KaamoItems);
        }

        [Test]
        public void OneHullPerType()
        {
            Assert.IsTrue(KaamoClub.Store(9, 1, new List<int> { 2 }));
            Assert.IsFalse(KaamoClub.Store(9, 0, null), "Station::addShip ignores a stored type");
            Assert.IsTrue(KaamoClub.Store(0, 0, null));
            Assert.AreEqual(2, Session.KaamoShips.Count);
            Assert.IsTrue(KaamoClub.HasShip(9));
            Assert.IsFalse(KaamoClub.HasShip(10));
            Assert.AreEqual(new[] { 2 }, Session.KaamoShips[0].mods.ToArray());
            Assert.IsEmpty(Session.KaamoShips[1].mods);
            Assert.IsEmpty(Session.KaamoShips[1].equipment, "no equipment = a bare hull");
        }

        [Test]
        public void StoredEquipmentIsACopy()
        {
            var gear = new List<ItemStack> { new ItemStack(86, 1) };
            KaamoClub.Store(9, 0, null, gear);
            gear.Add(new ItemStack(55, 1));
            Assert.AreEqual(1, Session.KaamoShips[0].equipment.Count);
        }

        [Test]
        public void StorageKeepsItemOrderAndMergesRows()
        {
            KaamoClub.AddToStorage(new[] { new ItemStack(122, 3), new ItemStack(5, 1) });
            KaamoClub.AddToStorage(new[] { new ItemStack(122, 2), new ItemStack(55, 0), null, new ItemStack(200, 1) });
            CollectionAssert.AreEqual(new[] { 5, 55, 122, 200 }, Session.KaamoItems.Select(r => r.item).ToArray());
            Assert.AreEqual(5, Session.KaamoItems.Find(r => r.item == 122).amount);
            Assert.AreEqual(1, Session.KaamoItems.Find(r => r.item == 55).amount, "an empty stack counts as one unit");
            KaamoClub.AddToStorage(null);
            Assert.AreEqual(4, Session.KaamoItems.Count);
        }

        [Test]
        public void DealerOffersShipsThePlayerNeitherFliesNorStores()
        {
            CollectionAssert.AreEqual(new[] { 55, 56, 57, 58, 59, 60 }, KaamoClub.DealerCandidates());
            Session.ShipIndex = 57;
            KaamoClub.Store(59, 0, null);
            CollectionAssert.AreEqual(new[] { 55, 56, 58, 60 }, KaamoClub.DealerCandidates());
        }

        [Test]
        public void FirstVisitConversation()
        {
            // kaamo_club.md 4.1: 459-475 Mkkt Bkkt / Keith alternating (MSG_PLAYER_STATION_DLG_0_n), then the Info page 476
            var pages = KaamoClub.FirstVisitPages();
            Assert.AreEqual(18, pages.Count);
            for (int p = 0; p < 17; p++)
            {
                Assert.AreEqual(459 + p, pages[p].text);
                Assert.AreEqual(p % 2 == 0 ? 4 : 0, pages[p].speaker);
                Assert.AreEqual("MSG_PLAYER_STATION_DLG_0_" + p, pages[p].voice);
            }
            Assert.AreEqual(476, pages[17].text);
            Assert.AreEqual(16, pages[17].speaker);
        }

        [Test]
        public void PurchaseConversationAndSiegeCalls()
        {
            var pages = KaamoClub.PurchasePages();
            CollectionAssert.AreEqual(new[] { 479, 480, 481, 482, 483, 484 }, pages.Select(p => p.text).ToArray());
            Assert.AreEqual("MSG_PLAYER_STATION_DLG_1_5", pages[5].voice);
            Assert.AreEqual(457, KaamoClub.SiegeCall()[0].text);
            Assert.AreEqual("MSG_PLAYER_STATION_ENTER_ORBIT", KaamoClub.SiegeCall()[0].voice);
            Assert.AreEqual(458, KaamoClub.SiegeWon()[0].text);
            Assert.AreEqual("MSG_PLAYER_STATION_ENEMIES_DEAD", KaamoClub.SiegeWon()[0].voice);
        }

        [Test]
        public void OutpostHull()
        {
            // 5 (4 campaign + 15 rank + 20) x difficulty factor (x1 Normal, x2 Extreme); 180 for the campaign once won
            Session.CampaignMission = 10;
            int rank = Session.Rank;
            Assert.Less(rank, 21);
            Assert.AreEqual(5 * (40 + 15 * rank + 20), KaamoClub.OutpostHull());
            Session.Difficulty = Session.DifficultyExtreme;
            Assert.AreEqual(2 * 5 * (40 + 15 * rank + 20), KaamoClub.OutpostHull());
            Session.Difficulty = Session.DifficultyNormal;
            Session.CampaignMission = 100;
            rank = Session.Rank;
            Assert.AreEqual(5 * (180 + 15 * rank + 20), KaamoClub.OutpostHull(), "game won: 180");
        }

        // ---- ship mods (Kaamo mechanics) ------------------------------------------------------------------------------

        [Test]
        public void ModsOnceEachInTheOriginal()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoStacking, v => Settings.KaamoStacking = v))
            {
                Settings.KaamoStacking = false;
                Session.AddShipMod(1);
                Session.AddShipMod(1);
                Session.AddShipMod(3);
                Session.AddShipMod(-1);
                CollectionAssert.AreEqual(new[] { 1, 3 }, Session.ShipMods);
                Assert.AreEqual(1, Session.ModLevel(1));
                Assert.IsTrue(Session.HasMod(3));
                Assert.IsFalse(Session.HasMod(0));
            }
        }

        /// <summary>A mechanic whose upgrade the hull already has opens with 858 and greets plainly, like an accepted offer
        /// (SpaceLounge::getSoundId's hasAcceptedOffer); a player's report (2026-10-24): "I have something special" over 858.</summary>
        [Test]
        public void AMechanicWithNothingLeftGreetsPlainly()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoStacking, v => Settings.KaamoStacking = v))
            {
                Settings.KaamoStacking = false;
                Session.AddShipMod(1);
                var mechanic = new Agent { race = 0, male = true, offer = AgentOffer.SellMod, sellMod = 1, known = true };
                for (int i = 0; i < 30; i++)
                {
                    var chat = new LoungeChat(Db, mechanic, KaamoClub.Station, null);
                    chat.Start();
                    Assert.AreEqual(Localization.Get(858), chat.Text);
                    StringAssert.Contains("_GREETING_LOUNGE_GENERIC_", chat.VoiceName());
                }
            }
        }

        [Test]
        public void StackedModsUpToTheDefaultLevel()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoStacking, v => Settings.KaamoStacking = v))
            using (new SettingsScope<int>(() => Settings.KaamoMaxLevel, v => Settings.KaamoMaxLevel = v))
            {
                Settings.KaamoStacking = true;
                Settings.KaamoMaxLevel = SessionRules.DefaultKaamoLevel;
                for (int i = 0; i < 14; i++) Session.AddShipMod(2);
                Assert.AreEqual(Session.MaxModLevel, Session.ModLevel(2));
                Assert.AreEqual(5, Session.MaxModLevel, "the default limit");
                Assert.AreEqual(5, Session.ModLevel(Session.ShipMods, 2));
                Assert.AreEqual(0, Session.ModLevel(null, 2));
            }
        }

        [Test]
        public void TheUpgradeLimitIsTheSetting()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoStacking, v => Settings.KaamoStacking = v))
            using (new SettingsScope<int>(() => Settings.KaamoMaxLevel, v => Settings.KaamoMaxLevel = v))
            {
                Settings.KaamoStacking = true;
                Settings.KaamoMaxLevel = 3;
                for (int i = 0; i < 6; i++) Session.AddShipMod(1);
                Assert.AreEqual(3, Session.ModLevel(1));
                Settings.KaamoMaxLevel = 5;
                Session.AddShipMod(1);
                Assert.AreEqual(4, Session.ModLevel(1), "a raised limit sells the next level");
                Settings.KaamoMaxLevel = 2;
                Session.AddShipMod(1);
                Assert.AreEqual(4, Session.ModLevel(1), "levels above a lowered limit stay, none is added");
            }
        }

        [Test]
        public void ModLevelsFittedBeforeTheCapStay()
        {
            using (new SettingsScope<bool>(() => Settings.KaamoStacking, v => Settings.KaamoStacking = v))
            {
                Settings.KaamoStacking = true;
                Session.ShipMods = Enumerable.Repeat(0, 12).ToList();
                Session.AddShipMod(0);
                Assert.AreEqual(12, Session.ModLevel(0));
                Settings.KaamoStacking = false;
                Session.AddShipMod(0);
                Assert.AreEqual(12, Session.ModLevel(0), "levels already fitted stay when stacking is turned off");
            }
        }

        [Test]
        public void EveryModEffectIsPerLevel()
        {
            // mod 0 +40 hull, 1 +30 t cargo, 2 +1 equipment slot (Phantom: 200 hull, 52 t, 9 slots)
            Session.ShipMods = new List<int> { 0, 0, 0, 1, 1, 2, 2, 2, 2 };
            Assert.AreEqual(200 + 3 * 40, Shop.BaseHp(Db));
            Assert.AreEqual(52 + 2 * 30, Shop.MaxLoad(Db));
            Assert.AreEqual(9 + 4, new Hangar(Db, Stock(70)).SlotCount(3));
        }

        [TestCase(0, 0)] [TestCase(1, 0)] [TestCase(2, 0)] [TestCase(3, 0)]
        [TestCase(0, 1)] [TestCase(1, 1)] [TestCase(2, 1)] [TestCase(3, 1)]
        public void VipDiscountIsQuotedAndChargedForEachUpgrade(int mod, int level)
        {
            using (new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v))
            using (new SettingsScope<bool>(() => Settings.KaamoStacking, v => Settings.KaamoStacking = v))
            using (new DataUiTestKit.TextLanguage())
            {
                Settings.KaamoVipMode = Settings.KaamoVipCard;
                Settings.KaamoStacking = true;
                Session.KaamoState = 3;
                Session.KaamoVip = true;
                Session.ShipIndex = 9;
                for (int i = 0; i < level; i++) Session.AddShipMod(mod);
                int regular = (int)((double)Db.Ship(9).price * new[] { 20, 30, 40, 20 }[mod] / 100 * (1 << level));
                int expected = (int)(((long)regular * 80 + 99) / 100);
                Session.Credits = expected;
                var chat = new LoungeChat(Db, new Agent { offer = AgentOffer.SellMod, sellMod = mod }, KaamoClub.Station, null);
                chat.Start();
                StringAssert.Contains("VIP discount: 20%", chat.Text);
                Assert.AreEqual(LoungeChat.Outcome.Confirm, chat.Choose(LoungeChat.Choice.Okay));
                StringAssert.Contains("Regular price:", chat.ConfirmText);
                chat.Confirm();
                Assert.AreEqual(0, Session.Credits, "the quoted discounted amount is the amount charged");
                Assert.AreEqual(level + 1, Session.ModLevel(mod));
            }
        }

        [TestCase(false, 0, 108, 1001)]
        [TestCase(true, 2, 108, 1001)]
        [TestCase(true, 0, 107, 1001)]
        [TestCase(true, 0, 108, 801)]
        [TestCase(false, 1, 108, 801)]
        public void UpgradeDiscountRequiresMembershipModeAndKaamo(bool bought, int mode, int station, int expected)
        {
            using (new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v))
            {
                Settings.KaamoVipMode = mode;
                Session.KaamoState = 3;
                Session.KaamoVip = bought;
                Assert.AreEqual(expected, KaamoClub.UpgradePrice(1001, station));
                Session.KaamoState = 0;
                Assert.AreEqual(1001, KaamoClub.UpgradePrice(1001, station), "ownership is required");
            }
        }

        [TestCase(0, 0)] [TestCase(1, 1)] [TestCase(5, 4)] [TestCase(2147483647, 1717986918)]
        public void VipUpgradeRoundingDoesNotOverflow(int regular, int expected)
        {
            using (new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v))
            {
                Settings.KaamoVipMode = Settings.KaamoVipAlways;
                Session.KaamoState = 3;
                Assert.AreEqual(expected, KaamoClub.UpgradePrice(regular, KaamoClub.Station));
            }
        }

        [TestCase(AgentOffer.KaamoSpecial)] [TestCase(AgentOffer.ShipDealer)]
        public void VipDoesNotDiscountKaamoEquipmentOrShipSellers(int offer)
        {
            using (new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v))
            {
                Settings.KaamoVipMode = Settings.KaamoVipAlways;
                Session.KaamoState = 3;
                Session.Credits = 2000000;
                var chat = new LoungeChat(Db, new Agent { offer = offer, sellShip = 55, sellItem = 0, sellQuantity = 1, sellPrice = 123456 }, KaamoClub.Station, null);
                chat.Start();
                Assert.AreEqual(LoungeChat.Outcome.Confirm, chat.Choose(LoungeChat.Choice.Okay));
                chat.Confirm();
                Assert.AreEqual(2000000 - 123456, Session.Credits);
            }
        }

        [Test]
        public void BuyingVipGrantsDiscountWithoutControllingFreeUi()
        {
            using (new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v))
            using (new SettingsScope<bool>(() => Settings.KaamoSpecialUiFeatures, v => Settings.KaamoSpecialUiFeatures = v))
            {
                Settings.KaamoVipMode = Settings.KaamoVipCard;
                Settings.KaamoSpecialUiFeatures = true;
                Session.KaamoState = 3;
                Session.Credits = KaamoClub.VipPrice;
                Assert.IsTrue(KaamoClub.VipFeatures);
                Assert.IsFalse(KaamoClub.VipDiscount);
                Assert.IsTrue(KaamoClub.BuyVip());
                Assert.IsFalse(KaamoClub.BuyVip(), "cannot charge twice");
                Assert.AreEqual(0, Session.Credits);
                Assert.IsTrue(KaamoClub.VipDiscount);
                Settings.KaamoSpecialUiFeatures = false;
                Assert.IsFalse(KaamoClub.VipFeatures);
                Assert.IsTrue(KaamoClub.VipDiscount);
            }
        }

        [Test]
        public void VipDiscountPreservesLargeCreditAmounts()
        {
            using var mode = new SettingsScope<int>(() => Settings.KaamoVipMode, v => Settings.KaamoVipMode = v);
            Settings.KaamoVipMode = Settings.KaamoVipAlways;
            Session.KaamoState = 3;
            Assert.AreEqual(7378697629483820646L, KaamoClub.UpgradePrice(long.MaxValue, KaamoClub.Station));
            Assert.AreEqual(long.MaxValue, KaamoClub.UpgradePrice(long.MaxValue, 78));
        }
    }
}
