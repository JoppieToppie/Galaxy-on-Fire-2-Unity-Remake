// MovieMode.cs
// Remake-only: Movie mode (the pause menu's "Movie mode", single player in flight), players' request 2026-11-28: making
// films of the game. The world stands still while a scene is set up with the photo tools' free camera and Camera
// settings (PhotoTools: the lens, depth of field, the grade, hide the ship...) and a timeline (Events.MovieTimeline):
//   cues         any event command at a time (spawn, npc orders, camera shots, cutscene, fade, letterbox, title, dialog,
//                radio, sound, music, kill, tp...): typed, or started from the Insert buttons (placed where the camera
//                looks); Test runs one at once
//   camera keys  the camera's pose (K): the take flies a path through them; "cut" jumps instead
//   start        the player's ship is put back there before each take (Set start)
// Play (F5, the controller's Start) runs a take: the scene's last take cleared (its ships and objects), the ship placed,
// the world running, the cues as one event run (EventRunner.StartText), the camera on the keys (or the chase camera and
// any camera cues when there are none), the HUD hidden (FlightHud's hud-cinematic: radio and fades show), the player
// free to fly. Esc / Start or the end stops it and the world stands again where it was. Record it with any screen
// recorder (OBS, the Xbox Game Bar, ShadowPlay...): nothing of the editor shows during a take. The timeline is kept for
// the run (static) and saved to <data>/Movies (Save / Open). Plain class driven by FlightHud.

