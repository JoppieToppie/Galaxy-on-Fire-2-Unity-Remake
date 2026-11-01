// Radio.cs
// A campaign level's radio messages (Radio / RadioMessage, Reference/research/dialogue_cutscenes.md 2): the lines of
// Level::createRadioMessages for the index (StoryStep.radio), each shown once when its trigger holds.
//   RadioMessage::triggered 0x17c5d8    tested in array order every frame and latched once true; the original shows a newly
//                                       triggered line at once over the one showing, the remake queues it (a voiced line
//                                       isn't cut off): #132, a trigger true for one frame (a level script's event) while
//                                       another line showed was lost when the remake tested only with none showing
//   Radio::update / draw 0x180380       nothing for 2000 ms, then the box for lines * 2000 + 1500 ms; the voice plays when
//                                       it appears; "over" when the display time has passed
// Trigger types implemented (the rest never fire yet): 0 route waypoint passed, 1 any listed ship dead, 2 any listed
// friendly ship dead, 3 / 4 no enemies / friends left, 5 time, 6 chained on a line, 8 any listed ship active, 9 all listed
// dead, 10 any listed friendly ship active, 0xc / 0x13 / 0x1f listed ship below 1/2, 1/4, 3/4 hull, 0xf any ship dead,
// 0x10 a hostile ship active, 0x14 >= n ships dead, 0x15 ship[p] EMP-disabled, 0x16 crate cargo captured, 0x17 station
// locked, 0x18 ship[p] inactive but alive after 59 999 ms, 0x19 the route past waypoint 0 with >= p ships alive, 0x1a
// ship 0 active within 5000 of z = p, 0x1b level-script event, 0x1c player armor gone, 0x1e dead count among ships 2-5.
// Lines are counted for the duration from the text length (the original counts wrapped lines of 670 px; the alien font's
// glyphs are about twice as wide, AlienText).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using UnityEngine;

namespace GoF2Remake.Flight
{
    /// <summary>What the radio triggers read from the level (Level / LevelScript / Radar state).</summary>
    public interface IRadioWorld
    {
        float MissionMs { get; }           // LevelScript+8: the mission clock (reset by the briefing)
        int ScriptEvent { get; }           // LevelScript+0x1c
        int ShipCount { get; }
        bool ShipDead(int i);
        bool ShipActive(int i);            // awake, alive
        float ShipHullFraction(int i);
        bool ShipHostile(int i);
        int RouteIndex { get; }            // the player route's current waypoint
        int CrateCargoCaptured { get; }    // Level+0x1c
        bool StationLocked { get; }
        bool PlayerArmorGone { get; }
        int EnemiesLeft { get; }
        int FriendsLeft { get; }
        bool ShipFriendly(int i);          // Player+0x5d
        bool ShipEmpDisabled(int i);       // KIPlayer+0x20
        bool ShipInactive(int i);          // Player::isActive false (hidden / parked by its script), not dead
        float ShipGameZ(int i);            // game-space z of the ship
    }

    public class Radio
    {
        const float DelayMs = 2000f, LineMs = 2000f, ExtraMs = 1500f, CharsPerLine = 55f, AlienCharsPerLine = 30f;

        readonly List<RadioLine> lines;
        readonly bool[] triggered, over;
        /// <summary>RadioMessage+0x24: the route index this line saw last (types 0 / 0x19 fire on the frame it moves on).</summary>
        readonly int[] lastRoute;
        readonly bool[] routeEdge;
        /// <summary>Lines whose trigger fired while another line showed, in the order they fired.</summary>
        readonly Queue<int> waiting = new Queue<int>();
        int showing = -1;
        float showMs, durationMs;

        public Radio(List<RadioLine> radioLines)
        {
            lines = radioLines ?? new List<RadioLine>();
            triggered = new bool[lines.Count];
            over = new bool[lines.Count];
            lastRoute = new int[lines.Count];
            routeEdge = new bool[lines.Count];
        }

        public int Count => lines.Count;
        public bool Triggered(int i) => i >= 0 && i < triggered.Length && triggered[i];
        public bool Over(int i) => i >= 0 && i < over.Length && over[i];
        /// <summary>Radio::lastMessageShown: the array's last line is over.</summary>
        public bool LastOver => lines.Count > 0 && over[lines.Count - 1];
        /// <summary>The line on screen now (after the 2 s delay), null = none. Fast-forward is blocked while it shows.</summary>
        public RadioLine Visible => showing >= 0 && showMs >= DelayMs ? lines[showing] : null;
        public int VisibleIndex => Visible != null ? showing : -1;
        public bool Busy => showing >= 0 || waiting.Count > 0;
        /// <summary>Remake: the box stays up at least until the line's voice clip is over (the text formula undershoots a third
        /// of the clips, by up to 5.7 s).</summary>
        public void HoldFor(float ms) { if (showing >= 0) durationMs = Mathf.Max(durationMs, ms); }

        /// <summary>Remake co-op: a follower's radio (CampaignLevel.SetupFollower) tests no triggers of its own; the leader's
        /// lines come in through ShowRemote.</summary>
        public bool Remote { get; set; }
        /// <summary>A line began to show (its index): the leader's level tells the crew.</summary>
        public event Action<int> Shown;

        /// <summary>Remake co-op: the leader's level showed line 'i': it shows here too (once).</summary>
        public void ShowRemote(int i)
        {
            if (i < 0 || i >= lines.Count || triggered[i]) return;
            triggered[i] = true;
            waiting.Enqueue(i);
        }

