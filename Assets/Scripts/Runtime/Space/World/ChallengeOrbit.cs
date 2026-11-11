// ChallengeOrbit.cs
// The Supernova Challenge's orbit (Reference/research/supernova_challenge.md 3-7; the rules and score in
// Data.SupernovaChallenge): Level::createMission's 0xb7 branch 0xbed0a and Level::updateMissionOrbit 0xd4bd4 in Luur's orbit.
//   [0-7]   8 Specters (createShip(10, 0, 0x2c) at (-55000, 0, -35000) +- 20000 per axis, always enemy, hull 250 (setMaxHitpoints),
//           no loot, no route: they go for the player)
//   [8]     the original's code: the burning "Station Platform" (0x495d, text 3208) at the origin, turned (0, -2.35619, 0)
//   [9]     and a Terran carrier (0x4974) at (-70000, -5000, -35000)
//           both radar-hidden (KIPlayer+0x70), race 10 (the Specters leave them alone), 9 999 999 hull: scenery
//   [10-13] and 4 unarmed Rhinos (race 0, ship 0x33) shuttling carrier -> (30000, 1000, 40000) -> platform (Route(..., 9))
//           The remake leaves [8-13] out (below).
// Every 7500 ms each Specter whose explosion is over comes back at full hull 50 000-80 000 units from the player on x and z,
// 10 000-20 000 above or below (updateMissionOrbit). The player starts at (-30000, 5000, -100000) (0xbf102). The round ends
// after SupernovaChallenge.RoundMs (151 000 ms, the original's 2:31; Android's code has 181 000); the original's success
// page then restarts it (MGame::OnTouchEnd). Remake: the end asks "Play again?" (300)
// with the score, the kills and the highscore (294 / 295 / 296; the original's dead ChoiceWindow 1.2 had that shape), also
// after the player's death; the Specters fire their ordinary guns (assignGuns' damage 1 then the Specters' x0.7 made 0 in the
// Android build: harmless). Remake pick: the player faces the Specters' spawn point (the original's heading wasn't traced).
// Modified (#179, the user's comparison with the original's video): no platform, carrier or Rhinos (the original's round
// shows none of them: the Specters and the player alone at Luur, ringed with Novanium's magma asteroids,
// OrbitLayout.ChallengeNovaniumOrbit), and the round opens with the arrival fly-in (SpaceLevel's launch camera, close: 150 m
// ahead, over once the ship passes it; fire or any key skips). The first wave flies fast (FirstLegMs) to one point
// (FirstWavePoint), then attacks the player, easing down to its own speed (FirstWaveSlowMs); a Specter that comes back
// (Revive) has no order left and goes for the player as before.
// Modified (the iOS build's createMission 0xb7, ported by the user's 64-bit bridge work): the round is 151 000 ms; the
// Specters start at eight fixed points (IosSpecterStarts) with 300 hull, can't cloak (setCloakingPossible(false)), have no
// route and engage from anywhere (their attack range 500 000 instead of 50 000, SpecterDetectUnits); the first wave's
// point is the iOS route's first point (-40 000, 0, -15 000); the
// player starts at z -80 000; Radar::drawCurrentLock draws nothing (no lock ring or "??? NN%" plate: CombatView); the
// Specters' guns keep their damage (the iOS assignGuns ignores the challenge's damage 1).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoF2Remake.World
{
    public class ChallengeOrbit : MonoBehaviour
    {
        const float M = 0.05f;
        const float WaveMs = 7500f;        // Level+0x174's period (0x1d4c)
        const float FirstLegMs = 5000f;    // Modified: the first wave's run to its point (the original's video: about 5 s)
        // Modified: the first wave's one point (Unity m) and how near counts as there (300 m). The iOS build's
        // Level+0x110 route starts at (-40 000, 0, -15 000) (Android's (-55 000, 0, -35 000)): its Specters gather there before
        // attacking (the user's test of the iOS logic); before, the user's pick in the Editor from the original's video,
        // (-1975, -379.65, 91) m.
        static readonly Vector3 FirstWavePoint = ToUnity(new Vector3(-40000, 0, -15000));
        const float FirstWaveReachMeters = 300f;
        // Modified: after the point the first wave eases from its fast run down to its own speed over this long.
        const float FirstWaveSlowMs = 2000f;
        readonly Dictionary<NpcShip, (NpcOrder order, float from, float ms)> slowing = new Dictionary<NpcShip, (NpcOrder, float, float)>();
        readonly List<NpcShip> slowingKeys = new List<NpcShip>(), slowingDone = new List<NpcShip>();   // UpdateSlowing's, reused
        // The speed of each first-wave Specter's run to its point (UpdateFirstWave carries it into the attack).
        readonly Dictionary<NpcShip, float> lastRunSpeed = new Dictionary<NpcShip, float>();
        const float EndDelayMs = 1500f;    // remake: the question after the time-out (the original's successCheck ran every ~5 s)
        const float DeathDelayMs = 7000f;  // the game over's "Tap to ..." moment (FlightHud)
        const float SoundVolume = 0.42658f;   // the SV_Mode events' volume (supernova_challenge.md 6.3)

        static readonly Vector3 SpecterPoint = new Vector3(-55000, 0, -35000);
        // Modified: the iOS build's player start (z -80 000; Android's code has -100 000).
        static readonly Vector3 PlayerStart = new Vector3(-30000, 5000, -80000);
        /// <summary>Modified: the iOS build's eight Specter starts (game units; Android's code throws them round SpecterPoint
        /// +- 20 000).</summary>
        static readonly Vector3[] IosSpecterStarts =
        {
            new Vector3(-44299, 1507, -27330), new Vector3(-26881, -19046, -8000), new Vector3(-46732, -5078, -10557),
            new Vector3(-23820, 18543, -27063), new Vector3(-32703, -1456, -33452), new Vector3(-42863, 11801, -29354),
            new Vector3(-23002, -8641, -5828), new Vector3(-46488, 5601, -20346),
        };
        const int SpecterHull = 300;   // Modified: the iOS build's setMaxHitpoints(300) (Android's 250)
        /// <summary>Modified: the iOS build's attack range for the round's Specters (PlayerFighter+0x180 = 500 000; 50 000 by
        /// default): they go for the player from anywhere in the arena, also when they come back 50 000-80 000 away.</summary>
        const float SpecterDetectUnits = 500000f;

        SpaceLevel level;
        Traffic traffic;
        readonly List<NpcShip> specters = new List<NpcShip>();
        // Modified: the first wave flies on to the point it heads for at the start, then goes for the player (UpdateFirstWave).
        readonly List<NpcShip> firstWave = new List<NpcShip>();
        AudioSource sfx;
        // Modified: the round's clock ran out: the Khador Drive's jump scene plays, its end asks the question (jumpedOut).
        bool jumping, jumpedOut;
        AudioClip[] sounds;
        float waveMs, endMs;
        bool asked;

        /// <summary>The round is over and its question is up or coming (FlightHud leaves the game over's tap alone).</summary>
        public bool Ending => SupernovaChallenge.Over;

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;
        static float Sign() => Random.Range(0, 2) == 0 ? 1f : -1f;

        public void Setup(SpaceLevel spaceLevel, Traffic npcTraffic)
        {
            level = spaceLevel;
            traffic = npcTraffic;
            sounds = SupernovaAssets.Load()?.challengeSounds;
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            for (int i = 0; i < 8; i++)
            {
                var s = traffic.SpawnShip(new SpawnSpec { group = NpcGroup.Raider, race = Standing.Specter, ship = 44, position = IosSpecterStarts[i],
                                                          alwaysEnemy = true, hitpoints = SpecterHull, noLoot = true });
                if (s == null) continue;
                s.CloakingPossible = false;   // Modified: the iOS build's setCloakingPossible(false)
                s.detectRange = SpecterDetectUnits;
                specters.Add(s);
                // Modified: the round's first Specters fly to one fixed point (FirstWavePoint; they used to aim at their own
                // patrol route's first point and turn on the player before reaching it), done within 300 m, then attack
                // (UpdateFirstWave). The later waves come back around the player and go for it as before.
                // As in the original they cover the way in about FirstLegMs (fast: tens of km), facing it from the start, then
                // fly at their own speed again.
                {
                    var point = FirstWavePoint;
                    var way = point - s.transform.position;
                    float units = way.magnitude / M;
                    var go = new NpcOrder { kind = NpcOrder.Kind.Move, then = NpcOrder.Then.Resume, radius = FirstWaveReachMeters / M, speed = units / FirstLegMs };
                    go.points.Add(point);
                    if (way.sqrMagnitude > 1e-6f) s.transform.rotation = Quaternion.LookRotation(way, Vector3.up);
                    s.SetOrder(go);
                    firstWave.Add(s);
                }
            }
            // Modified: the burning platform, the carrier and the four Rhinos aren't spawned: the original's round has none of
            // them (the user's comparison with its video).
            traffic.ConnectPlayers();
            traffic.ShipDied += OnShipDied;
            var start = ToUnity(PlayerStart);
            level.MovePlayer(start, Quaternion.LookRotation((ToUnity(SpecterPoint) - start).normalized, Vector3.up));
            Debug.Log($"ChallengeOrbit: round {SupernovaChallenge.Round}, {specters.Count} Specters");
        }

        void OnDestroy()
        {
            if (traffic != null) traffic.ShipDied -= OnShipDied;
        }

        /// <summary>Level::enemyDied(i, byNpc = false): the round's kills (Level+0x24); the score and combo (remake).</summary>
        void OnShipDied(NpcShip ship, bool byPlayer)
        {
            if (byPlayer && specters.Contains(ship)) SupernovaChallenge.OnKill();
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f;
            bool dead = level.Health != null && level.Health.Dead;
            if (SupernovaChallenge.Over)
            {
                endMs += Time.unscaledDeltaTime * 1000f;
                if (jumping) { if (jumpedOut && !asked) Ask(); return; }
                if (!asked && endMs >= (dead ? DeathDelayMs : EndDelayMs)) Ask();
                return;
            }
            if (dead) { End(); return; }
            if (dtMs <= 0f) return;
            SupernovaChallenge.Tick(dtMs);
            PlaySound(SupernovaChallenge.TakeSound());
            UpdateFirstWave();
            UpdateSlowing(dtMs);
            UpdateWaves(dtMs);
            if (SupernovaChallenge.TimeUp)
            {
                End();
                // Modified: the ship jumps out with the Khador Drive's scene; the results come at its end.
                if (level.SystemJump != null) { jumping = true; level.SystemJump.PlayKhadorScene(() => jumpedOut = true); }
            }
        }

        /// <summary>Modified: a first-wave Specter that reached its point (its Move order over) attacks the player.</summary>
        void UpdateFirstWave()
        {
            var player = level.Health != null ? level.Health.Target : null;
            for (int i = firstWave.Count - 1; i >= 0; i--)
            {
                var s = firstWave[i];
                if (s == null || s.Gone || s.Current != NpcShip.State.Fly) { firstWave.RemoveAt(i); if ((object)s != null) lastRunSpeed.Remove(s); continue; }
                if (s.Order != null && s.Order.kind == NpcOrder.Kind.Move) { lastRunSpeed[s] = s.Order.speed; continue; }
                firstWave.RemoveAt(i);
                // Carrying the run's speed into the attack and easing it down to its own (UpdateSlowing).
                float from = lastRunSpeed.TryGetValue(s, out var v) ? v : -1f;
                lastRunSpeed.Remove(s);
                if (player == null) continue;
                var attack = new NpcOrder { kind = NpcOrder.Kind.Attack, target = player, speed = from };
                s.SetOrder(attack);
                if (from > s.OwnSpeed) slowing[s] = (attack, from, 0f);
            }
        }

        /// <summary>Modified: a first-wave Specter past its point slows from its run's speed to its own over FirstWaveSlowMs
        /// (smoothstep), so it reaches the player with some of that momentum; then its attack flies at its own speed.</summary>
        void UpdateSlowing(float dtMs)
        {
            if (slowing.Count == 0) return;
            var done = slowingDone;
            done.Clear();
            slowingKeys.Clear();
            slowingKeys.AddRange(slowing.Keys);
            foreach (var s in slowingKeys)
            {
                var (order, from, ms) = slowing[s];
                if (s == null || s.Gone || s.Order != order) { done.Add(s); continue; }
                ms += dtMs;
                float t = Mathf.Clamp01(ms / FirstWaveSlowMs);
                order.speed = t >= 1f ? -1f : Mathf.Lerp(from, s.OwnSpeed, Mathf.SmoothStep(0f, 1f, t));
                if (t >= 1f) done.Add(s); else slowing[s] = (order, from, ms);
            }
            foreach (var s in done) slowing.Remove(s);
        }

        /// <summary>Level::updateMissionOrbit: every 7500 ms the Specters whose death is over come back around the player.</summary>
        void UpdateWaves(float dtMs)
        {
            waveMs += dtMs;
            if (waveMs <= WaveMs) return;
            waveMs = 0f;
            var player = ToGame(level.Player.transform.position);
            foreach (var s in specters)
            {
                if (s == null || !s.Gone) continue;
                var at = player + new Vector3(Sign() * (Random.Range(0, 30000) + 50000), Sign() * (Random.Range(0, 10000) + 10000), Sign() * (Random.Range(0, 30000) + 50000));
                s.Revive(ToUnity(at));
            }
        }

        /// <summary>The round is over: its score is final (a running combo paid), the player can't be hurt any more.</summary>
        void End()
        {
            SupernovaChallenge.Finish();
            PlaySound(SupernovaChallenge.TakeSound());
            endMs = 0f;
            if (level.Health != null && level.Health.Target != null && !level.Health.Dead) level.Health.Target.invulnerable = true;
            if (level.Weapons != null) level.Weapons.Blocked = true;
        }

        /// <summary>The end of a round: the score, the kills and the highscore, "Play again?" (300): Yes restarts with Patala
        /// x10 (MGame::OnTouchEnd's 0xb7 restart), No goes to the main menu (the dead ChoiceWindow's answer 1).</summary>
        void Ask()
        {
            asked = true;
            string text = $"{Localization.Get(296)}: {SupernovaChallenge.Digits(SupernovaChallenge.Score)}\n"
                        + $"{Localization.Get(176)}: {SupernovaChallenge.Kills}\n"
                        + (SupernovaChallenge.NewHighscore ? Localization.Get(295) : $"{Localization.Get(294)}: {SupernovaChallenge.Digits(SupernovaChallenge.Highscore)}")
                        + "\n\n" + Localization.Get(300);
            traffic.Ask(Localization.Get(293), text, Localization.Get(134), Localization.Get(135),   // Modified: titled, Yes / No side by side
                        () => SceneManager.LoadScene(SupernovaChallenge.Start(true)),
                        () => SceneManager.LoadScene("MainMenu"));
        }

        void PlaySound(int n)
        {
            if (n <= 0 || sounds == null || n > sounds.Length || sounds[n - 1] == null) return;
            sfx.PlayOneShot(sounds[n - 1], SoundVolume * Sfx.EventGain * Settings.SfxVolume);
        }
    }
}
