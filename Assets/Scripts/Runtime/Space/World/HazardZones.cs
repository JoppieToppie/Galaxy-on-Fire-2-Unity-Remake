// HazardZones.cs
// Remake (2026-11-03, for mods): runs the orbit's hazard volumes on the player's ship (Flight.Hazards, stations.json
// "orbit.hazards": heat and radiation, after EVERSPACE 2's heat damage volumes and radiation fields) and holds a radiated
// ship's hull under its cap in every orbit until it docks. Entering a volume says so on the HUD ("Heat" / "Radiation");
// each new radiation token says the hull's new limit. The volumes aren't drawn (ES2 shows them by its post-process
// tint and the planet's surface).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class HazardZones : MonoBehaviour
    {
        SpaceLevel level;
        PlayerHealth health;
        Modding.ModWorld.HazardSpec[] zones = new Modding.ModWorld.HazardSpec[0];
        float heatTickMs, radiationMs;
        int inside = -1;

        public static HazardZones Attach(SpaceLevel level)
        {
            if (level == null || level.Player == null) return null;
            var h = level.Player.gameObject.AddComponent<HazardZones>();
            h.level = level;
            h.health = level.Health;
            var spec = level.Layout != null && !level.Layout.interstellar ? Modding.ModWorld.OrbitOf(level.Layout.stationIndex) : null;
            if (spec?.hazards != null) h.zones = spec.hazards.ToArray();
            return h;
        }

        void Update()
        {
            if (health == null || health.Dead || health.Hp == null) return;
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            var pos = transform.position;
            int now = -1;
            bool hot = false, radiated = false;
            float heatDps = 0f;
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                var c = OrbitLayout.ToUnity(z.centre);
                float r = z.radius * OrbitLayout.MetersPerUnit;
                float d = (pos - c).magnitude;
                if (d >= r) continue;
                if (now < 0) now = i;
                if (z.kind == Modding.ModWorld.HazardKind.Heat) { hot = true; heatDps += Hazards.HeatDamage(z.raiderDps > 0f ? z.raiderDps * NpcTables.RaiderGunDps : z.dps, d, r, 1000f); }
                else radiated = true;
            }
            if (now != inside && now >= 0)
                level.Navigation?.Post(zones[now].kind == Modding.ModWorld.HazardKind.Heat
                    ? Localization.Extra("hazardHeat", "Heat: the hull is burning")
                    : Localization.Extra("hazardRadiation", "Radiation: the hull's limit is falling"));
            inside = now;
            if (hot)
            {
                heatTickMs += dtMs;
                while (heatTickMs >= Hazards.HeatTickMs)
                {
                    heatTickMs -= Hazards.HeatTickMs;
                    if (!health.invulnerable) health.Target.Damage(heatDps * Hazards.HeatTickMs / 1000f, true, Vector3.zero);
                }
            }
            else heatTickMs = 0f;
            if (radiated)
            {
                radiationMs += dtMs;
                if (radiationMs >= Hazards.RadiationBuildupMs)
                {
                    radiationMs = 0f;
                    if (Hazards.AddRadiation())
                        level.Navigation?.Post(string.Format(Localization.Extra("hazardRadiated", "Radiated: hull limit {0} %"),
                                                             Mathf.RoundToInt(Hazards.RadiationFactor(Hazards.RadiationTokens) * 100f)));
                }
            }
            else radiationMs = 0f;
            // the radiated hull's cap, in any orbit until docking
            if (Hazards.RadiationTokens > 0)
            {
                var hp = health.Hp;
                int cap = Mathf.Max(1, Mathf.FloorToInt(hp.maxHull * Hazards.RadiationFactor(Hazards.RadiationTokens)));
                if (hp.hull > cap) { hp.hull = cap; health.Target.hp = cap; }
            }
        }
    }
}
