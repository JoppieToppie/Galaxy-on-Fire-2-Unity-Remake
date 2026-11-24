using System.Linq;
using GoF2Remake.Modding;
using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class PlayerIdentityTests
    {
        [Test]
        public void EveryStockRaceHasAValidatedPortrait()
        {
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 11), ModRefs.RaceWords.Select(entry => entry.race));
            foreach (var entry in ModRefs.RaceWords)
            {
                Assert.AreEqual(entry.race, ModRaces.GameRaceWord(entry.word));
                string raceKey = PlayerIdentity.RaceKey(entry.race);
                Assert.IsTrue(PlayerIdentity.TryResolveRace(raceKey, out int race));
                var portraits = PlayerIdentity.Portraits(race).Where(portrait => portrait.Key.StartsWith("stock/")).ToList();
                Assert.IsNotEmpty(portraits);
                foreach (var portrait in portraits)
                {
                    Assert.IsTrue(PlayerIdentity.TryResolvePortrait(race, portrait.Key, out var descriptor, out var mod));
                    Assert.AreEqual(5, descriptor.Length);
                    Assert.IsNull(mod);
                    Assert.IsFalse(PlayerIdentity.TryResolvePortrait((race + 1) % 11, portrait.Key, out _, out _));
                }
            }
        }

        [TestCase("octopod", 4)]
        [TestCase("bobolian", 6)]
        [TestCase("grey", 7)]
        public void MinorRacesUseTheirOwnPortraitBodies(string word, int body)
        {
            int race = ModRaces.GameRaceWord(word);
            Assert.AreEqual(body, race);
            var portrait = PlayerIdentity.Portraits(race).First();
            Assert.IsTrue(PlayerIdentity.TryResolvePortrait(race, portrait.Key, out var descriptor, out _));
            Assert.AreEqual(body, descriptor[0]);
        }
    }
}
