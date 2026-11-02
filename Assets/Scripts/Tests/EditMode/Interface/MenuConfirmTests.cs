// MenuConfirmTests.cs
// Feature: CLAUDE.md "UI and platforms" > Space confirms (UI.MenuConfirm): the UI module's Submit takes Space and the
// numpad's Enter besides Enter, on a copy of its actions (the shared asset stays as it is), and a module is patched once.

using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace GoF2Remake.Tests
{
    public class MenuConfirmTests
    {
        static bool Binds(InputAction action, string path)
        {
            foreach (var b in action.bindings) if (b.path == path) return true;
            return false;
        }

        [Test]
        public void TheCopysSubmitTakesSpaceAndTheNumpadsEnter()
        {
            var source = new DefaultInputActions().asset;
            int before = source.FindAction("UI/Submit").bindings.Count;
            var copy = MenuConfirm.WithSubmitKeys(source);
            try
            {
                var submit = copy.FindAction("UI/Submit");
                Assert.IsTrue(Binds(submit, "*/{Submit}"), "Enter and the controller's A stay");
                Assert.IsTrue(Binds(submit, "<Keyboard>/space"));
                Assert.IsTrue(Binds(submit, "<Keyboard>/numpadEnter"));
                Assert.AreEqual(before, source.FindAction("UI/Submit").bindings.Count, "the source asset is left as it was");
                Assert.IsFalse(Binds(copy.FindAction("UI/Cancel"), "<Keyboard>/space"));
            }
            finally
            {
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void AModuleIsPatchedOnce()
        {
            var go = new GameObject("MenuConfirmTests", typeof(EventSystem));
            var source = new DefaultInputActions().asset;
            InputActionAsset patched = null;
            try
            {
                var module = go.AddComponent<InputSystemUIInputModule>();
                module.actionsAsset = source;
                MenuConfirm.Patch(module);
                patched = module.actionsAsset;
                Assert.AreNotSame(source, patched);
                Assert.IsTrue(Binds(module.submit.action, "<Keyboard>/space"), "the module's submit reference follows the copy");
                MenuConfirm.Patch(module);
                Assert.AreSame(patched, module.actionsAsset, "a patched module keeps its copy");
                int spaces = 0;
                foreach (var b in module.submit.action.bindings) if (b.path == "<Keyboard>/space") spaces++;
                Assert.AreEqual(1, spaces);
            }
            finally
            {
                Object.DestroyImmediate(go);
                if (patched != source) Object.DestroyImmediate(patched);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void NoKeyboardIsNoConfirm()
        {
            Assert.IsFalse(MenuConfirm.Pressed(null));
            Assert.IsFalse(MenuConfirm.Held(null));
        }
    }
}
