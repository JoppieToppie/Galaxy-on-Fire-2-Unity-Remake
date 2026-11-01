// ModBuilderMcp.cs
// Remake-only: the Mod Builder's MCP server (Model Context Protocol, the way AI apps such as Claude Code, Claude Desktop or
// ChatGPT reach outside tools), so an AI assistant can build a mod in the open builder: look up the game's and the mods'
// content, read and write the project's entries and files, compile and validate missions, start a play test, read the log and
// take a screenshot (the tools: ModBuilderMcpTools). There is no AI in the game: the player's own assistant connects to it.
//   where     the builder's "AI tools" tab (ModBuilderScreen.BuildAiPanels): on / off (off by default, remembered), the port,
//             the token, the commands that connect Claude Code / Claude Desktop, what the AI did
//   when      while the Mod Builder is open (ModBuilder.Active; a play test too), on Windows (the game and the Editor)
//   transport MCP's Streamable HTTP: POST http://127.0.0.1:<port>/mcp, JSON-RPC 2.0, answered as application/json (no
//             server-sent events); a plain HTTP/1.1 server on a TcpListener like WebAdmin's (no HttpListener)
//   safety    only 127.0.0.1; every request needs "Authorization: Bearer <token>"; a browser's request (Origin) or a Host
//             other than this machine is refused (DNS rebinding); files only inside the project's folder; a removal asks the
//             player in the builder; every change is an undo step of the builder
// Tool calls run on the main thread (the pump, ModBuilderMcpPump) and may answer later (a reload, a play test's scene, a
// screenshot): the HTTP thread waits up to two minutes.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using GoF2Remake.Data;
using GoF2Remake.Modding;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static partial class ModBuilderMcp
    {
        public const int DefaultPort = 47630;
        const string PrefOn = "mb_mcp_on", PrefPort = "mb_mcp_port", PrefToken = "mb_mcp_token", PrefPermission = "mb_mcp_permission";
        /// <summary>How long a question waits for the player before it is a No.</summary>
        public const float AskSeconds = 300f;
        const int MaxBody = 96 * 1024 * 1024, MaxHeader = 16 * 1024, MaxConnections = 8;
        static readonly string[] Versions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        /// <summary>Only on Windows (the player and the Editor).</summary>
        public static bool Supported => Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>The player switched it on (the tab's toggle; off by default).</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefOn, 0) == 1;
            set { PlayerPrefs.SetInt(PrefOn, value ? 1 : 0); PlayerPrefs.Save(); Changed?.Invoke(); }
        }

        /// <summary>What the AI may do without the player's yes (the tab's Permissions row).</summary>
        public enum Permissions { ReadOnly, AskEveryChange, AskRemovals, AllowEverything }

        /// <summary>The player's choice (default: removals ask). Read on the main thread; the HTTP thread reads livePermission.</summary>
        public static Permissions Permission
        {
            get { int p = PlayerPrefs.GetInt(PrefPermission, (int)Permissions.AskRemovals); return p >= 0 && p <= 3 ? (Permissions)p : Permissions.AskRemovals; }
            set { PlayerPrefs.SetInt(PrefPermission, (int)value); PlayerPrefs.Save(); livePermission = (int)value; Changed?.Invoke(); }
        }

        static volatile int livePermission = (int)Permissions.AskRemovals;

        public static int Port
        {
            get { int p = PlayerPrefs.GetInt(PrefPort, DefaultPort); return p > 1023 && p < 65536 ? p : DefaultPort; }
            set { PlayerPrefs.SetInt(PrefPort, value); PlayerPrefs.Save(); Restart(); }
        }

        /// <summary>The secret a client sends (made once, kept; New token makes another).</summary>
        public static string Token
        {
            get
            {
                string t = PlayerPrefs.GetString(PrefToken, "");
                if (t.Length < 24) { t = NewTokenText(); PlayerPrefs.SetString(PrefToken, t); PlayerPrefs.Save(); }
                return t;
            }
        }

        public static void NewToken()
        {
            PlayerPrefs.SetString(PrefToken, NewTokenText());
            PlayerPrefs.Save();
            liveToken = Token;
            Changed?.Invoke();
        }

        /// <summary>The token and version as the HTTP thread reads them (set on the main thread: PlayerPrefs isn't thread-safe).</summary>
        static volatile string liveToken, liveVersion;

        static string NewTokenText()
        {
            var b = new byte[18];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Convert.ToBase64String(b).Replace('+', 'A').Replace('/', 'B').Replace("=", "");
        }

        public static string Url => $"http://127.0.0.1:{port}/mcp";
        public static bool Running => running;
        /// <summary>Why it isn't listening (a port in use...), else null.</summary>
        public static string Problem { get; private set; }
        /// <summary>The tab's state changed (on / off, a request, the token).</summary>
        public static event Action Changed;

        static volatile bool running;
        static TcpListener listener;
        static Thread thread;
        static int port, connections;
        static readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();

        // ---- what the AI did (the tab's list) ----------------------------------------------------------------------

        public struct Activity
        {
            public DateTime time;
            public string tool, summary;
            public bool error;
        }

        static readonly List<Activity> activity = new List<Activity>();
        public static IReadOnlyList<Activity> Recent { get { lock (activity) return activity.ToList(); } }
        public static string ClientName { get; private set; }
        public static DateTime LastRequest { get; private set; }

        static void Note(string tool, string summary, bool error)
        {
            lock (activity)
            {
                activity.Add(new Activity { time = DateTime.Now, tool = tool, summary = summary, error = error });
                if (activity.Count > 60) activity.RemoveRange(0, activity.Count - 60);
            }
            changedPending = true;
        }

        static volatile bool changedPending;

        // ---- the game's log (get_log) -----------------------------------------------------------------------------

        struct LogLine { public long seq; public string time, kind, text; }
        static readonly List<LogLine> log = new List<LogLine>();
        static long logSeq;

        static void OnLog(string message, string stackTrace, LogType type)
        {
            string kind = type == LogType.Log ? "info" : type == LogType.Warning ? "warning" : "error";
            if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace)) message += "\n" + stackTrace.Split('\n').FirstOrDefault();
            lock (log)
            {
                log.Add(new LogLine { seq = ++logSeq, time = DateTime.Now.ToString("HH:mm:ss"), kind = kind, text = liveToken != null ? message.Replace(liveToken, "<token>") : message });
                if (log.Count > 600) log.RemoveRange(0, log.Count - 600);
            }
        }

        // The HTTP thread never touches Unity's API (the localization table, Time...): its texts are plain English, and the
        // tab shows them as they are.

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Stop();
            Changed = null;
            lock (activity) activity.Clear();
            lock (log) log.Clear();
            ClientName = null;
        }

        // ---- on / off -----------------------------------------------------------------------------------------

        /// <summary>The pump (ModBuilderMcpPump) every frame: listening while the builder is open and it is switched on.</summary>
        internal static void Keep()
        {
            bool want = Supported && Enabled && ModBuilder.Active;
            if (want && !running && Problem == null) Start();
            else if (!want && running) Stop();
            if (!want) Problem = null;
        }

        static void Restart()
        {
            Stop();
            Problem = null;
            Changed?.Invoke();
        }

        static void Start()
        {
            port = Port;
            liveToken = Token;
            liveVersion = BuildVersion.Text;
            livePermission = (int)Permission;
            ModdingGuide.Preload();   // read_guide and the resources: the HTTP thread reads the texts
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
            }
            catch (Exception e)
            {
                listener = null;
                Problem = string.Format(Localization.Extra("mbMcpPortBusy", "Can't listen on port {0}: {1}"), port, e.Message);
                Debug.LogWarning("Mod Builder: MCP server: " + Problem);
                Changed?.Invoke();
                return;
            }
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            running = true;
            thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Mod Builder MCP" };
            thread.Start();
            Debug.Log($"Mod Builder: MCP server on {Url}");
            Changed?.Invoke();
        }

        public static void Stop()
        {
            McpInput.Uninstall();   // the AI's virtual keyboard and gamepad go with the server
            if (!running && listener == null) return;
            running = false;
            try { listener?.Stop(); } catch (Exception) { }
            listener = null;
            Application.logMessageReceivedThreaded -= OnLog;
            while (jobs.TryDequeue(out var j)) { j.Finish(Error(j.id, -32000, "The MCP server stopped.")); }
            Debug.Log("Mod Builder: MCP server stopped");
            Changed?.Invoke();
        }

        // ---- HTTP ----------------------------------------------------------------------------------------------

        sealed class Request
        {
            public string method, path, body;
            public readonly Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { if (!running) return; Thread.Sleep(50); continue; }
                if (Interlocked.Increment(ref connections) > MaxConnections)
                {
                    Interlocked.Decrement(ref connections);
                    try { client.Close(); } catch (Exception) { }
                    continue;
                }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Serve(client); }
                    catch (Exception) { }
                    finally
                    {
                        Interlocked.Decrement(ref connections);
                        try { client.Close(); } catch (Exception) { }
                    }
                });
            }
        }

        static void Serve(TcpClient client)
        {
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;
            var stream = client.GetStream();
            // Keep-alive: one connection may carry several requests.
            for (int n = 0; n < 100 && running; n++)
            {
                var r = Read(stream);
                if (r == null) return;
                var (status, body, extra) = Handle(r);
                Write(stream, status, body, extra);
                if (r.headers.TryGetValue("connection", out string c) && c.Equals("close", StringComparison.OrdinalIgnoreCase)) return;
            }
        }

        static Request Read(NetworkStream stream)
        {
            var head = new MemoryStream();
            uint last4 = 0;
            while (true)
            {
                if (head.Length >= MaxHeader) return null;
                int b;
                try { b = stream.ReadByte(); } catch (Exception) { return null; }
                if (b < 0) return null;
                head.WriteByte((byte)b);
                last4 = (last4 << 8) | (uint)b;
                if (last4 == 0x0D0A0D0Au) break;
            }
            var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length != 3) return null;
            var r = new Request { method = first[0].ToUpperInvariant() };
            int q = first[1].IndexOf('?');
            r.path = q < 0 ? first[1] : first[1].Substring(0, q);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) r.headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            if (r.headers.TryGetValue("content-length", out string lengthText))
            {
                if (!int.TryParse(lengthText, out int length) || length < 0 || length > MaxBody) return null;
                var body = new byte[length];
                int got = 0;
                while (got < length)
                {
                    int n = stream.Read(body, got, length - got);
                    if (n <= 0) return null;
                    got += n;
                }
                r.body = Encoding.UTF8.GetString(body);
            }
            return r;
        }

        static void Write(NetworkStream stream, int status, string body, string extraHeaders)
        {
            byte[] b = Encoding.UTF8.GetBytes(body ?? "");
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(status == 200 ? "OK" : status == 202 ? "Accepted" : status == 401 ? "Unauthorized"
                     : status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 405 ? "Method Not Allowed" : "Bad Request").Append("\r\n");
            if (b.Length > 0) sb.Append("Content-Type: application/json; charset=utf-8\r\n");
            sb.Append("Content-Length: ").Append(b.Length).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            if (!string.IsNullOrEmpty(extraHeaders)) sb.Append(extraHeaders);
            sb.Append("\r\n");
            byte[] h = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(h, 0, h.Length);
            stream.Write(b, 0, b.Length);
            stream.Flush();
        }

        static bool LocalHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return true;
            string h = host.StartsWith("[") ? host.Substring(0, host.IndexOf(']') + 1) : host.Split(':')[0];
            return h == "127.0.0.1" || h.Equals("localhost", StringComparison.OrdinalIgnoreCase) || h == "[::1]";
        }

        static bool SameToken(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        static (int, string, string) Handle(Request r)
        {
            if (r.path != "/mcp" && r.path != "/mcp/") return (404, "", null);
            // A web page must never reach the builder (DNS rebinding, a site posting to localhost).
            if (r.headers.TryGetValue("origin", out string origin) && !string.IsNullOrEmpty(origin) && origin != "null"
                && !(Uri.TryCreate(origin, UriKind.Absolute, out var o) && LocalHost(o.Host)))
                return (403, "", null);
            if (r.headers.TryGetValue("host", out string host) && !LocalHost(host)) return (403, "", null);
            string auth = r.headers.TryGetValue("authorization", out string a) && a.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? a.Substring(7).Trim() : null;
            if (!SameToken(auth, liveToken))
            {
                Note("-", "A request without the right token was refused.", true);
                return (401, JsonConvert.SerializeObject(new { error = "Missing or wrong token: send Authorization: Bearer <the token from the Mod Builder's AI tools tab>." }),
                        "WWW-Authenticate: Bearer\r\n");
            }
            if (r.method == "GET") return (405, "", "Allow: POST, DELETE\r\n");   // no server-sent event stream
            if (r.method == "DELETE") return (200, "", null);
            if (r.method != "POST") return (405, "", "Allow: POST, DELETE\r\n");
            LastRequest = DateTime.Now;
            JToken message;
            try { message = JToken.Parse(r.body ?? ""); }
            catch (Exception e) { return (400, Error(null, -32700, "Parse error: " + e.Message).ToString(Formatting.None), null); }
            string session = null;
            if (message is JArray batch)
            {
                var answers = new JArray();
                foreach (var m in batch.OfType<JObject>())
                {
                    var answer = Dispatch(m, ref session);
                    if (answer != null) answers.Add(answer);
                }
                return answers.Count == 0 ? (202, "", null) : (200, answers.ToString(Formatting.None), SessionHeader(session));
            }
            if (!(message is JObject single)) return (400, Error(null, -32600, "Invalid request").ToString(Formatting.None), null);
            var reply = Dispatch(single, ref session);
            return reply == null ? (202, "", null) : (200, reply.ToString(Formatting.None), SessionHeader(session));
        }

        static string SessionHeader(string session) => session != null ? $"Mcp-Session-Id: {session}\r\n" : null;

        // ---- JSON-RPC ----------------------------------------------------------------------------------------------

        static JObject Result(JToken id, JToken result) => new JObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };
        static JObject Error(JToken id, int code, string message) =>
            new JObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JObject { ["code"] = code, ["message"] = message } };

        /// <summary>One message: the answer, or null for a notification.</summary>
        static JObject Dispatch(JObject m, ref string session)
        {
            string method = (string)m["method"];
            var id = m["id"];
            bool notification = id == null;
            if (method == null) return null;   // a response from the client: nothing asked
            switch (method)
            {
                case "initialize":
                {
                    string asked = (string)m["params"]?["protocolVersion"];
                    string version = Versions.Contains(asked) ? asked : Versions[0];
                    var info = m["params"]?["clientInfo"];
                    ClientName = info != null ? $"{info["name"]} {info["version"]}".Trim() : "?";
                    session = Guid.NewGuid().ToString("N");
                    Note("initialize", string.Format("{0} connected.", ClientName), false);
                    return Result(id, new JObject
                    {
                        ["protocolVersion"] = version,
                        ["capabilities"] = new JObject { ["tools"] = new JObject { ["listChanged"] = false }, ["resources"] = new JObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JObject { ["name"] = "gof2-mod-builder", ["title"] = "Galaxy on Fire 2 Remake Mod Builder", ["version"] = liveVersion },
                        ["instructions"] = Instructions + PermissionNote((Permissions)livePermission),
                    });
                }
                case "ping":
                    return notification ? null : Result(id, new JObject());
                case "resources/list":
                    return Result(id, ModdingGuide.Loaded ? GuideResources() : new JObject { ["resources"] = new JArray() });
                case "resources/read":
                {
                    string uri = (string)m["params"]?["uri"];
                    var read = ModdingGuide.Loaded ? GuideResource(uri) : null;
                    return read != null ? Result(id, read) : Error(id, -32002, $"Resource not found: {uri}");
                }
                case "resources/templates/list":
                    return Result(id, new JObject { ["resourceTemplates"] = new JArray() });
                case "tools/list":
                    return Result(id, new JObject { ["tools"] = new JArray(Tools.Select(t => t.Describe())) });
                case "tools/call":
                {
                    string name = (string)m["params"]?["name"];
                    var args = m["params"]?["arguments"] as JObject ?? new JObject();
                    name = FromOldNames(name, args);
                    var tool = Tools.FirstOrDefault(t => t.name == name);
                    if (tool == null) return Error(id, -32602, $"Unknown tool: {name}");
                    var job = new Job { id = id, tool = tool, args = args };
                    jobs.Enqueue(job);
                    // a change may wait for the player's yes first
                    float wait = tool.timeoutSeconds + (tool.readOnly || tool.noAsk ? 0f : AskSeconds) + 5f;
                    if (!job.done.Wait(TimeSpan.FromSeconds(wait)))
                        return Result(id, ToolText("The game didn't answer in time (a scene still loading?). Ask builder_status.", true));
                    return job.response;
                }
                default:
                    if (notification) return null;   // notifications/initialized, notifications/cancelled...
                    return Error(id, -32601, $"Method not found: {method}");
            }
        }

        static JObject ToolText(string text, bool error) =>
            new JObject { ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }), ["isError"] = error };

        const string Instructions =
            "The live project of the Galaxy on Fire 2 Remake's Mod Builder (a mod in the game's normal format). Read the modding " +
            "guide first with read_guide (no arguments lists its files: SKILL.md the format and rules, then reference.md / missions.md / " +
            "mission.schema.json / voices.md / events.md / guide.md for numbers and formats, in parts: a section, a search; also the " +
            "resources gof2-guide://<file>). Find the game's, this project's and other mods' content " +
            "with search_content (use the values it returns); read entries with list_entries / get_entries, write them with " +
            "put_entry (the same JSON as the mod files: ships.json, items.json, characters.json, systems.json, stations.json, " +
            "interiors.json, races.json, blueprints.json, gameoptions.json, campaign.json, mod.json, events/<name>.gof2mission), files " +
            "(images, models, audio) with write_file. Check with validate (and compile_mission for a mission); try it with " +
            "play_test, read get_log and take a screenshot. Every change is an undo step in the builder (the undo tool takes the " +
            "last one back). To see your work: view_model draws any model (game, project ship with its hardpoints, station, .glb) " +
            "from any side, view_texture / list_textures show textures, select_entry and view put an entry on the builder's screen " +
            "and aim its camera (with a screenshot); hardpoint places hardpoints onto the hull from a side. Also test_fly, " +
            "weapon_range, engines, switch_hangar, and the projects (list_projects, create_project, open_project, import_mod, export_mod). " +
            "Audio: search_content kinds music / sound / voice give the game's track, sound and voice line names a file of the " +
            "same name replaces (voices per language in voices/<code>/); any other name is new. A story character is replaced " +
            "with replace_game_character; the game's .aem models convert on write_file; optimize shrinks big models and textures. " +
            "A station's star map planet is stations.json mapTexture (a 2:1 map wrapped round the sphere) / mapRing; a sky that " +
            "turns its top to each orbit's sun is skybox.sunAligned; a station's orbit level (station / jumpgate / arrival / asteroids / " +
            "fog / sun / planet) is stations.json orbit and its map planet's place mapPlace (SKILL.md); a mission's wormhole step opens a wormhole whose destination " +
            "may be a variable; play_test mode mission offers a bar mission in the Space Lounge to take as a player would. " +
            "Races of the mod's own (races.json: who owns systems, flies ships and raids, and who fights whom): list_races, " +
            "put_race, delete_race (with every reference to it), set_race (a system's owner / locals / raiders, a ship's race / " +
            "trafficRaces). ";

        /// <summary>The instructions' line on the permission mode (builder_status says the current one).</summary>
        static string PermissionNote(Permissions p) => p switch
        {
            Permissions.ReadOnly => "The player set the AI tools to read only: look things up and suggest changes; writing, undo and play tests are refused.",
            Permissions.AskEveryChange => "The player confirms every change in the game (a question waits up to 5 minutes): group your changes into few calls.",
            Permissions.AllowEverything => "The player allowed every change without asking, removals too: be careful with delete_entry.",
            _ => "Removing an entry asks the player in the game; other changes go through at once.",
        };

        /// <summary>A change of the AI's: done at once, asked first (Yes / No / Yes to everything), or refused, by the player's
        /// permission mode. 'removal' asks in Ask before removing too.</summary>
        internal static void Permit(Call c, string what, bool removal, Action go)
        {
            var mode = Permission;
            if (mode == Permissions.ReadOnly)
            {
                c.Fail("The player set the AI tools to Read only, so you may not " + what + ". Tell the player what you would change.");
                return;
            }
            if (mode == Permissions.AllowEverything || (mode == Permissions.AskRemovals && !removal)) { go(); return; }
            var screen = ModBuilderScreen.Open;
            if (screen == null || !screen.Alive)
            {
                c.Fail("The game can't ask the player now (the builder isn't on screen) whether you may " + what + ". Try again from the builder.");
                return;
            }
            bool? answer = null;
            Note(c.ToolName, "asks: " + Short(what), false);
            screen.ExternalAsk(Localization.Extra("mbAiAskTitle", "The AI asks"),
                string.Format(Localization.Extra("mbAiAskWants", "The AI wants to {0}. Allow it?"), what), yes => answer = yes,
                () => { Permission = Permissions.AllowEverything; answer = true; });
            c.Wait(() => answer != null, () =>
            {
                if (answer == true) go();
                else c.Fail("The player said no (or didn't answer): don't " + what + ".");
            }, AskSeconds, () => { if (screen.Alive) screen.ExternalCancelAsk(); });
        }

        // ---- jobs on the main thread ------------------------------------------------------------------------------

        internal sealed class Job
        {
            public JToken id;
            public Tool tool;
            public JObject args;
            public JObject response;
            public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
            public void Finish(JObject r) { if (response != null) return; response = r; done.Set(); }
        }

        /// <summary>A tool's call: answer now or later (Wait: when 'ready' holds, or after the tool's time).</summary>
        public sealed class Call
        {
            internal Job job;
            internal Func<bool> ready;
            internal Action then;
            internal float deadline;
            internal Action timedOut;
            public JObject Args => job.args;
            public string ToolName => job.tool.name;
            public bool Answered => job.response != null;

            public void Text(string text, bool error = false)
            {
                Note(job.tool.name, Short(text), error);
                job.Finish(Result(job.id, ToolText(text, error)));
            }

            public void Json(JToken value, string summary = null)
            {
                Note(job.tool.name, summary ?? job.tool.name, false);
                job.Finish(Result(job.id, new JObject { ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = value.ToString(Formatting.Indented) }), ["isError"] = false }));
            }

            public void Image(byte[] png, string caption)
            {
                Note(job.tool.name, caption, false);
                job.Finish(Result(job.id, new JObject
                {
                    ["content"] = new JArray(new JObject { ["type"] = "image", ["data"] = Convert.ToBase64String(png), ["mimeType"] = "image/png" },
                                             new JObject { ["type"] = "text", ["text"] = caption }),
                    ["isError"] = false,
                }));
            }

            public void Fail(string text) => Text(text, true);

            /// <summary>Goes on once 'condition' holds (checked every frame), else fails after the tool's time (or 'seconds';
            /// 'onTimeout' runs first then).</summary>
            public void Wait(Func<bool> condition, Action next, float seconds = 0f, Action onTimeout = null)
            {
                ready = condition;
                then = next;
                timedOut = onTimeout;
                deadline = Time.realtimeSinceStartup + (seconds > 0f ? seconds : job.tool.timeoutSeconds);
                lock (waiting) waiting.Add(this);
            }
        }

        static string Short(string s) { s = (s ?? "").Replace("\n", " "); return s.Length > 120 ? s.Substring(0, 117) + "…" : s; }

        static readonly List<Call> waiting = new List<Call>();

        /// <summary>ModBuilderMcpPump.Update: the waiting calls, then the new ones.</summary>
        internal static void Pump()
        {
            Keep();
            McpInput.Tick();
            List<Call> due;
            lock (waiting) due = waiting.ToList();
            foreach (var c in due)
            {
                if (c.Answered) { lock (waiting) waiting.Remove(c); continue; }
                bool ok;
                try { ok = c.ready(); } catch (Exception) { ok = false; }
                if (ok)
                {
                    lock (waiting) waiting.Remove(c);
                    try { c.then(); } catch (Exception e) { c.Fail("The game hit an error: " + e.Message); Debug.LogException(e); }
                }
                else if (Time.realtimeSinceStartup > c.deadline)
                {
                    lock (waiting) waiting.Remove(c);
                    try { c.timedOut?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                    c.Fail(Localization.Extra("mbMcpTimeout", "The game didn't answer in time (still loading?). Ask builder_status."));
                }
            }
            for (int n = 0; n < 16 && jobs.TryDequeue(out var job); n++)
            {
                var call = new Call { job = job };
                try
                {
                    var tool = job.tool;
                    // 'ask' answering null: this use changes nothing (hardpoint's list)
                    string what = tool.readOnly || tool.noAsk || tool.permitsItself ? null : tool.ask != null ? tool.ask(job.args) : "use \"" + tool.title + "\"";
                    if (what == null) tool.run(job.args, call);
                    else Permit(call, what, tool.destructive, () => tool.run(job.args, call));
                }
                catch (ArgumentException e) { call.Fail(e.Message); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    call.Fail("The game hit an error: " + e.Message);
                }
            }
            if (changedPending) { changedPending = false; Changed?.Invoke(); }
        }

        // ---- the tools' table ------------------------------------------------------------------------------------

        public sealed class Tool
        {
            public string name, title, description;
            public JObject input;
            public bool readOnly, destructive;
            /// <summary>Never asked or refused (it changes no content: the kind shown, ending a play test).</summary>
            public bool noAsk;
            /// <summary>Calls Permit itself, after its own checks (delete_entry).</summary>
            public bool permitsItself;
            /// <summary>What the question says (else the title).</summary>
            public Func<JObject, string> ask;
            public float timeoutSeconds = 30f;
            public Action<JObject, Call> run;

            public JObject Describe() => new JObject
            {
                ["name"] = name, ["title"] = title, ["description"] = description, ["inputSchema"] = input,
                ["annotations"] = new JObject { ["readOnlyHint"] = readOnly, ["destructiveHint"] = destructive, ["openWorldHint"] = false },
            };
        }

        /// <summary>An input schema: properties (name, JSON type or schema, description) and the required ones.</summary>
        static JObject Input(params (string name, object type, string description, bool required)[] props)
        {
            var p = new JObject();
            var req = new JArray();
            foreach (var (name, type, description, required) in props)
            {
                var s = type is JObject o ? (JObject)o.DeepClone() : new JObject { ["type"] = (string)type };
                s["description"] = description;
                p[name] = s;
                if (required) req.Add(name);
            }
            var schema = new JObject { ["type"] = "object", ["properties"] = p };
            if (req.Count > 0) schema["required"] = req;
            return schema;
        }

        static string Str(JObject a, string k) => a[k] is JValue v && v.Type != JTokenType.Null ? v.ToString() : null;

        /// <summary>The names from when the missions were called quests (a client's notes, an older guide): the tools
        /// compile_quest / get_quest_schema, the entry kind "quests", the argument "quest", play_test's mode and search_content's
        /// kind "quest". Returns the tool's name now; 'args' is changed in place.</summary>
        static string FromOldNames(string name, JObject args)
        {
            if (name == "compile_quest") name = "compile_mission";
            else if (name == "get_quest_schema") name = "get_mission_schema";
            if (Str(args, "kind") == "quests") args["kind"] = "missions";
            if (Str(args, "kind") == "quest" && name == "search_content") args["kind"] = "mission";
            if (Str(args, "mode") == "quest") args["mode"] = "mission";
            if (args["quest"] != null && args["mission"] == null) { args["mission"] = args["quest"]; args.Remove("quest"); }
            return name;
        }
        static int Int(JObject a, string k, int d) => a[k] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (int)v : int.TryParse(Str(a, k), out int i) ? i : d;
    }
}
