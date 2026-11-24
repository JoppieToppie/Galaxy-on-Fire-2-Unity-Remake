// TerranHangarBotsTests.cs
// Feature: CLAUDE.md "Station scene" > the hangar's service bots (remake). The Terran hangar's five bots (a-d and the remake's
// e, mesh ids 99004 / 99005, its own assembly hangar_terran_bot_e) and the Nivelian hangar's three fly one at a time
// (TerranHangarBots): the first within FirstPlayWithinMs of the hangar showing, then after a GapMinMs..GapMaxMs pause once
// the last one's animation has ended, picked at random but never the one that has just flown.
// Expected values: TerranHangarBots' constants; UnityEngine.Random.value is 0..1 inclusive, so 1 must still pick a bot.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class TerranHangarBotsTests : SandboxTest
    {
        [Test]
        public void TheFirstFlightComesWithinTheFirstPlayWindow()
        {
            var bots = new TerranHangarBots(5, () => 0.5f);   // 750 ms out
            Assert.AreEqual(-1, bots.Tick(749f, false, () => 0f));
            Assert.AreEqual(-1, bots.Current);
            Assert.AreEqual(0, bots.Tick(1f, false, () => 0f), "random 0: the first bot");
            Assert.AreEqual(0, bots.Current);
            Assert.AreEqual(-1, bots.Last, "nothing has flown before");

            var late = new TerranHangarBots(5, () => 1f);     // Random.value's top: the whole window, not past it
            Assert.AreEqual(-1, late.Tick(TerranHangarBots.FirstPlayWithinMs - 1f, false, () => 0f));
            Assert.GreaterOrEqual(late.Tick(1f, false, () => 0f), 0);
        }

        [Test]
        public void TheNextWaitsForTheEndAndTheGap()
        {
            var bots = new TerranHangarBots(5, () => 0f);
            int first = bots.Tick(0f, false, () => 0f);
            Assert.AreEqual(0, first);
            // Still flying: nothing else starts, however long it takes.
            for (int i = 0; i < 100; i++) Assert.AreEqual(-1, bots.Tick(100f, true, () => 0f));
            Assert.AreEqual(first, bots.Current);
            // Ended: the shortest gap (random 0) is GapMinMs, counted from the frame that saw the end.
            Assert.AreEqual(-1, bots.Tick(100f, false, () => 0f));
            Assert.AreEqual(-1, bots.Current);
            Assert.AreEqual(first, bots.Last);
            Assert.AreEqual(-1, bots.Tick(TerranHangarBots.GapMinMs - 101f, false, () => 0f));
            Assert.AreEqual(1, bots.Tick(1f, false, () => 0f), "random 0 with bot 0 just flown: bot 1");

            // The longest gap (random 1) is GapMaxMs.
            bots.Tick(16f, false, () => 1f);   // bot 1 ended at once (dt 16 counted)
            Assert.AreEqual(-1, bots.Tick(TerranHangarBots.GapMaxMs - 17f, false, () => 1f));
            Assert.GreaterOrEqual(bots.Tick(1f, false, () => 1f), 0);
        }

        [Test]
        public void NoBotFliesTwiceInARow()
        {
            var values = new System.Random(1234);
            float Rnd() => (float)values.NextDouble();
            var bots = new TerranHangarBots(5, Rnd);
            var flown = new List<int>();
            for (int i = 0; i < 20000; i++)
            {
                int start = bots.Tick(50f, false, Rnd);   // each flight ends on the next frame
                if (start < 0) continue;
                Assert.That(start, Is.InRange(0, 4));
                if (flown.Count > 0) Assert.AreNotEqual(flown[flown.Count - 1], start, "flight " + flown.Count);
                flown.Add(start);
            }
            Assert.Greater(flown.Count, 100);
            Assert.AreEqual(5, flown.Distinct().Count(), "every bot takes its turn");
        }

        [Test]
        public void ASingleBotFliesAgainAndAgain()
        {
            var bots = new TerranHangarBots(1, () => 0.7f);
            var flown = new List<int>();
            for (int i = 0; i < 1000; i++)
            {
                int start = bots.Tick(50f, false, () => 0.7f);
                if (start >= 0) flown.Add(start);
            }
            Assert.Greater(flown.Count, 10);
            Assert.IsTrue(flown.All(b => b == 0));

            var none = new TerranHangarBots(0, () => 0f);
            Assert.AreEqual(-1, none.Tick(10000f, false, () => 0f), "no bots: nothing to start");
        }

        [Test]
        public void RandomValueOneStillPicksAnotherBot()
        {
            // UnityEngine.Random.value can return 1 exactly: the pick must stay in range and skip the last bot.
            foreach (int count in new[] { 2, 3, 5 })
            {
                var bots = new TerranHangarBots(count, () => 1f);
                int last = -1;
                for (int i = 0; i < 200; i++)
                {
                    int start = bots.Tick(100f, false, () => 1f);
                    if (start < 0) continue;
                    Assert.That(start, Is.InRange(0, count - 1), $"{count} bots");
                    Assert.AreNotEqual(last, start, $"{count} bots");
                    last = start;
                }
                Assert.AreNotEqual(-1, last, $"{count} bots: something flew");
            }
        }

        [Test]
        public void BotEIsAssembledFromItsRemakeMeshes()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/GoF2Data/assemblies.json");
            var e = Newtonsoft.Json.Linq.JObject.Parse(json.text)["entries"].First(x => (string)x["name"] == "hangar_terran_bot_e");
            var meshes = e["parts"].Select(p => (int)p["mesh"]).OrderBy(m => m).ToArray();
            Assert.AreEqual(new[] { 99004, 99005 }, meshes);
            Assert.IsTrue(Resources.Load<GameObject>("Assembled/main/hangars/hangar_terran_bot_e") != null || Visuals.OriginalAssemblies.Has("hangar_terran_bot_e"),
                "the prefab, or the converted copy's assembly (a checkout without the originals)");
        }
    }
}
