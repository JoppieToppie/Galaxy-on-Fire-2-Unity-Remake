# Platforms and builds

Notes moved from `CLAUDE.md` (its index lists every notes file). Code comments citing `CLAUDE.md "<section>"` mean the section or bullet of that name here.

## UI and platforms

- Main menu research (flow, text IDs, image rects, camera, sounds): `Reference/research/mainmenu_notes.md`.
- **Discord Rich Presence** (remake, desktop; Windows so far): `DiscordPresence` + `DiscordIpc` (the desktop app's local
  pipe, no SDK) for the Discord application "Galaxy on Fire 2 Unity Remake" (1555054317155647571); details / state from
  the scene (docked, flying, mining, combat, cutscene; the freelance mission or the story step's title, multiplayer
  squad), the campaign art large and the system's race emblem small (assets from
  `Reference/tools/discord/make_assets.py`), the timer from the game's start; in the Mod Builder the project instead of
  the throwaway game it runs in ("Making a mod - <kind>" / "Test flight" / "Play-testing a mod", the project's name,
  large text "Mod Builder"); Options > Gameplay "Show what I'm doing in Discord".
- **App icon** (`MainMenuBuilder.BuildAppIcons`, run by Main Menu Scene; the art `UI/AppIcon/icon_source.png`, 2000 x 2000
  with rounded corners): `icon.png` the default icon (Windows / Linux / iOS, Android legacy and round), Android's adaptive
  icon = `icon_foreground.png` (the art at 62 % inside the 66.7 % visible area, so a circle mask keeps the title) over
  `icon_background.png` (the art's middle blurred and enlarged) + `icon_monochrome.png` (its bright parts in white, the
  themed icon); UWP's tiles, store logo and splash at 100 / 200 % in `UI/AppIcon/UWP` (`PlayerSettings.WSA` visual assets:
  squares = the art, wide / splash = the art on its blur; an image over 200 KB is left out at 200 %, the UWP packager refuses
  bigger logos (APPX3207), so the 310 x 310 and wide tiles and the splash come at 100 % only). Without the art the old icon
  (the GoF2 logo on skybox_003).
