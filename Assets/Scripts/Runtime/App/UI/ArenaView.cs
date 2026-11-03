// ArenaView.cs
// Remake-only: the flight HUD's arena panel while the player is in a match (NetArenaClient): the mode and the time left
// (or the countdown before the fight), every player's kills (the local player highlighted, the kill limit beside it), the
// last kills, the respawn countdown after being destroyed, and the result at the end. Built in code with inline styles
// (top centre, under the HUD's other panels). It also drives NetArenaClient.Tick (the controls, the respawn, the way
// home).

using System.Text;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class ArenaView : MonoBehaviour
    {
        VisualElement panel;
        Label title, clock, board, feed, banner;
        SpaceLevel level;

        /// <summary>The arena panel on 'parent' (again after a UI reload).</summary>
        public static void Attach(GameObject host, VisualElement parent)
        {
            if (parent == null) return;
            var view = host.GetComponent<ArenaView>();
            if (view == null) view = host.AddComponent<ArenaView>();
            view.Build(parent);
        }

        void Build(VisualElement parent)
        {
            panel?.RemoveFromHierarchy();
            banner?.RemoveFromHierarchy();
            panel = new VisualElement { name = "arena", pickingMode = PickingMode.Ignore };
            var s = panel.style;
            s.position = Position.Absolute;
            s.top = 96; s.left = new Length(50, LengthUnit.Percent); s.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            s.minWidth = 280;
            s.paddingTop = s.paddingBottom = 8; s.paddingLeft = s.paddingRight = 14;
            s.backgroundColor = new Color(0.02f, 0.05f, 0.09f, 0.72f);
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 6;
            s.borderTopWidth = s.borderBottomWidth = s.borderLeftWidth = s.borderRightWidth = 1;
            s.borderTopColor = s.borderBottomColor = s.borderLeftColor = s.borderRightColor = new Color(1f, 0.62f, 0.2f, 0.6f);
            s.display = DisplayStyle.None;
            title = Text(18, new Color(1f, 0.7f, 0.3f), FontStyle.Bold);
            clock = Text(22, Color.white, FontStyle.Bold);
            board = Text(16, new Color(0.9f, 0.93f, 1f), FontStyle.Normal);
            feed = Text(14, new Color(1f, 0.55f, 0.45f), FontStyle.Normal);
            panel.Add(title); panel.Add(clock); panel.Add(board); panel.Add(feed);
            parent.Insert(0, panel);

            banner = Text(40, Color.white, FontStyle.Bold);
            var b = banner.style;
            b.position = Position.Absolute;
            b.top = new Length(38, LengthUnit.Percent); b.left = 0; b.right = 0;
            b.unityTextAlign = TextAnchor.MiddleCenter;
            b.unityTextOutlineColor = Color.black; b.unityTextOutlineWidth = 1.5f;
            b.display = DisplayStyle.None;
            parent.Add(banner);
        }

        static Label Text(int size, Color colour, FontStyle style)
        {
            var l = new Label { pickingMode = PickingMode.Ignore, enableRichText = true };
            l.style.fontSize = UiLayout.Type(size);
            l.style.color = colour;
            l.style.unityFontStyleAndWeight = style;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        void Update()
        {
            if (level == null) level = FindAnyObjectByType<SpaceLevel>();
            NetArenaClient.Tick(level);
            var m = NetArenaClient.Current;
            if (panel == null) return;
            panel.style.display = m != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (m == null) { banner.style.display = DisplayStyle.None; return; }

            title.text = (m.kind == NetArena.Kind.Duel ? Localization.Extra("mpArenaDuel", "DUEL").OgCase() : Localization.Extra("mpArenaFfa", "FREE-FOR-ALL").OgCase())
                         + $"  ·  {string.Format(Localization.Extra("mpArenaFirstTo", "first to {0}"), m.killLimit)}";
            int secs = Mathf.CeilToInt(m.phaseLeft);
            clock.text = m.phase == NetArena.Phase.Fighting ? $"{secs / 60}:{secs % 60:00}" : "";

            var sb = new StringBuilder();
            var order = new int[m.players.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => m.kills[b].CompareTo(m.kills[a]));
            ulong me = NetGame.LocalId;
            foreach (int i in order)
            {
                string line = $"{NetArenaClient.NameOf(m.players[i])}   {m.kills[i]}";
                sb.Append(m.players[i] == me ? $"<color=#ffb347>{line}</color>" : line).Append('\n');
            }
            board.text = sb.ToString().TrimEnd('\n');
            sb.Clear();
            foreach (var f in m.feed) sb.Append(f.text).Append('\n');
            feed.text = sb.ToString().TrimEnd('\n');

            // The big text: the countdown, the respawn, the result.
            string big = null;
            if (m.result != null) big = m.result;
            else if (m.phase == NetArena.Phase.Loading) big = Localization.Extra("mpArenaWaiting", "Waiting for the other pilots...");
            else if (m.phase == NetArena.Phase.Countdown) big = secs > 0 ? secs.ToString() : Localization.Extra("mpArenaFight", "FIGHT!").OgCase();
            else if (level != null && level.Health != null && level.Health.Dead) big = Localization.Extra("mpArenaRespawn", "Respawning...");
            banner.style.display = big != null ? DisplayStyle.Flex : DisplayStyle.None;
            banner.style.fontSize = UiLayout.Type(m.result != null ? 26 : 40);
            if (big != null) banner.text = big;
        }

        void OnDestroy()
        {
            panel?.RemoveFromHierarchy();
            banner?.RemoveFromHierarchy();
        }
    }
}
