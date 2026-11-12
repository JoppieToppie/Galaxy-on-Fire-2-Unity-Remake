// HazardZones.cs
// Remake (2026-11-03, for mods): runs the orbit's hazard volumes on the player's ship (Flight.Hazards, stations.json
// "orbit.hazards": heat and radiation, after EVERSPACE 2's heat damage volumes and radiation fields) and holds a radiated
// ship's hull under its cap in every orbit until it docks. Entering a volume says so on the HUD ("Heat" / "Radiation");
// each new radiation token says the hull's new limit. 2026-11-06: the volumes are drawn as ES2 draws its fields (World.
// HazardLooks: their particles, the frost and radiation views from inside, the heat's bloom); the frost view follows the
// buildup, the radiation view the next dose. Also 2026-11-06: frost (its buildup freezes the ship: the web's slow, Target.SlowBy, at
// Hazards.FrozenFactor), corrosion (shield-piercing ticks on for 8 s after leaving, as a rift's corrosion field; a System
// Recovery Routine lifts it, PlayerDebuffs) and damage (a steady dps) volumes, after ES2's frost, corrosion and damage fields.

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
        float heatTickMs, radiationMs, frostMs, corrodedMs, corrosionTickMs, corrosionDamage, damageTickMs;
        int inside = -1;
        System.Func<bool> corroded;
        Material[] overlays = new Material[0];
        UnityEngine.Rendering.Volume heatBloom;
        float frozenMs;

        public static HazardZones Attach(SpaceLevel level)
        {
            if (level == null || level.Player == null) return null;
            var h = level.Player.gameObject.AddComponent<HazardZones>();
            h.level = level;
            h.health = level.Health;
            var spec = level.Layout != null && !level.Layout.interstellar ? Modding.ModWorld.OrbitOf(level.Layout.stationIndex) : null;
            if (spec?.hazards != null) h.zones = spec.hazards.ToArray();
            var looks = new GameObject("Hazard volumes");
            looks.transform.SetParent(level.transform, false);
            h.overlays = HazardLooks.Build(looks.transform, h.zones);
            h.heatBloom = HazardLooks.HeatBloom(looks.transform, h.zones);
            h.corroded = () => h.corrodedMs > 0f;
            PlayerDebuffs.Register(h.corroded, () => h.corrodedMs = 0f);   // (a System Recovery Routine lifts it)
            return h;
        }

        void OnDestroy()
        {
            if (corroded != null) PlayerDebuffs.Unregister(corroded);
            foreach (var m in overlays) if (m != null) Destroy(m);
            if (heatBloom != null && heatBloom.sharedProfile != null) Destroy(heatBloom.sharedProfile);
        }


        static float Dps(Modding.ModWorld.HazardSpec z) => z.raiderDps > 0f ? z.raiderDps * NpcTables.RaiderGunDps : z.dps;

        static string Entered(Modding.ModWorld.HazardKind kind) => kind switch
        {
            Modding.ModWorld.HazardKind.Heat => Localization.Extra("hazardHeat", "Heat: the hull is burning"),
            Modding.ModWorld.HazardKind.Radiation => Localization.Extra("hazardRadiation", "Radiation: the hull's limit is falling"),
            Modding.ModWorld.HazardKind.Frost => Localization.Extra("hazardFrost", "Frost: the ship is icing up"),
            Modding.ModWorld.HazardKind.Corrosion => Localization.Extra("hazardCorrosion", "Corrosion: the hull is being eaten away"),
            _ => Localization.Extra("hazardDamage", "Danger: the hull is taking damage"),
        };

        void Update()
        {
            if (health == null || health.Dead || health.Hp == null) return;
            float dtMs = Time.deltaTime * 1000f;
            if (dtMs <= 0f) return;
            var pos = transform.position;
            int now = -1;
            bool hot = false, radiated = false, frost = false, corrosive = false, harmful = false;
            float heatDps = 0f, damageDps = 0f;
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i];
                var c = OrbitLayout.ToUnity(z.centre);
                float r = z.radius * OrbitLayout.MetersPerUnit;
                float d = (pos - c).magnitude;
                if (d >= r) continue;
                if (now < 0) now = i;
                switch (z.kind)
                {
                    case Modding.ModWorld.HazardKind.Heat: hot = true; heatDps += Hazards.HeatDamage(Dps(z), d, r, 1000f); break;
                    case Modding.ModWorld.HazardKind.Radiation: radiated = true; break;
                    case Modding.ModWorld.HazardKind.Frost: frost = true; break;
                    case Modding.ModWorld.HazardKind.Corrosion: corrosive = true; corrosionDamage = Mathf.Max(corrosionDamage, Dps(z)); break;
                    default: harmful = true; damageDps += Dps(z); break;
                }
            }
            if (now != inside && now >= 0) level.Navigation?.Post(Entered(zones[now].kind));
            inside = now;
            // frost: the buildup freezes the ship (its moves at FrozenFactor) for FrozenMs, then builds again
            frostMs = Hazards.FrostStep(frostMs, dtMs, frost, out bool frozen);
            if (frozen) frozenMs = Hazards.FrozenMs;
            else if (frozenMs > 0f) frozenMs -= dtMs;
            // the views from inside: the frost with its buildup (whole while frozen), the radiation toward the next dose
            float frostView = frozenMs > 0f ? 1f : frostMs / Hazards.FrostBuildupMs;
            float radiationView = 0.3f + 0.7f * Mathf.Clamp01(radiationMs / Hazards.RadiationBuildupMs);
            for (int i = 0; i < zones.Length && i < overlays.Length; i++)
                if (overlays[i] != null) overlays[i].SetFloat("_Amount", zones[i].kind == Modding.ModWorld.HazardKind.Frost ? frostView : radiationView);
            var cam = Camera.main;
            if (heatBloom != null && cam != null) heatBloom.weight = HazardLooks.HeatDepth(zones, cam.transform.position);
            if (frozen && !health.invulnerable)
            {
                health.Target.SlowBy?.Invoke(Hazards.FrozenMs, Hazards.FrozenFactor, false);
                level.Navigation?.Post(Localization.Extra("hazardFrozen", "Frozen: the ship has lost control"));
            }
            // corrosion: on for its 8 s after leaving, a tick past the shield every 0.95 s
            if (corrosive) corrodedMs = RiftRun.CorrosionDebuffS * 1000f;
            if (corrodedMs > 0f)
            {
                corrodedMs -= dtMs;
                corrosionTickMs += dtMs;
                while (corrosionTickMs >= RiftRun.CorrosionTickS * 1000f)
                {
                    corrosionTickMs -= RiftRun.CorrosionTickS * 1000f;
                    if (!health.invulnerable) health.Target.Damage(corrosionDamage, true, Vector3.zero, 0f);
                }
            }
            else { corrosionTickMs = 0f; corrosionDamage = 0f; }
            if (harmful)
            {
                damageTickMs += dtMs;
                while (damageTickMs >= Hazards.DamageTickMs)
                {
                    damageTickMs -= Hazards.DamageTickMs;
                    if (!health.invulnerable) health.Target.Damage(damageDps * Hazards.DamageTickMs / 1000f, true, Vector3.zero);
                }
            }
            else damageTickMs = 0f;
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
