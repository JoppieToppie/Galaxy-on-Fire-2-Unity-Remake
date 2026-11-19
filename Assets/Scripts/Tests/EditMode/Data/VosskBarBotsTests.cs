// VosskBarBotsTests.cs
// Feature: CLAUDE.md "Station scene" > the Vossk bar's bartender arms (remake). The bar_vossk assembly holds the original's
// unused bar_vossk_bot_a/b/c_anim_alpha models (remake mesh ids 99001-99003, build_assemblies.py's MODEL_OVERRIDE) as
// per-mesh prefabs with the room's material 20059 and their animation. VosskBarBots plays the dead CutScene::process slots:
// the second arm (bot_b) every 20000 ms, the end arm (bot_a or bot_c at random) every 22000 ms, the first plays within 4 s.
// Expected values: the original's periods (station_interior.md 2.4) and the sidecars' tracks.

using System.Linq;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class VosskBarBotsTests : SandboxTest
    {
        static readonly string[] Bots = { "bar_vossk_bot_a_anim_alpha", "bar_vossk_bot_b_anim_alpha", "bar_vossk_bot_c_anim_alpha" };

        [Test]
        public void TheBarAssemblyHoldsTheThreeArms()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/GoF2Data/assemblies.json");
            var bar = Newtonsoft.Json.Linq.JObject.Parse(json.text)["entries"].First(e => (string)e["name"] == "bar_vossk");
            for (int i = 0; i < Bots.Length; i++)
            {
                var part = bar["parts"].FirstOrDefault(p => (string)p["model"] == "Models/main/bars/" + Bots[i] + ".fbx");
                Assert.IsNotNull(part, Bots[i]);
                Assert.AreEqual(99001 + i, (int)part["mesh"], Bots[i]);
            }
        }

        [Test]
        public void EachArmsPrefabAnimatesWithTheRoomsMaterial()
        {
            DataUiTestKit.NeedsOriginal("Assets/Prefabs/main/bars");   // (the editor's per-mesh prefabs)
            var room = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/mat_20059_bar_vossk_diffuse.mat");
            foreach (string bot in Bots)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/main/bars/" + bot + ".prefab");
                Assert.IsNotNull(prefab, bot);
                var anim = prefab.GetComponent<Visuals.PartAnimation>();
                Assert.IsNotNull(anim, bot);
                Assert.AreEqual(bot + ".gof2mesh", anim.meta.name);
                Assert.AreEqual(new[] { -1f, -1f, -1f }, anim.rotationMap.Select(m => m.sign).ToArray(), "the rotation signs of every game prefab");
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true)) Assert.AreEqual(room, r.sharedMaterial, bot);
            }
            var assembled = Resources.Load<GameObject>("Assembled/main/bars/bar_vossk");
            foreach (string bot in Bots) Assert.IsNotNull(assembled.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == bot), "assembled: " + bot);
        }

        [Test]
        public void TheFirstPlaysComeWithinFourSeconds()
        {
            var bots = new VosskBarBots(() => 0.5f);   // both slots 2000 ms out
            Assert.AreEqual(VosskBarBots.Start.None, bots.Tick(1999f, () => 0.9f));
            Assert.AreEqual(VosskBarBots.Start.EndArm | VosskBarBots.Start.SecondArm, bots.Tick(1f, () => 0.9f));
            Assert.IsFalse(bots.EndArmPlaysC, "0.9: bot_a");
        }

        [Test]
        public void TheSlotsComeRoundEvery20And22Seconds()
        {
            var bots = new VosskBarBots(() => 0f);
            Assert.AreEqual(VosskBarBots.Start.EndArm | VosskBarBots.Start.SecondArm, bots.Tick(1f, () => 0.2f));
            Assert.IsTrue(bots.EndArmPlaysC, "0.2: bot_c");
            int second = 0, end = 0;
            float t = 0f;
            for (int i = 0; i < 2200; i++)   // 220 s in 100 ms steps
            {
                var s = bots.Tick(100f, () => 0.7f);
                t += 100f;
                if ((s & VosskBarBots.Start.SecondArm) != 0) { second++; Assert.AreEqual(0f, t % 20000f, "second arm on its 20 s beat"); }
                if ((s & VosskBarBots.Start.EndArm) != 0) end++;
            }
            Assert.AreEqual(11, second, "220 s / 20 s");
            Assert.AreEqual(10, end, "220 s / 22 s");
        }
    }
}
