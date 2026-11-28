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
                var t = new Label(title.UiCaps()) { pickingMode = PickingMode.Ignore };
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
                var left = new ScrollView(ScrollViewMode.Vertical);
                left.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                new DragScroll(left);
                left.RegisterCallback<FocusInEvent>(ev => { if (ev.target is VisualElement row) left.ScrollTo(row); });
                left.AddToClassList("game-options-left");
                var anchor = panel.Q("gameOptionsStart");
                anchor.parent.Insert(anchor.parent.IndexOf(anchor), body);
                foreach (var name in new[] { "kaamoToggle", "kaamoVipToggle", "kaamoStackingToggle", "medalsToggle", "economyToggle", "blueprintAutocompleteToggle", "cloakBayToggle" })
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
                    badge.text = (on ? Localization.Extra("modOptionOn", "On") : Localization.Extra("modOptionOff", "Off")).UiCaps();
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
            OpenDifficultyForNewGame();
        }

        void PickModCampaign(Modding.ModCampaigns.Def c)
        {
            pendingDlc = null;
            pendingModCampaign = c;
            pendingCampaign = Campaign.GalaxyOnFire2;
            pendingStartIndex = -1;
            OpenDifficultyForNewGame();
        }

        void PickCampaign(Campaign c)
        {
            pendingModCampaign = null;
            pendingDlc = null;
            pendingCampaign = c;
            pendingStartIndex = -1;
            OpenDifficultyForNewGame();
        }

        bool kaamoVipNext, tutorialsNext, autocompleteNext, cloakBayNext;
        int kaamoStackLimitNext = 1;
        SavedGameRules remakeRulesNext;

        void RefreshKaamoRules()
        {
            root.Q<Label>("kaamoStackingDesc").text = kaamoStackLimitNext switch
            {
                0 => Localization.Extra("kaamoUpgradeRulesOffHelp", "Ship upgrades are disabled and upgrade dealers do not appear."),
                1 => Localization.Extra("kaamoUpgradeRulesOriginalHelp", "Original rules. One upgrade of each type per ship."),
                _ => string.Format(Localization.Extra("kaamoStackingHelp",
                    "Kaamo Club upgrades can be fitted again, each level twice the price, up to level {0}."), kaamoStackLimitNext)
            };
            root.Q<Label>("kaamoVipDesc").text = Localization.Extra("kaamoVipOptionHelp",
                "The VIP Card gives 20% off ship upgrades by Kaamo mechanics. Buy at the club for 1 million credits.");
            root.Q<Button>("kaamoStackingToggle").EnableInClassList("choice-button--on", kaamoStackLimitNext != 1);
            root.Q<Label>("kaamoStackingLabel").text = (Localization.Extra("kaamoUpgradeRules", "Kaamo Club upgrades") + ": " +
                (kaamoStackLimitNext == 0 ? Localization.Extra("kaamoVipOff", "Off") : kaamoStackLimitNext.ToString())).UiCaps();
            if (kaamoStackLimitNext == 0) kaamoVipNext = false;
            var vip = root.Q<Button>("kaamoVipToggle");
            vip.SetEnabled(kaamoStackLimitNext != 0);
            vip.EnableInClassList("choice-button--unavailable", kaamoStackLimitNext == 0);
            vip.EnableInClassList("choice-button--on", kaamoVipNext);
            root.Q<Label>("kaamoVipLabel").text = (Localization.Extra("kaamoVip", "Kaamo Club VIP Card") + ": " +
                (kaamoVipNext ? Localization.Extra("kaamoVipCard", "Buy at the club") : Localization.Extra("kaamoVipOff", "Off"))).UiCaps();
        }

        void OpenDifficultyForNewGame()
        {
            hardcoreNext = tutorialsNext = false;
            RefreshHardcoreToggle();
            RefreshTutorialToggle();
            OpenPanel("difficultyPanel");
        }

        void PickDifficulty(float difficulty)
        {
            pendingDifficulty = difficulty;
            remakeRulesNext = SavedGameRules.None;
            autocompleteNext = cloakBayNext = false;
            kaamoStackLimitNext = 1;
            kaamoVipNext = false;
            noMedalsNext = false;   // likewise
            RefreshMedalsToggle();
            pendingEconomy = pendingModCampaign?.economy ?? Economy.Default;
            RefreshEconomyToggle();
            RefreshSavedRuleToggles();
            RefreshKaamoRules();
            RefreshModOptions();   // remake mods: their new-game options
            OpenPanel("gameOptionsPanel");
        }

        void RefreshEconomyToggle()
        {
            bool android = pendingEconomy == Economy.Android;
            var button = root.Q<Button>("economyToggle");
            button.SetEnabled(pendingModCampaign?.economy == null);
            button.EnableInClassList("choice-button--on", android);
            string type = android ? "Android" : Localization.Extra("economyClassicType", "Classic");
            root.Q<Label>("economyTypeLabel").text = (Localization.Extra("economyType", "Economy") + ": " + type).UiCaps();
            root.Q<Label>("economyTypeDesc").text = android
                ? Localization.Extra("economyAndroidDesc", "Android prices: dearer goods and gear, bigger blueprints, cheaper ships.")
                : Localization.Extra("economyDefaultDesc", "PC / Mac / iPhone prices: cheaper goods and blueprints, dearer ships.");
            root.Q<Label>("remakeOptionsStartDesc").text = Session.DifficultyName(pendingDifficulty)
                + (hardcoreNext ? " / " + Localization.Extra("hardcoreTitle", "Hardcore") : "");
        }

        void RefreshHardcoreToggle()
        {
            var b = root.Q<Button>("hardcoreToggle");
            if (b == null) return;
            b.EnableInClassList("choice-button--on", hardcoreNext);
            var label = root.Q<Label>("hardcoreLabel");
            if (label != null)
                label.text = $"{Localization.Extra("hardcoreTitle", "Hardcore")}: {(hardcoreNext ? Localization.Extra("hardcoreOn", "Permadeath") : Localization.Extra("hardcoreOff", "Off"))}".UiCaps();
            var desc = root.Q<Label>("hardcoreDesc");
            if (desc != null)
                desc.text = Localization.Extra("hardcoreDesc",
                    "One life: saves only on docking; if your ship is destroyed, this game's saves are deleted.");
        }

        /// <summary>Remake (a player's request 2026-11-04): medals on (the original) or none in this game (Session.NoMedals):
        /// no medals, toasts or Medals tab, the VoidX with the main story won and the Specter / fireworks with the Supernova
        /// story finished (Achievements.VoidXUnlocked / SupernovaUnlocked).</summary>
        void RefreshMedalsToggle()
        {
            var b = root.Q<Button>("medalsToggle");
            if (b == null) return;
            b.EnableInClassList("choice-button--on", noMedalsNext);
            var label = root.Q<Label>("medalsLabel");
            if (label != null)
                label.text = $"{Localization.Extra("medalsTitle", "Medals")}: {(noMedalsNext ? Localization.Extra("off", "Off") : Localization.Extra("on", "On"))}".UiCaps();
            var desc = root.Q<Label>("medalsDesc");
            if (desc != null)
                desc.text = Localization.Extra("medalsDesc",
                    "Off: no medals in this game. The VoidX and wingmen who pay you to hire them become available after winning the main story; the Specter and fireworks after finishing Supernova.");
        }

        /// <summary>The Supernova Challenge's entry under the campaign cards (the remake's: the original offered it nowhere,
        /// supernova_challenge.md 1): its name (293) and the highscore (294) with the best kills (176), else the
        /// description's first paragraph (298).</summary>
        void RefreshChallenge()
        {
            var label = root.Q<Label>("challengeLabel");
            if (label != null) label.text = Localization.Get(293).UiCaps();
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
                label.text = $"{Localization.Extra("ngPlus", "New Game+")}: {(newGamePlus ? Localization.Extra("on", "On") : Localization.Extra("off", "Off"))}".UiCaps();
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
            b.EnableInClassList("choice-button--on", !on);
            var label = root.Q<Label>("kaamoLabel");
            if (label != null)
                label.text = $"{Localization.Get(78)}: {(on ? Localization.Extra("kaamoOwned", "Owned from the start") : Localization.Extra("kaamoNotOwned", "Buy it in-game"))}".UiCaps();
            var desc = root.Q<Label>("kaamoDesc");
            if (desc != null)
                desc.text = on
                    ? Localization.Extra("kaamoDesc", "Own the Kaamo Club in Shima from the start.")
                    : string.Format(Localization.Extra("kaamoUnlockDesc",
                        "Break the siege at Kaamo, then buy the club for 30 million credits and 50 t of {0}."), GameNames.Item(KaamoClub.BuskatItem));
        }

        /// <summary>Remake: the tutorial popups (Settings.TutorialHints, also in Options > Gameplay) as a game option before the
        /// start (it was a question after the economy).</summary>
        void RefreshTutorialToggle()
        {
            var b = root.Q<Button>("tutorialToggle");
            if (b == null) return;
            bool on = tutorialsNext;
            b.EnableInClassList("choice-button--on", on);
            var label = root.Q<Label>("tutorialLabel");
            if (label != null)
                label.text = $"{Localization.Extra("tutorialTitle", "Tutorials")}: {(on ? Localization.Extra("tutorialOn", "On") : Localization.Extra("tutorialOff", "Off"))}".UiCaps();
            var desc = root.Q<Label>("tutorialDesc");
            if (desc != null)
                desc.text = Localization.Extra("tutorialDesc",
                    "Popups explain the controls, hangar, map and missions the first time.");
        }

        /// <summary>Remake (players' suggestion): the capital ship enhancements (Settings.CapitalShips, also in Options >
        /// Gameplay) as a game option before the start.</summary>
        void RefreshSavedRuleToggles()
        {
            string X(string key, string fallback) => Localization.Extra(key, fallback);
            void Refresh(string id, string title, string value, string description, bool enabled)
            {
                root.Q<Button>(id + "Toggle").EnableInClassList("choice-button--on", enabled);
                root.Q<Label>(id + "Label").text = (title + ": " + value).UiCaps();
                root.Q<Label>(id + "Desc").text = description;
            }
            string ToggleValue(bool on) => on ? X("on", "On") : X("off", "Off");
            RefreshRemakeRule(SavedGameRules.PirateEvents, "pirateEvents", "Pirate outposts and bosses", "Some orbits hold a pirate outpost or a pirate boss with a bounty.");
            RefreshRemakeRule(SavedGameRules.CapitalShips, "capitalShips", "Capital ship enhancements", "Capital ships get escorts and stronger turrets. Carriers and Vossk battleships can be destroyed and docked at.");
            RefreshRemakeRule(SavedGameRules.DerelictWrecks, "derelictWrecks", "Derelict freighters", "Dock with derelict freighters and hack them for cargo.");
            RefreshRemakeRule(SavedGameRules.KhadorSlipstream, "khadorSlipstream", "Khador Slipstream", "Khador offers the Slipstream blueprint on Thynome.");
            Refresh("blueprintAutocomplete", X("blueprintAutocomplete", "Blueprint autocomplete"), ToggleValue(autocompleteNext),
                X("blueprintAutocompleteHelp", "Pay credits to supply missing blueprint ingredients where autocomplete is allowed."), autocompleteNext);
            Refresh("cloakBay", X("cloakBay", "Cloak bay"), ToggleValue(cloakBayNext),
                X("cloakBayHelp", "On the Specter and Scimitar a mounted cloak takes no equipment slot."), cloakBayNext);
        }

        void RefreshRemakeRule(SavedGameRules rule, string id, string title, string help)
        {
            bool on = (remakeRulesNext & rule) != 0;
            root.Q<Button>(id + "Toggle").EnableInClassList("choice-button--on", on);
            root.Q<Label>(id + "Label").text = (Localization.Extra(id, title) + ": " +
                (on ? Localization.Extra("on", "On") : Localization.Extra("off", "Off"))).UiCaps();
            root.Q<Label>(id + "Desc").text = Localization.Extra(id + "Help", help);
        }

        void BeginGame(Economy economy)
        {
            Session.ResetNewGame();   // Status::resetGame: Phantom at Var Hastra (Mido)
            Session.GameRules = remakeRulesNext;
            Settings.TutorialHints = tutorialsNext;
            Settings.BlueprintAutocomplete = autocompleteNext;
            Settings.CloakBay = cloakBayNext;
            Session.Hardcore = hardcoreNext;   // remake: permadeath (a new RunId came with ResetNewGame)
            Session.NoMedals = noMedalsNext;   // remake: a game without medals (before the campaign start's add-on medals)
            Session.Campaign = pendingCampaign;
            Session.Difficulty = pendingDifficulty;
            Session.StackKaamoUpgrades = kaamoStackLimitNext > 1;
            Session.KaamoUpgradeLimit = kaamoStackLimitNext;
            Settings.KaamoVipMode = kaamoVipNext ? Settings.KaamoVipCard : Settings.KaamoVipOff;
            Session.Economy = economy;   // before the Database: it loads that economy's tables
            if (KaamoFromStart) Session.KaamoState = 3;   // resetGame with the expansion bought: the club owned, its storage empty
            Modding.ModGameOptions.ApplyChoices();   // remake mods: the options picked in the Game options panel
            // Remake mods (#313): the campaign is known before its database loads, so a script's "database" handler can tell.
            if (pendingModCampaign != null) Session.ModCampaign = pendingModCampaign.Id;
            var db = Database.Load();
            if (!GoF2Remake.Multiplayer.NetGame.Active && Modding.ModPlayerInfrastructure.Current.PlayerIdentityEnabled)
            {
                singlePlayerIdentity = (race, portrait) =>
                {
                    Session.PlayerIdentityRace = race;
                    Session.PlayerIdentityPortrait = portrait;
                    StartSelectedGame(db);
                };
                BuildIdentitySetup();
                return;
            }
            StartSelectedGame(db);
        }

        void StartSelectedGame(Database db)
        {
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
            Speedrun.NewGame(pendingCampaign, pendingDifficulty, newGamePlus && ngPlusSave != null);   // remake: the speedrun timer's run
            StartCoroutine(Leave(scene));
        }
    }
}
