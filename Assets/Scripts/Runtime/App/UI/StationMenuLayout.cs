// StationMenuLayout.cs
// StationMenu: layout (part of the partial StationMenu; its fields are in StationMenu.cs).

using System.Linq;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using GoF2Remake.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class StationMenu
    {
        // ---- layout (same scaling and safe-area handling as the flight HUD) ---------------------------------
        Vector2Int ScreenSize()
        {
            var rt = runtimePanel != null ? runtimePanel.targetTexture : null;
            return rt != null ? new Vector2Int(rt.width, rt.height) : new Vector2Int(Screen.width, Screen.height);
        }
        void UpdateLayout()
        {
            lastScreen = ScreenSize();
            lastSafeArea = Screen.safeArea;
            tickerReady = false;   // the strip's width changes: fill it again
            lastLayout = UiLayout.Revision;
            bool offscreen = runtimePanel != null && runtimePanel.targetTexture != null;
            float w = Mathf.Max(1, lastScreen.x), h = Mathf.Max(1, lastScreen.y);
            UiLayout.Configure(runtimePanel, root, w, h);   // docs/UI-DESIGN.md "Scaling"
            ApplySafeArea(w, h, offscreen);
        }
        /// <summary>Device safe-area insets (the notch / Dynamic Island, the home indicator, cutouts), see SafeArea.</summary>
        void ApplySafeArea(float w, float h, bool offscreen) => SafeArea.Apply(root, safeArea, w, h, offscreen, SafeArea.Mode.Position);
        /// <summary>Remake: the system's race emblem at the left of the station info box (system, tech level, race): the four
        /// races' large emblems (GoF2Races/<race>_icon), the radar's for the pirates and the Void, none otherwise.</summary>
        static void ShowRaceIcon(VisualElement info, int race, int station = -1)
        {
            if (info == null) return;
            // Called again (RefreshTexts on a language change): the first call moved the box's lines into the "info-lines"
            // column, so a label's parent is that column now, not the box. Back to the box, or every call wrapped the column
            // in another one with another emblem, squeezing the text into a narrower strip each time.
            if (info.ClassListContains("info-lines") && info.parent != null) info = info.parent;
            var icon = info.Q("raceIcon");
            if (icon == null)
            {
                // The box's lines into a column of their own, the emblem beside it.
                var lines = new VisualElement { pickingMode = PickingMode.Ignore };
                lines.AddToClassList("info-lines");
                while (info.childCount > 0) lines.Add(info[0]);
                info.Add(lines);
                icon = new VisualElement { name = "raceIcon", pickingMode = PickingMode.Ignore };
                icon.AddToClassList("info-race-icon");
                info.Insert(0, icon);
                info.AddToClassList("station-info--emblem");
            }
            // The Kaamo Club: a skull until it is bought (or owned from the start), then the orange house.
            var tex = station == KaamoClub.Station ? Resources.Load<Texture2D>(KaamoClub.Owned ? "GoF2Races/kaamo_club_icon" : "GoF2Races/kaamo_club_locked_icon") : null;
            if (tex == null && Modding.ModRaces.Is(race)) tex = Modding.ModRaces.Icon(race);   // remake mods: a mod race's emblem
            if (tex == null) tex = race >= 0 && race <= 3 ? Resources.Load<Texture2D>($"GoF2Races/{StatusWindow.RaceFile(race)}_icon")
                    : race == 8 || race == 9 ? GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/race_" + race) : null;
            icon.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
            icon.style.display = tex != null ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
