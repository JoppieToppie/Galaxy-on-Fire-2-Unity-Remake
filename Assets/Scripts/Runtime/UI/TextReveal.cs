// TextReveal.cs
// Remake-only (option "Animated dialogue", Settings.AnimatedDialogue; off = the original's plain page at once): the
// dialogue window's and the radio box's text appears letter by letter with a little emotion, through rich text:
//   reveal     each letter fades in (FadeMs) and settles from a brighter tint to its colour; the rest of the page is laid
//              out already, invisible, so no word jumps to the next line while it types
//   pacing     ReadingSpeed letters per second, or paced to the voice line (the whole timeline ends at ~85 % of it);
//              short pauses after , ; : and . ? !, a longer hesitation after "...", sentences ending in "!" type faster
//   shouting   all-caps words ("KEITH!", "MORE!", "BANG!", "KABOOM"; 4+ letters, or 2+ before a "!", not the acronyms)
//              are bigger and bold and shake for a moment as they appear
//   actions    *Sigh* / *Yawn* (Russian: <шепотом>): italic, dimmer, the markers dropped, typed slowly
//   names      people pale gold (with their titles), places / factions light aqua (with "Station" / "System"), ships and
//              items pink, and each race in its emblem's colour (Terran orange, Vossk green, Nivelian blue, Midorian grey,
//              pirates bone white, Void violet), also the factions named after it ("Vossk Empire", "Terran Fleet"):
//              story speakers, stations, systems, races, ships, items and the lore names only the texts use (LoreNames);
//              whole words, case-sensitive (races in either case), plurals too
//   icons      inline <sprite>s from Resources/Sprite Assets/gof2_text_icons (GoF2 > Build > Text Icons), faded in with
//              their letter: a coin before every amount ("1,800$", "20,000 credits" and the credit word of each
//              language); before the first mention on the page: the race emblems (Terran, Vossk, Nivelian, Midorian,
//              pirates, Void), the jumpgate, wormhole, blueprint and autopilot icons (their text ids 547 / 545 / 271 / 571),
//              an item's or ship's shop icon before its name (items of two or more words in any case: "energy cells"),
//              an equipment category's icon (a first item of it) before the category word ("tractor beam", "scanner",
//              "missiles"; "mine" only as "mines" or "Mine"), the ore core icon before "core" and the container before
//              "container" (English)
// Alien-font pages (AlienText) fade their glyph images in the same rhythm; Arabic / Hebrew pages (joined, right to left)
// fade in whole instead, so the per-letter tags never split the letter joining. Japanese / Chinese punctuation (。！？、)
// pauses without a following space. All-caps words of item, ship, station and system names ("Micro Gun MKII") never
// count as shouting. The texts have no rich text tags of their own ('<' is escaped). Checked against all 11 text tables.
// Plain C#: the owner calls Begin after setting the text, then Tick every frame.

