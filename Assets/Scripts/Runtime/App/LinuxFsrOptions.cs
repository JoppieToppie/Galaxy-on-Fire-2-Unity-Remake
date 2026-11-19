// LinuxFsrOptions.cs
// Feature: docs/notes/options.md "FSR on Linux": the options of LinuxFsrUpscaler (URP's upscaler framework keeps one per
// upscaler; UpscalerFramework.SetQuality sets the quality from the Upscaler quality option).

using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake
{
    public sealed class LinuxFsrOptions : UpscalerOptions
    {
        /// <summary>FFX_UPSCALE_QUALITY_MODE_*: 0 native AA, 1 quality, 2 balanced, 3 performance, 4 ultra performance (the
        /// Upscaler quality option's order, UpscalerFramework.Quality*).</summary>
        [Range(0, 4)] public int qualityMode = 1;

        /// <summary>FSR 4 only: FSR 4 Lite, AMD's smaller model (Settings.Fsr4Lite; LinuxFsr.LiteAvailable).</summary>
        public bool lite;

        /// <summary>FSR's sharpening pass (RCAS) and its strength, 0..1.</summary>
        public bool sharpening;
        [Range(0f, 1f)] public float sharpness = 0.5f;
    }
}
