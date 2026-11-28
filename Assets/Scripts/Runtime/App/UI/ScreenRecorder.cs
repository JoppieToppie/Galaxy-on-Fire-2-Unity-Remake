// ScreenRecorder.cs
// Remake-only: the built-in screen recorder (docs/notes/platforms.md "Screen recorder"; the user's request 2026-11-28):
// F9 (rebindable, GameControls.RecordVideo), the pause menu's Start / Stop recording or Movie mode's takes record what
// is on screen (the HUD and menus too, unless Settings.RecordInterface is off) with the game's sound, to an MP4
// (ClipLibrary.Folder); the clips manager (ClipsView) lists them. Windows and Android (RecorderEncoder); nothing elsewhere
// (Supported false).
//   picture  at the end of each frame due (Settings.RecordFrameRates: frames on a fixed clock of real time, a late one
//            skips its slot) the back buffer is copied (ScreenCapture.CaptureScreenshotIntoRenderTexture), or without the
//            interface the cameras' colour before the UI (RecorderScenePass), packed into I420 at the clip's size on the
//            GPU (Resources/GoF2Recorder/RecorderYuv.shader) and read back without a stall (AsyncGPUReadback; more than
//            three in flight: the frame is skipped) into the encoder
//   HDR      with the screen in HDR (Bootstrap.HdrActive) the clip is HDR10 (HEVC Main 10, BT.2020, PQ; the user's request
//            "when hdr is on, it should also record in hdr"): the capture in half floats, 16-bit samples packed; where the
//            encoder can't (no HEVC Main 10 / P010), SDR tone-mapped from the HDR picture (the shader's _Hdr 2)
//   sound    RecorderAudioTap on the scene's AudioListener hands over the final mix on the audio thread; its clock is
//            the samples written, and a gap (the listener paused, a scene loading) is filled with silence from dspTime;
//            a mix at another rate than 44.1 / 48 kHz (a phone's own: the project leaves it to the device) is resampled
//            to 48 kHz (linear), which every encoder takes (the S24's first clip had no sound)
//   mark     a red dot at the top right (Settings.RecordIndicator: off, the first 3 s, or all along), drawn onto the screen
//            after the frame was taken, so it is never in the clip (the user's request); "Clip saved" after the stop on a
//            panel of its own over every scene
// A thumbnail (the frame half a second in) and the length go beside the clip (ClipLibrary.SaveInfo). The recording stops
// with the game, and on a phone when the game goes to the background.

