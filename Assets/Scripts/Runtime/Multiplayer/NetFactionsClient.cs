// NetFactionsClient.cs
// Remake-only: a player's side of the factions (NetFactions): who holds which station (NetState.Claims, read again when it
// changes) for the star map, the station's header and the orbit information; the faction bank's deposits (the server asks,
// the game pays from its credits and answers) and payouts; the home station a destroyed member respawns at
// (NetPlayer.FactionHome). Phase 3: the sieges (NetState.Sieges: who may fire at whom in a besieged orbit, the HUD's
// banner), the toll this pilot paid for the current visit (TollStation, shown to the others by NetPlayer), the station
// defence's verdict on a pilot (Relation: the held station's own race's fighters), and the trade cut (BuyPrice).

using System.Collections.Generic;
using GoF2Remake.Data;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetFactionsClient
    {
        static string parsedFrom;
        static readonly Dictionary<int, (string tag, string name)> owners = new Dictionary<int, (string, string)>();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { parsedFrom = siegesFrom = null; owners.Clear(); sieges.Clear(); TollStation = -1; }

        /// <summary>A siege as the players see it: the seconds left counted from when the line arrived.</summary>
        public sealed class SiegeInfo { public int station, control; public string attacker, defender; public bool started; public float secondsLeft, receivedAt; }

        static string siegesFrom;
        static readonly Dictionary<int, SiegeInfo> sieges = new Dictionary<int, SiegeInfo>();

        static void RefreshSieges()
        {
            string text = NetGame.Active && NetState.Instance != null ? NetState.Instance.Sieges : "";
            if (text == siegesFrom) return;
            siegesFrom = text;
            sieges.Clear();
            foreach (var line in text.Split('\n'))
            {
                var p = line.Split('|');
                if (p.Length < 6 || !int.TryParse(p[0], out int station)) continue;
                int.TryParse(p[4], out int left);
                int.TryParse(p[5], out int control);
                sieges[station] = new SiegeInfo { station = station, attacker = p[1], defender = p[2], started = p[3] == "1",
                                                  secondsLeft = left, control = control, receivedAt = UnityEngine.Time.unscaledTime };
            }
        }

        /// <summary>The siege of 'station', null = none.</summary>
        public static SiegeInfo SiegeAt(int station)
        {
            RefreshSieges();
            return sieges.TryGetValue(station, out var s) ? s : null;
        }

        /// <summary>Two pilots in 'station''s orbit may fire at each other: its siege runs and they are of its two factions.</summary>
        public static bool SiegePvp(int station, string tagA, string tagB)
        {
            var s = SiegeAt(station);
            if (s == null || !s.started || string.IsNullOrEmpty(tagA) || string.IsNullOrEmpty(tagB)) return false;
            return (tagA == s.attacker && tagB == s.defender) || (tagA == s.defender && tagB == s.attacker);
        }

        /// <summary>The station this pilot paid the toll at for the current visit (-1 = none; TerritoryView).</summary>
        public static int TollStation { get; set; } = -1;

        /// <summary>The held station's defence toward a pilot ('tag' their faction, 'tollAt' where they paid): +1 friend (a
        /// member), -1 enemy (another faction's pilot without the toll), 0 as always (no holder, no faction, the toll paid).</summary>
        public static int Relation(int station, string tag, int tollAt)
        {
            if (string.IsNullOrEmpty(tag) || !Owner(station, out string owner, out _)) return 0;
            if (tag == owner) return 1;
            return tollAt == station ? 0 : -1;
        }

        /// <summary>The trade cut on a unit's list price at 'station' (multiplayer, a held station): members pay
        /// NetFactions.MemberDiscountPercent less, other factions' pilots NetFactions.TaxPercent more (the holder's tax).</summary>
        public static int BuyPrice(int station, int price)
        {
            if (price <= 0 || NetPlayer.Local == null) return price;
            string tag = NetPlayer.Local.FactionTag;
            if (string.IsNullOrEmpty(tag) || !Owner(station, out string owner, out _)) return price;
            return tag == owner ? UnityEngine.Mathf.RoundToInt(price * (100 - NetFactions.MemberDiscountPercent) / 100f)
                                : UnityEngine.Mathf.RoundToInt(price * (100 + NetFactions.TaxPercent) / 100f);
        }

        /// <summary>What a sale at 'station' pays: the list price, never more than this player pays there (a member buying at
        /// the member's cut and selling at the full price made 10 % on every round trip).</summary>
        public static int SellPrice(int station, int price) => UnityEngine.Mathf.Min(price, BuyPrice(station, price));

        /// <summary>The toll for this station's holder: the credits go, the server banks them.</summary>
        public static bool PayToll(int station)
        {
            int toll = NetState.Instance != null ? NetState.Instance.Toll : 0;
            if (toll <= 0 || Session.Credits < toll) return false;
            Session.Credits -= toll;
            TollStation = station;
            NetState.Instance.TollPaidRpc(station);
            NetProfileClient.Upload();
            return true;
        }

        static void Refresh()
        {
            string text = NetGame.Active && NetState.Instance != null ? NetState.Instance.Claims : "";
            if (text == parsedFrom) return;
            parsedFrom = text;
            owners.Clear();
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split('|');
                if (parts.Length >= 3 && int.TryParse(parts[0], out int station)) owners[station] = (parts[1], parts[2]);
            }
        }

        /// <summary>The faction holding 'station' (its tag and name), false = nobody (or no session).</summary>
        public static bool Owner(int station, out string tag, out string name)
        {
            Refresh();
            if (owners.TryGetValue(station, out var o)) { tag = o.tag; name = o.name; return true; }
            tag = name = null;
            return false;
        }

        /// <summary>"[TAG] Name" of the station's faction, "" = none.</summary>
        public static string OwnerText(int station) => Owner(station, out string tag, out string name) ? $"[{tag}] {name}" : "";

        /// <summary>The first faction tag holding a station in 'system' (the star map's galaxy view), null = none.</summary>
        public static string SystemTag(Database db, int system)
        {
            Refresh();
            foreach (var pair in owners)
            {
                var st = db.Stations.Find(s => s.index == pair.Key);
                if (st != null && st.system == system) return pair.Value.tag;
            }
            return null;
        }

        /// <summary>The local player's faction home (respawn), -1 = none.</summary>
        public static int Home => NetPlayer.Local != null ? NetPlayer.Local.FactionHome : -1;

        /// <summary>The server asks for a faction deposit: paid from the credits if there are enough; the answer either way.</summary>
        internal static void OnCharge(int token, int amount)
        {
            bool paid = amount > 0 && Session.Credits >= amount;
            if (paid) Session.Credits -= amount;
            NetState.Instance?.ChargedRpc(token, paid);
            if (paid) { NetProfileClient.Upload(); RefreshStationCredits(); }
        }

        static void RefreshStationCredits() => UnityEngine.Object.FindAnyObjectByType<UI.StationMenu>()?.RefreshCredits();

        /// <summary>Credits from the faction bank.</summary>
        internal static void OnGrant(int amount)
        {
            if (amount <= 0) return;
            Session.Credits += amount;
            NetChat.Notice(string.Format(Localization.Extra("mpFactionGranted", "+{0:N0} credits from the faction bank."), amount));
            NetProfileClient.Upload();
            RefreshStationCredits();
        }
    }
}
