// DebugSpawner.cs
// Remake-only testing tools for the pause menu's Debug page (CheatsCatalog.Spawns, .Destroy): put any ship (race, model, behaviour) or any
// assembled object (assemblies.json: ships, stations, level props, effects...) in front of the player. Spawned ships are ordinary
// traffic (Traffic.SpawnShip: they fight, drop loot, count for the music); objects are plain scenery (no collision, no lock)
// that stays until the level is left; KillAround destroys what is around the player. Nothing here is in the original.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public static class DebugSpawner
    {
        const float M = 0.05f;

        public enum Behaviour { Hostile, Normal, Friendly, Neutral }

        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;

        /// <summary>Ship 'ship' of 'race' 400 m ahead of the player (hostile / by the standings / friendly / neutral), or at
        /// 'at' (a Unity position in this orbit, multiplayer's /spawn ... at x y z); 'count' of them side by side, 60 m apart;
        /// 'name' (/spawn ... named): the lock plate's name (KIPlayer+0x18 as a literal, SpawnSpec.name), numbered for
        /// several; 'hullFactor' / 'gunFactor' (remake, /spawn ... hull / guns: a mission's tougher ships) scale its hull and
        /// its gun's damage; the result text.</summary>
        public static string SpawnShip(SpaceLevel level, int race, int ship, Behaviour behaviour, int count = 1, Vector3? at = null, int eventTag = 0,
                                       string name = null, System.Collections.Generic.IList<int> turrets = null, float hullFactor = 1f, float gunFactor = 1f)
        {
            if (level == null || level.Traffic == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            var p = level.Player.transform;
            var centre = at ?? p.position + p.forward * 400f + p.up * 40f;
            int made = 0;
            // Remake mods: a mod's capital ship or freighter (ships.json "traffic": { "capital" }) flies the freighter path, with
            // its subsystems, parked clear ahead of the player (its length out) unless 'at' is given.
            bool capital = Modding.ModCapitalShips.KindOf(ship) != null;
            if (capital && at == null)
            {
                float length = Mathf.Max(400f, Modding.ModTraffic.HullLength(ship));
                centre = p.position + p.forward * (length * 1.2f + 300f);
            }
            for (int i = 0; i < count; i++)
            {
                float side = (i - (count - 1) * 0.5f) * (capital ? Mathf.Max(600f, Modding.ModTraffic.HullLength(ship) * 1.1f) : 60f);
                var spec = capital ? Modding.ModCapitalShips.Spawn(ship, race, ToGame(centre + p.right * side)) : new SpawnSpec
                {
                    group = NpcGroup.Raider,
                    race = race,
                    ship = ship,
                    freighter = ship == 15,
                    position = ToGame(centre + p.right * side),
                    alwaysEnemy = behaviour == Behaviour.Hostile,
                    alwaysFriend = behaviour == Behaviour.Friendly,
                    alwaysNeutral = behaviour == Behaviour.Neutral,
                    eventTag = eventTag,
                    name = string.IsNullOrEmpty(name) ? null : count > 1 ? $"{name} {i + 1}" : name,
                    // Remake (generic turrets): turret items on its turret mounts (/spawn ... turrets), null = its type's.
                    turretItems = turrets != null ? new System.Collections.Generic.List<int>(turrets) : null,
                };
                if (capital)
                {
                    spec.alwaysEnemy = behaviour == Behaviour.Hostile;
                    spec.alwaysFriend = behaviour == Behaviour.Friendly;
                    spec.alwaysNeutral = behaviour == Behaviour.Neutral;
                    spec.eventTag = eventTag;
                    if (!string.IsNullOrEmpty(name)) spec.name = count > 1 ? $"{name} {i + 1}" : name;
                    if (turrets != null) spec.turretItems = new System.Collections.Generic.List<int>(turrets);
                }
                Scale(spec, hullFactor, gunFactor);
                if (level.Traffic.SpawnShip(spec) != null) made++;
            }
            level.Traffic.ConnectPlayers();   // the new ships and the others see each other (targets, hit lists)
            string shown = ShipName(level.Database, ship);
            if (!string.IsNullOrEmpty(name)) shown = $"{name} ({shown})";
            return made == 0 ? string.Format(Localization.Extra("debugSpawnFailed", "{0} couldn't be spawned."), shown)
                 : made == 1 ? string.Format(Localization.Extra("debugShipSpawned", "{0} spawned."), shown)
                 : string.Format(Localization.Extra("debugShipsSpawned", "{0} x {1} spawned."), made, shown);
        }

        /// <summary>A spawn's hull x 'hullFactor' (its own hull points, else its type's: TrafficPlan.ApplyNpcType's rule) and gun
        /// damage x 'gunFactor' (a fighter without a gun of its own gets its race's, which NpcShip.SetGun scales).</summary>
        public static void Scale(SpawnSpec spec, float hullFactor, float gunFactor)
        {
            if (!Mathf.Approximately(hullFactor, 1f) && hullFactor > 0f)
            {
                int baseHull = spec.hitpoints > 0 && spec.hitpoints < 9999999 ? spec.hitpoints : NpcTables.Hull(spec.freighter ? 1 : 0, spec.ship);
                spec.hitpoints = Mathf.Max(1, Mathf.RoundToInt(baseHull * hullFactor));
            }
            if (!Mathf.Approximately(gunFactor, 1f) && gunFactor > 0f && !spec.freighter)
            {
                spec.gunFactor *= gunFactor;
                if (spec.gunItem < 0) spec.gunItem = NpcTables.GunItemFor(spec.race, false);
            }
        }

        /// <summary>Remake (events, quests: "spawn ... wreck"): a hackable freighter wreck (TrafficPlan.HackableWreck; race 0-3,
        /// else rolled) 1.5 km ahead of the player or at 'at' (a Unity position in this orbit); 'name' its name on the HUD
        /// (else "Damaged Freighter"). Hacked once (SpawnSpec.eventWreck), which fires "on hacked" (SpaceHooks.WreckHacked).
        /// The result text.</summary>
        public static string SpawnWreck(SpaceLevel level, int race, Vector3? at = null, string name = null)
        {
            if (level == null || level.Traffic == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            var p = level.Player.transform;
            var spec = TrafficPlan.HackableWreck(race, ToGame(at ?? p.position + p.forward * 1500f));
            spec.eventWreck = true;
            if (!string.IsNullOrEmpty(name)) spec.name = name;
            var made = level.Traffic.SpawnShip(spec);
            level.Traffic.ConnectPlayers();
            string shown = string.IsNullOrEmpty(name) ? Localization.Get(3211) : name;
            return made == null ? string.Format(Localization.Extra("debugSpawnFailed", "{0} couldn't be spawned."), shown)
                                : string.Format(Localization.Extra("debugShipSpawned", "{0} spawned."), shown);
        }

        /// <summary>A capital ship (SpawnSpec.Capital*) with the remake's enhancements (CapitalShips: turrets, escorts, killable,
        /// whatever the option says) about 3.5 km ahead of the player; the result text. 'turretItem' >= 0 (remake, generic
        /// turrets): its turret objects are that turret item (SpawnSpec.turretItem).</summary>
        public static string SpawnCapital(SpaceLevel level, int kind, int turretItem = -1)
        {
            if (level == null || level.Traffic == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            var p = level.Player.transform;
            var list = new System.Collections.Generic.List<SpawnSpec>();
            TrafficPlan.AddCapitalShipAt(level.Database, list, kind, ToGame(p.position + p.forward * 3500f), true);
            if (turretItem >= 0) foreach (var s in list) if (s.turretAssembly != null) s.turretItem = turretItem;
            foreach (var s in list) level.Traffic.SpawnShip(s);
            level.Traffic.ConnectPlayers();
            return string.Format(Localization.Extra("debugShipSpawned", "{0} spawned."), CapitalName(kind));
        }

        /// <summary>Remake mods: one of the NPC ship types (npcships.json, Modding.ModNpcShips: the game's five, the pirate boss
        /// among them, and the mods' own) with its turrets, escorts and rules, about 3.5 km ahead of the player (a capital ship
        /// 2 hull lengths) or at 'at' (a Unity position in this orbit: events and missions, "spawn type ..."); 'name' its lock
        /// plate's name, 'eventTag' the event's batch (its escorts too); the result text.</summary>
        public static string SpawnNpcType(SpaceLevel level, Modding.NpcShipType type, Vector3? at = null, string name = null, int eventTag = 0)
        {
            if (level == null || level.Traffic == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            if (type == null) return Localization.Extra("debugNoNpcTypes", "No mod NPC ships are on.");
            var p = level.Player.transform;
            float ahead = 3500f;
            if (type.ship >= 0 && type.kind != Modding.ModNpcShips.Boss) ahead = Mathf.Max(ahead, 2f * Modding.ModTraffic.HullLength(type.ship));
            var list = new System.Collections.Generic.List<SpawnSpec>();
            // A type without a race of its own: pirates for a boss (the pirate boss's), else Terran.
            int race = type.race >= 0 ? type.race : type.kind == Modding.ModNpcShips.Boss ? Standing.Pirate : 0;
            int first = TrafficPlan.AddNpcType(level.Database, list, type, race, ToGame(at ?? p.position + p.forward * ahead));
            if (!string.IsNullOrEmpty(name) && first < list.Count) { list[first].name = name; list[first].nameText = -1; }
            foreach (var s in list) { if (eventTag != 0) s.eventTag = eventTag; level.Traffic.SpawnShip(s); }
            level.Traffic.ConnectPlayers();
            return string.Format(Localization.Extra("debugShipSpawned", "{0} spawned."), !string.IsNullOrEmpty(name) ? name : type.name ?? type.key);
        }

        public static string CapitalName(int kind) => kind == SpawnSpec.CapitalCarrier ? CapitalShips.CarrierName
            : kind == SpawnSpec.CapitalVossk ? $"{Localization.Get(407)} {Localization.Get(1667)}" : $"{Localization.Get(406)} {Localization.Get(1667)}";

        /// <summary>A fleet battle (TrafficPlan.AddFleetBattle: a Terran and a Vossk capital ship with their wings) centred about
        /// 7 km ahead of the player, the two side by side across the player's view; the result text.</summary>
        public static string SpawnFleetBattle(SpaceLevel level)
        {
            if (level == null || level.Traffic == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            var p = level.Player.transform;
            var list = new System.Collections.Generic.List<SpawnSpec>();
            TrafficPlan.AddFleetBattle(level.Database, list, Session.StationIndex, ToGame(p.position + p.forward * 7000f));
            foreach (var s in list) level.Traffic.SpawnShip(s);
            level.Traffic.ConnectPlayers();
            return Localization.Extra("debugFleetBattle", "Fleet battle spawned ahead.");
        }

        /// <summary>An assembled object ahead of the player, far enough out for its size, facing the player, or centred on 'at'
        /// (a Unity position in this orbit, multiplayer's /object ... at x y z); 'name' (/spawn ... named): a HUD marker with
        /// that name on it (Navigation.Kind.Marker: the bracket, name and distance near the crosshair, never locked); the
        /// result text.</summary>
        public static string SpawnObject(SpaceLevel level, string assembly, Vector3? at = null, string name = null)
        {
            if (level == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            var prefab = AssembledObject.LoadPrefab(level.Database.AssemblyByName(assembly));
            if (prefab == null) return string.Format(Localization.Extra("debugSpawnFailed", "{0} couldn't be spawned."), assembly);
            var p = level.Player.transform;
            // An object with the original's collision volumes (axis-aligned, never rotated: Level::getBoundingVolume) keeps
            // its own heading; anything else faces the player and gets a box around its model.
            int collisionId = StaticCollisionId(assembly);
            var go = Object.Instantiate(prefab, p.position, collisionId >= 0 ? Quaternion.identity : Quaternion.LookRotation(-p.forward, p.up));
            go.name = "Debug " + assembly + (string.IsNullOrEmpty(name) ? "" : " " + name);
            var bounds = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            float radius = Mathf.Max(bounds.extents.magnitude, 5f);
            if (at.HasValue) go.transform.position = at.Value + (go.transform.position - bounds.center);
            else go.transform.position += p.forward * (radius + 150f) + (go.transform.position - bounds.center);
            // Collision: the player slides along it, NPC fighters steer out of it (Obstacle).
            var obstacle = go.AddComponent<Obstacle>();
            obstacle.projectFromVolume = collisionId >= 0;
            if (collisionId >= 0) obstacle.volumes = CollisionVolume.ForStaticObject(collisionId);
            if (obstacle.volumes == null || obstacle.volumes.Count == 0)
            {
                var placed = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) placed.Encapsulate(r.bounds);
                obstacle.volumes = new System.Collections.Generic.List<CollisionVolume> { CollisionVolume.Box(placed.center - go.transform.position, placed.extents) };
            }
            HullCollision.Attach(go, null, obstacle);   // the player slides along its real shape (HullCollision)
            if (!string.IsNullOrEmpty(name) && level.Navigation != null)
            {
                // The marker on the model's centre (a child, so it stays with it).
                var centre = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) centre.Encapsulate(r.bounds);
                var mark = new GameObject("Marker");
                mark.transform.SetParent(go.transform, false);
                mark.transform.position = centre.center;
                level.Navigation.Targets.Add(new Navigation.Target { kind = Navigation.Kind.Marker, transform = mark.transform, fixedPosition = centre.center, name = name });
            }
            string shown = string.IsNullOrEmpty(name) ? assembly : $"{name} ({assembly})";
            return string.Format(Localization.Extra("debugObjectSpawned", "{0} spawned ({1:0} m across)."), shown, radius * 2f);
        }

        /// <summary>The objects with the original's collision volumes (CollisionVolume.ForStaticObject): the pirate outpost
        /// 1002 (Level::createStaticObject for the pirate bases and the Kaamo siege); -1 = none.</summary>
        static int StaticCollisionId(string assembly) => assembly == "station_pirates" ? 1002 : -1;

        // ---- destroy around the player (CheatsCatalog.Destroy) -------------------------------------------------------

        public enum KillFilter { Hostile, Ships, Everything }

        /// <summary>The Debug page's range steps, metres.</summary>
        public static readonly float[] KillRanges = { 10f, 25f, 50f, 100f, 250f, 500f, 1000f, 2000f, 3000f, 5000f, 10000f, 20000f, 30000f };

        public static string RangeText(float metres) => metres < 1000f ? $"{metres:0} m" : $"{metres / 1000f:0.#} km";

        /// <summary>Whether KillAround takes 't': alive, not the player nor another player (multiplayer), within 'metres' of
        /// 'centre' (the nearest point of its hit boxes: a capital ship's centre can be a km away from its hull) and of the
        /// kind 'what' asks for (hostile: anything hostile to the player, its turrets and sentry guns too; ships; everything
        /// with a hit volume, asteroids and stations' guns as well).</summary>
        public static bool KillsIt(Target t, Vector3 centre, float metres, KillFilter what)
        {
            if (t == null || !t.Alive || t.isPlayer || t.netPilot) return false;
            if (what == KillFilter.Hostile && !t.hostileToPlayer) return false;
            if (what == KillFilter.Ships && !t.isShip) return false;
            return (t.NearestPoint(centre) - centre).sqrMagnitude <= metres * metres;
        }

        /// <summary>Remake (Debug page): every target KillsIt picks around the player, each taking a one-hit kill from the
        /// player (the One-hit kills cheat's hit: loot, bounties and standing as if shot, the story's invulnerable ships
        /// survive; a client's NPCs through the host); the result text.</summary>
        public static string KillAround(SpaceLevel level, float metres, KillFilter what)
        {
            if (level == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            var centre = level.Player.transform.position;
            int killed = 0;
            foreach (var t in Target.All.ToArray())   // a death takes it out of the list
            {
                if (!KillsIt(t, centre, metres, what)) continue;
                t.Damage(99999999f, false, (t.transform.position - centre).normalized);
                if (!t.Alive || t.RemoteDamage != null) killed++;
            }
            return string.Format(Localization.Extra("debugKilled", "{0} destroyed within {1}."), killed, RangeText(metres));
        }

        public static string ShipName(Database db, int ship)
        {
            string name = GameNames.Ship(ship);
            return string.IsNullOrEmpty(name) ? db.Ship(ship)?.name ?? ("#" + ship) : name;
        }
    }
}
