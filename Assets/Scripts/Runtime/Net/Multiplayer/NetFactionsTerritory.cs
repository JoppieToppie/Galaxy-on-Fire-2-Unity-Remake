// NetFactionsTerritory.cs
// NetFactions: the bank and territory (part of the partial NetFactions; its fields are in NetFactions.cs).

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
        // ---- the bank ---------------------------------------------------------------------------------------
        static bool TryAmount(string arg, out long amount) => long.TryParse(arg.Replace(",", "").Replace(".", "").Replace(" ", ""), out amount) && amount > 0;
        static string Deposit(ulong client, string me, string arg)
        {
            var faction = Of(me);
            if (faction == null) return Localization.Extra("mpFactionNone", "You aren't in a faction.");
            if (!TryAmount(arg, out long amount)) return "/faction deposit N";
            if (!NetProfiles.Controls(client)) return Localization.Extra("mpFactionController", "Do that on the device that controls your profile.");
            if (NetPlayerStations.ProfileWritesBlocked) return "Faction treasury recovery is pending.";
            long saved = NetProfiles.CreditAmount(me);
            if (amount > saved)
                return string.Format(Localization.Extra("mpFactionSavedShort", "Your saved game has {0:N0} credits: dock to save it, then deposit."), saved);
            if (faction.bank > long.MaxValue - amount) return "The faction treasury cannot hold that deposit.";
            // The host's live balance is authoritative. Do not pre-charge it and then debit it again.
            if (NetProfiles.IsHostClient(client))
            {
                if (!NetPlayerStations.CommitFactionBankTransfer(me, faction.id, amount)) return "The deposit could not be committed.";
                AnnounceDeposit(faction, me, amount);
                return "";
            }
            int token = nextCharge++;
            charges[token] = (me, faction.id, amount, Time.realtimeSinceStartup + ChargeSeconds);
            NetState.Instance?.Charge(client, token, amount);
            return "";
        }
        /// <summary>NetState.ChargedRpc: the player's game paid a deposit (or couldn't: not enough credits).</summary>
        public static void OnCharged(ulong client, int token, bool paid)
        {
            if (!charges.TryGetValue(token, out var c) || c.account != NetProfiles.AccountOf(client))
            {
                if (paid) Debug.LogWarning($"Server: client {client} paid an unknown or expired faction deposit ({token}): nothing banked.");
                return;
            }
            charges.Remove(token);
            var faction = list?.factions.Find(x => x.id == c.faction);
            if (!paid) { NetState.Instance?.Notify(client, Localization.Extra("mpFactionNoCredits", "You don't have that many credits.")); return; }
            if (faction == null || Of(c.account)?.id != c.faction || !NetProfiles.Controls(client) ||
                !NetPlayerStations.CommitFactionBankTransfer(c.account, c.faction, c.amount))
            {
                // The game says it paid, but its saved game hasn't that much (spent since, or a game that only claims to
                // have the credits): nothing banked, the game's credits back.
                Debug.LogWarning($"Server: profile {c.account} ({NameOf(c.account)}, client {client}) paid a {c.amount:N0} credits deposit its saved game doesn't hold: refused.");
                NetState.Instance?.Grant(client, c.amount);
                NetState.Instance?.Notify(client, string.Format(Localization.Extra("mpFactionSavedShort", "Your saved game has {0:N0} credits: dock to save it, then deposit."), NetProfiles.SpendableCredits(client, c.account)));
                return;
            }
            AnnounceDeposit(faction, c.account, c.amount);
        }
        static void AnnounceDeposit(Faction faction, string account, long amount)
        {
            NetAudit.Bank(faction.tag, "deposited", NameOf(account), amount, faction.bank);
            TellFaction(faction, string.Format(Localization.Extra("mpFactionDeposited", "{0} put {1:N0} credits into the bank ({2:N0})."), NameOf(account), amount, faction.bank));
        }
        static string Withdraw(ulong client, string me, string arg)
        {
            var faction = Of(me);
            if (faction == null || !IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            if (!TryAmount(arg, out long amount)) return "/faction withdraw N";
            if (!NetProfiles.Controls(client)) return Localization.Extra("mpFactionController", "Do that on the device that controls your profile.");
            if (NetPlayerStations.ProfileWritesBlocked) return "Faction treasury recovery is pending.";
            if (faction.bank < amount) return string.Format(Localization.Extra("mpFactionBankShort", "The bank has {0:N0} credits."), faction.bank);
            if (!NetPlayerStations.CommitFactionBankTransfer(me, faction.id, -amount))
                return "The withdrawal could not be committed; no credits were transferred.";
            if (!NetProfiles.IsHostClient(client)) NetState.Instance?.Grant(client, amount);
            NetAudit.Bank(faction.tag, "withdrew", NameOf(me), amount, faction.bank);
            TellFaction(faction, string.Format(Localization.Extra("mpFactionWithdrew", "{0} took {1:N0} credits from the bank ({2:N0})."), NameOf(me), amount, faction.bank));
            return "";
        }
        /// <summary>Credits to the player's game; their profile's worth moves with it (the upload check).</summary>
        static void Grant(ulong client, string account, long amount)
        {
            NetProfiles.MoveCredits(client, account, amount);
            NetState.Instance?.Grant(client, amount);
        }
        // ---- territory --------------------------------------------------------------------------------------
        static string StationName(int station)
        {
            var st = NetGame.Db.Stations.Find(s => s.index == station);
            return st != null ? st.name : $"station {station}";
        }
        static Claim ClaimAt(int station) => list?.claims.Find(c => c.station == station);
        /// <summary>The player's station while docked there (-1 = not docked).</summary>
        static int DockedAt(ulong client)
        {
            var p = NetSquad.Find(client);
            return p != null && p.InHangar ? p.Station : -1;
        }
        static string ClaimHere(ulong client, string me)
        {
            var faction = Of(me);
            if (faction == null || !IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            int station = DockedAt(client);
            var st = NetGame.Db.Stations.Find(s => s.index == station);
            if (st == null) return Localization.Extra("mpFactionDockFirst", "Dock at the station first.");
            if (station == KaamoStation || st.system == LomaSystem) return Localization.Extra("mpFactionNotClaimable", "This station can't be claimed.");
            var held = ClaimAt(station);
            if (held != null)
            {
                var owner = list.factions.Find(c => c.id == held.faction);
                return held.faction == faction.id ? Localization.Extra("mpFactionOwnClaim", "Your faction holds this station already.")
                                            : string.Format(Localization.Extra("mpFactionClaimedBy", "[{0}] {1} holds this station."), owner?.tag, owner?.name);
            }
            int count = list.claims.FindAll(c => c.faction == faction.id).Count;
            if (count >= MaxClaims) return string.Format(Localization.Extra("mpFactionMaxClaims", "A faction holds at most {0} stations."), MaxClaims);
            list.lapsed.RemoveAll(l => l.until <= UnixNow);
            var locked = list.lapsed.Find(l => l.station == station);
            if (locked != null)
                return string.Format(Localization.Extra("mpFactionLapsedLock", "This station's claim lapsed: it can't be claimed for another {0:0.#} hours."), (locked.until - UnixNow) / 3600f);
            if (faction.bank < ClaimCost)
                return string.Format(Localization.Extra("mpFactionClaimCost", "A claim costs {0:N0} credits from the bank (it has {1:N0}): /faction deposit N."), ClaimCost, faction.bank);
            faction.bank -= ClaimCost;
            string now = DateTime.UtcNow.ToString("o");
            list.claims.Add(new Claim { station = station, faction = faction.id, claimed = now, lastDock = now });
            if (faction.home < 0 || ClaimAt(faction.home)?.faction != faction.id) faction.home = station;
            Save();
            PublishClaims();
            RefreshHomes(faction);
            Debug.Log($"Server: [{faction.tag}] claimed {st.name}.");
            NetState.Instance?.Announce(string.Format(Localization.Extra("mpFactionClaimedNews", "[{0}] {1} claimed {2}."), faction.tag, faction.name, st.name));
            NetNews.Post(NetNews.Kind.Territory, $"{NetNews.FactionName(faction.tag, faction.name)} plants its flag on {NetNews.Place(station)}", station);
            return "";
        }
        static string Unclaim(ulong client, string me)
        {
            var faction = Of(me);
            if (faction == null || !IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            var held = ClaimAt(DockedAt(client));
            if (held == null || held.faction != faction.id) return Localization.Extra("mpFactionNotYours", "Dock at one of your faction's stations first.");
            list.claims.Remove(held);
            if (faction.home == held.station) faction.home = list.claims.Find(c => c.faction == faction.id)?.station ?? -1;
            Save();
            PublishClaims();
            RefreshHomes(faction);
            TellFaction(faction, string.Format(Localization.Extra("mpFactionUnclaimed", "Your faction gave up {0}."), StationName(held.station)));
            NetNews.Post(NetNews.Kind.Territory, $"{NetNews.FactionName(faction.tag, faction.name)} pulls out of {NetNews.Place(held.station)}: the station is free", held.station);
            return "";
        }
        static string SetHome(ulong client, string me)
        {
            var faction = Of(me);
            if (faction == null || !IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            var held = ClaimAt(DockedAt(client));
            if (held == null || held.faction != faction.id) return Localization.Extra("mpFactionNotYours", "Dock at one of your faction's stations first.");
            faction.home = held.station;
            Save();
            RefreshHomes(faction);
            TellFaction(faction, string.Format(Localization.Extra("mpFactionHome", "{0} is your faction's home now."), StationName(held.station)));
            return "";
        }
        static double DaysSince(string utc) =>
            DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? (DateTime.UtcNow - t.ToUniversalTime()).TotalDays : 0;
        static string ClaimsText(Faction faction)
        {
            if (faction == null) return Localization.Extra("mpFactionNone", "You aren't in a faction.");
            var claims = list.claims.FindAll(c => c.faction == faction.id);
            if (claims.Count == 0) return string.Format(Localization.Extra("mpFactionNoClaims", "[{0}] holds no stations."), faction.tag);
            var sb = new StringBuilder($"[{faction.tag}] {faction.name}:");
            foreach (var c in claims)
                sb.Append($"\n{StationName(c.station)}{(c.station == faction.home ? " (home)" : "")}: lapses in {Math.Max(0, LapseDays - DaysSince(c.lastDock)):0.#} days without a member docking");
            return sb.ToString();
        }
        /// <summary>A profile's faction home station, -1 = none (NetProfiles: where a member's game starts).</summary>
        public static int HomeOf(string account)
        {
            var faction = Of(account);
            return faction != null && ClaimAt(faction.home)?.faction == faction.id ? faction.home : -1;
        }
        static void RefreshHomes(Faction faction)
        {
            foreach (var m in faction.members)
                foreach (ulong c in NetProfiles.ClientsOf(m)) NetSquad.Find(c)?.SetFactionHome(HomeOf(m));
        }
        /// <summary>Every claim to the players (NetState.Claims): "station|TAG|Name|VAT %" per line.</summary>
        static void PublishClaims()
        {
            if (list == null || NetState.Instance == null) return;
            var sb = new StringBuilder();
            foreach (var c in list.claims)
            {
                var faction = list.factions.Find(x => x.id == c.faction);
                if (faction != null) sb.Append(c.station).Append('|').Append(faction.tag).Append('|').Append(faction.name.Replace("|", " "))
                                       .Append('|').Append(Mathf.Clamp(c.vat, 0, MaxVat)).Append('\n');
            }
            NetState.Instance.SetClaims(sb.ToString());
        }
        /// <summary>NetState: spawned on the server (the claims go out once it exists).</summary>
        public static void OnStateSpawned() { PublishClaims(); PublishSieges(); NetState.Instance?.SetToll(Toll); }
        /// <summary>NetState.Update (server): members docked at a claim keep it; claims nobody kept lapse; old deposits drop.</summary>
        public static void Tick()
        {
            if (list == null || NetPlayerStations.ProfileWritesBlocked) return;
            FlushDeferredStationTax();
            if ((siegeTimer -= Time.unscaledDeltaTime) <= 0f) { siegeTimer = SiegeTickSeconds; TickSieges(SiegeTickSeconds); }
            if (charges.Count > 0)
                foreach (var key in new List<int>(charges.Keys)) if (charges[key].until < Time.realtimeSinceStartup) charges.Remove(key);
            if ((claimTimer -= Time.unscaledDeltaTime) > 0f) return;
            claimTimer = ClaimTickSeconds;
            bool changed = taxDirty;   // the trade tax since the last write (saved once a minute)
            taxDirty = false;
            string now = DateTime.UtcNow.ToString("o");
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned || !p.InHangar) continue;
                var held = ClaimAt(p.Station);
                var faction = held != null ? OfClient(p.OwnerClientId) : null;
                // Under siege docking doesn't keep the claim (TickSieges halves its days instead).
                if (faction != null && faction.id == held.faction && SiegeAt(held.station) == null) { held.lastDock = now; changed = true; }
            }
            if (TickUpkeep()) changed = true;
            foreach (var c in new List<Claim>(list.claims))
            {
                if (DaysSince(c.lastDock) < LapseDays) continue;
                var siege = SiegeAt(c.station);
                var holderTag = list.factions.Find(x => x.id == c.faction)?.tag;
                if (siege != null && siege.defender == c.faction && list.factions.Exists(x => x.id == siege.attacker))
                {
                    // Lapsing under siege: the station goes to the attackers (the stake back to them).
                    EndSiege(siege, true, string.Format(Localization.Extra("mpSiegeLapsed", "Nobody of [{0}] kept {1} under siege: the claim lapsed."), holderTag, StationName(c.station)));
                    changed = true;
                    continue;
                }
                list.claims.Remove(c);
                list.lapsed.RemoveAll(l => l.station == c.station);
                list.lapsed.Add(new Lapsed { station = c.station, until = UnixNow + LapsedLockSeconds });
                var faction = list.factions.Find(x => x.id == c.faction);
                if (faction != null)
                {
                    if (faction.home == c.station) faction.home = list.claims.Find(x => x.faction == faction.id)?.station ?? -1;
                    TellFaction(faction, string.Format(Localization.Extra("mpFactionLapsed", "Nobody of your faction docked at {0} for {1} days: the claim lapsed."), StationName(c.station), LapseDays));
                    RefreshHomes(faction);
                }
                Debug.Log($"Server: the claim on {StationName(c.station)} lapsed.");
                NetNews.Post(NetNews.Kind.Territory, faction != null
                    ? $"{NetNews.FactionName(faction.tag, faction.name)} abandoned {NetNews.Place(c.station)}: nobody docked there for {LapseDays} days"
                    : $"{NetNews.Place(c.station)} is unclaimed again", c.station);
                changed = true;
            }
            if (changed)
            {
                if (!NetProfiles.Write(PathOf, JsonUtility.ToJson(list, true))) taxDirty = true;
                PublishClaims();
            }
        }
    }
}
