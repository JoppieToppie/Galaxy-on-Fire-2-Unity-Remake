// CloakedContainers.cs
// Remake (2026-11-06, for mods): finding an orbit's cloaked containers (stations.json "orbit.containers.cloaked",
// World.LootContainer's cloaked ones), after EVERSPACE 2's BP_Activity_InvisibleContainer / BP_Activity_Detect_Base and
// BP_InvisibleItemContainer (es2_re kismet): within the activity's DistanceToPlayer (200 000 cm, at the orbit places' own
// scale: NearbyM) its "CONTAINER NEARBY" warning; within the container's InteractRadius (4000 cm x the ES2 ships' 3.6, as
// LootContainer.OpenRangeM: RevealM) its marker comes on (ShowMarker: SetHiddenInHUD false), and the action key opens it;
// opened, "CONTAINER FOUND" (FoundContainer). On the player's ship, attached with the hazards.

using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.World
{
    public class CloakedContainers : MonoBehaviour
    {
        public const float NearbyM = 2000f, RevealM = 40f * 3.6f;

        SpaceLevel level;
        readonly HashSet<LootContainer> warned = new HashSet<LootContainer>(), found = new HashSet<LootContainer>();

        public static CloakedContainers Attach(SpaceLevel level)
        {
            if (level == null || level.Player == null || !LootContainer.All.Exists(c => c != null && c.Cloaked)) return null;
            var w = level.Player.gameObject.AddComponent<CloakedContainers>();
            w.level = level;
            return w;
        }

        /// <summary>What the ship at 'distance' metres from a closed cloaked container sets off: 2 revealed, 1 the warning, 0 none.</summary>
        public static int Stage(float distance) => distance < RevealM ? 2 : distance < NearbyM ? 1 : 0;

        void Update()
        {
            var pos = transform.position;
            foreach (var c in LootContainer.All)
            {
                if (c == null || !c.Cloaked) continue;
                if (c.Opened)
                {
                    if (found.Add(c)) level?.Navigation?.Post(Localization.Extra("containerFound", "Container found"));
                    continue;
                }
                int stage = Stage((c.transform.position - pos).magnitude);
                if (stage >= 1 && warned.Add(c) && !c.Revealed) level?.Navigation?.Post(Localization.Extra("containerNearby", "Container nearby"));
                if (stage == 2) c.Reveal();
            }
        }
    }
}
