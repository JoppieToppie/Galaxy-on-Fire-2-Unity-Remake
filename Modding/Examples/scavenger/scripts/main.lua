-- Scavenger: a whole game mode as a mod, for the Galaxy on Fire 2 Remake (Modding/README.md "Scripts",
-- "Patching the game's methods"). After AntonTolochko's Scavenger mode (GitHub #285 / #313).
--
-- campaign.json gives the start (a Betty at Dis, EMP gear, no credits), the Default economy and the one medal.
-- Everything else is here: the mode's rules are patches on the game's own methods. No patch does anything
-- outside a Scavenger game (the mod can stay on while you play the ordinary campaigns).

local ID = "scavenger"
local MAX_TECH = 5                               -- shops sell weapons and equipment up to this tech level
local MUTAGEN, CHROMO, FIREWORKS = 209, 210, 232

-- Session.ModCampaign is the running game's campaign: the mod's id, from before the game's tables load.
local function on()
    return Session.ModCampaign == ID
end

local function isOre(item)                        -- ores (category 23) and cores (24)
    return item.categoryId == 23 or item.categoryId == 24
end

-- What a freshly made offer may hold. TypeId: 0 primary, 1 secondary, 2 turret, 3 equipment, 4 goods.
local function sold(db, index)
    local item = db:Item(index)
    if item == nil then return true end
    if index == FIREWORKS or isOre(item) then return false end
    local kind = item.TypeId
    return kind == 1 or kind == 4 or item.techLevel <= MAX_TECH
end

-- ---- the start -------------------------------------------------------------------------------------------

-- The finished galaxy: every station open, every dealer's ship on sale, the map the campaigns reveal.
-- The game doesn't keep that switch in a save, so it is set again whenever a Scavenger game loads.
game.patch("ModCampaigns", "Start", function(original, db, campaign)
    local scene, problem = original(db, campaign)
    if scene ~= nil and campaign.Id == ID then
        Session.UseCompletedWorld()
        GalaxyMap.Visibility(db)
        if Session.KaamoState < 2 then Session.KaamoState = 2 end   -- the Kaamo Club can be bought
        Blueprints.Unlock(CHROMO)
        Blueprints.Unlock(FIREWORKS)
    end
    return scene, problem
end)

game.on("gameLoaded", function()
    if on() then Session.UseCompletedWorld() end
end)

-- ---- the two recipes -------------------------------------------------------------------------------------

-- One of each of the best weapons and equipment, the Chromo Plasma, the mutagen, five of each core.
local RECIPE = {
    { 228, 1 }, { 229, 1 }, { 183, 1 }, { 231, 1 }, { 230, 1 }, { 193, 1 }, { 178, 1 },                -- primary weapons
    { 224, 1 }, { 182, 1 },                                                                              -- turrets
    { 225, 1 }, { 54, 1 }, { 59, 1 }, { 67, 1 }, { 194, 1 }, { 195, 1 }, { 188, 1 }, { 80, 1 }, { 84, 1 },
    { 85, 1 }, { 90, 1 }, { 96, 1 }, { 184, 1 }, { 220, 1 }, { 200, 1 }, { 206, 1 }, { 208, 1 }, { 223, 1 },
    { 227, 1 },                                                                                          -- equipment
    { CHROMO, 1 }, { MUTAGEN, 1 },
    { 165, 5 }, { 166, 5 }, { 167, 5 }, { 168, 5 }, { 169, 5 }, { 170, 5 }, { 171, 5 }, { 172, 5 }, { 173, 5 },
    { 174, 5 }, { 175, 5 }, { 218, 5 },                                                                  -- cores
}

-- The game's tables, each time they load. Only the Chromo Plasma and the Fireworks can be built: every other
-- recipe goes, which also lets shops and loot hold what used to be a blueprint's product.
game.on("database", function(db)
    if not on() then return end
    for i = 0, db.Items.Count - 1 do
        local item = db.Items[i]
        if item.index ~= CHROMO and item.blueprint.Count > 0 then item.blueprint:Clear() end
    end
    local fireworks = db:Item(FIREWORKS)
    for _, part in ipairs(RECIPE) do
        local ingredient = db:Item(part[1])
        if ingredient ~= nil then
            local line = BlueprintPart.__new()
            line.item, line.amount, line.name = part[1], part[2], ingredient.name
            fireworks.blueprint:Add(line)
        end
    end
end)

-- Neither can be bought finished. The method answers (handled, allowed, price): out values follow the result.
game.patch("ModBlueprints", "AutocompleteOverride", function(original, product)
    if on() and (product == CHROMO or product == FIREWORKS) then return true, false, -1 end
    return original(product)
end)

-- ---- shops and sellers -----------------------------------------------------------------------------------

