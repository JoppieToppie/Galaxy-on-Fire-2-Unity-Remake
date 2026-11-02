// MenuConfirm.cs
// Remake (a player's request on Discord, 2026-10-25: "Spacebar acts as enter in all menus"): Space confirms in every menu,
// like Enter and the numpad's Enter. The UI Toolkit menus take their confirm from the UI input module's Submit action,
// which the Input System binds to "*/{Submit}": on the keyboard only Enter carries that usage, so Space pressed a button
// only on the screens that read it themselves (the pause menu, dialogues, the star map). Each scene's module gets a copy of
// its actions with Space and the numpad's Enter added to UI/Submit (the package's DefaultInputActions stays untouched);
// the screens that read their keys themselves take Pressed(kb). Not the flight's own keys: Space fires there (the
// autopilot menu, the action prompt), and a typed space stays in its text field (TextFieldKeys.IsTyping).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class MenuConfirm
    {
        /// <summary>The keys UI/Submit takes besides the ones it binds already (Enter, the controller's A).</summary>
        public static readonly string[] SubmitKeys = { "<Keyboard>/space", "<Keyboard>/numpadEnter" };

        /// <summary>A menu's confirm on the keyboard this frame: Enter, the numpad's Enter or Space.</summary>
        public static bool Pressed(Keyboard kb) =>
            kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame);

        /// <summary>A confirm key still held (a press that opened a window must be let go first).</summary>
        public static bool Held(Keyboard kb) =>
            kb != null && (kb.enterKey.isPressed || kb.numpadEnterKey.isPressed || kb.spaceKey.isPressed);

        /// <summary>The copy made of each actions asset (the scenes' modules share the package's DefaultInputActions).</summary>
        static readonly Dictionary<InputActionAsset, InputActionAsset> copies = new Dictionary<InputActionAsset, InputActionAsset>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            copies.Clear();
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            PatchModules();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => PatchModules();

        static void PatchModules()
        {
            foreach (var module in Object.FindObjectsByType<InputSystemUIInputModule>(FindObjectsInactive.Include)) Patch(module);
        }

        /// <summary>Gives the module its actions with the extra submit keys (once: a module already on a copy keeps it).</summary>
        public static void Patch(InputSystemUIInputModule module)
        {
            var asset = module != null ? module.actionsAsset : null;
            if (asset == null || copies.ContainsValue(asset)) return;
            if (!copies.TryGetValue(asset, out var copy) || copy == null)
                copies[asset] = copy = WithSubmitKeys(asset);
            module.actionsAsset = copy;   // the setter moves every action reference over and keeps them enabled
        }

        /// <summary>A copy of 'source' whose UI/Submit also takes SubmitKeys.</summary>
        public static InputActionAsset WithSubmitKeys(InputActionAsset source)
        {
            var copy = Object.Instantiate(source);
            copy.name = source.name;
            copy.hideFlags = HideFlags.DontSave;
            var submit = copy.FindAction("UI/Submit");
            if (submit == null) return copy;
            foreach (var path in SubmitKeys)
            {
                bool bound = false;
                foreach (var b in submit.bindings) bound |= b.path == path;
                if (!bound) submit.AddBinding(path);
            }
            return copy;
        }
    }
}
