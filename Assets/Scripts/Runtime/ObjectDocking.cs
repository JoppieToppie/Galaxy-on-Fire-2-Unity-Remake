// ObjectDocking.cs
// Docking at a story object in space (PlayerEgo::dockToDockingPoint 0xabf60 / approachDockingPoint 0xac250 / update
// 0xa8ed0, MGame::OnTouchBegin 0x1a838c; decoded here, not in the research files): a docking target (a static object with
// docking type 1-3 and a space-point set, SpacePoints) locked in the centre like a landmark; the action (or its autopilot
// menu entry) docks, pressing it again while docked undocks.
//   approach   state 0: the autopilot (moveToPosition, turn min(H + 2.7, 4)) to the object's nearest free approach point
//              (type 1); there state 2: sound 0x8de (Docking_Landing), the engine sound off, the camera a look-at camera,
//              and a 2000 ms ease-in to the docking point (type 2) nearest that approach point
//   docked     state 1, by the object's docking type and the level mission (Status+400):
//              2 + 0xb8   evacuees aboard (Status+0x174), 1 per 1500 ms up to min(cabins - aboard, value - aboard); no
//                         cabins -> 0x2b "No passenger cabin" (3203); "Loading" (3204) N / M, then "Transfer complete" (3200)
//              2 + 0xa8   the cargo bots: value - aboard units, 1 per 1500 ms into Status+0x174
//              1 + 0xb8   unloading: 1 per 1500 ms, aboard - 1 and the mission value - 1 ("Unloading", 3205)
//              1 + 0xae   the campaign mission's goods out of the hold, 1 per 1000 ms, value + 1 (the mining plant's titanium)
//              3          the hacking game (HackingGame(0, 4), (0, 1) at campaign 91)
//   undock     state 3: back out to the approach point, then the chase camera behind the ship and the controls back
//   docked pose (approachDockingPoint state 2): nose = the docking point's direction (SpacePoints), up = from the docking
//              point toward the approach point, eased in over 2000 ms; it stops when within the ship's hangar height
//              (DAT_00252204 = StationTables.ShipY) of the point, so its pivot rests that far up the approach line.
// Remake picks: the undock eases back to the approach point over 1500 ms instead of flying there; the approach reaches its point within
// PlayerEgo+0x1d8 = 0x578 (1400) units, and a ship circling it (its turning circle is ~2500 units across at 2 u/ms) starts
// the ease-in after 4 s within 4000 units.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ObjectDocking : MonoBehaviour
    {
        public enum Phase { Idle, Approach, Entering, Docked, Leaving }
        public const int DropOff = 1, Pickup = 2, Hackable = 3;
        /// <summary>Remake: the carrier's resupply dock (World.CapitalShips; the flight HUD's resupply menu while docked).</summary>
        public const int Resupply = 4;

        const float M = 0.05f;
        const float EnterMs = 2000f, LeaveMs = 1500f, ApproachReachUnits = 1400f;
        const float CircleUnits = 4000f, CircleMs = 4000f;
        float circleMs;
        const float PassengerMs = 1500f, OreMs = 1000f;

        public Phase State { get; private set; } = Phase.Idle;
        /// <summary>The object docked at / being docked at.</summary>
        public NpcShip Target { get; private set; }
        public bool IsDocked => State == Phase.Docked;
        /// <summary>PlayerEgo::isDockedToDockingPoint for the NPC guns (x0.75 damage).</summary>
        public static bool PlayerDocked { get; private set; }
        void OnDestroy() => PlayerDocked = false;
        public bool Busy => State != Phase.Idle;
        /// <summary>The hacking game while docked at a type-3 object (PlayerEgo+0x1e8), null otherwise.</summary>
        public HackingGame Hacking { get; private set; }
        /// <summary>PlayerEgo::hackingWon: docked and the game won.</summary>
        public bool HackingWon => IsDocked && Hacking != null && Hacking.Won;
        /// <summary>The HUD's transfer counter ("Loading 3 / 10"), null = none.</summary>
        public string TransferLabel { get; private set; }
        public int TransferDone { get; private set; }
        public int TransferTotal { get; private set; }
        public event Action<string> Message;
        /// <summary>Docked at a hackable object's won game (the generic hidden-blueprint wrecks use it).</summary>
        public event Action<NpcShip> HackWon;

        public string PromptText =>
            State == Phase.Docked ? Localization.Extra("hudUndock", "UNDOCK")
            : State == Phase.Approach ? Localization.Extra("hudAutopilotOff", "AUTOPILOT OFF")
            : null;

        Database db;
        ShipController ship;
        ChaseCamera chase;
        WeaponSystem weapons;
        CombatAudio sounds;
        AudioSource sfx;
        AudioClip dockSound, turnSound, solvedSound;
        Vector3 approachLocal, dockLocal, dockDirLocal, pivotLocal;
        Transform takenObject;   // the approach point reserved (SpacePoints.Take) while approaching / docked
        int takenPoint = -1;
        Vector3 fromPos, toPos;
        Quaternion fromRot, toRot;
        float phaseMs, tickMs;
        bool noCabinShown, transferDoneShown, hackWonReported;

        public void Setup(Database database, ShipController controller, ChaseCamera chaseCamera, WeaponSystem weaponSystem)
        {
            db = database;
            ship = controller;
            chase = chaseCamera;
            weapons = weaponSystem;
            sounds = CombatAudio.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            var sn = SupernovaAssets.Load();
            if (sn != null) { dockSound = sn.docking; turnSound = sn.hackingTurn; solvedSound = sn.hackingSolved; }
        }

        // ---- start / stop -------------------------------------------------------------------------------------

        /// <summary>PlayerEgo::dockToDockingPoint with a new target: the approach (state 0).</summary>
        public bool Dock(NpcShip target)
        {
            if (target == null || target.DockingType <= 0 || State != Phase.Idle) return false;
            if (target.DockingType == Resupply)
            {
                string refusal = CapitalShips.DockRefusal(target);
                if (refusal != null) { Say(refusal); return false; }
            }
            var points = SpacePoints.Set(target.SpacePointSet);
            if (!NearestPoints(target, points)) return false;
            Target = target;
            if (takenPoint >= 0) { takenObject = target.transform; SpacePoints.Take(takenObject, takenPoint); }
            State = Phase.Approach;
            weapons?.ResetGunDelay();   // PlayerEgo::approachDockingPoint
            phaseMs = 0f;
            ship.SetThrottle(1f);
            ship.autopilotTarget = () => Target != null ? Target.transform.TransformPoint(approachLocal) : ship.transform.position;
            Say(Localization.Get(3199));   // 0x22
            Play(sounds?.autopilotOn);
            return true;
        }

        /// <summary>The action while approaching (autopilot off) or docked (PlayerEgo::dockToDockingPoint again: undock).</summary>
        public void Interact()
        {
            if (State == Phase.Approach) { Cancel(); Say(Localization.Get(571) + " " + Localization.Get(39)); Play(sounds?.autopilotOff); }
            else if (State == Phase.Docked) Undock();
        }

        /// <summary>A level script undocks the player (Level scripts: "player undocked if docked").</summary>
        public void Undock()
        {
            if (State != Phase.Docked && State != Phase.Entering) return;
            Hacking = null;
            TransferLabel = null;
            State = Phase.Leaving;
            phaseMs = 0f;
            fromPos = ship.transform.position;
            fromRot = ship.transform.rotation;
            toPos = Target != null ? Target.transform.TransformPoint(approachLocal) : fromPos;
            toRot = fromRot;
        }

        /// <summary>A level script takes the ship back at once (a cutscene starting, the object leaving): an approach is
        /// called off, a docked ship is put back on its approach point with the controls back.</summary>
        public void Abort()
        {
            if (State == Phase.Idle) return;
            if (State == Phase.Approach) { Cancel(); return; }
            if (Target != null) ship.transform.position = Target.transform.TransformPoint(approachLocal);
            Release();
        }

        void Cancel()
        {
            ship.autopilotTarget = null;
            Target = null;
            State = Phase.Idle;
            FreePoint();
        }

        void FreePoint()
        {
            if (takenPoint >= 0) SpacePoints.Free(takenObject, takenPoint);
            takenObject = null;
            takenPoint = -1;
        }

        /// <summary>The nearest free approach point to the player (KIPlayer::getNearestNavigationPoint; one another ship has
        /// taken only when every one is taken) and the docking point nearest that (object-local Unity).</summary>
        bool NearestPoints(NpcShip target, List<SpacePoints.Point> points)
        {
            float best = float.MaxValue;
            bool found = false, free = false;
            takenPoint = -1;
            var local = target.transform.InverseTransformPoint(ship.transform.position);
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (p.type != SpacePoints.Approach) continue;
                bool isFree = !SpacePoints.IsTaken(target.transform, i);
                if (free && !isFree) continue;
                var l = ToLocal(p.engine);
                float d = (l - local).sqrMagnitude;
                if (d < best || (isFree && !free)) { best = d; approachLocal = l; found = true; free = isFree; takenPoint = isFree ? i : -1; }
            }
            if (!found) return false;
            best = float.MaxValue;
            found = false;
            foreach (var p in points)
            {
                if (p.type != SpacePoints.Dock) continue;
                var l = ToLocal(p.engine);
                float d = (l - approachLocal).sqrMagnitude;
                if (d < best) { best = d; dockLocal = l; dockDirLocal = SpacePoints.DirToLocal(p.dir); found = true; }
            }
            return found;
        }

        static Vector3 ToLocal(Vector3 engine) => SpacePoints.ToLocal(engine);

        // ---- per frame --------------------------------------------------------------------------------------

        void Update()
        {
            if (State == Phase.Idle || db == null) return;
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            if (Target == null || !Target.Target.Alive || Target.Gone)
            {
                if (State == Phase.Approach) Cancel(); else if (State != Phase.Leaving) Undock();
                if (State == Phase.Leaving && Target == null) Release();
            }
            switch (State)
            {
                case Phase.Approach: UpdateApproach(); break;
                case Phase.Entering: UpdateEntering(dtMs); break;
                case Phase.Docked: UpdateDocked(dtMs); break;
                case Phase.Leaving: UpdateLeaving(dtMs); break;
            }
        }

        void LateUpdate()
        {
            PlayerDocked = IsDocked;
            // TargetFollowCamera::setLookAtCam(true): the camera stays where it is and keeps the ship in view.
            if (State != Phase.Entering && State != Phase.Docked && State != Phase.Leaving) return;
            if (PlayerTurret.ViewActive(gameObject)) return;   // the turret view's camera (docked: the original allows it)
            var cam = Camera.main;
            if (cam != null && chase != null && !chase.enabled)
                cam.transform.rotation = Quaternion.LookRotation(ship.transform.position - cam.transform.position, Vector3.up);
        }

        void UpdateApproach()
        {
            if (Target == null) return;
            var at = Target.transform.TransformPoint(approachLocal);
            float d = (at - ship.transform.position).magnitude / M;
            circleMs = d < CircleUnits ? circleMs + Time.deltaTime * 1000f : 0f;
            if (d > ApproachReachUnits && circleMs < CircleMs) return;
            circleMs = 0f;
            // State 2: the ease-in.
            ship.autopilotTarget = null;
            ship.externalControl = true;
            if (weapons != null) weapons.Blocked = true;
            State = Phase.Entering;
            phaseMs = 0f;
            fromPos = ship.transform.position;
            fromRot = ship.transform.rotation;
            // approachDockingPoint state 2: up = docking point -> approach point, nose = the point's direction (rotated by the
            // object); docked within the ship's height of the point (DAT_00252204), on that line.
            var upLocal = approachLocal - dockLocal;
            upLocal = upLocal.sqrMagnitude > 1e-8f ? upLocal.normalized : Vector3.up;
            pivotLocal = dockLocal + upLocal * (StationTables.ShipY(Session.ShipIndex) * M);
            toPos = Target.transform.TransformPoint(pivotLocal);
            var q = Target.transform.rotation;
            var nose = q * dockDirLocal;
            toRot = Quaternion.LookRotation(nose.sqrMagnitude > 1e-6f ? nose : ship.transform.forward, q * upLocal);
            if (chase != null) chase.enabled = false;
            SetExhaust(false);
            // Remake: no sound at the carrier (Docking_Landing sounded like mining there; nothing fitting yet).
            if (Target.DockingType != Resupply) Play(dockSound);
        }

        void UpdateEntering(float dtMs)
        {
            phaseMs += dtMs;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseMs / EnterMs));
            if (Target != null) toPos = Target.transform.TransformPoint(pivotLocal);   // the object may drift (freighters)
            ship.transform.SetPositionAndRotation(Vector3.Lerp(fromPos, toPos, t), Quaternion.Slerp(fromRot, toRot, t));
            ship.ExternalSpeedMetersPerSecond = 0f;
            if (phaseMs < EnterMs) return;
            State = Phase.Docked;
            weapons?.ResetGunDelay();   // PlayerEgo::dockToDockingPoint
            phaseMs = tickMs = 0f;
            noCabinShown = transferDoneShown = hackWonReported = false;
            TransferLabel = null;
            if (Target.DockingType == Hackable) StartHacking();
        }

        void UpdateDocked(float dtMs)
        {
            if (Target != null) ship.transform.position = Target.transform.TransformPoint(pivotLocal);
            ship.ExternalSpeedMetersPerSecond = 0f;
            if (Target == null) return;
            if (Target.DockingType == Hackable && Hacking == null) StartHacking();
            // A level script turned the hacked object into a transfer target (139: the prism's cargo bots).
            if (Hacking != null && Hacking.Won && Target.DockingType != Hackable) Hacking = null;
            if (Hacking != null)
            {
                Hacking.Update(dtMs);
                if (Hacking.Won && !hackWonReported) { hackWonReported = true; HackWon?.Invoke(Target); }
                return;
            }
            Transfer(dtMs);
        }

        void StartHacking()
        {
            Hacking = new HackingGame(Session.CampaignMission == 91 ? 1 : 4, Target != null ? Target.DockIndex : -1);
            Hacking.Turned += () => Play(turnSound);
            Hacking.SolvedNow += () => Play(solvedSound);
        }

        /// <summary>PlayerEgo::update's docked transfers (see the header).</summary>
        void Transfer(float dtMs)
        {
            var level = Story.IsLevelMission(Session.StationIndex) ? Story.Mission : null;
            var campaign = Story.Mission;
            int type = Target.DockingType;
            if (level != null && (level.type == StoryType.Passengers || level.type == StoryType.Counter) && type == Pickup)
            {
                int aboard = Session.StoryCounter;
                int room;
                if (level.type == StoryType.Counter) room = level.value - aboard;
                else
                {
                    int cabins = Freelance.MaxPassengers(db);
                    if (cabins < 1) { if (!noCabinShown) Say(Localization.Get(3203)); noCabinShown = true; TransferLabel = null; return; }
                    room = Mathf.Min(cabins - aboard, level.value - aboard);
                }
                Tick(dtMs, PassengerMs, room, Localization.Get(3204), () => Session.StoryCounter++);
            }
            else if (level != null && level.type == StoryType.Passengers && type == DropOff)
            {
                Tick(dtMs, PassengerMs, Session.StoryCounter, Localization.Get(3205), () =>
                {
                    Session.StoryCounter--;
                    level.value = Mathf.Max(0, level.value - 1);
                });
            }
            else if (campaign != null && campaign.type == StoryType.AmountReached && type == DropOff && campaign.goodsItem >= 0
                     && Session.StationIndex == campaign.station)
            {
                int left = Mathf.Min(Story.CargoOf(campaign.goodsItem), campaign.goodsAmount - campaign.value);
                Tick(dtMs, OreMs, left, Localization.Get(3205), () =>
                {
                    Shop.RemoveFromCargo(campaign.goodsItem, 1);
                    campaign.value++;
                });
            }
            else if (Freelance.Active && Freelance.Mission.type == MissionType.OreMining && type == DropOff && Target.Spec.fixedObject != null
                     && Session.StationIndex == Freelance.Mission.target)
            {
                // Freelance 15 Ore Mining (Objective 0x1c: delivered ore >= amount): 1 t per 1000 ms, counted in the mission's
                // status.
                var m = Freelance.Mission;
                int left = Mathf.Min(Story.CargoOf(m.good), m.amount - m.status);
                Tick(dtMs, OreMs, left, Localization.Get(3205), () =>
                {
                    Shop.RemoveFromCargo(m.good, 1);
                    m.status++;
                    GoF2Remake.Multiplayer.NetMissions.AddStatus(m, 1);   // multiplayer: the squad's delivered ore
                });
            }
            else TransferLabel = null;
        }

        /// <summary>One unit per 'everyMs' while 'left' &gt; 0; the HUD counter; "Transfer complete" (3200) once at the end.</summary>
        void Tick(float dtMs, float everyMs, int left, string label, Action one)
        {
            if (left <= 0)
            {
                if (TransferLabel != null && !transferDoneShown) { Say(Localization.Get(3200)); transferDoneShown = true; }
                TransferLabel = null;
                return;
            }
            if (TransferLabel == null) { TransferDone = 0; TransferTotal = left; transferDoneShown = false; }
            TransferLabel = label;
            TransferTotal = Mathf.Max(TransferTotal, TransferDone + left);
            tickMs += dtMs;
            if (tickMs < everyMs) return;
            tickMs -= everyMs;
            one();
            TransferDone++;
        }

        void UpdateLeaving(float dtMs)
        {
            phaseMs += dtMs;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseMs / LeaveMs));
            ship.transform.position = Vector3.Lerp(fromPos, toPos, t);
            ship.ExternalSpeedMetersPerSecond = 0f;
            if (phaseMs >= LeaveMs) Release();
        }

        /// <summary>PlayerEgo state 3 done: LevelScript::resetCamera, the controls back.</summary>
        void Release()
        {
            State = Phase.Idle;
            Target = null;
            FreePoint();
            Hacking = null;
            TransferLabel = null;
            ship.externalControl = false;
            ship.autopilotTarget = null;
            SetExhaust(true);
            if (weapons != null) weapons.Blocked = false;
            if (chase != null) { chase.enabled = true; chase.Snap(); }
        }

        /// <summary>The player's engine glow (as Mining: the player-variant parts of the model).</summary>
        void SetExhaust(bool on)
        {
            var asm = ship.visualModel != null ? ship.visualModel.GetComponent<AssembledObject>() : null;
            if (asm?.playerVariantParts != null) foreach (var p in asm.playerVariantParts) if (p != null) p.SetActive(on);
        }

        void Say(string text) => Message?.Invoke(text);

        void Play(AudioClip clip)
        {
            if (clip != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), Settings.SfxVolume);
        }
    }
}
