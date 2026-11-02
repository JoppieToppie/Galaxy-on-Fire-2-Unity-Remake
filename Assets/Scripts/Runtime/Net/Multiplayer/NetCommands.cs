// NetCommands.cs
// Chat commands: a line typed in the chat that starts with "/" is a command, never sent as chat (NetChat.Send). Every
// command that acts on the session runs on the server (ServerCommands): the chat sends its name and arguments
// (NetState.ServerCommandRpc), the server checks the sender's rights and runs it, and its answer comes back as a notice
// only the sender sees. The dedicated server's console runs the very same commands (RunOnServer with no issuer: every
// right, named "Server"). Only the commands that just change or read this game stay here (help, netstats, pos).
// Each command says who may use it (`available` here for /help and the suggestions, `allowed` on the server, which
// decides). Typing "/" lists the matching commands over the chat line and Tab completes / cycles them, and a player name
// after the commands that take one (ChatView, Completions).
// This game:
//   /help                      the commands this player can use
//   /netstats                  shows / hides the network stats over the HUD (NetStats)
//   /version                   this game's build (BuildVersion.Text)
//   /pos                       your orbit and game coordinates (what /tp takes)
// The server (the console without the "/"):
//   /players                   everyone in the session: where they are, their ship, squad, admin (admins and the console:
//                              their client ids too)
//   /g <text>, /l <text>       one line to Global / Local without switching the channel (the console: g = its say)
//   /w <player> <text>         a private message to that player only
//   /invite <player>           a squad invitation (docked at the same station, like the pilot list's Invite)
//   /leave                     leaves the arena queue or match (NetArena), else the squad
//   /kick <player> [reason]    admins: drops a player (never the host's own player; only the host removes an admin); on a
//                              server with profiles NetModeration's kick (ops too, a rejoin cooldown in minutes)
//   /tp [player] <player | station [x y z | dock]>   admins: teleports (NetTeleport) a player (the chat: yourself by
//                              default) to a player, an orbit (at the launch spot or game coordinates) or a station's hangar
//   /tphere <player>           admins: brings a player to you
//   /kill, /heal, /give, /credits, /spawn, /mute, /unmute, /ship, /ammo, /reveal, /peace, /cheat, /title, /timer
//                              admins: NetAdmin (destroy, repair, items, credits, NPC ships and objects, chat muting, the
//                              debug panel's tools for one player: fly any hull, refill, reveal the map, make peace, a
//                              cheat toggle for the session; a title and a countdown on screen, EventScreen)
//   /dialog [players] <speaker> : <text> [| page ...]   admins: the dialogue window with a story speaker's or a race's face
//   /reward [players] <credits | item [amount]> [+ ...] [| title]   admins: a payout in the mission reward box
//   /event <name | stop | list>  admins: runs an event graph (EventRunner: game modes such as waves of enemies)
//   /news <text | clear>       admins: a GalNet item on every station's news ticker (NetNews)
//   /admin, /unadmin <player>  the host: makes a player an admin for the session / takes it back (with profiles: the
//                              master admin, NetModeration, kept on the profile)
// A dedicated server with player profiles adds (their own handlers answer them): moderation by the profiles' roles
// (NetModeration: /tempban /ban /unban /bans /op /deop /staff /say /disband /deleteprofile /settings /set /claimadmin;
// /web a login code for the web admin, WebAdmin),
// the profiles (NetProfiles: /link /control /profile) and factions (NetFactions: /faction, /f); everywhere the arena matches
// (NetArena: /duel /accept /decline /ffa /arena /top) and, this game's own, the squad's distress calls (NetDistress:
// /sos, /assist <player>).
// Admins: the host's own player always, else the players the host or the server console made admins (IPilot.IsAdmin,
// for the session only: names aren't verified, so nothing is remembered by name), and on a server with profiles the
// profiles' admins and masters (NetPlayer.StaffRole, NetModeration).
// Players are named whole and case-insensitively, the longest name the arguments start with ("Player 2 hi"), by a client
// id as the first word, or by a selector like Minecraft's (FindTargets): @a everyone, @s yourself, @p the nearest other
// player, @r a random other player, and by state @alive (in space, not destroyed), @space, @docked, @dead (destroyed in
// space), @survivors (an event's players never destroyed since its fight began, EventRunner.Survived), @team (the event's
// players: a bar mission's team; while a mission's event runs a line, every selector only finds its team: Scope), any of them narrowed
// to one orbit by [orbit=<station>] (in its space or docked at its station: "@alive[orbit=78]", "@r[orbit=Var Hastra]");
// a command on several players runs for each ("/tp @a 78 dock", "/kick @r", "/reward @survivors 1000").

