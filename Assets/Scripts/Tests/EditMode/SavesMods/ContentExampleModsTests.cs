// ContentExampleModsTests.cs
// The JSON-only examples in Modding/Examples (docs/notes/mods.md "The examples"), each about one part of the mod format,
// checked for what each one says it shows (they load without a warning: ExampleModsTests).
//   courier_ships    ships.json: two new ships on the game's models (a dealer, a lounge seller, a Default Economy price,
//                    kept out of NPC traffic), and an override of the Betty's hold (25 -> 35 t).
//   warlord_fleet    npcships.json: a pirate capital ship on the battleship's hull with three turret hardpoints,
//                    escorts, loot and its spawn rules, and a pirate boss on the Hernstein.
//   hired_guns       wingmen.json (a crew's ships and radio lines) and wanted.json (three Most Wanted criminals).
//   tinkerers_bench  items.json, blueprints.json (each way to obtain one, the three autocomplete forms) and gameoptions.json
//                    (an option the blueprints' "available" asks for).
//   kepler_guild     races.json (two races with relations to the game's and to each other) on top of frontier_systems: its
//                    Kepler system given to the guild (a cross-mod override), two guild ships.
//   heavy_flight     flight.json (every ship's flight rules) and one ship's own rules winning over them.
//   lost_shipment    two hand-written .gof2mission files: a bar mission (two lounges, once, from rank 2) with a Lua step
//                    that pays a bonus a kill, and a quest that starts by itself when the mission is done.
//   epilogue         dlc.json: a DLC after Supernova, its intro call and its one mission.
//   sound_pack       sounds/ (a file named like a game sound replaces it: the lock) and music/ (a new track, given to
//                    Pan's orbits by a system override).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ContentExampleModsTests : SavesModsTest
    {
        Database Load(params string[] ids)
        {
            foreach (string id in ids) CopyExample(id);
            ScanAndEnable(ids);
            foreach (string id in ids) Assert.IsEmpty(Installed(id).Warnings, AllMessages(Installed(id)));
            return Database.Load();
        }

        [Test]
        public void CourierShips()
        {
            var db = Load("courier_ships");
            var quillon = db.Ship(ModContent.ShipIndexOf("courier_ships:quillon"));
            var stevedore = db.Ship(ModContent.ShipIndexOf("courier_ships:stevedore"));
            Assert.IsNotNull(quillon);
            Assert.IsNotNull(stevedore);
            Assert.AreEqual("Quillon", GameNames.Ship(quillon.index));
            Assert.Greater(quillon.handling, stevedore.handling, "the courier turns, the hauler carries");
            Assert.Greater(stevedore.cargo, quillon.cargo * 5);
            Assert.AreEqual(1, stevedore.slots.turret);
            Assert.AreEqual(35, db.Ship(0).cargo, "the Betty's hold, overridden");
        }

        [Test]
        public void WarlordFleet()
        {
            Load("warlord_fleet");
            var capital = ModNpcShips.Get("warlord_fleet:iron_tithe");
            Assert.IsNotNull(capital);
            Assert.AreEqual(ModNpcShips.Capital, capital.kind);
            Assert.AreEqual(ModNpcShips.Battleship, capital.looksLike);
            Assert.AreEqual(Standing.Pirate, capital.race);
            Assert.AreEqual(3, capital.hardpoints.Count);
            Assert.AreEqual(3, capital.escorts.min);
            Assert.AreEqual(4, capital.escorts.max);
            Assert.AreEqual(3, capital.loot.items.Count);
            Assert.AreEqual(15f, capital.spawn.chance);
            Assert.IsTrue(capital.Killable, "a mod's own capital ship can be destroyed");
            var boss = ModNpcShips.Get("warlord_fleet:tithe_collector");
            Assert.AreEqual(ModNpcShips.Boss, boss.kind);
            Assert.AreEqual(11, boss.ship, "the Hernstein");
            Assert.IsTrue(boss.loot.keep, "its crate keeps the game's loot too");
        }

        [Test]
        public void HiredGuns()
        {
            var db = Load("hired_guns");
            var crew = ModWingmen.Of(db, 5);
            Assert.IsNotNull(crew, "a crew in the ship it flies");
            Assert.AreEqual(60, crew.chance);
            CollectionAssert.AreEquivalent(new[] { 5, 27, 9, 18, 30 }, crew.ships);
            foreach (string kind in WingmanCrew.LineKinds) Assert.IsTrue(crew.lines.ContainsKey(kind) && crew.lines[kind].Count > 0, kind);
            var wanted = db.Wanted.Where(w => w.key != null && w.key.StartsWith("hired_guns:")).ToList();
            Assert.AreEqual(3, wanted.Count);
            var hask = wanted.Single(w => w.key == "hired_guns:hask_the_ledger");
            Assert.AreEqual(29, hask.ship);
            Assert.AreEqual(320000, hask.reward);
            Assert.AreEqual(1, hask.requiredBounties);
            Assert.AreEqual("stat(piratekills) >= 15", hask.available);
            Assert.IsFalse(wanted.Single(w => w.key == "hired_guns:ilvane_sorr").male);
        }

        [Test]
        public void TinkerersBench()
        {
            var db = Load("tinkerers_bench");
            var plating = ModBlueprints.Of(ModContent.ItemIndexOf("tinkerers_bench:patchwork_plating"));
            var thruster = ModBlueprints.Of(ModContent.ItemIndexOf("tinkerers_bench:hand_tuned_thruster"));
            var repeater = ModBlueprints.Of(ModContent.ItemIndexOf("tinkerers_bench:scrapyard_repeater"));
            Assert.IsTrue(plating.unlocked && !plating.autocomplete, "known from the start, built by hand");
            Assert.IsTrue(thruster.autocomplete);
            Assert.AreEqual(8000, thruster.autocompletePrice);
            Assert.Greater(thruster.loungeChance, 0f, "a lounge seller");
            Assert.IsTrue(repeater.autocomplete);
            Assert.AreEqual(-1, repeater.autocompletePrice, "the game's price");
            Assert.Greater(repeater.derelictChance, 0f, "a derelict freighter");
            Assert.AreEqual(2, repeater.drops.Count, "and destroyed ships");
            Assert.AreEqual(23, db.Item(repeater.product).blueprint[0].item, "a weapon among the ingredients");
            var option = ModGameOptions.Find("tinkerers_bench:workbench");
            Assert.IsNotNull(option);
            Assert.IsTrue(option.defaultOn);
            StringAssert.Contains("option(tinkerers_bench:workbench)", repeater.available);
        }

        [Test]
        public void KeplerGuild()
        {
            var db = Load("frontier_systems", "kepler_guild");
            var guild = ModRaces.Get("kepler_guild:guild");
            var jumpers = ModRaces.Get("kepler_guild:jumpers");
            Assert.IsNotNull(guild);
            Assert.IsNotNull(jumpers);
            Assert.IsTrue(jumpers.hostile);
            Assert.AreEqual(ModRaces.Enemy, ModRaces.Relation(guild.race, jumpers.race));
            Assert.AreEqual(ModRaces.Friend, ModRaces.Relation(guild.race, 0));
            Assert.AreEqual(ModRaces.Enemy, ModRaces.Relation(guild.race, 1));
            Assert.IsTrue(ModWorld.TryResolveSystem("frontier_systems:kepler", out int kepler));
            Assert.AreEqual(guild.race, db.Systems.Find(s => s.index == kepler).raceId, "the other mod's system, given to the guild");
            Assert.AreEqual(guild.race, ((CustomShipData)db.Ship(ModContent.ShipIndexOf("kepler_guild:guild_warden"))).race);
            Assert.AreEqual(guild.race, ((CustomShipData)db.Ship(ModContent.ShipIndexOf("kepler_guild:guild_prospector"))).race, "a race named by its full key");
        }

        [Test]
        public void HeavyFlight()
        {
            var db = Load("heavy_flight");
            var any = ModFlight.RulesFor(db.Ship(0));
            Assert.AreEqual(252f, any.turnDecayDivisor, "flight.json: every ship");
            Assert.AreEqual(1600f, any.dodgeDurationMs);
            Assert.AreEqual(600f, any.turnRateScale);
            var rhino = ModFlight.RulesFor(db.Ship(51));
            Assert.AreEqual(400f, rhino.turnDecayDivisor, "the Rhino's own rule wins");
            Assert.AreEqual(2200f, rhino.dodgeDurationMs);
            Assert.AreEqual(600f, rhino.turnRateScale, "what it doesn't name still comes from flight.json");
            Assert.AreEqual(-1, ModFlight.ForcedStyle, "the player keeps the flight style");
            Assert.AreNotEqual(252f, FlightRules.Default.turnDecayDivisor, "the game's own rules are untouched");
        }

        static string Script(string mod, string file)
        {
            var issues = new List<string>();
            string script = QuestFile.Compile(Installed(mod).Source.ReadText("events/" + file), issues);
            Assert.IsEmpty(issues, string.Join("; ", issues));
            return script;
        }

        [Test]
        public void LostShipment()
        {
            Load("lost_shipment");
            var mod = Installed("lost_shipment");
            var lines = EventsTestUtil.Lines(Script("lost_shipment", "lost_container.gof2mission"));
            CollectionAssert.Contains(lines, "mission kind mission");
            CollectionAssert.Contains(lines, "mission once");
            CollectionAssert.Contains(lines, "mission stations 21, 70");
            string lua = lines.Single(l => l.StartsWith("lua "));
            StringAssert.StartsWith("lua bonus ", lua);
            CollectionAssert.AreEqual(new[] { "events/lost_container.gof2mission" }, ModScripts.LuaEventFiles(mod), "its Lua step: a script mod");

            // The Lua step as the mission runs it: 4 kills, 2000 credits.
            ModScripts.ApprovedOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                ModScripts.SetApproved(mod, true);
                ModScripts.Sync(true);
                Session.Credits = 100;
                string code = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(lua.Substring(lua.LastIndexOf(' ') + 1)));
                Assert.IsTrue(ModScripts.RunCode("lost_shipment", code, "lost_container", e => e == "kills" ? 4 : 0, (k, v) => { }, out double? bonus, out string error), error);
                Assert.AreEqual(2000, bonus);
                Assert.AreEqual(2100, Session.Credits);
            }
            finally { ModScripts.StopAll(); ModScripts.ApprovedOverride = null; }

            var quest = EventsTestUtil.Lines(Script("lost_shipment", "marta_calls_back.gof2mission"));
            CollectionAssert.Contains(quest, "mission kind quest");
            Assert.IsTrue(quest.Any(l => l.StartsWith("mission starts") && l.Contains("quest(lost_container) == 2")), "it waits for the bar mission");
        }

        [Test]
        public void Epilogue()
        {
            Load("epilogue");
            var dlc = ModDlcs.Of("epilogue");
            Assert.IsNotNull(dlc);
            Assert.AreEqual("epilogue_story", dlc.quest);
            Assert.AreEqual(1, dlc.index);
            Assert.AreEqual(1, ModDlcs.Place("epilogue"), "right after Supernova");
            StringAssert.Contains("Maissa", dlc.Intro);
            var lines = EventsTestUtil.Lines(Script("epilogue", "epilogue_story.gof2mission"));
            CollectionAssert.Contains(lines, "mission kind quest");
        }

        [Test]
        public void SoundPack()
        {
            var db = Load("sound_pack");
            Assert.IsTrue(ModSounds.Has("Target_Lock_v08"), "the game's lock sound, replaced");
            Assert.IsTrue(ModMusic.Has("calm_drift"), "a new track");
            int pan = db.Stations.First(st => st.system == 1).index;
            Assert.AreEqual("calm_drift", ModWorld.SpaceMusicName(pan), "Pan's orbits play it");
            Assert.IsNull(ModWorld.SpaceMusicName(db.Stations.First(st => st.system == 14).index), "the other systems keep the game's");
        }
    }
}
