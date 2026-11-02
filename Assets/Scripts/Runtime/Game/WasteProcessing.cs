// WasteProcessing.cs
// Remake-only (players' suggestion, 2026-11-02): the Mido Mining Plant (MiningPlant) also sorts Space Waste (99) for a
// fee, a gamble: an entry of the plant's menu (SpaceLevel). Every ton is sorted on its own (players: "just go through each
// item"): the fee is paid, the ton goes, and it is either junk or one ton of something still worth having. Balanced by the
// usual gambling rules:
//   return to player  a ton returns Payback (93 %) of what it costs (fee + the waste's price) on average, in either
//                     economy: the fee is worked out from the active item table. Never a profit, as the waste sells for
//                     5-6$ and stations stock hundreds of tons; Extreme pays 1.5x the fee.
//   tiers             a find is Scrap, Salvage, Valuable, Rare or Exceptional (FindTier: the result page's colours), by
//                     what it is worth in the active table (ScrapFrom ..): every commodity, ore and core the shops stock, so
//                     the pools follow the economy; Rare also a piece of shop gear up to tech level 5; Exceptional goods
//                     worth 5,000$ a ton or more.
//   steady            most of the return is in the common tiers, so a hold rarely comes back poor; the rare finds are the
//                     extra thrill, not the whole return (players' wish: "more rewarding"). The numbers: docs/notes/mining.md.
//   bad-luck caps     ("pity") at most DryCap empty tons in a row, and a rare find at the latest in ton RarePity since the
//                     last; both are part of the return worked out above (not shown to the player).
//   no disguised wins the answer shows the fees paid beside what the finds are worth.
// No blueprints (they come from the story's sellers), no mod items. The rolls come from the game's RunId and the counters
// saved with it (Session.WasteTons / WasteDry / WasteSinceRare): loading a save and sorting again finds the same.

