// HudReadout.cs
// Hud::draw 0x190d08's top readout (Hud+0x3d4, the top right; LAB_00191910 / LAB_00191994):
//   a time limit running (a campaign level's, not at 0x2a; Junk removal's; the Supernova Challenge's round)   the clock
//                                         plate 0x4c5 with "m:ss"
//   story type 0xb8 (passengers)          the passengers plate 0x1f43 "aboard / cabins" (at 0x66 in 113's orbit and at 0x8b
//                                         in 131's the cargo plate instead), under it 0x1f42 with the mission's remainder
//   story type 0xae (goods delivered)     the goods plate 0x1f61 "in the hold / free", under it 0x1f60 the amount still due
//   a kill contest (type 0xc: index 36, the freelance Challenge)   the cargo plate with "player : rival"
//   else                                  the cargo plate 0x520 "load / max t"
// With volatile goods aboard the red bar 0x1f5c runs over the plate from its left edge, its width x the volatile force.
// Plain class driven by FlightHud; images from Build HUD Images (GoF2Hud/hud_*).

using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class HudReadout
    {
        readonly VisualElement root, plate, bar, barFill, second;
        readonly Label text, secondText;
        string plateImage, secondImage;

        /// <summary>The readout's box at the top right of the safe area (FlightHud moves it left of the touch pause button).</summary>
        public VisualElement Root => root;

        public HudReadout(VisualElement parent)
        {
            root = new VisualElement { name = "hudReadout", pickingMode = PickingMode.Ignore };
            root.AddToClassList("hud-readout");
            plate = new VisualElement { pickingMode = PickingMode.Ignore };
            plate.AddToClassList("hud-readout-plate");
            bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("hud-readout-bar");
            barFill = new VisualElement { pickingMode = PickingMode.Ignore };
            barFill.AddToClassList("hud-readout-bar-fill");
            barFill.style.backgroundImage = Image("hud_volatile");
            bar.Add(barFill);
            plate.Add(bar);
            text = new Label { pickingMode = PickingMode.Ignore };
            text.AddToClassList("hud-readout-text");
            text.AddToClassList("gof-semibold");
            plate.Add(text);
            second = new VisualElement { pickingMode = PickingMode.Ignore };
            second.AddToClassList("hud-readout-plate");
            second.AddToClassList("hud-readout-plate--second");
            secondText = new Label { pickingMode = PickingMode.Ignore };
            secondText.AddToClassList("hud-readout-text");
            secondText.AddToClassList("gof-semibold");
            second.Add(secondText);
            root.Add(plate);
            root.Add(second);
            parent.Add(root);
        }

        static StyleBackground Image(string name)
        {
            var t = GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/" + name);
            return t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
        }

        // The line as last built: the readout runs every frame and its numbers seldom change (the string and the boxed ints
        // were made every frame).
        string lineFormat, lineText;
        int lineA = int.MinValue, lineB = int.MinValue;

        string Line(string format, int a, int b)
        {
            if (!ReferenceEquals(format, lineFormat) || a != lineA || b != lineB)
            {
                lineFormat = format; lineA = a; lineB = b;
                lineText = string.Format(format, a, b);
            }
            return lineText;
        }

        void SetPlate(VisualElement e, ref string current, string image)
        {
            if (current == image) return;
            current = image;
            e.style.backgroundImage = Image(image);
        }

        public void Update(SpaceLevel level, bool visible, float volatileForce)
        {
            root.style.display = visible && level != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible || level == null) return;
            var db = level.Database;
            string line = null, secondLine = null, image = "hud_cargo", secondImg = null;
            float left = -1f;
            var freelance = level.FreelanceOrbit;
            if (freelance != null && !freelance.DialogueOpen) left = freelance.TimeLeftMs;
            else if (freelance == null && level.Campaign != null && Session.CampaignMission != 0x2a) left = level.Campaign.TimeLeftMs;
            bool challenge = level.Challenge != null;   // the Supernova Challenge's round, down to 0:00
            if (challenge) left = SupernovaChallenge.TimeLeftMs;
            var mission = level.Campaign != null && !Session.FreePlay ? Story.Mission : null;
            if (left > 0f || challenge)
            {
                // Modified: the Supernova Challenge's clock drops its hidden milliseconds (floor), as the original's: it shows
                // 0:00 for the round's last second and the Khador jump comes when that runs out, not as 0:00 appears.
                int sec = challenge ? Mathf.FloorToInt(left / 1000f) : Mathf.CeilToInt(left / 1000f);
                image = "hud_timer";
                line = Line("{0}:{1:00}", sec / 60, sec % 60);
            }
            else if (mission != null && mission.type == StoryType.Passengers)
            {
                int cm = Session.CampaignMission, st = level.Layout.stationIndex;
                bool cargoPlate = (cm == 0x66 && st == 0x71) || (cm == 0x8b && st == 0x83);
                if (cargoPlate) line = Line("{0} / {1}t", Shop.CargoLoad(), Shop.MaxLoad(db));
                else { image = "hud_passengers"; line = $"{Session.StoryCounter} / {Freelance.MaxPassengers(db)}"; }
                secondImg = "hud_passengers_left";
                secondLine = mission.value.ToString();
            }
            else if (mission != null && mission.type == StoryType.AmountReached)
            {
                int goods = Session.Cargo.Find(c => c.item == mission.goodsItem)?.amount ?? 0;
                image = "hud_goods";
                line = $"{goods} / {Shop.FreeCargo(db)}";
                secondImg = "hud_goods_left";
                secondLine = Mathf.Max(0, mission.goodsAmount - mission.value).ToString();
            }
            else if (freelance != null && freelance.Type == MissionType.Challenge)
                line = Line("{0} : {1}", freelance.PlayerKills, freelance.OtherKills);
            else if (level.Campaign != null && !Session.FreePlay && Session.CampaignMission == 36)
                line = Line("{0} : {1}", level.Campaign.PlayerKills, level.Campaign.NpcKills);
            else line = Line("{0} / {1}t", Shop.CargoLoad(), Shop.MaxLoad(db));
            SetPlate(plate, ref plateImage, image);
            text.text = line;
            bool volatileGoods = GalaxyMap.HasVolatileGoods && image != "hud_timer";
            bar.style.display = volatileGoods ? DisplayStyle.Flex : DisplayStyle.None;
            if (volatileGoods) bar.style.width = Length.Percent(Mathf.Clamp01(volatileForce) * 100f);
            second.style.display = secondLine != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (secondLine != null) { SetPlate(second, ref secondImage, secondImg); secondText.text = secondLine; }
        }
    }
}
