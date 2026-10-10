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
        /// <summary>Radar::drawCurrentLock: the Hijacker (1611) and the Informer (1663) show their name alone; a Most Wanted
        /// criminal is drawn in its own colour.</summary>
        public bool plateNameOnly, plateWanted;
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
        /// <summary>Multiplayer: a hit (amount, hit vector, by an NPC) is passed on to the game that owns the ship (NetProxy,
        /// NetPlayer) instead of applied here.</summary>
        [NonSerialized] public Action<float, Vector3, bool> RemoteDamage;
        /// <summary>Multiplayer: EMP (points) passed on to the game that owns the ship (NetProxy, NetPlayer).</summary>
        [NonSerialized] public Action<int> RemoteEmp;
        /// <summary>Multiplayer: the killing hit came from another player (NetProxy): counts as the player's for a freelance
        /// mission (FreelanceOrbit), though the game takes it as an NPC's hit (killedByNpc: no standing change here).</summary>
        [NonSerialized] public bool killedByRemote;
        /// <summary>Multiplayer: the client id of the player whose hit killed it (with killedByRemote; NetOrbit's raid news).</summary>
        [NonSerialized] public ulong remoteKiller = ulong.MaxValue;
        /// <summary>Multiplayer: a squadmate's ship: the players' weapons don't affect it (NPCs still do).</summary>
        [NonSerialized] public bool playerProof;
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
        [Tooltip("Local-space hit boxes (metres) instead of the cube (big ships).")]
        public Bounds[] boxes;
        /// <summary>Remake: the hull of MeshColliders shots hit instead of the cube / boxes (HullCollision), null = none.</summary>
        [NonSerialized] public HullBody hull;
        /// <summary>Shots test the hull (HullCollision.Cast), not the cube / boxes.</summary>
        public bool HasHull => hull != null && hull.Active;

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
        public int CoreItem => oreItem == 217 ? 218 : oreItem + 11;

        public bool Alive => hitpoints != null ? hitpoints.Alive : hp > 0f;
        public float HullFraction => hitpoints != null ? hitpoints.HullFraction : maxHp > 0f ? hp / maxHp : 0f;
        /// <summary>Active and alive (a valid target for NPCs).</summary>
        public bool Targetable => Alive && isActiveAndEnabled && !untargetable;

        /// <summary>(target, damage, byNpc) after every hit, before the death check.</summary>
        public event Action<Target, int, bool> Damaged;
        public event Action<Target> Died;

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() => All.Remove(this);

        /// <summary>Player::damage: 'byNpc' = an NPC gun fired it (friendGun); 'hitVector' = the bullet velocity.</summary>
        public void Damage(float amount, bool byNpc = false, Vector3 hitVector = default)
        {
            if (!Alive || invulnerable || phased) return;
            if (isPlayer && Data.Cheats.GodMode) return;   // remake: the Debug panel's god mode
            if (playerProof && !byNpc) return;
            if (RemoteDamage != null) { RemoteDamage(amount, hitVector, byNpc); return; }
            int dmg = Mathf.Max(0, (int)amount);
            lastHitVector = hitVector;
            bool dead;
            if (hitpoints != null)
            {
                dead = hitpoints.Damage(dmg);
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
            if (broadFrame != frame)
            {
                broadFrame = frame;
                if (this == null) { broadReach = -1f; return false; }
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
            return broadReach >= 0f && (point - broadCentre).sqrMagnitude <= broadReach * broadReach;
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
            bool classA = quality == 7;
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
