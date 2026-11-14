// NetCommandsRunning.cs
// NetCommands: this game, the server's one implementation for the chat and the console, completion (part of the partial NetCommands; its fields are in NetCommands.cs).

using System;
using System.Collections.Generic;
using System.Text;
using GoF2Remake.Data;
using Unity.Netcode;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    public static partial class NetCommands
    {
        // ---- this game ----------------------------------------------------------------------------------------
        /// <summary>A chat line starting with "/": runs it here or sends it to the server (true = it was a command, nothing
        /// is sent as chat).</summary>
        public static bool TryRun(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '/') return false;
            string body = line.Substring(1).Trim();
            int space = body.IndexOf(' ');
            string name = (space < 0 ? body : body.Substring(0, space)).ToLowerInvariant();
            string args = space < 0 ? "" : body.Substring(space + 1).Trim();
            var command = Array.Find(Commands, c => c.name == name && c.available());
            // A script mod's own command (game.addCommand): run in this game, its answer a notice.
            if (command == null && Modding.ModScripts.RunCommand(name, args, EventHost.LocalName, out string said))
            {
                if (!string.IsNullOrEmpty(said)) NetChat.Notice(said);
            }
            else if (command == null)
                NetChat.Notice(string.Format(X("mpCmdUnknown", "Unknown command /{0}. Type /help for the commands you can use."), name));
            else if (command.arg != Arg.None && args.Length == 0 && !command.optional)
                NetChat.Notice(Usage(command));
            else if (command.local != null)
                command.local(args);
            else if (Ready)
                NetState.Instance.ServerCommandRpc(command.name, args);
            return true;
        }
        static void Help()
        {
            NetChat.Notice(X("mpCmdList", "Commands:"));
            foreach (var c in Commands)
                if (c.available()) NetChat.Notice($"{Usage(c)}: {c.description()}");
            foreach (string name in Modding.ModScripts.CommandNames) NetChat.Notice("/" + name);   // the script mods' own (game.addCommand)
        }
        static void ToggleStats()
        {
            NetStats.Shown = !NetStats.Shown;
            NetChat.Notice(NetStats.Shown ? X("mpStatsOn", "Network stats on (/netstats hides them).") : X("mpStatsOff", "Network stats off."));
        }
        // ---- the server: one implementation for the chat and the dedicated server's console -------------------
        /// <summary>The server commands' names (the console's commands and Tab completion).</summary>
        public static IEnumerable<string> ServerCommandNames
        {
            get { foreach (var c in Commands) if (c.run != null) yield return c.name; }
        }
        /// <summary>The console's help for the server commands: "name usage: description" lines.</summary>
        public static string ServerCommandHelp()
        {
            var sb = new StringBuilder();
            foreach (var c in Commands)
                if (c.run != null && !c.needsPlayer) sb.Append($"\n  {c.name}{(c.usage.Length > 0 ? " " + c.usage : "")}: {c.description()}");
            return sb.ToString();
        }
        /// <summary>Server: runs command 'name' for 'issuer' (null = the dedicated server's console) after checking its
        /// rights; the answer for the issuer, null when 'name' is no server command.</summary>
        public static string RunOnServer(string name, string args, IPilot issuer)
        {
            var c = Get((name ?? "").ToLowerInvariant());
            // Not one of the game's: a script mod's own command (an event graph's Command node, game.command).
            if (c == null) return Modding.ModScripts.RunCommand(name, args, issuer != null ? issuer.DisplayName : "", out string said) ? said ?? "" : null;
            if (c.run == null) return null;
            if (!EventHost.ServerReady) return X("mpCmdNoServer", "The server isn't running.");
            if (issuer == null && c.needsPlayer) return string.Format(X("mpCmdPlayersOnly", "/{0} is for players in the chat."), c.name);
            if (issuer != null && !c.allowed(issuer)) return X("mpCmdNoRights", "You don't have the rights for that command.");
            args = (args ?? "").Trim();
            if (c.arg != Arg.None && args.Length == 0 && !c.optional) return Usage(c);
            return c.run(args, issuer) ?? "";
        }
        /// <summary>The issuer's name in notices and the log ("Server" for the console).</summary>
        public static string IssuerName(IPilot issuer) => issuer != null ? issuer.DisplayName : X("mpServerName", "Server");
        /// <summary>The player whose name 'args' starts with (whole name, any case, the longest one), the rest after it.</summary>
        internal static IPilot MatchPlayer(string args, out string rest)
        {
            IPilot best = null;
            rest = "";
            foreach (var p in EventHost.Pilots)
            {
                if (p == null || !p.IsSpawned) continue;
                string n = p.DisplayName;
                if (n.Length == 0 || !args.StartsWith(n, StringComparison.OrdinalIgnoreCase)) continue;
                if (args.Length > n.Length && args[n.Length] != ' ') continue;
                if (best == null || n.Length > best.DisplayName.Length) best = p;
            }
            if (best != null) rest = args.Substring(best.DisplayName.Length).Trim();
            return best;
        }
        // ---- completion (ChatView) --------------------------------------------------------------------------
        /// <summary>What Tab can write for the line typed so far: the commands its name starts with ("/" alone: all), or after a
        /// command that takes a player, the players whose name starts with what follows it. Empty = nothing to complete.</summary>
        public static List<Completion> Completions(string line)
        {
            var list = new List<Completion>();
            if (string.IsNullOrEmpty(line) || line[0] != '/') return list;
            int space = line.IndexOf(' ');
            if (space < 0)
            {
                string prefix = line.Substring(1).ToLowerInvariant();
                foreach (var c in Commands)
                    if (c.available() && c.name.StartsWith(prefix, StringComparison.Ordinal))
                        list.Add(new Completion { line = "/" + c.name + (c.arg != Arg.None ? " " : ""), label = Usage(c), description = c.description() });
                return list;
            }
            var command = Get(line.Substring(1, space - 1).ToLowerInvariant());
            if (command == null || !command.available() || (command.arg != Arg.Player && command.arg != Arg.PlayerText)) return list;
            string typed = line.Substring(space + 1);
            foreach (var (sel, what) in Selectors)
                if (sel.StartsWith(typed, StringComparison.OrdinalIgnoreCase) && typed.IndexOf(' ') < 0)
                    list.Add(new Completion { line = $"/{command.name} {sel}{(command.arg == Arg.PlayerText ? " " : "")}", label = sel, description = what() });
            foreach (var p in EventHost.Pilots)
            {
                if (p == null || !p.IsSpawned || (p.IsOwner && !command.self)) continue;
                string n = p.DisplayName;
                if (!n.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new Completion
                {
                    line = $"/{command.name} {n}{(command.arg == Arg.PlayerText ? " " : "")}",
                    label = n, description = WhereText(p),
                });
            }
            return list;
        }
    }
}
