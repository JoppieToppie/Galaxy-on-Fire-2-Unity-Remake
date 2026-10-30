// SupernovaLevelsFinale.cs
// Part of SupernovaLevels: levels 154-158: Alice's betrayal in the Void (154, Hans' route), the final battle at Var Lupra
// with the Valkyrie (157) and the duel with Trunt Harval (158)

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SupernovaLevels
    {
        // 154: the Void, Alice's betrayal.
        void Build154()
        {
            var p = new Vector3(0, 0, 140000);
            MovePlayer(p, -p);
            var at = p + GameRight(Player) * 2600f + PlayerDirGame * 4000f;
            var hans = c.SpawnShip(0, 51, at, false, s => { s.alwaysFriend = true; s.hitpoints = 9999999; s.noLoot = true; s.nameText = 1652; });
            hans.Place(ToUnity(at), Player.forward);
            hans.SetOnlyEnemy(null);
            hans.frozen = true;
            // Level case 0x9a: setRotation(0, pi, 0) (disassembly 0xc4ce0), like every PlayerStation and 157's Valkyrie.
            var valkyrie = Static("v_station_battlestation_anim_mission_object", Vector3.zero, new Vector3(0f, Mathf.PI, 0f), 77, ObjectDocking.Hackable, 7);
            // Level::createStaticObject(0x4220) 0xcdcee: Transform::Update(the length) once, unfolded; PlayerFixedObject::update
            // never advances an idle animation after that.
            PartAnimation.HoldAllAtEnd(valkyrie.gameObject);
            valkyrie.RadarHidden = true;
            valkyrie.DockingType = 0;   // hackable once Alice turns on the player
            for (int i = 2; i < 22; i++)
            {
                float sx = i % 2 == 0 ? 1f : -1f;
                var off = i <= 10 ? new Vector3(sx * (900 + R(100)), -(R(200) + 1), R(400) - 200) : new Vector3(sx * (1600 + R(100)), -(101 + R(100)), 1000 + R(500));
                var q = new Vector3(0, 0, 40000) + off * i;
                var s = c.SpawnShip(Standing.Void, NpcTables.RandomFighter(Standing.Void), q, false, sp => { sp.alwaysEnemy = true; sp.inactive = true; });
                s.Place(ToUnity(q), Dir(p - q));
                s.SetHull(s.Hp.maxHull * 2);
                s.SetOnlyEnemy(null);
            }
            c.WinObjective = () => Over(11);   // Objective(4, 11)
        }

        // 157: Var Lupra, the final battle.
        void Build157()
        {
            var fr = new Route(false); fr.points.Add(new Vector3(70000, 0, 20000)); fr.points.Add(new Vector3(30000, 10000, 60000));
            for (int i = 0; i < 10; i++) c.SpawnShip(0, NpcTables.RandomFighter(0), fr.points[0], true, s => { s.alwaysFriend = true; s.route = fr; });
            Static("sn_carrier_terran_1", new Vector3(30000, -15000, -50000), Vector3.zero, -1, 0, -1, s => s.race = 0);
            var start = new Vector3(-110000, 10000, 170000);
            var er = new Route(false); er.points.Add(start); er.points.Add(new Vector3(-110000, 0, 20000));
            // createCampaignMission 0x9d: the Specters (+-100 per axis) and Harval face the route, normalize(wp1 - wp0) = game -z:
            // toward the Valkyrie.
            var fleetDir = Dir(er.points[1] - er.points[0]);
            for (int k = 0; k < 10; k++)
            {
                var off = k < 2 ? new Vector3(-1000 + 2000 * k, 0, 5000) : k < 5 ? new Vector3(3000 * k - 9000, 0, 7000) : k < 9 ? new Vector3(3000 * k - 20000, 0, 9000) : new Vector3(0, 0, 11000);
                off += new Vector3(R(200) - 100, R(200) - 100, R(200) - 100);
                var s = Specter(start + off, false, sp => { sp.inactive = true; sp.route = er; });
                s.Place(ToUnity(start + off), fleetDir);
                s.SetVisible(false);
                s.CloakingPossible = false;
            }
            var harval = c.SpawnShip(2, 49, start, false, s => { s.alwaysEnemy = true; s.nameText = 1636; s.inactive = true; s.route = er; s.speed = 5f; s.noLoot = true; });
            harval.Place(ToUnity(start), fleetDir);
            harval.SetVisible(false);
            harval.CloakingPossible = false;
            ArmHarval(harval);
            // Alice: race 3, AI off, hidden and inactive (no always-friend, normal hit points).
            var alice = c.SpawnShip(3, 20, new Vector3(50000, 50000, 50000), false, s => { s.nameText = 1623; s.inactive = true; s.noLoot = true; });
            alice.SetVisible(false);
            // [23] Valkyrie with the plasma gun at (-120000, 0, 20000).
            c.AddPlaceholder();
            var vp = new Vector3(-120000, 0, 20000);
            var vrot = OrbitLayout.RotationToUnity(new Vector3(0, Mathf.PI, 0));
            var valkyrie = Scenery("v_station_battlestation_anim_mission_object", vp, vrot, "Valkyrie");
            valkyrieGun = Scenery("sn_plasma_gun_valkyrie", vp, vrot, "Valkyrie plasma gun");
            PartAnimation.HoldAllAtEnd(valkyrie);   // PlayerStation ctor at 0x9d (0x14727a): unfolded, the burning stages on its arms
            valkyrieGo = valkyrie;
            valkyrieAt = vp;
            valkyrieRot = vrot;
            valkyrieSound = false;
            // The ctor's burning stages 1 (0x4a97 / 0x4a98) and 2 (0x4a99 / 0x4a9a), hidden until state 9.
            stage1 = Scenery("sn_burning_valkyrie_stage_1", vp, vrot, "Valkyrie burning 1");
            stage2 = Scenery("sn_burning_valkyrie_stage_2", vp, vrot, "Valkyrie burning 2");
            if (stage1 != null) stage1.SetActive(false);
            if (stage2 != null) stage2.SetActive(false);
            MovePlayer(new Vector3(100000, 0, 20000), new Vector3(-1, -0.2f, -0.5f));
        }

        // 158: Luur, the duel with Trunt Harval.
        void Build158()
        {
            var hp = new Vector3(9000, 0, -13000);
            var harval = c.SpawnShip(Standing.Specter, 49, hp, false, s => { s.alwaysEnemy = true; s.nameText = 1636; s.speed = 5.3f; s.noLoot = true; });
            harval.Place(ToUnity(hp), Dir(new Vector3(-1, 0, 3)));
            harval.CloakingPossible = false;
            harval.scriptedSpeed = 0f;
            harval.SetVisible(false);
            ArmHarval(harval);
            var hpts = harval.Target.hitpoints;
            hpts.SetEmp(hpts.maxEmp * 3, hpts.empRecoveryMs);   // createCampaignMission 0x9e: EMP points x3
            for (int i = 0; i < 3; i++)
                c.SpawnShip(Standing.Specter, -1, new Vector3(50000, 50000, 50000), false, s =>
                {
                    s.group = NpcGroup.Turret; s.turretAssembly = "sn_sentry_gun_003"; s.hitpoints = 100; s.noLoot = true; s.inactive = true; s.alwaysEnemy = true; s.stationary = true;
                });
            level.EndStartSequence();
            // Ctor: the camera at (-50000, 0, 15000) + (-28000, 500, 8500); the helper 50 000 toward the sun from it; the
            // player moved to the helper, turned toward (camera - helper) + (20000, 0, 0), pushed 30 000 forward, drifting
            // at 0.1 u/ms ("blown off course"). Harval rides the helper (hidden until 25 s).
            var p = new Vector3(-50000, 0, 15000);
            var camAt = p + new Vector3(-28000, 500, 8500);
            var helperAt = camAt + LightGame * 50000f;
            var face = (camAt - helperAt + new Vector3(20000, 0, 0)).normalized;
            MovePlayer(helperAt + face * 30000f, face);
            EnterCutscene(false);
            helper = new GameObject("Cutscene helper");
            helper.transform.position = ToUnity(helperAt);
            cam.LookAt(camAt, harval.transform);
            c.Fade(true, Color.white, 8000f);   // startFade(false, -1, 8000): from white, the supernova's white-out carries over
            playerSpeed = 0.1f;
            Step = 1;
            c.WinObjective = () => Over(10);   // Objective(0x16, 0): the last radio line (Keith once Harval is dead) is over
        }

        /// <summary>Level::assignGuns at 0x9d / 0x9e: Harval's Scimitar fires item 7 x3, and the Shesha (0xd6) x4 in slot 1.</summary>
        static void ArmHarval(NpcShip harval)
        {
            harval.SetGun(7, 3f);
            harval.SetSecondaryGun(214, 4f);
        }

        void Tick154()
        {
            var hans = S(0);
            var valkyrie = S(1);
            var dock = level.Docking;
            switch (Step)
            {
                case 0:
                    if (!Triggered(0)) break;
                    // The cut: the Liberator killed, turret view and free look off, no shooting; an enabled auto turret is
                    // switched off (remembered in this+0xaa, back on with the controls).
                    level.Weapons?.KillLiberator();
                    level.FreeLook?.Set(FreeLookCamera.Mode.Standard, false);
                    level.Turret?.SetTurretView(false);
                    level.Weapons?.SetPrimaryHeld(false);
                    // Remake: any of the ship's auto turrets (SetAuto switches them all).
                    PlayerTurret cutAutoTurret = null;
                    foreach (var cutTurret in level.Turrets) if (cutTurret != null && cutTurret.IsAuto) { cutAutoTurret = cutTurret; break; }
                    autoTurretWasOn = cutAutoTurret != null && cutAutoTurret.AutoEnabled;
                    if (autoTurretWasOn) cutAutoTurret.SetAuto(false, false);
                    EnterCutscene(false);
                    var f2 = S(2);
                    if (f2 != null) { Show(f2, G(f2), PlayerGame - G(f2)); cam.LookAt(G(f2) + new Vector3(300, 300, 5800), f2.transform); }
                    for (int i = 2; i < 22; i++) if (S(i) != null) { Show(S(i), G(S(i)), PlayerGame - G(S(i))); S(i).scriptedSpeed = 0f; }
                    Step = 1;
                    break;
                case 1:
                    // The Void fighters creep toward the player at 0.5 u/ms while Alice talks.
                    for (int i = 2; i < 22; i++) if (S(i) != null) S(i).scriptedSpeed = 0.5f;
                    if (Over(1) && stepMs >= 1500f) { for (int i = 2; i < 22; i++) if (S(i) != null) { S(i).SetVisible(false); S(i).scriptedSpeed = 0f; } cam.LookAt(PlayerGame + new Vector3(-350, 400, -1500), Player); Step = 2; } break;
                case 2: if (Over(2) && stepMs >= 1000f && valkyrie != null) { cam.LookAt(G(valkyrie) + new Vector3(0, 4000, 26000), valkyrie.transform); Step = 3; } break;
                case 3: if (Over(3) && stepMs >= 1000f) { cam.LookAt(PlayerGame + new Vector3(-350, 400, -1500), Player); Step = 4; } break;
                case 4: if (Over(4) && stepMs >= 1000f && valkyrie != null) { cam.LookAt(G(valkyrie) + new Vector3(0, 4000, 42000), valkyrie.transform); Step = 5; } break;
                case 5:
                    if (!Over(5) || stepMs < 1000f) break;
                    cam.LookAt(PlayerGame + new Vector3(150, 400, -1500), Player);
                    // setPosition(-10000, 0, 22000) and AI on (heading kept): Hans flies his route to Valkyrie's pad.
                    if (hans != null) { hans.Place(ToUnity(new Vector3(-10000, 0, 22000)), hans.transform.forward); hans.frozen = false; if (valkyrie != null) StartHansRoute(hans, valkyrie); }
                    for (int i = 2; i < 22; i++) if (S(i) != null) S(i).scriptedSpeed = 0f;
                    Step = 6;
                    break;
                case 6: if (Over(6) && stepMs >= 1000f && hans != null) { cam.LookAt(G(hans) + new Vector3(200, 300, 1800), hans.transform); Step = 7; } break;
                case 7:
                    // KIPlayer::isDocked (PlayerFighter state 9): Hans on Valkyrie's pad (it used to fly into the hull's centre).
                    if (hansRoute == null || hansRoute.finished || hansRoute.leg > 1 || (hansRoute.leg == 1 && hansRoute.waiting)) Step = 8;
                    break;
                case 8: if (stepMs >= 2000f) Step = 9; break;
                case 9:
                    if (!Over(7) || stepMs < 2000f) break;
                    for (int i = 2; i < 22; i++)
                    {
                        var s = S(i);
                        if (s == null) continue;
                        s.SetVisible(true);
                        s.Wake();
                        s.scriptedSpeed = -1f;
                        s.SetOnlyEnemy(i % 3 == 0 && hans != null ? hans.Target : level.Health.Target);
                    }
                    if (valkyrie != null)
                    {
                        valkyrie.SetRace(Standing.Pirate);
                        valkyrie.alwaysFriend = false;
                        valkyrie.alwaysEnemy = true;
                        valkyrie.RadarHidden = false;
                        valkyrie.DockingType = ObjectDocking.Hackable;
                    }
                    LeaveCutscene();
                    if (autoTurretWasOn) foreach (var backTurret in level.Turrets) if (backTurret != null && backTurret.IsAuto) { backTurret.SetAuto(true, false); break; }
                    Step = 10;
                    break;
                case 10:
                    if (Over(8) && c.TimeLimitMs <= 0f && !hacked)
                    {
                        c.TimeLimitMs = c.MissionMs + 91000f;
                        c.FailObjective = () => c.TimeLeftMs == 0f;   // Objective(3, 91000) counted from here
                    }
                    if (dock != null && dock.HackingWon && dock.Target == valkyrie && !hacked)
                    {
                        hacked = true;
                        c.FailObjective = null;
                        c.TimeLimitMs = 0f;
                        for (int i = 2; i < 22; i++) S(i)?.SetOnlyEnemy(null);
                        c.Event = 11;
                        Step = 11;
                    }
                    break;
            }
        }

        // 157 (campaign_levels_c.md 3.17): the fight, then the ending of the add-on's story.
        void Tick157(float dtMs)
        {
            var harval = S(21);
            var alice = S(22);
            var vp = new Vector3(-120000, 0, 20000);
            switch (Step)
            {
                case 0:
                    helper = new GameObject("Cutscene helper");
                    helper.transform.position = ToUnity(new Vector3(-120000, 0, 5000));
                    Step = 1;
                    break;
                case 1:
                    if (!Over(0)) break;
                    EnterCutscene(false);
                    level.Weapons?.KillLiberator();   // state 1 also ends the Liberator's rocket control (0x164042)
                    // The camera at the helper (-120000, 0, 5000) + (-10000, 5000, 25000), looking at it.
                    cam.LookAt(new Vector3(-130000, 5000, 30000), helper.transform);
                    Step = 2;
                    break;
                case 2:
                    helper.transform.position += ToUnity(new Vector3(0, 0, 3f * dtMs)) - ToUnity(Vector3.zero);
                    cam.SetDolly(new Vector3(0, 0, 1f));
                    if (stepMs < 16000f) break;
                    for (int i = 11; i <= 21; i++) { var s = S(i); if (s == null) continue; s.Wake(); s.SetVisible(true); s.scriptedSpeed = 0f; }
                    // 0x165d0c: the helper to the Valkyrie, the camera at Harval + (-1500, -5000, 2500) still looking at the helper:
                    // down the fleet line at the Valkyrie 150 km away.
                    helper.transform.position = ToUnity(vp);
                    if (harval != null) cam.LookAt(G(harval) + new Vector3(-1500, -5000, 2500), helper.transform);
                    flyInMs = 0f;
                    Step = 3;
                    break;
                case 3:
                case 4:
                {
                    // this+0x90 runs from state 3's start through state 4: the camera rises dt * max(1 - t / 15000, 0).
                    flyInMs += dtMs;
                    cam.SetDolly(new Vector3(0f, Mathf.Max(1f - flyInMs / 15000f, 0f), 0f));
                    if (Step == 3) { if (stepMs >= 8000f) Step = 4; break; }
                    // State 4: moveForward(0.02 * t98) an original frame, t98 from state 4's start, stopping near 7000.
                    float t98 = Mathf.Min(stepMs, 7000f);
                    for (int i = 11; i <= 21; i++) if (S(i) != null) S(i).scriptedSpeed = t98 * 0.02f / OriginalFrame.Ms;
                    if (!Over(3) || flyInMs < 18001f) break;
                    // 0x16a6ce: KIPlayer::translate shifts each ship and its route; the Specters by (120000, rnd(12000) - 6000,
                    // rnd(30000) - 87000), Harval by (20000, rnd(6000) - 3000, rnd(6000) - 7000); AI on, the Specters may cloak.
                    for (int i = 11; i <= 21; i++)
                    {
                        var s = S(i);
                        if (s == null) continue;
                        var shift = i < 21 ? new Vector3(120000, R(12000) - 6000, R(30000) - 87000) : new Vector3(20000, R(6000) - 3000, R(6000) - 7000);
                        s.transform.position += ToUnity(shift) - ToUnity(Vector3.zero);
                        s.scriptedSpeed = -1f;
                        s.CloakingPossible = i < 21;
                        var r = new Route(false);
                        r.points.Add(new Vector3(-110000, 0, 20000) + shift);   // the route's remaining point, shifted
                        s.SetRoute(r);
                    }
                    LeaveCutscene();
                    harvalCloakMs = t98;   // the 25 s cloak toggle runs on the same timer, still ~7000 from the fly-in
                    c.Event = 5;
                    Step = 5;
                    break;
                }
                case 5:
                {
                    harvalCloakMs += dtMs;
                    if (harval != null && harvalCloakMs >= 25001f) { harvalCloakMs = 0f; harval.CloakingPossible = !harval.CloakingPossible; }
                    int dead = 0;
                    for (int i = 11; i <= 21; i++) if (c.ShipDestroyed(i)) dead++;   // KIPlayer::isDead
                    bool done = (dead >= 9 && T > 200000f) || (harval != null && harval.Target.HullFraction < 0.25f);
                    if (!done) break;
                    level.Weapons?.KillLiberator();   // isInRocketControl -> setRocketControl(null) + killLiberator
                    EnterCutscene(false);
                    var camAt = vp + new Vector3(45000, 2000, 11000);
                    cam.LookAt(camAt, alice != null ? alice.transform : null, ToUnity(vp));
                    if (alice != null) { var at = camAt + new Vector3(15000, -200, 1000); Show(alice, at, vp - at); alice.scriptedSpeed = 0f; }
                    if (harval != null) { harval.Target.invulnerable = true; harval.CloakingPossible = false; }   // +0x13c = 0
                    Step = 6;
                    break;
                }
                case 6:
                    // Alice flies at 3 u/ms toward the Valkyrie until she is level with it (x).
                    if (alice != null) alice.scriptedSpeed = G(alice).x > valkyrieAt.x ? 3f : 0f;
                    if (!Triggered(10)) break;
                    Remove(alice);
                    if (valkyrieGo != null) cam.SetTarget(valkyrieGo.transform);
                    Step = 7;
                    break;
                case 7:
                {
                    // The Valkyrie backs away, accelerating over 10 s to 10 u/ms (moveForward(-10 dt * t / 10000), facing -z).
                    if (!Over(10)) { stepMs = 0f; break; }
                    if (!valkyrieSound && sn != null) { valkyrieSound = true; Sfx.PlayAt(sn.carrierJump, cam.Camera != null ? cam.Camera.position : Player.position); }   // 0x8cb CS_94_ValkyrieJump
                    MoveValkyrie(10f * dtMs * Mathf.Min(stepMs, 10000f) / 10000f);
                    if (valkyrieAt.z <= 100000f) break;
                    var camPos = GameCamera();
                    if (harval != null)
                    {
                        var at = camPos + new Vector3(4000, -600, -2000);
                        chaseDir = (valkyrieAt + new Vector3(0, 0, 40000) - at).normalized;
                        harval.Place(ToUnity(at), Dir(chaseDir));
                        harval.scriptedSpeed = 0f;
                        harval.SetVisible(true);
                    }
                    // The player sits near the camera on the same heading (setComputerControlled; the speed is lost in the
                    // decompilation, the remake keeps it drifting at the base speed).
                    var pAt = camPos + new Vector3(5000, 300, -14000);
                    MovePlayer(pAt, chaseDir);
                    playerSpeed = 4f;   // 0x16e638 setSpeed(4.0)
                    Step = 8;
                    break;
                }
                case 8:
                    MoveValkyrie(10f * dtMs);
                    if (harval != null) { harval.scriptedSpeed = 4f; harval.scriptedFire = true; }
                    if (stepMs <= 12000f || !Over(12)) break;
                    cam.LookAt(valkyrieAt + new Vector3(35000, 0, 70000), valkyrieGo != null ? valkyrieGo.transform : null);
                    if (harval != null) { harval.Place(harval.transform.position, Dir(Vector3.forward)); harval.SetVisible(false); harval.scriptedFire = false; }
                    // 0x16cd3c: the player turned to (0, 0, 1) at setSpeed(10), still shown: flying beside the Valkyrie.
                    Player.rotation = Quaternion.LookRotation(Dir(Vector3.forward), Vector3.up);
                    playerSpeed = 10f;
                    burnMs = -1f;
                    blastMs = -1f;
                    Step = 9;
                    break;
                case 9:
                {
                    MoveValkyrie(10f * dtMs);
                    if (harval != null)
                    {
                        // Harval comes back after 9000 ms beside the camera and chases the Valkyrie at 2 u/ms, firing.
                        if (stepMs >= 9000f && stepMs - dtMs < 9000f)
                        {
                            harval.Place(ToUnity(GameCamera() + new Vector3(4000, -600, -2000)), harval.transform.forward);
                            harval.SetVisible(true);
                        }
                        harval.scriptedSpeed = 2f;
                        harval.scriptedFire = stepMs >= 9000f;
                        harval.transform.rotation = Quaternion.LookRotation(Dir(valkyrieAt - G(harval)), Vector3.up);
                    }
                    // After radio 13 the first explosion (0x8c4) at + (-2000, 1000, -8000); 300 ms later burning stage 1;
                    // then one every 8000 ms at + (-6000, 2000, -10000) (the timer restarts at -2000 after 6000).
                    if (blastMs < 0f && Over(13))
                    {
                        Blast(valkyrieAt + new Vector3(-2000, 1000, -8000), combat?.explosionMid, 5f);
                        blastMs = 0f;
                        burnMs = 300f;
                    }
                    if (burnMs > 0f && (burnMs -= dtMs) <= 0f && stage1 != null)
                    {
                        stage1.transform.SetPositionAndRotation(ToUnity(valkyrieAt), valkyrieRot);
                        stage1.SetActive(true);
                    }
                    if (blastMs >= 0f && (blastMs += dtMs) > 6000f)
                    {
                        blastMs = -2000f;
                        Blast(valkyrieAt + new Vector3(-6000, 2000, -10000), combat?.explosionMid, 5f);
                    }
                    if (stage1 != null && stage1.activeSelf) stage1.transform.position = ToUnity(valkyrieAt);
                    if (!Over(14)) break;
                    cam.LookAt(valkyrieAt + new Vector3(50000, 0, 70000), valkyrieGo != null ? valkyrieGo.transform : null);
                    if (harval != null) { harval.SetVisible(false); harval.scriptedFire = false; harval.scriptedSpeed = 0f; }
                    SetPlayerVisible(false);
                    if (stage2 != null) { stage2.transform.SetPositionAndRotation(ToUnity(valkyrieAt), valkyrieRot); stage2.SetActive(true); }
                    if (stage1 != null) stage1.SetActive(false);
                    timerMs = 0f;
                    Step = 10;
                    break;
                }
                case 10:
                    // The Valkyrie stops; the camera translates (-2, 0, -0.33) u/ms (0x16ec74); at 9000 ms the big blast (0x8c3,
                    // scale 10, at the Valkyrie - L * 3000) with setRumblePercentage(100, 30) for that one frame, else (0, 0).
                    cam.SetDolly(new Vector3(-2f, 0f, -0.33f));
                    cam.Rumble = 0f;
                    if (timerMs >= 9000f && timerMs - dtMs < 9000f)
                    {
                        Blast(valkyrieAt - LightGame * 3000f, combat?.explosionBig, 10f);
                        cam.Rumble = 100f;
                        cam.RumbleAmplitude = 30;
                    }
                    if (!Over(15) || stepMs <= 3000f) break;
                    // The beam (0x4a75 + 0x4a76 / 0x4a77) at the Valkyrie facing (0, 0, -1), up (0, 1, 0); never re-aimed.
                    beam = Scenery("sn_plasma_gun_fx_valkyrie_beam_anim_add", valkyrieAt, Quaternion.LookRotation(Dir(new Vector3(0, 0, -1)), Vector3.up), "Plasma beam");
                    if (beam != null) { beam.SetActive(false); SetBeamAnimating(false); }
                    Step = 11;
                    break;
                case 11:
                    cam.SetDolly(new Vector3(-3f, 0f, -0.33f));   // 0x16ed6c
                    cam.Rumble = 0f;
                    if (stepMs > 6000f && stepMs - dtMs <= 6000f)
                    {
                        if (beam != null) { beam.SetActive(true); SetBeamAnimating(true); }   // animates from 6001 ms
                        if (sn != null) Sfx.PlayAt(sn.valkyrieBeam, cam.Camera != null ? cam.Camera.position : Player.position);   // 0x8c7
                    }
                    if (stepMs <= 10000f) break;
                    helper.transform.SetPositionAndRotation(ToUnity(valkyrieAt), Quaternion.LookRotation(Dir(LightGame), Vector3.up));
                    cam.SetTarget(helper.transform);
                    cam.SetDolly(Vector3.zero);
                    Remove(harval);
                    Step = 12;
                    break;
                case 12:
                    // The camera pulls back (-20, 0, -20) u/ms for 3 s (0x16eeb4), then (-3, 0, -10) until 7 s; the rumble
                    // (100, int(t / 6500 * 30)) builds to the blast's strength; the helper races to the sun while t <= 4999
                    // (0x16f010); the beam animates until 5999 ms, then stays as it is.
                    cam.SetDolly(stepMs < 3000f ? new Vector3(-20f, 0f, -20f) : stepMs < 7000f ? new Vector3(-3f, 0f, -10f) : Vector3.zero);
                    int amp = (int)(stepMs / 6500f * 30f);
                    cam.Rumble = amp > 0 ? 100f : 0f;
                    cam.RumbleAmplitude = Mathf.Max(1, amp);
                    if (stepMs <= 4999f) helper.transform.position += ToUnity(LightGame * 35f * dtMs) - ToUnity(Vector3.zero);
                    if (stepMs >= 6000f) SetBeamAnimating(false);
                    if (stepMs >= 5000f && stepMs - dtMs < 5000f)
                    {
                        // Level::switchSkyboxForSupernovaReversal: the system's normal sky, no more flares.
                        foreach (var r in Object.FindObjectsByType<SkyLayers>(FindObjectsInactive.Exclude)) r.gameObject.SetActive(false);
                    }
                    // 0x16f20e: from 5001 to 5999 ms the sun billboard x0.8 an original frame until 5499, then x1.7: it collapses,
                    // then balloons.
                    if (stepMs >= 5001f && stepMs < 6000f && level.Backdrop != null)
                        level.Backdrop.sunScaleFactor *= Mathf.Pow(stepMs <= 5499f ? 0.8f : 1.7f, dtMs / OriginalFrame.Ms);
                    if (stepMs >= 5500f && stepMs - dtMs < 5500f) Blast(valkyrieAt + new Vector3(0, 0, -12000) - LightGame * 3000f, combat?.explosionBig, 25f);
                    if (stepMs >= 5700f && stepMs - dtMs < 5700f)
                    {
                        // The beam and the station (its gun is a child) go; burning stage 2 stays.
                        if (beam != null) beam.SetActive(false);
                        if (valkyrieGo != null) valkyrieGo.SetActive(false);
                        if (valkyrieGun != null) valkyrieGun.SetActive(false);
                        c.Fade(false, Color.white, 6600f - stepMs);
                        if (sn != null && cam.Camera != null) Sfx.PlayAt(sn.explosion, cam.Camera.position);   // 0x8c8 Supernova_Explosion, 2D
                    }
                    if (stepMs >= 6500f) AdvanceAndTravel(1, 111);
                    break;
            }
        }

        /// <summary>moveForward(-d) on the station mesh (it faces game -z): the Valkyrie and its gun go d units along +z.</summary>
        void MoveValkyrie(float d)
        {
            valkyrieAt.z += d;
            var p = ToUnity(valkyrieAt);
            if (valkyrieGo != null) valkyrieGo.transform.position = p;
            if (valkyrieGun != null) valkyrieGun.transform.position = p;
            if (stage2 != null && stage2.activeSelf) stage2.transform.position = p;
        }

        Vector3 GameCamera()
        {
            var t = cam.Camera != null ? cam.Camera : Player;
            var u = t.position / M;
            return new Vector3(u.x, u.y, -u.z);
        }

        /// <summary>LevelScript's Explosion (type 0 with fire streaks) at a game position.</summary>
        void Blast(Vector3 gamePos, AudioClip[] clips, float scale = 4f) =>
            Explosion.Spawn(0, ToUnity(gamePos), Vector3.forward, scale, CombatAssets.Pick(clips), true);

        /// <summary>The plasma beam's animation runs only in its windows (state 11 from 6001 ms, state 12 until 5999 ms).</summary>
        void SetBeamAnimating(bool on)
        {
            if (beam == null) return;
            foreach (var a in beam.GetComponentsInChildren<PartAnimation>(true)) a.speed = on ? 1f : 0f;
        }

        // 158 (campaign_levels_c.md 3.18).
        void Tick158(float dtMs)
        {
            var harval = S(0);
            switch (Step)
            {
                case 1:
                {
                    // The camera drifts (-0.7 (1 - t / 34000), 0, -0.7) u/ms; the helper faces (9000, 0, -13000) and flies
                    // 2.5 u/ms until 30 s; Harval rides it (shown from 25 s); the player turns dt / 2000 rad (the axis is
                    // lost in the decompilation: the remake rolls it); Harval's exhaust is off from 20 s to 34 s.
                    // 0x1621dc: translate(4.8 dt (1 - t / 34000), 0, -0.7 dt (1 - t / 34000)).
                    float drift = 1f - T / 34000f;
                    cam.SetDolly(new Vector3(4.8f * drift, 0f, -0.7f * drift));
                    if (helper != null)
                    {
                        var hp = ToGame(helper.transform.position);
                        var to = (new Vector3(9000, 0, -13000) - hp).normalized;
                        if (T < 30000f) helper.transform.position += ToUnity(to * 2.5f * dtMs) - ToUnity(Vector3.zero);
                    }
                    if (harval != null)
                    {
                        if (T > 25000f && harval.Hidden) harval.SetVisible(true);
                        if (helper != null) harval.transform.position = helper.transform.position;
                        harval.scriptedSpeed = 0f;
                        if (T >= 20000f && T - dtMs < 20000f) harval.SetExhaust(false);
                    }
                    // 0x16c296: AEGeometry::rotate(dt / 2000) on all three axes.
                    float tumble = dtMs / 2000f * Mathf.Rad2Deg;
                    Player.Rotate(tumble, tumble, tumble, Space.Self);
                    if (T < 34000f) break;
                    if (harval != null) harval.SetExhaust(true);
                    cam.SetDolly(Vector3.zero);   // the camera holds still in state 2
                    Step = 2;
                    break;
                }
                case 2:
                    // Harval coasts, speeding up to 300 units per original frame over 3 s (moveForward(min(t, 3000) * 0.1)).
                    if (harval != null) harval.scriptedSpeed = Mathf.Min(stepMs, 3000f) * 0.1f / OriginalFrame.Ms;
                    if (stepMs <= 12000f || !Over(2)) break;
                    playerSpeed = 0f;
                    LeaveCutscene();
                    Ship.SetThrottle(1f);   // 0x1691c6 PlayerEgo::setSpeed(2.0): full base speed
                    if (harval != null)
                    {
                        harval.scriptedSpeed = -1f; harval.CloakingPossible = true; harval.SetOnlyEnemy(level.Health.Target);
                        Player.rotation = Quaternion.LookRotation((harval.transform.position - Player.position).normalized, Vector3.up);   // facing Harval
                    }
                    timerMs = 0f;
                    cloakMs = 0f;
                    Step = 3;
                    break;
                case 3:
                    cloakMs += dtMs;
                    if (harval != null && cloakMs >= 25000f) { cloakMs = 0f; harval.CloakingPossible = !harval.CloakingPossible; }
                    if (timerMs < 4000f || harval == null || !harval.Target.Alive) break;
                    timerMs = 0f;
                    if ((harval.transform.position - Player.position).magnitude / M < 25000f && R(100) > 60)
                        for (int i = 1; i <= 3; i++)
                        {
                            var s = S(i);
                            if (s == null || !(s.Inactive || s.Gone)) continue;
                            s.Revive(harval.transform.position);
                            s.Wake();
                            s.SetVisible(true);
                            break;
                        }
                    break;
            }
        }

        /// <summary>154: Hans' route (createCampaignMission 0x9a, Route(int*, Array*, int*, 9)): (-10000, 1000, 15000), docked on
        /// Valkyrie's pad (docking set 7, the waypoint's parameter 10000 = the stay), back to (-10000, 1000, 15000). A player's
        /// report: he flew into the battlestation's centre and the camera stared at its hull.</summary>
        void StartHansRoute(NpcShip hans, NpcShip valkyrie)
        {
            if (hansRoute != null) { shuttles.Undock(hansRoute); hansRoute.leg = 0; hansRoute.waiting = hansRoute.finished = false; return; }
            var wp = new Vector3(-10000, 1000, 15000);
            hansRoute = new ShuttleRoutes.Shuttle { ship = hans, points = new[] { wp, Vector3.zero, wp }, dockMs = new[] { 0f, 10000f, 0f }, dropOff = -1, once = true,
                                      dockAt = new[] { null, valkyrie.transform, null }, dockSets = new[] { 0, 7, 0 } };
            shuttles.Add(hansRoute);
        }
    }
}
