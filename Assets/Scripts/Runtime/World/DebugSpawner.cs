// DebugSpawner.cs
// Remake-only testing tools for the pause menu's Debug page (CheatsCatalog.Spawns): put any ship (race, model, behaviour) or any
// assembled object (assemblies.json: ships, stations, level props, effects...) in front of the player. Spawned ships are ordinary
// traffic (Traffic.SpawnShip: they fight, drop loot, count for the music); objects are plain scenery (no collision, no lock)
// that stays until the level is left. Nothing here is in the original.

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
        /// several; the result text.</summary>
        public static string SpawnShip(SpaceLevel level, int race, int ship, Behaviour behaviour, int count = 1, Vector3? at = null, int eventTag = 0,
                                       string name = null)
        {
            if (level == null || level.Traffic == null || level.Player == null) return Localization.Extra("debugNoFlight", "Only in flight.");
            var p = level.Player.transform;
            var centre = at ?? p.position + p.forward * 400f + p.up * 40f;
            int made = 0;
            for (int i = 0; i < count; i++)
            {
                float side = (i - (count - 1) * 0.5f) * 60f;
                var spec = new SpawnSpec
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
                };
                if (level.Traffic.SpawnShip(spec) != null) made++;
            }
            level.Traffic.ConnectPlayers();   // the new ships and the others see each other (targets, hit lists)
            string shown = ShipName(level.Database, ship);
            if (!string.IsNullOrEmpty(name)) shown = $"{name} ({shown})";
            return made == 0 ? string.Format(Localization.Extra("debugSpawnFailed", "{0} couldn't be spawned."), shown)
                 : made == 1 ? string.Format(Localization.Extra("debugShipSpawned", "{0} spawned."), shown)
                 : string.Format(Localization.Extra("debugShipsSpawned", "{0} x {1} spawned."), made, shown);
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

        public static string ShipName(Database db, int ship)
        {
            string name = CustomShips.ShipName(ship);
            return string.IsNullOrEmpty(name) ? db.Ship(ship)?.name ?? ("#" + ship) : name;
        }
    }
}
