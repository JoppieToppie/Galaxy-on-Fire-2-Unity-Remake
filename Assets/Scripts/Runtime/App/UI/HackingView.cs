// HackingView.cs
// The hacking minigame on the flight HUD (HackingGame::render2D 0x179bd0): the puzzle (two rows of three code tiles) in its
// frame above the screen centre, the bar across the centre, the target pattern in the smaller frame below, the two turn
// buttons between the puzzle's rows (a glowing star while that block turns) and slashed marks at the same spots on the
// target. A turn slides the block's four tiles clockwise over 300 ms; when solved the tiles blink gold every 200 ms.
// The original's iPad-large sizes are scaled by 1080 / 1536 (the panel's reference height). The Layout offsets
// (+0x30c..+0x318) aren't known and are 0.
// Input (remake): a click / tap on a button or on the puzzle's left / right half; keyboard A / Left / Q and D / Right / E;
// controller LB / D-pad left and RB / D-pad right.

using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public class HackingView
    {
        const float S = 1080f / 1536f;
        static readonly float TileW = 256f * S, TileH = 180f * S, BarW = 770f * S, BarH = 58f * S;
        static readonly float TopW = 566f * S, TopH = 416f * S, BottomW = 400f * S, BottomH = 400f * S;
        static readonly float ButtonSize = 62f * S, GlowSize = 291f * S, BlockedW = 68f * S, BlockedH = 67f * S;

        readonly VisualElement layer;
        readonly VisualElement[] tiles = new VisualElement[6], targets = new VisualElement[6];
        readonly VisualElement[] buttons = new VisualElement[2], glows = new VisualElement[2];
        readonly Texture2D[] tileTex = new Texture2D[6], goldTex = new Texture2D[6];
        readonly System.Collections.Generic.List<VisualElement> unsolvedOnly = new System.Collections.Generic.List<VisualElement>();
        HackingGame game;

        static Texture2D Tex(string name) => GoF2Remake.Data.OriginalTextures.Image("GoF2Hud/" + name);

        public HackingView(VisualElement root)
        {
            layer = new VisualElement { name = "hacking", pickingMode = PickingMode.Ignore };
            layer.AddToClassList("hacking-layer");
            root.Add(layer);
            for (int i = 0; i < 6; i++) { tileTex[i] = Tex("hack_tile_" + i); goldTex[i] = Tex("hack_gold_" + i); }

            // Frames (mirrored halves), the bar.
            Image(Tex("hack_frame_top"), -TopW, -BarH / 2f - TopH, TopW, TopH, false);
            Image(Tex("hack_frame_top"), 0f, -BarH / 2f - TopH, TopW, TopH, true);
            // render2D: the target's frame, the bar and the slashed marks only while unsolved.
            unsolvedOnly.Add(Image(Tex("hack_frame_bottom"), -BottomW, BarH / 2f, BottomW, BottomH, false));
            unsolvedOnly.Add(Image(Tex("hack_frame_bottom"), 0f, BarH / 2f, BottomW, BottomH, true));
            unsolvedOnly.Add(Image(Tex("hack_bar"), -BarW / 2f, -BarH / 2f, BarW, BarH, false));
            for (int i = 0; i < 6; i++)
            {
                tiles[i] = Image(null, 0, 0, TileW, TileH, false);
                targets[i] = Image(null, -1.5f * TileW + (i % 3) * TileW, BarH / 2f + (i / 3) * TileH, TileW, TileH, false);
            }
            // The two blocks' buttons (clickable) and the slashed marks on the target pattern.
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? -TileW / 2f : TileW / 2f;
                float y = -BarH / 2f - TileH;
                glows[side] = Image(Tex("hack_button_on"), x - GlowSize / 2f, y - GlowSize / 2f, GlowSize, GlowSize, false);
                buttons[side] = Image(Tex("hack_button"), x - ButtonSize / 2f, y - ButtonSize / 2f, ButtonSize, ButtonSize, false);
                unsolvedOnly.Add(Image(Tex("hack_blocked"), x - BlockedW / 2f, BarH / 2f + TileH - BlockedH / 2f, BlockedW, BlockedH, false));
            }
            // Clicks: anywhere on the puzzle's left / right half (the buttons sit on the boundary).
            var hit = new VisualElement();
            hit.AddToClassList("hacking-abs");
            Place(hit, -1.5f * TileW, -BarH / 2f - 2f * TileH, 3f * TileW, 2f * TileH);
            hit.RegisterCallback<PointerDownEvent>(e =>
            {
                if (game == null) return;
                if (e.localPosition.x < hit.layout.width / 2f) game.TurnLeft(); else game.TurnRight();
                e.StopPropagation();
            });
            layer.Add(hit);
            layer.style.display = DisplayStyle.None;
        }

        VisualElement Image(Texture2D tex, float x, float y, float w, float h, bool mirrored)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("hacking-abs");
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            if (mirrored) e.style.scale = new Scale(new Vector3(-1f, 1f, 1f));
            Place(e, x, y, w, h);
            layer.Add(e);
            return e;
        }

        /// <summary>Positions relative to the screen centre (the layer is centred, 0 x 0).</summary>
        static void Place(VisualElement e, float x, float y, float w, float h)
        {
            e.style.left = x;
            e.style.top = y;
            e.style.width = w;
            e.style.height = h;
        }

        public bool Visible => game != null;

        public void Update(ObjectDocking docking)
        {
            game = docking != null && docking.IsDocked ? docking.Hacking : null;
            if (game != null && game.Won) game = null;   // render2D draws nothing once the 1500 ms blink is over
            layer.style.display = game != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (game == null) return;
            ReadKeys();

            bool solved = game.Solved;
            foreach (var e in unsolvedOnly) e.style.display = solved ? DisplayStyle.None : DisplayStyle.Flex;
            bool gold = solved && ((int)(game.SolvedMs / 200f) & 1) == 0;
            float f = game.Turning != 0 ? game.TurnProgress : 0f;
            for (int i = 0; i < 6; i++)
            {
                // The shown (old) tiles slide clockwise within the turning block.
                float dx = 0f, dy = 0f;
                if (game.Turning < 0) { if (i == 0) dx = TileW * f; else if (i == 1) dy = TileH * f; else if (i == 4) dx = -TileW * f; else if (i == 3) dy = -TileH * f; }
                else if (game.Turning > 0) { if (i == 1) dx = TileW * f; else if (i == 2) dy = TileH * f; else if (i == 5) dx = -TileW * f; else if (i == 4) dy = -TileH * f; }
                int v = Mathf.Clamp(game.shown[i], 0, 5);
                tiles[i].style.backgroundImage = new StyleBackground(gold ? goldTex[v] : tileTex[v]);
                Place(tiles[i], -1.5f * TileW + (i % 3) * TileW + dx, -BarH / 2f + (i / 3 - 2) * TileH + dy, TileW, TileH);
                targets[i].style.backgroundImage = new StyleBackground(tileTex[Mathf.Clamp(game.target[i], 0, 5)]);
                targets[i].style.display = solved ? DisplayStyle.None : DisplayStyle.Flex;
            }
            glows[0].style.display = game.Turning < 0 ? DisplayStyle.Flex : DisplayStyle.None;
            glows[1].style.display = game.Turning > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            buttons[0].style.display = game.Turning < 0 ? DisplayStyle.None : DisplayStyle.Flex;
            buttons[1].style.display = game.Turning > 0 ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void ReadKeys()
        {
            var kb = GoF2Remake.Multiplayer.NetChat.Keys;   // null while a multiplayer chat line is typed
            var pad = Gamepad.current;
            bool left = kb != null && (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame)
                        || pad != null && (pad.leftShoulder.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame);
            bool right = kb != null && (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                         || pad != null && (pad.rightShoulder.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame);
            if (left) game.TurnLeft();
            else if (right) game.TurnRight();
        }
    }
}
