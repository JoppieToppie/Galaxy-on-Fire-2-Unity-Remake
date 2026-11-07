// EngineFlame.cs
// Remake mods: drives a mod ship's engine flames (materials with "flame", GoF2/ModEngineFlame; EVERSPACE 2's EngineExhaust
// sections): per renderer (a MaterialPropertyBlock on each flame submesh, so the ship's materials stay shared) the flame's
// backward direction in that renderer's object space (the ship root's -Z: the parts never turn against the root), and per
// frame EVERSPACE 2's inputs and the engines' on / off level: on while the ship's engine glow part
// (AssembledObject.playerVariantParts) is active (dark and pulled into the nozzles on a ship parked in a hangar, while
// mining or docking at an object, on a dead ship), fading over a quarter second. Without a glow part the flames are always on.
// EVERSPACE 2's inputs (2026-10-24, decompiled: BP_ShipBase's EngineVFX_Update / EaseEngineVFX, AESPawn::
// UpdatePrimativeDataParamsNative in the game's exe, research/engine_exhaust.md of the ES2 importer):
// - the linear thrust, a vector in the ship's frame: the player's ship (EngineOpacityControlledBy 1) the movement input
//   asked for (GetLastInputVector: free flight's keys, FlightModel.ThrustInput; the original flight's throttle and strafe),
//   eased toward it like EaseEngineVFX: VInterpTo at 16 / s rising and 8 / s falling with BP_Ship_Player's fixed 0.05 s step
//   per tick (80 % / 40 % of the way a frame, here per 60 fps frame); any other ship (0) its velocity in its own frame over
//   its top speed (clamped to length 1), at once (BP_ShipBase's update step 0.3 s: VInterpTo's alpha reaches 1); a parked
//   mod battleship forward at least at its idle drive (EngineDrive.Idle);
// - the boost, 1 while boosting (bIsBoosting, or the cruise drive: here a boost or a jump), else 0, eased the same; any other
//   ship's when it flies faster than its top speed;
// - SteeringOffset, the player's ship's velocity: each flame's tip bends against it, u^2 x -0.008 x min(|v|, 100 m/s) (in
//   EVERSPACE 2's cm: at most 80 cm on a 5 m flame; here 0.16 of the flame's length), only while thrusting forward (x the
//   thrust's forward share) (_SteerBend, world space).
// The flames read the thrust's forward part (_FlameThrust) and the boost (_FlameBoost); LateralThrusters and Es2EngineVfx
// read the rest (Input, Thrust, Boost, Boosting, Velocity, Pilot).
// A customized exhaust (ModExhausts: the hangar's Customize screen, any ship) brings flames of its own under a holder
// (ModExhausts.HolderName) with an EngineFlame of its own: the ship's EngineFlame leaves them alone and, 'suppressed', keeps
// the model's own flames shrunk into the nozzles and dark. Each of the holder's nozzles (ModExhausts.NozzleName) blows along
// its own backward axis (the mount's turn), the model's own flames along the ship's.

