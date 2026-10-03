// AboutText.cs
// The About text (45): the original's credits, and (remake) under the FULL HD version's KiritoJPK line (the English,
// Dutch and Portuguese tables have it) a link to that version's repository, the source of the game's assets. The link
// is a rich-text <link> tag; Hook makes a Label open it on a click or tap (PointerUpLinkTagEvent). Then the remake's
// third-party notices: code (JoyShockLibrary) and the custom ships' models (custom_ships.json).

using UnityEngine;
using UnityEngine.UIElements;
using GoF2Remake.Data;

namespace GoF2Remake.UI
{
    public static class AboutText
    {
        public const string SourceUrl = "https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android";
        const string LinkColour = "#8FD3FF";

        const string JoyShockUrl = "https://github.com/JibbSmart/JoyShockLibrary";
        const string JediStarfighterUrl = "https://sketchfab.com/3d-models/jedi-star-fighter-0b641c2f2b854f1f9ae7f2a731e44dbd";

        /// <summary>Text 45 with the source link after the paragraph that names KiritoJPK (unchanged without one), then the
        /// remake's third-party notices.</summary>
        public static string Get() => WithSourceLink() + ThirdParty;

        /// <summary>The remake's third-party code that asks for its notice in copies (the controller gyro's library) and the
        /// custom ships' models (ship 64's).</summary>
        static string ThirdParty =>
            "\n\nJoyShockLibrary (controller gyro): Copyright 2018-2023 Julian Smart, MIT License\n" +
            $"<link=\"{JoyShockUrl}\"><color={LinkColour}><u>{JoyShockUrl}</u></color></link>" +
            "\n\nJedi Starfighter model: Petri Liuhto\n" +
            $"<link=\"{JediStarfighterUrl}\"><color={LinkColour}><u>{JediStarfighterUrl}</u></color></link>";

        static string WithSourceLink()
        {
            string text = Localization.Get(45).TrimEnd();
            int at = text.IndexOf("KiritoJPK", System.StringComparison.Ordinal);
            if (at < 0) return text;
            int end = text.IndexOf('\n', at);
            string link = $"<link=\"{SourceUrl}\"><color={LinkColour}><u>{SourceUrl}</u></color></link>";
            return end < 0 ? text + "\n" + link : text.Substring(0, end).TrimEnd('\r') + "\n" + link + text.Substring(end).TrimStart('\r');
        }

        /// <summary>Opens the links of this label's rich text in the browser when clicked or tapped (once per label).</summary>
        public static void Hook(Label label)
        {
            if (label == null || label.ClassListContains("about-links")) return;
            label.AddToClassList("about-links");
            label.pickingMode = PickingMode.Position;
            label.RegisterCallback<UnityEngine.UIElements.Experimental.PointerUpLinkTagEvent>(e =>
            {
                if (!string.IsNullOrEmpty(e.linkID) && e.linkID.StartsWith("https://")) Application.OpenURL(e.linkID);
            });
        }
    }
}
