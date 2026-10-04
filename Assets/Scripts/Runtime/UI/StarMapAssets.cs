// StarMapAssets.cs
// What the star map and the system jumps need that can't be loaded by name (Resources/GoF2StarMap/StarMapAssets, made by
// GoF2 > Build > Star Map Assets): the overlay UXML and panel settings, the galaxy-view sun materials (mesh 18070 + system
// texture index -> its material), the Khador jump fx (mesh 15026) and the sounds (starmap_travel.md 11.1):
//   103 Select_System, 104 / 105 Map_Select_Planet_Push / _Release, 106 / 107 Map_Zoom_In / _Out, 124 / 123 / 126 buttons and
//   message box, 31 Jumpgate (sound definition /Jumpgate: Jumpgate_3b / 4c / 1b / 2b, one at random), 33 Jumpgate_Charge,
//   32 KhadorDrive (sound definition /KhadorDrive: Jumpgate_5c), 102 Map_Whoosh (Map_Click_01 per parameter region); the
//   FEV's LGCY data (Reference/tools/audio/fev_lgcy.py).

using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class StarMapAssets : ScriptableObject
    {
        public const string ResourcePath = "GoF2StarMap/StarMapAssets";

        public VisualTreeAsset layout;
        public PanelSettings panelSettings;
        [Tooltip("Indexed by the system's textureIndex.")]
        public Material[] sunMaterials;
        public GameObject khadorJump;

        public AudioClip selectSystem, planetPush, planetRelease, zoomIn, zoomOut;
        public AudioClip buttonPush, buttonRelease, infoSound;
        public AudioClip[] jumpgate;
        public AudioClip jumpgateCharge, khadorDrive;
        public AudioClip mapClick;   // 102 Map_Whoosh's only wave, Map_Click_01 (StarMap.AddWhoosh)

        static StarMapAssets cached;
        public static StarMapAssets Load() => cached != null ? cached : cached = Resources.Load<StarMapAssets>(ResourcePath);
    }
}