using System;
using System.Collections.Generic;
using System.Globalization;
using GoF2Remake.Data;
using GoF2Remake.Events;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public sealed class MovieMode
    {
        const string RunName = "movie";
        const float Snap = 0.05f;                 // s: the markers' and playhead's step
        const float AheadMetres = 250f;           // the Insert buttons' place: this far where the camera looks
        const float FreeRangeMetres = 50000f;

        // Kept for the run, like the photo settings: leaving Movie mode (or a death) doesn't lose the scene.
        static MovieTimeline movie = new MovieTimeline();
        static bool dirty;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { movie = new MovieTimeline(); dirty = false; recordTakes = false; }

        readonly VisualElement root, overlay, hints, panel, lanes, ruler, playheadLine, inspector, files;
        readonly VisualElement[] laneRows = new VisualElement[Enum.GetValues(typeof(MovieTrack)).Length];
        readonly Label message, timeLabel, help;
        readonly TextField nameField, lengthField;
        readonly Button playButton, viewButton, recordButton;
        static bool recordTakes;
        bool takeRecorded;
        readonly PhotoTools tools;
        readonly MonoBehaviour host;

        SpaceLevel level;
        Camera cam;
        ChaseCamera chase;
        bool chaseWasEnabled, savedFree, uiHidden, shotLatched, shotView, capturing;
        float playhead, clock, messageMs;
        object selected;                          // a MovieCue or a MovieCameraKey
        object dragging;                          // the marker being dragged, or this (the playhead)
        bool discardArmed;
        InputKind hintsKind = (InputKind)(-1);
        readonly HashSet<int> takeTags = new HashSet<int>();
        readonly List<GameObject> takeObjects = new List<GameObject>();
        HashSet<GameObject> objectsBefore;

        public bool Active { get; private set; }
        /// <summary>A take runs: the world goes on and the player flies (FlightHud carries on under it).</summary>
        public bool Playing { get; private set; }
        public Action<bool> ButtonSound;

        static string T(string key, string english) => Localization.Extra(key, english);
        static string Num(float v, string format = "0.0#") => v.ToString(format, CultureInfo.InvariantCulture);

        public MovieMode(VisualElement hudRoot, MonoBehaviour coroutineHost)
        {
            root = hudRoot;
            host = coroutineHost;
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("photo-overlay");
            overlay.AddToClassList("movie-overlay");
            var title = OrbitViewUi.Title();
            title.text = T("movieTitle", "MOVIE MODE");
            overlay.Add(title);
            message = OrbitViewUi.Message();
            message.AddToClassList("movie-message");
            overlay.Add(message);
            hints = OrbitViewUi.Hints();
            overlay.Add(hints);

            panel = new VisualElement();
            panel.AddToClassList("movie-panel");
            panel.AddToClassList(SafeArea.EdgeClass);

            // The transport and the file.
            var bar = Row();
            playButton = Btn(bar, T("moviePlay", "PLAY (F5)"), Play, true);
            timeLabel = new Label { pickingMode = PickingMode.Ignore };
            timeLabel.AddToClassList("movie-time");
            timeLabel.AddToClassList("gof-semibold");
            bar.Add(timeLabel);
            Btn(bar, T("movieKey", "CAMERA KEY (K)"), AddKey);
            viewButton = Btn(bar, "", ToggleShotView);
            Btn(bar, T("movieSetStart", "SET START"), SetStart);
            Btn(bar, T("movieDelete", "DELETE"), DeleteSelected);
            // The built-in screen recorder: each take its own clip, named after the movie (ScreenRecorder).
            if (ScreenRecorder.Supported)
            {
                recordButton = Btn(bar, "", () => { recordTakes = !recordTakes; Rebuild(); });
                Btn(bar, T("recordedClips", "Recorded clips").UiCaps(), ScreenRecorder.ShowClips);
            }
            Label(bar, T("movieName", "Name"));
            nameField = Field(bar, "movie-field--name", v => { movie.name = v; dirty = true; });
            Label(bar, T("movieLength", "Length (s)"));
            lengthField = Field(bar, "movie-field--short", v =>
            {
                if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float s)) { movie.length = Mathf.Clamp(s, MovieTimeline.MinLength, MovieTimeline.MaxLength); dirty = true; }
                Rebuild();
            });
            Btn(bar, T("movieNew", "NEW"), () => Discarding(() => { movie = new MovieTimeline { station = Session.StationIndex }; dirty = false; playhead = 0f; selected = null; Rebuild(); }));
            Btn(bar, T("movieOpen", "OPEN"), ShowFiles);
            Btn(bar, T("movieSave", "SAVE"), Save);
            Btn(bar, T("hudBack", "BACK").OgCase(), Exit);
            panel.Add(bar);

            // Cues started from a template, placed where the camera looks.
            var insert = Row();
            Label(insert, T("movieInsert", "Add cue:"));
            Btn(insert, T("movieCueBlank", "COMMAND"), () => AddCue(""));
            Btn(insert, T("movieCueSpawn", "SPAWN SHIP"), () => AddCue($"spawn @a {Session.ShipIndex} enemy named {NewShipName()} at {Ahead()}"));
            Btn(insert, T("movieCueFly", "FLY HERE"), () => AddCue($"npc @a {LastShipName()} goto {Ahead()}"));
            Btn(insert, T("movieCueAttack", "ATTACK ME"), () => AddCue($"npc @a {LastShipName()} attack player"));
            Btn(insert, T("movieCueShot", "CAMERA SHOT"), () => AddCue($"camera @a {Here()} look ship {LastShipName()} follow"));
            Btn(insert, T("movieCueFadeOut", "FADE OUT"), () => AddCue("fade @a out 1"));
            Btn(insert, T("movieCueFadeIn", "FADE IN"), () => AddCue("fade @a in 1"));
            Btn(insert, T("movieCueBars", "LETTERBOX"), () => AddCue("letterbox @a on"));
            Btn(insert, T("movieCueTitle", "TITLE"), () => AddCue("title @a GALAXY ON FIRE 2 | " + T("movieCueSubtitle", "a film") + " for 4"));
            Btn(insert, T("movieCueRadio", "RADIO"), () => AddCue("radio @a player : " + T("movieCueRadioText", "Here they come!")));
            Btn(insert, T("movieCueMusic", "MUSIC"), () => AddCue("music @a battle"));
            Btn(insert, T("movieCueSound", "SOUND"), () => AddCue("sound @a alarm"));
            panel.Add(insert);

            // The lanes: names at the left, the ruler and the markers at the right, the playhead over them all.
            var tracks = new VisualElement();
            tracks.AddToClassList("movie-tracks");
            var names = new VisualElement { pickingMode = PickingMode.Ignore };
            names.AddToClassList("movie-track-names");
            string[] laneNames = { T("movieTrackCamera", "Camera keys"), T("movieTrackScene", "Shots & screen"), T("movieTrackShips", "Ships"),
                                   T("movieTrackText", "Text & radio"), T("movieTrackSound", "Sound & music"), T("movieTrackOther", "Other") };
            lanes = new VisualElement();
            lanes.AddToClassList("movie-lanes");
            ruler = new VisualElement { pickingMode = PickingMode.Ignore };
            ruler.AddToClassList("movie-ruler");
            lanes.Add(ruler);
            for (int i = 0; i < laneRows.Length; i++)
            {
                var n = new Label(laneNames[i]) { pickingMode = PickingMode.Ignore };
                n.AddToClassList("movie-track-name");
                names.Add(n);
                laneRows[i] = new VisualElement { pickingMode = PickingMode.Ignore };
                laneRows[i].AddToClassList("movie-lane");
                lanes.Add(laneRows[i]);
            }
            playheadLine = new VisualElement { pickingMode = PickingMode.Ignore };
            playheadLine.AddToClassList("movie-playhead");
            lanes.Add(playheadLine);
            lanes.RegisterCallback<PointerDownEvent>(OnLanesDown);
            lanes.RegisterCallback<PointerMoveEvent>(OnLanesMove);
            lanes.RegisterCallback<PointerUpEvent>(OnLanesUp);
            lanes.RegisterCallback<GeometryChangedEvent>(_ => Rebuild());
            tracks.Add(names);
            tracks.Add(lanes);
            panel.Add(tracks);

            inspector = Row();
            panel.Add(inspector);
            files = Row();
            files.style.display = DisplayStyle.None;
            panel.Add(files);
            help = new Label(T("movieHelp", "Click the lanes to move the playhead (, and . step it), drag a marker to retime it. A cue is any event command, @a is you; places are this orbit's coordinates. Record the takes with any screen recorder."))
                { pickingMode = PickingMode.Ignore };
            help.AddToClassList("movie-help");
            panel.Add(help);
            overlay.Add(panel);
            root.Add(overlay);
            SafeArea.KeepClear(overlay);
            tools = new PhotoTools(overlay, s => ButtonSound?.Invoke(s));
            tools.Changed += BuildHints;
            tools.Tapped += () => { if (uiHidden) SetUiHidden(false); };
        }

        // ---- building blocks ----------------------------------------------------------------------------------

        static VisualElement Row()
        {
            var r = new VisualElement();
            r.AddToClassList("movie-row");
            return r;
        }

        static void Label(VisualElement parent, string text)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("movie-label");
            parent.Add(l);
        }

        Button Btn(VisualElement parent, string text, Action click, bool primary = false)
        {
            var b = new Button(() => { ButtonSound?.Invoke(false); click(); }) { text = text, focusable = false };
            b.AddToClassList("movie-button");
            b.AddToClassList("gof-semibold");
            if (primary) b.AddToClassList("movie-button--primary");
            b.RegisterCallback<PointerDownEvent>(_ => ButtonSound?.Invoke(true), TrickleDown.TrickleDown);
            parent.Add(b);
            return b;
        }

        /// <summary>A text field that commits on Enter or when it loses the focus; while one has the focus the camera's and the
        /// timeline's keys rest (Typing).</summary>
        TextField Field(VisualElement parent, string sizeClass, Action<string> commit)
        {
            var f = new TextField { isDelayed = true };
            f.AddToClassList("gof-field");
            f.AddToClassList("movie-field");
            if (sizeClass != null) f.AddToClassList(sizeClass);
            f.RegisterValueChangedCallback(e => commit(e.newValue));
            parent.Add(f);
            return f;
        }

        // ---- entering and leaving -----------------------------------------------------------------------------

        public static bool Available(SpaceLevel level) =>
            level != null && level.Player != null && !level.Cutscene && !GoF2Remake.Multiplayer.NetGame.Active;

        public void Enter(SpaceLevel spaceLevel)
        {
            if (Active || !Available(spaceLevel)) return;
            cam = Camera.main;
            if (cam == null) return;
            level = spaceLevel;
            Active = true;
            shotLatched = true;   // the Enter / A that chose it in the menu
            chase = cam.GetComponent<ChaseCamera>();
            chaseWasEnabled = chase != null && chase.enabled;
            if (chase != null) chase.enabled = false;
            ObjectDocking.CameraHeld = true;
            savedFree = PhotoSettings.Free;
            PhotoSettings.Free = true;   // Movie mode works with the free camera (the orbit has nothing to turn here)
            if (movie.station < 0) movie.station = Session.StationIndex;
            Hold(true);
            root.AddToClassList("hud-photo");
            FpsCounter.Suppressed = true;
            overlay.AddToClassList("photo-overlay--shown");
            var ship = level.Player.transform;
            float radius = 30f;
            var rs = ship.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); radius = Mathf.Max(5f, b.extents.magnitude); }
            tools.Begin(cam, ship, () => ship != null ? ship.position : Vector3.zero, radius, FreeRangeMetres, float.NegativeInfinity,
                        () => (cam.transform.position, cam.transform.rotation), false);
            SetUiHidden(false);
            files.style.display = DisplayStyle.None;
            if (movie.station != Session.StationIndex) Say(T("movieOtherOrbit", "This movie was made in another orbit: its places are that orbit's."));
            BuildHints();
            Rebuild();
        }

        public void Exit()
        {
            if (!Active) return;
            if (Playing) StopTake();
            Active = false;
            tools.End();
            PhotoSettings.Free = savedFree;
            SetUiHidden(false);
            root.RemoveFromClassList("hud-photo");
            FpsCounter.Suppressed = false;
            overlay.RemoveFromClassList("photo-overlay--shown");
            if (chase != null) { chase.enabled = chaseWasEnabled; if (chaseWasEnabled) chase.Snap(); }
            ObjectDocking.CameraHeld = false;
            Hold(false);
            Unfocus();
        }

        /// <summary>One of the panel's text fields has the focus (read from the panel: a field removed while focused sends no
        /// focus-out).</summary>
        bool Typing => panel.focusController?.focusedElement is VisualElement v && panel.Contains(v)
                       && (v is TextField || v.GetFirstAncestorOfType<TextField>() != null);

        void Unfocus() => panel.focusController?.focusedElement?.Blur();

        /// <summary>The world stands while the scene is set up (the pause menu's hold: time, sounds and controls).</summary>
        void Hold(bool on)
        {
            if (level != null && level.Navigation != null) level.Navigation.PauseMenuOpen = on;
        }

        // ---- every frame -------------------------------------------------------------------------------------

        /// <summary>FlightHud's Update while active. True: Movie mode had the frame (setting up); false: a take runs and the
        /// game goes on under it.</summary>
        public bool Tick()
        {
            if (!Active) return false;
            if (level == null || level.Player == null || cam == null) { Exit(); return false; }
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;
            var pad = Gamepad.current;
            if (Playing)
            {
                clock += Time.deltaTime;
                EventRunner.BatchTagsOf(RunName, takeTags);
                tools.LateTick(false);   // the grade and effects stay on the take
                if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame)
                    || clock >= movie.length) { StopTake(); return true; }
                return false;
            }
            if (ScreenRecorder.ClipsOpen) return true;   // the clips manager over it has the keys
            if (Typing)
            {
                // Typing: the keys are the field's; Esc gives them back.
                if (kb != null && kb.escapeKey.wasPressedThisFrame) Unfocus();
                kb = null;
            }
            bool panelKeys = tools.TickKeys(kb, pad, out bool backTaken);
            // Always the free camera: the photo tools' F / Camera row would switch to an orbit Movie mode doesn't drive.
            if (!PhotoSettings.Free) { PhotoSettings.Free = true; tools.RestartFree(); }
            if (!panelKeys && !backTaken)
            {
                if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { Exit(); return true; }
                if ((kb != null && kb.f5Key.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame)) { Play(); return true; }
                if (kb != null) Keys(kb);
                if (OrbitViewUi.ShotPressed(kb, pad, ref shotLatched) && !capturing) host.StartCoroutine(Capture());
                if ((kb != null && kb.hKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) SetUiHidden(!uiHidden);
            }
            if (InputMode.Current != hintsKind) BuildHints();
            if (messageMs > 0f && (messageMs -= Time.unscaledDeltaTime * 1000f) <= 0f) message.text = "";
            if (shotView && movie.CameraAt(playhead, out var p, out var r, out float fov))
            {
                cam.transform.SetPositionAndRotation(OrbitScale.ToUnity(p), r);
                cam.fieldOfView = fov;
                tools.LateTick(false);
            }
            else
            {
                tools.TickFree(kb, pad, Time.unscaledDeltaTime, OverControl, out bool looking);
                if (tools.Free) { var (fp, fr) = tools.FreePose; cam.transform.SetPositionAndRotation(fp, PhotoTools.Rolled(fr)); }
                overlay.EnableInClassList("photo-overlay--turning", looking && !tools.PanelOpen);
                tools.LateTick();
            }
            UpdatePlayhead();
            return true;
        }

        /// <summary>FlightHud's LateUpdate: during a take the camera keys place the camera (after the chase camera, which is off
        /// then; a camera cue's shot, later in the frame, wins while it runs).</summary>
        public void LateTick()
        {
            if (!Playing || cam == null || movie.keys.Count == 0) return;
            if (chase != null && chase.enabled && !chase.scriptCamera) chase.enabled = false;   // a cue's "cutscene end" gave it back
            if (!movie.CameraAt(clock, out var p, out var r, out float fov)) return;
            cam.transform.SetPositionAndRotation(OrbitScale.ToUnity(p), r);
            cam.fieldOfView = fov;
        }

        void Keys(Keyboard kb)
        {
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (kb.kKey.wasPressedThisFrame) AddKey();
            if (kb.vKey.wasPressedThisFrame) ToggleShotView();
            if (kb.deleteKey.wasPressedThisFrame) DeleteSelected();
            if (ctrl && kb.sKey.wasPressedThisFrame) Save();
            float step = shift ? 1f : 0.1f;
            if (kb.commaKey.wasPressedThisFrame) SetPlayhead(playhead - step);
            if (kb.periodKey.wasPressedThisFrame) SetPlayhead(playhead + step);
            if (kb.homeKey.wasPressedThisFrame) SetPlayhead(0f);
            if (kb.endKey.wasPressedThisFrame) SetPlayhead(movie.length);
        }

        bool OverControl(Vector2 screen)
        {
            if (uiHidden) return false;
            if (OrbitViewUi.OverControl(root, null, hints, screen)) return true;
            if (panel.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel.panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.worldBound.Contains(p);
        }

        void SetUiHidden(bool hidden)
        {
            uiHidden = hidden;
            overlay.EnableInClassList("orbit-view--clean", hidden);
        }

        void BuildHints()
        {
            hintsKind = InputMode.Current;
            overlay.EnableInClassList("can-hover", hintsKind == InputKind.KeyboardMouse);
            OrbitViewUi.BuildHints(hints, hintsKind, new[] { "↑", "←", "↓", "→" },
                () => { ButtonSound?.Invoke(false); SetUiHidden(true); },
                () => { ButtonSound?.Invoke(false); if (!capturing) host.StartCoroutine(Capture()); },
                () => { ButtonSound?.Invoke(false); Exit(); },
                () => ButtonSound?.Invoke(true),
                null, tools.ToggleSettings, true);   // no orbit / free switch: always free
        }

        System.Collections.IEnumerator Capture()
        {
            capturing = true;
            yield return tools.Capture(overlay, ok => Say(Localization.Get(ok ? 55 : 56)));
            capturing = false;
        }

        void Say(string text)
        {
            message.text = text;
            messageMs = 5000f;
        }

        // ---- the take ----------------------------------------------------------------------------------------

        void Play()
        {
            if (Playing) return;
            if (EventRunner.Running && !EventRunner.IsRunning(RunName)) { Say(T("movieEventBusy", "Another event is running: a take can't start.")); return; }
            EventRunner.StopText(RunName);
            string script = movie.Script();
            string error = EventRunner.Check(script);
            if (error != null) { Say(error); return; }
            ClearLastTake();
            Unfocus();
            if (movie.hasStart && level.Player != null)
                level.Player.transform.SetPositionAndRotation(OrbitScale.ToUnity(movie.startPosition), movie.startRotation);
            objectsBefore = SpawnedObjects();
            Playing = true;
            clock = 0f;
            files.style.display = DisplayStyle.None;
            overlay.RemoveFromClassList("photo-overlay--shown");
            root.RemoveFromClassList("hud-photo");
            if (chase != null) { chase.enabled = movie.keys.Count == 0; if (chase.enabled) chase.Snap(); }
            Hold(false);
            // Recorded from this frame on: the editor is already off the screen (no REC mark in a take).
            takeRecorded = recordTakes && !ScreenRecorder.Recording && ScreenRecorder.Begin(movie.name, false);
            string answer = EventRunner.StartText(RunName, script);
            if (!EventRunner.IsRunning(RunName)) { StopTake(); Say(answer); }
        }

        void StopTake()
        {
            if (!Playing) return;
            EventRunner.BatchTagsOf(RunName, takeTags);
            EventRunner.StopText(RunName);   // its cutscene, fade, music... end with it
            Playing = false;
            if (takeRecorded) { takeRecorded = false; ScreenRecorder.Stop(); }
            Hold(true);
            if (objectsBefore != null)
                foreach (var o in SpawnedObjects()) if (!objectsBefore.Contains(o)) takeObjects.Add(o);
            objectsBefore = null;
            if (chase != null) chase.enabled = false;   // after the run's "cutscene end", which gives it back
            SetPlayhead(clock);
            root.AddToClassList("hud-photo");
            overlay.AddToClassList("photo-overlay--shown");
            tools.RestartFree();   // the free camera from where the take left it
        }

        /// <summary>The last take's ships (its spawn batches) and objects go, so the next one starts on the same scene.</summary>
        void ClearLastTake()
        {
            var traffic = UnityEngine.Object.FindAnyObjectByType<Traffic>();
            if (traffic != null && takeTags.Count > 0)
                foreach (var s in traffic.Ships)
                    if (s != null && !s.Gone && s.Spec != null && s.Spec.eventTag != 0 && takeTags.Contains(s.Spec.eventTag)) s.Vanish();
            foreach (var go in takeObjects)
            {
                if (go == null) continue;
                level.Navigation?.Targets.RemoveAll(t => t.kind == Navigation.Kind.Marker && t.transform != null && t.transform.IsChildOf(go.transform));
                UnityEngine.Object.Destroy(go);
            }
            takeTags.Clear();
            takeObjects.Clear();
        }

        /// <summary>The objects /spawn placed (DebugSpawner.SpawnObject: "Debug ..." with an Obstacle).</summary>
        static HashSet<GameObject> SpawnedObjects()
        {
            var set = new HashSet<GameObject>();
            foreach (var o in UnityEngine.Object.FindObjectsByType<Obstacle>())
                if (o != null && o.gameObject.name.StartsWith("Debug ", StringComparison.Ordinal)) set.Add(o.gameObject);
            return set;
        }

        // ---- editing -----------------------------------------------------------------------------------------

        void SetPlayhead(float t)
        {
            playhead = Mathf.Clamp(Mathf.Round(t / Snap) * Snap, 0f, movie.length);
            UpdatePlayhead();
        }

        void UpdatePlayhead()
        {
            playheadLine.style.left = Length.Percent(playhead / movie.length * 100f);
            timeLabel.text = $"{Num(playhead, "0.00")} / {Num(movie.length, "0.0")} s";
        }

        /// <summary>The camera's pose at the playhead: a key there is updated, else a new one.</summary>
        void AddKey()
        {
            var key = movie.keys.Find(k => Mathf.Abs(k.time - playhead) < Snap * 0.5f);
            if (key == null) { key = new MovieCameraKey { time = playhead }; movie.keys.Add(key); }
            key.position = OrbitScale.ToGame(cam.transform.position);
            key.rotation = cam.transform.rotation;
            key.fov = cam.fieldOfView;
            movie.Sort();
            selected = key;
            dirty = true;
            Rebuild();
        }

        void AddCue(string command)
        {
            var cue = new MovieCue { time = playhead, command = command };
            movie.cues.Add(cue);
            movie.Sort();
            selected = cue;
            dirty = true;
            Rebuild();
            inspector.Q<TextField>("movieCommand")?.Focus();
        }

        void DeleteSelected()
        {
            if (selected is MovieCue c) movie.cues.Remove(c);
            else if (selected is MovieCameraKey k) movie.keys.Remove(k);
            else return;
            selected = null;
            dirty = true;
            Rebuild();
        }

        void SetStart()
        {
            var t = level.Player.transform;
            movie.hasStart = true;
            movie.startPosition = OrbitScale.ToGame(t.position);
            movie.startRotation = t.rotation;
            dirty = true;
            Say(T("movieStartSet", "Each take starts with your ship here."));
        }

        void ToggleShotView()
        {
            if (movie.keys.Count == 0 && !shotView) { Say(T("movieNoKeys", "No camera keys yet: K adds one from this view.")); return; }
            shotView = !shotView;
            if (!shotView) tools.RestartFree();
            Rebuild();
        }

        /// <summary>A game coordinate where the camera looks, AheadMetres out ("x y z" for the commands).</summary>
        string Ahead() => Coords(cam.transform.position + cam.transform.forward * AheadMetres);
        string Here() => Coords(cam.transform.position);
        static string Coords(Vector3 unity)
        {
            var g = OrbitScale.ToGame(unity);
            return $"{Mathf.RoundToInt(g.x)} {Mathf.RoundToInt(g.y)} {Mathf.RoundToInt(g.z)}";
        }

        string NewShipName()
        {
            int n = 1;
            while (movie.cues.Exists(c => c.command.Contains("named Ship" + n + " ") || c.command.EndsWith("named Ship" + n))) n++;
            return "Ship" + n;
        }

        /// <summary>The name of the last ship a cue spawns ("named X"), Ship1 when none.</summary>
        string LastShipName()
        {
            for (int i = movie.cues.Count - 1; i >= 0; i--)
            {
                var w = MovieTimeline.Clean(movie.cues[i].command).Split(' ');
                for (int j = 0; j + 1 < w.Length; j++) if (w[j] == "named") return w[j + 1];
            }
            return "Ship1";
        }

        void Save()
        {
            try
            {
                string path = movie.Save();
                dirty = false;
                Say(string.Format(T("movieSaved", "Saved: {0}"), path.Replace('\\', '/')));
            }
            catch (Exception e) { Say(e.Message); }
            Rebuild();
        }

        void ShowFiles()
        {
            files.Clear();
            files.style.display = DisplayStyle.Flex;
            Label(files, T("movieOpenWhich", "Open:"));
            var list = MovieTimeline.List();
            if (list.Count == 0) Label(files, string.Format(T("movieNoFiles", "No saved movies in {0}."), MovieTimeline.Folder.Replace('\\', '/')));
            foreach (var n in list)
            {
                string name = n;
                Btn(files, name, () => Discarding(() =>
                {
                    var m = MovieTimeline.Load(name);
                    if (m == null) { Say(string.Format(T("movieUnreadable", "{0} couldn't be read."), name)); return; }
                    movie = m;
                    dirty = false;
                    selected = null;
                    playhead = 0f;
                    files.style.display = DisplayStyle.None;
                    if (movie.station >= 0 && movie.station != Session.StationIndex) Say(T("movieOtherOrbit", "This movie was made in another orbit: its places are that orbit's."));
                    Rebuild();
                }));
            }
            Btn(files, T("movieClose", "CLOSE"), () => files.style.display = DisplayStyle.None);
        }

        /// <summary>Unsaved changes: the first press warns, the second goes ahead.</summary>
        void Discarding(Action go)
        {
            if (dirty && !discardArmed) { discardArmed = true; Say(T("movieUnsaved", "Unsaved changes: press again to drop them.")); return; }
            discardArmed = false;
            go();
        }

        // ---- the timeline view -------------------------------------------------------------------------------

        void Rebuild()
        {
            discardArmed = discardArmed && dirty;
            nameField.SetValueWithoutNotify(movie.name);
            lengthField.SetValueWithoutNotify(Num(movie.length, "0.##"));
            viewButton.text = shotView ? T("movieViewShot", "VIEW: KEYS (V)") : T("movieViewFree", "VIEW: FREE (V)");
            viewButton.EnableInClassList("movie-button--on", shotView);
            if (recordButton != null)
            {
                recordButton.text = recordTakes ? T("movieRecordOn", "RECORD TAKES: ON") : T("movieRecordOff", "RECORD TAKES: OFF");
                recordButton.EnableInClassList("movie-button--on", recordTakes);
            }
            // The ruler: about ten labels whatever the length.
            ruler.Clear();
            float[] steps = { 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f };
            float step = steps[steps.Length - 1];
            foreach (var s in steps) if (movie.length / s <= 12f) { step = s; break; }
            for (float t = 0f; t < movie.length - 1e-3f; t += step)
            {
                var tick = new Label(Num(t, "0") + " s") { pickingMode = PickingMode.Ignore };
                tick.AddToClassList("movie-tick");
                tick.style.left = Length.Percent(t / movie.length * 100f);
                ruler.Add(tick);
            }
            foreach (var row in laneRows) row.Clear();
            foreach (var k in movie.keys) Marker(laneRows[(int)MovieTrack.Camera], k, k.time, k.cut ? "movie-marker--cut" : "movie-marker--key");
            foreach (var c in movie.cues) Marker(laneRows[(int)MovieTimeline.TrackOf(c.command)], c, c.time, null);
            if (playhead > movie.length) playhead = movie.length;
            UpdatePlayhead();
            BuildInspector();
        }

        void Marker(VisualElement lane, object item, float time, string kind)
        {
            var m = new VisualElement { userData = item };
            m.AddToClassList("movie-marker");
            if (kind != null) m.AddToClassList(kind);
            if (item == selected) m.AddToClassList("movie-marker--selected");
            m.style.left = Length.Percent(Mathf.Clamp(time, 0f, movie.length) / movie.length * 100f);
            if (item is MovieCue c) m.tooltip = MovieTimeline.Clean(c.command);
            lane.Add(m);
        }

        float TimeAt(Vector2 panelPosition)
        {
            var local = lanes.WorldToLocal(panelPosition);
            float w = Mathf.Max(1f, lanes.layout.width);
            return Mathf.Clamp(Mathf.Round(local.x / w * movie.length / Snap) * Snap, 0f, movie.length);
        }

        void OnLanesDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            Unfocus();   // a field being typed in commits first (and the inspector may rebuild)
            var target = e.target as VisualElement;
            if (target != null && target.ClassListContains("movie-marker"))
            {
                selected = target.userData;
                dragging = selected;
                if (selected is MovieCue c) SetPlayhead(c.time);
                else if (selected is MovieCameraKey k) SetPlayhead(k.time);
                Rebuild();
            }
            else
            {
                dragging = this;
                SetPlayhead(TimeAt(e.position));
            }
            lanes.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnLanesMove(PointerMoveEvent e)
        {
            if (dragging == null || !lanes.HasPointerCapture(e.pointerId)) return;
            float t = TimeAt(e.position);
            if (dragging == (object)this) { SetPlayhead(t); return; }
            if (dragging is MovieCue c && c.time != t) { c.time = t; dirty = true; }
            else if (dragging is MovieCameraKey k && k.time != t) { k.time = t; dirty = true; }
            else return;
            SetPlayhead(t);
            Rebuild();
        }

        void OnLanesUp(PointerUpEvent e)
        {
            if (!lanes.HasPointerCapture(e.pointerId)) return;
            lanes.ReleasePointer(e.pointerId);
            if (dragging != null && dragging != (object)this) { movie.Sort(); Rebuild(); }
            dragging = null;
        }

        /// <summary>The selected marker's fields: a cue's time, command and Test; a key's time, field of view, cut, and
        /// Update / Go to.</summary>
        void BuildInspector()
        {
            // The focused field would lose what is being typed: not while one of these has the focus.
            if (Typing && panel.focusController.focusedElement is VisualElement fe && inspector.Contains(fe)) return;
            inspector.Clear();
            if (selected == null)
            {
                Label(inspector, T("movieNothingSelected", "Select a marker, or add a cue or a camera key at the playhead."));
                return;
            }
            Label(inspector, T("movieTime", "Time (s)"));
            float time = selected is MovieCue sc ? sc.time : ((MovieCameraKey)selected).time;
            var timeField = Field(inspector, "movie-field--short", v =>
            {
                if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) return;
                t = Mathf.Clamp(t, 0f, movie.length);
                if (selected is MovieCue c) c.time = t; else if (selected is MovieCameraKey k) k.time = t;
                dirty = true;
                movie.Sort();
                SetPlayhead(t);
                Rebuild();
            });
            timeField.SetValueWithoutNotify(Num(time, "0.00"));
            if (selected is MovieCue cue)
            {
                var command = Field(inspector, "movie-field--command", v => { cue.command = MovieTimeline.Clean(v); dirty = true; Rebuild(); });
                command.name = "movieCommand";
                command.SetValueWithoutNotify(cue.command);
                var answer = new Label { pickingMode = PickingMode.Ignore };
                answer.AddToClassList("movie-label");
                Btn(inspector, T("movieTest", "TEST"), () =>
                {
                    // Runs the command now, as the take would (its ships stay: they aren't the take's).
                    string line = MovieTimeline.Clean(command.value);
                    int sp = line.IndexOf(' ');
                    string said = GoF2Remake.Multiplayer.NetCommands.RunOnServer(sp < 0 ? line : line.Substring(0, sp), sp < 0 ? "" : line.Substring(sp + 1), null);
                    answer.text = said == null ? string.Format(T("movieUnknownCommand", "No command \"{0}\"."), sp < 0 ? line : line.Substring(0, sp))
                                 : said.Length == 0 ? T("movieTestDone", "Done.") : said;
                });
                inspector.Add(answer);
            }
            else if (selected is MovieCameraKey key)
            {
                Label(inspector, T("movieFov", "Field of view"));
                var fov = Field(inspector, "movie-field--short", v =>
                {
                    if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { key.fov = Mathf.Clamp(f, PhotoSettings.MinFov, PhotoSettings.MaxFov); dirty = true; }
                    Rebuild();
                });
                fov.SetValueWithoutNotify(Num(key.fov, "0.#"));
                var cut = new Toggle(T("movieCut", "Cut to it")) { value = key.cut, focusable = false };
                cut.AddToClassList("movie-toggle");
                cut.RegisterValueChangedCallback(e => { key.cut = e.newValue; dirty = true; Rebuild(); });
                inspector.Add(cut);
                Btn(inspector, T("movieKeyUpdate", "TO THIS VIEW"), () => { SetPlayhead(key.time); AddKey(); });
                Btn(inspector, T("movieKeyGo", "GO TO IT"), () =>
                {
                    cam.transform.SetPositionAndRotation(OrbitScale.ToUnity(key.position), key.rotation);
                    PhotoSettings.Fov = key.fov;
                    tools.RestartFree();
                });
            }
        }
    }
}
