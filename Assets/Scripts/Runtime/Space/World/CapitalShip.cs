// CapitalShip.cs
// Remake (option "Capital ship enhancements", Settings.CapitalShips): one capital ship of the orbit's traffic (the Terran
// battleship, the carrier, the Vossk battleship) built with the option on (SpawnSpec.capitalEnhanced). Added by Traffic next
// to its NpcShip. The rules are CapitalShips' (see there):
//   provoked    the player's first hit on a friendly or neutral one says "Hold your fire!" (Traffic.FriendTurnedEnemy); 3
//               hits or 0.5 % of its hull turn the race hostile (Traffic.AlarmAllFriends: its turrets, escorts and the local
//               fighters, the station remembers); a hostile one is provoked by the first hit
//   launches    the carrier (Inflicts, docking set 5's deck pads) and the Vossk battleship (H'Socs, set 8's wing pads),
//               attacked by NPCs or by a provoking player in the last 20 s, launch 5 fighters from the pads' approach points
//               every 20 s, 15 in all, hostile to the player once provoked (and to the other players who shot it, its
//               multiplayer aggressors)
//   death       the carrier / Vossk battleship: explosions along its hull through its 8 s of dying (NpcShip, deathMs) before
//               the big blast; its turrets go with it; the player's kill is a delict of 20 with the race, the Vossk one counts
//               as a battleship destroyed (medal 39)
//   crate       every enhanced capital ship (the Terran battleship too) carries one (CapitalShips.RollLoot): its hold, rare goods
//               and cells, and the gear only an hour of play after the last capital ship's gear was taken
//   fleet battle (SpawnSpec.fleetBattle) both sail a shared course (BattleCourse: a point at 0.35 u/ms whose heading curves
//               gently left and right, back toward where the battle began past 60 000 units and away from the station), each
//               in its lane 25 000 units to its side (21 000, +-6000 weaving, +-3000 up and down), steering toward a point 20 000
//               units ahead in the lane: at most 2 deg/s, banking into the turn up to 15 deg, pitching at most 8 deg, faster
//               or slower to stay abreast; their turrets parented to them, their collision volumes turning along
//               (Obstacle.rotates), their escorts' loops moved along (SpawnSpec.routeAround); the system race's one calls the
//               battle 6 s in; at a death the winners call it and coast to a stop, and a player who helped (the kill or 5 %
//               of the hull) is paid while the winners aren't hostile; no docking meanwhile
//   missiles    every 12-15 s a salvo of 6 homing missiles, each race its own (Terran: Intelli Jet 37, fast; Vossk: S'koonn 38,
//               the Vossk-made one (attr 60), slower and harder; the item's speed, damage relative to 100, 10 s of flight)
//               launched upward from its turret mounts with the player's smoke trails: in a fleet battle at the enemy capital
//               ship, each missile at its own spot of it (CapitalShips.SpreadAim: a turret or anywhere on the hull), 1 in 3
//               at a hostile fighter within 40 000 units of its hull instead, else at the nearest hostile ship within 40 000 units of its hull (the player only when hostile to it);
//               the NPC gun damage x10 on a big ship's hull, x3 on a fighter (x0.2 a stray hit on a player it isn't after);
//               a boost shakes them off: the salvo at the player loses its lock (the missiles fly straight on) and stops

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class CapitalShip : MonoBehaviour
    {
        const float M = 0.05f;

        /// <summary>Every capital ship in the orbit by its spec (the battle area for its fighters, BattleArea).</summary>
        static readonly Dictionary<SpawnSpec, CapitalShip> bySpec = new Dictionary<SpawnSpec, CapitalShip>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => bySpec.Clear();

        /// <summary>A fleet battle's area for the fighters of 'host' (its escorts, wing, launched ships): the shared course's
        /// point (between the two capital ships) and how far from it (Unity metres) a fighter takes an enemy; false = no
        /// battle under way.</summary>
        public static bool BattleArea(SpawnSpec host, out Vector3 centre, out float reach)
        {
            centre = Vector3.zero;
            reach = CapitalShips.BattleLeashUnits * M;
            if (host == null || !bySpec.TryGetValue(host, out var c) || c == null || c.course == null) return false;
            centre = c.course.point;
            return true;
        }

        NpcShip ship;
        Traffic traffic;
        CombatAssets assets;
        int playerHits, playerDamage, playerDamageTotal;
        bool warned, provoked, gearLoot;
        float sinceAttackMs = float.MaxValue, waveMs;
        int launched;
        Database db;
        float blastMs;
        readonly List<Vector3> pads = new List<Vector3>();   // ship-local Unity
        NpcShip foe;
        bool partsRide, battleCalled;
        float battleMs;
        /// <summary>What it has to hand out this visit, all of its race (CapitalShips.Store; the resupply window).</summary>
        [System.NonSerialized] public CapitalShips.Store store;   // runtime only (not a field to save in the scene)

        /// <summary>The ships it launched (the carrier's Inflicts).</summary>
        public int Launched => launched;
        public bool Provoked => provoked;

        /// <summary>Multiplayer (NetOrbit.AdoptHandedOver): taken over from another game, where it had launched 'count' ships
        /// and been provoked or not: its waves go on from there, not from the start.</summary>
        public void Restore(int count, bool wasProvoked)
        {
            launched = Mathf.Max(launched, count);
            if (wasProvoked) provoked = true;
        }
        /// <summary>A fleet battle's capital ship whose enemy capital ship still stands (no docking meanwhile).</summary>
        public bool InBattle => ship != null && ship.Spec.fleetBattle
                                && (foe == null ? traffic.FleetBattleRaging : foe.Target.Alive && !foe.Gone && foe.Current == NpcShip.State.Fly);
        /// <summary>Still moving after a fleet battle (it coasts to a stop): no docking yet.</summary>
        public bool Underway => partsRide && speedScale > 0.01f;

        public static CapitalShip Attach(NpcShip host, Traffic owner, Database db)
        {
            var c = host.gameObject.AddComponent<CapitalShip>();
            c.ship = host;
            c.store = new CapitalShips.Store(host.Race);
            bySpec[host.Spec] = c;
            c.traffic = owner;
            c.db = db;
            c.assets = CombatAssets.Load();
            c.salvoMs = Random.Range(3000f, 15000f);   // the first salvo, each ship its own (two spawned together fired in step)
            host.Target.Damaged += c.OnDamaged;
            host.Target.Died += c.OnDied;
            // Every enhanced host carries a crate (the Terran battleship had only a freighter's roll).
            host.SetLoot(CapitalShips.RollLoot(db, out c.gearLoot));
            if (CapitalShips.HasDeck(host.Spec))
                foreach (var p in SpacePoints.Set(CapitalShips.PadSet(host.Spec)))
                    if (p.type == SpacePoints.Approach) c.pads.Add(SpacePoints.ToLocal(p.engine));
            return c;
        }

        void OnDestroy()
        {
            if (ship != null && bySpec.TryGetValue(ship.Spec, out var c) && c == this) bySpec.Remove(ship.Spec);
            if (ship == null || ship.Target == null) return;
            ship.Target.Damaged -= OnDamaged;
            ship.Target.Died -= OnDied;
        }

        void OnDamaged(Target t, int dmg, bool byNpc)
        {
            if (byNpc) { sinceAttackMs = 0f; return; }   // an NPC (or, in multiplayer, another player: NpcShip.OnRemoteHit)
            playerDamageTotal += dmg;
            if (provoked) { sinceAttackMs = 0f; return; }
            if (ship.Target.hostileToPlayer) { Provoke(); return; }
            playerHits++;
            playerDamage += dmg;
            if (!warned) { warned = true; traffic.FriendTurnedEnemy(ship.Race); }   // "Hold your fire!"
            if (playerHits >= 3 || playerDamage >= Mathf.Max(50, ship.Hp.maxHull / 200)) Provoke();
        }

        void Provoke()
        {
            provoked = true;
            sinceAttackMs = 0f;
            ship.turnedEnemy = true;
            traffic.AlarmAllFriends(ship.Race, true);
        }

        void Update()
        {
            if (ship == null) return;
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            if (ship.Current == NpcShip.State.Dying) { UpdateDeath(dtMs); return; }
            if (ship.Gone || !ship.Target.Alive || ship.Current != NpcShip.State.Fly) return;
            if (sinceAttackMs < float.MaxValue) sinceAttackMs += dtMs;
            if (ship.Spec.fleetBattle) UpdateBattle(dtMs);
            UpdateMissiles(dtMs);
            if (!CapitalShips.HasDeck(ship.Spec) || launched >= CapitalShips.CarrierInflicts || pads.Count == 0) return;
            if (sinceAttackMs >= CapitalShips.UnderAttackMs) return;
            waveMs -= dtMs;
            if (waveMs > 0f) return;
            waveMs = CapitalShips.WaveMs;
            Launch();
        }

        /// <summary>One wave of Inflicts (the carrier) / H'Socs (the Vossk battleship) off the pads, climbing out along its heading.</summary>
        void Launch()
        {
            int n = Mathf.Min(CapitalShips.InflictsPerWave, CapitalShips.CarrierInflicts - launched);
            bool hostile = ship.alwaysEnemy || ship.turnedEnemy;
            var up = ship.transform.up;
            var dir = (ship.transform.forward + up * 0.35f).normalized;
            for (int i = 0; i < n; i++)
            {
                var at = ship.transform.TransformPoint(pads[(launched + i) % pads.Count]) + up * (i / pads.Count) * 1000f * M;
                var spec = new SpawnSpec
                {
                    group = NpcGroup.Escort, race = ship.Race, ship = CapitalShips.LaunchShip(ship.Spec), position = new Vector3(at.x, at.y, -at.z) / M,
                    alwaysEnemy = hostile, noLoot = true, capitalPart = true, capitalHost = ship.Spec, capitalEnhanced = true,
                    // With nothing to fight they loop round their ship like its escorts (the default patrol box was in front
                    // of the station, out of the battle).
                    route = CapitalShips.LoopAround(ship.Spec, new Vector3(ship.transform.position.x, ship.transform.position.y, -ship.transform.position.z) / M, launched + i),
                    routeAround = ship.Spec,
                };
                var s = traffic.SpawnShip(spec);
                s.Place(at, dir);
                foreach (var a in ship.aggressors) s.aggressors.Add(a);
            }
            launched += n;
            traffic.ConnectPlayers();
            if (hostile)
                traffic.Warn(ship.Spec.capital == SpawnSpec.CapitalVossk
                    ? Localization.Extra("vosskLaunch", "The Vossk battleship is launching its H'Socs!")
                    : Localization.Extra("carrierLaunch", "The carrier is launching its Inflicts!"));
        }

        /// <summary>The fleet battle: its turrets ride along, it sails beside the enemy capital ship, the opening call.</summary>
        void UpdateBattle(float dtMs)
        {
            foe ??= traffic.Ships.Find(s => s.Spec == ship.Spec.battleFoe);
            if (!partsRide)
            {
                partsRide = true;
                foreach (var s in traffic.Ships) if (s.Spec.capitalHost == ship.Spec && s.IsTurret) s.transform.SetParent(ship.transform, true);
                StartManoeuvres();
            }
            battleMs += dtMs;
            if (!battleCalled && battleMs >= CapitalShips.BattleRadioMs && ship.Race == traffic.SystemRace)
            {
                battleCalled = true;
                traffic.RaceRadio(ship.Race == 1
                    ? Localization.Extra("battleCallVossk", "A Terran warship in Vossk space! All ships, destroy it!")
                    : Localization.Extra("battleCallTerran", "All Terran ships: the Vossk battleship is in range. Bring it down!"), ship.Race);
            }
            Manoeuvre(dtMs);
            ShiftRoutes(dtMs);
        }

        // ---- the fleet battle's manoeuvres -------------------------------------------------------------------------

        /// <summary>The battle's shared course (one object for both capital ships): a point sailing at BattleSpeed along a
        /// heading that curves gently left and right (an S every 2 min), turned back toward where the battle began past
        /// BattleHomeUnits and away from the station (the orbit's origin) within 100 000 units; Unity metres / degrees.</summary>
        class BattleCourse
        {
            public Vector3 point, origin;
            public float heading, turnRate, phase, age;
            public bool stopped;
            int frame = -1;

            public Vector3 Forward => Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            public Vector3 Right => Quaternion.Euler(0f, heading, 0f) * Vector3.right;

            public void Step(float dtMs)
            {
                if (frame == Time.frameCount || stopped) return;
                frame = Time.frameCount;
                float dt = dtMs / 1000f, max = CapitalShips.BattleCourseTurnDeg;
                age += dt;
                float want = max * Mathf.Sin(age * 2f * Mathf.PI / 120f + phase);
                var away = point; away.y = 0f;   // the station is at the origin
                var home = origin - point; home.y = 0f;
                if (away.magnitude < 100000f * M) want = Toward(Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg, max * 1.5f);
                else if (home.magnitude > CapitalShips.BattleHomeUnits * M) want = Toward(Mathf.Atan2(home.x, home.z) * Mathf.Rad2Deg, max * 1.5f);
                turnRate = Mathf.MoveTowards(turnRate, want, 0.15f * dt);
                heading += turnRate * dt;
                point += Forward * (CapitalShips.BattleSpeed * dtMs * M);
            }

            float Toward(float yaw, float max) => Mathf.Clamp(Mathf.DeltaAngle(heading, yaw) * 0.1f, -max, max);
        }

        BattleCourse course;
        float side = 1f, weavePhase, climbPhase, yaw, pitch, roll, yawRate, speedScale = 1f;
        Vector3 routeFrom;
        float routeMs;
        readonly HashSet<Route> shiftedRoutes = new HashSet<Route>();

        void StartManoeuvres()
        {
            var obstacle = GetComponent<Obstacle>();
            if (obstacle != null) { obstacle.rotates = true; obstacle.baseRotation = transform.rotation; }
            yaw = transform.rotation.eulerAngles.y;
            course = foe != null ? foe.GetComponent<CapitalShip>()?.course : null;
            if (course == null)
            {
                var mid = foe != null ? (transform.position + foe.transform.position) / 2f : transform.position;
                course = new BattleCourse { point = mid, origin = mid, heading = yaw, phase = Random.Range(0f, 2f * Mathf.PI) };
            }
            side = Vector3.Dot(transform.position - course.point, course.Right) < 0f ? -1f : 1f;
            weavePhase = Random.Range(0f, 2f * Mathf.PI);
            climbPhase = Random.Range(0f, 2f * Mathf.PI);
            routeFrom = transform.position;
        }

        /// <summary>Steers toward a point BattleLeadUnits ahead in its lane: a limited, eased turn rate, banked into the turn,
        /// a gentle pitch, the speed trimmed to stay abreast; once the enemy is gone it levels out and coasts to a stop.</summary>
        void Manoeuvre(float dtMs)
        {
            if (course == null) return;
            bool fighting = InBattle;
            if (!fighting) course.stopped = true;
            course.Step(dtMs);
            float dt = dtMs / 1000f, turn = CapitalShips.BattleTurnDeg;
            speedScale = Mathf.MoveTowards(speedScale, fighting ? 1f : 0f, dt / 12f);
            float t = battleMs / 1000f;
            float lane = (CapitalShips.BattleLaneUnits + CapitalShips.BattleWeaveUnits * Mathf.Sin(t * 2f * Mathf.PI / 75f + weavePhase)) * M;
            float climb = CapitalShips.BattleClimbUnits * Mathf.Sin(t * 2f * Mathf.PI / 95f + climbPhase) * M;
            var laneNow = course.point + course.Right * (side * lane) + Vector3.up * climb;
            float along = Vector3.Dot(transform.position - laneNow, course.Forward);
            float lead = CapitalShips.BattleLeadUnits * M;
            float speed = CapitalShips.BattleSpeed * Mathf.Clamp(1f - along / lead, 0.6f, 1.5f) * speedScale;
            var want = laneNow + course.Forward * lead - transform.position;
            // A gentle gain against the eased turn rate: it settles into a turn instead of swinging across its lane.
            float wantRate = speedScale > 0.05f ? Mathf.Clamp(Mathf.DeltaAngle(yaw, Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg) * 0.07f, -turn, turn) : 0f;
            yawRate = Mathf.MoveTowards(yawRate, wantRate * speedScale, turn * 0.25f * dt);
            yaw += yawRate * dt;
            float wantPitch = speedScale > 0.05f
                ? Mathf.Clamp(-Mathf.Atan2(want.y, new Vector2(want.x, want.z).magnitude) * Mathf.Rad2Deg, -CapitalShips.BattlePitchDeg, CapitalShips.BattlePitchDeg) : 0f;
            pitch = Mathf.MoveTowards(pitch, wantPitch * 0.6f, 0.5f * dt);
            roll = Mathf.MoveTowards(roll, -Mathf.Clamp(yawRate / turn, -1f, 1f) * CapitalShips.BattleBankDeg, 2.5f * dt);   // right turn: right wing down
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
            transform.position += transform.forward * (speed * dtMs * M);
        }

        /// <summary>Every 2 s the loops circling it (its escorts, the enemy wing: SpawnSpec.routeAround) move with it.</summary>
        void ShiftRoutes(float dtMs)
        {
            routeMs += dtMs;
            if (routeMs < 2000f) return;
            routeMs = 0f;
            var d = transform.position - routeFrom;
            if (d.sqrMagnitude < 1f) return;
            routeFrom = transform.position;
            var game = new Vector3(d.x, d.y, -d.z) / M;
            shiftedRoutes.Clear();
            foreach (var s in traffic.Ships)
            {
                if (s.Spec.routeAround != ship.Spec) continue;
                var r = s.Spec.route;
                if (r != null && shiftedRoutes.Add(r)) for (int i = 0; i < r.points.Count; i++) r.points[i] += game;   // a relaunch's
                s.ShiftRoute(game);
            }
        }

        // ---- missiles ------------------------------------------------------------------------------------------

        Gun missiles;
        GunRig missileRig;
        WeaponFx missileFx;
        bool missilesTried;
        readonly List<Vector3> launchers = new List<Vector3>();   // ship-local Unity
        Target missileTarget;
        float salvoMs, salvoGapMs;
        int salvoLeft, launcherIndex, missileBase;

        void SetupMissiles()
        {
            missilesTried = true;
            var item = db != null ? db.Item(ship.Race == 1 ? CapitalShips.VosskMissile : CapitalShips.TerranMissile) : null;
            if (item == null) return;
            // The race's own missile: its speed (attr 13) and its damage (attr 9) against the Intelli Jet's 100.
            missileBase = Mathf.Max(1, Mathf.RoundToInt(NpcTables.GunDamage(ship.Spec, false, true, out _) * item.Attr(9, 100) / 100f));
            float speed = item.Attr(13, 0) > 0 ? item.Attr(13, 0) : CapitalShips.MissileSpeed;
            missiles = new Gun(item, missileBase, CapitalShips.MissileGapMs, CapitalShips.MissilesPerSalvo, CapitalShips.MissileLifetimeMs, speed)
            {
                owner = ship.Target, homingDelayMs = 1000f,
            };
            missileFx = WeaponFx.Load(item.index);
            var root = new GameObject("Capital ship missiles").transform;
            root.SetParent(traffic.transform, false);
            missileRig = new GunRig(missiles, missileFx, root, null, 2);
            missileRig.EnableTrails();   // RocketGun::setRadar's smoke, the player's (NPC rockets have none in the original)
            missiles.Hit += OnMissileHit;
            ship.ExtraGuns.Add(missiles);   // multiplayer: the other players see them (NetProxy's shot sender)
            // Launch from the turret mounts (a vertical launcher beside each), else from the top of the hull.
            foreach (var s in traffic.Ships)
                if (s.Spec.capitalHost == ship.Spec && s.IsTurret)
                    launchers.Add(ship.transform.InverseTransformPoint(s.transform.position) + Vector3.up * 400f * M);
            if (launchers.Count == 0) launchers.Add(Vector3.up * 3000f * M);
        }

        void OnMissileHit(int b, Target hit, Vector3 point)
        {
            bool big = hit.boxes != null && hit.boxes.Length > 0;
            float dmg = missileBase * (big ? CapitalShips.MissileCapitalFactor : CapitalShips.MissileFactor);
            if (hit.isPlayer && !ship.Target.hostileToPlayer) dmg *= 0.2f;   // a stray hit (NpcShip.GunHit)
            else if (!hit.isPlayer && IsRemotePlayer(hit) && !NpcShip.HostileToRemote(ship, hit)) dmg *= 0.2f;   // multiplayer: the same
            hit.Damage(dmg, true, missiles.bullets[b].velocity);
            missileRig.ShowImpact(point);
        }

        void UpdateMissiles(float dtMs)
        {
            if (!missilesTried) SetupMissiles();
            if (missiles == null) return;
            var lockTarget = missileTarget != null && missileTarget.Alive && !missileTarget.cloaked ? missileTarget : null;
            missiles.Update(dtMs, ship.HitTargets, lockTarget);   // a boost shakes off the missiles in flight (Gun, Target.boosting)
            missileRig.UpdateVisuals(dtMs, Camera.main, ship.transform.up);
            // ...and calls off the rest of the salvo at that ship.
            if (lockTarget != null && lockTarget.boosting) { missileTarget = lockTarget = null; salvoLeft = 0; }
            if (salvoLeft > 0)
            {
                salvoGapMs -= dtMs;
                if (salvoGapMs > 0f) return;
                salvoGapMs = CapitalShips.MissileGapMs;
                if (lockTarget == null) { salvoLeft = 0; return; }
                // A fleet battle's salvo also goes for the enemy's fighters around it, a missile in three.
                var fighter = lockTarget == foe?.Target && Random.value < CapitalShips.BattleFighterShare ? PickFighter() : null;
                if (FireMissile(lockTarget, fighter)) salvoLeft--;
                else salvoGapMs = 0f;   // the launcher not reloaded yet (the gap and the reload tie): again next frame
                return;
            }
            salvoMs -= dtMs;
            if (salvoMs > 0f) return;
            salvoMs = CapitalShips.SalvoMs + Random.Range(0f, CapitalShips.SalvoJitterMs);
            missileTarget = PickMissileTarget();
            if (missileTarget != null) { salvoLeft = CapitalShips.MissilesPerSalvo; salvoGapMs = 0f; }
        }


        /// <summary>A hostile fighter within MissileRangeUnits of its hull, at random (the escorts, wings and launched ships
        /// of the other side); null = none.</summary>
        Target PickFighter()
        {
            fighters.Clear();
            float reach = CapitalShips.MissileRangeUnits * M;
            foreach (var s in traffic.Ships)
            {
                if (s == null || s.IsTurret || s.Spec.capital != 0 || !s.Target.Alive || s.Current != NpcShip.State.Fly || s.Target.cloaked) continue;
                if (!Standing.RacesHostile(s.Race, ship.Race)) continue;
                if ((ship.Target.NearestPoint(s.transform.position) - s.transform.position).sqrMagnitude > reach * reach) continue;
                fighters.Add(s.Target);
            }
            return fighters.Count > 0 ? fighters[Random.Range(0, fighters.Count)] : null;
        }

        readonly List<Target> fighters = new List<Target>();

        /// <summary>One missile up out of a launcher, leaning toward the target (it homes after 1000 ms), or toward 'own' when
        /// given (its own target, Gun.SetTarget); false = not fired.</summary>
        bool FireMissile(Target target, Target own = null)
        {
            if (own != null) target = own;
            var from = ship.transform.TransformPoint(launchers[launcherIndex % launchers.Count]);
            var toward = (target.transform.position - from).normalized;
            var dir = (ship.transform.up * 0.35f + toward).normalized;   // a short climb out of the launcher, then homing
            int b = missiles.TryFire(from, Quaternion.LookRotation(dir, ship.transform.forward), false);
            if (b < 0) return false;
            if (own != null) missiles.SetTarget(b, own);
            else if (CapitalShips.SpreadAim(target, traffic.Ships, out var aim)) missiles.SetAim(b, aim);   // its own spot of a big hull
            launcherIndex++;
            missileRig.OnShot();
            Sfx.PlayAt(missileFx != null ? missileFx.Shot : null, from, 0.8f);
            return true;
        }

        /// <summary>The fleet battle's enemy capital ship while it stands, else the nearest hostile ship within reach of its hull
        /// (the player only while hostile to it).</summary>
        Target PickMissileTarget()
        {
            if (foe != null && foe.Target.Alive && foe.Current == NpcShip.State.Fly && !foe.Gone)
                return foe.Target;
            Target best = null;
            float bestD = CapitalShips.MissileRangeUnits * M;
            foreach (var e in ship.enemies)
            {
                if (e == null || !e.Targetable || e.cloaked) continue;
                bool hostile = e.isPlayer ? ship.Target.hostileToPlayer : e.isShip && e.race >= 0 && Standing.RacesHostile(e.race, ship.Race);
                if (!hostile) continue;
                float d = (ship.Target.NearestPoint(e.transform.position) - e.transform.position).magnitude;
                if (d < bestD) { bestD = d; best = e; }
            }
            // Multiplayer: the other players here it is hostile to (their game takes the hits, NpcShip.HostileToRemote).
            var remote = NpcShip.RemotePlayers;
            if (remote != null && NpcShip.HostileToRemote != null)
                foreach (var r in remote)
                {
                    if (r == null || !r.Alive || !r.isActiveAndEnabled || r.cloaked || !NpcShip.HostileToRemote(ship, r)) continue;
                    float d = (ship.Target.NearestPoint(r.transform.position) - r.transform.position).magnitude;
                    if (d < bestD) { bestD = d; best = r; }
                }
            return best;
        }

        static bool IsRemotePlayer(Target t)
        {
            var remote = NpcShip.RemotePlayers;
            if (remote == null) return false;
            foreach (var r in remote) if (r == t) return true;
            return false;
        }

        void OnDied(Target t)
        {
            blastMs = 0f;
            missileRig?.HideAll();
            missiles?.RemoveAll();
            if (ship.Spec.fleetBattle) BattleOver(t);
            if (CapitalShips.Killable(ship.Spec)) traffic.DestroyCapitalTurrets(ship.Spec);
            if (t.killedByNpc) return;
            if (CapitalShips.Killable(ship.Spec))
            {
                Standing.ApplyDelict(ship.Race, 20);
                if (ship.Spec.capital == SpawnSpec.CapitalVossk) Achievements.Progress(39, ++Session.BattleshipsDestroyed);   // a battleship (1667) too
            }
            if (gearLoot) Session.CapitalLootReadyAt = Session.PlaySeconds + CapitalShips.LootCooldownSeconds;
        }

        /// <summary>A fleet battle's capital ship died: the winners call it and pay a player who helped. Not when the enemy
        /// went first (both destroyed: a player's report 2026-10-16, the dead side called its victory too).</summary>
        void BattleOver(Target t)
        {
            var winner = ship.Spec.battleFoe;
            if (winner == null) return;
            var standing = foe ?? traffic.Ships.Find(s => s.Spec == winner);
            if (!WinnerStands(standing)) return;
            traffic.RaceRadio(winner.race == 1
                ? Localization.Extra("battleWonVossk", "The Terran warship is breaking up. Victory to the Vossk!")
                : Localization.Extra("battleWonTerran", "The Vossk battleship is going down! Well fought, pilots."), winner.race);
            var winnerShip = standing;
            bool helped = !t.killedByNpc || playerDamageTotal >= ship.Hp.maxHull / 20;
            bool winnersHostile = Standing.IsEnemy(winner.race) || (winnerShip != null && (winnerShip.turnedEnemy || winnerShip.alwaysEnemy));
            if (helped && !winnersHostile) traffic.PayBounty(CapitalShips.BattleBounty);
        }

        /// <summary>The fleet battle's other capital ship is still in it (alive, not dying or gone).</summary>
        public static bool WinnerStands(NpcShip winner) => winner != null && !winner.Gone && winner.Target != null && winner.Target.Alive
                                                            && winner.Current == NpcShip.State.Fly;

        /// <summary>The carrier / Vossk battleship breaking up: an explosion somewhere on its hull every 0.4-0.9 s.</summary>
        void UpdateDeath(float dtMs)
        {
            if (!CapitalShips.Killable(ship.Spec)) return;
            blastMs -= dtMs;
            if (blastMs > 0f) return;
            blastMs = Random.Range(400f, 900f);
            var boxes = ship.Target.boxes;
            if (boxes == null || boxes.Length == 0) return;
            var b = boxes[Random.Range(0, boxes.Length)];
            var local = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
            Explosion.Spawn(0, ship.transform.TransformPoint(local), Random.onUnitSphere, Random.Range(3f, 6f),
                            assets != null ? CombatAssets.Pick(assets.explosionMid) : null, true);
        }
    }
}
