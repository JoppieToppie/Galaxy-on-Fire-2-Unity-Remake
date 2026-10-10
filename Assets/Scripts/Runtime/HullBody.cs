// HullBody.cs
// Remake: an object's collision hull, made of Unity MeshColliders on its own model's meshes (HullCollision.Attach), on
// the model's root with a kinematic Rigidbody so a moving or animated hull stays cheap for PhysX. It links the colliders to
// the Target they belong to (shots: Gun) and to the Obstacle the player slides along (PlayerCollision); either may be null
// (a station is never hit, a fighter is no obstacle). The model hidden (a dead ship's, a cloaked proxy's) takes its
// colliders with it.

using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Flight
{
    public class HullBody : MonoBehaviour
    {
        /// <summary>What a shot on this hull hits; null = nothing (shots pass, like the original's stations).</summary>
        [System.NonSerialized] public Target target;
        /// <summary>What the player is pushed out of; null = the player flies through (fighters, like the original).</summary>
        [System.NonSerialized] public Obstacle obstacle;
        [System.NonSerialized] public readonly List<MeshCollider> colliders = new List<MeshCollider>();

        /// <summary>Its colliders exist and take part (the model shown).</summary>
        public bool Active => isActiveAndEnabled && colliders.Count > 0;

        void OnDestroy()
        {
            foreach (var c in colliders) if (c != null) HullCollision.Forget(c);
        }
    }
}
