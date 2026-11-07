// CooldownView.cs
// Remake-only: the booster's and the cloak's recharge for keyboard and controller play, under the status bars next to the
// secondary weapon's plate. The original shows the boost charge only on its touch button (Hud::draw 18: the button's alpha)
// and the cloak's in the quick menu's entry (getCloakRechargeRate); with keys or a pad neither is on screen. Each is the
// equipment's own shop icon (GoF2Icons/item_NNN), lit from the left as it recharges (booster: FlightModel
// BoostRechargePercent, dim while boosting; cloak: Cloak.ChargeRate / RechargeRate, dim while cloaked) and lit with a short
// flash once it is ready. Hidden in touch mode (the touch buttons show it) and without the equipment.
// Remake (players' suggestion): the icon sweeps left to right: the part not yet recharged is see-through (a dark shade
// over the right), the whole icon opaque when ready; while the device runs (boosting, cloaked, the time extender slowing
// the world) the lit part drains with what is left, with a cyan edge. The time extender (category 26) has its slot too,
// and each active device (PlayerDevices: GoF3's, mod items) its own after it (the strike charge lights up as it charges).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed class CooldownView
    {
        const int BoosterCategory = 14, TimeExtenderCategory = 26;
        const float FlashSeconds = 0.6f;

        sealed class Slot
        {
            public VisualElement root, shade;
            public int item = -1;
            public bool wasReady = true;
            public float flashUntil;
        }

        readonly VisualElement row;
        readonly Slot boost, cloak, extender;
        readonly List<Slot> devices = new List<Slot>();

        public CooldownView(VisualElement parent)
        {
            row = new VisualElement { name = "cooldowns", pickingMode = PickingMode.Ignore };
            row.AddToClassList("cooldowns");
            parent.Add(row);
            boost = Make();
            cloak = Make();
            extender = Make();
        }

        Slot Make()
        {
            var s = new Slot { root = new VisualElement { pickingMode = PickingMode.Ignore } };
            s.root.AddToClassList("cooldown");
            s.shade = new VisualElement { pickingMode = PickingMode.Ignore };
            s.shade.AddToClassList("cooldown-shade");
            s.root.Add(s.shade);
            row.Add(s.root);
            return s;
        }

        /// <summary>Each frame. 'shown' = keyboard / controller flight with the HUD up.</summary>
        public void Update(Database db, ShipController ship, PlayerCloak playerCloak, TimeExtender timeExtender, PlayerDevices playerDevices, bool shown)
        {
            var model = ship != null ? ship.Model : null;
            var booster = shown && model != null && model.HasBooster && db != null ? Shop.FirstMounted(db, BoosterCategory) : null;
            var rules = shown && playerCloak != null ? playerCloak.Rules : null;
            Set(boost, booster != null ? booster.index : -1,
                model == null ? 1f : model.IsBoosting ? 0f : model.BoostRechargePercent, model != null && model.BoostReady,
                model != null && model.IsBoosting ? model.BoostLeftPercent : -1f);
            Set(cloak, rules != null ? rules.item : -1,
                rules == null ? 1f : rules.State == Cloak.Phase.Charging ? rules.ChargeRate : rules.Cloaked ? 0f : rules.RechargeRate,
                rules != null && rules.Available, rules != null && rules.Cloaked ? rules.CloakLeft : -1f);
            var slowItem = shown && timeExtender != null && db != null ? Shop.FirstMounted(db, TimeExtenderCategory) : null;
            Set(extender, slowItem != null ? slowItem.index : -1,
                timeExtender == null ? 1f : timeExtender.Running ? 0f : timeExtender.RechargeRate, timeExtender != null && timeExtender.Ready,
                timeExtender != null && timeExtender.Running ? timeExtender.RunLeft : -1f);
            var list = shown && playerDevices != null ? playerDevices.Devices : null;
            int n = list != null ? list.Count : 0;
            while (devices.Count < n) devices.Add(Make());
            for (int i = 0; i < devices.Count; i++)
            {
                if (i >= n) { Set(devices[i], -1, 1f, true, -1f); continue; }
                var d = list[i].rules;
                Set(devices[i], d.item, d.RechargeRate, d.Available, d.Active ? d.ActiveLeft : -1f);
            }
            row.style.display = booster != null || rules != null || slowItem != null || n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>'charge' 0..1 recharging (1 = ready); 'left' 0..1 what is left while the device runs, -1 = not running.</summary>
        static void Set(Slot s, int item, float charge, bool ready, float left)
        {
            if (item < 0) { s.root.style.display = DisplayStyle.None; s.item = -1; return; }
            s.root.style.display = DisplayStyle.Flex;
            if (item != s.item)
            {
                s.item = item;
                var tex = ItemInfo.ItemIcon(item);
                s.root.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground();
                s.wasReady = ready;
            }
            bool running = left >= 0f;
            float lit = running ? left : ready ? 1f : Mathf.Clamp01(charge);
            s.shade.style.width = Length.Percent((1f - lit) * 100f);   // the shade covers the right, the sweep reveals from the left
            s.root.EnableInClassList("cooldown--running", running);
            s.root.EnableInClassList("cooldown--waiting", !ready);
            if (ready && !s.wasReady) s.flashUntil = Time.unscaledTime + FlashSeconds;
            s.wasReady = ready;
            s.root.EnableInClassList("cooldown--flash", Time.unscaledTime < s.flashUntil);
        }
    }
}
