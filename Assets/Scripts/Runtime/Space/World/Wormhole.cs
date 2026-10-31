// Wormhole.cs
// The Void's wormhole, landmark 3 of an orbit (PlayerWormHole 0xb1d3c, mesh 16994 wormhole_anim_add; decoded from
// PlayerWormHole::update 0xb1e68 and PlayerEgo::calcCollision 0xab550):
//   Level::createSpace   exists until the game is won (index <= 44); visible when coming out of the Void, at the station
//                        the Void attack (Session.VoidInvasionStation) or in the alien orbit, index < 43; radius 40000,
//                        at (rand(80000) - 40000, rand(40000) - 20000, rand(40000) + 40000), looping animation
//   timer (+0x150)       counts up while visible: < 0 it grows (scale 1 - t / -3000), 0..60000 open, 60000..63000 it
//                        shrinks; past 63000 it vanishes, or (alien orbit / attacked station) reopens at once somewhere
//                        else: alien orbit x +-(rand(60000) + 30000), y rand(40000) + 20000, z -60000 - rand(40000), else
//                        +-(rand(40000) + 20000) per axis; at index 29 / 41 that offset x 2.7 from the player. The mission
//                        lock (+0x15c) keeps it from shrinking at index 40 (not in the alien orbit) and 42 (in it) and
//                        from reopening outside the alien orbit at 42; freeMissionLock releases it
//   reset(closing)       timer 0 (open for 60 s) or 59000 (shrinks in 1 s); open(): timer -3000, scale 0 (grows in 3 s)
//   drawing              turned to face the camera every frame (setDirection(normalize(camera - wormhole) + (0.5, 0, 0),
//                        up): a slight tilt), scaled by the scale (4096 = 1). Its two layers spin about the facing axis
//                        (file rotY, -2 pi per 20 s / 10 s); PartAnimation's default mapping turned that spin the wrong
//                        way round (the import's 180 deg yaw reverses a spin about Z), so the wormhole flips it
//   PlayerEgo::calcCollision  visible, not shrinking, within 40000: the loop sound 34 at the wormhole, the player moved
//                        toward it by (40000 - d) / 256 units per original frame, camera hit(); within 1000 = inside
//                        (PlayerEgo+0x25, isInWormhole); the ride itself is SpaceLevel's (MGame::OnUpdate)
// Remake events / missions (EventWormhole, /wormhole): a 'held' wormhole stays open until Close() shrinks it away, with no
// story rules; SpaceLevel rides it to its own destination.

