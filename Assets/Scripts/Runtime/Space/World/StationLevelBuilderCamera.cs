// StationLevelBuilderCamera.cs
// StationLevel: remake: the Mod Builder's free camera (part of the partial StationLevel; its fields are in StationLevel.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace GoF2Remake.World
{
    public partial class StationLevel
    {
        /// <summary>The custom room shown (a mod's or the builder's preview), null = the race's.</summary>
        public Modding.ModInteriors.Room CustomHangar => customHangar;
        public Modding.ModInteriors.Room CustomBar => customBar;
        void UpdateCamera(float dtMs)
        {
            if (mainCamera == null) return;
            var cam = mainCamera.transform;
            if (FreeCamera)
            {
                cam.SetPositionAndRotation(FreeCameraPose.position, FreeCameraPose.rotation);
                mainCamera.fieldOfView = Aspect.VerticalFov(FreeCameraFov, mainCamera.aspect);
                mainCamera.nearClipPlane = 0.5f;
                mainCamera.farClipPlane = 20000f;
                return;
            }
            if (GoF2Remake.Data.VrFlags.Enabled) { VrCamera(cam); return; }
            if (View == StationView.Hangar)
            {
                // ModStation::OnUpdate: each axis eases to a new target on the other side of the base when within 5 units.
                var p = Vector3.zero;
                for (int i = 0; i < 3; i++)
                {
                    drift[i].Increase(dtMs);
                    if (Mathf.Abs(drift[i].Value - drift[i].Target) < 5f) NextDriftLeg(i, drift[i].Value);
                    p[i] = drift[i].Value;
                }
                if (customHangar != null)
                {
                    // A custom hangar: its "camera" marker, drifting a few metres, looking at "camera_target" (else the pad).
                    // EVERSPACE 2's dock: the CineCamera stays on its spring arm (no drift).
                    var at = hangarRoot.TransformPoint(customHangar.camera) + (Es2Docked ? Vector3.zero : OrbitLayout.ToUnity(p));
                    cam.SetPositionAndRotation(at, Quaternion.LookRotation(hangarRoot.TransformPoint(customHangar.cameraTarget) - at, hangarRoot.up));
                    SetCustomLens(customHangar.def, StationTables.HangarFov, StationTables.HangarNear, StationTables.HangarFar, false);
                }
                else
                {
                    var r = StationTables.HangarCameraRot[HangarIndex];
                    cam.SetPositionAndRotation(shipPivot + OrbitLayout.ToUnity(p), CameraRotation(r.x, r.y, StationTables.HangarCameraRoll));
                    SetLens(StationTables.HangarFov, StationTables.HangarNear, StationTables.HangarFar, false);
                }
                hangarView = HangarFlight.View.Of(mainCamera);   // the room's view, before the orbit below moves the camera
                // Remake: Inspect ship, from the hangar pose just set to the orbit around the ship (and back).
                if (Inspect.Shown || Inspect.Active)
                {
                    Inspect.Tick(Time.unscaledDeltaTime * 1000f);
                    mainCamera.nearClipPlane = Inspect.Apply(cam, cam.position, cam.rotation, mainCamera.nearClipPlane);
                    // The photo tools' field of view (UI.PhotoTools), eased in with the orbit.
                    if (Inspect.FovDeg > 0f) mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, Inspect.FovDeg, Mathf.SmoothStep(0f, 1f, Inspect.Blend));
                    // A panel over part of the screen (the Customize screen): the view turns aside so the ship stands in the
                    // middle of the free part (InspectFrameX). The turn comes and goes with the orbit's own eased blend, so
                    // the Customize screen moves in and out like Inspect ship (a separate linear ease times the raw blend
                    // started and stopped abruptly, and the target reset on leaving swung the view back before the orbit
                    // did); a change of the panel's width once in eases over InspectFrameMs.
                    if (!Inspect.Active || Inspect.Blend < 1f) inspectFrame = InspectFrameX;
                    else inspectFrame = Mathf.MoveTowards(inspectFrame, InspectFrameX, Time.unscaledDeltaTime * 1000f / InspectFrameMs);
                    float off = (inspectFrame - 0.5f) * Mathf.SmoothStep(0f, 1f, Inspect.Blend);
                    if (Mathf.Abs(off) > 0.0005f)
                    {
                        float halfTanX = Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * mainCamera.aspect;
                        cam.rotation *= Quaternion.AngleAxis(-Mathf.Atan(halfTanX * 2f * off) * Mathf.Rad2Deg, Vector3.up);
                    }
                }
                else inspectFrame = InspectFrameX = 0.5f;   // the blend back has ended: the frame back in the middle
            }
            else
            {
                Vector3 pos;
                Quaternion rot;
                if (introT < 1.25f)
                {
                    // EaseInOutMatrix(A, B, 3000): t 0.75 -> 1.25, blend sin(2 pi t) * 0.5 + 0.5; Increase(min(dt, 50)).
                    float introMs = introSkip ? 700f : StationTables.BarIntroMs;
                    introT = Mathf.Min(1.25f, introT + Mathf.Min(dtMs, 50f) * 0.5f / introMs);
                    if (introT >= 1.25f) introSkip = false;
                    float b = Mathf.Sin(TwoPi * introT) * 0.5f + 0.5f;
                    pos = Vector3.Lerp(barPosA, barPosB, b);
                    rot = Quaternion.Slerp(barRotA, barRotB, b);
                }
                else
                {
                    // Sway: [RotY(v / 35), translation (0, bob, 0)] * B, v easing between -4..5 at speed 1..4.
                    swayYaw.Increase(dtMs * swaySpeed);
                    if (Mathf.Abs(swayYaw.Value - swayYaw.Target) < 0.25f)
                    {
                        swayYaw.Start(swayYaw.Value, 5 - Random.Range(0, 10));
                        swaySpeed = 1 + Random.Range(0, 4);
                    }
                    // Phase step 0.05..0.12 per 20 ms frame in the original (normalised); bob at most 3.5 units.
                    bobPhase += Mathf.Clamp(dtMs * 0.0025f, 0f, 0.12f);
                    var yaw = Quaternion.Euler(0f, -swayYaw.Value / 35f * Mathf.Rad2Deg, 0f);
                    // The original turns the view about the room's origin; a custom bar about its own camera spot.
                    pos = (customBar != null ? barPosB : yaw * barPosB) + new Vector3(0f, Mathf.Sin(bobPhase) * 3.5f * M, 0f);
                    rot = yaw * barRotB;
                }
                // Remake: talking to a visitor, the view turns to them and zooms in a little (FocusVisitor), easing back
                // when the chat closes. Only sideways, keeping the room camera's pitch and roll: they stand beside the chat
                // window, in the middle of the room left of it (ChatFrameX, from LoungePanel; #82).
                chatZoom = Mathf.MoveTowards(chatZoom, chatVisitor >= 0 ? 1f : 0f, dtMs / ChatZoomMs);
                float z = chatZoom * chatZoom * (3f - 2f * chatZoom);   // smoothstep
                if (customBar != null) SetCustomLens(customBar.def, StationTables.BarFov, StationTables.BarNear, StationTables.BarFar);
                else SetLens(StationTables.BarFov, StationTables.BarNear, StationTables.BarFar);
                if (z > 0f) mainCamera.fieldOfView *= 1f - ChatZoomIn * z;
                if (z > 0f && chatTarget != Vector3.zero)
                {
                    var toVisitor = chatTarget - pos;
                    toVisitor.y = 0f;
                    if (toVisitor.sqrMagnitude > 0.01f)
                    {
                        // Turn past the visitor by the angle that puts them ChatFrameX of the width from the left.
                        float halfTanX = Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * mainCamera.aspect;
                        float past = Mathf.Atan(halfTanX * (1f - 2f * ChatFrameX)) * Mathf.Rad2Deg;
                        var e = rot.eulerAngles;
                        float yaw = Mathf.Atan2(toVisitor.x, toVisitor.z) * Mathf.Rad2Deg + past;
                        rot = Quaternion.Euler(e.x, Mathf.LerpAngle(e.y, yaw, z), e.z);
                    }
                }
                cam.SetPositionAndRotation(pos, rot);
            }
        }
        /// <summary>The Space Lounge's chat opened with visitor 'index' (LoungePanel): the camera eases toward them; -1 = the
        /// chat closed, back to the room's view.</summary>
        public void FocusVisitor(int index)
        {
            chatVisitor = index >= 0 && index < visitors.Count ? index : -1;
            if (chatVisitor >= 0) chatTarget = visitors[chatVisitor].feet + Vector3.up * visitors[chatVisitor].height * 0.5f;   // the figure's middle
        }
        /// <summary>VR: standing in the room, still (no drift, sway or intro). The hangar: on the floor beside the pad, between
        /// the original camera's spot and the ship, facing it, the eyes 1.7 m above the hull's bottom (the pad); the bar: the
        /// original's view point (B), level.</summary>
        void VrCamera(Transform cam)
        {
            if (View == StationView.Hangar)
            {
                var ship = playerShip;
                var bounds = new Bounds(shipPivot, Vector3.one);
                if (ship != null && !PlayerFlying)
                {
                    bool any = false;
                    foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
                    {
                        if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
                    }
                    vrShipBounds = bounds;
                }
                else if (vrShipBounds.size != Vector3.zero) bounds = vrShipBounds;
                var original = customHangar != null ? customHangar.camera : shipPivot + OrbitLayout.ToUnity(hangarCamBase);
                var away = original - bounds.center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Vector3.back;
                away.Normalize();
                float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
                var pos = bounds.center + away * (radius + 5f);
                pos.y = bounds.min.y + 1.7f;
                cam.SetPositionAndRotation(pos, Quaternion.LookRotation(-away, Vector3.up));
                SetLens(StationTables.HangarFov, StationTables.HangarNear, StationTables.HangarFar, false);
            }
            else
            {
                introT = 1.25f;   // no intro ease
                cam.SetPositionAndRotation(barPosB, Quaternion.Euler(0f, barRotB.eulerAngles.y, 0f));
                SetLens(StationTables.BarFov, StationTables.BarNear, StationTables.BarFar);
            }
        }
        float Base(int axis) => hangarCamBase[axis];
        void NextDriftLeg(int axis, float from)
        {
            driftSign[axis] = !driftSign[axis];
            int min = axis == 0 ? 18 : axis == 1 ? 30 : 50, range = axis == 0 ? 131 : axis == 1 ? 120 : 100;
            float offset = (min + Random.Range(0, range)) * (driftSign[axis] ? 1f : -1f);
            drift[axis].Start(from, Base(axis) + offset);
        }
        /// <summary>A custom room's lens: "fov" (vertical degrees), "near" / "far" (metres); the original's otherwise.</summary>
        void SetCustomLens(Modding.ModInteriors.Def d, float fovRad, float near, float far, bool keepWidth = true)
        {
            SetLens(d.fov > 0f ? d.fov * Mathf.Deg2Rad : fovRad, d.near > 0f ? d.near / M : near, d.far > 0f ? d.far / M : far, keepWidth);
        }
        /// <summary>'keepWidth': wider screens keep the 16:9 width (the bars end outside it); the hangars use the original
        /// Hor+ view like the flight cameras (a new hangar model is being made, so their cut-off edges are left for it).</summary>
        void SetLens(float fovRad, float near, float far, bool keepWidth = true)
        {
            mainCamera.fieldOfView = keepWidth ? Aspect.VerticalFovKeepWidth(fovRad * Mathf.Rad2Deg, mainCamera.aspect)
                                               : Aspect.VerticalFov(fovRad * Mathf.Rad2Deg, mainCamera.aspect);
            mainCamera.nearClipPlane = near * M;
            mainCamera.farClipPlane = far * M;
        }
        /// <summary>SpaceLounge::updateScreenPositions: visitors and glows face the camera (MatrixGetLookAt, local +Z toward
        /// the camera); the glow sits 100 units behind; Terran bars turn visitors a further 180 deg. Modified: upright (turned
        /// about the vertical only, not with the camera's up), the glow centred behind the figure, the shadow under it.</summary>
        void UpdateBillboards()
        {
            // VR: they face the head (the headset's camera), upright.
            var eye = SpaceHooks.VrEye?.Invoke();
            var cam = eye != null ? eye.transform : mainCamera.transform;
            foreach (var v in visitors)
            {
                // Modified: upright, turned only about the vertical axis toward the camera (the original's MatrixGetLookAt with
                // the camera's up tipped them back and forth with the camera's pitch and sway).
                var toCam = cam.position - v.feet;
                toCam.y = 0f;
                if (toCam.sqrMagnitude < 1e-6f) continue;
                var look = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                if (v.body != null) v.body.rotation = BarRace == 0 ? look * Quaternion.Euler(0f, 180f, 0f) : look;
                // The figure's drawn centre (the quad isn't centred on its pivot, and Terran bars turn it round).
                var centre = v.body != null ? v.body.TransformPoint(v.bodyCentre) : v.feet;
                if (v.glow != null)
                {
                    // Modified: the glow 100 units behind the figure with its centre on the figure's (the same across and up).
                    var z = look * Vector3.forward;
                    v.glow.rotation = look;
                    var target = centre - z * (100f * M);
                    v.glow.position += target - v.glow.TransformPoint(v.glowCentre);
                }
                // Modified: the shadow on the floor right under the figure's centre.
                if (v.shadow != null) v.shadow.position = new Vector3(centre.x, v.feet.y + 20f * M, centre.z);
            }
        }
    }
}
