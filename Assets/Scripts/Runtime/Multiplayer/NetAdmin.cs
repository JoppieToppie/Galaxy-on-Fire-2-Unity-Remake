// NetAdmin.cs
// Remake multiplayer: the admin commands that act on players' games (NetCommands' table; the chat and the dedicated
// server's console alike). The server checks the rights and the arguments, then sends the order to each target's own game
// (NetState.SendAdmin, server-only), which applies it to its own ship and Session: the server holds no game state.
//   /kill [players]                         destroys their ships (in space; also through god mode)
//   /heal [players]                         hull, shield, armor and the gamma pool full
//   /give [players] <item> [amount] [mount] items into the hold (an item index or name; 1..1000, default 1); "mount" docked:
//                                           mounted like the hangar does (Cheats.GiveAndMount)
//   /credits [players] <amount>             credits added (negative: taken, never below 0)
//   /spawn [players] <ship | object> [race] [count] [enemy | friendly | neutral | standing] [at x y z]
//                                           NPC ships 400 m ahead of each player as their orbit's traffic (DebugSpawner):
//                                           a ship index or name, a race (terran, vossk, nivelian, midorian, pirate, void,
//                                           specter; default the ship's maker, else pirates), 1..10 of them, enemy by
//                                           default; neutral = neither side whatever the standings, until shot; standing =
//                                           by the race's standing; "at x y z": there in that player's orbit (game
//                                           coordinates, what /pos shows), else 400 m ahead of them; a name that is no
//                                           ship is an assembled object (assemblies.json: station_083_terran, ...) placed
//                                           as scenery, ahead of them (far enough out for its size) or there
//   /mute <players> [minutes], /unmute <players>   their chat and whispers dropped on the server (default: the session)
// The debug panel's tools (Cheats, PlayerHull, DebugSpawner), for one player:
//   /ship [players] <ship | own>            fly any hull of the Ships tab (a ship's number or name, the capital ships by
//                                           name; docked only the ownable ones), "own" back to the ship flown before
//   /ammo [players]                         every mounted secondary to 50
//   /reveal [players], /peace [players]     every system on the map; both standing axes neutral, no station grudges
//   /cheat [players] <flag> [on | off]      a debug toggle for the session (god, ammo, cooldown, boost, onehit, locks,
//                                           shopping, jumps; no on / off: toggled), whatever the session allows
//   /title [players] <text> [| subtitle] [for <seconds>]   a big title on their screens (NetScreen; 4 s by default;
//                                           "clear" takes it away)
//   /timer [players] <seconds | m:ss> [label]   a countdown at the top of their screens ("stop" takes it away)
//   /dialog [players] <speaker> : <text> [| [speaker :] next page ...]   the game's dialogue window, page by page, each
//                                           page with its speaker (a page without one keeps the last): a story speaker by name
//                                           (Keith, Gunant...; "Keith as Bob" renames it), a race and a name ("vossk K'ekki",
//                                           "terran female Jane": a random face of that race, the same for everyone and for
//                                           every page of it), or "player": the reader, Keith's face with their pilot name;
//                                           %player% in a text or name is the reader's name; shown after any dialogue already
//                                           open (NetScreen's queue); a page "reward [title]: <rewards>" pays when the
//                                           dialogue closes (like /reward)
//   /reward [players] <credits | item [amount]> [+ more ...] [| title]   the mission payout: credits and / or items into
//                                           the hold, shown in the reward box (NetScreen.ShowReward: "Mission accomplished!"
//                                           or the title, "+ credits", the items with their icons, sound 36)
// The players: names, client ids or selectors (@a @s @p @r, NetCommands.FindTargets); in the chat the issuer when none is
// named. Every order is logged on the server and the target gets a notice naming the admin.

