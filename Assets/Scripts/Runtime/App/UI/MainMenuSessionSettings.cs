// MainMenuSessionSettings.cs
// Part of MainMenu: the host card's Session settings window (mpSettingsPanel; the user's request 2026-11-28: one button
// for every setting of a hosted session, many of which the card never showed). The card keeps the important ones (mode,
// game name, password, player limit, Combat, World); the window has the Debug and Mods switches, the co-op campaign's
// rows (moved there from the card) and a row for each of the server settings a player host had only as "/set"
// (NetServerSettings.HostWindow: the gameplay rules, the Kaamo limit, flight model, ship collision, a persistent world's
// profile and faction numbers). Its choices are kept (NetServerSettings.ChooseForHost, PlayerPrefs "mp_set_<key>") and
// applied when hosting (NetGame.StartHost); a running session's admins change them with /set or the Admin tab.

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        readonly List<System.Action> sessionRows = new List<System.Action>();
        readonly List<VisualElement> sessionElements = new List<VisualElement>();

        static string SX(string key, string english) => Localization.Extra(key, english);

        void SetupSessionSettings()
        {
            Bind("mpSettings", OpenSessionSettings);
            Bind("mpSettingsReset", () => { NetServerSettings.ClearHostChoices(); RefreshSessionSettings(); });
            ApplySessionTexts();
        }

        /// <summary>The generated rows, made again at each opening (the language may have changed): after the moved ones.</summary>
        void BuildSessionRows()
        {
            var grid = root.Q("mpSettingsGrid");
            if (grid == null) return;
            foreach (var e in sessionElements) e.RemoveFromHierarchy();
            sessionElements.Clear();
            sessionRows.Clear();
            void Add(VisualElement e) { grid.Add(e); sessionElements.Add(e); }
            bool world = false;
            Add(SessionHeader(SX("mpSessionRulesHeader", "Gameplay rules")));
            foreach (var s in NetServerSettings.HostWindow)
            {
                if (!world && IsWorldSetting(s.key)) { world = true; Add(SessionHeader(SX("mpSessionWorldHeader", "Persistent world: profiles and factions"))); }
                Add(SessionRow(s));
            }
        }

        static bool IsWorldSetting(string key) => key == "maxearn" || key == "claimcost" || key == "maxclaims" || key == "claimdays"
                                                  || key == "siegecost" || key == "toll";

        void OpenSessionSettings()
        {
            ApplySessionTexts();
            BuildSessionRows();
            RefreshSessionSettings();
            OpenPanel("mpSettingsPanel");
        }

        void ApplySessionTexts()
        {
            if (root.Q<Label>("mpSettingsTitle") is Label title) title.text = SX("mpSessionSettings", "Session settings").UiCaps();
            if (root.Q<Label>("mpSettingsNote") is Label note)
                note.text = SX("mpSessionNote", "Kept for the next time you host. In a running session, admins change them with /set.")
                            + (coopPanelMode ? " " + SX("mpSessionNoteCoop", "In a co-op campaign, the rules you leave unchanged come from the hosted save.") : "");
            if (root.Q<Button>("mpSettingsReset") is Button reset) InputGlyph.SetText(reset, SX("mpSessionReset", "Reset to defaults").UiCaps());
            if (root.Q<Button>("mpSettings") is Button open) InputGlyph.SetText(open, SX("mpSessionSettings", "Session settings").UiCaps());
        }

        void RefreshSessionSettings()
        {
            foreach (var r in sessionRows) r();
        }

        static Label SessionHeader(string text)
        {
            var l = new Label(text.UiCaps());
            l.AddToClassList("mp-label");
            l.AddToClassList("gof-semibold");
            l.style.width = Length.Percent(100);
            l.style.marginTop = 12;
            return l;
        }

        VisualElement SessionRow(NetServerSettings.Setting s)
        {
            var row = new VisualElement();
            row.AddToClassList("mp-debug-row");
            var label = new Label(SessionLabel(s).UiCaps());
            label.AddToClassList("mp-label");
            label.AddToClassList("mp-debug-label");
            label.AddToClassList("gof-semibold");
            row.Add(label);
            string[] values = s.kind == NetServerSettings.Kind.Toggle ? new[] { "off", "on" }
                            : s.key == NetRules.FlightKey ? new[] { "player", "gof2", "es2", "chorus" }
                            : s.key == NetRules.CollisionKey ? new[] { "original", "mixed", "exact" } : null;
            if (values != null)
            {
                var segments = new VisualElement();
                segments.AddToClassList("choice-segments");
                segments.AddToClassList("mp-debug-segments");
                var buttons = new List<Button>();
                for (int i = 0; i < values.Length; i++)
                {
                    string v = values[i];
                    var b = new Button(() =>
                    {
                        Play(buttonRelease);
                        NetServerSettings.ChooseForHost(s, v);
                        RefreshSessionSettings();
                    }) { text = SessionValueText(s.key, v).UiCaps() };
                    b.AddToClassList("choice-segment");
                    b.AddToClassList("gof-semibold");
                    if (i == values.Length - 1) b.AddToClassList("choice-segment--last");
                    HookFocusSound(b);
                    segments.Add(b);
                    buttons.Add(b);
                }
                row.Add(segments);
                sessionRows.Add(() =>
                {
                    string now = NetServerSettings.HostValue(s);
                    for (int i = 0; i < values.Length; i++) buttons[i].EnableInClassList("choice-segment--active", values[i] == now);
                });
            }
            else
            {
                var field = new TextField { isDelayed = true };
                field.AddToClassList("debug-filter");
                if (s.kind == NetServerSettings.Kind.Number) field.keyboardType = TouchScreenKeyboardType.NumberPad;
                // isDelayed: the value arrives on Enter or when the field is left; the shown value is the checked one.
                field.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue != NetServerSettings.HostValue(s) && NetServerSettings.ChooseForHost(s, e.newValue) is string problem)
                        if (root.Q<Label>("mpSettingsNote") is Label note) note.text = problem;   // until the next opening
                    RefreshSessionSettings();
                });
                row.Add(field);
                sessionRows.Add(() => field.SetValueWithoutNotify(NetServerSettings.HostValue(s)));
            }
            return row;
        }

        /// <summary>The row's name: the Options page's text where the rule has one there, else the window's own.</summary>
        static string SessionLabel(NetServerSettings.Setting s) => s.key switch
        {
            "cloakhides" => SX("cloakLosesPursuers", "Cloak shakes off pursuers"),
            "pirateevents" => SX("pirateEvents", "Pirate outposts and bosses"),
            "capitalships" => SX("capitalShips", "Capital ship enhancements"),
            "informeroriginal" => SX("mpSessionInformer", "Informer missions: the original's rule"),
            "kaamostacking" => SX("kaamoStacking", "Stackable Kaamo Club upgrades"),
            NetRules.KaamoLevelKey => SX("kaamoMaxLevel", "Kaamo Club upgrade limit"),
            "kaamoequipment" => SX("kaamoKeepsEquipment", "Stored ships keep their equipment"),
            "cloakbay" => SX("cloakBay", "Cloak bay on cloaking ships"),
            "empcloak" => SX("empBreaksCloak", "EMP breaks cloaks"),
            "smarttargeting" => SX("smarterNpcTargeting", "Smarter NPC targeting"),
            "es2aiming" => SX("es2Aiming", "EVERSPACE 2 NPC aiming"),
            "es2flight" => SX("es2NpcFlight", "EVERSPACE 2 NPC flight"),
            "derelicts" => SX("derelictWrecks", "Derelict freighters"),
            NetRules.FlightKey => SX("mpSessionFlight", "Flight model"),
            NetRules.CollisionKey => SX("shipCollision", "Ship collision"),
            "maxearn" => SX("mpSessionMaxEarn", "Worth a profile may gain per minute"),
            "claimcost" => SX("mpSessionClaimCost", "Faction claim cost"),
            "maxclaims" => SX("mpSessionMaxClaims", "Stations per faction"),
            "claimdays" => SX("mpSessionClaimDays", "Days before an unvisited claim lapses"),
            "siegecost" => SX("mpSessionSiegeCost", "Siege cost"),
            "toll" => SX("mpSessionToll", "Toll at faction stations (credits or %)"),
            _ => s.label,
        };

        static string SessionValueText(string key, string value) => value switch
        {
            "on" => SX("on", "On"),
            "off" => SX("off", "Off"),
            "player" => SX("mpFlightPlayer", "Each player's"),
            "gof2" => SX("flightStyleOriginal", "Original"),
            "es2" => "EVERSPACE 2",
            "chorus" => "Chorus",
            "original" => SX("shipCollisionOriginal", "Original"),
            "mixed" => SX("shipCollisionMixed", "Mixed"),
            "exact" => SX("shipCollisionExact", "Exact shape"),
            _ => value,
        };
    }
}