using System.Collections.Generic;
using System.Text;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class WasteProcessing
    {
        public const int Waste = 99;
        /// <summary>What a ton returns on average, as a share of what it costs (Normal; the fee is rounded up to 1$).</summary>
        public const float Payback = 0.93f;
        public const float ExtremeFee = 1.5f;
        public const int GearMaxTech = 5;
        /// <summary>The most empty tons in a row: the next one always finds something.</summary>
        public const int DryCap = 6;
        /// <summary>A rare find at the latest in this ton since the last one.</summary>
        public const int RarePity = 500;

        /// <summary>The worth (middle price a ton, in the active table) where each tier starts: below Scrap's it is junk and
        /// never found (basic food, towels...).</summary>
        public const int ScrapFrom = 25, SalvageFrom = 100, ValuableFrom = 400, RareFrom = 2000, ExceptionalFrom = 5000;

        enum Pool { Nothing, Scrap, Salvage, Valuable, RareGear, RareGoods, Exceptional }

        /// <summary>How good a find is, for the result page (its colour and name).</summary>
        public enum FindTier { None, Scrap, Salvage, Valuable, Rare, Exceptional }
        static FindTier TierOf(Pool pool) => pool switch
        {
            Pool.Scrap => FindTier.Scrap,
            Pool.Salvage => FindTier.Salvage,
            Pool.Valuable => FindTier.Valuable,
            Pool.RareGear or Pool.RareGoods => FindTier.Rare,
            Pool.Exceptional => FindTier.Exceptional,
            _ => FindTier.None,
        };
        /// <summary>A tier's name ("Scrap" .. "Exceptional").</summary>
        public static string TierName(FindTier tier) => tier switch
        {
            FindTier.Scrap => Localization.Extra("wasteTierScrap", "Scrap"),
            FindTier.Salvage => Localization.Extra("wasteTierSalvage", "Salvage"),
            FindTier.Valuable => Localization.Extra("wasteTierValuable", "Valuable"),
            FindTier.Rare => Localization.Extra("wasteTierRare", "Rare"),
            FindTier.Exceptional => Localization.Extra("wasteTierExceptional", "Exceptional"),
            _ => Localization.Extra("wasteTierNothing", "Nothing usable"),
        };

        /// <summary>A prize tier: its weight within its group and the pool it picks from (a find is always one ton, or one
        /// piece of gear).</summary>
        readonly struct Tier
        {
            public readonly int weight;
            public readonly Pool pool;
            public Tier(int weight, Pool pool) { this.weight = weight; this.pool = pool; }
        }

        /// <summary>A rare find in RareChance of 1000 tons, then one of these: a piece of gear, a ton of rare goods or of
        /// an exceptional good.</summary>
        public const int RareChance = 3;
        static readonly Tier[] Rare =
        {
            new Tier(12, Pool.RareGear),
            new Tier(6, Pool.RareGoods),
            new Tier(4, Pool.Exceptional),
        };
        /// <summary>Otherwise one of these; after DryCap empty tons without the first.</summary>
        static readonly Tier[] Common =
        {
            new Tier(640, Pool.Nothing),
            new Tier(210, Pool.Scrap),
            new Tier(110, Pool.Salvage),
            new Tier(40, Pool.Valuable),
        };

        /// <summary>The plant is in Coromesk's orbit (Traffic.AddCampaignStatics: campaign > 0x54, not free play).</summary>
        public static bool PlantBuilt => !Session.FreePlay && Session.CampaignMission > 0x54;

        // ---- the prizes ------------------------------------------------------------------------------------------------

        /// <summary>An item's middle price in this table (what it sells for on average).</summary>
        public static float Value(Database db, int item)
        {
            var it = db.Item(item);
            return it == null ? 0f : (it.minPrice + it.maxPrice) / 2f;
        }

        /// <summary>Goods a sorted heap might still hold: the original's commodities, ores and cores that shops stock
        /// (occurrence > 0, a price), worth Scrap's band or more; not the waste itself. Contraband-sounding goods are in
        /// (drugs, explosives, radioactive goods, organs): the game has no rule against carrying anything. Once the plasma
        /// kit's step is over (campaign > 142, PlasmaFound) the four plasmas too (otherwise only from gas clouds), Red
        /// Plasma volatile in the hold (VolatileCargo: the ship can blow up). Never the K'mirkk Toad Mutagen, the other
        /// volatile good: a story item (bought at 117, handed over at 122) worth 512,500$ in the Android tables.</summary>
        static bool Goods(Database db, ItemData it) =>
            it != null && !it.modded && it.index != Waste && it.minPrice > 0 && Value(db, it.index) >= ScrapFrom
            && (it.categoryId >= 22 && it.categoryId <= 24 && it.occurrence > 0
                || PlasmaFound && it.index >= FirstPlasma && it.index <= RedPlasma);

        public const int FirstPlasma = 201, RedPlasma = 204;
        /// <summary>The plasmas can turn up: the story is past the plasma kit (142's tutorial counts any plasma aboard).</summary>
        public static bool PlasmaFound => !Session.FreePlay && Session.CampaignMission > 142;

        /// <summary>Gear a sorted heap might hold: the original's weapons and equipment that shops stock (occurrence > 0, a
        /// price), up to tech level 5, none built from a blueprint.</summary>
        public static List<int> Gear(Database db)
        {
            var l = new List<int>();
            foreach (var it in db.Items)
            {
                if (it == null || it.modded || (it.categoryId >= 22 && it.categoryId <= 24)) continue;
                if (it.occurrence <= 0 || it.minPrice <= 0 || it.techLevel > GearMaxTech) continue;
                if (it.blueprint != null && it.blueprint.Count > 0) continue;
                l.Add(it.index);
            }
            return l;
        }

        static Database poolsDb;
        static bool poolsPlasma;
        static Dictionary<Pool, List<int>> poolsCached;

        /// <summary>The items each pool picks from in this table, in item order (worked out once per table and plasma
        /// state): the goods by their band, Rare's gear apart.</summary>
        static Dictionary<Pool, List<int>> Pools(Database db)
        {
            if (ReferenceEquals(db, poolsDb) && poolsPlasma == PlasmaFound) return poolsCached;
            poolsPlasma = PlasmaFound;
            var pools = new Dictionary<Pool, List<int>>();
            foreach (Pool p in System.Enum.GetValues(typeof(Pool))) pools[p] = new List<int>();
            foreach (var it in db.Items)
            {
                if (!Goods(db, it)) continue;
                float v = Value(db, it.index);
                var pool = v >= ExceptionalFrom ? Pool.Exceptional : v >= RareFrom ? Pool.RareGoods
                         : v >= ValuableFrom ? Pool.Valuable : v >= SalvageFrom ? Pool.Salvage : Pool.Scrap;
                pools[pool].Add(it.index);
            }
            pools[Pool.RareGear] = Gear(db);
            poolsDb = db;
            return poolsCached = pools;
        }

        /// <summary>The items a tier finds in this table (a copy), by the band it is worth.</summary>
        public static List<int> Finds(Database db, FindTier tier)
        {
            var l = new List<int>();
            foreach (var kv in Pools(db)) if (TierOf(kv.Key) == tier) l.AddRange(kv.Value);
            l.Sort();
            return l;
        }

        /// <summary>What a pick from these tiers finds on average ('skipNothing': without the empty tier); a tier whose pool
        /// is empty in this table is left out, as Pick does.</summary>
        static float Average(Database db, Tier[] tiers, bool skipNothing)
        {
            float ev = 0f;
            int total = 0;
            foreach (var t in tiers)
            {
                if (skipNothing && t.pool == Pool.Nothing) continue;
                var items = Pools(db)[t.pool];
                if (t.pool != Pool.Nothing && items.Count == 0) continue;
                total += t.weight;
                if (items.Count == 0) continue;
                float sum = 0f;
                foreach (int i in items) sum += Value(db, i);
                ev += t.weight * sum / items.Count;
            }
            return total > 0 ? ev / total : 0f;
        }

        // ---- the odds --------------------------------------------------------------------------------------------------

        /// <summary>The long-run odds with both bad-luck caps: what a ton finds on average (middle prices), the share of tons
        /// that find something, the share with a rare find.</summary>
        public readonly struct Odds
        {
            public readonly float value, hitRate, rareRate;
            public Odds(float value, float hitRate, float rareRate) { this.value = value; this.hitRate = hitRate; this.rareRate = rareRate; }
        }

        static Database oddsDb;
        static bool oddsPlasma;
        static Odds oddsCached;

        /// <summary>The steady state of the counters (empty tons in a row 0..DryCap, tons since a rare find 0..RarePity-1),
        /// by power iteration, and what a ton finds in each state. Kept for the table it was worked out from.</summary>
        public static Odds LongRun(Database db)
        {
            if (ReferenceEquals(db, oddsDb) && oddsPlasma == PlasmaFound) return oddsCached;
            oddsPlasma = PlasmaFound;
            float rare = Average(db, Rare, false), common = Average(db, Common, false), forced = Average(db, Common, true);
            float pRare = RareChance / 1000f, pNothing = Weight(Pool.Nothing) / (float)CommonWeight(db, false);
            int D = DryCap + 1, S = RarePity;
            var p = new double[D, S];
            var next = new double[D, S];
            p[0, 0] = 1;
            for (int step = 0; step < 50000; step++)
            {
                System.Array.Clear(next, 0, next.Length);
                for (int d = 0; d < D; d++)
                for (int s = 0; s < S; s++)
                {
                    double here = p[d, s];
                    if (here == 0) continue;
                    double r = s == S - 1 ? 1 : pRare;
                    next[0, 0] += here * r;
                    if (s == S - 1) continue;
                    double n = d == DryCap ? 0 : pNothing;
                    next[0, s + 1] += here * (1 - r) * (1 - n);
                    if (n > 0) next[d + 1, s + 1] += here * (1 - r) * n;
                }
                double change = 0;
                for (int d = 0; d < D; d++) for (int s = 0; s < S; s++) change += System.Math.Abs(next[d, s] - p[d, s]);
                (p, next) = (next, p);
                if (change < 1e-11) break;
            }
            double value = 0, hit = 0, rareRate = 0;
            for (int d = 0; d < D; d++)
            for (int s = 0; s < S; s++)
            {
                double r = s == S - 1 ? 1 : pRare;
                bool force = d == DryCap;
                value += p[d, s] * (r * rare + (1 - r) * (force ? forced : common));
                hit += p[d, s] * (r + (1 - r) * (force ? 1 : 1 - pNothing));
                rareRate += p[d, s] * r;
            }
            oddsDb = db;
            return oddsCached = new Odds((float)value, (float)hit, (float)rareRate);
        }

        static int Weight(Pool pool) { foreach (var t in Common) if (t.pool == pool) return t.weight; return 0; }
        static int CommonWeight(Database db, bool skipNothing)
        {
            int n = 0;
            foreach (var t in Common)
            {
                if (skipNothing && t.pool == Pool.Nothing) continue;
                if (t.pool != Pool.Nothing && Pools(db)[t.pool].Count == 0) continue;
                n += t.weight;
            }
            return n;
        }

        /// <summary>What one ton finds on average in the long run, in this table's middle prices.</summary>
        public static float ExpectedValue(Database db) => LongRun(db).value;

        /// <summary>The fee per ton: what makes a ton return Payback of fee + waste on average, rounded up to 1$ (Extreme
        /// 1.5x).</summary>
        public static int Fee(Database db)
        {
            float fee = ExpectedValue(db) / Payback - Value(db, Waste);
            if (Session.IsExtreme) fee *= ExtremeFee;
            return System.Math.Max(1, (int)System.Math.Ceiling(fee));
        }

        /// <summary>What a ton costs in all: the fee and the waste's middle price.</summary>
        public static float TonCost(Database db) => Fee(db) + Value(db, Waste);

        // ---- sorting ---------------------------------------------------------------------------------------------------

        /// <summary>Space Waste in the hold: the plant's menu lists it.</summary>
        public static bool Offered() => Shop.CargoOf(Waste) > 0;

        /// <summary>The tons sorted now: all of the hold's waste, as much as the credits pay for.</summary>
        public static int Tons(Database db) => System.Math.Max(0, System.Math.Min(Shop.CargoOf(Waste), Session.Credits / Fee(db)));

        /// <summary>FNV-1a of the run id: string.GetHashCode differs between runs of the game.</summary>
        static ulong Hash(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
            return h;
        }

        /// <summary>SplitMix64's finaliser: java.util.Random's first numbers from neighbouring seeds are alike.</summary>
        static long Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (long)(z ^ (z >> 31));
        }

        /// <summary>What ton number 'ton' of this game finds (null: nothing usable; otherwise one ton, or one piece of gear)
        /// and its tier, with the bad-luck counters it starts from and moves on: 'dry' empty tons in a row, 'sinceRare' tons
        /// since a rare find.</summary>
        public static ItemStack Roll(Database db, int ton, ref int dry, ref int sinceRare, out FindTier tier)
        {
            var rnd = new JavaRandom(Mix(Hash(Session.RunId) ^ (ulong)ton));
            bool rare = rnd.NextInt(1000) < RareChance || sinceRare >= RarePity - 1;
            var found = rare ? Pick(db, Rare, false, rnd, out tier) : Pick(db, Common, dry >= DryCap, rnd, out tier);
            sinceRare = rare ? 0 : sinceRare + 1;
            dry = found == null ? dry + 1 : 0;
            return found;
        }

        static ItemStack Pick(Database db, Tier[] tiers, bool skipNothing, JavaRandom rnd, out FindTier tier)
        {
            tier = FindTier.None;
            var pools = Pools(db);
            bool Usable(Tier t) => !(skipNothing && t.pool == Pool.Nothing) && (t.pool == Pool.Nothing || pools[t.pool].Count > 0);
            int total = 0;
            foreach (var t in tiers) if (Usable(t)) total += t.weight;
            if (total == 0) return null;
            int r = rnd.NextInt(total);
            foreach (var t in tiers)
            {
                if (!Usable(t)) continue;
                if (r >= t.weight) { r -= t.weight; continue; }
                if (t.pool == Pool.Nothing) return null;
                var items = pools[t.pool];
                tier = TierOf(t.pool);
                return new ItemStack(items[rnd.NextInt(items.Count)], 1);
            }
            return null;
        }

        /// <summary>One kind of item found: how much in all, its tier, the ton it first turned up in.</summary>
        public class Find
        {
            public int item, amount, firstTon;
            public FindTier tier;
        }

        /// <summary>What a sorting did: each ton's tier in order (None: junk; the result page's squares), the fees paid,
        /// the items found (merged, best tier first, then in the order found).</summary>
        public class Sorting
        {
            public List<FindTier> tons = new List<FindTier>();
            public int fees;
            public List<Find> found = new List<Find>();
            public int Tons => tons.Count;
            /// <summary>The best tier among the tons.</summary>
            public FindTier Best { get { var b = FindTier.None; foreach (var t in tons) if (t > b) b = t; return b; } }
        }

        /// <summary>Yes: sorts the tons, pays the fees, moves the counters on.</summary>
        public static Sorting Process(Database db)
        {
            var result = new Sorting();
            int tons = Tons(db), fee = Fee(db);
            for (int i = 0; i < tons; i++)
            {
                Shop.RemoveFromCargo(Waste, 1);
                Session.Credits -= fee;
                result.fees += fee;
                var p = Roll(db, Session.WasteTons++, ref Session.WasteDry, ref Session.WasteSinceRare, out var tier);
                result.tons.Add(tier);
                if (p == null) continue;
                Shop.AddToCargo(p.item, p.amount);
                var same = result.found.Find(f => f.item == p.item);
                if (same != null) same.amount += p.amount;   // an item is in one pool: always the same tier
                else result.found.Add(new Find { item = p.item, amount = p.amount, tier = tier, firstTon = i });
            }
            // best first; List.Sort isn't stable, so the order found breaks ties
            var order = new List<Find>(result.found);
            result.found.Sort((a, b) => a.tier != b.tier ? b.tier.CompareTo(a.tier) : order.IndexOf(a).CompareTo(order.IndexOf(b)));
            return result;
        }

        /// <summary>What the finds sell for at their middle prices.</summary>
        public static int Worth(Database db, List<Find> found)
        {
            float sum = 0f;
            foreach (var f in found) sum += f.amount * Value(db, f.item);
            return (int)System.Math.Round(sum);
        }

        // ---- the texts -------------------------------------------------------------------------------------------------

        /// <summary>The plant menu's entry (upper case, the menu's style), offered when a ton or more is paid for: the tons
        /// sorted and what it costs, which choosing it pays.</summary>
        public static string MenuEntry(Database db) =>
            string.Format(Localization.Extra("plantSortWaste", "Sort space waste: {0} t for {1}").ToUpperInvariant(),
                Tons(db), GameNames.Credits(Tons(db) * Fee(db)));

        /// <summary>The plant menu's waste section: the fee a ton, then the waste in the hold with what sorting it all costs,
        /// as much as the credits pay for, or that they pay for none; a line without any waste.</summary>
        public static MenuSection Section(Database db)
        {
            int fee = Fee(db), tons = Tons(db), held = Shop.CargoOf(Waste);
            var s = new MenuSection
            {
                heading = Localization.Extra("plantWasteHeading", "Space waste sorting"),
                text = string.Format(Localization.Extra("plantWastePerTon", "{0} a ton. Whatever can still be used is yours."),
                    GameNames.Credits(fee)),
                empty = Localization.Extra("plantWasteNoneHeld", "No Space Waste in your hold."),
            };
            if (held > 0)
                s.rows.Add(new MenuRow
                {
                    item = Waste, amount = held + " t", ready = tons > 0,
                    note = tons == held ? string.Format(Localization.Extra("plantWasteAll", "all for {0}"), GameNames.Credits(tons * fee))
                         : tons > 0 ? string.Format(Localization.Extra("plantWastePart", "{0} t for {1}"), tons, GameNames.Credits(tons * fee))
                         : Localization.Extra("plantWasteNoCredits", "not enough credits"),
                });
            return s;
        }

        /// <summary>The page's title.</summary>
        public static string Title => Localization.Extra("wasteTitle", "Space waste sorted");

        /// <summary>The line over the finds: what was sorted, or that nothing in it could still be used.</summary>
        public static string Heading(Sorting s) => s.found.Count == 0
            ? string.Format(Localization.Extra("wasteNothing", "The plant sorted {0} t of space waste. Nothing in it could still be used."), s.Tons)
            : string.Format(Localization.Extra("wasteFound", "The plant sorted {0} t of space waste and found:"), s.Tons);

        /// <summary>A rare or exceptional find's line over everything, null below.</summary>
        public static string Highlight(Sorting s) => s.Best switch
        {
            FindTier.Exceptional => Localization.Extra("wasteExceptional", "An exceptional find!"),
            FindTier.Rare => Localization.Extra("wasteRare", "A rare find!"),
            _ => null,
        };

        /// <summary>The fees beside what the finds are worth (no find dressed up as a gain).</summary>
        public static string Summary(Database db, Sorting s) =>
            string.Format(Localization.Extra("wasteSummary", "Fees: {0} · found worth about {1}"),
                GameNames.Credits(s.fees), GameNames.Credits(Worth(db, s.found)));

        /// <summary>The answer as plain text (the question window, where the HUD's result page isn't there).</summary>
        public static string Result(Database db, Sorting s)
        {
            var sb = new StringBuilder();
            string highlight = Highlight(s);
            if (highlight != null) sb.Append(highlight).Append('\n');
            sb.Append(Heading(s));
            if (s.found.Count > 0) sb.Append('\n');
            foreach (var f in s.found)
                sb.Append('\n').Append(f.amount).Append("t ").Append(GameNames.Item(f.item)).Append(" · ").Append(TierName(f.tier));
            return sb.Append("\n\n").Append(Summary(db, s)).ToString();
        }

        /// <summary>The line under Space Waste's description once the plant is built.</summary>
        public static string Hint => Localization.Extra("wasteHint", "The Mido Mining Plant at Coromesk sorts space waste for a fee.");
    }
}