- **Android name**: package `com.joppietoppie.gof2remake`, launcher label "GoF2 Remake" (`AndroidAppLabel` rewrites the
  Gradle project's app_name); the product name stays "Galaxy on Fire 2" so the desktop save folder and PlayerPrefs don't
  move.
  **Beta builds** (fork, `AndroidBetaPackage`): with GoF2 > Build > Android Beta Package ticked (per machine) or
  `GOF2_ANDROID_BETA=1`, an Android build gets the package `com.joppietoppie.gof2remake.beta` and the label "GoF2 Beta", so it
  installs beside the published game (the same package signed with another key has to replace it); put back after the build
  like `BuildVersionStamp`. The beta has its own data folder (saves, mods, Transfer). The Java class
  `com.joppietoppie.gof2remake.ModImportActivity` keeps its name: a class's Java package is not the app's package.
- **iOS** (remake, 2026-10-17; checked in the iPhone 18 Pro simulator: main menu, flight HUD, cutscene pause button,
  station, hangar, lounge, star map, Missions, Status, Inspect ship, Action Freeze, pause and autopilot menus; not yet
  run on an iPhone): **Safe area**: `UI.SafeArea` (App/UI) turns `Screen.safeArea`
  into a panel's insets, clamped to the screen (a safe area under half the screen ignored); iPhones in landscape report the
  Dynamic Island / notch inset on both the left and the right and the home indicator's at the bottom. The main menu
  (margins: its children are absolutely placed, which a parent's padding never moves, so its old padding inset nothing),
  the flight HUD and the station (their `safeArea` element's position) and the star map (`mapSafe`, which it
  never inset before) use `SafeArea.Apply`; full-screen overlays keep their edge-anchored parts clear with
  `SafeArea.KeepClear(overlay)`, margins on every descendant with the class `safe-edge` (`SafeArea.EdgeClass`): Action
  Freeze's and Inspect ship's title, hints, footer, photo settings panel and saved notice, the Customize panel, the Mod
  Builder's top bar, side panels and status line (the overlay itself stays full screen: the photo frame bars must match the
  screenshot); the story-step label moves in by the right inset only (the bottom one lifted it onto the station's
  ticker). A new screen with controls at the edges uses
  these. **Player settings and Info.plist** (`IosPlayerSetup`, Editor, on Editor load and before an iOS build, each only
  while at Unity's default): bundle id `com.joppietoppie.gof2remake`, the home indicator auto-hidden, system gestures
  deferred on the top and bottom edges; after the build Info.plist gets `CFBundleDisplayName` "GoF2 Remake" (never through
  `PlayerSettings.iOS.applicationDisplayName`: that is the product name itself, which names the desktop save folder and
  PlayerPrefs), `UIFileSharingEnabled` + `LSSupportsOpeningDocumentsInPlace` (Documents = persistentDataPath in the Files
  app: Saves, Mods, Transfer, Screenshots), `CADisableMinimumFrameDurationOnPhone` (ProMotion above 60 fps),
  `NSLocalNetworkUsageDescription` (iOS 14+ blocks LAN games without it), `NSPhotoLibraryAddUsageDescription`, Game Mode
  (`LSApplicationCategoryType` action games, `GCSupportsGameMode`) and controller support (`GCSupportsControllerUserInteraction`,
  `GCSupportedGameControllers` ExtendedGamepad). **Native plugins** (`Assets/Plugins/iOS`, each .mm's meta naming its
  framework): `PhoneHaptics.mm` (Core Haptics behind `PhoneVibrator`: every pulse at its intensity, none on iPads),
  `PhotoLibrary.mm` (`PhotoMode.StoreAt`: Documents/Screenshots and the photo library, add-only), `FileImportPicker.mm`
  (`ModImport.Pick`), `SaveFolderPicker.mm` (the save folder). Local network adapters by iOS name (`NetGame.AdapterName`:
  en0 Wi-Fi, bridge Hotspot, utun VPN; mobile data and AirDrop left out). Switching the active target to iOS adds a default
  iOS entry to every texture .meta (like UWP's): harmless, revert them before committing.
  **Foldables, the iPhone Duo** (2026-11-14, the user's request; Apple's spec page: an A20 Pro, a 7.6" 1878 x 2670 inner
  and a 5.4" 1398 x 2034 outer OLED, both ProMotion up to 120 Hz with a Dynamic Island; iOS 27; its RAM, ProMotion's
  minimum and the app's life over a fold aren't published): both screens are about 1.42-1.46:1 in landscape, so the UI
  takes `layout-narrow` (not `-square`) and the cameras' Hor+ keeps the 16:9 width; folding or unfolding changes the
  screen size and safe area while running, which every screen already follows (`lastScreen` / `lastSafeArea` /
  `UiLayout.Revision` re-layout, `SafeArea` on GeometryChanged). Changes: a mobile screen under `UiLayout.PhoneMaxInches`
  (8.1", was 7.5") stays a phone, so the inner screen's 7.6" doesn't switch the UI to the desktop's size on unfolding
  (tablets, the iPad mini's 8.3" up, stay desktop); iOS defaults to 60 fps and the Medium preset, its upscaler MetalFX
  Spatial (options.md "Graphics preset"). Not done: a thermal-state response (ProcessInfo.thermalState needs a native plugin), anything Duo-specific by
  model (its identifier isn't known). Not run on the device or a simulator of it.
- **Android Vulkan pre-transform** (Player settings > Android > "Apply display rotation during rendering",
  `PlayerSettings.vulkanEnablePreTransform`): on, with `PreTransformGuard` (Space, installed by `Bootstrap`): while it is in
  use (`PreTransformGuard.Active`: Vulkan, `SystemInfo.supportsSwapChainPreTransform`, not
  `QualitySettings.disableSwapChainPreTransform`) every base game camera drawing to the screen queues an empty render pass
  with `requiresIntermediateTexture`, so URP never draws the scene straight to the pre-rotated back buffer; only its final
  blit and the UI do. Straight to it (render scale 100 % without MSAA, FSR at 100 %+, the only cases where URP skips the
  intermediate texture) part of the screen froze (a player's report, #45): the remake's screen-space shaders (the hangar
  shadows reading the depth texture, the cloak / shield bubble refraction, the classic bloom, the bar flybys' pass) work in
  unrotated coordinates, where URP's own (`DeclareDepthTexture`, `Shadows`) correct for the rotation. It was off from
  2026-10-14 to 2026-10-21 for that: Android's compositor then rotates every frame, a full-screen GPU pass on phones whose
  display hardware can't (a Dimensity 8350: a solid 60 fps in 2026.10.09's Terran hangar, 40-60 in 2026.10.18's, at 50 %
  render scale). The guard costs a full-resolution blit at 100 % (below it, or with MSAA, URP blits anyway).
  `Bootstrap.ApplyUpscaling` still keeps FSR 1 below 100 % (there is nothing to upscale at 100 %). Not yet run on a phone.
  The freeze at 100 % was reported again with 2026.10.23 (the guard in), and doesn't happen with MSAA on (the user's test on
  a phone). The guard can't have changed anything: with post-processing on URP renders through its intermediate texture
  anyway (`UniversalRenderer.RequiresIntermediateColorTexture`); the cause is still unknown. A lead: with MSAA off the
  station camera runs URP's TAA (`StationLevel.ApplyAntialiasing`), with MSAA SMAA. So on Android 100 % with MSAA off gets
  MSAA 2x (`Bootstrap.AppliedMsaa`; the option still shows Off), and under a temporal upscaler (MSAA off) 94 %
  (`AppliedRenderScale` / `AndroidFullScale`: URP renders a scale within 5 % of 1 as exactly 1, `kRenderScaleThreshold`).
  The MSAA 2x didn't help every phone (#163: a Mali-G57 still half black, then fully black after a restart), while 90 %,
  125 % and 200 % work everywhere, so since 2026-11-03 Android's 100 % is always drawn at 94 % (`AppliedRenderScale`, with
  or without a temporal upscaler) and `AppliedMsaa` leaves MSAA as picked. Not yet checked on a phone.
- **Android Vulkan device filter** (`Assets/Settings/VulkanDeviceFilters.asset`, Player settings > Android > Vulkan Device
  Filtering Asset, `PlayerSettings.Android.androidVulkanDeviceFilterListAsset`; 2026-11-01): one deny entry, device name
  `Adreno[^0-9]*[56][0-9][0-9]` and Android version `^([7-9]|10)([.][0-9.]*)?$`, so Adreno 5xx / 6xx phones on Android 10
  or older start on GLES3 (the global API list is Vulkan, GLES3; the "Android Vulkan" profile has no GLES3 and ignores
  it). Why: a OnePlus 7T (Adreno 640, Android 10, Discord 2026-10-24) drew random rectangles of the screen black or stale
  with MSAA on, more the higher the MSAA: GPU bins never stored, an old Adreno Vulkan driver's fault (Unity's own Adreno
  "flickers or goes black with MSAA" reports); URP's render graph always merges passes into Vulkan subpasses
  (`nativeRenderPassesEnabled` is obsolete and always on), so there is no switch to work around it in Vulkan. Every
  filter field but the Vulkan API / driver versions is an ECMAScript regex, not a "this version or older": hence the
  anchored Android pattern (a bare `10` matched Android 10 alone, a partial `[5-9]` would catch 15). The fallback costs
  those phones STP (no GLES; FSR 1 only where GLES reports shader model 4.5, see "Options"). `SettingsTests.OldAdrenoPhonesStartOnOpenGlEs`. Not yet run on
  the phone: if GLES breaks the same way, the driver is not the cause.
- **UWP** (Universal Windows Platform, no build profile: `EditorUserBuildSettings.SwitchActiveBuildTarget(WSA, WSAPlayer)`
  first, then `BuildPipeline.BuildPlayer` to a folder; IL2CPP, x64, D3D): package `JoppieToppie.GoF2Remake`, Start menu
  name "GoF2 Remake", capabilities InternetClient / InternetClientServer / PrivateNetworkClientServer (multiplayer and
  hosting), signed with a test certificate `Assets/WSATestCertificate.pfx` (publisher CN=JoppieToppie, made by the internal
  `EditorUtility.WSACreateTestCertificate(path, "JoppieToppie", "", overwrite)` through reflection: a publisher given as
  "CN=..." became CN="CN=..."; git-ignored, a new clone makes its own). Unity writes a Visual Studio solution; the
  package comes from MSBuild (`MSBuild "Galaxy on Fire 2.sln" -p:Configuration=Master -p:Platform=x64 -p:AppxBundle=Never
  -p:UapAppxPackageBuildMode=SideloadOnly -p:AppxPackageDir=...`; needs Visual Studio's "C++ Universal Windows Platform
  tools", `Microsoft.VisualStudio.ComponentGroup.UWP.VC`, which Burst needs too): `<name>_x64_Master_Test` with the
  .msix, the .cer and Install.ps1 (it trusts the certificate, admin prompt, then installs); a release zips that folder
  without the .appxsym symbols and the TelemetryDependencies folder (the script then sends no sideload telemetry). Not
  in UWP: Discord, the controller gyro, the save file dialog (Transfer folder instead), the dedicated server
  (everything desktop-only is behind `UNITY_STANDALONE_WIN`), the window mode / resolution options
  (`Bootstrap.HasDisplayOptions` false under `UNITY_WSA`: `Screen.mainWindowDisplayInfo` throws NotSupportedException
  there, which stopped `Bootstrap.Init` and the main menu's options setup, a black screen after the splash). The back
  buffer took the window's view-pixel size (1920 x 1080 on a 4K TV, scaled up): `UwpDisplay` sets it to the window's
  raw pixels (`DisplayInformation.RawPixelsPerViewPixel`), on an Xbox to the HDMI mode (`HdmiDisplayInformation`; only
  the One X / Series X above 1080p, `AnalyticsInfo.DeviceForm`), again on resize / DPI change, and logs "UwpDisplay: ..."
  (not yet verified on a 4K display or an Xbox). Testing
  a UWP build here: with Developer Mode on, unpack the .msix (its entry names are URL-encoded) without
  AppxSignature.p7x / AppxBlockMap.xml / [Content_Types].xml / AppxMetadata, `Add-AppxPackage -Register` its
  AppxManifest.xml, start it from `shell:AppsFolder\<family>!App`; the log is
  `%LOCALAPPDATA%\Packages\JoppieToppie.GoF2Remake_*\TempState\UnityPlayer.log`. `BuildVersionStamp` also stamps the package version
  (yyyy.M.d.HHmm). Switching to UWP adds default WindowsStoreApps entries to every texture .meta: revert them.
- **UWP sky** (#20): the space-sky strips (`Resources/GoF2Sky`) have a WindowsStoreApps override to uncompressed RGBA32 with 1365 px faces
  (`SkyboxBaker.ApplyUwpOverride`; the asteroid belt sky layer's `skybox_asteroid_belt_diffuse` too, RGBA32 at 2048, set
  by `AssetImport` since 2026-10-11: compressed it showed as a checkerboard of squares on the Xbox; the Xbox runs UWP
  apps at D3D feature level 10, which has no BC7: the default CompressedHQ
  drew bands of shifted tiles, BC1 bands and a criss-cross; if RGBA32 still breaks it is the cubemap layout, and the next step
  is the shader's 2D strip path; unverified on the device). Menus: a text field is one
  focus stop (`MainMenu.InsideTextField`; its inner input was listed too and handed the focus back, trapping the D-pad in the
  multiplayer panel's name field), panels open on their first non-text item, and on UWP the text typed while the system keyboard
  was open is kept when it closes with B (`KeepKeyboardText`; closing counts as Cancel and put the old text back).
- **Version** (`BuildVersion`): the menu's credit line and the About page show "Galaxy on Fire 2 Unity Remake created with
  <heart sprite> by JoppieToppie · <version>" (`BuildVersion.Text`; also `/version` in the chat, the multiplayer window's
  foot and the dedicated server's first log line; no git branch / commit any more: joining compares the fingerprint);
  the version is the date and time of the git commit the build comes from (`yyyy.MM.dd.HHmm`, UTC; fork change, upstream
  uses the build's own time, still the fallback without git), so Windows, Linux and Android builds of one commit match; stamped into
  `PlayerSettings.bundleVersion` for each build by `BuildVersionStamp` (Editor) and put back afterwards, so
  `Application.version` and Android's versionName carry it; "editor" in the Editor.
- **Releases** (the files every GitHub release carries, from 2026.11.15 on; the user's rule: the update packs in every
  release): tag `yyyy.MM.dd` = the HEAD commit's date (`BuildVersion`; commit first, `gh release create --target <that
  commit>`). Build Windows, Linux, Android (`Android™` profile, this machine's debug key), UWP (MSBuild), the Linux and
  Windows dedicated servers (`ServerBuild`), then GoF2 > Build > Update Patch (`PatchPublisherWindow.StartAll(tag)`).
  Upload: `GoF2Remake-<Windows|Linux|UWP>-<tag>.zip`, `GoF2Remake-Android-<tag>.apk`,
  `GoF2Remake-<Linux|Windows>Server-<tag>.zip`, and the patcher's `GoF2Remake-<Windows|Linux|Android>-<tag>.pack` +
  `.patch.json`, `GoF2Remake-UWP-<tag>.msix`, `GoF2Remake-UWP.appinstaller`. A release without a platform's .patch.json
  makes that platform's "Update available" open the release page, and the next release's packs then have to carry
  everything it would have published (each pack holds only the chunks no earlier release's manifest names).
- **Update check** (remake, `UpdateCheck`): entering the main menu (after the title screen) asks GitHub once per run for the
  newest release (`api.github.com/repos/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest`, no token; drafts and
  pre-releases ignored). Release tags are `yyyy.MM.dd`; the build's `.HHmm` is ignored, so only a release from a later day
  than the build counts. Then "Update available · <tag>" shows at the bottom centre (`.update-row`, only while no panel is
  open; Down from the last menu row reaches it) and opens the release page (`Application.OpenURL`). A failed request is tried
  again on the next menu entry. The Editor ("editor") checks only with `MainMenu.editorTestVersion` set (e.g. 2026.09.29.2200).
- **Update patcher** (remake, 2026-11-05, players asked not to download 1.5 GB per update; `GamePatcher`, `MainMenuUpdate`,
  `AndroidUpdateInstaller` + Plugins/Android `UpdateInstaller` / `UpdateApkProvider`, `UwpAppInstaller`, Core `PatchChunker` /
  `PatchManifest` / `PatchInstall` / `PatchPublish`, `GamePatchTests`): the "Update available" button updates in place on
  every platform where the release carries the way to (else, and for a read-only game folder, it opens the release page):
  Windows / Linux / macOS `GoF2Remake-<platform>-<tag>.patch.json` (changed chunks, files swapped after quitting), Android
  `GoF2Remake-Android-<tag>.patch.json` (changed chunks, the APK rebuilt and handed to Android's installer), UWP
  `GoF2Remake-UWP.appinstaller` (Windows' App Installer). Everything is a GitHub release file: no other server (2026-11-05,
  first planned on the user's NAS). Why chunks: Unity packs all of `Resources/` into one
  `resources.assets.resS` (2.7 GB, 1.1 GB zipped), which any asset change rewrites with shifted offsets; a code-only
  release still changed 122 of 1,467 MB zipped (10.23 -> 10.24), an asset release 1,425 MB.
  - Chunks: content-defined (FastCDC gear hash; the gear table is fixed, never change it or every published chunk is cut
    differently from the game's), id = first 16 bytes of SHA-256 as hex. An insertion only renews the chunks around it.
    Size per file (`PatchChunker.BitsFor`, `PatchFile.bits`): 512 KB normal (128 KB - 2 MB), 16 KB (4 - 64 KB) for Unity's
    serialized files (`*.assets`, globalgamemanagers, level*: they hold .resS offsets, so changes are scattered) and the APK
    (13,000 separately packed zip entries).
  - Packs: each publish writes the chunks no earlier release had into one file per platform, uploaded with the release as
    `GoF2Remake-<platform>-<tag>.pack` (raw deflate, or stored when that's no smaller); the manifest names each chunk's pack
    as "<tag>/<name>" under `packBase` (`PatchManifest.ReleaseDownloads`, github.com/<repo>/releases/download/) and its
    offset; a chunk is published once for all platforms and releases. The game plans range requests
    (`PatchInstall.PlanRanges`: neighbours up to 256 KB apart joined, at most 16 MB a request, four at a time): one release
    behind it is mostly one sequential read of the newest pack; further behind it reads the pieces it needs from older
    releases' packs. GitHub's download link answers 302 to its file host, which answers `Range` with 206 (checked with
    UnityWebRequest on the real 11.02 zip, 2026-11-05: the header survives the redirect, 16 MB in 0.5 s). A 200 for a part is
    refused. Python's http.server ignores Range: test locally with a range-aware one. Release files are limited to 2 GB
    each (a first, full pack is ~1.5 GB).
  - Trust: the manifest (file list, chunk ids / sizes / packs / offsets, `packBase`, `removed`) comes from the GitHub release;
    every downloaded chunk is checked against the hash the manifest names. Paths and
    pack names are checked (relative, no `..`, nothing a shell would read: they go into the apply script).
  - In game (Windows / Linux / Android): button -> manifest -> `PatchInstall.Scan` (thread) cuts the files of the new build
    the game has -> "downloads N instead of the full <zip / APK size>" (Update / Release page / Cancel) -> range downloads,
    each chunk checked, kept in `_update/chunks` (an interrupted try keeps them) -> `Assemble` (thread) writes each changed
    file whole into `_update/files` from local + downloaded chunks, checking every chunk again. Disk space checked first
    (changed files + download + 64 MB). The dialog (the menu's own) shows a progress bar (`dialogProgress`, hidden by
    `CloseDialog`) while checking, downloading and preparing, and while downloading "<got> of <total>" with the speed over
    the last 4 s (real time) and the time left as a clock (`patchSpeed`, `GamePatcher.BytesPerSecond` / `SecondsLeft`).
  - Apply, Windows / Linux: running files are locked, so `<game>/_update/apply.cmd` / `apply.sh` waits for the game's PID to
    end, moves each file in (30 tries a second apart), deletes the manifest's `removed` files, removes `_update` (kept when
    a move failed) and with "restart" starts the player again (Linux through `setsid` when present, `chmod +x` on `exec`
    files). Later = the script runs from `Application.quitting`.
  - macOS (2026-11-15, the user's request; untested on a Mac, no macOS release yet): the same as Windows / Linux with the
    app bundle as the game's folder (`Application.dataPath` is its Contents; the manifest's paths start at Contents/,
    `PatchPublish.InspectMacApp`; executable: anything in a Contents/MacOS/ and the .dylib files). The work folder is
    `persistentDataPath/_update`, never in the bundle (its signature covers everything in it), and the bundle's Contents
    must be writable, else the release page opens (App Translocation runs an app started straight from Downloads from a
    read-only copy). `PatchInstall.MacScript` names the bundle by its absolute path (`ShellQuote`), moves the files in,
    signs the bundle again ad hoc (`codesign --force --deep --sign -`: the new files break its signature and macOS won't
    open a "damaged" app), clears the quarantine (`xattr -dr com.apple.quarantine`) and with "restart" runs `open` on
    it. A notarized Developer ID build would carry only that ad-hoc signature after its first update. iOS can't update
    itself (a signed, read-only bundle): there the button opens the release page. The macOS player scripts don't
    compile from this Windows Editor (2026-11-15: URP core's MetalFX upscalers miss `MetalFXDevice`), so a macOS build
    and its first in-place update are still to be tried on a Mac.
  - Apply, Android: the manifest's one file is the APK (`PatchManifest.ApkName`); the chunks it shares are read from the
    installed APK (`Application.dataPath`), the work folder is `persistentDataPath/_update`, and the rebuilt APK is byte for
    byte the published one, so its signature holds. Install: Android 8+ first needs the app's "Install unknown apps" switch
    (`patchAllowInstalls` opens it), then `UpdateInstaller.install` opens the APK in the package installer through
    `UpdateApkProvider` (content://<package>.gof2update/<name>, read only, only files in `_update/files`) and Android asks
    the player. Manifest: REQUEST_INSTALL_PACKAGES and the provider, added by the Editor's `AndroidUpdateInstallerManifest`.
    The same signing key as the installed APK is needed (as for any APK update): the releases so far are signed with
    the build machine's Android Debug key (cert SHA-256 6b1560d3..., 2026-11-05), so a lost debug.keystore or a build from
    another machine makes Android refuse the update, patched or downloaded.
  - Multiplayer: the fingerprint (`BuildFingerprint`) hashes the runtime sources, not the compiled code, so the platform-only
    updater code (`#if UNITY_ANDROID` / `ENABLE_WINMD_SUPPORT`) doesn't split platforms; a patched install is byte-identical
    to the release, fingerprint included.
  - Apply, UWP: a package can't change its files, and its .msix can't be rebuilt from the installed files (they're stored
    unpacked, the package compressed and signed). Windows' App Installer updates it from an .appinstaller that names the
    package's web address and downloads only the blocks that changed (64 KB blocks per file: good for code releases,
    poorer when the .resS shifts). `GamePatcher.OpenAppInstaller` downloads the release's .appinstaller to
    temporaryCachePath and opens it (`Launcher.LaunchFileAsync` on the UI thread; if Windows refuses that file type, the
    browser gets its URL). The .appinstaller's `OnLaunch` (24 h) also lets Windows check by itself when the game was
    installed from it. Untested on a real UWP install (2026-11-05): only compiled for WSA.
  - The next start deletes an `_update` whose state.json names the running version (`PatchInstall.CleanUpAppliedIn`). Builds
    from before the patcher (2026.11.02 and older) can't patch: one last full download.
  - Release side: GoF2 > Build > Update Patch (`PatchPublisherWindow`, editor-tools.md) after the builds writes the
    release files into Build/Release beside the zips: per chunked platform (Build/Release/<Windows|Linux>/GoF2Remake,
    Build/Release/macOS/GoF2Remake.app, the newest .apk in Build/Release or Build/Release/Android) `GoF2Remake-<platform>-<tag>.pack` + `.patch.json`, for UWP (the
    newest .msix under Build/Release/UWP) `GoF2Remake-UWP-<tag>.msix` + `GoF2Remake-UWP.appinstaller` (its own Uri is
    releases/latest/download/GoF2Remake-UWP.appinstaller, so Windows' OnLaunch check finds the newest; the package Uri is
    this release's). Upload them all with `gh release create`. What earlier releases published comes from their .patch.json
    files, fetched from GitHub into `Library/GoF2PatchHistory` before each publish (`FetchHistory`, `fetched.txt` remembers
    which); a rebuild under the same tag writes that platform's pack anew and drops the superseded manifest from the history.
    The tag defaults to the build version's date (`PatchPublish.TagOf`); set it in the window when the release day differs.
    Versions come from globalgamemanagers (the APK's `assets/bin/Data/globalgamemanagers`) and the msix's AppxManifest.xml.
  - Editor tests: `GamePatcher.TestInstallDir` = a build folder treats it as the game's (the Editor's platform),
    `TestApkPath` = an APK as the installed one (Android; TestInstallDir is then the app's data), `TestManifestUrl` = the
    manifest instead of the release's; pump Prepare / Download from `EditorApplication.update` (no Play mode). Keep the
    Editor in front: in the background its update loop barely runs and a download crawled at 0.15 MB/s. Player scripts for
    Android / WSA / Linux compile without switching with `PlayerBuildInterface.CompilePlayerScripts` (WSA returns an empty
    list but writes the assemblies); the Java plugin with the Editor's OpenJDK javac against the SDK's android.jar.
  - Measured on the real releases (2026-11-05), 10.24 -> 11.02 (skies, reflections and hangars rebuilt): Windows 376 MB
    instead of the 1,492 MB zip (resources.assets.resS ~305 MB of genuinely new textures, GameAssembly.dll ~32 MB at any
    chunk size: IL2CPP moves its code everywhere; resources.assets 19 MB in 16 KB chunks); Android 237 MB instead of the
    1,170 MB APK (manifest 6.6 MB). Packs: 3,448 chunks in 24 requests. End to end through GamePatcher against a local
    range server: Windows staged 26 files, the apply script (started as the game starts it) swapped them in under a second,
    and the 10.24 folder hashed identical to 11.02 (104 files); a cancelled download resumed with its chunks kept.
  - Checked before the release after 2026.11.15, the first one players update to in place (2026-11-20, the user's
    request; Windows): the patcher's code is the same in the 2026.11.15 tag and now (`git diff 2026.11.15..HEAD` over
    Core's Patch*, GamePatcher, MainMenuUpdate, UpdateCheck: nothing), so the Editor run is what that build runs. The
    release zip unpacked, the new build published with `PatchPublish.Run` into a test folder with a copy of the
    history (the real `Library/GoF2PatchHistory` not written), served by a range-aware local server (the manifest's
    packBase pointed at it): 47 of 123 files change, 6,020 chunks in 67 range requests, all from the new pack, 1,058 MB
    instead of the 1,571 MB zip (765 MB of resources.assets.resS, which Unity a16 -> a20 rewrote, 105 MB
    nvngx_dlssnr.dll, 57 MB libxess.dll, 35 MB GameAssembly.dll), prepared in 8 s, staged in 57 s; apply.cmd swapped
    the files in and the folder hashed identical to the new build (123 files, none left over), which starts. Not
    covered: the 2026.11.15 player's own button against GitHub (the release's files have to be there for it), Linux,
    Android, UWP. A shell standing in `_update` keeps the script from removing that (then empty) folder.
- **Screen recorder** (remake, 2026-11-28, the user's requests: "build in a screen recorder with a simple clips manager",
  "should also work on phone", "settings like resolution, framerate", "rec should not be shown in the recording, also
  you should be able to choose include the ui or not", "when hdr is on, it should also record in hdr", "linux should
  also work"; `ScreenRecorder`, `RecorderEncoder`, `RecorderScenePass`, `RecorderAudioTap`, `FfmpegEncoder`,
  `ClipLibrary`, `ClipsView`; tests `ClipLibraryTests`, `RecorderYuvTests`, `ScreenRecorderTests`): F9
  (`GameControls.RecordVideo`, rebindable, any scene, not while typing), the pause menu's Recording page (Start / Stop
  recording, Movie mode, Recorded clips; Start: the menu closes and the game is recorded) or Movie mode's "Record
  takes" (each take a clip named after the movie, no
  mark) record the game with its sound to an MP4. Windows (player and Editor), Android and Linux; elsewhere
  `Supported` is false and none of it shows. **One pipeline**: at the end of each frame due (Settings.RecordFrameRates
  24 / 30 / 60 on a fixed clock of real time; a late frame skips its slot) the picture is the back buffer
  (`ScreenCapture.CaptureScreenshotIntoRenderTexture`: HUD and menus in it) or, with "Record the interface" off, the
  cameras' colour after the post-processing and before the screen-space UI (`RecorderScenePass`, enqueued on every game
  camera at `AfterRenderingPostProcessing`, a render-graph blit into the recorder's texture; a camera drawing straight
  to the back buffer has nothing to copy and the screen is taken instead: letterbox, fades, titles and subtitles are
  interface too), packed on the GPU into planar 4:2:0 at the clip's size (`Resources/GoF2Recorder/RecorderYuv.shader`
  pass 0: one target w x 1.5 h, the Y plane then U then V, limited range, written by the target's uv so the image's top
  row comes out first on every API; `_Flip` for the back buffer's copy on a `graphicsUVStartsAtTop` API, `_Linear` for
  the cameras' linear colour) and read back without a stall (`AsyncGPUReadback`, three in flight at most). Its pass 1
  makes the thumbnail. `Graphics.Blit` didn't bind `_MainTex` for this URP shader (every frame the default grey, the
  first clip pink), so it is set on the material, the pass named (pass -1 runs both); the blits put
  `RenderTexture.active` back (left on the recorder's target, the screen flickered while recording). **The mark** (a
  pulsing red dot under the cargo readout; Settings.RecordIndicator off / the first 3 s / always, always by default) is
  drawn onto the back buffer after the frame was taken (`Graphics.DrawTexture` at the end of the frame), so it shows on
  screen and never in the clip; checked: on the Game view's capture, not in the clip's frame of the same moment.
  **HDR** (`Bootstrap.HdrActive`): HDR10, the capture in half floats; the screen's copy holds PQ codes (the HDR10 swap
  chain), the cameras' colour is linear (BT.2020 nits after URP's HDR tonemapping, BT.709 relative to paper white
  without it: `_SceneNits`) and is PQ-coded in the shader; packed as BT.2020 10-bit with the code in the 16-bit
  sample's high bits (`_Hdr` 1, an R16 target); peak (the display's `maxToneMapLuminance`, else the option's) and
  paper white as MaxCLL / MaxFALL. Where the encoder can't, SDR from the HDR picture (`_Hdr` 2: PQ decoded, BT.709
  relative to paper white, a shoulder over 80 %, sRGB). **Encoders**: Windows `gof2_recorder.dll` (`Native/Recorder`,
  CMake + MSVC like XeSS; Media Foundation's sink writer: H.264 + AAC, or HEVC Main 10 from P010 (U and V interleaved
  there) with BT.2020 / PQ / the light levels; a hardware encoder when there is one; one worker thread owns every MF
  object, a 512 MB queue drops frames, never sound. The Editor never unloads it: to put a rebuild in place, rename the
  loaded DLL aside (Windows allows that) and copy the new one in; the Editor uses it from its next start), Android
  `ClipEncoder.java` (MediaCodec H.264 from `getInputImage`'s planes by their strides, or HEVC Main 10 from P010 where
  an encoder takes `COLOR_FormatYUVP010`, Android 13+; AAC; MediaMuxer started once both tracks' formats are known;
  the frame as a direct ByteBuffer over the readback, JNI ids looked up on the main thread, the audio thread attached
  once), Linux the system's ffmpeg (`FfmpegEncoder`: on the PATH or `$GOF2_FFMPEG`; raw frames into its standard input
  at a constant rate, a writer thread filling a skipped slot with the frame before; libx264 / libopenh264 / mpeg4, HDR10
  by libx265 with its HDR10 parameters; the sound into a raw float file and muxed in (AAC) by a second ffmpeg at the
  stop; Windows takes it too with `$GOF2_RECORDER=ffmpeg`, chosen per recording, which is how it is tested here). No
  screen-capture consent, no microphone permission: the game hands over what it draws and its own mix. **Sound**:
  `RecorderAudioTap` on the scene's AudioListener (put back on whichever listener a new scene has) gives the final mix
  on the audio thread; its clock is the samples written, a gap of over 0.2 s by `AudioSettings.dspTime` (the listener
  paused, a scene loading) filled with silence so it stays with the picture; 44.1 / 48 kHz only (else the picture
  alone); a mix at another rate (a phone's own: the project leaves it to the device, the S24 mixes at 24 kHz, so its
  first clip had no sound) is resampled to 48 kHz (`ScreenRecorder.Resample`, linear, seamless across blocks).
  **Settings** (Options > Graphics "Recording", the last section, and the clips manager's top; in
  `Settings.GraphicsKeys`): resolution (Screen, 2160p..480p, never above the screen, the screen's shape, multiples of
  8: `ClipLibrary.Size`), frame rate, quality (Low..Very high: 0.05..0.22 bit per pixel per frame, 1.5..120 Mbit/s,
  `BitrateKbps`), game sound, the interface, the mark, "Recorded clips". Phones start at 720p 30. **Clips**: desktop the
  Videos library's "Galaxy on Fire 2", Android and tests `<data>/Clips`; a thumbnail (320 wide, half a second in, SDR
  even from HDR) and the length in `<data>/ClipThumbs`. **The clips manager** (`ClipsView`, on the recorder's own panel
  over every scene, `GameControls.ModalOpen` while open; the pause menu's Recording page, the Options row, Movie mode):
  newest first with thumbnail, length, size, date; Play (Windows `explorer.exe`, Android the system player, Linux
  the desktop's), Show in folder / Open folder (desktop), Share and Save to gallery (Android: `ClipProvider`,
  `content://<package>.gof2clips/<name>`, registered by `AndroidUpdateInstallerManifest`; the gallery copy needs Android
  10), Rename (F2), Delete (a second press within 4 s; for good). The clip being recorded isn't listed. Recording stops
  with the game and when a phone puts it in the background; stopping waits for the encoder to finish (a moment).
  **Verified** on Windows (Editor): the encoder alone (synthetic clips: orientation, colours, sound, BT.709 tags; HDR10:
  HEVC Main 10, yuv420p10le, bt2020nc / smpte2084 and the light-level side data, by ffprobe), flights recorded at 60 fps
  with sound in step, with and without the interface, the mark on screen and not in the clip, the ffmpeg path (a play
  mode test), the shader's packing (SDR, HDR10, SDR from HDR) on the GPU. On the S24 Ultra (a beta build, F9 by adb on the main
  menu, HDR on): HEVC Main 10 HDR10 (yuv420p10le, PQ) through the P010 path, AAC 48 kHz stereo from the 24 kHz mix,
  picture and sound the same length, the thumbnail. **Not verified**: the HDR picture from a desktop HDR screen (the
  Editor has none here), Linux on Linux (its ffmpeg path ran on Windows), the clips manager's Share / Save to gallery. Not done: iOS
  (AVAssetWriter, needs a Mac), a replay buffer, the microphone.
