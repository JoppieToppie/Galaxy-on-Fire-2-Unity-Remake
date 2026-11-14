# Scavenger mode

An opt-in, single-player collection mode selected under New game > Custom gamemodes. Working title:
**Scavenger**. Difficulty remains selectable; the economy is always Default. Network support is out
of scope. Statistics and planet/station visit indicators retain their existing behavior.

## Start and world

- Betty at Dis (station 70), with Luna EMP Mk I, AB-1 Retractor, Telta Ecoscan and 10 mounted EMP GL I missiles.
- Zero credits, the existing single-player default. A different starting balance has not been agreed.
- No campaign. `Session.UseCompletedWorld` supplies the finished world's rules; all systems are revealed, but only
  Dis is marked visited. Freelance missions, diplomats and wingmen remain available.
- VoidX at Thynome and Specter at Katashán are available from the start through the completed-world dealer rules.
- Kaamo starts purchasable (state 2), or owned (state 3) when its existing new-game toggle is on. Its purchase price,
  Buskat requirement, ship dealer and upgrades are unchanged. The unique-item seller is omitted.

## Economy and loot

`ScavengerMode` owns the mode policy. Primary weapons, turrets and equipment have a generated-stock ceiling of **TL 5**.
Secondary weapons can include the former blueprint products, but not Fireworks. They retain the normal station-tech, occurrence and random stock checks; availability is not guaranteed. Station specialization remains: Sao Perula
sells commodities, Quineros equipment. Commodities keep their original generation rules, with ore and cores removed.
Bak S'ondorr (126) stocks mutagen from the start. Selling loot and buying it back is unrestricted.

Ordinary NPC cargo draws uniformly per item ID from the original game's permitted items (0..232). Neither occurrence,
category, faction, tech level nor former blueprint/Kaamo status affects selection. Default's empty-hold probability,
stack count and quantity rules remain. There is no forced energy-cell replacement or ore fallback.

Excluded: documents (115), secure container/cabin (116/117), Alien Remains (131), all ore/core categories (23/24),
four plasmas (201..204), mutagen (209), Chromo Plasma (210), Fireworks (232). These exclusions concern random cargo:
mining, plasma harvesting, Void remains and dedicated mission crates keep their original sources.

Capital cargo uses two or three freighter rolls from the same pool, without guaranteed rare goods, gear or cells.
Predetermined ordinary NPC cargo (e.g. pirate event rewards) is rerolled from this pool, retaining its quantities;
dedicated mission crates are preserved. External mods' custom loot is not added to the pool.

## Production

Only Chromo Plasma (210) and Fireworks (232) are craftable and unlocked immediately. No blueprint sellers.
Autocomplete is disabled for both. Chromo Plasma keeps the Default recipe: 917 of each plasma.
Fireworks produces the standard secondary-weapon batch of **10**. Ingredients can be invested incrementally and are
consumed using the existing production rules; Kaamo storage and freight behavior remain unchanged.

The Fireworks recipe has 42 rows in this explicit order. Every listed weapon/equipment item costs one unit:

| Group | Item IDs and names |
| --- | --- |
| Primary | 228 M6 A4 Raccoon; 229 Dark Matter Laser; 183 Disruptor Laser; 231 Mimung Blaster; 230 Mass Driver MD 12; 193 SunFire o50; 178 Icarus Heavy AS |
| Turrets | 224 Matador TS; 182 HH-AT Archimedes |
| Equipment | 225 Particle Shield; 54 Fluxed Matter Shield; 59 T'yol; 67 Rhoda Blackhole; 194 AB-4 Octopus; 195 Polytron Boost; 188 Ketar Repair Bot II; 80 Pulsed Plasma Thrust; 84 Hiroto Ultrascan; 85 Khador Drive; 90 Gunant's Drill; 96 Yin Co. Shadow Ninja; 184 Rhoda Vortex; 220 Spectral Filter Omega; 200 PE Fusion H2; 206 Gamma Shield II; 208 Nirai SPP-M50; 223 Pandora Leech; 227 Phoenix SIS |
| Goods | 210 Chromo Plasma x1; 209 K'mirkk Toad Mutagen x1 |
| Cores (last) | 165..175 and 218, each x5: Golden, Titanium, Iron, Orichalzin, Pyresium, Sodil, Doxtrit, Cesogen, Perrius, Hypanium, Void Essence, Novanium Core |

