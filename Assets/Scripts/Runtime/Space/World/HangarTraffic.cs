// HangarTraffic.cs
// Remake-only: the hangar's other ships come and go (the original's parked ships never move, Level::createScene 0x17).
// The hangar starts with the original's parked ships (0..max, StationLevel). Every 12-35 s one ship either lands on a
// free parked-ship slot (a new one, picked like the parked ships: 70 % the hangar's race, else any race or pirates; the
// emptier the hangar, the likelier; parked at a random yaw like them) or takes off from its slot and leaves through
// the forcefield (HangarFlight). At most one flight is in the air at a time, the player's included, so nothing flies through
// anything: the traffic waits while the player lands or takes off, and a departing player holds over its pad while
// an NPC flight finishes (StationLevel). Never more ships than the original's parked maximum.
// Multiplayer guests (NetHangar): the other players docked here park on the slots too, by client id: one who docks flies
// in, one who takes off flies out (queued like the others: one flight at a time), one already here when this player
// arrives (or with the flights off) is simply parked; a full hangar makes room by removing a parked NPC ship. The random
// traffic never takes a guest away.
// Plain C#: StationLevel creates and ticks it (the NPC traffic not in the owned Kaamo Club, whose parked ships are the
// stored hulls; with the hangar flights off only the guests, parked without flying).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using Random = UnityEngine.Random;
using Object = UnityEngine.Object;

namespace GoF2Remake.World
{
    public sealed class HangarTraffic
    {
        public sealed class Parked
        {
            public GameObject go;
            public int slot, ship;
            /// <summary>Multiplayer: the client id of the player it belongs to, -1 = an NPC ship.</summary>
            public long guest = -1;
            /// <summary>Its parked yaw (game radians) and, multiplayer, its id in the hangar's NPC traffic (the slot in the game
            /// that runs it, NetHangar), -1 = none.</summary>
            public float yaw;
            public int key = -1;
        }

        // ---- multiplayer: one hangar's NPC traffic for everyone docked there (NetHangar) ------------------------

        /// <summary>This game runs the NPC ships (single player always; multiplayer: the hangar's first player, NetHangar).</summary>
        public bool RunsNpcs { get; set; } = true;
        /// <summary>Multiplayer: the ids this game gives its new NPC ships start here ((client id + 1) * 1000), 0 = the slot.</summary>
        public int KeyBase { get; set; }
        int nextKey;
        /// <summary>Multiplayer: the NPC ships never take the last free pad (another player may dock).</summary>
        public bool KeepOneFree { get; set; }
        /// <summary>An NPC ship starts landing on 'slot' (ship, parked yaw) / takes off from 'slot' (the game running them).</summary>
        public event Action<int, int, float> NpcLanding;
        public event Action<int> NpcTakingOff;

        /// <summary>The parked NPC ships (and one landing), for another player's copy: (key, ship, yaw).</summary>
        public List<(int key, int ship, float yaw)> NpcSnapshot()
        {
            var list = new List<(int, int, float)>();
            foreach (var p in parked) if (p.guest < 0 && p.go != null) list.Add((p.key >= 0 ? p.key : p.slot, p.ship, p.yaw));
            if (flying != null && flying.guest < 0 && flight != null && flight.arriving) list.Add((flying.key >= 0 ? flying.key : flying.slot, flying.ship, flying.yaw));
            return list;
        }

        /// <summary>The running game's NPC ships as this copy's: the ones already here stay (by ship type), the rest parked or
        /// removed at once.</summary>
        public void ApplyNpcSnapshot(List<(int key, int ship, float yaw)> want)
        {
            orders.RemoveAll(o => o.npc);
            parked.RemoveAll(p => p.go == null);
            var mine = parked.FindAll(p => p.guest < 0);
            var left = new List<(int key, int ship, float yaw)>();
            foreach (var w in want)
            {
                var same = mine.Find(p => p.ship == w.ship);
                if (same != null) { same.key = w.key; mine.Remove(same); } else left.Add(w);
            }
            foreach (var p in mine) { parked.Remove(p); if (p.go != null) Object.Destroy(p.go); }
            foreach (var w in left) ParkNpc(w.key, w.ship, w.yaw);
        }

        /// <summary>The running game's NPC ship lands ('key' = its slot there) / takes off: the same here.</summary>
        public void RemoteNpcLands(int key, int ship, float yaw) => orders.Add(new GuestOrder { npc = true, arrive = true, key = key, ship = ship, yaw = yaw });
        public void RemoteNpcTakesOff(int key) => orders.Add(new GuestOrder { npc = true, key = key });

