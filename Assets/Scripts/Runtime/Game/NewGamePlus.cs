// NewGamePlus.cs
// Remake (GitHub #4): New Game+. A save whose campaign is finished (the main game past 44, Valkyrie past 83, Supernova at
// its last step 162) can start a new game that keeps the pilot's wealth: the credits, the unlocked blueprints and the
// medals carry over, the Kaamo Club is owned, and the old Kaamo storage is kept with the old ship (with its mods), its
// equipment and its cargo parked in it (the story's own items stay behind). The story, the map, standings, missions and
// the starting ship are a new game's.
// Plain C#: MainMenu offers it on the campaign panel and applies it after the campaign's first step is set up.

using System;
using System.Collections.Generic;
using System.IO;

namespace GoF2Remake.Data
{
    public static class NewGamePlus
    {
        /// <summary>The save's campaign is played to its end.</summary>
        public static bool Finished(SaveData s)
        {
            if (s == null || s.freePlay) return false;
            switch ((Campaign)s.campaign)
            {
                case Campaign.Supernova: return s.campaignMission >= Story.LastIndex;
                case Campaign.Valkyrie: return s.campaignMission >= Story.Dlc1WonIndex;
                default: return s.campaignMission >= Story.GameWonIndex;
            }
        }

        /// <summary>The most recently written finished save, null = none.</summary>
        public static SaveData FindFinished(out int slot)
        {
            slot = -1;
            SaveData best = null;
            DateTime newest = DateTime.MinValue;
            for (int i = 0; i < SaveGame.SlotCount; i++)
            {
                if (!SaveGame.Exists(i)) continue;
                var s = SaveGame.Preview(i);
                if (!Finished(s)) continue;
                var t = File.GetLastWriteTimeUtc(SaveGame.SlotPath(i));
                if (t > newest) { newest = t; best = s; slot = i; }
            }
            return best;
        }

        /// <summary>The better of two medal grades: 1 gold, 2 silver, 3 bronze, 0 none.</summary>
        static int BetterGrade(int a, int b) => a == 0 ? b : b == 0 ? a : Math.Min(a, b);

        /// <summary>Carries 'old' over into the new game just set up (after Story.StartCampaign).</summary>
        public static void Apply(SaveData old)
        {
            if (old == null) return;
            Session.Credits += Math.Max(0, old.credits);
            if (old.unlockedBlueprints != null) foreach (int b in old.unlockedBlueprints) Session.UnlockedBlueprints.Add(b);
            if (old.medals != null && old.medals.Length == Session.Medals.Length)
                for (int i = 0; i < old.medals.Length; i++) Session.Medals[i] = BetterGrade(Session.Medals[i], old.medals[i]);

            // The Kaamo Club, owned, with the old storage and the old ship, equipment and cargo in it.
            var story = new HashSet<int>(old.unsaleable ?? new List<int>());
            var items = new List<ItemStack>();
            void AddAll(List<ItemStack> list)
            {
                if (list == null) return;
                foreach (var st in list)
                {
                    if (st == null || st.amount <= 0 || story.Contains(st.item)) continue;
                    var have = items.Find(x => x.item == st.item);
                    if (have != null) have.amount += st.amount; else items.Add(new ItemStack(st.item, st.amount));
                }
            }
            AddAll(old.kaamoItems);
            AddAll(old.equipment);
            AddAll(old.cargo);
            var ships = new List<StoredShip>();
            if (old.kaamoShips != null) foreach (var k in old.kaamoShips) if (k != null && !ships.Exists(x => x.ship == k.ship)) ships.Add(k);
            if (old.ship >= 0 && !ships.Exists(x => x.ship == old.ship))
                ships.Add(new StoredShip(old.ship, old.ship < Shop.ShipRace.Length ? Shop.ShipRace[old.ship] : 0, new List<int>(old.shipMods ?? new List<int>())));
            Session.KaamoState = 3;
            Session.KaamoVip = old.kaamoVip;   // remake: the VIP Card stays with the club
            Session.KaamoItems = items;
            Session.KaamoShips = ships;
        }
    }
}
