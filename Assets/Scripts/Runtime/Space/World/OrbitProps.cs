// OrbitProps.cs
// Remake (2026-11-06, for mods): an orbit's static props (stations.json "orbit.props", Modding.ModWorld.PropSpec), first for
// EVERSPACE 2's placed unique asteroids (its levels' StaticMeshActors of SM_Asteroid_Rock_*_Unique_*: landmark rocks up to
// kilometres across). Each the mod's model (loaded with the mod crates, ModCrates) at its place, turned and fitted to its size
// as the wreck hulls are (WreckField.SpawnHulks), a solid box in its own frame (Obstacle) the ship bumps off.

using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public static class OrbitProps
    {
        /// <summary>Places the orbit's props; how many.</summary>
        public static int Spawn(OrbitLayout layout)
        {
            var spec = layout != null ? Modding.ModWorld.OrbitOf(layout.stationIndex) : null;
            if (spec?.props == null || spec.owner == null || !Modding.ModCrates.Ready) return 0;
            GameObject root = null;
            int made = 0;
            foreach (var p in spec.props)
            {
                if (root == null) root = new GameObject("Orbit props");
                var go = Modding.ModCrates.InstantiateFitted(spec.owner.Id + "|" + p.model, OrbitLayout.ToUnity(p.at), Quaternion.Euler(p.rotation),
                                                             p.size * OrbitLayout.MetersPerUnit, root.transform);
                if (go == null) continue;
                go.name = "Prop";
                var b = Modding.ModCrates.LocalBounds(go);
                var o = go.AddComponent<Obstacle>();
                o.rotates = true;
                o.volumes.Add(CollisionVolume.Box(b.center, b.extents));
                made++;
            }
            if (made == 0 && root != null) Object.Destroy(root);
            return made;
        }
    }
}
