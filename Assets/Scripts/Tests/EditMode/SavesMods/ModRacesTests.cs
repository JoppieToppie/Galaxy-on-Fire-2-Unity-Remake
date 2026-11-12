// ModRacesTests.cs
// Feature: CLAUDE.md "Mods (remake-only)" > Mod races (ModRaces.cs, Standing.cs, ModWorld.cs): a mod's races.json adds
// races as race numbers from ModRaces.FirstRace, each with its own standing (Session.ModRaceStanding, saved), its
// relations to the other races (NPC vs NPC), and a look race for the game's tables (hangar, bar,
// visitors); systems.json may give a system to a mod race, fly mods' ships only there ("traffic": "mod") and name its locals
// and raiders; ships.json may name mod races as a ship's race and its traffic's races; the lounges of a mod race's system hold
// diplomats who restore a hostile mod race to neutral.
// Expected values: the test mod's own numbers (a guild, a hostile gang and a mod race that is the guild's friend, the
// standings it starts them at, the relations it writes) and the game's rules (hostile at -71, a delict of 5 per kill, x2 on
// Extreme; the game's 35 % diplomat chance; Midorian = hangar / bar 3).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class ModRacesTests : SavesModsTest
    {
        const string Races = @"[
            { 'id': 'guild', 'name': 'Mining Guild', 'look': 'midorian', 'standing': 10, 'relations': { 'gang': 'enemy', 'allies': 'friend' } },
            { 'id': 'gang', 'name': 'The Gang', 'look': 'pirates', 'standing': -100, 'hostile': true },
            { 'id': 'allies', 'name': 'Allies', 'look': 'terran', 'standing': 80 } ]";
        const string Systems = @"[ { 'id': 'rim', 'name': 'Rim', 'race': 'guild', 'security': 1, 'position': [110, 50, 50],
            'traffic': 'mod', 'locals': ['guild', 'allies'], 'raiders': ['gang'] } ]";
        const string Stations = @"[ { 'id': 'rim_one', 'system': 't_fac:rim', 'name': 'Rim One', 'techLevel': 4, 'planet': 3 } ]";
        const string Ships = @"[ { 'id': 'raider', 'name': 'Raider', 'model': 'raider.glb', 'race': 'gang', 'armor': 100, 'cargo': 0,
            'handling': 100, 'price': 0, 'slots': { 'primary': 1, 'secondary': 0, 'turret': 0, 'equipment': 0 },
            'traffic': { 'races': ['gang', 'pirates'] } } ]";

        Database Load()
        {
            string dir = WriteMod("f", "t_fac", "1.0", "", ("races.json", Races), ("systems.json", Systems), ("stations.json", Stations),
                                  ("ships.json", Ships));
            WriteFile(dir, "raider.glb", Dummy);
            ScanAndEnable("t_fac");
            var m = Installed("t_fac");
            Assert.IsFalse(m.Broken, AllMessages(m));
            return Database.Load();
        }

        /// <summary>An emblem stays the same texture through a mods revision while its file is unchanged (ModMaterials'
        /// cache destroyed it at every Mod Builder reload, blanking the race list and the builder's map), a new one when the
        /// file changes.</summary>
        [Test]
        public void AnEmblemOutlivesAReloadUntilItsFileChanges()
        {
            string dir = WriteMod("f", "t_fac", "1.0", "", ("races.json", "[ { 'id': 'guild', 'name': 'Guild', 'icon': 'guild.png' } ]"));
            var png = new Texture2D(4, 4);
            WriteFile(dir, "guild.png", png.EncodeToPNG());
            ScanAndEnable("t_fac");
            var first = ModRaces.Icon(Race("guild"));
            Assert.IsNotNull(first);
            ModManager.Scan();   // a new revision, the file unchanged
            Assert.AreSame(first, ModRaces.Icon(Race("guild")));
            png.SetPixel(0, 0, Color.red);
            WriteFile(dir, "guild.png", png.EncodeToPNG());
            Object.DestroyImmediate(png);
            ModManager.Scan();
            var second = ModRaces.Icon(Race("guild"));
            Assert.IsNotNull(second);
            Assert.AreNotSame(first, second);
        }

        static int Race(string id) { Assert.IsTrue(ModRaces.TryResolve("t_fac:" + id, null, out int r), id); return r; }

        [Test]
        public void ModRacesAreRacesOfTheirOwn()
        {
            Load();
            int guild = Race("guild"), gang = Race("gang"), allies = Race("allies");
            Assert.AreEqual(ModRaces.FirstRace, guild);
            Assert.AreEqual(ModRaces.FirstRace + 1, gang);
            Assert.AreEqual(ModRaces.FirstRace + 2, allies);
            Assert.AreEqual("Mining Guild", Standing.RaceName(guild));
            Assert.AreEqual(3, Standing.Look(guild));
            Assert.AreEqual(8, Standing.Look(gang));
            Assert.AreEqual(3, StationTables.HangarIndex(140, guild), "the guild's hangar: its look's (Midorian)");
            Assert.AreEqual(3, StationTables.BarRace(guild));
            Assert.AreEqual(2, Standing.Look(2), "a game race is its own look");
            Assert.AreEqual(guild, Standing.RaceWord("Mining Guild"));
            Assert.AreEqual(gang, Standing.RaceWord("t_fac:gang"));
        }

        [Test]
        public void EachModRaceHasItsOwnStanding()
        {
            Load();
            int guild = Race("guild"), gang = Race("gang"), allies = Race("allies");
            Assert.AreEqual(10, Standing.Toward(guild), "its \"standing\" until it changes");
            Assert.IsFalse(Standing.IsEnemy(guild));
            Assert.IsTrue(Standing.IsEnemy(gang), "hostile: always an enemy");
            Assert.IsTrue(Standing.IsFriend(allies), "80: a friend");
            Standing.ApplyKill(guild, guild);
            Assert.AreEqual(5, Standing.Toward(guild), "a kill: delict 5");
            ModRaces.SetStanding(guild, -71);
            Assert.IsTrue(Standing.IsEnemy(guild), "hostile at -71");
            ModRaces.SetStanding(gang, 100);
            Assert.IsTrue(Standing.IsEnemy(gang), "a hostile mod race stays an enemy whatever its standing");
            Assert.AreEqual(new[] { 30, 0 }, Session.Standing, "the game's axes untouched");
        }

        [Test]
        public void KillingTheGangPleasesItsEnemies()
        {
            Load();
            int guild = Race("guild"), gang = Race("gang");
            Standing.ApplyKill(gang, guild);
            Assert.AreEqual(11, Standing.Toward(guild), "a hostile mod race's kill in its enemy's system: +1 there, like a pirate's");
        }

        [Test]
        public void RelationsDecideWhoFights()
        {
            Load();
            int guild = Race("guild"), gang = Race("gang"), allies = Race("allies");
            Assert.IsTrue(Standing.RacesHostile(guild, gang), "named enemies");
            Assert.IsTrue(Standing.RacesHostile(gang, guild), "either side's word counts");
            Assert.IsFalse(Standing.RacesHostile(guild, allies), "named friends");
            Assert.IsTrue(Standing.RacesHostile(gang, allies), "a hostile mod race fights everyone it names no friend");
            Assert.IsTrue(Standing.RacesHostile(allies, Standing.Pirate), "the game's pirates fight every mod race");
            Assert.IsFalse(Standing.RacesHostile(allies, 0), "a mod race and a game race: neutral unless they say");
            Assert.AreEqual(gang, Standing.EnemyRaceOf(guild), "the guild's raiders: its hostile enemies");
        }

        [Test]
        public void AModRacesSystemFliesItsOwnTraffic()
        {
            var db = Load();
            int sys = ModWorld.SystemIndexOf("t_fac:rim");
            var s = db.Systems.Find(x => x.index == sys);
            Assert.AreEqual(Race("guild"), s.raceId);
            Assert.IsTrue(ModWorld.ModTrafficOnly(sys));
            CollectionAssert.AreEqual(new[] { Race("guild"), Race("allies") }, ModWorld.Locals(sys));
            CollectionAssert.AreEqual(new[] { Race("gang") }, ModWorld.Raiders(sys));
            Assert.IsFalse(ModWorld.ModTrafficOnly(0), "the game's systems as they are");
            Assert.IsFalse(PirateEventRules.Allowed(db, ModWorld.StationIndexOf("t_fac:rim_one"), s, true), "no GoF2 pirate events there");
        }

        [Test]
        public void ShipsNameModRaces()
        {
            Load();
            int ship = OriginalShips;
            var c = CustomShips.All.FirstOrDefault(x => x.index == ship);
            Assert.IsNotNull(c);
            Assert.AreEqual(Race("gang"), c.race);
            CollectionAssert.AreEqual(new[] { Race("gang"), Standing.Pirate }, c.traffic.races);
        }

        [Test]
        public void ModRaceStandingsAreSaved()
        {
            Load();
            int guild = Race("guild");
            ModRaces.SetStanding(guild, -42);
            Assert.IsTrue(SaveGame.Save(1));
            Session.ResetNewGame();
            Assert.AreEqual(10, Standing.Toward(guild), "a new game: the mod race's own start");
            Assert.IsTrue(SaveGame.Load(1));
            Assert.AreEqual(-42, Standing.Toward(guild));
        }

        [Test]
        public void DiplomatsRestoreAModRaceToNeutral()
        {
            var db = Load();
            int guild = Race("guild"), allies = Race("allies");
            ModRaces.SetStanding(allies, -90);
            int station = ModWorld.StationIndexOf("t_fac:rim_one");
            Agent diplomat = null;
            for (int seed = 0; seed < 200 && diplomat == null; seed++)
            {
                Random.InitState(seed);
                diplomat = AgentGenerator.CreateAgents(db, station).Find(a => a.offer == AgentOffer.Diplomat && a.modRace == allies);
            }
            Assert.IsNotNull(diplomat, "a diplomat for the hostile mod race now and then (35 %)");
            Assert.AreEqual(0, diplomat.race, "it looks like the mod race's look race (Terran)");
            Assert.AreEqual((int)(90 / 100f * 16000f), diplomat.costs);
            Assert.IsFalse(AgentGenerator.CreateAgents(db, station).Exists(a => a.offer == AgentOffer.Diplomat && a.modRace == guild),
                           "none for a mod race that isn't hostile");
        }

        // ---- remake 2026-10-28: the old file name, warnings, campaigns, other players, /standing, standing() ----------

        [Test]
        public void TheOldFactionsFileStillLoads()
        {
            WriteMod("f", "t_fac", "1.0", "", ("factions.json", Races));
            ScanAndEnable("t_fac");
            Assert.IsFalse(Installed("t_fac").Broken, AllMessages(Installed("t_fac")));
            Assert.AreEqual("Mining Guild", Standing.RaceName(Race("guild")), "factions.json: the name mod races had before");
        }

        [Test]
        public void AShipsUnknownRaceIsReportedOnce()
        {
            string dir = WriteMod("f", "t_fac", "1.0", "", ("races.json", Races), ("ships.json", Ships.Replace("'race': 'gang'", "'race': 'gnag'")
                                  .Replace("['gang', 'pirates']", "['gang', 'nobody']")));
            WriteFile(dir, "raider.glb", Dummy);
            ScanAndEnable("t_fac");
            Database.Load();
            Database.Load();   // ShipData runs at every load: still one warning each
            var m = Installed("t_fac");
            Assert.AreEqual(1, m.Warnings.Count(w => w.Contains("\"gnag\" is no race")), AllMessages(m));
            Assert.AreEqual(1, m.Warnings.Count(w => w.Contains("\"nobody\" is no race")), AllMessages(m));
        }

        [Test]
        public void ACampaignSetsItsStartTowardModRaces()
        {
            WriteMod("f", "t_fac", "1.0", "", ("races.json", Races), ("systems.json", Systems), ("stations.json", Stations),
                     ("campaign.json", @"{ 'name': 'Rim', 'startStation': 't_fac:rim_one', 'startShip': 5,
                                           'raceStanding': { 'guild': -60, 't_fac:allies': 150, 'nobody': 5 } }"));
            ScanAndEnable("t_fac");
            var c = ModCampaigns.All().Single(x => x.Id == "t_fac");
            Session.ResetNewGame();
            Assert.IsNotNull(ModCampaigns.Start(Database.Load(), c, out string error), error);
            Assert.AreEqual(-60, Standing.Toward(Race("guild")));
            Assert.AreEqual(100, Standing.Toward(Race("allies")), "clamped to 100");
            Assert.IsTrue(Installed("t_fac").Warnings.Any(w => w.Contains("raceStanding \"nobody\" is no mod race")));
        }

        [Test]
        public void AnotherPlayersStandingsDecideHowTheyAreSeen()
        {
            Load();
            int guild = Race("guild");
            var theirs = new System.Collections.Generic.Dictionary<string, int> { ["t_fac:guild"] = -80 };
            Assert.IsFalse(Standing.IsEnemyWith(guild, 30, 0, -1), "without theirs: this game's (10)");
            Assert.IsTrue(Standing.IsEnemyWith(guild, 30, 0, -1, theirs), "theirs: -80, an enemy");
            Assert.AreEqual(80, Standing.TowardWith(Race("allies"), 30, 0, -1, theirs), "one they never changed: its start");
            // multiplayer carries them as text (NetModRaceStandings)
            var back = new System.Collections.Generic.Dictionary<string, int>();
            Multiplayer.NetModRaceStandings.Decode(Multiplayer.NetModRaceStandings.Encode(theirs), back);
            CollectionAssert.AreEquivalent(theirs, back);
        }

        [Test]
        public void StandingMovesARacesAxisOrAModRace()
        {
            Load();
            Standing.Move(1, 20, false);   // Vossk: the Terran axis's other end
            Assert.AreEqual(10, Session.Standing[0], "30 toward Terran = -30 toward Vossk; +20 there = 10 toward Terran");
            Standing.Move(3, 40, true);
            Assert.AreEqual(-40, Session.Standing[1], "set Midorian to 40: the Nivelian axis at -40");
            Standing.Move(0, 500, false);
            Assert.AreEqual(100, Session.Standing[0], "clamped");
            Assert.IsTrue(Events.EventRunner.Condition("standing(terran) == 100", out string error), error);
            Assert.IsTrue(Events.EventRunner.Condition("standing(guild) == 10", out error), error);
            Assert.IsTrue(Events.EventRunner.Condition("standing(t_fac:gang) == -100", out error), "a hostile mod race: -100 " + error);
            Assert.IsTrue(Events.EventRunner.Condition("standing(pirates) == -100", out error), error);
            Assert.IsFalse(Events.EventRunner.Condition("standing(klingon) > 0", out error));
            Assert.IsNotNull(error, "an unknown race is an error");
        }

        [Test]
        public void PeaceIsMadeWithModRacesToo()
        {
            Load();
            ModRaces.SetStanding(Race("guild"), -90);
            Cheats.MakePeace();
            Assert.AreEqual(0, Standing.Toward(Race("guild")));
            Assert.IsTrue(Standing.IsEnemy(Race("gang")), "a hostile one stays hostile");
        }

        [Test]
        public void TheStandingStepReadsBackFromItsLine()
        {
            var step = Events.QuestSteps.New("standing");
            step["race"] = "t_fac:guild";
            step["amount"] = -15;
            step["set"] = true;
            var problems = new System.Collections.Generic.List<string>();
            string script = Events.QuestFile.Compile(new Newtonsoft.Json.Linq.JObject { ["format"] = "gof2quest", ["title"] = "T", ["kind"] = "quest",
                                                                                     ["steps"] = new Newtonsoft.Json.Linq.JArray(step) }, problems);
            CollectionAssert.IsEmpty(problems, script);
            StringAssert.Contains("standing @a t_fac:guild -15 set", script);
            var back = Events.QuestFile.FromScript(script, "T", new System.Collections.Generic.List<string>());
            var st = ((Newtonsoft.Json.Linq.JArray)back["steps"]).OfType<Newtonsoft.Json.Linq.JObject>().FirstOrDefault(s => (string)s["type"] == "standing");
            Assert.IsNotNull(st, back.ToString());
            Assert.AreEqual("t_fac:guild", (string)st["race"]);
            Assert.AreEqual(-15, (int)st["amount"]);
            Assert.IsTrue((bool)st["set"]);
        }
    }
}
