// StationLevelViews.cs
// StationLevel: view switching, the turntable, audio (part of the partial StationLevel; its fields are in StationLevel.cs).

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
        // ---- view switching ----------------------------------------------------------------------------------
        /// <summary>Instant switch, like ModStation (no fades). The lounge plays its camera intro on the first visit only.</summary>
        public void SetView(StationView view, bool force = false)
        {
            if (view == View && !force) return;
            View = view;
            if (view != StationView.Hangar) Inspect.Reset();
            hangarRoot.gameObject.SetActive(view == StationView.Hangar);
            barRoot.gameObject.SetActive(view == StationView.Lounge);
            if (view == StationView.Lounge)
            {
                introT = loungeVisited ? 1.25f : 0.75f;   // SpaceLounge::init starts at B
                introSkip = false;
                loungeVisited = true;
                ApplyLoungeLighting();
            }
            else
            {
                flingVelocity = 0f;
                ApplyHangarLighting();
                traffic?.ResumeAudio();
            }
            PlayAmbience();
            UpdateCamera(0f);
            ViewChanged?.Invoke();
        }
        void ApplyHangarLighting()
        {
            SkyReflection.SetSystem(Layout);   // the hulls' reflection: the station's system's cubemap
            if (orbitDocked) { if (keyLight != null) keyLight.enabled = false; return; }   // the orbit's own light
            if (Es2Docked)
            {
                // EVERSPACE 2's dock is the station in its orbit: the system's sun and the sky's ambient, as in space (the ship
                // landed there a moment ago), not a hangar's flat light
                OrbitBuilder.SetupLights(Layout, keyLight, null, sunIntensityAt2, sunShadows: LightShadows.None);
                RenderSettings.ambientMode = AmbientMode.Skybox;
                DynamicGI.UpdateEnvironment();
                GoF2Remake.World.SpaceHooks.SceneFog(false);
                return;
            }
            OrbitBuilder.SetSunGamma(Vector3.zero);   // Modified: the hangar's key light stays URP's linear diffuse
            if (customHangar == null) SkyReflection.SetHangar(HangarIndex);   // Remake: the hangar's own (none in the Valkyrie's)
            if (keyLight != null)
            {
                keyLight.transform.rotation = Quaternion.LookRotation(-OrbitLayout.DirToUnity(StationTables.HangarTowardLight).normalized);
                if (customHangar != null && customHangar.hasLight && (customHangar.pad - customHangar.light).sqrMagnitude > 1e-4f)
                    keyLight.transform.rotation = Quaternion.LookRotation((customHangar.pad - customHangar.light).normalized);   // its "light" marker
                keyLight.color = Color.white;
                // No URP shadows on the ships (the original has none; HangarShipShadow gives their contact shadows): within
                // the 50 m shadow distance Inspect ship's close-ups showed the hull shading itself (a player's report).
                keyLight.shadows = LightShadows.None;
                keyLight.intensity = customHangar != null && customHangar.def.lightIntensity >= 0f ? customHangar.def.lightIntensity : hangarLightIntensity;
            }
            var amb = (StationTables.HangarAmbient(Layout.raceId) + new Color(0.2f, 0.2f, 0.2f)) * 0.7f * hangarAmbientScale;
            if (customHangar?.def.ambient is Color customAmb) amb = customAmb;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(amb.r, amb.g, amb.b);
            if (customHangar != null) SetCustomFog(customHangar.def);
            else SetFog(HangarIndex == 1, StationTables.HangarFogEnd);
        }
        void ApplyLoungeLighting()
        {
            SkyReflection.SetSystem(Layout);
            OrbitBuilder.SetupLights(Layout, keyLight, null, sunIntensityAt2, sunShadows: LightShadows.Soft, gammaSun: false);   // the room's own shadows
            RenderSettings.ambientMode = AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
            if (customBar != null)
            {
                // A custom bar: its "light" marker shines toward the resting view's target; its own ambient and fog.
                if (keyLight != null && customBar.hasLight && (customBar.cameraTarget - customBar.light).sqrMagnitude > 1e-4f)
                    keyLight.transform.rotation = Quaternion.LookRotation((customBar.cameraTarget - customBar.light).normalized);
                if (keyLight != null && customBar.def.lightIntensity >= 0f) keyLight.intensity = customBar.def.lightIntensity;
                if (customBar.def.ambient is Color a) { RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = a; }
                SetCustomFog(customBar.def);
                return;
            }
            SetFog(BarRace == 1, StationTables.BarFogEnd);
        }
        /// <summary>A custom room's fog ("fog": colour and end in metres), else none.</summary>
        static void SetCustomFog(Modding.ModInteriors.Def d)
        {
            bool on = d.fogColor.HasValue && d.fogEnd > 0f;
            GoF2Remake.World.SpaceHooks.SceneFog(on);
            if (!on) return;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 0f;
            RenderSettings.fogEndDistance = d.fogEnd;
            RenderSettings.fogColor = d.fogColor.Value;
        }
        static void SetFog(bool on, float end)
        {
            GoF2Remake.World.SpaceHooks.SceneFog(on);   // off below Quality High
            if (!on) return;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 0f;
            RenderSettings.fogEndDistance = end * M;
            RenderSettings.fogColor = StationTables.VosskFog;
        }
        /// <summary>Turns the player's ship by 'gameRadians' (the original: +0xe0 += dx px, yaw = +0xe0 / 120).</summary>
        public void RotateShip(float gameRadians)
        {
            if (PlayerFlying) return;
            flingVelocity = 0f;
            shipYaw += gameRadians;
            ApplyShipYaw();
        }
        /// <summary>Lets the ship keep turning after a drag; slows by x0.9 per 20 ms frame (normalised).</summary>
        public void FlingShip(float gameRadiansPerSecond) => flingVelocity = PlayerFlying ? 0f : gameRadiansPerSecond;
        /// <summary>Remake mods: the hangar is the station's own model docked EVERSPACE 2's way (World.Es2Dock): the ship rests on
        /// the dock's DockingTransform (its hull's middle on it, facing along it), the camera still on the dock's spring arm.</summary>
        bool Es2Docked => customHangar != null && customHangar.es2Dock;
        void ApplyShipYaw()
        {
            if (playerShip == null || PlayerFlying) return;
            if (Es2Docked)
            {
                // (room metres: hangarRoot's frame, the world's unless docked inside the orbit)
                var rot = Quaternion.LookRotation(customHangar.shipAhead - customHangar.ship, Vector3.up);
                var local = ShipLocalBounds(playerShip, false);
                playerShip.SetLocalPositionAndRotation(customHangar.ship - rot * Vector3.Scale(local.center, playerShip.localScale), rot);
                return;
            }
            playerShip.rotation = OrbitLayout.RotationToUnity(new Vector3(0f, shipYaw, 0f));
            // Modified: a hull off its pivot stands centred on the pad and turns about its own middle (CentreOnPad).
            if (padCentre != Vector3.zero) playerShip.position = padPivot - playerShip.TransformVector(padCentre);
        }
        /// <summary>Modified: the player's ship centred on its pad (ApplyShipYaw keeps it so as it turns). The arrival flight
        /// lands on the centred spot (it flies to where the ship stands).</summary>
        void CentreOnPad()
        {
            padCentre = Vector3.zero;
            if (playerShip == null) return;
            padPivot = playerShip.position;
            if (shipIndex != OffCentreShip) return;
            var local = ShipLocalBounds(playerShip, false);
            if (local.size != Vector3.zero) padCentre = new Vector3(local.center.x, 0f, local.center.z);
        }
        // ---- audio -------------------------------------------------------------------------------------------
        /// <summary>The screen's ambience event (the FEV's LGCY data, CycleSound): 122 Station_Atmo_Mainview on the main view,
        /// 95 Station_Atmo_Hangar while the hangar window is open (ModStation::OnKeyPress stops 122 and plays 95), 108
        /// Station_Atmo_Lounge in the lounge.</summary>
        void PlayAmbience()
        {
            if (mainViewAtmo == null)
            {
                mainViewAtmo = CycleSound.MainView(gameObject, mainViewAmbience, mainViewAdds);
                hangarAtmo = CycleSound.Hangar(gameObject, hangarAmbience, hangarAdds);
                // The two lounge add definitions: every Add_* wave but Add_1 (Add_1) / but Add_11 (Add_2).
                var adds = loungeAdds ?? new AudioClip[0];
                loungeAtmo = CycleSound.Lounge(gameObject, loungeAmbience,
                                               System.Array.FindAll(adds, c => c != null && c.name != "Station_Atmo_Lounge_Add_1"),
                                               System.Array.FindAll(adds, c => c != null && c.name != "Station_Atmo_Lounge_Add_11"));
            }
            // Modified (a player's request): an evacuated Ginoya station (Evacuated: system 27 before 158, its lounge and hangar
            // empty) is silent, no crowd or hangar bustle; it comes back with its people and trade at 158.
            var want = Evacuated ? null : View == StationView.Lounge ? loungeAtmo : hangarWindowOpen ? hangarAtmo : mainViewAtmo;
            foreach (var a in new[] { mainViewAtmo, loungeAtmo, hangarAtmo }) if (a != want && a.IsPlaying) a.Stop();
            if (want != null && !want.IsPlaying) want.Play();
        }
        /// <summary>The hangar window opened / closed: its own ambience (95) instead of the main view's (122).</summary>
        public void SetHangarWindowOpen(bool open)
        {
            if (hangarWindowOpen == open) return;
            hangarWindowOpen = open;
            PlayAmbience();
        }
    }
}
