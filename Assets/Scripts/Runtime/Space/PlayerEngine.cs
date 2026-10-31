// PlayerEngine.cs
// The player ship's engine loop and boost sound (the FEV's LGCY data, fev_lgcy.py --event 42..45 / 1104-1107 / 38-41).
//   PlayerEgo::PlayerEgo 0xa5940    +0x1c engine event: ship 42 -> 1104 EngineDLC_08, 43 -> 1106 EngineDLC_11, 40 -> 1107
//                                   EngineDLC_01, else by handling (Ship::getHandling): >= 1.4 45, >= 1.15 44, >= 0.95 43,
//                                   else 42; +0xd4 boost sound: boosters 71-74 -> 38-41, 195 -> 1102 Polytron_Boost
//   MGame::OnInitialize             FModSound::play(+0x1c) at the level's start; PlayerEgo::PauseEngineSound while mining
//   PlayerEgo::handleShip 0xa7df0   parameter 0 "Vertical" = max(|stick x|, |stick y|) (+0x268 / +0x270, PlayerEgo::right /
//                                   down): the pitch envelope x0.891 -> x1 at 0.5 -> x1.122 (42 / 44 / 45; 43 x0.944 .. x1.059;
//                                   the DLC engines x0.891 .. x1.122 linear, only their second layer); parameter 1 = x * 0.2 +
//                                   0.5 (speaker spread, no 2D effect); "load" is never set: its volume envelope's 0.7
//   PlayerEgo::boost                FModSound::play(+0xd4) when a boost starts
// In the supernova orbits the gamma shield's loop takes the engine's slot (PlayerHealth plays it), so no engine then.
// Remake mods: a ship with its own sounds (ships.json "sounds", Modding.ModShipSounds: EVERSPACE 2's engines) plays its engine
// loop by speed, its boost start / loop / stop and its booster's recharge loop instead.
// Volumes: the event volume x Sfx.EventGain, x the events' linear rolloff from the camera (EngineVoices; the sound stays
// 2D, only its volume follows the distance).

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class PlayerEngine : MonoBehaviour
    {
        const float LoadGain = 0.7f;
        static readonly float[] EngineVolume = { 0.0977f, 0.0955f, 0.0876f, 0.0876f, 0.0746f, 0.068f, 0.0566f };
        static readonly float[] BoostVolume = { 0.2018f, 0.195f, 0.1862f, 0.2065f, 0.2526f };

        ShipController ship;
        PlayerHealth health;
        Mining mining;
        AudioSource main, extra, boostSource;
        /// <summary>Remake, multiplayer: the cruise mode (Navigation.Cruise) sounds like a boost, the booster's own, or without
        /// one item 71's (index 0): a new pilot's Betty has no booster.</summary>
        AudioClip cruiseClip;
        int cruiseBoost;
        // Remake mods: the ship's own sounds (Modding.ModShipSounds), null = the game's.
        Modding.ModShipSounds.ShipSounds modSounds;
        AudioSource boostLoop, charger;
        int engine = -1, boost = -1;
        bool wasBoosting;
        /// <summary>MGame::OnInitialize starts the engine loop only above campaign index 1: the prologue's and the rescue's
        /// Phantom is silent (index 0 swaps it for the broken engine 156, IntroCutscenes).</summary>
        bool storySilent;

        public static PlayerEngine Attach(GameObject player, Database db, ShipController ship)
        {
            var e = player.AddComponent<PlayerEngine>();
            e.Setup(db, ship);
            e.storySilent = !Session.FreePlay && Session.CampaignMission <= 1;
            return e;
        }

        /// <summary>PlayerEgo::PlayerEgo's boost sound (+0xd4) for the mounted booster, by its look: boosters 71-74 -> 0-3 (events
        /// 38-41), 195 -> 4 (1102); -1 without a booster. Remake: a mod's booster sounds like the original it is based on
        /// (ItemData.Look: it was silent, its own number matching none), and any other booster like item 71's.</summary>
        public static int BoostSound(ItemData booster)
        {
            if (booster == null) return -1;
            int look = booster.Look;
            return look >= 71 && look <= 74 ? look - 71 : look == 195 ? 4 : 0;
        }

        void Setup(Database db, ShipController shipController)
        {
            ship = shipController;
            wasBoosting = ship != null && ship.Model != null && ship.Model.LooksBoosting;   // out of a load at speed, no new start
            health = GetComponent<PlayerHealth>();
            mining = GetComponent<Mining>();
            var audio = CombatAudio.Load();
            engine = EngineIndex(db, Session.ShipIndex);
            var booster = Shop.FirstMounted(db, 14);
            boost = BoostSound(booster);
            modSounds = Modding.ModShipSounds.Ship(Session.ShipIndex);
            if (modSounds?.engine?.Clip is AudioClip own)
            {
                main = Loop(own);
                engine = -1;   // no game engine layers
            }
            if (modSounds != null)
            {
                if (modSounds.boostLoop?.Clip is AudioClip loop) boostLoop = Loop(loop);
                if (modSounds.charger?.Clip is AudioClip charge) charger = Loop(charge);
            }
            boostSource = gameObject.AddComponent<AudioSource>();
            boostSource.playOnAwake = false;
            boostSource.spatialBlend = 0f;
            if (audio == null) return;
            if (main == null)
            {
                main = Loop(Pick(audio.playerEngines, engine));
                extra = Loop(Pick(audio.playerEngineExtras, engine));
            }
            boostSource.clip = GoF2Remake.Modding.ModSounds.Get(Pick(audio.boosters, boost));
            cruiseBoost = boost >= 0 ? boost : 0;
            cruiseClip = boostSource.clip != null ? boostSource.clip : GoF2Remake.Modding.ModSounds.Get(Pick(audio.boosters, cruiseBoost));
        }

        /// <summary>The boost sound for this boost's start: the booster's, or the cruise's fallback while only cruising.</summary>
        AudioClip StartClip(out int index)
        {
            bool cruiseOnly = ship.Model != null && !ship.Model.IsBoosting;
            index = cruiseOnly ? cruiseBoost : boost;
            return cruiseOnly ? cruiseClip : boostSource != null ? boostSource.clip : null;
        }

        /// <summary>PlayerEgo::PlayerEgo 0xa5940: ships 42 / 43 / 40 their DLC engines, else by handling.</summary>
        static int EngineIndex(Database db, int ship)
        {
            float handling = (db.Ship(ship)?.handling ?? 100) / 100f;
            return ship == 42 ? 4 : ship == 43 ? 5 : ship == 40 ? 6
                 : handling >= 1.4f ? 3 : handling >= 1.15f ? 2 : handling >= 0.95f ? 1 : 0;
        }

        /// <summary>The ship's engine loop and its volume (event volume x load x EventGain, without the FX volume): the
        /// hangar flights (HangarFlight).</summary>
        public static AudioClip EngineClip(Database db, int ship, out float volume)
        {
            // Remake mods: the ship's own engine (ModShipSounds), heard at cruising speed.
            if (Modding.ModShipSounds.EngineClip(ship, 0.6f, out volume) is AudioClip own) return own;
            int e = EngineIndex(db, ship);
            volume = EngineVolume[e] * LoadGain * Sfx.EventGain;
            return Pick(CombatAudio.Load()?.playerEngines, e);
        }

        static AudioClip Pick(AudioClip[] list, int i) => list != null && i >= 0 && i < list.Length ? list[i] : null;

        AudioSource Loop(AudioClip clip)
        {
            if (clip == null) return null;
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.clip = GoF2Remake.Modding.ModSounds.Get(clip);
            return s;
        }

        void Update()
        {
            if (ship == null) return;
            if (health == null) health = GetComponent<PlayerHealth>();
            if (mining == null) mining = GetComponent<Mining>();
            bool visible = ship.visualModel == null || ship.visualModel.gameObject.activeInHierarchy;
            bool on = visible && !storySilent && (health == null || (!health.Dead && !health.GammaLoopActive))
                      && (mining == null || (mining.State != Mining.Phase.Docked && mining.State != Mining.Phase.Mining));
            // "Vertical": the stick's larger axis, 0..1.
            var stick = ship.SteerInput;
            float v = Mathf.Clamp01(Mathf.Max(Mathf.Abs(stick.x), Mathf.Abs(stick.y)));
            float pitch = engine >= 4 ? Mathf.Pow(2f, 8f * Mathf.Lerp(0.479167f, 0.520833f, v) - 4f)
                        : engine == 1 ? PitchEnvelope(v, 0.489583f, 0.510417f) : PitchEnvelope(v, 0.479167f, 0.520833f);
            float volume = (engine >= 0 ? EngineVolume[engine] : 0f) * LoadGain * Sfx.EventGain * Settings.SfxVolume;
            // A 3D event at the ship heard from the camera (EngineVoices): about 85 % from the chase camera, quiet from the
            // launch camera's fly-by.
            var ear = EngineVoices.Listener();
            // Remake debug: a capital ship's fitted camera sits km out, far past the 500 m rolloff: the distance is scaled by
            // the normal chase distance / the fitted one (ShipController.engineEarScale, 1 for an ordinary ship).
            if (ear != null) volume *= EngineVoices.Rolloff(Vector3.Distance(ear.position, transform.position) * ship.engineEarScale);
            // ... and deeper the heavier it is (ShipController.mass).
            float deep = Mathf.Lerp(1f, 0.6f, ship.mass);
            bool boosting = ship.Model != null && ship.Model.LooksBoosting;   // the multiplayer cruise mode sounds like a boost too
            if (modSounds != null) { UpdateOwn(on, boosting, ear, deep); return; }
            // 1104: one layer with the pitch; 1106 / 1107: EngineDLC_07 plain + the pitched second layer.
            Drive(main, on, volume, (engine >= 5 ? 1f : pitch) * deep);
            Drive(extra, on, volume, pitch * deep);

            if (boosting && !wasBoosting && boostSource != null && StartClip(out int bi) is AudioClip startClip)
            {
                boostSource.pitch = Mathf.Lerp(1f, 0.7f, ship.mass);   // 1 for every ordinary ship, as before
                boostSource.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(startClip), BoostVolume[bi] * Sfx.EventGain * Settings.SfxVolume);
            }
            wasBoosting = boosting;
        }

        /// <summary>Remake mods: the ship's own sounds (ModShipSounds). The engine by speed (0 at a standstill, 1 at top speed),
        /// the boost's start / loop / stop, the booster's recharge loop; each at the FX volume x the camera's distance.</summary>
        void UpdateOwn(bool on, bool boosting, Transform ear, float deep)
        {
            var model = ship.Model;
            float gain = Settings.SfxVolume * (ear != null ? EngineVoices.Rolloff(Vector3.Distance(ear.position, transform.position) * ship.engineEarScale) : 1f);
            float top = model != null ? Mathf.Max(0.01f, model.TopSpeed) : 1f;
            float speed = model == null ? 1f : Mathf.Clamp01((model.FreeFlight ? model.FreeVelocity.magnitude : model.MoveSpeed) / top);
            var e = modSounds.engine;
            if (e != null && main != null && engine < 0) Drive(main, on, e.Volume(speed) * gain, e.Pitch(speed) * deep);
            else
            {
                // The game's engine with the ship's own boost sounds.
                var stick = ship.SteerInput;
                float v = Mathf.Clamp01(Mathf.Max(Mathf.Abs(stick.x), Mathf.Abs(stick.y)));
                float pitch = engine >= 4 ? Mathf.Pow(2f, 8f * Mathf.Lerp(0.479167f, 0.520833f, v) - 4f)
                            : engine == 1 ? PitchEnvelope(v, 0.489583f, 0.510417f) : PitchEnvelope(v, 0.479167f, 0.520833f);
                float volume = (engine >= 0 ? EngineVolume[engine] : 0f) * LoadGain * Sfx.EventGain * gain;
                Drive(main, on, volume, (engine >= 5 ? 1f : pitch) * deep);
                Drive(extra, on, volume, pitch * deep);
            }
            float boostLevel = model != null ? model.BoostVisualPercent : 0f;
            if (boostLoop != null) Drive(boostLoop, on && boosting, modSounds.boostLoop.Volume(boostLevel) * gain, modSounds.boostLoop.Pitch(boostLevel) * deep);
            if (boostSource != null && on && boosting != wasBoosting)
            {
                var s = boosting ? modSounds.boostStart : modSounds.boostStop;
                if (s?.Clip is AudioClip clip)
                {
                    boostSource.pitch = s.RandomPitch * deep * TimeExtender.SoundPitch;
                    boostSource.PlayOneShot(clip, s.vol0 * gain);
                }
                else if (boosting && s == null && modSounds.boostStart == null && StartClip(out int bi) is AudioClip startClip && bi >= 0)
                {
                    boostSource.pitch = deep;
                    boostSource.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(startClip), BoostVolume[bi] * Sfx.EventGain * gain);
                }
            }
            wasBoosting = boosting;
            if (charger != null)
            {
                float refill = model != null ? model.BoostRechargePercent : 1f;
                bool charging = on && !boosting && model != null && model.HasBooster && refill < 0.999f;
                Drive(charger, charging, modSounds.charger.Volume(refill) * gain, modSounds.charger.Pitch(refill));
            }
        }

        static float PitchEnvelope(float v, float low, float high) =>
            Mathf.Pow(2f, 8f * (v < 0.5f ? Mathf.Lerp(low, 0.5f, v * 2f) : Mathf.Lerp(0.5f, high, v * 2f - 1f)) - 4f);

        static void Drive(AudioSource s, bool on, float volume, float pitch)
        {
            if (s == null) return;
            s.volume = volume;
            s.pitch = pitch * TimeExtender.SoundPitch;
            if (on && !s.isPlaying) s.Play();
            else if (!on && s.isPlaying) s.Stop();
        }
    }
}
