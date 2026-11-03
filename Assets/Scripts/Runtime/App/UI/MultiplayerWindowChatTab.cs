// MultiplayerWindowChatTab.cs
// MultiplayerWindow: the Chat tab (part of the partial MultiplayerWindow; its fields are in MultiplayerWindow.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class MultiplayerWindow
    {
        /// <summary>The chat pane: the lines (the ones NetChat still has, then each new one), the channel, the line, Send.</summary>
        void BuildChatPane()
        {
            chatPane = new VisualElement();
            chatPane.style.flexGrow = 1;
            chatPane.style.marginTop = 10;
            chatPane.style.display = DisplayStyle.None;
            chatScroll = new ScrollView(ScrollViewMode.Vertical);
            chatScroll.AddManipulator(new DragScroll(chatScroll));
            chatScroll.style.flexGrow = 1;
            chatScroll.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            chatScroll.style.paddingLeft = chatScroll.style.paddingRight = 10;
            chatScroll.style.paddingTop = chatScroll.style.paddingBottom = 6;
            chatPane.Add(chatScroll);
            foreach (var m in NetChat.Messages) AddChatLine(m);

            var hint = Text(Localization.Extra("mpChatWindowHint", "Enter send · /help commands · /w <pilot> whisper"), 13, Dim);
            hint.style.marginTop = 6;
            chatPane.Add(hint);

            var row = Row();
            row.style.flexWrap = Wrap.NoWrap;
            row.style.marginTop = 6;
            chatChannel = Btn("", ToggleChannel, null);
            chatChannel.style.marginLeft = 0;
            chatChannel.style.minWidth = 100;
            row.Add(chatChannel);
            chatField = new TextField { maxLength = NetChat.MaxCommandLength };
            chatField.style.flexGrow = 1;
            chatField.style.flexShrink = 1;
            chatField.style.marginLeft = 6;
            chatField.textEdition.placeholder = Localization.Extra("mpChatWindowPlaceholder", "Type a message");
            chatField.selectAllOnFocus = false;   // refocused after a send, the next line's letters showed selected
            chatField.selectAllOnMouseUp = false;
            // Enter sends (the TextField would take it as its own submit and drop the focus); Tab switches the channel
            // unless a command is being typed.
            chatField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.character == '\n')
                {
                    if (e.keyCode != KeyCode.None) SendChat();
                    e.StopPropagation();
                    chatField.focusController?.IgnoreEvent(e);
                }
                else if (e.keyCode == KeyCode.Tab || e.character == '\t')
                {
                    if (e.keyCode == KeyCode.Tab && !chatField.value.StartsWith("/")) ToggleChannel();
                    e.StopPropagation();
                    chatField.focusController?.IgnoreEvent(e);
                }
            }, TrickleDown.TrickleDown);
            chatField.RegisterCallback<NavigationSubmitEvent>(e => { e.StopPropagation(); chatField.focusController?.IgnoreEvent(e); }, TrickleDown.TrickleDown);
            chatField.RegisterCallback<NavigationMoveEvent>(e => { e.StopPropagation(); chatField.focusController?.IgnoreEvent(e); }, TrickleDown.TrickleDown);
            TextFieldKeys.Guard(chatField);   // Esc drops the focus (the next Esc closes the window)
            row.Add(chatField);
            var send = Btn(Localization.Extra("mpChatSend", "Send"), SendChat, "squad-button--accept");
            row.Add(send);
            // Phones: the game's own keyboard (Android's key repeat: a held Backspace), Done sends and keeps typing; the
            // channel and Send buttons keep it up.
            PhoneKeyboard.Attach(chatField, () => { bool line = !string.IsNullOrWhiteSpace(chatField.value); SendChat(); return line; }, chatChannel, send);
            chatPane.Add(row);
            RefreshChannel();
        }
        void ShowPane()
        {
            bool chat = tab == Tab.Chat;
            if (scroll != null) scroll.style.display = chat ? DisplayStyle.None : DisplayStyle.Flex;
            if (status != null) status.style.display = chat ? DisplayStyle.None : DisplayStyle.Flex;
            if (chatPane == null) return;
            chatPane.style.display = chat ? DisplayStyle.Flex : DisplayStyle.None;
            if (!chat) { if (chatField?.focusController?.focusedElement == chatField) chatField.Blur(); return; }
            unreadChat = false;
            ScrollChatDown();
            // Into the line only with keys and mouse: a controller's D-pad stayed trapped in it, a phone's keyboard covered the window.
            if (InputMode.Current == InputKind.KeyboardMouse)
                chatField.schedule.Execute(() => { if (isOpen && tab == Tab.Chat) chatField.Focus(); }).ExecuteLater(50);
        }
        void AddChatLine(NetChat.Message m)
        {
            if (chatScroll == null || m == null) return;
            var l = new Label(ChatView.Format(m)) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = Mathf.Round(UiLayout.Type(18) * scale);   // running text: the small step, not the caption
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            l.style.color = m.channel == NetChat.Channel.Notice ? Dim : m.own ? new Color(1f, 1f, 1f, 0.8f) : Color.white;
            // Follow the new line only while reading the end (scrolled up to read older lines, the view stays put).
            bool atEnd = chatScroll.scrollOffset.y >= ChatMaxOffset - 24f;
            chatScroll.contentContainer.Add(l);
            while (chatScroll.contentContainer.childCount > ChatKeep) chatScroll.contentContainer.RemoveAt(0);
            if (atEnd) ScrollChatDown();
        }
        float ChatMaxOffset => chatScroll == null ? 0f
            : Mathf.Max(0f, chatScroll.contentContainer.layout.height - chatScroll.contentViewport.layout.height);
        void ScrollChatDown() =>
            chatScroll?.schedule.Execute(() => chatScroll.scrollOffset = new Vector2(0f, ChatMaxOffset)).ExecuteLater(1);
        void SendChat()
        {
            string line = chatField.value;
            PhoneKeyboard.SetText(chatField, "");   // a phone's keyboard emptied too
            if (!string.IsNullOrWhiteSpace(line)) NetChat.Send(line);
            chatField.schedule.Execute(() => chatField.Focus()).ExecuteLater(1);   // keep typing
        }
        void ToggleChannel()
        {
            NetChat.NextChannel();
            RefreshChannel();
        }
        void RefreshChannel()
        {
            if (chatChannel == null) return;
            var c = NetChat.SendingNow;
            chatChannel.text = NetChat.ChannelName(c).UiCaps();
            chatChannel.style.color = c == NetChat.Channel.Global ? new Color(0.94f, 0.7f, 0.35f)
                                    : c == NetChat.Channel.Faction ? new Color(0.56f, 0.89f, 0.56f)
                                    : c == NetChat.Channel.Squad ? new Color(1f, 0.62f, 0.8f) : Accent;
        }
        /// <summary>The Admin tab of a session without profiles (hosted fresh from the menu): kick, mute and session admins
        /// (NetCommands); bans, ops and the profile list need profiles, which the tab says.</summary>
        void BuildAdminFresh(NetPanel.State s, bool master)
        {
            body.Add(Text(Localization.Extra("mpPanelFreshAdmin", "A fresh world keeps nothing: bans, ops and factions need World: Persistent. Kicks and mutes work."), 14, Dim));
            var opts = Row();
            opts.Add(Field(Localization.Extra("mpPanelReason", "Reason"), reasonText, 80, 320, v => reasonText = v));
            opts.Add(Field(Localization.Extra("mpPanelMinutes", "Minutes"), minutesText, 6, 110, v => minutesText = v));
            body.Add(opts);
            body.Add(Text(Localization.Extra("mpPanelFreshHint", "Mute: for the minutes (0 = the whole session)."), 14, Dim));
            Section(Localization.Extra("mpPanelPilotsOnline", "Pilots online"));
            foreach (var p in s.pilots)
            {
                var row = Line((p.tag.Length > 0 ? $"[{p.tag}] " : "") + p.name + RoleLabel(p.role) + (p.self ? "  (" + Localization.Extra("mpPanelYou", "you") + ")" : ""), p.self ? Dim : Color.white);
                if (!p.self && s.role > p.role)
                {
                    string target = "#" + p.client;
                    row.Add(Btn(Localization.Extra("mpPanelKick", "Kick"), () => Send($"/kick {target} {reasonText.Trim()}"), "squad-button--leave"));
                    row.Add(Btn(Localization.Extra("mpPanelMute", "Mute"), () =>
                    {
                        int m = int.TryParse(minutesText.Trim(), out int v) && v > 0 ? v : 0;
                        Send(m > 0 ? $"/mute {target} {m}" : $"/mute {target}");
                    }, null));
                    row.Add(Btn(Localization.Extra("mpPanelUnmute", "Unmute"), () => Send($"/unmute {target}"), null));
                    if (master && p.role < NetModeration.Admin) row.Add(Btn(Localization.Extra("mpPanelMakeAdmin", "Make admin"), () => Send($"/admin {target}"), "squad-button--accept"));
                    if (master && p.role == NetModeration.Admin) row.Add(Btn(Localization.Extra("mpPanelUnadmin", "Remove admin"), () => Send($"/unadmin {target}"), null));
                }
                body.Add(row);
            }
        }
        static string SelfName(NetPanel.State s) => s.pilots.Find(p => p.self)?.name ?? "";
        static string StationName(int station) => NetGame.Db.Stations.Find(x => x.index == station)?.name ?? station.ToString();
        void Section(string title) => body.Add(Heading(title));
        /// <summary>A section's title (Section adds it to the tab; the Trade tab's columns take their own).</summary>
        static Label Heading(string title)
        {
            var l = Text(title.UiCaps(), 15, Accent);
            l.style.marginTop = 16;
            l.style.marginBottom = 4;
            l.style.letterSpacing = 1;
            l.style.borderBottomWidth = 1;
            l.style.borderBottomColor = new Color(Accent.r, Accent.g, Accent.b, 0.3f);
            return l;
        }
        static VisualElement Row()
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.alignItems = Align.Center;
            r.style.flexWrap = Wrap.Wrap;
            return r;
        }
        /// <summary>A line of text with room for buttons after it.</summary>
        static VisualElement Line(string text, Color colour)
        {
            var r = Row();
            r.style.marginBottom = 4;
            var l = Text(text, 16, colour);
            l.style.flexGrow = 1;
            l.style.flexShrink = 1;
            r.Add(l);
            return r;
        }
        static Label Text(string text, int size, Color colour)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            // Rework (docs/UI-DESIGN.md "Type scale"): nothing under the small-text step (18) but the build line; the window's
            // 14 / 15 / 16 descriptions were the caption's 16, small for their room.
            l.style.fontSize = Mathf.Round(UiLayout.Type(size <= 12 ? size : Mathf.Max(size, 18)) * scale);
            l.style.color = colour;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }
        static Button Btn(string text, Action onClick, string cls)
        {
            var b = new Button(onClick) { text = text.UiCaps() };
            b.AddToClassList("squad-button");
            if (cls != null) b.AddToClassList(cls);
            b.style.marginLeft = 6;
            b.style.marginTop = 2; b.style.marginBottom = 2;
            return b;
        }
        static TextField Field(string label, string value, int max, int width, Action<string> changed,
                                TouchScreenKeyboardType keyboard = TouchScreenKeyboardType.Default)
        {
            var f = new TextField { value = value, maxLength = max, keyboardType = keyboard };
            f.AddToClassList("gof-field");   // the shared field box (GoF2Common.uss): without it the values read as labels
            f.textEdition.placeholder = label;
            TextFieldKeys.Guard(f);   // typed keys stay in the field (no menu navigation, no game keys)
            PhoneKeyboard.Attach(f);  // phones: the game's own keyboard (a held Backspace repeats)
            f.style.width = Mathf.Round(width * scale);
            f.style.marginLeft = 6;
            f.RegisterValueChangedCallback(e => changed(e.newValue));
            return f;
        }
    }
}
