// Gun.cs
// One weapon on one mount, like the original's Gun (Reference/research/weapons.md, weapons_special.md): a fixed pool of
// bullets, a reload timer, straight flight along the ship's nose (no convergence, no lead), and the original's hit test.
// Plain C#: WeaponSystem feeds it the ship pose and targets and draws the bullets.
//   Gun::shootAt 0x17d518   spawn at ship + R * (mount + (0, 0, 100)), velocity = R * forward * speed, spread; beams
//                           (items 9-11, 228) are hitscan on the nearest auto-aim target (none: a bullet parked 30000
//                           ahead, the next shot waits for its lifetime); cluster missiles fire every free bullet at once
//   Gun::update 0x17e940    reload += dt; bullets move; lifetime = attr 12 ms ("range" is a time); rockets and
//                           missiles coast and still hit for 2000 ms after their lifetime; bombs ignite at the end
//   Gun::calcCharacterCollision 0x17e154  axis-aligned cube |target - bullet + vel| < radius on every axis; scatter guns
//                           scale the cube with the player-target distance (x1.5 / 2 / 3) and add area damage; bombs and
//                           the shock blast ignite on contact; rockets, missiles and bombs kill an asteroid outright and
//                           are NOT used up by it; mines are pulled toward a hostile ship inside 5x its radius and ignite
//                           inside 1x
//   Gun::ignite 0x17dd08    area damage f = clamp((mag - d) / mag, 0, 1) (mines: (10000 - d) / 10000) to every target
//                           within attr 14; EMP bombs (sort 6) do EMP only and skip asteroids; asteroids take x0.6
//   RocketGun::seekEnemy 0x18bd70  missiles steer 1/6 of the error per (30 fps) frame toward the locked target (an NPC's:
//                           its current target, from 1000 ms after the launch: NPC rockets have no trails)
//   RocketGun::update       cluster rockets corkscrew around their path (radius ~670 units)
//   MineGun::update 0x181c8c  mines drop (forward + up) at 2 u/ms and stop within 500 ms
//   Level::assignGuns 0xcb638  NPC guns: 4 bullets, 16 u/ms, 3000 ms, the race's reload and damage, mount at the ship
//                           centre (the item only gives the look); 'owner' is never hit by its own bullets
// Units: positions in Unity metres, times in ms, velocities in metres per ms (game speed u/ms * 0.05).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Gun
    {
        public enum Kind
        {
            Laser = 0, Blaster = 1, AutoCannon = 2, Thermo = 3, Rocket = 4, Missile = 5, EmpBomb = 6, Nuke = 7, Turret = 8,
            Mine = 11, ScatterGun = 25, Ionizing = 34, PlasmaCollector = 35, Sentry = 39, ClusterMissile = 40, ShockBlast = 42,
        }

        public struct Bullet
        {
            public Vector3 position, velocity, up;
            public float timer;        // ms left; <= limit = free
            public float age;          // ms since the shot (mines, cluster corkscrew)
            public bool lockLost;      // remake: a missile shaken off by a boost flies straight on (Target.boosting)
            public bool Active(float limit) => timer > limit;
        }

        public const float MetersPerUnit = 0.05f;
        const float CoastMs = 2000f;          // rockets/missiles keep flying (and hitting) past their lifetime
        const float SpawnForwardUnits = 100f; // offset = mount + (0, 0, 100)
        public const float BeamRangeUnits = 30000f;  // no target: the beam's bullet is parked this far ahead
        const float MineSpeedUnits = 2f, MineStopMs = 500f, MinePullUnitsPerFrame = 200f;
        const float ShockBlastRadiusUnits = 80000f;

        public readonly int itemIndex;
        /// <summary>The original item this gun looks and works like (a mod's item: its base; Modding.ModContent.ItemLook).</summary>
        public readonly int lookIndex;
        public readonly Kind kind;
        public readonly int categoryId;
        public float damage;
        /// <summary>EMP per hit (attr 10); settable for the wingmen's Dia EMP Mk III.</summary>
        public float emp;
        public float reloadMs;
        public readonly float lifetimeMs, speedUnitsPerMs;
        /// <summary>Attr 14: the blast radius of bombs, mines and scatter guns (units).</summary>
        public readonly float magnitude;
        public readonly Vector3 mountLocal;   // Unity metres, ship space
        public readonly Bullet[] bullets;
        public readonly bool isSecondary;
        /// <summary>Items 9, 10, 11, 228: hitscan beam lasers.</summary>
        public readonly bool isBeam;
        /// <summary>The ship carrying the gun (never hit by it).</summary>
        public Target owner;
        public float reloadAcc;               // ms since the last shot
        public float spreadError;             // Gun+0xe0: 2 auto-cannon/scatter, 20 thermo, 0 otherwise
        /// <summary>RocketGun::update: homing only once the bullet is this old (NPC rockets 1000 ms, the player's at once).</summary>
        public float homingDelayMs;

        /// <summary>Multiplayer: targets this gun's bullets pass straight through (a squadmate of the shooter), null = none.</summary>
        public Func<Target, bool> Ignores;

        /// <summary>Beams: picks the nearest auto-aim target (on screen, &lt; 60000 units, in the crosshair box), or null.</summary>
        public Func<Target> AutoAim;
        /// <summary>The last beam's world direction and length (units), set when it fires.</summary>
        public Vector3 BeamDir { get; private set; }
        public float BeamLengthUnits { get; private set; }
        /// <summary>The target the last beam locked, null = none (straight ahead).</summary>
        public Transform BeamTarget { get; private set; }

        /// <summary>Raised when a bullet hits: (bullet index, target, hit point). Rockets on asteroids: the bullet lives on.</summary>
        public event Action<int, Target, Vector3> Hit;
        /// <summary>Gun::ignite area damage on one target: (target, hull damage, EMP damage, blast centre).</summary>
        public event Action<Target, int, int, Vector3> AreaHit;
        /// <summary>A rocket / missile's bullet ran out of life without hitting anything (Gun::update, medal 41's reset).</summary>
        public event Action<int> Expired;
        /// <summary>A bomb, mine, scatter shell or the shock blast went off at this point (the explosion).</summary>
        public event Action<Vector3> Ignited;
        /// <summary>A shot left the gun (gun, bullet index; a cluster salvo: its first bullet). Multiplayer sends it to the
        /// other players (NetProxy, NetPlayer).</summary>
        public event Action<Gun, int> Fired;
        /// <summary>Any gun's bomb / mine / blast went off (gun, Unity point): the gas clouds listen for the ionizing missiles.</summary>
        public static event Action<Gun, Vector3> Detonated;
        /// <summary>Remake: a homing missile lost its lock on this target, which boosted (Target.boosting): any gun, any shooter.</summary>
        public static event Action<Gun, Target> LockShaken;
        /// <summary>The lock the last Update steered toward (multiplayer: NetShotSender sends it with a shot, so a capital
        /// ship's salvo is mirrored at its own target, not the ship's).</summary>
        public Target LastLock { get; private set; }

        public bool Homing => kind == Kind.Missile || kind == Kind.ClusterMissile || kind == Kind.Thermo;
        bool Coasts => kind == Kind.Rocket || kind == Kind.Missile || kind == Kind.ClusterMissile;
        /// <summary>Sorts 6 / 7 / 34: slow bombs that go off at the end of life, on contact or on the next press.</summary>
        public bool IsBomb => kind == Kind.EmpBomb || kind == Kind.Nuke || kind == Kind.Ionizing;
        /// <summary>Rockets, missiles and bombs kill asteroids outright (9999) and keep flying.</summary>
        bool KillsAsteroids => Coasts || IsBomb || kind == Kind.ShockBlast;
        float FreeLimit => Coasts ? -CoastMs : 0f;
        public bool Ready => reloadAcc > reloadMs && (!isBeam || bullets[0].timer <= 0f);

        IReadOnlyList<Target> lastTargets;

        public Gun(ItemData item, Vector3 mountLocal, bool secondary)
        {
            itemIndex = item.index;
            lookIndex = item.Look;
            categoryId = item.categoryId;
            kind = (Kind)item.categoryId;
            isSecondary = secondary;
            int look = item.Look;   // a mod's item works like its base (Modding.ModContent)
            isBeam = look == 9 || look == 10 || look == 11 || look == 228;
            damage = item.Attr(9);
            emp = item.Attr(10);
            reloadMs = Mathf.Max(1, item.Attr(11, 500));
            lifetimeMs = kind == Kind.ShockBlast ? 1f : item.Attr(12, 2000);
            speedUnitsPerMs = kind == Kind.Mine ? MineSpeedUnits : kind == Kind.ShockBlast ? 0f : item.Attr(13, 20);
            magnitude = kind == Kind.ShockBlast ? ShockBlastRadiusUnits : item.Attr(14);
            this.mountLocal = mountLocal;
            // Level::createGun: pool sizes and spread per sort.
            int pool = kind switch
            {
                Kind.AutoCannon or Kind.ScatterGun or Kind.Turret => kind == Kind.Turret ? 15 : 25,
                Kind.Rocket or Kind.Missile => 5,
                Kind.ClusterMissile => Mathf.Max(1, look - 211),   // Shesha 3, Garuda 4, Patala 5
                Kind.EmpBomb or Kind.Nuke or Kind.Ionizing or Kind.ShockBlast => 1,
                Kind.Mine => 10,
                Kind.Sentry => 3,
                _ => isBeam ? 1 : 20,
            };
            spreadError = kind switch { Kind.AutoCannon or Kind.ScatterGun => 2f, Kind.Thermo => 20f, _ => 0f };
            bullets = new Bullet[pool];
            for (int i = 0; i < pool; i++) bullets[i].timer = -1e9f;
            reloadAcc = reloadMs + 1f;   // ready at start
        }

        /// <summary>Level::assignGuns: an NPC gun with the look of 'visualItem' and the generic NPC stats.</summary>
        public Gun(ItemData visualItem, float damage, float reloadMs, int pool, float lifetimeMs, float speedUnitsPerMs)
        {
            itemIndex = visualItem.index;
            lookIndex = visualItem.Look;
            categoryId = visualItem.categoryId;
            kind = (Kind)visualItem.categoryId;
            this.damage = damage;
            this.reloadMs = Mathf.Max(1f, reloadMs);
            this.lifetimeMs = lifetimeMs;
            this.speedUnitsPerMs = speedUnitsPerMs;
            mountLocal = Vector3.zero;
            bullets = new Bullet[pool];
            for (int i = 0; i < pool; i++) bullets[i].timer = -1e9f;
            reloadAcc = this.reloadMs + 1f;   // Gun::Gun: the delay starts at the reload time, the first shot is ready
        }

        public bool IsActive(int i) => bullets[i].Active(FreeLimit);

        /// <summary>Any bomb of this gun still flying (the next secondary press detonates it).</summary>
        public bool BombInFlight
        {
            get
            {
                if (!IsBomb) return false;
                for (int i = 0; i < bullets.Length; i++) if (bullets[i].timer > 0f) return true;
                return false;
            }
        }

        /// <summary>Gun::shootAt: returns the bullet index, or -1 when reloading or the pool is exhausted.</summary>
        public int TryFire(Transform ship) => TryFire(ship.position, ship.rotation);

        /// <summary>Gun::shootAt from an arbitrary pose (turrets fire from the turret gun's matrix).</summary>
        public int TryFire(Vector3 origin, Quaternion rotation, bool addForwardOffset = true)
        {
            if (!Ready) return -1;
            int free = -1;
            for (int i = 0; i < bullets.Length; i++) if (bullets[i].timer <= FreeLimit) { free = i; break; }
            if (free < 0) return -1;

            var fwd = rotation * Vector3.forward;
            var up = rotation * Vector3.up;
            Vector3 Spawn() => origin + rotation * (mountLocal + (addForwardOffset && kind != Kind.ShockBlast
                ? Vector3.forward * SpawnForwardUnits * MetersPerUnit : Vector3.zero));

            if (isBeam)
            {
                // Gun::shootAt beam branch: hitscan on the nearest auto-aim target, else a bullet parked 30000 ahead.
                var target = AutoAim?.Invoke();
                ref var bb = ref bullets[0];
                var from = Spawn();
                if (target != null)
                {
                    var d = target.transform.position - from;
                    BeamDir = d.normalized;
                    BeamLengthUnits = d.magnitude / MetersPerUnit;
                    BeamTarget = target.transform;
                    bb.position = target.transform.position;
                }
                else
                {
                    BeamDir = fwd;
                    BeamLengthUnits = BeamRangeUnits;
                    BeamTarget = null;
                    bb.position = from + fwd * BeamRangeUnits * MetersPerUnit;
                }
                bb.velocity = fwd * MetersPerUnit;   // a unit vector (1 u/ms): hits the target centre on the next pass
                bb.up = up;
                bb.timer = lifetimeMs;
                bb.age = 0f;
                bb.lockLost = false;
                reloadAcc = 0f;
                Fired?.Invoke(this, 0);
                return 0;
            }

            if (kind == Kind.ClusterMissile)
            {
                // Every free bullet in the same call: one salvo, one ammo.
                for (int i = 0; i < bullets.Length; i++)
                {
                    if (bullets[i].timer > FreeLimit) continue;
                    ref var c = ref bullets[i];
                    c.position = Spawn();
                    c.velocity = fwd * speedUnitsPerMs * MetersPerUnit;
                    c.up = up;
                    c.timer = lifetimeMs;
                    c.age = 0f;
                    c.lockLost = false;
                }
                reloadAcc = 0f;
                Fired?.Invoke(this, free);
                return free;
            }

            var dir = Vector3.forward;
            if (spreadError > 0f)
            {
                // each axis += rnd(int(err)) * 0.01 - err * 0.005, i.e. uniform in [-err/200, err/200)
                float e = spreadError;
                dir += new Vector3(UnityEngine.Random.Range(0, (int)e) * 0.01f - e * 0.005f,
                                   UnityEngine.Random.Range(0, (int)e) * 0.01f - e * 0.005f, 0f);
            }
            ref var b = ref bullets[free];
            b.position = Spawn();
            b.velocity = kind == Kind.Mine ? (fwd + up).normalized * MineSpeedUnits * MetersPerUnit
                                           : rotation * dir.normalized * speedUnitsPerMs * MetersPerUnit;
            b.up = up;
            b.timer = lifetimeMs;
            b.age = 0f;
            b.lockLost = false;
            reloadAcc = 0f;
            Fired?.Invoke(this, free);
            return free;
        }

        /// <summary>Multiplayer: another player's shot drawn by this (visual-only) gun (NetShotMirror): a free bullet at
        /// that pose with the sender's life left; false when the pool is full.</summary>
        public bool Inject(Vector3 position, Vector3 velocity, Vector3 up, float lifeMs)
        {
            for (int i = 0; i < bullets.Length; i++)
            {
                if (bullets[i].timer > FreeLimit) continue;
                ref var b = ref bullets[i];
                b.position = position;
                b.velocity = velocity;
                b.up = up;
                b.timer = lifeMs;
                b.age = 0f;
                b.lockLost = false;
                return true;
            }
            return false;
        }

        /// <summary>Gun::update: move bullets, home missiles, test hits. 'lockTarget' may be null.</summary>
        public void Update(float dtMs, IReadOnlyList<Target> targets, Target lockTarget)
        {
            reloadAcc += dtMs;
            // Remake: a lock that went away because its ship cloaked (the radar drops it, an NPC stops homing on a cloaked
            // target) shakes off the missiles too, so they don't home again on the next lock or when the cloak ends.
            var previousLock = LastLock;
            bool dropShaken = Homing && previousLock != null && previousLock != lockTarget && previousLock.ShakesMissiles;
            LastLock = lockTarget;
            lastTargets = targets;
            float limit = FreeLimit;
            // Missiles: 1/6 of the error per 33 ms frame, made frame-rate independent.
            float steer = Homing && lockTarget != null && lockTarget.Alive ? 1f - Mathf.Pow(5f / 6f, dtMs / 33.3f) : 0f;
            bool shaken = false;
            for (int i = 0; i < bullets.Length; i++)
            {
                ref var b = ref bullets[i];
                if (b.timer <= limit) continue;
                // Remake: a boost or a cloak shakes off every missile homing on that ship (players only: the local player's
                // ship and, in multiplayer, the other players' copies, NetPlayer); they fly straight on. The original has no
                // evasion.
                if (dropShaken && !b.lockLost) { b.lockLost = true; shaken = true; }
                if (steer > 0f && !b.lockLost && lockTarget.ShakesMissiles) { b.lockLost = true; shaken = true; }
                if (steer > 0f && !b.lockLost && lockTarget.isPlayer) IncomingMissiles.Report(this, i, b.position, b.velocity);
                if (steer > 0f && !b.lockLost && b.age >= homingDelayMs)
                {
                    float speed = b.velocity.magnitude;
                    var desired = (lockTarget.transform.position - b.position).normalized;
                    var cur = b.velocity / Mathf.Max(speed, 1e-6f);
                    b.velocity = Vector3.Normalize(cur + (desired - cur) * steer) * speed;
                }
                b.timer -= dtMs;
                b.age += dtMs;
                if (Coasts && b.timer <= limit) { Expired?.Invoke(i); continue; }   // Gun::update: a rocket ran out
                if (kind == Kind.ShockBlast) { Ignite(targets); continue; }   // one instant blast at the ship
                if (kind == Kind.Mine) b.position += b.velocity * dtMs * Mathf.Max(1f - b.age / MineStopMs, 0f);
                else b.position += b.velocity * dtMs;
                if (kind == Kind.ClusterMissile) Corkscrew(i, ref b, dtMs);
                if (IsBomb && b.timer < 1f) { Ignite(targets); continue; }   // end of life
                if (targets != null)
                {
                    if (kind == Kind.Mine) TestMine(i, ref b, targets, dtMs);
                    else TestHits(i, ref b, targets);
                }
            }
            if (shaken) LockShaken?.Invoke(this, dropShaken ? previousLock : lockTarget);
        }

        /// <summary>RocketGun::update, sort 40: each rocket winds around its path, phase-shifted by its slot.</summary>
        void Corkscrew(int i, ref Bullet b, float dtMs)
        {
            float speed = b.velocity.magnitude;
            if (speed < 1e-6f) return;
            var dir = b.velocity / speed;
            var side = Vector3.Cross(dir, b.up).normalized;
            float phase = lifetimeMs * i / bullets.Length;
            float a = (b.age + phase) * 0.003f;
            b.position += (side * (2f * Mathf.Sin(a)) + b.up * (2f * Mathf.Cos(a))) * dtMs * MetersPerUnit;
        }

        void TestHits(int i, ref Bullet b, IReadOnlyList<Target> targets)
        {
            int frame = Time.frameCount;
            var probe = b.position - b.velocity;
            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                // The cheap sphere test first (Target.MayContain): most targets are nowhere near the bullet. Scatter guns'
                // cube grows with the distance, so they keep the full test.
                if ((object)target == null || (kind != Kind.ScatterGun && !target.MayContain(probe, frame))) continue;
                if (target == null || target == owner || !target.Alive || target.phased) continue;   // phased: flies through
                if (Ignores != null && Ignores(target)) continue;
                if (kind == Kind.ScatterGun ? !InScatterCube(target, b.position - b.velocity) : !target.Contains(b.position - b.velocity)) continue;
                // Remake: a shot inside a big hull's boxes and a smaller target's cube (a capital ship's turret on its hull)
                // hits the smaller one. Gun::calcCharacterCollision takes the first target of the list, and the host comes
                // before its turrets, so most turrets, inside the hull's boxes, couldn't be shot (the hull took it); shots
                // anywhere else on the hull still hit the hull.
                if (target.boxes != null && target.boxes.Length > 0) target = SmallerInside(target, probe, targets, frame);
                var point = b.position;
                if (target.isAsteroid && KillsAsteroids)
                {
                    // Asteroid contact: destroyed outright, the rocket / bomb keeps flying (not consumed).
                    Hit?.Invoke(i, target, point);
                    continue;
                }
                if (IsBomb) { Ignite(targets); return; }   // contact: area damage, no direct hit
                b.timer = -1e9f;   // gone
                Hit?.Invoke(i, target, point);
                if (kind == Kind.ScatterGun) { AreaDamage(point, targets, false); Ignited?.Invoke(point); }   // + the burst around
                return;
            }
        }

        /// <summary>The first target without hit boxes whose cube holds 'p' (a turret on 'hull'), else 'hull'.</summary>
        Target SmallerInside(Target hull, Vector3 p, IReadOnlyList<Target> targets, int frame)
        {
            for (int t = 0; t < targets.Count; t++)
            {
                var o = targets[t];
                if ((object)o == null || o == hull || (o.boxes != null && o.boxes.Length > 0) || !o.MayContain(p, frame)) continue;
                if (o == null || o == owner || !o.Alive || o.phased || o.isAsteroid) continue;
                if (Ignores != null && Ignores(o)) continue;
                if (o.Contains(p)) return o;
            }
            return hull;
        }

        /// <summary>Scatter guns: the hit cube grows with the shooter's distance to the target (a proximity fuse).</summary>
        bool InScatterCube(Target target, Vector3 p)
        {
            float distUnits = owner != null ? (target.transform.position - owner.transform.position).magnitude / MetersPerUnit : 0f;
            float k = distUnits <= 10000f ? 1.5f : distUnits <= 20000f ? 2f : 3f;
            var d = target.transform.position - p;
            float r = target.radius * k;
            if (target.boxes != null && target.boxes.Length > 0) return target.Contains(p);
            return Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r;
        }

        /// <summary>Gun::calcCharacterCollision, mine branch: hostile ships only; pulled in from 5x their radius, ignite at 1x.</summary>
        void TestMine(int i, ref Bullet b, IReadOnlyList<Target> targets, float dtMs)
        {
            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                if (target == null || target == owner || !target.Alive || !target.isShip || !target.hostileToPlayer || target.mineProof) continue;
                var d = target.transform.position - b.position;
                float r = target.radius;
                if (Mathf.Abs(d.x) >= 5f * r || Mathf.Abs(d.y) >= 5f * r || Mathf.Abs(d.z) >= 5f * r) continue;
                if (Mathf.Abs(d.x) < r && Mathf.Abs(d.y) < r && Mathf.Abs(d.z) < r) { Ignite(targets); return; }
                b.velocity = d.normalized * MineSpeedUnits * MetersPerUnit;
                b.position += d.normalized * MinePullUnitsPerFrame * MetersPerUnit * (dtMs / 33.3f);   // 200 units per frame
                return;
            }
        }

        /// <summary>Attr 15 = 1 (the Liberator): a remote-steered bomb.</summary>
        public bool Guided { get; set; }

        /// <summary>PlayerEgo::left / right / up / down in rocket control: turns bullet 'i' by the stick (x yaw, y pitch)
        /// at 'rateRadPerMs' (the original's rates were stored but their reader wasn't found: a tuned rate).</summary>
        public void SteerBullet(int i, Vector2 stick, float dtMs, float rateRadPerMs)
        {
            ref var b = ref bullets[i];
            float speed = b.velocity.magnitude;
            if (speed < 1e-9f) return;
            var fwd = b.velocity / speed;
            var right = Vector3.Cross(b.up, fwd).normalized;
            var rot = Quaternion.AngleAxis(stick.x * rateRadPerMs * dtMs * Mathf.Rad2Deg, b.up)
                    * Quaternion.AngleAxis(-stick.y * rateRadPerMs * dtMs * Mathf.Rad2Deg, right);
            b.velocity = rot * fwd * speed;
            b.up = rot * b.up;
        }

        /// <summary>Every bullet gone without effect (PlayerEgo::killLiberator: the rocket is simply removed).</summary>
        public void RemoveAll()
        {
            for (int i = 0; i < bullets.Length; i++) bullets[i].timer = -1e9f;
        }

        /// <summary>Gun::ignite for every bomb in flight (the secondary pressed again).</summary>
        public void Detonate()
        {
            if (!BombInFlight) return;
            Ignite(lastTargets);
        }

        /// <summary>Gun::ignite: area damage around each live bullet; mines chain (every mine with a target in reach goes off).</summary>
        void Ignite(IReadOnlyList<Target> targets)
        {
            for (int i = 0; i < bullets.Length; i++)
            {
                ref var b = ref bullets[i];
                // Fired and not yet gone: a mine only while it lives; bombs and the blast also in the frame their time ran out.
                bool live = kind == Kind.Mine ? b.timer > 0f : b.timer > -1e8f;
                if (!live) continue;
                if (kind == Kind.Mine && !AnyTargetInReach(b.position, targets)) continue;
                var at = b.position;
                b.timer = -1e9f;
                AreaDamage(at, targets, true);
                Ignited?.Invoke(at);
                Detonated?.Invoke(this, at);
            }
        }

        bool AnyTargetInReach(Vector3 p, IReadOnlyList<Target> targets)
        {
            if (targets == null) return false;
            float mag = magnitude * MetersPerUnit;
            foreach (var t in targets)
                if (t != null && t != owner && t.Alive && (t.transform.position - p).sqrMagnitude < mag * mag) return true;
            return false;
        }

        /// <summary>The blast: every target within attr 14 takes damage * f and EMP * f.</summary>
        void AreaDamage(Vector3 center, IReadOnlyList<Target> targets, bool bomb)
        {
            if (targets == null || magnitude <= 0f) return;
            for (int t = 0; t < targets.Count; t++)
            {
                var target = targets[t];
                if (target == null || target == owner || !target.Alive || target.isPlayer || target.phased) continue;
                if (kind == Kind.EmpBomb && target.isAsteroid) continue;   // sort 6 skips asteroids
                float d = (target.transform.position - center).magnitude / MetersPerUnit;
                if (d >= magnitude) continue;
                float f = kind == Kind.Mine ? (10000f - d) / 10000f : Mathf.Clamp01((magnitude - d) / magnitude);
                if (f <= 0f) continue;
                int dmg = 0, e = (int)(f * emp);
                if (kind != Kind.EmpBomb)   // EMP bombs: EMP only, attr 9 is never applied
                {
                    float fd = bomb && target.isAsteroid ? f * 0.6f : f;
                    dmg = (int)(fd * damage);
                }
                if (dmg > 0 || e > 0) AreaHit?.Invoke(target, dmg, e, center);
            }
        }

        /// <summary>Render scale: projectiles shrink linearly over their last 1000 ms (ObjectGun); rockets, missiles, bombs
        /// and mines keep their size (the original's scale goes negative while coasting).</summary>
        public float VisualScale(int i) => Coasts || IsBomb || kind == Kind.Mine || isBeam ? 1f : Mathf.Clamp01(bullets[i].timer / 1000f);
    }
}