using System;
using System.Collections.Generic;
using System.Text;
using GoF2Remake.Data;
using Unity.Netcode;
using UnityEngine;
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class NetCommands
    {
        enum Arg { None, Text, Player, PlayerText }

        /// <summary>One Tab completion: the whole line it writes, and the suggestion row's name and description.</summary>
        public struct Completion
        {
            public string line, label, description;
        }

        sealed class Command
        {
            public string name, usage = "";
            public Arg arg;
            public bool self;                              // the player argument may be the issuer themselves (completion)
            public Func<string> description;
            public Func<bool> available;                   // this game: listed by /help and the suggestions
            public Action<string> local;                   // runs on this game; null = a server command
            public Func<IPilot, bool> allowed;          // the server: the issuer may run it (null issuer = the console)
            public Func<string, IPilot, string> run;    // the server: args, issuer -> the answer
            public bool needsPlayer;                       // only a player can run it (not the console)
            public bool optional;                          // the argument may be left out (the chat: yourself)
        }

        static bool Everyone() => true;

        static readonly (string, Func<string>)[] Selectors =
        {
            ("@a", () => X("mpSelAll", "everyone")),
            ("@s", () => X("mpSelSelf", "yourself")),
            ("@p", () => X("mpSelNearest", "the nearest player")),
            ("@r", () => X("mpSelRandom", "a random player")),
            ("@alive", () => X("mpSelAlive", "everyone alive in space")),
            ("@survivors", () => X("mpSelSurvivors", "the event's players never destroyed")),
            ("@team", () => X("mpSelTeam", "the event's players (a bar mission's team)")),
            ("@space", () => X("mpSelSpace", "everyone in space")),
            ("@docked", () => X("mpSelDocked", "everyone docked")),
            ("@dead", () => X("mpSelDead", "everyone destroyed in space")),
        };
        static bool Anyone(IPilot p) => true;

        /// <summary>Every command's name, usage and description, for the modding reference (GoF2 > Build > Modding AI Reference).</summary>
        public static IEnumerable<(string name, string usage, string description)> Reference()
        {
            foreach (var c in Commands) yield return (c.name, c.usage ?? "", c.description?.Invoke() ?? "");
        }

        /// <summary>The player selectors and what they pick, for the modding reference.</summary>
        public static IEnumerable<(string selector, string description)> SelectorReference()
        {
            foreach (var (s, d) in Selectors) yield return (s, d());
        }

        static readonly Command[] Commands =
        {
            new Command { name = "help", available = Everyone, local = _ => Help(),
                description = () => X("mpCmdHelp", "lists the commands you can use") },
            new Command { name = "netstats", available = Everyone, local = _ => ToggleStats(),
                description = () => X("mpCmdNetstats", "shows or hides the network stats (ping, packet loss, data in / out)") },
            new Command { name = "version", available = Everyone,
                local = _ => NetChat.Notice(string.Format(X("mpVersion", "This game: build {0}"), UI.BuildVersion.Text)),
                description = () => X("mpCmdVersion", "this game's build (version and the code it was built from)") },
            new Command { name = "pos", available = Everyone, local = _ => NetChat.Notice(NetTeleport.Position()),
                description = () => X("mpCmdPos", "your orbit and coordinates (what /tp takes)") },

            new Command { name = "players", available = Everyone, allowed = Anyone, run = (_, by) => Players(by),
                description = () => X("mpCmdPlayers", "everyone in the session: where they are, ship, squad") },
            new Command { name = "g", usage = "<text>", arg = Arg.Text, available = Everyone, allowed = Anyone, run = (a, by) => Say(a, by, true),
                description = () => X("mpCmdGlobal", "sends one line to Global") },
            new Command { name = "l", usage = "<text>", arg = Arg.Text, available = Everyone, allowed = Anyone, run = (a, by) => Say(a, by, false),
                needsPlayer = true, description = () => X("mpCmdLocal", "sends one line to Local") },
            new Command { name = "w", usage = "<player> <text>", arg = Arg.PlayerText, available = Everyone, allowed = Anyone, run = Whisper,
                description = () => X("mpCmdWhisper", "a private message to one player") },
            new Command { name = "s", usage = "<text>", arg = Arg.Text, available = Everyone, allowed = Anyone, run = SquadSay,
                needsPlayer = true, description = () => X("mpCmdSquadChat", "one line to your squad, wherever they are") },
            new Command { name = "invite", usage = "<player>", arg = Arg.Player, available = Everyone, allowed = Anyone, run = Invite,
                needsPlayer = true, description = () => X("mpCmdInvite", "invites a player docked here to your squad") },
            new Command { name = "leave", available = Everyone, allowed = Anyone,
                run = (_, by) => NetArena.InMatch(by.OwnerClientId) ? NetArena.Command(by.OwnerClientId, "/leave") ?? "" : Leave(by),
                needsPlayer = true, description = () => X("mpCmdLeave", "leaves your arena queue or match, else your squad") },
            new Command { name = "kick", usage = "<player> [minutes] [reason]", arg = Arg.PlayerText, available = () => LocalIsOp, allowed = IsOp,
                run = (a, by) => NetProfiles.Enabled ? Moderate("kick", a, by) : Kick(a, by),
                description = () => X("mpCmdKick", "admins: removes a player (with profiles: can't rejoin for the minutes, default 5)") },
            new Command { name = "tp", usage = NetTeleport.Usage, arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = (a, by) => NetTeleport.Command(a, by, false),
                description = () => X("mpCmdTp", "admins: teleports a player to a player, an orbit (x y z) or a hangar (dock)") },
            new Command { name = "tphere", usage = "<player>", arg = Arg.Player, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = (a, by) => NetTeleport.Command(a, by, true), needsPlayer = true,
                description = () => X("mpCmdTpHere", "admins: brings a player to you") },
            new Command { name = "kill", usage = "[players]", arg = Arg.Player, optional = true, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = NetAdmin.Kill, description = () => X("mpCmdKill", "admins: destroys ships in space (yourself by default)") },
            new Command { name = "heal", usage = "[players]", arg = Arg.Player, optional = true, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = NetAdmin.Heal, description = () => X("mpCmdHeal", "admins: repairs hull, shield and armor (yourself by default)") },
            new Command { name = "give", usage = "[players] <item> [amount] [mount]", arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = NetAdmin.Give, description = () => X("mpCmdGive", "admins: items into the hold (an item's number or name)") },
            new Command { name = "take", usage = "[players] <item> [amount | all] [mounted]", arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = NetAdmin.Take, description = () => X("mpCmdTake", "admins: items out of the hold (all by default; mounted: the mounted ones too)") },
            new Command { name = "credits", usage = "[players] <amount>", arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = NetAdmin.Credits, description = () => X("mpCmdCredits", "admins: gives credits (negative: takes them)") },
            new Command { name = "standing", usage = "[players] <race> <amount> [set]", arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin,
                allowed = IsAdmin, run = NetAdmin.StandingCommand,
                description = () => X("mpCmdStanding", "admins: moves the standing toward a race, a mod's too (set: to the amount)") },
            new Command { name = "spawn", usage = "[players] <ship | object | wreck | type <npc type>> [race] [count] [enemy | friendly | neutral | standing] [turrets <item,item>] [hull <x>] [guns <x>] [named <name>] [at x y z]", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Spawn,
                description = () => X("mpCmdSpawn", "admins: NPC ships or an object, ahead or at x y z (enemy by default; named: a HUD name; hull / guns: scaled; type: an NPC ship type)") },
            new Command { name = "ship", usage = "[players] <ship | own>", arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = NetAdmin.Ship, description = () => X("mpCmdShip", "admins: flies any ship of the debug Ships tab (its number or name; own = back)") },
            new Command { name = "ammo", usage = "[players]", arg = Arg.Player, optional = true, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = (a, by) => NetAdmin.Simple(a, by, NetAdmin.Order.Ammo), description = () => X("mpCmdAmmo", "admins: every mounted secondary to 50") },
            new Command { name = "reveal", usage = "[players]", arg = Arg.Player, optional = true, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = (a, by) => NetAdmin.Simple(a, by, NetAdmin.Order.Reveal), description = () => X("mpCmdReveal", "admins: every system on the star map") },
            new Command { name = "system", usage = "[players] <system> <show | hide | reset>", arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = NetAdmin.SystemShow, description = () => X("mpCmdSystem", "admins: shows or hides a system on their star map until reset") },
            new Command { name = "peace", usage = "[players]", arg = Arg.Player, optional = true, self = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = (a, by) => NetAdmin.Simple(a, by, NetAdmin.Order.Peace), description = () => X("mpCmdPeace", "admins: neutral standings, no station grudges") },
            new Command { name = "cheat", usage = "[players] <god | ammo | cooldown | primary | boost | onehit | locks | shopping | jumps> [on | off]", arg = Arg.PlayerText,
                self = true, available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Cheat,
                description = () => X("mpCmdCheat", "admins: a debug toggle for a player, this session only (toggled without on / off)") },
            new Command { name = "title", usage = "[players] <text> [| subtitle] [for <seconds>]", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Title,
                description = () => X("mpCmdTitle", "admins: a big title on screen (\"clear\" removes it)") },
            new Command { name = "timer", usage = "[players] <seconds | m:ss> [label]", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Timer,
                description = () => X("mpCmdTimer", "admins: a countdown on screen (\"stop\" removes it)") },
            new Command { name = "dialog", usage = "[players] <speaker> : <text> [| [speaker :] next page ...]", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Dialog,
                description = () => X("mpCmdDialog", "admins: a conversation; speaker: a story character, mod_id:id, a race and name, or player") },
            new Command { name = "reward", usage = "[players] <credits | item [amount]> [+ more ...] [| title]", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Reward,
                description = () => X("mpCmdReward", "admins: a mission payout (credits and / or items) in the reward box") },
            new Command { name = "pvp", usage = "<on | off>", arg = Arg.Text, available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Pvp,
                description = () => X("mpCmdPvp", "admins: free for all, every pilot an enemy of every other (squadmates excepted)") },
            new Command { name = "respawn", usage = "[players] <station [x y z] [spread <units>] [delay <seconds>] | off>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Respawn,
                description = () => X("mpCmdRespawn", "admins: where destroyed ships come back in space (not docked), until off") },
            new Command { name = "restrict", usage = "[players] <jumps | docking | all | khador | off>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Restrict,
                description = () => X("mpCmdRestrict", "admins: no jumps (planet, gate, Khador) and / or no docking (khador: only the Khador Drive), until off") },
            new Command { name = "flightstyle", usage = "[players] <es2 | gof2 | player>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.FlightStyle,
                description = () => X("mpCmdFlightStyle", "admins: some players' flight model (es2 / gof2) for now; player gives it back") },
            new Command { name = "provoke", usage = "[players] [race] [within <metres>]", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Provoke,
                description = () => X("mpCmdProvoke", "admins: the NPC ships around them turn on them and their squad (a race, a distance)") },
            new Command { name = "radio", usage = "[players] <speaker> : <text>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Radio,
                description = () => X("mpCmdRadio", "admins: a radio call in flight (a face, a name, gone by itself); speakers as /dialog") },
            new Command { name = "waypoint", usage = "[players] <station x y z | off>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Waypoint,
                description = () => X("mpCmdWaypoint", "admins: a waypoint to fly to (game coordinates in a station's orbit; /pos shows yours)") },
            new Command { name = "wormhole", usage = "[players] <station> [x y z] to <station | void> [for <seconds>] | close", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Wormhole,
                description = () => X("mpCmdWormhole", "admins: a wormhole in a station's orbit; flying in takes them to another station or the Void") },
            new Command { name = "cutscene", usage = "[players] <start [nobars] [freeze] [invulnerable] | end>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Cutscene,
                description = () => X("mpCmdCutscene", "admins: cinematic mode in flight (no HUD or controls, letterbox) until end") },
            new Command { name = "camera", usage = "[players] <place [to place] [over s] [look place] [follow] [fov deg] [shake 0-1] | chase>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Camera,
                description = () => X("mpCmdCamera", "admins: a camera shot; places as /npc (player, station, x y z, ship <name>); chase ends it") },
            new Command { name = "npc", usage = "[players] <ship> <goto place | route place / place ... [loop] | follow | escort | attack <player | ship \"name\"> | flee | dock | hold | resume | jump | speed n> [speed n] [radius r] [fight] [then hold | resume | vanish | jump]",
                arg = Arg.PlayerText, self = true, available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Npc,
                description = () => X("mpCmdNpc", "admins: orders for spawned ships by name: fly somewhere, follow / escort, attack, flee, dock, hold, resume") },
            new Command { name = "fade", usage = "[players] <out | in> [seconds] [rrggbb colour] | clear", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Fade,
                description = () => X("mpCmdFade", "admins: fades the screen to a colour (out, held) or back (in)") },
            new Command { name = "letterbox", usage = "[players] <on | off>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Letterbox,
                description = () => X("mpCmdLetterbox", "admins: the cinema bars at the top and bottom of the screen") },
            new Command { name = "sound", usage = "[players] <sound>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Sound,
                description = () => X("mpCmdSound", "admins: plays a sound (alarm, warning, success, explosion...)") },
            new Command { name = "music", usage = "[players] <track | stop>", arg = Arg.PlayerText, self = true,
                available = () => LocalIsAdmin, allowed = IsAdmin, run = NetAdmin.Music,
                description = () => X("mpCmdMusic", "admins: plays a music track instead of the game's (battle, boss, void...), stop ends it") },
            new Command { name = "event", usage = "<name [setting=value ...] | stop | list>", arg = Arg.Text, optional = true, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = EventRunner.Command, description = () => X("mpCmdEvent", "admins: runs an event (game modes like waves), stops it or lists them") },
            new Command { name = "motd", usage = "[reload | set <text> | clear]", arg = Arg.Text, optional = true, available = Everyone, allowed = Anyone,
                run = NetMotd.Command, description = () => X("mpCmdMotd", "shows the server's message of the day; admins: reload motd.txt, set or clear it") },
            new Command { name = "news", usage = "<text | clear>", arg = Arg.Text, available = () => LocalIsAdmin, allowed = IsAdmin, run = News,
                description = () => X("mpCmdNews", "admins: a GalNet item on every station's news ticker (\"clear\" empties the news)") },
            new Command { name = "mute", usage = "<players> [minutes]", arg = Arg.PlayerText, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = (a, by) => NetAdmin.Mute(a, by, true), description = () => X("mpCmdMute", "admins: blocks a player's chat (for the session or some minutes)") },
            new Command { name = "unmute", usage = "<players>", arg = Arg.Player, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = (a, by) => NetAdmin.Mute(a, by, false), description = () => X("mpCmdUnmute", "admins: lets a muted player chat again") },
            new Command { name = "admin", usage = "<player>", arg = Arg.Player, available = () => LocalIsHost || LocalRole >= NetModeration.Master,
                allowed = p => NetProfiles.Enabled || IsHostPlayer(p),
                run = (a, by) => NetProfiles.Enabled ? Moderate("admin", a, by) : SetAdmin(a, by, true),
                description = () => X("mpCmdAdmin", "host: makes a player an admin (with profiles: the master admin, for good)") },
            new Command { name = "unadmin", usage = "<player>", arg = Arg.Player, available = () => LocalIsHost || LocalRole >= NetModeration.Master,
                allowed = p => NetProfiles.Enabled || IsHostPlayer(p),
                run = (a, by) => NetProfiles.Enabled ? Moderate("unadmin", a, by) : SetAdmin(a, by, false),
                description = () => X("mpCmdUnadmin", "host: takes a player's admin rights away") },

            // A dedicated server with player profiles: moderation by the profiles' roles (NetModeration; it checks the role).
            new Command { name = "tempban", usage = "<player> <minutes> [reason]", arg = Arg.PlayerText, available = () => LocalIsOp, allowed = Anyone,
                run = (a, by) => Moderate("tempban", a, by), description = () => X("mpCmdTempban", "ops: bans a pilot and their devices for some minutes") },
            new Command { name = "ban", usage = "<player> [reason]", arg = Arg.PlayerText, available = () => LocalRole >= NetModeration.Admin, allowed = Anyone,
                run = (a, by) => Moderate("ban", a, by), description = () => X("mpCmdBan", "admins: bans a pilot and their devices for good") },
            new Command { name = "unban", usage = "<name | profile>", arg = Arg.Text, available = () => LocalIsOp, allowed = Anyone,
                run = (a, by) => Moderate("unban", a, by), description = () => X("mpCmdUnban", "ops: lifts a ban") },
            new Command { name = "bans", available = () => LocalIsOp, allowed = Anyone,
                run = (a, by) => Moderate("bans", a, by), description = () => X("mpCmdBans", "ops: the bans") },
            new Command { name = "op", usage = "<player>", arg = Arg.Player, available = () => LocalRole >= NetModeration.Admin, allowed = Anyone,
                run = (a, by) => Moderate("op", a, by), description = () => X("mpCmdOp", "admins: makes a pilot an op (kicks, temporary bans)") },
            new Command { name = "deop", usage = "<player>", arg = Arg.Player, available = () => LocalRole >= NetModeration.Admin, allowed = Anyone,
                run = (a, by) => Moderate("deop", a, by), description = () => X("mpCmdDeop", "admins: takes a pilot's op role away") },
            new Command { name = "staff", available = Everyone, allowed = Anyone,
                run = (a, by) => Moderate("staff", a, by), description = () => X("mpCmdStaff", "the server's master admins, admins and ops") },
            new Command { name = "say", usage = "<text>", arg = Arg.Text, available = () => LocalRole >= NetModeration.Admin, allowed = Anyone,
                run = (a, by) => Moderate("say", a, by), needsPlayer = true, description = () => X("mpCmdSay", "admins: an announcement under your role") },
            new Command { name = "disband", usage = "<TAG>", arg = Arg.Text, available = () => LocalRole >= NetModeration.Admin, allowed = Anyone,
                run = (a, by) => Moderate("disband", a, by), description = () => X("mpCmdDisband", "admins: ends a faction") },
            new Command { name = "deleteprofile", usage = "<profile>", arg = Arg.Text, available = () => LocalRole >= NetModeration.Master, allowed = Anyone,
                run = (a, by) => Moderate("deleteprofile", a, by), description = () => X("mpCmdDeleteProfile", "master: deletes an offline pilot's profile") },
            new Command { name = "coop", usage = "[strength 0|25|50|100] [rewards shared|full]", arg = Arg.Text,
                available = () => NetCoop.Active && LocalIsAdmin, allowed = IsAdmin, run = NetCoop.Command,
                description = () => X("mpCmdCoop", "the host: the co-op campaign's enemy strength and story credits") },
            new Command { name = "flightmodel", usage = "[gof2 | es2 | player]", arg = Arg.Text, available = () => LocalIsAdmin, allowed = IsAdmin,
                run = FlightModel,
                description = () => X("mpCmdFlightModel", "admins: the flight model for everyone: gof2, es2 or player (each player's own)") },
            new Command { name = "settings", available = () => LocalRole >= NetModeration.Admin, allowed = Anyone,
                run = (a, by) => Moderate("settings", a, by), description = () => X("mpCmdSettings", "admins: the server's settings") },
            new Command { name = "set", usage = "<key> <value>", arg = Arg.Text, available = () => LocalRole >= NetModeration.Admin, allowed = Anyone,
                run = (a, by) => Moderate("set", a, by), description = () => X("mpCmdSet", "admins: changes a server setting (saved)") },
            new Command { name = "web", available = () => ProfilesOn && LocalIsOp, allowed = Anyone, needsPlayer = true,
                run = (a, by) => WebAdmin.CodeFor(by as NetPlayer),
                description = () => X("mpCmdWeb", "ops: a one-time login code for the server's web admin") },
            new Command { name = "claimadmin", usage = "<token>", arg = Arg.Text, available = Everyone, allowed = Anyone,
                run = (a, by) => Moderate("claimadmin", a, by), needsPlayer = true,
                description = () => X("mpCmdClaimAdmin", "makes you the server's master admin with its admin token") },

            // Player profiles (NetProfiles), factions (NetFactions), arena matches (NetArena): answered by their own handlers.
            new Command { name = "link", usage = "[code] [force]", arg = Arg.Text, optional = true, available = Everyone, allowed = Anyone,
                run = (a, by) => Handler(NetProfiles.Command, true, "link", a, by), needsPlayer = true,
                description = () => X("mpCmdLink", "a code to join this profile from another device, or joins one with its code") },
            new Command { name = "control", available = Everyone, allowed = Anyone,
                run = (a, by) => Handler(NetProfiles.Command, true, "control", a, by), needsPlayer = true,
                description = () => X("mpCmdControl", "this device takes control of your profile") },
            new Command { name = "profile", available = Everyone, allowed = Anyone,
                run = (a, by) => Handler(NetProfiles.Command, true, "profile", a, by), needsPlayer = true,
                description = () => X("mpCmdProfile", "your profile on this server") },
            new Command { name = "faction", usage = "<create | invite | join | leave | kick | promote | demote | leader | disband | info | list | deposit | withdraw | claim | unclaim | home | claims | siege | sieges> ...",
                arg = Arg.Text, optional = true, available = Everyone, allowed = Anyone, run = (a, by) => Handler(NetFactions.Command, true, "faction", a, by), needsPlayer = true,
                description = () => X("mpCmdFaction", "your faction: members, bank, claimed stations, sieges") },
            new Command { name = "f", usage = "<text>", arg = Arg.Text, available = Everyone, allowed = Anyone, needsPlayer = true,
                run = (a, by) => NetAdmin.IsMuted(by.OwnerClientId, out string muted) ? muted : Handler(NetFactions.Command, true, "f", a, by),
                description = () => X("mpCmdFactionChat", "one line to your faction's pilots online") },
            new Command { name = "duel", usage = "<player> [voids]", arg = Arg.PlayerText, available = Everyone, allowed = Anyone,
                run = (a, by) => Handler(NetArena.Command, false, "duel", a, by), needsPlayer = true,
                description = () => X("mpCmdDuel", "challenges a docked pilot to an arena duel (voids: with the Void fighters)") },
            new Command { name = "accept", available = Everyone, allowed = Anyone, run = (a, by) => Handler(NetArena.Command, false, "accept", a, by), needsPlayer = true,
                description = () => X("mpCmdAccept", "accepts an arena challenge") },
            new Command { name = "decline", available = Everyone, allowed = Anyone, run = (a, by) => Handler(NetArena.Command, false, "decline", a, by), needsPlayer = true,
                description = () => X("mpCmdDecline", "declines an arena challenge") },
            new Command { name = "ffa", usage = "[voids]", arg = Arg.Text, optional = true, available = Everyone, allowed = Anyone,
                run = (a, by) => Handler(NetArena.Command, false, "ffa", a, by), needsPlayer = true,
                description = () => X("mpCmdFfa", "queues for an arena free-for-all") },
            new Command { name = "arena", available = Everyone, allowed = Anyone, run = (a, by) => Handler(NetArena.Command, false, "arena", a, by), needsPlayer = true,
                description = () => X("mpCmdArena", "the arena matches and queues") },
            new Command { name = "top", available = Everyone, allowed = Anyone, run = (a, by) => Handler(NetArena.Command, false, "top", a, by), needsPlayer = true,
                description = () => X("mpCmdTop", "the arena leaderboard") },

            // The squad's distress calls (NetDistress): this game's own.
            new Command { name = "sos", available = Everyone, local = _ => Sos(),
                description = () => X("mpCmdSos", "in space: calls your squad for help, or ends the call") },
            new Command { name = "assist", usage = "<player>", arg = Arg.Player, available = Everyone, local = Assist,
                description = () => X("mpCmdAssist", "flies to a squadmate calling for help") },
            new Command { name = "follow", usage = "[<player>]", arg = Arg.Player, optional = true, available = Everyone, local = Follow,
                description = () => X("mpCmdFollow", "goes after a squadmate whenever they jump to another orbit or system; no name: stops") },
            new Command { name = "squad", usage = "[invite <player> | accept | decline | leave]", arg = Arg.Text, optional = true, available = Everyone,
                local = Squad, description = () => X("mpCmdSquad", "your squad; invite a pilot docked at your station, answer an invitation, leave") },
            // Trades between players (NetTradeClient / NetTrade): asked and answered by this game, the server checks them.
            new Command { name = "trade", usage = "[<player> | accept | decline | cancel]", arg = Arg.Text, optional = true, available = Everyone,
                local = Trade, description = () => X("mpCmdTrade", "trades credits and cargo with a pilot docked at your station") },
        };

        static bool ProfilesOn => NetState.Instance != null && NetState.Instance.ProfilesOn;

        /// <summary>This player's server role (NetModeration; 0 without profiles).</summary>
        static int LocalRole => NetPlayer.Local != null ? NetPlayer.Local.StaffRole : 0;

        /// <summary>This player may use the moderation commands: an admin, or an op of a server with profiles.</summary>
        static bool LocalIsOp => LocalIsAdmin || LocalRole >= NetModeration.Op;

        /// <summary>Server: 'p' may kick: an admin, or an op of a server with profiles.</summary>
        static bool IsOp(IPilot p) => IsAdmin(p) || (p is NetPlayer np && np.StaffRole >= NetModeration.Op);

        /// <summary>Server: a moderation command (NetModeration checks the role; the console outranks everyone).</summary>
        /// <summary>/flightmodel [gof2 | es2 | player]: the session's flight model (NetRules.HostFlight), one for everyone so
        /// PvP is fair; no argument shows it. Saved like /set flightmodel on a server that keeps its settings.</summary>
        static string FlightModel(string args, IPilot by)
        {
            args = (args ?? "").Trim();
            if (args.Length == 0)
                return string.Format(X("mpFlightModelNow", "The flight model is {0}. /flightmodel gof2 | es2 | player changes it."), SessionRules.FlightText(NetRules.HostFlight));
            if (SessionRules.ParseFlight(args) < SessionRules.PlayerFlight) return UsageOf("flightmodel");
            return NetServerSettings.Set(NetRules.FlightKey, args, IssuerName(by));
        }

        static string Moderate(string name, string args, IPilot by)
        {
            if (!NetProfiles.Enabled) return ModerateWithoutProfiles(name, args, by);
            return (by == null ? NetModeration.ConsoleCommand(name, args) : NetModeration.Command(by.OwnerClientId, $"/{name} {args}")) ?? "";
        }

        /// <summary>A session without profiles (hosted fresh from the menu): the settings and announcements still work for
        /// its admins (the host); bans, ops and the rest need profiles.</summary>
        static string ModerateWithoutProfiles(string name, string args, IPilot by)
        {
            bool admin = by == null || IsAdmin(by);
            switch (name)
            {
                case "settings":
                    return admin ? NetServerSettings.ListText() : X("mpCmdNoRights", "You don't have the rights for that command.");
                case "set":
                {
                    if (!admin) return X("mpCmdNoRights", "You don't have the rights for that command.");
                    args = (args ?? "").Trim();
                    int sp = args.IndexOf(' ');
                    return sp < 0 ? "/set <key> <value> (/settings lists them)" : NetServerSettings.Set(args.Substring(0, sp), args.Substring(sp + 1), IssuerName(by));
                }
                case "say":
                {
                    if (!admin) return X("mpCmdNoRights", "You don't have the rights for that command.");
                    string text = NetChat.Clean(args);
                    if (text.Length == 0) return "/say <text>";
                    NetState.Instance?.ServerChat(by == null ? IssuerName(null) : $"[admin] {by.DisplayName}", text);
                    return "";
                }
                default:
                    return X("mpCmdNeedsProfiles", "That needs player profiles: a dedicated server, or a game hosted with World: Persistent.");
            }
        }

        /// <summary>Server: a command answered by a (client, "/name args") handler (NetArena, NetFactions, NetProfiles);
        /// 'profiles' = it needs a server with player profiles.</summary>
        static string Handler(Func<ulong, string, string> handler, bool profiles, string name, string args, IPilot by)
        {
            if (by == null) return "";
            if (profiles && !NetProfiles.Enabled) return X("mpCmdNeedsProfiles", "That needs player profiles: a dedicated server, or a game hosted with World: Persistent.");
            return handler(by.OwnerClientId, $"/{name} {args}".Trim()) ?? "";
        }

        /// <summary>/news: an admin's item on the stations' tickers (NetNews), or "clear".</summary>
        static string News(string args, IPilot by)
        {
            args = (args ?? "").Trim();
            if (args.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                NetNews.Clear();
                Debug.Log($"Server: {IssuerName(by)} cleared the news.");
                return X("mpNewsCleared", "The news is cleared.");
            }
            if (args.Length > NetNews.MaxTextLength) return string.Format(X("mpNewsLong", "At most {0} characters."), NetNews.MaxTextLength);
            Debug.Log($"Server: {IssuerName(by)} posted news.");
            return NetNews.Post(NetNews.Kind.Galnet, NetNews.Safe(args)) ? X("mpNewsPosted", "On every station's ticker now.") : X("mpNewsFailed", "Not posted.");
        }

        /// <summary>/sos: the squad's distress call (NetDistress; only in space and in a squad, else why not).</summary>
        static void Sos()
        {
            if (!NetSquad.InSquad && !NetDistress.Active)
                NetChat.Notice(X("mpSosNoSquad", "Distress calls go to your squad: form one first (docked: /squad invite <pilot>)."));
            else NetChat.Notice(NetDistress.Toggle());
        }

        /// <summary>/squad: who is in it and where; invite, accept, decline, leave (the same as the squad window's buttons).</summary>
        static void Squad(string args)
        {
            args = (args ?? "").Trim();
            int space = args.IndexOf(' ');
            string sub = (space < 0 ? args : args.Substring(0, space)).ToLowerInvariant();
            string rest = space < 0 ? "" : args.Substring(space + 1).Trim();
            switch (sub)
            {
                case "":
                case "info":
                {
                    var members = NetSquad.Members();
                    var waiting = NetSquad.Invites;
                    if (members.Count == 0)
                    {
                        NetChat.Notice(waiting.Count > 0
                            ? string.Format(X("mpSquadWaiting", "{0} invites you to their squad: /squad accept or /squad decline."), waiting[waiting.Count - 1].name)
                            : X("mpSquadNone", "You aren't in a squad. Docked, /squad invite <pilot> invites a pilot docked at the same station."));
                        return;
                    }
                    var sb = new StringBuilder(X("mpSquadList", "Your squad:"));
                    foreach (var m in members)
                        sb.Append('\n').Append(m.Distress ? "⚠ " : "").Append(m.DisplayName).Append(m.IsOwner ? " " + X("mpCmdYou", "(you)") : "")
                          .Append(": ").Append(WhereText(m));
                    NetChat.Notice(sb.ToString());
                    return;
                }
                case "invite":
                {
                    var p = MatchPlayer(rest, out _) as NetPlayer;   // squads are session players
                    if (p == null) { NetChat.Notice(string.Format(X("mpSquadNoPilot", "No pilot called \"{0}\" online."), rest)); return; }
                    if (p.IsOwner) { NetChat.Notice(X("mpSquadSelf", "That's you.")); return; }
                    if (NetSquad.Same(p, NetPlayer.Local)) { NetChat.Notice(string.Format(X("mpSquadAlready", "{0} is in your squad already."), p.DisplayName)); return; }
                    var me = NetPlayer.Local;
                    if (me == null || !me.InHangar || !p.InHangar || me.Station != p.Station)
                    {
                        NetChat.Notice(X("mpSquadHangarOnly", "Squads can only be formed while docked in the same hangar."));
                        return;
                    }
                    NetSquad.InviteTo(p);
                    NetChat.Notice(string.Format(X("mpSquadSent", "Invitation sent to {0} (45 seconds)."), p.DisplayName));
                    return;
                }
                case "accept":
                {
                    var waiting = NetSquad.Invites;
                    if (waiting.Count == 0) { NetChat.Notice(X("mpSquadNoInvite", "No squad invitation is waiting.")); return; }
                    NetSquad.Accept(waiting[waiting.Count - 1]);
                    return;
                }
                case "decline":
                {
                    var waiting = NetSquad.Invites;
                    if (waiting.Count == 0) { NetChat.Notice(X("mpSquadNoInvite", "No squad invitation is waiting.")); return; }
                    NetSquad.Decline(waiting[waiting.Count - 1]);
                    NetChat.Notice(X("mpSquadDeclined", "Invitation declined."));
                    return;
                }
                case "leave":
                    if (!NetSquad.InSquad) { NetChat.Notice(X("mpCmdNoSquad", "You're not in a squad.")); return; }
                    NetSquad.Leave();
                    return;
                default:
                    NetChat.Notice("/squad [invite <player> | accept | decline | leave]");
                    return;
            }
        }

        /// <summary>/trade: ask a pilot docked here, answer a request, cancel the open trade (the Multiplayer window's Trade tab).</summary>
        static void Trade(string args)
        {
            args = (args ?? "").Trim();
            string sub = args.ToLowerInvariant();
            var waiting = NetTradeClient.Requests;
            switch (sub)
            {
                case "":
                    if (NetTradeClient.Current != null) { UI.UiHooks.OpenTrade?.Invoke(); return; }
                    NetChat.Notice(waiting.Count > 0
                        ? string.Format(X("mpTradeWaitingCmd", "{0} wants to trade with you: /trade accept or /trade decline."), waiting[waiting.Count - 1].name)
                        : "/trade [<player> | accept | decline | cancel]");
                    return;
                case "accept":
                    if (waiting.Count == 0) { NetChat.Notice(X("mpTradeNoRequest", "No trade request is waiting.")); return; }
                    NetTradeClient.Accept(waiting[waiting.Count - 1]);
                    return;
                case "decline":
                    if (waiting.Count == 0) { NetChat.Notice(X("mpTradeNoRequest", "No trade request is waiting.")); return; }
                    NetTradeClient.Decline(waiting[waiting.Count - 1]);
                    return;
                case "cancel":
                    if (NetTradeClient.Current == null) { NetChat.Notice(X("mpTradeNone", "You aren't trading.")); return; }
                    NetTradeClient.Cancel();
                    return;
            }
            var p = MatchPlayer(args, out _) as NetPlayer;
            if (p == null) { NetChat.Notice(string.Format(X("mpSquadNoPilot", "No pilot called \"{0}\" online."), args)); return; }
            if (p.IsOwner) { NetChat.Notice(X("mpTradeSelf", "You can't trade with yourself.")); return; }
            var me = NetPlayer.Local;
            if (me == null || !me.InHangar || !p.InHangar || me.Station != p.Station)
            {
                NetChat.Notice(X("mpTradeHangarOnly", "Trades are only possible while docked in the same hangar."));
                return;
            }
            NetTradeClient.Ask(p);   // the server answers (sent, or why not)
        }

        /// <summary>/assist: help a squadmate calling (NetDistress).</summary>
        static void Assist(string who)
        {
            var caller = MatchPlayer(who, out _) as NetPlayer;
            NetChat.Notice(caller != null && NetSquad.Same(caller, NetPlayer.Local) ? NetDistress.Help(caller) ?? ""
                : string.Format(X("mpHelpNoMate", "No squadmate called \"{0}\"."), who));
        }

        /// <summary>/follow: follow a squadmate from orbit to orbit, or (no name, or the one followed) stop (NetFollow).</summary>
        static void Follow(string who)
        {
            who = (who ?? "").Trim();
            if (who.Length == 0) { NetChat.Notice(NetFollow.Stop() ?? X("mpFollowNobody", "You aren't following anyone. /follow <squadmate> follows one.")); return; }
            var mate = MatchPlayer(who, out _) as NetPlayer;
            if (mate == null || mate == NetPlayer.Local || !NetSquad.Same(mate, NetPlayer.Local))
                NetChat.Notice(string.Format(X("mpHelpNoMate", "No squadmate called \"{0}\"."), who));
            else NetChat.Notice(NetFollow.Toggle(mate) ?? "");
        }

        static string X(string key, string english) => Localization.Extra(key, english);

        static bool LocalIsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

        /// <summary>This player may use the admin commands: the host, made an admin by the host / the server console, or a
        /// profile's admin / master (NetModeration).</summary>
        public static bool LocalIsAdmin => LocalIsHost || (NetPlayer.Local != null && (NetPlayer.Local.IsAdmin || NetPlayer.Local.StaffRole >= NetModeration.Admin));

        /// <summary>Server: 'p' has admin rights (the host's own player, made an admin, or a profile's admin / master).</summary>
        public static bool IsAdmin(IPilot p) => p != null && (p.IsAdmin || IsHostPlayer(p) || (p is NetPlayer np && np.StaffRole >= NetModeration.Admin));

        internal static bool IsHostPlayer(IPilot p) =>
            p != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost && p.OwnerClientId == NetworkManager.ServerClientId;

        static bool Ready => NetState.Instance != null && NetState.Instance.IsSpawned;

        static Command Get(string name) => Array.Find(Commands, c => c.name == name);

        static string Usage(Command c) => $"/{c.name}{(c.usage.Length > 0 ? " " + c.usage : "")}";

        /// <summary>"/name usage" of a command (NetAdmin's answers).</summary>
        internal static string UsageOf(string name) { var c = Get(name); return c != null ? Usage(c) : "/" + name; }

        // ---- player arguments: names, client ids and selectors ---------------------------------------------

        /// <summary>While a bar mission's event runs a line (EventRunner.Tick): the players its selectors may find (null: all).</summary>
        internal static Func<IPilot, bool> Scope;

    }
}
