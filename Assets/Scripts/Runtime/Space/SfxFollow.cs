// SfxFollow.cs
// Modified: keeps an Sfx voice on a moving object while its sound plays (Sfx.PlayOn): the volume falls off and the pan
// turns with the object's distance and side as it moves, like the original's 3D FMOD event on the object's geometry
// (92's freighter jump 0x8c9, 102's carrier). Off again when the sound ends or the voice is taken for another sound.

using UnityEngine;

namespace GoF2Remake.Flight
{
    public class SfxFollow : MonoBehaviour
    {
        AudioSource src;
        Transform follow;
        float volume;

        public void Begin(AudioSource source, Transform target, float vol)
        {
            src = source;
            follow = target;
            volume = vol;
            enabled = true;
        }

        void LateUpdate()
        {
            if (src == null || follow == null || !src.isPlaying) { enabled = false; return; }
            var p = follow.position;
            src.transform.position = p;
            src.volume = Sfx.VolumeAt(p, volume);
            src.panStereo = Sfx.Pan(p);
        }
    }
}
