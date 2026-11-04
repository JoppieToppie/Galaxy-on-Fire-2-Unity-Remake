// MainMenuWelcome.cs
// Part of MainMenu: the welcome screen on the first start (the user's request, 2026-11-04): what the remake is, where
// things are, then how much of the remake to play with (Settings.ApplyExperience: Original / Cosmetic / Remake). Shown once
// (Settings.WelcomeShown), in the menu's own dialog, so keys, a controller and touch work as everywhere. An install played
// before (Settings.FreshInstall false) keeps its options and only hears where the new choice is.

using GoF2Remake.Data;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        /// <summary>EnterMenu: once the main buttons are in (their reveal takes the focus first).</summary>
        const long WelcomeDelayMs = 1300;

        void ScheduleWelcome()
        {
            if (Settings.WelcomeShown) return;
            root.schedule.Execute(() =>
            {
                if (Settings.WelcomeShown || screen != MenuState.Menu) return;
                // A panel or a dialog opened meanwhile: once it closes, the next time the menu shows.
                if (openPanel != null || dialog.ClassListContains("dialog-backdrop--shown")) return;
                ShowWelcome();
            }).ExecuteLater(WelcomeDelayMs);
        }

        // The menus' own names, as their buttons and tabs read (so a translation never names them differently).
        static string OptionsName => Localization.Get(31);
        static string GameplayName => OptionsCatalog.PageTitle(OptionPage.Gameplay);

        void ShowWelcome()
        {
            string text = X("welcomeText", "This is a remake of Galaxy on Fire 2, rebuilt from the original game. It also adds things of its own: new gameplay, presentation touches and mods. You choose how much of that you want.")
                          + "\n\n" + X("welcomeWhere", "Where to find things:")
                          + "\n• " + string.Format(X("welcomeWhereExperience", "{0} > {1}: the game experience, and every new feature on its own"), OptionsName, GameplayName)
                          + "\n• " + string.Format(X("welcomeWhereControls", "{0} > {1} and {2}: keyboard, mouse, controller and touch"), OptionsName,
                                                   OptionsCatalog.PageTitle(OptionPage.Controls), OptionsCatalog.PageTitle(OptionPage.Bindings))
                          + "\n• " + string.Format(X("welcomeWhereMods", "{0}: add mods and switch them off"), X("modsButton", "Mods"))
                          + "\n• " + string.Format(X("welcomeWhereMultiplayer", "{0} and {1}: play with others"), X("multiplayer", "Multiplayer"), CoopTitle);
            if (!Settings.FreshInstall)
            {
                // Played before: the options stay as they were; only where the new choice is.
                ShowNotice(X("welcomeTitle", "Welcome, pilot"), text + "\n\n" + string.Format(X("welcomeKept", "New: {0} > {1} > {2} switches the remake's features at once. Your options are kept as they were."),
                    OptionsName, GameplayName, X("gameExperience", "Game experience")));
                Settings.WelcomeShown = true;
                return;
            }
            ShowNotice(X("welcomeTitle", "Welcome, pilot"), text);
            InputGlyph.SetText(root.Q<Button>("dialogYes"), X("welcomeNext", "Next").UiCaps());
            dialogYes = ShowExperienceChoice;
            dialogNo = () => Settings.WelcomeShown = true;   // Back / Esc skips it: the Original, the default, stays
        }

        /// <summary>The second step: three answers. Back / B / Esc answer No, so No is the Original (the default, nothing
        /// changed); the focus starts there.</summary>
        void ShowExperienceChoice()
        {
            var names = OptionsCatalog.ExperienceNames();
            string text = string.Format(X("welcomeOriginal", "{0}: the game as it was, nothing added."), names[Settings.ExperienceOriginal])
                          + "\n" + string.Format(X("welcomeCosmetic", "{0}: the original plus the remake's presentation, like ships flying in and out of the hangars and animated dialogue."), names[Settings.ExperienceCosmetic])
                          + "\n" + string.Format(X("welcomeRemake", "{0}: every new feature on, like pirate outposts, stronger capital ships and the Khador Slipstream."), names[Settings.ExperienceRemake])
                          + "\n\n" + string.Format(X("welcomeChangeLater", "You can change it, or any single feature, in {0} > {1}."), OptionsName, GameplayName);
            ShowDialog(X("welcomeChoiceTitle", "How do you want to play?"), text, () => ChooseExperience(Settings.ExperienceRemake));
            dialogAlt = () => ChooseExperience(Settings.ExperienceCosmetic);
            dialogNo = () => ChooseExperience(Settings.ExperienceOriginal);
            var yes = root.Q<Button>("dialogYes");
            var alt = root.Q<Button>("dialogAlt");
            var no = root.Q<Button>("dialogNo");
            InputGlyph.SetText(yes, names[Settings.ExperienceRemake].UiCaps());
            InputGlyph.SetText(alt, names[Settings.ExperienceCosmetic].UiCaps());
            InputGlyph.SetText(no, names[Settings.ExperienceOriginal].UiCaps());
            alt.style.display = DisplayStyle.Flex;
            Select(no);
        }

        void ChooseExperience(int experience)
        {
            Settings.ApplyExperience(experience);
            Settings.WelcomeShown = true;
            root.schedule.Execute(() => FocusFirst(mainButtons));
        }
    }
}