using System;
using System.Collections;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class ScreenRecorder : MonoBehaviour
    {
        const float MarkSeconds = 3f, SavedSeconds = 4f;
        const int MaxInFlight = 3;

        static ScreenRecorder instance;
        PanelRenderer panelRenderer;
        PanelSettings runtimePanel;
        VisualTreeAsset emptyTree;
        VisualElement root;
        Label toast;
        Texture2D dot;
        ClipsView clips;

        bool recording, markAllowed = true, thumbTaken, withInterface;
        int session, fps, inFlight, hdrMode;   // hdrMode: the shader's _Hdr (0 SDR, 1 HDR10, 2 SDR from an HDR screen)
        bool hdrScreen;
        Vector2Int size;
        string path, name;
        double startTime;
        long lastSlot = -1;
        float toastUntil;
        RenderTexture screenRt, sceneRt, yuvRt;
        Material packer;

        // The sound (the audio thread reads these).
        static volatile bool hearing;
        static int rate, inputRate;      // the clip's sample rate, the mix's
        static double resamplePos;       // the next output sample's place in input frames (-1..0: between the blocks)
        static readonly float[] previous = new float[2];
        static float[] resampled = new float[0];
        static long samplesWritten;
        static double dspStart;
        static float[] silence = new float[4096 * 2];

        public static bool Supported => RecorderEncoder.Supported;
        public static bool Recording => instance != null && instance.recording;
        /// <summary>The file being recorded (null: none).</summary>
        public static string CurrentPath => Recording ? instance.path : null;
        /// <summary>The recording is HDR10.</summary>
        public static bool RecordingHdr => Recording && instance.hdrMode == 1;
        public static float Seconds => Recording ? (float)(Time.realtimeSinceStartupAsDouble - instance.startTime) : 0f;
        /// <summary>A recording started or stopped (the pause menu's entry, Movie mode's button).</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; hearing = false; Changed = null; }

        public static void Install()
        {
            if (instance != null || !Supported) return;
            var assets = StarMapAssets.Load();
            if (assets == null || assets.panelSettings == null) return;
            var go = new GameObject("ScreenRecorder");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ScreenRecorder>();
            instance.runtimePanel = Instantiate(assets.panelSettings);
            instance.runtimePanel.sortingOrder = assets.panelSettings.sortingOrder + 95;   // over the menus, under the FPS counter
            instance.emptyTree = ScriptableObject.CreateInstance<VisualTreeAsset>();
            var pr = go.AddComponent<PanelRenderer>();
            pr.panelSettings = instance.runtimePanel;
            pr.visualTreeAsset = instance.emptyTree;
            go.SetActive(true);
        }

        void OnEnable()
        {
            panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
        }

        void OnDisable() => panelRenderer?.UnregisterUIReloadCallback(OnUIReload);

        void OnDestroy()
        {
            StopNow();
            clips?.Close();
            if (runtimePanel != null) Destroy(runtimePanel);
            if (emptyTree != null) Destroy(emptyTree);
            if (dot != null) Destroy(dot);
            if (instance == this) instance = null;
        }

        void OnApplicationQuit() => StopNow();

        void OnApplicationPause(bool paused)
        {
            if (paused && Application.isMobilePlatform) StopNow();   // a phone's encoder may not outlive the background
        }

        void OnUIReload(PanelRenderer renderer, VisualElement rootElement, int version)
        {
            root = rootElement;
            UiLayout.Keep(runtimePanel, root);
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.top = root.style.right = root.style.bottom = 0;
            toast = new Label { pickingMode = PickingMode.Ignore };
            toast.AddToClassList("gof-toast");
            var t = toast.style;
            t.position = Position.Absolute;
            t.right = 24; t.top = 84;   // under the cargo readout at the top right
            t.fontSize = UiLayout.Type(18);
            t.color = Color.white;
            root.Add(toast);
            clips = new ClipsView(root);
            UpdateToast();
        }

        void Update()
        {
            if (GameControls.RecordVideo.WasPressedThisFrame() && !GameControls.BlocksMenus && GoF2Remake.Multiplayer.NetChat.Keys != null)
                Toggle();
            if (recording) KeepListening();
            UpdateToast();
            clips?.Tick();
        }

        void UpdateToast()
        {
            if (toast != null) toast.style.display = Time.unscaledTime < toastUntil ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Say(string text)
        {
            if (toast == null) return;
            toast.text = text;
            toastUntil = Time.unscaledTime + SavedSeconds;
            UpdateToast();
        }

        // ---- the mark ----------------------------------------------------------------------------------------

        bool MarkShown
        {
            get
            {
                int mode = Settings.RecordIndicator;
                return recording && markAllowed && mode != Settings.RecordIndicatorOff && (mode == Settings.RecordIndicatorAlways || Seconds < MarkSeconds);
            }
        }

        /// <summary>The red dot onto the back buffer, after this frame was taken: on screen, never in the clip. Pulsing
        /// slowly; 22 px at 1080 lines, under the cargo readout at the top right.</summary>
        void DrawMark()
        {
            if (!MarkShown) return;
            if (dot == null)
            {
                const int n = 64;
                dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                        px[y * n + x] = new Color32(240, 40, 40, (byte)(255 * Mathf.Clamp01(n / 2f - 1f - d)));   // a soft edge
                    }
                dot.SetPixels32(px);
                dot.Apply();
            }
            float k = Screen.height / 1080f, side = 22f * k;
            float alpha = 0.7f + 0.3f * Mathf.Cos(Time.unscaledTime * 4f);
            var active = RenderTexture.active;
            RenderTexture.active = null;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
            // (DrawTexture's colour is doubled: 0.5 is the texture as it is)
            Graphics.DrawTexture(new Rect(Screen.width - 40f * k - side, 96f * k, side, side), dot, new Rect(0, 0, 1, 1), 0, 0, 0, 0,
                                 new Color(0.5f, 0.5f, 0.5f, 0.5f * alpha));
            GL.PopMatrix();
            RenderTexture.active = active;
        }

        // ---- start / stop ------------------------------------------------------------------------------------

        public static void Toggle()
        {
            if (Recording) Stop(); else Begin();
        }

        /// <summary>Starts a clip named after 'prefix' ("GoF2" by default); 'mark' false keeps the red dot off (Movie mode's
        /// takes). False when it couldn't start (the reason shown).</summary>
        public static bool Begin(string prefix = null, bool mark = true)
        {
            if (instance == null || instance.recording) return false;
            return instance.StartClip(prefix, mark);
        }

        public static void Stop() => instance?.StopNow();

        public static void ShowClips() => instance?.clips?.Open();
        public static bool ClipsOpen => instance != null && instance.clips != null && instance.clips.IsOpen;

        bool StartClip(string prefix, bool showMark)
        {
            if (packer == null)
            {
                var shader = Resources.Load<Shader>("GoF2Recorder/RecorderYuv");
                if (shader == null || !shader.isSupported) { Say(string.Format(T("recFailed", "Recording failed: {0}"), "shader")); return false; }
                packer = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            fps = Settings.RecordFrameRates[Settings.RecordFrameRate];
            size = ClipLibrary.Size(Screen.width, Screen.height, Settings.RecordResolution);
            int kbps = ClipLibrary.BitrateKbps(size, fps, Settings.RecordQuality);
            path = ClipLibrary.NewPath(prefix);
            name = System.IO.Path.GetFileNameWithoutExtension(path);
            int mixRate = AudioSettings.outputSampleRate;
            int sampleRate = mixRate == 44100 || mixRate == 48000 ? mixRate : 48000;   // anything else resampled (OnAudio)
            bool sound = Settings.RecordSound && mixRate > 0;
            // HDR10 when the screen is in HDR and the encoder can; else SDR (tone-mapped from an HDR screen).
            hdrScreen = Bootstrap.HdrActive;
            string error = null;
            hdrMode = 0;
            if (hdrScreen && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R16))
            {
                int peak = Mathf.RoundToInt(PeakNits()), average = Mathf.RoundToInt(Settings.HdrPaperWhite);
                error = RecorderEncoder.Start(path, size.x, size.y, fps, kbps, sound ? sampleRate : 0, 2, true, peak, average);
                if (error == null) hdrMode = 1;
                else { Debug.Log("ScreenRecorder: no HDR10 encoder (" + error + "), recording SDR"); TryDelete(path); }
            }
            if (hdrMode != 1)
            {
                error = RecorderEncoder.Start(path, size.x, size.y, fps, kbps, sound ? sampleRate : 0, 2, false, 0, 0);
                hdrMode = hdrScreen ? 2 : 0;
            }
            if (error != null)
            {
                Debug.LogWarning("ScreenRecorder: " + error);
                Say(string.Format(T("recFailed", "Recording failed: {0}"), error));
                TryDelete(path);
                return false;
            }
            yuvRt = new RenderTexture(size.x, size.y * 3 / 2, 0, hdrMode == 1 ? RenderTextureFormat.R16 : RenderTextureFormat.R8,
                                      RenderTextureReadWrite.Linear) { name = "Recorder YUV" };
            yuvRt.Create();
            // Without the interface: the cameras' picture before the UI is drawn (RecorderScenePass), at the screen's size.
            withInterface = Settings.RecordInterface;
            if (!withInterface)
            {
                sceneRt = new RenderTexture(Screen.width, Screen.height, 0, CaptureFormat, RenderTextureReadWrite.Linear) { name = "Recorder scene" };
                sceneRt.Create();
                RecorderScenePass.Set(sceneRt);
            }
            session++;
            recording = true;
            markAllowed = showMark;
            lastSlot = -1;
            inFlight = 0;
            thumbTaken = false;
            startTime = Time.realtimeSinceStartupAsDouble;
            rate = sampleRate;
            inputRate = mixRate;
            resamplePos = 0;
            previous[0] = previous[1] = 0f;
            samplesWritten = 0;
            dspStart = AudioSettings.dspTime;
            hearing = sound;
            KeepListening();
            StartCoroutine(Frames(session));
            Debug.Log($"ScreenRecorder: {path} ({size.x} x {size.y}, {fps} fps, {kbps} kbit/s{(sound ? $", sound {mixRate} Hz" + (mixRate != sampleRate ? $" -> {sampleRate}" : "") : ", no sound")}{(withInterface ? "" : ", no interface")}"
                      + $"{(hdrMode == 1 ? ", HDR10" : hdrMode == 2 ? ", SDR from HDR" : "")})");
            Changed?.Invoke();
            return true;
        }

        void StopNow()
        {
            if (!recording) return;
            recording = false;
            float seconds = (lastSlot + 1) / (float)Mathf.Max(1, fps);
            AsyncGPUReadback.WaitAllRequests();   // the frames in flight go in first
            hearing = false;
            string error = RecorderEncoder.Stop();
            int dropped = RecorderEncoder.Dropped;
            RecorderScenePass.Set(null);
            Free(ref sceneRt);
            Free(ref screenRt);
            Free(ref yuvRt);
            if (error != null || lastSlot < 0)
            {
                Debug.LogWarning("ScreenRecorder: " + (error ?? "no frames"));
                TryDelete(path);
                Say(string.Format(T("recFailed", "Recording failed: {0}"), error ?? "no frames"));
            }
            else
            {
                try { ClipLibrary.SaveInfo(name, seconds, null); } catch (Exception e) { Debug.LogWarning("ScreenRecorder: " + e.Message); }
                Debug.Log($"ScreenRecorder: saved {path} ({seconds:0.0} s, {dropped} frames dropped)");
                Say(string.Format(T("recSaved", "Clip saved: {0} ({1})"), name, ClipLibrary.Duration(seconds)));
            }
            Changed?.Invoke();
            clips?.Refresh();
        }

        /// <summary>The copies' format: half floats for an HDR screen (its PQ codes, a camera's nits), else 8 bits.</summary>
        RenderTextureFormat CaptureFormat => hdrScreen ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;

        /// <summary>The screen's peak light (HDR10's metadata): what the display reports, else the option's.</summary>
        static float PeakNits()
        {
            try { var d = HDROutputSettings.main; if (d != null && d.available && d.maxToneMapLuminance > 0) return d.maxToneMapLuminance; }
            catch (Exception) { }
            return Settings.HdrPeak;
        }

        static void Free(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        static void TryDelete(string file)
        {
            try { if (file != null && System.IO.File.Exists(file)) System.IO.File.Delete(file); } catch (Exception) { }
        }

        static string T(string key, string english) => Localization.Extra(key, english);

        // ---- the picture -------------------------------------------------------------------------------------

        IEnumerator Frames(int id)
        {
            var end = new WaitForEndOfFrame();
            while (recording && session == id)
            {
                yield return end;
                if (!recording || session != id) yield break;
                Frame(id);
                DrawMark();   // after the frame was taken
            }
        }

        void Frame(int id)
        {
            double t = Time.realtimeSinceStartupAsDouble - startTime;
            long slot = (long)(t * fps);
            if (slot <= lastSlot) return;
            if (inFlight >= MaxInFlight) return;   // the GPU is behind: this frame is skipped
            lastSlot = slot;
            // The picture: the cameras' colour before the UI (linear, rows the Unity way), or the screen as it is shown
            // (gamma-encoded; its rows start at the top on a graphicsUVStartsAtTop API).
            bool scene = !withInterface && sceneRt != null && RecorderScenePass.CopiedFrame == Time.frameCount;
            RenderTexture source;
            if (scene) source = sceneRt;
            else
            {
                if (screenRt == null || screenRt.width != Screen.width || screenRt.height != Screen.height || screenRt.format != CaptureFormat)
                {
                    Free(ref screenRt);
                    screenRt = new RenderTexture(Screen.width, Screen.height, 0, CaptureFormat, RenderTextureReadWrite.Linear) { name = "Recorder screen" };
                    screenRt.Create();
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(screenRt);
                source = screenRt;
            }
            bool flip = !scene && SystemInfo.graphicsUVStartsAtTop, linear = scene && (hdrScreen || QualitySettings.activeColorSpace == ColorSpace.Linear);
            // Blit leaves its target active: the next frame's drawing went into it until put back (the screen flickered).
            var active = RenderTexture.active;
            packer.SetVector("_Size", new Vector4(size.x, size.y, 0f, 0f));
            packer.SetFloat("_Flip", flip ? 1f : 0f);
            packer.SetFloat("_Linear", linear ? 1f : 0f);
            packer.SetFloat("_Hdr", hdrMode);
            packer.SetFloat("_SceneNits", Settings.HdrTonemapping != 0 ? 1f : 0f);   // URP's HDR tonemapping: BT.2020 nits
            packer.SetFloat("_PaperWhite", Settings.HdrPaperWhite);
            packer.SetFloat("_Peak", PeakNits());
            packer.SetTexture("_MainTex", source);   // set here: Graphics.Blit didn't bind it for this shader (all grey)
            Graphics.Blit(source, yuvRt, packer, 0);
            RenderTexture.active = active;
            long time = slot * 10_000_000L / fps;
            int w = size.x, h = size.y;
            inFlight++;
            AsyncGPUReadback.Request(yuvRt, 0, hdrMode == 1 ? TextureFormat.R16 : TextureFormat.R8, req =>
            {
                inFlight--;
                if (req.hasError || session != id) return;
                RecorderEncoder.Video(req.GetData<byte>(), w, h, time);
            });
            if (!thumbTaken && t >= 0.5) { thumbTaken = true; Thumbnail(name, source); }
        }

        /// <summary>A 320-wide JPG of this frame beside the clip (ClipLibrary.SaveInfo), read back without a stall: the
        /// packer's thumbnail pass (SDR colour, an HDR picture tone-mapped) with this frame's settings.</summary>
        void Thumbnail(string clip, RenderTexture source)
        {
            int tw = 320, th = Mathf.Max(8, Mathf.RoundToInt(320f * Screen.height / Mathf.Max(1, Screen.width)));
            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            Graphics.Blit(source, rt, packer, 1);
            RenderTexture.active = active;
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                RenderTexture.ReleaseTemporary(rt);
                if (req.hasError) return;
                var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                tex.LoadRawTextureData(req.GetData<byte>());
                tex.Apply();
                byte[] jpg = tex.EncodeToJPG(85);
                Destroy(tex);
                try
                {
                    System.IO.Directory.CreateDirectory(ClipLibrary.ThumbFolder);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(ClipLibrary.ThumbFolder, clip + ".jpg"), jpg);
                }
                catch (Exception e) { Debug.LogWarning("ScreenRecorder: " + e.Message); }
            });
        }

        // ---- the sound -----------------------------------------------------------------------------------------

        /// <summary>The tap on whichever AudioListener the scene has now (a new scene brings its own).</summary>
        void KeepListening()
        {
            if (!hearing) return;
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener != null && listener.GetComponent<RecorderAudioTap>() == null) listener.gameObject.AddComponent<RecorderAudioTap>();
        }

        /// <summary>The audio thread's mix (RecorderAudioTap): a gap of over a fifth of a second since the last block is
        /// silence first, so the sound stays with the picture.</summary>
        public static void OnAudio(float[] data, int channels)
        {
            if (!hearing || channels <= 0 || rate <= 0) return;
            int frames = data.Length / channels;
            long due = (long)((AudioSettings.dspTime - dspStart) * rate) - (long)(frames * (double)rate / Math.Max(1, inputRate));
            while (due - samplesWritten > rate / 5)
            {
                int n = (int)Math.Min(silence.Length / 2, due - samplesWritten);
                RecorderEncoder.Audio(silence, n, 2, samplesWritten * 10_000_000L / rate);
                samplesWritten += n;
            }
            if (inputRate == rate)
            {
                RecorderEncoder.Audio(data, frames, channels, samplesWritten * 10_000_000L / rate);
                samplesWritten += frames;
                return;
            }
            int made = Resample(data, frames, channels);
            if (made <= 0) return;
            RecorderEncoder.Audio(resampled, made, 2, samplesWritten * 10_000_000L / rate);
            samplesWritten += made;
        }

        /// <summary>The block at the clip's rate, stereo, into 'resampled' (linear between the input frames, the last frame
        /// of the block before kept for the start of this one); returns its frames.</summary>
        static int Resample(float[] data, int frames, int channels)
        {
            double step = inputRate / (double)rate;
            int most = (int)(frames / step) + 2;
            if (resampled.Length < most * 2) resampled = new float[most * 2];
            int count = 0;
            while (true)
            {
                int i0 = (int)Math.Floor(resamplePos);
                if (i0 + 1 >= frames) break;
                float t = (float)(resamplePos - i0);
                for (int c = 0; c < 2; c++)
                {
                    int ch = Math.Min(c, channels - 1);
                    float a = i0 < 0 ? previous[c] : data[i0 * channels + ch];
                    float b = data[(i0 + 1) * channels + ch];
                    resampled[count * 2 + c] = a + (b - a) * t;
                }
                count++;
                resamplePos += step;
            }
            resamplePos -= frames;
            for (int c = 0; c < 2; c++) previous[c] = data[(frames - 1) * channels + Math.Min(c, channels - 1)];
            return count;
        }
    }
}
