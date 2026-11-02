# Running a dedicated server

A dedicated server hosts a Galaxy on Fire 2 Unity Remake multiplayer session without anyone playing on that machine.
It is the normal Windows or Linux build of the game, started with `-server`. No extra download is needed. There are also server-only builds for Linux and Windows without the game's graphics and sound (see
[Linux server-only build](#linux-server-only-build) and [Windows server-only build](#windows-server-only-build)).

This guide covers setting the server up, its command-line options, the settings you can change while it runs, the
server console, the files it keeps, and running it as a Linux service. For what players can do in a session (factions,
arenas, distress calls and their chat commands), see [Playing together](docs/MULTIPLAYER.md).

## Contents

- [Quick start](#quick-start)
- [Linux server-only build](#linux-server-only-build)
- [Windows server-only build](#windows-server-only-build)
- [Docker](#docker)
- [Online or local network](#online-or-local-network)
- [Command-line options](#command-line-options)
- [Settings you can change while the server runs](#settings-you-can-change-while-the-server-runs)
- [Message of the day](#message-of-the-day)
- [Becoming the server's admin](#becoming-the-servers-admin)
- [The server console](#the-server-console)
- [The web admin](#the-web-admin)
- [Files the server keeps](#files-the-server-keeps)
- [The money log](#the-money-log)
- [Running it as a Linux service](#running-it-as-a-linux-service)
- [When the connection drops](#when-the-connection-drops)
- [Updating the server](#updating-the-server)
- [Troubleshooting](#troubleshooting)

## Quick start

### Windows

1. Open `Start Dedicated Server.bat` in the game folder with a text editor (Notepad) and set:
   - `NAME`: the game's name in the server browser.
   - `PASSWORD`: leave it empty for no password.
   - `MAXPLAYERS`: the player limit, at most 100.
   - `ALLOWDEBUG`: `1` lets players use the Debug menu (cheats, items, spawns); `0` (the default) turns it off.
   - `FLIGHT`: the flight model: `player` (the default: each player picks their own in Options), or `gof2` (the game's) / `es2` (EVERSPACE 2 style) for everyone.
2. Save the file and double-click it. A console window opens. The game itself runs without a window and without sound.
3. The console shows the **join code**, and the game appears in every player's server browser.

### Linux

1. Edit `NAME`, `PASSWORD`, `MAXPLAYERS` and `ALLOWDEBUG` at the top of `start-server.sh` in the game folder.
2. Run it in a terminal:

   ```
   sh start-server.sh
   ```

   The terminal shows the join code, the log and the console.

To keep the server running after you log out, and to restart it when it stops, run it as a service (see
[Running it as a Linux service](#running-it-as-a-linux-service)).

### Starting it by hand

The launchers only start the game with options. You can start it yourself:

```
GoF2Remake.exe -batchmode -nographics -server -relay -name "My universe" -password secret -maxplayers 32
./GoF2Remake.x86_64 -batchmode -nographics -server -relay -name "My universe" -logFile -
```

`-batchmode -nographics` runs it with no window, rendering or sound. On Linux, `-logFile -` prints the log to the
terminal.

## Linux server-only build

A smaller Linux build that can only be a server: Unity's Dedicated Server build with the game's textures, shaders and
sounds left out (about 330 MB unpacked, a 126 MB zip, instead of 3.3 GB). It runs the same server code as the full game's `-server` mode, keeps the same files and serves the
same game builds (players see no difference).

Releases have it ready to download: `GoF2Remake-LinuxServer-<version>.zip` on the release page, next to the game's
files. Unzip it on the server machine (`unzip GoF2Remake-LinuxServer-<version>.zip`, the files keep their execute
permission) and run `GoF2Server/start-server.sh`.

Making it yourself (in the Unity Editor, once the Unity Hub module **Linux Dedicated Server Build Support** is installed for the
Editor version): **GoF2 > Build > Linux Dedicated Server**. It builds into `Build/LinuxServer/` with
`GoF2Server.x86_64` and a `start-server.sh` beside it; the Editor switches to the Linux server target for the build and
back afterwards. Copy the folder to the server machine. **GoF2 > Build > Linux Dedicated Server (Release Zip)** builds
the same and zips it as `Build/Release/GoF2Remake-LinuxServer-<version>.zip`, the file a release carries.

Running it is as above: edit and run `start-server.sh`, or start `./GoF2Server.x86_64` with the same options. It starts
as a server without `-server`; `-batchmode -nographics` are implied.

## Windows server-only build

The same server-only build for Windows (64-bit): no textures, shaders or sounds, the same server code and options.

Releases have it ready to download: `GoF2Remake-WindowsServer-<version>.zip` on the release page. Unzip it, edit the
settings at the top of `GoF2Server\Start Dedicated Server.bat` (right-click, Edit) and double-click it. The server opens
its own console window, which shows who joins and the chat and takes the console commands. You can also start
`GoF2Server.exe` with the options below; it starts as a server without `-server`.

Making it yourself (once the Unity Hub module **Windows Dedicated Server Build Support** is installed for the Editor
version): **GoF2 > Build > Windows Dedicated Server** builds into `Build/WindowsServer/`, and **GoF2 > Build > Windows
Dedicated Server (Release Zip)** zips it as `Build/Release/GoF2Remake-WindowsServer-<version>.zip`.

## Docker

Every release is also a Docker image of the Linux server-only build: `ghcr.io/joppietoppie/gof2-server`, tagged with
the release and `latest`. It is the release's own `GoF2Remake-LinuxServer` zip (the same build as the release's game
files), or, for a release without one, a build of the release's commit, which the release then gets as that zip. With Docker Compose, copy [`docker/compose.yaml`](docker/compose.yaml) and start it in its
folder:

```
docker compose up -d
```

The options are the command line's: edit `command` in the file. As it is, the server is online (`-relay`) and the web
admin answers on this machine's port 8080. The profiles and the server's other files are in the Docker volume `data`
(`/data` in the container, its `-profiledir`). The log and the admin token:

```
docker compose logs -f
docker compose logs | grep "admin token"
```

There is no console to type into: use the in-game Admin tab or the web admin. `docker compose stop` stops it like
`stop`. To update: `docker compose pull`, then `docker compose up -d`.

Building the image yourself from a checkout needs Docker and a Unity account (`UNITY_EMAIL` and `UNITY_PASSWORD`; its
Personal licence is used for the build and returned). The first build imports every asset.

```
docker/build-server.sh
docker compose -f docker/compose.yaml -f docker/compose.dev.yaml up --build
```

That one runs on the local network (no `-relay`) and isn't listed.

## Online or local network

- **Online** (`-relay`, what the launchers use): the server goes through Unity Relay. Players join from the server
  browser, or with the join code shown in the console. You need no port forwarding, but the server needs an internet
  connection, including outgoing HTTPS (443) and UDP.
- **Local network** (no `-relay`): players join on the server machine's address, for example `192.168.1.20` or
  `192.168.1.20:7778`. The console lists the machine's addresses. Open the UDP port (7777 by default) in the firewall.
  To reach the server from the internet this way, forward that UDP port on your router.

Players on a different game version are turned away with a message saying which version the server runs.

## Command-line options

| Option | Meaning |
|---|---|
| `-batchmode -nographics` | No window, no rendering, no sound. |
| `-server` | Run as a dedicated server. |
| `-relay` | Host online with a join code (Unity Relay). Without it, players join on the machine's address. |
| `-name "..."` | The name in the server browser (online). |
| `-unlisted` | Keep the game out of the server browser; players join with the join code. |
| `-password X` | Players need this password to join. |
| `-maxplayers N` | The player limit, 2 to 100 (default 16). |
| `-allowdebug` | Players may use the Debug menu (cheats, items, spawns). Off without it. |
| `-freepvp` | Players may fight each other anywhere, not only in arena matches and faction sieges. |
| `-flightmodel M` | The flight model: `player` (also `any` or `unrestricted`; the default: each player's own Options choice), or `gof2` (the game's own) / `es2` (EVERSPACE 2 style) for everyone. One model for everyone keeps PvP fair. Admins change it in the game with `/flightmodel`. |
| **Gameplay rules** | The game's Gameplay options that change how it plays are one rule for everyone on the server. Each flag turns one the other way from its default. |
| `-cloakhides` | NPC ships lose track of a cloaked player (they fly to where they last saw them, then back to their patrol). Off by default: as in the original, they keep chasing a cloaked player without firing. |
| `-capitalships` | Capital ship enhancements (escorts, stronger turrets, a killable carrier and Vossk battleship, fleet battles). Off by default. |
| `-informeroriginal` | Informer missions use the original's rule (any other ship destroyed before docking fails it). Off by default. |
| `-nopirateevents` | No pirate outposts and bosses (on by default). |
| `-nokaamostacking` | The Kaamo Club's upgrades only once each, as in the original (stackable by default). |
| `-kaamomaxlevel N` | With stacking: how many times each Kaamo Club upgrade can be fitted to one ship, 2 to 20 (default 5). Profiles above it are cut back when they sign in. |
| `-nokaamoequipment` | Ships stored in the Kaamo Club hand their equipment over, as in the original (they keep it by default). |
| `-nocloakbay` | A cloak mounted on the Specter or the Scimitar takes an equipment slot, as in the original (by default it goes into their integrated cloak's bay). |
| `-noempcloak` | EMP hits don't break cloaks, as in the original (by default an EMP hit drops a cloak at once). |
| `-smarttargeting` | NPC fighters take the nearest hostile ship and spread out over their targets (by default they go for the player first, as in the original). |
| `-shipcollision MODE` | Ship collision: `exact` (shots hit every ship and object on its real shape, and players' ships meet each other on it), `mixed` (the default: fighters and players' ships are hit on a box and meet as spheres, as in the original; freighters, capital ships, stations and objects keep their real shape) or `original` (the original's boxes and spheres for everything). The old `-precisecollision` still means `exact`. |
| `-port N` | The port for local network play (default 7777, UDP). |
| `-fps N` | The server's frame rate (default 60). |
| `-noconsole` | Windows: no console window of its own. |
| `-web [port]` | Start the [web admin](#the-web-admin) (default port 8080; `-webport N` does the same). |
| `-webbind ADDRESS` | What the web admin listens on (default `127.0.0.1`, this machine only; `0.0.0.0` for every network adapter). |
| **Player profiles** | |
| `-noprofiles` | Don't keep player profiles. Every session starts fresh, and factions, moderation and the leaderboard are off. |
| `-maxearn N` | Without `-allowdebug`: how much a profile's worth may grow per minute online (default 1 000 000). |
| `-profiledir PATH` | Where the profiles and the server's other files are stored (default: see [Files](#files-the-server-keeps)). |
| `-admintoken X` | The token for `/claimadmin` (default: a random one, see [Becoming the server's admin](#becoming-the-servers-admin)). |
| **Factions** | |
| `-claimcost N` | What a faction pays from its bank to claim a station (default 500 000). |
| `-maxclaims N` | Stations per faction (default 3). |
| `-claimdays N` | Days without a member docking before a claim is lost (default 14). |
| `-siegecost N` | A siege's stake, paid from the attacking faction's bank (default 250 000): the winner's bank gets it (the defenders' when nobody fought within the 2 days and 4 extra days); back to the attacker when the claim is given up or the defenders disband. |
| `-toll N` or `-toll N%` | The toll other factions' pilots pay at a faction's station: N credits, or N % (0-100) of the pilot's credits (default 10 000, 0 = none). |

The environment variable `GOF2_ADMIN_TOKEN` can replace `-admintoken`.

## Settings you can change while the server runs

These settings can be changed without a restart. Admins change them in the game, in the station's **Multiplayer**
window, **Admin** tab, **Server settings**. You can also type `/set <key> <value>` in the game's chat, or
`set <key> <value>` in the server console. `/settings` (or `settings` in the console) lists them.

| Key | Setting | Notes |
|---|---|---|
| `name` | The name in the server browser | Used at the next listing (after a restart). |
| `password` | The password (`-` = none) | Applies to new players at once. The browser's password tag updates after a restart. Never shown, only "set" or "none". |
| `maxplayers` | The player limit | Lowering it works at once. Online, raising it above the start's limit needs a restart. |
| `allowdebug` | Players may use the Debug menu (`on` / `off`) | At once. |
| `freepvp` | Players may fight anywhere (`on` / `off`) | At once. |
| `flightmodel` | The flight model above (`gof2`, `es2` or `player`; also `/flightmodel`) | At once. |
| `shipcollision` | The ship collision mode above (`exact`, `mixed` or `original`) | At once. |
| `cloakhides`, `pirateevents`, `capitalships`, `informeroriginal`, `kaamostacking`, `kaamoequipment`, `cloakbay`, `empcloak`, `smarttargeting` | The gameplay rules above (`on` / `off`; `on` = the option on) | At once (new orbits get the spawns that depend on them). |
| `kaamomaxlevel` | The Kaamo Club upgrade limit above (2-20) | At once for new upgrades; profiles above a lowered limit are cut back at their next sign-in or upload. |
| `maxearn` | Worth a profile may gain per minute | At once. |
| `claimcost`, `maxclaims`, `claimdays`, `siegecost`, `toll` | The faction settings above | At once. |

A change is saved in `server_settings.json` and kept after a restart. **An option given on the command line wins at
every start.** The launchers always pass `-name` and `-maxplayers`, so a change of those in the game only lasts until
the next restart, unless you also change the launcher. The Admin tab marks these settings "set by the launcher".

## Message of the day

Players see the message of the day (MOTD) when they join: a window with the server's name and the text in a monospace
font, so ASCII art lines up. A player sees it again only when it changes, or with `/motd` in the chat.

The text is the file `motd.txt` in the server's data folder (see [Files the server keeps](#files-the-server-keeps)). Edit
it with any text editor (UTF-8) and type `/motd reload` in the game or `motd reload` in the console. Every line and
space is kept; tabs become 4 spaces; at most 60 lines and 4000 bytes. These placeholders are filled in for each player:

| Placeholder | Becomes |
|---|---|
| `%player%` | The player's pilot name |
| `%players%` | How many players are online |
| `%server%` | The server's name |

For a short message there is no need to edit the file: `/motd set Welcome, %player%!\nBe nice.` (`\n` starts a new
line) writes it, `/motd clear` empties it. Admins and the console only. A hosted game (not a dedicated server) reads
`motd.txt` from the game's own data folder.

## Becoming the server's admin

You don't need the console for this. At every start the server writes a line like this to its log:

```
Server: admin token Ab3dE9fGh1Jk: type /claimadmin Ab3dE9fGh1Jk in the game's chat to become this server's master admin.
```

1. Read the token from the log or the console. On a Linux service: `journalctl -u gof2 | grep "admin token"`. The
   token is also in `admin_token.txt` in the server's data folder, and the console shows it with `token`.
2. Join the server and type `/claimadmin <token>` in the chat. Or open the station's **Multiplayer** window,
   **Profile** tab, **Claim this server**.
3. You are the **master admin**. The Multiplayer window now has an **Admin** tab.

To get a new token, delete `admin_token.txt` and restart the server, or start it with `-admintoken X`. Anyone who knows
the token can make themselves master admin, so keep it private. Wrong tries are logged, and only one try every 5
seconds is checked.

### Roles

| Role | Can |
|---|---|
| **master** | Everything, plus making and removing admins (`/admin`, `/unadmin`) and deleting profiles (`/deleteprofile`). |
| **admin** | Everything an op can, plus permanent bans (`/ban`), making and removing ops (`/op`, `/deop`), announcements (`/say`), ending factions (`/disband`), the server settings (`/set`). |
| **op** | Kicks with a cooldown (`/kick <pilot> [minutes] [reason]`), temporary bans of up to 24 hours (`/tempban`), lifting bans (`/unban`), the ban list (`/bans`). |

Nobody can act on someone of their own rank or higher, or give a role as high as their own. The server console can do
everything, and also makes masters (`master` / `unmaster`).

A ban covers the player's profile and every device it was used on, so a banned player can't come back with a new
profile from the same device.

## The server console

The console shows who joins and leaves, where each player is, and the chat. Type a command and press Enter. Only the
server can use the console; players use the chat commands above.

| Command | What it does |
|---|---|
| `help` | Lists the commands. |
| `status` | The join code (or port), uptime, players, world seed, whether the Debug menu is allowed. |
| `list` | The players: client id, name, where they are, ship, squad. |
| `say <text>` | A chat line to everyone, from "Server". |
| `kick <id or name> [minutes] [reason]` | Drops a player; they see the reason. With profiles they can't rejoin for the minutes (default 5). |
| `tempban`, `ban`, `unban`, `bans` | Bans (see [Roles](#roles)). |
| `op`, `deop`, `admin`, `unadmin`, `master`, `unmaster`, `staff` | Roles. |
| `token` | The admin token for `/claimadmin`. |
| `settings`, `set <key> <value>` | The settings that change while the server runs. |
| `profiles` | The player profiles: id, name, devices, worth, who is online. |
| `profile info <id or name>` | One profile: role, devices, worth, play time, last seen, online, blocked. |
| `profile delete <id or name> [force]` | Deletes a profile; its file is kept as `.bak`. Online it is refused unless `force`: its devices are dropped first ("Your profile on this server was deleted."). |
| `profile block <id or name> [reason]` | Bans the profile and every device it signed in from, for good (the same as `ban`); an online player is dropped. A new profile from those devices is turned away too. |
| `profile unblock <id or name>` | Lifts the block (`unban`). `profile blocked` lists the bans (`bans`). |
| `profile restore <id>` | Brings back a profile pruned after 30 days unused (from `Pruned/`). |
| `factions`, `faction disband <TAG>`, `sieges` | The factions; end one; the sieges. |
| `arenas` | The arena matches and queues. |
| `stop` | Tells the players and shuts the server down. Ctrl+C or closing the window does the same. |

A server running as a Linux service has no console you can type into. Use the in-game Admin tab, or the web admin's
console (below): its `/api/command` runs these same commands, so a control panel can call `profile delete` /
`profile block` there.

## The web admin

A browser page for running the server: its status, the players online (kick, bans, roles), the bans, the profiles, the
factions, the settings, the console and the live log. Start the server with `-web` (port 8080) or `-web 9000`. In the
launchers, set `WEBPORT`.

Open `http://127.0.0.1:8080/` on the server machine. There are two ways to log in, the same as becoming an admin in the
game:

- **Admin token**: the token `/claimadmin` takes (see [Becoming the server's admin](#becoming-the-servers-admin)). It
  gives everything the console can do, including `stop`.
- **Login code**: an op, admin or master types `/web` in the game's chat and gets a one-time code. It works once, within
  5 minutes. The page then has that pilot's role: ops get kicks and temporary bans, admins also bans, roles, settings and
  the log, masters also admins and profile deletion. If the pilot is demoted or banned, their session ends.

A login lasts 12 hours (2 hours without using the page). Restarting the server logs everyone out. After 5 wrong tries,
an address waits 5 minutes. Every command run from the page is logged with who ran it and from which address.

> **Warning:** the web admin is plain HTTP. By default it only listens on the server machine itself. To use it from
> another computer, use an SSH tunnel (`ssh -L 8080:127.0.0.1:8080 user@server`), or put it behind a reverse proxy with
> TLS (nginx, Caddy) and start it with `-webbind 127.0.0.1`. Don't open its port to the internet: the admin token would
> cross the network unencrypted.

The page loads its styles (Tailwind CSS) from `cdn.jsdelivr.net`. Without internet access in the browser it still
works, but looks plain.

## Files the server keeps

The server's data folder is `ServerProfiles` in the game's data folder, or the folder given with `-profiledir`:

- Linux: `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/ServerProfiles`
- Windows: `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\ServerProfiles`

| File | Contents |
|---|---|
| `accounts.json` | Every profile: id, name, devices (token hashes only), role, arena statistics, squad. |
| `<id>.json` | One player's progress: credits, ship, equipment, cargo, Kaamo Club. |
| `Pruned/<id>.json`, `<id>.account.json` | Profiles nobody signed in to for 30 days, with their `accounts.json` entry (the newest 50 are kept; `profile restore <id>`). |
| `factions.json` | The factions, their banks, claims and sieges. |
| `bans.json` | The bans. |
| `server_settings.json` | The settings changed while the server ran. |
| `news.json` | The sector news on the stations' tickers (the last 40 items, at most 3 days old). |
| `admin_token.txt` | The token for `/claimadmin`. |
| `motd.txt` | The message of the day (you write it; see [Message of the day](#message-of-the-day)). |

Every file is written through a temporary file, and the previous version is kept as `.bak`. To back the server up,
copy the whole folder while the server is stopped.

There is no limit on the number of profiles. A profile nobody has signed in to for 30 days is pruned at the server's
start and once an hour, except one that is online or belongs to staff (ops, admins, the master admin). Its file moves to
`Pruned/`, where the newest 50 are kept, with its entry from `accounts.json`. The console's `profile restore <id>`
brings one back: the player's devices sign in to it again (it has left its faction).

## The money log

Each player's game runs its own economy, so the server only sees what the games tell it. To help spot a profile that
grows faster than play allows, the server writes every money event it sees to its log with the prefix
`Server: audit`. List them with `grep "Server: audit"` on the log file, or `journalctl -u gof2 | grep audit` for the
Linux service.

- **Uploads.** A profile's upload whose worth grew by 100 000 or more since its last accepted upload gets a line. A
  refused upload always gets one. The line gives the worth before and after, the minutes between, the growth per minute
  and how each part changed: credits, ship, equipment, cargo, Kaamo Club and parked ship. It also names a ship change
  and the five items that grew most. Growth of 150 000 a minute or more, or a refusal, is logged as a warning.
- **Shop trades.** Each player's buys and sells of one item at one station are added up. The total is logged once they
  stop for 8 seconds, for example `Sleek (c716f6fcf5b65ebd) sold 120 x Gold (14) at Festus for 72 000 (600 each)`. A unit
  price outside the item's price range is a warning (`PRICE OUTSIDE`): no station asks that price.
- **Faction banks.** Every deposit, withdrawal and toll, with the bank's balance after it. Claims and sieges were
  logged before.

A profile that shows big credit growth in its uploads but no shop trades, missions or trades with other players is
worth a closer look (`profile info <name>`, its file in `ServerProfiles/`). The log changes nothing the server accepts:
`-maxearn` is still the limit.

## Running it as a Linux service

A systemd service starts the server with the machine, restarts it if it stops, and keeps its log in the journal.

1. Create `/etc/systemd/system/gof2.service`. Replace the user and the folder with your own:

   ```ini
   [Unit]
   Description=Galaxy on Fire 2 dedicated server
   After=network-online.target
   Wants=network-online.target

   [Service]
   User=gof2
   WorkingDirectory=/opt/gof2
   ExecStart=/bin/sh start-server.sh -noconsole
   Restart=always
   RestartSec=15

   [Install]
   WantedBy=multi-user.target
   ```

2. Turn it on:

   ```
   sudo systemctl daemon-reload
   sudo systemctl enable --now gof2
   ```

3. Read the log, live:

   ```
   journalctl -u gof2 -f
   ```

   Find the admin token:

   ```
   journalctl -u gof2 | grep "admin token"
   ```

The service has no console you can type into. Manage the server from the game's Admin tab.

## When the connection drops

When the internet connection drops (for example, a router restart), the players lose the server. The server then
starts its session again by itself. It tries after 5, 10, 20 and 40 seconds, then every minute, until the connection
is back. Online, it gets a new join code and is listed again. The players join again from the server browser, and
their profiles bring their progress back (to the last save, at most one minute old). An online server started without
internet keeps trying the same way.

`Restart=always` in the service above also covers the rare case where the server process ends.

## Updating the server

Players can only join a server running the same game version. To update:

1. Stop the server (`stop`, or `sudo systemctl stop gof2`).
2. Replace the game files with the new version. The data folder (profiles, factions, bans, settings) is separate and is
   kept.
3. Start the server again.

## Troubleshooting

| Problem | What to check |
|---|---|
| The server isn't in the browser | Started with `-relay` and without `-unlisted`? Does it have internet, including outgoing HTTPS (443) and UDP? A new game takes a few seconds to appear. |
| "This game runs version X" | The player's game and the server are different versions. Update both. |
| Local network: nobody can connect | Open the UDP port (default 7777) in the server's firewall. From outside the network, forward it on the router. |
| The process ends right after starting | Read the log: `journalctl -u gof2 -n 100`, or `Player.log` in the game's data folder. A local server stops when its port is in use; try another `-port`. |
| A player's progress isn't saved | Without `-allowdebug` the server turns away progress that grows faster than `maxearn`; the log says "upload not saved" (once per profile every 10 minutes, with how many were refused since), and the ops, admins and masters online get a chat notice as often. |
| "signed in more than 8 times in 10 minutes" | A player (or device) in a reconnect loop: the server turns them away for 10 minutes ("You reconnected too often"). Their joins and leaves don't show in the chat either: a join is told after 15 s in the game, and not when the player left under 2 minutes before. |
| "Kaamo upgrade level(s) ... removed" | A profile had more levels of one Kaamo upgrade on a hull than the server's limit (`kaamomaxlevel`, default 5; builds from 2026-10-08 had no cap). The server cuts it back at sign-in and on every upload. |
| A player's progress is gone | Profiles nobody has signed in to for 30 days are pruned. The last 50 pruned profiles are in `Pruned/` (see [Files](#files-the-server-keeps)). |
| Lost admin access | Read the token from the log, or delete `admin_token.txt` and restart, then use `/claimadmin` again. |
