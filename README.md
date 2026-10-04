![Galaxy on Fire 2 Remake main menu](.github/header.webp)

# Galaxy on Fire 2 Remake (Unity)

A remake of the 2010 space game *Galaxy on Fire 2* by FISHLABS in Unity 6, for Windows (also as a UWP app), Linux and
Android. It aims to be a faithful port of the original gameplay, including flight, combat, trading, mining, stations,
the bar and the whole story. It is built on the original assets and on game logic ported from the decompiled game code.
Downloads are on the [releases page](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases); each
release says how to install it.

**Status:** the main campaign, the Valkyrie add-on and the Supernova add-on can be played from start to end. The
Supernova opening and final battle have been rebuilt from the original scripts, but the add-on has not been fully played
through yet. Multiplayer and VR are experimental. A build's version is the date and time of its release (for example
`2026.10.01.1200`), the same on every platform, shown in the main menu.

## Features

- **Story:** the full main campaign with the prologue, the Void and the ending, plus the Valkyrie and Supernova add-ons.
  Dialogue is voiced (English or German), with portraits, radio chatter and cutscenes.
- **Flight:** the original flight model and chase camera. The station orbits are rebuilt exactly like the original,
  with their skies, planets, suns, lens flare and asteroid fields. Travel covers the autopilot, planet jumps,
  fast-forward, the star map, jumpgates, the Khador Drive and the Void's wormhole.
- **Combat:** every weapon, including missiles, beams, EMP, mines, turrets, sentry guns, the Liberator and the other
  special weapons. NPC traffic and AI, capital ships, pirate bases, Specters and the Most Wanted criminals. Combat
  equipment: cloak, emergency system, time extender, repair beams and gamma shields.
- **Economy and stations:** hangars, shops and ship dealers, and the space lounge with bar agents and every
  freelance mission type. Also blueprints, wingmen, medals, the Kaamo Club and save games.
- **Mining:** asteroid mining with the drilling minigame, plus gas clouds, hacking and docking at objects.
- **Multiplayer (experimental):** a shared universe, online through a server browser or a join code, or over a local
  network. Players can host from the game or run a dedicated server. Players see each other in space and in the
  hangars, and can fight each other. Squads share bar missions and their rewards. Everyone shares the NPC traffic, the
  crates, the asteroids and the shop stock. Local and global chat, chat commands, admin tools and scripted event game
  modes (waves of pirates, survival) with on-screen titles, timers, dialogs and rewards.
- **VR (experimental):** PC VR through OpenXR with the `-vr` launch option: a cockpit in flight with the instruments on
  its displays, standing in the hangars and the bars, the menus on a floating screen with a laser pointer.