- **Hitch logger** (`HitchLogger`, development builds only; in the Editor with PlayerPrefs `debug_hitchlog` = 1): frames
  over 33 ms on the real clock to `persistentDataPath/hitches.log` (scene, seconds since its load, campaign step /
  station, tags load / GC / focus, a summary per scene) and `GraphicsSettings.logWhenShaderIsCompiled` on, so logcat
  shows every "Uploaded shader variant to the GPU driver" with its time. Measured on the S24 Ultra (Vulkan, Adreno 750,
  Vulkan pipeline cache deleted, 2026-09-28): 28 variants / ~360 ms at startup before the menu shows, then only at scene
  loads (GoF2/Unlit 49 ms at the first station, SpaceDust 26 ms), nothing during play (the menu backdrop's live orbit
  already uses the flight shaders), no frame over 50 ms: no shader warm-up (`GraphicsStateCollection`) needed so far.
- **Issue reports** (remake, 2026-11-13, the user's request: an in-game report "with screenshot / logs / mods" like Unity's
  own; `IssueReport`, `IssueReportUpload`, `PauseMenuReport`, GoF2.App): the pause menu's last entry "Report an issue"
  hides the menu for a frame, takes the screen (`ScreenshotKey.Grab`) and shows a form: the picture, a text box (an entry
  of the menu: Enter / A / a click starts typing, Esc ends it, a controller without a system keyboard gets `PadKeyboard`;
  the menu's and the game's keys rest meanwhile, `GameControls.TextEntryOpen` + `NetChat.SetTyping`), "Include current
  game" and Send report (an empty description is refused). `IssueReport.Collect` builds the files: `report.txt` (the
  description, build, system without device name or id, scene / campaign step / rules / orbit / ship position, every
  mod with version, hash, errors and warnings), `screenshot.jpg`, `log.txt` (the last 400 log lines of the run, kept in
  memory from `Bootstrap` on since a phone has no Player.log; errors with their stack), the ends of `Player.log` /
  `Player-prev.log` in a build (512 / 256 KB), `settings.txt` (every `OptionsCatalog` option by id) and `save.json`
  (`SaveGame.SnapshotJson`: the running game, loadable as a `slot_NN.json`). The home folder is cut from every text
  (`Scrub`: "~"). `WriteZip` keeps them as `<data>/IssueReports/GoF2_issue_<time>.zip` (the newest 20).
  **Upload**: Unity's User Reporting (`com.unity.services.user-reporting` 2.0.15, the project's Unity Cloud id, already
  linked for Relay / Lobby): Unity Services start on the first report (`NetGame.StartServices`, no sign-in), a fresh
  client per report with its per-frame metrics off, the files as attachments (3 MB each, 8 MB together), the picture as
  the report's screenshot (through a render texture: the package only reads the screen, a camera or a render texture),
  the title (the description's first line) as summary, version / platform / language as dimensions. Reports are read in
  the Unity Dashboard (the project > Cloud Diagnostics > User Reporting). The code is behind `GOF2_USER_REPORTING`, a
  version define of `GoF2.App.asmdef`: without the package the game compiles and only saves the zip. Not sent (offline,
  the upload failed): the result page names the zip, Show file opens its folder and, on desktop, Open GitHub opens a
  filled-in issue to attach it to (`IssueReport.IssueUrl`, the address kept under 7000 characters). Checked in Play
  mode 2026-11-13: a report from the Space scene arrived (the service answered with the stored report), its zip held
  every file; the keys were checked through the form's own handlers (simulated keys don't reach an unfocused
  Editor). Not yet: the station menu and the main menu have no entry, nothing run on a phone.