        /// <summary>LevelScript::skipCutscene: the first 'n' lines count as shown.</summary>
        public void MarkShown(int n)
        {
            for (int i = 0; i < n && i < lines.Count; i++) triggered[i] = over[i] = true;
            if (showing >= 0 && showing < n) showing = -1;
            if (waiting.Count > 0)
            {
                var keep = new List<int>(waiting);
                waiting.Clear();
                foreach (int i in keep) if (i >= n) waiting.Enqueue(i);
            }
        }

        public void Update(float dtMs, IRadioWorld w)
        {
            if (Remote) { UpdateShowing(dtMs); return; }
            // Types 0 / 0x19 watch the route every frame (also while another line shows): the edge is remembered.
            for (int i = 0; i < lines.Count; i++)
            {
                int t = lines[i].trigger;
                if (triggered[i] || (t != 0 && t != 0x19)) continue;
                int last = lastRoute[i], now = w.RouteIndex;
                lastRoute[i] = now;
                if (now > last && last == (t == 0 ? lines[i].param : 0)) routeEdge[i] = true;
            }
            if (showing >= 0)
            {
                showMs += dtMs;
                if (showMs >= DelayMs + durationMs) { over[showing] = true; showing = -1; }
            }
            // Radio::update: every line's trigger every frame, latched once true (RadioMessage+0x20).
            for (int i = 0; i < lines.Count; i++)
            {
                if (triggered[i] || !Test(i, lines[i], w)) continue;
                triggered[i] = true;
                waiting.Enqueue(i);
            }
            if (showing < 0 && waiting.Count > 0) Show(waiting.Dequeue());
        }

        void UpdateShowing(float dtMs)
        {
            if (showing >= 0)
            {
                showMs += dtMs;
                if (showMs >= DelayMs + durationMs) { over[showing] = true; showing = -1; }
            }
            if (showing < 0 && waiting.Count > 0) Show(waiting.Dequeue());
        }

        void Show(int i)
        {
            Shown?.Invoke(i);
            showing = i;
            showMs = 0f;
            float perLine = StoryTable.UsesAlienFont(lines[i].speaker) ? AlienCharsPerLine : CharsPerLine;
            int n = Mathf.Max(1, Mathf.CeilToInt(Localization.Get(lines[i].text).Length / perLine));
            durationMs = n * LineMs + ExtraMs;
        }

        /// <summary>RadioMessage::triggered 0x17c5d8.</summary>
        bool Test(int index, RadioLine m, IRadioWorld w)
        {
            int p = m.param, count = Mathf.Max(1, m.count);
            bool Any(Func<int, bool> f) { for (int k = p; k < p + count; k++) if (k < w.ShipCount && f(k)) return true; return false; }
            bool All(Func<int, bool> f) { for (int k = p; k < p + count; k++) if (k >= w.ShipCount || !f(k)) return false; return true; }
            int DeadShips() { int d = 0; for (int k = 0; k < w.ShipCount; k++) if (w.ShipDead(k)) d++; return d; }
            switch (m.trigger)
            {
                // Type 0: the frame the player route moves on from waypoint p (a route already past it never fires).
                case 0: return routeEdge[index];
                case 1: return Any(w.ShipDead);
                case 2: return Any(k => w.ShipDead(k) && w.ShipFriendly(k));
                case 3: return w.EnemiesLeft < 1;
                case 4: return w.FriendsLeft < 1;
                case 5: return w.MissionMs >= p;
                case 6: return Triggered(p) && Over(p);   // shows right after that line
                case 8: return Any(w.ShipActive);
                case 9: return All(w.ShipDead);
                case 10: return Any(k => w.ShipActive(k) && w.ShipFriendly(k));
                // hit points below a half / a quarter / three quarters of the maximum (a dead ship's 0 counts too).
                case 0xc: return Any(k => w.ShipDead(k) || w.ShipHullFraction(k) < 0.5f);
                case 0x13: return Any(k => w.ShipDead(k) || w.ShipHullFraction(k) < 0.25f);
                case 0x1f: return Any(k => w.ShipDead(k) || w.ShipHullFraction(k) < 0.75f);
                case 0xf: return DeadShips() > 0;
                case 0x10: for (int k = 0; k < w.ShipCount; k++) if (w.ShipActive(k) && w.ShipHostile(k)) return true; return false;
                case 0x14: return DeadShips() >= p;
                case 0x15: return p < w.ShipCount && w.ShipEmpDisabled(p);
                case 0x16: return w.CrateCargoCaptured >= p;
                case 0x17: return w.StationLocked;
                case 0x18: return p < w.ShipCount && w.ShipInactive(p) && !w.ShipDead(p) && w.MissionMs > 59999f;
                // The frame the route moves on from its first waypoint, with at least p ships alive.
                case 0x19:
                {
                    if (!routeEdge[index]) return false;
                    int alive = 0; for (int k = 0; k < w.ShipCount; k++) if (!w.ShipDead(k)) alive++;
                    return alive >= p;
                }
                case 0x1a: return w.ShipCount > 0 && w.ShipActive(0) && Mathf.Abs(w.ShipGameZ(0) - p) < 5000f;
                case 0x1b: return w.ScriptEvent == p;
                case 0x1c: return w.PlayerArmorGone;
                case 0x1e: { int d = 0; for (int k = 2; k <= 5 && k < w.ShipCount; k++) if (w.ShipDead(k)) d++; return d == p; }
                default: return false;
            }
        }
    }
}
