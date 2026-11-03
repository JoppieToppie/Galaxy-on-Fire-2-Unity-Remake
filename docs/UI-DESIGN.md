# UI design

How the remake's interface looks and behaves, so new screens match the existing ones. It applies to every UI Toolkit
screen: the main menu, the station, the flight HUD, the star map, the overlays and the Mod Builder. The values below
come from the code (`GoF2Common.uss`, `UiLayout.cs`, `MenuKeys.cs`); when one changes, change it here too.

The rules follow the published game UI guidelines. They are cited where a number comes from one:

- [Xbox Accessibility Guidelines](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/101) (XAG 101 text, 102 contrast, 112 navigation)
- [Steam Deck compatibility](https://partner.steamgames.com/doc/steamdeck/compat)
- [Designing for Xbox and TV](https://learn.microsoft.com/en-us/windows/apps/design/devices/designing-for-tv)
- [Game Accessibility Guidelines](https://gameaccessibilityguidelines.com/)
- Material Design's and Apple's touch target sizes

## 1. Principles

1. **The original's look.** Dark translucent panels, steel-blue / cyan lines, amber for whatever has the focus or is
   pressed, the Serpentine typeface in capitals for titles and buttons. A new screen is built from the existing tokens
   and classes, never new colours or fonts.
2. **Readable everywhere.** One scaling model (section 3) makes text and controls a usable size on a 27″ monitor, a
   720p laptop, a Steam Deck, a TV across the room and a phone. Nothing is smaller than the type scale's caption.
3. **Every input, all the way.** Each screen works with touch, mouse, keyboard alone and a controller alone, and shows
   the prompts for the input in use (sections 9–10).
4. **Any screen shape.** Landscape from 1:1 to 32:9. Layouts stretch or stack; they are never cut off.

## 2. Visual language

### Colours (`GoF2Common.uss` `:root`)

| Token | Value | Use |
|---|---|---|
| `--gof-cyan` | `#3EC8FF` | lines, panel accents, titles, values, sliders |
| `--gof-cyan-soft` | cyan 35 % | outlines of buttons and boxes |
| `--gof-cyan-faint` | cyan 12 % | the fill of a focused / hovered row |
| `--gof-amber` | `#FFA630` | **focus and press only**, plus the title accent line and the primary action (Launch, Start game) |
| `--gof-amber-soft` | amber 30 % | the fill of a focused / selected choice |
| `--gof-text` | `#D8ECF7` | body text |
| `--gof-text-dim` | text 70 % | secondary text: descriptions, metadata, inactive rows |
| `--gof-text-disabled` | text 42 % | disabled controls (3:1, XAG 102's minimum for inactive elements) |
| `--gof-panel` | `rgba(4, 10, 18, 0.93)` | panel backgrounds: a scrim over the 3D scene |
| `--gof-line` | cyan 25 % | thin separators and panel borders |

Factions keep their emblem colours (Terran orange, Vossk green, Nivelian blue, Midorian grey, pirates bone white, Void
violet). Red and green mean hostile and friendly in the HUD and nowhere else.

### Type

- **Serpentine ICG Light** is the original's interface font and the default. Inter is its fallback for Cyrillic and
  Latin Extended-A. CJK and Devanagari come from the system's fonts.
- `.gof-semibold` is the faux-bold style, for titles, buttons and labels.
- Capitals with letter spacing (2–6 px) are for titles, buttons, tabs and prompts. Running text (descriptions,
  dialogue, help) is sentence case without extra spacing (XAG 101: all caps is harder to read in blocks).

### Shapes

- **Panel:** `--gof-panel` fill, a 1 px `--gof-line` border, a 3 px cyan left edge, 2 px radius. Inside it go a title
  (`.panel-title`, cyan, `--text-title`) and the amber accent line under it (`.panel-accent`, 120 × 2 px).
- **Buttons come in three kinds, and only these** (the `--button-*` tokens in `:root`; every screen's button classes
  take them):
  - **List row** (`.menu-button`, `.station-button`, `.choice-button`, the lounge's and save lists' rows): no box. Dim
    text; focused / hovered, the white label slides right, a 3 px amber left edge appears and the row gets the faint
    cyan fill.
  - **Secondary button** (`.gof-button` for new screens; `.dialogue-button`, `.map-button`, `.station-menu-button`,
    `.lounge-back`, `.mbv-button`, `.mp-action`...): a steel-blue fill (`--button-fill`), a `--button-line` outline,
    `--button-radius` 4 px. Focused or hovered: an amber outline, the amber 24 % fill and white text; pressed: solid
    amber with dark text. The dense panels' small ones (`.gof-button--compact`) take a 1 px outline.
  - **Primary action** (`.gof-button--primary`; Launch, Start game, the hangar's Buy / Mount, Keep): the amber 16 % fill
    and an amber outline, 38 % when focused. One per screen at most.
  A new kind of button is a new token value at most, never a new look. Button text is centred both ways (one shared
  rule for the classes without an alignment of their own: UI Toolkit's default is the upper left, which shows as soon
  as the phone layout makes a button taller); list rows keep their left-aligned text.
- **Text field** (`.gof-field`): a dark box with a faint outline that turns amber with the focus, the text centred
  vertically, 64 units tall on phones. A field with no box reads as a label (the multiplayer window's values did).
- **Labels with help:** a field's name stays short; the help goes on a dim line under it, never in brackets after the
  name ("Name in the server browser" / "after a restart"). The Mod Builder's field helpers do it by themselves
  (`ModBuilderScreen.FieldLabel`: a bracket of more than 10 characters at a label's end); short units stay ("Cargo (t)").
- **Modal windows** (the multiplayer window, "Keep these settings?") are opaque and sit over a 60 % black backdrop
  that covers the whole screen; a press on the backdrop closes them where closing loses nothing.
- **Hint box** (`.hint` in `GoF2InputGlyphs.uss`): keycaps or controller buttons over a dim label. Used in the HUD's
  hint column and the hangar's hint strip.

### Tabs

One look in every window: `.tab-button` in a `.tab-row`, or the hangar's, the Missions window's and the Debug
pages' own classes, which match it.

- No box: a dim label on a 3 px underline that is transparent until the tab is open. The row has a 1 px `--gof-line`
  under it.
- **Open tab:** white label, amber underline.
- **Focused** (keys or controller): amber fill at 24 %, amber underline. **Hovered** (mouse): white label.
- Size: `--text-body` for a panel's tabs (Options, Debug), `--text-subhead` for a full window's (hangar, Missions,
  Status).
- Q / E and LB / RB switch tabs from anywhere in the window, and the window's prompt row says so. The tab row fits on
  one line at 1280 units. If it can't, the panel is too narrow: widen it, or shorten the labels.
- **More tabs than room** (the Mod Builder's 13 kinds): the row scrolls, the open tab is always scrolled into view, and
  a More button after it lists every tab in the options' drop-down (`ChoicePopup`). Tabs are never cut off with no
  sign of the rest.

### Interface style: Original (`GoF2Original.uss`)

Options > Graphics "Interface style" (`Settings.OriginalUi`, default Remake) puts `.ui-style-original` on every screen's
root (`UiLayout.Configure`, part of `UiLayout.Revision`). The screens, layouts, scaling and features stay the remake's;
only the skin changes, to the original game's own pieces cut from the Android HD atlases into `Assets/UI/Original`
(`og_*.png`, sources and image ids in the sheet's header): the scanline tile for windows, the steel-blue 3-slice button
that turns amber when focused, hovered or pressed (the original only showed it pressed), the hangar's list rows, the
section and title plates, the check box, the header and footer bars, steel-grey lines and white text. The pieces are
9-sliced (`-unity-slice-*`, `-unity-slice-scale`), so they scale with the interface like every other rule.

- Every rule sits under `.ui-style-original` (one class more than the remake's rule it replaces) in `GoF2Original.uss`
  (tokens and the shared classes) and `Assets/UI/Original/<Screen>.uss` (one sheet per screen sheet), all imported by
  `GoF2Theme.tss`. A new screen class that has a box, a button or a list row gets its original look there too.
- Hover rules are written twice, `.ui-style-original .can-hover .x:hover` and `.ui-style-original.can-hover .x:hover`
  (the class may be on the same root or an ancestor).
- Inline styles (code-built panels such as the flight's docked menu) keep the remake's look.

### Motion

Panels fade and slide 40 px in over 0.28 s with ease-out cubic. Focus changes ease over 0.18 s. Menus that leave the
screen (the station's for a take-off) fly to their own edge and fade over 0.45 s, one element after another. Nothing
loops or flashes for attention, except a warning that is about to cost the player something.

## 3. Scaling (`UiLayout`)

Every screen lays out in **panel units** of a 1920 × 1080 design: a USS `px` is a panel unit, not a screen pixel. Each
screen is drawn at one scale **S**, the largest of these:

| Rule | S | Why |
|---|---|---|
| Desktop | 1 | the design's own size: body text 20 px on a 1080p monitor (XAG's PC minimum is 18 px) |
| Handheld (Steam Deck: `SteamDeck=1`; a small Linux screen) | 1.25 | 30 cm away: the caption reaches Steam Deck's recommended 12 px |
| TV (an Xbox, `UwpDisplay`) | 1.3 | 3 m away: the body text comes out at 26 px, XAG's console minimum |
| Phone (`UnityEngine.Device.Application.isMobilePlatform`, < 7.5″) | 1.2 | the body text at about 1.6 mm on a 20:9 phone, a 900-unit panel (1.5 until 2026-10-23: a 720-unit panel read too big and the main menu's nine rows ran off it) |
| Pixel floor | 11 px ÷ caption | the 16-unit caption is never drawn smaller than 11 screen pixels (a 720p or 540p screen) |

Options > Graphics **Interface size** (80–150 %) multiplies S (XAG 101 asks for user-adjustable text size).

The panel shows 1080 ÷ S units of height, and **never fewer than 1280 units of width** (`PanelScreenMatchMode.Expand`).
A 1:1 or 4:3 screen therefore makes the whole UI smaller rather than cutting it off. Every layout must work at
**1280 × 720 units**, the smallest panel there is: a desktop at 150 % (a phone shows 900 units of height, 720 at 125 %).

`UiLayout.Configure` sets these classes on the screen's root for the USS to adapt to:

| Class | When |
|---|---|
| `layout-narrow` / `layout-square` | aspect below 1.55 / 1.2 |
| `layout-ultrawide` | aspect above 2.3 |
| `layout-compact` | the panel is under 1800 units wide: two side-by-side columns don't both fit, so stack or hide one |
| `layout-phone` / `layout-handheld` / `layout-tv` | the device class |
| `ui-large` | a phone or handheld whose body text is still under 2.15 mm after scaling: lists take their large variant |

A new screen with its own `PanelSettings` calls `UiLayout.Configure` when the screen size or `UiLayout.Revision`
changes, or `UiLayout.Keep(settings, root)` once. Never set a `referenceResolution` by hand.

### Safe area (`SafeArea`)

Interactive and readable elements stay inside the safe area. It is the union of three things:

- the device's cutouts: notch, Dynamic Island, home indicator, punch holes;
- the **screen edge margin** (Options > Graphics; automatically 5 % on a TV, the Xbox guidance);
- on screens wider than the **interface width** (Options > Graphics: 16:9, 21:9 by default, or full width), side bands
  that keep the HUD and menus in a centred band, so the player's eyes don't travel to the edges of a 32:9 screen.

Backgrounds, shades and the 3D view still fill the whole screen. A full-screen overlay keeps its edge elements clear
with `SafeArea.KeepClear` and the `safe-edge` class.

## 4. Type scale

Every `font-size` is one of these tokens (`var(--text-*)`). Code that sets a size uses `UiLayout.Type(px)`, which snaps
up to the scale. The scale itself never changes per platform: the panel scale above does that.

| Token | Units | Use |
|---|---|---|
| `--text-caption` | 16 | tags, badges, keycap letters, one-word metadata, in capitals. **Never running text.** |
| `--text-small` | 18 | the smallest running text: descriptions, hints, secondary lines, prompt labels |
| `--text-body` | 20 | body text, list rows, option labels (the root's default) |
| `--text-label` | 22 | buttons, row titles |
| `--text-subhead` | 24 | section headings, emphasised values |
| `--text-heading` | 28 | panel headings, main menu entries |
| `--text-title` | 34 | screen and panel titles |
| `--text-display` | 64 | splash lines, big numbers |

- **Line length:** a block of running text is at most about 80 characters wide (40 in CJK). With body text that is
  about 900 units: give text panels a `max-width`.
- **Line height:** UI Toolkit doesn't set one. Give long text blocks paragraph spacing (`margin-bottom` ≥ 0.75 em) and
  keep them left-aligned, never justified.
- **Room to grow:** German and Russian run up to 35 % longer than English. Text never sits in a fixed width that clips
  it: use `min-width`, wrapping, or `TextMarquee` for a value in a fixed box.

## 5. Spacing and sizes

- **Spacing steps:** 4, 8, 12, 16, 24, 32 (`--space-*`), on a 4-unit grid: every padding and margin is a multiple of 4
  (values under 4 are hairline gaps; negative ones centre an element). A single value is written as its token
  (`margin-top: var(--space-m)`); a shorthand keeps px on the grid (USS takes no var() inside a shorthand reliably).
  Panels pad 32–44 units; rows sit 4–12 apart.
- **Pointer targets:** at least 40 units high.
- **Touch targets:** on phones every interactive row and button is at least 64 units high (`.layout-phone` in
  `GoF2Common.uss`). That is about 5.5 mm on a 6.7″ phone, under Apple's 44 pt (7 mm), traded for a phone that doesn't read oversized.
  Neighbours sit at least 8 units apart, but for the main menu's rows, which touch on phones (`.layout-phone
  .main-buttons`, centred between the logo and the version line) so that all nine fit between them. A small visual can sit in a larger hit area (padding with a
  negative margin, as `.footer-version` does).
- **Icons** (the "?" help): 34–40 units, with their hit area padded to the touch minimum.

## 6. Layout across aspect ratios

- **Anchor to the safe area's edges or centre**, never to fixed screen positions. Prefer flex layout with percentages
  and `flex-grow` over absolute pixel offsets.
- **16:9 is the design.** At 21:9 and wider the interface width keeps menus and HUD in the centred band. At
  `layout-narrow` / `layout-compact`, side-by-side columns stack, or the secondary column gives way: the main menu hides
  its list while a panel is open.
- **Scroll in one direction only** (XAG 112). A list that may not fit scrolls vertically; a row of cards may scroll
  horizontally. Nothing needs both.
- **Long lists:** a `ScrollView` with its scroll bars hidden (`DragScroll` for touch and mouse drag). The focused row is
  scrolled into view (`ScrollTo` on `FocusInEvent`, unless a drag is under way).
- **Windows fit the screen's height.** The smallest panel is 720 units tall. Every window and panel has a
  `max-height` (at most 92 % of the safe area, or fixed top and bottom offsets). Its title, tabs and footer
  (Back, the prompts) never shrink, and the content between them is the part that scrolls (`flex-shrink: 1;
  min-height: 0` on the scroll view). Never give a scroll view a fixed height taller than half of 720 units: size it
  to its content up to a limit taken from the screen (`PauseMenu.FitToContent`).
- **Station windows** (hangar, Missions, Status) take the full width between the side margins while open; the station's
  side panel and Launch give way. Their prompts sit in the strip under the window.
- **Check every new screen** with `UiShots` (section 11) at all ten sizes before it is merged.

## 7. Contrast and legibility

- Text meets 4.5:1 against what is behind it, and large text / UI elements 3:1 (XAG 102, WCAG). The text tokens on
  `--gof-panel` do. Over the 3D scene the worst case counts (a sun, a bright nebula), so text there sits on a scrim:
  - a panel;
  - `.side-shade` / `.bottom-shade` / `.top-shade` behind menus and the station's header;
  - list rows over the scene at 85 % opacity (the lounge's visitors);
  - or for HUD labels, `text-shadow: 0 2px 6px rgba(0, 0, 0, 0.8)`.
- **High contrast** (Options > Accessibility): `.ui-high-contrast` redefines the colour tokens (opaque panels, 95 %
  secondary text, brighter lines) and makes the main windows solid black. A new window that should go solid joins its
  rule list.
- Never use colour alone to carry meaning. Hostile / friendly markers also differ in shape (brackets, the lock plate),
  and selection uses the amber edge as well as the fill. **Colour-blind markers** (Options > Accessibility) turn the
  friendly markers blue: red-green colour blindness reads green and red alike (`CombatView.MarkerTex`,
  `.ui-colorblind`).

## 8. Focus

- The focused element is always visible: amber text and outline or left edge, plus the faint cyan fill.
- **Hover is for the mouse only.** Hover rules go under `.can-hover`, set only while the mouse is the last input.
  Otherwise a resting cursor lights a second row next to the controller's.
- Hover never starts or interrupts a text/number edit, or takes focus during a captured drag (`UiHover`). Drag-to-scroll
  leaves sliders and text selection their gestures. Check `UiPointerButtons` before treating an empty UI move button mask
  as a lost release: the Input System can still have the mouse button held. A different held mouse button cannot keep a
  primary drag alive. Capture loss, panel detach and leaving the application also cancel map/turntable drags.
- A menu or window gives the focus to its first useful element when it opens. Text fields come last, except in a form.
  A touch press doesn't take the focus, except in a text field, which needs it for the keyboard.
- **Closing something gives the focus back** to what opened it: the star map to Map, the system menu to the button that
  had the focus before (never to Launch: Menu, B, then A out of habit took off), the lounge to Space Lounge. Entering
  the lounge, the keys and a controller start on its first visitor.
- **Scroll bars never take the focus** (`UiFocus.SkipScrollBars` on every screen's root): the D-pad stopped on a list's
  slider, where nothing reacted.

## 8a. Sound

- Three feedback sounds, the original's clips: a focus move by the keys or a controller (event 124 at 0.45), a button's
  click and Back (123). The main menu, the station and the flight HUD play them themselves; any other panel gets them
  from `UiSounds.Attach(root)` (the event screens, the controller keyboard, "Keep these settings?").
- A pointer focusing what it presses makes no move sound: its press clicks.

## 8b. Waiting, notices and empty lists

- **Waiting** (`UiBusy.Mark(label, true)`): a small turning amber arc before the line that says what is going on
  (connecting, starting a session, asking the server, looking for games, loading parts, importing a mod). A wait with
  a known end shows a bar instead (`ModLoadingView`).
- **Notices** that need no answer are toasts (`.gof-toast`: the panel colour, the amber bar on the left, 3 s, fading):
  the station's messages and the screenshot's notice. A message that needs an answer is a dialog.
- **Empty lists say why and what to do** (`missionsNoFreelance`: "No mission taken. The visitors in a Space Lounge
  offer work."; the hangar's `blueprintsNone`), never the original's "-BLANK-" or nothing under a heading.

## 8c. Options that can make a screen unusable

The display mode, the resolution, the interface size and the text size (`OptionDef.confirm`) ask "Keep these
settings?" 0.6 s after the last change, with a 15 s countdown (`SettingsConfirm`). Revert, Back, B or the countdown
running out put every value changed since back. Keep is selected; A / Enter press the selected button, B / Esc revert.

## 9. Navigation

What a controller or keyboard user can rely on everywhere (XAG 112):

| Action | Keyboard | Controller | Touch / mouse |
|---|---|---|---|
| Move | arrows (W A S D where the screen uses them) | D-pad, left stick | — |
| Confirm | Enter (Space) | A / ✕ | tap / click |
| Back / close | Esc | B / ○ | the screen's Back button; Android's back |
| Switch tabs | Q / E | LB / RB (L1 / R1) | tap the tab |
| Change a value | ← / → on the row | D-pad ← / → | tap, drag, the drop-down |
| Menu (pause, system) | Esc | Menu / Options | the menu button |

- **Spatial and predictable.** Up / down / left / right go to the nearest element in that direction (`SpatialNav`),
  in the order the screen shows them. A window's tab row, content and footer are rows in that order.
- **Single-axis lists wrap** (down on the last row selects the first): the main menu, the hangar's lists, the pause
  menu. Grids, two-axis layouts and a panel's rows with a footer under them don't.
- **Back always works** and always goes one step back: close a drop-down or "?" window, then a dialog, then the
  window, then the screen. Dialogs that must be answered take Back as their No / Cancel.
- **Nothing is mouse-only.** Every button, drag and gesture has a key and a controller equivalent: a drag-turn has
  the right stick, a pinch has the triggers.
- **Held directions repeat** (0.4 s, then every 0.08 s); a held key stops at the ends of a list instead of wrapping.

## 10. Button prompts

- **Prompts are on screen, not behind an icon**, for the input in use (Steam Deck: never keyboard glyphs while a
  controller is used). Keyboard and controller show them; touch shows none, since its controls are tapped directly.
  Options > Gameplay "Button hints" turns them off.
- **Glyphs** come from `InputGlyph`: keycaps for keys, the Xbox or PlayStation face for a controller
  (`InputMode.Style`), and the current bindings for rebindable actions (`InputGlyph.For`). A glyph's letter meets the
  caption size too.
- **Where:**

| Screen | Prompts |
|---|---|
| Menus and windows | `MenuKeys.Bar` in the footer: one line of "glyph LABEL" pairs, right-aligned beside the buttons (the main menu at the bottom right; the in-game Options pages in a row under their footer; Missions and Status in the strip under the window, like the hangar) |
| Flight HUD | the hint column (`InputGlyph.TrackHintsOption`) |
| Hangar window | the hint strip under the window |
| Star map | its footer's hint boxes |
| Inspect ship and Action Freeze | the hint row at the top right; on touch its hints are buttons |

- **Glyphs inside buttons** (`InputGlyph.PadPrompt`, with a controller only):
  - **B** sits in every Back, No and Cancel button, since B always presses it.
  - **A** sits in a dialog's answers but shows only on the focused one, since A presses whatever has the focus. Its
    space is kept, so the label doesn't move.
  - Launch shows its own binding's glyph the same way.
  - Once a button has a glyph its text lives in a label: set it with `InputGlyph.SetText`, never `button.text`.
  - A screen whose buttons carry their glyphs needs no separate prompt row for them (the pause menu's questions).
- **Order:** move / select, change, switch tab, confirm, then the screen's own actions, then back.
- A prompt names the action in the player's language (`Localization.Extra` keys such as `hudSelect`, `hudBack`). It
  is rebuilt when the input, the controller style, the bindings or the language change.
- **"?" icons stay:** they explain what a screen is for, not which keys to press.

## 11. Accessibility (Options > Accessibility)

| Option | What it does | Where |
|---|---|---|
| Interface size | 80–150 % of the automatic scale: everything bigger or smaller | `UiLayout.Scale` |
| Text size | 100–200 % of the type scale, the layout unchanged (XAG 101 asks for 200 %) | `.ui-text-115` … `.ui-text-200` redefine every `--text-*` and `--glyph-*` token; code gets the factor through `UiLayout.Type` |
| High contrast | solid panels, brighter secondary text and lines | `.ui-high-contrast` |
| Reduce motion | no sliding or fading UI transitions; the station's and the Mod Builder's menus appear at once | `.ui-reduce-motion *`, `Settings.ReduceMotion` in code |
| Colour-blind markers | friendly ships blue instead of green | `.ui-colorblind`, `CombatView` |

**HUD** (Options > Gameplay): "HUD size" (70–150 %) scales the flight HUD's panels from the corner or edge they hang on
(the status bars, readouts, lock plate, messages, hints, crosshair), apart from the menus' Interface size; the ship
markers, the radar ellipse and the touch controls keep their sizes. "HUD opacity" (30–100 %) fades the markings (bars,
readouts, markers, the radar, hints); messages, the radio, the action prompt, warnings and menus stay opaque
(`FlightHud.ApplyHudLook`).

Camera shake, animated dialogue, auto-play dialogue, button hints and the HUD mode are options of their own (Graphics,
Gameplay). A new screen must stay usable at 200 % text on a 1280 × 720-unit panel: labels wrap or scroll, rows grow,
and a tab row may take a second line, but nothing overlaps or is cut off. At large text sizes the layout goes compact
sooner (`layout-compact` widens its threshold by the text size).

## 12. Text entry

- **Touch:** the system's on-screen keyboard (`TouchScreenKeyboard`; the chat writes nothing to it while it is up,
  `PhoneKeyboard`).
- **Controller** on a platform without a system keyboard (Windows, Linux, Steam Deck): `PadKeyboard`, an on-screen key
  grid. It opens on A over a text field, and `PadKeyboard.Attach(root)` is called once per screen. D-pad / left stick
  move, A types, B deletes, X adds a space, Y is shift, Menu is done and View cancels; the keys also take clicks and taps.
  While it is open the menus ignore their keys (`GameControls.TextEntryOpen`).
- **Xbox / UWP:** the system keyboard opens on focus. The text typed is kept when it closes with B (`KeepKeyboardText`).
- A text field is never the first focus of a panel: it would take the keys (and on Xbox open the keyboard) by itself.

## 13. Checking a screen

1. Open the screen in Play mode and run **GoF2 > Tools > UI Screenshots** (`UiShots`; from the CLI:
   `GoF2Remake.EditorTools.UiShots.Run("tag")`, then poll `UiShots.Status`). Each of these is shot after 1.5 s to
   settle, into `Logs/UiShots/<tag>/`:

| Shot | Screen | Device |
|---|---|---|
| `desktop-1080p` | 1920 × 1080 | desktop |
| `desktop-720p` | 1280 × 720 | desktop |
| `desktop-4x3` | 1440 × 1080 | desktop |
| `desktop-1x1` | 1080 × 1080 | desktop |
| `desktop-21x9` | 2560 × 1080 | desktop |
| `desktop-32x9` | 3840 × 1080 | desktop |
| `deck-1280x800` | 1280 × 800 | handheld |
| `tv-1080p` | 1920 × 1080 | TV |
| `phone-20x9` | 2400 × 1080 | phone |
| `phone-16x9` | 1920 × 1080 | phone |

2. `python Reference/tools/ui/contact_sheet.py Logs/UiShots/<tag>` lays them out as one `sheet.png`.
3. Check that:
   - nothing is clipped, overlapping or wrapped where it shouldn't be;
   - no text is under the caption size;
   - the focus is visible;
   - the prompts match the input;
   - a controller reaches every control and Back leaves.

   PlayerPrefs `debug_ui_device` (1 desktop, 2 handheld, 3 phone, 4 TV) forces a device class by hand.

4. Walk it with a controller: `GoF2Remake.EditorTools.UiWalk.Run("down down a rb b")` presses buttons on a virtual
   gamepad (up down left right a b x y lb rb lt rt menu view, and wait) and `UiWalk.Result` lists the focused element
   after each press. Every control must be reached in screen order, the first focus must land on a useful control, and B
   must leave.
5. Check it once more at Text size 200 % with High contrast on.

## 14. Exceptions

- **The Mod Builder** is an authoring tool built for mouse and keyboard, and touch on tablets: its 3D gizmo and its
  graph editor need a pointer. Its fields, lists and buttons follow the type scale, the scaling and the safe area like
  every screen, but it is the one screen without full controller navigation.
- **The flight HUD's touch controls** keep the original's image sizes and places (`TouchControls`); the original
  designed them for touch.

## 15. Where things are

| | |
|---|---|
| Tokens, shared controls (sliders, toggles, drop-downs, menu buttons, options rows, orbit views) | `Assets/UI/GoF2Common.uss` |
| Prompts, keycaps, controller glyphs | `Assets/UI/GoF2InputGlyphs.uss`, `InputGlyph.cs`, `PadGlyph.cs`, `MenuKeys.cs` |
| Scaling, device classes, type snapping | `Assets/Scripts/Runtime/Space/UI/UiLayout.cs` |
| Safe area, edge margin, interface width | `SafeArea.cs`, `UiLayout.OptionInsets` |
| Small-screen large variants of the multiplayer windows | `UiScale.cs` |
| Screenshot check, controller walk | `Assets/Scripts/Editor/UiShots.cs`, `UiWalk.cs`, `Reference/tools/ui/contact_sheet.py` |
| Controller keyboard | `PadKeyboard.cs` |
| Accessibility options | `Settings` (`AccessibilityKeys`), `OptionsCatalog` (`OptionPage.Accessibility`) |
