// NpcShipControls.cs
// Part of NpcShip: the level-script controls (place, move, route, hull, race, speed, guns, only enemy, deactivate)
// and wingman mode (formation, commands).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class NpcShip
    {
        /// <summary>Places the ship (Unity world position) facing 'forward'.</summary>
        public void Place(Vector3 position, Vector3 forward)
        {
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        // ---- level-script controls (LevelScript, Level::createCampaignMission) ------------------------------------

        /// <summary>PlayerFixedObject::setMoving: a freighter flies game +Z at 1 u/ms, or stays parked (still a target).</summary>
        public void SetMoving(bool on) => parked = !on;

        /// <summary>Remake (CapitalShip): its route moved by 'gameDelta' (game units), an escort's loop following its capital
        /// ship through a fleet battle.</summary>
        public void ShiftRoute(Vector3 gameDelta)
        {
            if (route == null || route == Spec.route) return;
            for (int i = 0; i < route.points.Count; i++) route.points[i] += gameDelta;
        }

        /// <summary>KIPlayer::setRoute: fly this route (game units) when there is nothing to attack.</summary>
        public void SetRoute(Route r)
        {
            route = r != null ? r.Clone() : Route.DefaultPatrol(Race);
            attacking = false;
            targetIdx = -1;
        }

        /// <summary>Multiplayer: the waypoint of its route it flies to (NetProxy's status, for a handover).</summary>
        public int RouteIndex => route != null ? route.index : 0;

        /// <summary>Multiplayer (NetOrbit.AdoptHandedOver): a ship another game ran, rebuilt here as it was there: its speed
        /// (u/ms), waypoint, sleep, whether it turned on the players, and an EMP's disabling with the recovery time it had run.</summary>
        public void RestoreState(float unitsPerMs, int routeIndex, bool asleep, bool stillInactive, bool turned, bool empOff, float empElapsedMs)
        {
            if (!float.IsNaN(unitsPerMs) && unitsPerMs > 0f) speed = Mathf.Min(unitsPerMs, Mathf.Max(baseSpeed, 1f) * 3f);
            if (route != null) route.index = Mathf.Clamp(routeIndex, 0, route.points.Count);
            if (!asleep && !stillInactive && (Asleep || inactive)) Wake();
            if (turned) turnedEnemy = true;
            if (empOff && Hp != null) Hp.RestoreEmpDisabled(empElapsedMs);
        }

        /// <summary>Player::setMaxHitpoints + setHitpoints.</summary>
        public void SetHull(int hull, bool alsoMax = true)
        {
            if (alsoMax) Hp.maxHull = Mathf.Max(1, hull);
            Hp.hull = Mathf.Clamp(hull, 1, Hp.maxHull);
            Target.hp = Hp.hull;
            Target.maxHp = Hp.maxHull;
            lastHull = Hp.hull;
        }

        /// <summary>KIPlayer::setRace (index 40: Errkt's freighter becomes Vossk).</summary>
        public void SetRace(int race)
        {
            Spec.race = race;
            Target.race = race;
        }

        /// <summary>setSpeed: a fixed speed (no boosts), u/ms.</summary>
        public void SetSpeed(float unitsPerMs)
        {
            Spec.speed = unitsPerMs;
            speed = baseSpeed = unitsPerMs;
        }

        /// <summary>Level::assignGuns' per-mission guns (index 70: every ship the Disruptor Laser 183 at x2.5 damage).</summary>
        public void SetGun(int itemIndex, float damageFactor)
        {
            var item = db.Item(itemIndex);
            if (item == null || IsFreighter || IsFixed) return;
            rig?.HideAll();
            if (gun != null) { gun.Hit -= OnGunHit; gun.AreaHit -= HitByBlast; }
            gun = MakeGun(item, gunBase * damageFactor);
            rig = new GunRig(gun, WeaponFx.Load(item.index), fxRootRef, IsTurret ? turretCore?.GunNode : null, 2);
            WireGun(gun);
            turretCore?.SetGun(gun, rig);
        }

        /// <summary>Level::assignGuns' second slot: a Wanted flying ship's G'liissk, Harval's Shesha (index 157 / 158).</summary>
        public void SetSecondaryGun(int itemIndex, float damageFactor)
        {
            var item = db.Item(itemIndex);
            if (item == null || IsFreighter || IsFixed || IsTurret) return;
            secondRig?.HideAll();
            secondGun = MakeGun(item, gunBase * damageFactor);
            secondRig = new GunRig(secondGun, WeaponFx.Load(item.index), fxRootRef, null, 2);
            var g = secondGun;
            var r = secondRig;
            secondGun.Hit += (b, hit, point) =>
            {
                GunHit(g, r, b, hit, point);
            };
            useSecond = false;
            slotMs = 0f;
        }

        /// <summary>Level::assignGuns: rockets / missiles (sorts 4, 5, 40) become a RocketGun (speed 8, lifetime 10 000, reload
        /// 3000, homing for 5 / 40 after 1000 ms); every other item only gives the look of the NPC gun (16 u/ms, 3000 ms).</summary>
        Gun MakeGun(ItemData item, float damage)
        {
            bool rocket = item.categoryId == (int)Gun.Kind.Rocket || item.categoryId == (int)Gun.Kind.Missile || item.categoryId == (int)Gun.Kind.ClusterMissile;
            if (!rocket)
                return new Gun(item, damage, NpcTables.GunReloadMs, NpcTables.GunPool, NpcTables.GunLifetimeMs, gunSpeed) { owner = Target, emp = item.Attr(10) };
            int pool = item.categoryId == (int)Gun.Kind.ClusterMissile ? Mathf.Max(1, item.index - 211) : NpcTables.GunPool;
            return new Gun(item, damage, RocketReloadMs, pool, RocketLifetimeMs, RocketSpeed) { owner = Target, homingDelayMs = 1000f };
        }

        /// <summary>Only this target (the level script aims ships at Errkt's freighter / at the player).</summary>
        public void SetOnlyEnemy(Target t)
        {
            ScriptEnemy = t;
            enemies.Clear();
            if (t != null) enemies.Add(t);
            attacking = false;
            targetIdx = -1;
            reselectTimer = 0f;
        }

        /// <summary>Player::setAlwaysEnemy 0xaf206: always hostile (+0xec) and turned on the player (+0xe0) as well, so a
        /// fighter of any race but the pirates' takes the player at any range while it isn't attacking (TakesAtAnyRange). A
        /// SpawnSpec's alwaysEnemy sets only the first; a level script's setAlwaysEnemy call comes here.</summary>
        public void SetAlwaysEnemy()
        {
            alwaysEnemy = true;
            turnedEnemy = true;
        }

        /// <summary>KIPlayer::setActive(false) + parked far away (index 40: the freighter after the wormhole).</summary>
        public void Deactivate()
        {
            Asleep = inactive = true;
            Target.untargetable = true;
            if (engine != null) engine.Stop();
        }

        // ---- wingman ------------------------------------------------------------------------------------------

        /// <summary>KIPlayer::setWingman(true, slot) + the createWingmen setup; 'armed' false in a Challenge.</summary>
        /// <summary>Remake mods (NpcDevices, after GoF3's boss Mind Control: "Can cause wingmen to attack their leader ...
        /// Shooting them can get them to regain their senses"): it turns on the player for 'ms', until the player hits it.</summary>
        public void MindControl(float ms)
        {
            if (Current != State.Fly || !Target.Alive) return;
            mindControlMs = Mathf.Max(mindControlMs, ms);
        }

        public bool MindControlled => mindControlMs > 0f;

        /// <summary>Under mind control: the player its target, until the time is up.</summary>
        void UpdateMindControlled(float dtMs)
        {
            mindControlMs = Mathf.Max(0f, mindControlMs - dtMs);
            var player = traffic != null ? traffic.Player : null;
            if (player == null || !player.Alive) { mindControlMs = 0f; return; }
            target = player;
            targetPos = player.transform.position;
            attacking = true;
            followingWaypoint = false;
        }

        public void MakeWingman(int slot, bool armed)
        {
            IsWingman = true;
            Target.wingman = true;   // remake (#216): the HUD's blue wingman markers
            wingSlot = slot;
            wingCommand = 1;
            alwaysFriend = true;
            // Event 48 Spaceship_Engine_Wingmen: the enemy engine's waves at event volume 0.0437 (46: 0.0759).
            if (engine != null) EngineVoices.Register(engine, 48, 0.043652f * Sfx.EventGain);
            // Remake mods: its hull's device and its crew's lines (ships.json "wingmanDevice", wingmen.json)
            var kind = WingmanDevice.Parse(CustomShips.Get(Spec.ship)?.wingmanDevice);
            wingDevice = kind != WingmanDevice.Kind.None ? new WingmanDevice(kind) : null;
            wingCrew = Modding.ModWingmen.Of(db, Spec.ship);
            wingFace = slot == 0 && Session.WingmanPortrait != null && Session.WingmanPortrait.Length == 5 ? Session.WingmanPortrait
                     : AgentGenerator.CreatePortrait(true, Race == 0 || Race == 2 || Race == 3 ? Race : 1);
            // Level::assignGuns: a wingman's laser skips the other ships' per-mission factors.
            gunBase = NpcTables.GunDamage(Spec, true, false, out gunSpeed);
            if (gun != null) gun.damage = gunBase;
            if (!armed)
            {
                rig?.HideAll();
                gun = null;
                return;
            }
            // Level::assignGuns: slot 1 = Dia EMP Mk III, damage 0, 4 bullets, reload 400, 3000 ms, speed 16.
            var emp = db.Item(18);
            if (emp == null) return;
            empGun = new Gun(emp, 0f, 400f, 4, 3000f, 16f) { owner = Target, emp = emp.Stat("empDamage", 8) };
            empRig = new GunRig(empGun, WeaponFx.Load(18), fxRootRef, null, 2);
            empGun.Hit += (b, hit, point) => { EmpBreaksCloak(hit); if (hit.hitpoints != null && !hit.isPlayer) hit.hitpoints.DamageEmp((int)empGun.emp); empRig.ShowImpact(point); };
        }

        /// <summary>PlayerFighter::setWingmanCommand 0xf096c: 1 fire at will (boosts off for good), 2 secure the next
        /// waypoint (a clone of the player's route), 3 attack the locked ship; 0 toggles laser / EMP blaster.</summary>
        public void WingmanCommand(int command, Target locked, Route playerRoute)
        {
            if (!IsWingman || Current != State.Fly) return;
            switch (command)
            {
                case 0: useEmp = !useEmp; return;
                case 1: evasionOff = true; boosting = panic = false; break;
                case 2:
                    if (playerRoute == null || playerRoute.Waypoint == null) return;
                    scout = playerRoute.Clone();
                    scout.index = playerRoute.index;
                    scoutStart = playerRoute.index;
                    reselectTimer = 5001f;
                    break;
                case 3:
                    if (locked == null || !locked.Alive) return;   // no lock: ignored
                    wingTarget = locked;
                    reselectTimer = 5001f;
                    break;
            }
            wingCommand = command;
            speed = baseSpeed;
        }

        /// <summary>Remake mods: one of its crew's radio lines (Data.WingmanCrew's kinds), null crew or none of that kind = silent.</summary>
        public void WingmanSays(string kind, float chance = 1f)
        {
            if (!IsWingman || wingCrew == null || traffic == null || !Target.Alive && kind != WingmanCrew.Lost) return;
            traffic.WingmanSay(wingCrew.Line(kind, n => Random.Range(0, n)), Target.displayName, wingFace, chance);
        }

        /// <summary>Remake mods: its device (Flight.WingmanDevice: Hull Repair mends the player, Ammo Supply restocks the
        /// player's secondary, Charged Shots its own guns, GunHit) and its calls for help and about the player.</summary>
        void UpdateWingmanCrew(float dtMs)
        {
            if (Hp.hull >= Hp.maxHull * 0.5f) wingHelpSaid = false;
            else if (!wingHelpSaid && Hp.hull < Hp.maxHull / 3f) { wingHelpSaid = true; WingmanSays(WingmanCrew.Help); }
            var player = traffic.Player;
            var php = player != null && player.Alive ? player.hitpoints : null;
            int pool = php != null ? Mathf.Max(1, php.maxHull + php.maxArmor) : 1;
            float share = php != null ? (php.hull + php.armor) / (float)pool : 0f;
            if (wingCrew != null && php != null && traffic.TakePlayerDown(share)) WingmanSays(WingmanCrew.PlayerDown);
            else if (wingCrew != null && wingCrew.lines.ContainsKey(WingmanCrew.Warn) && traffic.TakeEnemyOnTail()) WingmanSays(WingmanCrew.Warn);
            if (wingDevice == null) return;
            int ammo = traffic.PlayerSecondaryAmmo != null ? traffic.PlayerSecondaryAmmo() : -1;
            bool wanted = php != null && wingDevice.Wanted(share, ammo, attacking && target != null);
            bool started = wingDevice.Step(dtMs, wanted, out float repairShare);
            if (repairShare > 0f && php != null)
            {
                // the hull first, then the armor (GoF3 mends its one hull)
                int add = Mathf.CeilToInt(repairShare * pool);
                int toHull = Mathf.Min(add, php.maxHull - php.hull);
                php.hull += toHull;
                php.armor = Mathf.Min(php.maxArmor, php.armor + add - toHull);
            }
            if (!started) return;
            if (wingDevice.kind == WingmanDevice.Kind.Ammo) traffic.RestockPlayer?.Invoke(WingmanDevice.AmmoRounds);
            string what = wingDevice.kind == WingmanDevice.Kind.Repair ? Localization.Extra("wingmanRepair", "{0}: Hull Repair")
                        : wingDevice.kind == WingmanDevice.Kind.Ammo ? Localization.Extra("wingmanAmmo", "{0}: Ammo Supply")
                        : Localization.Extra("wingmanCharged", "{0}: Charged Shots");
            traffic.Warn(string.Format(what, Target.displayName));
        }

        /// <summary>The wingman branches of the target selection and the formation route.</summary>
        void UpdateWingman()
        {
            target = null;
            attacking = false;
            followingWaypoint = false;
            var player = traffic.Player != null ? traffic.Player.transform : null;
            if (wingCommand == 3)
            {
                if (wingTarget == null || !wingTarget.Alive) { wingCommand = 1; wingTarget = null; }
                else if (InBox(wingTarget)) { target = wingTarget; attacking = true; }
            }
            if (wingCommand == 2 && scout != null)
            {
                scout.Update(ToGame(transform.position));
                var wp = scout.Waypoint;
                if (wp.HasValue && scout.index <= scoutStart) { targetPos = ToUnity(wp.Value); followingWaypoint = true; return; }
                scout = null;
                wingCommand = 1;
            }
            if (!attacking && wingCommand != 3 && gun != null)
                foreach (var s in traffic.Ships)
                {
                    if (s == this || s.Gone || s.IsWingman || s.Current != State.Fly || s.Hidden) continue;
                    if (s.Target.Alive && s.Target.hostileToPlayer) { target = s.Target; attacking = true; break; }
                }
            // Multiplayer: another game's hostile ship here (the orbit's authority runs them).
            if (!attacking && wingCommand != 3 && gun != null && GoF2Remake.Data.NetSession.Active)
                foreach (var t in Target.NetShips)
                    if (HostileOther(t) && InBox(t)) { target = t; attacking = true; break; }
            if (target != null) { targetPos = target.transform.position; return; }
            if (player == null) { targetPos = transform.position + transform.forward; return; }
            // Formation point (per frame a one-point route).
            Vector3 f = player.forward, r = player.right, u = player.up;
            Vector3 off = wingSlot == 0 ? -r * 4000f - f * 3000f : wingSlot == 1 ? r * 4000f - f * 3000f : u * 2000f - f * 2000f;
            targetPos = player.position + off * M;
            followingWaypoint = true;
        }
    }
}
