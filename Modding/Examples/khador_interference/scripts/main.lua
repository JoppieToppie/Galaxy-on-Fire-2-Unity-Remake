-- Khador Interference: no Khador Drive in some star systems (Modding/README.md "Patching the game's methods").
--
-- data/interference.json lists the systems by name (or number) and the line shown on arriving in one.
local config = game.readJson("data/interference.json") or {}
local blocked = {}                               -- system number -> true

-- The names are looked up each time the game's tables load (other mods may add systems).
game.on("database", function(db)
    blocked = {}
    for _, name in ipairs(config.systems or {}) do
        local system = db.Systems:Find(function(s) return s.name == name or s.index == name end)
        if system ~= nil then
            blocked[system.index] = true
        else
            game.log("Khador Interference: there is no system \"" .. tostring(name) .. "\"")
        end
    end
end)

local function here(db)
    local station = db.Stations:Find(function(s) return s.index == Session.StationIndex end)
    return station ~= nil and blocked[station.system] == true
end

-- GalaxyMap.HasJumpDrive is what the star map and the flight controls ask before a Khador jump.
game.patch("GalaxyMap", "HasJumpDrive", function(original, db)
    if here(db) then return false end
    return original(db)
end)

game.on("space", function()
    if here(Database.Shared) then
        game.message(config.message or "The Khador Drive is offline in this system.")
    end
end)
