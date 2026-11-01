// FreelanceOrbit.cs
// A freelance mission's orbit (Level::createMission 0xbda70 with a non-empty level mission, MGame::dialogueEvent /
// successCheck / gameOverCheck; Reference/research/freelance_missions.md 4). Created by SpaceLevel when the orbit is
// the freelance mission's target (Freelance.IsMissionOrbit); the free-flight traffic is off then. hc = 1 + (difficulty
// - 0.5) (x1 normal, x2 Extreme), d = the mission's difficulty, A = 75 % pirates else the system's enemy race, E = the
// client race's enemy (pirates for other races):
//   1 Defense        int((int(d/10*5)+3)*hc) race-A fighters around the station (always enemy) + nextInt(5)+3 system-race
//                    friends on a route; win: the attackers dead (objective 7)
//   2 Protection     int(d/10*4)+2 race-A attackers from (-20000-rnd, 0, -20000-rnd) (+2000 apart) heading for the origin;
//                    'amount' system-race fighters parked 2000 above the asteroids, x3 hull, always friend; win: attackers
//                    dead / fail: all miners dead (0x12)
//   3 / 5 Recovery / Salvage  'amount' pirates asleep at (+-(40000+rnd 80000), 0, +-(...)); the last, "Hijacker" (1611),
//                    carries the container (117 / 116, swapped against the offer like the original); win: the container
//                    aboard (EMP + tractor beam) -> bring it to the client / fail: the carrier destroyed first (0xb / 0xc)
//   4 Pirate hunting int((int(d/10*5)+2)*hc) pirates asleep at the asteroid field (or a createRoute point); win: all dead
//   6 Wanted         one pirate asleep at (+-(60000+rnd 80000), 0, +-(...)), x3 hull, speed 3.0, named; win: it's dead
//   7 Junk removal   int(d/10*20)+15 space junk (hull 1, always enemy) around a point 40-70 km ahead + int(0.2d) pirates;
//                    121 000 ms (Level+0x130, checked every 5 s); win: all junk destroyed
//   9 Escort         int((int(d/10*5)+3)*hc) race-E attackers asleep (always enemy) at random points of a 3-point route
//                    ahead; 5 freighters of the client race (Terran for races above Midorian) at fixed points flying +Z,
//                    hull (2*min(level,20) + 150 + 2*campaign) (x1.4 Extreme), always friend; win: attackers dead / fail:
//                    all freighters dead
//   10 Intercept     race E (pirates -> Terran): nextInt(2)+2 convoy freighters asleep and parked around a route point,
//                    hull x0.7 (x1.4 Extreme) + int((int(d/10*5)+3)*hc) escorts asleep; win: the convoy dead
//   15 Ore Mining    (multiplayer sessions only: the original's generator never rolls it) int((int(0.2d)+1)*hc) race-E
//                    enemies (always enemy) on a 1-waypoint route (rnd(140000) - 70000, 0, 70000); the mining plant
//                    (docking type 1) at the asteroid field; 2 client-race haulers looping plant <-> plant - 30000 z, never
//                    attacking; win: the ore delivered to the plant (ObjectDocking) >= amount (Objective 0x1c).
//                    Remake (players' feedback: 5-6 km in front of the station it felt out of place): all of it at a remote
//                    site about 10 km out (MiningSite, seeded by the mission, the same in every game of the squad) with a field
//                    of its own around the plant (5-7 big + 18-25 small rocks of the mission's ore, OrbitBuilder.SpawnField),
//                    the enemies asleep there (awake from afar they blocked the fast-forward all the way); the player gets
//                    the lockable "Mining site" waypoint short of the plant and the briefing names it.
//   12 Challenge     the agent's ship (its race, 9 999 999 hull, speed 3.0, named, friend) flies a createRoute(3..4) past
//                    an odd number of sleeping pirates (i = int(d/10*4): i+3 for an even i, i+4 for an odd one); win: all
//                    dead with more kills than the rival (0x14) / fail: all dead, the rival as good or better (0x15)
// Briefing (after the launch / arrival camera, not for 0 / 8 / 11): Challenge 372, Junk 378, else 379-383; the game pauses.
// Checks from 5000 ms of level time. Success: 3 / 5 turn into the return trip (389), the others pay (reward message +
// sound 36, standing +5). Failure: 384-388 + 392 (Challenge 371 with the score). The remake marks the pirates' location
// with a waypoint for the hunting-type missions (the original's HUD shows no freelance marker).
// Multiplayer: the mission's ships and junk are shown to every player in the orbit (NetOrbit) and spawn out of their view
// (NetOrbit.OutOfSight); other players' kills count like the player's (Target.killedByRemote), so a squad plays it
// together; a squadmate's delivered ore and captured container count too (the shared status, NetMissions.AddStatus);
// the ships aren't taken over when this player leaves (NpcShip.MissionShip); the orbit stops when the mission is gone.
// A squadmate arriving where another member runs it gets the follower mode (SetupFollower): nothing built, but the
// mission's briefing, the runner's route (waypoint), timer and Challenge score (NetPlayer.MissionRoute / Clock / Score),
// the return trip's message and the result as the mission's dialog (NetMissions.ResultView); when the runner leaves (docks,
// jumps, respawns, leaves the squad), the member there with the lowest client id takes it over (Promote): the runner's
// mission ships go on as its own (their role from NetProxy.RoleFlags, their hull), the junk, the mining plant and a loose
// container too, so no progress is lost; with none left it counts as done (inherited), with none ever seen it is built
// anew. A follower's Ore Mining gets a local stand-in of the runner's plant to unload at (hidden: the runner's shows).
// Two runners at once (both arrived in the same moment): the higher client id stands down (its ships go). The runner
// dying in a session doesn't stop the mission: a result then comes without its dialog.

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GoF2Remake.World
{
    public class FreelanceOrbit : MonoBehaviour
    {
        const float M = 0.05f;
        public const float JunkTimeMs = 121000f;

        /// <summary>Show a bar agent's one-page message (text, name, portrait) and call the action when it closes.</summary>
        /// <summary>(text, client name, portrait, voice line, closed).</summary>
        public event Action<string, string, int[], string, Action> MessageRequested;
        public event Action<string> RewardMessage;
        public bool DialogueOpen { get; private set; }
        public Route PlayerRoute { get; private set; }
        public int Type => mission.type;

        // A mission ship's part (NetProxy.RoleFlags), for a squadmate's takeover.
        public const int RoleEnemy = 1, RoleFriend = 2, RoleStationary = 4, RoleNoLoot = 8, RoleCarrier = 16, RolePlant = 32,
                         RoleRival = 64, RoleConvoy = 128, RoleNoFire = 256, RoleFast = 512, RoleAlwaysEnemy = 1024, RoleAlwaysFriend = 2048;

        /// <summary>'s' part in this mission (0 = none of its ships).</summary>
        public int RoleFlags(NpcShip s)
        {
            if (s == null || !Running) return 0;
            int f = 0;
            int ei = enemies.IndexOf(s);
            if (ei >= 0) f |= RoleEnemy;
            if (friends.Contains(s)) f |= RoleFriend;
            if (s == plant) f |= RolePlant;
            if (s == rival) f |= RoleRival;
            if (s == carrier && !s.MissionCrateTaken) f |= RoleCarrier;
            if (mission.type == MissionType.Intercept && ei >= 0 && ei < convoyCount) f |= RoleConvoy;
            var spec = s.Spec;
            if (spec.stationary) f |= RoleStationary;
            if (spec.noLoot) f |= RoleNoLoot;
            if (spec.speed > 0f) f |= RoleFast;
            if (spec.alwaysEnemy) f |= RoleAlwaysEnemy;
            if (spec.alwaysFriend) f |= RoleAlwaysFriend;
            if (!s.shootingEnabled) f |= RoleNoFire;
            return f;
        }
        /// <summary>Junk removal: time left (ms), -1 = no limit or the mission over (#46: the clock stayed on the HUD after it).</summary>
        public float TimeLeftMs => mission.type == MissionType.JunkRemoval && !done ? Mathf.Max(0f, JunkTimeMs - missionMs) : -1f;
        /// <summary>Multiplayer: this game builds and runs the orbit (false = a squadmate's view of theirs).</summary>
        public bool Running { get; private set; }
        /// <summary>Multiplayer: the route for the squadmates ("x,y,z;..." game units) and the mission clock (ms).</summary>
        public string RouteText { get; private set; } = "";
        public float ClockMs => missionMs;

        SpaceLevel level;
        Traffic traffic;
        FreelanceMission mission;
        readonly List<NpcShip> enemies = new List<NpcShip>(), friends = new List<NpcShip>();
        readonly List<Target> junk = new List<Target>();
        readonly List<int> junkKinds = new List<int>();
        /// <summary>Junk removal's space junk and each one's prefab (CombatAssets.junk), for multiplayer's proxies.</summary>
        public IReadOnlyList<Target> Junk => junk;
        public int JunkKind(int i) => i >= 0 && i < junkKinds.Count ? junkKinds[i] : 0;
        NpcShip carrier, rival;
        int playerKills, otherKills;
        /// <summary>Level+0x24 / +0x20: the Challenge's score (the HUD's "player : rival").</summary>
        public int PlayerKills => Running ? playerKills : runnerScore >> 16;
        public int OtherKills => Running ? otherKills : runnerScore & 0xffff;
        int runnerScore;
        bool returnShown;
        // Follower: what the runner's orbit had (seen on its proxies), so a takeover with none left counts them as done.
        bool seenEnemies, seenFriends, seenJunk, inheritedEnemies, inheritedFriends;
        float scanMs;
        NpcShip localPlant;   // a follower's stand-in of the runner's mining plant (Ore Mining unloading)
        MiningSite site;      // Ore Mining: the remote site (the same in every game of the squad)
        /// <summary>Ore Mining: the site's own asteroid field (built in every game of the squad, runner or not), null = none.</summary>
        public Transform SiteAsteroids { get; private set; }
        /// <summary>Multiplayer: the site field's first asteroid index (NetOrbit, NetState's destroyed lists).</summary>
        public int SiteIndexBase => site != null ? site.IndexBase : -1;
        float levelMs, missionMs, timeCheckMs;
        bool briefed, done;

        static Vector3 ToUnity(Vector3 game) => new Vector3(game.x, game.y, -game.z) * M;
        static Vector3 ToGame(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;
        static Vector3 Jitter() => new Vector3(Random.Range(0, 40000) - 20000, Random.Range(0, 40000) - 20000, Random.Range(0, 40000) - 20000);
        static float Sign() => Random.Range(0, 2) == 0 ? 1f : -1f;

        public void Setup(SpaceLevel spaceLevel, Traffic npcTraffic)
        {
            level = spaceLevel;
            traffic = npcTraffic;
            mission = Freelance.Mission;
            Running = true;
            BuildSite();
            Build();
            traffic.ConnectPlayers();
            RouteText = EncodeRoute(PlayerRoute);
            Debug.Log($"FreelanceOrbit: {mission.Name} (type {mission.type}, difficulty {mission.difficulty}), {enemies.Count} enemies, " +
                      $"{friends.Count} friends, {junk.Count} junk");
        }

        /// <summary>Multiplayer: a squadmate here runs this mission: show it, build nothing (see the header).</summary>
        public void SetupFollower(SpaceLevel spaceLevel, Traffic npcTraffic)
        {
            level = spaceLevel;
            traffic = npcTraffic;
            mission = Freelance.Mission;
            Running = false;
            BuildSite();
            if (site != null) { PlayerRoute = SiteRoute(); RouteText = EncodeRoute(PlayerRoute); level.Navigation?.SetRoute(PlayerRoute, true); }
            GoF2Remake.World.SpaceHooks.MissionResultView = ShowResult;
            Debug.Log($"FreelanceOrbit: {mission.Name} run by a squadmate here");
        }

        GoF2Remake.World.IOrbitPeer Runner()
        {
            int station = level.Layout.stationIndex;
            foreach (var p in GoF2Remake.World.SpaceHooks.Peers)
                if (p != null && !p.IsOwner && p.IsSpawned && p.InSpace && p.Station == station && p.MissionRun == mission.netId) return p;
            return null;
        }

        /// <summary>No other member here with a lower client id holds the mission (the one who takes it over).</summary>
        bool FirstHere()
        {
            var me = GoF2Remake.World.SpaceHooks.LocalPeer;
            if (me == null) return false;
            int station = level.Layout.stationIndex;
            foreach (var p in GoF2Remake.World.SpaceHooks.Peers)
                if (p != null && !p.IsOwner && p.IsSpawned && p.InSpace && p.Station == station && p.MissionHeld == mission.netId
                    && p.OwnerClientId < me.OwnerClientId) return false;
            return true;
        }

        /// <summary>What the runner's orbit has (its mission proxies), for a later takeover.</summary>
        void ScanRunner(ulong runner)
        {
            int station = level.Layout.stationIndex;
            foreach (var p in SpaceHooks.Proxies)
            {
                if (!p.IsSpawned || p.Station != station || p.Creator != runner) continue;
                if (p.IsJunk) { seenJunk = true; continue; }
                if ((p.RoleFlags & RoleEnemy) != 0) seenEnemies = true;
                if ((p.RoleFlags & RoleFriend) != 0) seenFriends = true;
            }
        }

        static Vector3 GamePos(Vector3 unity) => new Vector3(unity.x, unity.y, -unity.z) / M;

        /// <summary>Promote: the old runner's mission ships, junk, plant and loose container as this game's own; true = there was
        /// an orbit to take over (even with nothing left alive).</summary>
        bool AdoptOldOrbit()
        {
            ulong me = GoF2Remake.Data.NetSession.LocalId;
            int station = level.Layout.stationIndex;
            bool any = seenEnemies || seenFriends || seenJunk;
            int crateItem = mission.type == MissionType.Recovery ? Freelance.SecureCabin : Freelance.SecureContainer;
            var convoy = new List<NpcShip>();
            foreach (var p in SpaceHooks.Proxies)
            {
                if (!p.IsSpawned || p.Station != station || !p.MissionAdoptable) continue;
                var at = GamePos(p.WorldPosition);
                int role = p.RoleFlags;
                if (p.IsJunk)
                {
                    if (mission.type != MissionType.JunkRemoval) continue;
                    SpawnJunk(at, p.JunkKind, p.WorldRotation);
                }
                else if ((role & RolePlant) != 0)
                {
                    if (localPlant != null) { plant = localPlant; localPlant = null; ShowPlant(); }
                    else
                    {
                        var spec = Traffic.MiningPlant();
                        spec.position = at;
                        plant = traffic.SpawnShip(spec);
                        plant.MissionShip = true;
                    }
                }
                else
                {
                    if (p.AdoptSpec(at).ship < 0) continue;
                    var spec = p.AdoptSpec(at);
                    if ((role & RoleFast) != 0) spec.speed = 3f;
                    if ((role & (RoleRival | RoleCarrier)) != 0 || mission.type == MissionType.Wanted) spec.name = p.Label.Length > 0 ? p.Label : null;
                    if ((role & RoleCarrier) != 0 && mission.status == 0) { spec.missionCrate = crateItem; spec.nameText = 1611; spec.name = null; }
                    if ((role & RoleRival) != 0 && PlayerRoute != null) spec.route = PlayerRoute.Clone();
                    var s = traffic.Adopt(spec, p.WorldRotation, p.HullFraction);
                    s.MissionShip = true;
                    foreach (var id in p.Aggressors) s.aggressors.Add(id);
                    if ((role & RoleNoFire) != 0) s.shootingEnabled = false;
                    bool enemy = (role & RoleEnemy) != 0;
                    if ((role & RoleConvoy) != 0) convoy.Add(s);
                    else (enemy ? enemies : friends).Add(s);
                    if (enemy || (role & RoleConvoy) != 0) s.Target.Died += OnEnemyDied;
                    if ((role & RoleCarrier) != 0) carrier = s;
                    if ((role & RoleRival) != 0) rival = s;
                }
                p.Adopt();
                any = true;
            }
            if (convoy.Count > 0) { enemies.InsertRange(0, convoy); convoyCount = convoy.Count; }
            // Recovery / Salvage: the container already out of the Hijacker, still floating.
            if ((mission.type == MissionType.Recovery || mission.type == MissionType.Salvage) && mission.status == 0)
                foreach (var c in SpaceHooks.Crates)
                {
                    if (!c.IsSpawned || c.IsOwner || c.Station != station || !c.IsMissionCrate) continue;
                    var assets = CombatAssets.Load();
                    var prefab = assets != null ? assets.Crate(Standing.Pirate) : null;
                    var go = prefab != null ? Instantiate(prefab, c.WorldPosition, Random.rotation) : new GameObject("Crate");
                    go.name = "Crate";
                    var crate = go.AddComponent<Crate>();
                    crate.Setup(new List<ItemStack> { new ItemStack(crateItem, 1) }, Standing.Pirate);
                    crate.missionCrate = crate.missionLoot = true;
                    c.Adopt();
                    any = true;
                }
            inheritedEnemies = seenEnemies;
            inheritedFriends = seenFriends;
            return any;
        }

        void ShowPlant()
        {
            plant.LocalOnly = false;
            foreach (var r in plant.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        }

        /// <summary>This game's mission ships, junk and plant go (a second runner standing down).</summary>
        void ClearOwn()
        {
            foreach (var s in enemies) if (s != null && !s.Gone) s.Vanish();
            foreach (var s in friends) if (s != null && !s.Gone) s.Vanish();
            if (plant != null && !plant.Gone) plant.Vanish();
            foreach (var j in junk) if (j != null) { Target.RadarObjects.Remove(j); j.gameObject.SetActive(false); }
            enemies.Clear(); friends.Clear(); junk.Clear(); junkKinds.Clear();
            plant = carrier = rival = null;
            convoyCount = 0;
        }

        /// <summary>Another member runs it here with a lower client id (both built it at once).</summary>
        bool LowerRunnerHere()
        {
            var me = GoF2Remake.World.SpaceHooks.LocalPeer;
            if (me == null) return false;
            int station = level.Layout.stationIndex;
            foreach (var p in GoF2Remake.World.SpaceHooks.Peers)
                if (p != null && p != me && p.IsSpawned && p.InSpace && p.Station == station && p.MissionRun == mission.netId && p.OwnerClientId < me.OwnerClientId)
                    return true;
            return false;
        }

        /// <summary>A second runner stands down: its ships go, it follows the other's orbit.</summary>
        void Demote()
        {
            ClearOwn();
            Running = false;
            RouteText = "";
            GoF2Remake.World.SpaceHooks.MissionResultView = ShowResult;
            Debug.Log($"FreelanceOrbit: {mission.Name} also built by a squadmate here, standing down");
        }

        /// <summary>The runner left: this game runs the orbit, taking over what is left of theirs (the score and the clock go
        /// on), or building it anew when there never was one to see.</summary>
        void Promote()
        {
            int keepPlayer = PlayerKills, keepOther = OtherKills;
            if (GoF2Remake.World.SpaceHooks.MissionResultView == (Func<int, int, string, bool>)ShowResult) GoF2Remake.World.SpaceHooks.MissionResultView = null;
            Running = true;
            NpcTables.LevelFreelanceType = mission.type;
            if (!AdoptOldOrbit())
            {
                if (localPlant != null) { localPlant.Vanish(); localPlant = null; }
                Build();
            }
            else if (localPlant != null) { localPlant.Vanish(); localPlant = null; }
            traffic.ConnectPlayers();
            playerKills = keepPlayer;
            otherKills = keepOther;
            levelMs = 0f;
            RouteText = EncodeRoute(PlayerRoute);
            level.Navigation?.SetRoute(PlayerRoute, true);
            Debug.Log($"FreelanceOrbit: {mission.Name} taken over, {enemies.Count} enemies, {friends.Count} friends, {junk.Count} junk" +
                      $"{(inheritedEnemies || inheritedFriends ? " (inherited)" : "")}");
        }

        static string EncodeRoute(Route r)
        {
            if (r == null) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var p in r.points) sb.Append((int)p.x).Append(',').Append((int)p.y).Append(',').Append((int)p.z).Append(';');
            return sb.ToString();
        }

        static Route DecodeRoute(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var r = new Route(false);
            foreach (var one in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var c = one.Split(',');
                if (c.Length == 3 && int.TryParse(c[0], out int x) && int.TryParse(c[1], out int y) && int.TryParse(c[2], out int z))
                    r.points.Add(new Vector3(x, y, z));
            }
            return r.points.Count > 0 ? r : null;
        }

        /// <summary>A squadmate's view: the runner's route, clock and score, the briefing, the return trip.</summary>
        void UpdateFollower(float dt)
        {
            var runner = Runner();
            if (runner == null)
            {
                if (level.StartSequenceOver && FirstHere()) Promote();
                return;
            }
            runnerScore = runner.MissionScore;
            missionMs = Mathf.Abs(missionMs + dt - runner.MissionClock) > 1000f ? runner.MissionClock : missionMs + dt;
            if ((scanMs -= dt) <= 0f) { scanMs = 1000f; ScanRunner(runner.OwnerClientId); }
            string route = runner.MissionRoute;
            if (route != RouteText)
            {
                RouteText = route;
                PlayerRoute = DecodeRoute(route);
                if (PlayerRoute != null && site != null) PlayerRoute.name = SiteName;
                level.Navigation?.SetRoute(PlayerRoute, true);
            }
            // Ore Mining: a stand-in of the runner's plant at the site, to unload at here (ObjectDocking).
            if (site != null && localPlant == null)
            {
                var spec = Traffic.MiningPlant();
                spec.position = site.plant;
                localPlant = traffic.SpawnShip(spec);
                localPlant.MissionShip = true;
                localPlant.LocalOnly = true;   // not shown to the others (the runner's plant is)
                foreach (var r in localPlant.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            }
            if (!level.StartSequenceOver || levelMs <= 5000f) return;
            if (!briefed) { Brief(); return; }
            if (mission.status == -1 && !returnShown)
            {
                // The runner's Recovery / Salvage turned into the return trip (NetMissions.Receive).
                returnShown = done = true;
                Open(Freelance.ReturnText(level.Database), 389, () => level.Navigation?.SetRoute(null));
            }
        }

        /// <summary>NetMissions.ResultView: the runner's result as this squadmate's dialog.</summary>
        bool ShowResult(int result, int share, string from)
        {
            if (done || DialogueOpen || Running || result == GoF2Remake.World.SpaceHooks.MissionAbandoned) return false;
            done = true;
            if (result == GoF2Remake.World.SpaceHooks.MissionSuccess)
            {
                Open(Freelance.SuccessText(out int successText, PlayerKills, OtherKills), successText, () =>
                {
                    RewardMessage?.Invoke($"{Localization.Get(216)} +{UI.ItemInfo.Credits(share)}");
                    var assets = CombatAssets.Load();
                    Sfx.PlayAt(assets != null ? assets.missionAccomplished : null, level.Player.transform.position);
                    level.Navigation?.SetRoute(null);
                });
                return true;
            }
            int textId = 371;
            string text = mission.type == MissionType.Challenge
                ? Localization.Get(371).Replace("#Q1", PlayerKills.ToString()).Replace("#Q2", OtherKills.ToString())
                : Freelance.FailureText(out textId);
            Open(text, textId, () => level.Navigation?.SetRoute(null));
            return true;
        }

        // ---- building (Level::createMission) ------------------------------------------------------------

        NpcShip Spawn(int race, Vector3 gamePos, Action<SpawnSpec> setup = null, bool enemy = true)
        {
            var spec = new SpawnSpec { group = NpcGroup.Raider, race = race, ship = NpcTables.RandomFighter(race), position = gamePos };
            setup?.Invoke(spec);
            spec.position = GoF2Remake.World.SpaceHooks.OutOfSightOf(spec.position);   // multiplayer: not where another player looks
            var s = traffic.SpawnShip(spec);
            s.MissionShip = true;
            (enemy ? enemies : friends).Add(s);
            s.Target.Died += OnEnemyDied;
            return s;
        }

        void OnEnemyDied(Target t)
        {
            if (!enemies.Exists(e => e.Target == t)) return;
            // Level+0x24 player kills / +0x20 kills by others; multiplayer: another player's kill counts as the player's.
            if (t.killedByNpc && !t.killedByRemote) otherKills++; else playerKills++;
        }

        /// <summary>Level::createRoute(n) 0xd0464: x +-(50000 + rnd 30000), y +-10000, z advancing 50000 + rnd 30000.</summary>
        static Route CreateRoute(int n)
        {
            var r = new Route(false);
            float z = 0f;
            for (int i = 0; i < n; i++)
            {
                z += Random.Range(0, 30000) + 50000;
                r.points.Add(new Vector3(Sign() * (Random.Range(0, 30000) + 50000), Random.Range(0, 20000) - 10000, z));
            }
            return r;
        }

        void Build()
        {
            float hc = 1f + (Session.Difficulty - 0.5f);
            int d = mission.difficulty;
            float df = d / 10f;
            int systemRace = traffic.SystemRace;
            int attackRace = Random.Range(0, 100) < 75 ? Standing.Pirate : Standing.EnemyRaceOf(systemRace);
            int clientEnemy = Standing.EnemyRaceOf(mission.clientRace);
            switch (mission.type)
            {
                case MissionType.Defense:
                {
                    int n = (int)(((int)(df * 5f) + 3) * hc);
                    for (int i = 0; i < n; i++) Spawn(attackRace, Jitter(), s => s.alwaysEnemy = true);
                    float x = Sign() * 50000f;
                    var route = new Route(false);
                    route.points.Add(new Vector3(x, 0, 50000)); route.points.Add(new Vector3(x, 0, 75000)); route.points.Add(new Vector3(x, 0, 100000));
                    int f = Random.Range(0, 5) + 3;
                    for (int i = 0; i < f; i++) Spawn(systemRace, route.points[0] + Jitter(), s => { s.alwaysFriend = true; s.route = route.Clone(); }, false);
                    break;
                }
                case MissionType.Protection:
                {
                    int n = (int)(df * 4f) + 2;
                    var wp = new Vector3(-20000 - Random.Range(0, 20000), 0, -20000 - Random.Range(0, 20000));
                    for (int i = 0; i < n; i++)
                    {
                        var route = new Route(false);
                        route.points.Add(Vector3.zero);
                        Spawn(attackRace, wp + new Vector3(2000f * i, 0, 0), s => { s.alwaysEnemy = true; s.route = route; });
                    }
                    var rocks = level.Asteroids != null ? level.Asteroids.GetComponentsInChildren<Target>() : new Target[0];
                    for (int i = 0; i < mission.amount; i++)
                    {
                        Vector3 at = rocks.Length > 0 ? ToGame(rocks[Mathf.Min(rocks.Length - 1, i + rocks.Length / 2)].transform.position) + new Vector3(0, 2000, 0)
                                                      : Jitter();
                        var miner = Spawn(systemRace, at, s => { s.alwaysFriend = true; s.stationary = true; s.noLoot = true; }, false);
                        // Player::setHitpoints(max * 3) raises the maximum too (+0x84): the friendly-fire thresholds of
                        // Player::damage are fractions of it. Only the hull tripled, a fifth of it in stray hits alarmed the
                        // whole station race (#33: the bribe at Carme).
                        miner.SetHull(miner.Hp.maxHull * 3);
                    }
                    break;
                }
                case MissionType.Recovery:
                case MissionType.Salvage:
                {
                    var at = new Vector3(Sign() * (40000 + Random.Range(0, 80000)), 0, Sign() * (40000 + Random.Range(0, 80000)));
                    int crateItem = mission.type == MissionType.Recovery ? Freelance.SecureCabin : Freelance.SecureContainer;
                    for (int i = 0; i < mission.amount; i++)
                    {
                        bool last = i == mission.amount - 1;
                        var s = Spawn(Standing.Pirate, at + Jitter(), spec =>
                        {
                            spec.asleep = true;
                            if (last) { spec.missionCrate = crateItem; spec.nameText = 1611; }
                        });
                        if (last) carrier = s;
                    }
                    MarkRoute(at);
                    break;
                }
                case MissionType.PirateHunting:
                {
                    var at = HasField ? level.Layout.asteroidCentre : CreateRoute(Random.Range(2, 4)).points[0];
                    int n = (int)(((int)(df * 5f) + 2) * hc);
                    for (int i = 0; i < n; i++) Spawn(Standing.Pirate, at + Jitter(), s => s.asleep = true);
                    MarkRoute(at);
                    break;
                }
                case MissionType.Wanted:
                {
                    var at = new Vector3(Sign() * (60000 + Random.Range(0, 80000)), 0, Sign() * (60000 + Random.Range(0, 80000)));
                    var w = Spawn(Standing.Pirate, at, s => { s.asleep = true; s.speed = 3f; s.name = mission.targetName; });
                    w.Target.hitpoints.hull = w.Target.hitpoints.maxHull *= 3;
                    w.Target.hp = w.Target.maxHp = w.Target.hitpoints.maxHull;
                    MarkRoute(at);
                    break;
                }
                case MissionType.JunkRemoval:
                {
                    var wp = new Vector3(Random.Range(0, 40000) - 20000, Random.Range(0, 20000) - 10000, Random.Range(0, 30000) + 40000);
                    int pirates = (int)(df + df);
                    int total = (int)(df * 20f) + pirates + 15;
                    for (int i = 0; i < total - pirates; i++) SpawnJunk(wp + new Vector3(Random.Range(0, 20000) - 10000, Random.Range(0, 20000) - 10000, Random.Range(0, 20000) - 10000));
                    for (int i = 0; i < pirates; i++) Spawn(Standing.Pirate, Jitter());
                    MarkRoute(wp);
                    break;
                }
                case MissionType.Escort:
                {
                    int n = (int)(((int)(df * 5f) + 3) * hc);
                    var route = new Route(false);
                    route.points.Add(new Vector3(10000, 0, 100000)); route.points.Add(new Vector3(10000, 0, 150000)); route.points.Add(new Vector3(10000, 0, 200000));
                    // Level::createMission case 9: each attacker at a random waypoint (createShip's +-20000 jitter), setToSleep,
                    // setAlwaysEnemy: hostile whatever the player's standing (a neutral or friendly race's attackers showed as
                    // friends while they killed the freighters).
                    for (int i = 0; i < n; i++)
                        Spawn(clientEnemy, route.points[Random.Range(0, route.points.Count)] + Jitter(), s => { s.asleep = true; s.alwaysEnemy = true; s.route = route.Clone(); });
                    Vector3[] points = { new Vector3(-2500, -300, 27000), new Vector3(6500, 3000, 24000), new Vector3(-4000, -2000, 19000), new Vector3(9000, -6000, 17000), new Vector3(3000, 7000, 15000) };
                    int race = mission.clientRace >= 0 && mission.clientRace < 4 ? mission.clientRace : 0;   // clientRace < 4, else Terran
                    int hull = (int)((2 * Mathf.Min(Session.Rank, 20) + 150 + 2 * Session.CampaignMission) * (Session.Difficulty > 0.7f ? 1.4f : 1f));
                    foreach (var p in points)
                        Spawn(race, p, s => { s.freighter = true; s.ship = race == 1 ? 13 : 15; s.alwaysFriend = true; s.hitpoints = hull; s.noLoot = true; }, false);
                    break;
                }
                case MissionType.Intercept:
                {
                    // Level::createMission case 10: a two-point route (x / y rnd(5000) - 2500 each, z 80000 + rnd(30000) and
                    // 120000 + rnd(30000)); the freighters sleep around its second waypoint (+-10000, setToSleep,
                    // setAlwaysEnemy, setMoving(false)), the escorts sleep at a random waypoint.
                    int race = clientEnemy == Standing.Pirate ? 0 : clientEnemy;
                    var route = new Route(false);
                    route.points.Add(new Vector3(Random.Range(0, 5000) - 2500, Random.Range(0, 5000) - 2500, 80000 + Random.Range(0, 30000)));
                    route.points.Add(new Vector3(Random.Range(0, 5000) - 2500, Random.Range(0, 5000) - 2500, 120000 + Random.Range(0, 30000)));
                    int convoy = Random.Range(0, 2) + 2;
                    for (int i = 0; i < convoy; i++)
                    {
                        var at = route.points[1] + new Vector3(Random.Range(0, 20000) - 10000, Random.Range(0, 20000) - 10000, Random.Range(0, 20000) - 10000);
                        var f = Spawn(race, at, s => { s.freighter = true; s.ship = race == 1 ? 13 : 15; s.stationary = true; s.alwaysEnemy = true; s.asleep = true; });
                        f.Target.hitpoints.hull = f.Target.hitpoints.maxHull = (int)(f.Target.hitpoints.maxHull * 0.7f * (Session.Difficulty > 0.7f ? 1.4f : 1f));
                        f.Target.hp = f.Target.maxHp = f.Target.hitpoints.maxHull;
                    }
                    int escorts = (int)(((int)(df * 5f) + 3) * hc);
                    for (int i = 0; i < escorts; i++)
                        Spawn(race, route.points[Random.Range(0, route.points.Count)] + Jitter(), s => { s.asleep = true; s.route = route.Clone(); s.alwaysEnemy = true; });
                    convoyCount = convoy;
                    MarkRoute(route.points[1]);
                    break;
                }
                case MissionType.OreMining:
                {
                    int n = (int)(((int)(0.2f * d) + 1) * hc);
                    var route = new Route(false);
                    // Level::createMission case 0xf: a one-waypoint route (rnd(140000) - 70000, 0, 70000); the enemies awake at
                    // it (createShip's +-20000 jitter), setAlwaysEnemy. Remake: the route point beside the remote site
                    // (MiningSite), the enemies asleep there (sleepers wake on the player within 25000 / their detect range).
                    route.points.Add(site.plant + new Vector3(Random.Range(0, 60000) - 30000f, Random.Range(0, 20000) - 10000f, Random.Range(0, 60000) - 30000f));
                    for (int i = 0; i < n; i++) Spawn(clientEnemy, route.points[0] + Jitter(), s => { s.alwaysEnemy = true; s.asleep = true; s.route = route.Clone(); });
                    // The plant exactly at the site (its field is built around it in every game of the squad), not moved
                    // out of the others' sight.
                    var plantAt = site.plant;
                    var spec = Traffic.MiningPlant();
                    spec.position = plantAt;
                    plant = traffic.SpawnShip(spec);
                    plant.MissionShip = true;
                    int race = Mathf.Clamp(mission.clientRace, 0, 3);
                    for (int i = 0; i < 2; i++)
                    {
                        var loop = new Route(true);
                        loop.points.Add(plantAt + new Vector3(3000f * (i == 0 ? 1 : -1), 0, 0));
                        loop.points.Add(plantAt + new Vector3(3000f * (i == 0 ? 1 : -1), 0, -30000f));
                        var hauler = Spawn(race, loop.points[i], s => { s.alwaysFriend = true; s.noLoot = true; s.route = loop; }, false);
                        hauler.shootingEnabled = false;   // they never attack
                    }
                    PlayerRoute = SiteRoute();
                    break;
                }
                case MissionType.Challenge:
                {
                    var route = CreateRoute(Random.Range(3, 5));
                    // Level::createMission case 0xc: n = i + 3, i + 4 when that is even, + 1 for the rival: the pirates are
                    // always an odd number (i + 3 for an even i, i + 4 for an odd one).
                    int i4 = (int)(df * 4f);
                    int pirates = (i4 + 3) % 2 == 1 ? i4 + 3 : i4 + 4;
                    rival = Spawn(Mathf.Clamp(mission.clientRace, 0, 7) > 3 ? 0 : mission.clientRace, ToGame(level.Player.transform.position) + new Vector3(1500, 0, 3000),
                                  s => { s.alwaysFriend = true; s.hitpoints = 9999999; s.speed = 3f; s.route = route.Clone(); s.name = mission.clientName; s.noLoot = true; }, false);
                    for (int i = 0; i < pirates; i++)
                    {
                        var wp = route.points[Random.Range(0, route.points.Count)];
                        Spawn(Standing.Pirate, wp + Jitter(), s => s.asleep = true);
                    }
                    var player = route.Clone();
                    PlayerRoute = player;
                    break;
                }
            }
        }

        int convoyCount;
        NpcShip plant;

        /// <summary>The orbit has an asteroid field: its centre is OrbitLayout.asteroidCentre (game units). The "Asteroids"
        /// root sits at the origin with the asteroids placed around the centre, so its own position is the station's.</summary>
        bool HasField => level.Asteroids != null && level.Asteroids.childCount > 0;

        /// <summary>Ore Mining (remake): the remote site and its asteroid field, from the mission's seed, so every game of
        /// the squad builds the same (MiningSite). The field's rocks are all the mission's ore.</summary>
        void BuildSite()
        {
            if (mission.type != MissionType.OreMining || site != null) return;
            site = MiningSite.For(mission, level.Layout);
            var saved = Random.state;
            Random.InitState(site.seed);
            var s = site;
            SiteAsteroids = OrbitBuilder.SpawnField(level.Database, level.Layout, s.fieldCentre, s.bigCount + s.smallCount, s.bigCount,
                                                    MiningSite.BigSide, MiningSite.SmallSide, mission.good, transform,
                                                    u => s.Blocks(ToGame(u)), "Mining site asteroids");
            Random.state = saved;
            Debug.Log($"FreelanceOrbit: mining site at {site.plant} ({site.plant.magnitude * M / 1000f:0.0} km out), {SiteAsteroids.childCount} asteroids of item {mission.good}");
        }

        /// <summary>Remake: the waypoint's name (Route.name) for the Ore Mining site.</summary>
        public static string SiteName => Localization.Extra("miningSite", "Mining site");

        Route SiteRoute()
        {
            var r = new Route(false) { name = SiteName };
            r.points.Add(site.waypoint);
            return r;
        }

        void MarkRoute(Vector3 gamePoint)
        {
            var r = new Route(false);
            r.points.Add(gamePoint);
            PlayerRoute = r;
        }

        /// <param name="takenKind">A takeover (Promote): the old runner's junk, its kind and pose (else a random one, out of sight).</param>
        void SpawnJunk(Vector3 gamePos, int takenKind = -1, Quaternion takenRotation = default)
        {
            var assets = CombatAssets.Load();
            bool taken = takenKind >= 0;
            int kind = taken ? takenKind : assets != null && assets.junk != null && assets.junk.Length > 0 ? Random.Range(0, assets.junk.Length) : -1;
            var prefab = kind >= 0 && assets != null && assets.junk != null && kind < assets.junk.Length ? assets.junk[kind] : null;
            if (!taken) gamePos = GoF2Remake.World.SpaceHooks.OutOfSightOf(gamePos);   // multiplayer: not where another player looks
            var go = prefab != null ? Instantiate(prefab, ToUnity(gamePos), taken ? takenRotation : Random.rotation, transform) : new GameObject("Junk");
            junkKinds.Add(Mathf.Max(0, kind));
            go.name = "Space junk";
            var t = go.AddComponent<Target>();
            t.hp = t.maxHp = 1f;                 // Player(1000, 1, ...): one hit
            t.radius = 600f * M;
            t.race = Standing.Pirate;
            t.hostileToPlayer = true;
            t.destroyedSound = CombatAssets.Pick(assets?.garbageExplosion);   // PlayerJunk::update: 22 Garbage_Explosion
            t.displayName = Target.JunkName;
            t.plateNameOnly = t.plateNoIcon = true;
            t.Died += dead =>
            {
                // PlayerJunk::update 0x18afb4: no Explosion, one emitManual of record 0x15 SET_EXPLOSION_MANUALLY_JUNK
                // (Level+0x34): a camera-facing sprite_explosion, 1600 + rnd(200) units, +500/s, 1000 ms.
                ShipBurn.ManualBurst(dead.transform.position, 1f, 1600f, 1800f, 500f);
                Session.JunkDestroyed++;   // Status+0xb0
                Target.RadarObjects.Remove(dead);
                // PlayerJunk::update 0x18afb4: 10 % leave a container (kind 3, mesh 16920) with 1-10 t Space Waste (99).
                if (Random.Range(0, 100) < 10) DropJunkCrate(dead.transform.position);
            };
            Target.RadarObjects.Add(t);
            junk.Add(t);
        }

        void DropJunkCrate(Vector3 at)
        {
            // KIPlayer::createCrate(3): mesh 0x4218 space_junk_004
            var crate = Crate.Spawn(at, new List<ItemStack> { new ItemStack(99, Random.Range(0, 10) + 1) }, Standing.Pirate, Crate.LookJunk);
            crate.missionLoot = true;   // multiplayer: only the mission's team takes it
        }

        void OnDestroy()
        {
            foreach (var j in junk) Target.RadarObjects.Remove(j);
            if (GoF2Remake.World.SpaceHooks.MissionResultView == (Func<int, int, string, bool>)ShowResult) GoF2Remake.World.SpaceHooks.MissionResultView = null;
        }

        // ---- per frame (MGame::dialogueEvent / successCheck / gameOverCheck) ----------------------------

        void Update()
        {
            // #48: the mission ended another way (discarded in the Missions window, another one taken, ended by the squad): its
            // waypoint goes too; it stayed on the HUD until the orbit was left.
            if (Freelance.Mission != mission && !routeCleared && level != null && !DialogueOpen)
            {
                routeCleared = true;
                done = true;
                level.Navigation?.SetRoute(null);
            }
            if (done || level == null || DialogueOpen || MessageRequested == null) return;
            float dt = Time.deltaTime * 1000f;
            levelMs += dt;
            if (!Running) { UpdateFollower(dt); return; }
            missionMs += dt;
            // Multiplayer: the squad plays on while this runner is dead (a result then comes without its dialog).
            bool dead = level.Health != null && level.Health.Dead;
            if (dead && !GoF2Remake.Data.NetSession.Active) return;
            if (GoF2Remake.Data.NetSession.Active && levelMs < 15000f && LowerRunnerHere()) { Demote(); return; }
            if (!level.StartSequenceOver) return;
            // MGame::OnUpdate: dialogueEvent only once the level clock (MGame+0x48) is past 5000 ms.
            if (!briefed && !dead && levelMs > 5000f && Brief()) return;
            if (Failed()) { if (dead) Resolve(false); else Fail(); return; }
            // Level+0x130: the time limit, checked every 5000 ms.
            if (mission.type == MissionType.JunkRemoval && (timeCheckMs += dt) >= 5000f)
            {
                timeCheckMs = 0f;
                if (missionMs >= JunkTimeMs && !Won()) { if (dead) Resolve(false); else Fail(); return; }
            }
            if (levelMs >= 5000f && Won()) { if (dead) Resolve(true); else Succeed(); }
        }

        /// <summary>The mission ended another way and its route was taken off the HUD.</summary>
        bool routeCleared;

        /// <summary>Multiplayer, the runner dead: the result without its dialog (a chat line instead).</summary>
        void Resolve(bool success)
        {
            done = true;
            level.Navigation?.SetRoute(null);
            if (!success) { Freelance.Fail(); GoF2Remake.World.SpaceHooks.Notice?.Invoke(Localization.Get(392)); return; }
            if (mission.type == MissionType.Recovery || mission.type == MissionType.Salvage) { Freelance.ToReturnTrip(); return; }
            int paid = Freelance.Succeed(false);
            GoF2Remake.World.SpaceHooks.Notice?.Invoke($"{Localization.Get(216)} +{UI.ItemInfo.Credits(paid)}");
        }

        /// <summary>The briefing after the launch / arrival camera and 5 s of level time (not for 0 / 8 / 11); true = its dialog opened.</summary>
        bool Brief()
        {
            briefed = true;
            int t = mission.type;
            if (t == MissionType.Courier || t == MissionType.Purchase || t == MissionType.Passenger) return false;
            int text = t == MissionType.Challenge ? 372 : t == MissionType.JunkRemoval ? 378 : 379 + Random.Range(0, 5);
            string body = Localization.Get(text);
            // Remake: Ore Mining's site is far out; the briefing gives its coordinates as the waypoint.
            if (site != null)
                body += "\n\n" + Localization.Extra("miningSiteBriefing", "The mining plant is at a remote site in this orbit. I've sent you its coordinates: follow the \"Mining site\" waypoint, mine the ore in the asteroid field around the plant and unload it there.");
            Open(body, text, () => { if (Running) missionMs = 0f; });
            return true;
        }

        /// <summary>Objective::achieved counts KIPlayer::isDead (state 4): a fighter after its tumble (the explosion), a
        /// freighter after its wreck animation, not on the killing hit (isDying, state 3). Gone = removed from the orbit.</summary>
        static bool AllDead(List<NpcShip> ships) => ships.TrueForAll(s => s == null || s.Gone || s.Current == NpcShip.State.Dead);

        bool Won()
        {
            switch (mission.type)
            {
                case MissionType.Recovery:
                case MissionType.Salvage:
                {
                    int item = mission.type == MissionType.Recovery ? Freelance.SecureCabin : Freelance.SecureContainer;
                    // Multiplayer: status > 0 = a squadmate captured it.
                    return Session.Cargo.Exists(c => c.item == item && c.amount > 0) || (GoF2Remake.Data.NetSession.Active && mission.status > 0);
                }
                case MissionType.JunkRemoval: return junk.TrueForAll(j => j == null || !j.Alive);
                case MissionType.Protection:
                case MissionType.Escort:
                case MissionType.Defense:
                case MissionType.PirateHunting:
                case MissionType.Wanted: return (enemies.Count > 0 || inheritedEnemies) && AllDead(enemies);   // inherited: all dead before
                case MissionType.Intercept: return AllDead(enemies.GetRange(0, Mathf.Min(convoyCount, enemies.Count)));
                case MissionType.Challenge: return AllDead(enemies) && playerKills > otherKills;
                case MissionType.OreMining: return mission.status >= mission.amount;   // Objective 0x1c: delivered ore
            }
            return false;
        }

        bool Failed()
        {
            switch (mission.type)
            {
                case MissionType.Recovery:
                case MissionType.Salvage: return carrier != null && !carrier.MissionCrateTaken && (!carrier.Target.Alive || carrier.Current != NpcShip.State.Fly);
                case MissionType.Protection:
                case MissionType.Escort: return (friends.Count > 0 || inheritedFriends) && AllDead(friends);
                case MissionType.Challenge: return AllDead(enemies) && playerKills <= otherKills;
            }
            return false;
        }

        void Succeed()
        {
            done = true;
            var m = mission;
            if (m.type == MissionType.Recovery || m.type == MissionType.Salvage)
            {
                // MGame::successCheck: the orbit becomes a plain one; deliver the container to the client.
                Open(Freelance.ReturnText(level.Database), 389, () =>
                {
                    if (Freelance.Mission == m) Freelance.ToReturnTrip();   // multiplayer: unless the squad ended it meanwhile
                    level.Navigation?.SetRoute(null);
                });
                return;
            }
            Open(Freelance.SuccessText(out int successText, playerKills, otherKills), successText, () =>
            {
                if (Freelance.Mission != m) { level.Navigation?.SetRoute(null); return; }   // multiplayer: ended meanwhile
                int paid = Freelance.Succeed(false);
                RewardMessage?.Invoke($"{Localization.Get(216)} +{UI.ItemInfo.Credits(paid)}");
                var assets = CombatAssets.Load();
                Sfx.PlayAt(assets != null ? assets.missionAccomplished : null, level.Player.transform.position);
                level.Navigation?.SetRoute(null);
            });
        }

        void Fail()
        {
            done = true;
            int textId = 371;
            string text = mission.type == MissionType.Challenge
                ? Localization.Get(371).Replace("#Q1", playerKills.ToString()).Replace("#Q2", otherKills.ToString())
                : Freelance.FailureText(out textId);
            Open(text, textId, () => { if (Freelance.Mission == mission) Freelance.Fail(); level.Navigation?.SetRoute(null); });
        }

        void Open(string text, int textId, Action after)
        {
            DialogueOpen = true;
            if (level.Navigation != null) level.Navigation.Conversation = true;
            MessageRequested?.Invoke(text, mission.clientName, mission.clientPortrait, Freelance.Voice(textId), () =>
            {
                DialogueOpen = false;
                if (level.Navigation != null) level.Navigation.Conversation = false;
                after?.Invoke();
            });
        }
    }
}
