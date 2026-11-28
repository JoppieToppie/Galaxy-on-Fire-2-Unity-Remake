# Advanced economy builder verification

## Editing

The Mod Builder's Advanced economy menu has General, Production, Freight, Pricing and Routes tabs. Production can filter by faction, multiple systems and commodity. Bulk changes affect all matching rules, not just the current page. At least one filter is required before a bulk edit. The page size is 20.

MCP entry kind: `advancedEconomy`. It is a singleton JSON object, with no entry id required for get/put/patch/delete. `write_file` accepts `advanced-economy.json` as text or base64 and uses the same state/validation/history path. Prefer a guarded patch, e.g.:

```json
{"kind":"advancedEconomy","ops":[{"op":"test","path":"tickIntervalSeconds","value":900},{"op":"set","path":"tickIntervalSeconds","value":1200}]}
```

Use get_entries to inspect the current value first. Runtime reference validation uses the builder database; a newly added reference should be loaded before it is used in an economy edit. Disabled configurations stay inactive.

## Physical freight dispatch rules

The Freight tab exposes `freightMinimumShipment` (50 t), `freightMinimumUtilizationPercent` (25%), `freightEmergencyDispatch` (true), and `freightCriticalStockPercent` (15%). These defaults apply to existing configurations on reload. Set 1 t and 0% to restore unrestricted physical dispatch. The host considers all racial and custom hulls and their actual cargo capacities; fleet weights apply only among qualifying hulls. The threshold cannot exceed the route capacity, destination target or hull capacity. Emergency checks use destination stock plus already reserved inbound cargo. Existing shipments are not re-dispatched or cancelled.

## Save recovery

A normal project save stages serialized files in memory, skips byte-identical data, probes changed destinations for sharing failures, writes a journal beside (not inside) the project folder, then atomically replaces individual files. If an operation fails, the builder restores prior contents and reports the affected path. A journal named `<project-folder>.save-recovery.json` remains if recovery cannot complete. Do not delete it blindly. Close other programs holding the reported files, then retry. If recovery reports a third-party change, retain both the journal and the external file and resolve manually before saving. Separate direct asset imports are atomic individually rather than part of the serialized-project transaction.

## Verification before release

Run Unity EditMode tests for ModProjectHistoryTests, ModBuilderMcpTests, ModStockTests and AdvancedEconomyTests, then the full EditMode suite. On a disposable project:

1. Add an economy JSON externally with the builder open, then save a separate edit. Verify the external bytes remain and the editor/MCP reflect the values.
2. Change a config both externally and in the builder before saving. Verify the warning and that neither version is silently overwritten.
3. Hold a changed mission or config file with an exclusive Windows sharing lock and attempt a save. Verify the error names the path and no other changed serialized file is published. Release the lock and retry.
4. Exercise MCP get/put/patch/delete and write_file, including an invalid patch and undo/redo. Verify the live preview reloads and a disabled configuration remains disabled.
5. Open a 740-rule project, test all tabs, page boundaries and multi-system/faction/commodity bulk edits. Verify off-page rows outside the filter are untouched.

On a disposable persistent multiplayer profile server with two clients, verify:

- Simultaneous purchases from the same limited stock; no duplicated commodities, negative inventory or incorrect credits.
- Competing purchase/sale against the same price snapshot; authoritative prices and stock on both clients.
- Freight dispatch, physical destruction, partial salvage and repeated salvage attempts; no duplicated cargo or negative stock.
- Server restart before/after dispatch, arrival, purchase and freight destruction; saved inventory and shipment recovery without double application.
- Unmodded and disabled-economy sessions, solo and co-op campaign: no unintended advanced-economy simulation.
- Persistent profile server with enabled economy: expected price, stock and freight behavior.

These are release checks, not a record of executed tests.
