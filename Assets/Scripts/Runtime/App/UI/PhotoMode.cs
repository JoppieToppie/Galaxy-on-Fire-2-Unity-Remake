// PhotoMode.cs
// Action Freeze (text 59), the pause menu's photo mode: MenuTouchWindow button 0x13 -> state 0xd; MGame::setCinematicMode
// 0x1ac2b0 switches to camera 3 (the orbit camera, TargetFollowCamera::setRotationAroundTarget) while the game stays paused
// (Level::update(dt = 0)); MGame::OnRender2D draws only the logo 0x534 at the top-right and the footer with Back (170),
// hidden while a finger turns the camera.
//   MGame::freeCamTouchBegin / Move / End 0x1a7f20..  one finger: px, py accumulate (py clamped +-200 only by a fling,
//                                                     MGame::OnUpdate 0x1aec2c; a drag goes all the way round), yaw = -0.005 px,
//                                                     pitch = -0.005 py rad; a release over 3 px keeps turning x0.9 per
//                                                     frame; pinch: distance += span change x -50, 1500..20000 units;
//                                                     wheel -50 per step, decaying x0.9; arrow keys +-4 px per frame
// The original's share buttons (61 Facebook, 60 Save to library) are never drawn nor handled in this build (the capture
// countdown is always -1, SaveImageToPhotosAlbum is an empty stub). Remake-only: 60 saves a PNG (PC: Pictures/Galaxy on
// Fire 2, Android: MediaStore Pictures/Galaxy on Fire 2) and shows 55 / 56; no upload. Plain class driven by PauseMenu.
// Remake: the overlay is the hangar's Inspect ship one (OrbitViewUi): "ACTION FREEZE" at the top left, the controls as
// hints at the top right (Hide UI H / Y, Screenshot Enter / P / A, Back clickable), Back and the remake logo in the
// footer; Hide UI clears the screen until a short click / tap or H / Y again; + / - (Page Up / Down) zoom too.
// Remake (players' suggestion "Everspace-style Action Freeze", 2026-10-17): the photo tools (PhotoTools) shared with Inspect
// ship: the free camera (F / X), anywhere within 5 km of the ship, and the Camera settings panel (Tab / View: lens, depth of
// field, colour, frame and grid, hide the ship, the screenshot's size).

