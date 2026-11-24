// DerivedImages.cs
// Feature: docs/notes/player-assets.md "The content layer": the images the Editor's builders cut from the original's interface
// atlases, made again from the player's converted copy by the converter (plain C#, no Unity types): the flight HUD's
// (HudImageBuilder: Resources/GoF2Hud/<name>.png, its table here, shared with the builder) and the shop's item and ship icons
// (ItemIconBuilder: Resources/GoF2Icons/item_NNN.png / ship_NNN.png, ItemIconTable). Pixels are RGBA8 rows top first, as
// AeiImage decodes them, so the atlas rects (top-left based) are used as they are.

using System;
using System.Collections.Generic;

namespace GoF2Remake.Data
{
#if UNITY_5_3_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]   // (only constant tables: nothing to reset)
#endif
    public static class DerivedImages
    {
        /// <summary>A decoded atlas: RGBA8, rows top first.</summary>
        public sealed class Atlas { public byte[] Rgba; public int Width, Height; }

        // the HUD's atlases (Textures/textures/<name>)
        public const string Main = "gof2_interface_iphone4.png", Ipad = "gof2_interface2_ipad_large.png", Low = "gof2_interface.png",
                            Logos = "gof2_logos_ipad2.png", Ipad3 = "gof2_interface3_ipad_large.png", Items = "gof2_items_ipad_large.png",
                            Challenge = "gof2_challenge_interface_ipad_large.png";