- **FMOD event IDs -> .ogg**: `Reference/research/fmod_event_ids.txt` (every system id, from the FEV's LGCY data; see
  "Known open items"). Menu theme = 145 `Space_NoCombat_Void`, new game = 143 `IntroAtmo`, buttons 124/123, dialog 126.
- **Build times** (`ShaderVariantRules`, `ShaderVariantStripper`, 2026-10-31): a full Linux build took 940 s (Clean Build,
  2026-10-26): 640 s shaders, 262 s IL2CPP's C++, the rest ~40 s. All 16 threads are busy through both (15.9 and 15.5 on
  average, from `Library/BuildHistory/<id>/BuildPlayerTEP.json`, which holds each build's trace), so only compiling less helps.
  `GoF2/Hull` was 6425 of the 10 110 CPU-seconds and URP Lit 2274: 4232 and 1608 variants at 1.4 s each. A dry build dumped
  what a build asks for: of Hull's 4096 forward variants 8 combinations are materials, the rest doublings of API, stereo, LOD
  crossfade, fog modes, cookies, SSAO and shadows. The stripper drops what the game can't turn on (the rules' header says
  why each is safe; `ShaderVariantTests` reads the prefabs and scenes for crossfades and cookies): 14 612 variants to 4322,
  Hull to 290, build size the same, 183 s measured with the other shaders cached and a third of the C++ to do; a Clean Build
  should be near 370 s (estimate: about 70 s of shaders plus the C++). Clean Build keeps the shader cache (`Library/ShaderCache`)
  but redoes all of IL2CPP. A cancelled build seems to lose the shader it was on: Hull started over on 2026-10-31 (2884 of 4232).
  Not stripped, they are live: SSAO, both main-light shadow keywords, soft shadows, UberPost's effects (the photo tools use
  them). `PaniniProjection ... stripped` in a player's log is URP's own (it was never in the build).
  **Backend profiles** (`BuildProfilesBuilder`, GoF2 > Build > Backend Build Profiles, 2026-10-31): a build compiles every
  shader once per graphics API, so each platform has a profile per backend beside its multiple-backend one, which keeps its
  name and the project's list: Linux (OpenGLCore, then Vulkan, automatic), Linux Vulkan, Linux OpenGL; Windows (D3D12, D3D11),
  Windows D3D12, Windows D3D11; Android (Vulkan, GLES3), Android Vulkan, Android GLES3; macOS and iOS are Metal alone, so one
  plain profile each. `unity build --profile "Linux Vulkan"` builds one. A profile saved without its platform's Editor module has
  empty platform settings; Unity fills them in when an Editor with the module loaded next opens it (the module loads at startup,
  so install, then restart). The override is a Player Settings override on a copy of the platform's profile, and that is a whole frozen copy
  of the Player Settings: changes to them don't reach these profiles until the menu item is run again (which also gives them
  new GUIDs; nothing refers to them). `BuildVersionStamp` still reaches the build (checked: Linux Vulkan's `globalgamemanagers`
  holds the commit's version). Linux Vulkan: 2161 variants against the Linux profile's 4322, half the size in shaders, 49 s
  with shaders and C++ cached; only that one has been built so far. Automatic Linux runs on OpenGLCore first, so Linux Vulkan
  also changes what players render with; a single-backend build has no fallback if that API fails to start. D3D11 alone loses
  FSR 3 / 4 and GLES3 alone FSR (options.md). Linux, Android and iOS drop stereo, VR being unable to start there (OpenXR: no
  Linux library; no XR loader for Android, no XR settings for iOS); macOS keeps it, as OpenXR ships a loader for its players.
  Android is already ARM64 alone, and the platforms' quality levels already exclude each other's URP assets. The macOS profile
  is Unity's default, Apple silicon alone (x64 or both is its Architecture setting), and with the project's IL2CPP builds only on
  a Mac: this Linux Editor's macOS module has CoreCLR players alone (`scripting backend (IL2CPP) is not installed`, 2026-10-31).
  The iOS profile builds here, IL2CPP included, as far as the Xcode project (2026-10-31: 1671 variants, 105 s, 11 min with the
  first iOS import of every asset, no errors); compiling and signing that project needs a Mac. `Linux Server` keeps its Development tick (the menu's
  Linux Dedicated Server build passes its own options).
- **Steam Deck relaunch** (remake, `SteamDeckRelaunch`, GoF2.App; #232): Steam sets `SteamDeck=1` for every launch on the
  Deck, and Unity 7000.0.0a12's Linux player then selects a Steam Deck presentation path (the variable's only reference in
  `UnityPlayer.so`, next to the Xdbe strings) that stalls the render loop at ~0 FPS. The engine reads it before managed code,
  so the Linux player (not the Editor or a server) re-execs `/proc/self/exe` once at SubsystemRegistration without it, leaving
  `GOF2_STEAMDECK=1` (the loop guard, also read by `UiLayout.Detect` and `InputDeviceLog`). `GOF2_KEEP_STEAMDECK=1` skips it;
  players' fallback is the launch option `env -u SteamDeck %command%`.
