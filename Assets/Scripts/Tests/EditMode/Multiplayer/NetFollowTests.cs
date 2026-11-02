// NetFollowTests.cs
// Feature: docs/notes/multiplayer-social.md "Following a squadmate" (NetFollow.Decide): this pilot sets out after the
// followed squadmate once they are placed in another orbit and this pilot is free in space; one trip per place they go
// (goingTo); a destination of one's own (Session.ProgrammedStation other than goingTo) ends it, so does the squadmate
// leaving the squad or the game; never after them into an arena match.
// Expected values: the notes' rules; station numbers are any two stations (70 Dis, 71 another).

using GoF2Remake.Multiplayer;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class NetFollowTests
    {
        const int Here = 70, There = 71, Nowhere = int.MinValue;

        static NetFollow.Step Decide(bool mate = true, bool placed = true, int theirs = There, bool arena = false, bool ready = true,
                                     int mine = Here, int programmed = -1, int goingTo = Nowhere)
            => NetFollow.Decide(mate, placed, theirs, arena, ready, mine, programmed, goingTo);

        [Test]
        public void GoesAfterThemInAnotherOrbit() => Assert.AreEqual(NetFollow.Step.Travel, Decide());

        [Test]
        public void WaitsInTheSameOrbit() => Assert.AreEqual(NetFollow.Step.Wait, Decide(theirs: Here));

        [Test]
        public void WaitsWhileTheyAreBetweenOrbits() => Assert.AreEqual(NetFollow.Step.Wait, Decide(placed: false));

        [Test]
        public void WaitsWhileThisShipIsBusyOrDocked() => Assert.AreEqual(NetFollow.Step.Wait, Decide(ready: false));

        [Test]
        public void NeverIntoAnArenaMatch() => Assert.AreEqual(NetFollow.Step.Wait, Decide(arena: true));

        [Test]
        public void OneTripPerPlace()
        {
            // set out already (or the autopilot was switched off on the way): not again until they move on
            Assert.AreEqual(NetFollow.Step.Wait, Decide(goingTo: There, programmed: There));
            Assert.AreEqual(NetFollow.Step.Wait, Decide(goingTo: There));
            Assert.AreEqual(NetFollow.Step.Travel, Decide(theirs: 72, goingTo: There, programmed: There));
        }

        [Test]
        public void ADestinationOfOnesOwnEndsIt() => Assert.AreEqual(NetFollow.Step.OwnDestination, Decide(programmed: 72, goingTo: There));

        [Test]
        public void LosingTheSquadmateEndsIt() => Assert.AreEqual(NetFollow.Step.Lost, Decide(mate: false, programmed: 72));

        [Test]
        public void NobodyFollowedAtFirst()
        {
            NetFollow.Clear();
            Assert.IsFalse(NetFollow.Active);
            Assert.AreEqual(NetFollow.None, NetFollow.Following);
            Assert.IsNull(NetFollow.Stop());
        }
    }
}
