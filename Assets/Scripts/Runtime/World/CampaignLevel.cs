// CampaignLevel.cs
// The content of an orbit built around a campaign mission (Level::createCampaignMission 0xc3370, LevelScript::process
// 0x160d50; Reference/research/campaign_levels_a.md, levelscript_cutscenes.md): the step's ships in the original's
// order (Level+0xf8, which the radio triggers and objectives index), its radio messages (Radio), the player route
// (HUD waypoints, Level+0x108), the level-script event and mission clock, and the win / fail objectives. Created by
// SpaceLevel when Story.IsLevelMission holds for the orbit; normal traffic is off then (Traffic). Indices
// without a case spawn nothing (an empty orbit) but still play their radio lines. The level keeps running after its
// success dialogue with the next index (e.g. 4 -> 5 on the same ships).
// Built: 0 / 1 (the prologue and the rescue cutscenes, IntroCutscenes), 4 / 5 (mining, the pirate ambush), 7 (the pirate
// trap with Gunant Breh); the rest of the main campaign (14 - 42) in MainCampaignLevels, the Valkyrie add-on (48 - 81) in
// ValkyrieLevels, the Supernova add-on (87 - 158) in SupernovaLevels.
// Level+0x20 / +0x24: hostile ships killed by NPCs / by the player (Level::enemyDied); Level+0x1c: crate cargo captured
// here; LevelScript+0: a time limit (the HUD counts it down; index 29's survival objective).
// Cutscene support: the look-at camera (CutsceneCamera), fades (Layout::startFade: full-screen colour over n ms),
// the level's own music and sound loops, and flags for the level (HUD off, invulnerable, no collision, start sequence).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class CampaignLevel : MonoBehaviour, IRadioWorld
    {
        const float M = 0.05f;

        public readonly List<NpcShip> Ships = new List<NpcShip>();
        public Radio Radio { get; private set; }
        /// <summary>Level+0x108: the player route (game units), shown as HUD waypoints; null = none.</summary>
        public Route PlayerRoute { get; private set; }
        public int BuiltIndex { get; private set; }
        public int Event { get; set; }
        public float MissionMs { get; private set; }
        /// <summary>Level::checkObjective (Level+0x28) / checkGameOver (+0x2c).</summary>
        public bool Won => win != null && win();
        public bool Failed => fail != null && fail();
        /// <summary>The level script sets / replaces the objectives.</summary>
        public System.Func<bool> WinObjective { get => win; set => win = value; }
        public System.Func<bool> FailObjective { get => fail; set => fail = value; }
        /// <summary>MGame::removeObjectives (after a success dialogue, index 42).</summary>
        public void RemoveObjectives() { win = fail = null; TimeLimitMs = 0f; }
        /// <summary>Level+0x20 / +0x24: hostile ships killed by NPCs / by the player.</summary>
        public int NpcKills { get; private set; }
        public int PlayerKills { get; private set; }
        /// <summary>LevelScript+0: the mission's time limit (0 = none), shown on the HUD.</summary>
        public float TimeLimitMs { get; set; }
        public float TimeLeftMs => TimeLimitMs > 0f ? Mathf.Max(0f, TimeLimitMs - MissionMs) : -1f;
        /// <summary>Errkt's freighter's hull (ship 0 at index 40), -1 = none.</summary>
        public int FreighterHull => Ships.Count > 0 && Ships[0] != null ? Ships[0].Hp.hull : -1;
        public SpaceLevel Level => level;
        public Traffic Traffic => traffic;
        MainCampaignLevels main;
        ValkyrieLevels valkyrie;
        SupernovaLevels supernova;
        int cratesAtStart;

        // Cutscene state (LevelScript: this[0x11] cinematic, player invulnerable / no collision, startSequenceOver).
        public bool Cutscene { get; set; }
        public bool PlayerInvulnerable { get; set; }
        public bool CollisionOff { get; set; }
        public bool StartSequenceOver { get; set; } = true;
        /// <summary>The level plays its own music (the traffic music stays silent).</summary>
        public bool MusicOwned { get; set; }
        /// <summary>Layout::drawFade: the full-screen colour's opacity now (0 = none).</summary>
        public float FadeAlpha { get; private set; }
        public Color FadeColor { get; private set; } = Color.black;
        public bool FadeDone => fadeMs >= fadeLength;

        IntroCutscenes intro;
        /// <summary>The prologue / rescue cutscene of this level (indices 0 / 1), null otherwise.</summary>
        public IntroCutscenes Intro => intro != null && Story.Index == BuiltIndex ? intro : null;
        /// <summary>The pause menu's Skip (395): the prologue / rescue, or a Supernova cutscene (154 / 157 / 158).</summary>
        public bool CanSkipCutscene => (Intro != null && Intro.CanSkip) || (supernova != null && supernova.CanSkipCutscene);
        public void SkipCutscene()
        {
            if (Intro != null && Intro.CanSkip) Intro.Skip();
            else supernova?.SkipCutscene();
        }
        AudioSource music;
        readonly AudioSource[] loops = new AudioSource[3];
        float fadeMs, fadeLength = 1f;
        bool fadeIn, fading;

        SpaceLevel level;
        Traffic traffic;
        System.Func<bool> win, fail;

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;
        static Vector3 Jitter() => new Vector3(Random.Range(0, 40000) - 20000, Random.Range(0, 40000) - 20000, Random.Range(0, 40000) - 20000);

        public void Setup(SpaceLevel spaceLevel, Traffic npcTraffic)
        {
            level = spaceLevel;
            traffic = npcTraffic;
            BuiltIndex = Story.Index;
            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            cratesAtStart = Session.CratesSalvaged;
            traffic.ShipDied += OnShipDied;
            Build(BuiltIndex);
            traffic.MusicMuted = MusicOwned;
            traffic.ConnectPlayers(main != null ? main.PlayerExemptRace : -99);
            valkyrie?.AfterConnect();
            Radio = new Radio(Story.Step?.radio);
            Debug.Log($"CampaignLevel: index {BuiltIndex}, {Ships.Count} ships, {Radio.Count} radio lines");
        }

        /// <summary>Level+0x108 set by a level (the HUD waypoints); PlayerEgo::setRoute hands it to the ship, whose update
        /// advances it (Navigation).</summary>
        public void SetPlayerRoute(Route route)
        {
            PlayerRoute = route;
            if (level != null && level.Navigation != null) level.Navigation.SetRoute(route);
        }

        /// <summary>MGame::dialogueEvent: the briefing restarts the mission clock.</summary>
        public void ResetClock() => MissionMs = 0f;

        /// <summary>A slot of Level+0xf8 that holds no NpcShip (index 80's battlestation is scenery the radio doesn't count).</summary>
        public void AddPlaceholder() => Ships.Add(null);

        /// <summary>Level::createShip(race, kind, ship, waypoint ...): at the waypoint +- 20 000 per axis.</summary>
        public NpcShip SpawnShip(int race, int ship, Vector3 waypoint, bool jitter = true, System.Action<SpawnSpec> setup = null)
        {
            var spec = new SpawnSpec { group = NpcGroup.Raider, race = race, ship = ship, position = waypoint + (jitter ? Jitter() : Vector3.zero) };
            setup?.Invoke(spec);
            var s = traffic.SpawnShip(spec);
            Ships.Add(s);
            return s;
        }

        void Build(int index)
        {
            var player = level.Player.transform;
            switch (index)
            {
                case 0:
                case 1:
                    intro = new IntroCutscenes(this, level, index);
                    intro.Build();
                    break;
                case 4:
                    // One pirate (ship 2 Hiro) far out, inactive, always-enemy, asleep; index 5's script brings it in.
                    SpawnShip(Standing.Pirate, 2, new Vector3(0, 0, -200000), true, s => { s.inactive = true; s.alwaysEnemy = true; });
                    break;
                case 7:
                {
                    // Player route (-4000, -3000, 80 000) -> (10 000, 7000, 160 000); 3 pirates asleep at waypoint 1; Gunant
                    // Breh (Midorian ship 30) beside the player, flying the route, unkillable, always-friend.
                    var route = new Route(false);
                    route.points.Add(new Vector3(-4000, -3000, 80000));
                    route.points.Add(new Vector3(10000, 7000, 160000));
                    PlayerRoute = route;
                    for (int i = 0; i < 3; i++) SpawnShip(Standing.Pirate, 2, route.points[1], true, s => { s.asleep = true; });
                    var gunantPos = ToGame(player.TransformPoint(new Vector3(-700, 50, 6000) * M));
                    SpawnShip(3, 30, gunantPos, false, s => { s.alwaysFriend = true; s.hitpoints = 9999999; s.route = route.Clone(); s.nameText = 1599; });
                    win = () => DeadRange(0, 3);   // Objective 0x12 (0, 3)
                    break;
                }
                default:
                    main = new MainCampaignLevels(this, level);
                    if (!main.Build(index)) main = null;
                    if (main != null) break;
                    valkyrie = new ValkyrieLevels(this, level);
                    if (!valkyrie.Build(index)) valkyrie = null;
                    if (valkyrie != null) break;
                    supernova = new SupernovaLevels(this, level);
                    if (!supernova.Build(index)) supernova = null;
                    break;
            }
        }

        void OnShipDied(NpcShip ship, bool byPlayer)
        {
            if (ship == null || !ship.Target.hostileToPlayer) return;
            if (byPlayer) PlayerKills++; else NpcKills++;
        }

        void OnDestroy()
        {
            if (traffic != null) traffic.ShipDied -= OnShipDied;
        }

        public bool DeadRange(int a, int b)
        {
            for (int i = a; i < b; i++) if (!ShipDestroyed(i)) return false;
            return true;
        }

        bool musicHeld;

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            Navigation.SyncMusic(music, ref musicHeld);   // on through conversations, paused by the pause menu
            if (dtMs <= 0f) return;
            MissionMs += dtMs;
            if (fading)
            {
                fadeMs += dtMs;
                float t = Mathf.Clamp01(fadeMs / fadeLength);
                FadeAlpha = fadeIn ? 1f - t : t;
                if (fadeIn && t >= 1f) fading = false;   // a fade-out stays opaque (enableFillScreen)
            }
            // The player's death stops the level: its script, cutscenes and radio wait (MGame::OnUpdate only runs the dialogue
            // windows and the success check while the player lives; remake: LevelScript::process waits too, so a cutscene
            // or a story step can't play on under the game over).
            if (level.Health != null && level.Health.Dead) return;
            if (intro != null && Story.Index == BuiltIndex) intro.Tick(dtMs);
            main?.Tick(Story.Index, dtMs);
            valkyrie?.Tick(Story.Index, dtMs);
            supernova?.Tick(Story.Index, dtMs);
            Script(Story.Index);
            if (!level.Dialogue && level.LaunchCameraOver) Radio?.Update(dtMs, this);   // not during the launch / arrival camera
        }

        void LateUpdate()
        {
            if (intro != null) intro.LateTick(Time.deltaTime * 1000f);
            main?.LateTick(Time.deltaTime * 1000f);
            valkyrie?.LateTick(Time.deltaTime * 1000f);
            supernova?.LateTick(Time.deltaTime * 1000f);
        }

        // ---- cutscene helpers ------------------------------------------------------------------------------

        /// <summary>Layout::startFade: in = from the colour to clear, out = from clear to the colour (then held).</summary>
        public void Fade(bool fadingIn, Color colour, float ms, bool fromOpaque = false)
        {
            fadeIn = fadingIn;
            FadeColor = colour;
            fadeLength = Mathf.Max(1f, ms);
            fadeMs = 0f;
            fading = true;
            FadeAlpha = fadingIn ? 1f : 0f;
        }

        /// <summary>Stop the music and play another track (null = silence).</summary>
        public void PlayMusic(AudioClip clip, bool loop)
        {
            music.Stop();
            music.clip = clip;
            music.loop = loop;
            music.volume = Settings.MusicVolume;
            if (clip != null) music.Play();
        }

        /// <summary>A held sound in one of three slots (the rumble, the broken engines) at its FMOD event volume (x
        /// Sfx.EventGain); 'loop' false = a oneshot that can still be stopped early.</summary>
        public void PlayLoop(int slot, AudioClip clip, float eventVolume = 0.25f, bool loop = true)
        {
            if (loops[slot] == null)
            {
                loops[slot] = gameObject.AddComponent<AudioSource>();
                loops[slot].playOnAwake = false;
                loops[slot].spatialBlend = 0f;
            }
            loops[slot].loop = loop;
            loops[slot].clip = clip;
            loops[slot].volume = Mathf.Min(1f, eventVolume * Sfx.EventGain) * Settings.SfxVolume;
            if (clip != null) loops[slot].Play();
        }

        public void StopLoop(int slot) { if (loops[slot] != null) loops[slot].Stop(); }

        /// <summary>LevelScript::process per index (campaign_levels_a.md 3).</summary>
        void Script(int index)
        {
            switch (index)
            {
                case 5:
                    // On index 4's level: ship 0 comes at the player from player + (5000, 0, 30 000) (world axes) and wakes.
                    if (Event == 0 && BuiltIndex == 4 && Ships.Count > 0)
                    {
                        Event = 1;
                        var p = ToGame(level.Player.transform.position) + new Vector3(5000, 0, 30000);
                        Ships[0].Place(ToUnity(p), level.Player.transform.position - ToUnity(p));
                        Ships[0].Wake();
                    }
                    break;
            }
        }

        // ---- IRadioWorld -------------------------------------------------------------------------------

        public int ScriptEvent => Event;
        public int ShipCount => Ships.Count;
        public bool ShipDead(int i) => i >= 0 && i < Ships.Count && (Ships[i] == null || !Ships[i].Target.Alive);
        /// <summary>KIPlayer::isDead (state 4), what the level objectives check (Objective::achieved 1 / 7 / 0x12 / 0x14): past
        /// the death tumble, at the explosion. The radio's triggers take Player::isDead (no hull left, ShipDead).</summary>
        public bool ShipDestroyed(int i) => i >= 0 && i < Ships.Count
                                            && (Ships[i] == null || (!Ships[i].Target.Alive && (Ships[i].Current == NpcShip.State.Dead || Ships[i].Gone)));
        public bool ShipActive(int i) => i >= 0 && i < Ships.Count && Ships[i] != null && !Ships[i].Gone && !Ships[i].Asleep && Ships[i].Target.Alive;
        public float ShipHullFraction(int i) => i >= 0 && i < Ships.Count && Ships[i] != null ? Ships[i].Target.HullFraction : 0f;
        /// <summary>Radio trigger 0x10 / the enemies-left counter: a ship that isn't always-friend.</summary>
        public bool ShipHostile(int i) => i >= 0 && i < Ships.Count && Ships[i] != null && !Ships[i].alwaysFriend && !Ships[i].IsWingman;
        public bool ShipFriendly(int i) => i >= 0 && i < Ships.Count && Ships[i] != null && Ships[i].Target.friendToPlayer;
        public bool ShipEmpDisabled(int i) => i >= 0 && i < Ships.Count && Ships[i] != null && Ships[i].Target.Alive && Ships[i].Hp.empDisabled;
        public bool ShipInactive(int i) => i >= 0 && i < Ships.Count && Ships[i] != null && (Ships[i].Inactive || Ships[i].Gone);
        public float ShipGameZ(int i) => i >= 0 && i < Ships.Count && Ships[i] != null ? -Ships[i].transform.position.z / M : 0f;
        public int RouteIndex => PlayerRoute != null ? PlayerRoute.index : 0;
        public int CrateCargoCaptured => Session.CratesSalvaged - cratesAtStart;
        public bool StationLocked => level.Navigation != null && level.Navigation.Locked != null && level.Navigation.Locked.kind == Navigation.Kind.Station;
        /// <summary>Radio type 0x1c: Player::getArmorHP &lt; 1 (no armor mounted counts too).</summary>
        public bool PlayerArmorGone => level.Health != null && level.Health.Hp.armor < 1;
        public int EnemiesLeft { get { int n = 0; for (int i = 0; i < Ships.Count; i++) if (!ShipDead(i) && ShipHostile(i)) n++; return n; } }
        public int FriendsLeft { get { int n = 0; for (int i = 0; i < Ships.Count; i++) if (!ShipDead(i) && !ShipHostile(i)) n++; return n; } }
    }
}
