// ContentTimings.cs
// Feature: docs/notes/player-assets.md "Load times": what the content layer spends its time on (a texture from the GPU
// cache or decoded, an assembled object built, a sky rendered, a table read...): calls and milliseconds per kind, inclusive
// (an assembly's time holds its textures'). Any thread. PlayerAssetsCheck prints them per scene; cheap enough to stay
// on (a Stopwatch read per load).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace GoF2Remake.Data
{
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class ContentTimings
    {
        static readonly Dictionary<string, (int calls, long ticks)> totals = new Dictionary<string, (int, long)>(StringComparer.Ordinal);

        /// <summary>A measurement running until disposed; its kind can change on the way (a texture found in the GPU cache).</summary>
        public sealed class Scope : IDisposable
        {
            readonly long start = Stopwatch.GetTimestamp();
            public string Kind;
            internal Scope(string kind) => Kind = kind;
            public void Dispose()
            {
                long t = Stopwatch.GetTimestamp() - start;
                lock (totals)
                {
                    totals.TryGetValue(Kind, out var v);
                    totals[Kind] = (v.calls + 1, v.ticks + t);
                }
            }
        }

        public static Scope Measure(string kind) => new Scope(kind);

        /// <summary>"kind n× ms" per kind, slowest first ("" when nothing was measured); 'reset' starts again.</summary>
        public static string Report(bool reset)
        {
            var sb = new StringBuilder();
            lock (totals)
            {
                foreach (var kv in totals.OrderByDescending(kv => kv.Value.ticks))
                    sb.Append(sb.Length > 0 ? "; " : "").Append($"{kv.Key} {kv.Value.calls}x {kv.Value.ticks * 1000.0 / Stopwatch.Frequency:0} ms");
                if (reset) totals.Clear();
            }
            return sb.ToString();
        }
    }
}
