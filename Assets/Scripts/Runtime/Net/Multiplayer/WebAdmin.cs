// WebAdmin.cs
// Remake-only: a small web page for running a dedicated server (DedicatedServer started with -web): the server's status,
// the players (kick, bans, roles), the bans, the profiles, the factions, the settings, the console and the live log, in a
// browser. One page (Resources/GoF2Server/WebAdmin.html: Tailwind CSS 4's browser build from jsDelivr, the rest inline)
// and a JSON API, served by a minimal HTTP/1.1 server on a TcpListener (works in the IL2CPP player; no HttpListener).
//   -web [port]        on, default port DefaultPort (also -webport N)
//   -webbind ADDRESS   what it listens on, default 127.0.0.1 (this machine only: an SSH tunnel or a reverse proxy reaches
//                      it); 0.0.0.0 = every adapter. The page is plain HTTP: anything farther than the local network belongs
//                      behind a TLS reverse proxy. An address other than loopback also needs -webinsecure (the operator
//                      saying they know the login crosses that network unencrypted; Docker's image binds 0.0.0.0 inside the
//                      container and publishes the port on the host's 127.0.0.1); without it the web admin stays off.
// Logging in works like becoming an admin in the game (NetModeration):
//   the admin token (the one /claimadmin takes: -admintoken, GOF2_ADMIN_TOKEN or admin_token.txt, logged at every start):
//     the console's rights: every console command (DedicatedServer.Run), "stop" too;
//   a login code: an op, admin or master types /web in the game's chat and gets a one-time code (8 letters, 5 minutes)
//     bound to their profile; the page then acts with that profile's role (NetModeration.WebCommand: the moderation
//     commands only), checked again on every request (a demoted or banned pilot's session ends).
// A login gets a session cookie (HttpOnly, SameSite=Strict, 12 hours, 2 hours idle; kept in memory: a restart logs
// everyone out). Every POST needs the X-GoF2-Admin header (a cross-site form can't send it). Wrong logins: 5 per address,
// then that address waits 5 minutes. Every command run from the page is logged with who ran it and from where.
// The HTTP thread only parses and writes: every API request runs on the main thread (Pump, from DedicatedServer.Update).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Multiplayer
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class WebAdmin
    {
        public const int DefaultPort = 8080;
        const string Cookie = "gof2admin";
        const string PostHeader = "x-gof2-admin";
        const int MaxHeader = 16 * 1024, MaxBody = 32 * 1024, MaxConnections = 16, LogKeep = 1000;
        const float SessionSeconds = 12 * 3600f, IdleSeconds = 2 * 3600f, CodeSeconds = 300f;
        const int MaxFailures = 5;
        const float BlockSeconds = 300f;
        const string CodeLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // no 0 / O, 1 / I

        sealed class Request
        {
            public string method = "", path = "", query = "", ip = "", body = "";
            public readonly Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        sealed class Response
        {
            public int status = 200;
            public string type = "application/json; charset=utf-8", body = "";
            public readonly List<string> headers = new List<string>();
        }

        sealed class Job
        {
            public Request request;
            public Response response;
            public readonly ManualResetEventSlim done = new ManualResetEventSlim(false);
        }

        sealed class Session
        {
            public string id, account, name, ip;   // account null = the admin token
            public float created, seen;
        }

        // ---- the API's JSON --------------------------------------------------------------------------------------

        [Serializable] class LoginBody { public string token = "", code = ""; }
        [Serializable] class CommandBody { public string line = ""; }
        [Serializable] class Answer { public bool ok; public string text = "", name = ""; public int role; }

        [Serializable]
        class WebPlayer
        {
            public long client;
            public string name = "", tag = "", where = "", ship = "";
            public int squad, role;
            public bool observer, admin;
        }

        [Serializable]
        class WebState
        {
            public string me = "", roleName = "";
            public int role;                  // NetModeration's; 4 = the console (the admin token)
            public bool console;
            public string serverName = "", version = "", uptime = "", joinCode = "", arenas = "";
            public int port, online, maxPlayers;
            public bool running, profiles, password, debug, freePvp;
            public List<WebPlayer> players = new List<WebPlayer>();
            public NetPanel.State admin = new NetPanel.State();
        }

        [Serializable] class LogLine { public long seq; public string time = "", kind = "", text = ""; }
        [Serializable] class LogPage { public long last; public List<LogLine> lines = new List<LogLine>(); }

        // ---- state ------------------------------------------------------------------------------------------

        static TcpListener listener;
        static Thread thread;
        static volatile bool running;
        static int connections;
        static string page;
        static string bind = "";
        static int port;
        static readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();
        static readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>();
        static readonly Dictionary<string, (string account, string name, float until)> codes = new Dictionary<string, (string, string, float)>();
        static readonly Dictionary<string, (int count, float until)> failures = new Dictionary<string, (int, float)>();
        static readonly List<LogLine> log = new List<LogLine>();
        static long logSeq;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Stop();
            while (jobs.TryDequeue(out _)) { }
            sessions.Clear(); codes.Clear(); failures.Clear();
            lock (log) { log.Clear(); logSeq = 0; }
            page = null;
        }

        /// <summary>The web admin is listening.</summary>
        public static bool Running => running;

        /// <summary>DedicatedServer.Start: on with -web / -webport.</summary>
        public static void StartFromCommandLine()
        {
            string web = NetGame.CommandLineValue("-web");
            bool on = Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-web", StringComparison.OrdinalIgnoreCase));
            string portText = NetGame.CommandLineValue("-webport") ?? web;
            if (!on && NetGame.CommandLineValue("-webport") == null) return;
            int p = int.TryParse(portText, out int n) && n > 0 && n < 65536 ? n : DefaultPort;
            Start(p, NetGame.CommandLineValue("-webbind") ?? "127.0.0.1");
        }

        public static void Start(int listenPort, string address)
        {
            if (running) return;
            var html = Resources.Load<TextAsset>("GoF2Server/WebAdmin");
            if (html == null) { Debug.LogError("Server: the web admin's page (Resources/GoF2Server/WebAdmin.html) is missing."); return; }
            page = html.text;
            if (!IPAddress.TryParse(address, out var ip)) { Debug.LogError($"Server: -webbind {address} isn't an IP address."); return; }
            // Remake security (Spectre's review, 2026-11-16): a warning didn't stop the admin token crossing a network as plain
            // HTTP by a typo or a copied command line; a non-loopback address needs the operator's -webinsecure.
            if (!IPAddress.IsLoopback(ip) && !Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-webinsecure", StringComparison.OrdinalIgnoreCase)))
            {
                Debug.LogError($"Server: the web admin is plain HTTP and stays off on {address}: bind it to 127.0.0.1 (an SSH tunnel or " +
                               "a TLS reverse proxy reaches it), or add -webinsecure if this network is trusted.");
                return;
            }
            try
            {
                listener = new TcpListener(ip, listenPort);
                listener.Start();
            }
            catch (Exception e)
            {
                Debug.LogError($"Server: the web admin can't listen on {address}:{listenPort} ({e.Message}).");
                listener = null;
                return;
            }
            NetModeration.EnsureToken();
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            Application.quitting -= Stop;
            Application.quitting += Stop;
            bind = address;
            port = listenPort;
            running = true;
            thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Web admin" };
            thread.Start();
            string where = ip.Equals(IPAddress.Any) ? $"http://<this machine's address>:{listenPort}/" : $"http://{address}:{listenPort}/";
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "Server: {0}", $"Web admin on {where} (log in with the admin token, or a code from /web in the game).");
            if (!IPAddress.IsLoopback(ip))
                Debug.LogWarning("Server: the web admin is plain HTTP on the network: reach it over a TLS reverse proxy or a VPN, not the open internet.");
        }

        public static void Stop()
        {
            if (!running && listener == null) return;
            running = false;
            try { listener?.Stop(); } catch (Exception) { }
            listener = null;
            Application.logMessageReceivedThreaded -= OnLog;
            while (jobs.TryDequeue(out var j)) j.done.Set();
        }

        // ---- the log ----------------------------------------------------------------------------------------

        static void OnLog(string message, string stackTrace, LogType type)
        {
            string kind = type == LogType.Log ? "info" : type == LogType.Warning ? "warning" : "error";
            lock (log)
            {
                // The admin token never reaches the web log: NetModeration logs it at every start (and after a reconnect, once
                // this handler is on), and /api/log is open to the Admin role, which could have read it and claimed master.
                log.Add(new LogLine { seq = ++logSeq, time = DateTime.Now.ToString("HH:mm:ss"), kind = kind, text = NetModeration.Redact(message) });
                if (log.Count > LogKeep) log.RemoveRange(0, log.Count - LogKeep);
            }
        }

        // ---- the HTTP thread -----------------------------------------------------------------------------------

        static void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { if (!running) return; Thread.Sleep(100); continue; }
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
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            var stream = client.GetStream();
            var request = Read(stream, client);
            Response response;
            if (request == null) response = Plain(400, "Bad request");
            else if (request.method == "GET" && (request.path == "/" || request.path == "/index.html")) response = Page();
            else if (request.path == "/favicon.ico") response = new Response { status = 204, type = "text/plain" };
            else if (request.path.StartsWith("/api/"))
            {
                var job = new Job { request = request };
                jobs.Enqueue(job);
                response = job.done.Wait(10000) && job.response != null ? job.response : Plain(503, "The server is busy.");
            }
            else response = Plain(404, "Not found");
            Write(stream, response);
        }

        static Request Read(NetworkStream stream, TcpClient client)
        {
            // The head: up to the blank line (CR LF CR LF).
            var head = new MemoryStream();
            uint last4 = 0;
            while (true)
            {
                if (head.Length >= MaxHeader) return null;
                int b = stream.ReadByte();
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
            r.query = q < 0 ? "" : first[1].Substring(q + 1);
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0) r.headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            r.ip = client.Client.RemoteEndPoint is IPEndPoint ep ? ep.Address.ToString() : "?";
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

        static void Write(NetworkStream stream, Response r)
        {
            byte[] body = Encoding.UTF8.GetBytes(r.body ?? "");
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(r.status).Append(' ').Append(Reason(r.status)).Append("\r\n");
            sb.Append("Content-Type: ").Append(r.type).Append("\r\n");
            sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            sb.Append("Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\n");
            sb.Append("Referrer-Policy: no-referrer\r\nConnection: close\r\n");
            foreach (var h in r.headers) sb.Append(h).Append("\r\n");
            sb.Append("\r\n");
            byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        static string Reason(int status) =>
            status == 200 ? "OK" : status == 204 ? "No Content" : status == 400 ? "Bad Request" : status == 401 ? "Unauthorized" :
            status == 403 ? "Forbidden" : status == 404 ? "Not Found" : status == 405 ? "Method Not Allowed" : status == 429 ? "Too Many Requests" :
            status == 500 ? "Internal Server Error" : "Service Unavailable";

        static Response Plain(int status, string text) => new Response { status = status, type = "text/plain; charset=utf-8", body = text };

        /// <summary>The page, with a fresh nonce for its own scripts (the Content-Security-Policy allows only those and the
        /// Tailwind build).</summary>
        static Response Page()
        {
            string nonce = Convert.ToBase64String(RandomBytes(16));
            var r = new Response { type = "text/html; charset=utf-8", body = page.Replace("{{NONCE}}", nonce) };
            r.headers.Add($"Content-Security-Policy: default-src 'none'; script-src 'nonce-{nonce}' https://cdn.jsdelivr.net; " +
                          "style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'");
            return r;
        }

        // ---- the main thread ---------------------------------------------------------------------------------

        /// <summary>DedicatedServer.Update: the waiting API requests.</summary>
        public static void Pump()
        {
            for (int n = 0; n < 32 && jobs.TryDequeue(out var job); n++)
            {
                try { job.response = Handle(job.request); }
                catch (Exception e)
                {
                    Debug.LogError($"Server: web admin request {job.request.path} failed: {e.Message}");
                    job.response = Json(500, new Answer { text = "The server hit an error (see its log)." });
                }
                job.done.Set();
            }
        }

        static float Now => Time.realtimeSinceStartup;

        static Response Handle(Request r)
        {
            if (r.method == "POST" && !r.headers.ContainsKey(PostHeader)) return Json(403, new Answer { text = "Missing the page's header." });
            switch (r.path)
            {
                case "/api/login": return r.method == "POST" ? Login(r) : Plain(405, "POST");
                case "/api/logout":
                {
                    var old = SessionOf(r, false);
                    if (old != null) sessions.Remove(old.id);
                    var res = Json(200, new Answer { ok = true });
                    res.headers.Add($"Set-Cookie: {Cookie}=; Path=/; HttpOnly; SameSite=Strict; Max-Age=0");
                    return res;
                }
            }
            var s = SessionOf(r, true);
            if (s == null) return Json(401, new Answer { text = "Log in first." });
            int role = RoleOf(s);
            switch (r.path)
            {
                case "/api/state": return Json(200, State(s, role));
                case "/api/command":
                    if (r.method != "POST") return Plain(405, "POST");
                    return Command(s, role, Parse<CommandBody>(r.body)?.line ?? "", r.ip);
                case "/api/log":
                    if (role < NetModeration.Admin) return Json(403, new Answer { text = "Admins only." });
                    return Json(200, Log(QueryLong(r.query, "after")));
                default: return Json(404, new Answer { text = "Not found." });
            }
        }

        static T Parse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); } catch (Exception) { return null; }
        }

        static Response Json(int status, object value) => new Response { status = status, body = JsonUtility.ToJson(value) };

        static long QueryLong(string query, string key)
        {
            foreach (var part in query.Split('&'))
                if (part.StartsWith(key + "=") && long.TryParse(part.Substring(key.Length + 1), out long v)) return v;
            return 0;
        }

        // ---- logging in ----------------------------------------------------------------------------------------

        static Response Login(Request r)
        {
            if (failures.TryGetValue(r.ip, out var f) && f.count >= MaxFailures)
            {
                if (Now < f.until) return Json(429, new Answer { text = $"Too many wrong tries: wait {Mathf.CeilToInt((f.until - Now) / 60f)} min." });
                failures.Remove(r.ip);
            }
            var body = Parse<LoginBody>(r.body) ?? new LoginBody();
            string token = (body.token ?? "").Trim(), code = (body.code ?? "").Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");
            Session s = null;
            if (token.Length > 0 && NetModeration.TokenMatches(token))
                s = new Session { name = "Admin token" };
            else if (code.Length > 0 && codes.TryGetValue(code, out var c))
            {
                codes.Remove(code);   // once
                if (Now <= c.until && NetProfiles.RoleOf(c.account) >= NetModeration.Op && NetModeration.BanReason(c.account, null) == null)
                    s = new Session { account = c.account, name = c.name };
            }
            if (s == null)
            {
                int count = failures.TryGetValue(r.ip, out var had) ? had.count + 1 : 1;
                failures[r.ip] = (count, Now + BlockSeconds);
                Debug.LogWarning($"Server: a wrong web admin login from {r.ip} ({count} / {MaxFailures}).");
                return Json(401, new Answer { text = token.Length > 0 ? "That isn't this server's admin token." : "That code isn't valid (they work once, for 5 minutes)." });
            }
            failures.Remove(r.ip);
            s.id = Convert.ToBase64String(RandomBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            s.ip = r.ip;
            s.created = s.seen = Now;
            sessions[s.id] = s;
            int role = RoleOf(s);
            Debug.Log($"Server: {s.name} logged in to the web admin from {r.ip} ({RoleName(role)}).");
            var res = Json(200, new Answer { ok = true, name = s.name, role = role });
            res.headers.Add($"Set-Cookie: {Cookie}={s.id}; Path=/; HttpOnly; SameSite=Strict; Max-Age={(int)SessionSeconds}");
            return res;
        }

        static Session SessionOf(Request r, bool touch)
        {
            if (!r.headers.TryGetValue("cookie", out string cookies)) return null;
            foreach (var part in cookies.Split(';'))
            {
                string kv = part.Trim();
                if (!kv.StartsWith(Cookie + "=")) continue;
                string id = kv.Substring(Cookie.Length + 1);
                if (!sessions.TryGetValue(id, out var s)) return null;
                if (Now - s.created > SessionSeconds || Now - s.seen > IdleSeconds || RoleOf(s) < NetModeration.Op)
                {
                    sessions.Remove(id);
                    return null;
                }
                if (touch) s.seen = Now;
                return s;
            }
            return null;
        }

        /// <summary>The session's role now: the console's for the admin token, else its profile's (0 when demoted or
        /// banned since: the session ends).</summary>
        static int RoleOf(Session s)
        {
            if (s.account == null) return NetModeration.ServerConsole;
            if (!NetProfiles.Enabled || NetModeration.BanReason(s.account, null) != null) return NetModeration.Player;
            return NetProfiles.RoleOf(s.account);
        }

        static string RoleName(int role) => role >= NetModeration.ServerConsole ? "console" : NetModeration.RoleName(role);

        /// <summary>NetCommands' /web: a one-time login code for this pilot's profile (ops and up).</summary>
        internal static string CodeFor(NetPlayer by)
        {
            if (!running) return Localization.Extra("mpWebOff", "This server has no web admin (it starts with -web).");
            string account = by != null ? NetProfiles.AccountOf(by.OwnerClientId) : null;
            if (account == null || NetProfiles.RoleOf(account) < NetModeration.Op)
                return Localization.Extra("mpWebNoRole", "Only the server's ops, admins and masters can use the web admin.");
            var stale = new List<string>();
            foreach (var pair in codes) if (pair.Value.account == account || pair.Value.until < Now) stale.Add(pair.Key);
            foreach (string k in stale) codes.Remove(k);
            var bytes = RandomBytes(8);
            var sb = new StringBuilder();
            foreach (byte b in bytes) sb.Append(CodeLetters[b % CodeLetters.Length]);   // 32 letters: no bias
            string code = sb.ToString();
            codes[code] = (account, by.DisplayName, Now + CodeSeconds);
            Debug.Log($"Server: {by.DisplayName} asked for a web admin login code.");
            return string.Format(Localization.Extra("mpWebCode", "Web admin login code: {0} (once, within 5 minutes; port {1}). Don't share it."), code, port);
        }

        static byte[] RandomBytes(int n)
        {
            var b = new byte[n];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return b;
        }

        // ---- the API -------------------------------------------------------------------------------------------

        static WebState State(Session s, int role)
        {
            var st = new WebState
            {
                me = s.name, role = role, roleName = RoleName(role), console = role >= NetModeration.ServerConsole,
                serverName = DedicatedServer.ListName, version = Application.version, uptime = DedicatedServer.Uptime,
                joinCode = NetGame.JoinCode ?? "", port = DedicatedServer.Port, online = NetGame.ClientIds.Count, maxPlayers = NetGame.MaxPlayers,
                running = NetGame.Active, profiles = NetProfiles.Enabled, password = NetGame.HasPassword, debug = NetGame.HostAllowsDebug,
                freePvp = NetGame.FreePvp, arenas = NetArena.ConsoleList(),
            };
            foreach (var p in NetPlayer.All)
            {
                if (p == null || !p.IsSpawned) continue;
                st.players.Add(new WebPlayer
                {
                    client = (long)p.OwnerClientId, name = p.DisplayName, tag = p.FactionTag, where = DedicatedServer.Where(p),
                    ship = UI.ItemInfo.ShipName(p.ShipIndex), squad = p.SquadId, role = p.StaffRole, observer = p.Observer,
                    admin = NetCommands.IsAdmin(p),
                });
            }
            st.admin.role = role;
            NetModeration.FillFor(st.admin);
            st.admin.dockedStation = -1;
            if (NetProfiles.Enabled) NetFactions.FillPanel(ulong.MaxValue, st.admin);   // every faction and the sieges (no player's own part)
            return st;
        }

        static Response Command(Session s, int role, string line, string ip)
        {
            line = (line ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (line.StartsWith("/")) line = line.Substring(1);
            if (line.Length == 0) return Json(400, new Answer { text = "Type a command." });
            if (line.Length > 500) return Json(400, new Answer { text = "That command is too long." });
            string cmd = line.Split(' ')[0].ToLowerInvariant();
            string shown = cmd == "set" && line.Split(' ').Length > 1 && line.Split(' ')[1].Equals("password", StringComparison.OrdinalIgnoreCase)
                ? "set password (hidden)" : line;
            Debug.Log($"Server: web admin {s.name} ({ip}): {shown}");
            string answer = role >= NetModeration.ServerConsole
                ? DedicatedServer.Run(line)
                : NetModeration.WebCommand(line, role, s.name) ?? "Only the moderation commands work with a login code (the admin token gives the whole console).";
            return Json(200, new Answer { ok = true, text = answer ?? "" });
        }

        static LogPage Log(long after)
        {
            var pageOut = new LogPage();
            lock (log)
            {
                pageOut.last = logSeq;
                foreach (var l in log) if (l.seq > after) pageOut.lines.Add(l);
            }
            if (pageOut.lines.Count > 500) pageOut.lines.RemoveRange(0, pageOut.lines.Count - 500);
            return pageOut;
        }
    }
}