using System;
using System.Collections;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class PhotoMode
    {
        const float M = 0.05f, FrameMs = OriginalFrame.Ms;
        const float MinDistance = 1500f, MaxDistance = 20000f;

        readonly VisualElement root, overlay, hints;
        readonly Label message;
        readonly Button back;
        readonly MonoBehaviour host;
        Camera cam;
        ChaseCamera chase;
        Transform target;
        Vector3 savedPos;
        Quaternion savedRot;
        bool chaseWasEnabled;
        float px, py, distance, wheel, messageMs;
        Vector2 fling, lastPointer;
        bool dragging, capturing, uiHidden;
        float dragTravel;
        InputKind hintsKind = (InputKind)(-1);
        int draggingFinger = -1;
        float pinchSpan = -1f;
        readonly PhotoTools tools;
        const float FreeRangeMetres = 5000f;

        public bool Active { get; private set; }
        /// <summary>Back: the pause menu takes over again.</summary>
        public event Action Exited;
        /// <summary>The flight HUD's button sounds: push (true) on a press, release (false) on a click.</summary>
        public Action<bool> ButtonSound;

        public PhotoMode(VisualElement hudRoot, MonoBehaviour coroutineHost)
        {
            root = hudRoot;
            host = coroutineHost;
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("photo-overlay");
            var title = OrbitViewUi.Title();
            title.text = Localization.Get(59).ToUpperInvariant();   // Action Freeze
            overlay.Add(title);
            message = OrbitViewUi.Message();
            overlay.Add(message);
            hints = OrbitViewUi.Hints();
            overlay.Add(hints);   // top right, the controls for the input in use (docs/UI-DESIGN.md "Button prompts")
            var footer = new VisualElement { pickingMode = PickingMode.Ignore };
            footer.AddToClassList("orbit-view-footer");
            footer.AddToClassList(SafeArea.EdgeClass);
            back = OrbitViewUi.Back(() => { ButtonSound?.Invoke(false); Exit(); }, () => ButtonSound?.Invoke(true));
            footer.Add(back);
            var logo = new VisualElement { pickingMode = PickingMode.Ignore };
            logo.AddToClassList("photo-logo");   // remake: the title screen's remake logo (FlightHud.uss), not 0x534's Full HD one
            footer.Add(logo);
            overlay.Add(footer);
            root.Add(overlay);
            SafeArea.KeepClear(overlay);   // the title, hints, footer and settings panel clear of the notch / Dynamic Island
            tools = new PhotoTools(overlay, s => ButtonSound?.Invoke(s));
            tools.Changed += BuildHints;
            tools.Tapped += () => { if (uiHidden) SetUiHidden(false); };
        }

        public void Enter(SpaceLevel level)
        {
            if (Active || level == null || level.Player == null) return;
            cam = Camera.main;
            if (cam == null) return;
            Active = true;
            target = level.Player.transform;
            chase = cam.GetComponent<ChaseCamera>();
            chaseWasEnabled = chase != null && chase.enabled;
            if (chase != null) chase.enabled = false;
            savedPos = cam.transform.position;
            savedRot = cam.transform.rotation;
            // Start from where the chase camera is: its yaw / pitch around the ship, the distance clamped.
            var d = savedPos - target.position;
            distance = Mathf.Clamp(d.magnitude / M, MinDistance, MaxDistance);
            float yaw = Mathf.Atan2(-d.x, -d.z);
            float pitch = Mathf.Asin(Mathf.Clamp(d.y / Mathf.Max(d.magnitude, 1e-3f), -1f, 1f));
            px = yaw / 0.005f;   // Place's inverse
            py = Mathf.Clamp(-pitch / 0.005f, -200f, 200f);
            fling = Vector2.zero;
            wheel = 0f;
            messageMs = 0f;
            message.text = "";
            SetUiHidden(false);
            BuildHints();
            root.AddToClassList("hud-photo");
            FpsCounter.Suppressed = true;   // not in the photos either
            overlay.AddToClassList("photo-overlay--shown");
            // The ship's size (its meshes' bounds): the depth of field's "on the ship" reads it.
            float radius = 30f;
            var rs = target.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); radius = Mathf.Max(5f, b.extents.magnitude); }
            tools.Begin(cam, target, () => target != null ? target.position : Vector3.zero, radius, FreeRangeMetres, float.NegativeInfinity,
                        OrbitPose, false);
            BuildHints();
            Place();
        }

        public void Exit()
        {
            if (!Active) return;
            Active = false;
            tools.End();   // the lens back first: the camera's field of view and near plane
            SetUiHidden(false);
            root.RemoveFromClassList("hud-photo");
            FpsCounter.Suppressed = false;
            overlay.RemoveFromClassList("photo-overlay--shown");
            if (cam != null) { cam.transform.position = savedPos; cam.transform.rotation = savedRot; }
            if (chase != null) chase.enabled = chaseWasEnabled;
            Exited?.Invoke();
        }

        /// <summary>Every frame while active (unscaled: the game is frozen). Returns false once exited.</summary>
        public bool Tick()
        {
            if (!Active) return false;
            if (target == null || cam == null) { Exit(); return false; }
            float frames = Time.unscaledDeltaTime * 1000f / FrameMs;
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            // The photo tools' keys first: the settings panel takes the arrows / Enter / Esc while open.
            bool panelKeys = tools.TickKeys(kb, pad, out bool backTaken);
            if (!panelKeys && !backTaken)
            {
                if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonEast.wasPressedThisFrame))
                { Exit(); return false; }
                if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame))
                    || (pad != null && pad.buttonSouth.wasPressedThisFrame)) Save();
                if ((kb != null && kb.hKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame))
                { ButtonSound?.Invoke(false); SetUiHidden(!uiHidden); }
            }
            if (InputMode.Current != hintsKind) BuildHints();
            if (messageMs > 0f && (messageMs -= Time.unscaledDeltaTime * 1000f) <= 0f) message.text = "";
            if (tools.Free)
            {
                // The free camera: the orbit rests (it takes up again where it was when switched back).
                tools.TickFree(kb, pad, Time.unscaledDeltaTime, OverControl, out bool looking);
                var (fp, fr) = tools.FreePose;
                cam.transform.SetPositionAndRotation(fp, PhotoTools.Rolled(fr));
                overlay.EnableInClassList("photo-overlay--turning", looking && !capturing && !tools.PanelOpen);
                tools.LateTick();
                return true;
            }
            if (panelKeys) { kb = null; pad = null; }   // the panel has the keys and the controller

            float scale = 1080f / Mathf.Max(1, Screen.height);   // the original's pixels: a 1080-high canvas
            Vector2 delta = Vector2.zero;
            bool held = false;
            // One finger / the mouse: turn; two fingers: zoom.
            var ts = Touchscreen.current;
            int down = 0;
            if (ts != null) foreach (var t in ts.touches) if (t.press.isPressed) down++;
            if (down >= 2)
            {
                Vector2 a = Vector2.zero, b = Vector2.zero; int n = 0;
                foreach (var t in ts.touches) if (t.press.isPressed) { if (n == 0) a = t.position.ReadValue(); else if (n == 1) b = t.position.ReadValue(); n++; }
                float span = (a - b).magnitude * scale;
                if (pinchSpan >= 0f) distance += (span - pinchSpan) * -50f;
                pinchSpan = span;
                draggingFinger = -1;
            }
            else
            {
                pinchSpan = -1f;
                Vector2? pos = null;
                if (down == 1) pos = ts.primaryTouch.position.ReadValue();
                else if (Mouse.current != null && Mouse.current.leftButton.isPressed) pos = Mouse.current.position.ReadValue();
                // A press that began on the panel (a slider) or a button stays theirs when the pointer leaves them.
                bool overUi = tools.PressOverUi(pos, OverControl);
                if (pos.HasValue && !overUi)
                {
                    if (!dragging) { dragging = true; lastPointer = pos.Value; draggingFinger = down == 1 ? 1 : -1; dragTravel = 0f; }
                    delta = (pos.Value - lastPointer) * scale;
                    dragTravel += delta.magnitude;
                    // The original turns screen y up into its y down (a drag up takes the camera up); remake (players' request):
                    // vertical drags inverted, a drag up takes it down (the ship tips up with the finger), like the hangar's
                    // Inspect ship and the Mod Builder's orbit.
                    lastPointer = pos.Value;
                    fling = delta;
                    held = true;
                }
                else if (dragging)
                {
                    dragging = false;
                    draggingFinger = -1;
                    if (fling.magnitude <= 3f) fling = Vector2.zero;
                    // The UI hidden: a short click / tap (no drag) brings it back.
                    if (uiHidden && dragTravel < 8f) { fling = Vector2.zero; SetUiHidden(false); }
                }
            }
            // MGame::OnUpdate (cinematic mode, 0x1aec2c): the pitch is clamped to +-200 only in the fling branch, when the
            // release keeps it turning (over 1 px a frame); a drag, the arrows and the sticks turn it all the way round.
            bool flingClamps = !held && Mathf.Abs(fling.y) > 1f;
            if (!held && fling != Vector2.zero)
            {
                delta = fling * frames;
                fling *= Mathf.Pow(0.9f, frames);
                if (fling.magnitude <= 1f) fling = Vector2.zero;
            }
            // MGame::OnUpdate in cinematic mode: the arrows as touch moves of -+4 px per frame (right -4: the camera goes to the
            // ship's right, up -4: up); the sticks (remake) the same way at 8 px, the triggers zoom.
            if (kb != null)
            {
                delta.x += ((kb.leftArrowKey.isPressed ? 1 : 0) - (kb.rightArrowKey.isPressed ? 1 : 0)) * 4f * frames;
                delta.y += ((kb.downArrowKey.isPressed ? 1 : 0) - (kb.upArrowKey.isPressed ? 1 : 0)) * 4f * frames;
                // Remake: + / - (Page Up / Down) zoom like the triggers.
                distance += ((kb.minusKey.isPressed || kb.numpadMinusKey.isPressed || kb.pageDownKey.isPressed ? 1 : 0)
                           - (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed || kb.pageUpKey.isPressed ? 1 : 0)) * 200f * frames;
            }
            if (pad != null)
            {
                var s = pad.rightStick.ReadValue() + pad.leftStick.ReadValue();
                delta += new Vector2(-s.x, -s.y) * 8f * frames;
                distance += (pad.leftTrigger.ReadValue() - pad.rightTrigger.ReadValue()) * 200f * frames;
            }
            if (Mouse.current != null && !tools.OverPanel(Mouse.current.position.ReadValue()))
            {
                float steps = MouseWheel.Steps(Mouse.current);
                if (Mathf.Abs(steps) > 0f) wheel += steps;
            }
            if (Mathf.Abs(wheel) > 0.01f) { distance += wheel * -50f * frames; wheel *= Mathf.Pow(0.9f, frames); } else wheel = 0f;
            delta = Settings.OrbitTurn(delta);   // #121: the camera views' own inversion
            px += delta.x;
            py += delta.y;
            if (flingClamps) py = Mathf.Clamp(py, -200f, 200f);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            overlay.EnableInClassList("photo-overlay--turning", held && !capturing && !tools.PanelOpen);
            Place();
            tools.LateTick();
            return true;
        }

        bool OverControl(Vector2 screen) => !uiHidden && OrbitViewUi.OverControl(root, back, hints, screen);

        void SetUiHidden(bool hidden)
        {
            uiHidden = hidden;
            overlay.EnableInClassList("orbit-view--clean", hidden);
        }

        /// <summary>The controls for the current input kind (again when it changes); the mouse's hover only with keys / mouse.</summary>
        void BuildHints()
        {
            hintsKind = InputMode.Current;
            overlay.EnableInClassList("can-hover", hintsKind == InputKind.KeyboardMouse);
            OrbitViewUi.BuildHints(hints, hintsKind, new[] { "↑", "←", "↓", "→" },
                () => { ButtonSound?.Invoke(false); SetUiHidden(true); },
                () => { ButtonSound?.Invoke(false); Save(); },
                () => { ButtonSound?.Invoke(false); Exit(); },
                () => ButtonSound?.Invoke(true),
                tools.ToggleFree, tools.ToggleSettings, tools.Free);
        }

        /// <summary>rotateAroundTarget(-0.005 py, -0.005 px, 0) at the distance, looking at the ship (MGame::OnUpdate 0x1af324).
        /// The game's ship frame has +x on the ship's left (Unity's is mirrored, (-x, y, z)), so in Unity the yaw is +0.005 px
        /// and the pitch -0.005 py: a drag right takes the camera to the ship's left (the ship turns with the finger), a drag
        /// up takes it down (remake: inverted, see the drag). Copied without the mirror, every axis was inverted.</summary>
        void Place()
        {
            var (pos, rot) = OrbitPose();
            cam.transform.SetPositionAndRotation(pos, PhotoTools.Rolled(rot));   // the photo tools' roll on top
        }

        (Vector3, Quaternion) OrbitPose()
        {
            var rot = Quaternion.Euler(-0.005f * py * Mathf.Rad2Deg, 0.005f * px * Mathf.Rad2Deg, 0f);
            // Looking at the ship with the orbit's own up (LookAt with world up flipped the view over the top: past +-90 deg
            // the pitch is free now); below 90 deg it is the same view.
            return (target.position + rot * new Vector3(0f, 0f, -distance * M), rot);
        }

        // ---- 60 Save to library (remake-only) -------------------------------------------------------------------

        void Save()
        {
            if (!Active || capturing || host == null) return;
            host.StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            capturing = true;
            // The photo tools' capture: the overlay hidden, cut to the frame, twice the size if asked (PhotoTools.Capture).
            yield return tools.Capture(overlay, ok =>
            {
                message.text = Localization.Get(ok ? 55 : 56);   // "Screenshot saved." / "Error saving screenshot."
                messageMs = 3000f;
            });
            capturing = false;
        }

        /// <summary>Saves a PNG to the pictures library (also the F12 screenshots, ScreenshotKey).</summary>
        public static bool Store(byte[] png, string name) => StoreAt(png, name) != null;

        /// <summary>The system's file browser can show a saved screenshot (desktop: Windows, macOS, Linux).</summary>
        public static bool CanShowInFolder =>
