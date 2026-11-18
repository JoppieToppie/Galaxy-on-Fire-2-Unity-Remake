// NetPlayerEquipment.cs
// NetPlayer (part of the partial NetPlayer; its other fields are in NetPlayer.cs): the equipment that reaches the other
// players' games (the user's report: these only worked against the ships of the player's own game).
//   Emergency system (185): its bubble shows on this player's ship in the others' games too (EmergencyBubble, from the
//     owner's clock: its end in server time and its length, so a player arriving meanwhile sees the rest of it) with the
//     1115 loop at the ship (3D); meanwhile the copy takes no hits (Target.invulnerable: the owner's ship takes none either,
//     so nothing is sent for nothing).
//   Repair beams (207 / 208): a squadmate's beam heals this player's hull (Target.RemoteHeal -> HealUpRpc: the server
//     checks the same orbit and squad, a sane amount and the rate -> HealRpc: hull only, up to the maximum). The max hull
//     is shared (Target.netMaxHull) so the beams pick the most damaged ship as with the game's own. Transfusion beams
//     need nothing here: their drain is a hit (Target.RemoteDamage, HitUpRpc).
//   Sentry guns (211-213): the owner's deployed sentries (SentryGun.All, at most 6 sent) go out as a list (id, item,
//     position, aim, hull, destroyed; every 0.2 s when it changed); each other game in the orbit builds a copy (the
//     sentry's model, turned to the aim, a Target in Target.NetShips: marker and lock like the owner's ship, its relation
//     the owner's) and shows its explosion when it is destroyed. The NPCs a game runs attack a copy like its player's ship
//     (NetOrbit puts the copies among the other players' targets: SentriesTo, SentryOwner), and every hit on a copy goes
//     to the owner's sentry (SentryHitUpRpc: the same checks as a hit on the ship -> SentryHitRpc: an NPC's hit always, a
//     player's only where players may fight). Before, NPCs in an orbit another player ran never shot at a sentry.

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public sealed partial class NetPlayer
    {
        const int MaxSentriesSent = 6;
        const float SentrySendSeconds = 0.2f;

        readonly NetworkVariable<int> maxHull = new NetworkVariable<int>(-1, Read, Write);   // the ship's max hull (-1 unknown)
        readonly NetworkVariable<double> emergencyUntil = new NetworkVariable<double>(0d, Read, Write);   // server time the bubble ends
        readonly NetworkVariable<float> emergencyLength = new NetworkVariable<float>(0f, Read, Write);    // its whole length (ms), 0 = none
        readonly NetworkVariable<FixedString512Bytes> sentryList = new NetworkVariable<FixedString512Bytes>(default, Read, Write);

        // ---- the owner ----------------------------------------------------------------------------------------

        float sentrySendTimer;
        readonly StringBuilder sentryText = new StringBuilder();

        /// <summary>Owner, every frame: the max hull, the emergency clock and the sentry list.</summary>
        void WriteEquipment()
        {
            var health = level != null ? level.Health : null;
            var own = health != null ? health.Target : null;
            int max = own != null && own.hitpoints != null ? own.hitpoints.maxHull : -1;
            if (max > 0 && maxHull.Value != max) maxHull.Value = max;
            float left = health != null && !health.Dead ? health.EmergencyLeftMs : 0f;
            if (left > 0f)
            {
                if (emergencyLength.Value <= 0f)
                {
                    emergencyUntil.Value = NetworkManager.ServerTime.Time + left / 1000.0;
                    emergencyLength.Value = Mathf.Max(1f, health.EmergencyLengthMs);
                }
            }
            else if (emergencyLength.Value > 0f) emergencyLength.Value = 0f;
            if ((sentrySendTimer -= Time.unscaledDeltaTime) > 0f) return;
            sentrySendTimer = SentrySendSeconds;
            sentryText.Clear();
            if (level != null)
            {
                int n = 0;
                foreach (var s in SentryGun.All)
                {
                    if (s == null || s.Target == null || n >= MaxSentriesSent) continue;
                    var p = s.transform.position;
                    var q = s.Aim;
                    sentryText.Append(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F1},{3:F1},{4:F1},{5:F3},{6:F3},{7:F3},{8:F3},{9:F2},{10};",
                        s.NetId, s.Item, p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.Target.HullFraction, s.Dead ? 1 : 0));
                    n++;
                }
            }
            string text = sentryText.ToString();
            if (sentryList.Value.ToString() != text) sentryList.Value = text;
        }

        // ---- the others' copies ---------------------------------------------------------------------------------

        EmergencyBubble remoteBubble;
        GameObject bubbleModel;
        AudioSource remoteInvincible;

        sealed class SentryCopy
        {
            public int id, item;
            public GameObject go;
            public Transform pivot;
            public Target target;
            public Quaternion aim;
            public bool dead;
        }

        readonly Dictionary<int, SentryCopy> sentryCopies = new Dictionary<int, SentryCopy>();
        readonly HashSet<int> sentrySeen = new HashSet<int>();
        readonly List<int> sentryGone = new List<int>();
        readonly float[] sentryValues = new float[8];
        static readonly Dictionary<Target, NetPlayer> sentryOwners = new Dictionary<Target, NetPlayer>();

        /// <summary>A sentry copy's player (null: not a sentry copy).</summary>
        public static NetPlayer SentryOwner(Target t) => t != null && sentryOwners.TryGetValue(t, out var p) ? p : null;

        /// <summary>This player's sentry copies here that are alive (the NPCs' other-player targets, NetOrbit).</summary>
        public void SentriesTo(List<Target> into)
        {
            foreach (var c in sentryCopies.Values)
                if (c.go != null && c.target != null && c.target.Alive && c.target.enabled) into.Add(c.target);
        }

        /// <summary>Remote, every frame: the bubble, the max hull and the sentry copies ('show': this ship is shown here).</summary>
        void ApplyEquipment(bool show)
        {
            if (target != null) target.netMaxHull = maxHull.Value;
            float length = emergencyLength.Value;
            float left = length > 0f && IsSpawned ? (float)((emergencyUntil.Value - NetworkManager.ServerTime.Time) * 1000.0) : 0f;
            left = Mathf.Min(left, length);
            bool bubbleOn = show && model != null && left > 0f;
            if (remoteBubble != null && (!bubbleOn || bubbleModel != model)) { remoteBubble.Hide(); remoteBubble = null; }
            if (bubbleOn && remoteBubble == null)
            {
                var assets = CombatAssets.Load();
                remoteBubble = EmergencyBubble.Show(assets, model.transform, model.GetComponentInChildren<Renderer>());
                bubbleModel = model;
                if (assets != null && assets.invincibility != null)
                {
                    if (remoteInvincible == null)
                    {
                        remoteInvincible = gameObject.AddComponent<AudioSource>();
                        remoteInvincible.loop = true;
                        remoteInvincible.playOnAwake = false;
                        EngineVoices.Setup3D(remoteInvincible);
                    }
                    remoteInvincible.clip = Modding.ModSounds.Get(assets.invincibility);
                    remoteInvincible.volume = Settings.SfxVolume;
                    remoteInvincible.Play();
                }
            }
            remoteBubble?.Update(Mathf.Max(0f, left), length);
            if (remoteBubble == null && remoteInvincible != null && remoteInvincible.isPlaying) remoteInvincible.Stop();
            if (target != null) target.invulnerable = left > 0f;   // the owner's ship takes no hits meanwhile
            ApplySentries(SharesOrbit);
        }

        void ApplySentries(bool here)
        {
            sentrySeen.Clear();
            if (here && !NetGame.Dedicated)
            {
                string text = sentryList.Value.ToString();
                foreach (var entry in text.Split(';'))
                {
                    var f = entry.Split(',');
                    if (f.Length != 11 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                        || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int item)) continue;
                    // position x y z, aim x y z w, hull fraction
                    bool ok = true;
                    for (int i = 0; i < 8 && ok; i++)
                        ok = float.TryParse(f[2 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out sentryValues[i]) && NetGuard.Finite(sentryValues[i]);
                    if (!ok) continue;
                    var v = sentryValues;
                    float hull = v[7];
                    bool dead = f[10] == "1";
                    var pos = new Vector3(v[0], v[1], v[2]);
                    var aim = new Quaternion(v[3], v[4], v[5], v[6]);
                    if (aim.x * aim.x + aim.y * aim.y + aim.z * aim.z + aim.w * aim.w < 0.5f) aim = Quaternion.identity;
                    sentrySeen.Add(id);
                    if (!sentryCopies.TryGetValue(id, out var c) || c.go == null)
                    {
                        if (c != null) sentryOwners.Remove(c.target);
                        c = BuildSentryCopy(id, item, pos, aim, dead);
                        sentryCopies[id] = c;
                    }
                    c.go.transform.position = pos;
                    c.aim = aim;
                    c.pivot.rotation = Quaternion.Slerp(c.pivot.rotation, aim, 1f - Mathf.Exp(-12f * Time.deltaTime));
                    var t = c.target;
                    t.hp = dead ? 0f : Mathf.Max(0.001f, Mathf.Clamp01(hull)) * t.maxHp;
                    // Its relation is its player's: a squadmate's green, an enemy pilot's red where players may fight.
                    if (target != null)
                    {
                        t.friendToPlayer = target.friendToPlayer;
                        t.hostileToPlayer = target.hostileToPlayer;
                        t.playerProof = target.playerProof;
                    }
                    if (dead && !c.dead) KillSentryCopy(c);
                    c.dead = dead;
                }
            }
            sentryGone.Clear();
            foreach (var kv in sentryCopies) if (!sentrySeen.Contains(kv.Key)) sentryGone.Add(kv.Key);
            foreach (int id in sentryGone) { DestroySentryCopy(sentryCopies[id]); sentryCopies.Remove(id); }
        }

        SentryCopy BuildSentryCopy(int id, int item, Vector3 pos, Quaternion aim, bool dead)
        {
            var c = new SentryCopy { id = id, item = item, aim = aim, dead = dead };
            c.go = new GameObject($"Sentry gun {item} ({DisplayName})");
            c.go.transform.position = pos;
            c.pivot = new GameObject("gun").transform;
            c.pivot.SetParent(c.go.transform, false);
            c.pivot.rotation = aim;
            var fx = WeaponFx.Load(item);
            if (fx != null && fx.sentry != null)
            {
                var m = Instantiate(fx.sentry, c.pivot, false);
                GunRig.StripForFx(m);
            }
            if (dead) c.pivot.gameObject.SetActive(false);
            var t = c.go.AddComponent<Target>();
            t.isShip = true;
            t.race = 0;
            t.radius = 800f * Gun.MetersPerUnit;
            t.hp = t.maxHp = 100f;
            t.customDeath = true;
            t.displayName = Localization.Get(1666);
            int sentry = id;
            t.RemoteDamage = (amount, hitVector, byNpc, _) => SentryHitUpRpc(sentry, amount, hitVector, byNpc);
            c.target = t;
            sentryOwners[t] = this;
            Target.NetShips.Add(t);
            return c;
        }

        /// <summary>SentryGun.OnDied's look: the mid explosion, the model gone.</summary>
        static void KillSentryCopy(SentryCopy c)
        {
            if (c.go == null) return;
            var assets = CombatAssets.Load();
            Explosion.Spawn(0, c.go.transform.position, c.pivot.forward, 1f, assets != null ? CombatAssets.Pick(assets.explosionMid) : null, true);
            c.pivot.gameObject.SetActive(false);
        }

        static void DestroySentryCopy(SentryCopy c)
        {
            if (c == null) return;
            if (c.target != null) { Target.NetShips.Remove(c.target); sentryOwners.Remove(c.target); }
            if (c.go != null) Destroy(c.go);
        }

        /// <summary>Despawned: the copies, the bubble and its sound go.</summary>
        void ClearEquipment()
        {
            foreach (var c in sentryCopies.Values) DestroySentryCopy(c);
            sentryCopies.Clear();
            remoteBubble?.Hide();
            remoteBubble = null;
        }

        // ---- the RPCs -------------------------------------------------------------------------------------------

        /// <summary>A squadmate's repair beam (RepairBeam, Target.RemoteHeal) on this player's ship, through the server: the
        /// same orbit and squad, a sane amount, at the hits' rate.</summary>
        [Rpc(SendTo.Server)]
        void HealUpRpc(int points, RpcParams rpc = default)
        {
            ulong healer = rpc.Receive.SenderClientId;
            if (healer == OwnerClientId || !NetRateLimit.Allow(healer, NetRateLimit.Kind.Hit)) return;
            if (!NetGuard.Damage(points)) { NetRateLimit.Reject(healer, $"a repair of {points}"); return; }
            var by = NetSquad.Find(healer);
            if (!NetGuard.SameOrbit(by, this) || !NetSquad.Same(by, this)) return;
            HealRpc(points);
        }

        /// <summary>The checked repair: Player::heal, hull only, up to the maximum.</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void HealRpc(int points)
        {
            var health = level != null ? level.Health : null;
            var own = health != null && !health.Dead ? health.Target : null;
            var hp = own != null ? own.hitpoints : null;
            if (hp == null || !hp.Alive) return;
            hp.hull = Mathf.Min(hp.maxHull, hp.hull + points);
            own.hp = hp.hull;
        }

        /// <summary>Another game's hit on one of this player's sentries (its copy there), through the server like a hit on
        /// the ship: the same orbit, never a squadmate's weapon, a finite damage up to NetGuard.MaxDamage, at its rate.</summary>
        [Rpc(SendTo.Server)]
        void SentryHitUpRpc(int sentry, float amount, Vector3 hitVector, bool byNpc, RpcParams rpc = default)
        {
            ulong shooter = rpc.Receive.SenderClientId;
            if (shooter == OwnerClientId || !NetRateLimit.Allow(shooter, NetRateLimit.Kind.Hit)) return;
            if (!NetGuard.Damage(amount) || !NetGuard.Finite(hitVector)) { NetRateLimit.Reject(shooter, $"a sentry hit of {amount}"); return; }
            var by = NetSquad.Find(shooter);
            if (!NetGuard.SameOrbit(by, this)) return;
            if (!byNpc && NetSquad.Same(by, this)) return;
            SentryHitRpc(sentry, amount, hitVector, byNpc, shooter);
        }

        /// <summary>The checked hit: the sentry takes it (an NPC's always; a player's only where players may fight).</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        void SentryHitRpc(int sentry, float amount, Vector3 hitVector, bool byNpc, ulong shooter)
        {
            if (!byNpc)
            {
                var from = NetSquad.Find(shooter);
                if (from == null || from.Station != Station || !PvpWith(from)) return;
            }
            foreach (var s in SentryGun.All)
                if (s != null && s.NetId == sentry) { s.TakeRemoteHit(amount, byNpc, hitVector); return; }
        }
    }
}
