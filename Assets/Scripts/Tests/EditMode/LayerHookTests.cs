// LayerHookTests.cs
// The layers below reach the ones above only through their hooks (GameHooks, NetSession, SpaceHooks, UiHooks), which the
// installers of the layers above fill in. A hook nobody fills makes that feature quietly act like single player without
// events, so every delegate a hook class declares must be set once the installers have run.

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class LayerHookTests
    {
        static readonly Type[] HookClasses =
        {
            typeof(GoF2Remake.Data.GameHooks), typeof(GoF2Remake.Data.NetSession),
            typeof(GoF2Remake.World.SpaceHooks), typeof(GoF2Remake.UI.UiHooks),
        };

        /// <summary>Set by the level while it runs, not by an installer: FreelanceOrbit's follower view.</summary>
        static readonly HashSet<string> SetAtRuntime = new HashSet<string> { "SpaceHooks.MissionResultView" };

        [Test]
        public void EveryHookIsInstalled()
        {
            GoF2Remake.Flight.SpaceHooksInstaller.Install();
            GoF2Remake.Multiplayer.NetHooksInstaller.Install();
            GoF2Remake.AppHooksInstaller.Install();
            var missing = new List<string>();
            foreach (var t in HookClasses)
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (typeof(Delegate).IsAssignableFrom(f.FieldType) && f.GetValue(null) == null && !SetAtRuntime.Contains(t.Name + "." + f.Name))
                        missing.Add(t.Name + "." + f.Name);
            Assert.IsEmpty(missing, "hooks no installer fills: " + string.Join(", ", missing));
        }

        /// <summary>A player build runs the installers only through their [RuntimeInitializeOnLoadMethod] (the Editor calls
        /// them itself, EditorHookInstaller): a method put between the attribute and Install (2026-11-15, the mod scripts'
        /// ChatToScripts) left every build without the multiplayer and event graph hooks while Play mode worked.</summary>
        [Test]
        public void EveryInstallerRunsAtStartup()
        {
            foreach (var t in new[] { typeof(GoF2Remake.Flight.SpaceHooksInstaller), typeof(GoF2Remake.Multiplayer.NetHooksInstaller), typeof(GoF2Remake.AppHooksInstaller) })
            {
                var install = t.GetMethod("Install", BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(install, t.Name + ".Install");
                Assert.IsNotNull(install.GetCustomAttribute<UnityEngine.RuntimeInitializeOnLoadMethodAttribute>(), t.Name + ".Install runs at startup");
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (m.GetCustomAttribute<UnityEngine.RuntimeInitializeOnLoadMethodAttribute>() != null)
                        Assert.IsEmpty(m.GetParameters(), t.Name + "." + m.Name + " can't run at startup with arguments");
            }
        }
    }
}
