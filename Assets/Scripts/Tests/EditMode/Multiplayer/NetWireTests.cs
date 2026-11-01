// NetWireTests.cs
// Feature: docs/notes/multiplayer-sync.md "Bandwidth" and "Orbit authority": the pose stream's encoding (positions in 0.05 m
// steps, rotations in 32 bits, batches below 1100 bytes, about 15 bytes an NPC and 21 a player), its send schedule (10 Hz
// near another player, 4 / 2 Hz farther, 2 s when unchanged, a player's ship 20 Hz; alone 2 / 10 s, a player's 1 / 2 s),
// a shot sent once in about 27 bytes, the whole description of an orbit object (ProxySpec: every SpawnSpec field, its
// route and waypoint, cargo, the proxies it points at, a capital ship's waves) and the server's handover rule (every kind
// of object whose game left goes to the runner; a wingman goes with its player).
// Expected values: the steps and rates the notes state; hand-picked poses and specs; a SpawnSpec with every field set to
// a non-default value, compared field by field through reflection (so a field added later and not encoded fails here).

using System.Collections.Generic;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Multiplayer;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class NetWireTests
    {
        // ---- the wire primitives ----

        [Test]
        public void IntegersAndTextRoundTrip()
        {
            var w = new WireWriter(4);
            w.VarULong(0);
            w.VarULong(127);
            w.VarULong(128);
            w.VarULong(ulong.MaxValue);
            w.VarInt(-1);
            w.VarInt(int.MinValue);
            w.Int24(-8388607);
            w.UShort(65535);
            w.Text("Kaamo – Ünïcode");
            w.Float(-123.25f);
            var r = new WireReader(w.ToArray());
            Assert.AreEqual(0UL, r.VarULong());
            Assert.AreEqual(127UL, r.VarULong());
            Assert.AreEqual(128UL, r.VarULong());
            Assert.AreEqual(ulong.MaxValue, r.VarULong());
            Assert.AreEqual(-1, r.VarInt());
            Assert.AreEqual(int.MinValue, r.VarInt());
            Assert.AreEqual(-8388607, r.Int24());
            Assert.AreEqual(65535, r.UShort());
            Assert.AreEqual("Kaamo – Ünïcode", r.Text());
            Assert.AreEqual(-123.25f, r.Float());
            Assert.IsTrue(r.Ok && r.AtEnd);
        }

        [Test]
        public void AShortMessageIsNotOk()
        {
            var r = new WireReader(new byte[] { 0x80, 0x80 });
            r.VarULong();
            Assert.IsFalse(r.Ok, "a varint running past the end");
            var r2 = new WireReader(new byte[] { 200, 1 });
            Assert.AreEqual("", r2.Text());
            Assert.IsFalse(r2.Ok, "a text longer than what is left");
        }

        [Test]
        public void PositionsInGameUnitSteps()
        {
            var points = new[] { Vector3.zero, new Vector3(1234.567f, -89.01f, 0.026f), new Vector3(-400000f, 400000f, 1f) };
            foreach (var p in points)
            {
                var w = new WireWriter();
                WireQuant.WritePosition(w, p);
                Assert.AreEqual(10, w.Length, "1 flag + 3 x 24 bits");
                var back = WireQuant.ReadPosition(new WireReader(w.ToArray()));
                Assert.Less((back - p).magnitude, WireQuant.PositionStep, $"{p}");
            }
            // Beyond +-419 km: the floats, exactly.
            var far = new Vector3(500000f, -1e6f, 3.5f);
            var wf = new WireWriter();
            WireQuant.WritePosition(wf, far);
            Assert.AreEqual(13, wf.Length);
            Assert.AreEqual(far, WireQuant.ReadPosition(new WireReader(wf.ToArray())));
        }

        [Test]
        public void RotationsIn32Bits()
        {
            Random.InitState(7);
            for (int i = 0; i < 500; i++)
            {
                var q = Random.rotationUniform;
                var back = WireQuant.UnpackRotation(WireQuant.PackRotation(q));
                Assert.Less(Quaternion.Angle(q, back), 0.25f, $"{q.eulerAngles}");
                var neg = WireQuant.UnpackRotation(WireQuant.PackRotation(new Quaternion(-q.x, -q.y, -q.z, -q.w)));
                Assert.Less(Quaternion.Angle(q, neg), 0.25f, "q and -q are one rotation");
            }
            Assert.Less(Quaternion.Angle(Quaternion.identity, WireQuant.UnpackRotation(WireQuant.PackRotation(Quaternion.identity))), 0.01f);
        }

        [Test]
        public void Directions()
        {
            foreach (var v in new[] { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, new Vector3(0.3f, -0.5f, -0.8f).normalized })
            {
                var w = new WireWriter();
                WireQuant.WriteDirection(w, v);
                Assert.AreEqual(4, w.Length);
                Assert.Less(Vector3.Angle(v, WireQuant.ReadDirection(new WireReader(w.ToArray()))), 0.05f, $"{v}");
            }
            var wz = new WireWriter();
            WireQuant.WriteDirection(wz, Vector3.zero);
            Assert.AreEqual(Vector3.zero, WireQuant.ReadDirection(new WireReader(wz.ToArray())));
        }

        [Test]
        public void Fractions()
        {
            Assert.AreEqual(-1f, WireQuant.Fraction(WireQuant.Fraction(-1f)), "none");
            Assert.AreEqual(0f, WireQuant.Fraction(WireQuant.Fraction(0f)), "destroyed");
            Assert.AreEqual(1f, WireQuant.Fraction(WireQuant.Fraction(1f)));
            Assert.Greater(WireQuant.Fraction(WireQuant.Fraction(0.0001f)), 0f, "a scratch of hull is still alive");
        }

        // ---- the pose stream ----

        static PoseEntry Npc(ulong id, float x) => new PoseEntry { id = id, position = new Vector3(x, 20f, -300f), rotation = Quaternion.Euler(10f, x, 0f) };

        [Test]
        public void ABatchRoundTrips()
        {
            var look = new PoseLook { engine = true, empShock = true, boost = 127, cloak = 30, hull = 200, shield = 255, armor = 17 };
            var entries = new List<PoseEntry> { Npc(5, 100f), new PoseEntry { id = 70000, position = new Vector3(-5f, 0f, 9f), rotation = Quaternion.identity, hasLook = true, look = look } };
            var batches = PoseBatch.Encode(65535, 1234, entries);
            Assert.AreEqual(1, batches.Count);
            var back = new List<PoseEntry>();
            Assert.IsTrue(PoseBatch.Decode(batches[0], out int seq, out int clock, back));
            Assert.AreEqual(65535, seq);
            Assert.AreEqual(1234, clock);
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(5UL, back[0].id);
            Assert.IsFalse(back[0].hasLook);
            Assert.Less((back[0].position - entries[0].position).magnitude, 0.05f);
            Assert.AreEqual(70000UL, back[1].id);
            Assert.IsTrue(back[1].hasLook);
            Assert.AreEqual(look, back[1].look);
        }

        [Test]
        public void EntrySizes()
        {
            int npc = PoseBatch.Encode(1, 1, new List<PoseEntry> { Npc(300, 50f) })[0].Length - 4;
            int player = PoseBatch.Encode(1, 1, new List<PoseEntry> { new PoseEntry { id = 300, hasLook = true } })[0].Length - 4;
            Assert.LessOrEqual(npc, 16, "an NPC's pose (the old NetworkVariable delta was ~58 bytes)");
            Assert.LessOrEqual(player, 22, "a player's pose and look (~69 bytes before)");
        }

        [Test]
        public void ABigOrbitIsSplit()
        {
            var many = new List<PoseEntry>();
            for (int i = 0; i < 300; i++) many.Add(Npc((ulong)i, i));
            var batches = PoseBatch.Encode(9, 9, many);
            Assert.Greater(batches.Count, 1);
            int total = 0;
            foreach (var b in batches)
            {
                Assert.LessOrEqual(b.Length, PoseBatch.MaxBytes, "unreliable messages aren't split by the transport");
                var back = new List<PoseEntry>();
                Assert.IsTrue(PoseBatch.Decode(b, out int seq, out _, back));
                Assert.AreEqual(9, seq);
                total += back.Count;
            }
            Assert.AreEqual(300, total, "every entry once");
        }

        [Test]
        public void ADamagedBatchIsRefused()
        {
            var b = PoseBatch.Encode(1, 1, new List<PoseEntry> { Npc(1, 1f), Npc(2, 2f) })[0];
            var cut = new byte[b.Length - 3];
            System.Array.Copy(b, cut, cut.Length);
            Assert.IsFalse(PoseBatch.Decode(cut, out _, out _, new List<PoseEntry>()));
        }

        [Test]
        public void SequenceAndClockWrap()
        {
            Assert.IsTrue(PoseBatch.Newer(1, 65535), "1 comes after 65535");
            Assert.IsFalse(PoseBatch.Newer(65535, 1));
            Assert.IsFalse(PoseBatch.Newer(5, 5));
            Assert.AreEqual(0.1f, PoseBatch.ClockGap(50, 65486), 1e-4f);
        }

        [Test]
        public void TheSchedule()
        {
            Assert.AreEqual(0.1f, PoseSchedule.Interval(false, true, 500f), "an NPC near another player: 10 Hz");
            Assert.AreEqual(0.05f, PoseSchedule.Interval(true, true, 500f), "a player's ship near another: 20 Hz");
            Assert.AreEqual(0.25f, PoseSchedule.Interval(false, true, 4000f));
            Assert.AreEqual(0.5f, PoseSchedule.Interval(false, true, 20000f));
            Assert.AreEqual(2f, PoseSchedule.Interval(false, false, 500f), "unchanged: refreshed every 2 s");
            Assert.AreEqual(2f, PoseSchedule.Interval(false, true, float.PositiveInfinity), "nobody else there");
            Assert.AreEqual(10f, PoseSchedule.Interval(false, false, float.PositiveInfinity));
            Assert.AreEqual(1f, PoseSchedule.Interval(true, true, float.PositiveInfinity), "a player's own ship alone (its hull for the server)");
            Assert.IsFalse(PoseSchedule.Changed(Vector3.zero, Quaternion.identity, new Vector3(0.01f, 0f, 0f), Quaternion.Euler(0.2f, 0f, 0f)));
            Assert.IsTrue(PoseSchedule.Changed(Vector3.zero, Quaternion.identity, new Vector3(0.1f, 0f, 0f), Quaternion.identity));
            Assert.IsTrue(PoseSchedule.Changed(Vector3.zero, Quaternion.identity, Vector3.zero, Quaternion.Euler(0f, 1f, 0f)));
        }

        // ---- shots ----

        [Test]
        public void AShotRoundTripsInAbout27Bytes()
        {
            var shot = new NetShotData(41, new Vector3(120.5f, -3f, 999f), new Vector3(300f, -2.5f, 1200f), new Vector3(0f, 0.8f, 0.6f), -1200f, 350f, 1234UL);
            var w = new WireWriter();
            shot.Write(w);
            Assert.LessOrEqual(w.Length, 30, "56 bytes of parameters before");
            var back = NetShotData.Read(new WireReader(w.ToArray()));
            Assert.AreEqual(41, back.item);
            Assert.Less((back.position - shot.position).magnitude, 0.05f);
            Assert.Less((back.velocity - shot.velocity).magnitude, shot.velocity.magnitude * 0.002f);
            Assert.Less(Vector3.Angle(back.up, shot.up), 0.1f);
            Assert.AreEqual(-1200f, back.lifetimeMs, "a coasting rocket's timer below 0");
            Assert.AreEqual(350f, back.homingDelayMs);
            Assert.AreEqual(1234UL, back.targetId);
            var none = new NetShotData(0, Vector3.zero, Vector3.forward, Vector3.up, 0f, 0f, NetShots.NoTarget);
            var w2 = new WireWriter();
            none.Write(w2);
            Assert.AreEqual(NetShots.NoTarget, NetShotData.Read(new WireReader(w2.ToArray())).targetId);
        }

        // ---- an orbit object's description (handover) ----

        /// <summary>A spec with every value field off its default.</summary>
        static SpawnSpec FullSpec()
        {
            var route = new Route(true) { index = 2, name = "Patrol" };
            route.points.AddRange(new[] { new Vector3(1f, 2f, 3f), new Vector3(-4f, 5f, -6f), new Vector3(7f, 8f, 9f) });
            return new SpawnSpec
            {
                group = NpcGroup.Escort, race = 3, ship = 22, freighter = true, position = new Vector3(100f, -200f, 300f), route = route,
                startsDead = true, asleep = true, inactive = true, alwaysEnemy = true, alwaysFriend = true, alwaysNeutral = true,
                eventTag = 77, hitpoints = 4321, noLoot = true, nameText = 1663, name = "[ABC] Garrison", stationary = true, speed = 3f,
                missionCrate = 116, fixedObject = "outpost_pirate", collisionId = 2005, hitRadius = 1500f, deadButSelectable = true,
                eventWreck = true, explosionScale = 8f, turretAssembly = "turret_003_static", rotation = new Vector3(0.1f, 0.2f, 0.3f),
                scale = 6f, turretItem = 30, turretItems = new List<int> { 30, 31 }, guard = true, lootItem = 99, lootAmount = 7,
                extraLoot = new List<ItemStack> { new ItemStack(5, 2) }, convoyRole = SpawnSpec.ConvoyEscort, dockingType = 3,
                spacePoints = 4, radarHidden = true, wantedIndex = 6, wantedEscortOf = 6, gunItem = 12, gunFactor = 1.75f,
                secondaryItem = 48, secondaryFactor = 4f, hiddenBlueprint = 2, modBlueprint = 300, pirateEvent = SpawnSpec.EventBoss,
                eventTier = 5, sightingId = 9, capital = SpawnSpec.CapitalCarrier, capitalPart = true, capitalEnhanced = true,
                fleetBattle = true, turretHunter = true, deathMs = 12000f, empPoints = 900, modCapital = true,
            };
        }

        [Test]
        public void EverySpawnSpecFieldSurvives()
        {
            var spec = FullSpec();
            var p = new ProxySpec
            {
                model = "Assembled/ships/ship_022_midorian", scale = new Vector3(2f, 2f, 2f), label = "Wanted", explosionScale = 3f,
                leavesWreck = true, hitVolume = 100022, missionShip = true, turrets = "22:30,31", maxHull = 4321, eventFlags = 155,
                spec = spec, cargo = new List<ItemStack> { new ItemStack(1, 10), new ItemStack(99, 3) }, hostRef = 1234, foeRef = 5678,
                aroundRef = 99, capitalLaunched = 4, capitalProvoked = true,
            };
            var back = ProxySpec.Decode(p.Encode());
            Assert.IsNotNull(back);
            Assert.AreEqual(p.model, back.model);
            Assert.AreEqual(p.scale, back.scale);
            Assert.AreEqual(p.label, back.label);
            Assert.AreEqual(p.explosionScale, back.explosionScale);
            Assert.AreEqual(p.leavesWreck, back.leavesWreck);
            Assert.AreEqual(p.hitVolume, back.hitVolume);
            Assert.AreEqual(p.missionShip, back.missionShip);
            Assert.AreEqual(p.turrets, back.turrets);
            Assert.AreEqual(p.maxHull, back.maxHull);
            Assert.AreEqual(p.eventFlags, back.eventFlags);
            Assert.AreEqual(1234UL, back.hostRef);
            Assert.AreEqual(5678UL, back.foeRef);
            Assert.AreEqual(99UL, back.aroundRef);
            Assert.AreEqual(4, back.capitalLaunched);
            Assert.IsTrue(back.capitalProvoked);
            Assert.AreEqual(2, back.cargo.Count);
            Assert.AreEqual(99, back.cargo[1].item);
            Assert.AreEqual(3, back.cargo[1].amount);
            var s = back.spec;
            Assert.IsNotNull(s);
            // Every public instance field of SpawnSpec, compared by value (references and lists separately below).
            foreach (var f in typeof(SpawnSpec).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(SpawnSpec) || f.FieldType == typeof(GameObject) || f.FieldType == typeof(Route)
                    || f.FieldType == typeof(List<int>) || f.FieldType == typeof(List<ItemStack>)) continue;
                Assert.AreEqual(f.GetValue(spec), f.GetValue(s), $"SpawnSpec.{f.Name}");
            }
            CollectionAssert.AreEqual(spec.turretItems, s.turretItems);
            Assert.AreEqual(1, s.extraLoot.Count);
            Assert.AreEqual(5, s.extraLoot[0].item);
            Assert.AreEqual(3, s.route.points.Count);
            Assert.AreEqual(spec.route.points[1], s.route.points[1]);
            Assert.IsTrue(s.route.loop);
            Assert.AreEqual(2, s.route.index, "the waypoint it flies to");
            Assert.AreEqual("Patrol", s.route.name);
            Assert.IsNull(s.capitalHost, "references travel as proxy ids");
        }

        [Test]
        public void AMinimalSpecAndJunk()
        {
            var plain = ProxySpec.Decode(new ProxySpec { model = "m", spec = new SpawnSpec { ship = 3 } }.Encode());
            Assert.AreEqual(3, plain.spec.ship);
            Assert.IsNull(plain.spec.route, "the default patrol");
            Assert.IsNull(plain.spec.name);
            Assert.IsNull(plain.spec.fixedObject);
            Assert.IsNull(plain.spec.turretItems, "its type's turret items");
            Assert.AreEqual(-1, plain.spec.hitpoints);
            var junk = ProxySpec.Decode(new ProxySpec { junkKind = 2 }.Encode());
            Assert.AreEqual(2, junk.junkKind);
            Assert.IsNull(junk.spec);
            Assert.IsNull(ProxySpec.Decode("not base64!"));
            Assert.IsNull(ProxySpec.Decode(System.Convert.ToBase64String(new byte[] { 99, 1, 2 })), "another version");
        }

        [Test]
        public void ALongRouteIsCut()
        {
            var route = new Route(false) { index = 100 };
            for (int i = 0; i < 200; i++) route.points.Add(new Vector3(i, 0f, 0f));
            var back = ProxySpec.Decode(new ProxySpec { spec = new SpawnSpec { route = route } }.Encode());
            Assert.AreEqual(ProxySpec.MaxRoutePoints, back.spec.route.points.Count);
            Assert.LessOrEqual(back.spec.route.index, ProxySpec.MaxRoutePoints);
        }

        [Test]
        public void TheStatusRoundTrips()
        {
            var st = new NetProxyStatus { race = 8, relation = NetProxy.Hostile, hull = NetProxyStatus.Hull(0.0001f), radius = 75f,
                                          flags = NetProxyStatus.EmpDisabled | NetProxyStatus.Turned, life = NetProxy.Dying, cloak = 40,
                                          stealItem = 99, stealAmount = 12, roleFlags = 3, killer = 2, routeIndex = 4, empMs = 120 };
            Assert.Greater(st.HullFraction, 0f, "a scratch of hull is not destroyed");
            Assert.IsTrue(st.Has(NetProxyStatus.Turned));
            Assert.IsFalse(st.Has(NetProxyStatus.Asleep));
            var copy = st;
            Assert.IsTrue(st.Equals(copy));
            copy.hull++;
            Assert.IsFalse(st.Equals(copy));
        }

        // ---- the server's handover rule ----

        [Test]
        public void EveryKindIsHandedOverWhenItsGameLeft()
        {
            // Its game left the orbit: a ship, fixed object, turret, capital part, wreck, dying ship, junk (all rebuildable
            // proxies) and a crate go to the runner. Before, only flying traffic ships did; the rest vanished after 5 s.
            Assert.AreEqual(NetHandOverRule.Act.HandOver, NetHandOverRule.Decide(false, false, false, false, true, false, 0f));
            Assert.AreEqual(NetHandOverRule.Act.HandOver, NetHandOverRule.Decide(false, true, false, false, false, false, 0f), "a crate");
            Assert.AreEqual(NetHandOverRule.Act.HandOver, NetHandOverRule.Decide(true, false, true, false, true, false, 0f),
                            "a disconnected player's ship the host holds while flying there");
        }

        [Test]
        public void WhatStaysWithItsGameOrWaits()
        {
            Assert.AreEqual(NetHandOverRule.Act.Keep, NetHandOverRule.Decide(true, false, false, false, true, false, 0f), "its game runs it");
            Assert.AreEqual(NetHandOverRule.Act.Keep, NetHandOverRule.Decide(true, true, false, false, false, false, 0f), "a crate's holder takes its copy");
            Assert.AreEqual(NetHandOverRule.Act.Sweep, NetHandOverRule.Decide(false, false, false, true, true, false, 0f), "a wingman goes with its player");
            Assert.AreEqual(NetHandOverRule.Act.Wait, NetHandOverRule.Decide(false, false, false, false, true, true, 1f), "a squadmate's promotion first");
            Assert.AreEqual(NetHandOverRule.Act.HandOver, NetHandOverRule.Decide(false, false, false, false, true, true, NetHandOverRule.MissionWaitSeconds),
                            "then the runner, as a plain ship");
        }
    }
}
