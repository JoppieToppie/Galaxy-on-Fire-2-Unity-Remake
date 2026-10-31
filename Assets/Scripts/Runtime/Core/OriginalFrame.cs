// OriginalFrame.cs
// The original game's frame, for the rules it ran once per frame without a dt (a death spin of 0.03 rad a frame, a fling's
// x0.9 a frame, a missile's 1/6 of the error a frame...). The iOS 1.1.19 build starts its CADisplayLink with frame interval 1
// (AppController::applicationDidFinishLaunching: / AppView::setRenderingFrequency:), 60 Hz on its devices, and
// MGame::OnUpdate takes the real elapsed milliseconds (capped at 150) per update; the Android build's cap was never
// determined. Until 2026-10-30 the remake converted those rules at 30 fps (33.3 ms), half the iPhone's rate.
namespace GoF2Remake.Flight
{
    public static class OriginalFrame
    {
        /// <summary>One frame of the original at its 60 Hz display link, in milliseconds.</summary>
        public const float Ms = 1000f / 60f;
    }
}
