// ModWeaponsTests.cs
// Feature: Modding/README.md "Weapon effects" (ModWeapons.Defs, the items.json "fx" entries): a turret's own model ("turret":
// a file, or { "model", "muzzle", "shotStart", "scale" }), a part borrowed from another item ("item") and resized ("scale"), a sound
// taken from another item ("shot": a number), and their refusals. Only the parsing: the models aren't loaded here.

using System.Linq;
using GoF2Remake.Modding;
using NUnit.Framework;

namespace GoF2Remake.Tests
{
    public class ModWeaponsTests : SavesModsTest
    {
        const string Mod = "t_fx";

        ModWeapons.Def DefOf(string fxJson, params (string path, string text)[] files)
        {
            var all = files.Append(("items.json", "[ { 'id': 'mat', 'base': 182, 'name': 'Missile turret', 'fx': " + fxJson + " } ]")).ToArray();
            WriteMod("fx", Mod, "1.0", "", all);
            ScanAndEnable(Mod);
            var defs = ModWeapons.Defs();
            return defs.Count == 1 ? defs[0] : null;
        }

        // A mod's own "impact" on a rocket shows where it hits, in place of the game's burst (WeaponFx.modImpact).
        [Test]
        public void AModsImpactShowsWhereAMissileHits()
        {
            var fx = UnityEngine.ScriptableObject.CreateInstance<Flight.WeaponFx>();
            fx.impact = new UnityEngine.GameObject("impact");
            fx.modImpact = true;
            var root = new UnityEngine.GameObject("fx root").transform;
            try
            {
                var gun = new Flight.Gun(Data.Database.Shared.Items[31], UnityEngine.Vector3.zero, true);
                Assert.IsTrue(gun.IsMissile);
                var rig = new Flight.GunRig(gun, fx, root, null);
                Assert.IsFalse(root.Cast<UnityEngine.Transform>().Any(t => t.gameObject.activeSelf));
                rig.ShowImpact(new UnityEngine.Vector3(1f, 2f, 3f));
                var shown = root.Cast<UnityEngine.Transform>().Single(t => t.gameObject.activeSelf);
                Assert.AreEqual(new UnityEngine.Vector3(1f, 2f, 3f), shown.position);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                UnityEngine.Object.DestroyImmediate(fx.impact);
                UnityEngine.Object.DestroyImmediate(fx);
            }
        }

        [Test]
        public void ATurretModelWithItsMuzzleAndShotStart()
        {
            var d = DefOf("{ 'turret': { 'model': 'models/t.glb', 'muzzle': 28, 'shotStart': 32 } }", ("models/t.glb", "glb"));
            Assert.IsNotNull(d, AllMessages(Installed(Mod)));
            Assert.AreEqual("models/t.glb", d.turretModel);
            Assert.AreEqual(28f, d.turretMuzzle);
            Assert.AreEqual(32f, d.turretShot);
        }

        [Test]
        public void ATurretModelAsAFileKeepsTheBaseOffsets()
        {
            var d = DefOf("{ 'turret': 'models/t.glb' }", ("models/t.glb", "glb"));
            Assert.AreEqual("models/t.glb", d.turretModel);
            Assert.AreEqual(-1f, d.turretMuzzle);
            Assert.AreEqual(-1f, d.turretShot);
        }

        [Test]
        public void ATurretModelWithItsScale()
        {
            var d = DefOf("{ 'turret': { 'model': 'models/t.glb', 'scale': 2.5 } }", ("models/t.glb", "glb"));
            Assert.AreEqual(2.5f, d.turretScale);
            Assert.AreEqual(1f, DefOf("{ 'turret': 'models/t.glb' }", ("models/t.glb", "glb")).turretScale, "as made");
        }

        [Test]
        public void ATurretModelMovedOffItsMount()
        {
            var d = DefOf("{ 'turret': { 'model': 'models/t.glb', 'offset': [10, -20, 30] } }", ("models/t.glb", "glb"));
            Assert.AreEqual(new UnityEngine.Vector3(10f, -20f, 30f), d.turretOffset);
            Assert.IsTrue(d.TurretReshaped);
        }

        [Test]
        public void TheBaseTurretResizedWithoutAModel()
        {
            var d = DefOf("{ 'turret': { 'scale': 1.5, 'offset': [0, 40, 0] } }");
            Assert.IsNotNull(d, AllMessages(Installed(Mod)));
            Assert.IsNull(d.turretModel, "the base item's model");
            Assert.AreEqual(1.5f, d.turretScale);
            Assert.IsTrue(d.TurretReshaped);
        }

        [Test]
        public void AnEmptyTurretIsRefused()
        {
            Assert.IsNull(DefOf("{ 'turret': { } }"));
            StringAssert.Contains("needs \"model\"", AllMessages(Installed(Mod)));
            Assert.IsNull(DefOf("{ 'turret': { 'model': 'models/t.glb', 'offset': [1, 2] } }", ("models/t.glb", "glb")));
            StringAssert.Contains("\"offset\" is [x, y, z]", AllMessages(Installed(Mod)));
        }

        [Test]
        public void ATurretScaleOfZeroIsRefused()
        {
            Assert.IsNull(DefOf("{ 'turret': { 'model': 'models/t.glb', 'scale': 0 } }", ("models/t.glb", "glb")));
            StringAssert.Contains("\"scale\" must be above 0", AllMessages(Installed(Mod)));
        }

