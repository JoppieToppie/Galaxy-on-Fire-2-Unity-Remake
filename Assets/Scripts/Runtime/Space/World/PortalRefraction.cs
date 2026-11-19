// PortalRefraction.cs
// Remake (2026-11-11): an object with a Chorus portal material (GoF2/ChorusPortal, a mod material's "portal" block) needs
// the scene behind it for its refraction pass: URP's opaque texture, switched on for the main camera while the object is
// in the scene (Flight.OpaqueTexture, reference counted, as the cloak's and the shield bubble's).

using UnityEngine;

namespace GoF2Remake.World
{
    public class PortalRefraction : MonoBehaviour
    {
        const string ShaderName = "GoF2/ChorusPortal";

        /// <summary>Adds the component when any of the object's renderers draws a Chorus portal; whether it did.</summary>
        public static bool AttachIfPortal(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.shader != null && m.shader.name == ShaderName)
                    {
                        if (root.GetComponent<PortalRefraction>() == null) root.AddComponent<PortalRefraction>();
                        return true;
                    }
            return false;
        }

        void OnEnable() => Flight.OpaqueTexture.Request(this, true);
        void OnDisable() => Flight.OpaqueTexture.Request(this, false);
    }
}
