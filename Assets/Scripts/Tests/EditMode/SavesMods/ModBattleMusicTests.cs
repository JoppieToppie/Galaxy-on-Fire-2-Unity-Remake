// ModBattleMusicTests.cs
// Feature: CLAUDE.md "Mods" > Music: systems.json "battleMusic" (ModWorld.BattleMusicName, Traffic's fight music): one
// track for every battle category, or three for low / medium / full; any other count is reported and ignored; an override
// of an original system takes it too.

using GoF2Remake.Data;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModBattleMusicTests : SavesModsTest
    {
        const string ModId = "t_bm";

        static int StationOf(Database db, int system) => db.Stations.Find(s => s.system == system).index;

        [Test]
        public void OneTrackOrThreeByTheHostilesAround()
        {
            WriteMod("bm", ModId, "1.0", "",
                ("systems.json", @"[ { 'id': 'one', 'name': 'One', 'race': 0, 'security': 1, 'position': [40, 40, 40], 'gates': [15], 'battleMusic': 'fight' },
                                     { 'override': 0, 'battleMusic': ['low', 'mid', 'full'] },
                                     { 'override': 1, 'battleMusic': ['low', 'mid'] } ]"),
                ("stations.json", @"[ { 'id': 'st', 'system': 't_bm:one', 'name': 'St', 'techLevel': 3, 'planet': 1 } ]"));
            ScanAndEnable(ModId);
            var mod = Installed(ModId);
            Assert.IsFalse(mod.Broken, AllMessages(mod));
            var db = Database.Load();
            int one = StationOf(db, ModWorld.SystemIndexOf(ModId + ":one"));
            for (int c = 1; c <= 3; c++) Assert.AreEqual("fight", ModWorld.BattleMusicName(one, c));
            Assert.IsNull(ModWorld.BattleMusicName(one, 0), "calm is the space music");
            int first = StationOf(db, 0);
            Assert.AreEqual("low", ModWorld.BattleMusicName(first, 1));
            Assert.AreEqual("mid", ModWorld.BattleMusicName(first, 2));
            Assert.AreEqual("full", ModWorld.BattleMusicName(first, 3));
            Assert.IsNull(ModWorld.BattleMusicName(StationOf(db, 1), 1), "two tracks: ignored");
            StringAssert.Contains("battleMusic", AllMessages(mod));
        }
    }
}
