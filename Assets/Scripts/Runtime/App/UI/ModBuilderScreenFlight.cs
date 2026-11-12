// ModBuilderScreenFlight.cs
// ModBuilderScreen: the flight rules editor (Flight.FlightRules, Modding.ModFlight). The menu's "Flight rules" edits the
// mod's flight.json (every ship the player flies); a ship's page edits its own ships.json "flight": { "rules" }
// (ModBuilderViewMore.BuildShipFlight). Both list every rule with the game's value; a value set back to it is removed.

using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public abstract partial class ModBuilderScreen
    {
        /// <summary>The menu's "Flight rules": flight.json.</summary>
        void ShowFlightRules()
        {
            modal.Clear();
            var box = Add(modal, new VisualElement(), "mbv-modal-box", "mbv-modal-box--list", "mbv-panel");
            Heading(box, T("mbFlightRules", "Flight rules"));
            Note(box, T("mbFlightRulesNote", "How every ship the player flies moves while this mod is on (a ship's own rules win)."));
            var scroll = Add(box, new ScrollView(ScrollViewMode.Vertical), "mbv-picker-scroll");
            FlightRulesFields(scroll, Project.FlightRules, ShowFlightRules);
            Act(box, T("mbClose", "Close"), CloseModal);
            modal.AddToClassList("mbv-modal--shown");
        }

        /// <summary>Every FlightRules field as a field of 'rules' (edited in place), grouped by its [Header], its [Tooltip]
        /// below; 'rebuild' draws the list again (after a reset or a toggle).</summary>
        protected void FlightRulesFields(VisualElement parent, JObject rules, Action rebuild)
        {
            if (rules.Count > 0) Act(parent, T("mbFlightRulesReset", "All back to the game's"), () => { rules.RemoveAll(); MarkDirty(); rebuild(); });
            foreach (var f in Flight.FlightRules.Fields)
            {
                string name = f.Name;
                if (f.GetCustomAttribute<HeaderAttribute>() is HeaderAttribute h) Heading(parent, T("mbFlightGroup_" + name, h.header));
                string label = T("mbRule_" + name, Regex.Replace(name, "(?<=[a-z])([A-Z])", " $1").ToLowerInvariant());
                if (f.FieldType == typeof(bool))
                {
                    bool def = (bool)f.GetValue(Flight.FlightRules.Default);
                    bool cur = rules[name] is JValue b && b.Type == JTokenType.Boolean ? (bool)b : def;
                    FlagButton(Row(parent), label, cur, on => { if (on == def) rules.Remove(name); else rules[name] = on; }, rebuild);
                }
                else
                {
                    float def = (float)f.GetValue(Flight.FlightRules.Default);
                    float cur = rules[name] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : def;
                    var r = f.GetCustomAttribute<RangeAttribute>();
                    Float(parent, label, cur, r?.min ?? float.MinValue, r?.max ?? float.MaxValue, x =>
                    {
                        if (Mathf.Approximately(x, def)) rules.Remove(name); else rules[name] = Math.Round(x, 6);
                        MarkDirty();
                    });
                }
                if (f.GetCustomAttribute<TooltipAttribute>() is TooltipAttribute tip)
                    Note(parent, T("mbRuleNote_" + name, tip.tooltip) + $" ({T("mbGameValue", "game")}: {f.GetValue(Flight.FlightRules.Default)})");
            }
        }
    }
}
