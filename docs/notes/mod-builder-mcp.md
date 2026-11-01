# Mod Builder MCP server

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## Mods (remake-only)

- **Mod Builder MCP server** (remake, 2026-10-15; user's request: the player's own AI assistant builds mods with the game's
  checks, no AI chat in the game; `UI.ModBuilderMcp` (+ `ModBuilderMcpTools`, `ModBuilderMcpPump`), the builder's **AI
  tools** button in the top bar between Redo and the Hangar button (not among the kinds: `ModBuilder.Kind.AiTools`, not in
  `KindTabs`; `ModBuilderScreenAi`, shown in the hangar's and space's builder screens): MCP's Streamable HTTP on a
  `TcpListener` (POST /mcp JSON-RPC 2.0 answered as application/json: initialize (protocol 2025-06-18 / 2025-03-26 /
  2024-11-05, `Mcp-Session-Id`), ping, tools/list, tools/call (text / image content, isError); notifications 202, GET 405,
  DELETE 200). Off by default (PlayerPrefs `mb_mcp_on`, port `mb_mcp_port` 47630, `mb_mcp_token`), Windows only (player and
  Editor, `Supported`), running only while `ModBuilder.Active` and not in a play test's own screens; 127.0.0.1 only, a
  Bearer token (constant-time), Origin / Host must be localhost (DNS rebinding). The HTTP thread never touches the Unity
  API (Localization, PlayerPrefs, Application, Time: `liveToken` / `liveVersion` are cached at Start): each call is queued
  and run on the main thread by the pump (`Call.Wait(condition, then)` for answers after a reload; screenshots at
  WaitForEndOfFrame). Edits go through `ModProject.CaptureState` / `RestoreState` like an undo (`ExternalEdit`: saved, an
  undo step, the screen refreshed, the project read again); files only inside the project's folders with their extensions
  (models, textures, icons, portraits, music, sounds, voices, events, text). **Permissions** (the tab's row, PlayerPrefs
  `mb_mcp_permission`, `ModBuilderMcp.Permissions`; user's request: an AI building a mod without the player watching the
  game): Read only (every change refused, the AI told to suggest instead), Ask always (every change waits for the player's
  Yes), Ask to remove (the default: removals ask, the rest goes through), Allow all (nothing asks); `ModBuilderMcp.Permit`
  gates every tool that isn't `readOnly` / `noAsk` (show_kind, end_play_test) in `Pump` (delete_entry `permitsItself`,
  after its own checks), with the tool's `ask` text; the question (`ExternalAsk`: Yes / No / Yes to everything, the last
  setting Allow all) waits up to 5 minutes (`AskSeconds`, the HTTP thread's wait grows by it; then a No, the dialog closed,
  `ExternalCancelAsk`); without a builder screen on show the change is refused; builder_status and the instructions say
  the mode (`livePermission` for the HTTP thread). Tools: builder_status, search_content (`ModRefs`: the game's, the project's, other mods'),
  list_entries / get_entries / put_entry / delete_entry (the mod files' own JSON; put answers after the reload with the
  mod's errors / warnings and a quest's problems, `EventRunner.CheckScript`), list_files / read_file / write_file,
  compile_mission, validate, get_mission_schema, play_test / end_play_test, get_log (a 600-line ring, the token redacted),
  screenshot, undo / redo, show_kind; and (user's request: the AI should see textures and models and do anything the builder's
  screen does; `ModBuilderMcpVision`) view_model (`UI.ModelViewer`: any model rendered off-screen from named angles,
  `ModelViewer.Angles` front / back / left / right / top / bottom / threequarter / rear, into one contact sheet, like
  `ShipIconMaker` on its own stage and layer 30 with the scene's lights off it and two lights following each view; "gof2:<name>",
  "ship:..." (its hardpoints as dots in ModBuilderGizmo's colours with "P1" tags, hollow where a ray against MeshColliders on
  the readable meshes meets the hull first), "station:..." (`OrbitBuilder.StationAssembly`), a project .glb through
  `ModGltf.Load`; the tags drawn after every tile in a 3 x 5 bitmap font of its own), view_texture (a project image through
  `ImageImport.Decode`, or a game texture by name: of a model, loaded, a GoF2Backdrop material, or the models whose names start
  it; blitted, a normal map decoded like the Skins tab's `DecodeNormals`, the alpha on a checkerboard / kept / a mask),
  list_textures, select_entry (`ModBuilderScreen.ExternalSelectEntry`: every kind, as a click on its row; the kind's scene
  first), view (`ExternalCamera`: `HangarInspect.SetView` on the turntable ship, the Interiors kind's free camera, the space
  screen's orbit, kept through the range's / station's own re-framing; answered with a screenshot, `clean` = the panels faded
  by opacity, `ExternalCleanView`), hardpoint (`ModBuilderViewAi`: list with the hull's box, add where a ray from a side through
  'at' / 'offset' meets the gizmo's pick colliders, the view's own `AddMount`; snap, select, mirror, delete), engines, test_fly
  / end_test_fly, weapon_range, switch_hangar (`ModBuilderView.HangarChoices`, shared with the Hangar button), list_projects,
  create_project / open_project (`ModBuilder.Begin` on another project without ending the builder, so the server stays up;
  the Station scene once `ModLoading` is idle), export_mod (asks before replacing an installed mod); loadout (the Loadout tab's
  `SetSlot` / Auto-fill / the test cheats; an item by number, key or the project's id), room_marker (`ExternalRoomMarker`:
  list with the room's box; place at a position or where a ray from a point inside the room 'toward' a side meets its pick
  colliders (layer 21), the camera-like markers a little off the surface like `PlaceMarkerAt`; remove; the room rebuilt;
  before its required markers exist the room isn't built, so the first go by position), view's `map` (`StarMap.FocusSystem`;
  not the overview: framing every system of the builder's map, placeholders far out too, broke it), and play-test input
  (`UI.McpInput`, only during a play test or test flight: press holds game actions by their current bindings
  (`GameControls.Paths`, keyboard first, else the controller's) and keys, steer holds the left stick (+ boost / fire /
  throttle), on a virtual keyboard and gamepad of its own fed in `InputSystem.onBeforeUpdate` like `VrPad`, state sent only
  while something is held; ui_buttons / ui_click find shown Buttons by text on every `PanelRenderer`'s root (by reflection) and
  send a NavigationSubmitEvent). The game window is unfocused while the player is in their AI app: a build drops keyboard input
  then and the Editor gives it to its own windows unless the Game view has the focus, so while something is held the input
  settings ignore the focus (`backgroundBehavior` IgnoreFocus, `editorInputBehaviorInPlayMode` AllDeviceInputAlwaysGoesToGameView)
  on a copy made once (an asset never changed), their own values back 0.3 s after the release (`McpInput.Tick` from the pump);
  swapping whole settings objects lost the original (the Input System destroys the default one it made). A tool's `ask`
  returning null passes the gate (hardpoint's / loadout's / room_marker's list). patch_entry (user's request: small edits
  without rewriting a quest; `Modding.EntryPatch`, plain C#: set / insert / remove / move / test by slash paths into the
  entry, "-" past a list's end, applied to a copy all or nothing, the rest byte for byte; get_entries' `path` reads a part):
  one undo step through `ExternalEdit`, an entry's id / override can't change, mod.json only its editable fields; a quest
  already laid out in the graph (`startPos`) gets its new steps placed by `QuestGraph.Layout(all: false)` and the steps
  after one in its list moved down when it lands on them (`MakeRoomAfter`); answered like put_entry after the reload
  (`AnswerAfterReload`: the mod's errors, the quest's own problems). Verified in Play mode: a laid-out quest's line changed,
  a radio step and a title inserted into its `if` block (the hand-moved node kept, the new ones placed, the one below moved
  down), a misnamed field caught by the answer and renamed by `move`, undo / redo, a ship's armor, an id change refused.
  The tab: on / off, the permissions, the state, the port, the token (hidden; Show, Copy, New token), the
  Claude Code / Claude Desktop (`npx mcp-remote`) / any-client setup with Copy (the token masked on screen), what the AI did
  (the button's green border while it runs). Docs: `Modding/ai/README.md`, SKILL.md. Verified with curl in Play mode: auth,
  the Host check, initialize, tools/list, put / compile / validate, a removal refused and confirmed, undo, a screenshot, a
  quest play test with its dialogue, the log, end play test, write_file and a character, a path escape and ships.json as a
  file refused; each permission mode (Read only refusing put_entry, Ask always's question answered by Yes to everything
  switching to Allow all, a removal under Allow all without a question, a No under Ask to remove); the vision tools: a game
  model and ship 28's contact sheet with its 12 tagged hardpoints, a project ship on a game model with hardpoints added onto
  its hull from the front / top / back and mirrored, a project .glb, station 70, the Veteran's diffuse / normal (decoded) /
  glow textures, select_entry of a ship (Hardpoints tab) and of a quest in its graph view, view from three sides in the hangar
  and from the top in the weapon range, a clean screenshot, engines, a test flight and back, the range with a recoloured
  weapon, the Vossk and Midorian hangars, a second project made, opened and left, export; a loadout set, auto-filled and its
  cheats switched; a project hangar on the game's Terran room given its pad and camera, then a parked pad found on the floor
  by a ray; the map centred on a system; a test flight steered (boost, fire), its autopilot menu opened and Dis picked by number
  keys, docked on held fast-forward; a quest played through by input alone (the conversation clicked away, Launch clicked, the
  orbit reached, the radio, the autopilot docking, its reward paid). Not yet: a real Claude client connected.
  Brought up to date with the builder's later kinds (2026-10-22, `ModBuilderMcpContent`): `search_content` kinds `sound`
  (Resources/GoF2ModBuilder/SoundNames, what a `sounds/<name>` replaces, and the project's new sounds) and `voice` (the
  story's pages and radio, the voice table and `StoryAssets.VoiceNames`, with speaker and English line, and the project's
  lines per language); `write_file` of a `models/<name>.aem` converts it with `AemImport` and answers the `.glb` and its
  length; `optimize` (`ModOptimizer`, run a step a frame through `Call.Wait`, a `History.Commit` first, `ModLodCache.Forget`
  after; `list` reads only); `import_mod` (`ModProject.CopyMod` on a worker thread, `replace` / `open`); and
  `replace_game_character` (`Portrait.RenderSpeaker`, as the Characters kind). Verified with curl in Play mode: both
  searches, a character replacing Gunant, an optimized normal map (2.1 -> 0.87 MB), an .aem quad converted, an installed
  mod imported, and three undos putting it all back.
- **Races** (remake, 2026-10-28, with the builder's Races kind; `ModBuilderMcpRaces`): the entry kind `races` (races.json,
  so list_entries / get_entries / put_entry / patch_entry / delete_entry / select_entry / show_kind take it),
  search_content kind `race` (`RefKinds` is in `RefKind`'s order: the index is the kind), builder_status's count, and
  four tools: `list_races` (the game's by word and number, the project's with relations, owned systems and ships, other
  mods' by key), `put_race` (`CheckRace`: id, name, look, color, standing, flags, an icon file in the project, the gun an
  item, each relation a known race and a relation word; another mod's race or item becomes a dependency), `delete_race`
  (asks like delete_entry, `ModProject.RemoveRace` with every reference, answers how many changed) and `set_race` (target
  system: owner / locals / raiders, ship: race / trafficRaces; each value resolved through `ModRefs.Find(RefKind.Race)`,
  an owner only Terran-Midorian or a mod race, a ship's no Specters; traffic races' game races as numbers). Edits run
  through `EditProject`: the project's own methods inside `ExternalEdit`, the state replaced by a fresh `CaptureState`
  (ModRefs.Use writes the manifest, which a state captured before would have put back); answered after the reload like
  put_entry. Docs: Modding/ai/README.md, SKILL.md, the server's Instructions. Not verified with a client yet.
- **The modding guide on the server** (remake, 2026-11-01, the user's request: the instructions sent assistants to SKILL.md
  "in the game's repository", which a player connecting their assistant to a build doesn't have): `read_guide`
  (`ModBuilderMcpGuide`, read-only) hands out Modding/ai/gof2-modding's seven files (`Modding.ModdingGuide.Files`) in parts,
  at most `MaxChars` 40000 an answer with a note where the text goes on: no arguments the list with sizes, a file its
  headings with line numbers (outside code blocks; a file under 40000 characters whole), `section` the text under the first
  heading equal to or containing the words up to the next heading of its level or above, `search` the lines holding every
  word (file, line, the heading above, a long line cut around the first word), `line` / `lines` a range. The same files are
  MCP resources (`resources/list` / `resources/read`, `gof2-guide://<file>`, capability `resources`; empty
  `resources/templates/list`), answered on the HTTP thread from the texts `Start` preloads (`ModdingGuide.Preload`: the
  Editor reads the repository's folder, so edits show at once; a build reads Resources/GoF2ModdingGuide, which
  `EditorTools.ModdingGuideBundle` copies in before every build and deletes after it, git-ignored like BuildVersionStamp's
  fingerprint). The instructions now point at read_guide. Tests: `ModdingGuideTests`. Verified with curl in Play mode:
  the capability, the instructions, all seven resources listed and missions.md read, read_guide's list, SKILL.md's
  outline, guide.md's "Engine effect" section, a search, a range of reference.md, an unknown file refused; and the
  bundle's copy loaded back through Resources (all seven) and removed. Not yet: a real build, a real client.
