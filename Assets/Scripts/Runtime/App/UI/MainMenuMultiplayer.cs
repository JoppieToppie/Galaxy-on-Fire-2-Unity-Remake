// MainMenuMultiplayer.cs
// Part of MainMenu: the Multiplayer panel: the server browser, the host card (mode, password, player limit, Debug /
// Combat / World / Mods switches, addresses), the pilot name prompt and the fade into hosting or joining.

using System;
using System.Collections;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace GoF2Remake.UI
{
    public partial class MainMenu
    {
        /// <summary>The name a public game is listed under: the Game name field, else "<pilot>'s universe".</summary>
        string SessionName()
        {
            string n = mpSessionName != null ? mpSessionName.value.Replace("<", "").Replace(">", "").Trim() : "";
            return n.Length > 0 ? n : GoF2Remake.Multiplayer.NetGame.DefaultSessionName();
        }

        void SetupMultiplayerPanel()
        {
            mpAddress = root.Q<TextField>("mpAddress");
            mpStatus = root.Q<Label>("mpStatus");
            if (mpAddress != null) mpAddress.value = PlayerPrefs.GetString("mp_address", "127.0.0.1");
            mpName = root.Q<TextField>("mpName");
            if (mpName != null)
            {
                mpName.maxLength = GoF2Remake.Multiplayer.NetGame.MaxNameLength;
                mpName.value = GoF2Remake.Multiplayer.NetGame.PlayerName;
                mpName.RegisterValueChangedCallback(e => { GoF2Remake.Multiplayer.NetGame.PlayerName = e.newValue; ApplyHostMode(); });
            }
            mpAddressList = root.Q("mpAddressList");
            mpPort = root.Q<TextField>("mpPort");
            if (mpPort != null)
            {
                mpPort.maxLength = 5;
                mpPort.keyboardType = TouchScreenKeyboardType.NumberPad;   // the on-screen keyboard's number pad
                mpPort.value = GoF2Remake.Multiplayer.NetGame.HostPort.ToString();
                mpPort.RegisterValueChangedCallback(e =>
                {
                    if (ushort.TryParse(e.newValue, out ushort p) && p >= 1024) GoF2Remake.Multiplayer.NetGame.HostPort = p;
                    RefreshAddresses();
                });
            }
            mpLocalBox = root.Q("mpLocalBox");
            SetupHostMore();
            mpSessionName = root.Q<TextField>("mpSessionName");
            if (mpSessionName != null)
            {
                mpSessionName.maxLength = 48;
                mpSessionName.value = PlayerPrefs.GetString("mp_session_name", "");
                mpSessionName.RegisterValueChangedCallback(e => PlayerPrefs.SetString("mp_session_name", e.newValue.Trim()));
            }
            var hostPassword = root.Q<TextField>("mpHostPassword");
            if (hostPassword != null)
            {
                hostPassword.maxLength = GoF2Remake.Multiplayer.NetGame.MaxPasswordLength;
                hostPassword.isPasswordField = true;
                hostPassword.value = PlayerPrefs.GetString("mp_host_password", "");
                GoF2Remake.Multiplayer.NetGame.HostPassword = hostPassword.value;
                hostPassword.RegisterValueChangedCallback(e =>
                {
                    GoF2Remake.Multiplayer.NetGame.HostPassword = e.newValue;
                    PlayerPrefs.SetString("mp_host_password", GoF2Remake.Multiplayer.NetGame.CleanPassword(e.newValue));
                });
            }
            var maxPlayers = root.Q<TextField>("mpMaxPlayers");
            if (maxPlayers != null)
            {
                // The session's size, the host included (2..100, PlayerPrefs "mp_max_players"); shown clamped once left.
                maxPlayers.maxLength = 3;
                maxPlayers.keyboardType = TouchScreenKeyboardType.NumberPad;
                GoF2Remake.Multiplayer.NetGame.MaxPlayers = PlayerPrefs.GetInt("mp_max_players", GoF2Remake.Multiplayer.NetGame.DefaultMaxPlayers);
                maxPlayers.value = GoF2Remake.Multiplayer.NetGame.MaxPlayers.ToString();
                maxPlayers.RegisterValueChangedCallback(e =>
                {
                    if (!int.TryParse(e.newValue, out int n)) return;
                    GoF2Remake.Multiplayer.NetGame.MaxPlayers = n;
                    PlayerPrefs.SetInt("mp_max_players", GoF2Remake.Multiplayer.NetGame.MaxPlayers);
                });
                maxPlayers.RegisterCallback<FocusOutEvent>(_ => maxPlayers.SetValueWithoutNotify(GoF2Remake.Multiplayer.NetGame.MaxPlayers.ToString()));
            }
            // The Debug menu in the hosted session (PlayerPrefs "mp_allow_debug", off by default; NetGame.HostAllowsDebug).
            GoF2Remake.Multiplayer.NetGame.HostAllowsDebug = PlayerPrefs.GetInt("mp_allow_debug", 0) != 0;
            Bind("mpDebugOff", () => SetHostDebug(false));
            Bind("mpDebugOn", () => SetHostDebug(true));
            ApplyHostDebug();
            // Combat in the hosted session (PlayerPrefs "mp_pvp", PvE by default; NetGame.FreePvp): PvE = players fight only in
            // arena matches and faction sieges, PvP = anywhere. The server browser shows it as a chip.
            GoF2Remake.Multiplayer.NetGame.FreePvp = PlayerPrefs.GetInt("mp_pvp", 0) != 0;
            Bind("mpPvpOff", () => SetHostPvp(false));
            Bind("mpPvpOn", () => SetHostPvp(true));
            ApplyHostPvp();
            // The hosted world (PlayerPrefs "mp_persistent": 0 fresh by default, 1 persistent, 2 co-op): persistent keeps every
            // player's progress, the factions, bans and news on this device (NetGame.HostWantsPersistent; the host is its
            // master admin); co-op is the host's story campaign for 2-4 players (NetGame.HostWantsCoop, NetCoop).
            int world = PlayerPrefs.GetInt("mp_persistent", 0);
            GoF2Remake.Multiplayer.NetGame.HostWantsPersistent = world == 1;
            GoF2Remake.Multiplayer.NetGame.HostWantsCoop = world == 2;
            Bind("mpWorldFresh", () => SetHostWorld(0));
            Bind("mpWorldPersistent", () => SetHostWorld(1));
            Bind("mpWorldCoop", () => SetHostWorld(2));
            // A co-op campaign's settings (shown with Co-op): the enemies' strength per player past the first
            // (PlayerPrefs "mp_coop_strength", +50 % by default) and the story credits shared out or paid in full ("mp_coop_split").
            foreach (int pct in GoF2Remake.Data.CoopCampaign.StrengthChoices)
            {
                int choice = pct;
                Bind("mpCoopStrength" + pct, () => { GoF2Remake.Multiplayer.NetGame.CoopStrength = choice; ApplyHostWorld(); });
            }
            Bind("mpCoopRewardsShared", () => { GoF2Remake.Multiplayer.NetGame.CoopSplitRewards = true; ApplyHostWorld(); });
            Bind("mpCoopRewardsFull", () => { GoF2Remake.Multiplayer.NetGame.CoopSplitRewards = false; ApplyHostWorld(); });
            ApplyHostWorld();
            // Modded content in the hosted session (PlayerPrefs "mp_allow_mods", off by default; NetMods.HostAllowsMods).
            GoF2Remake.Multiplayer.NetMods.HostAllowsMods = PlayerPrefs.GetInt("mp_allow_mods", 0) != 0;
            Bind("mpModsOff", () => SetHostMods(false));
            Bind("mpModsOn", () => SetHostMods(true));
            ApplyHostMods();
            mpJoinPassword = root.Q<TextField>("mpJoinPassword");
            if (mpJoinPassword != null)
            {
                mpJoinPassword.maxLength = GoF2Remake.Multiplayer.NetGame.MaxPasswordLength;
                mpJoinPassword.isPasswordField = true;
                mpJoinPassword.value = GoF2Remake.Multiplayer.NetGame.JoinPassword;   // this run's only (not saved)
                mpJoinPassword.RegisterValueChangedCallback(e => GoF2Remake.Multiplayer.NetGame.JoinPassword = e.newValue);
            }
            mpServerList = root.Q<ScrollView>("mpServerList");
            Bind("mpModePublic", () => SetHostMode(HostMode.Public));
            Bind("mpModePrivate", () => SetHostMode(HostMode.Private));
            Bind("mpModeLocal", () => SetHostMode(HostMode.Local));
            ApplyHostMode();
            Bind("mpHost", () => WithName(() =>
            {
                if (GoF2Remake.Multiplayer.NetGame.HostWantsCoop) AskCoopCampaign();   // which campaign the crew plays
                else StartCoroutine(LeaveForMultiplayer(null));
            }));
            Bind("mpJoin", () =>
            {
                string address = mpAddress != null ? mpAddress.value.Trim() : "";
                if (address.Length == 0) return;
                PlayerPrefs.SetString("mp_address", address);
                WithName(() => StartCoroutine(LeaveForMultiplayer(address)));
            });
        }

        /// <summary>-mphost: hosts once the menu is up (the panel has listed the addresses).</summary>
        IEnumerator TestHostSoon()
        {
            yield return new WaitForSeconds(2f);
            while (screen != MenuState.Menu) yield return null;
            yield return LeaveForMultiplayer(null);
        }

        /// <summary>-mpjoin: joins that address, again every 2 s until a host answers (and after a session ends).</summary>
        IEnumerator AutoJoin(string address)
        {
            while (true)
            {
                while (screen != MenuState.Menu) yield return null;
                yield return LeaveForMultiplayer(address);   // returns only when the connection failed
                yield return new WaitForSeconds(2f);
            }
        }

        void SetHostDebug(bool allowed)
        {
            GoF2Remake.Multiplayer.NetGame.HostAllowsDebug = allowed;
            PlayerPrefs.SetInt("mp_allow_debug", allowed ? 1 : 0);
            ApplyHostDebug();
        }

        void ApplyHostDebug()
        {
            bool allowed = GoF2Remake.Multiplayer.NetGame.HostAllowsDebug;
            root.Q<Button>("mpDebugOff")?.EnableInClassList("choice-segment--active", !allowed);
            root.Q<Button>("mpDebugOn")?.EnableInClassList("choice-segment--active", allowed);
        }

        void SetHostPvp(bool pvp)
        {
            GoF2Remake.Multiplayer.NetGame.FreePvp = pvp;
            PlayerPrefs.SetInt("mp_pvp", pvp ? 1 : 0);
            ApplyHostPvp();
        }

        void ApplyHostPvp()
        {
            bool pvp = GoF2Remake.Multiplayer.NetGame.FreePvp;
            root.Q<Button>("mpPvpOff")?.EnableInClassList("choice-segment--active", !pvp);
            root.Q<Button>("mpPvpOn")?.EnableInClassList("choice-segment--active", pvp);
        }

        /// <summary>0 fresh, 1 persistent, 2 co-op campaign.</summary>
        void SetHostWorld(int world)
        {
            GoF2Remake.Multiplayer.NetGame.HostWantsPersistent = world == 1;
            GoF2Remake.Multiplayer.NetGame.HostWantsCoop = world == 2;
            PlayerPrefs.SetInt("mp_persistent", world);
            ApplyHostWorld();
            // A co-op campaign takes 2-4 players: the field shows what the session will allow.
            var maxPlayers = root.Q<TextField>("mpMaxPlayers");
            maxPlayers?.SetValueWithoutNotify(GoF2Remake.Multiplayer.NetGame.MaxPlayers.ToString());
        }

        void ApplyHostWorld()
        {
            bool persistent = GoF2Remake.Multiplayer.NetGame.HostWantsPersistent, coop = GoF2Remake.Multiplayer.NetGame.HostWantsCoop;
            root.Q<Button>("mpWorldFresh")?.EnableInClassList("choice-segment--active", !persistent && !coop);
            root.Q<Button>("mpWorldPersistent")?.EnableInClassList("choice-segment--active", persistent);
            root.Q<Button>("mpWorldCoop")?.EnableInClassList("choice-segment--active", coop);
            int strength = GoF2Remake.Multiplayer.NetGame.CoopStrength;
            foreach (int pct in GoF2Remake.Data.CoopCampaign.StrengthChoices)
                root.Q<Button>("mpCoopStrength" + pct)?.EnableInClassList("choice-segment--active", strength == pct);
            bool split = GoF2Remake.Multiplayer.NetGame.CoopSplitRewards;
            root.Q<Button>("mpCoopRewardsShared")?.EnableInClassList("choice-segment--active", split);
            root.Q<Button>("mpCoopRewardsFull")?.EnableInClassList("choice-segment--active", !split);
            foreach (var row in new[] { "mpCoopStrengthRow", "mpCoopRewardsRow" })
                if (root.Q(row) is VisualElement r) r.style.display = coop ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static string CoopTitle => Localization.Extra("coopTitle", "Co-op campaign");

        static string CampaignName(int campaign) => campaign switch { 1 => "Valkyrie", 2 => "Supernova", _ => "Galaxy on Fire 2" };

        /// <summary>Hosting a co-op campaign: go on with the saved one (NetCoop's summary) or start a new one.</summary>
        void AskCoopCampaign()
        {
            var saved = GoF2Remake.Multiplayer.NetCoop.ReadSummary();
            if (saved == null) { AskNewCoopCampaign(); return; }
            string crew = string.IsNullOrEmpty(saved.crew) ? "" : "\n" + string.Format(Localization.Extra("coopCrew", "Crew: {0}"), saved.crew);
            ShowChoice(CoopTitle,
                string.Format(Localization.Extra("coopContinueText", "Go on with the crew's {0} campaign at step {1}?"), CampaignName(saved.campaign), saved.index) + crew,
                Localization.Extra("coopContinue", "Continue"), () => HostCoop(-1),
                Localization.Extra("coopNew", "New campaign"), AskNewCoopCampaign);
        }

        void AskNewCoopCampaign()
        {
            string text = Localization.Extra("coopNewText", "Which campaign does the crew play? A saved co-op campaign is replaced (its files are kept as a backup).");
            ShowChoice(CoopTitle, text, CampaignName(0), () => HostCoop(0), Localization.Extra("coopAddons", "Add-ons"),
                () => ShowChoice(CoopTitle, text, CampaignName(1), () => HostCoop(1), CampaignName(2), () => HostCoop(2)));
        }

        /// <summary>A new co-op campaign's lobby (NetGame.InLobby): the menu's dialog lists the crew; the host's Start takes
        /// everyone into the campaign at once (the main game's prologue in flight), Leave ends the session for this player
        /// (the host's: for everyone).</summary>
        IEnumerator CoopLobby()
        {
            fade.RemoveFromClassList("fade--on");
            screen = MenuState.Menu;
            StartCoroutine(FadeMusic(Settings.MusicVolume, 1f));
            bool host = GoF2Remake.Multiplayer.NetGame.IsServer;
            ShowDialog(CoopTitle, LobbyText(host), host ? () => StartCoopFromLobby() : (System.Action)null);
            dialogNo = LeaveLobby;
            var yes = root.Q<Button>("dialogYes");
            var no = root.Q<Button>("dialogNo");
            if (host) InputGlyph.SetText(yes, Localization.Extra("coopStart", "Start").ToUpperInvariant());
            else yes.style.display = DisplayStyle.None;
            InputGlyph.SetText(no, Localization.Extra("coopLeave", "Leave").ToUpperInvariant());
            Select(host ? yes : no);
            // Kept up to date while the crew comes and goes, until the host starts or the session ends.
            while (GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetGame.InLobby)
            {
                if (dialog.ClassListContains("dialog-backdrop--shown")) root.Q<Label>("dialogText").text = LobbyText(host);
                yield return new WaitForSecondsRealtime(0.5f);
            }
            yes.style.display = StyleKeyword.Null;
            if (dialog.ClassListContains("dialog-backdrop--shown")) CloseDialog();
            if (GoF2Remake.Multiplayer.NetGame.Active)
            {
                // Into the world: the fade as for any start.
                screen = MenuState.Leaving;
                fade.AddToClassList("fade--on");
                StartCoroutine(FadeMusic(0f, 1.2f));
            }
        }

        static string LobbyText(bool host)
        {
            var crew = GoF2Remake.Multiplayer.NetCoop.CrewNames();
            string list = string.Format(Localization.Extra("coopLobbyCrew", "Crew ({0}/{1}): {2}"), crew.Count,
                                        GoF2Remake.Data.CoopCampaign.MaxCrew, string.Join(", ", crew));
            string code = GoF2Remake.Multiplayer.NetGame.JoinCode;
            if (host && !string.IsNullOrEmpty(code)) list += "\n" + string.Format(Localization.Extra("coopLobbyCode", "Join code: {0}"), code);
            string wait = host ? Localization.Extra("coopLobbyHost", "Start once the crew is here: everyone begins the campaign together.")
                               : string.Format(Localization.Extra("coopLobbyWait", "Waiting for {0} to start the campaign."), crew.Count > 0 ? crew[0] : "");
            return list + "\n\n" + wait;
        }

        void StartCoopFromLobby()
        {
            if (GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetGame.InLobby) GoF2Remake.Multiplayer.NetCoop.StartCampaign();
        }

        void LeaveLobby()
        {
            root.Q<Button>("dialogYes").style.display = StyleKeyword.Null;
            GoF2Remake.Multiplayer.NetGame.Shutdown();
        }

        void HostCoop(int newCampaign)
        {
            GoF2Remake.Multiplayer.NetGame.CoopNewCampaign = newCampaign;
            StartCoroutine(LeaveForMultiplayer(null));
        }

        void SetHostMods(bool allowed)
        {
            GoF2Remake.Multiplayer.NetMods.HostAllowsMods = allowed;
            PlayerPrefs.SetInt("mp_allow_mods", allowed ? 1 : 0);
            ApplyHostMods();
        }

        void ApplyHostMods()
        {
            bool allowed = GoF2Remake.Multiplayer.NetMods.HostAllowsMods;
            root.Q<Button>("mpModsOff")?.EnableInClassList("choice-segment--active", !allowed);
            root.Q<Button>("mpModsOn")?.EnableInClassList("choice-segment--active", allowed);
            var label = root.Q<Label>("mpModsLabel");
            if (label != null)
            {
                int n = Modding.ModManager.SinglePlayerActive.Count;
                label.text = (n == 0 ? Localization.Extra("mpMods", "Mods")
                    : string.Format(Localization.Extra("mpModsCount", "Mods ({0} on)"), n)).ToUpperInvariant();
            }
        }

        /// <summary>The host card's settings scroll when they don't fit (several network adapters, a short screen), and the
        /// scroll bars are hidden everywhere: "More options" under them says so while settings are cut off below, and a
        /// tap scrolls down to them.</summary>
        void SetupHostMore()
        {
            var scroll = root.Q<ScrollView>("mpHostScroll");
            var more = root.Q<Label>("mpHostMore");
            if (scroll == null || more == null) return;
            more.text = "▼ " + Localization.Extra("mpMoreOptions", "More options").ToUpperInvariant();
            void Update()
            {
                float hidden = scroll.contentContainer.layout.height - scroll.contentViewport.layout.height;
                more.EnableInClassList("mp-host-more--shown", hidden > 1f && scroll.scrollOffset.y < hidden - 1f);
            }
            scroll.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => Update());
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Update());
            scroll.verticalScroller.valueChanged += _ => Update();
            more.RegisterCallback<ClickEvent>(_ =>
                scroll.scrollOffset = new Vector2(0f, scroll.scrollOffset.y + scroll.contentViewport.layout.height * 0.8f));
        }

        void SetHostMode(HostMode mode)
        {
            Mode = mode;
            ApplyHostMode();
        }

        /// <summary>The host card for its choice: the segments, the text, the addresses only for the local network.</summary>
        void ApplyHostMode()
        {
            var mode = Mode;
            void Seg(string name, HostMode m) => root.Q<Button>(name)?.EnableInClassList("choice-segment--active", mode == m);
            Seg("mpModePublic", HostMode.Public);
            Seg("mpModePrivate", HostMode.Private);
            Seg("mpModeLocal", HostMode.Local);
            if (mpLocalBox != null) mpLocalBox.style.display = mode == HostMode.Local ? DisplayStyle.Flex : DisplayStyle.None;
            // Online: the address box's room goes (the fill still pushes Host to the bottom, level with Join).
            root.Q("mpHostFill")?.EnableInClassList("mp-card-fill--empty", mode != HostMode.Local);
            if (mpSessionName != null)
            {
                // Only a listed game has a name; the placeholder shows the default (the pilot name may have changed).
                mpSessionName.style.display = mode == HostMode.Public ? DisplayStyle.Flex : DisplayStyle.None;
                mpSessionName.textEdition.placeholder = string.Format(Localization.Extra("mpSessionNameHint", "Game name: {0}"), GoF2Remake.Multiplayer.NetGame.DefaultSessionName());
            }
            var text = root.Q<Label>("mpHostText");
            if (text != null)
                text.text = mode == HostMode.Public
                    ? Localization.Extra("mpHostPublicText", "Listed in the server browser; you also get a join code.")
                    : mode == HostMode.Private
                        ? Localization.Extra("mpHostPrivateText", "Not listed: friends join with the code you get.")
                        : Localization.Extra("mpHostText", "Players on your network join with an address (tap to copy):");
        }

        /// <summary>While the Multiplayer panel is open: the server list, again every 5 s.</summary>
        IEnumerator BrowseServers()
        {
            float next = 0f;
            while (openPanel != null && panels.TryGetValue("multiplayerPanel", out var mp) && openPanel == mp)
            {
                if (Time.unscaledTime >= next && !serverQueryRunning && screen == MenuState.Menu)
                {
                    next = Time.unscaledTime + ServerRefreshSeconds;
                    yield return QueryServers();
                }
                if (serverPingRevision != GoF2Remake.Multiplayer.NetPing.Revision)
                {
                    serverPingRevision = GoF2Remake.Multiplayer.NetPing.Revision;
                    foreach (var fill in serverPingRows) fill();
                }
                yield return null;
            }
            serverBrowse = null;
        }

        IEnumerator QueryServers()
        {
            if (mpServerList == null) yield break;
            serverQueryRunning = true;
            if (mpServerList.childCount == 0) ServerListNote(Localization.Extra("mpSearching", "Looking for games..."), true);
            var query = GoF2Remake.Multiplayer.NetLobby.Query();
            while (!query.IsCompleted) yield return null;
            serverQueryRunning = false;
            var list = query.IsFaulted ? null : query.Result;
            var count = root.Q<Label>("mpServerCount");
            if (count != null)
                count.text = list == null ? "" : list.Count == 1 ? Localization.Extra("mpOneGame", "1 game")
                    : string.Format(Localization.Extra("mpGames", "{0} games"), list.Count);
            if (list == null) { ServerListNote(GoF2Remake.Multiplayer.NetGame.Status); yield break; }
            if (list.Count == 0) { ServerListNote(Localization.Extra("mpNoGames", "No public games right now. Host one, or join with a code.")); yield break; }
            // The round trip to each listed game's Relay region (NetPing: one QoS measurement per region, at most
            // once a minute); the rows fill their ping in when it lands.
            GoF2Remake.Multiplayer.NetPing.Measure(list.ConvertAll(x => x.region));
            // Unchanged since the last refresh: the rows stay (a controller's focus on one too).
            var key = new System.Text.StringBuilder();
            foreach (var e in list) key.Append(e.code).Append(e.name).Append(e.players).Append('/').Append(e.maxPlayers).Append(e.password).Append(e.mods).Append(e.pvp).Append('|');
            if (key.ToString() == serverListKey) yield break;
            serverListKey = key.ToString();
            mpServerList.Clear();
            serverPingRows.Clear();
            foreach (var e in list)
            {
                var entry = e;
                // Two lines: the name and its tags, then the host, the players and the version.
                var row = new Button { focusable = true };
                row.AddToClassList("mp-address-row");
                row.AddToClassList("mp-server-row");
                var missingMods = entry.MissingMods;   // a modded game: the mods this game lacks (NetMods)
                bool joinable = !entry.Full && entry.SameVersion && missingMods.Count == 0;
                row.EnableInClassList("mp-server-row--full", !joinable);
                var top = new VisualElement { pickingMode = PickingMode.Ignore };
                top.AddToClassList("mp-server-line");
                var name = new Label(entry.name) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("mp-server-name");
                name.AddToClassList("gof-semibold");
                top.Add(name);
                void Tag(string text, string extra)
                {
                    var tag = new Label(text.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                    tag.AddToClassList("mp-server-tag");
                    if (extra != null) tag.AddToClassList(extra);
                    top.Add(tag);
                }
                if (entry.dedicated) Tag(Localization.Extra("mpServerTag", "Server"), null);
                if (entry.coop) Tag(Localization.Extra("mpCoopTag", "Co-op"), "mp-server-tag--mods");   // a co-op campaign (NetCoop)
                if (entry.password) Tag(Localization.Extra("mpPasswordTag", "Password"), "mp-server-tag--password");
                if (entry.Modded) Tag(Localization.Extra("mpModdedTag", "Modded"), "mp-server-tag--mods");
                if (entry.pvp.HasValue)   // players fight anywhere / only in arenas and sieges (NetGame.FreePvp)
                    Tag(entry.pvp.Value ? Localization.Extra("mpPvp", "PvP") : Localization.Extra("mpPve", "PvE"),
                        entry.pvp.Value ? "mp-server-tag--pvp" : "mp-server-tag--pve");
                // Where the game is and how far away: the short name of its Relay region and the round trip to it,
                // with a green / amber / red dot (NetPing). A game listed by a build from before the region key has
                // none; a ping not measured yet reads "..." and fills in by itself.
                if (!string.IsNullOrEmpty(entry.region))
                {
                    var ping = new VisualElement { pickingMode = PickingMode.Ignore };
                    ping.AddToClassList("mp-server-ping");
                    var regionLabel = new Label(GoF2Remake.Multiplayer.NetPing.RegionName(entry.region)) { pickingMode = PickingMode.Ignore };
                    regionLabel.AddToClassList("mp-server-region");
                    ping.Add(regionLabel);
                    var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                    dot.AddToClassList("mp-server-ping-dot");
                    ping.Add(dot);
                    var msLabel = new Label { pickingMode = PickingMode.Ignore };
                    msLabel.AddToClassList("mp-server-ping-ms");
                    ping.Add(msLabel);
                    top.Add(ping);
                    void FillPing()
                    {
                        int ms = GoF2Remake.Multiplayer.NetPing.Of(entry.region);
                        int quality = GoF2Remake.Multiplayer.NetPing.Quality(ms);
                        msLabel.text = ms < 0 ? "..." : string.Format(Localization.Extra("mpPingMs", "{0} ms"), ms);
                        dot.EnableInClassList("mp-server-ping-dot--good", quality == 0);
                        dot.EnableInClassList("mp-server-ping-dot--fair", quality == 1);
                        dot.EnableInClassList("mp-server-ping-dot--poor", quality == 2);
                    }
                    FillPing();
                    serverPingRows.Add(FillPing);
                }
                row.Add(top);
                var bottom = new VisualElement { pickingMode = PickingMode.Ignore };
                bottom.AddToClassList("mp-server-line");
                string info = (entry.dedicated ? "" : entry.host + "  ·  ") + $"{entry.players} / {entry.maxPlayers}"
                              + (entry.Full ? "  ·  " + Localization.Extra("mpFull", "full") : "")
                              + (missingMods.Count > 0 ? "  ·  " + string.Format(Localization.Extra("mpNeedsModsShort", "needs mods: {0}"), string.Join(", ", missingMods))
                                 : entry.Modded ? "  ·  " + entry.modNames : "");
                var infoLabel = new Label(info) { pickingMode = PickingMode.Ignore };
                infoLabel.AddToClassList("mp-server-info");
                bottom.Add(infoLabel);
                // The version: every row; another version's says it can't be joined from here.
                var version = new Label(entry.SameVersion ? entry.version
                    : entry.version == GoF2Remake.Multiplayer.NetGame.Version ? Localization.Extra("mpOtherBuild", "another build")
                    : string.Format(Localization.Extra("mpNeedsVersion", "needs {0}"), entry.version)) { pickingMode = PickingMode.Ignore };
                version.AddToClassList("mp-server-version");
                version.EnableInClassList("mp-server-version--other", !entry.SameVersion);
                bottom.Add(version);
                row.Add(bottom);
                row.clicked += () =>
                {
                    if (joinable && entry.password && GoF2Remake.Multiplayer.NetGame.CleanPassword(GoF2Remake.Multiplayer.NetGame.JoinPassword).Length == 0)
                    {
                        // A password game: the field first (the server checks it).
                        SetMpStatus(Localization.Extra("mpEnterPassword", "This game needs a password: enter it under Join."));
                        mpJoinPassword?.Focus();
                    }
                    else if (joinable) WithName(() => StartCoroutine(LeaveForMultiplayer(entry.code)));
                    else if (mpStatus != null)
                        mpStatus.text = entry.Full ? Localization.Extra("mpGameFull", "That game is full.")
                            : entry.SameVersion && missingMods.Count > 0 ? string.Format(Localization.Extra("mpNeedsMods",
                                "Needs mods you don't have (or another version): {0}."), string.Join(", ", missingMods))
                            : entry.version == GoF2Remake.Multiplayer.NetGame.Version
                            ? string.Format(Localization.Extra("mpOtherBuildText", "That game runs another build of {0}: only the same build can join."), entry.version)
                            : string.Format(Localization.Extra("mpOtherVersion", "That game runs version {0}, yours is {1}."), entry.version, GoF2Remake.Multiplayer.NetGame.Version);
                };
                mpServerList.Add(row);
            }
        }

        void ServerListNote(string text, bool busy = false)
        {
            serverListKey = "";
            mpServerList.Clear();
            serverPingRows.Clear();
            var note = new Label(text);
            note.AddToClassList("mp-server-empty");
            UiBusy.Mark(note, busy);
            mpServerList.Add(note);
        }

        /// <summary>The Multiplayer panel's status line; 'busy' puts the waiting arc before it (docs/UI-DESIGN.md "Waiting").</summary>
        void SetMpStatus(string text, bool busy = false)
        {
            if (mpStatus == null) return;
            mpStatus.text = text;
            UiBusy.Mark(mpStatus, busy && !string.IsNullOrEmpty(text));
        }

        void OpenMultiplayer()
        {
            SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);   // why the last session ended
            OpenPanel("multiplayerPanel");
            // The first visit: the name first; Back there leaves the panel (#20: with a controller it came back to the
            // prompt on every move).
            if (NeedsName(null)) dialogNo = () => { if (openPanel == panels["multiplayerPanel"]) Back(); };
        }

        /// <summary>Multiplayer needs a pilot name (the others see it on the lock plate, in the chat and the pilot lists):
        /// without one (NetGame.PlayerName empty) a prompt asks for it, then runs 'then'. False = a name is set already.</summary>
        bool NeedsName(Action then)
        {
            if (GoF2Remake.Multiplayer.NetGame.Clean(GoF2Remake.Multiplayer.NetGame.PlayerName).Length > 0 || dialogField == null) return false;
            ShowDialog(Localization.Extra("mpNameTitle", "Pilot name"),
                       Localization.Extra("mpNameText", "The name other pilots see. Change it later at the top of the Multiplayer panel."), then);
            dialogField.value = "";
            dialogField.textEdition.placeholder = Localization.Extra("mpNamePlaceholder", "Your pilot name");
            dialogField.style.display = DisplayStyle.Flex;
            InputGlyph.SetText(root.Q<Button>("dialogYes"), "OK");
            InputGlyph.SetText(root.Q<Button>("dialogNo"), Localization.Get(170).ToUpperInvariant());   // Back
            dialogCheck = () =>
            {
                string name = GoF2Remake.Multiplayer.NetGame.Clean(dialogField.value);
                if (name.Length == 0) { dialogField.Focus(); return false; }
                GoF2Remake.Multiplayer.NetGame.PlayerName = name;
                if (mpName != null) mpName.SetValueWithoutNotify(GoF2Remake.Multiplayer.NetGame.PlayerName);
                ApplyHostMode();   // the default game name is the pilot's
                return true;
            };
            // With a controller the prompt opens on OK (A on it with no name, or Up, goes to the field): a field focused
            // at once brought the Xbox's keyboard up before the prompt had settled, and what it typed got lost (#20).
            if (InputMode.Current == InputKind.Gamepad) Select(root.Q<Button>("dialogYes"));
            else dialogField.Focus();
            return true;
        }

        /// <summary>Hosting and joining: 'action' once there is a pilot name (asked first when there is none).</summary>
        void WithName(Action action)
        {
            if (!NeedsName(action)) action();
        }

        /// <summary>The host card's addresses: one row per adapter (NetGame.LocalAddresses) with the port when it isn't the
        /// default; a tap copies it (what a friend types into Join).</summary>
        void RefreshAddresses()
        {
            if (mpAddressList == null) return;
            mpAddressList.Clear();
            ushort port = GoF2Remake.Multiplayer.NetGame.HostPort;
            var addresses = GoF2Remake.Multiplayer.NetGame.LocalAddresses();
            if (Debug.isDebugBuild) Debug.Log("Multiplayer addresses: " + string.Join(", ", addresses));
            if (addresses.Count == 0)
            {
                // No network: hosting still works on this device, but nobody else could join yet.
                var none = new Label(Localization.Extra("mpNoNetwork", "No network: connect to Wi-Fi or a hotspot so others can join."));
                none.AddToClassList("mp-card-text");
                mpAddressList.Add(none);
            }
            foreach (var (name, address) in addresses)
            {
                string join = port == GoF2Remake.Multiplayer.NetGame.DefaultPort ? address : $"{address}:{port}";
                var row = new Button { focusable = true };
                row.AddToClassList("mp-address-row");
                var n = new Label(name) { pickingMode = PickingMode.Ignore };
                n.AddToClassList("mp-address-name");
                var v = new Label(join) { pickingMode = PickingMode.Ignore };
                v.AddToClassList("mp-address-value");
                v.AddToClassList("gof-semibold");
                var c = new Label(Localization.Extra("mpCopy", "copy")) { pickingMode = PickingMode.Ignore };
                c.AddToClassList("mp-address-copy");
                row.Add(n); row.Add(v); row.Add(c);
                row.clicked += () =>
                {
                    GUIUtility.systemCopyBuffer = join;
                    foreach (var other in mpAddressList.Children()) other.RemoveFromClassList("mp-address-row--copied");
                    foreach (var other in mpAddressList.Query<Label>(className: "mp-address-copy").ToList()) other.text = Localization.Extra("mpCopy", "copy");
                    row.AddToClassList("mp-address-row--copied");
                    c.text = Localization.Extra("mpCopied", "copied");
                };
                mpAddressList.Add(row);
            }
        }

        /// <summary>Fades out, then hosts (address null: NetGame loads Space for everyone) or joins (the host's scene loads
        /// once connected). A failed start or connection fades the menu back in with the reason.</summary>
        IEnumerator LeaveForMultiplayer(string address)
        {
            if (screen != MenuState.Menu) yield break;
            screen = MenuState.Leaving;
            bool started;
            if (address == null && Mode != HostMode.Local)
            {
                // Online: the Relay session (and its listing) is reserved before the fade, so a failure shows here.
                SetMpStatus(Localization.Extra("mpOnlineStarting", "Starting an online session..."), true);
                var prep = GoF2Remake.Multiplayer.NetGame.PrepareOnlineHost(Mode == HostMode.Public ? SessionName() : null);
                while (!prep.IsCompleted) yield return null;
                if (prep.IsFaulted) GoF2Remake.Multiplayer.NetGame.AbortStart(prep.Exception);
                if (prep.IsFaulted || !prep.Result)
                {
                    SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);
                    screen = MenuState.Menu;
                    yield break;
                }
                SetMpStatus("");
                fade.AddToClassList("fade--on");
                StartCoroutine(FadeMusic(0f, 1.2f));
                yield return new WaitForSeconds(1.3f);
                started = TryStart(GoF2Remake.Multiplayer.NetGame.StartHost);
            }
            else if (address == null)
            {
                // Only fade out when it can start: a port in use says so at once (with a free one in the port field).
                if (!GoF2Remake.Multiplayer.NetGame.CanHost())
                {
                    SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);
                    if (GoF2Remake.Multiplayer.NetGame.SuggestedPort > 0 && mpPort != null) mpPort.value = GoF2Remake.Multiplayer.NetGame.SuggestedPort.ToString();
                    screen = MenuState.Menu;
                    yield break;
                }
                SetMpStatus("");
                fade.AddToClassList("fade--on");
                StartCoroutine(FadeMusic(0f, 1.2f));
                yield return new WaitForSeconds(1.3f);
                started = TryStart(GoF2Remake.Multiplayer.NetGame.StartHost);
            }
            else
            {
                // Joining: the menu stays while it connects (a missing host would be a long black screen); the fade once connected.
                SetMpStatus(string.Format(Localization.Extra("mpConnecting", "Connecting to {0}..."), address), true);
                if (GoF2Remake.Multiplayer.NetGame.IsJoinCode(address))
                {
                    // A join code: Relay finds the session, then it connects like an address.
                    var join = GoF2Remake.Multiplayer.NetGame.StartClientOnline(address);
                    while (!join.IsCompleted) yield return null;
                    if (join.IsFaulted) GoF2Remake.Multiplayer.NetGame.AbortStart(join.Exception);
                    started = !join.IsFaulted && join.Result;
                }
                else started = TryStart(() => GoF2Remake.Multiplayer.NetGame.StartClient(address));
                while (started && GoF2Remake.Multiplayer.NetGame.Active && !GoF2Remake.Multiplayer.NetGame.Connected) yield return null;
                if (started && GoF2Remake.Multiplayer.NetGame.Active)
                {
                    fade.AddToClassList("fade--on");
                    StartCoroutine(FadeMusic(0f, 1.2f));
                }
            }
            // A new co-op campaign's lobby: the crew gathers here until the host starts (the scene change ends this).
            while (started && GoF2Remake.Multiplayer.NetGame.Active && !GoF2Remake.Multiplayer.NetGame.InLobby) yield return null;
            if (started && GoF2Remake.Multiplayer.NetGame.Active && GoF2Remake.Multiplayer.NetGame.InLobby) yield return CoopLobby();
            while (started && GoF2Remake.Multiplayer.NetGame.Active) yield return null;   // the scene change ends this
            SetMpStatus(GoF2Remake.Multiplayer.NetGame.Status);
            // The port was in use: the free one it found goes into the port field (hosting again uses it).
            if (address == null && GoF2Remake.Multiplayer.NetGame.SuggestedPort > 0 && mpPort != null)
                mpPort.value = GoF2Remake.Multiplayer.NetGame.SuggestedPort.ToString();
            fade.RemoveFromClassList("fade--on");
            screen = MenuState.Menu;
            StartCoroutine(FadeMusic(Settings.MusicVolume, 1f));
        }

        /// <summary>A host / join start that threw ends with its reason in the status line (NetGame.AbortStart), not a
        /// coroutine stopped on the fade or on "Connecting..." for good (a Discord report: unable to play multiplayer at all).</summary>
        static bool TryStart(System.Func<bool> start)
        {
            try { return start(); }
            catch (System.Exception e) { GoF2Remake.Multiplayer.NetGame.AbortStart(e); return false; }
        }
    }
}
