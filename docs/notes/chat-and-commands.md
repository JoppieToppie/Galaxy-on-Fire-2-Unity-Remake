# Chat and commands

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Multiplayer (remake-only)

- **Chat** (`NetChat`, `ChatView` in the flight HUD and the station menu; styles `Resources/GoF2Net/Chat.uss`; placed in
  percentages; on a small high-density screen (`UiScale.Large`: body text under 2.8 mm from the panel scaling and dpi,
  e.g. the Retroid Pocket G2) the chat, the squad window / invitation and the multiplayer window use their large
  variants (`.chat--large`, `.squad--large`, `.mpw--large`: about 1.5x, finger-sized buttons, the window 96 x 94 %); the
  on-screen keyboard shows its own input box (`hideInput` false: hidden, holding Backspace stopped after a letter);
  nothing writes to the keyboard while it is up (`PhoneKeyboard`, also every text field of the multiplayer window, which
  had UI Toolkit's own soft keyboard: on GameActivity each write to a TouchScreenKeyboard (text, character limit, a new
  Open) restarts the IME's input connection in Unity's `UGASoftKeyboard::UpdateTextInputState` (the Editor's
  AndroidPlayer/Source/GameActivity), which ends a held key's repeat; a player's report 2026-10-18, held Backspace
  deleted one letter; the chat rows' Local / Global and Send buttons keep the keyboard up and hand the focus back to the
  line; not yet tried on a phone); the lines never select all on focus (after a send the next line showed highlighted);
  the multiplayer window: LB / RB or Q / E switch tabs, a controller starts on the tab row, the list scrolls to the
  focus and a rebuild restores it, a phone typing raises the window to the top half, the Chat tab's line takes the focus
  only with keys and mouse): global
  (everyone) and local (the same orbit, or docked at the same station); the host stamps each line with the sender's name
  and location. The chat key (B), Enter in flight (remake rework 2026-10-22, players' request; in a session Enter is no
  longer the action prompt's second key there, `FlightHud.ActionByEnter`; the station keeps Enter for its menus), the
  typed character "/" (opens with the "/" in the line, any keyboard layout: `Keyboard.onTextInput`; like Minecraft) or the
  small Chat button above the lines (`.chat-tab`, a finger-sized button that opens on the press; its name only, CHAT) opens
  the input with the cursor in it (none of them while a menu, conversation, question or another text field has the keys,
  `ChatView.CanOpenByKey`) (`ChatView` keeps focusing the
  field for a few frames until its row shows; the opening key's letter, which arrives after the focus, is dropped), the
  send key (Enter / keypad Enter, `GameControls.ChatSend`) sends, the channel key (Tab, `GameControls.ChatChannel`)
  cycles Local / Global and, for a squad's member, Squad, for a faction's, Faction (`NetChat.NextChannel` / `SendingNow`:
  Squad lines go as the server's `/s` (the user's request, 2026-10-27), `NetState.SquadChat` -> `SquadLineRpc` to every
  member wherever they are -> `NetChat.ReceiveSquad`, a pink "[Squad]" tag; Faction lines as `/f`, `NetFactions.Talk` ->
  `NetState.TellFactionLine` -> `NetChat.ReceiveFaction`, a green "[Faction]" tag; back on Local when the player leaves the
  squad / faction), Esc closes only the chat (`NetChat.Keys` stays null on the frame the typing
  ended and the next, `NetChat.JustStopped`: the pause / system menu's Esc read the same press). Sending keeps the chat
  open for the next line (`ChatView.SendLine`: the field keeps
  the focus, a phone's keyboard comes straight back up), so a conversation needs no reopening; Enter / Done on an empty
  line, Esc / B, the row's × or the Chat tab close it. Phones: the field's own keyboard is off (`hideSoftKeyboard`) and `ChatView` opens
  the on-screen keyboard itself (`TouchScreenKeyboard`, its input box hidden, the text copied into the field each frame):
  Done / the checkmark sends, Back or a tap outside ends the typing with the draft kept (UI Toolkit's own keyboard only
  closed and blurred the field on Done, so nothing was sent); while typing the chat sits at the top of the screen
  (`.chat--phone.chat--open`, the tab hidden, the "/" suggestions capped) where the keyboard can't cover the line (at 36 %
  down the input row was under it; the station's chat, anchored at the bottom, stayed under it until 2026-10-23:
  `bottom: auto` there, from the left edge); any other text field on a phone (the pilot name prompt, the Host card, the
  Mod Builder) slides its screen up above the keyboard while focused (`KeyboardLift.Watch` beside `PadKeyboard.Attach`:
  the root's translate, TouchScreenKeyboard.area or 55 % of the screen; GameActivity's keyboard shows no input box of its
  own); a held Backspace repeats (2026-10-23, found in logcat: GameActivity's GameTextInput restarts the keyboard's input
  after every key event its view gets, `InputConnection.onKey` -> `restartInput`, and Gboard sends Backspace to the game's
  view as KEYCODE_DEL key events, so every field deleted one character and stopped, the game's own writes or not: the
  activity is `com.unity3d.player.GoF2GameActivity`, `Plugins/Android/GoF2GameActivity.java`, put in the manifest by the
  Editor's `AndroidGameActivity`, whose view takes the soft keyboard's Backspace while the keyboard is up and makes the
  edit through the input connection without the restart); Send with the keyboard up empties the keyboard's text too
  (`PhoneKeyboard.SetText`; the poll had copied the sent line back); the send and channel keys are rebindable rows read straight from the devices
  (`GameControls.PressedNow`: the flight map is off while typing), and their key events and characters stay out of the
  line. The field keeps the focus: the project-wide UI map's Navigate (arrows, W A S D), Tab and Submit (Space / Enter)
  are swallowed there (`StopPropagation` + `focusController.IgnoreEvent`: stopping alone still moved the focus to a menu
  button, which ended the typing). Enter / keypad Enter always send, read from the key event (the rebindable send key is
  read from the device and could miss the UI event's frame: the TextField then took Enter as its submit, lost the focus,
  and the line was only hidden by `Suspend`); a Send button after the line (touch, mouse). Another player's line plays the original's
  incoming-message sound (FMOD 125 Message_Inc, volume 0.241 × `Sfx.EventGain` × the FX volume, one at a time; a copy
  of the clip in `Resources/GoF2Net/ChatMessage.ogg`). Lines fade 12 s after arriving (full width on a solid background:
  drawn over the flight HUD's key hints, it covers them); join / leave notices; every line
  is also in the player log (`[Chat ...]`). **Chat commands** (`NetCommands`): a line starting with "/" is a command, never
  sent as chat. Every command that acts on the session runs on the server: the chat sends its name and arguments
  (`NetState.ServerCommandRpc`), the server checks the sender's rights (`allowed`) and runs it, the answer is a notice for
  the sender; the dedicated server's console runs the very same table (`NetCommands.RunOnServer` with no issuer: every
  right, named "Server"; `list` = players, `say` = g). Only what changes or reads this game stays local: `/help` (the
  commands this player can run, `available`), `/netstats`, `/pos` (the orbit and the game coordinates /tp takes), `/sos`
  and `/assist` (`NetDistress`), `/follow` (`NetFollow`). The arena (`/duel` ..., `/leave` leaves a queue or match before the squad), profile
  (`/link` ...), faction (`/faction`, `/c`) and moderation commands are rows of the same table calling their own
  handlers; the station window's buttons send them as chat lines, which `SendChatRpc` runs through
  `NetCommands.RunOnServer`. Server
  commands: `/players` (where, ship, squad, admin; admins and the console see the client ids), `/g` / `/l <text>` (one line
  to Global / Local), `/w <player> <text>` (a private message to that player only, "[From X]" / "[To X]" in violet, not
  logged), `/invite <player>` (docked at the same station) and `/leave`; admins `/kick <player> [reason]` (never the host's
  player or the issuer, only the host / the console kicks another admin; everyone gets "X was removed from the session by
  Y"), `/tp [players] <player | station [x y z | dock]>` (`NetTeleport`: players (the chat: yourself by default) to another
  player (beside their ship, or into the hangar they are docked in), an orbit by station index, name or "void" (the launch
  spot, or game coordinates facing the station) or a station's hangar ("dock"); the server sends the order to that
  player's game, which moves its ship in place within the same orbit (`SpaceLevel.MoveForTeleport`; not while mining /
  object docking / jumping) or loads the orbit / station like a jump (`SpaceLevel.TeleportOut`, the pose through
  `NetTeleport.TakePose` in `SpawnPlayer`, no launch camera); refused while destroyed or leaving) and `/tphere <player>`;
  the host (and the console) `/admin` / `/unadmin <player>`. Admin tools (`NetAdmin`: the server checks the rights and arguments, logs the
  order and sends it to each target's own game, `NetState.AdminRpc`, server-only; in the chat the players may be left out
  = yourself): `/kill [players]` (in space, through god mode, "X was destroyed by Y" for everyone), `/heal [players]`
  (`Cheats.Repair`), `/give [players] <item> [amount] [mount]` (an item's number or name, 1..1000; "mount" docked:
  `Cheats.GiveAndMount`), `/credits [players] <amount>` (negative takes, never below 0), `/standing [players] <race> <amount> [set]`
  (remake, 2026-10-28, `NetAdminStanding`: the standing toward a game race 0-3 (its axis, `Standing.Move`) or a mod's
  race (`ModRaces.SetStanding`; it travels by key in the order's text: numbers can differ between games) moved by the
  amount or set to it, -100..100, no Extreme doubling; pirates, the Void, Specters and a hostile mod race refused; the
  event graphs' Standing node and the quests' Standing step write it), `/spawn [players] <ship | object>
  [race] [count] [enemy | friendly | neutral | standing] [turrets <item,item>] [at x y z]` (`DebugSpawner`: a ship's number or name as that game's
  traffic, the race (a mod's race too, `Standing.RaceWord`) by default its maker's else pirates, 1..10 side by side, enemy by default; "turrets": turret items on the
  ships' turret mounts, `NpcMountedTurrets`, carried in the order's text after '~'; neutral = the remake's
  `SpawnSpec.alwaysNeutral` / `NpcShip.alwaysNeutral`, neither side whatever the standings until turned or shot, also toward
  the other players (`NetOrbit.HostileToRemote`); a name that is no ship is an assemblies.json object as scenery; "at" = game
  coordinates in that player's orbit, else ahead of them; "named <name>" (to the end or up to "at x y z", at most 32
  characters, cleaned like a chat line; in the order's text after a `|`): a ship's lock-plate name (`SpawnSpec.name`,
  numbered "Name 1", "Name 2"... for several, the others see it through `NetProxy`'s label), an object a HUD marker
  (`Navigation.Kind.Marker` on the model's centre: bracket, name and distance near the crosshair like a landmark, never a
  lock candidate; local to that player's game); the event graph's Spawn node has a Name port for it, empty = none, old
  graphs without the port compile as before), `/ship [players] <ship | own>` (`PlayerHull.Fly` / `Restore`: any
  hull of the debug Ships tab, docked only ownable ones), `/ammo`, `/reveal`, `/peace [players]`, `/cheat [players] <god | ammo |
  cooldown | primary | boost | onehit | locks | shopping | jumps> [on | off]` (`Cheats.Grant`: that player's flag for the session, not
  saved, whatever the session allows), `/mute <players> [minutes]` / `/unmute` (the server drops their chat and whispers;
  never the host's player, only the host / console mutes an admin), `/title [players] <text> [| subtitle] [for <seconds>]`
  ("clear") and `/timer [players] <seconds | m:ss> [label]` ("stop"): `EventScreen`, its own panel over every scene, a big
  title with a subtitle in the upper middle (4 s by default, fades) and a countdown at the top centre (the last 10 s amber),
  cleared when the session ends. `/dialog [players] <speaker> : <text> [| [speaker :] page ...]`: the scene's dialogue window
  (`DialogueView.Latest`, queued behind one already open, `EventScreen.QueueDialog`), each page with its speaker (a page
  without one keeps the last): a story speaker by name (`StoryTable.SpeakerName`; "Keith as Bob" renames it), a race and a
  name ("vossk K'ekki", "terran female Jane": one `AgentGenerator.CreatePortrait` face made on the server, the same for
  everyone and every page), or "player" (the reader: speaker 0's face with their pilot name); %player% in a text or name is
  the reader's name; a page `reward [title]: <rewards>` pays when the dialogue closes (like a single-player mission's
  success page). `/reward [players] <credits | item [amount]> [+ ...] [| title]`: credits and / or up to 8 items into
  the hold, shown in the reward box (`EventScreen.ShowReward`, `Layout::showMissionRewardMessage` /
  `drawMissionRewardMessage` 0xe7684: the title, default 216 "Mission accomplished!", over "+ credits" and each item
  with its shop icon, centred, fades in 2 s, holds until 5 s, fades out until 7 s, sound 36). Typed commands may be 500
  characters (`NetChat.MaxCommandLength`; chat lines stay 160).
- **Chat typing** ends when the field loses the focus (a click elsewhere, the keyboard closed; the draft stays); actions a
  component enables meanwhile go off too (`NetChat.KeepGameKeysOff`), a scene change drops the list (`DropTyping`); the
  windows' own key reads (dialogue, hacking, star map, item info, photo mode) go through `NetChat.Keys`; no mouse
  steering while typing.
