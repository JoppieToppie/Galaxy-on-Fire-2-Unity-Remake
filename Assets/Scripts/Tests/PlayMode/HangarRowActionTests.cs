// CLAUDE.md "Row actions": full-height action geometry and validated Shift-click mount/demount.
using System.Collections;
using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GoF2Remake.Tests
{
    public class HangarRowActionTests : PlayModeTest
    {
        static object Row(HangarWindow window, string kind, int item) =>
            ((IEnumerable)Get<object>(window, "rows")).Cast<object>()
                .First(r => Get<object>(r, "kind").ToString() == kind && Get<int>(r, "item") == item);

        static void Click(VisualElement target, bool shift, int button = 0, int count = 1)
        {
            using var click = ClickEvent.GetPooled();
            var type = typeof(PointerEventBase<ClickEvent>);
            type.GetProperty("button").SetValue(click, button);
            type.GetProperty("clickCount").SetValue(click, count);
            type.GetProperty("modifiers").SetValue(click, shift ? EventModifiers.Shift : EventModifiers.None);
            click.target = target;
            target.SendEvent(click);
        }

        [UnityTest]
        public IEnumerator ActionsFillTheirRowsAndShiftClickUsesExistingValidation(
            [Values(false, true)] bool original, [Values(640, 960, 1280)] int rowWidth)
        {
            using var style = new SettingsScope<bool>(() => Settings.OriginalUi, v => Settings.OriginalUi = v);
            Settings.OriginalUi = original;
            FreePlayAt(78);
            Session.ShipIndex = Db.Ships.First(s => s.slots.primary >= 2 && s.slots.equipment >= 1).index;
            Session.Equipment.Clear(); Session.Equipment.Add(new ItemStack(0, 1)); Session.Equipment.Add(new ItemStack(1, 1));
            Session.Cargo.Clear(); Session.Cargo.Add(new ItemStack(55, 1));
            Blueprints.Unlock(85);
            yield return Load("Station");
            var menu = Find<StationMenu>();
            yield return WaitFor(() => !Get<bool>(menu, "UiBlocked") && !Get<bool>(menu, "ArrivalPending"), 15f, "station controls");
            Invoke(menu, "OpenHangar");
            var window = Get<HangarWindow>(menu, "hangarWindow");
            window.SetTab(HangarWindow.Tab.Ship);
            var slot = Row(window, "Slot", 0);
            Get<VisualElement>(slot, "element").style.width = rowWidth;
            Invoke(window, "Select", slot, false);
            yield return Run(0.2f);
            var action = Get<Button>(window, "actionButton");
            var up = Get<Button>(window, "moveUpButton");
            var down = Get<Button>(window, "moveDownButton");
            float demountWidth = action.worldBound.width;
            Assert.Greater(action.worldBound.height, up.worldBound.height * 1.8f, "Demount spans the two arrow rows");
            Assert.LessOrEqual(up.worldBound.xMax, action.worldBound.xMin + 1f);
            Assert.LessOrEqual(up.worldBound.yMax, down.worldBound.yMin + 1f);
            Assert.AreEqual(action.worldBound.width, up.worldBound.width, 2f,
                "each stacked arrow retains the old half-area width, matching Demount");
            Click(up, true);
            Assert.AreEqual(2, Session.Equipment.Count, "an embedded arrow must not also demount the row");
            var cargo = Get<VisualElement>(Row(window, "CargoItem", 55), "element");
            cargo.style.width = rowWidth;
            Click(cargo, false);
            yield return Run(0.2f);
            var amount = Get<VisualElement>(window, "rowQty");
            Assert.AreEqual(demountWidth, action.worldBound.width, 5f,
                "Mount shares Demount's proportion, allowing the reorder gap and pixel rounding");
            Assert.AreEqual(0.125f, action.worldBound.width / cargo.worldBound.width, 0.01f,
                "button width follows the row at each tested viewport width");
            Assert.Greater(action.worldBound.height, cargo.worldBound.height * 0.75f,
                "Mount fills the row with only the style-specific vertical insets");
            Assert.LessOrEqual(action.worldBound.xMax, amount.worldBound.xMin - 1f,
                "cargo amount has its own column to the right of Mount");
            Assert.AreEqual(2, Session.Equipment.Count, "one ordinary click selects only");
            Click(cargo, true, 1);
            Assert.AreEqual(2, Session.Equipment.Count, "Shift-right-click does not mount");
            Click(cargo, true);
            Assert.IsTrue(Session.Equipment.Any(s => s.item == 55), "Shift-primary-click mounts");
            Click(Get<VisualElement>(Row(window, "Slot", 55), "element"), true);
            Assert.IsFalse(Session.Equipment.Any(s => s.item == 55), "Shift-primary-click demounts");
            var builtIn = Session.Equipment.First(s => s.item == 0); builtIn.builtIn = true;
            Invoke(window, "Rebuild");
            Click(Get<VisualElement>(Row(window, "Slot", 0), "element"), true);
            Assert.Contains(builtIn, Session.Equipment, "built-in equipment stays installed");
            window.SetTab(HangarWindow.Tab.Blueprints);
            var blueprint = Row(window, "Blueprint", 85);
            Get<VisualElement>(blueprint, "element").style.width = rowWidth;
            Invoke(window, "Select", blueprint, false);
            yield return Run(0.2f);
            var actions = Get<VisualElement>(window, "rowActions");
            Assert.AreEqual(demountWidth, action.worldBound.width, 5f, "Edit shares the agreed Demount proportion");
            Assert.AreEqual(actions.worldBound.height, action.worldBound.height, 1f, "Edit fills its action area");
            var blueprintRow = Get<VisualElement>(Row(window, "Blueprint", 85), "element");
            Assert.Greater(action.worldBound.height, blueprintRow.worldBound.height * 0.75f,
                "Edit fills the row with only the style-specific vertical insets");
        }
    }
}
