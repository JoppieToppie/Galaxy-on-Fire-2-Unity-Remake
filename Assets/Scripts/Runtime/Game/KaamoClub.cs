// KaamoClub.cs
// The Kaamo Club (station 108, Shima system 26) as plain C#: ownership, the purchase, the storage and its parked hulls.
// Reference/research/kaamo_club.md (states, siege, docking, storage rules), blueprints_mods.md 3, shop.md 5.
//   Status+0x114                    0 not owned, 1 Mkkt Bkkt's call heard (orbit entry, 457), 2 purchasable (after the
//                                   18-page first visit), 3 owned
//   ModStation::OnTouchEnd 0xea4ec  477 -> Yes: -30 000 000 $, -50 t Buskat (item 109), state 3, both item lists cleared;
//                                   the check is credits >= 30 000 001 (< 0x1c9c381 refuses) and 50 t Buskat in the hold
//   Status::departStation 0xb63e0   the storage's goods / ships are station 108's stock while docked there (the remake keeps
//                                   one list: Session.KaamoItems is 108's stock while owned)
//   Station::addShip 0xb3dd8        a type that is already stored is silently ignored (one ship per type)
//   Level::createMission 0xbda70    the state-0 siege: 4 Pirate Outposts (0x37a3) + 6 (8) pirates (KaamoSiege)
//   PlayerFixedObject ctor 0x17ece0 outpost hull f = 5 ((game won ? 180 : 4 campaign) + (rank < 21 ? 15 rank + 20 : 320)),
//                                   x (difficulty + 0.5): x1 normal, x2 hardcore
//   DAT_00251f40                    the dealer's ships [55..60] (agent 26, offer 10)

using System.Collections.Generic;
using System.Linq;

namespace GoF2Remake.Data
{
    public static class KaamoClub
    {
        public const int Station = 108, SystemIndex = 26;
        public const int BuskatItem = 109, BuskatNeeded = 50, Price = 30000000;
        public const int ClientName = 1601;   // "Mkkt Bkkt", story speaker 4
        public const int SpeakerMkkt = 4, SpeakerKeith = 0, SpeakerInfo = 16;
        public static readonly int[] DealerShips = { 55, 56, 57, 58, 59, 60 };

        public static bool Owned => Session.KaamoState >= 3;
        /// <summary>HangarWindow+0x11d: docked at the club while owning it (the Store tab, free transfers).</summary>
        public static bool StorageAt(int station) => station == Station && Owned;

        /// <summary>ModStation::OnInitialize state 2: enough credits (the original's off-by-one) and 50 t Buskat in the hold.</summary>
        public static bool CanBuy => Session.Credits > Price && Shop.CargoOf(BuskatItem) >= BuskatNeeded;

        /// <summary>477 -> Yes.</summary>
        public static void Buy()
        {
            Session.Credits -= Price;
            Shop.RemoveFromCargo(BuskatItem, BuskatNeeded);
            Session.KaamoState = 3;
            Session.KaamoItems.Clear();   // Station::setItems(108 / storage, null): anything sold to Kaamo before is gone
        }

        // ---- conversations (DialogueWindow tables, kaamo_club.md 4.1 / 4.3) -------------------------------------

        /// <summary>DAT_0025c5f0, the first visit after the siege (state 1 -> 2): 459-475 Mkkt Bkkt / Keith alternating
        /// (voices MSG_PLAYER_STATION_DLG_0_n), then the Info page 476.</summary>
        public static List<DialoguePage> FirstVisitPages()
        {
            var pages = new List<DialoguePage>();
            for (int p = 0; p < 17; p++)
                pages.Add(new DialoguePage { speaker = p % 2 == 0 ? SpeakerMkkt : SpeakerKeith, text = 459 + p, voice = $"MSG_PLAYER_STATION_DLG_0_{p}" });
            pages.Add(new DialoguePage { speaker = SpeakerInfo, text = 476 });
            return pages;
        }

        /// <summary>DAT_0025c680 (479-484, voices MSG_PLAYER_STATION_DLG_1_n): unreachable in the original's station code;
        /// the remake plays it after 477 -> Yes, before 485 (clearly its intent).</summary>
        public static List<DialoguePage> PurchasePages()
        {
            var pages = new List<DialoguePage>();
            for (int p = 0; p < 6; p++)
                pages.Add(new DialoguePage { speaker = p % 2 == 0 ? SpeakerMkkt : SpeakerKeith, text = 479 + p, voice = $"MSG_PLAYER_STATION_DLG_1_{p}" });
            return pages;
        }

