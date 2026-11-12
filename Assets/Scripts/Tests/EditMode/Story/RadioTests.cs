// RadioTests.cs
// Feature: CLAUDE.md "Story", Radio (Radio: a campaign level's radio lines, one at a time, 2 s delay, lines * 2000 + 1500 ms).
// Expected behaviour: Radio::update 0x180380 tests every line's trigger every frame and RadioMessage::triggered 0x17c5d8
// latches it (+0x20), so a trigger true for a single frame is never lost; the remake queues a line that fires while another
// shows instead of replacing it (#132: level 105's one-frame event 10 "bombs away" was lost).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class RadioTests
    {
        sealed class World : IRadioWorld
        {
            public float MissionMs { get; set; }
            public int ScriptEvent { get; set; }
            public int ShipCount => 0;
            public bool ShipDead(int i) => false;
            public bool ShipActive(int i) => false;
            public float ShipHullFraction(int i) => 1f;
            public bool ShipHostile(int i) => false;
            public int RouteIndex => 0;
            public int CrateCargoCaptured => 0;
            public bool StationLocked => false;
            public bool PlayerArmorGone => false;
            public int EnemiesLeft => 1;
            public int FriendsLeft => 1;
            public bool ShipFriendly(int i) => false;
            public bool ShipEmpDisabled(int i) => false;
            public bool ShipInactive(int i) => false;
            public float ShipGameZ(int i) => 0f;
        }

        static Radio TwoLines() => new Radio(new List<RadioLine>
        {
            new RadioLine { text = 2664, trigger = 5, param = 0 },      // time 0
            new RadioLine { text = 2665, trigger = 0x1b, param = 10 },  // level-script event 10
        });

        [Test]
        public void AOneFrameEventWhileAnotherLineShowsIsQueuedNotLost()
        {
            var w = new World();
            var radio = TwoLines();
            radio.Update(16f, w);
            Assert.IsTrue(radio.Triggered(0), "the time-0 line fires at once");
            Assert.IsTrue(radio.Busy);

            // Event 10 holds for one frame while line 0 is still on its delay / display time.
            w.ScriptEvent = 10;
            radio.Update(16f, w);
            w.ScriptEvent = 11;
            Assert.IsTrue(radio.Triggered(1), "latched in the frame its event held");
            Assert.IsFalse(radio.Over(0));

            // Line 0 runs out, then line 1 shows and runs out in its turn.
            for (int i = 0; i < 2000 && !radio.Over(0); i++) radio.Update(16f, w);
            Assert.IsTrue(radio.Over(0));
            Assert.IsTrue(radio.Busy, "line 1 waits its turn, then shows");
            for (int i = 0; i < 2000 && !radio.Over(1); i++) radio.Update(16f, w);
            Assert.IsTrue(radio.Over(1));
            Assert.IsFalse(radio.Busy);
        }

        [Test]
        public void MarkShownDropsTheWaitingLinesItCovers()
        {
            var w = new World { ScriptEvent = 10 };
            var radio = TwoLines();
            radio.Update(16f, w);   // both fire: line 0 shows, line 1 waits
            radio.MarkShown(2);
            Assert.IsFalse(radio.Busy);
            Assert.IsTrue(radio.Over(1));
        }

        [Test]
        public void AFollowerThatMissedTheLeadersLinesCatchesUp()
        {
            var radio = TwoLines();
            radio.Remote = true;
            radio.CatchUp(1);   // the leader shows line 1; line 0's RPC never came
            Assert.IsTrue(radio.Over(0));
            Assert.IsTrue(radio.Triggered(1));
            radio.Update(16f, new World());
            Assert.IsTrue(radio.Busy);   // line 1 shows here now
            radio.CatchUp(1);   // the state again: nothing new
            Assert.IsFalse(radio.Over(1));
        }
    }
}
