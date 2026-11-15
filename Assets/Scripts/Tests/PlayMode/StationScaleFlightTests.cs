// StationScaleFlightTests.cs
// Feature: docs/notes/space-scene.md "Station scale" (OrbitScale, Options > Gameplay "Station scale (experimental)"): with
// the stations at 4x, free-play orbits and the story spread (the same steps as StoryLevelTests' smoke run) build and run
// without an error, the station is drawn 4x, and neither the player nor a flying NPC starts inside a grown station or
// structure.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    public class StationScaleFlightTests : PlayModeTest
    {
        const float Scale = 4f;

        static readonly int[] Spread =
        {
            0, 1, 2, 5, 7, 14, 16, 21, 24, 25, 29, 36, 38, 40, 41, 44,
            45, 48, 49, 56, 64, 69, 73, 78, 79, 80, 81, 84,
            89, 91, 94, 102, 105, 114, 125, 135, 139, 144, 145, 154, 157, 158, 162,
        };

        static IEnumerable<int> Steps() => Spread;

        /// <summary>Var Hastra, Tornard (the lopsided one), the Kaamo Club, a Vossk station, Suteo (a gate orbit).</summary>
        static readonly int[] Stations = { 78, 57, 108, 30, 0 };

        static IEnumerable<int> FreePlayStations() => Stations;

        [UnityTest, Category("Smoke")]
        public IEnumerator FreePlayOrbitAtFourTimes([ValueSource(nameof(FreePlayStations))] int station)
        {
            float before = Settings.StationScale;
            try
            {
                Settings.StationScale = Scale;
                FreePlayAt(station);
                Session.LaunchedFromStation = true;
                yield return Load("Space");
                yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null, 60f, "the orbit");
                var level = Find<SpaceLevel>();
                Assert.IsTrue(level.Layout.Scaled, "the orbit is scaled");
                Assert.AreEqual(Scale, OrbitScale.Factor);
                if (level.Station != null) Assert.AreEqual(Scale, level.Station.transform.localScale.x, 0.01f, "the station drawn 4x");
                yield return Run(4f);
                AssertClear(level);
            }
            finally { Settings.StationScale = before; }
        }

        [UnityTest, Category("Smoke")]
        public IEnumerator StoryStepAtFourTimes([ValueSource(nameof(Steps))] int step)
        {
            float before = Settings.StationScale;
            try
            {
                Settings.StationScale = Scale;
                Session.ResetNewGame();
                string scene = Story.StartAtMission(Database.Load(), step);
                Assert.IsFalse(string.IsNullOrEmpty(scene), "a scene to load");
                yield return Load(scene);
                if (scene == "Space")
                {
                    yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().Player != null, 60f, "the story orbit");
                    yield return Run(4f);
                    AssertClear(Find<SpaceLevel>());
                }
                else
                {
                    yield return WaitFor(() => Find<StationLevel>() != null, 60f, "the station");
                    yield return Run(4f);
                }
                Assert.GreaterOrEqual(Story.Index, Story.MissionSelectStart(step), "the story didn't fall back");
            }
            finally { Settings.StationScale = before; }
        }

        /// <summary>The station's volumes at its original size (they are scaled about the station at the origin).</summary>
        static List<CollisionVolume> StationVolumes(SpaceLevel level)
        {
            var o = level.Station != null ? level.Station.GetComponent<Obstacle>() : null;
            return o != null ? CollisionVolume.Scaled(o.volumes, 1f / level.Layout.StationScale) : new List<CollisionVolume>();
        }

        /// <summary>Neither the player nor a flying NPC inside the station's or a structure's volumes (docked ships, fixed
        /// objects and turrets aside).</summary>
        static void AssertClear(SpaceLevel level)
        {
            if (level == null || level.Player == null) return;
            var structures = Obstacle.All.Where(o => o != null && o.isActiveAndEnabled && (o.isStation || o.landmark)).ToList();
            bool docked = level.Docking != null && level.Docking.Busy;
            if (!docked)
                foreach (var o in structures)
                    Assert.IsFalse(o.Touches(level.Player.transform.position, out _) && !o.cubeIsContact,
                                   $"the player starts inside {o.name} (station {Session.StationIndex}, step {Story.Index})");
            var inside = new List<string>();
            foreach (var npc in Object.FindObjectsByType<NpcShip>())
            {
                var s = npc.Spec;
                if (s == null || s.stationary || s.fixedObject != null || s.turretAssembly != null || s.capitalPart || s.placedOn != null) continue;
                // the original puts some there itself (step 48's Vossk escorts "around the origin +-20 000", steering out):
                // a ship whose original spot was inside the original station is not the scale's doing
                bool wasInStation = s.placedOn == null && StationVolumes(level).Any(v => v.Contains(OrbitLayout.ToUnity(s.position)));
                foreach (var o in structures)
                    if (!o.cubeIsContact && o.gameObject != npc.gameObject && !(o.isStation && wasInStation) && o.Touches(npc.transform.position, out _))
                        inside.Add($"{npc.name} in {o.name}");
            }
            Assert.IsEmpty(inside, $"NPCs inside a grown structure (station {Session.StationIndex}, step {Story.Index})");
        }
    }
}
