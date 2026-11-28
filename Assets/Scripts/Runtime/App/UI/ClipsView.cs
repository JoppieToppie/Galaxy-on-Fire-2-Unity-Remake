// ClipsView.cs
// Remake-only: the screen recorder's clips manager (docs/notes/platforms.md "Screen recorder"), on ScreenRecorder's panel over
// every scene; opened by the pause menu's "Recorded clips", Options > Graphics "Recorded clips" and Movie mode. A list
// of the clips (ClipLibrary: thumbnail, name, length, size, date; newest first) and, for the one selected: Play (the
// system's player), Show in folder (desktop) or Share and Save to gallery (Android, ClipProvider), Rename and Delete (a
// second press within 4 s: the file goes for good). Above the list the recording settings (the Options rows, OptionControl)
// and where the clips go. It reads the devices itself while open and the menus under it rest (GameControls.ModalOpen):
// up / down pick, Enter / A play, F2 rename, Delete / X delete, Esc / B close. Plain class.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class ClipsView
    {
        const float ArmSeconds = 4f;
        static readonly string[] SettingIds = { "recordResolution", "recordFrameRate", "recordQuality", "recordSound", "recordInterface", "recordIndicator" };

        readonly VisualElement root, backdrop, list, actions, settings;
        readonly Label where, status;
        readonly List<ClipLibrary.Clip> clips = new List<ClipLibrary.Clip>();
        readonly List<VisualElement> rows = new List<VisualElement>();
        readonly List<Texture2D> thumbs = new List<Texture2D>();
        readonly List<OptionControl> settingRows = new List<OptionControl>();
        int selected;
        float deleteArmedUntil = -1f, statusUntil;
        TextField renaming;

        public bool IsOpen { get; private set; }

        static string T(string key, string english) => Localization.Extra(key, english);

        public ClipsView(VisualElement panelRoot)
        {
            root = panelRoot;
            backdrop = new VisualElement();
            var b = backdrop.style;
            b.position = Position.Absolute;
            b.left = b.top = b.right = b.bottom = 0;
            b.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
            b.alignItems = Align.Center;
            b.justifyContent = Justify.Center;
            b.display = DisplayStyle.None;
            var window = new VisualElement();
            var w = window.style;
            w.width = Length.Percent(92); w.maxWidth = 1500;
            w.height = Length.Percent(90);
            w.backgroundColor = new Color(0.016f, 0.04f, 0.07f, 0.97f);
            w.borderTopWidth = w.borderBottomWidth = w.borderLeftWidth = w.borderRightWidth = 1;
            w.borderTopColor = w.borderBottomColor = w.borderLeftColor = w.borderRightColor = new Color(0.24f, 0.78f, 1f, 0.35f);
            w.paddingLeft = w.paddingRight = 24; w.paddingTop = 18; w.paddingBottom = 16;
            backdrop.Add(window);

            var title = Text(window, T("clipsTitle", "RECORDED CLIPS"), 30, Color.white, true);
            title.style.letterSpacing = 4;
            where = Text(window, "", 15, new Color(0.85f, 0.93f, 0.97f, 0.7f));
            settings = new VisualElement();
            settings.style.flexDirection = FlexDirection.Row;
            settings.style.flexWrap = Wrap.Wrap;
            settings.style.marginTop = 8;
            settings.style.marginBottom = 8;
            window.Add(settings);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.backgroundColor = new Color(0f, 0f, 0f, 0.3f);
            list = scroll.contentContainer;
            window.Add(scroll);
            actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.alignItems = Align.Center;
            actions.style.marginTop = 10;
            window.Add(actions);
            status = Text(window, "", 16, new Color(1f, 0.65f, 0.19f));
            status.style.marginTop = 4;
            root.Add(backdrop);
        }

        static Label Text(VisualElement into, string text, float size, Color color, bool bold = false)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = UiLayout.Type(size);
            l.style.color = color;
            l.style.whiteSpace = WhiteSpace.Normal;
            if (bold) l.AddToClassList("gof-semibold");
            into.Add(l);
            return l;
        }

        Button Btn(VisualElement into, string text, Action click, bool primary = false)
        {
            var b = new Button(() => { UiSounds.Confirm(); click(); }) { text = text.UiCaps(), focusable = false };
            b.AddToClassList("gof-button");
            if (primary) b.AddToClassList("gof-button--primary");
            b.AddToClassList("gof-semibold");
            b.style.marginRight = 10;
            b.style.marginTop = 4;
            into.Add(b);
            return b;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            GameControls.ModalOpen = true;
            root.pickingMode = PickingMode.Position;
            backdrop.style.display = DisplayStyle.Flex;
            backdrop.BringToFront();
            BuildSettings();
            selected = 0;
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameControls.ModalOpen = false;
            root.pickingMode = PickingMode.Ignore;
            backdrop.style.display = DisplayStyle.None;
            renaming = null;
            FreeThumbs();
        }

        void FreeThumbs()
        {
            foreach (var t in thumbs) if (t != null) UnityEngine.Object.Destroy(t);
            thumbs.Clear();
        }

        /// <summary>The recording rows of Options > Graphics, the same definitions (so both stay alike).</summary>
        void BuildSettings()
        {
            settings.Clear();
            settingRows.Clear();
            var all = OptionsCatalog.All();
            foreach (var id in SettingIds)
            {
                var def = all.Find(d => d.id == id);
                if (def == null) continue;
                var c = new OptionControl(def);
                c.Root.style.width = Length.Percent(49);
                c.Root.style.marginRight = Length.Percent(1);
                settingRows.Add(c);
                settings.Add(c.Root);
            }
        }

        public void Refresh()
        {
            if (!IsOpen) return;
            FreeThumbs();
            clips.Clear();
            clips.AddRange(ClipLibrary.List());
            clips.RemoveAll(c => c.path == ScreenRecorder.CurrentPath);   // (the one being recorded isn't a clip yet)
            bool keys = !Application.isMobilePlatform;   // (a phone has no F9: the pause menu's entry)
            where.text = string.Format(keys ? T("clipsWhere", "Clips go to {0}. F9 starts and stops a recording (the pause menu too).")
                                            : T("clipsWhereMobile", "Clips go to {0}. The pause menu starts and stops a recording."),
                                       ClipLibrary.Folder.Replace('\\', '/'));
            list.Clear();
            rows.Clear();
            if (clips.Count == 0)
                Text(list, keys ? T("clipsNone", "No clips yet. Press F9 in the game, or Start recording in the pause menu.")
                                : T("clipsNoneMobile", "No clips yet. Start recording in the pause menu."),
                     18, new Color(0.85f, 0.93f, 0.97f, 0.8f)).style.marginTop = 16;
            for (int i = 0; i < clips.Count; i++) rows.Add(Row(i));
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, clips.Count - 1));
            Select(selected, false);
        }

        VisualElement Row(int i)
        {
            var clip = clips[i];
            var row = new VisualElement();
            var s = row.style;
            s.flexDirection = FlexDirection.Row;
            s.alignItems = Align.Center;
            s.paddingLeft = s.paddingRight = 8; s.paddingTop = s.paddingBottom = 6;
            s.borderBottomWidth = 1;
            s.borderBottomColor = new Color(0.24f, 0.78f, 1f, 0.12f);
            var image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.style.width = 192; image.style.height = 108;
            image.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            image.style.marginRight = 16;
            if (clip.thumbnail != null)
            {
                try
                {
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(System.IO.File.ReadAllBytes(clip.thumbnail))) { thumbs.Add(tex); image.style.backgroundImage = new StyleBackground(tex); }
                    else UnityEngine.Object.Destroy(tex);
                }
                catch (Exception) { }
            }
            row.Add(image);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            Text(text, clip.name, 20, Color.white, true);
            var facts = new List<string>();
            string length = ClipLibrary.Duration(clip.seconds);
            if (length.Length > 0) facts.Add(length);
            facts.Add(ClipLibrary.Bytes(clip.bytes));
            facts.Add(clip.when.ToString("g"));
            Text(text, string.Join("  ·  ", facts), 16, new Color(0.85f, 0.93f, 0.97f, 0.7f));
            row.Add(text);
            row.RegisterCallback<ClickEvent>(e =>
            {
                if (e.clickCount >= 2) { Select(i, true); Play(); }
                else { UiSounds.Move(); Select(i, true); }
            });
            list.Add(row);
            return row;
        }

        void Select(int i, bool scroll)
        {
            if (renaming != null && i != selected) renaming = null;
            selected = i;
            for (int r = 0; r < rows.Count; r++)
                rows[r].style.backgroundColor = r == selected ? new Color(0.24f, 0.78f, 1f, 0.18f) : new Color(0f, 0f, 0f, 0f);
            if (scroll && selected < rows.Count) (list.parent?.parent as ScrollView)?.ScrollTo(rows[selected]);
            deleteArmedUntil = -1f;
            BuildActions();
        }

        ClipLibrary.Clip Current => selected >= 0 && selected < clips.Count ? clips[selected] : null;

        void BuildActions()
        {
            actions.Clear();
            if (Current != null)
            {
                if (renaming != null)
                {
                    actions.Add(renaming);
                    Btn(actions, T("clipsRenameSave", "Save name"), () => CommitRename(renaming.value), true);
                    Btn(actions, T("clipsRenameCancel", "Cancel"), () => { renaming = null; BuildActions(); });
                }
                else
                {
                    Btn(actions, T("clipsPlay", "Play"), Play, true);
#if UNITY_ANDROID && !UNITY_EDITOR
                    Btn(actions, T("clipsShare", "Share"), Share);
                    Btn(actions, T("clipsGallery", "Save to gallery"), SaveToGallery);
#else
                    Btn(actions, T("clipsShowFile", "Show in folder"), () => PhotoMode.ShowInFolder(Current.path));
#endif
                    Btn(actions, T("clipsRename", "Rename"), StartRename);
                    Btn(actions, Time.unscaledTime < deleteArmedUntil ? T("clipsDeleteSure", "Delete for good?") : T("clipsDelete", "Delete"), Delete);
                }
            }
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1;
            actions.Add(spacer);
#if !(UNITY_ANDROID || UNITY_IOS) || UNITY_EDITOR
            Btn(actions, T("clipsOpenFolder", "Open folder"), () =>
            {
                System.IO.Directory.CreateDirectory(ClipLibrary.Folder);
                Application.OpenURL("file:///" + ClipLibrary.Folder.Replace('\\', '/'));
            });
#endif
            Btn(actions, T("hudBack", "BACK"), Close);
        }

        void Say(string text)
        {
            status.text = text ?? "";
            statusUntil = Time.unscaledTime + 5f;
        }

        // ---- the actions ---------------------------------------------------------------------------------------

        void Play()
        {
            var clip = Current;
            if (clip == null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Clip("play", clip.name)) Say(T("clipsNoPlayer", "No app here can play the clip."));
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{clip.path}\""); }
            catch (Exception e) { Say(e.Message); }
