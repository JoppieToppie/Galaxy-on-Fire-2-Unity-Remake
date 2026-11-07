// TrafficRadio.cs
// Traffic: radio chatter, pirate events, Alice in the Void (part of the partial Traffic; its fields are in Traffic.cs).

using System;
using System.Collections.Generic;
using GoF2Remake.Data;
using GoF2Remake.Flight;
using GoF2Remake.Visuals;
using UnityEngine;

namespace GoF2Remake.World
{
    public partial class Traffic
    {
        bool RadioBlocked => LevelMissionActive != null && LevelMissionActive();
        /// <summary>A story speaker's line in the radio box (Level::createRadioMessage 0x13 / 0x1b), with its voice.</summary>
        public void QueueLine(int text, int speakerId, string voice = null)
        {
            if (RadioBlocked) return;
            chatterQueue.Enqueue(new Chatter { text = Localization.Get(text), speaker = StoryTable.SpeakerName(speakerId), speakerId = speakerId, voice = voice });
        }
        /// <summary>The generic line on screen (after the 2000 ms delay), null = none.</summary>
        public Chatter ChatterVisible => chatter != null && chatterMs >= 2000f ? chatter : null;
        /// <summary>Speaker image by race: 0 -> 64, 2 -> 65, 3 -> 21, 8 -> 9 (Pirate Boss), else 63; the name 1597 + image. The
        /// voice: Globals::getDialogueSoundId(text, Agent(race, male)) (GenericVoice).</summary>
        /// <summary>Level::createRadioMessage replaces the level's radio list (Radio::setMessages): a new line cuts the old.
        /// ImageFactory::createChar is asked for a male face every time.</summary>
        void Radio(int firstText, int lastText, int race)
        {
            if (RadioBlocked) return;
            chatterQueue.Clear();
            chatter = null;
            int image = race == 0 ? 64 : race == 2 ? 65 : race == 3 ? 21 : race == 8 ? 9 : 63;
            int text = UnityEngine.Random.Range(firstText, lastText + 1);
            const bool male = true;
            var c = new Chatter { text = Localization.Get(text), speaker = Localization.Get(1597 + image) };
            if (image == 9) c.speakerId = 9;
            else c.portrait = AgentGenerator.CreatePortrait(male, race == 0 || race == 2 || race == 3 ? race : 1);
            c.voice = GenericVoice.For(text, race, male, c.portrait);
            chatterQueue.Enqueue(c);
        }
        /// <summary>Remake multiplayer (/radio, an event's Radio node): a line in the radio box after the ones waiting (not
        /// cutting them, unlike the original's chatter), shown like them: 2 s later, for 2 s per 55 letters + 1.5 s.</summary>
        public void Say(Chatter line)
        {
            if (line == null || string.IsNullOrEmpty(line.text)) return;
            if (chatterQueue.Count < 10) chatterQueue.Enqueue(line);
        }
        /// <summary>Remake mods (Data.WingmanCrew): a hired wingman's line in the radio box, its name and face; at most one
        /// every WingmanQuietMs (a kill line only now and then: 'chance'). False = not said.</summary>
        public bool WingmanSay(string text, string name, int[] face, float chance = 1f)
        {
            if (string.IsNullOrEmpty(text) || wingmanQuietMs > 0f || chatter != null || chatterQueue.Count > 0) return false;
            if (chance < 1f && UnityEngine.Random.value >= chance) return false;
            wingmanQuietMs = WingmanQuietMs;
            Say(new Chatter { text = text, speaker = name, portrait = face });
            return true;
        }
        const float WingmanQuietMs = 12000f;
        /// <summary>The warn line: an enemy after the player within TailM behind it (looked for once a second; then quiet for
        /// TailQuietMs).</summary>
        public bool TakeEnemyOnTail()
        {
            if (tailQuietMs > 0f || tailScanMs > 0f || Player == null || !Player.Alive) return false;
            tailScanMs = 1000f;
            var p = Player.transform;
            foreach (var s in Ships)
            {
                if (s == null || s.Gone || s.IsWingman || s.Current != NpcShip.State.Fly || !s.Target.Alive || !s.Target.hostileToPlayer) continue;
                if (s.CurrentTarget != Player) continue;
                var d = s.transform.position - p.position;
                if (d.sqrMagnitude > TailM * TailM || Vector3.Dot(d.normalized, p.forward) > -0.5f) continue;
                tailQuietMs = TailQuietMs;
                return true;
            }
            return false;
        }
        const float TailM = 1500f, TailQuietMs = 30000f;
        /// <summary>The player-down line: once until the player's hull is back over half.</summary>
        public bool TakePlayerDown(float hullShare)
        {
            if (hullShare > 0.5f) { playerDownSaid = false; return false; }
            if (playerDownSaid || hullShare >= 0.2f || hullShare <= 0f) return false;
            playerDownSaid = true;
            return true;
        }
        /// <summary>Remake: the chatter stays up at least until its voice clip is over (as Radio.HoldFor).</summary>
        public void HoldChatter(float ms) { if (chatter != null) chatterDurationMs = Mathf.Max(chatterDurationMs, ms); }
        /// <summary>Radio::update: hidden 2000 ms, then lines * 2000 + 1500 ms (world time).</summary>
        void UpdateChatter(float dtMs)
        {
            if (wingmanQuietMs > 0f) wingmanQuietMs -= dtMs;
            if (tailScanMs > 0f) tailScanMs -= dtMs;
            if (tailQuietMs > 0f) tailQuietMs -= dtMs;
            if (chatter != null)
            {
                chatterMs += dtMs;
                if (chatterMs >= 2000f + chatterDurationMs) chatter = null;
                return;
            }
            if (chatterQueue.Count == 0) return;
            chatter = chatterQueue.Dequeue();
            chatterMs = 0f;
            chatterDurationMs = Mathf.Max(1, Mathf.CeilToInt(chatter.text.Length / 55f)) * 2000f + 1500f;
        }
        /// <summary>A HUD message (Hud::hudEvent), e.g. "Signature invalid".</summary>
        public void Warn(string text) => Message?.Invoke(text);
        /// <summary>Level::pirateStationAction 0xd6338: a guard woke (radio 435-437, once per level) or the outpost died
        /// (the base destroyed, the reward pending, radio 438-440, once).</summary>
        public void PirateStationAction(bool guardWoke)
        {
            if (guardWoke)
            {
                if (baseWakeRadio) return;
                baseWakeRadio = true;
                Radio(435, 437, Standing.Pirate);
                return;
            }
            if (baseDestroyedRadio || PirateBases.IndexOf(StationIndex) < 0) return;
            baseDestroyedRadio = true;
            PirateBases.Destroyed(StationIndex);
            Radio(438, 440, Standing.Pirate);
        }
        /// <summary>The event's outpost or boss destroyed (whoever did it): the pirates' "Nooooo!" for an outpost and its turrets
        /// going with it, and the bounty (outpost 10 000 + 1500 per rank, boss 4000 + 1500 per rank; x1.5 in a Dangerous
        /// system; a Pirate activity report's own bounty, set when it was reported, and the report marked done).</summary>
        public void PirateEventDone(NpcShip ship)
        {
            int rank = Mathf.Min(Session.Rank, 20);
            bool outpost = ship.Spec.pirateEvent == SpawnSpec.EventOutpost;
            if (outpost)
            {
                Radio(438, 440, Standing.Pirate);
                foreach (var s in Ships.ToArray()) if (s.Spec.pirateEvent == SpawnSpec.EventTurret) s.DestroyAsTurret();
            }
            int reward = PirateReports.Bounty(outpost, rank, ship.Spec.eventTier, false);
            int reported = PirateReports.Done(ship.Spec.sightingId);
            if (reported >= 0)
            {
                reward = reported;
                // Multiplayer: the session's board is the server's, and so is the payout: to the killer, or split across
                // the killer's squad (NetState.PirateSightingDoneRpc).
                if (GoF2Remake.Data.NetSession.Active)
                {
                    bool remote = ship.Target.killedByRemote && ship.Target.remoteKiller != ulong.MaxValue;
                    ulong killer = remote ? ship.Target.remoteKiller : GoF2Remake.Data.NetSession.LocalId;
                    SpaceHooks.PirateSightingDone?.Invoke(ship.Spec.sightingId, killer);
                    return;
                }
            }
            Session.Credits += reward;
            BountyCollected?.Invoke(reward);
        }
        /// <summary>The boss calls the player 8 s into the orbit (the Pirate Boss face, speaker 9; a reported boss by its
        /// name). Remake (the Pirate activity board): 5 s in, the system's authorities warn once of a reported outpost or boss
        /// in this system (PirateReports.TakeWarning).</summary>
        void UpdatePirateEvent(float dtMs)
        {
            if (RadioBlocked || MenuBackdrop) return;
            eventMs += dtMs;
            if (!warningChecked && eventMs >= 5000f)
            {
                warningChecked = true;
                if (!storyOrbit && PirateReports.TakeWarning(db, systemIndex) is string warning) { RaceRadio(warning, SystemRace); warningSent = true; }
            }
            if (bossCalled || eventMs < 8000f) return;
            var boss = Ships.Find(s => s.Spec.pirateEvent == SpawnSpec.EventBoss);
            if (boss == null || !boss.Target.Alive) { bossCalled = true; return; }
            bossCalled = true;
            if (!warningSent) { chatterQueue.Clear(); chatter = null; }   // after the authorities' warning, not over it
            chatterQueue.Enqueue(new Chatter
            {
                text = Localization.Extra("pirateBossCall", "This orbit is mine now, pilot. Drop your cargo and turn around, or my boys turn you into scrap!"),
                speakerId = 9, speaker = !string.IsNullOrEmpty(boss.Spec.name) ? boss.Spec.name : Localization.Get(1606),
            });
        }
        /// <summary>After the Valkyrie add-on (Status::dlc1Won), in the alien orbit before campaign 0x93: one of eight short
        /// talks between Alice and Keith (the first line after 5000 ms, the rest chained), voiced.</summary>
        void SetupVoidChatter()
        {
            if (Session.FreePlay || StationIndex != Session.VoidOrbit || !Story.Dlc1Won || Session.CampaignMission >= 0x93) return;
            int c = UnityEngine.Random.Range(0, 8), start = 0;
            for (int i = 0; i < c; i++) start += VoidTalkCounts[i];
            for (int i = 0; i < VoidTalkCounts[c] && start + i < VoidTalk.Length; i++)
            {
                var (image, text) = VoidTalk[start + i];
                QueueLine(text, image, GenericVoice.For(text));
            }
        }
    }
}
