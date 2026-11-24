-- Turret Status: a line on the flight HUD telling whether the automatic turret is switched on
-- (Modding/README.md "Objects and the interface").
--
-- The game's interface is Unity's UI Toolkit. The script adds a Label of its own to the flight HUD and keeps
-- its text right four times a second. The HUD is built again for every flight, so the label is too.
local LEFT, TOP = 24, 230                        -- where on the HUD, in its own pixels
local ON, OFF = "#9fd8ff", "#ff7a5c"

local label

-- The first automatic turret on the player's ship (PlayerTurret is the game's component for a turret item).
local function autoTurret()
    for _, turret in ipairs(game.find("PlayerTurret")) do
        if turret.IsAuto then return turret end
    end
    return nil
end

-- The HUD's root element is a private field of the FlightHud component: game.field reads those.
local function hudLabel()
    local hud = game.findOne("FlightHud")
    local root = hud ~= nil and game.field(hud, "root") or nil
    if root == nil then return nil end
    if label == nil or label.parent ~= root then
        label = Label.__new("")
        label.name = "turretStatus"
        label:AddToClassList("gof-semibold")                     -- the game's own text style
        game.style(label, { position = "absolute", left = LEFT, top = TOP, fontSize = 16 })
        root:Add(label)
    end
    return label
end

game.every(0.25, function()
    if game.scene() ~= "Space" then return end
    local line = hudLabel()
    if line == nil then return end
    local turret = autoTurret()
    if turret == nil then
        game.style(line, { display = "none" })
        return
    end
    line.text = "AUTO TURRET: " .. (turret.AutoEnabled and "ON" or "OFF")
    game.style(line, { display = "flex", color = turret.AutoEnabled and ON or OFF })
end)
