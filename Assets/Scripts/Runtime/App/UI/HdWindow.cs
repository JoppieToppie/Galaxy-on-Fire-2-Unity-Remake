// HdWindow.cs
// Feature: docs/notes/player-assets.md "HD resources": the setup card's HD steps (OriginalSetupView, Start.Hd) as a window
// over the screen that opened it (Options > Graphics "HD resources…", or "Add a pack…" given the HD pack): supply them,
// install them beside the originals or over them, merge them into the copy or remove them. A dim overlay over the whole
// panel takes the clicks; the screen behind is disabled meanwhile (its rows can't take the focus) and the menus ignore
// their keys (GameControls.ModalOpen).

using System.Collections;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class HdWindow
    {
        /// <summary>The window is open.</summary>
        public static bool Open { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Open = false;

        /// <summary>Opens the window over the panel 'anyInPanel' is in; 'hdPack': an HD pack picked already. False when there is
        /// no panel or the window is open already.</summary>
        public static bool Show(VisualElement anyInPanel, OriginalPacks.Info hdPack = null)
        {
            var tree = anyInPanel?.panel?.visualTree;
            if (tree == null || Open) return false;
            Open = true;
            var behind = new List<VisualElement>();
            foreach (var child in tree.Children())
                if (child.enabledSelf) { behind.Add(child); child.SetEnabled(false); }
            var overlay = new VisualElement();
            overlay.AddToClassList("original-setup-overlay");
            tree.Add(overlay);
            GameControls.ModalOpen = true;
            var host = new GameObject("HD window").AddComponent<Host>();
            host.StartCoroutine(Run(host, overlay, behind, hdPack));
            return true;
        }

        static IEnumerator Run(Host host, VisualElement overlay, List<VisualElement> behind, OriginalPacks.Info hdPack)
        {
            try { yield return new OriginalSetupView(overlay).Run(OriginalSetupView.Start.Hd, hdPack, window: true); }
            finally
            {
                overlay.RemoveFromHierarchy();
                foreach (var e in behind) e.SetEnabled(true);
                GameControls.ModalOpen = false;
                Open = false;
                OptionControl.RefreshShown();
                if (host != null) Object.Destroy(host.gameObject);
            }
        }

        sealed class Host : MonoBehaviour { }
    }
}
