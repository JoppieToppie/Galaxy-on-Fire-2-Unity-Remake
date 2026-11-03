// GunRig.cs
// The visuals of one Gun (weapons.md 5): a projectile instance per bullet slot (ObjectGun), the muzzle flash on the
// mount, a small pool of impact effects. Plain C#, shared by the player's WeaponSystem and the NPC ships.
// Blasters, thermo guns and turrets face the camera (not a turret shot modelled as a streak); lasers, cannons and rockets point along their flight. Projectiles shrink over
// their last 1000 ms (Gun.VisualScale). Effects never cast shadows or cull by LOD.
// Beams (BeamGun::update 0x1a6b04): one mesh 1 unit long along +Z, scaled (1, 1, beam length) along the direction chosen
// at fire time, following the mount, its animation restarted per shot and hidden when it ends. Mines are drawn at x0.7
// and tumble (MineGun). Scatter shells have no impact mesh (their burst explosion replaces it).
// The guided Liberator (BombGun, attr 15): its deploy animation plays once per launch, starting 500 ms after it.
// The player's rockets, missiles and thermo shots trail smoke, the bombs fire sprites, the fireworks sparks (RocketTrail,
// EnableTrails; NPC guns have none).
// Projectiles, muzzle flashes and impacts fade by their `extra` (opacity) channel: without it an impact's big glow part
// (radius ~3600 units, meant at 20 % and gone after 267 ms) stayed at full brightness and the impact looked far too big.
// The camera-facing projectiles and the impacts also carry a CameraFacing: their owners stop calling UpdateVisuals while
// the world is frozen (Action Freeze, a paused game), and turned round in the photo mode's orbit they were seen edge-on.

