// RebuiltPlanetOwner.cs
// Remake: a rebuilt planet's drawing (World.RebuiltPlanets.Spawn) takes its own materials with it when it goes.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.World
{
    /// <summary>Destroys a rebuilt planet's own materials with it.</summary>
    public class RebuiltPlanetOwner : MonoBehaviour
    {
        public List<Material> materials;
        void OnDestroy() { if (materials != null) foreach (var m in materials) if (m != null) Destroy(m); }
    }
}
