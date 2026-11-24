// NetFactionsTolls.cs
// NetFactions: the station's toll and trade tax, the station window, the server console (part of the partial NetFactions; its fields are in NetFactions.cs).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetFactions
    {
        // Stock trades may finish while an unrelated station journal awaits recovery.
        // Keep their tax separate until the journal's absolute treasury targets are settled.
        static readonly Dictionary<string, long> deferredStationTax = new Dictionary<string, long>(StringComparer.Ordinal);
        static void CreditStationTax(Faction faction, long amount)
        {
            if (faction == null || amount <= 0) return;
            if (NetPlayerStations.ProfileWritesBlocked)
            {
                deferredStationTax.TryGetValue(faction.id, out long pending);
                deferredStationTax[faction.id] = Session.SaturatingAdd(pending, amount);
                return;
            }
            faction.bank = Session.SaturatingAdd(faction.bank, amount);
            taxDirty = true;
        }

        static void FlushDeferredStationTax()
        {
            if (deferredStationTax.Count == 0) return;
            foreach (var entry in deferredStationTax) CreditStationTax(FindById(entry.Key), entry.Value);
            deferredStationTax.Clear();
        }

        /// <summary>NetState.TollPaidRpc: a player of another faction paid the toll at 'station' (their game took the credits):
        /// into the holder's bank.</summary>
        public static void OnTollPaid(ulong client, int station, int amount)
        {
            var held = ClaimAt(station);
            string account = NetProfiles.AccountOf(client);
            var payer = Of(account);
            if (held == null || account == null || payer == null || payer.id == held.faction || Toll == 0) return;
            if (!NetGuard.InOrbit(client, station)) return;   // a toll is paid on arriving there (TerritoryView)
            var owner = list.factions.Find(c => c.id == held.faction);
            if (owner == null) return;
            // A fixed toll is the server's own amount; a percentage is what the pilot's game says it paid, at most that share
            // of the profile's recorded worth (its credits are part of it) and at least 1.
            int paid = Toll > 0 ? Toll : Mathf.Clamp(amount, 1, TollFor(Toll, NetProfiles.WorthOf(account)));
            if (!NetProfiles.MoveCredits(client, account, -paid))
            {
                // Not in the saved game: the game's credits back, nothing to the holder (a game that only claims to pay,
                // or one sending the toll again and again), and the toll counts as unpaid: the game marked it paid when it
                // sent it, so the holder's fighters would have spared the pilot for nothing.
                Debug.LogWarning($"Server: profile {account} ({NameOf(account)}, client {client}) paid a {paid:N0} credits toll its saved game doesn't hold: refused.");
                NetState.Instance?.TollRefused(client, station, paid);
                return;
            }
            owner.bank += paid;
            Save();
            NetAudit.Bank(owner.tag, $"paid the toll at {StationName(station)}:", NameOf(account), paid, owner.bank);
            TellFaction(owner, string.Format(Localization.Extra("mpTollReceived", "[{0}] {1} paid the {2:N0} credits toll at {3}."), payer.tag, NameOf(account), paid, StationName(station)));
        }
        /// <summary>NetState.StockItemRpc: 'units' of 'item' bought at 'station' for 'price' each that carry VAT (not resold
        /// stock): the rate's share of what a non-member paid into the holder's bank. The price is the buyer's game's, capped
        /// at the item's highest list price with the markup.</summary>
        public static void OnPurchase(ulong client, int station, int item, int price, int units)
        {
            var held = ClaimAt(station);
            int rate = VatAt(station);
            if (held == null || price <= 0 || units <= 0 || rate <= 0) return;
            var buyer = OfClient(client);
            if (buyer != null && buyer.id == held.faction) return;   // members: their discount, no VAT
            var owner = list.factions.Find(c => c.id == held.faction);
            var it = NetGame.Db?.Item(item);
            if (owner == null || it == null) return;
            long paid = Math.Min(price, VatMarkup(it.maxPrice, rate)) * (long)units;
            long vat = (long)Math.Round(paid / (1.0 + rate / 1000.0) * rate / 100.0);
            if (vat <= 0) return;
            CreditStationTax(owner, vat);
        }
        /// <summary>NetState.StockShipRpc: a dealer ship bought at 'station' (the reserved row taken): the VAT of a non-member's
        /// purchase from the ship's list price (the server's own number).</summary>
        public static void OnShipBought(ulong client, int station, int ship)
        {
            var held = ClaimAt(station);
            int rate = VatAt(station);
            var s = NetGame.Db?.Ship(ship);
            if (held == null || rate <= 0 || s == null || s.price <= 0) return;
            var buyer = OfClient(client);
            if (buyer != null && buyer.id == held.faction) return;
            var owner = list.factions.Find(c => c.id == held.faction);
            if (owner == null) return;
            CreditStationTax(owner, (long)Math.Round(s.price * rate / 100.0));
        }
        /// <summary>"/faction vat [rate] [station]": the claim's VAT rate (officers), or what it is now. The station is the one
        /// docked at unless given (any of the faction's claims, by number). A new rate thins the goods from the next stock roll.</summary>
        static string SetVat(ulong client, string me, string arg)
        {
            var faction = Of(me);
            if (faction == null) return Localization.Extra("mpFactionNone", "You aren't in a faction.");
            var words = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int station = DockedAt(client);
            if (words.Length >= 2 && int.TryParse(words[1], out int given)) station = given;
            var held = ClaimAt(station);
            if (held == null || held.faction != faction.id) return Localization.Extra("mpVatWhere", "Dock at one of your faction's stations, or name one: /faction vat <rate> <station>.");
            if (words.Length == 0)
                return string.Format(Localization.Extra("mpVatInfo", "{0}: VAT {1}%, {2}% fewer goods."), StationName(station), held.vat, held.vat);
            if (!IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            if (!int.TryParse(words[0].TrimEnd('%'), out int rate) || rate < 0 || rate > MaxVat)
                return string.Format(Localization.Extra("mpVatUsage", "/faction vat <0-{0}> [station]"), MaxVat);
            held.vat = rate;
            Save();
            PublishClaims();
            TellFaction(faction, string.Format(Localization.Extra("mpVatSet", "{0}'s VAT is {1}% now: its goods thin to {2}% from the next restock."),
                                               StationName(station), rate, 100 - rate));
            return "";
        }
        // ---- the station window (NetPanel) -----------------------------------------------------------------
        /// <summary>The player's faction, invitations, the station they are docked at and every faction into the snapshot.</summary>
        internal static void FillPanel(ulong client, NetPanel.State s)
        {
            if (list == null) return;
            string me = NetProfiles.AccountOf(client);
            s.claimCost = ClaimCost; s.siegeCost = SiegeCost; s.maxClaims = MaxClaims; s.toll = Toll;
            s.sieges = SiegesText();
            foreach (var c in list.factions)
                s.factions.Add(new NetPanel.FactionRow { tag = c.tag, name = c.name, members = c.members.Count, claims = list.claims.FindAll(x => x.faction == c.id).Count });
            if (s.dockedStation >= 0)
            {
                var held = ClaimAt(s.dockedStation);
                s.stationHolder = held != null ? list.factions.Find(c => c.id == held.faction)?.tag ?? "" : "";
                var st = NetGame.Db.Stations.Find(x => x.index == s.dockedStation);
                s.stationClaimable = st != null && s.dockedStation != KaamoStation && st.system != LomaSystem;
            }
            if (me != null && invites.TryGetValue(me, out var inv) && inv.until > Time.realtimeSinceStartup)
            {
                var from = list.factions.Find(c => c.id == inv.faction);
                if (from != null) s.factionInvites.Add(from.tag + "|" + from.name);
            }
            var faction = Of(me);
            if (faction == null) return;
            s.inFaction = true;
            s.factionTag = faction.tag;
            s.factionName = faction.name;
            s.bank = faction.bank;
            s.home = HomeOf(me);
            s.rank = faction.leader == me ? 2 : faction.officers.Contains(me) ? 1 : 0;
            foreach (var m in faction.members)
                s.members.Add(new NetPanel.Member { name = NameOf(m), rank = faction.leader == m ? 2 : faction.officers.Contains(m) ? 1 : 0, online = NetProfiles.IsOnline(m) });
            s.members.Sort((a, b) => a.rank != b.rank ? b.rank.CompareTo(a.rank) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            foreach (var c in list.claims)
                if (c.faction == faction.id)
                    s.claims.Add(new NetPanel.ClaimRow { station = c.station, name = StationName(c.station), home = c.station == s.home,
                                                         daysLeft = (float)Math.Max(0, LapseDays - DaysSince(c.lastDock)), sieged = SiegeAt(c.station) != null,
                                                         garrisonSize = c.garrisonSize, garrisonLevel = Mathf.Clamp(c.garrisonLevel, 1, MaxGarrisonLevel),
                                                         vat = Mathf.Clamp(c.vat, 0, MaxVat) });
        }
        // ---- the server console -----------------------------------------------------------------------------
        /// <summary>DedicatedServer's "factions" command.</summary>
        public static string ConsoleList()
        {
            if (list == null) return "Factions need player profiles (-noprofiles is set).";
            if (list.factions.Count == 0) return "No factions.";
            var sb = new StringBuilder($"{list.factions.Count} faction(s):");
            foreach (var c in list.factions)
                sb.Append($"\n  [{c.tag}] {c.name}: {c.members.Count} member(s), leader {NameOf(c.leader)}, bank {c.bank:N0}");
            return sb.ToString();
        }
        /// <summary>DedicatedServer's "sieges" command.</summary>
        public static string ConsoleSieges() => list == null ? "Factions need player profiles (-noprofiles is set)." : SiegesText();
        /// <summary>DedicatedServer's "faction disband TAG".</summary>
        public static string ConsoleDisband(string tag)
        {
            var faction = ByTag(tag);
            if (faction == null) return $"No faction with the tag {tag}.";
            var result = Disband(faction.leader);
            return string.IsNullOrEmpty(result) ? $"Disbanded [{faction.tag}] {faction.name}." : result;
        }
    }
}
