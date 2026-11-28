// NetServerSettings.cs
// Remake-only: a dedicated server's settings that can change while it runs: from the game (the station window's Admin tab,
// or "/set <key> <value>" in the chat, admins; "/settings" lists them) and the console ("set", "settings"). A change applies
// at once (where the setting allows it, Note says when not) and is saved to server_settings.json beside the profiles
// (NetProfiles.Folder), so it holds after a restart. At a start the order is: the defaults, then the file, then the
// command line: an option given on the command line wins (FromCommandLine; the window says a change of it lasts until the
// next restart, unless the launcher's line is changed too).
// The password is never shown: the window and the lists say whether one is set.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetServerSettings
    {
        public enum Kind { Number, Toggle, Text, Password }

        /// <summary>One setting: its key ("/set key"), the command-line option it mirrors, how to read and apply it.</summary>
        public sealed class Setting
        {
            public string key, option, label, note;
            public Kind kind;
            public int min, max;
            public Func<string> get;
            public Action<string> apply;
        }

        [Serializable] class Entry { public string key = "", value = ""; }
        [Serializable] class Saved { public List<Entry> values = new List<Entry>(); }

        static Saved saved;
        static readonly HashSet<string> fromCommandLine = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { saved = null; fromCommandLine.Clear(); }

        static Setting Num(string key, string option, string label, int min, int max, Func<int> get, Action<int> set, string note = null) =>
            new Setting { key = key, option = option, label = label, kind = Kind.Number, min = min, max = max, note = note,
                          get = () => get().ToString(), apply = v => set(Mathf.Clamp(int.Parse(v), min, max)) };

        static Setting Flag(string key, string option, string label, Func<bool> get, Action<bool> set, string note = null) =>
            new Setting { key = key, option = option, label = label, kind = Kind.Toggle, note = note,
                          get = () => get() ? "on" : "off", apply = v => set(v == "on") };

        /// <summary>Every setting, in the window's order.</summary>
        public static readonly List<Setting> All = WithRules(new List<Setting>
        {
            new Setting { key = "name", option = "-name", label = "Name in the server browser", kind = Kind.Text, note = "after a restart",
                          get = () => DedicatedServer.ListName, apply = v => DedicatedServer.ListName = NetGame.Clean(v).Length > 0 ? v.Trim() : DedicatedServer.DefaultListName },
            new Setting { key = "password", option = "-password", label = "Password", kind = Kind.Password, note = "\"-\" = none; the browser's tag after a restart",
                          get = () => NetGame.HasPassword ? "set" : "none", apply = v => NetGame.HostPassword = v == "-" ? "" : NetGame.CleanPassword(v) },
            Num("maxplayers", "-maxplayers", "Player limit", 2, NetGame.MaxPlayersLimit, () => NetGame.MaxPlayers, v => NetGame.MaxPlayers = v,
                "online: a higher limit needs a restart"),
            Flag("allowdebug", "-allowdebug", "Players may use the Debug menu", () => NetGame.HostAllowsDebug,
                 v => { NetGame.HostAllowsDebug = v; NetState.Instance?.SetDebugAllowed(v); }),
            Flag("freepvp", "-freepvp", "Players may fight anywhere", () => NetGame.FreePvp,
                 v => { NetGame.FreePvp = v; NetState.Instance?.SetFreePvp(v); }),
            Num("maxearn", "-maxearn", "Worth a profile may gain per minute", 0, int.MaxValue, () => NetProfiles.EarnPerMinute, v => NetProfiles.EarnPerMinute = v),
            Num("claimcost", "-claimcost", "Faction claim cost", 0, int.MaxValue, () => NetFactions.ClaimCost, v => NetFactions.ClaimCost = v),
            Num("maxclaims", "-maxclaims", "Stations per faction", 0, 100, () => NetFactions.MaxClaims, v => NetFactions.MaxClaims = v),
            Num("claimdays", "-claimdays", "Days before an unvisited claim lapses", 1, 3650, () => NetFactions.LapseDays, v => NetFactions.LapseDays = v),
            Num("siegecost", "-siegecost", "Siege cost", 0, int.MaxValue, () => NetFactions.SiegeCost, v => NetFactions.SiegeCost = v),
            new Setting { key = "toll", option = "-toll", label = "Toll at faction stations", kind = Kind.Text, note = "credits, or N% of the pilot's credits; 0 = none",
                          get = () => NetFactions.TollText(NetFactions.Toll),
                          apply = v => { int t = NetFactions.ParseToll(v) ?? throw new FormatException(); NetFactions.Toll = t; NetState.Instance?.SetToll(t); } },
        });

        /// <summary>NetRules: the session's gameplay options, after free PvP ("on" = the option on, whatever its flag).</summary>
        static List<Setting> WithRules(List<Setting> list)
        {
            int at = list.FindIndex(x => x.key == "freepvp") + 1;
            foreach (var r in NetRules.All)
            {
                int bit = r.bit;
                list.Insert(at++, Flag(r.key, r.flag, r.label, () => (NetRules.Host & bit) != 0, v => NetRules.Set(bit, v)));
                if (bit == NetRules.KaamoStacking)
                    list.Insert(at++, Num(NetRules.KaamoLevelKey, NetRules.KaamoLevelFlag, NetRules.KaamoLevelLabel,
                        SessionRules.MinKaamoLevel, SessionRules.MaxKaamoLevel, () => NetRules.HostKaamoLevel, NetRules.SetKaamoLevel,
                        "higher profiles are cut back at sign-in"));
            }
            list.Insert(at++, new Setting
            {
                key = NetRules.FlightKey, option = NetRules.FlightFlag, label = NetRules.FlightLabel, kind = Kind.Text,
                get = () => SessionRules.FlightText(NetRules.HostFlight),
                apply = v => { int s = SessionRules.ParseFlight(v); if (s < -1) throw new FormatException(); NetRules.SetFlight(s); },
            });
            list.Insert(at++, new Setting
            {
                key = NetRules.CollisionKey, option = NetRules.CollisionFlag, label = NetRules.CollisionLabel, kind = Kind.Text,
                get = () => SessionRules.CollisionText(NetRules.HostCollision),
                apply = v => { int c = SessionRules.ParseCollision(v); if (c < 0) throw new FormatException(); NetRules.SetCollision(c); },
            });
            return list;
        }

        static string PathOf => string.IsNullOrEmpty(NetProfiles.Folder) ? null : Path.Combine(NetProfiles.Folder, "server_settings.json");

        /// <summary>The setting was given on the command line this run (it wins over the file at every start).</summary>
        public static bool FromCommandLine(Setting s) => fromCommandLine.Contains(s.key);

        /// <summary>DedicatedServer.Boot, after the command line: the saved values for every setting the command line
        /// didn't give.</summary>
        public static void Load(Func<string, bool> onCommandLine)
        {
            fromCommandLine.Clear();
            foreach (var s in All) if (onCommandLine(s.option)) fromCommandLine.Add(s.key);
            if (onCommandLine(NetRules.LegacyCollisionFlag)) fromCommandLine.Add(NetRules.CollisionKey);   // the old toggle's flag
            saved = null;
            try { if (PathOf != null && File.Exists(PathOf)) saved = JsonUtility.FromJson<Saved>(File.ReadAllText(PathOf)); }
            catch (Exception e) { Debug.LogError($"Server: server_settings.json unreadable ({e.Message}); the defaults and the command line only."); }
            if (saved == null) saved = new Saved();
            if (saved.values == null) saved.values = new List<Entry>();
            // The old toggle (2026-10-17): "precisecollision" on = exact, unless the mode itself was saved.
            var legacy = saved.values.Find(e => e.key == "precisecollision");
            if (legacy != null && legacy.value == "on" && !saved.values.Exists(e => e.key == NetRules.CollisionKey) && !fromCommandLine.Contains(NetRules.CollisionKey))
                NetRules.SetCollision(SessionRules.CollisionExact);
            foreach (var e in saved.values)
            {
                var s = All.Find(x => x.key == e.key);
                if (s == null || fromCommandLine.Contains(s.key)) continue;
                try { s.apply(e.value); }
                catch (Exception) { Debug.LogWarning($"Server: the saved setting {e.key} = \"{e.value}\" isn't valid; skipped."); }
            }
        }

        /// <summary>"/set key value" (admins) and the console's set: checked, applied, saved. The answer for the sender.</summary>
        public static string Set(string key, string value, string by)
        {
            var s = All.Find(x => string.Equals(x.key, (key ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            if (s == null) return Localization.Extra("mpSetUnknown", "Unknown setting. /settings lists them.");
            if (Check(s, ref value) is string problem) return problem;
            s.apply(value);
            Remember(s.key, value);
            Debug.Log($"Server: {by} set {s.key} to {(s.kind == Kind.Password ? (value == "-" ? "none" : "(hidden)") : s.get())}.");
            string answer = string.Format(Localization.Extra("mpSetDone", "{0} is {1} now."), s.label, s.kind == Kind.Password ? s.get() : s.get());
            if (s.note != null) answer += " (" + s.note + ")";
            if (fromCommandLine.Contains(s.key)) answer += " " + string.Format(Localization.Extra("mpSetCli", "The launcher's command line sets it too ({0}): change it there as well, or the next start puts that back."), s.option);
            return answer;
        }

        /// <summary>'value' made the setting's own form (a number clamped, on / off, the toll's, a mode's name); the problem
        /// for the player when it isn't one, else null.</summary>
        static string Check(Setting s, ref string value)
        {
            value = (value ?? "").Trim();
            switch (s.kind)
            {
                case Kind.Number:
                    if (!long.TryParse(value.Replace(",", "").Replace(" ", ""), out long n)) return string.Format(Localization.Extra("mpSetNumber", "{0} needs a number."), s.key);
                    value = Math.Max(s.min, Math.Min(s.max, n)).ToString();
                    break;
                case Kind.Toggle:
                    value = value.ToLowerInvariant();
                    value = value == "on" || value == "1" || value == "true" || value == "yes" ? "on" : value == "off" || value == "0" || value == "false" || value == "no" ? "off" : null;
                    if (value == null) return string.Format(Localization.Extra("mpSetToggle", "{0} is on or off."), s.key);
                    break;
                default:
                    if (value.Length == 0) return string.Format(Localization.Extra("mpSetText", "/set {0} <value>"), s.key);
                    if (s.key == "toll")
                    {
                        if (NetFactions.ParseToll(value) is not int t) return Localization.Extra("mpSetToll", "toll is a number of credits or a percentage (0-100%).");
                        value = NetFactions.TollText(t);
                    }
                    if (s.key == NetRules.CollisionKey)
                    {
                        int c = SessionRules.ParseCollision(value);
                        if (c < 0) return Localization.Extra("mpSetCollision", "shipcollision is exact, mixed or original.");
                        value = SessionRules.CollisionText(c);
                    }
                    if (s.key == NetRules.FlightKey)
                    {
                        int f = SessionRules.ParseFlight(value);
                        if (f < SessionRules.PlayerFlight) return Localization.Extra("mpSetFlight", "flightmodel is gof2, es2 or player.");
                        value = SessionRules.FlightText(f);
                    }
                    break;
            }
            return null;
        }

        // ---- a player host's choices (the main menu's Session settings window) ------------------------------------------

        /// <summary>The settings the Session settings window lists: all but the Host card's own (name, password, player
        /// limit, Combat) and the Debug switch (the window's own row, MainMenu).</summary>
        public static List<Setting> HostWindow => All.FindAll(s => s.key != "name" && s.key != "password" && s.key != "maxplayers"
                                                                  && s.key != "allowdebug" && s.key != "freepvp");

        const string HostChoicePrefix = "mp_set_";

        /// <summary>What the player chose for the setting in the window (PlayerPrefs "mp_set_<key>"), null = left alone.</summary>
        public static string HostChoice(Setting s) => PlayerPrefs.HasKey(HostChoicePrefix + s.key) ? PlayerPrefs.GetString(HostChoicePrefix + s.key) : null;

        /// <summary>The window: keeps the choice for the next hosting (checked as /set checks it); the problem, else null.</summary>
        public static string ChooseForHost(Setting s, string value)
        {
            if (Check(s, ref value) is string problem) return problem;
            PlayerPrefs.SetString(HostChoicePrefix + s.key, value);
            return null;
        }

        /// <summary>The window's Reset: every choice back to its start value.</summary>
        public static void ClearHostChoices()
        {
            foreach (var s in HostWindow) PlayerPrefs.DeleteKey(HostChoicePrefix + s.key);
        }

        /// <summary>The window's shown value: the choice, else what hosting starts with (the rules from the player's options
        /// and the server defaults, NetRules.FromOptions; the other settings' own defaults).</summary>
        public static string HostValue(Setting s)
        {
            if (HostChoice(s) is string chosen) return chosen;
            if (!NetGame.Active)
            {
                NetRules.Host = NetRules.FromOptions(null, out int level);   // StartHost sets them again
                NetRules.HostKaamoLevel = level;
                NetRules.HostFlight = SessionRules.DefaultFlight;
            }
            return s.get();
        }

        /// <summary>NetGame.StartHost (a player host), after its rules and a persistent world's saved settings: the window's
        /// choices win, as the Host card's do.</summary>
        public static void ApplyHostChoices()
        {
            foreach (var s in HostWindow)
            {
                if (!(HostChoice(s) is string v)) continue;
                try { s.apply(v); }
                catch (Exception) { Debug.LogWarning($"NetGame: the session setting {s.key} = \"{v}\" isn't valid; skipped."); }
            }
        }

        static void Remember(string key, string value)
        {
            if (saved == null) saved = new Saved();
            saved.values.RemoveAll(e => e.key == key);
            saved.values.Add(new Entry { key = key, value = value });
            string path = PathOf;
            if (path == null) return;
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); } catch (Exception) { }
            NetProfiles.Write(path, JsonUtility.ToJson(saved, true));
        }

        /// <summary>/settings and the console's settings: every setting with its value.</summary>
        public static string ListText()
        {
            var sb = new StringBuilder(Localization.Extra("mpSettingsTitle", "Server settings (/set <key> <value>):"));
            foreach (var s in All)
                sb.Append($"\n{s.key} = {s.get()}  ({s.label}{(fromCommandLine.Contains(s.key) ? ", from the command line" : "")})");
            return sb.ToString();
        }

        /// <summary>The admins' window: the settings.</summary>
        internal static void FillPanel(NetPanel.State st)
        {
            foreach (var s in All)
                st.settings.Add(new NetPanel.SettingRow { key = s.key, label = s.label, value = s.kind == Kind.Password ? "" : s.get(), passwordSet = s.kind == Kind.Password && NetGame.HasPassword,
                                                          kind = (int)s.kind, cli = fromCommandLine.Contains(s.key), note = s.note ?? "" });
        }
    }
}
