// ModShipAnimator.cs
// Remake (2026-11-11, for Chorus's Forsaken): a mod ship's model plays named animations by what the ship is doing, not
// just its first clip on a loop (ModGltf.LoopAnimations). ships.json:
//   "animations": { "idle": "<clip>",
//                   "drift":  { "start": "<clip>", "loop": "<clip>", "end": "<clip>" },
//                   "travel": { "start": "<clip>", "loop": "<clip>", "end": "<clip>" },
//                   "weapons": { "start": "<clip>", "loop": "<clip>", "end": "<clip>" },
//                   "steerLeft": "<clip>", "steerRight": "<clip>", "thrust": "<clip>", "brake": "<clip>",
//                   "bankLeft": "<clip>", "bankRight": "<clip>",
//                   "dodgeLeft": { "start": "<clip>", "end": "<clip>" }, "dodgeRight": { ... },
//                   "driftDodgeLeft": "<clip>", "driftDodgeRight": "<clip>" }
// The clips are the model's glTF animations by name (glTFast puts them all on one legacy Animation).
//   idle     loops whenever no state plays (in flight, in the hangar, on NPC ships)
//   drift    Chorus flight's drift (FlightModel.Drifting), travel its travel boost (TravelBoosting): "start" once, "loop"
//            while it holds, "end" once as it stops, back to idle (or to the other state, if that one now holds)
//   weapons  on a layer of its own (1), while the player fires and 3 s after: "start" once, "loop", then "end" once,
//            which holds its last frame (the doors shut again)
//   steerLeft / steerRight / thrust / brake / bankLeft / bankRight: poses laid over whatever plays (layer 2), as much as
//            the player steers (the stick's turn), thrusts or brakes (bank: with the steering): each clip's last frame,
//            weighted 0..1 each frame
//   dodgeLeft / dodgeRight: Chorus flight's dash, once (layer 3: "start" then "end"); driftDodgeLeft / driftDodgeRight in
//            their place out of a drift
// Every layered clip should key only the parts it moves (the flaps, the doors), so the rest stay with the layers below.
// A clip a model lacks is skipped (no "start": straight to "loop"; no "end": a fade back). Node animations only: parts
// moved, turned or scaled, like the looping ones (bones don't play, ModShipBuilder.FlattenSkinned).

