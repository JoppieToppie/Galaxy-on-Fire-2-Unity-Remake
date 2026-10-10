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
            new Setting { key = "password", option = "-password", label = "Password (\"-\" = none)", kind = Kind.Password, note = "the browser's tag after a restart",
                          get = () => NetGame.HasPassword ? "set" : "none", apply = v => NetGame.HostPassword = v == "-" ? "" : NetGame.CleanPassword(v) },
            Num("maxplayers", "-maxplayers", "Player limit", 2, NetGame.MaxPlayersLimit, () => NetGame.MaxPlayers, v => NetGame.MaxPlayers = v,
                "online, a higher limit than at the start needs a restart"),
            Flag("allowdebug", "-allowdebug", "Players may use the Debug menu", () => NetGame.HostAllowsDebug,
                 v => { NetGame.HostAllowsDebug = v; NetState.Instance?.SetDebugAllowed(v); }),
            Flag("freepvp", "-freepvp", "Players may fight anywhere", () => NetGame.FreePvp,
                 v => { NetGame.FreePvp = v; NetState.Instance?.SetFreePvp(v); }),
            Num("maxearn", "-maxearn", "Worth a profile may gain per minute", 0, int.MaxValue, () => NetProfiles.EarnPerMinute, v => NetProfiles.EarnPerMinute = v),
            Num("claimcost", "-claimcost", "Faction claim cost", 0, int.MaxValue, () => NetFactions.ClaimCost, v => NetFactions.ClaimCost = v),
            Num("maxclaims", "-maxclaims", "Stations per faction", 0, 100, () => NetFactions.MaxClaims, v => NetFactions.MaxClaims = v),
            Num("claimdays", "-claimdays", "Days before an unvisited claim lapses", 1, 3650, () => NetFactions.LapseDays, v => NetFactions.LapseDays = v),
            Num("siegecost", "-siegecost", "Siege cost", 0, int.MaxValue, () => NetFactions.SiegeCost, v => NetFactions.SiegeCost = v),
            Num("toll", "-toll", "Toll at a faction's station (0 = none)", 0, int.MaxValue, () => NetFactions.Toll, v => { NetFactions.Toll = v; NetState.Instance?.SetToll(v); }),
        });

        /// <summary>NetRules: the session's gameplay options, after free PvP ("on" = the option on, whatever its flag).</summary>
        static List<Setting> WithRules(List<Setting> list)
        {
            int at = list.FindIndex(x => x.key == "freepvp") + 1;
            foreach (var r in NetRules.All)
            {
                int bit = r.bit;
                list.Insert(at++, Flag(r.key, r.flag, r.label, () => (NetRules.Host & bit) != 0, v => NetRules.Set(bit, v)));
            }
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
            saved = null;
            try { if (PathOf != null && File.Exists(PathOf)) saved = JsonUtility.FromJson<Saved>(File.ReadAllText(PathOf)); }
            catch (Exception e) { Debug.LogError($"Server: server_settings.json unreadable ({e.Message}); the defaults and the command line only."); }
            if (saved == null) saved = new Saved();
            if (saved.values == null) saved.values = new List<Entry>();
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
                    break;
            }
            s.apply(value);
            Remember(s.key, value);
            Debug.Log($"Server: {by} set {s.key} to {(s.kind == Kind.Password ? (value == "-" ? "none" : "(hidden)") : s.get())}.");
            string answer = string.Format(Localization.Extra("mpSetDone", "{0} is {1} now."), s.label, s.kind == Kind.Password ? s.get() : s.get());
            if (s.note != null) answer += " (" + s.note + ")";
            if (fromCommandLine.Contains(s.key)) answer += " " + string.Format(Localization.Extra("mpSetCli", "The launcher's command line sets it too ({0}): change it there as well, or the next start puts that back."), s.option);
            return answer;
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
