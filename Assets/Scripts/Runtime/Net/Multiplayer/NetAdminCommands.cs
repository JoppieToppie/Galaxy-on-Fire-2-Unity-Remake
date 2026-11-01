// NetAdminCommands.cs
// NetAdmin: the commands (server) (part of the partial NetAdmin; its fields are in NetAdmin.cs).

using System;
using System.Globalization;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetAdmin
    {
        /// <summary>The players the arguments start with, else (a chat issuer, 'playersOptional') the issuer and the whole
        /// line; runs 'each' and joins the answers. No arguments at all: the issuer.</summary>
        static string ForTargets(string args, IPilot by, bool playersOptional, Func<IPilot, string, string> each)
        {
            args = (args ?? "").Trim();
            List<IPilot> targets;
            string rest;
            if (args.Length == 0)
            {
                if (by == null) return X("mpAdmNamePlayers", "Name the players (a name, a client id, @a or @r).");
                targets = new List<IPilot> { by };
                rest = "";
            }
            else
            {
                targets = NetCommands.FindTargets(args, by, out rest, out string error);
                if (targets.Count == 0)
                {
                    if (!playersOptional || by == null || args.StartsWith("@")) return error;
                    targets = new List<IPilot> { by };   // "/give 85 3": the issuer
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
        static void Send(IPilot to, Order order, int a, int b, int c, IPilot by, string log, string text = null)
        {
            EventHost.Send(to.OwnerClientId, order, a, b, c, text, NetCommands.IssuerName(by));
            Debug.Log($"Server: {NetCommands.IssuerName(by)} {log} ({to.DisplayName}, {to.OwnerClientId})");
        }
        static string NotInSpace(IPilot t) => string.Format(X("mpAdmNotInSpace", "{0} isn't in space."), t.DisplayName);
        public static string Kill(string args, IPilot by) => ForTargets(args, by, false, (t, _) =>
        {
            if (!t.InSpace) return NotInSpace(t);
            if (t.Hull <= 0f) return string.Format(X("mpAdmAlreadyDead", "{0} is already destroyed."), t.DisplayName);
            Send(t, Order.Kill, 0, 0, 0, by, "destroyed a ship");
            EventHost.NoticeAll(string.Format(X("mpAdmKilledAll", "{0} was destroyed by {1}."), t.DisplayName, NetCommands.IssuerName(by)));
            return "";
        });
        public static string Heal(string args, IPilot by) => ForTargets(args, by, false, (t, _) =>
        {
            Send(t, Order.Heal, 0, 0, 0, by, "repaired a ship");
            return string.Format(X("mpAdmHealed", "Repaired {0}'s ship."), t.DisplayName);
        });
        /// <summary>/ammo, /reveal, /peace: one order without arguments.</summary>
        public static string Simple(string args, IPilot by, Order order) => ForTargets(args, by, false, (t, _) =>
        {
            Send(t, order, 0, 0, 0, by, order.ToString().ToLowerInvariant());
            switch (order)
            {
                case Order.Ammo: return string.Format(X("mpAdmAmmo", "{0}'s secondaries refilled."), t.DisplayName);
                case Order.Reveal: return string.Format(X("mpAdmReveal", "Every system revealed for {0}."), t.DisplayName);
                default: return string.Format(X("mpAdmPeace", "{0} is at peace with every race."), t.DisplayName);
            }
        });
        /// <summary>/system [players] &lt;system&gt; &lt;show | hide | reset&gt;: the system on or off their star map (its routes and
        /// jumps with it) until reset.</summary>
        public static string SystemShow(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            int space = rest.LastIndexOf(' ');
            if (space <= 0) return Usage("system");
            string word = rest.Substring(space + 1).ToLowerInvariant();
            int action = word == "show" ? 1 : word == "hide" ? 0 : word == "reset" ? 2 : -1;
            if (action < 0 || !ParseSystem(rest.Substring(0, space).Trim(), out int system)) return Usage("system");
            Send(t, Order.System, system, action, 0, by, $"system {system} {word}");
            return "";
        });
        /// <summary>A system by its number, a mod's key ("mod_id:id") or its name (any case, the whole name).</summary>
        public static bool ParseSystem(string text, out int system)
        {
            system = -1;
            if (string.IsNullOrWhiteSpace(text)) return false;
            text = text.Trim();
            var db = NetGame.Db;
            if (text.IndexOf(':') > 0 && Modding.ModWorld.TryResolveSystem(text, out int mod) && db.Systems.Exists(s => s.index == mod)) { system = mod; return true; }
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                if (!db.Systems.Exists(s => s.index == n)) return false;
                system = n;
                return true;
            }
            var found = db.Systems.Find(s => !string.IsNullOrEmpty(s.name) && string.Equals(s.name.Trim(), text, StringComparison.OrdinalIgnoreCase));
            if (found == null) return false;
            system = found.index;
            return true;
        }
        public static string Give(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
        public static string Credits(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            if (!int.TryParse(rest.Trim(), out int amount) || amount == 0) return Usage("credits");
            amount = Mathf.Clamp(amount, -MaxCredits, MaxCredits);
            Send(t, Order.Credits, amount, 0, 0, by, $"gave {amount} credits");
            return string.Format(amount > 0 ? X("mpAdmCreditsGiven", "Gave {0} {1} credits.") : X("mpAdmCreditsTaken", "Took {1} credits from {0}."),
                t.DisplayName, Math.Abs(amount));
        });
        public static string Spawn(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            if (!t.InSpace) return NotInSpace(t);
            if (!TakeName(ref rest, out string name)) return Usage("spawn");
            if (!TakeAt(ref rest, out string at)) return Usage("spawn");
            if (!TakeTurrets(ref rest, out var turrets, out string turretError)) return turretError ?? Usage("spawn");
            string named = (turrets != null ? TurretSeparator + string.Join(",", turrets) : "") + (name != null ? NameSeparator + name : "");
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
                else if (RaceWord(w) >= 0) race = RaceWord(w);   // (a mod's race too: Standing.RaceWord)
                else break;
                words.RemoveAt(words.Count - 1);
            }
            string spec = string.Join(" ", words);
            if (string.Equals(spec, "wreck", StringComparison.OrdinalIgnoreCase))
            {
                // Remake: a hackable freighter wreck (its race's freighter: 0-3, any other = rolled).
                if (race > 3) return X("mpAdmWreckRace", "A wreck is a Terran, Vossk, Nivelian or Midorian freighter.");
                Send(t, Order.Wreck, race, 0, 0, by, $"spawned a hackable wreck{(at != null ? " at " + at : "")}{(name != null ? " named " + name : "")}",
                    (at ?? "") + (name != null ? NameSeparator + name : ""));
                return string.Format(X("mpAdmWreck", "Spawning a hackable wreck at {0}."), t.DisplayName);
            }
            int ship = FindShip(spec, out string error);
            if (ship < 0)
            {
                // Not a ship: an assembled object (the whole name, before any words were taken as a race or a count).
                var asm = FindAssembly(rest.Trim(), out string objectError);
                if (asm == null) return ship == -2 ? error : objectError ?? error;
                Send(t, Order.Object, 0, 0, 0, by, $"spawned object {asm.name}{(at != null ? " at " + at : "")}{(name != null ? " named " + name : "")}",
                    (at != null ? asm.name + "@" + at : asm.name) + named);
                return string.Format(X("mpAdmObject", "Spawning {0} at {1}."), asm.name, t.DisplayName);
            }
            if (race < 0)
            {
                race = Shop.ShipMakerRace(ship);
                if (race < 0 || (race > 3 && race != Standing.Pirate && race != Standing.Void && race != Standing.Specter && !Modding.ModRaces.Is(race)))
                    race = Standing.Pirate;
            }
            int tag = EventRunner.NewBatch(count, behaviour == DebugSpawner.Behaviour.Hostile);   // an event's spawn: counted (enemies / ships)
            Send(t, Order.Spawn, ship, race, tag << 12 | count << 8 | (int)behaviour, by,
                $"spawned {count} x ship {ship} (race {race}, {behaviour}{(at != null ? ", at " + at : "")}{(name != null ? ", named " + name : "")}{(tag != 0 ? ", event batch " + tag : "")})",
                (at ?? "") + named);
            return string.Format(X("mpAdmSpawned", "Spawning {0} x {1} at {2}."), count, DebugSpawner.ShipName(NetGame.Db, ship), t.DisplayName);
        });
        public static string Ship(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
        public static string Cheat(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
        public static string Title(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
        public static string Dialog(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
                    int at = SplitSpeaker(page, faces, out string spec);
                    if (at > 0) { current = spec; page = page.Substring(at + 1).Trim(); }
                    else if (current == null) return string.Format(X("mpAdmNoSpeaker", "No speaker \"{0}\": a story character's name (Keith as Bob renames), a mod's character, a race and a name (vossk K'ekki), or player."), page.Substring(0, colon).Trim());
                }
                if (current == null) return Usage("dialog");
                page = NetChat.Clean(page, NetChat.MaxPageLength).Replace("\u001f", " ");
                if (page.Length > 0 && lines.Count < 20 && System.Linq.Enumerable.Sum(lines, l => l.Length + 1) + current.Length + page.Length < NetChat.MaxConversation)
                    lines.Add(current + "\u001f" + page);
            }
            if (lines.Count == 0) return Usage("dialog");
            Send(t, Order.Dialog, 0, 0, 0, by, $"dialog, {lines.Count} page(s)", string.Join("\n", lines));
            return "";
        });
        public static string Reward(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
            EventScreen.ShowReward(heading, credits, list);
        }
        /// <summary>"&lt;speaker&gt; : &lt;text&gt;": the speaker's spec and the colon it ends at, trying each colon in turn so a mod
        /// character's key ("frontier_systems:vega : Hello") splits right; -1 = no speaker before any colon.</summary>
        internal static int SplitSpeaker(string page, Dictionary<string, string> faces, out string spec)
        {
            spec = null;
            for (int colon = page.IndexOf(':'); colon > 0; colon = page.IndexOf(':', colon + 1))
            {
                spec = ResolveSpeaker(page.Substring(0, colon).Trim(), faces);
                if (spec != null) return colon;
            }
            return -1;
        }
        /// <summary>A page's speaker as "id\u001fname\u001fface" (NetAdmin.Apply, Events.SpeakerSpec), null = not a speaker:
        /// "player", a story speaker ("Keith", "Keith as Bob"), a mod's character ("mod_id:vega", its id or its name; Modding.
        /// ModCharacters), or "&lt;race&gt; [female | male] [name]" with a face made once per dialog.</summary>
        internal static string ResolveSpeaker(string who, Dictionary<string, string> faces)
        {
            if (who.Length == 0) return null;
            if (who.Equals("player", StringComparison.OrdinalIgnoreCase) || who.Equals("you", StringComparison.OrdinalIgnoreCase))
                return "-2\u001f%player%\u001f";
            string rename = "";
            int asAt = who.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            string baseName = asAt > 0 ? who.Substring(0, asAt).Trim() : who;
            if (asAt > 0) rename = NetChat.Clean(who.Substring(asAt + 4).Trim()).Replace("\u001f", " ");
            // A mod character by its key first ("mod_id:vega"), then a story speaker, then a character by id / name.
            var character = baseName.Contains(":") ? Modding.ModCharacters.Find(baseName) : null;
            if (character != null) return SpeakerSpec.OfCharacter(character, rename, '\u001f');
            int speaker = FindSpeaker(baseName);
            if (speaker >= 0) return speaker + "\u001f" + rename + "\u001f";
            character = Modding.ModCharacters.Find(baseName);
            if (character != null) return SpeakerSpec.OfCharacter(character, rename, '\u001f');
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
                // The game's name and a mod's replacing one (characters.json "replaces") both find the speaker.
                string own = StoryTable.OriginalSpeakerName(i), shown = StoryTable.SpeakerName(i);
                bool partial = false;
                foreach (var n in own == shown ? new[] { own } : new[] { own, shown })
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return i;
                    if (n.StartsWith(name, StringComparison.OrdinalIgnoreCase) || n.Split(' ')[0].Equals(name, StringComparison.OrdinalIgnoreCase)) partial = true;
                }
                if (partial) { found = i; matches++; }
            }
            return matches == 1 ? found : -1;
        }
        public static string Timer(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
        public static string Pvp(string args, IPilot by)
        {
            string a = (args ?? "").Trim().ToLowerInvariant();
            if (a != "on" && a != "off") return Usage("pvp");
            EventHost.SetFreeForAll(a == "on");
            EventRunner.NoteFreeForAll(a == "on");
            EventHost.NoticeAll(a == "on" ? X("mpPvpOn", "Free for all: every pilot is your enemy.") : X("mpPvpOff", "The free for all is over."));
            Debug.Log($"Server: {NetCommands.IssuerName(by)} turned free for all {a}");
            return "";
        }
        /// <summary>/respawn [players] &lt;station [x y z] [spread &lt;units&gt;] [delay &lt;s&gt;] | off&gt;: where their ship comes back
        /// after being destroyed: in that orbit (at x y z, else 3 km in front of its station, spread around it), not docked.</summary>
        public static string Respawn(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
            EventRunner.NoteRespawnSet();
            return "";
        });
        /// <summary>/restrict [players] &lt;jumps | docking | off&gt; ...: no jumps (planet, gate, Khador) and / or no docking for them.</summary>
        public static string Restrict(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            int flags = 0;
            foreach (string w in rest.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (w == "jumps" || w == "jump") flags |= EventRules.Jumps;
                else if (w == "docking" || w == "dock") flags |= EventRules.Docking;
                else if (w == "all") flags |= EventRules.Jumps | EventRules.Docking;
                else if (w != "off") return Usage("restrict");
            }
            Send(t, Order.Rules, flags, 0, 0, by, $"travel restricted ({flags})");
            if (flags != 0) EventRunner.NoteRulesSet();
            return "";
        });
        /// <summary>/flightstyle [players] &lt;free | original | player&gt;: the flight style their games fly with (Events.EventFlight,
        /// before the ship's, the mod campaign's and their own option: Modding.ModFlight); "player" gives it back.</summary>
        public static string FlightStyle(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            if (!Modding.ModFlight.TryParseStyle(rest, out int style) || string.IsNullOrWhiteSpace(rest)) return Usage("flightstyle");
            Send(t, Order.Flight, style, 0, 0, by, $"flight style {(style < 0 ? "player" : style == FlightStyles.Free ? "free" : "original")}");
            if (style >= 0) EventRunner.NoteFlightSet();
            return "";
        });
        /// <summary>/provoke [players] [race] [within &lt;m&gt;]: the NPC ships around them (that race; within that many metres,
        /// else the whole orbit) turn on them and their squad, like ships they shot (NpcShip.aggressors); sent to each ship's
        /// own game (the orbit's authority, or whoever spawned it) by its NetProxy.</summary>
        public static string Provoke(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
            var byOwner = new Dictionary<ulong, List<ulong>>();
            foreach (var p in UnityEngine.Object.FindObjectsByType<NetProxy>())
            {
                if (p == null || !p.IsSpawned || !p.FlyingNow || p.IsWingman || p.Station != t.Station) continue;
                if (race >= 0 && p.Race != race) continue;
                if (within > 0f && (p.WorldPosition - t.Position).magnitude > within) continue;
                if (!byOwner.TryGetValue(p.OwnerClientId, out var ids)) byOwner[p.OwnerClientId] = ids = new List<ulong>();
                ids.Add(p.NetworkObjectId);   // (its proxy: the ship's own game may have taken it over from another one)
            }
            int count = 0;
            foreach (var kv in byOwner)
            {
                string payload = t.OwnerClientId.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", kv.Value);
                EventHost.Send(kv.Key, Order.Provoke, 0, 0, 0, payload, NetCommands.IssuerName(by));
                count += kv.Value.Count;
            }
            Debug.Log($"Server: {NetCommands.IssuerName(by)} turned {count} ship(s) on {t.DisplayName}");
            return string.Format(X("mpAdmProvoked", "{0} ship(s) turned on {1}."), count, t.DisplayName);
        });
        /// <summary>/radio [players] &lt;speaker&gt; : &lt;text&gt;: a line in their flight HUD's radio box (a face and a name, gone by
        /// itself; docked: a chat line). The speakers as /dialog's (ResolveSpeaker).</summary>
        public static string Radio(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            int first = rest.IndexOf(':');
            if (first <= 0) return Usage("radio");
            int colon = SplitSpeaker(rest, new Dictionary<string, string>(), out string spec);
            if (colon < 0) return string.Format(X("mpAdmNoSpeaker", "No speaker \"{0}\": a story character's name (Keith as Bob renames), a mod's character, a race and a name (vossk K'ekki), or player."), rest.Substring(0, first).Trim());
            string line = NetChat.Clean(rest.Substring(colon + 1), NetChat.MaxPageLength).Replace("\u001f", " ");
            if (line.Length == 0) return Usage("radio");
            Send(t, Order.Radio, 0, 0, 0, by, "radio", spec + "\u001f" + line);
            return "";
        });
        /// <summary>/waypoint [players] &lt;station x y z | off&gt;: a lockable "Waypoint" at those game coordinates in that orbit
        /// (EventWaypoint), the autopilot flies there; reaching it clears it.</summary>
        public static string Waypoint(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
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
            EventRunner.NoteWaypointSet();
            return "";
        });
        /// <summary>/wormhole [players] &lt;station&gt; [x y z] to &lt;destination&gt; [for &lt;seconds&gt;] | close: a wormhole in that
        /// orbit (EventWormhole); flying in takes them to the destination (a station, or "void"). Open until closed, the seconds
        /// or the event's end.</summary>
        public static string Wormhole(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (string.Equals(rest, "close", StringComparison.OrdinalIgnoreCase) || string.Equals(rest, "off", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Wormhole, 0, 0, 0, by, "wormhole closed", "close");
                return "";
            }
            if (!TryParseWormhole(rest, out string payload)) return Usage("wormhole");
            Send(t, Order.Wormhole, 0, 0, 0, by, "wormhole " + payload, payload);
            EventRunner.NoteWormholeSet();
            return "";
        });
        /// <summary>"&lt;station&gt; [x y z] to &lt;destination&gt; [for &lt;seconds&gt;]" to EventWormhole's order text.</summary>
        internal static bool TryParseWormhole(string rest, out string payload)
        {
            payload = null;
            if (!ParseOrbit(rest, out int station, out string after)) return false;
            var w = new List<string>(after.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            var at = WormholeDefaultAt;
            if (w.Count >= 3 && float.TryParse(w[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                && float.TryParse(w[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                if (Mathf.Abs(x) > 1e7f || Mathf.Abs(y) > 1e7f || Mathf.Abs(z) > 1e7f) return false;
                at = new Vector3(x, y, z);
                w.RemoveRange(0, 3);
            }
            if (w.Count < 2 || !w[0].Equals("to", StringComparison.OrdinalIgnoreCase)) return false;
            w.RemoveAt(0);
            float seconds = 0f;
            int f = w.FindLastIndex(s => s.Equals("for", StringComparison.OrdinalIgnoreCase));
            if (f >= 0)
            {
                if (f != w.Count - 2 || !float.TryParse(w[f + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds < 0f || seconds > 86400f) return false;
                w.RemoveRange(f, 2);
            }
            if (!ParseOrbit(string.Join(" ", w), out int to, out string left) || left.Length > 0) return false;
            payload = string.Join(" ", station.ToString(CultureInfo.InvariantCulture), at.x.ToString(CultureInfo.InvariantCulture),
                at.y.ToString(CultureInfo.InvariantCulture), at.z.ToString(CultureInfo.InvariantCulture), to.ToString(CultureInfo.InvariantCulture),
                seconds.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        /// <summary>A station as NetTeleport.ParseStation reads it, also -1 for the Void's home orbit (a variable's number).</summary>
        static bool ParseOrbit(string text, out int station, out string rest)
        {
            text = (text ?? "").Trim();
            if (text == "-1" || text.StartsWith("-1 ")) { station = Session.VoidOrbit; rest = text.Substring(2).Trim(); return true; }
            return NetTeleport.ParseStation(text, out station, out rest);
        }
        /// <summary>/cutscene [players] &lt;start [nobars] [freeze] [invulnerable] | end&gt;: cinematic mode in flight
        /// (EventCutscene): no HUD, no controls, the letterbox bars.</summary>
        public static string Cutscene(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            var w = rest.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 0 || (w[0] != "start" && w[0] != "end")) return Usage("cutscene");
            for (int i = 1; i < w.Length; i++)
                if (w[0] == "end" || (w[i] != "nobars" && w[i] != "freeze" && w[i] != "invulnerable")) return Usage("cutscene");
            Send(t, Order.Cutscene, 0, 0, 0, by, "cutscene " + string.Join(" ", w), string.Join(" ", w));
            EventRunner.NoteCutsceneSet();
            return "";
        });
        /// <summary>/camera [players] &lt;shot | chase&gt;: a camera shot on their game (EventCutscene.TryParseShot).</summary>
        public static string Camera(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (!rest.Equals("chase", StringComparison.OrdinalIgnoreCase) && !EventCutscene.TryParseShot(rest, out _, out string error))
                return Usage("camera") + (error != null ? " (" + error + ")" : "");
            Send(t, Order.Camera, 0, 0, 0, by, "camera " + rest, rest);
            EventRunner.NoteCutsceneSet();
            return "";
        });
        /// <summary>/fade [players] &lt;out | in&gt; [seconds] [rrggbb] | clear: the screen to / from a colour.</summary>
        public static string Fade(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            var w = rest.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (w.Length == 0 || w.Length > 3) return Usage("fade");
            string k = w[0].ToLowerInvariant();
            if (k != "out" && k != "in" && k != "clear") return Usage("fade");
            if (w.Length > 1 && (!float.TryParse(w[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s) || s < 0f || s > 600f)) return Usage("fade");
            if (w.Length > 2 && !ColorUtility.TryParseHtmlString(w[2].StartsWith("#") ? w[2] : "#" + w[2], out _)) return Usage("fade");
            Send(t, Order.Fade, 0, 0, 0, by, "fade " + string.Join(" ", w), string.Join(" ", w));
            EventRunner.NoteFadeSet();
            return "";
        });
        /// <summary>/npc [players] &lt;ship&gt; &lt;order&gt;: an order for the ships of that name in their games (EventShipOrders).</summary>
        public static string Npc(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (!EventShipOrders.TryParse(rest, out var parsed, out string error)) return Usage("npc") + (error != null ? " (" + error + ")" : "");
            if (!t.InSpace) return "";   // docked: none of their ships around
            Send(t, Order.Npc, 0, 0, 0, by, "npc " + rest, rest);
            return "";
        });
        /// <summary>/letterbox [players] &lt;on | off&gt;: the cinema bars at the top and bottom of the screen.</summary>
        public static string Letterbox(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim().ToLowerInvariant();
            if (rest != "on" && rest != "off") return Usage("letterbox");
            Send(t, Order.Letterbox, 0, 0, 0, by, "letterbox " + rest, rest);
            if (rest == "on") EventRunner.NoteCutsceneSet();
            return "";
        });
        /// <summary>/sound [players] &lt;sound&gt;: an event sound (EventAudio), or a mod's by name (Modding.ModSounds), on their
        /// game.</summary>
        public static string Sound(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (!EventAudio.TryParse(rest, out EventSound sound))
            {
                // A mod's sound by name: each game plays its own copy (a session's mods are the same).
                if (Modding.ModSounds.Has(rest)) { Send(t, Order.Sound, -2, 0, 0, by, $"sound {rest}", rest); return ""; }
                return string.Format(X("mpAdmNoSound", "Sounds: {0}."), EventAudio.Names<EventSound>()
                    + (System.Linq.Enumerable.Any(Modding.ModSounds.Names) ? ", " + string.Join(", ", Modding.ModSounds.Names) : ""));
            }
            Send(t, Order.Sound, (int)sound, 0, 0, by, $"sound {sound}");
            return "";
        });
        /// <summary>/music [players] &lt;track | stop&gt;: an event track looped instead of their scene's music.</summary>
        public static string Music(string args, IPilot by) => ForTargets(args, by, true, (t, rest) =>
        {
            rest = rest.Trim();
            if (string.Equals(rest, "stop", StringComparison.OrdinalIgnoreCase))
            {
                Send(t, Order.Music, -1, 0, 0, by, "music stop");
                return "";
            }
            if (!EventAudio.TryParse(rest, out EventMusic track))
            {
                // A mod's track by name (Modding.ModMusic): each game plays its own copy (a session's mods are the same).
                if (Modding.ModMusic.Has(rest)) { Send(t, Order.Music, -2, 0, 0, by, $"music {rest}", rest); return ""; }
                return string.Format(X("mpAdmNoMusic", "Music: {0}, or stop."), EventAudio.Names<EventMusic>()
                    + (System.Linq.Enumerable.Any(Modding.ModMusic.Names) ? ", " + string.Join(", ", Modding.ModMusic.Names) : ""));
            }
            Send(t, Order.Music, (int)track, 0, 0, by, $"music {track}");
            return "";
        });
        public static string Mute(string args, IPilot by, bool on) => ForTargets(args, by, false, (t, rest) =>
        {
            if (!on)
            {
                if (!muted.Remove(t.OwnerClientId)) return string.Format(X("mpAdmNotMuted", "{0} isn't muted."), t.DisplayName);
                EventHost.NoticeTo(t, X("mpUnmutedYou", "You can chat again."));
                Debug.Log($"Server: {NetCommands.IssuerName(by)} unmuted {t.DisplayName} ({t.OwnerClientId})");
                return string.Format(X("mpAdmUnmuted", "{0} can chat again."), t.DisplayName);
            }
            if (t == by) return X("mpCmdNotYourself", "Not on yourself.");
            if (NetCommands.IsHostPlayer(t) || (NetCommands.IsAdmin(t) && by != null && !NetCommands.IsHostPlayer(by)))
                return string.Format(X("mpAdmCantMute", "{0} can't be muted."), t.DisplayName);
            float minutes = float.TryParse(rest.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m) && m > 0f ? m : 0f;
            muted[t.OwnerClientId] = minutes > 0f ? Time.unscaledTime + minutes * 60f : float.PositiveInfinity;
            IsMuted(t.OwnerClientId, out string text);
            EventHost.NoticeTo(t, text);
            Debug.Log($"Server: {NetCommands.IssuerName(by)} muted {t.DisplayName} ({t.OwnerClientId}){(minutes > 0f ? $" for {minutes} min" : "")}");
            return minutes > 0f ? string.Format(X("mpAdmMutedFor", "{0} is muted for {1} minute(s)."), t.DisplayName, minutes)
                                : string.Format(X("mpAdmMuted", "{0} is muted for this session."), t.DisplayName);
        });
    }
}
