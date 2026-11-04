// VrRig.cs
// Remake VR: one per scene (VrMode attaches it on sceneLoaded). The scene's own camera stays the game's logical camera:
// every system keeps using Camera.main for its projections, cutscenes and the chase camera, but it renders nothing to the
// headset (an empty desktop-only pass under everything). A separate stereo camera, the VR eye, sits on a rig that follows
// the logical camera every frame (after all camera scripts), with the headset's pose on top (Input System
// TrackedPoseDriver, seated: the tracking origin is the head's start). Outside flight the rig keeps only the camera's yaw
// (a level horizon). The simulation (-vrsim) has no headset: right mouse drag looks around.
// The floating screen (VrPanels: every UI panel on a quad): with a headset 2.3 m wide, 2 m ahead (in flight it fills the
// scene camera's view at 2 m, so the HUD's markers sit on what they mark); in the simulation it fills the view, so the
// desktop mouse lines up with it. The right controller's laser points at it (VrPad's virtual mouse, the trigger clicks).
// The star map draws into a texture on the screen too (its own 3D scene switches the level's cameras off).
// Cutscenes in flight (a level script's camera, the launch / arrival fly-in, the gate / Khador / planet jump scenes, the
// death camera; only while the scene camera has left the seat): stabilised, the player placed where the cinematic camera
// is but with a level horizon (only its yaw, never its pitch or roll; within 20 deg of the camera's heading the rig doesn't
// turn at all, beyond it turns at most 40 deg/s), the cockpit switched off (the hull shows again), and every cut (the camera
// jumping 60 m or 30 deg in a frame, entering or leaving the cutscene) masked by a fade from black (0.3 s).
// Recentre (#205): the head and the hands sit under a tracking space whose offset puts the headset's current pose (its
// position and heading) on the rig's origin, so the eyes are where the scene camera is (the HUD's markers on what they
// mark) and straight ahead is the ship's nose. Done once when tracking starts, and again on Home (keyboard) or both
// thumbsticks clicked together; kept across scenes.

