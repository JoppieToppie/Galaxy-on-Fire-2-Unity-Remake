// Es2Dock.cs
// Remake mods: EVERSPACE 2's docking and take-off at a station whose hangar is "dock": "es2" (ModInteriors): the hangar is
// the station's own model, so the ship docks onto its pad in the station's orbit and takes off from it there, as ES2 does,
// instead of GoF2's cut to the hangar and its hangar flights. From BP_DockableStation's decompiled blueprint (CUE4Parse's
// Kismet bytecode; its timelines' curves from the blueprint's CurveFloat exports):
// - Docking (BeginDocking -> PlayDockingAnimation): the view cuts to the dock's CineCamera (SetViewTargetWithBlend(self, 0)),
//   on its spring arm, which looks straight at the ship every frame (CameraLookAtShip(false): FindLookAtRotation to the
//   ship + CameraLookAtOffsetZ in the arm's frame), while the ShipDocking timeline (5 s) moves the ship from where it was
//   (InitialShipTransformLocal) onto its docked transform (DockingTransform, the ship's bounds centre on it):
//   TLerp(initial, docked, TransformAlpha); the BankingNode wobbles by ApplyRandomBanking(BankingAlpha x 30 x
//   InitialDistanceFactor) (roll sin(2 T) x, pitch sin(1.5 (T + 0.5)) y, yaw sin(T + 0.75) z, T the game time),
//   InitialDistanceFactor = clamp(|initial| / 4000 cm); the camera sways by BP_CamShake_SteadyCam. Its menu shows at 2 s;
//   the remake opens the station's when the ship has landed (4 s), over the live orbit (SpaceLevel.DockInOrbit: the ship
//   stays on the pad, Phase.Docked).
// - Take-off (PlayLaunchAnimation -> TakeOffTimeline, 3 s): from the docked transform the ship climbs TakeOffHeight (500 cm)
//   turning to face the take-off point (Align, by 1.5 s), then flies to TakeOffTransform (Launch, 0.75 .. 3 s, accelerating):
//   TLerp(TLerp(docked, climb, Align), takeoff, Launch); banking Banking x 10 (roll only); the camera eases toward the ship
//   (CameraLookAtShip(true): REase by clamp(dt x 8), ease in-out exponent 2); at 1 s the boost starts and the view blends
//   from the dock's camera to the ship's over 3 s (SetViewTargetWithBlend(pawn, 3, VTBlend_EaseInOut, 4)); at the end the
//   player flies on.
// - Its lights (ToggleLights, from the docking's start until the take-off ends): SpotLight_Top 30 m above the dock pointing
//   straight down and SpotLight_Fill, cyan, on a pivot turned with the dock's camera each frame (SpotLight_Fill_Pivot's
//   world rotation = the CineCamera's), 5 m ahead, 20 m right, 5 m below the pivot, aimed back at it.
// - The landing dust (P_Vehicle_LandingDust, active from the docking's start to its ToggleDust at 4 s): each frame a trace
//   50 m down the ship's banked up from its model finds the surface under it; the dust sits there, Amount = 1 - its
//   distance / 50 m, Velocity = the ship's up across the dock's plane x -50 m/s. The Cascade emitter: 200 x Amount a second,
//   0.5 .. 1 s, on a 5 m circle in the dock's plane flying out at 3 .. 4 x its radius a second + Velocity, rising 10 m/s^2,
//   5 .. 10 m sprites of a random frame of the 6 x 6 smoke flipbook, colour 0.2, alpha 0 -> 0.1 (half way) -> 0 x Amount.
// Points come from the room's markers (room metres) through the station model's "hull" (ModStations), whose frame is the
// room's: W(marker) = hull.TransformPoint(marker / room scale).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    [DefaultExecutionOrder(5000)]   // after the chase camera: the take-off blends from the dock's camera into its pose
    public class Es2Dock : MonoBehaviour
    {
        public enum Phase { None, Docking, TakeOff, Docked }

        public Phase State { get; private set; }
        /// <summary>The docking or the take-off runs (a cinematic: no HUD, no controls, no collision).</summary>
        public bool Running => State == Phase.Docking || State == Phase.TakeOff;
        /// <summary>The ship rests on the pad (docked inside the orbit: the station's menu runs over it, SpaceLevel).</summary>
        public bool Docked => State == Phase.Docked;
        /// <summary>The station model's hull (its frame is the room's, scaled by the room's "scale").</summary>
        public Transform Hull => hull;
        public ModInteriors.Room Room => room;
        /// <summary>The station this dock is (the orbit's, or a mobile station's).</summary>
        public int StationIndex { get; private set; }
        /// <summary>This is a manually authored large-ship exterior attachment, not an EVERSPACE 2 hangar pad.</summary>
        public bool IsExternal { get; private set; }
        public Vector3 DockedPosition => DockedPos;
        public Quaternion DockedRotation => DockedRot;

        /// <summary>A point that follows the dock zone (a mobile station's landmark for the autopilot).</summary>
        public Transform MakeDockPoint()
        {
            var t = new GameObject("ES2 dock").transform;
            t.SetParent(hull, false);
            t.localPosition = room.dockZone / Mathf.Max(0.001f, room.def.scale);
            return t;
        }
        /// <summary>The take-off has handed the ship to the player.</summary>
        public event Action TookOff;

        ModInteriors.Room room;
        Transform hull;
        ShipController ship;
        ChaseCamera chase;
        Camera cam;
        Action docked;
        float t, distanceFactor, blendT = -1f, lastFov;
        Vector3 startPos, climbPos, hullCentre, lastPos, chasePos;
        Quaternion startRot, climbRot, chaseRot;
        float chaseFov;
        bool boosted, released;
        Light topLight, fillLight;
        DustFx dust;

        /// <summary>The dock of this orbit's station ('station': the spawned station object), or null when the station's hangar
        /// isn't "dock": "es2" or its model isn't the mod's (a fallback look).</summary>
        public static Es2Dock Attach(GameObject station, int stationIndex, ShipController ship, ChaseCamera chase)
        {
            var room = ModInteriors.Es2DockOf(stationIndex);
            // (a station's model: its "hull" child; a mobile station's: under its "Station")
            var hull = station != null ? station.transform.Find("hull") ?? station.transform.Find("Station/hull") : null;
            if (room == null || hull == null || ship == null || chase == null) return null;
            var d = ship.gameObject.AddComponent<Es2Dock>();
            d.room = room;
            d.hull = hull;
            d.StationIndex = stationIndex;
            d.ship = ship;
            d.chase = chase;
            d.cam = chase.GetComponent<Camera>();
            return d;
        }

        /// <summary>Attach a large ship to a station's manually placed exterior point using its slotType 8 hardpoint.</summary>
        public static Es2Dock AttachExternal(GameObject station, int stationIndex, ShipController ship, ChaseCamera chase,
                                             Transform point, WeaponMount mount)
        {
            if (station == null || point == null || ship == null || chase == null || mount == null) return null;
            // The external point is authored in the station root's orbit frame, not the NPC pad or interior hull frame.
            var hull = station.transform;
            Es2Dock d = null;
            foreach (var existing in ship.GetComponents<Es2Dock>())
                if (existing.IsExternal) { d = existing; break; }
            if (d == null) d = ship.gameObject.AddComponent<Es2Dock>();
            d.hull = hull;
            d.StationIndex = stationIndex;
            d.ship = ship;
            d.chase = chase;
            d.cam = chase.GetComponent<Camera>();
            d.external = true;
            d.externalPoint = point;
            d.externalMount = mount;
            d.IsExternal = true;
            return d;
        }

        bool external;
        Transform externalPoint;
        WeaponMount externalMount;
        Vector3 externalTakeOffPosition;
        const float ExternalDockSeconds = 2.5f, ExternalTakeOffSeconds = 1.8f;
        float Scale => hull.lossyScale.x / Mathf.Max(0.001f, room != null ? room.def.scale : 1f);
        Vector3 W(Vector3 roomPoint) => hull.TransformPoint(roomPoint / Mathf.Max(0.001f, room.def.scale));
        Quaternion Facing(Vector3 from, Vector3 to) => Quaternion.LookRotation(W(to) - W(from), hull.up);

        /// <summary>Where the autopilot flies to dock: the dock zone's centre.</summary>
        public Vector3 DockPoint => W(room.dockZone);

        /// <summary>BP_DockableStation's Interact box (TriggerBoxExtent around the dock, its own axes): the ship may dock.</summary>
        public bool InZone(Vector3 p)
        {
            var local = Quaternion.Inverse(Facing(room.ship, room.shipAhead)) * (p - DockPoint);
            var half = room.def.dockZone * Scale;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        Quaternion DockedRot => external
            ? externalPoint.rotation * Quaternion.Inverse(Modding.ModHardpoints.MountTurn(externalMount))
            : Facing(room.ship, room.shipAhead);
        Vector3 DockedPos => external
            ? externalPoint.position - DockedRot * WeaponSystem.MountToLocal(externalMount)
            : W(room.ship) - DockedRot * hullCentre;   // GetShipDockingTransform: the bounds' centre on it
        Vector3 LookAt(Vector3 shipPos, Quaternion shipRot) => shipPos + shipRot * hullCentre + (W(room.cameraTarget) - W(room.ship));

        /// <summary>BeginDocking: the ship glides onto the pad under the dock's camera; 'landed' runs when it is down (the
        /// station's scene).</summary>
        public void BeginDocking(Action landed)
        {
            if (Running) return;
            docked = landed;
            startPos = ship.transform.position;
            startRot = ship.transform.rotation;
            // InitialDistanceFactor: the start's distance from the dock in ES2 centimetres / 4000
            distanceFactor = Mathf.Clamp01((startPos - W(room.pad)).magnitude / Mathf.Max(0.001f, hull.lossyScale.x) * 100f / 4000f);
            Begin(Phase.Docking);
            PlaySound(room.def.dockSound);   // PlayDockingAnimation: DockSound (S_SC_Docking) at the ship
            cam.transform.rotation = lastStationRot = Quaternion.LookRotation(LookAt(startPos, startRot) - cam.transform.position, hull.up);
        }

        /// <summary>Approach and attach to a manually authored station exterior point; the orbit camera remains active.</summary>
        public void BeginExternalDocking(Action landed)
        {
            if (!external || Running || Docked) return;
            docked = landed;
            startPos = ship.transform.position;
            startRot = ship.transform.rotation;
            State = Phase.Docking;
            t = 0f;
            released = false;
            ship.externalControl = true;
            ship.inputLocked = true;
            ship.modelHeld = true;
            ship.autopilotTarget = null;
            chase.enabled = true;
        }

        /// <summary>External test spawn: attach immediately, without an approach animation.</summary>
        public void HoldExternalDocked()
        {
            if (!external) return;
            State = Phase.Docked;
            t = 0f;
            released = false;
            ship.externalControl = true;
            ship.inputLocked = true;
            ship.modelHeld = true;
            ship.autopilotTarget = null;
            chase.enabled = true;
            ship.transform.SetPositionAndRotation(DockedPos, DockedRot);
        }

        /// <summary>PlayLaunchAnimation: the ship on its pad takes off under the dock's camera, then the player flies.</summary>
        /// <summary>Docked on the pad at once (a save or a story move into the station: no docking flight).</summary>
        public void HoldDocked()
        {
            Begin(Phase.Docked);
            ship.transform.SetPositionAndRotation(DockedPos, DockedRot);
            cam.transform.rotation = lastStationRot = Quaternion.LookRotation(LookAt(DockedPos, DockedRot) - cam.transform.position, hull.up);
        }

        public void BeginTakeOff()
        {
            if (Running) return;
            if (external) { BeginExternalTakeOff(); return; }
            hullCentre = ship.visualModel != null ? PlayerHull.HullBounds(ship.transform, ship.visualModel).center : Vector3.zero;
            ship.transform.SetPositionAndRotation(DockedPos, DockedRot);
            startPos = ship.transform.position;
            startRot = ship.transform.rotation;
            climbPos = startPos + hull.up * (room.def.rise * Scale);
            climbRot = Quaternion.LookRotation(W(room.takeoff) - climbPos, hull.up);
            Begin(Phase.TakeOff);
            PlaySound(room.def.undockSound);   // UndockSound (S_SC_Undocking) at the ship
            cam.transform.rotation = lastStationRot = Quaternion.LookRotation(LookAt(startPos, startRot) - cam.transform.position, hull.up);
        }

        void Begin(Phase phase)
        {
            hullCentre = ship.visualModel != null ? PlayerHull.HullBounds(ship.transform, ship.visualModel).center : Vector3.zero;
            State = phase;
            t = 0f;
            blendT = -1f;
            boosted = released = false;
            lastPos = ship.transform.position;
            ship.externalControl = true;
            ship.inputLocked = true;
            ship.modelHeld = true;
            ship.autopilotTarget = null;
            chase.enabled = false;
            cam.transform.position = W(room.camera);
            ShowLights(true);
            if (phase == Phase.Docking && !string.IsNullOrEmpty(room.def.dust))
            {
                dust ??= new DustFx(ModMaterials.Texture(room.def.mod, room.def.dust, true));
                dust.emitting = true;
            }
            lastFov = cam.fieldOfView;
            cam.fieldOfView = Aspect.VerticalFov(room.def.fov > 0f ? room.def.fov : 23f, cam.aspect);
        }

        void UpdateExternal()
        {
            if (State == Phase.Docked)
            {
                ship.transform.SetPositionAndRotation(DockedPos, DockedRot);
                ship.ExternalSpeedMetersPerSecond = 0f;
                return;
            }
            if (!Running) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            bool takingOff = State == Phase.TakeOff;
            float duration = takingOff ? ExternalTakeOffSeconds : ExternalDockSeconds;
            float a = Mathf.Clamp01(t / duration);
            a = a * a * (3f - 2f * a);
            Vector3 target = takingOff ? externalTakeOffPosition : DockedPos;
            Vector3 position = Vector3.LerpUnclamped(startPos, target, a);
            Quaternion rotation = Quaternion.SlerpUnclamped(startRot, DockedRot, a);
            ship.ExternalSpeedMetersPerSecond = (position - ship.transform.position).magnitude / dt;
            ship.transform.SetPositionAndRotation(position, rotation);
            if (t < duration) return;
            if (takingOff) End();
            else
            {
                State = Phase.Docked;
                ship.transform.SetPositionAndRotation(DockedPos, DockedRot);
                docked?.Invoke();
            }
        }

        void BeginExternalTakeOff()
        {
            if (!external || State != Phase.Docked) return;
            startPos = DockedPos;
            startRot = DockedRot;
            externalTakeOffPosition = startPos + externalPoint.forward * 5000f * 0.05f;
            State = Phase.TakeOff;
            t = 0f;
            released = false;
            ship.externalControl = true;
            ship.inputLocked = true;
            ship.modelHeld = true;
            chase.enabled = true;
        }

        void Update()
        {
            if (external) { UpdateExternal(); return; }
            // The view blend's chase camera works from its own pose (it keeps its state in the camera's transform).
            if (blendT >= 0f) { cam.transform.SetPositionAndRotation(chasePos, chaseRot); cam.fieldOfView = chaseFov; }
            UpdateDust();
            if (State == Phase.Docked)
            {
                // on the pad (a mobile station carries it along); the station's menu drives the camera
                ship.transform.SetPositionAndRotation(DockedPos, DockedRot);
                ship.ExternalSpeedMetersPerSecond = 0f;
                return;
            }
            if (!Running) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            t += dt;
            Vector3 pos;
            Quaternion rot;
            if (State == Phase.Docking)
            {
                float a = Es2DockCurves.Eval(Es2DockCurves.TransformAlpha, t);
                pos = Vector3.LerpUnclamped(startPos, DockedPos, a);
                rot = Quaternion.SlerpUnclamped(startRot, DockedRot, a);
                float b = Es2DockCurves.Eval(Es2DockCurves.BankingAlpha, t) * 30f * distanceFactor;
                Bank(b, b, 0f);
            }
            else
            {
                float align = Es2DockCurves.Eval(Es2DockCurves.Align, t), launch = Es2DockCurves.Eval(Es2DockCurves.Launch, t);
                var p1 = Vector3.LerpUnclamped(startPos, climbPos, align);
                var r1 = Quaternion.SlerpUnclamped(startRot, climbRot, align);
                pos = Vector3.LerpUnclamped(p1, W(room.takeoff), launch);
                rot = Quaternion.SlerpUnclamped(r1, Facing(room.takeoff, room.takeoffAhead), launch);
                Bank(Es2DockCurves.Eval(Es2DockCurves.Banking, t) * 10f, 0f, 0f);
                if (!boosted && t >= 1f)
                {
                    // TakeOffTimeline's Boost and CameraFade events at 1 s: the boost, and the view's 3 s blend to the ship's camera
                    boosted = true;
                    ship.Boost();
                    chase.enabled = true;
                    chase.Snap();
                    blendT = 0f;
                    chasePos = cam.transform.position; chaseRot = cam.transform.rotation; chaseFov = lastFov;
                }
            }
            ship.ExternalSpeedMetersPerSecond = (pos - lastPos).magnitude / dt;
            lastPos = pos;
            ship.transform.SetPositionAndRotation(pos, rot);
            if (State == Phase.Docking && t >= Es2DockCurves.DockedSeconds)
            {
                if (ship.visualModel != null) ship.visualModel.localRotation = Quaternion.identity;
                State = Phase.Docked;
                docked?.Invoke();
                return;
            }
            if (State == Phase.TakeOff && t >= Es2DockCurves.TakeOffSeconds) End();
        }

        void LateUpdate()
        {
            // Exterior docking has no ES2 hangar room or CineCamera: keep the orbit's chase camera in control.
            // The interior-dock camera path below reads room.camera / room.def and would dereference a null room here.
            if (external) return;
            PlaceLights();
            if (!Running && blendT < 0f) return;   // (docked: the station's menu has the camera)
            // The dock's camera: straight at the ship docking, eased toward it taking off (CameraLookAtShip).
            var at = LookAt(ship.transform.position, ship.transform.rotation);
            var stationPos = W(room.camera);
            var stationRot = Quaternion.LookRotation(at - stationPos, hull.up);
            if (State == Phase.TakeOff || blendT >= 0f)
            {
                float a = Es2DockCurves.EaseInOut(Mathf.Clamp01(Time.deltaTime * 8f), 2f);
                stationRot = Quaternion.Slerp(lastStationRot, stationRot, a);
            }
            lastStationRot = stationRot;
            if (State == Phase.Docking)
            {
                // ShipDocking's PlayCameraShake(BP_CamShake_SteadyCam, scale InitialDistanceFactor x GlobalAlpha): a legacy
                // oscillation of 1 deg pitch / yaw / roll at 5.19 / 5.88 / 6.95 rad/s (Amplitude x sin(t x Frequency)). Its
                // Rumble_002 reaches 30 m round the ship: the dock's camera sits 35 m off, out of it.
                float k = distanceFactor * Es2DockCurves.Eval(Es2DockCurves.GlobalAlpha, t) * Settings.CameraShake;
                stationRot *= Quaternion.Euler(-Mathf.Sin(t * 5.192004f) * k, Mathf.Sin(t * 5.876658f) * k, -Mathf.Sin(t * 6.945331f) * k);
            }
            float stationFov = Aspect.VerticalFov(room.def.fov > 0f ? room.def.fov : 23f, cam.aspect);
            if (blendT < 0f)
            {
                cam.transform.SetPositionAndRotation(stationPos, stationRot);
                cam.fieldOfView = stationFov;
                return;
            }
            // VTBlend_EaseInOut, exponent 4, over 3 s from the dock's camera to the ship's (the chase camera's pose this frame)
            chasePos = cam.transform.position; chaseRot = cam.transform.rotation; chaseFov = cam.fieldOfView;
            blendT += Time.deltaTime;
            float w = Es2DockCurves.EaseInOut(Mathf.Clamp01(blendT / Es2DockCurves.ViewBlendSeconds), 4f);
            cam.transform.SetPositionAndRotation(Vector3.Lerp(stationPos, chasePos, w), Quaternion.Slerp(stationRot, chaseRot, w));
            cam.fieldOfView = Mathf.Lerp(stationFov, chaseFov, w);
            if (blendT >= Es2DockCurves.ViewBlendSeconds) blendT = -1f;   // the chase camera's own from here
        }
        Quaternion lastStationRot = Quaternion.identity;

        void PlaySound(string name)
        {
            if (!string.IsNullOrEmpty(name)) Sfx.PlayAt(ModSounds.Named(name), ship.transform.position);
        }

        /// <summary>ApplyRandomBanking: the BankingNode (the model) rolls sin(2 T) x, pitches sin(1.5 (T + 0.5)) y and yaws
        /// sin(T + 0.75) z degrees.</summary>
        void Bank(float x, float y, float z)
        {
            if (ship.visualModel == null) return;
            float T = Time.time;
            ship.visualModel.localRotation = Quaternion.Euler(-Mathf.Sin((T + 0.5f) * 1.5f) * y, Mathf.Sin(T + 0.75f) * z, -Mathf.Sin(T * 2f) * x);
        }

        void End()
        {
            bool takeOff = State == Phase.TakeOff;
            State = Phase.None;
            ShowLights(false);
            if (ship.visualModel != null) ship.visualModel.localRotation = Quaternion.identity;
            if (!takeOff) return;
            // SetPlayerUndocked: the player flies on from the take-off point
            released = true;
            ship.externalControl = false;
            ship.inputLocked = false;
            ship.modelHeld = false;
            if (blendT < 0f) chase.enabled = true;
            TookOff?.Invoke();
        }

        /// <summary>The docking was called off (the scene ends, the player died): control back.</summary>
        void OnDisable()
        {
            ShowLights(false);
            if (dust != null) dust.Clear();
            if (!Running && !Docked) return;
            State = Phase.None;
            if (ship != null) { ship.externalControl = false; ship.inputLocked = false; ship.modelHeld = false; }
            if (chase != null) chase.enabled = true;
            blendT = -1f;
        }

        /// <summary>The take-off has handed the ship back to the player.</summary>
        public bool Released => released;

        void OnDestroy()
        {
            if (topLight != null) Destroy(topLight.gameObject);
            if (fillLight != null) Destroy(fillLight.gameObject);
            dust?.Destroy();
        }

        // ---- the dock's lights and landing dust -------------------------------------------------------------------------

        /// <summary>World units per EVERSPACE 2 centimetre (the room's metres are ES2's x its scale).</summary>
        float Cm => hull.lossyScale.x / 100f;
        /// <summary>The dock actor's frame (BP_DockableStation's root: its pad, the docked ship's heading, the station's up).</summary>
        Quaternion DockFrame => DockedRot;

        /// <summary>UE's unitless (legacy falloff) intensity to the light the remake's ship takes from it.</summary>
        public const float LightScale = 0.005f;
        const float TopIntensity = 100f, FillIntensity = 30f;

        /// <summary>The URP intensity that gives the ship UE's legacy falloff (1 - (d / R)^2)^2 instead of URP's inverse
        /// square (with its range fade): matched where the ship is.</summary>
        public static float Intensity(float ue, float d, float range)
        {
            float x = d * d / Mathf.Max(range * range, 1e-6f);
            float legacy = Mathf.Pow(Mathf.Clamp01(1f - x), 2f);
            float urp = Mathf.Pow(Mathf.Clamp01(1f - x * x), 2f);
            return ue * LightScale * legacy * d * d / Mathf.Max(urp, 1e-3f);
        }

        Light MakeSpot(string name, Color colour)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 90f;        // OuterConeAngle 45 (half the cone)
            l.innerSpotAngle = 0f;
            l.color = colour;
            l.shadows = LightShadows.None;   // (no URP shadows in space)
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        void ShowLights(bool on)
        {
            if (on && topLight == null)
            {
                topLight = MakeSpot("ES2 dock light (top)", Color.white);
                fillLight = MakeSpot("ES2 dock light (fill)", new Color32(0x77, 0xD2, 0xFF, 0xFF));
            }
            if (topLight != null) topLight.enabled = on;
            if (fillLight != null) fillLight.enabled = on;
            if (on) PlaceLights();
        }

        void PlaceLights()
        {
            if (topLight == null || !topLight.enabled || hull == null) return;
            var pivot = W(room.pad);
            float cm = Cm;
            // AttenuationRadius 5000 cm
            topLight.range = fillLight.range = 5000f * cm;
            // SpotLight_Top: (0, 0, 3000) under LightPivot, pitch -90
            topLight.transform.SetPositionAndRotation(pivot + hull.up * (3000f * cm), Quaternion.LookRotation(-hull.up, DockFrame * Vector3.forward));
            // SpotLight_Fill: (X 500 forward, Y 2000 right, Z -500 up) in the camera's frame round the pivot, toward the pivot
            var camRot = cam != null ? cam.transform.rotation : DockFrame;
            var at = pivot + camRot * new Vector3(2000f, -500f, 500f) * cm;
            fillLight.transform.SetPositionAndRotation(at, Quaternion.LookRotation(pivot - at, camRot * Vector3.up));
            var shipAt = ship.transform.position;
            topLight.intensity = Intensity(TopIntensity, Vector3.Distance(topLight.transform.position, shipAt), topLight.range);
            fillLight.intensity = Intensity(FillIntensity, Vector3.Distance(at, shipAt), fillLight.range);
        }

        readonly RaycastHit[] hits = new RaycastHit[16];

        void UpdateDust()
        {
            if (dust == null) return;
            if (State != Phase.Docking || t >= Es2DockCurves.DockedSeconds) dust.emitting = false;   // ToggleDust at 4 s
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float cm = Cm;
            var banking = ship.visualModel != null ? ship.visualModel : ship.transform;
            var from = banking.position;
            var down = -banking.up;
            float reach = 5000f * cm;   // LandingDustDistanceMax
            float amount = 0f;
            var at = from;
            if (dust.emitting && FindGround(from, down, reach, out at))
                amount = 1f - Mathf.Clamp01((ship.transform.position - at).magnitude / reach);
            // Velocity: the ship's up in the dust's frame x -5000 x (1, 1, 0): blown off the tilt across the dock's plane
            var up = hull.up;
            var lean = banking.up - up * Vector3.Dot(banking.up, up);
            dust.Step(dt, amount, at, DockFrame, -lean * (5000f * cm), up * (1000f * cm), cm);
        }

        /// <summary>LineTraceSingle down from the ship (not hitting it): the station's hull where it has one, else the dock's
        /// plane.</summary>
        bool FindGround(Vector3 from, Vector3 dir, float reach, out Vector3 at)
        {
            int n = Physics.RaycastNonAlloc(from, dir, hits, reach, 1 << HullCollision.HullLayer, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            at = from;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider == null || hits[i].collider.transform.IsChildOf(ship.transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; at = hits[i].point; }
            }
            if (best < float.MaxValue) return true;
            var plane = new Plane(hull.up, W(room.pad));
            if (plane.Raycast(new Ray(from, dir), out float d) && d <= reach) { at = from + dir * d; return true; }
            return false;
        }

        /// <summary>P_Vehicle_LandingDust's one GPU sprite emitter, drawn by GoF2/ModEs2Trail's landing dust kind.</summary>
        sealed class DustFx
        {
            struct P { public Vector3 pos, vel; public float age, life, rot, frame, amount; public Vector2 size; }

            public bool emitting;
            readonly List<P> particles = new List<P>();
            readonly MeshFilter mf;
            readonly Renderer renderer;
            float due;

            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            readonly List<Vector2> uv = new List<Vector2>(), size = new List<Vector2>();
            readonly List<Vector4> extra = new List<Vector4>(), dyn = new List<Vector4>();
            readonly List<Color> col = new List<Color>();
            readonly List<int> tri = new List<int>();

            public DustFx(Texture mask)
            {
                var go = new GameObject("ES2 landing dust");
                mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = new Mesh { name = "es2_landing_dust_add" };
                mf.sharedMesh.MarkDynamic();
                renderer = go.AddComponent<MeshRenderer>();
                var shader = Resources.Load<Shader>("GoF2Mods/ModEs2Trail");
                var mat = new Material(shader) { name = "es2_landing_dust" };
                mat.SetFloat("_Kind", 11f);
                if (mask != null) mat.SetTexture("_MaskTex", mask);
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
            }

            public void Clear() { particles.Clear(); emitting = false; due = 0f; if (renderer != null) renderer.enabled = false; }

            public void Destroy()
            {
                if (mf == null) return;
                UnityEngine.Object.Destroy(mf.sharedMesh);
                UnityEngine.Object.Destroy(renderer.sharedMaterial);
                UnityEngine.Object.Destroy(mf.gameObject);
            }

            /// <summary>One frame: 'amount' the Amount parameter, 'at' / 'frame' the dust component's place and turn,
            /// 'velocity' the Velocity parameter and 'accel' the constant acceleration (world units), 'cm' world units per cm.</summary>
            public void Step(float dt, float amount, Vector3 at, Quaternion frame, Vector3 velocity, Vector3 accel, float cm)
            {
                for (int k = particles.Count - 1; k >= 0; k--)
                {
                    var p = particles[k];
                    p.age += dt;
                    if (p.age >= p.life) { particles.RemoveAt(k); continue; }
                    p.vel += accel * dt;
                    p.pos += p.vel * dt;
                    particles[k] = p;
                }
                if (emitting && amount > 0f)
                {
                    due += 200f * amount * dt;
                    while (due >= 1f)
                    {
                        due -= 1f;
                        // LocationPrimitiveSphere: radius 500, surface only, no Z (a circle in the dock's plane), its offset
                        // x VelocityScale 3 .. 4 as the velocity
                        float a = UnityEngine.Random.value * Mathf.PI * 2f;
                        var offset = frame * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (500f * cm);
                        float age = UnityEngine.Random.value * dt;
                        var vel = offset * UnityEngine.Random.Range(3f, 4f) + velocity;
                        particles.Add(new P
                        {
                            pos = at + offset + vel * age, vel = vel, age = age, life = UnityEngine.Random.Range(0.5f, 1f),
                            rot = UnityEngine.Random.value * Mathf.PI * 2f, frame = UnityEngine.Random.value * 35f, amount = amount,
                            // StartSize 1 .. 2 per axis x 500 (the size's life multiplier)
                            size = new Vector2(UnityEngine.Random.Range(1f, 2f), UnityEngine.Random.Range(1f, 2f)) * (500f * cm),
                        });
                    }
                }
                else due = 0f;
                Draw();
            }

            void Draw()
            {
                bool any = particles.Count > 0;
                if (renderer.enabled != any) renderer.enabled = any;
                if (!any) return;
                v.Clear(); n.Clear(); uv.Clear(); size.Clear(); extra.Clear(); dyn.Clear(); col.Clear(); tri.Clear();
                var bounds = new Bounds(particles[0].pos, Vector3.zero);
                float reach = 0f;
                foreach (var p in particles)
                {
                    float rel = p.age / p.life;
                    // AlphaOverLife 0 -> 0.1 at half -> 0, x Amount at its birth (AlphaScaleOverLife); ColorOverLife 0.2
                    float alpha = (rel < 0.5f ? rel * 0.2f : (1f - rel) * 0.2f) * p.amount;
                    var c = new Color(0.2f, 0.2f, 0.2f, alpha);
                    var half = p.size * 0.5f;
                    int b = v.Count;
                    foreach (var corner in Corners)
                    {
                        v.Add(p.pos); n.Add(Vector3.up); uv.Add(corner); size.Add(half);
                        extra.Add(new Vector4(p.rot, 0f, rel, 0f)); dyn.Add(new Vector4(p.frame, 0f, 0f, 0f)); col.Add(c);
                    }
                    tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b); tri.Add(b + 2); tri.Add(b + 3);
                    bounds.Encapsulate(p.pos);
                    reach = Mathf.Max(reach, half.x, half.y);
                }
                bounds.Expand(reach * 3f);
                var m = mf.sharedMesh;
                m.Clear();
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetUVs(1, size); m.SetUVs(2, extra); m.SetUVs(3, dyn);
                m.SetColors(col); m.SetTriangles(tri, 0);
                m.bounds = bounds;
            }

            readonly Vector2[] Corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        }
    }

    /// <summary>BP_DockableStation's timeline curves (its CurveFloat exports: UE cubic keys with their tangents).</summary>
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class Es2DockCurves
    {
        /// <summary>A key: time, value, arrive and leave tangents (value per second).</summary>
        public struct Key
        {
            public float time, value, arrive, leave;
            public Key(float time, float value, float arrive, float leave) { this.time = time; this.value = value; this.arrive = arrive; this.leave = leave; }
        }

        // ShipDocking (5 s): CurveFloat_0 / _1
        public static readonly Key[] TransformAlpha = { new Key(0f, 0f, 0.787f, 0.787f), new Key(4f, 1f, 0f, -0.203f) };
        public static readonly Key[] BankingAlpha = { new Key(1f, 1f, -0.822f, -0.513f), new Key(5f, 0f, 0f, -0.104f) };
        public static readonly Key[] GlobalAlpha = { new Key(0f, 1f, 0f, 0f), new Key(5f, 0f, 0f, 0f) };
        // TakeOffTimeline (3 s): CurveFloat_10 / _11 / _13
        public static readonly Key[] Align = { new Key(0f, 0f, 0f, 0f), new Key(1.5f, 1f, 0f, 0f) };
        public static readonly Key[] Launch = { new Key(0.75f, 0f, 0f, 0f), new Key(3f, 1f, 1.419f, 1.419f) };
        public static readonly Key[] Banking = { new Key(0f, 0f, 0f, 0f), new Key(1.5f, 1f, 0f, 0f), new Key(3f, 0f, 0f, 0f) };

        /// <summary>The ship is down (TransformAlpha's last key).</summary>
        public const float DockedSeconds = 4f;
        public const float TakeOffSeconds = 3f;
        /// <summary>SetViewTargetWithBlend(pawn, 3 s) at the take-off's CameraFade event.</summary>
        public const float ViewBlendSeconds = 3f;

        /// <summary>FRichCurve's cubic interpolation: a Bezier between two keys with the leave / arrive tangents x dt / 3;
        /// the first / last value outside.</summary>
        public static float Eval(Key[] keys, float t)
        {
            if (t <= keys[0].time) return keys[0].value;
            for (int i = 0; i < keys.Length - 1; i++)
            {
                var k0 = keys[i]; var k1 = keys[i + 1];
                if (t > k1.time) continue;
                float dt = k1.time - k0.time, a = (t - k0.time) / dt, b = 1f - a;
                float p1 = k0.value + k0.leave * dt / 3f, p2 = k1.value - k1.arrive * dt / 3f;
                return b * b * b * k0.value + 3f * b * b * a * p1 + 3f * b * a * a * p2 + a * a * a * k1.value;
            }
            return keys[keys.Length - 1].value;
        }

        /// <summary>UE's FMath::InterpEaseInOut.</summary>
        public static float EaseInOut(float a, float exp) =>
            a < 0.5f ? 0.5f * Mathf.Pow(2f * a, exp) : 1f - 0.5f * Mathf.Pow(2f * (1f - a), exp);
    }
}
