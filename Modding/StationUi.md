# Native station UI customization

Scripts can customize the native UI Toolkit interface after it is laid out:

```lua
game.on("stationUi", function(screen, page)
    local ui = game.stationUi
    if screen == "station" then
        ui.text("hangarButton", "Trade & outfitting")
    elseif screen == "station_management" and page == "overview" then
        ui.addLabel("managementBody", "welcome", "Welcome to station administration.")
        ui.addButton("managementBody", "help", "Help")
    elseif screen == "marketplace" then
        ui.text("tabShop", "Exchange")
        ui.style("hangarDetails", "minWidth", 300)
    end
end)
game.on("stationUiAction", function(id, screen, page)
    if id == "help" and screen == "station_management" then
        game.message("Select a facility or management page to begin.")
    end
end)
```

The event is synchronous; do not wait or retain UI references. It runs again after native rebuilds, including marketplace catalogue refreshes. Apply changes declaratively on each pass. The current screen can be `station` (menu), `station_management` (including flight), `marketplace_listing` (listing form), `hangar` or `marketplace` (shop/storage). The page identifies the native view, management page or hangar tab. The management page is the page resolved by the native page bar.

API (only during the event):

- `names()`: named elements in the current scope, for discovery. Names are scoped to a screen. Native implementation names may evolve; unnamed controls are not individually addressable.
- `text(name, text)`: relabel a named native TextElement or TextField caption; preserves controller glyphs and field values. Avoid altering prices, stock counts or other live data.
- `hide(name)`: hide a named element for this render pass; not a security mechanism or a way to disable keyboard shortcuts.
- `style(name, property, value)`: numeric UI Toolkit points for width, height, minWidth, minHeight, fontSize, marginTop/Bottom/Left/Right, paddingTop/Bottom/Left/Right; numeric flexGrow.
- `color(name, property, htmlColor)`: set text or background color (e.g. "#6AAFE6").
- `addPanel(parentName, id, direction)`: create a custom row or column container, returning its namespaced name or false.
- `addLabel(parentName, id, text)`, `addButton(parentName, id, text)`: add mod-namespaced controls to an existing container. Buttons invoke the registering mod's stationUiAction event. Use dedicated containers, not native item lists whose selection and navigation assume native rows. Custom buttons may require mouse or touch depending on the screen's controller navigation.
- `"$root"` addresses the scope root. Functions modifying controls return true on success, false if unsupported or not found.

Station management anchors: managementHeader, managementHeading, managementBack, managementPrimaryNav, managementStationList, managementPageBar, managementBody, managementFeedback. Existing navigation buttons are named managementGroup-<page> and managementPage-<page>. Fields are named field-<key>. The station menu scope excludes the hangar and management overlays; customize those with their own events. Station menu and hangar UXML names remain available (e.g. hangarButton, playerStationsButton, tabShip, tabShop, tabStorage, tabBlueprints, hangarDetails).

Changes are restored before the next native render pass, on panel detach, or when the scripts stop. If two mods set the same property, the later handler wins. Some live native updates can overwrite UI modifications between render passes. This API does not replace native transaction code, permission checks, input bindings, tabs, data models or multiplayer commands; hiding controls is cosmetic and does not grant or revoke permissions. Custom buttons run ordinary Lua, with the same authority as existing mod scripts.
