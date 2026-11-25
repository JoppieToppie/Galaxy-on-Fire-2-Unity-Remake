// NetFactionsSieges.cs
// NetFactions: sieges and the garrison (part of the partial NetFactions; its fields are in NetFactions.cs).

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GoF2Remake.Data;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetFactions
    {
        // ---- sieges (phase 3) ---------------------------------------------------------------------------
        static long UnixNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static Siege SiegeAt(int station) => list?.sieges.Find(s => s.station == station);
        internal static bool CanRespawnAt(int station, string account)
        {
            var siege = SiegeAt(station);
            if (siege == null) return true;
            var faction = Of(account);
            return (faction != null && faction.id == siege.defender &&
                (faction.leader == account || (Modding.ModPlayerInfrastructure.Current.SiegeRespawnFactionOfficers && faction.officers?.Contains(account) == true))) ||
                NetPlayerStations.HasSiegeRespawnPrivilege(station, account);
        }

        internal static int DeathRespawnStation(NetPlayer pilot, int lastDocked, int deathOrigin = int.MinValue)
        {
            var db = NetGame.Db;
            if (pilot == null || db?.Stations == null || db.Systems == null) return -1;
            string account = NetProfiles.AccountOf(pilot.OwnerClientId);
            int origin = deathOrigin != int.MinValue ? deathOrigin : pilot.Station;
            var faction = Of(account);
            bool Allowed(int orbit) => orbit >= 0 && CanRespawnAt(orbit, account) &&
                !NetPlayerStations.StationClosedByCombat(orbit) && NetPlayerStations.CanVisitStation(orbit, account) &&
                db.Stations.Exists(s => s != null && s.index == orbit) && OrbitLayout.Build(db, orbit).hasStation;
            var siege = SiegeAt(origin);
            bool defending = siege != null && faction != null && faction.id == siege.defender;
            if (defending && Allowed(origin)) return origin;
            if (!defending && Allowed(lastDocked)) return lastDocked;
            int system = db.Stations.Find(s => s != null && s.index == origin)?.system ?? -1;
            var queue = new Queue<int>();
            var seen = new HashSet<int>();
            if (system >= 0) { queue.Enqueue(system); seen.Add(system); }
            var liveRoutes = NetPlayerStations.AccessiblePlayerGateRoutes(account, faction?.id);
            int fallback = -1;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                var sys = db.Systems.Find(s => s != null && s.index == current);
                if (sys == null) continue;
                foreach (int orbit in sys.stations ?? new List<int>())
                {
                    if (!Allowed(orbit)) continue;
                    var playerStation = NetPlayerStations.StationAt(orbit);
                    if (faction != null && (ClaimAt(orbit)?.faction == faction.id || playerStation?.ownerFactionId == faction.id)) return orbit;
                    if (fallback < 0 && orbit < 100000 && playerStation == null) fallback = orbit;
                }
                if (sys.jumpRoutesTo != null)
                    foreach (int next in sys.jumpRoutesTo)
                        if (seen.Add(next)) queue.Enqueue(next);
                if (liveRoutes.TryGetValue(current, out var destinations))
                    foreach (int next in destinations)
                        if (seen.Add(next)) queue.Enqueue(next);
            }
            if (fallback >= 0) return fallback;
            if (Allowed(lastDocked)) return lastDocked;
            if (faction != null && Allowed(faction.home)) return faction.home;
            foreach (var station in db.Stations)
                if (station != null && station.index < 100000 && Allowed(station.index)) return station.index;
            return -1;
        }

        static string DeclareSiege(ulong client, string me)
        {
            var faction = Of(me);
            if (faction == null || !IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            var p = NetSquad.Find(client);
            int station = p != null ? p.Station : -1;
            var held = ClaimAt(station);
            if (held == null) return Localization.Extra("mpSiegeWhere", "Fly to (or dock at) a station another faction holds first.");
            if (held.faction == faction.id) return Localization.Extra("mpFactionOwnClaim", "Your faction holds this station already.");
            if (SiegeAt(station) != null) return Localization.Extra("mpSiegeRunning", "This station is under siege already.");
            if (list.sieges.Exists(s => s.attacker == faction.id)) return Localization.Extra("mpSiegeOne", "Your faction is besieging another station already.");
            var holder = list.factions.Find(c => c.id == held.faction);
            if (holder != null && holder.home == station && list.claims.Exists(c => c.faction == holder.id && c.station != station))
                return string.Format(Localization.Extra("mpSiegeHome", "{0} is the home of [{1}]: besiege their other stations first."), StationName(station), holder.tag);
            if (held.protectedUntil > UnixNow)
                return string.Format(Localization.Extra("mpSiegeProtected", "This station can't be besieged for another {0:0.#} hours."), (held.protectedUntil - UnixNow) / 3600f);
            if (list.claims.FindAll(c => c.faction == faction.id).Count >= MaxClaims)
                return string.Format(Localization.Extra("mpFactionMaxClaims", "A faction holds at most {0} stations."), MaxClaims);
            if (faction.bank < SiegeCost)
                return string.Format(Localization.Extra("mpSiegeCost", "A siege costs {0:N0} credits from the bank (it has {1:N0})."), SiegeCost, faction.bank);
            var defender = list.factions.Find(c => c.id == held.faction);
            faction.bank -= SiegeCost;
            long now = UnixNow;
            list.sieges.Add(new Siege { station = station, attacker = faction.id, defender = held.faction, startsAt = now + SiegeDelaySeconds,
                                        endsAt = now + SiegeDelaySeconds + SiegeSeconds, expiresAt = now + SiegeDelaySeconds + SiegeMeetSeconds, stake = SiegeCost });
            Save();
            PublishSieges();
            NetState.Instance?.Announce(string.Format(Localization.Extra("mpSiegeDeclared3",
                "[{0}] {1} besieges {2}, held by [{3}] {4}: in {5} minutes the station's defences rise until pilots of both factions meet in the orbit (within {6} days); then a 15-minute fight, the most kills wins. {7:N0} credits go to the winner."),
                faction.tag, faction.name, StationName(station), defender?.tag, defender?.name, SiegeDelaySeconds / 60, SiegeMeetSeconds / 86400, SiegeCost));
            Debug.Log($"Server: [{faction.tag}] besieges {StationName(station)} ([{defender?.tag}]).");
            NetNews.Post(NetNews.Kind.War, $"{NetNews.FactionName(faction.tag, faction.name)} declares war on {NetNews.FactionName(defender?.tag, defender?.name)}: "
                + $"siege of {NetNews.Place(station)} in {SiegeDelaySeconds / 60} minutes", station);
            return "";
        }
        static string SiegesText()
        {
            if (list == null || list.sieges.Count == 0) return Localization.Extra("mpSiegeNone", "No sieges.");
            var sb = new StringBuilder(Localization.Extra("mpSiegeList", "Sieges:"));
            long now = UnixNow;
            foreach (var s in list.sieges)
            {
                var a = list.factions.Find(c => c.id == s.attacker);
                var d = list.factions.Find(c => c.id == s.defender);
                sb.Append($"\n{StationName(s.station)}: [{a?.tag}] against [{d?.tag}], {s.stake:N0} credits at stake, ")
                  .Append(!s.armed ? $"armed in {(s.startsAt - now) / 60 + 1} min"
                          : !s.started ? $"waiting for both sides ({(s.expiresAt - now) / 3600f:0.#} h left, {s.extensions}/{MaxExtensions} days added, "
                                         + $"{DefenderCount(Step(s, now))} defenders at level {DefenderLevel(Step(s, now))}, standoff [{a?.tag}] {s.attStandoff / 60f:0} / [{d?.tag}] {s.defStandoff / 60f:0} of {StandoffSeconds / 60} min)"
                          : $"fighting {s.attKills} : {s.defKills}, {(s.endsAt - now) / 60 + 1} min left");
            }
            return sb.ToString();
        }
        /// <summary>The escalation step of an armed siege (frozen at the fight's start).</summary>
        static int Step(Siege s, long now) => !s.armed ? 0 : s.fightStep >= 0 ? s.fightStep : (int)(Math.Max(0, now - s.startsAt) / DefenderStepSeconds);
        /// <summary>Every 5 s: see the header (arming, escalation, the claim draining, the standoff clock, the extensions, the
        /// fight's score and white flags).</summary>
        static void TickSieges(float dt)
        {
            if (list.sieges.Count == 0) return;
            long now = UnixNow;
            bool changed = false;
            foreach (var s in new List<Siege>(list.sieges))
            {
                var held = ClaimAt(s.station);
                var attacker = list.factions.Find(c => c.id == s.attacker);
                var defender = list.factions.Find(c => c.id == s.defender);
                if (attacker == null) { list.sieges.Remove(s); changed = true; continue; }   // the attackers' faction ended: the stake with it
                if (held == null || held.faction != s.defender || defender == null)
                {
                    // The claim was given up: nothing to fight for, the stake back.
                    CallOff(s, Localization.Extra("mpSiegeOffClaim", "{2} isn't held by [{0}] {1} any more: the siege is off, the {3:N0} credits stake goes back to [{4}]."),
                            defender?.tag, defender?.name);
                    changed = true;
                    continue;
                }
                if (now < s.startsAt) continue;
                string place = StationName(s.station);
                if (!s.armed)
                {
                    s.armed = true;
                    s.garrisonAlive = held.garrisonSize;
                    s.garrisonLevel = Mathf.Clamp(held.garrisonLevel, 1, MaxGarrisonLevel);
                    s.garrisonBack.Clear();
                    changed = true;
                    NetState.Instance?.Announce(string.Format(Localization.Extra("mpSiegeArmed2",
                        "The siege of {0} is armed: its defences rise until pilots of [{1}] and [{2}] meet in its orbit."), place, attacker.tag, defender.tag));
                }
                // The garrison's dead come back after their cooldown.
                for (int i = s.garrisonBack.Count - 1; i >= 0; i--)
                    if (now >= s.garrisonBack[i]) { s.garrisonBack.RemoveAt(i); s.garrisonAlive++; changed = true; }

                int att = 0, def = 0;
                bool attOnline = false, defOnline = false;
                var seen = new HashSet<ulong>();
                foreach (var p in NetPlayer.All)
                {
                    if (p == null || !p.IsSpawned) continue;
                    var faction = OfClient(p.OwnerClientId);
                    bool isAtt = faction != null && faction.id == s.attacker, isDef = faction != null && faction.id == s.defender;
                    if (!isAtt && !isDef) continue;
                    ulong id = p.OwnerClientId;
                    seen.Add(id);
                    if (isAtt) attOnline = true; else defOnline = true;
                    bool inOrbit = p.InSpace && p.Station == s.station, alive = p.Hull > 0f, docked = p.InHangar && p.Station == s.station;
                    s.last.TryGetValue(id, out var prev);
                    if (s.started && prev.inOrbit && prev.alive)
                    {
                        if (inOrbit && !alive)
                        {
                            // Destroyed in the fight: a point for the other side; a defender respawns docked there.
                            if (isDef) { s.attKills++; if (!s.respawning.Contains(id)) s.respawning.Add(id); } else s.defKills++;
                            changed = true;
                        }
                        else if (!inOrbit) { if (isAtt) s.attLeftAlive = true; else s.defLeftAlive = true; }
                    }
                    if (inOrbit && alive) { if (isAtt) att++; else def++; }
                    if (isDef && docked && !prev.docked)
                    {
                        if (s.respawning.Remove(id)) changed = true;      // back after dying in the fight: not a dock
                        else if (HalveLapse(held, defender)) changed = true;
                    }
                    s.last[id] = (isAtt, inOrbit, alive, docked);
                }
                // Gone from the session while alive in the orbit during the fight: left it alive.
                foreach (var id in new List<ulong>(s.last.Keys))
                {
                    if (seen.Contains(id)) continue;
                    var prev = s.last[id];
                    s.last.Remove(id);
                    if (s.started && prev.inOrbit && prev.alive) { if (prev.attacker) s.attLeftAlive = true; else s.defLeftAlive = true; }
                }

                if (!s.started)
                {
                    if (att > 0 && def > 0)
                    {
                        s.fightStep = Step(s, now);
                        s.started = true;
                        s.endsAt = now + SiegeSeconds;
                        s.noAttackersSince = s.noDefendersSince = 0;
                        s.attKills = s.defKills = 0;
                        s.attLeftAlive = s.defLeftAlive = false;
                        s.respawning.Clear();
                        changed = true;
                        NetState.Instance?.Announce(string.Format(Localization.Extra("mpSiegeStarts3",
                            "The siege of {0} has begun: [{1}] and [{2}] may fire at each other there for 15 minutes. The most kills wins; leaving the orbit alive is surrender."),
                            place, attacker.tag, defender.tag));
                        NetNews.Post(NetNews.Kind.War, $"Fighting erupts at {NetNews.Place(s.station)}: [{NetNews.Safe(attacker.tag)}] against [{NetNews.Safe(defender.tag)}]", s.station);
                        continue;
                    }
                    // The standoff clock: one side waits there while the other has a pilot online elsewhere.
                    if (att > 0 && def == 0 && defOnline) { s.defStandoff += dt; changed = true; }
                    if (def > 0 && att == 0 && attOnline) { s.attStandoff += dt; changed = true; }
                    if (s.defStandoff >= StandoffSeconds)
                        EndSiege(s, true, string.Format(Localization.Extra("mpSiegeStandoffDef", "[{0}] kept away from {1} while the attackers waited there."), defender.tag, place));
                    else if (s.attStandoff >= StandoffSeconds)
                        EndSiege(s, false, string.Format(Localization.Extra("mpSiegeStandoffAtt", "[{0}] kept away from {1} while the defenders waited there."), attacker.tag, place));
                    else if (now >= s.expiresAt)
                    {
                        if (s.extensions < MaxExtensions)
                        {
                            s.extensions++;
                            s.expiresAt += DaySeconds;
                            changed = true;
                            NetState.Instance?.Announce(string.Format(Localization.Extra("mpSiegeExtended",
                                "No fight at {0} yet: the siege goes on for another day ({1} of {2})."), place, s.extensions, MaxExtensions));
                        }
                        else EndSiege(s, false, string.Format(Localization.Extra("mpSiegeNoFight", "Nobody fought at {0}."), place));
                    }
                    continue;
                }
                // The fight: the white flag (a side's last pilot left the orbit alive and stays gone), then the clock.
                if (att > 0) { s.noAttackersSince = 0; s.attLeftAlive = false; } else if (s.noAttackersSince == 0) { s.noAttackersSince = now; changed = true; }
                if (def > 0) { s.noDefendersSince = 0; s.defLeftAlive = false; } else if (s.noDefendersSince == 0) { s.noDefendersSince = now; changed = true; }
                if (s.defLeftAlive && s.noDefendersSince > 0 && now - s.noDefendersSince >= SiegeForfeitSeconds)
                    EndSiege(s, true, string.Format(Localization.Extra("mpSiegeWhiteFlag", "[{0}] left the fight at {1}: white flag."), defender.tag, place));
                else if (s.attLeftAlive && s.noAttackersSince > 0 && now - s.noAttackersSince >= SiegeForfeitSeconds)
                    EndSiege(s, false, string.Format(Localization.Extra("mpSiegeWhiteFlag", "[{0}] left the fight at {1}: white flag."), attacker.tag, place));
                else if (now >= s.endsAt)
                    EndSiege(s, s.attKills > s.defKills, string.Format(Localization.Extra("mpSiegeScore", "The fight at {0} is over: [{1}] {2} : {3} [{4}]."),
                                                                       place, attacker.tag, s.attKills, s.defKills, defender.tag));
            }
            if (changed) { Save(); PublishSieges(); }
        }
        /// <summary>NetState.DestroyedByRpc (checked there: a hit from the killer within 30 s): in a siege fight a pilot of one
        /// side destroying one of the other side in that orbit gets their ship repaired (hull, shield, armor), as the
        /// destroyed one respawns fresh.</summary>
        public static void OnPilotKilled(NetPlayer victim, NetPlayer killer)
        {
            if (list == null || victim == null || killer == null || NetState.Instance == null) return;
            var s = SiegeAt(victim.Station);
            if (s == null || !s.started || !killer.InSpace || killer.Station != s.station || killer.Hull <= 0f) return;
            var a = OfClient(victim.OwnerClientId);
            var b = OfClient(killer.OwnerClientId);
            if (a == null || b == null) return;
            bool opposite = (a.id == s.attacker && b.id == s.defender) || (a.id == s.defender && b.id == s.attacker);
            if (!opposite) return;
            NetState.Instance.SendAdmin(killer.OwnerClientId, NetAdmin.Order.Heal, 1, 0, 0, null, "");
        }
        /// <summary>A defending pilot docked at their besieged claim: half its remaining lapse days (rounded up) while more than
        /// LapseFloorDays are left (15 -> 8 -> 4 -> 2; 3 or less stays). True = changed.</summary>
        static bool HalveLapse(Claim held, Faction defender)
        {
            double remaining = LapseDays - DaysSince(held.lastDock);
            if (remaining <= LapseFloorDays) return false;
            double left = Math.Ceiling(remaining / 2.0);
            held.lastDock = DateTime.UtcNow.AddDays(-(LapseDays - left)).ToString("o");
            TellFaction(defender, string.Format(Localization.Extra("mpSiegeLapseHalved",
                "{0} is under siege: docking there doesn't keep the claim, it lapses in {1:0} days now. Fight for it."), StationName(held.station), left));
            return true;
        }
        /// <summary>A siege that wasn't fought out: gone, its stake back in the attacker's bank, announced ('format': {0} / {1}
        /// the defenders' tag / name, {2} the station, {3} the stake, {4} the attackers' tag).</summary>
        static void CallOff(Siege s, string format, string defenderTag, string defenderName)
        {
            list.sieges.Remove(s);
            var attacker = list.factions.Find(c => c.id == s.attacker);
            if (attacker != null) attacker.bank += Math.Max(0, s.stake);
            NetState.Instance?.Announce(string.Format(format, defenderTag, defenderName, StationName(s.station), Math.Max(0, s.stake), attacker?.tag));
            Debug.Log($"Server: the siege of {StationName(s.station)} is called off; {s.stake:N0} back to [{attacker?.tag}].");
            Save();
            PublishSieges();
        }
        /// <summary>A siege decided: 'taken' = the attackers take the station; 'reason' (optional) is announced first.</summary>
        static void EndSiege(Siege s, bool taken, string reason = null)
        {
            if (!list.sieges.Remove(s)) return;
            if (!string.IsNullOrEmpty(reason)) NetState.Instance?.Announce(reason);
            var held = ClaimAt(s.station);
            var attacker = list.factions.Find(c => c.id == s.attacker);
            var defender = list.factions.Find(c => c.id == s.defender);
            if (held != null) held.protectedUntil = UnixNow + ProtectionHours * 3600L;
            var winner = taken ? attacker : defender;
            if (winner != null) winner.bank += Math.Max(0, s.stake);   // the stake to the winning side
            if (taken && held != null && attacker != null)
            {
                held.faction = attacker.id;
                held.lastDock = DateTime.UtcNow.ToString("o");
                held.garrisonSize = 0;   // the beaten garrison is gone; the new holder sets its own
                held.garrisonLevel = 1;
                held.garrisonPaidUntil = 0;
                if (defender != null && defender.home == s.station) defender.home = list.claims.Find(c => c.faction == defender.id)?.station ?? -1;
                if (attacker.home < 0 || ClaimAt(attacker.home)?.faction != attacker.id) attacker.home = s.station;
                PublishClaims();
                if (defender != null) RefreshHomes(defender);
                RefreshHomes(attacker);
            }
            NetState.Instance?.Announce(taken
                ? string.Format(Localization.Extra("mpSiegeTaken2", "[{0}] {1} took {2} from [{3}] and wins the {4:N0} credits stake."), attacker?.tag, attacker?.name, StationName(s.station), defender?.tag, Math.Max(0, s.stake))
                : string.Format(Localization.Extra("mpSiegeHeld2", "[{0}] held {1} against [{2}] and wins the {3:N0} credits stake."), defender?.tag, StationName(s.station), attacker?.tag, Math.Max(0, s.stake)));
            Debug.Log($"Server: the siege of {StationName(s.station)} is over: {(taken ? "taken" : "held")}.");
            NetNews.Post(taken ? NetNews.Kind.Breaking : NetNews.Kind.War, taken
                ? $"{NetNews.Place(s.station)} has fallen! {NetNews.FactionName(attacker?.tag, attacker?.name)} seizes it from [{NetNews.Safe(defender?.tag)}]"
                : $"{NetNews.FactionName(defender?.tag, defender?.name)} holds {NetNews.Place(s.station)}: the [{NetNews.Safe(attacker?.tag)}] siege is broken", s.station);
            Save();
            PublishSieges();
        }
        /// <summary>The sieges to the players (NetState.Sieges): "station|attacker TAG|defender TAG|phase (0 announced, 1 armed
        /// and waiting for both sides, 2 fighting)|seconds left (to the arming, the call-off or the end)|stake|garrison
        /// alive|garrison level|station defenders|their level|attackers' kills|defenders' kills|days added" per line.</summary>
        static void PublishSieges()
        {
            if (list == null || NetState.Instance == null) return;
            var sb = new StringBuilder();
            long now = UnixNow;
            foreach (var s in list.sieges)
            {
                var a = list.factions.Find(c => c.id == s.attacker);
                var d = list.factions.Find(c => c.id == s.defender);
                if (a == null || d == null) continue;
                long left = s.started ? s.endsAt - now : s.armed ? s.expiresAt - now : s.startsAt - now;
                sb.Append(s.station).Append('|').Append(a.tag).Append('|').Append(d.tag).Append('|').Append(s.started ? 2 : s.armed ? 1 : 0)
                  .Append('|').Append(Math.Max(0, left)).Append('|').Append(Math.Max(0, s.stake))
                  .Append('|').Append(s.armed ? s.garrisonAlive : 0).Append('|').Append(s.garrisonLevel)
                  .Append('|').Append(s.armed ? DefenderCount(Step(s, now)) : 0).Append('|').Append(DefenderLevel(Step(s, now)))
                  .Append('|').Append(s.attKills).Append('|').Append(s.defKills).Append('|').Append(s.extensions).Append('\n');
            }
            NetState.Instance.SetSieges(sb.ToString());
        }
        // ---- the garrison ----------------------------------------------------------------------------------
        /// <summary>"/faction garrison [ships level] [station]": the claim's garrison (officers), or what it is now. The station is
        /// the one docked at unless given (any of the faction's claims, by number). Setting it pays the new upkeep's first day.</summary>
        static string SetGarrison(ulong client, string me, string arg)
        {
            var faction = Of(me);
            if (faction == null) return Localization.Extra("mpFactionNone", "You aren't in a faction.");
            var words = arg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int station = DockedAt(client);
            if (words.Length >= 3 && int.TryParse(words[2], out int given)) station = given;
            var held = ClaimAt(station);
            if (held == null || held.faction != faction.id) return Localization.Extra("mpGarrisonWhere", "Dock at one of your faction's stations, or name one: /faction garrison <ships> <level> <station>.");
            if (words.Length == 0)
                return string.Format(Localization.Extra("mpGarrisonInfo", "{0}: {1} fighters at level {2}, {3:N0} credits a day."),
                                     StationName(station), held.garrisonSize, held.garrisonLevel, GarrisonCost(held.garrisonSize, held.garrisonLevel));
            if (!IsOfficer(faction, me)) return Localization.Extra("mpFactionNotOfficer", "Only a faction's leader and officers can do that.");
            if (words.Length < 2 || !int.TryParse(words[0], out int ships) || !int.TryParse(words[1], out int level)
                || ships < 0 || ships > MaxGarrison || level < 1 || level > MaxGarrisonLevel)
                return string.Format(Localization.Extra("mpGarrisonUsage", "/faction garrison <ships 0-{0}> <level 1-{1}> [station]"), MaxGarrison, MaxGarrisonLevel);
            var siege = SiegeAt(station);
            if (siege != null && siege.armed) return Localization.Extra("mpGarrisonSiege", "The garrison can't change while the siege runs.");
            int cost = GarrisonCost(ships, level);
            long now = UnixNow;
            if (ships > 0 && (ships > held.garrisonSize || level > held.garrisonLevel || now >= held.garrisonPaidUntil))
            {
                if (faction.bank < cost)
                    return string.Format(Localization.Extra("mpGarrisonCost", "That garrison costs {0:N0} credits a day, paid now from the bank (it has {1:N0})."), cost, faction.bank);
                faction.bank -= cost;
                held.garrisonPaidUntil = now + DaySeconds;
            }
            held.garrisonSize = ships;
            held.garrisonLevel = level;
            Save();
            TellFaction(faction, ships == 0
                ? string.Format(Localization.Extra("mpGarrisonNone", "{0} has no garrison now."), StationName(station))
                : string.Format(Localization.Extra("mpGarrisonSet", "{0}'s garrison: {1} fighters at level {2}, {3:N0} credits a day."), StationName(station), ships, level, cost));
            return "";
        }
        /// <summary>Claim tick: each garrison's day of upkeep from the bank when its paid day is over; disbanded without it.</summary>
        static bool TickUpkeep()
        {
            bool changed = false;
            long now = UnixNow;
            foreach (var c in list.claims)
            {
                if (c.garrisonSize <= 0 || now < c.garrisonPaidUntil) continue;
                var faction = list.factions.Find(x => x.id == c.faction);
                if (faction == null) continue;
                int cost = GarrisonCost(c.garrisonSize, c.garrisonLevel);
                changed = true;
                if (faction.bank >= cost)
                {
                    faction.bank -= cost;
                    c.garrisonPaidUntil = Math.Max(c.garrisonPaidUntil, now - DaySeconds) + DaySeconds;
                    continue;
                }
                c.garrisonSize = 0;
                TellFaction(faction, string.Format(Localization.Extra("mpGarrisonUnpaid", "The bank couldn't pay {0}'s garrison ({1:N0} credits a day): it left."), StationName(c.station), cost));
            }
            return changed;
        }
        /// <summary>NetState.GarrisonKillRpc: the orbit's authority saw one of 'station''s garrison die. Checked: the siege runs,
        /// a fighter is alive, the sender runs that orbit and is there; at most GarrisonKillsPerWindow in GarrisonKillWindow.</summary>
        public static void OnGarrisonKill(ulong client, int station)
        {
            var s = SiegeAt(station);
            if (s == null || !s.armed || s.garrisonAlive <= 0) return;
            var from = NetSquad.Find(client);
            if (from == null || from.RunsOrbit != station || !NetGuard.InOrbit(client, station)) return;
            float t = Time.realtimeSinceStartup;
            if (t - s.killWindowStart > GarrisonKillWindow) { s.killWindowStart = t; s.killsInWindow = 0; }
            if (++s.killsInWindow > GarrisonKillsPerWindow) return;
            s.garrisonAlive--;
            s.garrisonBack.Add(UnixNow + GarrisonRespawnSeconds);
            Save();
            PublishSieges();
        }
    }
}