        /// <summary>Pads free now (a ship in the air holds its pad; a guest waiting to land counts as one too).</summary>
        int FreePads()
        {
            parked.RemoveAll(p => p.go == null);
            int waiting = orders.FindAll(o => !o.npc && o.arrive).Count;
            return slotCount - parked.Count - (flying != null ? 1 : 0) - waiting;
        }

        /// <summary>A slot for a mirrored NPC ship: its own slot there if free here, else any free one; -1 = none.</summary>
        int NpcSlot(int key)
        {
            parked.RemoveAll(p => p.go == null);
            bool Taken(int i) => parked.Exists(p => p.slot == i) || (flying != null && flying.slot == i);
            if (key >= 0 && key < slotCount && !Taken(key)) return key;
            for (int i = 0; i < slotCount; i++) if (!Taken(i)) return i;
            return -1;
        }

        void ParkNpc(int key, int ship, float yaw)
        {
            int slot = NpcSlot(key);
            if (slot < 0) return;
            var rot = OrbitLayout.RotationToUnity(new Vector3(0f, yaw, 0f));
            var go = spawn(ship, padPosition(slot, ship, rot), rot);
            if (go != null) NpcSpawned?.Invoke(go, ship);
            if (go != null) parked.Add(new Parked { go = go, slot = slot, ship = ship, yaw = yaw, key = key });
        }

        const float MinGapMs = 12000f, MaxGapMs = 35000f;

        readonly StationTables.HangarLane lane;
        readonly int slotCount, max;
        readonly List<Parked> parked;
        readonly Database db;
        readonly Func<int> randomShip;
        readonly Func<int, int, float, float> fitYaw;                 // (slot, ship, game yaw) -> a yaw that fits the pad
        readonly Func<int, int, Quaternion, Vector3> padPosition;     // (slot, ship, parked rotation) -> the ship's parked pivot
        readonly Func<int, Vector3, Quaternion, GameObject> spawn;     // (ship, position, rotation) -> the ship
        /// <summary>Remake: an NPC ship (not a guest) was spawned: (its object, its ship), StationLevel adds its turrets.</summary>
        public Action<GameObject, int> NpcSpawned;
        /// <summary>The hangar camera's own view (not Inspect ship's orbit): where the flights shrink at its edge
        /// (HangarFlight.View; null = the camera as it is).</summary>
        public Func<HangarFlight.View?> View;

        /// <summary>The guest whose ship 'spawn' is making, or whose pad 'padPosition' is placing, now (multiplayer:
        /// StationLevel shows it in that player's build of a customizable ship, at that build's height), -1 = an NPC ship.</summary>
        public long SpawningGuest { get; private set; } = -1;

        HangarFlight flight;
        Parked flying;
        float nextMs;
        readonly bool npcTraffic, flights;

        sealed class GuestOrder { public long id; public int ship, key; public bool arrive, npc; public float yaw; }
        readonly List<GuestOrder> orders = new List<GuestOrder>();

        /// <summary>An NPC ship is in the air (its whole flight: the lane runs over the other pads).</summary>
        public bool Busy => flight != null;

        /// <param name="npcTraffic">The NPC ships come and go (else only the multiplayer guests).</param>
        /// <param name="flights">Ships fly in and out (the lane and the option); else guests just appear and go.</param>
        public HangarTraffic(StationTables.HangarLane lane, int slotCount, int max, List<Parked> parked, Database db,
                             Func<int> randomShip, Func<int, int, Quaternion, Vector3> padPosition, Func<int, Vector3, Quaternion, GameObject> spawn,
                             bool npcTraffic = true, bool flights = true, Func<int, int, float, float> fitYaw = null)
        {
            this.fitYaw = fitYaw ?? ((slot, ship, yaw) => yaw);
            this.npcTraffic = npcTraffic;
            this.flights = flights && lane != null;
            this.lane = lane;
            this.slotCount = slotCount;
            this.max = Mathf.Min(max, slotCount);
            this.parked = parked;
            this.db = db;
            this.randomShip = randomShip;
            this.padPosition = padPosition;
            this.spawn = spawn;
            nextMs = Random.Range(4000f, 12000f);
        }

