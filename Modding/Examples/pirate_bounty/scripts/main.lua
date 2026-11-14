-- Pirate Bounty: a script example for the Galaxy on Fire 2 Remake (Modding/README.md "Scripts").
-- main.lua runs first when the mod starts. This top part only sets things up; the work is done
-- in the functions the game calls when something happens (game.on).

-- A module of this mod: scripts/lib/bounty.lua, run once, giving back the table it returns.
local bounty = require("lib.bounty")

-- game.saved is a table kept in the player's save file. Read it inside the handlers:
-- loading another save changes what is in it.
local function kills()
    return game.saved.kills or 0
end

-- An NPC ship was destroyed: the ship (the game's NpcShip) and whether the player did it.
-- Standing is one of the game's classes: every class is a global by its name, and
-- print(game.describe("Standing")) lists what it offers.
game.on("shipDestroyed", function(ship, byPlayer)
    if not byPlayer or ship.Race ~= Standing.Pirate then return end
    game.saved.kills = kills() + 1
    local pay = bounty.forKill(kills())
    Session.Credits = Session.Credits + pay
    game.message("Pirate bounty: +" .. pay .. " $")
end)

-- Docked: the hangar stands. game.wait pauses this function for two seconds of game time
-- (the game goes on meanwhile), so the line comes after the docking's own messages.
game.on("station", function(level)
    if kills() == 0 then return end
    game.wait(2)
    game.message("Pirates destroyed for the bounty office: " .. kills())
end)

-- A command of the mod's own: "/bounty" in the multiplayer chat, "bounty" in a mission
-- graph's Command node, game.command("bounty") from another script.
game.addCommand("bounty", function(args, who)
    return "Pirates destroyed: " .. kills() .. ", the next one pays " .. bounty.forKill(kills() + 1) .. " $"
end)

game.log("Pirate Bounty is running: " .. bounty.base .. " $ a pirate")