#if UNITY_STANDALONE || UNITY_EDITOR
            true;
#else
            false;
#endif

        /// <summary>Opens the folder holding 'path' in the file browser, the file selected where the system can (Windows).</summary>
        public static void ShowInFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path.Replace('/', '\\')}\"");
                return;
            }
            catch (Exception e) { Debug.LogWarning("PhotoMode: explorer " + e.Message); }
#endif
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Application.OpenURL("file:///" + dir.Replace('\\', '/'));
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void GoF2Photos_Save(string path);
#endif

        /// <summary>Saves a PNG to the pictures library; returns where (the file's full path; on Android the library's
        /// "Pictures/Galaxy on Fire 2/name", on iOS "Photos · Screenshots/name"), null when it couldn't.</summary>
        public static string StoreAt(byte[] png, string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");
                using var values = new AndroidJavaObject("android.content.ContentValues");
                values.Call("put", "_display_name", name);
                values.Call("put", "mime_type", "image/png");
                values.Call("put", "relative_path", "Pictures/Galaxy on Fire 2");
                using var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                using var uri = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                using var item = resolver.Call<AndroidJavaObject>("insert", uri, values);
                if (item == null) return null;
                using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", item);
                stream.Call("write", png);
                stream.Call("close");
                return "Pictures/Galaxy on Fire 2/" + name;
            }
            catch (Exception e) { Debug.LogWarning("PhotoMode: MediaStore " + e.Message); }
