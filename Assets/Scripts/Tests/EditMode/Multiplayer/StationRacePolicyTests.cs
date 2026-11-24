using GoF2Remake.Multiplayer;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class StationRacePolicyTests
    {
        [Test]
        public void GateAllowOverridesFactionDenyAndInheritRestoresIt()
        {
            const string race = "smep:example";
            var defaults = NetPlayerStations.WithRaceAccess(null, race, PlayerStationPermissionState.Deny);
            Assert.IsFalse(NetPlayerStations.RaceAccessAllowed(race, null, defaults));

            var local = NetPlayerStations.WithRaceAccess(null, race, PlayerStationPermissionState.Allow);
            Assert.IsTrue(NetPlayerStations.RaceAccessAllowed(race, local, defaults));

            local = NetPlayerStations.WithRaceAccess(local, race, PlayerStationPermissionState.Inherit);
            Assert.IsNull(local);
            Assert.IsFalse(NetPlayerStations.RaceAccessAllowed(race, local, defaults));
            Assert.IsTrue(NetPlayerStations.RaceAccessAllowed(null, local, defaults));
            Assert.IsTrue(NetPlayerStations.RaceAccessAllowed("unmatched", local, defaults));
        }

        [Test]
        public void GateRulesPersistAndPublicRecordsOmitExemptions()
        {
            const string race = "smep:example";
            var gate = new PlayerJumpgateRecord
            {
                raceAccess = NetPlayerStations.WithRaceAccess(null, race, PlayerStationPermissionState.Deny),
                raceAccessExemptions = new System.Collections.Generic.List<string> { "profile-id" }
            };
            var loaded = JsonUtility.FromJson<PlayerJumpgateRecord>(JsonUtility.ToJson(gate));
            Assert.IsFalse(NetPlayerStations.RaceAccessAllowed(race, loaded.raceAccess, null));
            CollectionAssert.AreEqual(gate.raceAccessExemptions, loaded.raceAccessExemptions);

            var visible = loaded.ToPublicRecord();
            Assert.IsNull(visible.raceAccess);
            Assert.IsNull(visible.raceAccessExemptions);

            var legacy = JsonUtility.FromJson<PlayerJumpgateRecord>("{\"orbitStationIndex\":42}");
            Assert.IsTrue(NetPlayerStations.RaceAccessAllowed(race, legacy.raceAccess, null));
        }
    }
}
