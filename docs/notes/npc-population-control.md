# Ambient NPC population control

## Mod format

A `systems.json` entry may set `population` (0–4, default 1) to scale randomly generated ambient NPC traffic. Fractional counts use stochastic rounding; a multiplier of exactly 1 preserves the original count and random sequence. Values below 1 never increase a count, and a plan that rounds down to zero does not trigger the four-patrol empty-orbit fallback. `derelictFreighters` (default true) independently enables or disables random derelict freighters for that system; it is deliberately unaffected by `population`. The global Derelict Freighters gameplay option remains an additional master switch for vanilla random wrecks.

Both fields work on new systems and system overrides and are exposed in the Mod Builder. Default values are omitted from builder JSON.

## Spawn boundaries

Population scaling applies to local patrols, jumpers, freighters, raider groups, pirate-base escorts, the special pirate-orbit groups, late-campaign random Specter raids, random derelict escorts, and mod NPC types whose spawn rules place them as ambient traffic. Random big battles, random capital-ship specials and fleet battles are disabled at population zero. Random derelict freighters and mod blueprint derelicts are controlled by the independent per-system `derelictFreighters` switch, not by `population`; the separate toggle lets a mod disable derelicts without disabling other ambient traffic. Vanilla random wrecks also require the global Derelict Freighters gameplay option. Escorts waiting at a random derelict remain ambient NPC traffic and are scaled by `population`.

It does not remove mission/story ships, event-graph spawns, explicit scripted mod spawns, reported pirate sightings, siege/rift encounters or other content whose spawn is explicitly requested by gameplay logic. The separate unreported random pirate-event roll is suppressed at population zero; an active reported pirate sighting remains explicit content and is still spawned. This is implemented at traffic-plan generation points, not by a global NPC cap or despawn sweep.

## Multiplayer

The existing orbit authority builds the traffic plan. Its resulting NPCs are replicated through the existing NPC proxy synchronisation; this feature adds no generator, authority election or network protocol.

## Verification checklist

- Check parsing and validation of both fields on new systems and overrides.
- Check Mod Builder JSON round-trip and default omission.
- Exercise population values 0, fractional, 1, 2 and 4; verify zero suppresses ambient random specials and fractional values do not invoke the unscaled four-ship fallback.
- Verify `derelictFreighters: false` blocks random wrecks without changing other ambient traffic; population zero still permits derelicts when the per-system toggle is true; the global option still gates vanilla random wrecks.
- Verify mission/story/event/scripted spawns remain present at population zero.
- Verify multiplayer clients receive the authority's population and derelict decisions through existing proxy replication.
- Run the Unity EditMode suite and compile the project; record those checks separately from source inspection.
