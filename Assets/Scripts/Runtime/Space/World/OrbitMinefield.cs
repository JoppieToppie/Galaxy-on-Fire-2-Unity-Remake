// OrbitMinefield.cs
// Remake (2026-11-06, for mods): an orbit's minefields (stations.json "orbit.minefields", Modding.ModWorld.MinefieldSpec), after
// EVERSPACE 2's BP_Minefield (Radius 25 000 cm, AverageMineDistance 12 000 cm, MaxMineAmount 40) and its proximity mines
// (BP_ProximityMine_Base / Web / Cryo / Thermal / Corrosion): still mines scattered through a sphere that arm, blink for a short
// fuse once the player's ship comes near and blow up, with the rift's mines' numbers (Flight.RiftRun.Mine*). Each kind adds to
// the blast as ES2's do: a web mine webs the ship (NpcRoles.WebSpeedFactor for 4 s, its WebDuration), a cryo mine freezes it
// (Hazards.FrozenMs / FrozenFactor), a corrosion mine leaves a corrosion field (RiftField, Corrosive Death's small one), a
// thermal one burns wider (1.5 x the blast radius). They hurt the player's ship only (ES2's minefields are its outlaws').
// Mines add danger, so a field is laid only where its spec allows: a system of security maxSecurity or lower, a player of rank
// minRank or more, and a roll under 'share' each visit (GoF2's difficulty stays as it was for the rest).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public static class OrbitMinefield
    {
        /// <summary>A minefield's mine's colour by kind.</summary>
        public static Color ColorOf(Modding.ModWorld.MinefieldSpec.Kind kind) => kind switch
        {
            Modding.ModWorld.MinefieldSpec.Kind.Web => new Color(0.7f, 0.4f, 1f, 0.85f),
            Modding.ModWorld.MinefieldSpec.Kind.Cryo => new Color(0.4f, 0.85f, 1f, 0.85f),
            Modding.ModWorld.MinefieldSpec.Kind.Thermal => new Color(1f, 0.55f, 0.1f, 0.85f),
            Modding.ModWorld.MinefieldSpec.Kind.Corrosion => new Color(0.4f, 1f, 0.2f, 0.85f),
            _ => new Color(1f, 0.1f, 0.1f, 0.85f),
        };

        /// <summary>A mine's blast in seconds of a raider's fire: the spec's, else ES2's BaseDamage (kinetic + energy) over a
        /// raider's 55 a second (BP_Minefield 60 + 60, _Web 10 + 5, _Cryo 90 + 30).</summary>
        public static float RaiderSeconds(Modding.ModWorld.MinefieldSpec spec) =>
            spec.raiderSeconds >= 0f ? spec.raiderSeconds : spec.kind == Modding.ModWorld.MinefieldSpec.Kind.Web ? 15f / 55f : 120f / 55f;

        /// <summary>Whether a minefield is laid this visit: the system's security, the player's rank and the roll (0..1).</summary>
        public static bool Laid(Modding.ModWorld.MinefieldSpec spec, int security, int rank, float roll) =>
            spec != null && security <= spec.maxSecurity && rank >= spec.minRank && roll < spec.share;

        /// <summary>'count' places within 'radius' of 'centre', no two closer than 'spacing' where that fits (a few tries each).</summary>
        public static List<Vector3> Places(Vector3 centre, float radius, int count, float spacing, System.Random rng)
        {
            var list = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
            {
                Vector3 best = centre;
                float bestGap = -1f;
                for (int t = 0; t < 8; t++)
                {
                    // uniform in the ball: a direction and the cube root of the radius share
                    var dir = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
                    if (dir.sqrMagnitude < 1e-4f || dir.sqrMagnitude > 1f) { t--; continue; }
                    var p = centre + dir.normalized * radius * Mathf.Pow((float)rng.NextDouble(), 1f / 3f);
                    float gap = float.MaxValue;
                    foreach (var q in list) gap = Mathf.Min(gap, (q - p).magnitude);
                    if (gap > bestGap) { best = p; bestGap = gap; }
                    if (gap >= spacing) break;
                }
                list.Add(best);
            }
            return list;
        }

        /// <summary>Lays the orbit's minefields that are allowed this visit; returns the mines laid.</summary>
        public static int Attach(SpaceLevel level)
        {
            if (level == null || level.Layout == null || level.Layout.interstellar) return 0;
            var specs = Modding.ModWorld.OrbitOf(level.Layout.stationIndex)?.minefields;
            if (specs == null || specs.Count == 0) return 0;
            int security = level.Database?.Systems.Find(s => s.index == level.Layout.systemIndex)?.securityLevel ?? 3;
            float dps = NpcTables.RaiderGunDps;
            int laid = 0;
            GameObject root = null;
            foreach (var spec in specs)
            {
                if (!Laid(spec, security, Session.Rank, Random.value)) continue;
                if (root == null) { root = new GameObject("Orbit minefields"); root.transform.SetParent(level.transform, false); }
                var centre = OrbitLayout.ToUnity(spec.centre);
                float radiusM = spec.radius * OrbitLayout.MetersPerUnit;
                // ES2's AverageMineDistance over Radius (12 000 / 25 000) as the spacing's share of the field
                float spacing = radiusM * 0.48f * Mathf.Pow(40f / spec.count, 1f / 3f);
                foreach (var at in Places(centre, radiusM, spec.count, spacing, new System.Random(Random.Range(int.MinValue, int.MaxValue))))
                {
                    OrbitMine.Lay(level, root.transform, at, spec.kind, RaiderSeconds(spec) * dps);
                    laid++;
                }
            }
            if (laid > 0) Debug.Log($"OrbitMinefield: {laid} mines laid (security {security}, rank {Session.Rank})");
            return laid;
        }
    }
}
