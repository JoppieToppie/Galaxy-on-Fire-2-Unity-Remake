// Host-owned inventory snapshot, independent of the vanilla shop list's lifetime.
using System;
using System.Collections.Generic;
using System.IO;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class AdvancedEconomyLedger
    {
        public sealed class Balance
        {
            public int Amount { get; private set; }
            public int Resale { get; private set; }
            public Balance(int amount, int resale, int maximum) => Set(amount, resale, maximum);
            public bool Set(int amount, int resale, int maximum)
            {
                amount = Math.Clamp(amount, 0, maximum);
                resale = Math.Clamp(resale, 0, amount);
                bool changed = amount != Amount || resale != Resale;
                Amount = amount;
                Resale = resale;
                return changed;
            }
        }

        // Stable mod keys, not allocated runtime indices; zero is a real saved balance.
        readonly struct Key : IEquatable<Key>
        {
            public readonly string Station, Item;
            public Key(string station, string item) { Station = station; Item = item; }
            public bool Equals(Key other) => Station == other.Station && Item == other.Item;
            public override bool Equals(object other) => other is Key key && Equals(key);
            public override int GetHashCode() => (Station, Item).GetHashCode();
        }

        static readonly Dictionary<Key, Balance> balances = new Dictionary<Key, Balance>();
        static readonly Dictionary<(int station, int item), Balance> live = new Dictionary<(int, int), Balance>();
        static bool loaded, dirty, readFailed;
        static float nextSave;
        static string path;
        public static bool Active => ModAdvancedEconomy.Enabled && ModAdvancedEconomy.PersistentInventory &&
            NetGame.Active && !NetGame.CoopSession && !CoopCampaign.Active;

        internal static void Reset()
        {
            balances.Clear();
            live.Clear();
            loaded = dirty = readFailed = false;
            path = null;
            nextSave = 0;
        }

        static Key StableKey(int station, int item) => new Key(
            ModWorld.StationKey(station) ?? station.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ModContent.ItemKey(item) ?? item.ToString(System.Globalization.CultureInfo.InvariantCulture));

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            if (!ModAdvancedEconomy.SaveInventory || !NetProfiles.Enabled) return;
            path = Path.Combine(NetProfiles.Folder, "advanced-economy-stock.json");
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return;
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    var parsed = Read(File.ReadAllText(candidate));
                    foreach (var pair in parsed) balances.Add(pair.Key, pair.Value);
                    if (candidate != path)
                    {
                        Debug.LogWarning("Advanced economy: recovered the backup snapshot.");
                        dirty = true;
                    }
                    return;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                    e is JsonException || e is ArgumentException || e is OverflowException)
                {
                    Debug.LogError($"Advanced economy: cannot read {candidate}: {e.Message}");
                }
            }
            readFailed = true;
            Debug.LogError("Advanced economy: snapshots are unreadable. Disk saving is suspended for this session to preserve recovery data.");
        }

        static Dictionary<Key, Balance> Read(string json)
        {
            var root = JObject.Parse(json);
            if (root["version"]?.Type != JTokenType.Integer || (long)root["version"] != 1 ||
                !(root["rows"] is JArray rows))
                throw new JsonException("Unsupported advanced economy snapshot.");
            var result = new Dictionary<Key, Balance>();
            foreach (var token in rows)
            {
                if (!(token is JObject row) || row["station"]?.Type != JTokenType.String ||
                    row["item"]?.Type != JTokenType.String || row["amount"]?.Type != JTokenType.Integer ||
                    row["resale"]?.Type != JTokenType.Integer)
                    throw new JsonException("Invalid advanced economy stock row.");
                string station = (string)row["station"], item = (string)row["item"];
                long amount = (long)row["amount"], resale = (long)row["resale"];
                if (string.IsNullOrEmpty(station) || string.IsNullOrEmpty(item) || amount < 0 ||
                    amount > ModAdvancedEconomy.MaxStock || resale < 0 || resale > amount)
                    throw new JsonException("Invalid advanced economy stock balance.");
                var key = new Key(station, item);
                if (result.ContainsKey(key)) throw new JsonException("Duplicate advanced economy stock row.");
                result.Add(key, new Balance((int)amount, (int)resale, ModAdvancedEconomy.MaxStock));
            }
            return result;
        }

        internal static Balance Get(int station, ModAdvancedEconomy.Rule rule)
        {
            Load();
            var runtimeKey = (station, rule.Item);
            if (live.TryGetValue(runtimeKey, out var balance)) return balance;
            var stable = StableKey(station, rule.Item);
            if (!balances.TryGetValue(stable, out balance))
            {
                balance = new Balance(rule.Initial, 0, rule.Maximum);
                balances.Add(stable, balance);
                dirty = true;
            }
            else if (balance.Set(balance.Amount, balance.Resale, rule.Maximum)) dirty = true;
            live[runtimeKey] = balance;
            return balance;
        }

        internal static void Set(int station, ModAdvancedEconomy.Rule rule, int amount, int resale)
        {
            if (Get(station, rule).Set(amount, resale, rule.Maximum)) dirty = true;
        }

        internal static void Flush(bool force = false)
        {
            if (!loaded || !dirty || readFailed || path == null || !Active ||
                !ModAdvancedEconomy.SaveInventory || (!force && Time.unscaledTime < nextSave)) return;
            nextSave = Time.unscaledTime + 10f;
            var rows = new JArray();
            foreach (var pair in balances)
                rows.Add(new JObject { ["station"] = pair.Key.Station, ["item"] = pair.Key.Item,
                    ["amount"] = pair.Value.Amount, ["resale"] = pair.Value.Resale });
            var root = new JObject { ["version"] = 1, ["rows"] = rows };
            if (NetProfiles.Write(path, root.ToString(Formatting.None))) dirty = false;
        }
    }
}