        [Test]
        public void AMissingTurretModelIsRefused()
        {
            Assert.IsNull(DefOf("{ 'turret': 'models/none.glb' }"));
            StringAssert.Contains("isn't in the mod", AllMessages(Installed(Mod)));
        }

        [Test]
        public void APartFromAnotherItemResized()
        {
            var d = DefOf("{ 'projectile': { 'item': 46, 'scale': 0.35 }, 'impact': { 'scale': 2 } }");
            Assert.IsTrue(d.projectile.copy);
            Assert.IsFalse(d.projectile.recolor);
            Assert.AreEqual(46, d.projectile.fromItem);
            Assert.AreEqual(0.35f, d.projectile.scale, 1e-6f);
            Assert.IsTrue(d.impact.copy, "the base item's own impact, twice the size");
            Assert.AreEqual(-1, d.impact.fromItem);
            Assert.AreEqual(2f, d.impact.scale);
            Assert.IsEmpty(Installed(Mod).Warnings, AllMessages(Installed(Mod)));
        }

        [Test]
        public void ARecolourIsStillACopy()
        {
            var d = DefOf("{ 'projectile': { 'color': '#ff0000' } }");
            Assert.IsTrue(d.projectile.copy);
            Assert.IsTrue(d.projectile.recolor);
            Assert.AreEqual(1f, d.projectile.scale);
        }

        [Test]
        public void AnUnknownItemIsRefused()
        {
            Assert.IsNull(DefOf("{ 'projectile': { 'item': 99999 } }"));
            StringAssert.Contains("there is no item 99999", AllMessages(Installed(Mod)));
        }

        [Test]
        public void ScaleOnASpriteIsIgnoredWithAWarning()
        {
            var d = DefOf("{ 'projectile': { 'sprite': 'fx/b.png', 'scale': 2 } }", ("fx/b.png", "png"));
            Assert.IsFalse(d.projectile.copy);
            StringAssert.Contains("only applies without a sprite or model", AllMessages(Installed(Mod)));
        }

        [Test]
        public void ASoundFromAnotherItem()
        {
            var d = DefOf("{ 'shot': 46, 'explosionSound': 7, 'shotLoops': false }");
            Assert.AreEqual(46, d.shotItem);
            Assert.IsNull(d.shots);
            Assert.AreEqual(7, d.explosionItem);
            Assert.AreEqual(false, d.shotLoops);
        }

        // ---- a beam laser's sprite (a modder's report: under the beam's length scale a camera-facing sprite smeared) -------

        [Test]
        public void ABeamsSpriteRunsAlongTheBeamFacingTheCamera()
        {
            Assert.IsTrue(GoF2Remake.Flight.Gun.IsBeamLook(9) && GoF2Remake.Flight.Gun.IsBeamLook(228));
            Assert.IsFalse(GoF2Remake.Flight.Gun.IsBeamLook(12), "a laser bolt is no beam");
            var cam = new UnityEngine.GameObject("test camera") { tag = "MainCamera" };
            cam.AddComponent<UnityEngine.Camera>();
            var root = new UnityEngine.GameObject("beam root").transform;
            try
            {
                const float M = 0.05f, Length = 800f, Size = 60f;
                // GunRig.UpdateBeam: at the mount, along the shot, scaled (1, 1, the beam's length in game units).
                root.SetPositionAndRotation(new UnityEngine.Vector3(10f, 2f, 5f), UnityEngine.Quaternion.Euler(10f, 35f, 0f));
                root.localScale = new UnityEngine.Vector3(1f, 1f, Length);
                cam.transform.position = root.position + root.right * 30f + UnityEngine.Vector3.up * 8f;
                var sprite = new UnityEngine.GameObject("sprite").transform;
                sprite.SetParent(root, false);
                ModWeapons.FitBeamSprite(sprite, Size);
                var part = sprite.gameObject.AddComponent<ModFxPart>();
                part.beam = true;
                part.Restart();
                var start = sprite.TransformPoint(new UnityEngine.Vector3(0f, -0.5f, 0f));
                var end = sprite.TransformPoint(new UnityEngine.Vector3(0f, 0.5f, 0f));
                float width = (sprite.TransformPoint(new UnityEngine.Vector3(0.5f, 0f, 0f)) - sprite.TransformPoint(new UnityEngine.Vector3(-0.5f, 0f, 0f))).magnitude;
                Assert.Less((start - root.position).magnitude, 1e-3f, "it starts at the mount");
                Assert.Less((end - (root.position + root.forward * Length * M)).magnitude, 1e-2f, "and ends at the beam's length");
                Assert.AreEqual(Size * M, width, 1e-3f, "'size' wide, not stretched by the beam's scale");
                var normal = sprite.TransformDirection(UnityEngine.Vector3.back).normalized;
                Assert.AreEqual(0f, UnityEngine.Vector3.Dot(normal, root.forward), 1e-3f, "its face turned about the beam");
                var toCam = UnityEngine.Camera.main.transform.position - sprite.position;   // the Editor's open scene may have its own
                Assert.Greater(UnityEngine.Vector3.Dot(normal, (toCam - root.forward * UnityEngine.Vector3.Dot(toCam, root.forward)).normalized), 0.999f, "toward the camera");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                UnityEngine.Object.DestroyImmediate(cam);
            }
        }
    }
}