#else
            Application.OpenURL("file://" + clip.path);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool Clip(string method, string name)
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var provider = new AndroidJavaClass("com.joppietoppie.gof2remake.ClipProvider");
            return provider.CallStatic<bool>(method, activity, name + ClipLibrary.Extension);
        }

        void Share()
        {
            if (Current != null && !Clip("share", Current.name)) Say(T("clipsNoShare", "The clip couldn't be shared."));
        }

        void SaveToGallery()
        {
            if (Current == null) return;
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var provider = new AndroidJavaClass("com.joppietoppie.gof2remake.ClipProvider");
            string error = provider.CallStatic<string>("saveToGallery", activity, Current.name + ClipLibrary.Extension);
            Say(string.IsNullOrEmpty(error) ? T("clipsInGallery", "Saved to the gallery (Movies/Galaxy on Fire 2).")
                                           : string.Format(T("clipsGalleryFailed", "Not saved to the gallery: {0}"), error));
        }
#endif

        void StartRename()
        {
            if (Current == null) return;
            renaming = new TextField { value = Current.name };
            renaming.AddToClassList("gof-field");
            renaming.style.width = 420;
            renaming.style.marginRight = 10;
            BuildActions();
            renaming.Focus();
        }

        void CommitRename(string name)
        {
            var clip = Current;
            if (clip == null) return;
            string error = ClipLibrary.Rename(clip, name);
            if (error != null) { Say(error); return; }
            renaming = null;
            Refresh();
        }

        void Delete()
        {
            var clip = Current;
            if (clip == null) return;
            if (Time.unscaledTime >= deleteArmedUntil)
            {
                deleteArmedUntil = Time.unscaledTime + ArmSeconds;
                Say(T("clipsDeleteAsk", "Press Delete again to delete the clip for good."));
                BuildActions();
                return;
            }
            if (!ClipLibrary.Delete(clip)) { Say(T("clipsDeleteFailed", "The clip couldn't be deleted (is it open in a player?).")); return; }
            Say(string.Format(T("clipsDeleted", "{0} deleted."), clip.name));
            Refresh();
        }

        // ---- every frame ---------------------------------------------------------------------------------------

        public void Tick()
        {
            if (!IsOpen) return;
            if (statusUntil > 0f && Time.unscaledTime > statusUntil) { status.text = ""; statusUntil = 0f; }
            if (deleteArmedUntil > 0f && Time.unscaledTime > deleteArmedUntil) { deleteArmedUntil = -1f; BuildActions(); }
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if (renaming != null && renaming.focusController?.focusedElement != null)
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame) { renaming = null; BuildActions(); }
                else if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) CommitRename(renaming.value);
                return;
            }
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) { UiSounds.Back(); Close(); return; }
            int move = 0;
            if ((kb != null && kb.downArrowKey.wasPressedThisFrame) || (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame))) move = 1;
            if ((kb != null && kb.upArrowKey.wasPressedThisFrame) || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame))) move = -1;
            if (move != 0 && clips.Count > 0) { UiSounds.Move(); Select(Mathf.Clamp(selected + move, 0, clips.Count - 1), true); }
            if ((kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame)) { UiSounds.Confirm(); Play(); }
            if (kb != null && kb.f2Key.wasPressedThisFrame) StartRename();
            if ((kb != null && kb.deleteKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame)) Delete();
        }
    }
}
