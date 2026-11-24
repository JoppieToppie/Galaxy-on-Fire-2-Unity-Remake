-- Heavy Ammo: mounted secondary weapons take cargo space (Modding/README.md "Patching the game's methods").
--
-- Shop.CargoLoad is what the whole game asks for the tons in the hold (the shop, the HUD, the tractor beam,
-- mining). This adds the weight of the rounds mounted on the ship to the game's own answer, so a full rack
-- of missiles leaves less room for cargo.
local TONS_PER_ROUND = 0.5

game.patch("Shop", "CargoLoad", function(original)
    local load = original()
    local db = Database.Shared
    for i = 0, Session.Equipment.Count - 1 do
        local mounted = Session.Equipment[i]
        local item = db:Item(mounted.item)
        if item ~= nil and item.TypeId == 1 then                 -- 1: a secondary weapon; its amount is its ammo
            load = load + math.floor(mounted.amount * TONS_PER_ROUND)
        end
    end
    return load
end)
