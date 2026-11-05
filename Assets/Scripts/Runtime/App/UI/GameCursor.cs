// GameCursor.cs
// Modified: the mouse cursor in the interface's style (Options > Graphics "Game mouse cursor", Settings.GameCursor, on by
// default): a pointer symmetric about its 62 degree axis, in the menus' colours: a dark blue fill lit a little along its
// edge, a cyan outline (the buttons' line colour) and a soft cyan glow. Drawn in code at the screen's scale (remake: 24 px at 1080 lines at 100 %, was 32, times Options > Accessibility
// "Cursor size", Settings.CursorSizePercent), so it is sharp at any resolution, and set as the
// hardware cursor at 100 % (Cursor.SetCursor; Modified: a software one, drawn by Unity, at the other Cursor sizes, scaled
// from the size the system shows the hardware one at): it shows and hides as the system's did
// (InputMode.CursorShown, the flight's mouse steering). The original has no cursor of its own (the PC version used the
// system's). Desktop only; not on a dedicated server.

using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class GameCursor : MonoBehaviour
    {
        static GameCursor instance;
        Texture2D texture;
        int madeSize = -1;
        CursorMode madeMode = CursorMode.Auto;
        bool madeLinear;
        bool applied;
        float nextCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (instance != null || Application.isMobilePlatform || Multiplayer.DedicatedServer.Enabled) return;
            var go = new GameObject("GameCursor");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameCursor>();
        }

        void OnEnable() { Settings.Changed += Apply; Apply(); }
        void OnDisable() { Settings.Changed -= Apply; }

        void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;   // a resolution or window size change
            if (Settings.GameCursor && TextureSize(Screen.height) != madeSize) Apply();
        }

        // Modified: 24 px at 1080 lines (32 was a bit large), in 2 px steps: the 100 % texture, the hardware cursor's.
        static int SizeFor(int height) => Mathf.Clamp(Mathf.RoundToInt(24f * height / 1080f / 2f) * 2, 12, 128);

        // Modified: Options > Accessibility "Cursor size". At 100 % the hardware cursor, the system's way: it shows the texture
        // at the system's cursor size (Windows scales it to SM_CXCURSOR), so the other sizes, drawn by Unity
        // (CursorMode.ForceSoftware) at the texture's own size, are that size times the option (a player's request: the
        // option only made the hardware cursor sharper or blurrier, never bigger or smaller).
        static int SoftwareSize(int height) => Mathf.Clamp(Mathf.RoundToInt(SystemCursorSize(height) * Settings.CursorSizePercent / 100f / 2f) * 2, 12, 256);

        /// <summary>How big the system shows a hardware cursor, in screen pixels: Windows' SM_CXCURSOR, else the 100 % texture's.</summary>
        static int SystemCursorSize(int height)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try { int s = GetSystemMetrics(13); if (s > 0) return s; } catch (System.Exception) { }
#endif
            return SizeFor(height);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetSystemMetrics(int index);
#endif

        static bool Hardware => Settings.CursorSizePercent == 100;
        // The hardware texture drawn at the system's cursor size itself: drawn at 24 px a 1080 line screen and scaled up by
        // Windows to its 32 px or more, it came out blurrier than the software sizes (a player's report).
        static int TextureSize(int height) => Hardware ? SystemCursorSize(height) : SoftwareSize(height);

        void Apply()
        {
            if (!Settings.GameCursor)
            {
                if (applied) Cursor.SetCursor(null, Vector2.zero, madeMode);
                applied = false;
                return;
            }
            int size = TextureSize(Screen.height);
            var mode = Hardware ? CursorMode.Auto : CursorMode.ForceSoftware;   // Modified: SoftwareSize
            if (mode != madeMode) { madeMode = mode; applied = false; }
            if (size != madeSize || texture == null || madeLinear != !Hardware)
            {
                if (texture != null) Destroy(texture);
                // Modified: the software cursor's texture read as linear: Unity drew its sRGB colours decoded once more than the
                // system shows the hardware one's, a darker blue at every size but 100 % (a player's report).
                madeLinear = !Hardware;
                texture = Draw(size, madeLinear);
                madeSize = size;
                applied = false;
            }
            if (applied) return;
            Cursor.SetCursor(texture, new Vector2(size * 3.6f / 32f, size * 2.9f / 32f), mode);   // the outline's tip
            applied = true;
        }

        // The pointer, in a 32-unit box, y down: its tip at (4, 3.6), its axis 62 degrees below the x axis, the two arms 25.5
        // units long at 24 degrees either side of it, the notch on the axis 19 units out.
        static readonly Vector2[] Arrow =
        {
            new Vector2(4f, 3.6f), new Vector2(24.09f, 19.30f), new Vector2(12.92f, 20.38f), new Vector2(5.78f, 29.04f),
        };

        static Texture2D Draw(int size, bool linear)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, linear) { name = "GameCursor", filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float unit = 32f / size;   // box units per pixel
            var fill = new Color(0.03f, 0.10f, 0.17f, 0.92f);
            var line = new Color(0.35f, 0.85f, 1f, 1f);
            var glow = new Color(0.20f, 0.70f, 1f, 1f);
            const float half = 0.75f;   // the outline: 1.5 units, centred on the edge
            const int ss = 4;   // samples per axis
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / ss) * unit, (y + (sy + 0.5f) / ss) * unit);
                            float d = EdgeDistance(p);
                            Color c;
                            if (d < half) c = line;
                            else if (Inside(p))
                            {
                                c = Color.Lerp(fill, line, Mathf.Clamp01(1f - (d - half) / 3f) * 0.35f);   // the inner edge lit a little
                                c.a = fill.a;
                            }
                            else { c = glow; c.a = 0.45f * Mathf.Exp(-(d - half) * 1.4f); }
                            sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);   // premultiplied for the average
                        }
                    sum /= ss * ss;
                    px[(size - 1 - y) * size + x] = sum.a > 1e-4f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;   // row 0 is the bottom
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        static bool Inside(Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = Arrow.Length - 1; i < Arrow.Length; j = i++)
                if ((Arrow[i].y > p.y) != (Arrow[j].y > p.y)
                    && p.x < (Arrow[j].x - Arrow[i].x) * (p.y - Arrow[i].y) / (Arrow[j].y - Arrow[i].y) + Arrow[i].x)
                    c = !c;
            return c;
        }

        static float EdgeDistance(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = Arrow.Length - 1; i < Arrow.Length; j = i++)
            {
                Vector2 a = Arrow[j], b = Arrow[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
