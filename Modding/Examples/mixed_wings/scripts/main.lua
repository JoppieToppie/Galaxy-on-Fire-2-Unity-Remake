-- Mixed Wings: every raider in a group flies its own ship (Modding/README.md "Patching the game's methods").
--
-- The game, like the original, picks one ship model for a whole raider group. TrafficPlan.AddRaiders is the
-- (private) method that adds a group to an orbit's list of ships: this lets it run, then gives each ship it
-- added a pick of its own. "ships" is the game's list of SpawnSpec; game.describe("SpawnSpec") shows its fields.
game.patch("TrafficPlan", "AddRaiders", function(original, ships, count, race, spawn)
    local first = ships.Count
    original(ships, count, race, spawn)
    for i = first, ships.Count - 1 do
        ships[i].ship = NpcTables.RandomFighter(race)
    end
end)
