// CruiseEncounterRules.cs
// Where a cruise drive's encounters (Flight.CruiseEncounters) may come, here in GoF2.Game beside PirateEventRules so the
// tests and the HUD share them: not the special orbits (the deep science and Valkyrie stations, Thynome, the Kaamo Club,
// the dropship orbits), the Void and supernova systems 27 / 32 / 33, Loma's black market 25, the alien orbit, a system
// with mods' ships only. The level adds its own state (a scripted orbit, a multiplayer orbit another player runs).

namespace GoF2Remake.Data
{
    public static class CruiseEncounterRules
    {
        public static bool Allowed(Database db, int station, int system, bool alienOrbit)
        {
            if (db == null || alienOrbit) return false;
            if (station == 10 || station == 100 || station == 101 || station == 108 || (station >= 102 && station <= 104)) return false;
            if (system == 25 || system == 27 || system == 32 || system == 33) return false;
            if (Modding.ModWorld.ModTrafficOnly(system)) return false;
            return db.Systems.Find(s => s.index == system) != null;
        }
    }
}
