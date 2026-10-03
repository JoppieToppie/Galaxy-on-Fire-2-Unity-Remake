// Traffic.cs
// The orbit's NPC ships (Reference/research/npc_traffic_ai.md 2, 4, 7, 8), created by SpaceLevel:
//   Level::createMission / createShip   spawns TrafficPlan's ships (NpcShip)
//   Level::connectPlayers 0xcc330       each NPC's enemy list: the player first, then every ship of another race (same-race
//                                       ships never target or hit each other)
//   Level::updateOrbit 0xd50b0          every 10 s one gone jumper relaunches from the station; every 45 s gone local
//                                       fighters relaunch from the station and a destroyed raider group comes back 60-120 km
//                                       from the player (at most 2 waves; security 0, or 3 ships in security 1)
//   Level::friendTurnedEnemy / alarmAllFriends   radio 0 "Hold your fire!" (426-428) once, radio 1 (429-431) once and
//                                       every ship of that race turns hostile; the station remembers it (next visit: at
//                                       least 7 local fighters, all hostile at once, radio 2 "He's back!" 445-447)
//   Level::enemyDied / Standing::applyKill   a player kill: kills +1 (hostile ships), pirate kills, standing -5 with the
//                                       race (a pirate: +1 toward the system race); not in the black-market system
//   Radar::draw hostile counter         hostile living ships (only with a scanner): blocks fast-forward and picks the music:
//                                       0 the system's space track, 1-2 / 3-4 / 5+ Space_Battle_Low / Medium / Full
// Radio messages are shown as HUD messages (the original's radio window with portraits isn't built yet).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class Traffic : MonoBehaviour
    {
        const float M = 0.05f;

        public readonly List<NpcShip> Ships = new List<NpcShip>();
        public Target Player { get; private set; }
        public int SystemRace { get; private set; }
        public int AttackRace { get; private set; }
        public int Security { get; private set; }
        public int StationIndex { get; private set; }
        public Vector3 StationPosition { get; private set; }
        /// <summary>Radar+0x1b8: hostile living ships (0 without a scanner).</summary>
        public int HostileCount { get; private set; }
        public event Action<string> Message;

        Database db;
        bool hasScanner, radioTurned, radioAlarm, blackMarket;
        float respawnMs, jumperMs;
        int raiderWaves, raidersRespawned;
        AudioSource music;
        int musicCategory = -1, pendingCategory = -1;
        float fade = 1f;
        CombatAssets assets;

        /// <summary>'storyOrbit': the orbit is built around a campaign mission (Level::init calls createCampaignMission
        /// instead of createMission), so no normal traffic; the campaign level spawns its ships with SpawnShip.</summary>
        public void Setup(Database database, OrbitLayout layout, Target player, GameObject station, bool storyOrbit = false, Wormhole wormhole = null)
        {
            db = database;
            Wormhole = wormhole;
            // Level::update: updateAlienAttackers in the alien orbit and at the station the Void attack; their music.
            VoidAttack = layout.alienOrbit || (!Session.FreePlay && Session.CampaignMission < 45 && layout.stationIndex == Session.VoidInvasionStation);
            Player = player;
            assets = CombatAssets.Load();
            StationIndex = layout.stationIndex;
            var sys = db.Systems.Find(s => s.index == layout.systemIndex);
            SystemRace = Mathf.Clamp(sys?.raceId ?? 0, 0, 3);
            AttackRace = Standing.EnemyRaceOf(SystemRace);
            Security = sys?.securityLevel ?? 0;
            blackMarket = layout.systemIndex == 25;
            hasScanner = Shop.FirstMounted(db, 17) != null;
            // MGame::OnInitialize: the elite medal counters that only count within one level.
            if (hasScanner) Session.BlindKills = 0;
            Session.EmpDisabledNow = Session.RocketAsteroids = 0;
            if (station != null) StationPosition = station.transform.position;

            fxRoot = new GameObject("NPC weapon fx").transform;
            fxRoot.SetParent(transform, false);
            IsStoryOrbit = storyOrbit;
            if (!storyOrbit)
                foreach (var spec in TrafficPlan.Build(db, StationIndex, sys, player != null ? new Vector3(player.transform.position.x, player.transform.position.y, -player.transform.position.z) / 0.05f : Vector3.zero,
                                                       wormhole != null && wormhole.Visible ? wormhole.GamePosition : (Vector3?)null)) Create(spec);
            AddCampaignStatics(layout);
            ConnectPlayers();
            wantedShip = Ships.Find(s => s.Spec.wantedIndex >= 0);
            convoyFreighter = Ships.Find(s => s.Spec.convoyRole == SpawnSpec.ConvoyFreighter);
            if (convoyFreighter != null)
            {
                ConvoyRoute = new Route(false);
                ConvoyRoute.points.Add(convoyFreighter.Spec.position);
            }
            Debug.Log($"Traffic: {Ships.Count} ships ({CountGroup(NpcGroup.Local)} local, {CountGroup(NpcGroup.Jumper)} jumpers, " +
                      $"{CountGroup(NpcGroup.Freighter)} freighters, {CountGroup(NpcGroup.Raider)} raiders)");

            music = gameObject.AddComponent<AudioSource>();
            music.loop = true;
            music.playOnAwake = false;
            music.spatialBlend = 0f;

            // Level::initParticleSystems: the red static fog around a pirate base's outpost.
            foreach (var s in Ships) if (s.Spec.group == NpcGroup.Outpost) SpawnRedFog(s.transform.position);

            SetupLoma();
            SetupVoidChatter();
            // Level+0x18a: back at a station whose race the player attacked.
            if (Session.AttackedStations.Contains(StationIndex))
            {
                AlarmAllFriends(SystemRace, false);
                Radio(445, 447, SystemRace);
            }
        }

        int CountGroup(NpcGroup g) => Ships.FindAll(s => s.Spec.group == g).Count;

        Transform fxRoot;
        public bool IsStoryOrbit { get; private set; }
        /// <summary>The orbit's wormhole (landmark 3), null = none.</summary>
        public Wormhole Wormhole { get; private set; }
        /// <summary>The alien orbit or a station under Void attack: dead Void ships come back, Void music.</summary>
        public bool VoidAttack { get; private set; }
        float alienMs;

        /// <summary>Multiplayer (NetOrbit): a ship another player ran in this orbit, taken over where it is (spec.position)
        /// with its heading and hull.</summary>
        public NpcShip Adopt(SpawnSpec spec, Quaternion rotation, float hullFraction)
        {
            var ship = Create(spec);
            ship.transform.rotation = rotation;
            var hp = ship.Hp;
            if (hp != null && hp.maxHull > 0)
            {
                hp.hull = Mathf.Clamp(Mathf.RoundToInt(hp.maxHull * hullFraction), 1, hp.maxHull);
                ship.Target.hp = hp.hull;
            }
            return ship;
        }

        NpcShip Create(SpawnSpec spec)
        {
            var entry = spec.turretAssembly != null ? db.AssemblyByName(spec.turretAssembly)
                      : spec.ship == 14 ? db.AssemblyByName("battleship_terran")
                      : spec.fixedObject != null ? db.AssemblyByName(spec.fixedObject)
                      : spec.freighter ? db.AssemblyByName(NpcTables.FreighterAssembly(spec.race))
                                       : ShipAssembly(spec.ship, spec.race);
            var prefab = AssembledObject.LoadPrefab(entry);
            var go = new GameObject($"NPC {spec.group} {spec.race}/{spec.ship}");
            go.transform.SetParent(transform, false);
            var ship = go.AddComponent<NpcShip>();
            Ships.Add(ship);
            ship.ModelPath = AssembledObject.PrefabPath(entry);   // multiplayer: the clients' proxies load the same prefab
            ship.Setup(this, db, spec, prefab, fxRoot);
            if (spec.turretAssembly == null && spec.fixedObject == null && !spec.freighter && (spec.ship == 45 || spec.ship == 51))
                AttachFighterTurret(ship);
            return ship;
        }

        /// <summary>Level::createFighterTurrets 0xcb200: ship 45 (Qyrr Myfft's) and 51 (Hans') carry an invulnerable turret
        /// object 0x1a74 with the host's max hull at the ship-relative (0, 172.25, -460.7) / (0, 470, -83), race 8 / 0
        /// (PlayerTurret::setHost); Level::assignGuns gives it item 22 at x0.5. It follows its host (NpcShip.SyncTurret).</summary>
        void AttachFighterTurret(NpcShip host)
        {
            bool myfft = host.Spec.ship == 45;
            var spec = new SpawnSpec
            {
                group = NpcGroup.Turret, race = myfft ? Standing.Pirate : 0, ship = -1, turretAssembly = "turret_002_static",
                hitpoints = Mathf.Max(1, host.Hp.maxHull), noLoot = true, nameText = 1666, stationary = true,
                alwaysEnemy = host.Spec.alwaysEnemy, alwaysFriend = host.Spec.alwaysFriend, position = host.Spec.position,
            };
            var t = Create(spec);
            var off = myfft ? new Vector3(0f, 172.25f, -460.7f) : new Vector3(0f, 470f, -83f);
            t.transform.SetParent(host.transform, false);
            t.transform.localPosition = new Vector3(-off.x, off.y, off.z) * M;   // ship-relative offsets: (-x, y, z) x 0.05
            t.transform.localRotation = Quaternion.identity;
            t.Target.invulnerable = true;
            // Remake: no hit cube of its own (the +-1000 turret cube would soak up the shots at its host) and no lock.
            t.Target.radius = 0f;
            t.Target.untargetable = true;
            t.SetGun(22, 0.5f);
            host.AttachedTurret = t;
        }

        /// <summary>Level::createWingmen 0xcb338: the hired wingmen next to the player (after every other ship). The model
        /// comes from a generator seeded with 5 x the name's length, so each wingman flies the same ship everywhere; 600
        /// hull at least; unarmed in a Challenge. Not at campaign 158.</summary>
        public void SpawnWingmen(Transform player, bool unarmed)
        {
            var names = Session.Wingmen;
            if (names == null || names.Count == 0 || player == null || Session.CampaignMission == 158) return;
            // Level::createWingmen: not in the supernova system (27) either, before campaign 0x9e.
            if (!Session.FreePlay && Session.CampaignMission < 0x9e && (db.Stations.Find(s => s.index == StationIndex)?.system ?? -1) == 27) return;
            int race = Session.WingmanRace;
            float[] right = { -1000f, 2000f, 0f };
            for (int i = 0; i < Mathf.Min(3, names.Count); i++)
            {
                var state = UnityEngine.Random.state;
                UnityEngine.Random.InitState(5 * names[i].Length);
                int ship = NpcTables.RandomFighter(race <= 3 ? race : Standing.Pirate);   // races 4-7: the pirate pool
                UnityEngine.Random.state = state;
                var pos = player.position + (player.right * right[i] - player.forward * 2000f + Vector3.up * (i == 2 ? 1000f : 0f)) * 0.05f;
                var spec = new SpawnSpec
                {
                    group = NpcGroup.Wingman, race = race, ship = ship, alwaysFriend = true, noLoot = true, name = names[i],
                    position = new Vector3(pos.x, pos.y, -pos.z) / 0.05f, hitpoints = Mathf.Max(600, NpcTables.Hull(0, ship)),
                };
                var s = Create(spec);
                s.transform.rotation = player.rotation;
                s.MakeWingman(i, !unarmed);
            }
            ConnectPlayers();
        }

        public List<NpcShip> LivingWingmen => Ships.FindAll(s => s.IsWingman && !s.Gone && s.Current == NpcShip.State.Fly);

        /// <summary>The wingman menu's command for every living wingman (MGame::OnTouchEnd, menu 2).</summary>
        public void CommandWingmen(int command, Target locked, Route playerRoute)
        {
            if (command == 0) Session.WingmanShowEmp = !Session.WingmanShowEmp;   // Status+0xf8
            foreach (var w in LivingWingmen) w.WingmanCommand(command, locked, playerRoute);
        }

        /// <summary>Level::createShip for a campaign level: one ship; call ConnectPlayers once all are spawned.</summary>
        public NpcShip SpawnShip(SpawnSpec spec) => Create(spec);

        /// <summary>Multiplayer: this player now runs the orbit (took it over): its relaunches and raider waves run
        /// (Traffic.Setup had it passive), unless the level itself keeps it passive (a story, mission or siege orbit).</summary>
        public void SetPassive(bool passive) => IsStoryOrbit = passive;

        /// <summary>Level::connectPlayers: each ship's enemy list is the player first, then every ship of another race.
        /// 'playerExempt': ships of these races leave the player out (campaign 16 / 24 / 28: the Void attack the others).</summary>
        public void ConnectPlayers(int playerExemptRace = -99)
        {
            // Level::connectPlayers 0xcc330: in free flight (Mission::isEmpty) a Specter's only enemy is the player (setEnemy).
            bool freeFlight = !NpcTables.InCampaignLevel && NpcTables.LevelFreelanceType < 0;
            foreach (var s in Ships)
            {
                s.enemies.Clear();
                if (Player != null && s.Race != playerExemptRace) s.enemies.Add(Player);
                if (freeFlight && s.Race == Standing.Specter) continue;
                foreach (var o in Ships) if (o != s && o.Race != s.Race) s.enemies.Add(o.Target);
            }
        }

        AssemblyData ShipAssembly(int ship, int race)
        {
            string raceName = race switch { 0 => "terran", 1 => "vossk", 2 => "nivelian", 3 => "midorian", 9 => "void", _ => "pirate" };
            return db.ShipAssembly(ship, raceName);
        }

        // ---- generic radio chatter (Level::createRadioMessage(kind, race) 0xd5568; combat_equipment.md 6) ------------

        /// <summary>One generic radio line: a random face of the race (ImageFactory::createChar) or the Pirate Boss (speaker 9).</summary>
        public class Chatter
        {
            public string text, speaker;
            public int[] portrait;      // a generic face, or null with speakerId
            public int speakerId = -1;  // a story speaker (9 Pirate Boss)
            public string voice;        // Globals::getDialogueSoundId (the story's radio calls)
        }

        /// <summary>Status::getMission (Status+400) is not empty: the orbit is a story level's or the freelance mission's
        /// target (set by Status::departStation, cleared when the mission ends). Level::createRadioMessage returns at once
        /// then, so no generic line plays in a mission orbit (the level's own radio is separate). Null = never.</summary>
        public Func<bool> LevelMissionActive;

        /// <summary>A story cutscene runs (SpaceLevel.Cutscene): the original draws no radar then, so no music switch.</summary>
        public Func<bool> RadarHidden;

        bool RadioBlocked => LevelMissionActive != null && LevelMissionActive();

        /// <summary>A story speaker's line in the radio box (Level::createRadioMessage 0x13 / 0x1b), with its voice.</summary>
        public void QueueLine(int text, int speakerId, string voice = null)
        {
            if (RadioBlocked) return;
            chatterQueue.Enqueue(new Chatter { text = Localization.Get(text), speaker = StoryTable.SpeakerName(speakerId), speakerId = speakerId, voice = voice });
        }

        readonly Queue<Chatter> chatterQueue = new Queue<Chatter>();
        Chatter chatter;
        float chatterMs, chatterDurationMs;

        /// <summary>The generic line on screen (after the 2000 ms delay), null = none.</summary>
        public Chatter ChatterVisible => chatter != null && chatterMs >= 2000f ? chatter : null;

        /// <summary>Speaker image by race: 0 -> 64, 2 -> 65, 3 -> 21, 8 -> 9 (Pirate Boss), else 63; the name 1597 + image. The
        /// voice: Globals::getDialogueSoundId(text, Agent(race, male)) (GenericVoice).</summary>
        /// <summary>Level::createRadioMessage replaces the level's radio list (Radio::setMessages): a new line cuts the old.
        /// ImageFactory::createChar is asked for a male face every time.</summary>
        void Radio(int firstText, int lastText, int race)
        {
            if (RadioBlocked) return;
            chatterQueue.Clear();
            chatter = null;
            int image = race == 0 ? 64 : race == 2 ? 65 : race == 3 ? 21 : race == 8 ? 9 : 63;
            int text = UnityEngine.Random.Range(firstText, lastText + 1);
            const bool male = true;
            var c = new Chatter { text = Localization.Get(text), speaker = Localization.Get(1597 + image) };
            if (image == 9) c.speakerId = 9;
            else c.portrait = AgentGenerator.CreatePortrait(male, race == 0 || race == 2 || race == 3 ? race : 1);
            c.voice = GenericVoice.For(text, race, male, c.portrait);
            chatterQueue.Enqueue(c);
        }

        /// <summary>Remake: the chatter stays up at least until its voice clip is over (as Radio.HoldFor).</summary>
        public void HoldChatter(float ms) { if (chatter != null) chatterDurationMs = Mathf.Max(chatterDurationMs, ms); }

        /// <summary>Radio::update: hidden 2000 ms, then lines * 2000 + 1500 ms (world time).</summary>
        void UpdateChatter(float dtMs)
        {
            if (chatter != null)
            {
                chatterMs += dtMs;
                if (chatterMs >= 2000f + chatterDurationMs) chatter = null;
                return;
            }
            if (chatterQueue.Count == 0) return;
            chatter = chatterQueue.Dequeue();
            chatterMs = 0f;
            chatterDurationMs = Mathf.Max(1, Mathf.CeilToInt(chatter.text.Length / 55f)) * 2000f + 1500f;
        }

        bool baseWakeRadio, baseDestroyedRadio;
        int emergencyKills;

        /// <summary>A HUD message (Hud::hudEvent), e.g. "Signature invalid".</summary>
        public void Warn(string text) => Message?.Invoke(text);

        /// <summary>Level::pirateStationAction 0xd6338: a guard woke (radio 435-437, once per level) or the outpost died
        /// (the base destroyed, the reward pending, radio 438-440, once).</summary>
        public void PirateStationAction(bool guardWoke)
        {
            if (guardWoke)
            {
                if (baseWakeRadio) return;
                baseWakeRadio = true;
                Radio(435, 437, Standing.Pirate);
                return;
            }
            if (baseDestroyedRadio || PirateBases.IndexOf(StationIndex) < 0) return;
            baseDestroyedRadio = true;
            PirateBases.Destroyed(StationIndex);
            Radio(438, 440, Standing.Pirate);
        }

        // ---- remake: pirate events (TrafficPlan.AddPirateEvent, GitHub #6) ----------------------------------------------

        bool bossCalled;
        float eventMs;

        /// <summary>The event's outpost or boss destroyed (whoever did it): the pirates' "Nooooo!" for an outpost, and the bounty
        /// (outpost 10 000 + 1500 per rank, boss 4000 + 1500 per rank).</summary>
        public void PirateEventDone(NpcShip ship)
        {
            int rank = Mathf.Min(Session.Rank, 20);
            bool outpost = ship.Spec.pirateEvent == SpawnSpec.EventOutpost;
            if (outpost) Radio(438, 440, Standing.Pirate);
            int reward = (outpost ? 10000 : 4000) + 1500 * rank;
            Session.Credits += reward;
            BountyCollected?.Invoke(reward);
        }

        /// <summary>The boss calls the player 8 s into the orbit (the Pirate Boss face, speaker 9).</summary>
        void UpdatePirateEvent(float dtMs)
        {
            if (bossCalled || RadioBlocked) return;
            eventMs += dtMs;
            if (eventMs < 8000f) return;
            var boss = Ships.Find(s => s.Spec.pirateEvent == SpawnSpec.EventBoss);
            if (boss == null || !boss.Target.Alive) { bossCalled = true; return; }
            bossCalled = true;
            chatterQueue.Clear();
            chatter = null;
            chatterQueue.Enqueue(new Chatter
            {
                text = Localization.Extra("pirateBossCall", "This orbit is mine now, pilot. Drop your cargo and turn around, or my boys turn you into scrap!"),
                speakerId = 9, speaker = Localization.Get(1606),
            });
        }

        // ---- Alice in the Void (MGame::OnInitialize -> Level::createRadioMessage(8), tables 0x2541c8 / 0x2543a0) ---------

        /// <summary>Per conversation the number of lines (DAT_002543a0) and the (speaker image, text) pairs (DAT_002541c8):
        /// 0x1a Alice, 0 Keith, 0x1f Cornelius Tenner.</summary>
        static readonly int[] VoidTalkCounts = { 2, 2, 2, 4, 3, 3, 2, 3 };
        static readonly (int image, int text)[] VoidTalk =
        {
            (0x1a, 2453), (0, 2454), (0x1a, 2455), (0, 2456), (0x1a, 2457), (0, 2458), (0x1a, 2459), (0, 2460), (0x1a, 2461),
            (0, 2462), (0x1a, 2463), (0x1f, 2464), (0x1a, 2465), (0, 2466), (0x1a, 2467), (0, 2468), (0x1a, 2469), (0, 2470),
            (0x1f, 2471), (0x1a, 2472), (0, 2473),
        };

        /// <summary>After the Valkyrie add-on (Status::dlc1Won), in the alien orbit before campaign 0x93: one of eight short
        /// talks between Alice and Keith (the first line after 5000 ms, the rest chained), voiced.</summary>
        void SetupVoidChatter()
        {
            if (Session.FreePlay || StationIndex != Session.VoidOrbit || !Story.Dlc1Won || Session.CampaignMission >= 0x93) return;
            int c = UnityEngine.Random.Range(0, 8), start = 0;
            for (int i = 0; i < c; i++) start += VoidTalkCounts[i];
            for (int i = 0; i < VoidTalkCounts[c] && start + i < VoidTalk.Length; i++)
            {
                var (image, text) = VoidTalk[start + i];
                QueueLine(text, image, GenericVoice.For(text));
            }
        }

        // ---- Loma's pirate toll (MGame::OnInitialize / OnUpdate / OnTouchEnd, Player::damage; Status+0x110 / +0x111) --------

        /// <summary>A ChoiceWindow in flight: (text, yes, no, on yes, on no); the game pauses meanwhile.</summary>
        public event Action<string, string, string, Action, Action> ChoiceRequested;
        /// <summary>The toll is paid: Loma's pirates are neither hostile nor friendly (PlayerFighter::update).</summary>
        public bool LomaTollPaid => blackMarket && Session.LomaTollPaid;
        /// <summary>The launch / arrival camera runs (SpaceLevel): the wingmen drift meanwhile.</summary>
        public Func<bool> LaunchCameraRunning;
        bool lomaAskPending;
        int lomaToll, lomaPercent;

        /// <summary>MGame::OnInitialize: outside system 25 both flags clear. In Loma the pirates hold fire (KIPlayer+0x21 = 0)
        /// unless the toll was refused; a new visitor hears radio 9 (449 / 450) and is asked afterwards, one who refused
        /// radio 0xd (455 / 456).</summary>
        void SetupLoma()
        {
            if (!blackMarket) { Session.LomaTollPaid = Session.LomaTollRefused = false; return; }
            if (Session.LomaTollRefused) { Radio(455, 456, Standing.Pirate); return; }
            SetPiratesArmed(false);
            if (Session.LomaTollPaid) return;
            Radio(449, 450, Standing.Pirate);
            lomaAskPending = true;
        }

        void SetPiratesArmed(bool on)
        {
            foreach (var s in Ships) if (s.Race == Standing.Pirate) s.shootingEnabled = on;
        }

        /// <summary>MGame::OnUpdate: once the arrival radio is over, the toll 448: the cargo's value (amount x single price,
        /// 100 when empty) x 2 / 5 / 10 / 20 % by difficulty (0 / 0.5 / 1.0 / Extreme); #P = the percentage.</summary>
        void UpdateLomaToll()
        {
            if (!lomaAskPending || chatter != null || chatterQueue.Count > 0 || Player == null || !Player.Alive) return;
            lomaAskPending = false;
            int value = 0;
            int priceStation = Session.PreviousStationIndex >= 0 ? Session.PreviousStationIndex : StationIndex;
            var items = Session.Cargo.ConvertAll(c => c.item);
            var prices = Shop.PriceList(db, priceStation, items);
            for (int i = 0; i < items.Count; i++) value += prices[i] * Session.Cargo[i].amount;
            if (value == 0) value = 100;
            float d = Session.Difficulty;
            float f = d <= 0f ? 0.02f : d == 0.5f ? 0.05f : d == 1f ? 0.1f : 0.2f;
            lomaPercent = Mathf.RoundToInt(f * 100f);
            lomaToll = (int)(f * value);
            string text = Localization.Get(448).Replace("#C", GoF2Remake.UI.ItemInfo.Credits(lomaToll)).Replace("#P", lomaPercent.ToString());
            ChoiceRequested?.Invoke(text, Localization.Get(134), Localization.Get(135), PayLomaToll, RefuseLomaToll);
        }

        /// <summary>Yes: with the credits, pay (radio 10: 451 / 452), the pirates stay quiet; without, 203 and they attack.</summary>
        void PayLomaToll()
        {
            if (Session.Credits < lomaToll)
            {
                ChoiceRequested?.Invoke(Localization.Get(203).Replace("#C", GoF2Remake.UI.ItemInfo.Credits(lomaToll - Session.Credits)), null, null, null, null);
                RefuseLomaToll();
                return;
            }
            Session.Credits -= lomaToll;
            Session.LomaTollPaid = true;
            SetPiratesArmed(false);
            Radio(451, 452, Standing.Pirate);
        }

        /// <summary>No (or too poor): the pirates arm, the refusal is remembered (+0x111), radio 0xb (453 / 454).</summary>
        void RefuseLomaToll()
        {
            SetPiratesArmed(true);
            Session.LomaTollRefused = true;
            Radio(453, 454, Standing.Pirate);
        }

        /// <summary>Player::damage on a pirate in Loma: Level::alarmAllFriends(8) (radio 0xc, 453), every pirate armed, the
        /// toll revoked.</summary>
        public void LomaPirateHit()
        {
            if (!blackMarket) return;
            lomaAskPending = false;
            AlarmAllFriends(Standing.Pirate, true);
            SetPiratesArmed(true);
            Session.LomaTollRefused = true;
            Session.LomaTollPaid = false;
        }

        /// <summary>SET_FOG_STATIC (space_props.md 4): 30 sprites of 32768 units within +-40000 of the outpost, colour
        /// 0xE2282880, forever, not tied to the camera. Level::initParticleSystems adds it to the fog manager (Level+0x7c):
        /// material 20095 fog.png, additive, like the ambient fog; no HDR glow (the original blends in LDR).</summary>
        void SpawnRedFog(Vector3 at)
        {
            var go = new GameObject("Pirate base fog");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 30;
            main.startLifetime = float.PositiveInfinity;
            main.startSpeed = 0f;
            main.startSize = 32768f * 0.05f;
            main.startColor = new Color(0xE2 / 255f, 0x28 / 255f, 0x28 / 255f, 0x80 / 255f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.maxParticleSize = 10f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (assets != null && assets.fogMaterial != null)
            {
                var fog = new Material(assets.fogMaterial) { name = "Pirate base fog" };
                if (fog.HasProperty("_Glow")) fog.SetFloat("_Glow", 1f);
                r.sharedMaterial = fog;
            }
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < 30; i++)
            {
                p.position = at + new Vector3(UnityEngine.Random.Range(-40000f, 40000f), UnityEngine.Random.Range(-40000f, 40000f), UnityEngine.Random.Range(-40000f, 40000f)) * 0.05f;
                p.rotation = UnityEngine.Random.Range(0f, 360f);
                ps.Emit(p, 1);
            }
        }

        /// <summary>The Terran battleship died: its turrets go with it.</summary>
        public void DestroyTurrets()
        {
            foreach (var s in Ships.ToArray()) s.DestroyAsTurret();
        }

        /// <summary>createRadioMessage kinds 0 / 1 say nothing at one of step 59's target stations (Status+0x90).</summary>
        bool AtStoryTarget => !Session.FreePlay && Session.StoryTargets != null && Session.StoryTargets.Contains(StationIndex);

        /// <summary>Level::friendTurnedEnemy: radio 0 once per level.</summary>
        public void FriendTurnedEnemy(int race)
        {
            if (radioTurned) return;
            radioTurned = true;
            if (!AtStoryTarget) Radio(426, 428, race);
        }

        /// <summary>Level::alarmAllFriends: every ship of 'race' turns hostile; radio 1 once; the station remembers it.</summary>
        public void AlarmAllFriends(int race, bool radio)
        {
            foreach (var s in Ships) if (s.Race == race) s.alwaysEnemy = true;
            if (radio && !radioAlarm)
            {
                radioAlarm = true;
                if (blackMarket) Radio(453, 453, race);   // kind 0xc in the black market
                else if (!AtStoryTarget) Radio(429, 431, race);
                if (race == SystemRace) Session.AttackedStations.Add(StationIndex);
            }
        }

        /// <summary>Level::enemyDied / friendDied bookkeeping and Standing::applyKill.</summary>
        /// <summary>Level::enemyDied / friendDied: a ship died, killed by the player or not (the campaign's kill counters).</summary>
        public event Action<NpcShip, bool> ShipDied;
        /// <summary>Level::stealFriendCargo (Level+0x13c): cargo was stolen from a friendly ship (Objective 0x13).</summary>
        public bool FriendCargoStolen { get; set; }

        public void OnShipDied(NpcShip ship, bool byPlayer)
        {
            ShipDied?.Invoke(ship, byPlayer);
            // Informer mission (PlayerFighter::update ~0xf1cb0): the spy dead -> Status+0xf0; the player shooting another
            // ship in its orbit -> Status+0xf1 (failed on the next docking).
            var fm = Session.FreelanceMission;
            if (fm != null && fm.type == MissionType.Informer && fm.target == StationIndex)
            {
                // PlayerFighter::update 0xf1c8c: any other ship dying first (whoever killed it) spoils it (+0xf1), and after
                // that the spy's death no longer counts.
                bool killed = Session.InformerKilled, failed = Session.InformerFailed;
                if (ship.Spec.nameText == 1663) { if (!Session.InformerFailed) Session.InformerKilled = true; }
                else if (!Session.InformerKilled) Session.InformerFailed = true;
                // Multiplayer: the squad's mission (NetMissions.OnStatus: 1 = the spy dead, 1000 = spoiled).
                if (Session.InformerKilled && !killed) GoF2Remake.Multiplayer.NetMissions.AddStatus(fm, 1);
                if (Session.InformerFailed && !failed) GoF2Remake.Multiplayer.NetMissions.AddStatus(fm, 1000);
            }
            // PlayerFighter::update's death: a Most Wanted criminal pays its bounty whoever killed it; no standing hit.
            if (ship.Spec.wantedIndex >= 0) { WantedKilled(ship); if (byPlayer && ship.Target.hostileToPlayer) CountKill(); return; }
            if (ship.Spec.pirateEvent != 0) PirateEventDone(ship);   // remake: the event's bounty, whoever killed it
            if (!byPlayer) return;
            // Player::damage: the convoy freighter ("Arms delivery") destroyed by the Liberator (0xb3) -> step 59's bonus.
            if (ship.Spec.convoyRole == SpawnSpec.ConvoyFreighter && ship.Target.lastPlayerWeapon == 179 && Session.StoryMission != null)
                Session.StoryMission.value++;
            // Player::damage: no standing change for a kill in the black market (Loma); the kill still counts
            // (Level::enemyDied's incKills, PlayerFighter::update's incPirateKills).
            if (!blackMarket) Standing.ApplyKill(ship.Race, SystemRace);
            if (ship.Target.hostileToPlayer)
            {
                if (PlayerHealth.EmergencyActive) Session.GraveRiserKills = Mathf.Max(Session.GraveRiserKills, ++emergencyKills);
                else emergencyKills = 0;
                CountKill();
                if (ship.Race == Standing.Pirate) Session.PirateKills++;
            }
        }

        /// <summary>Level::enemyDied: Status::incKills; without a scanner also Status+0x11c (medal 40 Blindfolded Killer).</summary>
        void CountKill()
        {
            Session.Kills++;
            if (!hasScanner && !Achievements.Has(40)) Achievements.Elite(40, ++Session.BlindKills);
        }

        bool musicHeld;

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            Navigation.SyncMusic(music, ref musicHeld);   // on through conversations, paused by the pause menu
            UpdateOrbit(dtMs);
            UpdateAlienAttackers(dtMs);
            UpdateChatter(dtMs);
            UpdateLomaToll();
            UpdateConvoy();
            UpdateWanted();
            UpdatePirateEvent(dtMs);
            int hostiles = 0;
            if (hasScanner)
                foreach (var s in Ships)
                    // Radar::draw 0x156xxx: only active ships (Player::isActive) count, so a ship held back by its level
                    // script doesn't start the battle music or block fast-forward before it shows up. A sleeping ship is
                    // inactive too (KIPlayer::setToSleep -> Player::setActive(false)): index 7's ambush, sleeping guards.
                    // Level::getEnemies holds the freighters (PlayerFixedObject) too: a hostile one counts (Intercept's convoy).
                    if (!s.Gone && !s.Inactive && !s.Asleep && s.Current != NpcShip.State.Dying && s.Current != NpcShip.State.Dead && s.Target.Alive && s.Target.hostileToPlayer)
                        hostiles++;
            // Multiplayer: the other players' ships here (NetProxy targets, shown only in this orbit) count too.
            if (hasScanner && GoF2Remake.Multiplayer.NetGame.Active)
                foreach (var t in Target.NetShips)
                    if (t != null && t.enabled && !t.untargetable && !t.isPlayer && t.Alive && t.hostileToPlayer) hostiles++;
            HostileCount = hostiles;
            if (!MenuBackdrop) UpdateMusic(Time.unscaledDeltaTime);   // the menu plays its own theme
        }

        /// <summary>The main menu's backdrop (CutScene(2)): the orbit's traffic without the level's music.</summary>
        [NonSerialized] public bool MenuBackdrop;

        // ---- Level::createStaticObjects 0xcadb0 (campaign_levels_c.md 4, space_props.md 3) ------------------------------

        /// <summary>The Supernova's orbit objects in every level: the Mining Plant at Coromesk (103) for campaign > 0x54 (not
        /// at 0x87, whose level has its own), and at Var Lupra (112) for 0x80..0x91 the plasma array at (-50000, 0, 50000)
        /// facing the sun in its build stage (stage 5 with its glow at 0x91). Friendly, unkillable, never moving.</summary>
        void AddCampaignStatics(OrbitLayout layout)
        {
            if (Session.FreePlay || layout.alienOrbit) return;
            int cm = Session.CampaignMission;
            if (layout.stationIndex == 103 && cm > 0x54 && cm != 0x87) Create(MiningPlant());
            if (layout.stationIndex == 112 && cm >= 0x80 && cm <= 0x91) Create(PlasmaArray(layout, cm));
        }

        /// <summary>Level::createStaticObject 0x4a88: the Mining Plant (3210), docking type 1 (the titanium goes in here);
        /// getBoundingVolume 2000.</summary>
        public static SpawnSpec MiningPlant() => new SpawnSpec
        {
            group = NpcGroup.Special, race = 3, ship = -1, position = Vector3.zero, fixedObject = "sn_station_mining_plant", stationary = true,
            alwaysFriend = true, hitpoints = 9999999, noLoot = true, nameText = 3210, dockingType = ObjectDocking.DropOff, spacePoints = 1, hitRadius = 8000f,
            collisionId = 2000,
        };

        /// <summary>The plasma array (3207) in the build stage of campaign 'cm' (stages 1-5: from 0x80, 0x83, 0x87, 0x8a, 0x8e);
        /// getBoundingVolume 2001 for every stage (Level::createStaticObject 0x493e + 3k).</summary>
        public static SpawnSpec PlasmaArray(OrbitLayout layout, int cm)
        {
            int stage = cm < 0x83 ? 1 : cm < 0x87 ? 2 : cm < 0x8a ? 3 : cm < 0x8e ? 4 : 5;
            var sun = layout.lightDirection;
            float yaw = Mathf.Atan2(sun.x, sun.z);
            return new SpawnSpec
            {
                group = NpcGroup.Special, race = 3, ship = -1, position = new Vector3(-50000, 0, 50000), rotation = new Vector3(0, yaw, 0),
                fixedObject = $"sn_plasma_array_midorian_stage_00{stage}", stationary = true, alwaysFriend = true, hitpoints = 9999999, noLoot = true,
                nameText = 3207, hitRadius = 8000f, collisionId = 2001,
            };
        }

        /// <summary>PlayerEgo::update's hacking won at a hidden-blueprint wreck: Status::unlockBluePrint, the wreck's flag and
        /// loot cleared, docking type 0, Level::createRadioMessage(k + 0x15) (3156 + k, BLUEPRINT_RECOVERED_k), found for good.</summary>
        public void OnHackWon(NpcShip ship)
        {
            int k = ship != null ? ship.Spec.hiddenBlueprint : -1;
            if (k < 0 || k >= TrafficPlan.HiddenBlueprints.Length || (Session.HiddenBlueprintsFound & (1 << k)) != 0) return;
            Session.HiddenBlueprintsFound |= 1 << k;
            Blueprints.Unlock(TrafficPlan.HiddenBlueprints[k].blueprint);
            ship.DockingType = 0;
            QueueLine(3156 + k, 0, $"BLUEPRINT_RECOVERED_{k}");
        }

        // ---- the Most Wanted criminal (wingmen_wanted.md 2.6-2.7) -----------------------------------------------------

        NpcShip wantedShip;
        bool wantedUncovered, wantedAttacked, wantedSurrendered;
        int damageAfterSurrender;
        /// <summary>The player's radar ship lock (CombatRadar.Locked), set by the level: locking the criminal uncovers it.</summary>
        public Func<Target> LockedTarget;
        /// <summary>A bounty was paid (the reward message: 3206 + credits, sound 36).</summary>
        public event Action<int> BountyCollected;
        /// <summary>An uncovered criminal is alive (Radar::draw: music 151).</summary>
        public bool WantedUncovered => wantedShip != null && wantedShip.Target.Alive && wantedShip.alwaysEnemy;

        WantedData WantedOf(NpcShip s) => s != null && s.Spec.wantedIndex >= 0 && s.Spec.wantedIndex < db.Wanted.Count ? db.Wanted[s.Spec.wantedIndex] : null;

        void UpdateWanted()
        {
            if (wantedShip == null || !wantedShip.Target.Alive) return;
            // PlayerFighter::update ~0xf0f00: the radar lock on it uncovers it (a scanner is needed to lock ships).
            if (!wantedUncovered && !wantedShip.alwaysEnemy && !wantedSurrendered && LockedTarget != null && LockedTarget() == wantedShip.Target)
                UncoverWanted();
            // Player::damage -> Level::almostKillWanted: the storyline criminals give up at a third of their hull.
            var w = WantedOf(wantedShip);
            if (w != null && WantedBoard.IsStoryline(w.index) && !wantedSurrendered && wantedShip.Hp.hull < wantedShip.Hp.maxHull / 3)
            {
                wantedSurrendered = true;
                wantedShip.alwaysEnemy = false;
                wantedShip.turnedEnemy = false;
                wantedShip.alwaysFriend = true;
                wantedShip.shootingEnabled = false;
                damageAfterSurrender = 0;
                WantedBoard.OnSurrender(db, w.index, StationIndex);
            }
        }

        /// <summary>Level::uncoverWanted 0xd63ac: it and its escort turn on the player; the uncover line (or the storyline talk).</summary>
        void UncoverWanted()
        {
            wantedUncovered = true;
            TurnWantedHostile();
            var w = WantedOf(wantedShip);
            if (w == null) return;
            if (w.index == 0) { WantedLine(3134, -1, w); WantedLine(3135, w.index, w); }
            else if (w.index == 1) { WantedLine(3139, -1, w); WantedLine(3140, w.index, w); }
            else WantedLine(3141 + UnityEngine.Random.Range(0, 5), -1, w);
        }

        /// <summary>Level::attackWanted 0xd642c: the player's first hit (instead of the friendly-fire rules).</summary>
        public void AttackWanted(NpcShip s, int dmg)
        {
            if (s != wantedShip) return;
            if (wantedSurrendered)
            {
                // A surrendered criminal fights again when the player keeps shooting (above 5 % of its hull).
                damageAfterSurrender += dmg;
                if (damageAfterSurrender > s.Hp.maxHull / 20) { s.alwaysFriend = false; s.alwaysEnemy = true; s.shootingEnabled = true; }
                return;
            }
            if (wantedAttacked) return;
            wantedAttacked = true;
            TurnWantedHostile();
            var w = WantedOf(s);
            if (w == null) return;
            if (w.index == 0) { WantedLine(3132, w.index, w); WantedLine(3133, -1, w); }
            else if (w.index == 1) { WantedLine(3136, w.index, w); WantedLine(3137, -1, w); WantedLine(3138, w.index, w); }
            else if ((w.index | 4) == 6) WantedLine(3146 + UnityEngine.Random.Range(0, 5), w.index, w);   // only wanted 2 and 6 taunt
        }

        void TurnWantedHostile()
        {
            wantedShip.alwaysEnemy = true;
            foreach (var s in Ships)
                if (s.Spec.wantedEscortOf == wantedShip.Spec.wantedIndex && s.Target.Alive) { s.alwaysEnemy = true; s.turnedEnemy = true; }
        }

        void WantedKilled(NpcShip ship)
        {
            var w = WantedOf(ship);
            if (w == null) return;
            int reward = WantedBoard.OnKilled(db, w.index);
            if (reward > 0) BountyCollected?.Invoke(reward);
            WantedLine(3151 + UnityEngine.Random.Range(0, 5), -1, w);   // Level::killWanted: "a job well done"
        }

        /// <summary>Level::createRadioMessage(0x10 / 0x11 / 0x12): Keith (speaker 0) or the criminal (image 10000 + i: its face).</summary>
        void WantedLine(int text, int wantedSpeaker, WantedData w)
        {
            var c = new Chatter { text = Localization.Get(text), voice = GenericVoice.For(text) };   // voice_table.json (WANTED_*)
            if (wantedSpeaker < 0) { c.speakerId = 0; c.speaker = Localization.Get(1597); }
            else { c.portrait = w.portraitParts; c.speaker = w.name; }
            chatterQueue.Enqueue(c);
        }

        // ---- step 59's arms convoy (LevelScript::process, LevelScript+0xa9) ------------------------------------------

        NpcShip convoyFreighter;
        int convoyStep;
        /// <summary>The convoy point as the player's route (Level+0x108), null = none / done.</summary>
        public Route ConvoyRoute { get; private set; }
        /// <summary>The convoy freighter died: the player's route and autopilot are cleared.</summary>
        public event Action ConvoyDone;

        /// <summary>Step 0: the player within 50 000 of the freighter -> the freighter, its escorts and turrets always-enemy,
        /// radio 0xe. Then the freighter dead -> its turrets destroyed, this station done in Status+0x90 (-1), radio 0xf.</summary>
        void UpdateConvoy()
        {
            if (convoyFreighter == null || convoyStep >= 2 || Player == null) return;
            // A cloaked player (Player+0x5e) doesn't raise the alarm.
            if (convoyStep == 0 && !Player.cloaked && (convoyFreighter.transform.position - Player.transform.position).magnitude < 50000f * 0.05f)
            {
                foreach (var s in Ships) if (s.Spec.convoyRole > 0) s.alwaysEnemy = true;
                ConvoyRadio(0x88f, false);
                convoyStep = 1;
            }
            if (convoyFreighter.Target.Alive) return;
            foreach (var s in Ships) if (s.Spec.convoyRole == SpawnSpec.ConvoyTurret) s.DestroyAsTurret();
            for (int i = 0; i < Session.StoryTargets.Count; i++) if (Session.StoryTargets[i] == StationIndex) Session.StoryTargets[i] = -1;
            ConvoyRadio(0x88e, true);
            ConvoyRoute = null;
            // PlayerEgo::setAutoPilot(null) + setRoute(null) + Level::setPlayerRoute(null).
            Player.GetComponent<Navigation>()?.SetAutopilot(null);
            ConvoyDone?.Invoke();
            convoyStep = 2;
        }

        /// <summary>Level::createRadioMessage(0xe / 0xf, system race): text 'baseText' - 2 per target station still to do
        /// (0xe: 2185 / 2187 / 2189 "too close" lines from the convoy, a face of the system race; 0xf: 2186 / 2188, none after
        /// the last one, are Keith's: Level.c 15863 jumps to the image-0 case, speaker 0 "Keith T. Maxwell").</summary>
        void ConvoyRadio(int baseText, bool afterKill)
        {
            int text = baseText;
            foreach (int t in Session.StoryTargets) if (t >= 0) text -= 2;
            if (!afterKill) { Radio(text, text, SystemRace); return; }
            if (text < 0x889 || text > 0x88d || RadioBlocked) return;
            chatterQueue.Clear();
            chatter = null;
            chatterQueue.Enqueue(new Chatter { text = Localization.Get(text), speakerId = 0, speaker = Localization.Get(1597), voice = GenericVoice.For(text) });
        }

        /// <summary>Level::updateOrbit: relaunches and raider waves (not in a campaign orbit).</summary>
        void UpdateOrbit(float dtMs)
        {
            if (IsStoryOrbit) return;
            jumperMs += dtMs;
            respawnMs += dtMs;
            if (jumperMs > 10000f)
            {
                jumperMs = 0f;
                var j = Ships.Find(s => s.IsJumper && s.Gone);
                if (j != null) j.Revive(StationPosition);
            }
            if (respawnMs < 45001f) return;
            respawnMs = 0f;
            var raiders = Ships.FindAll(s => s.Spec.group == NpcGroup.Raider);
            int deadRaiders = raiders.FindAll(s => s.Gone).Count;
            bool anyBack = false;
            foreach (var s in Ships)
            {
                if (!s.Gone) continue;
                if (s.Spec.group == NpcGroup.Local && s.Spec.wantedIndex < 0) { s.Revive(StationPosition); continue; }   // not the criminal
                if (s.Spec.group == NpcGroup.Raider && deadRaiders > 1 && raiderWaves < 2 && Player != null
                    && (s.Race == Standing.Void || Security == 0 || (Security == 1 && raidersRespawned <= 2)))
                {
                    raidersRespawned++;
                    float Sign() => UnityEngine.Random.value < 0.5f ? -1f : 1f;
                    var off = new Vector3(Sign() * (60000 + UnityEngine.Random.Range(0, 60000)), Sign() * (5000 + UnityEngine.Random.Range(0, 5000)),
                                          Sign() * (60000 + UnityEngine.Random.Range(0, 60000))) * M;
                    s.Revive(Player.transform.position + off);
                    anyBack = true;
                }
            }
            if (anyBack) raiderWaves++;
        }

        /// <summary>Level::updateAlienAttackers 0xd5ce0: every 45 000 ms (10 000 at index 41) the dead Void ships come back, at
        /// the wormhole +-10 000 while it is open, else around the player (+-40 000, +-30 000, 40 000 ahead: the fixed z offset
        /// is assumed).</summary>
        void UpdateAlienAttackers(float dtMs)
        {
            if (!VoidAttack) return;
            // Level::updateAlienAttackers 0xd5ce0: not during the campaign levels 0x28 / 0x93 / 0x9a (they place their own).
            int cm = Session.CampaignMission;
            if (!Session.FreePlay && NpcTables.InCampaignLevel && (cm == 0x28 || cm == 0x93 || cm == 0x9a)) return;
            alienMs += dtMs;
            if (alienMs < (Session.CampaignMission == 41 ? 10000f : 45000f)) return;
            alienMs = 0f;
            foreach (var s in Ships)
            {
                if (!s.Gone || s.Race != Standing.Void || s.IsWingman) continue;
                Vector3 at;
                float R(float r) => UnityEngine.Random.Range(-r, r);
                if (Wormhole != null && Wormhole.Visible) at = Wormhole.transform.position + new Vector3(R(10000f), R(10000f), R(10000f)) * M;
                else if (Player != null)   // the player's x / y +- 40000 / 30000 and a world z of +-40000 (DAT_000d5fbc / c0)
                    at = new Vector3(Player.transform.position.x + R(40000f) * M, Player.transform.position.y + R(30000f) * M, (UnityEngine.Random.Range(0, 2) == 0 ? 40000f : -40000f) * M);
                else continue;
                s.Revive(at);
            }
        }

        /// <summary>A cutscene plays its own music (the prologue / rescue): the traffic music stays silent.</summary>
        public bool MusicMuted { get; set; }

        /// <summary>Radar::draw music choice: switch (with a short fade) only when the category changes.</summary>
        /// <summary>Any active hostile Specter (race 10) in the orbit.</summary>
        bool SpectersHostile => Ships.Exists(s => s.Race == Standing.Specter && !s.Gone && !s.Inactive && s.Target.Alive && s.Target.hostileToPlayer);
        /// <summary>The supernova system (27) before campaign 0x9e: its own calm music (148).</summary>
        bool SupernovaCalm => !Session.FreePlay && Session.CampaignMission < 0x9e && (db.Stations.Find(s => s.index == StationIndex)?.system ?? -1) == 27;

        /// <summary>DAT_00252010[race]: the system race's space track (Globals::playMusicAndFadeOutCurrent(1)).</summary>
        public AudioClip RaceSpaceMusic() => assets.spaceMusic != null && assets.spaceMusic.Length == 4 ? assets.spaceMusic[SystemRace] : null;

        /// <summary>Radar::draw 0x157c6c: the calm track (no hostile ship). The alien orbit and a Void-attacked station 145,
        /// campaign 1 (the rescue) 143, the Kaamo Club 146, 101 147, the supernova system the mission target's 2241 (before
        /// 0x6a) / 2242 or else 148, the deep science orbits (10 / 100) 152, else the system race's (DAT_00252010).</summary>
        public AudioClip CalmClip()
        {
            var story = StoryAssets.Load();
            var sn = SupernovaAssets.Load();
            bool campaign = !Session.FreePlay;
            if (VoidAttack) return story?.voidMusic;
            if (campaign && Session.CampaignMission == 1) return story?.introAtmo;
            if (StationIndex == KaamoClub.Station) return assets.homeBaseMusic;
            if (StationIndex == 101) return assets.valkyrieMusic;
            if (SupernovaCalm)
            {
                bool target = Story.TargetStation == StationIndex
                              || (Session.FreelanceMission != null && Session.FreelanceMission.type != MissionType.Empty && Session.FreelanceMission.target == StationIndex);
                return target ? (Session.CampaignMission < 0x6a ? sn?.mission102Loop : sn?.mission102Loop2) : sn?.gammaRayMusic;
            }
            if (StationIndex == 10 || StationIndex == 100) return assets.deepScienceMusic;
            return assets.spaceMusic != null && assets.spaceMusic.Length == 4 ? assets.spaceMusic[SystemRace] : null;
        }

        void UpdateMusic(float dt)
        {
            if (assets == null || music == null) return;
            if (MusicMuted) { if (music.isPlaying) music.Stop(); musicCategory = pendingCategory = -1; return; }
            // MGame::OnRender2D skips Radar::draw while LevelScript's cutscene flag is set, so nothing switches the music
            // during a story cutscene (index 14's arrest: the pirates vanish, the battle track plays on).
            if (RadarHidden != null && RadarHidden() && musicCategory >= 0) { music.volume = fade * Settings.MusicVolume; return; }
            // Radar::draw 0x157c6c: while 143 IntroAtmo plays nothing switches.
            var introAtmo = StoryAssets.Load()?.introAtmo;
            if (introAtmo != null && music.clip == introAtmo && music.isPlaying) { music.volume = fade * Settings.MusicVolume; return; }
            int cat = HostileCount <= 0 ? 0 : HostileCount <= 2 ? 1 : HostileCount <= 4 ? 2 : 3;
            // At campaign 0x91 (the plasma array's destruction) a calm orbit never brings the calm track back.
            if (cat == 0 && musicCategory > 0 && !Session.FreePlay && Session.CampaignMission == 0x91) cat = musicCategory;
            // Radar::draw: an uncovered Most Wanted criminal -> 151; hostile Specters (race 10) -> 149 / 150.
            if (cat > 0 && WantedUncovered) cat = 4;
            else if (cat > 0 && SpectersHostile) cat = cat >= 3 ? 6 : 5;
            // A battle track that plays is kept until the orbit is calm (the 0xe071 set: 136, 140-142, 149-151); only an
            // uncovered criminal switches to 151.
            if (cat > 0 && musicCategory > 0 && cat != 4) cat = musicCategory;
            if (cat != musicCategory && cat != pendingCategory) pendingCategory = cat;
            if (pendingCategory >= 0)
            {
                fade = musicCategory < 0 ? 0f : Mathf.Max(0f, fade - dt / 1.5f);
                if (fade <= 0f)
                {
                    musicCategory = pendingCategory;
                    pendingCategory = -1;
                    var sn = SupernovaAssets.Load();
                    var story = StoryAssets.Load();
                    // In the alien orbit, at a Void-attacked station and at campaign 0x10 every fight is 136 Space_Combat_Void.
                    bool voidBattle = VoidAttack || (!Session.FreePlay && Session.CampaignMission == 0x10);
                    var clip = musicCategory == 0 ? CalmClip()
                             : musicCategory == 4 ? sn?.wantedMusic
                             : voidBattle ? story?.voidBattle
                             : musicCategory == 5 ? sn?.stealthMusic1
                             : musicCategory == 6 ? sn?.stealthMusic2
                             : (assets.battleMusic != null && assets.battleMusic.Length == 3 ? assets.battleMusic[musicCategory - 1] : null);
                    music.clip = clip;
                    if (clip != null) music.Play();
                }
            }
            else fade = Mathf.Min(1f, fade + dt / 1.5f);
            music.volume = fade * Settings.MusicVolume;
        }
    }
}
