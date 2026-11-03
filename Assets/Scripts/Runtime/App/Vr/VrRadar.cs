// VrRadar.cs
// Remake VR: the cockpit's radar scope in the middle of the dashboard (VrCockpit; the original has no scope: its radar is
// the HUD's markers and the ellipse). Top-down around the ship, forward up: the ships within 3 km (60 000 units) as dots,
// red hostile / green friendly / yellow neutral (Target.hostileToPlayer / friendToPlayer, like the HUD markers; the
// player's wingmen blue, Target.wingman, #216), the
// distance square-root scaled (near ones spread out), farther ones on the rim; the station cyan at the orbit's origin; the
// player a white dot in the middle. Ships only with a scanner (CombatRadar.HasScanner, as the HUD's ship markers);
// cloaked, hidden and untargetable ships never. A dot above the ship is a little bigger, below a little smaller.

using System.Collections.Generic;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.Vr
{
    public sealed class VrRadar : MonoBehaviour
    {
        const float Radius = 0.085f, DotSize = 0.009f, RangeMetres = 3000f;

        readonly List<Transform> dots = new List<Transform>();
        Material red, green, blue, yellow, cyan, white, disc;
        Texture2D discTexture;
        Transform stationDot;

        public void Init()
        {
            discTexture = DiscTexture(256);
            disc = VrPanels.ScreenMaterial(3001);
            disc.SetTexture("_BaseMap", discTexture);
            var bg = Quad("Radar scope", Vector3.zero, Radius * 2f, disc);
            bg.localPosition = new Vector3(0f, 0f, 0.0005f);
            red = Dot(new Color(1f, 0.25f, 0.15f));
            green = Dot(new Color(0.3f, 1f, 0.35f));
            blue = Dot(new Color(0.31f, 0.7f, 1f));   // the wingmen (#216), the HUD's blue friend label #4FB2FF
            yellow = Dot(new Color(1f, 0.85f, 0.25f));
            cyan = Dot(new Color(0.3f, 0.9f, 1f));
            white = Dot(Color.white);
            Quad("Radar self", Vector3.zero, DotSize * 1.2f, white);
            stationDot = Quad("Radar station", Vector3.zero, DotSize * 1.6f, cyan);
        }

        static Material Dot(Color c)
        {
            var m = VrPanels.ScreenMaterial(3002);
            m.SetColor("_BaseColor", c);
            return m;
        }

        Transform Quad(string name, Vector3 position, float size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = new Vector3(size, size, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>The scope's face: a dark disc, range rings at 1/3 and 2/3 (square-root scale: ~330 m / 1.3 km), a rim, the
        /// forward line.</summary>
        static Texture2D DiscTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "VR radar" };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            var rim = new Color32(80, 170, 220, 255);
            var ring = new Color32(60, 120, 160, 200);
            var face = new Color32(4, 14, 22, 235);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c, r = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    Color32 col = new Color32(0, 0, 0, 0);
                    if (r <= 1f) col = face;
                    if (r > 0.96f && r <= 1f) col = rim;
                    else if (Mathf.Abs(r - 1f / 3f) < 0.008f || Mathf.Abs(r - 2f / 3f) < 0.008f) col = ring;
                    else if (r < 0.96f && Mathf.Abs(dx) < 0.8f && dy > 0f) col = ring;   // the forward line
                    px[y * size + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        Vector3 OnScope(Transform ship, Vector3 world, out float sizeFactor)
        {
            var local = ship.InverseTransformPoint(world);
            var flat = new Vector2(local.x, local.z);
            float dist = flat.magnitude;
            float r = Mathf.Sqrt(Mathf.Min(dist / RangeMetres, 1f)) * Radius * 0.94f;
            var p = dist > 1e-3f ? flat / dist * r : Vector2.zero;
            sizeFactor = 1f + Mathf.Clamp(local.y / RangeMetres * 2f, -0.4f, 0.4f);
            return new Vector3(p.x, p.y, -0.0005f);
        }

        void LateUpdate()
        {
            var level = FindAnyObjectByType<World.SpaceLevel>();
            var ship = level != null && level.Player != null ? level.Player.transform : null;
            int used = 0;
            if (ship != null)
            {
                stationDot.gameObject.SetActive(level.Layout != null && level.Layout.stationObject);
                stationDot.localPosition = OnScope(ship, Vector3.zero, out _);
                var self = level.Health != null ? level.Health.Target : null;
                bool scanner = level.Radar != null && level.Radar.HasScanner;
                if (scanner)
                    foreach (var t in Target.All)
                    {
                        if (t == null || t == self || !t.isShip || !t.Alive || t.untargetable || t.cloaked || !t.isActiveAndEnabled) continue;
                        if (used >= 64) break;
                        if (used == dots.Count) dots.Add(Quad("Radar dot", Vector3.zero, DotSize, yellow));
                        var d = dots[used++];
                        d.gameObject.SetActive(true);
                        d.localPosition = OnScope(ship, t.transform.position, out float k);
                        d.localScale = new Vector3(DotSize * k, DotSize * k, 1f);
                        d.GetComponent<MeshRenderer>().sharedMaterial = t.hostileToPlayer ? red : t.friendToPlayer ? (t.wingman ? blue : green) : yellow;
                    }
            }
            for (int i = used; i < dots.Count; i++) if (dots[i].gameObject.activeSelf) dots[i].gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            foreach (var m in new[] { red, green, blue, yellow, cyan, white, disc }) if (m != null) Destroy(m);
            if (discTexture != null) Destroy(discTexture);
        }
    }
}