using System;
using System.Globalization;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetAdmin
    {
        public enum Order : byte { Kill = 1, Heal = 2, Give = 3, Credits = 4, Spawn = 5, Ship = 6, Ammo = 7, Reveal = 8, Peace = 9, Cheat = 10, Object = 11, Title = 12, Timer = 13, Dialog = 14, Reward = 15, Scoreboard = 16, Sound = 17, Music = 18, Respawn = 19, Rules = 20, Ask = 21, Provoke = 22, Radio = 23, Waypoint = 24 }

        const int MaxGive = 1000, MaxSpawn = 10, MaxCredits = 999999999;

        static string X(string key, string english) => Localization.Extra(key, english);

        // ---- mutes (server) ----------------------------------------------------------------------------------

        /// <summary>Server: muted client -> until when (unscaled time; infinity = the session).</summary>
        static readonly Dictionary<ulong, float> muted = new Dictionary<ulong, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => muted.Clear();

        /// <summary>Server: a new session (NetState spawned).</summary>
        internal static void Reset() => muted.Clear();

        /// <summary>Server: 'client' may not chat now; 'answer' tells them why.</summary>
        internal static bool IsMuted(ulong client, out string answer)
        {
            answer = null;
            if (!muted.TryGetValue(client, out float until)) return false;
            if (Time.unscaledTime >= until) { muted.Remove(client); return false; }
            answer = float.IsPositiveInfinity(until) ? X("mpMutedYou", "You are muted.")
                : string.Format(X("mpMutedYouFor", "You are muted for {0} more minute(s)."), Mathf.CeilToInt((until - Time.unscaledTime) / 60f));
            return true;
        }

        // ---- the commands (server) --------------------------------------------------------------------------

        /// <summary>The players the arguments start with, else (a chat issuer, 'playersOptional') the issuer and the whole
        /// line; runs 'each' and joins the answers. No arguments at all: the issuer.</summary>
        static string ForTargets(string args, NetPlayer by, bool playersOptional, Func<NetPlayer, string, string> each)
        {
            args = (args ?? "").Trim();
            List<NetPlayer> targets;
            string rest;
            if (args.Length == 0)
            {
                if (by == null) return X("mpAdmNamePlayers", "Name the players (a name, a client id, @a or @r).");
                targets = new List<NetPlayer> { by };
                rest = "";
            }
            else
            {
                targets = NetCommands.FindTargets(args, by, out rest, out string error);
                if (targets.Count == 0)
                {
                    if (!playersOptional || by == null || args.StartsWith("@")) return error;
                    targets = new List<NetPlayer> { by };   // "/give 85 3": the issuer
                    rest = args;
                }
            }
            var answers = new List<string>();
            foreach (var t in targets)
            {
                string a = each(t, rest);
                if (!string.IsNullOrEmpty(a)) answers.Add(a);
            }
            return string.Join("\n", answers);
        }

        static void Send(NetPlayer to, Order order, int a, int b, int c, NetPlayer by, string log, string text = null)
        {
            NetState.Instance.SendAdmin(to.OwnerClientId, order, a, b, c, text, NetCommands.IssuerName(by));
            Debug.Log($"Server: {NetCommands.IssuerName(by)} {log} ({to.DisplayName}, {to.OwnerClientId})");
        }

        static string NotInSpace(NetPlayer t) => string.Format(X("mpAdmNotInSpace", "{0} isn't in space."), t.DisplayName);

        public static string Kill(string args, NetPlayer by) => ForTargets(args, by, false, (t, _) =>
        {
            if (!t.InSpace) return NotInSpace(t);
            if (t.Hull <= 0f) return string.Format(X("mpAdmAlreadyDead", "{0} is already destroyed."), t.DisplayName);
            Send(t, Order.Kill, 0, 0, 0, by, "destroyed a ship");
            NetState.Instance.NoticeAll(string.Format(X("mpAdmKilledAll", "{0} was destroyed by {1}."), t.DisplayName, NetCommands.IssuerName(by)));
            return "";
        });

        public static string Heal(string args, NetPlayer by) => ForTargets(args, by, false, (t, _) =>
        {
            Send(t, Order.Heal, 0, 0, 0, by, "repaired a ship");
            return string.Format(X("mpAdmHealed", "Repaired {0}'s ship."), t.DisplayName);
        });

        /// <summary>/ammo, /reveal, /peace: one order without arguments.</summary>
        public static string Simple(string args, NetPlayer by, Order order) => ForTargets(args, by, false, (t, _) =>
        {
            Send(t, order, 0, 0, 0, by, order.ToString().ToLowerInvariant());
            switch (order)
            {
                case Order.Ammo: return string.Format(X("mpAdmAmmo", "{0}'s secondaries refilled."), t.DisplayName);
                case Order.Reveal: return string.Format(X("mpAdmReveal", "Every system revealed for {0}."), t.DisplayName);
                default: return string.Format(X("mpAdmPeace", "{0} is at peace with every race."), t.DisplayName);
            }
        });

        public static string Give(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            bool mount = rest.EndsWith(" mount", StringComparison.OrdinalIgnoreCase);
            if (mount) rest = rest.Substring(0, rest.Length - 6).Trim();
            if (mount && !t.InHangar) return string.Format(X("mpAdmMountDocked", "{0} isn't docked: mounting works in a hangar."), t.DisplayName);
            if (!ParseAmount(ref rest, 1, MaxGive, out int amount)) return Usage("give");
            int item = FindItem(rest, out string error);
            if (item < 0) return error;
            Send(t, Order.Give, item, amount, mount ? 1 : 0, by, $"gave {amount} x item {item}{(mount ? " (mounted)" : "")}");
            return string.Format(X("mpAdmGave", "Gave {0} {1} x {2}."), t.DisplayName, amount, UI.ItemInfo.ItemName(item));
        });

        public static string Credits(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            if (!int.TryParse(rest.Trim(), out int amount) || amount == 0) return Usage("credits");
            amount = Mathf.Clamp(amount, -MaxCredits, MaxCredits);
            Send(t, Order.Credits, amount, 0, 0, by, $"gave {amount} credits");
            return string.Format(amount > 0 ? X("mpAdmCreditsGiven", "Gave {0} {1} credits.") : X("mpAdmCreditsTaken", "Took {1} credits from {0}."),
                t.DisplayName, Math.Abs(amount));
        });

        public static string Spawn(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            if (!t.InSpace) return NotInSpace(t);
            if (!TakeAt(ref rest, out string at)) return Usage("spawn");
            // From the end: the behaviour, the count and the race, in any order; the rest is the ship.
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (words.Count == 0) return Usage("spawn");
            var behaviour = DebugSpawner.Behaviour.Hostile;
            int count = 1, race = -1;
            while (words.Count > 1)
            {
                string w = words[words.Count - 1];
                var b = BehaviourWord(w);
                if (b.HasValue) behaviour = b.Value;
                else if (int.TryParse(w, out int n)) count = Mathf.Clamp(n, 1, MaxSpawn);
                else if (RaceWord(w) >= 0) race = RaceWord(w);
                else break;
                words.RemoveAt(words.Count - 1);
            }
            string spec = string.Join(" ", words);
            int ship = FindShip(spec, out string error);
            if (ship < 0)
            {
                // Not a ship: an assembled object (the whole name, before any words were taken as a race or a count).
                var asm = FindAssembly(rest.Trim(), out string objectError);
                if (asm == null) return ship == -2 ? error : objectError ?? error;
                Send(t, Order.Object, 0, 0, 0, by, $"spawned object {asm.name}{(at != null ? " at " + at : "")}", at != null ? asm.name + "@" + at : asm.name);
                return string.Format(X("mpAdmObject", "Spawning {0} at {1}."), asm.name, t.DisplayName);
            }
            if (race < 0)
            {
                race = Shop.ShipMakerRace(ship);
                if (race < 0 || (race > 3 && race != Standing.Pirate && race != Standing.Void && race != Standing.Specter)) race = Standing.Pirate;
            }
            int tag = NetEvents.NewBatch(count, behaviour == DebugSpawner.Behaviour.Hostile);   // an event's spawn: counted (enemies / ships)
            Send(t, Order.Spawn, ship, race, tag << 12 | count << 8 | (int)behaviour, by,
                $"spawned {count} x ship {ship} (race {race}, {behaviour}{(at != null ? ", at " + at : "")}{(tag != 0 ? ", event batch " + tag : "")})", at);
            return string.Format(X("mpAdmSpawned", "Spawning {0} x {1} at {2}."), count, DebugSpawner.ShipName(NetGame.Db, ship), t.DisplayName);
        });

        public static string Ship(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (rest.Length == 0) return Usage("ship");
            if (string.Equals(rest, "own", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Ship, -1, 0, 0, by, "sent back to their own ship");
                return string.Format(X("mpAdmShipOwn", "{0} goes back to their own ship."), t.DisplayName);
            }
            var hull = FindHull(rest, out string error);
            if (hull == null) return error;
            if (!t.InSpace && !hull.playerShip)
                return string.Format(X("mpAdmShipHangar", "{0} is docked: that ship doesn't fit in a hangar."), t.DisplayName);
            Send(t, Order.Ship, 0, 0, 0, by, $"swapped the ship to {hull.key}", hull.key);
            return string.Format(X("mpAdmShip", "{0} flies the {1}."), t.DisplayName, HullName(hull));
        });

        public static string Cheat(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            var words = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return Usage("cheat") + "  " + FlagList();
            int flag = Array.FindIndex(Cheats.Flags, f => string.Equals(f.word, words[0], StringComparison.OrdinalIgnoreCase));
            if (flag < 0) return string.Format(X("mpAdmNoCheat", "No cheat \"{0}\": {1}"), words[0], FlagList());
            int state = 2;   // toggle
            if (words.Length > 1)
            {
                string w = words[1].ToLowerInvariant();
                if (w == "on" || w == "true" || w == "1") state = 1;
                else if (w == "off" || w == "false" || w == "0") state = 0;
                else return Usage("cheat");
            }
            Send(t, Order.Cheat, flag, state, 0, by, $"cheat {Cheats.Flags[flag].word} {(state == 2 ? "toggled" : state == 1 ? "on" : "off")}");
            return string.Format(X("mpAdmCheat", "{0}: {1} {2}."), t.DisplayName, Cheats.Flags[flag].word,
                state == 2 ? X("mpAdmToggled", "toggled") : state == 1 ? X("mpAdmOn", "on") : X("mpAdmOff", "off"));
        });

        public static string Title(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (rest.Length == 0) return Usage("title");
            if (string.Equals(rest, "clear", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Title, 0, 0, 0, by, "cleared the title", "");
                return "";
            }
            int seconds = 4;
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (words.Count >= 3 && string.Equals(words[words.Count - 2], "for", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(words[words.Count - 1], out int s) && s > 0)
            {
                seconds = Mathf.Min(s, 3600);
                rest = string.Join(" ", words.GetRange(0, words.Count - 2));
            }
            int bar = rest.IndexOf('|');
            string main = NetChat.Clean(bar < 0 ? rest : rest.Substring(0, bar)), sub = bar < 0 ? "" : NetChat.Clean(rest.Substring(bar + 1));
            if (main.Length + sub.Length == 0) return Usage("title");
            Send(t, Order.Title, seconds * 1000, 0, 0, by, $"title \"{main}\" / \"{sub}\" for {seconds} s", main + "\n" + sub);
            return "";
        });

        public static string Dialog(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            // Pages: "speaker : text", a page without a speaker keeps the last one. Each line sent: id, name, face, text
            // (separated by \u001f): id >= 0 a story speaker (a name = renamed), -1 a generated face, -2 the reader.
            var lines = new List<string>();
            var faces = new Dictionary<string, string>();
            string current = null;
            foreach (var part in rest.Split('|'))
            {
                string page = part.Trim();
                int colon = page.IndexOf(':');
                string prefix = colon > 0 ? page.Substring(0, colon).Trim() : "";
                if (prefix.Equals("reward", StringComparison.OrdinalIgnoreCase) || prefix.StartsWith("reward ", StringComparison.OrdinalIgnoreCase))
                {
                    // A reward page: paid when the dialogue closes.
                    if (!ParseReward(page.Substring(colon + 1), out int credits, out string items, out string why)) return why;
                    string heading = NetChat.Clean(prefix.Substring(6).Trim()).Replace("\u001f", " ");
                    lines.Add($"R\u001f{credits}\u001f{items}\u001f{heading}");
                    continue;
                }
                if (colon > 0)
                {
                    string spec = ResolveSpeaker(page.Substring(0, colon).Trim(), faces);
                    if (spec != null) { current = spec; page = page.Substring(colon + 1).Trim(); }
                    else if (current == null) return string.Format(X("mpAdmNoSpeaker", "No speaker \"{0}\": a story character's name (Keith as Bob renames), a race and a name (vossk K'ekki), or player."), page.Substring(0, colon).Trim());
                }
                if (current == null) return Usage("dialog");
                page = NetChat.Clean(page).Replace("\u001f", " ");
                if (page.Length > 0 && lines.Count < 20) lines.Add(current + "\u001f" + page);
            }
            if (lines.Count == 0) return Usage("dialog");
            Send(t, Order.Dialog, 0, 0, 0, by, $"dialog, {lines.Count} page(s)", string.Join("\n", lines));
            return "";
        });

        public static string Reward(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            int bar = rest.IndexOf('|');
            string heading = bar < 0 ? "" : NetChat.Clean(rest.Substring(bar + 1));
            if (!ParseReward(bar < 0 ? rest : rest.Substring(0, bar), out int credits, out string items, out string why)) return why;
            Send(t, Order.Reward, credits, 0, 0, by, $"reward {credits} credits, items {items}", heading + "\n" + items);
            return string.Format(X("mpAdmRewarded", "Rewarded {0}."), t.DisplayName);
        });

        /// <summary>"5000 + Khador Drive + Energy Cells 10": the credits (summed) and the items as "item:amount;..." (false with
        /// the reason: nothing, an unknown item).</summary>
        static bool ParseReward(string spec, out int credits, out string items, out string error)
        {
            credits = 0;
            items = "";
            error = null;
            var list = new List<string>();
            foreach (var raw in spec.Split(new[] { '+', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                string digits = part.Replace("$", "").Replace(" ", "");
                if (long.TryParse(digits, out long c) && c > 0) { credits = (int)Math.Min((long)MaxCredits, credits + c); continue; }
                if (!ParseAmount(ref part, 1, MaxGive, out int amount)) { error = Usage("reward"); return false; }
                int item = FindItem(part, out string why);
                if (item < 0) { error = why; return false; }
                if (list.Count < 8) list.Add(item + ":" + amount);
            }
            items = string.Join(";", list);
            if (credits == 0 && list.Count == 0) { error = Usage("reward"); return false; }
            return true;
        }

        /// <summary>This game: credits and items into the hold, and the reward box.</summary>
        static void GrantReward(int credits, string items, string heading)
        {
            var list = new List<(int, int)>();
            foreach (var entry in (items ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = entry.Split(':');
                if (kv.Length != 2 || !int.TryParse(kv[0], out int item) || !int.TryParse(kv[1], out int amount) || !NetGuard.Item(item) || amount < 1) continue;
                amount = Mathf.Min(amount, MaxGive);
                Cheats.GiveItem(item, amount);
                list.Add((item, amount));
            }
            credits = Mathf.Clamp(credits, 0, MaxCredits);
            if (credits > 0) Session.Credits = (int)Math.Min((long)Session.Credits + credits, MaxCredits);
            NetScreen.ShowReward(heading, credits, list);
        }

        /// <summary>A page's speaker as "id\u001fname\u001fface" (NetAdmin.Apply), null = not a speaker: "player", a story
        /// speaker ("Keith", "Keith as Bob"), or "&lt;race&gt; [female | male] [name]" with a face made once per dialog.</summary>
        internal static string ResolveSpeaker(string who, Dictionary<string, string> faces)
        {
            if (who.Length == 0) return null;
            if (who.Equals("player", StringComparison.OrdinalIgnoreCase) || who.Equals("you", StringComparison.OrdinalIgnoreCase))
                return "-2\u001f%player%\u001f";
            string rename = "";
            int asAt = who.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            string baseName = asAt > 0 ? who.Substring(0, asAt).Trim() : who;
            if (asAt > 0) rename = NetChat.Clean(who.Substring(asAt + 4).Trim()).Replace("\u001f", " ");
            int speaker = FindSpeaker(baseName);
            if (speaker >= 0) return speaker + "\u001f" + rename + "\u001f";
            if (faces.TryGetValue(who.ToLowerInvariant(), out string known)) return known;
            var words = new List<string>(who.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int race = RaceWord(words[0]);
            if (race < 0) return null;
            words.RemoveAt(0);
            bool male = true;
            if (words.Count > 0 && (words[0].Equals("female", StringComparison.OrdinalIgnoreCase) || words[0].Equals("male", StringComparison.OrdinalIgnoreCase)))
            {
                male = words[0].Equals("male", StringComparison.OrdinalIgnoreCase);
                words.RemoveAt(0);
            }
            string name = NetChat.Clean(string.Join(" ", words)).Replace("\u001f", " ");
            if (name.Length == 0) name = Localization.Get(406 + race);
            string spec = "-1\u001f" + name + "\u001f" + string.Join(",", AgentGenerator.CreatePortrait(male, race));
            faces[who.ToLowerInvariant()] = spec;
            return spec;
        }

        /// <summary>A story speaker by name (whole, or the one it starts, any case); -1 = none.</summary>
        static int FindSpeaker(string name)
        {
            int found = -1, matches = 0;
            for (int i = 0; i < StoryTable.SpeakerCount; i++)
            {
                string n = StoryTable.SpeakerName(i);
                if (string.IsNullOrEmpty(n)) continue;
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return i;
                if (n.StartsWith(name, StringComparison.OrdinalIgnoreCase) || n.Split(' ')[0].Equals(name, StringComparison.OrdinalIgnoreCase)) { found = i; matches++; }
            }
            return matches == 1 ? found : -1;
        }

        public static string Timer(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            int space = rest.IndexOf(' ');
            string first = space < 0 ? rest : rest.Substring(0, space), label = space < 0 ? "" : NetChat.Clean(rest.Substring(space + 1));
            if (string.Equals(first, "stop", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Timer, -1, 0, 0, by, "stopped the timer", "");
                return "";
            }
            int seconds;
            int colon = first.IndexOf(':');
            if (colon > 0 && int.TryParse(first.Substring(0, colon), out int m) && int.TryParse(first.Substring(colon + 1), out int sec)) seconds = m * 60 + sec;
            else if (!int.TryParse(first, out seconds)) return Usage("timer");
            if (seconds <= 0) return Usage("timer");
            seconds = Mathf.Min(seconds, 24 * 3600);
            Send(t, Order.Timer, seconds, 0, 0, by, $"timer {seconds} s \"{label}\"", label);
            return "";
        });

        /// <summary>/pvp &lt;on | off&gt;: free for all, every player an enemy of every other (squadmates excepted).</summary>
        public static string Pvp(string args, NetPlayer by)
        {
            string a = (args ?? "").Trim().ToLowerInvariant();
            if (a != "on" && a != "off") return Usage("pvp");
            NetState.Instance.SetFreeForAll(a == "on");
            NetEvents.NoteFreeForAll(a == "on");
            NetState.Instance.NoticeAll(a == "on" ? X("mpPvpOn", "Free for all: every pilot is your enemy.") : X("mpPvpOff", "The free for all is over."));
            Debug.Log($"Server: {NetCommands.IssuerName(by)} turned free for all {a}");
            return "";
        }

        /// <summary>/respawn [players] &lt;station [x y z] [spread &lt;units&gt;] [delay &lt;s&gt;] | off&gt;: where their ship comes back
        /// after being destroyed: in that orbit (at x y z, else 3 km in front of its station, spread around it), not docked.</summary>
        public static string Respawn(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (string.Equals(rest, "off", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Respawn, -1, 0, 0, by, "respawn off", "off");
                return "";
            }
            if (!NetTeleport.ParseStation(rest, out int station, out string after)) return Usage("respawn");
            var words = new List<string>(after.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            float spread = 2000f, delay = 5f;
            for (int i = words.Count - 2; i >= 0; i--)
            {
                string w = words[i].ToLowerInvariant();
                if ((w == "spread" || w == "delay") && float.TryParse(words[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    if (w == "spread") spread = Mathf.Clamp(v, 0f, 100000f); else delay = Mathf.Clamp(v, 1f, 120f);
                    words.RemoveRange(i, 2);
                }
            }
            string pos = "";
            if (words.Count == 3)
            {
                foreach (var w in words) if (!float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out float c) || Mathf.Abs(c) > 1e7f) return Usage("respawn");
                pos = string.Join(" ", words);
            }
            else if (words.Count != 0) return Usage("respawn");
            string payload = string.Join("|", station.ToString(CultureInfo.InvariantCulture), pos,
                spread.ToString(CultureInfo.InvariantCulture), delay.ToString(CultureInfo.InvariantCulture));
            Send(t, Order.Respawn, station, 0, 0, by, $"respawn point {payload}", payload);
            NetEvents.NoteRespawnSet();
            return "";
        });

        /// <summary>/restrict [players] &lt;jumps | docking | off&gt; ...: no jumps (planet, gate, Khador) and / or no docking for them.</summary>
        public static string Restrict(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            int flags = 0;
            foreach (string w in rest.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w == "jumps" || w == "jump") flags |= NetEventRules.Jumps;
                else if (w == "docking" || w == "dock") flags |= NetEventRules.Docking;
                else if (w == "all") flags |= NetEventRules.Jumps | NetEventRules.Docking;
                else if (w != "off") return Usage("restrict");
            }
            Send(t, Order.Rules, flags, 0, 0, by, $"travel restricted ({flags})");
            if (flags != 0) NetEvents.NoteRulesSet();
            return "";
        });

        /// <summary>/provoke [players] [race] [within &lt;m&gt;]: the NPC ships around them (that race; within that many metres,
        /// else the whole orbit) turn on them and their squad, like ships they shot (NpcShip.aggressors); sent to each ship's
        /// own game (the orbit's authority, or whoever spawned it) by its NetProxy.</summary>
        public static string Provoke(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            if (!t.InSpace) return NotInSpace(t);
            int race = -1;
            float within = 0f;
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            for (int i = 0; i < words.Count; i++)
            {
                if (words[i].Equals("within", StringComparison.OrdinalIgnoreCase) && i + 1 < words.Count
                    && float.TryParse(words[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float m)) { within = Mathf.Max(0f, m); i++; }
                else if (RaceWord(words[i]) >= 0) race = RaceWord(words[i]);
                else return Usage("provoke");
            }
            var byOwner = new Dictionary<ulong, List<int>>();
            foreach (var p in UnityEngine.Object.FindObjectsByType<NetProxy>())
            {
                if (p == null || !p.IsSpawned || !p.FlyingNow || p.IsWingman || p.Station != t.Station) continue;
                if (race >= 0 && p.Race != race) continue;
                if (within > 0f && (p.WorldPosition - t.Position).magnitude > within) continue;
                if (!byOwner.TryGetValue(p.OwnerClientId, out var ids)) byOwner[p.OwnerClientId] = ids = new List<int>();
                ids.Add(p.LocalId);
            }
            int count = 0;
            foreach (var kv in byOwner)
            {
                string payload = t.OwnerClientId.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", kv.Value);
                NetState.Instance.SendAdmin(kv.Key, Order.Provoke, 0, 0, 0, payload, NetCommands.IssuerName(by));
                count += kv.Value.Count;
            }
            Debug.Log($"Server: {NetCommands.IssuerName(by)} turned {count} ship(s) on {t.DisplayName}");
            return string.Format(X("mpAdmProvoked", "{0} ship(s) turned on {1}."), count, t.DisplayName);
        });

        /// <summary>/radio [players] &lt;speaker&gt; : &lt;text&gt;: a line in their flight HUD's radio box (a face and a name, gone by
        /// itself; docked: a chat line). The speakers as /dialog's (ResolveSpeaker).</summary>
        public static string Radio(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            int colon = rest.IndexOf(':');
            if (colon <= 0) return Usage("radio");
            string spec = ResolveSpeaker(rest.Substring(0, colon).Trim(), new Dictionary<string, string>());
            if (spec == null) return string.Format(X("mpAdmNoSpeaker", "No speaker \"{0}\": a story character's name (Keith as Bob renames), a race and a name (vossk K'ekki), or player."), rest.Substring(0, colon).Trim());
            string line = NetChat.Clean(rest.Substring(colon + 1)).Replace("\u001f", " ");
            if (line.Length == 0) return Usage("radio");
            Send(t, Order.Radio, 0, 0, 0, by, "radio", spec + "\u001f" + line);
            return "";
        });

        /// <summary>/waypoint [players] &lt;station x y z | off&gt;: a lockable "Waypoint" at those game coordinates in that orbit
        /// (NetEventWaypoint), the autopilot flies there; reaching it clears it.</summary>
        public static string Waypoint(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (string.Equals(rest, "off", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Waypoint, 0, 0, 0, by, "waypoint off", "off");
                return "";
            }
            if (!NetTeleport.ParseStation(rest, out int station, out string after)) return Usage("waypoint");
            var c = after.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (c.Length != 3) return Usage("waypoint");
            foreach (var w in c) if (!float.TryParse(w, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || Mathf.Abs(v) > 1e7f) return Usage("waypoint");
            Send(t, Order.Waypoint, station, 0, 0, by, "waypoint", station.ToString(CultureInfo.InvariantCulture) + " " + string.Join(" ", c));
            NetEvents.NoteWaypointSet();
            return "";
        });

        /// <summary>/sound [players] &lt;sound&gt;: an event sound (NetEventAudio) on their game.</summary>
        public static string Sound(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            if (!NetEventAudio.TryParse(rest.Trim(), out EventSound sound))
                return string.Format(X("mpAdmNoSound", "Sounds: {0}."), NetEventAudio.Names<EventSound>());
            Send(t, Order.Sound, (int)sound, 0, 0, by, $"sound {sound}");
            return "";
        });

        /// <summary>/music [players] &lt;track | stop&gt;: an event track looped instead of their scene's music.</summary>
        public static string Music(string args, NetPlayer by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (string.Equals(rest, "stop", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Music, -1, 0, 0, by, "music stop");
                return "";
            }
            if (!NetEventAudio.TryParse(rest, out EventMusic track))
                return string.Format(X("mpAdmNoMusic", "Music: {0}, or stop."), NetEventAudio.Names<EventMusic>());
            Send(t, Order.Music, (int)track, 0, 0, by, $"music {track}");
            return "";
        });

        public static string Mute(string args, NetPlayer by, bool on) => ForTargets(args, by, false, (t, rest) =>
        {
            if (!on)
            {
                if (!muted.Remove(t.OwnerClientId)) return string.Format(X("mpAdmNotMuted", "{0} isn't muted."), t.DisplayName);
                NetState.Instance.NoticeTo(t, X("mpUnmutedYou", "You can chat again."));
                Debug.Log($"Server: {NetCommands.IssuerName(by)} unmuted {t.DisplayName} ({t.OwnerClientId})");
                return string.Format(X("mpAdmUnmuted", "{0} can chat again."), t.DisplayName);
            }
            if (t == by) return X("mpCmdNotYourself", "Not on yourself.");
            if (NetCommands.IsHostPlayer(t) || (NetCommands.IsAdmin(t) && by != null && !NetCommands.IsHostPlayer(by)))
                return string.Format(X("mpAdmCantMute", "{0} can't be muted."), t.DisplayName);
            float minutes = float.TryParse(rest.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m) && m > 0f ? m : 0f;
            muted[t.OwnerClientId] = minutes > 0f ? Time.unscaledTime + minutes * 60f : float.PositiveInfinity;
            IsMuted(t.OwnerClientId, out string text);
            NetState.Instance.NoticeTo(t, text);
            Debug.Log($"Server: {NetCommands.IssuerName(by)} muted {t.DisplayName} ({t.OwnerClientId}){(minutes > 0f ? $" for {minutes} min" : "")}");
            return minutes > 0f ? string.Format(X("mpAdmMutedFor", "{0} is muted for {1} minute(s)."), t.DisplayName, minutes)
                                : string.Format(X("mpAdmMuted", "{0} is muted for this session."), t.DisplayName);
        });

        // ---- argument parsing --------------------------------------------------------------------------------

        static string Usage(string command) => NetCommands.UsageOf(command);

        /// <summary>Takes a trailing "at x y z" (game coordinates) off 'rest': 'at' = "x y z" (invariant), null without one;
        /// false = an "at" without three finite numbers.</summary>
        static bool TakeAt(ref string rest, out string at)
        {
            at = null;
            var words = new List<string>(rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            int i = words.FindLastIndex(w => string.Equals(w, "at", StringComparison.OrdinalIgnoreCase));
            if (i < 0) return true;
            if (i != words.Count - 4) return false;
            var v = new float[3];
            for (int k = 0; k < 3; k++)
                if (!float.TryParse(words[i + 1 + k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[k])
                    || !NetGuard.Finite(v[k]) || Mathf.Abs(v[k]) > 2e7f) return false;
            at = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2}", v[0], v[1], v[2]);
            rest = string.Join(" ", words.GetRange(0, i));
            return true;
        }

        /// <summary>"x y z" game coordinates (TakeAt) as a Unity position; null when empty or broken.</summary>
        static Vector3? ParseAt(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var w = text.Split(' ');
            if (w.Length != 3) return null;
            var v = new float[3];
            for (int k = 0; k < 3; k++)
                if (!float.TryParse(w[k], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[k]) || !NetGuard.Finite(v[k]))
                    return null;
            return OrbitLayout.ToUnity(new Vector3(v[0], v[1], v[2]));
        }

        /// <summary>A trailing whole number (with more before it) is the amount, else 'fallback'; false = nothing left or a
        /// number below 1.</summary>
        static bool ParseAmount(ref string rest, int fallback, int max, out int amount)
        {
            amount = fallback;
            rest = rest.Trim();
            int space = rest.LastIndexOf(' ');
            if (space > 0 && int.TryParse(rest.Substring(space + 1), out int n))
            {
                if (n < 1) return false;
                amount = Mathf.Min(n, max);
                rest = rest.Substring(0, space).Trim();
            }
            return rest.Length > 0;
        }

        /// <summary>An item by its index, its whole name or the one name it starts (any case); -1 with the reason.</summary>
        internal static int FindItem(string spec, out string error)
        {
            error = null;
            var db = NetGame.Db;
            if (int.TryParse(spec, out int index) && NetGuard.Item(index)) return index;
            int found = -1, matches = 0;
            foreach (var it in db.Items)
            {
                string n = UI.ItemInfo.ItemName(it.index);
                if (string.IsNullOrEmpty(n)) n = it.name ?? "";
                if (string.Equals(n, spec, StringComparison.OrdinalIgnoreCase)) return it.index;
                if (n.StartsWith(spec, StringComparison.OrdinalIgnoreCase)) { found = it.index; matches++; }
            }
            if (matches == 1) return found;
            error = matches > 1 ? string.Format(X("mpAdmItemAmbiguous", "\"{0}\" fits {1} items: write more of the name, or its number."), spec, matches)
                                : string.Format(X("mpAdmNoItem", "No item \"{0}\"."), spec);
            return -1;
        }

        /// <summary>An assembled object (assemblies.json) by its whole name or the one name it starts (any case); null with
        /// the reason.</summary>
        internal static AssemblyData FindAssembly(string spec, out string error)
        {
            error = null;
            var all = NetGame.Db.Assemblies;
            var asm = all.Find(a => string.Equals(a.name, spec, StringComparison.OrdinalIgnoreCase));
            if (asm != null) return asm;
            var starts = all.FindAll(a => a.name.StartsWith(spec, StringComparison.OrdinalIgnoreCase));
            if (starts.Count == 1) return starts[0];
            error = starts.Count > 1
                ? string.Format(X("mpAdmObjectAmbiguous", "\"{0}\" fits {1} objects (e.g. {2}): write more of the name."), spec, starts.Count, starts[0].name)
                : string.Format(X("mpAdmNoShipOrObject", "No ship or object \"{0}\" (a ship's number or name, or an assemblies.json name like station_083_terran)."), spec);
            return null;
        }

        /// <summary>A ship of ships.json by its index, its whole name or the one name it starts; -1 with the reason (-2: the
        /// name fits several ships).</summary>
        internal static int FindShip(string spec, out string error)
        {
            error = null;
            var db = NetGame.Db;
            if (int.TryParse(spec, out int index) && db.Ship(index) != null && CustomShips.Offered(index)) return index;
            int found = -1, matches = 0;
            foreach (var s in db.Ships)
            {
                if (!CustomShips.Offered(s.index)) continue;   // custom ships: not in multiplayer for now
                string n = DebugSpawner.ShipName(db, s.index);
                if (string.Equals(n, spec, StringComparison.OrdinalIgnoreCase)) return s.index;
                if (spec.Length > 0 && n.StartsWith(spec, StringComparison.OrdinalIgnoreCase)) { found = s.index; matches++; }
            }
            if (matches == 1) return found;
            error = matches > 1 ? string.Format(X("mpAdmShipAmbiguous", "\"{0}\" fits {1} ships: write more of the name, or its number."), spec, matches)
                                : string.Format(X("mpAdmNoShip", "No ship \"{0}\"."), spec);
            return matches > 1 ? -2 : -1;
        }

        internal static string HullName(PlayerHull.Hull h)
        {
            int i = h.label.IndexOf(" · ");
            return i >= 0 ? h.label.Substring(i + 3) : h.label;
        }

        /// <summary>A hull of the debug Ships tab by its ship number, its key, its whole name or the one name it starts; null
        /// with the reason.</summary>
        internal static PlayerHull.Hull FindHull(string spec, out string error)
        {
            error = null;
            var all = PlayerHull.Offered(NetGame.Db);
            if (int.TryParse(spec, out int index))
            {
                var byIndex = all.Find(h => h.key == "ship_" + index) ?? all.Find(h => h.stats == index);
                if (byIndex != null) return byIndex;
            }
            PlayerHull.Hull found = null;
            int matches = 0;
            foreach (var h in all)
            {
                string n = HullName(h);
                if (string.Equals(n, spec, StringComparison.OrdinalIgnoreCase) || string.Equals(h.key, spec, StringComparison.OrdinalIgnoreCase)) return h;
                if (n.StartsWith(spec, StringComparison.OrdinalIgnoreCase)) { found = h; matches++; }
            }
            if (matches == 1) return found;
            error = matches > 1 ? string.Format(X("mpAdmShipAmbiguous", "\"{0}\" fits {1} ships: write more of the name, or its number."), spec, matches)
                                : string.Format(X("mpAdmNoShip", "No ship \"{0}\"."), spec);
            return null;
        }

        static string FlagList()
        {
            var words = new List<string>();
            foreach (var f in Cheats.Flags) words.Add(f.word);
            return string.Join(", ", words);
        }

        static DebugSpawner.Behaviour? BehaviourWord(string w)
        {
            switch (w.ToLowerInvariant())
            {
                case "enemy": case "hostile": return DebugSpawner.Behaviour.Hostile;
                case "friendly": case "friend": return DebugSpawner.Behaviour.Friendly;
                case "neutral": return DebugSpawner.Behaviour.Neutral;
                case "standing": return DebugSpawner.Behaviour.Normal;
            }
            return null;
        }

        internal static int RaceWord(string w)
        {
            switch (w.ToLowerInvariant())
            {
                case "terran": case "terrans": return 0;
                case "vossk": return 1;
                case "nivelian": case "nivelians": return 2;
                case "midorian": case "midorians": return 3;
                case "pirate": case "pirates": return Standing.Pirate;
                case "void": return Standing.Void;
                case "specter": case "specters": return Standing.Specter;
            }
            foreach (int r in new[] { 0, 1, 2, 3, Standing.Pirate, Standing.Void, Standing.Specter })
                if (string.Equals(Localization.Get(406 + r), w, StringComparison.OrdinalIgnoreCase)) return r;
            return -1;
        }

        // ---- the target's game ------------------------------------------------------------------------------

        static void Notice(string by, string text) => NetChat.Notice(string.Format(X("mpAdmByYou", "{0}: {1}"), by, text));

        /// <summary>An admin's order on this game's own ship and Session (NetState.AdminRpc, sent only by the server).</summary>
        internal static void Apply(Order order, int a, int b, int c, string text, string by)
        {
            var level = UnityEngine.Object.FindAnyObjectByType<SpaceLevel>();
            var docked = level == null ? UnityEngine.Object.FindAnyObjectByType<StationLevel>() : null;
            switch (order)
            {
                case Order.Kill:
                    if (level == null || level.Health == null || level.Health.Dead) return;
                    level.Health.Kill(true);
                    break;
                case Order.Heal:
                    Cheats.Repair();
                    NetChat.Notice(string.Format(X("mpAdmHealedYou", "{0} repaired your ship."), by));
                    break;
                case Order.Give:
                    if (!NetGuard.Item(a) || b < 1) return;
                    b = Mathf.Min(b, MaxGive);
                    if (c == 1 && docked != null && docked.Stock != null)
                        Notice(by, $"{UI.ItemInfo.ItemName(a)}: {Cheats.GiveAndMount(NetGame.Db, docked.Stock, a, b)}");
                    else
                    {
                        Cheats.GiveItem(a, b);
                        NetChat.Notice(string.Format(X("mpAdmGaveYou", "{0} gave you {1} x {2}."), by, b, UI.ItemInfo.ItemName(a)));
                    }
                    break;
                case Order.Credits:
                    Session.Credits = (int)Mathf.Clamp((long)Session.Credits + a, 0L, MaxCredits);
                    NetChat.Notice(string.Format(a > 0 ? X("mpAdmCreditsYou", "{0} gave you {1} credits.") : X("mpAdmCreditsTakenYou", "{0} took {1} credits."),
                        by, Math.Abs(a)));
                    break;
                case Order.Spawn:
                    if (level == null || NetGame.Db.Ship(a) == null || !CustomShips.Offered(a)) return;
                    Notice(by, DebugSpawner.SpawnShip(level, b, a, (DebugSpawner.Behaviour)Mathf.Clamp(c & 0xff, 0, 3), Mathf.Clamp((c >> 8) & 0xf, 1, MaxSpawn),
                        ParseAt(text), c >> 12));
                    break;
                case Order.Ship:
                    if (a < 0) { Notice(by, PlayerHull.Restore(level, docked)); break; }
                    var hull = PlayerHull.Offered(NetGame.Db).Find(h => h.key == text);
                    if (hull != null) Notice(by, PlayerHull.Fly(hull, level, docked));
                    break;
                case Order.Ammo:
                    Cheats.RefillAmmo(NetGame.Db);
                    NetChat.Notice(string.Format(X("mpAdmAmmoYou", "{0} refilled your secondaries."), by));
                    break;
                case Order.Reveal:
                    Cheats.RevealAllSystems();
                    NetChat.Notice(string.Format(X("mpAdmRevealYou", "{0} revealed every system on your map."), by));
                    break;
                case Order.Peace:
                    Cheats.MakePeace();
                    NetChat.Notice(string.Format(X("mpAdmPeaceYou", "{0} made peace for you with every race."), by));
                    break;
                case Order.Cheat:
                    if (a < 0 || a >= Cheats.Flags.Length) return;
                    bool now = Cheats.Grant(Cheats.Flags[a].key, b == 2 ? (bool?)null : b == 1);
                    NetChat.Notice(string.Format(now ? X("mpAdmCheatOnYou", "{0} turned {1} on for you.") : X("mpAdmCheatOffYou", "{0} turned {1} off for you."),
                        by, Cheats.Flags[a].word));
                    break;
                case Order.Title:
                {
                    int nl = (text ?? "").IndexOf('\n');
                    NetScreen.ShowTitle(nl < 0 ? text : text.Substring(0, nl), nl < 0 ? "" : text.Substring(nl + 1), a / 1000f);
                    break;
                }
                case Order.Timer:
                    NetScreen.ShowTimer(a, text);
                    break;
                case Order.Scoreboard:
                    NetScreen.ShowScoreboard(text);
                    break;
                case Order.Sound:
                    NetScreen.PlaySound(a);
                    break;
                case Order.Respawn:
                    NetEventRespawn.Apply(text);
                    break;
                case Order.Rules:
                    NetEventRules.Apply(a);
                    break;
                case Order.Waypoint:
                    NetEventWaypoint.Apply(text);
                    break;
                case Order.Radio:
                {
                    // "id US name US face US text": a story speaker (a name = renamed), a generated face (-1) or the reader (-2).
                    var f = (text ?? "").Split('\u001f');
                    if (f.Length < 4 || !int.TryParse(f[0], out int id)) return;
                    string me = NetPlayer.Local != null ? NetPlayer.Local.DisplayName : "";
                    var line = new Traffic.Chatter { text = f[3].Replace("%player%", me), speaker = f[1].Replace("%player%", me) };
                    if (id == -1)
                    {
                        var parts = f[2].Split(',');
                        line.portrait = new int[parts.Length];
                        for (int k = 0; k < parts.Length; k++) int.TryParse(parts[k], out line.portrait[k]);
                    }
                    else
                    {
                        if (id == -2) { id = 0; line.speaker = me; }
                        line.speakerId = id >= 0 && id < StoryTable.SpeakerCount ? id : 0;
                        if (string.IsNullOrEmpty(line.speaker)) line.speaker = StoryTable.SpeakerName(line.speakerId);
                    }
                    if (level != null && level.Traffic != null) level.Traffic.Say(line);
                    else NetChat.Notice($"[{line.speaker}] {line.text}");   // docked: no radio box
                    break;
                }
                case Order.Provoke:
                {
                    // "client:id,id,...": this game's traffic ships by index turn on that player (and their squad).
                    var traffic = level != null ? level.Traffic : null;
                    int colon = (text ?? "").IndexOf(':');
                    if (traffic == null || colon <= 0 || !ulong.TryParse(text.Substring(0, colon), out ulong client)) return;
                    foreach (string idText in text.Substring(colon + 1).Split(','))
                    {
                        if (!int.TryParse(idText, out int id) || id < 0 || id >= traffic.Ships.Count) continue;
                        var ship = traffic.Ships[id];
                        if (ship != null) ship.aggressors.Add(client);
                    }
                    break;
                }
                case Order.Ask:
                    if (a < 0) NetScreen.CloseQuestion(-a);
                    else NetScreen.ShowQuestion(text);
                    break;
                case Order.Music:
                    NetScreen.PlayMusic(a);
                    break;
                case Order.Dialog:
                {
                    string me = NetPlayer.Local != null ? NetPlayer.Local.DisplayName : "";
                    var list = new List<UI.DialogueView.Page>();
                    var rewards = new List<(int credits, string items, string heading)>();
                    foreach (var line in (text ?? "").Split('\n'))
                    {
                        var f = line.Split('\u001f');
                        if (f.Length == 4 && f[0] == "R") { int.TryParse(f[1], out int cr); rewards.Add((cr, f[2], f[3])); continue; }
                        if (f.Length < 4 || !int.TryParse(f[0], out int id) || list.Count >= 20) continue;
                        string name = f[1].Replace("%player%", me), body = f[3].Replace("%player%", me);
                        int[] face = null;
                        if (id == -1 && f[2].Length > 0)
                        {
                            var parts = f[2].Split(',');
                            face = new int[parts.Length];
                            for (int k = 0; k < parts.Length; k++) int.TryParse(parts[k], out face[k]);
                        }
                        // The reader: Keith's face (the player character, speaker 0) with their pilot name.
                        if (id == -2) id = 0;
                        if (id >= StoryTable.SpeakerCount) continue;
                        list.Add(new UI.DialogueView.Page { speaker = id, text = body, agentName = name.Length > 0 ? name : null, agentPortrait = face });
                    }
                    // The reward pages pay when the dialogue closes (at once without pages).
                    NetScreen.QueueDialog(list, rewards.Count == 0 ? null : (System.Action)(() => { foreach (var r in rewards) GrantReward(r.credits, r.items, r.heading); }));
                    break;
                }
                case Order.Reward:
                {
                    var parts = (text ?? "").Split('\n');
                    GrantReward(a, parts.Length > 1 ? parts[1] : "", parts[0]);
                    break;
                }
                case Order.Object:
                    if (level == null || string.IsNullOrEmpty(text)) return;
                    int sep = text.IndexOf('@');
                    Notice(by, DebugSpawner.SpawnObject(level, sep < 0 ? text : text.Substring(0, sep), sep < 0 ? null : ParseAt(text.Substring(sep + 1))));
                    break;
            }
        }
    }
}
