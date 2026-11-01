// NetLobby.cs
// Remake-only: the server browser, on Unity Lobby (Multiplayer Services). An online host (NetGame: a Relay session) that is
// listed publishes a public lobby carrying what the browser shows (the session's name, the host, the players, a dedicated
// server or not, the build version, the Relay region its traffic goes through) and its Relay join code; the Multiplayer panel lists every version's lobbies, this
// build's first, and joins a picked one of the same version with that code (NetGame.StartClientOnline; the connection
// approval would turn any other version away, so those rows only say which version they need). Nobody joins the lobby
// itself: it is only the listing, kept alive by the host's heartbeat (15 s; Lobby hides one without a heartbeat for 30 s)
// with the player count updated as it changes; the session's end deletes it (a game that closed without that drops out of
// the list after those 30 s).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class NetLobby
    {
        const float HeartbeatSeconds = 15f, CountSeconds = 5f;
        const string KeyVersion = "version", KeyProtocol = "protocol", KeyCode = "code", KeyHost = "host", KeyPlayers = "players", KeyServer = "server", KeyPassword = "password",
            KeyMods = "mods", KeyModNames = "modnames", KeyPvp = "pvp", KeyRegion = "region", KeyCoop = "coop";

        /// <summary>One listed session in the browser.</summary>
        public sealed class Entry
        {
            public string name, host, code, version, protocol;
            /// <summary>The session's mods (NetMods.SessionList) and their names for showing; empty = none.</summary>
            public string mods = "", modNames = "";
            public bool Modded => !string.IsNullOrEmpty(mods);
            /// <summary>The mods this game lacks to join (names), empty = it has them all.</summary>
            public List<string> MissingMods => Modded ? NetMods.MissingForListing(mods) : new List<string>();
            /// <summary>The Unity Relay region the session runs through ("europe-west4"); the browser shows its short
            /// name and the round trip to it (NetPing). Empty for a listing from before the key.</summary>
            public string region = "";
            public int players, maxPlayers;
            public bool dedicated, password;
            /// <summary>Players fight anywhere (NetGame.FreePvp: "PvP") or only in arenas and sieges ("PvE"); null for a
            /// listing from before the key (no chip).</summary>
            public bool? pvp;
            /// <summary>A co-op campaign (NetGame.CoopSession): 2-4 players playing the host's story.</summary>
            public bool coop;
            public bool Full => players >= maxPlayers;
            /// <summary>The same code as this game (the fingerprint: the only ones it can join, whenever they were built; the
            /// Editor joins any, for testing). A listing from before the fingerprint has only its version.</summary>
            public bool SameVersion => (string.IsNullOrEmpty(protocol) ? version : protocol) == NetGame.Protocol || Application.isEditor;
        }

        static string lobbyId;
        static int generation, lastCount = -1, lastPvp = -1;

        /// <summary>This game's session is listed (its lobby exists).</summary>
        public static bool Listed => lobbyId != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lobbyId = null;
            generation++;
            lastCount = -1;
            lastPvp = -1;
        }

        static DataObject Public(string value, DataObject.IndexOptions index = default) =>
            index == default ? new DataObject(DataObject.VisibilityOptions.Public, value) : new DataObject(DataObject.VisibilityOptions.Public, value, index);

        /// <summary>The host (signed in, its Relay session started): lists it until Unpublish.</summary>
        public static async void Publish(string name, string host, string code, int maxPlayers, bool dedicated)
        {
            Unpublish();
            int gen = ++generation;
            try
            {
                var lobby = await LobbyService.Instance.CreateLobbyAsync(Trim(name, 60), Mathf.Clamp(maxPlayers, 1, 100), new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>
                    {
                        [KeyVersion] = Public(NetGame.Version, DataObject.IndexOptions.S1),
                        [KeyProtocol] = Public(NetGame.Protocol),
                        [KeyCode] = Public(code),
                        [KeyHost] = Public(Trim(host, NetGame.MaxNameLength)),
                        [KeyPlayers] = Public(PlayerCount().ToString(), DataObject.IndexOptions.N1),
                        [KeyServer] = Public(dedicated ? "1" : "0"),
                        [KeyPassword] = Public(NetGame.HasPassword ? "1" : "0"),
                        [KeyMods] = Public(Trim(NetMods.SessionList, 2000)),
                        [KeyModNames] = Public(Trim(NetMods.SessionNames, 300)),
                        [KeyPvp] = Public(NetGame.FreePvp ? "1" : "0"),
                        [KeyRegion] = Public(Trim(NetGame.HostRegion, 40)),
                        [KeyCoop] = Public(NetGame.CoopSession ? "1" : "0"),
                    },
                });
                if (gen != generation || !NetGame.Active)
                {
                    // The session ended while the lobby was being made.
                    _ = LobbyService.Instance.DeleteLobbyAsync(lobby.Id);
                    return;
                }
                lobbyId = lobby.Id;
                lastCount = PlayerCount();
                lastPvp = NetGame.FreePvp ? 1 : 0;
                Debug.Log($"NetLobby: listed \"{lobby.Name}\" in the server browser");
                Keep(gen);
            }
            catch (Exception e)
            {
                Debug.LogWarning("NetLobby: listing the session failed (it can still be joined with its code): " + e.Message);
            }
        }

        /// <summary>The players in the session, the host's own included.</summary>
        static int PlayerCount() => NetGame.ClientIds.Count;

        /// <summary>The heartbeat and the player count, while this listing is the current one.</summary>
        static async void Keep(int gen)
        {
            float sinceBeat = 0f;
            while (gen == generation && lobbyId != null)
            {
                await Task.Delay((int)(CountSeconds * 1000));
                if (gen != generation || lobbyId == null) return;
                if (!NetGame.Active) { Unpublish(); return; }
                sinceBeat += CountSeconds;
                try
                {
                    int count = PlayerCount();
                    int pvp = NetGame.FreePvp ? 1 : 0;   // the server settings / web admin can switch it while the game runs
                    if (count != lastCount || pvp != lastPvp)
                    {
                        lastCount = count;
                        lastPvp = pvp;
                        await LobbyService.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions
                        {
                            Data = new Dictionary<string, DataObject>
                            {
                                [KeyPlayers] = Public(count.ToString(), DataObject.IndexOptions.N1),
                                [KeyPvp] = Public(pvp.ToString()),
                            },
                        });
                    }
                    if (sinceBeat >= HeartbeatSeconds && lobbyId != null)
                    {
                        sinceBeat = 0f;
                        await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("NetLobby: " + e.Message);
                }
            }
        }

        /// <summary>The session ended: off the list.</summary>
        public static void Unpublish()
        {
            generation++;
            string id = lobbyId;
            lobbyId = null;
            lastCount = -1;
            lastPvp = -1;
            if (id == null) return;
            try { _ = LobbyService.Instance.DeleteLobbyAsync(id); }
            catch (Exception) { }
        }

        /// <summary>The listed sessions, this version's first, the fullest first; null = the query failed (NetGame.Status says why).</summary>
        public static async Task<List<Entry>> Query()
        {
            try
            {
                await NetGame.SignInForOnline();
                var response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                {
                    Count = 100,
                    Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.N1), new QueryOrder(false, QueryOrder.FieldOptions.Created) },
                });
                var list = new List<Entry>();
                foreach (var l in response.Results)
                {
                    string code = Get(l, KeyCode);
                    if (string.IsNullOrEmpty(code)) continue;
                    int.TryParse(Get(l, KeyPlayers), out int players);
                    list.Add(new Entry
                    {
                        name = l.Name, host = Get(l, KeyHost), code = code, version = Get(l, KeyVersion), protocol = Get(l, KeyProtocol),
                        players = players, maxPlayers = l.MaxPlayers, dedicated = Get(l, KeyServer) == "1", password = Get(l, KeyPassword) == "1",
                        mods = Get(l, KeyMods) ?? "", modNames = Get(l, KeyModNames) ?? "",
                        pvp = Get(l, KeyPvp) switch { "1" => true, "0" => false, _ => (bool?)null },
                        region = Get(l, KeyRegion) ?? "",
                        coop = Get(l, KeyCoop) == "1",
                    });
                }
                // This version's first (the query's order, the fullest first, within each).
                var ordered = list.FindAll(x => x.SameVersion);
                ordered.AddRange(list.FindAll(x => !x.SameVersion));
                return ordered;
            }
            catch (Exception e)
            {
                NetGame.SetOnlineError(e);
                Debug.LogWarning("NetLobby: the server list failed: " + e.Message);
                return null;
            }
        }

        static string Get(Lobby l, string key) => l.Data != null && l.Data.TryGetValue(key, out var d) ? d.Value : null;

        static string Trim(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
