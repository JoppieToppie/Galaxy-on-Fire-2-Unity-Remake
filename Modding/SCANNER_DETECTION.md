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

Effective range = scanner range × target class percentage / 100. These are example balance values, not global defaults. Unknown/unclassified and unspecified classes use 100%. Positive integer percentages from 1 to 1000 are supported. Later active mods override earlier entries for the same class. The map is included in the multiplayer mod signature; item stats are already hashed. A finite scanner range is required for class modifiers to have an effect.

The rule covers ship markers, local/remote ship locks, lock loss, radar junk and manual salvage candidates/markers. It does not change NPC AI detection, visual models, cloak, spectral-filter gas clouds, navigation/docking, autonomous tractor beams, weapons' independent ranges or network replication. Existing scan readout/lock-time settings still apply.

Verification: test a 100000-unit scanner against an ordinary target at 5 km, a stealth fighter at 2.5 km and a carrier at 10 km. Check just inside/outside each limit, lock loss while leaving range, empty/no modifiers, remote NPCs and players, and free-look camera offsets. A scanner without the attribute must retain its original behavior.
