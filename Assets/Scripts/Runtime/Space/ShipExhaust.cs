// ShipExhaust.cs
// The player ship's exhaust particles (Level::initParticleSystems 0xcc990; the NPCs have none, only their _engine_add
// meshes; the Trail class for NPC trails is never constructed in this build). One sprite system per weapons_hd slot-3
// anchor i (its scale s = the anchor's "turretAngles"), record 29 + i, material 20090 (additive particles.png):
//   one particle every 8 units travelled, pool 20, size 250 s.x shrinking 1000/s, life 80 min(1.5 s.x, 1) ms,
//   velocity -4000 min(1.5 s.x, 1) u/s along the ship axis + 0.8 x the ship's + -100..100 random, colour 0xDDDDDD -> 0,
//   the ship's cell of particles.png (table 0x252ba0: 3 / 2 / 0 / 1 / 8 / 9 = the first six cells of the 8 x 8 grid)
//   PlayerEgo::update ~0xa9..: while boosting the size x (1 + 0.5 ramp), the ramp up over the boost's first sixth, down
//                               over its last
//   PlayerEgo::setExhaustVisible 0xa637c   off with the engine glow (mining, object docking, cutscenes, death)
//   Level::setPlayerEngineColor            the start colour grey clamp(221 - 2.01 x cloak %)
// Remake: a mod ship's exhaust mount with a glowColor (else the ship's engineGlowColor, ships.json) gets that colour as its
// start colour, multiplied onto the cell (CellColour, measured from the texture: orange, blue, teal, green, red, purple)
// whose product comes closest to it: six cells alone can't show a yellow-green or a pale orange.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class ShipExhaust : MonoBehaviour
    {
        const float M = 0.05f;
        static readonly int[] ShipCell =
        {
            3, 0, 8, 3, 2, 0, 3, 0, 9, 1, 0, 8, 2, 0, 0, 0, 2, 0, 2, 3, 3, 2, 0, 8, 8, 8, 0, 0, 0, 8, 3, 2,
            8, 0, 0, 2, 0, 0, 0, 1, 0, 1, 1, 2, 1, 3, 3, 3, 3, 1, 1, 0, 8, 1, 1, 0, 3, 2, 0, 0, 8, 1, 3, 1,
        };

        // particles.png cells 0..5, their brightness-weighted mean colours (max channel 1)
        static readonly Color[] CellColour =
        {
            new Color(1f, 0.761f, 0.363f), new Color(0.373f, 0.495f, 1f), new Color(0.368f, 0.862f, 1f),
            new Color(0.362f, 1f, 0.701f), new Color(1f, 0.374f, 0.458f), new Color(0.708f, 0.378f, 1f),
        };

        ShipController ship;
        PlayerHealth health;
        PlayerCloak cloak;
        int nextCloakLook;
        GameObject glow;
        readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        readonly List<float> baseSizes = new List<float>();
        readonly List<Color> tints = new List<Color>();
        bool on;
        float shownBoost = float.NaN, shownGrey = float.NaN;

        /// <summary>Remake (performance, 2026-10-22): another ship's plume (AttachRemote) is only emitted within this distance
        /// of the camera (metres). Its particles are 12.5 m quads living 80 ms: 4 km out they are under 4 pixels on a 1080p
        /// screen, beside the ship's engine glow, which stays; 20 fleet battles ran ~590 such emitters (~4 ms a frame).</summary>
        const float RemoteReach = 4000f;
        static int camFrame = -1;
        static Vector3 camPos;
        static bool camKnown;

        /// <summary>'at' is within RemoteReach of the main camera (no camera: true). The camera is read once a frame.</summary>
        static bool NearCamera(Vector3 at)
        {
            if (camFrame != Time.frameCount)
            {
                camFrame = Time.frameCount;
                var cam = Camera.main;
                camKnown = cam != null;
                if (camKnown) camPos = cam.transform.position;
            }
            return !camKnown || (at - camPos).sqrMagnitude <= RemoteReach * RemoteReach;
        }

        /// <summary>Remake (the Mod Builder's engine slider): the plume of a ship standing still, as at this fraction of full
        /// throttle (0 = none). The particles come per distance flown (one per 8 units), so a parked ship showed none; this
        /// adds the rate a ship at that share of the base speed of 2 units/ms would have (250 a second at full) over time.</summary>
        public float stationary;
        /// <summary>The boost look while standing still (0..1, like BoostVisualPercent): the plume grows as in a boost.</summary>
        public float stationaryBoost;
        float stationaryApplied;
        const float CruiseUnitsPerSecond = 2000f;

        public static ShipExhaust Attach(GameObject player, Database db, ShipController ship, int shipIndex)
        {
            var e = player.AddComponent<ShipExhaust>();
            e.ship = ship;
            e.Setup(db, ship.visualModel != null ? ship.visualModel : player.transform, shipIndex);
            return e;
        }

        // Multiplayer: another player's ship (NetPlayer), its state from their game.
        Func<bool> remoteOn;
        Func<float> remoteBoost, remoteCloak;

        /// <summary>Multiplayer (NetPlayer): the exhaust of another player's ship 'model', driven by their game's engine state
        /// (on: the engine glow shows), boost (0..1) and cloak (0..100).</summary>
        /// 'scaled': the plume shrinks with the ship's own scale (the hangar flights shrink a ship to nothing at the view's edge).
        /// 'mounts': the exhaust mounts the model shows (another player's kit ship build), null = Database.MountsOf's.
        public static ShipExhaust AttachRemote(GameObject host, Database db, Transform model, int shipIndex, Func<bool> on, Func<float> boost, Func<float> cloak,
                                               bool scaled = false, System.Collections.Generic.IList<WeaponMount> mounts = null)
        {
            var e = host.AddComponent<ShipExhaust>();
            e.remoteOn = on;
            e.remoteBoost = boost;
            e.remoteCloak = cloak;
            e.Setup(db, model, shipIndex, scaled, mounts);
            return e;
        }

        void Setup(Database db, Transform parent, int shipIndex, bool scaled = false, System.Collections.Generic.IList<WeaponMount> mounts = null)
        {
            shownBoost = float.NaN;   // the start size and colour are written again
            var mat = ExhaustMaterial(CombatAssets.Load()?.particlesMaterial);
            int value = shipIndex >= 0 && shipIndex < ShipCell.Length ? ShipCell[shipIndex] : 0;
            int cell = value switch { 3 => 0, 2 => 1, 1 => 3, 8 => 4, 9 => 5, _ => 2 };
            var asm = parent.GetComponent<Visuals.AssembledObject>();
            if (asm != null && asm.playerVariantParts != null && asm.playerVariantParts.Length > 0) glow = asm.playerVariantParts[0];
            var custom = CustomShips.Get(shipIndex);
            // A mod ship with its own engine flames (Modding.EngineFlame: EVERSPACE 2's) or Galaxy on Fire 3's engine effect
            // (Modding.EngineFx) has no particle plume.
            bool flames = parent.GetComponentInChildren<Modding.EngineFlame>(true) != null || parent.GetComponentInChildren<Modding.EngineFx>(true) != null;
            foreach (var m in flames ? new WeaponMount[0] : mounts ?? db.MountsOf(shipIndex, 3))
            {
                var want = m.glowColor != null && m.glowColor.Length >= 3 ? m.glowColor : custom?.engineGlowColor;
                int mountCell = cell;
                var tint = Color.white;
                if (want != null && want.Length >= 3) mountCell = CellFor(new Color(want[0], want[1], want[2]), out tint);
                float s = m.turretAngles != null && m.turretAngles.Length > 0 ? m.turretAngles[0] : 1f;
                float k = Mathf.Min(1.5f * s, 1f);
                var go = new GameObject("Exhaust");
                go.transform.SetParent(parent, false);
                float life = 0.08f * k, size = 250f * s;
                // Remake (players' reports, #85): the plume starts a quarter of a particle's size behind the anchor. Each
                // particle is a camera-facing quad of 1.3 x its size centred on the anchor, so seen from the side or a rear
                // corner the newest one lay over the engine housing (the Dark Angel's ~100-unit housings under a 325-unit
                // quad). Half the size cleared them but uncovered the engine glow rings in the chase view.
                go.transform.localPosition = WeaponSystem.MountToLocal(m) + Vector3.back * (PlumeSetback * size * M);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = false;
                main.startLifetime = life;
                // Backwards along the ship at 4000 k u/s (+-100 u/s), given at emission in the direction the nozzle points
                // then (the shape below), the particle keeping it in the world: in a turn the plume bends into a streak
                // behind the ship. #114: as a local-space velocity over the lifetime it was re-applied in the ship's
                // current orientation every frame, so the plume swung round rigidly with the ship and stayed a blob.
                main.startSpeed = new ParticleSystem.MinMaxCurve(-4000f * k * M - 100f * M, -4000f * k * M + 100f * M);
                main.startSize = size * M;
                main.startColor = (Color)new Color32(0xDD, 0xDD, 0xDD, 0xFF);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                if (scaled) main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
                main.maxParticles = 20;
                // IParticleSystem::emit (0x1b2790) writes a ring: at the 20-particle cap the newest replaces the oldest, so the
                // plume always starts at the nozzle. Unity's default drops new ones while the pool is full: at 2 u/ms the
                // plume needs ~20 and more boosting, cruising or strafing, so a gap opened behind the nozzle and it flickered
                // between full and not (Discord report "Thruster fire gap"). An expired particle held here is black with 0
                // alpha under additive blending: unseen.
                main.ringBufferMode = ParticleSystemRingBufferMode.PauseUntilReplaced;
                var em = ps.emission;
                em.rateOverTime = 0f;
                em.rateOverDistance = 1f / (8f * M);   // one per 8 units
                // The emission direction: the emitter's +z (the ship's nose; the negative start speed sends the particle back),
                // from the anchor itself.
                var sh = ps.shape;
                sh.enabled = true;
                sh.shapeType = ParticleSystemShapeType.Cone;
                sh.angle = 0f;
                sh.radius = 0.0001f;
                sh.radiusThickness = 0f;
                // +-100 u/s of jitter across, plus 0.8 x the ship's velocity (inheritVelocity).
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-100f * M, 100f * M);
                vel.y = new ParticleSystem.MinMaxCurve(-100f * M, 100f * M);
                vel.z = new ParticleSystem.MinMaxCurve(-100f * M, 100f * M);
                var iv = ps.inheritVelocity;
                iv.enabled = true;
                iv.mode = ParticleSystemInheritVelocityMode.Initial;
                iv.curve = new ParticleSystem.MinMaxCurve(0.8f);
                // size - 1000/s over the life
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, Mathf.Max(0f, (size - 1000f * life) / size)));
                // 0xDDDDDD -> black (additive: the RGB fade)
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                // The cell as a camera-facing quad: a texture-sheet cell reached the cell's edge, and the filter pulled in the
                // trail strips that start right under the glows (a purple / red line under every particle: lines behind the
                // thrusters). The cell is its own clamped texture with its own mipmaps (CellMaterial): an inset into the
                // atlas held only at full size, the fx atlases' mipmaps (as the original) brought the lines back.
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Mesh;
                var cellMat = CellMaterial(mat, mountCell);
                r.mesh = CellQuad(cellMat != mat ? -1 : mountCell);
                r.alignment = ParticleSystemRenderSpace.View;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (cellMat != null) r.sharedMaterial = cellMat;
                systems.Add(ps);
                baseSizes.Add(size * M);
                tints.Add(tint);
            }
        }

        /// <summary>The particles.png cell whose colour, multiplied by the start colour 'tint' ('c' with its brightest channel
        /// at 1; particle colours clamp at 1), comes closest to 'c' (both scaled to a brightest channel of 1), favouring
        /// bright products.</summary>
        static int CellFor(Color c, out Color tint)
        {
            float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            tint = peak > 1e-4f ? new Color(c.r / peak, c.g / peak, c.b / peak, 1f) : Color.white;
            var want = new Vector3(tint.r, tint.g, tint.b);
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < CellColour.Length; i++)
            {
                var k = CellColour[i];
                var got = new Vector3(k.r * tint.r, k.g * tint.g, k.b * tint.b);
                float m = Mathf.Max(got.x, Mathf.Max(got.y, got.z));
                if (m <= 1e-4f) continue;
                float d = (got / m - want).sqrMagnitude + 0.5f * (1f - m);   // and not a dim one (a blue cell for yellow-green)
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Remake, matched to a screenshot of the original (the Inflict from behind on the chase camera, the same framing): the
        // original's particle quads are wider than a unit quad of 'size' (ParticleSystemMesh::setParticle's billboard reads
        // as centre +- size, twice as wide, but that measured too wide on screen; 1.3 x matched), and its additive blending
        // in gamma space saturates the overlapping particles to white, which the remake's linear HDR pipeline doesn't: the
        // exhaust gets its own copy of the particles material at _Glow 7 (the shared one is 2.5). So bright, the plume also
        // covers the engine glow's dashed ring the way the original's does.
        const float QuadScale = 1.3f;
        const float PlumeSetback = 0.25f;   // x the particle size, along the ship's -Z (see Setup)
        const float ExhaustGlow = 7f;
        static Material exhaustMaterial;

        static Material ExhaustMaterial(Material particles)
        {
            if (particles == null) return null;
            if (exhaustMaterial == null || exhaustMaterial.shader != particles.shader)
            {
                exhaustMaterial = new Material(particles) { name = particles.name + " (exhaust)" };
                exhaustMaterial.SetFloat("_Glow", ExhaustGlow);
            }
            return exhaustMaterial;
        }

        static readonly Material[] cellMaterials = new Material[64];
        static readonly RenderTexture[] cellTextures = new RenderTexture[64];

        /// <summary>The cell texels (of 128) left out on each side of a cell texture (CellMaterial), the quad (CellQuad(-1))
        /// shrunk to match. The six glows lie within texels 10..121 and fade out before 116; the trail strips start under
        /// cells 0-2 (row 127 of the png already holds their soft edge). Android report (OnePlus 7T, "thin particles under
        /// the thrusters"): its Low preset loads textures at half size (Texture quality) and compresses them as ASTC 6x6,
        /// whose blocks straddle the cell's last rows, so the copy's bottom row took half the strip's brightness (measured:
        /// 0.53, 0.06 at full size) and the exhaust's _Glow 7 drew it as a line under every plume. 10 texels keep the copy's
        /// samples out of any texel or 6 x 6 block that touches the strips down to quarter size (8 reach the block across
        /// the cell's edge at half size), where the glows are down to 1-3 / 255, so the quad's edge doesn't show.</summary>
        const int CellInset = 10;

        /// <summary>The exhaust material on cell 'cell' of the 8 x 8 particles.png alone: the cell's middle (CellInset)
        /// copied into a 128 px render texture (clamped, its own mipmaps), so no mip level samples the neighbouring cells.
        /// Without a graphics device (the dedicated server) the shared material and the inset atlas quad.</summary>
        static Material CellMaterial(Material exhaust, int cell)
        {
            if (exhaust == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return exhaust;
            var sheet = exhaust.mainTexture;
            if (sheet == null) return exhaust;
            var rt = cellTextures[cell];
            if (rt == null || !rt.IsCreated() || cellMaterials[cell] == null || cellMaterials[cell].shader != exhaust.shader)
            {
                if (rt == null)
                {
                    rt = new RenderTexture(new RenderTextureDescriptor(128, 128, RenderTextureFormat.ARGB32, 0)
                    {
                        useMipMap = true, autoGenerateMips = true, sRGB = true,
                    })
                    { name = $"ExhaustCell{cell}", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                    cellTextures[cell] = rt;
                }
                if (!rt.IsCreated()) rt.Create();
                const float step = 1f / 8f, inset = step * CellInset / 128f, size = step - 2f * inset;
                int cx = cell % 8, cy = cell / 8;   // rows from the top
                Graphics.Blit(sheet, rt, new Vector2(size, size), new Vector2(cx * step + inset, 1f - (cy + 1) * step + inset));
                var m = new Material(exhaust) { name = exhaust.name + $" cell {cell}" };
                m.mainTexture = rt;
                m.mainTextureScale = Vector2.one;
                m.mainTextureOffset = Vector2.zero;
                cellMaterials[cell] = m;
            }
            return cellMaterials[cell];
        }

        static readonly Mesh[] cellQuads = new Mesh[64];
        static Mesh fullQuad;

        /// <summary>A quad of QuadScale x the particle's size mapping cell 'cell' of the 8 x 8, 1024 px particles.png, inset half
        /// a texel; -1 = the whole texture (a cell texture of CellMaterial), the quad the size of the cell's middle it holds.</summary>
        internal static Mesh CellQuad(int cell)
        {
            if (cell < 0 ? fullQuad != null : cellQuads[cell] != null) return cell < 0 ? fullQuad : cellQuads[cell];
            const float px = 1f / 1024f, step = 1f / 8f;
            int cx = Mathf.Max(cell, 0) % 8, cy = Mathf.Max(cell, 0) / 8;   // rows from the top
            float u0 = cx * step + 0.5f * px, u1 = (cx + 1) * step - 0.5f * px;
            float v1 = 1f - cy * step - 0.5f * px, v0 = 1f - (cy + 1) * step + 0.5f * px;
            if (cell < 0) { u0 = v0 = 0f; u1 = v1 = 1f; }
            var m = new Mesh { name = cell < 0 ? "ExhaustCellTexture" : $"ExhaustCell{cell}" };
            float h = 0.5f * QuadScale * (cell < 0 ? (128f - 2f * CellInset) / 128f : 1f);
            m.vertices = new[] { new Vector3(-h, -h, 0f), new Vector3(h, -h, 0f), new Vector3(h, h, 0f), new Vector3(-h, h, 0f) };
            m.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            if (cell < 0) return fullQuad = m;
            return cellQuads[cell] = m;
        }

        void Update()
        {
            if (ship == null && remoteOn == null) return;
            bool want;
            if (remoteOn != null) want = remoteOn() && NearCamera(transform.position);
            else
            {
                if (health == null) health = GetComponent<PlayerHealth>();
                // Only ships with a cloak have one: looked for twice a second, not every frame (added later by a debug mount).
                if (cloak == null && Time.frameCount >= nextCloakLook) { cloak = GetComponent<PlayerCloak>(); nextCloakLook = Time.frameCount + 30; }
                // Free flight with no forward thrust held: the engines are off (FlightModel.EnginesOff).
                want = (glow == null || glow.activeInHierarchy) && (health == null || !health.Dead) && (ship.Model == null || !ship.Model.EnginesOff)
                       && (ship.visualModel == null || ship.visualModel.gameObject.activeInHierarchy);
            }
            if (want != on)
            {
                on = want;
                foreach (var ps in systems) if (on) ps.Play(true); else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (stationary != stationaryApplied)
            {
                stationaryApplied = stationary;
                foreach (var ps in systems) { var em = ps.emission; em.rateOverTime = Mathf.Clamp01(stationary) * CruiseUnitsPerSecond / 8f; }
            }
            if (!on) return;
            float boost = remoteBoost != null ? remoteBoost() : ship.Model != null ? ship.Model.BoostVisualPercent : 0f;
            boost = Mathf.Max(boost, stationaryBoost);
            float cloakPct = remoteCloak != null ? remoteCloak() : cloak != null && cloak.Rules != null ? cloak.Rules.Percentage : 0f;
            float grey = Mathf.Clamp(221f - 2.01f * cloakPct, 0f, 255f) / 255f;
            if (boost == shownBoost && grey == shownGrey) return;   // a native call per setting and system otherwise
            shownBoost = boost;
            shownGrey = grey;
            for (int i = 0; i < systems.Count; i++)
            {
                var main = systems[i].main;
                main.startSize = baseSizes[i] * (1f + 0.5f * boost);
                var t = tints[i];
                main.startColor = new Color(grey * t.r, grey * t.g, grey * t.b, 1f);
            }
        }
    }
}