No alcohol, cabins, signatures, Dia EMP Mk III, weapon modifiers or Emergency System in the recipe.

Only Champion (35) is displayed/awarded. Its requirement is a completed Fireworks production run, not possession of
Fireworks. The production counter persists, so the medal can be checked after loading. The existing station medal
notification handles the award after the hangar closes. Other statistics still count normally.

## Integration and saves

- `Session.Mode` / save v19 `gameMode` identifies the ruleset (missing means Standard).
- Runtime database overlays affect recipes only for Scavenger; the shared database cache includes the mode.
  Original JSON item/economy tables are untouched.
- Standard saves retain `Saves/slot_NN.json`; Scavenger uses `Saves/scavenger_slot_NN.json` with its own autosave and
  eleven manual slots. Load game has a group selector. On entering the main menu, Resume selects the newest group.
- Export/import operates on the selected group; mismatched or mixed groups are rejected before writing. Each group
  has its own import backup. Save-folder synchronization mirrors both groups with distinct names.
- Original-game save transfer is only available for Standard, since its format cannot represent the custom recipe.
- A new standard campaign resets the mode. No Scavenger save is offered in the co-op save picker.

## Verification

`ScavengerModeTests` covers the loadout, recipes/order, shop restrictions, unlocks, seeded loot distribution, sale and
buyback, lounge offers, Champion, save isolation/round-trip/import, and returning to the standard ruleset.

Run in Unity 7000.0.0a16:

```sh
python -I Reference/tools/tests/run_tests.py --filter ScavengerModeTests --reload
python -I Reference/tools/tests/run_tests.py
```

Manual acceptance: start each Kaamo option; inspect the full recipe using keyboard/controller/touch; dock, save and
reload; switch save groups and export/import each; trade TL 6+ loot; accept/finish a freelance cargo mission; inspect
normal and capital cargo; harvest plasma and mine cores; incrementally supply and complete Fireworks; close the hangar
and verify Champion once; then start/load an ordinary campaign and verify its recipes and medals.

Unity 7000.0.0a16 compiled the combined integration checkout, including the current upstream Lua scripting update.
All 4,418 EditMode tests passed (zero failures or skips), including the Scavenger regression suite. These results
cover the combined checkout; the independently submitted feature branches have not each received a separate full run.
The contributor is also testing interactively. The batch PlayMode smoke run passed 45 of 46 cases; Splash's audio
assertion failed with batch audio disabled and passed separately in the interactive editor. A Windows player build
and the full manual acceptance checklist above have not been validated.

## Localization

The mode adds localized names, synopsis and detailed rules, the Champion requirement, new-game tabs and save-group
labels. All 14 offered languages are covered: English uses the existing code fallback; Arabic, German,
Spanish, French, Hindi, Italian, Japanese, Korean, Dutch, Polish, Brazilian Portuguese, Russian and Simplified Chinese
use the existing extra-language tables. Medal names in the descriptions match the original language tables. Item names,
recipe ingredients and the remaining controls reuse existing translations. Documentation and source comments remain
in English. Translations have not yet received native-speaker or in-game layout review.

## Custom gamemode menu

Start new game separates Campaigns and Custom gamemodes into tabs in one fixed-height frame. Both pages scroll within
the content area, keeping the heading, tabs and Back button stable. Custom modes use text rows and require no cover art.
`MainMenuGamemodes.CustomGamemodes` supplies stable IDs, localized names/descriptions and selection callbacks. Future
built-in modes can add an entry there and own their setup flow without adding campaign cards. This is a menu extension
point, not a mod scripting API; new persisted rulesets still need explicit save compatibility and tests.
Scavenger keeps its existing difficulty, Default economy, Kaamo options and isolated save group.

Save-group display names now call Standard "Original Game" (localized), pinned above a separator. Other persisted
modes are sorted by their displayed name using the current culture. Save identifiers and filenames are unchanged.
