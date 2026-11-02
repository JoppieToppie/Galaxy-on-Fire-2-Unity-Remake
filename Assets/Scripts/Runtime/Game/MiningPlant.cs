// MiningPlant.cs
// The Mido Mining Plant at Coromesk (station 103) as plain C#: docked there with no campaign mission, the ore in the hold
// turns into cores. The plant itself is World.Traffic.MiningPlant (from campaign 0x55); its menu is SpaceLevel's.
//   PlayerEgo::isDockedToMiningPlant 0xadcb8  docked at a docking point (state 1), Mission::isEmpty, not the alien orbit,
//                                             station 0x67
//   MGame::OnUpdate 0x1ac778                  on docking there: ChoiceWindow 290 ("#" = the ratio) + every ore (sort 0x17)
//                                             in the hold as "\nNt <name>", none -> "\n" + 286 ("Nothing"); remake: a
//                                             menu instead (Section: each ore and what it makes, then the actions)
//   MGame::OnTouchEnd 0x1a98d8                Yes: ores 0x9a..0xa4 -> core = ore + 0xb, then Novanium 0xd9 -> 0xda; every
//                                             full 30 t (Status::hardCoreMode: 100 t) of one ore -> 1 t of its core, the
//                                             rest stays; 291 + the cores made, or 292 when nothing was converted

using System.Collections.Generic;
using System.Text;

namespace GoF2Remake.Data
{
    public static class MiningPlant
    {
        public const int Station = 103;
        public const int OreCategory = 23;
        public const int FirstOre = 154, LastOre = 164, CoreOffset = 11, NovaniumOre = 217, NovaniumCore = 218;

        /// <summary>Tons of one ore per ton of its core: 30, Extreme 100 (Status::hardCoreMode).</summary>
        public static int Ratio => Session.IsExtreme ? 100 : 30;

        /// <summary>The core an ore turns into, -1 for anything else (a mod's ore included: the original's table only).</summary>
        public static int CoreOf(int ore) =>
            ore >= FirstOre && ore <= LastOre ? ore + CoreOffset : ore == NovaniumOre ? NovaniumCore : -1;

        /// <summary>PlayerEgo::isDockedToMiningPlant without the docking state: at Coromesk, not in the alien orbit, no
        /// campaign mission (the titanium of step 135 goes in through the ordinary drop-off).</summary>
        public static bool Offered(int station, bool alienOrbit)
        {
            var m = Story.Mission;
            return station == Station && !alienOrbit && (m == null || m.IsEmpty);
        }

        /// <summary>Yes: converts the hold; the cores made (core item -> tons), in the original's order.</summary>
        public static List<ItemStack> Convert()
        {
            var made = new List<ItemStack>();
            int ratio = Ratio;
            for (int ore = FirstOre; ore <= NovaniumOre; ore = ore == LastOre ? NovaniumOre : ore + 1)
            {
                int cores = Shop.CargoOf(ore) / ratio;
                if (cores <= 0) continue;
                Shop.RemoveFromCargo(ore, cores * ratio);
                Shop.AddToCargo(CoreOf(ore), cores);
                made.Add(new ItemStack(CoreOf(ore), cores));
            }
            return made;
        }

        /// <summary>Remake: the cores the hold's ore would make now (the menu offers converting only then).</summary>
        public static int CoresMade()
        {
            int n = 0;
            for (int ore = FirstOre; ore <= NovaniumOre; ore = ore == LastOre ? NovaniumOre : ore + 1) n += Shop.CargoOf(ore) / Ratio;
            return n;
        }

        /// <summary>Remake: the plant menu's ore section, in place of the original's question (290): the ratio, then every ore
        /// in the hold with what it makes ("→ 2 t Titanium Core") or how much more a core needs; a mod's ore has none.</summary>
        public static MenuSection Section(Database db)
        {
            var s = new MenuSection
            {
                heading = Localization.Extra("plantOreHeading", "Ore into cores"),
                text = string.Format(Localization.Extra("plantOreText", "Every {0} t of one ore makes 1 t of its core."), Ratio),
                empty = Localization.Extra("plantNoOre", "No ore in your hold."),
            };
            var listed = new HashSet<int>();
            foreach (var stack in Session.Cargo)
            {
                var it = db.Item(stack.item);
                if (it == null || it.categoryId != OreCategory || !listed.Add(stack.item)) continue;
                int tons = Shop.CargoOf(stack.item), core = CoreOf(stack.item), cores = core >= 0 ? tons / Ratio : 0;
                s.rows.Add(new MenuRow
                {
                    item = stack.item, amount = tons + " t", ready = cores > 0,
                    note = core < 0 ? Localization.Extra("plantOreNoCore", "no core")
                         : cores > 0 ? string.Format(Localization.Extra("plantOreMakes", "→ {0} t {1}"), cores, GameNames.Item(core))
                         : string.Format(Localization.Extra("plantOreNeeds", "{0} t more for a core"), Ratio - tons),
                });
            }
            return s;
        }

        /// <summary>Remake: the plant's menu (SpaceLevel, in place of the original's Yes / No): its title (3210), the ore
        /// entry and Leave, upper case as the menu's entries.</summary>
        public static string Title => Localization.Get(3210).ToUpperInvariant();
        public static string ConvertEntry => Localization.Extra("plantConvertOre", "Convert ore to cores").ToUpperInvariant();
        public static string LeaveEntry => Localization.Extra("plantLeave", "Leave").ToUpperInvariant();

        /// <summary>The answer: 291 and the cores made, or 292 "You do not have enough ore".</summary>
        public static string Result(List<ItemStack> made)
        {
            if (made.Count == 0) return Localization.Get(292);
            var sb = new StringBuilder(Localization.Get(291)).Append('\n');
            foreach (var c in made) sb.Append('\n').Append(c.amount).Append("t ").Append(GameNames.Item(c.item));
            return sb.ToString();
        }
    }
}
