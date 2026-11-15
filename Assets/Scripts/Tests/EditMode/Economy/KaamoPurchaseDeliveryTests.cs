// CLAUDE.md "Kaamo Club": delivery buys the offered hull at full price without replacing or trading the flown ship.
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class KaamoPurchaseDeliveryTests : EconomyTest
    {
        Hangar Dealer(int ship)
        {
            var stock = Stock(70);
            stock.ships = new List<int> { ship };
            return new Hangar(Db, stock);
        }

        [Test]
        public void DeliveryPreservesTheFlownShipAndTransfersThePurchasedRowsMods()
        {
            Session.KaamoState = 3;
            Session.Passengers = 2;
            Session.Cargo.Add(new ItemStack(122, 100));
            var h = Dealer(8);
            h.Stock.PutMods(8, new List<int> { 2, 1 });
            int price = h.BuyShipPrice(8), oldShip = Session.ShipIndex;
            Session.Credits = price + 123L;
            var cargo = Session.Cargo;
            var equipment = Session.Equipment;
            var mods = Session.ShipMods;

            Assert.IsTrue(h.BuyShipToKaamo(8));
            Assert.AreEqual(123, Session.Credits, "full purchase price; no trade-in credit");
            Assert.AreEqual(oldShip, Session.ShipIndex);
            Assert.AreSame(cargo, Session.Cargo);
            Assert.AreSame(equipment, Session.Equipment);
            Assert.AreSame(mods, Session.ShipMods);
            Assert.AreEqual(2, Session.Passengers);
            Assert.AreEqual(100, Session.Cargo.Find(s => s.item == 122).amount);
            CollectionAssert.AreEqual(new[] { 2, 1 }, Session.KaamoShips.Find(s => s.ship == 8).mods);
            CollectionAssert.DoesNotContain(h.Stock.ships, 8);
            CollectionAssert.Contains(Session.ShipsBought, Session.ShipRef(8));
        }

        [Test]
        public void DuplicateDeliveryDoesNotChargeOrConsumeDealerStock()
        {
            Session.KaamoState = 3;
            KaamoClub.Store(8, 0, null);
            Session.Credits = 1000000000;
            var h = Dealer(8);
            Assert.AreEqual(Hangar.Result.AlreadyStored, h.CanBuyShipToKaamo(8, out _));
            Assert.IsFalse(h.BuyShipToKaamo(8));
            Assert.AreEqual(1000000000, Session.Credits);
            CollectionAssert.Contains(h.Stock.ships, 8);
            Assert.AreEqual(1, Session.KaamoShips.Count);
        }

        [Test]
        public void DeliveryRequiresTheClubFullPriceAndAvailableStock()
        {
            var h = Dealer(8);
            Session.Credits = 1000000000;
            Assert.AreEqual(Hangar.Result.NotSaleable, h.CanBuyShipToKaamo(8, out _));
            Session.KaamoState = 3;
            Session.Credits = 0;
            Assert.AreEqual(Hangar.Result.NoCredits, h.CanBuyShipToKaamo(8, out int need));
            Assert.AreEqual(h.BuyShipPrice(8), need);
            Assert.IsFalse(h.BuyShipToKaamo(8));
            h.Stock.ships.Clear();
            Assert.AreEqual(Hangar.Result.NoStock, h.CanBuyShipToKaamo(8, out _));
            Assert.AreEqual(0, Session.KaamoShips.Count);
        }

        [Test]
        public void LoungeDeliveryUsesTheSellersPriceAndConsumesTheOfferOnce()
        {
            Session.KaamoState = 3;
            Session.Credits = 1500;
            var agent = new Agent { name = "Dealer", station = 70, offer = AgentOffer.SellShip, sellShip = 8, sellPrice = 1000 };
            var chat = new LoungeChat(Db, agent, 70, null);
            int old = Session.ShipIndex;
            Assert.IsNull(chat.ConfirmShipTrade(false, false, true));
            Assert.AreEqual(500, Session.Credits);
            Assert.AreEqual(old, Session.ShipIndex);
            Assert.IsTrue(KaamoClub.HasShip(8));
            Assert.IsTrue(agent.accepted);
            Assert.IsNotNull(chat.ConfirmShipTrade(false, false, true), "an accepted offer cannot be purchased twice");
            Assert.AreEqual(500, Session.Credits);
        }
    }
}
