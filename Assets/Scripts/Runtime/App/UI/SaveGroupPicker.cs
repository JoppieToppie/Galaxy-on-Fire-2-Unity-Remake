using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    /// <summary>A compact save-group popup. Its overlay never reparents or resizes the underlying menu.</summary>
    public class SaveGroupPicker : VisualElement
    {
        readonly Button selector;
        readonly Action changed;
        VisualElement popup, overlayRoot;
        public static SaveGroupPicker Create(Action changed) => new SaveGroupPicker(changed);

        SaveGroupPicker(Action changed)
        {
            this.changed = changed;
            name = "saveGroupPicker";
            AddToClassList("save-group-control");
            var label = new Label(Localization.Extra("saveMode", "Save group"));
            label.AddToClassList("save-group-caption");
            Add(label);
            selector = new Button(Toggle) { name = "saveGroupSelector" };
            selector.AddToClassList("save-group-selector");
            Add(selector);
            Refresh();
            RegisterCallback<DetachFromPanelEvent>(_ => Close(false));
        }

        static string NameOf(GameMode mode) => mode == GameMode.Scavenger ? ScavengerMode.Name : Localization.Extra("standardMode", "Original Game");
        void Refresh() => selector.text = NameOf(SaveGame.SelectedMode) + "   \u25BE";
        void Toggle() { if (popup != null) Close(true); else Open(); }

        void Open()
        {
            overlayRoot = panel.visualTree;
            popup = new VisualElement { name = "saveGroupPopup" };
            popup.AddToClassList("save-group-popup");
            for (VisualElement ancestor = this; ancestor != null; ancestor = ancestor.parent)
                for (int i = 0; i < ancestor.styleSheets.count; i++) popup.styleSheets.Add(ancestor.styleSheets[i]);
            popup.style.unityFontDefinition = selector.resolvedStyle.unityFontDefinition;
            var bounds = selector.worldBound;
            var position = overlayRoot.WorldToLocal(new Vector2(bounds.xMin, bounds.yMax));
            popup.style.position = Position.Absolute;
            popup.style.left = position.x;
            popup.style.top = position.y + 4;
            popup.style.width = bounds.width;
            var modes = new List<GameMode>((GameMode[])Enum.GetValues(typeof(GameMode)));
            modes.Sort((a, b) =>
            {
                if (a == b) return 0;
                if (a == GameMode.Standard) return -1;
                if (b == GameMode.Standard) return 1;
                int order = StringComparer.CurrentCultureIgnoreCase.Compare(NameOf(a), NameOf(b));
                return order != 0 ? order : a.CompareTo(b);
            });
            bool firstCustom = true;
            foreach (GameMode mode in modes)
            {
                if (mode != GameMode.Standard && firstCustom)
                {
                    firstCustom = false;
                    var separator = new VisualElement { pickingMode = PickingMode.Ignore };
                    separator.AddToClassList("save-group-separator");
                    popup.Add(separator);
                }
                var selected = mode;
                var option = new Button(() =>
                {
                    bool different = SaveGame.SelectedMode != selected;
                    SaveGame.SelectedMode = selected;
                    Refresh();
                    Close(true);
                    if (different) changed();
                }) { text = NameOf(mode) };
                option.AddToClassList("save-group-option");
                option.EnableInClassList("save-group-option--selected", mode == SaveGame.SelectedMode);
                popup.Add(option);
            }
            overlayRoot.Add(popup);
            overlayRoot.RegisterCallback<PointerDownEvent>(Outside, TrickleDown.TrickleDown);
            overlayRoot.RegisterCallback<NavigationCancelEvent>(Cancel, TrickleDown.TrickleDown);
            popup.ElementAt(0).Focus();
        }

        void Outside(PointerDownEvent e)
        {
            if (e.target is VisualElement target && !Contains(target) && popup != null && !popup.Contains(target)) Close(false);
        }
        void Cancel(NavigationCancelEvent e) { Close(true); e.StopImmediatePropagation(); e.PreventDefault(); }
        void Close(bool focus)
        {
            if (overlayRoot != null)
            {
                overlayRoot.UnregisterCallback<PointerDownEvent>(Outside, TrickleDown.TrickleDown);
                overlayRoot.UnregisterCallback<NavigationCancelEvent>(Cancel, TrickleDown.TrickleDown);
            }
            popup?.RemoveFromHierarchy();
            popup = null;
            overlayRoot = null;
            if (focus && panel != null) selector.Focus();
        }
    }
}