using System.Collections.Generic;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class GunRig
    {
        public readonly Gun gun;
        public readonly WeaponFx fx;
        readonly Transform[] projectiles;
        readonly Transform ship;
        float beamMs, beamLength;
        Vector3[] spin;
        bool[] wasActive;
        // What each projectile object was last set to (SetActive) and its last scale: asking the engine every frame for every
        // NPC's four projectiles was a native call each (20 fleet battles: ~3400 a frame).
        bool[] shown;
        float[] shownScale;
        PartAnimation[][] projAnims;
        readonly bool billboard;
        readonly GameObject muzzle;
        readonly float muzzleLength;
        float muzzleMs;
        // Remake: more flashes of the same muzzle (SetMuzzles: a twin-barrelled gun flashes at each barrel), with their parts.
        readonly List<(GameObject go, (PartAnimation[] anims, Modding.ModFxPart[] mods) parts)> extraMuzzles =
            new List<(GameObject, (PartAnimation[], Modding.ModFxPart[]))>();
        readonly GameObject[] impacts;
        readonly float[] impactMs;
        readonly float impactLength;
        int nextImpact;
        readonly Transform fxRoot;
        RocketTrail[] trails;
        ParticleSystem[] flames;   // bombs: record 12, fireworks 47 (RocketTrail.MissileTrail)
        Color? tint;   // the weapon colour (WeaponTint), null = the weapon's own look
        readonly Dictionary<Renderer, Material[]> ownMaterials = new Dictionary<Renderer, Material[]>();

        /// <param name="fxRoot">Parent of the projectiles and impacts (world space).</param>
        /// <param name="muzzleParent">The ship (null = no muzzle flash).</param>
        public GunRig(Gun gun, WeaponFx fx, Transform fxRoot, Transform muzzleParent, int impactPool = 4)
        {
            this.gun = gun;
            this.fx = fx;
            this.fxRoot = fxRoot;
            ship = muzzleParent;
            if (gun.echo != null) gun.Echoed += ShowEcho;
            // ObjectGun::ObjectGun 0x18c020: +0x4c = (0x85 >> (sort - 1)) & 1, i.e. blasters (1), thermo guns (3) and turrets (8)
            // take the camera's rotation in ObjectGun::render (turret shots were drawn along their flight, edge-on: flat).
            // Modified: not a turret shot modelled as a streak along its flight (the Archimedes' projectile_023, 950 units
            // long and 175 wide): facing the camera its length pointed into the screen, a tail running away from the centre.
            billboard = gun.kind == Gun.Kind.Blaster || gun.kind == Gun.Kind.Thermo
                        || (gun.kind == Gun.Kind.Turret && (fx == null || !AlongFlight(fx.projectile)));
            projectiles = new Transform[gun.bullets.Length];
            if (fx != null && fx.projectile != null)
                for (int i = 0; i < projectiles.Length; i++)
                {
                    var go = Object.Instantiate(fx.projectile, fxRoot);
                    go.name = $"{fx.projectile.name} {i}";
                    StripForFx(go);
                    EnableFades(go);
                    if (billboard) go.AddComponent<CameraFacing>();
                    go.SetActive(false);
                    projectiles[i] = go.transform;
                }
            if (fx != null && fx.muzzleFlash != null && muzzleParent != null)
            {
                muzzle = Object.Instantiate(fx.muzzleFlash, muzzleParent, false);
                muzzle.transform.localPosition = gun.mountLocal;
                StripForFx(muzzle);
                EnableFades(muzzle);
                muzzleLength = Mathf.Max(80f, MaxLength(muzzle));
                muzzleParts = Parts(muzzle);
                muzzle.SetActive(false);
            }
            wasActive = new bool[projectiles.Length];
            shown = new bool[projectiles.Length];   // made inactive above
            shownScale = new float[projectiles.Length];
            for (int i = 0; i < shownScale.Length; i++) shownScale[i] = float.NaN;
            projAnims = new PartAnimation[projectiles.Length][];
            for (int i = 0; i < projectiles.Length; i++)
                projAnims[i] = projectiles[i] != null ? projectiles[i].GetComponentsInChildren<PartAnimation>(true) : new PartAnimation[0];
            if (gun.kind == Gun.Kind.Mine)
            {
                // MineGun: a random tumble per mine, (rnd(200) - 100) / 50 per axis (read as rad/s).
                spin = new Vector3[projectiles.Length];
                for (int i = 0; i < spin.Length; i++)
                    spin[i] = new Vector3(Random.Range(0, 200) - 100, Random.Range(0, 200) - 100, Random.Range(0, 200) - 100) / 50f;
            }
            if (gun.isBeam && projectiles.Length > 0 && projectiles[0] != null)
            {
                beamLength = Mathf.Max(200f, MaxLength(projectiles[0].gameObject));
                beamParts = Parts(projectiles[0].gameObject);
            }
            if (fx != null && fx.impact != null && impactPool > 0 && gun.kind != Gun.Kind.ScatterGun)
            {
                impacts = new GameObject[impactPool];
                impactMs = new float[impactPool];
                for (int i = 0; i < impactPool; i++)
                {
                    impacts[i] = Object.Instantiate(fx.impact, fxRoot);
                    StripForFx(impacts[i]);
                    EnableFades(impacts[i]);
                    impacts[i].AddComponent<CameraFacing>();   // "_lookat" meshes
                    impacts[i].SetActive(false);
                }
                impactParts = new (PartAnimation[], Modding.ModFxPart[])[impactPool];
                for (int i = 0; i < impactPool; i++) impactParts[i] = Parts(impacts[i]);
                impactLength = Mathf.Max(200f, MaxLength(impacts[0]));
            }
        }

        /// <summary>RocketGun::setRadar (the player's guns, and other players' mirrored shots): one smoke trail per bullet
        /// for rockets, missiles, cluster missiles and thermo guns, a fire-sprite system for EMP bombs.</summary>
        public void EnableTrails()
        {
            if (flames == null && RocketTrail.HasMissileTrail(gun))
            {
                flames = new ParticleSystem[gun.bullets.Length];
                for (int i = 0; i < flames.Length; i++) flames[i] = RocketTrail.MissileTrail(gun, fxRoot);
            }
            var look = fx != null ? fx.trail : null;   // a mod's trail (items.json fx "trail")
            var rec = look != null && (look.none || look.width > 0f || look.lifeMs > 0f || look.color != Color.white) ? RocketTrail.For(gun, look) : RocketTrail.For(gun);
            if (rec == null || trails != null) return;
            var mat = RocketTrail.MaterialFor(gun);
            if (mat == null) return;
            if (tint.HasValue) mat = WeaponTint.Tinted(mat, tint.Value);   // a thermo gun's trail in the weapon colour
            trails = new RocketTrail[gun.bullets.Length];
            for (int i = 0; i < trails.Length; i++) trails[i] = new RocketTrail(rec, fxRoot, mat, look != null ? look.color : (Color?)null);
        }

        /// <summary>The weapon colour (WeaponTint; null = the weapon's own look) on the projectiles, the muzzle flash, the
        /// impacts and (when called before EnableTrails) the trails: tinted copies of their materials, the fades re-read.</summary>
        public void Tint(Color? color)
        {
            if (tint == color) return;
            tint = color;
            void On(GameObject go)
            {
                if (go == null) return;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!ownMaterials.TryGetValue(r, out var own)) ownMaterials[r] = own = r.sharedMaterials;
                    var mats = (Material[])own.Clone();
                    if (color.HasValue) for (int i = 0; i < mats.Length; i++) mats[i] = WeaponTint.Tinted(mats[i], color.Value);
                    r.sharedMaterials = mats;
                    r.SetPropertyBlock(null);
                }
                foreach (var a in go.GetComponentsInChildren<PartAnimation>(true)) a.RefreshMaterials();
            }
            foreach (var p in projectiles) if (p != null) On(p.gameObject);
            On(muzzle);
            foreach (var e in extraMuzzles) On(e.go);
            if (impacts != null) foreach (var i in impacts) On(i);
        }

        // The muzzle's, each impact's and the beam's animated parts, collected once (PlayOnce per shot / hit walked them).
        (PartAnimation[] anims, Modding.ModFxPart[] mods) muzzleParts, beamParts;
        (PartAnimation[] anims, Modding.ModFxPart[] mods)[] impactParts;

        static (PartAnimation[], Modding.ModFxPart[]) Parts(GameObject go) =>
            (go.GetComponentsInChildren<PartAnimation>(true), go.GetComponentsInChildren<Modding.ModFxPart>(true));

        /// <summary>Modified: a projectile model drawn along its flight: its meshes reach more than twice as far along +Z as
        /// across it (a streak or comet; a flat camera-facing sprite is as long as it is wide).</summary>
        static bool AlongFlight(GameObject projectile)
        {
            if (projectile == null) return false;
            var root = projectile.transform;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in projectile.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    var pt = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(pt, Vector3.zero); any = true; } else b.Encapsulate(pt);
                }
            }
            return any && b.size.z > 2f * Mathf.Max(b.size.x, b.size.y);
        }

        public static float MaxLength(GameObject go)
        {
            float l = 0f;
            foreach (var a in go.GetComponentsInChildren<PartAnimation>(true)) l = Mathf.Max(l, a.LengthMs);
            return Mathf.Max(l, Modding.ModFxPart.MaxLength(go));
        }

        /// <summary>The part animations apply their `extra` channel as opacity (PartAnimation.applyMaterialChannels).</summary>
        public static void EnableFades(GameObject go)
        {
            foreach (var a in go.GetComponentsInChildren<PartAnimation>(true))
            {
                a.applyMaterialChannels = true;
                a.ownMaterials = true;   // copies, not property blocks: the SRP Batcher keeps them
                a.onlyWhenSeen = true;   // a shot nobody sees needn't pulse (20 fleet battles: ~760 of them, 3 ms a frame)
            }
        }

        public static void StripForFx(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            GoF2Remake.Visuals.AutoLods.FullDetail(go);   // no made levels: the LODGroup goes off
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.enabled = false;
        }

        /// <summary>Remake: the muzzle flash at each of 'localPositions' on 'parent' (a twin-barrelled gun: one per barrel's
        /// tip; the Valkyrie battlestation's turrets in 80, whose single flash sat inside the turret). The rig's own flash
        /// moves to the first point, copies of it to the others; OnShot() flashes them all, OnShot(i) the one that fired.</summary>
        public void SetMuzzles(Transform parent, params Vector3[] localPositions)
        {
            if (muzzle == null || parent == null || localPositions == null || localPositions.Length == 0) return;
            foreach (var e in extraMuzzles) if (e.go != null) Object.Destroy(e.go);
            extraMuzzles.Clear();
            muzzle.transform.SetParent(parent, false);
            muzzle.transform.SetLocalPositionAndRotation(localPositions[0], Quaternion.identity);
            for (int i = 1; i < localPositions.Length; i++)
            {
                var go = Object.Instantiate(muzzle, parent, false);
                go.name = $"{muzzle.name} {i}";
                go.transform.SetLocalPositionAndRotation(localPositions[i], Quaternion.identity);
                go.SetActive(muzzle.activeSelf);
                extraMuzzles.Add((go, Parts(go)));
            }
        }

        /// <summary>A shot left the gun: the muzzle flash.</summary>
        public void OnShot() => OnShot(-1);

        /// <summary>A shot left the gun: the muzzle flash at muzzle 'muzzleIndex' only (SetMuzzles' order: a twin-barrelled
        /// gun flashes at the barrel that fired), -1 = at every muzzle.</summary>
        public void OnShot(int muzzleIndex)
        {
            if (gun.isBeam && projectiles.Length > 0 && projectiles[0] != null)
            {
                projectiles[0].gameObject.SetActive(true);
                PartAnimation.PlayOnce(beamParts.anims, beamParts.mods);
                beamMs = beamLength;
            }
            if (muzzle == null) return;
            int count = 1 + extraMuzzles.Count;
            bool one = muzzleIndex >= 0 && count > 1;
            for (int i = 0; i < count; i++)
            {
                var go = i == 0 ? muzzle : extraMuzzles[i - 1].go;
                if (go == null) continue;
                if (one && i != muzzleIndex % count) { go.SetActive(false); continue; }   // the other barrel's goes out
                go.SetActive(true);
                var parts = i == 0 ? muzzleParts : extraMuzzles[i - 1].parts;
                PartAnimation.PlayOnce(parts.anims, parts.mods);
            }
            muzzleMs = muzzleLength;
        }

        /// <summary>A bullet hit something at 'point'.</summary>
        /// <summary>A shot's echo went off here (GunEcho): its explosion, at the echo's size, a second blast after the hit.</summary>
        void ShowEcho(Vector3 point)
        {
            var echo = gun.echo;
            if (echo == null || echo.explosion < 0) return;
            var forward = fxRoot != null ? fxRoot.forward : Vector3.forward;
            Explosion.Spawn(echo.explosion, point, forward, echo.Scale, fx != null ? fx.ExplosionSound : null, false);
        }

        public void ShowImpact(Vector3 point)
        {
            // Gun::calcCharacterCollision: rockets, missiles and cluster missiles have no impact mesh; a hit emits record 11
            // SET_EXPLOSION_MANUALLY_BIG at the bullet (one sprite_explosion particle, 1500 ms, 2000..3000 units, +500/s).
            if (gun.IsMissile)
            {
                ShipBurn.ManualBurst(point, 1.5f, 2000f, 3000f, 500f);
                return;
            }
            if (impacts == null) return;
            int i = nextImpact;
            nextImpact = (nextImpact + 1) % impacts.Length;
            var go = impacts[i];
            go.SetActive(true);
            go.transform.position = point;
            PartAnimation.PlayOnce(impactParts[i].anims, impactParts[i].mods);
            impactMs[i] = impactLength;
        }

        public void UpdateVisuals(float dtMs, Camera cam, Vector3 fallbackForward)
        {
            if (gun.isBeam)
            {
                UpdateBeam(dtMs);
                projectilesDone(dtMs, cam);
                return;
            }
            Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
            for (int i = 0; i < projectiles.Length; i++)
            {
                var t = projectiles[i];
                if ((object)t == null) continue;
                bool active = gun.IsActive(i);
                if (shown[i] != active) { if (t == null) continue; t.gameObject.SetActive(active); shown[i] = active; }
                bool launched = active && !wasActive[i];
                if (trails != null)
                {
                    if (launched) trails[i].Restart(gun.bullets[i].position);
                    else if (!active && wasActive[i]) trails[i].Stop();
                }
                if (flames?[i] != null && !active && wasActive[i]) flames[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                wasActive[i] = active;
                if (!active) { trails?[i].Tick(dtMs, false, default, cam); continue; }
                ref var b = ref gun.bullets[i];
                // BombGun::BombGun 0x170e74: the EMP bomb (mesh 0x395c) loops its glow child 0x395d (SetAnimationState 2):
                // the core strobes and the cyan sparks pulse while it flies.
                if (launched && gun.kind == Gun.Kind.EmpBomb)
                    foreach (var a in projAnims[i]) { a.loop = true; a.Restart(); }
                if (gun.Guided)
                {
                    // BombGun::update: restarted on launch (state 3 -> 1), advancing only after the first 500 ms, once.
                    if (launched) PartAnimation.PlayOnce(t.gameObject);
                    float sp = b.age >= 500f ? 1f : 0f;
                    foreach (var a in projAnims[i]) a.speed = sp;
                }
                var rot = billboard && cam != null
                    ? camRot
                    : Quaternion.LookRotation(b.velocity.sqrMagnitude > 1e-9f ? b.velocity : fallbackForward, b.up);
                if (spin != null) rot = Quaternion.Euler(spin[i] * (b.age * 0.001f * Mathf.Rad2Deg));
                t.SetPositionAndRotation(b.position, rot);
                float scale = gun.VisualScale(i) * (spin != null ? 0.7f : 1f);
                if (scale != shownScale[i]) { t.localScale = Vector3.one * scale; shownScale[i] = scale; }
                trails?[i].Tick(dtMs, true, b.position, cam);
                if (flames?[i] != null)
                {
                    flames[i].transform.SetPositionAndRotation(b.position, rot);
                    if (launched) flames[i].Play(true);
                }
            }
            projectilesDone(dtMs, cam);
        }

        /// <summary>The beam: at the mount, along its fire-time direction, scaled to its length (units).</summary>
        void UpdateBeam(float dtMs)
        {
            var t = projectiles.Length > 0 ? projectiles[0] : null;
            if (t == null) return;
            if (beamMs <= 0f) { if (t.gameObject.activeSelf) t.gameObject.SetActive(false); return; }
            beamMs -= dtMs;
            // BeamGun::update: at ship + R * (mount + (0, 0, -100)) when the gun has a mount (5 m behind it).
            var from = ship != null ? ship.TransformPoint(gun.mountLocal == Vector3.zero ? Vector3.zero
                                                          : gun.mountLocal + new Vector3(0f, 0f, -100f * Gun.MetersPerUnit))
                                    : gun.bullets[0].position;
            // The fire-time direction and length (Gun::shootAt writes +0x8c / +0x90 once; BeamGun::update only reads them):
            // the beam stays where it was shot while the ship turns, only its start rides on the mount. (A remake change of
            // 2026-10-01 re-aimed it every frame, at the target or the nose, so it swung with the ship; players compared it
            // with the original's M6 A4 "Raccoon" and asked for it back.)
            var dir = gun.BeamDir.sqrMagnitude > 1e-9f ? gun.BeamDir : Vector3.forward;
            float length = gun.BeamLengthUnits;
            // setDirection(beamDir, up (0, 1, 0)), world up (the crossed planes don't roll with the ship); a beam fired
            // straight up / down takes the ship's up instead.
            var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.999f && ship != null ? ship.up : Vector3.up;
            t.SetPositionAndRotation(from, Quaternion.LookRotation(dir, up));
            t.localScale = new Vector3(1f, 1f, length);
            if (beamMs <= 0f) t.gameObject.SetActive(false);
        }

        void projectilesDone(float dtMs, Camera cam)
        {
            if (muzzle != null && muzzleMs > 0f)
            {
                muzzleMs -= dtMs;
                if (muzzleMs <= 0f)
                {
                    muzzle.SetActive(false);
                    foreach (var e in extraMuzzles) if (e.go != null) e.go.SetActive(false);
                }
                // ObjectGun::update 0x18c2ec: along the ship's nose with the camera's up (turrets: the turret's matrix).
                else if (gun.kind != Gun.Kind.Turret && ship != null && cam != null)
                {
                    muzzle.transform.rotation = Quaternion.LookRotation(ship.forward, cam.transform.up);
                    foreach (var e in extraMuzzles) if (e.go != null) e.go.transform.rotation = muzzle.transform.rotation;
                }
            }
            if (impacts != null)
                for (int i = 0; i < impacts.Length; i++)
                {
                    if (impactMs[i] <= 0f) continue;
                    impactMs[i] -= dtMs;
                    if (impactMs[i] <= 0f) impacts[i].SetActive(false);
                    else if (cam != null) impacts[i].transform.rotation = cam.transform.rotation;   // "_lookat" meshes
                }
        }

        /// <summary>Hides every projectile (a dead or sleeping ship).</summary>
        public void HideAll()
        {
            foreach (var t in projectiles) if (t != null) t.gameObject.SetActive(false);
            if (shown != null) System.Array.Clear(shown, 0, shown.Length);
            for (int i = 0; i < gun.bullets.Length; i++) gun.bullets[i].timer = -1e9f;
            if (trails != null) foreach (var tr in trails) tr.Clear();
            if (flames != null) foreach (var f in flames) if (f != null) f.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
