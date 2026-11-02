// Target.cs
// Something guns can hit: the original's Player objects (KIPlayers: ships, asteroids, gas clouds; never stations; and the
// player's own ship for NPC guns). Registered in Target.All while enabled. The hit test uses 'radius' as the
// half-size of an axis-aligned cube (Gun::calcCharacterCollision), or the local 'boxes' for big ships (KIPlayer+0x3c
// custom collide: freighters, battleship).
//   Asteroids: radius = meshRadius * scale * 0.7, HP = scale * 100 + 30 (PlayerAsteroid ctor); at 0 HP the asteroid
//     explosion plays (about 10 s) with sound 21. They also carry what mining needs (PlayerAsteroid +0x124 ore item,
//     +0x14c quality 4..7 = D..A, +0x134 scale).
//   Ships and the player: a Hitpoints (shield -> armor -> hull, Player::damage 0xafa70), a race, the hostile /
//     friend flags (Player+0x5c / +0x5d, marker colours) and 'customDeath' (the owner plays its own death sequence).

using System;
using System.Collections.Generic;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Target : MonoBehaviour
    {
        public static readonly List<Target> All = new List<Target>();

        [Tooltip("Half-size of the hit cube in metres.")]
        public float radius = 10f;
        public float maxHp = 100f;
        public float hp = 100f;
        public bool isAsteroid;
        public GameObject explosionPrefab;
        public float explosionScale = 1f;
        /// <summary>Remake: replaces the explosion billboard's (the first part's) texture: the ordinary asteroids' rock-coloured
        /// copy of asteroid_explosion.png, whose purple fragments the original shares with the Void asteroid.</summary>
        public Texture explosionTexture;
        public AudioClip destroyedSound;

        [Header("Asteroid (mining)")]
        [Tooltip("Ore item index (154-164, 217), -1 = not an asteroid.")]
        public int oreItem = -1;
        [Tooltip("4 D, 5 C, 6 B, 7 A: the minigame's layer count; class A also yields a core (ore + 11).")]
        public int quality = 4;
        public float scale = 1f;

        [Header("Ships")]
        [Tooltip("KIPlayer+0x24: 0 Terran, 1 Vossk, 2 Nivelian, 3 Midorian, 8 Pirate, 9 Void, 10 Specter; -1 not a ship.")]
        public int race = -1;
        public bool isPlayer;
        public bool isShip;
        [Tooltip("Player+0x5c / +0x5d: hostile to / friend of the player (recomputed every frame by the ship).")]
        public bool hostileToPlayer, friendToPlayer;
        [Tooltip("Player+0x5e: NPCs don't attack it.")]
        public bool untargetable;
        /// <summary>KIPlayer+0x3d (the pirate outposts): mines neither trigger on nor pull toward it.</summary>
        public bool mineProof;
        /// <summary>Radar::drawCurrentLock: the Hijacker (1611) and the Informer (1663) show their name alone; they and a Most
        /// Wanted criminal ("<name> NN%", Player+0x3e) are drawn in red (the plate's other colour, SetColor around them).</summary>
        public bool plateNameOnly, plateRed;
        /// <summary>No race icon on the lock plate (the space junk).</summary>
        public bool plateNoIcon;
        /// <summary>The space junk's lock plate. PlayerJunk is a KIPlayer of race -1, which Radar::drawCurrentLock turns into
        /// text 406 - 1 = 405 "Secure" (and no icon); remake: "Space junk".</summary>
        public static string JunkName => GoF2Remake.Data.Localization.Extra("spaceJunk", "Space junk");
        /// <summary>Targets outside the traffic's ship list that the radar still shows and locks (the Junk removal's space junk,
        /// PlayerJunk: a far dot always).</summary>
        public static readonly System.Collections.Generic.List<Target> RadarObjects = new System.Collections.Generic.List<Target>();
        /// <summary>Multiplayer: the other players' ships (NetPlayer) and, on a client, the host's NPC ships (NetProxy): the
        /// radar locks them and the HUD marks them like the traffic's ships.</summary>
        public static readonly System.Collections.Generic.List<Target> NetShips = new System.Collections.Generic.List<Target>();
        /// <summary>Multiplayer: a hit (amount, hit vector, by an NPC, the weapon's shield factor) is passed on to the game
        /// that owns the ship (NetProxy, NetPlayer) instead of applied here.</summary>
        [NonSerialized] public Action<float, Vector3, bool, float> RemoteDamage;
        /// <summary>Multiplayer: EMP (points) passed on to the game that owns the ship (NetProxy, NetPlayer).</summary>
        [NonSerialized] public Action<int> RemoteEmp;
        /// <summary>Multiplayer: a repair beam's hull points passed on to the game that owns the ship (NetProxy, NetPlayer);
        /// null = it can't be repaired from here.</summary>
        [NonSerialized] public Action<int> RemoteHeal;
        /// <summary>Multiplayer: another game's ship's real maximum hull (its 'hp' / 'maxHp' is the hull fraction x 100), for
        /// the repair / transfusion beams' pick of the most damaged ship; -1 = unknown.</summary>
        [NonSerialized] public float netMaxHull = -1f;
        /// <summary>Multiplayer (NetProxy): another game's EMP-disabled NPC with cargo: its first cargo entry (item -1 = none),
        /// which this player's tractor beam may steal (CombatRadar); RemoteSteal(item, amount) takes it off the owner's ship.</summary>
        [NonSerialized] public int netStealItem = -1, netStealAmount;
        [NonSerialized] public Action<int, int> RemoteSteal;
        /// <summary>Multiplayer: a cargo steal candidate on another game's ship (KIPlayer+0x20 on the owner's side).</summary>
        public bool NetStealable => RemoteSteal != null && netStealItem >= 0 && netStealAmount > 0;
        /// <summary>Multiplayer: the killing hit came from another player (NetProxy): counts as the player's for a freelance
        /// mission (FreelanceOrbit), though the game takes it as an NPC's hit (killedByNpc: no standing change here).</summary>
        [NonSerialized] public bool killedByRemote;
        /// <summary>Multiplayer: the client id of the player whose hit killed it (with killedByRemote; NetOrbit's raid news).</summary>
        [NonSerialized] public ulong remoteKiller = ulong.MaxValue;
        /// <summary>Multiplayer: a squadmate's ship: the players' weapons don't affect it (NPCs still do).</summary>
        [NonSerialized] public bool playerProof;
        /// <summary>Multiplayer: another player's shield / armor fractions (NetPlayer), drawn on their marker (CombatView);
        /// -1 = no such pool (and every NPC).</summary>
        [NonSerialized] public float netShield = -1f, netArmor = -1f;
        /// <summary>Multiplayer: another player's ship (NetPlayer, not a proxied NPC or sentry): its marker carries the pilot's
        /// name (CombatView), so players stand apart from the NPCs of the same colour.</summary>
        [NonSerialized] public bool netPilot;
        /// <summary>The player's cloak (Player+0x5e set by PlayerEgo::toggleCloaking): NPCs keep it as their target but don't
        /// fire, turrets don't aim at it, sleepers don't wake for it.</summary>
        [NonSerialized] public bool cloaked;
        /// <summary>Remake: phased out by a phase cloak (PlayerCloak.Phasing, NetPlayer for the other players): shots fly
        /// through it (Gun), blasts pass it and it takes no damage.</summary>
        [NonSerialized] public bool phased;
        /// <summary>Remake: a player's ship boosting (ShipController for the local one, NetPlayer for the others): homing
        /// missiles locked on it lose their lock (Gun). NPC ships never set it.</summary>
        [NonSerialized] public bool boosting;
        /// <summary>Remake: homing missiles locked on this ship lose their lock for good (Gun): a player boosting or cloaked
        /// (only players set these two).</summary>
        public bool ShakesMissiles => boosting || cloaked;
        [Tooltip("The owner handles the death (ships): no automatic explosion, renderers stay on.")]
        public bool customDeath;
        /// <summary>Asked when the hull runs out: true = saved (PlayerEgo::tryToStartEmergencySystem).</summary>
        [NonSerialized] public Func<bool> SaveFromDeath;
        /// <summary>Takes no damage (a sentry gun's first 3 s, Player+0xc2 cleared).</summary>
        [NonSerialized] public bool invulnerable;
        /// <summary>Remake mods (World.CapitalSubsystems): every hit x this (a capital ship behind its shield generators: 0 = it
        /// shrugs the hits off, the Damaged event still sees them).</summary>
        [NonSerialized] public float damageFactor = 1f;
        /// <summary>Remake mods (World.CapitalSubsystems): a weak spot's hits also go to this target (its capital ship) x
        /// 'forwardFactor'.</summary>
        [NonSerialized] public Target forwardTo;
        [NonSerialized] public float forwardFactor;
        [Tooltip("Local-space hit boxes (metres) instead of the cube (big ships).")]
        public Bounds[] boxes;
        /// <summary>Remake: the hull of MeshColliders shots hit instead of the cube / boxes (HullCollision), null = none.</summary>
        [NonSerialized] public HullBody hull;
        /// <summary>Shots test the hull (HullCollision.Cast), not the cube / boxes.</summary>
        public bool HasHull => hull != null && hull.Collides;   // not taken by the ship collision mode (Mixed: a flyable ship's; Original: any): the cube / boxes
        /// <summary>Remake: a target with a hull is also hit by a shot passing within this many metres of its centre (0 = only
        /// the hull): the player's ship, its collision probe (PlayerCollision), as its real shape alone was too thin a target.</summary>
        [NonSerialized] public float hitSphereRadius;

        /// <summary>Shield / armor / hull pools; null = plain 'hp' (asteroids).</summary>
        [NonSerialized] public Hitpoints hitpoints;
        /// <summary>Player+0x44: the killing hit came from an NPC gun (no kill credit, no standing change).</summary>
        [NonSerialized] public bool killedByNpc;
        /// <summary>Player+0xc4: the last hit's vector (the bullet velocity).</summary>
        [NonSerialized] public Vector3 lastHitVector;
        /// <summary>Player::damage's weapon argument: the item of the player weapon that hit last (-1 = none / an NPC).</summary>
        [NonSerialized] public int lastPlayerWeapon = -1;
        /// <summary>KIPlayer+0x18: a named ship's name (the lock plate shows it instead of race and hull), null = none.</summary>
        [NonSerialized] public string displayName;
        /// <summary>A crate is waiting to be salvaged from this (dead) ship: the radar may lock it.</summary>
        [NonSerialized] public Crate crate;

        /// <summary>PlayerAsteroid::getQualityString: A (7) .. D (4).</summary>
        public string QualityLetter => ((char)('A' + Mathf.Clamp(7 - quality, 0, 4))).ToString();
        /// <summary>The core a class-A asteroid of this ore gives (ore + 11, Novanium's 218); -1 for an ore without one: a
        /// mod's ore (remake mods' orbit "ores") is no game ore, and ore + 11 was whatever item came 11 numbers after it.</summary>
        public int CoreItem => oreItem == 217 ? 218 : oreItem >= 154 && oreItem <= 164 ? oreItem + 11 : -1;

        public bool Alive => hitpoints != null ? hitpoints.Alive : hp > 0f;
        public float HullFraction => hitpoints != null ? hitpoints.HullFraction : maxHp > 0f ? hp / maxHp : 0f;
        /// <summary>Active and alive (a valid target for NPCs).</summary>
        public bool Targetable => Alive && isActiveAndEnabled && !untargetable;

        /// <summary>(target, damage, byNpc) after every hit, before the death check.</summary>
        public event Action<Target, int, bool> Damaged;
        public event Action<Target> Died;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>Player::damage: 'byNpc' = an NPC gun fired it (friendGun); 'hitVector' = the bullet velocity;
        /// 'shieldFactor' = the weapon's x on the shield (remake, Flight.Gun.ShieldFactor; Hitpoints.Damage).</summary>
        public void Damage(float amount, bool byNpc = false, Vector3 hitVector = default, float shieldFactor = 1f)
        {
            if (!Alive || invulnerable || phased) return;
            if (isPlayer && Data.Cheats.GodMode) return;   // remake: the Debug panel's god mode
            if (playerProof && !byNpc) return;
            if (RemoteDamage != null) { RemoteDamage(amount, hitVector, byNpc, shieldFactor); return; }
            // (a weak spot passes on what it takes, not the overkill of the hit that destroys it)
            if (forwardTo != null && forwardTo != this && forwardFactor > 0f && forwardTo.Alive)
                forwardTo.Damage(Mathf.Min(amount * damageFactor, hitpoints != null ? hitpoints.Combined : hp) * forwardFactor, byNpc, hitVector, shieldFactor);
            amount *= damageFactor;
            int dmg = Mathf.Max(0, (int)amount);
            lastHitVector = hitVector;
            bool dead;
            if (hitpoints != null)
            {
                dead = hitpoints.Damage(dmg, shieldFactor);
                hp = hitpoints.hull;
                maxHp = hitpoints.maxHull;
            }
            else
            {
                hp -= amount;
                dead = hp <= 0f;
            }
            Damaged?.Invoke(this, dmg, byNpc);
            if (dead && SaveFromDeath != null && SaveFromDeath()) dead = false;   // the player's emergency system
            if (dead) { killedByNpc = byNpc; Die(true); }
        }

        /// <summary>Destroys it with its explosion and sound (a mined asteroid: PlayerEgo::stopMining sets HP to -1 and clears
        /// the loot flag, so no crate; multiplayer: another game destroyed it and dropped the crate there).</summary>
        public void Explode()
        {
            if (!Alive) return;
            if (hitpoints != null) hitpoints.hull = 0;
            Die(false);
        }

        // Gun's broad phase (MayContain): a sphere around the hit volume, measured once a frame.
        int broadFrame = -1;
        Vector3 broadCentre;
        float broadReach, boxesLocalReach;
        Bounds[] broadBoxes;

        /// <summary>False when 'point' (metres) can't be inside the hit volume, so Contains would be false too: a sphere
        /// around the cube or the boxes, its centre and size taken on the first call each 'frame' (Time.frameCount), with a
        /// margin for the target still moving later in the frame. Every bullet was tested against every target with several
        /// engine calls per pair (the destroyed check, the transform, its position); this is plain arithmetic. A destroyed
        /// target: false.</summary>
        public bool MayContain(Vector3 point, int frame)
        {
            float reach = BroadSphere(frame, out var centre);
            return reach >= 0f && (point - centre).sqrMagnitude <= reach * reach;
        }

        /// <summary>MayContain's sphere this 'frame': its centre and its radius (metres), -1 for a destroyed target. TargetGrid
        /// files the targets by it.</summary>
        public float BroadSphere(int frame, out Vector3 centre)
        {
            if (broadFrame != frame)
            {
                broadFrame = frame;
                if (this == null) { broadReach = -1f; centre = broadCentre; return -1f; }
                var t = transform;
                broadCentre = t.position;
                if (boxes != null && boxes.Length > 0)
                {
                    if (!ReferenceEquals(boxes, broadBoxes))
                    {
                        broadBoxes = boxes;
                        boxesLocalReach = 0f;
                        foreach (var b in boxes) boxesLocalReach = Mathf.Max(boxesLocalReach, b.center.magnitude + b.extents.magnitude);
                    }
                    var s = t.lossyScale;
                    broadReach = boxesLocalReach * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                }
                else broadReach = radius * 1.7321f;   // the cube's corner
                // How far a ship can still move this frame after being measured (at most ~1 m/ms), plus 10 m.
                broadReach += 10f + Time.deltaTime * 1000f;
            }
            centre = broadCentre;
            return broadReach;
        }

        /// <summary>Gun::calcCharacterCollision: 'point' (metres) inside the cube, or inside a local box.</summary>
        public bool Contains(Vector3 point)
        {
            if (boxes != null && boxes.Length > 0)
            {
                var local = transform.InverseTransformPoint(point);
                foreach (var b in boxes) if (b.Contains(local)) return true;
                return false;
            }
            var d = transform.position - point;
            return Mathf.Abs(d.x) < radius && Mathf.Abs(d.y) < radius && Mathf.Abs(d.z) < radius;
        }

        // ---- aim spots (remake, the user's request 2026-10-22: NPCs shot every ship in its middle) ---------------------

        /// <summary>Points on the model NPC fighters and turrets aim at (NpcShip.AimAt), each on a part (so it moves with it):
        /// up to SpotsPerMesh vertices of each readable mesh (not the additive / light / glow layers or a lower LOD), else
        /// the hit boxes' inner corners; none = the centre.</summary>
        [NonSerialized] List<(Transform part, Vector3 local)> aimSpots;
        static readonly Dictionary<Mesh, Vector3[]> meshSpots = new Dictionary<Mesh, Vector3[]>();
        const int SpotsPerMesh = 24;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSpots() => meshSpots.Clear();

        /// <summary>Aim spot 'i' (any number, wrapped) in world space; the centre when the model has none.</summary>
        public Vector3 AimSpot(int i)
        {
            BuildAimSpots();
            if (aimSpots.Count == 0) return transform.position;
            var s = aimSpots[(int)((uint)i % (uint)aimSpots.Count)];
            if (s.part == null) { aimSpots = null; return transform.position; }   // the model was swapped: made again next time
            return s.part.TransformPoint(s.local);
        }

        void BuildAimSpots()
        {
            if (aimSpots != null) return;
            aimSpots = new List<(Transform, Vector3)>();
            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (m == null || !m.isReadable || m.vertexCount == 0 || mf.GetComponent<Renderer>() == null) continue;
                string n = m.name.ToLowerInvariant();
                if (n.Contains("_add") || n.Contains("_lights") || n.Contains("_lod_") || n.Contains("glow")) continue;
                if (!meshSpots.TryGetValue(m, out var pts))
                {
                    var v = m.vertices;
                    int count = Mathf.Min(SpotsPerMesh, v.Length);
                    pts = new Vector3[count];
                    for (int k = 0; k < count; k++) pts[k] = v[(int)((long)k * v.Length / count)];
                    meshSpots[m] = pts;
                }
                foreach (var p in pts) aimSpots.Add((mf.transform, p));
            }
            if (aimSpots.Count > 0 || boxes == null) return;
            foreach (var b in boxes)
                for (int c = 0; c < 8; c++)
                    aimSpots.Add((transform, b.center + Vector3.Scale(b.extents * 0.7f,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
        }

        /// <summary>Remake (World.CapitalShips' turrets): the point of its hit boxes nearest 'from' (world), a little inside the
        /// box so a shot at it lands; its position when it has no boxes.</summary>
        public Vector3 NearestPoint(Vector3 from)
        {
            if (boxes == null || boxes.Length == 0) return transform.position;
            var local = transform.InverseTransformPoint(from);
            Vector3 best = transform.InverseTransformPoint(transform.position);
            float bestD = float.MaxValue;
            foreach (var b in boxes)
            {
                var p = Vector3.MoveTowards(b.ClosestPoint(local), b.center, 3f);
                float d = (p - local).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return transform.TransformPoint(best);
        }

        void Die(bool loot)
        {
            hp = 0f;
            if (loot && isAsteroid && oreItem >= 0) DropAsteroidCrate();
            if (!customDeath)
            {
                foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
                var spin = GetComponent<Spin>();
                if (spin != null) spin.enabled = false;
                if (explosionPrefab != null)
                {
                    // Explosion types 2-5 (the asteroids; PlayerAsteroid -> Explosion::start(Matrix) 0xb5620 / render
                    // 0xb5a60): both meshes take the asteroid's matrix, then the alpha billboard (the first part) is turned
                    // to the camera's direction every frame; its `extra` channel fades it out as it grows. setScaling(s)
                    // 0xb4ee0: below scale 1 the animations run (1 - s) * 3 + 1 times as fast (a small asteroid's is quick).
                    var fx = Instantiate(explosionPrefab, transform.position, transform.rotation);
                    fx.transform.localScale = explosionPrefab.transform.localScale * explosionScale;
                    float speed = isAsteroid && explosionScale < 1f ? (1f - explosionScale) * 3f + 1f : 1f;
                    foreach (var a in fx.GetComponentsInChildren<PartAnimation>(true)) { a.applyMaterialChannels = true; a.speed = speed; }
                    if (explosionTexture != null && fx.transform.childCount > 0)
                    {
                        var block = new MaterialPropertyBlock();
                        foreach (var r in fx.transform.GetChild(0).GetComponentsInChildren<Renderer>(true))
                        {
                            r.GetPropertyBlock(block);   // PartAnimation reads the block back before its fades, so this stays
                            block.SetTexture("_MainTex", explosionTexture);
                            r.SetPropertyBlock(block);
                        }
                    }
                    if (isAsteroid && fx.transform.childCount > 0) CameraFacing.Wrap(fx.transform.GetChild(0));
                    float length = PartAnimation.PlayOnce(fx) / speed;
                    Destroy(fx, Mathf.Max(1f, length / 1000f + 0.2f));
                }
                Sfx.PlayAt(destroyedSound, transform.position);
            }
            Died?.Invoke(this);
            if (!customDeath) All.Remove(this);
        }

        /// <summary>PlayerAsteroid::update 0xf7060: an asteroid destroyed by a hit (shot, rammed, a blast; its loot flag +0x48
        /// is set by the constructor and only mining clears it) leaves a crate: class A (quality 7) 4 % with 1 core (ore + 11,
        /// Novanium's 218), the others 20 % with 1-3 t of its ore; KIPlayer::createCrate(1) the rock container 0x421e
        /// asteroid_01_junk, (2) 0x421f asteroid_void_junk for Void Crystals (164). The asteroid's KIPlayer race is -1: no
        /// race rules on the capture.</summary>
        void DropAsteroidCrate()
        {
            bool classA = quality == 7 && CoreItem >= 0;   // (a class-A rock of an ore without a core drops its ore)
            if (UnityEngine.Random.Range(0, 100) > (classA ? 3 : 19)) return;
            var stack = new GoF2Remake.Data.ItemStack(classA ? CoreItem : oreItem, classA ? 1 : UnityEngine.Random.Range(0, 3) + 1);
            Crate.Spawn(transform.position, new[] { stack }, -1, oreItem == 164 ? Crate.LookVoidRock : Crate.LookRock);
        }

        /// <summary>A dead ship relaunched (KIPlayer::revive): full pools.</summary>
        public void Revive()
        {
            if (hitpoints != null)
            {
                hitpoints.hull = hitpoints.maxHull;
                hitpoints.shield = hitpoints.maxShield;
                hitpoints.armor = hitpoints.maxArmor;
                hitpoints.emp = hitpoints.maxEmp;
                hitpoints.empDisabled = false;
                hp = maxHp = hitpoints.maxHull;
            }
            else hp = maxHp;
            killedByNpc = false;
            killedByRemote = false;
            remoteKiller = ulong.MaxValue;
            crate = null;
            if (isActiveAndEnabled && !All.Contains(this)) All.Add(this);
        }
    }
}
