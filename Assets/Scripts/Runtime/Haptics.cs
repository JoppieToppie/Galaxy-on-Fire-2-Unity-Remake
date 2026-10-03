// Haptics.cs
// Remake-only: haptic feedback on the controller (the Input System's dual-motor rumble: Xbox / XInput pads, DualShock 4
// and DualSense over USB or Bluetooth, Steam Input's virtual pad) and the phone's vibration motor (PhoneVibrator, Android).
// The original has none (AbyssEngine::Engine::Vibrate is an empty stub, Globals::init switches it off). Created by
// Bootstrap and kept for the whole run.
//   output     follows InputMode: the controller last used while playing with a controller, the phone while playing by
//              touch (touch or tilt steering), nothing with keyboard and mouse
//   strength   Settings.HapticsIntensity (Options > Controls "Vibration", 0 = off) scales every motor and phone amplitude
//   events     Play(pulse) for one-off events (the presets below, called by the game code), Rumble(level) every frame for
//              continuous ones (ChaseCamera's explosion / boost / Liberator rumble, CutsceneCamera's rumble, scraping a
//              hull, the wormhole's pull, the Khador charge, the mining drill)
//   pause      everything stops while the game is paused (Navigation.InputHalted: menus, maps, hints, multiplayer menus),
//              the sound is paused, or the window / app loses focus; only the options' preview plays meanwhile
// The controller's motors run on the real clock (fast-forward and the time extender don't stretch a pulse); the phone gets
// one-shots (PhoneVibrator), the continuous rumble as short overlapping ones while it is strong enough to be felt.