- **Remake extras:**
  - Arrival and take-off flights in the hangar, animated dialogue text, and the original-style bloom as an option.
  - Pirate events: now and then an orbit holds a pirate outpost or a pirate boss with a bounty (an option).
  - **New Game+:** with a finished game in your saves, start again with its credits, blueprints, medals and ships.
  - The Kaamo Club expansion can be owned from the start of a new game.
  - A full-map overview on the star map, new medals as a short toast instead of a window, tutorial popups as an option
    (off by default; a new game asks).
  - Four difficulties (Easy, Normal, Hard, Extreme) that can be changed during a game, and a choice between the PC
    and Android economies for a new game.
  - Other ships' engines like the player's (an option), photo mode, screenshots, an FPS counter, and every weapon's
    own shot sound (an option; the original only plays the first gun's).
  - Upscaling: FSR 1 and STP everywhere they are supported, NVIDIA DLSS and AMD FSR 2 / 3 / 4 on Windows, MetalFX on
    macOS and iOS.
  - Discord Rich Presence on Windows: your Discord status shows what you are doing in the game.
  - A Dutch translation, and a choice between German and English voices.
  - Debug tools (Options > Gameplay): jump to any story step, cheats, give items, spawn ships and objects.
  - The current story step and station show small at the bottom right, so a screenshot of a bug shows where it happened.
  - Export and import of all save games as one file (Options > Gameplay in the main menu), to move your games to
    another PC or phone. Importing checks the file first and replaces every existing save.
- **Controls and screens:** touch, tilt, keyboard and mouse (with the PC version's clickable on-screen buttons when
  mouse steering is off), and controllers (shown with Xbox buttons), all rebindable.
  Gyro steering with a DualSense, DualShock 4 or Switch Pro Controller on Windows. Haptic feedback on controllers and
  Android phones (hits, collisions, explosions, weapons, boost, jumps and mining) with an intensity setting. Landscape
  screens from 4:3 up to 32:9, phones included.

## Getting started

1. Install **Unity 6000.7.0b2** (Unity 6.7) with Unity Hub. Add Android Build Support if you want phone builds.
2. Clone the repository with Git LFS installed. The assets are about 2.1 GB in LFS.
3. Open the project in Unity and open `Assets/Scenes/MainMenu.unity`, then press Play.

The scenes are `MainMenu` (build index 0), `Space` (the flight level) and `Station` (docked). The **GoF2** menu in the
Editor holds the asset build tools. The generated assets are already in the repository.

### Building

Always **switch the active build profile** (File > Build Profiles > Switch Profile) before building another platform.
URP chooses which shader variants to keep from the active platform. An Android build made while Windows was active
renders black, so the editor script `BuildTargetGuard` refuses such builds. `BuildVersionStamp` gives every build its
date and time as its version; for a release, set the Editor process's `GOF2_BUILD_VERSION` environment variable so all
platforms get the same one.

### Multiplayer

Multiplayer is experimental. Everyone in a session shares one universe: you see each other in space and in the hangars,
form squads, and fly bar missions together. Every player starts a fresh free-play game docked at Var Hastra, and
sessions don't touch your single-player saves. Only the **exact same game version** can play together, so everyone
needs the same release.

#### Joining a game

1. In the main menu, open **Multiplayer** and type your **pilot name** at the top.
2. The **server browser** lists the public games with their name, host, players (for example `4 / 16`) and version.
   It refreshes every 5 seconds. Click a game to join it.
   - A game tagged **PASSWORD** needs its password: type it in the **Password** field under the list first.
   - A game on another version shows "needs <version>" and can't be joined.
3. To join a game that isn't listed, type its **join code** (six letters or digits, like `QKJH9N`) in the field under
   the list and press **Join**. On a local network you can type the host's address instead (`192.168.1.20`, or
   `192.168.1.20:7778` for another port).

#### Hosting from the game

On the **Host a game** card, pick a mode:

- **Public:** online, and listed in the server browser under the **Game name** you choose.
- **Invite only:** online, but not listed. Friends join with your join code.
- **Local network:** for players on the same network (or a VPN like Hamachi or ZeroTier). The card lists your
  addresses (tap one to copy it) and the port, 7777 by default.

Every mode can have a **password** and a **Max players** limit (2 to 100, you included). **Debug menu** (Off by
default) decides whether the players may use the Debug menu (cheats, items, spawns) in your session. Press **Host**. In an online
game the join code is copied to your clipboard and shown under the station's system information, with a Copy button.
Online play goes through Unity Relay: no port forwarding, but it needs an internet connection. With a player host,
all traffic goes through the host's connection, so for big sessions a dedicated server is better.

#### Running a dedicated server

A dedicated server hosts a session without anyone playing on that machine. It uses the normal Windows or Linux
download; no extra files are needed.

**Windows**

1. Open `Start Dedicated Server.bat` in the game folder with a text editor (Notepad), and set:
   - `NAME`: the game's name in the server browser.
   - `PASSWORD`: leave it empty for none.
   - `MAXPLAYERS`: the player limit, at most 100.
   - `ALLOWDEBUG`: `1` lets the players use the Debug menu (cheats, items, spawns); `0` (the default) turns it off.
2. Save the file and double-click it. A console window opens; the game itself runs without a window and without sound.
3. The console shows the **join code**, and the game appears in everyone's server browser.

**Linux**

1. Edit `NAME`, `PASSWORD`, `MAXPLAYERS` and `ALLOWDEBUG` at the top of `start-server.sh` in the game folder.
2. Run it in a terminal: `sh start-server.sh`. The terminal shows the join code and the log.

**The console**

The console shows who joins and leaves, where each player is, and the chat. Only the server can run commands; players
can't. Type one and press Enter:

| Command | What it does |
|---|---|
| `help` | Lists the commands. |
| `status` | The join code (or port), uptime, players, world seed, whether the Debug menu is allowed. |
| `list` | The players: client id, name, where they are, ship, squad. |
| `say <text>` | A chat line to everyone, from "Server". |
| `kick <id or name> [reason]` | Drops a player; they see the reason. |
| `stop` | Tells the players and shuts the server down. Ctrl+C or closing the window does the same. |

**Starting it by hand**

The launchers only start the game with these options, which you can also use yourself:

```
GoF2Remake.exe -batchmode -nographics -server -relay -name "My universe" -password secret -maxplayers 32
./GoF2Remake.x86_64 -batchmode -nographics -server -relay -name "My universe" -logFile -
```

| Option | Meaning |
|---|---|
| `-batchmode -nographics` | No window, no rendering, no sound. |
| `-server` | Run as a dedicated server. |
| `-relay` | Host online with a join code (Unity Relay). Without it, players join on the server machine's address. |
| `-name "..."` | The name in the server browser (online). |
| `-unlisted` | Keep the game out of the server browser; players join with the join code. |
| `-password X` | Players need this password to join. |
| `-maxplayers N` | The player limit, 2 to 100 (default 16). |
| `-allowdebug` | The players may use the Debug menu (cheats, items, spawns). Off without it. |
| `-port N` | The port for local network play (default 7777, UDP). |
| `-fps N` | The server's frame rate (default 60). |

Good to know:

- A local network server (without `-relay`) needs UDP port 7777 open in the firewall for the other players.
- The server keeps the shared world: the shop stock, squads, missions and chat. Each orbit's NPCs are run by the first
  player who arrives there, so the server itself needs very little CPU.
- Players on a different game version are turned away with a message saying which version the server runs.

The console also runs every chat command below without the "/" (`list` and `say` are `players` and `g`). Tab completes
command names, Up / Down go through the history.

#### Chat commands

Open the chat with **B** and type a line starting with `/`. Typing `/` lists the commands; **Tab** completes commands
and player names. Everyone can use:

| Command | What it does |
|---|---|
| `/help` | The commands you can use. |
| `/players` | Who is playing, where, in which ship and squad. |
| `/g <text>`, `/l <text>` | A line to the Global or Local channel. |
| `/w <player> <text>` | A private message. |
| `/invite <player>`, `/leave` | Invite a pilot docked at your station to your squad, or leave it. |
| `/netstats` | Network statistics (ping, packet loss, data rates). |
| `/pos` | Your orbit and position, in the coordinates `/tp` takes. |

**Admin commands** are for the host's own player, the dedicated server's console, and the players the host makes
admin with `/admin <player>` (`/unadmin` takes it back). They work on players by name, by client id, or with a
selector: `@a` everyone, `@s` yourself, `@p` the nearest other player, `@r` a random one, and `@alive`, `@space`,
`@docked`, `@dead`, `@survivors` (in an event: never destroyed since it began). In the chat, leaving out the players
means yourself.

| Command | What it does |
|---|---|
| `/kick <player> [reason]` | Removes a player; they see the reason. |
| `/mute <players> [minutes]`, `/unmute` | Silences a player's chat. |
| `/tp [players] <player \| station [x y z \| dock]>`, `/tphere <player>` | Teleports to a player, an orbit (by number or name) or into a station's hangar. |
| `/kill`, `/heal`, `/ammo`, `/reveal`, `/peace [players]` | Destroys, repairs, refills secondaries, reveals the map, resets the standings. |
| `/give [players] <item> [amount] [mount]` | Items into the hold (docked, `mount` also mounts them). |
| `/credits [players] <amount>` | Gives (or with a minus, takes) credits. |
| `/spawn [players] <ship \| object> [race] [count] [enemy \| friendly \| neutral] [named <name>] [at x y z]` | Ships (by number or name) or scenery near the players; `named` puts a name on them in the HUD. |
| `/ship [players] <ship \| own>` | Swaps the players' ship (any hull of the debug tools), or back to their own. |
| `/cheat [players] <god \| ammo \| cooldown \| boost \| onehit \| locks \| shopping \| jumps> [on \| off]` | A cheat for those players, this session only. |
| `/title [players] <text> [\| subtitle] [for <seconds>]` | A big title on their screen (`clear` removes it). |
| `/timer [players] <seconds \| m:ss> [label]` | A countdown at the top of their screen (`stop` removes it). |
| `/dialog [players] <speaker> : <text> [\| [speaker :] next page ...]` | A conversation window. The speaker is a story character ("Keith", "Keith as Bob"), a race and a name ("vossk K'ekki") or `player`; `%player%` is the reader's name. A page `reward: <rewards>` pays when it closes. |
| `/reward [players] <credits \| item [amount]> [+ more] [\| title]` | Credits and items, shown in the mission reward box. |
| `/event <name \| stop \| list>` | Starts or stops an event script. |

#### Events

