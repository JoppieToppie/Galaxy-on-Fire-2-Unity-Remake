// SupernovaLevelsPlasmaArray.cs
// Part of SupernovaLevels: levels 131-145: the plasma array fly-by (131), Coromesk's titanium (135), the Vossk battleships
// (139), the plasma tutorial (142), Harval's ultimatum and the array destroyed (144 / 145)

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SupernovaLevels
    {
        // 131: Var Lupra, the plasma array fly-by.
        void Build131()
        {
            level.EndStartSequence();
            var p = new Vector3(-65000, 0, 95000);
            MovePlayer(p, -p);
            EnterCutscene(true);
            playerSpeed = 2f;   // the autopilot's full throttle (the camera drifts at 2.01 u/ms)
            cam.LookAt(p - PlayerDirGame * 2400f + GameUp(Player) * 500f - GameRight(Player) * 600f, Player);
            cam.SetDolly(PlayerDirGame * 2.01f + GameRight(Player) * 0.03f + new Vector3(0, 0.01f, 0));
            Step = 1;
            c.WinObjective = () => Over(0);   // Objective(4, 0)
        }

        // 135: Coromesk, the titanium for the mining plant.
        void Build135()
        {
            MovePlayer(new Vector3(70000, 0, 100000), -new Vector3(70000, 0, 100000));
            c.SpawnShip(3, -1, Vector3.zero, false, s =>
            {
                var plant = Traffic.MiningPlant();
                s.group = plant.group; s.fixedObject = plant.fixedObject; s.stationary = true; s.alwaysFriend = true; s.hitpoints = 9999999;
                s.noLoot = true; s.nameText = plant.nameText; s.dockingType = plant.dockingType; s.spacePoints = plant.spacePoints; s.hitRadius = plant.hitRadius;
                s.collisionId = plant.collisionId;
            });
            for (int i = 0; i < 2; i++) c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), new Vector3(7000000, 7000000, 7000000), false, s => { s.alwaysEnemy = true; s.asleep = true; s.noLoot = true; });   // cargo list deleted (+0x4c = 0)
            // Every asteroid of the orbit is titanium (ore 155).
            if (level.Asteroids != null)
                foreach (Transform t in level.Asteroids) { var tg = t.GetComponent<Target>(); if (tg != null) tg.oreItem = 155; }
        }

        // 139: Bra'Murr, the two Vossk battleships.
        void Build139()
        {
            var b0 = new Vector3(-50000, -1500, 70000);
            var b1 = new Vector3(-200000, -1500, 30000);
            Static("sn_battleship_vossk", b0, Vector3.zero, 1667, ObjectDocking.Hackable, 8, s => s.race = 1);
            Static("sn_battleship_vossk", b1, Vector3.zero, 1667, ObjectDocking.Hackable, 8, s => { s.race = 1; s.name = Localization.Get(1667) + " 2"; });
            foreach (var host in new[] { b0, b1 })
                foreach (var off in BattleshipTurrets)
                    c.SpawnShip(1, -1, host + off, false, s =>
                    {
                        s.group = NpcGroup.Turret; s.turretAssembly = "turret_003_static"; s.scale = 6f; s.hitpoints = 1000; s.noLoot = true; s.stationary = true;
                    });
            var loop0 = new Route(true); loop0.points.Add(new Vector3(-70000, 0, 80000)); loop0.points.Add(new Vector3(-30000, 0, 60000));
            var loop1 = new Route(true); loop1.points.Add(new Vector3(-220000, 0, 40000)); loop1.points.Add(new Vector3(-180000, 0, 20000));
            for (int i = 0; i < 5; i++) c.SpawnShip(1, NpcTables.RandomFighter(1), b0, true, s => { s.route = loop0; s.alwaysFriend = true; });
            for (int i = 0; i < 5; i++) c.SpawnShip(1, NpcTables.RandomFighter(1), b1, true, s => { s.route = loop1; s.alwaysFriend = true; });
            MovePlayer(new Vector3(20000, 0, 190000), b1 - new Vector3(20000, 0, 190000));
            Session.StoryCounter = 0;
        }

        // 142: Kernstal, plasma with Gunant.
        void Build142()
        {
            var wp = new Vector3(90000, 0, 42000);
            var start = new Vector3(60000, 0, 120000);
            MovePlayer(start, wp - start);
            var route = new Route(false);
            route.points.Add(wp);
            c.SetPlayerRoute(route);
            var at = start + GameRight(Player) * 2100f + PlayerDirGame * 2000f;
            var g = c.SpawnShip(3, 30, at, false, s => { s.alwaysFriend = true; s.hitpoints = 9999999; s.nameText = 1599; s.route = route.Clone(); s.noLoot = true; });
            g.Place(ToUnity(at), Player.forward);
            g.SetOnlyEnemy(null);
            c.WinObjective = () => Over(4);   // Objective(4, 4)
        }

        // 144: Var Lupra, Harval's ultimatum (cutscene).
        void Build144()
        {
            var a = new Vector3(60000, 10000, 100000);
            var b = new Vector3(-50000, 0, 50000);
            var facing = new Vector3(b.x - a.x, 0, b.z - a.z);
            var harval = c.SpawnShip(2, 49, a, false, s => { s.alwaysEnemy = true; s.nameText = 1636; s.noLoot = true; });
            harval.Place(ToUnity(a), Dir(facing));
            harval.CloakingPossible = false;
            harval.scriptedSpeed = 0f;
            foreach (var off in Formation)
            {
                var s = Specter(a, false);
                var p = a + Quaternion.LookRotation(facing.normalized) * (off + new Vector3(R(60) - 30, R(60) - 30, R(60) - 30));
                s.Place(ToUnity(p), Dir(facing));
                s.CloakingPossible = false;
                s.scriptedSpeed = 0f;
            }
            level.EndStartSequence();
            EnterCutscene(false);
            SetPlayerVisible(false);
            helper = new GameObject("Cutscene helper");
            helper.transform.position = ToUnity(new Vector3(30000, 10000, 160000));
            cam.LookAt(a + new Vector3(-4600, 1000, -4300), helper.transform);
            Step = 1;
        }

        // 145: Var Lupra, the plasma array destroyed.
        void Build145()
        {
            var harval = c.SpawnShip(2, 49, new Vector3(30000, 0, 80000), false, s => { s.alwaysEnemy = true; s.nameText = 1636; s.noLoot = true; });
            harval.Place(ToUnity(new Vector3(30000, 0, 80000)), Dir(new Vector3(-50000, 0, 50000) - new Vector3(30000, 0, 80000)));
            harval.CloakingPossible = false;
            harval.SetGun(214, 2f);   // Level::assignGuns 0x91, ship 0x31: the Shesha (0xd6, sort 0x28), damage x2
            for (int i = 1; i <= 12; i++)
            {
                var p = new Vector3(23000 + 3000 * (i - 1), (R(1000) - 500) * i + R(1000), 70000 + 2000 * i + R(2500));
                var s = Specter(p, false);
                s.Place(ToUnity(p), Dir(new Vector3(-50000, 0, 50000) - p));
                s.CloakingPossible = false;
            }
            // [13] the array's explosion (hidden until it goes), [14] the array (Level::createStaticObjects, stage 5 + glow).
            c.AddPlaceholder();
            explosionAnim = Scenery("sn_plasma_array_midorian_explosion_anim", new Vector3(-50000, 0, 50000), OrbitLayout.RotationToUnity(Vector3.zero), "Array explosion");
            if (explosionAnim != null) explosionAnim.SetActive(false);
            var array = level.Traffic.Ships.Find(s => s.Spec.fixedObject != null && s.Spec.fixedObject.StartsWith("sn_plasma_array"));
            c.Ships.Add(array);
            foreach (var s in c.Ships) if (s != null && s.Race == Standing.Specter || s == S(0)) { if (s != null) s.scriptedSpeed = 2f; }
            c.WinObjective = () => Over(2);   // Objective(4, 2)
        }

        // 131: the fly-by, then the controls back.
        void Tick131()
        {
            if (Step == 1 && stepMs >= 3000f) Step = 2;
            else if (Step == 2 && Over(0)) { playerSpeed = 0f; LeaveCutscene(); Step = 3; }
        }

        // 135 (campaign_levels_c.md 3.8).
        void Tick135()
        {
            var dock = level.Docking;
            // The pirates (race 8) at player + (25000 + 1000 * list index, 5000, 25000), targeting the player; onlyDead: the
            // 75 s wave revives the dead ones only.
            void Bring(bool onlyDead)
            {
                for (int k = 1; k <= 2; k++)
                {
                    var s = S(k);
                    if (s == null) continue;
                    bool dead = !s.Target.Alive || s.Gone;
                    if (onlyDead && !dead) continue;
                    var at = PlayerGame + new Vector3(25000 + 1000 * k, 5000, 25000);
                    if (dead) s.Revive(ToUnity(at));
                    Show(s, at, PlayerGame - at);
                    s.SetOnlyEnemy(level.Health.Target);
                }
            }
            switch (Step)
            {
                case 0:
                    if ((dock != null && dock.IsDocked) || T > 60000f) { Bring(false); timerMs = 0f; Step = 1; }
                    break;
                default:
                    // From state 1 on: half the titanium -> 2; every 75 000 ms the dead pirates come back (2 -> 3 on that tick).
                    if (Step == 1 && Story.Mission.value >= Story.Mission.goodsAmount / 2) Step = 2;
                    if (timerMs > 75000f)
                    {
                        timerMs = 0f;
                        if (Step == 2) Step = 3;
                        Bring(true);
                    }
                    break;
            }
        }

        // 139 (campaign_levels_c.md 3.10).
        void Tick139()
        {
            var dock = level.Docking;
            switch (Step)
            {
                case 0:
                    if (dock == null || !dock.HackingWon || dock.Target == null || dock.Target.DockingType != ObjectDocking.Hackable) break;
                    dock.Target.Target.displayName = "";
                    dock.Target.DockingType = 0;
                    dock.Undock();
                    Step = 1;
                    break;
                case 1:
                    if (Triggered(1) && !turned)
                    {
                        turned = true;
                        foreach (var s in c.Ships) if (s != null && s.Race == 1 && !s.IsFixed) { s.alwaysFriend = false; s.alwaysEnemy = true; s.turnedEnemy = true; }
                    }
                    if (dock == null || !dock.HackingWon || dock.Target == null || dock.Target.DockingType != ObjectDocking.Hackable) break;
                    // The other one carries the prism: its cargo bots (docking type 2, the counter from 0).
                    dock.Target.DockingType = ObjectDocking.Pickup;
                    Session.StoryCounter = 0;
                    Step = 2;
                    break;
            }
        }

        // 142 (campaign_levels_c.md 3.11).
        void Tick142()
        {
            var g = S(0);
            if (g != null && Step < 2 && c.PlayerRoute != null && c.PlayerRoute.points.Count > 0
                && (G(g) - c.PlayerRoute.points[0]).magnitude < 3000f) g.SetSpeed(0f);
            switch (Step)
            {
                case 0: if (c.PlayerRoute != null && c.PlayerRoute.Waypoint == null) Step = 1; break;
                case 1: if (level.GasClouds != null && level.GasClouds.AnyExploded) Step = 2; break;
                case 2:
                    for (int item = 201; item <= 205; item++)
                        if (Story.CargoOf(item) > 0) { if (g != null) g.SetSpeed(2.1f); Step = 3; break; }
                    break;
            }
        }

        // 144 (process case 0x90).
        void Tick144(float dtMs)
        {
            if (helper != null) helper.transform.position += ToUnity(new Vector3(1, 0, -1).normalized * 5f * dtMs) - ToUnity(Vector3.zero);
            // TargetFollowCamera::translate(0, 0.05 dt, 0.28 dt) every frame (0x162efa: r1 = 0, the 0.05 / 0.28 at 0x162df0;
            // Ghidra shows it as (0.28 dt, -, 0.28)). Read as (0.28, 0, 0.28) the camera flew through the fleet and left it
            // behind while the view followed the helper (a player's report: the shot ended on empty space).
            cam.SetDolly(new Vector3(0f, 0.05f, 0.28f));
            switch (Step)
            {
                case 1: Step = 2; break;
                case 2: if (Over(3)) Step = 3; break;
                case 3:
                    for (int i = 1; i < c.Ships.Count; i++)
                    {
                        var s = S(i);
                        if (s == null) continue;
                        if (!s.Cloaked && R(100) < 10) { s.CloakingPossible = true; s.Cloak(20000f, true); }   // cloak(20000, true)
                        s.scriptedSpeed = stepMs * 0.05f / OriginalFrame.Ms;
                    }
                    if (stepMs >= 4000f && S(0) != null) S(0).scriptedSpeed = (stepMs - 4000f) * 0.05f / OriginalFrame.Ms;
                    if (stepMs >= 7001f)
                    {
                        // LevelScript::process 0x90: the 145 attack is a stream-out arrival (the hidden gate of this gate orbit)
                        // only after index 143 was won in space here (Level::initStreamOutPositionAfterCutscene, set by
                        // MGame::successCheck); else initStreamOutPosition stays 0 and the player starts at the undock point
                        // (a player's report: always at the gate).
                        bool streamOut = Session.StreamOutAfterCutscene;
                        Session.StreamOutAfterCutscene = false;
                        AdvanceAndTravel(1, 112, streamOut);
                    }
                    break;
            }
        }

        // 145 (campaign_levels_c.md 3.13).
        void Tick145(float dtMs)
        {
            var harval = S(0);
            var arrayPos = new Vector3(-50000, 0, 50000);
            // LevelScript::process 0x91: the Specters fire straight ahead from state 2 on (not in state 4 after 1 s).
            for (int i = 1; i <= 12; i++) { var s = S(i); if (s != null) s.scriptedFire = Step >= 2 && Step != 5 && !(Step == 4 && stepMs > 1000f); }   // 5 = done
            switch (Step)
            {
                case 0: Step = 1; break;
                case 1:
                    if (!Over(0) || harval == null) break;
                    EnterCutscene(false);
                    SetPlayerVisible(false);
                    cam.LookAt(G(harval) + GameDir(harval.transform) * 18000f + GameRight(harval.transform) * 3000f, harval.transform);
                    Step = 2;
                    break;
                case 2:
                    if (stepMs < 1000f) break;
                    helper = Scenery("rocket_explosive", G(harval), harval.transform.rotation, "Harval's shot");
                    if (helper != null) { helper.transform.rotation = Quaternion.LookRotation((ToUnity(arrayPos) - helper.transform.position).normalized); cam.SetTarget(helper.transform); }
                    Step = 3;
                    break;
                case 3:
                    if (helper != null) helper.transform.position += helper.transform.forward * 8f * dtMs * M;
                    if (stepMs < 5000f) break;
                    Explosion.Spawn(0, ToUnity(arrayPos), Vector3.forward, 6f, CombatAssets.Pick(combat?.explosionBig), true);
                    // Level+0x54 (record 23 SET_EXPLOSION_BATTLESHIP) on the array's matrix, emitting until the aftermath ends.
                    if (S(14) != null) { arrayBurn = new WreckBurn(S(14).transform, true); arrayBurn.SetEmitting(true); }
                    Step = 4;
                    break;
                case 4:
                    if (stepMs < 324f) break;
                    var array = S(14);
                    if (array != null) array.SetVisible(false);
                    if (explosionAnim != null)
                    {
                        explosionAnim.SetActive(true);
                        foreach (var a in explosionAnim.GetComponentsInChildren<PartAnimation>(true)) { a.speed = 0.3f; a.loop = false; a.Restart(); }
                    }
                    // The shot's helper (+0xcc) stays the camera's target where its flight ended (LevelScript::process 0x91: it
                    // is invisible and kept): destroyed, the camera looked at the origin, Var Lupra station, past the array
                    // (a player's report).
                    if (helper != null) helper.SetActive(false);
                    Step = 6;   // event 5 is radio 2's trigger: only once the aftermath is over
                    break;
                case 6:
                    for (int i = 0; i <= 12; i++)
                    {
                        var s = S(i);
                        if (s == null) continue;
                        if (stepMs >= 2000f && stepMs <= 5000f) s.transform.Rotate(Vector3.up, 0.02f * dtMs, Space.World);
                        s.scriptedSpeed = 2f + stepMs * 0.001f;
                    }
                    if (stepMs < 10000f) break;
                    arrayBurn?.SetEmitting(false);
                    for (int i = 0; i <= 12; i++) { if (S(i) != null) S(i).scriptedFire = false; Remove(S(i)); }
                    if (helper != null) { Object.Destroy(helper); helper = null; }
                    LeaveCutscene();
                    c.Event = 5;   // radio 2, then the win (Objective(4, 2))
                    break;
            }
        }
    }
}
