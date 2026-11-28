// RecorderAudioTap.cs
// Remake-only: the screen recorder's ear (ScreenRecorder puts one on the scene's AudioListener while it records): Unity hands
// a listener's filters the final mix of everything heard, on the audio thread; it goes to the recorder as it is.

using UnityEngine;

namespace GoF2Remake.UI
{
    public sealed class RecorderAudioTap : MonoBehaviour
    {
        void OnAudioFilterRead(float[] data, int channels) => ScreenRecorder.OnAudio(data, channels);
    }
}
