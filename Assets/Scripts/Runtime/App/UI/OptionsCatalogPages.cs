// OptionsCatalogPages.cs
// OptionsCatalog: the Graphics, Controls and Gameplay pages' rows, which All() adds after the Sound page's
// (part of the partial OptionsCatalog; its tables and helpers are in OptionsCatalog.cs).

using System;
using System.Collections.Generic;
using System.Linq;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.UI
{
    public static partial class OptionsCatalog
    {
        /// <summary>All()'s Options > Graphics rows.</summary>
        static void AddGraphics(List<OptionDef> list)
        {
            // Remake: Low / Medium / High / Ultra set the preset options at once (Settings.ApplyPreset); the row shows the preset
            // the options match now, else Custom (picking Custom changes nothing).
            list.Add(Choice("graphicsPreset", OptionPage.Graphics, () => X("graphicsPreset", "Graphics preset"), true,
                () => new[] { Localization.Get(507), Localization.Get(508), Localization.Get(509), X("presetUltra", "Ultra"), X("presetCustom", "Custom") },
                CurrentPreset, i => Settings.ApplyPreset(i),
                () => X("graphicsPresetHelp", "Sets the render scale, effects, shadows and detail at once. Low suits phones and slower devices; High is the full look; Ultra puts every option at its most, for fast graphics cards.")));
            if (Bootstrap.HasDisplayOptions)
            {
                list.Add(Confirmed(Choice("displayMode", OptionPage.Graphics, () => X("displayMode", "Display mode"), true,
                    () => new[] { X("borderless", "Borderless"), X("fullscreen", "Fullscreen"), X("windowed", "Windowed") },
                    () => (int)Settings.DisplayMode, i => Settings.DisplayMode = (DisplayMode)i)));
                var sizes = Bootstrap.Resolutions();
                list.Add(Confirmed(Choice("resolution", OptionPage.Graphics, () => X("resolution", "Resolution"), false,
                    () => sizes.Select(r => $"{r.x} × {r.y}").Prepend(X("resolutionNative", "Display's own")).ToArray(),
                    () => { int i = sizes.IndexOf(Settings.Resolution); return i < 0 ? 0 : i + 1; },
                    i => Settings.Resolution = i <= 0 ? Vector2Int.zero : sizes[i - 1])));
            }
            list.Add(Choice("frameRate", OptionPage.Graphics, () => X("frameRate", "Frame rate"), true,
                () => new[] { "30", "60", "120", X("fpsUncapped", "UNCAPPED"), X("fpsVSync", "V-SYNC") },
                () => (int)Settings.FrameRate, i => Settings.FrameRate = (FrameRate)i));
            // The original's Quality (504) in its parts: fire and smoke, fog, detail distance (Settings.FireSmoke etc.).
            var fireSmoke = Toggle("fireSmoke", OptionPage.Graphics, () => X("fireSmoke", "Fire and smoke"), () => Settings.FireSmoke, v => Settings.FireSmoke = v);
            fireSmoke.description = () => X("fireSmokeHelp", "Damaged ships trail smoke and fire, and ships and wrecks burn as they are destroyed.");
            list.Add(fireSmoke);
            var fog = Toggle("fog", OptionPage.Graphics, () => X("fog", "Fog"), () => Settings.Fog, v => Settings.Fog = v);
            fog.description = () => X("fogHelp", "The haze of the star systems that have it.");
            list.Add(fog);
            list.Add(Choice("detailDistance", OptionPage.Graphics, () => X("detailDistance", "Detail distance"), true,
                () => new[] { Localization.Get(507), Localization.Get(508), Localization.Get(509), X("presetUltra", "Ultra") },
                () => Settings.DetailDistance, i => Settings.DetailDistance = i,
                () => X("detailDistanceHelp", "How far away ships, stations and asteroids keep their full detail before simpler models take over.")));
            // DLSS / FSR 2+ pick their own render resolution (the Upscaler quality below): no render scale while one is on.
            // MetalFX keeps the render scale (its input resolution; URP clamps it into MetalFX Temporal's supported range).
            bool DlssOrFsrOn() => Bootstrap.ActiveUpscaler == Settings.UpscalerDlss || Bootstrap.ActiveUpscaler == Settings.UpscalerFsrTemporal;
            var renderScale = Choice("renderScale", OptionPage.Graphics, () => X("renderScale", "Render scale"), false,
                () => RenderScales.Select(Percent).ToArray(),
                () => Nearest(RenderScales, Settings.RenderScale > 0f ? Settings.RenderScale : Bootstrap.DefaultRenderScale),
                i => Settings.RenderScale = RenderScales[i]);
            renderScale.visible = () => !DlssOrFsrOn();
            list.Add(renderScale);
            // Only the upscalers this device runs (Android: FSR 1 needs GLES 3.1 / Vulkan, STP Vulkan; DLSS / FSR 2+: Windows
            // builds with the upscaler framework, the GPU and graphics API they need; MetalFX: macOS / iOS on Metal, UpscalerFramework). Asked again on every
            // refresh: DLSS / FSR are only known once URP has made its pipeline (the first frame), and the main menu builds
            // its rows before that.
            List<int> Upscalers()
            {
                var l = new List<int> { Settings.UpscalerOff };
                if (Bootstrap.FsrSupported) l.Add(Settings.UpscalerFsr);
                if (Bootstrap.StpSupported) l.Add(Settings.UpscalerStp);
                if (Bootstrap.DlssSupported) l.Add(Settings.UpscalerDlss);
                if (Bootstrap.FsrTemporalSupported) l.Add(Settings.UpscalerFsrTemporal);
                if (Bootstrap.MetalFxSpatialSupported) l.Add(Settings.UpscalerMetalFxSpatial);
                if (Bootstrap.MetalFxTemporalSupported) l.Add(Settings.UpscalerMetalFxTemporal);
                return l;
            }
            var upscalers = Upscalers();
            string UpscalerName(int u) => u switch
            {
                Settings.UpscalerFsr => "FSR 1",
                Settings.UpscalerStp => "STP",
                Settings.UpscalerDlss => "DLSS",
                Settings.UpscalerFsrTemporal => UpscalerFramework.BestFsrLabel ?? "FSR",
                Settings.UpscalerMetalFxSpatial => "MetalFX",
                Settings.UpscalerMetalFxTemporal => "MetalFX Temporal",
                _ => X("off", "Off"),
            };
            if (upscalers.Count > 1 || UpscalerFramework.Compiled)
                list.Add(Choice("upscaler", OptionPage.Graphics, () => X("upscaler", "Upscaler"), true,
                    () => Upscalers().Select(UpscalerName).ToArray(),
                    () => Math.Max(0, Upscalers().IndexOf(Bootstrap.ActiveUpscaler)),
                    i => { var l = Upscalers(); Settings.Upscaler = l[Math.Clamp(i, 0, l.Count - 1)]; },
                    () => Bootstrap.ActiveUpscaler switch
                    {
                        Settings.UpscalerFsr => X("upscalerFsr", "AMD FSR 1: sharp upscaling from the render scale"),
                        Settings.UpscalerStp => X("upscalerStp", "Unity STP: temporal anti-aliasing and upscaling; replaces MSAA"),
                        Settings.UpscalerDlss => X("upscalerDlss", "NVIDIA DLSS: AI upscaling and anti-aliasing; replaces MSAA"),
                        Settings.UpscalerFsrTemporal => string.Format(X("upscalerFsrTemporal", "AMD {0}: temporal upscaling and anti-aliasing; replaces MSAA"),
                            UpscalerFramework.BestFsrLabel ?? "FSR"),
                        Settings.UpscalerMetalFxSpatial => X("upscalerMetalFxSpatial", "Apple MetalFX Spatial: sharp upscaling from the render scale"),
                        Settings.UpscalerMetalFxTemporal => X("upscalerMetalFxTemporal", "Apple MetalFX Temporal: temporal upscaling and anti-aliasing; replaces MSAA"),
                        _ => X("upscalerOff", "Plain scaling from the render scale"),
                    }));
            // DLSS / FSR 2+: the render resolution by quality mode, in place of the render scale; only while one of them is on (in
            // builds with the framework).
            if (UpscalerFramework.Compiled)
            {
                var quality = Choice("upscalerQuality", OptionPage.Graphics, () => X("upscalerQuality", "Upscaler quality"), false,
                    () => new[] { X("upscalerNative", "Native (DLAA / native AA)"), X("upscalerQ", "Quality"), X("upscalerB", "Balanced"),
                                  X("upscalerP", "Performance"), X("upscalerUP", "Ultra performance") },
                    () => Settings.UpscalerQuality, i => Settings.UpscalerQuality = i,
                    () => X("upscalerQualityHelp", "The resolution DLSS / FSR render at: from native down to a third."));
                quality.visible = DlssOrFsrOn;
                list.Add(quality);
            }
            // MSAA with a temporal upscaler on (STP, DLSS, FSR 2+, MetalFX Temporal): its anti-aliasing takes the place, so the row
            // is greyed out (a player's request: with DLAA it read as if it did something) and says why.
            var msaa = Choice("msaa", OptionPage.Graphics, () => X("antiAliasing", "Anti-aliasing"), true,
                () => new[] { X("off", "Off"), "MSAA 2×", "MSAA 4×", "MSAA 8×" },
                () => Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler) ? 0
                    : Math.Max(0, Array.IndexOf(MsaaSamples, Settings.Msaa > 0 ? Settings.Msaa : Bootstrap.DefaultMsaa)),
                i =>
                {
                    if (i > 0 && Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler)) Settings.Upscaler = Settings.UpscalerOff;
                    Settings.Msaa = MsaaSamples[i];
                },
                () => Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler)
                    ? X("msaaByUpscaler", "The upscaler does the anti-aliasing: turn it off to use MSAA")
                    : X("msaaHelp", "Smooths the jagged edges of models. More samples look smoother and cost more."));
            msaa.enabled = () => !Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler);
            list.Add(msaa);
            // Remake: the textures' mipmap levels skipped on loading (Settings.TextureQuality); less memory on phones.
            list.Add(Choice("textureQuality", OptionPage.Graphics, () => X("textureQuality", "Texture quality"), true,
                () => new[] { X("textureFull", "Full"), X("textureHalf", "Half"), X("textureQuarter", "Quarter") },
                () => Settings.TextureQuality, i => Settings.TextureQuality = i,
                () => X("textureQualityHelp", "Half and Quarter load smaller textures: less memory and faster on phones, a little blurrier up close.")));
            list.Add(Choice("textureFiltering", OptionPage.Graphics, () => X("textureFiltering", "Texture filtering"), true,
                () => new[] { X("filterBilinear", "Bilinear"), X("filterAnisotropic", "Anisotropic"), X("filterForced", "Forced"), X("filterForced16", "Forced 16×") },
                () => Settings.TextureFiltering, i => Settings.TextureFiltering = i,
                () => X("textureFilteringHelp", "Keeps surfaces seen at a slant sharp. Anisotropic: where a texture asks for it; Forced: every texture; 16×: the sharpest.")));
            list.Add(Choice("brightness", OptionPage.Graphics, () => Localization.Get(503), true,
                () => new[] { Localization.Get(513), Localization.Get(514), Localization.Get(515) },
                () => Settings.Brightness, i => Settings.Brightness = i));
            // Remake: the remake's bloom (the HDR glow of lights and effects), the original's (every bright pixel, ClassicBloomPass)
            // or both (#45).
            list.Add(Choice("bloom", OptionPage.Graphics, () => X("bloom", "Bloom"), true,
                () => new[] { X("off", "Off"), X("bloomRemake", "Remake"), X("bloomOriginal", "Original"), X("bloomBoth", "Both") },
                () => Mathf.Clamp(Settings.BloomStyle, 0, 3), i => Settings.BloomStyle = i));
            // Remake: the hangar ships' contact shadows (HangarShipShadow); fewer for slower devices.
            list.Add(Choice("hangarShadows", OptionPage.Graphics, () => X("hangarShadows", "Hangar ship shadows"), true,
                () => new[] { X("off", "Off"), X("hangarShadowsPlayer", "Player ship only"), X("hangarShadowsAll", "All ships") },
                () => Settings.HangarShadows, i => Settings.HangarShadows = i));
            // Remake: levels of detail (AutoLods); on by default.
            var lods = Toggle("lods", OptionPage.Graphics, () => X("lods", "Levels of detail"), () => Settings.Lods, v => Settings.Lods = v);
            lods.description = () => X("lodsHelp", "Faraway ships, stations and other objects are drawn with fewer triangles, for a smoother frame rate. Off: every model at full detail.");
            list.Add(lods);
            var hangarDof = Toggle("hangarDof", OptionPage.Graphics, () => X("hangarDof", "Hangar depth of field"),
                () => Settings.HangarDepthOfField, v => Settings.HangarDepthOfField = v);
            hangarDof.description = () => X("hangarDofHelp", "In the hangar your ship stays sharp and the room behind it blurs.");
            list.Add(hangarDof);
            list.Add(Toggle("lensFlare", OptionPage.Graphics, () => X("lensFlare", "Lens flare"), () => Settings.LensFlare, v => Settings.LensFlare = v));
            list.Add(Toggle("npcPlayerEngines", OptionPage.Graphics, () => X("npcPlayerEngines", "Other ships' engines like yours"),
                () => Settings.NpcPlayerEngines, v => Settings.NpcPlayerEngines = v));
            list.Add(Slider("fov", OptionPage.Graphics, () => X("fov", "Field of view"), 55f, 95f,
                () => Settings.FieldOfView, v => Settings.FieldOfView = Mathf.Round(v), v => $"{Mathf.RoundToInt(v)}°"));
            list.Add(Slider("cameraShake", OptionPage.Graphics, () => X("cameraShake", "Camera shake"), 0f, 1f,
                () => Settings.CameraShake, v => Settings.CameraShake = v, Percent));
            list.Add(Choice("chaseCamera", OptionPage.Graphics, () => X("chaseCamera", "Chase camera"), true,
                () => new[] { X("chaseCameraRemake", "Remake"), X("chaseCameraOriginal", "Original") },
                () => Settings.OriginalChaseCamera ? 1 : 0, i => Settings.OriginalChaseCamera = i == 1,
                () => X("chaseCameraHelp", "Remake: the camera stays close and keeps the ship centred. Original: it lags behind, the ship swings out in turns.")));
            // Remake: the interface's size, width and edge margin (UiLayout, docs/UI-DESIGN.md "Scaling").
            list.Add(Confirmed(Choice("uiScale", OptionPage.Accessibility, () => X("uiScale", "Interface size"), false,
                () => System.Array.ConvertAll(UiScaleSteps, p => p + " %"),
                () => Mathf.Max(0, System.Array.IndexOf(UiScaleSteps, Settings.UiScalePercent)), i => Settings.UiScalePercent = UiScaleSteps[Mathf.Clamp(i, 0, UiScaleSteps.Length - 1)],
                () => X("uiScaleHelp", "Makes text, buttons and the HUD bigger or smaller. 100 % already suits your screen."))));
            // Remake (docs/UI-DESIGN.md "Accessibility"): the text alone up to 200 % (XAG 101), high contrast, reduced motion,
            // colour-blind markers.
            list.Add(Confirmed(Choice("uiTextScale", OptionPage.Accessibility, () => X("uiTextScale", "Text size"), false,
                () => System.Array.ConvertAll(UiTextSteps, p => p + " %"),
                () => Mathf.Max(0, System.Array.IndexOf(UiTextSteps, Settings.UiTextScalePercent)), i => Settings.UiTextScalePercent = UiTextSteps[Mathf.Clamp(i, 0, UiTextSteps.Length - 1)],
                () => X("uiTextScaleHelp", "Makes only the text bigger; long texts wrap or scroll."))));
            // Remake (the user's request, 2026-10-24): the interface font (UiFont), Klavika or one the player imported or has
            // installed; the import's and removal's rows under it.
            var uiFont = Choice("uiFont", OptionPage.Accessibility, () => X("uiFont", "Interface font"), false,
                () => UiFont.Choices().ConvertAll(c => c.name ?? X("uiFontDefault", "Klavika (default)")).ToArray(),
                () => Mathf.Max(0, UiFont.Choices().FindIndex(c => c.key == Settings.UiFont)),
                i => { var all = UiFont.Choices(); Settings.UiFont = all[Mathf.Clamp(i, 0, all.Count - 1)].key; },
                () => UiFont.Current == null && UiFont.Problem != null ? UiFont.Problem
                    : X("uiFontHelp", "The font of every menu and the HUD: Klavika, a font file you import, or one installed on this device."));
            uiFont.defaultValue = () => 0f;
            list.Add(uiFont);
            list.Add(new OptionDef
            {
                id = "uiFontImport", page = OptionPage.Accessibility, kind = OptionKind.Button,
                label = () => X("uiFontImport", "Import font file…"),
                description = () => UiFont.Problem ?? X("uiFontImportHelp", "Pick a .ttf, .otf or .ttc file; it is copied into the game's Fonts folder and used at once."),
                visible = () => Modding.ModImport.Available,
                action = () => UiFont.Import(X("uiFontImport", "Import font file…"), _ => OptionControl.RefreshShown()),
            });
            list.Add(new OptionDef
            {
                id = "uiFontRemove", page = OptionPage.Accessibility, kind = OptionKind.Button,
                label = () => X("uiFontRemove", "Remove this font file"),
                description = () => X("uiFontRemoveHelp", "Deletes the imported font in use; Klavika comes back."),
                visible = () => (Settings.UiFont ?? "").StartsWith(UiFont.FilePrefix, System.StringComparison.Ordinal),
                action = UiFont.RemoveCurrent,
            });
            var highContrast = Toggle("highContrast", OptionPage.Accessibility, () => X("highContrast", "High contrast"),
                () => Settings.HighContrast, v => Settings.HighContrast = v);
            highContrast.description = () => X("highContrastHelp", "Solid panels and brighter secondary text and lines.");
            list.Add(highContrast);
            var reduceMotion = Toggle("reduceMotion", OptionPage.Accessibility, () => X("reduceMotion", "Reduce motion"),
                () => Settings.ReduceMotion, v => Settings.ReduceMotion = v);
            reduceMotion.description = () => X("reduceMotionHelp", "Menus and panels appear at once instead of sliding and fading in.");
            list.Add(reduceMotion);
            var colorblind = Toggle("colorblindMarkers", OptionPage.Accessibility, () => X("colorblindMarkers", "Colour-blind markers"),
                () => Settings.ColorblindMarkers, v => Settings.ColorblindMarkers = v);
            colorblind.description = () => X("colorblindMarkersHelp", "Friendly ships are blue instead of green.");
            list.Add(colorblind);
            list.Add(Choice("uiWidth", OptionPage.Graphics, () => X("uiWidth", "Interface width"), true,
                () => new[] { "16:9", "21:9", X("uiWidthFull", "Full width") },
                () => Settings.UiWidth, i => Settings.UiWidth = i,
                () => X("uiWidthHelp", "Keeps the HUD and menus in a 16:9 or 21:9 area on wider screens.")));
            list.Add(Choice("uiMargin", OptionPage.Graphics, () => X("uiMargin", "Screen edge margin"), false,
                () => System.Array.ConvertAll(UiMarginSteps, m => m < 0f ? X("uiMarginAuto", "Automatic") : m == 0f ? X("off", "Off") : m.ToString("0.#") + " %"),
                () => Mathf.Max(0, System.Array.IndexOf(UiMarginSteps, Settings.UiMarginPercent)), i => Settings.UiMarginPercent = UiMarginSteps[Mathf.Clamp(i, 0, UiMarginSteps.Length - 1)],
                () => X("uiMarginHelp", "Moves the interface in from the screen's edges, for TVs that cut them off.")));
            list.Add(Toggle("showFps", OptionPage.Graphics, () => X("showFps", "Show FPS"), () => Settings.ShowFps, v => Settings.ShowFps = v));
            // Remake (#137): the counter in any corner or the middle of any side, so it can stay clear of what a player watches.
            var fpsPosition = Choice("fpsPosition", OptionPage.Graphics, () => X("fpsPosition", "FPS counter position"), false,
                () => new[]
                {
                    X("fpsPosTopLeft", "Top left"), X("fpsPosTop", "Top"), X("fpsPosTopRight", "Top right"),
                    X("fpsPosLeft", "Left"), X("fpsPosRight", "Right"),
                    X("fpsPosBottomLeft", "Bottom left"), X("fpsPosBottom", "Bottom"), X("fpsPosBottomRight", "Bottom right"),
                },
                () => Settings.FpsPosition, i => Settings.FpsPosition = i,
                () => X("fpsPositionHelp", "Where the FPS counter sits."));
            fpsPosition.visible = () => Settings.ShowFps;
            list.Add(fpsPosition);
            // Modified: the interface's own mouse cursor (GameCursor); desktop only, phones have none.
            if (!Application.isMobilePlatform)
            {
                var gameCursor = Toggle("gameCursor", OptionPage.Graphics, () => X("gameCursor", "Game mouse cursor"), () => Settings.GameCursor, v => Settings.GameCursor = v);
                gameCursor.description = () => X("gameCursorHelp", "A mouse cursor in the interface's style; off, the system's.");
                list.Add(gameCursor);
            }
        }

        /// <summary>All()'s Options > Controls and the key bindings rows.</summary>
        static void AddControls(List<OptionDef> list)
        {
            // Remake: free flight after EVERSPACE 2 (Settings.FlightStyle, FlightModel.StepFree); it switches the key layout too.
            // A toggle: off = the original's flight and key layout, untouched.
            var freeFlight = Toggle("flightStyle", OptionPage.Controls, () => X("flightStyleFree", "Free flight (EVERSPACE 2 style)"),
                () => Settings.FlightStyle == FlightStyles.Free, v => Settings.FlightStyle = v ? FlightStyles.Free : FlightStyles.Original);
            freeFlight.description = () => (FlightStyles.Forced ? FlightStyleForcedNote() + " " : "") + X("flightStyleHelp", "Free flight: W / S thrust, A / D strafe, Space / Ctrl up and down, Q / E roll, Shift boost, Z dampeners. Own key layout.");
            list.Add(freeFlight);
            // MenuTouchWindow state 8: 490 Touch / 491 Accelerometer pictures (options[0x11]), 492 Steering Calibration
            // (493, then OK stores the device's position), the sensitivity slider per mode (+0x14 / +0x18).
            if (Flight.TiltSteering.Available)
            {
                list.Add(Choice("steering", OptionPage.Controls, () => X("steering", "Steering"), true,
                    () => new[] { Localization.Get(490), Localization.Get(491) },
                    () => Settings.TiltSteering ? 1 : 0, i =>
                    {
                        bool tilt = i == 1;
                        if (tilt && !Settings.TiltCalibrated) Flight.TiltSteering.Calibrate();   // remake: calibrate on first use
                        Settings.TiltSteering = tilt;
                    }));
                list.Add(new OptionDef
                {
                    id = "calibrate", page = OptionPage.Controls, kind = OptionKind.Button, label = () => Localization.Get(492),
                    description = () => Localization.Get(493), action = Flight.TiltSteering.Calibrate,
                });
                list.Add(Slider("tiltSensitivity", OptionPage.Controls, () => Localization.Get(499) + " (" + Localization.Get(491) + ")", 0f, 1f,
                    () => Settings.TiltSensitivity, v => Settings.TiltSensitivity = v, v => Mathf.RoundToInt(v * 100f).ToString()));
            }
            list.Add(Slider("sensitivity", OptionPage.Controls, () => Localization.Get(499), 0.2f, 2.2f,
                () => Settings.Sensitivity, v => Settings.Sensitivity = v, v => v.ToString("0.0")));
            // Remake: the touch fire button's double-press autofire for the keys, the mouse and the controller.
            list.Add(Toggle("keyAutofire", OptionPage.Controls, () => X("keyAutofire", "Double-press fire for auto-fire"),
                () => Settings.KeyAutofire, v => Settings.KeyAutofire = v));
            // Remake (#37): Level out levels the nose too (the original: the roll only).
            list.Add(Toggle("levelPitch", OptionPage.Controls, () => X("levelPitch", "Level out also levels the nose"),
                () => Settings.LevelPitch, v => Settings.LevelPitch = v));
            // options[0x10] "Invert controls" (500), split per axis; the mining drill has its own pair.
            list.Add(Toggle("invert", OptionPage.Controls, () => X("invertY", "Invert up / down"), () => Settings.InvertPitch, v => Settings.InvertPitch = v));
            list.Add(Toggle("invertYaw", OptionPage.Controls, () => X("invertX", "Invert left / right"), () => Settings.InvertYaw, v => Settings.InvertYaw = v));
            list.Add(Toggle("invertDrillY", OptionPage.Controls, () => X("invertDrillY", "Mining drill: invert up / down"), () => Settings.InvertDrillY, v => Settings.InvertDrillY = v));
            list.Add(Toggle("invertDrillX", OptionPage.Controls, () => X("invertDrillX", "Mining drill: invert left / right"), () => Settings.InvertDrillX, v => Settings.InvertDrillX = v));
            // Remake (#121): the cameras turned round the ship (free look, Action Freeze, Inspect ship) have their own pair.
            var orbitY = Toggle("invertOrbitY", OptionPage.Controls, () => X("invertOrbitY", "Camera views: invert up / down"),
                () => Settings.InvertOrbitY, v => Settings.InvertOrbitY = v);
            orbitY.description = () => X("invertOrbitHelp", "For cameras you turn around the ship: free look, Action Freeze, Inspect ship, the Mod Builder.");
            list.Add(orbitY);
            var orbitX = Toggle("invertOrbitX", OptionPage.Controls, () => X("invertOrbitX", "Camera views: invert left / right"),
                () => Settings.InvertOrbitX, v => Settings.InvertOrbitX = v);
            orbitX.description = orbitY.description;
            list.Add(orbitX);
            if (Flight.ControllerGyro.Supported)
            {
                list.Add(Toggle("gyroSteering", OptionPage.Controls, () => X("gyroSteering", "Controller gyro (DualSense, DualShock 4, Switch Pro)"),
                    () => Settings.GyroSteering, v => { Settings.GyroSteering = v; Flight.ControllerGyro.Recenter(); }));
                list.Add(Slider("gyroSensitivity", OptionPage.Controls, () => X("gyroSensitivity", "Gyro sensitivity"), 0.25f, 3f,
                    () => Settings.GyroSensitivity, v => Settings.GyroSensitivity = v, v => v.ToString("0.00")));
            }
            {
                // Phones and tablets show them while a mouse is connected (Android: mouse steering by pointer capture).
                Func<bool> hasMouse = () => !Application.isMobilePlatform || UnityEngine.InputSystem.Mouse.current != null;
                var mouseSteering = Toggle("mouseSteering", OptionPage.Controls, () => X("mouseSteering", "Mouse steering"), () => Settings.MouseSteering, v => Settings.MouseSteering = v);
                mouseSteering.visible = hasMouse;
                list.Add(mouseSteering);
                // Remake: a dead zone around the centre, so a mouse near the middle leaves the ship flying straight.
                var mouseDeadzone = Slider("mouseDeadzone", OptionPage.Controls, () => X("mouseDeadzone", "Mouse steering dead zone"), 0f, 0.3f,
                    () => Settings.MouseDeadzone, v => Settings.MouseDeadzone = v, Percent);
                mouseDeadzone.description = () => X("mouseDeadzoneHelp", "How far the mouse can move from the centre before the ship turns.");
                mouseDeadzone.visible = hasMouse;
                list.Add(mouseDeadzone);
                // Remake (Discord "Mouse Sensitivity seems to do nothing"): Sensitivity doesn't apply to the mouse (the PC
                // version's), and the raw counts of a high-DPI mouse were too fast.
                var mouseSensitivity = Slider("mouseSensitivity", OptionPage.Controls, () => X("mouseSensitivity", "Mouse sensitivity"),
                    Settings.MouseSensitivityMin, Settings.MouseSensitivityMax, () => Settings.MouseSensitivity, v => Settings.MouseSensitivity = v,
                    v => v.ToString("0.00"));
                mouseSensitivity.description = () => X("mouseSensitivityHelp", "How far the mouse moves the crosshair: steering, the mining drill and free look.");
                mouseSensitivity.visible = hasMouse;
                list.Add(mouseSensitivity);
            }
            // Remake (#61): the original's Configure screen (Options > Controls, 494 / 495) moved the touch controls' left
            // group and right cluster up and down; here as two sliders, shown when the touch controls are in use.
            foreach (bool right in new[] { false, true })
            {
                bool r = right;
                var touchHeight = Slider(r ? "touchRightHeight" : "touchLeftHeight", OptionPage.Controls,
                    () => r ? X("touchRightHeight", "Touch controls height: fire buttons") : X("touchLeftHeight", "Touch controls height: stick"), 0f, 1f,
                    () => { float v = r ? Settings.TouchRightHeight : Settings.TouchLeftHeight; return v < 0f ? TouchControls.DefaultHeight01(r) : v; },
                    v => { if (r) Settings.TouchRightHeight = v; else Settings.TouchLeftHeight = v; }, Percent);
                touchHeight.description = () => X("touchHeightHelp", "0 % is the highest place, 100 % the lowest.");
                touchHeight.visible = () => Application.isMobilePlatform || InputMode.Current == InputKind.Touch;
                list.Add(touchHeight);
            }
            // Remake: the touch stick moves under the thumb that touches its area and back when let go.
            var floatingStick = Toggle("floatingTouchStick", OptionPage.Controls, () => X("floatingTouchStick", "Touch stick follows your thumb"),
                () => Settings.FloatingTouchStick, v => Settings.FloatingTouchStick = v);
            floatingStick.description = () => X("floatingTouchStickHelp", "Touching near the stick moves it under your thumb, so the ship doesn't turn until you move it. It goes back to its place when you let go.");
            floatingStick.visible = () => Application.isMobilePlatform || InputMode.Current == InputKind.Touch;
            list.Add(floatingStick);
            // Remake VR: the cockpit's grabbable stick (right grip) and throttle lever (left grip), else the controllers as a gamepad.
            var vrGrab = Toggle("vrGrabControls", OptionPage.Controls, () => X("vrGrabControls", "VR flight: grab the stick and throttle"),
                () => Settings.VrGrabControls, v => Settings.VrGrabControls = v);
            vrGrab.visible = () => Vr.VrMode.Enabled;
            list.Add(vrGrab);
            // Remake: haptic feedback (Haptics), the controller's rumble and the phone's vibration; moving it plays a sample.
            var haptics = Slider("haptics", OptionPage.Controls, () => X("haptics", "Vibration"), 0f, 1f,
                () => Settings.HapticsIntensity, v => { Settings.HapticsIntensity = v; Flight.Haptics.Preview(); },
                v => v <= 0f ? X("off", "Off") : Percent(v));
            haptics.description = () => Application.isMobilePlatform
                ? X("hapticsHelpMobile", "Phone vibration and controller rumble for hits, explosions, boosts, jumps and mining.")
                : X("hapticsHelp", "Controller rumble for hits, explosions, shots, boosts, jumps and mining.");
            list.Add(haptics);
            list.Add(Slider("deadzone", OptionPage.Controls, () => X("deadzone", "Stick dead zone"), 0.05f, 0.4f,
                () => Settings.StickDeadzone, v => Settings.StickDeadzone = v, Percent));
            // Remake: the controller hints' button icons (InputMode.Style, PadGlyph): the controller in use, or always one style.
            list.Add(Choice("padGlyphs", OptionPage.Controls, () => X("padGlyphs", "Controller button icons"), true,
                () => new[] { X("padGlyphsAuto", "Auto"), "Xbox", "PlayStation" },
                () => Settings.PadGlyphs, i => Settings.PadGlyphs = i,
                () => X("padGlyphsHelp", "Auto: PlayStation buttons for a DualSense or DualShock, Xbox buttons for others.")));
            // Remake: every flight control rebindable (GameControls): two keyboard / mouse keys and a controller button each.
            list.Add(new OptionDef
            {
                id = "resetBindings", page = OptionPage.Bindings, kind = OptionKind.Button,
                label = () => X("resetBindings", "Reset key bindings"),
                // Which layout the list shows: each flight style has its own (GameControls.ApplyStyle).
                description = () =>
                {
                    Flight.GameControls.ApplyStyle();   // the layout of the style in use (a mod's or an event's too)
                    return string.Format(X("bindingsLayout", "Key layout: {0}. The Free flight toggle under {1} switches it."),
                                         FlightStyles.Current == FlightStyles.Free ? X("flightStyleFree", "Free flight (EVERSPACE 2 style)")
                                                                                    : X("flightStyleOriginal", "Original"),
                                         PageTitle(OptionPage.Controls))
                           + (FlightStyles.Forced ? " " + FlightStyleForcedNote() : "")
                           + " " + X("bindingsHelp", "Pick a key or button to change it. Esc cancels. Delete, × or a right click clears it.");
                },
                action = Flight.GameControls.ResetToDefaults,
                extra = BindingRow.Header,
            });
            foreach (var row in Flight.GameControls.Rows)
                list.Add(new OptionDef { id = "bind_" + row.id, page = OptionPage.Bindings, kind = OptionKind.Binding, label = row.label, control = row,
                                         visible = row.visible });   // rows only one flight style reads
        }

        /// <summary>All()'s Options > Gameplay rows.</summary>
        static void AddGameplay(List<OptionDef> list)
        {
            list.Add(Toggle("launchCamera", OptionPage.Gameplay, () => X("launchCamera", "Launch and arrival camera"),
                () => Settings.LaunchCamera, v => Settings.LaunchCamera = v));
            list.Add(Toggle("hangarFlights", OptionPage.Gameplay, () => X("hangarFlights", "Hangar arrival and take-off"),
                () => Settings.HangarFlights, v => Settings.HangarFlights = v));
            list.Add(Toggle("tutorialHints", OptionPage.Gameplay, () => X("tutorialHints", "Tutorials"),
                () => Settings.TutorialHints, v => Settings.TutorialHints = v));
            var storyStep = Toggle("showStoryStep", OptionPage.Gameplay, () => X("showStoryStep", "Show story step"),
                () => Settings.ShowStoryStep, v => Settings.ShowStoryStep = v);
            storyStep.description = () => X("showStoryStepHelp", "The current story step at the bottom right, for bug reports.");
            list.Add(storyStep);
            var dyingCargo = Toggle("dyingCargoMarkers", OptionPage.Gameplay, () => X("dyingCargoMarkers", "Salvage markers on dying ships"),
                () => Settings.DyingCargoMarkers, v => Settings.DyingCargoMarkers = v);
            dyingCargo.description = () => X("dyingCargoMarkersHelp", "A dying ship with cargo shows container markers before it explodes.");
            list.Add(dyingCargo);
            var cloakLoses = Toggle("cloakLosesPursuers", OptionPage.Gameplay, () => X("cloakLosesPursuers", "Cloak shakes off pursuers"),
                () => Settings.CloakLosesPursuers, v => Settings.CloakLosesPursuers = v);
            cloakLoses.description = () => X("cloakLosesPursuersHelp", "While you're cloaked, enemies fly to where they last saw you, then return to patrol.");
            list.Add(SessionRule(cloakLoses));
            var empCloak = Toggle("empBreaksCloak", OptionPage.Gameplay, () => X("empBreaksCloak", "EMP breaks cloaks"),
                () => Settings.EmpBreaksCloak, v => Settings.EmpBreaksCloak = v);
            empCloak.description = () => X("empBreaksCloakHelp", "An EMP hit drops a cloak at once, also one still charging.");
            list.Add(SessionRule(empCloak));
            var smartTargeting = Toggle("smarterNpcTargeting", OptionPage.Gameplay, () => X("smarterNpcTargeting", "Smarter NPC targeting"),
                () => Settings.SmarterNpcTargeting, v => Settings.SmarterNpcTargeting = v);
            smartTargeting.description = () => X("smarterNpcTargetingHelp", "Enemy fighters spread over nearby targets instead of all going for you.");
            list.Add(SessionRule(smartTargeting));
            // Remake (players' report: fighters hard to hit since the mesh collision): every hull's real shape, the flyable ships
            // on the original's hit cube, or the original's volumes everywhere (Settings.ShipCollision, Flight.HullCollision.Mode).
            list.Add(SessionRule(Choice("shipCollision", OptionPage.Gameplay, () => X("shipCollision", "Ship collision"), true,
                () => new[] { X("shipCollisionExact", "Exact shape"), X("shipCollisionMixed", "Mixed"), X("shipCollisionOriginal", "Original") },
                () => Settings.ShipCollision, i => Settings.ShipCollision = i,
                () => X("shipCollisionHelp", "Exact: real shapes everywhere. Mixed: fighters use boxes, big ships keep real shapes. Original: boxes and spheres."))));
            var pirateEvents = Toggle("pirateEvents", OptionPage.Gameplay, () => X("pirateEvents", "Pirate outposts and bosses"),
                () => Settings.PirateEvents, v => Settings.PirateEvents = v);
            pirateEvents.description = () => X("pirateEventsHelp", "Some orbits hold a pirate outpost or a pirate boss with a bounty.");
            list.Add(SessionRule(pirateEvents));
            var capitalShips = Toggle("capitalShips", OptionPage.Gameplay, () => X("capitalShips", "Capital ship enhancements"),
                () => Settings.CapitalShips, v => Settings.CapitalShips = v);
            capitalShips.description = () => X("capitalShipsHelp", "Capital ships get escorts and stronger turrets; carriers and Vossk battleships can be destroyed and docked at.");
            list.Add(SessionRule(capitalShips));
            var kaamoStacking = Toggle("kaamoStacking", OptionPage.Gameplay, () => X("kaamoStacking", "Stackable Kaamo Club upgrades"),
                () => Settings.KaamoStacking, v => Settings.KaamoStacking = v);
            kaamoStacking.description = () => string.Format(X("kaamoStackingHelp", "Kaamo Club upgrades can be fitted again, each level twice the price, up to level {0}."),
                Settings.KaamoMaxLevel);
            list.Add(SessionRule(kaamoStacking));
            // Remake: the stacking's top level (Session.MaxModLevel), a session rule too (-kaamomaxlevel, /set kaamomaxlevel).
            var kaamoLevel = Choice("kaamoMaxLevel", OptionPage.Gameplay, () => X("kaamoMaxLevel", "Kaamo Club upgrade limit"), false,
                () => Enumerable.Range(SessionRules.MinKaamoLevel, SessionRules.MaxKaamoLevel - SessionRules.MinKaamoLevel + 1).Select(n => n.ToString()).ToArray(),
                () => Settings.KaamoMaxLevel - SessionRules.MinKaamoLevel, i => Settings.KaamoMaxLevel = SessionRules.MinKaamoLevel + i,
                () => X("kaamoMaxLevelHelp", "How many times each upgrade can be fitted to one ship."));
            kaamoLevel.visible = () => Settings.KaamoStacking;
            list.Add(SessionRule(kaamoLevel));
            var kaamoGear = Toggle("kaamoKeepsEquipment", OptionPage.Gameplay, () => X("kaamoKeepsEquipment", "Stored ships keep their equipment"),
                () => Settings.KaamoKeepsEquipment, v => Settings.KaamoKeepsEquipment = v);
            kaamoGear.description = () => X("kaamoKeepsEquipmentHelp", "A ship parked in the Kaamo Club keeps its weapons and equipment.");
            list.Add(SessionRule(kaamoGear));
            var cloakBay = Toggle("cloakBay", OptionPage.Gameplay, () => X("cloakBay", "Cloak bay on cloaking ships"),
                () => Settings.CloakBay, v => Settings.CloakBay = v);
            cloakBay.description = () => X("cloakBayHelp", "On the Specter and Scimitar a mounted cloak takes no equipment slot.");
            list.Add(SessionRule(cloakBay));
            // #28: the Informer mission's rule for other ships dying after the spy.
            list.Add(SessionRule(Choice("informerRule", OptionPage.Gameplay, () => X("informerRule", "Informer missions"), true,
                () => new[] { X("informerRemake", "Remake"), X("informerOriginal", "Original") },
                () => Settings.InformerOriginalRule ? 1 : 0, i => Settings.InformerOriginalRule = i == 1,
                () => X("informerRuleHelp", "Remake: once the spy is dead, other kills don't count. Original: any kill before docking fails it."))));
            list.Add(Choice("targetLock", OptionPage.Gameplay, () => X("targetLock", "Target lock"), true,
                () => new[] { X("targetLockSmart", "Smart"), X("targetLockOriginal", "Original") },
                () => Settings.OriginalTargetLock ? 1 : 0, i => Settings.OriginalTargetLock = i == 1,
                () => X("targetLockPriorityHelp", "Smart: hostile ships first, then the one nearest the crosshair. Original: the first ship in the box.") + " "
                      + X("targetLockBeams", "Beam lasers then aim only at hostiles or your lock.") + " "
                      + X("targetLockAsteroids", "Asteroids: the one under the crosshair first.") + " "
                      + X("targetLockUnlock", "Unlock target (U) drops the lock; on touch, tap the lock plate.")));
            // Remake (players' suggestion): the Smart lock's hold on an enemy, its own option (CombatRadar, TargetLockRules).
            var keepEnemy = Toggle("keepEnemyLock", OptionPage.Gameplay, () => X("keepEnemyLock", "Keep enemy lock"),
                () => Settings.KeepEnemyLock, v => Settings.KeepEnemyLock = v);
            keepEnemy.description = () => X("keepEnemyLockHelp", "While an enemy is locked, passing neutral or friendly ships can't take the lock.");
            keepEnemy.visible = () => !Settings.OriginalTargetLock;
            list.Add(keepEnemy);
            // Remake mods: NPC fighters in the mods' ships of their race (Modding.ModTraffic).
            list.Add(Choice("modShipTraffic", OptionPage.Gameplay, () => X("modShipTraffic", "Mod ships in traffic"), true,
                () => new[] { X("modShipTrafficOff", "Off"), "10 %", "25 %", "50 %" },
                () => Settings.ModShipTraffic, i => Settings.ModShipTraffic = i,
                () => X("modShipTrafficHelp", "How often NPC fighters fly your mods' ships; their hulls and guns stay the game's.")));
            list.Add(Toggle("autoAdvance", OptionPage.Gameplay, () => X("autoPlayDialogue", "Auto-play dialogue"),
                () => Settings.AutoAdvanceDialogue, v => Settings.AutoAdvanceDialogue = v));
            list.Add(Toggle("animatedDialogue", OptionPage.Gameplay, () => X("animatedDialogue", "Animated dialogue"),
                () => Settings.AnimatedDialogue, v => Settings.AnimatedDialogue = v));
            // The key and controller button hints everywhere: the flight HUD, the menus' prompt bars, the station, the star map,
            // Inspect ship and Action Freeze (it was "Button hints in flight", though the menus' bars read it too).
            var inputHints = Toggle("inputHints", OptionPage.Gameplay, () => X("inputHints", "Button hints"),
                () => Settings.InputHints, v => Settings.InputHints = v);
            inputHints.description = () => X("inputHintsHelp", "Shows which keys and controller buttons do what, in flight, in the menus and in the station.");
            list.Add(inputHints);
            // Remake (immersion): the flight HUD's markings Full / Minimal / Off (FlightHud); also the "Toggle HUD" key (F1).
            list.Add(Choice("hudMode", OptionPage.Gameplay, () => X("hudMode", "Flight HUD"), true,
                () => new[] { X("hudFull", "Full"), X("hudMinimal", "Minimal"), X("hudOff", "Off") },
                () => Settings.HudMode, i => Settings.HudMode = i,
                () => X("hudModeHelp", "Minimal: crosshair, prompts and messages only. Off: messages only. F1 switches it in flight.")));
            // Remake (docs/UI-DESIGN.md "HUD"): the flight HUD's own size and opacity, apart from the menus' Interface size.
            list.Add(Choice("hudScale", OptionPage.Gameplay, () => X("hudScale", "HUD size"), false,
                () => System.Array.ConvertAll(HudScaleSteps, p => p + " %"),
                () => Mathf.Max(0, System.Array.IndexOf(HudScaleSteps, Settings.HudScalePercent)), i => Settings.HudScalePercent = HudScaleSteps[Mathf.Clamp(i, 0, HudScaleSteps.Length - 1)],
                () => X("hudScaleHelp", "Sizes the flight HUD without changing the menus.")));
            list.Add(Choice("hudOpacity", OptionPage.Gameplay, () => X("hudOpacity", "HUD opacity"), false,
                () => System.Array.ConvertAll(HudOpacitySteps, p => p + " %"),
                () => Mathf.Max(0, System.Array.IndexOf(HudOpacitySteps, Settings.HudOpacityPercent)), i => Settings.HudOpacityPercent = HudOpacitySteps[Mathf.Clamp(i, 0, HudOpacitySteps.Length - 1)],
                () => X("hudOpacityHelp", "How solid the HUD markings are; messages and menus stay opaque.")));
            // Remake (players' request): a far ship's marker that leaves the ship in view, and its distance (CombatView,
            // FarShipMarkers).
            list.Add(Choice("farShipMarkers", OptionPage.Gameplay, () => X("farShipMarkers", "Far ship markers"), true,
                () => new[] { X("farMarkerOriginal", "Original"), X("farMarkerRing", "Ring"), X("farMarkerSmallDot", "Small dot") },
                () => Settings.FarShipMarkers, i => Settings.FarShipMarkers = i,
                () => X("farShipMarkersHelp", "How a distant ship is marked: a dot over it, a hollow ring, or a small dot above it.")));
            var markerDistance = Toggle("markerDistance", OptionPage.Gameplay, () => X("markerDistance", "Distance on ship markers"),
                () => Settings.MarkerDistance, v => Settings.MarkerDistance = v);
            markerDistance.description = () => X("markerDistanceHelp", "Shows the distance under every distant ship's marker.");
            list.Add(markerDistance);
            // Remake: Discord Rich Presence (DiscordPresence), desktop only.
            if (!Application.isMobilePlatform)
                list.Add(Toggle("discordPresence", OptionPage.Gameplay, () => X("discordPresence", "Show what I'm doing in Discord"),
                    () => Settings.DiscordPresence, v => Settings.DiscordPresence = v));
            // Remake: the difficulty of the game in progress (the new game's panel sets it first; saved with the game). NPC
            // hulls and guns, raiders and the Extreme rules follow from the next orbit or docking. Not in multiplayer: every
            // session plays on Normal.
            if (!Multiplayer.NetGame.Active)
            {
                float[] levels = { Session.DifficultyEasy, Session.DifficultyNormal, Session.DifficultyHard, Session.DifficultyExtreme };
                var difficulty = Choice("difficulty", OptionPage.Gameplay, () => X("difficulty", "Difficulty"), true,
                    () => levels.Select(Session.DifficultyName).ToArray(),
                    () => Nearest(levels, Session.Difficulty), i => Session.Difficulty = levels[Mathf.Clamp(i, 0, levels.Length - 1)],
                    () => X("difficultyHelp", "Takes effect from the next orbit or docking."));
                difficulty.inGameOnly = true;
                list.Add(difficulty);
            }
            // Modified: the text language in a running game's menus too (the main menu has its own language buttons, which
            // this row stands in for; the same list, LanguageTables.Offered). Loading the table raises Localization.Changed,
            // which the station menu and the flight HUD re-label themselves on, so it applies without leaving the game.
            var language = Choice("language", OptionPage.Language, () => Localization.Get(0), false,
                () => LanguageTables.Offered.Select(l => l.name).ToArray(),
                () => Array.IndexOf(LanguageTables.Offered.Select(l => l.code).ToArray(), Settings.Language) is var li && li >= 0 ? li : 0,
                i => SetLanguage(LanguageTables.Offered[Mathf.Clamp(i, 0, LanguageTables.Offered.Length - 1)].code));
            language.inGameOnly = true;
            list.Add(language);
            // Remake mods: the gameplay settings of the mods that are on (gameoptions.json "setting": true), only while they are.
            foreach (var m in Modding.ModGameOptions.GameplaySettings())
            {
                var def = m;
                var row = Toggle("mod:" + def.Key, OptionPage.Gameplay, () => def.Name, () => Modding.ModGameOptions.Setting(def), v => Modding.ModGameOptions.SetSetting(def, v));
                if (def.Description.Length > 0) row.description = () => def.Description;
                list.Add(row);
            }
            // Remake: the testing tools (Cheats.Unlocked), also opened by F10, LB + RB or three fingers on the main menu.
            var debug = Toggle("debugTools", OptionPage.Gameplay, () => X("debugTools", "Debug tools"), () => Cheats.Unlocked, v => Cheats.Unlocked = v);
            debug.description = () => X("debugToolsHelp", "A Debug button in the main menu and a Debug page in the pause and station menus.");
            debug.visible = () => Cheats.Allowed;   // not in a multiplayer session that doesn't allow the Debug menu
            list.Add(debug);
            // Remake (a player's request): the saves also kept in a folder the player picks, for file managers and sync tools
            // (Data.SaveFolder); main menu only: no game is loaded there. The main menu runs the choice (SaveFolderPressed).
            if (SaveFolder.Supported)
                list.Add(new OptionDef
                {
                    id = "saveFolder", page = OptionPage.Gameplay, kind = OptionKind.Button, menuOnly = true,
                    label = () => string.Format(X("saveFolder", "Save folder: {0}"),
                        SaveFolder.Chosen ? SaveFolder.DisplayName : X("saveFolderDefault", "this device only")),
                    description = () => SaveFolder.LastProblem != null
                        ? string.Format(X("saveFolderUnreachable", "Can't reach the folder ({0}). Saves stay here and sync when it's back."), SaveFolder.LastProblem)
                        : X("saveFolderHelp", "Also keeps your saves in a folder you choose, for sync tools and other devices."),
                    action = () => SaveFolderPressed?.Invoke(),
                });
        }
    }
}
