// NpcDevices.cs
// Remake (2026-11-04, for mods, after Galaxy on Fire 3's boss devices; the GoF3 Ships importer's bosses): a Most Wanted
// criminal's devices (wanted.json "devices", Flight.NpcDeviceSpec / NpcDeviceState for when each starts), on its ship while it
// flies against the player. What each does (GoF3's boss device lore, BD_*_Lore):
//   teleport       it vanishes and comes out 'distance' from the player, outside the player's front 60 degrees, nose on
//                  them ("They teleport nearby, lined up for a devastating series of blasts"); a flash at both ends
//   frontShield,   while it runs, a hit from within 'shieldAngle' of the nose (the tail) is stopped ("blocking almost 100% of
//   backShield     all primary and secondary weapon damage. Can be defeated by attacking from a different vector")
//   energyAbsorber while it runs every hit is soaked, the hull mended by 'healPercent' of it ("It absorbs any and all weapon
//                  energy ... Then it redirects this energy into shield regeneration": GoF2's ships have no shield)
//   radarJammer    the player's radar for its time (Flight.RadarJam: no lock, no ship markers)
//   flashGrenade   thrown, it flashes 'delayMs' later: white over the player's view (Flight.ScreenFlash) for its time, as
//                  strong as the player looks at it (none from 90 degrees away)
//   mindControl    the player's wingmen turn on them for its time, each until the player hits it ("Can cause wingmen to
//                  attack their leader ... Shooting them can get them to regain their senses"; NpcShip.MindControl)
//   bombSpreader   a proximity mine behind it every 'bombEveryMs' while it runs, flung within 45 degrees (GoF3's spreadAngle;
//                  the rifts' RiftMine), each blasting the player's ship for 'damage' ("most of these devices scatter a
//                  large amount of mines")
//   mineLayer      a mine 'layBehind' its tail every 'bombEveryMs' while it runs (GoF3's boss mine layers: minesPerBurst
//                  'timeBetweenMines' apart), each chasing the player and going off near them (World.SeekerMine)
//   cloud          a cloud 'layBehind' its tail (GoF3's smart bombs' effect zones: Mudshark's nanobite fog) that slows the
//                  player's ship inside it and eats its hull (World.NpcCloud)
//   shadowClone    'count' copies of it with 'hullPercentOfShip' of its hull ("two clones of the original ship that for a
//                  limited time can fight like the original. They are easily destroyed"), gone when the run ends or it dies
//   minions        'count' shield drones circling it 'radius' out, a beam from each to it ("Shooting down the annoying stupid
//                  drones will leave the shield weak"): while one lives its hits lose 'blockPercent'; gone when the run ends
//   wormhole       a hole behind its tail; the player flying in comes out in front of its nose ("if a foolish pilot flies
//                  into the singularity, they'll end up right in the cross hairs of the ship using this weapon"; World.NpcWormhole)
// Also a mod traffic hull's ("traffic": { "devices" }, GoF3's kamikazes): the same, without the HUD lines.
// The single player's game only: in multiplayer the authority runs them for its own player (the others see the teleport).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class NpcDevices : MonoBehaviour
    {
        const float M = 0.05f;
        const float TeleportFrontDeg = 60f;

        NpcShip ship;
        Traffic traffic;
        SpaceLevel level;
        readonly List<NpcDeviceState> states = new List<NpcDeviceState>();
        readonly Dictionary<NpcDeviceState, List<NpcShip>> clones = new Dictionary<NpcDeviceState, List<NpcShip>>();
        readonly Dictionary<NpcDeviceState, List<Target>> minions = new Dictionary<NpcDeviceState, List<Target>>();
        static Material beamLook;
        float orbitDeg;
        bool hit;

        public static NpcDevices Attach(NpcShip ship, Traffic traffic, List<NpcDeviceSpec> specs)
        {
            if (specs == null || specs.Count == 0) return null;
            // who carries which: by the player's rank and each device's share of the hull's NPCs (GoF3's loadouts)
            var carried = specs.FindAll(s => s.Carried(Session.Rank, Random.value));
            if (carried.Count == 0) return null;
            specs = carried;
            var d = ship.gameObject.AddComponent<NpcDevices>();
            d.ship = ship;
            d.traffic = traffic;
            d.level = FindAnyObjectByType<SpaceLevel>();
            foreach (var s in specs) d.states.Add(new NpcDeviceState(s));
            ship.Target.Damaged += (t, dmg, byNpc) => d.hit = true;
            ship.Target.HitFilter = d.Filter;
            return d;
        }

        void Update()
        {
            if (ship == null || ship.Target == null || !ship.Target.Alive) { ClearAll(); enabled = false; return; }
            var player = traffic != null ? traffic.Player : null;
            var view = new NpcDeviceState.View { hit = hit, hull = ship.Hp != null ? ship.Hp.HullFraction : 1f };
            hit = false;
            if (player != null && player.Alive && player.isActiveAndEnabled && ship.Target.hostileToPlayer && !ship.Asleep
                && ship.Current == NpcShip.State.Fly && !ship.Hp.empDisabled)
            {
                var d = player.transform.position - transform.position;
                view.playerNear = true;
                view.distance = d.magnitude / M;
                view.offNoseDeg = Vector3.Angle(transform.forward, d);
                view.playerFacingDeg = Vector3.Angle(player.transform.forward, -d);
            }
            float dt = Time.deltaTime * 1000f;
            foreach (var s in states)
            {
                if (s.Step(dt, view, Random.value, out bool flashed)) Begin(s, player);
                if (!s.Active) Clear(s);
                if (flashed && player != null && player.Alive) Flash(s, player);
                for (int i = 0; i < s.DueBombs; i++)
                    if (s.spec.kind == NpcDeviceSpec.Kind.MineLayer) SeekerMine.Lay(level, s.spec, transform.position - transform.forward * (s.spec.layBehind * M));
                    else if (s.spec.kind == NpcDeviceSpec.Kind.Cloud) NpcCloud.Lay(level, s.spec, transform.position - transform.forward * (s.spec.layBehind * M));
                    else RiftMine.Drop(level, null, transform.position, -transform.forward, s.spec.damage);
            }
            UpdateMinions(dt);
        }

        void OnDestroy() => ClearAll();

        void Begin(NpcDeviceState s, Target player)
        {
            string name = ship.Target.displayName ?? Standing.RaceName(ship.Race);
            switch (s.spec.kind)
            {
                case NpcDeviceSpec.Kind.Teleport: Teleport(s.spec, player); break;
                case NpcDeviceSpec.Kind.FrontShield:
                    Say(string.Format(Localization.Extra("bossFrontShield", "{0}: front shield up"), name)); break;
                case NpcDeviceSpec.Kind.BackShield:
                    Say(string.Format(Localization.Extra("bossBackShield", "{0}: back shield up"), name)); break;
                case NpcDeviceSpec.Kind.EnergyAbsorber:
                    Say(string.Format(Localization.Extra("bossEnergyAbsorber", "{0}: energy absorber on. Hold your fire!"), name)); break;
                case NpcDeviceSpec.Kind.RadarJammer:
                    RadarJam.Start(s.spec.durationMs);
                    Say(Localization.Extra("bossRadarJammer", "Radar jammed!")); break;
                case NpcDeviceSpec.Kind.FlashGrenade:
                    Say(string.Format(Localization.Extra("bossFlashGrenade", "{0} threw a flash grenade: look away!"), name)); break;
                case NpcDeviceSpec.Kind.MindControl:
                    var wingmen = traffic != null ? traffic.LivingWingmen : null;
                    if (wingmen == null || wingmen.Count == 0) break;
                    foreach (var w in wingmen) w.MindControl(s.spec.durationMs);
                    Say(string.Format(Localization.Extra("bossMindControl", "{0} took over your wingmen! Hit them to wake them up."), name)); break;
                case NpcDeviceSpec.Kind.BombSpreader:
                case NpcDeviceSpec.Kind.MineLayer:
                    Say(string.Format(Localization.Extra("bossBombSpreader", "{0} is laying mines!"), name)); break;
                case NpcDeviceSpec.Kind.ShadowClone:
                    SpawnClones(s);
                    Say(string.Format(Localization.Extra("bossShadowClone", "{0} split into clones: find the real one!"), name)); break;
                case NpcDeviceSpec.Kind.Minions:
                    SpawnMinions(s);
                    Say(string.Format(Localization.Extra("bossMinions", "{0} launched shield drones: shoot them down!"), name)); break;
                case NpcDeviceSpec.Kind.Wormhole:
                    NpcWormhole.Open(level, s.spec, transform);
                    Say(string.Format(Localization.Extra("bossWormhole", "{0} opened a wormhole behind it: don't fly in!"), name)); break;
                case NpcDeviceSpec.Kind.Cloud:
                    Say(string.Format(Localization.Extra("bossCloud", "{0} released a cloud: keep out of it!"), name)); break;
            }
        }

        /// <summary>A criminal's HUD line (a traffic hull's devices go without: there may be many of them).</summary>
        void Say(string text) { if (ship.Spec.wantedIndex >= 0) traffic?.Warn(text); }

        /// <summary>Out 'distance' from the player in a direction outside their front 60 degrees, nose on them.</summary>
        void Teleport(NpcDeviceSpec spec, Target player)
        {
            if (player == null) return;
            var p = player.transform.position;
            var dir = Random.onUnitSphere;
            for (int i = 0; i < 12 && Vector3.Angle(player.transform.forward, dir) < TeleportFrontDeg; i++) dir = Random.onUnitSphere;
            if (Vector3.Angle(player.transform.forward, dir) < TeleportFrontDeg) dir = -player.transform.forward;
            var to = p + dir * (Random.Range(spec.distanceMin, spec.distanceMax) * M);
            Explosion.Spawn(transform.position, 0.4f, false);
            transform.SetPositionAndRotation(to, Quaternion.LookRotation(p - to, player.transform.up));
            Explosion.Spawn(to, 0.4f);
        }

        void Flash(NpcDeviceState s, Target player)
        {
            Explosion.Spawn(transform.position, 0.3f);
            float facing = Vector3.Angle(player.transform.forward, transform.position - player.transform.position);
            float a = s.FlashOpacity(facing);
            if (a > 0.02f) ScreenFlash.Start(a, s.spec.durationMs);
        }

        /// <summary>Copies of the ship beside it: its hull, guns and speed, 'hullShare' of its hull, no loot.</summary>
        void SpawnClones(NpcDeviceState s)
        {
            if (traffic == null) return;
            Clear(s);
            var src = ship.Spec;
            var list = new List<NpcShip>();
            for (int i = 0; i < s.spec.count; i++)
            {
                float side = (i % 2 == 0 ? 1f : -1f) * (1 + i / 2) * 60f;   // (metres, alternately right and left)
                var at = transform.position + transform.right * side;
                var clone = traffic.SpawnShip(new SpawnSpec
                {
                    group = NpcGroup.Escort, race = src.race, ship = src.ship, name = src.name, nameText = src.nameText,
                    hitpoints = Mathf.Max(1, Mathf.RoundToInt(ship.Hp.maxHull * s.spec.hullShare)), speed = src.speed,
                    gunItem = src.gunItem, gunFactor = src.gunFactor, secondaryItem = src.secondaryItem, secondaryFactor = src.secondaryFactor,
                    turretItems = src.turretItems != null ? new List<int>(src.turretItems) : null,
                    noLoot = true, alwaysEnemy = true, noDevices = true, position = new Vector3(at.x, at.y, -at.z) / M,
                });
                if (clone == null) continue;
                clone.transform.rotation = transform.rotation;
                Explosion.Spawn(at, 0.4f, false);
                list.Add(clone);
            }
            clones[s] = list;
        }

        /// <summary>Shield drones round the ship, shootable by the player (a radar object each), a beam to the ship.</summary>
        void SpawnMinions(NpcDeviceState s)
        {
            Clear(s);
            var list = new List<Target>();
            for (int i = 0; i < s.spec.count; i++)
            {
                var go = RiftHazards.Sphere(null, transform.position, 4f, new Color(0.3f, 0.8f, 1f, 0.9f), "Shield drone");
                var t = go.AddComponent<Target>();
                t.maxHp = t.hp = Mathf.Max(1f, ship.Hp.maxHull * s.spec.hullShare);
                t.radius = 8f;
                t.hostileToPlayer = true;
                t.untargetable = true;   // (the NPCs' guns leave it alone)
                t.customDeath = true;
                t.displayName = Localization.Extra("shieldDrone", "Shield drone");
                t.plateNameOnly = t.plateNoIcon = true;
                Target.RadarObjects.Add(t);
                var line = go.AddComponent<LineRenderer>();
                line.positionCount = 2;
                line.startWidth = line.endWidth = 0.6f;
                line.useWorldSpace = true;
                if (beamLook == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) beamLook = new Material(shader) { name = "Shield drone beam" };
                }
                line.sharedMaterial = beamLook;
                line.startColor = line.endColor = new Color(0.3f, 0.8f, 1f, 0.5f);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Explosion.Spawn(transform.position, 0.2f, false);
                list.Add(t);
            }
            minions[s] = list;
        }

        /// <summary>The drones circle the ship (one ring, evenly spaced, turning); a shot-down one bursts.</summary>
        void UpdateMinions(float dtMs)
        {
            if (minions.Count == 0) return;
            orbitDeg = Mathf.Repeat(orbitDeg + dtMs * 0.03f, 360f);
            foreach (var kv in minions)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                    if (list[i] == null || !list[i].Alive) { Burst(list[i]); list.RemoveAt(i); }
                for (int i = 0; i < list.Count; i++)
                {
                    float a = (orbitDeg + 360f * i / list.Count) * Mathf.Deg2Rad;
                    var p = transform.position + (transform.right * Mathf.Cos(a) + transform.up * Mathf.Sin(a)) * (kv.Key.spec.radius * M);
                    list[i].transform.position = p;
                    var line = list[i].GetComponent<LineRenderer>();
                    if (line != null) { line.SetPosition(0, p); line.SetPosition(1, transform.position); }
                }
            }
        }

        bool Shielded()
        {
            foreach (var kv in minions) if (kv.Key.Active && kv.Value.Count > 0) return true;
            return false;
        }

        static void Burst(Target t)
        {
            if (t == null) return;
            Explosion.Spawn(t.transform.position, 0.4f);
            Target.RadarObjects.Remove(t);
            Destroy(t.gameObject);
        }

        /// <summary>A run's clones and drones go (the run ended).</summary>
        void Clear(NpcDeviceState s)
        {
            if (clones.TryGetValue(s, out var cl))
            {
                foreach (var c in cl)
                    if (c != null && c.Target != null && c.Target.Alive) { Explosion.Spawn(c.transform.position, 0.4f, false); c.Vanish(); }
                clones.Remove(s);
            }
            if (minions.TryGetValue(s, out var mi))
            {
                foreach (var t in mi) Burst(t);
                minions.Remove(s);
            }
        }

        void ClearAll()
        {
            foreach (var s in states) Clear(s);
        }

        /// <summary>Target.HitFilter: the shields stop what comes at their side, the absorber soaks everything, the drones
        /// shield the ship while one lives.</summary>
        float Filter(float amount, Vector3 hitVector)
        {
            if (amount <= 0f) return amount;
            foreach (var s in states)
            {
                if (!s.Active) continue;
                if (s.spec.kind == NpcDeviceSpec.Kind.Minions && Shielded()) amount *= 1f - s.spec.blockShare;
                if (s.spec.kind == NpcDeviceSpec.Kind.EnergyAbsorber)
                {
                    var hp = ship.Hp;
                    if (hp != null && s.spec.healShare > 0f)
                    {
                        hp.hull = Mathf.Min(hp.maxHull, hp.hull + Mathf.Max(1, Mathf.RoundToInt(amount * s.spec.healShare)));
                        ship.Target.hp = hp.hull;
                    }
                    return 0f;
                }
                if (hitVector.sqrMagnitude > 1e-6f) amount *= s.ShieldPass(Vector3.Angle(transform.forward, -hitVector));
            }
            return amount;
        }
    }
}
