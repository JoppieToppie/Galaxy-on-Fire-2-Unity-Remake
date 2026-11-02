// LiveSplitLink.cs
// Remake-only (the speedrun timer, docs/notes/speedrun.md): drives a LiveSplit on this computer through its TCP server
// (LiveSplit 1.8+: Control > Start TCP Server, port 16834; older ones the LiveSplit Server component), so a player who
// times with LiveSplit (its layouts, splits.io, speedrun.com's submission) gets the start, splits, skips and resets from
// the game without an auto splitter. The protocol is lines of text: "starttimer", "split", "skipsplit", "reset",
// "initgametime", "setgametime 1:02:03.45" (LiveSplit's game time = the remake's time without loading). Only while
// Options > Gameplay "Connect to LiveSplit" is on (Settings.SpeedrunLiveSplit). A background thread connects and writes,
// so the game never waits on the socket; commands sent while no LiveSplit listens are dropped (a timer that is not
// running would take them later and out of time), and it tries again every few seconds.

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class LiveSplitLink
    {
        public const int Port = 16834;
        const int RetryMs = 3000, ConnectTimeoutMs = 1000;

        static readonly ConcurrentQueue<string> queue = new ConcurrentQueue<string>();
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static volatile bool running, connected;
        static int generation;   // each thread's own: one stopped and begun again leaves the old one to end alone

        /// <summary>A LiveSplit is listening and takes the commands.</summary>
        public static bool Connected => connected && running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Stop();
            Application.quitting -= Stop;
            Application.quitting += Stop;
        }

        /// <summary>Sends a command line when the option is on (connecting first if need be).</summary>
        public static void Send(string command)
        {
            if (!Settings.SpeedrunLiveSplit || string.IsNullOrEmpty(command)) return;
            Begin();
            if (!connected) return;
            queue.Enqueue(command);
            wake.Set();
        }

        /// <summary>LiveSplit's game time text ("h:mm:ss.ff").</summary>
        public static string GameTime(double ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"h\:mm\:ss\.ff", CultureInfo.InvariantCulture);

        /// <summary>Starts the connecting thread (the option's toggle, the first command).</summary>
        public static void Begin()
        {
            if (running || Application.isMobilePlatform) return;
            running = true;
            int gen = Interlocked.Increment(ref generation);
            new Thread(() => Loop(gen)) { IsBackground = true, Name = "LiveSplitLink" }.Start();
        }

        public static void Stop()
        {
            if (!running) return;
            running = false;
            connected = false;
            Interlocked.Increment(ref generation);
            wake.Set();
            while (queue.TryDequeue(out _)) { }
        }

        static void Loop(int gen)
        {
            bool Mine() => running && gen == Volatile.Read(ref generation);
            while (Mine())
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient { NoDelay = true };
                    var connecting = client.ConnectAsync("127.0.0.1", Port);
                    if (!connecting.Wait(ConnectTimeoutMs) || !client.Connected) throw new SocketException();
                    var stream = client.GetStream();
                    while (queue.TryDequeue(out _)) { }   // nothing queued before it listened
                    connected = true;
                    while (Mine())
                    {
                        wake.WaitOne(1000);
                        while (Mine() && queue.TryDequeue(out string line))
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(line + "\r\n");
                            stream.Write(bytes, 0, bytes.Length);
                        }
                        // LiveSplit closing its server: a read of 0 bytes (it never sends unasked).
                        if (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0) break;
                    }
                }
                catch (Exception) { }   // no LiveSplit listening, or it went away
                finally
                {
                    if (gen == Volatile.Read(ref generation)) connected = false;
                    try { client?.Close(); } catch (Exception) { }
                }
                if (Mine()) wake.WaitOne(RetryMs);
            }
        }
    }
}