-- Shop.GenerateItems makes a station's stock when it is new: what the player sells there later is never
-- touched, so better loot can be sold and bought back.
game.patch("Shop", "GenerateItems", function(original, db, station)
    local stock = original(db, station)
    if not on() then return stock end
    for i = stock.Count - 1, 0, -1 do
        if not sold(db, stock[i].item) then stock:RemoveAt(i) end
    end
    return stock
end)

-- A bar visitor selling an item: another visitor until the item is one a shop could have.
game.patch("AgentGenerator", "CreateAgent", function(original, db, station)
    local agent = original(db, station)
    if not on() then return agent end
    for _ = 1, 20 do
        if agent.offer ~= AgentOffer.SellItem or sold(db, agent.sellItem) then break end
        agent = original(db, station)
    end
    return agent
end)

-- No blueprint sellers, and not the Kaamo Club's dealer in unique items (story character 25).
game.patch("AgentGenerator", "StorySellerAvailable", function(original, character)
    if on() and (character.sellBlueprint >= 0 or character.index == 25) then return false end
    return original(character)
end)

game.patch("AgentGenerator", "AddStoryBlueprintSellers", function(original, db, station, agents)
    if not on() then original(db, station, agents) end
end)

-- Nor any blueprint of the newer kind (blueprints.json: the game's own Khador Slipstream, other mods'):
-- none is on offer anywhere, from a seller, a derelict or a destroyed ship.
game.patch("ModBlueprints", "Available", function(original, blueprint)
    if on() then return false end
    return original(blueprint)
end)

-- A capital ship's resupply: the same limit on what it sells.
game.patch("CapitalShips", "ResupplyOffers", function(original, db, health, store)
    local offers = original(db, health, store)
    if not on() then return offers end
    for i = offers.Count - 1, 0, -1 do
        if not sold(db, offers[i].item) then offers:RemoveAt(i) end
    end
    return offers
end)

-- ---- cargo -----------------------------------------------------------------------------------------------

-- NPC cargo: any of the original's items with the same chance, whatever its rarity or tech level. Not what
-- has its own source: mission goods, alien remains, ores and cores, the plasmas, the mutagen, the two products.
local NOT_LOOT = { [115] = true, [116] = true, [117] = true, [131] = true, [201] = true, [202] = true,
                   [203] = true, [204] = true, [MUTAGEN] = true, [CHROMO] = true, [FIREWORKS] = true }
local pool

local function lootPool(db)
    if pool == nil then
        pool = {}
        for index = 0, 232 do
            local item = db:Item(index)
            if item ~= nil and not NOT_LOOT[index] and not isOre(item) then pool[#pool + 1] = index end
        end
    end
    return pool
end

-- NpcTables.RollLoot rolls every fighter's and freighter's hold. The game's own roll decides how many
-- stacks there are (an empty hold as often as before); the stacks themselves come from the pool.
game.patch("NpcTables", "RollLoot", function(original, db, freighter)
    local hold = original(db, freighter)
    if not on() then return hold end
    local stacks = hold.Count
    local items = lootPool(db)
    hold:Clear()
    for _ = 1, stacks do
        local item = db:Item(items[math.random(#items)])
        local amount = item.TypeId == 4 and math.random(1, 9) or math.random(1, 3)
        if freighter then amount = math.max(amount * math.random(2, 5), math.random(8, 12)) end
        hold:Add(ItemStack.__new(item.index, amount))
    end
    return hold
end)

-- A capital ship's crate: two or three freighter holds, without its guaranteed gear, rare goods and cells.
-- The method has two versions; the fourth argument names this one's parameters.
game.patch("CapitalShips", "RollLoot", function(original, db)
    local crate, gear = original(db)
    if not on() then return crate, gear end
    crate:Clear()
    for _ = 1, math.random(2, 3) do
        local hold = NpcTables.RollLoot(db, true)
        for i = 0, hold.Count - 1 do CapitalShips.Add(crate, hold[i].item, hold[i].amount) end
    end
    return crate, false
end, "Database, bool")

-- A ship with a fixed crate (a pirate outpost's, a pirate boss's): the same amounts, of items from the pool.
-- AddOwnLoot runs for every NPC ship once its cargo is set, and again when it comes back. "loot" is a
-- private field of the ship: game.field reads those. A mission's container is left alone.
game.patch("NpcShip", "AddOwnLoot", function(original, ship, capital)
    original(ship, capital)
    if not on() or capital then return end
    local spec = ship.Spec
    if spec == nil or spec.lootItem < 0 or spec.missionCrate >= 0 then return end
    local items = lootPool(Database.Shared)
    local loot = game.field(ship, "loot")
    for i = 0, loot.Count - 1 do loot[i].item = items[math.random(#items)] end
end)

-- ---- the medal -------------------------------------------------------------------------------------------

-- medals.json's Champion belongs to this mode only (campaign.json "medals" hides all others in it).
game.patch("ModMedals", "Available", function(original, medal)
    if medal.key == ID .. ":champion" and not on() then return false end
    return original(medal)
end)

game.log("Scavenger is ready: pick it under Start new game")
