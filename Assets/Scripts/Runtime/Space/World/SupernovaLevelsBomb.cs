// SupernovaLevelsBomb.cs
// Part of SupernovaLevels: levels 105-125: Khador's reverse-matter bomb (105), Luur after it (106), the Marktesh loan sharks
// (114), Valadon's Specters (120) and Kappa's secure containers (125)

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class SupernovaLevels
    {
        // 105: Naneroh, the reverse-matter bomb.
        void Build105()
        {
            var light = LightGame;
            Player.rotation = Quaternion.LookRotation(Dir(light), Vector3.up);
            var route = new Route(false);
            route.points.Add(new Vector3(650000f * light.x, 0, 650000f * light.z));
            c.SetPlayerRoute(route);
            var p = PlayerGame;
            var right = GameRight(Player);
            for (int k = 0; k < 2; k++)
            {
                var at = p + right * (k == 0 ? 2100f : -2100f) + PlayerDirGame * 2000f;
                var s = c.SpawnShip(0, 37, at, false, sp => { sp.alwaysFriend = true; sp.noLoot = true; sp.hitpoints = 9999999; });
                s.Place(ToUnity(at), Dir(light));
                s.SetOnlyEnemy(null);
                s.scriptedSpeed = FormationSpeed;
            }
            for (int i = 0; i < 3; i++) Specter(new Vector3(7000000, 7000000, 7000000), true, s => s.asleep = true).CloakingPossible = false;
            // The ctor: a cutscene camera behind the player; its autopilot (setAutoPilotToProgrammedStation) isn't needed.
            level.EndStartSequence();
            EnterCutscene(true);
            // The original flies the player on the autopilot here; the camera's dolly (2.43 u/ms, ev1) would overtake a
            // player at the launch speed and slide the escorts (2 u/ms) back at it tail first. Remake: the player and the
            // escorts keep just ahead of the camera until they break away.
            playerSpeed = FormationSpeed;
            cam.LookAt(p - PlayerDirGame * 2400f + GameUp(Player) * 500f, Player);
            Step = 1;
        }

        // 106: Luur's aftermath.
        void Build106()
        {
            var route = new Route(false);
            route.points.Add(new Vector3(-500000, 0, -1700000));
            route.points.Add(new Vector3(-500000, 0, -3700000));
            // "hitpoints 1" is Player::setHitpoints(1): the current hull only, the maximum stays (setHitpoints raises it, never
            // lowers it), so the crippled fighter trails smoke and fire (PlayerFighter::update's 33 % rule). Set as it's shown.
            var s = c.SpawnShip(Standing.Specter, 44, route.points[0], false, sp => { sp.alwaysFriend = true; sp.route = route; sp.inactive = true; sp.noLoot = true; });
            s.Place(ToUnity(route.points[0]), Dir(new Vector3(-1, 0, 0)));
            s.CloakingPossible = false;
            s.SetVisible(false);
            level.EndStartSequence();
            var p = new Vector3(300000, 0, 300000);
            MovePlayer(p, LightGame);
            EnterCutscene(false);
            cam.LookAt(p + new Vector3(2200, 1300, -4000), Player);
            c.Fade(true, Color.black, 8000f);
            Step = 1;
        }

        // 114: Marktesh, the loan sharks in the asteroid field.
        void Build114()
        {
            MovePlayer(new Vector3(0, 0, -120000), new Vector3(0, 0, 1));
            var rocks = AsteroidPositions();
            for (int i = 0; i < 6; i++)
            {
                var at = rocks.Count > 0 ? rocks[(i + rocks.Count / 2) % rocks.Count] + new Vector3(0, 2000, 0) : new Vector3(30000, 2000, 80000);
                var s = c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), at, false, sp => sp.asleep = true);
                s.detectRange = 7000f;
                s.SetHull(s.Hp.maxHull * 2);
            }
            c.WinObjective = () => c.DeadRange(0, 6);   // 0x12 (0, 6)
        }

        List<Vector3> AsteroidPositions()
        {
            var list = new List<Vector3>();
            if (level.Asteroids != null) foreach (Transform t in level.Asteroids) list.Add(ToGame(t.position));
            return list;
        }

        void Build120()
        {
            var right = GameRight(Player);
            for (int k = 0; k < 2; k++)
            {
                var at = PlayerGame + right * 50000f + PlayerDirGame * (k == 0 ? 4000f : -4000f);
                var s = Specter(at, false);
                var r = new Route(false); r.points.Add(new Vector3(0, 0, -80000)); s.SetRoute(r);
            }
            c.WinObjective = () => Over(0);   // Objective(4, 0); no success conversation: on at once
        }

        // 125: Kappa, the black box and the secure containers.
        void Build125()
        {
            for (int i = 0; i < 2; i++) c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), Vector3.zero, true, s => s.alwaysEnemy = true);
            for (int i = 2; i < 8; i++) c.SpawnShip(Standing.Pirate, NpcTables.RandomFighter(Standing.Pirate), new Vector3(7000000, 7000000, 7000000), true, s => { s.alwaysEnemy = true; s.asleep = true; });
            var boxes = new[] { new Vector3(-80000, 0, -160000), new Vector3(-72000, 0, -190000), new Vector3(-66000, 53000, -170000) };
            foreach (var b in boxes)
            {
                var s = Static("sn_secure_container_nivelian", b, Vector3.zero, -1, 0, 2);
                s.RadarHidden = true;
            }
            for (int k = 0; k < 4; k++)
            {
                var junk = Static("sn_junk_field", new Vector3(-72000 + 4000 * k, R(1500) - 200 * k, -145000 - 4000 * k), new Vector3(k + 11 - 5.7596f, 0, k + 11 - 4.3197f), -1, 0, -1);
                junk.RadarHidden = true;
            }
            c.WinObjective = () => Over(7);   // Objective(4, 7)
        }

        // 105 (process 7180-7865).
        void Tick105(float dtMs)
        {
            var light = LightGame;
            var dir = PlayerDirGame;
            var right = GameRight(Player);
            var step = (6.08f * dir + 0.2f * right) * 0.4f;
            switch (Step)
            {
                case 1:
                    cam.SetDolly(step);
                    if (stepMs >= 13000f) Step = 2;
                    break;
                case 2:
                    cam.SetDolly(step * 0.9f / 0.4f * 0.4f);
                    if (!Over(1)) break;
                    for (int k = 0; k < 2; k++)
                    {
                        var s = S(k);
                        if (s == null) continue;
                        var r = new Route(false);
                        r.points.Add(k == 0 ? 900000f * (right - dir) : -900000f * dir - 900000f * right);
                        s.scriptedSpeed = -1f;
                        s.SetRoute(r);
                    }
                    Step = 3;
                    break;
                case 3: cam.SetDolly(step); if (stepMs >= 3000f) Step = 4; break;
                case 4: cam.SetDolly(step * 0.25f); if (stepMs >= 3000f) { playerSpeed = 0f; LeaveCutscene(); Step = 5; } break;
                case 5: if (stepMs >= 10000f) { Remove(S(0)); Remove(S(1)); Step = 6; } break;
                case 6:
                {
                    if (stepMs < 24000f) break;
                    var s2 = new Vector3(PlayerGame.x + light.x * 100000f, 0, PlayerGame.z + light.z * 100000f) - right * 30000f;
                    var up = GameUp(Player);
                    Show(S(2), s2, -light);
                    Show(S(3), s2 + right * 2200f - up * 200f - dir * 2200f, -light);
                    Show(S(4), s2 - right * 2200f + up * 200f - dir * 2200f, -light);
                    for (int i = 2; i <= 4; i++) { S(i).Cloak(1000f); S(i).scriptedSpeed = 2.5f - 0.1f * (i - 2); }
                    EnterCutscene(true);
                    cam.LookAt(G(S(2)) + GameDir(S(2).transform) * 6800f + GameRight(S(2).transform) * 1200f + GameUp(S(2).transform) * 400f, S(2).transform);
                    Step = 7;
                    break;
                }
                case 7:
                    if (S(2) != null) cam.SetDolly(GameDir(S(2).transform) * 1.96f + GameRight(S(2).transform) * 0.03f);
                    if (stepMs < 10000f) break;
                    for (int i = 2; i <= 4; i++) { S(i).scriptedSpeed = -1f; S(i).CloakingPossible = true; }
                    playerSpeed = 0f;
                    LeaveCutscene();
                    timerMs = 0f;   // the original zeroes +0x90 here, and ev8 / ev9 share it
                    Step = 8;
                    break;
                // The +0x90 timer runs on from ev8 into ev9 (not zeroed): the first revive comes 50 s into ev9, 110 s after
                // ev8 began (#132: zeroed here it came a minute late).
                case 8: if (stepMs >= 60000f) Step = 9; break;
                case 9:
                    if (timerMs >= 110000f)
                    {
                        timerMs = 0f;
                        for (int i = 2; i <= 4; i++)
                        {
                            var s = S(i);
                            if (s == null || (s.Target.Alive && !s.Gone)) continue;
                            var at = PlayerGame + light * 100000f + new Vector3(10000 + 5000 * i + R(1000), R(1000), R(1000));
                            s.Revive(ToUnity(at));
                            Show(s, at, -light);
                        }
                    }
                    if (c.PlayerRoute != null && c.PlayerRoute.Waypoint == null && !level.Health.Dead)
                    {
                        c.PlayerInvulnerable = true;
                        Step = 10;
                    }
                    break;
                case 10:
                    if (!Over(7)) break;
                    EnterCutscene(false);
                    Player.rotation = Quaternion.LookRotation(Dir(light), Vector3.up);
                    cam.LookAt(PlayerGame + PlayerDirGame * 8000f + GameRight(Player) * 1000f + GameUp(Player) * 1000f, Player);
                    Step = 11;
                    break;
                case 11:
                    if (bomb == null && stepMs >= 2500f)
                    {
                        bomb = Scenery("rocket_explosive", PlayerGame, Player.rotation, "Reverse-matter bomb");
                        if (bomb != null) cam.SetTarget(bomb.transform);
                        Sfx.PlayAt(StoryAssets.Load()?.probeLaunch, Player.position);   // 14 Explosion_Bomb_AMR_Tormentor
                        bombMs = 0f;
                    }
                    if (bomb == null) break;
                    bombMs += dtMs;
                    bomb.transform.position += bomb.transform.forward * 13f * dtMs * M;
                    if (bombMs >= 9000f && bombMs - dtMs < 9000f && sn != null) Sfx.PlayAt(sn.explosion, Player.position);
                    if (bombMs >= 9000f && level.Backdrop != null) level.Backdrop.sunScaleFactor *= Mathf.Pow(0.95f, dtMs / OriginalFrame.Ms);
                    if (bombMs >= 10000f) { c.Fade(false, Color.white, 500f); Step = 12; }
                    break;
                case 12:
                    if (level.Backdrop != null && level.Backdrop.sunScaleFactor < 10f) level.Backdrop.sunScaleFactor *= Mathf.Pow(4f, dtMs / OriginalFrame.Ms);
                    cam.Rumble = 0.5f;
                    foreach (var s in c.Ships) if (s != null && s.Race == Standing.Specter && s.Target.Alive) s.InitPush(Player.position, 100000f * M);
                    if (!c.FadeDone) break;
                    c.Fade(true, Color.white, 2000f);
                    cam.SetTarget(Player);
                    Player.rotation = Quaternion.LookRotation(-Player.forward, Vector3.up);
                    if (bomb != null) { Object.Destroy(bomb); bomb = null; }
                    // StarSystem::switchSunForSupernovaExpansion: the sun back to a uniform 1.37329 (the size from 0x6a on),
                    // same texture.
                    if (level.Backdrop != null) level.Backdrop.sunScaleFactor = 1.373291015625f / level.Layout.sunScale;
                    playerSpeed = 12f;
                    Step = 13;
                    break;
                case 13:
                    Player.Rotate(Vector3.one, dtMs / 800f * Mathf.Rad2Deg, Space.Self);
                    cam.Rumble = 0.5f;
                    if (stepMs >= 6000f) AdvanceAndTravel(1, 111);
                    break;
            }
        }

        // 106 (process ~2317-2470).
        void Tick106()
        {
            var sp = S(0);
            if (Step >= 2 && Step <= 4 && sp != null) sp.scriptedSpeed = 1f;
            switch (Step)
            {
                case 1:
                    cam.SetDolly(new Vector3(-0.3f, -0.05f, 0.1f));
                    if (Over(2) && sp != null) { sp.Wake(); sp.SetVisible(true); sp.SetHull(1, false); sp.scriptedSpeed = 1f; Step = 2; }
                    break;
                case 2:
                    cam.SetDolly(new Vector3(-0.1f, -0.1f, 0.4f));
                    if (stepMs >= 3000f && sp != null) { cam.LookAt(G(sp) + GameDir(sp.transform) * 8000f + GameUp(sp.transform) * 500f + GameRight(sp.transform) * 1000f, sp.transform); Step = 3; }
                    break;
                case 3: if (Triggered(4)) Step = 4; break;
                case 4:
                    if (!Over(4)) break;
                    cam.SetDolly(Vector3.zero);
                    if (sp != null) { sp.scriptedSpeed = -1f; sp.Target.Damage(9999999, true, Vector3.zero); }
                    Step = 5;
                    break;
                case 5: if (stepMs >= 3000f) Step = 6; break;
                case 6: if (Over(5)) AdvanceAndTravel(2, 10); break;   // 106 -> 107 -> 108, then Thynome
            }
        }

        // 114: one pirate woken wakes them all.
        void Tick114()
        {
            if (Step != 0) return;
            bool any = false;
            for (int i = 0; i < 6; i++) if (S(i) != null && !S(i).Asleep) any = true;
            if (!any) return;
            for (int i = 0; i < 6; i++) if (S(i) != null) { S(i).detectRange = 50000f; S(i).Wake(); }
            Step = 1;
        }

        // 125 (campaign_levels_c.md 3.5).
        void Tick125()
        {
            var dock = level.Docking;
            switch (Step)
            {
                case 0:
                    if (!Triggered(1)) break;
                    var r = new Route(false); r.points.Add(new Vector3(-70000, 0, -130000));
                    c.SetPlayerRoute(r);
                    level.Navigation?.SetRoute(r);
                    Step = 1;
                    break;
                case 1:
                    if (c.PlayerRoute == null || c.PlayerRoute.Waypoint != null) break;
                    for (int k = 0; k < 3; k++)
                    {
                        var box = S(8 + k);
                        if (box == null) continue;
                        box.Target.displayName = $"{Localization.Get(3212)} {k + 1}";
                        box.DockingType = ObjectDocking.Hackable;
                        box.RadarHidden = false;
                    }
                    Step = 2;
                    break;
                case 2:
                {
                    if (!Over(2)) break;
                    var p = PlayerGame;
                    for (int k = 0; k < 4; k++)
                    {
                        float s = k % 2 == 0 ? 1f : -1f;
                        var at = p + new Vector3(s * (2000 * k + 2000), 2000 + R(1000), -57000 - s * R(2000));
                        Show(S(2 + k), at, p - at);
                        S(2 + k).scriptedSpeed = 0f;
                        S(2 + k).SetOnlyEnemy(level.Health.Target);
                    }
                    EnterCutscene(false);
                    SetPlayerVisible(false);
                    var s4 = S(4);
                    cam.LookAt(G(s4) + GameDir(s4.transform) * 4000f - GameUp(s4.transform) * 500f, s4.transform);
                    Step = 3;
                    break;
                }
                case 3:
                    for (int k = 2; k <= 5; k++) if (S(k) != null) S(k).scriptedSpeed = 2f;
                    if (stepMs < 7000f) break;
                    for (int k = 2; k <= 5; k++) if (S(k) != null) S(k).scriptedSpeed = -1f;
                    LeaveCutscene();
                    Step = 4;
                    break;
                case 4:
                case 5:
                case 6:
                    if (dock == null || !dock.HackingWon || dock.Target == null || dock.Target.DockingType != ObjectDocking.Hackable) break;
                    dock.Target.DockingType = 0;
                    dock.Target.Target.displayName = "";
                    dock.Target.RadarHidden = true;
                    dock.Undock();
                    if (Step == 5)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            var at = PlayerGame + new Vector3(25000 + 1000 * k, 2300 + 50 * k, 25000);
                            Show(S(6 + k), at, PlayerGame - at);
                        }
                    }
                    Step++;
                    break;
            }
        }
    }
}