        /// <summary>'playerFlying': the player's own flight is running or about to (then no new flight starts).</summary>
        public void Update(float dtMs, bool playerFlying)
        {
            if (flight != null)
            {
                flight.Update(dtMs / 1000f);
                if (!flight.Done) return;
                if (flight.arriving) parked.Add(flying);
                else if (flying.go != null) Object.Destroy(flying.go);
                flight = null;
                flying = null;
                nextMs = Random.Range(MinGapMs, MaxGapMs);
                return;
            }
            // Multiplayer: the other players' landings and take-offs first (and the running game's NPC ships').
            // Multiplayer: no pad left (a player docked here): an NPC ship takes off to free one (the game running them).
            if (KeepOneFree && RunsNpcs && !playerFlying && FreePads() < 1)
            {
                var npc = parked.Find(p => p.guest < 0 && p.go != null);
                if (npc != null) { TakeOff(npc); return; }
            }
            // A guest's landing waits until a pad is free (multiplayer: an NPC ship leaves first, above); the orders behind it go on.
            int next = orders.FindIndex(o => o.npc || !o.arrive || !KeepOneFree || FreeSlotNoEvict() >= 0);
            if (!playerFlying && next >= 0)
            {
                var order = orders[next];
                orders.RemoveAt(next);
                if (order.npc)
                {
                    if (order.arrive) LandNpc(order.key, order.ship, order.yaw);
                    else { var n = parked.Find(x => x.guest < 0 && x.key == order.key); if (n != null) TakeOff(n); }
                }
                else if (order.arrive) LandGuest(order.id, order.ship);
                else { var p = parked.Find(x => x.guest == order.id); if (p != null) TakeOff(p); }
                return;
            }
            if (!npcTraffic || !RunsNpcs || playerFlying || (nextMs -= dtMs) > 0f) return;
            nextMs = Random.Range(MinGapMs, MaxGapMs);
            parked.RemoveAll(p => p.go == null);
            // Landings are likelier the emptier the hangar is (80 % empty .. 20 % full), so it stays busy. Multiplayer: never
            // on the last free pad.
            int freePads = slotCount - parked.Count - (flying != null ? 1 : 0);
            bool canLand = parked.Count < max && (!KeepOneFree || freePads > 1);
            bool land = canLand && (parked.Count == 0 || Random.value < Mathf.Lerp(0.8f, 0.2f, (float)parked.Count / max));
            if (land) Land();
            else
            {
                var npcs = parked.FindAll(p => p.guest < 0);
                if (npcs.Count > 0) TakeOff(npcs[Random.Range(0, npcs.Count)]);
            }
        }

        // ---- multiplayer guests (NetHangar) ------------------------------------------------------------------

        /// <summary>Player 'id''s ship here (parked or in the air), null = none.</summary>
        public GameObject GuestShip(long id) => parked.Find(p => p.guest == id)?.go ?? (flying != null && flying.guest == id ? flying.go : null);

        /// <summary>Player 'id' is parked here, landing or about to land.</summary>
        public bool HasGuest(long id) => parked.Exists(p => p.guest == id) || (flying != null && flying.guest == id && flight.arriving)
                                         || orders.Exists(o => o.id == id && o.arrive);

        /// <summary>Player 'id' docked here with ship 'ship': flies in ('fly', the flights on), else parked at once.</summary>
        public void GuestArrives(long id, int ship, bool fly)
        {
            if (HasGuest(id)) return;
            if (fly && flights) orders.Add(new GuestOrder { id = id, ship = ship, arrive = true });
            else ParkGuest(id, ship);
        }

        /// <summary>Player 'id' left: takes off ('fly', the flights on; after landing if it is still coming in), else gone at once.</summary>
        public void GuestLeaves(long id, bool fly)
        {
            if (orders.RemoveAll(o => o.id == id && o.arrive) > 0) return;   // never landed
            if (orders.Exists(o => o.id == id) || (flying != null && flying.guest == id && !flight.arriving)) return;   // leaving already
            fly &= flights;
            if (flying != null && flying.guest == id)
            {
                if (fly) { orders.Add(new GuestOrder { id = id }); return; }   // lands, then takes off again
                if (flying.go != null) Object.Destroy(flying.go);
                flight = null;
                flying = null;
                return;
            }
            var p = parked.Find(x => x.guest == id);
            if (p == null) return;
            if (fly) { orders.Add(new GuestOrder { id = id }); return; }
            parked.Remove(p);
            if (p.go != null) Object.Destroy(p.go);
        }

        /// <summary>A free slot for a guest; a full hangar makes room by removing a parked NPC ship. -1 = none.</summary>
        int FreeSlot()
        {
            parked.RemoveAll(p => p.go == null);
            var free = new List<int>();
            for (int i = 0; i < slotCount; i++)
                if (!parked.Exists(p => p.slot == i) && (flying == null || flying.slot != i)) free.Add(i);
            if (free.Count > 0) return free[Random.Range(0, free.Count)];
            if (KeepOneFree) return -1;   // multiplayer: an NPC ship takes off for it instead (Update)
            var npc = parked.Find(p => p.guest < 0);
            if (npc == null) return -1;
            parked.Remove(npc);
            if (npc.go != null) Object.Destroy(npc.go);
            return npc.slot;
        }

