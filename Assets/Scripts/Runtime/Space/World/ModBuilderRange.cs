// ModBuilderRange.cs
// Remake-only: the Mod Builder's test flight (Modding.ModBuilder.TestFlying) in Dis' orbit. SpaceLevel keeps the orbit
// quiet (no traffic, no wingmen) and refuses every jump; this keeps Targets of pirates in it: three hostile pirate fighters
// 3-4 km ahead of the player once the launch camera is over, and a new one 6 s after each death, always at least
// TargetCount alive, so the ship's weapons can be tried. Docking lands back in the builder (SpaceLevel.Dock, the Station
// scene in builder mode).
// Modified: a test flight from the NPC ships kind (ModBuilder.TestNpcShip; the user's request: the mod NPC ship "as they
// would normally fly") meets that type instead of the pirates, spawned as the game spawns it (DebugSpawner.SpawnNpcType:
// TrafficPlan.AddNpcType, its escorts, turrets, orders and rules) ahead of the player, and again 6 s after it is gone
// (destroyed, jumped away or flown off).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public sealed class ModBuilderRange : MonoBehaviour
    {
        const int TargetCount = 3;
        const float RespawnMs = 6000f;
        const float M = 0.05f;
        const int PirateRace = 8;

        SpaceLevel level;
        readonly List<NpcShip> ships = new List<NpcShip>();
        float respawnMs = -1f;
        bool started;
        // The NPC ships kind's test flight: the type's key (null = pirates) and its host ship, once spawned.
        string npcKey;
        NpcShip npcHost;

        public void Setup(SpaceLevel level)
        {
            this.level = level;
            npcKey = Modding.ModBuilder.TestNpcShip;
        }

        void Update()
        {
            if (level == null || level.Traffic == null || level.Player == null || !level.LaunchCameraOver) return;
            if (npcKey != null) { UpdateNpcShip(); return; }
            if (!started)
            {
                started = true;
                level.Traffic.Warn(Localization.Extra("mbTestStart", "Test flight: pirates ahead. Dock at the station (or Esc > Back to Mod Builder) to return."));
                for (int i = 0; i < TargetCount; i++) Spawn(i);
                return;
            }
            ships.RemoveAll(s => s == null || s.Gone || !s.Target.Alive);
            if (ships.Count >= TargetCount) { respawnMs = -1f; return; }
            if (respawnMs < 0f) respawnMs = RespawnMs;
            respawnMs -= Time.deltaTime * 1000f;
            if (respawnMs <= 0f) { respawnMs = -1f; Spawn(Random.Range(0, TargetCount)); }
        }

        void Spawn(int slot)
        {
            var p = level.Player.transform;
            var flat = Vector3.ProjectOnPlane(p.forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-3f) flat = Vector3.forward;
            flat.Normalize();
            var side = Vector3.Cross(Vector3.up, flat);
            var at = p.position + flat * Random.Range(150f, 200f) + side * ((slot - 1) * 40f) + Vector3.up * Random.Range(-15f, 15f);
            var spec = new SpawnSpec
            {
                group = NpcGroup.Raider,
                race = PirateRace,
                ship = NpcTables.RandomFighter(PirateRace),
                position = new Vector3(at.x, at.y, -at.z) / M,   // Unity -> game units
                alwaysEnemy = true,
            };
            var ship = level.Traffic.SpawnShip(spec);
            if (ship == null) return;
            ships.Add(ship);
            level.Traffic.ConnectPlayers();
        }

        // ---- the NPC ships kind's test flight ------------------------------------------------------------------------

        void UpdateNpcShip()
        {
            if (!started)
            {
                started = true;
                var type = Modding.ModNpcShips.Get(npcKey);
                if (type == null)
                {
                    // Not read (the project has problems): the pirates instead, with the reason.
                    level.Traffic.Warn(Localization.Extra("mbTestNpcMissing", "Test flight: the NPC ship couldn't be read (see the builder's messages). Pirates instead."));
                    npcKey = null;
                    started = false;
                    return;
                }
                SpawnNpcShip(type);
                level.Traffic.Warn(string.Format(Localization.Extra("mbTestNpcStart", "Test flight: {0} ahead. Dock at the station (or Esc > Back to Mod Builder) to return."),
                                                 !string.IsNullOrEmpty(type.name) ? type.name : type.key));
                return;
            }
            if (npcHost != null && !npcHost.Gone && npcHost.Target.Alive) { respawnMs = -1f; return; }
            if (respawnMs < 0f) respawnMs = RespawnMs;
            respawnMs -= Time.deltaTime * 1000f;
            if (respawnMs > 0f) return;
            respawnMs = -1f;
            var again = Modding.ModNpcShips.Get(npcKey);
            if (again != null) SpawnNpcShip(again);
        }

        void SpawnNpcShip(Modding.NpcShipType type)
        {
            ships.RemoveAll(s => s == null || s.Gone || !s.Target.Alive);
            var made = new List<NpcShip>();
            DebugSpawner.SpawnNpcType(level, type, null, null, 0, made);
            npcHost = made.Count > 0 ? made[0] : null;
            ships.AddRange(made);
        }
    }
}
