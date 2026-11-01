// NpcTurnedEnemyTests.cs
// Feature: CLAUDE.md "NPCs and combat" > AI (target selection) and docs/notes/supernova.md "Level details" (158): a turned
// ship (Player+0xe0) of any race but the pirates' takes the player outside its +-50 000 box, Player::setAlwaysEnemy turns
// the ship as well as making it hostile, and 158 hands Trunt Harval to his AI that way.
// Expected values: the disassembly of PlayerFighter::update 0xf131e / 0xf1444 (race != 8 && turnedEnemy, then the box),
// Player::setAlwaysEnemy 0xaf206 (+0xec, +0x5c and +0xe0 = 1), LevelScript::process 0x169204..0x169222 (158, state 2 -> 3:
// setCloakingPossible(true), setAlwaysEnemy(true), setAIDisabled(false)). A player's report (2026-11-01): Harval patrolled
// far away instead of fighting the duel.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class NpcTurnedEnemyTests
    {
        GameObject shipGo, playerGo;

        [TearDown]
        public void DestroyObjects()
        {
            if (shipGo != null) Object.DestroyImmediate(shipGo);
            if (playerGo != null) Object.DestroyImmediate(playerGo);
        }

        [TestCase(Standing.Specter, true, true)]   // Harval at 158 (race 10)
        [TestCase(2, true, true)]                  // any other race, turned
        [TestCase(Standing.Void, true, true)]
        [TestCase(Standing.Pirate, true, false)]   // pirates only ever take what is in the box
        [TestCase(Standing.Specter, false, false)] // not turned: the box decides
        public void TurnedShipsTakeTargetsAtAnyRange(int race, bool turned, bool anyRange)
        {
            Assert.AreEqual(anyRange, NpcShip.TakesAtAnyRange(race, turned));
        }

        [Test]
        public void SetAlwaysEnemyAlsoTurnsTheShip()
        {
            var ship = NewShip();
            Assert.IsFalse(ship.alwaysEnemy || ship.turnedEnemy);
            ship.SetAlwaysEnemy();
            Assert.IsTrue(ship.alwaysEnemy, "always hostile (+0xec)");
            Assert.IsTrue(ship.turnedEnemy, "turned on the player (+0xe0)");
        }

        [Test]
        public void HarvalTakesThePlayerWhenTheDuelStarts()
        {
            // Spawned like Build158 (SpawnSpec.alwaysEnemy only), riding the cutscene with a scripted speed.
            var harval = NewShip();
            harval.alwaysEnemy = true;
            harval.scriptedSpeed = 0f;
            playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<Target>();
            SupernovaLevels.ReleaseHarval(harval, player);
            Assert.AreEqual(-1f, harval.scriptedSpeed, "his own AI flies him again");
            Assert.IsTrue(harval.turnedEnemy, "setAlwaysEnemy turned him on the player");
            Assert.IsTrue(NpcShip.TakesAtAnyRange(Standing.Specter, harval.turnedEnemy), "so he comes for the player from afar");
            Assert.AreSame(player, harval.ScriptEnemy);
            Assert.AreEqual(1, harval.enemies.Count);
            Assert.AreSame(player, harval.enemies[0], "the player is his only enemy (index 0)");
        }

        NpcShip NewShip()
        {
            shipGo = new GameObject("Harval");
            return shipGo.AddComponent<NpcShip>();
        }
    }
}
