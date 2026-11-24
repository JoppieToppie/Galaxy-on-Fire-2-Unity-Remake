using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class StationDebugAvailabilityTests : PlayModeTest
    {
        [UnityTest]
        public IEnumerator EnablingDebugInOptionsRevealsEntryWithoutReloadingStation()
        {
            using var unlocked = new SettingsScope<bool>(() => Cheats.Unlocked, v => Cheats.Unlocked = v);
            using var consent = new SettingsScope<bool>(() => GameRulesAccess.Confirmed,
                v => { if (v) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); });
            Cheats.Unlocked = false;
            GameRulesAccess.Reset();
            FreePlayAt(78);
            yield return Load("Station");
            var menu = Find<StationMenu>();
            yield return WaitFor(() => !Get<bool>(menu, "UiBlocked") && !Get<bool>(menu, "ArrivalPending"), 15f, "station controls");
            Invoke(menu, "OpenSystemMenu");
            var entry = Get<Button>(menu, "debugButton");
            Assert.IsNotNull(entry, "Debug must be available to reveal after the scene has loaded");
            Assert.AreEqual(DisplayStyle.None, entry.style.display.value);
            var pageType = typeof(StationMenu).GetNestedType("SysPage", BindingFlags.NonPublic);
            Invoke(menu, "ShowSystemPage", Enum.Parse(pageType, "Options"));
            var view = Get<OptionsView>(menu, "optionsView");
            view.SelectTab(5);
            var toggle = view.NavItems().Select(view.RowOf).First(c => c?.def.id == "debugTools");
            toggle.Step(1);
            Assert.IsTrue(view.RulesPromptOpen, "enabling still requires confirmation");
            Assert.IsFalse(Cheats.Unlocked);
            view.Activate(view.FooterButtons[1]);
            Assert.IsTrue(Cheats.Unlocked);
            view.Activate(view.BackButton);
            Assert.AreNotEqual(DisplayStyle.None, entry.style.display.value, "entry appears without rebuilding the station");
            int profile = Settings.CurrentScopedExperience(false);
            Invoke(menu, "ShowSystemPage", Enum.Parse(pageType, "Debug"));
            Assert.AreEqual("Debug", Get<object>(menu, "sysPage").ToString());
            Assert.IsFalse(menu.IsDialogOpen, "already-enabled Debug needs no Game Rules confirmation");
            Assert.IsFalse(GameRulesAccess.Confirmed);
            Assert.AreEqual(profile, Settings.CurrentScopedExperience(false));
            Invoke(menu, "ShowSystemPage", Enum.Parse(pageType, "Options"));
            view = Get<OptionsView>(menu, "optionsView");
            view.SelectTab(5);
            toggle = view.NavItems().Select(view.RowOf).First(c => c?.def.id == "debugTools");
            toggle.Step(-1);
            Assert.IsFalse(Cheats.Unlocked);
            view.Activate(view.BackButton);
            Assert.AreEqual(DisplayStyle.None, entry.style.display.value, "disabling hides the same entry");
        }

        [UnityTest]
        public IEnumerator MainMenuDebugConfirmsItsOwnEnableOnly()
        {
            using var unlocked = new SettingsScope<bool>(() => Cheats.Unlocked, v => Cheats.Unlocked = v);
            using var welcome = new SettingsScope<bool>(() => Settings.WelcomeShown, v => Settings.WelcomeShown = v);
            using var consent = new SettingsScope<bool>(() => GameRulesAccess.Confirmed,
                v => { if (v) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); });
            Settings.WelcomeShown = true;
            Cheats.Unlocked = false;
            GameRulesAccess.Reset();
            yield return Load("MainMenu");
            var menu = Find<MainMenu>();
            Invoke(menu, "EnterMenu");
            yield return Run(0.3f);
            Invoke(menu, "OpenDebug");
            var root = Get<VisualElement>(menu, "root");
            Assert.IsTrue(Get<VisualElement>(menu, "dialog").ClassListContains("dialog-backdrop--shown"));
            Assert.AreEqual(Localization.Extra("debugTools", "Debug tools").UiCaps(), root.Q<Label>("dialogTitle").text);
            Assert.IsFalse(Cheats.Unlocked);
            Invoke(menu, "ConfirmDialog");
            Assert.IsTrue(Cheats.Unlocked);
            Assert.AreEqual("debugPanel", Get<VisualElement>(menu, "openPanel").name);
            Assert.IsFalse(GameRulesAccess.Confirmed);
            Invoke(menu, "Back");
            Invoke(menu, "OpenDebug");
            Assert.AreEqual("debugPanel", Get<VisualElement>(menu, "openPanel").name);
            Assert.IsFalse(Get<VisualElement>(menu, "dialog").ClassListContains("dialog-backdrop--shown"));
        }

        [UnityTest]
        public IEnumerator FlightDebugDoesNotOpenGameRulesConsent()
        {
            using var unlocked = new SettingsScope<bool>(() => Cheats.Unlocked, v => Cheats.Unlocked = v);
            using var consent = new SettingsScope<bool>(() => GameRulesAccess.Confirmed,
                v => { if (v) GameRulesAccess.Accept(); else GameRulesAccess.Reset(); });
            Cheats.Unlocked = true;
            GameRulesAccess.Reset();
            FreePlayAt(78);
            yield return Load("Space");
            var pause = Get<PauseMenu>(Find<FlightHud>(), "pauseMenu");
            pause.Open(Find<GoF2Remake.World.SpaceLevel>());
            var pageType = typeof(PauseMenu).GetNestedType("Page", BindingFlags.NonPublic);
            Invoke(pause, "Show", Enum.Parse(pageType, "Debug"));
            Assert.AreEqual("Debug", Get<object>(pause, "page").ToString());
            Assert.IsFalse(GameRulesAccess.Confirmed);
            pause.Close();
        }
    }
}
