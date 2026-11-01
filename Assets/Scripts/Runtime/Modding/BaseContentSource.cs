// BaseContentSource.cs
// The remake's own additions to the game (ModManager.BaseContent), laid out like a mod in Resources/GoF2Base. Resources
// can't list a folder, so the files are named here; add a new file to All. An image is kept as "name.png.bytes".

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Modding
{
    public sealed class BaseContentSource : ModSource
    {
        const string Root = "GoF2Base/";
        static readonly string[] All = { ModManifest.FileName, "items.json", "blueprints.json", "icons/khador_slipstream.png" };

        public BaseContentSource() { Location = "GoF2Base"; }

        public override IReadOnlyList<string> Files => All;

        public override byte[] ReadBytes(string path)
        {
            string file = Resolve(path);
            if (file == null) return null;
            bool json = file.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            var asset = Resources.Load<TextAsset>(Root + (json ? file.Substring(0, file.Length - 5) : file));
            return asset != null ? asset.bytes : null;
        }
    }
}