        /// <summary>A free slot without removing any ship, -1 = none.</summary>
        int FreeSlotNoEvict()
        {
            parked.RemoveAll(p => p.go == null);
            for (int i = 0; i < slotCount; i++)
                if (!parked.Exists(p => p.slot == i) && (flying == null || flying.slot != i)) return i;
            return -1;
        }

        Quaternion ParkedYaw(int slot, int ship) => OrbitLayout.RotationToUnity(new Vector3(0f, fitYaw(slot, ship, Random.Range(0, 300) / 100f), 0f));

        void ParkGuest(long id, int ship)
        {
            int slot = FreeSlot();
            if (slot < 0) return;
            var rot = ParkedYaw(slot, ship);
            SpawningGuest = id;
            try
            {
                var go = spawn(ship, padPosition(slot, ship, rot), rot);
                if (go != null) parked.Add(new Parked { go = go, slot = slot, ship = ship, guest = id });
            }
            finally { SpawningGuest = -1; }
        }

        void LandGuest(long id, int ship)
        {
            int slot = FreeSlot();
            if (slot < 0) return;
            SpawningGuest = id;
            try
            {
                var go = spawn(ship, lane.gate, Quaternion.identity);
                if (go == null) return;
                flying = new Parked { go = go, slot = slot, ship = ship, guest = id };
                var engine = HangarFlight.AddEngine(go, false, db, ship, out float volume);
                var rot = ParkedYaw(slot, ship);
                flight = HangarFlight.Arrival(go.transform, lane, padPosition(slot, ship, rot), rot, engine, volume, View?.Invoke());
            }
            finally { SpawningGuest = -1; }
        }

        void Land()
        {
            var free = new List<int>();
            for (int i = 0; i < slotCount; i++) if (!parked.Exists(p => p.slot == i)) free.Add(i);
            for (int i = free.Count - 1; i >= 0; i--) if (flying != null && flying.slot == free[i]) free.RemoveAt(i);
            if (free.Count == 0) return;
            int slot = free[Random.Range(0, free.Count)], ship = randomShip();
            // Parked at a random yaw like the ships already there (Level::createScene: nextInt(300) / 100 rad).
            float yaw = fitYaw(slot, ship, Random.Range(0, 300) / 100f);   // remake: one the pad fits (StationTables.FitParkingYaw)
            int key = KeyBase > 0 ? KeyBase + nextKey++ : slot;   // multiplayer: an id no other game's NPC ship has
            if (!StartNpcLanding(slot, ship, yaw, key)) return;
            NpcLanding?.Invoke(key, ship, yaw);   // multiplayer: the same landing for the others here
        }

        bool StartNpcLanding(int slot, int ship, float yaw, int key)
        {
            var go = spawn(ship, lane.gate, Quaternion.identity);
            if (go == null) return false;
            NpcSpawned?.Invoke(go, ship);
            flying = new Parked { go = go, slot = slot, ship = ship, yaw = yaw, key = key };
            var engine = HangarFlight.AddEngine(go, false, db, ship, out float volume);
            var rot = OrbitLayout.RotationToUnity(new Vector3(0f, yaw, 0f));
            flight = HangarFlight.Arrival(go.transform, lane, padPosition(slot, ship, rot), rot, engine, volume, View?.Invoke());
            return true;
        }

        /// <summary>A mirrored NPC landing (the flights on), else parked at once.</summary>
        void LandNpc(int key, int ship, float yaw)
        {
            int slot = NpcSlot(key);
            if (slot < 0) return;
            if (!flights) { ParkNpc(key, ship, yaw); return; }
            StartNpcLanding(slot, ship, yaw, key);
        }

        void TakeOff(Parked p)
        {
            if (p.go == null) return;
            if (p.guest < 0 && RunsNpcs) NpcTakingOff?.Invoke(p.key >= 0 ? p.key : p.slot);   // multiplayer: the others' copy too
            if (p.guest < 0 && !flights) { parked.Remove(p); Object.Destroy(p.go); return; }
            parked.Remove(p);
            flying = p;
            foreach (var old in p.go.GetComponents<AudioSource>()) Object.Destroy(old);   // a ship that landed earlier
            var engine = HangarFlight.AddEngine(p.go, false, db, p.ship, out float volume);
            flight = HangarFlight.Departure(p.go.transform, lane, engine, volume, View?.Invoke());
        }

        /// <summary>The engine loops pause with their inactive ships in the lounge; start them again.</summary>
        public void ResumeAudio()
        {
            if (flight == null || flying?.go == null) return;
            var src = flying.go.GetComponent<AudioSource>();
            if (src != null && src.clip != null && !src.isPlaying && flying.go.activeInHierarchy) src.Play();
        }
    }
}
