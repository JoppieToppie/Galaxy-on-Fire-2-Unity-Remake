# Making mods with an AI assistant

`gof2-modding/` holds a guide written for AI assistants (Claude, ChatGPT, Gemini...): `SKILL.md` is the complete mod file
format with rules, balancing numbers and a checklist, `reference.md` lists every number and name of the original game a mod
refers to (items, stats, blueprints, ships, systems, stations, story steps and speakers, models and their textures, music,
sounds), `voices.md` every voice clip with its line, `missions.md` + `mission.schema.json` the mission format (`.gof2mission`: missions,
bar missions and events as steps, which the Mod Builder also shows as a node graph), `events.md` every event graph node and
command, and `guide.md` the full modders' guide. With both, an assistant can write a working mod for you.

## Claude

- **claude.ai (Skills):** zip the `gof2-modding` folder and upload it as a custom skill in claude.ai's settings. Then ask, for
  example: "Make a GoF2 Remake mod that adds a Valkyrie ship as a blueprint after the Supernova."
- **Claude Code:** copy the `gof2-modding` folder into `~/.claude/skills/` (or your project's `.claude/skills/`). Claude uses
  it by itself when you ask for a GoF2 mod.
- **Any chat:** attach `SKILL.md`, `reference.md` and `guide.md` (plus `missions.md` for missions / bar missions, `voices.md` for voice-over) (or paste them) at the start of the conversation.

## ChatGPT and others

Attach `SKILL.md`, `reference.md` and `guide.md` (plus `missions.md` for missions / bar missions, `voices.md` for voice-over) to the chat (or a custom GPT's knowledge, or a project's files), then say what the mod
should do. If the assistant can't read files, paste `SKILL.md` first and the part of `reference.md` you need.

## Tips

- Say which ships or items your new content should be like ("as strong as the Phantom", "a blaster like the K'booskk") and
  what you have: models (GLB), icons, card art, sounds. The assistant tells you the sizes and folders for the files it names.
- Copy the files it gives you into a folder named after the mod id, put it in the game's Mods folder, press **Refresh** in the
  Mods screen and turn it on. If the Mods screen shows an error or warning, paste it back to the assistant.
- Story missions and bar missions: the assistant writes them as `events/<name>.gof2mission` files (`missions.md`), which the game runs and
  the Mod Builder opens as a graph. Unity-made event graphs still work too.

## Live in the Mod Builder (MCP, Windows)

The game's Mod Builder can let your assistant work on the open project directly, with the game's own checks: main menu >
Mods > Mod Builder, open a project, press **AI tools** in the top bar (between Redo and the hangar) and switch the **MCP
server** on (it is off by default and runs only while the builder is open, on this computer only, with a token). The tab
shows the commands to connect, with Copy buttons:

- **Claude Code:** `claude mcp add --transport http gof2-builder http://127.0.0.1:47630/mcp --header "Authorization: Bearer <token>"`
- **Claude Desktop:** the tab's JSON for `claude_desktop_config.json` (Settings > Developer > Edit config; it uses
  `npx mcp-remote`, so Node.js must be installed). Restart Claude Desktop.
- **Other MCP apps:** Streamable HTTP, the URL `http://127.0.0.1:47630/mcp` and the header `Authorization: Bearer <token>`.

The server carries this guide too: `read_guide` hands the assistant the files above in parts (the list, a file's headings, a
section, a search, a range of lines), and clients that attach MCP resources see them as `gof2-guide://<file>`, so an
assistant connected to the game needs nothing else. Setting the skill up as well (above) still saves it the lookups. Its tools:
`builder_status`, `search_content` (the game's, your project's and other mods' items, ships, systems, stations,
characters, missions, music, rooms, races, and the game's sound effects and voice lines to replace), `list_entries` / `get_entries` / `put_entry` / `delete_entry` (the project's entries in
the mod files' own JSON; `patch_entry` changes part of one, e.g. one line of a mission, leaving the rest and its graph layout
as they are), `list_files` / `read_file` / `write_file` (portraits, textures, models, audio inside the project; a game
`.aem` model is converted to `.glb`),
`compile_mission`, `validate`, `get_mission_schema`, `play_test` / `end_play_test`, `get_log`, `screenshot`, `undo` / `redo`,
`show_kind`; for the mod's own races (races.json) `list_races`, `put_race`, `delete_race` (with every reference to it) and
`set_race` (a system's owner, locals and raiders, a ship's race and traffic races). To see its work: `view_model` (any model, a game one, your ship with its hardpoints tagged, a station or a .glb,
drawn from any side into one image without touching the screen), `view_texture` / `list_textures` (textures as images, normal
maps decoded), `select_entry` and `view` (an entry on the builder's screen, its camera aimed from a side, with a screenshot,
optionally without the panels). To work the builder: `hardpoint` (hardpoints placed onto the hull from a side, snapped,
mirrored, removed), `engines`, `test_fly` / `end_test_fly`, `weapon_range`, `switch_hangar`, and the projects:
`list_projects`, `create_project`, `open_project`, `import_mod` (an installed mod as a project), `export_mod`; `optimize`
(big models and textures made smaller), `replace_game_character` (a story character replaced, starting from its own
portrait); `loadout` (the test flight's weapons and cheats),
`room_marker` (a room's pad, camera and other markers put on its surfaces). And to play your mod: during a play test or test
flight `press` (game actions and keys), `steer` and `ui_buttons` / `ui_click` (the buttons on screen by their text), so the
assistant can launch, talk, dock and play a mission through, watching with screenshots. Every change is an undo step in the builder, and the **AI
tools** tab lists what the assistant did.
**Permissions** on the same tab decide what waits for you: *Read only* (the assistant only looks and suggests), *Ask
always* (every change waits for your Yes in the game), *Ask to remove* (the default: only removing an entry asks) or
*Allow all* (nothing asks, so you needn't watch the game while it builds). A question's **Yes to everything** switches to
Allow all. Then ask, for example: "Make a mission where Vega asks me to clear two waves of pirates at Var
Hastra, then play-test it."

