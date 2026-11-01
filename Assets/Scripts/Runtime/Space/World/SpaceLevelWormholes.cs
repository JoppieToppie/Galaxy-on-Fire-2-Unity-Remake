// SpaceLevelWormholes.cs
// SpaceLevel: the Void's wormhole and an event's / mission's wormhole (part of the partial SpaceLevel; its fields are in SpaceLevel.cs).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace GoF2Remake.World
{
    public partial class SpaceLevel
    {
        /// <summary>Level::createSpace: landmark 3 while the game isn't won, at a random spot (time-seeded).</summary>
        void SpawnWormhole()
        {
            if (Session.FreePlay || Story.GameWon) return;
            bool attacked = Layout.stationIndex == Session.VoidInvasionStation;
            bool visible = (ComingFromVoid || attacked || Layout.alienOrbit) && Story.Index < 43;
            var pos = new Vector3(Random.Range(0, 80000) - 40000, Random.Range(0, 40000) - 20000, Random.Range(0, 40000) + 40000);
            Wormhole = Wormhole.Spawn(StoryAssets.Load(), pos, visible);
            Wormhole.alienOrbit = Layout.alienOrbit;
            Wormhole.attackedStation = attacked;
        }
        /// <summary>LevelScript::LevelScript 0x16056c: coming out of the Void (or in the alien orbit) the wormhole sits
        /// 10000 units behind the player and closes after a second (reset(true)).</summary>
        void PlaceArrivalWormhole()
        {
            if (Session.ArrivedByWormhole)
            {
                // Remake events: out of an event's wormhole, one closing 10000 units behind the player (like the Void's).
                Session.ArrivedByWormhole = false;
                var behind = Player.transform.position - Player.transform.forward * 10000f * M;
                var hole = Wormhole.Spawn(StoryAssets.Load(), new Vector3(behind.x, behind.y, -behind.z) / M, true);
                hole.FreeMissionLock();
                hole.ResetTimer(true);
                hole.player = Player.transform;
            }
            if (Wormhole == null) return;
            Wormhole.player = Player.transform;
            if (!(ComingFromVoid || Layout.alienOrbit) || Story.Index >= 43) return;
            var p = Player.transform.position - Player.transform.forward * 10000f * M;
            Wormhole.ResetTimer(true);
            Wormhole.transform.position = p;
            Wormhole.SetVisible(true);
        }
        /// <summary>Entering the wormhole now kills the player (MGame::OnUpdate: 29 / 41, and 40 until Errkt's freighter has
        /// gone through).</summary>
        bool WormholeKills
        {
            get
            {
                int index = Story.Index;
                return !Session.FreePlay && Campaign != null && Story.IsLevelMission(Layout.stationIndex)
                       && (index == 29 || index == 41 || (index == 40 && Campaign.Event <= 3));
            }
        }
        /// <summary>Remake: during a level's cutscene, while entering would kill, the wormhole neither pulls nor takes the
        /// player, who has no control then (step 40: Errkt's call comes 40 s in wherever the player is, and the cutscene
        /// following his freighter drew a player already near the wormhole into it, dead before the scene ended). And at
        /// step 40 until his freighter has gone through it doesn't pull at all: its pull near the centre is faster than a
        /// ship, so a player near it when he called was dragged in the moment the scene gave the controls back; flying into
        /// it still kills (the original's rule).</summary>
        bool WormholeHeld => Campaign != null && Campaign.Cutscene && WormholeKills;
        /// <summary>The Khador Drive's refusal at 42 in the Void (Story.EscapesByWormhole): the way out instead of 525.</summary>
        static string WormholeEscapeHint => Localization.Extra("khadorWormholeEscape",
            "The Khador Drive won't work here. Destroy the freighter, then escape through the wormhole.");
        bool WormholeNoPull => WormholeKills && Story.Index == 40;
        /// <summary>MGame::OnUpdate, PlayerEgo::isInWormhole (see the header).</summary>
        void UpdateWormholeRide()
        {
            if (!riding && Collision != null && Collision.InEventWormhole && Health != null && !Health.Dead && EventWormhole != null)
            {
                RideEventWormhole();
                return;
            }
            if (riding || Collision == null || !Collision.InWormhole || Health == null || Health.Dead) return;
            int index = Story.Index;
            bool active = !Session.FreePlay && Campaign != null && Story.IsLevelMission(Layout.stationIndex);
            if (index == 42)
            {
                if (Layout.alienOrbit) return;   // the mother ship's explosion: the level script rides out itself
                RideWormhole();
                return;
            }
            // Step 24: Carla's "What is this thing? KEITH!" (radio line 4, text 1921) starts as the wormhole opens and shows
            // 2 s later; the ride waits for it (8 s at most), or the scene load swallows the line.
            if (active && index == 24 && Campaign.Radio != null && Campaign.Radio.Triggered(4) && !Campaign.Radio.Over(4)
                && (rideHoldMs += Time.deltaTime * 1000f) < 8000f) return;
            if (active)
            {
                if (WormholeKills) { if (!WormholeHeld) { Health.Kill(); riding = Health.Dead; } return; }   // the original sets HP 0 every frame: an emergency system or god mode tries again
                if (index == 40) Session.LastFreighterHull = Campaign.FreighterHull;
                if (index < 41) Story.Advance(db);
            }
            RideWormhole();
        }
        /// <summary>The ride: into the alien orbit (remembering this station as Status+0x84), or out of it back there; the
        /// hull etc. are kept (Status), a stream-out arrival with the wormhole behind the player.</summary>
        public void RideWormhole()
        {
            if (rode) return;
            CoopLeaderMoved(Layout.alienOrbit ? Session.VoidReturnStation : Session.VoidOrbit, false, true);   // remake co-op: the crew rides too
            rode = riding = true;
            Leaving = true;
            Weapons?.StoreAmmo();
            int from = Layout.stationIndex;
            if (Layout.alienOrbit) Session.StationIndex = Session.VoidReturnStation;
            else { Session.VoidReturnStation = from; Session.StationIndex = Session.VoidOrbit; }
            Session.PreviousStationIndex = from;
            Session.ArrivedByTravel = true;
            Session.LaunchedFromStation = false;
            Session.ComingFromVoid = true;
            Session.ProgrammedStation = -1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        /// <summary>Opens (or moves) the event's wormhole at 'game' (game units in this orbit); flying in takes the player to
        /// 'destination' (a station, Session.VoidOrbit the Void's home).</summary>
        public void OpenEventWormhole(Vector3 game, int destination)
        {
            if (Player == null) return;
            eventWormholeTo = destination;
            if (EventWormhole == null)
            {
                EventWormhole = Wormhole.Spawn(StoryAssets.Load(), game, true);
                EventWormhole.held = true;
                EventWormhole.FreeMissionLock();
                EventWormhole.player = Player.transform;
                if (Collision != null) Collision.eventWormhole = EventWormhole;
                Navigation?.SetEventWormhole(EventWormhole);
            }
            else
            {
                EventWormhole.SetPosition(game);
                EventWormhole.SetVisible(true);
            }
            EventWormhole.Open();
        }
        /// <summary>The event's wormhole shrinks away.</summary>
        public void CloseEventWormhole() => EventWormhole?.Close();
        /// <summary>The ride through an event's wormhole: a stream-out arrival at its destination with a wormhole closing
        /// behind (hull etc. kept, like the Void's).</summary>
        void RideEventWormhole()
        {
            if (rode || Leaving) return;
            rode = riding = true;
            Leaving = true;
            Weapons?.StoreAmmo();
            int from = Layout.stationIndex;
            if (eventWormholeTo == Session.VoidOrbit && !Layout.alienOrbit) Session.VoidReturnStation = from;
            Session.PreviousStationIndex = from;
            Session.StationIndex = eventWormholeTo;
            Session.ArrivedByTravel = true;
            Session.LaunchedFromStation = false;
            Session.ProgrammedStation = -1;
            Session.ArrivedByWormhole = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        /// <summary>A level script's jump to another orbit (MGame::OnTouchEnd after index 64's / 80's success conversation:
        /// switch_to_target_setting, departStation, initStreamOutPosition): hull, shield and armor kept, a stream-out arrival;
        /// 'streamOut' false = initStreamOutPosition 0, the undock point (MGame::OnTouchEnd / LevelScript::process at 0x90).</summary>
        public void TravelTo(int station, bool streamOut = true)
        {
            if (Leaving) return;
            CoopLeaderMoved(station, false, streamOut);   // remake co-op: the crew in this story orbit goes along
            Leaving = true;
            Weapons?.StoreAmmo();
            if (station == Session.VoidOrbit) Session.VoidReturnStation = Layout.stationIndex;
            Session.PreviousStationIndex = Layout.stationIndex;
            Session.StationIndex = station;
            Session.ArrivedByTravel = streamOut;
            Session.LaunchedFromStation = false;
            Session.ProgrammedStation = -1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        /// <summary>Remake multiplayer (NetTeleport): an admin's teleport out of this orbit, into another orbit (the pose
        /// from NetTeleport.TakePose, no launch camera) or into a station's hangar ('dock'). False while the ship can't go
        /// (already leaving, destroyed).</summary>
        public bool TeleportOut(int station, bool dock)
        {
            if (Leaving || (Health != null && Health.Dead)) return false;
            if (dock)
            {
                Session.StationIndex = station;
                Dock(false);
                return true;
            }
            Leaving = true;
            Weapons?.StoreAmmo();
            if (station == Session.VoidOrbit && !Layout.alienOrbit) Session.VoidReturnStation = Layout.stationIndex;
            Session.PreviousStationIndex = Layout.stationIndex;
            Session.StationIndex = station;
            Session.ArrivedByTravel = false;
            Session.LaunchedFromStation = false;
            Session.ProgrammedStation = -1;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            return true;
        }
        /// <summary>Remake multiplayer (NetTeleport): a teleport within this orbit moves the ship in place, the autopilot off.
        /// False while something else flies it (mining, object docking, a planet jump, a script's camera, dead): the orbit
        /// is loaded again instead.</summary>
        public bool MoveForTeleport(Vector3 position, Quaternion rotation)
        {
            if (Leaving || Player == null || (Health != null && Health.Dead) || (Mining != null && Mining.State != Flight.Mining.Phase.Idle)
                || (Docking != null && Docking.Busy) || (Navigation != null && Navigation.Jumping) || chase == null || chase.scriptCamera)
                return false;
            Navigation?.SetAutopilot(null);
            MovePlayer(position, rotation);
            return true;
        }
        /// <summary>The station's volumes (collision.json) and the visible jumpgate's sphere (see Obstacle).</summary>
        void AddObstacles()
        {
            OrbitBuilder.AddObstacles(Layout, Station, Jumpgate);
            // Remake: the player slides along their real shapes (HullCollision); shots pass them as in the original.
            if (Station != null) HullCollision.Attach(Station, null, Station.GetComponent<Obstacle>());
            if (Jumpgate != null) HullCollision.Attach(Jumpgate, null, Jumpgate.GetComponent<Obstacle>());
            // Remake mods: a station of its own model sizes the dock range and the launch point.
            var own = Modding.ModWorld.ModelOf(Layout.stationIndex);
            var obstacle = Station != null ? Station.GetComponent<Obstacle>() : null;
            if (own != null && Modding.ModStations.Built(own.station) && obstacle != null)
            {
                float radius = obstacle.cubeHalf / M - 5000f;   // AddObstacles: the bounding radius + 5000 units
                StationDockRange = Mathf.Max(DockRange, radius + 6000f);
                UndockPoint = new Vector3(OrbitLayout.UndockPosition.x, OrbitLayout.UndockPosition.y, Mathf.Max(OrbitLayout.UndockPosition.z, radius + 3000f));
            }
        }
        void SetupCamera()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            mainCamera.nearClipPlane = 20f * M;
            // StarSystem::render: 300000, 450000 in the alien orbit before mission 0x50 (the Void's fighters sit up to
            // 100000 out and the wormhole reopens 60000-100000 out while the player arrives 170000-220000 out).
            farClip = (Layout != null && Layout.alienOrbit && Story.Index < 0x50 ? 450000f : 300000f) * M;
            mainCamera.farClipPlane = farClip;
            mainCamera.clearFlags = CameraClearFlags.Skybox;
        }
        // Level::createPlayer + the undock branch of Level::init.
        /// <summary>Remake debug (the Debug page's Ships tab, PlayerHull): the player's hull replaced in flight where it is:
        /// the new model, its flight stats, the chase camera's distance and the model-bound parts (exhaust particles, cloak,
        /// mounted turret) rebuilt; the old hull's turrets go, the new one's come (the capital ships, ships 45 / 51).
        /// Session.ShipIndex and PlayerHull's pick are set first (the model is PlayerHull.Assembly).</summary>
        public void SwapPlayerShip(int shipIndex)
        {
            var ctrl = Player;
            if (ctrl == null) return;
            int before = Session.ShipIndex;
            Session.ShipIndex = shipIndex;
            var prefab = AssembledObject.LoadPrefab(PlayerHull.Assembly(db, shipIndex));
            if (prefab == null) { Session.ShipIndex = before; return; }
            Hangar.SyncBuiltIn(db);   // the new hull's built-in items, the old one's gone
            var root = ctrl.gameObject;
            PlayerHull.RemoveTurrets(this);   // before the old model goes (they ride on it)
            if (ctrl.visualModel != null) Destroy(ctrl.visualModel.gameObject);
            var model = Instantiate(prefab, root.transform, false);
            model.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
            GoF2Remake.Visuals.AutoLods.FullDetail(model);   // the player's own ship: full detail
            foreach (var lg in model.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
            PlayerHull.PrepareModel(db, model);   // the Void ship at its full size
            // Remake mods: the exhaust style of the ship's build (ModExhausts; before the exhaust particles, which it replaces).
            if (PlayerHull.OwnEngines(db)) Modding.ModExhausts.Apply(db, shipIndex, model, Modding.ModShipKits.PlayerBuild(shipIndex));
            ctrl.visualModel = model.transform;
            if (Health != null) HullCollision.Attach(model, Health.Target, null, HullCollision.FlyableShip(shipIndex));

            var ship = db.Ship(shipIndex);
            var equipment = new System.Collections.Generic.List<ItemData>();
            foreach (var e in Session.Equipment) { var it = db.Item(e.item); if (it != null) equipment.Add(it); }
            if (ship != null) ctrl.stats = Database.BuildFlightStats(ship, equipment, Session.ModLevel(3));
            ctrl.stats.cargoAffectsHandling = Session.IsExtreme;
            ctrl.stats.cargoCapacity = Mathf.Max(1, Shop.MaxLoad(db));
            ctrl.stats.cargoLoad = Shop.CargoLoad();
            ctrl.ApplyStats();
            Health?.RefreshLoadout(db, true);   // the new hull's hull points (its armor value, mod 0) and the equipment's pools

            PlayerHull.FitCamera(root.transform, model.transform, chase, farClip);
            PlayerHull.ApplyMass(ctrl, model.transform, chase);   // a capital ship flies like one
            Weapons?.Rebuild(db, shipIndex, Session.Equipment);   // the new hull's mounts (none for 13 / 14 / 15 / capital ships)

            foreach (var ex in root.GetComponents<ShipExhaust>()) Destroy(ex);
            if (PlayerHull.OwnEngines(db)) ShipExhaust.Attach(root, db, ctrl, shipIndex);   // a freighter / capital ship: none
            if (Cloak != null) Destroy(Cloak);
            Cloak = PlayerCloak.Attach(root, db, shipIndex, Health.Target, model.transform);
            if (Navigation != null) Navigation.Cloak = Cloak;
            if (Collision != null) Collision.cloak = Cloak;
            PlayerTurret.RemoveAll(turrets);
            turrets.Clear();
            turrets.AddRange(PlayerTurret.AttachAll(root, db, shipIndex, Session.Equipment, chase));
            if (hardpoints != null) Destroy(hardpoints);
            hardpoints = Modding.ModHardpoints.AttachWeapons(db, shipIndex, Session.Equipment, ctrl.visualModel);

            PlayerHull.AttachTurrets(this);
        }
        void SpawnPlayer()
        {
            Hangar.SyncBuiltIn(db);   // remake mods: the flown ship's built-in items (ships.json "integrated": "items")
            var ship = db.Ships.Find(s => s.index == Session.ShipIndex);
            var root = new GameObject($"Player ({ship?.name})");
            if (Session.ArrivedByTravel)
            {
                Layout.ArrivalPose(Session.PreviousStationIndex, out var arrival, out var facing);
                root.transform.SetPositionAndRotation(arrival, facing);
            }
            else if (launchFromMobile >= 0 && Mobiles.Find(m => m.Station == launchFromMobile) is MobileStation from)
            {
                var pose = from.LaunchPose();   // remake mods: out beside the mobile station, along its course
                root.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            else
                root.transform.SetPositionAndRotation(
                    OrbitLayout.ToUnity(UndockPoint),
                    OrbitLayout.RotationToUnity(new Vector3(0f, (Random.value < 0.5f ? 1 : -1) * OrbitLayout.UndockYaw / 65536f * 2f * Mathf.PI, 0f)));
            // Multiplayer: players launching together sit side by side, 80 m apart by client id (one of 8 slots, -240..+320 m
            // across the undock point: by the raw id a player who had reconnected a few dozen times came out kilometres to the
            // side of the entrance, the ids only grow); an arena's spawn ring.
            if (GoF2Remake.World.SpaceHooks.InArena) { var spawn = GoF2Remake.World.SpaceHooks.ArenaSpawnPose(); root.transform.SetPositionAndRotation(spawn.position, spawn.rotation); }
            else if (NetSession.Active) root.transform.position += root.transform.right * (((int)(NetSession.LocalId % 8) - 3) * 80f);
            // Multiplayer: answering a squadmate's distress call, next to them (NetDistress).
            if (GoF2Remake.Data.NetSession.Active && Session.ArrivedByTravel && SpaceHooks.DistressArrival != null && SpaceHooks.DistressArrival(Layout.stationIndex, out var near, out var nearFacing))
                root.transform.SetPositionAndRotation(near, nearFacing);
            // LevelScript::LevelScript 0x160380: at Coromesk (103) from campaign 0x55 on (or at 0x87), outside the Void, every
            // start is at (70000, 0, 100000) facing the station (the mining plant stands at the origin from then on).
            int cm = Session.CampaignMission;
            if ((cm > 0x54 || cm == 0x87) && Layout.stationIndex == 103 && Layout.systemIndex >= 0)
            {
                var start = OrbitLayout.ToUnity(new Vector3(70000f, 0f, 100000f));
                root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(-start.normalized, Vector3.up));
            }
            // Remake: a fly-in out of interstellar space starts as far out as its speed carries it, to end where arrivals do.
            if (warpArrival && InterstellarSpace.Drive(db) is ItemData warpDrive)
            {
                root.transform.position -= root.transform.forward * InterstellarSpace.CarryMeters(warpDrive.Attr(CruiseDrive.SpeedAttr), WarpSky.ArrivalCarryMs);
                root.transform.rotation *= Session.WarpHeading;   // still pointing the way it was, off its course or not
            }
            // Remake: outside the system it left, heading for the programmed destination (InterstellarSpace).
            if (Layout.interstellar)
            {
                var from = InterstellarSpace.Position(db.Systems.Find(s => s.index == Layout.systemIndex) ?? db.Systems[0]);
                var goal = db.Stations.Find(s => s.index == Session.ProgrammedStation) is StationData prog
                           && db.Systems.Find(s => s.index == prog.system) is SystemData sys && sys.index != Layout.systemIndex && GalaxyMap.IsVisible(db, sys.index)
                    ? InterstellarSpace.Position(sys) : Vector3.zero;
                InterstellarSpace.Start(from, goal, out var at, out var forward);
                root.transform.SetPositionAndRotation(OrbitLayout.ToUnity(at), Quaternion.LookRotation(OrbitLayout.DirToUnity(forward), Vector3.up));
            }
            // Remake multiplayer: an admin's teleport into this orbit (NetTeleport) brings its own pose.
            if (SpaceHooks.TeleportPose != null && SpaceHooks.TeleportPose(out var tpPos, out var tpRot)) root.transform.SetPositionAndRotation(tpPos, tpRot);
            var ctrl = root.AddComponent<ShipController>();
            var equipment = new System.Collections.Generic.List<ItemData>();
            foreach (var e in Session.Equipment) { var it = db.Item(e.item); if (it != null) equipment.Add(it); }
            if (ship != null) ctrl.stats = Database.BuildFlightStats(ship, equipment, Session.ModLevel(3));   // mod 3: handling +0.2 per level
            // PlayerEgo ctor: +0x235 = Status::hardCoreMode(), the cargo load then weighs on the handling.
            ctrl.stats.cargoAffectsHandling = Session.IsExtreme;
            ctrl.stats.cargoCapacity = Mathf.Max(1, Shop.MaxLoad(db));
            ctrl.stats.cargoLoad = Shop.CargoLoad();
            ctrl.sensitivity = Settings.Sensitivity;
            ctrl.invertPitch = Settings.InvertPitch;
            ctrl.invertYaw = Settings.InvertYaw;
            ctrl.ApplyStats();

            var entry = PlayerHull.Assembly(db, Session.ShipIndex);   // remake debug: the Ships tab's pick (else the ship's own)
            var prefab = AssembledObject.LoadPrefab(entry);
            if (prefab != null)
            {
                var model = Instantiate(prefab, root.transform, false);
                model.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
                // AEGeometry::updateLod always picks LOD 0 in this binary: the player's ship keeps full detail, or its
                // lights (LOD 0 only) popped in as a cutscene camera closed in.
                GoF2Remake.Visuals.AutoLods.FullDetail(model);
                foreach (var lg in model.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
                PlayerHull.PrepareModel(db, model);   // remake debug: the Void ship at its full size
                // Remake mods: the exhaust style of the ship's build (ModExhausts; the exhaust particles are attached below).
                if (PlayerHull.OwnEngines(db)) Modding.ModExhausts.Apply(db, Session.ShipIndex, model, Modding.ModShipKits.PlayerBuild(Session.ShipIndex));
                ctrl.visualModel = model.transform;
            }
            Player = ctrl;

            // Level::createPlayer: one gun per equipped weapon on the ship's mounts (weapons_hd.json).
            Weapons = root.AddComponent<WeaponSystem>();
            Weapons.Setup(db, Session.ShipIndex, Session.Equipment);

            chase = mainCamera.GetComponent<ChaseCamera>();
            if (chase == null) chase = mainCamera.gameObject.AddComponent<ChaseCamera>();
            chase.target = ctrl;
            // TargetFollowCamera offsets (game local) -> Unity local (-x, y, z) * 0.05; a mod ship's own "camera" (ModShipView).
            Modding.ModShipView.CameraOffsets(db.Ship(Session.ShipIndex), M, out chase.offset, out chase.lookOffset);
            // Remake debug: a freighter's or capital ship's hull is far bigger than any ship the camera was made for.
            if (PlayerHull.Big) PlayerHull.FitCamera(root.transform, ctrl.visualModel, chase, farClip);
            PlayerHull.ApplyMass(ctrl, ctrl.visualModel, chase);   // remake debug: a capital ship flies like one (0 for every ordinary ship)
            // CameraSetPerspective(1.22 rad) is the vertical FOV: with the level look offset the ship then sits in the
            // lower middle of the screen like in the original. Used as the 16:9 value (Hor+ on wider screens). Remake: the
            // field of view option (Settings.OriginalFov by default).
            chase.baseFov = Settings.FieldOfView;
            chase.boostFovAdd = 0.35f * Mathf.Rad2Deg;   // MGame::OnUpdate: + 0.35 rad x the boost percentage
            chase.positionCoefficient = 0.006f;         // TargetFollowCamera::resetShipHandling: position / look-at
            chase.rotationCoefficient = 0.005f;
            chase.Snap();
            if (warpArrival)
            {
                chase.CarryView(Session.WarpView, Session.WarpViewTurn);
                ctrl.Model.CarryCruiseLook(WarpSky.ArrivalCarryMs);
            }
            // PlayerEgo::checkForTurret: the turret-slot item on the ship's turret mount (remake: each on its own mount).
            turrets.Clear();
            turrets.AddRange(PlayerTurret.AttachAll(root, db, Session.ShipIndex, Session.Equipment, chase));
            hardpoints = Modding.ModHardpoints.AttachWeapons(db, Session.ShipIndex, Session.Equipment, ctrl.visualModel);   // a kit ship's weapon models
            // MGame::switchCamera: the camera button's modes (standard / turret / free look).
            FreeLook = FreeLookCamera.Attach(root, chase, Turret);
            // Level::createGasClouds: the Supernova plasma clouds (a spectral filter mounted).
            if (!Layout.interstellar) GasClouds = GasCloudField.Spawn(db, Layout, ctrl, Turret);
            if (Radar != null) Radar.Clouds = GasClouds;   // the spectral filters' markers and lock
            Extender = TimeExtender.Attach(root, db);

            // Asteroid mining (lock, autopilot approach, minigame): needs a drill (category 19) to lock.
            Mining = root.AddComponent<Mining>();
            Mining.Setup(db, ctrl, Weapons, chase);
            // MGame::OnInitialize: the engine loop (PlayerEgo+0x1c) and the boost sound (+0xd4).
            PlayerEngine.Attach(root, db, ctrl);
            // Level::initParticleSystems: the exhaust particles (remake debug: none on a freighter / capital ship, PlayerHull).
            if (PlayerHull.OwnEngines(db)) ShipExhaust.Attach(root, db, ctrl, Session.ShipIndex);

            // Modified: the Supernova Challenge's round opens with the arrival fly-in, as after a jump into a system, but
            // close (ChallengeFlyInLocal), gliding to the chase position in 2 s (the original's video, frame by frame);
            // ChallengeOrbit's MovePlayer places it again around the round's start.
            challengeFlyIn = SupernovaChallenge.Active && !GoF2Remake.Data.NetSession.Active && GoF2Remake.World.SpaceHooks.Arena == null;
            if (challengeFlyIn) Session.ArrivedByTravel = true;
            if (warpArrival) { launchTravel = true; Session.LaunchedFromStation = Session.ArrivedByTravel = false; }
            else if (Session.LaunchedFromStation || Session.ArrivedByTravel) StartLaunchCamera();
        }
        /// <summary>Level::init with initStreamOutPosition (space_level_setup.md 4): into the gate orbit at the hidden gate
        /// (landmark 2); else 4 x the planet billboard (-20000 * dir) of the station the player came from, or (0, 0, 100000)
        /// when that station isn't in this system.</summary>
        /// <summary>Level::createCampaignMission / LevelScript: the level puts the player somewhere else (Unity pose); the launch /
        /// arrival camera is placed again around the new pose.</summary>
        public void MovePlayer(Vector3 position, Quaternion rotation)
        {
            Player.transform.SetPositionAndRotation(position, rotation);
            chase.Snap();
            if (launchCameraMs <= 0f) return;
            bool travel = launchTravel;
            float Side() => (Random.value < 0.5f ? -1f : 1f) * (travel ? Random.Range(500, 1000) : Random.Range(500, 2500));
            var local = new Vector3(-Side(), Side(), travel ? 7000f : 9000f) * M;
            if (challengeFlyIn) local = ChallengeFlyInLocal * M;
            mainCamera.transform.position = Player.transform.TransformPoint(local);
            mainCamera.transform.rotation = Quaternion.LookRotation(Player.transform.position - mainCamera.transform.position, Player.transform.up);
            if (challengeFlyIn) { challengeFlyStart = mainCamera.transform.position; challengeFlyMs = 0f; }
            PlaceArrivalWormhole();
        }
        /// <summary>Level::initStreamOutPosition: this level began with an arrival by travel (planet jump, gate, wormhole).</summary>
        public bool StreamOutArrival => launchTravel;
        // LevelScript::LevelScript: TargetFollowCamera in look-at mode at playerPos + playerRotation * (+-(500..2499),
        // +-(500..2499), 9000) (arrival by travel: +-(500..999), 7000); the chase camera takes over after 7000 ms.
        void StartLaunchCamera()
        {
            Session.LaunchedFromStation = false;
            bool travel = Session.ArrivedByTravel;
            launchTravel = travel;
            Session.ArrivedByTravel = false;
            float Side() => (Random.value < 0.5f ? -1f : 1f) * (travel ? Random.Range(500, 1000) : Random.Range(500, 2500));
            var local = new Vector3(-Side(), Side(), travel ? 7000f : 9000f) * M;   // ship-local game -> Unity (-x, y, z)
            if (challengeFlyIn) local = ChallengeFlyInLocal * M;
            mainCamera.transform.position = Player.transform.TransformPoint(local);
            mainCamera.transform.rotation = Quaternion.LookRotation(Player.transform.position - mainCamera.transform.position, Player.transform.up);
            if (challengeFlyIn) { challengeFlyStart = mainCamera.transform.position; challengeFlyMs = 0f; }
            mainCamera.fieldOfView = Aspect.VerticalFov(chase.baseFov, mainCamera.aspect);
            chase.enabled = false;
            launchCameraMs = LaunchCameraMs;
            Player.inputLocked = true;
            if (Weapons != null) Weapons.Blocked = true;
        }
        /// <summary>The end of the start sequence (LevelScript +0x24 > 7000): the chase camera and the controls come back, then
        /// the autopilot to a programmed station. 'skipped': the camera snaps behind the ship instead of easing there (the
        /// launch camera option off, a level opening on its own cutscene); a player's skip eases like the natural end.</summary>
        void EndLaunchCamera(bool skipped)
        {
            launchCameraMs = 0f;
            Player.inputLocked = false;
            if (Weapons != null) Weapons.Blocked = false;
            if (chase.scriptCamera) return;   // a level script's cutscene camera owns it (its Release brings the chase back)
            chase.enabled = true;   // eases from here to the chase position
            if (skipped) chase.Snap();
            if (Session.ProgrammedStation >= 0 && !Session.InstantJump) Navigation?.ContinueToProgrammedStation();
        }
        /// <summary>A level that opens on its own cutscene (LevelScript ctor, index 78 / 81): no launch / arrival camera.</summary>
        public void EndStartSequence()
        {
            if (launchCameraMs > 0f) EndLaunchCamera(true);
        }
        /// <summary>MGame::OnTouchEnd -> LevelScript::skipSequence: the player tried to fly (steer, throttle, boost, fire, a
        /// tap, any key or button except the pause and autopilot-menu ones) during the start sequence.</summary>
        /// <summary>Any key but Esc and the flight menus' (Q / E / V / K by default, GameControls), a click, a tap, a stick or a
        /// controller button but Menu and the menus' (View, D-pad left) this frame (also the station's skip for the hangar
        /// flights). The menu keys do nothing during the fly-in (FlightHud), so they don't skip it either.</summary>
        public static bool PlayerTriedToFly()
        {
            var kb = GoF2Remake.World.SpaceHooks.Keys;
            bool menuKey = GameControls.AutopilotMenu.WasPressedThisFrame() || GameControls.ActionsMenu.WasPressedThisFrame()
                           || GameControls.Wingmen.WasPressedThisFrame() || GameControls.KhadorDrive.WasPressedThisFrame();
            if (kb != null && kb.anyKey.wasPressedThisFrame && !kb.escapeKey.wasPressedThisFrame && !menuKey) return true;
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return true;
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.25f || pad.rightStick.ReadValue().sqrMagnitude > 0.25f) return true;
                if (pad.rightTrigger.wasPressedThisFrame || pad.leftTrigger.wasPressedThisFrame) return true;
                foreach (var c in pad.allControls)
                    if (c is ButtonControl b && b.wasPressedThisFrame && b != pad.startButton && !menuKey) return true;
            }
            return false;
        }
        /// <summary>Modified: the Supernova Challenge's fly-in: from where it started (in front of the ship) the camera glides to
        /// the chase position behind the ship over ChallengeFlyInMs, eased in and out, looking at the ship and turning to the
        /// chase camera's look point as it arrives; at the end it is exactly the chase camera's pose, so the hand-over has no
        /// jump (a skip eases in from wherever it is, like the arrival's).</summary>
        void UpdateChallengeFlyIn()
        {
            float dtMs = Time.deltaTime * 1000f;
            var ship = Player.transform;
            var cam = mainCamera.transform;
            // The ship's velocity (m per ms), measured and smoothed: where the chase camera trails it in flight.
            if (challengeFlyMs <= 0f) { challengeFlyShipLast = ship.position; challengeFlyVelocity = Vector3.zero; }
            else if (dtMs > 0f)
            {
                var v = (ship.position - challengeFlyShipLast) / dtMs;
                challengeFlyVelocity = Vector3.Lerp(challengeFlyVelocity, v, challengeFlyVelocity == Vector3.zero ? 1f : 0.2f);
                challengeFlyShipLast = ship.position;
            }
            challengeFlyMs += dtMs;
            float t = Mathf.Clamp01(challengeFlyMs / ChallengeFlyInMs);
            float w = t * t * t * (t * (t * 6f - 15f) + 10f);   // smootherstep: no jerk at either end
            // The end: the chase camera's own resting place behind the moving ship (it trails the standard offset by its
            // follow lag), so the hand-over neither jumps nor drifts back.
            chase.SteadyPose(challengeFlyVelocity, dtMs, out var endPos, out var endRot);
            cam.position = Vector3.Lerp(challengeFlyStart, endPos, w);
            var look = Vector3.Lerp(ship.position, cam.position + endRot * Vector3.forward * (ship.TransformPoint(chase.lookOffset) - endPos).magnitude, w);
            var dir = look - cam.position;
            if (dir.sqrMagnitude > 1e-6f) cam.rotation = Quaternion.LookRotation(dir, ship.up);
            if (t >= 1f) { cam.SetPositionAndRotation(endPos, endRot); EndLaunchCamera(false); }   // the chase goes on from here
        }
        /// <summary>The last save (auto-save slot) after a failed mission, or the main menu without one.</summary>
        /// <summary>Remake co-op: the leader's story level moved it on (a ride, a jump, docked by the story): the crew in this
        /// orbit follows (NetCoop -> CampaignLevel.OnLeaderTravel).</summary>
        void CoopLeaderMoved(int to, bool dock, bool streamOut)
        {
            if (!CoopCampaign.Leader || Campaign == null || Campaign.Follower || Layout == null || Leaving) return;
            SpaceHooks.CoopTravel?.Invoke(Layout.stationIndex, to, dock, streamOut);
        }

        /// <summary>Remake co-op: step 43 won by the crew while this player flies: the ending's credits (EndingCredits, over the
        /// main menu scene's backdrop; the session stays), then docked at the step's station (Thynome, where Keith's epilogue
        /// 44 follows) rather than here (the Void's orbit has no station).</summary>
        public void PlayEnding()
        {
            if (Leaving) return;
            Leaving = true;
            Weapons?.StoreAmmo();
            int to = Story.Mission != null && Story.Mission.station >= 0 ? Story.Mission.station : Session.LastDockedStation;
            if (to >= 0 && to < db.Stations.Count) Session.StationIndex = to;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            Session.EndingPending = true;
            SceneManager.LoadScene("MainMenu");
        }

        /// <summary>Remake co-op: this orbit built again (a prologue follower into the rescue once the leader is there).</summary>
        public void Reload()
        {
            if (Leaving) return;
            Leaving = true;
            Weapons?.StoreAmmo();
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// <summary>Remake co-op: a failed story mission: back docked at the station last docked at (the session has no saves).</summary>
        public void ReturnToLastDocked()
        {
            if (Leaving) return;
            int docked = Session.LastDockedStation;
            if (docked >= 0 && docked < db.Stations.Count) Session.StationIndex = docked;
            Leaving = true;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            Cheats.Repair();
            if (Application.CanStreamedLevelBeLoaded(stationScene)) SceneManager.LoadScene(stationScene);
        }

        public void LoadLastSave()
        {
            if (Modding.ModBuilder.PlayTesting) { Modding.ModBuilder.EndPlayTest(); return; }   // a Mod Builder play test: back to the builder
            if (Session.LoadAutosave() && Application.CanStreamedLevelBeLoaded(stationScene)) SceneManager.LoadScene(stationScene);
            else SceneManager.LoadScene("MainMenu");   // (build index 0 is the Splash scene)
        }
        /// <summary>MGame::dockEvent: straight to the station module (SetCurrentApplicationModule(5)). 'flyIn': the player
        /// docked (the autopilot, touching the station, the Dock prompt), so the station opens with the remake's hangar
        /// fly-in; the story's own moves into a station (the rescue, arrests, "docked at ...") pass false.</summary>
        public void Dock(bool flyIn = false)
        {
            if (!flyIn) CoopLeaderMoved(Session.StationIndex, true, false);   // remake co-op: the story's move into a station
            // Remake mods: the player's own docking near a mobile station (and not at the orbit's station) docks there.
            if (flyIn && MobileInRange is MobileStation near && !(Layout.hasStation && StationShown && leftDockRange && InDockRange))
            {
                DockMobile(near);
                return;
            }
            // Remake mods: EVERSPACE 2's docking: the ship lands on the dock in this orbit, then the station opens (Es2Dock).
            if (flyIn && Es2 != null && !Leaving && Layout.hasStation && StationShown && PlayerHull.PlayerShip)
            {
                if (Es2Busy || DockedInOrbit) return;
                if (DockingBlocked) { Navigation?.Refuse(); return; }
                Navigation?.SetAutopilot(null);
                ActiveEs2 = Es2;
                Es2.BeginDocking(DockInOrbit);
                return;
            }
            DockInto(flyIn);
        }
        void DockInto(bool flyIn)
        {
            // Remake debug (PlayerHull): a hull the player can't normally fly never lands in a hangar. The player's own docking
            // is refused (CanDock and the autopilot already refuse it); the story's moves into a station put the player
            // back in their own ship first.
            if (!PlayerHull.PlayerShip)
            {
                if (flyIn)
                {
                    Navigation?.Refuse(string.Format(Localization.Extra("hullNoDock", "The {0} doesn't fit in the hangar."), PlayerHull.Label));
                    return;
                }
                PlayerHull.ForceOwnShip();
            }
            Leaving = true;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = flyIn;
            Weapons?.StoreAmmo();   // MGame::dockEvent saves the ship state to Status
            if (Application.CanStreamedLevelBeLoaded(stationScene)) SceneManager.LoadScene(stationScene);
        }
        /// <summary>Remake mods: EVERSPACE 2's docking is over (the ship on its pad): the Station scene loads on top of this
        /// level, which lives on around it (traffic, sky, the station's own model as the hangar); the HUD and the flight
        /// controls wait, the ship's state goes to Status like a docking's, its sounds pause.</summary>
        void DockInOrbit()
        {
            if (ActiveEs2 == null || !Application.CanStreamedLevelBeLoaded(stationScene)) { DockInto(false); return; }
            if (ActiveEs2.StationIndex != Layout.stationIndex && Session.StationIndex != ActiveEs2.StationIndex)
            {
                // a mobile station's dock (DockAt's): its hangar
                Session.PreviousStationIndex = Layout.stationIndex;
                Session.StationIndex = ActiveEs2.StationIndex;
            }
            DockedInOrbit = true;
            SpaceHooks.DockedInOrbit = true;
            Session.LaunchedFromStation = false;
            Session.DockedFromSpace = false;
            Weapons?.StoreAmmo();
            Health?.StoreToSession();
            Navigation?.SetAutopilot(null);
            if (Player.visualModel != null) Player.visualModel.gameObject.SetActive(false);   // the station shows its own
            dockPausedAudio.Clear();
            // (the loops: music, engines, ambience; one-shots like the docking's own sound play out)
            foreach (var a in FindObjectsByType<AudioSource>())
                if (a.isPlaying && a.loop && a.gameObject.scene == gameObject.scene) { a.Pause(); dockPausedAudio.Add(a); }
            // One EventSystem at a time: the Station scene brings its own (its menu's input), so the Space scene's rests while
            // docked ("There are 2 event systems in the scene" every frame otherwise). By name: GoF2.Space doesn't reference UGUI.
            spaceEventSystem = null;
            foreach (var g in gameObject.scene.GetRootGameObjects())
                if (g.name == "EventSystem" && g.activeSelf) { spaceEventSystem = g; g.SetActive(false); break; }
            SceneManager.LoadSceneAsync(stationScene, LoadSceneMode.Additive);
        }
        /// <summary>Remake mods: the station's Launch while docked inside the orbit: the station's scene goes, the ship (as the
        /// station left it: a new hull, equipment, repairs) takes off EVERSPACE 2's way from its pad.</summary>
        public void UndockFromOrbit()
        {
            if (!DockedInOrbit) return;
            DockedInOrbit = false;
            SpaceHooks.DockedInOrbit = false;
            var station = SceneManager.GetSceneByName(stationScene);
            // the Space scene's EventSystem back once the station's has gone with its scene
            var es = spaceEventSystem;
            spaceEventSystem = null;
            void Restore() { if (es != null) es.SetActive(true); }
            var unload = station.IsValid() && station.isLoaded ? SceneManager.UnloadSceneAsync(station) : null;
            if (unload != null) unload.completed += _ => Restore(); else Restore();
            RestoreOrbitSky();
            if (mainCamera != null) mainCamera.gameObject.SetActive(true);
            foreach (var a in dockPausedAudio) if (a != null) a.UnPause();
            dockPausedAudio.Clear();
            SwapPlayerShip(Session.ShipIndex);   // what the station changed: the hull, its build, the equipment, the turrets
            Health?.ReloadFromSession(db);       // repaired on docking
            leftDockRange = false;
            ActiveEs2.BeginTakeOff();
        }
        /// <summary>Remake mods: the player's docking at a mobile station: EVERSPACE 2's onto its pad when it docks that way,
        /// else its hangar (DockAt).</summary>
        void DockMobile(MobileStation mobile)
        {
            if (MobileDock(mobile) is not Es2Dock d) { DockAt(mobile); return; }
            if (Es2Busy || DockedInOrbit || Leaving) return;
            if (DockingBlocked) { Navigation?.Refuse(); return; }
            if (!PlayerHull.PlayerShip)
            {
                Navigation?.Refuse(string.Format(Localization.Extra("hullNoDock", "The {0} doesn't fit in the hangar."), PlayerHull.Label));
                return;
            }
            Navigation?.SetAutopilot(null);
            ActiveEs2 = d;
            d.BeginDocking(DockInOrbit);
        }
        /// <summary>Remake mods: docks at a mobile station (its hangar and bar: the Station scene with its index).</summary>
        void DockAt(MobileStation mobile)
        {
            if (DockingBlocked) { Navigation?.Refuse(); return; }
            if (!PlayerHull.PlayerShip)
            {
                Navigation?.Refuse(string.Format(Localization.Extra("hullNoDock", "The {0} doesn't fit in the hangar."), PlayerHull.Label));
                return;
            }
            Session.PreviousStationIndex = Layout.stationIndex;
            Session.StationIndex = mobile.Station;
            DockInto(true);
        }
    }
}
