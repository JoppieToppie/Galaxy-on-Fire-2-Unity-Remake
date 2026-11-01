// StationLevelInteriors.cs
// StationLevel: remake mods: a custom hangar / bar (ModInteriors) (part of the partial StationLevel; its fields are in StationLevel.cs).

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
        /// <summary>The custom hangar's way in and out: through its "gate" marker toward "gate_out", passable at gateSpan (default
        /// the gate's height +-30 m), crossing the room at 'cruise' metres above the pad (default 40); none without the markers.</summary>
        StationTables.HangarLane CustomLane
        {
            get
            {
                if (customLane != null || customHangar == null || !customHangar.hasGate || !customHangar.def.flights || customHangar.es2Dock) return customLane;
                var outward = customHangar.gateOut - customHangar.gate;
                outward.y = 0f;
                if (outward.sqrMagnitude < 1e-4f) return null;
                float gy = customHangar.gate.y;
                customLane = new StationTables.HangarLane
                {
                    gate = customHangar.gate, outward = outward.normalized,
                    gateSpan = customHangar.def.gateSpan ?? new Vector2(gy - 30f, gy + 30f),
                    cruise = customHangar.pad.y + (float.IsNaN(customHangar.def.cruise) ? 40f : customHangar.def.cruise),
                };
                return customLane;
            }
        }
        /// <summary>The player's (and a parked ship's) pivot on a pad: the original's table, or a custom hangar's marker plus the
        /// ship's own height (StationTables.ShipY).</summary>
        Vector3 PadPosition(int ship) => customHangar != null
            ? customHangar.pad + Vector3.up * (StationTables.ShipY(ship) * M)
            : OrbitLayout.ToUnity(new Vector3(0f, StationTables.PadPivotY(HangarIndex, -1, ship, Quaternion.identity) / M, 0f));
        int ParkedSlotCount => customHangar != null ? customHangar.parked.Count : StationTables.ParkedSlots[HangarIndex]?.Length ?? 0;
        int ParkedMaxCount => customHangar != null ? (customHangar.def.parkedMax >= 0 ? Mathf.Min(customHangar.def.parkedMax, customHangar.parked.Count) : customHangar.parked.Count)
                                                   : StationTables.ParkedMax[HangarIndex];
        int VisitorSlotCount => customBar != null ? customBar.visitors.Count : StationTables.VisitorSlots[BarRace].Length;
        Vector3 SlotFeet(int slot) => customBar != null ? customBar.visitors[slot] : OrbitLayout.ToUnity(StationTables.VisitorSlots[BarRace][slot]);
        /// <summary>Remake: the remodelled Nivelian room's pads (hangar_nivelian_alt_alpha_add, material 99006: lit, blended,
        /// glowing) are drawn in the opaque range with depth. The ship shadows (HangarShipShadow, a decal on the camera's depth
        /// texture) lie on what the depth shows; as a transparent layer the pads wrote none, so the shadows fell on the pit
        /// 3.5 m under them, outside their box, and the pads (drawn after the decals) covered them. URP's material validation
        /// turns a transparent Lit material's depth back off whenever the Editor loads it, so the copy is made here.</summary>
        static void DrawPadsWithDepth(GameObject room)
        {
            if (room == null) return;
            foreach (var r in room.GetComponentsInChildren<MeshRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m == null || !r.name.StartsWith("hangar_nivelian_alt_alpha_add")) continue;
                var c = new Material(m) { name = m.name + " (depth)" };
                c.SetFloat("_ZWrite", 1f);
                c.SetShaderPassEnabled("DepthOnly", true);
                c.renderQueue = (int)RenderQueue.GeometryLast;   // after the pit under them, before the shadows (Transparent-10)
                r.sharedMaterial = c;
            }
        }
        /// <summary>Remake: the Valkyrie battlestation hangar's v_hangar_battlestation_add_part32 / 33 sat off their place (their
        /// X keys are the absolute place while the import baked it into their vertices too): held at a local x placed by eye
        /// in the remake (their other keys still play).</summary>
        static void PlaceValkyrieHangarParts(GameObject room)
        {
            if (room == null) return;
            foreach (var t in room.GetComponentsInChildren<Transform>(true))
            {
                float x;
                if (t.name == "v_hangar_battlestation_add_part32") x = -57.2f;
                else if (t.name == "v_hangar_battlestation_add_part33") x = -57.1f;
                else continue;
                var anim = t.GetComponentInParent<PartAnimation>(true);
                if (anim == null || !anim.PinLocalX(t, x)) t.localPosition = new Vector3(x, t.localPosition.y, t.localPosition.z);
            }
        }
        void BuildHangar()
        {
            hangarRoot = new GameObject("Hangar").transform;
            hangarRoot.gameObject.AddComponent<GoF2Remake.Visuals.StationModelContext>();
            string room = StationTables.HangarRoom[HangarIndex];
            // Level::createScene: PlayerStatic + setRotation(0, pi, 0) = Unity identity. Remake mods: a custom room as it is.
            GameObject roomGo;
            if (orbitDocked && Es2Docked) roomGo = null;   // the station in its orbit is the room
            else if (customHangar != null) { roomGo = Instantiate(customHangar.template, Vector3.zero, Quaternion.identity, hangarRoot); roomGo.name = "Room (" + customHangar.def.key + ")"; }
            else roomGo = Spawn(room, Vector3.zero, OrbitLayout.RotationToUnity(new Vector3(0f, Mathf.PI, 0f)), hangarRoot, "Room");
            HangarRoom = roomGo != null ? roomGo.transform : null;
            // Modified: the Nivelian hangar's three service bots (hangar_nivelian_bot_a..c, their own assemblies: the
            // original never creates them, CutScene+0x38's bot array stays empty), on the room's transform like the Terran
            // hangar's (which are in its assembly); they fly one at a time like those (SetupTerranBots).
            if (customHangar == null && room == "hangar_nivelian" && roomGo != null)
                foreach (char c in "abc")
                    Spawn("hangar_nivelian_bot_" + c, roomGo.transform.position, roomGo.transform.rotation, roomGo.transform, "hangar_nivelian_bot_" + c);
            // Modified: the Terran hangar's fifth bot (hangar_terran_bot_e, its own assembly: a remake model, not in the room's
            // assembly with a to d), on the room's transform; it takes its turn with the other four (SetupTerranBots).
            if (customHangar == null && room == "hangar_terran" && roomGo != null)
                Spawn("hangar_terran_bot_e", roomGo.transform.position, roomGo.transform.rotation, roomGo.transform, "hangar_terran_bot_e");
            // CutScene::process updates every geometry of the scene each frame, so the room's animated layers all run,
            // not only the *_anim meshes (the Terran hangar_terran_add lights run along the side gutters; at frame 0 they
            // sat on the player's pad). The loops skip their one-off first key (every part at the origin for 33 / 50 ms,
            // a jump on every wrap: PartAnimation's default start), the rotations swing back and forth (the original Vossk ring lights' 57 deg sweep, clear of the
            // portal, instead of snapping back; the remodelled room's turn full circles), and the Vossk portal's light fades out as it moves (its `extra` channel).
            if (roomGo != null)
                foreach (var a in roomGo.GetComponentsInChildren<PartAnimation>(true))
                {
                    a.play = true;
                    // Modified: not the Terran hangar's bots (hangar_terran_bot_*): they drive a keyed loop, a heading swung
                    // back on every other loop turned them against their path.
                    // Modified: nor the remodelled Vossk hangar's ring lights (hangar_vossk_anim_add, 2026-10-24): they turn
                    // full circles now, swung back they ran backwards on every other loop.
                    a.pingPongRotation = !a.gameObject.name.Contains("_bot_") && !a.gameObject.name.StartsWith("hangar_vossk");
                    // Remake: and the Nivelian hangar's flashing light (hangar_nivelian_add, PartAnimation.HasSignedExtra).
                    // Modified: and the Terran hangar's blinking light (hangar_terran_add_part0, its `extra` 0..100), and the
                    // Midorian hangar's (hangar_midorian_add_part0: dark, up over 250 ms, lit 500 ms, down over 250 ms, a 2 s loop).
                    if (a.gameObject.name.Contains("_anim") || a.HasSignedExtra || a.gameObject.name.StartsWith("hangar_terran_add")
                        || a.gameObject.name.StartsWith("hangar_midorian_add"))
                        a.applyMaterialChannels = true;
                    // Modified: the Terran hangar's beta bots are keyed heading first (PartAnimation.headingFirst).
                    if (a.gameObject.name.StartsWith("hangar_terran_bot_")) a.headingFirst = true;
                }
            SetupTerranBots(roomGo);
            if (customHangar == null) PlaceValkyrieHangarParts(roomGo);
            DrawPadsWithDepth(roomGo);
            CollectRoomHighlights(roomGo);

            shipPivot = PadPosition(shipIndex);   // clear of the pad (remake)
            var ship = SpawnShip(shipIndex, shipPivot, Quaternion.identity, hangarRoot, "Player ship");
            playerShip = ship != null ? ship.transform : null;
            if (playerShip != null) PlayerHull.FitHangar(playerShip);   // remake debug: a freighter / capital ship shrunk
            CentreOnPad();
            RefreshTurret(true);
            shipYaw = StationTables.StartYaw(HangarIndex);
            if (customHangar != null)
            {
                // Unity degrees: "startYaw", else the ship's nose 35 deg off the camera's line (game yaw: Unity yaw = 180 - it).
                var toCam = customHangar.camera - customHangar.pad;
                float unityYaw = !float.IsNaN(customHangar.def.startYaw) ? customHangar.def.startYaw
                               : Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg + 35f;
                shipYaw = (180f - unityYaw) * Mathf.Deg2Rad;
            }
            ApplyShipYaw();
            // Multiplayer: another player docked here runs the hangar's NPC ships: theirs come with its snapshot (NetHangar).
            bool hangarFollower = GoF2Remake.Data.NetSession.Active && GoF2Remake.World.SpaceHooks.OtherDocked(Layout.stationIndex);
            if (!hangarFollower) SpawnParkedShips();
            // The others come and go (remake), except the club's stored hulls. Multiplayer: the other players docked here park
            // on the slots too (NetHangar), with or without the NPC traffic and the flights.
            bool slots = ParkedSlotCount > 0 && ParkedMaxCount > 0;
            bool npcTraffic = Settings.HangarFlights && Lane != null && slots && !KaamoClub.StorageAt(Layout.stationIndex) && !Evacuated;
            if (npcTraffic || (slots && GoF2Remake.Data.NetSession.Active))
                traffic = new HangarTraffic(Lane, ParkedSlotCount, ParkedMaxCount,
                                            parkedShips, db, NewParkedShip, ParkedPosition,
                                            (ship, pos, rot) => SpawnShip(ship, pos, rot, hangarRoot, "Visiting ship",
                                                                          // another player's: their build of a customizable ship
                                                                          traffic != null && traffic.SpawningGuest >= 0 ? SpaceHooks.GuestAssembly?.Invoke((ulong)traffic.SpawningGuest, ship) : null),
                                            npcTraffic, Settings.HangarFlights, FitParkingYaw);
            if (traffic != null) traffic.NpcSpawned = (go, ship) => AddNpcTurrets(go, ship, null);
            if (traffic != null && GoF2Remake.Data.NetSession.Active)
            {
                traffic.KeepOneFree = true;          // a pad for another player docking
                traffic.RunsNpcs = !hangarFollower;
            }
            SpaceHooks.StationBuilt?.Invoke(this);   // multiplayer: the other players in the hangar (NetHangar)

            // Docked inside the orbit: the hangar rides on the station's hull (room metres in its frame; a mobile station
            // carries it along), in the space level's scene.
            if (orbitDocked && Es2Docked)
            {
                hangarRoot.SetParent(orbit.ActiveEs2.Hull, false);
                hangarRoot.localPosition = Vector3.zero;
                hangarRoot.localRotation = Quaternion.identity;
                hangarRoot.localScale = Vector3.one / Mathf.Max(0.001f, customHangar.def.scale);
            }
            // Camera: ModStation::OnInitialize state 0x14 (phone table), rotation order 2 with roll -0.03.
            hangarCamBase = customHangar != null ? Vector3.zero : StationTables.HangarCameraPos[HangarIndex];   // a custom room drifts about its marker
            for (int i = 0; i < 3; i++) { driftSign[i] = Random.Range(0, 20) < 10; NextDriftLeg(i, Base(i)); }
        }
        /// <summary>Modified: HangarCubemapCapture's "every hangar" (an editor tool, Play mode): the hangar room built again as
        /// hangar 'index' (0..3 the races', 7 / 8 the Valkyrie's) with its lighting for 'raceId' (ambient, fog), the
        /// player's ship on its pad. RestoreHangar puts the station's own back.</summary>
        public void RebuildHangarAs(int index, int raceId)
        {
            if (index < 0 || index >= StationTables.HangarRoom.Length || StationTables.HangarRoom[index] == null) return;
            if (captureSavedIndex < 0) { captureSavedIndex = HangarIndex; captureSavedRace = Layout.raceId; captureSavedCustom = customHangar; }
            if (View != StationView.Hangar) SetView(StationView.Hangar);
            RebuildHangar(index, null, raceId);
        }
        /// <summary>Modified: the station's own hangar again after RebuildHangarAs.</summary>
        public void RestoreHangar()
        {
            if (captureSavedIndex < 0) return;
            RebuildHangar(captureSavedIndex, captureSavedCustom, captureSavedRace);
            captureSavedIndex = -1;
            captureSavedCustom = null;
        }
        void RebuildHangar(int index, Modding.ModInteriors.Room custom, int raceId)
        {
            traffic = null;
            parkedShips.Clear();
            if (hangarRoot != null) { hangarRoot.gameObject.SetActive(false); Destroy(hangarRoot.gameObject); }
            HangarIndex = index;
            customHangar = custom;
            BuildHangar();
            hangarRoot.gameObject.SetActive(View == StationView.Hangar);
            int race = Layout.raceId;
            Layout.raceId = raceId;
            ApplyHangarLighting();
            Layout.raceId = race;
        }
        /// <summary>CutScene::replacePlayerShip 0xa53b4: after buying a ship, the new one on the turntable at its own height
        /// (the camera keeps the pivot of the ship the hangar was entered with).</summary>
        public void ReplacePlayerShip(int index)
        {
            // The Mod Builder changes the mods while docked: a ship added since the hangar opened isn't in the old tables.
            if (dbRevision != Modding.ModManager.Revision) { db = Database.Load(); dbRevision = Modding.ModManager.Revision; }
            if (playerShip != null) Destroy(playerShip.gameObject);
            shipIndex = index;
            if (index == Session.ShipIndex) Hangar.SyncBuiltIn(db);   // its built-in items (a purchase, a stored hull, a debug swap)
            var ship = SpawnShip(index, PadPosition(index), Quaternion.identity, hangarRoot, "Player ship");
            playerShip = ship != null ? ship.transform : null;
            if (playerShip != null) PlayerHull.FitHangar(playerShip);   // remake debug: a freighter / capital ship shrunk
            CentreOnPad();
            RefreshTurret(true);
            ApplyShipYaw();
            ApplyEnginePreview();
        }
        /// <summary>How hard the previewed engines run (1 full throttle, 2 a full boost; EnginePreview).</summary>
        public void PreviewEngineDrive(float drive)
        {
            enginePreviewDrive = drive;
            var preview = playerShip != null ? playerShip.GetComponent<GoF2Remake.Visuals.EnginePreview>() : null;
            if (preview != null) preview.drive = drive;
        }
        /// <summary>Remake mods: the turntable ship's engines run (the Customize screen: its exhaust can be seen) or stop again.</summary>
        public void PreviewEngines(bool on)
        {
            enginePreview = on;
            ApplyEnginePreview();
        }
        void ApplyEnginePreview()
        {
            if (playerShip == null) return;
            var asm = playerShip.GetComponent<AssembledObject>();
            if (asm != null && asm.playerVariantParts != null)
                foreach (var p in asm.playerVariantParts) if (p != null) p.SetActive(enginePreview);
            var preview = playerShip.GetComponent<GoF2Remake.Visuals.EnginePreview>();
            if (enginePreview && preview == null) playerShip.gameObject.AddComponent<GoF2Remake.Visuals.EnginePreview>().drive = enginePreviewDrive;
            else if (!enginePreview && preview != null) Destroy(preview);
        }
        /// <summary>CutScene::checkForTurret 0xa4594: the mounted turret on the turntable ship, rebuilt whenever the turret
        /// item changes (the original re-runs it after every equipment change). Remake: every turret, each on its mount, and a
        /// kit ship's weapon models (ModHardpoints), rebuilt when the weapons change too.</summary>
        void RefreshTurret(bool force)
        {
            string items = string.Join(",", GoF2Remake.Flight.PlayerTurret.TurretItems(db, Session.Equipment)) + "|"
                         + string.Join(",", Modding.ModHardpoints.WeaponItems(db, Session.Equipment, false).Select(i => i.index)) + "|"
                         + string.Join(",", Modding.ModHardpoints.WeaponItems(db, Session.Equipment, true).Select(i => i.index));
            if (!force && items == turretItems) return;
            turretItems = items;
            if (turret != null) Destroy(turret);
            if (hardpoints != null) Destroy(hardpoints);
            turret = playerShip != null ? GoF2Remake.Flight.PlayerTurret.BuildStatic(db, shipIndex, Session.Equipment, playerShip) : null;
            hardpoints = playerShip != null ? Modding.ModHardpoints.AttachWeapons(db, shipIndex, Session.Equipment, playerShip) : null;
        }
        /// <summary>The club's parked hulls changed (Use / Sell in the storage): park them again.</summary>
        public void RefreshParkedShips()
        {
            if (!KaamoClub.StorageAt(Layout.stationIndex)) return;
            foreach (var p in parkedShips) if (p.go != null) Destroy(p.go);
            parkedShips.Clear();
            SpawnParkedShips();
        }
        /// <summary>Level::createScene 0x17: count = nextInt(max + 1), 70 % a fighter of the hangar's race, 30 % a random race
        /// (of those 30 % pirates); random free slot, yaw nextInt(300) / 100 rad. The owned Kaamo Club (kaamo_club.md 6.6)
        /// parks the first min(stored, max) hulls of its storage instead, in list order.</summary>
        void SpawnParkedShips()
        {
            int slotCount = ParkedSlotCount;
            int max = ParkedMaxCount;
            if (slotCount <= 0 || max <= 0 || Evacuated) return;   // remake: an evacuated station's hangar is empty
            bool club = KaamoClub.StorageAt(Layout.stationIndex);
            int count = Mathf.Min(club ? Mathf.Min(Session.KaamoShips.Count, max) : Random.Range(0, max + 1), slotCount);
            if (GoF2Remake.Data.NetSession.Active && !club) count = Mathf.Min(count, slotCount - 1);   // a pad for another player
            var taken = new bool[slotCount];
            for (int n = 0; n < count; n++)
            {
                int ship = club ? Session.KaamoShips[n].ship : NewParkedShip();
                int slot = Random.Range(0, slotCount), tries = 0;
                while (taken[slot] && ++tries < 100) slot = Random.Range(0, slotCount);
                if (taken[slot]) break;
                taken[slot] = true;
                float yaw = FitParkingYaw(slot, ship, Random.Range(0, 300) / 100f);
                var rot = OrbitLayout.RotationToUnity(new Vector3(0f, yaw, 0f));
                var parked = SpawnShip(ship, ParkedPosition(slot, ship, rot), rot, hangarRoot, $"Parked ship {n}");
                // Remake: its turrets (a stored hull its own mounted ones, Settings.KaamoKeepsEquipment; an NPC's its type's).
                AddNpcTurrets(parked, ship, club ? GoF2Remake.Flight.PlayerTurret.TurretItems(db, Session.KaamoShips[n].equipment) : null);
                if (parked != null) parkedShips.Add(new HangarTraffic.Parked { go = parked, slot = slot, ship = ship, yaw = yaw });
            }
        }
        /// <summary>Remake (generic turrets): a ship parked or landing here shows its turrets still, like the player's ship
        /// (Flight.TurretCore.Display): 'items' (a stored hull's mounted ones), else the turret items its type's NPCs carry in
        /// space (Database.NpcTurrets, a mod's "npcTurrets"), else the fighter turret of ships 45 / 51 (Traffic, the
        /// turret_002_static object Level::createFighterTurrets puts on them). Returns 'shipGo'.</summary>
        GameObject AddNpcTurrets(GameObject shipGo, int ship, IList<int> items)
        {
            if (shipGo == null) return shipGo;
            var shown = GoF2Remake.Flight.TurretCore.DisplayAll(db, ship, items ?? db.NpcTurretsOf(ship), shipGo.transform);
            // Turret items take the fighter turret's place (Traffic.Create), which sits on that mount.
            string fighterTurret = items == null && shown == null ? GoF2Remake.World.Traffic.FighterTurretAssembly(ship) : null;
            var prefab = fighterTurret != null ? AssembledObject.LoadPrefab(db.AssemblyByName(fighterTurret)) : null;
            if (prefab != null)
            {
                var t = Instantiate(prefab, shipGo.transform, false);
                t.name = "Fighter turret";
                t.transform.localPosition = GoF2Remake.World.Traffic.FighterTurretLocal(ship) + prefab.transform.localPosition;
                foreach (var a in t.GetComponentsInChildren<PartAnimation>(true)) a.speed = 0f;
            }
            return shipGo;
        }
        /// <summary>A heading that fits the pad (StationTables.FitParkingYaw); a custom hangar's as it is.</summary>
        float FitParkingYaw(int slot, int ship, float gameYaw) =>
            customHangar != null ? gameYaw : StationTables.FitParkingYaw(HangarIndex, slot, ship, gameYaw);
        /// <summary>The evacuated supernova system's stations (Shop.InSupernovaSystem, system 27 before campaign 158): an empty
        /// lounge like the original's (AgentGenerator.CreateAgents), and (remake) no parked ships, hangar traffic or bar flybys
        /// either.</summary>
        bool Evacuated => Shop.InSupernovaSystem(Shop.SystemOf(db, Layout.stationIndex), Layout.stationIndex);
        int NewParkedShip() => Layout.stationIndex == 100 ? StationTables.DeepScienceShips[Random.Range(0, 3)] : RandomParkedShip();
        /// <summary>The Unity pivot of 'ship' parked on 'slot' at 'rotation' (the slot plus the ship's height, lifted where the
        /// hull would cut into the pad: StationTables.PadPivotY).</summary>
        Vector3 ParkedPosition(int slot, int ship, Quaternion rotation)
        {
            // Another player's customizable ship floats at the height of their build (multiplayer, HangarTraffic.SpawningGuest).
            float guestY = traffic != null && traffic.SpawningGuest >= 0 && NetSession.Active && SpaceHooks.GuestShipY != null
                ? SpaceHooks.GuestShipY((ulong)traffic.SpawningGuest, ship) : float.NaN;
            return customHangar != null
                ? customHangar.parked[slot] + Vector3.up * ((float.IsNaN(guestY) ? StationTables.ShipY(ship) : guestY) * M)   // a custom hangar's marker
                : new Vector3(0f, StationTables.PadPivotY(HangarIndex, slot, ship, rotation, guestY), 0f)
                  + Vector3.Scale(OrbitLayout.ToUnity(StationTables.ParkedSlots[HangarIndex][slot]), new Vector3(1f, 0f, 1f));
        }
        int RandomParkedShip()
        {
            // Remake mods: a system with mods' ships only ("traffic": "mod"): its local factions' ships (ModWorld.Locals, else its
            // owner), like its orbit's locals; the game's fighters only when they have none.
            if (Modding.ModWorld.ModTrafficOnly(Layout.systemIndex))
            {
                var locals = Modding.ModWorld.Locals(Layout.systemIndex);
                int side = locals != null ? locals[Random.Range(0, locals.Count)] : Layout.raceId;
                return Flight.NpcTables.RandomFighter(side);
            }
            int race = HangarIndex;
            if (Random.value >= 0.7f) race = Random.value < 0.3f ? 8 : Random.Range(0, 4);
            var list = race == 8 ? StationTables.PirateFighters : StationTables.RaceFighters[Mathf.Clamp(race, 0, 3)];
            return list[Random.Range(0, list.Length)];
        }
        void BuildBar()
        {
            barRoot = new GameObject("Space Lounge").transform;
            barRoot.gameObject.AddComponent<GoF2Remake.Visuals.StationModelContext>();
            // createScene branch 4: rooms with no rotation (game identity = Unity yaw 180).
            GameObject room;
            if (customBar != null) { room = Instantiate(customBar.template, Vector3.zero, Quaternion.identity, barRoot); room.name = "Room (" + customBar.def.key + ")"; }
            else room = Spawn(StationTables.BarRoom[BarRace], Vector3.zero, OrbitLayout.RotationToUnity(Vector3.zero), barRoot, "Room");
            BarRoom = room != null ? room.transform : null;
            // As in the hangar: the loops (and the Midorian prop's replays) skip their one-off first key, where every part
            // sits at the origin for 33 / 50 ms; played, it flashed for a frame on every wrap (the Nivelian bar's 6.5 s
            // bar_nivelian_anim_add, the Midorian prop each time it replayed).
            if (room != null && BarRace == 3 && customBar == null)
            {
                // CutScene::initialize (mode 4): bar_midorian_alpha_anim is a one-shot, restarted with 30 % every 2 s.
                foreach (var a in room.GetComponentsInChildren<PartAnimation>(true))
                    if (a.gameObject.name.Contains("alpha_anim")) { midorianProp = a; a.loop = false; a.play = false; }
            }
            if (room != null && BarRace == 1 && customBar == null)
            {
                // Remake: the bartender arms (bar_vossk_bot_*, in the assembly): one-shots, VosskBarBots says when.
                foreach (var a in room.GetComponentsInChildren<PartAnimation>(true))
                {
                    string n = a.gameObject.name;
                    if (!n.StartsWith("bar_vossk_bot_")) continue;
                    // Modified: the remade bots (keyed in Blender with the AEM plugin) turn heading first, like the
                    // Terran hangar's (PartAnimation.headingFirst): X about the part's own axis, the file's Z outermost.
                    a.headingFirst = true;
                    a.loop = false;
                    a.play = false;
                    if (n.StartsWith("bar_vossk_bot_a")) vosskBotA = a;
                    else if (n.StartsWith("bar_vossk_bot_b")) vosskBotB = a;
                    else if (n.StartsWith("bar_vossk_bot_c")) vosskBotC = a;
                }
                if (vosskBotA != null && vosskBotB != null && vosskBotC != null)
                {
                    vosskBots = new VosskBarBots(() => Random.value);
                    vosskBotC.gameObject.SetActive(false);   // the end arm's other animation: one arm shows at a time
                }
            }

            // One visitor per agent (Generator::createAgents, kept with the stock): slot nextInt(7) re-rolled until free,
            // the mesh by the agent's race (Level::createScene 0xc2b3e: a Midorian with a Nivelian face uses the Nivelian
            // mesh, female Terrans their own).
            var agents = Stock != null ? Stock.agents : new List<Agent>();
            slotTaken = new bool[VisitorSlotCount];
            VisitorCount = Mathf.Min(agents.Count, VisitorSlotCount);
            for (int i = 0; i < VisitorCount; i++) SpawnVisitor(agents[i]);
            // Remake multiplayer: the event graphs' bar missions offered here (EventMissions) join as they arrive.
            GoF2Remake.World.SpaceHooks.RequestEventOffers?.Invoke(Station != null ? Station.index : Session.StationIndex, AddVisitor);

            barPosA = OrbitLayout.ToUnity(StationTables.BarCameraStart[BarRace]);
            barPosB = OrbitLayout.ToUnity(StationTables.BarCameraRest[BarRace]);
            barRotA = CameraRotation(0f, StationTables.BarCameraStartYaw[BarRace], 0f);
            barRotB = CameraRotation(0f, StationTables.BarCameraRestYaw[BarRace], 0f);
            if (customBar != null)
            {
                // A custom bar's markers: the resting view, and the first visit's glide from "camera_start" (else no glide).
                barPosB = customBar.camera;
                barRotB = Quaternion.LookRotation(customBar.cameraTarget - customBar.camera, Vector3.up);
                barPosA = customBar.hasStart ? customBar.cameraStart : barPosB;
                barRotA = customBar.hasStart ? Quaternion.LookRotation(customBar.cameraStartTarget - customBar.cameraStart, Vector3.up) : barRotB;
            }
            swayYaw.Start(0f, 5f);
        }
        /// <summary>Remake: one more visitor on a free slot after the bar was built (false: every slot taken). The lounge's
        /// plates and list follow (LoungePanel rebuilds when the count changes).</summary>
        public bool AddVisitor(Agent agent)
        {
            if (agent == null || slotTaken == null || barRoot == null || System.Array.IndexOf(slotTaken, false) < 0) return false;
            if (visitors.Exists(v => v.agent == agent)) return true;
            return SpawnVisitor(agent);
        }
        /// <summary>A visitor billboard for 'agent' on a random free slot (Generator::createAgents' slot nextInt(7) re-rolled
        /// until free) with its glow and floor shadow.</summary>
        bool SpawnVisitor(Agent agent)
        {
            if (System.Array.IndexOf(slotTaken, false) < 0) return false;
            int slot;
            do slot = Random.Range(0, slotTaken.Length); while (slotTaken[slot]);
            slotTaken[slot] = true;
            int i = visitors.Count;
            {
                int race = agent.race == 3 && agent.portrait != null && agent.portrait[0] == 2 ? 2 : agent.race;
                var prefab = VisitorPrefab(StationTables.VisitorFor(race, !agent.male));
                if (prefab == null) return false;
                var feet = SlotFeet(slot);
                var v = new Visitor { feet = feet, agent = agent };
                v.body = Instantiate(prefab, feet, Quaternion.identity, barRoot).transform;
                v.body.name = $"Visitor {i} ({prefab.name})";
                // Modified: the sizes from the meshes (VisitorMeshBounds), not Renderer.bounds.
                float top = VisitorMeshBounds(v.body, Matrix4x4.identity, out var wb) ? Mathf.Max(wb.max.y, feet.y) : feet.y;
                v.height = Mathf.Max((top - feet.y) * 0.55f, 60f * M);   // the billboard quad is about twice the figure
                // The drawn centre in the figure's own space (not the feet point).
                v.bodyCentre = VisitorMeshBounds(v.body, v.body.worldToLocalMatrix, out var bb) ? bb.center : Vector3.zero;
                if (visitorGlow != null)
                {
                    v.glow = Instantiate(visitorGlow, feet, Quaternion.identity, barRoot).transform;
                    v.glow.name = $"Visitor {i} glow";
                    var mat = glowMaterials != null && BarRace < glowMaterials.Length ? glowMaterials[BarRace] : null;
                    if (mat != null) foreach (var r in v.glow.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
                    v.glowCentre = VisitorMeshBounds(v.glow, v.glow.worldToLocalMatrix, out var gb) ? gb.center : Vector3.zero;
                }
                if (visitorShadow != null)
                {
                    v.shadow = Instantiate(visitorShadow, feet + new Vector3(0f, 20f * M, 0f), OrbitLayout.RotationToUnity(Vector3.zero), barRoot).transform;
                    v.shadow.name = $"Visitor {i} shadow";
                }
                visitors.Add(v);
            }
            return true;
        }
        /// <summary>Modified: the box of the meshes under 'root' in the space 'toSpace' maps the world into (false: no mesh),
        /// from the meshes' own bounds. Renderer.bounds is empty on an inactive renderer, and a multiplayer event's visitor
        /// (AddVisitor, by RPC) can arrive while the bar is hidden (barRoot inactive): its glow and shadow sat at the wrong
        /// place, its renderers weren't even found (GetComponentsInChildren skipped them).</summary>
        static bool VisitorMeshBounds(Transform root, Matrix4x4 toSpace, out Bounds b)
        {
            bool any = false;
            b = new Bounds();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.gameObject.activeSelf || !mf.TryGetComponent<Renderer>(out var r) || !r.enabled) continue;
                var lb = mf.sharedMesh.bounds;
                var m = toSpace * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = m.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return any;
        }
        GameObject VisitorPrefab(StationTables.Visitor v) => v switch
        {
            StationTables.Visitor.TerranFemale => visitorTerranFemale != null ? visitorTerranFemale : visitorTerranMale,
            StationTables.Visitor.Vossk => visitorVossk,
            StationTables.Visitor.Nivelian => visitorNivelian,
            StationTables.Visitor.Multipod => visitorMultipod,
            StationTables.Visitor.Bobolan => visitorBobolan,
            StationTables.Visitor.Grey => visitorGrey,
            _ => visitorTerranMale,
        };
        GameObject Spawn(string assembly, Vector3 unityPos, Quaternion rot, Transform parent, string label)
        {
            var prefab = AssembledObject.LoadPrefab(db.AssemblyByName(assembly));
            if (prefab == null) { Debug.LogWarning($"StationLevel: no assembled prefab '{assembly}'"); return null; }
            var go = Instantiate(prefab, unityPos, rot, parent);
            go.name = label;
            GoF2Remake.Visuals.AutoLods.FullDetail(go);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);   // close-up room: full detail
            return go;
        }
        /// <summary>createShip(race, 0, idx, null, false): NPC mesh group, setExhaustVisible(false), asleep.</summary>
        GameObject SpawnShip(int index, Vector3 gamePos, float gameYaw, Transform parent, string label, AssemblyData assembly = null)
        {
            var entry = assembly ?? PlayerHull.Assembly(db, index);   // the player's own ship: remake debug, the Ships tab's pick
            if (entry == null) return null;
            var go = Spawn(entry.name, OrbitLayout.ToUnity(gamePos), OrbitLayout.RotationToUnity(new Vector3(0f, gameYaw, 0f)), parent, label);
            var asm = go != null ? go.GetComponent<AssembledObject>() : null;
            if (asm != null)
            {
                asm.SetPlayerVariant(false);
                if (asm.npcVariantParts != null) foreach (var p in asm.npcVariantParts) if (p != null) p.SetActive(false);   // exhaust off
                // Remake mods: the exhaust style of the player's build, or of a guest's (ModExhausts; before the particles,
                // which it replaces).
                var exhaustBuild = label == "Player ship" ? (PlayerHull.OwnEngines(db) ? Modding.ModShipKits.PlayerBuild(index) : null)
                                 : traffic != null && traffic.SpawningGuest >= 0 && NetSession.Active
                                   ? SpaceHooks.GuestBuild?.Invoke((ulong)traffic.SpawningGuest, index) : null;
                if (exhaustBuild != null) Modding.ModExhausts.Apply(db, index, go, exhaustBuild);
                // Remake: the hangar flights and bar flybys fly on the player's engine (its glow mesh and exhaust particles)
                // instead of the NPC flame meshes, like the NPCs in space (Settings.NpcPlayerEngines; the player's own ship
                // always): some hulls' _engine_add parts showed nothing, and the VoidX's was blue where the player's is purple.
                // The particles follow the glow, which HangarFlight / BarFlybys switch on (HangarFlight.UsesPlayerEngine).
                if (asm.playerVariantParts != null && asm.playerVariantParts.Length > 0 && asm.playerVariantParts[0] != null
                    && (Settings.NpcPlayerEngines || !asm.HasNpcExhaust || label == "Player ship"))
                {
                    var glow = asm.playerVariantParts[0];
                    // A customizable ship's exhausts where its build has them (a guest's build, the player's).
                    var build = Modding.ModShipKits.BuildOfAssembly(entry.name);
                    GoF2Remake.Flight.ShipExhaust.AttachRemote(go, db, go.transform, index, () => glow != null && glow.activeInHierarchy,
                                                               () => 0f, () => 0f, scaled: true,
                                                               mounts: build != null ? Modding.ModShipKits.MountsShown(db, index, 3, build) : null);
                }
            }
            // Remake mods: a ship's "engine" materials (its nozzles) glow only while its engine glow shows: dark parked, lit in a
            // hangar flight (EngineMaterialGlow).
            if (go != null && go.GetComponent<Modding.EngineMaterialGlow>() is Modding.EngineMaterialGlow nozzles) nozzles.followEngine = true;
            // Remake: a soft shadow on the floor under every hangar ship (not the bar's flybys); it shows once the ship rests
            // (the "Hangar ship shadows" option: off, the player's own ship only, every ship).
            if (go != null && hangarRoot != null && parent == hangarRoot && !orbitDocked) HangarShipShadow.Attach(go, entry.name, true, label == "Player ship");   // at once; a landing flight hides it (ExpectLanding)
            return go;
        }
        GameObject SpawnShip(int index, Vector3 unityPos, Quaternion unityRot, Transform parent, string label, AssemblyData assembly = null)
        {
            var go = SpawnShip(index, Vector3.zero, 0f, parent, label, assembly);
            // (docked inside the orbit the hangar's places are room metres in hangarRoot's frame, on the station's hull)
            if (go != null && orbitDocked && parent != null && parent == hangarRoot) go.transform.SetLocalPositionAndRotation(unityPos, unityRot);
            else if (go != null) go.transform.SetPositionAndRotation(unityPos, unityRot);
            return go;
        }
        /// <summary>Remake: the rooms' thin, glossy parts (the Nivelian bar stools, the Midorian window frames) shimmered as the
        /// camera swayed: SMAA works within one frame. The station camera takes URP's temporal AA instead (its slow camera
        /// and still rooms are where TAA has nothing to smear), unless a temporal upscaler (DLSS, FSR 2+, STP) already
        /// anti-aliases or MSAA is on (URP's TAA needs it off); then SMAA as before. Again when the options change.</summary>
        /// <summary>Remake mods: a customizable ship's parts arrived (loaded on demand, ModShips.EnsureBuild): the turntable shows
        /// the player's build. Also a mod ship whose model wasn't built yet when the hangar opened (a save loaded while the mods
        /// still loaded, a player's report: the Phantom stood in until the next docking): its own model now.</summary>
        void OnModShipModels()
        {
            if (this == null || playerShip == null || PlayerFlying) return;
            // The stand-in is another prefab (AssembledObject.ModScope = the prefab's name); the Mod Builder rebuilds its ship itself.
            var want = PlayerHull.Assembly(db, Session.ShipIndex);
            var shown = playerShip.GetComponent<AssembledObject>();
            bool standIn = !Modding.ModManager.BuilderOpen && want != null && want.pack == Modding.ModShips.Pack
                           && shown != null && shown.ModScope != want.name;
            if (!standIn && Modding.ModShipKits.CustomizationOf(Session.ShipIndex) == null) return;
            ReplacePlayerShip(Session.ShipIndex);
        }
    }
}
