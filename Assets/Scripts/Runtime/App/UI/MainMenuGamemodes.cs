using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        bool customGamemodesTab;

        // Add future modes here. Each entry owns its selection action; campaign artwork is optional by design.
        IEnumerable<(string id, string title, string description, Action select)> CustomGamemodes()
        {
            yield return ("scavenger", ScavengerMode.Name,
                Localization.Extra("scavengerDescription", "Salvage your equipment. Manufacture fireworks to earn Champion."),
                OpenScavengerDetails);
        }

        void OpenScavengerDetails()
        {
            scavengerNext = true;
            newGamePlus = false;
            var panel = panels["modeDetailsPanel"];
            panel.Q<Label>("modeDetailsTitle").text = ScavengerMode.Name.UiCaps();
            panel.Q<Label>("modeDetailsText").text = Localization.Extra("scavengerDetails", ScavengerDetails);
            InputGlyph.SetText(panel.Q<Button>("modeDetailsBack"), (Settings.OriginalUi ? "" : "‹  ") + Localization.Get(170).UiCaps());
            var next = panel.Q<Button>("modeDetailsContinue");
            next.text = Localization.Extra("scavengerContinue", "Choose difficulty").UiCaps();
            if (!next.ClassListContains("mode-details-bound"))
            {
                next.AddToClassList("mode-details-bound");
                next.clicked += () => { Play(buttonRelease); PickCampaign(Campaign.GalaxyOnFire2); scavengerNext = true; newGamePlus = false; };
                HookFocusSound(next);
                new DragScroll(panel.Q<ScrollView>("modeDetailsScroll"));
            }
            OpenPanel("modeDetailsPanel");
        }

        const string ScavengerDetails = "COLLECT, TRADE, PRODUCE\nBuild your equipment collection from salvaged cargo. Complete Fireworks production to earn Champion: owning Fireworks alone does not count.\n\nYOUR START\nBegin at Dis in a Betty with Luna EMP Mk I, 10 EMP GL I missiles, an AB-1 Retractor and a Telta Ecoscan. You have zero credits. Choose your difficulty and whether you own Kaamo from the start. The economy is always Default.\n\nAN OPEN WORLD\nThere is no campaign. All systems are revealed; visit indicators and statistics work normally. Freelance missions, diplomats, wingmen, mining and plasma harvesting remain available.\n\nFIND YOUR EQUIPMENT\nStations generate primary weapons, turrets and equipment up to tech level 5. Higher-tech equipment must come from loot; selling it and buying it back is allowed. Each permitted original item type has an equal chance in ordinary NPC cargo, regardless of rarity or tech level. Shops retain their normal station-tech and random stock checks. Mining resources, plasma and mission cargo keep their dedicated sources.\n\nTWO RECIPES\nChromo Plasma and Fireworks are unlocked immediately; other blueprints and blueprint sellers are unavailable. Autocomplete is disabled.\n\nYOUR GOAL\nOnly Champion is awarded in this mode. Finish Fireworks production and close the hangar to receive it.";
        void RefreshCustomGamemodes()
        {
            var panel = panels["campaignPanel"];
            var tabs = panel.Q("newGameTabs");
            if (tabs == null)
            {
                tabs = new VisualElement { name = "newGameTabs" };
                tabs.AddToClassList("new-game-tabs");
                panel.Insert(2, tabs);
                var campaigns = new Button(() => SelectNewGameTab(false)) { name = "campaignsTab" };
                var custom = new Button(() => SelectNewGameTab(true)) { name = "customGamemodesTab" };
                foreach (var button in new[] { campaigns, custom })
                {
                    button.AddToClassList("new-game-tab");
                    button.AddToClassList("gof-semibold");
                    HookFocusSound(button);
                    tabs.Add(button);
                }
                var body = new VisualElement { name = "newGameContent" };
                body.AddToClassList("new-game-content");
                panel.Insert(panel.IndexOf(panel.Q("campaignBack")), body);
                var campaignsPage = new ScrollView(ScrollViewMode.Vertical) { name = "campaignsPage" };
                campaignsPage.AddToClassList("new-game-page");
                campaignsPage.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                campaignsPage.verticalScrollerVisibility = ScrollerVisibility.Auto;
                campaignsPage.Add(campaignCardScroll);
                campaignsPage.Add(panel.Q("campaignChoices"));
                body.Add(campaignsPage);
                new DragScroll(campaignsPage);
                var list = new ScrollView(ScrollViewMode.Vertical) { name = "customGamemodesList" };
                list.AddToClassList("new-game-page");
                list.AddToClassList("custom-gamemodes-list");
                list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                list.verticalScrollerVisibility = ScrollerVisibility.Auto;
                body.Add(list);
                new DragScroll(list);
            }
            panel.Q<Button>("campaignsTab").text = Localization.Extra("newGameCampaigns", "Campaigns").UiCaps();
            panel.Q<Button>("customGamemodesTab").text = Localization.Extra("customGamemodes", "Custom gamemodes").UiCaps();
            var modes = panel.Q<ScrollView>("customGamemodesList");
            modes.Clear();
            foreach (var mode in CustomGamemodes())
            {
                var select = mode.select;
                var row = new Button(() => { Play(buttonRelease); select(); }) { name = "gamemode-" + mode.id };

                row.AddToClassList("custom-gamemode");
                var title = new Label(mode.title.UiCaps()) { pickingMode = PickingMode.Ignore };
                title.AddToClassList("choice-title");
                title.AddToClassList("gof-semibold");
                var description = new Label(mode.description) { pickingMode = PickingMode.Ignore };
                description.AddToClassList("choice-desc");
                row.Add(title);
                row.Add(description);
                row.RegisterCallback<FocusInEvent>(_ => modes.ScrollTo(row));
                row.RegisterCallback<PointerDownEvent>(_ => Play(buttonPush), TrickleDown.TrickleDown);
                HookFocusSound(row);
                modes.Add(row);
            }
            SelectNewGameTab(customGamemodesTab, false);
        }

        void SelectNewGameTab(bool custom, bool sound = true)
        {
            customGamemodesTab = custom;
            var panel = panels["campaignPanel"];
            panel.Q<Label>("campaignTitle").text = root.Q<Button>("newGameButton").text;
            panel.Q("campaignsPage").style.display = custom ? DisplayStyle.None : DisplayStyle.Flex;
            panel.Q("campaignChoices").style.display = custom ? DisplayStyle.None : DisplayStyle.Flex;
            panel.Q("customGamemodesList").style.display = custom ? DisplayStyle.Flex : DisplayStyle.None;
            panel.Q("campaignsTab").EnableInClassList("new-game-tab--selected", !custom);
            panel.Q("customGamemodesTab").EnableInClassList("new-game-tab--selected", custom);
            if (sound) Play(buttonRelease);
        }
    }
}
