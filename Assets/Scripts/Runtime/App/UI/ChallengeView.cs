// ChallengeView.cs
// The Supernova Challenge's score (Hud::drawChallengeModeScore 0x1908bc, never called in the Android build; the rules in
// Data.SupernovaChallenge): the score as 7 zero-padded digits of the gof2_challenge_interface atlas (images 6000-6009,
// GoF2Hud/challenge_digit_N) centred at the top, tinted 0x77CCFF; while a combo runs (timer > 0, combo > 1) the "COMBOx"
// image (6010, GoF2Hud/challenge_combo) and the count in white under it, the count popping by 1 + (timer - 7000) x 0.01;
// in the combo's last 3000 ms it blinks (50 ms off in every 100) with the bonus it pays beside it. Remake: the original
// put the bonus one line further down; beside the count it keeps the HUD's top plates (lock plate, messages) one row lower
// only (.hud-challenge in FlightHud.uss). Sizes x DigitScale of the iPad-large atlas.
// Plain class driven by FlightHud.

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class ChallengeView
    {
        const float DigitScale = 0.42f;
        const float DigitW = 84f * DigitScale, DigitH = 88f * DigitScale;
        const float Step = DigitW - 4f;   // frameWidth - pad (layout+0x2c)
        static readonly Color ScoreTint = new Color(0x77 / 255f, 0xCC / 255f, 1f);

        readonly VisualElement root, comboRow, comboCount, bonus;
        readonly VisualElement[] score = new VisualElement[SupernovaChallenge.ScoreDigits];
        readonly StyleBackground[] digits = new StyleBackground[10];
        int shownScore = -1, shownCombo = -1, shownBonus = -1;

        public ChallengeView(VisualElement parent)
        {
            for (int i = 0; i < 10; i++)
            {
                var t = GoF2Remake.Data.OriginalTextures.Image($"GoF2Hud/challenge_digit_{i}");
                digits[i] = t != null ? new StyleBackground(t) : new StyleBackground(StyleKeyword.None);
            }
            root = new VisualElement { name = "challengeHud", pickingMode = PickingMode.Ignore };
            root.AddToClassList("challenge-hud");
            var scoreRow = Row("challenge-score");
            for (int i = 0; i < score.Length; i++) score[i] = Digit(scoreRow, ScoreTint);
            root.Add(scoreRow);
            comboRow = Row("challenge-combo");
            var label = new VisualElement { pickingMode = PickingMode.Ignore };
            var comboTex = GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/challenge_combo");
            if (comboTex != null) label.style.backgroundImage = new StyleBackground(comboTex);
            label.style.width = 471f * DigitScale;
            label.style.height = 86f * DigitScale;
            label.style.marginRight = 2f;
            comboRow.Add(label);
            comboCount = Row("challenge-combo-count");
            comboRow.Add(comboCount);
            bonus = Row("challenge-bonus");
            comboRow.Add(bonus);
            root.Add(comboRow);
            parent.Add(root);
            root.style.display = DisplayStyle.None;
        }

        static VisualElement Row(string cls)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList(cls);
            return e;
        }

        static VisualElement Digit(VisualElement row, Color tint)
        {
            var d = new VisualElement { pickingMode = PickingMode.Ignore };
            d.style.width = DigitW;
            d.style.height = DigitH;
            d.style.marginRight = Step - DigitW;
            d.style.unityBackgroundImageTintColor = tint;
            row.Add(d);
            return d;
        }

        /// <summary>A number's digits in 'row' (made or dropped to its length), in white.</summary>
        void SetNumber(VisualElement row, string text)
        {
            while (row.childCount < text.Length) Digit(row, Color.white);
            while (row.childCount > text.Length) row.RemoveAt(row.childCount - 1);
            for (int i = 0; i < text.Length; i++) row[i].style.backgroundImage = digits[text[i] - '0'];
        }

        public void Update(bool challenge)
        {
            root.style.display = challenge ? DisplayStyle.Flex : DisplayStyle.None;
            if (!challenge) return;
            if (SupernovaChallenge.Score != shownScore)
            {
                shownScore = SupernovaChallenge.Score;
                string s = SupernovaChallenge.Digits(shownScore);
                for (int i = 0; i < score.Length; i++) score[i].style.backgroundImage = digits[s[i] - '0'];
            }
            bool combo = SupernovaChallenge.ComboShown;
            comboRow.style.visibility = combo ? Visibility.Visible : Visibility.Hidden;
            if (!combo) return;
            if (SupernovaChallenge.Combo != shownCombo)
            {
                shownCombo = SupernovaChallenge.Combo;
                SetNumber(comboCount, shownCombo.ToString());
            }
            comboCount.style.scale = new Scale(Vector3.one * SupernovaChallenge.ComboScale);
            bool showBonus = SupernovaChallenge.BonusShown;
            bonus.style.display = showBonus ? DisplayStyle.Flex : DisplayStyle.None;
            int b = SupernovaChallenge.ComboBonus(SupernovaChallenge.Combo);
            if (showBonus && b != shownBonus)
            {
                shownBonus = b;
                SetNumber(bonus, b.ToString());
            }
        }
    }
}
