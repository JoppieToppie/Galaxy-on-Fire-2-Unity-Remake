// ModLoadingView.cs
// Remake mods: the main menu's loading screen while the mods that are on load (Modding.ModLoading): "Loading mods", a
// progress bar and what is loading now. Built in code inside a given element (the startup splash, the fade before a
// game scene); styles .mod-loading* in MainMenu.uss. Plain class: the menu calls Update each frame while it shows.

using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class ModLoadingView
    {
        readonly VisualElement root, fill;
        readonly Label title, line;

        public ModLoadingView(VisualElement parent)
        {
            root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList("mod-loading");
            title = new Label(Data.Localization.Extra("modsLoading", "Loading mods").UiCaps()) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("mod-loading__title");
            title.AddToClassList("gof-semibold");
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.AddToClassList("mod-loading__track");
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("mod-loading__fill");
            track.Add(fill);
            line = new Label { pickingMode = PickingMode.Ignore };
            line.AddToClassList("mod-loading__line");
            root.Add(title);
            root.Add(track);
            root.Add(line);
            parent.Add(root);
            Show(false);
        }

        public bool Shown => root.ClassListContains("mod-loading--shown");

        public void Show(bool on)
        {
            root.EnableInClassList("mod-loading--shown", on);
            if (on) Update();
        }

        public void Update()
        {
            float p = Mathf.Clamp01(Modding.ModLoading.Progress);
            fill.style.width = Length.Percent(p * 100f);
            line.text = Modding.ModLoading.Line;
        }
    }
}
