// VolatileCargo.cs
// Volatile goods in the hold (Ship::hasVolatileGoods: items 204 / 209; PlayerEgo+0x398) and their "volatile force"
// (Player+0x60, 0..1), from PlayerEgo::update 0xa9xxx:
//   no volatile goods              the force is 0
//   every frame                    + dt * 0.001 * 0.5 * max(|d stick x|, |d stick y|) (the change of +0x268 / +0x270 since
//                                  the last frame; the remake counts it per 30 fps frame, so the total doesn't depend on
//                                  the frame rate)
//   while boosting                 + dt * 0.001 * 0.13 (0.17 with the Polytron Boost 195; DAT_000a9ccc / 0xa9cd0)
//   an asteroid touched            + 0.2 (PlayerEgo::calcCollision)
//   force >= 1, not invulnerable   Player::setHitpoints(0): the ship blows up
// Sound 35 Selfdestruct_Warning plays while the goods are aboard, its parameter SpawnIntensity = the force (the FEV's LGCY
// data): a 58 ms beep every 1000 ms / intensity, the envelope 0 below 0.2 then 0.18 -> 0.66 at 1 (libfmodevent maps an
// envelope value v to the intensity (e^(5.7865 v) - 1) * 0.3078, 0..100: 0.58x at 0.2, 14x at 1, so the beeps run from
// every 1.7 s to almost continuous; the remake took the value as the factor until 2026-10), pitch x1 -> x1.189 (+3 st)
// from 0.18 to 1, at most 2 at a time; event volume 0.2. Also: every dodge request + 0.17 (PlayerEgo::initManeuver) and a
// player bomb's ignition + 3 f (PlayerEgo::addNukeVolatileForce, WeaponSystem.OnIgnited), every shot + 0.008 (Player::shoot),
// every hit taken + 0.065 (Player::damage); it decays by 0.025 per second (Player::update).

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class VolatileCargo : MonoBehaviour
    {
        const float InputRate = 0.001f * 0.5f * (1000f / 30f), EventVolume = 0.2007f, SpawnMs = 1000f;

        ShipController ship;
        PlayerHealth health;
        AudioSource beep;
        AudioClip clip;
        float boostRate = 0.13f, sinceBeepMs;
        Vector2 lastStick;
        bool had;

        /// <summary>Player+0x60: 0..1, the ship explodes at 1.</summary>
        public float Force { get; private set; }

        /// <summary>PlayerEgo::initManeuver (+0.17) / addNukeVolatileForce (+3 f): only with volatile goods aboard.</summary>
        public void Add(float amount) { if (GalaxyMap.HasVolatileGoods) Force += amount; }

        public static VolatileCargo Attach(GameObject player, Database db, ShipController ship)
        {
            var v = player.AddComponent<VolatileCargo>();
            v.ship = ship;
            v.health = player.GetComponent<PlayerHealth>();
            var booster = Shop.FirstMounted(db, 14);
            v.boostRate = booster != null && booster.index == 195 ? 0.17f : 0.13f;
            v.clip = GoF2Remake.Modding.ModSounds.Get(SupernovaAssets.Load()?.selfDestruct);
            v.beep = player.AddComponent<AudioSource>();
            v.beep.playOnAwake = false;
            v.beep.spatialBlend = 0f;
            var collision = player.GetComponent<PlayerCollision>();
            if (collision != null) collision.AsteroidHit += () => v.Add(0.2f);
            ship.DodgeRequested += () => v.Add(0.17f);   // PlayerEgo::initManeuver(1 / 2), even while a dodge runs
            return v;
        }

        void Update()
        {
            if (ship == null) return;
            if (health == null) health = GetComponent<PlayerHealth>();
            float dtMs = Time.deltaTime * 1000f * TimeExtender.PlayerFactor;
            var stick = ship.SteerInput;
            bool has = GalaxyMap.HasVolatileGoods;
            if (!has || (health != null && health.Dead)) { Force = 0f; lastStick = stick; had = has; return; }
            if (!had) sinceBeepMs = 0f;
            had = true;
            if (dtMs <= 0f) return;
            Force += InputRate * Mathf.Max(Mathf.Abs(stick.x - lastStick.x), Mathf.Abs(stick.y - lastStick.y));
            lastStick = stick;
            if (ship.Model != null && ship.Model.IsBoosting) Force += dtMs * 0.001f * boostRate;
            Force = Mathf.Max(0f, Force - dtMs * 0.001f * 0.025f);   // Player::update: -0.025 per second
            if (Force >= 1f && health != null && !health.invulnerable && !Cheats.GodMode) { health.Kill(); return; }

            // The warning beeps (spawn intensity and pitch envelopes of event 35).
            float p = Mathf.Clamp01(Force);
            float envelope = p < 0.2f ? 0f : Mathf.Lerp(0.183544f, 0.663453f, (p - 0.2f) / 0.8f);
            float intensity = (Mathf.Exp(5.786521f * envelope) - 1f) * 0.30780852f;   // FMOD's spawn intensity scale
            sinceBeepMs += dtMs;
            if (intensity > 0f && sinceBeepMs >= SpawnMs / intensity && clip != null)
            {
                sinceBeepMs = 0f;
                float v = p < 0.184486f ? 0.5f : Mathf.Lerp(0.5f, 0.53125f, (p - 0.184486f) / (1f - 0.184486f));
                beep.pitch = Mathf.Pow(2f, 8f * v - 4f) * TimeExtender.SoundPitch;
                beep.PlayOneShot(GoF2Remake.Modding.ModSounds.Get(clip), EventVolume * Sfx.EventGain * Settings.SfxVolume);
            }
        }
    }
}
