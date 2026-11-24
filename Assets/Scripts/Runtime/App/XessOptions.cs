// XessOptions.cs
// Feature: docs/notes/options.md "XeSS": the options of XessUpscaler (URP's upscaler framework keeps one per upscaler;
// UpscalerFramework.SetQuality sets the quality from the Upscaler quality option).

using UnityEngine;
using UnityEngine.Rendering;

namespace GoF2Remake
{
    public sealed class XessOptions : UpscalerOptions
    {
        /// <summary>The Upscaler quality option's order (UpscalerFramework.Quality*): 0 native AA, 1 quality, 2 balanced,
        /// 3 performance, 4 ultra performance; the plugin turns it into XeSS's own setting.</summary>
        [Range(0, 4)] public int qualityMode = 1;
    }
}