using GoF2Remake.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace GoF2Remake.Vr
{
    [DefaultExecutionOrder(10000)]
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class VrRig : MonoBehaviour
    {
        public const int UiLayer = 29;
        /// <summary>Drawn by the scene cameras but not the eye: the player's hull while seated in the cockpit.</summary>
        public const int HiddenLayer = 28;
        const float ScreenDistance = 2f, ScreenWidth = 2.3f, LaserLength = 8f;
        const float CutDistance = 60f, CutAngle = 30f, YawDeadZone = 20f, YawRate = 40f, FadeSeconds = 0.3f;
        const float ModalWidth = 1.6f, ModalSimDistance = 0.3f;

        /// <summary>This scene's rig, null outside VR.</summary>
        public static VrRig Current { get; private set; }

        Camera logical, eye;
        Transform tracking, head, leftHand, rightHand, screen, mapScreen;
        VrPanels panels;
        LineRenderer laser;
        RenderTexture mapTexture;
        Material mapMaterial;
        Camera mapCamera;
        int eyeMask;
        CameraClearFlags eyeClear;
        Color eyeBackground;
        bool flight;
        Vector2 simLook;
        GameObject cockpit;
        World.SpaceLevel level;
        Transform fade;
        Material fadeMaterial;
        float fadeAlpha, rigYaw;
        Vector3 lastCameraPosition;
        Quaternion lastCameraRotation;

        /// <summary>In flight a cutscene holds the camera: the stabilised view, no cockpit.</summary>
        public bool Cinematic { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; DebugLook = null; trackingOffset = null; }

        /// <summary>The tracking space's offset (local to the rig) from the last recentre; null = not recentred yet.</summary>
        static Pose? trackingOffset;
        bool recentreWasDown;

        /// <summary>Testing (the simulation): a fixed look (degrees: x yaw, y pitch up) instead of the mouse's; null = the mouse.</summary>
        public static Vector2? DebugLook;

        /// <summary>The headset's camera (null outside VR).</summary>
        public Camera Eye => eye;
        /// <summary>The controllers' poses (null without a headset): VrControls grabs the stick and the throttle.</summary>
        public Transform LeftHand => leftHand;
        public Transform RightHand => rightHand;

        /// <summary>The right controller's position (VrStation: grabbing the ship).</summary>
        public Vector3 RightHandPosition => rightHand != null ? rightHand.position : head.position;

        /// <summary>The floating screen (the UI quads' frame).</summary>
        public Transform Screen => screen;

        public static void Attach(Scene scene)
        {
            var cam = Camera.main;
            if (cam == null || cam.gameObject.scene != scene)
                foreach (var root in scene.GetRootGameObjects()) { cam = root.GetComponentInChildren<Camera>(); if (cam != null) break; }
            if (cam == null) { Debug.LogWarning($"VrRig: no camera in {scene.name}"); return; }
            var go = new GameObject("VR Rig");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<VrRig>().Init(cam, scene.name == "Space");
        }

        void Init(Camera cam, bool inFlight)
        {
            Current = this;
            logical = cam;
            flight = inFlight;

            tracking = new GameObject("VR Tracking Space").transform;
            tracking.SetParent(transform, false);
            if (trackingOffset.HasValue) tracking.SetLocalPositionAndRotation(trackingOffset.Value.position, trackingOffset.Value.rotation);
            head = new GameObject("VR Head").transform;
            head.SetParent(tracking, false);
            eye = head.gameObject.AddComponent<Camera>();
            eye.CopyFrom(cam);
            head.localPosition = Vector3.zero;   // CopyFrom moves the eye onto the camera's pose too
            head.localRotation = Quaternion.identity;
            eye.tag = "Untagged";
            eye.depth = cam.depth + 100f;
            eye.nearClipPlane = 0.02f;
            eyeMask = (cam.cullingMask | (1 << UiLayer)) & ~(1 << HiddenLayer);
            eye.cullingMask = eyeMask;
            eyeClear = cam.clearFlags;
            eyeBackground = cam.backgroundColor;
            eye.stereoTargetEye = VrMode.Headset ? StereoTargetEyeMask.Both : StereoTargetEyeMask.None;
            var src = cam.GetUniversalAdditionalCameraData();
            var dst = eye.GetUniversalAdditionalCameraData();
            dst.renderPostProcessing = src.renderPostProcessing;
            dst.antialiasing = src.antialiasing;
            dst.antialiasingQuality = src.antialiasingQuality;
            Visuals.ScreenAntialiasing.Follow(eye, cam);   // the Post-process AA option, as the camera it stands in for
            dst.volumeLayerMask = src.volumeLayerMask;
            dst.volumeTrigger = head;
            dst.renderShadows = src.renderShadows;
            dst.requiresColorOption = src.requiresColorOption;
            dst.requiresDepthOption = src.requiresDepthOption;

            // The logical camera: nothing to see, under everything, never in the headset.
            cam.cullingMask = 0;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            src.renderPostProcessing = false;
            src.renderShadows = false;
            cam.depth = eye.depth - 200f;
            var listener = cam.GetComponent<AudioListener>();
            if (listener != null) { listener.enabled = false; head.gameObject.AddComponent<AudioListener>(); }

            if (VrMode.Headset)
            {
                var tpd = head.gameObject.AddComponent<TrackedPoseDriver>();
                tpd.positionInput = new InputActionProperty(new InputAction("head position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
                tpd.rotationInput = new InputActionProperty(new InputAction("head rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
                leftHand = Hand("VR Left Hand", "LeftHand");
                rightHand = Hand("VR Right Hand", "RightHand");
                laser = rightHand.gameObject.AddComponent<LineRenderer>();
                laser.useWorldSpace = true;
                laser.positionCount = 2;
                laser.widthMultiplier = 0.004f;
                laser.material = VrPanels.ScreenMaterial(3300);
                laser.material.SetColor("_BaseColor", new Color(0.4f, 0.85f, 1f, 0.8f));
                laser.gameObject.layer = UiLayer;
                laser.enabled = false;
            }

            screen = new GameObject("VR Screen").transform;
            screen.SetParent(transform, false);
            panels = new VrPanels(screen, UiLayer);
            mapScreen = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            mapScreen.name = "VR Star Map";
            Destroy(mapScreen.GetComponent<Collider>());
            mapScreen.gameObject.layer = UiLayer;
            mapScreen.SetParent(screen, false);
            mapScreen.localPosition = new Vector3(0f, 0f, 0.002f);
            mapMaterial = VrPanels.ScreenMaterial(3050);
            mapScreen.GetComponent<MeshRenderer>().sharedMaterial = mapMaterial;
            mapScreen.gameObject.SetActive(false);
            if (!flight && FindAnyObjectByType<World.StationLevel>() != null) gameObject.AddComponent<VrStation>().Init(this);
            if (flight)
            {
                cockpit = new GameObject("VR Cockpit");
                cockpit.transform.SetParent(transform, false);
                cockpit.AddComponent<VrCockpit>().Init(this, panels);
                level = FindAnyObjectByType<World.SpaceLevel>();
            }
            // The fade: a black quad just past the near plane, over everything.
            fade = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            fade.name = "VR Fade";
            Destroy(fade.GetComponent<Collider>());
            fade.gameObject.layer = UiLayer;
            fade.SetParent(head, false);
            fade.localPosition = new Vector3(0f, 0f, 0.05f);
            fade.localScale = new Vector3(0.6f, 0.6f, 1f);
            fadeMaterial = VrPanels.ScreenMaterial(4000);
            fadeMaterial.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            fade.GetComponent<MeshRenderer>().sharedMaterial = fadeMaterial;
            fade.gameObject.SetActive(false);
            Follow();
        }

        Transform Hand(string name, string usage)
        {
            var t = new GameObject(name).transform;
            t.SetParent(tracking, false);
            var tpd = t.gameObject.AddComponent<TrackedPoseDriver>();
            var pos = new InputAction(name + " position", expectedControlType: "Vector3");
            pos.AddBinding($"<XRController>{{{usage}}}/pointerPosition");
            pos.AddBinding($"<XRController>{{{usage}}}/devicePosition");
            var rot = new InputAction(name + " rotation", expectedControlType: "Quaternion");
            rot.AddBinding($"<XRController>{{{usage}}}/pointerRotation");
            rot.AddBinding($"<XRController>{{{usage}}}/deviceRotation");
            tpd.positionInput = new InputActionProperty(pos);
            tpd.rotationInput = new InputActionProperty(rot);
            // A small handle so the hands are visible.
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(model.GetComponent<Collider>());
            model.layer = UiLayer;
            model.transform.SetParent(t, false);
            model.transform.localScale = new Vector3(0.035f, 0.035f, 0.12f);
            model.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            return t;
        }

        void OnDestroy()
        {
            panels?.Release();
            if (mapTexture != null) { mapTexture.Release(); Destroy(mapTexture); }
            if (mapMaterial != null) Destroy(mapMaterial);
            if (fadeMaterial != null) Destroy(fadeMaterial);
            if (Current == this) Current = null;
        }

        void LateUpdate()
        {
            if (logical == null || panels == null) return;   // Init didn't finish (#115: it threw, and this threw every frame)
            Follow();
            UpdateRecentre();
            UpdateFade();
            panels.Update();
            PlaceScreen();
            UpdateMap();
            UpdatePointer();
        }

        /// <summary>The rig on the logical camera: its whole pose in flight, else (and in a cutscene) its position and a
        /// level heading.</summary>
        void Follow()
        {
            var t = logical.transform;
            bool cinematic = flight && CutsceneHolds();
            if (cinematic != Cinematic)
            {
                Cinematic = cinematic;
                if (cockpit != null) cockpit.SetActive(!cinematic);
                Cut(t);
            }
            if (cinematic)
            {
                if (Vector3.Distance(t.position, lastCameraPosition) > CutDistance || Quaternion.Angle(t.rotation, lastCameraRotation) > CutAngle) Cut(t);
                else if (Heading(t, out float yaw))
                {
                    float off = Mathf.DeltaAngle(rigYaw, yaw);
                    if (Mathf.Abs(off) > YawDeadZone)
                        rigYaw += Mathf.Sign(off) * Mathf.Min(Mathf.Abs(off) - YawDeadZone, YawRate * Time.unscaledDeltaTime);
                }
                transform.SetPositionAndRotation(t.position, Quaternion.Euler(0f, rigYaw, 0f));
            }
            else if (flight) transform.SetPositionAndRotation(t.position, t.rotation);
            else transform.SetPositionAndRotation(t.position, Quaternion.Euler(0f, t.eulerAngles.y, 0f));
            lastCameraPosition = t.position;
            lastCameraRotation = t.rotation;
            if (!VrMode.Headset)
            {
                // The simulation: the eye matches the logical camera (+ the right-drag look).
                var mouse = Mouse.current;
                if (mouse != null && mouse.rightButton.isPressed)
                {
                    simLook += mouse.delta.ReadValue() * 0.15f;
                    simLook.y = Mathf.Clamp(simLook.y, -80f, 80f);
                }
                else simLook = Vector2.Lerp(simLook, Vector2.zero, Time.unscaledDeltaTime * 4f);
                if (DebugLook.HasValue) simLook = DebugLook.Value;
                var baseRot = flight ? Quaternion.identity : Quaternion.Inverse(transform.rotation) * t.rotation;
                head.localRotation = baseRot * Quaternion.Euler(-simLook.y, simLook.x, 0f);
                eye.fieldOfView = logical.fieldOfView;
            }
        }

        /// <summary>Recentre on Home or both thumbsticks clicked; the first time on the first tracked frame.</summary>
        void UpdateRecentre()
        {
            if (!VrMode.Headset) return;
            var kb = Keyboard.current;
            bool down = (kb != null && kb.homeKey.isPressed) || VrPad.BothSticksClicked;
            bool pressed = down && !recentreWasDown;
            recentreWasDown = down;
            bool tracked = head.localPosition != Vector3.zero || head.localRotation != Quaternion.identity;
            if (pressed || (!trackingOffset.HasValue && tracked)) Recentre();
        }

        /// <summary>The headset's current pose onto the rig's origin: its position, and its heading (yaw only, the horizon
        /// stays level).</summary>
        public void Recentre()
        {
            if (head == null || tracking == null) return;
            var f = head.localRotation * Vector3.forward;
            float yaw = new Vector2(f.x, f.z).sqrMagnitude > 0.01f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            var rot = Quaternion.Euler(0f, -yaw, 0f);
            var pos = -(rot * head.localPosition);
            tracking.SetLocalPositionAndRotation(pos, rot);
            trackingOffset = new Pose(pos, rot);
            fadeAlpha = Mathf.Max(fadeAlpha, 0.5f);
        }

        /// <summary>A cutscene holds the scene camera: a level script's, the fly-in, a jump scene or the death camera, and the
        /// camera isn't on the seat (some scripts keep the pilot's view).</summary>
        bool CutsceneHolds()
        {
            if (level == null) return false;
            var nav = level.Navigation;
            var jump = level.SystemJump;
            bool held = level.Cutscene || !level.LaunchCameraOver || (nav != null && nav.Jumping) || (jump != null && jump.Cinematic)
                        || (level.Health != null && level.Health.Dead);
            if (!held) return false;
            var ship = level.Player != null ? level.Player.transform : null;
            if (ship == null) return true;
            var t = logical.transform;
            return Vector3.Distance(t.position, ship.TransformPoint(VrCockpit.Seat)) > 0.5f || Quaternion.Angle(t.rotation, ship.rotation) > 2f;
        }

        /// <summary>The camera's heading (degrees about world up); false looking straight up or down.</summary>
        static bool Heading(Transform t, out float yaw)
        {
            var f = t.forward;
            yaw = 0f;
            if (new Vector2(f.x, f.z).sqrMagnitude < 0.01f) return false;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>A cut: face the camera's heading at once, behind a fade from black.</summary>
        void Cut(Transform t)
        {
            if (Heading(t, out float yaw)) rigYaw = yaw;
            fadeAlpha = 1f;
        }

        void UpdateFade()
        {
            fadeAlpha = Mathf.MoveTowards(fadeAlpha, 0f, Time.unscaledDeltaTime / FadeSeconds);
            bool on = fadeAlpha > 0f;
            if (fade.gameObject.activeSelf != on) fade.gameObject.SetActive(on);
            if (on) fadeMaterial.SetColor("_BaseColor", new Color(0f, 0f, 0f, fadeAlpha));
        }

        /// <summary>The floating screen 2 m ahead: in flight and in the simulation as big as the logical camera's view at that
        /// distance (the HUD's markers line up), else 2.3 m wide.</summary>
        void PlaceScreen()
        {
            float aspect = panels.Aspect;
            float height, width, distance = ScreenDistance;
            // In the cockpit a conversation, a menu or the map (the flight controls halted) would sit behind the dashboard: with a
            // headset the screen rises above it (1.6 m wide, its bottom edge 2 deg below straight ahead, tilted toward the eye);
            // the simulation brings it in front of the cockpit, still filling the view (the desktop mouse lines up with it).
            bool modal = flight && !Cinematic && Flight.Navigation.InputHalted;
            if (modal && VrMode.Headset)
            {
                width = ModalWidth;
                height = width / aspect;
                float centre = ScreenDistance * Mathf.Tan(-2f * Mathf.Deg2Rad) + height * 0.5f;
                screen.localRotation = Quaternion.Euler(-Mathf.Atan2(centre, ScreenDistance) * Mathf.Rad2Deg, 0f, 0f);
                screen.localPosition = new Vector3(0f, centre, ScreenDistance);
                screen.localScale = new Vector3(width, height, 1f);
                return;
            }
            if (modal) distance = ModalSimDistance;
            if ((flight && !Cinematic) || !VrMode.Headset)
            {
                height = 2f * distance * Mathf.Tan(logical.fieldOfView * 0.5f * Mathf.Deg2Rad);
                width = height * aspect;
            }
            else
            {
                width = ScreenWidth;
                height = width / aspect;
            }
            // The simulation outside flight: the eye looks along the logical camera (its pitch too), the screen in front of it.
            screen.localRotation = !VrMode.Headset && !flight ? Quaternion.Inverse(transform.rotation) * logical.transform.rotation : Quaternion.identity;
            screen.localPosition = screen.localRotation * new Vector3(0f, 0f, distance);
            screen.localScale = new Vector3(width, height, 1f);
        }

        /// <summary>The star map's own camera into a texture on the screen; the eye sees only the VR layer meanwhile.</summary>
        void UpdateMap()
        {
            var map = StarMap.Current;
            var cam = map != null ? map.MapCamera : null;
            if (cam != mapCamera)
            {
                mapCamera = cam;
                if (cam != null)
                {
                    if (mapTexture == null || mapTexture.width != UnityEngine.Screen.width || mapTexture.height != UnityEngine.Screen.height)
                    {
                        if (mapTexture != null) { mapTexture.Release(); Destroy(mapTexture); }
                        mapTexture = new RenderTexture(Mathf.Max(64, UnityEngine.Screen.width), Mathf.Max(64, UnityEngine.Screen.height), 24, RenderTextureFormat.ARGB32);
                        mapTexture.Create();
                    }
                    cam.targetTexture = mapTexture;
                    cam.stereoTargetEye = StereoTargetEyeMask.None;
                    mapMaterial.SetTexture("_BaseMap", mapTexture);
                }
            }
            bool open = mapCamera != null;
            mapScreen.gameObject.SetActive(open);
            // StarMap switches every camera off while open: the eye comes back, seeing only the screens.
            if (open && !eye.enabled) eye.enabled = true;
            eye.cullingMask = open ? 1 << UiLayer : eyeMask;
            eye.clearFlags = open ? CameraClearFlags.SolidColor : eyeClear;
            eye.backgroundColor = open ? Color.black : eyeBackground;
        }

        /// <summary>The pointer's ray past the UI into the world this frame (VrStation: the ship, the visitors), null = on the
        /// UI or nothing to point at.</summary>
        public Ray? WorldPointer { get; private set; }
        /// <summary>The pointer's select this frame: the right trigger (a headset) or the left mouse button (the simulation).</summary>
        public bool SelectPressed { get; private set; }
        bool selectWasDown;

        /// <summary>The laser to 'point' (VrStation, after the rig: a visitor or the ship under it).</summary>
        public void ShowLaserTo(Vector3 point)
        {
            if (laser == null || rightHand == null) return;
            laser.enabled = true;
            laser.SetPosition(0, rightHand.position);
            laser.SetPosition(1, point);
        }

        /// <summary>The pointer: a headset's right controller (its laser), or the simulation's mouse through the eye. Over a UI
        /// element on the screen it is the UI's (VrPad's virtual mouse, the trigger clicks); elsewhere it points into the world.
        /// In flight only while a menu, conversation or map halts the flight controls (else the trigger fires).</summary>
        void UpdatePointer()
        {
            WorldPointer = null;
            bool down;
            Ray ray;
            if (VrMode.Headset)
            {
                if (rightHand == null) return;
                ray = new Ray(rightHand.position, rightHand.forward);
                down = VrPad.RightTrigger > 0.6f;
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse == null) return;
                ray = eye.ScreenPointToRay(mouse.position.ReadValue());
                down = mouse.leftButton.isPressed;
            }
            SelectPressed = down && !selectWasDown;
            selectWasDown = down;
            if (laser != null) laser.enabled = false;
            if (flight && !Flight.Navigation.InputHalted) { VrPad.SetPointer(false, default, false, 0f); return; }
            bool onScreen = HitScreen(ray, out Vector2 uv, out float distance);
            bool ui = onScreen && panels.UiAt(uv);
            if (ui && laser != null)
            {
                laser.enabled = true;
                laser.SetPosition(0, ray.origin);
                laser.SetPosition(1, ray.GetPoint(Mathf.Min(distance, LaserLength)));
            }
            if (!ui) WorldPointer = ray;
            if (VrMode.Headset)
            {
                var pixel = new Vector2(uv.x * UnityEngine.Screen.width, uv.y * UnityEngine.Screen.height);
                VrPad.SetPointer(onScreen, pixel, ui && down, 0f);
            }
        }

        /// <summary>Where 'ray' hits the floating screen: (u, v) from its bottom-left corner.</summary>
        public bool HitScreen(Ray ray, out Vector2 uv, out float distance)
        {
            uv = default;
            distance = 0f;
            var plane = new Plane(-screen.forward, screen.position);
            if (!plane.Raycast(ray, out distance) || distance <= 0f) return false;
            var local = screen.InverseTransformPoint(ray.GetPoint(distance));
            if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f) return false;
            uv = new Vector2(local.x + 0.5f, local.y + 0.5f);
            return true;
        }
    }
}
