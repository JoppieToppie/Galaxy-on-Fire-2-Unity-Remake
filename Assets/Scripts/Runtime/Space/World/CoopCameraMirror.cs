// CoopCameraMirror.cs
// Remake co-op: a follower's camera during the leader's story cutscene shows the leader's camera (CampaignLevel's
// TryCoopCamera, sent ten times a second, eased between), after the chase camera and everything else has placed it.

using UnityEngine;

namespace GoF2Remake.World
{
    [DefaultExecutionOrder(30000)]
    public class CoopCameraMirror : MonoBehaviour
    {
        CampaignLevel campaign;
        bool following;
        Vector3 pos;
        Quaternion rot = Quaternion.identity;
        float fov = 60f, ownFov = -1f;

        public void Setup(CampaignLevel level) => campaign = level;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (campaign == null || cam == null) return;
            if (!campaign.TryCoopCamera(out var p, out var r, out float f))
            {
                if (following && ownFov > 0f) cam.fieldOfView = ownFov;
                following = false;
                return;
            }
            if (!following) { pos = p; rot = r; fov = f; ownFov = cam.fieldOfView; following = true; }
            // Eased toward the newest pose: the poses come ten times a second.
            float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
            pos = Vector3.Lerp(pos, p, k);
            rot = Quaternion.Slerp(rot, r, k);
            fov = Mathf.Lerp(fov, f, k);
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
        }
    }
}
