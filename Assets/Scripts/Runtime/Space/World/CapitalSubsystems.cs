// CapitalSubsystems.cs
// Remake mods: a mod capital ship's subsystems (ships.json "subsystems", Data.CapitalSubsystem; EVERSPACE 2's warships, whose
// turrets, shield generators, weak spots and hangars are actors of their own on the hull: the ES2 Ships mod's Dreadnought).
// Each part is an NPC object of its own on the hull (Traffic.SpawnShip, capitalPart / capitalHost = the ship, so it goes when
// the ship dies), placed like a turret on a mount (position_engine in the ship's frame, turretUp its facing):
//   "turret"  a turret object of its turret item (SpawnSpec.turretItem: the item's model, aim and gun);
//   "object"  a fixed object (no gun) of its "model": a game assembly ("gof2:<name>") or a ship's model;
//   "hangar"  a fixed object that launches its "ships" ("every" seconds, "perWave" at a time, at most "maxAlive" flying and
//             "total" in all) while its ship is hostile to the player and the player within LaunchReachUnits.
// The rules: while a part marked "shields" stands, the hull takes "shieldedDamage" of every hit (0 = none; a player's hit is
// answered "Its shields hold" now and then) and the renderers whose material is named like "shieldMaterial" (the ship's
// energy fields) show, hidden once the last generator falls; a "weakspot" part passes its hits on to the hull x that factor
// and its death takes "bonus" of the hull's maximum; a part of a higher "stage" can't be harmed while one of a lower stage
// stands (ES2's encounter stages). A retreating ship (CapitalRetreat) halts them: they stop firing and launching, its hangars'
// fighters jump out (ES2's BP_Dreadnought_Command_JumpOut: TimeUntilJumpOut 3 s on, then one at random every 0.1-0.3 s; the
// rest at once should the ship be gone first), and they leave with it. Parts take the ship's side: hostile when it is, turned when it turns. Only the game running
// the orbit's NPCs builds them (Traffic.Create); the other players see them as proxies.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class CapitalSubsystems : MonoBehaviour
    {
        const float M = 0.05f;
        /// <summary>A hangar launches only while the player is this close (game units, 7.5 km).</summary>
        const float LaunchReachUnits = 150000f;
        const float ShieldWarnMs = 6000f;
        /// <summary>ES2's BP_Dreadnought_Command_JumpOut: TimeUntilJumpOut, then the escorts one by one RandomFloatInRange(0.1, 0.3) apart.</summary>
        public const float EscortJumpDelayMs = 3000f, EscortJumpGapMinMs = 100f, EscortJumpGapMaxMs = 300f;
        const string DefaultObjectModel = "v_station_battlestation_shield";

        class Part
        {
            public CapitalSubsystem data;
            public NpcShip ship;              // null: a launch point on the hull alone ("model": "none")
            public Vector3 localPos, localUp; // ... its place on the host (Unity, the host's frame)
            public float waveMs;
            public int launched;
            public readonly List<NpcShip> flying = new List<NpcShip>();
            public bool bonusPaid;
        }

        NpcShip host;
        Traffic traffic;
        Database db;
        CustomShipData data;
        List<CapitalSubsystem> specs;
        readonly List<Part> parts = new List<Part>();
        readonly List<Renderer> fields = new List<Renderer>();
        bool built, shieldsUp, dead, halted;
        readonly List<NpcShip> escaping = new List<NpcShip>();
        float escapeMs;
        float sinceWarnMs = ShieldWarnMs;

        /// <summary>Any part marked "shields" still stands (the hull takes shieldedDamage).</summary>
        public bool ShieldsUp => shieldsUp;

        public static CapitalSubsystems Attach(NpcShip host, Traffic traffic, Database db, List<CapitalSubsystem> subsystems)
        {
            var c = host.gameObject.AddComponent<CapitalSubsystems>();
            c.host = host; c.traffic = traffic; c.db = db; c.specs = subsystems;
            c.data = CustomShips.Get(host.Spec.ship);
            host.Target.Died += _ => c.OnHostDied();
            host.Target.Damaged += c.OnHostDamaged;
            return c;
        }

        void Update()
        {
            if (halted && !dead) { UpdateEscape(Time.deltaTime * 1000f); return; }
            if (host == null || dead || halted) return;
            if (!built) { built = true; Build(); }
            float dtMs = Time.deltaTime * 1000f;
            sinceWarnMs += dtMs;
            bool hostile = host.alwaysEnemy || host.turnedEnemy;
            // The shields, the stages, the parts' side.
            bool shields = false;
            int lowest = int.MaxValue;
            foreach (var p in parts)
                if (Standing(p))
                {
                    if (p.data.shields) shields = true;
                    if (p.data.stage > 0) lowest = Mathf.Min(lowest, p.data.stage);
                }
            SetShields(shields);
            foreach (var p in parts)
            {
                if (p.ship == null) { if (p.data.kind == "hangar") UpdateHangar(p, dtMs, hostile); continue; }
                if (Standing(p))
                {
                    p.ship.Target.invulnerable = p.data.stage > 0 && p.data.stage > lowest;
                    if (hostile && !p.ship.alwaysEnemy && !p.ship.turnedEnemy) p.ship.turnedEnemy = true;
                }
                else if (!p.bonusPaid) { p.bonusPaid = true; PayBonus(p); }
                if (p.data.kind == "hangar") UpdateHangar(p, dtMs, hostile);
            }
        }

        static bool Standing(Part p) => p.ship != null && !p.ship.Gone && p.ship.Target.Alive;
        bool Launching(Part p) => p.ship == null ? host.Target.Alive : Standing(p);

        void Build()
        {
            int rank = Mathf.Min(Session.Rank, 20);
            float diff = Session.DifficultyFactor;
            float k = host.Spec.HullScale;   // an NPC ship type's size: its hardpoints out from the pivot and as big
            for (int i = 0; i < specs.Count; i++)
            {
                var d = specs[i];
                var mount = new WeaponMount
                {
                    slotType = 2,
                    position_engine = new[] { Mathf.RoundToInt(d.position.x), Mathf.RoundToInt(d.position.y), Mathf.RoundToInt(d.position.z) },
                    turretUp = new[] { d.up.x, d.up.y, d.up.z },
                };
                var localPos = Flight.WeaponSystem.MountToLocal(mount) * k;
                var worldPos = host.transform.TransformPoint(localPos);
                var worldRot = host.transform.rotation * mount.TurretTurn;
                if (d.kind != "turret" && string.Equals(d.model, "none", System.StringComparison.OrdinalIgnoreCase))
                {
                    // A launch point alone (a hangar deck without a part of its own to shoot off).
                    parts.Add(new Part { data = d, localPos = localPos, localUp = mount.TurretTurn * Vector3.up, waveMs = d.everyMs * 0.5f });
                    continue;
                }
                int baseHull = Mathf.RoundToInt((500 + 50 * rank) * diff);
                int hull = d.hull > 0 ? Mathf.RoundToInt(d.hull * diff) : d.kind == "turret" ? baseHull : d.kind == "hangar" ? baseHull * 3 : baseHull * 2;
                var spec = new SpawnSpec
                {
                    group = NpcGroup.Turret, race = host.Race, ship = -1, position = new Vector3(worldPos.x, worldPos.y, -worldPos.z) / M,
                    hitpoints = hull, noLoot = true, stationary = true, scale = d.scale * k,
                    alwaysEnemy = host.Spec.alwaysEnemy, alwaysFriend = host.Spec.alwaysFriend, alwaysNeutral = host.Spec.alwaysNeutral,
                    capitalPart = true, capitalHost = host.Spec, eventTag = host.Spec.eventTag,
                    name = string.IsNullOrEmpty(d.name) ? null : d.name, nameText = string.IsNullOrEmpty(d.name) ? (d.kind == "turret" ? 1666 : 1665) : -1,
                    hitRadius = d.radius * k,
                };
                if (d.kind == "turret") { spec.turretAssembly = "turret_002_static"; spec.turretItem = d.item; }
                else spec.fixedObject = ObjectModel(d.model);
                var s = traffic.SpawnShip(spec);
                if (s == null) continue;
                s.transform.SetPositionAndRotation(worldPos, worldRot);
                s.transform.SetParent(host.transform, true);
                if (d.kind != "turret") s.shootingEnabled = false;
                if (d.weakspot > 0f) { s.Target.forwardTo = host.Target; s.Target.forwardFactor = d.weakspot; }
                parts.Add(new Part { data = d, ship = s, waveMs = d.everyMs * 0.5f });
            }
            if (data != null && !string.IsNullOrEmpty(data.shieldMaterial))
                foreach (var r in host.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m != null && m.name.IndexOf(data.shieldMaterial, System.StringComparison.OrdinalIgnoreCase) >= 0) { fields.Add(r); break; }
            traffic.ConnectPlayers();
        }

        /// <summary>A part's model: "gof2:<assembly>", a ship (its assembly), else the game's battlestation shield generator.</summary>
        string ObjectModel(string model)
        {
            if (string.IsNullOrEmpty(model)) return DefaultObjectModel;
            if (Modding.GameModels.Is(model, out string name)) return name;
            if (Modding.ModContent.TryResolveShip(model, out int ship)) return db.ShipAssembly(ship)?.name ?? DefaultObjectModel;
            return model;
        }

        void SetShields(bool up)
        {
            if (shieldsUp == up && (built || !up)) { ApplyShields(); return; }
            bool wasUp = shieldsUp;
            shieldsUp = up;
            ApplyShields();
            if (wasUp && !up && (host.alwaysEnemy || host.turnedEnemy))
                traffic.Warn(string.Format(Localization.Extra("capitalShieldsDown", "{0}'s shields are down!"), host.Target.displayName ?? GameNames.Ship(host.Spec.ship)));
        }

        void ApplyShields()
        {
            host.Target.damageFactor = shieldsUp ? Mathf.Max(0f, data != null ? data.shieldedDamage : 0f) : 1f;
            foreach (var r in fields) if (r != null && r.enabled != shieldsUp) r.enabled = shieldsUp;
        }

        void OnHostDamaged(Target t, int dmg, bool byNpc)
        {
            if (!shieldsUp || byNpc || sinceWarnMs < ShieldWarnMs) return;
            sinceWarnMs = 0f;
            traffic.Warn(Localization.Extra("capitalShieldsHold", "Its shields hold: destroy the shield generators!"));
        }

        void PayBonus(Part p)
        {
            if (p.data.bonus <= 0f || !host.Target.Alive || p.ship.Target.killedByNpc) return;
            float factor = host.Target.damageFactor;
            host.Target.damageFactor = 1f;   // a weak spot's blast gets through the shields
            host.Target.Damage(host.Hp.maxHull * p.data.bonus, false);
            host.Target.damageFactor = factor;
        }

        void UpdateHangar(Part p, float dtMs, bool hostile)
        {
            p.flying.RemoveAll(s => s == null || s.Gone || !s.Target.Alive);
            if (!Launching(p) || !hostile || !host.Target.Alive) return;
            if (p.data.total >= 0 && p.launched >= p.data.total) return;
            var player = traffic.Player;
            var at = p.ship != null ? p.ship.transform.position : host.transform.TransformPoint(p.localPos);
            var dir = p.ship != null ? p.ship.transform.up : host.transform.TransformDirection(p.localUp);
            if (player == null || !player.Alive || (player.transform.position - at).sqrMagnitude > LaunchReachUnits * M * LaunchReachUnits * M) return;
            p.waveMs -= dtMs;
            if (p.waveMs > 0f) return;
            p.waveMs = p.data.everyMs;
            for (int i = 0; i < p.data.perWave && p.flying.Count < p.data.maxAlive; i++)
            {
                if (p.data.total >= 0 && p.launched >= p.data.total) break;
                string reference = p.data.ships[Random.Range(0, p.data.ships.Count)];
                if (!Modding.ModContent.TryResolveShip(reference, out int ship) || db.Ship(ship) == null) continue;
                var pos = at + dir * (40f + 25f * i);
                var spec = new SpawnSpec
                {
                    group = NpcGroup.Escort, race = host.Race, ship = ship, position = new Vector3(pos.x, pos.y, -pos.z) / M,
                    alwaysEnemy = true, noLoot = true, routeAround = host.Spec, eventTag = host.Spec.eventTag,
                    route = CapitalShips.LoopAround(host.Spec, new Vector3(host.transform.position.x, host.transform.position.y, -host.transform.position.z) / M, p.launched),
                };
                var s = traffic.SpawnShip(spec);
                if (s == null) continue;
                s.Place(pos, dir);
                foreach (var a in host.aggressors) s.aggressors.Add(a);
                p.flying.Add(s);
                p.launched++;
            }
            traffic.ConnectPlayers();
        }

        /// <summary>CapitalRetreat: the ship gives up the fight and charges its jump drive. Its parts hold their fire and can't
        /// be harmed any more, the hangars launch no more, and the fighters they launched jump out (ES2's StartEnemyJumpOut).</summary>
        public void Halt()
        {
            if (halted || dead) return;
            halted = true;
            foreach (var p in parts)
            {
                if (Standing(p)) { p.ship.shootingEnabled = false; p.ship.Target.invulnerable = true; }
                foreach (var f in p.flying) if (f != null && !f.Gone && f.Target.Alive) escaping.Add(f);
            }
            escapeMs = -EscortJumpDelayMs;
        }

        /// <summary>The escorts' jump-out: after EscortJumpDelayMs one at random, then one every EscortJumpGap*Ms.</summary>
        void UpdateEscape(float dtMs)
        {
            escaping.RemoveAll(f => f == null || f.Gone || !f.Target.Alive);
            if (escaping.Count == 0 || (escapeMs += dtMs) < 0f) return;
            int i = Random.Range(0, escaping.Count);
            escaping[i].JumpOut();
            escaping.RemoveAt(i);
            escapeMs = -Random.Range(EscortJumpGapMinMs, EscortJumpGapMaxMs);
        }

        /// <summary>CapitalRetreat: the ship has jumped away, its parts with it (nobody's kills).</summary>
        public void LeaveWithHost()
        {
            dead = true;
            foreach (var f in escaping) if (f != null && !f.Gone && f.Target.Alive) f.JumpOut();
            escaping.Clear();
            foreach (var p in parts)
                if (Standing(p)) { p.ship.Target.killedByNpc = true; p.ship.Vanish(); }
        }

        void OnHostDied()
        {
            if (dead) return;
            dead = true;
            foreach (var r in fields) if (r != null) r.enabled = false;
            foreach (var p in parts)
            {
                if (!Standing(p)) continue;
                p.ship.Target.invulnerable = false;
                p.ship.Target.damageFactor = 1f;
                p.ship.Target.forwardTo = null;
                p.ship.Target.Damage(9999999f, true);
            }
        }
    }
}
