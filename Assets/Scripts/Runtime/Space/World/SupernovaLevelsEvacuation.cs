// SupernovaLevelsEvacuation.cs
// Part of SupernovaLevels: levels 87-102: the flight with Carla, Luur's supernova (cutscene), the miners and evacuees of
// Valpatro, Tadram and Luur, the "Meanwhile..." cutscenes, Genoh, Alioth and the carrier evacuation

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SupernovaLevels
    {
        void Build87()
        {
            // LevelScript ctor: the player at (0, 0, 210000) facing the station; the chat is the objective (0x16).
            MovePlayer(new Vector3(0, 0, 210000), new Vector3(0, 0, -1));
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;
        }

        // 89: "Meanwhile in Midorian space..." (campaign_levels_b.md mission 89).
        void Build89()
        {
            if (sn != null && sn.supernovaIntroSky != null) { RenderSettings.skybox = sn.supernovaIntroSky; SkyReflection.Update(); }
            var friend = new Route(true); friend.points.Add(new Vector3(-63000, 0, 75000)); friend.points.Add(new Vector3(-60000, 0, 110000));
            var enemy = new Route(true); enemy.points.Add(new Vector3(-63000, -5000, 75000)); enemy.points.Add(new Vector3(-60000, -5000, 110000));
            // [0] the container that becomes the "beam" target, [1] intact Luur, [2] burning Luur (hidden).
            // The container: (-25000, 800, 120000) + L * 1000 facing L (up (0, 1, 0)), then translate(3000, 0, 3000).
            var box0 = Static("container_003_terran", new Vector3(-25000, 800, 120000) + LightGame * 1000f + new Vector3(3000, 0, 3000), Vector3.zero, -1, 0, -1);
            if (box0 != null) box0.Place(box0.transform.position, Dir(LightGame));
            var luurRot = new Vector3(0, Mathf.PI / 2f, 0);
            Static("station_111_luur_intact_mission_89", new Vector3(-77000, -3000, 90000), luurRot, -1, 0, -1);
            var burning = Static("sn_burning_station_fire_intro", new Vector3(-77000, -3000, 90000), luurRot, -1, 0, -1);
            burning.SetVisible(false);
            // [3-10] Midorian fighters on the two loops (no hostility), [11] a freighter flying on. Level::createCampaignMission:
            // 0 / 1 at the friend loop's first point, 2 / 3 at its second, 4-6 at the enemy loop's second, 7 at its first.
            for (int i = 0; i < 8; i++)
            {
                var at = i < 2 ? friend.points[0] : i < 4 ? friend.points[1] : i < 7 ? enemy.points[1] : enemy.points[0];
                c.SpawnShip(3, NpcTables.RandomFighter(3), at, true, s => { s.alwaysFriend = true; s.route = friend.Clone(); s.noLoot = true; });
            }
            var f = c.SpawnShip(3, 15, new Vector3(-63000, -3000, 65000), false, s => { s.freighter = true; s.group = NpcGroup.Special; s.alwaysFriend = true; s.noLoot = true; });
            // LevelScript ctor: HUD / radar off, the player hidden and invulnerable; the camera by Luur, looking past it.
            level.EndStartSequence();
            EnterCutscene(false);
            SetPlayerVisible(false);
            var luur = S(1);
            var luurPos = G(luur);
            // The look-at helper (+0xcc): at Luur facing -L (up (0, 1, 0)), moveForward(-100000) and translate(right * 200000):
            // 100 000 units sunward of Luur, 200 000 to its own right; it drifts back along its right, so the view swings onto
            // the sun just as it explodes. The original's right is Unity's left (HelperRight89): the z flip mirrors the frame, and
            // with transform.right the helper started on the wrong side, the camera looking into empty space past Luur (PR #56).
            helper = new GameObject("Cutscene helper");
            helper.transform.rotation = Quaternion.LookRotation(Dir(-LightGame), Vector3.up);
            helper.transform.position = ToUnity(luurPos + LightGame * 100000f) + HelperRight89 * 200000f * M;
            cam.LookAt(luurPos + new Vector3(10000, 1500, -20000), helper.transform);
            c.MusicOwned = true;
            c.PlayMusic(sn?.supernovaIntro, false);
            Step = 1;
        }

        // 91: Valpatro, the damaged freighter.
        void Build91()
        {
            MovePlayer(new Vector3(0, 0, -130000), new Vector3(0, 0, 1));
            var wreck = Static("sn_cargo_001_midorian_wrecked", new Vector3(-20000, 0, 60000), new Vector3(0, 5.4978f, 0), -1, 0, 3,
                               s => { s.hitpoints = Mathf.Max(1, NpcTables.Hull(1, 15) / 20); s.inactive = true; });
            wreck.RadarHidden = true;
            c.FailObjective = () => c.ShipDestroyed(0);   // Objective(1, 0): the wreck destroyed
            // PlayerFixedObject::update animates the wreck's +0x14 transform; +0xc (the *_wrecked_anim root) only from the
            // break-up on (below): held until then.
            wreckAnims.Clear();
            foreach (var a in wreck.GetComponentsInChildren<PartAnimation>(true))
                if (a.gameObject.name.Contains("wrecked_anim")) { a.Hold(); wreckAnims.Add(a); }
            wreckBroken = false;
            // FMOD 3D event 0x8e9 LOOP kept at the wreck every frame.
            wreckLoop = wreck.gameObject.AddComponent<AudioSource>();
            wreckLoop.clip = GoF2Remake.Modding.ModSounds.Get(sn != null ? sn.transferLoop : null);
            wreckLoop.loop = true;
            wreckLoop.spatialBlend = 0f;
            wreckLoop.volume = 0f;
            if (wreckLoop.clip != null) wreckLoop.Play();
        }

        // 92: Tadram, the first Specter attack.
        void Build92()
        {
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(R(140000) - 70000, 0, -70000 - 35000 * R(3));
                Specter(at, true, s => { s.asleep = true; }).CloakingPossible = false;
            }
            var freighter = Static("cargo_001_midorian", new Vector3(80000, 0, 110000), Vector3.zero, 3209, ObjectDocking.DropOff, 4);
            MovePlayer(new Vector3(0, 0, 200000), G(freighter) - new Vector3(0, 0, 200000));
            c.WinObjective = () => c.Radio != null && c.Radio.LastOver;   // 0x16
            c.FailObjective = () => c.ShipDestroyed(3);                          // 1 (3): the freighter destroyed
        }

        // 94: Luur, the burning station's evacuation.
        void Build94()
        {
            for (int i = 0; i < 6; i++) Specter(new Vector3(-700000, 0, -700000), true, s => s.asleep = true).CloakingPossible = false;
            var burning = Static("sn_burning_station_mission_object", Vector3.zero, new Vector3(0, -2.356f, 0), 3208, ObjectDocking.Pickup, 6);
            var freighter = Static("cargo_001_midorian", new Vector3(30000, -5000, 40000), Vector3.zero, 3209, ObjectDocking.DropOff, 4);
            // [8, 9] Midorian shuttles looping station -> (20000, -3000, 30000) -> freighter, 12 s at each end.
            for (int i = 0; i < 2; i++)
            {
                var s = c.SpawnShip(3, NpcTables.RandomFighter(3), new Vector3(0, 2000 * i, 3000), false,
                                    sp => { sp.alwaysFriend = true; sp.noLoot = true; });
                s.SetOnlyEnemy(null);
                shuttles.Add(new ShuttleRoutes.Shuttle { ship = s, points = new[] { Vector3.zero, new Vector3(20000, -3000, 30000), new Vector3(30000, -5000, 40000) },
                                           dockMs = new[] { 12000f, 0f, 12000f }, dropOff = 2, perUnitMs = 1500f, leg = i,
                                           dockAt = new[] { burning?.transform, null, freighter?.transform }, dockSets = new[] { 6, 0, 4 } });
            }
            c.FailObjective = () => c.ShipDestroyed(7);   // Objective(1, 7): the freighter destroyed
        }

        // "Meanwhile, back on Thynome station..." (95, 99, 109, 119, 126, 133, 160, 161): the ctor block 0x15f852.
        void BuildMeanwhile()
        {
            level.EndStartSequence();
            level.MovePlayer(Vector3.zero, Quaternion.identity);
            EnterCutscene(false);
            SetPlayerVisible(false);
            cam.LookAt(new Vector3(-15000, 800, 75000), null, Vector3.zero);
            cam.SetDolly(new Vector3(1f, 0f, -2f));
            if ((built == 126 || built == 133) && sn != null && sn.katashunMusic != null) { c.MusicOwned = true; c.PlayMusic(sn.katashunMusic, true); }
        }

        void Build97()
        {
            var wp = new Vector3(0, 0, 50000);
            const int n = 12;
            for (int i = 0; i < n; i++) c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), wp, true, s => s.alwaysEnemy = true);
            for (int i = 0; i < 4; i++) c.SpawnShip(2, NpcTables.RandomFighter(2), wp, true, s => s.alwaysFriend = true);
            for (int i = 0; i < 3; i++)
                c.SpawnShip(2, 15, wp, true, s => { s.freighter = true; s.group = NpcGroup.Special; s.stationary = true; s.alwaysFriend = true; });
            c.WinObjective = () => c.DeadRange(0, n);   // 0x12 (0, N)
        }

        void Build100()
        {
            var p = PlayerGame + new Vector3(Sign() * R(50000) + 20000, Sign() * R(50000) + 10000, Sign() * R(50000) + 20000);
            for (int i = 0; i < 2; i++) Specter(p, true);
            c.WinObjective = () => Over(1);   // Objective(4, 1)
        }

        // 102: Tadram, the carrier evacuation.
        void Build102()
        {
            MovePlayer(new Vector3(90000, 6000, 150000), -new Vector3(90000, 6000, 150000));
            var carrierPos = new Vector3(-50000, 1000, 70000);
            var carrier = Static("sn_carrier_terran_1", carrierPos, Vector3.zero, -1, ObjectDocking.DropOff, 5);
            // [1] the damaged Tadram: the original's object is inactive (docking type 0, the orbit's own station stands in) and
            // only the dropships use its pads (set 10, rotation (0, pi, 0) = Unity identity). Remake (a player's report): the
            // player may ferry evacuees too, so it is a hidden pick-up target on those pads in the station's place (its model
            // off, no volume or hit cube of its own: the station has them), named like the station.
            var st = level.Database.Stations.Find(x => x.index == Session.StationIndex);
            var tadramObj = Static("sn_station_113_midorian", Vector3.zero, new Vector3(0f, Mathf.PI, 0f), -1, ObjectDocking.Pickup, 10, sp =>
            {
                sp.name = st != null ? $"{st.name} {Localization.Get(136)}" : null;
                sp.collisionId = -1;
                sp.hitRadius = 1f;
            });
            if (tadramObj != null)
            {
                foreach (var r in tadramObj.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                tadramObj.Target.untargetable = true;
                if (level.Navigation != null) level.Navigation.StationReplaced = true;
            }
            var tadram = tadramObj != null ? tadramObj.transform : level.Station != null ? level.Station.transform : new GameObject("Tadram pads").transform;
            var stops = new[] { carrierPos, new Vector3(-30000, 1000, 40000), Vector3.zero };
            var starts = new[] { carrierPos + new Vector3(10000, 6000, -20000), new Vector3(30000, 8000, -35000), new Vector3(35000, 8000, -40000), new Vector3(40000, 8000, -45000) };
            for (int i = 0; i < 4; i++)
            {
                var s = c.SpawnShip(0, 51, starts[i], false, sp => { sp.alwaysFriend = true; sp.noLoot = true; });
                s.SetOnlyEnemy(null);
                shuttles.Add(new ShuttleRoutes.Shuttle { ship = s, points = stops, dockMs = new[] { 20000f, 0f, 20000f }, dropOff = 0, perUnitMs = 200f, leg = i % 3, rhino = true,
                                           dockAt = new[] { carrier?.transform, null, tadram }, dockSets = new[] { 5, 0, 10 } });
            }
            for (int i = 0; i < 4; i++) Specter(new Vector3(1e6f, 1e6f, 1e6f), false, s => s.asleep = true).CloakingPossible = false;
            c.WinObjective = () => Over(8);                   // Objective(4, 8)
            c.FailObjective = () => c.DeadRange(2, 6);        // 0x12 (2, 6): every dropship lost
        }

        // 89 (LevelScript.c 2473-2647).
        void Tick89(float dtMs)
        {
            var luur = S(1);
            if (Step < 3 && helper != null)
            {
                // k isn't clamped: from 35 s the dolly runs back.
                float k = 1f - T / 35000f;
                cam.SetDolly(new Vector3(k, 0, 2f * k));
                helper.transform.position -= HelperRight89 * 7f * dtMs * (1f - T / 50000f) * M;
            }
            var freighter = S(11);
            if (freighter != null) freighter.transform.position += freighter.transform.forward * dtMs * M;
            switch (Step)
            {
                case 1:
                    if (T >= 30001f)
                    {
                        // The container rushes past as the blast front: camera + (0, 500, 0), moveForward(-10000) (behind the camera,
                        // facing the sun). Its trail (+0xdc, projectile_009) points back along -its direction, scale (1, 1, 500000),
                        // its animation once at half speed.
                        var box = S(0);
                        if (box != null && cam.Camera != null) { box.transform.position = cam.Camera.position + Vector3.up * 500f * M; box.transform.position -= box.transform.forward * 10000f * M; }
                        var back = box != null ? -box.transform.forward : Vector3.back;
                        beam = Scenery("projectile_009_anim_add", ToGame(box != null ? box.transform.position : Player.position), Quaternion.LookRotation(back, Vector3.up), "Supernova front");
                        if (beam != null)
                        {
                            beam.transform.localScale = new Vector3(1f, 1f, 500000f);
                            foreach (var a in beam.GetComponentsInChildren<PartAnimation>(true)) { a.applyMaterialChannels = true; a.speed = 0.5f; }
                            PartAnimation.PlayOnce(beam);
                        }
                        Step = 2;
                    }
                    break;
                case 2:
                {
                    var box = S(0);
                    if (box != null && cam.Camera != null)
                    {
                        float d = (cam.Camera.position - box.transform.position).magnitude / M;
                        cam.Rumble = (1f - Mathf.Min(d / 7000f, 1f)) * 100f;   // setRumblePercentage(p * 100, 30)
                        cam.RumbleAmplitude = 30;
                        if (d < 200000f) box.transform.position += box.transform.forward * (10f * dtMs + 0.1f * d) * M;
                        if (beam != null) beam.transform.position = box.transform.position;
                    }
                    // 0x8c8, 2D (FModSound::play without a position).
                    if (T >= 38000f && T - dtMs < 38000f && sn != null && cam.Camera != null) Sfx.PlayAt(sn.explosion, cam.Camera.position);
                    if (T >= 38001f && level.Backdrop != null) level.Backdrop.sunScaleFactor *= Mathf.Pow(0.95f, dtMs / OriginalFrame.Ms);
                    if (T >= 39001f) { c.Fade(false, Color.white, 500f); Step = 3; }
                    break;
                }
                case 3:
                    // The sun x4 a frame until its scale reaches 10; the rumble (1, 100) holds to the end.
                    if (level.Backdrop != null && level.Backdrop.sunScaleFactor * level.Layout.sunScale < 10f) level.Backdrop.sunScaleFactor *= Mathf.Pow(4f, dtMs / OriginalFrame.Ms);
                    cam.Rumble = 1f;
                    cam.RumbleAmplitude = 100;
                    if (c.FadeDone)
                    {
                        // switchSunForSupernovaIntro: the explosion's ring and core take the sun's place.
                        if (level.Backdrop != null && sn != null)
                        {
                            level.Backdrop.sunScaleFactor = 1f;
                            level.Backdrop.StartSupernovaExplosion(sn.sunExplosionRing, sn.sunExplosionCoreTexture, sn.sunExplosionCore, sn.sunExplosionRingTexture,
                                                                   SkyLayerAssets.Load()?.flaresMaterial);
                        }
                        c.Fade(true, Color.white, 10000f);
                        // The trail stays where it was, no longer animated.
                        if (beam != null) foreach (var a in beam.GetComponentsInChildren<PartAnimation>(true)) a.speed = 0f;
                        if (S(2) != null) S(2).SetVisible(true);
                        if (luur != null) luur.SetVisible(false);
                        for (int i = 3; i < c.Ships.Count; i++) if (S(i) != null && S(i).Target.Alive) S(i).Target.Damage(9999999, true, Vector3.zero);
                        Step = 4;
                    }
                    break;
                case 4:
                    level.Backdrop?.GrowSupernova(dtMs);   // scaleSunDuringSupernovaIntro
                    if (stepMs >= 7000f && stepMs - dtMs < 7000f) c.Fade(false, Color.black, 1000f);
                    // departStation(10) + Station::setAttackedFriends(false): Thynome forgives any friendly fire.
                    if (stepMs >= 8000f && c.FadeDone) { Session.AttackedStations.Remove(10); AdvanceAndDock(10); Step = 5; }
                    break;
            }
        }

        void Tick91(float dtMs)
        {
            var wreck = S(0);
            var dock = level.Docking;
            if (wreckLoop != null && cam.Camera != null)
            {
                float d = Vector3.Distance(cam.Camera.position, wreckLoop.transform.position);
                wreckLoop.volume = wreck != null && wreck.Target.Alive ? Mathf.Clamp01(1f - d / Sfx.AudibleMeters) * Settings.SfxVolume : 0f;
            }
            // 8+ aboard after 180 s: sound 0x8ea once, and the wreck animates its second transform (the break-up, unverified).
            if (!wreckBroken && Session.StoryCounter >= 8 && T > 179999f)
            {
                wreckBroken = true;
                if (sn != null && wreck != null) Sfx.PlayAt(sn.container, wreck.transform.position);
                foreach (var a in wreckAnims) if (a != null) { a.loop = false; a.Restart(); }
            }
            switch (Step)
            {
                case 0:
                    if (Triggered(3) && wreck != null) { wreck.Wake(); wreck.RadarHidden = false; wreck.DockingType = 0; wreck.Target.displayName = Localization.Get(3211); Step = 1; }
                    // Remake: the damaged freighter is a docking target from the start of its call (dock type 0 can't dock):
                    break;
                case 1:
                    // The docking target shows now; docking at it with no transfer yet (the radio line calls everyone aboard).
                    if (wreck != null && wreck.DockingType == 0) wreck.DockingType = ObjectDocking.DropOff;   // lockable, no transfer
                    if (dock != null && dock.IsDocked && dock.Target == wreck) Step = 2;
                    break;
                case 2:
                    if (Over(5) && wreck != null) { wreck.DockingType = ObjectDocking.Pickup; Step = 3; }
                    break;
                case 3:
                    if (Session.StoryCounter >= 10) { c.FailObjective = null; Step = 4; }
                    break;
                case 4:
                    if (Over(6) && dock != null && !dock.Busy) Step = 5;
                    break;
                case 5:
                    if (stepMs < 3000f) break;
                    level.Navigation?.SetAutopilot(null);
                    EnterCutscene(true);
                    if (wreck != null) Player.rotation = Quaternion.LookRotation((Player.position - wreck.transform.position).normalized, Vector3.up);
                    cam.LookAt(PlayerGame + PlayerDirGame * 10000f + GameRight(Player) * 700f, Player);
                    // Modified: the break-up (the wreck's *_wrecked_anim pieces drifting apart) plays as it dies if the 180 s
                    // rule above hasn't started it yet (PlayerFixedObject's dying state plays its wreck animation; a player's
                    // report: only the explosion showed, the pieces never separated).
                    if (!wreckBroken)
                    {
                        wreckBroken = true;
                        foreach (var a in wreckAnims) if (a != null) { a.loop = false; a.Restart(); }
                    }
                    if (wreck != null) wreck.Target.Damage(9999999, true, Vector3.zero);
                    Step = 6;
                    break;
                case 6:
                    if (stepMs >= 2000f && playerSpeed < 100f) playerSpeed = Mathf.Max(playerSpeed, 1f) * Mathf.Pow(1.05f, dtMs / OriginalFrame.Ms);
                    if (stepMs >= 8000f) AdvanceAndTravel(1, 113);
                    break;
            }
        }

        // 92 (process 5640-6166).
        void Tick92(float dtMs)
        {
            var freighter = S(3);
            float[] slow = { 2.0f, 2.08f, 2.05f }, mid = { 2.0f, 2.3f, 2.2f }, fast = { 6.0f, 4.6f, 4.4f };
            void Fly(float[] v) { for (int i = 0; i < 3; i++) if (S(i) != null) S(i).scriptedSpeed = v[i]; }
            switch (Step)
            {
                case 0: if (Triggered(2)) Step = 1; break;
                case 1:
                    if (Session.StoryCounter != 0 || freighter == null) break;
                    Story.Mission.type = StoryType.Level;   // the unloading is done: the radio decides from here
                    var f = G(freighter);
                    var offs = new[] { new Vector3(-76000, 5000, 5000), new Vector3(-79000, 4900, 7500), new Vector3(-79000, 4900, 2500) };
                    for (int i = 0; i < 3; i++) { Show(S(i), f + offs[i], new Vector3(1, 0, 0)); S(i).scriptedSpeed = slow[i]; }
                    EnterCutscene(true);
                    var s0 = S(0);
                    cam.LookAt(G(s0) + GameDir(s0.transform) * 7000f + GameRight(s0.transform) * 700f + GameUp(s0.transform) * 300f, s0.transform);
                    freighter.DockingType = 0;
                    freighter.Target.displayName = "";
                    Step = 2;
                    break;
                case 2:
                    Fly(slow);
                    if (Triggered(5)) { var s = S(0); cam.LookAt(G(s) + GameDir(s.transform) * 3900f + GameRight(s.transform) * 500f + GameUp(s.transform) * 400f, s.transform); Step = 3; }
                    break;
                case 3:
                    Fly(mid);
                    if (Over(5)) { for (int i = 0; i < 3; i++) { S(i).CloakingPossible = true; S(i).Cloak(18000f); } Step = 4; }
                    break;
                case 4: Fly(mid); if (stepMs >= 2000f) Step = 5; break;
                case 5:
                    Fly(fast);
                    if (stepMs < 2000f) break;
                    for (int i = 0; i < 3; i++) if (S(i) != null) S(i).scriptedSpeed = -1f;
                    LeaveCutscene();
                    Step = 6;
                    break;
                case 6: if (stepMs >= 3000f) Step = 7; break;
                case 7:
                    if (!Triggered(8)) break;
                    level.Docking?.Undock();
                    EnterCutscene(false);
                    cam.LookAt(PlayerGame + PlayerDirGame * 7000f + GameRight(Player) * 700f, Player);
                    Step = 8;
                    break;
                case 8:
                    if (freighter != null) freighter.transform.position += freighter.transform.forward * 2f * dtMs * M;
                    if (stepMs >= 8000f && freighter != null)
                    {
                        cam.LookAt(G(freighter) + GameDir(freighter.transform) * 10000f + GameRight(freighter.transform) * 4800f, freighter.transform);
                        timerMs = 0f;
                        freighterSpeed = 2f;
                        if (sn != null) Sfx.PlayAt(sn.carrierJump, freighter.transform.position);   // 0x8c9 CS_92_FreighterJump
                        Step = 9;
                    }
                    break;
                case 9:
                case 10:
                    freighterSpeed += 0.006f * dtMs;
                    if (freighter != null) freighter.transform.position += freighter.transform.forward * freighterSpeed * dtMs * M;
                    if (Step == 9 && stepMs >= 2000f) Step = 10;
                    if (timerMs >= 7000f) { LeaveCutscene(); if (freighter != null) { freighter.SetVisible(false); freighter.Deactivate(); } Step = 11; }
                    break;
            }
        }

        // 94 (process 6167-6348).
        void Tick94()
        {
            switch (Step)
            {
                case 0:
                    if (Session.StoryCounter <= 6 && T <= 60000f) break;
                    Show(S(0), new Vector3(50000, 1000, 140000), new Vector3(0, 0, -1));
                    Show(S(1), new Vector3(53000, 1000, 140000), new Vector3(0, 0, -1));
                    S(0).scriptedSpeed = S(1).scriptedSpeed = 3f;
                    EnterCutscene(true);
                    cam.LookAt(G(S(0)) + GameDir(S(0).transform) * 8000f + GameRight(S(0).transform) * 1200f - GameUp(S(0).transform) * 300f, S(0).transform);
                    Step = 1;
                    break;
                case 1:
                    if (stepMs < 6000f) break;
                    S(0).scriptedSpeed = S(1).scriptedSpeed = -1f;
                    S(0).CloakingPossible = S(1).CloakingPossible = true;
                    LeaveCutscene();
                    Step = 2;
                    break;
                case 2:
                case 3:
                    if (stepMs < 100000f) break;
                    int a = Step == 2 ? 2 : 4;
                    Show(S(a), PlayerGame + new Vector3(-40000, 3000, 20000), PlayerGame - (PlayerGame + new Vector3(-40000, 3000, 20000)));
                    Show(S(a + 1), PlayerGame + new Vector3(-43000, 3000, 20000), PlayerGame - (PlayerGame + new Vector3(-43000, 3000, 20000)));
                    S(a).CloakingPossible = S(a + 1).CloakingPossible = true;
                    Step++;
                    break;
            }
        }

        // The "Meanwhile..." cutscenes: 2000 ms -> the caption (event 1), over -> 2000 ms -> statusValue 1.
        void TickMeanwhile()
        {
            switch (Step)
            {
                case 0: if (stepMs >= 2000f) Step = 1; break;
                case 1: if (Over(0)) Step = 2; break;
                case 2: if (stepMs >= 2000f) { Story.Mission.value = 1; Step = 3; } break;
            }
        }

        // 102 (process 6349-6903).
        void Tick102(float dtMs)
        {
            var carrier = S(0);
            switch (Step)
            {
                case 0:
                    if (!Triggered(0) || carrier == null) break;
                    EnterCutscene(false);
                    SetPlayerVisible(false);
                    cam.LookAt(G(carrier) + new Vector3(9000, -7000, 40000), carrier.transform);
                    c.MusicOwned = true;
                    level.Traffic.MusicMuted = true;
                    c.PlayMusic(sn?.mission102, false);
                    Step = 1;
                    break;
                case 1:
                    cam.SetDolly(new Vector3(0.6f, 0.8f, -2.5f));
                    if (Over(2)) { if (timerMs < 0f || specterWake == 0f) specterWake = T; }
                    if (specterWake > 0f && T - specterWake >= 2000f)
                    {
                        LeaveCutscene();
                        // Radar::draw 0x157c6c takes the music from here: the calm track at the target is this loop (2241,
                        // Traffic.CalmClip), and when the Specters attack 149 / 150 replace it (the loop isn't in the battle
                        // set); the level kept the loop through the attacks.
                        c.PlayMusic(null, false);
                        c.MusicOwned = false;
                        level.Traffic.MusicMuted = false;
                        Step = 2;
                    }
                    break;
                case 2:
                    if (stepMs < 30000f) break;
                    level.Docking?.Abort();   // remake: the player may be at a pad (Build102)
                    WakeSpecters102(true);
                    var lead = S(6);
                    EnterCutscene(false);
                    SetPlayerVisible(false);
                    cam.LookAt(G(lead) + GameDir(lead.transform) * 4500f + GameRight(lead.transform) * 600f + GameUp(lead.transform) * 400f, lead.transform);
                    Step = 3;
                    break;
                case 3:
                    for (int i = 6; i <= 9; i++) if (S(i) != null) S(i).scriptedSpeed = i == 6 ? 2.3f : 2.2f;
                    if (S(6) != null) cam.SetDolly(GameDir(S(6).transform) * 2.04f + GameRight(S(6).transform) * 0.04f);
                    if (stepMs < 10000f) break;
                    for (int i = 6; i <= 9; i++) if (S(i) != null) { S(i).scriptedSpeed = -1f; S(i).CloakingPossible = true; }
                    LeaveCutscene();
                    timerMs = 0f;
                    Step = 4;
                    break;
                case 4:
                    if (timerMs >= 60000f) { timerMs = 0f; WakeSpecters102(false); }
                    // A Specter whose target died takes the first living dropship, else the player.
                    for (int i = 6; i <= 9; i++)
                    {
                        var s = S(i);
                        if (s == null || !s.Target.Alive || s.ScriptEnemy == null || s.ScriptEnemy.Alive) continue;
                        Target next = level.Health.Target;
                        for (int k = 2; k <= 5; k++) if (S(k) != null && S(k).Target.Alive) { next = S(k).Target; break; }
                        s.SetOnlyEnemy(next);
                    }
                    if (Story.Mission.value >= 10 || carrier == null) break;
                    level.Docking?.Abort();
                    carrier.DockingType = 0;
                    if (S(1) != null) { S(1).DockingType = 0; S(1).RadarHidden = true; }
                    if (level.Navigation != null) level.Navigation.StationReplaced = false;
                    Story.Mission.type = StoryType.Level;
                    EnterCutscene(false);
                    SetPlayerVisible(false);
                    cam.LookAt(G(carrier) + new Vector3(9000, -7000, 40000), carrier.transform);
                    for (int i = 2; i <= 9; i++) Remove(S(i));
                    if (sn != null) Sfx.PlayAt(sn.carrierJump, carrier.transform.position);
                    Step = 5;
                    break;
                case 5:
                    carrier.transform.position += carrier.transform.forward * 6f * dtMs * M;
                    cam.SetDolly(new Vector3(0.4f, 0.1f, -1.8f));
                    if (stepMs >= 8000f) Step = 6;
                    break;
                case 6:
                    carrier.transform.position += carrier.transform.forward * 200f * dtMs * M;
                    if (stepMs >= 4000f) { carrier.SetVisible(false); LeaveCutscene(); Step = 7; }
                    break;
            }
        }

        /// <summary>102's Specters: the first wave by the carrier and around the player; later the dead ones come back.</summary>
        void WakeSpecters102(bool first)
        {
            var carrierPos = G(S(0));
            for (int i = 6; i <= 9; i++)
            {
                var s = S(i);
                if (s == null) continue;
                if (!first && s.Target.Alive && !s.Gone) continue;
                Vector3 at = first && i == 6 ? carrierPos + new Vector3(-82000, 2000, 0)
                           : first && i == 7 ? carrierPos + new Vector3(-84000, 1200, -2000)
                           : PlayerGame + new Vector3(Sign() * (35000 + R(10000)), R(10000) - 5000, Sign() * (35000 + R(10000)));
                if (!first) s.Revive(ToUnity(at));
                Show(s, at, new Vector3(1, 0, 0));
                if (first && i <= 7) s.Cloak(1000f, true);   // cloak(1000, true)
                var drop = S(2 + R(4));
                s.SetOnlyEnemy(R(5) == 0 || drop == null || !drop.Target.Alive ? level.Health.Target : drop.Target);
                if (first) s.scriptedSpeed = 2.2f;
            }
        }

        /// <summary>94 / 102: at the drop-off the evacuees go aboard the freighter / carrier (1 per 1500 ms, the Rhinos 1 per
        /// 200 ms). At 94 they stop unloading once the rest fits the player's cabins (the player brings the last load).</summary>
        void Unload(ShuttleRoutes.Shuttle sh, float dtMs)
        {
            if (sh.leg != sh.dropOff || Story.Mission.type != StoryType.Passengers) return;
            sh.tickMs += dtMs;
            int cabins = Freelance.MaxPassengers(level.Database);
            bool stop = !sh.rhino && Story.Mission.value <= cabins;
            while (sh.tickMs >= sh.perUnitMs) { sh.tickMs -= sh.perUnitMs; if (!stop && Story.Mission.value > 0) Story.Mission.value--; }
        }
    }
}
