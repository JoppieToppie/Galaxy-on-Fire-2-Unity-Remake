using GoF2Remake.Multiplayer;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class StationRacePolicyTests
    {
        [TestCase(20, 15, true, 1250, 850, 400)]
        [TestCase(20, 15, false, 1250, 850, 50)]
        [TestCase(-20, 0, true, 1000, 1000, 0)]
        [TestCase(0, 100, true, 1050, 0, 1050)]
        public void MarketplaceAppliesRaceAdjustmentsToBothSides(int purchase, int sale, bool payOwner,
            long charge, long proceeds, long ownerRevenue)
        {
            Assert.AreEqual((charge, proceeds, ownerRevenue),
                NetPlayerStations.RaceMarketSettlement(1000, 50, purchase, sale, payOwner));
        }

        [Test]
        public void StationOverrideAffectsOnlyTheSelectedFacilityAndRace()
        {
            const string race = "7";
            var defaults = NetPlayerStations.WithFacilityRaceRule(null, new StationFacilityRaceRule
                { facility = "hangar", race = race, state = PlayerStationPermissionState.Deny });
            Assert.IsFalse(NetPlayerStations.FacilityRaceAllowed(race, "hangar", null, defaults));
            Assert.IsTrue(NetPlayerStations.FacilityRaceAllowed(race, "drydock", null, defaults));
            Assert.IsTrue(NetPlayerStations.FacilityRaceAllowed("4", "hangar", null, defaults));
            Assert.IsTrue(NetPlayerStations.FacilityRaceAllowed(null, "hangar", null, defaults));
            var local = NetPlayerStations.WithFacilityRaceRule(null, new StationFacilityRaceRule
                { facility = "hangar", race = race, state = PlayerStationPermissionState.Allow });
            Assert.IsTrue(NetPlayerStations.FacilityRaceAllowed(race, "hangar", local, defaults));
            local = NetPlayerStations.WithFacilityRaceRule(local, new StationFacilityRaceRule
                { facility = "hangar", race = race, state = PlayerStationPermissionState.Inherit });
            Assert.IsFalse(NetPlayerStations.FacilityRaceAllowed(race, "hangar", local, defaults));
        }

        [Test]
        public void DockingRecoveryTracksOwnedPropertyAndKeepsRulesPrivate()
        {
            var station = new PlayerStationRecord();
            station.storageLots.Add(new PlayerStationStorageLot { ownerProfile = "pilot", item = 5, amount = 3 });
            Assert.IsTrue(NetPlayerStations.HasRaceProtectedProperty(station, "pilot"));
            Assert.IsFalse(NetPlayerStations.HasRaceProtectedProperty(station, "other"));
            station.storageLots[0].amount = 0;
            Assert.IsFalse(NetPlayerStations.HasRaceProtectedProperty(station, "pilot"));
            station.facilityRaceRules = NetPlayerStations.WithFacilityRaceRule(null, new StationFacilityRaceRule
                { facility = "docking", race = "7", state = PlayerStationPermissionState.Deny });
            var loaded = JsonUtility.FromJson<PlayerStationRecord>(JsonUtility.ToJson(station));
            Assert.IsFalse(NetPlayerStations.FacilityRaceAllowed("7", "docking", loaded.facilityRaceRules, null));
            Assert.IsNull(loaded.ToPublicRecord().facilityRaceRules);
        }

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
