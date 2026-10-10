// ShipPreview.cs
// A turning 3D ship in a UI box (ListItemWindow's preview, Reference/research/shop.md 2.4): the player variant of the ship
// (Globals::getShipGroup(idx, race, true)) on a stage far below the station, on layer 30, its own camera at the original's
// direction ((1362, 1690, -5257) game units, turned -0.1 rad) framing the ship's bounds, rendered into a RenderTexture shown
// over the floor glow 0x50b (drawn once and mirrored). Turning: yaw only, 120 px = 1 rad, starting at 260 px; a drag turns
// it, a release over 3 px keeps turning x0.9 per 30 fps frame and stops at 1 px.
// Used by the full-screen item window (ItemInfoWindow) and (remake, #82) the hangar's details panel, which also spins it
// slowly while it isn't dragged (AutoSpin) and opens the item window on a click / tap (Clicked).

using System;
using GoF2Remake.Data;
using GoF2Remake.Visuals;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace GoF2Remake.UI
{
    public sealed class ShipPreview
    {
        const int PreviewLayer = 30;
        const float M = 0.05f, FrameMs = 1000f / 30f, PixelsPerRadian = 120f, ClickSlop = 10f;

        /// <summary>The box: the floor glow and the rendered ship; drags on it turn the ship.</summary>
        public readonly VisualElement Element;
        /// <summary>A click / tap on the box that didn't turn it.</summary>
        public event Action Clicked;
        /// <summary>Turning by itself while not dragged or flung, in pixels per 30 fps frame (0 = still).</summary>
        public float AutoSpin;
        public bool Shown => model != null;
        /// <summary>The ship shown, -1 = none.</summary>
        public int Ship { get; private set; } = -1;

        readonly VisualElement image;
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
            var floorLeft = new VisualElement { pickingMode = PickingMode.Ignore };
            floorLeft.AddToClassList("info-floor");
            var floorRight = new VisualElement { pickingMode = PickingMode.Ignore };
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
            var go = Object.Instantiate(prefab, stage.transform, false);
            go.GetComponent<AssembledObject>()?.SetPlayerVariant(true);
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) lg.ForceLOD(0);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
            model = go.transform;
            // The camera: (1362, 1690, -5257) game units at 1920 x 1080 -> Unity (x, y, -z) * 0.05, looking at the ship.
            var camGo = new GameObject("Ship preview camera");
            camGo.transform.SetParent(stage.transform, false);
            camGo.transform.localPosition = new Vector3(1362f, 1690f, 5257f) * M;
            var cam = camGo.AddComponent<Camera>();
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
            camGo.transform.Rotate(0f, 0f, -0.1f * Mathf.Rad2Deg, Space.Self);
            rt = new RenderTexture(rtWidth, rtHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = Application.isMobilePlatform ? 2 : 4 };
            cam.targetTexture = rt;
            var lightGo = new GameObject("Ship preview light");
            lightGo.transform.SetParent(stage.transform, false);
            lightGo.transform.rotation = Quaternion.LookRotation(new Vector3(-5f, -1f, -5f) * -1f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << PreviewLayer;
            light.intensity = 1f;
            image.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            yawPx = 260f;   // ListItemWindow: starts at 260 px
            flingPx = 0f;
            ApplyYaw();
        }

        /// <summary>Takes the ship, its camera and texture away and hides the box.</summary>
        public void Hide()
        {
            Element.style.display = DisplayStyle.None;
            image.style.backgroundImage = StyleKeyword.None;
            EndDrag();
            if (stage != null) Object.Destroy(stage);
            stage = null;
            model = null;
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
