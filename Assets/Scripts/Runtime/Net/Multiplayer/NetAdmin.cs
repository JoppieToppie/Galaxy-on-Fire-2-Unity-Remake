// NetAdmin.cs
// Remake multiplayer: the admin commands that act on players' games (NetCommands' table; the chat and the dedicated
// server's console alike). The server checks the rights and the arguments, then sends the order to each target's own game
// (NetState.SendAdmin, server-only), which applies it to its own ship and Session: the server holds no game state.
//   /kill [players]                         destroys their ships (in space; also through god mode)
//   /heal [players]                         hull, shield, armor and the gamma pool full
//   /give [players] <item> [amount] [mount] items into the hold (an item index or name; 1..1000, default 1); "mount" docked:
//                                           mounted like the hangar does (Cheats.GiveAndMount)
//   /take [players] <item> [amount | all] [mounted]   remake: items out of the hold (a mission's hand-in; default all of
//                                           them); "mounted": the mounted ones too, like the story takes the jump drive
//                                           (Cheats.TakeItem)
//   /credits [players] <amount>             credits added (negative: taken, never below 0)
//   /standing [players] <race> <amount> [set]   the standing toward a game race or a mod's race moved by the
//                                           amount (or set to it); NetAdminStanding.cs
//   /spawn [players] <ship | object> [race] [count] [enemy | friendly | neutral | standing] [turrets <item,item>]
//          [named <name>] [at x y z]
//                                           NPC ships 400 m ahead of each player as their orbit's traffic (DebugSpawner):
//                                           a ship index or name, a race (terran, vossk, nivelian, midorian, pirate, void,
//                                           specter, or a mod's race; default the ship's maker, else pirates),
//                                           1..10 of them, enemy by
//                                           default; neutral = neither side whatever the standings, until shot; standing =
//                                           by the race's standing; "at x y z": there in that player's orbit (game
//                                           coordinates, what /pos shows), else 400 m ahead of them; a name that is no
//                                           ship is an assembled object (assemblies.json: station_083_terran, ...) placed
//                                           as scenery, ahead of them (far enough out for its size) or there;
//                                           "named <name>" (to the end, or up to "at x y z"; quotes optional): the ships'
//                                           name on the lock plate (numbered "Name 1", "Name 2"... for several; the other
//                                           players see it too, NetProxy's label), an object's name as a HUD marker;
//                                           "turrets 48,182" (remake, generic turrets): turret items (numbers or mod:id) on
//                                           the ships' turret mounts, aimed and fired by the NPC turrets' rules
//                                           (World.NpcMountedTurrets; none = the type's, a mod's "npcTurrets");
//                                           "hull 2.5" / "guns 1.5" (remake, a modder's suggestion): the ships' hull and gun
//                                           damage scaled (DebugSpawner.Scale; 0.1..50);
//   /spawn [players] type <key> [named <name>] [at x y z]   remake: an NPC ship type (npcships.json: battleship, carrier,
//                                           vossk_battleship, freighter, pirate_boss or a mod's "mod:id") with its own
//                                           hull, guns, escorts and turrets (DebugSpawner.SpawnNpcType)
//                                           "wreck" [terran | vossk | nivelian | midorian] (remake): a hackable freighter
//                                           wreck (TrafficPlan.HackableWreck, the race's own; any race at random), 1.5 km
//                                           ahead or at x y z; hacking it fires the events' "on hacked" (once; it is only
//                                           in that player's game, the others see it but can't dock)
//   /mute <players> [minutes], /unmute <players>   their chat and whispers dropped on the server (default: the session)
// The debug panel's tools (Cheats, PlayerHull, DebugSpawner), for one player:
//   /ship [players] <ship | own>            fly any hull of the Ships tab (a ship's number or name, the capital ships by
//                                           name; docked only the ownable ones), "own" back to the ship flown before
//   /ammo [players]                         every mounted secondary to 50
//   /reveal [players], /peace [players]     every system on the map; both standing axes neutral, no station grudges
//   /system [players] <system> <show | hide | reset>   one system on (or off) their map, over what is revealed and a mod's
//                                           "available" condition, until reset (GalaxyMap.SetShown, saved); a player in a
//                                           hidden system is taken out (SpaceLevel)
//   /cheat [players] <flag> [on | off]      a debug toggle for the session (god, ammo, cooldown, boost, onehit, locks,
//                                           shopping, jumps; no on / off: toggled), whatever the session allows
//   /title [players] <text> [| subtitle] [for <seconds>]   a big title on their screens (EventScreen; 4 s by default;
//                                           "clear" takes it away)
//   /timer [players] <seconds | m:ss> [label]   a countdown at the top of their screens ("stop" takes it away)
//   /dialog [players] <speaker> : <text> [| [speaker :] next page ...]   the game's dialogue window, page by page, each
//                                           page with its speaker (a page without one keeps the last): a story speaker by name
//                                           (Keith, Gunant...; "Keith as Bob" renames it), a race and a name ("vossk K'ekki",
//                                           "terran female Jane": a random face of that race, the same for everyone and for
//                                           every page of it), or "player": the reader, Keith's face with their pilot name;
//                                           %player% in a text or name is the reader's name; shown after any dialogue already
//                                           open (EventScreen's queue); a page "reward [title]: <rewards>" pays when the
//                                           dialogue closes (like /reward)
//   /reward [players] <credits | item [amount]> [+ more ...] [| title]   the mission payout: credits and / or items into
//                                           the hold, shown in the reward box (EventScreen.ShowReward: "Mission accomplished!"
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
using GoF2Remake.Events;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class NetAdmin
    {
        public enum Order : byte { Kill = 1, Heal = 2, Give = 3, Credits = 4, Spawn = 5, Ship = 6, Ammo = 7, Reveal = 8, Peace = 9, Cheat = 10, Object = 11, Title = 12, Timer = 13, Dialog = 14, Reward = 15, Scoreboard = 16, Sound = 17, Music = 18, Respawn = 19, Rules = 20, Ask = 21, Provoke = 22, Radio = 23, Waypoint = 24, Cutscene = 25, Camera = 26, Fade = 27, Letterbox = 28, Npc = 29, Flight = 30, Wreck = 31, System = 32, Wormhole = 33, Standing = 34, Take = 35, NpcType = 36 }

        const int MaxGive = 1000, MaxSpawn = 10, MaxCredits = 999999999;
        /// <summary>A /spawn name's length, and its separator in the order's text (taken out of names).</summary>
        const int MaxSpawnName = 32;
        const char NameSeparator = '|';
        /// <summary>/spawn's turret items in the order's text: "at" + TurretSeparator + "48,182" + the name.</summary>
        const char TurretSeparator = '~';
        /// <summary>/spawn's hull and gun factors in the order's text: "at" + FactorSeparator + "2,1.5" + the turrets + the name.</summary>
        const char FactorSeparator = '^';
        const float MaxSpawnFactor = 50f;
        const int MaxSpawnTurrets = 4;

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

        /// <summary>The spot an event wormhole opens at without "x y z": about 4 km out, ahead of the station and 2.6 km to its
        /// side, so its 2 km pull misses the ships launching straight ahead (along game +z from x 0, y 0).</summary>
        public static readonly Vector3 WormholeDefaultAt = new Vector3(50000f, 15000f, 60000f);

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
                    // a = 1: a siege fight's kill (NetFactions.OnPilotKilled), not an admin's order.
                    NetChat.Notice(a == 1 ? X("mpSiegeKillRepair", "Kill confirmed: your ship is repaired.")
                                          : string.Format(X("mpAdmHealedYou", "{0} repaired your ship."), by));
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
                case Order.Take:
                {
                    // a: the item, b: the amount (0 = all), c: 1 = the mounted ones too. Silent when nothing was aboard.
                    if (!NetGuard.Item(a)) return;
                    int taken = Cheats.TakeItem(a, Mathf.Min(b, MaxGive * 100), c == 1, out bool unmounted);
                    if (unmounted && level != null && level.Player != null) level.SwapPlayerShip(Session.ShipIndex);   // the loadout, guns and pools again
                    if (taken > 0) NetChat.Notice(string.Format(X("mpAdmTookYou", "{0} took {1} x {2}."), by, taken, UI.ItemInfo.ItemName(a)));
                    break;
                }
                case Order.Credits:
                    Session.Credits = (int)Mathf.Clamp((long)Session.Credits + a, 0L, MaxCredits);
                    NetChat.Notice(string.Format(a > 0 ? X("mpAdmCreditsYou", "{0} gave you {1} credits.") : X("mpAdmCreditsTakenYou", "{0} took {1} credits."),
                        by, Math.Abs(a)));
                    break;
                case Order.Spawn:
                {
                    if (level == null || NetGame.Db.Ship(a) == null || !CustomShips.Offered(a)) return;
                    string at = SplitName(text, out string shipName);
                    var turrets = SplitTurrets(ref at);
                    var (hullFactor, gunFactor) = SplitFactors(ref at);
                    Notice(by, DebugSpawner.SpawnShip(level, b, a, (DebugSpawner.Behaviour)Mathf.Clamp(c & 0xff, 0, 3), Mathf.Clamp((c >> 8) & 0xf, 1, MaxSpawn),
                        ParseAt(at), c >> 12, shipName, turrets, hullFactor, gunFactor));
                    break;
                }
                case Order.NpcType:
                {
                    // text: the type's key + "@" + "x y z" (optional) + the name; a: the event batch.
                    if (level == null) return;
                    string spec = SplitName(text, out string typeName) ?? "";
                    int sep = spec.IndexOf('@');
                    var type = Modding.ModNpcShips.Get(sep < 0 ? spec : spec.Substring(0, sep));
                    if (type == null) return;
                    Notice(by, DebugSpawner.SpawnNpcType(level, type, sep < 0 ? null : ParseAt(spec.Substring(sep + 1)), typeName, a));
                    break;
                }
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
                case Order.System:
                    // b: 1 show, 0 hide, 2 reset (the map's own state again); silent, the event / quest says why.
                    GalaxyMap.SetShown(a, b == 2 ? (bool?)null : b == 1);
                    break;
                case Order.Peace:
                    Cheats.MakePeace();
                    NetChat.Notice(string.Format(X("mpAdmPeaceYou", "{0} made peace for you with every race."), by));
                    break;
                case Order.Standing:
                    ApplyStanding(a, b, c != 0, text, by);
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
                    EventScreen.ShowTitle(nl < 0 ? text : text.Substring(0, nl), nl < 0 ? "" : text.Substring(nl + 1), a / 1000f);
                    break;
                }
                case Order.Timer:
                    EventScreen.ShowTimer(a, text);
                    break;
                case Order.Scoreboard:
                    EventScreen.ShowScoreboard(text);
                    break;
                case Order.Sound:
                    if (a == -2) EventScreen.PlayModSound(text);
                    else EventScreen.PlaySound(a);
                    break;
                case Order.Respawn:
                    EventRespawn.Apply(text);
                    break;
                case Order.Rules:
                    EventRules.Apply(a);
                    break;
                case Order.Flight:
                    EventFlight.Apply(a);
                    break;
                case Order.Waypoint:
                    EventWaypoint.Apply(text);
                    break;
                case Order.Wormhole:
                    EventWormhole.Apply(text);
                    break;
                case Order.Cutscene:
                    EventCutscene.ApplyCutscene(text);
                    break;
                case Order.Camera:
                    EventCutscene.ApplyCamera(text);
                    break;
                case Order.Fade:
                    EventCutscene.ApplyFade(text);
                    break;
                case Order.Letterbox:
                    EventCutscene.ApplyLetterbox(text);
                    break;
                case Order.Npc:
                    EventShipOrders.Apply(text);
                    break;
                case Order.Radio:
                {
                    // "id US name US face US text": a story speaker (a name = renamed), a generated face (-1) or the reader (-2).
                    var f = (text ?? "").Split('\u001f');
                    if (f.Length < 4 || !SpeakerSpec.TryParse(text, '\u001f', out var who)) return;
                    int id = who.id;
                    string me = EventHost.LocalName;
                    var line = new Traffic.Chatter { text = f[3].Replace("%player%", me), speaker = who.name.Replace("%player%", me) };
                    line.voice = Modding.ModVoices.Take(ref line.text);   // "[voice <clip>]" (the radio's 2 s delay covers the loading)
                    Modding.ModVoices.Preload(line.voice);
                    if (id == SpeakerSpec.Generated) line.portrait = who.face ?? new int[0];
                    else if (id == SpeakerSpec.Character) { line.character = who.characterKey; line.speaker = who.DisplayName(me); }
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
                    // "client:id,id,...": this game's ships by their proxies (NetworkObject ids) turn on that player (and their
                    // squad).
                    int colon = (text ?? "").IndexOf(':');
                    var spawned = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.SpawnManager : null;
                    if (spawned == null || colon <= 0 || !ulong.TryParse(text.Substring(0, colon), out ulong client)) return;
                    foreach (string idText in text.Substring(colon + 1).Split(','))
                    {
                        if (!ulong.TryParse(idText, out ulong id) || !spawned.SpawnedObjects.TryGetValue(id, out var obj) || obj == null) continue;
                        var ship = obj.GetComponent<NetProxy>()?.BoundShip;
                        if (ship != null) ship.aggressors.Add(client);
                    }
                    break;
                }
                case Order.Ask:
                    if (a < 0) EventScreen.CloseQuestion(-a);
                    else EventScreen.ShowQuestion(text);
                    break;
                case Order.Music:
                    if (a == -2) EventScreen.PlayModMusic(text);
                    else EventScreen.PlayMusic(a);
                    break;
                case Order.Dialog:
                {
                    string me = EventHost.LocalName;
                    var list = new List<UI.DialogueLine>();
                    var rewards = new List<(int credits, string items, string heading)>();
                    foreach (var line in (text ?? "").Split('\n'))
                    {
                        var f = line.Split('\u001f');
                        if (f.Length == 4 && f[0] == "R") { int.TryParse(f[1], out int cr); rewards.Add((cr, f[2], f[3])); continue; }
                        if (f.Length < 4 || list.Count >= 20 || !SpeakerSpec.TryParse(line, '\u001f', out var who)) continue;
                        string name = who.name.Replace("%player%", me), body = f[3].Replace("%player%", me);
                        int id = who.id;
                        // The reader: Keith's face (the player character, speaker 0) with their pilot name.
                        if (id == SpeakerSpec.Reader) id = 0;
                        string character = null;
                        if (id == SpeakerSpec.Character) { id = -1; character = who.characterKey; if (name.Length == 0) name = who.DisplayName(me); }
                        if (id >= StoryTable.SpeakerCount) continue;
                        string voice = Modding.ModVoices.Take(ref body);   // "[voice <clip>]": an original line or a mod's file
                        Modding.ModVoices.Preload(voice);
                        list.Add(new UI.DialogueLine { speaker = id, text = body, agentName = name.Length > 0 ? name : null, agentPortrait = who.face,
                                                            character = character, voice = voice });
                    }
                    // The reward pages pay when the dialogue closes (at once without pages).
                    EventScreen.QueueDialog(list, rewards.Count == 0 ? null : (System.Action)(() => { foreach (var r in rewards) GrantReward(r.credits, r.items, r.heading); }));
                    break;
                }
                case Order.Reward:
                {
                    var parts = (text ?? "").Split('\n');
                    GrantReward(a, parts.Length > 1 ? parts[1] : "", parts[0]);
                    break;
                }
                case Order.Wreck:
                {
                    if (level == null) return;
                    string at = SplitName(text, out string wreckName);
                    Notice(by, DebugSpawner.SpawnWreck(level, a, ParseAt(at), wreckName));
                    break;
                }
                case Order.Object:
                {
                    if (level == null || string.IsNullOrEmpty(text)) return;
                    string spec = SplitName(text, out string objectName);
                    int sep = spec.IndexOf('@');
                    Notice(by, DebugSpawner.SpawnObject(level, sep < 0 ? spec : spec.Substring(0, sep), sep < 0 ? null : ParseAt(spec.Substring(sep + 1)), objectName));
                    break;
                }
            }
        }
    }
}