using GoF2Remake.Data;
using GoF2Remake.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class Haptics : MonoBehaviour
    {
        // ---- presets: controller (low motor, high motor, ms), phone (ms, amplitude); phone 0 = controller only ----------

        /// <summary>Every primary shot (WeaponSystem, like TargetFollowCamera::hitSmall): a light tick, not on the phone.</summary>
        public static readonly HapticPulse PrimaryShot = new HapticPulse(0f, 0.14f, 40f);
        /// <summary>A missile, bomb, mine or sentry launched.</summary>
        public static readonly HapticPulse SecondaryFire = new HapticPulse(0.35f, 0.5f, 160f, 35f, 0.55f);
        /// <summary>A ship lock completed (CombatRadar, sound 26).</summary>
        public static readonly HapticPulse TargetLock = new HapticPulse(0f, 0.3f, 35f, 12f, 0.3f);
        /// <summary>Player hits by the layer they hit (PlayerHealth, sounds 25 / 23 / 24).</summary>
        public static readonly HapticPulse HitShield = new HapticPulse(0.12f, 0.4f, 120f, 20f, 0.3f);
        public static readonly HapticPulse HitArmor = new HapticPulse(0.4f, 0.55f, 180f, 40f, 0.55f);
        public static readonly HapticPulse HitHull = new HapticPulse(0.7f, 0.75f, 260f, 60f, 0.8f);
        /// <summary>Ramming an asteroid or running into a station, gate or freighter (PlayerCollision).</summary>
        public static readonly HapticPulse Impact = new HapticPulse(0.85f, 0.6f, 320f, 70f, 1f);
        /// <summary>The booster fires (the boost's own rumble follows through ChaseCamera).</summary>
        public static readonly HapticPulse Boost = new HapticPulse(0.35f, 0.2f, 220f, 30f, 0.45f);
        /// <summary>The hull gives out (the tumble begins) and the ship's explosion 3 s later (PlayerHealth).</summary>
        public static readonly HapticPulse Crippled = new HapticPulse(0.6f, 0.4f, 500f, 120f, 0.7f);
        public static readonly HapticPulse Death = new HapticPulse(1f, 1f, 1200f, 350f, 1f);
        /// <summary>A jumpgate opening, the Khador Drive's jump, a planet jump.</summary>
        public static readonly HapticPulse Jump = new HapticPulse(0.6f, 0.45f, 700f, 160f, 0.75f);
        /// <summary>Mining: touching down on the asteroid, a new ton, a layer drilled, the drill off target (repeated),
        /// the game won / lost.</summary>
        public static readonly HapticPulse MiningLanding = new HapticPulse(0.45f, 0.25f, 300f, 60f, 0.55f);
        public static readonly HapticPulse DrillTon = new HapticPulse(0f, 0.22f, 30f, 10f, 0.25f);
        public static readonly HapticPulse DrillLayer = new HapticPulse(0.3f, 0.35f, 150f, 35f, 0.5f);
        public static readonly HapticPulse DrillStutter = new HapticPulse(0.3f, 0.1f, 80f, 20f, 0.3f);
        public static readonly HapticPulse MiningWon = new HapticPulse(0.4f, 0.6f, 400f, 80f, 0.7f);
        public static readonly HapticPulse MiningLost = new HapticPulse(0.6f, 0.3f, 450f, 120f, 0.7f);
        /// <summary>The options' slider: a sample at the new strength.</summary>
        public static readonly HapticPulse PreviewPulse = new HapticPulse(0.6f, 0.6f, 180f, 50f, 0.8f);

        /// <summary>The phone takes the continuous rumble only from this strength (after the option): a weak, steady buzz
        /// through the whole boost or drilling would only be annoying on a phone.</summary>
        const float PhoneRumbleMinimum = 0.2f;
        const float PhoneRumbleMs = 110f, PhoneRumbleEvery = 0.09f;   // overlapping one-shots: no gaps
        const float PreviewEvery = 0.2f;

        public enum Output { None, Controller, Phone }

        static Haptics instance;
        static readonly HapticMixer mixer = new HapticMixer();
        static Gamepad pad;
        static float sentLow = -1f, sentHigh = -1f, nextPhoneRumble, nextPreview, resendAt, lastRunning;
        const float ResendEvery = 0.5f, StopResendFor = 2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            mixer.Clear();
            pad = null;
            sentLow = sentHigh = -1f;
            nextPhoneRumble = nextPreview = resendAt = lastRunning = 0f;
        }

        public static void Install()
        {
            if (instance != null) return;
            var go = new GameObject("Haptics");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Haptics>();
        }

        /// <summary>Where haptics go right now: the last used controller, the phone, or nowhere (keyboard and mouse, no
        /// motor, the option at 0).</summary>
        public static Output Route
        {
            get
            {
                if (Settings.HapticsIntensity <= 0f) return Output.None;
                switch (InputMode.Current)
                {
                    case InputKind.Gamepad: return Gamepad.current != null ? Output.Controller : Output.None;
                    case InputKind.Touch: return PhoneVibrator.Supported ? Output.Phone : Output.None;
                    default: return Output.None;
                }
            }
        }

        /// <summary>The game is held: paused, a menu or map over it, the sound paused, or the app in the background.</summary>
        static bool Halted => Navigation.InputHalted || AudioListener.pause || !Application.isFocused;

        /// <summary>A one-off haptic event; 'scale' weakens it (a distant blast).</summary>
        public static void Play(in HapticPulse pulse, float scale = 1f) => Start(scale >= 1f ? pulse : pulse.Scaled(scale), false);

        /// <summary>A continuous rumble for this frame, 0..1 (call every frame while it lasts; the strongest of the frame wins).</summary>
        public static void Rumble(float level)
        {
            if (level <= 0f || Halted) return;
            mixer.Rumble(level);
        }

        /// <summary>The options' slider moved: a sample at the new strength (also in the pause menu), at most every 0.2 s.</summary>
        public static void Preview()
        {
            if (Time.unscaledTime < nextPreview) return;
            nextPreview = Time.unscaledTime + PreviewEvery;
            Start(PreviewPulse, true);
        }

        static void Start(in HapticPulse pulse, bool ui)
        {
            if (instance == null || (!ui && Halted)) return;
            var route = Route;
            if (route == Output.None) return;
            mixer.Add(pulse, ui);
            if (route == Output.Phone && pulse.phoneMs > 0f)
                PhoneVibrator.Pulse(pulse.phoneMs, pulse.phoneAmplitude * Settings.HapticsIntensity);
        }

        void Update()
        {
            if (Halted) mixer.ClearGame();
            float k = Settings.HapticsIntensity;
            var motors = mixer.Evaluate(Time.unscaledDeltaTime * 1000f) * k;
            var route = Route;

            // Controller: only the one in use rumbles; the one before is stopped when another takes over.
            var target = route == Output.Controller ? Gamepad.current : null;
            if (target != pad)
            {
                if (pad != null && pad.added) pad.SetMotorSpeeds(0f, 0f);
                pad = target;
                sentLow = sentHigh = -1f;
                resendAt = 0f;
            }
            if (pad != null) SetMotors(motors.x, motors.y);

            // Phone: the continuous rumble as overlapping short one-shots.
            if (route == Output.Phone)
            {
                float r = mixer.LastRumble * k;
                if (r >= PhoneRumbleMinimum && Time.unscaledTime >= nextPhoneRumble)
                {
                    nextPhoneRumble = Time.unscaledTime + PhoneRumbleEvery;
                    PhoneVibrator.Pulse(PhoneRumbleMs, r);
                }
            }
        }

        /// <summary>Motor speeds to the pad, only when they changed (every change is an output report to the device).</summary>
        static void SetMotors(float low, float high)
        {
            if (pad == null || !pad.added) return;
            low = Mathf.Clamp01(low);
            high = Mathf.Clamp01(high);
            float now = Time.unscaledTime;
            if (!HapticMixer.ShouldSend(low, high, sentLow, sentHigh) && now < resendAt) return;
            sentLow = low;
            sentHigh = high;
            // Sent again every ResendEvery while running and for StopResendFor after a stop: a backend that drops a command
            // (the Xbox's, which kept a motor running until the guide was opened) catches up.
            bool stop = low <= 0f && high <= 0f;
            if (!stop) lastRunning = now;
            resendAt = stop && now - lastRunning > StopResendFor ? float.MaxValue : now + ResendEvery;
            // An explicit zero, not ResetHaptics: that skips the command when the device's own copy already reads 0.
            pad.SetMotorSpeeds(low, high);
        }

        /// <summary>Everything off at once (focus lost, quitting, leaving Play mode: a motor left on keeps running).</summary>
        public static void StopAll()
        {
            mixer.Clear();
            pad = null;
            sentLow = sentHigh = -1f;
            resendAt = 0f;
            try { InputSystem.ResetHaptics(); }
            catch (System.Exception) { }   // the Input System already shut down (quitting)
            PhoneVibrator.Cancel();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) StopAll();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) StopAll();
        }

        void OnDestroy()
        {
            StopAll();
            if (instance == this) instance = null;
        }
    }
}
