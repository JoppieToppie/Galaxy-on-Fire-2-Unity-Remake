// TimeExtender.cs
// The Time Extender (item 184 Rhoda Vortex, sort 26; Reference/research/combat_equipment.md 3.1).
//   MGame::OnInitialize 0x1a7300   duration attr 42 (15000 ms), cooldown attr 43 (30000 ms); ready at every level start
//   MGame::OnTouchBegin 0x1a8660   the fast-forward button's slot (HUD key 0x100) outside the autopilot / asteroid
//                                  approach: tap while ready starts it, tap while running ends it early
//   MGame::OnUpdate 0x1acb60       running: world dt = 0.3 dt (NPCs, bullets, particles, the mission clock and radio),
//                                  player dt = 0.7 dt (PlayerEgo::update, the chase camera); at the end the cooldown runs,
//                                  then the icon flashes for 2000 ms (Hud::setTimeExtender)
// Sounds: 1120 (TimeShift_Start) on start, every sound pitched down (FModSound::setDownPitch) while it runs, 1119
// (TimeShift_01b) when it ends (MGame::OnTouchBegin tapped again / MGame::OnUpdate the timer running out). The remake scales Time.timeScale to 0.3 (Navigation.ApplyTimeScale) and the player's own updates
// by PlayerFactor (0.7 / 0.3). Remake keys: the touch button's slot, X, left stick press.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class TimeExtender : MonoBehaviour
    {
        public const float WorldScale = 0.3f, PlayerScale = 0.7f, DownPitch = 0.7f;

        /// <summary>The extender runs: Time.timeScale is 0.3 and the player updates get PlayerFactor.</summary>
        public static bool Active { get; private set; }
        /// <summary>The player's own dt multiplier on top of Time.timeScale (0.7 of real time while running).</summary>
        public static float PlayerFactor => Active ? PlayerScale / WorldScale : 1f;
        /// <summary>For sources that set their own pitch every frame (engine, drill): FModSound::setDownPitch.</summary>
        public static float SoundPitch => Active ? DownPitch : 1f;

        /// <summary>Set by the level: no extender now (cinematics, jumps, death).</summary>
        public Func<bool> Blocked;

        float durationMs = 15000f, cooldownMs = 30000f;
        /// <summary>MGame+0x164: 0 ready, &gt; 0 running (ms left), &lt; 0 cooldown (counting toward -attr43).</summary>
        float state;
        float flashMs;
        static InputAction action => GameControls.TimeExtender;   // rebindable (X / left stick press)
        AudioSource sfx;
        CombatAssets assets;
        readonly Dictionary<AudioSource, float> pitched = new Dictionary<AudioSource, float>();

        public bool Running => state > 0f;
        public bool Ready => state == 0f;
        /// <summary>Hud::setTimeExtender flash: highlighted one frame in 80 ms for 2000 ms after the cooldown.</summary>
        public bool Flashing => flashMs > 0f && ((int)(flashMs / 80f) & 1) == 0;
        public float RechargeRate => state < 0f ? -state / cooldownMs : 1f;
        /// <summary>What is left of a running slow-down, 1 at its start, 0 when it ends.</summary>
        public float RunLeft => state > 0f && durationMs > 0f ? Mathf.Clamp01(state / durationMs) : 0f;

        public static TimeExtender Attach(GameObject player, Database db)
        {
            var item = Shop.FirstMounted(db, 26);
            // Multiplayer: the shared world can't slow down for one player (it would only slow their own ship).
            if (item == null || GoF2Remake.Data.NetSession.Active) return null;
            var t = player.AddComponent<TimeExtender>();
            t.durationMs = item.Attr(42, 15000);
            t.cooldownMs = item.Attr(43, 30000);
            return t;
        }

        void Awake()
        {
            Active = false;
            assets = CombatAssets.Load();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
        }

        void OnDestroy()
        {
            if (!Active) return;
            Stop(false);
            // Modified: a place loading while it runs (it now carries on through the jumps): the next scene starts at full
            // speed (a station scene doesn't set Time.timeScale itself; a level that does sets it again).
            if (Mathf.Approximately(Time.timeScale, WorldScale)) Time.timeScale = 1f;
        }

        /// <summary>The button: start when ready, end early when running.</summary>
        public void Toggle()
        {
            if (Time.timeScale <= 0f) return;
            if (Ready && (Blocked == null || !Blocked()))
            {
                state = durationMs;
                Active = true;
                if (assets != null && assets.timeShift != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(assets.timeShift), Settings.SfxVolume);
                SetPitch(true);
            }
            else if (Running) { Stop(true); PlayEnd(); }
        }

        void PlayEnd() { if (assets != null && assets.timeShiftEnd != null) sfx.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(assets.timeShiftEnd), Settings.SfxVolume); }

        /// <summary>A cinematic (MGame::OnUpdate 0x1af162), a jump or the player's death cancels it.</summary>
        public void Cancel() { if (Running) Stop(true); }

        void Stop(bool cooldown)
        {
            Active = false;
            state = cooldown ? -0.001f : 0f;
            SetPitch(false);
        }

        void Update()
        {
            if (action.WasPressedThisFrame()) Toggle();
            float dt = Time.unscaledDeltaTime * 1000f;
            if (Time.timeScale <= 0f) return;   // paused
            if (flashMs > 0f) flashMs -= dt;
            if (state > 0f)
            {
                state -= dt;
                if (state <= 0f) { Stop(true); PlayEnd(); }
                else if (pitched.Count > 0) PitchNew();
            }
            else if (state < 0f)
            {
                state -= dt;
                if (state < -cooldownMs || Cheats.NoEquipmentCooldown) { state = 0f; flashMs = 2000f; }   // remake debug: no cooldown
            }
        }

        void SetPitch(bool down)
        {
            if (down)
            {
                foreach (var s in FindObjectsByType<AudioSource>())
                    if (s != sfx && !pitched.ContainsKey(s)) { pitched[s] = s.pitch; s.pitch *= DownPitch; }
                if (pitched.Count == 0) pitched[sfx] = sfx.pitch;
                return;
            }
            foreach (var kv in pitched) if (kv.Key != null && kv.Key != sfx) kv.Key.pitch = kv.Value;
            pitched.Clear();
        }

        float scanMs;

        /// <summary>Sources created while running (explosions, new ships) get pitched too, checked twice a second.</summary>
        void PitchNew()
        {
            scanMs += Time.unscaledDeltaTime * 1000f;
            if (scanMs < 500f) return;
            scanMs = 0f;
            foreach (var s in FindObjectsByType<AudioSource>())
                if (s != sfx && !pitched.ContainsKey(s)) { pitched[s] = s.pitch; s.pitch *= DownPitch; }
        }
    }
}
