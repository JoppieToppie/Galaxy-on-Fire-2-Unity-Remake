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
        /// <summary>Orbit of an independently built gate; -1 for all other scenery.</summary>
        [System.NonSerialized] public int playerJumpgateOrbit = -1;
        [System.NonSerialized] public int playerStationOrbit = -1;
        [System.NonSerialized] public int orbitalStructureId;
        /// <summary>What the player is pushed out of; null = the player flies through (fighters, like the original).</summary>
        [System.NonSerialized] public Obstacle obstacle;
        [System.NonSerialized] public readonly List<MeshCollider> colliders = new List<MeshCollider>();
        /// <summary>A ship a player can fly (a fighter: an NPC's, the player's own, another player's; not a freighter, the
        /// battleship, a capital ship, a station, a turret or an object): with the Gameplay option "Ship collision" on
        /// Mixed (Settings.ShipCollision, a session rule) its hull takes no part (Collides), so shots hit it on the original's
        /// hit cube and the player meets another player's ship on its collision sphere again.</summary>
        [System.NonSerialized] public bool flyableShip;

        /// <summary>Its colliders exist and take part (the model shown).</summary>
        public bool Active => isActiveAndEnabled && colliders.Count > 0;

        /// <summary>Shots and the player's ship meet this hull: active, and taken by the ship collision mode (Exact: every
        /// hull; Mixed: not a flyable ship's; Original: none, HullCollision.Takes).</summary>
        public bool Collides => Active && (HullCollision.IsForcedExact(this) || HullCollision.Takes(flyableShip));

        /// <summary>NPC navigation uses obstacle geometry regardless of the player's ship-collision preference.</summary>
        public bool NavigationActive => Active && obstacle != null;

        void OnDestroy()
        {
            HullCollision.ForgetBody(this);
            foreach (var c in colliders) if (c != null) HullCollision.Forget(c);
        }
    }
}
