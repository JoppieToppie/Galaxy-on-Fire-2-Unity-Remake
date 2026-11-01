// AppHooksInstaller.cs
// Fills in what the layers below the screens (GoF2.Space, GoF2.Net) need from them: the star map, the VR eye, the
// multiplayer window, the graphics settings (SpaceHooks), the dialogue window, the flight HUD's messages and the station
// menu's toasts, dialogs, credits and cargo (UiHooks). Runs before the first scene, and in the Editor on every load
// (EditorHookInstaller).

using GoF2Remake.UI;
using GoF2Remake.Vr;
using GoF2Remake.World;
using UnityEngine;

namespace GoF2Remake
{
    public static class AppHooksInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            SpaceHooks.MultiplayerWindowOpen = () => MultiplayerWindow.IsOpenAny;
            SpaceHooks.StarMapOpenProvider = () => StarMap.IsOpen;
            SpaceHooks.OpenStarMap = (db, mode, jumpDrive, closed, promptStation, askVoid) =>
                StarMap.Open(db, mode, jumpDrive, closed, promptStation, -1, askVoid) != null;
            SpaceHooks.VrEye = () => VrRig.Current != null ? VrRig.Current.Eye : null;
            SpaceHooks.SetSceneFog = Bootstrap.SetSceneFog;
            SpaceHooks.KeepHud = FlightHud.KeepForSwap;
            SpaceHooks.DropNewHud = FlightHud.DropSceneHud;
            SpaceHooks.SettleHud = FlightHud.SettleKept;
            SpaceHooks.TemporalUpscaler = () => Bootstrap.IsTemporal(Bootstrap.ActiveUpscaler);

            UiHooks.LatestDialogueProvider = () => DialogueView.Latest;
            UiHooks.FlightMessage = text =>
            {
                var hud = Object.FindAnyObjectByType<FlightHud>();
                if (hud == null) return false;
                hud.ShowMessage(text);
                return true;
            };
            UiHooks.StationToast = text =>
            {
                var menu = Object.FindAnyObjectByType<StationMenu>();
                if (menu == null) return false;
                menu.ShowToast(text);
                return true;
            };
            UiHooks.StationDialog = (text, onYes, info) =>
            {
                var menu = Object.FindAnyObjectByType<StationMenu>();
                if (menu == null) return false;
                menu.ShowDialog(text, onYes, info);
                return true;
            };
            UiHooks.RefreshStationCredits = () => Object.FindAnyObjectByType<StationMenu>()?.RefreshCredits();
            UiHooks.StationCargoChanged = () => Object.FindAnyObjectByType<StationMenu>()?.CargoChanged();
            UiHooks.OpenTrade = MultiplayerWindow.OpenTradeAny;
        }
    }
}
