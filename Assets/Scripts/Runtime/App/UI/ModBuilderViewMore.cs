// ModBuilderViewMore.cs
// The Mod Builder's less common item and ship fields (part of ModBuilderView; Modding/README.md "Items" and "Ships"): an
// item's cheapest / dearest systems, Vossk-only, the station that always stocks it, its condition, its attributes by number
// and its Default Economy prices; a ship's condition, the ordinary dealers and the lounge sellers that sell it, the turret
// items its NPC ships carry, its Default Economy price, its engine flame, and how it is seen: its chase camera, cockpit seat,
// damage smoke points and explosion size (ModShipView). Everything else is in Edit as JSON.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public partial class ModBuilderView
    {
        static int? JInt(JToken t) => t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (int)Math.Round((double)v) : (int?)null;
        static float? JFloat(JToken t) => t is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : (float?)null;

        static readonly string[] RaceChoices = { "Any", "Terran", "Vossk", "Nivelian", "Midorian" };
        static int RaceIndex(int race) => race < 0 || race > 3 ? 0 : race + 1;

        /// <summary>An item's other shop fields.</summary>
        void BuildItemMore(ProjectItem it, ItemData original)
        {
            Heading(itemPage, T("mbItemMore", "Where it sells"));
            RefField(itemPage, T("mbCheapestSystem", "Cheapest in the system"), RefKind.System, ProjectEntry.RefText(it.Get("lowestPriceSystem")),
                     v => { it.Set("lowestPriceSystem", v == null ? null : ProjectEntry.RefToken(v)); ItemEdited(); BuildItemPage(); },
                     original != null ? string.Format(T("mbAsBase", "as its base ({0})"), original.lowestPriceSystem) : null);
            RefField(itemPage, T("mbDearestSystem", "Dearest in the system"), RefKind.System, ProjectEntry.RefText(it.Get("highestPriceSystem")),
                     v => { it.Set("highestPriceSystem", v == null ? null : ProjectEntry.RefToken(v)); ItemEdited(); BuildItemPage(); },
                     original != null ? string.Format(T("mbAsBase", "as its base ({0})"), original.highestPriceSystem) : null);
            RefField(itemPage, T("mbAlwaysSoldAt", "Always in stock at"), RefKind.Station, ProjectEntry.RefText(it.Get("alwaysSoldAt")),
                     v => { it.Set("alwaysSoldAt", v == null ? null : ProjectEntry.RefToken(v)); ItemEdited(); BuildItemPage(); }, T("mbNoStation", "no station"));
            bool vossk = it.Get("vosskOnly") is JValue vo && vo.Type == JTokenType.Boolean && (bool)vo;
            FlagButton(Row(itemPage), T("mbVosskOnly", "Only Vossk systems sell it"), vossk, v => { it.Set("vosskOnly", v ? (JToken)true : null); ItemEdited(); });
            ConditionField(itemPage, T("mbAvailable", "Sold (and dropped) only when"), (string)it.Get("available"), v => { it.Set("available", v); ItemEdited(); });

            // Default Economy: the prices when the player picked the PC / Mac economy (the rest stays the item's).
            var de = it.Get("defaultEconomy") as JObject;
            int dMin = JInt(de?["minPrice"]) ?? 0, dMax = JInt(de?["maxPrice"]) ?? 0;
            void SetDe(string k, int v)
            {
                var o = it.Get("defaultEconomy") as JObject ?? new JObject();
                if (v <= 0) o.Remove(k); else o[k] = v;
                it.Set("defaultEconomy", o.HasValues ? o : null);
                ItemEdited();
            }
            Int(itemPage, T("mbDefaultMin", "Default Economy: lowest"), dMin, 0, 999999999, v => SetDe("minPrice", v));
            Int(itemPage, T("mbDefaultMaxPrice", "Default Economy: highest (0 = as the lowest)"), dMax, 0, 999999999, v => SetDe("maxPrice", v));

            // Raw attributes: the original game's attribute numbers ({ "9": 14 }).
            Heading(itemPage, T("mbAttributes", "Attributes by number"));
            var attrs = it.Get("attributes") as JObject;
            if (attrs != null)
                foreach (var p in attrs.Properties().ToList())
                {
                    string key = p.Name;
                    var arow = Row(itemPage);
                    Int(arow, string.Format(T("mbAttrN", "Attribute {0}"), key), JInt(p.Value) ?? 0, int.MinValue / 2, int.MaxValue / 2, v => { ((JObject)it.Get("attributes"))[key] = v; ItemEdited(); });
                    Act(arow, "×", () => { var o = (JObject)it.Get("attributes"); o.Remove(key); it.Set("attributes", o.HasValues ? o : null); ItemEdited(); BuildItemPage(); }, "mbv-step");
                }
            Act(itemPage, T("mbAddAttribute", "+ Attribute"), () => AskText(T("mbAddAttribute", "+ Attribute"), T("mbAttrNumber", "The attribute's number (reference.md lists them)"), "", text =>
            {
                if (!int.TryParse(text.Trim(), out int n) || n < 0) { Status(T("mbAttrBad", "An attribute is a number."), true); return; }
                var o = it.Get("attributes") as JObject ?? new JObject();
                o[n.ToString(CultureInfo.InvariantCulture)] = original != null ? original.Attr(n) : 0;
                it.Set("attributes", o);
                ItemEdited();
                BuildItemPage();
            }, 6), "mbv-add");
            Note(itemPage, T("mbAttributesNote", "For numbers the stats above don't name (reference.md lists every attribute)."));
        }

        /// <summary>A ship's selling and NPC fields.</summary>
        static readonly (string key, string english, int min, int max)[] FlightBonusFields =
        {
            ("topSpeed", "Top speed (% more)", -90, 1000), ("turnRate", "Turn rate (% more)", -90, 1000),
            ("strafeSpeed", "Strafe speed (% more)", -90, 1000), ("acceleration", "Acceleration in free flight (% more)", -90, 1000),
            ("boostFactor", "Free flight boost (x100 of the top speed, 0 = the booster's)", 0, 5000),
            ("fullStopDamage", "Gun damage standing still (% more)", 0, 1000), ("lowSpeedResistance", "Damage taken at low speed (% less)", 0, 100),
        };

        /// <summary>ships.json "flightStyle" and "flight" (ModFlight): the style it always flies, its bonuses for both styles
        /// (the per-style "original" / "free" ones by Edit as JSON) and its own flight rules (FlightRulesFields).</summary>
        void BuildShipFlight(ProjectShip s)
        {
            Heading(ship, T("mbFlight", "Flight"));
            ModFlight.TryParseStyle((string)s.Get("flightStyle"), out int style);
            Segments(ship, T("mbFlightStyle", "Flies with"), new[] { T("mbFlightStylePlayer", "The player's choice"), T("mbFlightStyleOriginal", "Original flight"), T("mbFlightStyleFree", "Free flight") },
                     style == FlightStyles.Original ? 1 : style == FlightStyles.Free ? 2 : 0,
                     n => { s.Set("flightStyle", n == 0 ? null : n == 1 ? "original" : "free"); MarkDirty(); BuildShipPage(); });
            var flight = s.Get("flight") as JObject ?? new JObject();
            void Store() { s.Set("flight", flight.Count > 0 ? flight : null); MarkDirty(); }
            foreach (var (key, english, min, max) in FlightBonusFields)
            {
                string k = key;
                Int(ship, T("mbFlight_" + key, english), JInt(flight[key]) ?? 0, min, max, v => { if (v == 0) flight.Remove(k); else flight[k] = v; Store(); });
            }
            var rules = flight["rules"] as JObject;
            FlagButton(Row(ship), T("mbShipFlightRules", "Flight rules of its own"), rules != null,
                       on => { if (on) flight["rules"] = new JObject(); else flight.Remove("rules"); Store(); }, BuildShipPage);
            if (rules != null) FlightRulesFields(Add(ship, new VisualElement(), "mbv-page-box"), rules, BuildShipPage);
        }

        void BuildShipMore(ProjectShip s)
        {
            Heading(ship, T("mbShipSelling", "Where it sells"));
            ConditionField(ship, T("mbAvailable", "Sold only when"), (string)s.Get("available"), v => { s.Set("available", v); MarkDirty(); });
            Int(ship, T("mbPriceDefault", "Price in the Default Economy (0 = the same)"), JInt(s.Get("priceDefault")) ?? 0, 0, 999999999,
                v => { s.Set("priceDefault", v > 0 ? (JToken)v : null); MarkDirty(); });

            // The ordinary dealers.
            var dealer = s.Get("dealer") as JObject;
            FlagButton(Row(ship), T("mbDealerSells", "Ship dealers stock it"), dealer != null, v =>
            {
                s.Set("dealer", v ? new JObject { ["chance"] = 2, ["systemRace"] = -1, ["minTechLevel"] = 0 } : null);
                MarkDirty();
            }, () => BuildShipPage());
            if (dealer != null)
            {
                var box = Add(ship, new VisualElement(), "mbv-page-box");
                Float(box, T("mbChancePct", "Chance (%)"), JFloat(dealer["chance"]) ?? 2f, 0f, 100f, v => { dealer["chance"] = Math.Round(v, 2); MarkDirty(); });
                Segments(box, T("mbInSystemsOf", "In systems of"), RaceChoices.Select(r => T("mbRace" + r, r)).ToArray(), RaceIndex(JInt(dealer["systemRace"]) ?? -1),
                         n => { dealer["systemRace"] = n - 1; MarkDirty(); BuildShipPage(); });
                Int(box, T("mbMinTech", "At stations of tech level at least"), JInt(dealer["minTechLevel"]) ?? 0, 0, 10, v => { dealer["minTechLevel"] = v; MarkDirty(); });
            }

            // A lounge visitor sells it.
            var lounge = s.Get("lounge") as JObject;
            FlagButton(Row(ship), T("mbLoungeSells", "A lounge visitor sells it"), lounge != null, v =>
            {
                s.Set("lounge", v ? new JObject { ["systemRace"] = -1, ["minCampaign"] = 0, ["minRank"] = 0, ["chance"] = 10 } : null);
                MarkDirty();
            }, () => BuildShipPage());
            if (lounge != null)
            {
                var box = Add(ship, new VisualElement(), "mbv-page-box");
                Int(box, T("mbChancePct", "Chance (%)"), JInt(lounge["chance"]) ?? 10, 0, 100, v => { lounge["chance"] = v; MarkDirty(); });
                Segments(box, T("mbInSystemsOf", "In systems of"), RaceChoices.Select(r => T("mbRace" + r, r)).ToArray(), RaceIndex(JInt(lounge["systemRace"]) ?? -1),
                         n => { lounge["systemRace"] = n - 1; MarkDirty(); BuildShipPage(); });
                Int(box, T("mbMinCampaign", "From story step"), JInt(lounge["minCampaign"]) ?? 0, 0, 162, v => { lounge["minCampaign"] = v; MarkDirty(); });
                Int(box, T("mbMinRank", "In free play from rank"), JInt(lounge["minRank"]) ?? 0, 0, 50, v => { lounge["minRank"] = v; MarkDirty(); });
            }

            // The turret items NPC ships of this type carry.
            Heading(ship, T("mbNpcTurrets", "NPC turrets"));
            var turrets = (s.Get("npcTurrets") as JArray)?.Select(ProjectEntry.RefText).Where(x => x != null).ToList() ?? new List<string>();
            for (int i = 0; i < turrets.Count; i++)
            {
                int n = i;
                var trow = Row(ship);
                Add(trow, new Label($"{n + 1}. {RefLabel(RefKind.Item, turrets[n], turrets[n])}"), "mbv-inline-label");
                Act(trow, "×", () => { turrets.RemoveAt(n); s.Set("npcTurrets", turrets.Count > 0 ? new JArray(turrets.Select(ProjectEntry.RefToken)) : null); MarkDirty(); BuildShipPage(); }, "mbv-step");
            }
            Act(ship, T("mbAddNpcTurret", "+ Turret for NPC ships"), () => PickRef(RefKind.Item, T("mbNpcTurrets", "NPC turrets"), e =>
            {
                if (e == null) return;
                turrets.Add(e.value);
                s.Set("npcTurrets", new JArray(turrets.Select(ProjectEntry.RefToken)));
                MarkDirty();
                BuildShipPage();
            }, e => int.TryParse(e.value, out int idx) ? Database.Shared.Item(idx)?.categoryId == 8 : true), "mbv-add");
            Note(ship, T("mbNpcTurretsNote", "The n-th turret item goes on NPC ships' n-th turret mount."));
            // Remake: traffic "races", the game's or the mods' own races whose fighters fly it (ModBuilderViewRaces).
            RaceListField(ship, T("mbModRaceTraffic", "Flown by the NPC fighters of"), s.TrafficRaces, l => { s.TrafficRaces = l; MarkDirty(); BuildShipPage(); },
                          T("mbModRaceTrafficNone", "Its own race's (with the Gameplay option \"Mod ships in traffic\"; a mod race's fighters always fly mods' ships)."),
                          numbers: true);

            // The engine flame.
            Heading(ship, T("mbEngineFlame", "Engine flame"));
            Float(ship, T("mbGlowRadius", "Flame size (game units; 24 by default)"), JFloat(s.Get("engineGlowRadius")) ?? 24f, 1f, 2000f,
                  v => { s.Set("engineGlowRadius", Mathf.Approximately(v, 24f) ? null : (JToken)Math.Round(v, 2)); MarkDirty(); RequestReload(true); });
            var colour = s.Get("engineGlowColor") as JArray;
            FlagButton(Row(ship), T("mbGlowColour", "Its own flame colour"), colour != null, v =>
            {
                s.Set("engineGlowColor", v ? new JArray(0.4, 0.7, 1.0) : null);
                MarkDirty();
                RequestReload(true);
            }, () => BuildShipPage());
            if (colour != null && colour.Count >= 3)
            {
                var crow = Row(ship, "mbv-xyz");
                for (int k = 0; k < 3; k++)
                {
                    int c = k;
                    Float(crow, "RGB".Substring(c, 1), (float)colour[c], 0f, 1f, v => { colour[c] = Math.Round(v, 3); MarkDirty(); RequestReload(true); });
                }
            }
            Note(ship, T("mbMoreJsonNote", "Per-mesh materials and glows: Edit as JSON (top)."));

            BuildShipView(s);
        }

        /// <summary>How the ship is seen (ModShipView): the chase camera, the cockpit view's seat, the damage smoke's points
        /// and the explosion's size. Points in game units like the hardpoints (x the pilot's left, y up, z the nose).</summary>
        void BuildShipView(ProjectShip s)
        {
            Heading(ship, T("mbShipView", "Camera and damage"));
            var cam = s.Get("camera") as JObject;
            FlagButton(Row(ship), T("mbOwnCamera", "Its own chase camera"), cam != null, v =>
            {
                var d = ModShipView.DefaultCamera;
                s.Set("camera", v ? new JObject { ["distance"] = d[0], ["height"] = d[1], ["lookHeight"] = d[2], ["lookAhead"] = d[3] } : null);
                MarkDirty();
            }, () => BuildShipPage());
            if (cam != null)
            {
                var box = Add(ship, new VisualElement(), "mbv-page-box");
                string[] keys = { "distance", "height", "lookHeight", "lookAhead" };
                string[] labels =
                {
                    T("mbCamDistance", "Distance behind (game units; 1338 by default)"), T("mbCamHeight", "Height (600)"),
                    T("mbCamLookHeight", "Looks at a point this high (600)"), T("mbCamLookAhead", "That point ahead of the ship (-650: behind it)"),
                };
                for (int k = 0; k < keys.Length; k++)
                {
                    string key = keys[k];
                    Float(box, labels[k], JFloat(cam[key]) ?? ModShipView.DefaultCamera[k], k == 0 ? 50f : -50000f, 50000f,
                          v => { cam[key] = Math.Round(v); MarkDirty(); });
                }
            }

            PointsField(s, "cockpit", T("mbOwnCockpit", "Its own cockpit seat"), single: true);
            PointsField(s, "smoke", T("mbSmokePoints", "Damage smoke points"), single: false);

            Float(ship, T("mbExplosionScale", "Explosion size (1 = a fighter's)"), JFloat(s.Get("explosionScale")) ?? 1f, ModShipView.MinExplosion, ModShipView.MaxExplosion,
                  v => { s.Set("explosionScale", Mathf.Approximately(v, 1f) ? null : (JToken)Math.Round(v, 2)); MarkDirty(); });
        }

        /// <summary>A ships.json point ("cockpit": [x, y, z]) or list of points ("smoke": [[x, y, z], ...]).</summary>
        void PointsField(ProjectShip s, string key, string label, bool single)
        {
            var points = new List<JArray>();
            if (single) { if (s.Get(key) is JArray one && one.Count == 3) points.Add(one); }
            else if (s.Get(key) is JArray list) points.AddRange(list.OfType<JArray>().Where(a => a.Count == 3));
            void Store()
            {
                if (single) s.Set(key, points.Count > 0 ? points[0] : null);
                else s.Set(key, points.Count > 0 ? new JArray(points) : null);
                MarkDirty();
            }
            if (single)
                FlagButton(Row(ship), label, points.Count > 0, v =>
                {
                    points.Clear();
                    if (v) points.Add(new JArray(0, 0, 0));
                    Store();
                }, () => BuildShipPage());
            else Add(ship, new Label(label), "mbv-inline-label");
            for (int i = 0; i < points.Count; i++)
            {
                int n = i;
                var prow = Row(ship, "mbv-xyz");
                for (int k = 0; k < 3; k++)
                {
                    int c = k;
                    Float(prow, "XYZ".Substring(c, 1), JFloat(points[n][c]) ?? 0f, -100000f, 100000f, v => { points[n][c] = Math.Round(v); Store(); });
                }
                if (!single) Act(prow, "×", () => { points.RemoveAt(n); Store(); BuildShipPage(); }, "mbv-step");
            }
            if (!single && points.Count < ModShipView.MaxSmokePoints)
                Act(ship, T("mbAddSmokePoint", "+ Smoke point"), () => { points.Add(new JArray(0, 0, 0)); Store(); BuildShipPage(); }, "mbv-add");
            if (points.Count > 0) Note(ship, T("mbPointsNote", "Game units like the hardpoints: X the pilot's left, Y up, Z the nose."));
        }
    }
}
