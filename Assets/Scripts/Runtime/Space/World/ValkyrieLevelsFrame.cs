// ValkyrieLevelsFrame.cs
// ValkyrieLevels: the cutscene helpers and per frame (LevelScript::process) (part of the partial ValkyrieLevels; its fields are in ValkyrieLevels.cs).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class ValkyrieLevels
    {
        void EnterCutscene(bool keepSpeed = true)
        {
            c.Cutscene = true;
            c.PlayerInvulnerable = true;
            playerSpeed = keepSpeed ? Ship.SpeedMetersPerSecond / (1000f * M) : 0f;
            Ship.externalControl = true;
            if (level.Weapons != null) level.Weapons.Blocked = true;
            level.Navigation?.SetAutopilot(null);
        }
        void LeaveCutscene()
        {
            c.Cutscene = false;
            c.PlayerInvulnerable = false;
            Ship.externalControl = false;
            if (level.Weapons != null) level.Weapons.Blocked = false;
            cam.Release();
        }
        void SetPlayerVisible(bool on)
        {
            if (Ship.visualModel != null) Ship.visualModel.gameObject.SetActive(on);
        }
        static void Remove(NpcShip s)
        {
            if (s == null) return;
            s.SetVisible(false);
            s.Deactivate();
            s.Place(new Vector3(-1e5f, -1e5f, -1e5f), Vector3.forward);
        }
        /// <summary>The hyper_drive fx (scale 20 / 30): anim state 3 then 1 = played once, facing the camera.</summary>
        void SpawnHyperDrive(Vector3 gamePos, float scale, bool texture78 = false)
        {
            fxFixed = false;   // Modified: billboarded unless a level pins it (78)
            if (fx != null) Object.Destroy(fx);
            fx = null;
            if (assets == null || assets.hyperDrive == null) return;
            fx = Object.Instantiate(assets.hyperDrive, ToUnity(gamePos), (cam.Camera != null ? cam.Camera.rotation : Quaternion.identity) * Quaternion.Euler(0f, 180f, 0f));
            if (texture78) ReplaceFxTexture78(fx);   // Modified: before the fades / animation pick up the materials
            fx.transform.localScale *= scale;
            GunRig.StripForFx(fx);
            GunRig.EnableFades(fx);   // the parts' `extra` fade-out
            float len = PartAnimation.PlayOnce(fx);
            fxLength = len > 0f ? len : 3000f;
            fxMs = 0f;
            Sfx.PlayAt(assets.timeJump, cam.Camera != null ? cam.Camera.position : Player.position);   // 160
        }
        void FaceCamera(GameObject go)
        {
            if (go != null && cam.Camera != null)
                go.transform.rotation = Quaternion.LookRotation(cam.Camera.position - go.transform.position, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }
        static void PlayAtCamera(AudioClip clip)
        {
            var cam = Camera.main;
            if (clip != null && cam != null) Sfx.PlayAt(clip, cam.transform.position);
        }
        public void Tick(int index, float dtMs)
        {
            stepMs += dtMs;
            // Only while the level's own cutscene holds the ship: the mining approach and object docking set externalControl
            // too, and this push (at the speed from the last cutscene, a boost's if the player was boosting) flew the ship
            // through the asteroid it was landing on (step 29's Void crystals, a player's report).
            if (c.Cutscene && Ship.externalControl && !level.Health.Dead)
            {
                Player.position += Player.forward * playerSpeed * dtMs * M;
                Ship.ExternalSpeedMetersPerSecond = playerSpeed * 1000f * M;
            }
            if (loading) return;
            // The level's win advanced the index while its cutscene still ran (78 -> 79 by the Khador Drive keeps it).
            if (index != built && c.Cutscene && built != 78) LeaveCutscene();
            switch (built)
            {
                case 48: if (index == 48) Tick48(); break;
                case 50: if (index == 50 || index == 51) Tick50(); break;
                case 56: if (index == 56) Tick56(); break;
                case 64: if (index == 64) Tick64(); break;
                // 67 / 69: the last line wins the level, so the script's final step runs after the advance.
                case 67: if (index == 67 || index == 68) Tick67(); break;
                case 69: if (index == 69 || index == 70) Tick69(); break;
                case 70: if (index == 70) Tick70(); break;
                case 73: if (index == 73) Tick73(); break;
                case 78: Tick78(dtMs); break;
                case 80: if (index == 80) Tick80(dtMs); break;
                case 81: if (index == 81) Tick81(dtMs); break;
            }
        }
        public void LateTick(float dtMs)
        {
            cam.LateTick(dtMs);
            if (built == 80) UpdateFlamePieces80();   // Modified
            if (!fxFixed) FaceCamera(fx);   // Modified: 78's jump is aligned with the station, not the camera
            // The fx plays once (anim state 3, then 1) and is gone at its end.
            if (fx != null && (fxMs += dtMs) >= fxLength) { Object.Destroy(fx); fx = null; }
        }
        // 48: Taret flies the player in: the autopilot to the station once the arrival camera is over, the stick ignored.
        void Tick48()
        {
            Ship.steeringLocked = true;
            if (autopiloted || !level.StartSequenceOver || level.Navigation == null) return;
            var station = level.Navigation.Targets.Find(t => t.kind == Navigation.Kind.Station);
            if (station == null) return;
            level.Navigation.SetAutopilot(station);
            autopiloted = true;
        }
        // 50 (and 51 on its level): the shared 0x32 / 0x33 block: once line 2 is over the H'Soc turn hostile.
        void Tick50()
        {
            if (Step != 0 || c.Radio == null || c.Radio.Count < 3 || !Over(2)) return;
            foreach (var s in c.Ships)
            {
                if (s == null || s.IsWingman) continue;
                s.alwaysFriend = false;
                s.alwaysEnemy = true;
                s.turnedEnemy = true;
            }
            Session.Standing[0] = 100;   // Standing::setStanding(0, 100): the Vossk hostile
            Step = 1;
        }
        // 56: the escort holds fire until the player's armor is gone.
        void Tick56()
        {
            if (Step == 0)
            {
                foreach (var s in c.Ships) if (s != null && s.alwaysFriend) s.shootingEnabled = false;
                Step = 1;
            }
            else if (Step == 1 && c.PlayerArmorGone)
            {
                foreach (var s in c.Ships) if (s != null && s.alwaysFriend) s.shootingEnabled = true;
                Step = 2;
            }
        }
        // 64: Khador's rescue.
        void Tick64()
        {
            var khador = S(0);
            switch (Step)
            {
                case 0:
                    if (Triggered(0) && khador != null)
                    {
                        EnterCutscene();
                        var kdir = new Vector3(khador.transform.forward.x, khador.transform.forward.y, -khador.transform.forward.z);
                        cam.LookAtUnity(khador.transform.position + ToUnity(kdir * 10000f + new Vector3(0, 300, 3000)), khador.transform);   // beside him (an offset)
                        cam.SetDolly(new Vector3(0f, 0.2f, 0f));
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Triggered(2))
                    {
                        LeaveCutscene();
                        foreach (var s in c.Ships) s?.SetRoute(null);
                        Step = 2;
                    }
                    break;
                case 2:
                    if (khador != null) khador.frozen = true;
                    if (Triggered(5))
                    {
                        if (khador != null) khador.frozen = false;
                        var flee = new Route(false);
                        flee.points.Add(new Vector3(500000, 500000, 500000));
                        for (int i = 1; i <= 8; i++)
                        {
                            var s = S(i);
                            if (s == null || s.IsWingman || !s.Target.Alive) continue;
                            s.SetSpeed(16.5f);
                            s.SetOnlyEnemy(null);
                            s.SetRoute(flee);
                        }
                        Step = 3;
                    }
                    break;
                case 3:
                    if (Over(6))
                    {
                        for (int i = 1; i <= 8; i++) if (S(i) != null && S(i).Target.Alive) S(i).Vanish();   // setDead
                        Step = 4;
                    }
                    break;
            }
        }
        // 67: Corny's break-in.
        void Tick67()
        {
            var corny = S(10);
            var outpost = S(0);
            if (corny == null) return;
            // (Where the outpost stands in the scene, in game units: the spots below are offsets from it, ToUnity's.)
            var w = outpost != null ? ToGame(outpost.transform.position) : OrbitScale.Place(new Vector3(220000, -20000, -10000));
            float grown = outpost != null ? outpost.StructureScale : StructureScale;   // OrbitScale: the outpost as NpcShip grew it
            switch (Step)
            {
                case 0:
                    if (Triggered(1)) { corny.SetSpeed(0f); corny.SetOnlyEnemy(null); corny.SetExhaust(false); Step = 1; }
                    break;
                case 1:
                    if (Over(3))
                    {
                        corny.SetSpeed(3f);
                        corny.SetExhaust(true);
                        EnterCutscene();
                        cam.LookAtUnity(corny.transform.position + ToUnity(new Vector3(6000, -200, 1000)), corny.transform);   // beside him
                        Step = 2;
                    }
                    break;
                case 2:
                    if (Over(4))
                    {
                        LeaveCutscene();
                        corny.Place(ToUnity(new Vector3(500000, 500000, 500000)), corny.transform.forward);
                        corny.Deactivate();
                        corny.SetVisible(false);
                        Step = 3;
                    }
                    break;
                case 3:
                    if (Triggered(5))
                    {
                        for (int i = 5; i <= 8; i++)
                        {
                            // OrbitScale: around the outpost (a structure), the offset grown with it.
                            var at = w + new Vector3(Sign() * (15000 + R(10000)), R(10000) - 5000, Sign() * (15000 + R(10000))) * grown;
                            S(i)?.Place(ToUnity(at), S(i).transform.forward);
                        }
                        Step = 4;
                    }
                    break;
                case 4:
                    if (Triggered(7))
                    {
                        var at = w + new Vector3(0, 0, 12000 * grown);   // OrbitScale: out of the grown outpost
                        corny.Place(ToUnity(at), Dir(new Vector3(0, 0, 1)));
                        corny.Wake();
                        corny.SetVisible(true);
                        corny.SetSpeed(2f);
                        var away = new Route(false);
                        away.points.Add(new Vector3(0, 0, 800000));
                        corny.SetRoute(away);
                        c.FailObjective = null;   // Level+0x2c = 0: he's out
                        EnterCutscene();
                        cam.LookAtUnity(ToUnity(at + new Vector3(6000, -200, 10000)), corny.transform);   // beside him (scene coordinates)
                        Step = 5;
                    }
                    break;
                case 5:
                    if (Triggered(9)) { corny.SetSpeed(8f); LeaveCutscene(); Step = 6; }
                    break;
                case 6:
                    if (Over(11)) { corny.Deactivate(); corny.SetVisible(false); Step = 7; }
                    break;
            }
        }
        // 69: Trot Lykkt leaves.
        void Tick69()
        {
            var trot = S(0);
            if (trot == null) return;
            if (Step == 0 && Triggered(0))
            {
                EnterCutscene();
                var dir = new Vector3(trot.transform.forward.x, trot.transform.forward.y, -trot.transform.forward.z);
                cam.LookAtUnity(trot.transform.position + ToUnity(dir * 10000f + new Vector3(600, 300, 1000)), trot.transform);
                Step = 1;
            }
            else if (Step == 1 && Over(1))
            {
                LeaveCutscene();
                Remove(trot);
                trot.Place(ToUnity(new Vector3(0, 0, -5000000)), trot.transform.forward);
                Step = 2;
            }
        }
        // 70: stop Trot Lykkt.
        void Tick70()
        {
            var trot = S(0);
            if (trot == null) return;
            var dir = new Vector3(trot.transform.forward.x, trot.transform.forward.y, -trot.transform.forward.z);
            switch (Step)
            {
                case 0:
                    if (Triggered(0))
                    {
                        EnterCutscene();
                        cam.LookAtUnity(trot.transform.position + ToUnity(dir * 3000f + new Vector3(-600, -300, -1000)), trot.transform);
                        Step = 1;
                    }
                    break;
                case 1:
                    if (Over(1))
                    {
                        trot.alwaysFriend = false;
                        trot.alwaysEnemy = true;
                        trot.detectRange = 100000f;
                        trot.SetRoute(null);
                        cam.LookAtUnity(trot.transform.position + ToUnity(dir * 2000f + new Vector3(-600, 800, -1000)), trot.transform);
                        Step = 2;
                    }
                    break;
                case 2:
                    if (Over(2))
                    {
                        LeaveCutscene();
                        trot.SetHull(trot.Hp.maxHull * 3);
                        trot.SetSpeed(10f);
                        Step = 3;
                    }
                    break;
            }
        }
        // 73: the convoy.
        void Tick73()
        {
            switch (Step)
            {
                case 0:
                    if (S(8) != null && (S(8).transform.position - Player.position).magnitude < 50000f * M) Step = 1;
                    break;
                case 1:
                    for (int i = 8; i < 12; i++)
                        if (S(i) != null && S(i).Target.Alive && S(i).Hp.emp < S(i).Hp.maxEmp) { Step = 2; break; }
                    break;
                case 2:
                    for (int i = 8; i < 12; i++)
                    {
                        if (S(i) == null || !S(i).Hp.empDisabled) continue;
                        // The first freighter (from 8) stops for good.
                        for (int k = 8; k < 12; k++) if (S(k) != null && S(k).Target.Alive) { S(k).frozen = true; S(k).SetMoving(false); break; }
                        Step = 3;
                        break;
                    }
                    break;
                case 3:
                    if (Triggered(7))
                    {
                        var p = ToGame(Player.position);   // around the player: scene coordinates, offsets (ToUnity)
                        for (int i = 4; i <= 7; i++)
                        {
                            var s = S(i);
                            if (s == null) continue;
                            var at = p + new Vector3(Sign() * (35000 + R(10000)), R(10000) - 5000, Sign() * (35000 + R(10000)));
                            s.Place(ToUnity(at), Dir(p - at));
                            s.Wake();
                        }
                        Step = 4;
                    }
                    break;
            }
        }
        // 78: the escape from Valkyrie (LevelScript.c 2001-2302, t = the level clock).
        void Tick78(float dtMs)
        {
            var station = level.Station;
            if (T >= 7901f && station != null && !animStarted)
            {
                animStarted = true;
                float unfoldMs = PartAnimation.PlayOnce(station);
                // Modified: the ship stops and faces the station at the end of the unfolding's first part (StopAtUnfold78 of
                // the whole animation; see below).
                stopAtT78 = T + (unfoldMs > 0f ? unfoldMs * StopAtUnfold78 : 0f);
            }
            if (animStarted && !stopped78 && T >= stopAtT78)
            {
                // Modified: at the end of the unfolding's first part the ship stops at once and faces the station (computer
                // controlled again, speed 0).
                stopped78 = true;
                Ship.externalControl = true;
                playerSpeed = 0f;
                if (station != null)
                {
                    var to = station.transform.position - Player.position;
                    if (to.sqrMagnitude > 1e-6f) Player.rotation = Quaternion.LookRotation(to, Vector3.up);
                }
            }
            switch (Step)
            {
                case 1:
                    if (station != null) cam.SetTarget(station.transform);
                    Step = 2;
                    break;
                case 2:
                    cam.SetDolly(new Vector3(-0.5f, 0.3f, 1f));
                    if (T >= 7001f) { cam.LookAtUnity(At(new Vector3(20000, 5000, 30000)), station != null ? station.transform : Player); Step = 3; }   // OrbitScale: absolute
                    break;
                case 3:
                    if (T >= 7901f && !musicPlayed) { musicPlayed = true; PlayAtCamera(assets?.valkyrieBattlemode); }   // 0x462
                    if (T >= 9001f) Step = 4;
                    break;
                case 4:
                    if (T >= 18001f)
                    {
                        cam.LookAtUnity(At(new Vector3(8000, 31000, -9000)), station != null ? station.transform : Player);   // OrbitScale: absolute
                        // Modified: the player's ship stays visible through the rest of the cutscene (was SetPlayerVisible(false)).
                        Step = 5;
                    }
                    break;
                case 5:
                    cam.SetDolly(new Vector3(-0.8f, 0f, 0f));
                    turretMoved = 0f;
                    Step = 6;
                    break;
                case 6:
                    if (SlideTurret(S(0), TurretSide0_78, TurretStartX0_78, TurretEndX0_78, rollSign0_78, dtMs)) { turretMoved = 0f; Step = 7; }   // Modified: [0] first (was [1])
                    break;
                case 7:
                    if (T >= 27001f) Step = 8;
                    break;
                case 8:
                    if (SlideTurret(S(1), TurretSide1_78, TurretStartX1_78, TurretEndX1_78, rollSign1_78, dtMs) && stepMs >= 2001f)   // Modified: [1] second (was [0])
                    {
                        cam.LookAtUnity(At(new Vector3(-12000, 5000, 15000)), station != null ? station.transform : Player);   // OrbitScale: absolute
                        Step = 9;
                    }
                    break;
                case 9:
                    cam.SetDolly(new Vector3(-0.7f, 1f, 3f));
                    cam.Rumble = Mathf.Clamp01(stepMs / 10000f);
                    if (stepMs >= 6000f)
                    {
                        SpawnHyperDrive(new Vector3(0, 0, 14000), 20f, texture78: true);
                        // Modified: the jump aligned with the station instead of facing the camera (values from the
                        // Inspector, Unity units relative to the station: position, rotation, scale).
                        if (fx != null)
                        {
                            var st = station != null ? station.transform.position : Vector3.zero;
                            // OrbitScale: sized and placed on the grown station
                            fx.transform.SetPositionAndRotation(st + HyperDrivePos78 * StructureScale, Quaternion.Euler(HyperDriveRot78));
                            fx.transform.localScale = Vector3.one * (HyperDriveScale78 * StructureScale);
                            fxFixed = true;
                        }
                        Step = 10;
                    }
                    break;
                case 10:
                    cam.SetDolly(new Vector3(-0.7f, 1f, 4f));
                    cam.Rumble = Mathf.Clamp01(1f - stepMs / 4000f);
                    if (stepMs >= 2151f && station != null && station.activeSelf)
                    {
                        // The battlestation jumps away with its turrets (the camera keeps looking where it was).
                        Remove(S(0));
                        Remove(S(1));
                        station.SetActive(false);
                    }
                    if (stepMs >= 4001f) Step = 11;
                    break;
                case 11:
                    cam.SetDolly(new Vector3(-0.7f, 1f, 3f));
                    cam.Rumble = 0f;
                    if (stepMs >= 3000f)
                    {
                        for (int i = 2; i < c.Ships.Count; i++)
                        {
                            var s = S(i);
                            if (s == null) continue;
                            s.transform.position += ToUnity(new Vector3(0, 0, -50000));
                            s.Wake();
                        }
                        var lead = S(c.Ships.Count - 10);
                        if (lead != null) cam.LookAtUnity(lead.transform.position + ToUnity(new Vector3(2000, 500, -20000)), lead.transform);
                        Step = 12;
                    }
                    break;
                case 12:
                    cam.SetDolly(new Vector3(-0.3f, 0.3f, 0.5f));
                    if (stepMs >= 5000f)
                    {
                        SetPlayerVisible(true);
                        // The original never turns the ship (state 0xc only hands it back): it still faces game +Z, set in the
                        // constructor, where the 20 pirates come from. The remake's stop above turned it to the station behind
                        // it, so control came back with its back to them (a player's report): the original's heading again,
                        // before the chase camera snaps behind it.
                        Player.rotation = Quaternion.LookRotation(Dir(new Vector3(0, 0, 1)), Vector3.up);
                        Ship.inputLocked = false;   // Modified: the controls back (locked in Build78)
                        LeaveCutscene();
                        Step = 13;
                    }
                    break;
            }
        }
        void ReplaceFxTexture78(GameObject go)
        {
            if (go == null || string.IsNullOrEmpty(HyperDriveTexture78)) return;
            var names = new System.Collections.Generic.HashSet<string>();
            var tex = Resources.Load<Texture2D>(HyperDriveTexture78);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.materials)   // per-instance copies: the prefab's materials stay untouched
                {
                    foreach (var prop in new[] { "_BaseMap", "_MainTex" })
                    {
                        if (!m.HasProperty(prop)) continue;
                        var cur = m.GetTexture(prop);
                        if (cur != null) names.Add(cur.name);
                        if (tex == null) continue;
                        if (!string.IsNullOrEmpty(HyperDriveTextureOriginal78) && (cur == null || cur.name != HyperDriveTextureOriginal78)) continue;
                        m.SetTexture(prop, tex);
                    }
                }
            if (tex != null) Debug.Log($"[78] hyperdrive textures: {string.Join(", ", names)} -> {tex.name}");
        }
        /// <summary>78 steps 6 / 8: a top turret slides out at 0.3 u/ms until it has moved 1100 units, swinging round as it
        /// goes. True when done.</summary>
        // Modified: driven from the measured start / end poses (side = +1 / -1): X from startX to endX, rotation X
        // from TurretStartRotX78 to 0, Y kept at 0, Z from TurretStartRollDeg78 to TurretRollDeg78 with the turret's sign
        // (the remake swung it about Y).
        bool SlideTurret(NpcShip turret, float side, float startX, float endX, float rollSign, float dtMs)
        {
            if (turret == null || turretMoved >= 1100f) return true;
            turretMoved = Mathf.Min(turretMoved + 0.3f * dtMs, 1100f);
            float k = turretMoved / 1100f;
            float hostX = level.Station != null ? level.Station.transform.position.x : 0f;
            var p = turret.transform.position;
            turret.transform.position = new Vector3(hostX + side * Mathf.Lerp(startX, endX, k) * StructureScale, p.y, p.z);   // OrbitScale: on the grown station
            turret.transform.rotation = Quaternion.Euler(Mathf.Lerp(TurretStartRotX78, 0f, k), 0f, rollSign * Mathf.Lerp(TurretStartRollDeg78, TurretRollDeg78, k));
            // Modified: the gun stays drawn in for the first half of the turn (180 -> 90 deg) and slides out in the second.
            SetTurretGun(turret, Vector3.Lerp(TurretGunIn78, TurretGunOut, Mathf.InverseLerp(0.5f, 1f, k)));
            return turretMoved >= 1100f;
        }
        // 81: Alice stranded in the Void (LevelScript.c 1899-1998).
        /// <summary>81's hyperdrive fx, the battlestation dropping out of hyperspace: like 78 / 80, aligned with the
        /// battlestation instead of facing the camera, with 78's custom texture when there is one. Remake co-op: a follower's
        /// too.</summary>
        void ArriveFx81()
        {
            SpawnHyperDrive(new Vector3(0, 0, 10000), 30f, texture78: true);
            if (fx == null) return;
            var bs = battlestation != null ? battlestation.transform.position : Vector3.zero;
            fx.transform.SetPositionAndRotation(bs + HyperDrivePos81 * StructureScale, Quaternion.Euler(HyperDriveRot81));   // OrbitScale
            fx.transform.localScale = Vector3.one * (HyperDriveScale81 * StructureScale);
            fxFixed = true;
        }
        void Tick81(float dtMs)
        {
            switch (Step)
            {
                case 0:
                    cam.Rumble = Mathf.Clamp01(T / 10000f);
                    if (T >= 10001f)
                    {
                        ArriveFx81();
                        Step = 1;
                    }
                    break;
                case 1:
                    cam.Rumble = Mathf.Clamp01(1f - stepMs / 4000f);
                    // Modified: the battlestation appears 0.2 s later (12 201 ms, was 12 001), after the hyperdrive's flash.
                    if (T >= BattlestationAppears81 && battlestation != null && !battlestation.activeSelf) battlestation.SetActive(true);
                    if (T >= 14001f) { cam.Rumble = 0f; Step = 2; }
                    break;
                case 2:
                    if (Over(3)) { c.Fade(false, Color.black, 5000f); Step = 3; }
                    break;
                case 3:
                    if (c.FadeDone)
                    {
                        // nextCampaignMission (-> 82), departStation(100), the station module.
                        loading = true;
                        Story.Advance(level.Database);
                        Session.StationIndex = 100;
                        Session.AttackedStations.Remove(100);   // Station::setAttackedFriends(false) on the new station
                        Session.ComingFromVoid = false;
                        level.Dock();
                    }
                    break;
            }
        }
    }
}
