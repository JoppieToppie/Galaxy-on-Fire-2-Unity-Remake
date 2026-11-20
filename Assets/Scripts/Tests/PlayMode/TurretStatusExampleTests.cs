// TurretStatusExampleTests.cs
// Modding/Examples/turret_status (GitHub #314): a script's own line on the flight HUD, "AUTO TURRET: ON" / "OFF", for the
// automatic turret mounted on the player's ship (a Label added to FlightHud's root with game.style; docs/notes/mods.md
// "Patches" > the examples). Flown with an automatic turret (item 180) on a ship with a turret mount.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Modding;
using GoF2Remake.UI;
using GoF2Remake.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    [Category("Smoke")]
    public class TurretStatusExampleTests : PlayModeTest
    {
        const string Mod = "turret_status";

        [TearDown]
        public void ModOff()
        {
            ModManager.SetEnabled(Mod, false);
            ModScripts.StopAll();
            ModScripts.AutoApprove = false;
        }

        [OneTimeTearDown]
        public void MountedModsBack() { ModManager.Scan(); }

        static void CopyFolder(string from, string to)
        {
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dst = Path.Combine(to, f.Substring(from.Length).TrimStart('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
            }
        }

        [UnityTest]
        public IEnumerator TheHudSaysWhetherTheAutoTurretIsOn()
        {
            string example = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Modding", "Examples", Mod));
            if (!Directory.Exists(example)) Assert.Ignore("Modding/Examples/" + Mod + " isn't in this checkout");
            CopyFolder(example, Path.Combine(ModManager.UserFolder, Mod));
            ModManager.Scan();
            ModManager.SetEnabled(Mod, true);
            ModScripts.AutoApprove = true;   // (no question in a test; the player's own approvals aren't touched)

            FreePlayAt(78);
            Session.ShipIndex = Db.Ships.First(s => s.slots != null && s.slots.turret > 0).index;
            Session.Equipment = new List<ItemStack> { new ItemStack(180, 1) };   // an automatic turret
            Session.LaunchedFromStation = true;
            yield return Load("Space");
            yield return WaitFor(() => Find<SpaceLevel>() != null && Find<SpaceLevel>().LaunchCameraOver, 60f, "the orbit");

            Label Line() => Get<VisualElement>(Find<FlightHud>(), "root").Q<Label>("turretStatus");
            yield return WaitFor(() => Find<FlightHud>() != null && Line() != null && Line().text.Length > 0, 15f, "the mod's line on the HUD");
            Assert.AreEqual("", string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text)));
            var turret = UnityEngine.Object.FindObjectsByType<PlayerTurret>().First(t => t.IsAuto);
            Assert.AreEqual("AUTO TURRET: " + (turret.AutoEnabled ? "ON" : "OFF"), Line().text);
            Assert.AreEqual(Position.Absolute, Line().resolvedStyle.position);

            string shot = Directory.Exists(Path.Combine(Path.GetTempPath(), "gof2_test_shots")) ? Path.Combine(Path.GetTempPath(), "gof2_test_shots", "turret_status.png") : null;   // (a look at it, when asked for)
            if (!string.IsNullOrEmpty(shot))
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(shot);
                yield return Run(0.5f);
            }

            bool was = turret.AutoEnabled;
            turret.SetAuto(!was, false);
            yield return Run(0.6f);
            Assert.AreEqual("AUTO TURRET: " + (was ? "OFF" : "ON"), Line().text, "it follows the switch");
            Assert.AreEqual("", string.Join("|", ModScripts.Lines.Where(l => l.error).Select(l => l.text)));
        }
    }
}
