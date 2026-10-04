// CustomShipAutoBuild.cs  (Editor only)
// Runs "GoF2 > Build > Custom Ships" by itself when a custom ship's prefab or shop icon is missing, or older than
// custom_ships.json or its model: after the scripts reload in the editor and before every player build. Without the
// prefab Database.ShipAssembly names a prefab Resources can't load, and the ship flies as nothing but its exhaust particles
// (ShipExhaust works from the mounts alone).

using System.IO;
using GoF2Remake.Data;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GoF2Remake.EditorTools
{
    [InitializeOnLoad]
    public class CustomShipAutoBuild : IPreprocessBuildWithReport
    {
        const string Json = ImportSettings.Root + "/Resources/GoF2Data/" + CustomShips.FileName + ".json";

        static CustomShipAutoBuild()
        {
            if (Application.isBatchMode) return;   // batch builds go through OnPreprocessBuild
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                BuildIfStale("editor load");
            };
        }

        public int callbackOrder => -100;   // before anything that packs Resources

        public void OnPreprocessBuild(BuildReport report) => BuildIfStale("player build");

        /// <summary>True when some custom ship's prefab or icon is missing or older than its sources.</summary>
        public static bool Stale()
        {
            if (!File.Exists(Json)) return false;
            var json = File.GetLastWriteTimeUtc(Json);
            foreach (var c in Database.ReadCustomShips())
            {
                if (string.IsNullOrEmpty(c.assembly) || string.IsNullOrEmpty(c.model)) continue;
                string prefab = $"{ImportSettings.Root}/Resources/{Visuals.AssembledObject.ResourcesFolder}/custom/ships/{c.assembly}.prefab";
                string icon = $"{ItemIconBuilder.OutDir}/ship_{c.index:000}.png";
                if (!File.Exists(prefab) || !File.Exists(icon)) return true;
                var built = File.GetLastWriteTimeUtc(prefab);
                string model = ImportSettings.Root + "/" + c.model;
                if (json > built || (File.Exists(model) && File.GetLastWriteTimeUtc(model) > built)) return true;
            }
            return false;
        }

        static void BuildIfStale(string when)
        {
            if (!Stale()) return;
            Debug.Log($"GoF2: custom ships out of date ({when}): running Build Custom Ships.");
            CustomShipBuilder.Build(true);
        }
    }
}
