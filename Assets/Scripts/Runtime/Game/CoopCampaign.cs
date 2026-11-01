// CoopCampaign.cs
// Remake multiplayer: the co-op campaign (docs/notes/multiplayer-coop.md), the story as a crew shares it (4 by default). The rules as
// plain C#; the networking (NetCoop) fills GameHooks' co-op members and carries the messages.
//   The leader (the host) owns the story: its Session's step, mission and the step's shared counters are the crew's
//     (State, published whenever they change). Every game still keeps and advances its own story (Story.Advance) so the
//     step's side effects (loaner ships, blueprints, revealed systems, rewards, cargo) happen to every player.
//   A guest behind the leader catches up silently (Advance without conversations), except for the one step whose own
//     success conversation it is about to show (CatchUpWaitSeconds at most); one ahead (it closed that conversation
//     first) waits for the leader. A guest's own changes to the step's shared counters (the mission value: bars searched,
//     units evacuated, freighters scanned; the story counter; step 59's convoy targets) go to the leader as a Progress
//     and come back merged.
//   Any crew member's game may find a step complete (ReportWon: docked at the target, the cargo aboard...): the leader
//     tells the whole crew (OnStepWon), and each game opens that step's success conversation where it is (RemoteWon).
//   A guest's own advance that wasn't a won step (a level script, the Khador misjump at 78, step 148's broker at Kalun
//     Amir) goes to the leader, who follows (FollowGuestAdvance).
//   Step 13's three freelance missions count the crew's (CrewMissions, each mission once).
//   A failed mission (the leader's level failed, or the leader died in it): the leader's step goes back to its checkpoint
//     (Story.RepairCheckpoint) and every guest's to the leader's (failSerial).
//   A new guest starts with the leader's own ship and loadout (the kit) and catches up from step 0.
//   The host's two settings (the Host card, /coop), in the State for every game: how much stronger the enemies get per
//     player past the first (Strength: their hull in story levels, every hostile NPC's guns at half that), and whether a
//     story step's credits are shared out across the crew or paid to each in full (SplitRewards).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class CoopCampaign
    {
        /// <summary>A crew's default size (the Co-op campaign screen's Max players) and the largest (a session's, NetGame).</summary>
        public const int DefaultCrew = 4, MaxCrew = 100;

        /// <summary>The leader's session's player limit (in its State): the lobby's "crew N / limit".</summary>
        public static int CrewLimit { get; private set; } = DefaultCrew;

        /// <summary>The leader (NetCoop.PrepareHost): the session's player limit.</summary>
        public static void SetCrewLimit(int limit) => CrewLimit = Mathf.Clamp(limit, 2, MaxCrew);
        /// <summary>A guest's own success conversation of the step the leader has just left: how long the catch-up waits for it.</summary>
        public const float CatchUpWaitSeconds = 120f;
        /// <summary>A guest ahead of the leader this long (an advance the leader didn't follow) is set back to the leader's step.</summary>
        public const float AheadSeconds = 120f;
        /// <summary>The Strength choices (% per player past the first) and the default.</summary>
        public static readonly int[] StrengthChoices = { 0, 25, 50, 100 };
        public const int DefaultStrength = 50;

        /// <summary>The session's enemy strength: % more per player past the first (the leader's setting, in its State).</summary>
        public static int Strength { get; private set; } = DefaultStrength;
        /// <summary>The session's story rewards are shared out across the crew (else each player gets them in full).</summary>
        public static bool SplitRewards { get; private set; } = true;

        /// <summary>The leader (the Host card at the start, /coop later): the session's settings.</summary>
        public static void SetSettings(int strength, bool splitRewards)
        {
            Strength = Mathf.Clamp(strength, 0, 200);
            SplitRewards = splitRewards;
        }

        /// <summary>The crew's squad: every player of a co-op session is in it (NetCoop).</summary>
        public const int CrewSquad = 1;

        /// <summary>The leader's shared story.</summary>
        [Serializable]
        public class State
        {
            public int index = -1;
            public StoryMission mission = new StoryMission();
            public int counter;
            public List<int> targets = new List<int>();
            public int invasionSystem = -1, invasionStation = -1;
            public int crewMissions;
            public int failSerial;
            public int campaign;
            /// <summary>The leader's settings: enemy strength (% per extra player), story rewards shared.</summary>
            public int strength = DefaultStrength;
            public bool splitRewards = true;
            public int crewLimit = DefaultCrew;
            /// <summary>Where the leader last docked: a new guest's start.</summary>
            public int station = -1;
            /// <summary>The leader's own ship and loadout: a new guest's start.</summary>
            public int kitShip = -1;
            public List<ItemStack> kit = new List<ItemStack>();
        }

        /// <summary>A guest's changes to the step's shared counters since the last State it applied.</summary>
        [Serializable]
        public class Progress
        {
            public int index = -1;
            public int valueAdd, valueOr, counterAdd;
            public List<int> targetsDone = new List<int>();
            public bool IsEmpty => valueAdd == 0 && valueOr == 0 && counterAdd == 0 && targetsDone.Count == 0;
        }

        static State leaderState;      // a guest: the last State from the leader
        static State baseline;         // a guest: the shared fields after the last State applied or Progress sent
        static string lastPublished;   // the leader: the last State sent
        static int remoteWon = -1;
        static float waitingSince = -1f, aheadSince = -1f;
        static bool catchingUp;
        static int appliedFailSerial = -1;
        // The leader's own bookkeeping (in its State).
        static int crewMissions, crewMissionsIndex = -1, failSerial;
        static readonly HashSet<long> missionsCounted = new HashSet<long>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reset();

        /// <summary>A session starts or ends: nothing of the last one.</summary>
        public static void Reset()
        {
            leaderState = baseline = null;
            lastPublished = null;
            remoteWon = -1;
            waitingSince = aheadSince = -1f;
            catchingUp = false;
            appliedFailSerial = -1;
            crewMissions = 0;
            crewMissionsIndex = -1;
            failSerial = 0;
            missionsCounted.Clear();
            Strength = DefaultStrength;
            SplitRewards = true;
            CrewLimit = DefaultCrew;
        }

        /// <summary>A co-op campaign session runs.</summary>
        public static bool Active => GameHooks.CoopActive != null && GameHooks.CoopActive();
        /// <summary>This game leads it (the host).</summary>
        public static bool Leader => Active && GameHooks.CoopLeader != null && GameHooks.CoopLeader();
        public static bool Guest => Active && !Leader;
        /// <summary>The players in the session (1 outside one).</summary>
        public static int CrewSize => Active && GameHooks.CoopCrewSize != null ? Mathf.Clamp(GameHooks.CoopCrewSize(), 1, MaxCrew) : 1;
        /// <summary>A guest is advancing to the leader's step (no conversations, no reports).</summary>
        public static bool CatchingUp => catchingUp;
        /// <summary>Counts the catch-ups (the station's turntable and menu locks follow one, StationMenu).</summary>
        public static int CatchUpSerial { get; private set; }
        /// <summary>A guest: the leader's last State, null before it has arrived.</summary>
        public static State LeaderState => leaderState;

        // ---- crew rules -----------------------------------------------------------------------------------------

        /// <summary>A story level's hostile ships' hull for 'crew' players: +Strength % per player past the first.</summary>
        public static float HullFactor(int crew) => HullFactor(crew, Strength);
        public static float HullFactor(int crew, int strength) => 1f + strength / 100f * (Mathf.Clamp(crew, 1, MaxCrew) - 1);

        /// <summary>The hostile NPCs' gun damage for 'crew' players: half the hull's increase (the crew's ships share the fire).</summary>
        public static float DamageFactor(int crew) => DamageFactor(crew, Strength);
        public static float DamageFactor(int crew, int strength) => 1f + strength / 200f * (Mathf.Clamp(crew, 1, MaxCrew) - 1);

        /// <summary>This game's hostile NPCs' guns in a co-op session (NpcTables): 1 outside one.</summary>
        public static float NpcDamageFactor => Active ? DamageFactor(CrewSize) : 1f;

        /// <summary>A story reward as this player gets it: shared out across the crew (rounded up), or in full.</summary>
        public static int Share(int reward) => Share(reward, Active ? CrewSize : 1, SplitRewards);
        public static int Share(int reward, int crew, bool split) =>
            !split || reward <= 0 ? reward : (reward + Mathf.Clamp(crew, 1, MaxCrew) - 1) / Mathf.Clamp(crew, 1, MaxCrew);

        /// <summary>Steps whose mission value is a set of bits (bars searched at 116, freighters scanned at 125, brokers at
        /// 148): merged with OR; every other step's value is a count (merged by adding).</summary>
        public static bool BitSteps(int index) => index == 116 || index == 125 || index == 148;

        /// <summary>Step 13's count: the freelance missions the crew finished since the step began.</summary>
        public static int CrewMissions
        {
            get
            {
                if (Leader) return crewMissionsIndex == Story.Index ? crewMissions : 0;
                return leaderState != null && leaderState.index == Story.Index ? leaderState.crewMissions : 0;
            }
        }

        // ---- a won step ---------------------------------------------------------------------------------------------

        /// <summary>This game found the step complete (StationMenu / StorySpace): the leader tells the crew.</summary>
        public static void ReportWon(int index)
        {
            if (!Active || catchingUp) return;
            GameHooks.CoopStepWon?.Invoke(index);
        }

        /// <summary>The leader: step 'index' is won (by any crew member). This game opens its success conversation where it is.</summary>
        public static void OnStepWon(int index)
        {
            if (!Active || Story.Mission == null || Story.Mission.IsEmpty) return;
            if (index == Story.Index && !Story.Mission.won) remoteWon = index;
        }

        /// <summary>Another crew member won the current step and this game hasn't shown its conversation yet.</summary>
        public static bool RemoteWon => Active && remoteWon >= 0 && remoteWon == Story.Index && Story.Mission != null
                                        && !Story.Mission.won && !Story.Mission.IsEmpty;

        // ---- advancing ----------------------------------------------------------------------------------------------

        /// <summary>Story.Advance: 'from' was left ('wasWon': through its success conversation). A guest's own advance that
        /// wasn't a won step goes to the leader.</summary>
        public static void OnAdvanced(int from, int to, bool wasWon)
        {
            if (!Active) return;
            if (remoteWon == from) remoteWon = -1;
            if (Leader || catchingUp || wasWon) return;
            GameHooks.CoopAdvanced?.Invoke(from);
        }

        /// <summary>A guest jumped steps without Advance (step 148's broker at Kalun Amir sets 151): the leader follows.</summary>
        public static void OnIndexSet(int from)
        {
            if (Guest && !catchingUp) GameHooks.CoopAdvanced?.Invoke(from);
        }

        /// <summary>The leader: a guest advanced from 'from' by itself; it follows when it is on that step and hasn't won it
        /// (a won step advances with its own conversation). 148 goes straight to 151 (the broker found).</summary>
        public static bool FollowGuestAdvance(int from, Database db)
        {
            if (!Leader || Story.Index != from || Story.Mission == null || Story.Mission.won) return false;
            if (from == 148)
            {
                Session.CampaignMission = 151;
                Session.StoryMission = StoryMission.From(StoryTable.Step(151));
                Session.StoryStepStart = Session.PlaySeconds;
                return true;
            }
            Story.Advance(db);
            return true;
        }

        // ---- the leader's State ----------------------------------------------------------------------------------

        public static State Capture()
        {
            var m = Session.StoryMission ?? new StoryMission();
            var s = new State
            {
                index = Session.CampaignMission,
                mission = Clone(m),
                counter = Session.StoryCounter,
                targets = new List<int>(Session.StoryTargets),
                invasionSystem = Session.VoidInvasionSystem,
                invasionStation = Session.VoidInvasionStation,
                crewMissions = crewMissionsIndex == Session.CampaignMission ? crewMissions : 0,
                failSerial = failSerial,
                campaign = (int)Session.Campaign,
                strength = Strength,
                splitRewards = SplitRewards,
                crewLimit = CrewLimit,
                station = Session.LastDockedStation >= 0 ? Session.LastDockedStation : Session.StationIndex,
            };
            // The kit: the leader's own ship (the parked one while a loaner is flown).
            var parked = Session.ParkedShip;
            s.kitShip = parked != null ? parked.ship : Session.ShipIndex;
            var gear = parked != null ? parked.equipment : Session.Equipment;
            foreach (var e in gear) if (e != null && !e.builtIn) s.kit.Add(e.Clone());
            return s;
        }

        public static string ToJson(State s) => JsonUtility.ToJson(s);

        public static State FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<State>(json); }
            catch (Exception e) { Debug.LogWarning("CoopCampaign: the leader's story didn't read: " + e.Message); return null; }
        }

        /// <summary>The leader, every frame: the State out when it changed.</summary>
        public static void LeaderTick()
        {
            if (!Leader) return;
            if (crewMissionsIndex != Story.Index) { crewMissionsIndex = Story.Index; crewMissions = 0; missionsCounted.Clear(); }
            string json = ToJson(Capture());
            if (json == lastPublished) return;
            lastPublished = json;
            GameHooks.CoopPublish?.Invoke(json);
        }

        /// <summary>The leader: a guest's Progress on the current step, merged into its own.</summary>
        public static void Merge(string json)
        {
            if (!Leader || string.IsNullOrEmpty(json)) return;
            Progress p;
            try { p = JsonUtility.FromJson<Progress>(json); }
            catch (Exception) { return; }
            if (p == null) return;
            Merge(p);
        }

        public static void Merge(Progress p)
        {
            if (p == null || p.index != Story.Index || Story.Mission == null) return;
            if (BitSteps(p.index)) Story.Mission.value |= p.valueOr;
            else Story.Mission.value += p.valueAdd;
            Session.StoryCounter += p.counterAdd;
            if (p.targetsDone != null)
                foreach (int t in p.targetsDone)
                    for (int i = 0; i < Session.StoryTargets.Count; i++) if (Session.StoryTargets[i] == t) Session.StoryTargets[i] = -1;
        }

        /// <summary>A freelance mission of this player succeeded (Freelance.Succeed): it counts for the crew's step 13.</summary>
        public static void FreelanceDone(long missionId)
        {
            if (!Active) return;
            if (Leader) CountCrewMission(missionId);
            else GameHooks.CoopFreelanceDone?.Invoke(missionId);
        }

        /// <summary>The leader: a crew member's freelance success (a squad mission's members count it once).</summary>
        public static void CountCrewMission(long missionId)
        {
            if (!Leader) return;
            if (crewMissionsIndex != Story.Index) { crewMissionsIndex = Story.Index; crewMissions = 0; missionsCounted.Clear(); }
            if (missionId != 0 && !missionsCounted.Add(missionId)) return;
            crewMissions++;
        }

        /// <summary>The leader: its level failed (or it died in it). The step goes back to where it can be tried again (an
        /// in-space chain's first step), and every guest follows.</summary>
        public static void LeaderFailed()
        {
            if (!Leader) return;
            Story.RepairCheckpoint();
            if (Story.Mission != null) Story.Mission.won = false;
            remoteWon = -1;
            failSerial++;
        }

        // ---- a guest applying the leader's State -------------------------------------------------------------------

        /// <summary>A guest: the leader's State arrived.</summary>
        public static void ApplyLeader(string json, Database db)
        {
            var s = FromJson(json);
            if (s == null || s.index < 0) return;
            leaderState = s;
            if (!Guest) return;
            SetSettings(s.strength, s.splitRewards);   // the leader's settings
            SetCrewLimit(s.crewLimit);
            if (appliedFailSerial < 0) appliedFailSerial = s.failSerial;
            if (s.failSerial != appliedFailSerial)
            {
                appliedFailSerial = s.failSerial;
                if (Story.Index > s.index) ForceTo(s);   // the failed chain starts again
            }
            CatchUp(s, db);
            if (Story.Index == s.index) Overwrite(s);
        }

        /// <summary>A guest, every frame: the catch-up's wait, the time ahead, its own Progress out.</summary>
        public static void GuestTick(Database db)
        {
            if (!Guest || leaderState == null) return;
            CatchUp(leaderState, db);
            if (Story.Index > leaderState.index)
            {
                if (aheadSince < 0f) aheadSince = Time.unscaledTime;
                else if (Time.unscaledTime - aheadSince > AheadSeconds) { Debug.Log($"CoopCampaign: step {Story.Index} ahead of the leader's {leaderState.index} too long"); ForceTo(leaderState); }
            }
            else aheadSince = -1f;
            if (baseline == null || baseline.index != Story.Index) { baseline = Snapshot(); return; }
            var p = Diff(baseline);
            if (p.IsEmpty) return;
            baseline = Snapshot();
            GameHooks.CoopProgress?.Invoke(JsonUtility.ToJson(p));
        }

        static void CatchUp(State s, Database db, bool now = false)
        {
            if (Story.Index >= s.index) { waitingSince = -1f; return; }
            // This game's own success conversation of the step the leader just left: it runs first (its moves, its reward).
            bool ownSuccess = Story.Index == s.index - 1 && (Story.Mission != null && Story.Mission.won || RemoteWon);
            if (ownSuccess && !now)
            {
                if (waitingSince < 0f) waitingSince = Time.unscaledTime;
                if (Time.unscaledTime - waitingSince < CatchUpWaitSeconds) return;
            }
            waitingSince = -1f;
            int from = Story.Index;
            catchingUp = true;
            try
            {
                for (int guard = 0; Story.Index < s.index && guard < 200; guard++)
                {
                    int before = Story.Index;
                    Story.Advance(db);
                    if (Story.Index <= before) break;
                }
            }
            finally { catchingUp = false; }
            remoteWon = -1;
            CatchUpSerial++;
            Debug.Log($"CoopCampaign: caught up from step {from} to {Story.Index}");
        }

        /// <summary>A guest: the leader is on step 'index' now (its level moved the crew on before its State arrived): at once
        /// to that step, so the next orbit is built as the story's.</summary>
        public static void CatchUpTo(int index, Database db)
        {
            if (!Guest || Story.Index >= index) return;
            var s = leaderState != null && leaderState.index >= index ? leaderState : new State { index = index };
            CatchUp(s, db, true);   // no waiting for this game's own conversation
        }

        /// <summary>The leader's step as it is, without Advance (a failure's checkpoint, a guest ahead for too long).</summary>
        static void ForceTo(State s)
        {
            Session.CampaignMission = s.index;
            Session.StoryMission = Clone(s.mission);
            Session.StoryMission.won = false;
            Session.StoryStepStart = Session.PlaySeconds;
            remoteWon = -1;
            aheadSince = -1f;
            Overwrite(s);
        }

        /// <summary>The leader's shared fields of the step both are on (the local mission object kept: its won flag is this
        /// game's own conversation).</summary>
        static void Overwrite(State s)
        {
            var m = Session.StoryMission;
            if (m == null) Session.StoryMission = m = new StoryMission();
            var l = s.mission ?? new StoryMission();
            m.type = l.type; m.reward = l.reward; m.station = l.station; m.value = l.value;
            m.goodsItem = l.goodsItem; m.goodsAmount = l.goodsAmount; m.visible = l.visible;
            if (l.won && !m.won) remoteWon = s.index;
            Session.StoryCounter = s.counter;
            Session.StoryTargets.Clear();
            if (s.targets != null) Session.StoryTargets.AddRange(s.targets);
            Session.VoidInvasionSystem = s.invasionSystem;
            Session.VoidInvasionStation = s.invasionStation;
            baseline = Snapshot();
        }

        static State Snapshot() => new State
        {
            index = Session.CampaignMission,
            mission = Clone(Session.StoryMission ?? new StoryMission()),
            counter = Session.StoryCounter,
            targets = new List<int>(Session.StoryTargets),
        };

        /// <summary>A guest's changes to the step's shared fields since 'from'.</summary>
        public static Progress Diff(State from)
        {
            var p = new Progress { index = Session.CampaignMission };
            var m = Session.StoryMission;
            if (from == null || m == null || from.index != p.index) return p;
            int before = from.mission != null ? from.mission.value : 0;
            if (BitSteps(p.index)) p.valueOr = m.value & ~before;
            else p.valueAdd = m.value - before;
            p.counterAdd = Session.StoryCounter - from.counter;
            for (int i = 0; i < from.targets.Count && i < Session.StoryTargets.Count; i++)
                if (from.targets[i] >= 0 && Session.StoryTargets[i] < 0) p.targetsDone.Add(from.targets[i]);
            return p;
        }

        static StoryMission Clone(StoryMission m) => new StoryMission
        {
            type = m.type, reward = m.reward, station = m.station, value = m.value,
            goodsItem = m.goodsItem, goodsAmount = m.goodsAmount, visible = m.visible, won = m.won,
        };

        // ---- starting ---------------------------------------------------------------------------------------------

        /// <summary>The host's new co-op campaign (NetGame.PrepareSession made a fresh game): the campaign's start. The main
        /// game opens with the prologue in flight (step 0, the crew's Phantoms in the Dareius belt; NetCoop's lobby gathers
        /// them first), the add-ons docked.</summary>
        public static void NewCampaign(Database db, Campaign campaign)
        {
            Session.FreePlay = false;
            Session.CompletedWorld = false;
            Session.Campaign = campaign;
            Story.StartCampaign(db, campaign);
            Session.LaunchedFromStation = false;
            Session.ArrivedByTravel = false;
        }

        /// <summary>A single-player save the co-op host may continue: one of the three campaigns, not hardcore (a death in a
        /// session ends nothing) and not a mod's campaign (the crew's story is the original's steps).</summary>
        public static bool CanHost(SaveData s) =>
            s != null && s.campaign >= 0 && s.campaign <= (int)Campaign.Supernova && !s.hardcore && string.IsNullOrEmpty(s.modCampaign);

        /// <summary>A guest's game in a co-op session before its profile (an empty one keeps this): the story from the start,
        /// the leader's own ship and loadout, then caught up to the leader's step (every step's side effects).</summary>
        public static void PrepareGuest(State s, Database db)
        {
            Session.FreePlay = false;
            Session.CompletedWorld = false;
            Story.StartNewGame(0);
            if (s == null) return;
            Session.Campaign = (Campaign)Mathf.Clamp(s.campaign, 0, 2);
            if (s.kitShip >= 0 && db.Ship(s.kitShip) != null)
            {
                Session.ShipIndex = s.kitShip;
                Session.Equipment = new List<ItemStack>();
                if (s.kit != null) foreach (var e in s.kit) if (e != null && db.Item(e.item) != null) Session.Equipment.Add(e.Clone());
            }
            if (s.station >= 0 && s.station < db.Stations.Count) Session.StationIndex = s.station;   // docked where the leader last docked
            Session.LaunchedFromStation = Session.ArrivedByTravel = false;   // (the prologue: in the belt like the leader)
            SetSettings(s.strength, s.splitRewards);
            SetCrewLimit(s.crewLimit);
            leaderState = s;
            appliedFailSerial = s.failSerial;
            CatchUp(s, db);
            if (Story.Index == s.index) Overwrite(s);
        }

        /// <summary>A co-op profile (SaveGame.ApplyProfile): the campaign's world, not the finished one.</summary>
        public static void AfterProfile(Database db)
        {
            Session.FreePlay = false;
            Session.CompletedWorld = false;
            if (Guest && leaderState != null)
            {
                appliedFailSerial = leaderState.failSerial;
                if (Story.Index > leaderState.index) ForceTo(leaderState);
                CatchUp(leaderState, db);
                if (Story.Index == leaderState.index) Overwrite(leaderState);
            }
        }
    }
}
