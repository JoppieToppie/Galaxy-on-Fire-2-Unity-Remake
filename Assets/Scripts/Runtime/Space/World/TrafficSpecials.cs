// TrafficSpecials.cs
// Traffic: static objects, the Most Wanted criminal, step 59's arms convoy (part of the partial Traffic; its fields are in Traffic.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class Traffic
    {
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
            if (ship != null && ship.Spec.modBlueprint >= 0)
            {
                // Remake mods: a derelict's blueprint, in a data crate drifting off the wreck (pulled in, it is learnt); none
                // when it was learnt meanwhile (a blueprint is handed out once).
                ship.DockingType = 0;
                if (!Blueprints.IsUnlocked(ship.Spec.modBlueprint) && !Crate.HoldsAny(ship.Spec.modBlueprint))
                    Crate.Spawn(ship.transform.position + ship.transform.up * 300f * M, new[] { new ItemStack(ship.Spec.modBlueprint, 1) },
                            ship.Spec.race, Crate.LookJunk);
                return;
            }
            if (ship != null && ship.Spec.eventWreck) { ship.DockingType = 0; return; }   // an event's wreck: its "on hacked" does the rest
            int k = ship != null ? ship.Spec.hiddenBlueprint : -1;
            if (k < 0 || k >= TrafficPlan.HiddenBlueprints.Length || (Session.HiddenBlueprintsFound & (1 << k)) != 0) return;
            Session.HiddenBlueprintsFound |= 1 << k;
            Blueprints.Unlock(TrafficPlan.HiddenBlueprints[k].blueprint);
            ship.DockingType = 0;
            QueueLine(3156 + k, 0, $"BLUEPRINT_RECOVERED_{k}");
        }
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
            WantedRadio();
            if (w.index == 0) { WantedLine(3134, -1, w); WantedLine(3135, w.index, w); }
            else if (w.index == 1) { WantedLine(3139, -1, w); WantedLine(3140, w.index, w); }
            else WantedLine(3141 + UnityEngine.Random.Range(0, 5), -1, w);
            if (!string.IsNullOrEmpty(w.taunt)) chatterQueue.Enqueue(CriminalSays(w, w.taunt, null));   // (a mod's "taunt")
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
            // Remake (a player's report 2026-11-03, steps 129 / 130): uncoverWanted doesn't set the flag (Level+0x29c), so in
            // the original the first hit after a lock greeted the criminal a second time ("Damn it. Someone found me!" twice,
            // "negotiations Keith T. Maxwell style" twice). The storyline pairs are both first contact: after the lock's, the hit
            // says nothing. Wanted 2 / 6 still taunt (Keith's uncover line is no repeat of it).
            if (w == null || (wantedUncovered && WantedBoard.IsStoryline(w.index))) return;
            WantedRadio();
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
            // Player::damage calls Level::almostKillWanted before its death branch: a storyline criminal killed from above a
            // third of its hull in one blow (or one frame) still wins step 128 / 130. UpdateWanted only sees living ships, so
            // such a kill left the step waiting for a dead criminal (a player's report).
            if (WantedBoard.IsStoryline(w.index) && !wantedSurrendered)
            {
                wantedSurrendered = true;
                WantedBoard.OnSurrender(db, w.index, StationIndex);
            }
            int reward = WantedBoard.OnKilled(db, w.index);
            if (reward > 0) BountyCollected?.Invoke(reward);
            WantedRadio();
            WantedLine(3151 + UnityEngine.Random.Range(0, 5), -1, w);   // Level::killWanted: "a job well done"
        }
        /// <summary>Level::createRadioMessage replaces the level's radio list: a wanted message cuts the lines still waiting or
        /// showing (the remake queued them after each other, so a lock, a hit and a kill played one long conversation).</summary>
        void WantedRadio()
        {
            chatterQueue.Clear();
            chatter = null;
        }
        /// <summary>Level::createRadioMessage(0x10 / 0x11 / 0x12): Keith (speaker 0) or the criminal (image 10000 + i: its face).</summary>
        void WantedLine(int text, int wantedSpeaker, WantedData w)
        {
            var c = new Chatter { text = Localization.Get(text), voice = GenericVoice.For(text) };   // voice_table.json (WANTED_*)
            if (wantedSpeaker < 0) { c.speakerId = 0; c.speaker = Localization.Get(1597); }
            else c = CriminalSays(w, c.text, c.voice);
            chatterQueue.Enqueue(c);
        }
        /// <summary>A line in the criminal's name, with its face (a mod's: its character's portrait).</summary>
        static Chatter CriminalSays(WantedData w, string text, string voice) =>
            new Chatter { text = text, voice = voice, portrait = w.portraitParts, speaker = w.name, character = w.character };
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
                var j = Ships.Find(s => s.IsJumper && s.Gone && s.Spec.eventTag == 0);
                if (j != null) j.Revive(StationPosition);
            }
            if (respawnMs < 45001f) return;
            respawnMs = 0f;
            var raiders = Ships.FindAll(s => s.Spec.group == NpcGroup.Raider);
            int deadRaiders = raiders.FindAll(s => s.Gone).Count;
            bool anyBack = false;
            foreach (var s in Ships)
            {
                if (!s.Gone || s.Spec.eventTag != 0) continue;   // an event's ships never come back by themselves (remake)
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
        /// <summary>Level::updateAlienAttackers 0xd5ce0: every 45 000 ms (10 000 at index 41) the dead Void ships come back: at
        /// the wormhole +-10 000 only at a station the Void attacks (!inAlienOrbit && Station::isAttackedByAliens), else (the
        /// alien orbit, coming out of the Void) around the player (+-40 000, +-30 000, a world z of +-40 000; #35: they came
        /// out of the wormhole in the alien orbit too).</summary>
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
                if (!s.Gone || s.Race != Standing.Void || s.IsWingman || s.Spec.eventTag != 0) continue;
                Vector3 at;
                float R(float r) => UnityEngine.Random.Range(-r, r);
                if (Wormhole != null && !Wormhole.alienOrbit && Wormhole.attackedStation)
                    at = Wormhole.transform.position + new Vector3(R(10000f), R(10000f), R(10000f)) * M;
                else if (Player != null)   // the player's x / y +- 40000 / 30000 and a world z of +-40000 (DAT_000d5fbc / c0)
                    at = new Vector3(Player.transform.position.x + R(40000f) * M, Player.transform.position.y + R(30000f) * M, (UnityEngine.Random.Range(0, 2) == 0 ? 40000f : -40000f) * M);
                else continue;
                s.Revive(at);
            }
        }
        /// <summary>Radar::draw music choice: switch (with a short fade) only when the category changes.</summary>
        /// <summary>Any active hostile Specter (race 10) in the orbit.</summary>
        bool SpectersHostile => Ships.Exists(s => s.Race == Standing.Specter && !s.Gone && !s.Inactive && s.Target.Alive && s.Target.hostileToPlayer);
        /// <summary>The supernova system (27) before campaign 0x9e: its own calm music (148).</summary>
        bool SupernovaCalm => !Session.FreePlay && Session.CampaignMission < 0x9e && (db.Stations.Find(s => s.index == StationIndex)?.system ?? -1) == 27;
        /// <summary>DAT_00252010[race]: the system race's space track (Globals::playMusicAndFadeOutCurrent(1)).</summary>
        public AudioClip RaceSpaceMusic() => Modding.ModWorld.SpaceMusic(StationIndex)   // remake mods: a system's own space music
            ?? (assets.spaceMusic != null && assets.spaceMusic.Length == 4 ? assets.spaceMusic[Mathf.Clamp(Standing.Look(SystemRace), 0, 3)] : null);
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
                              || (Session.FreelanceMission != null && Session.FreelanceMission.type != MissionType.Empty && Session.FreelanceMission.target == StationIndex)
                              || (SupernovaChallenge.Active && StationIndex == SupernovaChallenge.Station);   // 0xb7's mission targets Luur: 2242
                return target ? (Session.CampaignMission < 0x6a ? sn?.mission102Loop : sn?.mission102Loop2) : sn?.gammaRayMusic;
            }
            if (StationIndex == 10 || StationIndex == 100) return assets.deepScienceMusic;
            return assets.spaceMusic != null && assets.spaceMusic.Length == 4 ? assets.spaceMusic[Mathf.Clamp(Standing.Look(SystemRace), 0, 3)] : null;
        }
        void UpdateMusic(float dt)
        {
            if (assets == null || music == null) return;
            if (MusicMuted) { if (music.isPlaying) music.Stop(); musicCategory = pendingCategory = -1; return; }
            // MGame::OnRender2D skips Radar::draw while LevelScript's cutscene flag is set, so nothing switches the music
            // during a story cutscene (index 14's arrest: the pirates vanish, the battle track plays on).
            // The track's fade-in goes on: a level in its cutscene from the start (the Supernova's "Meanwhile..." orbits,
            // MGame::OnInitialize's playMusicAndFadeOutCurrent(1) playing 152 at Thynome) started its track at 0 and kept it
            // there.
            if (RadarHidden != null && RadarHidden() && musicCategory >= 0)
            {
                fade = Mathf.Min(1f, fade + dt / 1.5f);
                music.volume = fade * Settings.MusicVolume * GoF2Remake.World.SpaceHooks.SceneMusic;
                return;
            }
            // Radar::draw 0x157c6c: while 143 IntroAtmo plays nothing switches.
            var introAtmo = StoryAssets.Load()?.introAtmo;
            if (introAtmo != null && music.clip == introAtmo && music.isPlaying) { music.volume = fade * Settings.MusicVolume * GoF2Remake.World.SpaceHooks.SceneMusic; return; }
            // In the alien orbit, at a Void-attacked station and at campaign 0x10 every fight is 136 Space_Combat_Void.
            bool voidBattle = VoidAttack || (!Session.FreePlay && Session.CampaignMission == 0x10);
            // Radar::draw's category (MusicTracks.Category, which skips the tracks turned off in Options > Sound):
            // 1-2 / 3-4 / 5+ hostiles the battle tracks (a fleet battle at least Medium, remake CapitalShips), at
            // campaign 0x91 (the plasma array's destruction) a calm orbit never brings the calm track back, an uncovered
            // Most Wanted criminal 151, hostile Specters (race 10) 149 / 150; a battle track that plays is kept until
            // the orbit is calm, only 151 takes over.
            int cat = MusicTracks.Category(HostileCount, FleetBattleRaging, musicCategory,
                !Session.FreePlay && Session.CampaignMission == 0x91, WantedUncovered, SpectersHostile, voidBattle, MusicTracks.On);
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
                    var clip = musicCategory == 0 ? CalmClip()
                             : musicCategory == 4 ? Modding.ModMusic.Track(WantedOf(wantedShip)?.music) ?? sn?.wantedMusic   // (a mod's "music")
                             : voidBattle ? story?.voidBattle
                             : musicCategory == 5 ? sn?.stealthMusic1
                             : musicCategory == 6 ? sn?.stealthMusic2
                             : (GoF2Remake.World.EncounterMusic.Current is string em ? Modding.ModMusic.Track(em) : null)   // remake mods: an encounter's own "music"
                               ?? Modding.ModWorld.BattleMusic(StationIndex, musicCategory)   // remake mods: a system's own "battleMusic"
                               ?? (assets.battleMusic != null && assets.battleMusic.Length == 3 ? assets.battleMusic[musicCategory - 1] : null);
                    music.clip = clip = Modding.ModMusic.Replace(clip);   // a mod's track of the same name
                    if (clip != null) music.Play();
                }
            }
            else fade = Mathf.Min(1f, fade + dt / 1.5f);
            music.volume = fade * Settings.MusicVolume * GoF2Remake.World.SpaceHooks.SceneMusic;
        }
    }
}
