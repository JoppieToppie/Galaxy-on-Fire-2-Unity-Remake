// ShipIconMaker.cs
// A mod ship's shop icon made in the game (the Mod Builder: a .glb ship has no icon of its own), the look of the Editor's
// GoF2 > Build > Hull Icons (EditorTools.HullIconBuilder) and so of the original's ship icons: the model from its left side,
// above and a little ahead, the nose dipping toward the bottom left, lit warm from the front and cool from behind,
// orthographic, 4x supersampled, its alpha from a render on black and one on white, fitted to the pixels drawn, over the
// blue striped ship frame 1276 (Resources/GoF2Icons/frame_ship.png, written by that builder) on a 180 x 88 canvas.
// Never turned: HullIconBuilder lays a hull wider than long along the icon, which suits its freighters and capital ships,
// but a mod ship's nose is +Z, and a wide-winged fighter (the Betty, 39 x 28 m) came out with its nose away from the camera.
// It renders on a stage far below the station on the item window's layer (30); while it renders, the scene's own lights,
// ambient and fog are kept off that layer so every icon is lit alike whatever hangar the builder is in.

using System.Collections.Generic;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoF2Remake.Modding
{
    public static class ShipIconMaker
    {
        const int Layer = 30, IconW = 178, IconH = 86, PadW = 180, PadH = 88, Super = 4;
        static readonly Vector3 Stage = new Vector3(0f, -90000f, 60000f);   // away from the item window's stages
        static readonly Vector3 ViewFrom = new Vector3(-1f, 0.55f, 0.3f);   // HullIconBuilder.ViewFrom
        const float Roll = -15f;

        /// <summary>The icon of a ship template (a ModShips template or any assembled ship prefab) as a texture, null when
        /// nothing could be drawn. 'turn': degrees about the ship's up axis, for a model whose nose isn't its +Z.</summary>
        public static Texture2D Make(GameObject prefab, float turn = 0f)
        {
            if (prefab == null) return null;
            var stage = new GameObject("Ship icon stage");
            stage.transform.position = Stage;
            var rt = new RenderTexture(IconW * Super, IconH * Super, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var lights = new List<(Light light, int mask)>();
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            bool fog = RenderSettings.fog;
            try
            {
                var go = Object.Instantiate(prefab, stage.transform, false);
                go.SetActive(true);
                var asm = go.GetComponent<AssembledObject>();
                if (asm != null)
                {
                    asm.SetPlayerVariant(false);
                    // The engine glows and flames aren't drawn: the icon shows the hull.
                    foreach (var parts in new[] { asm.playerVariantParts, asm.npcVariantParts })
                        if (parts != null) foreach (var p in parts) if (p != null) p.SetActive(false);
                }
                GoF2Remake.Visuals.AutoLods.FullDetail(go);
                foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    if (r is TrailRenderer || r is ParticleSystemRenderer || r is LineRenderer) r.enabled = false;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                go.transform.rotation = Quaternion.Euler(0f, turn, 0f);   // a ship's nose is +Z: the view alone dips it to the bottom left
                var bounds = Bounds(go);
                if (bounds.size == Vector3.zero) return null;

                var camGo = new GameObject("Ship icon camera");
                camGo.transform.SetParent(stage.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;   // renders only when asked
                cam.cullingMask = 1 << Layer;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.orthographic = true;
                cam.aspect = (float)IconW / IconH;
                cam.allowHDR = false;
                cam.allowMSAA = true;
                cam.allowDynamicResolution = false;
                cam.targetTexture = rt;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
                // HullIconBuilder's lights: a warm key from the front above, a cool fill from behind below.
                AddLight(stage.transform, new Vector3(-0.4f, -0.6f, -0.7f), new Color(1f, 0.93f, 0.82f), 1.5f);
                AddLight(stage.transform, new Vector3(0.3f, 0.5f, 0.8f), new Color(0.55f, 0.65f, 0.8f), 0.6f);
                // The scene's lights off the layer, a plain ambient and no fog while rendering.
                foreach (var l in Object.FindObjectsByType<Light>())
                    if (l.transform.parent != stage.transform) { lights.Add((l, l.cullingMask)); l.cullingMask &= ~(1 << Layer); }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.22f, 0.23f, 0.26f);
                RenderSettings.fog = false;

                Frame(cam, bounds);
                var black = Render(cam, rt, Color.black);
                var white = Render(cam, rt, Color.white);
                if (!Refit(cam, rt.width, rt.height, black, white)) return null;
                black = Render(cam, rt, Color.black);
                white = Render(cam, rt, Color.white);
                return Compose(black, white, LoadFrame());
            }
            finally
            {
                foreach (var (l, mask) in lights) if (l != null) l.cullingMask = mask;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.fog = fog;
                Object.DestroyImmediate(stage);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        static void AddLight(Transform parent, Vector3 dir, Color colour, float intensity)
        {
            var go = new GameObject("Ship icon light");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.LookRotation(dir);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = colour;
            l.intensity = intensity;
            l.cullingMask = 1 << Layer;
            l.shadows = LightShadows.None;
        }

        /// <summary>The drawn meshes' bounds (no trails or particles, no glow layers).</summary>
        static Bounds Bounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r.name.Contains("_add")) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b : new Bounds();
        }

        static void Frame(Camera cam, Bounds b)
        {
            float radius = Mathf.Max(b.extents.magnitude, 0.01f);
            var t = cam.transform;
            t.position = b.center + ViewFrom.normalized * radius * 4f;
            t.rotation = Quaternion.LookRotation(-ViewFrom.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, Roll);
            cam.nearClipPlane = radius * 0.5f;
            cam.farClipPlane = radius * 8f;
            float halfW = 0f, halfH = 0f;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = t.InverseTransformPoint(c);
                halfW = Mathf.Max(halfW, Mathf.Abs(p.x));
                halfH = Mathf.Max(halfH, Mathf.Abs(p.y));
            }
            cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect) * 1.05f;
        }

        /// <summary>Re-centres and zooms on the drawn pixels' box (alpha over 0.1), a 4 % margin; false when nothing was drawn.</summary>
        static bool Refit(Camera cam, int w, int h, Color32[] black, Color32[] white)
        {
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (Alpha(black[i], white[i]) < 0.1f) continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            if (x1 < 0) return false;
            float perPixel = cam.orthographicSize * 2f / h;
            var t = cam.transform;
            float cx = ((x0 + x1 + 1) * 0.5f - w * 0.5f) * perPixel, cy = ((y0 + y1 + 1) * 0.5f - h * 0.5f) * perPixel;
            t.position += t.right * cx + t.up * cy;
            float halfW = (x1 - x0 + 1) * 0.5f * perPixel, halfH = (y1 - y0 + 1) * 0.5f * perPixel;
            cam.orthographicSize = Mathf.Max(halfH, halfW / cam.aspect) * 1.04f;
            return true;
        }

        /// <summary>White - black = what shows through.</summary>
        static float Alpha(Color32 b, Color32 w) => Mathf.Clamp01(1f - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / (3f * 255f));

        static Color32[] Render(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            Object.Destroy(tex);
            return px;
        }

        /// <summary>Down to 178 x 86 (a box filter), the alpha from the two backgrounds, over the frame on 180 x 88.</summary>
        static Texture2D Compose(Color32[] black, Color32[] white, Color32[] frame)
        {
            int w = IconW * Super;
            var canvas = new Color[PadW * PadH];
            if (frame != null) for (int i = 0; i < canvas.Length; i++) canvas[i] = frame[i];
            int ox = (PadW - IconW) / 2, oy = (PadH - IconH) / 2;
            for (int y = 0; y < IconH; y++)
                for (int x = 0; x < IconW; x++)
                {
                    float r = 0f, g = 0f, bl = 0f, alpha = 0f;
                    for (int sy = 0; sy < Super; sy++)
                        for (int sx = 0; sx < Super; sx++)
                        {
                            int i = (y * Super + sy) * w + x * Super + sx;
                            var b = black[i];
                            alpha += Alpha(b, white[i]);
                            r += b.r / 255f; g += b.g / 255f; bl += b.b / 255f;   // on black: colour x alpha
                        }
                    if (alpha <= 0.01f) continue;
                    var src = new Color(r / alpha, g / alpha, bl / alpha);   // un-premultiplied
                    alpha /= Super * Super;
                    ref var d = ref canvas[(oy + y) * PadW + ox + x];
                    float outA = alpha + d.a * (1f - alpha);
                    var rgb = (src * alpha + new Color(d.r, d.g, d.b) * d.a * (1f - alpha)) / Mathf.Max(outA, 1e-4f);
                    d = new Color(Mathf.Clamp01(rgb.r), Mathf.Clamp01(rgb.g), Mathf.Clamp01(rgb.b), outA);
                }
            var o = new Texture2D(PadW, PadH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "mod ship icon" };
            o.SetPixels(canvas);
            o.Apply();
            return o;
        }

        /// <summary>The ship frame's pixels (180 x 88), null when it isn't in the build or isn't readable.</summary>
        static Color32[] LoadFrame()
        {
            var t = GoF2Remake.Data.OriginalTextures.Image("GoF2Icons/frame_ship");
            if (t == null || !t.isReadable || t.width != PadW || t.height != PadH) return null;
            return t.GetPixels32();
        }
    }
}