using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public class EngineFlame : MonoBehaviour
    {
        const float FadePerSecond = 4f;
        const float IdleThrust = 0.02f;   // a drive below this is idle: no flame
        const float MetersPerUnit = 0.05f;
        // EaseEngineVFX with BP_Ship_Player's EngineVFXUpdateRate 0.05 as the step: VInterpTo's alpha 0.05 x 16 = 0.8 rising,
        // 0.05 x 8 = 0.4 falling, per tick; as rates over a 60 fps frame
        internal static readonly float RiseRate = -Mathf.Log(1f - 0.8f) * 60f, FallRate = -Mathf.Log(1f - 0.4f) * 60f;
        internal const float SteerShare = 0.16f;       // -0.008 x 10000 cm/s = 80 cm of the 500 cm flame
        internal const float SteerTopSpeed = 100f;     // m/s (EVERSPACE 2's |SteeringOffset| clamp 10000 cm/s; the remake's top speed)

        static readonly int DirId = Shader.PropertyToID("_FlameDir");
        static readonly int ThrustId = Shader.PropertyToID("_FlameThrust");
        static readonly int BoostId = Shader.PropertyToID("_FlameBoost");
        static readonly int LevelId = Shader.PropertyToID("_FlameLevel");
        static readonly int OverrideId = Shader.PropertyToID("_BoostOverride");
        static readonly int SteerId = Shader.PropertyToID("_SteerBend");
        static readonly int TravelId = Shader.PropertyToID("_Travel");

        readonly List<(Renderer r, int index, Vector4 dir)> flames = new List<(Renderer, int, Vector4)>();
        /// <summary>The model's own flames stay off (a customized exhaust replaces them).</summary>
        public bool suppressed;
        /// <summary>A boost turns the main flames to the boost colour and the boost cards stay hidden (an exhaust style on a ship
        /// without boost jets of its own: the game's ships, other mods'; ModExhausts).</summary>
        public bool boostOverride;
        /// <summary>Meshes made for this ship (a customized exhaust's flames bent to its nozzles), freed with it.</summary>
        public readonly List<Mesh> ownedMeshes = new List<Mesh>();

        /// <summary>What the pilot asks for this frame in the ship's frame (x right, y up, z forward; -1..1; zero for any other
        /// ship): EVERSPACE 2's GetLastInputVector.</summary>
        public Vector3 Input { get; private set; }
        /// <summary>EVERSPACE 2's LinearThrustCurrent in the ship's frame: the eased input, or another ship's velocity over its
        /// top speed.</summary>
        public Vector3 Thrust { get; private set; }
        /// <summary>EVERSPACE 2's BoostCurrent, 0..1.</summary>
        public float Boost { get; private set; }
        /// <summary>The ship boosts (or travels) this frame.</summary>
        public bool Boosting { get; private set; }
        /// <summary>EVERSPACE 2's TravelMode custom primitive data (index 21), 0..1: the player's cruise (FlightModel.CruiseVisual,
        /// the cruise drive and the fast-forward cruise). The flame material stretches by 1 + 4 x it and turns to its
        /// TravelModeEngineColor (the shader's _Travel / _TravelColor), lit whatever the thrust, as ES2's travel mode does.</summary>
        public float Travel { get; private set; }
        /// <summary>The ship's velocity, world space, metres a second.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>The local player's ship (EVERSPACE 2's BP_Ship_Player: input-driven, eased; its engine lights, veil trails
        /// and manoeuvring jets).</summary>
        public bool Pilot { get; private set; }
        /// <summary>The engines run (the glow part on, not idle or off), 0..1 as the flames fade.</summary>
        public float Level => Mathf.Max(0f, level);
        /// <summary>In the docked station.</summary>
        public bool InStation => inStation;

        void OnDestroy()
        {
            foreach (var m in ownedMeshes) if (m != null) Destroy(m);
        }
        MaterialPropertyBlock block;
        EngineDrive drive;
        ShipController ship;
        GameObject engineGlow;
        bool ready, inStation, hasLast;
        float level = -1f;
        Vector3 lastPosition;

        /// <summary>Adds the component to a ship template (once).</summary>
        public static void Add(GameObject ship)
        {
            if (ship != null && ship.GetComponent<EngineFlame>() == null) ship.AddComponent<EngineFlame>();
        }

        /// <summary>Whether a material is a flame (a copy of the flame template, also a colour slot's recoloured copy).</summary>
        public static bool IsFlame(Material m) => m != null && m.shader != null && m.shader.name == "GoF2/ModEngineFlame";

        void OnEnable()
        {
            drive?.Reset();
            hasLast = false;
            ship = null;
            inStation = EngineDrive.IsStationModel(transform);
        }

        void Setup()
        {
            ready = true;
            block = new MaterialPropertyBlock();
            drive = new EngineDrive(transform);
            var asm = GetComponent<AssembledObject>();
            if (asm != null && asm.playerVariantParts != null)
                engineGlow = asm.playerVariantParts.FirstOrDefault(p => p != null && p.name.Contains("engine_glow"));
            bool holder = name == ModExhausts.HolderName;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!IsFlame(mats[i]) || r.name.StartsWith("engine_glow_thruster")) continue;   // LateralThrusters drives its own
                    if (!holder && ModExhausts.InHolder(r.transform)) continue;                     // a customized exhaust's own
                    var nozzle = holder ? ModExhausts.NozzleOf(r.transform) : null;
                    var back = nozzle != null ? -nozzle.forward : -transform.forward;
                    var d = r.transform.InverseTransformDirection(back).normalized;
                    flames.Add((r, i, new Vector4(d.x, d.y, d.z, 0f)));
                    // The stretched flame reaches past the mesh's bounds: grow them so it isn't culled with its stub (the
                    // steering bend's few metres in every direction too).
                    float reach = mats[i].GetFloat("_Length") * (1f + mats[i].GetFloat("_BoostLength")) * 1.2f;
                    var bounds = r.localBounds;
                    bounds.Encapsulate(bounds.min + d * reach);
                    bounds.Encapsulate(bounds.max + d * reach);
                    bounds.Expand(reach * SteerShare * 2f);
                    r.localBounds = bounds;
                }
            }
        }

        bool On => engineGlow == null || engineGlow.activeInHierarchy;

        void LateUpdate()
        {
            if (!ready) Setup();
            float dt = Time.deltaTime;
            float d = drive.Sample(dt, engineGlow);
            if (inStation && !drive.Previewing) d = Mathf.Min(d, 1f);
            UpdateInputs(dt, d);
            if (flames.Count == 0) return;
            // Off (free flight with no thrust) or idle (no thrust, the throttle at 0 or a ship at rest): the flames shrink
            // into the nozzles, as EVERSPACE 2's do (they kept a short flame at idle before).
            float target = On && !drive.Off && !suppressed && d > IdleThrust ? 1f : 0f;
            level = level < 0f ? target : Mathf.MoveTowards(level, target, Time.unscaledDeltaTime * FadePerSecond);
            // SteeringOffset (the player's ship only, EngineVFXApplySteeringOffset): against the velocity, x the thrust's
            // forward share (sat(Thrust.x / |Thrust|)), as a share of the flame's length
            Vector4 steer = Pilot && !inStation ? (Vector4)SteerBend(Velocity, Thrust) : Vector4.zero;
            foreach (var (r, index, dir) in flames)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block, index);
                block.SetVector(DirId, dir);
                block.SetFloat(ThrustId, Mathf.Max(0f, Thrust.z));
                block.SetFloat(BoostId, Boost);
                block.SetFloat(LevelId, level);
                block.SetFloat(OverrideId, boostOverride ? 1f : 0f);
                block.SetVector(SteerId, steer);
                block.SetFloat(TravelId, Travel);
                r.SetPropertyBlock(block, index);
            }
        }

        /// <summary>What the player asks of the ship in its frame (x right, y up, z forward; -1..1): free flight's keys
        /// (FlightModel.ThrustInput), the original flight's throttle and strafe. EVERSPACE 2's GetLastInputVector.</summary>
        internal static Vector3 PilotInput(ShipController ship)
        {
            var m = ship.Model;
            if (ship.FreeFlightActive && !ship.externalControl && !ship.inputLocked) return m.ThrustInput;
            float side = Mathf.Abs(m.StrafeVelocity) > 0.05f ? Mathf.Sign(m.StrafeVelocity) : 0f;
            return new Vector3(side, 0f, m.Braking ? 0f : m.Throttle);
        }

        /// <summary>EVERSPACE 2's SteeringOffset (EngineVFXApplySteeringOffset) as the flame tip's offset, world space, a share
        /// of the flame's length: against 'velocity' (world, m/s), x the forward share of 'thrust' (the ship's frame).</summary>
        internal static Vector3 SteerBend(Vector3 velocity, Vector3 thrust)
        {
            float mag = thrust.magnitude;
            float share = mag > 1e-5f ? Mathf.Clamp01(thrust.z / mag) : 0f;
            return Vector3.ClampMagnitude(velocity / SteerTopSpeed, 1f) * (-SteerShare * share);
        }

        /// <summary>EVERSPACE 2's EngineVFX_Update and EaseEngineVFX for this frame ('d' the engine drive, 0..2).</summary>
        void UpdateInputs(float dt, float d)
        {
            if (ship == null) ship = GetComponentInParent<ShipController>();
            var p = transform.position;
            if (hasLast && dt > 0f)
            {
                var v = (p - lastPosition) / dt;
                // A jump or respawn moves the ship in one frame: ignore it.
                if (v.magnitude < FlightModel.BaseSpeed * 1000f * MetersPerUnit * 20f)
                    Velocity = Vector3.Lerp(Velocity, v, 1f - Mathf.Exp(-dt * 20f));
            }
            lastPosition = p;
            hasLast = true;
            var m = ship != null ? ship.Model : null;
            Pilot = m != null && !inStation;
            bool engines = On && !drive.Off && !suppressed;
            Vector3 thrustTarget, input = Vector3.zero;
            float boostTarget;
            if (inStation)
            {
                // Docked: the hangar flights and the Customize screen's preview (EngineDrive's 0..2 as the forward thrust)
                thrustTarget = new Vector3(0f, 0f, Mathf.Clamp01(d));
                boostTarget = d > 1f ? 1f : 0f;
            }
            else if (m != null)
            {
                input = PilotInput(ship);
                if (!engines) input = new Vector3(input.x, input.y, Mathf.Min(0f, input.z));
                thrustTarget = input;
                boostTarget = d > 1f ? 1f : 0f;   // a boost, the cruise mode, a jump
            }
            else
            {
                // EngineOpacityControlledBy 0: the velocity in the ship's frame over the top speed without a boost
                float top = FlightModel.BaseSpeed * 1000f * MetersPerUnit;
                thrustTarget = Vector3.ClampMagnitude(transform.InverseTransformDirection(Velocity) / top, 1f);
                if (thrustTarget.z < drive.Idle) thrustTarget.z = drive.Idle;
                boostTarget = Velocity.magnitude > top * 1.05f ? 1f : 0f;
                if (!engines) thrustTarget = Vector3.zero;
            }
            Input = input;
            Boosting = boostTarget > 0f;
            Travel = Pilot && m != null ? m.CruiseVisual : 0f;   // ES2's TravelMode: the player's cruise
            if (Pilot)
            {
                // VInterpTo toward the target, faster while the target is the bigger
                float kT = 1f - Mathf.Exp(-dt * (thrustTarget.magnitude > Thrust.magnitude ? RiseRate : FallRate));
                Thrust = Vector3.Lerp(Thrust, thrustTarget, kT);
                float kB = 1f - Mathf.Exp(-dt * (boostTarget > Boost ? RiseRate : FallRate));
                Boost = Mathf.Lerp(Boost, boostTarget, kB);
            }
            else { Thrust = thrustTarget; Boost = boostTarget; }
        }
    }
}
