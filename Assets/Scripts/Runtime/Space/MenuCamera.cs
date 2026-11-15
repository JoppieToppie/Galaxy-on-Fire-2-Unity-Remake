// MenuCamera.cs
// The main menu backdrop's camera, like the original's CutScene(2) (CutScene::initialize 0xa4074 / process 0xa49f4,
// mode 2): CameraSetPerspective(0.92, 200, 200000); a fixed position (rnd(20000) - 20000, 0, rnd(60000) + 40000) in
// game units; rotation (0, yaw, 0) with yaw starting at -pi/4 and growing by dt * 5e-5 per ms (CutScene+0x24), a
// full turn every ~126 s. No look-at, orbit or bob: the station is in view for part of each turn. MenuBackground
// places it (Place) after building the orbit.

using UnityEngine;

namespace GoF2Remake.Visuals
{
    [DisallowMultipleComponent]
    public class MenuCamera : MonoBehaviour
    {
        const float M = GoF2Remake.World.OrbitLayout.MetersPerUnit;

        [Tooltip("CutScene+0x24: yaw per ms (radians).")]
        public float yawPerMs = 5e-5f;
        [Tooltip("CameraSetPerspective: the vertical field of view (radians), near and far (game units).")]
        public float fovRadians = 0.92f, near = 200f, far = 200000f;

        float gameYaw = -Mathf.PI / 4f;
        Camera cam;

        void OnEnable() => cam = GetComponent<Camera>();

        /// <summary>The camera at 'gamePosition' (game units), yaw -pi/4.</summary>
        public void Place(Vector3 gamePosition)
        {
            gameYaw = -Mathf.PI / 4f;
            transform.position = GoF2Remake.World.OrbitScale.ToUnity(gamePosition);   // in the scaled orbit (OrbitScale)
            // VR (no turning): facing the station at the orbit's origin from the start.
            if (GoF2Remake.Data.VrFlags.Enabled && transform.position.sqrMagnitude > 1f)
            {
                var toStation = -transform.position;
                gameYaw = -Mathf.Atan2(toStation.x, toStation.z);
            }
            Apply();
        }

        void LateUpdate()
        {
            if (!GoF2Remake.Data.VrFlags.Enabled) gameYaw += Time.deltaTime * 1000f * yawPerMs;   // VR: no turning under the player's head
            Apply();
        }

        void Apply()
        {
            if (cam == null) cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.fieldOfView = Aspect.VerticalFov(fovRadians * Mathf.Rad2Deg, cam.aspect);
                cam.nearClipPlane = near * M;
                cam.farClipPlane = far * M;
            }
            // A game camera rotation Ry(yaw) looking down its -Z = Unity Euler(0, -yaw, 0) looking down +Z.
            transform.rotation = Quaternion.Euler(0f, -gameYaw * Mathf.Rad2Deg, 0f);
        }
    }
}
