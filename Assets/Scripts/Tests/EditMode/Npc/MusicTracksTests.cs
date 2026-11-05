// MusicTracksTests.cs
// CLAUDE.md "Music" (docs/notes/npcs-and-combat.md): Radar::draw 0x157c6c's flight music category (MusicTracks.Category,
// played by Traffic.UpdateMusic) and the remake's Options > Sound toggles that turn single tracks off: a battle track off
// leaves the calm track playing, a criminal's / Specters' track off gives the usual battle track. Every track is on by
// default. The PlayerPrefs are put back afterwards (DataUiTestKit.PrefsSnapshot).

using System.Linq;
using GoF2Remake.Data;
using NUnit.Framework;
using C = GoF2Remake.Data.MusicTracks;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class MusicTracksTests
    {
        DataUiTestKit.PrefsSnapshot prefs;

        [SetUp] public void Save() => prefs = new DataUiTestKit.PrefsSnapshot();
        [TearDown] public void Restore() => prefs?.Dispose();

        static System.Func<string, bool> AllBut(params string[] off) => t => !off.Contains(t);

        static int Pick(int hostiles, int playing = -1, bool wanted = false, bool specters = false, bool voidBattle = false,
                        bool fleet = false, bool calmHold = false, System.Func<string, bool> on = null) =>
            C.Category(hostiles, fleet, playing, calmHold, wanted, specters, voidBattle, on);

        [Test]
        public void TheOriginalChoiceWithEveryTrackOn()
        {
            Assert.AreEqual(C.Calm, Pick(0));
            Assert.AreEqual(C.Low, Pick(1));
            Assert.AreEqual(C.Low, Pick(2));
            Assert.AreEqual(C.Medium, Pick(3));
            Assert.AreEqual(C.Full, Pick(5));
            Assert.AreEqual(C.Medium, Pick(0, fleet: true), "a fleet battle plays at least Medium");
            Assert.AreEqual(C.Low, Pick(5, playing: C.Low), "a playing battle track is kept until the orbit is calm");
            Assert.AreEqual(C.Calm, Pick(0, playing: C.Full), "a calm orbit brings the calm track back");
            Assert.AreEqual(C.Full, Pick(0, playing: C.Full, calmHold: true), "not at campaign 0x91");
            Assert.AreEqual(C.WantedCategory, Pick(1, playing: C.Low, wanted: true), "only 151 takes over a battle track");
            Assert.AreEqual(C.SpectersFew, Pick(2, specters: true));
            Assert.AreEqual(C.SpectersMany, Pick(5, specters: true));
            Assert.AreEqual(C.Calm, Pick(0, specters: true, wanted: true), "no hostile, no special track");
        }

        [Test]
        public void ABattleTrackOffLeavesTheCalmTrack()
        {
            var noLow = AllBut(C.BattleLow);
            Assert.AreEqual(C.Calm, Pick(1, on: noLow), "red-marked enemies don't cut into the calm music");
            Assert.AreEqual(C.Calm, Pick(2, playing: C.Calm, on: noLow));
            Assert.AreEqual(C.Medium, Pick(3, playing: C.Calm, on: noLow), "the other battle tracks still play");
            Assert.AreEqual(C.Medium, Pick(1, playing: C.Medium, on: noLow), "and are kept as before");
            Assert.AreEqual(C.Calm, Pick(1, playing: C.Medium, on: AllBut(C.BattleMedium)), "a playing track turned off gives way");
            var noBattle = AllBut(C.BattleLow, C.BattleMedium, C.BattleFull);
            for (int n = 1; n <= 8; n++) Assert.AreEqual(C.Calm, Pick(n, on: noBattle), n + " hostiles");
            Assert.AreEqual(C.Calm, Pick(0, fleet: true, on: noBattle), "nor a fleet battle");
        }

        [Test]
        public void TheVoidTrackOffLeavesTheCalmTrack()
        {
            Assert.AreEqual(C.VoidBattle, C.Track(C.Low, true));
            Assert.AreEqual(C.Low, Pick(1, voidBattle: true), "136 by default");
            Assert.AreEqual(C.Calm, Pick(3, voidBattle: true, on: AllBut(C.VoidBattle)));
            Assert.AreEqual(C.Low, Pick(1, on: AllBut(C.VoidBattle)), "elsewhere the battle tracks are unchanged");
        }

        [Test]
        public void ADedicatedTrackOffGivesTheUsualBattleTrack()
        {
            Assert.AreEqual(C.Low, Pick(2, specters: true, on: AllBut(C.Specters1)));
            Assert.AreEqual(C.Full, Pick(6, specters: true, on: AllBut(C.Specters2)));
            Assert.AreEqual(C.SpectersFew, Pick(2, specters: true, on: AllBut(C.Specters2)), "each Specter track its own toggle");
            Assert.AreEqual(C.Medium, Pick(3, wanted: true, on: AllBut(C.Wanted)));
            Assert.AreEqual(C.Low, Pick(1, playing: C.Low, wanted: true, on: AllBut(C.Wanted)), "the criminal doesn't switch the track then");
            Assert.AreEqual(C.Calm, Pick(1, wanted: true, on: AllBut(C.Wanted, C.BattleLow)), "both off: the calm track");
            Assert.AreEqual(C.WantedCategory, Pick(1, wanted: true, on: AllBut(C.BattleLow)), "the criminal's track is its own");
        }

        [Test]
        public void TheTogglesAreSettingsOnByDefault()
        {
            Settings.Reset(Settings.SoundKeys);
            foreach (var t in C.All)
            {
                Assert.IsTrue(Settings.MusicTrackOn(t), t + " on by default (the original's music)");
                CollectionAssert.Contains(Settings.SoundKeys, C.Key(t), "Sound's Default settings resets " + t);
            }
            Settings.SetMusicTrackOn(C.BattleLow, false);
            Assert.IsFalse(C.On(C.BattleLow));
            Assert.IsTrue(C.On(C.BattleMedium));
            Settings.Reset(Settings.SoundKeys);
            Assert.IsTrue(C.On(C.BattleLow));
            Assert.AreEqual(C.All.Length, C.All.Distinct().Count());
        }
    }
}
