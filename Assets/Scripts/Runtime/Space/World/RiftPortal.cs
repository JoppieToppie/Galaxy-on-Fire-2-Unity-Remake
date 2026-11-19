// RiftPortal.cs
// Remake (2026-11-11): a rift look's portal (Modding.ModEncounters.RiftLook "portal", World.RiftEncounter) opening at a
// stage's end where the stage was fought, as EVERSPACE 2's rift stages end in portals to the next location: the look's model
// (fitted to its size, turned to face the camera) grows open, holds while the stage's survivors go through, and closes.

using UnityEngine;

namespace GoF2Remake.World
{
    public class RiftPortal : MonoBehaviour
    {
        public const float OpenS = 0.6f, HoldS = 1.8f, CloseS = 0.8f;
        Vector3 full;
        float age;

        /// <summary>Opens the model 'key' (ModCrates') at 'at', 'extent' Unity units across; null when it isn't loaded.</summary>
        public static RiftPortal Open(string key, Vector3 at, float extent, Transform parent)
        {
            var go = Modding.ModCrates.InstantiateFitted(key, at, Facing(at), extent, parent);
            if (go == null) return null;
            go.name = "Rift portal";
            PortalRefraction.AttachIfPortal(go);
            var p = go.AddComponent<RiftPortal>();
            p.full = go.transform.localScale;
            go.transform.localScale = Vector3.zero;
            return p;
        }

        /// <summary>Turned so its front (+Z) faces the main camera (identity without one).</summary>
        public static Quaternion Facing(Vector3 at)
        {
            var cam = Camera.main;
            if (cam == null) return Quaternion.identity;
            var d = cam.transform.position - at;
            return d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        /// <summary>Its opening at 'age' seconds: 0..1 (grows, holds, closes; below 0 after).</summary>
        public static float Opening(float age)
        {
            if (age < OpenS) return Mathf.SmoothStep(0f, 1f, age / OpenS);
            if (age < OpenS + HoldS) return 1f;
            float t = (age - OpenS - HoldS) / CloseS;
            return t < 1f ? Mathf.SmoothStep(1f, 0f, t) : -1f;
        }

        void Update()
        {
            age += Time.deltaTime;
            float k = Opening(age);
            if (k < 0f) { Destroy(gameObject); return; }
            transform.localScale = full * k;
        }
    }
}
