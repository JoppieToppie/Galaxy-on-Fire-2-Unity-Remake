// ScreenshotKey.cs
// Remake-only: F12 saves a screenshot of the whole screen (HUD and menus included) as a PNG at the end of the frame, where
// the photo mode saves (PhotoMode.Store: PC Pictures/Galaxy on Fire 2, Android MediaStore Pictures/Galaxy on Fire 2).
// Any scene, any time; created by Bootstrap and kept for the whole run.

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class ScreenshotKey : MonoBehaviour
    {
        static ScreenshotKey instance;
        bool capturing;

        public static void Install()
        {
            if (instance != null) return;
            var go = new GameObject("ScreenshotKey");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ScreenshotKey>();
        }

        void Update()
        {
            if (!capturing && GoF2Remake.Flight.GameControls.Screenshot.WasPressedThisFrame()) StartCoroutine(Capture());   // rebindable (F12)
        }

        /// <summary>The screen at the end of this frame as a texture for 'done' (the caller's to destroy; null when it
        /// couldn't be taken): an issue report's picture, taken with the pause menu hidden for that frame (PauseMenuReport).</summary>
        public static void Grab(Action<Texture2D> done)
        {
            if (instance == null) { done?.Invoke(null); return; }
            instance.StartCoroutine(instance.GrabAtEndOfFrame(done));
        }

        IEnumerator GrabAtEndOfFrame(Action<Texture2D> done)
        {
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try { shot = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception e) { Debug.LogWarning("ScreenshotKey: " + e.Message); }
            done?.Invoke(shot);
        }

        IEnumerator Capture()
        {
            capturing = true;
            yield return new WaitForEndOfFrame();
            try
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                byte[] png = shot.EncodeToPNG();
                Destroy(shot);
                string name = $"GoF2_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
                Debug.Log(PhotoMode.Store(png, name) ? $"ScreenshotKey: saved {name}" : "ScreenshotKey: saving failed");
            }
            catch (Exception e) { Debug.LogWarning("ScreenshotKey: " + e.Message); }
            capturing = false;
        }
    }
}
