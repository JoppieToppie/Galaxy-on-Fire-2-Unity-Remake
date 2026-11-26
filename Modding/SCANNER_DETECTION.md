# Scanner detection range

Set a scanner item's `stats.scannerRange` (attribute 194) to a positive number of game units (20 units per metre). Missing or zero retains unlimited legacy detection. The range is measured from the observing ship, not the camera. For example, `"stats": { "scannerRange": 100000 }` gives a 5 km base range.

In mod.json, optionally define target signatures by canonical ship class:

```json
"scannerClassDetectionPercent": {
  "stealth_fighter": 50,
  "stealth_bomber": 60,
  "frigate": 150,
  "freighter": 140,
  "cruiser": 180,
  "carrier": 200
}
```

Effective range = scanner range × target class percentage / 100. The stealth fighter (50%) and stealth bomber (60%) are defaults for finite scanners; the capital percentages shown are examples. Unknown/unclassified and other unspecified classes use 100%. Positive integer percentages from 1 to 1000 are supported. Later active mods override earlier entries for the same class. The map is included in the multiplayer mod signature; item stats are already hashed. A finite scanner range is required for class modifiers to have an effect.

## Passive stealth generators

A fitted or integrated cloak generator gives a passive detection multiplier even when the active cloak is off. Defaults to 80% of the target's class signature (20% reduction). Set `"scannerGeneratorDetectionPercent": 80` in mod.json to tune it (1–100, 100 disables the equipment bonus). Use a value above the dedicated stealth class signatures for the intended balance. Stealth fighters and bombers use their own class signature instead of stacking the generator multiplier. Capital signatures are multiplied by the generator percentage; e.g. a 200% carrier becomes 160%. Generator presence on remote players is replicated from their actual flight cloak equipment, and integrated cloak hulls are recognised for NPCs and their proxies. Procedural NPCs have no general equipment inventory for arbitrary fitted generators. Active cloak rules are unchanged. Both clients and server need the updated executable.

The rule covers ship markers, local/remote ship locks, lock loss, radar junk and manual salvage candidates/markers. It does not change NPC AI detection, visual models, cloak, spectral-filter gas clouds, navigation/docking, autonomous tractor beams, weapons' independent ranges or network replication. Existing scan readout/lock-time settings still apply.

Verification: test a 100000-unit scanner against an ordinary target at 5 km, a stealth fighter at 2.5 km and a carrier at 10 km. Check just inside/outside each limit, lock loss while leaving range, empty/no modifiers, remote NPCs and players, and free-look camera offsets. A scanner without the attribute must retain its original behavior.
