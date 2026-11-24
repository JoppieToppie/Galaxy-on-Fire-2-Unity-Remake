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
            EndHunt();
            EndFuryLook();
            EndStar(false);
            starShare = 1f; ApplyModelShare();
            if (held != null) Unjam(held);
            if (thrown != null) Unjam(thrown);
            held = thrown = null;
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
                case Device.Type.RepairKit:
                    if (Overheat.AnyHot) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNothingToRepair", "{0}: nothing to repair"), e.name));
                    return false;
                case Device.Type.Recovery:
                    if (PlayerDebuffs.Any) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoDebuff", "{0}: no debuff on the ship"), e.name));
                    return false;
                case Device.Type.Corrosion:
                case Device.Type.QuantumTether:
                case Device.Type.Entangler:
                case Device.Type.Virus:
                    if (LockWithin(e) != null) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoTarget", "{0}: no ship locked in reach"), e.name));
                    return false;
                case Device.Type.Teleport:
                case Device.Type.SpectralShift:
                    if (BlinkDistance(e.radius * M) > 0f) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoRoom", "{0}: no room ahead"), e.name));
                    return false;
                case Device.Type.RiteControl:
                    if (RiteTarget(e, Controllable) != null) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoRiteTarget", "{0}: no ship in the sights"), e.name));
                    return false;
                case Device.Type.RiteStorm:
                    if (StormTarget(e) != null) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoRiteTarget", "{0}: no ship in the sights"), e.name));
                    return false;
                case Device.Type.RiteHunt:
                {
                    var t = HuntTarget(e);
                    if (t == null)
                    {
                        Message?.Invoke(string.Format(Localization.Extra("deviceNoHuntTarget", "{0}: no ship to leap at"), e.name));
                        return false;
                    }
                    if (HuntRoom(e, t) > 0f) return true;
                    Message?.Invoke(string.Format(Localization.Extra("deviceNoRoomBehind", "{0}: no room behind the target"), e.name));
                    return false;
                }
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
        float BlinkDistance(float want) => BlinkDistance(transform.position, transform.forward, want, null);

        /// <summary>The same from 'from' along 'dir', 'ignore''s own hull left out (the Hunt's way back from its target).</summary>
        float BlinkDistance(Vector3 from, Vector3 dir, float want, Target ignore)
        {
            float probe = BlinkProbe();
            float hit = float.MaxValue;
            if (HullCollision.Any && HullCollision.Sweep(from, from + dir * want, probe,
                    b => b != null && b.obstacle != null && b.obstacle.Active && b.Collides && (ignore == null || b.target != ignore), out float hullHit))
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
                if (t == null || !t.isAsteroid || !t.Alive || t == ignore) continue;
                var c = t.transform.position - from;
                float along = Vector3.Dot(c, dir);
                if (along < 0f || along - t.radius > want) continue;
                float side = (c - dir * along).magnitude;
                if (side > t.radius + probe) continue;
                hit = Mathf.Min(hit, along - t.radius);
            }
            return Device.Blink(want, hit, probe);
        }

        /// <summary>The ship's top speed in m/s: the Rites' distances are seconds of it (Chorus's at 140 m/s).</summary>
        float TopSpeedMs()
        {
            var ship = GetComponent<ShipController>();
            return ship != null ? ship.Model.TopSpeed * 1000f * ship.metersPerUnit : 100f;
        }

        /// <summary>A Rite's reach (deviceRadius, else Chorus's 1000 m as seconds of the top speed).</summary>
        float RiteRange(Entry e) => e.radius > 0 ? e.radius * M
            : (e.rules.type == Device.Type.RiteControl ? Device.ControlGrabTops : Device.HuntRangeTops) * TopSpeedMs();

        /// <summary>How far behind its target the Hunt lands (deviceRange2, else Chorus's 180 m as seconds of the top speed).</summary>
        float HuntBehind(Entry e) => e.range2 > 0 ? e.range2 * M : Device.HuntBehindTops * TopSpeedMs();

        /// <summary>How far the Storm jumps (deviceRange2, else Chorus's 100 m as seconds of the top speed).</summary>
        float StormReach(Entry e) => e.range2 > 0 ? e.range2 * M : (e.A ? Device.StormChainTopsA : Device.StormChainTops) * TopSpeedMs();

        static bool IsPursuer(Target t)
        {
            var npc = t.GetComponent<GoF2Remake.World.NpcShip>();
            return npc != null && npc.Role == NpcRoles.Role.Pursuer;
        }

        /// <summary>A ship the Hunt may leap at: one the devices act on, not a capital ship, a weak spot passing its hits on, an
        /// unharmable one or the pursuer (Blink's ValidTargetsBlacklist: BP_NPC_Ship_Fortress and BP_NPC_Guilt).</summary>
        bool Huntable(Target t) =>
            Affects(t) && t.forwardTo == null && !t.invulnerable && t.GetComponentInParent<GoF2Remake.World.CapitalShip>() == null && !IsPursuer(t);

        /// <summary>A ship the Storm may strike: one the devices act on, its turrets and weak spots too, not a capital ship's hull,
        /// an unharmable one or the pursuer (Pulse Zap's whitelist: SpaceNPC, turrets, weak points; its blacklist: the
        /// Fortress, Guilt, the Disruptor, the bosses' weak points).</summary>
        bool Stormable(Target t) =>
            Affects(t) && !t.invulnerable && t.GetComponent<GoF2Remake.World.CapitalShip>() == null && !IsPursuer(t);

        /// <summary>A Rite's target: the lock if 'ok' and within reach, else the nearest such ship in the sights
        /// (Device.HuntAimDot of the nose).</summary>
        Target RiteTarget(Entry e, System.Func<Target, bool> ok)
        {
            float r = RiteRange(e);
            var pos = transform.position;
            var lockT = weapons != null ? weapons.LockTarget : null;
            if (ok(lockT) && (lockT.transform.position - pos).sqrMagnitude <= r * r) return lockT;
            Target best = null;
            float bestD = float.MaxValue;
            foreach (var t in Target.All)
            {
                if (!ok(t)) continue;
                var d = t.transform.position - pos;
                float sq = d.sqrMagnitude;
                if (sq > r * r || sq < 1e-4f || Vector3.Dot(transform.forward, d / Mathf.Sqrt(sq)) < Device.HuntAimDot) continue;
                if (sq < bestD) { bestD = sq; best = t; }
            }
            return best;
        }

        Target HuntTarget(Entry e) => RiteTarget(e, Huntable);
        Target StormTarget(Entry e) => RiteTarget(e, Stormable);

        /// <summary>How far behind 't' the Hunt can land: its distance, short of what is in the way back from the target.</summary>
        float HuntRoom(Entry e, Target t) => BlinkDistance(t.transform.position, -t.transform.forward, HuntBehind(e), t);

        /// <summary>The ship's velocity (Unity units a second, along its nose).</summary>
        Vector3 ShipVelocity()
        {
            var ship = GetComponent<ShipController>();
            return ship != null ? transform.forward * ship.SpeedMetersPerSecond : Vector3.zero;
        }

        /// <summary>The item's Rite looks (items.json fx "rite"), null = none.</summary>
        RiteLook LookOf(Entry e) => WeaponFx.Load(e.item.index)?.rite;

        // ---- Rite of the Hunt: Blink's timelines (Blink_Disappear, then the leap, Blink_Reappear) -------------------------
        Entry huntEntry; Target huntTarget; int huntPhase; float huntMs, huntRate = 1f;
        // the ship's model shrunk away by the Hunt and the Star (each its share, the smaller shown), as it was once both are 1
        Transform hiddenModel; Vector3 hiddenScale = Vector3.one; float huntShare = 1f, starShare = 1f;

        void ApplyModelShare()
        {
            float share = Mathf.Min(huntShare, starShare);
            if (share >= 1f)
            {
                if (hiddenModel != null) hiddenModel.localScale = hiddenScale;
                hiddenModel = null;
                return;
            }
            if (hiddenModel == null)
            {
                var ship = GetComponent<ShipController>();
                hiddenModel = ship != null ? ship.visualModel : null;
                if (hiddenModel == null) return;
                hiddenScale = hiddenModel.localScale;
            }
            hiddenModel.localScale = hiddenScale * Mathf.Max(0.001f, share);
        }

        /// <summary>The Hunt fired at 't': the ship vanishes (its model shrinking away over Blink_Disappear, faster for a short
        /// leap: BlinkPlayRateCurve), then leaps and comes back (UpdateHunt).</summary>
        void StartHunt(Entry e, Target t)
        {
            EndHunt();
            huntEntry = e; huntTarget = t; huntPhase = 1; huntMs = 0f;
            huntRate = Device.HuntPlayRate(Vector3.Distance(t.transform.position, transform.position) / Mathf.Max(1f, RiteRange(e)));
            var look = LookOf(e);
            if (look != null)
            {
                look.Play(look.spec.vanish, transform.position, transform.rotation, ShipVelocity());
                Play(RiteLook.Pick(look.vanish));
            }
        }

        void UpdateHunt(float dtMs)
        {
            if (huntPhase == 0) return;
            huntMs += dtMs * huntRate;
            if (huntPhase == 1)
            {
                ShowModel(1f - Device.HuntVanish(huntMs / Device.HuntVanishMs));
                if (huntMs < Device.HuntVanishMs) return;
                if (!Huntable(huntTarget) || !Hunt(huntEntry, huntTarget)) { EndHunt(); return; }   // gone meanwhile: it stays put
                if (huntEntry.A) VoidPool?.AddBonus(Device.HuntBonusPips);   // (Blink A: a bonus pip for 10 s)
                if (huntEntry.B) StartFury(huntEntry);                         // (Blink B: x1.5 for 5 s)
                var look = LookOf(huntEntry);
                if (look != null)
                {
                    look.Play(look.spec.appear, transform.position, transform.rotation, ShipVelocity());
                    Play(RiteLook.Pick(look.appear));
                }
                huntPhase = 2;
                huntMs = 0f;
            }
            ShowModel(1f - Device.HuntAppear(huntMs / Device.HuntAppearMs));
            if (huntMs >= Device.HuntAppearMs) EndHunt();
        }

        void ShowModel(float share) { huntShare = share; ApplyModelShare(); }

        /// <summary>The Hunt over (or stopped by a death, a cutscene, a jump): the model as it was.</summary>
        void EndHunt()
        {
            huntPhase = 0; huntEntry = null; huntTarget = null;
            ShowModel(1f);
        }

        /// <summary>The leap (ABlinkActivePower): onto 't''s tail, facing its way, at the ship's own speed (RestorePlayerSpeed),
        /// the camera gliding over from where it was (ChaseCamera.Glide, the 1 s camera timeline). False = no room.</summary>
        bool Hunt(Entry e, Target t)
        {
            float room = HuntRoom(e, t);
            if (room <= 0f) return false;
            var tt = t.transform;
            var ship = GetComponent<ShipController>();
            var chase = Camera.main != null ? Camera.main.GetComponent<ChaseCamera>() : null;
            var camPos = chase != null ? chase.transform.position : Vector3.zero;
            var camRot = chase != null ? chase.transform.rotation : Quaternion.identity;
            transform.SetPositionAndRotation(Device.HuntSpot(tt.position, tt.forward, room), Quaternion.LookRotation(tt.forward, tt.up));
            if (ship != null && ship.Model.FreeFlight) ship.Model.FreeVelocity = tt.forward * ship.Model.FreeVelocity.magnitude;
            if (chase != null && chase.isActiveAndEnabled) { chase.Snap(); chase.Glide(camPos, camRot, 1000f); }
            return true;
        }

        // ---- Rite of the Star: Drill's drive, its hidden ship and its burning sphere ----------------------------------------
        Entry starEntry; float starTickMs, starTailMs, starShowMs; bool starWasInvulnerable;
        readonly List<Vector3> starCentres = new List<Vector3>();
        GameObject starLoopFx;
        AudioSource starLoopSound;
        const float StarFadeMs = 150f;   // (the model shrinking away and back: Drill_Activate / Drill_Deactivate, not read)

        void StartStar(Entry e)
        {
            starEntry = e;
            starTickMs = 0f; starTailMs = 0f; starShowMs = 0f;
            starWasInvulnerable = self.invulnerable;   // (the Supernova Challenge's end sets it too)
            self.invulnerable = true;
            var ship = GetComponent<ShipController>();
            if (ship != null) { ship.DriveMs = Device.StarSpeedTops * TopSpeedMs(); ship.DriveTurnDegS = Device.StarTurnDegS; }
            var look = LookOf(e);
            if (look != null)
            {
                starLoopFx = look.Play(look.spec.loop, transform.position, transform.rotation, Vector3.zero, transform);
                var clip = RiteLook.Pick(look.loop);
                if (clip != null)
                {
                    starLoopSound ??= gameObject.AddComponent<AudioSource>();
                    starLoopSound.playOnAwake = false; starLoopSound.spatialBlend = 0f; starLoopSound.loop = true;
                    starLoopSound.clip = Modding.ModSounds.Get(clip);
                    starLoopSound.volume = Settings.SfxVolume;
                    starLoopSound.Play();
                }
            }
        }

        /// <summary>The Star's end (its time out, a second press, a death): the drive off, the ship back, its burn on for 0.3 s.</summary>
        void EndStar(bool burnOn = true)
        {
            if (starEntry == null) return;
            var ship = GetComponent<ShipController>();
            if (ship != null) ship.DriveMs = 0f;
            if (self != null) self.invulnerable = starWasInvulnerable;
            if (starLoopFx != null) Destroy(starLoopFx);
            starLoopFx = null;
            if (starLoopSound != null) starLoopSound.Stop();
            var look = LookOf(starEntry);
            if (look != null && burnOn)
            {
                look.Play(look.spec.end, transform.position, transform.rotation, ShipVelocity());
                Play(RiteLook.Pick(look.end));
            }
            starTailMs = burnOn ? Device.StarTailMs : 0f;
            if (!burnOn) starEntry = null;
        }

        void UpdateStar(float dtMs)
        {
            if (starEntry == null) { if (starShare < 1f) { starShare = 1f; ApplyModelShare(); } return; }
            bool running = starEntry.rules.Active;
            // the ship shrinks away as it starts and comes back as it ends
            starShowMs = running ? Mathf.Min(StarFadeMs, starShowMs + dtMs) : Mathf.Max(0f, starShowMs - dtMs);
            starShare = 1f - starShowMs / StarFadeMs;
            ApplyModelShare();
            if (!running)
            {
                starTailMs -= dtMs;
                if (starTailMs <= 0f && starShowMs <= 0f) { starEntry = null; return; }
                if (starTailMs <= 0f) return;
            }
            starTickMs += dtMs;
            if (starTickMs < Mathf.Max(50f, starEntry.growMs)) return;
            starTickMs = 0f;
            float top = TopSpeedMs();
            float ahead = Device.StarAheadTops * top, radius = starEntry.range2 > 0 ? starEntry.range2 * M : Device.StarRadiusTops * top;
            // the spheres: the one ahead, and with upgrade A four more turning about the nose (Drill A's side stars)
            starCentres.Clear();
            starCentres.Add(transform.position + transform.forward * ahead);
            if (starEntry.A)
                for (int k = 0; k < 4; k++) starCentres.Add(transform.TransformPoint(Device.StarSide(k, starEntry.rules.Timer) * top));
            var look = LookOf(starEntry);
            var all = Target.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (!Affects(t)) continue;
                bool inside = false;
                foreach (var c in starCentres)
                    if ((t.transform.position - c).sqrMagnitude <= (radius + t.radius) * (radius + t.radius)) { inside = true; break; }
                if (!inside) continue;
                weapons?.DeviceHit(t, starEntry.damage * RiteFury, starEntry.emp, transform.forward, starEntry.item.index);
                if (starEntry.B && !t.Alive)   // (Drill B: a quarter of the hull a kill)
                {
                    HealShare(Device.StarHealShare);
                    PlayOnShip(starEntry, l => l.spec.heal, l => l.heal);
                }
                if (look != null && t != null)
                {
                    look.Play(look.spec.hit, t.transform.position, t.transform.rotation, Vector3.zero, t.transform);
                    Play(RiteLook.Pick(look.hit));
                }
            }
        }

        // ---- Rite of Control: Telekinesis's hold and toss ----------------------------------------------------------------
        Entry controlEntry; Target held; float heldBeamMs;
        Target thrown, tossAt; Entry thrownEntry; Vector3 thrownDir; float thrownMs, thrownSpeed;

        /// <summary>A ship the Rite of Control may take: as the Hunt's (no capital ship, weak spot, unharmable one or the pursuer;
        /// Telekinesis's blacklist has the Fortress, the bat swarms and the Phantom), and one this game moves.</summary>
        bool Controllable(Target t) => Huntable(t) && t.isShip && t.RemoteDamage == null;

        /// <summary>Where the held ship is kept: Telekinesis's first OffsetLocation, beside the nose (seconds of the top speed).</summary>
        Vector3 HoldPoint() => transform.TransformPoint(Device.ControlHoldTops * TopSpeedMs());

        void Grab(Entry e, Target t)
        {
            controlEntry = e;
            held = t;
            heldBeamMs = 0f;
            Jam(t, e.rules.durationMs + 100f);
            var look = LookOf(e);
            if (look != null) { look.Play(look.spec.hit, t.transform.position, t.transform.rotation, Vector3.zero, t.transform); Play(RiteLook.Pick(look.hit)); }
        }

        /// <summary>BP_Telekinesis_Debuff's JamBrains: no fire, no flight of its own, no turning.</summary>
        static void Jam(Target t, float ms)
        {
            t.SlowBy?.Invoke(ms, 0f, true);
            t.jammedUntil = Mathf.Max(t.jammedUntil, Time.time + ms / 1000f);
        }

        static void Unjam(Target t)
        {
            if (t == null) return;
            t.jammedUntil = Time.time;
            t.ClearSlow?.Invoke();
        }

        /// <summary>The second press: the held ship thrown at the ship in the sights within the toss's reach (the lock first),
        /// else straight ahead (ActivateToss, CollectTossToTarget).</summary>
        void Toss(Entry e)
        {
            if (held == null || !Controllable(held)) { held = null; return; }
            float reach = Device.ControlTossTops * TopSpeedMs();
            var from = held;
            tossAt = RiteTarget(e, t => Affects(t) && t != from && (t.transform.position - from.transform.position).sqrMagnitude <= reach * reach);
            thrown = held; held = null;
            thrownEntry = e;
            thrownDir = tossAt != null ? (tossAt.transform.position - thrown.transform.position).normalized : transform.forward;
            thrownSpeed = Device.ControlSpeedTops * TopSpeedMs();
            thrownMs = 0f;
            Jam(thrown, Device.ControlFlightMs + 100f);
            var look = LookOf(e);
            if (look != null) { look.Play(look.spec.vanish, thrown.transform.position, thrown.transform.rotation, Vector3.zero, thrown.transform); Play(RiteLook.Pick(look.vanish)); }
        }

        /// <summary>The held ship pulled to its place beside the nose; the thrown one flying at its target, dying on what it hits.</summary>
        void UpdateControl(float dtMs)
        {
            float dt = dtMs / 1000f;
            if (held != null)
            {
                if (!Controllable(held)) { held = null; if (controlEntry != null && controlEntry.rules.Cancel()) End(controlEntry); }
                else
                {
                    var p = held.transform.position;
                    held.transform.position = Vector3.Lerp(p, HoldPoint(), 1f - Mathf.Exp(-6f * dt));
                    heldBeamMs -= dtMs;
                    if (heldBeamMs <= 0f) { ShowBeam(held); heldBeamMs = BeamShowMs * 0.75f; }
                }
            }
            if (thrown == null) return;
            if (thrown == null || !thrown.Alive) { thrown = null; return; }
            thrownMs += dtMs;
            if (tossAt != null && tossAt.Alive) thrownDir = (tossAt.transform.position - thrown.transform.position).normalized;   // (homing)
            var from = thrown.transform.position;
            float step = thrownSpeed * dt;
            var to = from + thrownDir * step;
            // a ship in the way: it takes the Rite's damage and the thrown one dies (CollisionDamageOtherTargets / FromOtherTarget)
            Target hitShip = null;
            foreach (var t in Target.All)
            {
                if (t == thrown || !Affects(t)) continue;
                var c = t.transform.position - from;
                float along = Mathf.Clamp(Vector3.Dot(c, thrownDir), 0f, step);
                float r = t.radius + thrown.radius;
                if ((c - thrownDir * along).sqrMagnitude <= r * r) { hitShip = t; break; }
            }
            bool geometry = hitShip == null && BlinkDistance(from, thrownDir, step, thrown) < step;   // (a hull, a landmark, an asteroid)
            thrown.transform.position = to;
            if (thrownDir.sqrMagnitude > 1e-6f) thrown.transform.rotation = Quaternion.LookRotation(thrownDir, thrown.transform.up);
            if (hitShip != null || geometry)
            {
                if (hitShip != null) weapons?.DeviceHit(hitShip, thrownEntry.damage * RiteFury, thrownEntry.emp, thrownDir, thrownEntry.item.index);
                var look = LookOf(thrownEntry);
                if (look != null) { look.Play(look.spec.end, thrown.transform.position, thrown.transform.rotation, Vector3.zero); Play(RiteLook.Pick(look.end)); }
                weapons?.DeviceHit(thrown, 1e7f, 0, thrownDir, thrownEntry.item.index, 0f);   // (its death: 500000 in Chorus)
                thrown = null;
                return;
            }
            if (thrownMs >= Device.ControlFlightMs) { Unjam(thrown); thrown = null; }   // nothing hit: let go where it is
        }

        // ---- Rite of the Senses: Scan's short range -------------------------------------------------------------------
        Entry sensesEntry; float sensesMs, sensesReach, sensesWaveMs; int sensesFound;

        void StartSenses(Entry e, bool longRange)
        {
            sensesEntry = e;
            sensesMs = 0f;
            sensesFound = 0;
            float top = TopSpeedMs();
            sensesReach = longRange ? Device.SensesLongTops * top : e.radius > 0 ? e.radius * M : Device.SensesTops * top;
            sensesWaveMs = longRange ? Device.SensesLongWaveMs : Device.SensesWaveMs;
            RiteSenses.Until = Mathf.Max(RiteSenses.Until, Time.time + e.jamMs / 1000f);   // (its jamMs: how long the senses last)
            Message?.Invoke(string.Format(Localization.Extra("riteSensesOn", "{0}: the class-A asteroids and the cargo of locked ships show"), e.name));
        }

        /// <summary>The wave: every cloaked hostile ship and cloaked container its front reaches is uncovered (a container's
        /// marker and lock come on, as when the ship comes near it: LootContainer.Reveal).</summary>
        void UpdateSenses(float dtMs)
        {
            if (sensesEntry == null) return;
            sensesMs += dtMs;
            float r = Device.WaveRadius(sensesMs, sensesWaveMs, sensesReach / M) * M;
            var look0 = LookOf(sensesEntry);
            foreach (var c in GoF2Remake.World.LootContainer.All)
            {
                if (c == null || !c.Cloaked || c.Revealed || c.Opened || (c.transform.position - transform.position).sqrMagnitude > r * r) continue;
                c.Reveal();
                sensesFound++;
                if (look0 != null) { look0.Play(look0.spec.hit, c.transform.position, c.transform.rotation, Vector3.zero, c.transform); Play(RiteLook.Pick(look0.hit)); }
            }
            var all = Target.All;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (!Affects(t) || (t.transform.position - transform.position).sqrMagnitude > r * r) continue;
                var npc = t.GetComponent<GoF2Remake.World.NpcShip>();
                if (npc == null || !npc.RadarHidden) continue;
                npc.Uncloak();
                if (npc.RadarHidden) continue;   // (hidden by a script, not a cloak: left so)
                var look = LookOf(sensesEntry);   // (Chorus's ping as the wave finds something)
                if (look != null) { look.Play(look.spec.hit, t.transform.position, t.transform.rotation, Vector3.zero, t.transform); Play(RiteLook.Pick(look.hit)); }
            }
            if (sensesMs < sensesWaveMs) return;
            if (sensesFound > 0)
                Message?.Invoke(string.Format(Localization.Extra("riteSensesFound", "{0}: {1} hidden container(s) found"), sensesEntry.name, sensesFound));
            sensesEntry = null;
        }

        // ---- Rite of the Storm: Pulse Zap's strike, BP_Buff_Pulse's jam, its chain ------------------------------------------
        Entry stormEntry; Target stormLast; Vector3 stormLastAt; int stormLeft; float stormMs;
        readonly HashSet<Target> stormZapped = new HashSet<Target>();

        void StartStorm(Entry e, Target t)
        {
            stormEntry = e;
            stormZapped.Clear();
            stormLeft = Mathf.Max(0, e.chain);
            stormMs = 0f;
            Zap(e, t, null);
        }

        /// <summary>One strike on 't' (from 'from', null: the ship): its shield hit, jammed for the jam's length (no fire,
        /// slowed, no turning), the beam and its look.</summary>
        void Zap(Entry e, Target t, Target from)
        {
            var origin = (object)from != null && from != null ? from.transform.position : transform.position;
            var dir = (t.transform.position - origin).normalized;
            float dealt = ShieldHit(e, t, dir, RiteFury);
            if (e.B && dealt > 0f)   // (Pulse B: a quarter of it onto the ship's own shield)
            {
                GainShield(dealt * Device.StealShare);
                PlayOnShip(e, l => l.spec.absorb, l => l.absorb);
            }
            t.SlowBy?.Invoke(e.jamMs, Device.SlowFactor(e.slowPercent), true);
            t.jammedUntil = Mathf.Max(t.jammedUntil, Time.time + e.jamMs / 1000f);
            ShowBeam(t, from);
            var look = LookOf(e);
            if (look != null && t != null)
            {
                look.Play(look.spec.hit, t.transform.position, t.transform.rotation, Vector3.zero, t.transform);
                Play(RiteLook.Pick(look.hit));
            }
            stormZapped.Add(t);
            stormLast = t;
            stormLastAt = t.transform.position;
        }

        /// <summary>The chain: every deviceGrowMs the nearest other ship the Storm may strike within its jump of the last one.</summary>
        void UpdateStorm(float dtMs)
        {
            if (stormEntry == null || stormLeft <= 0) return;
            stormMs += dtMs;
            if (stormMs < Mathf.Max(50f, stormEntry.growMs)) return;
            stormMs = 0f;
            var at = stormLast != null ? stormLast.transform.position : stormLastAt;
            float r = StormReach(stormEntry), best = r * r;
            Target next = null;
            foreach (var t in Target.All)
            {
                if (!Stormable(t) || stormZapped.Contains(t)) continue;
                float d = (t.transform.position - at).sqrMagnitude;
                if (d <= best) { best = d; next = t; }
            }
            if (next == null) { stormLeft = 0; return; }
            stormLeft--;
            Zap(stormEntry, next, stormLast);
        }

        // the running devices' state: the corrosion's target and tick, the hook, the tether, the entangler's sum
        Target corroding; float corrosionTickMs; Entry corrosionEntry;
        // the Shield Overcharge's look (Chorus's BP_Effect_Auxiliary_Shield_PlayerCapitalShip: a shield round the hull): the
        // ship's own shell (ships.json "overcharge": ShieldShell, Chorus's M_Shield), else the emergency system's refraction
        // bubble while it runs
        EmergencyBubble overchargeBubble; ShieldShell overchargeShell; Entry overchargeEntry;
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
                    if (e.rules.durationMs > 0f)   // (over its duration: UpdateRunning)
                    {
                        repairEntry = e; repairTotal = RepairPoints(hp.maxHull, e.damagePercent); repairDone = 0;
                        break;
                    }
                    hp.hull = Mathf.Min(hp.maxHull, hp.hull + RepairPoints(hp.maxHull, e.damagePercent));
                    self.hp = hp.hull;
                    break;
                }
                case Device.Type.RepairKit:
                    Overheat.CoolRandom(Random.value);
                    break;
                case Device.Type.ShieldOvercharge:
                {
                    if (self.hitpoints == null) break;
                    self.hitpoints.shield = self.hitpoints.maxShield;
                    self.hitpoints.shieldLocked = true;
                    var hull = GetComponent<ShipController>();
                    var model = hull != null ? hull.visualModel : null;
                    overchargeBubble?.Hide(); overchargeShell?.Hide(); overchargeBubble = null;
                    overchargeShell = model != null ? ShieldShell.Show(Session.ShipIndex, model.Find("hull") ?? model) : null;
                    if (overchargeShell == null)
                        overchargeBubble = assets != null ? EmergencyBubble.Show(assets, model != null ? model : transform,
                                                                                  model != null ? model.GetComponentInChildren<Renderer>() : null) : null;
                    overchargeEntry = e;
                    break;
                }
                case Device.Type.Recovery:
                    PlayerDebuffs.Lift();
                    break;
                case Device.Type.Virus:
                {
                    var t = LockWithin(e);
                    infected.Clear();
                    if (t == null) break;
                    infected.Add(t);
                    virusEntry = e; virusSpreadMs = 0f;
                    ShowBeam(t);
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
                case Device.Type.RiteSenses:
                {
                    var look = LookOf(e);
                    look?.Play(look.spec.start, pos, transform.rotation, Vector3.zero, transform);
                    StartSenses(e, senseLong);
                    break;
                }
                case Device.Type.RiteControl:
                {
                    var t = RiteTarget(e, Controllable);
                    if (t == null) { if (e.rules.Cancel()) End(e); break; }
                    var look = LookOf(e);
                    look?.Play(look.spec.start, pos, transform.rotation, Vector3.zero, transform);
                    Grab(e, t);
                    break;
                }
                case Device.Type.RiteStar:
                {
                    var look = LookOf(e);
                    look?.Play(look.spec.start, pos, transform.rotation, Vector3.zero, transform);
                    StartStar(e);
                    break;
                }
                case Device.Type.RiteHunt:
                case Device.Type.RiteStorm:
                {
                    var t = e.rules.type == Device.Type.RiteHunt ? HuntTarget(e) : StormTarget(e);
                    if (t == null) break;
                    var look = LookOf(e);
                    look?.Play(look.spec.start, pos, transform.rotation, Vector3.zero, transform);
                    if (e.rules.type == Device.Type.RiteHunt) StartHunt(e, t);
                    else StartStorm(e, t);
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
                case Device.Type.RiteStar:
                    EndStar();
                    break;
                case Device.Type.RiteControl:
                    if (held != null) { Unjam(held); held = null; }   // its hold over unthrown: let go where it is
                    controlEntry = null;
                    break;
                case Device.Type.StrikeCharge:
                    if (weapons != null) weapons.DamageBoost = 1f;
                    break;
                case Device.Type.Deflector:
                    if ((object)self.Deflects == (object)deflect) self.Deflects = null;
                    deflector = null;
                    break;
                case Device.Type.ShieldOvercharge:
                    if (self.hitpoints != null) self.hitpoints.shieldLocked = false;
                    overchargeBubble?.Hide(); overchargeBubble = null; overchargeEntry = null;
                    overchargeShell?.Hide(); overchargeShell = null;
                    break;
                case Device.Type.SensorCloud:
                    if (cloud == e) { cloud = null; Unhide(); }
                    break;
                case Device.Type.Corrosion:
                    corroding = null; corrosionEntry = null;
                    break;
                case Device.Type.HullRepair:
                    if (repairEntry == e) { Repair(repairTotal - repairDone); repairEntry = null; }   // (what is left, at its end)
                    break;
                case Device.Type.Virus:
                    if (virusEntry != e) break;
                    // every infected ship takes the virus's damage (ES2's 95 + 60 against its raider's 310 hull points)
                    float dmg = NpcTables.Hull(0, 0) * e.damagePercent / 100f;
                    foreach (var t in infected)
                    {
                        if (!Affects(t)) continue;
                        Explosion.Spawn(t.transform.position, 0.5f, false);
                        weapons?.DeviceHit(t, dmg, 0, (t.transform.position - transform.position).normalized, e.item.index);
                    }
                    infected.Clear(); virusEntry = null;
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
            UpdateFury(dtMs);
            UpdateHunt(dtMs);
            UpdateStorm(dtMs);
            UpdateStar(dtMs);
            UpdateControl(dtMs);
            UpdateSenses(dtMs);
            if (overchargeBubble != null && overchargeEntry != null)   // (the bubble grows in and fades out with the overcharge)
                overchargeBubble.Update(overchargeEntry.rules.ActiveLeft * overchargeEntry.rules.durationMs, overchargeEntry.rules.durationMs);
            if (overchargeShell != null)   // (the shell plays its own life: Chorus's particle, 0.5 s in, 4.8 s)
            {
                overchargeShell.Step(dt);
                if (!overchargeShell.Alive) overchargeShell = null;
            }
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
            if (repairEntry != null && repairEntry.rules.Active)
                Repair(Mathf.FloorToInt(repairTotal * Mathf.Clamp01(repairEntry.rules.Timer / Mathf.Max(1f, repairEntry.rules.durationMs))) - repairDone);
            if (virusEntry != null && virusEntry.rules.Active && infected.Count < Device.VirusMaxTargets
                && (virusSpreadMs += dtMs) >= Mathf.Max(100f, virusEntry.growMs > 0 ? virusEntry.growMs : 1000f))
            {
                virusSpreadMs = 0f;
                // the nearest hostile ship within deviceRadius of an infected one catches it
                float r = virusEntry.radius * M, best = r * r;
                Target next = null;
                foreach (var o in Target.All)
                {
                    if (!Affects(o) || infected.Contains(o)) continue;
                    foreach (var i in infected)
                    {
                        if (!Affects(i)) continue;
                        float d = (o.transform.position - i.transform.position).sqrMagnitude;
                        if (d <= best) { best = d; next = o; }
                    }
                }
                if (next != null) { infected.Add(next); ShowBeam(next); }
            }
        }

        // the nanobots' repair over time, the virus's infected ships
        Entry repairEntry, virusEntry;
        int repairTotal, repairDone;
        float virusSpreadMs;
        readonly List<Target> infected = new List<Target>();

        void Repair(int points)
        {
            var hp = self != null ? self.hitpoints : null;
            if (hp == null || points <= 0) return;
            repairDone += points;
            hp.hull = Mathf.Min(hp.maxHull, hp.hull + points);
            self.hp = hp.hull;
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
                var (beam, from, to, ms) = beams[i];
                ms -= dtMs;
                if (ms <= 0f || to == null || beam == null || ((object)from != null && from == null))
                {
                    if (beam != null) beam.gameObject.SetActive(false);
                    beams.RemoveAt(i);
                    continue;
                }
                beams[i] = (beam, from, to, ms);
                PlaceBeam(beam, from, to);
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
                    ShieldHit(e, t, dir);
                    break;
                default:   // a deflected shot
                    weapons?.DeviceHit(t, s.damage != null ? s.damage[b] : 0f, s.emp != null ? (int)s.emp[b] : 0, dir, e.item.index);
                    break;
            }
        }

        /// <summary>The shield disruptor's and the Rite of the Storm's hit: 'damage' off the shield (GoF3: the shield only); a
        /// ship without one (the NPCs) takes deviceDamagePercent of it as damage, another player's goes to its shield first.</summary>
        float ShieldHit(Entry e, Target t, Vector3 dir, float factor = 1f)
        {
            var hp = t.hitpoints;
            float amount = e.damage * factor;
            if (hp != null && hp.maxShield > 0 && t.RemoteDamage == null)
            {
                float was = hp.shield;
                hp.shield = Mathf.Max(0f, hp.shield - amount);
                t.lastPlayerWeapon = e.item.index;
                if (e.emp > 0) weapons?.DeviceHit(t, 0f, e.emp, dir, e.item.index);
                return was - hp.shield;
            }
            float dmg = t.netShield >= 0f ? amount : amount * e.damagePercent / 100f;
            weapons?.DeviceHit(t, dmg, e.emp, dir, e.item.index);
            return dmg;
        }

        // ---- the Rites' upgrades: the Hunt's fury (B), the Storm's shield steal (B), the Star's heal (B) ----------------
        float furyMs;
        GameObject furyFx;
        AudioSource furySound;

        /// <summary>The Rites' damage factor: x1.5 while the Hunt's fury lasts (BP_Player_Buff_Blink_UpgradeB).</summary>
        float RiteFury => furyMs > 0f ? Device.FuryFactor : 1f;

        void StartFury(Entry e)
        {
            furyMs = Device.FuryMs;
            if (weapons != null) weapons.DamageBoost = Mathf.Max(weapons.DamageBoost, Device.FuryFactor);
            // its look on the ship while it lasts (Blink B's buff: P_Muzzle_Blink_upgrade_B, its Damage_Buff loop)
            var look = LookOf(e);
            if (look == null) return;
            if (furyFx != null) Destroy(furyFx);
            furyFx = look.Play(look.spec.fury, transform.position, transform.rotation, Vector3.zero, transform);
            var clip = RiteLook.Pick(look.fury);
            if (clip == null) return;
            furySound ??= gameObject.AddComponent<AudioSource>();
            furySound.playOnAwake = false; furySound.spatialBlend = 0f; furySound.loop = true;
            furySound.clip = Modding.ModSounds.Get(clip);
            furySound.volume = Settings.SfxVolume;
            furySound.Play();
        }

        void EndFuryLook()
        {
            if (furyFx != null) Destroy(furyFx);
            furyFx = null;
            if (furySound != null) furySound.Stop();
        }

        /// <summary>One of a Rite's one-off upgrade looks on the ship ('absorb', 'heal') with its sound.</summary>
        void PlayOnShip(Entry e, System.Func<RiteLook, Data.CustomFx> part, System.Func<RiteLook, AudioClip[]> sound)
        {
            var look = LookOf(e);
            if (look == null) return;
            look.Play(part(look), transform.position, transform.rotation, Vector3.zero, transform);
            Play(RiteLook.Pick(sound(look)));
        }

        void UpdateFury(float dtMs)
        {
            if (furyMs <= 0f) return;
            furyMs -= dtMs;
            if (furyMs > 0f) return;
            EndFuryLook();
            if (weapons == null) return;
            // over: back to 1 unless the strike charge runs (it set its own)
            foreach (var o in devices) if (o.rules.type == Device.Type.StrikeCharge && o.rules.Active) return;
            weapons.DamageBoost = 1f;
        }

        /// <summary>Points onto the ship's own shield (the Storm's B: ShieldComponent.IncreaseShield), up to its maximum.</summary>
        void GainShield(float points)
        {
            var hp = self.hitpoints;
            if (hp == null || hp.maxShield <= 0 || points <= 0f) return;
            hp.shield = Mathf.Min(hp.maxShield, hp.shield + points);
        }

        /// <summary>A share of the hull's maximum repaired (the Star's B: HealthComponent.AddHealth).</summary>
        void HealShare(float share)
        {
            var hp = self.hitpoints;
            if (hp == null || hp.hull <= 0) return;
            hp.hull = Mathf.Min(hp.maxHull, hp.hull + Mathf.CeilToInt(hp.maxHull * share));   // (not Repair: the nanobots count theirs)
            self.hp = hp.hull;
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

        void ShowBeam(Target t, Target from = null)
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
            PlaceBeam(beam, from, t);
            beams.Add((beam, from, t, BeamShowMs));
        }

        /// <summary>A beam from 'from' (null: the ship) to 't'.</summary>
        void PlaceBeam(Transform beam, Target from, Target t)
        {
            var start = (object)from != null ? from.transform.position : transform.position;
            var d = t.transform.position - start;
            if (d.sqrMagnitude < 1e-6f) return;
            beam.SetPositionAndRotation(start, Quaternion.LookRotation(d, transform.up));
            beam.localScale = new Vector3(1f, 1f, d.magnitude / M);
        }
    }
}
