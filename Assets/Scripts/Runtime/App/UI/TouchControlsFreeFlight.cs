// TouchControlsFreeFlight.cs
// TouchControls: free flight's touch layout (remake; part of the partial TouchControls, its fields are in TouchControls.cs).
// The original has no touch layout for it (EVERSPACE 2 has none either: PC and consoles), so it is laid out after the
// controller's (left stick thrust / strafe, right stick aims, GameControls.FreeFlightDefaults) on the original's controls:
//   the stick            thrust (up / down) and strafe (left / right), linear; a double tap still levels out
//   the empty screen     aims: a floating stick, the point the finger went down its centre, the offset over AimRadius the
//                        deflection beyond a small dead zone (as EVERSPACE 2's mouse virtual joystick, which keeps turning
//                        while held off centre); a faint ring and dot show it; a quick sideways flick (FreeDodgeSeconds)
//                        still dodges; in free look the drag orbits as before
//   the fire button      aims as well while held, by the thumb's offset from where it went down (a larger dead zone, so
//                        plain firing doesn't turn the ship), so one thumb shoots and aims
//   beside the stick     a column: the dampeners toggle (lit while on), up, down (held)
//   the boost            held while held (free flight's boost; Settings.BoostToggle makes a tap start / stop it)
// No throttle drag (free flight has no throttle) and no roll buttons: the ship rolls itself level (ShipController.TouchFree
// turns EVERSPACE 2's auto-roll on, as ES2 does for a controller).

using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    public sealed partial class TouchControls
    {
        /// <summary>The aim's full deflection, panel units from where the finger went down.</summary>
        internal const float AimRadius = 140f;
        /// <summary>The aim's dead zones (panel units): the empty screen's, and the fire button's (larger: a press that only
        /// wants to shoot wobbles a little).</summary>
        internal const float AimDeadZone = 10f, FireAimDeadZone = 26f;
        /// <summary>A free-flight dodge: the sideways flick released within this (the original's 600 ms window took aiming
        /// drags for dodges).</summary>
        const float FreeDodgeSeconds = 0.25f;
        const float ColumnButton = 96f, ColumnGap = 14f, DampWidth = 150f, DampHeight = 60f;

        void BuildFreeFlight()
        {
            aimBase = new VisualElement { pickingMode = PickingMode.Ignore };
            aimBase.AddToClassList("touch-abs");
            aimBase.AddToClassList("touch-aim-ring");
            aimBase.style.width = AimRadius * 2f;
            aimBase.style.height = AimRadius * 2f;
            layer.Add(aimBase);
            aimKnob = new VisualElement { pickingMode = PickingMode.Ignore };
            aimKnob.AddToClassList("touch-abs");
            aimKnob.AddToClassList("touch-aim-dot");
            layer.Add(aimKnob);
            Show(aimBase, false);
            Show(aimKnob, false);

            dampButton = Button(FreeButton("touch-damp"), null, () => DampenersToggled?.Invoke());
            dampLabel = new Label { pickingMode = PickingMode.Ignore };
            dampLabel.AddToClassList("touch-damp-text");
            dampLabel.AddToClassList("gof-semibold");
            dampButton.Add(dampLabel);
            upButton = Button(FreeButton("touch-free-button"), null, null);
            upButton.Add(Chevron(true));
            downButton = Button(FreeButton("touch-free-button"), null, null);
            downButton.Add(Chevron(false));
        }

        VisualElement FreeButton(string cls)
        {
            var e = new VisualElement();
            e.AddToClassList("touch-abs");
            e.AddToClassList(cls);
            layer.Add(e);
            Show(e, false);
            return e;
        }

        static VisualElement Chevron(bool up)
        {
            var c = new VisualElement { pickingMode = PickingMode.Ignore };
            c.AddToClassList("touch-free-chevron");
            c.AddToClassList(up ? "touch-free-chevron--up" : "touch-free-chevron--down");
            return c;
        }

        /// <summary>A finger offset (panel units, y down) as a stick deflection (x right, y up): 0 inside the dead zone, then
        /// linear to 1 at AimRadius.</summary>
        internal static Vector2 FreeAim(Vector2 offset, float deadZone)
        {
            float m = offset.magnitude;
            if (m <= deadZone) return Vector2.zero;
            float k = Mathf.Min(1f, (m - deadZone) / (AimRadius - deadZone)) / m;
            return new Vector2(offset.x, -offset.y) * k;
        }

        void BeginAim(int pointerId, Vector2 p)
        {
            if (aimPointer >= 0) return;
            aimPointer = pointerId;
            aimStart = p;
            gestureAim = Vector2.zero;
        }

        void MoveAim(Vector2 p) => gestureAim = FreeAim(p - aimStart, AimDeadZone);

        void EndAim()
        {
            aimPointer = -1;
            gestureAim = Vector2.zero;
        }

        /// <summary>The column beside the stick (to the right of its touch area): the dampeners, up, down, centred on the stick.</summary>
        void LayoutFreeFlight()
        {
            float x = stickHome.x + StickHitX + 24f;
            float downTop = stickHome.y + ColumnGap / 2f;
            float upTop = stickHome.y - ColumnGap / 2f - ColumnButton;
            Place(upButton, new Vector2(x, upTop));
            Place(downButton, new Vector2(x, downTop));
            Place(dampButton, new Vector2(x + ColumnButton / 2f - DampWidth / 2f, upTop - ColumnGap - DampHeight));
        }

        void DrawFreeFlight(bool on, bool column)
        {
            bool aiming = on && aimPointer >= 0;
            Show(aimBase, aiming);
            Show(aimKnob, aiming);
            if (aiming)
            {
                Place(aimBase, aimStart - new Vector2(AimRadius, AimRadius));
                Place(aimKnob, aimStart + new Vector2(gestureAim.x, -gestureAim.y) * AimRadius - new Vector2(22f, 22f));
            }
            bool col = on && column;
            Show(upButton, col);
            Show(downButton, col);
            Show(dampButton, col);
            if (!col)
            {
                if (!on) { EndAim(); fireAim = Vector2.zero; }
                return;
            }
            upButton.EnableInClassList("touch-free-button--held", held.Contains(upButton));
            downButton.EnableInClassList("touch-free-button--held", held.Contains(downButton));
            dampButton.EnableInClassList("touch-damp--on", frame.dampeners);
            dampButton.EnableInClassList("touch-free-button--held", held.Contains(dampButton));
            string text = Localization.Extra("hudDampeners", "DAMPENERS");
            if (dampLabel.text != text) dampLabel.text = text;
            float alpha = frame.dimStick ? 50f / 255f : 1f;
            upButton.style.opacity = alpha;
            downButton.style.opacity = alpha;
        }
    }
}
