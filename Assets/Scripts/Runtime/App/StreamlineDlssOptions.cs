// StreamlineDlssOptions.cs
// Feature: docs/notes/options.md "DLSS with Streamline": the options of StreamlineDlssUpscaler (URP's upscaler framework
// keeps one per upscaler; UpscalerFramework.SetQuality sets them from the Upscaler quality and DLSS preset options).

using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake
{
    public sealed class StreamlineDlssOptions : UpscalerOptions
    {
        /// <summary>The Upscaler quality option's order (UpscalerFramework.Quality*): 0 DLAA, 1 quality, 2 balanced,
        /// 3 performance, 4 ultra performance.</summary>
        [Range(0, 4)] public int qualityMode = 1;

        /// <summary>Settings.DlssPreset (the NVIDIA module's DLSSPreset bit); 0 = NVIDIA's default for the mode.</summary>
        public int preset;
    }
}
