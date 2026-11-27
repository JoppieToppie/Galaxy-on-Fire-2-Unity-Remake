// MobileHdrGuardTests.cs
// HDR display output only for the desktop players (CLAUDE.md "HDR" in docs/notes/options.md, MobileHdrGuard): the global
// project settings use HDR, and the 2026.11.21 Android build started in HDR on an HDR phone (Discord: very dark, half the
// frame rate). Expected: phone builds (Android, iOS) are built without HDR output, the desktop ones keep it.

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace GoF2Remake.Tests
{
    public class MobileHdrGuardTests
    {
        [TestCase(BuildTarget.Android, false)]
        [TestCase(BuildTarget.iOS, false)]
        [TestCase(BuildTarget.StandaloneWindows64, true)]
        [TestCase(BuildTarget.StandaloneLinux64, true)]
        public void OnlyDesktopBuildsStartInHdr(BuildTarget target, bool allowed)
        {
            var guard = Type.GetType("GoF2Remake.EditorTools.MobileHdrGuard, Assembly-CSharp-Editor");
            Assert.IsNotNull(guard, "Assets/Scripts/Editor/MobileHdrGuard.cs");
            var hdrAllowed = guard.GetMethod("HdrAllowed", BindingFlags.Public | BindingFlags.Static);
            Assert.AreEqual(allowed, (bool)hdrAllowed.Invoke(null, new object[] { target }));
        }
    }
}
