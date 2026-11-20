// HdrCalibration.cs
// Remake (2026-11-20, the user's request; docs/notes/options.md "HDR"): the HDR calibration screen, from the Graphics
// tab's "HDR calibration…" row while HDR is on: a test pattern in place of the scene (HdrCalibration.shader, drawn by a
// render-graph pass before the post-processing) and two sliders over it, with the rest of the interface hidden:
//   - peak brightness: a block the display shows at its own brightest, with a diamond at the option's value; raised until
//     the diamond is gone, the option matches the display (it then sets the limits by hand: "use the display's limits" off),
//   - paper white: bands at the option's white and fractions of it; set so the top band is a comfortable white.
// While it is open Bootstrap's HDR volume tonemaps nothing and switches bloom and the brightness exposure off, so a value
// in the pattern leaves the display at the nits it stands for. The sliders write the options at once (not through the
// Graphics tab's Apply: the pattern has to follow). It reads the devices itself like SettingsConfirm (up / down pick,
// left / right change, Enter / Esc / A / B close) and the menus ignore their keys meanwhile (GameControls.ModalOpen).

using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class HdrCalibration
    {
        static readonly int NitsId = Shader.PropertyToID("_Nits");

        /// <summary>The screen is open: the pattern is drawn and the HDR volume is neutral (Bootstrap.ApplyHdr).</summary>
        public static bool Active { get; private set; }

        static VisualElement overlay;
        static Slider peak, paper;
        static int selected;
        static readonly List<(VisualElement element, StyleEnum<Visibility> was)> hidden = new List<(VisualElement, StyleEnum<Visibility>)>();
        static IVisualElementScheduledItem tick;
        static Material material;
        static PatternPass pass;
        static bool hooked;
        static float repeatAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; overlay = null; hidden.Clear(); tick = null; hooked = false; }

        static string X(string key, string english) => Localization.Extra(key, english);

        /// <summary>Opens the screen over the interface 'near' belongs to (the pressed row).</summary>
        public static void Show(VisualElement near)
        {
            if (Active || near?.panel == null || !Bootstrap.HdrActive) return;
            VisualElement host = null;
            for (var v = near; v != null; v = v.parent) if (v.ClassListContains("ui-root")) host = v;
            host ??= near.panel.visualTree;
            if (material == null)
            {
                var shader = Resources.Load<Shader>("GoF2PostFx/HdrCalibration");
                if (shader == null || !shader.isSupported) return;
                material = CoreUtils.CreateEngineMaterial(shader);
            }
            if (!hooked) { hooked = true; RenderPipelineManager.beginCameraRendering += OnBeginCamera; }

            // The rest of the interface out of the way of the pattern
            hidden.Clear();
            foreach (var child in host.Children()) { hidden.Add((child, child.style.visibility)); child.style.visibility = Visibility.Hidden; }

            overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0;
            var box = new VisualElement();
            box.AddToClassList("settings-confirm__box");
            box.style.position = Position.Absolute;
            box.style.left = Length.Percent(12f);
            box.style.right = Length.Percent(12f);
            box.style.bottom = Length.Percent(3f);
            overlay.Add(box);
            peak = Row(box, X("hdrPeak", "HDR peak brightness"), X("hdrCalibrationPeak", "Left: raise it until the diamond disappears into the block, and no further."),
                400f, 4000f, Settings.HdrPeak, v =>
                {
                    Settings.HdrPeak = Mathf.Round(v / 50f) * 50f;
                    Settings.HdrDetectLimits = false;   // the value set here is the one to use
                });
            paper = Row(box, X("hdrPaperWhite", "HDR paper white"), X("hdrCalibrationPaper", "Right: set it so the top band is a comfortable white for reading."),
                80f, 400f, Settings.HdrPaperWhite, v => Settings.HdrPaperWhite = Mathf.Round(v / 10f) * 10f);
            var done = new Button(Close) { text = X("hdrCalibrationDone", "Done").UiCaps(), focusable = false };
            done.AddToClassList("gof-button");
            done.AddToClassList("gof-button--primary");
            done.AddToClassList("gof-semibold");
            done.style.alignSelf = Align.Center;
            box.Add(done);
            host.Add(overlay);

            Active = true;
            selected = 0;
            Mark();
            GameControls.ModalOpen = true;
            Bootstrap.RefreshHdr();   // the volume goes neutral
            tick = overlay.schedule.Execute(Tick).Every(0);
        }

        static Slider Row(VisualElement box, string title, string help, float min, float max, float value, System.Action<float> set)
        {
            var label = new Label(title.UiCaps()) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("settings-confirm__title");
            label.AddToClassList("gof-semibold");
            box.Add(label);
            var text = new Label(help) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("settings-confirm__text");
            text.style.whiteSpace = WhiteSpace.Normal;
            box.Add(text);
            var slider = new Slider(min, max) { value = value, showInputField = false, focusable = false };
            slider.AddToClassList("option-row");
            slider.RegisterValueChangedCallback(e => { set(e.newValue); Refresh(); });
            box.Add(slider);
            return slider;
        }

        static void Refresh()
        {
            if (!Active) return;
            peak.label = Mathf.RoundToInt(Settings.HdrPeak) + " nits";
            paper.label = Mathf.RoundToInt(Settings.HdrPaperWhite) + " nits";
            material.SetVector(NitsId, new Vector4(Settings.HdrPaperWhite, Settings.HdrPeak, 0f, 0f));
        }

        static void Mark()
        {
            peak.EnableInClassList("options-selected", selected == 0);
            paper.EnableInClassList("options-selected", selected == 1);
            Refresh();
        }

        static void Tick()
        {
            if (!Active) return;
            if (overlay.panel == null || !Bootstrap.HdrActive) { Close(); return; }   // the screen went, or HDR did
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            bool close = false, up = false, down = false;
            float step = 0f;
            if (kb != null)
            {
                close |= kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                up |= kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame;
                down |= kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) step = -1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) step = 1f;
            }
            if (pad != null)
            {
                close |= pad.buttonEast.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame;
                up |= pad.dpad.up.wasPressedThisFrame;
                down |= pad.dpad.down.wasPressedThisFrame;
                float x = pad.dpad.ReadValue().x + pad.leftStick.ReadValue().x;
                if (Mathf.Abs(x) > 0.5f) step = Mathf.Sign(x);
            }
            if (close) { Close(); return; }
            if (up && selected != 0) { selected = 0; UiSounds.Move(); Mark(); }
            if (down && selected != 1) { selected = 1; UiSounds.Move(); Mark(); }
            if (step == 0f) { repeatAt = 0f; return; }
            if (Time.unscaledTime < repeatAt) return;
            repeatAt = Time.unscaledTime + (repeatAt == 0f ? 0.3f : 0.06f);   // one step, then repeating while held
            var slider = selected == 0 ? peak : paper;
            slider.value = Mathf.Clamp(slider.value + step * (selected == 0 ? 50f : 10f), slider.lowValue, slider.highValue);
        }

        static void Close()
        {
            if (!Active) return;
            Active = false;
            tick?.Pause();
            tick = null;
            overlay?.RemoveFromHierarchy();
            overlay = null;
            foreach (var (element, was) in hidden) element.style.visibility = was;
            hidden.Clear();
            GameControls.ModalOpen = false;
            // The Graphics rows may hold older staged values of the two options: what was set here stands.
            Settings.Unstage("hdrPeak", "hdrPaperWhite", "hdrDetectLimits");
            Bootstrap.RefreshHdr();
            OptionControl.RefreshShown();
            UiSounds.Confirm();
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!Active || material == null || cam.cameraType != CameraType.Game || cam.targetTexture != null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null || data.renderType != CameraRenderType.Base) return;
            pass ??= new PatternPass();
            data.scriptableRenderer.EnqueuePass(pass);
        }

        sealed class PatternPass : ScriptableRenderPass
        {
            public PatternPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
            }

            class Data { public Material material; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()) return;
                using var builder = renderGraph.AddRasterRenderPass<Data>("HDR calibration pattern", out var data);
                data.material = material;
                builder.SetRenderAttachment(resources.activeColorTexture, 0);
                builder.SetRenderFunc((Data d, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
            }
        }
    }
}