        /// <summary>Every atlas the images here are cut from (by its file name without .png).</summary>
        public static IEnumerable<string> AtlasNames()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var im in Hud) set.Add(Stem(im.atlas));
            foreach (var ic in ItemIconTable.Icons) { set.Add(ic.atlas); if (ic.frameAtlas.Length > 0) set.Add(ic.frameAtlas); }
            return set;
        }

        static string Stem(string png) => png.EndsWith(".png") ? png.Substring(0, png.Length - 4) : png;

        // atlas, name, x, y, w, h, frames (0 = single image), wrap (marquee strips repeat)
        public static readonly (string atlas, string name, int x, int y, int w, int h, int frames, bool repeat)[] Hud =
        {
            (Main, "lock", 1, 179, 1920, 80, 24, false),      // 0x456 lock ring, 24 frames 80x80
            (Main, "plate", 1601, 793, 408, 57, 0, false),    // 0x4c4 target plate
            (Main, "class", 447, 2002, 180, 29, 5, false),    // 0x44e class letters A B C D E, 36x29
            (Main, "disc_even_s", 1, 1900, 40, 40, 0, false), // 0x4e2 disc quadrants (top-left quarter of a disc)
            (Main, "disc_even_m", 1, 379, 140, 140, 0, false),   // 0x4dd
            (Main, "disc_even_l", 639, 934, 250, 250, 0, false), // 0x4de
            (Main, "disc_odd_s", 543, 884, 40, 40, 0, false),    // 0x4e1
            (Main, "disc_odd_m", 337, 1720, 140, 140, 0, false), // 0x4df
            (Main, "disc_odd_l", 893, 935, 250, 250, 0, false),  // 0x4e0
            (Main, "core_void", 181, 1607, 89, 66, 0, false),    // 0x522 core of Void Crystals
            (Main, "core", 545, 739, 80, 79, 0, false),          // 0x523 core of the other ores
            (Main, "drill_ring", 1934, 660, 81, 81, 0, false),   // 0x4e7
            (Main, "drill", 639, 855, 660, 66, 10, false),       // 0x4e6 drill bit, 10 frames 66x66
            (Main, "energy_frame", 651, 261, 408, 34, 0, false), // 0x4e3
            (Main, "energy_fill", 614, 1634, 388, 14, 0, false), // 0x4e8
            (Main, "charge_frame", 705, 1259, 408, 69, 0, false), // 0x53a the Khador Drive / cloak charge bar (combat_equipment.md)
            (Main, "charge_fill", 605, 1610, 388, 14, 0, false),  // 0x539 its fill, drawn from the centre outward
            (Main, "energy_label", 375, 160, 27, 8, 0, false),   // 0x4ed
            (Main, "strip_red", 1452, 1, 194, 11, 0, true),      // 0x4eb scrolling data strips
            (Main, "strip_orange", 162, 1, 194, 12, 0, true),    // 0x4ec
            (Main, "strip_blue", 1706, 147, 103, 18, 0, true),   // 0x4e4 depth strip next to the drill
            (Main, "ore_box", 337, 2002, 108, 45, 0, false),     // 0x4e5 box behind the ore amount
            // Navigation (autopilot_travel.md): bracket 0x4f2, jumpgate icon 0x453, autopilot 0x4b0 / 0x4b1, fast-forward
            // 0x541 / 0x540 and their pill 0x53f, race icons 0x4a1 0x49c 0x49f 0x49e (Terran, Vossk, Nivelian, Midorian),
            // 0x4a0 pirates, 0x49d void.
            (Ipad, "bracket", 292, 1653, 81, 81, 0, false),
            (Ipad, "gate_icon", 1776, 17, 26, 26, 0, false),
            (Ipad, "wormhole_icon", 355, 1578, 58, 58, 0, false),       // 0x450 the wormhole off the centre (Radar::draw)
            (Logos, "ending_logo", 0, 0, 452, 156, 0, false),           // 0x1b5a the ending's logo (dialogue_cutscenes.md 3.4)
            // Hud::drawChallengeModeScore (supernova_challenge.md 6.2): the Supernova Challenge's digits 0-9 (images 6000-6009) and "COMBOx" (6010).
            (Challenge, "challenge_digit_0", 173, 89, 84, 88, 0, false),
            (Challenge, "challenge_digit_1", 1, 179, 84, 88, 0, false),
            (Challenge, "challenge_digit_2", 1, 89, 84, 88, 0, false),
            (Challenge, "challenge_digit_3", 1, 359, 84, 88, 0, false),
            (Challenge, "challenge_digit_4", 87, 89, 84, 88, 0, false),
            (Challenge, "challenge_digit_5", 87, 179, 84, 88, 0, false),
            (Challenge, "challenge_digit_6", 1, 269, 84, 88, 0, false),
            (Challenge, "challenge_digit_7", 87, 269, 84, 88, 0, false),
            (Challenge, "challenge_digit_8", 87, 359, 84, 88, 0, false),
            (Challenge, "challenge_digit_9", 259, 89, 84, 88, 0, false),
            (Challenge, "challenge_combo", 1, 1, 471, 86, 0, false),
            (Ipad, "autopilot", 1, 515, 109, 109, 0, false),
            (Ipad, "autopilot_on", 1097, 782, 109, 109, 0, false),
            (Ipad, "fastforward", 1120, 1295, 109, 109, 0, false),
            (Ipad, "fastforward_on", 1208, 782, 109, 109, 0, false),
            (Ipad, "button_pill", 1740, 112, 126, 293, 0, false),
            (Low, "flare_0", 71, 934, 64, 64, 0, false),     // 1288 lens flare: big hexagon (space_backdrop.md)
            (Low, "flare_1", 160, 902, 64, 64, 0, false),    // 1289 small hexagon
            (Low, "flare_2", 285, 601, 64, 64, 0, false),    // 1290 glow
            // Touch flight controls (touch_hud.md 1): stick, fire (+ the action arrow), the right cluster's background (secondary
            // socket bottom / left), boost, pause, quick menu, secondary, the camera button's next-mode icons, the auto-turret
            // toggle, the secondary name plate and the throttle gauge.
            (Ipad, "touch_stick_base", 1097, 489, 291, 291, 0, false),  // 0x4c1
            (Ipad, "touch_stick_knob", 112, 1470, 181, 181, 0, false),  // 0x4b6
            (Ipad, "touch_stick_knob_on", 1122, 1080, 181, 181, 0, false), // 0x4b7
            (Ipad, "touch_fire", 1440, 211, 198, 198, 0, false),        // 0x4b4
            (Ipad, "touch_fire_on", 1316, 1169, 198, 198, 0, false),    // 0x4b5
            (Ipad, "touch_action", 112, 1890, 134, 128, 0, false),      // 0x536 the action arrow on the fire button
            (Ipad, "touch_cluster_left", 1501, 489, 401, 396, 0, false),   // 0x4c6 (secondary socket on the left)
            (Ipad, "touch_cluster_bottom", 1636, 1033, 397, 406, 0, false), // 0x6aa (secondary socket at the bottom)
            (Ipad, "touch_boost", 1390, 600, 109, 109, 0, false),       // 0x4b2
            (Ipad, "touch_boost_on", 893, 1547, 109, 109, 0, false),    // 0x4b3
            (Ipad, "touch_pause", 248, 1890, 109, 109, 0, false),       // 0x4b8
            (Ipad, "touch_pause_on", 1, 1325, 109, 109, 0, false),      // 0x4b9
            (Ipad, "touch_menu", 239, 1214, 125, 78, 0, false),         // 0x4ba quick menu
            (Ipad, "touch_menu_on", 112, 1214, 125, 78, 0, false),      // 0x4bb
            (Ipad, "touch_secondary", 1, 236, 109, 109, 0, false),      // 0x4bc
            (Main, "touch_secondary_on", 1, 887, 77, 77, 0, false),     // 0x4bd (only in the phone atlas: 77 px, touch_hud.md 12.1)
            (Ipad, "touch_cam_0", 1, 1923, 109, 122, 0, false),         // 0x528 next mode: standard
            (Ipad, "touch_cam_0_on", 1904, 819, 109, 122, 0, false),    // 0x527
            (Ipad, "touch_cam_1", 1, 928, 109, 122, 0, false),          // 0x4e9 next mode: turret
            (Ipad, "touch_cam_1_on", 1, 1624, 109, 122, 0, false),      // 0x4ea
            (Ipad, "touch_cam_2", 1, 112, 109, 122, 0, false),          // 0x4be next mode: cockpit (the eye; the original never shows it)
            (Ipad, "touch_cam_2_on", 1390, 807, 109, 122, 0, false),    // 0x4bf
            (Ipad, "touch_cam_3", 888, 1382, 109, 122, 0, false),       // 0x52a next mode: free look
            (Ipad, "touch_cam_3_on", 1, 1160, 109, 122, 0, false),      // 0x529
            (Ipad, "touch_turret", 1, 1, 109, 109, 0, false),           // 0x547 auto turret off
            (Ipad, "touch_turret_on", 1904, 708, 109, 109, 0, false),   // 0x546 auto turret on / pressed
            (Main, "touch_secondary_plate", 1253, 533, 374, 37, 0, false), // 0x4c2 bottom-centre name plate
            (Main, "throttle_gauge", 1337, 1105, 162, 83, 0, false),    // 0x548 half ring under the crosshair
            (Ipad, "time_extender", 1904, 489, 109, 109, 0, false),     // 0x543 idle clock (combat_equipment.md 3.1)
            (Ipad, "time_extender_on", 1390, 489, 109, 109, 0, false),  // 0x542 active / pressed / flashing
            (Ipad, "emp_bar", 112, 2020, 114, 10, 0, false),            // 0x4d8 EMP bar frame under a near ship (4.1)
            (Ipad, "emp_fill", 1581, 89, 110, 6, 0, false),             // 0x4d7 white fill
            (Main, "autopilot_title", 1, 1982, 83, 51, 0, false),   // 0x4f4 autopilot menu title icon
            (Main, "race_0", 226, 90, 36, 36, 0, false),
            (Main, "race_1", 273, 1889, 36, 36, 0, false),
            (Main, "race_2", 272, 1650, 36, 36, 0, false),
            (Main, "race_3", 2002, 222, 36, 36, 0, false),
            (Main, "race_8", 250, 1010, 36, 36, 0, false),
            (Main, "race_9", 83, 1900, 36, 36, 0, false),
            // Star map (starmap_travel.md 11.3): rings 0x48a / 0x48c, "you are here" pulse 0x4fd, legend icons visited 0x4a2,
            // story 0x454, freelance 0x455, products 0x452; big race logos 0x4a6 0x4a3 0x4a5 0x4a4 (system header, orbit info).
            (Main, "map_ring", 269, 47, 99, 99, 0, false),
            (Main, "map_ring_selected", 317, 739, 143, 143, 0, false),
            (Main, "map_pulse", 1, 1607, 99, 99, 0, false),
            (Main, "map_visited", 326, 148, 21, 18, 0, false),
            (Main, "map_story", 475, 147, 26, 23, 0, false),
            (Main, "map_freelance", 547, 124, 26, 23, 0, false),
            (Main, "map_products", 370, 47, 20, 18, 0, false),
            (Main, "map_home", 249, 487, 18, 18, 0, false),       // 0x545 orange house (the owned Kaamo Club)
            (Main, "logo_0", 337, 1464, 93, 103, 0, false),
            (Main, "logo_1", 1875, 1137, 98, 89, 0, false),
            (Main, "logo_2", 1659, 35, 90, 90, 0, false),
            (Main, "logo_3", 825, 1425, 88, 97, 0, false),
            // Combat (ship_combat.md 7.9): ship markers per faction (red enemy, green friend, yellow neutral): dot 0x4cc
            // 0x4cd 0x4cb, locked ring 0x4c8 0x4ca 0x4c9, near bracket 0x4db 0x4d2 0x4dc, hull bar frame 0x4da 0x4d3 0x4d5 /
            // fill 0x4d9 0x4d4 0x4d6; crates 0x4f1 (far) 0x451 / 0x44d (off screen); player status (0x4ac/0x4ad shield
            // icon, 0x4aa/0x4ab hull icon, 0x4a9 plate, 0x4ae/0x4af shield bar, 0x4a7/0x524/0x4a8 hull + armor bar), hit
            // arcs 0x52c/0x52b (blue) 0x526/0x525 (red), message background 0x4c3.
            (Ipad, "ship_dot_enemy", 66, 1284, 36, 36, 0, false),
            (Ipad, "ship_dot_friend", 1761, 61, 36, 36, 0, false),
            (Ipad, "ship_dot_neutral", 292, 58, 36, 36, 0, false),
            (Ipad, "ship_ring_enemy", 232, 17, 58, 58, 0, false),
            (Ipad, "ship_ring_friend", 292, 1382, 58, 58, 0, false),
            (Ipad, "ship_ring_neutral", 38, 1748, 58, 58, 0, false),
            (Ipad, "ship_bracket_enemy", 292, 1294, 81, 81, 0, false),
            (Ipad, "ship_bracket_friend", 1, 1840, 81, 81, 0, false),
            (Ipad, "ship_bracket_neutral", 149, 17, 81, 81, 0, false),
            (Ipad, "ship_bar_enemy", 399, 89, 114, 10, 0, false),
            (Ipad, "ship_bar_friend", 1821, 89, 114, 10, 0, false),
            (Ipad, "ship_bar_neutral", 1329, 93, 114, 10, 0, false),
            (Ipad, "ship_fill_enemy", 1445, 93, 110, 6, 0, false),
            (Ipad, "ship_fill_friend", 399, 101, 110, 6, 0, false),
            (Ipad, "ship_fill_neutral", 1445, 101, 110, 6, 0, false),
            (Ipad, "crate_dot", 61, 455, 39, 39, 0, false),
            (Ipad, "crate_off", 366, 1214, 58, 58, 0, false),
            (Ipad, "crate_off_void", 295, 1578, 58, 58, 0, false),
            (Main, "status_shield", 511, 78, 41, 42, 0, false),
            (Main, "status_shield_hit", 503, 122, 41, 42, 0, false),
            (Main, "status_hull", 272, 749, 41, 42, 0, false),
            (Main, "status_hull_red", 250, 966, 41, 42, 0, false),
            (Ipad, "status_plate", 112, 1741, 247, 39, 0, false),
            (Ipad, "status_shield_frame", 829, 93, 248, 14, 0, false),
            (Ipad, "status_shield_fill", 149, 1, 248, 14, 0, false),
            (Ipad, "status_hull_frame", 579, 93, 248, 14, 0, false),
            (Ipad, "status_hull_fill", 1440, 195, 248, 14, 0, false),
            (Ipad, "status_armor_fill", 1079, 93, 248, 14, 0, false),
            (Ipad3, "status_gamma", 1, 267, 42, 42, 0, false),           // 0x1f59 gamma shield row (supernova orbits)
            (Ipad3, "status_gamma_frame", 57, 956, 248, 14, 0, false),   // 0x1f5a
            (Ipad3, "status_gamma_fill", 1087, 17, 248, 14, 0, false),   // 0x1f5b
            (Ipad3, "cloud_off", 57, 365, 58, 58, 0, false),             // 0x1f62 a gas cloud off screen (Spectral Filter Omega)
            (Ipad, "hit_side_blue", 112, 112, 324, 1000, 0, false),
            (Ipad, "hit_top_blue", 1012, 1670, 1000, 374, 0, false),
            (Ipad, "hit_side_red", 438, 1074, 325, 892, 0, false),
            (Ipad, "hit_top_red", 438, 112, 1000, 375, 0, false),
            (Main, "message_bg", 1601, 746, 392, 44, 0, false),
            (Ipad, "radar_ellipse", 438, 489, 657, 491, 0, false),   // 0x4c7 the faint radar ellipse's top-left quarter (autopilot_travel.md 1.1)
            (Ipad, "portrait_bg", 1459, 955, 160, 200, 0, false),     // 0x485 ImageFactory::reload (dialogue_cutscenes.md 1.3)
            (Ipad, "portrait_frame", 1879, 249, 160, 200, 0, false),  // 0x511
            // Alien font (resource 1310 = font 1 of texture 10062, Globals::loadFont; AlienText): the magenta glyph row,
            // 26 glyphs = A..Z (the order is assumed, the .aei glyph table wasn't converted), boxes measured from the
            // alpha, all at y 263 so they share a baseline. The same row sits at gof2_interface.png (720..1018, 782).
            (Main, "alien_A", 1064, 263, 22, 31, 0, false), (Main, "alien_B", 1089, 263, 18, 31, 0, false),
            (Main, "alien_C", 1111, 263, 21, 31, 0, false), (Main, "alien_D", 1136, 263, 17, 31, 0, false),
            (Main, "alien_E", 1158, 263, 19, 31, 0, false), (Main, "alien_F", 1178, 263, 20, 31, 0, false),
            (Main, "alien_G", 1200, 263, 20, 31, 0, false), (Main, "alien_H", 1224, 263, 21, 31, 0, false),
            (Main, "alien_I", 1249, 263, 8, 31, 0, false),  (Main, "alien_J", 1261, 263, 21, 31, 0, false),
            (Main, "alien_K", 1286, 263, 17, 31, 0, false), (Main, "alien_L", 1303, 263, 23, 31, 0, false),
            (Main, "alien_M", 1327, 263, 27, 31, 0, false), (Main, "alien_N", 1359, 263, 16, 31, 0, false),
            (Main, "alien_O", 1380, 263, 19, 31, 0, false), (Main, "alien_P", 1402, 263, 22, 31, 0, false),
            (Main, "alien_Q", 1427, 263, 20, 31, 0, false), (Main, "alien_R", 1450, 263, 20, 31, 0, false),
            (Main, "alien_S", 1476, 263, 17, 31, 0, false), (Main, "alien_T", 1496, 263, 21, 31, 0, false),
            (Main, "alien_U", 1517, 263, 23, 31, 0, false), (Main, "alien_V", 1542, 263, 22, 31, 0, false),
            (Main, "alien_W", 1569, 263, 25, 31, 0, false), (Main, "alien_X", 1598, 263, 22, 31, 0, false),
            (Main, "alien_Y", 1625, 263, 18, 31, 0, false), (Main, "alien_Z", 1646, 263, 20, 31, 0, false),
            // HackingGame::HackingGame 0x179638 (the Supernova hacking game, images 0x1f44-0x1f55 of the iPad-large atlas 3):
            // the six code tiles (0x1f4a-0x1f4f) and their solved (gold) versions (0x1f50-0x1f55), the rotate buttons
            // (0x1f44 idle, 0x1f46 turning), the slashed marks on the target pattern (0x1f45), the frames (0x1f49 the
            // puzzle's half, 0x1f48 the target's half, both drawn mirrored) and the bar between them (0x1f47).
            (Ipad3, "hack_tile_0", 717, 843, 256, 180, 0, false), (Ipad3, "hack_tile_1", 57, 425, 256, 180, 0, false),
            (Ipad3, "hack_tile_2", 57, 676, 256, 180, 0, false), (Ipad3, "hack_tile_3", 315, 679, 256, 180, 0, false),
            (Ipad3, "hack_tile_4", 975, 661, 256, 180, 0, false), (Ipad3, "hack_tile_5", 717, 661, 256, 180, 0, false),
            (Ipad3, "hack_gold_0", 57, 183, 256, 180, 0, false), (Ipad3, "hack_gold_1", 1578, 61, 256, 180, 0, false),
            (Ipad3, "hack_gold_2", 975, 479, 256, 180, 0, false), (Ipad3, "hack_gold_3", 57, 1, 256, 180, 0, false),
            (Ipad3, "hack_gold_4", 717, 479, 256, 180, 0, false), (Ipad3, "hack_gold_5", 975, 843, 256, 180, 0, false),
            // The Status window's medals (TouchButton::draw style 4): plates by grade DAT_00252040 (0 none 2416, gold 2414,
            // silver 2415, bronze 2413; elite 36-44 DAT_00252030 8045 / 8035), the pressed overlay 2412, the symbols
            // DAT_0025c7dc (2376 + i, elite 8036..8044), 54x54 grey (tinted by grade).
            // MGame::OnRender2D state 0xd (Action Freeze): the "Galaxy on Fire 2 Full HD" logo 0x534.
            (Main, "photo_logo", 1591, 1595, 281, 97, 0, false),
            // ListItemWindow: the ship's floor glow 0x50b (the left half, drawn mirrored too), the comparison arrows
            // 0x512 worse / 0x513 better / 0x514 equal.
            (Main, "info_floor", 1, 17, 223, 160, 0, false),
            (Main, "compare_worse", 205, 1774, 82, 40, 0, false), (Main, "compare_better", 225, 337, 82, 40, 0, false),
            (Main, "compare_equal", 215, 1424, 82, 40, 0, false),
            // Hud::draw's top readout: the time limit's clock 0x4c5, the cargo hold 0x520 ("load / max t"), the passengers
            // 0x1f43 and their remainder 0x1f42, the goods 0x1f61 and their remainder 0x1f60, the volatile goods' red bar 0x1f5c.
            (Main, "hud_timer", 1497, 855, 133, 40, 0, false), (Ipad, "hud_cargo", 1440, 411, 176, 40, 0, false),
            (Ipad3, "hud_passengers", 57, 907, 191, 40, 0, false), (Ipad3, "hud_goods", 117, 365, 191, 40, 0, false),
            (Ipad3, "hud_goods_left", 57, 858, 222, 40, 0, false), (Ipad3, "hud_volatile", 57, 972, 191, 40, 0, false),
            (Ipad3, "hud_passengers_left", 1477, 1, 222, 40, 0, false),
            (Items,"medal_plate_none",1261,265,228,84,0,false), (Items,"medal_plate_gold",361,353,228,84,0,false),
            (Items,"medal_plate_silver",1081,1055,228,84,0,false), (Items,"medal_plate_bronze",591,353,228,84,0,false),
            (Items,"medal_pressed",901,89,228,84,0,false),
            (Ipad3,"medal_plate_elite_none",1256,588,228,84,0,false), (Ipad3,"medal_plate_elite_gold",315,861,228,84,0,false),
            (Items,"medal_00",1981,791,54,54,0,false),(Items,"medal_01",1752,353,54,54,0,false),(Items,"medal_02",1137,1,54,54,0,false),
            (Items,"medal_03",1584,353,54,54,0,false),(Items,"medal_04",1981,703,54,54,0,false),(Items,"medal_05",933,353,54,54,0,false),
            (Items,"medal_06",1528,353,54,54,0,false),(Items,"medal_07",1976,353,54,54,0,false),(Items,"medal_08",1808,353,54,54,0,false),
            (Items,"medal_09",1981,177,54,54,0,false),(Items,"medal_10",1981,615,54,54,0,false),(Items,"medal_11",1981,527,54,54,0,false),
            (Items,"medal_12",1248,353,54,54,0,false),(Items,"medal_13",877,353,54,54,0,false),(Items,"medal_14",1304,353,54,54,0,false),
            (Items,"medal_15",1,1993,54,54,0,false),(Items,"medal_16",1,441,54,54,0,false),(Items,"medal_17",181,1993,54,54,0,false),
            (Items,"medal_18",1360,353,54,54,0,false),(Items,"medal_19",1101,353,54,54,0,false),(Items,"medal_20",1045,353,54,54,0,false),
            (Items,"medal_21",1969,1,54,54,0,false),(Items,"medal_22",57,1993,54,54,0,false),(Items,"medal_23",989,353,54,54,0,false),
            (Items,"medal_24",1864,353,54,54,0,false),(Items,"medal_25",181,1673,54,54,0,false),(Items,"medal_26",1920,353,54,54,0,false),
            (Items,"medal_27",1696,353,54,54,0,false),(Items,"medal_28",1472,353,54,54,0,false),(Items,"medal_29",1981,439,54,54,0,false),
            (Items,"medal_30",181,1,54,54,0,false),(Items,"medal_31",821,353,54,54,0,false),(Items,"medal_32",1640,353,54,54,0,false),
            (Items,"medal_33",1416,353,54,54,0,false),(Items,"medal_34",113,1993,54,54,0,false),(Items,"medal_35",1913,1,54,54,0,false),
            (Ipad3,"medal_36",1,423,54,54,0,false),(Ipad3,"medal_37",1,479,54,54,0,false),(Ipad3,"medal_38",1,591,54,54,0,false),
            (Ipad3,"medal_39",1,367,54,54,0,false),(Ipad3,"medal_40",1,311,54,54,0,false),(Ipad3,"medal_41",1,113,54,54,0,false),
            (Ipad3,"medal_42",1,535,54,54,0,false),(Ipad3,"medal_43",1,57,54,54,0,false),(Ipad3,"medal_44",1,1,54,54,0,false),
            (Ipad3, "hack_button", 127, 607, 62, 62, 0, false), (Ipad3, "hack_button_on", 1285, 61, 291, 291, 0, false),
            (Ipad3, "hack_blocked", 57, 607, 68, 67, 0, false), (Ipad3, "hack_bar", 315, 1, 770, 58, 0, false),
            (Ipad3, "hack_frame_top", 717, 61, 566, 416, 0, false), (Ipad3, "hack_frame_bottom", 315, 61, 400, 400, 0, false),
            // Radar::draw with a plasma collector: the crosshair while a plasma spark is in range (0x1f5d).
            (Ipad3, "plasma_crosshair", 1285, 354, 214, 214, 0, false),       // 0x1f5d the collector's crosshair, plasma in range
            (Ipad3, "plasma_crosshair_idle", 315, 463, 214, 214, 0, false),  // 0x1f5e the collector's crosshair otherwise
        };

        /// <summary>Modified: the round touch buttons whose thin clear ring showed light specks (the touch controls, the
        /// autopilot / fast-forward / time extender buttons and their pill, the status icons, plate and bars).</summary>
        public static bool Bled(string name) => name.StartsWith("touch_") || (name.StartsWith("autopilot") && !name.EndsWith("_title"))
            || name.StartsWith("fastforward") || name.StartsWith("time_extender") || name == "button_pill"
            || name.StartsWith("status_");

        /// <summary>Modified: the atlas keeps bright colours (white, cyan) in nearly transparent pixels, e.g. in the thin clear
        /// ring of the touch stick, boost and camera buttons. The GPU filters colour and alpha apart, so where the buttons
        /// are drawn smaller or off the pixel grid those colours bled into the opaque neighbours as light specks at the
        /// ring's top, bottom and sides. Pixels below 8 / 255 alpha, and bright ones (a channel from 96 / 255) below 32 / 255
        /// (the time extender's lit clock and the secondary button had cyan up to alpha 24 in the ring), become fully clear
        /// and take the colour of their visible neighbours (the dilation done for 8 steps); the visible pixels keep theirs.</summary>
        public static void BleedEdges(byte[] px, int w, int h)
        {
            int n = w * h;
            var known = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int a = px[i * 4 + 3], mx = Math.Max(px[i * 4], Math.Max(px[i * 4 + 1], px[i * 4 + 2]));
                if (a < 8 || (a < 32 && mx >= 96)) px[i * 4 + 3] = 0;
                known[i] = px[i * 4 + 3] > 0;
            }
            var next = new bool[n];
            for (int step = 0; step < 8; step++)
            {
                Array.Copy(known, next, n);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (known[i]) continue;
                        int r = 0, g = 0, b = 0, k = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h || !known[yy * w + xx]) continue;
                                int j = (yy * w + xx) * 4; r += px[j]; g += px[j + 1]; b += px[j + 2]; k++;
                            }
                        if (k == 0) continue;
                        px[i * 4] = (byte)(r / k); px[i * 4 + 1] = (byte)(g / k); px[i * 4 + 2] = (byte)(b / k);
                        next[i] = true;
                    }
                Array.Copy(next, known, n);
            }
        }

        /// <summary>A rect of an atlas (top-left based), RGBA8 rows top first; pixels outside the atlas clear.</summary>
        public static byte[] Cut(Atlas a, int x, int y, int w, int h)
        {
            var o = new byte[w * h * 4];
            for (int row = 0; row < h; row++)
            {
                int sy = y + row;
                if (sy < 0 || sy >= a.Height) continue;
                for (int col = 0; col < w; col++)
                {
                    int sx = x + col;
                    if (sx < 0 || sx >= a.Width) continue;
                    Buffer.BlockCopy(a.Rgba, (sy * a.Width + sx) * 4, o, (row * w + col) * 4, 4);
                }
            }
            return o;
        }

        /// <summary>The HUD images: (Resources path without extension, RGBA, width, height) each; strips become numbered
        /// frames (name_00...), as HudImageBuilder writes them.</summary>
        public static IEnumerable<(string path, byte[] rgba, int w, int h)> HudImages(Func<string, Atlas> atlas)
        {
            foreach (var im in Hud)
            {
                var a = atlas(Stem(im.atlas));
                if (a == null) continue;
                int frames = Math.Max(1, im.frames), fw = im.w / frames;
                for (int f = 0; f < frames; f++)
                {
                    var px = Cut(a, im.x + f * fw, im.y, fw, im.h);
                    if (Bled(im.name)) BleedEdges(px, fw, im.h);
                    yield return (im.frames > 0 ? $"Resources/GoF2Hud/{im.name}_{f:00}" : $"Resources/GoF2Hud/{im.name}", px, fw, im.h);
                }
            }
        }

        public const int IconW = 180, IconH = 88;

        /// <summary>The shop icons (ItemIconBuilder): the item's / ship's image over its type's frame, centred on a 180 x 88
        /// canvas with a clear border.</summary>
        public static IEnumerable<(string path, byte[] rgba, int w, int h)> ItemIcons(Func<string, Atlas> atlas)
        {
            foreach (var ic in ItemIconTable.Icons)
            {
                var a = atlas(ic.atlas);
                if (a == null) continue;
                var canvas = new byte[IconW * IconH * 4];
                var fa = ic.frameAtlas.Length > 0 ? atlas(ic.frameAtlas) : null;
                if (fa != null) Blend(canvas, fa, ic.fx, ic.fy, ic.fw, ic.fh);
                Blend(canvas, a, ic.x, ic.y, ic.w, ic.h);
                yield return ($"Resources/GoF2Icons/{ic.kind}_{ic.index:000}", canvas, IconW, IconH);
            }
            // the bare ship frame (HullIconBuilder's frame_ship.png): the Mod Builder's runtime ship icons are drawn over it
            foreach (var ic in ItemIconTable.Icons)
            {
                if (ic.kind != "ship" || ic.frameAtlas.Length == 0) continue;
                var fa = atlas(ic.frameAtlas);
                if (fa == null) break;
                var frame = new byte[IconW * IconH * 4];
                Blend(frame, fa, ic.fx, ic.fy, ic.fw, ic.fh);
                yield return ("Resources/GoF2Icons/frame_ship", frame, IconW, IconH);
                break;
            }
        }

        /// <summary>ItemIconBuilder.Blend: a rect alpha-blended onto the canvas, centred.</summary>
        static void Blend(byte[] canvas, Atlas a, int x, int y, int rw, int rh)
        {
            int w = Math.Min(rw, IconW), h = Math.Min(rh, IconH), ox = (IconW - w) / 2, oy = (IconH - h) / 2;
            for (int row = 0; row < h; row++)
            {
                int sy = y + row;
                if (sy < 0 || sy >= a.Height) continue;
                for (int col = 0; col < w; col++)
                {
                    int sx = x + col;
                    if (sx < 0 || sx >= a.Width) continue;
                    int s = (sy * a.Width + sx) * 4, d = ((oy + row) * IconW + ox + col) * 4;
                    float sa = a.Rgba[s + 3] / 255f, da = canvas[d + 3] / 255f, outA = sa + da * (1f - sa);
                    if (outA <= 0f) continue;
                    for (int c = 0; c < 3; c++)
                        canvas[d + c] = (byte)((a.Rgba[s + c] * sa + canvas[d + c] * da * (1f - sa)) / outA);
                    canvas[d + 3] = (byte)(outA * 255f);
                }
            }
        }
    }
}
