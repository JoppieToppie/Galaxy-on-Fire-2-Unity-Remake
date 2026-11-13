// NetShotMirror.cs
// Multiplayer: another player's (or, on a client, the host's NPC's) shots, drawn locally. One visual-only Gun per weapon
// item (the item's own kind, pool and look: WeaponFx) with its GunRig; each received shot is injected at the sender's
// bullet pose and lifetime (Gun.Inject), beams are fired through the gun's beam path at the same target, homing weapons
// steer toward the sender's lock (resolved here), blasts (bombs, mines, scatter shells) are the fx's explosion at the
// sender's point. The mirror guns stop on what they hit here (a ship, an asteroid: the impact shows) but deal no damage:
// the shooter's own game applies the hits (and never on the shooter's own copy here, the guns' owner).
// NetShots: the targets travel as NetworkObject ids (a NetProxy, a NetPlayer).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class NetShotMirror
    {
        // The mirrors' projectile / impact pools sit under one scene object (gone with the scene); the mirrors themselves
        // live in NetPlayer / NetProxy for the whole session, so after a scene change they start over (Refresh).
        static Transform fxRoot;
        static int rootGeneration;
        int generation = -1;

        sealed class Mirror
        {
            public Gun gun;
            public GunRig rig;
            public WeaponFx fx;
            public Target target;
        }

        readonly Dictionary<int, Mirror> mirrors = new Dictionary<int, Mirror>();
        int soundItem = -1;   // the primary weapon item whose shots make the sound
        readonly Transform muzzleParent;
        readonly System.Func<Target> owner;
        readonly System.Func<Target, bool> ignores;
        readonly System.Func<Color?> weaponColor;
        readonly System.Func<int> ship;   // the shooter's ship (a kit's hardpoint sounds, ModShipSounds), null = none

        /// <param name="muzzleParent">The shooter's model (muzzle flashes), null = none (NPC guns draw none either).</param>
        /// <param name="owner">The shooter's own Target here (its shots pass through it).</param>
        /// <param name="ignores">What the shooter's shots pass through here (its squadmates), null = nothing.</param>
        /// <param name="weaponColor">The shooter's weapon colour (their build's, Flight.WeaponTint), null = none.</param>
        public NetShotMirror(Transform muzzleParent, System.Func<Target> owner = null, System.Func<Target, bool> ignores = null,
                             System.Func<Color?> weaponColor = null, System.Func<int> ship = null)
        {
            this.ship = ship;
            this.weaponColor = weaponColor;
            this.muzzleParent = muzzleParent;
            this.owner = owner;
            this.ignores = ignores;
        }

        /// <summary>The pools went with the last scene: the mirrors are made again on their next shot.</summary>
        void Refresh()
        {
            if (fxRoot == null) { if (mirrors.Count > 0) mirrors.Clear(); return; }
            if (generation != rootGeneration) { mirrors.Clear(); generation = rootGeneration; }
        }

        Mirror Get(int item)
        {
            Refresh();
            if (fxRoot == null) { fxRoot = new GameObject("NetShots").transform; rootGeneration++; generation = rootGeneration; }
            var data = Database.Shared.Item(item);
            if (mirrors.TryGetValue(item, out var m)) { m.rig.Tint(WeaponTint.For(data, weaponColor?.Invoke())); return m; }   // a changed colour
            if (data == null) return null;
            var gun = new Gun(data, Vector3.zero, false) { Ignores = ignores, Mirror = true };
            var fx = WeaponFx.Load(item);
            m = new Mirror { gun = gun, fx = fx, rig = new GunRig(gun, fx, fxRoot, muzzleParent, 2) };
            m.rig.Tint(WeaponTint.For(data, weaponColor?.Invoke()));   // their weapon colour
            m.rig.EnableTrails();   // another player's rockets trail smoke like one's own
            var rig = m.rig;
            gun.Hit += (i, t, point) => rig.ShowImpact(point);   // the impact only: no damage handler
            mirrors[item] = m;
            return m;
        }

        /// <summary>A shot: bullet pose and life left (beams: the start point and unit direction), the homing delay and lock.</summary>
        public void Shot(int item, Vector3 position, Vector3 velocity, Vector3 up, float lifetimeMs, float homingDelayMs, Target target)
        {
            var m = Get(item);
            if (m == null) return;
            m.target = target;
            m.gun.homingDelayMs = homingDelayMs;
            if (m.gun.isBeam)
            {
                m.gun.AutoAim = () => m.target != null && m.target.Alive ? m.target : null;
                m.gun.reloadAcc = m.gun.WaitMs + 1f;
                if (m.gun.TryFire(position, Quaternion.LookRotation(velocity, up), false) < 0) return;
            }
            else if (!m.gun.Inject(position, velocity, up, lifetimeMs)) return;
            m.rig.OnShot();
            // Player::calcWeaponSounds: only one primary gun makes the shot sound (the first that fired here stands in for
            // the shooter's first mounted primary); the others' shots were sounded too, a 4-gun ship 4 times over.
            if (!m.gun.isSecondary && !Settings.EachWeaponSound)   // the remake option: every weapon item sounds
            {
                if (soundItem < 0) soundItem = item;
                if (item != soundItem) return;
            }
            // Remake mods: the shooter's kit hardpoint sound (ModShipSounds; a looping one keeps the weapon's own here).
            var ownSound = ship != null ? Modding.ModShipSounds.Weapon(ship(), Database.Load().Item(item)) : null;
            if (ownSound?.shot is Modding.ModShipSounds.Sound own && ownSound.loop == null)
            {
                if (own.Clip is AudioClip ownClip) ShotVoices.PlayAt(ownClip, position, own.vol0 * 0.8f / 0.7f);
                return;
            }
            var clip = m.fx != null ? m.fx.Shot : null;
            if (clip != null) ShotVoices.PlayAt(clip, position, 0.8f);
        }

        /// <summary>A blast of that weapon at 'point': its explosion, and the nearest mirrored bullet is gone.</summary>
        public void Blast(int item, Vector3 point)
        {
            var m = Get(item);
            if (m == null) return;
            m.rig.ShowBlast(point, Vector3.forward);
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < m.gun.bullets.Length; i++)
            {
                if (!m.gun.IsActive(i)) continue;
                float d = (m.gun.bullets[i].position - point).sqrMagnitude;
                if (d < best) { best = d; nearest = i; }
            }
            if (nearest >= 0) m.gun.bullets[nearest].timer = -1e9f;
        }

        public void Update(float dtMs)
        {
            Refresh();
            if (mirrors.Count == 0) return;
            var cam = Camera.main;
            var forward = muzzleParent != null ? muzzleParent.forward : Vector3.forward;
            foreach (var m in mirrors.Values)
            {
                m.gun.owner = owner?.Invoke();
                m.gun.Update(dtMs, Target.All, m.target != null && m.target.Alive ? m.target : null);
                m.rig.UpdateVisuals(dtMs, cam, forward);
            }
        }

        public void Clear()
        {
            Refresh();
            foreach (var m in mirrors.Values) { m.gun.RemoveAll(); m.rig.HideAll(); }
        }
    }
}
