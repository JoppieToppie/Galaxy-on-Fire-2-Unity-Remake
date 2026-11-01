// ProxySpec.cs
// Multiplayer: an orbit object's whole description as its NetProxy carries it (docs/notes/multiplayer-sync.md "Orbit
// authority"), so any player's game can rebuild it exactly when the game that ran it leaves the orbit: how the copies show
// it (model, scale, name, explosion, hit boxes, turret items, junk) and the SpawnSpec it was made from with every field
// (gun and gun factor, name, loot, route and its waypoint, turret items, the capital ship it belongs to, its fleet-battle
// foe...), plus its cargo now. The specs it points at (capitalHost, battleFoe, routeAround) travel as the ids of their
// proxies (resolved by the caller). Plain C# (Data.WireWriter), the text a FixedString can hold (base64).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public sealed class ProxySpec
    {
        public string model = "";
        public Vector3 scale = Vector3.one;
        public string label = "";
        public float explosionScale = 1f;
        public bool leavesWreck;
        /// <summary>The hit boxes: -1 the cube, a static object's collision id, or FreighterVolumes + ship * 16 + race.</summary>
        public int hitVolume = -1;
        /// <summary>>= 0: a freelance mission's space junk (CombatAssets.junk), no SpawnSpec.</summary>
        public int junkKind = -1;
        public bool missionShip;
        /// <summary>The turret items on its mounts (NpcMountedTurrets.Pack), "" = none.</summary>
        public string turrets = "";
        public int maxHull = -1;
        /// <summary>An event's batch tag << 1 | spawned as an enemy (EventRunner).</summary>
        public int eventFlags;
        /// <summary>A player's hired wingman: never rebuilt by another game.</summary>
        public bool wingman;
        /// <summary>What it was made from (null = not rebuildable: junk). position is left as made.</summary>
        public SpawnSpec spec;
        public List<ItemStack> cargo;
        /// <summary>The proxies (NetworkObject ids) of spec.capitalHost / battleFoe / routeAround, 0 = none.</summary>
        public ulong hostRef, foeRef, aroundRef;
        /// <summary>A capital ship's own state (World.CapitalShip): the fighters it launched, whether it was provoked.</summary>
        public int capitalLaunched;
        public bool capitalProvoked;

        const byte Version = 1;

        public string Encode()
        {
            var w = new WireWriter(256);
            w.Byte(Version);
            w.Text(model);
            w.Float(scale.x); w.Float(scale.y); w.Float(scale.z);
            w.Text(label);
            w.Float(explosionScale);
            w.VarInt(hitVolume);
            w.VarInt(junkKind);
            w.Byte((byte)((leavesWreck ? 1 : 0) | (missionShip ? 2 : 0) | (wingman ? 4 : 0) | (spec != null ? 8 : 0) | (cargo != null ? 16 : 0)));
            w.Text(turrets);
            w.VarInt(maxHull);
            w.VarInt(eventFlags);
            w.VarULong(hostRef);
            w.VarULong(foeRef);
            w.VarULong(aroundRef);
            w.VarInt(capitalLaunched);
            w.Bool(capitalProvoked);
            if (cargo != null) Stacks(w, cargo);
            if (spec != null) WriteSpec(w, spec);
            return Convert.ToBase64String(w.ToArray());
        }

        /// <summary>null = not one of these (another version, damaged).</summary>
        public static ProxySpec Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(text); }
            catch (FormatException) { return null; }
            var r = new WireReader(bytes);
            if (r.Byte() != Version) return null;
            var p = new ProxySpec
            {
                model = r.Text(),
                scale = new Vector3(r.Float(), r.Float(), r.Float()),
                label = r.Text(),
                explosionScale = r.Float(),
                hitVolume = r.VarInt(),
                junkKind = r.VarInt(),
            };
            byte flags = r.Byte();
            p.leavesWreck = (flags & 1) != 0;
            p.missionShip = (flags & 2) != 0;
            p.wingman = (flags & 4) != 0;
            p.turrets = r.Text();
            p.maxHull = r.VarInt();
            p.eventFlags = r.VarInt();
            p.hostRef = r.VarULong();
            p.foeRef = r.VarULong();
            p.aroundRef = r.VarULong();
            p.capitalLaunched = r.VarInt();
            p.capitalProvoked = r.Bool();
            if ((flags & 16) != 0) p.cargo = ReadStacks(r);
            if ((flags & 8) != 0) p.spec = ReadSpec(r);
            return r.Ok ? p : null;
        }

        static void Stacks(WireWriter w, List<ItemStack> list)
        {
            w.VarULong((ulong)list.Count);
            foreach (var s in list) { w.VarInt(s.item); w.VarInt(s.amount); }
        }

        static List<ItemStack> ReadStacks(WireReader r)
        {
            int n = (int)Math.Min(r.VarULong(), 256);
            var list = new List<ItemStack>(n);
            for (int i = 0; i < n && r.Ok; i++) list.Add(new ItemStack(r.VarInt(), r.VarInt()));
            return list;
        }

        static void Ints(WireWriter w, List<int> list)
        {
            w.VarULong((ulong)list.Count);
            foreach (int v in list) w.VarInt(v);
        }

        static List<int> ReadInts(WireReader r)
        {
            int n = (int)Math.Min(r.VarULong(), 256);
            var list = new List<int>(n);
            for (int i = 0; i < n && r.Ok; i++) list.Add(r.VarInt());
            return list;
        }

        static void V3(WireWriter w, Vector3 v) { w.Float(v.x); w.Float(v.y); w.Float(v.z); }
        static Vector3 V3(WireReader r) => new Vector3(r.Float(), r.Float(), r.Float());

        /// <summary>The most route points carried (a level's long patrol is cut; the default patrol is null anyway).</summary>
        public const int MaxRoutePoints = 32;

        /// <summary>Every field of 'spec' (its references aside: the caller's hostRef / foeRef / aroundRef).</summary>
        public static void WriteSpec(WireWriter w, SpawnSpec s)
        {
            w.VarInt((int)s.group);
            w.VarInt(s.race);
            w.VarInt(s.ship);
            V3(w, s.position);
            uint bits = 0;
            void Bit(int i, bool on) { if (on) bits |= 1u << i; }
            Bit(0, s.freighter); Bit(1, s.startsDead); Bit(2, s.asleep); Bit(3, s.inactive); Bit(4, s.alwaysEnemy);
            Bit(5, s.alwaysFriend); Bit(6, s.alwaysNeutral); Bit(7, s.noLoot); Bit(8, s.stationary); Bit(9, s.deadButSelectable);
            Bit(10, s.eventWreck); Bit(11, s.guard); Bit(12, s.radarHidden); Bit(13, s.capitalPart); Bit(14, s.capitalEnhanced);
            Bit(15, s.fleetBattle); Bit(16, s.turretHunter); Bit(17, s.modCapital); Bit(18, s.route != null);
            Bit(19, s.turretItems != null); Bit(20, s.extraLoot != null); Bit(21, s.wreckPrefab != null);
            Bit(22, s.name != null); Bit(23, s.fixedObject != null); Bit(24, s.turretAssembly != null); Bit(25, s.npcType != null);
            w.UInt(bits);
            w.VarInt(s.eventTag);
            w.VarInt(s.hitpoints);
            w.VarInt(s.nameText);
            if (s.name != null) w.Text(s.name);
            w.Float(s.speed);
            w.VarInt(s.missionCrate);
            if (s.fixedObject != null) w.Text(s.fixedObject);
            w.VarInt(s.collisionId);
            w.Float(s.hitRadius);
            w.Float(s.explosionScale);
            if (s.turretAssembly != null) w.Text(s.turretAssembly);
            V3(w, s.rotation);
            w.Float(s.scale);
            w.VarInt(s.turretItem);
            if (s.turretItems != null) Ints(w, s.turretItems);
            w.VarInt(s.lootItem);
            w.VarInt(s.lootAmount);
            if (s.extraLoot != null) Stacks(w, s.extraLoot);
            w.VarInt(s.convoyRole);
            w.VarInt(s.dockingType);
            w.VarInt(s.spacePoints);
            w.VarInt(s.wantedIndex);
            w.VarInt(s.wantedEscortOf);
            w.VarInt(s.gunItem);
            w.Float(s.gunFactor);
            w.VarInt(s.secondaryItem);
            w.Float(s.secondaryFactor);
            w.VarInt(s.hiddenBlueprint);
            w.VarInt(s.modBlueprint);
            w.VarInt(s.pirateEvent);
            w.VarInt(s.eventTier);
            w.VarInt(s.sightingId);
            w.VarInt(s.capital);
            w.Float(s.deathMs);
            w.VarInt(s.empPoints);
            if (s.route != null)
            {
                int n = Math.Min(s.route.points.Count, MaxRoutePoints);
                w.VarULong((ulong)n);
                for (int i = 0; i < n; i++) V3(w, s.route.points[i]);
                w.Bool(s.route.loop);
                w.VarInt(Mathf.Min(s.route.index, Mathf.Max(0, n)));
                w.Bool(s.route.name != null);
                if (s.route.name != null) w.Text(s.route.name);
            }
            if (s.npcType != null) w.Text(s.npcType.key);   // remake mods: its NPC ship type (npcships.json), by key
        }

        /// <summary>WriteSpec's spec; wreckPrefab, when it had one, is the outpost wreck (the only one specs use).</summary>
        public static SpawnSpec ReadSpec(WireReader r)
        {
            var s = new SpawnSpec { group = (NpcGroup)r.VarInt(), race = r.VarInt(), ship = r.VarInt(), position = V3(r) };
            uint bits = r.UInt();
            bool Bit(int i) => (bits & (1u << i)) != 0;
            s.freighter = Bit(0); s.startsDead = Bit(1); s.asleep = Bit(2); s.inactive = Bit(3); s.alwaysEnemy = Bit(4);
            s.alwaysFriend = Bit(5); s.alwaysNeutral = Bit(6); s.noLoot = Bit(7); s.stationary = Bit(8); s.deadButSelectable = Bit(9);
            s.eventWreck = Bit(10); s.guard = Bit(11); s.radarHidden = Bit(12); s.capitalPart = Bit(13); s.capitalEnhanced = Bit(14);
            s.fleetBattle = Bit(15); s.turretHunter = Bit(16); s.modCapital = Bit(17);
            s.eventTag = r.VarInt();
            s.hitpoints = r.VarInt();
            s.nameText = r.VarInt();
            if (Bit(22)) s.name = r.Text();
            s.speed = r.Float();
            s.missionCrate = r.VarInt();
            if (Bit(23)) s.fixedObject = r.Text();
            s.collisionId = r.VarInt();
            s.hitRadius = r.Float();
            s.explosionScale = r.Float();
            if (Bit(24)) s.turretAssembly = r.Text();
            s.rotation = V3(r);
            s.scale = r.Float();
            s.turretItem = r.VarInt();
            if (Bit(19)) s.turretItems = ReadInts(r);
            s.lootItem = r.VarInt();
            s.lootAmount = r.VarInt();
            if (Bit(20)) s.extraLoot = ReadStacks(r);
            s.convoyRole = r.VarInt();
            s.dockingType = r.VarInt();
            s.spacePoints = r.VarInt();
            s.wantedIndex = r.VarInt();
            s.wantedEscortOf = r.VarInt();
            s.gunItem = r.VarInt();
            s.gunFactor = r.Float();
            s.secondaryItem = r.VarInt();
            s.secondaryFactor = r.Float();
            s.hiddenBlueprint = r.VarInt();
            s.modBlueprint = r.VarInt();
            s.pirateEvent = r.VarInt();
            s.eventTier = r.VarInt();
            s.sightingId = r.VarInt();
            s.capital = r.VarInt();
            s.deathMs = r.Float();
            s.empPoints = r.VarInt();
            if (Bit(18))
            {
                int n = (int)Math.Min(r.VarULong(), MaxRoutePoints);
                var route = new Route(false);
                for (int i = 0; i < n && r.Ok; i++) route.points.Add(V3(r));
                route.loop = r.Bool();
                route.index = Mathf.Clamp(r.VarInt(), 0, Mathf.Max(0, n));
                if (r.Bool()) route.name = r.Text();
                s.route = route;
            }
            if (Bit(25)) s.npcType = Modding.ModNpcShips.Get(r.Text());
            if (Bit(21))
            {
                var assets = Application.isPlaying ? CombatAssets.Load() : null;
                s.wreckPrefab = assets != null ? assets.outpostWreck : null;
            }
            return s;
        }
    }
}
