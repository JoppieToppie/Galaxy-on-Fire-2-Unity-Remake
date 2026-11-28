// A developer's line into a running game (the user, 2026-11-27: "a way you can communicate easier with the game, so you
// dont have to use screenshots"): started only by the launch argument "-remote <token>" (Android: adb shell am start ...
// -e unity "-remote <token>"), a plain HTTP server on 127.0.0.1 (port 47631; from a desktop: adb forward tcp:47631
// tcp:47631), every request carrying the token (?token= or a Bearer header). Plain-text GET commands:
//   /status                     scene, frame rate, frame time, memory, version
//   /buttons                    the texts of the buttons on screen, topmost panel first (McpInput.Buttons)
//   /click?text=Options&index=0 presses one, as a controller's submit (McpInput.Click)
//   /get?key=skyStyle           an option as stored (Settings.Raw; "default" when never set)
//   /set?key=skyStyle&value=2   stores it (Settings.SetRaw)
//   /log?n=100                  the last lines of the game's log (errors with three lines of their stack)
// The HTTP thread never touches the Unity API: each command runs on the main thread (Update) and the thread waits for it.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GoF2Remake.UI
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public class DevRemote : MonoBehaviour
    {
        public const int Port = 47631;
        const int LogLines = 500;

        static string token;
        static TcpListener listener;
        static volatile bool running;
        static readonly ConcurrentQueue<(Func<string> run, Action<string> done)> queue = new();
        static readonly Queue<string> log = new();
        float fps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (running) return;
            // (Android may hand the whole "-e unity" string over as one argument)
            var args = string.Join(" ", Environment.GetCommandLineArgs()).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int i = Array.FindIndex(args, a => string.Equals(a, "-remote", StringComparison.OrdinalIgnoreCase));
            if (i < 0 || i + 1 >= args.Length || args[i + 1].Length < 4) return;   // (no token, no server)
            token = args[i + 1];
            var go = new GameObject("DevRemote");
            DontDestroyOnLoad(go);
            go.AddComponent<DevRemote>();
        }

        void Start()
        {
            Application.logMessageReceivedThreaded += OnLog;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
            }
            catch (Exception e) { Debug.LogWarning("DevRemote: port " + Port + ": " + e.Message); return; }
            running = true;
            new Thread(Accept) { IsBackground = true, Name = "DevRemote" }.Start();
            Debug.Log("DevRemote: listening on 127.0.0.1:" + Port);
        }

        void OnDestroy()
        {
            running = false;
            Application.logMessageReceivedThreaded -= OnLog;
            try { listener?.Stop(); } catch (Exception) { }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) fps = fps <= 0f ? 1f / dt : Mathf.Lerp(fps, 1f / dt, 0.05f);
            while (queue.TryDequeue(out var job))
            {
                string answer;
                try { answer = job.run(); } catch (Exception e) { answer = "error: " + e.Message; }
                job.done(answer);
            }
        }

        static void OnLog(string message, string stack, LogType type)
        {
            string line = $"{DateTime.Now:HH:mm:ss} {type}: {message}";
            if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stack))
                line += "\n  " + string.Join("\n  ", stack.Split('\n'), 0, Math.Min(3, stack.Split('\n').Length));
            lock (log)
            {
                log.Enqueue(line);
                while (log.Count > LogLines) log.Dequeue();
            }
        }

        static void Accept()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch (Exception) { return; }
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Serve(client); } catch (Exception) { }
                    finally { try { client.Close(); } catch (Exception) { } }
                });
            }
        }

        static void Serve(TcpClient client)
        {
            client.ReceiveTimeout = client.SendTimeout = 10000;
            var stream = client.GetStream();
            var head = new StringBuilder();
            while (head.Length < 8192)
            {
                int b = stream.ReadByte();
                if (b < 0) return;
                head.Append((char)b);
                if (head.Length >= 4 && head.ToString(head.Length - 4, 4) == "\r\n\r\n") break;
            }
            var lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length != 3) return;
            int q = first[1].IndexOf('?');
            string path = q < 0 ? first[1] : first[1].Substring(0, q);
            var query = Query(q < 0 ? "" : first[1].Substring(q + 1));
            string auth = null;
            foreach (var l in lines)
                if (l.StartsWith("Authorization: Bearer ", StringComparison.OrdinalIgnoreCase)) auth = l.Substring(22).Trim();
            query.TryGetValue("token", out string given);
            int status = 200;
            string body;
            if (!Same(auth ?? given, token)) { status = 401; body = "token?"; }
            else body = Handle(path, query);
            byte[] bytes = Encoding.UTF8.GetBytes(body + "\n");
            byte[] h = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {(status == 200 ? "OK" : "Unauthorized")}\r\nContent-Type: text/plain; charset=utf-8\r\n" +
                                              $"Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            stream.Write(h, 0, h.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        static Dictionary<string, string> Query(string s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in s.Split('&'))
            {
                if (part.Length == 0) continue;
                int e = part.IndexOf('=');
                d[Uri.UnescapeDataString((e < 0 ? part : part.Substring(0, e)).Replace('+', ' '))] =
                    e < 0 ? "" : Uri.UnescapeDataString(part.Substring(e + 1).Replace('+', ' '));
            }
            return d;
        }

        static bool Same(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        static string Handle(string path, Dictionary<string, string> q)
        {
            string Arg(string k) => q.TryGetValue(k, out var v) ? v : null;
            switch (path)
            {
                case "/log":
                    int n = int.TryParse(Arg("n"), out int nn) ? Math.Clamp(nn, 1, LogLines) : 100;
                    lock (log) { var all = log.ToArray(); return string.Join("\n", all, Math.Max(0, all.Length - n), Math.Min(n, all.Length)); }
                case "/status": return OnMain(() =>
                {
                    var self = FindAnyObjectByType<DevRemote>();
                    return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}\nfps {self.fps:0.0} ({1000f / Mathf.Max(self.fps, 0.01f):0.0} ms)\n" +
                           $"screen {Screen.width}x{Screen.height} @ {Screen.currentResolution.refreshRateRatio.value:0} Hz, targetFrameRate {Application.targetFrameRate}, vSync {QualitySettings.vSyncCount}\n" +
                           $"memory {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB allocated, GC {GC.GetTotalMemory(false) / 1048576} MB\n" +
                           $"version {Application.version}, {Application.platform}, {SystemInfo.deviceModel}, {SystemInfo.graphicsDeviceName}");
                });
                case "/buttons": return OnMain(() => string.Join("\n", McpInput.Buttons().ConvertAll(b => b.text)));
                case "/click": return OnMain(() =>
                {
                    string why = McpInput.Click(Arg("text") ?? "", int.TryParse(Arg("index"), out int i) ? i : 0, out string pressed);
                    return why ?? "pressed " + pressed;
                });
                case "/get": return OnMain(() => { float v = Data.Settings.Raw(Arg("key") ?? ""); return float.IsNaN(v) ? "default" : v.ToString(System.Globalization.CultureInfo.InvariantCulture); });
                case "/set": return OnMain(() =>
                {
                    if (!float.TryParse(Arg("value"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) || string.IsNullOrEmpty(Arg("key")))
                        return "usage: /set?key=<option key>&value=<number>";
                    Data.Settings.SetRaw(Arg("key"), v);
                    return $"{Arg("key")} = {v}";
                });
                default: return "commands: /status /buttons /click?text=&index= /get?key= /set?key=&value= /log?n=";
            }
        }

        /// <summary>Runs 'f' on the main thread and waits for its answer (5 s).</summary>
        static string OnMain(Func<string> f)
        {
            string answer = null;
            var done = new ManualResetEventSlim();   // (not disposed: a late answer still sets it)
            queue.Enqueue((f, a => { answer = a; done.Set(); }));
            return done.Wait(5000) ? answer : "timed out (the game's main thread is busy or paused)";
        }
    }
}
