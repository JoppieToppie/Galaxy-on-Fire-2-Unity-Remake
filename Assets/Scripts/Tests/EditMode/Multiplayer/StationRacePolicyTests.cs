using GoF2Remake.Multiplayer;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class StationRacePolicyTests
    {
        [TestCase(-1, "refinery")]
        [TestCase(-2, "drydock")]
        [TestCase(-3, null)]
        [TestCase(-7, null)]
        [TestCase(1, null)]
        public void OnlyRefineryAndDryDockExteriorIdsAreStructuralModules(int id, string kind)
        {
            Assert.AreEqual(kind, NetPlayerStations.StructuralModuleContactKind(id));
        }

        [TestCase("drydock")]
        [TestCase("refinery")]
        public void ModuleIntegrityMigratesLegacyAndPersistsDamage(string kind)
        {
            var addon = new PlayerStationAddon { kind = kind, level = 1 };
            float max = NetPlayerStations.ModuleIntegrityCapacity(kind);
            Assert.AreEqual(max, NetPlayerStations.ModuleRemainingIntegrity(addon));
            addon.integrityInitialized = true;
            addon.integrityCapacity = max;
            addon.integrity = max / 2;
            var loaded = JsonUtility.FromJson<PlayerStationAddon>(JsonUtility.ToJson(addon));
            Assert.AreEqual(max / 2, NetPlayerStations.ModuleRemainingIntegrity(loaded), .01f);
            loaded.level = 0;
            Assert.AreEqual(0, NetPlayerStations.ModuleRemainingIntegrity(loaded));
        }

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
        public void BlacklistReasonsAreBoundedPersistedAndPrivate()
        {
            Assert.AreEqual("", NetPlayerStations.NormalizeBlacklistReason(null));
            Assert.AreEqual("No docking", NetPlayerStations.NormalizeBlacklistReason("  No docking  "));
            Assert.AreEqual(160, NetPlayerStations.NormalizeBlacklistReason(new string('x', 300)).Length);
            var gate = new PlayerJumpgateRecord
            {
                ownershipType = PlayerStationOwnershipType.Faction,
                blacklist = new System.Collections.Generic.List<PlayerJumpgateBlockEntry>
                {
                    new PlayerJumpgateBlockEntry { type = PlayerJumpgateBlockType.Faction, id = "faction", reason = "Faction embargo" },
                    new PlayerJumpgateBlockEntry { type = PlayerJumpgateBlockType.Profile, id = "pilot", reason = "Repeated attacks" }
                },
                raceAccess = NetPlayerStations.WithRaceAccess(null, "7", PlayerStationPermissionState.Deny, "Security closure")
            };
            var loaded = JsonUtility.FromJson<PlayerJumpgateRecord>(JsonUtility.ToJson(gate));
            Assert.AreEqual("Repeated attacks", NetPlayerStations.MatchingJumpgateBlacklist(loaded, "pilot", "faction").reason);
            Assert.AreEqual("Faction embargo", NetPlayerStations.MatchingJumpgateBlacklist(loaded, "other", "faction").reason);
            Assert.IsNull(NetPlayerStations.MatchingJumpgateBlacklist(loaded, "other", "unrelated"));
            Assert.AreEqual("Security closure", loaded.raceAccess[0].reason);
            Assert.IsNull(loaded.ToPublicRecord().blacklist);
            Assert.IsNull(loaded.ToPublicRecord().raceAccess);
            loaded.ownershipType = PlayerStationOwnershipType.Individual;
            loaded.ownerProfile = "pilot";
            Assert.IsNull(NetPlayerStations.MatchingJumpgateBlacklist(loaded, "pilot", "faction"));
        }

        [Test]
        public void RaceDenialMessageDoesNotChangeAccessDecision()
        {
            var deny = NetPlayerStations.WithRaceAccess(null, "7", PlayerStationPermissionState.Deny, "Embargo");
            Assert.IsFalse(NetPlayerStations.RaceAccessAllowed("7", deny, null));
            var allow = NetPlayerStations.WithRaceAccess(deny, "7", PlayerStationPermissionState.Allow, "Old reason");
            Assert.IsTrue(NetPlayerStations.RaceAccessAllowed("7", allow, null));
            Assert.AreEqual("", allow[0].reason);
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
