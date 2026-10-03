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
        const float M = 0.05f, FrameMs = 1000f / 30f;
        const float MinDistance = 1500f, MaxDistance = 20000f;

        readonly VisualElement root, overlay;
        readonly Label message;
        readonly MonoBehaviour host;
        Camera cam;
        ChaseCamera chase;
        Transform target;
        Vector3 savedPos;
        Quaternion savedRot;
        bool chaseWasEnabled;
        float px, py, distance, wheel, messageMs;
        Vector2 fling, lastPointer;
        bool dragging, capturing;
        int draggingFinger = -1;
        float pinchSpan = -1f;

        public bool Active { get; private set; }
        /// <summary>Back: the pause menu takes over again.</summary>
        public event Action Exited;

        public PhotoMode(VisualElement hudRoot, MonoBehaviour coroutineHost)
        {
            root = hudRoot;
            host = coroutineHost;
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("photo-overlay");
            var logo = new VisualElement { pickingMode = PickingMode.Ignore };
            logo.AddToClassList("photo-logo");
            var tex = Resources.Load<Texture2D>("GoF2Hud/photo_logo");
            if (tex != null) logo.style.backgroundImage = new StyleBackground(tex);
            overlay.Add(logo);
            message = new Label { pickingMode = PickingMode.Ignore };
            message.AddToClassList("photo-message");
            overlay.Add(message);
            var footer = new VisualElement();
            footer.AddToClassList("photo-footer");
            var back = new Button(Exit) { text = Localization.Get(170).ToUpperInvariant(), focusable = false };
            back.AddToClassList("photo-button");
            var save = new Button(Save) { text = Localization.Get(60).ToUpperInvariant(), focusable = false };
            save.AddToClassList("photo-button");
            footer.Add(back);
            footer.Add(save);
            overlay.Add(footer);
            root.Add(overlay);
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
            root.AddToClassList("hud-photo");
            FpsCounter.Suppressed = true;   // not in the photos either
            overlay.AddToClassList("photo-overlay--shown");
            Place();
        }

        public void Exit()
        {
            if (!Active) return;
            Active = false;
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
            if ((kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonEast.wasPressedThisFrame))
            { Exit(); return false; }
            if ((kb != null && kb.enterKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) Save();

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
                else if (Mouse.current != null && Mouse.current.leftButton.isPressed && !OverFooter(Mouse.current.position.ReadValue()))
                    pos = Mouse.current.position.ReadValue();
                if (pos.HasValue && !(down == 1 && draggingFinger < 0 && OverFooter(pos.Value)))
                {
                    if (!dragging) { dragging = true; lastPointer = pos.Value; draggingFinger = down == 1 ? 1 : -1; }
                    delta = (pos.Value - lastPointer) * scale;
                    delta.y = -delta.y;   // screen y up -> the original's y down
                    lastPointer = pos.Value;
                    fling = delta;
                    held = true;
                }
                else if (dragging)
                {
                    dragging = false;
                    draggingFinger = -1;
                    if (fling.magnitude <= 3f) fling = Vector2.zero;
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
            }
            if (pad != null)
            {
                var s = pad.rightStick.ReadValue() + pad.leftStick.ReadValue();
                delta += new Vector2(-s.x, -s.y) * 8f * frames;
                distance += (pad.leftTrigger.ReadValue() - pad.rightTrigger.ReadValue()) * 200f * frames;
            }
            if (Mouse.current != null)
            {
                float steps = Mathf.Clamp(Mouse.current.scroll.ReadValue().y, -1f, 1f);   // a notch reads 1 (uniform scroll) or 120 (raw)
                if (Mathf.Abs(steps) > 0f) wheel += steps;
            }
            if (Mathf.Abs(wheel) > 0.01f) { distance += wheel * -50f * frames; wheel *= Mathf.Pow(0.9f, frames); } else wheel = 0f;
            px += delta.x;
            py += delta.y;
            if (flingClamps) py = Mathf.Clamp(py, -200f, 200f);
            distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
            overlay.EnableInClassList("photo-overlay--turning", held && !capturing);
            if (messageMs > 0f && (messageMs -= Time.unscaledDeltaTime * 1000f) <= 0f) message.text = "";
            Place();
            return true;
        }

        bool OverFooter(Vector2 screen) => screen.y < Screen.height * 0.12f;

        /// <summary>rotateAroundTarget(-0.005 py, -0.005 px, 0) at the distance, looking at the ship (MGame::OnUpdate 0x1af324).
        /// The game's ship frame has +x on the ship's left (Unity's is mirrored, (-x, y, z)), so in Unity the yaw is +0.005 px
        /// and the pitch -0.005 py: a drag right takes the camera to the ship's left (the ship turns with the finger), a drag
        /// up takes it up. Copied without the mirror, every axis was inverted.</summary>
        void Place()
        {
            var rot = Quaternion.Euler(-0.005f * py * Mathf.Rad2Deg, 0.005f * px * Mathf.Rad2Deg, 0f);
            // Looking at the ship with the orbit's own up (LookAt with world up flipped the view over the top: past +-90 deg
            // the pitch is free now); below 90 deg it is the same view.
            cam.transform.SetPositionAndRotation(target.position + rot * new Vector3(0f, 0f, -distance * M), rot);
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
            overlay.style.visibility = Visibility.Hidden;
            yield return new WaitForEndOfFrame();
            bool ok = false;
            try
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] png = shot.EncodeToPNG();
                UnityEngine.Object.Destroy(shot);
                ok = Store(png, $"GoF2_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            }
            catch (Exception e) { Debug.LogWarning("PhotoMode: " + e.Message); }
            overlay.style.visibility = Visibility.Visible;
            message.text = Localization.Get(ok ? 55 : 56);   // "Screenshot saved." / "Error saving screenshot."
            messageMs = 3000f;
            capturing = false;
        }

        /// <summary>Saves a PNG to the pictures library (also the F12 screenshots, ScreenshotKey).</summary>
        public static bool Store(byte[] png, string name)
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
                if (item == null) return false;
                using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", item);
                stream.Call("write", png);
                stream.Call("close");
                return true;
            }
            catch (Exception e) { Debug.LogWarning("PhotoMode: MediaStore " + e.Message); }
            string dir = Path.Combine(Application.persistentDataPath, "Screenshots");
#else
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Galaxy on Fire 2");
#endif
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name), png);
            return true;
        }
    }
}
