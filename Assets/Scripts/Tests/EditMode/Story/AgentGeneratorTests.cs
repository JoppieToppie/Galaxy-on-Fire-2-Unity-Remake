// AgentGeneratorTests.cs
// Feature: CLAUDE.md "Bar and freelance" (Agents: Generator::createAgents / createAgent; story agents once campaign > 16,
// 3-5 visitors, one wingman offer per bar, diplomats for hostile races, the 10x mission, the evacuated supernova lounges,
// a blueprint seller whose blueprint is owned) and "Kaamo Club" (the Zoboon brothers' offers).
// Expected values: Reference/research/freelance_missions.md §1.3 (createAgents: n = s + 3 + nextInt(2) if < 5 else 5;
// station 108 story agents only; Sao Perula 106 after dlc1Won; the Kaamo items DAT_00251d10, k = nextInt(7)+2 before 0x8e,
// quantity 10 for k = 3, 4; the ship dealer's [55..60]; index 23 at Thynome: item 68 for 100 000; the 10x rule), §1.4
// (createAgent: 20 % any of 8 races, offers never 3 / 4, Vossk never wingmen, offers 5 / 6 become missions before 16, only
// Terrans female (60 % men), wingmen cost (nextInt(1300)+700)·(friends+1), x7 hardcore; the item seller's quantity and
// price; the offer distribution 0: 46.4 %, 1 / 2 / 5 / 6: 13.4 % each; names; ImageFactory::createChar's sets and part
// counts), §3.3 (the diplomat's cost int(|axis| / 100 · 16 000)); agents.json (the story agents' stations and offers);
// CLAUDE.md for the remake's owned-blueprint rule and the evacuated stations (system 27 before campaign 158).

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class AgentGeneratorTests : StoryAreaTestBase
    {
        static int StoryAgentsAt(int station) => AgentData.StoryAgents.Count(s => s.station == station);

        [Test]
        public void StoryAgentsTable()
        {
            Assert.AreEqual(27, AgentData.StoryAgents.Count, "agents.json: the 27 story agents");
            Assert.AreEqual(6, StoryAgentsAt(108), "21-26 at the Kaamo Club");
        }

        [Test]
        public void ThreeToFiveVisitorsWithoutStoryAgents()
        {
            Session.CampaignMission = 20;
            for (int seed = 0; seed < 100; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                var agents = AgentGenerator.CreateAgents(Db, 57);
                Assert.That(agents.Count, Is.InRange(3, 5), "seed " + seed);
                Assert.IsFalse(agents.Exists(a => a.IsStory));
                Assert.IsTrue(agents.TrueForAll(a => a.station == 57));
            }
        }

        [Test]
        public void StoryAgentsComeFirstFromCampaign17()
        {
            Assume.That(StoryAgentsAt(3), Is.EqualTo(1));   // Geryan Zhamoum at Suteo
            Session.CampaignMission = 16;
            Assert.IsFalse(AgentGenerator.CreateAgents(Db, 3).Exists(a => a.IsStory), "campaign > 16 only");
            Session.CampaignMission = 17;
            for (int seed = 0; seed < 30; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                var agents = AgentGenerator.CreateAgents(Db, 3);
                Assert.IsTrue(agents[0].IsStory && agents[0].storyIndex == 0, "story agents first");
                Assert.That(agents.Count, Is.InRange(4, 5), "s + 3 + nextInt(2), at most 5");
            }
        }

        [Test]
        public void TheKaamoClubHasOnlyItsStoryAgents()
        {
            Session.CampaignMission = 20;
            var agents = AgentGenerator.CreateAgents(Db, 108);
            Assert.AreEqual(6, agents.Count);
            Assert.IsTrue(agents.TrueForAll(a => a.IsStory));
            var kaamo = agents.Find(a => a.storyIndex == 25);
            Assert.AreEqual(AgentOffer.KaamoSpecial, kaamo.offer);
            var items = new[] { 200, 220, 208, 213, 216, 228, 229, 230, 231 };
            int k = System.Array.IndexOf(items, kaamo.sellItem);
            Assert.That(k, Is.InRange(2, 8), "nextInt(7) + 2 before campaign 0x8e");
            Assert.AreEqual(k == 3 || k == 4 ? 10 : 1, kaamo.sellQuantity);
            var it = Db.Item(kaamo.sellItem);
            Assert.AreEqual((it.minPrice + it.maxPrice) / 2 * kaamo.sellQuantity, kaamo.sellPrice, "single price x quantity");
            var dealer = agents.Find(a => a.storyIndex == 26);
            Assert.AreEqual(AgentOffer.ShipDealer, dealer.offer);
            Assert.That(dealer.sellShip, Is.InRange(55, 60));
            Assert.AreNotEqual(Session.ShipIndex, dealer.sellShip);
            Assert.AreEqual(Db.Ship(dealer.sellShip).price, dealer.sellPrice);
        }

        [Test]
        public void TheShipDealerHasNothingLeftWhenEveryShipIsTaken()
        {
            Session.CampaignMission = 20;
            foreach (int s in new[] { 55, 56, 57, 58, 59 }) Session.KaamoShips.Add(new StoredShip(s, 0, null));
            Session.ShipIndex = 60;
            var dealer = AgentGenerator.CreateAgents(Db, 108).Find(a => a.storyIndex == 26);
            Assert.IsTrue(dealer.accepted, "none left: the offer counts as accepted");
        }

        [Test]
        public void SaoPerulasBlueprintSellerOnlyAfterValkyrie()
        {
            Session.CampaignMission = 50;
            Assert.IsFalse(AgentGenerator.CreateAgents(Db, 106).Exists(a => a.storyIndex == 18));
            Session.CampaignMission = 90;
            Assert.IsTrue(AgentGenerator.CreateAgents(Db, 106).Exists(a => a.storyIndex == 18));
        }

        [Test]
        public void AnOwnedBlueprintIsntOfferedAgain()
        {
            Session.CampaignMission = 20;
            var seller = AgentGenerator.CreateAgents(Db, 54).Find(a => a.storyIndex == 3);   // Per Magnusson
            Assert.AreEqual(AgentOffer.SellBlueprint, seller.offer);
            Assert.AreEqual(27, seller.sellBlueprint);
            Assert.IsFalse(seller.accepted);
            Session.UnlockedBlueprints.Add(27);
            seller = AgentGenerator.CreateAgents(Db, 54).Find(a => a.storyIndex == 3);
            Assert.IsTrue(seller.accepted && seller.known, "858: nothing left");
        }

        [Test]
        public void ATakenStoryOfferStaysTaken()
        {
            Session.CampaignMission = 20;
            Session.StoryAgentsAccepted.Add(12);   // Ylook Pykku, coordinates
            var a = AgentGenerator.CreateAgents(Db, 18).Find(x => x.storyIndex == 12);
            Assert.AreEqual(AgentOffer.SellSystem, a.offer);
            Assert.IsTrue(a.accepted && a.known);
        }

        [Test]
        public void StoryAgentOffersFromTheirData()
        {
            Session.CampaignMission = 20;
            foreach (var s in AgentData.StoryAgents)
            {
                if (s.station == 106 || s.station == 108) continue;
                var a = AgentGenerator.CreateAgents(Db, s.station).Find(x => x.storyIndex == s.index);
                Assert.IsNotNull(a, "agent " + s.index);
                int expected = s.sellBlueprint >= 0 ? AgentOffer.SellBlueprint : s.sellItemSystem >= 0 ? AgentOffer.SellSystem
                             : s.sellMod >= 0 ? AgentOffer.SellMod : AgentOffer.SmallTalk;
                Assert.AreEqual(expected, a.offer, "agent " + s.index);
                Assert.AreEqual(s.name, a.name);
                Assert.AreEqual(s.sellItemPrice, a.sellPrice);
            }
        }

        [Test]
        public void TheEvacuatedSupernovaLoungesAreEmpty()
        {
            Session.CampaignMission = 120;
            foreach (int st in new[] { 109, 110, 111, 112, 113 }) Assert.AreEqual(0, AgentGenerator.CreateAgents(Db, st).Count, "station " + st);
            Session.CampaignMission = 157;
            Assert.AreEqual(0, AgentGenerator.CreateAgents(Db, 112).Count);
            Session.CampaignMission = 158;
            Assert.That(AgentGenerator.CreateAgents(Db, 112).Count, Is.GreaterThanOrEqualTo(3), "after the supernova (0x9e)");
        }

        [Test]
        public void TheRetractorSellerAtThynomeInStep23()
        {
            // The Android build's agent; the iOS build (the Default economy) puts the Retractor into the shops at steps 17-24 instead.
            Session.Economy = Economy.Default;
            Session.CampaignMission = 23;
            Assert.IsFalse(AgentGenerator.CreateAgents(Db, 10).Exists(a => a.sellItem == 68 && a.sellPrice == 100000), "not in the iOS build");
            Session.Economy = Economy.Android;
            var agents = AgentGenerator.CreateAgents(Db, 10);
            var seller = agents.Find(a => a.offer == AgentOffer.SellItem && a.sellItem == 68);
            Assert.IsNotNull(seller, "AB-1 Retractor");
            Assert.AreEqual(100000, seller.sellPrice);
            Assert.AreEqual(0, seller.race);
            Session.CampaignMission = 24;
            Assert.IsFalse(AgentGenerator.CreateAgents(Db, 10).Exists(a => a.sellItem == 68 && a.sellPrice == 100000));
        }

        [Test]
        public void Step23AtThynomeTheFirstCheapMissionPaysTenTimes()
        {
            Session.Economy = Economy.Android;   // the Android build's rule (the iOS build has none)
            Session.CampaignMission = 23;
            int level = System.Math.Min(Session.Rank, 20);
            int checkedBars = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                var agents = AgentGenerator.CreateAgents(Db, 10);
                bool first = true;
                foreach (var a in agents)
                {
                    if (a.offer != AgentOffer.Mission || !a.HasMission) continue;
                    var m = a.mission;
                    float dist = FreelanceReference.Distance(Db, FreelanceReference.SystemOf(Db, 10), FreelanceReference.SystemOf(Db, m.target));
                    int formula = FreelanceReference.Round50(FreelanceReference.Reward(m.type, m.difficulty, dist, m.amount, 0, level));
                    if (first && formula < 50000) { Assert.AreEqual(System.Math.Min(formula * 10, 50000), m.reward, "seed " + seed); first = false; checkedBars++; }
                    else Assert.AreEqual(formula, m.reward, "seed " + seed + ": only the first");
                }
            }
            Assert.That(checkedBars, Is.GreaterThan(10));
        }

        [Test]
        public void OneWingmanOfferPerBar()
        {
            Session.CampaignMission = 20;
            for (int seed = 0; seed < 300; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                Assert.That(AgentGenerator.CreateAgents(Db, 57).Count(a => a.offer == AgentOffer.Wingmen), Is.LessThanOrEqualTo(1), "seed " + seed);
            }
        }

        [Test]
        public void MissionOffersCarryTheirMission()
        {
            Session.CampaignMission = 20;
            for (int seed = 0; seed < 50; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                foreach (var a in AgentGenerator.CreateAgents(Db, 57))
                    if (a.offer == AgentOffer.Mission)
                    {
                        Assert.IsTrue(a.HasMission);
                        Assert.AreEqual(a.station, a.mission.clientStation);
                        Assert.AreEqual(a.race, a.mission.clientRace);
                    }
                    else if (a.offer == AgentOffer.Purchase) Assert.IsFalse(a.HasMission, "offer 5 creates it at the first chat");
            }
        }

        [Test]
        public void DiplomatsForHostileRaces()
        {
            Session.CampaignMission = 20;
            Session.Standing = new[] { -80, 0 };   // the Terrans hate you
            int bars = 0, withDiplomat = 0;
            for (int seed = 0; seed < 400; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                var agents = AgentGenerator.CreateAgents(Db, 57);
                bars++;
                var d = agents.FindAll(a => a.offer == AgentOffer.Diplomat);
                Assert.That(d.Count, Is.LessThanOrEqualTo(1), "one hostile race");
                if (d.Count == 0) continue;
                withDiplomat++;
                Assert.AreEqual(0, d[0].race, "a Terran diplomat");
                Assert.IsTrue(d[0].male);
                Assert.AreEqual(12800, d[0].costs, "int(80 / 100 x 16 000)");
            }
            Assert.That(withDiplomat / (float)bars, Is.InRange(0.27f, 0.43f), "35 %");
        }

        [Test]
        public void NoDiplomatsWhenNobodyIsHostile()
        {
            Session.CampaignMission = 20;
            for (int seed = 0; seed < 200; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                Assert.IsFalse(AgentGenerator.CreateAgents(Db, 57).Exists(a => a.offer == AgentOffer.Diplomat));
            }
        }

        [TestCase(0, -80, 0, 12800)]
        [TestCase(1, 75, 0, 12000)]
        [TestCase(2, 0, -100, 16000)]
        [TestCase(3, 0, 50, 8000)]
        public void DiplomatCosts(int race, int s0, int s1, int costs)
        {
            Session.Standing = new[] { s0, s1 };
            Assert.AreEqual(costs, AgentGenerator.DiplomatCosts(race));
        }

        // ---- createAgent ------------------------------------------------------------------------------------

        [Test]
        public void GenericAgentRules()
        {
            Session.CampaignMission = 20;
            for (int seed = 0; seed < 3000; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                var a = AgentGenerator.CreateAgent(Db, 57);
                string at = "seed " + seed;
                Assert.That(a.race, Is.InRange(0, 7), at);
                CollectionAssert.Contains(new[] { 0, 1, 2, 5, 6 }, a.offer, at + ": never 3 / 4 for a generic agent");
                Assert.IsFalse(a.race == 1 && a.offer == AgentOffer.Wingmen, at + ": Vossk never offer wingmen");
                if (!a.male) Assert.AreEqual(0, a.race, at + ": only Terrans can be female");
                if (a.offer == AgentOffer.Wingmen)
                {
                    Assert.IsTrue(a.male);
                    Assert.That(a.wingmen.Count, Is.InRange(0, 2));
                    int per = a.costs / (a.wingmen.Count + 1);
                    Assert.AreEqual(0, a.costs % (a.wingmen.Count + 1));
                    Assert.That(per, Is.InRange(700, 1999), at + ": nextInt(1300) + 700 per head");
                }
                if (a.offer == AgentOffer.SellItem)
                {
                    var it = Db.Item(a.sellItem);
                    CollectionAssert.DoesNotContain(new[] { 131, 164, 175, 217, 218 }, a.sellItem);
                    Assert.AreEqual(0, it.blueprint.Count);
                    Assert.That(it.occurrence, Is.GreaterThan(0));
                    int single = (it.minPrice + it.maxPrice) / 2;
                    Assert.That(single, Is.GreaterThan(0));
                    bool one = it.type == "primary" || it.type == "turret" || it.type == "equipment";
                    if (one) Assert.AreEqual(1, a.sellQuantity, at);
                    else Assert.That(a.sellQuantity, Is.InRange(5, 19), at);
                    Assert.AreEqual(0, a.sellPrice % a.sellQuantity);
                    int unit = a.sellPrice / a.sellQuantity;
                    Assert.That(unit, Is.InRange((int)(0.4f * single) - 1, (int)(1.59f * single) + 1), at + ": 40-159 % of the base price");
                }
                Assert.IsFalse(string.IsNullOrEmpty(a.name), at);
            }
        }

        [Test]
        public void WingmenCostSevenTimesOnExtreme()
        {
            Session.CampaignMission = 20;
            Session.Difficulty = Session.DifficultyExtreme;
            int found = 0;
            for (int seed = 0; seed < 2000 && found < 20; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                var a = AgentGenerator.CreateAgent(Db, 57);
                if (a.offer != AgentOffer.Wingmen) continue;
                found++;
                Assert.AreEqual(0, a.costs % (7 * (a.wingmen.Count + 1)));
                Assert.That(a.costs / (7 * (a.wingmen.Count + 1)), Is.InRange(700, 1999));
            }
            Assert.That(found, Is.GreaterThan(0));
        }

        [Test]
        public void BeforeCampaign16PurchaseAndWingmenBecomeMissions()
        {
            Session.CampaignMission = 10;
            for (int seed = 0; seed < 1000; seed++)
            {
                AgentGenerator.Rng = new System.Random(seed);
                CollectionAssert.Contains(new[] { 0, 1, 2 }, AgentGenerator.CreateAgent(Db, 57).offer);
            }
        }

        [Test]
        public void OfferDistribution()
        {
            // Union (57) is Terran: 97.5 % of the agents non-Vossk (0: 46.4 %, 1 / 2 / 5 / 6: 13.4 %), 2.5 % Vossk (0: 49.75 %,
            // 1 / 2 / 5: 16.8 %).
            Session.CampaignMission = 20;
            AgentGenerator.Rng = new System.Random(4242);
            var counts = new int[7];
            int terran = 0, female = 0, terranNoWingmen = 0;
            const int n = 20000;
            for (int k = 0; k < n; k++)
            {
                var a = AgentGenerator.CreateAgent(Db, 57);
                counts[a.offer]++;
                if (a.race == 0) { terran++; if (a.offer != AgentOffer.Wingmen) { terranNoWingmen++; if (!a.male) female++; } }
            }
            float P(int o) => counts[o] / (float)n;
            Assert.AreEqual(0.975f * 0.464f + 0.025f * 0.4975f, P(0), 0.015f, "missions");
            Assert.AreEqual(0.975f * 0.134f + 0.025f * 0.168f, P(1), 0.012f, "small talk");
            Assert.AreEqual(0.975f * 0.134f + 0.025f * 0.168f, P(2), 0.012f, "items");
            Assert.AreEqual(0.975f * 0.134f + 0.025f * 0.168f, P(5), 0.012f, "purchases");
            Assert.AreEqual(0.975f * 0.134f, P(6), 0.012f, "wingmen");
            Assert.AreEqual(0.8f + 0.2f / 8f, terran / (float)n, 0.015f, "80 % the system race + 20 % any of 8");
            Assert.AreEqual(0.4f, female / (float)terranNoWingmen, 0.02f, "Terrans: 60 % men");
        }

        // ---- names and portraits ------------------------------------------------------------------------------

        [Test]
        public void NamesPerRace()
        {
            var names = AgentData.Names;
            for (int race = 0; race <= 7; race++)
                for (int k = 0; k < 30; k++)
                {
                    string n = AgentGenerator.RandomName(race, k % 2 == 0);
                    Assert.IsFalse(string.IsNullOrEmpty(n), "race " + race);
                    if (race == 5) CollectionAssert.Contains(names["cyborg_0"], n, "Cyborgs have first names only");
                    if (race == 7) CollectionAssert.Contains(names["grey_0"], n, "Greys have first names only");
                }
            Assert.AreEqual("", AgentGenerator.RandomName(8, true));
            for (int k = 0; k < 30; k++)
            {
                string w = AgentGenerator.RandomName(0, false);
                Assert.IsTrue(names["terran_0_w"].Exists(f => w.StartsWith(f + " ")), "a woman's first name: " + w);
                string v = AgentGenerator.RandomName(1, true);
                Assert.IsTrue(names["vossk_0"].Exists(f => v.StartsWith(f + " ")) && names["vossk_1"].Exists(l => v.EndsWith(" " + l)), v);
            }
        }

        [Test]
        public void PortraitSets()
        {
            int[][] counts = { new[] { 11, 11, 11, 11 }, new[] { 4, 5, 6, 9 }, new[] { 5, 5, 5, 5 }, null, new[] { 3, 3, 5, 4 }, null,
                               new[] { 2, 3, 5, 0 }, new[] { 2, 2, 3, 2 }, new[] { 1, 1, 1, 1 }, new[] { 1, 1, 1, 1 }, new[] { 4, 4, 5, 7 } };
            int midoTerran = 0;
            for (int k = 0; k < 2000; k++)
            {
                int race = k % 8;
                bool male = k % 3 != 0;
                var p = AgentGenerator.CreatePortrait(male, race);
                Assert.AreEqual(5, p.Length);
                int expectedSet = race == 3 ? p[0] : race == 5 ? 0 : race == 0 && !male ? 10 : race;
                if (race == 3) { CollectionAssert.Contains(new[] { 0, 2 }, p[0]); if (p[0] == 0) midoTerran++; }
                Assert.AreEqual(expectedSet, p[0], "race " + race + " male " + male);
                var c = counts[p[0]];
                for (int j = 0; j < 4; j++) Assert.That(p[j + 1], Is.InRange(0, System.Math.Max(0, c[j] - 1)), "part " + j);
            }
            Assert.AreEqual(0.25f, midoTerran / 250f, 0.08f, "Midorians: 25 % Terran faces");
        }

        [Test]
        public void SinglePriceAndMissionBonus()
        {
            var it = Db.Item(122);
            Assert.AreEqual((it.minPrice + it.maxPrice) / 2, AgentGenerator.SinglePrice(it));
            Assert.AreEqual(0, AgentGenerator.SinglePrice(null));
            Session.Standing = new[] { 30, -60 };
            Assert.AreEqual(0.3f, AgentGenerator.MissionBonus(0), 1e-6f);
            Assert.AreEqual(0f, AgentGenerator.MissionBonus(1));
            Assert.AreEqual(0f, AgentGenerator.MissionBonus(2));
            Assert.AreEqual(0.6f, AgentGenerator.MissionBonus(3), 1e-6f);
            Assert.AreEqual(0f, AgentGenerator.MissionBonus(4));
            Assert.AreEqual(0f, AgentGenerator.MissionBonus(8));
        }

        [Test]
        public void GenerateStationIndexInMidoStaysInMido()
        {
            for (int k = 0; k < 200; k++)
            {
                int st = AgentGenerator.GenerateStationIndex(Db, 78, 78);
                CollectionAssert.Contains(new[] { 75, 77, 78 }, st, "a Mido station not on the excluded list (76, 79)");
            }
        }
    }
}