using System.Linq;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public class ModShipAnimator : MonoBehaviour
    {
        const float Fade = 0.12f;      // seconds of cross-fade between the clips
        const float PoseRate = 6f;     // how fast a pose follows the input (share per second)
        const float WeaponsHoldSeconds = 3f;   // the weapons stay out this long after the last shot

        public Animation anim;
        public string idle;
        public string[] drift = new string[3], travel = new string[3];   // start, loop, end
        public string[] weapons = new string[3], dodgeLeft = new string[3], dodgeRight = new string[3];
        public string steerLeft, steerRight, thrust, brake, bankLeft, bankRight, driftDodgeLeft, driftDodgeRight;
        bool drifting, travelling, armed;
        float wSteerL, wSteerR, wThrust, wBrake, armedUntil;

        /// <summary>Adds the animator to a built ship 'root' when its entry names animations; null when it names none or the
        /// model has no Animation (then the model's first clip loops as before).</summary>
        public static ModShipAnimator Attach(GameObject root, ShipAnimations spec)
        {
            if (spec == null || root == null) return null;
            var anim = root.GetComponentInChildren<Animation>(true);
            if (anim == null) return null;
            var a = root.AddComponent<ModShipAnimator>();
            a.anim = anim;
            a.idle = Has(anim, spec.idle);
            a.drift = State(anim, spec.drift);
            a.travel = State(anim, spec.travel);
            a.steerLeft = Pose(anim, spec.steerLeft); a.steerRight = Pose(anim, spec.steerRight);
            a.thrust = Pose(anim, spec.thrust); a.brake = Pose(anim, spec.brake);
            a.bankLeft = Pose(anim, spec.bankLeft); a.bankRight = Pose(anim, spec.bankRight);
            a.weapons = Layered(State(anim, spec.weapons), anim, 1);
            a.dodgeLeft = Layered(State(anim, spec.dodgeLeft), anim, 3); a.dodgeRight = Layered(State(anim, spec.dodgeRight), anim, 3);
            a.driftDodgeLeft = Layered(new[] { Has(anim, spec.driftDodgeLeft) }, anim, 3)[0];
            a.driftDodgeRight = Layered(new[] { Has(anim, spec.driftDodgeRight) }, anim, 3)[0];
            foreach (var n in a.dodgeLeft.Concat(a.dodgeRight).Concat(new[] { a.driftDodgeLeft, a.driftDodgeRight }))
                if (n != null) anim[n].wrapMode = WrapMode.Once;   // (a dodge ends: the layers below take over)
            if (a.idle != null) { anim[a.idle].wrapMode = WrapMode.Loop; anim.clip = anim[a.idle].clip; anim.Play(a.idle); }
            return a;
        }

        static string Has(Animation anim, string clip) => !string.IsNullOrEmpty(clip) && anim[clip] != null ? clip : null;

        static string[] State(Animation anim, ShipStateAnimation s)
        {
            var n = new[] { Has(anim, s?.start), Has(anim, s?.loop), Has(anim, s?.end) };
            if (n[0] != null) anim[n[0]].wrapMode = WrapMode.ClampForever;
            if (n[1] != null) anim[n[1]].wrapMode = WrapMode.Loop;
            if (n[2] != null) anim[n[2]].wrapMode = WrapMode.ClampForever;
            return n;
        }

        static string[] Layered(string[] clips, Animation anim, int layer)
        {
            foreach (var n in clips) if (n != null) { anim[n].layer = layer; anim[n].blendMode = AnimationBlendMode.Blend; }
            return clips;
        }

        static string Pose(Animation anim, string clip)
        {
            clip = Has(anim, clip);
            if (clip == null) return null;
            var st = anim[clip];
            st.layer = 2; st.blendMode = AnimationBlendMode.Blend; st.wrapMode = WrapMode.ClampForever;
            st.speed = 0f; st.normalizedTime = 1f; st.weight = 0f; st.enabled = false;
            return clip;
        }

        /// <summary>Chorus flight's drift began (true) or ended (false).</summary>
        public void SetDrift(bool on)
        {
            if (anim == null || on == drifting) return;
            drifting = on;
            Switch(on, drift);
        }

        /// <summary>Chorus flight's travel boost began (true) or ended (false).</summary>
        public void SetTravel(bool on)
        {
            if (anim == null || on == travelling) return;
            travelling = on;
            Switch(on, travel);
        }

        void Switch(bool on, string[] s)
        {
            if (on)
            {
                if (s[0] != null) { anim.CrossFade(s[0], Fade); if (s[1] != null) anim.CrossFadeQueued(s[1], Fade, QueueMode.CompleteOthers); }
                else if (s[1] != null) anim.CrossFade(s[1], Fade);
                return;
            }
            // back to the state that still holds, else idle
            string[] other = drifting ? drift : travelling ? travel : null;
            string after = other != null && other[1] != null ? other[1] : idle;
            if (s[2] != null) { anim.CrossFade(s[2], Fade); if (after != null) anim.CrossFadeQueued(after, Fade, QueueMode.CompleteOthers); }
            else if (after != null) anim.CrossFade(after, Fade);
        }

        /// <summary>The player fires (each frame): the weapons come out, and go back in WeaponsHoldSeconds after the last shot.</summary>
        public void SetFiring(bool firing, float now)
        {
            if (anim == null || weapons[1] == null && weapons[0] == null) return;
            if (firing) armedUntil = now + WeaponsHoldSeconds;
            bool on = now < armedUntil;
            if (on == armed) return;
            armed = on;
            var s = weapons;
            if (on)
            {
                if (s[0] != null) { anim.CrossFade(s[0], Fade); if (s[1] != null) anim.CrossFadeQueued(s[1], Fade, QueueMode.CompleteOthers); }
                else anim.CrossFade(s[1], Fade);
            }
            else if (s[2] != null) anim.CrossFade(s[2], Fade);   // (ClampForever: it holds the doors shut)
        }

        /// <summary>Chorus flight's dash began: -1 left, +1 right; out of a drift its own clip.</summary>
        public void Dodge(int dir)
        {
            if (anim == null) return;
            string single = drifting ? (dir < 0 ? driftDodgeLeft : driftDodgeRight) : null;
            if (single != null) { anim.CrossFade(single, 0.05f); return; }
            var s = dir < 0 ? dodgeLeft : dodgeRight;
            if (s[0] != null) { anim.CrossFade(s[0], 0.05f); if (s[2] != null) anim.CrossFadeQueued(s[2], 0.02f, QueueMode.CompleteOthers); }
            else if (s[2] != null) anim.CrossFade(s[2], 0.05f);
        }

        /// <summary>The poses over it, each frame: steer -1 (full left) .. 1 (full right), thrust and brake 0..1.</summary>
        public void SetPoses(float steer, float thrustIn, float brakeIn, float dt)
        {
            if (anim == null) return;
            float k = Mathf.Clamp01(PoseRate * dt);
            wSteerL = Mathf.Lerp(wSteerL, Mathf.Clamp01(-steer), k);
            wSteerR = Mathf.Lerp(wSteerR, Mathf.Clamp01(steer), k);
            wThrust = Mathf.Lerp(wThrust, Mathf.Clamp01(thrustIn), k);
            wBrake = Mathf.Lerp(wBrake, Mathf.Clamp01(brakeIn), k);
            Weigh(steerLeft, wSteerL); Weigh(steerRight, wSteerR); Weigh(thrust, wThrust); Weigh(brake, wBrake);
            Weigh(bankLeft, wSteerL); Weigh(bankRight, wSteerR);
        }

        void Weigh(string clip, float w)
        {
            if (clip == null) return;
            var st = anim[clip];
            bool on = w > 0.01f;
            if (st.enabled != on) st.enabled = on;
            st.normalizedTime = 1f; st.weight = w;
        }

        public bool Drifting => drifting;
        public bool Travelling => travelling;
        public bool Armed => armed;
    }
}
