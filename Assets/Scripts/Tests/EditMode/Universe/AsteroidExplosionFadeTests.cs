// AsteroidExplosionFadeTests.cs
// Feature: Kothar's (Beidan's) ice asteroid explosion cloud fades out like the other asteroids' (Discord report 2026-11-03:
// it stayed at full strength and vanished at once). Its billboard's `extra` channel is keyed 199.5 -> 99.5, which the
// original's colour byte wraps (PartAnimation.IsOffsetExtra / ExtraOpacity).

using System.IO;
using GoF2Remake.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace GoF2Remake.Tests
{
    public class AsteroidExplosionFadeTests
    {
        static AnimationKey[] Extra(string model, int part)
        {
            var meta = JsonUtility.FromJson<MeshMeta>(File.ReadAllText(Path.Combine(Application.dataPath, "Models", model + ".gof2mesh.json")));
            foreach (var c in meta.parts[part].channels) if (c.target == "extra") return c.keys;
            Assert.Fail($"{model} part {part} has no extra channel");
            return null;
        }

        static float At(AnimationKey[] keys, float t)
        {
            for (int i = 1; i < keys.Length; i++)
                if (t <= keys[i].t) return Mathf.Lerp(keys[i - 1].v, keys[i].v, Mathf.InverseLerp(keys[i - 1].t, keys[i].t, t));
            return keys[keys.Length - 1].v;
        }

        [Test]
        public void TheIceCloudFadesOutOverItsAnimation()
        {
            var keys = Extra("valkyrie/misc/v_asteroid_ice_explosion_anim_alpha", 0);
            Assert.IsTrue(PartAnimation.IsOffsetExtra(keys));
            bool offset = PartAnimation.IsOffsetExtra(keys);
            Assert.Greater(PartAnimation.ExtraOpacity(At(keys, 50f), false, offset), 0.95f, "full strength at the start");
            Assert.AreEqual(0.5f, PartAnimation.ExtraOpacity(At(keys, 5025f), false, offset), 0.03f, "half way");
            Assert.Less(PartAnimation.ExtraOpacity(At(keys, 9900f), false, offset), 0.03f, "nearly gone at the end");
            Assert.AreEqual(0f, PartAnimation.ExtraOpacity(At(keys, 10050f), false, offset), "no flash on the last key");
        }

        [Test]
        public void OrdinaryFadeChannelsAreNotOffset()
        {
            Assert.IsFalse(PartAnimation.IsOffsetExtra(Extra("main/misc/asteroid_explosion_anim_alpha", 0)));
            Assert.IsFalse(PartAnimation.IsOffsetExtra(Extra("valkyrie/misc/v_asteroid_ice_explosion_anim_alpha", 1)));
            Assert.IsFalse(PartAnimation.IsOffsetExtra(Extra("main/fx/explosion_emp_anim_lookat_add", 1)), "overshoots 100 by a key only");
            Assert.AreEqual(1f, PartAnimation.ExtraOpacity(100.24f, false, false));
            Assert.AreEqual(0.5f, PartAnimation.ExtraOpacity(50f, false, false), 1e-5f);
        }
    }
}
