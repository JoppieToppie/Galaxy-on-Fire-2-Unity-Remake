// MainMenuNewGame.cs
// Part of MainMenu: the new game flow: the campaign cards and the mods' campaigns, difficulty, economy, the game options
// (hardcore, New Game+, Kaamo Club, tutorials, capital ships, the mods' option cards) and BeginGame.

using System;
using System.Collections;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        /// <summary>A mod's card, like the campaign cards (290 x 448 art, the hover art fading in while selected, scaled with
        /// them): its art cropped to the card, and unless the art has its own title, the name on a plate at the foot.</summary>
        static Button ModCard(Texture2D art, Texture2D hover, bool showTitle, string title, string subtitle)
        {
            var b = new Button();
            b.AddToClassList("campaign-card");
            b.AddToClassList("mod-card");
            var a = new VisualElement { pickingMode = PickingMode.Ignore };
            a.AddToClassList("card-art");
            a.AddToClassList("mod-card__art");
            if (art != null) a.style.backgroundImage = new StyleBackground(art);
            b.Add(a);
            if (hover != null)
            {
                var h = new VisualElement { pickingMode = PickingMode.Ignore };
                h.AddToClassList("card-art");
                h.AddToClassList("card-art-hover");
                h.AddToClassList("mod-card__art");
                h.style.backgroundImage = new StyleBackground(hover);
                b.Add(h);
            }
            if (showTitle || art == null)
            {
                var plate = new VisualElement { pickingMode = PickingMode.Ignore };
                plate.AddToClassList("mod-card__plate");
                var t = new Label(title.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                t.AddToClassList("mod-card__title");
                t.AddToClassList("gof-semibold");
                plate.Add(t);
                if (!string.IsNullOrEmpty(subtitle))
                {
                    var st = new Label(subtitle) { pickingMode = PickingMode.Ignore };
                    st.AddToClassList("mod-card__sub");
                    plate.Add(st);
                }
                b.Add(plate);
            }
            return b;
        }

        /// <summary>The mods' campaigns (Modding.ModCampaigns) as cards after the three campaign cards, picked the same way;
        /// built anew each time the panel opens (the mods change in the menu). Four or five cards shrink to fit the row, more
        /// scroll sideways.</summary>
        void RefreshModCampaigns()
        {
            var panel = panels["campaignPanel"];
            var row = panel.Q(className: "card-row");
            if (row == null) return;
            if (campaignCardScroll == null || !panel.Contains(campaignCardScroll))   // made again after a UI reload
            {
                // The row goes into a sideways scroll view (it only scrolls with more than five cards).
                campaignCardScroll = new ScrollView(ScrollViewMode.Horizontal);
                campaignCardScroll.AddToClassList("card-scroll");
                campaignCardScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                campaignCardScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                row.parent.Insert(row.parent.IndexOf(row), campaignCardScroll);
                campaignCardScroll.Add(row);
                new DragScroll(campaignCardScroll);   // a drag scrolls the cards sideways
            }
            row.Query(className: "mod-card").ForEach(e => e.RemoveFromHierarchy());
            // The mods' DLCs first, right after Supernova, in their play order (ModDlcs: the Mods browser's DLC place).
            var dlcs = Modding.ModDlcs.All();
            for (int k = 0; k < dlcs.Count; k++)
            {
                var def = dlcs[k];
                var b = ModCard(Modding.ModDlcs.Image(def), Modding.ModDlcs.ImageHover(def), def.showTitle, def.Name,
                                string.Format(Localization.Extra("modDlcCard", "DLC {0}  ·  a mod: {1}"), k + 1, def.mod.Name));
                b.AddToClassList("dlc-card");
                b.tooltip = def.Description;
                b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
                b.clicked += () => { Play(buttonRelease); PickDlc(def); };
                b.RegisterCallback<FocusInEvent>(_ => campaignCardScroll.ScrollTo(b));
                HookFocusSound(b);
                row.Add(b);
            }
            var all = Modding.ModCampaigns.All();
            foreach (var c in all)
            {
                var def = c;
                var b = ModCard(Modding.ModCampaigns.Image(def), Modding.ModCampaigns.ImageHover(def), def.showTitle, def.Name,
                                string.Format(Localization.Extra("modCampaignBy", "a mod: {0}"), def.mod.Name));
                b.tooltip = def.Description;
                b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
                b.clicked += () => { Play(buttonRelease); PickModCampaign(def); };
                b.RegisterCallback<FocusInEvent>(_ => campaignCardScroll.ScrollTo(b));
                HookFocusSound(b);
                row.Add(b);
            }
            int n = 3 + dlcs.Count + all.Count;
            row.EnableInClassList("card-row--4", n == 4);
            row.EnableInClassList("card-row--many", n >= 5);
        }

        /// <summary>Remake mods: the mods' new-game options (Modding.ModGameOptions) as cards like the campaign cards, in a
        /// column beside the game options' toggles (the panel widens for them); a card toggles its option for this new game
        /// (remembered for the next one).</summary>
        void RefreshModOptions()
        {
            var panel = panels["gameOptionsPanel"];
            if (modOptionCards == null || !panel.Contains(modOptionCards))   // made again after a UI reload
            {
                // The toggles into a left column, the cards in a right one that scrolls sideways; Start under both.
                var body = new VisualElement();
                body.AddToClassList("game-options-body");
                var left = new VisualElement();
                left.AddToClassList("game-options-left");
                var anchor = panel.Q("gameOptionsStart");
                anchor.parent.Insert(anchor.parent.IndexOf(anchor), body);
                foreach (var name in new[] { "kaamoToggle", "hardcoreToggle", "tutorialToggle", "capitalToggle" })
                    if (panel.Q(name) is VisualElement e) left.Add(e);
                body.Add(left);
                var right = new VisualElement();
                right.AddToClassList("mod-option-column");
                modOptionCards = new ScrollView(ScrollViewMode.Horizontal);
                modOptionCards.AddToClassList("mod-option-cards");
                modOptionCards.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                modOptionCards.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                modOptionCards.mouseWheelScrollSize = 120f;
                right.Add(modOptionCards);
                modOptionHint = new Label { pickingMode = PickingMode.Ignore };
                modOptionHint.AddToClassList("mod-option-hint");
                right.Add(modOptionHint);
                body.Add(right);
                new DragScroll(modOptionCards);   // a drag (mouse or finger) scrolls the cards sideways
            }
            modOptionCards.Clear();
            var all = Modding.ModGameOptions.Cards();
            modOptionCards.parent.style.display = all.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            panel.EnableInClassList("panel--wide", all.Count > 0);
            // Two cards show at a time; more scroll sideways (a drag, the wheel, or the focus moving onto one).
            modOptionHint.style.display = all.Count > 2 ? DisplayStyle.Flex : DisplayStyle.None;
            modOptionHint.text = string.Format(Localization.Extra("modOptionsMore", "{0} options  ·  scroll for more"), all.Count);
            foreach (var o in all)
            {
                var def = o;
                var b = ModCard(Modding.ModGameOptions.Image(def), Modding.ModGameOptions.ImageHover(def), def.showTitle, def.Name,
                                string.Format(Localization.Extra("modCampaignBy", "a mod: {0}"), def.mod.Name));
                b.AddToClassList("mod-option-card");
                b.tooltip = def.Description;
                var badge = new Label { pickingMode = PickingMode.Ignore };
                badge.AddToClassList("mod-option-card__badge");
                badge.AddToClassList("gof-semibold");
                b.Add(badge);
                void Show()
                {
                    bool on = Modding.ModGameOptions.Chosen(def);
                    b.EnableInClassList("mod-option-card--on", on);
                    badge.text = (on ? Localization.Extra("modOptionOn", "On") : Localization.Extra("modOptionOff", "Off")).ToUpperInvariant();
                }
                Show();
                b.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
                b.clicked += () => { Play(buttonRelease); Modding.ModGameOptions.SetChosen(def, !Modding.ModGameOptions.Chosen(def)); Show(); };
                b.RegisterCallback<FocusInEvent>(_ => modOptionCards.ScrollTo(b));
                HookFocusSound(b);
                modOptionCards.Add(b);
            }
        }

        /// <summary>A mod DLC's card: a game at the end of the whole story with the DLC's quest (ModDlcs.Start).</summary>
        void PickDlc(Modding.ModDlcs.Def d)
        {
            pendingDlc = d;
            pendingModCampaign = null;
            pendingCampaign = Campaign.Supernova;
            pendingStartIndex = -1;
            OpenPanel("difficultyPanel");
        }

        void PickModCampaign(Modding.ModCampaigns.Def c)
        {
            pendingDlc = null;
            pendingModCampaign = c;
            pendingCampaign = Campaign.GalaxyOnFire2;
            pendingStartIndex = -1;
            OpenPanel("difficultyPanel");
        }

        void PickCampaign(Campaign c)
        {
            pendingModCampaign = null;
            pendingDlc = null;
            pendingCampaign = c;
            pendingStartIndex = -1;
            OpenPanel("difficultyPanel");
        }

        void PickDifficulty(float difficulty)
        {
            pendingDifficulty = difficulty;
            hardcoreNext = false;   // never carried over from an earlier new game: chosen each time
            RefreshHardcoreToggle();
            OpenPanel("economyPanel");
        }

        /// <summary>The economy picked: then the game options (the Kaamo Club from the start, hardcore) and Start.</summary>
        void PickEconomy(Economy economy)
        {
            pendingEconomy = economy;
            RefreshTutorialToggle();   // the option may have changed in Options meanwhile
            RefreshCapitalToggle();
            RefreshModOptions();   // remake mods: their new-game options
            var desc = root.Q<Label>("gameOptionsStartDesc");
            if (desc != null) desc.text = $"{Session.DifficultyName(pendingDifficulty)}  ·  {Session.EconomyName(economy)}";
            OpenPanel("gameOptionsPanel");
        }

        void RefreshHardcoreToggle()
        {
            var b = root.Q<Button>("hardcoreToggle");
            if (b == null) return;
            b.EnableInClassList("choice-button--on", hardcoreNext);
            var label = root.Q<Label>("hardcoreLabel");
            if (label != null)
                label.text = $"{Localization.Extra("hardcoreTitle", "Hardcore")}: {(hardcoreNext ? Localization.Extra("hardcoreOn", "Permadeath") : Localization.Extra("hardcoreOff", "Off"))}".ToUpperInvariant();
            var desc = root.Q<Label>("hardcoreDesc");
            if (desc != null)
                desc.text = Localization.Extra("hardcoreDesc",
                    "One life: saves only on docking; if your ship is destroyed, this game's saves are deleted.");
        }

        /// <summary>The Supernova Challenge's entry under the campaign cards (the remake's: the original offered it nowhere,
        /// supernova_challenge.md 1): its name (293) and the highscore (294) with the best kills (176), else the
        /// description's first paragraph (298).</summary>
        void RefreshChallenge()
        {
            var label = root.Q<Label>("challengeLabel");
            if (label != null) label.text = Localization.Get(293).ToUpperInvariant();
            var desc = root.Q<Label>("challengeDesc");
            if (desc == null) return;
            int best = SupernovaChallenge.Highscore;
            string intro = Localization.Get(298).Replace("\r\n", "\n");
            int cut = intro.IndexOf("\n\n", System.StringComparison.Ordinal);
            desc.text = best > 0
                ? $"{Localization.Get(294)}: {SupernovaChallenge.Digits(best)}  ·  {Localization.Get(176)}: {SupernovaChallenge.BestKills}"
                : cut > 0 ? intro.Substring(0, cut) : intro;
        }

        void RefreshNgPlus(bool rescan)
        {
            var b = root.Q<Button>("ngPlusToggle");
            if (b == null) return;
            if (rescan) { ngPlusSave = NewGamePlus.FindFinished(out _); newGamePlus = false; }
            b.style.display = ngPlusSave != null ? DisplayStyle.Flex : DisplayStyle.None;
            b.EnableInClassList("choice-button--on", newGamePlus);
            var label = root.Q<Label>("ngPlusLabel");
            if (label != null)
                label.text = $"{Localization.Extra("ngPlus", "New Game+")}: {(newGamePlus ? Localization.Extra("on", "On") : Localization.Extra("off", "Off"))}".ToUpperInvariant();
            var desc = root.Q<Label>("ngPlusDesc");
            if (desc != null && ngPlusSave != null)
                desc.text = string.Format(Localization.Extra("ngPlusDesc",
                    "Start again with your finished game's ({0}, {1}) credits, blueprints, medals and Kaamo Club."),
                    (Campaign)ngPlusSave.campaign switch { Campaign.Valkyrie => "Valkyrie", Campaign.Supernova => "Supernova", _ => "Galaxy on Fire 2" }, ItemInfo.Credits(ngPlusSave.credits));
        }

        void RefreshKaamoToggle()
        {
            var b = root.Q<Button>("kaamoToggle");
            if (b == null) return;
            bool on = KaamoFromStart;
            b.EnableInClassList("choice-button--on", on);
            var label = root.Q<Label>("kaamoLabel");
            if (label != null)
                label.text = $"{Localization.Get(78)}: {(on ? Localization.Extra("kaamoOwned", "Owned from the start") : Localization.Extra("kaamoNotOwned", "Win it in the game"))}".ToUpperInvariant();
            var desc = root.Q<Label>("kaamoDesc");
            if (desc != null)
                desc.text = Localization.Extra("kaamoDesc",
                    "Own the Kaamo Club in Shima from the start: no siege, no 30 million.");
        }

        /// <summary>Remake: the tutorial popups (Settings.TutorialHints, also in Options > Gameplay) as a game option before the
        /// start (it was a question after the economy).</summary>
        void RefreshTutorialToggle()
        {
            var b = root.Q<Button>("tutorialToggle");
            if (b == null) return;
            bool on = Settings.TutorialHints;
            b.EnableInClassList("choice-button--on", on);
            var label = root.Q<Label>("tutorialLabel");
            if (label != null)
                label.text = $"{Localization.Extra("tutorialTitle", "Tutorials")}: {(on ? Localization.Extra("tutorialOn", "On") : Localization.Extra("tutorialOff", "Off"))}".ToUpperInvariant();
            var desc = root.Q<Label>("tutorialDesc");
            if (desc != null)
                desc.text = Localization.Extra("tutorialDesc",
                    "Popups explain the controls, hangar, map and missions the first time.");
        }

        /// <summary>Remake (players' suggestion): the capital ship enhancements (Settings.CapitalShips, also in Options >
        /// Gameplay) as a game option before the start.</summary>
        void RefreshCapitalToggle()
        {
            var b = root.Q<Button>("capitalToggle");
            if (b == null) return;
            bool on = Settings.CapitalShips;
            b.EnableInClassList("choice-button--on", on);
            var label = root.Q<Label>("capitalLabel");
            if (label != null)
                label.text = $"{Localization.Extra("capitalTitle", "Capital ships")}: {(on ? Localization.Extra("capitalOn", "Enhanced") : Localization.Extra("capitalOff", "Original"))}".ToUpperInvariant();
            var desc = root.Q<Label>("capitalDesc");
            if (desc != null)
                desc.text = Localization.Extra("capitalDesc",
                    "Capital ships get escorts and stronger turrets; carriers and Vossk battleships can be destroyed and docked at.");
        }

        void BeginGame(Economy economy)
        {
            Session.ResetNewGame();   // Status::resetGame: Phantom at Var Hastra (Mido)
            Session.Hardcore = hardcoreNext;   // remake: permadeath (a new RunId came with ResetNewGame)
            Session.Campaign = pendingCampaign;
            Session.Difficulty = pendingDifficulty;
            Session.Economy = economy;   // before the Database: it loads that economy's tables
            if (KaamoFromStart) Session.KaamoState = 3;   // resetGame with the expansion bought: the club owned, its storage empty
            Modding.ModGameOptions.ApplyChoices();   // remake mods: the options picked in the Game options panel
            var db = Database.Load();
            // Remake mods: a mod's campaign (Modding.ModCampaigns): its start, docked at its station; no GoF2 story.
            // Remake mods: a DLC (Modding.ModDlcs): the end of the whole story, then its quest.
            if (pendingDlc != null)
            {
                string dlcScene = Modding.ModDlcs.Start(db, pendingDlc, out string dlcError);
                if (dlcScene == null) { ShowNotice(pendingDlc.Name, dlcError); return; }
                StartCoroutine(Leave(dlcScene));
                return;
            }
            if (pendingModCampaign != null)
            {
                string modScene = Modding.ModCampaigns.Start(db, pendingModCampaign, out string error);
                if (modScene == null) { ShowNotice(pendingModCampaign.Name, error); return; }
                StartCoroutine(Leave(modScene));
                return;
            }
            // Remake: the mission select starts a new game at the chosen story step (Story.StartAtMission).
            if (pendingStartIndex >= 0) { StartCoroutine(Leave(Story.StartAtMission(db, pendingStartIndex))); return; }
            // MenuTouchWindow::startGOF2 / startValkyrie / startSupernova: the story's first step (Story).
            string scene = Story.StartCampaign(db, pendingCampaign);
            if (newGamePlus && ngPlusSave != null) NewGamePlus.Apply(ngPlusSave);   // remake: New Game+ carries over
            StartCoroutine(Leave(scene));
        }
    }
}