#elif UNITY_IOS && !UNITY_EDITOR
            // iOS: Documents/Screenshots (the Files app shows it) and the photo library (PhotoLibrary.mm, add-only access).
            try
            {
                string shots = Path.Combine(GoF2Remake.GamePaths.Data, "Screenshots");
                Directory.CreateDirectory(shots);
                string file = Path.Combine(shots, name);
                File.WriteAllBytes(file, png);
                GoF2Photos_Save(file);
                return "Photos · Screenshots/" + name;
            }
            catch (Exception e) { Debug.LogWarning("PhotoMode: Photos " + e.Message); }
#elif !UNITY_WSA || UNITY_EDITOR
            // UWP / Xbox (UNITY_WSA) has no writable Pictures folder without the Pictures library capability: straight to the
            // app's own folder below.
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (!string.IsNullOrEmpty(pictures))
            {
                try
                {
                    string preferred = Path.Combine(pictures, "Galaxy on Fire 2");
                    Directory.CreateDirectory(preferred);
                    string file = Path.Combine(preferred, name);
                    File.WriteAllBytes(file, png);
                    return file;
                }
                catch (Exception e) { Debug.LogWarning("PhotoMode: Pictures " + e.Message); }
            }
#endif
            // The fallback: <persistentDataPath>/Screenshots (Android without MediaStore, UWP, an unwritable Pictures folder).
            string dir = Path.Combine(GoF2Remake.GamePaths.Data, "Screenshots");
            Directory.CreateDirectory(dir);
            string saved = Path.Combine(dir, name);
            File.WriteAllBytes(saved, png);
            Debug.Log("PhotoMode: saved to " + dir);
            return saved;
        }
    }
}
