// AcidSackBase.cs
// Remake (2026-11-06, for mods): an acid pouch on its stalk (stations.json "orbit.acidSacks"), after EVERSPACE 2's
// BP_AcidSack_Base and BP_AcidSack ("Acid Pouch"): the base holds a pouch (World.Portable kind AcidSack) that every
// SpitIntervalMin..Max (8-13 s) charges for SpitChargeDuration (2 s) and spits a corrosion field (its ConditionField,
// BP_CorrosionField: Corrosive Death's small RiftField on the player, World.ShipCorrosion on the NPC ships). The pilot may
// pull it off with the action key (GRAB, World.CarryPuzzle) and throw it (primary fire): it bursts on the first ship it comes
// near (World.Portable, its BP_Explosion_Corrosion and the same field). A taken pouch grows again on its base
// MinRegenerationTime..Max (4-6 s) plus RegenerationDuration (2 s) later.

using GoF2Remake.Flight;
using UnityEngine;

namespace GoF2Remake.World
{
    public class AcidSackBase : MonoBehaviour
    {
        public const float SpitMinS = 8f, SpitMaxS = 13f, SpitChargeS = 2f, RegrowMinS = 4f, RegrowMaxS = 6f, GrowS = 2f, StalkM = 6f;

        SpaceLevel level;
        Portable sack;
        float spitS, regrowS = -1f, chargeS = -1f;

        /// <summary>Lays the orbit's acid pouches; how many.</summary>
        public static int LayOrbit(SpaceLevel level)
        {
            var spec = level != null && level.Layout != null && !level.Layout.interstellar ? Modding.ModWorld.OrbitOf(level.Layout.stationIndex)?.acidSacks : null;
            if (spec == null) return 0;
            var root = new GameObject("Acid pouches");
            root.transform.SetParent(level.transform, false);
            foreach (var p in spec)
            {
                var go = RiftHazards.Sphere(root.transform, OrbitLayout.ToUnity(p), StalkM, new Color(0.25f, 0.45f, 0.15f, 0.9f), "Acid pouch base");
                var b = go.AddComponent<AcidSackBase>();
                b.level = level;
                b.Grow();
            }
            return spec.Count;
        }

        /// <summary>A corrosion field at 'at': the rift's on the player, ShipCorrosion on the NPC ships.</summary>
        public static void Corrosion(SpaceLevel level, Transform parent, Vector3 at)
        {
            float tick = RiftRun.CorrosionTickSeconds * NpcTables.RaiderGunDps;
            var f = RiftField.Make(level, parent, RiftField.Kind.Corrosion, at, RiftRun.CorrosionSmallM, RiftRun.CorrosionSmallS, tick);
            ShipCorrosion.Add(f.gameObject, RiftRun.CorrosionSmallM, RiftRun.CorrosionSmallS, tick);
        }

        void Grow()
        {
            sack = Portable.Make(transform.parent, transform.position + transform.up * (StalkM + Portable.RadiusM), Portable.Kind.AcidSack);
            sack.Level = level;
            spitS = Random.Range(SpitMinS, SpitMaxS);
            regrowS = -1f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool held = sack != null && sack.Carrier == null && !sack.Thrown && (sack.transform.position - transform.position).sqrMagnitude < (StalkM * 4f) * (StalkM * 4f);
            if (!held)
            {
                if (sack != null) sack = null;   // (taken: it's the pilot's now)
                if (regrowS < 0f) regrowS = Random.Range(RegrowMinS, RegrowMaxS) + GrowS;
                if ((regrowS -= dt) <= 0f) Grow();
                return;
            }
            if (chargeS >= 0f)
            {
                if ((chargeS += dt) < SpitChargeS) return;
                chargeS = -1f;
                Corrosion(level, transform.parent, sack.transform.position);   // (the spit)
                spitS = Random.Range(SpitMinS, SpitMaxS);
                return;
            }
            if ((spitS -= dt) <= 0f) chargeS = 0f;
        }
    }
}
