// PlayerDevicesEffects.cs
// PlayerDevices, part: what each device does when fired (Flight.Device.Type), its shots, wave, beams and deflected shots.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public partial class PlayerDevices
    {
        const float M = Gun.MetersPerUnit;
        /// <summary>Explosion type 11 (the shock blast) at scale 1 is the shock blast's reach, 80000 units.</summary>
        const float ShockScaleUnits = 80000f;
        /// <summary>The Dia EMP Mk III (item 18): the EMP shots' gun and their look when the device has no fx of its own.</summary>
        const int EmpLookItem = 18;
        const float ShotLifetimeMs = 5000f, ShotSpeedUnits = 11.4f, DeflectLifetimeMs = 4000f, BeamShowMs = 400f;

        /// <summary>A gun the devices fire from: an EMP device's shots, or the shots the deflector turns back (one per look).</summary>
        sealed class Shot
        {
            public Entry e;
            public Gun gun;
            public GunRig rig;
            public float[] damage, emp;   // per bullet (the deflected shots carry their shooter's)
        }

        sealed class Wave
        {
            public Entry e;
            public Vector3 centre;
            public float ms;
            public readonly HashSet<Target> reached = new HashSet<Target>();
        }

        void SetupEffects()
        {
            fxRoot = new GameObject("Player device fx").transform;
            deflect = Deflect;
            var empItem = db.Item(EmpLookItem);
            foreach (var e in devices)
            {
                if ((e.rules.type != Device.Type.EmpShot && e.rules.type != Device.Type.ShieldDisruptor) || empItem == null) continue;
                float speed = e.item.HasAttr(13) ? e.item.Attr(13) : ShotSpeedUnits;
                float life = e.item.HasAttr(12) ? e.item.Attr(12) : ShotLifetimeMs;
                var s = new Shot { e = e, gun = new Gun(empItem, 0f, 1f, 2, life, speed, true) { owner = self } };
                s.rig = new GunRig(s.gun, WeaponFx.Load(e.item.index) ?? WeaponFx.Load(EmpLookItem), fxRoot, null);
                s.gun.Hit += (b, t, p) => OnShotHit(s, b, t, p);
                shots[e] = s;
            }
        }

        void DestroyEffects()
        {
            if (tethered != null) Tether(null, null);
            if (self != null) self.Damaged -= OnSelfDamaged;
            if (fxRoot != null) Destroy(fxRoot.gameObject);
            foreach (var b in beamPool) if (b != null) Destroy(b.gameObject);
        }

        /// <summary>What must hold before 'e' fires: the overcharger needs the shield it spends; a repair something to repair, the
        /// restock a selected secondary, the charger a cooldown to end (EVERSPACE 2's CanBeUsed of each consumable).</summary>
        bool CanFire(Entry e)
        {
            var hp = self.hitpoints;
            switch (e.rules.type)
            {
                case Device.Type.OverchargeArea:
                case Device.Type.OverchargeBeam:
                {
                    int cost = hp != null ? Device.OverchargeCost(hp.maxShield, e.shieldCost) : 0;
                    if (cost > 0 && hp.shield >= cost) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoShield", "{0}: not enough shield"), e.name));
                    return false;
                }
                case Device.Type.HullRepair:
                    if (hp != null && hp.hull < hp.maxHull) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNothingToRepair", "{0}: nothing to repair"), e.name));
                    return false;
                case Device.Type.ArmorRepair:
                    if (hp != null && hp.maxArmor > 0 && hp.armor < hp.maxArmor) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNothingToRepair", "{0}: nothing to repair"), e.name));
                    return false;
                case Device.Type.Restock:
                    if (weapons != null && weapons.SelectedSecondary >= 0) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoSecondary", "{0}: no secondary weapon selected"), e.name));
                    return false;
                case Device.Type.DeviceCharger:
                    foreach (var o in devices) if (o != e && o.rules.State == Device.Phase.Cooldown) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoCooldowns", "{0}: no device is cooling down"), e.name));
                    return false;
                case Device.Type.Corrosion:
                case Device.Type.QuantumTether:
                case Device.Type.Entangler:
                    if (LockWithin(e) != null) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoTarget", "{0}: no ship locked in reach"), e.name));
                    return false;
                case Device.Type.Teleport:
                case Device.Type.SpectralShift:
                    if (BlinkDistance(e.radius * M) > 0f) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoRoom", "{0}: no room ahead"), e.name));
                    return false;
                case Device.Type.FusionHook:
                    if (HookTarget(e) != null) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoHookTarget", "{0}: nothing in the sights"), e.name));
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>The targeted devices' target: the lock, hostile and within deviceRadius.</summary>
        Target LockWithin(Entry e)
        {
            var t = weapons != null ? weapons.LockTarget : null;
            if (!Affects(t)) return null;
            float r = e.radius * M;
            return (t.transform.position - transform.position).sqrMagnitude <= r * r ? t : null;
        }

        /// <summary>The fusion hook's target: the lock if it is in the sights, else the nearest hostile ship there (within deviceRadius
        /// and ES2's aim cone, dot 0.95 with the nose).</summary>
        Target HookTarget(Entry e)
        {
            float r = e.radius * M;
            var pos = transform.position;
            bool InSights(Target t)
            {
                var d = t.transform.position - pos;
                return d.sqrMagnitude <= r * r && d.sqrMagnitude > 1e-4f && Vector3.Dot(transform.forward, d.normalized) > Device.HookAimDot;
            }
            var lockT = weapons != null ? weapons.LockTarget : null;
            if (Affects(lockT) && InSights(lockT)) return lockT;
            Target best = null;
            float bestD = float.MaxValue;
            foreach (var t in Target.All)
            {
                if (!Affects(t) || !InSights(t)) continue;
                float d = (t.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }

        /// <summary>The ship's probe for a blink: a third of its model's largest extent (PlayerCollision's), 3..60 m.</summary>
        float BlinkProbe()
        {
            var ship = GetComponent<ShipController>();
            var model = ship != null ? ship.visualModel : null;
            if (model == null) return 10f;
            var b = new Bounds(model.position, Vector3.zero);
            foreach (var mr in model.GetComponentsInChildren<MeshRenderer>()) b.Encapsulate(mr.bounds);
            return Mathf.Clamp(Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) / 3f, 3f, 60f);
        }

        /// <summary>How far ahead the ship may blink of 'want' metres: short of the first hull, landmark volume or asteroid on the
        /// way by ES2's SafetyMargin and the probe (Device.Blink); 0 or less = no room.</summary>
        float BlinkDistance(float want)
        {
            var from = transform.position;
            var dir = transform.forward;
            float probe = BlinkProbe();
            float hit = float.MaxValue;
            if (HullCollision.Any && HullCollision.Sweep(from, from + dir * want, probe, b => b != null && b.obstacle != null && b.obstacle.Active && b.Collides, out float hullHit))
                hit = Mathf.Min(hit, hullHit);
            // the landmarks' volumes (those without hulls): sampled along the way
            var all = Obstacle.All;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || !o.Active || o.UsesHull) continue;
                for (int k = 1; k <= 20; k++)
                {
                    float d = want * k / 20f;
                    if (d >= hit) break;
                    if (o.Touches(from + dir * d, out _)) { hit = Mathf.Min(hit, d); break; }
                }
            }
            // the asteroids' cubes as spheres of their radius
            var targets = Target.All;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.isAsteroid || !t.Alive) continue;
                var c = t.transform.position - from;
                float along = Vector3.Dot(c, dir);
                if (along < 0f || along - t.radius > want) continue;
                float side = (c - dir * along).magnitude;
                if (side > t.radius + probe) continue;
                hit = Mathf.Min(hit, along - t.radius);
            }
            return Device.Blink(want, hit, probe);
        }

        // the running devices' state: the corrosion's target and tick, the hook, the tether, the entangler's sum
        Target corroding; float corrosionTickMs; Entry corrosionEntry;
        Target hooked; Entry hookEntry; float hookMs, hookPullSpeed, hookTargetSpeed, hookBeamMs;
        Target tethered; Entry tetherEntry; bool sharing;
        Target entangled; Entry entanglerEntry; float entangledDamage;

        /// <summary>Remake: the points a repair device restores, 'percent' of the layer's maximum (rounded up, at least 1).</summary>
        public static int RepairPoints(int max, int percent) => max <= 0 ? 0 : Mathf.Max(1, Mathf.CeilToInt(max * Mathf.Clamp(percent, 0, 100) / 100f));

        // the sensor cloud: where it was dropped and its reach (Unity metres); the ship hidden by it this frame
        Entry cloud;
        Vector3 cloudCentre;
        bool hiddenByCloud;

        void Fire(Entry e)
        {
            var fx = WeaponFx.Load(e.item.index) ?? (e.rules.type <= Device.Type.ShieldDisruptor ? WeaponFx.Load(EmpLookItem) : null);
            Play(fx != null ? fx.Shot : null);
            var pos = transform.position;
            switch (e.rules.type)
            {
                case Device.Type.AreaEmp:
                    waves.Add(new Wave { e = e, centre = pos });
                    Explosion.Spawn(11, pos, transform.forward, e.radius / ShockScaleUnits, null, false);
                    break;
                case Device.Type.EmpShot:
                case Device.Type.ShieldDisruptor:
                    if (shots.TryGetValue(e, out var s))
                    {
                        int b = s.gun.TryFire(transform);
                        if (b >= 0) s.gun.SetTarget(b, weapons != null && weapons.LockTarget != null && weapons.LockTarget.Alive ? weapons.LockTarget : null);
                    }
                    break;
                case Device.Type.OverchargeArea:
                case Device.Type.OverchargeBeam:
                {
                    var hp = self.hitpoints;
                    int cost = Device.OverchargeCost(hp.maxShield, e.shieldCost);
                    hp.shield = Mathf.Max(0f, hp.shield - cost);
                    float dmg = Device.OverchargeDamage(cost, e.damagePercent);
                    bool beam = e.rules.type == Device.Type.OverchargeBeam;
                    float range = e.radius * M;
                    var all = Target.All;
                    for (int i = all.Count - 1; i >= 0; i--)
                    {
                        var t = all[i];
                        if (!Affects(t)) continue;
                        var to = t.transform.position - pos;
                        if (!Device.InCone(transform.forward, to, range, beam ? e.coneDeg : 180f)) continue;
                        if (beam) ShowBeam(t);
                        weapons?.DeviceHit(t, dmg, e.emp, to.normalized, e.item.index);
                    }
                    if (!beam) Explosion.Spawn(11, pos, transform.forward, e.radius / ShockScaleUnits, null, false);
                    break;
                }
                case Device.Type.StrikeCharge:
                    if (weapons != null) weapons.DamageBoost = Mathf.Max(1f, e.damagePercent / 100f);
                    break;
                case Device.Type.Deflector:
                    deflector = e;
                    self.Deflects = deflect;
                    break;
                case Device.Type.SensorCloud:
                    cloud = e;
                    cloudCentre = pos;
                    Explosion.Spawn(11, pos, transform.forward, e.radius / ShockScaleUnits, null, false);   // (the burst; the cloud itself isn't drawn yet)
                    break;
                case Device.Type.HullRepair:
                {
                    var hp = self.hitpoints;
                    if (hp == null) break;
                    hp.hull = Mathf.Min(hp.maxHull, hp.hull + RepairPoints(hp.maxHull, e.damagePercent));
                    self.hp = hp.hull;
                    break;
                }
                case Device.Type.ArmorRepair:
                {
                    var hp = self.hitpoints;
                    if (hp == null) break;
                    hp.armor = Mathf.Min(hp.maxArmor, hp.armor + RepairPoints(hp.maxArmor, e.damagePercent));
                    break;
                }
                case Device.Type.Restock:
                    weapons?.Restock(e.ammo);
                    break;
                case Device.Type.DeviceCharger:
                    foreach (var o in devices) if (o != e) o.rules.ResetCooldown();
                    break;
                case Device.Type.Corrosion:
                    corroding = LockWithin(e);
                    corrosionEntry = e;
                    corrosionTickMs = 0f;
                    if (corroding != null) ShowBeam(corroding);
                    break;
                case Device.Type.Teleport:
                case Device.Type.SpectralShift:
                {
                    float want = e.radius * M;
                    float d = BlinkDistance(want);
                    if (d <= 0f) break;
                    var from = pos;
                    var dir = transform.forward;
                    transform.position = from + dir * d;
                    if (e.rules.type == Device.Type.SpectralShift)
                    {
                        // every hostile ship within deviceRange2 of the way flown takes the device's damage (ES2's sphere trace)
                        float r2 = e.range2 * M;
                        var all = Target.All;
                        for (int i = all.Count - 1; i >= 0; i--)
                        {
                            var t = all[i];
                            if (!Affects(t)) continue;
                            var c = t.transform.position - from;
                            float along = Mathf.Clamp(Vector3.Dot(c, dir), 0f, d);
                            if ((c - dir * along).sqrMagnitude > r2 * r2) continue;
                            weapons?.DeviceHit(t, e.damage, e.emp, dir, e.item.index);
                        }
                    }
                    break;
                }
                case Device.Type.FusionHook:
                    hooked = HookTarget(e);
                    hookEntry = e;
                    hookMs = hookPullSpeed = hookTargetSpeed = hookBeamMs = 0f;
                    break;
                case Device.Type.QuantumTether:
                    Tether(e, LockWithin(e));
                    break;
                case Device.Type.Entangler:
                    entangled = LockWithin(e);
                    entanglerEntry = e;
                    entangledDamage = 0f;
                    if (entangled != null) { self.Damaged += OnSelfDamaged; ShowBeam(entangled); }
                    break;
            }
        }

        void Tether(Entry e, Target t)
        {
            if (tethered != null) { tethered.Damaged -= OnTetheredDamaged; tethered.Died -= OnTetheredDied; }
            tethered = t;
            tetherEntry = e;
            if (t == null) return;
            t.Damaged += OnTetheredDamaged;
            t.Died += OnTetheredDied;
            ShowBeam(t);
        }

        /// <summary>The tethered ship took 'dmg': every hostile ship within deviceRadius of it takes it too (ES2's chain reaction
        /// damage, which isn't shared again).</summary>
        void OnTetheredDamaged(Target t, int dmg, bool byNpc)
        {
            if (sharing || tetherEntry == null || dmg <= 0) return;
            sharing = true;
            try
            {
                float r = tetherEntry.radius * M;
                var all = Target.All;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    var o = all[i];
                    if (o == t || !Affects(o)) continue;
                    var d = o.transform.position - t.transform.position;
                    if (d.sqrMagnitude > r * r) continue;
                    weapons?.DeviceHit(o, dmg, 0, d.normalized, tetherEntry.item.index);
                }
            }
            finally { sharing = false; }
        }

        /// <summary>The tethered ship died: the tether jumps to a random hostile ship within its reach (ES2's JumpToRandomSurroundingEnemy).</summary>
        void OnTetheredDied(Target t)
        {
            var e = tetherEntry;
            if (e == null) return;
            float r = e.radius * M;
            var near = new List<Target>();
            foreach (var o in Target.All)
                if (o != t && Affects(o) && (o.transform.position - t.transform.position).sqrMagnitude <= r * r) near.Add(o);
            Tether(e, near.Count > 0 ? near[Random.Range(0, near.Count)] : null);
        }

        void OnSelfDamaged(Target t, int dmg, bool byNpc) { if (dmg > 0) entangledDamage += dmg; }

        /// <summary>A run's end (its time or Cancel).</summary>
        void End(Entry e)
        {
            switch (e.rules.type)
            {
                case Device.Type.StrikeCharge:
                    if (weapons != null) weapons.DamageBoost = 1f;
                    break;
                case Device.Type.Deflector:
                    if ((object)self.Deflects == (object)deflect) self.Deflects = null;
                    deflector = null;
                    break;
                case Device.Type.SensorCloud:
                    if (cloud == e) { cloud = null; Unhide(); }
                    break;
                case Device.Type.Corrosion:
                    corroding = null; corrosionEntry = null;
                    break;
                case Device.Type.FusionHook:
                    hooked = null; hookEntry = null;
                    break;
                case Device.Type.QuantumTether:
                    Tether(e, null); tetherEntry = null;
                    break;
                case Device.Type.Entangler:
                    self.Damaged -= OnSelfDamaged;
                    if (entangled != null && entangled.Alive && entangledDamage > 0f)
                    {
                        var d = entangled.transform.position - transform.position;
                        weapons?.DeviceHit(entangled, entangledDamage * e.damagePercent / 100f, 0, d.normalized, e.item.index);
                    }
                    entangled = null; entanglerEntry = null; entangledDamage = 0f;
                    break;
            }
        }

        /// <summary>The corrosion's tick, the hook's pull, the entangler's and the tether's end when their ship dies.</summary>
        void UpdateRunning(float dtMs)
        {
            float dt = dtMs / 1000f;
            if (corrosionEntry != null && corrosionEntry.rules.Active)
            {
                if (!Affects(corroding)) { if (corrosionEntry.rules.Cancel()) End(corrosionEntry); }
                else
                {
                    corrosionTickMs += dtMs;
                    if (corrosionTickMs >= Device.CorrosionTickMs)
                    {
                        corrosionTickMs -= Device.CorrosionTickMs;
                        var d = corroding.transform.position - transform.position;
                        weapons?.DeviceHit(corroding, corrosionEntry.damage, 0, d.normalized, corrosionEntry.item.index, 0f);   // past the shield
                    }
                }
            }
            if (hookEntry != null && hookEntry.rules.Active)
            {
                var e = hookEntry;
                var d = hooked != null ? hooked.transform.position - transform.position : Vector3.zero;
                float dist = d.magnitude;
                bool over = !Affects(hooked) || dist < Device.HookMinDistance || (dist > 1e-4f && Vector3.Dot(transform.forward, d / dist) < Device.HookMaxAngleDot);
                if (over) { if (e.rules.Cancel()) End(e); }
                else
                {
                    hookMs += dtMs;
                    hookBeamMs -= dtMs;
                    if (hookBeamMs <= 0f) { ShowBeam(hooked); hookBeamMs = BeamShowMs * 0.75f; }
                    if (hookMs >= Device.HookPullDelayMs && dist > 1e-4f)
                    {
                        var dir = d / dist;
                        hookPullSpeed = Mathf.Min(Device.HookMaxSpeed, hookPullSpeed + Device.HookOwnerAccel * dt);
                        transform.position += dir * (hookPullSpeed * dt);
                        float share = Mathf.Clamp01(dist / Device.HookTargetAccelRange);
                        hookTargetSpeed = Mathf.Min(Device.HookMaxSpeed, hookTargetSpeed + Device.HookTargetAccel * share * dt);
                        if (hooked.RemoteDamage == null) hooked.transform.position -= dir * (hookTargetSpeed * dt);   // another game's ship isn't moved from here
                    }
                }
            }
            if (entanglerEntry != null && entanglerEntry.rules.Active && !Affects(entangled))
            {
                entangledDamage = 0f;   // the link broke with its ship: nothing is dealt
                if (entanglerEntry.rules.Cancel()) End(entanglerEntry);
            }
            if (tetherEntry != null && tetherEntry.rules.Active && tethered == null)
                if (tetherEntry.rules.Cancel()) End(tetherEntry);
        }

        /// <summary>The ship's own cloak's state back on Target.cloaked once the cloud no longer hides it.</summary>
        void Unhide()
        {
            if (!hiddenByCloud) return;
            hiddenByCloud = false;
            var cloak = GetComponent<PlayerCloak>();
            self.cloaked = cloak != null && cloak.Rules != null && cloak.Rules.Cloaked;
        }

        /// <summary>EVERSPACE 2's smoke bomb field: grown over GrowDuration to its radius, the ship inside it hidden (IsHiddenByActor).</summary>
        void UpdateCloud()
        {
            if (cloud == null || !cloud.rules.Active) { Unhide(); return; }
            float reach = Device.WaveRadius(cloud.rules.Timer, cloud.growMs, cloud.radius) * M;
            bool inside = (transform.position - cloudCentre).sqrMagnitude <= reach * reach;
            if (inside) { hiddenByCloud = true; self.cloaked = true; }
            else Unhide();
        }

        void UpdateEffects(float dtMs)
        {
            UpdateCloud();
            UpdateRunning(dtMs);
            for (int w = waves.Count - 1; w >= 0; w--)
            {
                var wave = waves[w];
                wave.ms += dtMs;
                float r = Device.WaveRadius(wave.ms, wave.e.growMs, wave.e.radius) * M;
                Slow(wave.e, wave.centre, r, wave.reached);
                if (wave.ms >= wave.e.growMs) waves.RemoveAt(w);
            }
            var cam = Camera.main;
            foreach (var s in shots.Values) { s.gun.Update(dtMs, Target.All, null); s.rig.UpdateVisuals(dtMs, cam, transform.forward); }
            foreach (var s in deflected.Values) { s.gun.Update(dtMs, Target.All, null); s.rig.UpdateVisuals(dtMs, cam, transform.forward); }
            for (int i = beams.Count - 1; i >= 0; i--)
            {
                var (beam, to, ms) = beams[i];
                ms -= dtMs;
                if (ms <= 0f || to == null || beam == null)
                {
                    if (beam != null) beam.gameObject.SetActive(false);
                    beams.RemoveAt(i);
                    continue;
                }
                beams[i] = (beam, to, ms);
                PlaceBeam(beam, to);
            }
        }

        /// <summary>The EMP devices' slow on every hostile ship within 'radius' (metres) of 'centre' not yet in 'reached'.</summary>
        void Slow(Entry e, Vector3 centre, float radius, HashSet<Target> reached)
        {
            float factor = Device.SlowFactor(e.slowPercent);
            var all = Target.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (!Affects(t) || (reached != null && reached.Contains(t))) continue;
                var d = t.transform.position - centre;
                if (d.sqrMagnitude > radius * radius) continue;
                reached?.Add(t);
                t.lastPlayerWeapon = e.item.index;
                t.SlowBy?.Invoke(e.rules.durationMs, factor, true);
                if (e.emp > 0) weapons?.DeviceHit(t, 0f, e.emp, d.normalized, e.item.index);
            }
        }

        void OnShotHit(Shot s, int b, Target t, Vector3 p)
        {
            s.rig.ShowImpact(p);
            var e = s.e;
            var dir = s.gun.bullets[b].velocity.normalized;
            switch (e.rules.type)
            {
                case Device.Type.EmpShot:
                    Explosion.Spawn(11, p, dir, e.radius / ShockScaleUnits, null, false);
                    Slow(e, p, e.radius * M, null);
                    if (Affects(t) && t.SlowBy != null && (t.transform.position - p).sqrMagnitude > e.radius * M * e.radius * M)
                        t.SlowBy(e.rules.durationMs, Device.SlowFactor(e.slowPercent), true);   // a big hull whose centre lies beyond the radius
                    break;
                case Device.Type.ShieldDisruptor:
                {
                    var hp = t.hitpoints;
                    if (hp != null && hp.maxShield > 0 && t.RemoteDamage == null)
                    {
                        hp.shield = Mathf.Max(0f, hp.shield - e.damage);   // GoF3: the shield only
                        t.lastPlayerWeapon = e.item.index;
                    }
                    else
                    {
                        // No shield here (the NPCs) or another game's ship: another player's goes to their shield first.
                        float dmg = t.netShield >= 0f ? e.damage : e.damage * e.damagePercent / 100f;
                        weapons?.DeviceHit(t, dmg, e.emp, dir, e.item.index);
                    }
                    break;
                }
                default:   // a deflected shot
                    weapons?.DeviceHit(t, s.damage != null ? s.damage[b] : 0f, s.emp != null ? (int)s.emp[b] : 0, dir, e.item.index);
                    break;
            }
        }

        // ---- the Carom Deflector -------------------------------------------------------------------------------------

        /// <summary>Target.Deflects while the deflector runs: an enemy shot about to hit the ship flies back at its shooter,
        /// homing, at deviceDamagePercent of its damage (GoF3: x2); a beam is stopped (GoF3: "deflect just about everything").</summary>
        bool Deflect(Gun g, int i)
        {
            var e = deflector;
            if (e == null || g == null || g.Mirror) return false;   // another game's shot: a picture only (NetShotMirror)
            var shooter = g.owner;
            if ((object)shooter == null || shooter == self || shooter.isPlayer) return false;
            if (g.isBeam) return true;
            if (!deflected.TryGetValue(g.itemIndex, out var s))
            {
                var look = db.Item(g.itemIndex) ?? db.Item(g.lookIndex);
                if (look == null) return false;
                s = new Shot { e = e, gun = new Gun(look, 0f, 1f, 12, DeflectLifetimeMs, Mathf.Max(g.speedUnitsPerMs, ShotSpeedUnits), true) { owner = self } };
                s.damage = new float[s.gun.bullets.Length];
                s.emp = new float[s.gun.bullets.Length];
                s.rig = new GunRig(s.gun, WeaponFx.Load(g.itemIndex), fxRoot, null);
                s.gun.Hit += (b, t, p) => OnShotHit(s, b, t, p);
                deflected[g.itemIndex] = s;
            }
            s.e = e;
            var from = g.bullets[i].position;
            bool back = shooter != null && shooter.Alive;
            var dir = back ? shooter.transform.position - from : -g.bullets[i].velocity;
            if (dir.sqrMagnitude < 1e-8f) return true;
            s.gun.reloadAcc = s.gun.reloadMs + 1f;   // several in one frame
            int n = s.gun.TryFire(from, Quaternion.LookRotation(dir.normalized), false);
            if (n < 0) return true;   // the pool is full: the shot is gone all the same
            s.gun.SetTarget(n, back ? shooter : null);
            s.damage[n] = g.damage * e.damagePercent / 100f;
            s.emp[n] = g.emp * e.damagePercent / 100f;
            return true;
        }

        // ---- the overcharger beam's look (the transfusion beam, RepairBeam) -----------------------------------------

        void ShowBeam(Target t)
        {
            Transform beam = null;
            foreach (var b in beamPool) if (b != null && !b.gameObject.activeSelf) { beam = b; break; }
            if (beam == null)
            {
                var prefab = assets != null ? assets.transfusionBeam : null;
                if (prefab == null) return;
                var go = Instantiate(prefab);
                go.name = "Overcharger beam";
                GunRig.StripForFx(go);
                beam = go.transform;
                beamPool.Add(beam);
            }
            beam.gameObject.SetActive(true);
            PlaceBeam(beam, t);
            beams.Add((beam, t, BeamShowMs));
        }

        void PlaceBeam(Transform beam, Target t)
        {
            var d = t.transform.position - transform.position;
            if (d.sqrMagnitude < 1e-6f) return;
            beam.SetPositionAndRotation(transform.position, Quaternion.LookRotation(d, transform.up));
            beam.localScale = new Vector3(1f, 1f, d.magnitude / M);
        }
    }
}