using System.Collections.Generic;
using System.Text;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class TextReveal
    {
        /// <summary>Letters per second without a voice line.</summary>
        public const float ReadingSpeed = 55f;
        const float MinSpeed = 18f, MaxSpeed = 110f, VoiceShare = 0.85f;
        const float FadeMs = 140f, SettleMs = 260f, ShakeMs = 450f, ShakeEm = 0.07f;
        const float CommaPauseMs = 90f, StopPauseMs = 220f, EllipsisPauseMs = 420f;
        const float ExclaimPace = 0.75f, ActionPace = 2.2f;
        const float ShoutSize = 130f, ActionDim = 0.65f;
        static readonly HashSet<string> Acronyms = new HashSet<string> { "EMP", "PEM", "IEM", "ЭМИ", "HUD", "AMR", "IMT", "OK" };   // EMP in es / fr / ru too

        struct Letter
        {
            public char c;
            public Color? tint;
            public bool shout, action;
            public string sprite;                   // an inline icon (c = U+FFFC) instead of a letter
            public float pace, pauseAfter, start;   // start: ms on the page's clock
        }

        readonly Label label;
        readonly List<Letter> letters = new List<Letter>();
        readonly List<VisualElement> glyphs = new List<VisualElement>();
        bool alien, running, whole;
        string speakerName;
        float clock, letterMs, endMs;
        int shown;

        public TextReveal(Label label) { this.label = label; }

        /// <summary>True while letters are still appearing (or settling).</summary>
        public bool Running => running;
        /// <summary>0..1 of the letters started (1 when not animating).</summary>
        public float Progress => running && letters.Count > 0 ? (float)shown / letters.Count : 1f;

        /// <summary>Starts revealing the label's page (already set with AlienText.Set). 'voice' paces it; 'instant' shows it
        /// styled at once (a page read before); 'speaker' (a bar agent's generated name) is tinted like the story names.
        /// Does nothing with the option off.</summary>
        public void Begin(string pageText, bool alienFont, AudioClip voice = null, bool instant = false, string speaker = null)
        {
            speakerName = speaker;
            running = false;
            glyphs.Clear();
            letters.Clear();
            whole = false;
            if (label == null) return;
            label.style.opacity = StyleKeyword.Null;
            if (!Settings.AnimatedDialogue) return;
            alien = alienFont && AlienText.CollectGlyphs(label, glyphs);
            if (!alien && IsRightToLeft(pageText))
            {
                // The page as it is, faded in whole.
                whole = true;
                clock = instant ? float.MaxValue / 4f : 0f;
                endMs = SettleMs * 1.5f;
                running = !instant;
                Render();
                return;
            }
            if (alien) for (int i = 0; i < glyphs.Count; i++) letters.Add(new Letter { c = 'A', pace = 1f });
            else Parse(pageText ?? "");
            if (letters.Count == 0) return;

            // The timeline: every letter costs letterMs x its pace, pauses on top. A voice line sets letterMs so the whole
            // page (pauses included) ends at VoiceShare of the recording.
            float paces = 0f, pauses = 0f;
            foreach (var l in letters) { paces += l.pace; pauses += l.pauseAfter; }
            letterMs = 1000f / ReadingSpeed;
            if (voice != null && voice.length > 0.5f)
                letterMs = Mathf.Clamp((voice.length * 1000f * VoiceShare - pauses) / Mathf.Max(1f, paces), 1000f / MaxSpeed, 1000f / MinSpeed);
            float t = 0f;
            for (int i = 0; i < letters.Count; i++)
            {
                var l = letters[i];
                l.start = t;
                letters[i] = l;
                t += letterMs * l.pace + l.pauseAfter;
            }
            endMs = t + SettleMs + ShakeMs;
            clock = instant ? float.MaxValue / 4f : 0f;
            running = !instant;
            Render();
        }

        /// <summary>No effects for the label's current text (a page that isn't dialogue: the info / narrator pages).</summary>
        public void Clear()
        {
            running = false;
            whole = false;
            glyphs.Clear();
            letters.Clear();
            if (label != null) label.style.opacity = StyleKeyword.Null;
        }

        /// <summary>Shows the whole page now (Next during the reveal).</summary>
        public void Finish()
        {
            if (!running) return;
            running = false;
            clock = float.MaxValue / 4f;
            Render();
        }

        public void Tick(float unscaledDtMs)
        {
            if (!running) return;
            clock += unscaledDtMs;
            if (clock >= endMs) { Finish(); return; }
            Render();
        }

        // ---- parsing -------------------------------------------------------------------------------------------

        void Parse(string page)
        {
            // *Action* markers (Russian <action>): the words stay, the markers go.
            var action = new List<bool>();
            var sb = new StringBuilder(page.Length);
            char closeMark = '\0';
            for (int i = 0; i < page.Length; i++)
            {
                char c = page[i];
                if (closeMark != '\0' && c == closeMark) { closeMark = '\0'; continue; }
                if (closeMark == '\0' && (c == '*' || c == '<'))
                {
                    char end = c == '*' ? '*' : '>';
                    int close = page.IndexOf(end, i + 1);
                    if (close > i + 1 && page.IndexOf('\n', i + 1, close - i - 1) < 0) { closeMark = end; continue; }
                }
                sb.Append(c);
                action.Add(closeMark != '\0');
            }
            string text = sb.ToString();
            var tints = Highlight(text, speakerName);
            var shout = Shouts(text);
            var exclaim = ExclaimedSentences(text);
            var icons = Icons(text);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (icons[i] != null) letters.Add(new Letter { c = '\uFFFC', sprite = icons[i], pace = 1f });
                bool endOfWord = i + 1 >= text.Length || char.IsWhiteSpace(text[i + 1]);
                float pause = 0f;
                if (c == '.' && i >= 2 && text[i - 1] == '.' && text[i - 2] == '.') pause = EllipsisPauseMs;
                else if (c == '…') pause = EllipsisPauseMs;
                else if (endOfWord && (c == '.' || c == '?' || c == '!') && !(i + 1 < text.Length && text[i + 1] == '.')) pause = StopPauseMs;
                else if (endOfWord && (c == ',' || c == ';' || c == ':')) pause = CommaPauseMs;
                else if (c == '\u3002' || c == '\uFF01' || c == '\uFF1F') pause = StopPauseMs;   // 。！？
                else if (c == '\u3001' || c == '\uFF0C') pause = CommaPauseMs;                   // 、，
                if (i + 1 >= text.Length) pause = 0f;
                letters.Add(new Letter
                {
                    c = c, tint = tints[i], shout = shout[i], action = action[i], pauseAfter = pause,
                    pace = action[i] ? ActionPace : exclaim[i] ? ExclaimPace : 1f,
                });
            }
        }

        /// <summary>All-caps words: 4+ letters, or 2+ followed by '!' (the '!' too), not the acronyms.</summary>
        static bool[] Shouts(string text)
        {
            var result = new bool[text.Length];
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsLetter(text[i])) { i++; continue; }
                int start = i, letterCount = 0;
                bool caps = true;
                while (i < text.Length && (char.IsLetter(text[i]) || text[i] == '\'' || text[i] == '-'))
                {
                    if (char.IsLetter(text[i])) { letterCount++; if (!char.IsUpper(text[i])) caps = false; }
                    i++;
                }
                if (!caps || letterCount < 2) continue;
                string word = text.Substring(start, i - start);
                bool bang = i < text.Length && text[i] == '!';
                if (letterCount < 4 && !bang) continue;
                if (Acronyms.Contains(word)) continue;   // "Ready the EMP!" isn't a shout
                if (NameCaps().Contains(word)) continue;   // "Micro Gun MKII", not a shout
                for (int k = start; k < i; k++) result[k] = true;
                while (i < text.Length && (text[i] == '!' || text[i] == '?')) result[i++] = true;
            }
            return result;
        }

        /// <summary>The letters of sentences that end in '!'.</summary>
        static bool[] ExclaimedSentences(string text)
        {
            var result = new bool[text.Length];
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c != '.' && c != '!' && c != '?' && c != '\n' && c != '\u3002' && c != '\uFF01' && c != '\uFF1F') continue;
                if (c == '!' || c == '\uFF01') for (int k = start; k <= i; k++) result[k] = true;
                start = i + 1;
            }
            return result;
        }

        // ---- rendering -----------------------------------------------------------------------------------------

        static bool IsRightToLeft(string text)
        {
            if (text == null) return false;
            foreach (char c in text) if (c >= '\u0590' && c <= '\u08FF' || c >= '\uFB1D' && c <= '\uFEFC') return true;
            return false;
        }

        void Render()
        {
            shown = 0;
            if (whole)
            {
                label.style.opacity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(clock / endMs));
                return;
            }
            if (alien)
            {
                for (int i = 0; i < glyphs.Count; i++)
                {
                    float age = clock - letters[i].start;
                    if (age >= 0f) shown++;
                    glyphs[i].style.opacity = Mathf.Clamp01(age / FadeMs);
                }
                return;
            }
            var baseColour = label.resolvedStyle.color;
            if (baseColour.a <= 0f) baseColour = Color.white;
            var sb = new StringBuilder(letters.Count * 6);
            string open = null, close = null;
            for (int i = 0; i < letters.Count; i++)
            {
                var l = letters[i];
                float age = clock - l.start;
                if (age >= 0f) shown++;
                if (l.sprite != null)
                {
                    // tint=1: the icon takes the run's colour, white with the letter's alpha, so it fades in like one.
                    if (close != null) sb.Append(close);
                    open = close = null;
                    var white = new Color(1f, 1f, 1f, Quantize(Mathf.Clamp01(age / FadeMs)));
                    sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(white)).Append("><sprite=\"").Append(IconAsset)
                      .Append("\" name=\"").Append(l.sprite).Append("\" tint=1></color>");
                    continue;
                }
                var colour = l.tint ?? baseColour;
                if (l.action) colour = new Color(colour.r * ActionDim, colour.g * ActionDim, colour.b * ActionDim, colour.a);
                float shake = 0f;
                if (!char.IsWhiteSpace(l.c))
                {
                    // Fade in, then settle from the bright tint (16 steps each, so the rich text stays a handful of runs).
                    float alpha = Quantize(Mathf.Clamp01(age / FadeMs));
                    float settle = Quantize(Mathf.Clamp01((age - FadeMs * 0.5f) / SettleMs));
                    colour = Color.Lerp(Color.Lerp(colour, Color.white, 0.75f), colour, settle);
                    colour.a *= alpha;
                    if (l.shout && age > 0f && age < ShakeMs)
                        shake = Mathf.Round(ShakeEm * (1f - age / ShakeMs) * Mathf.Sin(age * 0.09f + i * 2.1f) * 100f) / 100f;
                }
                var tag = new StringBuilder("<color=#").Append(ColorUtility.ToHtmlStringRGBA(colour)).Append('>');
                var end = new StringBuilder();
                if (l.shout) { tag.Append("<size=").Append(ShoutSize.ToString("0")).Append("%><b>"); end.Insert(0, "</b></size>"); }
                if (l.action) { tag.Append("<i>"); end.Insert(0, "</i>"); }
                if (shake != 0f)
                {
                    tag.Append("<voffset=").Append(shake.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).Append("em>");
                    end.Insert(0, "</voffset>");
                }
                end.Append("</color>");
                string t = tag.ToString();
                if (t != open)
                {
                    if (close != null) sb.Append(close);
                    sb.Append(t);
                    open = t;
                    close = end.ToString();
                }
                if (l.c == '<') sb.Append("<noparse><</noparse>");
                else sb.Append(l.c);
            }
            if (close != null) sb.Append(close);
            label.text = sb.ToString();
        }

        static float Quantize(float v) => Mathf.Round(v * 16f) / 16f;

        // ---- icons ---------------------------------------------------------------------------------------------

        /// <summary>The sprite asset in Resources/Sprite Assets/ (TextIconsBuilder.AssetName).</summary>
        const string IconAsset = "gof2_text_icons";

        struct IconRule
        {
            public string word, sprite;
            public bool anyCase, pluralOnly;   // anyCase: matched in lower case; pluralOnly: only "...s" (or capitalised)
        }

        static List<IconRule> iconRules;

        /// <summary>Amounts of money: a number (with , . or space thousands) followed by "$" or a credit word.</summary>
        static readonly System.Text.RegularExpressions.Regex Amount = new System.Text.RegularExpressions.Regex(
            @"(?<![\p{L}\p{N}])\d{1,3}(?:[,.\u00A0\u202F ]\d{3})+(?![\p{N}])(?=\s?\$|\s+(?:credits?|crédits?|créditos?|crediti|credito|kredyt\w*|кредит\w*|크레딧|クレジット))|(?<![\p{L}\p{N}])\d+(?=\s?\$|\s+(?:credits?|crédits?|créditos?|crediti|credito|kredyt\w*|кредит\w*|크레딧|クレジット))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>The icon to put before each letter of 'text' (null = none).</summary>
        static string[] Icons(string text)
        {
            var result = new string[text.Length];
            if (text.Length == 0) return result;
            var claimed = new bool[text.Length];
            foreach (System.Text.RegularExpressions.Match m in Amount.Matches(text))
            {
                result[m.Index] = "coin";
                for (int k = m.Index; k < m.Index + m.Length; k++) claimed[k] = true;
            }
            string lower = text.ToLowerInvariant();
            var used = new HashSet<string>();
            foreach (var rule in IconRules())
            {
                if (rule.sprite != null && used.Contains(rule.sprite)) continue;   // the first mention on the page only
                string hay = rule.anyCase ? lower : text, word = rule.anyCase ? rule.word.ToLowerInvariant() : rule.word;
                int from = 0;
                while (from < hay.Length)
                {
                    int at = hay.IndexOf(word, from, System.StringComparison.Ordinal);
                    if (at < 0) break;
                    int end = at + word.Length;
                    from = end;
                    if (at > 0 && char.IsLetterOrDigit(text[at - 1])) continue;
                    bool plural = end < text.Length && text[end] == 's' && (end + 1 >= text.Length || !char.IsLetterOrDigit(text[end + 1]));
                    if (plural) end++;
                    if (end < text.Length && char.IsLetterOrDigit(text[end])) continue;
                    if (rule.pluralOnly && !plural && !char.IsUpper(text[at])) continue;
                    bool free = true;
                    for (int k = at; k < end; k++) if (claimed[k]) { free = false; break; }
                    if (!free) continue;
                    for (int k = at; k < end; k++) claimed[k] = true;
                    if (rule.sprite == null) continue;   // a phrase that only takes the word ("plasma array"), every time
                    result[at] = rule.sprite;
                    used.Add(rule.sprite);
                    break;
                }
            }
            return result;
        }

        /// <summary>The word -> icon rules, longest word first; rebuilt when the language changes.</summary>
        static List<IconRule> IconRules()
        {
            if (iconRules != null) return iconRules;
            Hook();
            var list = new List<IconRule>();
            var seen = new HashSet<string>();
            void Add(string word, string sprite, bool anyCase, bool pluralOnly = false)
            {
                word = word?.Trim();
                if (string.IsNullOrEmpty(word) || word.Length < 3 || word.IndexOf('\uFFFD') >= 0 || word.StartsWith("#")) return;
                if (!seen.Add((anyCase ? word.ToLowerInvariant() : word) + "|" + (sprite ?? "-"))) return;
                list.Add(new IconRule { word = word, sprite = sprite, anyCase = anyCase, pluralOnly = pluralOnly });
            }
            string[] raceSprites = { "terran", "vossk", "nivelian", "midorian", null, null, null, null, "pirate", "void" };
            for (int r = 0; r < raceSprites.Length; r++) if (raceSprites[r] != null) Add(Localization.Get(406 + r), raceSprites[r], true);
            Add(Localization.Get(547), "gate", true);
            Add(Localization.Get(545), "wormhole", true);
            Add(Localization.Get(271), "blueprint", true);
            Add(Localization.Get(571), "autopilot", true);
            if (Localization.Language == "en")
            {
                Add("jump gate", "gate", true);
                Add("container", "container", true);
                Add("core", "core", true);
                // The same words for something else: no icon ("plasma array" is the supernova structure, not plasma).
                foreach (var phrase in new[] { "plasma array", "core generator", "core of" }) Add(phrase, null, true);
            }
            var db = Database.Load();
            if (db != null)
            {
                var lowerWords = LowerCaseWords();
                for (int i = 0; i < db.Items.Count; i++)
                {
                    string item = Localization.Get(1274 + i);
                    bool single = item.IndexOf(' ') < 0;
                    if (item.Length < 4 || single && lowerWords.Contains(item.ToLowerInvariant())) continue;
                    Add(item, $"item_{db.Items[i].index:000}", !single);
                }
                for (int i = 0; i < db.Ships.Count && i < 64; i++) Add(Localization.Get(913 + i), $"ship_{i:000}", false);
                foreach (var c in CustomShips.All) Add(CustomShips.ShipName(c.index), $"ship_{c.index:000}", false);
                // Equipment categories: the icon of a first item of the category (not the commodities; ore cores = "core").
                var firstOf = new Dictionary<int, int>();
                foreach (var it in db.Items) if (!firstOf.ContainsKey(it.categoryId)) firstOf[it.categoryId] = it.index;
                foreach (var kv in firstOf)
                {
                    if (kv.Key == 22) continue;
                    string sprite = kv.Key == 24 ? "core" : $"item_{kv.Value:000}";
                    Add(Localization.Get(221 + kv.Key), sprite, true, pluralOnly: kv.Key == 11);   // 11 Mine: not "a friend of mine"
                }
            }
            list.Sort((a, b) => b.word.Length.CompareTo(a.word.Length));
            iconRules = list;
            return iconRules;
        }

        // ---- names ---------------------------------------------------------------------------------------------

        const int Person = 0, Place = 1, Thing = 2, FirstRace = 3;
        /// <summary>The races with an emblem (race id -> tint FirstRace + slot): Terran, Vossk, Nivelian, Midorian, pirates, Void.</summary>
        static readonly int[] RaceIds = { 0, 1, 2, 3, 8, 9 };

        /// <summary>Names that only the story texts use (no data table has them): people who never speak, places, factions and
        /// species of the lore, story goods. Found by going through every dialogue text (story pages and radio, the lounge
        /// and freelance lines 370-459 / 700-899); proper nouns, so they read the same in the other languages.</summary>
        static readonly (string name, int kind)[] LoreNames =
        {
            ("Carla Paolini", Person), ("Alice Paolini", Person), ("Paolini", Person), ("Thomas Boyle", Person), ("Boyle", Person),
            ("Smith", Person), ("Squand", Person), ("Thadellonius", Person), ("Maximilius", Person), ("Keithius Maximus", Person),
            ("Urrkt", Person), ("Orssk", Person), ("Emperor", Person), ("Queen of Moogaresh", Person), ("Tweezleboks", Person),
            ("Eden Prime", Place), ("Earth", Place), ("Novaterra", Place), ("Apocce", Place), ("Damarque", Place),
            ("B'akkram", Place), ("Moogaresh", Place), ("Dareius Asteroid Belt", Place), ("Dareius", Place),
            ("Deep Science", Place), ("Mido Confederation of Planets", Place), ("Mido Confederation", Place),
            ("Nivelian Republic", Place), ("Vossk Empire", Place), ("Nivelian Civil War", Place), ("Space Fleet", Place),
            ("Terran Fleet", Place), ("Fleet Command", Place), ("Interstellar Security Agency", Place),
            ("Homespace Security", Place), ("Terran Intelligence", Place), ("Trans-Galactic Academy", Place),
            ("Nivelian Vaults of Névan", Place), ("Névan", Place), ("Janice's Satellites", Place), ("Silky Way", Place),
            ("Octopod", Place), ("Bobolan", Place), ("Rhinocitroll", Place),
            ("K'mirrk Toad Mutagen", Thing), ("K'mirrk Frog", Thing), ("K'Sarr", Thing), ("Alice Drive", Thing),
            ("Multirail", Thing), ("S'kloptorr", Thing), ("Flabbergaster", Thing),
            ("Mutagen", Thing), ("Empire", Place), ("Berger", Place),
        };

        /// <summary>Ranks and forms of address in front of a person's name join its tint ("Lieutenant Commander Brent Snocom").</summary>
        static readonly HashSet<string> Titles = new HashSet<string>
        {
            "Lieutenant", "Commander", "Captain", "Admiral", "Doctor", "Dr.", "Professor", "Counsellor", "Curator", "Chief",
            "Director", "Mr.", "Mrs.", "Ms.",
        };

        /// <summary>Words that make a place's name longer: "Kappa Station", "Weymire System", "Var Hastra Mining Station".</summary>
        static readonly string[] PlaceSuffixes = { " Mining Station", " Station", " System" };

        static readonly Color[] Tints =
        {
            new Color32(0xFF, 0xD3, 0x7A, 0xFF),   // people: pale gold
            new Color32(0x7F, 0xE3, 0xF0, 0xFF),   // places, factions: light aqua
            new Color32(0xF0, 0xA8, 0xDC, 0xFF),   // ships, items, story goods: pink
            // the races, from their emblems (GoF2Hud/race_N), lightened to read on the dark panels
            new Color32(0xFF, 0x8C, 0x1A, 0xFF),   // Terran: orange (emblem #E08F02)
            new Color32(0x45, 0xE0, 0x6A, 0xFF),   // Vossk: green (#00CC3F)
            new Color32(0x5C, 0x8D, 0xFF, 0xFF),   // Nivelian: blue (#6687A9)
            new Color32(0x9A, 0xA0, 0xA6, 0xFF),   // Midorian: grey (the black emblem, #292823)
            new Color32(0xF0, 0xEA, 0xD8, 0xFF),   // pirates: bone white (the skull, #C3C3C3)
            new Color32(0xA7, 0x7B, 0xFF, 0xFF),   // Void: violet (#4E22AC)
        };

        static List<(string name, int kind)> names;
        static HashSet<string> nameCaps;
        static bool hooked;

        /// <summary>The all-caps words in item, ship, station and system names.</summary>
        static HashSet<string> NameCaps()
        {
            if (nameCaps != null) return nameCaps;
            Hook();
            nameCaps = new HashSet<string>();
            void Scan(string n)
            {
                if (string.IsNullOrEmpty(n)) return;
                foreach (var w in n.Split(' ', '-', '/', '(', ')'))
                {
                    int letters = 0;
                    bool caps = true;
                    foreach (char c in w) if (char.IsLetter(c)) { letters++; if (!char.IsUpper(c)) caps = false; }
                    if (caps && letters >= 2) nameCaps.Add(w.Trim('.', ',', '!', '?'));
                }
            }
            var db = Database.Load();
            if (db != null)
            {
                for (int i = 0; i < db.Items.Count; i++) Scan(Localization.Get(1274 + i));
                for (int i = 0; i < db.Ships.Count && i < 64; i++) Scan(Localization.Get(913 + i));
                foreach (var c in CustomShips.All) Scan(CustomShips.ShipName(c.index));
                foreach (var st in db.Stations) Scan(st.name);
                foreach (var sy in db.Systems) Scan(sy.name);
            }
            return nameCaps;
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Localization.Changed += () => { names = null; nameCaps = null; iconRules = null; };
        }

        /// <summary>The tint per letter of the names in 'page' (null = the label's colour). A name matches as a whole word,
        /// also with a plural "s" ("Midorians"); a person's titles and a place's "Station" / "System" join it.</summary>
        static Color?[] Highlight(string page, string speaker)
        {
            var result = new Color?[page.Length];
            var list = new List<(string, int)>();
            if (!string.IsNullOrWhiteSpace(speaker))
            {
                list.Add((speaker.Trim(), Person));
                foreach (var part in speaker.Split(' ')) if (part.Length >= 3 && char.IsUpper(part[0])) list.Add((part, Person));
            }
            // Multiplayer: the session's pilots are people too (a server dialog's %player%, other players named in it).
            if (GoF2Remake.Multiplayer.NetGame.Active)
                foreach (var p in GoF2Remake.Multiplayer.NetPlayer.All)
                    if (p != null && p.IsSpawned && p.DisplayName.Trim().Length >= 2) list.Add((p.DisplayName.Trim(), Person));
            list.AddRange(Names());
            string lowerPage = page.ToLowerInvariant();
            foreach (var (name, kind) in list)
            {
                // Things of two or more words match in any case ("Khador drive", "energy cells"), the rest as written.
                bool anyCase = kind == Thing && name.IndexOf(' ') > 0;
                string hay = anyCase ? lowerPage : page, needle = anyCase ? name.ToLowerInvariant() : name;
                int from = 0;
                while (from < page.Length)
                {
                    int at = hay.IndexOf(needle, from, System.StringComparison.Ordinal);
                    if (at < 0) break;
                    int end = at + name.Length;
                    from = end;
                    if (at > 0 && char.IsLetterOrDigit(page[at - 1])) continue;
                    if (end < page.Length && page[end] == 's' && (end + 1 >= page.Length || !char.IsLetterOrDigit(page[end + 1]))) end++;
                    if (end < page.Length && char.IsLetterOrDigit(page[end])) continue;
                    if (Taken(result, at, end)) continue;   // longer names first
                    if (kind == Person) at = WithTitles(page, result, at);
                    if (kind == Place)
                        foreach (var suffix in PlaceSuffixes)
                            if (string.CompareOrdinal(page, end, suffix, 0, suffix.Length) == 0 && !Taken(result, end, end + suffix.Length))
                            {
                                end += suffix.Length;
                                break;
                            }
                    for (int i = at; i < end; i++) if (!char.IsWhiteSpace(page[i])) result[i] = Tints[kind];
                    from = end;
                }
            }
            return result;
        }

        static bool Taken(Color?[] result, int from, int to)
        {
            for (int i = from; i < to && i < result.Length; i++) if (result[i].HasValue) return true;
            return false;
        }

        /// <summary>The start of the titles right before 'at' ("Lieutenant Commander " + name).</summary>
        static int WithTitles(string page, Color?[] result, int at)
        {
            while (true)
            {
                int end = at;
                while (end > 0 && page[end - 1] == ' ') end--;
                if (end == at || end == 0) return at;
                int start = end;
                while (start > 0 && !char.IsWhiteSpace(page[start - 1])) start--;
                string word = page.Substring(start, end - start);
                if (!Titles.Contains(word) || Taken(result, start, end)) return at;
                at = start;
            }
        }

        /// <summary>Story speakers with proper names (and their first / last names), races, stations and systems (places),
        /// ships and items (things; a one-word item only when the texts never write it in lower case, so "Gold" and
        /// "Drugs" stay plain), and the lore names; longest first. Rebuilt when the language changes.</summary>
        static List<(string, int)> Names()
        {
            if (names != null) return names;
            Hook();
            var set = new Dictionary<string, int>();
            void Add(string n, int kind)
            {
                n = n?.Trim();
                if (string.IsNullOrEmpty(n) || n.Length < 3 || n.IndexOf('�') >= 0 || set.ContainsKey(n)) return;
                set[n] = kind;
            }
            // Speakers with a personal name (not "Pirate", "Computer", "Barkeeper" ...); "T." and "Dr." aren't names.
            int[] people = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 20, 24, 25, 26, 31, 32, 34, 36, 37, 38, 39, 40, 44, 45, 46, 47, 55, 60 };
            foreach (int s in people)
            {
                string full = StoryTable.SpeakerName(s);
                Add(full, Person);
                foreach (var part in full.Split(' '))
                    if (part.Length >= 4 && !part.EndsWith(".") && char.IsUpper(part[0])) Add(part, Person);
            }
            foreach (var (n, kind) in LoreNames) Add(n, kind);
            var db = Database.Load();
            if (db != null)
            {
                foreach (var st in db.Stations) Add(st.name, Place);
                foreach (var sy in db.Systems) Add(sy.name, Place);
                for (int i = 0; i < db.Ships.Count && i < 64; i++) Add(Localization.Get(913 + i), Thing);   // 977+ = descriptions
                foreach (var c in CustomShips.All) Add(CustomShips.ShipName(c.index), Thing);
                var lower = LowerCaseWords();
                for (int i = 0; i < db.Items.Count; i++)
                {
                    string item = Localization.Get(1274 + i);
                    if (item.Length < 4 || item.IndexOf(' ') < 0 && lower.Contains(item.ToLowerInvariant())) continue;
                    Add(item, Thing);
                }
            }
            // The races in their emblem colours, in either case ("pirates"; the Void capitalised only); the other races (Multipod, Bobolian, Grey ...)
            // are places. A place or faction named after a race takes its colour ("Vossk Empire", "Terran Fleet").
            var raceWords = new List<(string word, int kind)>();
            for (int slot = 0; slot < RaceIds.Length; slot++)
            {
                string race = Localization.Get(406 + RaceIds[slot]);
                if (string.IsNullOrEmpty(race)) continue;
                raceWords.Add((race, FirstRace + slot));
                set[race] = FirstRace + slot;
                string lowerFirst = char.ToLowerInvariant(race[0]) + race.Substring(1);
                if (lowerFirst != race && RaceIds[slot] != 9 && !set.ContainsKey(lowerFirst)) set[lowerFirst] = FirstRace + slot;   // not "the void"
            }
            for (int r = 0; r <= 9; r++) if (System.Array.IndexOf(RaceIds, r) < 0) Add(Localization.Get(406 + r), Place);
            foreach (var key in new List<string>(set.Keys))
                if (set[key] == Place)
                    foreach (var (word, kind) in raceWords)
                        if (key.StartsWith(word + " ", System.StringComparison.Ordinal)) { set[key] = kind; break; }
            names = new List<(string, int)>();
            foreach (var kv in set) names.Add((kv.Key, kv.Value));
            names.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));
            return names;
        }

        /// <summary>Every word the text table writes in lower case (ordinary words, not names).</summary>
        static HashSet<string> LowerCaseWords()
        {
            var set = new HashSet<string>();
            var word = new StringBuilder();
            for (int id = 0; id < Localization.Count; id++)
            {
                string s = Localization.Get(id);
                for (int i = 0; i <= s.Length; i++)
                {
                    char c = i < s.Length ? s[i] : ' ';
                    if (char.IsLetter(c) || c == '\'') { word.Append(c); continue; }
                    if (word.Length > 0 && char.IsLower(word[0])) set.Add(word.ToString());
                    word.Clear();
                }
            }
            return set;
        }
    }
}
