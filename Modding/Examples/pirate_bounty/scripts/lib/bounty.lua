-- A module: scripts in subfolders don't run by themselves. main.lua loads this one with
-- require("lib.bounty") and gets the table returned at the end.

local M = {}

-- The numbers come from a file of the mod (data/bounty.json), so they can be changed without
-- touching the code. A script reads its own mod's files only; a missing file gives nil.
local data = game.readJson("data/bounty.json") or {}

M.base = data.base or 250                -- credits for a pirate
M.streakBonus = data.streakBonus or 50   -- more for every ten pirates destroyed

-- The bounty for the pirate that brought the count to 'count'.
function M.forKill(count)
    return M.base + math.floor(count / 10) * M.streakBonus
end

return M
