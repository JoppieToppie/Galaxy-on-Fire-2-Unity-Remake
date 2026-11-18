// LangFile.cs
// Feature: docs/notes/player-assets.md "Importers so far": Galaxy on Fire 2's own text tables (<code>.lang, one per
// language) read from the player's copy of the game: the strings in id order, what the remake's text_<lang>.json holds.
// Plain C#, no Unity types. Layout: string after string, each a big-endian u16 byte count then that many bytes of UTF-8
// (Java's writeUTF).

using System;
using System.Collections.Generic;
using System.Text;

namespace GoF2Remake.Data
{
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
#endif
    public static class LangFile
    {
        /// <summary>The original's file name (without .lang) -> the remake's language code (text_<code>.json).</summary>
        public static readonly IReadOnlyDictionary<string, string> Codes = new Dictionary<string, string>
        {
            { "gb", "en" }, { "de", "de" }, { "es", "es" }, { "fr", "fr" }, { "it", "it" }, { "ptl", "pt" }, { "pl", "pl" },
            { "ru", "ru" }, { "ar", "ar" }, { "ja", "ja" }, { "ko", "ko" }, { "zs", "zh" }, { "zt", "zh-Hant" },
        };

        /// <summary>The strings in order. Throws FormatException when the file doesn't end on a string's end.</summary>
        public static List<string> Read(byte[] b)
        {
            var o = new List<string>();
            var utf8 = new UTF8Encoding(false, true);
            int p = 0;
            while (p < b.Length)
            {
                if (p + 2 > b.Length) throw new FormatException("a .lang file ends inside a length");
                int n = b[p] << 8 | b[p + 1]; p += 2;
                if (p + n > b.Length) throw new FormatException("a .lang file ends inside a string");
                o.Add(utf8.GetString(b, p, n)); p += n;
            }
            return o;
        }

        /// <summary>The strings as the remake's text_<lang>.json: a JSON array, one string a line.</summary>
        public static string ToJson(IList<string> strings)
        {
            var s = new StringBuilder("[\n");
            for (int i = 0; i < strings.Count; i++)
            {
                s.Append('"');
                foreach (char c in strings[i])
                {
                    switch (c)
                    {
                        case '"': s.Append("\\\""); break;
                        case '\\': s.Append("\\\\"); break;
                        case '\n': s.Append("\\n"); break;
                        case '\r': s.Append("\\r"); break;
                        case '\t': s.Append("\\t"); break;
                        default:
                            if (c < 0x20) s.Append("\\u").Append(((int)c).ToString("x4"));
                            else s.Append(c);
                            break;
                    }
                }
                s.Append(i + 1 < strings.Count ? "\",\n" : "\"\n");
            }
            return s.Append(']').ToString();
        }
    }
}