using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public class Wormhole : MonoBehaviour
    {
        const float M = OrbitLayout.MetersPerUnit;
        public const float RadiusUnits = 40000f, InsideUnits = 1000f;
        const int OpenMs = 60000, ShrinkMs = 3000, GrowMs = 3000;

        int timer;          // +0x150
        int scale = 4096;   // +0x154, 4096 = full size
        bool missionLock = true;   // +0x15c
        GameObject model;
        Vector3 baseScale = Vector3.one;
        AudioSource loop;

        public bool Visible { get; private set; }
        /// <summary>isShrinking: past its 60 s (no pull any more).</summary>
        public bool Shrinking => timer > OpenMs;
        public Vector3 GamePosition => new Vector3(transform.position.x, transform.position.y, -transform.position.z) / M;
        /// <summary>Set by the level: the alien orbit, the attacked station (it reopens instead of vanishing).</summary>
        public bool alienOrbit, attackedStation;
        /// <summary>The player (for the index 29 / 41 relocation near the player).</summary>
        public Transform player;
        /// <summary>Remake events: open until Close(), never relocated, no mission lock.</summary>
        public bool held;
        bool closing;

        public static Wormhole Spawn(StoryAssets assets, Vector3 gamePosition, bool visible)
        {
            var go = new GameObject("Wormhole");
            var w = go.AddComponent<Wormhole>();
            if (assets != null && assets.wormhole != null)
            {
                w.model = Instantiate(assets.wormhole, go.transform, false);
                w.baseScale = w.model.transform.localScale;
                foreach (var a in w.model.GetComponentsInChildren<PartAnimation>(true))
                {
                    a.loop = true;   // animation state 2 (the spin's direction: PartAnimation's rotation map)
                }
            }
            w.loop = go.AddComponent<AudioSource>();
            w.loop.playOnAwake = false;
            w.loop.loop = true;
            w.loop.clip = GoF2Remake.Modding.ModSounds.Get(assets != null ? assets.wormholeSound : null);
            w.loop.spatialBlend = 1f;
            w.loop.rolloffMode = AudioRolloffMode.Linear;
            w.loop.minDistance = 100f;
            w.loop.maxDistance = RadiusUnits * M;
            w.SetPosition(gamePosition);
            w.SetVisible(visible);
            return w;
        }

        public void SetPosition(Vector3 game) => transform.position = OrbitLayout.ToUnity(game);

        /// <summary>KIPlayer::setVisible.</summary>
        public void SetVisible(bool on)
        {
            Visible = on;
            if (model != null) model.SetActive(on);
            if (!on) StopSound();
        }

        /// <summary>PlayerWormHole::reset: open for 60 s again, or ('closing') shrinking after 1 s.</summary>
        public void ResetTimer(bool closing)
        {
            timer = closing ? 59000 : 0;
            scale = 4096;
            this.closing = closing;
        }

        /// <summary>PlayerWormHole::open: grows from nothing over 3 s.</summary>
        public void Open()
        {
            timer = -GrowMs;
            scale = 0;
            closing = false;
        }

        /// <summary>Remake events: a held wormhole shrinks away over 3 s.</summary>
        public void Close()
        {
            if (!Visible || closing) return;
            closing = true;
            if (timer < OpenMs) timer = OpenMs;
        }

        public void FreeMissionLock() => missionLock = false;

        /// <summary>The pull's loop sound (calcCollision): on while the player is inside the 40000 radius.</summary>
        public void SetSound(bool on)
        {
            if (loop == null || loop.clip == null) return;
            if (on && !loop.isPlaying) { loop.volume = Settings.SfxVolume; loop.Play(); }
            else if (!on && loop.isPlaying) loop.Stop();
        }

        void StopSound() { if (loop != null && loop.isPlaying) loop.Stop(); }

        void Update()
        {
            if (!Visible) return;
            int dt = Mathf.RoundToInt(Time.deltaTime * 1000f);
            timer += dt;
            int index = Story.Index;
            if (timer < 0) scale = 4096 - (int)(-timer / (float)GrowMs * 4096f);
            else if (held && !closing) { timer = 0; scale = 4096; }   // an event's: open until closed
            else if (timer > OpenMs)
            {
                bool locked = missionLock && !held && ((index == 42 && alienOrbit) || (index == 40 && !alienOrbit));
                if (locked) timer = OpenMs;
                scale = 4096 - (int)((timer - OpenMs) / (float)ShrinkMs * 4096f);
                if (timer > OpenMs + ShrinkMs)
                {
                    if (held || (!alienOrbit && !attackedStation)) { SetVisible(false); return; }
                    if (missionLock && index == 42 && !alienOrbit) { SetVisible(false); return; }
                    Relocate(index);
                }
            }
            scale = Mathf.Clamp(scale, 0, 4096);
        }

        /// <summary>Reopens at a new random spot (Globals::rnd, not seeded).</summary>
        void Relocate(int index)
        {
            timer = -GrowMs;
            float Sign() => Random.Range(0, 2) == 0 ? 1f : -1f;
            Vector3 p;
            if (alienOrbit) p = new Vector3(Sign() * (Random.Range(0, 60000) + 30000), Random.Range(0, 40000) + 20000, -60000 - Random.Range(0, 40000));
            else p = new Vector3(Sign() * (Random.Range(0, 40000) + 20000), Sign() * (Random.Range(0, 40000) + 20000), Sign() * (Random.Range(0, 40000) + 20000));
            if ((index == 29 || index == 41) && player != null)
            {
                var pp = player.position;
                p = new Vector3(pp.x, pp.y, -pp.z) / M + p * 2.7f;
            }
            SetPosition(p);
        }

        void LateUpdate()
        {
            if (!Visible || model == null) return;
            var cam = Camera.main;
            if (cam != null)
            {
                var d = cam.transform.position - transform.position;
                // PlayerWormHole::update: the direction to the camera, normalised, then x + 0.5 (game x = Unity x).
                if (d.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(d.normalized + new Vector3(0.5f, 0f, 0f), Vector3.up);
            }
            model.transform.localScale = baseScale * (scale / 4096f);
        }
    }
}
