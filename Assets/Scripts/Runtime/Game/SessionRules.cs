// SessionRules.cs
// The gameplay rules a multiplayer session decides for everyone (NetRules, GoF2.Net): their bits, here so the options
// (Settings) can ask for the session's value through GameHooks.SessionRule.

namespace GoF2Remake.Data
{
    public static class SessionRules
    {
        public const int CloakLosesPursuers = 1, PirateEvents = 2, CapitalShips = 4, InformerOriginalRule = 8,
                         KaamoStacking = 16, KaamoKeepsEquipment = 32, CloakBay = 64, EmpBreaksCloak = 128,
                         SmarterNpcTargeting = 256, PreciseShipCollision = 512, OriginalCollision = 1024,
                         DogfightOriginal = 2048, DogfightEvery = 4096, Es2Aiming = 8192, DogfightEs2 = 16384, Es2NpcFlight = 32768;

        /// <summary>Remake (the GoF3 roadmap's G6; ES2's added 2026-11-06): how NPC fighters fight (Settings.NpcDogfight,
        /// Flight.Gof3Dogfight / Es2Dogfight), three bits: DogfightOriginal = GoF2's everywhere, DogfightEvery = GoF3's for every
        /// fighter, DogfightEs2 = EVERSPACE 2's for every fighter, none = the hulls' own (ships.json "traffic": { "ai": "gof3" /
        /// "es2" }; the default, so a session without the bits has it).</summary>
        public const int DogfightOriginalMode = 0, DogfightModHulls = 1, DogfightAll = 2, DogfightEs2All = 3, DefaultDogfight = DogfightModHulls;

        public static int DogfightOf(int rules) =>
            (rules & DogfightOriginal) != 0 ? DogfightOriginalMode : (rules & DogfightEs2) != 0 ? DogfightEs2All
            : (rules & DogfightEvery) != 0 ? DogfightAll : DogfightModHulls;

        public static int WithDogfight(int rules, int mode)
        {
            rules &= ~(DogfightOriginal | DogfightEvery | DogfightEs2);
            if (mode == DogfightOriginalMode) rules |= DogfightOriginal;
            else if (mode == DogfightAll) rules |= DogfightEvery;
            else if (mode == DogfightEs2All) rules |= DogfightEs2;
            return rules;
        }

        /// <summary>Remake: the ship collision mode (Settings.ShipCollision), carried in sessions by two bits:
        /// PreciseShipCollision = Exact, OriginalCollision = Original (it wins), neither = Mixed.
        /// Exact: every hull on its real shape (Flight.HullCollision). Mixed (default since 2026-10-17): the ships a player
        /// can fly on the original's hit cube and sphere, everything else on its real shape. Original: no mesh collision,
        /// every object on the original's boxes, spheres and cubes.</summary>
        public const int CollisionExact = 0, CollisionMixed = 1, CollisionOriginal = 2, DefaultCollision = CollisionMixed;

        public static int CollisionOf(int rules) =>
            (rules & OriginalCollision) != 0 ? CollisionOriginal : (rules & PreciseShipCollision) != 0 ? CollisionExact : CollisionMixed;

        public static int WithCollision(int rules, int mode)
        {
            rules &= ~(PreciseShipCollision | OriginalCollision);
            if (mode == CollisionExact) rules |= PreciseShipCollision;
            else if (mode == CollisionOriginal) rules |= OriginalCollision;
            return rules;
        }

        /// <summary>A collision mode's name ("exact" / "precise", "mixed", "original" / "gof2"), -1 = none of them.</summary>
        public static int ParseCollision(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "exact": case "precise": case "mesh": case "0": return CollisionExact;
                case "mixed": case "1": return CollisionMixed;
                case "original": case "gof2": case "2": return CollisionOriginal;
                default: return -1;
            }
        }

        public static string CollisionText(int mode) => mode == CollisionExact ? "exact" : mode == CollisionOriginal ? "original" : "mixed";

        /// <summary>Remake: the highest level of one Kaamo Club upgrade on a ship while stacking is on (Settings.KaamoMaxLevel,
        /// Session.MaxModLevel); a number, so the session carries it beside the bits (NetRules.HostKaamoLevel). 5 by default; 10 was the
        /// players' feedback of 2026-10 (45 equipment slots on a Cronus uncapped); 1 would be stacking off.</summary>
        public const int DefaultKaamoLevel = 5, MinKaamoLevel = 2, MaxKaamoLevel = 20;

        public static int ClampKaamoLevel(int level) => level < MinKaamoLevel ? MinKaamoLevel : level > MaxKaamoLevel ? MaxKaamoLevel : level;

        /// <summary>Remake: the flight model a session flies (NetRules.HostFlight): FlightStyles.Original ("gof2", the game's;
        /// one model for everyone keeps PvP fair: players flying the other one had other turn rates, strafing and drift),
        /// FlightStyles.Free ("es2", EVERSPACE 2 style) for everyone, or PlayerFlight ("player" / "any" / "unrestricted") =
        /// each player's own option, the default since 2026-10-17 (the user: the players choose the flight model they want;
        /// it was gof2).</summary>
        public const int PlayerFlight = -1, DefaultFlight = PlayerFlight;

        /// <summary>A flight model's name ("gof2" / "original", "es2" / "free", "player" / "any" / "choice" / "unrestricted"),
        /// -2 = none of them.</summary>
        public static int ParseFlight(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "gof2": case "original": case "0": return FlightStyles.Original;
                case "free": case "es2": case "everspace": case "1": return FlightStyles.Free;
                case "player": case "any": case "choice": case "unrestricted": case "-1": return PlayerFlight;
                default: return -2;
            }
        }

        public static string FlightText(int style) => style == FlightStyles.Free ? "es2" : style == PlayerFlight ? "player" : "gof2";
    }
}
