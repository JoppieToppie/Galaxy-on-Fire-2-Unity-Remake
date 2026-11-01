// Crate.cs
// A cargo container left by a destroyed ship (KIPlayer::createCrate 0xb2e2c, Reference/research/ship_combat.md 5.4):
// the race's container mesh at the explosion, drifting along a random direction at bombForce = 50..50.49 units per
// original frame, x0.98 per frame until < 0.05, spinning slowly; gone 60 s after the death. Only a tractor beam collects it
// (CombatRadar: salvage lock, pull at 10 u/ms, captured within 400 units; the first non-empty entry). A crate stolen from
// a living (EMP-disabled) ship (KIPlayer::createCrate(0) from TractorBeam::update) carries that ship's cargo and is
// gone once captured: what is left stays aboard the ship. A shot asteroid leaves one too (Target.DropAsteroidCrate).

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class Crate : MonoBehaviour
    {
        const float M = 0.05f, LifetimeMs = 60000f;

        /// <summary>The crates in the scene (enabled, on active objects): what FindObjectsByType&lt;Crate&gt; returned, without
        /// a scene search every frame (the HUD markers, the radar's salvage lock). Copy it before destroying crates in a loop.</summary>
        public static readonly List<Crate> All = new List<Crate>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        /// <summary>A crate in the scene holds this item (a mod blueprint's data crate: a blueprint is handed out once, so
        /// no second crate is made while one still floats).</summary>
        public static bool HoldsAny(int item) => All.Exists(c => c != null && c.loot.Exists(s => s.item == item && s.amount > 0));

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public readonly List<ItemStack> loot = new List<ItemStack>();
        public int race;
        /// <summary>Being pulled by the tractor beam (no drift).</summary>
        public bool pulled;
        /// <summary>The living ship whose cargo this is (a steal), null for a wreck's crate.</summary>
        public World.NpcShip stolenFrom;
        /// <summary>Multiplayer: the other game's ship (its NetProxy's Target) whose cargo this is (a steal); the capture
        /// takes it off that ship through Target.RemoteSteal.</summary>
        [System.NonSerialized] public Target stolenFromRemote;
        /// <summary>A steal's crate (this ship's or another game's): the stealer's own, never shared or auto-salvaged.</summary>
        public bool Stolen => stolenFrom != null || stolenFromRemote != null;
        /// <summary>Player+0x5d of the ship it came from (a friend: Level::stealFriendCargo) / a mission container (116 / 117).</summary>
        public bool fromFriend, missionCrate;

        /// <summary>Multiplayer (NetCrate): this player's beam captured it (CombatRadar, just before it is destroyed).</summary>
        [System.NonSerialized] public System.Action CapturedHere;
        /// <summary>Multiplayer: dropped by a freelance mission's ship or junk: only the mission's team can take it (NetCrate).</summary>
        [System.NonSerialized] public bool missionLoot;
        /// <summary>Multiplayer (NetCrate): another player's tractor beam has it; the radar leaves it alone.</summary>
        [System.NonSerialized] public bool claimedByOther;
        /// <summary>Multiplayer: pulled in, but the host hasn't confirmed this player's claim yet: it waits at the ship.</summary>
        [System.NonSerialized] public bool captureBlocked;
        /// <summary>Multiplayer: the local tractor beam started pulling it (the claim goes to the host).</summary>
        [System.NonSerialized] public System.Action PullStarted;
        /// <summary>A mirror of another game's crate (NetCrate): it neither drifts nor expires by itself.</summary>
        [System.NonSerialized] public bool remote;

        /// <summary>KIPlayer::createCrate's type: the model. 0 the race's container, 1 the rock 0x421e asteroid_01_junk (a shot
        /// asteroid), 2 its Void variant 0x421f, 3 the space junk's 0x4218.</summary>
        public const int LookContainer = 0, LookRock = 1, LookVoidRock = 2, LookJunk = 3;
        public int look;

        /// <summary>KIPlayer::createCrate(look) at 'at' (Unity): the model by look / race, drifting off with 'cargo'.</summary>
        public static Crate Spawn(Vector3 at, IEnumerable<ItemStack> cargo, int race, int look)
        {
            var assets = CombatAssets.Load();
            var prefab = assets != null ? assets.CrateModel(look, race) : null;
            var go = prefab != null ? Instantiate(prefab, at, Random.rotation) : new GameObject();
            go.name = "Crate";
            var crate = go.AddComponent<Crate>();
            crate.look = look;
            crate.Setup(cargo, race);
            return crate;
        }

        /// <summary>A fixed object's crate: the 60 s start when its wreck animation ends (state 4).</summary>
        public void DelayExpiry(float ms) => ageMs -= ms;
        /// <summary>Multiplayer: how long it has floated (ms; it expires at 60 s), carried over when another game takes it over
        /// (NetCrate).</summary>
        public float AgeMs { get => ageMs; set => ageMs = value; }

        Vector3 drift;
        float force, ageMs;

        public bool HasLoot => loot.Exists(s => s.amount > 0);

        public void Setup(IEnumerable<ItemStack> cargo, int race)
        {
            foreach (var s in cargo) if (s.amount > 0) loot.Add(s.Clone());
            this.race = race;
            drift = new Vector3(Random.Range(0, 200) - 100, Random.Range(0, 200) - 100, Random.Range(0, 200) - 100).normalized;
            force = 50f + Random.Range(0, 50) * 0.01f;
        }

        /// <summary>Remake: thrown off along 'direction' at 'scale' x the usual bomb force (a ship's several crates, NpcShip:
        /// they burst apart in different directions; the slowdown is the usual x0.98 a frame).</summary>
        public void Launch(Vector3 direction, float scale)
        {
            if (direction.sqrMagnitude > 1e-6f) drift = direction.normalized;
            force *= scale;
        }

        void Update()
        {
            float dtMs = Time.deltaTime * 1000f, frames = dtMs / OriginalFrame.Ms;
            if (remote) return;   // the host's crate moves it and ends it (NetCrate)
            ageMs += dtMs;
            // The Hijacker's mission container stays (the original tractors it straight out of the ship; the remake's crate
            // expiring left Recovery / Salvage neither won nor failed).
            if ((ageMs > LifetimeMs && !missionCrate) || !HasLoot) { Destroy(gameObject); return; }
            if (!pulled && force > 0.05f)
            {
                transform.position += drift * force * frames * M;
                force *= Mathf.Pow(0.98f, frames);
            }
            transform.Rotate(0f, dtMs / 2f / 65536f * 360f * frames, 0f, Space.Self);
        }
    }
}
