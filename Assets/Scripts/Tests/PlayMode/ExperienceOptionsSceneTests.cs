using System.Collections;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GoF2Remake.Tests
{
    public class ExperienceOptionsSceneTests : PlayModeTest
    {
        [UnityTest]
        public IEnumerator PresetPoliciesAndCosmeticSectionsSurviveSceneChanges()
        {
            foreach (var scene in new[] { "MainMenu", "Station" })
            {
                if (scene == "Station") FreePlayAt(78);
                yield return Load(scene);
                var view = new OptionsView(() => { }, false);
                for (int i = 0; i < 4; i++) view.StepTab(1);
                var sections = view.NavItems().Select(e => view.RowOf(e)?.def)
                    .Where(d => d != null).Select(d => d.section).Distinct().ToArray();
                CollectionAssert.AreEqual(new[] {
                    OptionSection.SpecialFeatures, OptionSection.Interface, OptionSection.Hud
                }, sections, scene);
            }
        }
    }
}