        /// <summary>MGame::OnUpdate 0x1ac778: the call on entering the orbit (457, voice 1468); and the success page 458
        /// (DialogueWindow::loadContent's target-108 rule, voice 1467).</summary>
        public static List<DialoguePage> SiegeCall() =>
            new List<DialoguePage> { new DialoguePage { speaker = SpeakerMkkt, text = 457, voice = "MSG_PLAYER_STATION_ENTER_ORBIT" } };
        public static List<DialoguePage> SiegeWon() =>
            new List<DialoguePage> { new DialoguePage { speaker = SpeakerMkkt, text = 458, voice = "MSG_PLAYER_STATION_ENEMIES_DEAD" } };

        // ---- stored ships -------------------------------------------------------------------------------------

        public static bool HasShip(int ship) => Session.KaamoShips.Any(s => s.ship == ship);

        /// <summary>Station::addShip on the storage: ignored when that type is already parked. Returns whether it was added.</summary>
        public static bool Store(int ship, int race, List<int> mods, List<ItemStack> equipment = null)
        {
            if (HasShip(ship)) return false;
            Session.KaamoShips.Add(new StoredShip(ship, race, mods, equipment));
            return true;
        }

        /// <summary>Items into the club's storage (Session.KaamoItems, 108's stock while owned), in item order.</summary>
        public static void AddToStorage(IEnumerable<ItemStack> items)
        {
            if (items == null) return;
            foreach (var e in items)
            {
                if (e == null) continue;
                int n = Hangar.UnitsOf(Database.Shared, e);   // an empty secondary is nothing
                if (n <= 0) continue;
                var row = Session.KaamoItems.Find(s => s.item == e.item);
                if (row != null) { row.amount += n; continue; }
                int at = Session.KaamoItems.FindIndex(s => s.item > e.item);
                Session.KaamoItems.Insert(at < 0 ? Session.KaamoItems.Count : at, new ItemStack(e.item, n));
            }
        }

        // ---- the VIP Card (remake) -----------------------------------------------------------------------------

        /// <summary>Remake (players' suggestion, Discord "Kaamo Club features"): the original's second in-app purchase, the
        /// "Kaamo Club VIP Card" (texts 80 / 90 / 95, options[0x38]: the Kaamo dealers at half price), comes back as a card
        /// bought with credits in the club's Store tab. It gives the club the shop tools the players asked for: a search box
        /// and sorting in the Store, the stored hulls in a Ships tab of their own, and a Kaamo marker on every dealer's ship
        /// of a type already parked at the club. The remake's Kaamo prices were already the card's, so it changes no price.
        /// Options > Gameplay "Kaamo Club VIP Card" (Settings.KaamoVipMode): bought at the club (default), always on, or off.</summary>
        public const int VipPrice = 1000000;

        /// <summary>The VIP Card's tools are on: the option's "Always", or its default with the card bought.</summary>
        public static bool VipFeatures => Settings.KaamoVipMode == Settings.KaamoVipAlways
                                          || (Settings.KaamoVipMode == Settings.KaamoVipCard && Session.KaamoVip);

        /// <summary>The Store tab offers the card: the club owned, the option at "bought at the club", not bought yet.</summary>
        public static bool VipForSale => Owned && Settings.KaamoVipMode == Settings.KaamoVipCard && !Session.KaamoVip;

        /// <summary>The card bought: VipPrice off the credits. False when there aren't enough or it is no longer for sale.</summary>
        public static bool BuyVip()
        {
            if (!VipForSale || Session.Credits < VipPrice) return false;
            Session.Credits -= VipPrice;
            Session.KaamoVip = true;
            return true;
        }

        /// <summary>Agent 26 (offer 10): the dealer's ships the player neither flies nor stores.</summary>
        public static List<int> DealerCandidates() =>
            DealerShips.Where(s => s != Session.ShipIndex && !HasShip(s)).ToList();

        // ---- the siege ---------------------------------------------------------------------------------------

        /// <summary>Level::createMission: the siege runs while the club isn't owned and Mkkt Bkkt hasn't been helped yet.</summary>
        public static bool SiegeAt(int station) => station == Station && Session.KaamoState == 0;

        /// <summary>PlayerFixedObject::PlayerFixedObject 0x17ece0 for 0x37a3.</summary>
        public static int OutpostHull()
        {
            int rank = Session.Rank;
            float f = 5f * ((Story.GameWon && (!Session.FreePlay || Session.CompletedWorld) ? 180 : 4 * Session.CampaignMission) + (rank < 21 ? 15 * rank + 20 : 320));
            return (int)(f * Session.DifficultyFactor);
        }
    }
}