An event is a small text script the server runs, for game modes like waves of pirates. Two come with the game:
`/event waves` (more pirates every wave, the survivors paid per wave) and `/event survival`. To write your own, put a
`<name>.txt` file in the `Events` folder next to the game (or the dedicated server) and start it with `/event <name>`.
A script is a list of commands without the "/", plus `wait <seconds>`, `wait until <condition> [timeout <seconds>]`,
`if` / `else` / `end`, `while` / `end`, `repeat <n> [as <variable>]` / `end`, `set <variable> = <value>`,
`score <kills | time>`, `winner` and `stop`. `{...}` puts a value into a command. Conditions can use `enemies` (the
event's living enemy ships), `ships`, `players`, `inspace`, `docked`, `dead` and `time`. An event ends by itself when
nobody is left alive in its orbit, and announces the winner (the most kills, or the longest time alive).

```
title @a Wave 1 | Incoming for 4
spawn @a Hiro 4 pirate enemy
wait until enemies == 0 or inspace == 0
reward @survivors 1000 | Wave 1 survived
winner
```

## VR (experimental)

Start the Windows build with `-vr` (for example a shortcut to `GoF2Remake.exe -vr`) with a PC VR headset connected and an
OpenXR runtime active (SteamVR, Meta Quest Link, Windows Mixed Reality). Without a headset the game starts normally.

- **Flight:** you sit in a cockpit. The shield, hull and cargo readouts are on its displays, the radar in the middle of
  the dashboard, the rest of the HUD on the canopy. Cutscenes keep the horizon level and fade between shots.
- **Stations:** you stand in the hangar beside your ship (grab it with the grip and pull to turn it) and in the bar
  (point at a visitor and pull the trigger to talk).
- **Menus** appear on a floating screen; point with the right controller and pull the trigger.
- **Controls:** the controllers work as a gamepad (sticks, triggers, A / B / X / Y, the grips as LB / RB). With Options >
  Controls > "VR flight: grab the stick and throttle" you fly with the cockpit's side-stick (right hand) and throttle
  lever (left hand) instead.

The cockpit is the same for every ship for now. VR has only been tested without a headset so far.

## Controls (keyboard)

| Key | Action |
|---|---|
| Arrow keys | Steer |
| W | Boost |
| S | Brake (the engines stop while held) |
| `]` / `/` or the mouse wheel | Throttle |
| Space / left mouse | Primary weapons |
| R / right mouse | Secondary weapon |
| G | Switch secondary weapon |
| A / D | Strafe left / right |
| 1 / 3 | Roll left / right |
| 2 | Level out |
| F / Enter | Action (dock, autopilot, mine, jump) |
| Q | Autopilot menu |
| E | Actions menu (secondary weapons, wingmen, cloak, Khador Drive) |
| Tab | Fast-forward |
| T | Camera / turret view |
| V / K / C / X | Wingmen / Khador Drive / cloak / time extender |
| M or middle mouse | Toggle mouse steering |
| B | Chat (multiplayer) |
| F12 | Screenshot |
| Esc | Pause |

Every flight control can be rebound in Options > Key bindings (two keyboard / mouse keys and a controller button
each). Controllers and touch are fully supported. The in-game hints show the buttons for whichever input you last used.

**Controllers on Linux:** Xbox, PlayStation and Switch Pro controllers work directly. On a Steam Deck in desktop mode,
or with a controller the game doesn't recognise, add the game to Steam (Add a Non-Steam Game) and start it from there:
Steam Input then presents the controls as an Xbox controller.

## Project layout

```
Assets/Scripts/Runtime/   game code (flight, world, NPCs, UI, multiplayer)
Assets/Scripts/Editor/    import settings, asset and prefab builders, menu items
Assets/Resources/         game data (JSON), assembled prefabs, sky / HUD / combat assets
Assets/UI/                UI Toolkit screens (UXML / USS)
Reference/                decompiled original code, research notes and conversion tools
```

`CLAUDE.md` is the full technical documentation. It covers every system, the original functions it is based on and
the choices the remake made.

## Credits

**Galaxy on Fire 2 Remake** by JoppieToppie.

The remake is built on the **FULL HD version and modifications made by KiritoJPK**, thanks to the Galaxy on Fire 2™ and
4PDA community: <https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ and
ABYSS® are registered trademarks of FISHLABS Entertainment GmbH. All rights reserved. This is an unofficial fan project,
not affiliated with or endorsed by FISHLABS or Deep Silver.

Fonts: the original game's interface typeface, and Inter (SIL Open Font License). Controller gyro: [JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary)
by Julian Smart (MIT License). Built with Unity, the Universal Render Pipeline, Netcode for GameObjects and OpenXR.
