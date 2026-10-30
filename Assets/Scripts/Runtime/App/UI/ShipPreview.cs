// ShipPreview.cs
// A turning 3D ship in a UI box (ListItemWindow's preview, Reference/research/shop.md 2.4): the player variant of the ship
// (Globals::getShipGroup(idx, race, true)) on a stage far below the station, on layer 30, its own camera at the original's
// direction ((1362, 1690, -5257) game units, turned -0.1 rad) framing the ship's bounds, rendered into a RenderTexture shown
// over the floor glow 0x50b (drawn once and mirrored). Turning: yaw only, 120 px = 1 rad, starting at 260 px; a drag turns
// it, a release over 3 px keeps turning x0.9 per original frame (OriginalFrame.Ms) and stops at 1 px.
// Used by the full-screen item window (ItemInfoWindow) and (remake, #82) the hangar's details panel, which also spins it
// slowly while it isn't dragged (AutoSpin) and opens the item window on a click / tap (Clicked).
// Remake: the camera isn't rolled (the original's -0.1 rad left the ships leaning left over the level floor), and the
// item window's floor moves under the ship (FloorUnderShip): the centre of its inner ring below the turning axis, at the
// ship's lowest point, so the ship stands on it like on a platform.

using System;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using GoF2Remake.Flight;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class ShipPreview
    {
        const int PreviewLayer = 30;
        const float M = 0.05f, FrameMs = OriginalFrame.Ms, PixelsPerRadian = 120f, ClickSlop = 10f;

        /// <summary>The box: the floor glow and the rendered ship; drags on it turn the ship.</summary>
        public readonly VisualElement Element;
        /// <summary>A click / tap on the box that didn't turn it.</summary>
        public event Action Clicked;
        /// <summary>Turning by itself while not dragged or flung, in pixels per original frame (0 = still).</summary>
        public float AutoSpin;
        public bool Shown => model != null;
        /// <summary>The ship shown, -1 = none.</summary>
        public int Ship { get; private set; } = -1;
        /// <summary>Remake: the floor glow is placed under the ship (the item window) instead of at its fixed place.</summary>
        public bool FloorUnderShip;
        /// <summary>Remake: the box keeps the rendered image's proportions (its height follows its width), like the item window.</summary>
        public bool KeepAspect;
        /// <summary>Remake (FloorUnderShip): the floor's height against the box's (0 = the stylesheet's).</summary>
        public float FloorHeightRatio;
        /// <summary>info_floor's inner ring centre: its height in the texture (0 = top).</summary>
        const float FloorRingCentre = 0.355f;

        readonly VisualElement image, floorLeft, floorRight;
        Vector3 footWorld;   // where the ship stands: under its turning axis, at its lowest point
        bool hasFoot;
        Camera cam;
        readonly System.Collections.Generic.List<Material> previewMaterials = new System.Collections.Generic.List<Material>();
        static Shader previewAdditive;
        readonly Vector3 stagePosition;
        readonly int rtWidth, rtHeight;
        readonly float framing;
        GameObject stage;
        Transform model;
        RenderTexture rt;
        float yawPx, flingPx, lastX, travel;
        int pointer = -1;

        static Texture2D Hud(string n) => Resources.Load<Texture2D>("GoF2Hud/" + n);

        /// <param name="stagePosition">where the ship stands (each preview its own place: two can be open at once)</param>
        /// <param name="framing">the camera distance against the one that just fits the ship's bounding sphere</param>
        public ShipPreview(Vector3 stagePosition, int rtWidth, int rtHeight, float framing)
        {
            this.stagePosition = stagePosition;
            this.rtWidth = rtWidth;
            this.rtHeight = rtHeight;
            this.framing = framing;
            Element = new VisualElement();
            Element.AddToClassList("ship-preview");
            floorLeft = new VisualElement { pickingMode = PickingMode.Ignore };
            floorLeft.AddToClassList("info-floor");
            floorRight = new VisualElement { pickingMode = PickingMode.Ignore };
            floorRight.AddToClassList("info-floor");
            floorRight.AddToClassList("info-floor--mirrored");
            var floor = Hud("info_floor");
            if (floor != null) { floorLeft.style.backgroundImage = new StyleBackground(floor); floorRight.style.backgroundImage = new StyleBackground(floor); }
            image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.AddToClassList("info-preview-image");
            Element.Add(floorLeft);
            Element.Add(floorRight);
            Element.Add(image);

            Element.RegisterCallback<PointerDownEvent>(e =>
            {
                if (pointer >= 0) return;
                pointer = e.pointerId;
                lastX = e.position.x;
                travel = 0f;
                flingPx = 0f;
                Element.CapturePointer(pointer);
                e.StopPropagation();
            });
            Element.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != pointer) return;
                float dx = e.position.x - lastX;
                lastX = e.position.x;
                travel += Mathf.Abs(dx);
                // The ship follows the finger: dragging right turns its near side right.
                yawPx -= dx;
                flingPx = -dx;
            });
            Element.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != pointer) return;
                EndDrag();
                if (Mathf.Abs(flingPx) <= 3f) flingPx = 0f;
                if (travel < ClickSlop) { flingPx = 0f; Clicked?.Invoke(); }
                e.StopPropagation();
            });
            Element.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == pointer) EndDrag(); });
            Element.RegisterCallback<PointerCaptureOutEvent>(_ => pointer = -1);
            Element.RegisterCallback<GeometryChangedEvent>(_ => { FitAspect(); FitTexture(); PlaceFloor(); });
            // Ticks itself while the panel updates (the fling and the spin).
            Element.schedule.Execute(Tick).Every(0);
            Element.style.display = DisplayStyle.None;
        }

        void EndDrag()
        {
            if (pointer >= 0 && Element.HasPointerCapture(pointer)) Element.ReleasePointer(pointer);
            pointer = -1;
        }

        /// <summary>Shows the ship (the same one again keeps its turn).</summary>
        public void Show(Database db, int ship)
        {
            Element.style.display = DisplayStyle.Flex;
            if (ship == Ship && model != null) return;
            Hide();
            Element.style.display = DisplayStyle.Flex;
            Ship = ship;
            var entry = db.ShipAssembly(ship);
            var prefab = entry != null ? AssembledObject.LoadPrefab(entry) : null;
            if (prefab == null) return;
            stage = new GameObject("Ship preview stage");
            stage.transform.position = stagePosition;
            // A station model to its engine effects (EngineDrive.IsStationModel): no flight trails or glows on the turning
            // ship. Before Instantiate: they take it in OnEnable.
            stage.AddComponent<GoF2Remake.Visuals.StationModelContext>();
            var go = Object.Instantiate(prefab, stage.transform, false);
            go.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
            GoF2Remake.Visuals.AutoLods.FullDetail(go);
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
            UsePreviewAdditive(go);
            NoReflections(go);
            RenderPipelineManager.beginCameraRendering += BeginPreviewRender;
            RenderPipelineManager.endCameraRendering += EndPreviewRender;
            model = go.transform;
            if (ship == GoF2Remake.World.StationLevel.OffCentreShip) CentreOnAxis(go);
            // The camera: (1362, 1690, -5257) game units at 1920 x 1080 -> Unity (x, y, -z) * 0.05, looking at the ship.
            var camGo = new GameObject("Ship preview camera");
            camGo.transform.SetParent(stage.transform, false);
            camGo.transform.localPosition = new Vector3(1362f, 1690f, 5257f) * M;
            cam = camGo.AddComponent<Camera>();
            cam.cullingMask = 1 << PreviewLayer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 0.9203f * Mathf.Rad2Deg;
            var bounds = new Bounds(stage.transform.position, Vector3.zero);
            // Meshes only: a trail or particle renderer (a mod ship's ThrottleGlow trails) is empty at the world origin, far
            // from the stage, and framed the ship from kilometres away.
            foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r is MeshRenderer || r is SkinnedMeshRenderer) bounds.Encapsulate(r.bounds);
            // Remake: the original's direction, at the distance that frames this ship (its offset was made for the
            // original's model scale, the remake's ships sat tiny in the box).
            float radius = Mathf.Max(bounds.extents.magnitude, 1f);
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * framing;
            camGo.transform.position = bounds.center + camGo.transform.localPosition.normalized * dist;
            cam.farClipPlane = dist + radius * 4f;
            cam.nearClipPlane = Mathf.Max(0.1f, dist - radius * 2f);
            camGo.transform.LookAt(bounds.center);
            // Remake: no roll. The original turns its camera -0.1 rad about its view axis; here that leant every ship to the
            // left of the level floor beneath it.
            // Where the ship stands: below its turning axis (the model's origin, which the yaw keeps) at its lowest point.
            footWorld = new Vector3(model.position.x, bounds.min.y, model.position.z);
            hasFoot = true;
            rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = UnityEngine.Device.Application.isMobilePlatform ? 2 : 4 };
            cam.targetTexture = rt;
            var lightGo = new GameObject("Ship preview light");
            lightGo.transform.SetParent(stage.transform, false);
            lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(-5f, -1f, -5f) * -1f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << PreviewLayer;
            light.intensity = 1f;
            // On only while its own camera draws: the PC renderer is Forward+, which ignores a light's culling mask, so this
            // light (pointing slightly up) lit the whole hangar while a ship row was selected and the ceiling went dark on an
            // item row (Discord report "[10.18] Hangar: Visual glitch").
            light.enabled = false;
            previewLight = light;
            if (!lightHooked)
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCamera;
                lightHooked = true;
            }
            image.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            yawPx = 260f;   // ListItemWindow: starts at 260 px
            flingPx = 0f;
            ApplyYaw();
            FitTexture();
            PlaceFloor();
        }

        /// <summary>Modified: an off-centre hull (the VoidX, StationLevel.OffCentreShip: its middle ~18 m along its length from
        /// its origin) turns about its own middle: under a pivot of its own (now the turned 'model'), its hull's middle on the
        /// turning axis (the glow layers left out of the measure, as in the hangar). It swung around the box's centre.</summary>
        void CentreOnAxis(GameObject go)
        {
            var hull = new Bounds();
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer) || r.name.Contains("_add")) continue;
                if (!any) { hull = r.bounds; any = true; } else hull.Encapsulate(r.bounds);
            }
            if (!any) return;
            var pivot = new GameObject("Ship preview pivot") { layer = PreviewLayer }.transform;
            pivot.SetParent(stage.transform, false);
            go.transform.SetParent(pivot, false);
            var c = hull.center - pivot.position;
            go.transform.localPosition = new Vector3(-c.x, 0f, -c.z);
            model = pivot;
        }

        /// <summary>Remake: the texture at the game's own resolution: as tall as the screen (1080 at 1080p, 2160 at 4K) and at
        /// least twice the box's height in screen pixels, as wide as the box's proportions make it. The fixed 1268 x 636 one was stretched over the box (soft in the big view).
        /// Same vertical field of view, so the ship keeps its size in the box; the UI scales it down, which keeps it sharp.</summary>
        void FitTexture()
        {
            if (cam == null || rt == null) return;
            float w = Element.resolvedStyle.width, h = Element.resolvedStyle.height;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;
            float ph = Mathf.Max(16f, Screen.height), pw;
            // At least twice the box's height on screen: the UI's trilinear sampling then reads a mip level at or above the
            // screen's own detail (a big box at only ~1.5x read half of it from the half-size level, the item window's view
            // looked low-resolution).
            float panelH = Element.panel != null ? Element.panel.visualTree.layout.height : float.NaN;
            bool mobile = UnityEngine.Device.Application.isMobilePlatform;   // the Device Simulator's phone too
            if (mobile)
            {
                // Remake: on phones the box at its own size on screen times the game's render scale (the Mobile asset's 0.8
                // or the option), no 2x supersampling: the screen-tall, twice-the-box texture was far more pixels than the
                // game itself draws and the frame rate dropped in the big item window.
                if (!float.IsNaN(panelH) && panelH > 0f) ph = h * Screen.height / panelH;
                var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
                ph *= urp != null ? Mathf.Clamp(urp.renderScale, 0.5f, 1f) : 1f;
            }
            else if (!float.IsNaN(panelH) && panelH > 0f) ph = Mathf.Max(ph, 2f * h * Screen.height / panelH);
            pw = ph * w / h;
            float fit = Mathf.Min(1f, 4096f / Mathf.Max(pw, ph));   // the texture size limit
            int tw = Mathf.Max(16, Mathf.RoundToInt(pw * fit)), th = Mathf.Max(16, Mathf.RoundToInt(ph * fit));
            int pwI = tw, phI = th;
            if (Mathf.Abs(rt.width - pwI) <= 2 && Mathf.Abs(rt.height - phI) <= 2) return;
            var old = rt;
            // Mipmapped: shown smaller than it is, the UI samples a matching level instead of skipping pixels (no shimmer);
            // the extra resolution is the anti-aliasing (MSAA can't be mipmapped).
            // On phones (shown at its own size) no mipmaps: no minification to smooth, and no mip chain to rebuild every frame.
            rt = mobile
                ? new RenderTexture(pwI, phI, 24, RenderTextureFormat.ARGB32) { antiAliasing = 2, filterMode = FilterMode.Bilinear }
                : new RenderTexture(pwI, phI, 24, RenderTextureFormat.ARGB32) { useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear };
            cam.targetTexture = rt;
            cam.ResetAspect();
            image.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            old.Release();
            Object.Destroy(old);
        }

        /// <summary>Modified: the thumbnail shows the bare hull, without the place's reflection cubemap (SkyReflection: the
        /// system's or the hangar's) or the rim light. GoF2/Hull's own terms (and EmissiveCube's) read globals, overridden
        /// here per renderer; URP's environment reflection is the custom reflection texture, swapped for a black cubemap
        /// while the preview camera renders (BeginPreviewRender / EndPreviewRender).</summary>
        void NoReflections(GameObject go)
        {
            var block = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(HullReflectionId, 0f);
                block.SetColor(HullRimId, Color.black);
                block.SetTexture(ReflectionCubeId, BlackCube);
                r.SetPropertyBlock(block);
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        Texture savedReflection;
        float savedReflectionIntensity;
        bool swapped;

        void BeginPreviewRender(ScriptableRenderContext context, Camera camera)
        {
            if (camera != cam || cam == null || swapped) return;
            savedReflection = RenderSettings.customReflectionTexture;
            savedReflectionIntensity = RenderSettings.reflectionIntensity;
            if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom) RenderSettings.customReflectionTexture = BlackCube;
            RenderSettings.reflectionIntensity = 0f;
            swapped = true;
        }

        void EndPreviewRender(ScriptableRenderContext context, Camera camera)
        {
            if (!swapped || (camera != cam && cam != null)) return;
            if (RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom) RenderSettings.customReflectionTexture = savedReflection;
            RenderSettings.reflectionIntensity = savedReflectionIntensity;
            swapped = false;
        }

        static readonly int HullReflectionId = Shader.PropertyToID("_GoF2HullReflection"), HullRimId = Shader.PropertyToID("_GoF2HullRimColor"),
            ReflectionCubeId = Shader.PropertyToID("_GoF2ReflectionCube");
        static Cubemap blackCube;
        static Cubemap BlackCube
        {
            get
            {
                if (blackCube != null) return blackCube;
                blackCube = new Cubemap(1, TextureFormat.RGBA32, false) { name = "ShipPreviewNoReflection", hideFlags = HideFlags.DontSave };
                for (int f = 0; f < 6; f++) blackCube.SetPixel((CubemapFace)f, 0, 0, Color.black);
                blackCube.Apply(false, true);
                return blackCube;
            }
        }

        /// <summary>Remake: the ship's additive lights (the Additive Shader Graph) drawn with GoF2/PreviewAdditive, which
        /// leaves the texture's alpha to their brightness: the graph's alpha blend left each light's whole sprite in it, a
        /// black star around the light once the UI laid the texture over the floor.</summary>
        void UsePreviewAdditive(GameObject go)
        {
            if (previewAdditive == null) previewAdditive = Shader.Find("GoF2/PreviewAdditive");
            if (previewAdditive == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null || !m.shader.name.EndsWith("Additive")) continue;
                    var pm = new Material(previewAdditive) { name = m.name + " (preview)", renderQueue = m.renderQueue };
                    if (m.HasProperty("_MainTex")) { pm.SetTexture("_MainTex", m.GetTexture("_MainTex")); pm.SetTextureScale("_MainTex", m.GetTextureScale("_MainTex")); pm.SetTextureOffset("_MainTex", m.GetTextureOffset("_MainTex")); }
                    if (m.HasProperty("_Color")) pm.SetColor("_Color", m.GetColor("_Color"));
                    if (m.HasProperty("_Glow")) pm.SetFloat("_Glow", m.GetFloat("_Glow"));
                    if (m.HasProperty("_UseVertexColor")) pm.SetFloat("_UseVertexColor", m.GetFloat("_UseVertexColor"));
                    previewMaterials.Add(pm);
                    mats[i] = pm;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>Remake (KeepAspect): the height that keeps the rendered image's proportions at the box's width.</summary>
        void FitAspect()
        {
            if (!KeepAspect) return;
            float w = Element.resolvedStyle.width, h = Element.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 0f) return;
            float want = Mathf.Round(w * rtHeight / rtWidth);
            if (float.IsNaN(h) || Mathf.Abs(want - h) > 1f) Element.style.height = want;
        }

        /// <summary>Remake (FloorUnderShip): the floor's inner ring centre under the ship's foot, as the rendered image shows
        /// it (scale-and-crop: the texture scaled to cover the box, centred). Otherwise the stylesheet's place.</summary>
        void PlaceFloor()
        {
            if (!FloorUnderShip || model == null || !hasFoot || cam == null || rt == null) return;
            var vp = cam.WorldToViewportPoint(footWorld);
            if (vp.z <= 0f) return;
            float w = Element.resolvedStyle.width, h = Element.resolvedStyle.height;
            float floorH = floorLeft.resolvedStyle.height, floorW = floorLeft.resolvedStyle.width;
            if (float.IsNaN(w) || float.IsNaN(h) || w <= 0f || h <= 0f) return;
            if (FloorHeightRatio > 0f)
            {
                floorH = Mathf.Round(h * FloorHeightRatio);
                floorLeft.style.height = floorH;
                floorRight.style.height = floorH;
            }
            if (float.IsNaN(floorH) || floorH <= 0f) return;
            float texW = rt.width, texH = rt.height;
            float s = Mathf.Max(w / texW, h / texH);
            float x = vp.x * texW * s - (texW * s - w) * 0.5f;
            float y = (1f - vp.y) * texH * s - (texH * s - h) * 0.5f;
            if (float.IsNaN(floorW) || floorW <= 0f) floorW = w * 0.5f;
            float top = y - floorH * FloorRingCentre;
            floorLeft.style.top = top;
            floorRight.style.top = top;
            floorLeft.style.left = x - floorW;
            floorRight.style.left = x;
        }

        /// <summary>Takes the ship, its camera and texture away and hides the box.</summary>
        Light previewLight;
        bool lightHooked;

        void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext _, Camera c) { if (previewLight != null) previewLight.enabled = c == cam; }
        void OnEndCamera(UnityEngine.Rendering.ScriptableRenderContext _, Camera c) { if (previewLight != null && c == cam) previewLight.enabled = false; }

        public void Hide()
        {
            if (lightHooked)
            {
                UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCamera;
                lightHooked = false;
            }
            previewLight = null;
            Element.style.display = DisplayStyle.None;
            image.style.backgroundImage = StyleKeyword.None;
            EndDrag();
            RenderPipelineManager.beginCameraRendering -= BeginPreviewRender;
            RenderPipelineManager.endCameraRendering -= EndPreviewRender;
            EndPreviewRender(default, null);   // never left swapped
            if (stage != null) Object.Destroy(stage);
            stage = null;
            model = null;
            cam = null;
            foreach (var m in previewMaterials) if (m != null) Object.Destroy(m);
            previewMaterials.Clear();
            hasFoot = false;
            Ship = -1;
            if (rt != null) { rt.Release(); Object.Destroy(rt); rt = null; }
        }

        /// <summary>Turns like a drag of 'px' panel pixels to the left (keys, a stick).</summary>
        public void Turn(float px) => yawPx += px;

        void ApplyYaw()
        {
            if (model != null) model.localRotation = Quaternion.Euler(0f, yawPx / PixelsPerRadian * Mathf.Rad2Deg, 0f);
        }

        void Tick()
        {
            if (model == null) return;
            float frames = Mathf.Min(Time.unscaledDeltaTime, 0.1f) * 1000f / FrameMs;
            if (pointer < 0 && flingPx != 0f)
            {
                yawPx += flingPx * frames;
                flingPx *= Mathf.Pow(0.9f, frames);
                if (Mathf.Abs(flingPx) <= 1f) flingPx = 0f;
            }
            else if (pointer < 0) yawPx += AutoSpin * frames;
            ApplyYaw();
        }
    }
}
